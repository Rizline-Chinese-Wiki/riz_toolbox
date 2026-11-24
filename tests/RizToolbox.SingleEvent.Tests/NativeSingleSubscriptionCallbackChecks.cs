using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// A separate process configures an unused native operation slot over retained CLR
// originals. It executes real native Enter -> production broker Capture -> original
// -> broker Complete/Dispose -> native Exit. It neither issues FirstInstall Ready
// nor claims positive Single producer ReadCurrent/IL2CPP/effect-end verification.
internal static class NativeSingleSubscriptionCallbackChecks
{
    internal const string ModeArgument = "--single-subscription-native";
    private const uint Slot = 31;
    private const ulong SingleEpoch = 19;
    [StructLayout(LayoutKind.Sequential)] private struct Config
    { internal uint Version, Size; internal IntPtr Target, Original; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint kind, uint slot, ref Config config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint kind, uint slot, ref NativeOperationBootstrapInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SingleNative(IntPtr self, float value, IntPtr method);
    private static readonly List<object> Permanent = new();

    internal static int Run(string[] args)
    {
        try
        {
            Check(args.Length == 2, "Expected one callback mode."); var mode = args[1];
            Check(OperatingSystem.IsWindows() && IntPtr.Size == 8 && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero,
                "Requires an isolated Windows x64 process without GameAssembly.");
            var helper = Environment.GetEnvironmentVariable("RIZ_SINGLE_SUBSCRIPTION_HELPER");
            Check(helper is not null && Path.IsPathFullyQualified(helper) && File.Exists(helper), "Missing fixed helper path.");
            var hash = Hash(helper!); var fixture = new Fixture(helper!);
            switch (mode)
            {
                case "withdraw-original": WithdrawOriginal(fixture); break;
                case "withdraw-capture": WithdrawCapture(fixture); break;
                case "nested-foreign": NestedForeign(fixture); break;
                case "two-runs": TwoRuns(fixture); break;
                case "observer-fault": ObserverFault(fixture); break;
                case "completion-fault": CompletionFault(fixture, false); break;
                case "context-fault": CompletionFault(fixture, true); break;
                case "collected-run": CollectedRun(fixture); break;
                default: throw new InvalidOperationException("Unknown native subscription mode.");
            }
            Check(fixture.Bridge.ReadActivity() is { Known: true, InFlight: 0 } && fixture.Engine.Calls == 0,
                "Native transport did not drain or an installation was attempted.");
            var info = fixture.Information();
            Check(info.Entered == info.Exited && info.Entered == (ulong)fixture.Originals && info.InFlight == 0
                && info.EnterFaults == 0 && info.ExitFaults == 0 && info.Attached == 1 && fixture.Factories == 1,
                "Actual native original/callback pairing changed.");
            Check(Hash(helper!) == hash && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero,
                "A fixed native input changed or game code was loaded.");
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "single-subscription-native-operation-callbacks", mode,
                passed = true, actualNativeOriginals = fixture.Originals, actualManagedCallbacks = fixture.Callbacks,
                actualOperationCaptureAndExitVerified = true, actualSingleDeliveryReadCurrentVerified = false,
                privateScalarParentAssociationVerified = fixture.ParentAssociations > 0,
                parentAssociationViaPrivateReflection = true, productionFirstInstallVerified = false,
                actualIl2CppMetadataVerified = false, effectsCompletionVerified = false,
                nativeObserverAttached = info.Attached, nativeInFlight = info.InFlight, gameExecuted = false,
                installed = false, helperSha256 = hash }));
            GC.KeepAlive(fixture); return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error); return 1; }
    }
    private sealed record Record(NativeOperationFrame Parent, NativeTweenSingleParentKey Key, bool Owned)
    {
        internal NativeTweenSingleEventFrame Frame => new() { Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Mid,
            Epoch = Key.Epoch, ParentEpoch = Key.ParentEpoch, ParentSequence = Key.Sequence, ParentManagedToken = Key.ManagedToken,
            Event = Key.Event, ParentMethod = Key.Method, ParentCaller = Key.Caller, ValueBits = Key.ValueBits, ThreadId = Key.Thread };
    }
    private sealed class Counters
    { internal int Captures, Observed, Completed, Disposed, Faults; internal bool ThrowObserve, ThrowComplete, ThrowDispose; }
    private sealed class Context : IDisposable
    {
        internal readonly Counters Owner;
        internal Context(Counters owner) => Owner = owner;
        internal void Complete(NativeOperationExitFlags flags)
        { Check(flags == NativeOperationExitFlags.Returned, "Unexpected original outcome."); ++Owner.Completed;
            if (Owner.ThrowComplete) throw new IOException("controlled completion fault"); }
        public void Dispose() { ++Owner.Disposed; if (Owner.ThrowDispose) throw new IOException("controlled context fault"); }
    }
    private sealed class Attachment
    {
        private readonly Fixture _fixture;
        internal readonly Counters State;
        internal NativeTweenSingleEventSubscriptions<Record>.Subscription Subscription = null!;
        internal ExactHookBroker.Binding Binding = null!;
        internal Attachment(Fixture fixture, Counters state) => (_fixture, State) = (fixture, state);
        internal IDisposable? Capture(NativeOperationFrame frame)
        {
            ++_fixture.Callbacks;
            var physical = Binding.CaptureSingleSubscriberParent(frame, SingleEpoch);
            var record = new Record(frame, physical.Key, frame.Self == Fixture.Owned);
            if (!record.Owned) { _fixture.Pending = record; return null; }
            var loan = _fixture.Hub.Begin(Subscription, record.Key);
            try
            {
                ++State.Captures;
                var scope = new NativeTweenSingleEventScope<Record>(new Context(State), frame, loan);
                _fixture.OnCapture?.Invoke(this, physical, record);
                physical.VerifyCurrent(); _fixture.Pending = record; return scope;
            }
            catch { loan.CaptureFault("controlled Capture failed after actual enrollment"); throw; }
        }
        internal void Complete(IDisposable context, NativeOperationFrame frame, NativeOperationExitFlags flags)
        {
            ++_fixture.Callbacks;
            ((NativeTweenSingleEventScope<Record>)context).Complete(frame, flags,
                (inner, _, actual) => ((Context)inner).Complete(actual));
        }
        internal void Observe(Record record)
        {
            ++_fixture.Callbacks;
            // This invokes the private production scalar association core only. The
            // public object-reading path still additionally requires Bridge.Delivery.
            var context = _fixture.Associate(Binding, record.Frame) as NativeTweenSingleEventScope<Record>;
            Check(context?.Inner is Context inner && ReferenceEquals(inner.Owner, State) && record.Owned,
                "Selected callback lost its exact actual broker context.");
            Check(record.Parent.ScalarBits == record.Key.ValueBits && record.Key.ValueBits == Fixture.Bits,
                "Actual native Single raw bits changed in the captured parent.");
            ++State.Observed;
            if (State.ThrowObserve) throw new IOException("controlled selected observer fault");
        }
        internal void Fault(string reason)
        { ++_fixture.Callbacks; ++State.Faults; Binding.SingleEventFault(reason); }
        internal void Withdraw() { Subscription.Dispose(); Binding.Dispose(); }
    }
    private sealed class Fixture
    {
        internal static readonly IntPtr Owned = new(0x123450), Foreign = new(0x123460), Method = new(0x234560);
        internal const uint Bits = 0x7fc12345;
        internal readonly NativeTweenSingleEventSubscriptions<Record> Hub = new();
        internal readonly NeverInstall Engine = new();
        internal NativeBootstrappedOperationBridge Bridge = null!;
        internal int Originals, Callbacks, ParentAssociations, Factories;
        internal Record? Pending;
        internal Action<Record?>? OnOriginal;
        internal Action<Attachment, ExactHookBroker.SingleCaptureAuthority, Record>? OnCapture;
        private readonly IntPtr _helper;
        private readonly string _helperPath;
        private readonly InfoNative _read;
        private readonly SingleNative _original, _targetMarker, _entry;
        private readonly ExactHookBroker _broker;
        private readonly Logger _logger = new();
        private readonly NativeOperationChannelIdentity _identity;
        private readonly VerifiedHookTarget _target;
        private static readonly MethodInfo Association = typeof(ExactHookBroker.Binding).GetMethod("CurrentSingleContext", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Private scalar parent association core changed.");
        internal Fixture(string helperPath)
        {
            _helperPath = helperPath; _helper = NativeLibrary.Load(helperPath); Permanent.Add(_helper);
            _read = Export<InfoNative>(_helper, "RizOperationBootstrap_GetInfo");
            var before = NewInfo(); Check(_read((uint)NativeOperationKind.Single1, Slot, ref before) == 2 && before.Epoch == 0,
                "The child process native slot is not fresh.");
            _original = Original; _targetMarker = (_, _, _) => throw new InvalidOperationException("Identity marker must never execute.");
            Permanent.Add(_original); Permanent.Add(_targetMarker);
            var targetAddress = Marshal.GetFunctionPointerForDelegate(_targetMarker); var originalAddress = Marshal.GetFunctionPointerForDelegate(_original);
            var config = new Config { Version = 1, Size = 24, Target = targetAddress, Original = originalAddress };
            Check(Export<ConfigureNative>(_helper, "RizOperationBootstrap_Configure")((uint)NativeOperationKind.Single1, Slot, ref config) == 0,
                "Controlled unused native channel configuration failed.");
            var configured = Information();
            _identity = new(NativeOperationKind.Single1, Slot, targetAddress, originalAddress, configured.Entry, configured.Epoch);
            Check(NativeBootstrappedOperationBridge.MatchesInformation(configured, _identity, false), "Configured native identity differs.");
            _target = new("single.subscription.owned-clr-original", targetAddress, Method, new(0x345670), ExactHookAbi.OperationSingle1Void);
            _entry = Marshal.GetDelegateForFunctionPointer<SingleNative>(_identity.Entry);
            _broker = new(Engine, new Main(), _logger);
        }
        internal Attachment Attach(Counters state)
        {
            var attachment = new Attachment(this, state);
            attachment.Subscription = Hub.Subscribe(attachment.Observe, attachment.Fault);
            attachment.Binding = _broker.BindConfiguredNativeOperationObservation(_target, _identity,
                () => { ++Factories; return Bridge = NativeBootstrappedOperationBridge.AdoptBootstrap(_helperPath, _identity, _logger); },
                attachment.Capture, attachment.Complete, (_, _) => ++state.Faults);
            return attachment;
        }
        internal IDisposable? Associate(ExactHookBroker.Binding binding, NativeTweenSingleEventFrame frame)
        { ++ParentAssociations; return (IDisposable?)Association.Invoke(binding, new object[] { frame }); }
        internal void Invoke(IntPtr? self = null) => _entry(self ?? Owned, BitConverter.Int32BitsToSingle(unchecked((int)Bits)), Method);
        private void Original(IntPtr self, float value, IntPtr method)
        {
            // Never let a managed exception escape a reverse P/Invoke original.
            try
            {
                ++Originals; Check(method == Method && unchecked((uint)BitConverter.SingleToInt32Bits(value)) == Bits,
                    "The real native original arguments changed.");
                var record = Pending; Pending = null;
                if (OnOriginal is { } observe) observe(record);
                else if (record is not null) Check(Hub.Dispatch(record.Key, record) == 0, "Default dispatch failed.");
            }
            catch (Exception error) { OriginalError ??= error; }
        }
        internal Exception? OriginalError;
        internal void CheckOriginal() { if (OriginalError is { } error) throw new InvalidOperationException("Original callback assertion failed.", error); }
        internal NativeOperationBootstrapInformation Information()
        { var info = NewInfo(); Check(_read((uint)NativeOperationKind.Single1, Slot, ref info) == 0, "Actual native information unavailable."); return info; }
    }
    private static void WithdrawOriginal(Fixture f)
    {
        var state = new Counters(); var attachment = f.Attach(state); Record last = null!;
        ExactHookBroker.SingleCaptureAuthority physical = null!;
        f.OnCapture = (_, actual, _) => physical = actual;
        f.OnOriginal = record => {
            Check(record is { Owned: true }, "No owned capture."); last = record!;
            attachment.Withdraw(); Check(!attachment.Subscription.IsDrained, "Borrowed parent was erased on withdrawal.");
            Check(f.Hub.Dispatch(last.Key, last) == 0 && f.Hub.Dispatch(last.Key, last) == 0, "Frozen callbacks were lost.");
        };
        f.Invoke(); f.CheckOriginal(); Throws(physical.VerifyCurrent);
        Check(state is { Captures: 1, Observed: 2, Completed: 1, Disposed: 1, Faults: 0 } && attachment.Subscription.IsDrained,
            "The withdrawn actual capture did not complete once.");
        f.OnOriginal = record => { Check(record is null, "A closed sink captured another parent."); Check(f.Hub.Dispatch(last.Key, last) == 0, "Late callback failed."); };
        f.Invoke(); f.CheckOriginal(); Check(state.Observed == 2 && f.Originals == 2 && attachment.Binding.ReadActivity().Known, "Retired run was read again.");
    }
    private static void WithdrawCapture(Fixture f)
    {
        var state = new Counters(); var attachment = f.Attach(state); var confirmed = false;
        f.OnCapture = (actual, physical, record) => {
            actual.Withdraw(); physical.VerifyCurrent();
            Throws(() => f.Hub.Begin(actual.Subscription, record.Key with { Sequence = record.Key.Sequence + 1 }));
            f.Invoke(Fixture.Foreign); f.CheckOriginal(); physical.VerifyCurrent(); confirmed = true;
        };
        f.Invoke(); f.CheckOriginal();
        Check(confirmed && f.Originals == 2 && state is { Captures: 1, Observed: 1, Completed: 1, Disposed: 1, Faults: 0 }
            && attachment.Subscription.IsDrained, "Actual Capture withdrawal revoked an already borrowed parent.");
    }
    private static void NestedForeign(Fixture f)
    {
        var state = new Counters(); var attachment = f.Attach(state); Record? outer = null; var depth = 0; var foreignMasked = false; var nestedMasked = false;
        f.OnOriginal = record => {
            Check(record is not null, "Expected actual captured callback record.");
            if (!record!.Owned) {
                Check(f.Associate(attachment.Binding, outer!.Frame) is null && f.Hub.Dispatch(record.Key, record) == 0,
                    "Foreign direct parent borrowed an owned ancestor."); foreignMasked = true; return;
            }
            if (depth++ == 0) {
                outer = record; Check(f.Hub.Dispatch(record.Key, record) == 0, "Outer callback failed.");
                f.Invoke(Fixture.Foreign); f.Invoke();
                Check(f.Associate(attachment.Binding, record.Frame) is not null, "Outer context did not restore after nested Exit.");
                Check(f.Hub.Dispatch(record.Key, record) == 0, "Restored outer callback failed.");
            } else {
                Check(f.Associate(attachment.Binding, outer!.Frame) is null, "Owned nested parent inherited ancestor authority."); nestedMasked = true;
                Check(f.Hub.Dispatch(record.Key, record) == 0, "Nested callback failed.");
            }
        };
        f.Invoke(); f.CheckOriginal(); attachment.Withdraw();
        Check(foreignMasked && nestedMasked && f.Originals == 3 && state is { Captures: 2, Observed: 3, Completed: 2, Disposed: 2, Faults: 0 }
            && attachment.Subscription.IsDrained, "Nested/foreign physical responsibility did not restore exactly.");
    }
    private static void TwoRuns(Fixture f)
    {
        var old = new Counters(); var first = f.Attach(old); Record previous = null!;
        f.OnOriginal = record => { previous = record!; Check(f.Hub.Dispatch(record!.Key, record) == 0, "First run callback failed."); };
        f.Invoke(); f.CheckOriginal(); first.Withdraw();
        var next = new Counters(); var second = f.Attach(next);
        f.OnOriginal = record => {
            Check(record is not null && record.Key.Sequence != previous.Key.Sequence && record.Key.ManagedToken != previous.Key.ManagedToken,
                "Two actual native runs reused a parent generation.");
            Check(f.Hub.Dispatch(previous.Key, previous) == 0 && old.Observed == 1, "A later run read old callback state.");
            Check(f.Hub.Dispatch(record!.Key, record) == 0, "Second run callback failed.");
        };
        f.Invoke(); f.CheckOriginal(); second.Withdraw();
        Check(old is { Captures: 1, Observed: 1, Completed: 1, Disposed: 1, Faults: 0 }
            && next is { Captures: 1, Observed: 1, Completed: 1, Disposed: 1, Faults: 0 }
            && first.Subscription.IsDrained && second.Subscription.IsDrained && f.Factories == 1,
            "Two runs did not share exactly one permanent observer with separate cleanup snapshots.");
    }
    private static void ObserverFault(Fixture f)
    {
        var state = new Counters { ThrowObserve = true }; var attachment = f.Attach(state);
        f.OnOriginal = record => { attachment.Withdraw(); Check(f.Hub.Dispatch(record!.Key, record) == 1, "Selected fault disappeared after withdrawal."); };
        f.Invoke(); f.CheckOriginal();
        Check(state is { Captures: 1, Observed: 1, Completed: 1, Disposed: 1 } && state.Faults > 0
            && !attachment.Subscription.IsDrained && !attachment.Subscription.ReadActivity().Known && !attachment.Binding.ReadActivity().Known,
            "Selected callback failure erased actual broker/subscription fault responsibility.");
    }
    private static void CompletionFault(Fixture f, bool disposal)
    {
        var state = new Counters { ThrowComplete = !disposal, ThrowDispose = disposal }; var attachment = f.Attach(state);
        f.Invoke(); f.CheckOriginal(); attachment.Withdraw();
        Check(state is { Captures: 1, Observed: 1, Completed: 1, Disposed: 1 } && state.Faults > 0
            && !attachment.Subscription.IsDrained && !attachment.Subscription.ReadActivity().Known && !attachment.Binding.ReadActivity().Known,
            "Consumer completion/cleanup failure was retried or reported drained.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference FinishCollectable(Fixture f)
    {
        var state = new Counters(); var attachment = f.Attach(state); f.Invoke(); f.CheckOriginal(); attachment.Withdraw();
        Check(attachment.Subscription.IsDrained && state is { Captures: 1, Observed: 1, Completed: 1, Disposed: 1 }, "Collection precondition failed.");
        return new(state);
    }
    private static void CollectedRun(Fixture f)
    {
        var run = FinishCollectable(f);
        for (var i = 0; i < 3; ++i) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Check(!run.IsAlive, "The permanent native bridge/broker/hub anchored an ended run.");
        f.Invoke(); f.CheckOriginal(); Check(f.Originals == 2, "The permanent native original disappeared after collection.");
        // This mode still exercises the production scalar core in the first callback.
    }
    private static NativeOperationBootstrapInformation NewInfo() => new() { Version = 1, Size = 96 };
    private static T Export<T>(IntPtr library, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Check(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
    private static void Throws(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("Expected actual authority rejection."); }
    private sealed class Main : IMainThreadDispatcher
    { private readonly int _thread = Environment.CurrentManagedThreadId; public bool IsMainThread => Environment.CurrentManagedThreadId == _thread;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Worker."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
    internal sealed class NeverInstall : INativeDetourEngine
    { internal int Calls; public Delegate Install(VerifiedHookTarget target, Delegate replacement) { ++Calls; throw new NotSupportedException(); } }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
