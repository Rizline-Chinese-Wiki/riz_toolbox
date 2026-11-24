using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    private static int RunLateProgressResourceRetry(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && Hash(arguments[1]) == PlaybackSourceGroupTargets.ProgressResourceFixtureSha256,
                "Late resource retry requires its exact twenty-member native fixture.");
            var firstCapture = mode == "resource-first-late-retry";
            using var native = new ConstructorContext(arguments, hostOnly: firstCapture);
            if (!firstCapture) native.Arm();
            var make = Export<CreateStart>(native.Module, "StockFixtureCreateStart");
            var call = Export<InvokeStart>(native.Module, "StockFixtureInvokeStart");
            var release = Export<VoidNative>(native.Module, "StockFixtureReleaseGetTime");
            IntPtr Enumerator()
            {
                var value = make(native.Api.GameplayProgress, native.Api.CreateProgressResourceClip(0xdead));
                native.Api.RegisterProgressResourceEnumerator(value);
                return value;
            }
            uint Start(IntPtr value)
            {
                Check(call(native.Targets.ProgressStartMoveNext.Address, value, native.Targets.ProgressStartMoveNext.MethodInfo,
                    out var raw, out var error) == 1 && error == 0, "Actual late-retry Start failed: " + native.Router.LastFailure);
                return raw;
            }
            void CompleteActualStart(IntPtr value, IntPtr expected)
            {
                Check(Start(value) == 1 && Start(value) == 0
                    && Marshal.ReadIntPtr(native.Api.GameplaySource, 40) == expected,
                    "Late-retry actual Start did not load the current reference through its real getter/set_clip chain.");
                native.Api.AdoptActuallyInitializedPlayer(); native.Api.AdoptProgressResourceClip(expected);
            }
            CompleteActualStart(Enumerator(), native.Api.ProgressResourceClipA);
            var originalTracker = Private<NativePlaybackInitializationTracker>(native.Router, "_initialization");
            Task<float>? blocked = null;
            var hub = Private<object>(Private<object>(native.Group, "_owner"), "_hub");
            var progressWatches = Private<System.Collections.ICollection>(hub, "_progressWatches");
            var originalWatchCount = progressWatches.Count;
            // Wait until the real new native/managed Progress watch exists.
            // Blocking in an earlier API sample correctly rejects capture
            // before any pending seven-root candidate has been staged.
            native.Api.DuringHandleTarget = target =>
            {
                if (blocked is not null || target != native.Api.GameplayProgress || progressWatches.Count <= originalWatchCount) return;
                Export<VoidNative>(native.Module, "StockFixtureBlockGetTime")();
                blocked = Task.Run(() => native.Time(native.Api.GameplayPlayer, native.Targets.GetTime.MethodInfo));
                Check(Export<WaitNative>(native.Module, "StockFixtureWaitGetTime")() == 0,
                    "The actual raw GetTime original did not enter its fixture wait.");
            };
            NativePlaybackStockRootedSource priorCandidate;
            NativePlaybackSingleReturnBridge.InitialCapture? priorFirst = null;
            NativePlaybackSingleReturnBridge.LogicalHandoff? priorHandoff = null;
            try
            {
                Check(!native.Router.TryCompleteInitialization() && blocked is not null
                    && native.Group.ReadInitialization() is { Phase: 6, Blocked: 1 }
                    && native.Group.GetTimeLease.ReadActivity().InFlight == 1,
                    "Late retry did not hold a real admitted initializer behind a physical GetTime borrower.");
                var admission = Private<object>(native.Router, firstCapture ? "_firstCaptureAdmission" : "_initializationHandoff");
                priorCandidate = Private<NativePlaybackStockRootedSource>(admission, "_successor");
                if (firstCapture)
                {
                    priorFirst = Private<NativePlaybackSingleReturnBridge.InitialCapture>(admission, "_native");
                    Check(priorFirst.Read(2).Phase == 2, "First retry never reached the admitted receipt.");
                }
                else
                {
                    priorHandoff = Private<NativePlaybackSingleReturnBridge.LogicalHandoff>(admission, "_native");
                    Check(priorHandoff.Read(2).Phase == 2, "Handoff retry never reached the admitted receipt.");
                }
                var actual = Private<NativePlaybackActualSource>(priorCandidate, "_actual");
                Check(Private<List<RizToolbox.Runtime.NativeRuntime.RootHandle>>(actual, "_roots").Count == 7
                    && !priorCandidate.RootsReleased && ReferenceEquals(Private<object>(native.Router, "_source"), priorCandidate),
                    "The interrupted late candidate did not own its exact seven retained roots.");
                var setter = native.Targets.ProgressClip;
                var invoked = native.Invoke(4, setter.Address, native.Api.GameplayProgress,
                    unchecked((ulong)native.Api.ProgressResourceB.ToInt64()), setter.MethodInfo, out var error);
                var resourceTicket = Private<NativePlaybackStockSourceHub.ProgressResourceTicket>(originalTracker, "_resource").Read();
                Console.WriteLine(JsonSerializer.Serialize(new { diagnostic = "actual-resource-phase6-interruption",
                    invoked, error, invalidated = originalTracker.ResourceCaptureInvalidated,
                    nativeInitializerPhase = native.Group.ReadInitialization().Phase, resourcePhase = resourceTicket.Phase,
                    resourceCurrent = resourceTicket.Current, initializerFailure = originalTracker.LastFailure,
                    routerFailure = native.Router.LastFailure, native.CallbackFailure,
                    physicalKnown = native.Group.ReadActivity().Known,
                    observedResourceWrites = native.Observations.Count(value => value.Role == NativePlaybackSourceRole.ProgressClip) }));
                Check(invoked == 1 && error == 0
                    && originalTracker.ResourceCaptureInvalidated && native.Group.ReadInitialization().Phase == 5,
                    "The real resource setter did not invalidate the exact admitted initializer.");
                Check(!priorCandidate.RootsReleased,
                    "An in-flight late interruption released its pending candidate before replacement.");
            }
            finally
            {
                native.Api.DuringHandleTarget = null; release();
                if (blocked is not null) Check(blocked.Wait(10000) && float.IsFinite(blocked.Result),
                    "The actual GetTime borrower did not physically drain.");
            }
            native.Router.PumpCompleted();
            Check(ReferenceEquals(Private<object>(native.Router, "_resourceRetryCandidate"), priorCandidate)
                && !priorCandidate.RootsReleased && native.Router.InitializationPending,
                "The drained interruption lost its exact retry record or old seven-root candidate.");
            Refuse(() => originalTracker.CaptureCompleted(), "The old initializer completion revived after resource replacement.");
            CompleteActualStart(Enumerator(), native.Api.ProgressResourceClipB);
            Check(native.Router.TryCompleteInitialization() && !native.Router.InitializationPending,
                "A fresh actual Start could not replace the late resource capture: " + native.Router.LastFailure);
            Check(priorCandidate.RootsReleased && native.Api.ActiveRoots == 7
                && native.Group.ReadInitialization() is { Phase: 7, Blocked: 0 },
                "The exact replacement failed to release only the superseded seven roots.");
            native.Arm();
            Check(native.Clock.Capture().Binding.Clip == native.Api.ProgressResourceClipB.ToInt64()
                && native.Group.GetTimeLease.ReadCurrent().Active && native.UniqueFinalClock,
                "The late replacement did not admit one usable Clock for the actual new clip.");
            if (priorFirst is not null) Refuse(() => priorFirst.TryOpenSource(), "The superseded first receipt reopened its old graph.");
            if (priorHandoff is not null) Refuse(() => priorHandoff.TryOpenSource(), "The superseded handoff receipt reopened its old graph.");
            priorCandidate.Dispose(); priorCandidate.PumpCompleted(); originalTracker.Dispose();
            Check(native.Group.GetTimeLease.ReadCurrent().Active && native.Api.ActiveRoots == 7,
                "Late old-candidate or initializer cleanup cleared the replacement watch.");
            native.Router.Dispose(); native.Router.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(value => value.IsReleased),
                "Known physical exit left late-retry roots or observation responsibilities behind.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 20, actualCommit = 20, actualPhase6Interruption = true,
                actualPendingSevenRootReplacement = true, supersededReceiptRejected = true,
                gameExecuted = false, productionAdmission = false, fullGenerationCompleted = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false,
                checks = _checks, failure = error.ToString() })); return 1;
        }
    }
}
