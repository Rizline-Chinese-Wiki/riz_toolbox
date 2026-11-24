using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InvokeStock(uint kind, IntPtr target, IntPtr self, ulong bits, IntPtr method, out uint code);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint WaitNative();
    private static T Private<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static T Copy<T>(T value) where T : class => (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;
    private static void Refuse(Action action, string reason)
    {
        ++_checks;
        try { action(); }
        catch (Exception error) when (error is GameplayStateException or UnsupportedSignatureException or InvalidOperationException or ObjectDisposedException) { return; }
        throw new Exception(reason);
    }
    private static T Need<T>(Result<T> result)
    { Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing SDK value."); return result.Value!; }
    private sealed class RouterPlugin : IRizPlugin
    {
        internal IPluginContext Context = null!;
        internal RouterPlugin(string id) => Manifest = new(id, new(1, 0), new(1, 0));
        public PluginManifest Manifest { get; }
        public void Enable(IPluginContext context) => Context = context;
        public void Disable() { }
    }
    private static int RunRouter(string[] a, string mode)
    {
        try
        {
            Check(a.Length == 5 && mode is "router-normal" or "router-busy" or "router-stop-inflight" or "router-run-loss" or "router-one-cycle", "Unknown Router mode.");
            Check(Hash(a[1]) == "9fe8cde32ae1ce1fe9426efafb46a758d4e37593007a64747a042fdcab51bd6e", "Only the fixed actual Start fixture may execute.");
            var native = NativeLibrary.Load(Path.GetFullPath(a[1]));
            using var api = new FakeNative(); var enumerator = api.ConfigureInitializer(native);
            api.SetRootedGraphValue("progress.now", 2.8); api.SetRootedGraphValue("info.nowTime", 5.6f);
            var targets = PlaybackSourceGroupTargets.ResolveOwnedFixture(native);
            var state = new GameEventState(); state.Scene("Level"); state.SetAvailability(scenes: true, runs: true, pause: true);
            var started = state.Controller(api.GameplayInformation.ToInt64(), true, false, "router-initializer", api.GameplayController.ToInt64())!;
            state.ObservePause(true);
            var binding = new NativePlaybackBinding(started.RunId, state.SceneGeneration, 1, 1,
                api.GameplayController.ToInt64(), api.GameplayInformation.ToInt64(), api.GameplayProgress.ToInt64(), api.GameplayPlayer.ToInt64(),
                api.GameplaySource.ToInt64(), api.GameplayClip.ToInt64(), targets.GetTime.MethodInfo.ToInt64());
            var origin = new NativePlaybackRateOrigin(binding.RunId, binding.ControllerGeneration, binding.Controller, binding.Information, binding.Progress, api.GameplayPause.ToInt64());
            var run = state.CapturePlaybackRun(binding.Information, binding.Controller);
            using var main = new MainThreadDispatcher(); var log = new Logger();
            using var router = new NativePlaybackStockClockRouter(api.GameplayApi, binding, origin, run);
            var broker = new ExactHookBroker(Path.GetFullPath(a[4]), main, log);
            NativePlaybackStockClockRouter.Session? session = null; NativePlaybackClock? finalClock = null;
            var nativeReads = 0; var uniqueFinalClock = true;
            var operationWindows = new List<object>();
            var observations = new List<NativePlaybackSourceObservation>();
            var group = broker.BindPlaybackSourceGroup(targets, Path.GetFullPath(a[2]), Hash(a[2]), Path.GetFullPath(a[3]), Hash(a[3]),
                () => { ++nativeReads; uniqueFinalClock &= finalClock is not null && ReferenceEquals(finalClock, session!.Clock); return router.ReadClock(); },
                router.OnGetTimeFault, router.OnSourceFault,
                observation => { observations.Add(observation); router.ObserveBefore(observation); }, router.ObserveReturned);
            router.Attach(group);
            var getTime = Marshal.GetDelegateForFunctionPointer<GetTime>(targets.GetTime.Address);
            var invoke = Export<InvokeStock>(native, "StockFixtureInvoke");
            void Stock(NativePlaybackSourceRole role, float argument = 0)
            {
                var target = targets.SourceTarget(role);
                var kind = role == NativePlaybackSourceRole.Pause ? 1u : 2u;
                Check(invoke(kind, target.Address, api.GameplayPlayer, unchecked((uint)BitConverter.SingleToInt32Bits(argument)), target.MethodInfo, out var code) == 1 && code == 0,
                    "Actual stock original failed: " + role);
            }
            void Pause() { Marshal.WriteInt32(api.GameplaySource, 48, 1); Stock(NativePlaybackSourceRole.Pause); }
            var callStart = Export<InvokeStart>(native, "StockFixtureInvokeStart");
            uint Start()
            {
                Check(callStart(targets.ProgressStartMoveNext.Address, enumerator, targets.ProgressStartMoveNext.MethodInfo, out var raw, out var code) == 1 && code == 0,
                    "Actual routed Start original failed: " + router.LastFailure);
                return raw;
            }
            Pause(); session = router.PreparePausedClock(); Check(session.TryArm(), "First final Clock did not arm."); finalClock = session.Clock;
            using var provider = router.AdoptControlledRateHost();
            Check(provider.Probe().Supported && api.ActiveRoots == 7, "The actual route did not expose its first seven-root source.");
            using var journal = new RunModificationJournal(main, log, state);
            var module = new GameplayModule(api.GameplayApi, main, () => state.LifecycleSnapshot(), journal.Record, state.MatchesCurrentGameplayRun, provider);
            Check(module.Probe().Ready, "Formal Module did not adopt the same physical provider host.");
            var service = Private<GameplayService>(module, "_service");
            var capabilities = new CapabilityRegistry(new("router-initialization", "fixture", "synthetic", "offline"));
            var registration = new ModuleRegistration(capabilities, module.Manifest); module.Register(registration); registration.Complete();
            capabilities.RegisterScoped<IRunModificationService>(RunModificationCapabilityIds.Read, journal.ForPlugin);
            using var host = new PluginHost(capabilities, main, new EventHub(log), log);
            var one = new RouterPlugin("initializer-one"); var two = new RouterPlugin("initializer-two");
            Check(host.Register(one).Success && host.Register(two).Success && host.Enable(one.Manifest.Id).Success && host.Enable(two.Manifest.Id).Success, "Dual actual PluginHost scopes failed.");
            var controls = one.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
            var other = two.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
            var clockView = one.Context.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!;
            var lease = Need(controls.Acquire(true));
            long hostTick = 0;
            GameplayControlRequest Request(IGameplayControlLease value, double rate) => new(GameplayControlAction.SetSpeed, value.ControllerGeneration, value.ObservedRunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);
            void Speed(IGameplayControlLease value, double rate)
            {
                var before = group.ReadOperationDiagnostics(); var writes = Marshal.ReadInt32(api.GameplaySource, 20);
                var result = Need(value.Execute(Request(value, rate)));
                var after = group.ReadOperationDiagnostics();
                Check(result.Modification.TransactionState == GameplayControlTransactionState.Confirmed && result.Modification.ConfirmedValue == rate
                    && finalClock!.Capture().Rate == rate && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == (float)rate,
                    "The original SDK view did not confirm both actual native pitch and the one final Clock.");
                Check(after.FullAudits == before.FullAudits && after.FullImageAudits == before.FullImageAudits
                    && after.RateAdmissions == before.RateAdmissions + 1 && Marshal.ReadInt32(api.GameplaySource, 20) == writes + 1,
                    "Initialized steady SDK control repeated full installation audit/rate admission or skipped its actual native setter.");
                operationWindows.Add(new { path = "initialized-current-source-sdk-rate", sourceGeneration = finalClock!.Capture().Binding.SourceGeneration, rate,
                    fullAudits = after.FullAudits - before.FullAudits, ownerThreadFullImageAudits = after.FullImageAudits - before.FullImageAudits,
                    rateAdmissions = after.RateAdmissions - before.RateAdmissions, clockChecks = after.ClockChecks - before.ClockChecks,
                    formalFreshSourceChecks = after.SourceChecks - before.SourceChecks, actualPitchWrites = Marshal.ReadInt32(api.GameplaySource, 20) - writes });
            }
            if (mode is "router-normal" or "router-busy") Speed(lease, 2);
            Check(!other.Acquire(true).Success, "Independent plugin bypassed initial exclusive lease.");
            // The formal Service retains its own gameplay capture independently
            // of the Router's seven source roots. Account for that owner rather
            // than treating the native source as the only GC-handle consumer.
            var serviceRoots = api.ActiveRoots - 7;
            Check(serviceRoots == 7, "The same formal Service did not retain its independent seven-root capture.");
            var physical = (group.Epoch, group.GetTimeLease.EntryAddress, group.GetTimeLease.OriginalAddress, group.SetPitchLease.EntryAddress);
            var recaptures = 0; var initialized = 0; var permitChecks = 0;
            for (var cycle = 0; cycle < (mode == "router-normal" ? 2 : 1); ++cycle)
            {
                var oldSession = session; var oldClock = finalClock; var oldBinding = binding; var oldLease = lease;
                var oldSource = Private<NativePlaybackStockRootedSource>(router, "_source");
                var producer = Private<NativePlaybackStockOccurrenceProducer>(oldSource, "_producer");
                var oldPublication = Private<NativePlaybackClockPublication>(oldSession, "_publication");
                var oldState = oldClock!.Capture();
                Marshal.WriteInt32(enumerator, 0x10, 0);
                Check(Start() == 1 && router.InitializationPending && api.ActiveRoots == 9 + serviceRoots, "Yield did not retain two roots independently of the old source and formal Service captures.");
                var tracker = Private<NativePlaybackInitializationTracker>(router, "_initialization");
                var yielded = observations.Last(value => value.Role == NativePlaybackSourceRole.ProgressStartMoveNext);
                Check(tracker.Owns(yielded) && !Private<System.Collections.IDictionary>(producer, "_calls").Contains(yielded), "The retired producer also owned the routed initializer observation.");
                Check(oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn && !provider.Probe().Supported && !group.GetTimeLease.ReadHealth().AuthorityActive,
                    "Actual Start Before retained the old Clock or advertised current support.");
                Refuse(() => router.PreparePausedClock(), "Yield admitted a new Clock without initialization completion.");
                Refuse(() => router.BeginHandoff(oldSession), "Ordinary cross-run handoff bypassed pending initialization.");
                Refuse(() => Copy(router).TryCompleteInitialization(), "Copied Router consumed the initializer.");
                Refuse(() => Copy(tracker).CaptureCompleted(), "Copied tracker owned retained initialization roots.");
                Check(!router.TryCompleteInitialization() && group.ReadInitialization().Phase == 2, "Yield completed the initialization transfer.");
                Reject(group.RequireFreshAdmission, "Zero-in-flight yield reopened Fresh.");
                Check(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == (float)BitConverter.Int64BitsToDouble(Marshal.ReadInt64(api.GameplaySource, 24)), "Old policy crossed the actual native initialization hold.");
                if (mode == "router-run-loss") state.ControllerUnavailable();
                var boundaries = new List<uint>();
                var stoppedInside = false;
                Boundary boundary = (value, at) =>
                {
                    Check(value == enumerator, "Initializer changed its actual receiver."); boundaries.Add(at);
                    if (!stoppedInside) Check(!router.TryCompleteInitialization(), "An inline-store boundary admitted initialization before parent physical exit.");
                    Reject(group.RequireFreshAdmission, "Inline-store boundary reopened Fresh.");
                    if (mode == "router-stop-inflight" && at == 2)
                    {
                        router.Dispose();
                        stoppedInside = true;
                        Check(tracker.RootsRetained && api.ActiveRoots >= 2, "In-flight Router stop freed the independently owned initializer roots.");
                    }
                };
                Export<SetBoundary>(native, "StockFixtureSetStartBoundary")(boundary);
                Check(Start() == 0 && boundaries.SequenceEqual(new uint[] { 1, 2, 3, 4 }), "Same retained initializer did not execute exact final clip/stores/AL0.");
                GC.KeepAlive(boundary);
                if (mode is "router-run-loss" or "router-stop-inflight")
                {
                    router.Dispose(); router.PumpCompleted();
                    Check(!tracker.RootsRetained && api.ActiveRoots == serviceRoots && router.RootsReleased && !provider.Probe().Supported, "Ended initializer retained its own root resources after the actual physical drain.");
                    Check(observations.All(value => value.IsReleased), "The unique stopped initializer consumer did not release its actual observations.");
                    module.Release();
                    Check(api.ActiveRoots == 0, "Formal Service capture roots remained after its independent release.");
                    Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, stopped = true, initialized, activeRoots = api.ActiveRoots,
                        actualPrepare = 12, actualCommit = 12, gameExecuted = false, productionAdmission = false }));
                    return 0;
                }
                var completion = tracker.CaptureCompleted();
                Refuse(() => Copy(completion).NativeIdentityFor(group), "Copied completion consumed a private native identity.");
                api.AdoptActuallyInitializedPlayer();
                Check(api.GameplayPlayer.ToInt64() != oldBinding.Player, "Actual native Start did not install a new player.");
                var initialCalls = observations.Where(value => tracker.Owns(value)).ToArray();
                Check(initialCalls.Length == 3 && initialCalls.All(value => !Private<System.Collections.IDictionary>(producer, "_calls").Contains(value)), "Start/SetClip acquired duplicate observation owners.");
                Task<float>? blocked = null;
                if (mode == "router-busy")
                {
                    Export<VoidNative>(native, "StockFixtureBlockGetTime")();
                    blocked = Task.Run(() => getTime(api.GameplayPlayer, targets.GetTime.MethodInfo));
                    Check(Export<WaitNative>(native, "StockFixtureWaitGetTime")() == 0, "Actual raw GetTime original did not block.");
                    Check(!router.TryCompleteInitialization() && api.ActiveRoots == 9 + serviceRoots && tracker.RootsRetained && group.GetTimeLease.ReadActivity().InFlight == 1,
                        "Initialization bypassed the real joint-drain gate or released the old roots.");
                    Export<VoidNative>(native, "StockFixtureReleaseGetTime")();
                    Check(blocked.Wait(10000) && float.IsFinite(blocked.Result), "Blocked actual original did not drain.");
                }
                var injected = false;
                api.DuringRootedPitchRead = () =>
                {
                    if (injected) return; injected = true;
                    var pending = Private<object>(router, "_initializationHandoff");
                    var permit = Private<NativePlaybackInitializationTracker.CapturePermit>(pending, "_permit");
                    var captured = Private<NativePlaybackStockRootedSource>(permit, "_source");
                    var next = Private<NativePlaybackBinding>(permit, "_binding");
                    var actual = Private<NativePlaybackActualSource>(captured, "_actual");
                    var actualRoots = Private<List<NativeRuntime.RootHandle>>(actual, "_roots");
                    var expectedObjects = new long[] { next.Controller, next.Information, next.Progress, origin.Pause, next.Player, next.AudioSource, next.Clip };
                    // Call roots the receiver while resolving metadata; InvokeCore
                    // retains its independent receiver until runtime_invoke returns.
                    var capturedObjects = actualRoots.Select(value => value.Target.ToInt64()).ToArray();
                    var nativePhase = group.ReadInitialization().Phase;
                    Console.WriteLine(JsonSerializer.Serialize(new { diagnostic = "conditional-root-ownership", actualRootCount = actualRoots.Count,
                        capturedObjects, expectedObjects, activeRoots = api.ActiveRoots, serviceRoots, nativePhase, temporaryReceiverRoots = 2 }));
                    Check(actualRoots.Count == 7 && capturedObjects.SequenceEqual(expectedObjects)
                        && api.ActiveRoots == 18 + serviceRoots && nativePhase == 6,
                        "Conditional capture lost exact owned roots, one of the two invocation receiver roots, or its native hold.");
                    Refuse(() => new NativePlaybackStockRootedSource(api.GameplayApi, next, origin, run, group), "Ordinary construction bypassed the native Fresh hold.");
                    Refuse(() => Copy(permit).Verify(captured, group, next), "Copied typed capture permit retained admission.");
                    Refuse(() => permit.Verify(captured, group, next with { Clip = oldBinding.Clip + 8 }), "Typed capture accepted another graph.");
                    Refuse(() => permit.Claim(captured, group, next), "The initialization permit claimed a second capture.");
                    ++permitChecks;
                };
                Check(router.TryCompleteInitialization(), "Actual completed initializer did not admit/open its rooted same-run successor: " + router.LastFailure);
                api.DuringRootedPitchRead = null; ++initialized; ++recaptures;
                Check(injected && !router.InitializationPending && !tracker.RootsRetained && api.ActiveRoots == 7 + serviceRoots
                    && initialCalls.All(value => value.IsReleased) && group.ReadInitialization() is { Phase: 7, Blocked: 0 }, "Source-open failed to return to Fresh and release only the initializer's actual responsibilities.");
                group.RequireFreshAdmission();
                Check(!provider.Probe().Supported && !group.GetTimeLease.ReadCurrent().Active, "Initialized graph became a Clock before a later actual Pause.");
                Refuse(() => router.PreparePausedClock(), "Initializer completion substituted for the later actual Pause.");
                Refuse(() => oldSession.BeginOwnInitializationHandoff(tracker, completion), "Old initializer completion replayed its native same-run grant.");
                Pause(); session = router.PreparePausedClock();
                Check(!provider.Probe().Supported && !group.GetTimeLease.ReadCurrent().Active, "Dormant successor exposed an active native policy.");
                Check(session.TryArm(), "Actual Pause/readback failed to arm the initialized unique final Clock."); finalClock = session.Clock;
                binding = finalClock.Capture().Binding;
                Check(binding.RunId == oldBinding.RunId && binding.SceneGeneration == oldBinding.SceneGeneration && binding.ControllerGeneration == oldBinding.ControllerGeneration
                    && binding.SourceGeneration > oldBinding.SourceGeneration && binding.Player == api.GameplayPlayer.ToInt64()
                    && finalClock.Capture().Revision > oldState.Revision && !ReferenceEquals(finalClock, oldClock), "Initialized successor changed its host run or lost native epoch/revision/unique Clock allocation.");
                Check(provider.Probe().Supported && provider.CaptureSourceDescriptor() == new NativePlaybackRateSourceDescriptor(binding, origin.Pause)
                    && ReferenceEquals(provider.CaptureGetTimeHook(), group.GetTimeLease), "Stable provider did not expose the armed current descriptor and the same physical hook.");
                Check(module.Tick().Ready && ReferenceEquals(service, Private<GameplayService>(module, "_service")), "Initialization recreated the same Module/Service.");
                module.ObserveControls(++hostTick, hostTick, requested: true);
                var nextState = Need(controls.GetState()); lease = Need(controls.Acquire(true));
                var clockBefore = group.ReadOperationDiagnostics(); var dspBefore = api.RootedDspReads; var pitchBefore = api.RootedPitchReads;
                var currentOrigin = new NativePlaybackRateOrigin(binding.RunId, binding.ControllerGeneration, binding.Controller, binding.Information, binding.Progress, origin.Pause);
                var directClock = provider.ReadPlaybackClock(currentOrigin);
                provider.VerifyPlaybackClock(directClock); provider.VerifyPlaybackClockState(directClock);
                var clockAfter = group.ReadOperationDiagnostics();
                Check(ReferenceEquals(directClock.Clock, finalClock) && directClock.State.Binding == binding
                    && api.RootedDspReads == dspBefore + 2 && api.RootedPitchReads == pitchBefore + 1
                    && clockAfter.FullAudits == clockBefore.FullAudits && clockAfter.FullImageAudits == clockBefore.FullImageAudits,
                    "Initialized independent Clock read borrowed predecessor samples or repeated complete installation audit.");
                var directDspReads = api.RootedDspReads - dspBefore; var directPitchReads = api.RootedPitchReads - pitchBefore;
                var sdkBefore = group.ReadOperationDiagnostics(); var sdkClock = Need(clockView.Read()); var sdkAfter = group.ReadOperationDiagnostics();
                Check(sdkAfter.FullAudits == sdkBefore.FullAudits && sdkAfter.FullImageAudits == sdkBefore.FullImageAudits,
                    "Initialized independent SDK playback read repeated complete installation audit.");
                operationWindows.Add(new { path = "initialized-current-source-independent-clock", sourceGeneration = binding.SourceGeneration,
                    directDspReads, directPitchReads,
                    providerFullAudits = clockAfter.FullAudits - clockBefore.FullAudits, providerOwnerThreadFullImageAudits = clockAfter.FullImageAudits - clockBefore.FullImageAudits,
                    sdkFullAudits = sdkAfter.FullAudits - sdkBefore.FullAudits, sdkOwnerThreadFullImageAudits = sdkAfter.FullImageAudits - sdkBefore.FullImageAudits,
                    sdkClock.Playback.Available });
                Check(lease.ControllerGeneration > oldLease.ControllerGeneration && nextState.ControllerGeneration == lease.ControllerGeneration
                    && lease.ObservedRunId == oldLease.ObservedRunId && !oldLease.Execute(Request(oldLease, 4)).Success && !other.Acquire(true).Success,
                    "Same-run initialized source retained the old control lease or allowed a second plugin.");
                Check(sdkClock.ControllerGeneration == lease.ControllerGeneration && sdkClock.ObservedRunId == lease.ObservedRunId
                    && sdkClock.GameTime.Available && sdkClock.Playback.Available && sdkClock.Playback.Sample is not null,
                    "The original SDK clock view did not sample the successor's optional native Clock through the same Service generation.");
                oldLease.Dispose(); oldSession.Withdraw("Late predecessor cleanup"); oldPublication.Withdraw("Late predecessor publication cleanup"); oldSource.Dispose();
                Check(provider.Probe().Supported && group.GetTimeLease.ReadCurrent().Active, "Late predecessor cleanup damaged the initialized successor.");
                Speed(lease, cycle == 0 ? 1.5 : 2.5);
                Stock(NativePlaybackSourceRole.Replay, .125f); var scheduled = finalClock.Capture();
                api.SetRootedDsp(scheduled.DspAnchorSeconds + .5);
                Check(Math.Abs(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) - .5f * (float)scheduled.Rate) < .00001f, "Actual successor native GetTime did not consume the same final Clock after SDK rate confirmation.");
                Stock(NativePlaybackSourceRole.Pause); Stock(NativePlaybackSourceRole.Resume, .25f); var resumed = finalClock.Capture();
                api.SetRootedDsp(resumed.DspAnchorSeconds + .375);
                Check(resumed.Rate == scheduled.Rate && Math.Abs(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) - .375f * (float)resumed.Rate) < .00001f,
                    "Actual initialized Resume lost the SDK-selected rate/SetTime/pitch/schedule path.");
                Stock(NativePlaybackSourceRole.Pause);
                api.SetRootedGraphValue("progress.now", -.2); api.SetRootedGraphValue("info.nowTime", -.2f * (float)resumed.Rate);
            }
            Check(uniqueFinalClock && nativeReads >= 2 && (group.Epoch, group.GetTimeLease.EntryAddress, group.GetTimeLease.OriginalAddress, group.SetPitchLease.EntryAddress) == physical
                && broker.ReadActualPlaybackDobbyCallCounts() == (12, 12), "Initialization replaced physical hooks or used a temporary final Clock.");
            lease.Dispose(); module.Release(); provider.PumpCompleted();
            Check(router.RootsReleased && api.ActiveRoots == 0 && !provider.Probe().Supported && observations.All(value => value.IsReleased), "Final actual drain leaked or double-owned native roots/observations.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, initialized, sevenRootRecaptures = recaptures, permitChecks,
                operationWindows, formalFreshSourceScopeCovered = false,
                sameRun = true, sameModuleServiceSdk = true, independentPluginScopes = 2, uniqueFinalClock, nativeReads, actualPrepare = 12, actualCommit = 12,
                activeRoots = api.ActiveRoots, initializationProtocol = 2, sourceVersion = 4, gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() })); return 1;
        }
    }
}
