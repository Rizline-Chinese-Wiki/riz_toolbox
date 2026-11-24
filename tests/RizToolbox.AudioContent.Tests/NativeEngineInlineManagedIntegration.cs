using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Independent process entry. Native side records here are delivered through
// the actual configured helper exports and CLR callbacks, never synthesized.
// Tracker providers remain controlled scalar identities, not Unity objects.
internal static class NativeEngineInlineManagedIntegration
{
    internal const string ModeArgument = "--engine-inline-managed";
    internal static readonly string[] Modes = { "free", "retained", "nested", "address-reuse" };
    private static readonly List<object> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct WorkDescriptor
    { internal uint Id, Retained; internal IntPtr Owner, Enumerator; internal ulong Handle; }
    [StructLayout(LayoutKind.Sequential)] private struct Descriptor
    {
        internal uint Version, Size, Scenario, Slot, WorkCount, Configured, MainThreadId, Failures;
        internal IntPtr ImageBase, Kernel, InlineEntry, RelayBase;
        internal NativeEngineInlineSite Begin, EndFree, EndRetained;
        internal WorkDescriptor Work0, Work1;
        internal uint NativeChecks, Reserved;
        internal WorkDescriptor Work(int index) => index == 0 ? Work0 : Work1;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WorkReceipt
    {
        internal uint Id, Retained, Starts, Executes, BeginReplay, Ends, Freed, RetainedCleanup;
        internal IntPtr Owner, Enumerator, Node, Output;
        internal ulong Handle;
        internal uint RawReturn, Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Receipt
    {
        internal uint Version, Size, Scenario, WorkCount, Checks, Failures, FreeCount, RetainedCount;
        internal uint AddressReuse, OriginalStarts, OriginalExecutes, Reserved;
        internal IntPtr ImageBase, RelayBase;
        internal WorkReceipt Work0, Work1;
        internal WorkReceipt Work(int index) => index == 0 ? Work0 : Work1;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInformation
    { internal IntPtr BaseAddress, AllocationBase; internal uint AllocationProtect; internal ushort PartitionId; internal nuint RegionSize; internal uint State, Protect, Type; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(
        [MarshalAs(UnmanagedType.LPWStr)] string helperPath, uint scenario, ref Descriptor descriptor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint RunNative(ref Receipt receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NodeInfoNative(uint kind, uint slot, ref NativeEngineNodeInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InlineInfoNative(uint slot, ref NativeEngineInlineInformation info);
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
    private sealed class Provider
    {
        internal readonly object Owner = new();
        internal readonly long Receiver;
        internal readonly WorkDescriptor Work;
        internal int Reads;
        internal Provider(long receiver, WorkDescriptor work) => (Receiver, Work) = (receiver, work);
        internal NativeCoroutineCancellationIdentity Row => new(Owner, Receiver, 0, 1, Work.Enumerator.ToInt64(), 0,
            false, false, true, false, false, true);
        internal NativeCoroutineCancellationIdentityProvider Description => new(Owner, Receiver,
            () => { ++Reads; return new[] { Row }; }, identity => Row.SameRecord(identity));
    }
    private sealed class NodeScope : IDisposable
    {
        internal readonly NativeEngineCoroutineIdentityLedger.Call Call;
        private readonly Action<NodeScope> _disposed;
        internal NodeScope(NativeEngineCoroutineIdentityLedger.Call call, Action<NodeScope> disposed) => (Call, _disposed) = (call, disposed);
        public void Dispose() { Call.Dispose(); _disposed(this); }
    }

    internal static int RunChild(string[] args)
    {
        var mode = args.Length == 2 ? args[1] : "invalid";
        var checks = 0;
        var faults = new List<string>();
        void Check(bool value, string reason) { ++checks; if (!value) throw new InvalidOperationException(reason); }
        try
        {
            SetErrorMode(0x8003);
            var scenario = Array.IndexOf(Modes, mode);
            Check(args.Length == 2 && args[0] == ModeArgument && scenario >= 0, "Choose one exact independent managed inline child mode.");
            Check(OperatingSystem.IsWindows() && IntPtr.Size == 8 && GetModuleHandle("UnityPlayer.dll") == IntPtr.Zero
                && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero, "Use an isolated Windows x64 child without game modules.");
            Check(Marshal.SizeOf<Descriptor>() == 184 && Marshal.SizeOf<Receipt>() == 224
                && Marshal.SizeOf<WorkDescriptor>() == 32 && Marshal.SizeOf<WorkReceipt>() == 80
                && Marshal.SizeOf<NativeEngineInlineFrame>() == 144, "The exact owned native/managed fixture layouts changed.");
            var helperPath = AbsoluteEnvironment("RIZ_ENGINE_INLINE_MANAGED_HELPER");
            var fixturePath = AbsoluteEnvironment("RIZ_ENGINE_INLINE_MANAGED_FIXTURE");
            var files = new[] { helperPath, fixturePath }.ToDictionary(path => path, Hash);
            foreach (var path in files.Keys) Owners.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            var helper = NativeLibrary.Load(helperPath); var fixture = NativeLibrary.Load(fixturePath);
            Owners.Add(helper); Owners.Add(fixture);
            var configure = Export<ConfigureNative>(fixture, "RizEngineInlineManaged_Configure");
            var run = Export<RunNative>(fixture, "RizEngineInlineManaged_Run");
            var nodeInfo = Export<NodeInfoNative>(helper, "RizEngineNodeContext_GetInfo");
            var inlineInfo = Export<InlineInfoNative>(helper, "RizEngineNodeContext_GetInlineInfo");
            var descriptor = new Descriptor { Version = 1, Size = 184 };
            Check(configure(helperPath, checked((uint)scenario), ref descriptor) == 0 && descriptor.Version == 1
                && descriptor.Size == 184 && descriptor.Configured == 1 && descriptor.Failures == 0 && descriptor.Reserved == 0
                && descriptor.Scenario == scenario && descriptor.WorkCount == (scenario >= 2 ? 2u : 1u)
                && descriptor.ImageBase == fixture && descriptor.Kernel == helper && descriptor.Slot == 26
                && descriptor.MainThreadId == NativeEngineInlinePlatform.CurrentOsThreadId,
                "The owned DLL did not configure its own source module, four typed channels and exact side family.");
            foreach (var site in new[] { descriptor.Begin, descriptor.EndFree, descriptor.EndRetained })
                Check(VirtualQuery(site.SitePc, out var memory, MemorySize) == MemorySize && memory.AllocationBase == fixture
                    && memory.Type == 0x1000000 && site.RelayReturnPc != IntPtr.Zero,
                    "An instruction source is outside the actual owned fixture DLL.");
            var log = new Logger(); var bridges = new NativeEngineNodeContextBridge[4];
            var configured = new NativeEngineNodeInformation[4];
            for (var kind = 0; kind != 4; ++kind)
            {
                var info = new NativeEngineNodeInformation { Version = 1, Size = 112 };
                Check(nodeInfo(checked((uint)kind), descriptor.Slot, ref info) == 0 && info.Attached == 0 && info.Unknown == 0
                    && info.Entered == 0 && info.Exited == 0 && info.InFlight == 0,
                    "The native fixture must configure once and leave every physical Attach to CLR.");
                configured[kind] = info;
                bridges[kind] = NativeEngineNodeContextBridge.AdoptConfigured(helperPath, info.Identity, log);
            }
            var sideInformation = new NativeEngineInlineInformation { Version = 1, Size = 160 };
            Check(inlineInfo(descriptor.Slot, ref sideInformation) == 0 && sideInformation.Attached == 0
                && sideInformation.Unknown == 0 && sideInformation.StartsEntered == 0 && sideInformation.Events == 0,
                "The native side family must be unused and unattached before managed adoption.");
            var sideIdentity = new NativeEngineInlineChannelIdentity(descriptor.Slot, sideInformation.Epoch, descriptor.MainThreadId,
                descriptor.InlineEntry, configured[0].Identity, descriptor.Begin, descriptor.EndFree, descriptor.EndRetained);
            var sideBridge = NativeEngineInlineContextBridge.AdoptConfigured(helperPath, sideIdentity, log);
            var runtime = NativeEngineRuntimeCoordinator.ShareControlled(new object(), bridges, log, sideBridge);
            runtime.RequireInlineObservation();
            using var main = new MainThreadDispatcher();
            var nodes = new NativeEngineCoroutineNodeLedger(main);
            var providers = Enumerable.Range(0, checked((int)descriptor.WorkCount)).Select(index => new Provider(101 + index, descriptor.Work(index))).ToArray();
            var identities = new NativeEngineCoroutineIdentityLedger(main, nodes, providers.Select(value => value.Description).ToArray(),
                providers.Select(value => new NativeEngineCoroutineOwnerPair(value.Receiver, value.Work.Owner.ToInt64())).ToArray());
            var inline = new NativeEngineCoroutineInlineLedger(main, identities, nodes);
            var nodeFrames = new List<NativeEngineNodeFrame>(); var completions = new List<NativeEngineNodeCompletion>();
            var sideFrames = new List<NativeEngineInlineFrame>();
            var disposed = 0; var startsCompletedBeforeDispose = 0; var startsFinalAfterDispose = 0;
            var activeExecuteObserved = 0; var afterFreeEndpoints = 0; var retainedEndpoints = 0;
            using var subscription = runtime.BindWithInline(frame => {
                Check(frame.IsValid && frame.Kind is NativeEngineNodeKind.Start or NativeEngineNodeKind.Execute,
                    "Only real Start and synchronous Execute should run in this normal-path fixture.");
                Check(VirtualQuery(frame.Caller, out var caller, MemorySize) == MemorySize && caller.AllocationBase == fixture,
                    "The physical typed call did not originate in the actual owned native DLL.");
                nodeFrames.Add(frame); var call = identities.Capture(frame);
                if (frame.Kind == NativeEngineNodeKind.Execute)
                {
                    Check(call.NodeCall?.Node is { Executions: 1, OpenCalls: 1 } node && node.Start.Frame.Kind == NativeEngineNodeKind.Start
                        && !node.Start.Completed && !node.Start.Disposed && frame.ReferenceCount == 1,
                        "Actual synchronous Execute did not bind its native node generation before Start returned.");
                    ++activeExecuteObserved;
                }
                return new NodeScope(call, scope => {
                    ++disposed;
                    if (scope.Call.Frame.Kind == NativeEngineNodeKind.Start)
                    {
                        Check(scope.Call.Disposed && scope.Call.Identity?.Node is { FinalCleanup: true, InlineCleanupUnproven: false,
                            Executions: 0, OpenCalls: 0 } && !nodes.Unknown,
                            "Inline cleanup was not confirmed after the exact actual Start Dispose.");
                        ++startsFinalAfterDispose;
                    }
                });
            }, (scope, frame, completion) => {
                Check(completion.IsValidFor(frame.Kind), "The actual typed completion ABI differs.");
                identities.Complete(scope.Call, frame, completion); completions.Add(completion);
                if (frame.Kind == NativeEngineNodeKind.Start)
                {
                    Check(scope.Call.Completed && !scope.Call.Disposed && scope.Call.Identity?.Node is { FinalCleanup: false,
                        InlineCleanupUnproven: true } && completion.StartOutputNode == IntPtr.Zero,
                        "A side endpoint incorrectly skipped the actual Start Complete/Dispose barrier.");
                    ++startsCompletedBeforeDispose;
                }
            }, (_, reason) => { faults.Add(reason); }, receipt => {
                Check(receipt.IsCurrent && receipt.Frame.Kind == NativeEngineInlineKind.StartEntered,
                    "Inline Capture lacks its current actual native Start-issued receipt.");
                sideFrames.Add(receipt.Frame); return inline.Capture(receipt);
            }, (context, receipt) => {
                Check(receipt.IsCurrent, "A stale or synthetic side receipt reached the actual inline ledger.");
                sideFrames.Add(receipt.Frame); inline.Observe(context, receipt);
                if (receipt.Frame.Kind == NativeEngineInlineKind.EndFree)
                {
                    // Page-state query only: no node is dereferenced after the
                    // native original VirtualFree. End uses its Begin cache.
                    Check(VirtualQuery(receipt.Frame.Node, out var memory, MemorySize) == MemorySize && memory.State == 0x10000,
                        "The real EndFree callback did not follow a real native VirtualFree.");
                    ++afterFreeEndpoints;
                }
                if (receipt.Frame.Kind == NativeEngineInlineKind.EndRetained) ++retainedEndpoints;
            }, (frame, reason) => { faults.Add(reason); inline.ObserveFault(frame, reason); });
            Owners.AddRange(new object[] { runtime, bridges, sideBridge, subscription, nodes, identities, inline, providers });
            var result = new Receipt { Version = 1, Size = 224 };
            Check(run(ref result) == 0 && result.Version == 1 && result.Size == 224 && result.Scenario == scenario
                && result.WorkCount == descriptor.WorkCount && result.Failures == 0 && result.Checks > 0
                && result.ImageBase == fixture && result.RelayBase == descriptor.RelayBase && result.Reserved == 0,
                "The actual owned native body or real Begin replay failed.");
            var count = checked((int)descriptor.WorkCount);
            Check(faults.Count == 0 && !runtime.Unknown && runtime.ReadActivity() is { Known: true, InFlight: 0 }
                && !inline.Unknown && !nodes.Unknown && !identities.Unknown,
                "Native-to-CLR physical callbacks or actual ledger ownership became Unknown.");
            Check(nodeFrames.Count == count * 2 && completions.Count == count * 2 && disposed == count * 2
                && sideFrames.Count == count * 4 && activeExecuteObserved == count
                && startsCompletedBeforeDispose == count && startsFinalAfterDispose == count,
                "The actual native/managed pipeline omitted or repeated a physical context or cleanup duty.");
            var snapshot = identities.Read(); var expectedEndpoint = mode == "retained" ? NativeEngineInlineKind.EndRetained : NativeEngineInlineKind.EndFree;
            Check(snapshot.Outstanding == 0 && snapshot.Pending && !snapshot.EngineCancelled && snapshot.Identities.Count == count
                && snapshot.Nodes.Nodes.Count == count && snapshot.Nodes.Nodes.All(node => node.FinalCleanupObserved
                    && !node.InlineCleanupUnproven && node.InlineBranchObserved && node.InlineEndpoint == expectedEndpoint
                    && node.ActiveExecutions == 0 && node.OpenCalls == 0 && node.StartCompleted)
                && snapshot.Identities.All(identity => identity.NativeStartReturned && identity.StartDisposed)
                && inline.Read() is { Outstanding: 0 } inlineState && inlineState.Branches == count,
                "The real inline path did not confirm only the exact completed native node generations.");
            Check(result.OriginalStarts == count && result.OriginalExecutes == count && result.FreeCount == afterFreeEndpoints
                && result.RetainedCount == retainedEndpoints && (mode == "retained" ? retainedEndpoints == count : afterFreeEndpoints == count),
                "Actual native original, free and retained cleanup counts disagree with CLR endpoint callbacks.");
            for (var index = 0; index < count; ++index)
            {
                var work = result.Work(index); var expected = descriptor.Work(index);
                Check(work.Id == expected.Id && work.Owner == expected.Owner && work.Enumerator == expected.Enumerator
                    && work.Handle == expected.Handle && work.Starts == 1 && work.Executes == 1 && work.BeginReplay == 1
                    && work.Ends == 1 && work.RawReturn == 0 && work.Output == IntPtr.Zero && work.Reserved == 0
                    && (mode == "retained" ? work.Freed == 0 && work.RetainedCleanup == 1 : work.Freed == 1 && work.RetainedCleanup == 0),
                    "A native work receipt differs from its actual source arguments, allocation or branch.");
                var chain = snapshot.Nodes.Nodes.Single(node => node.NativeOwner == work.Owner.ToInt64());
                Check(chain.Node == work.Node.ToInt64() && chain.Enumerator == work.Enumerator.ToInt64() && chain.EnumeratorHandle == work.Handle,
                    "The actual native allocation/enumerator tuple did not reach the same actual node ledger generation.");
            }
            if (mode == "nested") Check(sideFrames.Any(frame => frame.Depth == 2 && frame.ParentStartSequence != 0)
                && sideFrames.Where(frame => frame.Depth == 2).All(frame => frame.ParentStartSequence == checked((ulong)snapshot.Nodes.Nodes[0].StartSequence)),
                "Nested native Start did not retain its exact actual Start parent sequence.");
            if (mode == "address-reuse") Check(result.AddressReuse == 1 && result.Work0.Node == result.Work1.Node
                && snapshot.Nodes.Nodes[0].Generation < snapshot.Nodes.Nodes[1].Generation,
                "Native address reuse did not create a new independently completed ledger generation.");
            var finalInfo = new NativeEngineInlineInformation { Version = 1, Size = 160 };
            Check(inlineInfo(descriptor.Slot, ref finalInfo) == 0 && finalInfo.Attached == 1 && finalInfo.Unknown == 0
                && finalInfo.StartsEntered == (ulong)count && finalInfo.StartsExited == (ulong)count && finalInfo.StartsInFlight == 0
                && finalInfo.Begins == (ulong)count && finalInfo.EndsFree == (ulong)afterFreeEndpoints
                && finalInfo.EndsRetained == (ulong)retainedEndpoints && finalInfo.Events == (ulong)(count * 4)
                && finalInfo.ProbesInFlight == 0 && finalInfo.Faults == 0 && finalInfo.SnapshotFaults == 0
                && finalInfo.OrphanProbes == 0 && finalInfo.UnmatchedStarts == 0,
                "The real native side family did not return to an exact known idle state.");
            for (var kind = 0; kind != 4; ++kind)
            {
                var info = new NativeEngineNodeInformation { Version = 1, Size = 112 };
                Check(nodeInfo(checked((uint)kind), descriptor.Slot, ref info) == 0 && info.Attached == 1 && info.Unknown == 0
                    && info.InFlight == 0 && info.Entered == (kind < 2 ? (ulong)count : 0) && info.Entered == info.Exited,
                    "One actual native physical channel was unattached, repeated or left in flight.");
            }
            Check(files.All(pair => Hash(pair.Key) == pair.Value), "The owned input DLL files changed on disk.");
            Console.WriteLine(JsonSerializer.Serialize(new {
                schemaVersion = 1, kind = "native-engine-inline-managed-integration", mode, passed = true, checks, failures = 0,
                nativeChecks = result.Checks, nativeFailures = result.Failures, actualNativeToClrCallbacksVerified = true,
                actualConfiguredBridgeAndDispatcherVerified = true, actualIdentityNodeInlineLedgerVerified = true,
                trackerIdentityProvidersControlled = true, actualTrackerBackendExecuted = false,
                configuredChannels = 4, attachedChannels = 4, nativeStarts = count, synchronousNativeExecutes = activeExecuteObserved,
                typedCompletions = completions.Count, typedDisposals = disposed, actualSideEvents = sideFrames.Count,
                sourceModuleIsOwnedDll = true, actualBeginReplayVerified = true, actualVirtualFreeEndpoints = afterFreeEndpoints,
                actualRetainedCleanupEndpoints = retainedEndpoints, nativeAddressReuse = result.AddressReuse == 1,
                actualStartCompleteBeforeDisposeVerified = startsCompletedBeforeDispose == count,
                finalCleanupAfterActualDisposeVerified = startsFinalAfterDispose == count,
                nativeInFlight = 0, ledgerOutstanding = snapshot.Outstanding, ledgerPending = snapshot.Pending,
                engineCancelled = false, engineCancellationCompleted = false, futureProducerClosureVerified = false,
                schedulerClosureVerified = false, productionAdmissionVerified = false, productionPublishedAdoptionUsed = false,
                gameExecuted = false, installed = false, nodeGenerations = snapshot.Nodes.Nodes,
                sideEvents = sideFrames.Select(frame => new { frame.Kind, frame.StartSequence, frame.ParentStartSequence, frame.Depth,
                    frame.EventSequence, node = frame.Node.ToInt64(), frame.RawStartReturn, flags = (uint)frame.Flags }).ToArray(),
                files = files.Select(pair => new { path = pair.Key, beforeSha256 = pair.Value, afterSha256 = Hash(pair.Key) }).ToArray()
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "native-engine-inline-managed-integration", mode,
                passed = false, checks, failures = 1, failure = error.ToString(), callbackFaults = faults,
                actualNativeToClrCallbacksVerified = false, actualIdentityNodeInlineLedgerVerified = false,
                engineCancelled = false, engineCancellationCompleted = false, futureProducerClosureVerified = false,
                productionAdmissionVerified = false, gameExecuted = false, installed = false }));
            return 1;
        }
    }
    private static string AbsoluteEnvironment(string name)
    {
        var path = Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Missing " + name);
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new InvalidOperationException("An existing absolute file is required: " + name);
        return Path.GetFullPath(path);
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    private static readonly nuint MemorySize = checked((nuint)Marshal.SizeOf<MemoryInformation>());
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll")] private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation info, nuint length);
}
