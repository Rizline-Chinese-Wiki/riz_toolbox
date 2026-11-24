using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Real callbacks over the self-owned native DLL. No constructed machine frame,
// game address, synthetic Ready receipt, IL2CPP metadata, or native GC root is
// used. The optional real ledger consumes controlled graph/root observations.
internal static class NativeListenerManagedRegression
{
    internal const string ModeArgument = "--listener-managed-callbacks";
    private const ulong Payload = 0xfedcba9876543210;
    private static readonly List<object> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct WorkConfig
    { internal uint Version, Size, Target, Mode, Site, Flags, Count, Reserved; internal IntPtr Nested; }
    [StructLayout(LayoutKind.Sequential)] private struct FixtureInfo
    {
        internal uint Version, Size, Target, SetupStatus;
        internal NativeVoidBootstrapInformation Parent;
        internal IntPtr Mid, Method;
        internal NativeListenerInformation Lane;
        internal uint SourceWidth0, SourceWidth1, SourceWidth2, PostWidth0, PostWidth1, PostWidth2;
        internal uint Writes, Lookups, Boundaries, Functions, Unknown, Reserved;
        internal IntPtr FunctionTable, Allocation;
        internal ulong AllocationSize;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WorkResult
    {
        internal uint Version, Size, State, Mode, Target, Thread, Returned, Caught, ExceptionCode, ExceptionFlags, ExceptionParameters, Held;
        internal ulong Exception0, Exception1, Exception2;
        internal IntPtr ExceptionAddress;
        internal ulong Originals, Replays, ReturnChecks, Checks, Failures, Nested;
        internal ulong Invoked0, Invoked1, Invoked2;
        internal IntPtr ActualReturn0, ActualReturn1, ActualReturn2, Event, List, Invokable0, Invokable1, Invokable2, Args;
        internal ulong AfterCount, Exception3;
        internal IntPtr Row(uint site) => site switch { 0 => Invokable0, 1 => Invokable1, 2 => Invokable2, _ => throw new ArgumentOutOfRangeException(nameof(site)) };
        internal IntPtr Return(uint site) => site switch { 0 => ActualReturn0, 1 => ActualReturn1, 2 => ActualReturn2, _ => throw new ArgumentOutOfRangeException(nameof(site)) };
    }
    [StructLayout(LayoutKind.Sequential)] private struct ParentStats
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults; }
    [StructLayout(LayoutKind.Sequential)] private struct MidStats
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, Faults; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private delegate uint SetupNative([MarshalAs(UnmanagedType.LPWStr)] string helper, [MarshalAs(UnmanagedType.LPWStr)] string dobby, uint slot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint target, ref FixtureInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CreateNative(ref WorkConfig config, out IntPtr work);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(IntPtr work);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(IntPtr work, ref WorkResult result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint WaitNative(IntPtr work, uint timeout);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ResumeNative(IntPtr work);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ParentStatsNative(uint slot, ref ParentStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint MidStatsNative(uint slot, ref MidStats stats);

    internal static int RunChild(string[] args)
    {
        var mode = args.Length == 2 ? args[1] : "invalid";
        var run = new Run();
        try
        {
            SetErrorMode(0x8003);
            run.Check(args.Length == 2 && mode is "normal" or "original-seh" or "original-cpp" or "target-read-av"
                or "late-attach" or "detach" or "worker" or "nested-same" or "nested-cross" or "foreign-original"
                or "fault-before" or "fault-after", "Unknown listener managed child mode.");
            run.Check(OperatingSystem.IsWindows() && IntPtr.Size == 8 && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero,
                "This fixture requires an isolated Windows x64 child without a game image.");
            run.Check(Marshal.SizeOf<WorkConfig>() == 40 && Marshal.SizeOf<FixtureInfo>() == 336 && Marshal.SizeOf<WorkResult>() == 240,
                "The actual native fixture ABI size changed.");
            var helperPath = AbsoluteEnv("RIZ_INITIALIZATION_CONTEXT_HELPER");
            var dobbyPath = AbsoluteEnv("RIZ_INITIALIZATION_ABI_DOBBY");
            var fixturePath = AbsoluteEnv("RIZ_LISTENER_MANAGED_FIXTURE");
            var hashes = new[] { helperPath, dobbyPath, fixturePath }.ToDictionary(path => path, Hash);
            var helper = NativeLibrary.Load(helperPath); var fixture = NativeLibrary.Load(fixturePath);
            Owners.Add(helper); Owners.Add(fixture);
            var api = new Api(run, fixture, helper);
            run.Check(Export<SetupNative>(fixture, "RizListenerManaged_Setup")(helperPath, dobbyPath, 19) == 0,
                "The fixture could not prepare its own two native targets before their first calls.");
            var info0 = api.Info(0); var info1 = api.Info(1);
            VerifyInitial(run, info0, 19); VerifyInitial(run, info1, 18);
            var dispatcher = new MainDispatcher(); var log = new Logger(); var engine = new NeverInstallEngine();
            var channels = new[] { new Channel(run, api, helperPath, info0, dispatcher, log, engine),
                new Channel(run, api, helperPath, info1, dispatcher, log, engine) };
            var channel = channels[0];
            run.Check(Rejected<UnsupportedSignatureException>(() => channel.Broker.BindNativeVoidObservationWithListeners<Context>(channel.Target,
                _ => null, (_, _) => { }, (_, _) => { }, (_, _, _) => { }, (_, _, _) => { }, (_, _, _) => { },
                (_, _, _) => { }, (_, _, _, _) => { })), "The production front accepted a fixture without the native first-install receipt.");
            run.Check(api.Info(0).Parent.Attached == 0 && api.Info(0).Lane.Attached == 0,
                "Rejected production adoption attached fixture callbacks.");
            var observers = new List<Observer>();
            var results = new List<WorkResult>();
            var modeVerified = false; var ledgerVerified = false; var dualSink = false;
            var sharedPhysicalToken = false; var oldCallUnobserved = false; var detachCleanup = false;
            var workerMasked = false; var foreignMasked = false; var rawTargetReadAfter = false;
            NativeLocalUnityEventLedgerSnapshot? ledgerSnapshot = null;

            if (mode == "target-read-av")
            {
                var raw = TargetRead(run, api, channel, log); results.Add(raw.Result);
                modeVerified = rawTargetReadAfter = raw.Verified;
            }
            else
            {
                var a = new Observer(run, api, channel, "a"); var b = new Observer(run, api, channel, "b");
                a.Peer = b; b.Peer = a; observers.Add(a); observers.Add(b);
                var workMode = mode == "original-seh" ? 1u : mode == "original-cpp" ? 2u : 0u;
                var nested = IntPtr.Zero;
                if (mode is "nested-same" or "nested-cross" or "foreign-original")
                {
                    var nestedTarget = mode == "nested-cross" ? 1u : 0u;
                    nested = api.Create(nestedTarget, mode == "foreign-original" ? 4u : 0u, count: 1);
                    if (nestedTarget == 1) {
                        var peer = new Observer(run, api, channels[1], "cross"); observers.Add(peer); channels[1].Bind(peer);
                    }
                }
                var work = api.Create(0, workMode, site: mode is "nested-same" or "nested-cross" or "foreign-original" ? 0u : 2u,
                    flags: mode == "late-attach" ? 1u : 0u, nested: nested);
                var initial = api.Read(work);
                LedgerBackend? ledger = null;
                if (mode == "late-attach")
                {
                    var pending = Task.Run(() => api.Invoke(work));
                    try {
                        run.Check(api.WaitHeld(work) == 0 && api.Read(work).State == 1 && api.Read(work).Held == 1,
                            "The old call did not stop inside the original before the actual Mid CALL.");
                        var active = api.Parent(channel.Info.Parent.Slot);
                        run.Check(active.InFlight == 1 && api.Mid(channel.Info.Parent.Slot).Entered == 0,
                            "The alleged pre-mid pause did not retain the original physical parent.");
                        channel.Bind(a); channel.Bind(b);
                        run.Check(a.Total + b.Total == 0, "Attach borrowed the old invocation's null observer.");
                        Collect();
                    } finally {
                        run.Check(api.Resume(work) == 0 && pending.Wait(5000), "The late-attached original failed to resume.");
                    }
                    var old = pending.Result; results.Add(old);
                    oldCallUnobserved = a.Total + b.Total == 0 && old.Originals == 3 && old.Replays == 3 && old.ReturnChecks == 3;
                    run.Check(oldCallUnobserved, "Old native Mid/listener/Exit borrowed a newly attached observer.");
                    work = api.Create(0); initial = api.Read(work);
                }
                else { channel.Bind(a); channel.Bind(b); }
                dualSink = channel.ParentFactories == 1 && channel.ListenerFactories == 1;
                run.Check(dualSink, "Two logical sinks installed or adopted the native channel more than once.");
                run.Check(Rejected<UnsupportedSignatureException>(() => channel.Bind(new Observer(run, api, channel, "bad"),
                    channel.LaneIdentity with { ParentEpoch = channel.LaneIdentity.ParentEpoch + 1 })),
                    "A listener identity from a different parent was shared.");
                run.Check(Rejected<UnsupportedSignatureException>(() => channel.Bind(new Observer(run, api, channel, "bad-source"),
                    channel.LaneIdentity with { Source2 = IntPtr.Add(channel.LaneIdentity.Source2, 1) })),
                    "A listener identity with an altered source PC was shared.");
                if (mode is "normal" or "original-seh" or "original-cpp")
                {
                    ledger = new LedgerBackend(run, channel, initial, dispatcher);
                    ledger.Capture();
                }
                if (mode == "detach")
                    a.BeforeAction = (_, frame) => {
                        if (frame.Site != 0) return;
                        run.Check(channel.A.Binding.ReadActivity().InFlight > 0, "Detach was not performed during a native child.");
                        channel.A.Binding.Dispose(); channel.A.Probe.Dispose();
                    };
                if (mode == "worker")
                    a.BeforeAction = (_, frame) => {
                        if (frame.Site != 0) return;
                        var captures = a.Captures + b.Captures; var before = a.BeforeCount + b.BeforeCount;
                        var worker = api.Invoke(api.Create(0, flags: 4)); results.Add(worker);
                        run.Check(worker.Originals == 3 && a.Captures + b.Captures == captures
                            && a.BeforeCount + b.BeforeCount == before, "A native worker borrowed the main thread's parent context.");
                        workerMasked = true;
                    };
                a.ThrowBefore = mode == "fault-before"; a.ThrowAfter = mode == "fault-after";
                var result = api.Invoke(work); results.Add(result);
                api.VerifyResult(result, workMode == 0, expectedOriginals: 3, expectedReplays: workMode == 0 ? 3UL : 2UL);
                if (nested != IntPtr.Zero) {
                    var nestedResult = api.Read(nested); results.Add(nestedResult);
                    run.Check(nestedResult.State == 2 && nestedResult.Originals == 1 && result.Nested == 1,
                        "The actual native callee did not invoke its one nested native work.");
                    api.VerifyResult(nestedResult, true, 1, 1);
                    if (mode == "foreign-original") {
                        foreignMasked = a.Contexts.All(value => value.Self != nestedResult.Event)
                            && b.Contexts.All(value => value.Self != nestedResult.Event)
                            && !channel.A.Binding.ReadActivity().Known && api.Info(0).Unknown == 1;
                        run.Check(foreignMasked, "An original-only child borrowed an ancestor's retained context or remained falsely known.");
                    } else {
                        var nestedObservers = mode == "nested-cross" ? observers.Where(value => value.Channel == channels[1]).ToArray() : new[] { a, b };
                        run.Check(nestedObservers.All(value => value.Contexts.Any(context => context.Self == nestedResult.Event && context.Disposed)),
                            "Nested native work did not acquire and retire its own retained contexts.");
                        if (mode == "nested-same")
                            run.Check(a.Contexts.Select(value => value.Identity?.Token).Distinct().Count() == 2,
                                "Same-parent recursion reused its ancestor's physical generation token.");
                    }
                }
                sharedPhysicalToken = a.SharedToken && b.SharedToken && a.PeerRejected && b.PeerRejected;
                run.Check(sharedPhysicalToken, "Peer sinks did not share one physical parent token with distinct context authority.");
                if (mode == "detach")
                {
                    run.Check(a.BeforeCount == 1 && a.AfterCount == 1 && a.Disposals == 1 && a.ParentAfters == 0
                        && b.BeforeCount == 3 && b.AfterCount == 3, "In-flight logical detach lost current child cleanup or kept future delivery.");
                    var oldA = a.Total; Collect(); results.Add(api.Invoke(api.Create(0)));
                    run.Check(a.Total == oldA && b.Captures == 2, "Detached sink received a new invocation after CLR collection.");
                    detachCleanup = true;
                }
                if (mode is "original-seh" or "original-cpp")
                {
                    run.Check(result.Caught == 1 && result.ExceptionAddress != IntPtr.Zero && a.AbnormalCount == 1 && b.AbnormalCount == 1
                        && a.AfterCount == 3 && b.AfterCount == 3 && a.ParentAfters == 0 && b.ParentAfters == 0,
                        "The native original exception lost its abnormal listener completion or crossed the CLR boundary.");
                    run.Check(mode == "original-seh"
                        ? result.ExceptionCode == 0xe04c534f && result.ExceptionParameters == 3 && result.Exception0 == Payload
                            && result.Exception1 == unchecked((ulong)work.ToInt64()) && result.Exception2 == 2
                        : result.ExceptionCode == 0xe06d7363 && result.ExceptionParameters == 4
                            && result.Exception0 == 0x19930520 && result.Exception1 != 0 && result.Exception2 != 0
                            && result.Exception3 == unchecked((ulong)fixture.ToInt64()), "The actual native exception record or typed C++ payload check changed.");
                    run.Check(!channel.A.Binding.ReadActivity().Known, "An abnormal parent was reported as known normal completion.");
                }
                if (mode is "fault-before" or "fault-after")
                {
                    run.Check(a.ControlledFaults == 1 && result.Returned == 1 && result.Originals == 3 && result.ReturnChecks == 3
                        && a.AfterCount == 1 && a.Disposals == 1 && b.Disposals == 1 && !channel.A.Binding.ReadActivity().Known,
                        "A managed observer fault escaped, suppressed the original, lost child cleanup or falsely stayed known.");
                    var recovered = api.Invoke(api.Create(0)); results.Add(recovered);
                    run.Check(recovered.Returned == 1 && recovered.Originals == 3 && recovered.Failures == 0,
                        "Sticky observer uncertainty suppressed a later original.");
                }
                if (ledger is not null)
                {
                    ledgerSnapshot = ledger.Owner!.Read();
                    var snapshot = ledgerSnapshot;
                    ledgerVerified = snapshot.InvokableDispatches == 3 && snapshot.ReturnedDispatches == (workMode == 0 ? 3 : 2)
                        && snapshot.AbnormalDispatches == (workMode == 0 ? 0 : 1) && snapshot.NativeInFlight == 0
                        && snapshot.Pending && !snapshot.MutationClosed && snapshot.InlineExecutionUnobserved && !snapshot.DispatchPathVerified
                        && snapshot.Unknown == (workMode != 0) && snapshot.EventSnapshots.Single().Dispatches!.All(value => value.Completed)
                        && ledger.InlineCaptures == 1 && ledger.DispatchCaptures == 3;
                    run.Check(ledgerVerified, "The real ledger did not retain three actual dispatch identities with its honest pending/unknown boundary.");
                    if (workMode == 0) run.Check(ledger.Owner.TryConfirmNativeIdle(), "The normal real ledger retained a native physical invocation.");
                }
                if (mode == "normal")
                {
                    run.Check(a.BeforeCount == 3 && b.BeforeCount == 3 && a.AfterCount == 3 && b.AfterCount == 3
                        && a.Disposals == 1 && b.Disposals == 1 && a.ParentAfters == 1 && b.ParentAfters == 1,
                        "Normal three-site callbacks did not pair both logical sinks.");
                }
                foreach (var observer in observers)
                    run.Check(observer.Contexts.All(value => value.Disposed && (value.Identity is null || !value.Identity.IsCurrentFor(value))),
                        "A retained managed context or current physical authority survived native Exit.");
                if (mode == "worker") run.Check(workerMasked && channel.A.Binding.Describe().SuppressedWrongThread == 1,
                    "The worker suppression was not diagnosed by the actual shared broker.");
                modeVerified = true;
            }
            run.ThrowCallbackFailure();
            ulong nativeInFlight = 0; var finalInfos = new List<FixtureInfo>();
            foreach (var candidate in channels)
            {
                var final = api.Info(candidate.Info.Target); finalInfos.Add(final);
                var parent = api.Parent(final.Parent.Slot); var mid = api.Mid(final.Parent.Slot);
                run.Check(parent.Entered == parent.Exited && parent.InFlight == 0 && parent.EnterFaults == 0 && parent.ExitFaults == 0
                    && mid.Entered == mid.Exited && mid.InFlight == 0 && mid.Faults == 0
                    && final.Lane.Entered == final.Lane.Exited && final.Lane.InFlight == 0
                    && final.Lane.Overflow == 0 && final.Lane.MissingPost == 0, "Native parent/mid/listener cleanup left physical responsibility or callback faults.");
                nativeInFlight += parent.InFlight + mid.InFlight + final.Lane.InFlight;
            }
            run.Check(engine.Calls == 0 && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero,
                "Managed adoption installed native instructions or loaded the game.");
            var files = hashes.Select(pair => new { path = pair.Key, beforeSha256 = pair.Value, afterSha256 = Hash(pair.Key) }).ToArray();
            run.Check(files.All(file => file.beforeSha256 == file.afterSha256), "A native fixture input changed on disk.");
            Console.WriteLine(JsonSerializer.Serialize(new {
                schemaVersion = 1, kind = "native-listener-managed-callback-fixture", mode, passed = true, checks = run.Checks, failures = 0,
                modeVerified, nativeToClrCallbackVerified = rawTargetReadAfter || observers.Sum(value => value.BeforeCount) > 0,
                sharedProductionListenerCoreVerified = mode != "target-read-av", productionGameFrontRejected = true,
                firstInstallReceiptUsed = false, actualLedgerCaptureVerified = ledgerVerified, metadataGraphRootsControlled = true,
                actualIl2CppMetadataResolved = false, gameCodeExecuted = false, gameRuntimeVerified = false, firstInstallSafetyVerified = false,
                delegateLeafExecutionVerified = false, allFutureListenersClosed = false, fixtureOwnedOriginal = true,
                dualSinkVerified = dualSink, sharedPhysicalTokenVerified = sharedPhysicalToken, oldInvocationDidNotInheritObserver = oldCallUnobserved,
                detachedInFlightCleanupVerified = detachCleanup, workerAncestorMasked = workerMasked, foreignOriginalAncestorMasked = foreignMasked,
                targetReadAfterWithoutBeforeVerified = rawTargetReadAfter, managedObserverFaultVerified = mode is "fault-before" or "fault-after",
                nativeOriginalExceptionVerified = mode is "original-seh" or "original-cpp" or "target-read-av",
                brokerInstallCalls = engine.Calls, nativeChecks = results.Sum(value => checked((long)value.Checks)),
                nativeFailures = results.Sum(value => checked((long)value.Failures)), originalNativeCalls = results.Sum(value => checked((long)value.Originals)),
                actualReturnPcChecks = results.Sum(value => checked((long)value.ReturnChecks)), nativeReplayChecks = results.Sum(value => checked((long)value.Replays)),
                managedListenerBefore = observers.Sum(value => value.BeforeCount), managedListenerAfter = observers.Sum(value => value.AfterCount),
                managedListenerAbnormal = observers.Sum(value => value.AbnormalCount), contextDisposals = observers.Sum(value => value.Disposals),
                nativeInFlight, brokerKnown = channel.HasBinding ? channel.A.Binding.ReadActivity().Known : (bool?)null,
                ledgerPending = ledgerSnapshot?.Pending, ledgerUnknown = ledgerSnapshot?.Unknown,
                ledgerDispatches = ledgerSnapshot?.InvokableDispatches, ledgerReturned = ledgerSnapshot?.ReturnedDispatches,
                ledgerAbnormal = ledgerSnapshot?.AbnormalDispatches, ledgerRoots = ledgerSnapshot?.Roots,
                exceptionRecords = results.Where(value => value.Caught != 0).Select(value => new {
                    code = value.ExceptionCode, flags = value.ExceptionFlags, parameters = value.ExceptionParameters,
                    information = new[] { value.Exception0, value.Exception1, value.Exception2, value.Exception3 },
                    address = value.ExceptionAddress.ToInt64() }).ToArray(), exceptionFlagsTransparencyVerified = false,
                diskHashesUnchanged = true, files
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "native-listener-managed-callback-fixture", mode,
                passed = false, checks = run.Checks, failures = 1, failure = error.ToString(), nativeToClrCallbackVerified = false,
                firstInstallReceiptUsed = false, gameRuntimeVerified = false, gameCodeExecuted = false, firstInstallSafetyVerified = false }));
            return 1;
        }
    }

    private static void VerifyInitial(Run run, FixtureInfo info, uint slot)
    {
        run.Check(info.Version == 1 && info.Size == 336 && info.SetupStatus == 0 && info.Unknown == 0 && info.Reserved == 0
            && info.Parent.Slot == slot && info.Parent.Attached == 0 && info.Parent.Epoch != 0 && info.Parent.Unknown == 0
            && info.Lane.Slot == slot && info.Lane.Attached == 0 && info.Lane.Unknown == 0 && info.Lane.Epoch != 0
            && info.Lane.ParentEpoch == info.Parent.Epoch && info.Method != IntPtr.Zero && info.AllocationSize == 4096,
            "The actual fixture setup identity is invalid or already attached.");
        run.Check(info.Writes == 8 && info.Lookups == 12 && info.Boundaries == 12 && info.Functions == 3
            && info.SourceWidth0 == 5 && info.SourceWidth1 == 5 && info.SourceWidth2 == 6
            && info.PostWidth0 == 6 && info.PostWidth1 == 9 && info.PostWidth2 == 9,
            "The fixture did not publish all of its own eight writes after twelve registered boundary checks.");
        run.Check(info.Mid == IntPtr.Add(info.Parent.Target, 0x5d)
            && info.Lane.SourceReturnPc0 == IntPtr.Add(info.Parent.Target, 0xc1)
            && info.Lane.SourceReturnPc1 == IntPtr.Add(info.Parent.Target, 0xf0)
            && info.Lane.SourceReturnPc2 == IntPtr.Add(info.Parent.Target, 0x11b),
            "The real fixture labels differ from their native assembly layout; game RVAs must not be substituted.");
    }
    private sealed class Api
    {
        private readonly Run _run; private readonly InfoNative _info; private readonly CreateNative _create;
        private readonly InvokeNative _invoke; private readonly ReadNative _read; private readonly WaitNative _wait; private readonly ResumeNative _resume;
        private readonly ParentStatsNative _parent; private readonly MidStatsNative _mid;
        internal Api(Run run, IntPtr fixture, IntPtr helper)
        {
            _run = run; _info = Export<InfoNative>(fixture, "RizListenerManaged_GetInfo");
            _create = Export<CreateNative>(fixture, "RizListenerManaged_CreateWork"); _invoke = Export<InvokeNative>(fixture, "RizListenerManaged_InvokeWork");
            _read = Export<ReadNative>(fixture, "RizListenerManaged_ReadWork"); _wait = Export<WaitNative>(fixture, "RizListenerManaged_WaitHeld");
            _resume = Export<ResumeNative>(fixture, "RizListenerManaged_Resume"); _parent = Export<ParentStatsNative>(helper, "RizVoidContext_GetStats");
            _mid = Export<MidStatsNative>(helper, "RizEventMidContext_GetStats");
        }
        internal FixtureInfo Info(uint target) { var info = new FixtureInfo { Version = 1, Size = 336 }; _run.Check(_info(target, ref info) == 0, "Actual fixture identity read failed."); return info; }
        internal IntPtr Create(uint target, uint mode = 0, uint site = 2, uint flags = 0, uint count = 3, IntPtr nested = default)
        { var config = new WorkConfig { Version = 1, Size = 40, Target = target, Mode = mode, Site = site, Flags = flags, Count = count, Nested = nested };
            _run.Check(_create(ref config, out var work) == 0 && work != IntPtr.Zero, "Native permanent work creation failed."); return work; }
        internal WorkResult Read(IntPtr work) { var result = new WorkResult { Version = 1, Size = 240 }; _run.Check(_read(work, ref result) == 0, "Native result copy failed."); return result; }
        internal WorkResult Invoke(IntPtr work)
        { _run.Check(_invoke(work) == 0, "The native invocation failed its finite execution contract."); var result = Read(work);
            _run.Check(result.State == 2 && result.Failures == 0 && result.Checks > 0 && result.ReturnChecks == result.Originals,
                "The actual native original, arguments, canaries or ReturnAddress checks failed."); _run.ThrowCallbackFailure(); return result; }
        internal uint WaitHeld(IntPtr work) => _wait(work, 5000);
        internal uint Resume(IntPtr work) => _resume(work);
        internal ParentStats Parent(uint slot) { var value = new ParentStats { Version = 1, Size = 48 }; _run.Check(_parent(slot, ref value) == 0, "Native parent statistics failed."); return value; }
        internal MidStats Mid(uint slot) { var value = new MidStats { Version = 1, Size = 40 }; _run.Check(_mid(slot, ref value) == 0, "Native Mid statistics failed."); return value; }
        internal void VerifyResult(WorkResult result, bool returned, ulong expectedOriginals, ulong expectedReplays)
        { _run.Check(result.Returned == (returned ? 1u : 0u) && result.Caught == (returned ? 0u : 1u)
                && result.Originals == expectedOriginals && result.Replays == expectedReplays && result.ReturnChecks == expectedOriginals
                && result.AfterCount == (returned ? 1UL : 0UL), "Native original execution/replay/exception counters differ.");
            var identity = NativeListenerAdoptionIdentity.From(Info(result.Target).Lane);
            for (uint site = 0; site < expectedOriginals; ++site) _run.Check(result.Return(site) == identity.Source(site), "Actual native callee ReturnAddress differs from its source +5/+6 PC."); }
    }
    private sealed class Channel
    {
        internal readonly Run Run; internal readonly Api Api; internal readonly FixtureInfo Info; internal readonly string HelperPath;
        internal readonly ExactHookBroker Broker; internal readonly VerifiedHookTarget Target;
        internal readonly ExactHookBroker.NativeVoidAdoptionIdentity ParentIdentity; internal readonly NativeListenerAdoptionIdentity LaneIdentity;
        internal readonly Logger Log;
        internal int ParentFactories, ListenerFactories; internal bool HasBinding;
        internal (ExactHookBroker.Binding Binding, ExactHookBroker.BoundaryProbeBinding Probe) A;
        internal Channel(Run run, Api api, string helper, FixtureInfo info, MainDispatcher main, Logger log, NeverInstallEngine engine)
        {
            (Run, Api, HelperPath, Info, Log) = (run, api, helper, info, log); Broker = new(engine, main, log);
            Target = new("listener.managed." + info.Target, info.Parent.Target, info.Method, new IntPtr(0x234567), ExactHookAbi.NativeVoid0);
            ParentIdentity = new(info.Parent.Slot, info.Parent.Target, info.Parent.Original, info.Parent.Entry, info.Parent.Epoch, info.Mid);
            LaneIdentity = NativeListenerAdoptionIdentity.From(info.Lane);
        }
        internal NativeBootstrappedVoidBridge Parent()
        { ++ParentFactories; return NativeBootstrappedVoidBridge.AdoptBootstrap(HelperPath, Info.Parent.Slot, Info.Parent.Target, Info.Parent.Original, Info.Parent.Epoch, Log); }
        internal NativeListenerContextBridge Lane()
        { ++ListenerFactories; return NativeListenerContextBridge.AdoptConfigured(HelperPath, LaneIdentity, Log); }
        internal (ExactHookBroker.Binding Binding, ExactHookBroker.BoundaryProbeBinding Probe) Bind(Observer observer, NativeListenerAdoptionIdentity? identity = null)
        {
            var bound = Broker.BindConfiguredNativeVoidObservationWithListeners(Target with { Id = Target.Id + "." + observer.Name },
                ParentIdentity, Parent, identity ?? LaneIdentity, Lane, observer.Capture, observer.ParentAfter, observer.Fault,
                observer.Mid, observer.Before, observer.After, observer.ProbeEnter, observer.ProbeExit);
            observer.Binding = bound.Binding;
            if (!HasBinding) { A = bound; HasBinding = true; }
            return bound;
        }
    }
    private sealed class Context : IDisposable
    {
        internal readonly Observer Owner; internal readonly IntPtr Self; internal readonly WorkResult Work;
        internal ExactHookBroker.NativeVoidPhysicalIdentity? Identity;
        internal bool Disposed; internal readonly Dictionary<ulong, NativeListenerFrame> Children = new();
        internal Context(Observer owner, NativeVoidFrame frame) { Owner = owner; Self = frame.Self; Work = owner.Api.Read(frame.Self); }
        public void Dispose() => Owner.Run.Callback(() => {
            Owner.Run.Check(!Disposed && (Identity is null || !Identity.IsCurrentFor(this)), "Retained parent disposal was duplicated or still authorized a physical scope.");
            Disposed = true; ++Owner.Disposals;
        });
    }
    private sealed class Observer
    {
        internal readonly Run Run; internal readonly Api Api; internal readonly Channel Channel; internal readonly string Name;
        internal Observer? Peer; internal ExactHookBroker.Binding? Binding;
        internal readonly List<Context> Contexts = new();
        internal int Captures, Mids, ParentAfters, BeforeCount, AfterCount, AbnormalCount, Disposals, Faults, ProbeEnters, ProbeExits, ControlledFaults;
        internal bool SharedToken, PeerRejected, ThrowBefore, ThrowAfter;
        internal Action<Context, NativeListenerFrame>? BeforeAction;
        internal int Total => Captures + Mids + ParentAfters + BeforeCount + AfterCount + Disposals + Faults + ProbeEnters + ProbeExits;
        internal Observer(Run run, Api api, Channel channel, string name) => (Run, Api, Channel, Name) = (run, api, channel, name);
        internal Context Capture(NativeVoidFrame frame) => Run.Callback(() => {
            Run.Check(frame.IsValid && frame.Method == Channel.Info.Method && frame.Self != IntPtr.Zero, "Parent capture lost actual native arguments.");
            var context = new Context(this, frame); Contexts.Add(context); ++Captures; return context;
        });
        internal void Mid(Context context, NativeEventMidFrame frame, ExactHookBroker.NativeVoidPhysicalIdentity identity) => Run.Callback(() => {
            Run.Check(frame.IsValid && frame.ReturnPc == Channel.Info.Mid && frame.Event == context.Self && frame.Executing == context.Work.List
                && identity.IsCurrentFor(context) && identity.ConfiguredMidReturnPc == frame.ReturnPc && identity.Token != 0,
                "Actual native Mid did not retain this exact configured parent identity.");
            context.Identity = identity; ++Mids;
            var peer = Peer?.Contexts.LastOrDefault(value => value.Self == context.Self && !value.Disposed);
            if (peer is not null) {
                Run.Check(!identity.IsCurrentFor(peer), "A parent identity authorized a peer sink's context."); PeerRejected = true;
                if (peer.Identity is { } other) {
                    Run.Check(other.Token == identity.Token && !ReferenceEquals(other, identity), "Two logical contexts did not share the native physical token.");
                    SharedToken = true; Peer!.SharedToken = true;
                }
            }
        });
        internal void Before(Context context, NativeListenerObservation observation, ExactHookBroker.NativeVoidPhysicalIdentity identity) => Run.Callback(() => {
            var frame = observation.Frame; Validate(context, observation);
            Run.Check(frame.Stage == NativeListenerStage.Before && frame.Flags == 0 && observation.Machine is not null
                && identity.IsCurrentFor(context) && frame.ParentManagedToken == unchecked((ulong)identity.Token)
                && identity.ConfiguredListenerReturnPc(frame.Site) == frame.SourceReturnPc && frame.TransportReturnPc == frame.SourceReturnPc,
                "Before did not receive the actual native child and exact current parent token.");
            Run.Check(context.Children.TryAdd(frame.ChildSequence, frame), "Native child sequence was reused within a parent.");
            ++BeforeCount; BeforeAction?.Invoke(context, frame);
            if (ThrowBefore && ControlledFaults++ == 0) throw new ControlledFault();
        });
        internal void After(Context context, NativeListenerObservation observation, bool captured) => Run.Callback(() => {
            var frame = observation.Frame; Validate(context, observation);
            if (context.Children.Remove(frame.ChildSequence, out var before))
                Run.Check(before.ParentSequence == frame.ParentSequence && before.ParentManagedToken == frame.ParentManagedToken
                    && before.SourceReturnPc == frame.SourceReturnPc && before.SourceRsp == frame.SourceRsp
                    && before.Invokable == frame.Invokable && before.ActualTarget == frame.ActualTarget && before.Site == frame.Site,
                    "Completion did not consume the same actual native child identity.");
            else Run.Check(!captured, "A captured completion had no actual Before observation.");
            Run.Check(frame.Stage is NativeListenerStage.Returned or NativeListenerStage.Abnormal && !context.Disposed,
                "Completion arrived after parent disposal or without a terminal native stage.");
            if (frame.Stage == NativeListenerStage.Abnormal) {
                Run.Check(observation.Machine is null && frame.TransportReturnPc == IntPtr.Zero
                    && (frame.Flags & NativeListenerFlags.ParentAbnormal) != 0, "Original abnormal close fabricated a normal post machine frame.");
                ++AbnormalCount;
            } else Run.Check(observation.Machine is not null && frame.TransportReturnPc == Channel.LaneIdentity.Transport(frame.Site),
                "Normal completion lost its separate real post-transport return PC.");
            ++AfterCount;
            if (ThrowAfter && ControlledFaults++ == 0) throw new ControlledFault();
        });
        private void Validate(Context context, NativeListenerObservation observation)
        {
            var frame = observation.Frame;
            Run.Check(frame.Version == 1 && frame.Size == 176 && frame.Site < 3 && frame.Epoch == Channel.Info.Lane.Epoch
                && frame.Slot == Channel.Info.Parent.Slot && frame.OsThreadId == GetCurrentThreadId()
                && frame.Event == context.Self && frame.List == context.Work.List && frame.Invokable == context.Work.Row(frame.Site)
                && frame.ArgRcx == frame.Invokable && frame.SourceReturnPc == Channel.LaneIdentity.Source(frame.Site)
                && frame.Index == frame.Site && frame.ParentSequence != 0 && frame.ChildSequence != 0
                && frame.MachineFrame == IntPtr.Zero && frame.SourceRsp != 0,
                "Copied listener identity differs from actual permanent native work.");
            Run.Check(frame.ArgRdx == (frame.Site == 2 ? context.Work.Args : IntPtr.Zero)
                && frame.ArgR9 == new IntPtr(unchecked((long)0x9999999989abcdefUL))
                && (frame.Site == 2 || frame.ArgR8 == new IntPtr(unchecked((long)0x88888888789abcdeUL))),
                "Listener callback lost native high-bit arguments.");
            if (observation.Machine is { } machine) Run.Check(machine.IsValid && machine.Event == frame.Event && machine.Executing == frame.List
                && machine.ReturnPc == frame.TransportReturnPc && machine.Rsp == frame.SourceRsp && machine.Rbx == frame.Index,
                "Copied live machine state disagrees with its native listener observation.");
        }
        internal void ParentAfter(Context context, NativeVoidFrame frame) => Run.Callback(() => {
            Run.Check(!context.Disposed && frame.Self == context.Self && context.Children.Count == 0
                && (context.Identity is null || !context.Identity.IsCurrentFor(context)), "Parent After preceded child cleanup or retained Mid authority."); ++ParentAfters;
        });
        internal void Fault(NativeVoidFrame _, string reason) => Run.Callback(() => { Run.Check(!string.IsNullOrEmpty(reason), "Fault diagnostic was empty."); Interlocked.Increment(ref Faults); });
        internal void ProbeEnter(IntPtr self, byte value, IntPtr method) => Run.Callback(() => {
            Run.Check(self != IntPtr.Zero && value == 0 && method == Channel.Info.Method, "Parent physical probe lost actual arguments."); Interlocked.Increment(ref ProbeEnters);
        });
        internal void ProbeExit(IntPtr self, byte value, IntPtr method, bool returned) => Run.Callback(() => {
            Run.Check(self != IntPtr.Zero && value == 0 && method == Channel.Info.Method, "Parent Exit probe lost actual arguments."); Interlocked.Increment(ref ProbeExits);
        });
    }
    private sealed class LedgerAuthority : ICutoutListenerAuthority
    {
        internal bool Capturing = true;
        internal int Faults;
        public bool Unknown => false; // Controlled source owner only, not a run.
        public void VerifyBeforeCapture() { if (!Capturing) throw new InvalidOperationException("Controlled ledger capture already ended."); }
        public void VerifyObservation() { if (Capturing) throw new InvalidOperationException("Controlled ledger has not activated its observation scope."); }
        public bool TryConfirmSourceIdle() => true;
        public void ObserveFaultFromAnyThread() => Interlocked.Increment(ref Faults);
    }
    private sealed class LedgerBackend : ICutoutListenerBackend
    {
        private readonly Run _run; private readonly Channel _channel; private readonly WorkResult _work;
        private readonly MainDispatcher _main; private readonly LedgerAuthority _authority = new();
        private readonly NativeUnityEventGraph _graph;
        private readonly HashSet<long> _controlledRoots = new();
        private ExactHookBroker.Binding? _binding;
        private ExactHookBroker.BoundaryProbeBinding? _probe;
        internal NativeLocalUnityEventLedger? Owner;
        internal int InlineCaptures, DispatchCaptures;
        public bool CleanupUnknown => false;
        public int RootRecords => _controlledRoots.Count;
        internal LedgerBackend(Run run, Channel channel, WorkResult work, MainDispatcher main)
        {
            (_run, _channel, _work, _main) = (run, channel, work, main);
            // Row object identities are real native work addresses. Classes,
            // delegate graph and root budget are deliberately controlled; no
            // metadata APIs or game GC handles are called by this backend.
            var rows = Enumerable.Range(0, 3).Select(index => new NativeUnityEventInvokable(work.Row((uint)index).ToInt64(),
                0x101, true, Array.Empty<NativeUnityEventLeaf>())).ToArray();
            _graph = new(work.Event.ToInt64(), 0x2001, 0x2002, false, false,
                new(0x2003, 0, Array.Empty<NativeUnityEventInvokable>()), new(0x2004, 0, Array.Empty<NativeUnityEventInvokable>()),
                new(work.List.ToInt64(), 17, rows), 0x2005, 0, Array.Empty<NativeUnityEventPersistent>(), work.Args.ToInt64());
        }
        internal void Capture()
        {
            Owner = NativeLocalUnityEventLedger.CaptureBackend(_main, this, _authority,
                checked(_channel.Info.Parent.Target.ToInt64() - 0x15642e0));
            _authority.Capturing = false;
        }
        public void Initialize(NativeLocalUnityEventLedger owner)
        {
            Owner = owner; Hold(_work.Event.ToInt64()); _run.Check(owner.RegisterEvent(_work.Event.ToInt64()) == 0,
                "The real ledger registered a different native event.");
            var bound = _channel.Broker.BindConfiguredNativeVoidObservationWithListeners(_channel.Target with { Id = "listener.managed.real-ledger" },
                _channel.ParentIdentity, _channel.Parent, _channel.LaneIdentity, _channel.Lane,
                frame => _run.Callback(() => owner.ShouldObserveInvoke(frame.Self.ToInt64()) ? owner.CaptureNativeInvoke(frame) : null),
                (context, frame) => _run.Callback(() => owner.CompleteNativeInvoke(context, frame)),
                (frame, reason) => owner.ObserveInvokeFault(frame, reason),
                (context, machine, identity) => _run.Callback(() => owner.ObserveInlineList(context, machine, identity)),
                (context, observation, identity) => _run.Callback(() => owner.ObserveListenerBefore(context, observation, identity)),
                (context, observation, captured) => _run.Callback(() => owner.ObserveListenerAfter(context, observation, captured)),
                (pointer, _, method) => owner.ProbeEnter(pointer.ToInt64(), method == _channel.Info.Method),
                (pointer, _, _, returned) => owner.ProbeExit(pointer.ToInt64(), returned));
            (_binding, _probe) = bound;
        }
        private void Hold(long identity)
        { _run.Check(identity != 0, "The controlled root table cannot retain null."); _controlledRoots.Add(identity); }
        public NativeUnityEventGraph ReadGraph(int slot)
        {
            _main.VerifyAccess(); _run.Check(slot == 0, "Controlled graph read used another event slot.");
            foreach (var value in new[] { _graph.Event, _graph.Calls, _graph.PersistentGroup, _graph.CandidatesList,
                _graph.PersistentCalls.Object, _graph.RuntimeCalls.Object, _graph.ExecutingCalls.Object, _graph.InvokeArray }) Hold(value);
            foreach (var row in _graph.ExecutingCalls.Rows) Hold(row.Object);
            return _graph;
        }
        public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList)
            => throw new InvalidOperationException("The stock inline path never synthesizes a Prepare call.");
        public NativeUnityEventPrepared CaptureInline(int eventSlot, long actualList)
        {
            _run.Check(eventSlot == 0 && actualList == _work.List.ToInt64(), "Actual Mid list differs from native work.");
            ++InlineCaptures; var graph = ReadGraph(eventSlot); return new(graph, graph.ExecutingCalls);
        }
        public NativeUnityEventDispatchCapture CaptureDispatch(int eventSlot, NativeListenerFrame frame)
        {
            _run.Check(eventSlot == 0 && frame.Event == _work.Event && frame.List == _work.List && frame.Index < 3
                && frame.Invokable == _work.Row(checked((uint)frame.Index)), "Actual child row differs from the controlled rooted graph.");
            Hold(frame.List.ToInt64()); Hold(frame.Invokable.ToInt64());
            if (frame.Site == 2) Hold(frame.ArgRdx.ToInt64());
            ++DispatchCaptures;
            return new(_graph.ExecutingCalls, _graph.ExecutingCalls.Rows[checked((int)frame.Index)], frame.Site == 2 ? frame.ArgRdx.ToInt64() : 0);
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => _binding?.ReadActivity() ?? new(0, false);
    }
    private static (WorkResult Result, bool Verified) TargetRead(Run run, Api api, Channel channel, Logger log)
    {
        var work = api.Create(0, 3); var identity = api.Read(work);
        var parent = channel.Parent(); var lane = channel.Lane();
        var before = new Dictionary<ulong, NativeListenerFrame>();
        var beforeCount = 0; var afterCount = 0; var abnormal = 0; var noBefore = 0; var parentExits = 0;
        parent.ConfigureMid(machine => run.Callback(() => run.Check(machine.IsValid && machine.ReturnPc == channel.Info.Mid
            && machine.Event == identity.Event && machine.Executing == identity.List, "Direct bridge Mid lost actual native identities.")));
        parent.Configure(channel.Info.Parent.Original,
            frame => run.Callback(() => { run.Check(frame.Self == work && frame.Method == channel.Info.Method, "Direct parent Enter changed native arguments."); return (nuint)0x198; }),
            (frame, token, flags) => run.Callback(() => { run.Check(frame.Self == work && token == 0x198 && flags == NativeVoidExitFlags.Abnormal,
                "Target-read fault did not close the actual direct parent abnormally."); ++parentExits; }));
        lane.Attach(observation => run.Callback(() => {
            var frame = observation.Frame;
            run.Check(frame.Site < 2 && frame.Stage == NativeListenerStage.Before && observation.Machine is not null
                && frame.Event == work && frame.List == identity.List && frame.Invokable == identity.Row(frame.Site)
                && frame.ParentManagedToken == 0x198 && frame.MachineFrame == IntPtr.Zero, "A target read fault fabricated its missing Before.");
            run.Check(before.TryAdd(frame.ChildSequence, frame), "Direct native child sequence repeated.");
            ++beforeCount; return checked((nuint)(frame.ChildSequence + 0x200));
        }), (observation, token) => run.Callback(() => {
            var frame = observation.Frame; ++afterCount;
            if (frame.Site == 2) {
                run.Check(token == 0 && frame.Stage == NativeListenerStage.Abnormal
                    && frame.Flags == (NativeListenerFlags.ParentAbnormal | NativeListenerFlags.TargetReadFaulted)
                    && observation.Machine is null && frame.ActualTarget == IntPtr.Zero && frame.TransportReturnPc == IntPtr.Zero
                    && !before.ContainsKey(frame.ChildSequence), "Pre-target AV lost its native-only cleanup record or minted a managed Before token.");
                ++abnormal; ++noBefore;
            } else {
                run.Check(before.Remove(frame.ChildSequence, out var original) && frame.Stage == NativeListenerStage.Returned
                    && observation.Machine is not null && frame.Flags == 0
                    && token == checked((nuint)(frame.ChildSequence + 0x200)) && original.SourceReturnPc == frame.SourceReturnPc,
                    "The two successful direct calls did not complete before the virtual-target AV.");
            }
        }), reason => run.Callback(() => run.Check(false, "Direct listener bridge fault: " + reason)));
        Owners.Add(parent); Owners.Add(lane); Collect();
        var result = api.Invoke(work); api.VerifyResult(result, false, 2, 2);
        var info = api.Info(0);
        var verified = beforeCount == 2 && afterCount == 3 && abnormal == 1 && noBefore == 1 && parentExits == 1 && before.Count == 0
            && result.ExceptionCode == 0xc0000005 && result.ExceptionParameters == 2 && result.Exception0 == 0 && result.Exception1 != 0
            && result.ExceptionAddress != IntPtr.Zero && info.Lane.Abnormal == 1 && info.Lane.Unknown == 0 && info.Lane.Faults == 0
            && info.Lane.InFlight == 0 && lane.ReadActivity() is { Known: true, InFlight: 0 } && log.Errors == 0;
        run.Check(verified, "Target-read AV lost its native exception, zero-token abnormal After or physical cleanup.");
        return (result, verified);
    }
    private sealed class Run
    {
        internal int Checks; private Exception? _callbackFailure;
        internal void Check(bool condition, string reason)
        { Interlocked.Increment(ref Checks); if (!condition) throw new InvalidOperationException(reason); }
        internal void Callback(Action action)
        { try { action(); } catch (ControlledFault) { throw; } catch (Exception error) { Interlocked.CompareExchange(ref _callbackFailure, error, null); throw; } }
        internal T Callback<T>(Func<T> action)
        { try { return action(); } catch (ControlledFault) { throw; } catch (Exception error) { Interlocked.CompareExchange(ref _callbackFailure, error, null); throw; } }
        internal void ThrowCallbackFailure()
        { if (Volatile.Read(ref _callbackFailure) is { } error) throw new InvalidOperationException("Actual native-to-CLR callback assertion failed.", error); }
    }
    private sealed class ControlledFault : Exception { }
    private sealed class NeverInstallEngine : INativeDetourEngine, IPreparedNativeDetourEngine
    {
        internal int Calls;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) { ++Calls; throw new InvalidOperationException("Managed listener adopted channel attempted Install."); }
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement) { ++Calls; throw new InvalidOperationException("Managed listener adopted channel attempted Prepare."); }
        public void Commit(VerifiedHookTarget target) { ++Calls; throw new InvalidOperationException("Managed listener adopted channel attempted Commit."); }
    }
    private sealed class MainDispatcher : IMainThreadDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public bool IsMainThread => Environment.CurrentManagedThreadId == _thread;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("The callback test left its controlled main thread."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("This offline test never queues game work.");
    }
    private sealed class Logger : ILogger
    { internal int Errors; public void Log(LogLevel level, string message, Exception? exception = null) { if (level == LogLevel.Error) Interlocked.Increment(ref Errors); } }
    private static string AbsoluteEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (value is null || !Path.IsPathFullyQualified(value) || !File.Exists(value)) throw new ArgumentException("Missing absolute native file environment: " + name);
        return Path.GetFullPath(value);
    }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static T Export<T>(IntPtr module, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    private static bool Rejected<T>(Action action) where T : Exception
    { try { action(); return false; } catch (T) { return true; } }
    private static string Hash(string path)
    { using var stream = File.OpenRead(path); using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant(); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
}
