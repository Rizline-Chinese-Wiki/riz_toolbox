using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Real self-owned native original -> Operation bridge -> production broker ->
// actual mutation ledger. Graph/root/initial idle observations are controlled;
// this does not execute the production object backend, game or IL2CPP GC.
internal static class NativeOperationLedgerRegression
{
    internal const string ModeArgument = "--operation-ledger-callbacks";
    private static readonly List<object> Owners = new();
    private const ulong Payload = 0xfedcba9876543210;
    [StructLayout(LayoutKind.Sequential)] private struct NativeResult
    {
        internal uint Version, Size, Kind, Mode, Returned, Caught, Code, Flags, ParameterCount, Checks, Failures, Reserved;
        internal IntPtr Self, Arg0, Arg1, Method, OriginalCaller;
        internal ulong OriginalCalls, Info0, Info1, Info2, Info3;
        internal IntPtr ExceptionAddress;
        internal ulong ReservedTail;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr OriginalNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr MethodNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(IntPtr entry, uint kind, uint mode, IntPtr method, ref NativeResult result);

    internal static int RunChild(string[] args)
    {
        var mode = args.Length == 2 ? args[1] : "invalid"; var run = new Run();
        try
        {
            SetErrorMode(0x8003);
            run.Check(args.Length == 2 && mode is "normal" or "original-seh" or "original-cpp" or "before-fault" or "after-fault", "Unknown operation ledger child mode.");
            run.Check(OperatingSystem.IsWindows() && IntPtr.Size == 8 && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero,
                "Use an isolated Windows x64 child without a game module.");
            run.Check(Marshal.SizeOf<NativeOperationFrame>() == 64 && Marshal.SizeOf<NativeResult>() == 144, "Operation fixture ABI changed.");
            var helperPath = AbsoluteEnv("RIZ_INITIALIZATION_CONTEXT_HELPER");
            var fixturePath = AbsoluteEnv("RIZ_OPERATION_MANAGED_FIXTURE");
            var files = new[] { helperPath, fixturePath }.ToDictionary(path => path, Hash);
            var library = NativeLibrary.Load(fixturePath); Owners.Add(library);
            var original = Export<OriginalNative>(library, "RizOperationManaged_GetOriginal");
            var method = Export<MethodNative>(library, "RizOperationManaged_GetMethod")();
            var invoke = Export<InvokeNative>(library, "RizOperationManaged_Invoke");
            run.Check(method != IntPtr.Zero, "Actual opaque native method is missing.");
            var results = new List<object>();
            ulong originalCalls = 0; var ledgerMutations = 0L; var confirmed = 0L;
            var completionCount = 0; var disposalCount = 0; var controlledRoots = 0;
            var cancelRequests = 0L; var cancelCompletions = 0; var cancelDisposals = 0;
            for (uint kind = 0; kind != 3; ++kind)
            {
                var nativeKind = (NativeOperationKind)kind; var actualOriginal = original(kind);
                run.Check(actualOriginal != IntPtr.Zero, "Actual fixture original is missing.");
                var channel = new Channel(run, helperPath, nativeKind, actualOriginal, method, mode);
                Owners.Add(channel); Collect();
                var nativeMode = mode == "original-seh" ? 1u : mode == "original-cpp" ? 2u : 0u;
                var result = new NativeResult { Version = 1, Size = 144 };
                run.Check(invoke(channel.Bridge.EntryAddress, kind, nativeMode, method, ref result) == 0, "Actual fixture invocation rejected.");
                run.ThrowCallbackFailure();
                run.Check(result.Version == 1 && result.Size == 144 && result.Kind == kind && result.Mode == nativeMode
                    && result.Checks > 0 && result.Failures == 0 && result.OriginalCalls == 1
                    && result.Reserved == 0 && result.ReservedTail == 0, "Native original/ABI assertions failed.");
                var frame = channel.CapturedFrame;
                run.Check(channel.Captures == 1 && channel.Completions == 1 && channel.Disposals == 1
                    && channel.Context is { Inner.Completed: true, Inner.Disposed: true, Disposed: true },
                    "Actual broker did not complete/dispose one retained mutation context.");
                run.Check(frame.Self == result.Self && frame.Arg0 == result.Arg0 && frame.Arg1 == result.Arg1
                    && frame.Method == result.Method && frame.Caller != IntPtr.Zero && result.OriginalCaller != IntPtr.Zero,
                    "The ledger did not receive the unchanged actual native source frame.");
                run.Check(kind == 0 ? result.Arg0 == IntPtr.Zero && result.Arg1 == IntPtr.Zero
                    : kind == 1 ? result.Arg0 != IntPtr.Zero && result.Arg1 == IntPtr.Zero
                    : result.Arg0 != IntPtr.Zero && result.Arg1 != IntPtr.Zero, "Full native argument arity was lost.");
                run.Check(result.Returned == (nativeMode == 0 ? 1u : 0u) && result.Caught == (nativeMode == 0 ? 0u : 1u),
                    "Original exception did not finish in the native DLL.");
                if (nativeMode == 1)
                    run.Check(result.Code == 0xe04f4d53 && result.ParameterCount == 3 && result.Info0 == Payload
                        && result.Info1 == unchecked((ulong)result.Self.ToInt64()) && result.Info2 == kind && result.ExceptionAddress != IntPtr.Zero,
                        "Actual original SEH code/parameters changed.");
                if (nativeMode == 2)
                    run.Check(result.Code == 0xe06d7363 && result.ParameterCount == 4 && result.Info0 == 0x19930520
                        && result.Info1 != 0 && result.Info2 != 0 && result.Info3 == unchecked((ulong)library.ToInt64())
                        && result.ExceptionAddress != IntPtr.Zero, "Actual typed C++ record differs.");
                var owner = channel.Owner!; var backend = channel.Backend!;
                var snapshot = owner.Read(); var eventRow = snapshot.EventSnapshots.Single(); var mutation = eventRow.Mutations!.Single();
                var normal = mode == "normal"; var beforeFault = mode == "before-fault";
                var expectedFlags = nativeMode == 0 ? NativeOperationExitFlags.Returned : NativeOperationExitFlags.Abnormal;
                run.Check(eventRow.Event == result.Self.ToInt64() && eventRow.MutationGeneration == 1
                    && mutation.Operation == channel.Operation && mutation.Generation == 1 && mutation.NativeSequence == frame.Sequence
                    && mutation.Receiver == result.Self.ToInt64() && mutation.Arg0 == result.Arg0.ToInt64()
                    && mutation.Arg1 == result.Arg1.ToInt64() && mutation.Caller == frame.Caller.ToInt64()
                    && mutation.Method == method.ToInt64(), "Actual ledger mutation scalar identity differs from the native call.");
                run.Check(mutation.Captured == !beforeFault && mutation.Completed && mutation.NativeReturned == (nativeMode == 0)
                    && mutation.ConfirmedReturned == normal && mutation.Flags == (uint)expectedFlags
                    && (mutation.Before is not null) == !beforeFault && (mutation.After is not null) == normal,
                    "Native Returned and confirmed graph completion were conflated.");
                run.Check(snapshot.ObservedMutations == 1 && snapshot.ReturnedMutations == (normal ? 1 : 0)
                    && snapshot.NativeInFlight == 0 && snapshot.Unknown == !normal && snapshot.Pending && !snapshot.MutationClosed
                    && snapshot.InlineExecutionUnobserved && !snapshot.DispatchPathVerified,
                    "The actual ledger lost scalar cleanup or claimed future mutation closure.");
                run.Check(backend.Captures == 1 && backend.Reads == (normal || mode == "after-fault" ? 2 : 1)
                    && backend.InitialIdleReads > 0 && backend.ActualActivityReads > 0 && backend.Roots.Count > 0,
                    "The measured controlled before/after read boundary differs.");
                run.Check(backend.Roots.Contains(result.Self.ToInt64())
                    && (result.Arg0 == IntPtr.Zero || backend.Roots.Contains(result.Arg0.ToInt64()))
                    && (result.Arg1 == IntPtr.Zero || backend.Roots.Contains(result.Arg1.ToInt64())),
                    "Controlled mutation argument responsibility was not retained.");
                var nativeActivity = channel.Bridge.ReadActivity(); var brokerActivity = channel.Binding.ReadActivity();
                var exactActivity = channel.Broker.ReadExactActivity(channel.Target);
                run.Check(nativeActivity is { Known: true, InFlight: 0 } && brokerActivity is { Known: true, InFlight: 0 }
                    && exactActivity is { Known: true, InFlight: 0 } && channel.Engine.Prepared == 1
                    && channel.Engine.Committed == 1 && channel.Engine.Installed == 0,
                    "Actual native/broker responsibility did not drain or a detour was installed.");
                if (normal) run.Check(owner.TryConfirmNativeIdle(), "Normal actual ledger did not retire its scalar context.");
                else
                {
                    var reads = backend.Reads; var captures = backend.Captures; var roots = backend.Roots.Count;
                    run.Check(Rejected<InvalidOperationException>(() => owner.TryConfirmNativeIdle()), "Unknown ledger falsely confirmed idle.");
                    run.Check(reads == backend.Reads && captures == backend.Captures && roots == backend.Roots.Count,
                        "Unknown ledger continued graph reads or released controlled roots.");
                }
                NativeCoroutineCancellationSnapshot? cancellation = null;
                if (kind < 2)
                {
                    cancellation = channel.Cancellation!.Read(); var request = cancellation.Requests.Single();
                    run.Check(cancellation.ObservedRequests == 1 && cancellation.Outstanding == 0 && cancellation.UntrackedRequests == 0
                        && cancellation.Pending && !cancellation.EngineCancelled && cancellation.Unknown == (nativeMode != 0)
                        && cancellation.CanConfirmRequestIdle == (nativeMode == 0), "Actual cancellation request scalar cleanup was lost.");
                    run.Check(request.Operation == (kind == 0 ? NativeCoroutineCancellationOperation.StopAll : NativeCoroutineCancellationOperation.StopEnumerator)
                        && request.NativeSequence == frame.Sequence && request.Receiver == result.Self.ToInt64()
                        && request.Arg0 == result.Arg0.ToInt64() && request.Arg1 == result.Arg1.ToInt64()
                        && request.Method == result.Method.ToInt64() && request.Caller == frame.Caller.ToInt64()
                        && request.Requested && request.Captured && request.NativeCompleted && request.Disposed
                        && request.NativeReturned == (nativeMode == 0) && request.Abnormal == (nativeMode != 0)
                        && request.Flags == (uint)expectedFlags && !request.MatchUnproven && !request.IdentityChanged
                        && request.Before.Count == 1 && request.Matched.Count == 1 && request.After.Count == (nativeMode == 0 ? 1 : 0),
                        "The actual callback did not retain its exact cancellation request identity and flags.");
                    run.Check(channel.CancelCaptures == 1 && channel.CancelCompletions == 1 && channel.CancelDisposals == 1
                        && channel.CancellationContext is { Request.NativeCompleted: true, Request.Disposed: true, Disposed: true }
                        && channel.ProviderReads == (nativeMode == 0 ? 2 : 1), "Cancellation provider reads or physical lifecycle differ.");
                    run.Check(channel.CancellationActivity is { Known: true, InFlight: 0 }, "Shared cancellation sink did not drain its real operation activity.");
                    cancelRequests += cancellation.ObservedRequests; cancelCompletions += channel.CancelCompletions; cancelDisposals += channel.CancelDisposals;
                }
                else run.Check(channel.Cancellation is null && channel.CancelCaptures == 0,
                    "The two-reference ABI was incorrectly mapped to a Stop operation.");
                originalCalls += result.OriginalCalls; ledgerMutations += snapshot.ObservedMutations; confirmed += snapshot.ReturnedMutations;
                completionCount += channel.Completions; disposalCount += channel.Disposals; controlledRoots += backend.Roots.Count;
                results.Add(new { kind, operation = channel.Operation.ToString(), frame.Sequence,
                    receiver = result.Self.ToInt64(), argument0 = result.Arg0.ToInt64(), argument1 = result.Arg1.ToInt64(),
                    method = result.Method.ToInt64(), sourceCaller = frame.Caller.ToInt64(), originalCaller = result.OriginalCaller.ToInt64(),
                    result.OriginalCalls, nativeChecks = result.Checks, nativeFailures = result.Failures,
                    ledgerObserved = snapshot.ObservedMutations, ledgerConfirmed = snapshot.ReturnedMutations,
                    ledgerUnknown = snapshot.Unknown, ledgerNativeInFlight = snapshot.NativeInFlight,
                    mutation.Captured, mutation.Completed, mutation.NativeReturned, mutation.ConfirmedReturned,
                    beforeGraphPresent = mutation.Before is not null, afterGraphPresent = mutation.After is not null,
                    controlledRootRecords = backend.Roots.Count, graphReads = backend.Reads, captureReads = backend.Captures,
                    initialControlledIdleReads = backend.InitialIdleReads, actualActivityReads = backend.ActualActivityReads,
                    completions = channel.Completions, disposals = channel.Disposals, nativeInFlight = nativeActivity.InFlight,
                    brokerKnown = brokerActivity.Known, enginePrepare = channel.Engine.Prepared, engineCommit = channel.Engine.Committed,
                    engineInstall = channel.Engine.Installed, nativeException = new { code = result.Code, flags = result.Flags,
                        parameters = result.ParameterCount, information = new[] { result.Info0, result.Info1, result.Info2, result.Info3 },
                        address = result.ExceptionAddress.ToInt64() }, cancellation = cancellation is null ? null : new {
                            cancellation.ObservedRequests, cancellation.Outstanding, cancellation.Pending, cancellation.EngineCancelled,
                            cancellation.Unknown, cancellation.NativeReturnedRequests, cancellation.AbnormalRequests,
                            captures = channel.CancelCaptures, completions = channel.CancelCompletions, disposals = channel.CancelDisposals,
                            providerReads = channel.ProviderReads, actualSequence = cancellation.Requests.Single().NativeSequence } });
            }
            run.Check(originalCalls == 3 && ledgerMutations == 3 && completionCount == 3 && disposalCount == 3,
                "Three actual typed native calls did not reach three actual ledger completions.");
            run.Check(cancelRequests == 2 && cancelCompletions == 2 && cancelDisposals == 2,
                "Actual native zero/one-reference callbacks did not reach both cancellation request ledgers.");
            run.Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && files.All(pair => pair.Value == Hash(pair.Key)),
                "A game module was loaded or a fixture input changed.");
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "native-operation-ledger-fixture", mode,
                passed = true, checks = run.Checks, failures = 0, modeVerified = true,
                nativeToClrCallbackVerified = true, productionBridgeAndBrokerVerified = true, actualLedgerCaptureVerified = true,
                actualMutationLedgerVerified = true, actualOriginalCalls = originalCalls, observedMutations = ledgerMutations,
                confirmedReturnedMutations = confirmed, completionCount, disposalCount, controlledRootRecords = controlledRoots,
                actualCancellationRequestLedgerVerified = true, cancellationRequests = cancelRequests,
                cancellationCompletions = cancelCompletions, cancellationDisposals = cancelDisposals,
                identityProviderControlled = true, actualTrackerProviderUsed = false, productionStopBindingVerified = false,
                nativeInFlight = 0, ledgerNativeInFlight = 0, initialLedgerActivityControlled = true,
                metadataGraphRootsControlled = true, actualIl2CppMetadataResolved = false, productionObjectBackendExecuted = false,
                firstInstallReceiptUsed = false, firstInstallSafetyVerified = false, gameCodeExecuted = false,
                gameRuntimeVerified = false, exceptionFlagsTransparencyVerified = false, nativeExceptionsCaughtBeforePInvoke = true,
                dobbyPublicationVerified = false, allMutationWritersClosed = false, allFutureListenersClosed = false,
                engineCancellationExecuted = false, diskHashesUnchanged = true, installed = false, results,
                files = files.Select(pair => new { path = pair.Key, beforeSha256 = pair.Value, afterSha256 = Hash(pair.Key) }).ToArray() }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "native-operation-ledger-fixture", mode,
                passed = false, checks = run.Checks, failures = 1, failure = error.ToString(), actualLedgerCaptureVerified = false,
                gameCodeExecuted = false, gameRuntimeVerified = false, firstInstallReceiptUsed = false }));
            return 1;
        }
    }

    private sealed class Channel
    {
        private readonly Run _run; private readonly string _mode; private readonly Main _main = new();
        private readonly Authority _authority = new();
        internal readonly Engine Engine; internal readonly ExactHookBroker Broker; internal readonly VerifiedHookTarget Target;
        internal readonly NativeOperationContextBridge Bridge; internal readonly ExactHookBroker.Binding Binding;
        internal readonly UnityEventOperation Operation;
        internal NativeLocalUnityEventLedger? Owner; internal Backend? Backend; internal LedgerContext? Context;
        internal NativeOperationFrame CapturedFrame;
        internal int Captures, Completions, Disposals;
        internal NativeCoroutineCancellationLedger? Cancellation; internal CancellationContext? CancellationContext;
        internal int CancelCaptures, CancelCompletions, CancelDisposals, ProviderReads;
        private readonly ExactHookBroker.Binding? _cancellationBinding;
        internal ExactHookBroker.ManagedBoundaryActivity? CancellationActivity => _cancellationBinding?.ReadActivity();
        internal Channel(Run run, string helperPath, NativeOperationKind kind, IntPtr original, IntPtr method, string mode)
        {
            (_run, _mode) = (run, mode); Engine = new(original); var logger = new Logger(); Broker = new(Engine, _main, logger);
            Operation = kind switch { NativeOperationKind.Void0 => UnityEventOperation.RemoveAllListeners,
                NativeOperationKind.Reference1 => UnityEventOperation.AddListener, _ => UnityEventOperation.RemoveListener };
            Target = new("operation.ledger.fixture." + kind, original, method, new(0x45678), ExactHookBroker.OperationAbi(kind));
            Bridge = new(helperPath, kind, logger);
            Binding = Broker.BindNativeOperationObservation<LedgerContext>(Target, kind, () => Bridge,
                Capture, Complete, (frame, reason) => Owner?.ObserveMutationFault(Operation, frame, reason));
            if (kind is NativeOperationKind.Void0 or NativeOperationKind.Reference1)
                _cancellationBinding = Broker.BindNativeOperationObservation<CancellationContext>(Target with { Id = Target.Id + ".cancel-request" }, kind,
                    () => throw new InvalidOperationException("The cancellation sink must share the actual operation bridge."),
                    CaptureCancellation, CompleteCancellation, (frame, reason) => Cancellation?.ObserveFault(CancelOperation, frame, reason));
        }
        private LedgerContext Capture(NativeOperationFrame frame) => _run.Callback(() => {
            _run.Check(frame.IsValid && frame.Kind == UnityEventOperationProfile.Get(Operation).Kind
                && frame.Method == Target.MethodInfo && _run.Sequences.Add(frame.Sequence) && Owner is null,
                "The actual native Before has a foreign or repeated physical identity.");
            CapturedFrame = frame; ++Captures;
            _run.Check(Binding.ReadActivity() is { Known: true, InFlight: > 0 }, "The actual native call is not physically active.");
            Backend = new(_run, _main, frame, () => Binding.ReadActivity(), _mode);
            // CaptureBackend's initial idle contract belongs to the controlled
            // graph backend. The actual native activity above is independently
            // measured, remains nonzero, and is never rewritten or called Ready.
            Owner = NativeLocalUnityEventLedger.CaptureBackend(_main, Backend, _authority, frame.Self.ToInt64());
            Backend.Initializing = false; _authority.Capturing = false;
            _run.Check(!Owner.IsFor(null!) && Binding.ReadActivity() is { Known: true, InFlight: > 0 },
                "Controlled owner creation granted production authority or hid native activity.");
            _run.Check(Backend.ReadActivity() is { Known: true, InFlight: > 0 },
                "The controlled backend did not switch to actual physical activity after owner initialization.");
            var context = Owner.CaptureMutation(Operation, frame) ?? throw new InvalidOperationException("Actual owned operation was not recorded.");
            _run.Check(context.Frame.Sequence == frame.Sequence && context.Records.Length == 1,
                "The actual mutation context does not retain this native frame.");
            return Context = new(this, context, frame);
        });
        private void Complete(LedgerContext context, NativeOperationFrame frame, NativeOperationExitFlags flags) => _run.Callback(() => {
            _run.Check(ReferenceEquals(Context, context) && frame.Sequence == context.Frame.Sequence && frame.Self == context.Frame.Self
                && frame.Arg0 == context.Frame.Arg0 && frame.Arg1 == context.Frame.Arg1 && frame.Method == context.Frame.Method
                && frame.Caller == context.Frame.Caller && !context.Disposed, "Native completion lost the retained mutation source.");
            ++Completions; Owner!.CompleteMutation(context.Inner, frame, flags);
            _run.Check(context.Inner.Completed && !context.Inner.Disposed, "Ledger scalar completion was not recorded before disposal.");
        });
        internal void DisposeContext(LedgerContext context) => _run.Callback(() => {
            _run.Check(!context.Disposed && context.Inner.Completed, "Context disposal preceded exact scalar completion.");
            context.Inner.Dispose(); context.Disposed = true; ++Disposals;
            _run.Check(context.Inner.Disposed, "Actual ledger mutation context did not retire.");
        });
        private NativeCoroutineCancellationOperation CancelOperation => Target.Abi == ExactHookAbi.OperationVoid0
            ? NativeCoroutineCancellationOperation.StopAll : NativeCoroutineCancellationOperation.StopEnumerator;
        private CancellationContext CaptureCancellation(NativeOperationFrame frame) => _run.Callback(() => {
            _run.Check(Cancellation is null && frame.IsValid && frame.Sequence == CapturedFrame.Sequence
                && frame.Self == CapturedFrame.Self && frame.Arg0 == CapturedFrame.Arg0 && frame.Arg1 == CapturedFrame.Arg1
                && Binding.ReadActivity() is { Known: true, InFlight: > 0 }, "Cancellation sink received another actual physical call.");
            ++CancelCaptures;
            var enumerator = frame.Kind == NativeOperationKind.Reference1 ? frame.Arg0.ToInt64() : checked(frame.Self.ToInt64() + 0x7000);
            var identity = new NativeCoroutineCancellationIdentity(this, frame.Self.ToInt64(), 0, 1, enumerator,
                0, false, false, true, false, false, true);
            var provider = new NativeCoroutineCancellationIdentityProvider(this, frame.Self.ToInt64(), () => {
                ++ProviderReads; return Array.AsReadOnly(new[] { identity });
            }, supplied => ReferenceEquals(supplied.Owner, this) && supplied.Equals(identity));
            Cancellation = new(_main, new[] { provider });
            var request = Cancellation.Capture(CancelOperation, frame)
                ?? throw new InvalidOperationException("The actual same receiver was not owned by its scalar cancellation provider.");
            return CancellationContext = new(this, request);
        });
        private void CompleteCancellation(CancellationContext context, NativeOperationFrame frame, NativeOperationExitFlags flags) => _run.Callback(() => {
            _run.Check(ReferenceEquals(CancellationContext, context) && !context.Disposed, "Cancellation completion crossed its actual retained context.");
            Cancellation!.Complete(context.Request, frame, flags); ++CancelCompletions;
            _run.Check(context.Request.NativeCompleted && !context.Request.Disposed, "Actual cancellation completion was lost before Dispose.");
        });
        internal void DisposeCancellation(CancellationContext context) => _run.Callback(() => {
            _run.Check(!context.Disposed && context.Request.NativeCompleted, "Cancellation Dispose preceded actual native completion.");
            context.Request.Dispose(); context.Disposed = true; ++CancelDisposals;
            _run.Check(context.Request.Disposed, "Actual cancellation request scope did not retire.");
        });
    }
    private sealed class LedgerContext : IDisposable
    {
        private readonly Channel _channel;
        internal readonly NativeLocalUnityEventLedger.MutationContext Inner;
        internal readonly NativeOperationFrame Frame; internal bool Disposed;
        internal LedgerContext(Channel channel, NativeLocalUnityEventLedger.MutationContext inner, NativeOperationFrame frame)
            => (_channel, Inner, Frame) = (channel, inner, frame);
        public void Dispose() => _channel.DisposeContext(this);
    }
    private sealed class CancellationContext : IDisposable
    {
        private readonly Channel _channel;
        internal readonly NativeCoroutineCancellationLedger.RequestContext Request; internal bool Disposed;
        internal CancellationContext(Channel channel, NativeCoroutineCancellationLedger.RequestContext request) => (_channel, Request) = (channel, request);
        public void Dispose() => _channel.DisposeCancellation(this);
    }
    private sealed class Backend : ICutoutListenerBackend
    {
        private readonly Run _run; private readonly Main _main; private readonly NativeOperationFrame _native;
        private readonly Func<ExactHookBroker.ManagedBoundaryActivity> _activity; private readonly string _mode;
        private readonly NativeUnityEventGraph _graph;
        internal readonly HashSet<long> Roots = new(); internal bool Initializing = true;
        internal int Reads, Captures, InitialIdleReads, ActualActivityReads;
        internal Backend(Run run, Main main, NativeOperationFrame native, Func<ExactHookBroker.ManagedBoundaryActivity> activity, string mode)
        {
            (_run, _main, _native, _activity, _mode) = (run, main, native, activity, mode);
            var actual = native.Self.ToInt64();
            _graph = new(actual, checked(actual + 0x1000), checked(actual + 0x1010), false, false,
                new(checked(actual + 0x1020), 0, Array.Empty<NativeUnityEventInvokable>()),
                new(checked(actual + 0x1030), 0, Array.Empty<NativeUnityEventInvokable>()),
                new(checked(actual + 0x1040), 0, Array.Empty<NativeUnityEventInvokable>()),
                checked(actual + 0x1050), 0, Array.Empty<NativeUnityEventPersistent>(), 0);
        }
        public bool CleanupUnknown => false;
        public int RootRecords => Roots.Count;
        public void Initialize(NativeLocalUnityEventLedger owner)
        { _main.VerifyAccess(); _run.Check(Initializing && owner.RegisterEvent(_native.Self.ToInt64()) == 0, "Controlled owner did not register actual Self."); }
        public NativeUnityEventGraph ReadGraph(int slot)
        {
            _main.VerifyAccess(); ++Reads; _run.Check(slot == 0, "Controlled graph read used another event.");
            if (!Initializing && _mode == "after-fault") throw new IOException("Controlled post graph failure on actual native completion.");
            foreach (var value in new[] { _graph.Event, _graph.Calls, _graph.PersistentGroup, _graph.PersistentCalls.Object,
                _graph.RuntimeCalls.Object, _graph.ExecutingCalls.Object, _graph.CandidatesList }) Roots.Add(value);
            return _graph;
        }
        public NativeUnityEventGraph CaptureMutation(int slot, UnityEventOperation operation, NativeOperationFrame frame)
        {
            _main.VerifyAccess(); ++Captures;
            _run.Check(!Initializing && slot == 0 && frame.Sequence == _native.Sequence && frame.Self == _native.Self
                && frame.Arg0 == _native.Arg0 && frame.Arg1 == _native.Arg1 && frame.Caller == _native.Caller,
                "Controlled graph received a rewritten native mutation frame.");
            foreach (var value in new[] { frame.Self.ToInt64(), frame.Arg0.ToInt64(), frame.Arg1.ToInt64() }) if (value != 0) Roots.Add(value);
            if (_mode == "before-fault") throw new IOException("Controlled pre graph failure after retaining actual native arguments.");
            return _graph;
        }
        public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList) => throw new InvalidOperationException("No synthetic Prepare is provided.");
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        {
            _main.VerifyAccess();
            if (Initializing) { ++InitialIdleReads; return new(0, true); }
            ++ActualActivityReads; return _activity();
        }
    }
    private sealed class Authority : ICutoutListenerAuthority
    {
        internal bool Capturing = true; private int _fault;
        public bool Unknown => Volatile.Read(ref _fault) != 0;
        public void VerifyBeforeCapture() { if (!Capturing || Unknown) throw new InvalidOperationException("Controlled capture authority expired."); }
        public void VerifyObservation() { if (Capturing || Unknown) throw new InvalidOperationException("Controlled observation authority unknown."); }
        public bool TryConfirmSourceIdle() => true;
        public void ObserveFaultFromAnyThread() => Interlocked.Exchange(ref _fault, 1);
    }
    private sealed class Run
    {
        internal int Checks; internal readonly HashSet<ulong> Sequences = new(); private Exception? _failure;
        internal void Check(bool value, string message) { Interlocked.Increment(ref Checks); if (!value) throw new InvalidOperationException(message); }
        internal T Callback<T>(Func<T> callback)
        { try { return callback(); } catch (Exception error) { Interlocked.CompareExchange(ref _failure, error, null); throw; } }
        internal void Callback(Action callback)
        { try { callback(); } catch (Exception error) { Interlocked.CompareExchange(ref _failure, error, null); throw; } }
        internal void ThrowCallbackFailure() { if (Volatile.Read(ref _failure) is { } error) throw new InvalidOperationException("Actual callback assertion failed.", error); }
    }
    private sealed class Main : IMainThreadDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public bool IsMainThread => _thread == Environment.CurrentManagedThreadId;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Controlled ledger left its actual callback thread."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Engine : INativeDetourEngine, IPreparedNativeDetourEngine
    {
        private readonly IntPtr _original; internal int Prepared, Committed, Installed;
        internal Engine(IntPtr original) => _original = original;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) { ++Installed; throw new NotSupportedException("No detour installation is allowed."); }
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement) { ++Prepared; return _original; }
        public void Commit(VerifiedHookTarget target) => ++Committed;
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
    private static string AbsoluteEnv(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        if (path is null || !Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new ArgumentException("Missing absolute fixture file: " + name);
        return Path.GetFullPath(path);
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    private static bool Rejected<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
}
