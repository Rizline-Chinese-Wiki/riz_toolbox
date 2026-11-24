using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    private static (uint Role, long Entry, long Original)[] DirectHandoffPhysical(ConstructorContext native)
        => native.Targets.Roles.Select(role => (Role: (uint)role,
                Entry: native.Group.SourceLease(role).EntryAddress.ToInt64(),
                Original: native.Group.SourceLease(role).OriginalAddress.ToInt64()))
            .Append((0u, native.Group.GetTimeLease.EntryAddress.ToInt64(), native.Group.GetTimeLease.OriginalAddress.ToInt64())).ToArray();

    private static NativePlaybackStockSourceHub DirectHandoffHub(ConstructorContext native)
    {
        var owner = Private<ExactHookBroker.PlaybackSourceOwner>(native.Group.SourceLease(NativePlaybackSourceRole.AudioPlayHelper), "_owner");
        return Private<NativePlaybackStockSourceHub>(owner, "_hub");
    }

    private static int RunDirectAudioHandoff(string[] a, string mode)
    {
        try
        {
            Check(a.Length == 5 && mode is "direct-source-same" or "direct-source-cross"
                && Hash(a[1]) == PlaybackSourceGroupTargets.DirectAudioFixtureSha256,
                "Direct source-open requires the fixed nineteen-member owned fixture.");
            var cross = mode == "direct-source-cross";
            var currentIndex = cross ? 4 : 5;
            using var native = new ConstructorContext(a);
            Check(native.Targets.HasDirectAudioControls && native.PhysicalMemberCount == 19
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (19, 19),
                "Direct source-open did not physically install the exact nineteen members.");
            var epoch = native.Group.Epoch;
            var physical = DirectHandoffPhysical(native);
            Check(physical.Length == 19 && physical.Select(value => value.Entry).Distinct().Count() == 19,
                "Direct source-open has no unique retained nineteen-member physical group.");
            using (var sdk = new ConstructorSdk(native))
            {
                sdk.Speed(1.5);
                var oldClock = native.Clock;
                var oldBinding = oldClock.Capture().Binding;
                var oldSession = native.Session;
                var oldPublication = Private<NativePlaybackClockPublication>(oldSession, "_publication");
                var oldRooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
                var oldPlayer = native.Api.GameplayPlayer;
                var oldSource = native.Api.GameplaySource;
                var hub = DirectHandoffHub(native);
                var oldWatch = Private<List<NativePlaybackStockSourceHub.PlayerWatch>>(hub, "_playerWatches")
                    .Single(value => ReferenceEquals(value.SourceOwner, oldRooted));
                native.Group.VerifyActivePlayerWatch(oldRooted);

                if (cross) DirectOpenCrossRun(native, sdk, oldSession, oldClock, oldPlayer, oldSource);
                else DirectOpenSameRun(native, sdk, oldClock, oldPlayer, oldSource);

                var currentPlayer = native.Api.GameplayPlayer;
                var currentSource = native.Api.GameplaySource;
                var successor = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
                var successorWatch = Private<List<NativePlaybackStockSourceHub.PlayerWatch>>(hub, "_playerWatches")
                    .Single(value => ReferenceEquals(value.SourceOwner, successor));
                var actual = Private<NativePlaybackActualSource>(successor, "_actual");
                var roots = Private<List<NativeRuntime.RootHandle>>(actual, "_roots");
                Check(!ReferenceEquals(successor, oldRooted) && !ReferenceEquals(successorWatch, oldWatch)
                    && successorWatch.NativeReceipt != oldWatch.NativeReceipt && oldRooted.RootsReleased
                    && roots.Select(root => root.Target.ToInt64()).SequenceEqual(new[] {
                        native.Api.GameplayController.ToInt64(), native.Api.GameplayInformation.ToInt64(),
                        native.Api.GameplayProgress.ToInt64(), native.Api.GameplayPause.ToInt64(),
                        currentPlayer.ToInt64(), currentSource.ToInt64(), native.Api.GameplayClip.ToInt64() }),
                    "Source-open did not replace the exact watch and its seven rooted native objects.");
                native.Group.VerifyActivePlayerWatch(successor);
                Check(!native.Group.GetTimeLease.ReadCurrent().Active && !sdk.Provider.Probe().Supported,
                    "Source-open itself armed a final Clock before the successor Pause.");
                Refuse(() => native.Router.PreparePausedClock(), "Source-open bypassed the later actual successor Pause.");
                native.Arm();
                native.SynchronizePausedFixtureProgress();
                var binding = native.Clock.Capture().Binding;
                Check(!ReferenceEquals(native.Clock, oldClock) && ReferenceEquals(native.Clock, native.Session.Clock)
                    && binding.Player == currentPlayer.ToInt64() && binding.AudioSource == currentSource.ToInt64()
                    && binding.SourceGeneration > oldBinding.SourceGeneration
                    && (binding.RunId != oldBinding.RunId) == cross
                    && oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn,
                    "Later actual Pause did not create the unique successor Clock and source epoch.");
                Check(sdk.Module.Tick().Ready && ReferenceEquals(sdk.Service, Private<GameplayService>(sdk.Module, "_service"))
                    && sdk.ClockView.Read().Success && sdk.OtherClock.Read().Success && sdk.Provider.Probe().Supported,
                    "Original Module/Service and both retained SDK clock views did not adopt the successor.");
                using var lease = Need(sdk.Controls.Acquire(true));
                GameplayControlRequest Request(double rate) => new(GameplayControlAction.SetSpeed,
                    lease.ControllerGeneration, lease.ObservedRunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);
                Check(lease.ControllerGeneration > sdk.Lease.ControllerGeneration && lease.ObservedRunId == binding.RunId
                    && !sdk.Lease.Execute(sdk.Request(3)).Success && !sdk.Other.Acquire(true).Success,
                    "Retained SDK controls lost successor exclusivity or preserved the predecessor lease.");
                var pitchCalls = Marshal.ReadInt32(currentSource, 20);
                var health = native.Group.ReadSourceHealth();
                var result = Need(lease.Execute(Request(2)));
                Check(result.Modification.TransactionState == GameplayControlTransactionState.Confirmed
                    && result.Modification.ConfirmedValue == 2 && native.Clock.Capture().Rate == 2
                    && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(currentSource, 16)) == 2
                    && Marshal.ReadInt32(currentSource, 20) == pitchCalls + 1
                    && Marshal.ReadIntPtr(currentSource, 72) == native.Targets.PitchInstance.MethodInfo
                    && native.Group.ReadSourceHealth().WriterSequence == health.WriterSequence + 1,
                    "Original SDK controls did not confirm one actual successor pitch body and final Clock rate.");

                sdk.Lease.Dispose();
                oldSession.Withdraw("Late direct source-open predecessor session cleanup");
                oldPublication.Withdraw("Late direct source-open predecessor publication cleanup");
                oldRooted.Dispose(); oldRooted.PumpCompleted();
                Check(native.Group.TryRetirePlayerWatch(oldRooted), "Late predecessor owner could not finish its already-retired watch cleanup.");
                Refuse(() => oldWatch.TryRetire(oldRooted), "A retained removed predecessor watch regained retirement authority.");
                native.Group.VerifyActivePlayerWatch(successor);
                Check(native.Group.GetTimeLease.ReadCurrent().Active && sdk.Provider.Probe().Supported
                    && native.Time(currentPlayer, native.Targets.GetTime.MethodInfo) == (float)native.Clock.Capture().MediaAnchorSeconds
                    && native.UniqueFinalClock && native.Api.ActiveRoots == 14,
                    "Late old session/publication/root/watch cleanup withdrew the actual successor or its roots.");

                if (cross) DirectOldSourceTransparent(native, sdk, oldSource);
                else Check(oldSource == currentSource && oldPlayer != currentPlayer
                    && Marshal.ReadIntPtr(oldPlayer, 0x10) == currentSource
                    && Marshal.ReadIntPtr(currentPlayer, 0x10) == currentSource,
                    "Same-run Start must reuse the AudioSource address; that old address is still current, not foreign.");

                DirectSuccessorRevokes(native, sdk, lease, Request, currentIndex);
                Check(epoch == native.Group.Epoch && physical.SequenceEqual(DirectHandoffPhysical(native))
                    && native.Broker.ReadActualPlaybackDobbyCallCounts() == (19, 19),
                    "Logical source-open or direct invalidation replaced a retained physical hook.");
            }
            native.Router.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0
                && native.Observations.All(value => value.IsReleased) && native.Group.UnresolvedPlayerRootOwnerCount == 0,
                "Known-drained direct source-open retained native graph roots, watches or observations.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 19, actualCommit = 19, physicalSources = 18, nativeSourceVersion = 4, playerWatchVersion = 2,
                sameModuleServiceSdk = true, sameRunInitV2 = !cross, crossRunConditionalHandoff = cross,
                sourceAddressReused = !cross, predecessorTransparentRoles = cross ? 6 : 0,
                currentRevocationRole = ((NativePlaybackSourceRole)(13 + currentIndex)).ToString(), currentRevocationSamples = 1,
                retainedRoots = native.Api.ActiveRoots, gameExecuted = false, productionAdmission = false, fullGenerationCompleted = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
    }

    private static void DirectOpenSameRun(ConstructorContext native, ConstructorSdk sdk, NativePlaybackClock oldClock,
        IntPtr oldPlayer, IntPtr oldSource)
    {
        var enumerator = native.Api.SourceOpenEnumerator;
        var start = native.Targets.ProgressStartMoveNext;
        var invoke = Export<InvokeStart>(native.Module, "StockFixtureInvokeStart");
        uint Start()
        {
            Check(invoke(start.Address, enumerator, start.MethodInfo, out var raw, out var code) == 1 && code == 0,
                "Actual direct source-open Start original failed: " + native.Router.LastFailure);
            return raw;
        }
        Marshal.WriteInt32(enumerator, 0x10, 0);
        Check(Start() == 1 && native.Router.InitializationPending
            && oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn
            && !sdk.Provider.Probe().Supported && !native.Group.GetTimeLease.ReadCurrent().Active,
            "Actual AL1 did not withdraw the predecessor and retain the exact initializer.");
        var yield = native.Observations.Last(value => value.Role == NativePlaybackSourceRole.ProgressStartMoveNext);
        Check(yield.TryReadCompletion(out var yielded) && yielded is { PhysicalExited: true, Returned: true, RawReturnByte: 1 }
            && !native.Router.TryCompleteInitialization() && native.Group.ReadInitialization() is { Phase: 2, Blocked: 1 },
            "Actual AL1 physical exit admitted a premature same-run successor.");
        Check(Start() == 0 && native.Group.ReadInitialization() is { Phase: 4, Blocked: 1 },
            "Actual AL0 did not retain its exact InitV2 completed receipt.");
        var final = native.Observations.Last(value => value.Role == NativePlaybackSourceRole.ProgressStartMoveNext);
        Check(final.TryReadCompletion(out var completed) && completed is { PhysicalExited: true, Returned: true, RawReturnByte: 0 },
            "Same-run source-open lacks the actual final AL0 parent physical exit.");
        native.Api.AdoptActuallyInitializedPlayer();
        Check(native.Api.GameplayPlayer != oldPlayer && native.Api.GameplaySource == oldSource
            && Marshal.ReadIntPtr(native.Api.GameplayProgress, 0x38) == native.Api.GameplayPlayer
            && Marshal.ReadIntPtr(native.Api.GameplayPlayer, 0x10) == oldSource,
            "Actual same-run inline stores must replace Player and retain the exact AudioSource address.");
        Check(native.Router.TryCompleteInitialization() && native.Group.ReadInitialization() is { Phase: 7, Blocked: 0 },
            "Actual InitV2 same-run source-open failed: " + native.Router.LastFailure);
    }

    private static void DirectOpenCrossRun(ConstructorContext native, ConstructorSdk sdk,
        NativePlaybackStockClockRouter.Session oldSession, NativePlaybackClock oldClock, IntPtr oldPlayer, IntPtr oldSource)
    {
        var handoff = native.Router.BeginHandoff(oldSession);
        Check(oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn && !sdk.Provider.Probe().Supported
            && !native.Group.GetTimeLease.ReadCurrent().Active,
            "Conditional cross-run handoff did not close predecessor authority before graph construction.");
        native.State.Scene("Menu"); native.State.Scene("Level");
        native.State.SetAvailability(scenes: true, runs: true, pause: true);
        native.Api.NextRootedStockGraph();
        native.Api.SetRootedGraphValue("progress.now", 2.8); native.Api.SetRootedGraphValue("info.nowTime", 5.6f);
        var started = native.State.Controller(native.Api.GameplayInformation.ToInt64(), true, false,
            "direct-source-successor", native.Api.GameplayController.ToInt64())!;
        native.State.ObservePause(true);
        var next = new NativePlaybackBinding(started.RunId, native.State.SceneGeneration,
            oldClock.Capture().Binding.ControllerGeneration + 1, 1,
            native.Api.GameplayController.ToInt64(), native.Api.GameplayInformation.ToInt64(), native.Api.GameplayProgress.ToInt64(),
            native.Api.GameplayPlayer.ToInt64(), native.Api.GameplaySource.ToInt64(), native.Api.GameplayClip.ToInt64(),
            native.Targets.GetTime.MethodInfo.ToInt64());
        var run = native.State.CapturePlaybackRun(next.Information, next.Controller);
        Check(native.Api.GameplayPlayer != oldPlayer && native.Api.GameplaySource != oldSource
            && next.RunId != oldClock.Capture().Binding.RunId
            && Marshal.ReadIntPtr(native.Api.GameplaySource) == Marshal.ReadIntPtr(oldSource),
            "Cross-run source-open needs a different host-issued run and distinct AudioSources of the same class.");
        Refuse(() => Copy(handoff).TryAdmit(run, next, native.Api.GameplayPause.ToInt64()),
            "A copied handoff claimed the successor direct AudioSource watch.");
        Check(handoff.TryAdmit(run, next, native.Api.GameplayPause.ToInt64()),
            "Actual conditional cross-run source-open failed: " + native.Router.LastFailure);
    }

    private static void DirectOldSourceTransparent(ConstructorContext native, ConstructorSdk sdk, IntPtr oldSource)
    {
        var currentSource = native.Api.GameplaySource;
        Check(oldSource != currentSource && Marshal.ReadIntPtr(oldSource) == Marshal.ReadIntPtr(currentSource),
            "Predecessor transparency needs two retained same-class sources at different actual addresses.");
        var read = Export<DirectRead>(native.Module, "StockFixtureDirectRead");
        var health = native.Group.ReadSourceHealth(); var observed = native.Observations.Count; var clockReads = native.ClockReads;
        for (var index = 0; index < 6; ++index)
        {
            var role = (NativePlaybackSourceRole)(13 + index); var target = native.Targets.SourceTarget(role);
            var incoming = index % 3 == 0 ? target.MethodInfo : index % 3 == 1 ? IntPtr.Zero : native.Targets.Pause.MethodInfo;
            var calls = read((uint)role, 0); var returns = read((uint)role, 1);
            var bodyCalls = Marshal.ReadInt32(oldSource, DirectCallsOffset);
            var kind = (uint)(index == 0 ? 8 : index == 1 ? 3 : index < 4 ? 1 : 5);
            Check(native.Invoke(kind, target.Address, oldSource, DirectBits(index), incoming, out var code) == 1 && code == 0
                && read((uint)role, 0) == calls + 1 && read((uint)role, 1) == returns + 1
                && read((uint)role, 2) == unchecked((ulong)oldSource.ToInt64()) && read((uint)role, 3) == DirectBits(index)
                && read((uint)role, 4) == unchecked((ulong)incoming.ToInt64())
                && Marshal.ReadInt32(oldSource, DirectCallsOffset) == bodyCalls + 1,
                "Late predecessor direct source lost one exact original or its unmodified raw ABI: " + role);
        }
        Check(native.Group.ReadSourceHealth() == health && native.Observations.Count == observed && native.ClockReads == clockReads
            && native.Group.GetTimeLease.ReadCurrent().Active && sdk.Provider.Probe().Supported
            && native.Clock.Capture().Rate == 2 && native.Api.ActiveRoots == 14 && native.CallbackFailure is null,
            "Distinct same-class predecessor direct calls changed successor fences, journal, Clock, SDK or roots.");
    }

    private static void DirectSuccessorRevokes(ConstructorContext native, ConstructorSdk sdk, IGameplayControlLease lease,
        Func<double, GameplayControlRequest> request, int index)
    {
        var source = native.Api.GameplaySource; var role = (NativePlaybackSourceRole)(13 + index);
        var target = native.Targets.SourceTarget(role); var read = Export<DirectRead>(native.Module, "StockFixtureDirectRead");
        var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
        var origin = Private<NativePlaybackRateOrigin>(native.Router, "_origin");
        var run = Private<GameEventState.PlaybackRunAssociation>(native.Router, "_run");
        var binding = native.Clock.Capture().Binding;
        using var captured = sdk.Provider.Capture(origin);
        using var writer = captured.PreparePitchWrite();
        using var prepared = NativePlaybackRateProviderAdoption.Prepare(sdk.Provider, origin,
            new(GameplayControlAction.SetSpeed, binding.ControllerGeneration, binding.RunId, 2.5, GameplayControlValueUnit.PlaybackRateMultiplier),
            () => run.VerifyCurrent(binding));
        var health = native.Group.ReadSourceHealth(); var calls = read((uint)role, 0); var returns = read((uint)role, 1);
        var bodyCalls = Marshal.ReadInt32(source, DirectCallsOffset);
        var oldLoop = Marshal.ReadInt32(source, DirectCallsOffset + 4);
        var oldPlaying = Marshal.ReadInt32(source, 48);
        var entered = 0; var boundaries = new List<uint>();
        native.AfterDirectAudioBefore = observation =>
        {
            Check(observation.Role == role && observation.Self == source && observation.IncomingMethod == target.MethodInfo
                && observation.ArgBits == DirectBits(index) && observation.SchedulingSequence == health.SchedulingSequence + 1
                && observation.SourceSequence == health.SourceSequence && read((uint)role, 0) == calls
                && Marshal.ReadInt32(source, DirectCallsOffset) == bodyCalls,
                "Successor direct Before did not precede the original with exact raw arguments and scheduling cut.");
            Check(native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn
                && !native.Group.GetTimeLease.ReadCurrent().Active && !sdk.Provider.Probe().Supported
                && !lease.Execute(request(3)).Success,
                "Successor direct Before retained final Clock/publication or formal SDK control authority.");
            Refuse(() => { _ = rooted.Current; }, "Successor direct Before retained the rooted source authority.");
            Refuse(captured.VerifyAuthority, "Successor direct Before retained a captured rate source.");
            Refuse(writer.VerifyCurrent, "Successor direct Before retained a continuous writer.");
            Refuse(prepared.Commit, "Successor direct Before committed a previously prepared rate transaction.");
            prepared.Restore();
            ++entered;
        };
        DirectBoundary boundary = (self, actualRole, bits, method, stage) => native.RecordBoundary(() =>
        {
            boundaries.Add(stage);
            Check(self == source && actualRole == (uint)role && bits == DirectBits(index) && method == target.MethodInfo
                && native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn && !native.Group.GetTimeLease.ReadCurrent().Active
                && Marshal.ReadInt32(source, DirectCallsOffset) == bodyCalls + (stage == 1 ? 0 : 1)
                && (index == 5 ? Marshal.ReadInt32(source, DirectCallsOffset + 4) == (stage == 1 ? oldLoop : 1)
                    : Marshal.ReadInt32(source, 48) == (stage == 1 ? oldPlaying : 0)),
                "Successor direct original changed raw arguments, store order or reopened Clock authority.");
        });
        var configure = Export<ConfigureDirectBoundary>(native.Module, "StockFixtureConfigureDirect");
        configure(0, boundary);
        int status; uint code;
        try { status = native.Invoke(5, target.Address, source, DirectBits(index), target.MethodInfo, out code); }
        finally { configure(0, null); native.AfterDirectAudioBefore = null; GC.KeepAlive(boundary); }
        var after = native.Group.ReadSourceHealth();
        Check(status == 1 && code == 0 && entered == 1 && boundaries.SequenceEqual(new uint[] { 1, 2 })
            && native.CallbackFailure is null && read((uint)role, 0) == calls + 1 && read((uint)role, 1) == returns + 1
            && read((uint)role, 2) == unchecked((ulong)source.ToInt64()) && read((uint)role, 3) == DirectBits(index)
            && read((uint)role, 4) == unchecked((ulong)target.MethodInfo.ToInt64())
            && Marshal.ReadInt32(source, DirectCallsOffset) == bodyCalls + 1,
            "Successor direct native original did not execute once with exact raw ABI: " + native.CallbackFailure);
        Check(after is { Known: true, InFlight: 0, Unknown: false } && after.Entered == health.Entered + 1 && after.Exited == health.Exited + 1
            && after.SchedulingSequence == health.SchedulingSequence + 1 && after.SourceSequence == health.SourceSequence
            && native.Group.ReadActivity() is { Known: true, InFlight: 0 }
            && native.Observations.Last(value => value.Role == role).IsReleased,
            "Successor direct native call did not physically exit/release or invented a source generation/transport fault.");
    }
}
