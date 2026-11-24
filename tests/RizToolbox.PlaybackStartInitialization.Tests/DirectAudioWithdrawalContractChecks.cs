using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    // Acquire is a general operation lease. NativeDescriptor and operation
    // support independently decide whether that lease can control native rate.
    private static int RunDirectWithdrawalContract(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && Hash(arguments[1]) == PlaybackSourceGroupTargets.DirectAudioFixtureSha256,
                "Withdrawal-contract checks require the exact nineteen-member owned fixture.");
            using var native = new ConstructorContext(arguments);
            bool otherAcquired = false; string? rejectedCode = null; var entered = 0;
            using (var sdk = new ConstructorSdk(native))
            {
                var oldClock = native.Clock;
                var oldIdentity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
                Check(oldIdentity is { NativeDescriptor: not null } && sdk.Provider.Probe().Supported,
                    "The initial SDK lease did not hold an actual native rate descriptor.");
                var pitchCalls = Marshal.ReadInt32(native.Api.GameplaySource, 20);
                var directCalls = Marshal.ReadInt32(native.Api.GameplaySource, DirectCallsOffset);
                native.AfterDirectAudioBefore = _ =>
                {
                    Check(oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn
                        && !native.Group.GetTimeLease.ReadCurrent().Active && !sdk.Provider.Probe().Supported,
                        "The actual direct Before retained native rate or Clock publication.");
                    Check(!sdk.Lease.Execute(sdk.Request(2)).Success,
                        "The original SDK lease retained a supported speed operation.");
                    var acquired = sdk.Other.Acquire(true); otherAcquired = acquired.Success;
                    if (acquired.Success)
                    {
                        using var next = Need(acquired);
                        var request = new GameplayControlRequest(GameplayControlAction.SetSpeed,
                            next.ControllerGeneration, next.ObservedRunId, 2, GameplayControlValueUnit.PlaybackRateMultiplier);
                        var rejected = next.Execute(request); rejectedCode = rejected.Error?.Code.ToString();
                        Check(!rejected.Success, "A newly acquired general SDK lease recovered native rate control.");
                    }
                    var identity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
                    Check(identity is { NativeDescriptor: null } && identity != oldIdentity
                        && !native.Group.GetTimeLease.ReadCurrent().Active && !sdk.Provider.Probe().Supported
                        && Marshal.ReadInt32(native.Api.GameplaySource, 20) == pitchCalls
                        && Marshal.ReadInt32(native.Api.GameplaySource, DirectCallsOffset) == directCalls,
                        "General lease acquisition recovered the old native descriptor or wrote pitch before the original.");
                    ++entered;
                };
                var target = native.Targets.SourceTarget(NativePlaybackSourceRole.AudioPlayHelper);
                Check(native.Invoke(8, target.Address, native.Api.GameplaySource, DirectBits(0), target.MethodInfo, out var code) == 1
                    && code == 0 && entered == 1 && native.CallbackFailure is null,
                    "The actual withdrawal-contract boundary failed: " + native.CallbackFailure);
                native.AfterDirectAudioBefore = null;
            }
            native.Router.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0,
                "Known withdrawal-contract source did not drain actual roots.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                otherGeneralLeaseAcquired = otherAcquired, otherSpeedRejectedCode = rejectedCode,
                initialNativeDescriptor = true, afterNativeDescriptor = false, nativeSpeedRecovered = false,
                sdkExecuted = true, speedBeforeWithdrawalExecuted = false, actualPrepare = 19, actualCommit = 19,
                retainedRoots = native.Api.ActiveRoots, gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error);Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));return 1; }
    }
}
