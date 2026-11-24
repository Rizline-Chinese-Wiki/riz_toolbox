using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

return RootedChecks.Run(args);

internal static class RootedChecks
{
    private static int _checks;
    private static void Check(bool value, string reason) { ++_checks; if (!value) throw new InvalidOperationException(reason); }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    private static T Clone<T>(T value) where T : class => (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;
    private static void Reject(Action action, string reason)
    { ++_checks; try { action(); } catch (Exception error) when (error is UnsupportedSignatureException or GameplayStateException or ObjectDisposedException) { return; } throw new InvalidOperationException(reason); }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind, IntPtr target, IntPtr self, ulong bits, IntPtr method, out uint code);
    private sealed class Main : IMainThreadDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public bool IsMainThread => _thread == Environment.CurrentManagedThreadId;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Wrong installation thread."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) { VerifyAccess(); action(); return Task.CompletedTask; }
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
    internal static int Run(string[] arguments)
    {
        var mode = arguments.Length > 0 ? arguments[0] : "invalid";
        try
        {
            Check(arguments.Length == 5, "Expected mode fixture GetTime-helper source-helper Dobby.");
            var module = NativeLibrary.Load(Path.GetFullPath(arguments[1]));
            using var api = new FakeNative(); api.ConfigureRootedStock(module);
            var targets = PlaybackSourceGroupTargets.ResolveOwnedFixture(module);
            var broker = new ExactHookBroker(Path.GetFullPath(arguments[4]), new Main(), new Logger());
            NativePlaybackStockRootedSource? source = null;
            var group = broker.BindPlaybackSourceGroup(targets, Path.GetFullPath(arguments[2]), Hash(arguments[2]),
                Path.GetFullPath(arguments[3]), Hash(arguments[3]), () => throw new InvalidOperationException("No Clock is issued by this layer."), _ => { }, _ => { },
                before => { if (source is null) before.Dispose(); else source.ObserveBefore(before); },
                returned => source?.ObserveReturned(returned));
            var state = new GameEventState();
            state.Scene("Boot"); state.Scene("Menu"); state.Scene("Level");
            state.SetAvailability(scenes: true, runs: true, pause: true);
            var started = state.Controller(api.GameplayInformation.ToInt64(), true, false, "rooted-stock-chart", api.GameplayController.ToInt64())
                ?? throw new InvalidOperationException("The actual shared host state did not observe a run start.");
            state.ObservePause(true);
            var binding = new NativePlaybackBinding(started.RunId, state.SceneGeneration, 1, 1,
                api.GameplayController.ToInt64(), api.GameplayInformation.ToInt64(), api.GameplayProgress.ToInt64(),
                api.GameplayPlayer.ToInt64(), api.GameplaySource.ToInt64(), api.GameplayClip.ToInt64(), targets.GetTime.MethodInfo.ToInt64());
            var origin = new NativePlaybackRateOrigin(binding.RunId, binding.ControllerGeneration, binding.Controller, binding.Information, binding.Progress, api.GameplayPause.ToInt64());
            var run = state.CapturePlaybackRun(binding.Information, binding.Controller);
            Check(ReferenceEquals(run, state.CapturePlaybackRun(binding.Information, binding.Controller)), "One current host association was replaced by a diagnostic capture.");
            Reject(() => Clone(run).VerifyCurrent(binding), "A copied run association passed the host's exact object registry.");
            Reject(() => Clone(run).Withdraw("copied cleanup", new object()), "Copied cleanup could notify the registered association's subscribers.");
            Reject(() => run.VerifyCurrent(binding with { RunId = "copied-public-run" }), "A caller's matching graph substituted another run.");
            var forged = new GameEventState.PlaybackRunAssociation(state, run.Epoch, run.SessionId, run.RunId, run.SceneGeneration, run.Information, run.Controller);
            Reject(() => forged.VerifyCurrent(binding), "A constructed equal descriptor acquired host registration.");
            Reject(() => new NativePlaybackStockRootedSource(api.GameplayApi, binding, origin, forged, group),
                "An unregistered run descriptor acquired native roots or a source observer.");
            Check(api.ActiveRoots == 0, "Rejected run admission allocated native roots.");
            Check(Task.Run(() => { try { run.VerifyCurrent(binding); return false; }
                catch (GameplayStateException error) when (error.Code == ErrorCode.WrongThread) { return true; } }).GetAwaiter().GetResult(),
                "A worker thread consumed the host run association.");
            using var brokenListener = run.OnWithdrawn(binding, _ => throw new InvalidOperationException("Independent listener failure."));
            var returnedInsidePhysical = 0;
            source = new(api.GameplayApi, binding, origin, run, group, receipt =>
            {
                Check(!receipt.PhysicalExited && api.ActiveRoots >= 7, "API readback lost the actual outer physical/root responsibility.");
                Check(receipt.RunAuthorityProven && !receipt.ProductionAdmission, "A rooted readback lost its actual host association or issued production admission.");
                ++returnedInsidePhysical;
            });
            Check(api.ActiveRoots == 7 && source.Current is null, "The rooted consumer did not hold exactly seven independent roots before any stock receipt.");
            var invoke = Export<Invoke>(module, "StockFixtureInvoke");
            void Call(NativePlaybackSourceRole role, ulong bits = 0)
            {
                var target = targets.SourceTarget(role);
                var self = role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume
                    ? api.GameplayPlayer : role == NativePlaybackSourceRole.ProgressPlay ? api.GameplayProgress : api.GameplaySource;
                var kind = role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.PauseLeaf ? 1u
                    : role is NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume or NativePlaybackSourceRole.SetPitch ? 2u
                    : role == NativePlaybackSourceRole.PlayScheduled ? 3u : role == NativePlaybackSourceRole.SetClip ? 4u : 5u;
                Check(invoke(kind, target.Address, self, bits, target.MethodInfo, out var code) == 1 && code == 0, "The real stock original failed: " + role);
            }
            if (mode == "run-inflight")
            {
                var injected = false; var retained = false;
                api.DuringRootedPitchRead = () =>
                {
                    if (injected) return; injected = true;
                    state.ControllerUnavailable();
                    Check(source.LastFailure is not null, "Host withdrawal did not synchronously stop the actual Returned consumer.");
                    source.Dispose(); retained = api.ActiveRoots >= 7 && !source.RootsReleased;
                };
                Marshal.WriteInt32(api.GameplaySource,48,1); Call(NativePlaybackSourceRole.Pause);
                Check(injected && retained && returnedInsidePhysical == 0, "Run loss during actual Returned released roots or issued a readback.");
                api.DuringRootedPitchRead = null; source.PumpCompleted();
            }
            else if (mode == "dispose-inflight")
            {
                var retained = false;
                api.DuringRootedDspRead = () => { source.Dispose(); retained = api.ActiveRoots >= 7 && !source.RootsReleased; };
                Marshal.WriteInt32(api.GameplaySource,48,1); Call(NativePlaybackSourceRole.Pause);
                Check(retained, "Callback Dispose released the seven roots before actual native finally.");
                api.DuringRootedDspRead = null; source.PumpCompleted();
            }
            else
            {
                Marshal.WriteInt32(api.GameplaySource,48,1); Call(NativePlaybackSourceRole.Pause);
                var pause = source.Current ?? throw new InvalidOperationException("The actual rooted Pause has no API readback: " + source.LastFailure);
                Check(pause.PhysicalExited && pause.Sample.AudioPositionSeconds == 3 && pause.Sample.Pitch == 1 && !pause.Sample.HasStockOrigin,
                    "Independent API Pause/media/pitch/origin readback differs from the actual native source.");
                Reject(Clone(pause).VerifyCurrent, "A copied API readback receipt was accepted.");
                if (mode == "run-during-read")
                {
                    var injected = false;
                    api.DuringRootedPitchRead = () =>
                    {
                        if (injected) return; injected = true;
                        state.ControllerUnavailable();
                        state.Controller(binding.Information, true, false, "rooted-stock-chart", binding.Controller);
                    };
                    Reject(() => pause.Read(), "A lost-and-recovered host association inside actual API readback was accepted.");
                    Check(injected && source.LastFailure is not null, "The actual getter did not execute and withdraw the old host association.");
                    api.DuringRootedPitchRead = null;
                    Reject(pause.VerifyCurrent, "Restored current addresses revived the pre-read host association.");
                }
                else if (mode.StartsWith("run-", StringComparison.Ordinal))
                {
                    var reentryRejected = false;
                    using var reentry = run.OnWithdrawn(binding, _ =>
                    {
                        try { state.CapturePlaybackRun(binding.Information, binding.Controller); }
                        catch (GameplayStateException error) when (error.Code == ErrorCode.NotReady) { reentryRejected = true; }
                    });
                    if (mode == "run-observation-gap") state.ControllerUnavailable();
                    else if (mode == "run-availability-gap")
                    {
                        state.SetAvailability(runs: false); state.SetAvailability(runs: true);
                        Reject(() => state.CapturePlaybackRun(binding.Information, binding.Controller), "Availability recovery fabricated a fresh controller observation.");
                    }
                    else if (mode == "run-scene-aba")
                    {
                        state.Scene("Menu"); state.Scene("Level");
                        Reject(() => state.CapturePlaybackRun(binding.Information, binding.Controller), "Scene recovery reused a controller observed in the previous scene.");
                    }
                    else if (mode == "run-controller-aba")
                        state.Controller(binding.Information, true, false, "rooted-stock-chart", binding.Controller + 8);
                    else if (mode == "run-over")
                        state.Controller(binding.Information, true, true, "rooted-stock-chart", binding.Controller);
                    else if (mode == "run-cancel") state.AbortFromVerifiedCallback("Actual verified cancellation boundary.");
                    else throw new InvalidOperationException("Unknown host-run mode: " + mode);
                    Check(source.LastFailure is not null && reentryRejected, "Host loss did not withdraw all listeners before reentrant capture.");
                    Reject(pause.VerifyCurrent, "A host-withdrawn readback survived the exact run edge.");
                    state.Controller(binding.Information, true, false, "rooted-stock-chart", binding.Controller);
                    Reject(() => run.VerifyCurrent(binding), "Recovered public values revived the old registered host token.");
                    Reject(pause.VerifyCurrent, "Recovered host values revived the old rooted native source.");
                    if (mode is "run-over" or "run-cancel")
                        Reject(() => state.CapturePlaybackRun(binding.Information, binding.Controller), "A terminated run could issue another current association.");
                    else
                    {
                        var successor = state.CapturePlaybackRun(binding.Information, binding.Controller);
                        Check(successor.Epoch > run.Epoch && !ReferenceEquals(successor, run), "Recovered observation reused the old host association epoch/object.");
                        successor.VerifyCurrent(binding with { SceneGeneration = state.SceneGeneration });
                        Check(successor.RunId == binding.RunId, "An observation gap fabricated a new run start.");
                    }
                }
                else if (mode == "dsp-regression")
                {
                    api.SetRootedDsp(99); Reject(() => pause.Read(), "Actual native DSP regression was accepted.");
                    api.SetRootedDsp(100); Reject(pause.VerifyCurrent, "Restored DSP revived a withdrawn rooted receipt.");
                }
                else if (mode == "graph-replacement")
                {
                    Marshal.WriteIntPtr(api.GameplayPlayer,16,api.GameplayClip);
                    Reject(pause.VerifyCurrent, "A changed actual native player/source edge was accepted.");
                    Marshal.WriteIntPtr(api.GameplayPlayer,16,api.GameplaySource);
                    Reject(pause.VerifyCurrent, "Restoring an observed graph replacement revived the old receipt.");
                }
                else if (mode == "external-aba")
                {
                    var changed = source.WritePausedPitch(pause, 2);
                    api.GameplayApi.Call(api.GameplaySource, "set_pitch", NativeRuntime.Arg.Value(1f));
                    api.GameplayApi.Call(api.GameplaySource, "set_pitch", NativeRuntime.Arg.Value(2f));
                    Reject(changed.VerifyCurrent, "Direct NativeRuntime setter ABA evaded the actual stock writer hook.");
                }
                else if (mode == "readback-mismatch")
                {
                    api.WrongRootedPitchReadback = true;
                    Reject(() => source.WritePausedPitch(pause, 2), "The native setter's normal return replaced independent API readback.");
                }
                else if (mode == "getter-writer-race")
                {
                    var changed = source.WritePausedPitch(pause,2); var injected=false;
                    api.DuringRootedPitchRead=()=>
                    {
                        if(injected)return;injected=true;
                        api.GameplayApi.Call(api.GameplaySource,"set_pitch",NativeRuntime.Arg.Value(2f));
                    };
                    Reject(()=>changed.Read(),"An equal-value native writer inside API readback was missed.");
                    Check(injected,"The actual getter race did not execute.");
                    api.DuringRootedPitchRead=null;
                }
                else
                {
                    var changed = source.WritePausedPitch(pause, 2); changed.VerifyCurrent();
                    Check(changed.Sample.Pitch == 2 && changed.Read().Sample.Pitch == 2, "The seven-root actual API did not read the native reserved write.");
                    Reject(pause.VerifyCurrent, "The old API snapshot survived a native writer successor.");
                    Call(NativePlaybackSourceRole.Replay, unchecked((uint)BitConverter.SingleToInt32Bits(.125f)));
                    var replay = source.Current ?? throw new InvalidOperationException("No actual API Replay receipt: " + source.LastFailure);
                    Check(replay.PhysicalExited && replay.Sample.Pitch == 2 && replay.Sample.AudioPositionSeconds == 0
                        && replay.ActualScheduleBits == unchecked((ulong)BitConverter.DoubleToInt64Bits(100.125)),
                        "Rooted API Replay lost actual schedule/reset/selected-rate restoration.");
                    Call(NativePlaybackSourceRole.Pause);
                    Marshal.Copy(new[]{4.25},0,IntPtr.Add(api.GameplaySource,24),1);
                    Call(NativePlaybackSourceRole.Resume, unchecked((uint)BitConverter.SingleToInt32Bits(.25f)));
                    var resume = source.Current ?? throw new InvalidOperationException("No actual API Resume receipt: " + source.LastFailure);
                    Check(resume.PhysicalExited && resume.Sample.Pitch == 2 && resume.Sample.AudioPositionSeconds == 4.25f
                        && resume.Sample.StockOriginSeconds == 96 && resume.ActualScheduleBits == unchecked((ulong)BitConverter.DoubleToInt64Bits(100.25)),
                        "The API readback conflated actual DSP schedule and distinct post-origin/media.");
                    Check(returnedInsidePhysical == 4, "The exact completed stock occurrences did not own API readback before physical exit.");
                }
                source.Dispose(); source.PumpCompleted();
            }
            Check(source.RootsReleased && api.ActiveRoots == 0, "The exact native drain did not release all seven accessor roots.");
            Check(group.IsActive && group.GetTimeLease.IsActive && broker.ReadActualPlaybackDobbyCallCounts() == (12,12),
                "A logical rooted readback boundary changed the healthy physical installation.");
            Check(api.AllGameplayReceiversRooted && api.RootedDspReads > 0 && api.RootedPitchReads > 0,
                "Actual NativeRuntime API receivers were not independently rooted.");
            Console.WriteLine(JsonSerializer.Serialize(new { kind="actual-stock-rooted-api-readback-checks",mode,passed=true,checks=_checks,
                sevenIndependentRoots=true,actualNativeRuntimeApiReadback=true,api.RootedDspReads,api.RootedPitchReads,
                api.ActiveRoots,actualDobbyPrepareCount=12,actualDobbyCommitCount=12,metadataSynthetic=true,
                hostRunAssociationIssued=true,completeNativeSourceCoverage=false,providerIssued=false,clockIssued=false,gameExecuted=false,productionAdmission=false }));
            return 0;
        }
        catch(Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new {kind="actual-stock-rooted-api-readback-checks",mode,passed=false,checks=_checks,error=error.Message}));return 1;
        }
    }
}
