using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    // This harness owns the fixed native fixture and drives the actual bridge.
    // It deliberately stops below Router/session and seven-root graph capture.
    private static int RunNativeHandoff(string[] a, string mode)
    {
        var stages = new List<string>();
        try
        {
            Check(a.Length == 5 && mode is "handoff" or "handoff-graph" or "handoff-crossrun", "Unknown native handoff mode.");
            Check(Hash(a[1]) == "9fe8cde32ae1ce1fe9426efafb46a758d4e37593007a64747a042fdcab51bd6e", "Only the fixed prior owned fixture may execute.");
            var module = NativeLibrary.Load(Path.GetFullPath(a[1]));
            using var api = new FakeNative();
            var enumerator = api.ConfigureInitializer(module);
            var targets = PlaybackSourceGroupTargets.ResolveOwnedFixture(module);
            var host = new GameEventState(); host.Scene("Level"); host.SetAvailability(scenes: true, runs: true, pause: true);
            var started = host.Controller(api.GameplayInformation.ToInt64(), true, false, "native-initializer-handoff", api.GameplayController.ToInt64())!;
            var binding = new NativePlaybackBinding(started.RunId, host.SceneGeneration, 1, 1,
                api.GameplayController.ToInt64(), api.GameplayInformation.ToInt64(), api.GameplayProgress.ToInt64(),
                api.GameplayPlayer.ToInt64(), api.GameplaySource.ToInt64(), api.GameplayClip.ToInt64(), targets.GetTime.MethodInfo.ToInt64());
            var run = host.CapturePlaybackRun(binding.Information, binding.Controller);
            var sampleBinding = binding; long sampleRevision = 1;
            var readerCalls = 0;
            NativePlaybackInitializationTracker? tracker = null;
            var observations = new List<NativePlaybackSourceObservation>();
            var returnBytes = new List<byte>();
            var returnedParentStillInflight = 0;
            var broker = new ExactHookBroker(Path.GetFullPath(a[4]), new Main(), new Logger());
            var group = broker.BindPlaybackSourceGroup(targets, Path.GetFullPath(a[2]), Hash(a[2]), Path.GetFullPath(a[3]), Hash(a[3]),
                () => { ++readerCalls; return new NativePlaybackSingleReturnSample(NativePlaybackClockPublication.Identity(sampleBinding), sampleRevision, NativePlaybackSingleReturnPhase.Paused, 101); },
                _ => { }, _ => { },
                o => { observations.Add(o); if (tracker is null || !tracker.ObserveBefore(o)) o.Dispose(); },
                c => {
                    tracker?.ObserveReturned(c);
                    if (c.Observation.Role == NativePlaybackSourceRole.ProgressStartMoveNext && c.Returned)
                    {
                        Check(!c.PhysicalExited && c.HasRawReturnByte, "Returned callback pretended to be physical exit or lost raw AL.");
                        Check(!c.Observation.TryReadCompletion(out _), "Parent completed inside its Returned callback.");
                        ++returnedParentStillInflight; returnBytes.Add(c.RawReturnByte);
                    }
                });
            NativePlaybackSingleReturnAuthority Policy(NativePlaybackBinding b, long revision, double media)
                => new(NativePlaybackClockPublication.Identity(b), revision, NativePlaybackSingleReturnPhase.Paused, 100, media, 2, 200, IntPtr.Zero);
            var old = Policy(binding, 1, 6);
            var bootstrap = group.GetTimeLease.PrepareDormant(old);
            var getTime = Marshal.GetDelegateForFunctionPointer<GetTime>(targets.GetTime.Address);
            Check(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 3 && readerCalls == 0 && !group.GetTimeLease.ReadCurrent().Active,
                "Dormant native bootstrap consumed the old reader/policy.");
            Check(bootstrap.TryArm(), "Actual native DormantBootstrap could not arm the exact predecessor.");
            Check(group.GetTimeLease.OwnsPublication(old) && group.GetTimeLease.ReadCurrent().Active && getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 6,
                "Old exact publication is not actually armed.");
            group.BindLogicalHandoff(); stages.Add("old-native-dormant-bootstrap-armed");
            if (mode == "handoff-crossrun")
            {
                var ordinary = group.GetTimeLease.BeginLogicalHandoff(old);
                Check(ordinary.TryAdmit(), "Ordinary native transfer failed to admit after drain.");
                Reject(() => group.VerifyInitializationCapture(ordinary, binding.Player, binding.AudioSource, binding.Clip), "Ordinary transfer granted initialization capture.");
                Check(ordinary.TryOpenSource(), "Ordinary transfer source did not open.");
                Reject(() => ordinary.Prepare(Policy(binding, 2, 11)), "Ordinary handoff accepted the same run.");
                var nextRun = binding with { RunId = binding.RunId + "-next" };
                ordinary.Prepare(Policy(nextRun, 2, 11));
                Check(!group.GetTimeLease.ReadCurrent().Active && getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 3, "Ordinary successor activated before Arm.");
                sampleBinding = nextRun; sampleRevision = 2;
                Check(ordinary.TryArm() && getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 11, "Ordinary cross-run branch no longer works.");
                stages.Add("ordinary-same-run-rejected-cross-run-armed");
                Check(api.ActiveRoots == 0, "Ordinary bridge test fabricated root ownership.");
                group.GetTimeLease.Withdraw(2); group.Dispose();
                Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, stages, sameRunRejected = true, crossRunArmed = true,
                    initializationCaptureRejected = true, finalRoots = api.ActiveRoots, routerCovered = false, sevenRootRecapture = false, gameExecuted = false, productionAdmission = false }));
                return 0;
            }
            tracker = new NativePlaybackInitializationTracker(api.GameplayApi, group, run, binding);
            var invoke = Export<InvokeStart>(module, "StockFixtureInvokeStart");
            uint Call()
            {
                Check(invoke(targets.ProgressStartMoveNext.Address, enumerator, targets.ProgressStartMoveNext.MethodInfo, out var raw, out var code) == 1 && code == 0,
                    "Actual Start failed: " + tracker.LastFailure);
                return raw;
            }
            Check(Call() == 1 && Marshal.ReadInt32(enumerator, 0x10) == 1, "Actual Start state0 did not yield raw AL1/state1.");
            var yield = observations.Single(o => o.Role == NativePlaybackSourceRole.ProgressStartMoveNext);
            Check(yield.TryReadCompletion(out var yielded) && yielded is { PhysicalExited: true, Returned: true, Abnormal: false, HasRawReturnByte: true, RawReturnByte: 1 }, "No actual state0 physical-exit AL1 receipt.");
            Check(tracker.RootsRetained && api.ActiveRoots == 2 && group.ReadInitialization() is { Phase: 2, Blocked: 1, Receipt: 0 }, "Yield lost its two private initializer roots or native hold.");
            Reject(group.RequireFreshAdmission, "Yield opened fresh admission.");
            Check(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 3, "Old policy remained consumable during yield hold.");
            stages.Add("state0-physical-exit-raw-AL1-two-roots-held");
            var boundaries = new List<uint>();
            Boundary boundary = (value, at) =>
            {
                Check(value == enumerator, "Inline native boundary changed the actual enumerator."); boundaries.Add(at);
                var notReady = false; try { tracker.CaptureCompleted(); } catch (GameplayStateException error) when (error.Code == ErrorCode.NotReady) { notReady = true; }
                Check(notReady && group.ReadInitialization().Receipt == 0, "Completion existed before parent physical exit.");
                Reject(group.RequireFreshAdmission, "Inline graph store reopened fresh admission.");
                Check(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 3, "Inline boundary consumed predecessor policy.");
            };
            Export<SetBoundary>(module, "StockFixtureSetStartBoundary")(boundary);
            Check(Call() == 0 && boundaries.SequenceEqual(new uint[] { 1, 2, 3, 4 }), "Actual state1 did not execute clip, inline stores and raw AL0.");
            var parent = observations.Last(o => o.Role == NativePlaybackSourceRole.ProgressStartMoveNext);
            var child = observations.Single(o => o.Role == NativePlaybackSourceRole.SetClip);
            Check(parent.TryReadCompletion(out var exited) && exited is { PhysicalExited: true, Returned: true, Abnormal: false, HasRawReturnByte: true, RawReturnByte: 0 }, "State1 lacks actual physical-exit AL0.");
            Check(child.ParentSequence == parent.Sequence && child.Caller == group.StockLeafCaller(NativePlaybackSourceRole.ProgressStartMoveNext, NativePlaybackSourceRole.SetClip)
                && child.TryReadCompletion(out var childExit) && childExit is { PhysicalExited: true, Returned: true, Abnormal: false }, "Actual direct set_clip child is not physically complete.");
            var complete = tracker.CaptureCompleted(); complete.VerifyCurrent();
            var player = complete.Player; var source = complete.Source; var clip = complete.Clip;
            var identity = complete.NativeIdentityFor(group);
            Check(player != binding.Player && player == Marshal.ReadIntPtr(api.GameplayProgress, 0x38).ToInt64()
                && Marshal.ReadIntPtr(new IntPtr(player), 0x10).ToInt64() == source && Marshal.ReadIntPtr(new IntPtr(source), 40).ToInt64() == clip,
                "Actual newplayer/source/clip graph differs from retained initializer completion.");
            Check(api.ActiveRoots == 2 && group.ReadSourceHealth() is { Known: true, Armed: true, InFlight: 0 }, "Physical completion changed observer health or the two private roots.");
            stages.Add("state1-clip-and-inline-stores-parent-physical-exit-raw-AL0-private-completion");
            if (mode == "handoff-graph")
            {
                var replacement = Export<CreateChild>(module, "StockFixtureCreatePlayer")(api.GameplaySource);
                Marshal.WriteIntPtr(api.GameplayProgress, 0x38, replacement);
                Reject(() => group.GetTimeLease.BeginInitializationHandoff(old, identity), "Initializer begin silently replaced its completed player.");
                Marshal.WriteIntPtr(api.GameplayProgress, 0x38, new IntPtr(player));
                Check(group.ReadInitialization().Player == (ulong)player, "Rejected begin rewrote the original initializer player.");
            }
            var handoff = group.GetTimeLease.BeginInitializationHandoff(old, identity);
            Check(handoff.Read(1).GetTimeInFlight == 0 && !group.GetTimeLease.ReadCurrent().Active, "Begin failed to close the exact old native publication.");
            Check(handoff.TryAdmit() && handoff.AdmittedRevision == 2 && handoff.Read(2).SourceInFlight == 0, "Drained native initializer handoff did not admit.");
            Reject(group.RequireFreshAdmission, "Admit prematurely opened ordinary fresh capture.");
            stages.Add("initializer-begin-and-native-joint-admit");
            group.VerifyInitializationCapture(handoff, player, source, clip);
            Reject(() => group.VerifyInitializationCapture(handoff, binding.Player, source, clip), "Conditional capture accepted the predecessor player.");
            if (mode == "handoff-graph")
            {
                var alternate = Export<Create>(module, "StockFixtureCreateSource")();
                try
                {
                    Marshal.WriteIntPtr(new IntPtr(player), 0x10, alternate);
                    Reject(() => group.VerifyInitializationCapture(handoff, player, source, clip), "Conditional capture did not reread actual player.source.");
                    Check(!handoff.TryOpenSource() && handoff.Read(2).Phase == 2, "Source Open accepted a graph differing from initialization.");
                }
                finally { Marshal.WriteIntPtr(new IntPtr(player), 0x10, new IntPtr(source)); }
                group.VerifyInitializationCapture(handoff, player, source, clip);
            }
            Check(handoff.TryOpenSource() && group.ReadInitialization() is { Phase: 7, Blocked: 0, Receipt: 0 }, "Native initializer source did not open exactly once.");
            group.RequireFreshAdmission(); stages.Add("native-conditional-capture-verified-source-opened");
            var nextBinding = binding with { Player = player };
            handoff.Prepare(Policy(nextBinding, 2, 11));
            Check(!group.GetTimeLease.ReadCurrent().Active && getTime(new IntPtr(player), targets.GetTime.MethodInfo) == 3, "Successor native policy activated before explicit Arm.");
            sampleBinding = nextBinding; sampleRevision = 2;
            Check(handoff.TryArm() && handoff.Read(5).CurrentRevision == 2, "Native initializer same-run Arm failed.");
            Check(group.GetTimeLease.ReadCurrent().Active && getTime(new IntPtr(player), targets.GetTime.MethodInfo) == 11
                && getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 3, "Actual GetTime did not consume only the same-run newplayer policy.");
            Check(group.ReadSourceHealth() is { Known: true, Armed: true, InFlight: 0 } && group.GetTimeLease.ReadHealth() is { Known: true, AuthorityActive: true, InFlight: 0 }, "Final actual source/GetTime gates are not known and drained.");
            stages.Add("same-run-newplayer-prepared-dormant-then-armed-native-GetTime11");
            Check(broker.ReadActualPlaybackDobbyCallCounts() == (12, 12), "Initializer handoff installed duplicate physical hooks.");
            tracker.Dispose(); Check(!tracker.RootsRetained && api.ActiveRoots == 0, "Known-drained tracker did not release exactly its two private roots.");
            group.GetTimeLease.Withdraw(2); group.Dispose(); GC.KeepAlive(boundary);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, stages, sourceVersion = 4, frameBytes = 168, infoBytes = 136, writerVersion = 1,
                initializationVersion = 2, initializationInfoBytes = 160, returnedParentStillInflight, returnBytes, actualPhysicalParentExit = true, rawFinalAL = 0,
                retainedInitializerRoots = 2, finalRoots = api.ActiveRoots, oldNativeBootstrapArmed = true, sameRunTransfer = true, nativeGetTimeNewPlayer = 11,
                nativeGetTimeOldPlayer = 3, actualPrepare = 12, actualCommit = 12, routerCovered = false, sevenRootRecapture = false, gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, stages, failure = error.ToString() }));
            return 1;
        }
    }
}
