using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>Self-owned real Dobby publication -> actual operation bootstrap ->
/// production shared broker callbacks. No game/FirstInstall Ready, real IL2CPP
/// metadata, production object backend or ledger is fabricated by this test.</summary>
internal static class NativeOperationBootstrapManagedRegression
{
    internal const string ModeArgument = "--operation-bootstrap-managed";
    private const uint Slot = 22;
    private const string DobbyHash = "8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded";
    private static readonly List<object> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Result
    {
        internal uint Version, Size, Kind, Mode, Returned, Caught, Code, Flags, ParameterCount, NativeChecks, NativeFailures, Reserved;
        internal IntPtr Self, Arg0, Arg1, Method, OriginalCaller;
        internal ulong OriginalCalls, Info0, Info1, Info2, Info3;
        internal IntPtr ExceptionAddress;
        internal ulong ReservedTail;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Configuration
    { internal uint Version, Size; internal IntPtr Target, Original; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInformation
    { internal IntPtr BaseAddress, AllocationBase; internal uint AllocationProtect; internal ushort PartitionId; internal nuint RegionSize; internal uint State, Protect, Type; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr OriginalNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr MethodNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(IntPtr entry, uint kind, uint mode, IntPtr method, ref Result result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PrepareNative(IntPtr target, IntPtr entry, out IntPtr original);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CommitNative(IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint kind, uint slot, ref Configuration config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint kind, uint slot, ref NativeOperationBootstrapInformation info);

    internal static int RunChild(string[] args)
    {
        var run = new Run(); var mode = args.Length == 2 ? args[1] : "invalid";
        try
        {
            SetErrorMode(0x8003);
            run.Check(args.Length == 2 && mode is "normal" or "original-seh" or "original-cpp" or "detach" or "observer-fault", "Unknown operation bootstrap mode.");
            run.Check(OperatingSystem.IsWindows() && IntPtr.Size == 8 && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero,
                "An isolated Windows x64 child without GameAssembly is required.");
            run.Check(Marshal.SizeOf<Result>() == 144 && Marshal.SizeOf<Configuration>() == 24 && Marshal.SizeOf<NativeOperationBootstrapInformation>() == 96,
                "Actual operation fixture/bootstrap layouts changed.");
            var helperPath = AbsoluteEnv("RIZ_INITIALIZATION_CONTEXT_HELPER");
            var fixturePath = AbsoluteEnv("RIZ_OPERATION_MANAGED_FIXTURE");
            var dobbyPath = AbsoluteEnv("RIZ_OPERATION_BOOTSTRAP_DOBBY");
            run.Check(Hash(dobbyPath) == DobbyHash, "Dobby differs from the inspected Prepare/Commit image.");
            var files = new[] { helperPath, fixturePath, dobbyPath }.ToDictionary(path => path, Hash);
            var helper = NativeLibrary.Load(helperPath); var fixture = NativeLibrary.Load(fixturePath); var dobby = NativeLibrary.Load(dobbyPath);
            Owners.Add(helper); Owners.Add(fixture); Owners.Add(dobby);
            var getOriginal = Export<OriginalNative>(fixture, "RizOperationManaged_GetOriginal");
            var method = Export<MethodNative>(fixture, "RizOperationManaged_GetMethod")();
            var invoke = Export<InvokeNative>(fixture, "RizOperationManaged_Invoke");
            var prepare = Export<PrepareNative>(dobby, "DobbyPrepare"); var commit = Export<CommitNative>(dobby, "DobbyCommit");
            var configure = Export<ConfigureNative>(helper, "RizOperationBootstrap_Configure");
            var read = Export<InfoNative>(helper, "RizOperationBootstrap_GetInfo");
            var rows = new List<object>(); var actualOriginals = 0UL; var preAttachOriginals = 0UL;
            var captures = 0; var completed = 0; var disposed = 0; var detachCalls = 0;
            for (uint kind = 0; kind < 3; ++kind)
            {
                var targetAddress = getOriginal(kind); var nativeKind = (NativeOperationKind)kind;
                run.Check(targetAddress != IntPtr.Zero && method != IntPtr.Zero && targetAddress != method,
                    "Missing actual self-owned original or method carrier.");
                run.Check(VirtualQuery(targetAddress, out var memory, (nuint)Marshal.SizeOf<MemoryInformation>()) != 0
                    && memory.AllocationBase == fixture && NativeListenerPublication.ExecutableProtection(memory.Protect),
                    "The owned original is outside the actual executable fixture image.");
                var cold = Bytes(targetAddress, 32); var initial = NewInfo();
                run.Check(read(kind, Slot, ref initial) == 2 && initial.Kind == kind && initial.Slot == Slot && initial.Entry != IntPtr.Zero
                    && initial.Target == IntPtr.Zero && initial.Original == IntPtr.Zero && initial.Epoch == 0 && initial.Unknown == 0,
                    "The selected permanent bootstrap slot is not actually empty.");
                run.Check(prepare(targetAddress, initial.Entry, out var trampoline) == 0 && trampoline != IntPtr.Zero && trampoline != targetAddress,
                    "Actual DobbyPrepare did not return a distinct real native trampoline.");
                run.Check(cold.SequenceEqual(Bytes(targetAddress, 32)), "Prepare unexpectedly changed source instructions.");
                var config = new Configuration { Version = 1, Size = 24, Target = targetAddress, Original = trampoline };
                run.Check(configure(kind, Slot, ref config) == 0, "The real prepared native bootstrap configuration was rejected.");
                var configured = ReadInfo(run, read, kind); var identity = new NativeOperationChannelIdentity(nativeKind, Slot,
                    targetAddress, trampoline, initial.Entry, configured.Epoch);
                run.Check(NativeBootstrappedOperationBridge.MatchesInformation(configured, identity, false), "Configured native identity differs before publication.");
                var committed = -1; var restored = false; var flushed = false;
                try { committed = commit(targetAddress); }
                finally
                {
                    restored = VirtualProtect(targetAddress, 32, memory.Protect, out _) != 0;
                    flushed = FlushInstructionCache(GetCurrentProcess(), targetAddress, 32) != 0;
                }
                run.Check(committed == 0 && restored && flushed && !cold.SequenceEqual(Bytes(targetAddress, 32))
                    && Reaches(targetAddress, identity.Entry), "Actual DobbyCommit did not publish the exact bootstrap entry.");
                var published = Bytes(targetAddress, 32);
                var beforeResults = new List<object>();
                for (uint beforeMode = 0; beforeMode < 3; ++beforeMode)
                {
                    var result = Invoke(run, invoke, targetAddress, kind, beforeMode, method);
                    preAttachOriginals += result.OriginalCalls; actualOriginals += result.OriginalCalls;
                    var info = ReadInfo(run, read, kind);
                    run.Check(NativeBootstrappedOperationBridge.MatchesInformation(info, identity, false)
                        && info.Entered == beforeMode + 1 && info.Exited == info.Entered && info.InFlight == 0,
                        "Unattached normal/SEH/C++ forwarding borrowed an observer or lost native cleanup.");
                    beforeResults.Add(new { mode = beforeMode, result.OriginalCalls, result.Returned, result.Caught, code = result.Code,
                        flags = result.Flags, parameters = result.ParameterCount });
                }
                var main = new Main(); var engine = new NeverInstallEngine(); var logger = new Logger();
                var broker = new ExactHookBroker(engine, main, logger); var peer = new ExactHookBroker(engine, main, logger);
                var target = new VerifiedHookTarget("operation.bootstrap.fixture." + kind, targetAddress, method, new(0x345670), ExactHookBroker.OperationAbi(nativeKind));
                var channel = new Channel(run, mode, target, identity, broker, peer);
                channel.Attach(helperPath, logger);
                run.Check(channel.Factories == 1 && NativeBootstrappedOperationBridge.MatchesInformation(ReadInfo(run, read, kind), identity, true)
                    && channel.First!.ReadActivity() is { Known: true, InFlight: 0 } && engine.Calls == 0,
                    "The shared production core did not adopt one actual native bootstrap channel.");
                run.Check(Rejected<InvalidOperationException>(() => NativeBootstrappedOperationBridge.AdoptBootstrap(helperPath, identity, logger)),
                    "A duplicate managed owner was allowed to consume the native slot.");
                run.Check(Rejected<UnsupportedSignatureException>(() => broker.BindConfiguredNativeOperationObservation<Context>(target,
                    identity with { Epoch = identity.Epoch + 1 }, () => throw new Exception("Foreign factory must be lazy."),
                    _ => new Context(run, default, () => { }), (_, _, _) => { }, (_, _) => { })),
                    "A different epoch shared the configured production entry.");
                run.Check(Rejected<UnsupportedSignatureException>(() => peer.BindConfiguredNativeOperationObservation<Context>(target with { MethodInfo = method + 8 },
                    identity, () => throw new Exception("Foreign factory must be lazy."), _ => new Context(run, default, () => { }), (_, _, _) => { }, (_, _) => { })),
                    "A foreign metadata identity shared the configured production entry.");
                Collect();
                var postMode = mode == "original-seh" ? 1u : mode == "original-cpp" ? 2u : 0u;
                var observed = Invoke(run, invoke, targetAddress, kind, postMode, method); actualOriginals += observed.OriginalCalls;
                run.ThrowCallbackFailure(); channel.VerifyObserved(observed, postMode);
                var firstFrame = channel.Frames.Single();
                run.Check(firstFrame.Self == observed.Self && firstFrame.Arg0 == observed.Arg0 && firstFrame.Arg1 == observed.Arg1
                    && firstFrame.Method == observed.Method && firstFrame.Caller != IntPtr.Zero
                    && VirtualQuery(firstFrame.Caller, out var caller, (nuint)Marshal.SizeOf<MemoryInformation>()) != 0 && caller.AllocationBase == fixture,
                    "The actual machine callback source/arguments differ from its native work.");
                var recoveryCalls = 0;
                if (postMode != 0 || mode == "observer-fault")
                {
                    var previousCaptures = channel.Captures.Sum();
                    var recovery = Invoke(run, invoke, targetAddress, kind, 0, method); ++recoveryCalls; actualOriginals += recovery.OriginalCalls;
                    run.ThrowCallbackFailure();
                    if (mode == "observer-fault") run.Check(channel.Captures.Sum() == previousCaptures
                        && channel.First!.ReadActivity() is { Known: false, InFlight: 0 }, "Sticky broker Unknown admitted a new capture after a fault.");
                    else run.Check(channel.Captures[0] == 2 && channel.Captures[1] == 2 && channel.Completed[0] == 2 && channel.Completed[1] == 2
                        && channel.Released[0] == 2 && channel.Released[1] == 2, "Normal original after native exception did not restore the exact callback/context chain.");
                }
                var afterObserved = ReadInfo(run, read, kind);
                var expectedNative = 4UL + (ulong)recoveryCalls;
                run.Check(NativeBootstrappedOperationBridge.MatchesInformation(afterObserved, identity, true)
                    && afterObserved.Entered == expectedNative && afterObserved.Exited == expectedNative && afterObserved.InFlight == 0,
                    "Attached original exception or observer failure leaked the actual native call.");
                var brokerKnown = mode != "observer-fault";
                run.Check(channel.First!.ReadActivity() is { InFlight: 0 } firstActivity && firstActivity.Known == brokerKnown
                    && broker.ReadExactActivity(target) is { InFlight: 0 } exactActivity && exactActivity.Known == brokerKnown
                    && channel.Bridge!.ReadActivity() is { Known: true, InFlight: 0 }, "The real native/broker activity or sticky fault responsibility differs.");
                channel.First.Dispose(); channel.Second!.Dispose(); Collect();
                if (mode == "detach")
                {
                    var previousCaptures = channel.Captures.Sum();
                    var empty = Invoke(run, invoke, targetAddress, kind, 0, method); actualOriginals += empty.OriginalCalls; ++detachCalls;
                    run.ThrowCallbackFailure(); run.Check(channel.Captures.Sum() == previousCaptures && empty.OriginalCalls == 1,
                        "Logical detach did not preserve the native original without user delivery.");
                    channel.Rebind(); Collect();
                    var rebound = Invoke(run, invoke, targetAddress, kind, 0, method); actualOriginals += rebound.OriginalCalls; ++detachCalls;
                    run.ThrowCallbackFailure();
                    run.Check(channel.ReboundCaptures == 1 && channel.ReboundCompletes == 1 && channel.ReboundDisposes == 1
                        && channel.Factories == 1 && channel.Rebound!.ReadActivity() is { Known: true, InFlight: 0 },
                        "GC/rebind replaced the permanent native observer or lost its real CLR callbacks.");
                    channel.Rebound!.Dispose();
                }
                var final = ReadInfo(run, read, kind);
                run.Check(NativeBootstrappedOperationBridge.MatchesInformation(final, identity, true) && final.InFlight == 0
                    && final.Entered == final.Exited && final.Entered == expectedNative + (mode == "detach" ? 2UL : 0UL)
                    && published.SequenceEqual(Bytes(targetAddress, 32)) && Reaches(targetAddress, identity.Entry)
                    && engine.Calls == 0, "Permanent source/slot identity changed after observation, exception, detach or GC.");
                captures += channel.Captures.Sum() + channel.ReboundCaptures;
                completed += channel.Completed.Sum() + channel.ReboundCompletes;
                disposed += channel.Released.Sum() + channel.ReboundDisposes;
                rows.Add(new { kind, slot = Slot, target = identity.Target.ToInt64(), original = identity.Original.ToInt64(), entry = identity.Entry.ToInt64(),
                    identity.Epoch, preAttach = beforeResults, attachedOriginalMode = postMode, recoveryCalls,
                    captures = channel.Captures, completions = channel.Completed, disposals = channel.Released,
                    reboundCaptures = channel.ReboundCaptures, reboundCompletes = channel.ReboundCompletes, reboundDisposes = channel.ReboundDisposes,
                    final.Entered, final.Exited, final.InFlight, final.Unknown, final.EnterFaults, final.ExitFaults,
                    brokerKnown, nativeKnown = true, factoryCalls = channel.Factories, brokerEngineCalls = engine.Calls,
                    sourceInstructionsUnchangedAfterAdoption = true, controlledObserverFaults = channel.ControlledFaults,
                    actualSequence = firstFrame.Sequence, actualSelf = firstFrame.Self.ToInt64(), actualArg0 = firstFrame.Arg0.ToInt64(),
                    actualArg1 = firstFrame.Arg1.ToInt64(), actualMethod = firstFrame.Method.ToInt64(), actualSourceCaller = firstFrame.Caller.ToInt64(),
                    actualOriginalCaller = observed.OriginalCaller.ToInt64(), nativeException = new { code = observed.Code, flags = observed.Flags,
                        parameters = observed.ParameterCount, information = new[] { observed.Info0, observed.Info1, observed.Info2, observed.Info3 },
                        address = observed.ExceptionAddress.ToInt64() } });
                Owners.AddRange(new object[] { main, broker, peer, engine, logger, channel });
            }
            var expectedOriginals = mode is "detach" ? 18UL : mode is "original-seh" or "original-cpp" or "observer-fault" ? 15UL : 12UL;
            run.Check(actualOriginals == expectedOriginals && preAttachOriginals == 9 && rows.Count == 3,
                "The actual pre/post-attach original totals differ from the three typed channels.");
            run.Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && files.All(row => Hash(row.Key) == row.Value),
                "A game module was loaded or an offline fixture input changed on disk.");
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "native-operation-bootstrap-managed-fixture", mode,
                passed = true, checks = run.Checks, failures = 0, modeVerified = true, actualOriginalCalls = actualOriginals,
                preAttachOriginalCalls = preAttachOriginals, configuredChannels = 3, attachedChannels = 3, sharedPeerSinks = 3,
                actualDobbyPreparedTargets = 3, actualDobbyCommittedTargets = 3, captures, completions = completed, disposals = disposed,
                logicalDetachExtraCalls = detachCalls, nativeInFlight = 0, nativeToClrCallbackVerified = true,
                actualDobbyPublicationVerified = true, productionBridgeAndSharedBrokerVerified = true,
                bootstrapUnattachedOriginalsVerified = true, immutableNativeIdentityVerified = true,
                sourceInstructionsUnchangedAfterAdoption = true, metadataIdentityControlled = true,
                actualIl2CppMetadataResolved = false, actualLedgerCaptureVerified = false, firstInstallReceiptUsed = false,
                firstInstallSafetyVerified = false, concurrentFirstPublicationVerified = false, oldCallAcrossAttachVerified = false,
                exceptionFlagsTransparencyVerified = false, nativeExceptionsCaughtBeforePInvoke = true,
                engineCancellationExecuted = false, gameCodeExecuted = false, gameRuntimeVerified = false, installed = false,
                diskHashesUnchanged = true, results = rows, files = files.Select(row => new { path = row.Key, beforeSha256 = row.Value, afterSha256 = Hash(row.Key) }).ToArray() }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "native-operation-bootstrap-managed-fixture", mode,
                passed = false, checks = run.Checks, failures = 1, failure = error.ToString(), gameCodeExecuted = false,
                gameRuntimeVerified = false, firstInstallReceiptUsed = false })); return 1;
        }
    }

    private sealed class Channel
    {
        private readonly Run _run; private readonly string _mode; private readonly VerifiedHookTarget _target;
        private readonly NativeOperationChannelIdentity _identity; private readonly ExactHookBroker _broker, _peer;
        internal readonly int[] Captures = new int[2], Completed = new int[2], Released = new int[2];
        internal readonly List<NativeOperationFrame> Frames = new(); internal readonly List<NativeOperationExitFlags> Flags = new();
        internal NativeBootstrappedOperationBridge? Bridge; internal ExactHookBroker.Binding? First, Second, Rebound;
        internal int Factories, ControlledFaults, ReboundCaptures, ReboundCompletes, ReboundDisposes;
        internal Channel(Run run, string mode, VerifiedHookTarget target, NativeOperationChannelIdentity identity,
            ExactHookBroker broker, ExactHookBroker peer)
        { _run = run; _mode = mode; _target = target; _identity = identity; _broker = broker; _peer = peer; }
        internal void Attach(string helperPath, ILogger logger)
        {
            First = _broker.BindConfiguredNativeOperationObservation<Context>(_target, _identity,
                () => { ++Factories; return Bridge = NativeBootstrappedOperationBridge.AdoptBootstrap(helperPath, _identity, logger); },
                frame => Capture(0, frame), (context, frame, flags) => Complete(0, context, frame, flags), (_, _) => { });
            Second = _peer.BindConfiguredNativeOperationObservation<Context>(_target with { Id = _target.Id + ".peer" }, _identity,
                () => throw new InvalidOperationException("A peer tried to attach a second native observer."),
                frame => Capture(1, frame), (context, frame, flags) => Complete(1, context, frame, flags), (_, _) => { });
        }
        private Context Capture(int sink, NativeOperationFrame frame)
        {
            _run.Callback(() => {
                ++Captures[sink];
                _run.Check(frame.IsValid && frame.Kind == _identity.Kind && frame.Method == _target.MethodInfo
                    && First!.ReadActivity() is { InFlight: > 0 }, "The real adopted callback has a foreign frame or no active native window.");
                if (sink == 0)
                {
                    _run.Check(_run.Sequences.Add(frame.Sequence), "Actual native sequence was reused."); Frames.Add(frame);
                    if (_mode == "detach") First!.Dispose();
                }
                else _run.Check(Frames.Last().Equals(frame), "Two sinks received different physical native frames.");
            });
            // This fault is intentional, after all assertion checks. Recording
            // it as callbackFailure would incorrectly turn containment into a
            // swallowed test assertion; only unexpected assertions are saved.
            if (sink == 0 && _mode == "observer-fault") { ++ControlledFaults; throw new IOException("Controlled adopted observer fault."); }
            return new Context(_run, frame, () => ++Released[sink]);
        }
        private void Complete(int sink, Context context, NativeOperationFrame frame, NativeOperationExitFlags flags)
            => _run.Callback(() => {
                ++Completed[sink]; Flags.Add(flags);
                _run.Check(context.Frame.Equals(frame) && !context.Disposed && First!.ReadActivity() is { InFlight: > 0 },
                    "The exact retained context did not complete in its actual native window.");
            });
        internal void VerifyObserved(Result result, uint nativeMode)
        {
            var expected = nativeMode == 0 ? NativeOperationExitFlags.Returned : NativeOperationExitFlags.Abnormal;
            _run.Check(Captures[0] == 1 && Captures[1] == 1 && Completed[1] == 1 && Released[1] == 1,
                "A peer observer or exact context cleanup did not execute once.");
            _run.Check(Completed[0] == (_mode == "observer-fault" ? 0 : 1) && Released[0] == Completed[0]
                && Flags.Count == (_mode == "observer-fault" ? 1 : 2) && Flags.All(value => value == expected),
                "Observer fault/native exception completion facts differ.");
            _run.Check(ControlledFaults == (_mode == "observer-fault" ? 1 : 0) && Frames.Count == 1 && result.OriginalCalls == 1,
                "Controlled observer faults or native once-only execution differ.");
        }
        internal void Rebind()
        {
            Rebound = _broker.BindConfiguredNativeOperationObservation<Context>(_target with { Id = _target.Id + ".rebound" }, _identity,
                () => throw new InvalidOperationException("Logical rebind tried to replace the permanent native observer."),
                frame => _run.Callback(() => { ++ReboundCaptures; _run.Check(frame.IsValid, "Rebound frame invalid.");
                    return new Context(_run, frame, () => ++ReboundDisposes); }),
                (context, frame, flags) => _run.Callback(() => { ++ReboundCompletes; _run.Check(context.Frame.Equals(frame)
                    && flags == NativeOperationExitFlags.Returned, "Rebound context/flags differ."); }), (_, _) => { });
        }
    }
    private sealed class Context : IDisposable
    {
        private readonly Run _run; private readonly Action _release; internal readonly NativeOperationFrame Frame; internal bool Disposed;
        internal Context(Run run, NativeOperationFrame frame, Action release) => (_run, Frame, _release) = (run, frame, release);
        public void Dispose() => _run.Callback(() => { _run.Check(!Disposed, "A physical context was disposed twice."); Disposed = true; _release(); });
    }
    private sealed class Run
    {
        internal int Checks; internal readonly HashSet<ulong> Sequences = new(); private Exception? _callbackFailure;
        internal void Check(bool value, string message) { Interlocked.Increment(ref Checks); if (!value) throw new InvalidOperationException(message); }
        internal T Callback<T>(Func<T> body) { try { return body(); } catch (Exception error) { Interlocked.CompareExchange(ref _callbackFailure, error, null); throw; } }
        internal void Callback(Action body) { try { body(); } catch (Exception error) { Interlocked.CompareExchange(ref _callbackFailure, error, null); throw; } }
        internal void ThrowCallbackFailure() { if (Volatile.Read(ref _callbackFailure) is { } error) throw new InvalidOperationException("An actual CLR callback assertion failed.", error); }
    }
    private static Result Invoke(Run run, InvokeNative invoke, IntPtr target, uint kind, uint mode, IntPtr method)
    {
        var result = new Result { Version = 1, Size = 144 };
        run.Check(invoke(target, kind, mode, method, ref result) == 0 && result.Version == 1 && result.Size == 144
            && result.Kind == kind && result.Mode == mode && result.NativeFailures == 0 && result.NativeChecks > 0 && result.OriginalCalls == 1
            && result.Method == method && result.Self != IntPtr.Zero && result.OriginalCaller != IntPtr.Zero,
            "The actual patched target/original ABI or native once-only checks failed.");
        run.Check(result.Returned == (mode == 0 ? 1u : 0u) && result.Caught == (mode == 0 ? 0u : 1u),
            "The original exception escaped its owned native catcher or changed normal return.");
        if (mode == 1) run.Check(result.Code == 0xe04f4d53 && result.ParameterCount == 3 && result.Info0 == 0xfedcba9876543210UL
            && result.Info1 == unchecked((ulong)result.Self.ToInt64()) && result.Info2 == kind && result.ExceptionAddress != IntPtr.Zero,
            "Original SEH record/payload differs.");
        if (mode == 2) run.Check(result.Code == 0xe06d7363 && result.ParameterCount == 4 && result.Info0 == 0x19930520
            && result.Info1 != 0 && result.Info2 != 0 && result.Info3 != 0 && result.ExceptionAddress != IntPtr.Zero,
            "Original typed C++ exception did not retain its native payload record.");
        return result;
    }
    private static NativeOperationBootstrapInformation NewInfo() => new() { Version = 1, Size = 96 };
    private static NativeOperationBootstrapInformation ReadInfo(Run run, InfoNative read, uint kind)
    { var info = NewInfo(); run.Check(read(kind, Slot, ref info) == 0, "Actual bootstrap information could not be read."); return info; }
    private static byte[] Bytes(IntPtr address, int size) { var data = new byte[size]; Marshal.Copy(address, data, 0, size); return data; }
    private static bool Reaches(IntPtr address, IntPtr expected)
    {
        var visited = new HashSet<IntPtr>();
        for (var i = 0; i < 4; ++i)
        {
            if (address == expected) return true;
            if (address == IntPtr.Zero || !visited.Add(address)) return false;
            HookTargetValidation.VerifyReadable(address, 16);
            if (Marshal.ReadByte(address) == 0xe9) address = IntPtr.Add(address, 5 + Marshal.ReadInt32(address, 1));
            else if (Marshal.ReadByte(address) == 0xff && Marshal.ReadByte(address, 1) == 0x25)
            { var slot = IntPtr.Add(address, 6 + Marshal.ReadInt32(address, 2)); HookTargetValidation.VerifyReadable(slot, 8); address = Marshal.ReadIntPtr(slot); }
            else if (Marshal.ReadByte(address) == 0x48 && Marshal.ReadByte(address, 1) == 0xb8 && Marshal.ReadByte(address, 10) == 0xff && Marshal.ReadByte(address, 11) == 0xe0)
                address = Marshal.ReadIntPtr(address, 2);
            else return false;
        }
        return address == expected;
    }
    private sealed class NeverInstallEngine : INativeDetourEngine, IPreparedNativeDetourEngine
    {
        internal int Calls;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) { ++Calls; throw new InvalidOperationException("Adoption tried to Install."); }
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement) { ++Calls; throw new InvalidOperationException("Adoption tried to Prepare."); }
        public void Commit(VerifiedHookTarget target) { ++Calls; throw new InvalidOperationException("Adoption tried to Commit."); }
    }
    private sealed class Main : IMainThreadDispatcher
    { private readonly int _thread = Environment.CurrentManagedThreadId; public bool IsMainThread => Environment.CurrentManagedThreadId == _thread; public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Unexpected worker."); } public Task InvokeAsync(Action body, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
    private static string AbsoluteEnv(string name) { var path = Environment.GetEnvironmentVariable(name); if (path is null || !Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new ArgumentException("Missing absolute fixture: " + name); return Path.GetFullPath(path); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    private static bool Rejected<T>(Action body) where T : Exception { try { body(); return false; } catch (T) { return true; } }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll")] private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation info, nuint size);
    [DllImport("kernel32.dll")] private static extern int VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("kernel32.dll")] private static extern int FlushInstructionCache(IntPtr process, IntPtr address, nuint size);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
}
