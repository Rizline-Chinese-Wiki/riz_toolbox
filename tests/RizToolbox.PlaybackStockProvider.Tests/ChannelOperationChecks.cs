using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StockProviderChecks
{
    private const string ChannelFixtureHash = "9fe8cde32ae1ce1fe9426efafb46a758d4e37593007a64747a042fdcab51bd6e";
    private static readonly string[] ChannelModes =
    {
        "operation-normal", "operation-getter-run", "operation-getter-source",
        "operation-getter-media-source", "operation-getter-dsp-run",
        "operation-writer-equal", "operation-writer-aba", "operation-write-throw",
        "operation-readback-once", "operation-readback-confirm-once", "operation-readback-always",
        "operation-readback-mismatch", "operation-readback-writer-equal", "operation-readback-writer-aba",
        "operation-readback-source", "operation-readback-progress", "operation-readback-dsp-once",
        "operation-readback-confirm-always", "operation-readback-confirm-drift",
        "operation-pitch-source-busy", "operation-pitch-source-preparation-close",
        "operation-final-graph-writer-equal", "operation-final-graph-writer-aba",
        "operation-final-graph-native-fault",
        "operation-publication-fault", "operation-publication-inflight", "operation-inflight", "operation-native-epoch",
        "operation-audit", "operation-install-audit", "operation-gettime-readback-source",
        "operation-clock-activation-drift", "operation-clock-activation-commit-drift"
    };

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ChannelReadOwner(IntPtr owner, ref NativePlaybackSourceInformation information);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ChannelSetArmed(IntPtr owner, ulong epoch, int armed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ChannelMarkFault(IntPtr owner, ulong epoch);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ChannelNativeAction();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ChannelNativeWait();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ChannelReadSingleStats(uint slot, ref ChannelSingleStats stats);
    [StructLayout(LayoutKind.Sequential)]
    private struct ChannelSingleStats
    {
        internal uint Version, Size;
        internal ulong Entered, Exited, InFlight, OriginalCalls, EnterFaults, ExitFaults,
            SourceFaults, Applied, Declined, Withdrawals;
        internal uint Unknown, Configured, ObserverEnabled, AuthorityActive;
        internal ulong PolicyGeneration;
    }
    [DllImport("kernel32", EntryPoint = "VirtualProtect", SetLastError = true)]
    private static extern bool ChannelProtect(IntPtr address, nuint size, uint protection, out uint previous);

    // Reflection retrieves actual retained owners and root handles only. Counters are
    // read through their diagnostics API or the real native information export.
    private static T ChannelPrivate<T>(object owner, string field) =>
        (T)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private sealed record ChannelSnapshot(long FullAudits, long FullImageAudits, long ClockChecks,
        long RateAdmissions, long SourceChecks, int DspReads, int PitchReads, int MediaReads,
        int AudioDspReads, int ClipReads, int FieldReads,
        int NativePitchCalls, int NativeTimeCalls, ulong WriterFence, ulong SourceFence, ulong SchedulingFence,
        ulong Entered, ulong Exited, ulong SourceInFlight, uint NativeFlags, ulong NativeFaults);
    private sealed record ChannelSingleSnapshot(ulong Entered, ulong Exited, ulong InFlight, ulong OriginalCalls,
        ulong EnterFaults, ulong ExitFaults, ulong SourceFaults, ulong Applied, ulong Declined, ulong Withdrawals,
        uint Unknown, uint Configured, uint ObserverEnabled, uint AuthorityActive, ulong PolicyGeneration);

    private sealed class ChannelCodeMutation : IDisposable
    {
        private readonly IntPtr _address;
        private readonly byte _before;
        private bool _restored;
        internal ChannelCodeMutation(IntPtr address)
        {
            _address = address; _before = Marshal.ReadByte(address);
            Check(_before == 0x90, "The hash-pinned unused Replay padding byte is not NOP.");
            Check(ChannelProtect(address, 1, 0x40, out var previous), "The owned fixture padding page is not writable.");
            try { Marshal.WriteByte(address, (byte)(_before ^ 1)); }
            finally { Check(ChannelProtect(address, 1, previous, out _), "The owned padding page protection was not restored before operation checks."); }
        }
        public void Dispose()
        {
            if (_restored) return;
            Check(ChannelProtect(_address, 1, 0x40, out var previous), "The owned fixture padding could not be restored.");
            try { Marshal.WriteByte(_address, _before); }
            finally { Check(ChannelProtect(_address, 1, previous, out _), "The restored padding page lost its original protection."); }
            Check(Marshal.ReadByte(_address) == _before, "The actual mapped padding byte was not restored.");
            _restored = true;
        }
    }

    private static int RunChannelOperation(string[] args, string mode)
    {
        var windows = new List<object>();
        var nativeEvents = new List<object>();
        object[] inputs = Array.Empty<object>();
        try
        {
            Check(args.Length == 5 && ChannelModes.Contains(mode), "Expected a known operation mode plus fixture, Clock helper, source helper and Dobby.");
            var paths = args.Skip(1).Select(Path.GetFullPath).ToArray();
            var hashes = paths.Select(Hash).ToArray();
            Check(hashes[0] == ChannelFixtureHash, "Operation regression requires the fixed owned stock fixture, not a game module or another fixture layout.");
            Check(new FileInfo(paths[0]).Length == 28160, "The pinned operation fixture has another physical layout.");
            inputs = paths.Select((path, index) => (object)new { role = new[] { "ownedFixture", "clockHelper", "sourceHelper", "dobby" }[index], path, sha256 = hashes[index], bytes = new FileInfo(path).Length }).ToArray();
            var native = NativeLibrary.Load(paths[0]);
            using var api = new FakeNative(); api.ConfigureRootedStock(native); api.ObserveChannelOperationSamples();
            api.SetRootedGraphValue("progress.now", 2.8); api.SetRootedGraphValue("info.nowTime", 5.6f);
            using var main = new MainThreadDispatcher(); var log = new Logger();
            var state = new GameEventState(); state.Scene("Level"); state.SetAvailability(scenes: true, runs: true, pause: true);
            var started = state.Controller(api.GameplayInformation.ToInt64(), true, false, "channel-operation-chart", api.GameplayController.ToInt64())!;
            state.ObservePause(true);
            var targets = PlaybackSourceGroupTargets.ResolveOwnedFixture(native);
            Check(targets.SourceRoleCount == 11 && !targets.HasPlayerConstructor,
                "The operation fixture scope must report its historical eleven source roles without a native player watch.");
            var binding = new NativePlaybackBinding(started.RunId, state.SceneGeneration, 1, 1,
                api.GameplayController.ToInt64(), api.GameplayInformation.ToInt64(), api.GameplayProgress.ToInt64(),
                api.GameplayPlayer.ToInt64(), api.GameplaySource.ToInt64(), api.GameplayClip.ToInt64(), targets.GetTime.MethodInfo.ToInt64());
            var origin = new NativePlaybackRateOrigin(binding.RunId, 1, binding.Controller, binding.Information, binding.Progress, api.GameplayPause.ToInt64());
            var run = state.CapturePlaybackRun(binding.Information, binding.Controller);
            using var router = new NativePlaybackStockClockRouter(api.GameplayApi, binding, origin, run);
            var broker = new ExactHookBroker(paths[3], main, log);
            NativePlaybackStockClockRouter.Session? session = null; NativePlaybackClock? finalClock = null;
            var originalBits = new List<uint>(); var nativeClockReaders = 0; var sameFinalClock = true;
            var managedClockFaultCallbacks = 0; var managedSourceFaultCallbacks = 0;
            Action<NativePlaybackSourceObservation>? beforeSourceOperation = null;
            Action<NativePlaybackSourceCompletion>? afterSourceOperation = null;
            PlaybackSourceGroupLease Install() => broker.BindPlaybackSourceGroup(targets, paths[1], hashes[1], paths[2], hashes[2],
                () => { ++nativeClockReaders; sameFinalClock &= finalClock is not null && ReferenceEquals(finalClock, session!.Clock); return router.ReadClock(); },
                fault => { Interlocked.Increment(ref managedClockFaultCallbacks); router.OnGetTimeFault(fault); },
                fault => { Interlocked.Increment(ref managedSourceFaultCallbacks); router.OnSourceFault(fault); },
                observation =>
                {
                    nativeEvents.Add(new { stage = "before", role = observation.Role.ToString(), observation.Sequence, observation.ParentSequence,
                        observation.WriterSequence, observation.SourceSequence, observation.SchedulingSequence,
                        observation.ArgBits, self = observation.Self.ToInt64(), method = observation.IncomingMethod.ToInt64(), callerRva = observation.Caller.ToInt64() - native.ToInt64() });
                    router.ObserveBefore(observation);
                    beforeSourceOperation?.Invoke(observation);
                },
                completion =>
                {
                    nativeEvents.Add(new { stage = "returned", role = completion.Observation.Role.ToString(), completion.Observation.Sequence,
                        completion.Returned, completion.Abnormal, completion.PhysicalExited, completion.Unknown });
                    router.ObserveReturned(completion);
                    afterSourceOperation?.Invoke(completion);
                },
                getTimeAfter: (_, completion) => originalBits.Add(completion.OriginalBits));

            // +512 is NOP padding after Replay's RET in the pinned native body.
            // It is restored before this isolated process exits; no mutated
            // Replay function is executed by either audit case.
            var unusedReplay = IntPtr.Add(targets.ReplayScheduled.Address, 512);
            if (mode == "operation-install-audit")
            {
                var beforeImages = NativePlaybackExecutableSeal.ReadThreadFullImageAudits();
                using (var mutation = new ChannelCodeMutation(unusedReplay))
                    Reject(() => Install(), "Initial installation admitted an actual mapped code mutation after target capture.");
                var counts = broker.ReadActualPlaybackDobbyCallCounts();
                Check(counts == (0, 0) && api.ActiveRoots == 0, "Rejected first installation performed Dobby commits or acquired source roots.");
                // Capture can reject while it compares the mapped image to
                // the pinned file, before a retained VerifyCore entry exists.
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "playback-channel-operation-regression", mode, passed = true, checks = _checks,
                    inputs, actualPrepare = counts.Prepared, actualCommit = counts.Committed, mappedPaddingRestored = true,
                    fullImageAudits = NativePlaybackExecutableSeal.ReadThreadFullImageAudits() - beforeImages,
                    sourceRoleCount = targets.SourceRoleCount, hasPlayerConstructor = targets.HasPlayerConstructor,
                    nativeSuppressObserverScopeCovered = false, formalFreshSourceScopeCovered = false,
                    metadataSynthetic = true, productionAdmission = false, gameExecuted = false, installed = false }));
                return 0;
            }

            var group = Install(); router.Attach(group);
            Check(!group.HasPlayerConstructor, "The actual installed operation fixture unexpectedly has a native player watch channel.");
            var invoke = Export<Invoke>(native, "StockFixtureInvoke");
            var getTime = Marshal.GetDelegateForFunctionPointer<GetTime>(targets.GetTime.Address);
            var sourceNative = NativeLibrary.Load(paths[2]);
            var readOwner = Export<ChannelReadOwner>(sourceNative, "RtPlaybackSourceReadOwner");
            var actualGroup = ChannelPrivate<ExactHookBroker.PlaybackSourceInstallGroup>(group, "_owner");
            var actualHub = ChannelPrivate<NativePlaybackStockSourceHub>(actualGroup, "_hub");
            var actualGetTime = ChannelPrivate<ExactHookBroker.PlaybackOwner>(group.GetTimeLease, "_owner");
            var actualClockBridge = ChannelPrivate<NativePlaybackSingleReturnBridge>(actualGetTime, "_bridge");
            NativePlaybackSourceInformation NativeOwner()
            {
                var value = new NativePlaybackSourceInformation { Version = 4, Size = 136 };
                Check(readOwner(actualHub.NativeOwner, ref value) == 1 && value.Version == 4 && value.Size == 136
                    && value.Owner == actualHub.NativeOwner && value.OwnerEpoch == actualHub.Epoch,
                    "The actual source helper did not return its retained native owner and epoch.");
                return value;
            }
            ChannelSnapshot Snapshot()
            {
                var diagnostics = group.ReadOperationDiagnostics(); var owner = NativeOwner();
                return new(diagnostics.FullAudits, diagnostics.FullImageAudits, diagnostics.ClockChecks, diagnostics.RateAdmissions,
                    diagnostics.SourceChecks, api.RootedDspReads, api.RootedPitchReads, api.ChannelMediaReads,
                    api.ChannelAudioDspReads, api.ChannelClipReads, api.FieldReads,
                    Marshal.ReadInt32(api.GameplaySource, 20), Marshal.ReadInt32(api.GameplaySource, 120),
                    owner.WriterSequence, owner.SourceSequence, owner.SchedulingSequence, owner.Entered, owner.Exited,
                    owner.InFlight, (uint)owner.Flags, owner.Faults);
            }
            (ChannelSnapshot Before, ChannelSnapshot After) Window(string name, Action action, bool narrow = true)
            {
                var before = Snapshot(); action(); var after = Snapshot();
                windows.Add(new { name, before, after, deltas = new {
                    fullAudits = after.FullAudits - before.FullAudits, fullImageAudits = after.FullImageAudits - before.FullImageAudits,
                    clockChecks = after.ClockChecks - before.ClockChecks, rateAdmissions = after.RateAdmissions - before.RateAdmissions,
                    formalFreshSourceChecks = after.SourceChecks - before.SourceChecks, dspReads = after.DspReads - before.DspReads,
                    pitchReads = after.PitchReads - before.PitchReads, mediaReads = after.MediaReads - before.MediaReads,
                    audioDspReads = after.AudioDspReads - before.AudioDspReads, clipReads = after.ClipReads - before.ClipReads,
                    fieldReads = after.FieldReads - before.FieldReads,
                    nativePitchCalls = after.NativePitchCalls - before.NativePitchCalls, nativeTimeCalls = after.NativeTimeCalls - before.NativeTimeCalls,
                    writerFence = after.WriterFence - before.WriterFence, sourceFence = after.SourceFence - before.SourceFence,
                    schedulingFence = after.SchedulingFence - before.SchedulingFence } });
                if (narrow) Check(after.FullAudits == before.FullAudits && after.FullImageAudits == before.FullImageAudits,
                    name + " entered a complete group/image audit during its running operation.");
                return (before, after);
            }
            void Call(NativePlaybackSourceRole role, float argument = 0)
            {
                var target = targets.SourceTarget(role); var self = role == NativePlaybackSourceRole.ProgressPlay ? api.GameplayProgress : api.GameplayPlayer;
                var kind = role == NativePlaybackSourceRole.Pause ? 1u : role == NativePlaybackSourceRole.ProgressPlay ? 5u : 2u;
                var bits = role == NativePlaybackSourceRole.ProgressPlay ? 0ul : unchecked((uint)BitConverter.SingleToInt32Bits(argument));
                Check(invoke(kind, target.Address, self, bits, target.MethodInfo, out var error) == 1 && error == 0, "The actual stock source original failed: " + role);
            }
            Marshal.WriteInt32(api.GameplaySource, 48, 1); Call(NativePlaybackSourceRole.Pause);
            session = router.PreparePausedClock();
            if (mode is "operation-clock-activation-drift" or "operation-clock-activation-commit-drift")
            {
                const int pitchOffset = 16, positionOffset = 24;
                var baselinePosition = Marshal.ReadInt64(api.GameplaySource, positionOffset);
                var baselinePitch = Marshal.ReadInt32(api.GameplaySource, pitchOffset);
                var before = Snapshot(); var roots = api.ActiveRoots; var injected = 0;
                Check(actualClockBridge.ReadHealth() is { Known: true, AuthorityActive: false }, "Prepared Clock unexpectedly became active.");
                if (mode == "operation-clock-activation-drift")
                {
                    Marshal.WriteInt64(api.GameplaySource, positionOffset, BitConverter.DoubleToInt64Bits(7));
                    Reject(() => session.TryArm(), "Clock activated with an obsolete paused position.");
                    Check(actualClockBridge.ReadHealth() is { Known: true, AuthorityActive: false } && api.ActiveRoots == roots,
                        "Pre-activation drift made the original dormant policy active or lost source roots.");
                    Marshal.WriteInt64(api.GameplaySource, positionOffset, baselinePosition);
                    Marshal.WriteInt32(api.GameplaySource, pitchOffset, BitConverter.SingleToInt32Bits(1.25f));
                    Reject(() => session.TryArm(), "Clock activated with an obsolete paused pitch.");
                    Check(actualClockBridge.ReadHealth() is { Known: true, AuthorityActive: false } && api.ActiveRoots == roots,
                        "Pitch drift activated the dormant policy or lost source roots.");
                    Marshal.WriteInt32(api.GameplaySource, pitchOffset, baselinePitch);
                    Check(session.TryArm(), "The same dormant owner could not retry after the paused baseline returned.");
                    finalClock = session.Clock;
                    Check(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == (float)BitConverter.Int64BitsToDouble(baselinePosition)
                        && nativeClockReaders == 1 && sameFinalClock, "Retried Clock failed an actual GetTime return on the original baseline.");
                }
                else
                {
                    api.DuringRootedDspRead = () =>
                    {
                        if (!actualClockBridge.ReadHealth().AuthorityActive) return;
                        ++injected; api.DuringRootedDspRead = null;
                        Marshal.WriteInt64(api.GameplaySource, positionOffset, BitConverter.DoubleToInt64Bits(7));
                    };
                    try { Reject(() => session.TryArm(), "Clock kept its newly activated policy after commit-time drift."); }
                    finally { api.DuringRootedDspRead = null; }
                    Check(injected == 1 && actualClockBridge.ReadHealth() is { Known: true, AuthorityActive: false },
                        "Commit-time drift did not retire the exact attempted Clock policy.");
                    var health = actualClockBridge.ReadHealth();
                    _ = getTime(api.GameplayPlayer, targets.GetTime.MethodInfo);
                    Check(nativeClockReaders == 0 && actualClockBridge.ReadHealth().OriginalCalls == health.OriginalCalls + 1,
                        "After withdrawal GetTime did not preserve the original-only path.");
                }
                var after = Snapshot();
                Check(after.NativePitchCalls == before.NativePitchCalls && after.NativeTimeCalls == before.NativeTimeCalls
                    && after.WriterFence == before.WriterFence, "Clock activation wrote back the previous pause values.");
                router.Dispose(); router.PumpCompleted();
                Check(router.RootsReleased && api.ActiveRoots == 0, "Clock activation drift retained drained source roots.");
                group.Dispose();
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "paused-clock-activation-regression", mode, passed = true,
                    checks = _checks, inputs, before, after, injected, nativeClockReaders, activeRoots = api.ActiveRoots,
                    formalFreshSourceScopeCovered = false, metadataSynthetic = true, gameExecuted = false, installedInGame = false }));
                return 0;
            }
            Check(session.TryArm(), "The real dormant native Clock did not arm."); finalClock = session.Clock;
            using var provider = router.AdoptControlledRateHost();
            Check(provider.Probe().Supported, "The actual controlled stable provider did not adopt the armed Session.");
            var installed = Snapshot(); Check(installed.FullAudits > 0 && installed.FullImageAudits > 0, "Installation/adoption omitted its retained complete audits.");
            Check(NativeOwner().ConfiguredSlots == 11 && installed.SourceChecks == 0,
                "The actual fixture counters must not claim formal fresh-source operation scope coverage.");
            var explicitAudit = Window("explicit-current-installation-audit", group.VerifyCurrent, narrow: false);
            Check(explicitAudit.After.FullAudits > explicitAudit.Before.FullAudits && explicitAudit.After.FullImageAudits > explicitAudit.Before.FullImageAudits,
                "Explicit diagnostic verification skipped the complete body/image audit.");

            using var journal = new RunModificationJournal(main, log, state);
            var module = new GameplayModule(api.GameplayApi, main, () => state.LifecycleSnapshot(), journal.Record, state.MatchesCurrentGameplayRun, provider);
            Check(module.Probe().Ready, "The actual GameplayModule did not construct its backend and service.");
            try
            {
                var caps = new CapabilityRegistry(new("channel-operation", "owned-fixture", "synthetic", "offline"));
                var registration = new ModuleRegistration(caps, module.Manifest); module.Register(registration); registration.Complete();
                caps.RegisterScoped<IRunModificationService>(RunModificationCapabilityIds.Read, journal.ForPlugin);
                using var host = new PluginHost(caps, main, new EventHub(log), log);
                var plugin = new Plugin("channel-operation-one"); var second = new Plugin("channel-operation-two");
                Check(host.Register(plugin).Success && host.Register(second).Success && host.Enable(plugin.Manifest.Id).Success && host.Enable(second.Manifest.Id).Success,
                    "The actual PluginHost did not issue two independent scopes.");
                var controls = plugin.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
                var other = second.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
                var sdkClock = plugin.Context.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!;
                long tick = 0;
                GameplayControlSnapshot Observe()
                {
                    module.ObserveControls(++tick, tick, requested: true);
                    return Need(controls.GetState());
                }
                if (mode == "operation-normal")
                {
                    var idle = Window("no-consumer-playback-maintenance", () =>
                    {
                        for (var index = 0; index != 3; ++index)
                        {
                            Check(router.MaintainsReadySession(session, run) && module.Tick().Ready, "Idle host maintenance lost its registered armed session.");
                            module.ObserveControls(++tick, tick, requested: false);
                        }
                    });
                    Check(idle.After.DspReads == idle.Before.DspReads && idle.After.PitchReads == idle.Before.PitchReads
                        && idle.After.MediaReads == idle.Before.MediaReads && idle.After.RateAdmissions == idle.Before.RateAdmissions,
                        "No-consumer maintenance sampled audio or admitted a value operation.");
                }
                var observed = Observe();
                Check(observed.ObservedRunId == binding.RunId && observed.ControllerGeneration == 1
                    && observed.Operations.Single(row => row.Action == GameplayControlAction.SetSpeed).Supported,
                    "The SDK did not expose the actual current source/run.");
                using var lease = Need(controls.Acquire(true)); Check(!other.Acquire(true).Success, "Another plugin bypassed exclusive SDK ownership.");
                GameplayControlRequest Request(double rate) => new(GameplayControlAction.SetSpeed, lease.ControllerGeneration, lease.ObservedRunId,
                    rate, GameplayControlValueUnit.PlaybackRateMultiplier);
                GameplayControlCommandSnapshot QueuedSpeed(double rate)
                {
                    var beforeSubmit = Snapshot(); var command = Need(lease.Submit(Request(rate)));
                    Check(command.Snapshot.State == GameplayControlCommandState.Queued, "The actual SDK Submit bypassed the host command phase.");
                    var queued = Snapshot();
                    Check(queued.NativePitchCalls == beforeSubmit.NativePitchCalls && queued.DspReads == beforeSubmit.DspReads
                        && queued.PitchReads == beforeSubmit.PitchReads && queued.MediaReads == beforeSubmit.MediaReads
                        && queued.FullAudits == beforeSubmit.FullAudits && queued.FullImageAudits == beforeSubmit.FullImageAudits,
                        "Queue publication accessed the native source or installation.");
                    module.PumpControlCommand(); return command.Snapshot;
                }
                void ConfirmSpeed(double rate)
                {
                    var before = Snapshot(); var revision = finalClock.Capture().Revision;
                    var terminal = QueuedSpeed(rate); var after = Snapshot();
                    Check(terminal.State == GameplayControlCommandState.Succeeded
                        && terminal.Receipt?.Modification.TransactionState == GameplayControlTransactionState.Confirmed
                        && terminal.Receipt.Modification.ConfirmedValue == rate,
                        "The actual queued SDK transaction did not reach joint native/Clock confirmation: " + JsonSerializer.Serialize(terminal));
                    Check(after.NativePitchCalls == before.NativePitchCalls + 1 && after.WriterFence == before.WriterFence + 1
                        && after.PitchReads > before.PitchReads && after.DspReads > before.DspReads,
                        "The confirmed request lacks one actual setter, its continuous native fence, or independent API readback.");
                    Check(after.RateAdmissions == before.RateAdmissions + 1 && after.ClockChecks > before.ClockChecks,
                        "The captured SDK request did not use one rate admission and fresh native Clock policy checks.");
                    Check(finalClock.Capture().Rate == rate && finalClock.Capture().Revision > revision
                        && ReferenceEquals(finalClock, session.Clock) && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == (float)rate,
                        "The actual setter and unique final Clock did not agree.");
                    windows.Add(new { name = "sdk-command-terminal", terminal });
                }
                void NoOldSdkPlayback()
                {
                    var reading = sdkClock.Read();
                    Check(!reading.Success || reading.Value is { Playback.Available: false, Playback.Sample: null },
                        "The SDK exposed an old optional native Clock sample after current authority ended.");
                }
                var physicalUnknown = false;
                Action? verifyPublicationRootsAfterOwnerRelease = null;
                if (mode == "operation-pitch-source-busy")
                {
                    var rooted = ChannelPrivate<NativePlaybackStockRootedSource>(router, "_source");
                    var clockBefore = finalClock.Capture(); var rootsBefore = api.ActiveRoots;
                    using var prepared = NativePlaybackRateProviderAdoption.Prepare(provider, origin, Request(2), () => run.VerifyCurrent(binding));
                    var verify = ChannelPrivate<Action>(prepared, "_verifyOrigin");
                    var bound = (INativePlaybackRateAccess)verify.Target!;
                    var captured = ChannelPrivate<INativePlaybackRateSource>(bound, "_source");
                    var participants = ChannelPrivate<IGameplayValueParticipant[]>(prepared, "_participants");
                    var plan = ChannelPrivate<object>(participants[0], "_plan");
                    var writer = ChannelPrivate<INativePlaybackPitchWrite>(plan, "_pitchWrite");
                    var actualWriter = ChannelPrivate<NativePlaybackStockRootedSource.PitchWrite>(rooted, "_pitchWriter");
                    var preparedAccessor = ChannelPrivate<NativePlaybackActualSource>(captured, "_actual");
                    var preparedHandles = ChannelPrivate<List<NativeRuntime.RootHandle>>(preparedAccessor, "_roots");
                    var retainedRoots = api.CaptureChannelRootHandles(preparedHandles.Select(root => ChannelPrivate<uint>(root, "_id")));
                    var returned = 0; var injected = 0; var refused = 0; var rootsInGetter = 0;
                    Exception? injectionFailure = null;
                    void RefuseBusy(Action dispose, string owner)
                    {
                        try { dispose(); }
                        catch (GameplayStateException error) when (error.Code == ErrorCode.MutationReentrancy)
                        { ++refused; return; }
                        throw new InvalidOperationException("The actual busy " + owner + " consumed its cleanup instead of refusing.");
                    }
                    afterSourceOperation = completion =>
                    {
                        if (completion.Observation.Role == NativePlaybackSourceRole.SetPitch && completion.Returned) ++returned;
                    };
                    api.ChannelReadException = name =>
                    {
                        if (name != "get_pitch" || returned != 1 || injected != 0) return false;
                        ++injected; api.ChannelReadException = null;
                        try
                        {
                            rootsInGetter = api.ActiveRoots; var frees = api.FreeHandleAttempts;
                            RefuseBusy(captured.Dispose, "captured source");
                            RefuseBusy(bound.Dispose, "adopted source");
                            RefuseBusy(writer.Dispose, "adopted writer");
                            Check(refused == 3 && !ChannelPrivate<bool>(captured, "_disposed")
                                && ReferenceEquals(ChannelPrivate<NativePlaybackStockRootedSource.PitchWrite>(captured, "_pitchWriter"), actualWriter)
                                && api.ActiveRoots == rootsInGetter && api.FreeHandleAttempts == frees
                                && api.RetainsChannelRootHandles(retainedRoots),
                                "A busy source/writer refusal lost registration, preparation handles or an outer wrapper.");
                            bound.VerifyAuthority(); writer.VerifyCurrent();
                            return false;
                        }
                        catch (Exception error) { injectionFailure = error; return true; }
                    };
                    var execution = Window("actual-pitch-busy-source-retains-original-preparation", () =>
                    {
                        try { prepared.Commit(); }
                        catch (Exception) when (injectionFailure is not null)
                        { throw new InvalidOperationException("The actual getter cleanup assertion failed.", injectionFailure); }
                        finally { api.ChannelReadException = null; afterSourceOperation = null; }
                        Check(injected == 1 && refused == 3 && prepared.Confirm().Observed == true
                            && finalClock.Capture().Rate == 2 && rooted.LastFailure is null,
                            "Rejected getter cleanup prevented the original audio/Clock transaction from confirming.");
                        prepared.Restore(); Check(prepared.ConfirmRestored().OriginalStateRestored,
                            "The original busy-refused transaction lost its actual inverse writer.");
                        Check(finalClock.Capture().Rate == clockBefore.Rate
                            && finalClock.Capture().MediaAnchorSeconds == clockBefore.MediaAnchorSeconds
                            && finalClock.Capture().DspAnchorSeconds == clockBefore.DspAnchorSeconds
                            && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 1,
                            "The original confirmed pitch/Clock baseline did not return after busy-refused cleanup.");
                        prepared.Dispose();
                        var frees = api.FreeHandleAttempts;
                        bound.Dispose(); captured.Dispose(); writer.Dispose(); prepared.Dispose();
                        Check(api.ActiveRoots == rootsBefore && api.FreeHandleAttempts == frees && provider.Probe().Supported,
                            "Actual owner cleanup leaked preparation roots or replayed a completed native free.");
                    });
                    Check(execution.After.NativePitchCalls == execution.Before.NativePitchCalls + 2
                        && execution.After.WriterFence == execution.Before.WriterFence + 2
                        && execution.After.NativeTimeCalls == execution.Before.NativeTimeCalls
                        && execution.After.SourceFence == execution.Before.SourceFence
                        && execution.After.SchedulingFence == execution.Before.SchedulingFence,
                        "Busy-refused cleanup lost the exact requested/restoring native attempts or changed source/schedule authority.");
                    windows.Add(new { name = "pitch-source-busy-retention", injected, refused, rootsInGetter,
                        preparedThroughActualAdoption = true, actualInverseConfirmed = true, cleanupRetried = false });
                }
                else if (mode == "operation-pitch-source-preparation-close")
                {
                    var rooted = ChannelPrivate<NativePlaybackStockRootedSource>(router, "_source");
                    var rootsBefore = api.ActiveRoots; var clockBefore = finalClock.Capture();
                    using var captured = provider.Capture(origin);
                    var injected = 0; INativePlaybackPitchWrite? escaped = null; Exception? refusal = null; Exception? injectionFailure = null;
                    api.ChannelReadException = name =>
                    {
                        if (name != "get_pitch" || injected != 0) return false;
                        ++injected; api.ChannelReadException = null;
                        try
                        {
                            captured.Dispose();
                            Check(ChannelPrivate<bool>(captured, "_disposed") && !rooted.RootsReleased,
                                "The preparing getter did not close its actual captured source while retaining the original observer.");
                            return false;
                        }
                        catch (Exception error) { injectionFailure = error; return true; }
                    };
                    var execution = Window("actual-pitch-preparation-getter-closes-captured-source", () =>
                    {
                        try { escaped = captured.PreparePitchWrite(); }
                        catch (Exception error) { refusal = error; }
                        finally { api.ChannelReadException = null; escaped?.Dispose(); }
                        if (injectionFailure is not null) throw new InvalidOperationException("The actual preparation getter cleanup assertion failed.", injectionFailure);
                        Check(injected == 1 && escaped is null && refusal is GameplayStateException { Code: ErrorCode.TargetExpired },
                            "A source closed by its preparation getter still returned a registered pitch writer.");
                        Check(rooted.GetType().GetField("_pitchWriter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rooted) is null
                            && api.ActiveRoots == rootsBefore && rooted.LastFailure is null && provider.Probe().Supported
                            && finalClock.Capture() == clockBefore,
                            "Refused pitch preparation retained an unreachable writer, preparation roots or a false source fault.");
                        var frees = api.FreeHandleAttempts; captured.Dispose();
                        Check(api.FreeHandleAttempts == frees, "Repeated closed-source cleanup replayed its original free.");
                    });
                    Check(execution.After.NativePitchCalls == execution.Before.NativePitchCalls
                        && execution.After.WriterFence == execution.Before.WriterFence
                        && execution.After.NativeTimeCalls == execution.Before.NativeTimeCalls,
                        "Rejected closed-source preparation dispatched a native value store.");
                    ConfirmSpeed(2);
                    windows.Add(new { name = "pitch-source-preparation-close", injected, refusal = ((GameplayStateException)refusal!).Code.ToString(),
                        writerReturned = false, actualNextSdkWriteConfirmed = true });
                }
                else if (mode == "operation-normal")
                {
                    var hook = group.GetTimeLease;
                    void VerifyGetTimeOperation() => hook.VerifyOperationCurrent(native, targets.GetTime.Address, targets.GetTime.MethodInfo);
                    var hookWindow = Window("grouped-gettime-lease-operation-current", () =>
                    {
                        for (var index = 0; index != 3; ++index) VerifyGetTimeOperation();
                    });
                    Check(hookWindow.After.ClockChecks == hookWindow.Before.ClockChecks + 3
                        && hookWindow.After.RateAdmissions == hookWindow.Before.RateAdmissions
                        && hookWindow.After.DspReads == hookWindow.Before.DspReads
                        && hookWindow.After.PitchReads == hookWindow.Before.PitchReads
                        && hookWindow.After.MediaReads == hookWindow.Before.MediaReads
                        && hookWindow.After.NativePitchCalls == hookWindow.Before.NativePitchCalls
                        && hookWindow.After.NativeTimeCalls == hookWindow.Before.NativeTimeCalls,
                        "The new grouped GetTime lease API did not perform exactly three native Clock checks without sampling or writing.");
                    var hookRoots = api.ActiveRoots;
                    var invalidHook = Window("grouped-gettime-lease-operation-identity-refusal", () =>
                    {
                        Reject(() => Clone(hook).VerifyOperationCurrent(native, targets.GetTime.Address, targets.GetTime.MethodInfo),
                            "A copied lease passed the new running GetTime API.");
                        Reject(() => hook.VerifyOperationCurrent(IntPtr.Add(native, 1), targets.GetTime.Address, targets.GetTime.MethodInfo),
                            "The running GetTime API accepted another module.");
                        Reject(() => hook.VerifyOperationCurrent(native, IntPtr.Add(targets.GetTime.Address, 1), targets.GetTime.MethodInfo),
                            "The running GetTime API accepted another target address.");
                        Reject(() => hook.VerifyOperationCurrent(native, targets.GetTime.Address, IntPtr.Add(targets.GetTime.MethodInfo, 1)),
                            "The running GetTime API accepted another metadata method.");
                        Task.Run(() => Reject(VerifyGetTimeOperation, "A worker passed the new running GetTime API.")).GetAwaiter().GetResult();
                    });
                    Check(invalidHook.After.ClockChecks == invalidHook.Before.ClockChecks && api.ActiveRoots == hookRoots,
                        "Foreign GetTime API identities consumed the real group's Clock channel or retained roots.");
                    var ownerRetry = Window("grouped-gettime-lease-operation-owner-retry", VerifyGetTimeOperation);
                    Check(ownerRetry.After.ClockChecks == ownerRetry.Before.ClockChecks + 1 && api.ActiveRoots == hookRoots,
                        "Rejected GetTime API identities poisoned the lawful owner retry.");
                    var probes = Window("repeated-probe-descriptor-published-state", () =>
                    {
                        for (var index = 0; index != 3; ++index)
                        {
                            Check(provider.Probe().Supported && provider.ObserveSourceDescriptor()?.Binding == binding
                                && provider.CaptureSourceDescriptor()?.Binding == binding, "An unchanged registered observation disappeared.");
                            Check(Need(controls.GetState()).ObservationRevision == observed.ObservationRevision, "GetState rebuilt an unchanged host publication.");
                        }
                    });
                    Check(probes.After.DspReads == probes.Before.DspReads && probes.After.PitchReads == probes.Before.PitchReads
                        && probes.After.MediaReads == probes.Before.MediaReads && probes.After.RateAdmissions == probes.Before.RateAdmissions,
                        "Ordinary probes or published control reads sampled audio or repeated fresh admission.");
                    var copies = Window("copied-and-worker-operation-authority", () =>
                    {
                        var roots = api.ActiveRoots;
                        Reject(() => Clone(group).ReadOperationClockPolicy(group.GetTimeLease), "A copied group borrowed the actual Clock channel.");
                        Reject(Clone(group).RequireRateOperationAdmission, "A copied group admitted a writer.");
                        Reject(() => Clone(group).ReadOperationDiagnostics(), "A copied group borrowed the actual diagnostic record.");
                        Reject(() => group.ReadOperationClockPolicy(Clone(group.GetTimeLease)), "A copied GetTime lease borrowed current policy.");
                        Reject(() => Clone(provider).Capture(origin), "A copied stable host captured source authority.");
                        Reject(Clone(provider).Dispose, "Copied host cleanup disposed the actual provider.");
                        using var source = provider.Capture(origin); using var writer = source.PreparePitchWrite();
                        Reject(Clone(source).VerifyAuthority, "A copied RateSource borrowed actual roots.");
                        Reject(Clone(source).Dispose, "A copied RateSource freed actual roots.");
                        var readout = provider.ReadPlaybackClock(origin);
                        Reject(Clone(readout.SourceReceipt!).VerifyOperationIdentity, "A copied rooted receipt borrowed the current registered source.");
                        Task.Run(() =>
                        {
                            Reject(() => group.ReadOperationClockPolicy(group.GetTimeLease), "A worker read owner-thread native Clock policy.");
                            Reject(group.RequireRateOperationAdmission, "A worker admitted the native writer.");
                            Reject(() => provider.ReadPlaybackClock(origin), "A worker sampled owner-thread playback.");
                            Reject(() => writer.Write(4), "A worker wrote the prepared source.");
                            Reject(source.Dispose, "A worker released the captured roots.");
                        }).GetAwaiter().GetResult();
                        Check(api.ActiveRoots == roots + 7 && provider.Probe().Supported, "Rejected copy/worker actions lost legitimate source roots or provider availability.");
                    });
                    Check(copies.After.NativePitchCalls == copies.Before.NativePitchCalls, "Rejected copy/worker authority performed a pitch write.");
                    for (var index = 1; index <= 3; ++index)
                    {
                        var dsp = 100 + index * .125; api.SetRootedDsp(dsp);
                        var read = Window("independent-provider-clock-" + index, () =>
                        {
                            var value = provider.ReadPlaybackClock(origin);
                            Check(ReferenceEquals(value.Clock, finalClock) && value.State.Binding == binding && value.DspSeconds == dsp && value.MediaSeconds == 3,
                                "A provider read reused an old DSP sample or another source/Clock.");
                            provider.VerifyPlaybackClock(value); provider.VerifyPlaybackClockState(value);
                        });
                        Check(read.After.DspReads == read.Before.DspReads + 2 && read.After.PitchReads == read.Before.PitchReads + 1
                            && read.After.MediaReads == read.Before.MediaReads + 1 && read.After.RateAdmissions == read.Before.RateAdmissions,
                            "One independent provider Clock read did not consume exactly two DSP, one pitch and one media getter without rate admission.");
                    }
                    Window("sdk-current-paused-rate-command", () => ConfirmSpeed(2));
                    Window("sdk-independent-optional-playback", () =>
                    {
                        var value = Need(sdkClock.Read());
                        Check(value.ObservedRunId == binding.RunId && value.Playback.Available && value.Playback.Sample is { PlaybackRate: 2, MediaSeconds: 3 },
                            "SDK raw game time succeeded while its independent optional native Clock was lost.");
                    });
                    var sourceWindow = Window("actual-replay-pause-resume-gettime", () =>
                    {
                        Call(NativePlaybackSourceRole.Replay, .125f); var replay = finalClock.Capture();
                        Check(replay.Phase == NativePlaybackPhase.Scheduled && replay.Rate == 2, "Actual Replay lost the selected native/Clock rate.");
                        Check(Math.Abs(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) + .125f) < .00001f, "Actual native GetTime lost unscaled pre-roll.");
                        api.SetRootedDsp(replay.DspAnchorSeconds + .375);
                        Check(Math.Abs(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) - .75f) < .00001f, "Actual native GetTime lost the final Clock multiplier.");
                        Call(NativePlaybackSourceRole.Pause); Call(NativePlaybackSourceRole.Resume, .25f);
                        var resume = finalClock.Capture(); api.SetRootedDsp(resume.DspAnchorSeconds + .25);
                        Check(resume.Rate == 2 && Math.Abs(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) - (float)(resume.MediaAnchorSeconds + .5)) < .00001f,
                            "Actual native Resume/GetTime lost its saved media or final Clock multiplier.");
                    });
                    Check(sourceWindow.After.NativePitchCalls >= sourceWindow.Before.NativePitchCalls + 4
                        && sourceWindow.After.NativeTimeCalls == sourceWindow.Before.NativeTimeCalls + 1
                        && originalBits.Count == 3 && actualClockBridge.ReadHealth().OriginalCalls == 3 && nativeClockReaders == 3 && sameFinalClock,
                        "The narrow source path skipped actual reset/restore/SetTime originals or used another final Clock.");
                }
                else if (mode is "operation-final-graph-writer-equal" or "operation-final-graph-writer-aba"
                    or "operation-final-graph-native-fault")
                {
                    var healthy = Need(sdkClock.Read());
                    Check(healthy.Playback.Available && healthy.Playback.Sample is { PlaybackRate: 1, MediaSeconds: 3 },
                        "The final graph case has no real current SDK playback sample before interference.");
                    var rooted = ChannelPrivate<NativePlaybackStockRootedSource>(router, "_source");
                    var receipt = provider.ReadPlaybackClock(origin);
                    var nativeFault = mode == "operation-final-graph-native-fault";
                    var attempts = nativeFault ? 0 : mode == "operation-final-graph-writer-aba" ? 2 : 1;
                    var setter = targets.SourceTarget(NativePlaybackSourceRole.SetPitch);
                    var markFault = Export<ChannelMarkFault>(sourceNative, "RtPlaybackSourceMarkFault");
                    var callbacksBefore = nativeEvents.Count;
                    var sourceFaultCallbacksBefore = Volatile.Read(ref managedSourceFaultCallbacks);
                    var clockFaultCallbacksBefore = Volatile.Read(ref managedClockFaultCallbacks);
                    var rootsBefore = api.ActiveRoots;
                    var injected = 0; var rootsInsideGetter = 0; var workerThread = 0;
                    ChannelSnapshot? beforeInterference = null, afterInterference = null;
                    var managedObserverCallbackDelta = 0;
                    var managedSourceFaultCallbackDelta = 0; var managedClockFaultCallbackDelta = 0;
                    var physicalFaultPendingAfterInjection = false; var groupActiveAfterInjection = false;
                    var workerValues = new List<float>();
                    api.ChannelWholeCaptureClipReads = 0;
                    api.ChannelFinalCaptureStack = Array.Empty<string>();
                    api.ChannelFinalCaptureInjectionError = null;
                    api.DuringChannelFinalCaptureClipRead = () =>
                    {
                        ++injected; rootsInsideGetter = api.ActiveRoots;
                        beforeInterference = Snapshot();
                        Check(api.ChannelWholeCaptureClipReads == 2 && api.ChannelFinalCaptureStack.Contains("SameCapture")
                            // NativeRuntime.Call and InvokeCore independently
                            // root this receiver until the getter returns.
                            && api.ChannelFinalCaptureStack.Contains("Read") && rootsInsideGetter == rootsBefore + 2
                            && api.ChannelFinalCaptureClipReceiver == api.GameplaySource && api.ChannelFinalCaptureClipResult == api.GameplayClip
                            && api.ChannelFinalCaptureThread == Environment.CurrentManagedThreadId,
                            "Interference missed the actual final whole-capture get_clip return or lost its retained roots.");
                        Check(group.IsActive && !router.PhysicalFaultPending
                            && (beforeInterference.NativeFlags & (uint)NativePlaybackSourceFlags.Unknown) == 0,
                            "The final graph boundary was already physically faulted before interference.");
                        if (nativeFault)
                        {
                            // The real POD export changes native health only. No
                            // broker poll or managed fault callback precedes the
                            // service's final receipt/policy fence in this case.
                            Check(markFault(actualHub.NativeOwner, actualHub.Epoch) == 1,
                                "The actual final graph source owner rejected its exact native fault epoch.");
                        }
                        else
                        {
                            Task.Run(() =>
                            {
                                workerThread = Environment.CurrentManagedThreadId;
                                void Write(float pitch)
                                {
                                    Check(invoke(2, setter.Address, api.GameplaySource,
                                        unchecked((uint)BitConverter.SingleToInt32Bits(pitch)), setter.MethodInfo, out var error) == 1 && error == 0,
                                        "The actual worker pitch original did not complete.");
                                    workerValues.Add(pitch);
                                }
                                if (attempts == 2) Write(2);
                                Write(1);
                            }).GetAwaiter().GetResult();
                            Check(workerThread != Environment.CurrentManagedThreadId,
                                "The final graph writer did not execute on an actual worker thread.");
                        }
                        // Snapshot reads the diagnostic record and actual POD
                        // export; it neither polls health nor sends fault events.
                        afterInterference = Snapshot();
                        managedObserverCallbackDelta = nativeEvents.Count - callbacksBefore;
                        managedSourceFaultCallbackDelta = Volatile.Read(ref managedSourceFaultCallbacks) - sourceFaultCallbacksBefore;
                        managedClockFaultCallbackDelta = Volatile.Read(ref managedClockFaultCallbacks) - clockFaultCallbacksBefore;
                        physicalFaultPendingAfterInjection = router.PhysicalFaultPending;
                        groupActiveAfterInjection = group.IsActive;
                        Check(afterInterference.NativePitchCalls == beforeInterference.NativePitchCalls + attempts
                            && afterInterference.NativeTimeCalls == beforeInterference.NativeTimeCalls
                            && afterInterference.WriterFence == beforeInterference.WriterFence + (ulong)attempts
                            && afterInterference.SourceFence == beforeInterference.SourceFence
                            && afterInterference.SchedulingFence == beforeInterference.SchedulingFence
                            && afterInterference.Entered == beforeInterference.Entered + (ulong)attempts
                            && afterInterference.Exited == beforeInterference.Exited + (ulong)attempts && afterInterference.SourceInFlight == 0
                            && (afterInterference.NativeFlags & (uint)NativePlaybackSourceFlags.Unknown) != 0
                            && afterInterference.NativeFaults > beforeInterference.NativeFaults,
                            "The final graph interference lacks its actual native attempts, fences, exits or sticky Unknown.");
                        if (nativeFault)
                        {
                            Check(afterInterference.NativeFaults == beforeInterference.NativeFaults + 1
                                && groupActiveAfterInjection && !physicalFaultPendingAfterInjection
                                && managedObserverCallbackDelta == 0 && managedSourceFaultCallbackDelta == 0 && managedClockFaultCallbackDelta == 0,
                                "The POD-only final graph fault notified managed code or withdrew the broker before the final native fence.");
                        }
                        windows.Add(new { name = "final-whole-capture-injection", mode, actualServiceStack = api.ChannelFinalCaptureStack,
                            beforeInterference, afterInterference, managedObserverCallbackDelta, managedSourceFaultCallbackDelta,
                            managedClockFaultCallbackDelta, physicalFaultPendingAfterInjection, groupActiveAfterInjection });
                    };
                    GameplayClockSnapshot? published = null;
                    var failure = Window(nativeFault ? "sdk-final-whole-capture-native-pod-fault" : "sdk-final-whole-capture-worker-writer", () =>
                    {
                        try { published = Need(sdkClock.Read()); }
                        finally { api.DuringChannelFinalCaptureClipRead = null; }
                        if (api.ChannelFinalCaptureInjectionError is { } injectionError)
                            throw new InvalidOperationException("The actual final getter injection failed before its intended native interference.", injectionError);
                        Check(injected == 1 && published.Playback is { Available: false, Sample: null, Error: not null },
                            "The SDK published a stale native playback sample after final graph interference.");
                        if (nativeFault) Check(published.ObservedRunId == binding.RunId && published.GameTime.Available,
                            "The native POD case failed the whole capture instead of isolating the final optional playback fence.");
                    }, narrow: false);
                    Check(beforeInterference is not null && afterInterference is not null
                        && beforeInterference.DspReads == failure.Before.DspReads + 2
                        && beforeInterference.PitchReads == failure.Before.PitchReads + 1
                        && beforeInterference.MediaReads == failure.Before.MediaReads + 1,
                        "Final graph interference did not follow one actual independent playback sample.");
                    var finalInterference = afterInterference!;
                    Check(failure.After.DspReads == finalInterference.DspReads && failure.After.PitchReads == finalInterference.PitchReads
                        && failure.After.MediaReads == finalInterference.MediaReads && failure.After.AudioDspReads == finalInterference.AudioDspReads
                        && failure.After.ClipReads == finalInterference.ClipReads && failure.After.FieldReads == finalInterference.FieldReads,
                        "The final receipt/policy fence consumed a new audio sample or graph getter after final whole-capture interference.");
                    Check(failure.After.NativePitchCalls == failure.Before.NativePitchCalls + attempts
                        && failure.After.NativeTimeCalls == failure.Before.NativeTimeCalls
                        && failure.After.WriterFence == failure.Before.WriterFence + (ulong)attempts
                        && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 1
                        && !rooted.RootsReleased && api.ActiveRoots == rootsBefore,
                        "Final graph interference was retried/restored or lost the actual source root responsibility.");
                    Reject(receipt.SourceReceipt!.VerifyOperationIdentity, "The old read receipt survived final graph interference.");
                    Check(!provider.Probe().Supported && !lease.Execute(Request(4)).Success,
                        "Final graph interference revived old provider or SDK writer authority.");
                    NoOldSdkPlayback(); physicalUnknown = true;
                    windows.Add(new { name = "final-whole-capture-native-evidence", nativeFault, attempts, injected, workerThread,
                        workerValues, rootsInsideGetter, actualServiceStack = api.ChannelFinalCaptureStack,
                        sameCaptureClipReturns = api.ChannelWholeCaptureClipReads,
                        capturedClipReceiver = api.ChannelFinalCaptureClipReceiver.ToInt64(), capturedClipResult = api.ChannelFinalCaptureClipResult.ToInt64(),
                        captureThread = api.ChannelFinalCaptureThread,
                        managedObserverCallbackDelta, managedSourceFaultCallbackDelta, managedClockFaultCallbackDelta,
                        physicalFaultPendingAfterInjection, groupActiveAfterInjection,
                        sourceRoleCount = targets.SourceRoleCount, hasPlayerConstructor = group.HasPlayerConstructor,
                        nativeSuppressObserverScopeCovered = false,
                        beforeInterference, afterInterference, published!.Playback });
                }
                else if (mode.StartsWith("operation-getter-", StringComparison.Ordinal))
                {
                    using var oldSource = provider.Capture(origin); using var oldWriter = oldSource.PreparePitchWrite();
                    var sourceSwitch = mode.Contains("source", StringComparison.Ordinal); var successor = sourceSwitch ? api.CreateChannelSuccessorPlayer() : IntPtr.Zero;
                    var injected = 0; var rootsInsideGetter = 0;
                    void Inject()
                    {
                        api.DuringRootedPitchRead = null; api.DuringRootedDspRead = null; api.DuringChannelMediaRead = null;
                        ++injected; rootsInsideGetter = api.ActiveRoots;
                        Check(rootsInsideGetter >= 14, "The real getter lost the source and independent preparation roots while in flight.");
                        if (sourceSwitch)
                        {
                            Check(invoke(4, targets.ProgressMusicPlayer.Address, api.GameplayProgress, unchecked((ulong)successor.ToInt64()),
                                targets.ProgressMusicPlayer.MethodInfo, out var error) == 1 && error == 0,
                                "The actual Progress player writer failed inside the native sample boundary.");
                        }
                        else state.ControllerUnavailable();
                    }
                    if (mode.Contains("media", StringComparison.Ordinal)) api.DuringChannelMediaRead = Inject;
                    else if (mode.Contains("dsp", StringComparison.Ordinal)) api.DuringRootedDspRead = Inject;
                    else api.DuringRootedPitchRead = Inject;
                    var failure = Window("getter-reentrant-current-authority-loss", () =>
                    {
                        try { Reject(() => provider.ReadPlaybackClock(origin), "The direct Clock returned a stale sample after getter run/source replacement."); }
                        finally { api.DuringRootedPitchRead = null; api.DuringRootedDspRead = null; api.DuringChannelMediaRead = null; }
                        Check(injected == 1, "The selected actual getter did not execute its replacement exactly once.");
                        Reject(oldSource.VerifyAuthority, "A prepared old source survived getter replacement.");
                        Reject(() => oldWriter.Write(4), "A prepared old writer modified the replacement source.");
                        Check(!lease.Execute(Request(4)).Success, "The old SDK lease executed after getter replacement.");
                        NoOldSdkPlayback();
                    }, narrow: false);
                    Check(failure.After.NativePitchCalls == failure.Before.NativePitchCalls
                        && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 1,
                        "Getter replacement performed a stale pitch write or fabricated restoration.");
                    if (sourceSwitch) Check(failure.After.SourceFence == failure.Before.SourceFence + 1
                        && Marshal.ReadIntPtr(api.GameplayProgress, 0x38) == successor,
                        "Getter replacement did not execute the actual native reference writer and source fence.");
                    windows.Add(new { name = "getter-interleave", boundary = mode, injected, rootsInsideGetter, actualReferenceWriter = sourceSwitch });
                }
                else if (mode is "operation-writer-equal" or "operation-writer-aba")
                {
                    using var oldSource = provider.Capture(origin); using var oldWriter = oldSource.PreparePitchWrite();
                    var attempts = mode == "operation-writer-aba" ? 2 : 1;
                    var failure = Window("actual-external-equal-or-aba-writer", () =>
                    {
                        api.GameplayApi.Call(api.GameplaySource, "set_pitch", NativeRuntime.Arg.Value(attempts == 2 ? 2f : 1f));
                        if (attempts == 2) api.GameplayApi.Call(api.GameplaySource, "set_pitch", NativeRuntime.Arg.Value(1f));
                        Reject(oldSource.VerifyAuthority, "An old source survived actual equal/ABA native attempts.");
                        Reject(oldWriter.VerifyCurrent, "An old writer survived actual equal/ABA native attempts.");
                        Reject(() => oldWriter.Write(4), "An old writer wrote after the native continuous fence advanced.");
                        Check(!provider.Probe().Supported && !lease.Execute(Request(4)).Success, "Equal final scalar readback revived old provider/SDK authority.");
                        NoOldSdkPlayback();
                    }, narrow: false);
                    Check(failure.After.NativePitchCalls == failure.Before.NativePitchCalls + attempts
                        && failure.After.WriterFence == failure.Before.WriterFence + (ulong)attempts
                        && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 1,
                        "Equal/ABA regression lacks actual distinct native setter attempts and continuous fence changes.");
                }
                else if (mode.StartsWith("operation-readback-", StringComparison.Ordinal))
                {
                    var rooted = ChannelPrivate<NativePlaybackStockRootedSource>(router, "_source");
                    var accessor = ChannelPrivate<NativePlaybackActualSource>(rooted, "_actual");
                    var handles = ChannelPrivate<List<NativeRuntime.RootHandle>>(accessor, "_roots");
                    var sourceRoots = api.CaptureChannelRootHandles(handles.Select(root => ChannelPrivate<uint>(root, "_id")));
                    var before = Snapshot(); var clockBefore = finalClock.Capture();
                    var failures = 0; var injections = 0; var callbacksAfterStore = 0; var postWriteReads = 0;
                    var pendingWasPrivate = false; var rootsDuringFailure = 0;
                    NativePlaybackStockSourceHub.WriterReservation? completedAttempt = null;
                    var ownedAttemptSequences = new HashSet<ulong>();
                    NativePlaybackStockRootedSource.PitchWrite? retainedWriter = null;
                    ChannelSnapshot? pendingSnapshot = null;
                    var interferenceWrites = mode == "operation-readback-writer-aba" ? 2
                        : mode == "operation-readback-writer-equal" ? 1 : 0;
                    var restored = mode is "operation-readback-once" or "operation-readback-confirm-once"
                        or "operation-readback-mismatch" or "operation-readback-dsp-once";
                    var confirmedDrift = mode == "operation-readback-confirm-drift";
                    const float externalPitch = 1.25f;
                    var canRestoreWrite = restored || mode == "operation-readback-always";
                    beforeSourceOperation = observation =>
                    {
                        if (observation.Role != NativePlaybackSourceRole.SetPitch) return;
                        var producer = ChannelPrivate<NativePlaybackStockOccurrenceProducer>(rooted, "_producer");
                        var reservation = typeof(NativePlaybackStockOccurrenceProducer)
                            .GetField("_pendingWriter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(producer)
                            as NativePlaybackStockSourceHub.WriterReservation;
                        if (reservation is null) return;
                        Check(ReferenceEquals(reservation.ActualAttempt, observation), "A recovery attempt lost its actual native reservation.");
                        completedAttempt ??= reservation; ownedAttemptSequences.Add(observation.Sequence);
                    };
                    afterSourceOperation = completion =>
                    {
                        if (completion.Observation.Role != NativePlaybackSourceRole.SetPitch
                            || !ownedAttemptSequences.Contains(completion.Observation.Sequence)) return;
                        ++callbacksAfterStore;
                        Check(completion is { Returned: true, Abnormal: false, Unknown: false, PhysicalExited: false },
                            "A recovery read failure did not follow a normal actual original return.");
                        if (callbacksAfterStore == 1)
                        {
                            Check(BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 2,
                                "The read failure was armed before the native requested pitch store.");
                            if (mode == "operation-readback-mismatch") api.WrongRootedPitchReadback = true;
                        }
                        else if (mode == "operation-readback-mismatch") api.WrongRootedPitchReadback = false;
                    };
                    var successor = mode == "operation-readback-source" ? api.CreateChannelSuccessorPlayer() : IntPtr.Zero;
                    api.ChannelReadException = name =>
                    {
                        if (callbacksAfterStore == 0) return false;
                        var dspFailure = mode == "operation-readback-dsp-once";
                        if (name != (dspFailure ? "get_DspTime" : "get_pitch")) return false;
                        ++postWriteReads;
                        if (confirmedDrift || mode is "operation-readback-confirm-once" or "operation-readback-confirm-always")
                        {
                            // The second pitch read belongs to Audio.Commit's
                            // separate verification, after the rooted writer
                            // already issued its actual successor receipt.
                            if (postWriteReads == 1) return false;
                        }
                        var always = mode is "operation-readback-always" or "operation-readback-confirm-always";
                        if (!always && failures != 0) return false;
                        ++failures;
                        retainedWriter = ChannelPrivate<NativePlaybackStockRootedSource.PitchWrite>(rooted, "_pitchWriter");
                        rootsDuringFailure = api.ActiveRoots;
                        Check(api.RetainsChannelRootHandles(sourceRoots) && !rooted.RootsReleased,
                            "The failed actual getter released the original seven roots before recovery.");
                        if (!always || failures == 1)
                        {
                            if (mode == "operation-readback-once")
                            {
                                var oldReceipt = ChannelPrivate<NativePlaybackStockRootedSource.ReadbackReceipt>(rooted, "_current");
                                Reject(oldReceipt.VerifyOperationIdentity, "Pending native pitch completion was exposed as an old API receipt.");
                                Reject(() => rooted.PreparePausedPitchWrite(oldReceipt), "Another pitch operation bypassed the retained writer.");
                                Reject(() => provider.Capture(origin), "Another source capture borrowed the pending pitch operation.");
                                var recovery = (INativePlaybackPitchRecovery)retainedWriter;
                                Reject(() => retainedWriter.Write(4), "A getter reentered its running native pitch writer.");
                                Reject(retainedWriter.RestoreOriginal, "A getter restored its running native pitch writer.");
                                Reject(retainedWriter.Dispose, "A getter released its running native pitch operation.");
                                Reject(((INativePlaybackPitchRecovery)Clone(retainedWriter)).RestoreOriginal,
                                    "A copied writer consumed the original recovery authority.");
                                Task.Run(() => Reject(recovery.RestoreOriginal,
                                    "A worker consumed the owner-thread recovery authority.")).GetAwaiter().GetResult();
                                Check(rooted.LastFailure is null && provider.Probe().Supported,
                                    "Pending-read/copy/worker rejection withdrew healthy actual recovery authority.");
                                pendingWasPrivate = true;
                            }
                            if (interferenceWrites != 0)
                            {
                                ++injections;
                                api.GameplayApi.Call(api.GameplaySource, "set_pitch", NativeRuntime.Arg.Value(interferenceWrites == 2 ? 1f : 2f));
                                if (interferenceWrites == 2) api.GameplayApi.Call(api.GameplaySource, "set_pitch", NativeRuntime.Arg.Value(2f));
                            }
                            else if (mode == "operation-readback-source")
                            {
                                ++injections;
                                api.GameplayApi.Call(api.GameplayProgress, "set_MusicPlayer", NativeRuntime.Arg.Ref(successor));
                            }
                            else if (mode == "operation-readback-progress")
                            {
                                ++injections; api.SetRootedGraphValue("progress.now", 9d);
                            }
                            else if (confirmedDrift)
                            {
                                Check(!retainedWriter.PendingRead && retainedWriter.ObserveReadbackIdentity().Sample.Pitch == 2f,
                                    "The external change must follow a complete, independently confirmed pitch write.");
                                ++injections;
                                // Change the owned fixture storage independently of the observed native writer.
                                Marshal.WriteInt32(api.GameplaySource, 16, BitConverter.SingleToInt32Bits(externalPitch));
                            }
                            pendingSnapshot = Snapshot();
                        }
                        return mode != "operation-readback-mismatch";
                    };
                    GameplayControlCommandSnapshot? terminal = null;
                    var failure = Window("completed-native-writer-api-recovery", () =>
                    {
                        try { terminal = QueuedSpeed(2); }
                        finally
                        {
                            api.ChannelReadException = null; api.WrongRootedPitchReadback = false;
                            beforeSourceOperation = null; afterSourceOperation = null;
                        }
                        Check(terminal!.State == (restored ? GameplayControlCommandState.Rejected : GameplayControlCommandState.Unknown)
                            && terminal.Error is not null && terminal.Receipt is null,
                            "Failed native readback lost its restored rejection or honest Unknown outcome: " + JsonSerializer.Serialize(terminal));
                        if (!restored) Check(!terminal.Error!.Retryable, "Unknown recovery failure remained retryable.");
                        Check(completedAttempt is not null, "No actual first native pitch attempt was retained.");
                        var firstAttempt = completedAttempt!.Completed;
                        Check(firstAttempt.Returned == 1 && firstAttempt.Abnormal == 0 && firstAttempt.ExceptionCode == 0
                            && firstAttempt.PhysicalExited == 1 && firstAttempt.PostWriter == firstAttempt.ExpectedWriter + 1,
                            "API recovery was inferred without the actual normal returned-and-exited setter attempt.");
                        Check(callbacksAfterStore == (canRestoreWrite ? 2 : 1),
                            "The requested setter or original-pitch restore was omitted, retried, or guessed after lost ownership.");
                        var audit = Need(plugin.Context.GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service!.Read());
                        Check(audit.Records.Count == 1 && audit.Records[0].Outcome == (restored
                            ? RunModificationOutcome.Restored : RunModificationOutcome.InvocationFailedSideEffectsUnknown),
                            "The journal misreported or duplicated the actual recovery outcome.");
                        var afterTerminal = Snapshot();
                        for (var index = 0; index != 3; ++index) module.PumpControlCommand();
                        Check(Snapshot().NativePitchCalls == afterTerminal.NativePitchCalls, "The failed command automatically repeated a native attempt.");
                        var actualPitch = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16));
                        Check(actualPitch == (confirmedDrift ? externalPitch : canRestoreWrite ? 1 : 2),
                            "The original or refused pitch recovery does not match actual source memory.");
                        if (restored)
                        {
                            Check(provider.Probe().Supported && ReferenceEquals(finalClock, session.Clock)
                                && ReferenceEquals(finalClock.Capture(), clockBefore) && finalClock.Capture().Rate == 1
                                && group.ReadOperationClockPolicy(group.GetTimeLease).Authority!.Revision == clockBefore.Revision,
                                "An audio-only recovered failure changed or withdrew the untouched actual Clock policy.");
                            var readout = provider.ReadPlaybackClock(origin);
                            Check(readout.State.Rate == 1 && readout.MediaSeconds == 3, "Independent recovered Clock reading differs from its actual baseline.");
                            if (mode == "operation-readback-once") Check(pendingWasPrivate, "The pending readback never exercised private recovery restrictions.");
                        }
                        else
                        {
                            NoOldSdkPlayback();
                            if (retainedWriter is not null) Reject(retainedWriter.RestoreOriginal, "Disposed or failed recovery authority allowed another original-pitch attempt.");
                        }
                        windows.Add(new { name = "readback-recovery-terminal", terminal, mode, failures, injections, callbacksAfterStore,
                            postWriteReads, pendingWasPrivate, rootsDuringFailure, pendingSnapshot,
                            sourceRoots = sourceRoots.Select(row => new { id = row.Key, target = row.Value.ToInt64() }),
                            actualFirstAttempt = new { firstAttempt.AttemptSequence, firstAttempt.AttemptToken, firstAttempt.Returned,
                                firstAttempt.Abnormal, firstAttempt.PhysicalExited, firstAttempt.ExpectedWriter, firstAttempt.PostWriter },
                            restored, actualPitch, clockBefore, clockAfter = finalClock.Capture() });
                    }, narrow: false);
                    var expectedOwnWrites = canRestoreWrite ? 2 : 1;
                    Check(failure.After.NativePitchCalls == before.NativePitchCalls + expectedOwnWrites + interferenceWrites
                        && failure.After.WriterFence == before.WriterFence + (ulong)(expectedOwnWrites + interferenceWrites)
                        && failure.After.NativeTimeCalls == before.NativeTimeCalls && failure.After.SourceInFlight == 0,
                        "Recovery lacks its exact native attempts, continuous fences or actual physical exits.");
                    if (mode == "operation-readback-source")
                        Check(failure.After.SourceFence == before.SourceFence + 1
                            && Marshal.ReadIntPtr(api.GameplayProgress, 0x38) == successor,
                            "Readback source rejection did not observe the actual native reference writer.");
                    else Check(failure.After.SourceFence == before.SourceFence, "A pitch recovery changed the source fence.");
                    Check(failure.After.SchedulingFence == before.SchedulingFence, "Pitch recovery advanced a stock scheduling occurrence.");
                    Check(failures > 0 && (confirmedDrift || mode == "operation-readback-progress" || interferenceWrites != 0 || successor != IntPtr.Zero ? injections == 1 : injections == 0),
                        "The intended actual getter recovery/fault boundary did not execute.");
                    if (interferenceWrites != 0)
                    {
                        // These are fully observed owner-thread originals with
                        // known exits. They withdraw logical recovery authority;
                        // they do not manufacture a fault in the physical channel.
                        Check(!provider.Probe().Supported && finalClock.Capture().Phase == NativePlaybackPhase.Withdrawn
                            && !rooted.RootsReleased,
                            "An equal/ABA external native writer revived logical recovery or released live source roots.");
                    }
                }
                else if (mode is "operation-write-throw" or "operation-publication-fault")
                {
                    var injected = 0; var revision = finalClock.Capture().Revision;
                    NativePlaybackStockSourceHub.WriterReservation? actualAttempt = null;
                    beforeSourceOperation = observation =>
                    {
                        if (observation.Role != NativePlaybackSourceRole.SetPitch) return;
                        var rooted = ChannelPrivate<NativePlaybackStockRootedSource>(router, "_source");
                        var producer = ChannelPrivate<NativePlaybackStockOccurrenceProducer>(rooted, "_producer");
                        actualAttempt = ChannelPrivate<NativePlaybackStockSourceHub.WriterReservation>(producer, "_pendingWriter");
                        Check(ReferenceEquals(actualAttempt.ActualAttempt, observation), "The actual SetPitch Before did not match its retained native reservation.");
                    };
                    if (mode == "operation-write-throw") Marshal.WriteInt32(api.GameplaySource, 68, 1);
                    else afterSourceOperation = completion =>
                    {
                        if (completion.Observation.Role != NativePlaybackSourceRole.SetPitch) return;
                        afterSourceOperation = null; ++injected;
                        Check(completion is { Returned: true, Abnormal: false, PhysicalExited: false },
                            "Publication failure did not follow the actual successful SetPitch original return.");
                        Check(BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 2,
                            "Publication failure was injected before the actual native setter stored its value.");
                        Check(group.GetTimeLease.Withdraw(revision), "The exact actual native policy could not be withdrawn after its pitch setter.");
                    };
                    GameplayControlCommandSnapshot? terminal = null;
                    var failure = Window("actual-write-or-publication-failure", () =>
                    {
                        try { terminal = QueuedSpeed(2); }
                        finally { Marshal.WriteInt32(api.GameplaySource, 68, 0); beforeSourceOperation = null; afterSourceOperation = null; }
                        Check(terminal!.State == GameplayControlCommandState.Unknown && terminal.Error is { Retryable: false },
                            "A real post-store native/publication failure was reported as confirmed or restored: " + JsonSerializer.Serialize(terminal));
                        for (var index = 0; index != 3; ++index) module.PumpControlCommand();
                        Check(!lease.Execute(Request(3)).Success, "A failed original attempt enabled a new stale SDK write.");
                        NoOldSdkPlayback();
                    }, narrow: false);
                    // Sticky physical-fault pumping may run the explicit diagnostic
                    // audit. It is counted separately from healthy operation claims.
                    Check(failure.After.NativePitchCalls == failure.Before.NativePitchCalls + 1
                        && failure.After.WriterFence == failure.Before.WriterFence + 1
                        && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 2,
                        "A failed actual attempt was skipped, retried, or restored without live ownership.");
                    Check(actualAttempt is not null, "The actual failed setter did not retain its native writer reservation.");
                    var completedAttempt = actualAttempt!.Completed;
                    Check(completedAttempt.Version == 1 && completedAttempt.Size == 224 && completedAttempt.AttemptSequence != 0
                        && completedAttempt.PhysicalExited == 1 && completedAttempt.PostWriter == completedAttempt.ExpectedWriter + 1,
                        "The failed setter lacks its actual native writer attempt and post-fence physical-exit record.");
                    if (mode == "operation-write-throw")
                    {
                        physicalUnknown = true;
                        Check((failure.After.NativeFlags & (uint)NativePlaybackSourceFlags.Unknown) != 0
                            && failure.After.NativeFaults > 0 && failure.After.SourceInFlight == 0,
                            "The actual native SEH after store lacks sticky Unknown or a physical exit.");
                        Check(completedAttempt.Abnormal == 1 && completedAttempt.ExceptionCode == 0xe2400011u,
                            "The actual retained native writer did not record the fixture post-store SEH code.");
                    }
                    else Check(injected == 1 && !group.ReadOperationClockPolicy(group.GetTimeLease).Active,
                        "The real Clock publication failure was not injected or revived its old native policy.");
                    var audit = Need(plugin.Context.GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service!.Read());
                    Check(audit.Records.Count == 1 && audit.Records[0].Outcome == RunModificationOutcome.InvocationFailedSideEffectsUnknown,
                        "The actual journal erased or duplicated the SDK Unknown outcome.");
                    windows.Add(new { name = "failure-command-terminal", terminal, nativePostStoreSeh = mode == "operation-write-throw", injectedPolicyWithdrawals = injected,
                        actualWriter = new { completedAttempt.State, completedAttempt.AttemptSequence, completedAttempt.AttemptToken,
                            completedAttempt.Returned, completedAttempt.Abnormal, completedAttempt.PhysicalExited, completedAttempt.ExceptionCode,
                            completedAttempt.ExpectedWriter, completedAttempt.PostWriter } });
                }
                else if (mode == "operation-publication-inflight")
                {
                    var rooted = ChannelPrivate<NativePlaybackStockRootedSource>(router, "_source");
                    var producer = ChannelPrivate<NativePlaybackStockOccurrenceProducer>(rooted, "_producer");
                    var rootedAccessor = ChannelPrivate<NativePlaybackActualSource>(rooted, "_actual");
                    var rootedHandles = ChannelPrivate<List<NativeRuntime.RootHandle>>(rootedAccessor, "_roots");
                    var sourceRoots = api.CaptureChannelRootHandles(rootedHandles.Select(root => ChannelPrivate<uint>(root, "_id")));
                    Check(sourceRoots.Length == 7 && sourceRoots.All(row => row.Key != 0)
                        && sourceRoots.Select(row => row.Key).Distinct().Count() == 7
                        && sourceRoots.Select(row => row.Value).SequenceEqual(new[] { api.GameplayController, api.GameplayInformation,
                            api.GameplayProgress, api.GameplayPause, api.GameplayPlayer, api.GameplaySource, api.GameplayClip }),
                        "The publication case did not retain the actual source's original seven handle identities.");
                    var slot = ChannelPrivate<uint>(actualClockBridge, "_slot");
                    var readSingleStats = Export<ChannelReadSingleStats>(ChannelPrivate<IntPtr>(actualClockBridge, "_library"),
                        "RizSingleReturnPrototype_GetStats");
                    Check(Marshal.SizeOf<ChannelSingleStats>() == 112, "The actual native Single stats wire is not V1/112B.");
                    ChannelSingleSnapshot NativeClockStats()
                    {
                        var value = new ChannelSingleStats { Version = 1, Size = 112 };
                        Check(readSingleStats(slot, ref value) == 0 && value.Version == 1 && value.Size == 112 && value.Configured == 1
                            && value.Entered >= value.Exited && value.Entered - value.Exited == value.InFlight,
                            "The actual native Single export lost its configured slot or physical entry/exit accounting.");
                        return new(value.Entered, value.Exited, value.InFlight, value.OriginalCalls, value.EnterFaults, value.ExitFaults,
                            value.SourceFaults, value.Applied, value.Declined, value.Withdrawals,
                            value.Unknown, value.Configured, value.ObserverEnabled, value.AuthorityActive, value.PolicyGeneration);
                    }
                    var clockStateBefore = finalClock.Capture(); var clockBefore = NativeClockStats();
                    var sourceBefore = Snapshot(); var clockReadersBefore = Volatile.Read(ref nativeClockReaders);
                    var ownerThread = Environment.CurrentManagedThreadId; var workerThread = 0;
                    var foreignMethod = IntPtr.Add(targets.GetTime.MethodInfo, 8);
                    Check(foreignMethod != IntPtr.Zero && foreignMethod != targets.GetTime.MethodInfo
                        && clockBefore is { Unknown: 0, InFlight: 0, AuthorityActive: 1, ObserverEnabled: 1 }
                        && clockBefore.PolicyGeneration == (ulong)clockStateBefore.Revision
                        && actualClockBridge.ReadHealth().Known && group.ReadOperationClockPolicy(group.GetTimeLease).Active,
                        "The post-store publication case did not start with its actual healthy active Clock and a foreign nonzero method.");
                    var blockGetTime = Export<ChannelNativeAction>(native, "StockFixtureBlockGetTime");
                    var waitGetTime = Export<ChannelNativeWait>(native, "StockFixtureWaitGetTime");
                    var releaseGetTime = Export<ChannelNativeAction>(native, "StockFixtureReleaseGetTime");
                    Task<float>? worker = null; var blockRequested = false; var originalEntered = false;
                    var originalReleased = false; var originalJoined = false; var originalValue = float.NaN;
                    var beforeCallbacks = 0; var injected = 0; var callbackThread = 0; var rootsInsideCallback = 0;
                    NativePlaybackSourceObservation? actualBefore = null;
                    NativePlaybackStockSourceHub.WriterReservation? actualAttempt = null;
                    ChannelSnapshot? sourceAfterStore = null, sourceAtUnknown = null;
                    ChannelSingleSnapshot? clockAfterStore = null, clockBlocked = null, clockAtUnknown = null, clockAfterRefusals = null;
                    GameplayControlCommandSnapshot? terminal = null;
                    api.ChannelSourceCallbackInjectionError = null; api.ChannelSourceCallbackInjectionStage = null;
                    void RequireSourceRoots()
                        => Check(api.RetainsChannelRootHandles(sourceRoots) && !rooted.RootsReleased && !router.RootsReleased,
                            "The native GetTime borrower lost an original source handle before physical responsibility retired.");
                    void RequireBlockedClock(ChannelSingleSnapshot value, bool active)
                        => Check(value.InFlight == 1 && value.Entered == clockBefore.Entered + 1 && value.Exited == clockBefore.Exited
                            && value.OriginalCalls == clockBefore.OriginalCalls + 1 && value.Applied == clockBefore.Applied
                            && value.Declined == clockBefore.Declined && value.PolicyGeneration == clockBefore.PolicyGeneration
                            && value.AuthorityActive == (active ? 1u : 0u) && value.Unknown == (active ? 0u : 1u),
                            "The post-store borrower exited early, duplicated its original, or changed the retained native policy.");
                    beforeSourceOperation = observation =>
                    {
                        if (observation.Role != NativePlaybackSourceRole.SetPitch) return;
                        api.RunChannelSourceCallbackInjection("actual-setpitch-before", () =>
                        {
                            ++beforeCallbacks; actualBefore = observation;
                            actualAttempt = ChannelPrivate<NativePlaybackStockSourceHub.WriterReservation>(producer, "_pendingWriter");
                            Check(beforeCallbacks == 1 && ReferenceEquals(actualAttempt.ActualAttempt, observation)
                                && observation.Self == api.GameplaySource
                                && observation.ArgBits == unchecked((uint)BitConverter.SingleToInt32Bits(2f))
                                && Environment.CurrentManagedThreadId == ownerThread,
                                "The actual SetPitch Before missed its once-only source/writer reservation or owner thread.");
                            RequireSourceRoots();
                        });
                    };
                    afterSourceOperation = completion =>
                    {
                        if (completion.Observation.Role != NativePlaybackSourceRole.SetPitch) return;
                        api.RunChannelSourceCallbackInjection("actual-setpitch-returned-gettime-block", () =>
                        {
                            afterSourceOperation = null; ++injected; callbackThread = Environment.CurrentManagedThreadId;
                            Check(injected == 1 && completion is { Returned: true, Abnormal: false, PhysicalExited: false, Unknown: false }
                                && actualAttempt is not null && ReferenceEquals(actualBefore, completion.Observation)
                                && ReferenceEquals(actualAttempt.ActualAttempt, completion.Observation) && callbackThread == ownerThread,
                                "The interference missed the actual normal SetPitch return before its physical finally exit.");
                            sourceAfterStore = Snapshot(); rootsInsideCallback = api.ActiveRoots;
                            Check(sourceAfterStore.NativePitchCalls == sourceBefore.NativePitchCalls + 1
                                && sourceAfterStore.WriterFence == sourceBefore.WriterFence + 1
                                && sourceAfterStore.SourceFence == sourceBefore.SourceFence && sourceAfterStore.SchedulingFence == sourceBefore.SchedulingFence
                                && sourceAfterStore.Entered == sourceBefore.Entered + 1 && sourceAfterStore.Exited == sourceBefore.Exited
                                && sourceAfterStore.SourceInFlight == 1
                                && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 2,
                                "GetTime interference preceded the actual single pitch store/fence or followed its physical exit.");
                            clockAfterStore = NativeClockStats();
                            Check(clockAfterStore == clockBefore && actualClockBridge.ReadHealth() is { Known: true, InFlight: 0 }
                                && group.ReadOperationClockPolicy(group.GetTimeLease).Active,
                                "The actual Clock was already occupied or faulted before the post-store worker began.");
                            RequireSourceRoots(); blockRequested = true; blockGetTime();
                            worker = Task.Run(() =>
                            {
                                Volatile.Write(ref workerThread, Environment.CurrentManagedThreadId);
                                return getTime(api.GameplayPlayer, foreignMethod);
                            });
                            Check(waitGetTime() == 0, "The actual post-store native GetTime original did not enter its blocking event.");
                            originalEntered = true; clockBlocked = NativeClockStats(); RequireBlockedClock(clockBlocked, active: true);
                            Check(actualClockBridge.ReadHealth() is { Known: true, InFlight: 1 }
                                && group.ReadOperationClockPolicy(group.GetTimeLease).Active
                                && Volatile.Read(ref workerThread) != 0 && Volatile.Read(ref workerThread) != callbackThread
                                && Volatile.Read(ref nativeClockReaders) == clockReadersBefore,
                                "The post-store foreign-method worker faulted before publication or selected the owner-thread DSP reader.");
                            RequireSourceRoots();
                            windows.Add(new { name = "actual-setpitch-returned-native-gettime-injection", callbackThread,
                                workerThread = Volatile.Read(ref workerThread), rootsInsideCallback, sourceAfterStore, clockAfterStore, clockBlocked,
                                returned = completion.Returned, abnormal = completion.Abnormal, physicalExited = completion.PhysicalExited });
                        });
                    };
                    try
                    {
                        Window("sdk-post-store-native-publication-inflight-unknown", () =>
                        {
                            try { terminal = QueuedSpeed(2); }
                            finally
                            {
                                beforeSourceOperation = null; afterSourceOperation = null;
                                // The real observer catches callback exceptions. An injection
                                // assertion must fail this test before any product refusal counts.
                                if (api.ChannelSourceCallbackInjectionError is { } injectionError)
                                    throw new InvalidOperationException("Fixture source callback injection failed at "
                                        + api.ChannelSourceCallbackInjectionStage + ".", injectionError);
                            }
                            Check(beforeCallbacks == 1 && injected == 1 && originalEntered && worker is { IsCompleted: false }
                                && terminal is { State: GameplayControlCommandState.Unknown, Receipt: null, Error.Retryable: false }
                                && terminal.Error.Message.Contains("Controlled Single clock publication rejected: 8.", StringComparison.Ordinal),
                                "The actual queued command did not become Unknown at the native in-flight publication fence: " + JsonSerializer.Serialize(terminal));
                            clockAtUnknown = NativeClockStats(); RequireBlockedClock(clockAtUnknown, active: false);
                            sourceAtUnknown = Snapshot(); var completed = actualAttempt!.Completed;
                            Check(completed.Version == 1 && completed.Size == 224 && completed.State == 3
                                && completed.AttemptSequence == actualBefore!.Sequence && completed.AttemptToken != 0
                                && completed.Returned == 1 && completed.Abnormal == 0 && completed.PhysicalExited == 1
                                && completed.Contended == 0 && completed.ExceptionCode == 0 && completed.PostWriter == completed.ExpectedWriter + 1
                                && completed.PostSource == completed.ExpectedSource && completed.PostScheduling == completed.ExpectedScheduling
                                && completed.Owner == unchecked((ulong)actualHub.NativeOwner.ToInt64()) && completed.OwnerEpoch == actualHub.Epoch,
                                "Publication Unknown lost the actual normal writer's once-only physical completion and post fences.");
                            Check(sourceAtUnknown.NativePitchCalls == sourceBefore.NativePitchCalls + 1
                                && sourceAtUnknown.WriterFence == sourceBefore.WriterFence + 1
                                && sourceAtUnknown.Entered == sourceBefore.Entered + 1 && sourceAtUnknown.Exited == sourceBefore.Exited + 1
                                && sourceAtUnknown.SourceInFlight == 0
                                && (sourceAtUnknown.NativeFlags & (uint)NativePlaybackSourceFlags.Unknown) != 0
                                && sourceAtUnknown.NativeFaults > sourceBefore.NativeFaults
                                && clockAtUnknown.Withdrawals > clockBefore.Withdrawals
                                && !group.IsActive && !group.GetTimeLease.IsActive
                                && finalClock.Capture() is { Phase: NativePlaybackPhase.Withdrawn },
                                "The publication failure did not close actual native/Clock authority while retaining the pending original.");
                            RequireSourceRoots();
                            for (var index = 0; index != 3; ++index) module.PumpControlCommand();
                            Reject(group.RequireRateOperationAdmission, "Native publication Unknown admitted another rate capture.");
                            Check(!provider.Probe().Supported && !lease.Execute(Request(3)).Success,
                                "The native publication failure revived old provider or SDK write authority.");
                            NoOldSdkPlayback(); provider.PumpCompleted();
                            clockAfterRefusals = NativeClockStats(); RequireBlockedClock(clockAfterRefusals, active: false); RequireSourceRoots();
                            var afterRefusals = Snapshot();
                            Check(afterRefusals.NativePitchCalls == sourceAtUnknown.NativePitchCalls
                                && afterRefusals.NativeTimeCalls == sourceBefore.NativeTimeCalls
                                && afterRefusals.WriterFence == sourceAtUnknown.WriterFence && afterRefusals.SourceFence == sourceBefore.SourceFence
                                && afterRefusals.SchedulingFence == sourceBefore.SchedulingFence
                                && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 2
                                && Volatile.Read(ref nativeClockReaders) == clockReadersBefore && worker is { IsCompleted: false },
                                "Old SDK refusals or fault pumping retried/restored the actual store, sampled the native Clock, or drained the borrower early.");
                            var audit = Need(plugin.Context.GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service!.Read());
                            Check(audit.Records.Count == 1 && audit.Records[0].Outcome == RunModificationOutcome.InvocationFailedSideEffectsUnknown,
                                "The journal erased or duplicated the actual publication-fence Unknown outcome.");
                            windows.Add(new { name = "native-publication-fence-unknown-terminal", terminal, clockAtUnknown, clockAfterRefusals,
                                sourceAtUnknown, actualWriter = new { completed.State, completed.AttemptSequence, completed.AttemptToken,
                                    completed.Returned, completed.Abnormal, completed.PhysicalExited, completed.ExceptionCode,
                                    completed.ExpectedWriter, completed.PostWriter, completed.PostSource, completed.PostScheduling } });
                        }, narrow: false);
                    }
                    finally
                    {
                        beforeSourceOperation = null; afterSourceOperation = null;
                        if (blockRequested) { releaseGetTime(); originalReleased = true; }
                        Exception? workerFailure = null;
                        if (worker is not null)
                        {
                            // A timed-out borrower must never reach FakeNative.Dispose,
                            // which would free its backing storage during the original.
                            if (!ReferenceEquals(Task.WhenAny(worker, Task.Delay(10000)).GetAwaiter().GetResult(), worker))
                                Environment.FailFast("The released post-store native GetTime original did not drain within ten seconds; backing storage must remain retained.");
                            originalJoined = true;
                            try { originalValue = worker.GetAwaiter().GetResult(); }
                            catch (Exception error) { workerFailure = error; }
                        }
                        windows.Add(new { name = "post-store-native-borrower-finally", originalEntered, originalReleased, originalJoined,
                            fixtureInjectionStage = api.ChannelSourceCallbackInjectionStage,
                            fixtureInjectionError = api.ChannelSourceCallbackInjectionError?.Message, workerFailure = workerFailure?.Message });
                        if (workerFailure is not null)
                        {
                            if (api.ChannelSourceCallbackInjectionError is { } injectionError)
                                throw new AggregateException("The fixture source callback injection and actual native worker failed independently.",
                                    injectionError, workerFailure);
                            throw new InvalidOperationException("The actual post-store native GetTime worker failed after release.", workerFailure);
                        }
                    }
                    Window("publication-unknown-after-actual-native-drain", () =>
                    {
                        var drained = NativeClockStats();
                        Check(originalEntered && originalReleased && originalJoined && float.IsFinite(originalValue) && originalValue == 3
                            && drained.Entered == clockBefore.Entered + 1 && drained.Exited == clockBefore.Exited + 1 && drained.InFlight == 0
                            && drained.OriginalCalls == clockBefore.OriginalCalls + 1 && drained.Applied == clockBefore.Applied
                            && drained.Declined == clockBefore.Declined + 1 && drained.Unknown == 1 && drained.AuthorityActive == 0
                            && drained.PolicyGeneration == clockBefore.PolicyGeneration,
                            "The actual native return was duplicated, applied an old candidate, or revived policy after publication Unknown.");
                        RequireSourceRoots();
                        for (var index = 0; index != 3; ++index) module.PumpControlCommand();
                        Check(!lease.Execute(Request(4)).Success && !provider.Probe().Supported,
                            "Draining a faulted original revived old SDK/provider write authority.");
                        NoOldSdkPlayback(); provider.PumpCompleted(); RequireSourceRoots();
                        var afterDrainPumps = NativeClockStats(); var sourceAfterDrain = Snapshot();
                        Check(afterDrainPumps.Entered == drained.Entered && afterDrainPumps.Exited == drained.Exited
                            && afterDrainPumps.InFlight == 0 && afterDrainPumps.OriginalCalls == drained.OriginalCalls
                            && afterDrainPumps.Applied == drained.Applied && afterDrainPumps.AuthorityActive == 0
                            && afterDrainPumps.Unknown == 1 && afterDrainPumps.PolicyGeneration == clockBefore.PolicyGeneration
                            && sourceAfterDrain.NativePitchCalls == sourceBefore.NativePitchCalls + 1
                            && sourceAfterDrain.NativeTimeCalls == sourceBefore.NativeTimeCalls
                            && sourceAfterDrain.WriterFence == sourceBefore.WriterFence + 1
                            && sourceAfterDrain.SourceFence == sourceBefore.SourceFence && sourceAfterDrain.SchedulingFence == sourceBefore.SchedulingFence
                            && sourceAfterDrain.SourceInFlight == 0
                            && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 2
                            && finalClock.Capture().Phase == NativePlaybackPhase.Withdrawn
                            && Volatile.Read(ref nativeClockReaders) == clockReadersBefore,
                            "Fault drain pumping restored/retried a write, republished old policy, or invoked the owner-thread native Clock reader.");
                        physicalUnknown = true;
                        windows.Add(new { name = "actual-publication-inflight-native-evidence", slot, clockBefore, clockAfterStore, clockBlocked,
                            clockAtUnknown, clockAfterRefusals, drained, afterDrainPumps, sourceBefore, sourceAfterStore, sourceAtUnknown, sourceAfterDrain,
                            clockStateBefore, clockStateAfter = finalClock.Capture(), ownerThread, callbackThread,
                            workerThread = Volatile.Read(ref workerThread), sameWatchedPlayer = true, foreignNonzeroMethod = foreignMethod.ToInt64(),
                            expectedDispositionBeforeFault = "WrongPhysicalIdentity", originalValue, originalEntered, originalReleased, originalJoined,
                            beforeCallbacks, injected, rootsInsideCallback, sourceRoots = sourceRoots.Select(row => new { id = row.Key, target = row.Value.ToInt64() }),
                            originalSourceHandlesRetained = api.RetainsChannelRootHandles(sourceRoots), terminal,
                            nativeClockReadersBefore = clockReadersBefore, nativeClockReadersAfter = Volatile.Read(ref nativeClockReaders) });
                    }, narrow: false);
                    verifyPublicationRootsAfterOwnerRelease = () =>
                    {
                        RequireSourceRoots(); var clockAfterRelease = NativeClockStats(); var sourceAfterRelease = Snapshot();
                        Check(clockAfterRelease.Unknown == 1 && clockAfterRelease.InFlight == 0 && clockAfterRelease.AuthorityActive == 0
                            && clockAfterRelease.PolicyGeneration == clockBefore.PolicyGeneration
                            && clockAfterRelease.Entered == clockBefore.Entered + 1 && clockAfterRelease.Exited == clockBefore.Exited + 1
                            && clockAfterRelease.OriginalCalls == clockBefore.OriginalCalls + 1 && clockAfterRelease.Applied == clockBefore.Applied
                            && sourceAfterRelease.NativePitchCalls == sourceBefore.NativePitchCalls + 1
                            && sourceAfterRelease.WriterFence == sourceBefore.WriterFence + 1
                            && sourceAfterRelease.NativeTimeCalls == sourceBefore.NativeTimeCalls
                            && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource, 16)) == 2
                            && Volatile.Read(ref nativeClockReaders) == clockReadersBefore,
                            "Final SDK owner release revived the old policy, restored/retried a write, or released physical Unknown source responsibility.");
                        windows.Add(new { name = "publication-unknown-original-roots-after-owner-release", clockAfterRelease, sourceAfterRelease,
                            originalSourceHandlesRetained = api.RetainsChannelRootHandles(sourceRoots), activeRoots = api.ActiveRoots });
                    };
                }
                else if (mode == "operation-inflight")
                {
                    var clockBefore = actualClockBridge.ReadHealth(); var roots = api.ActiveRoots;
                    Export<ChannelNativeAction>(native, "StockFixtureBlockGetTime")();
                    // Native declines this foreign nonzero metadata identity
                    // before selecting the owner-thread DSP reader. The actual
                    // original still retains its native return responsibility.
                    var foreignMethod = IntPtr.Add(targets.GetTime.MethodInfo, 8);
                    var blocked = Task.Run(() => getTime(api.GameplayPlayer, foreignMethod));
                    try
                    {
                        Check(Export<ChannelNativeWait>(native, "StockFixtureWaitGetTime")() == 0, "The actual native GetTime original did not enter its blocking event.");
                        Window("actual-inflight-gettime-command-refusal", () =>
                        {
                            Check(actualClockBridge.ReadHealth() is { Known: true, InFlight: 1 }, "The blocked original lacks actual native in-flight responsibility.");
                            Reject(group.RequireRateOperationAdmission, "Rate admission ignored an actual GetTime call already in flight.");
                            Reject(() => provider.Capture(origin), "A fresh rate source ignored the actual Clock preflight.");
                            Check(group.ReadOperationClockPolicy(group.GetTimeLease).Active && api.ActiveRoots == roots,
                                "A refused in-flight capture withdrew current policy or freed original roots.");
                        });
                    }
                    finally
                    {
                        Export<ChannelNativeAction>(native, "StockFixtureReleaseGetTime")();
                        Check(blocked.Wait(10000), "The actual blocked original did not leave after release.");
                    }
                    Check(float.IsFinite(blocked.Result) && actualClockBridge.ReadHealth() is { Known: true, InFlight: 0 }
                        && actualClockBridge.ReadHealth().OriginalCalls == clockBefore.OriginalCalls + 1,
                        "The released actual original lacks one known physical completion.");
                    windows.Add(new { name = "actual-worker-original-only", sameWatchedPlayer = true,
                        foreignNonzeroMethod = foreignMethod.ToInt64(), expectedDisposition = "WrongPhysicalIdentity", originalCompleted = true });
                    Window("owner-retry-after-actual-native-drain", () => ConfirmSpeed(2));
                }
                else if (mode == "operation-gettime-readback-source")
                {
                    var originalBefore = actualClockBridge.ReadHealth().OriginalCalls;
                    var sampleBefore = Snapshot(); var clockBefore = finalClock.Capture();
                    var roots = api.ActiveRoots; var injected = 0; var dspGetterCalls = 0;
                    var rootsInsideGetter = 0; var successor = api.CreateChannelSuccessorPlayer();
                    var returnedNormally = false; uint returnBits = 0;
                    api.DuringRootedDspRead = () =>
                    {
                        if (++dspGetterCalls != 2) return; // After the media sample, at its final actual DSP read.
                        api.DuringRootedDspRead = null; ++injected; rootsInsideGetter = api.ActiveRoots;
                        api.GameplayApi.Call(api.GameplayProgress, "set_MusicPlayer", NativeRuntime.Arg.Ref(successor));
                    };
                    try
                    {
                        var value = getTime(api.GameplayPlayer, targets.GetTime.MethodInfo);
                        returnBits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
                        returnedNormally = true;
                    }
                    finally { api.DuringRootedDspRead = null; }
                    var after = Snapshot(); var nativeHealth = actualClockBridge.ReadHealth();
                    Check(injected == 1 && dspGetterCalls == 2 && rootsInsideGetter >= 7 && rootsInsideGetter == roots,
                        "The actual GetTime sample did not reach its one final DSP source-change boundary with retained roots.");
                    Check(returnedNormally && returnBits == unchecked((uint)BitConverter.SingleToInt32Bits(3))
                        && nativeHealth.OriginalCalls == originalBefore + 1 && nativeHealth.InFlight == 0
                        && !nativeHealth.AuthorityActive && nativeClockReaders == 1,
                        "The failed final GetTime source read replaced raw stock, repeated the original, or kept Clock authority.");
                    Check(after.NativePitchCalls == sampleBefore.NativePitchCalls
                        && after.NativeTimeCalls == sampleBefore.NativeTimeCalls
                        && after.SourceFence == sampleBefore.SourceFence + 1
                        && Marshal.ReadIntPtr(api.GameplayProgress, 0x38) == successor
                        && finalClock.Capture().Phase == NativePlaybackPhase.Withdrawn,
                        "Final GetTime source replacement fabricated a write/restore or left the old unique Clock live.");
                    Check(nativeHealth.Known && after.NativeFaults == sampleBefore.NativeFaults
                        && (after.NativeFlags & (uint)NativePlaybackSourceFlags.Unknown) == 0,
                        "A normal source replacement was mislabeled as an unknown physical failure.");
                    Check(!provider.Probe().Supported && !lease.Execute(Request(2)).Success,
                        "The old provider or SDK lease regained authority after native GetTime sample replacement.");
                    NoOldSdkPlayback();
                    // The actual source writer and GetTime original both exited
                    // normally. Logical withdrawal keeps roots while sampling,
                    // then permits their known drain at final owner release.
                    windows.Add(new { name = "actual-gettime-final-dsp-source-replacement", clockBefore, before = sampleBefore, after,
                        injected, dspGetterCalls, rootsInsideGetter, returnedNormally, returnBits, nativeHealth,
                        sameFinalClock, sourceReplaced = true, nativeClockReaders });
                }
                else if (mode == "operation-native-epoch")
                {
                    var setArmed = Export<ChannelSetArmed>(sourceNative, "RtPlaybackSourceSetArmed");
                    var markFault = Export<ChannelMarkFault>(sourceNative, "RtPlaybackSourceMarkFault");
                    Window("actual-native-wrong-epoch-refusal", () =>
                    {
                        var before = NativeOwner(); var wrong = checked(actualHub.Epoch + 1);
                        Check(setArmed(actualHub.NativeOwner, wrong, 0) == 0 && markFault(actualHub.NativeOwner, wrong) == 0,
                            "The real native source owner accepted another epoch.");
                        var after = NativeOwner();
                        Check(after.Flags == before.Flags && after.Faults == before.Faults && after.WriterSequence == before.WriterSequence
                            && group.ReadOperationClockPolicy(group.GetTimeLease).Active,
                            "A native wrong-epoch refusal changed the legitimate current policy or writer owner.");
                        var value = provider.ReadPlaybackClock(origin); Check(value.MediaSeconds == 3, "Wrong-epoch refusal damaged the legitimate current independent Clock.");
                    });
                    Window("actual-native-exact-epoch-sticky-unknown", () =>
                    {
                        Check(markFault(actualHub.NativeOwner, actualHub.Epoch) == 1, "The exact real native source epoch could not mark fault.");
                        var owner = NativeOwner(); Check((owner.Flags & NativePlaybackSourceFlags.Unknown) != 0 && owner.Faults > 0,
                            "The native fault export did not produce actual sticky Unknown.");
                        Reject(group.RequireRateOperationAdmission, "Actual native Unknown retained rate admission.");
                        Check(!provider.Probe().Supported && !lease.Execute(Request(2)).Success, "Actual native Unknown retained provider/SDK write authority.");
                        NoOldSdkPlayback();
                    }, narrow: false);
                    physicalUnknown = true;
                }
                else if (mode == "operation-audit")
                {
                    using (var mutation = new ChannelCodeMutation(unusedReplay))
                    {
                        Window("unused-native-body-mutation-current-paused-read-write", () =>
                        {
                            var value = provider.ReadPlaybackClock(origin);
                            Check(value.MediaSeconds == 3 && provider.Probe().Supported, "An unused machine-code mutation blocked the contracted narrow current-source read.");
                            ConfirmSpeed(2);
                        });
                        var audit = Window("explicit-full-audit-finds-actual-mapped-mutation", () =>
                            Reject(group.VerifyCurrent, "Explicit complete installation audit missed the actual unconsumed Replay body mutation."), narrow: false);
                        Check(audit.After.FullAudits > audit.Before.FullAudits && audit.After.FullImageAudits > audit.Before.FullImageAudits,
                            "Actual mapped-code diagnostic refusal lacks a complete body/image audit entry.");
                    }
                    Check(!group.IsActive && !group.GetTimeLease.IsActive && !provider.Probe().Supported,
                        "Restoring mapped machine code revived sticky physical installation authority.");
                    Check(!lease.Execute(Request(4)).Success, "A restored physical code byte revived old SDK write authority.");
                    physicalUnknown = true;
                    windows.Add(new { name = "bounded-unused-code-mutation", member = "Replay", offset = 512,
                        addressRva = unusedReplay.ToInt64() - native.ToInt64(), originalByte = 0x90, mutatedByte = 0x91, mappedPaddingRestored = true });
                }

                var beforeRelease = Snapshot(); lease.Dispose(); module.Release(); provider.PumpCompleted();
                Check(physicalUnknown ? api.ActiveRoots >= 7 && !router.RootsReleased : api.ActiveRoots == 0 && router.RootsReleased,
                    "Final owner release lost physical Unknown root responsibility or retained known drained roots.");
                verifyPublicationRootsAfterOwnerRelease?.Invoke();
                Check(broker.ReadActualPlaybackDobbyCallCounts() == (12, 12), "Running operations replaced the actual retained physical installation.");
                if (mode == "operation-normal")
                {
                    // All running consumers and lawful owner retries have
                    // finished. Retire the already drained physical group only
                    // at this existing final cleanup boundary.
                    group.Dispose();
                    Check(!group.IsActive && !group.GetTimeLease.IsActive && api.ActiveRoots == 0 && router.RootsReleased,
                        "The drained real group did not complete its final physical withdrawal.");
                    var withdrawn = Window("grouped-gettime-lease-operation-after-withdrawal", () =>
                        Reject(() => group.GetTimeLease.VerifyOperationCurrent(native, targets.GetTime.Address, targets.GetTime.MethodInfo),
                            "The new GetTime operation API accepted its withdrawn physical group."));
                    Check(withdrawn.After.ClockChecks == withdrawn.Before.ClockChecks + 1
                        && withdrawn.After.NativePitchCalls == withdrawn.Before.NativePitchCalls
                        && withdrawn.After.NativeTimeCalls == withdrawn.Before.NativeTimeCalls,
                        "Withdrawn GetTime operation refusal skipped its actual group check or invoked a native writer.");
                }
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "playback-channel-operation-regression", mode, passed = true, checks = _checks,
                    inputs, installed, windows, nativeEvents, beforeRelease, afterRelease = Snapshot(), activeRoots = api.ActiveRoots, physicalUnknownRetained = physicalUnknown,
                    nativeClockReaders, sameFinalClock, originalBits, actualPrepare = 12, actualCommit = 12,
                    sourceRoleCount = targets.SourceRoleCount, hasPlayerConstructor = group.HasPlayerConstructor,
                    nativeSuppressObserverScopeCovered = false,
                    managedClockFaultCallbacks = Volatile.Read(ref managedClockFaultCallbacks),
                    managedSourceFaultCallbacks = Volatile.Read(ref managedSourceFaultCallbacks),
                    formalFreshSourceScopeCovered = false, metadataSynthetic = true, controlledFixtureProvider = true,
                    productionAdmission = false, gameExecuted = false, installedInGame = false }));
                return 0;
            }
            finally { module.Release(); }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "playback-channel-operation-regression", mode, passed = false, checks = _checks,
                inputs, windows, nativeEvents, error = error.Message, productionAdmission = false, gameExecuted = false }));
            return 1;
        }
    }
}
