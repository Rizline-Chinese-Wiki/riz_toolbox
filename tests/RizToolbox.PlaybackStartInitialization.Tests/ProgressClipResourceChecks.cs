using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong ProgressResourceRead(uint field);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong ResourceObjectRead(IntPtr reference, uint field);
    private static int RunProgressResource(string[] arguments, string mode)
    {
        if (mode is "resource-first-late-retry" or "resource-handoff-late-retry")
            return RunLateProgressResourceRetry(arguments, mode);
        if (mode is "resource-sdk-current" or "resource-withdraw-retire")
            return RunProgressResourceLifecycle(arguments, mode);
        if (mode is "resource-foreign" or "resource-worker" or "resource-seh")
            return RunProgressResourceIsolation(arguments, mode);
        if (mode is "resource-dormant-setclip" or "resource-dormant-progressclip")
            return RunProgressResourceDormant(arguments, mode);
        try
        {
            Check(arguments.Length == 5 && Hash(arguments[1]) == PlaybackSourceGroupTargets.ProgressResourceFixtureSha256,
                "Resource integration requires its independently fixed twenty-member owned fixture.");
            using var native = new ConstructorContext(arguments);
            Check(native.Targets.HasProgressResource && native.Group.HasProgressResource
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (20, 20), "Missing exact resource physical group.");
            native.Arm();
            var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
            var resource = native.Targets.ProgressClip;
            var barrier = Export<ProgressResourceRead>(native.Module, "StockFixtureProgressResourceRead");
            var getter = Export<ResourceObjectRead>(native.Module, "StockFixtureResourceRead");
            var baseline = native.Clock.Capture();
            using var future = native.Group.CaptureProgressResource(rooted, native.Api.GameplayApi);
            var basis = future.Read();
            var sourceClip = Marshal.ReadIntPtr(native.Api.GameplaySource, 40);
            var health = native.Group.ReadSourceHealth();
            var barrierBefore = barrier(0); var observations = native.Observations.Count;
            var beforeCalled = 0;
            native.AfterDirectAudioBefore = observation =>
            {
                if (observation.Role != NativePlaybackSourceRole.ProgressClip) return;
                Check(observation.Self == native.Api.GameplayProgress && observation.ReferenceArgument == native.Api.ProgressResourceB
                    && observation.IncomingMethod == IntPtr.Zero && Marshal.ReadIntPtr(observation.Self, 0x30) == native.Api.ProgressResourceA
                    && barrier(0) == barrierBefore && future.Read().Phase == 4,
                    "Resource Before did not precede the real store/barrier and expire its exact old future ticket.");
                Check(native.Clock.Capture() == baseline && native.Group.GetTimeLease.ReadCurrent().Active,
                    "Pure future-resource setter revoked the independently bound current-audio Clock.");
                ++beforeCalled;
            };
            Check(native.Invoke(4, resource.Address, native.Api.GameplayProgress,
                unchecked((ulong)native.Api.ProgressResourceB.ToInt64()), IntPtr.Zero, out var code) == 1 && code == 0,
                "Actual resource setter failed.");
            native.AfterDirectAudioBefore = null;
            Check(beforeCalled == 1 && native.CallbackFailure is null && barrier(0) == barrierBefore + 1
                && barrier(6) == barrierBefore + 1 && Marshal.ReadIntPtr(native.Api.GameplayProgress, 0x30) == native.Api.ProgressResourceB
                && Marshal.ReadIntPtr(native.Api.GameplaySource, 40) == sourceClip && getter(native.Api.ProgressResourceB, 0) == 0,
                "Pure setter did not preserve once-only reference store, barrier and independent current clip: " + native.CallbackFailure);
            var after = native.Group.ReadSourceHealth();
            Check(after.Known && after.InFlight == 0 && after.WriterSequence == health.WriterSequence
                && after.SourceSequence == health.SourceSequence && after.SchedulingSequence == health.SchedulingSequence
                && native.Observations.Count == observations + 1 && native.Clock.Capture() == baseline,
                "Resource-only attempt polluted an unrelated audio fence or failed to physically exit.");
            Refuse(() => future.VerifyCurrent(), "A resource pointer replacement revived its predecessor future credential.");
            using (var next = native.Group.CaptureProgressResource(rooted, native.Api.GameplayApi))
            {
                Check(next.Revision == basis.Revision + 1 && next.ContinuityIdentity == basis.ContinuityIdentity
                    && next.Read().Reference == unchecked((ulong)native.Api.ProgressResourceB.ToInt64()),
                    "The new reference could not obtain its exact fresh resource revision.");
            }
            if (mode is "resource-capture" or "resource-yield-retry")
            {
                var make = Export<CreateStart>(native.Module, "StockFixtureCreateStart");
                var call = Export<InvokeStart>(native.Module, "StockFixtureInvokeStart");
                IntPtr Enumerator()
                {
                    // The earlier registered enum class supplies only metadata.
                    // The real native constructor still builds each new object.
                    var value = make(native.Api.GameplayProgress, native.Api.CreateProgressResourceClip(0xdead));
                    native.Api.RegisterProgressResourceEnumerator(value);
                    return value;
                }
                uint Start(IntPtr value)
                {
                    Check(call(native.Targets.ProgressStartMoveNext.Address, value, native.Targets.ProgressStartMoveNext.MethodInfo,
                        out var raw, out var error) == 1 && error == 0, "Actual resource Start failed: " + native.Router.LastFailure);
                    return raw;
                }
                var enumerator = Enumerator();
                Check(Start(enumerator) == 1 && native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn,
                    "Actual Start did not end current-audio authority at its real initialization boundary.");
                if (mode == "resource-yield-retry")
                {
                    var interrupted = Private<NativePlaybackInitializationTracker>(native.Router, "_initialization");
                    Check(native.Invoke(4, resource.Address, native.Api.GameplayProgress,
                        unchecked((ulong)native.Api.ProgressResourceA.ToInt64()), resource.MethodInfo, out code) == 1 && code == 0
                        && interrupted.ResourceCaptureInvalidated,
                        "A resource write during the actual yield did not invalidate that initializer.");
                    native.Router.PumpCompleted();
                    Check(!native.Router.InitializationPending, "A drained resource-only interrupted future could not restart.");
                    enumerator = Enumerator(); Check(Start(enumerator) == 1, "Fresh actual resource Start did not yield.");
                }
                var expected = mode == "resource-yield-retry" ? native.Api.ProgressResourceClipA : native.Api.ProgressResourceClipB;
                Check(Start(enumerator) == 0 && Marshal.ReadIntPtr(native.Api.GameplaySource, 40) == expected,
                    "Start did not reload the resource field and deliver its real new AudioClip to set_clip.");
                native.Api.AdoptActuallyInitializedPlayer(); native.Api.AdoptProgressResourceClip(expected);
                Check(native.Router.TryCompleteInitialization() && !native.Router.InitializationPending,
                    "New actual resource did not pass native capture/source-open: " + native.Router.LastFailure);
                native.Arm();
                Check(native.Clock.Capture().Binding.Clip == expected.ToInt64() && native.Clock.Capture().Phase == NativePlaybackPhase.Paused
                    && native.Group.GetTimeLease.ReadCurrent().Active && native.UniqueFinalClock,
                    "Fresh resource capture did not admit one usable successor Clock.");
            }
            else Check(mode == "resource-current", "Unknown resource integration mode.");
            future.Dispose();
            native.Router.Dispose(); native.Router.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(value => value.IsReleased),
                "Resource integration retained roots or physical observation responsibility after known exit.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, actualPrepare = 20, actualCommit = 20,
                resourceVersion = 1, actualResourceConsumer = mode != "resource-current", currentClockPreservedBeforeStart = true,
                gameExecuted = false, productionAdmission = false, fullGenerationCompleted = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() })); return 1;
        }
    }
}
