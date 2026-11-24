using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>Real Dobby parent and native mid -> native immutable descriptor ->
/// adopted CLR callbacks. This owns only an offline fixture, not a game patch.</summary>
internal static class NativeBootstrappedVoidRegression
{
    internal const string CaseName = "Native bootstrap adopted bridge preserves pre-attach physical calls and exact late-attached native mid callbacks";
    internal static readonly (string Name, Action Run)[] Cases = { (CaseName, Run) };
    private static readonly List<object> Owners = new();
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static T Export<T>(IntPtr library, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    [StructLayout(LayoutKind.Sequential)] private struct Config
    { internal uint Version, Size; internal IntPtr Target, Original, ExpectedMid; }
    [StructLayout(LayoutKind.Sequential)] private struct Info
    { internal uint Version, Size, Slot, Attached; internal IntPtr Target, Original, Entry; internal ulong Epoch; internal uint Unknown, Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Observation
    { internal uint Size, Kind; internal ulong Entered, Exited; internal IntPtr Self, Argument, Method; internal ulong Returned; internal uint Scalar, Thread; }
    [StructLayout(LayoutKind.Sequential)] private struct Result
    { internal uint Size, Status, ExceptionKind, ExceptionCode; internal ulong Payload; internal IntPtr Reference; internal uint Thread, OriginalKind; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr TargetNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint slot, ref Config config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint slot, ref Info info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint MidPatchNative(uint kind, IntPtr midEntry, IntPtr list);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ControlNative(uint kind, uint mode, uint block, IntPtr nested, IntPtr nestedSelf, IntPtr nestedMethod);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint kind, ref Observation value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(uint kind, IntPtr entry, IntPtr self, IntPtr method, ref Result value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ResumeNative(uint kind);
    private static void Run()
    {
        string Env(string key) => Environment.GetEnvironmentVariable(key) ?? throw new InvalidOperationException("Missing native bootstrap environment: " + key);
        var path = Env("RIZ_INITIALIZATION_CONTEXT_HELPER");
        var helper = NativeLibrary.Load(path); var fixture = NativeLibrary.Load(Env("RIZ_INITIALIZATION_ABI_FIXTURE"));
        var engine = new DobbyDetourEngine(Env("RIZ_INITIALIZATION_ABI_DOBBY")); Owners.Add(engine);
        const uint slot = 30, kind = 4;
        var target = Export<TargetNative>(fixture, "RtVoidTarget")(kind);
        var caller = Export<TargetNative>(fixture, "RtVoidExpectedCaller")(kind);
        var midPc = Export<TargetNative>(fixture, "RtEventMidExpectedReturn")(kind);
        var self = new IntPtr(unchecked((long)0x12345678ABCDEF00UL));
        var method = new IntPtr(unchecked((long)0x23456789BCDEF020UL));
        var list = new IntPtr(unchecked((long)0x3456789ACDEF1040UL));
        var verified = new VerifiedHookTarget("bootstrap.native.fixture", target, method, new(0x2468), ExactHookAbi.NativeVoid0);
        // Only this fixture's never-invoked kind4 is patched. Production startup
        // and its first-publication contract are deliberately not exercised here.
        var original = engine.Prepare(verified, Export<TargetNative>(helper, "RizVoidContext_GetEntry")(slot));
        var config = new Config { Version = 1, Size = 32, Target = target, Original = original, ExpectedMid = midPc };
        Check(Export<ConfigureNative>(helper, "RizVoidBootstrap_Configure")(slot, ref config) == 0, "Bootstrap native slot configuration failed.");
        var info = new Info { Version = 1, Size = 56 };
        Check(Export<InfoNative>(helper, "RizVoidBootstrap_GetInfo")(slot, ref info) == 0 && info.Slot == slot && info.Attached == 0
            && info.Target == target && info.Original == original && info.Entry != IntPtr.Zero && info.Epoch != 0 && info.Unknown == 0 && info.Reserved == 0,
            "Native bootstrap returned a mismatched actual slot identity.");
        Check(Export<MidPatchNative>(fixture, "RtEventMidConfigure")(kind, Export<EntryNative>(helper, "RizEventMidContext_GetEntry")(), list) == 1,
            "Actual fixture mid CALL was not prepared before its first invocation.");
        engine.Commit(verified);
        var control = Export<ControlNative>(fixture, "RtVoidControl"); var read = Export<ReadNative>(fixture, "RtVoidRead");
        var invoke = Export<InvokeNative>(fixture, "RtVoidInvoke"); var workerInvoke = Export<InvokeNative>(fixture, "RtVoidInvokeWorker");
        var resume = Export<ResumeNative>(fixture, "RtVoidResume");
        Observation Read() { var value = new Observation { Size = 64 }; Check(read(kind, ref value) == 1, "Bootstrap original read failed."); return value; }
        void Control(uint mode = 0, bool block = false) => Check(control(kind, mode, block ? 1u : 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 1, "Bootstrap fixture control failed.");
        Result Call(bool worker = false)
        { var value = new Result { Size = 40 }; Check((worker ? workerInvoke : invoke)(kind, target, self, method, ref value) == value.Status, "Bootstrap native catch failed."); return value; }
        Control(); Check(Call().Status == 1 && Read().Entered == 1, "Native pre-attach pass-through failed.");
        var logger = new Logger();
        var bridge = NativeBootstrappedVoidBridge.AdoptBootstrap(path, slot, target, original, info.Epoch, logger); Owners.Add(bridge);
        var enters = 0; var exits = 0; var mids = 0; var abnormal = 0; var observerFailure = false;
        Exception? callbackFailure = null;
        void AssertCallback(Action action) { try { action(); } catch (Exception error) { Interlocked.CompareExchange(ref callbackFailure, error, null); throw; } }
        bridge.ConfigureMid(frame => AssertCallback(() => {
            Check(frame.IsValid && frame.ReturnPc == midPc && frame.Event == self && frame.Executing == list,
                "Adopted bootstrap lost the actual mid return, event or executing-list registers."); Interlocked.Increment(ref mids);
        }));
        Control(block: true); var pending = Task.Run(() => Call(true));
        try {
            Check(SpinWait.SpinUntil(() => Read().Entered > Read().Exited, 5000), "Pre-attach native worker did not reach its post-mid blocked original.");
            bridge.Configure(original, frame => {
                AssertCallback(() => Check(frame.IsValid && frame.Self == self && frame.Method == method && frame.Caller == caller,
                    "Bootstrap callback frame or real caller changed."));
                Interlocked.Increment(ref enters); return unchecked((nuint)0xFEDCBA9876543210UL);
            }, (frame, token, flags) => AssertCallback(() => {
                Check(frame.Caller == caller && token == unchecked((nuint)0xFEDCBA9876543210UL), "Bootstrap native token did not pair exact CLR callbacks.");
                Interlocked.Increment(ref exits); if ((flags & NativeVoidExitFlags.Abnormal) != 0) Interlocked.Increment(ref abnormal);
                if (observerFailure) throw new InvalidOperationException("Bootstrap controlled CLR exit fault.");
            }));
            Check(bridge.ReadActivity() is { Known: true, InFlight: 1 } && enters == 0 && exits == 0 && mids == 0,
                "Late attach manufactured callbacks for an already-entered native invocation.");
        }
        finally { resume(kind); Check(pending.Wait(5000), "Pre-attach worker failed to continue."); }
        Check(pending.Result.Status == 1 && enters == 0 && exits == 0 && mids == 0 && bridge.ReadActivity() is { Known: true, InFlight: 0 },
            "Old invocation Exit borrowed a new callback descriptor after attach.");
        for (uint mode = 0; mode <= 2; mode++) {
            Control(mode); var result = Call();
            Check(result.Status == (mode == 0 ? 1u : 2u) && result.ExceptionKind == mode
                && (mode == 0 || (result.ExceptionCode == (mode == 1 ? 0xE04F494Eu : 0xE06D7363u)
                    && result.Payload == (0xBADC0FFEE0DDF00DUL ^ kind) && result.OriginalKind == 0x5600 + kind)),
                "Adopted native parent changed its original result or SEH/C++ payload.");
        }
        Check(callbackFailure is null && enters == 3 && mids == 3 && exits == 3 && abnormal == 2
            && bridge.ReadActivity() is { Known: true, InFlight: 0 }, "Attached callbacks or native exception cleanup failed.");
        var attempt = bridge.TryAcquireAdmission(1000);
        Check(attempt.Status == NativeVoidAdmissionAttemptStatus.Acquired && attempt.Lease is not null, "Adopted native slot admission failed.");
        using (var lease = attempt.Lease!) Check(lease.VerifyClosed() && lease.ReleaseUnmodified(), "Adopted native slot receipt failed verification/release.");
        Control(); observerFailure = true; Check(Call().Status == 1, "CLR exit failure suppressed the native original.");
        Check(enters == 4 && mids == 4 && exits == 4 && bridge.ReadActivity() is { Known: false, InFlight: 0 }
            && Read().Entered == 6 && Read().Exited == 6 && Read().Returned == 6,
            "Adopted observer fault did not preserve exact original count and sticky unknown.");
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
}
