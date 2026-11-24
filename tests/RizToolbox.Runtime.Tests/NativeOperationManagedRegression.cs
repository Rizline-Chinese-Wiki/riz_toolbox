using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static class NativeOperationManagedRegression
{
    internal const string ModeArgument = "--operation-managed-callbacks";
    private static readonly List<object> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Result
    {
        internal uint Version, Size, Kind, Mode, Returned, Caught, Code, Flags, ParameterCount, NativeChecks, NativeFailures, Reserved;
        internal IntPtr Self, Arg0, Arg1, Method, OriginalCaller;
        internal ulong OriginalCalls, Info0, Info1, Info2, Info3;
        internal IntPtr ExceptionAddress;
        internal ulong ReservedTail;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr OriginalNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr MethodNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(IntPtr entry, uint kind, uint mode, IntPtr method, ref Result result);
    private static T Export<T>(IntPtr module, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    internal static int RunChild(string[] args)
    {
        var checks = 0; var originals = 0UL; var callbacks = 0; var completions = 0; var disposals = 0;
        var mode = args.Length == 2 ? args[1] : "invalid";
        void Check(bool value, string message) { ++checks; if (!value) throw new InvalidOperationException(message); }
        try
        {
            SetErrorMode(0x8003);
            Check(args.Length == 2 && mode is "normal" or "original-seh" or "original-cpp" or "detach" or "observer-fault", "Unknown operation child mode.");
            Check(OperatingSystem.IsWindows() && IntPtr.Size == 8 && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero, "An isolated x64 child without GameAssembly is required.");
            Check(Marshal.SizeOf<NativeOperationFrame>() == 64 && Marshal.SizeOf<Result>() == 144, "Native operation layout changed.");
            var helperPath = Environment.GetEnvironmentVariable("RIZ_INITIALIZATION_CONTEXT_HELPER") ?? "";
            var fixturePath = Environment.GetEnvironmentVariable("RIZ_OPERATION_MANAGED_FIXTURE") ?? "";
            Check(Path.IsPathFullyQualified(helperPath) && Path.IsPathFullyQualified(fixturePath), "Fixture paths must be absolute.");
            var hashes = new[] { helperPath, fixturePath }.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
            var library = NativeLibrary.Load(fixturePath); Owners.Add(library);
            var original = Export<OriginalNative>(library, "RizOperationManaged_GetOriginal");
            var method = Export<MethodNative>(library, "RizOperationManaged_GetMethod")();
            var invoke = Export<InvokeNative>(library, "RizOperationManaged_Invoke");
            var seenSequences = new HashSet<ulong>();
            for (uint kind = 0; kind != 3; ++kind)
            {
                var nativeKind = (NativeOperationKind)kind; var actualOriginal = original(kind);
                Check(actualOriginal != IntPtr.Zero && method != IntPtr.Zero, "Missing native originals or method identity.");
                var main = new Main(); var engine = new Engine(actualOriginal); var log = new FixtureLogger();
                var broker = new ExactHookBroker(engine, main, log);
                var target = new VerifiedHookTarget("operation.fixture." + kind, actualOriginal, method, new IntPtr(0x13579), ExactHookBroker.OperationAbi(nativeKind));
                NativeOperationContextBridge? bridge = null;
                ExactHookBroker.Binding? first = null;
                var captures = new int[2]; var after = new int[2]; var released = new int[2];
                var frames = new List<NativeOperationFrame>();
                var flagsSeen = new List<NativeOperationExitFlags>();
                Context? Capture(int sink, NativeOperationFrame frame)
                {
                    ++callbacks; ++captures[sink];
                    Check(frame.IsValid && frame.Kind == nativeKind && frame.Method == method, "Actual native callback has a foreign typed identity.");
                    if (sink == 0)
                    {
                        Check(seenSequences.Add(frame.Sequence), "Native sequence was reused."); frames.Add(frame);
                        if (mode == "observer-fault") throw new IOException("Controlled observation failure.");
                        if (mode == "detach") first!.Dispose();
                    }
                    return new Context(frame, () => { ++released[sink]; ++disposals; });
                }
                void Complete(int sink, Context context, NativeOperationFrame frame, NativeOperationExitFlags flags)
                {
                    ++after[sink]; ++completions;
                    Check(frame.Sequence == context.Frame.Sequence && frame.Self == context.Frame.Self
                        && frame.Arg0 == context.Frame.Arg0 && frame.Arg1 == context.Frame.Arg1 && frame.Caller == context.Frame.Caller,
                        "Completion differs from its actual retained native frame.");
                    flagsSeen.Add(flags);
                }
                first = broker.BindNativeOperationObservation(target, nativeKind, () => bridge = new NativeOperationContextBridge(helperPath, nativeKind, log),
                    frame => Capture(0, frame), (context, frame, flags) => Complete(0, context, frame, flags), (_, _) => { });
                var second = broker.BindNativeOperationObservation(target, nativeKind,
                    () => throw new InvalidOperationException("A second native channel must not be created."),
                    frame => Capture(1, frame), (context, frame, flags) => Complete(1, context, frame, flags), (_, _) => { });
                Check(engine.Prepared == 1 && engine.Committed == 1 && engine.Installed == 0, "Operation transport publication order differs.");
                var result = new Result { Version = 1, Size = 144 };
                var nativeMode = mode == "original-seh" ? 1u : mode == "original-cpp" ? 2u : 0u;
                Check(invoke(bridge!.EntryAddress, kind, nativeMode, method, ref result) == 0, "Owned native invocation rejected.");
                Check(result.Version == 1 && result.Size == 144 && result.Kind == kind && result.Mode == nativeMode
                    && result.NativeFailures == 0 && result.NativeChecks > 0 && result.OriginalCalls == 1, "Native original or ABI checks failed.");
                originals += result.OriginalCalls;
                var expectedFlags = nativeMode == 0 ? NativeOperationExitFlags.Returned : NativeOperationExitFlags.Abnormal;
                Check(flagsSeen.Count == (mode == "observer-fault" ? 1 : 2) && flagsSeen.All(flag => flag == expectedFlags), "Original native completion flags changed.");
                Check(captures[0] == 1 && captures[1] == 1 && after[1] == 1 && released[1] == 1, "Peer observer or cleanup did not run once.");
                Check(after[0] == (mode == "observer-fault" ? 0 : 1) && released[0] == after[0], "First observer cleanup differs from capture responsibility.");
                var actual = frames.Single();
                Check(actual.Self == result.Self && actual.Arg0 == result.Arg0 && actual.Arg1 == result.Arg1 && actual.Method == result.Method
                    && actual.Caller != IntPtr.Zero && result.OriginalCaller != IntPtr.Zero, "Native source references changed in CLR.");
                Check(result.Returned == (nativeMode == 0 ? 1u : 0u) && result.Caught == (nativeMode == 0 ? 0u : 1u), "Original exception did not remain inside the native catch.");
                if (nativeMode != 0) Check(result.Code != 0 && result.ExceptionAddress != IntPtr.Zero && result.ParameterCount > 0, "Missing native exception record.");
                var activity = first.ReadActivity(); var exact = broker.ReadExactActivity(target);
                Check(activity.InFlight == 0 && exact.InFlight == 0 && activity.Known == (mode != "observer-fault") && exact.Known == activity.Known,
                    "Actual native and broker physical activity did not drain.");
                first.Dispose(); second.Dispose(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                if (mode == "detach")
                {
                    var beforeCallbacks = callbacks; result = new Result { Version = 1, Size = 144 };
                    Check(invoke(bridge.EntryAddress, kind, 0, method, ref result) == 0 && result.OriginalCalls == 1 && result.NativeFailures == 0,
                        "Permanent native original disappeared after detach and GC.");
                    ++originals; Check(callbacks == beforeCallbacks && first.ReadActivity() is { Known: true, InFlight: 0 }, "Detached callbacks remained logically active.");
                }
                Owners.AddRange(new object[] { broker, bridge, first, second, engine, log });
            }
            Check(hashes.All(row => row.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(row.Key)))) && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero,
                "Fixture inputs changed or a game module was loaded.");
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "native-operation-managed-fixture", mode, passed = true, failures = 0, checks,
                actualOriginalCalls = originals, callbacks, completions, disposals, nativeInFlight = 0, modeVerified = true,
                productionBridgeAndBrokerVerified = true, nativeExceptionsCaughtBeforePInvoke = true, diskHashesUnchanged = true,
                gameCodeExecuted = false, gameRuntimeVerified = false, firstInstallSafetyVerified = false,
                actualIl2CppMetadataResolved = false, actualLedgerCaptureVerified = false, dobbyPublicationVerified = false, installed = false }));
            return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { kind = "native-operation-managed-fixture", mode, passed = false, failures = 1, checks })); return 1; }
    }
    private sealed class Context : IDisposable
    { internal NativeOperationFrame Frame; private readonly Action _release; internal Context(NativeOperationFrame frame, Action release) => (Frame, _release) = (frame, release); public void Dispose() => _release(); }
    private sealed class Main : IMainThreadDispatcher
    { private readonly int _thread = Environment.CurrentManagedThreadId; public bool IsMainThread => Environment.CurrentManagedThreadId == _thread; public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Worker."); } public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
    private sealed class FixtureLogger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
    private sealed class Engine : INativeDetourEngine, IPreparedNativeDetourEngine
    {
        private readonly IntPtr _original; internal int Prepared, Committed, Installed;
        internal Engine(IntPtr original) => _original = original;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) { ++Installed; throw new NotSupportedException(); }
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement) { ++Prepared; return _original; }
        public void Commit(VerifiedHookTarget target) => ++Committed;
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32")] private static extern uint SetErrorMode(uint mode);
}
