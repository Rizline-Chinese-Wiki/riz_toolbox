using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>Real native parent/mid calls through the shared production adoption
/// and broker dispatch core, over a fixture-owned original. This never creates a
/// game first-install receipt or loads GameAssembly.</summary>
internal static class NativeAdoptedBrokerRegression
{
    internal const string ModeArgument = "--adopted-broker-callbacks";
    private const uint Slot = 30, Kind = 4;
    private static readonly List<object> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Config
    { internal uint Version, Size; internal IntPtr Target, Original, ExpectedMid; }
    [StructLayout(LayoutKind.Sequential)] private struct Observation
    { internal uint Size, Kind; internal ulong Entered, Exited; internal IntPtr Self, Argument, Method; internal ulong Returned; internal uint Scalar, Thread; }
    [StructLayout(LayoutKind.Sequential)] private struct Result
    { internal uint Size, Status, ExceptionKind, ExceptionCode; internal ulong Payload; internal IntPtr Reference; internal uint Thread, OriginalKind; }
    [StructLayout(LayoutKind.Sequential)] private struct VoidStats
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults; }
    [StructLayout(LayoutKind.Sequential)] private struct MidStats
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, Faults; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AddressNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint slot, ref Config config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint slot, ref NativeVoidBootstrapInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint PatchMidNative(uint kind, IntPtr entry, IntPtr list);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ControlNative(uint kind, uint mode, uint block, IntPtr nested, IntPtr self, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint kind, ref Observation observation);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(uint kind, IntPtr entry, IntPtr self, IntPtr method, ref Result result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint VoidStatsNative(uint slot, ref VoidStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint MidStatsNative(uint slot, ref MidStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GateStatsNative(uint slot, ref NativeVoidAdmissionGateStatistics stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ControlBeforeNative(uint kind, uint block);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint StateBeforeNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ResumeBeforeNative(uint kind);

    internal static int RunChild(string[] args)
    {
        var mode = args.Length == 2 ? args[1] : "invalid";
        var run = new Run();
        try
        {
            run.Check(args.Length == 2 && mode is "happy" or "fault" or "late-attach", "Expected happy, fault or late-attach child mode.");
            run.Check(OperatingSystem.IsWindows() && IntPtr.Size == 8, "Native callback regression requires Windows x64.");
            run.Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero, "GameAssembly was already loaded in the isolated fixture process.");
            string Env(string name)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (value is null || !Path.IsPathFullyQualified(value) || !File.Exists(value))
                    throw new ArgumentException("Missing absolute fixture file environment: " + name);
                return Path.GetFullPath(value);
            }
            var helperPath = Env("RIZ_INITIALIZATION_CONTEXT_HELPER");
            var fixturePath = Env("RIZ_INITIALIZATION_ABI_FIXTURE");
            var dobbyPath = Env("RIZ_INITIALIZATION_ABI_DOBBY");
            var filesBefore = new[] { helperPath, fixturePath, dobbyPath }.ToDictionary(path => path, Hash);
            var helper = NativeLibrary.Load(helperPath); var fixture = NativeLibrary.Load(fixturePath);
            Owners.Add(helper); Owners.Add(fixture);
            var installer = new DobbyDetourEngine(dobbyPath); Owners.Add(installer);
            var target = Export<AddressNative>(fixture, "RtVoidTarget")(Kind);
            var caller = Export<AddressNative>(fixture, "RtVoidExpectedCaller")(Kind);
            var midPc = Export<AddressNative>(fixture, "RtEventMidExpectedReturn")(Kind);
            var self = new IntPtr(unchecked((long)0x12345678ABCDEF00UL));
            var method = new IntPtr(unchecked((long)0x23456789BCDEF020UL));
            var list = new IntPtr(unchecked((long)0x3456789ACDEF1040UL));
            var verified = new VerifiedHookTarget("adopted.broker.fixture", target, method, new(0x2468), ExactHookAbi.NativeVoid0);
            run.Check(target != IntPtr.Zero && caller != IntPtr.Zero && midPc != IntPtr.Zero, "Actual native fixture labels were missing.");
            var original = installer.Prepare(verified, Export<AddressNative>(helper, "RizVoidContext_GetEntry")(Slot));
            var config = new Config { Version = 1, Size = 32, Target = target, Original = original, ExpectedMid = midPc };
            run.Check(Export<ConfigureNative>(helper, "RizVoidBootstrap_Configure")(Slot, ref config) == 0,
                "The fixture's previously unused native slot could not be configured.");
            var readInfo = Export<InfoNative>(helper, "RizVoidBootstrap_GetInfo");
            NativeVoidBootstrapInformation Info()
            {
                var info = new NativeVoidBootstrapInformation { Version = 1, Size = 56 };
                run.Check(readInfo(Slot, ref info) == 0, "Actual native bootstrap information could not be read."); return info;
            }
            var initial = Info();
            run.Check(NativeBootstrappedVoidBridge.MatchesInformation(initial, Slot, target, original, initial.Epoch, initial.Entry, false),
                "The actual native configured identity did not match the fixture trampoline.");
            run.Check(Export<PatchMidNative>(fixture, "RtEventMidConfigure")(Kind,
                Export<EntryNative>(helper, "RizEventMidContext_GetEntry")(), list) == 1, "The fixture's real inline CALL publication failed.");
            installer.Commit(verified);
            var identity = new ExactHookBroker.NativeVoidAdoptionIdentity(Slot, target, original, initial.Entry, initial.Epoch, midPc);
            var control = Export<ControlNative>(fixture, "RtVoidControl");
            var read = Export<ReadNative>(fixture, "RtVoidRead");
            var invoke = Export<InvokeNative>(fixture, "RtVoidInvoke");
            var workerInvoke = Export<InvokeNative>(fixture, "RtVoidInvokeWorker");
            var voidStats = Export<VoidStatsNative>(helper, "RizVoidContext_GetStats");
            var midStats = Export<MidStatsNative>(helper, "RizEventMidContext_GetStats");
            var gateStats = Export<GateStatsNative>(helper, "RizVoidContext_GetGateStats");
            void Control(uint nativeMode = 0) => run.Check(control(Kind, nativeMode, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 1,
                "Fixture control failed or tried to alter an active native original.");
            Observation Read()
            {
                var value = new Observation { Size = 64 };
                run.Check(read(Kind, ref value) == 1, "Fixture original counters could not be read."); return value;
            }
            (VoidStats Parent, MidStats Mid) Statistics()
            {
                var parent = new VoidStats { Version = 1, Size = 48 }; var mid = new MidStats { Version = 1, Size = 40 };
                run.Check(voidStats(Slot, ref parent) == 0 && midStats(Slot, ref mid) == 0,
                    "Actual native parent/mid counters could not be read.");
                return (parent, mid);
            }
            Result Call(bool worker = false, IntPtr? actualMethod = null)
            {
                var before = Read(); var value = new Result { Size = 40 };
                var status = (worker ? workerInvoke : invoke)(Kind, target, self, actualMethod ?? method, ref value);
                run.Check(status == value.Status, "The native catch result disagreed with its returned status.");
                run.ThrowCallbackFailure();
                var after = Read();
                run.Check(after.Entered == before.Entered + 1 && after.Exited == before.Exited + 1 && after.Returned == before.Returned + 1,
                    "The fixture original or inline continuation did not execute exactly once.");
                return value;
            }
            var logger = new Logger(); var engine = new NeverInstallEngine(); var dispatcher = new MainDispatcher();
            var firstBroker = new ExactHookBroker(engine, dispatcher, logger); var secondBroker = new ExactHookBroker(engine, dispatcher, logger);
            run.Check(Rejected<UnsupportedSignatureException>(() => firstBroker.BindNativeVoidObservationWithMid<Context>(verified,
                _ => null, (_, _) => { }, (_, _) => { }, (_, _, _) => { }, (_, _, _) => { }, (_, _, _, _) => { })),
                "The production front accepted a fixture without a real game first-install receipt.");
            run.Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && Info().Attached == 0,
                "Production rejection loaded the game or attached an observer.");
            var factoryCalls = 0; WeakReference<NativeBootstrappedVoidBridge>? bridgeReference = null;
            NativeBootstrappedVoidBridge Adopt()
            {
                ++factoryCalls;
                var bridge = NativeBootstrappedVoidBridge.AdoptBootstrap(helperPath, Slot, target, original, initial.Epoch, logger);
                bridgeReference = new(bridge); return bridge;
            }
            NativeBootstrappedVoidBridge Bridge()
                => bridgeReference is { } reference && reference.TryGetTarget(out var bridge) ? bridge
                    : throw new InvalidOperationException("Production ownership did not retain the adopted callback bridge.");
            var a = new Observer(run, "a", self, method, caller, list, midPc);
            var b = new Observer(run, "b", self, method, caller, list, midPc);
            var observers = new List<Observer> { a, b };
            a.Peer = b; b.Peer = a;
            (ExactHookBroker.Binding Binding, ExactHookBroker.BoundaryProbeBinding Probe) Bind(ExactHookBroker broker,
                Observer observer, ExactHookBroker.NativeVoidAdoptionIdentity? channel = null, VerifiedHookTarget? hook = null)
                => broker.BindConfiguredNativeVoidObservationWithMid(hook ?? (verified with { Id = "adopted.broker." + observer.Name }), channel ?? identity, Adopt,
                    observer.Capture, observer.After, observer.Fault, observer.Mid, observer.ProbeEnter, observer.ProbeExit);
            (ExactHookBroker.Binding Binding, ExactHookBroker.BoundaryProbeBinding Probe) first, second;
            var oldCallDidNotInherit = false;
            Control();
            if (mode == "late-attach")
            {
                var controlBefore = Export<ControlBeforeNative>(fixture, "RtEventMidControlBefore");
                var stateBefore = Export<StateBeforeNative>(fixture, "RtEventMidBeforeState");
                var resumeBefore = Export<ResumeBeforeNative>(fixture, "RtEventMidResumeBefore");
                run.Check(controlBefore(Kind, 1) == 1, "The fixture pre-mid gate could not be armed before the old call.");
                var pending = Task.Run(() => { var value = new Result { Size = 40 }; var status = workerInvoke(Kind, target, self, method, ref value); return (status, value); });
                try
                {
                    run.Check(SpinWait.SpinUntil(() => stateBefore(Kind) != 0, 5000) && stateBefore(Kind) == 1,
                        "The old native worker did not stop after Enter and before its real Mid CALL.");
                    var paused = Statistics();
                    run.Check(paused.Parent.Entered == 1 && paused.Parent.Exited == 0 && paused.Parent.InFlight == 1
                        && paused.Mid.Entered == 0 && Read().Entered == 0, "The alleged pre-mid pause occurred after Mid or outside the active original.");
                    first = Bind(firstBroker, a); second = Bind(secondBroker, b);
                    run.Check(Info().Attached == 1 && Bridge().ReadActivity() is { Known: true, InFlight: 1 }
                        && a.Total == 0 && b.Total == 0, "Attach inherited the old invocation's missing observer descriptor.");
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                }
                finally { resumeBefore(Kind); run.Check(pending.Wait(5000), "The old native worker did not resume after Attach."); }
                var completed = pending.Result;
                run.Check(completed.status == 1 && completed.value.Status == 1 && stateBefore(Kind) == 1,
                    "The old call timed out or failed while its observer was attached late.");
                var old = Statistics(); var oldOriginal = Read();
                oldCallDidNotInherit = a.Total == 0 && b.Total == 0 && old.Parent.Entered == 1 && old.Parent.Exited == 1
                    && old.Parent.InFlight == 0 && old.Mid.Entered == 1 && old.Mid.Exited == 1
                    && oldOriginal.Entered == 1 && oldOriginal.Exited == 1 && oldOriginal.Returned == 1;
                run.Check(oldCallDidNotInherit && Bridge().ReadActivity() is { Known: true, InFlight: 0 },
                    "The old invocation borrowed a late Enter, Mid or Exit observer, or stranded native activity.");
                run.Check(controlBefore(Kind, 0) == 1, "The completed pre-mid gate could not be reset to pass through.");
            }
            else { first = Bind(firstBroker, a); second = Bind(secondBroker, b); }
            run.Check(factoryCalls == 1 && Info().Attached == 1 && first.Binding.ReadActivity() is { Known: true, InFlight: 0 },
                "Two broker sinks did not share one actual native observer attachment.");
            foreach (var wrong in new[] { identity with { Slot = 29 }, identity with { Original = IntPtr.Add(original, 1) },
                identity with { Entry = IntPtr.Add(initial.Entry, 1) }, identity with { Epoch = initial.Epoch + 1 },
                identity with { MidReturnPc = IntPtr.Add(midPc, 1) }, identity with { Target = IntPtr.Add(target, 1) } })
                run.Check(Rejected<UnsupportedSignatureException>(() => Bind(secondBroker, b, wrong)),
                    "A different configured channel identity shared the adopted broker entry.");
            foreach (var wrong in new[] { verified with { MethodInfo = IntPtr.Add(method, 8) },
                verified with { DeclaringClass = new IntPtr(0x2469) }, verified with { Abi = ExactHookAbi.InstanceVoid } })
                run.Check(Rejected<UnsupportedSignatureException>(() => Bind(secondBroker, b, identity, wrong)),
                    "A different ABI or metadata identity shared the adopted broker entry.");
            run.Check(factoryCalls == 1, "Identity rejection or sharing evaluated the bridge factory twice.");
            var value = Call(); run.Check(value.Status == 1, "The first adopted main-thread native call failed.");
            run.Check(a.Captures == 1 && b.Captures == 1 && a.Mids == 1 && b.Mids == 1 && a.Afters == 1 && b.Afters == 1
                && a.Disposals == 1 && b.Disposals == 1 && a.Last!.Identity!.Token == b.Last!.Identity!.Token
                && !ReferenceEquals(a.Last, b.Last) && a.IdentityRejectedPeer && b.IdentityRejectedPeer,
                "Two sinks did not observe the same physical token with distinct retained contexts.");
            var firstIdentityA = a.Last!.Identity!; var firstIdentityB = b.Last!.Identity!;
            var sharedPhysicalTokenVerified = firstIdentityA.Token == firstIdentityB.Token && a.IdentityRejectedPeer && b.IdentityRejectedPeer;
            run.Check(!firstIdentityA.IsCurrentFor(a.Last) && !firstIdentityB.IsCurrentFor(b.Last),
                "A native physical identity remained current after its matching Exit.");
            var managedFaultObserved = false; var nativeExceptionsPreserved = false;
            if (mode == "fault")
            {
                a.ThrowAfter = true;
                var beforeFaultA = a.Disposals; var beforeFaultB = b.Disposals;
                run.Check(Call().Status == 1 && a.ExpectedObserverFaults == 1 && a.Disposals == beforeFaultA + 1 && b.Disposals == beforeFaultB + 1,
                    "A managed after-observer fault changed original return or skipped retained-context cleanup.");
                a.ThrowAfter = false;
                managedFaultObserved = !first.Binding.ReadActivity().Known && Bridge().ReadActivity() is { Known: true, InFlight: 0 }
                    && logger.Errors > 0 && a.Faults > 0;
                run.Check(managedFaultObserved, "A contained managed observer fault failed to make broker responsibility sticky unknown.");
                run.Check(Rejected<UnsupportedSignatureException>(() => Bind(secondBroker, b)) && factoryCalls == 1,
                    "Unknown adopted responsibility accepted another logical sink or evaluated another bridge factory.");
                var preserved = 0;
                for (uint nativeMode = 1; nativeMode <= 2; ++nativeMode)
                {
                    Control(nativeMode); var capturesA = a.Captures; var capturesB = b.Captures;
                    var disposalsA = a.Disposals; var disposalsB = b.Disposals; var aftersA = a.Afters; var aftersB = b.Afters;
                    var abnormal = Call();
                    run.Check(abnormal.Status == 2 && abnormal.ExceptionKind == nativeMode
                        && abnormal.ExceptionCode == (nativeMode == 1 ? 0xE04F494Eu : 0xE06D7363u)
                        && abnormal.Payload == (0xBADC0FFEE0DDF00DUL ^ Kind) && abnormal.OriginalKind == 0x5600 + Kind,
                        "The native original SEH/C++ exception identity or payload changed through adopted CLR callbacks.");
                    run.Check(a.Captures == capturesA + 1 && b.Captures == capturesB + 1
                        && a.Disposals == disposalsA + 1 && b.Disposals == disposalsB + 1
                        && a.Afters == aftersA && b.Afters == aftersB && a.AbnormalProbeExits > 0 && b.AbnormalProbeExits > 0,
                        "An abnormal original delivered After or stranded a retained context/physical probe.");
                    run.Check(first.Binding.ReadActivity() is { Known: false, InFlight: 0 }
                        && Bridge().ReadActivity() is { Known: true, InFlight: 0 }, "An abnormal invocation reset sticky broker unknown or stranded the native channel.");
                    Control(); capturesA = a.Captures; capturesB = b.Captures;
                    disposalsA = a.Disposals; disposalsB = b.Disposals; aftersA = a.Afters; aftersB = b.Afters;
                    var probeExitsA = a.ProbeExits; var probeExitsB = b.ProbeExits;
                    run.Check(Call().Status == 1 && a.Captures == capturesA + 1 && b.Captures == capturesB + 1
                        && a.Disposals == disposalsA + 1 && b.Disposals == disposalsB + 1
                        && a.Afters == aftersA + 1 && b.Afters == aftersB + 1
                        && a.ProbeExits == probeExitsA + 1 && b.ProbeExits == probeExitsB + 1,
                        "The next normal same-thread native original did not recover its broker context/probe pairing after unwind.");
                    run.Check(first.Binding.ReadActivity() is { Known: false, InFlight: 0 }
                        && Bridge().ReadActivity() is { Known: true, InFlight: 0 }, "Native unwind recovery lost sticky broker unknown or left TLS activity.");
                    ++preserved;
                }
                nativeExceptionsPreserved = preserved == 2;
            }
            else if (mode == "happy")
            {
                var captures = a.Captures + b.Captures; var mids = a.Mids + b.Mids; var afters = a.Afters + b.Afters;
                var probes = a.ProbeEnters + b.ProbeEnters; var faults = a.Faults + b.Faults;
                var worker = Call(worker: true);
                run.Check(worker.Status == 1 && worker.Thread != GetCurrentThreadId()
                    && a.Captures + b.Captures == captures && a.Mids + b.Mids == mids && a.Afters + b.Afters == afters
                    && a.ProbeEnters + b.ProbeEnters == probes + 2 && a.Faults + b.Faults == faults + 2,
                    "A native worker inherited a main-thread retained context or lost its physical probes.");
                run.Check(first.Binding.ReadActivity() is { Known: true, InFlight: 0 }
                    && first.Binding.Describe().SuppressedWrongThread == 1, "The real worker changed known idle activity or lost its diagnostic.");
                probes = a.ProbeEnters + b.ProbeEnters; faults = a.Faults + b.Faults;
                run.Check(Call(actualMethod: IntPtr.Add(method, 8)).Status == 1
                    && a.Captures + b.Captures == captures && a.Mids + b.Mids == mids && a.Afters + b.Afters == afters
                    && a.ProbeEnters + b.ProbeEnters == probes + 2 && a.Faults + b.Faults == faults + 2,
                    "A foreign native MethodInfo alias inherited a retained context or lost its native original.");
                run.Check(first.Binding.ReadActivity() is { Known: true, InFlight: 0 }, "A masked foreign alias changed known physical activity.");
                second.Binding.Dispose(); second.Probe.Dispose();
                var oldB = b.Total; var oldA = a.Captures;
                run.Check(Call().Status == 1 && a.Captures == oldA + 1 && b.Total == oldB,
                    "Logical detachment removed a peer or continued detached delivery.");
                first.Binding.Dispose(); first.Probe.Dispose();
                oldA = a.Total; oldB = b.Total;
                run.Check(Call().Status == 1 && a.Total == oldA && b.Total == oldB && Bridge().ReadActivity() is { Known: true, InFlight: 0 },
                    "Removing all logical sinks removed the native original or kept callbacks delivered.");
                var rebound = new Observer(run, "rebound", self, method, caller, list, midPc);
                observers.Add(rebound);
                var binding = Bind(secondBroker, rebound);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                run.Check(Call().Status == 1 && rebound.Captures == 1 && rebound.Mids == 1 && rebound.Afters == 1 && rebound.Disposals == 1,
                    "The actual adopted native callbacks failed after logical rebind and CLR collection.");
                binding.Binding.Dispose(); binding.Probe.Dispose();
                run.Check(factoryCalls == 1 && Info().Entry == initial.Entry && Info().Epoch == initial.Epoch,
                    "Logical rebind performed another adoption or changed the permanent native channel identity.");
            }
            first.Binding.Dispose(); first.Probe.Dispose(); second.Binding.Dispose(); second.Probe.Dispose();
            run.ThrowCallbackFailure();
            var final = Statistics(); var nativeOriginal = Read(); var finalInfo = Info();
            var gate = new NativeVoidAdmissionGateStatistics { Version = 1, Size = 40 };
            run.Check(gateStats(Slot, ref gate) == 0 && gate.IsValid && gate.Unknown == 0 && gate.Active == 0 && gate.Parked == 0
                && gate.Phase == NativeVoidAdmissionPhase.Open, "The native admission gate was unknown or retained active/parked calls.");
            run.Check(final.Parent.Entered == nativeOriginal.Entered && final.Parent.Exited == nativeOriginal.Exited
                && final.Parent.Entered == final.Parent.Exited && final.Parent.InFlight == 0
                && final.Parent.EnterFaults == 0 && final.Parent.ExitFaults == 0 && final.Mid.Entered == final.Mid.Exited
                && final.Mid.Entered == nativeOriginal.Entered && final.Mid.InFlight == 0 && final.Mid.Faults == 0,
                "Final native original/parent/mid counts disagreed or native callbacks stranded an invocation.");
            run.Check(NativeBootstrappedVoidBridge.MatchesInformation(finalInfo, Slot, target, original, initial.Epoch, initial.Entry, true)
                && factoryCalls == 1 && engine.Calls == 0, "Actual adoption identity changed or a broker installed another detour.");
            run.Check(first.Binding.Describe().NativeCalls == checked((long)nativeOriginal.Entered - (mode == "late-attach" ? 1 : 0))
                && first.Binding.Describe().OriginalCalls == first.Binding.Describe().NativeCalls,
                "Broker diagnostics did not match genuinely observed native invocations.");
            run.Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero, "The callback fixture unexpectedly loaded the game module.");
            var files = filesBefore.Select(pair => new { path = pair.Key, beforeSha256 = pair.Value, afterSha256 = Hash(pair.Key) }).ToArray();
            run.Check(files.All(file => file.beforeSha256 == file.afterSha256), "The fixture changed a native input file on disk.");
            run.Check(mode == "fault" || logger.Errors == 0, "An unexpected managed observer error was logged.");
            Console.WriteLine(JsonSerializer.Serialize(new {
                schemaVersion = 1, kind = "native-adopted-broker-callback-fixture", mode, passed = true, checks = run.Checks, failures = 0,
                nativeToClrCallbackVerified = a.Captures > 0 && a.Mids > 0 && a.Afters > 0 && a.Disposals > 0,
                sharedProductionAdoptionCoreVerified = true, productionGameFrontRejected = true, firstInstallReceiptUsed = false,
                metadataIdentityControlled = true, actualIl2CppMetadataResolved = false, gameRuntimeVerified = false,
                gameCodeExecuted = false, firstInstallSafetyVerified = false, actualLedgerCaptureVerified = false,
                fixtureOwnedOriginal = true, originalNativeCalls = nativeOriginal.Entered,
                observedNativeInvocations = first.Binding.Describe().NativeCalls, realNativeMidCalls = final.Mid.Entered,
                captures = observers.Sum(observer => observer.Captures), mids = observers.Sum(observer => observer.Mids), afters = observers.Sum(observer => observer.Afters),
                contextDisposals = observers.Sum(observer => observer.Disposals), probeEnters = observers.Sum(observer => observer.ProbeEnters), probeExits = observers.Sum(observer => observer.ProbeExits),
                sharedPhysicalTokenVerified, gateActive = gate.Active, gateParked = gate.Parked, gateUnknown = gate.Unknown,
                oldInvocationDidNotInheritObserver = oldCallDidNotInherit, managedObserverFaultVerified = managedFaultObserved,
                nativeOriginalExceptionsVerified = nativeExceptionsPreserved, brokerKnown = first.Binding.ReadActivity().Known,
                bridgeKnown = Bridge().ReadActivity().Known, helperSlot = Slot, epoch = initial.Epoch, bridgeFactoryCalls = factoryCalls,
                brokerInstallCalls = engine.Calls, diskHashesUnchanged = true, files
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "native-adopted-broker-callback-fixture", mode,
                passed = false, checks = run.Checks, failures = 1, failure = error.GetType().Name + ": " + error.Message,
                nativeToClrCallbackVerified = false, metadataIdentityControlled = true, firstInstallReceiptUsed = false,
                gameRuntimeVerified = false, gameCodeExecuted = false, firstInstallSafetyVerified = false }));
            return 1;
        }
    }

    private sealed class Run
    {
        internal int Checks; private Exception? _callbackFailure;
        internal void Check(bool condition, string reason)
        { Interlocked.Increment(ref Checks); if (!condition) throw new InvalidOperationException(reason); }
        internal void Callback(Action action)
        { try { action(); } catch (ControlledObserverFault) { throw; } catch (Exception error) { Interlocked.CompareExchange(ref _callbackFailure, error, null); throw; } }
        internal T Callback<T>(Func<T> action)
        { try { return action(); } catch (Exception error) { Interlocked.CompareExchange(ref _callbackFailure, error, null); throw; } }
        internal void ThrowCallbackFailure()
        { if (Volatile.Read(ref _callbackFailure) is { } error) throw new InvalidOperationException("An actual native-to-CLR callback assertion failed.", error); }
    }
    private sealed class Context : IDisposable
    {
        private readonly Observer _owner;
        internal ExactHookBroker.NativeVoidPhysicalIdentity? Identity;
        internal bool Disposed, AfterSeen;
        internal Context(Observer owner) => _owner = owner;
        public void Dispose() => _owner.Run.Callback(() => {
            _owner.Run.Check(!Disposed, "The same retained context was released twice.");
            _owner.Run.Check(Identity is null || !Identity.IsCurrentFor(this), "A retained context remained current during native Exit cleanup.");
            Disposed = true; ++_owner.Disposals;
        });
    }
    private sealed class ControlledObserverFault : Exception { }
    private sealed class Observer
    {
        internal readonly Run Run; internal readonly string Name;
        private readonly IntPtr _self, _method, _caller, _list, _midPc;
        internal Observer? Peer; internal Context? Last;
        internal int Captures, Mids, Afters, Faults, Disposals, ProbeEnters, ProbeExits, AbnormalProbeExits, ExpectedObserverFaults;
        internal bool ThrowAfter, IdentityRejectedPeer;
        internal int Total => Captures + Mids + Afters + Faults + Disposals + ProbeEnters + ProbeExits;
        internal Observer(Run run, string name, IntPtr self, IntPtr method, IntPtr caller, IntPtr list, IntPtr midPc)
            => (Run, Name, _self, _method, _caller, _list, _midPc) = (run, name, self, method, caller, list, midPc);
        internal Context Capture(NativeVoidFrame frame) => Run.Callback(() => {
            Run.Check(frame.IsValid && frame.Self == _self && frame.Method == _method && frame.Caller == _caller,
                "Broker capture lost the real native receiver, caller or method.");
            ++Captures; return Last = new(this);
        });
        internal void Mid(Context context, NativeEventMidFrame frame, ExactHookBroker.NativeVoidPhysicalIdentity identity) => Run.Callback(() => {
            Run.Check(!context.Disposed && !context.AfterSeen && frame.IsValid && frame.ReturnPc == _midPc
                && frame.Event == _self && frame.Executing == _list, "The real inline Mid frame or retained observation order changed.");
            Run.Check(identity.IsCurrentFor(context) && identity.Token != 0 && identity.Frame.Self == _self
                && identity.Frame.Method == _method && identity.Frame.Caller == _caller,
                "The broker did not mint a current identity for this exact retained context.");
            if (Peer?.Last is { } peer)
            {
                Run.Check(!identity.IsCurrentFor(peer), "A physical identity authorized another sink's retained context.");
                IdentityRejectedPeer = true;
                if (peer.Identity is { } other && !peer.Disposed)
                    Run.Check(other.Token == identity.Token && !ReferenceEquals(other, identity),
                        "Two sinks did not share one physical invocation token with separate identities.");
            }
            context.Identity = identity; ++Mids;
        });
        internal void After(Context context, NativeVoidFrame frame) => Run.Callback(() => {
            Run.Check(!context.Disposed && !context.AfterSeen && ReferenceEquals(context, Last) && frame.Caller == _caller,
                "Broker After did not use its retained context before release.");
            Run.Check(context.Identity is null || !context.Identity.IsCurrentFor(context), "Mid authority remained live during After.");
            context.AfterSeen = true; ++Afters;
            if (ThrowAfter) { ++ExpectedObserverFaults; throw new ControlledObserverFault(); }
        });
        internal void Fault(NativeVoidFrame frame, string reason) => Run.Callback(() => {
            Run.Check(frame.Self == _self && frame.Caller == _caller && !string.IsNullOrEmpty(reason), "Broker fault lost its physical frame.");
            Interlocked.Increment(ref Faults);
        });
        internal void ProbeEnter(IntPtr self, byte value, IntPtr method) => Run.Callback(() => {
            Run.Check(self == _self && value == 0 && method != IntPtr.Zero, "Physical Enter probe lost native scalar arguments.");
            Interlocked.Increment(ref ProbeEnters);
        });
        internal void ProbeExit(IntPtr self, byte value, IntPtr method, bool returned) => Run.Callback(() => {
            Run.Check(self == _self && value == 0 && method != IntPtr.Zero, "Physical Exit probe lost native scalar arguments.");
            Interlocked.Increment(ref ProbeExits); if (!returned) Interlocked.Increment(ref AbnormalProbeExits);
        });
    }
    private sealed class NeverInstallEngine : INativeDetourEngine, IPreparedNativeDetourEngine
    {
        internal int Calls;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) { ++Calls; throw new InvalidOperationException("Broker attempted another detour install."); }
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement) { ++Calls; throw new InvalidOperationException("Broker attempted another detour prepare."); }
        public void Commit(VerifiedHookTarget target) { ++Calls; throw new InvalidOperationException("Broker attempted another detour commit."); }
    }
    private sealed class MainDispatcher : IMainThreadDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public bool IsMainThread => Environment.CurrentManagedThreadId == _thread;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Broker configuration changed its controlled main thread."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The fixture must not enqueue game work.");
    }
    private sealed class Logger : ILogger
    { internal int Errors; public void Log(LogLevel level, string message, Exception? exception = null) { if (level == LogLevel.Error) Interlocked.Increment(ref Errors); } }
    private static T Export<T>(IntPtr library, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    private static bool Rejected<T>(Action action) where T : Exception
    { try { action(); return false; } catch (T) { return true; } }
    private static string Hash(string path)
    { using var stream = File.OpenRead(path); using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant(); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
