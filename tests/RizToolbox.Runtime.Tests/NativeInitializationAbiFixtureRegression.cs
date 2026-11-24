using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>Actual Windows x64 C++ caller -> Dobby patched production replacement ->
/// Dobby trampoline -> C++ original. Reflection constructs only the sealed test
/// target authority; no managed original, substitute transport or game is used.
/// Native libraries, patched targets and reverse-P/Invoke delegates stay retained.
/// </summary>
internal static class NativeInitializationAbiFixtureRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("sealed initialization real Windows x64 native ABI, Dobby, worker, recursion and null abort", () => Run(
            Environment.GetEnvironmentVariable("RIZ_INITIALIZATION_ABI_FIXTURE")
                ?? throw new InvalidOperationException("Build the native initialization fixture and set RIZ_INITIALIZATION_ABI_FIXTURE."),
            Environment.GetEnvironmentVariable("RIZ_INITIALIZATION_ABI_DOBBY")
                ?? throw new InvalidOperationException("Set RIZ_INITIALIZATION_ABI_DOBBY to the inspected Dobby DLL.")))
    };
    private static readonly List<Fixture> ProcessOwners = new();
    private enum Kind : uint { Start, Getter, Play, Tooltip, Progress, Mode, Instantaneous, EndRange }
    private static readonly IntPtr Receiver = new(unchecked((long)0x12345678ABCDEF00UL));
    private static readonly IntPtr Information = new(unchecked((long)0x23456789BCDEF010UL));
    private static readonly IntPtr Parameters = new(unchecked((long)0x3456789ACDEF0120UL));
    private static readonly IntPtr Controller = new(unchecked((long)0x456789ABDEF01230UL));
    private static readonly IntPtr LocalProgress = new(unchecked((long)0x56789ABCEF012340UL));
    private static readonly IntPtr Ordinary = new(unchecked((long)0x6789ABCDF0123450UL));
    private static readonly ExactHookBroker.LocalInitializationIdentity Local = new("native-abi-run", 17, Controller.ToInt64());
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal static void Run(string fixturePath, string dobbyPath)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("This check requires actual Windows x64 native calls.");
        using (var stream = File.OpenRead(dobbyPath))
            Check(Convert.ToHexString(SHA256.HashData(stream)).Equals(
                "8015DE7D867245A1095D13947A63763878E4CF5FD3D3089B63CC39200B055DED", StringComparison.Ordinal),
                "The actual Dobby fixture must use the inspected DLL.");
        var fixture = new Fixture(Path.GetFullPath(fixturePath), Path.GetFullPath(dobbyPath));
        ProcessOwners.Add(fixture);
        try
        {
            OrdinaryAbi(fixture);
            NativeRecursionAndWorkers(fixture);
            SuccessfulLocalStart(fixture);
            IndependentLocalPlay(fixture);
            NativeOriginalExceptions(fixture);
            FailedLocalGetters(fixture);
            DetachDuringNativeStart(fixture);
            Check(fixture.Engine.Installs.Count == 8 && fixture.Engine.Installs.Values.All(count => count == 1),
                "The existing Mode authority was patched a second time or one exact ABI was omitted.");
        }
        finally { fixture.StopObserving(); }
    }
    private static void OrdinaryAbi(Fixture f)
    {
        f.Reset();
        foreach (var method in new[] { f.Method(Kind.Start), IntPtr.Zero, new IntPtr(unchecked((long)0x789ABCDEF0123450UL)) })
        {
            var before = f.Read(Kind.Start).Entered;
            f.Call(Kind.Start, Receiver, method: method);
            var native = f.Read(Kind.Start);
            Check(native.Self == Receiver && native.Method == method && native.Entered == before + 1 && native.Exited == native.Entered,
                "Start changed a full-width receiver/MethodInfo or called native original more than once.");
        }
        foreach (var returned in new[] { Parameters, IntPtr.Zero })
        {
            f.SetReference(returned);
            var before = f.Read(Kind.Getter).Entered;
            Check(f.Call(Kind.Getter, Ordinary, Information).Reference == returned, "Ordinary getter changed its native reference return.");
            var native = f.Read(Kind.Getter);
            Check(native.Self == Ordinary && native.Argument == Information && native.Method == f.Method(Kind.Getter)
                && native.Entered == before + 1 && native.Returned == unchecked((ulong)returned.ToInt64()), "Getter argument/native original count differs.");
        }
        foreach (var play in new uint[] { 0, 1 })
        {
            var before = f.Read(Kind.Play).Entered;
            var rawEdx = 0xA5B6C700U | play;
            f.Call(Kind.Play, Ordinary, scalar: rawEdx);
            var native = f.Read(Kind.Play);
            Check(f.PlayInputBits() == rawEdx && native.Self == Ordinary && native.Scalar == play && native.Method == f.Method(Kind.Play) && native.Entered == before + 1,
                "The native caller truncated EDX poison or the production transport read beyond the one-byte Play Boolean.");
        }
        foreach (var bits in new uint[] { 0, 0x80000000, 0xBF400000, 0x7FC01234, 0xFFC12345, 0x7FA01234 })
        foreach (var text in new[] { Receiver, IntPtr.Zero })
        {
            var before = f.Read(Kind.Tooltip).Entered;
            f.Call(Kind.Tooltip, text, scalar: bits);
            var native = f.Read(Kind.Tooltip);
            Check(native.Self == text && native.Scalar == bits && native.Method == f.Method(Kind.Tooltip) && native.Entered == before + 1,
                "Tooltip changed RCX string, XMM1 float bits or R8 MethodInfo.");
        }
        f.SetReference(Parameters);
        Check(f.Initialization.Read() is { InFlight: 0, UnknownInFlight: 0, Known: true }, "Ordinary ABI entries did not drain.");
    }
    private static void NativeRecursionAndWorkers(Fixture f)
    {
        foreach (var kind in new[] { Kind.Start, Kind.Getter, Kind.Play, Kind.Tooltip })
        {
            f.Reset(); var depth = 0; var sawRecursiveInFlight = false;
            f.Observe = entered => {
                if (entered != kind) return;
                Check(f.Initialization.Read().InFlight >= 1, "Native original was outside the production entry count.");
                if (++depth == 1) f.Call(kind, Ordinary, Information, scalar: kind == Kind.Play ? 1U : 0xFFC12345U);
                else sawRecursiveInFlight = f.Initialization.Read().InFlight >= 2;
                --depth;
            };
            f.Call(kind, Ordinary, Information, scalar: kind == Kind.Play ? 0U : 0x80000000U);
            Check(sawRecursiveInFlight && f.Read(kind).Entered == 2 && f.Read(kind).Exited == 2 && f.Initialization.Read().InFlight == 0,
                "A native recursive ABI entry skipped/doubled original or escaped complete entry/exit accounting.");
            f.Observe = entered => {
                if (entered == kind) Check(!f.Main.IsMainThread && f.Initialization.Read().InFlight != 0,
                    "CreateThread ordinary original was misclassified as main-thread or drained early.");
            };
            var worker = f.Call(kind, Ordinary, Information, scalar: kind == Kind.Play ? 1U : 0xBF400000U, worker: true);
            var native = f.Read(kind);
            Check(worker.ThreadId != f.MainNativeThread && native.ThreadId == worker.ThreadId && native.Entered == 3 && native.Exited == 3
                && native.Self == Ordinary && native.Method == f.Method(kind) && f.Initialization.Read().InFlight == 0,
                "The native CreateThread call did not preserve the ordinary ABI/once guarantee.");
        }
    }
    private static void SuccessfulLocalStart(Fixture f)
    {
        f.Reset(); var before = 0; var after = 0; var depth = 0;
        bool GetterWindow() => ExactHookBroker.HasCurrentNativeGetter(Local.SessionId, Local.RunGeneration, Controller.ToInt64(), Information.ToInt64());
        Check(!GetterWindow(), "A native getter provenance window leaked from an earlier call.");
        f.Before = args => {
            Check(!GetterWindow(), "Getter provenance was granted inside its before validation.");
            ++before;
            Check(args.Identity == Local && args.StartReceiver == Receiver && args.StartMethod == f.Method(Kind.Start)
                && args.Controller == Controller && args.Information == Information && args.GetterMethod == f.Method(Kind.Getter)
                && f.Initialization.Read().InFlight >= 2, "The real getter callback lost the native Start parent or raw arguments.");
            return true;
        };
        f.After = (_, value) => { ++after; Check(!GetterWindow(), "Getter provenance survived into successful after validation."); Check(value == Parameters && f.Initialization.Read().InFlight >= 2, "Getter native result/return-phase count differs."); return true; };
        f.Observe = kind => {
            if (kind == Kind.Getter) Check(GetterWindow() && f.Initialization.Read().InFlight >= 2, "The exact native getter original lost its provenance or appeared drained while native Start was live.");
            if (kind != Kind.Start) return;
            Check(!GetterWindow(), "Start inherited getter original provenance.");
            ++depth;
            if (depth == 1)
            {
                Check(f.Call(Kind.Getter, Controller, Information).Reference == Parameters, "Validated native getter reference was not preserved.");
                // Actual native recursive entries exercise both the inherited exact
                // parent and an ordinary child that masks the outer permission.
                f.Call(Kind.Start, Receiver); f.Call(Kind.Start, Ordinary);
            }
            f.Tail(); --depth;
        };
        f.Call(Kind.Start, Receiver);
        Check(!GetterWindow(), "Native getter provenance survived its complete physical exit.");
        Check(before == 1 && after == 1 && f.Read(Kind.Getter).Entered == 1 && f.Read(Kind.Start).Entered == 3
            && f.Read(Kind.Mode).Entered == 1 && f.Read(Kind.Tooltip).Entered == 1
            && f.Initialization.Read() is { InFlight: 0, UnknownInFlight: 0, Known: true },
            "LOCAL parent protection, native recursion masking, original once or final drain differs.");
    }
    private static void IndependentLocalPlay(Fixture f)
    {
        f.Reset(); var depth = 0;
        f.Observe = kind => {
            if (kind != Kind.Play) return;
            ++depth;
            if (depth == 1) f.Call(Kind.Play, Ordinary, scalar: 0);
            f.Tail(); --depth;
        };
        foreach (var play in new uint[] { 0, 1 })
        {
            f.Call(Kind.Play, LocalProgress, scalar: play);
            Check(f.Read(Kind.Play).Scalar == 0, "The native ordinary child lost its false Boolean.");
        }
        // No Start frame or terminal marker exists. Play protects Mode only.
        Check(f.Read(Kind.Play).Entered == 4 && f.Read(Kind.Mode).Entered == 2 && f.Read(Kind.Tooltip).Entered == 4,
            "Independent LOCAL Play did not isolate Mode or incorrectly suppressed Tooltip/ordinary nested Play.");
        f.Observe = kind => { if (kind == Kind.Play) f.Tail(); };
        f.Call(Kind.Play, LocalProgress, scalar: 1, worker: true);
        Check(f.Read(Kind.Play) is { Entered: 5, Scalar: 1 } && f.Read(Kind.Mode).Entered == 2 && f.Read(Kind.Tooltip).Entered == 5
            && f.Initialization.Read().InFlight == 0, "Native worker LOCAL Play lost true/MethodInfo or its exact Mode scope.");
    }
    private static void FailedLocalGetters(Fixture f)
    {
        // Failure before original, failure after the actual native original and
        // a worker rejection all return native null. Fault itself may also throw;
        // the enclosing C++ __try still must see no CLR/SEH exception.
        for (var variant = 0; variant != 4; ++variant)
        {
            f.Reset(); var before = 0; var after = 0; var faults = 0; var mode = variant;
            f.Before = _ => { ++before; if (mode == 0) throw new IOException("before original"); return true; };
            f.After = (_, _) => { ++after; if (mode == 1) throw new IOException("after native original"); return mode != 2; };
            f.Fault = (_, _) => { ++faults; throw new IOException("fault observer also fails inside protected transport"); };
            f.Observe = kind => {
                if (kind != Kind.Start) return;
                var getter = f.Call(Kind.Getter, Controller, Information);
                Check(getter.Reference == IntPtr.Zero && getter.ExceptionCode == 0 && f.Initialization.Read().InFlight >= 1,
                    "A failed claimed getter escaped native null or ended its Start parent early.");
                f.Tail();
            };
            var result = f.Call(Kind.Start, Receiver, worker: mode == 3);
            Check(result.Status == 1 && result.ExceptionCode == 0 && faults == 1 && f.Read(Kind.Mode).Entered == 0 && f.Read(Kind.Tooltip).Entered == 0
                && f.Read(Kind.Getter).Entered == (mode is 1 or 2 ? 1UL : 0UL)
                && before == (mode == 3 ? 0 : 1) && after == (mode is 1 or 2 ? 1 : 0)
                && f.Initialization.Read() is { InFlight: 0, UnknownInFlight: 0, Known: false },
                "A native failed/worker getter ran original twice, crossed an unmanaged boundary or released its provisional tail claim.");
        }
    }
    private static void NativeOriginalExceptions(Fixture f)
    {
        // Each throw starts after the safe observer has returned. The C++ caller
        // catches it before returning through P/Invoke: no native exception uses
        // a CLR frame as its unwind transport, including the nested getter call.
        foreach (var kind in new[] { Kind.Start, Kind.Getter })
        foreach (var mode in new uint[] { 1, 2 })
        {
            f.Reset(); var before = 0; var after = 0; var faults = 0;
            f.Before = _ => { ++before; return true; };
            f.After = (_, _) => { ++after; return true; };
            f.Fault = (_, _) => ++faults;
            f.SetFault(kind, mode);
            CheckCaughtOriginal(f.CallCatch(kind, Ordinary, Information), kind, mode, f.MainNativeThread);
            Check(f.Read(kind) is { Entered: 1, Exited: 1 } && before == 0 && after == 0 && faults == 0
                && f.Initialization.Read() is { InFlight: 0, UnknownInFlight: 0 },
                "Ordinary native original exception was swallowed, relabelled LOCAL or left a live native/TLS scope.");
            f.SetFault(kind, 0); f.Call(kind, Ordinary, Information); f.Tail();
            Check(f.Read(kind) is { Entered: 2, Exited: 2 } && f.Read(Kind.Mode).Entered == 1 && f.Read(Kind.Tooltip).Entered == 1,
                "Ordinary native exception cleanup polluted the following same-thread invocation.");

            f.Reset(); before = 0; after = 0; faults = 0; CatchResult? getterException = null;
            f.Before = _ => { ++before; return true; };
            f.After = (_, value) => { ++after; Check(value == Parameters, "A native return receipt changed its actual reference."); return true; };
            f.Fault = (_, _) => ++faults;
            f.SetFault(kind, mode);
            f.Observe = entered => {
                if (entered != Kind.Start) return;
                if (kind == Kind.Getter)
                {
                    getterException = f.CallCatch(Kind.Getter, Controller, Information);
                    CheckCaughtOriginal(getterException.Value, kind, mode, f.MainNativeThread);
                    Check(after == 0 && f.Initialization.Read().InFlight > 0,
                        "An abnormal native getter fabricated a returned/root callback or dropped its live Start parent.");
                }
                else Check(f.Call(Kind.Getter, Controller, Information).Reference == Parameters,
                    "The LOCAL Start original did not acquire its exact getter claim before throwing.");
                f.Tail();
            };
            if (kind == Kind.Start)
                CheckCaughtOriginal(f.CallCatch(Kind.Start, Receiver), kind, mode, f.MainNativeThread);
            else
            {
                f.Call(Kind.Start, Receiver);
                Check(getterException.HasValue, "The actual native Start never called its original getter fault case.");
            }
            Check(f.Read(Kind.Start) is { Entered: 1, Exited: 1 } && f.Read(Kind.Getter) is { Entered: 1, Exited: 1 }
                && before == 1 && after == (kind == Kind.Start ? 1 : 0) && faults == 1
                && f.Read(Kind.Mode).Entered == 0 && f.Read(Kind.Tooltip).Entered == 0
                && f.Initialization.Read() is { InFlight: 0, UnknownInFlight: 0, Known: false },
                "LOCAL original unwind lost original-once, sticky fault, actual-return callback rules, tail claim or complete drain.");
            f.SetFault(kind, 0); f.Observe = null;
            f.Call(Kind.Start, Ordinary); f.Call(Kind.Getter, Ordinary, Information); f.Tail();
            Check(f.Read(Kind.Start) is { Entered: 2, Exited: 2 } && f.Read(Kind.Getter) is { Entered: 2, Exited: 2 }
                && before == 1 && after == (kind == Kind.Start ? 1 : 0) && faults == 1
                && f.Read(Kind.Mode).Entered == 1 && f.Read(Kind.Tooltip).Entered == 1
                && f.Initialization.Read() is { InFlight: 0, UnknownInFlight: 0, Known: false },
                "A native exception left same-thread TLS permissions/counters or cleared its sticky unknown after cleanup.");
        }
    }
    private static void CheckCaughtOriginal(CatchResult result, Kind kind, uint mode, uint thread)
    {
        Check(result.Status == 2 && result.ExceptionKind == mode && result.OriginalKind == kind
            && result.ExceptionCode == (mode == 1 ? 0xE04F494EU : 0xE06D7363U)
            && result.Payload == (0xBADC0FFEE0DDF00DUL ^ (ulong)kind)
            && result.Reference == IntPtr.Zero && result.ThreadId == thread,
            "The native caller did not receive the original SEH code/arguments or typed C++ payload on the original thread.");
    }
    private static void DetachDuringNativeStart(Fixture f)
    {
        f.Reset();
        f.Observe = kind => {
            if (kind != Kind.Start) return;
            Check(f.Call(Kind.Getter, Controller, Information).Reference == Parameters, "Pre-detach getter did not complete once.");
            f.Initialization.Dispose();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            f.Tail();
        };
        f.Call(Kind.Start, Receiver);
        Check(f.Read(Kind.Getter).Entered == 1 && f.Read(Kind.Mode).Entered == 0 && f.Read(Kind.Tooltip).Entered == 0
            && f.Initialization.Read().InFlight == 0, "Detach/GC dropped an in-flight native parent claim or callback ownership.");
        f.Observe = null; f.Call(Kind.Start, Ordinary); f.Tail();
        Check(f.Read(Kind.Start).Entered == 2 && f.Read(Kind.Mode).Entered == 1 && f.Read(Kind.Tooltip).Entered == 1,
            "Retained native trampoline did not forward ordinary calls after logical detach.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Request
    { internal uint Size; internal Kind Kind; internal IntPtr Entry, Self, Argument, Method; internal uint Scalar, Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Result
    { internal uint Size, Status, ExceptionCode, ThreadId; internal IntPtr Reference; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CatchResult
    { internal uint Size, Status, ExceptionKind, ExceptionCode; internal ulong Payload; internal IntPtr Reference; internal uint ThreadId; internal Kind OriginalKind; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Observation
    { internal uint Size; internal Kind Kind; internal ulong Entered, Exited; internal IntPtr Self, Argument, Method; internal ulong Returned; internal uint Scalar, ThreadId; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetTarget(Kind kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ResetNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadPlayInputNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(Kind kind, ref Observation observation);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Observer(Kind kind, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetObserver(IntPtr observer, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetReferenceNative(IntPtr reference);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(ref Request request, ref Result result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeCatchNative(ref Request request, ref CatchResult result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SetFaultNative(Kind kind, uint mode);
    private sealed class Fixture
    {
        private readonly IntPtr _library;
        private readonly GetTarget _target;
        private readonly ResetNative _reset;
        private readonly ReadPlayInputNative _playInput;
        private readonly ReadNative _read;
        private readonly SetObserver _setObserver;
        private readonly SetReferenceNative _setReference;
        private readonly InvokeNative _invoke, _worker;
        private readonly InvokeCatchNative _invokeCatch;
        private readonly SetFaultNative _setFault;
        private readonly Observer _observer;
        private Exception? _observerFailure;
        internal readonly MainDispatcher Main = new();
        internal readonly CountingDobby Engine;
        internal readonly ExactHookBroker Broker;
        internal readonly ExactHookBroker.LocalProgressIsolationBinding Progress;
        internal readonly ExactHookBroker.LocalInitializationIsolationBinding Initialization;
        internal readonly uint MainNativeThread;
        internal Func<ExactHookBroker.LocalInitializationArguments, bool> Before = _ => true;
        internal Func<ExactHookBroker.LocalInitializationArguments, IntPtr, bool> After = (_, value) => value == Parameters;
        internal Action<ExactHookBroker.LocalInitializationArguments?, string> Fault = (_, _) => { };
        internal Action<Kind>? Observe;
        internal Fixture(string path, string dobby)
        {
            Check(Marshal.SizeOf<Request>() == 48 && Marshal.SizeOf<Result>() == 24 && Marshal.SizeOf<Observation>() == 64,
                "The managed/native fixture record layout differs.");
            Check(Marshal.SizeOf<CatchResult>() == 40, "The native exception receipt layout differs.");
            _library = NativeLibrary.Load(path);
            _target = Export<GetTarget>("RtInitTarget"); _reset = Export<ResetNative>("RtInitReset"); _read = Export<ReadNative>("RtInitRead");
            _playInput = Export<ReadPlayInputNative>("RtInitPlayInputBits");
            _setObserver = Export<SetObserver>("RtInitSetObserver"); _setReference = Export<SetReferenceNative>("RtInitSetReference");
            _invoke = Export<InvokeNative>("RtInitInvoke"); _worker = Export<InvokeNative>("RtInitInvokeWorker");
            _invokeCatch = Export<InvokeCatchNative>("RtInitInvokeCatch"); _setFault = Export<SetFaultNative>("RtInitSetFault");
            var targets = new[] {
                Target(Kind.Start, ExactHookAbi.InstanceVoid), Target(Kind.Getter, ExactHookAbi.LocalInitializationReference1),
                Target(Kind.Play, ExactHookAbi.LocalProgressPlayBooleanVoid), Target(Kind.Tooltip, ExactHookAbi.StaticTimelineTooltipVoid),
                Target(Kind.Progress, ExactHookAbi.InstanceVoid), Target(Kind.Mode, ExactHookAbi.StaticTimelineModeVoid),
                Target(Kind.Instantaneous, ExactHookAbi.StaticTimelineInstantaneousHandle), Target(Kind.EndRange, ExactHookAbi.StaticTimelineEndRangeVoid) };
            Check(targets.Select(target => target.Address).Distinct().Count() == 8, "Native linker aliased distinct fixture targets.");
            var logger = new FixtureLogger();
            Engine = new(dobby); Broker = new(Engine, Main, logger);
            var progressTargets = Construct<ExactHookBroker.LocalProgressIsolationTargets>(targets[4], targets[5], targets[6], targets[7]);
            Progress = Broker.BindLocalProgressIsolation(progressTargets, self => self == LocalProgress
                ? new(Local.SessionId, Local.RunGeneration, Local.ControllerIdentity, self.ToInt64()) : null);
            var initializationTargets = Construct<ExactHookBroker.LocalInitializationIsolationTargets>(targets[0], targets[1], targets[2], targets[5], targets[3]);
            var helper = Environment.GetEnvironmentVariable("RIZ_INITIALIZATION_CONTEXT_HELPER")
                ?? throw new InvalidOperationException("Set RIZ_INITIALIZATION_CONTEXT_HELPER to the exact built native initialization context helper.");
            Check(Path.IsPathFullyQualified(helper) && File.Exists(helper), "The native initialization helper requires an existing absolute path.");
            Initialization = Broker.BindLocalInitializationIsolation(Progress, initializationTargets, self => self == Controller ? Local : null,
                new(args => Before(args), (args, result) => After(args, result), (args, failure) => Fault(args, failure)),
                kind => new NativeInitializationContextBridge(helper, kind, logger));
            _observer = (kind, _) => { try { Observe?.Invoke(kind); } catch (Exception error) { Interlocked.CompareExchange(ref _observerFailure, error, null); } };
            _setObserver(Marshal.GetFunctionPointerForDelegate(_observer), IntPtr.Zero);
            MainNativeThread = Call(Kind.Tooltip, IntPtr.Zero).ThreadId;
            Reset();
        }
        private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
        private VerifiedHookTarget Target(Kind kind, ExactHookAbi abi) => new("native.initialization." + kind, _target(kind), Method(kind), new(0x4567), abi,
            kind == Kind.Getter ? new IntPtr(0x5678) : IntPtr.Zero);
        internal IntPtr Method(Kind kind) => new(unchecked((long)0x789ABCDEF0123400UL) + (long)kind * 16);
        internal void Reset()
        {
            Observe = null; Before = _ => true; After = (_, value) => value == Parameters; Fault = (_, _) => { };
            _observerFailure = null; Check(_reset() == 1, "The native fixture still has a live original."); SetReference(Parameters);
        }
        internal void SetReference(IntPtr reference) => _setReference(reference);
        internal uint PlayInputBits() => _playInput();
        internal void SetFault(Kind kind, uint mode) => Check(_setFault(kind, mode) == 1, "The native original fault configuration was rejected.");
        internal CatchResult CallCatch(Kind kind, IntPtr self, IntPtr argument = default)
        {
            var request = new Request { Size = (uint)Marshal.SizeOf<Request>(), Kind = kind, Entry = _target(kind), Self = self,
                Argument = argument, Method = Method(kind) };
            var result = new CatchResult { Size = (uint)Marshal.SizeOf<CatchResult>() };
            var status = _invokeCatch(ref request, ref result);
            Check(status == result.Status && status is 1 or 2, "The native exception caller reported an unexpected or invalid fault receipt.");
            if (_observerFailure is { } error) throw new InvalidOperationException("An assertion failed inside the safe native original observer.", error);
            return result;
        }
        internal Result Call(Kind kind, IntPtr self, IntPtr argument = default, uint scalar = 0, IntPtr? method = null, bool worker = false)
        {
            var request = new Request { Size = (uint)Marshal.SizeOf<Request>(), Kind = kind, Entry = _target(kind), Self = self,
                Argument = argument, Scalar = scalar, Method = method ?? Method(kind) };
            var result = new Result { Size = (uint)Marshal.SizeOf<Result>() };
            var status = (worker ? _worker : _invoke)(ref request, ref result);
            Check(status == 1 && result.Status == 1 && result.ExceptionCode == 0,
                $"A CLR/native exception crossed the actual unmanaged {kind} caller (SEH 0x{result.ExceptionCode:X8}).");
            if (_observerFailure is { } error) throw new InvalidOperationException("An assertion failed inside the safe native original observer.", error);
            return result;
        }
        internal Observation Read(Kind kind)
        {
            var record = new Observation { Size = (uint)Marshal.SizeOf<Observation>() };
            Check(_read(kind, ref record) == 1, "Native observation export rejected its exact record size."); return record;
        }
        internal void Tail()
        { Call(Kind.Mode, IntPtr.Zero, scalar: 0x80000000); Call(Kind.Tooltip, Receiver, scalar: 0xFFC12345); }
        internal void StopObserving() { Observe = null; _setObserver(IntPtr.Zero, IntPtr.Zero); GC.KeepAlive(_observer); }
    }
    private static T Construct<T>(params VerifiedHookTarget[] targets) => (T)typeof(T).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
        .Single(constructor => constructor.GetParameters().Length == targets.Length).Invoke(targets.Cast<object>().ToArray());
    private sealed class CountingDobby : INativeDetourEngine, IPreparedNativeDetourEngine
    {
        private readonly DobbyDetourEngine _real;
        internal readonly Dictionary<IntPtr, int> Installs = new();
        internal CountingDobby(string path) => _real = new(path);
        public Delegate Install(VerifiedHookTarget target, Delegate replacement)
        { Installs.TryGetValue(target.Address, out var count); Installs[target.Address] = count + 1; return _real.Install(target, replacement); }
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement)
        { Installs.TryGetValue(target.Address, out var count); Installs[target.Address] = count + 1; return _real.Prepare(target, replacement); }
        public void Commit(VerifiedHookTarget target) => _real.Commit(target);
    }
    private sealed class MainDispatcher : IMainThreadDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public bool IsMainThread => Environment.CurrentManagedThreadId == _thread;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Not the fixture main thread."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        { VerifyAccess(); cancellationToken.ThrowIfCancellationRequested(); action(); return Task.CompletedTask; }
    }
    private sealed class FixtureLogger : ILogger
    { public void Log(LogLevel level, string message, Exception? exception = null) { } }
}
