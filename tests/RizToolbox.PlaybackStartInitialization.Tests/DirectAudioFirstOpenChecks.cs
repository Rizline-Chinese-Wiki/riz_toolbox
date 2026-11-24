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
    private static int FinishFirstDirectAudioSourceWatch(
        FakeNative api, IntPtr native, PlaybackSourceGroupTargets targets, PlaybackSourceGroupLease group,
        NativePlaybackStockClockRouter router, NativePlaybackStockClockRouter.Session session,
        NativePlaybackClock finalClock, IntPtr template, List<NativePlaybackSourceObservation> observations,
        ReadSharedConstructor readShared, GetTime getTime, ExactHookBroker broker, GameEventState state,
        GameEventState.PlaybackRunAssociation run,
        MainThreadDispatcher main, Logger log,
        Action<Action<NativePlaybackSourceObservation>?> setDirectBefore, Func<string?> callbackFailure,
        Func<int> nativeReads, Func<bool> uniqueFinalClock)
    {
        // This is reached only after the original host-only four roots, actual
        // AL1/AL0, typed initial-capture/source-open, and a later actual Pause.
        // The earlier host-only period does not claim a direct AudioSource watch.
        var player = api.GameplayPlayer;
        var source = api.GameplaySource;
        var binding = finalClock.Capture().Binding;
        var origin = new NativePlaybackRateOrigin(binding.RunId, binding.ControllerGeneration,
            binding.Controller, binding.Information, binding.Progress, api.GameplayPause.ToInt64());
        var physical = (group.Epoch, group.GetTimeLease.EntryAddress, group.GetTimeLease.OriginalAddress,
            group.SetPitchLease.EntryAddress);
        Check(targets.HasDirectAudioControls && targets.SourceRoleCount == 18 && group.HasDirectAudioControls
            && player != template && binding.Player == player.ToInt64() && binding.AudioSource == source.ToInt64()
            && ReferenceEquals(finalClock, session.Clock) && api.ActiveRoots == 7
            && readShared(0) == 2 && readShared(1) == 2
            && observations.All(value => value.Role != NativePlaybackSourceRole.PlayerConstructor)
            && group.GetTimeLease.ReadCurrent().Active
            && getTime(player, targets.GetTime.MethodInfo) == (float)finalClock.Capture().MediaAnchorSeconds
            && broker.ReadActualPlaybackDobbyCallCounts() == (19, 19),
            "First direct source-open did not reach the unique actual player/source, seven roots and nineteen physical members.");

        using var provider = router.AdoptControlledRateHost();
        using var journal = new RunModificationJournal(main, log, state);
        var module = new GameplayModule(api.GameplayApi, main, () => state.LifecycleSnapshot(),
            journal.Record, state.MatchesCurrentGameplayRun, provider);
        Check(module.Probe().Ready && provider.Probe().Supported,
            "First direct source-open did not reach the formal Module.");
        var service = Private<GameplayService>(module, "_service");
        var capabilities = new CapabilityRegistry(new("first-direct-audio-source", "fixture", "synthetic", "offline"));
        var registration = new ModuleRegistration(capabilities, module.Manifest);
        module.Register(registration); registration.Complete();
        capabilities.RegisterScoped<IRunModificationService>(RunModificationCapabilityIds.Read, journal.ForPlugin);
        using var plugins = new PluginHost(capabilities, main, new EventHub(log), log);
        var firstPlugin = new RouterPlugin("first-direct-one");
        var secondPlugin = new RouterPlugin("first-direct-two");
        Check(plugins.Register(firstPlugin).Success && plugins.Register(secondPlugin).Success
            && plugins.Enable(firstPlugin.Manifest.Id).Success && plugins.Enable(secondPlugin.Manifest.Id).Success,
            "First direct source-open did not register two independent PluginHost scopes.");
        var controls = firstPlugin.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
        var other = secondPlugin.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
        var clockView = firstPlugin.Context.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!;
        var otherClock = secondPlugin.Context.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!;
        using var lease = Need(controls.Acquire(true));
        GameplayControlRequest Request(double rate) => new(GameplayControlAction.SetSpeed,
            lease.ControllerGeneration, lease.ObservedRunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);
        Check(!ReferenceEquals(controls, other) && !other.Acquire(true).Success
            && clockView.Read().Success && otherClock.Read().Success && api.ActiveRoots == 14,
            "First direct SDK views lost independent scopes, exclusive control or seven independent Service roots.");
        var pitchCalls = Marshal.ReadInt32(source, 20);
        var speedHealth = group.ReadSourceHealth();
        var speed = Need(lease.Execute(Request(1.5)));
        Check(speed.Modification.TransactionState == GameplayControlTransactionState.Confirmed
            && speed.Modification.ConfirmedValue == 1.5 && finalClock.Capture().Rate == 1.5
            && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(source, 16)) == 1.5f
            && Marshal.ReadInt32(source, 20) == pitchCalls + 1
            && Marshal.ReadIntPtr(source, 72) == targets.PitchInstance.MethodInfo
            && unchecked((ulong)Marshal.ReadInt64(source, 104)) == unchecked((uint)BitConverter.SingleToInt32Bits(1.5f))
            && group.ReadSourceHealth().WriterSequence == speedHealth.WriterSequence + 1
            && ReferenceEquals(finalClock, session.Clock),
            "First direct SDK SetSpeed did not confirm exactly one actual native pitch and the sole final Clock.");

        var invoke = Export<InvokeStock>(native, "StockFixtureInvoke");
        var ctor = targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor);
        var health = group.ReadSourceHealth();
        var observed = observations.Count;
        var ctorCalls = readShared(0); var ctorReturns = readShared(1);
        Check(invoke(4, ctor.Address, template, unchecked((ulong)source.ToInt64()), ctor.MethodInfo, out var code) == 1 && code == 0
            && readShared(0) == ctorCalls + 1 && readShared(1) == ctorReturns + 1
            && group.ReadSourceHealth() == health && observations.Count == observed
            && group.GetTimeLease.ReadCurrent().Active && provider.Probe().Supported,
            "The allocation-template player acquired authority over the first initialized player's source watch.");

        var foreign = api.CreateSharedConstructorSource();
        Check(foreign != source && Marshal.ReadIntPtr(foreign) == Marshal.ReadIntPtr(source),
            "First direct source-open requires a distinct actual source of the same exact class.");
        var read = Export<DirectRead>(native, "StockFixtureDirectRead");
        for (var index = 0; index < 6; ++index)
        {
            var foreignRole = (NativePlaybackSourceRole)(13 + index);
            var target = targets.SourceTarget(foreignRole);
            var before = read((uint)foreignRole, 0); var after = read((uint)foreignRole, 1);
            var bodyCalls = Marshal.ReadInt32(foreign, DirectCallsOffset);
            for (var variation = 0; variation < 3; ++variation)
            {
                var incoming = variation == 0 ? target.MethodInfo : variation == 1 ? IntPtr.Zero : targets.Pause.MethodInfo;
                var kind = (uint)(index == 0 ? 8 : index == 1 ? 3 : index < 4 ? 1 : 5);
                Check(invoke(kind, target.Address, foreign, DirectBits(index), incoming, out code) == 1 && code == 0
                    && read((uint)foreignRole, 2) == unchecked((ulong)foreign.ToInt64())
                    && read((uint)foreignRole, 3) == DirectBits(index)
                    && read((uint)foreignRole, 4) == unchecked((ulong)incoming.ToInt64()),
                    "An actual foreign same-class direct original changed its raw ABI.");
            }
            Check(read((uint)foreignRole, 0) == before + 3 && read((uint)foreignRole, 1) == after + 3
                && Marshal.ReadInt32(foreign, DirectCallsOffset) == bodyCalls + 3
                && group.ReadSourceHealth() == health && observations.Count == observed
                && group.GetTimeLease.ReadCurrent().Active && provider.Probe().Supported,
                "A foreign same-class source changed the first source's watch, fences, journal or SDK authority.");
        }
        Check(module.Tick().Ready && ReferenceEquals(service, Private<GameplayService>(module, "_service"))
            && getTime(player, targets.GetTime.MethodInfo) == (float)finalClock.Capture().MediaAnchorSeconds,
            "Template/foreign calls replaced the first final Clock or formal Service.");

        var role = NativePlaybackSourceRole.AudioPlayHelper;
        var entry = targets.SourceTarget(role);
        var calls = read((uint)role, 0); var returns = read((uint)role, 1);
        var stores = Marshal.ReadInt32(source, DirectCallsOffset);
        var currentHealth = group.ReadSourceHealth();
        var rooted = Private<NativePlaybackStockRootedSource>(router, "_source");
        var otherGeneralLeaseAcquired = false; string? otherSpeedRejectedCode = null;
        using (var captured = provider.Capture(origin))
        using (var writer = captured.PreparePitchWrite())
        using (var prepared = NativePlaybackRateProviderAdoption.Prepare(provider, origin,
            new(GameplayControlAction.SetSpeed, binding.ControllerGeneration, binding.RunId, 2,
                GameplayControlValueUnit.PlaybackRateMultiplier), () => run.VerifyCurrent(binding)))
        {
            var entered = 0;
            setDirectBefore(observation =>
            {
                Check(observation.Role == role && observation.Self == source && observation.IncomingMethod == entry.MethodInfo
                    && observation.ArgBits == DirectBits(0) && read((uint)role, 0) == calls
                    && Marshal.ReadInt32(source, DirectCallsOffset) == stores
                    && observation.SchedulingSequence == currentHealth.SchedulingSequence + 1,
                    "First current direct Before did not precede the actual original/store with exact U64 and fence.");
                Check(finalClock.Capture().Phase == NativePlaybackPhase.Withdrawn && !group.GetTimeLease.ReadCurrent().Active
                    && !provider.Probe().Supported && !lease.Execute(Request(3)).Success,
                    "First current direct Before retained old Clock/publication/native rate or the original SDK speed authority.");
                Refuse(() => { _ = rooted.Current; }, "First current direct Before retained the old rooted source.");
                Refuse(captured.VerifyAuthority, "First current direct Before retained the independently captured source.");
                Refuse(writer.VerifyCurrent, "First current direct Before retained its continuous writer.");
                Refuse(prepared.Commit, "First current direct Before committed a previously prepared speed change.");
                prepared.Restore();
                // Acquire grants general operation ownership. A freshly issued
                // generic lease may exist after native rate has withdrawn, but
                // it must have no old native descriptor or supported speed call.
                var pitchCallsBeforeAcquire = Marshal.ReadInt32(source, 20);
                var acquired = other.Acquire(true);
                otherGeneralLeaseAcquired = acquired.Success;
                if (acquired.Success)
                {
                    using var next = Need(acquired);
                    var rejected = next.Execute(new GameplayControlRequest(GameplayControlAction.SetSpeed,
                        next.ControllerGeneration, next.ObservedRunId, 3, GameplayControlValueUnit.PlaybackRateMultiplier));
                    otherSpeedRejectedCode = rejected.Error?.Code.ToString();
                    Check(!rejected.Success && Private<GameplaySourceIdentity?>(service, "_sourceIdentity") is { NativeDescriptor: null },
                        "A fresh general SDK lease recovered the first source's native descriptor or speed authority.");
                }
                Check(!group.GetTimeLease.ReadCurrent().Active && !provider.Probe().Supported
                    && finalClock.Capture().Phase == NativePlaybackPhase.Withdrawn
                    && Marshal.ReadInt32(source, 20) == pitchCallsBeforeAcquire,
                    "Other SDK acquisition or speed rejection restored publication or wrote native pitch before the original.");
                ++entered;
            });
            var configure = Export<ConfigureDirectBoundary>(native, "StockFixtureConfigureDirect");
            var boundaries = new List<uint>();
            string? boundaryFailure = null;
            DirectBoundary boundary = (self, actualRole, bits, incoming, stage) =>
            {
                try
                {
                    boundaries.Add(stage);
                    Check(self == source && actualRole == (uint)role && bits == DirectBits(0) && incoming == entry.MethodInfo
                        && !group.GetTimeLease.ReadCurrent().Active && finalClock.Capture().Phase == NativePlaybackPhase.Withdrawn
                        && Marshal.ReadInt32(source, DirectCallsOffset) == stores + (stage == 1 ? 0 : 1),
                        "First direct original changed raw arguments or restored old authority across its actual store.");
                }
                catch (Exception error) { boundaryFailure ??= error.ToString(); }
            };
            configure(0, boundary);
            var status = invoke(8, entry.Address, source, DirectBits(0), entry.MethodInfo, out code);
            configure(0, null); setDirectBefore(null); GC.KeepAlive(boundary);
            Check(status == 1 && code == 0 && entered == 1 && callbackFailure() is null && boundaryFailure is null
                && boundaries.SequenceEqual(new uint[] { 1, 2 })
                && read((uint)role, 0) == calls + 1 && read((uint)role, 1) == returns + 1
                && read((uint)role, 2) == unchecked((ulong)source.ToInt64()) && read((uint)role, 3) == DirectBits(0)
                && read((uint)role, 4) == unchecked((ulong)entry.MethodInfo.ToInt64())
                && Marshal.ReadInt32(source, DirectCallsOffset) == stores + 1,
                "First current direct original did not execute exactly once and physically drain: " + callbackFailure() + boundaryFailure);
        }
        Check(group.ReadSourceHealth() is { Known: true, InFlight: 0, Unknown: false }
            && group.ReadSourceHealth().SchedulingSequence == currentHealth.SchedulingSequence + 1
            && group.ReadSourceHealth().SourceSequence == currentHealth.SourceSequence
            && group.ReadSourceHealth().WriterSequence == currentHealth.WriterSequence
            && !group.GetTimeLease.ReadCurrent().Active && !provider.Probe().Supported
            && !lease.Execute(Request(3)).Success && uniqueFinalClock() && nativeReads() > 0,
            "First direct physical exit invented source/writer generation or recovered old SDK/Clock authority.");
        Check(physical == (group.Epoch, group.GetTimeLease.EntryAddress, group.GetTimeLease.OriginalAddress,
            group.SetPitchLease.EntryAddress) && broker.ReadActualPlaybackDobbyCallCounts() == (19, 19),
            "First direct source-open replaced the retained nineteen physical members.");
        lease.Dispose(); module.Release(); provider.PumpCompleted(); router.PumpCompleted();
        Check(router.RootsReleased && api.ActiveRoots == 0 && observations.All(value => value.IsReleased),
            "First direct source-open retained actual roots/observations after known physical exit.");
        Console.WriteLine(JsonSerializer.Serialize(new { mode = "direct-source-first", passed = true, checks = _checks,
            hostOnlyInitialCapture = true, hostOnlyDirectAudioCoverageClaimed = false,
            firstSourceOpen = true, actualRawAl1Al0 = true, independentPluginScopes = 2, sdkExecuted = true,
            allocationTemplateConstructorTransparent = true, sameClassForeignDirectCalls = 18,
            currentDirectRole = role.ToString(), currentDirectBeforeStoreRevokes = true, uniqueFinalClock = uniqueFinalClock(),
            otherGeneralLeaseAcquired, otherSpeedRejectedCode, otherNativeSpeedRecovered = false,
            nativeReads = nativeReads(), initialCaptureProtocol = 1, initializationProtocol = 2,
            nativeSourceVersion = 4, playerWatchVersion = 2, physicalSources = 18,
            actualPrepare = 19, actualCommit = 19, retainedRoots = api.ActiveRoots,
            gameExecuted = false, productionAdmission = false }));
        return 0;
    }
}
