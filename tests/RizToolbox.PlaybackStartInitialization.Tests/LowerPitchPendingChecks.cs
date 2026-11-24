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
    private static int RunLowerPitchPending(string[] a, string mode)
    {
        try
        {
            var first = mode == "lower-pitch-first-pending";
            Check(a.Length == 5 && mode is "lower-pitch-first-pending" or "lower-pitch-transfer-pending"
                && Hash(a[1]) == NativePlaybackLowerPitchTarget.OwnedWrapperImageSha256,
                "Lower pending recovery requires its exact two-image profile and mode.");
            using var native = new ConstructorContext(a, hostOnly: first);
            Check(native.PhysicalMemberCount == 21 && native.Api.ActiveRoots == (first ? 4 : 7),
                "The actual initial route did not retain exactly its host or source roots.");
            if (!first) native.Arm();
            var physical = (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress);
            LowerActualStart(native);
            var tracker = Private<NativePlaybackInitializationTracker>(native.Router, "_initialization");
            var hub = Private<object>(Private<object>(native.Group, "_owner"), "_hub");
            var watches = Private<ICollection>(hub, "_unityBackingWatches");
            var previous = ((IEnumerable)watches).Cast<object>().ToHashSet(ReferenceEqualityComparer.Instance);
            var originalCalls = native.Api.UnityGetTimeOriginalCalls;
            var clockReads = native.ClockReads;
            Task<float>? blocked = null;
            native.Api.DuringHandleTarget = target =>
            {
                // Identity of the newly registered receipt is required. A
                // source/clip read is early enough to hold real phase six;
                // aggregate count or a later Progress read is insufficient.
                if (blocked is not null || target != native.Api.GameplaySource && target != native.Api.GameplayClip
                    || !((IEnumerable)watches).Cast<object>().Any(watch => !previous.Contains(watch))) return;
                Export<VoidNative>(native.Module, "StockFixtureBlockGetTime")();
                blocked = Task.Run(() => native.Time(native.Api.GameplayPlayer, native.Targets.GetTime.MethodInfo));
                Check(Export<WaitNative>(native.Module, "StockFixtureWaitGetTime")() == 0,
                    "The phase-six lower test did not reach the actual waiting GetTime original.");
            };
            NativePlaybackStockRootedSource candidate;
            NativePlaybackSingleReturnBridge.InitialCapture? priorFirst = null;
            NativePlaybackSingleReturnBridge.LogicalHandoff? priorTransfer = null;
            try
            {
                Check(!native.Router.TryCompleteInitialization() && blocked is not null
                    && native.Group.ReadInitialization() is { Phase: 6, Blocked: 1 }
                    && native.Group.GetTimeLease.ReadActivity().InFlight == 1,
                    "The lower interruption did not hold a genuine admitted phase-six candidate.");
                var admission = Private<object>(native.Router, first ? "_firstCaptureAdmission" : "_initializationHandoff");
                candidate = Private<NativePlaybackStockRootedSource>(admission, "_successor");
                if (first)
                {
                    priorFirst = Private<NativePlaybackSingleReturnBridge.InitialCapture>(admission, "_native");
                    Check(priorFirst.Read(2).Phase == 2, "The first lower candidate had no actual admitted InitialCapture.");
                }
                else
                {
                    priorTransfer = Private<NativePlaybackSingleReturnBridge.LogicalHandoff>(admission, "_native");
                    Check(priorTransfer.Read(2).Phase == 2, "The transferred lower candidate had no actual admitted handoff.");
                }
                var actual = Private<NativePlaybackActualSource>(candidate, "_actual");
                var roots = Private<List<NativeRuntime.RootHandle>>(actual, "_roots");
                Check(roots.Count == 7 && !candidate.RootsReleased
                    && ReferenceEquals(Private<object>(native.Router, "_source"), candidate)
                    && ((IEnumerable)watches).Cast<object>().Count(watch => !previous.Contains(watch)) == 1,
                    "The phase-six interruption occurred before actual seven-root candidate construction.");
                var lower = Marshal.GetDelegateForFunctionPointer<LowerSetPitch>(
                    native.Targets.SourceTarget(NativePlaybackSourceRole.LowerSetPitch).Address);
                var read = Export<LowerFixtureRead>(native.LowerModule, "StockLowerFixtureRead");
                var before = native.Group.ReadSourceHealth(); var calls = read(0); var stored = read(1);
                var activeRoots = native.Api.ActiveRoots;
                lower(native.Api.GameplaySource, 1);
                var after = native.Group.ReadSourceHealth();
                Check(tracker.LowerPitchCaptureInvalidated && !tracker.ResourceCaptureInvalidated
                    && !tracker.BackingCaptureInvalidated
                    && native.Group.ReadInitialization() is { Phase: 5, Blocked: 1 }
                    && after is { Known: true, Unknown: false, InFlight: 0 }
                    && after.WriterSequence == before.WriterSequence + 1 && after.SourceSequence == before.SourceSequence
                    && read(0) == calls + 1 && read(1) == stored + 1,
                    "The direct lower attempt failed to issue an orthogonal private invalidation for the actual pending initializer.");
                // The tracker retains its initial projection for identity
                // continuity. Query the current native diagnostic projection
                // through that actual retained enumerator/yield pair.
                var badge = native.Group.ReadLowerPitchInitializationInvalidation(
                    Private<NativeRuntime.RootHandle>(tracker, "_enumerator"),
                    Private<NativePlaybackSourceObservation>(tracker, "_yield"));
                Console.WriteLine(JsonSerializer.Serialize(new { diagnostic = "actual-pending-lower-native-badge",
                    first, badge.Version, badge.Size, badge.Invalidated, badge.Reason, badge.Owner,
                    badge.OwnerEpoch, badge.AttemptSequence, badge.AttemptToken, badge.InvalidationWriter,
                    badge.InvalidationSourceFence, badge.PlayerWatchReceipt,
                    enteredBefore = before.Entered, enteredAfter = after.Entered,
                    exitedBefore = before.Exited, exitedAfter = after.Exited }));
                // Source-open has not happened. A Dormant physical lower call
                // has native responsibility and a private interruption badge,
                // but intentionally dispatches no managed source observation.
                Check(badge is { Version: 1, Size: 112, Invalidated: 1, Reason: 1 }
                    && badge.Owner != 0 && badge.OwnerEpoch == native.Group.Epoch
                    && after.Entered == before.Entered + 1 && after.Exited == before.Exited + 1
                    && badge.AttemptSequence == after.Entered && badge.AttemptToken != 0
                    && badge.InvalidationWriter == after.WriterSequence
                    && badge.InvalidationSourceFence == after.SourceSequence && badge.PlayerWatchReceipt != 0,
                    "The diagnostic projection lost the native pending initializer's exact real lower attempt.");
                Check(!candidate.RootsReleased && roots.Count == 7 && native.Api.ActiveRoots == activeRoots
                    && native.Api.UnityGetTimeOriginalCalls == originalCalls + 1 && native.ClockReads == clockReads
                    && !native.Group.GetTimeLease.ReadCurrent().Active
                    && native.Group.GetTimeLease.ReadActivity().InFlight == 1,
                    "A pending lower interruption released roots, borrowed a Clock, or falsely drained its actual GetTime original.");
                Refuse(native.Group.RequireFreshAdmission, "The invalidated lower candidate granted fresh source publication.");
            }
            finally
            {
                native.Api.DuringHandleTarget = null;
                Export<VoidNative>(native.Module, "StockFixtureReleaseGetTime")();
                if (blocked is not null) Check(blocked.Wait(10000) && float.IsFinite(blocked.Result),
                    "The phase-six actual GetTime original did not physically drain.");
            }
            native.Router.PumpCompleted();
            Check(native.Router.InitializationPending && !candidate.RootsReleased
                && ReferenceEquals(Private<object>(native.Router, "_resourceRetryCandidate"), candidate)
                && Private<bool>(native.Router, "_lowerPitchInitializationRetry")
                && !Private<bool>(native.Router, "_backingInitializationRetry")
                && ReferenceEquals(Private<object?>(native.Router, first ? "_resourceRetryFirst" : "_resourceRetryTransfer"),
                    first ? priorFirst : priorTransfer),
                "The drained lower interruption lost its actual first/transfer private retry owner.");
            Refuse(() => tracker.CaptureCompleted(), "The interrupted initializer completion retained source-opening authority.");
            LowerActualStart(native);
            Check(native.Router.TryCompleteInitialization() && !native.Router.InitializationPending
                && candidate.RootsReleased && native.Api.ActiveRoots == 7
                && native.Group.ReadInitialization() is { Phase: 7, Blocked: 0 }
                && !native.Group.GetTimeLease.ReadCurrent().Active,
                "A later real Start did not replace the lower-invalidated seven-root candidate: " + native.Router.LastFailure);
            if (first) Refuse(() => priorFirst!.TryOpenSource(), "The superseded first lower receipt reopened its prior source.");
            else Refuse(() => priorTransfer!.TryOpenSource(), "The superseded lower transfer reopened its prior source.");
            Refuse(() => native.Router.PreparePausedClock(), "The lower initialization retry substituted for the later stock Pause.");
            long recorded;
            using (var sdk = new ConstructorSdk(native))
            {
                var view = Private<IPluginContext>(sdk.Lease, "Context")
                    .GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service;
                Check(view is not null && sdk.Module.Tick().Ready && sdk.Provider.Probe().Supported,
                    "The recovered pending source did not reach the actual formal provider and independent plugin views.");
                var journal = Need(view!.Read());
                sdk.Speed(2);
                journal = CheckUnityBackingSpeedJournal(sdk, view, journal, sdk.Lease);
                candidate.Dispose(); candidate.PumpCompleted(); tracker.Dispose();
                Check(native.Group.GetTimeLease.ReadCurrent().Active
                    && native.Broker.ReadActualPlaybackDobbyCallCounts() == (21, 21)
                    && physical == (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                        native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress)
                    && native.Group.ReadActivity() is { Known: true, InFlight: 0 } && native.CallbackFailure is null,
                    "Late cleanup of the lower-invalidated candidate withdrew the successor or replaced the physical group.");
                recorded = journal.Counters.Recorded;
            }
            native.Router.PumpCompleted(); native.Group.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0
                && native.Observations.All(observation => observation.IsReleased),
                "The pending lower recovery retained real NativeRuntime roots or physical responsibilities.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 21, actualCommit = 21, retainedRoots = native.Api.ActiveRoots,
                actualPhase6Interruption = true, actualAdmittedSevenRootCandidate = true,
                orthogonalLowerInvalidation = true, initialCaptureRetry = first, actualNewStart = true,
                sdkExecuted = true, journalRecorded = recorded, gameExecuted = false, productionAdmission = false }));
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
