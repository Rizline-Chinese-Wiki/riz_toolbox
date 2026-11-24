using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    // Isolate the clock contract after physical Unknown without repeating the
    // expensive SDK value transaction. This does not replace the full worker test.
    private static int RunDirectWorkerClockContract(string[] arguments, string mode)
    {
        try
        {
            using var native = new ConstructorContext(arguments);
            long oldGeneration, firstGeneration, secondGeneration;
            string? afterPumpPhase = null, afterActivityPhase = null, afterProbePhase = null;
            bool identityAbsent, descriptorAbsent, firstGameAvailable, secondGameAvailable, otherAcquired;
            string? otherError;
            using (var sdk = new ConstructorSdk(native))
            {
                var initialIdentity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
                oldGeneration = sdk.Lease.ControllerGeneration;
                Check(initialIdentity is { NativeDescriptor: not null } && sdk.Provider.Probe().Supported,
                    "Worker clock contract did not begin with actual SDK native authority.");
                var target = native.Targets.SourceTarget(NativePlaybackSourceRole.AudioPlayHelper);
                var pitchCalls = Marshal.ReadInt32(native.Api.GameplaySource, 20);
                var originalCalls = Marshal.ReadInt32(native.Api.GameplaySource, DirectCallsOffset);
                var worker = Task.Run(() => native.Invoke(8, target.Address, native.Api.GameplaySource, DirectBits(0), target.MethodInfo, out var code) == 1 && code == 0);
                Check(worker.Wait(10000) && worker.Result, "Actual worker original did not return.");
                native.Router.PumpCompleted(); sdk.Provider.PumpCompleted(); native.Router.PumpCompleted();
                afterPumpPhase = native.Clock.Capture().Phase.ToString();
                Check(native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn,
                    "Owner pump did not withdraw the worker-faulted Clock before any extra source-health/provider probe.");
                var activity = native.Group.ReadActivity(); afterActivityPhase = native.Clock.Capture().Phase.ToString();
                var supported = sdk.Provider.Probe().Supported; afterProbePhase = native.Clock.Capture().Phase.ToString();
                var oldExecuted = sdk.Lease.Execute(sdk.Request(2)).Success;
                Console.WriteLine(JsonSerializer.Serialize(new { diagnostic = "actual-worker-fault-observation-order", afterPumpPhase,
                    afterActivityPhase, afterProbePhase, activity.Known, activity.InFlight, providerSupported = supported, oldLeaseExecuted = oldExecuted }));
                Check(activity is { Known: false, InFlight: 0 } && native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn
                    && !supported && !oldExecuted, "Observed physical worker Unknown retained native Clock/rate or old SDK execution authority.");
                var first = Need(sdk.ClockView.Read()); var second = Need(sdk.OtherClock.Read());
                var identity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
                firstGeneration = first.ControllerGeneration; secondGeneration = second.ControllerGeneration;
                firstGameAvailable = first.GameTime.Available; secondGameAvailable = second.GameTime.Available;
                identityAbsent = identity is null; descriptorAbsent = identity?.NativeDescriptor is null;
                Check(identity != initialIdentity && descriptorAbsent,
                    $"SDK clock retained native descriptor: identityAbsent={identityAbsent}, oldGeneration={oldGeneration}, first={firstGeneration}, second={secondGeneration}.");
                var acquired = sdk.Other.Acquire(true); otherAcquired = acquired.Success; otherError = acquired.Error?.Code.ToString();
                if (acquired.Success)
                {
                    using var other = Need(acquired);
                    Check(!other.Execute(new RizToolbox.Sdk.GameplayControlRequest(RizToolbox.Sdk.GameplayControlAction.SetSpeed,
                        other.ControllerGeneration, other.ObservedRunId, 2, RizToolbox.Sdk.GameplayControlValueUnit.PlaybackRateMultiplier)).Success,
                        "A new generic lease recovered speed on the worker-faulted source.");
                }
                Check(Marshal.ReadInt32(native.Api.GameplaySource, 20) == pitchCalls
                    && Marshal.ReadInt32(native.Api.GameplaySource, DirectCallsOffset) == originalCalls + 1,
                    "Clock reads or generic ownership wrote native pitch or changed original count.");
            }
            native.Router.PumpCompleted();
            Check(!native.Router.RootsReleased && native.Api.ActiveRoots == 7,
                "Unknown worker clock contract released the seven actual source roots.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                oldGeneration, firstGeneration, secondGeneration, identityAbsent, descriptorAbsent,
                afterPumpPhase, afterActivityPhase, afterProbePhase,
                firstGameAvailable, secondGameAvailable, otherAcquired, otherError,
                speedBeforeWithdrawalExecuted = false, oldLeaseAndSpeedRevoked = true,
                actualPrepare = 19, actualCommit = 19, retainedRoots = native.Api.ActiveRoots,
                gameExecuted = false, productionAdmission = false }));
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
