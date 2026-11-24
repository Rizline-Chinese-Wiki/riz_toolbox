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
    // There is no Clock or SDK lease before this host-only first capture.
    // Its old InitialCapture receipt, rather than a predecessor publication,
    // must expire when the staged clip retains its pointer but changes ID.
    private static int RunUnityBackingFirstPending(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && mode == "unity-backing-first-pending-id-recover"
                && Hash(arguments[1]) == PlaybackSourceGroupTargets.UnityBackingFixtureSha256,
                "First pending backing recovery requires its exact owned Unity profile and mode.");
            using var native = new ConstructorContext(arguments, hostOnly: true);
            Check(native.Targets.HasUnityBacking && native.PhysicalMemberCount == 20
                && native.Api.ActiveRoots == 4 && Private<object?>(native.Router, "_source") is null
                && !native.Group.GetTimeLease.ReadCurrent().Active && native.ClockReads == 0,
                "The first pending test did not begin with only the four actual host roots and no Clock.");
            var physical = (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress);
            var make = Export<CreateStart>(native.Module, "StockFixtureCreateStart");
            var invoke = Export<InvokeStart>(native.Module, "StockFixtureInvokeStart");
            IntPtr Enumerator()
            {
                var value = make(native.Api.GameplayProgress, native.Api.GameplayClip);
                native.Api.RegisterUnityEnumerator(value); return value;
            }
            uint Start(IntPtr value)
            {
                Check(invoke(native.Targets.ProgressStartMoveNext.Address, value,
                    native.Targets.ProgressStartMoveNext.MethodInfo, out var raw, out var error) == 1 && error == 0,
                    "Actual host-only pending Start failed: " + native.Router.LastFailure);
                return raw;
            }
            void CompleteActualStart(IntPtr value)
            {
                var priorPlayer = native.Api.GameplayPlayer;
                Check(Start(value) == 1 && Start(value) == 0
                    && Marshal.ReadIntPtr(native.Api.GameplaySource, native.SourceField(40)) == native.Api.GameplayClip,
                    "The host-only actual Start did not execute its yielded/final resource and set_clip chain.");
                native.Api.AdoptUnityInitializedPlayer(); native.Api.AdoptUnityClip(native.Api.GameplayClip);
                Check(native.Api.GameplayPlayer != priorPlayer,
                    "The host-only actual Start did not allocate a distinct real player.");
            }
            CompleteActualStart(Enumerator());
            var originalTracker = Private<NativePlaybackInitializationTracker>(native.Router, "_initialization");
            var hub = Private<object>(Private<object>(native.Group, "_owner"), "_hub");
            var watches = Private<ICollection>(hub, "_unityBackingWatches");
            var watchCount = watches.Count;
            var originalCalls = native.Api.UnityGetTimeOriginalCalls;
            Task<float>? blocked = null;
            native.Api.DuringHandleTarget = target =>
            {
                // An early API-sampling rejection is insufficient: the actual
                // new watch and seven-root candidate must already exist.
                if (blocked is not null || target != native.Api.GameplayProgress || watches.Count <= watchCount) return;
                Export<VoidNative>(native.Module, "StockFixtureBlockGetTime")();
                blocked = Task.Run(() => native.Time(native.Api.GameplayPlayer, native.Targets.GetTime.MethodInfo));
                Check(Export<WaitNative>(native.Module, "StockFixtureWaitGetTime")() == 0,
                    "The first-capture raw GetTime original did not reach its owned wait.");
            };
            NativePlaybackStockRootedSource priorCandidate;
            NativePlaybackSingleReturnBridge.InitialCapture priorFirst;
            try
            {
                Check(!native.Router.TryCompleteInitialization() && blocked is not null
                    && native.Group.ReadInitialization() is { Phase: 6, Blocked: 1 }
                    && native.Group.GetTimeLease.ReadActivity().InFlight == 1,
                    "The first capture did not reach admitted phase six behind a real GetTime borrower.");
                var admission = Private<object>(native.Router, "_firstCaptureAdmission");
                priorCandidate = Private<NativePlaybackStockRootedSource>(admission, "_successor");
                priorFirst = Private<NativePlaybackSingleReturnBridge.InitialCapture>(admission, "_native");
                var actual = Private<NativePlaybackActualSource>(priorCandidate, "_actual");
                var roots = Private<List<NativeRuntime.RootHandle>>(actual, "_roots");
                Check(priorFirst.Read(2).Phase == 2 && watches.Count == watchCount + 1 && roots.Count == 7
                    && roots.Select(root => root.Target.ToInt64()).SequenceEqual(new[] {
                        native.Api.GameplayController.ToInt64(), native.Api.GameplayInformation.ToInt64(),
                        native.Api.GameplayProgress.ToInt64(), native.Origin.Pause, native.Api.GameplayPlayer.ToInt64(),
                        native.Api.GameplaySource.ToInt64(), native.Api.GameplayClip.ToInt64() })
                    && ReferenceEquals(Private<object>(native.Router, "_source"), priorCandidate)
                    && !priorCandidate.RootsReleased,
                    "The first admitted candidate did not own its exact seven actual roots and native receipt.");
                var pending = ((IEnumerable)watches).Cast<object>().Single(watch =>
                    ReferenceEquals(watch.GetType().GetProperty("SourceOwner", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(watch), priorCandidate));
                object ReadPending() => pending.GetType().GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(pending, null)!;
                Check(Private<uint>(ReadPending(), "Phase") == 1,
                    "The first candidate's actual backing watch was not staged before interruption.");
                var before = native.Group.ReadSourceHealth();
                var retainedRoots = native.Api.ActiveRoots;
                var clip = native.Api.GameplayClip; var clipClass = Marshal.ReadIntPtr(clip);
                var clipBacking = Marshal.ReadIntPtr(clip, 16); var clipId = Marshal.ReadInt32(clipBacking, 8);
                var sourceBacking = Marshal.ReadIntPtr(native.Api.GameplaySource, 16);
                Check(native.Api.MutateUnityClip(4, -944) && clipId != -944
                    && native.Api.GameplayClip == clip && Marshal.ReadIntPtr(clip) == clipClass
                    && Marshal.ReadIntPtr(clip, 16) == clipBacking && Marshal.ReadInt32(clipBacking, 8) == -944
                    && Marshal.ReadIntPtr(native.Api.GameplaySource, 16) == sourceBacking
                    && Marshal.ReadIntPtr(native.Api.GameplaySource, native.SourceField(40)) == clip,
                    "The first pending mutation did not change only the actual clip native Int32 ID at the same pointer.");
                Refuse(native.Group.RequireFreshAdmission,
                    "A first pending stale backing identity granted fresh source admission.");
                var after = native.Group.ReadSourceHealth();
                Check(originalTracker.BackingCaptureInvalidated && !originalTracker.ResourceCaptureInvalidated
                    && Private<uint>(ReadPending(), "Phase") == 3
                    && native.Group.ReadInitialization() is { Phase: 5, Blocked: 1 }
                    && after is { Known: true, Unknown: false }
                    && after.SourceSequence == before.SourceSequence + 1 && after.WriterSequence == before.WriterSequence
                    && roots.Count == 7 && !priorCandidate.RootsReleased && native.Api.ActiveRoots == retainedRoots
                    && native.Api.UnityGetTimeOriginalCalls == originalCalls + 1 && native.ClockReads == 0
                    && !native.Group.GetTimeLease.ReadCurrent().Active,
                    "The first ID-only loss failed to retain its seven roots, orthogonal retry badge, or one known source boundary.");
            }
            finally
            {
                native.Api.DuringHandleTarget = null;
                Export<VoidNative>(native.Module, "StockFixtureReleaseGetTime")();
                if (blocked is not null) Check(blocked.Wait(10000) && float.IsFinite(blocked.Result),
                    "The first pending GetTime borrower did not physically drain.");
            }
            native.Router.PumpCompleted();
            Check(native.Router.InitializationPending && !priorCandidate.RootsReleased
                && ReferenceEquals(Private<object>(native.Router, "_resourceRetryCandidate"), priorCandidate)
                && ReferenceEquals(Private<object>(native.Router, "_resourceRetryFirst"), priorFirst)
                && Private<object?>(native.Router, "_resourceRetryTransfer") is null
                && Private<bool>(native.Router, "_backingInitializationRetry"),
                "The drained first capture lost its exact backing retry and retained pending candidate.");
            Refuse(() => originalTracker.CaptureCompleted(),
                "The interrupted first initializer completion revived its old backing identity.");
            CompleteActualStart(Enumerator());
            Check(native.Router.TryCompleteInitialization() && !native.Router.InitializationPending
                && priorCandidate.RootsReleased && native.Api.ActiveRoots == 7
                && native.Group.ReadInitialization() is { Phase: 7, Blocked: 0 }
                && !native.Group.GetTimeLease.ReadCurrent().Active && native.ClockReads == 0,
                "A fresh actual Start failed to replace the first ID-invalidated candidate with a dormant seven-root source: "
                    + native.Router.LastFailure);
            Refuse(() => priorFirst.TryOpenSource(),
                "The superseded native InitialCapture receipt reopened its old first graph.");
            Refuse(() => native.Router.PreparePausedClock(),
                "First source-open substituted for a later actual stock Pause.");
            long journalRecorded = 0; var journalApplied = 0;
            using (var sdk = new ConstructorSdk(native))
            {
                // ConstructorSdk now performs the first actual Pause/readback
                // and publishes the first Clock before issuing either plugin.
                Check(native.UniqueFinalClock && native.ClockReads > 0 && native.Clock.Capture().Revision == 1
                    && native.Group.GetTimeLease.ReadCurrent().Active && sdk.Module.Tick().Ready,
                    "Recovered host-only capture did not establish exactly its first usable Pause/Clock/SDK route.");
                var journalView = Private<IPluginContext>(sdk.Lease, "Context")
                    .GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service;
                Check(journalView is not null,
                    "The recovered first SDK plugin did not receive the formal read-only journal.");
                var journal = Need(journalView!.Read());
                Check(journal.Counters == sdk.Journal.Counters && journal.Counters.Recorded == 0
                    && !journal.Truncated && journal.Records.Count == 0,
                    "The first recovered SDK journal did not begin at its actual empty baseline.");
                RunModificationJournalSnapshot Applied(RunModificationJournalSnapshot before, IGameplayControlLease lease)
                {
                    var after = Need(journalView!.Read());
                    Check(after.Counters == sdk.Journal.Counters && after.Counters.Recorded == before.Counters.Recorded + 1
                        && !after.Truncated && after.Records.Count == before.Records.Count + 1
                        && after.Records.Take(before.Records.Count).SequenceEqual(before.Records)
                        && after.Records[^1] is { Outcome: RunModificationOutcome.Applied,
                            Category: RunModificationCategory.GameplayControl, Source: "riz.gameplay.controls", Operation: "SetSpeed" }
                        && after.Records[^1].Owner == Private<IPluginContext>(lease, "Context").PluginId
                        && after.Records[^1].TargetId == "gameplay.controller/" + lease.ControllerGeneration,
                        "A first-recovered SDK speed failed to append exactly one actual Applied record for its plugin/controller.");
                    return after;
                }
                sdk.Speed(1.5); journal = Applied(journal, sdk.Lease);
                // Host-only interruption had no predecessor SDK lease. This
                // tests an explicitly released SDK lease after recovery only.
                sdk.Lease.Dispose();
                var calls = Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(20));
                var health = native.Group.ReadSourceHealth();
                Check(!sdk.Lease.Execute(sdk.Request(3)).Success
                    && Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(20)) == calls
                    && native.Group.ReadSourceHealth() == health,
                    "An explicitly released first SDK lease still wrote native pitch or changed playback fences.");
                var refused = Need(journalView!.Read());
                Check(refused.Counters == sdk.Journal.Counters && refused.Counters == journal.Counters
                    && !refused.Truncated && refused.Records.SequenceEqual(journal.Records),
                    "The expired first SDK lease appended a journal record before any commit.");
                using (var lease = Need(sdk.Other.Acquire(true)))
                {
                    Check(!sdk.Controls.Acquire(true).Success,
                        "The second recovered plugin did not retain exclusive actual control.");
                    var request = new GameplayControlRequest(GameplayControlAction.SetSpeed, lease.ControllerGeneration,
                        lease.ObservedRunId, 2, GameplayControlValueUnit.PlaybackRateMultiplier);
                    var result = Need(lease.Execute(request));
                    Check(result.Modification.TransactionState == GameplayControlTransactionState.Confirmed
                        && result.Modification.ConfirmedValue == 2 && native.Clock.Capture().Rate == 2
                        && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(16))) == 2
                        && Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(20)) == calls + 1
                        && native.Group.ReadSourceHealth().WriterSequence == health.WriterSequence + 1
                        && sdk.ClockView.Read().Success && sdk.OtherClock.Read().Success,
                        "The recovered second formal plugin failed its exactly-once native pitch/Clock transaction.");
                    journal = Applied(journal, lease);
                }
                priorCandidate.Dispose(); priorCandidate.PumpCompleted(); originalTracker.Dispose();
                Check(native.Group.GetTimeLease.ReadCurrent().Active && native.UniqueFinalClock
                    && native.CallbackFailure is null && native.Api.ActiveRoots == 14
                    && physical == (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                        native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress)
                    && native.Broker.ReadActualPlaybackDobbyCallCounts() == (20, 20)
                    && native.Group.ReadActivity() is { Known: true, InFlight: 0 },
                    "Late first-candidate cleanup cleared the successor watch or changed its actual physical owner.");
                journalRecorded = sdk.Journal.Counters.Recorded;
                journalApplied = journal.Records.Count(record => record.Outcome == RunModificationOutcome.Applied);
                Check(journalRecorded == 2 && journalApplied == 2,
                    "First-recovered SDK did not retain exactly the two actual confirmed speed records.");
            }
            native.Router.PumpCompleted(); native.Group.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0
                && native.Observations.All(value => value.IsReleased),
                "Known first-pending recovery retained actual roots or physical observation duties.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                hostOnlyInitialRoots = 4, actualAdmittedSevenRootCandidate = true, actualPhase6Interruption = true,
                pendingNativePhase = 1, invalidatedNativePhase = 3, samePointerDifferentNativeId = true,
                orthogonalBackingInvalidation = true, initialCaptureRetry = true, supersededReceiptRejected = true,
                sdkExecuted = true, independentPluginScopes = 2, firstClockAfterActualPause = true,
                expiredSdkLeaseAfterExplicitRelease = true, actualPrepare = 20, actualCommit = 20,
                retainedRoots = native.Api.ActiveRoots, journalRecorded, journalApplied,
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
