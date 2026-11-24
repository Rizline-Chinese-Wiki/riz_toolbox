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
    private static readonly List<FakeNative> FirstCaptureRetainedFixtures = new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AllocationCount();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateForeignShared(uint classIndex, IntPtr reference);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ConfigureWaitUntil(IntPtr enumerator, IntPtr receiver, IntPtr reference, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong ReadSharedConstructor(uint field);

    private static int RunFirstCapture(string[] a, string mode, bool constructor = false, bool sourceWatch = false, bool directSourceWatch = false, bool progressForeign = false)
    {
        FakeNative? fixtureOwner = null;
        var reportedMode = progressForeign ? "progress-" + mode
            : directSourceWatch ? mode == "first-normal" ? "direct-source-first" : "direct-source-" + mode : constructor ? "ctor-" + mode : mode;
        try
        {
            Check(a.Length == 5 && mode is "first-normal" or "first-prepopulated" or "first-busy" or "first-hold" or "first-stop-inflight" or "first-run-loss" or "first-throw" or "first-clip-mismatch", "Unknown first-capture mode.");
            Check(Hash(a[1]) == (directSourceWatch ? PlaybackSourceGroupTargets.DirectAudioFixtureSha256
                : constructor ? PlaybackSourceGroupTargets.ConstructorFixtureSha256 : "7ef26e1c274cc1cf826fb8e7a9283e913224a6c9f9c6a35cb76103b943c19d7f"), "Only the fixed owned Start fixture may execute.");
            var native = NativeLibrary.Load(Path.GetFullPath(a[1]));
            var api = fixtureOwner = new FakeNative(); var enumerator = api.ConfigureInitializer(native);
            var template = api.GameplayPlayer; var nullPlayer = mode != "first-prepopulated";
            api.SetRootedGraphValue("progress.now", 2.8); api.SetRootedGraphValue("info.nowTime", 5.6f);
            if (constructor) api.ConfigureSharedConstructor(native);
            // Configure every MethodInfo/class and the retained Runtime API
            // before the host captures its first independent roots.
            if (directSourceWatch) api.ConfigureDirectAudio(native);
            var targets = directSourceWatch ? PlaybackSourceGroupTargets.ResolveOwnedDirectAudioFixture(native)
                : constructor ? PlaybackSourceGroupTargets.ResolveOwnedConstructorFixture(native) : PlaybackSourceGroupTargets.ResolveOwnedFixture(native);
            ReadSharedConstructor? readShared = null;
            if (constructor)
            {
                // Start creates WaitUntil through this same physical native
                // body. Its distinct actual receiver must be ignored before
                // journaling even with the initializer's unrelated MethodInfo.
                var wait = Export<CreateForeignShared>(native, "StockFixtureCreateForeignSharedReceiver")(0, IntPtr.Zero);
                Check(wait != IntPtr.Zero && wait != api.GameplayPlayer, "Missing distinct native WaitUntil receiver.");
                Export<ConfigureWaitUntil>(native, "StockFixtureConfigureStartWaitUntil")(enumerator, wait, api.GameplaySource, targets.ProgressStartMoveNext.MethodInfo);
                readShared = Export<ReadSharedConstructor>(native, "StockFixtureConstructorRead");
            }
            var state = new GameEventState(); state.Scene("Level"); state.SetAvailability(scenes: true, runs: true, pause: true);
            state.Controller(api.GameplayInformation.ToInt64(), true, false, "first-initializer", api.GameplayController.ToInt64()); state.ObservePause(true);
            var run = state.CapturePlaybackRun(api.GameplayInformation.ToInt64(), api.GameplayController.ToInt64());
            if (nullPlayer) Marshal.WriteIntPtr(api.GameplayProgress, 0x38, IntPtr.Zero);
            var descriptor = NativePlaybackInitializationHost.Capture(api.GameplayApi, run);
            Refuse(() => Copy(descriptor).VerifyCurrent(api.GameplayApi, run), "Copied host descriptor retained initialization authority.");
            Refuse(() => NativePlaybackInitializationHost.Capture(api.GameplayApi, Copy(run)), "Copied host association created initialization authority.");
            Check(api.ActiveRoots == 4 && descriptor.RootsRetained, "Host-only capture failed its independent four host roots.");
            using var main = new MainThreadDispatcher(); var log = new Logger();
            using var router = new NativePlaybackStockClockRouter(api.GameplayApi, descriptor, run);
            var broker = new ExactHookBroker(Path.GetFullPath(a[4]), main, log);
            var observations = new List<NativePlaybackSourceObservation>();
            NativePlaybackStockClockRouter.Session? session = null; NativePlaybackClock? finalClock = null;
            var nativeReads = 0; var uniqueFinalClock = true;
            Action<NativePlaybackSourceObservation>? constructorBefore = null;
            Action<NativePlaybackSourceObservation>? directBefore = null;
            string? constructorCallbackFailure = null;
            string? directCallbackFailure = null;
            var group = broker.BindPlaybackSourceGroup(targets, Path.GetFullPath(a[2]), Hash(a[2]), Path.GetFullPath(a[3]), Hash(a[3]),
                () => { ++nativeReads; uniqueFinalClock &= finalClock is not null && ReferenceEquals(finalClock, session!.Clock); return router.ReadClock(); },
                router.OnGetTimeFault, router.OnSourceFault,
                value =>
                {
                    observations.Add(value); router.ObserveBefore(value);
                    if (value.Role == NativePlaybackSourceRole.PlayerConstructor && constructorBefore is not null)
                        try { constructorBefore(value); }
                        catch (Exception error) { constructorCallbackFailure ??= error.ToString(); }
                    if (value.Role >= NativePlaybackSourceRole.AudioPlayHelper && directBefore is not null)
                        try { directBefore(value); }
                        catch (Exception error) { directCallbackFailure ??= error.ToString(); }
                }, router.ObserveReturned);
            router.Attach(group);
            var physical = (group.Epoch, group.GetTimeLease.EntryAddress, group.GetTimeLease.OriginalAddress, group.SetPitchLease.EntryAddress);
            if (directSourceWatch) Check(targets.HasDirectAudioControls && targets.SourceRoleCount == 18
                && broker.ReadActualPlaybackDobbyCallCounts() == (19, 19), "First direct capture lacks nineteen actual physical Prepare/Commit calls.");
            var getTime = Marshal.GetDelegateForFunctionPointer<GetTime>(targets.GetTime.Address);
            var invoke = Export<InvokeStock>(native, "StockFixtureInvoke");
            void Stock(NativePlaybackSourceRole role, float argument = 0)
            {
                var target = targets.SourceTarget(role);
                Check(invoke(role == NativePlaybackSourceRole.Pause ? 1u : 2u, target.Address, api.GameplayPlayer,
                    unchecked((uint)BitConverter.SingleToInt32Bits(argument)), target.MethodInfo, out var code) == 1 && code == 0, "Actual stock original failed: " + role);
            }
            void Pause() { Marshal.WriteInt32(api.GameplaySource, 48, 1); Stock(NativePlaybackSourceRole.Pause); }
            var callStart = Export<InvokeStart>(native, "StockFixtureInvokeStart");
            uint Start()
            {
                Check(callStart(targets.ProgressStartMoveNext.Address, enumerator, targets.ProgressStartMoveNext.MethodInfo, out var raw, out var code) == 1 && code == 0,
                    "Actual first Start original failed: " + router.LastFailure); return raw;
            }
            Check(api.ActiveRoots == 4 && Private<object?>(router, "_source") is null && Private<object?>(router, "_session") is null
                && group.GetTimeLease.ReadCurrent().Authority is null && nativeReads == 0, "Host-only Attach fabricated roots/session/publication/Clock reader.");
            Refuse(() => router.PreparePausedClock(), "Host-only route admitted a Clock before Start.");
            Refuse(() => router.AdoptControlledRateHost(), "Host-only route admitted a provider before first Clock.");
            if (progressForeign)
            {
                VerifyActualProgressWatch(api, targets, group, descriptor,
                    Private<List<NativeRuntime.RootHandle>>(descriptor, "_roots")[2], hostOnly: true);
                CheckActualProgressForeign(api, native, targets, group, () => observations.Count, () => nativeReads);
            }
            Check(Start() == 1 && (!nullPlayer || Marshal.ReadIntPtr(api.GameplayProgress, 0x38) == IntPtr.Zero)
                && api.ActiveRoots == 6 && router.InitializationPending, "Actual AL1 yield did not retain four host roots plus independent enum/Progress roots.");
            if (constructor) Check(readShared!(0) == 1 && readShared(1) == 1
                && observations.All(value => value.Role != NativePlaybackSourceRole.PlayerConstructor),
                "Nested foreign shared constructor acquired a first-yield journal entry or missed its original.");
            var tracker = Private<NativePlaybackInitializationTracker>(router, "_initialization");
            Check(observations.Count == 1 && tracker.Owns(observations[0]) && tracker.RootsRetained, "First Start lacked unique retained consumer.");
            Check(!router.TryCompleteInitialization() && group.ReadInitialization() is { Phase: 2, Blocked: 1 }, "Incomplete first yield admitted source.");
            Reject(group.RequireFreshAdmission, "Zero-in-flight first yield reopened ordinary Fresh.");
            Refuse(() => router.PreparePausedClock(), "First yield admitted a Clock without physical completion.");
            Refuse(() => Copy(router).TryCompleteInitialization(), "Copied Router consumed first initialization.");
            Refuse(() => Copy(tracker).CaptureCompleted(), "Copied tracker consumed first retained completion.");
            if (mode == "first-hold")
            {
                for (var repeat = 0; repeat < 3; ++repeat) Check(!router.TryCompleteInitialization(), "Missing final parent acquired first-capture receipt.");
                router.Dispose(); router.PumpCompleted();
                Check(router.RootsReleased && api.ActiveRoots == 0 && observations.All(value => value.IsReleased), "Stopped incomplete initializer leaked drained roots/observations.");
                Reject(group.RequireFreshAdmission, "Stopped incomplete initializer cleared native Fresh hold.");
                Check(group.GetTimeLease.ReadCurrent().Authority is null && nativeReads == 0, "Incomplete initializer published native authority.");
                return FirstResult(reportedMode, nativeReads, false, 0, true, nullPlayer);
            }
            if (mode == "first-run-loss") state.ControllerUnavailable();
            if (mode == "first-throw") Marshal.WriteInt32(enumerator, 0x34, 2);
            var boundaries = new List<uint>(); var stopped = false;
            Boundary boundary = (value, at) =>
            {
                Check(value == enumerator, "First initializer changed retained receiver."); boundaries.Add(at);
                if (!stopped) Check(!router.TryCompleteInitialization(), "First capture crossed live native parent/child boundary.");
                Reject(group.RequireFreshAdmission, "Inline-store boundary reopened ordinary Fresh.");
                if (mode == "first-stop-inflight" && at == 2)
                {
                    router.Dispose(); stopped = true;
                    Check(tracker.RootsRetained && api.ActiveRoots == 6 && !router.RootsReleased, "In-flight stop released first initializer roots.");
                }
            };
            Export<SetBoundary>(native, "StockFixtureSetStartBoundary")(boundary);
            if (mode == "first-throw")
            {
                Check(callStart(targets.ProgressStartMoveNext.Address, enumerator, targets.ProgressStartMoveNext.MethodInfo, out _, out var code) == 2 && code == 0xe2400032,
                    "Actual first initializer did not unwind native exception.");
                try { Check(!router.TryCompleteInitialization(), "Abnormal first initializer admitted source."); }
                catch (UnsupportedSignatureException) { ++_checks; }
                router.Dispose(); router.PumpCompleted();
                Check(tracker.RootsRetained && api.ActiveRoots == 6 && !router.RootsReleased, "Unknown native responsibility reported safely released.");
                GC.KeepAlive(boundary); Export<SetBoundary>(native, "StockFixtureSetStartBoundary")(null!);
                return FirstResult(reportedMode, nativeReads, false, api.ActiveRoots, false, nullPlayer);
            }
            Check(Start() == 0 && boundaries.SequenceEqual(new uint[] { 1, 2, 3, 4 }), "First state1 did not execute exact clip/stores/AL0.");
            GC.KeepAlive(boundary); Export<SetBoundary>(native, "StockFixtureSetStartBoundary")(null!);
            if (constructor) Check(readShared!(0) == 2 && readShared(1) == 2
                && observations.All(value => value.Role != NativePlaybackSourceRole.PlayerConstructor),
                "Nested foreign shared constructor interfered with the real final initializer child chain.");
            Check(Export<AllocationCount>(native, "StockFixtureStartAllocationCount")() == 1
                && Export<AllocationCount>(native, "StockFixtureStartNoPlayerAllocationCount")() == (nullPlayer ? 1u : 0u),
                "Actual first allocation read an old player or did not execute its native allocation path.");
            if (mode is "first-stop-inflight" or "first-run-loss")
            {
                if (mode == "first-run-loss") Check(!router.TryCompleteInitialization(), "Host loss admitted completed first graph.");
                router.Dispose(); router.PumpCompleted();
                Check(!tracker.RootsRetained && api.ActiveRoots == 0 && router.RootsReleased && observations.All(value => value.IsReleased), "First initializer did not release after actual physical drain.");
                Reject(group.RequireFreshAdmission, "Stop/host loss cleared native hold.");
                Check(group.GetTimeLease.ReadCurrent().Authority is null && nativeReads == 0, "Stopped first initializer created publication/reader.");
                return FirstResult(reportedMode, nativeReads, false, 0, true, nullPlayer);
            }
            var completion = tracker.CaptureCompleted();
            Refuse(() => Copy(completion).NativeIdentityFor(group), "Copied first completion acquired native identity.");
            api.AdoptActuallyInitializedPlayer();
            Check(api.GameplayPlayer != template && Marshal.ReadIntPtr(api.GameplayProgress, 0x38) == api.GameplayPlayer, "First completion did not capture new native allocation/store.");
            if (mode == "first-clip-mismatch")
            {
                // Actual NativeRuntime get_clip observes this native source
                // field; candidate still records the exact original child arg.
                Marshal.WriteIntPtr(api.GameplaySource, 40, IntPtr.Add(api.GameplayClip, 8));
                Refuse(() => router.TryCompleteInitialization(), "Actual get_clip mismatch admitted first source.");
                Check(api.ActiveRoots == 6 && tracker.RootsRetained && group.ReadInitialization() is { Phase: 6, Blocked: 1 }, "Partial seven-root capture leaked or native hold was lost.");
                Refuse(() => router.PreparePausedClock(), "Failed actual clip readback admitted Clock.");
                Reject(group.RequireFreshAdmission, "Failed actual clip readback reopened Fresh.");
                router.Dispose(); router.PumpCompleted();
                Check(api.ActiveRoots == 0 && router.RootsReleased && observations.All(value => value.IsReleased), "Failed capture leaked drained initializer resources.");
                Check(group.GetTimeLease.ReadCurrent().Authority is null && nativeReads == 0, "Failed clip capture fabricated publication/reader.");
                return FirstResult(reportedMode, nativeReads, false, 0, true, nullPlayer);
            }
            if (mode == "first-busy")
            {
                Export<VoidNative>(native, "StockFixtureBlockGetTime")();
                var blocked = Task.Run(() => getTime(api.GameplayPlayer, targets.GetTime.MethodInfo));
                Check(Export<WaitNative>(native, "StockFixtureWaitGetTime")() == 0, "Actual raw GetTime did not block.");
                Check(!router.TryCompleteInitialization() && api.ActiveRoots == 6 && tracker.RootsRetained, "First capture bypassed actual GetTime drain.");
                var waiting = Private<object>(router, "_firstCaptureAdmission");
                var receipt = Private<NativePlaybackSingleReturnBridge.InitialCapture>(waiting, "_native");
                Check(receipt.Read(1) is { Epoch: 0, GetTimeInFlight: 1 } && group.GetTimeLease.ReadCurrent().Authority is null, "Waiting capture invented epoch/publication.");
                Refuse(() => Copy(receipt).Read(), "Copied first native receipt retained drain authority.");
                Export<VoidNative>(native, "StockFixtureReleaseGetTime")();
                Check(blocked.Wait(10000) && float.IsFinite(blocked.Result), "Actual first GetTime original failed to drain.");
            }
            var conditional = false; NativePlaybackInitializationTracker.CapturePermit? permit = null;
            NativePlaybackStockRootedSource? source = null; NativePlaybackBinding? binding = null;
            NativePlaybackSingleReturnBridge.InitialCapture? first = null;
            api.DuringRootedPitchRead = () =>
            {
                if (conditional) return; conditional = true;
                var admission = Private<object>(router, "_firstCaptureAdmission");
                permit = Private<NativePlaybackInitializationTracker.CapturePermit>(admission, "_permit");
                first = Private<NativePlaybackSingleReturnBridge.InitialCapture>(admission, "_native");
                source = Private<NativePlaybackStockRootedSource>(permit, "_source"); binding = Private<NativePlaybackBinding>(permit, "_binding");
                var roots = Private<List<RizToolbox.Runtime.NativeRuntime.RootHandle>>(Private<NativePlaybackActualSource>(source, "_actual"), "_roots");
                Check(roots.Select(value => value.Target.ToInt64()).SequenceEqual(new[] { binding.Controller, binding.Information, binding.Progress,
                    descriptor.Origin.Pause, binding.Player, binding.AudioSource, binding.Clip }) && api.ActiveRoots == 15 && group.ReadInitialization() is { Phase: 6, Blocked: 1 },
                    "First capture lost exact seven objects, four host roots, two initializer roots, two temporary receivers or hold.");
                Check(first.Read(2).Epoch == (ulong)binding.SourceGeneration && group.GetTimeLease.ReadCurrent().Authority is null, "First native drain lost source epoch or invented publication.");
                Refuse(() => new NativePlaybackStockRootedSource(api.GameplayApi, binding, descriptor.Origin, run, group), "Ordinary source construction bypassed first hold.");
                Refuse(() => Copy(permit).Verify(source, group, binding), "Copied first typed permit retained capture authority.");
                Refuse(() => permit.Verify(source, group, binding with { Player = template.ToInt64() }), "First typed permit accepted allocation-template identity.");
                Refuse(() => permit.Claim(source, group, binding), "First typed permit allowed second source claim.");
                Refuse(() => Copy(first).Read(), "Copied first native receipt passed registration.");
            };
            Check(router.TryCompleteInitialization(), "First native completion failed conditional capture/source-open: " + router.LastFailure);
            api.DuringRootedPitchRead = null;
            Check(conditional && api.ActiveRoots == 7 && !tracker.RootsRetained && !router.InitializationPending && observations.All(value => value.IsReleased)
                && group.ReadInitialization() is { Phase: 7, Blocked: 0 }, "First source-open failed exact root/observation ownership.");
            group.RequireFreshAdmission();
            Check(first!.Read(3).Epoch > 0 && group.GetTimeLease.ReadCurrent().Authority is null && nativeReads == 0, "Initialization fabricated first publication/Clock reader.");
            Refuse(() => permit!.Verify(source!, group, binding!), "Opened first capture permit was replayable.");
            Refuse(() => router.PreparePausedClock(), "First completion substituted for later actual Pause.");
            Pause(); session = router.PreparePausedClock();
            Check(!group.GetTimeLease.ReadCurrent().Active && nativeReads == 0, "First dormant policy activated before final Clock.");
            Check(session.TryArm(), "Later actual Pause/readback failed to arm first Clock."); finalClock = session.Clock;
            Check(first.Read(4).Epoch == (ulong)finalClock.Capture().Binding.SourceGeneration && finalClock.Capture().Revision == 1, "First Clock invented predecessor revision or lost admitted epoch.");
            if (directSourceWatch)
                return FinishFirstDirectAudioSourceWatch(api, native, targets, group, router, session,
                    finalClock, template, observations, readShared!, getTime, broker, state, run, main, log,
                    value => directBefore = value, () => directCallbackFailure, () => nativeReads, () => uniqueFinalClock);
            if (sourceWatch)
                return FinishFirstConstructorSourceWatch(api, native, targets, group, router, session,
                    finalClock, template, observations, readShared!, getTime, broker, state, main, log,
                    value => constructorBefore = value, () => constructorCallbackFailure);
            if (mode != "first-normal")
            {
                Check(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == (float)finalClock.Capture().MediaAnchorSeconds && nativeReads == 1 && uniqueFinalClock,
                    "First active native GetTime did not consume its sole final Clock.");
                router.Dispose(); router.PumpCompleted();
                Check(api.ActiveRoots == 0 && router.RootsReleased && observations.All(value => value.IsReleased), "First positive final drain leaked roots/observations.");
                return FirstResult(reportedMode, nativeReads, false, 0, true, nullPlayer);
            }
            using var provider = router.AdoptControlledRateHost();
            using var journal = new RunModificationJournal(main, log, state);
            var module = new GameplayModule(api.GameplayApi, main, () => state.LifecycleSnapshot(), journal.Record, state.MatchesCurrentGameplayRun, provider);
            Check(module.Probe().Ready && provider.Probe().Supported, "Formal Module did not adopt first armed source.");
            var service = Private<GameplayService>(module, "_service");
            var capabilities = new CapabilityRegistry(new("first-initialization", "fixture", "synthetic", "offline"));
            var registration = new ModuleRegistration(capabilities, module.Manifest); module.Register(registration); registration.Complete();
            capabilities.RegisterScoped<IRunModificationService>(RunModificationCapabilityIds.Read, journal.ForPlugin);
            using var plugins = new PluginHost(capabilities, main, new EventHub(log), log);
            var one = new RouterPlugin("first-one"); var two = new RouterPlugin("first-two");
            Check(plugins.Register(one).Success && plugins.Register(two).Success && plugins.Enable(one.Manifest.Id).Success && plugins.Enable(two.Manifest.Id).Success,
                "First admission failed dual actual PluginHost scopes.");
            var controls = one.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
            var other = two.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
            var clockView = one.Context.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!;
            var lease = Need(controls.Acquire(true));
            GameplayControlRequest Request(IGameplayControlLease value, double rate) => new(GameplayControlAction.SetSpeed, value.ControllerGeneration,
                value.ObservedRunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);
            Check(!other.Acquire(true).Success && api.ActiveRoots == 14, "First SDK source lost exclusive plugin ownership or independent Service roots.");
            var result = Need(lease.Execute(Request(lease, 1.5)));
            Check(result.Modification.TransactionState == GameplayControlTransactionState.Confirmed && result.Modification.ConfirmedValue == 1.5
                && finalClock.Capture().Rate == 1.5 && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 1.5f,
                "First SDK view did not confirm actual native pitch and the unique final Clock.");
            Stock(NativePlaybackSourceRole.Replay, .125f); var replayed = finalClock.Capture(); api.SetRootedDsp(replayed.DspAnchorSeconds + .5);
            Check(Math.Abs(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) - .75f) < .00001f, "First native GetTime did not consume final SDK Clock after actual Replay.");
            Stock(NativePlaybackSourceRole.Pause);
            var oldSession = session; var oldClock = finalClock; var oldLease = lease; var oldBinding = finalClock.Capture().Binding;
            var oldPublication = Private<NativePlaybackClockPublication>(session, "_publication");
            Marshal.WriteInt32(enumerator, 0x10, 0);
            Check(Start() == 1 && api.ActiveRoots == 16 && !provider.Probe().Supported, "Subsequent initializer failed first-policy withdrawal/two-root retention.");
            Check(Start() == 0, "Subsequent initializer failed actual final AL0."); api.AdoptActuallyInitializedPlayer();
            Check(router.TryCompleteInitialization() && api.ActiveRoots == 14 && !provider.Probe().Supported,
                "First admission did not establish real joint owner for normal InitV2 rebuild.");
            Refuse(() => router.PreparePausedClock(), "Normal rebuild reused initializer as Pause baseline.");
            Pause(); session = router.PreparePausedClock(); Check(session.TryArm(), "Normal same-run rebuilt Clock did not arm."); finalClock = session.Clock;
            var next = finalClock.Capture().Binding;
            Check(next.RunId == oldBinding.RunId && next.ControllerGeneration == oldBinding.ControllerGeneration
                && next.SourceGeneration > oldBinding.SourceGeneration && next.Player != oldBinding.Player && !ReferenceEquals(finalClock, oldClock),
                "Normal rebuild changed host identity or lost actual source/Clock replacement.");
            Check(module.Tick().Ready && ReferenceEquals(service, Private<GameplayService>(module, "_service")), "First Module/Service did not survive normal same-run rebuild.");
            module.ObserveControls(1, 1, requested: true);
            Need(controls.GetState()); lease = Need(controls.Acquire(true)); var sdkClock = Need(clockView.Read());
            Check(lease.ControllerGeneration > oldLease.ControllerGeneration && lease.ObservedRunId == oldLease.ObservedRunId
                && !oldLease.Execute(Request(oldLease, 4)).Success && sdkClock.ControllerGeneration == lease.ControllerGeneration && sdkClock.GameTime.Available,
                "Original first SDK views failed to replace stale Service source lease.");
            oldLease.Dispose(); oldSession.Withdraw("Late first-session cleanup"); oldPublication.Withdraw("Late first-publication cleanup");
            Check(provider.Probe().Supported && group.GetTimeLease.ReadCurrent().Active, "First-session cleanup revoked real successor.");
            Stock(NativePlaybackSourceRole.Resume, .25f); var resumed = finalClock.Capture(); api.SetRootedDsp(resumed.DspAnchorSeconds + .4);
            Check(Math.Abs(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) - (float)(resumed.MediaAnchorSeconds + .4 * resumed.Rate)) < .00001f,
                "Rebuilt native Resume lost actual SetTime/pitch/schedule/final Clock consumption.");
            Stock(NativePlaybackSourceRole.Pause);
            Check(uniqueFinalClock && nativeReads > 1 && broker.ReadActualPlaybackDobbyCallCounts() == (constructor ? 13 : 12, constructor ? 13 : 12)
                && physical == (group.Epoch, group.GetTimeLease.EntryAddress, group.GetTimeLease.OriginalAddress, group.SetPitchLease.EntryAddress),
                "First capture/rebuild replaced physical hooks or used temporary Clock reader.");
            if (constructor) Check(readShared!(0) == 4 && readShared(1) == 4
                && observations.All(value => value.Role != NativePlaybackSourceRole.PlayerConstructor),
                "Successor watch misclassified the actual foreign shared constructor.");
            lease.Dispose(); module.Release(); provider.PumpCompleted();
            Check(router.RootsReleased && api.ActiveRoots == 0 && observations.All(value => value.IsReleased), "First route final drain leaked roots/observations.");
            return FirstResult(reportedMode, nativeReads, true, 0, true, nullPlayer);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { mode = reportedMode, passed = false, checks = _checks, failure = error.ToString() })); return 1;
        }
        finally
        {
            // An Unknown native parent still owns these fixture buffers through
            // real GC handles. Keep that owner until this test process exits;
            // freeing its buffers here would contradict the retention assertion.
            if (fixtureOwner is { } api)
            {
                if (api.ActiveRoots == 0) api.Dispose();
                else FirstCaptureRetainedFixtures.Add(api);
            }
        }
    }
    private static int FirstResult(string mode, int nativeReads, bool rebuilt, int retained, bool normalExit, bool nullPlayer)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, nativeReads, sameRunRebuilt = rebuilt,
            retainedRoots = retained, normalPhysicalExit = normalExit, initialCaptureProtocol = 1, initializationProtocol = 2,
            noPredecessorPublication = true, nullPlayerHostAndActualYield = nullPlayer, fixedFixtureAllocatorTemplateRequired = false,
            nestedForeignSharedConstructor = mode.StartsWith("ctor-", StringComparison.Ordinal) || mode.StartsWith("direct-source-", StringComparison.Ordinal),
            actualPrepare = mode.StartsWith("direct-source-", StringComparison.Ordinal) ? 19 : mode.StartsWith("ctor-", StringComparison.Ordinal) ? 13 : 12,
            actualCommit = mode.StartsWith("direct-source-", StringComparison.Ordinal) ? 19 : mode.StartsWith("ctor-", StringComparison.Ordinal) ? 13 : 12,
            gameExecuted = false, productionAdmission = false })); return 0;
    }
}
