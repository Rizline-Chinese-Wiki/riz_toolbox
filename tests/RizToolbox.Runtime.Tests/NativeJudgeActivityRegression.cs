using System.Runtime.InteropServices;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>Real Windows x64 native caller, Dobby patch, production native Judge
/// bridge, C++ original and native SEH/C++ catches. No game image is executed.</summary>
internal static class NativeJudgeActivityRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("native Judge activity covers real Dobby worker, detach, context cleanup and original SEH C++ exits", Run)
    };
    private static readonly List<Fixture> Owners = new();
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Run()
    {
        var f = new Fixture(); Owners.Add(f);
        Check(!f.Bridge.ReadActivity().Known, "Unconfigured helper fabricated a drain.");
        var releases = 0;
        f.Binding = f.Broker.BindNativeJudgeContext(f.Target, () => f.Bridge,
            _ => new Context(() => { Check(f.Binding!.ReadActivity().InFlight == 1, "Native activity ended before context release."); releases++; }),
            (_, _) => Check(f.Binding!.ReadActivity().InFlight == 1, "Native activity ended before after callback."));
        foreach (var mode in new uint[] { 0, 1, 2 })
        {
            f.Control(mode, false); var result = f.Call(false);
            Check(result.Status == (mode == 0 ? 1u : 2u), "Native original normal/abnormal status changed.");
            if (mode != 0) Check(result.ExceptionKind == mode && result.Payload == 0xBADC0FFEE0DDF00DUL
                && result.OriginalKind == 0x4a43, "Native original exception payload or kind changed.");
            Check(f.Binding.ReadActivity() is { Known: true, InFlight: 0 }, "Original exit failed to drain.");
        }
        Check(releases == 3, "Original abnormal paths lost captured context release.");
        for (var pass = 0; pass < 2; ++pass)
        {
            f.Control(0, true); var worker = Task.Run(() => f.Call(true));
            try {
                Check(SpinWait.SpinUntil(() => f.Active() == 1, 5000), "Native worker did not enter original.");
                Check(f.Binding.ReadActivity() is { Known: true, InFlight: 1 }, "Worker original appeared drained.");
                if (pass == 0) f.Binding.Dispose();
                Check(f.Broker.ReadExactActivity(f.Target, true) is { Known: true, InFlight: 1 }, "Detached physical original appeared drained.");
            } finally { f.Resume(); Check(worker.Wait(5000), "Native worker failed to join."); }
            Check(worker.Result.Status == 1 && f.Binding.ReadActivity() is { Known: true, InFlight: 0 }, "Worker return failed to drain.");
        }
        Check(releases == 3, "Worker or detached entry captured a main-thread context.");
    }
    private sealed class Context : IDisposable { private readonly Action _release; internal Context(Action release) => _release = release; public void Dispose() => _release(); }
    [StructLayout(LayoutKind.Sequential)] private struct Request { internal IntPtr Entry, Self, Method; internal int Judge; internal uint DeltaBits; }
    [StructLayout(LayoutKind.Sequential)] private struct Result { internal uint Size, Status, ExceptionKind, ExceptionCode; internal ulong Payload; internal IntPtr Reference; internal uint Thread, OriginalKind; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetTarget();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ControlNative(uint mode, uint block);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ActiveNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ResumeNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(ref Request request, ref Result result);
    private sealed class Fixture
    {
        private readonly IntPtr _library;
        private readonly MainThreadDispatcher _main = new();
        private readonly ControlNative _control; internal readonly ActiveNative Active; internal readonly ResumeNative Resume;
        private readonly InvokeNative _invoke, _worker;
        internal readonly NativeJudgeContextBridge Bridge; internal readonly ExactHookBroker Broker;
        internal readonly VerifiedHookTarget Target; internal ExactHookBroker.Binding? Binding;
        internal Fixture()
        {
            string Env(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Missing fixture environment: " + name);
            _library = NativeLibrary.Load(Env("RIZ_INITIALIZATION_ABI_FIXTURE"));
            _control = Export<ControlNative>("RtJudgeActivityControl"); Active = Export<ActiveNative>("RtJudgeActivityActive"); Resume = Export<ResumeNative>("RtJudgeActivityResume");
            _invoke = Export<InvokeNative>("RtJudgeActivityInvoke"); _worker = Export<InvokeNative>("RtJudgeActivityInvokeWorker");
            Target = new("native.judge.activity.fixture", Export<GetTarget>("RtJudgeActivityTarget")(), new(0x123456), new(0x654321), ExactHookAbi.InstanceInt32SingleVoid);
            var log = new Logger(); Bridge = new(Env("RIZ_INITIALIZATION_CONTEXT_HELPER"), log);
            Broker = new(Env("RIZ_INITIALIZATION_ABI_DOBBY"), _main, log);
            Check(Marshal.SizeOf<Request>() == 32 && Marshal.SizeOf<Result>() == 40
                && Marshal.OffsetOf<Result>(nameof(Result.Payload)).ToInt32() == 16
                && Marshal.OffsetOf<Result>(nameof(Result.Reference)).ToInt32() == 24
                && Marshal.OffsetOf<Result>(nameof(Result.OriginalKind)).ToInt32() == 36, "Native fixture record layout changed.");
        }
        private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
        internal void Control(uint mode, bool block) => Check(_control(mode, block ? 1u : 0u) == 1, "Native fixture control rejected.");
        internal Result Call(bool worker)
        {
            var request = new Request { Entry = Target.Address, Self = new(unchecked((long)0x12345678abcdef00)), Method = Target.MethodInfo, Judge = 2, DeltaBits = 0x3f400000 };
            var result = new Result { Size = 40 }; var status = (worker ? _worker : _invoke)(ref request, ref result);
            Check(status == result.Status && status is 1 or 2, "Native fixture invocation failed."); return result;
        }
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
}
