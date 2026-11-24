using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Invoked by hostfxr only after the same native executable completed its real
// early startup calls. No test here loads/configures/publishes native hooks.
public static class NativeEnginePublicationManagedEntry
{
    private static readonly List<object> Roots = new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint RunNative(uint kind, uint mode, IntPtr output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(IntPtr output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint KernelInfoNative(uint kind, uint slot, ref NativeEngineNodeInformation output);
    private sealed class Captured : IDisposable
    {
        internal NativeEngineNodeFrame Frame;
        internal NativeEngineNodeCompletion Completion;
        internal int Sink, NativeMode, CompleteCount, DisposeCount, CaptureThread;
        public void Dispose() => ++DisposeCount;
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int Run(IntPtr input, int length)
    {
        var checks = 0; var failures = new List<string>(); var output = ""; var events = new List<object>();
        var callbackFailures = new List<string>(); var faultEvents = new List<object>();
        void Check(bool value, string message) { ++checks; if (!value) throw new InvalidOperationException(message); }
        try
        {
            if (input == IntPtr.Zero || length is <= 0 or > 1048576) return 2;
            var bytes = new byte[length]; Marshal.Copy(input, bytes, 0, length);
            using var document = JsonDocument.Parse(bytes); var args = document.RootElement;
            string Value(string name) => args.GetProperty(name).GetString() ?? throw new InvalidOperationException(name);
            output = Value("output"); var mode = Value("mode");
            Check(Path.IsPathFullyQualified(output) && mode is "normal" or "detach" or "original-seh" or "original-cpp", "Exact managed fixture arguments required.");
            var owner = NativeEngineFirstInstall.RequireOwnedPublished(Value("bootstrapPath"), Value("bootstrapSha256"),
                Value("kernelPath"), Value("kernelSha256"), Value("targetPath"), Value("targetSha256"), Value("executablePath"), Value("executableSha256"));
            Check(owner.Publication.Profile == 1 && owner.Publication.State == 4 && !owner.Unknown, "Actual native owned V4 must already be Ready.");
            var capturedReceipt = owner.Publication.Bytes.ToArray();
            var bootstrap = GetModuleHandle("RizToolbox.EngineBootstrap.dll"); var target = GetModuleHandle("RizEngineOwnedTargets.dll");
            Check(bootstrap != IntPtr.Zero && target != IntPtr.Zero && owner.KernelModule != IntPtr.Zero, "All native images must already be loaded.");
            var run = Marshal.GetDelegateForFunctionPointer<RunNative>(NativeLibrary.GetExport(target, "RizEngineOwned_Run"));
            var read = Marshal.GetDelegateForFunctionPointer<ReadNative>(NativeLibrary.GetExport(bootstrap, "RizEngineFirstInstall_GetInfoV4"));
            var kernelInfo = Marshal.GetDelegateForFunctionPointer<KernelInfoNative>(NativeLibrary.GetExport(owner.KernelModule, "RizEngineNodeContext_GetInfo"));
            var log = new QuietLogger();
            var dispatchers = new NativeEngineNodeObserverDispatcher[4];
            var bindings = new List<IDisposable>(); var contexts = new List<Captured>();
            var captures = 0; var completed = 0; var faults = 0;
            var captureCounts = new int[4, 2]; var completeCounts = new int[4, 2]; var faultCounts = new int[4, 2];
            var identities = new NativeEngineNodeChannelIdentity[4]; var initialKernel = new NativeEngineNodeInformation[4];
            var physicalCalls = new ulong[4]; var activeMode = 0; var invokingKind = 0;
            var detachDuringCapture = false; var detachedInsideCapture = false;
            var mainThread = Environment.CurrentManagedThreadId;
            for (var kind = 0; kind < 4; ++kind)
            {
                var selected = (NativeEngineNodeKind)kind;
                var dispatcher = NativeEngineNodeObserverDispatcher.AdoptPublished(owner, selected, log);
                dispatchers[kind] = dispatcher; identities[kind] = dispatcher.Identity;
                // Two consumers share the one physical native attachment.
                for (var sink = 0; sink < 2; ++sink)
                {
                    var selectedSink = sink; var selectedKind = kind;
                    var binding = dispatcher.Bind(frame =>
                    {
                        try
                        {
                            Check(frame.IsValid && frame.Kind == selected && Environment.CurrentManagedThreadId == mainThread,
                                "Each real consumer capture must receive its exact kind on this synchronous native caller thread.");
                            var context = new Captured { Frame = frame, Sink = selectedSink, CaptureThread = Environment.CurrentManagedThreadId,
                                NativeMode = selectedKind == invokingKind ? activeMode : 0 };
                            contexts.Add(context); ++captures; ++captureCounts[selectedKind, selectedSink];
                            if (detachDuringCapture && selected == NativeEngineNodeKind.Start && selectedSink == 0)
                            {
                                detachDuringCapture = false; detachedInsideCapture = true;
                                // Enter already froze this Start's two observers. Both must
                                // complete; nested and subsequent calls see empty membership.
                                foreach (var retainedBinding in bindings) retainedBinding.Dispose();
                            }
                            return context;
                        }
                        catch (Exception error) { callbackFailures.Add(error.ToString()); throw; }
                    }, (Captured context, NativeEngineNodeFrame frame, NativeEngineNodeCompletion completion) =>
                    {
                        try
                        {
                            ++context.CompleteCount; ++completed; ++completeCounts[selectedKind, selectedSink];
                            Check(NativeEngineNodeObserverDispatcher.SameFrame(context.Frame, frame) && frame.Kind == selected
                                && context.Sink == selectedSink && context.CompleteCount == 1 && context.DisposeCount == 0
                                && context.CaptureThread == Environment.CurrentManagedThreadId && completion.IsValidFor(selected),
                                "One exact frame/consumer responsibility must complete once on its capture thread before disposal.");
                            var abnormal = context.NativeMode != 0;
                            var expectedFlags = abnormal ? NativeEngineNodeExitFlags.Abnormal : NativeEngineNodeExitFlags.Returned
                                | (selected == NativeEngineNodeKind.Detach ? 0 : NativeEngineNodeExitFlags.ReturnByteValid);
                            var expectedReturn = abnormal ? 0u : selected == NativeEngineNodeKind.Start ? 0x81u
                                : selected == NativeEngineNodeKind.Execute ? 0xa5c3fe81u : selected == NativeEngineNodeKind.Release ? 0xe7u : 0u;
                            Check(completion.Flags == expectedFlags && completion.RawReturn == expectedReturn,
                                "Managed completion must preserve exact full typed return bits or an exact abnormal result without a value.");
                            Check(selected == NativeEngineNodeKind.Start && !abnormal
                                ? completion.OutputFlags == (NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read) && completion.StartOutputNode != IntPtr.Zero
                                : completion.OutputFlags == 0 && completion.StartOutputNode == IntPtr.Zero,
                                "Only normally returned Start may expose its actual copied output node.");
                            context.Completion = completion;
                            events.Add(new { kind = (uint)frame.Kind, sink = selectedSink, sequence = frame.Sequence, threadId = Environment.CurrentManagedThreadId,
                                arg0 = unchecked((ulong)frame.Arg0.ToInt64()), flags = (uint)completion.Flags, returned = completion.RawReturn,
                                outputFlags = (uint)completion.OutputFlags, outputNode = unchecked((ulong)completion.StartOutputNode.ToInt64()) });
                        }
                        catch (Exception error) { callbackFailures.Add(error.ToString()); throw; }
                    }, (frame, reason) =>
                    {
                        ++faults; ++faultCounts[selectedKind, selectedSink];
                        faultEvents.Add(new { kind = (uint)frame.Kind, sink = selectedSink, frame.Sequence, reason });
                    });
                    bindings.Add(binding);
                }
            }
            Roots.AddRange(new object[] { owner, run, read, kernelInfo, log, dispatchers, bindings, contexts, events, callbackFailures, faultEvents });
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            for (uint kind = 0; kind < 4; ++kind)
            {
                var info = new NativeEngineNodeInformation { Version = 1, Size = 112 };
                Check(kernelInfo(kind, identities[kind].Slot, ref info) == 0
                    && NativeEngineNodeContextBridge.MatchesInformation(info, identities[kind], true)
                    && info.InFlight == 0 && info.Entered == info.Exited,
                    "Baseline actual Kernel channel must already be attached and quiescent while preserving prior startup call counts.");
                initialKernel[kind] = info;
            }
            var rowHashes = new List<string>();
            void Invoke(uint kind, uint nativeMode, bool expectConsumers = true, bool detachInside = false)
            {
                var firstContext = contexts.Count; var firstEvents = events.Count;
                activeMode = checked((int)nativeMode); invokingKind = checked((int)kind);
                var expectedPhysical = new int[4]; ++expectedPhysical[kind];
                if (kind == 0 && nativeMode == 0) { ++expectedPhysical[1]; ++expectedPhysical[2]; }
                for (var index = 0; index < 4; ++index) physicalCalls[index] += (ulong)expectedPhysical[index];
                var capturesBefore = (int[,])captureCounts.Clone(); var completesBefore = (int[,])completeCounts.Clone();
                if (detachInside) detachDuringCapture = true;
                var row = Marshal.AllocHGlobal(8448);
                try
                {
                    Marshal.Copy(new byte[8448], 0, row, 8448); Marshal.WriteInt32(row, 1); Marshal.WriteInt32(row, 4, 8448);
                    var status = run(kind, nativeMode, row); var result = new byte[8448]; Marshal.Copy(row, result, 0, result.Length);
                    Check(status == 0 && BitConverter.ToUInt32(result, 0) == 1 && BitConverter.ToUInt32(result, 4) == 8448,
                        "Actual typed target invocation must finish with its complete raw record.");
                    Check(BitConverter.ToUInt32(result, 12) == kind && BitConverter.ToUInt32(result, 16) == nativeMode
                        && BitConverter.ToUInt32(result, 8) == 11 && BitConverter.ToUInt32(result, 20) == GetCurrentThreadId()
                        && BitConverter.ToUInt32(result, 140) == 0, "Actual row identity, native thread and original diagnostics differ.");
                    if (nativeMode == 0)
                    {
                        var expected = kind == 0 ? 0x81u : kind == 1 ? 0xa5c3fe81u : kind == 2 ? 0xe7u : 0u;
                        Check(BitConverter.ToUInt32(result, 24) == 1 && BitConverter.ToUInt32(result, 28) == expected
                            && BitConverter.ToUInt32(result, 32) == 0, "Original typed return value must survive published observation.");
                    }
                    else Check(BitConverter.ToUInt32(result, 24) == 0 && BitConverter.ToUInt32(result, 32) == (nativeMode == 1 ? 1u : 2u)
                        && BitConverter.ToUInt32(result, 36) == (nativeMode == 1 ? 0xe0465853u : 0xe06d7363u), "Original native exception must propagate into its real typed catcher.");
                    Check(callbackFailures.Count == 0, "A callback assertion failure must never be swallowed into a passing fixture report.");
                    var added = contexts.Skip(firstContext).ToArray(); var expectedTotal = 0;
                    for (var selectedKind = 0; selectedKind < 4; ++selectedKind)
                    {
                        var expectedPerSink = expectConsumers ? (detachInside ? (selectedKind == 0 ? 1 : 0) : expectedPhysical[selectedKind]) : 0;
                        expectedTotal += 2 * expectedPerSink;
                        for (var sink = 0; sink < 2; ++sink)
                            Check(captureCounts[selectedKind, sink] - capturesBefore[selectedKind, sink] == expectedPerSink
                                && completeCounts[selectedKind, sink] - completesBefore[selectedKind, sink] == expectedPerSink,
                                "Each kind and each of two consumers must capture/complete exactly its actual physical invocation count.");
                    }
                    Check(added.Length == expectedTotal && events.Count - firstEvents == expectedTotal
                        && added.All(context => context.CompleteCount == 1 && context.DisposeCount == 1),
                        "Every added consumer context must complete and release exactly once, including after a native exception or logical detach.");
                    foreach (var context in added.Where(context => (uint)context.Frame.Kind == kind))
                    {
                        var frame = context.Frame;
                        Check(unchecked((ulong)frame.Arg0.ToInt64()) == BitConverter.ToUInt64(result, 80)
                            && unchecked((ulong)frame.Arg1.ToInt64()) == BitConverter.ToUInt64(result, 88)
                            && unchecked((ulong)frame.Arg2.ToInt64()) == BitConverter.ToUInt64(result, 96)
                            && unchecked((ulong)frame.Arg3.ToInt64()) == BitConverter.ToUInt64(result, 104),
                            "Managed top-level frame must match all four actual native argument scalars in the same call row.");
                    }
                    if (kind == 0 && nativeMode == 0 && expectConsumers && !detachInside)
                    {
                        var node = added.First(context => context.Frame.Kind == NativeEngineNodeKind.Execute).Frame.Arg0;
                        Check(node != IntPtr.Zero && added.Where(context => context.Frame.Kind is NativeEngineNodeKind.Execute or NativeEngineNodeKind.Release)
                                .All(context => context.Frame.Arg0 == node)
                            && added.Where(context => context.Frame.Kind == NativeEngineNodeKind.Start).All(context => context.Completion.StartOutputNode == node),
                            "Nested Execute/Release and returned Start output must retain the same actual node identity without dereferencing it.");
                    }
                    rowHashes.Add(Convert.ToHexString(SHA256.HashData(result)).ToLowerInvariant());
                }
                finally { Marshal.FreeHGlobal(row); }
            }
            for (uint kind = 0; kind < 4; ++kind) Invoke(kind, 0);
            Check(captures == 12 && captures == completed && contexts.All(context => context.CompleteCount == 1 && context.DisposeCount == 1) && faults == 0,
                "All actual captured consumer responsibilities must complete and dispose exactly once.");
            if (mode == "detach")
            {
                Invoke(0, 0, detachInside: true);
                Check(detachedInsideCapture && !detachDuringCapture && captures == 14 && completed == 14,
                    "Both frozen Start consumers must finish after the first capture logically detaches all sinks; nested calls capture none.");
                var before = captures;
                for (uint kind = 0; kind < 4; ++kind) Invoke(kind, 0, expectConsumers: false);
                Check(captures == before && faults == 0, "Detached sinks must not capture later physical native calls or acquire a false observation fault.");
            }
            if (mode is "original-seh" or "original-cpp")
            {
                Invoke(1, mode == "original-seh" ? 1u : 2u);
                Check(captures == 14 && completed == 14 && faults == 2 && dispatchers[1].Unknown
                    && faultCounts[1, 0] == 1 && faultCounts[1, 1] == 1 && faultEvents.Count == 2
                    && callbackFailures.Count == 0 && contexts.All(context => context.CompleteCount == 1 && context.DisposeCount == 1),
                    "An original exception must notify each Execute consumer once and still finish both captured cleanup responsibilities exactly once.");
                var rejected = false; try { owner.VerifyReady(); } catch (UnsupportedSignatureException) { rejected = true; }
                Check(rejected && owner.Unknown, "Original exception must retain sticky Unknown and refuse subsequent adoption.");
            }
            else
            {
                owner.VerifyReady(); Check(capturedReceipt.AsSpan().SequenceEqual(owner.Publication.Bytes), "CLR adoption must not rewrite the immutable publication record.");
                var duplicateRejected = false;
                try { _ = NativeEngineNodeContextBridge.AdoptPublished(owner, NativeEngineNodeKind.Start, log); }
                catch (InvalidOperationException) { duplicateRejected = true; }
                Check(duplicateRejected, "A second permanent CLR owner must be rejected.");
            }
            var abnormalMode = mode is "original-seh" or "original-cpp";
            var kernelObservations = new List<object>();
            for (uint kind = 0; kind < 4; ++kind)
            {
                var activity = dispatchers[kind].ReadActivity();
                Check(activity.InFlight == 0 && activity.Known == !abnormalMode,
                    "Normal observed activity must be known/quiescent; an abnormal publication must remain explicitly unknown.");
                // An Unknown bridge can conservatively return zero without a
                // native read. Independently query the actual retained Kernel.
                var info = new NativeEngineNodeInformation { Version = 1, Size = 112 };
                var identity = identities[kind]; var baseline = initialKernel[kind];
                Check(kernelInfo(kind, identity.Slot, ref info) == 0 && info.Version == 1 && info.Size == 112
                    && info.Kind == kind && info.Slot == identity.Slot && info.Target == identity.Target && info.Original == identity.Original
                    && info.Entry == identity.Entry && info.Epoch == identity.Epoch && info.Attached == 1
                    && info.InFlight == 0 && info.Entered == info.Exited && info.Entered >= baseline.Entered
                    && info.Entered - baseline.Entered == physicalCalls[kind]
                    && info.Unknown == (abnormalMode && kind == 1 ? 1u : 0u)
                    && info.EnterFaults == 0 && info.ExitFaults == 0 && info.SnapshotFaults == 0 && info.OutputFaults == 0,
                    "Actual Kernel counters, exact attached tuple and fault state must independently prove all expected physical invocations drained.");
                kernelObservations.Add(new { kind, info.Slot, info.Unknown, info.Entered, info.Exited, info.InFlight,
                    actualPhysicalCalls = physicalCalls[kind], activity.Known });
            }
            Check(callbackFailures.Count == 0 && contexts.All(context => context.CompleteCount == 1 && context.DisposeCount == 1)
                && captures == completed && faults == (abnormalMode ? 2 : 0), "Final callback and cleanup accounting must remain exact in every scenario.");
            var consumers = new List<object>();
            for (var kind = 0; kind < 4; ++kind) for (var sink = 0; sink < 2; ++sink)
                consumers.Add(new { kind, sink, captures = captureCounts[kind, sink], completed = completeCounts[kind, sink],
                    disposed = contexts.Where(context => (int)context.Frame.Kind == kind && context.Sink == sink).Sum(context => context.DisposeCount),
                    faults = faultCounts[kind, sink] });
            File.WriteAllText(output, JsonSerializer.Serialize(new { kind = "same-process-engine-publication-managed", passed = true,
                actualProcessId = Environment.ProcessId, checks, mode, captures, completed, faults, rowHashes,
                events, consumers, faultEvents, callbackFailures, kernelObservations, detachedInsideCapture,
                publicationSha256 = Convert.ToHexString(SHA256.HashData(capturedReceipt)).ToLowerInvariant(),
                productionAdmissionGranted = false, gameExecuted = false, installed = false }));
            return 0;
        }
        catch (Exception error)
        {
            failures.Add(error.ToString());
            if (Path.IsPathFullyQualified(output)) File.WriteAllText(output, JsonSerializer.Serialize(new { kind = "same-process-engine-publication-managed", passed = false,
                actualProcessId = Environment.ProcessId, checks, failures, events, callbackFailures, faultEvents, productionAdmissionGranted = false }));
            return 1;
        }
    }
    private sealed class QuietLogger : ILogger
    {
        public void Log(LogLevel level, string message, Exception? exception = null) { }
    }
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
