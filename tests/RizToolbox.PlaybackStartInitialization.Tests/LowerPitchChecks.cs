using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void LowerSetPitch(IntPtr self, float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong LowerFixtureRead(uint field);

    private static int RunLowerPitch(string[] a, string mode)
    {
        try
        {
            Check(a.Length == 5 && mode is "lower-pitch-route" or "lower-pitch-sdk"
                && Hash(a[1]) == NativePlaybackLowerPitchTarget.OwnedWrapperImageSha256,
                "Lower-pitch validation requires the exact owned two-image profile and mode.");
            using var native = new ConstructorContext(a);
            Check(native.Targets.HasLowerPitch && native.Targets.HasUnityBacking && native.PhysicalMemberCount == 21
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (21, 21),
                "The current group did not actually install all twenty-one physical members.");
            var read = Export<LowerFixtureRead>(native.LowerModule, "StockLowerFixtureRead");
            var lower = Marshal.GetDelegateForFunctionPointer<LowerSetPitch>(native.Targets.SourceTarget(NativePlaybackSourceRole.LowerSetPitch).Address);
            if (mode == "lower-pitch-sdk")
            {
                using var sdk = new ConstructorSdk(native);
                var calls = read(0); var writes = native.Group.ReadSourceHealth().WriterSequence;
                sdk.Speed(1.5);
                Check(read(0) == calls + 1 && read(1) == calls + 1
                    && native.Group.ReadSourceHealth().WriterSequence == writes + 1
                    && sdk.Journal.Counters.Recorded == 1,
                    "The actual SDK wrapper/lower path doubled its writer, native original or modification record.");
                Check(native.CallbackFailure is null && native.Group.ReadSourceHealth() is { Known: true, Unknown: false, InFlight: 0 },
                    "The successful SDK retained a lower physical borrow or poisoned the source owner.");
            }
            else
            {
                native.Arm(); var initial = native.Group.ReadSourceHealth();
                native.Stock(NativePlaybackSourceRole.Replay, .125f);
                Check(read(0) == 2 && read(1) == 2 && native.Group.ReadSourceHealth().WriterSequence == initial.WriterSequence + 2,
                    "Replay stock reset and selected-rate reapply did not each keep wrapper/lower as one writer.");
                native.Stock(NativePlaybackSourceRole.Pause);
                native.Stock(NativePlaybackSourceRole.Resume, .25f);
                Check(read(0) == 4 && read(1) == 4 && native.Group.ReadSourceHealth().WriterSequence == initial.WriterSequence + 4
                    && native.Observations.Count(value => value.Role == NativePlaybackSourceRole.SetPitch) == 4
                    && native.Observations.Count(value => value.IsExactLowerPitchChild) == 4,
                    "Resume lost its exact normal wrapper/lower scheduling occurrence.");
                var clock = native.Clock;
                lower(native.Api.GameplaySource, 2);
                Check(read(0) == 5 && read(1) == 5 && native.Group.ReadSourceHealth().WriterSequence == initial.WriterSequence + 5
                    && !native.Group.GetTimeLease.ReadCurrent().Active && native.Group.ReadSourceHealth() is { Known: true, Unknown: false },
                    "A direct lower store failed to withdraw the prior native policy as a recognized boundary.");
                var entry = native.Targets.ProgressStartMoveNext;
                var enumerator = Export<CreateStart>(native.Module, "StockFixtureCreateStart")(native.Api.GameplayProgress, native.Api.GameplayClip);
                native.Api.RegisterUnityEnumerator(enumerator);
                var invoke = Export<InvokeStart>(native.Module, "StockFixtureInvokeStart");
                Check(invoke(entry.Address, enumerator, entry.MethodInfo, out var first, out var code) == 1 && code == 0 && first == 1,
                    "The actual post-lower Start did not yield normally.");
                Check(invoke(entry.Address, enumerator, entry.MethodInfo, out var last, out code) == 1 && code == 0 && last == 0,
                    "The actual post-lower Start did not complete normally.");
                native.Api.AdoptUnityInitializedPlayer();
                Check(native.Router.TryCompleteInitialization() && !native.Group.GetTimeLease.ReadCurrent().Active,
                    "The lower boundary could not capture a dormant successor from an actual new Start: " + native.Router.LastFailure);
                native.Arm();
                Check(!ReferenceEquals(native.Clock, clock) && native.Clock.Capture().Binding.RunId == clock.Capture().Binding.RunId
                    && native.Clock.Capture().Binding.SourceGeneration > clock.Capture().Binding.SourceGeneration,
                    "The actual subsequent Pause did not establish a distinct current Clock.");
            }
            native.Router.Dispose(); native.Router.PumpCompleted(); native.Group.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.CallbackFailure is null,
                "The known lower-pitch route retained roots or lost a physical completion.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 21, actualCommit = 21, retainedRoots = native.Api.ActiveRoots,
                sdkExecuted = mode == "lower-pitch-sdk", gameExecuted = false, installed = false, productionAdmission = false }));
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
