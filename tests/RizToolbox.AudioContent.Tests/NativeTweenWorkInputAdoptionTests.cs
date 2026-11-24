using System.Reflection;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using static Assert;

internal static partial class NativeTweenWorkObservationTests
{
    // Calls the actual production Capture prefix over the existing native export
    // seam. Only startup metadata/binding fields are explicitly seeded. This does
    // not claim Initialize, native publication adoption or production readiness.
    private static readonly List<object> NativeInputOwners = new();
    private static void ProductionInputAdoption()
    {
        var f = new Fixture(); var native = new FakeNative(); var api = native.CreateListenerDispatchAdoptionApi();
        var main = f.Main; var listeners = NativeLocalUnityEventLedger.CaptureBackend(main, new EmptyListenerBackend(), new EmptyAuthority(), 0x7fa000000);
        var registry = new LocalRunSessionRegistry(main); var bundle = new InputBundle();
        var session = registry.RegisterPrepared(new(new("tween.input", "song"), 1, "chart"), 0x7fa000010, bundle);
        var launch = new LocalRunLaunchCommitState(main, registry, registry.BeginCommit(session), bundle);
        var profile = new AdapterBuildProfile("controlled-tween-input", "", "test", "test", "test", "test", new Dictionary<string, TypeBinding>());
        var production = new NativeLocalTweenBackend(api, main, profile, IntPtr.Zero, f.Backend.Broker, launch, listeners);
        NativeInputOwners.Add(new object[] { f, native, api, listeners, registry, bundle, launch, production });
        void Seed(string name, object value)
        {
            var field = typeof(NativeLocalTweenBackend).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Production Tween field was removed: " + name);
            Check(!field.IsInitOnly); field.SetValue(production, value);
        }
        Seed("_owner", f.Owner); Seed("_ready", true);
        var target = new VerifiedHookTarget("tween.input.controlled", new(0x7fa010000), new(701), new(901), ExactHookAbi.OperationReference1Void);
        var targets = (Dictionary<NativeTweenOperation, VerifiedHookTarget>)typeof(NativeLocalTweenBackend)
            .GetField("_targets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(production)!;
        targets.Add(NativeTweenOperation.AddTweenToWaitingList, target);
        var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Reference1,
            Self = native.Instance, Arg0 = native.OtherObject, Method = target.MethodInfo, Caller = new(999), Sequence = 1 };
        native.ListenerDispatchFailTarget = 1;
        Throws<TargetExpiredException>(() => production.Capture(NativeTweenOperation.AddTweenToWaitingList, frame, false));
        Equal(2, production.RootRecords); Equal(2, native.ListenerDispatchRootCalls);
        Equal(1, native.ListenerDispatchTargetReads); Equal(0, native.ListenerDispatchClassReads);
        Check(native.ListenerDispatchTrace.SequenceEqual(new[] { "root:" + frame.Self.ToInt64(),
            "root:" + frame.Arg0.ToInt64(), "target:" + frame.Self.ToInt64() }));
        Check(production.CleanupUnknown);
        var trace = native.ListenerDispatchTrace.ToArray();
        Throws<InvalidOperationException>(() => production.Capture(NativeTweenOperation.AddTweenToWaitingList, frame, true));
        Check(native.ListenerDispatchTrace.SequenceEqual(trace)); Equal(2, production.RootRecords);
    }
    private sealed class EmptyListenerBackend : ICutoutListenerBackend
    {
        public bool CleanupUnknown => false;
        public int RootRecords => 0;
        public void Initialize(NativeLocalUnityEventLedger owner) { }
        public NativeUnityEventGraph ReadGraph(int eventSlot) => throw new InvalidOperationException("No fake event graph is needed for adoption.");
        public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList) => throw new InvalidOperationException("No fake Prepare is needed for adoption.");
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
    }
    private sealed class EmptyAuthority : ICutoutListenerAuthority
    {
        public bool Unknown => false;
        public void VerifyBeforeCapture() { }
        public void VerifyObservation() { }
        public bool TryConfirmSourceIdle() => true;
        public void ObserveFaultFromAnyThread() { }
    }
    private sealed class InputBundle : IDisposable { public void Dispose() { } }
}
