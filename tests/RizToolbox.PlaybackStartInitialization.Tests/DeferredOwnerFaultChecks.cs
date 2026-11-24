using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    // Hold the real externally dispatched notification objects. Native fault
    // marking, original execution and owner health polling are unchanged.
    private sealed class DeferredPhysicalFaults : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<(object Fault, Action Deliver)> _pending = new();
        private readonly ManualResetEventSlim _captured = new();
        private bool _released;
        internal object[] Captured { get { lock (_gate) return _pending.Select(value => value.Fault).ToArray(); } }
        internal int DeliveryThread;

        internal void Hold(object fault, Action deliver)
        {
            lock (_gate)
            {
                if (!_released)
                {
                    _pending.Add((fault, deliver));
                    if (_pending.Any(value => value.Fault is NativePlaybackSourceFault)
                        && _pending.Any(value => value.Fault is NativePlaybackSingleReturnFault)) _captured.Set();
                    return;
                }
            }
            deliver();
        }
        internal bool WaitForBoth() => _captured.Wait(TimeSpan.FromSeconds(10));
        internal void Release()
        {
            (object Fault, Action Deliver)[] pending;
            lock (_gate)
            {
                if (_released) return;
                _released = true; pending = _pending.ToArray();
            }
            // Preserve external worker delivery. The test owner only chooses
            // when it is allowed; it never invokes a Clock lock on that worker.
            var delivered = Task.Run(() =>
            {
                DeliveryThread = Environment.CurrentManagedThreadId;
                foreach (var value in pending) value.Deliver();
            });
            if (!delivered.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("Deferred physical delivery did not complete.");
            delivered.GetAwaiter().GetResult();
        }
        public void Dispose() { Release(); _captured.Dispose(); }
    }

    private static void SetDeferredHostField<T>(NativePlaybackStockRateHost host, string name, T value)
        => typeof(NativePlaybackStockRateHost).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, value);

    private static int RunDeferredOwnerFault(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && mode is "ctor-owner-fault-deferred" or "ctor-host-fault-deferred"
                && Hash(arguments[1]) == PlaybackSourceGroupTargets.ConstructorFixtureSha256,
                "Deferred owner maintenance requires the exact actual thirteen-member constructor fixture.");
            using var notifications = new DeferredPhysicalFaults();
            ConcurrentQueue<object>? hostFaults = null;
            var hostMode = mode == "ctor-host-fault-deferred";
            using var native = new ConstructorContext(arguments, faultDelivery: (fault, deliver) =>
                notifications.Hold(fault, hostMode ? () => hostFaults!.Enqueue(fault) : deliver));
            var sdk = new ConstructorSdk(native);
            var sdkDisposed = false;
            NativePlaybackStockRateHost? host = null;
            if (hostMode)
            {
                // Maintenance-only host wiring to the real owned group and
                // session. This does not exercise or relax formal Fresh admission.
                host = new(native.Api.GameplayApi, native.Main, native.Log, native.State,
                    new AdapterBuildProfile("owned-maintenance", new string('0', 64), "owned", "owned", "owned", "owned",
                        new Dictionary<string, TypeBinding>()), arguments[4]);
                SetDeferredHostField(host, "_group", native.Group);
                SetDeferredHostField(host, "_route", native.Router);
                SetDeferredHostField(host, "_session", native.Session);
                SetDeferredHostField(host, "_logicalHost", sdk.Provider);
                SetDeferredHostField(host, "_run", native.Run);
                SetDeferredHostField(host, "<Stage>k__BackingField", NativePlaybackStockHostStage.ReadyExperimental);
                SetDeferredHostField(host, "_nextProgressPump", long.MaxValue);
                hostFaults = Private<ConcurrentQueue<object>>(host, "_faults");
                Check(host.FrameworkReady && host.PublishedObservationSource is not null && hostFaults.IsEmpty,
                    "Controlled host maintenance did not begin with the same armed route and a future progress deadline.");
            }
            try
            {
                var clock = native.Clock;
                var initialSdkIdentity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
                Check(initialSdkIdentity is { NativeDescriptor: not null },
                    "Deferred notification setup did not publish the actual SDK native source descriptor.");
                var source = native.Api.GameplaySource;
                var player = native.Api.GameplayPlayer;
                var method = native.Targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor).MethodInfo;
                var groupOwner = Private<object>(native.Group, "_owner");
                var hub = Private<NativePlaybackStockSourceHub>(groupOwner, "_hub");
                var readSource = Export<ConstructorWorkerReadSource>(hub.NativeLibraryHandle, "RtPlaybackSourceReadOwner");
                var initial = new ConstructorWorkerSourceInfo();
                Check(readSource(hub.NativeOwner, ref initial) == 1 && initial.Flags == 1 && initial.InFlight == 0,
                    "The retained actual owner did not start healthy and armed.");
                var pitchCalls = Marshal.ReadInt32(source, 20);
                var originalCalls = native.Read(0); var originalReturns = native.Read(1);
                var observations = native.Observations.Count;
                var clockReads = native.ClockReads;
                var ownerThread = Environment.CurrentManagedThreadId;
                uint workerThread = 0; int boundaries = 0, validBoundaries = 0;
                ConstructorBoundary boundary = (self, reference, incoming, at) =>
                {
                    var actual = new ConstructorWorkerSourceInfo();
                    ++boundaries; workerThread = ConstructorWorkerThreadId();
                    if (self == player && reference == source && incoming == method && at is 1 or 2
                        && Marshal.ReadIntPtr(player, 16) == source
                        && readSource(hub.NativeOwner, ref actual) == 1
                        && (actual.Flags & 4) != 0 && (actual.Flags & 1) == 0 && actual.InFlight == 1
                        && actual.Entered == initial.Entered + 1 && actual.Exited == initial.Exited
                        && actual.Source == initial.Source + 1 && actual.Writer == initial.Writer
                        && actual.Scheduling == initial.Scheduling && actual.Faults > initial.Faults) ++validBoundaries;
                };
                native.Configure(0, boundary);
                var worker = Task.Run(() => native.Ctor(player, source, method, out var code) == 1 && code == 0);
                Check(worker.Wait(TimeSpan.FromSeconds(10)) && worker.Result,
                    "The relevant worker's original constructor did not return normally.");
                native.Configure(0, null); GC.KeepAlive(boundary);
                var exited = new ConstructorWorkerSourceInfo();
                Check(boundaries == 2 && validBoundaries == 2 && workerThread != ConstructorWorkerThreadId()
                    && native.Read(0) == originalCalls + 1 && native.Read(1) == originalReturns + 1
                    && readSource(hub.NativeOwner, ref exited) == 1 && (exited.Flags & 4) != 0
                    && exited.InFlight == 0 && exited.Entered == initial.Entered + 1 && exited.Exited == initial.Exited + 1
                    && exited.Source == initial.Source + 1 && exited.Writer == initial.Writer && exited.Scheduling == initial.Scheduling
                    && native.Observations.Count == observations && native.ConstructorBefores == 0
                    && native.ClockReads == clockReads && native.CallbackFailure is null,
                    "Worker SuppressObserver lost pre-original Unknown, original store/exit, real fences or zero managed source callbacks.");
                Check(clock.Capture().Phase == NativePlaybackPhase.Paused && !native.Router.PhysicalFaultPending
                    && native.Router.FirstPhysicalFault is null && (hostFaults?.IsEmpty ?? true),
                    "The owner must still hold its old logical snapshot before the first maintenance poll.");

                if (hostMode) host!.PumpCompleted(); else native.Router.PumpCompleted();
                var firstPhase = clock.Capture().Phase;
                var firstFailure = Private<string?>(native.Router, "_physicalFailure");
                // Gate delivery cannot populate either managed queue. This
                // assertion is deterministic even if ThreadPool dispatch is late.
                Console.WriteLine(JsonSerializer.Serialize(new { diagnostic = "first-owner-pump-with-external-notifications-held",
                    firstPhase = firstPhase.ToString(), firstFailure, hostStage = host?.Stage.ToString(),
                    hostReady = host?.FrameworkReady, queued = native.Router.PhysicalFaultPending,
                    nativeUnknown = (exited.Flags & 4) != 0, sourcePhysicalExited = exited.InFlight == 0 }));
                Check(firstPhase == NativePlaybackPhase.Withdrawn && firstFailure is not null
                    && !native.Router.PhysicalFaultPending && native.Router.FirstPhysicalFault is null
                    && (hostFaults?.IsEmpty ?? true)
                    && (!hostMode || !host!.FrameworkReady && host.PublishedObservationSource is null
                        && host.Stage == NativePlaybackStockHostStage.Draining),
                    "The first owner pump waited for deferred external notification instead of revoking actual native Unknown.");
                Check(notifications.WaitForBoth() && notifications.Captured.Length == 2,
                    "The exact source/GetTime external notifications were not captured once each.");
                var withdrawal = clock.Capture();
                var providerSupported = sdk.Provider.Probe().Supported;
                var oldLeaseExecuted = sdk.Lease.Execute(sdk.Request(2)).Success;
                var firstSdkClock = Need(sdk.ClockView.Read());
                var secondSdkClock = Need(sdk.OtherClock.Read());
                var currentSdkIdentity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
                // A generic stock observation may still report game time. It
                // must not retain the withdrawn native descriptor or speed authority.
                Check(!providerSupported && !oldLeaseExecuted
                    && currentSdkIdentity != initialSdkIdentity && currentSdkIdentity?.NativeDescriptor is null
                    && firstSdkClock.ControllerGeneration == secondSdkClock.ControllerGeneration
                    && Marshal.ReadInt32(source, 20) == pitchCalls,
                    "Deferred physical delivery retained the old SDK native descriptor, speed lease or native pitch authority.");
                const float rawTime = 17.25f;
                Marshal.Copy(new[] { (double)rawTime }, 0, IntPtr.Add(source, 24), 1);
                var getTimeCalls = Private<NativePlaybackSingleReturnBridge>(Private<object>(native.Group.GetTimeLease, "_owner"), "_bridge").ReadHealth().OriginalCalls;
                Check(native.Time(player, native.Targets.GetTime.MethodInfo) == rawTime
                    && Private<NativePlaybackSingleReturnBridge>(Private<object>(native.Group.GetTimeLease, "_owner"), "_bridge").ReadHealth().OriginalCalls == getTimeCalls + 1
                    && native.ClockReads == clockReads,
                    "After synchronous withdrawal GetTime did not preserve one actual original Single return.");
                notifications.Release();
                Check(notifications.DeliveryThread != ownerThread, "Deferred notifications were delivered on the test owner.");
                if (hostMode) host!.PumpCompleted();
                native.Router.PumpCompleted();
                Check(clock.Capture() == withdrawal && !sdk.Provider.Probe().Supported
                    && Marshal.ReadInt32(source, 20) == pitchCalls
                    && notifications.Captured.Any(fault => ReferenceEquals(fault, native.Router.FirstPhysicalFault))
                    && !native.Router.PhysicalFaultPending && (hostFaults?.IsEmpty ?? true),
                    "Delayed exact notifications revived the session, rewrote pitch or replaced first delivered fault identity.");
                sdk.Dispose(); sdkDisposed = true; native.Router.PumpCompleted(); host?.Dispose();
                Check(!native.Router.RootsReleased && native.Api.ActiveRoots == 7
                    && native.Broker.ReadActualPlaybackDobbyCallCounts() == (13, 13) && native.UniqueFinalClock,
                    "Synchronous withdrawal or delayed disposal dropped the seven actual Unknown roots or replaced the physical group.");
                Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                    firstPhase = firstPhase.ToString(), firstOwnerPumpRevoked = true, delayedNotifications = notifications.Captured.Length,
                    ownerThread, workerThread, deliveryThread = notifications.DeliveryThread, sourcePhysicalExited = true,
                    constructorOriginalCalls = native.Read(0) - originalCalls, constructorOriginalReturns = native.Read(1) - originalReturns,
                    managedConstructorBefores = native.ConstructorBefores, unchangedWriterFence = exited.Writer == initial.Writer,
                    unchangedSchedulingFence = exited.Scheduling == initial.Scheduling, retainedRoots = native.Api.ActiveRoots,
                    sdkNativeDescriptorRevoked = currentSdkIdentity?.NativeDescriptor is null,
                    genericGameTimeAvailable = firstSdkClock.GameTime.Available,
                    hostMaintenanceOnly = hostMode, formalFreshSourceScopeCovered = false, gameExecuted = false, productionAdmission = false }));
                return 0;
            }
            finally
            {
                notifications.Release();
                if (!sdkDisposed) sdk.Dispose();
                host?.Dispose();
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
    }
}
