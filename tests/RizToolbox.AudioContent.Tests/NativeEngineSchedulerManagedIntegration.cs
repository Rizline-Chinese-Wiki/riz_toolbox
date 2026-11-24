using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Independent owned-DLL child mode. Native scopes, rows, snapshots and return
// values come from actual configured helper entries. Only tracker identity
// providers are controlled; they do not issue native completion receipts.
internal static class NativeEngineSchedulerManagedIntegration
{
    internal const string ModeArgument = "--engine-scheduler-managed";
    internal static readonly string[] Modes = { "normal", "high-eax", "repeat", "multirow", "remove", "drain", "address-reuse" };
    private static readonly List<object> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Descriptor
    {
        internal uint Version, Size, Scenario, Slot, NodeSlot, Configured, MainThread, Checks, Failures, Reserved;
        internal IntPtr Image, Kernel, RowEntry, Relay, Manager, Tree, Owner, Enumerator, Callback, Cleanup;
        internal ulong Handle;
        internal NativeEngineSchedulerSite Begin, End;
        internal IntPtr PumpCallback, RepeatCallback, DrainCallback, RemoveCleanup, PumpCleanup, DrainCleanup;
        internal IntPtr EntryReservation;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RowReceipt
    {
        internal ulong Id;
        internal IntPtr Entry, Payload;
        internal uint Registered, Consumed, Removed, Callback, Cleanup, StorageRetired, Repeat, Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Receipt
    {
        internal uint Version, Size, Scenario, Checks, Failures, Registrations, Consumes, Removals, Callbacks, Cleanups, Drains, Pumps;
        internal uint Gaps, FreedEntries, AddressReuse, RemainingEntries, NodeStarts, NodeExecutes, NodeReleases, NodeFreed, RowChecks, RowCount, Reserved0, Reserved1;
        internal IntPtr Image, Relay, Node;
        internal RowReceipt Row0, Row1, Row2, Row3, Row4, Row5, Row6, Row7;
        internal uint EntryDecommits, EntryRecommits, EntryReservationReleased, EntryGapState;
        internal IntPtr EntryReservation;
        internal uint EntryGapQuerySucceeded, Reserved2;
        internal RowReceipt Row(int index) => index switch {
            0 => Row0, 1 => Row1, 2 => Row2, 3 => Row3, 4 => Row4, 5 => Row5, 6 => Row6, 7 => Row7,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInformation
    {
        internal IntPtr BaseAddress, AllocationBase;
        internal uint AllocationProtect;
        internal ushort PartitionId;
        internal nuint RegionSize;
        internal uint State, Protect, Type;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(
        [MarshalAs(UnmanagedType.LPWStr)] string kernel, uint scenario, ref Descriptor descriptor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GapNative(IntPtr manager, IntPtr entry, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint RunNative(GapNative gap, IntPtr user, ref Receipt receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NodeInfoNative(uint kind, uint slot, ref NativeEngineNodeInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SchedulerInfoNative(uint kind, uint slot, ref NativeEngineSchedulerInformation info);
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
    private sealed class Provider
    {
        internal readonly object Owner = new();
        internal readonly Descriptor Descriptor;
        internal Provider(Descriptor descriptor) => Descriptor = descriptor;
        internal NativeCoroutineCancellationIdentity Identity => new(Owner, 101, 0, 1,
            Descriptor.Enumerator.ToInt64(), 0, false, false, true, false, false, true);
        internal NativeCoroutineCancellationIdentityProvider Description => new(Owner, 101,
            () => new[] { Identity }, identity => Identity.SameRecord(identity));
    }
    private sealed class NodeScope : IDisposable
    {
        internal readonly NativeEngineCoroutineIdentityLedger.Call Call;
        internal readonly NativeEngineSchedulerRowLedger.CleanupContext? Cleanup;
        private readonly Action<NodeScope> _after;
        internal NodeScope(NativeEngineCoroutineIdentityLedger.Call call,
            NativeEngineSchedulerRowLedger.CleanupContext? cleanup, Action<NodeScope> after)
            => (Call, Cleanup, _after) = (call, cleanup, after);
        public void Dispose()
        {
            Call.Dispose();
            Cleanup?.Dispose();
            _after(this);
        }
    }
    private sealed class SchedulerScope : IDisposable
    {
        internal readonly NativeEngineSchedulerRowLedger.Context Call;
        private readonly Action<SchedulerScope> _after;
        internal SchedulerScope(NativeEngineSchedulerRowLedger.Context call, Action<SchedulerScope> after)
            => (Call, _after) = (call, after);
        public void Dispose() { Call.Dispose(); _after(this); }
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
            Check(args.Length == 2 && args[0] == ModeArgument && scenario >= 0, "Use one exact scheduler integration child mode.");
            Check(OperatingSystem.IsWindows() && IntPtr.Size == 8 && GetModuleHandle("UnityPlayer.dll") == IntPtr.Zero
                && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero, "Use an isolated Windows x64 child without game modules.");
            Check(Marshal.SizeOf<Descriptor>() == 216 && Marshal.SizeOf<Receipt>() == 600 && Marshal.SizeOf<RowReceipt>() == 56
                && Marshal.SizeOf<NativeEngineSchedulerFrame>() == 256 && Marshal.SizeOf<NativeEngineSchedulerCompletion>() == 48
                && Marshal.SizeOf<NativeEngineSchedulerRowFrame>() == 224, "Owned native/managed scheduler layouts differ.");
            var helperPath = AbsoluteEnvironment("RIZ_ENGINE_SCHEDULER_MANAGED_HELPER");
            var fixturePath = AbsoluteEnvironment("RIZ_ENGINE_SCHEDULER_MANAGED_FIXTURE");
            var files = new[] { helperPath, fixturePath }.ToDictionary(path => path, Hash);
            foreach (var path in files.Keys) Owners.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            var helper = NativeLibrary.Load(helperPath);
            var fixture = NativeLibrary.Load(fixturePath);
            Owners.Add(helper); Owners.Add(fixture);
            var configure = Export<ConfigureNative>(fixture, "RizEngineSchedulerManaged_Configure");
            var run = Export<RunNative>(fixture, "RizEngineSchedulerManaged_Run");
            var readNode = Export<NodeInfoNative>(helper, "RizEngineNodeContext_GetInfo");
            var readScheduler = Export<SchedulerInfoNative>(helper, "RizEngineSchedulerContext_GetInfo");
            var descriptor = new Descriptor { Version = 2, Size = 216 };
            Check(configure(helperPath, checked((uint)scenario), ref descriptor) == 0 && descriptor.Version == 2
                && descriptor.Size == 216 && descriptor.Scenario == scenario && descriptor.Slot == 27 && descriptor.NodeSlot == 27
                && descriptor.Configured == 1 && descriptor.Checks > 0 && descriptor.Failures == 0 && descriptor.Reserved == 0
                && descriptor.Image == fixture && descriptor.Kernel == helper && descriptor.Relay != IntPtr.Zero
                && descriptor.MainThread == NativeEngineInlinePlatform.CurrentOsThreadId,
                "The owned DLL did not configure exact untouched node/scheduler channels on this actual thread.");
            if (mode == "address-reuse")
                Check(descriptor.EntryReservation != IntPtr.Zero
                    && VirtualQuery(descriptor.EntryReservation, out var reserved, MemorySize) == MemorySize
                    && reserved.State == 0x2000 && reserved.AllocationBase == descriptor.EntryReservation,
                    "The entry reuse test requires its own inaccessible address reservation before any CLR callbacks.");
            else Check(descriptor.EntryReservation == IntPtr.Zero, "Ordinary entry-free scenarios must not use the reuse reservation.");
            foreach (var site in new[] { descriptor.Begin, descriptor.End })
                Check(VirtualQuery(site.SitePc, out var memory, MemorySize) == MemorySize && memory.AllocationBase == fixture
                    && memory.Type == 0x1000000 && site.Complete,
                    "A Drain source instruction window lies outside the actual owned DLL.");
            foreach (var caller in new[] { descriptor.PumpCallback, descriptor.RepeatCallback, descriptor.DrainCallback,
                descriptor.RemoveCleanup, descriptor.PumpCleanup, descriptor.DrainCleanup })
                Check(VirtualQuery(caller, out var memory, MemorySize) == MemorySize && memory.AllocationBase == fixture
                    && (memory.Protect & 0xf0) != 0, "An exact actual CALL return PC is not in owned native code.");
            var log = new Logger();
            var nodeBridges = new NativeEngineNodeContextBridge[4];
            for (var kind = 0; kind != 4; ++kind)
            {
                var info = new NativeEngineNodeInformation { Version = 1, Size = 112 };
                Check(readNode(checked((uint)kind), descriptor.NodeSlot, ref info) == 0 && info.Attached == 0 && info.Unknown == 0
                    && info.Entered == 0 && info.Exited == 0 && info.InFlight == 0,
                    "Actual node channels must be configured but unused and unattached before CLR adopts them.");
                nodeBridges[kind] = NativeEngineNodeContextBridge.AdoptConfigured(helperPath, info.Identity, log);
            }
            var schedulerBridges = new NativeEngineSchedulerContextBridge[6];
            for (var kind = 0; kind != 6; ++kind)
            {
                var info = new NativeEngineSchedulerInformation { Version = 1, Size = 144 };
                Check(readScheduler(checked((uint)kind), descriptor.Slot, ref info) == 0 && info.Configured == 1 && info.Attached == 0
                    && info.Unknown == 0 && info.Entered == 0 && info.Exited == 0 && info.InFlight == 0
                    && info.RowsInFlight == 0 && info.RowBegins == 0 && info.RowEnds == 0,
                    "Actual scheduler channels must be unused and unattached before their physical CLR Attach.");
                var identity = new NativeEngineSchedulerChannelIdentity((NativeEngineSchedulerKind)kind,
                    descriptor.Slot, info.Epoch, descriptor.MainThread, info.Target, info.Original, info.Entry, info.RowEntry,
                    kind == 4 ? descriptor.Begin : default, kind == 4 ? descriptor.End : default);
                schedulerBridges[kind] = NativeEngineSchedulerContextBridge.AdoptConfigured(helperPath, identity, log);
            }
            // ShareControlled selects a fixture responsibility owner. Every
            // bridge above is still adopted from real loaded configured native
            // exports; no ControlledBridge.Send or synthetic observation exists.
            var runtime = NativeEngineRuntimeCoordinator.ShareControlled(new object(), nodeBridges, log,
                schedulerBridges: schedulerBridges);
            var dispatcher = runtime.RequireSchedulerObservation();
            using var main = new MainThreadDispatcher();
            var nodes = new NativeEngineCoroutineNodeLedger(main);
            var provider = new Provider(descriptor);
            var identities = new NativeEngineCoroutineIdentityLedger(main, nodes, new[] { provider.Description },
                new[] { new NativeEngineCoroutineOwnerPair(101, descriptor.Owner.ToInt64()) });
            var rows = new NativeEngineSchedulerRowLedger(main, identities, nodes, dispatcher, descriptor.Callback, descriptor.Cleanup,
                new NativeEngineSchedulerCallerProfile(descriptor.PumpCallback, descriptor.RepeatCallback, descriptor.DrainCallback,
                    descriptor.RemoveCleanup, descriptor.PumpCleanup, descriptor.DrainCleanup));
            var nodeFrames = new List<NativeEngineNodeFrame>();
            var nodeCompletions = new List<NativeEngineNodeCompletion>();
            var schedulerFrames = new List<NativeEngineSchedulerFrame>();
            var schedulerCompletions = new Dictionary<ulong, NativeEngineSchedulerCompletion>();
            var rowFrames = new List<NativeEngineSchedulerRowFrame>();
            var gapReceipts = new List<object>();
            var drainPageDiagnostics = new List<object>();
            var nodeDisposals = 0; var schedulerDisposals = 0; var cleanupCaptures = 0; var cleanupCompletes = 0; var cleanupDisposals = 0;
            var rootDisposals = 0; var rootCompleteStillRetained = 0; var gapChecks = 0; var gapRetirementGateChecks = 0;
            using var nodeBinding = runtime.Bind(frame => {
                Check(frame.IsValid && frame.Kind is NativeEngineNodeKind.Start or NativeEngineNodeKind.Execute or NativeEngineNodeKind.Release,
                    "An unexpected native node function entered the owned scenario.");
                Check(VirtualQuery(frame.Caller, out var memory, MemorySize) == MemorySize && memory.AllocationBase == fixture,
                    "A physical native node call did not originate in this owned fixture DLL.");
                nodeFrames.Add(frame);
                var call = identities.Capture(frame);
                var cleanup = frame.Kind == NativeEngineNodeKind.Release && call.NodeCall is { } actualNodeCall
                    ? rows.CaptureCleanup(actualNodeCall) : null;
                if (cleanup is not null) ++cleanupCaptures;
                return new NodeScope(call, cleanup, context => {
                    ++nodeDisposals;
                    Check(context.Call.Disposed, "Actual node Identity Dispose did not finish.");
                    if (context.Cleanup is not null)
                    {
                        ++cleanupDisposals;
                        Check(rows.Read().Records.Any(row => row.CleanupCompleted && row.State != NativeEngineSchedulerRowState.Retired),
                            "Actual cleanup disposal must still retain its row until the owning scheduler root exits.");
                    }
                });
            }, (context, frame, completion) => {
                Check(completion.IsValidFor(frame.Kind), "An actual node completion differs from its fixed ABI.");
                identities.Complete(context.Call, frame, completion);
                nodeCompletions.Add(completion);
                if (context.Cleanup is not null) { context.Cleanup.Complete(); ++cleanupCompletes; }
            }, (frame, reason) => { faults.Add(reason); identities.ObserveFault(frame, reason); });
            using var schedulerBinding = dispatcher.Bind(observation => {
                Check(observation.IsCurrent && observation.Completion is null && observation.Row is null && observation.Frame.IsValid,
                    "A scheduler Enter lacks its native-issued current physical observation.");
                var frame = observation.Frame;
                Check(VirtualQuery(frame.Caller, out var memory, MemorySize) == MemorySize && memory.AllocationBase == fixture,
                    "An actual scheduler call did not originate in this owned DLL.");
                schedulerFrames.Add(frame);
                return new SchedulerScope(rows.Capture(observation), context => {
                    ++schedulerDisposals;
                    Check(context.Call.Disposed, "Actual scheduler ledger context was not disposed.");
                    if (context.Call.Frame.Kind is NativeEngineSchedulerKind.Pump or NativeEngineSchedulerKind.Remove or NativeEngineSchedulerKind.Drain)
                    {
                        ++rootDisposals;
                        Check(!rows.Unknown, "Row retirement became Unknown after its actual root Dispose.");
                    }
                });
            }, (context, observation) => {
                Check(observation.IsCurrent && observation.Completion is { Normal: true },
                    "Scheduler completion is not the actual normal physical completion.");
                rows.Complete(context.Call, observation);
                schedulerCompletions.Add(observation.Frame.Sequence, observation.Completion!.Value);
                if (context.Call.Frame.Kind is NativeEngineSchedulerKind.Pump or NativeEngineSchedulerKind.Remove or NativeEngineSchedulerKind.Drain)
                {
                    Check(rows.Read().Records.Any(row => row.State != NativeEngineSchedulerRowState.Retired),
                        "A row retired before its actual owning root Dispose.");
                    ++rootCompleteStillRetained;
                }
            }, (context, observation) => {
                Check(observation.IsCurrent && observation.Row is { } frame && frame.IsValid && !frame.HasFault,
                    "Drain observation is not an exact current native row receipt.");
                var row = observation.Row!.Value;
                rowFrames.Add(row); rows.ObserveRow(context.Call, observation);
                if (row.Kind == NativeEngineSchedulerRowKind.End)
                {
                    var queried = VirtualQuery(row.Entry, out var memory, MemorySize) == MemorySize;
                    drainPageDiagnostics.Add(new { entry = row.Entry.ToInt64(), row.RowSequence, querySucceeded = queried,
                        pageStateDiagnostic = true, currentState = queried ? memory.State : 0u });
                    Check(rows.Read().Records.Any(value => value.Entry == row.Entry.ToInt64() && value.CleanupCompleted
                        && value.State != NativeEngineSchedulerRowState.Retired),
                        "Drain End skipped cleanup or retired before its outer actual Drain Dispose.");
                }
            }, (frame, reason) => { faults.Add(reason); rows.ObserveFault(frame, reason); });
            GapNative gap = (manager, entry, user) => {
                try
                {
                    Check(user == IntPtr.Zero && manager == descriptor.Manager && entry != IntPtr.Zero,
                        "The real consume/callback gap has unexpected owned arguments.");
                    var snapshot = rows.Read();
                    var pending = snapshot.Records.Where(row => row.Entry == entry.ToInt64()
                        && row.State == NativeEngineSchedulerRowState.ConsumedPendingCallback).ToArray();
                    Check(snapshot.Pending && snapshot.Outstanding > 0 && !snapshot.Unknown && pending.Length == 1
                        && pending[0].ConsumeSequence != 0 && pending[0].CallbackRawRax == 0 && !pending[0].CleanupCompleted,
                        "A real consumed row lost its callback delivery duty before callback began.");
                    Check(dispatcher.CurrentOriginalScope is { } scope && scope.Frame.Kind == NativeEngineSchedulerKind.Pump,
                        "The consume/callback gap has no actual still-open Pump scope.");
                    var selected = identities.Read().Identities.Single(identity => identity.Node == pending[0].Payload
                        && identity.EngineGeneration == pending[0].NodeGeneration && identity.StartSequence == pending[0].StartSequence);
                    Check(!rows.MayRetireNodeGeneration(selected) && !rows.Unknown && dispatcher.ReadActivity().Known,
                        "Actual native consume/callback gap must deny this exact identity generation's retirement without losing Known.");
                    ++gapRetirementGateChecks;
                    var queried = VirtualQuery(entry, out var memory, MemorySize) == MemorySize;
                    if (mode == "address-reuse")
                        Check(queried && memory.State == 0x2000 && entry == descriptor.EntryReservation && memory.AllocationBase == descriptor.EntryReservation
                            && memory.Protect is 0 or 1,
                            "Address reuse must decommit the real entry page while retaining its exact owned reservation.");
                    ++gapChecks;
                    gapReceipts.Add(new { entry = entry.ToInt64(), pending[0].RegistrationGeneration, pending[0].ConsumeSequence,
                        snapshot.Outstanding, snapshot.Pending, entryPageQuerySucceeded = queried, entryPageState = queried ? memory.State : 0u,
                        pageStateDiagnostic = mode != "address-reuse", entryStorageUnmapped = mode == "address-reuse" ? (bool?)true : null,
                        ownedReservationHeld = mode == "address-reuse", callbackNotEntered = true });
                }
                catch (Exception error) { faults.Add("Actual gap inspection: " + error); }
            };
            Owners.AddRange(new object[] { runtime, nodeBridges, schedulerBridges, dispatcher, nodes, identities, rows, provider,
                nodeBinding, schedulerBinding, gap });
            var native = new Receipt { Version = 2, Size = 600 };
            Check(run(gap, IntPtr.Zero, ref native) == 0 && native.Version == 2 && native.Size == 600
                && native.Scenario == scenario && native.Checks > 0 && native.Failures == 0 && native.Reserved0 == 0 && native.Reserved1 == 0 && native.Reserved2 == 0
                && native.Image == fixture && native.Relay == descriptor.Relay && native.RemainingEntries == 0,
                "The actual owned native scheduler body did not return a complete successful receipt.");
            var expectedRows = mode switch { "repeat" or "multirow" or "remove" or "address-reuse" => 2, "drain" => 3, _ => 1 };
            var expectedCallbacks = mode switch { "remove" => 0, "repeat" => 1, "drain" => 2, _ => expectedRows };
            var expectedCleanups = mode switch { "high-eax" or "address-reuse" => 0, "repeat" => 1, _ => expectedRows };
            var expectedReleases = Math.Max(1, expectedCleanups);
            var expectedGaps = mode is "remove" or "drain" ? 0 : expectedCallbacks;
            var final = rows.Read();
            var nodeFinal = identities.Read();
            Check(faults.Count == 0 && !rows.Unknown && !identities.Unknown && !nodes.Unknown && !dispatcher.Unknown
                && runtime.ReadActivity() is { Known: true, InFlight: 0 } && dispatcher.ReadActivity() is { Known: true, InFlight: 0 },
                "The actual Native-to-CLR dispatcher or shared identity/node/row ledger became Unknown or stayed open: "
                    + string.Join("; ", faults) + "; " + final.Failure);
            Check(final.Rows == expectedRows && final.Records.Count == expectedRows && final.Outstanding == 0 && !final.Pending
                && !final.EngineCancelled && !final.FutureClosed && final.Records.All(row => row.State == NativeEngineSchedulerRowState.Retired
                    && row.StartSequence != 0 && row.NodeGeneration > 0 && !row.NativeReferenceAdded),
                "Actual registered generations were not retired only after callback/cleanup and outer scope disposal.");
            var expectedReleasedEntries = mode == "address-reuse" ? 0 : expectedRows;
            Check(native.Registrations == expectedRows && native.FreedEntries == expectedReleasedEntries && native.RowCount == expectedRows
                && native.Callbacks == expectedCallbacks && native.Cleanups == expectedCleanups && native.Gaps == expectedGaps
                && gapChecks == expectedGaps && native.NodeStarts == 1 && native.NodeExecutes == 1
                && native.NodeReleases == expectedReleases && native.NodeFreed == 1 && native.AddressReuse == (mode == "address-reuse" ? 1u : 0u),
                "Actual native originals/entry frees/complete callback EAX/cleanup/node release counts disagree.");
            Check(native.EntryDecommits == (mode == "address-reuse" ? 2u : 0u)
                && native.EntryRecommits == (mode == "address-reuse" ? 1u : 0u)
                && native.EntryReservationReleased == (mode == "address-reuse" ? 1u : 0u)
                && native.EntryReservation == descriptor.EntryReservation && native.EntryGapQuerySucceeded <= 1
                && (expectedGaps != 0 || native.EntryGapQuerySucceeded == 0 && native.EntryGapState == 0)
                && (mode != "address-reuse" || native.EntryGapQuerySucceeded == 1 && native.EntryGapState == 0x2000),
                "Native reservation/decommit/recommit and gap page-state receipts disagree with the exact scenario.");
            Check(cleanupCaptures == expectedCleanups && cleanupCompletes == expectedCleanups && cleanupDisposals == expectedCleanups
                && final.Records.Count(row => row.CleanupCompleted) == expectedCleanups
                && nodeFrames.Count == 2 + expectedReleases && nodeCompletions.Count == nodeFrames.Count
                && nodeDisposals == nodeFrames.Count && schedulerFrames.Count == schedulerCompletions.Count
                && schedulerDisposals == schedulerFrames.Count && rootDisposals == rootCompleteStillRetained,
                "Actual completion/disposal duties were omitted, repeated, or replaced with a row-only marker.");
            Check(nodeFinal.Outstanding == 0 && !nodeFinal.Unknown && nodeFinal.Identities.Count == 1
                && nodeFinal.Identities.Single() is { NativeStartReturned: true, StartDisposed: true }
                && nodeFinal.Nodes.Nodes.Count == 1 && nodeFinal.Nodes.Nodes.Single() is { FinalCleanupObserved: true,
                    ActiveExecutions: 0, OpenCalls: 0, StartCompleted: true, InlineCleanupUnproven: false },
                "The same actual identity/node ledger did not observe native Start return and final Release completion.");
            var nodeGeneration = nodeFinal.Nodes.Nodes.Single();
            Check(final.Records.All(row => row.NodeGeneration == nodeGeneration.Generation && row.StartSequence == (ulong)nodeGeneration.StartSequence
                && row.Payload == native.Node.ToInt64() && row.Tree == descriptor.Tree.ToInt64()),
                "Scheduler rows borrowed a different or synthetic native node generation.");
            var actualSelected = nodeFinal.Identities.Single(identity => identity.Node == nodeGeneration.Node
                && identity.EngineGeneration == nodeGeneration.Generation && identity.StartSequence == (ulong)nodeGeneration.StartSequence);
            Check(rows.MayRetireNodeGeneration(actualSelected) && !rows.Unknown && dispatcher.ReadActivity().Known
                && gapRetirementGateChecks == expectedGaps,
                "The exact actual identity generation remained blocked after all of its observed scheduler rows retired.");
            for (var index = 0; index != expectedRows; ++index)
            {
                var raw = native.Row(index);
                var managedRow = final.Records[index];
                Check(raw.Id == (ulong)(index + 1) && raw.Registered == 1 && raw.StorageRetired == 1 && raw.Reserved == 0
                    && raw.Entry.ToInt64() == managedRow.Entry && raw.Payload.ToInt64() == managedRow.Payload
                    && (raw.Cleanup == 1) == managedRow.CleanupCompleted && (raw.Consumed != 0) == (managedRow.ConsumeSequence != 0),
                    "A retained row generation differs from its actual owned native record allocation.");
            }
            if (mode == "high-eax")
                Check(final.Records.Single().CallbackRawRax == 0xabcdef0112340001UL && !final.Records.Single().CleanupCompleted,
                    "Callback EAX was bool-normalized or incorrectly triggered cleanup.");
            if (mode == "repeat")
                Check(final.Records.Count(row => row.RepeatCopy) == 1 && !final.Records[0].CleanupCompleted
                    && final.Records[1].CleanupCompleted && native.Consumes == 1 && native.Removals == 1,
                    "Repeat did not create a new actual registration before consuming its old row.");
            if (mode == "address-reuse")
                Check(final.Records[0].Entry == final.Records[1].Entry
                    && final.Records[0].RegistrationGeneration < final.Records[1].RegistrationGeneration,
                    "Actual entry address reuse failed to produce a newer independent registration generation.");
            Check(rowFrames.Count == (mode == "drain" ? 6 : 0) && native.RowChecks == (mode == "drain" ? 3u : 0u),
                "Actual native Drain row Begin/End probes were omitted or repeated.");
            ulong nativeInFlight = 0;
            var schedulerInformation = new List<object>();
            for (var kind = 0; kind != 6; ++kind)
            {
                var info = new NativeEngineSchedulerInformation { Version = 1, Size = 144 };
                Check(readScheduler(checked((uint)kind), descriptor.Slot, ref info) == 0 && info.Configured == 1 && info.Attached == 1
                    && info.Unknown == 0 && info.Entered == info.Exited && info.InFlight == 0 && info.Faults == 0
                    && info.SnapshotFaults == 0 && info.RowsInFlight == 0 && info.RowBegins == info.RowEnds && info.UnmatchedRows == 0
                    && info.Entered == (ulong)schedulerFrames.Count(frame => (int)frame.Kind == kind),
                    "One real scheduler channel was unattached, unbalanced or left in flight.");
                nativeInFlight += info.InFlight + info.RowsInFlight;
                schedulerInformation.Add(new { kind, info.Epoch, info.Entered, info.Exited, info.InFlight, info.RowBegins, info.RowEnds, info.RowsInFlight });
            }
            for (var kind = 0; kind != 4; ++kind)
            {
                var info = new NativeEngineNodeInformation { Version = 1, Size = 112 };
                Check(readNode(checked((uint)kind), descriptor.NodeSlot, ref info) == 0 && info.Attached == 1 && info.Unknown == 0
                    && info.Entered == info.Exited && info.InFlight == 0
                    && info.Entered == (ulong)nodeFrames.Count(frame => (int)frame.Kind == kind),
                    "One real node channel was unattached or left a physical completion duty open.");
                nativeInFlight += info.InFlight;
            }
            Check(files.All(pair => Hash(pair.Key) == pair.Value), "Owned native fixture inputs changed on disk.");
            Console.WriteLine(JsonSerializer.Serialize(new {
                schemaVersion = 1, kind = "native-engine-scheduler-managed-integration", mode, passed = true, checks, failures = 0,
                ownedFixtureAbi = 2,
                nativeChecks = native.Checks, nativeFailures = native.Failures, actualNativeToClrCallbacksVerified = true,
                actualSchedulerDispatcherVerified = true, actualSchedulerRowLedgerVerified = true, actualNodeIdentityLedgerVerified = true,
                sourceModuleIsOwnedDll = true, actualCallerProfilesVerified = true, allRowsRetiredAfterRootDisposeVerified = true,
                actualNodeRetirementGateVerified = true, gapRetirementGateChecks, nodeGenerationMayRetire = true,
                trackerIdentityProvidersControlled = true, actualTrackerBackendExecuted = false,
                configuredSchedulerChannels = 6, attachedSchedulerChannels = 6, configuredNodeChannels = 4, attachedNodeChannels = 4,
                nativeStarts = native.NodeStarts, nativeExecutes = native.NodeExecutes, nativeReleases = native.NodeReleases,
                nativeCallbacks = native.Callbacks, nativeCleanups = native.Cleanups, nativeRegistrations = native.Registrations,
                nativeFreedEntries = native.FreedEntries, nativeAddressReuse = native.AddressReuse == 1, nativeNodeFreed = native.NodeFreed == 1,
                nativeEntryDecommits = native.EntryDecommits, nativeEntryRecommits = native.EntryRecommits,
                nativeEntryReservationReleased = native.EntryReservationReleased == 1, nativeEntryGapState = native.EntryGapState,
                nativeEntryGapQuerySucceeded = native.EntryGapQuerySucceeded == 1,
                nativeEntryGapPageStateDiagnostic = mode != "address-reuse",
                entryReservation = descriptor.EntryReservation.ToInt64(),
                addressReuseWithinOwnedReservationOnly = mode == "address-reuse",
                systemAddressReleasedAndReacquiredVerified = false,
                oldGenerationSystemReleaseSuccessVerified = mode != "address-reuse",
                oldGenerationReleaseEvidence = "Exact successful native VirtualFree return, per-row StorageRetired and independent release counters; not current page state after CLR callbacks.",
                actualGapChecks = gapChecks, actualDrainRows = native.RowChecks,
                nodeDisposals, schedulerDisposals, cleanupCaptures, cleanupCompletes, cleanupDisposals,
                nativeInFlight, dispatcherInFlight = dispatcher.ReadActivity().InFlight,
                ledgerOutstanding = final.Outstanding, ledgerPending = final.Pending, ledgerEngineCancelled = final.EngineCancelled,
                ledgerFutureClosed = final.FutureClosed, nodeLedgerUnknown = nodeFinal.Unknown,
                engineCancelled = false, engineCancellationCompleted = false, futureProducerClosureVerified = false,
                schedulerClosureVerified = false, productionAdmissionVerified = false, productionPublishedAdoptionUsed = false,
                gameExecuted = false, installed = false, rowGenerations = final.Records, nodeGenerations = nodeFinal.Nodes.Nodes,
                gaps = gapReceipts, drainPageDiagnostics, schedulerInformation,
                schedulerCalls = schedulerFrames.Select(frame => new { frame.Kind, frame.Sequence, frame.ParentSequence,
                    frame.RootSequence, frame.SourceSequence, tree = frame.Tree.ToInt64(), manager = frame.Manager.ToInt64(),
                    entry = frame.Entry.ToInt64(), caller = frame.Caller.ToInt64(), flags = (uint)frame.Flags,
                    rawRax = schedulerCompletions[frame.Sequence].RawRax }).ToArray(),
                drainRows = rowFrames.Select(frame => new { frame.Kind, frame.DispatchSequence, frame.RowSequence,
                    frame.EventSequence, entry = frame.Entry.ToInt64(), flags = (uint)frame.Flags }).ToArray(),
                files = files.Select(pair => new { path = pair.Key, beforeSha256 = pair.Value, afterSha256 = Hash(pair.Key) }).ToArray()
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new {
                schemaVersion = 1, kind = "native-engine-scheduler-managed-integration", mode, passed = false, checks, failures = 1,
                failure = error.ToString(), callbackFaults = faults, actualNativeToClrCallbacksVerified = false,
                actualSchedulerRowLedgerVerified = false, engineCancelled = false, engineCancellationCompleted = false,
                futureProducerClosureVerified = false, schedulerClosureVerified = false, productionAdmissionVerified = false,
                gameExecuted = false, installed = false
            }));
            return 1;
        }
    }
    private static string AbsoluteEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Missing " + name);
        if (!Path.IsPathFullyQualified(value) || !File.Exists(value)) throw new InvalidOperationException("An existing absolute file is required: " + name);
        return Path.GetFullPath(value);
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    private static readonly nuint MemorySize = checked((nuint)Marshal.SizeOf<MemoryInformation>());
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll")] private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation info, nuint length);
}
