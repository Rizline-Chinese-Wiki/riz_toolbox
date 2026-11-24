using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>Actual Windows x64 MASM caller -> Dobby -> native void helper ->
/// C++ original and native exception catches. No game image is executed.</summary>
internal static class NativeVoidAbiRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("native void0 preserves real Dobby arguments, exact tail-relay caller, foreign recursion and probe snapshots", Observe),
        ("native void0 keeps original SEH and C++ unwind, worker activity and logical detach inside the physical window", UnwindAndWorker),
        ("native void0 contains managed capture, after and release failures while calling the native original once", ManagedFaults),
        ("native void0 native callback SEH and C++ faults preserve exact flags, original exceptions and Prepare Configure Commit", NativeCallbackFaults)
    };
    // Native patches and callback delegates stay owned for the process lifetime.
    // These four fixed physical entries do not reset helper slot ownership.
    private static readonly List<Fixture> Owners = new();
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Context : IDisposable
    {
        internal readonly NativeVoidFrame Frame;
        private readonly Action _release;
        internal Context(NativeVoidFrame frame, Action release) => (Frame, _release) = (frame, release);
        public void Dispose() => _release();
    }
    private static void Observe()
    {
        var f = Own(0); var captured = 0; var after = 0; var released = 0; var faults = 0;
        var physicalEnter = 0; var physicalExit = 0; var foreignSeen = 0;
        var stack = new Stack<(IntPtr Self, IntPtr Method)>();
        f.Binding = f.Broker.BindNativeVoidObservation(f.Target, () => f.Bridge,
            frame => {
                Check(frame.IsValid && frame.Caller == f.ExpectedCaller && f.IsNativeCaller(frame.Caller), "Void0 caller differs from the actual assembly return label through the tail relay.");
                Check(frame.Method == f.Target.MethodInfo || frame.Method == IntPtr.Zero, "Void0 MethodInfo changed.");
                if (frame.Self == f.Foreign) { foreignSeen++; return null; }
                Check(frame.Self == f.Self, "Void0 full receiver identity was truncated.");
                Check(f.Bridge.TryAcquireAdmission(0) is { Status: NativeVoidAdmissionAttemptStatus.Rejected, Lease: null },
                    "A managed capture inside the actual native admitted window closed its own gate.");
                captured++;
                Check(f.Read().Entered == f.Read().Exited, "Capture ran after the native original entered.");
                return new Context(frame, () => { released++; Check(f.Bridge.ReadActivity().InFlight >= 1, "Void0 context release escaped the native physical window."); });
            }, (context, frame) => {
                after++; Check(context.Frame.Equals(frame) && f.Read().Entered == f.Read().Exited, "After lost its captured frame or ran before original completion.");
                Check(f.Bridge.ReadActivity().InFlight >= 1, "Void0 after ran after native drain.");
            }, (_, _) => Interlocked.Increment(ref faults));
        using var probe = f.Broker.BindBoundaryProbe(f.Target,
            (self, value, method) => { physicalEnter++; Check(value == 0, "Void0 probe manufactured a Boolean value."); stack.Push((self, method)); },
            (self, value, method, returned) => { physicalExit++; Check(value == 0 && returned && stack.Pop() == (self, method), "Void0 physical probe exits failed to pair exact nested identities."); });
        ExactHookBroker.BoundaryProbeBinding? retiring = null; var retiredEnter = 0; var retiredExit = 0;
        retiring = f.Broker.BindBoundaryProbe(f.Target, (_, _, _) => { retiredEnter++; retiring!.Dispose(); }, (_, _, _, _) => retiredExit++);
        foreach (var method in new[] { f.Target.MethodInfo, IntPtr.Zero })
        {
            var before = f.Read(); var result = f.Call(f.Self, method);
            var observed = f.Read();
            Check(result.Status == 1 && observed.Entered == before.Entered + 1 && observed.Exited == observed.Entered
                && observed.Self == f.Self && observed.Method == method && observed.Argument == IntPtr.Zero,
                "Void0 original did not execute once with full self/MethodInfo.");
        }
        Check(retiredEnter == 1 && retiredExit == 1, "Detached probe lost the exit from its immutable entry snapshot.");
        f.Control(0, false, f.Target.Address, f.Foreign, f.Target.MethodInfo);
        var beforeNested = f.Read(); f.Call(f.Self, f.Target.MethodInfo); var nested = f.Read();
        Check(nested.Entered == beforeNested.Entered + 2 && nested.Exited == nested.Entered && foreignSeen == 1,
            "Native recursive foreign receiver inherited its parent's ownership or escaped observation.");
        f.Control(0, false, f.Target.Address, f.Foreign, f.ForeignMethod);
        beforeNested = f.Read(); f.Call(f.Self, f.Target.MethodInfo); nested = f.Read();
        Check(nested.Entered == beforeNested.Entered + 2 && nested.Exited == nested.Entered && faults == 1,
            "Foreign recursive MethodInfo did not preserve native original and report its own boundary.");
        Check(captured == 4 && after == 4 && released == 4 && physicalEnter == 6 && physicalExit == 6 && stack.Count == 0,
            "Void0 capture/after/release or every-invocation physical probe counts differ.");
        Check(f.Binding.ReadActivity() is { Known: true, InFlight: 0 } && probe.ReadActivity() is { Known: true, InFlight: 0 }, "Void0 normal entries failed to drain.");
        f.Control(0, false); f.Binding.Dispose(); retiring.Dispose();
        VerifyRealAdmissionLease(f);
    }
    private static void VerifyRealAdmissionLease(Fixture f)
    {
        // Same real, previously installed Dobby parent and private bridge protocol.
        // No patched instruction is written while testing this admission barrier.
        var physicalEnter = 0; var physicalExit = 0;
        using var physical = f.Broker.BindBoundaryProbe(f.Target,
            (_, _, _) => Interlocked.Increment(ref physicalEnter),
            (_, _, _, _) => Interlocked.Increment(ref physicalExit));
        var before = f.Read();
        var attempt = f.Bridge.TryAcquireAdmission(1000);
        Check(attempt.Status == NativeVoidAdmissionAttemptStatus.Acquired && attempt.Lease is not null,
            "Actual configured native bridge did not issue a managed admission lease.");
        var lease = attempt.Lease!;
        Task<Result>? worker = null;
        try {
            Check(lease.VerifyClosed() && !lease.Unknown && !lease.WriteAttempted,
                "Actual native receipt was not verified before use.");
            worker = Task.Run(() => f.Call(f.Self, f.Target.MethodInfo, true));
            Check(SpinWait.SpinUntil(() => f.Bridge.ReadActivity().InFlight == 1, 5000),
                "Closed native entry did not retain a parked physical worker.");
            Check(!worker.IsCompleted && f.Read().Entered == before.Entered && physicalEnter == 0 && physicalExit == 0 && lease.VerifyClosed()
                && f.Bridge.ReadActivity() is { Known: true, InFlight: 1 },
                "Parked call reached an original/CLR observer or invalidated an otherwise closed native gate.");
            Check(!Task.Run(lease.ReleaseUnmodified).GetAwaiter().GetResult() && !lease.Unknown && lease.VerifyClosed(),
                "Wrong OS thread released or poisoned the owner's valid real receipt.");
            var busy = Task.Run(() => f.Bridge.TryAcquireAdmission(0)).GetAwaiter().GetResult();
            Check(busy is { Status: NativeVoidAdmissionAttemptStatus.Rejected, Lease: null } && lease.VerifyClosed(),
                "Concurrent Close reused or replaced the issued native receipt.");
            Check(lease.ReleaseUnmodified() && lease.Released && !lease.ReleaseUnmodified(),
                "The correct owner failed to release exactly once.");
        }
        finally {
            lease.Dispose();
            if (worker is not null) Check(worker.Wait(5000), "Released real native worker failed to join.");
        }
        AssertResult(worker!.Result, 0, 0);
        Check(f.Read().Entered == before.Entered + 1 && f.Read().Exited == f.Read().Entered
            && physicalEnter == 1 && physicalExit == 1 && f.Bridge.ReadActivity() is { Known: true, InFlight: 0 },
            "Reopened parked call repeated its original or leaked physical activity.");

        // Close timeout is a native known rejection, so no managed lease opens
        // or retries an uncertain outcome. The original was already admitted.
        before = f.Read(); f.Control(0, true);
        worker = Task.Run(() => f.Call(f.Self, f.Target.MethodInfo, true));
        try {
            Check(SpinWait.SpinUntil(() => f.Read().Entered > f.Read().Exited, 5000),
                "Timeout integration worker did not reach its native original.");
            Check(f.Bridge.TryAcquireAdmission(20) is { Status: NativeVoidAdmissionAttemptStatus.Rejected, Lease: null },
                "Native active-window timeout was misrepresented as an acquired or unknown managed lease.");
        }
        finally { f.Resume(); Check(worker.Wait(5000), "Timed-out native lease lost original progress."); }
        AssertResult(worker.Result, 0, 0);
        Check(f.Read().Entered == before.Entered + 1 && f.Read().Exited == f.Read().Entered
            && physicalEnter == 2 && physicalExit == 2 && f.Bridge.ReadActivity() is { Known: true, InFlight: 0 }, "Timeout did not reopen native admission.");
        f.Control(0, false);
        var final = f.Bridge.TryAcquireAdmission(1000);
        Check(final.Status == NativeVoidAdmissionAttemptStatus.Acquired && final.Lease is not null,
            "Native slot failed to close again after a confirmed timeout.");
        using var last = final.Lease!;
        Check(last.VerifyClosed() && last.Epoch > lease.Epoch, "Real native admission epoch was reused.");
        // Fixture-only model of an uncertain future writer: deliberately retain
        // the issued closed gate and its DLL/delegates for the process lifetime.
        Check(last.MarkWriteAttempted(), "An exact known real lease could not record its first write attempt.");
        last.Dispose();
        Check(last.WriteAttempted && !last.Released && !last.ReleaseUnmodified() && last.VerifyClosed(),
            "Dispose reopened an actual native gate after a write attempt.");
        last.RetainUnknown("Fixture-only uncertain future publication; no instruction was written.");
        Check(last.Unknown && !last.VerifyClosed() && !last.ReleaseUnmodified()
            && f.Bridge.ReadActivity() is { Known: false, InFlight: 0 },
            "A retained real admission responsibility failed to reach the bridge's sticky unknown authority.");
    }
    private static void UnwindAndWorker()
    {
        var f = Own(1); var captures = 0; var after = 0; var releases = 0; var faults = 0;
        f.Binding = f.Broker.BindNativeVoidObservation(f.Target, () => f.Bridge,
            frame => { captures++; return new Context(frame, () => { releases++; Check(f.Bridge.ReadActivity().InFlight >= 1, "Abnormal void context released after native drain."); }); },
            (_, _) => after++, (_, _) => Interlocked.Increment(ref faults));
        var probeEnter = 0; var probeExit = 0; var abnormal = 0; var workers = 0;
        var mainThread = GetCurrentThreadId();
        using var probe = f.Broker.BindBoundaryProbe(f.Target,
            (self, value, method) => { Interlocked.Increment(ref probeEnter); Check(self == f.Self && value == 0 && method == f.Target.MethodInfo, "Worker/exception probe input changed."); if (GetCurrentThreadId() != mainThread) Interlocked.Increment(ref workers); },
            (_, _, _, returned) => { Interlocked.Increment(ref probeExit); if (!returned) Interlocked.Increment(ref abnormal); });
        foreach (var mode in new uint[] { 0, 1, 2 })
        {
            f.Control(mode, false); var before = f.Read(); var result = f.Call(f.Self, f.Target.MethodInfo);
            AssertResult(result, mode, 1); Check(f.Read().Entered == before.Entered + 1 && f.Read().Exited == f.Read().Entered, "Abnormal void original did not run exactly once.");
            Check(f.Bridge.ReadActivity() is { Known: true, InFlight: 0 } && f.Binding.ReadActivity().InFlight == 0,
                "Abnormal void original failed to drain its physical transport.");
            Check(f.Binding.ReadActivity().Known == (mode == 0), "Native original exception failed to retain unknown observation authority.");
        }
        Check(captures == 3 && after == 1 && releases == 3 && abnormal == 2, "Void0 abnormal original manufactured success or lost cleanup.");
        f.Control(0, true); var beforeWorker = f.Read();
        var worker = Task.Run(() => f.Call(f.Self, f.Target.MethodInfo, true));
        try {
            Check(SpinWait.SpinUntil(() => f.Read().Entered > f.Read().Exited, 5000), "Native void worker failed to enter its original.");
            Check(f.Bridge.ReadActivity() is { Known: true, InFlight: 1 } && f.Binding.ReadActivity().InFlight == 1 && probe.ReadActivity().InFlight == 1,
                "Native void worker appeared physically drained.");
            f.Binding.Dispose(); probe.Dispose();
            Check(f.Binding.ReadActivity().InFlight == 1 && probe.ReadActivity().InFlight == 1, "Logical detach ended the native original window.");
        }
        finally { f.Resume(); Check(worker.Wait(5000), "Native void worker failed to join."); }
        AssertResult(worker.Result, 0, 1);
        Check(worker.Result.Thread != mainThread && workers == 1 && probeEnter == 4 && probeExit == 4 && f.Read().Entered == beforeWorker.Entered + 1,
            "Native worker/probe snapshot pairing differs.");
        Check(captures == 3 && releases == 3 && faults == 3 && f.Binding.ReadActivity() is { Known: false, InFlight: 0 },
            "Worker captured a Unity context or native abnormal exits lost their fault authority.");
        f.Control(0, false); f.Call(f.Self, f.Target.MethodInfo);
        Check(captures == 3 && probeEnter == 4 && probeExit == 4, "Detached void observer/probe was called again.");
    }
    private static void ManagedFaults()
    {
        var f = Own(2); var mode = 0; var captures = 0; var after = 0; var releases = 0; var faults = 0;
        f.Binding = f.Broker.BindNativeVoidObservation(f.Target, () => f.Bridge,
            frame => { captures++; if (mode == 1) throw new InvalidOperationException("capture fixture");
                return new Context(frame, () => { releases++; Check(f.Bridge.ReadActivity().InFlight >= 1, "Faulted managed context released after native drain."); if (mode == 3) throw new InvalidOperationException("release fixture"); }); },
            (_, _) => { after++; if (mode == 2) throw new InvalidOperationException("after fixture"); },
            (_, _) => { faults++; if (mode == 4) throw new InvalidOperationException("fault fixture"); });
        foreach (var failure in new[] { 1, 2, 3, 0 })
        {
            mode = failure; var before = f.Read(); AssertResult(f.Call(f.Self, f.Target.MethodInfo), 0, 2);
            Check(f.Read().Entered == before.Entered + 1 && f.Read().Exited == f.Read().Entered && f.Bridge.ReadActivity() is { Known: true, InFlight: 0 }, "Managed observer failure suppressed/repeated the native original or leaked native activity.");
        }
        Check(captures == 4 && after == 3 && releases == 3 && faults >= 1, "Managed observer failure lost context release or fault notification.");
        mode = 4; var beforeAlias = f.Read(); AssertResult(f.Call(f.Self, f.ForeignMethod), 0, 2);
        Check(f.Read().Entered == beforeAlias.Entered + 1 && f.Read().Exited == f.Read().Entered && captures == 4,
            "Throwing fault handler allowed a foreign MethodInfo capture or changed native execution.");
        f.Binding.Dispose();
    }
    private static void NativeCallbackFaults()
    {
        var f = Own(3); var helper = NativeLibrary.Load(f.Helper);
        var getEntry = Export<GetTargetNative>(helper, "RizVoidContext_GetEntry");
        var configure = Export<ConfigureNative>(helper, "RizVoidContext_Configure");
        var stats = Export<StatsNative>(helper, "RizVoidContext_GetStats");
        var callback = f.Export<GetTargetNative>("RtVoidCallbackAddress");
        var control = f.Export<SingleNative>("RtVoidCallbackControl");
        var read = f.Export<ReadScalarNative>("RtVoidCallbackRead");
        const uint slot = 31; var user = new IntPtr(unchecked((long)0x6789abcdef012340UL));
        var config = new NativeConfig { Version = 1, Size = 40, Enter = callback(0), Exit = callback(1), User = user };
        var engine = new DobbyDetourEngine(f.Dobby); f.DirectEngine = engine;
        var beforeBytes = new byte[32]; Marshal.Copy(f.Target.Address, beforeBytes, 0, beforeBytes.Length);
        f.Call(f.Self, f.Target.MethodInfo); Check(read(0) == 0, "Unpatched native void callback executed.");
        config.Original = engine.Prepare(f.Target, getEntry(slot));
        var current = new byte[32]; Marshal.Copy(f.Target.Address, current, 0, current.Length);
        Check(current.SequenceEqual(beforeBytes), "Dobby Prepare published the void replacement early.");
        var invalid = config; invalid.Version = 2; Check(configure(slot, ref invalid) == 1, "Void helper accepted an invalid configuration version.");
        invalid = config; invalid.Size = 39; Check(configure(slot, ref invalid) == 1, "Void helper accepted a truncated configuration.");
        invalid = config; invalid.Original = IntPtr.Zero; Check(configure(slot, ref invalid) == 1, "Void helper accepted a null original.");
        Check(configure(slot, ref config) == 0 && configure(slot, ref config) == 2, "Void helper configuration was not immutable.");
        config = default; // The native helper owns its immutable configuration copy.
        f.Call(f.Self, f.Target.MethodInfo); Check(read(0) == 0, "Configure published the void detour before Commit.");
        engine.Commit(f.Target); Marshal.Copy(f.Target.Address, current, 0, current.Length);
        Check(!current.SequenceEqual(beforeBytes), "Dobby Commit did not publish the void replacement.");
        ulong expectedCalls = 0, expectedEnterFaults = 0, expectedExitFaults = 0;
        foreach (var originalMode in new uint[] { 0, 1, 2 })
            foreach (var callbackMode in new uint[] { 0, 1, 2, 4, 8, 5, 10 })
            {
                f.Control(originalMode, false); Check(control(callbackMode) == 1, "Native callback fixture control failed.");
                var before = f.Read(); var result = f.Call(f.Self, f.Target.MethodInfo); AssertResult(result, originalMode, 3);
                var enterFault = (callbackMode & 3) != 0; var exitFault = (callbackMode & 12) != 0;
                var expectedFlags = (originalMode == 0 ? 1u : 2u) | (enterFault ? 4u : 0u);
                Check(read(0) == 1 && read(1) == 1 && read(2) == expectedFlags && read(3) == (enterFault ? 0 : unchecked((nuint)0xFEDCBA9876543210UL)),
                    "Native callback fault changed exact helper flags or token pairing.");
                Check(read(4) == unchecked((nuint)f.Self.ToInt64()) && read(5) == unchecked((nuint)f.Target.MethodInfo.ToInt64())
                    && read(6) == unchecked((nuint)f.ExpectedCaller.ToInt64()) && read(7) == unchecked((nuint)user.ToInt64()) && read(8) == 0,
                    "Native callback frame/self/MethodInfo/actual assembly caller/user identity changed.");
                Check(f.Read().Entered == before.Entered + 1 && f.Read().Exited == f.Read().Entered
                    && f.Read().Self == f.Self && f.Read().Method == f.Target.MethodInfo, "Native callback fault skipped/repeated the original or changed its full arguments.");
                expectedCalls++; if (enterFault) expectedEnterFaults++; if (exitFault) expectedExitFaults++;
                var activity = new NativeStats { Version = 1, Size = 48 }; Check(stats(slot, ref activity) == 0, "Void native helper statistics failed.");
                Check(activity.Entered == expectedCalls && activity.Exited == expectedCalls && activity.InFlight == 0
                    && activity.EnterFaults == expectedEnterFaults && activity.ExitFaults == expectedExitFaults, "Native callback faults leaked activity or were silently omitted from helper statistics.");
            }
        f.Control(0, false); Check(control(0) == 1, "Native callback final control failed.");
    }
    private static Fixture Own(uint kind) { var fixture = new Fixture(kind); Owners.Add(fixture); return fixture; }
    private static void AssertResult(Result result, uint mode, uint kind)
    {
        Check(result.Status == (mode == 0 ? 1u : 2u), "Native void original status changed.");
        if (mode == 0) Check(result.ExceptionKind == 0 && result.ExceptionCode == 0 && result.Reference == IntPtr.Zero, "Void0 normal original manufactured a result or exception.");
        else Check(result.ExceptionKind == mode && result.ExceptionCode == (mode == 1 ? 0xE04F494Eu : 0xE06D7363u)
            && result.Payload == (0xBADC0FFEE0DDF00DUL ^ kind) && result.OriginalKind == 0x5600 + kind,
            "Native void original SEH/C++ code, kind or payload was not preserved.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Result
    { internal uint Size, Status, ExceptionKind, ExceptionCode; internal ulong Payload; internal IntPtr Reference; internal uint Thread, OriginalKind; }
    [StructLayout(LayoutKind.Sequential)] private struct Observation
    { internal uint Size, Kind; internal ulong Entered, Exited; internal IntPtr Self, Argument, Method; internal ulong Returned; internal uint Scalar, Thread; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeConfig
    { internal uint Version, Size; internal IntPtr Original, Enter, Exit, User; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeStats
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetTargetNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ControlNative(uint kind, uint mode, uint block, IntPtr nested, IntPtr nestedSelf, IntPtr nestedMethod);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint kind, ref Observation result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(uint kind, IntPtr entry, IntPtr self, IntPtr method, ref Result result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ResumeNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SingleNative(uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint ReadScalarNative(uint field);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint slot, ref NativeConfig value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint StatsNative(uint slot, ref NativeStats value);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private static T Export<T>(IntPtr library, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    private sealed class Fixture
    {
        private readonly IntPtr _library; private readonly uint _kind; private readonly int _imageSize;
        private readonly ControlNative _control; private readonly ReadNative _read;
        private readonly InvokeNative _invoke, _worker; private readonly ResumeNative _resume;
        internal readonly NativeVoidContextBridge Bridge; internal readonly ExactHookBroker Broker;
        internal readonly VerifiedHookTarget Target; internal ExactHookBroker.Binding? Binding;
        internal readonly IntPtr ExpectedCaller;
        internal readonly string Helper, Dobby; internal DobbyDetourEngine? DirectEngine;
        internal readonly IntPtr Self = new(unchecked((long)0x12345678abcdef00UL)), Foreign = new(unchecked((long)0x23456789bcdef010UL));
        internal readonly IntPtr ForeignMethod = new(unchecked((long)0x3456789acdef0140UL));
        internal Fixture(uint kind)
        {
            string Env(string key) => Environment.GetEnvironmentVariable(key) ?? throw new InvalidOperationException("Missing native void fixture environment: " + key);
            var path = Env("RIZ_INITIALIZATION_ABI_FIXTURE"); Helper = Env("RIZ_INITIALIZATION_CONTEXT_HELPER"); Dobby = Env("RIZ_INITIALIZATION_ABI_DOBBY");
            _kind = kind; _library = NativeLibrary.Load(path);
            using (var stream = File.OpenRead(path)) using (var pe = new PEReader(stream)) _imageSize = pe.PEHeaders.PEHeader!.SizeOfImage;
            _control = Export<ControlNative>("RtVoidControl"); _read = Export<ReadNative>("RtVoidRead"); _resume = Export<ResumeNative>("RtVoidResume");
            _invoke = Export<InvokeNative>("RtVoidInvoke"); _worker = Export<InvokeNative>("RtVoidInvokeWorker");
            Target = new("void.native.fixture." + kind, Export<GetTargetNative>("RtVoidTarget")(kind), new(unchecked((long)0x3456789acdef0120UL)), new(0x4321), ExactHookAbi.NativeVoid0);
            ExpectedCaller = Export<GetTargetNative>("RtVoidExpectedCaller")(kind);
            var target = Export<GetTargetNative>("RtVoidTarget"); var caller = Export<GetTargetNative>("RtVoidExpectedCaller");
            Check(Enumerable.Range(0, 4).Select(index => target((uint)index)).Distinct().Count() == 4
                && Enumerable.Range(0, 4).Select(index => caller((uint)index)).Distinct().Count() == 4,
                "Native void fixture entries or actual caller labels are not distinct.");
            var log = new Logger(); Bridge = new(Helper, log); Broker = new(Dobby, new MainThreadDispatcher(), log);
            Check(Marshal.SizeOf<NativeVoidFrame>() == 40 && Marshal.SizeOf<NativeConfig>() == 40 && Marshal.SizeOf<NativeStats>() == 48
                && Marshal.SizeOf<Result>() == 40 && Marshal.SizeOf<Observation>() == 64 && IsNativeCaller(ExpectedCaller), "Native void fixture ABI layout changed.");
            Control(0, false);
        }
        internal T Export<T>(string name) where T : Delegate => NativeVoidAbiRegression.Export<T>(_library, name);
        internal bool IsNativeCaller(IntPtr caller) => caller.ToInt64() >= _library.ToInt64() && caller.ToInt64() < _library.ToInt64() + _imageSize;
        internal void Control(uint mode, bool block, IntPtr nested = default, IntPtr nestedSelf = default, IntPtr nestedMethod = default)
            => Check(_control(_kind, mode, block ? 1u : 0u, nested, nestedSelf, nestedMethod) == 1, "Native void fixture control rejected.");
        internal void Resume() => _resume(_kind);
        internal Observation Read() { var value = new Observation { Size = 64 }; Check(_read(_kind, ref value) == 1, "Native void original read failed."); return value; }
        internal Result Call(IntPtr self, IntPtr method, bool worker = false)
        { var result = new Result { Size = 40 }; var status = (worker ? _worker : _invoke)(_kind, Target.Address, self, method, ref result); Check(status == result.Status && status is 1 or 2, "Native void caller/catch failed."); return result; }
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
}
