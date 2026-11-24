using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static class NativeCoroutineAbiRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("native coroutine Start and MoveNext preserve real Dobby ABI, caller, worker, nested publication and SEH C++ unwind", Run),
        ("native coroutine Factory preserves full references and publishes an actual enum before a named Start first MoveNext", RunFactory)
    };
    private static readonly List<Fixture> Owners = new();
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private sealed class Context : IDisposable { private readonly Action _dispose; internal Context(Action dispose) => _dispose = dispose; public void Dispose() => _dispose(); }
    private static void Run()
    {
        var f = new Fixture(); Owners.Add(f); f.NativeMode(0, false);
        var captures = new int[2]; var afters = new int[2]; var releases = new int[2]; var frames = new List<NativeCoroutineFrame>();
        for (var index = 0; index < 2; ++index)
        {
            var kind = index; var bridge = new NativeCoroutineContextBridge(f.Helper, (NativeCoroutineKind)index, f.Log);
            Check(!bridge.ReadActivity().Known, "Unconfigured coroutine bridge fabricated known state.");
            f.Bindings[index] = f.Broker.BindNativeCoroutineObservation(f.Targets[index], (NativeCoroutineKind)index, () => bridge,
                frame => {
                    captures[kind]++; frames.Add(frame);
                    Check(frame.IsValid && frame.Kind == (NativeCoroutineKind)kind && f.IsNativeCaller(frame.Caller)
                        && frame.Caller == f.ExpectedCallers[kind], "Native caller did not preserve the exact assembly return label through the tail relay and Dobby.");
                    return new Context(() => { releases[kind]++; Check(f.Bindings[kind]!.ReadActivity().InFlight >= 1, "Coroutine context released after native drain."); });
                }, (_, frame, result) => { afters[kind]++; Check(result == f.Returned[kind] && frame.Self != IntPtr.Zero, "Observer changed/lost the actual return."); },
                (_, _) => Interlocked.Increment(ref f.Faults), kind == 0 ? f.Alias : null);
        }
        var legacy = 0;
        using var ordinary = f.Broker.BindRetainedBooleanObservation(f.Targets[1], _ => true,
            _ => new Context(() => { }), _ => legacy++);
        foreach (var value in new nuint[] { 0, 1, 0x80, 0xff })
        {
            f.Control(1, value); var returned = f.Call(1, f.Enumerator, IntPtr.Zero, f.Targets[1].MethodInfo);
            Check(unchecked((nuint)returned.Reference.ToInt64()) == value && returned.Status == 1, "MoveNext AL was canonicalized.");
        }
        Check(legacy == 4, "Shared Boolean observers were bypassed.");
        f.Control(0, unchecked((nuint)0x76543210abcdef00UL));
        foreach (var method in new[] { f.Targets[0].MethodInfo, f.Alias.MethodInfo, IntPtr.Zero })
        {
            var result = f.Call(0, f.Controller, f.Enumerator, method); var input = f.Read(0);
            Check(result.Reference == unchecked((IntPtr)(long)f.Returned[0]) && input.Self == f.Controller && input.Argument == f.Enumerator && input.Method == method,
                "Start receiver/enumerator/MethodInfo or reference result changed.");
        }
        f.Control(1, 1); f.Control(0, f.Returned[0], nested: true);
        var beforeMoves = captures[1]; f.Call(0, f.Controller, f.Enumerator, f.Targets[0].MethodInfo);
        Check(captures[1] == beforeMoves + 1, "Synchronous first MoveNext escaped before Start returned.");
        foreach (var kind in new[] { 0, 1 })
            foreach (var mode in new uint[] { 1, 2 })
            {
                f.Control(0, f.Returned[0]); f.NativeMode(mode, false); var beforeAfter = afters[kind]; var beforeRelease = releases[kind];
                var result = f.Call(kind, kind == 0 ? f.Controller : f.Enumerator, f.Enumerator, f.Targets[kind].MethodInfo);
                Check(result.Status == 2 && result.ExceptionKind == mode && result.OriginalKind == 0x4a43 && result.Payload == 0xBADC0FFEE0DDF00DUL,
                    "Coroutine original exception code/kind/payload changed.");
                Check(afters[kind] == beforeAfter && releases[kind] == beforeRelease + 1 && f.Bindings[kind]!.ReadActivity() is { Known: true, InFlight: 0 },
                    "Coroutine abnormal original fabricated After or lost cleanup/drain.");
            }
        f.NativeMode(0, true); var worker = Task.Run(() => f.Call(0, f.Controller, f.Enumerator, f.Targets[0].MethodInfo, worker: true));
        try {
            Check(SpinWait.SpinUntil(() => f.Read(0) is var input && input.Entered > input.Exited, 5000), "Native coroutine worker did not enter.");
            Check(f.Bindings[0]!.ReadActivity() is { Known: true, InFlight: 1 }, "Native Start worker looked drained.");
            f.Bindings[0]!.Dispose();
            Check(f.Bindings[0]!.ReadActivity().InFlight == 1, "Logical detach ended the physical native window.");
        } finally { f.Resume(); Check(worker.Wait(5000), "Native coroutine worker did not join."); }
        Check(worker.Result.Status == 1 && f.Bindings[0]!.ReadActivity() is { Known: true, InFlight: 0 } && f.Faults == 1,
            "Worker did not preserve original/pass-through fault notification.");
        f.NativeMode(0, false); var entered = captures[0]; f.Call(0, f.Controller, f.Enumerator, f.Targets[0].MethodInfo);
        Check(captures[0] == entered && captures.SequenceEqual(releases), "Detached Start delivered or contexts leaked.");
        f.Bindings[1]!.Dispose();
    }
    private static void RunFactory()
    {
        var f = new Fixture(); Owners.Add(f); f.NativeMode(0, false); f.Named(false);
        var published = false; var factoryAfters = 0; var factoryReleases = 0; var moveCalls = 0; var faults = 0;
        var bridge = new NativeCoroutineContextBridge(f.Helper, NativeCoroutineKind.Factory, f.Log);
        using var factory = f.Broker.BindNativeCoroutineObservation(f.Targets[2], NativeCoroutineKind.Factory, () => bridge,
            frame => { Check(frame.IsValid && frame.Self == f.Controller && frame.Enumerator == IntPtr.Zero && frame.Caller == f.ExpectedCallers[2], "Factory frame/caller changed.");
                return new Context(() => { factoryReleases++; Check(bridge.ReadActivity().InFlight >= 1, "Factory cleanup escaped native activity."); }); },
            (_, _, result) => { factoryAfters++; Check(result == f.Returned[2], "Factory reference return was truncated or canonicalized."); published = result == unchecked((nuint)f.Enumerator.ToInt64()); },
            (_, _) => Interlocked.Increment(ref faults));
        foreach (var value in new[] { (nuint)0, unchecked((nuint)0x23456789bcdef010UL), unchecked((nuint)0x76543210abcdef00UL) })
        {
            f.Control(2, value); Check(unchecked((nuint)f.Call(2, f.Controller, IntPtr.Zero, f.Targets[2].MethodInfo).Reference.ToInt64()) == value, "Factory original ref0 return differs.");
        }
        f.Control(2, unchecked((nuint)f.Enumerator.ToInt64())); f.Control(0, unchecked((nuint)0x76543210abcdef00UL)); f.Control(1, 1);
        using var start = f.Broker.BindNativeCoroutineObservation<Context>(f.Targets[0], NativeCoroutineKind.Start,
            () => throw new Exception("Named Start must share the existing native target"), _ => null, (_, _, _) => { }, (_, _) => { }, f.Alias);
        using var move = f.Broker.BindNativeCoroutineObservation(f.Targets[1], NativeCoroutineKind.MoveNext,
            () => throw new Exception("First Move must share the existing native target"), frame => {
                Check(published && frame.Self == f.Enumerator, "Named Start advanced its actual enum before Factory after published it."); moveCalls++; return new Context(() => { });
            }, (_, _, _) => { }, (_, _) => { });
        published = false; f.Named(true); f.Call(0, f.Controller, new(0x1111), f.Targets[0].MethodInfo); f.Named(false);
        Check(published && moveCalls == 1 && factoryAfters == 4 && factoryReleases == 4, "Named Start/Factory/first Move ordering differs.");
        foreach (var mode in new uint[] { 1, 2 })
        {
            f.NativeMode(mode, false); var before = factoryAfters; var beforeRelease = factoryReleases; var result = f.Call(2, f.Controller, IntPtr.Zero, f.Targets[2].MethodInfo);
            Check(result.Status == 2 && result.ExceptionKind == mode && result.Payload == 0xBADC0FFEE0DDF00DUL && factoryAfters == before
                && factoryReleases == beforeRelease + 1 && factory.ReadActivity() is { Known: true, InFlight: 0 }, "Factory native original unwind changed or fabricated success.");
        }
        f.NativeMode(0, true); var worker = Task.Run(() => f.Call(2, f.Controller, IntPtr.Zero, f.Targets[2].MethodInfo, worker: true));
        try { Check(SpinWait.SpinUntil(() => f.Read(2) is var input && input.Entered > input.Exited, 5000), "Factory worker did not enter.");
            Check(factory.ReadActivity() is { Known: true, InFlight: 1 }, "Factory worker appeared drained."); factory.Dispose(); Check(factory.ReadActivity().InFlight == 1, "Factory detach ended original window."); }
        finally { f.Resume(); Check(worker.Wait(5000), "Factory worker did not join."); }
        Check(worker.Result.Status == 1 && faults == 1 && factory.ReadActivity() is { Known: true, InFlight: 0 }, "Factory worker forwarding/fault/drain differs."); f.NativeMode(0, false);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Result
    { internal uint Size, Status, ExceptionKind, ExceptionCode; internal ulong Payload; internal IntPtr Reference; internal uint Thread, OriginalKind; }
    [StructLayout(LayoutKind.Sequential)] private struct Observation
    { internal uint Size, Kind; internal ulong Entered, Exited; internal IntPtr Self, Argument, Method; internal ulong Returned; internal uint Scalar, Thread; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr TargetNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ControlNative(uint kind, IntPtr result, IntPtr nested, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ModeNative(uint mode, uint block);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ResumeNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(uint kind, IntPtr entry, IntPtr self, IntPtr argument, IntPtr method, ref Result result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint kind, ref Observation result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NamedNative(IntPtr factory, IntPtr factoryMethod, IntPtr move, IntPtr moveMethod);
    private sealed class Fixture
    {
        private readonly IntPtr _library; private readonly int _imageSize; private readonly MainThreadDispatcher _main = new();
        private readonly ControlNative _control; private readonly ModeNative _mode; internal readonly ResumeNative Resume;
        private readonly InvokeNative _invoke, _worker; private readonly ReadNative _read;
        private readonly NamedNative _named;
        internal readonly Logger Log = new(); internal readonly ExactHookBroker Broker; internal readonly string Helper;
        internal readonly VerifiedHookTarget[] Targets; internal readonly VerifiedHookTarget Alias;
        internal readonly IntPtr[] ExpectedCallers;
        internal readonly ExactHookBroker.Binding?[] Bindings = new ExactHookBroker.Binding?[3];
        internal readonly nuint[] Returned = new nuint[3]; internal int Faults;
        internal readonly IntPtr Controller = new(unchecked((long)0x12345678abcdef00UL)), Enumerator = new(unchecked((long)0x23456789bcdef010UL));
        internal Fixture()
        {
            string Env(string key) => Environment.GetEnvironmentVariable(key) ?? throw new InvalidOperationException("Missing native fixture environment: " + key);
            var path = Env("RIZ_INITIALIZATION_ABI_FIXTURE"); Helper = Env("RIZ_INITIALIZATION_CONTEXT_HELPER"); _library = NativeLibrary.Load(path);
            using (var stream = File.OpenRead(path)) using (var pe = new PEReader(stream)) _imageSize = pe.PEHeaders.PEHeader!.SizeOfImage;
            _control = Export<ControlNative>("RtCoroutineControl"); _mode = Export<ModeNative>("RtJudgeActivityControl"); Resume = Export<ResumeNative>("RtJudgeActivityResume");
            _invoke = Export<InvokeNative>("RtCoroutineInvoke"); _worker = Export<InvokeNative>("RtCoroutineInvokeWorker"); _read = Export<ReadNative>("RtCoroutineRead"); _named = Export<NamedNative>("RtCoroutineNamedControl");
            var target = Export<TargetNative>("RtCoroutineTarget");
            var caller = Export<TargetNative>("RtCoroutineExpectedCaller"); ExpectedCallers = new[] { caller(0), caller(1), caller(2) };
            Check(ExpectedCallers[0] != ExpectedCallers[1] && ExpectedCallers.All(IsNativeCaller), "Native caller labels are not distinct fixture image addresses.");
            Targets = new[] { new VerifiedHookTarget("coroutine.native.start", target(0), new(0x123), new(0x321), ExactHookAbi.CoroutineStartReference1, new(0x789)),
                new VerifiedHookTarget("coroutine.native.move", target(1), new(0x456), new(0x654), ExactHookAbi.InstanceBooleanReturn0),
                new VerifiedHookTarget("coroutine.native.factory", target(2), new(0x987), new(0x321), ExactHookAbi.CoroutineFactoryReference0, new(0x888)) };
            Alias = Targets[0] with { Id = "coroutine.native.alias", MethodInfo = new(0x124) };
            Broker = new(Env("RIZ_INITIALIZATION_ABI_DOBBY"), _main, Log);
            Check(Marshal.SizeOf<Result>() == 40 && Marshal.SizeOf<Observation>() == 64 && Marshal.SizeOf<NativeCoroutineFrame>() == 48
                && Marshal.OffsetOf<NativeCoroutineFrame>(nameof(NativeCoroutineFrame.Caller)).ToInt32() == 40, "Coroutine fixture ABI layout changed.");
        }
        private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
        internal bool IsNativeCaller(IntPtr caller) => caller.ToInt64() >= _library.ToInt64() && caller.ToInt64() < _library.ToInt64() + _imageSize;
        internal void Control(int kind, nuint value, bool nested = false)
        { Check(_control((uint)kind, unchecked((IntPtr)(long)value), nested ? Targets[1].Address : IntPtr.Zero, Targets[1].MethodInfo) == 1, "Native coroutine control rejected."); Returned[kind] = value; }
        internal void NativeMode(uint mode, bool block) => Check(_mode(mode, block ? 1u : 0u) == 1, "Native coroutine block/mode rejected.");
        internal void Named(bool active) => Check(_named(active ? Targets[2].Address : IntPtr.Zero, Targets[2].MethodInfo,
            active ? Targets[1].Address : IntPtr.Zero, Targets[1].MethodInfo) == 1, "Named coroutine control rejected.");
        internal Observation Read(int kind) { var value = new Observation { Size = 64 }; Check(_read((uint)kind, ref value) == 1, "Native coroutine read failed."); return value; }
        internal Result Call(int kind, IntPtr self, IntPtr argument, IntPtr method, bool worker = false)
        { var result = new Result { Size = 40 }; var status = (worker ? _worker : _invoke)((uint)kind, Targets[kind].Address, self, argument, method, ref result); Check(status == result.Status && status is 1 or 2, "Native coroutine caller failed."); return result; }
    }
    internal sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
}
