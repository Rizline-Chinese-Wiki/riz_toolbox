using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    // StockSource appends DirectCalls immediately after LastTimeBits at 0x88.
    private const int DirectCallsOffset = 0x90;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong DirectRead(uint role, uint field);
    private static ulong DirectBits(int index) => index == 0 ? 0xFEDCBA9876543210UL : index == 1 ? 0x3FF4000000000000UL : index < 4 ? 0UL : 1UL;
    private static int RunDirectAudio(string[] a, string mode)
    {
        if (mode == "direct-sdk-withdraw-contract") return RunDirectWithdrawalContract(a, mode);
        if (mode == "direct-sdk-worker-clock-contract") return RunDirectWorkerClockContract(a, mode);
        if (mode is "direct-source-first" or "direct-source-first-hold")
            return RunFirstCapture(a, mode == "direct-source-first-hold" ? "first-hold" : "first-normal",
                constructor: true, directSourceWatch: true);
        if (mode is "direct-sdk-foreign" or "direct-sdk-worker" || mode.StartsWith("direct-sdk-worker-", StringComparison.Ordinal))
            return RunDirectSdkIsolation(a, mode);
        if (mode is "direct-foreign" or "direct-worker") return RunDirectIsolation(a, mode);
        try
        {
            Check(mode is "direct-install" or "direct-runtime" || mode.StartsWith("direct-sdk-native-", StringComparison.Ordinal)
                || mode.StartsWith("direct-sdk-runtime-", StringComparison.Ordinal), "Unknown direct audio mode.");
            Check(a.Length == 5 && Hash(a[1]) == PlaybackSourceGroupTargets.DirectAudioFixtureSha256,
                "Direct audio requires the independently fixed nineteen-member fixture.");
            using var native = new ConstructorContext(a);
            Check(native.Targets.HasDirectAudioControls && native.PhysicalMemberCount == 19
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (19, 19),
                "Direct audio did not physically install all nineteen members.");
            var read = Export<DirectRead>(native.Module, "StockFixtureDirectRead");
            var sdkMode = mode.StartsWith("direct-sdk-", StringComparison.Ordinal);
            var runtime = mode.Contains("runtime", StringComparison.Ordinal);
            ConstructorSdk? sdk = sdkMode ? new(native) : null;
            try
            {
                sdk?.Speed(1.5);
                var indexes = sdkMode ? new[] { int.Parse(mode[(mode.LastIndexOf('-') + 1)..]) } : Enumerable.Range(0, 6).ToArray();
                foreach (var index in indexes)
                {
                    Check(index is >= 0 and < 6, "Unknown direct audio role.");
                    var role = (NativePlaybackSourceRole)(13 + index);
                    var target = native.Targets.SourceTarget(role);
                    var health = native.Group.ReadSourceHealth(); var before = read((uint)role, 0); var after = read((uint)role, 1);
                    var bodyCalls = Marshal.ReadInt32(native.Api.GameplaySource, DirectCallsOffset); var entered = 0;
                    var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
                    var oldClock = sdkMode ? native.Clock : null;
                    using var captured = sdk?.Provider.Capture(native.Origin);
                    using var writer = captured?.PreparePitchWrite();
                    using var prepared = sdk is null ? null : NativePlaybackRateProviderAdoption.Prepare(sdk.Provider, native.Origin,
                        new(GameplayControlAction.SetSpeed, native.Binding.ControllerGeneration, native.Binding.RunId, 2, GameplayControlValueUnit.PlaybackRateMultiplier),
                        () => native.Run.VerifyCurrent(native.Binding));
                    native.AfterDirectAudioBefore = observation =>
                    {
                        Check(observation.Role == role && observation.Self == native.Api.GameplaySource && observation.IncomingMethod == target.MethodInfo
                            && observation.ArgBits == DirectBits(index) && read((uint)role, 0) == before
                            && Marshal.ReadInt32(native.Api.GameplaySource, DirectCallsOffset) == bodyCalls
                            && observation.SchedulingSequence == health.SchedulingSequence + 1,
                            "Actual direct audio Before did not precede the original with exact raw arguments and scheduling fence.");
                        if (sdk is not null)
                        {
                            Check(oldClock!.Capture().Phase == NativePlaybackPhase.Withdrawn && !native.Group.GetTimeLease.ReadCurrent().Active
                                && !sdk.Provider.Probe().Supported && !sdk.Lease.Execute(sdk.Request(3)).Success,
                                "Direct audio retained formal SDK/Clock authority before its original.");
                            Refuse(() => { _ = rooted.Current; }, "Direct audio retained the old rooted source.");
                            Refuse(captured!.VerifyAuthority, "Direct audio retained a captured source.");
                            Refuse(writer!.VerifyCurrent, "Direct audio retained its continuous writer.");
                            Refuse(prepared!.Commit, "Direct audio committed a previously prepared rate change.");
                            prepared.Restore();
                        }
                        ++entered;
                    };
                    if (runtime) DirectRuntimeCall(native, index);
                    else
                    {
                        var kind = (uint)(index == 0 ? 8 : index == 1 ? 3 : index < 4 ? 1 : 5);
                        Check(native.Invoke(kind, target.Address, native.Api.GameplaySource, DirectBits(index), target.MethodInfo, out var code) == 1 && code == 0,
                            "Actual direct audio native caller failed.");
                    }
                    Check(native.CallbackFailure is null && entered == 1 && read((uint)role, 0) == before + 1 && read((uint)role, 1) == after + 1
                        && read((uint)role, 2) == unchecked((ulong)native.Api.GameplaySource.ToInt64()) && read((uint)role, 3) == DirectBits(index)
                        && read((uint)role, 4) == unchecked((ulong)target.MethodInfo.ToInt64())
                        && Marshal.ReadInt32(native.Api.GameplaySource, DirectCallsOffset) == bodyCalls + 1,
                        "Direct audio did not preserve raw ABI/original counts: " + native.CallbackFailure);
                    Check(native.Group.ReadSourceHealth() is { Known: true, InFlight: 0, Unknown: false }
                        && native.Group.ReadSourceHealth().SchedulingSequence == health.SchedulingSequence + 1
                        && native.Group.ReadSourceHealth().SourceSequence == health.SourceSequence,
                        "A direct audio attempt invented a source generation or physical transport fault.");
                    native.AfterDirectAudioBefore = null;
                }
                Check(!runtime || native.Api.DirectAudioRuntimeInvocations == indexes.Length && native.Api.AllGameplayReceiversRooted,
                    "The actual NativeRuntime path lost temporary source receiver roots.");
            }
            finally { native.AfterDirectAudioBefore = null; sdk?.Dispose(); }
            native.Router.Dispose(); native.Router.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(value => value.IsReleased),
                "Direct audio retained rooted graph responsibility after its known physical exit.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, actualPrepare = 19, actualCommit = 19,
                physicalSources = 18, nativeSourceVersion = 4, playerWatchVersion = 2, sdkExecuted = sdkMode, runtimeExecuted = runtime,
                runtimeCalls = native.Api.DirectAudioRuntimeInvocations, retainedRoots = native.Api.ActiveRoots,
                gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() })); return 1;
        }
    }
    private static void DirectRuntimeCall(ConstructorContext native, int index)
    {
        var api = native.Api.GameplayApi; var source = native.Api.GameplaySource;
        switch (index)
        {
            case 0: api.CallStatic(api.ObjectClass(source), "PlayHelper", NativeRuntime.Arg.Ref(source), NativeRuntime.Arg.Value(DirectBits(0))); break;
            case 1: api.Call(source, "Play", NativeRuntime.Arg.Value(BitConverter.Int64BitsToDouble(unchecked((long)DirectBits(1))))); break;
            case 2: api.Call(source, "Play"); break;
            case 3: api.Call(source, "Stop"); break;
            case 4: api.Call(source, "Stop", NativeRuntime.Arg.Value(true)); break;
            case 5: api.Call(source, "set_loop", NativeRuntime.Arg.Value(true)); break;
        }
    }
}
