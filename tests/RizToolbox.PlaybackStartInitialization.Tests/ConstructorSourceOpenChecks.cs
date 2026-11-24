using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    private static int FinishFirstConstructorSourceWatch(
        FakeNative api, IntPtr native, PlaybackSourceGroupTargets targets, PlaybackSourceGroupLease group,
        NativePlaybackStockClockRouter router, NativePlaybackStockClockRouter.Session session,
        NativePlaybackClock finalClock, IntPtr template, List<NativePlaybackSourceObservation> observations,
        ReadSharedConstructor readShared, GetTime getTime, ExactHookBroker broker, GameEventState state,
        MainThreadDispatcher main, Logger log,
        Action<Action<NativePlaybackSourceObservation>?> setConstructorBefore, Func<string?> callbackFailure)
    {
        // The caller reached this point through host-only Capture, actual
        // AL1/AL0, InitV2 completion, and the first later Pause. The allocation
        // template has never owned a Clock or a session.
        var currentPlayer = api.GameplayPlayer;
        var currentSource = api.GameplaySource;
        var physical = (group.Epoch, group.GetTimeLease.EntryAddress, group.GetTimeLease.OriginalAddress,
            group.SetPitchLease.EntryAddress);
        Check(currentPlayer != template && ReferenceEquals(finalClock, session.Clock)
            && finalClock.Capture().Binding.Player == currentPlayer.ToInt64()
            && group.GetTimeLease.ReadCurrent().Active && readShared(0) == 2 && readShared(1) == 2
            && getTime(currentPlayer, targets.GetTime.MethodInfo) == (float)finalClock.Capture().MediaAnchorSeconds
            && observations.All(value => value.Role != NativePlaybackSourceRole.PlayerConstructor),
            "First source-open did not reach a unique actual player after host-only capture.");

        using var provider = router.AdoptControlledRateHost();
        using var journal = new RunModificationJournal(main, log, state);
        var module = new GameplayModule(api.GameplayApi, main, () => state.LifecycleSnapshot(),
            journal.Record, state.MatchesCurrentGameplayRun, provider);
        Check(module.Probe().Ready && provider.Probe().Supported, "First constructor watch did not reach the formal Module.");
        var service = Private<GameplayService>(module, "_service");
        var capabilities = new CapabilityRegistry(new("first-constructor-source", "fixture", "synthetic", "offline"));
        var registration = new ModuleRegistration(capabilities, module.Manifest);
        module.Register(registration); registration.Complete();
        capabilities.RegisterScoped<IRunModificationService>(RunModificationCapabilityIds.Read, journal.ForPlugin);
        using var plugins = new PluginHost(capabilities, main, new EventHub(log), log);
        var firstPlugin = new RouterPlugin("first-ctor-one");
        var secondPlugin = new RouterPlugin("first-ctor-two");
        Check(plugins.Register(firstPlugin).Success && plugins.Register(secondPlugin).Success
            && plugins.Enable(firstPlugin.Manifest.Id).Success && plugins.Enable(secondPlugin.Manifest.Id).Success,
            "First constructor source did not register two independent SDK scopes.");
        var controls = firstPlugin.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
        var other = secondPlugin.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
        using var lease = Need(controls.Acquire(true));
        GameplayControlRequest Request(double rate) => new(GameplayControlAction.SetSpeed,
            lease.ControllerGeneration, lease.ObservedRunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);
        Check(!other.Acquire(true).Success && api.ActiveRoots == 14, "First constructor SDK lost exclusive control or formal roots.");
        var speed = Need(lease.Execute(Request(1.5)));
        Check(speed.Modification.TransactionState == GameplayControlTransactionState.Confirmed
            && finalClock.Capture().Rate == 1.5
            && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(currentSource, 16)) == 1.5f,
            "First constructor SDK did not publish an actual native pitch and final Clock.");

        var ctor = targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor);
        var invoke = Export<InvokeStock>(native, "StockFixtureInvoke");
        var read = Export<ConstructorRead>(native, "StockFixtureConstructorRead");
        var configure = Export<ConstructorConfigure>(native, "StockFixtureConfigureConstructor");
        int Ctor(IntPtr self, IntPtr reference, out uint code)
            => invoke(4, ctor.Address, self, unchecked((ulong)reference.ToInt64()), ctor.MethodInfo, out code);
        var health = group.ReadSourceHealth();
        var observed = observations.Count;
        var calls = read(0); var returns = read(1);
        Check(Ctor(template, currentSource, out var code) == 1 && code == 0
            && read(0) == calls + 1 && read(1) == returns + 1
            && group.ReadSourceHealth() == health && observations.Count == observed
            && group.GetTimeLease.ReadCurrent().Active && provider.Probe().Supported
            && module.Tick().Ready && ReferenceEquals(service, Private<GameplayService>(module, "_service")),
            "Allocation-template constructor acquired authority over the first actual player.");

        var fence = group.ReadSourceHealth().SourceSequence;
        var entered = 0;
        setConstructorBefore(observation =>
        {
            Check(observation.Self == currentPlayer && observation.ReferenceArgument == currentSource
                && observation.IncomingMethod == ctor.MethodInfo
                && Marshal.ReadIntPtr(currentPlayer, 16) == currentSource
                && group.ReadSourceHealth().SourceSequence == fence + 1
                && finalClock.Capture().Phase == NativePlaybackPhase.Withdrawn
                && !group.GetTimeLease.ReadCurrent().Active && !provider.Probe().Supported
                && !lease.Execute(Request(3)).Success,
                "First actual player's constructor failed to revoke SDK/Clock before its same-value store.");
            ++entered;
        });
        var boundaries = new List<uint>();
        string? boundaryFailure = null;
        ConstructorBoundary boundary = (self, reference, method, at) =>
        {
            try
            {
                boundaries.Add(at);
                Check(self == currentPlayer && reference == currentSource && method == ctor.MethodInfo
                    && Marshal.ReadIntPtr(self, 16) == currentSource
                    && !group.GetTimeLease.ReadCurrent().Active,
                    "First constructor original changed raw arguments or restored policy during its store.");
            }
            catch (Exception error) { boundaryFailure ??= error.ToString(); }
        };
        configure(0, boundary);
        calls = read(0); returns = read(1);
        var status = Ctor(currentPlayer, currentSource, out code);
        configure(0, null); setConstructorBefore(null); GC.KeepAlive(boundary);
        Check(status == 1 && code == 0 && read(0) == calls + 1 && read(1) == returns + 1
            && entered == 1 && boundaries.SequenceEqual(new uint[] { 1, 2 })
            && callbackFailure() is null && boundaryFailure is null
            && finalClock.Capture().Phase == NativePlaybackPhase.Withdrawn
            && group.ReadSourceHealth() is { Known: true, InFlight: 0, Unknown: false },
            "First current constructor did not revoke exactly once and physically drain: "
            + callbackFailure() + boundaryFailure);
        Check(physical == (group.Epoch, group.GetTimeLease.EntryAddress, group.GetTimeLease.OriginalAddress,
            group.SetPitchLease.EntryAddress) && broker.ReadActualPlaybackDobbyCallCounts() == (13, 13),
            "First constructor watch replaced the thirteen retained physical hooks.");
        module.Release(); provider.PumpCompleted(); router.PumpCompleted();
        Check(router.RootsReleased && api.ActiveRoots == 0 && observations.All(value => value.IsReleased),
            "First constructor source-open leaked actual roots or observations.");
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "ctor-source-first", passed = true, checks = _checks,
            hostOnlyInitialCapture = true, firstSourceOpen = true, allocationTemplateTransparent = true,
            currentConstructorBeforeStoreRevokes = true, initialCaptureProtocol = 1, initializationProtocol = 2,
            actualPrepare = 13, actualCommit = 13, retainedRoots = api.ActiveRoots,
            gameExecuted = false, productionAdmission = false }));
        return 0;
    }

    private static int RunConstructorSourceOpen(string[] a, string mode)
    {
        if (mode == "ctor-source-first") return RunFirstCapture(a, "first-normal", constructor: true, sourceWatch: true);
        if (mode == "ctor-source-cross")
        {
            // The existing exact cross-run fixture performs this same watch
            // transition with a separate host-issued run and native graph.
            var result = RunConstructorCrossRun(a, "ctor-sdk-crossrun");
            if (result == 0) Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                crossRunSourceOpen = true, predecessorConstructorTransparent = true, successorConstructorRevokes = true,
                actualPrepare = 13, actualCommit = 13, gameExecuted = false, productionAdmission = false }));
            return result;
        }
        try
        {
            Check(a.Length == 5 && mode == "ctor-source-same", "Unknown constructor source-open mode.");
            Check(Hash(a[1]) == PlaybackSourceGroupTargets.ConstructorFixtureSha256,
                "Only the independently frozen constructor fixture may execute source-open.");
            using var native = new ConstructorContext(a);
            using (var sdk = new ConstructorSdk(native))
            {
                sdk.Speed(1.5);
                var enumerator = native.Api.SourceOpenEnumerator;
                var oldPlayer = native.Api.GameplayPlayer;
                var oldSource = native.Api.GameplaySource;
                var oldClock = native.Clock;
                var oldSession = native.Session;
                var oldPublication = Private<NativePlaybackClockPublication>(oldSession, "_publication");
                var oldRooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
                var physical = (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                    native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress);
                var start = native.Targets.ProgressStartMoveNext;
                var invokeStart = Export<InvokeStart>(native.Module, "StockFixtureInvokeStart");
                uint Start()
                {
                    Check(invokeStart(start.Address, enumerator, start.MethodInfo, out var raw, out var code) == 1 && code == 0,
                        "Actual Start original failed: " + native.Router.LastFailure);
                    return raw;
                }
                Marshal.WriteInt32(enumerator, 0x10, 0);
                Check(Start() == 1 && native.Router.InitializationPending && oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn
                    && !sdk.Provider.Probe().Supported && !native.Group.GetTimeLease.ReadCurrent().Active,
                    "AL1 did not withdraw predecessor authority while retaining the initializer.");
                Check(!native.Router.TryCompleteInitialization() && native.Group.ReadInitialization() is { Phase: 2, Blocked: 1 },
                    "An AL1 yield opened the next source without final parent exit.");
                Check(Start() == 0 && native.Group.ReadInitialization() is { Phase: 4, Blocked: 1 },
                    "Actual AL0 did not retain its completed initialization receipt.");
                native.Api.AdoptActuallyInitializedPlayer();
                var currentPlayer = native.Api.GameplayPlayer;
                Check(currentPlayer != oldPlayer && Marshal.ReadIntPtr(native.Api.GameplayProgress, 0x38) == currentPlayer,
                    "Actual inline Start store did not install a new MUG player.");
                Check(native.Router.TryCompleteInitialization() && native.Group.ReadInitialization() is { Phase: 7, Blocked: 0 }
                    && !sdk.Provider.Probe().Supported && !native.Group.GetTimeLease.ReadCurrent().Active,
                    "Same-run source-open did not replace the watch in a dormant state: " + native.Router.LastFailure);
                Refuse(() => native.Router.PreparePausedClock(), "Initializer source-open substituted for later actual Pause.");
                native.Arm();
                Check(native.Clock.Capture().Binding.RunId == oldClock.Capture().Binding.RunId
                    && native.Clock.Capture().Binding.Player == currentPlayer.ToInt64()
                    && native.Clock.Capture().Binding.SourceGeneration > oldClock.Capture().Binding.SourceGeneration
                    && !ReferenceEquals(native.Clock, oldClock) && sdk.Provider.Probe().Supported,
                    "Later Pause did not arm a new same-run final Clock.");
                Check(sdk.Module.Tick().Ready && ReferenceEquals(sdk.Service, Private<GameplayService>(sdk.Module, "_service")),
                    "Original Module/Service did not accept same-run source-open.");
                using var lease = Need(sdk.Controls.Acquire(true));
                GameplayControlRequest Request(double rate) => new(GameplayControlAction.SetSpeed, lease.ControllerGeneration,
                    lease.ObservedRunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);
                Check(lease.ControllerGeneration > sdk.Lease.ControllerGeneration && lease.ObservedRunId == sdk.Lease.ObservedRunId
                    && !sdk.Lease.Execute(sdk.Request(3)).Success && !sdk.Other.Acquire(true).Success,
                    "Old SDK lease survived the new source epoch or exclusive control was lost.");
                var speed = Need(lease.Execute(Request(2)));
                Check(speed.Modification.TransactionState == GameplayControlTransactionState.Confirmed
                    && native.Clock.Capture().Rate == 2 && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(native.Api.GameplaySource, 16)) == 2,
                    "Successor Clock/source did not accept actual SDK pitch.");

                var ctor = native.Targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor);
                var health = native.Group.ReadSourceHealth(); var observed = native.Observations.Count;
                var before = native.Read(0); var returned = native.Read(1);
                Check(native.Ctor(oldPlayer, oldSource, ctor.MethodInfo, out var code) == 1 && code == 0
                    && native.Read(0) == before + 1 && native.Read(1) == returned + 1
                    && native.Group.ReadSourceHealth() == health && native.Observations.Count == observed
                    && native.Group.GetTimeLease.ReadCurrent().Active && sdk.Provider.Probe().Supported,
                    "Late predecessor constructor changed successor fence, journal or Clock.");
                sdk.Lease.Dispose(); oldSession.Withdraw("Late same-run predecessor session cleanup");
                oldPublication.Withdraw("Late same-run predecessor publication cleanup"); oldRooted.Dispose();
                Check(native.Group.GetTimeLease.ReadCurrent().Active && sdk.Provider.Probe().Supported
                    && native.Time(currentPlayer, native.Targets.GetTime.MethodInfo) == (float)native.Clock.Capture().MediaAnchorSeconds,
                    "Late predecessor cleanup revoked the same-run successor.");

                var currentSource = native.Api.GameplaySource;
                var fence = native.Group.ReadSourceHealth().SourceSequence;
                var entered = 0; var boundaries = new List<uint>();
                native.AfterConstructorBefore = observation =>
                {
                    Check(observation.Self == currentPlayer && observation.ReferenceArgument == currentSource
                        && observation.IncomingMethod == ctor.MethodInfo && Marshal.ReadIntPtr(currentPlayer, 16) == currentSource
                        && native.Group.ReadSourceHealth().SourceSequence == fence + 1
                        && native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn
                        && !native.Group.GetTimeLease.ReadCurrent().Active && !sdk.Provider.Probe().Supported
                        && !lease.Execute(Request(3)).Success,
                        "New watch did not revoke Clock/source/SDK before the same-value store.");
                    ++entered;
                };
                ConstructorBoundary boundary = (self, reference, incoming, at) => native.RecordBoundary(() =>
                {
                    boundaries.Add(at);
                    Check(self == currentPlayer && reference == currentSource && incoming == ctor.MethodInfo
                        && Marshal.ReadIntPtr(self, 16) == currentSource && !native.Group.GetTimeLease.ReadCurrent().Active,
                        "Successor original changed arguments or reopened policy during its store.");
                });
                native.Configure(0, boundary);
                before = native.Read(0); returned = native.Read(1);
                Check(native.Ctor(currentPlayer, currentSource, ctor.MethodInfo, out code) == 1 && code == 0
                    && native.Read(0) == before + 1 && native.Read(1) == returned + 1
                    && entered == 1 && boundaries.SequenceEqual(new uint[] { 1, 2 }) && native.CallbackFailure is null
                    && native.Group.ReadSourceHealth() is { Known: true, InFlight: 0, Unknown: false },
                    "Successor constructor did not execute once and drain: " + native.CallbackFailure);
                native.Configure(0, null); native.AfterConstructorBefore = null; GC.KeepAlive(boundary);
                Check(physical == (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                    native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress)
                    && native.Broker.ReadActualPlaybackDobbyCallCounts() == (13, 13),
                    "Same-run source-open replaced the retained physical group.");
            }
            native.Router.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(value => value.IsReleased),
                "Known-drained same-run route leaked roots or observations.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                sameRunSourceOpen = true, predecessorConstructorTransparent = true, successorConstructorRevokes = true,
                typedInitializerCompletion = true, actualPrepare = 13, actualCommit = 13,
                retainedRoots = native.Api.ActiveRoots, gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() })); return 1; }
    }
}

sealed partial class FakeNative
{
    internal IntPtr SourceOpenEnumerator => _gameplayFields["initializer.state"].Owner;
}
