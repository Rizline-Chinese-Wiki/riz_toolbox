using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ContractReadLowerBadge(IntPtr owner, IntPtr enumerator, ulong yieldSequence,
        ref NativePlaybackStockSourceHub.LowerPitchInitializationInformation info);

    private static int RunContractPhase6NoPending(string[] a, string mode)
    {
        ConstructorContext? native = null;
        Task<float>? blocked = null;
        VoidNative? release = null;
        try
        {
            Check(mode is "contract-phase6-first" or "contract-phase6-transfer"
                && Hash(a[1]) == NativePlaybackLowerPitchTarget.OwnedWrapperImageSha256,
                "The contract reproduction requires its exact owned lower-pitch profile.");
            var firstCapture = mode == "contract-phase6-first";
            native = new ConstructorContext(a, hostOnly: firstCapture);
            if (!firstCapture) native.Arm();
            var make = Export<CreateStart>(native.Module, "StockFixtureCreateStart");
            var invoke = Export<InvokeStart>(native.Module, "StockFixtureInvokeStart");
            IntPtr Enumerator()
            {
                var result = make(native.Api.GameplayProgress, native.Api.GameplayClip);
                native.Api.RegisterUnityEnumerator(result); return result;
            }
            uint Start(IntPtr enumerator)
            {
                Check(invoke(native.Targets.ProgressStartMoveNext.Address, enumerator,
                    native.Targets.ProgressStartMoveNext.MethodInfo, out var value, out var error) == 1 && error == 0,
                    "The owned actual initializer did not return normally.");
                return value;
            }
            var enumerator = Enumerator();
            Check(Start(enumerator) == 1 && Start(enumerator) == 0,
                "The owned actual initializer did not complete its yield/final sequence.");
            native.Api.AdoptUnityInitializedPlayer(); native.Api.AdoptUnityClip(native.Api.GameplayClip);
            var tracker = Private<NativePlaybackInitializationTracker>(native.Router, "_initialization");
            var hub = Private<NativePlaybackStockSourceHub>(Private<object>(native.Group, "_owner"), "_hub");
            var watches = Private<ICollection>(hub, "_playerWatches");
            uint[] WatchPhases() => ((IEnumerable)watches).Cast<object>().Select(watch =>
                Private<uint>(watch.GetType().GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(watch, new[] { watch.GetType().GetProperty("SourceOwner", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(watch) })!, "Phase")).ToArray();
            var initialWatchCount = watches.Count;
            var rootsBeforeAdmission = native.Api.ActiveRoots;
            var read = Export<LowerFixtureRead>(native.LowerModule, "StockLowerFixtureRead");
            var lower = Marshal.GetDelegateForFunctionPointer<LowerSetPitch>(native.Targets.SourceTarget(NativePlaybackSourceRole.LowerSetPitch).Address);
            var foreign = native.Api.CreateSharedConstructorSource();
            release = Export<VoidNative>(native.Module, "StockFixtureReleaseGetTime");
            Export<VoidNative>(native.Module, "StockFixtureBlockGetTime")();
            blocked = Task.Run(() => native.Time(native.Api.GameplayPlayer, native.Targets.GetTime.MethodInfo));
            Check(Export<WaitNative>(native.Module, "StockFixtureWaitGetTime")() == 0,
                "The actual owned GetTime original did not enter its real wait.");
            Check(!native.Router.TryCompleteInitialization()
                && native.Group.ReadInitialization() is { Phase: 6, Blocked: 1 }
                && native.Group.GetTimeLease.ReadActivity().InFlight == 1,
                "GetTime must hold phase-six admission before a successor candidate is built.");
            var admission = Private<object>(native.Router, firstCapture ? "_firstCaptureAdmission" : "_initializationHandoff");
            var nativeReceipt = Private<object>(admission, "_native");
            var successorMissing = Private<object?>(admission, "_successor") is null;
            var watchPhases = WatchPhases();
            Check(successorMissing && watches.Count == initialWatchCount && watchPhases.All(value => value != 1)
                && native.Api.ActiveRoots == rootsBeforeAdmission,
                "This reproduction requires no new pending watch or seven-root successor candidate.");
            var beforeForeign = native.Group.ReadSourceHealth();
            var originalBeforeForeign = read(0); var observationsBeforeForeign = native.Observations.Count;
            lower(foreign, 3);
            var foreignWorker = Task.Run(() => lower(foreign, 4));
            Check(foreignWorker.Wait(10000) && native.Group.ReadSourceHealth() == beforeForeign
                && native.Observations.Count == observationsBeforeForeign && read(0) == originalBeforeForeign + 2,
                "Same-class foreign owner/worker calls must execute originals while leaving native fences, tokens and observers untouched.");
            var beforeCurrent = native.Group.ReadSourceHealth(); var originalBeforeCurrent = read(0);
            lower(native.Api.GameplaySource, 7);
            var afterCurrent = native.Group.ReadSourceHealth();
            var actualYield = Private<NativePlaybackSourceObservation>(tracker, "_yield");
            var badge = new NativePlaybackStockSourceHub.LowerPitchInitializationInformation { Version = 1, Size = 112 };
            Check(Export<ContractReadLowerBadge>(hub.NativeLibraryHandle, "RtPlaybackSourceReadLowerPitchInitializationInvalidation")(
                hub.NativeOwner, enumerator, actualYield.Sequence, ref badge) == 1,
                "The actual source helper did not return its native phase-six lower interruption badge.");
            var buffer = Marshal.AllocHGlobal(112);
            try
            {
                Marshal.StructureToPtr(badge, buffer, false); var bytes = new byte[112]; Marshal.Copy(buffer, bytes, 0, 112);
                File.WriteAllBytes("native-badge-112.bin", bytes);
            }
            finally { Marshal.FreeHGlobal(buffer); }
            Check(badge.Invalidated == 1 && badge.Reason == 1 && badge.PlayerWatchReceipt == 0
                && badge.AttemptSequence != 0 && badge.AttemptToken != 0 && badge.InvalidationWriter != 0
                && badge.InvalidationSourceFence != 0 && native.Group.ReadInitialization().Phase == 5
                && afterCurrent.WriterSequence == beforeCurrent.WriterSequence + 1
                && afterCurrent.SourceSequence == beforeCurrent.SourceSequence
                && afterCurrent.SchedulingSequence == beforeCurrent.SchedulingSequence
                && afterCurrent is { Known: true, Unknown: false } && read(0) == originalBeforeCurrent + 1
                && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(16))) == 7
                && native.Api.ActiveRoots == rootsBeforeAdmission,
                "The actual owner lower must perform one write and publish a known zero-pending-watch phase-six interruption.");
            Check(tracker.LowerPitchCaptureInvalidated && !native.Router.TryCompleteInitialization()
                && native.Api.ActiveRoots == rootsBeforeAdmission && tracker.RootsRetained,
                "The actual zero-watch badge must defer recovery and retain roots while GetTime remains in flight.");
            release();
            Check(blocked.Wait(10000) && float.IsFinite(blocked.Result), "The actual GetTime original did not physically drain.");
            blocked = null;
            native.Router.PumpCompleted();
            var rootsAfterDrain = native.Api.ActiveRoots;
            var healthAfterDrain = native.Group.ReadSourceHealth();
            var retryFirstBeforeStart = Private<object?>(native.Router, "_resourceRetryFirst") is not null;
            var retryTransferBeforeStart = Private<object?>(native.Router, "_resourceRetryTransfer") is not null;
            Check(healthAfterDrain is { Known: true, Unknown: false, InFlight: 0 }
                && !tracker.RootsRetained && (firstCapture ? retryFirstBeforeStart && !retryTransferBeforeStart
                    : retryTransferBeforeStart && !retryFirstBeforeStart),
                "The real drained interruption must retain the exact native retry receipt and release only its exited initializer roots.");
            var recoveryEnumerator = Enumerator();
            var recoveryFirst = Start(recoveryEnumerator); var recoveryLast = Start(recoveryEnumerator);
            native.Api.AdoptUnityInitializedPlayer(); native.Api.AdoptUnityClip(native.Api.GameplayClip);
            var recovered = native.Router.TryCompleteInitialization();
            Check(recovered && recoveryFirst == 1 && recoveryLast == 0 && !native.Router.InitializationPending
                && native.Group.ReadInitialization() is { Phase: 7, Blocked: 0 }
                && native.Group.ReadSourceHealth() is { Known: true, Unknown: false, InFlight: 0 }
                && !native.Group.GetTimeLease.ReadCurrent().Active && native.Api.ActiveRoots == 7,
                "A fresh actual Start must consume the native zero-watch retry and open only the dormant seven-root successor.");
            native.Group.RequireFreshAdmission();
            Refuse(() => tracker.CaptureCompleted(), "The interrupted original initializer completion must remain expired.");
            if (nativeReceipt is NativePlaybackSingleReturnBridge.InitialCapture oldFirst)
                Refuse(() => oldFirst.TryOpenSource(), "The superseded zero-watch first-capture receipt must remain expired.");
            else if (nativeReceipt is NativePlaybackSingleReturnBridge.LogicalHandoff oldTransfer)
                Refuse(() => oldTransfer.TryOpenSource(), "The superseded zero-watch handoff receipt must remain expired.");
            Refuse(() => native.Router.PreparePausedClock(), "Dormant source-open must still require a later actual Pause.");
            long journalRecorded; float actualGetTime; double actualPitch; double clockRate;
            using (var sdk = new ConstructorSdk(native))
            {
                var calls = read(0); var writer = native.Group.ReadSourceHealth().WriterSequence;
                sdk.Speed(2);
                actualGetTime = native.Time(native.Api.GameplayPlayer, native.Targets.GetTime.MethodInfo);
                actualPitch = native.Api.GameplayApi.Unbox<float>(native.Api.GameplayApi.Call(native.Api.GameplaySource, "get_pitch"));
                clockRate = native.Clock.Capture().Rate; journalRecorded = sdk.Journal.Counters.Recorded;
                Check(read(0) == calls + 1 && native.Group.ReadSourceHealth().WriterSequence == writer + 1
                    && journalRecorded == 1 && actualPitch == 2 && clockRate == 2 && float.IsFinite(actualGetTime)
                    && actualGetTime == (float)native.Clock.Capture().MediaAnchorSeconds && native.UniqueFinalClock
                    && native.Group.GetTimeLease.ReadCurrent().Active && sdk.ClockView.Read().Success && sdk.OtherClock.Read().Success,
                    "The recovered same formal Module/Service/dual-plugin SDK must confirm one actual Speed(2), writer and journal record.");
            }
            native.Router.PumpCompleted(); native.Group.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0
                && native.Observations.All(value => value.IsReleased) && native.CallbackFailure is null
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (21, 21),
                "The known fixed retry must release only actually exited roots and retain its same twenty-one physical hooks.");
            var finalHealth = hub.ReadHealth();
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, findingReproduced = false, fixVerified = true, checks = _checks,
                firstCapture, actualPrepare = 21, actualCommit = 21, actualGetTimeInFlight = 1,
                receiptType = nativeReceipt.GetType().Name, successorMissing, initialWatchCount, watchPhases,
                rootsBeforeAdmission, rootsAfterDrain, retainedRoots = native.Api.ActiveRoots,
                badge = new { badge.Version, badge.Size, badge.Invalidated, badge.Reason, badge.Owner, badge.OwnerEpoch,
                    badge.Initializer, badge.InitializerOrdinal, badge.Enumerator, badge.Progress, badge.YieldSequence,
                    badge.AttemptSequence, badge.AttemptToken, badge.InvalidationWriter, badge.InvalidationSourceFence, badge.PlayerWatchReceipt },
                sameClassForeignOwnerAndWorkerTransparent = true, directCurrentOriginalExactlyOnce = true,
                healthAfterDrain, retryFirstBeforeStart, retryTransferBeforeStart,
                recoveryFirst, recoveryLast, recovered, finalHealth, native.CallbackFailure,
                journalRecorded, actualGetTime, actualPitch, clockRate, sameFormalModuleServiceSdk = true,
                independentPluginScopes = 2, sdkExecuted = true, actualPauseRequired = true,
                gameExecuted = false, productionAdmission = false, productFilesChanged = 1 }));
            native.Dispose(); native = null;
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString(), gameExecuted = false }));
            if (native is not null) { ConstructorRetainedContexts.Add(native); native = null; }
            return 1;
        }
        finally
        {
            release?.Invoke();
            if (blocked is not null) blocked.Wait(10000);
        }
    }
}
