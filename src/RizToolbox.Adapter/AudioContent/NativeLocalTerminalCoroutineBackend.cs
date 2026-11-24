using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTerminalCoroutineBackend : ITerminalCoroutineBackend
{
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile; private readonly IntPtr _module; private readonly ExactHookBroker _broker;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[NativeLocalTerminalCoroutineTracker.MaximumRecords * 2];
    private readonly IntPtr[] _enumClasses = new IntPtr[2];
    private readonly long[] _enumIdentities = new long[NativeLocalTerminalCoroutineTracker.MaximumRecords];
    private readonly bool[] _freeAttempted = new bool[NativeLocalTerminalCoroutineTracker.MaximumRecords * 2];
    private readonly ExactHookBroker.Binding?[] _bindings = new ExactHookBroker.Binding?[6];
    private IntPtr _coroutineClass;
    private bool _attempted, _ready, _detachAttempted;
    private NativeLocalTerminalCoroutineTracker _owner = null!;
    public bool CleanupUnknown { get; private set; }
    public int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null); } }
    internal NativeLocalTerminalCoroutineBackend(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile,
        IntPtr module, ExactHookBroker broker)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(main); ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(broker);
        (_api, _main, _profile, _module, _broker) = (api, main, profile, module, broker);
    }
    public void Initialize(NativeLocalTerminalCoroutineTracker owner)
    {
        _main.VerifyAccess(); if (_attempted) throw new InvalidOperationException("Terminal coroutine observation installs once."); _attempted = true; _owner = owner;
        try
        {
            if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
                || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
                throw new UnsupportedSignatureException("Terminal coroutines require the pinned Windows x64 Unity build.");
            using (var stream = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll")))
            using (var sha = SHA256.Create())
                if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
                    throw new UnsupportedSignatureException("Terminal coroutine image differs from the pinned profile.");
            var controller = Native(() => _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController"));
            _enumClasses[0] = Native(() => _api.NestedClass(controller, "<Cutout>d__112"));
            _enumClasses[1] = Native(() => _api.NestedClass(controller, "<FadeOutScoreDisplay>d__113"));
            var named = Native(() => _api.ResolveCoroutineStartHook(_module, named: true));
            var start = Native(() => _api.ResolveCoroutineStartHook(_module));
            var alias = Native(() => _api.ResolveCoroutineStartHook(_module, true)); _coroutineClass = start.ReturnClass;
            if (named.ReturnClass != _coroutineClass) throw new UnsupportedSignatureException("Named and enum Coroutine returns differ.");
            // All six transports install before activation; callback closure storage
            // and root slots already belong to the retained owner.
            _bindings[0] = _broker.BindNativeCoroutineObservation(named, NativeCoroutineKind.Start, owner.CaptureNamed, owner.CompleteStart, owner.ObserveFault);
            RequireAuthorityKnown();
            _bindings[1] = _broker.BindNativeCoroutineObservation(start, NativeCoroutineKind.Start, owner.CaptureEnumStart, owner.CompleteStart, owner.ObserveFault, alias);
            RequireAuthorityKnown();
            for (var index = 0; index < 2; ++index)
            {
                var role = (TerminalCoroutineRole)index;
                Native(() => _api.RequireInstanceFieldLayout(_enumClasses[index], "<>1__state", "System.Int32", 0x10));
                Native(() => _api.RequireInstanceFieldLayout(_enumClasses[index], "<>2__current", "System.Object", 0x18));
                Native(() => _api.RequireInstanceFieldLayout(_enumClasses[index], "<>4__this", "NormalModeInLevelController", 0x20));
                var factory = Native(() => _api.ResolveCoroutineFactoryHook(_module, controller, index == 0 ? "Cutout" : "FadeOutScoreDisplay", index == 0 ? 0x3BED10 : 0x3BED80));
                var move = Native(() => _api.ResolveExactHook("local.terminal-coroutine.move." + role, _module, _enumClasses[index], "MoveNext", Array.Empty<string>(),
                    index == 0 ? 0x3C5900 : 0x3C5A40, ExactHookAbi.InstanceBooleanReturn0));
                // The pinned explicit Reset throws, so normal consumers cannot
                // restart a naturally terminal iterator through IEnumerator.Reset.
                Native(() => _api.ResolveExactHook("local.terminal-coroutine.reset." + role, _module, _enumClasses[index], "System.Collections.IEnumerator.Reset",
                    Array.Empty<string>(), index == 0 ? 0x3C5A00 : 0x3C5C40, ExactHookAbi.InstanceVoid));
                Native(() => _api.RequireAssignable(factory.ReturnClass, _enumClasses[index]));
                _bindings[2 + index] = _broker.BindNativeCoroutineObservation(factory, NativeCoroutineKind.Factory,
                    frame => owner.CaptureFactory(role, frame), owner.CompleteFactory, owner.ObserveFault);
                RequireAuthorityKnown();
                _bindings[4 + index] = _broker.BindNativeCoroutineObservation(move, NativeCoroutineKind.MoveNext,
                    frame => owner.CaptureMove(role, frame), owner.CompleteMove, owner.ObserveFault);
                RequireAuthorityKnown();
            }
            _ready = true;
        }
        catch { CleanupUnknown = true; throw; }
    }
    public string ReadMethodName(long name)
    {
        Check();
        try { return Native(() => _api.TextWithPublication(new(name))); } catch { CleanupUnknown = true; throw; }
    }
    public void RetainEnumerator(int slot, TerminalCoroutineRole role, long enumerator)
    {
        Check(); if (slot < 0 || slot >= _enumIdentities.Length || _roots[slot] is not null) throw new InvalidOperationException("Terminal enum root slot unavailable.");
        try
        {
            _enumIdentities[slot] = enumerator; _freeAttempted[slot] = false;
            RetainPublished(slot, enumerator, _enumClasses[(int)role]);
        }
        catch { CleanupUnknown = true; throw; }
    }
    public TerminalCoroutineObservation ReadEnumerator(int slot, TerminalCoroutineRole role)
    {
        Check();
        try
        {
            var root = _roots[slot] ?? throw new InvalidOperationException("No independently rooted terminal enum.");
            var value = Native(() => root.Target);
            if (value.ToInt64() != _enumIdentities[slot] || Native(() => _api.ObjectClass(value)) != _enumClasses[(int)role])
                throw new UnsupportedSignatureException("Actual terminal enum identity or class changed.");
            var controller = Native(() => _api.ReadWithPublication<IntPtr>(value, "<>4__this")).ToInt64();
            var state = Native(() => _api.ReadWithPublication<int>(value, "<>1__state"));
            return new(value.ToInt64(), controller, state);
        }
        catch { CleanupUnknown = true; throw; }
    }
    public void RetainCoroutine(int slot, long coroutine)
    {
        Check(); var rootSlot = slot + _enumIdentities.Length;
        if (_roots[rootSlot] is not null) throw new InvalidOperationException("Terminal Coroutine root slot occupied.");
        try { _freeAttempted[rootSlot] = false; RetainPublished(rootSlot, coroutine, _coroutineClass); }
        catch { CleanupUnknown = true; throw; }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        => ReadActivityCore(true);
    private ExactHookBroker.ManagedBoundaryActivity ReadActivityCore(bool includeCleanupFailure)
    {
        _main.VerifyAccess(); long count = 0;
        foreach (var binding in _bindings)
        {
            if (_owner.ObservationAuthorityUnknown) return new(count, false);
            if (!includeCleanupFailure) _owner.RequireCancellationRetirement();
            var value = binding?.ReadActivity(); if (!includeCleanupFailure) _owner.RequireCancellationRetirement();
            if (value is not { Known: true, InFlight: >= 0 } || count > long.MaxValue - value.InFlight)
            { CleanupUnknown = true; return new(0, false); }
            if (_owner.ObservationAuthorityUnknown) return new(count, false);
            count += value.InFlight;
        }
        if (!includeCleanupFailure) _owner.RequireCancellationRetirement();
        return new(count, !_owner.ObservationAuthorityUnknown && (!includeCleanupFailure || !CleanupUnknown));
    }
    public void ReleaseEndedRoots(int slot, long enumerator, long generation)
    {
        Check();
        _owner.RequireCancellationRetirement();
        if (!_owner.IsRetiringForCleanup(slot, enumerator, generation) || slot < 0 || slot >= _enumIdentities.Length
            || _enumIdentities[slot] != enumerator || _roots[slot] is null)
            throw new InvalidOperationException("Only this exact withdrawn terminal record generation can release its roots.");
        var failures = new List<Exception>(2);
        foreach (var index in new[] { slot + _enumIdentities.Length, slot })
        {
            if (_roots[index] is not { } root) continue;
            // A free failure is local to that root. Known independent roots still
            // get their one attempt; a new owned callback/native window stops work.
            try { _owner.RequireEngineRetirementFree(slot, enumerator, generation); }
            catch (Exception error) { failures.Add(error); break; }
            _owner.RequireCancellationRetirement(); var activity = ReadActivityCore(false);
            if (_owner.CoroutineCancellationPending)
            { failures.Add(new InvalidOperationException("Cancellation arrived during terminal root cleanup activity read.")); break; }
            if (_owner.ObservationAuthorityUnknown || activity is not { Known: true, InFlight: 0 })
            { failures.Add(new InvalidOperationException("Terminal calls changed during completed-root cleanup.")); break; }
            if (_freeAttempted[index]) { failures.Add(new InvalidOperationException("Unknown terminal root free cannot retry.")); continue; }
            try { _owner.RequireEngineRetirementFree(slot, enumerator, generation); }
            catch (Exception error) { failures.Add(error); break; }
            _owner.RequireCancellationRetirement(); _freeAttempted[index] = true;
            try { root.Dispose(); _roots[index] = null; } catch (Exception error) { CleanupUnknown = true; failures.Add(error); }
            try { _owner.RequireEngineRetirementFree(slot, enumerator, generation); }
            catch (Exception error) { failures.Add(error); break; }
            if (_owner.ObservationAuthorityUnknown || _owner.CoroutineCancellationPending)
            { failures.Add(new InvalidOperationException("External launch/source authority faulted during an independent terminal root free.")); break; }
        }
        if (failures.Count != 0) { CleanupUnknown = true; throw new AggregateException("Completed terminal roots retain unconfirmed responsibility.", failures); }
        _enumIdentities[slot] = 0;
    }
    public void DetachAndReleaseUnused()
    {
        Check(); if (_detachAttempted || _roots.Any(root => root is not null) || _enumIdentities.Any(value => value != 0))
            throw new InvalidOperationException("Only unused terminal observers can detach through this path.");
        _owner.RequireCancellationRetirement();
        _detachAttempted = true;
        try
        {
            foreach (var binding in _bindings) { RequireAuthorityKnown(); _owner.RequireCancellationRetirement(); binding!.Dispose(); RequireAuthorityKnown(); _owner.RequireCancellationRetirement(); }
            var activity = ReadActivity(); _owner.RequireCancellationRetirement();
            if (activity is not { Known: true, InFlight: 0 }) throw new InvalidOperationException("Terminal native activity changed during unused detach.");
        }
        catch { CleanupUnknown = true; throw; }
    }
    private void Check()
    { _main.VerifyAccess(); RequireAuthorityKnown(); if (!_ready || _detachAttempted || CleanupUnknown) throw new InvalidOperationException("Terminal native observations unavailable or unknown."); }
    private void RetainPublished(int slot, long identity, IntPtr expected)
    {
        RequireAuthorityKnown();
        if (identity == 0) throw new InvalidOperationException("A real non-null terminal publication is required.");
        // The independent root is published before any post-allocation fault,
        // Target or metadata inspection can throw and lose acquired responsibility.
        _roots[slot] = _api.Root(new(identity)); RequireAuthorityKnown();
        var target = Native(() => _roots[slot]!.Target);
        if (target.ToInt64() != identity || Native(() => _api.ObjectClass(target)) != expected)
            throw new UnsupportedSignatureException("Terminal publication differs from its actual exact class/identity.");
    }
    private void RequireAuthorityKnown()
    { if (_owner.ObservationAuthorityUnknown) throw new InvalidOperationException("Terminal launch/source authority is unknown."); }
    private T Native<T>(Func<T> operation)
    { RequireAuthorityKnown(); var result = operation(); RequireAuthorityKnown(); return result; }
    private void Native(Action operation)
    { RequireAuthorityKnown(); operation(); RequireAuthorityKnown(); }
}
