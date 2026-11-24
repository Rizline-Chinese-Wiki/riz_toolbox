using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

return FormalHandoffChecks.Run(args);

internal static class FormalHandoffChecks
{
    private static int _checks;
    private static void Check(bool value, string reason) { ++_checks; if (!value) throw new InvalidOperationException(reason); }
    private static T Need<T>(Result<T> value) { Check(value.Success && value.Value is not null, value.Error?.Message ?? "Missing SDK value."); return value.Value!; }
    private static void Reject(Action action, string reason)
    { ++_checks; try { action(); } catch (Exception error) when (error is InvalidOperationException or UnsupportedSignatureException or ObjectDisposedException) { return; } throw new Exception(reason); }
    private static T Clone<T>(T value) where T : class => (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;
    private static T Private<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind, IntPtr target, IntPtr self, ulong bits, IntPtr method, out uint code);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetTime(IntPtr self, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ActionNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint WaitNative();
    [DllImport("kernel32", SetLastError = true)] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
    private sealed class Plugin : IRizPlugin
    {
        internal IPluginContext Context = null!;
        internal Plugin(string id) => Manifest = new(id, new(1, 0), new(1, 0));
        public PluginManifest Manifest { get; }
        public void Enable(IPluginContext context) => Context = context;
        public void Disable() { }
    }
    internal static int Run(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "invalid";
        try
        {
            Check(args.Length == 5, "Expected mode fixture gettime-helper source-helper Dobby.");
            Check(new[] { "normal-formal-two-run", "gettime-handoff-gap", "source-handoff-gap", "stale-access-cleanup", "worker-release", "physical-fault", "physical-code-fault", "exact-withdrawn-handoff", "prepared-stock-race" }.Contains(mode), "Unknown formal handoff mode.");
            var native = NativeLibrary.Load(Path.GetFullPath(args[1]));
            using var api = new FakeNative(); api.ConfigureRootedStock(native);
            api.SetRootedGraphValue("progress.now", 2.8); api.SetRootedGraphValue("info.nowTime", 5.6f);
            using var main = new MainThreadDispatcher(); var log = new Logger();
            var state = new GameEventState(); state.Scene("Level"); state.SetAvailability(scenes: true, runs: true, pause: true);
            var started = state.Controller(api.GameplayInformation.ToInt64(), true, false, "formal-run-one", api.GameplayController.ToInt64())!; state.ObservePause(true);
            var targets = PlaybackSourceGroupTargets.ResolveOwnedFixture(native);
            NativePlaybackBinding Graph(string runId, long generation) => new(runId, state.SceneGeneration, generation, 1,
                api.GameplayController.ToInt64(), api.GameplayInformation.ToInt64(), api.GameplayProgress.ToInt64(), api.GameplayPlayer.ToInt64(),
                api.GameplaySource.ToInt64(), api.GameplayClip.ToInt64(), targets.GetTime.MethodInfo.ToInt64());
            long[] Roots() => new[] { api.GameplayController.ToInt64(), api.GameplayInformation.ToInt64(), api.GameplayProgress.ToInt64(), api.GameplayPlayer.ToInt64(),
                api.GameplaySource.ToInt64(), api.GameplayClip.ToInt64(), api.GameplayPause.ToInt64() };
            var binding = Graph(started.RunId, 1);
            var origin = new NativePlaybackRateOrigin(binding.RunId, 1, binding.Controller, binding.Information, binding.Progress, api.GameplayPause.ToInt64());
            var run = state.CapturePlaybackRun(binding.Information, binding.Controller);
            using var router = new NativePlaybackStockClockRouter(api.GameplayApi, binding, origin, run);
            var broker = new ExactHookBroker(Path.GetFullPath(args[4]), main, log);
            NativePlaybackStockClockRouter.Session? session = null; NativePlaybackClock? finalClock = null;
            var nativeReads = 0; var sameFinalClock = true; var faultThread = 0;
            var operationWindows = new List<object>();
            var physicalFault = new ManualResetEventSlim(); var deliveredFaults = new System.Collections.Concurrent.ConcurrentBag<object>();
            Action? duringReturn = null; Action<NativePlaybackSourceObservation>? sourceInjection = null;
            NativePlaybackSourceCompletion? oldCompletion = null;
            var group = broker.BindPlaybackSourceGroup(targets, Path.GetFullPath(args[2]), Hash(args[2]), Path.GetFullPath(args[3]), Hash(args[3]),
                () => { ++nativeReads; sameFinalClock &= finalClock is not null && ReferenceEquals(finalClock, session!.Clock); return router.ReadClock(); },
                fault => { deliveredFaults.Add(fault); faultThread = Environment.CurrentManagedThreadId; router.OnGetTimeFault(fault); physicalFault.Set(); },
                fault => { deliveredFaults.Add(fault); faultThread = Environment.CurrentManagedThreadId; router.OnSourceFault(fault); physicalFault.Set(); },
                observation => { router.ObserveBefore(observation); sourceInjection?.Invoke(observation); },
                completion => { oldCompletion ??= completion; router.ObserveReturned(completion); },
                getTimeAfter: (_, _) => duringReturn?.Invoke());
            router.Attach(group);
            var physical = (group.Epoch, group.GetTimeLease.EntryAddress, group.GetTimeLease.OriginalAddress, group.SetPitchLease.EntryAddress, group.SetPitchLease.OriginalAddress);
            var installationCounts = broker.ReadActualPlaybackDobbyCallCounts();
            var installedEntries = targets.SourceRoleCount + 1; // GetTime plus this profile's source roles.
            Check(installationCounts == (installedEntries, installedEntries), "The installed sources differ from the exact fixture profile.");
            var invoke = Export<Invoke>(native, "StockFixtureInvoke"); var getTime = Marshal.GetDelegateForFunctionPointer<GetTime>(targets.GetTime.Address);
            void Call(NativePlaybackSourceRole role, float argument = 0)
            {
                var target = targets.SourceTarget(role); var self = role == NativePlaybackSourceRole.ProgressPlay ? api.GameplayProgress : api.GameplayPlayer;
                var kind = role == NativePlaybackSourceRole.Pause ? 1u : role == NativePlaybackSourceRole.ProgressPlay ? 5u : 2u;
                var bits = role == NativePlaybackSourceRole.ProgressPlay ? 0ul : unchecked((uint)BitConverter.SingleToInt32Bits(argument));
                Check(invoke(kind, target.Address, self, bits, target.MethodInfo, out var error) == 1 && error == 0, "Actual stock original failed: " + role);
            }
            void InitialPause() { Marshal.WriteInt32(api.GameplaySource, 48, 1); Call(NativePlaybackSourceRole.Pause); }
            InitialPause(); session = router.PreparePausedClock(); Check(session.TryArm(), "First final Clock could not arm."); finalClock = session.Clock;
            Reject(() => Clone(router).AdoptControlledRateHost(), "A copied Router issued a stable provider host.");
            Reject(() => Clone(router).BeginHandoff(session), "A copied Router began a real joint handoff.");
            Reject(() => Clone(router).Dispose(), "Copied Router cleanup withdrew the actual route.");
            using var provider = router.AdoptControlledRateHost();
            Check(provider.Probe().Supported && !provider.ProductionAdmission, "Actual route did not issue its controlled stable host.");
            Reject(() => router.AdoptControlledRateHost(), "One physical route issued two stable hosts.");
            Reject(() => Clone(provider).Capture(origin), "A copied stable host captured registered Session authority.");
            Reject(() => Clone(provider).Dispose(), "Copied host cleanup withdrew the actual Router.");
            Check(ReferenceEquals(provider.CaptureGetTimeHook(), group.GetTimeLease) && provider.Probe().Supported, "The stable host lost the exact current physical hook.");
            using var journal = new RunModificationJournal(main, log, state);
            var module = new GameplayModule(api.GameplayApi, main, () => state.LifecycleSnapshot(), journal.Record, state.MatchesCurrentGameplayRun, provider);
            Check(module.Probe().Ready, "Formal GameplayModule did not adopt the stable host.");
            var service = Private<GameplayService>(module, "_service");
            Check(service.Probe.Sources["clockMapping"] is not null, "Synthetic fixture bypassed the pinned ClockMapping boundary.");
            Check(!service.Probe.Operations.Single(row => row.Action == GameplayControlAction.Seek).Supported, "Formal host enabled arbitrary Seek.");
            var capabilities = new CapabilityRegistry(new("controlled-formal-stock", "fixture", "synthetic", "offline"));
            var registration = new ModuleRegistration(capabilities, module.Manifest); module.Register(registration); registration.Complete();
            capabilities.RegisterScoped<IRunModificationService>(RunModificationCapabilityIds.Read, journal.ForPlugin);
            using var host = new PluginHost(capabilities, main, new EventHub(log), log);
            var firstPlugin = new Plugin("formal-stock-one"); var secondPlugin = new Plugin("formal-stock-two");
            Check(host.Register(firstPlugin).Success && host.Register(secondPlugin).Success && host.Enable(firstPlugin.Manifest.Id).Success && host.Enable(secondPlugin.Manifest.Id).Success, "Actual PluginHost dual-scope registration failed.");
            var controls = firstPlugin.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
            var other = secondPlugin.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
            var clockView = firstPlugin.Context.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!;
            long hostTick = 0;
            GameplayControlSnapshot SampleControlState()
            {
                module.ObserveControls(++hostTick, hostTick, requested: true);
                return Need(controls.GetState());
            }
            var firstState = SampleControlState();
            Check(firstState.ObservedRunId == binding.RunId && firstState.ControllerGeneration == 1 && firstState.Operations.Single(row => row.Action == GameplayControlAction.SetSpeed).Supported,
                "Original SDK view did not observe the first actual Service generation/run.");
            var firstLease = Need(controls.Acquire(true)); Check(!other.Acquire(true).Success, "The second plugin bypassed exclusive control ownership.");
            GameplayControlRequest Request(IGameplayControlLease lease, double rate) => new(GameplayControlAction.SetSpeed, lease.ControllerGeneration, lease.ObservedRunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);
            void SetSpeed(IGameplayControlLease lease, double rate)
            {
                var before = group.ReadOperationDiagnostics(); var writes = Marshal.ReadInt32(api.GameplaySource, 20);
                var reads = 0; api.DuringRootedPitchRead = () => { ++reads; Check(ReferenceEquals(finalClock, session.Clock), "Preparation/confirmation used another final Clock."); };
                GameplayControlReceipt receipt;
                try { receipt = Need(lease.Execute(Request(lease, rate))); }
                finally { api.DuringRootedPitchRead = null; }
                var after = group.ReadOperationDiagnostics();
                Check(reads > 0 && receipt.Modification.TransactionState == GameplayControlTransactionState.Confirmed && receipt.Modification.ConfirmedValue == rate,
                    "Formal SDK transaction did not confirm the actual current native/Clock participants.");
                Check(after.FullAudits == before.FullAudits && after.FullImageAudits == before.FullImageAudits
                    && after.RateAdmissions == before.RateAdmissions + 1 && after.ClockChecks > before.ClockChecks
                    && Marshal.ReadInt32(api.GameplaySource, 20) == writes + 1,
                    "Steady per-run SDK rate control repeated complete installation audit/admission or skipped its one actual native setter.");
                Check(finalClock.Capture().Rate == rate && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == (float)rate,
                    "Formal SDK selected rate did not reach the exact Clock and actual native source.");
                operationWindows.Add(new { path = "current-run-sdk-rate", run = binding.RunId, rate, fullAudits = after.FullAudits - before.FullAudits,
                    ownerThreadFullImageAudits = after.FullImageAudits - before.FullImageAudits,
                    rateAdmissions = after.RateAdmissions - before.RateAdmissions, clockChecks = after.ClockChecks - before.ClockChecks,
                    formalFreshSourceChecks = after.SourceChecks - before.SourceChecks, actualPitchWrites = Marshal.ReadInt32(api.GameplaySource, 20) - writes, actualPitchReads = reads });
            }
            void ReadCurrentPlayback(double rate)
            {
                var before = group.ReadOperationDiagnostics(); var dsp = api.RootedDspReads; var pitch = api.RootedPitchReads;
                var readout = provider.ReadPlaybackClock(origin);
                provider.VerifyPlaybackClock(readout); provider.VerifyPlaybackClockState(readout);
                var after = group.ReadOperationDiagnostics();
                Check(ReferenceEquals(readout.Clock, finalClock) && readout.State.Binding == binding && readout.State.Rate == rate
                    && api.RootedDspReads == dsp + 2 && api.RootedPitchReads == pitch + 1,
                    "Per-run independent provider Clock read reused another run/source or omitted its actual two DSP and one pitch getters.");
                Check(after.FullAudits == before.FullAudits && after.FullImageAudits == before.FullImageAudits
                    && after.RateAdmissions == before.RateAdmissions,
                    "Per-run independent Clock read repeated installation audit or rate admission.");
                operationWindows.Add(new { path = "current-run-independent-provider-clock", run = binding.RunId, rate,
                    ownerThreadFullImageAudits = after.FullImageAudits - before.FullImageAudits,
                    fullAudits = after.FullAudits - before.FullAudits, dspReads = api.RootedDspReads - dsp, pitchReads = api.RootedPitchReads - pitch });
                var sdkBefore = group.ReadOperationDiagnostics(); var snapshot = Need(clockView.Read()); var sdkAfter = group.ReadOperationDiagnostics();
                Check(snapshot.ObservedRunId == binding.RunId && snapshot.Playback.Available && snapshot.Playback.Sample?.PlaybackRate == rate,
                    "Per-run SDK raw game Clock remained available while optional native playback disappeared.");
                Check(sdkAfter.FullAudits == sdkBefore.FullAudits && sdkAfter.FullImageAudits == sdkBefore.FullImageAudits,
                    "Independent SDK playback read repeated the complete installation audit.");
                operationWindows.Add(new { path = "current-run-independent-sdk-playback", run = binding.RunId, rate, snapshot.Playback.Available,
                    ownerThreadFullImageAudits = sdkAfter.FullImageAudits - sdkBefore.FullImageAudits,
                    fullAudits = sdkAfter.FullAudits - sdkBefore.FullAudits });
            }
            void ExerciseStock(double rate)
            {
                var before = group.ReadOperationDiagnostics(); var writes = Marshal.ReadInt32(api.GameplaySource, 20); var times = Marshal.ReadInt32(api.GameplaySource, 120);
                Call(NativePlaybackSourceRole.Replay, .125f); var scheduled = finalClock!.Capture();
                Check(scheduled.Phase == NativePlaybackPhase.Scheduled && scheduled.Rate == rate, "Actual Replay did not restore the SDK-selected rate.");
                Check(Math.Abs(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) + .125f) < .00001f, "Actual native preroll lost stock units.");
                api.SetRootedDsp(scheduled.DspAnchorSeconds + .375);
                Check(Math.Abs(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) - .375f * (float)rate) < .00001f, "Actual native GetTime did not use this run's SDK-selected rate.");
                Call(NativePlaybackSourceRole.Pause); Call(NativePlaybackSourceRole.Resume, .25f);
                var resumed = finalClock.Capture(); api.SetRootedDsp(resumed.DspAnchorSeconds + .25);
                Check(resumed.Rate == rate && Math.Abs(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) - .25f * (float)rate) < .00001f,
                    "Actual Resume/native GetTime lost the selected rate or exact leaf schedule.");
                var after = group.ReadOperationDiagnostics();
                Check(after.FullAudits == before.FullAudits && after.FullImageAudits == before.FullImageAudits,
                    "Per-run stock callbacks repeated the complete installation audit.");
                operationWindows.Add(new { path = "current-run-actual-stock-sources-gettime", run = binding.RunId, rate,
                    ownerThreadFullImageAudits = after.FullImageAudits - before.FullImageAudits, fullAudits = after.FullAudits - before.FullAudits,
                    clockChecks = after.ClockChecks - before.ClockChecks, formalFreshSourceChecks = after.SourceChecks - before.SourceChecks,
                    actualPitchWrites = Marshal.ReadInt32(api.GameplaySource, 20) - writes, actualTimeWrites = Marshal.ReadInt32(api.GameplaySource, 120) - times });
            }
            if (mode == "worker-release")
            {
                var roots = api.ActiveRoots;
                Task.Run(() => { Reject(module.Release, "Worker released the live formal Module."); Reject(provider.Dispose, "Worker disposed the stable provider host."); }).GetAwaiter().GetResult();
                Check(ReferenceEquals(service, Private<GameplayService>(module, "_service")) && api.ActiveRoots == roots && provider.Probe().Supported, "Rejected worker cleanup lost the Service or root responsibility.");
            }
            SetSpeed(firstLease, 2);
            ReadCurrentPlayback(2);
            if (mode == "prepared-stock-race")
            {
                NativePlaybackClockState? boundary = null; string? sourceFailure = null;
                using (var conflicting = NativePlaybackRateParticipants.Prepare(Request(firstLease, 1.75), finalClock, () => provider.Capture(origin), () => run.VerifyCurrent(binding)))
                {
                    Call(NativePlaybackSourceRole.Replay, .125f);
                    var stopped = finalClock.Capture(); var reason = stopped.WithdrawalReason;
                    Check(stopped.Phase == NativePlaybackPhase.Withdrawn && stopped.Rate == 2 && reason is not null && reason.Contains("raced a prepared rate baseline"),
                        "Actual prepared/stock race did not preserve its documented Clock guard: " + JsonSerializer.Serialize(stopped) + " source=" + router.LastFailure);
                    var calls = group.GetTimeLease.ReadHealth().OriginalCalls;
                    Check(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 0 && group.GetTimeLease.ReadHealth().OriginalCalls == calls + 1 && !group.GetTimeLease.ReadHealth().AuthorityActive,
                        "Prepared/stock race did not retain actual raw original completion after logical withdrawal.");
                    Reject(conflicting.Commit, "A raced prepared rate committed after native transition withdrawal.");
                    Check(api.ActiveRoots >= 14, "Raced preparation lost independent roots before cleanup.");
                    boundary = stopped; sourceFailure = router.LastFailure;
                }
                firstLease.Dispose(); module.Release(); provider.PumpCompleted();
                Check(api.ActiveRoots == 0 && router.RootsReleased, "Known completed prepared/stock race leaked roots.");
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "prepared-stock-transition-boundary", mode, passed = true, checks = _checks, phase = boundary!.Phase.ToString(),
                    rate = boundary.Rate, withdrawalReason = boundary.WithdrawalReason, sourceFailure, activeRoots = api.ActiveRoots, actualRuns = 1, crossRunPositive = false,
                    actualPrepare = installationCounts.Prepared, actualCommit = installationCounts.Committed,
                    productionAdmission = false, metadataSynthetic = true, pinnedClockMappingAvailable = false, gameExecuted = false, installed = false }));
                return 0;
            }
            ExerciseStock(2); Call(NativePlaybackSourceRole.Pause);
            Reject(() => Clone(session).BeginOwnHandoff(), "A copied Session entered native handoff through its retained publication.");
            Check(group.GetTimeLease.ReadHealth().AuthorityActive && provider.Probe().Supported
                && getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 0,
                "Rejected copied Session changed the lawful current policy or actual native return.");
            var oldSession = session; var oldClock = finalClock; var oldGraph = binding; var oldOrigin = origin; var oldRun = run; var oldRoots = Roots();
            var oldConcrete = Private<NativePlaybackStockRateProvider>(oldSession, "_rateProvider");
            var oldPublication = Private<NativePlaybackClockPublication>(oldSession, "_publication");
            var oldRooted = Private<NativePlaybackStockRootedSource>(router, "_source");
            var lateWithdraw = Private<Action<string>>(oldRooted, "_withdrawn");
            var lateReturned = Private<Action<NativePlaybackStockRootedSource.ReadbackReceipt>>(oldRooted, "_returned");
            var lateReceipt = oldRooted.Current!; var oldAuthority = group.GetTimeLease.ReadCurrent().Authority!;
            var oldAccess = provider.Capture(origin); var oldWriter = oldAccess.PreparePitchWrite();
            // Initialize only the synthetic fixture's sampled paused progress.
            // The actual stock Pause and API media sample remain authoritative;
            // this is not an execution or admission of pinned Progress.Update.
            var paused = oldAccess.Read(); api.SetRootedGraphValue("progress.now", paused.AudioPositionSeconds - paused.AppliedOffsetSeconds);
            using var oldPrepared = NativePlaybackRateParticipants.Prepare(Request(firstLease, 1.75), oldClock, () => provider.Capture(oldOrigin), () => oldRun.VerifyCurrent(oldGraph));
            oldWriter.VerifyCurrent(); var oldState = oldClock.Capture();
            NativePlaybackStockClockRouter.Handoff? handoff = null; GameEventState.PlaybackRunAssociation? nextRun = null; NativePlaybackBinding? nextGraph = null;
            void NewRun()
            {
                state.Scene("Menu"); state.Scene("Level"); state.SetAvailability(scenes: true, runs: true, pause: true); api.NextRootedStockGraph();
                api.SetRootedGraphValue("progress.now", 2.8); api.SetRootedGraphValue("info.nowTime", 5.6f);
                var next = state.Controller(api.GameplayInformation.ToInt64(), true, false, "formal-run-two", api.GameplayController.ToInt64()); state.ObservePause(true);
                Check(next is not null && next.RunId != oldGraph.RunId, "The host did not issue a new exact run association.");
                var nextState = SampleControlState();
                Check(nextState.ControllerGeneration > firstState.ControllerGeneration && nextState.ObservedRunId == next!.RunId, "The same actual Service did not observe its new generation/run.");
                nextGraph = Graph(next!.RunId, nextState.ControllerGeneration); nextRun = state.CapturePlaybackRun(nextGraph.Information, nextGraph.Controller);
                Check(Roots().Zip(oldRoots).All(pair => pair.First != pair.Second), "Successor reused a predecessor root address.");
            }
            void Gap()
            {
                var probe = provider.Probe(); Check(!probe.Supported && probe.Error?.Code == ErrorCode.NotReady, "A joint handoff gap did not report NotReady from the stable host.");
                Reject(() => provider.Capture(oldOrigin), "Stable host captured a source during the joint handoff gap.");
                Reject(() => provider.CaptureGetTimeHook(), "Stable host issued a GetTime view during the joint handoff gap.");
                var observed = SampleControlState();
                Check(!observed.Operations.Single(row => row.Action == GameplayControlAction.SetSpeed).Supported && !firstLease.Execute(Request(firstLease, 2.5)).Success,
                    "Original SDK view executed SetSpeed in the handoff gap.");
                Check(module.Tick().Ready && ReferenceEquals(service, Private<GameplayService>(module, "_service")), "Handoff gap released/recreated the formal Service.");
            }
            if (mode == "gettime-handoff-gap")
            {
                Export<ActionNative>(native, "StockFixtureBlockGetTime")(); var blocked = Task.Run(() => getTime(api.GameplayPlayer, targets.GetTime.MethodInfo));
                Check(Export<WaitNative>(native, "StockFixtureWaitGetTime")() == 0, "Actual native GetTime original did not block.");
                handoff = router.BeginHandoff(oldSession); NewRun(); Gap();
                Check(!handoff.TryAdmit(nextRun!, nextGraph!, api.GameplayPause.ToInt64()) && group.GetTimeLease.ReadHealth() is { Known: true, AuthorityActive: false, InFlight: 1 },
                    "Stable host bypassed the actual blocked GetTime/drain gate.");
                Check(api.ActiveRoots >= 21, "Actual blocked old original lost its independent root responsibility.");
                Export<ActionNative>(native, "StockFixtureReleaseGetTime")(); Check(blocked.Wait(10000) && float.IsFinite(blocked.Result), "Withdrawn actual original did not finish with stock bits.");
            }
            else if (mode == "source-handoff-gap")
            {
                var injected = false;
                sourceInjection = observation =>
                {
                    if (injected || observation.Role != NativePlaybackSourceRole.Pause || observation.ParentSequence != 0) return;
                    injected = true; handoff = router.BeginHandoff(oldSession); NewRun(); Gap();
                    Check(!handoff.TryAdmit(nextRun!, nextGraph!, api.GameplayPause.ToInt64()) && group.ReadSourceHealth().InFlight > 0 && api.ActiveRoots >= 21,
                        "Stable host bypassed the actual in-flight source/root gate.");
                };
                Call(NativePlaybackSourceRole.Pause); sourceInjection = null; Check(injected, "Actual native source handoff interleave did not execute.");
            }
            else if (mode == "exact-withdrawn-handoff")
            {
                NewRun();
                Check(oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn && !group.GetTimeLease.ReadHealth().AuthorityActive,
                    "Host run loss did not first withdraw the exact predecessor's active policy.");
                handoff = router.BeginHandoff(oldSession); Gap();
            }
            else { handoff = router.BeginHandoff(oldSession); NewRun(); Gap(); }
            Check(!group.GetTimeLease.ReadHealth().AuthorityActive, "Old native policy stayed active during joint handoff.");
            Reject(() => oldConcrete.Capture(oldOrigin), "Old concrete provider captured successor source.");
            api.GameplayApi.Call(api.GameplaySource, "set_pitch", NativeRuntime.Arg.Value(1f));
            Marshal.WriteInt64(api.GameplaySource, 24, BitConverter.DoubleToInt64Bits(3));
            Check(handoff!.TryAdmit(nextRun!, nextGraph!, api.GameplayPause.ToInt64()), "Actual drained joint handoff could not admit its successor: " + router.LastFailure);
            Check(!provider.Probe().Supported, "Stable host became available before the successor final Clock was armed.");
            InitialPause(); session = router.PreparePausedClock();
            Check(!provider.Probe().Supported && !group.GetTimeLease.ReadCurrent().Active, "Dormant successor constructor exposed active provider/native policy.");
            Check(session.TryArm(), "Successor could not arm its unique admitted final Clock."); finalClock = session.Clock;
            binding = finalClock.Capture().Binding; origin = new(binding.RunId, binding.ControllerGeneration, binding.Controller, binding.Information, binding.Progress, api.GameplayPause.ToInt64()); run = nextRun!;
            Check(binding.RunId != oldState.Binding.RunId && binding.SourceGeneration > oldState.Binding.SourceGeneration && finalClock.Capture().Revision > oldState.Revision && !ReferenceEquals(finalClock, oldClock),
                "Stable provider did not consume the actual new run/source epoch/revision/final Clock.");
            Check(provider.Probe().Supported && ReferenceEquals(provider.CaptureGetTimeHook(), group.GetTimeLease), "The same stable provider did not recover current logical availability after real arm.");
            Check(ReferenceEquals(service, Private<GameplayService>(module, "_service")), "Second run recreated the formal Service.");
            var secondState = SampleControlState(); var secondLease = Need(controls.Acquire(true));
            // The successor's native binding was formed during the gap. When
            // the descriptor becomes available, the SDK observes a new source
            // and advances its own generation before granting a lease.
            Check(secondState.ControllerGeneration == binding.ControllerGeneration + 1
                && secondLease.ControllerGeneration == secondState.ControllerGeneration
                && secondLease.ControllerGeneration > firstLease.ControllerGeneration && secondLease.ObservedRunId == binding.RunId,
                $"Successor SDK state {secondState.ControllerGeneration}, lease {secondLease.ControllerGeneration} and native binding {binding.ControllerGeneration} did not follow their independent generation rules.");
            Check(!other.Acquire(true).Success && !firstLease.Execute(Request(firstLease, 4)).Success, "Old control lease or other plugin bypassed second-run exclusive authority.");
            Reject(() => provider.Capture(oldOrigin), "Old origin borrowed the stable provider's new Session.");
            Reject(oldAccess.VerifyAuthority, "Old independent source access remained current after joint transfer.");
            Reject(oldWriter.VerifyCurrent, "Old prepared writer borrowed the new source.");
            Reject(() => oldWriter.Write(4), "Late old prepared writer wrote the new source.");
            Reject(() => oldSession.BeginOwnHandoff(), "A superseded exact Session reacquired native handoff authority.");
            Reject(oldPrepared.Commit, "Late old prepared transaction committed through the stable host.");
            oldWriter.Dispose(); oldAccess.Dispose(); oldPrepared.Dispose(); firstLease.Dispose(); oldConcrete.Dispose(); oldPublication.Withdraw("Late exact predecessor cleanup");
            oldSession.Withdraw("Late logical cleanup"); Clone(oldSession).Withdraw("Late copied logical cleanup"); lateWithdraw("Late rooted withdrawal"); lateReturned(lateReceipt);
            if (oldCompletion is not null) router.ObserveReturned(oldCompletion);
            Check(group.GetTimeLease.ReadHealth().AuthorityActive && !group.GetTimeLease.OwnsPublication(oldAuthority) && !group.GetTimeLease.OwnsPublication(oldAuthority with { }),
                "Old cleanup withdrew or impersonated the lawful successor publication.");
            Check(provider.Probe().Supported && getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 3, "Old cleanup damaged the current stable provider or actual successor GetTime.");
            if (mode == "worker-release")
            {
                var roots = api.ActiveRoots;
                Task.Run(() => { Reject(module.Release, "Second-run worker released the Module."); Reject(provider.Dispose, "Second-run worker disposed the stable host."); }).GetAwaiter().GetResult();
                Check(api.ActiveRoots == roots && provider.Probe().Supported && module.Tick().Ready, "Second-run rejected worker cleanup prevented owner retry.");
            }
            SetSpeed(secondLease, 1.5); ReadCurrentPlayback(1.5); ExerciseStock(1.5);
            var audit = Need(firstPlugin.Context.GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service!.Read());
            Check(audit.Records.Any(row => row.RunId == binding.RunId && row.Owner == firstPlugin.Manifest.Id && row.Outcome == RunModificationOutcome.Applied), "Actual shared journal omitted the second-run SDK transaction.");
            Check(audit.Records.All(row => !row.LocalResultBehaviorVerified && !row.SubmissionBehaviorVerified), "Controlled formal handoff claimed result verification.");
            if (mode == "physical-fault")
            {
                duringReturn = () => throw new InvalidOperationException("Actual successor completion fault.");
                _ = getTime(api.GameplayPlayer, targets.GetTime.MethodInfo); duringReturn = null;
                Check(physicalFault.Wait(TimeSpan.FromSeconds(10)) && faultThread != Environment.CurrentManagedThreadId && router.PhysicalFaultPending && router.FirstPhysicalFault is null,
                    "Actual permanent fault was not queued for its owner-thread Module pump.");
                Check(module.Tick().Ready && !provider.Probe().Supported && finalClock.Capture().Phase == NativePlaybackPhase.Withdrawn, "Owner Module.Tick failed to withdraw the current successor after a physical fault.");
                Check(deliveredFaults.Any(fault => ReferenceEquals(fault, router.FirstPhysicalFault)), "The physical fault's exact delivered object was discarded.");
                Reject(() => router.BeginHandoff(session), "Physical fault revived through logical handoff.");
                Check(!secondLease.Execute(Request(secondLease, 2)).Success, "SDK executed after permanent successor physical fault.");
            }
            else if (mode == "physical-code-fault")
            {
                var address = IntPtr.Add(targets.GetTime.Address, 128); var before = Marshal.ReadByte(address);
                Check(VirtualProtect(address, 1, 0x40, out var protection), "Fixture code page could not be changed.");
                try
                {
                    Marshal.WriteByte(address, (byte)(before ^ 1));
                    Check(VirtualProtect(address, 1, protection, out _), "Mutated fixture protection was not restored before explicit inspection.");
                    var auditBefore = group.ReadOperationDiagnostics();
                    // Standard runtime has no arbitrary unused-code mutation
                    // detection guarantee. This is the retained explicit full
                    // installation audit, followed by sticky withdrawal.
                    Reject(group.VerifyCurrent, "Explicit full installation audit missed actual mapped GetTime code mutation.");
                    var auditAfter = group.ReadOperationDiagnostics();
                    Check(auditAfter.FullAudits > auditBefore.FullAudits && auditAfter.FullImageAudits > auditBefore.FullImageAudits,
                        "Explicit code diagnostic rejection lacks actual complete body/image audits.");
                    operationWindows.Add(new { path = "explicit-mapped-code-full-installation-audit", run = binding.RunId,
                        fullAudits = auditAfter.FullAudits - auditBefore.FullAudits,
                        ownerThreadFullImageAudits = auditAfter.FullImageAudits - auditBefore.FullImageAudits });
                    Check(!provider.Probe().Supported, "Explicit physical audit fault left the current provider supported.");
                }
                finally
                {
                    Check(VirtualProtect(address, 1, 0x40, out _), "Mutated fixture page could not be restored.");
                    Marshal.WriteByte(address, before);
                    Check(VirtualProtect(address, 1, protection, out _), "Restored fixture page protection changed.");
                }
                Check(!provider.Probe().Supported && !group.IsActive && !group.GetTimeLease.IsActive, "Restored code revived the sticky current physical owner.");
                Reject(() => router.BeginHandoff(session), "A logical handoff revived restored physical code.");
            }
            Check(sameFinalClock && nativeReads >= 7, "Actual native callbacks used a temporary or predecessor Clock.");
            Check((group.Epoch, group.GetTimeLease.EntryAddress, group.GetTimeLease.OriginalAddress, group.SetPitchLease.EntryAddress, group.SetPitchLease.OriginalAddress) == physical
                && broker.ReadActualPlaybackDobbyCallCounts() == installationCounts, "Second run changed the same physical group's identity or installation count.");
            secondLease.Dispose(); module.Release(); provider.PumpCompleted();
            Check(!provider.Probe().Supported, "Released formal Module continued to advertise provider support.");
            var unknown = mode is "physical-fault" or "physical-code-fault";
            Check(unknown ? api.ActiveRoots >= 7 && !router.RootsReleased : api.ActiveRoots == 0 && router.RootsReleased,
                "Final logical release failed to retain Unknown responsibility or release actual drained roots.");
            Check(broker.ReadActualPlaybackDobbyCallCounts() == installationCounts, "Final logical Module release changed physical installation identity.");
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "formal-module-service-sdk-two-run-stock-handoff", mode, passed = true, checks = _checks, nativeReads,
                operationWindows, arbitraryCodeMutationDetection = mode == "physical-code-fault" ? "explicit-full-installation-audit" : "not-exercised",
                sameModuleServiceSdkView = true, independentPluginScopes = 2, actualRuns = 2, actualJointTransfers = 1,
                actualPrepare = installationCounts.Prepared, actualCommit = installationCounts.Committed,
                samePhysicalGroup = true, sameFinalClock, activeRoots = api.ActiveRoots, physicalUnknownRetained = unknown, metadataSynthetic = true,
                pinnedClockMappingAvailable = false, controlledProvider = true, completeProductionProvider = false, productionAdmission = false, gameExecuted = false, installed = false }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { kind = "formal-module-service-sdk-two-run-stock-handoff", mode, passed = false, checks = _checks, error = error.Message })); return 1; }
    }
}
