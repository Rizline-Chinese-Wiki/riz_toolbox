using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    private static RunModificationJournalSnapshot CheckUnityBackingSpeedJournal(ConstructorSdk sdk,
        IRunModificationService view, RunModificationJournalSnapshot before, IGameplayControlLease lease)
    {
        var after = Need(view.Read());
        Check(after.Counters == sdk.Journal.Counters && after.Counters.Recorded == before.Counters.Recorded + 1
            && !after.Truncated && after.Records.Count == before.Records.Count + 1
            && after.Records.Take(before.Records.Count).SequenceEqual(before.Records)
            && after.Records[^1] is { Outcome: RunModificationOutcome.Applied,
                Category: RunModificationCategory.GameplayControl, Source: "riz.gameplay.controls", Operation: "SetSpeed" }
            && after.Records[^1].Owner == Private<IPluginContext>(lease, "Context").PluginId
            && after.Records[^1].TargetId == "gameplay.controller/" + lease.ControllerGeneration,
            "A confirmed Unity-backed SDK speed did not append exactly one actual Applied journal record for its plugin/controller.");
        return after;
    }

    private static void CheckUnityBackingJournalUnchanged(ConstructorSdk sdk, IRunModificationService view,
        RunModificationJournalSnapshot before)
    {
        var after = Need(view.Read());
        Check(after.Counters == sdk.Journal.Counters && after.Counters == before.Counters
            && !after.Truncated && after.Records.SequenceEqual(before.Records),
            "A pre-commit refused Unity-backed write changed actual journal counters or appended an Applied/other record.");
    }

    // Public clock reads expose independent game/DSP/host observations. They
    // do not grant a native rate descriptor or revive a predecessor lease.
    private static void CheckUnityBackingWithdrawalViews(ConstructorContext native, ConstructorSdk sdk)
    {
        var providerSupported = sdk.Provider.Probe().Supported;
        var first = Need(sdk.ClockView.Read());
        var second = Need(sdk.OtherClock.Read());
        var identity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
        sdk.SampleControls();
        var state = Need(sdk.Controls.GetState());
        var speedSupported = state.Operations.Single(operation => operation.Action == GameplayControlAction.SetSpeed).Supported;
        var oldLease = sdk.Lease.Execute(sdk.Request(3));
        Console.WriteLine(JsonSerializer.Serialize(new { diagnostic = "withdrawn-native-authority-independent-clock-views",
            firstGeneration = first.ControllerGeneration, secondGeneration = second.ControllerGeneration,
            predecessorGeneration = sdk.Lease.ControllerGeneration,
            nativeDescriptor = identity?.NativeDescriptor is not null, providerSupported, speedSupported,
            oldLeaseSucceeded = oldLease.Success, oldLeaseError = oldLease.Error?.Code.ToString(),
            actualNativePolicyActive = native.Group.GetTimeLease.ReadCurrent().Active }));
        // Losing only optional native availability expires control ownership;
        // the shared independent graph may keep its controller generation.
        Check(first.ControllerGeneration == second.ControllerGeneration
            && first.ControllerGeneration >= sdk.Lease.ControllerGeneration
            && first.AudioDspTime.Available && second.AudioDspTime.Available
            && first.HostMonotonicTime.Available && second.HostMonotonicTime.Available
            && identity is { NativeDescriptor: null } && !providerSupported && !speedSupported
            && !oldLease.Success && oldLease.Error?.Code == ErrorCode.TargetExpired
            && !native.Group.GetTimeLease.ReadCurrent().Active,
            "An independent public clock view revived the retired native descriptor, rate operation, policy or predecessor lease.");
    }

    private static int RunUnityBacking(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && Hash(arguments[1]) == PlaybackSourceGroupTargets.UnityBackingFixtureSha256
                && mode is "unity-backing-sdk-stable" or "unity-backing-source-recover" or "unity-backing-clip-recover"
                    or "unity-backing-source-id-recover" or "unity-backing-clip-id-recover",
                "Unity backing validation requires its separately frozen twenty-member owned image and exact mode.");
            using var native = new ConstructorContext(arguments);
            Check(native.Targets.HasUnityBacking && native.Targets.HasProgressResource && native.PhysicalMemberCount == 20,
                "The Unity-backed profile did not require the source/clip watch and complete physical source group.");
            long journalRecorded = 0; var journalApplied = 0;
            using (var sdk = new ConstructorSdk(native))
            {
                var journalView = Private<IPluginContext>(sdk.Lease, "Context")
                    .GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service;
                Check(journalView is not null, "The actual Unity-backed SDK plugin did not receive its formal read-only journal service.");
                var journal = Need(journalView!.Read());
                Check(journal.Counters == sdk.Journal.Counters && journal.Counters.Recorded == 0
                    && !journal.Truncated && journal.Records.Count == 0,
                    "The Unity-backed SDK journal did not start with its actual empty record baseline.");
                sdk.Speed(1.5);
                journal = CheckUnityBackingSpeedJournal(sdk, journalView, journal, sdk.Lease);
                Check(!sdk.Lease.Execute(sdk.Request(0)).Success,
                    "The formal Unity-backed SDK admitted an invalid zero playback-rate write.");
                CheckUnityBackingJournalUnchanged(sdk, journalView, journal);
                var oldRooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
                var physical = (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                    native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress);
                if (mode != "unity-backing-sdk-stable")
                {
                    var oldClock = native.Clock;
                    var oldPlayer = native.Api.GameplayPlayer;
                    var sourceBefore = native.Group.ReadSourceHealth();
                    using (var capture = sdk.Provider.Capture(native.Origin))
                    using (var prepared = DirectSdkPrepare(native, sdk))
                    {
                        // The prepared transaction owns the source's sole
                        // actual writer. Inspect that same reservation rather
                        // than creating a conflicting second prepared writer.
                        var writer = Private<NativePlaybackStockRootedSource.PitchWrite>(oldRooted, "_pitchWriter");
                        var nativeReads = native.ClockReads;
                        var originalCalls = native.Api.UnityGetTimeOriginalCalls;
                        var expectedOriginal = (float)BitConverter.Int64BitsToDouble(
                            Marshal.ReadInt64(native.Api.GameplaySource, native.SourceField(24)));
                        var clip = mode is "unity-backing-clip-recover" or "unity-backing-clip-id-recover";
                        var idOnly = mode is "unity-backing-source-id-recover" or "unity-backing-clip-id-recover";
                        var wrapper = clip ? native.Api.GameplayClip : native.Api.GameplaySource;
                        var backingBefore = Marshal.ReadIntPtr(wrapper, 16);
                        var idBefore = Marshal.ReadInt32(backingBefore, 8); // Exact owned fixture's initialized ID offset.
                        Check(clip ? native.Api.MutateUnityClip(idOnly ? 4u : 2u, -442)
                            : native.Api.MutateUnitySource(idOnly ? 4u : 2u, 441),
                            "The owned fixture did not change its actual independent native backing identity.");
                        var backingAfter = Marshal.ReadIntPtr(wrapper, 16);
                        Check(backingAfter != IntPtr.Zero && (idOnly ? backingAfter == backingBefore : backingAfter != backingBefore)
                            && Marshal.ReadInt32(backingAfter, 8) == (clip ? -442 : 441)
                            && Marshal.ReadInt32(backingAfter, 8) != idBefore,
                            "The owned mutation did not preserve/change the actual pointer and native ID as required by this mode.");
                        // Deliberately no Pump, readback, provider probe or source
                        // observation occurs between mutation and real detoured GetTime.
                        var result = native.Time(oldPlayer, native.Targets.GetTime.MethodInfo);
                        Check(result == expectedOriginal && native.Api.UnityGetTimeOriginalCalls == originalCalls + 1
                            && native.Api.UnityLastGetTimeMethod == native.Targets.GetTime.MethodInfo
                            && native.ClockReads == nativeReads && !native.Group.GetTimeLease.ReadCurrent().Active,
                            "GetTime used the retired Clock or lost its exact once-only original before the owner pump.");
                        var changed = native.Group.ReadSourceHealth();
                        Check(changed is { Known: true, Unknown: false, InFlight: 0 }
                            && changed.SourceSequence == sourceBefore.SourceSequence + 1
                            && changed.WriterSequence == sourceBefore.WriterSequence
                            && native.Group.PollOperationNativeFaults() is null,
                            "A normal Unity lifetime boundary poisoned its physical owner or failed to advance source continuity once.");
                        Refuse(capture.VerifyAuthority, "An old provider capture survived a changed Unity backing.");
                        Refuse(writer.VerifyCurrent, "An old pitch reservation survived a changed Unity backing.");
                        Refuse(prepared.Commit, "An old prepared SDK transaction wrote into the replacement Unity object.");
                        CheckUnityBackingWithdrawalViews(native, sdk);
                        CheckUnityBackingJournalUnchanged(sdk, journalView, journal);
                    }

                    var start = native.Targets.ProgressStartMoveNext;
                    var enumerator = Export<CreateStart>(native.Module, "StockFixtureCreateStart")(
                        native.Api.GameplayProgress, native.Api.GameplayClip);
                    native.Api.RegisterUnityEnumerator(enumerator);
                    var invoke = Export<InvokeStart>(native.Module, "StockFixtureInvokeStart");
                    uint Start()
                    {
                        Check(invoke(start.Address, enumerator, start.MethodInfo, out var raw, out var code) == 1 && code == 0,
                            "Actual Start failed while recovering a recognized backing boundary: " + native.Router.LastFailure);
                        return raw;
                    }
                    Check(Start() == 1 && native.Router.InitializationPending
                        && native.Group.ReadInitialization() is { Phase: 2, Blocked: 1 },
                        "The real initializer did not retain its yield and fresh-source block.");
                    Check(!native.Router.TryCompleteInitialization(), "A yielding initializer reopened a retired backing identity.");
                    Check(Start() == 0 && native.Group.ReadInitialization() is { Phase: 4, Blocked: 1 },
                        "The actual final Start did not produce its complete initialization occurrence.");
                    native.Api.AdoptUnityInitializedPlayer();
                    Check(native.Api.GameplayPlayer != oldPlayer && native.Router.TryCompleteInitialization()
                        && native.Group.ReadInitialization() is { Phase: 7, Blocked: 0 }
                        && !native.Group.GetTimeLease.ReadCurrent().Active,
                        "A fresh initialized source could not replace its retired backing watch in a dormant state: " + native.Router.LastFailure);
                    Refuse(() => native.Router.PreparePausedClock(), "Source-open was incorrectly treated as an actual stock Pause.");
                    native.Arm();
                    Check(!ReferenceEquals(native.Clock, oldClock) && native.Clock.Capture().Binding.RunId == oldClock.Capture().Binding.RunId
                        && native.Clock.Capture().Binding.SourceGeneration > oldClock.Capture().Binding.SourceGeneration
                        && sdk.Provider.Probe().Supported && sdk.Module.Tick().Ready,
                        "The later actual Pause did not establish the unique successor Clock for the existing formal provider.");
                    using var lease = Need(sdk.Controls.Acquire(true));
                    var request = new GameplayControlRequest(GameplayControlAction.SetSpeed, lease.ControllerGeneration,
                        lease.ObservedRunId, 2, GameplayControlValueUnit.PlaybackRateMultiplier);
                    Check(lease.ControllerGeneration > sdk.Lease.ControllerGeneration && !sdk.Lease.Execute(sdk.Request(3)).Success
                        && !sdk.Other.Acquire(true).Success,
                        "The old plugin control lease survived source recapture or exclusive ownership was lost.");
                    CheckUnityBackingJournalUnchanged(sdk, journalView, journal);
                    var callsBefore = Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(20));
                    var speed = Need(lease.Execute(request));
                    Check(speed.Modification.TransactionState == GameplayControlTransactionState.Confirmed
                        && speed.Modification.ConfirmedValue == 2 && native.Clock.Capture().Rate == 2
                        && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(16))) == 2
                        && Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(20)) == callsBefore + 1
                        && sdk.ClockView.Read().Success && sdk.OtherClock.Read().Success,
                        "The original dual-plugin SDK did not resume an actual once-only pitch write and successor Clock after recapture.");
                    journal = CheckUnityBackingSpeedJournal(sdk, journalView, journal, lease);
                }
                Check(physical == (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                        native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress)
                    && native.Broker.ReadActualPlaybackDobbyCallCounts() == (20, 20)
                    && native.Group.ReadActivity() is { Known: true, InFlight: 0 } && native.CallbackFailure is null,
                    "Unity backing recapture replaced physical hooks, retained a borrow, or lost known responsibility.");
                CheckUnityBackingJournalUnchanged(sdk, journalView, journal);
                journalRecorded = sdk.Journal.Counters.Recorded;
                journalApplied = journal.Records.Count(record => record.Outcome == RunModificationOutcome.Applied);
            }
            native.Router.PumpCompleted(); native.Group.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(value => value.IsReleased),
                "Known Unity backing lifecycle did not release its actual roots and physical observation duties.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 20, actualCommit = 20, actualDetouredGetTime = true, actualNativeBackingRead = true,
                ownerPumpBeforeRevocation = false, sdkExecuted = true, recoveredAfterActualStart = mode != "unity-backing-sdk-stable",
                samePointerIdChanged = mode is "unity-backing-source-id-recover" or "unity-backing-clip-id-recover",
                journalRecorded, journalApplied,
                retainedRoots = native.Api.ActiveRoots, gameExecuted = false, installed = false,
                productionAdmission = false, fullGenerationCompleted = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
    }
}
