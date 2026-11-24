using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using RizToolbox.Sdk;

static class TailWindowChecks
{
    private static void Check(bool condition, string message) => ManagedChecks.Check(condition, message);

    internal static int Run(string[] arguments)
    {
        var before = arguments.Length == 1 && arguments[0] == "--observe-r2";
        if (!before && !(arguments.Length == 1 && arguments[0] == "--require-r3"))
            throw new ArgumentException("Use one explicit before or after predicate.");
        var rows = new List<object>();
        var scenarios = 0;
        try
        {
            foreach (var stage in new[] { "final-native-verify", "final-monotonic", "last-same-capture" })
            foreach (var stop in new[] { "service-dispose", "host-dispose" })
            {
                rows.Add(Tail(stage, stop, before));
                ++scenarios;
            }
            if (!before)
            {
                rows.Add(CancelAtFinalVerification()); ++scenarios;
                rows.Add(NormalReadAndCachedScope()); ++scenarios;
                rows.Add(CommittedDispatchHistory()); ++scenarios;
            }
            Console.WriteLine(JsonSerializer.Serialize(new { mode = before ? "dispose-tail-before-r2" : "dispose-tail-after-r3",
                passed = true, scenarioCount = scenarios, checks = ManagedChecks.Checks,
                expectedOldBugObserved = before, featureAccepted = !before, cases = rows,
                scope = "pure-managed-tail-lifetime-guard", original17ScenariosRerun = false,
                nativeDllLoaded = false, gameExecuted = false, standardProductBuild = false, reflectionUsed = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode = before ? "dispose-tail-before-r2" : "dispose-tail-after-r3",
                passed = false, scenarioCount = scenarios, checks = ManagedChecks.Checks, cases = rows,
                failure = error.ToString(), featureAccepted = false, nativeDllLoaded = false, gameExecuted = false }));
            return 1;
        }
    }

    private static object Tail(string stage, string stop, bool before)
    {
        var fixture = new Fixture();
        var triggered = 0;
        try
        {
            Action close = () =>
            {
                ++triggered;
                Check(fixture.Backend.ActiveCaptures == 1 && fixture.Backend.Disposals == 0,
                    "The tail callback did not hold the actual current managed capture.");
                if (stop == "service-dispose") fixture.Service.Dispose(); else fixture.Host.Dispose();
                Check(fixture.Backend.ActiveCaptures == 1 && fixture.Backend.Disposals == 0,
                    "Disposal released the backend or current capture inside its active read.");
            };
            if (stage == "final-native-verify") fixture.Backend.OnVerify = close;
            else if (stage == "final-monotonic") fixture.OnEndCapture = close;
            else fixture.Backend.OnVerify = () => fixture.Backend.OnGraph = close;
            var result = fixture.FirstClock.Read();
            Check(triggered == 1, "The selected final callback was not reached exactly once.");
            if (before)
            {
                Check(result.Success && result.Value?.Playback.Available == true,
                    "The frozen r2 product did not reproduce the known disposed-read success window.");
            }
            else Check(!result.Success && result.Value is null && result.Error?.KnownCode == ErrorCode.ScopeExpired,
                "The r3 read published a snapshot after the service or requesting plugin scope expired.");
            if (stop == "service-dispose")
                Check(fixture.Backend.ActiveCaptures == 0 && fixture.Backend.Disposals == 1,
                    "The read finally did not release deferred service capture/backend exactly once.");
            else
                Check(fixture.Backend.ActiveCaptures == 1 && fixture.Backend.Disposals == 0,
                    "Host-only disposal unexpectedly destroyed the independently owned service backend.");
            var cached = fixture.FirstClock.Read();
            Check(!cached.Success && cached.Error?.KnownCode == ErrorCode.ScopeExpired,
                "The expired cached view allowed a later request.");
            return new { stage, stop, callbackCount = triggered, returnedSuccess = result.Success,
                playbackAvailable = result.Value?.Playback.Available ?? false,
                error = result.Error?.KnownCode.ToString(),
                oldBugObserved = before, scopeExpired = result.Error?.KnownCode == ErrorCode.ScopeExpired,
                activeCapturesBeforeModuleCleanup = fixture.Backend.ActiveCaptures,
                backendDisposalsBeforeModuleCleanup = fixture.Backend.Disposals };
        }
        finally
        {
            fixture.Dispose();
            Check(fixture.Backend.ActiveCaptures == 0 && fixture.Backend.Disposals == 1,
                "Final service cleanup did not drain each capture and backend exactly once.");
        }
    }

    private static object CancelAtFinalVerification()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var triggered = 0;
            fixture.Backend.OnVerify = () => { ++triggered; cancellation.Cancel(); };
            var result = fixture.FirstClock.Read(cancellation.Token);
            Check(triggered == 1 && !result.Success && result.Value is null && result.Error?.KnownCode == ErrorCode.Cancelled,
                "Cancellation after the earlier guard published a current-read snapshot.");
            Check(fixture.Backend.ActiveCaptures == 1 && fixture.Backend.Disposals == 0,
                "Request cancellation destroyed the independently retained service capture.");
            return new { regression = "final-verification-cancellation", error = result.Error?.KnownCode.ToString() };
        }
        finally { fixture.Dispose(); Check(fixture.Backend.ActiveCaptures == 0 && fixture.Backend.Disposals == 1, "Cancelled request cleanup leaked."); }
    }

    private static object NormalReadAndCachedScope()
    {
        var fixture = new Fixture();
        try
        {
            var first = ManagedChecks.Need(fixture.FirstClock.Read());
            var second = ManagedChecks.Need(fixture.SecondClock.Read());
            Check(first.Playback.Available && second.Playback.Available && first.Playback.Sample?.MediaSeconds == 5
                && first.ControllerGeneration == second.ControllerGeneration && first.GameTime.Available && second.GameTime.Available,
                "The final guard rejected an active two-plugin managed snapshot.");
            fixture.Host.Dispose();
            foreach (var view in new[] { fixture.FirstClock, fixture.SecondClock })
            {
                var stopped = view.Read();
                Check(!stopped.Success && stopped.Error?.KnownCode == ErrorCode.ScopeExpired,
                    "A stopped plugin's cached normal view remained readable.");
            }
            return new { regression = "normal-two-plugin-read-and-cached-scope-stop", passed = true };
        }
        finally { fixture.Dispose(); Check(fixture.Backend.ActiveCaptures == 0 && fixture.Backend.Disposals == 1, "Normal view cleanup leaked."); }
    }

    private static object CommittedDispatchHistory()
    {
        foreach (var stop in new[] { "service-dispose", "host-dispose" })
        {
            var fixture = new Fixture();
            try
            {
                using var lease = ManagedChecks.Need(fixture.Controls.Acquire(true));
                fixture.Backend.OnDispatch = () => { if (stop == "service-dispose") fixture.Service.Dispose(); else fixture.Host.Dispose(); };
                var receipt = ManagedChecks.Need(lease.Execute(GameplayControlAction.Pause));
                Check(receipt.Modification.Dispatched && receipt.ImmediatePostconditionObserved is null
                    && fixture.Backend.Dispatches == 1 && fixture.Backend.Paused,
                    "A completed dispatch lost its receipt/history when ownership ended in the native call.");
                var expired = lease.Execute(GameplayControlAction.Resume);
                Check(!expired.Success && expired.Error?.KnownCode == ErrorCode.ScopeExpired && fixture.Backend.Dispatches == 1,
                    "The ended control owner dispatched a later operation.");
            }
            finally { fixture.Dispose(); Check(fixture.Backend.ActiveCaptures == 0 && fixture.Backend.Disposals == 1, "Committed dispatch cleanup leaked."); }
        }
        return new { regression = "already-dispatched-receipt-preserved", stopModes = 2, passed = true };
    }
}
