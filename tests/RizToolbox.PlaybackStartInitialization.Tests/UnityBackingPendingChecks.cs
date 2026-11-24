using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    // This interrupts an already constructed seven-root successor after native
    // admission phase two. An earlier API-sampling failure cannot satisfy it.
    private static int RunUnityBackingPending(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && Hash(arguments[1]) == PlaybackSourceGroupTargets.UnityBackingFixtureSha256
                && mode is "unity-backing-pending-recover" or "unity-backing-pending-source-recover",
                "Pending backing recovery requires its exact owned Unity profile and mode.");
            using var native = new ConstructorContext(arguments);
            long journalRecorded = 0; var journalApplied = 0;
            using (var sdk = new ConstructorSdk(native))
            {
                var journalView = Private<IPluginContext>(sdk.Lease, "Context")
                    .GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service;
                Check(journalView is not null, "The pending Unity-backed SDK plugin did not receive its formal read-only journal service.");
                var journal = Need(journalView!.Read());
                Check(journal.Counters == sdk.Journal.Counters && journal.Counters.Recorded == 0
                    && !journal.Truncated && journal.Records.Count == 0,
                    "The pending Unity-backed SDK journal did not start with its actual empty record baseline.");
                sdk.Speed(1.5);
                journal = CheckUnityBackingSpeedJournal(sdk, journalView, journal, sdk.Lease);
                Check(!sdk.Lease.Execute(sdk.Request(0)).Success,
                    "The formal pending Unity-backed SDK admitted an invalid zero playback-rate write.");
                CheckUnityBackingJournalUnchanged(sdk, journalView, journal);
                var physical = (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                    native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress);
                var oldClock = native.Clock;
                var make = Export<CreateStart>(native.Module, "StockFixtureCreateStart");
                var invoke = Export<InvokeStart>(native.Module, "StockFixtureInvokeStart");
                IntPtr Enumerator()
                {
                    var value = make(native.Api.GameplayProgress, native.Api.GameplayClip);
                    native.Api.RegisterUnityEnumerator(value); return value;
                }
                uint Start(IntPtr value)
                {
                    Check(invoke(native.Targets.ProgressStartMoveNext.Address, value, native.Targets.ProgressStartMoveNext.MethodInfo,
                        out var raw, out var error) == 1 && error == 0,
                        "Actual pending-recovery Start failed: " + native.Router.LastFailure);
                    return raw;
                }
                void CompleteActualStart(IntPtr value)
                {
                    var priorPlayer = native.Api.GameplayPlayer;
                    Check(Start(value) == 1 && Start(value) == 0
                        && Marshal.ReadIntPtr(native.Api.GameplaySource, native.SourceField(40)) == native.Api.GameplayClip,
                        "The actual yielded/final Start did not consume its real resource and set_clip chain.");
                    native.Api.AdoptUnityInitializedPlayer(); native.Api.AdoptUnityClip(native.Api.GameplayClip);
                    Check(native.Api.GameplayPlayer != priorPlayer, "Actual Start did not allocate a distinct owned player.");
                }
                CompleteActualStart(Enumerator());
                var originalTracker = Private<NativePlaybackInitializationTracker>(native.Router, "_initialization");
                var hub = Private<object>(Private<object>(native.Group, "_owner"), "_hub");
                var watches = Private<ICollection>(hub, "_unityBackingWatches");
                var priorWatches = ((IEnumerable)watches).Cast<object>().ToHashSet(ReferenceEqualityComparer.Instance);
                var originalCalls = native.Api.UnityGetTimeOriginalCalls;
                var clockReads = native.ClockReads;
                Task<float>? blocked = null;
                native.Api.DuringHandleTarget = target =>
                {
                    // A predecessor can retire while its successor stages, so the
                    // list count is not a receipt identity. Unity staging samples
                    // its source/clip roots before source-open; Progress may only
                    // be sampled after open and is too late for this blocker.
                    if (blocked is not null || target != native.Api.GameplaySource && target != native.Api.GameplayClip
                        || !((IEnumerable)watches).Cast<object>().Any(watch => !priorWatches.Contains(watch))) return;
                    Export<VoidNative>(native.Module, "StockFixtureBlockGetTime")();
                    blocked = Task.Run(() => native.Time(native.Api.GameplayPlayer, native.Targets.GetTime.MethodInfo));
                    Check(Export<WaitNative>(native.Module, "StockFixtureWaitGetTime")() == 0,
                        "The actual closed-policy GetTime original did not reach its owned wait.");
                };
                NativePlaybackStockRootedSource priorCandidate;
                NativePlaybackSingleReturnBridge.LogicalHandoff priorHandoff;
                try
                {
                    Check(!native.Router.TryCompleteInitialization() && blocked is not null
                        && native.Group.ReadInitialization() is { Phase: 6, Blocked: 1 }
                        && native.Group.GetTimeLease.ReadActivity().InFlight == 1,
                        "The pending test did not hold source-open behind a real GetTime borrower after native admission.");
                    var admission = Private<object>(native.Router, "_initializationHandoff");
                    priorCandidate = Private<NativePlaybackStockRootedSource>(admission, "_successor");
                    priorHandoff = Private<NativePlaybackSingleReturnBridge.LogicalHandoff>(admission, "_native");
                    var actual = Private<NativePlaybackActualSource>(priorCandidate, "_actual");
                    Check(priorHandoff.Read(2).Phase == 2 && ((IEnumerable)watches).Cast<object>().Count(watch => !priorWatches.Contains(watch)) == 1
                        && Private<List<NativeRuntime.RootHandle>>(actual, "_roots").Count == 7
                        && ReferenceEquals(Private<object>(native.Router, "_source"), priorCandidate) && !priorCandidate.RootsReleased,
                        "The interrupted candidate had not completed its actual seven-root construction and pending native watch.");
                    var pending = ((IEnumerable)watches).Cast<object>().Single(watch =>
                        ReferenceEquals(watch.GetType().GetProperty("SourceOwner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(watch), priorCandidate));
                    object ReadPending() => pending.GetType().GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pending, null)!;
                    Check(Private<uint>(ReadPending(), "Phase") == 1,
                        "The successor's actual Unity backing receipt was not pending before interruption.");
                    var before = native.Group.ReadSourceHealth();
                    var source = mode == "unity-backing-pending-source-recover";
                    Check(source ? native.Api.MutateUnitySource(2, 743) : native.Api.MutateUnityClip(2, -744),
                        "The pending wrapper did not obtain a separately allocated replacement native backing.");
                    Refuse(native.Group.RequireFreshAdmission, "A pending stale Unity identity granted fresh source admission.");
                    var after = native.Group.ReadSourceHealth();
                    Check(originalTracker.BackingCaptureInvalidated && !originalTracker.ResourceCaptureInvalidated
                        && Private<uint>(ReadPending(), "Phase") == 3
                        && native.Group.ReadInitialization() is { Phase: 5, Blocked: 1 }
                        && after is { Known: true, Unknown: false }
                        && after.SourceSequence == before.SourceSequence + 1 && after.WriterSequence == before.WriterSequence,
                        "The actual pending backing change did not publish its orthogonal initializer badge and one recognized source boundary.");
                    Check(!priorCandidate.RootsReleased && native.Api.UnityGetTimeOriginalCalls == originalCalls + 1
                        && native.ClockReads == clockReads && !native.Group.GetTimeLease.ReadCurrent().Active,
                        "A pending interruption dropped roots, consumed a Clock, or changed the original GetTime call count.");
                }
                finally
                {
                    native.Api.DuringHandleTarget = null;
                    Export<VoidNative>(native.Module, "StockFixtureReleaseGetTime")();
                    if (blocked is not null) Check(blocked.Wait(10000) && float.IsFinite(blocked.Result),
                        "The actual GetTime borrower did not physically return after the late interruption.");
                }
                native.Router.PumpCompleted();
                Check(native.Router.InitializationPending && !priorCandidate.RootsReleased
                    && ReferenceEquals(Private<object>(native.Router, "_resourceRetryCandidate"), priorCandidate)
                    && Private<bool>(native.Router, "_backingInitializationRetry"),
                    "The drained initializer lost its authenticated backing retry and retained pending candidate.");
                Refuse(() => originalTracker.CaptureCompleted(), "The interrupted initializer completion revived its old backing identity.");
                CheckUnityBackingWithdrawalViews(native, sdk);
                CheckUnityBackingJournalUnchanged(sdk, journalView, journal);
                CompleteActualStart(Enumerator());
                Check(native.Router.TryCompleteInitialization() && !native.Router.InitializationPending
                    && priorCandidate.RootsReleased && native.Group.ReadInitialization() is { Phase: 7, Blocked: 0 }
                    && !native.Group.GetTimeLease.ReadCurrent().Active,
                    "A fresh actual Start could not replace the invalidated phase-two candidate: " + native.Router.LastFailure);
                Refuse(() => priorHandoff.TryOpenSource(), "The superseded pending native receipt reopened the retired source.");
                Refuse(() => native.Router.PreparePausedClock(), "Initializer retry substituted for a later actual stock Pause.");
                native.Arm();
                Check(!ReferenceEquals(native.Clock, oldClock) && native.Clock.Capture().Binding.RunId == oldClock.Capture().Binding.RunId
                    && native.Clock.Capture().Binding.SourceGeneration > oldClock.Capture().Binding.SourceGeneration
                    && sdk.Provider.Probe().Supported && sdk.Module.Tick().Ready,
                    "The later actual Pause/readback did not restore one successor Clock in the existing formal module.");
                using (var lease = Need(sdk.Controls.Acquire(true)))
                {
                    Check(lease.ControllerGeneration > sdk.Lease.ControllerGeneration && !sdk.Other.Acquire(true).Success,
                        "Pending recovery did not expire the old plugin lease or preserve exclusive ownership.");
                    CheckUnityBackingJournalUnchanged(sdk, journalView, journal);
                    var calls = Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(20));
                    var writerSequence = native.Group.ReadSourceHealth().WriterSequence;
                    var request = new GameplayControlRequest(GameplayControlAction.SetSpeed, lease.ControllerGeneration,
                        lease.ObservedRunId, 2, GameplayControlValueUnit.PlaybackRateMultiplier);
                    var result = Need(lease.Execute(request));
                    Check(result.Modification.TransactionState == GameplayControlTransactionState.Confirmed && result.Modification.ConfirmedValue == 2
                        && native.Clock.Capture().Rate == 2
                        && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(16))) == 2
                        && Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(20)) == calls + 1
                        && native.Group.ReadSourceHealth().WriterSequence == writerSequence + 1
                        && sdk.ClockView.Read().Success && sdk.OtherClock.Read().Success,
                        "The existing dual-plugin SDK did not resume an exactly-once native pitch write and unique successor Clock.");
                    journal = CheckUnityBackingSpeedJournal(sdk, journalView, journal, lease);
                }
                priorCandidate.Dispose(); priorCandidate.PumpCompleted(); originalTracker.Dispose();
                Check(native.Group.GetTimeLease.ReadCurrent().Active && native.UniqueFinalClock && native.CallbackFailure is null
                    && physical == (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                        native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress)
                    && native.Broker.ReadActualPlaybackDobbyCallCounts() == (20, 20)
                    && native.Group.ReadActivity() is { Known: true, InFlight: 0 },
                    "Late cleanup cleared the replacement watch, replaced the physical hooks, or lost known responsibility.");
                CheckUnityBackingJournalUnchanged(sdk, journalView, journal);
                journalRecorded = sdk.Journal.Counters.Recorded;
                journalApplied = journal.Records.Count(record => record.Outcome == RunModificationOutcome.Applied);
            }
            native.Router.PumpCompleted(); native.Group.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(value => value.IsReleased),
                "Known pending recovery left real NativeRuntime roots or physical observation duties behind.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPhase6Interruption = true, pendingNativePhase = 1, invalidatedNativePhase = 3,
                actualAdmittedSevenRootCandidate = true, orthogonalBackingInvalidation = true,
                sdkExecuted = true, resumedAfterFreshActualStart = true, supersededReceiptRejected = true,
                journalRecorded, journalApplied,
                actualPrepare = 20, actualCommit = 20, retainedRoots = native.Api.ActiveRoots,
                gameExecuted = false, installed = false, productionAdmission = false, fullGenerationCompleted = false }));
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
