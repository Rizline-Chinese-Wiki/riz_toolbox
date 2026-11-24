using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Diagnostic identities only. They never grant controller ownership or authorize a later native operation.</summary>
internal sealed record ControllerAwakeLedgerRow(long ReceiverIdentity, long BeforeOrder, int BeforeFrame,
    long AfterOrder, int AfterFrame, bool OriginalReturned, bool ContextFinished);
internal sealed record ControllerAwakeLedgerSnapshot(string Label, long CloneIdentity, bool ExactNormalClone,
    bool TimingVerifiable, long NativeCalls, long OriginalCalls, long SuppressedReentries, long SuppressedWrongThread,
    int WindowBefore, int WindowAfter, int CloneBefore, int CloneAfter, int InFlight,
    IReadOnlyList<ControllerAwakeLedgerRow> CloneRows, IReadOnlyList<string> Failures);

/// <summary>
/// Read-only, pinned InLevelController.Awake observation. An explicit bounded capture window begins
/// before Instantiate, when the clone identity is unknown; rooted observations can later be matched
/// against a caller-owned clone root. No skip, transform, clone, activation or global writes exist here.
/// </summary>
/// <remarks>
/// The shared broker invokes ordinary Before observers before retained capture, and other retained
/// observers may also run. This ledger proves only its observed broker boundary, not the absence of
/// other observers' mutations. The ordinary broker does not filter per-call MethodInfo aliases.
/// Native/original/reentry/wrong-thread deltas conservatively reject missed or unpaired observations.
/// </remarks>
internal sealed class NativeControllerAwakeLedger : IDisposable
{
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private readonly IMainThreadDispatcher _main;
    private readonly Func<IntPtr, bool> _matches, _normalController;
    private readonly Func<IntPtr, NativeRuntime.RootHandle> _retain;
    private readonly Func<int> _frame;
    private ExactHookBroker.Binding? _binding;
    private CaptureWindow? _window;
    private bool _disposed, _cleanupUnknown;

    private NativeControllerAwakeLedger(IMainThreadDispatcher main, Func<IntPtr, bool> matches,
        Func<IntPtr, bool> normalController, Func<IntPtr, NativeRuntime.RootHandle> retain, Func<int> frame)
        => (_main, _matches, _normalController, _retain, _frame) = (main, matches, normalController, retain, frame);

    internal static NativeControllerAwakeLedger Bind(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr gameAssemblyModule, ExactHookBroker broker, Func<int> readFrame)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(readFrame);
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || gameAssemblyModule == IntPtr.Zero
            || profile.GameAssemblySha256 != Fingerprint || profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Awake timing requires the pinned Windows x64 Unity 2022.3.62f2 build.");
        var path = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
            if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != Fingerprint)
                throw new UnsupportedSignatureException("Awake timing game image differs from the pinned profile.");
        var declaring = api.Class("Assembly-CSharp.dll", "", "InLevelController");
        var normal = api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        var target = api.ResolveExactHook("framework.local.controller.awake-timing", gameAssemblyModule,
            declaring, "Awake", Array.Empty<string>(), 0x371810, ExactHookAbi.InstanceVoid);
        return BindCore(main, broker, target, pointer => api.ExactHookReceiverMatches(pointer, declaring),
            pointer => api.ObjectClass(pointer) == normal, api.Root, readFrame);
    }

    /// <summary>Controlled transport seam; production Bind above resolves and pins the native target first.</summary>
    internal static NativeControllerAwakeLedger BindCore(IMainThreadDispatcher main, ExactHookBroker broker,
        VerifiedHookTarget target, Func<IntPtr, bool> matches, Func<IntPtr, bool> normalController,
        Func<IntPtr, NativeRuntime.RootHandle> retain, Func<int> readFrame)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(normalController); ArgumentNullException.ThrowIfNull(retain);
        ArgumentNullException.ThrowIfNull(readFrame);
        if (target.Abi != ExactHookAbi.InstanceVoid) throw new UnsupportedSignatureException("Awake ledger requires InstanceVoid.");
        var ledger = new NativeControllerAwakeLedger(main, matches, normalController, retain, readFrame);
        ledger._binding = broker.BindRetainedObservation(target, ledger.Matches, ledger.Capture, call => call.After());
        return ledger;
    }

    internal CaptureWindow BeginCaptureWindow(string label)
    {
        _main.VerifyAccess();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeControllerAwakeLedger));
        if (_cleanupUnknown) throw new InvalidOperationException("Awake receiver root cleanup is unknown; new capture is refused.");
        if (_window is not null) throw new InvalidOperationException("Only one Awake capture window can be active.");
        if (string.IsNullOrWhiteSpace(label) || label.Length > 128 || label.Any(char.IsControl))
            throw new ArgumentException("A bounded diagnostic capture label is required.", nameof(label));
        return _window = new CaptureWindow(this, label, _binding!.Describe());
    }

    private bool Matches(IntPtr receiver)
    {
        var window = _window;
        if (_disposed || window is null || window.DisposeRequested) return false;
        try { return _matches(receiver); }
        catch (Exception error) { window.Failure("Receiver filter failed: " + error.GetType().Name); return false; }
    }
    private AwakeCall Capture(IntPtr receiver)
    {
        var window = _window;
        if (_disposed || window is null || window.DisposeRequested) return new AwakeCall(null, null);
        // Every failure is contained; the broker must still call the original exactly once.
        try { return window.Before(receiver); }
        catch (Exception error) { window.Failure("Before capture failed: " + error.GetType().Name); return new AwakeCall(null, null); }
    }

    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return; _disposed = true;
        try { _window?.Dispose(); }
        finally { _binding?.Dispose(); }
    }

    internal sealed class CaptureWindow : IDisposable
    {
        private const int MaximumWindows = 32, MaximumCalls = 32, MaximumFailures = 16;
        private static readonly object ProcessGate = new();
        private static readonly HashSet<CaptureWindow> ProcessWindows = new();
        private readonly NativeControllerAwakeLedger _owner;
        private readonly ReceiverRecord?[] _records = new ReceiverRecord?[MaximumCalls];
        private readonly string?[] _failures = new string?[MaximumFailures];
        private readonly HookDiagnostics _baseline;
        private int _count, _failureCount, _inFlight;
        private long _order;
        private bool _disposeRequested, _releaseAttempted, _cleanupUnknown;
        internal string Label { get; }
        internal bool DisposeRequested => _disposeRequested;
        internal bool CleanupUnknown => _cleanupUnknown;
        internal int RootRecords { get { _owner._main.VerifyAccess(); return _records.Count(x => x?.Root is not null); } }
        internal HookDiagnostics Baseline => _baseline;
        internal CaptureWindow(NativeControllerAwakeLedger owner, string label, HookDiagnostics baseline)
        {
            (_owner, Label, _baseline) = (owner, label, baseline);
            lock (ProcessGate)
            {
                if (ProcessWindows.Count >= MaximumWindows)
                    throw new InvalidOperationException("Awake capture window responsibility capacity is exhausted.");
                ProcessWindows.Add(this);
            }
        }
        internal void Failure(string message)
        {
            if (_failureCount < MaximumFailures)
                _failures[_failureCount++] = message.Length <= 256 ? message : message[..256];
        }
        internal AwakeCall Before(IntPtr receiver)
        {
            _owner._main.VerifyAccess();
            if (_disposeRequested) return new AwakeCall(null, null);
            if (_count >= MaximumCalls)
            { Failure("Awake capture exceeded 32 calls; observation is incomplete."); return new AwakeCall(null, null); }
            // Allocate/register the record and callback before acquiring a root. Assignment to
            // the fixed record cannot allocate after native ownership has been returned.
            var record = new ReceiverRecord(); var context = new AwakeCall(this, record);
            _records[_count++] = record;
            // A testable allocator/frame reader may synchronously close this window. Reserve
            // the in-flight responsibility first so neither callback can free its new root.
            record.BeforeOrder = ++_order; _inFlight++;
            try { record.Root = _owner._retain(receiver) ?? throw new InvalidOperationException("Receiver root was null."); }
            catch (Exception error)
            { Failure("Receiver retention failed: " + error.GetType().Name); context.Dispose(); return new AwakeCall(null, null); }
            record.BeforeFrame = Frame();
            return context;
        }
        private int Frame()
        {
            try
            {
                var frame = _owner._frame();
                if (frame < 0) throw new InvalidDataException("Negative Unity frame.");
                return frame;
            }
            catch (Exception error) { Failure("Frame read failed: " + error.GetType().Name); return -1; }
        }
        internal void After(ReceiverRecord record)
        {
            try
            {
                _owner._main.VerifyAccess();
                if (record.AfterOrder != 0) { Failure("Duplicate Awake after observation."); return; }
                record.AfterOrder = ++_order; record.AfterFrame = Frame(); record.OriginalReturned = true;
                if (record.BeforeFrame >= 0 && record.AfterFrame < record.BeforeFrame)
                    Failure("Unity frame went backwards during Awake.");
            }
            catch (Exception error) { Failure("After observation failed: " + error.GetType().Name); }
        }
        internal void Finished(ReceiverRecord record)
        {
            try
            {
                _owner._main.VerifyAccess();
                if (record.ContextFinished) return;
                record.ContextFinished = true; _inFlight--;
                if (!record.OriginalReturned) Failure("Awake original did not produce a paired after observation.");
                if (_disposeRequested && _inFlight == 0) ReleaseRoots(false);
            }
            catch (Exception error) { Failure("Observation finalization failed: " + error.GetType().Name); }
        }

        /// <summary>Matches only currently rooted records against a live caller root; numeric IDs are output diagnostics.</summary>
        internal ControllerAwakeLedgerSnapshot SnapshotFor(NativeRuntime.RootHandle clone)
        {
            _owner._main.VerifyAccess(); ArgumentNullException.ThrowIfNull(clone);
            if (_disposeRequested) throw new ObjectDisposedException(nameof(CaptureWindow));
            var reasons = _failures.Take(_failureCount).Select(x => x!).ToList();
            var rows = new List<ControllerAwakeLedgerRow>(); long identity = 0; bool normal = false;
            try { var pointer = clone.Target; identity = pointer.ToInt64(); normal = _owner._normalController(pointer); }
            catch (Exception error) { reasons.Add("Clone root/class lookup failed: " + error.GetType().Name); }
            if (!normal) reasons.Add("Query root is not an exact NormalModeInLevelController.");
            foreach (var record in _records.Take(_count))
            {
                if (record?.Root is null) continue;
                try
                {
                    if (record.Root.Target.ToInt64() != identity || identity == 0) continue;
                    rows.Add(new(identity, record.BeforeOrder, record.BeforeFrame, record.AfterOrder, record.AfterFrame,
                        record.OriginalReturned, record.ContextFinished));
                }
                catch (Exception error) { reasons.Add("Captured receiver root lookup failed: " + error.GetType().Name); }
            }
            var current = _owner._binding!.Describe();
            var native = current.NativeCalls - _baseline.NativeCalls; var original = current.OriginalCalls - _baseline.OriginalCalls;
            var reentry = current.SuppressedReentries - _baseline.SuppressedReentries;
            var wrongThread = current.SuppressedWrongThread - _baseline.SuppressedWrongThread;
            var before = _records.Take(_count).Count(x => x is { BeforeOrder: > 0 });
            var after = _records.Take(_count).Count(x => x is { OriginalReturned: true });
            if (native < 0 || original < 0 || reentry != 0 || wrongThread != 0)
                reasons.Add("Broker counter regression, reentry or wrong-thread delivery makes timing unverifiable.");
            if (native != before || original != native || after != before || _inFlight != 0)
                reasons.Add("Broker counts and paired window observations differ or an invocation is still in flight.");
            return new(Label, identity, normal, reasons.Count == 0, native, original, reentry, wrongThread,
                before, after, rows.Count(x => x.BeforeOrder > 0), rows.Count(x => x.OriginalReturned), _inFlight,
                rows.AsReadOnly(), reasons.AsReadOnly());
        }
        public void Dispose()
        {
            _owner._main.VerifyAccess();
            if (_cleanupUnknown) throw new InvalidOperationException("Unknown Awake root free is retained and will not be retried.");
            if (_disposeRequested) return;
            _disposeRequested = true;
            if (ReferenceEquals(_owner._window, this)) _owner._window = null;
            // An original/ordinary callback may close the window. Its receiver root remains
            // until the broker invokes each retained context's finally, including absent After.
            if (_inFlight == 0) ReleaseRoots(true);
        }
        private void ReleaseRoots(bool throwOnFailure)
        {
            if (_releaseAttempted) return; _releaseAttempted = true;
            var failures = new List<Exception>(MaximumCalls);
            for (var i = _count - 1; i >= 0; i--)
            {
                var record = _records[i]; if (record?.Root is not { } root) continue;
                try { root.Dispose(); record.Root = null; } catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count == 0) { lock (ProcessGate) ProcessWindows.Remove(this); return; }
            _cleanupUnknown = true; _owner._cleanupUnknown = true;
            Failure("Captured receiver root free is unknown; records and capacity remain process-owned without retry.");
            if (throwOnFailure) throw new AggregateException("Awake capture roots have unknown cleanup.", failures);
        }
    }

    internal sealed class ReceiverRecord
    {
        internal NativeRuntime.RootHandle? Root;
        internal long BeforeOrder, AfterOrder;
        internal int BeforeFrame = -1, AfterFrame = -1;
        internal bool OriginalReturned, ContextFinished;
    }
    internal sealed class AwakeCall : IDisposable
    {
        private CaptureWindow? _window;
        private readonly ReceiverRecord? _record;
        internal AwakeCall(CaptureWindow? window, ReceiverRecord? record) => (_window, _record) = (window, record);
        internal void After() { if (_window is { } window && _record is { } record) window.After(record); }
        public void Dispose()
        { var window = _window; _window = null; if (window is not null && _record is { } record) window.Finished(record); }
    }
}
