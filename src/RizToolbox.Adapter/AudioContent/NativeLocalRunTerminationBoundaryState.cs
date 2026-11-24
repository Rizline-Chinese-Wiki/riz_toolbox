using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal interface INativeLocalRunTerminationBoundaryBackend
{
    bool CleanupUnknown { get; }
    string? InstallationStage => null;
    // Filesystem/profile gate only. No native access or borrowed-root resolution.
    void RequireSupportedBuild();
    LocalRunBoundaryActivity ReadJudgeActivity() => new(0, false);
    ExactHookBroker.LocalProgressIsolationBinding ResolveAndBind(Func<IntPtr, ExactHookBroker.LocalProgressIdentity?> classify);
    bool RequiresInitializationIsolation => false;
    ExactHookBroker.LocalInitializationIsolationBinding? BindInitialization(
        ExactHookBroker.LocalProgressIsolationBinding progress,
        Func<IntPtr, ExactHookBroker.LocalInitializationIdentity?> classify,
        ExactHookBroker.LocalInitializationCallbacks callbacks) => null;
    NativeRuntime.RootHandle RetainInitializationStart(IntPtr start, IntPtr information)
        => throw new UnsupportedSignatureException("No exact LevelControl.Start input authority is available.");
    NativeRuntime.RootHandle RetainInitializationInformation(IntPtr information)
        => throw new UnsupportedSignatureException("No exact initialization information authority is available.");
    NativeRuntime.RootHandle RetainInitializationReturn(IntPtr parameters)
        => throw new UnsupportedSignatureException("No initialization return reference authority is available.");
    void VerifyInitializationReturn(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
        NativeRuntime.RootHandle progress, NativeRuntime.RootHandle start, NativeRuntime.RootHandle parameters, IDisposable ownedBundle)
        => throw new UnsupportedSignatureException("No owned initialization return graph authority is available.");
    void VerifyInitializationInput(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
        NativeRuntime.RootHandle progress, NativeRuntime.RootHandle start, IDisposable ownedBundle)
        => throw new UnsupportedSignatureException("No owned initialization input graph authority is available.");
    void VerifyInitializationCompletedGraph(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
        NativeRuntime.RootHandle progress, long metadata) => VerifyGraph(controller, information, progress, metadata);
    NativeRuntime.RootHandle RetainController(NativeRuntime.RootHandle borrowedController, long exactController);
    NativeRuntime.RootHandle RetainInformation(NativeRuntime.RootHandle controller);
    NativeRuntime.RootHandle RetainInitializingInformation(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle borrowedInformation)
        => throw new UnsupportedSignatureException("This backend has no verified pending initialization input authority.");
    NativeRuntime.RootHandle RetainProgress(NativeRuntime.RootHandle information);
    void VerifyGraph(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
        NativeRuntime.RootHandle progress, long metadata);
    void VerifyInitializingGraph(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
        NativeRuntime.RootHandle progress, long metadata)
        => throw new UnsupportedSignatureException("This backend has no verified initializing graph contract.");
    int Frame();
}
internal sealed class NativeLocalRunTerminationBoundaryStateException : InvalidOperationException
{
    internal NativeLocalRunTerminationBoundaryState Responsibility { get; }
    internal NativeLocalRunTerminationBoundaryStateException(NativeLocalRunTerminationBoundaryState responsibility, Exception error)
        : base("Native LOCAL termination boundary state is unconfirmed; process/root responsibility is retained without retry. "
            + responsibility.Describe().Failure, error)
        => Responsibility = responsibility;
}
internal sealed record NativeLocalRunTerminationBoundaryDiagnostics(int Records, int PublishedMappings,
    bool BindingActive, bool CleanupUnknown, bool Stopped, string? Failure);

/// <summary>NativeRuntime bridge for the sealed termination handler. Exact progress
/// identities are published only after the committed graph is independently rooted and
/// verified. The hook classifier reads immutable managed identities on every thread;
/// root targets, Unity fields and frame getters are accessed only by the main-thread pump.</summary>
internal sealed partial class NativeLocalRunTerminationBoundaryState : ILocalRunTerminationBoundaryState
{
    private const int MaximumProcessOwners = 32;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<NativeLocalRunTerminationBoundaryState> ProcessOwners = new();
    private static readonly HashSet<Record> ProcessRecords = new();
    private readonly IMainThreadDispatcher _main;
    private readonly LocalRunProtectionHost _host;
    private readonly INativeLocalRunTerminationBoundaryBackend _backend;
    private readonly Record?[] _records = new Record?[MaximumProcessOwners];
    private readonly Record?[] _published = new Record?[MaximumProcessOwners];
    private ExactHookBroker.LocalProgressIsolationBinding? _binding;
    private bool _busy, _pumping, _unknown, _stopAttempted, _stopped;
    private int? _lastFrame;
    private string? _failure, _installationStage;
    private sealed class Record
    {
        internal readonly int Slot;
        internal readonly ExactHookBroker.LocalProgressIdentity ReservedProgressIdentity;
        internal readonly ExactHookBroker.LocalInitializationIdentity ReservedInitializationIdentity;
        internal readonly NativeLocalInitializationSettingsReceipt SettingsReceipt = new();
        internal Record(int slot, LocalRunSessionIdentity identity)
        {
            Slot = slot; Identity = identity;
            ReservedProgressIdentity = new(identity.SessionId, identity.RunGeneration, 0, 0);
            ReservedInitializationIdentity = new(identity.SessionId, identity.RunGeneration, 0);
        }
        internal NativeLocalRunTerminationBoundaryState Owner = null!;
        internal LocalRunSessionRegistry.SessionHandle Session = null!;
        internal LocalRunSessionIdentity Identity = null!;
        internal LocalRunLaunchCommitState? Launch;
        internal long Controller, Metadata, Information;
        internal NativeRuntime.RootHandle? BorrowedController;
        // Controller/info/progress and the actual Start receiver/returned params are
        // preallocated in the same bounded run record, before any native attempt.
        internal readonly NativeRuntime.RootHandle?[] Roots = new NativeRuntime.RootHandle?[5];
        internal ExactHookBroker.LocalProgressIdentity? ProgressIdentity;
        internal ExactHookBroker.LocalInitializationIdentity? InitializationIdentity;
        internal bool GetterAttempted, GetterReturnedVerified;
        internal bool Unknown, FreeAttempted, Published, InitializingGraphAttempted, InitializedGraphVerified;
    }
    private NativeLocalRunTerminationBoundaryState(IMainThreadDispatcher main, LocalRunProtectionHost host,
        INativeLocalRunTerminationBoundaryBackend backend) => (_main, _host, _backend) = (main, host, backend);
    internal static int RetainedProcessOwnerCount { get { lock (ProcessGate) return ProcessOwners.Count; } }
    internal static int RetainedProcessRecordCount { get { lock (ProcessGate) return ProcessRecords.Count; } }
    internal static bool HasPendingWorkForHost(LocalRunProtectionHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        // An idle active binding is still host responsibility. It must be logically
        // detached by the pump before the five native session gates may stop.
        lock (ProcessGate) return ProcessOwners.Any(owner => ReferenceEquals(owner._host, host));
    }
    internal static bool HasUnknownWorkForHost(LocalRunProtectionHost host)
        => HasUnknownWorkForHost(host, out _);
    internal static bool HasUnknownWorkForHost(LocalRunProtectionHost host, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(host);
        failure = null;
        NativeLocalRunTerminationBoundaryState[] owners;
        lock (ProcessGate) owners = ProcessOwners.Where(owner => ReferenceEquals(owner._host, host)).ToArray();
        var unknown = false;
        foreach (var owner in owners)
        {
            if (!owner._main.IsMainThread) { unknown = true; failure ??= owner._failure; continue; }
            try
            {
                if (owner._unknown || owner._backend.CleanupUnknown || owner._binding is null || !owner._binding.IsActive
                    || owner._binding.Describe().UnknownObserved != 0 || !owner.InitializationHealthy())
                {
                    owner.Fail(new InvalidOperationException("Native termination isolation is inactive or has observed unknown work."));
                    unknown = true; failure ??= owner._failure;
                }
            }
            catch (Exception error) { owner.Fail(error); unknown = true; failure ??= owner._failure; }
        }
        return unknown;
    }
    internal static void TickForHost(LocalRunProtectionHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        NativeLocalRunTerminationBoundaryState[] owners;
        lock (ProcessGate) owners = ProcessOwners.Where(owner => ReferenceEquals(owner._host, host)).ToArray();
        foreach (var owner in owners)
        {
            if (!owner._main.IsMainThread || owner._pumping) continue;
            owner._pumping = true;
            try { owner.TickOwned(); }
            catch (Exception error) { owner.Fail(error); } // One unknown owner cannot stop its peers' retirement.
            finally { owner._pumping = false; }
        }
    }
    private void TickOwned()
    {
        _main.VerifyAccess();
        if (_busy || _stopped || _unknown || _backend.CleanupUnknown) return;
        if (_binding is null || !_binding.IsActive || _binding.Describe().UnknownObserved != 0 || !InitializationHealthy())
        { Fail(new InvalidOperationException("Native Progress isolation is no longer confirmed active.")); return; }
        foreach (var entry in _records)
        {
            if (entry is null || entry.Unknown) continue;
            if (entry.GetterReturnedVerified && entry.Launch is { } launch
                && launch.Read().Phase == LocalRunLaunchCommitPhase.Initializing)
                VerifyInitializedGraph(launch);
            if (_host.Sessions.Read(entry.Session).Phase == LocalRunSessionPhase.Closed) TryRetire(entry.Session);
            if (_unknown) return;
        }
        // Keep an empty, healthy bridge available for a stable handler's next committed
        // run. Host stop requests explicitly drain the idle binding before gate detach.
        if (!_records.Any(item => item is not null) && _host.Read().StopRequested) TryStop();
    }
    public bool IsReadyForHost(LocalRunSessionRegistry registry)
    {
        _main.VerifyAccess();
        return !_busy && IsHostReadyCore(registry) && InitializationDrained() && JudgeDrained() && _binding!.Describe().InFlight == 0;
    }
    private bool IsHostReadyCore(LocalRunSessionRegistry registry)
    {
        if (!ReferenceEquals(registry, _host.Sessions) || _unknown || _backend.CleanupUnknown || _stopped || _stopAttempted
            || _binding is null || !_binding.IsActive || !InitializationHealthy()) return false;
        var progress = _binding.Describe();
        if (progress.UnknownObserved != 0 || progress.UnknownInFlight != 0) return false;
        var initialization = ReadInitializationActivity();
        if (!initialization.Known || initialization.UnknownInFlight != 0) return false;
        var host = _host.Read(); var activity = _host.ReadNativeBoundaryActivity();
        return host.AllGatesInstalled && !host.StopRequested && !host.Stopped && !host.CleanupUnknown && activity.Known
            && _backend.ReadJudgeActivity().Known;
    }

    internal void ReservePending(LocalRunLaunchCommitState launch)
    {
        Guard(); ArgumentNullException.ThrowIfNull(launch); _busy = true;
        try
        {
            launch.VerifyPendingOwnership(_host.Sessions);
            var current = _host.Sessions.Read(launch.Session);
            if (!IsHostReadyCore(_host.Sessions) || current.ControllerIdentity != 0 || launch.HasNativeAttempt
                || current.Identity != launch.Identity || current.MetadataIdentity != launch.MetadataIdentity)
                throw new InvalidOperationException("Native graph slots must be reserved for this exact empty launch before native attempts.");
            var slot = Array.FindIndex(_records, item => item is null);
            if (slot < 0) throw new InvalidOperationException("The native termination bridge's bounded graph slots are exhausted.");
            var entry = new Record(slot, launch.Identity) { Owner = this, Session = launch.Session,
                Metadata = launch.MetadataIdentity, Launch = launch };
            lock (ProcessGate)
            {
                if (ProcessRecords.Count >= MaximumProcessOwners || ProcessRecords.Any(item => ReferenceEquals(item.Session, launch.Session)))
                    throw new InvalidOperationException("The exact launch is already mapped or the native graph process slots are exhausted.");
                ProcessRecords.EnsureCapacity(ProcessRecords.Count + 1); ProcessRecords.Add(entry); _records[slot] = entry;
            }
            try { launch.AttachBridge(this); }
            catch (Exception error) { entry.Unknown = true; Fail(error); throw; }
        }
        finally { _busy = false; }
    }
    internal void AttachPendingController(LocalRunLaunchCommitState launch, NativeRuntime.RootHandle borrowedController)
    {
        Guard(); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(borrowedController);
        var entry = FindLaunch(launch); launch.VerifyPendingOwnership(_host.Sessions);
        if (!IsHostReadyCore(_host.Sessions) || entry.Unknown || entry.Controller != 0 || entry.Roots.Any(root => root is not null))
            throw new InvalidOperationException("The native pending graph adopts its exact controller once.");
        var current = _host.Sessions.Read(entry.Session);
        if (current.Identity != entry.Identity || current.ControllerIdentity == 0)
            throw new InvalidOperationException("The pending controller has not been exactly published in this registry.");
        _busy = true;
        try
        {
            launch.NoteControllerBound(current.ControllerIdentity);
            entry.Controller = current.ControllerIdentity; entry.BorrowedController = borrowedController;
            entry.Roots[0] = _backend.RetainController(borrowedController, entry.Controller);
            if (entry.Roots[0]!.Target.ToInt64() != entry.Controller)
                throw new InvalidOperationException("The independently retained pending controller differs from its exact role.");
            entry.BorrowedController = null;
            PublishInitializationController(entry);
        }
        catch (Exception error) { entry.Unknown = true; Fail(error); throw new NativeLocalRunTerminationBoundaryStateException(this, error); }
        finally { _busy = false; }
    }
    internal void AttachInitializingGraph(LocalRunLaunchCommitState launch, NativeRuntime.RootHandle borrowedInformation)
    {
        Guard(); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(borrowedInformation);
        AttachInitializingGraphCore(launch, () => _backend.RetainInitializingInformation(FindLaunch(launch).Roots[0]!, borrowedInformation));
    }
    private void AttachInitializingGraphCore(LocalRunLaunchCommitState launch, Func<NativeRuntime.RootHandle> retainInformation)
    {
        var entry = FindLaunch(launch); launch.VerifyPendingOwnership(_host.Sessions); var state = launch.Read();
        if (!IsHostReadyCore(_host.Sessions) || entry.Unknown || entry.Controller == 0 || entry.Roots[0] is null
            || entry.InitializingGraphAttempted || entry.Roots[1] is not null || entry.Roots[2] is not null
            || !state.ActivationAttempted || state.EarlyTerminal || state.Information != 0 || state.Progress != 0)
            throw new InvalidOperationException("Actual initializing information requires the exact pre-reserved activation window.");
        var outerBusy = _busy;
        _busy = true; entry.InitializingGraphAttempted = true;
        try
        {
            entry.Roots[1] = retainInformation();
            entry.Roots[2] = _backend.RetainProgress(entry.Roots[1]!);
            _backend.VerifyInitializingGraph(entry.Roots[0]!, entry.Roots[1]!, entry.Roots[2]!, entry.Metadata);
            var information = entry.Roots[1]!.Target.ToInt64(); var progress = entry.Roots[2]!.Target.ToInt64();
            if (information == 0 || progress == 0 || information == progress || entry.Controller == information || entry.Controller == progress)
                throw new InvalidOperationException("Initializing information/progress identities are not distinct actual objects.");
            entry.Information = information;
            entry.ReservedProgressIdentity.InitializeReservedGraph(entry.Controller, progress);
            entry.ProgressIdentity = entry.ReservedProgressIdentity;
            lock (ProcessGate)
            {
                RequireUniqueProgressPublication(entry, entry.Controller, progress);
                launch.NoteInitializingGraph(this, information, progress);
                PublishProgress(entry);
            }
        }
        catch (Exception error) { entry.Unknown = true; Fail(error); throw new NativeLocalRunTerminationBoundaryStateException(this, error); }
        finally { _busy = outerBusy; }
    }
    internal bool VerifyInitializedGraph(LocalRunLaunchCommitState launch)
    {
        Guard(); ArgumentNullException.ThrowIfNull(launch); var entry = FindLaunch(launch);
        launch.VerifyPendingOwnership(_host.Sessions);
        if (entry.Unknown || entry.ProgressIdentity is null || !entry.Published || entry.Roots.Take(3).Any(root => root is null))
            throw new InvalidOperationException("An actual initializing graph must be adopted before full verification.");
        if (_backend.RequiresInitializationIsolation && !entry.GetterReturnedVerified) return false;
        if (launch.Read().Phase == LocalRunLaunchCommitPhase.Initialized) return IsPendingInitialized(launch);
        if (launch.Read().Phase != LocalRunLaunchCommitPhase.Initializing || !IsHostReadyCore(_host.Sessions) || !IsDrained(entry)) return false;
        _busy = true;
        try
        {
            if (!entry.InitializedGraphVerified)
            {
                _backend.VerifyInitializationCompletedGraph(entry.Roots[0]!, entry.Roots[1]!, entry.Roots[2]!, entry.Metadata);
                entry.InitializedGraphVerified = true; // Native receipt, never a separate ready flag.
            }
            if (!IsDrained(entry)) return false;
            launch.NoteInitializedGraph(this); return true;
        }
        catch (Exception error) { entry.Unknown = true; Fail(error); throw new NativeLocalRunTerminationBoundaryStateException(this, error); }
        finally { _busy = false; }
    }
    internal bool HasPendingController(LocalRunLaunchCommitState launch)
    {
        _main.VerifyAccess(); var entry = _records.FirstOrDefault(item => item is not null && ReferenceEquals(item.Launch, launch));
        if (entry is null || entry.Unknown || _unknown || entry.Controller == 0 || entry.Roots[0] is null
            || !ReferenceEquals(launch.Registry, _host.Sessions) || launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown) return false;
        var current = _host.Sessions.Read(entry.Session);
        return current.Identity == entry.Identity && current.Phase == LocalRunSessionPhase.PendingCommit
            && current.ControllerIdentity == entry.Controller && !current.ReturnRequested && !current.ProtectionRetired;
    }
    internal bool HasPendingReservation(LocalRunLaunchCommitState launch)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch);
        var entry = _records.FirstOrDefault(item => item is not null && ReferenceEquals(item.Launch, launch));
        if (entry is null || entry.Unknown || _unknown || !ReferenceEquals(launch.Registry, _host.Sessions)
            || launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown) return false;
        var current = _host.Sessions.Read(entry.Session);
        return current.Identity == entry.Identity && current.Identity == launch.Identity && current.MetadataIdentity == launch.MetadataIdentity
            && current.Phase == LocalRunSessionPhase.PendingCommit && !current.ReturnRequested && !current.ProtectionRetired && !current.CleanupAttempted;
    }
    internal bool IsPendingInitialized(LocalRunLaunchCommitState launch)
    {
        _main.VerifyAccess(); var entry = _records.FirstOrDefault(item => item is not null && ReferenceEquals(item.Launch, launch));
        if (entry is null || entry.Unknown || !entry.InitializedGraphVerified || !entry.Published || entry.ProgressIdentity is null
            || entry.Roots.Take(3).Any(root => root is null) || !IsHostReadyCore(_host.Sessions) || !IsDrained(entry)
            || _backend.RequiresInitializationIsolation && !entry.GetterReturnedVerified) return false;
        var state = launch.Read();
        return state.Phase is LocalRunLaunchCommitPhase.Initialized or LocalRunLaunchCommitPhase.CommittedReady
            && state.Identity == entry.Identity && state.Controller == entry.Controller && state.Information == entry.Information
            && state.Progress == entry.ProgressIdentity.ProgressIdentity;
    }
    internal bool CancelPurePending(LocalRunLaunchCommitState launch)
    {
        Guard(); ArgumentNullException.ThrowIfNull(launch); var entry = FindLaunch(launch);
        if (!launch.CanCancelPureReservation || entry.Unknown || entry.Controller != 0 || entry.Published
            || entry.Roots.Any(root => root is not null) || entry.InitializingGraphAttempted) return false;
        lock (ProcessGate) { ProcessRecords.Remove(entry); _records[entry.Slot] = null; } return true;
    }
    private Record FindLaunch(LocalRunLaunchCommitState launch)
        => _records.FirstOrDefault(item => item is not null && ReferenceEquals(item.Launch, launch))
            ?? throw new InvalidOperationException("This exact launch has no reserved native graph record.");
    private bool IsDrained(Record entry)
    {
        if (_binding is null || !_binding.IsActive || _unknown || _backend.CleanupUnknown) return false;
        var progress = ProgressActivity(entry); var diagnostics = _binding.Describe(); var native = _host.ReadNativeBoundaryActivity();
        return progress.Known && progress.InFlight == 0 && progress.UnknownInFlight == 0 && diagnostics.UnknownObserved == 0
            && native.Known && native.InFlight == 0 && InitializationDrained() && JudgeDrained();
    }
    private bool JudgeDrained()
    { var activity = _backend.ReadJudgeActivity(); return activity.Known && activity.InFlight == 0; }
    private ExactHookBroker.LocalProgressBoundaryState ProgressActivity(Record entry)
    {
        if (entry.ProgressIdentity is not null) return _binding!.Read(entry.ProgressIdentity);
        var state = _binding!.Describe(); return new(state.InFlight, state.UnknownInFlight, state.UnknownInFlight == 0);
    }
    internal static NativeLocalRunTerminationBoundaryState Create(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker, LocalRunProtectionHost host)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(host);
        var backend = new NativeLocalRunTerminationBoundaryBackend(api, main, profile, module, broker);
        using var playReservation = backend.ReserveProgressPlay();
        return CreateBackend(main, host, backend);
    }
    internal static NativeLocalRunTerminationBoundaryState CreateBackend(IMainThreadDispatcher main,
        LocalRunProtectionHost host, INativeLocalRunTerminationBoundaryBackend backend)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(backend);
        backend.RequireSupportedBuild(); // Unsupported input precedes process reservation and all native work.
        var owner = new NativeLocalRunTerminationBoundaryState(main, host, backend);
        lock (ProcessGate)
        {
            if (ProcessOwners.Count >= MaximumProcessOwners)
                throw new InvalidOperationException("The 32-owner native termination bridge capacity is exhausted.");
            ProcessOwners.Add(owner);
        }
        owner._busy = true;
        try
        {
            // Resolve all four authorities before binding. Partial native installation may
            // still be live, so even a failure before a handle is returned retains this owner.
            owner._installationStage = "resolve and bind Progress/Timeline";
            owner._binding = backend.ResolveAndBind(owner.Classify)
                ?? throw new InvalidOperationException("No actual Progress/Timeline isolation binding was returned.");
            owner._installationStage = "verify Progress/Timeline binding";
            if (!owner._binding.IsActive) throw new InvalidOperationException("The native Progress isolation binding is inactive.");
            owner._installationStage = "bind initialization isolation";
            owner.BindInitialization();
            owner._installationStage = null;
            return owner;
        }
        catch (Exception error) { owner.Fail(error); throw new NativeLocalRunTerminationBoundaryStateException(owner, error); }
        finally { owner._busy = false; }
    }

    /// <summary>The borrowed root stays caller-owned. Registration reserves its process
    /// slot first, then acquires independent roots; failed registrations remain bounded
    /// process responsibility and are never silently retried or detached.</summary>
    internal void RegisterCommitted(LocalRunSessionRegistry.SessionHandle session, NativeRuntime.RootHandle borrowedController)
    {
        Guard(); ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(borrowedController);
        _busy = true;
        try { RegisterCommittedCore(session, borrowedController); }
        finally { _busy = false; }
    }
    private void RegisterCommittedCore(LocalRunSessionRegistry.SessionHandle session, NativeRuntime.RootHandle borrowedController)
    {
        _backend.RequireSupportedBuild();
        var current = _host.Sessions.Read(session);
        if (_unknown || _backend.CleanupUnknown || _binding is null || !_binding.IsActive || _stopped || _stopAttempted
            || current.Phase != LocalRunSessionPhase.Committed || current.ReturnRequested || current.ResultCallClaimed
            || current.ProtectionRetired || current.ControllerIdentity == 0
            || !ReferenceEquals(_host.Sessions.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, current.ControllerIdentity), session))
            throw new InvalidOperationException("Native termination mapping requires a supported committed exact protected session and active isolation.");
        var hostState = _host.Read(); var activity = _host.ReadNativeBoundaryActivity(); var progress = _binding.Describe();
        if (!hostState.AllGatesInstalled || hostState.CleanupUnknown || !activity.Known || activity.InFlight != 0 || !JudgeDrained()
            || progress.InFlight != 0 || progress.UnknownInFlight != 0 || progress.UnknownObserved != 0)
            throw new InvalidOperationException("Native termination registration requires five verified gates and known drained native entries.");
        var slot = Array.FindIndex(_records, item => item is null);
        if (slot < 0) throw new InvalidOperationException("The native termination bridge's fixed record slots are exhausted.");
        var entry = new Record(slot, current.Identity) { Owner = this, Session = session,
            Controller = current.ControllerIdentity, Metadata = current.MetadataIdentity, BorrowedController = borrowedController };
        lock (ProcessGate)
        {
            if (ProcessRecords.Count >= MaximumProcessOwners || ProcessRecords.Any(item => ReferenceEquals(item.Session, session)))
                throw new InvalidOperationException("The exact session is already mapped or the native termination 32-record capacity is exhausted.");
            ProcessRecords.EnsureCapacity(ProcessRecords.Count + 1); ProcessRecords.Add(entry); _records[slot] = entry;
        }
        try
        {
            entry.Roots[0] = _backend.RetainController(borrowedController, entry.Controller);
            entry.Roots[1] = _backend.RetainInformation(entry.Roots[0]!);
            entry.Roots[2] = _backend.RetainProgress(entry.Roots[1]!);
            _backend.VerifyGraph(entry.Roots[0]!, entry.Roots[1]!, entry.Roots[2]!, entry.Metadata);
            var controller = entry.Roots[0]!.Target.ToInt64(); var information = entry.Roots[1]!.Target.ToInt64(); var exactProgress = entry.Roots[2]!.Target.ToInt64();
            if (controller != entry.Controller || information == 0 || exactProgress == 0
                || controller == information || controller == exactProgress || information == exactProgress)
                throw new InvalidOperationException("The retained LOCAL controller/information/progress roots are not the exact distinct graph.");
            entry.Information = information;
            var again = _host.Sessions.Read(session);
            if (again.Identity != entry.Identity || again.ControllerIdentity != controller || again.MetadataIdentity != entry.Metadata
                || again.Phase != LocalRunSessionPhase.Committed || again.ReturnRequested || again.ResultCallClaimed || again.ProtectionRetired)
                throw new InvalidOperationException("The committed session changed while its native progress graph was retained.");
            var hostAgain = _host.Read(); var activityAgain = _host.ReadNativeBoundaryActivity(); var progressAgain = _binding.Describe();
            if (!_binding.IsActive || !hostAgain.AllGatesInstalled || hostAgain.CleanupUnknown || !activityAgain.Known || activityAgain.InFlight != 0 || !JudgeDrained()
                || progressAgain.InFlight != 0 || progressAgain.UnknownInFlight != 0 || progressAgain.UnknownObserved != 0)
                throw new InvalidOperationException("Native entries or protection changed before exact progress publication.");
            entry.ReservedProgressIdentity.InitializeReservedGraph(controller, exactProgress);
            entry.ProgressIdentity = entry.ReservedProgressIdentity;
            lock (ProcessGate)
            {
                RequireUniqueProgressPublication(entry, controller, exactProgress);
                entry.BorrowedController = null; PublishProgress(entry);
            }
        }
        catch (Exception error) { entry.Unknown = true; Fail(error); throw new NativeLocalRunTerminationBoundaryStateException(this, error); }
    }
    private ExactHookBroker.LocalProgressIdentity? Classify(IntPtr receiver)
    {
        var identity = receiver.ToInt64(); if (identity == 0) return null;
        for (var index = 0; index < _published.Length; ++index)
        {
            var entry = Volatile.Read(ref _published[index]);
            if (entry?.ProgressIdentity is { } token && token.ProgressIdentity == identity) return token;
        }
        return null;
    }
    private static void RequireUniqueProgressPublication(Record entry, long controller, long progress)
    {
        foreach (var item in ProcessRecords)
            if (!ReferenceEquals(item, entry) && item.Published
                && (item.Controller == controller || item.ProgressIdentity?.ProgressIdentity == progress))
                throw new InvalidOperationException("A controller/progress graph is already published for another LOCAL run.");
    }
    private void PublishProgress(Record entry)
    {
        var identity = entry.ProgressIdentity;
        if (!ReferenceEquals(_records[entry.Slot], entry) || Volatile.Read(ref _published[entry.Slot]) is not null
            || identity is null || !ReferenceEquals(identity, entry.ReservedProgressIdentity)
            || identity.ControllerIdentity == 0 || identity.ProgressIdentity == 0)
            throw new InvalidOperationException("The reserved graph publication slot must receive its completed identity once.");
        entry.Published = true;
        Volatile.Write(ref _published[entry.Slot], entry);
    }
    public bool MarkTerminal(LocalRunSessionRegistry.SessionHandle session, long exactController)
    {
        Guard(); var entry = Find(session, exactController);
        if (_unknown || entry.Unknown || _binding is null || !_binding.IsActive || entry.ProgressIdentity is null) return false;
        var marked = _binding.MarkTerminal(entry.ProgressIdentity);
        if (marked is ExactHookBroker.LocalProgressTerminalMark.MarkedCurrentContext or ExactHookBroker.LocalProgressTerminalMark.NoCurrentContext)
            return true;
        entry.Unknown = true; Fail(new InvalidOperationException("The exact Progress terminal context is rejected: " + marked));
        return false;
    }
    public LocalRunTerminationBoundarySnapshot Read(LocalRunSessionRegistry.SessionHandle session, long exactController)
    {
        Guard(); var entry = Find(session, exactController);
        if (_unknown || entry.Unknown || _backend.CleanupUnknown || _binding is null || !_binding.IsActive || entry.ProgressIdentity is null)
            return new(false, 0, false, 0);
        _busy = true;
        try
        {
            var host = _host.Read(); var activity = _host.ReadNativeBoundaryActivity();
            var progress = _binding.Read(entry.ProgressIdentity); var diagnostics = _binding.Describe();
            if (diagnostics.UnknownObserved != 0)
            {
                // A previous unknown around entry may have passed through native code.
                // Conservative sticky retention is a fault, not a recovered usable state.
                entry.Unknown = true; Fail(new InvalidOperationException("Progress isolation observed unknown native context."));
                return new(false, 0, false, 0);
            }
            var count = checked(activity.InFlight + progress.InFlight + progress.UnknownInFlight);
            var initialization = ReadInitializationActivity();
            count = checked(count + initialization.InFlight);
            var judge = _backend.ReadJudgeActivity(); count = checked(count + judge.InFlight);
            var known = activity.Known && progress.Known && progress.UnknownInFlight == 0 && count >= 0 && count <= int.MaxValue;
            known = known && initialization.Known && initialization.UnknownInFlight == 0 && InitializationHealthy()
                && judge.Known && judge.InFlight >= 0;
            if (!known) return new(host.AllGatesInstalled && !host.CleanupUnknown, 0, false, 0);
            var frame = _backend.Frame();
            if (frame < 0 || _lastFrame.HasValue && frame < _lastFrame.Value)
                throw new InvalidOperationException("The actual Unity frame count is invalid or moved backwards.");
            _lastFrame = frame;
            var active = _binding.IsActive;
            return new(host.AllGatesInstalled && !host.CleanupUnknown && active, frame, active, (int)count);
        }
        catch (Exception error) { entry.Unknown = true; Fail(error); return new(false, 0, false, 0); }
        finally { _busy = false; }
    }

    /// <summary>Counts alone never retire a mapping. The registry's completed close and
    /// native consumer-end evidence first prove no old source can enter again. Remove the
    /// pure identity before releasing any root, so recycled addresses cannot stay LOCAL.</summary>
    internal bool TryRetire(LocalRunSessionRegistry.SessionHandle session)
    {
        Guard(); var entry = _records.FirstOrDefault(item => item is not null && ReferenceEquals(item.Session, session));
        if (entry is null) return true;
        if (_unknown || entry.Unknown || entry.FreeAttempted || _backend.CleanupUnknown || _binding is null || !_binding.IsActive) return false;
        var current = _host.Sessions.Read(session);
        if (current.Identity != entry.Identity || entry.Controller != 0 && current.ControllerIdentity != entry.Controller || current.Phase != LocalRunSessionPhase.Closed
            || !current.ProtectionRetired || !current.CleanupAttempted || string.IsNullOrWhiteSpace(current.ConsumerEndEvidence)) return false;
        var progress = ProgressActivity(entry); var diagnostics = _binding.Describe();
        var activity = _host.ReadNativeBoundaryActivity();
        if (!progress.Known || progress.InFlight != 0 || progress.UnknownInFlight != 0 || diagnostics.UnknownObserved != 0
            || !activity.Known || activity.InFlight != 0 || !InitializationDrained() || !JudgeDrained()) return false;
        _busy = true;
        try
        {
            if (entry.Published)
                lock (ProcessGate) { entry.Published = false; Volatile.Write(ref _published[entry.Slot], null); }
            UnpublishInitializationController(entry);
            // An entry can already have obtained the old immutable token when the map
            // is removed. Keep every root and allow a later drain check; no free attempt
            // has begun merely because pure classification was retired.
            progress = ProgressActivity(entry); diagnostics = _binding.Describe();
            activity = _host.ReadNativeBoundaryActivity();
            if (!progress.Known || progress.InFlight != 0 || progress.UnknownInFlight != 0
                || !activity.Known || activity.InFlight != 0 || !InitializationDrained() || !JudgeDrained()) return false;
            if (diagnostics.UnknownObserved != 0) throw new InvalidOperationException("Progress isolation observed unknown work while retiring its mapping.");
            entry.FreeAttempted = true;
            var failures = new List<Exception>(3);
            for (var index = entry.Roots.Length - 1; index >= 0; index--)
                if (entry.Roots[index] is { } root)
                    try { root.Dispose(); entry.Roots[index] = null; } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("Native progress mapping root cleanup is unknown and will not be retried.", failures);
            lock (ProcessGate) { ProcessRecords.Remove(entry); _records[entry.Slot] = null; }
            return true;
        }
        catch (Exception error) { entry.Unknown = true; Fail(error); return false; }
        finally { _busy = false; }
    }
    internal bool TryStop()
    {
        Guard(); if (_stopped) return true;
        if (_unknown || _backend.CleanupUnknown || _stopAttempted || _records.Any(item => item is not null) || _binding is null) return false;
        var state = _binding.Describe();
        if (state.InFlight != 0 || state.UnknownInFlight != 0 || state.UnknownObserved != 0 || !InitializationDrained() || !JudgeDrained()) return false;
        _busy = true; _stopAttempted = true;
        try
        {
            _initializationBinding?.Dispose(); _binding.Dispose(); _stopped = true; lock (ProcessGate) ProcessOwners.Remove(this); return true;
        }
        catch (Exception error) { Fail(error); return false; }
        finally { _busy = false; }
    }
    internal NativeLocalRunTerminationBoundaryDiagnostics Describe()
    {
        _main.VerifyAccess(); var publications = 0;
        for (var index = 0; index < _published.Length; ++index)
            if (Volatile.Read(ref _published[index]) is not null) ++publications;
        return new(_records.Count(item => item is not null), publications,
            _binding?.IsActive == true, _unknown || _backend.CleanupUnknown, _stopped, _failure);
    }
    private Record Find(LocalRunSessionRegistry.SessionHandle session, long controller)
        => _records.FirstOrDefault(item => item is not null && ReferenceEquals(item.Session, session) && item.Controller == controller)
            ?? throw new InvalidOperationException("No exact rooted native progress identity belongs to this LOCAL session/controller.");
    private void Fail(Exception error)
    {
        _unknown = true;
        var stage = _installationStage is null ? null : _backend.InstallationStage ?? _installationStage;
        _failure ??= LocalRunFailureDiagnostics.Describe("Native termination boundary state remains retained without retry"
            + (stage is null ? "" : " during " + stage), error);
        foreach (var entry in _records) try { entry?.Launch?.MarkUnknown(_failure); } catch { }
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (_busy) throw new InvalidOperationException("Native termination boundary registration/read/retirement cannot reenter owned work.");
    }
}

internal sealed partial class NativeLocalRunTerminationBoundaryBackend : INativeLocalRunTerminationBoundaryBackend
{
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly ExactHookBroker _broker;
    private bool _buildVerified, _layoutVerified, _bindAttempted;
    private IntPtr _normal, _information, _progress, _parameters, _alive, _frame;
    private VerifiedHookTarget[]? _judgeTargets;
    public bool CleanupUnknown { get; private set; }
    public string? InstallationStage { get; private set; }
    internal NativeLocalRunTerminationBoundaryBackend(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(main); ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(broker);
        (_api, _main, _profile, _module, _broker) = (api, main, profile, module, broker);
    }
    public void RequireSupportedBuild()
    {
        _main.VerifyAccess();
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Native LOCAL termination state requires the pinned Windows x64 Unity build.");
        if (_buildVerified) return;
        using var input = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll"));
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
            throw new UnsupportedSignatureException("Native termination state image differs from the pinned profile.");
        _buildVerified = true;
    }
    internal ExactHookBroker.LocalProgressPlayReservation ReserveProgressPlay()
    {
        RequireSupportedBuild();
        if (_progressPlayReservation is not null)
            throw new InvalidOperationException("Native LOCAL ProgressControl.Play reservation is once-only.");
        InstallationStage = "resolve initialization targets";
        _initializationTargets = ExactHookBroker.LocalInitializationIsolationTargets.Resolve(_api, _module, _profile.GameAssemblySha256);
        var reservation = _broker.ReserveLocalProgressPlay(_initializationTargets.Play);
        InstallationStage = null;
        return _progressPlayReservation = reservation;
    }
    public ExactHookBroker.LocalProgressIsolationBinding ResolveAndBind(Func<IntPtr, ExactHookBroker.LocalProgressIdentity?> classify)
    {
        RequireSupportedBuild();
        if (_bindAttempted || _progressPlayReservation is null)
            throw new InvalidOperationException("Native Progress/Timeline binding is once-only and requires the reserved Play owner.");
        _bindAttempted = true;
        try
        {
            InstallationStage = "resolve Progress/Timeline targets";
            var targets = ExactHookBroker.LocalProgressIsolationTargets.Resolve(_api, _module, _profile.GameAssemblySha256);
            InstallationStage = "resolve judge activity targets";
            _judgeTargets = new[] {
                _api.ResolveExactHook("local.judge.caller-activity", _module, _api.Class("Assembly-CSharp.dll", "InLevel", "GetJudgeControl"),
                    "GetJudge", new[] { "System.Int32", "System.Single" }, 0x3E8030, ExactHookAbi.InstanceInt32SingleVoid),
                _api.ResolveExactHook("local.judge.inner-activity", _module, _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController"),
                    "GetJudge", new[] { "InLevelController.JudgeType", "System.Single" }, 0x3C0310, ExactHookAbi.InstanceEnum32SingleVoid),
                _api.ResolveExactHook("local.judge.hold-activity", _module, _api.NestedClass(_api.Class("Assembly-CSharp.dll", "InLevel", "NoteJudgeControl"), "<CheckHold>d__32"),
                    "MoveNext", Array.Empty<string>(), 0x402670, ExactHookAbi.InstanceBooleanReturn0)
            };
            InstallationStage = "verify graph/frame layout";
            ResolveLayout();
            InstallationStage = "verify initialization settings";
            PrepareInitializationSettings();
            if (!_progressPlayReservation.SharesOfficialPlay)
            {
                InstallationStage = "publish dormant shared ProgressControl.Play";
                var foundation = NativeFirstInstall.RequireReady().RequirePlaybackGroupAdoptionEvidence();
                var stock = _api.ResolvePlaybackStockTargets(_module);
                var playback = PlaybackSourceGroupTargets.FromStock(stock);
                var directory = Path.GetDirectoryName(typeof(NativeLocalRunTerminationBoundaryBackend).Assembly.Location)!;
                var sourcePath = Path.Combine(directory, "RizToolbox.PlaybackSourcePrototype.dll");
                using var source = File.OpenRead(sourcePath);
                using var sha = SHA256.Create();
                var sourceHash = Convert.ToHexString(sha.ComputeHash(source)).ToLowerInvariant();
                _progressPlayReservation.PublishDormantSharedPlay(playback, sourcePath, sourceHash, foundation);
            }
            InstallationStage = "bind Progress/Timeline isolation";
            var binding = _broker.BindLocalProgressIsolation(targets, classify, _progressPlayReservation);
            InstallationStage = null;
            return binding;
        }
        catch { CleanupUnknown = true; throw; }
    }
    private void ResolveLayout()
    {
        if (_layoutVerified) return;
        _normal = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        var controller = _api.Class("Assembly-CSharp.dll", "", "InLevelController");
        _information = _api.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation");
        _progress = _api.Class("Assembly-CSharp.dll", "InLevel", "ProgressControl");
        _parameters = _api.Class("Assembly-CSharp.dll", "", "InsideLevelParams");
        _levelControl = _api.Class("Assembly-CSharp.dll", "InLevel", "LevelControl");
        _api.RequireInstanceFieldLayout(_levelControl, "levelInformation", "InLevel.LevelInformation", 0x20);
        _api.RequireInstanceFieldLayout(controller, "LevelInformation", "InLevel.LevelInformation", 0x28);
        _api.RequireInstanceFieldLayout(controller, "LevelParams", "InsideLevelParams", 0x20);
        _api.RequireInstanceFieldLayout(_information, "progressControl", "InLevel.ProgressControl", 0x28);
        _api.RequireInstanceFieldLayout(_information, "loadOver", "System.Boolean", 0xBE);
        _api.RequireInstanceFieldLayout(_progress, "levelInformation", "InLevel.LevelInformation", 0x20);
        _api.RequireInstanceFieldLayout(_parameters, "metadata", "ILevelMetadata", 0x10);
        _alive = _api.Method(_api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object"), "op_Implicit", 1, true, new[] { "UnityEngine.Object" });
        if (_api.DescribeReturnType(_alive) != "System.Boolean") throw new UnsupportedSignatureException("Native progress Unity liveness signature changed.");
        var frame = _api.InspectDeclaredStaticMethod(_api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time"),
            "get_frameCount", Array.Empty<string>(), "System.Int32", _module);
        if (frame.Entry?.Rva != 0x155EFD0 || (frame.Flags & 0x10) == 0 || (frame.Flags & 0x400) != 0 || frame.GenericMethod || frame.InflatedMethod)
            throw new UnsupportedSignatureException("The exact Unity frame-count static getter metadata/RVA changed.");
        _frame = frame.MethodInfo; _layoutVerified = true;
    }
    private T Observe<T>(Func<T> operation)
    {
        RequireSupportedBuild();
        if (!_layoutVerified || CleanupUnknown) throw new InvalidOperationException("Native termination graph has no confirmed initialized read context.");
        try { return operation(); } catch { CleanupUnknown = true; throw; }
    }
    public NativeRuntime.RootHandle RetainController(NativeRuntime.RootHandle borrowedController, long exactController)
        => Observe(() => {
            var pointer = borrowedController.Target;
            if (pointer.ToInt64() != exactController || _api.ObjectClass(pointer) != _normal)
                throw new InvalidOperationException("Borrowed controller root is not the committed exact NormalMode instance.");
            return _api.Root(pointer);
        });
    public NativeRuntime.RootHandle RetainInformation(NativeRuntime.RootHandle controller)
        => Observe(() => RetainExact(_api.ReadWithPublication<IntPtr>(controller.Target, "LevelInformation"), _information));
    public NativeRuntime.RootHandle RetainInitializingInformation(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle borrowedInformation)
        => Observe(() => {
            var information = borrowedInformation.Target;
            var control = controller.Target;
            if (_api.ObjectClass(control) != _normal || information == IntPtr.Zero || _api.ObjectClass(information) != _information)
                throw new InvalidOperationException("The actual initialization input is not an exact LevelInformation instance.");
            var current = _api.ReadWithPublication<IntPtr>(control, "LevelInformation");
            if (current != IntPtr.Zero && current != information)
                throw new InvalidOperationException("The pending controller already refers to another information instance.");
            return _api.Root(information);
        });
    public NativeRuntime.RootHandle RetainProgress(NativeRuntime.RootHandle information)
        => Observe(() => RetainExact(_api.ReadWithPublication<IntPtr>(information.Target, "progressControl"), _progress));
    private NativeRuntime.RootHandle RetainExact(IntPtr pointer, IntPtr expectedClass)
    {
        if (pointer == IntPtr.Zero || _api.ObjectClass(pointer) != expectedClass)
            throw new InvalidOperationException("The exact initialized information/progress graph is unavailable.");
        return _api.Root(pointer);
    }
    public void VerifyGraph(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
        NativeRuntime.RootHandle progress, long metadata)
        => Observe(() => VerifyGraphCore(controller, information, progress, metadata, true));
    public void VerifyInitializingGraph(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
        NativeRuntime.RootHandle progress, long metadata)
        => Observe(() => VerifyGraphCore(controller, information, progress, metadata, false));
    private bool VerifyGraphCore(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
        NativeRuntime.RootHandle progress, long metadata, bool requireControllerField)
    {
            var control = controller.Target; var info = information.Target; var track = progress.Target;
            if (_api.ObjectClass(control) != _normal || _api.ObjectClass(info) != _information || _api.ObjectClass(track) != _progress)
                throw new InvalidOperationException("The retained initialization graph classes differ from their exact authorities.");
            var currentInformation = _api.ReadWithPublication<IntPtr>(control, "LevelInformation");
            if (currentInformation != info && (requireControllerField || currentInformation != IntPtr.Zero)
                || _api.ReadWithPublication<IntPtr>(info, "progressControl") != track
                || _api.ReadWithPublication<IntPtr>(track, "levelInformation") != info)
                throw new InvalidOperationException("The native controller/information/progress forward and reverse graph differs.");
            var parameters = _api.ReadWithPublication<IntPtr>(control, "LevelParams");
            if (parameters == IntPtr.Zero || _api.ObjectClass(parameters) != _parameters
                || _api.ReadWithPublication<IntPtr>(parameters, "metadata").ToInt64() != metadata)
                throw new InvalidOperationException("The native progress controller's retained LOCAL metadata differs.");
            foreach (var target in new[] { control, info, track })
                if (!_api.InvokeValueWithPublication<bool>(_alive, IntPtr.Zero, A.Ref(target)))
                    throw new TargetExpiredException("The registered native LOCAL progress graph was destroyed.");
            return true;
    }
    public LocalRunBoundaryActivity ReadJudgeActivity()
    {
        _main.VerifyAccess();
        if (!_buildVerified || !_layoutVerified || CleanupUnknown || _judgeTargets is null) return new(0, false);
        long count = 0;
        for (var index = 0; index < _judgeTargets.Length; ++index)
        {
            var activity = _broker.ReadExactActivity(_judgeTargets[index], requireNativeJudgeTransport: index == 0);
            if (!activity.Known || activity.InFlight < 0) return new(count, false);
            count = checked(count + activity.InFlight);
        }
        return new(count, true);
    }
    public int Frame() => Observe(() => _api.InvokeValueWithPublication<int>(_frame, IntPtr.Zero));
}
