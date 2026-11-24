using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    private static int RunProgressResourceLifecycle(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && Hash(arguments[1]) == PlaybackSourceGroupTargets.ProgressResourceFixtureSha256,
                "Resource lifecycle requires its exact twenty-member owned fixture.");
            using var native = new ConstructorContext(arguments);
            var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
            if (mode == "resource-withdraw-retire")
            {
                native.Arm();
                var future = native.Group.CaptureProgressResource(rooted, native.Api.GameplayApi);
                Check(future.RootsRetained && native.Api.ActiveRoots == 8,
                    "The future resource did not retain its independent actual reference root.");
                native.Group.Dispose();
                Check(native.Group.ReadActivity() is { Known: true, InFlight: 0 },
                    "Normal group withdrawal lost its known physical drain.");
                Refuse(() => future.VerifyCurrent(), "A withdrawn group retained future source authority.");
                Check(future.TryRetire() && !future.RootsRetained && native.Api.ActiveRoots == 7,
                    "Known physical group withdrawal permanently prevented resource-root retirement.");
                native.Router.Dispose(); native.Router.PumpCompleted();
            }
            else
            {
                using (var sdk = new ConstructorSdk(native))
                {
                    sdk.Speed(1.5);
                    using var future = native.Group.CaptureProgressResource(rooted, native.Api.GameplayApi);
                    using var captured = sdk.Provider.Capture(native.Origin);
                    using var writer = captured.PreparePitchWrite();
                    using var prepared = DirectSdkPrepare(native, sdk);
                    var before = native.Group.ReadSourceHealth(); var clock = native.Clock.Capture();
                    var journal = sdk.Journal.Counters; var observations = native.Observations.Count;
                    var currentReceipt = rooted.Current;
                    var sourceIdentity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
                    var sourceClip = Marshal.ReadIntPtr(native.Api.GameplaySource, 40);
                    var getter = Export<ResourceObjectRead>(native.Module, "StockFixtureResourceRead");
                    var setter = native.Targets.ProgressClip;
                    var basis = future.Read();
                    var references = new[] { native.Api.ProgressResourceA, native.Api.ProgressResourceB,
                        native.Api.ProgressResourceA, IntPtr.Zero, native.Api.ProgressResourceB };
                    foreach (var reference in references)
                    {
                        Check(native.Invoke(4, setter.Address, native.Api.GameplayProgress,
                            unchecked((ulong)reference.ToInt64()), IntPtr.Zero, out var error) == 1 && error == 0,
                            "Actual same/null/ABA future-resource setter failed.");
                        Refuse(() => future.VerifyCurrent(), "A same/null/ABA resource attempt revived its old future ticket.");
                        Check(native.Clock.Capture() == clock && native.Group.GetTimeLease.ReadCurrent().Active
                            && Marshal.ReadIntPtr(native.Api.GameplaySource, 40) == sourceClip,
                            "A pure resource attempt damaged current audio or its unique Clock.");
                    }
                    // Exercise the actual owned original through NativeRuntime,
                    // using its true constructed-generic reference argument.
                    native.Api.GameplayApi.Invoke(setter.MethodInfo, native.Api.GameplayProgress,
                        NativeRuntime.Arg.Ref(native.Api.ProgressResourceB));
                    var after = native.Group.ReadSourceHealth();
                    Check(after is { Known: true, InFlight: 0, Unknown: false }
                        && after.WriterSequence == before.WriterSequence && after.SourceSequence == before.SourceSequence
                        && after.SchedulingSequence == before.SchedulingSequence && native.Observations.Count == observations + 6
                        && getter(native.Api.ProgressResourceA, 0) == 0 && getter(native.Api.ProgressResourceB, 0) == 0
                        && sdk.Journal.Counters == journal && ReferenceEquals(rooted.Current, currentReceipt)
                        && Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity") == sourceIdentity,
                        "Future-resource attempts loaded resources, changed current audio fences or replaced its formal source identity.");
                    using (var next = native.Group.CaptureProgressResource(rooted, native.Api.GameplayApi))
                        Check(next.Revision == basis.Revision + 6 && next.ContinuityIdentity == basis.ContinuityIdentity,
                            "Same/null/ABA/runtime attempts did not advance the independent resource revision exactly once each.");
                    captured.VerifyAuthority(); writer.VerifyCurrent();
                    Check(captured.Read().Binding == native.Binding && sdk.ClockView.Read().Success && sdk.OtherClock.Read().Success
                        && !sdk.Other.Acquire(true).Success, "Resource-only attempts invalidated existing source/writer/SDK scopes.");
                    var pitchCalls = Marshal.ReadInt32(native.Api.GameplaySource, 20);
                    prepared.Commit(); var confirmed = prepared.Confirm();
                    Check(confirmed is { Observed: true, Value: 2, Unit: GameplayControlValueUnit.PlaybackRateMultiplier }
                        && native.Clock.Capture().Rate == 2
                        && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(native.Api.GameplaySource, 16)) == 2
                        && Marshal.ReadInt32(native.Api.GameplaySource, 20) == pitchCalls + 1,
                        "Pure future-resource writes invalidated a previously prepared actual current-audio transaction.");
                    // Confirmation retains rollback ownership until this prepared transaction is disposed.
                    // Release that reservation before starting the next independent SDK transaction.
                    prepared.Dispose();
                    sdk.Speed(1.25);
                    Check(native.CallbackFailure is null && native.UniqueFinalClock && native.Group.GetTimeLease.ReadCurrent().Active,
                        "The pre-existing formal SDK could not continue actual current-audio control.");
                }
                native.Router.PumpCompleted();
            }
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(value => value.IsReleased),
                "Known resource lifecycle retained actual roots or native observation responsibilities.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 20, actualCommit = 20, sdkExecuted = mode == "resource-sdk-current",
                preparedCurrentTransactionPreserved = mode == "resource-sdk-current",
                knownWithdrawalRetired = mode == "resource-withdraw-retire", retainedRoots = native.Api.ActiveRoots,
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
