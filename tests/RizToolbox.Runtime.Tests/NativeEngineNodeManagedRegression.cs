using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>Self-owned native Dobby targets -> typed helper -> real bridge and
/// dispatcher -> the actual node ledger. Native result records are fixture
/// inputs, never IL2CPP metadata, engine scheduler proof or game authority.</summary>
internal static class NativeEngineNodeManagedRegression
{
    internal const string ModeArgument = "--engine-node-managed";
    private const uint Slot = 25, FreeNode = 1, NestedExecute = 4, NestedRelease = 8, UnreadableNode = 16, NullOutputValue = 64;
    private const string DobbyHash = "8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded";
    private static readonly List<object> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Result
    {
        internal uint Version, Size, Kind, Mode, Scenario, Returned, Caught, ReturnByte;
        internal uint Code, Flags, ParameterCount, NativeChecks, NativeFailures, NodeFreed, OutputProtected, Reserved;
        internal IntPtr Arg0, Arg1, Arg2, Arg3, Node, Owner;
        internal ulong EnumeratorHandle;
        internal IntPtr EnumeratorCache;
        internal uint EnumeratorType;
        internal int ReferenceCount;
        internal ulong OriginalCalls;
        internal IntPtr OriginalCaller, OutputNode, ExceptionAddress;
        internal ulong Info0, Info1, Info2, Info3, NestedCalls;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Configuration
    { internal uint Version, Size; internal IntPtr Target, Original; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInformation
    { internal IntPtr BaseAddress, AllocationBase; internal uint AllocationProtect; internal ushort PartitionId; internal nuint RegionSize; internal uint State, Protect, Type; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr TargetNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CurrentNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(IntPtr target, uint kind, uint mode, uint scenario, uint rawByte, ref Result result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PrepareNative(IntPtr target, IntPtr entry, out IntPtr original);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CommitNative(IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint kind, uint slot, ref Configuration config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint kind, uint slot, ref NativeEngineNodeInformation info);

    internal static int RunChild(string[] args)
    {
        var run = new Run(); var mode = args.Length == 2 ? args[1] : "invalid";
        try
        {
            SetErrorMode(0x8003);
            run.Check(args.Length == 2 && mode is "normal" or "original-seh" or "original-cpp" or "detach" or "observer-fault", "Unknown engine-node child mode.");
            run.Check(OperatingSystem.IsWindows() && IntPtr.Size == 8 && NoGame(), "Use an isolated Windows x64 child without game modules.");
            run.Check(Marshal.SizeOf<Result>() == 208 && Marshal.SizeOf<Configuration>() == 24
                && Marshal.SizeOf<NativeEngineNodeFrame>() == 112 && Marshal.SizeOf<NativeEngineNodeCompletion>() == 32
                && Marshal.SizeOf<NativeEngineNodeInformation>() == 112, "Engine-node native/managed layouts changed.");
            var helperPath = AbsoluteEnv("RIZ_INITIALIZATION_CONTEXT_HELPER");
            var fixturePath = AbsoluteEnv("RIZ_ENGINE_NODE_MANAGED_FIXTURE");
            var dobbyPath = AbsoluteEnv("RIZ_ENGINE_NODE_DOBBY");
            run.Check(Hash(dobbyPath) == DobbyHash, "The inspected Dobby Prepare/Commit image is required.");
            var files = new[] { helperPath, fixturePath, dobbyPath }.ToDictionary(path => path, Hash);
            var helper = NativeLibrary.Load(helperPath); var fixture = NativeLibrary.Load(fixturePath); var dobby = NativeLibrary.Load(dobbyPath);
            Owners.AddRange(new object[] { helper, fixture, dobby });
            var target = Export<TargetNative>(fixture, "RizEngineNodeManaged_GetTarget");
            var current = Export<CurrentNative>(fixture, "RizEngineNodeManaged_GetCurrent");
            var invoke = Export<InvokeNative>(fixture, "RizEngineNodeManaged_Invoke");
            var prepare = Export<PrepareNative>(dobby, "DobbyPrepare"); var commit = Export<CommitNative>(dobby, "DobbyCommit");
            var configure = Export<ConfigureNative>(helper, "RizEngineNodeContext_Configure");
            var info = Export<InfoNative>(helper, "RizEngineNodeContext_GetInfo");
            var main = new Main(); var ledger = new NativeEngineCoroutineNodeLedger(main); var logger = new Logger();
            var identities = new IdentityJoint(run, main);
            var channels = new List<Channel>();
            // All four real targets must be published and adopted before the
            // Start original synchronously calls its real Execute/Release.
            for (uint kind = 0; kind < 4; ++kind)
            {
                var address = target(kind); MemoryInformation memory = default;
                run.Check(address != IntPtr.Zero && VirtualQuery(address, out memory, MemorySize) != 0
                    && memory.AllocationBase == fixture && NativeListenerPublication.ExecutableProtection(memory.Protect),
                    "A target is not executable code in the self-owned native fixture.");
                var cold = Bytes(address, 32); var empty = NewInfo();
                run.Check(info(kind, Slot, ref empty) == 2 && empty.Kind == kind && empty.Slot == Slot
                    && empty.Entry != IntPtr.Zero && empty.Target == IntPtr.Zero && empty.Original == IntPtr.Zero
                    && empty.Epoch == 0 && empty.Unknown == 0, "The permanent engine-node slot is not empty.");
                run.Check(prepare(address, empty.Entry, out var original) == 0 && original != IntPtr.Zero && original != address
                    && cold.SequenceEqual(Bytes(address, 32)), "Real Dobby Prepare failed or published source instructions early.");
                var config = new Configuration { Version = 1, Size = 24, Target = address, Original = original };
                run.Check(configure(kind, Slot, ref config) == 0, "Native immutable configuration was rejected.");
                var configured = ReadInfo(run, info, kind);
                var identity = new NativeEngineNodeChannelIdentity((NativeEngineNodeKind)kind, Slot, address, original, empty.Entry, configured.Epoch);
                run.Check(NativeEngineNodeContextBridge.MatchesInformation(configured, identity, false), "Native configured identity differs.");
                var status = -1; var restored = false; var flushed = false;
                try { status = commit(address); }
                finally
                {
                    restored = VirtualProtect(address, 32, memory.Protect, out _) != 0;
                    flushed = FlushInstructionCache(GetCurrentProcess(), address, 32) != 0;
                }
                run.Check(status == 0 && restored && flushed && !cold.SequenceEqual(Bytes(address, 32)) && Reaches(address, identity.Entry),
                    "Real Dobby Commit did not publish the typed native entry.");
                var bridge = NativeEngineNodeContextBridge.AdoptConfigured(helperPath, identity, logger);
                var dispatcher = new NativeEngineNodeObserverDispatcher(bridge, logger);
                var channel = new Channel(run, mode, fixture, helper, current, identity, bridge, dispatcher, ledger, identities);
                channel.Bind(); channels.Add(channel); Owners.Add(channel);
                run.Check(NativeEngineNodeContextBridge.MatchesInformation(ReadInfo(run, info, kind), identity, true)
                    && bridge.ReadActivity() is { Known: true, InFlight: 0 } && dispatcher.ReadActivity() is { Known: true, InFlight: 0 },
                    "Actual native channel adoption/Attach could not be confirmed.");
                run.Check(Rejected<InvalidOperationException>(() => NativeEngineNodeContextBridge.AdoptConfigured(helperPath, identity, logger)),
                    "A second CLR owner consumed a permanent native channel.");
            }
            Owners.AddRange(new object[] { main, ledger, logger, channels }); Collect();
            var invocations = new List<Result>(); var callsBeforeDetach = 0; var logicalDetachEmptyCalls = 0;
            Result Call(uint kind, uint nativeMode, uint scenario, uint rawByte)
            {
                var before = run.Captures; var beforeEvents = channels[(int)kind].Events.Count;
                var result = Invoke(run, invoke, channels[(int)kind].Identity.Target, fixture, helper, kind, nativeMode, scenario, rawByte);
                invocations.Add(result); run.ThrowCallbackFailure();
                var saved = channels[(int)kind].Events.Skip(beforeEvents).LastOrDefault(row => row.Frame.Arg0 == result.Arg0 && row.Frame.Arg1 == result.Arg1
                    && row.Frame.Arg2 == result.Arg2 && row.Frame.Arg3 == result.Arg3);
                if (run.Captures != before)
                {
                    run.Check(saved is not null && saved.Completion is { } && saved.LedgerCall.Completed && saved.LedgerCall.Disposed,
                        "Observed actual target did not retain its exact ledger completion/disposal.");
                    CheckCompletion(run, saved!.Completion!.Value, result, rawByte);
                    if (nativeMode == 1) run.Check(result.Info1 == unchecked((ulong)saved.OriginalAddress.ToInt64()),
                        "Original exception payload no longer identifies the actual native work.");
                }
                return result;
            }
            if (mode is "original-seh" or "original-cpp")
            {
                var nativeMode = mode == "original-seh" ? 1u : 2u;
                for (uint kind = 0; kind < 4; ++kind) Call(kind, nativeMode, 0, 0xff);
                var captured = run.Captures;
                for (uint kind = 0; kind < 4; ++kind) Call(kind, 0, kind == 0 ? NullOutputValue : 0, 0x80);
                run.Check(run.Captures == captured && captured == 4, "Sticky original exception responsibility admitted later captures.");
            }
            else if (mode == "detach")
            {
                Call(3, 0, FreeNode, 0); Call(0, 0, NestedExecute | NestedRelease, 0x80);
                callsBeforeDetach = run.Captures;
                run.Check(callsBeforeDetach == 4 && channels.All(channel => channel.DidDetach), "Each kind did not detach inside its actual frozen observer snapshot.");
                for (uint kind = 0; kind < 4; ++kind) { Call(kind, 0, kind == 0 ? NullOutputValue : 0, 0xff); ++logicalDetachEmptyCalls; }
                run.Check(run.Captures == callsBeforeDetach, "Detached sinks received new physical invocations.");
                foreach (var channel in channels) channel.Bind(); Collect();
                Call(0, 0, NestedExecute | NestedRelease, 0xff);
                run.Check(run.Captures == 7 && channels.All(channel => channel.Bindings == 2), "Logical rebind replaced native ownership or lost exact scopes.");
            }
            else if (mode == "observer-fault")
            {
                Call(0, 0, NestedExecute | NestedRelease, 0x80);
                Call(3, 0, UnreadableNode | FreeNode, 0);
                var captured = run.Captures;
                for (uint kind = 0; kind < 4; ++kind) Call(kind, 0, kind == 0 ? NullOutputValue : 0, 0xff);
                run.Check(captured == 4 && run.Captures == captured && channels.All(channel => channel.ControlledFaults == 1),
                    "A controlled peer fault dropped original execution or admitted new captures.");
                run.Check(channels[3].Events.Single().Frame.SnapshotFlags == (NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Faulted),
                    "The native unreadable Before did not reach both sinks as an actual faulted frame.");
            }
            else
            {
                // Observe the deliberately foreign standalone nodes before any
                // owned Start generation. Their arbitrary addresses may be
                // recycled after FreeNode and must not borrow a prior Start.
                Call(1, 0, 0, 0xabcdef01); Call(2, 0, FreeNode, 0x80); Call(3, 0, FreeNode, 0);
                Call(0, 0, NestedExecute | NestedRelease, 0x80);
                Call(0, 0, NestedExecute | NestedRelease, 0xff);
                run.Check(run.Captures == 9, "The real nested and standalone native original counts differ.");
            }
            var uint32Frames = channels[1].Events.Where(row => row.Completion is { } completion
                && (completion.Flags & NativeEngineNodeExitFlags.Returned) != 0 && completion.RawReturn > byte.MaxValue).ToArray();
            var uint32Observed = uint32Frames.Length != 0;
            run.Check(uint32Observed == (mode is "normal" or "detach" or "observer-fault")
                && (mode != "normal" || uint32Frames.Any(row => row.Completion!.Value.RawReturn == 0xabcdef01)),
                "Actual Execute EAX high bits were dropped or the low-byte scheduler predicate was substituted.");
            var snapshot = ledger.Read(); var expectedUnknown = mode is "original-seh" or "original-cpp" or "observer-fault";
            run.Check(snapshot.Calls == run.Captures && snapshot.Outstanding == 0 && snapshot.Untracked == 0
                && snapshot.Unknown == expectedUnknown && snapshot.Pending && !snapshot.EngineCancelled,
                "The actual shared node ledger leaked a scope or granted cancellation completion.");
            run.Check(run.Captures > 0 && channels.Sum(channel => channel.Captures) == run.Captures
                && channels.All(channel => channel.Captures == channel.PeerCaptures && channel.Captures == channel.Completions
                    && channel.Captures == channel.PeerCompletions && channel.Captures == channel.Disposals && channel.Captures == channel.PeerDisposals),
                "A physical frame did not retain both exact completion/disposal duties.");
            var expectedNodes = mode is "normal" or "detach" ? 2 : mode == "observer-fault" ? 1 : 0;
            run.Check(snapshot.Nodes.Count == expectedNodes, "Native Start/Execute correlation invented or lost a node generation.");
            if (!expectedUnknown)
            {
                run.Check(snapshot.Nodes.All(node => node.StartCompleted && node.FinalCleanupObserved && node.ActiveExecutions == 0 && node.OpenCalls == 0
                    && node.ExecuteCalls == 1 && node.ReleaseCalls == 1 && node.LastReleaseByte == 0 && !node.InlineCleanupUnproven),
                    "The actual synchronous Execute/final Release facts did not stay separate and balanced.");
                run.Check(snapshot.Nodes.Select(node => node.Generation).Distinct().Count() == 2
                    && snapshot.Nodes.Select(node => node.Enumerator).Distinct().Count() == 2
                    && snapshot.Nodes.Select(node => node.NativeOwner).Distinct().Count() == 2
                    && snapshot.Nodes.Select(node => node.EnumeratorHandle).Distinct().Count() == 2,
                    "Repeated actual Starts borrowed another native work's owner/enumerator/generation.");
            }
            var rows = new List<object>(); ulong nativeEntered = 0, nativeExited = 0;
            foreach (var channel in channels)
            {
                var native = ReadInfo(run, info, (uint)channel.Identity.Kind); nativeEntered += native.Entered; nativeExited += native.Exited;
                var bridgeActivity = channel.Bridge.ReadActivity(); var dispatcherActivity = channel.Dispatcher.ReadActivity();
                var nativeUnknown = mode is "original-seh" or "original-cpp" || mode == "observer-fault" && channel.Identity.Kind == NativeEngineNodeKind.Detach;
                run.Check(native.Identity == channel.Identity && native.Attached == 1 && native.Unknown == (nativeUnknown ? 1u : 0u)
                    && native.Entered == native.Exited && native.InFlight == 0 && native.EnterFaults == 0 && native.ExitFaults == 0
                    && native.SnapshotFaults == (mode == "observer-fault" && channel.Identity.Kind == NativeEngineNodeKind.Detach ? 1UL : 0UL)
                    && native.OutputFaults == 0 && channel.Source.SequenceEqual(Bytes(channel.Identity.Target, 32))
                    && Reaches(channel.Identity.Target, channel.Identity.Entry), "Permanent native identity/source or fault counters changed.");
                run.Check(bridgeActivity.InFlight == 0 && dispatcherActivity.InFlight == 0 && bridgeActivity.Known == !nativeUnknown
                    && dispatcherActivity.Known == !expectedUnknown, "Native/dispatcher physical activity or sticky Unknown differs.");
                rows.Add(new { kind = (uint)channel.Identity.Kind, slot = Slot, channel.Identity.Epoch,
                    target = channel.Identity.Target.ToInt64(), original = channel.Identity.Original.ToInt64(), entry = channel.Identity.Entry.ToInt64(),
                    native.Entered, native.Exited, native.InFlight, native.Unknown, native.SnapshotFaults, native.OutputFaults,
                    captures = channel.Captures, peerCaptures = channel.PeerCaptures, completions = channel.Completions,
                    peerCompletions = channel.PeerCompletions, disposals = channel.Disposals, peerDisposals = channel.PeerDisposals,
                    controlledObserverFaults = channel.ControlledFaults, faultNotifications = channel.FaultNotifications,
                    bindings = channel.Bindings, channel.DidDetach, bridgeKnown = bridgeActivity.Known, dispatcherKnown = dispatcherActivity.Known,
                    sourceInstructionsUnchangedAfterAdoption = true,
                    frames = channel.Events.Select(row => new { row.Frame.Sequence, kind = (uint)row.Frame.Kind,
                        arg0 = row.Frame.Arg0.ToInt64(), arg1 = row.Frame.Arg1.ToInt64(), arg2 = row.Frame.Arg2.ToInt64(), arg3 = row.Frame.Arg3.ToInt64(),
                        caller = row.Frame.Caller.ToInt64(), node = row.Frame.Node.ToInt64(), owner = row.Frame.Owner.ToInt64(),
                        row.Frame.EnumeratorHandle, cache = row.Frame.EnumeratorCache.ToInt64(), row.Frame.EnumeratorType, row.Frame.ReferenceCount,
                        snapshotFlags = (uint)row.Frame.SnapshotFlags, completed = row.LedgerCall.Completed, disposed = row.LedgerCall.Disposed,
                        flags = (uint)row.Completion!.Value.Flags, rawReturn = row.Completion.Value.RawReturn,
                        outputNode = row.Completion.Value.StartOutputNode.ToInt64(), outputFlags = (uint)row.Completion.Value.OutputFlags }).ToArray() });
            }
            var actualOriginals = invocations.Aggregate(0UL, (sum, row) => sum + row.OriginalCalls + row.NestedCalls);
            var identitySnapshots = identities.Read();
            run.Check(identitySnapshots.Length == (mode is "normal" or "detach" ? 2 : 1)
                && identitySnapshots.All(value => value.Outstanding == 0 && value.Nodes.Outstanding == 0
                    && value.Identities.Count == 1 && value.Pending && !value.EngineCancelled),
                "Actual engine callbacks did not balance their independent tracker-identity ledger scopes.");
            if (mode is "normal" or "detach")
                run.Check(identitySnapshots.All(value => !value.Unknown && value.Identities.Single().HandleUpgraded
                    && value.Identities.Single().NativeStartReturned && value.Nodes.Nodes.Count == 1
                    && value.Nodes.Nodes.Single().FinalCleanupObserved),
                    "Actual native Start/Execute/Release failed to associate before Start return and upgrade its controlled tracker handle later.");
            run.Check(actualOriginals == nativeEntered && nativeEntered == nativeExited && actualOriginals > 0
                && invocations.All(row => row.NativeChecks > 0 && row.NativeFailures == 0 && row.OriginalCalls == 1)
                && NoGame() && files.All(pair => Hash(pair.Key) == pair.Value), "Native original totals, disk inputs or process isolation differ.");
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "native-engine-node-managed-fixture", mode,
                passed = true, checks = run.Checks, failures = 0, modeVerified = true, configuredChannels = 4, attachedChannels = 4,
                actualDobbyPreparedTargets = 4, actualDobbyCommittedTargets = 4, actualOriginalCalls = actualOriginals,
                physicalCaptures = run.Captures, completionCount = channels.Sum(channel => channel.Completions),
                disposalCount = channels.Sum(channel => channel.Disposals), peerCompletionCount = channels.Sum(channel => channel.PeerCompletions),
                peerDisposalCount = channels.Sum(channel => channel.PeerDisposals), sharedPeerSinks = 4,
                nativeToClrCallbackVerified = true, actualDobbyPublicationVerified = true,
                executeUint32TransportVerified = uint32Observed,
                executeHighBitReturns = uint32Frames.Select(row => row.Completion!.Value.RawReturn).ToArray(),
                actualIdentityLedgerCaptureVerified = true, trackerIdentityProvidersControlled = true,
                actualTrackerBackendExecuted = false,
                identityChains = identitySnapshots.Select(value => new { value.Calls, value.Outstanding, value.Unknown, value.Pending,
                    value.EngineCancelled, nodeCount = value.Nodes.Nodes.Count,
                    identities = value.Identities.Select(item => new { item.StartSequence, item.NativeOwner, item.Node, item.EngineGeneration,
                        item.NativeStartReturned, item.HandleUpgraded, item.StartDisposed,
                        item.Tracker.Receiver, item.Tracker.Slot, trackerGeneration = item.Tracker.Generation,
                        item.Tracker.Enumerator, item.Tracker.ObservedHandle, item.Tracker.HandleConfirmed }).ToArray() }).ToArray(),
                productionBridgeAndDispatcherVerified = true, actualNodeLedgerCaptureVerified = true, oneSharedLedgerAcrossFourKinds = true,
                actualFrameSharedByTwoSinks = true, synchronousNodeAssociationBeforeStartReturnsVerified = expectedNodes > 0,
                freedNodeNotReadAfterOriginalVerified = mode is "normal" or "detach" or "observer-fault",
                logicalDetachEmptyCalls, capturesBeforeLogicalDetach = callsBeforeDetach,
                nativeInFlight = 0, dispatcherInFlight = 0, ledgerOutstanding = snapshot.Outstanding, ledgerUnknown = snapshot.Unknown,
                ledgerPending = snapshot.Pending, engineCancelled = snapshot.EngineCancelled, nodeGenerations = snapshot.Nodes,
                nativeFixtureScalarValuesControlled = true, metadataIdentityControlled = true, actualIl2CppMetadataResolved = false,
                productionObjectBackendExecuted = false, firstInstallReceiptUsed = false, firstInstallSafetyVerified = false,
                concurrentFirstPublicationVerified = false, productionEngineHooksPublished = false,
                exceptionFlagsTransparencyVerified = false, nativeExceptionsCaughtBeforePInvoke = true,
                engineCancellationExecuted = false, schedulerClosureVerified = false, futureStartClosureVerified = false,
                gameCodeExecuted = false, gameRuntimeVerified = false, installed = false, diskHashesUnchanged = true,
                results = rows, invocations = invocations.Select(row => new { row.Kind, row.Mode, row.Scenario, row.Returned, row.Caught,
                    row.ReturnByte, row.OriginalCalls, row.NestedCalls, row.NativeChecks, row.NativeFailures, row.NodeFreed,
                    node = row.Node.ToInt64(), owner = row.Owner.ToInt64(), enumCache = row.EnumeratorCache.ToInt64(), row.EnumeratorHandle,
                    outputNode = row.OutputNode.ToInt64(), originalCaller = row.OriginalCaller.ToInt64(),
                    exception = new { code = row.Code, flags = row.Flags, parameters = row.ParameterCount,
                        information = new[] { row.Info0, row.Info1, row.Info2, row.Info3 }, address = row.ExceptionAddress.ToInt64() } }).ToArray(),
                files = files.Select(pair => new { path = pair.Key, beforeSha256 = pair.Value, afterSha256 = Hash(pair.Key) }).ToArray() }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "native-engine-node-managed-fixture", mode,
                passed = false, checks = run.Checks, failures = 1, failure = error.ToString(), actualNodeLedgerCaptureVerified = false,
                engineCancelled = false, gameCodeExecuted = false, gameRuntimeVerified = false, firstInstallReceiptUsed = false })); return 1;
        }
    }

    private sealed class Event
    {
        internal readonly NativeEngineNodeFrame Frame; internal readonly NativeEngineCoroutineNodeLedger.Call LedgerCall;
        internal readonly IntPtr OriginalAddress; internal NativeEngineNodeCompletion? Completion;
        internal IdentityCapture? IdentityCall;
        internal Event(NativeEngineNodeFrame frame, NativeEngineCoroutineNodeLedger.Call call, IntPtr originalAddress)
            => (Frame, LedgerCall, OriginalAddress) = (frame, call, originalAddress);
    }
    private sealed class Channel
    {
        private readonly Run _run; private readonly string _mode; private readonly IntPtr _fixture, _helper;
        private readonly CurrentNative _current; private readonly NativeEngineCoroutineNodeLedger _ledger;
        private readonly IdentityJoint _identities;
        internal readonly NativeEngineNodeChannelIdentity Identity; internal readonly NativeEngineNodeContextBridge Bridge;
        internal readonly NativeEngineNodeObserverDispatcher Dispatcher; internal readonly byte[] Source;
        internal readonly List<Event> Events = new();
        private NativeEngineNodeObserverDispatcher.Binding? _first, _second;
        internal int Captures, PeerCaptures, Completions, PeerCompletions, Disposals, PeerDisposals, Bindings, ControlledFaults, FaultNotifications;
        internal bool DidDetach;
        internal Channel(Run run, string mode, IntPtr fixture, IntPtr helper, CurrentNative current,
            NativeEngineNodeChannelIdentity identity, NativeEngineNodeContextBridge bridge, NativeEngineNodeObserverDispatcher dispatcher,
            NativeEngineCoroutineNodeLedger ledger, IdentityJoint identities)
        { (_run, _mode, _fixture, _helper, _current, Identity, Bridge, Dispatcher, _ledger, _identities) = (run, mode, fixture, helper, current, identity, bridge, dispatcher, ledger, identities); Source = Bytes(identity.Target, 32); }
        internal void Bind()
        {
            ++Bindings;
            _first = Dispatcher.Bind<LedgerContext>(Capture, Complete, Fault);
            _second = Dispatcher.Bind<PeerContext>(CapturePeer, CompletePeer, Fault);
        }
        private LedgerContext Capture(NativeEngineNodeFrame frame) => _run.Callback(() => {
            _run.Check(frame.IsValid && frame.Kind == Identity.Kind && _run.Sequences.Add(frame.Sequence)
                && Bridge.ReadActivity().InFlight > 0 && Dispatcher.ReadActivity().InFlight > 0, "Actual native physical frame is foreign or inactive.");
            var pointer = _current(); _run.Check(pointer != IntPtr.Zero, "Native owned current record is unavailable at Enter.");
            var native = Marshal.PtrToStructure<Result>(pointer);
            _run.Check(native.Version == 1 && native.Size == 208 && native.Kind == (uint)frame.Kind && native.OriginalCalls == 0
                && frame.Arg0 == native.Arg0 && frame.Arg1 == native.Arg1 && frame.Arg2 == native.Arg2 && frame.Arg3 == native.Arg3
                && VirtualQuery(frame.Caller, out var caller, MemorySize) != 0 && caller.AllocationBase == _fixture,
                "Copied source frame does not identify the actual native fixture's pending original.");
            if (frame.Kind != NativeEngineNodeKind.Start)
            {
                _run.Check(frame.Node == native.Node, "Actual native node argument changed.");
                if ((frame.SnapshotFlags & NativeEngineNodeSnapshotFlags.Valid) != 0)
                    _run.Check(frame.Owner == native.Owner && frame.EnumeratorHandle == native.EnumeratorHandle
                        && frame.EnumeratorCache == native.EnumeratorCache && frame.EnumeratorType == native.EnumeratorType
                        && frame.ReferenceCount == native.ReferenceCount, "The native bounded Before snapshot differs from its actual input fields.");
                else _run.Check(frame.Owner == IntPtr.Zero && frame.EnumeratorHandle == 0 && frame.EnumeratorCache == IntPtr.Zero
                    && frame.EnumeratorType == 0 && frame.ReferenceCount == 0, "A faulted Before leaked a partial native identity.");
            }
            var call = _ledger.Capture(frame); var row = new Event(frame, call, pointer);
            row.IdentityCall = _identities.Capture(frame);
            Events.Add(row); ++Captures; ++_run.Captures;
            if (frame.Kind == NativeEngineNodeKind.Execute && frame.EnumeratorType == 2)
            {
                var state = _ledger.Read();
                _run.Check(state.Nodes.Any(node => node.Node == frame.Node.ToInt64() && node.ActiveExecutions == 1 && !node.StartCompleted),
                    "The actual synchronous Execute was not associated while its real Start was still open.");
                _run.Check(call.Parent is { Frame.Kind: NativeEngineNodeKind.Start, Completed: false, Disposed: false },
                    "Synchronous native execution borrowed another parent scope.");
            }
            if (_mode == "detach" && !DidDetach) { _first!.Dispose(); _second!.Dispose(); DidDetach = true; }
            return new LedgerContext(this, row);
        });
        private PeerContext CapturePeer(NativeEngineNodeFrame frame) => _run.Callback(() => {
            var row = Events.Single(item => item.Frame.Sequence == frame.Sequence);
            _run.Check(NativeEngineNodeObserverDispatcher.SameFrame(row.Frame, frame) && !row.LedgerCall.Completed && !row.LedgerCall.Disposed,
                "The peer sink did not receive the exact same physical native frame.");
            ++PeerCaptures; return new PeerContext(this, row);
        });
        private void Complete(LedgerContext context, NativeEngineNodeFrame frame, NativeEngineNodeCompletion completion) => _run.Callback(() => {
            _run.Check(NativeEngineNodeObserverDispatcher.SameFrame(context.Row.Frame, frame) && !context.Disposed && completion.IsValidFor(frame.Kind)
                && _current() == context.Row.OriginalAddress && Bridge.ReadActivity().InFlight > 0,
                "The actual ledger context completed outside its retained native work/window.");
            ++Completions; context.Row.Completion = completion; _ledger.Complete(context.Row.LedgerCall, frame, completion);
            _identities.Complete(context.Row.IdentityCall, frame, completion);
            _run.Check(context.Row.LedgerCall.Completed && !context.Row.LedgerCall.Disposed, "The actual node ledger did not record scalar completion first.");
            var native = Marshal.PtrToStructure<Result>(_current());
            _run.Check(native.OriginalCalls == 1 && native.NativeFailures == 0 && native.OriginalCaller != IntPtr.Zero
                && VirtualQuery(native.OriginalCaller, out var caller, MemorySize) != 0 && caller.AllocationBase == _helper,
                "The native helper did not execute the actual original once through its real trampoline.");
            if (frame.Kind == NativeEngineNodeKind.Start && native.NestedCalls == 2)
                _run.Check(native.NodeFreed == 1 && completion.StartOutputNode == native.Node,
                    "Start After failed to copy its actual output slot after its nested original freed the node.");
            if (frame.EnumeratorType == 2 && frame.Kind is NativeEngineNodeKind.Execute or NativeEngineNodeKind.Release)
                _run.Check(completion.RawReturn == (frame.Kind == NativeEngineNodeKind.Execute ? 0xa5c3fe81u : 0u),
                    "The actual nested Execute/Release raw return byte changed.");
        });
        private void CompletePeer(PeerContext context, NativeEngineNodeFrame frame, NativeEngineNodeCompletion completion)
        {
            _run.Callback(() => {
                ++PeerCompletions;
                _run.Check(NativeEngineNodeObserverDispatcher.SameFrame(context.Row.Frame, frame) && !context.Disposed
                    && context.Row.LedgerCall.Completed && !context.Row.LedgerCall.Disposed && context.Row.Completion!.Value.Equals(completion),
                    "The peer completion did not retain the same result and exact ledger cleanup scope.");
            });
            if (_mode == "observer-fault" && ControlledFaults == 0)
            { ++ControlledFaults; throw new IOException("Controlled engine-node peer completion fault after actual ledger completion."); }
        }
        private void Fault(NativeEngineNodeFrame frame, string reason) { ++FaultNotifications; _ledger.ObserveFault(frame, reason); _identities.ObserveFault(frame, reason); }
        internal void DisposeLedger(LedgerContext context) => _run.Callback(() => {
            _run.Check(!context.Disposed && context.Row.LedgerCall.Completed, "The ledger context disposed before exact completion.");
            context.Row.LedgerCall.Dispose(); context.Disposed = true; ++Disposals;
            _identities.Dispose(context.Row.IdentityCall);
            _run.Check(context.Row.LedgerCall.Disposed, "The actual node ledger physical scope did not retire.");
        });
        internal void DisposePeer(PeerContext context) => _run.Callback(() => {
            _run.Check(!context.Disposed && context.Row.LedgerCall.Completed && !context.Row.LedgerCall.Disposed,
                "Peer cleanup lost its reverse-order physical scope duty.");
            context.Disposed = true; ++PeerDisposals;
        });
    }
    private sealed class LedgerContext : IDisposable
    { private readonly Channel _owner; internal readonly Event Row; internal bool Disposed; internal LedgerContext(Channel owner, Event row) => (_owner, Row) = (owner, row); public void Dispose() => _owner.DisposeLedger(this); }
    private sealed class PeerContext : IDisposable
    { private readonly Channel _owner; internal readonly Event Row; internal bool Disposed; internal PeerContext(Channel owner, Event row) => (_owner, Row) = (owner, row); public void Dispose() => _owner.DisposePeer(this); }

    // Real copied native frames reach the actual new Adapter ledger. The
    // tracker DTO and receiver/native-owner pairing remain controlled inputs;
    // no synthetic pair is presented as a runtime object/first-install receipt.
    private sealed class IdentityWork
    {
        internal readonly IdentityWork? Parent;
        internal readonly NativeEngineCoroutineIdentityLedger Ledger;
        internal NativeCoroutineCancellationIdentity Row;
        internal IdentityWork(Main main, NativeEngineNodeFrame start, int index, IdentityWork? parent)
        {
            Parent = parent;
            Row = new(this, 0x1000 + index, 0, index + 17, start.Arg1.ToInt64(), 0, false, false, true, false, false, true);
            var provider = new NativeCoroutineCancellationIdentityProvider(this, Row.Receiver,
                () => new[] { Row }, value => Row.SameRecord(value));
            Ledger = new(main, new NativeEngineCoroutineNodeLedger(main), new[] { provider },
                new[] { new NativeEngineCoroutineOwnerPair(Row.Receiver, start.Arg0.ToInt64()) });
        }
    }
    private sealed record IdentityCapture(IdentityWork Work, NativeEngineCoroutineIdentityLedger.Call Call);
    private sealed class IdentityJoint
    {
        private readonly Run _run; private readonly Main _main;
        private readonly List<IdentityWork> _works = new();
        private IdentityWork? _current;
        internal IdentityJoint(Run run, Main main) => (_run, _main) = (run, main);
        internal IdentityCapture? Capture(NativeEngineNodeFrame frame)
        {
            if (frame.Kind == NativeEngineNodeKind.Start)
            { _current = new(_main, frame, _works.Count, _current); _works.Add(_current); }
            if (_current is null) return null;
            var call = _current.Ledger.Capture(frame);
            if (frame.Kind == NativeEngineNodeKind.Execute && ! _current.Ledger.Unknown)
            {
                var snapshot = _current.Ledger.Read(); var identity = snapshot.Identities.Single();
                _run.Check(identity.Node == frame.Node.ToInt64() && identity.EngineGeneration > 0
                    && !identity.NativeStartReturned && identity.Tracker.Enumerator == frame.EnumeratorCache.ToInt64()
                    && identity.NativeOwner == frame.Owner.ToInt64() && identity.Tracker.Receiver != identity.NativeOwner,
                    "Actual synchronous Execute lost its separate native owner, managed receiver or tracker/engine generation.");
            }
            return new(_current, call);
        }
        internal void Complete(IdentityCapture? context, NativeEngineNodeFrame frame, NativeEngineNodeCompletion completion)
        { if (context is not null) context.Work.Ledger.Complete(context.Call, frame, completion); }
        internal void Dispose(IdentityCapture? context)
        {
            if (context is null) return;
            context.Call.Dispose();
            _run.Check(context.Call.Completed && context.Call.Disposed, "Actual identity ledger lost paired physical disposal.");
            if (context.Call.Frame.Kind != NativeEngineNodeKind.Start) return;
            if (!context.Work.Ledger.Unknown)
            {
                context.Work.Row = context.Work.Row with { StartReturned = true, HandleConfirmed = true, ObservedHandle = 0x700000 + _works.Count };
                context.Work.Ledger.RefreshTrackedIdentities();
            }
            _current = context.Work.Parent;
        }
        internal void ObserveFault(NativeEngineNodeFrame frame, string reason) => _current?.Ledger.ObserveFault(frame, reason);
        internal NativeEngineCoroutineIdentityLedgerSnapshot[] Read() => _works.Select(work => work.Ledger.Read()).ToArray();
    }
    private sealed class Run
    {
        internal int Checks, Captures; internal readonly HashSet<ulong> Sequences = new(); private Exception? _failure;
        internal void Check(bool condition, string message) { Interlocked.Increment(ref Checks); if (!condition) throw new InvalidOperationException(message); }
        internal T Callback<T>(Func<T> callback) { try { return callback(); } catch (Exception error) { Interlocked.CompareExchange(ref _failure, error, null); throw; } }
        internal void Callback(Action callback) { try { callback(); } catch (Exception error) { Interlocked.CompareExchange(ref _failure, error, null); throw; } }
        internal void ThrowCallbackFailure() { if (Volatile.Read(ref _failure) is { } error) throw new InvalidOperationException("Actual engine-node CLR callback assertion failed.", error); }
    }
    private static Result Invoke(Run run, InvokeNative invoke, IntPtr target, IntPtr fixture, IntPtr helper, uint kind, uint mode, uint scenario, uint rawByte)
    {
        var result = new Result { Version = 1, Size = 208 };
        run.Check(invoke(target, kind, mode, scenario, rawByte, ref result) == 0 && result.Version == 1 && result.Size == 208
            && result.Kind == kind && result.Mode == mode && result.Scenario == scenario && result.Reserved == 0
            && result.NativeChecks > 0 && result.NativeFailures == 0 && result.OriginalCalls == 1 && result.OriginalCaller != IntPtr.Zero,
            "Actual patched fixture original/ABI checks failed.");
        run.Check(result.Returned == (mode == 0 ? 1u : 0u) && result.Caught == (mode == 0 ? 0u : 1u)
            && (mode != 0 || kind == 3 || result.ReturnByte == rawByte), "Raw AL changed or the original exception escaped its native catcher.");
        run.Check(VirtualQuery(result.OriginalCaller, out var caller, MemorySize) != 0 && caller.AllocationBase == helper,
            "The observed original was called outside the actual native helper.");
        if (mode == 1) run.Check(result.Code == 0xe04e4d53 && result.ParameterCount == 3 && result.Info0 == 0xfedcba9876543210
            && result.Info1 != 0 && result.Info2 == kind && result.ExceptionAddress != IntPtr.Zero, "Original native SEH record changed.");
        if (mode == 2) run.Check(result.Code == 0xe06d7363 && result.ParameterCount == 4 && result.Info0 == 0x19930520
            && result.Info1 != 0 && result.Info2 != 0 && result.Info3 == unchecked((ulong)fixture.ToInt64())
            && result.ExceptionAddress != IntPtr.Zero, "Original native typed C++ exception record changed.");
        return result;
    }
    private static void CheckCompletion(Run run, NativeEngineNodeCompletion completion, Result result, uint rawByte)
    {
        var expected = result.Mode == 0 ? NativeEngineNodeExitFlags.Returned
            | (result.Kind == 3 ? 0 : NativeEngineNodeExitFlags.ReturnByteValid) : NativeEngineNodeExitFlags.Abnormal;
        run.Check(completion.IsValidFor((NativeEngineNodeKind)result.Kind) && completion.Flags == expected
            && completion.RawReturn == (result.Mode == 0 && result.Kind != 3 ? rawByte : 0), "Actual completion lost original raw-byte/abnormal facts.");
        if (result.Kind == 0 && result.Mode == 0)
            run.Check(completion.OutputFlags == (NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read)
                && completion.StartOutputNode == result.OutputNode, "Start completion did not copy the actual native output slot.");
        else run.Check(completion.OutputFlags == 0 && completion.StartOutputNode == IntPtr.Zero, "A non-Start/abnormal After invented an output node.");
    }
    private sealed class Main : IMainThreadDispatcher
    { private readonly int _thread = Environment.CurrentManagedThreadId; public bool IsMainThread => Environment.CurrentManagedThreadId == _thread; public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Engine-node fixture left its actual callback thread."); } public Task InvokeAsync(Action body, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
    private static NativeEngineNodeInformation NewInfo() => new() { Version = 1, Size = 112 };
    private static NativeEngineNodeInformation ReadInfo(Run run, InfoNative read, uint kind)
    { var value = NewInfo(); run.Check(read(kind, Slot, ref value) == 0, "Native engine-node information is unavailable."); return value; }
    private static string AbsoluteEnv(string name) { var path = Environment.GetEnvironmentVariable(name); if (path is null || !Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new ArgumentException("Missing absolute fixture file: " + name); return Path.GetFullPath(path); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    private static byte[] Bytes(IntPtr pointer, int length) { var bytes = new byte[length]; Marshal.Copy(pointer, bytes, 0, length); return bytes; }
    private static bool Reaches(IntPtr pointer, IntPtr expected)
    {
        var visited = new HashSet<IntPtr>();
        for (var i = 0; i < 4; ++i)
        {
            if (pointer == expected) return true;
            if (pointer == IntPtr.Zero || !visited.Add(pointer)) return false;
            HookTargetValidation.VerifyReadable(pointer, 16);
            if (Marshal.ReadByte(pointer) == 0xe9) pointer = IntPtr.Add(pointer, 5 + Marshal.ReadInt32(pointer, 1));
            else if (Marshal.ReadByte(pointer) == 0xff && Marshal.ReadByte(pointer, 1) == 0x25)
            { var slot = IntPtr.Add(pointer, 6 + Marshal.ReadInt32(pointer, 2)); HookTargetValidation.VerifyReadable(slot, 8); pointer = Marshal.ReadIntPtr(slot); }
            else if (Marshal.ReadByte(pointer) == 0x48 && Marshal.ReadByte(pointer, 1) == 0xb8 && Marshal.ReadByte(pointer, 10) == 0xff && Marshal.ReadByte(pointer, 11) == 0xe0)
                pointer = Marshal.ReadIntPtr(pointer, 2);
            else return false;
        }
        return pointer == expected;
    }
    private static bool Rejected<T>(Action callback) where T : Exception { try { callback(); return false; } catch (T) { return true; } }
    private static bool NoGame() => GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && GetModuleHandle("UnityPlayer.dll") == IntPtr.Zero;
    private static nuint MemorySize => (nuint)Marshal.SizeOf<MemoryInformation>();
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll")] private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation information, nuint size);
    [DllImport("kernel32.dll")] private static extern int VirtualProtect(IntPtr address, nuint size, uint protection, out uint previous);
    [DllImport("kernel32.dll")] private static extern int FlushInstructionCache(IntPtr process, IntPtr address, nuint size);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
}
