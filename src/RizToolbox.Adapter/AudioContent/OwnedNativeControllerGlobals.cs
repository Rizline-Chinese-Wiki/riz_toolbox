using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal interface IControllerGlobalsReference : IDisposable { long Identity { get; } }
internal interface IControllerGlobalsBackend : IDisposable
{
    long OwnerIdentity { get; }
    bool IsCurrentOwner { get; }
    IControllerGlobalsReference RetainController(IntPtr controller);
    IControllerGlobalsReference? CaptureDelegate(int index);
    void VerifyBaseline(int index, IControllerGlobalsReference? value);
    bool TargetsController(int index, IControllerGlobalsReference value, long controller);
    bool ReadControllerSet();
    bool WriteDelegateIfSame(int index, long expected, IControllerGlobalsReference? baseline);
    bool WriteControllerSetIfSame(bool expected, bool baseline, IReadOnlyList<long> expectedDelegates);
}
internal enum ControllerGlobalRestoreState
{ BaselineUnchanged, Restored, SuccessorPreserved, OwnedDelegateChanged, OwnershipChanged, BaselineUnsafe, Unverified, WriteUnknown }
internal sealed record ControllerGlobalRestoreRow(string Field, long Baseline, long Installed, long Observed,
    ControllerGlobalRestoreState State, string? Failure = null);
internal sealed record ControllerGlobalsRestoreReport(bool SameManagerOwner, bool InstalledCaptureComplete,
    bool AllDelegatesDetached, bool ControllerSetRestored, IReadOnlyList<ControllerGlobalRestoreRow> Fields, string? Failure);
internal sealed record ControllerGlobalsRetirementRequest(long InLevelControl, long Controller,
    bool InstalledCaptureAttempted, ControllerGlobalsRestoreReport? Restoration);
internal sealed record ControllerGlobalsSnapshot(long OwnerIdentity, bool IsCurrentOwner, bool ControllerSet,
    IReadOnlyList<long> DelegateIdentities);
/// <summary>
/// External native trust boundary. Verify the LOCAL controller and all other consumers/callbacks
/// have ended and neither the old nor current manager globals reference it. Scene names, a return
/// click or successful restoration alone do not prove this. This layer never fabricates that proof.
/// </summary>
internal interface IControllerGlobalsConsumersEndedVerifier
{ bool Verify(ControllerGlobalsRetirementRequest request, out string evidence); }

/// <summary>
/// Host-owned snapshot/conditional restoration of six InLevelControl delegates and controllerSet.
/// No clone, delegate construction, scene transition or implicit consumer-end verification occurs here.
/// Failed capture/restoration retains bounded process responsibility until an external verifier permits release.
/// </summary>
internal sealed class OwnedNativeControllerGlobals : IDisposable, INativeLocalActiveConsumerClosureSource
{
    internal static readonly string[] FieldNames = { "LevelStart", "GetJudge", "LevelOver", "SetUiColor", "ControllerOver", "GetLevelResult" };
    private const int MaximumOwners = 32;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<OwnedNativeControllerGlobals> ProcessOwners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly IControllerGlobalsBackend _backend;
    private readonly IControllerGlobalsReference?[] _baseline = new IControllerGlobalsReference?[6];
    private readonly IControllerGlobalsReference?[] _installed = new IControllerGlobalsReference?[6];
    private readonly IControllerGlobalsReference?[] _comparison = new IControllerGlobalsReference?[6];
    private readonly bool[] _captured = new bool[6], _owned = new bool[6];
    private IControllerGlobalsReference? _controller;
    private long _ownerIdentity;
    private bool _baselineFlag, _installedFlag, _baselineReady, _captureAttempted, _captureComplete;
    private bool _busy, _restoreAttempted, _releaseAttempted, _backendReleased, _closed, _cleanupUnknown;
    private ControllerGlobalsRestoreReport? _restoration;
    private LocalRunLaunchCommitState? _activeConsumerLaunch;
    private string? _failure;

    private OwnedNativeControllerGlobals(IMainThreadDispatcher main, IControllerGlobalsBackend backend)
        => (_main, _backend) = (main, backend);

    internal static OwnedNativeControllerGlobals Capture(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr gameAssembly, NativeRuntime.RootHandle gameManager,
        NativeRuntime.RootHandle inLevelControl)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(inLevelControl);
        var backend = new NativeControllerGlobalsBackend(api, main, profile, gameAssembly);
        return CaptureBackend(main, backend, () => backend.Initialize(gameManager, inLevelControl));
    }

    /// <summary>Testable ownership algorithm; adopts backend after main-thread admission and process reservation.</summary>
    internal static OwnedNativeControllerGlobals CaptureBackend(IMainThreadDispatcher main,
        IControllerGlobalsBackend backend, Action? initialize = null)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend);
        var owner = new OwnedNativeControllerGlobals(main, backend);
        lock (ProcessGate)
        {
            if (ProcessOwners.Count >= MaximumOwners)
                throw new InvalidOperationException("Controller-global ownership capacity is exhausted; unknown cleanup remains process-owned.");
            ProcessOwners.Add(owner);
        }
        try
        {
            initialize?.Invoke(); owner._ownerIdentity = backend.OwnerIdentity;
            if (owner._ownerIdentity == 0 || !backend.IsCurrentOwner)
                throw new TargetExpiredException("Controller-global baseline owner is not the current exact manager owner.");
            lock (ProcessGate)
                if (ProcessOwners.Any(x => !ReferenceEquals(x, owner) && x._ownerIdentity == owner._ownerIdentity))
                    throw new InvalidOperationException("The same InLevelControl owner already has a host snapshot.");
            owner._baselineFlag = backend.ReadControllerSet();
            for (var index = 0; index < FieldNames.Length; index++)
            {
                owner._baseline[index] = backend.CaptureDelegate(index);
                backend.VerifyBaseline(index, owner._baseline[index]);
            }
            if (!backend.IsCurrentOwner || backend.ReadControllerSet() != owner._baselineFlag)
                throw new TargetExpiredException("Manager ownership changed while capturing its baseline.");
            owner._baselineReady = true; return owner;
        }
        catch (Exception failure)
        {
            try { owner.ReleaseRoots(); }
            catch (Exception cleanup) { throw new AggregateException("Controller-global capture failed and cleanup is unconfirmed.", failure, cleanup); }
            throw;
        }
    }

    internal bool InstalledCaptureComplete { get { _main.VerifyAccess(); return _captureComplete; } }
    internal bool CleanupUnknown { get { _main.VerifyAccess(); return _cleanupUnknown; } }
    internal bool IsClosed { get { _main.VerifyAccess(); return _closed; } }
    internal int OutstandingRootRecords
    {
        get { _main.VerifyAccess(); return _baseline.Count(x => x is not null) + _installed.Count(x => x is not null)
            + _comparison.Count(x => x is not null) + (_controller is null ? 0 : 1) + (_backendReleased ? 0 : 1); }
    }

    /// <summary>Immutable copy of the six retained baseline identities, observed when Capture succeeded.</summary>
    internal ControllerGlobalsSnapshot BaselineSnapshot()
    {
        Guard(); if (!_baselineReady) throw new InvalidOperationException("A complete globals baseline is unavailable.");
        return new(_ownerIdentity, true, _baselineFlag, Array.AsReadOnly(_baseline.Select(Id).ToArray()));
    }

    /// <summary>Immutable identities of the six delegates whose exact controller
    /// Targets were verified during the one successful installed capture. A later
    /// current observation must match these retained identities before publication.</summary>
    internal ControllerGlobalsSnapshot InstalledSnapshot()
    {
        Guard();
        if (!_captureComplete || _cleanupUnknown || _restoreAttempted || _controller is null
            || _installed.Any(value => value is null))
            throw new InvalidOperationException("A complete retained, unrestored installed globals receipt is unavailable.");
        return new(_ownerIdentity, true, _installedFlag, Array.AsReadOnly(_installed.Select(Id).ToArray()));
    }

    /// <summary>
    /// Read-only current observation. Retains every observed delegate until the complete immutable
    /// snapshot exists, then attempts independent temporary-root cleanup. This is not retirement proof.
    /// </summary>
    internal ControllerGlobalsSnapshot ObserveCurrent()
    {
        Guard(); if (!_baselineReady) throw new InvalidOperationException("A complete globals baseline is unavailable.");
        var retained = new IControllerGlobalsReference?[6]; var failures = new List<Exception>(7);
        ControllerGlobalsSnapshot? snapshot = null; _busy = true;
        try
        {
            try
            {
                var currentBefore = _backend.IsCurrentOwner; var flag = _backend.ReadControllerSet();
                for (var index = 0; index < retained.Length; index++) retained[index] = _backend.CaptureDelegate(index);
                var ids = Array.AsReadOnly(retained.Select(Id).ToArray());
                snapshot = new(_ownerIdentity, currentBefore && _backend.IsCurrentOwner, flag, ids);
            }
            catch (Exception error) { failures.Add(error); }
            for (var index = retained.Length - 1; index >= 0; index--)
                if (retained[index] is { } root)
                    try { root.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0)
            {
                _cleanupUnknown = true; _failure = "Read-only globals observation or temporary-root cleanup is unconfirmed.";
                throw new AggregateException(_failure, failures);
            }
            return snapshot!;
        }
        finally { _busy = false; }
    }

    /// <summary>
    /// Must run after clone/Awake, with the same prepared owner. Every installed delegate must be
    /// a verified single-cast exact field type whose Target is this exact live controller.
    /// Partial failures retain all successful captures and cannot be retried into apparent success.
    /// </summary>
    internal void CaptureInstalledFor(NativeRuntime.RootHandle controllerRoot)
    {
        Guard(); ArgumentNullException.ThrowIfNull(controllerRoot);
        if (!_baselineReady || _captureAttempted) throw new InvalidOperationException("Installed globals can be captured once after a complete baseline.");
        _busy = true; _captureAttempted = true;
        var failures = new List<Exception>(8);
        try
        {
            if (!_backend.IsCurrentOwner) throw new TargetExpiredException("The manager owner changed before installed capture.");
            _controller = _backend.RetainController(controllerRoot.Target);
            var controller = _controller.Identity;
            for (var index = 0; index < FieldNames.Length; index++)
            {
                try
                {
                    _installed[index] = _backend.CaptureDelegate(index); _captured[index] = true;
                    _owned[index] = _installed[index] is { } value && _backend.TargetsController(index, value, controller);
                    if (!_owned[index]) failures.Add(new UnsupportedSignatureException("Installed " + FieldNames[index] + " does not target the LOCAL controller."));
                }
                catch (Exception error) { failures.Add(error); }
            }
            _installedFlag = _backend.ReadControllerSet();
            if (!_installedFlag || !_backend.IsCurrentOwner)
                failures.Add(new UnsupportedSignatureException("Installed controllerSet or manager ownership differs from completed Awake publication."));
            if (failures.Count != 0) throw new AggregateException("Installed globals were only partially verified; responsibility is retained.", failures);
            _captureComplete = true;
        }
        catch (Exception error) { _cleanupUnknown = true; _failure = error.Message; throw; }
        finally { _busy = false; }
    }

    /// <summary>Never overwrites a successor. Each native setter is attempted at most once, including unknown outcomes.</summary>
    internal ControllerGlobalsRestoreReport RestoreIfOwned()
    {
        Guard();
        if (!_captureAttempted || _controller is null)
            throw new InvalidOperationException("No retained LOCAL controller publication is available for restoration.");
        if (_restoreAttempted) return _restoration ?? throw new InvalidOperationException("Restoration failed before a report was available; no setters will be retried.");
        var rows = new List<ControllerGlobalRestoreRow>(6); _busy = true; _restoreAttempted = true;
        try
        {
            if (!_backend.IsCurrentOwner)
                return _restoration = new(false, _captureComplete, false, false, rows.AsReadOnly(), "Manager ownership changed; no successor owner was written.");
            var allDetached = true; var allWereInstalled = _captureComplete;
            for (var index = 0; index < FieldNames.Length; index++)
            {
                long baseline = 0, installed = 0, current = 0;
                var state = ControllerGlobalRestoreState.Unverified; string? failure = null;
                try
                {
                    baseline = Id(_baseline[index]); installed = Id(_installed[index]);
                    _comparison[index] = _backend.CaptureDelegate(index); current = Id(_comparison[index]);
                    if (current == baseline)
                    { state = ControllerGlobalRestoreState.BaselineUnchanged; allWereInstalled = false; }
                    else if (!_captured[index] || !_owned[index] || current != installed)
                    {
                        allWereInstalled = false;
                        if (_comparison[index] is { } value && _backend.TargetsController(index, value, _controller.Identity))
                        { state = ControllerGlobalRestoreState.OwnedDelegateChanged; allDetached = false; }
                        else state = ControllerGlobalRestoreState.SuccessorPreserved;
                    }
                    else
                    {
                        try { _backend.VerifyBaseline(index, _baseline[index]); }
                        catch (Exception unsafeBaseline)
                        { state = ControllerGlobalRestoreState.BaselineUnsafe; throw new InvalidOperationException("Baseline target is no longer a verified live Unity object or null.", unsafeBaseline); }
                        state = ControllerGlobalRestoreState.WriteUnknown;
                        if (_backend.WriteDelegateIfSame(index, installed, _baseline[index])) state = ControllerGlobalRestoreState.Restored;
                        else { state = ControllerGlobalRestoreState.OwnershipChanged; allDetached = false; allWereInstalled = false; }
                    }
                }
                catch (Exception error) { failure = error.Message; allDetached = false; _cleanupUnknown = true; }
                rows.Add(new(FieldNames[index], baseline, installed, current, state, failure));
            }
            var flagRestored = false;
            if (allDetached && allWereInstalled && rows.All(x => x.State == ControllerGlobalRestoreState.Restored))
            {
                try { flagRestored = _backend.WriteControllerSetIfSame(_installedFlag, _baselineFlag, _baseline.Select(Id).ToArray()); }
                catch (Exception error) { _cleanupUnknown = true; _failure = error.Message; }
            }
            return _restoration = new(true, _captureComplete, allDetached, flagRestored, rows.AsReadOnly(), _failure);
        }
        catch (Exception error) { _cleanupUnknown = true; _failure = error.Message; throw; }
        finally { _busy = false; }
    }

    internal void BindActiveConsumerLaunch(LocalRunLaunchCommitState exactLaunch)
    {
        Guard(); ArgumentNullException.ThrowIfNull(exactLaunch);
        var state = exactLaunch.Read(); var current = exactLaunch.Registry.Read(exactLaunch.Session);
        if (_activeConsumerLaunch is not null && !ReferenceEquals(_activeConsumerLaunch, exactLaunch)
            || state.Identity != current.Identity || state.Controller == 0 || state.Controller != current.ControllerIdentity
            || current.ReturnRequested || current.CleanupAttempted || current.ProtectionRetired
            || state.ActivationAttempted || _controller is not null && _controller.Identity != state.Controller)
            throw new InvalidOperationException("Global consumer observation binds one exact protected launch before activation.");
        _activeConsumerLaunch = exactLaunch;
    }

    /// <summary>Read the actual retained global owner after its once-only
    /// restoration. No caller-supplied detached bit or cleanup permission is
    /// accepted; every current delegate target is independently inspected.</summary>
    public NativeLocalActiveConsumerCheck ReadActiveConsumerClosure(LocalRunLaunchCommitState exactLaunch,
        NativeLocalActiveConsumerObligation obligation)
    {
        if (obligation != NativeLocalActiveConsumerObligation.GlobalReferences)
            throw new ArgumentOutOfRangeException(nameof(obligation));
        ArgumentNullException.ThrowIfNull(exactLaunch); _main.VerifyAccess();
        var state = exactLaunch.Read(); var current = exactLaunch.Registry.Read(exactLaunch.Session);
        var protectedReturn = current.Phase == LocalRunSessionPhase.Returning && !current.ProtectionRetired && !current.CleanupAttempted;
        var verifiedClosed = current.Phase == LocalRunSessionPhase.Closed && current.ProtectionRetired && current.CleanupAttempted
            && current.OutstandingOwnershipRecords == 0 && !string.IsNullOrWhiteSpace(current.ConsumerEndEvidence);
        if (!ReferenceEquals(_activeConsumerLaunch, exactLaunch) || state.Identity != current.Identity || _controller is null || state.Controller != _controller.Identity
            || current.ControllerIdentity != state.Controller || !current.ReturnRequested || !(protectedReturn || verifiedClosed))
            throw new InvalidOperationException("Global closure requires this retained controller's exact still-protected active return.");
        if (_cleanupUnknown || state.Phase == LocalRunLaunchCommitPhase.Unknown)
            return new(obligation, NativeLocalActiveConsumerProofState.Unknown, "The retained global/launch owner is Unknown.");
        if (!_restoreAttempted || _restoration is null || !_restoration.AllDelegatesDetached || !_restoration.SameManagerOwner)
            return new(obligation, NativeLocalActiveConsumerProofState.Pending, "The actual six-global restoration has not established detachment.");
        Guard(); _busy = true;
        try
        {
            if (!_backend.IsCurrentOwner)
                return new(obligation, NativeLocalActiveConsumerProofState.Unknown, "The current manager differs from the exact retained global owner.");
            for (var index = 0; index < FieldNames.Length; ++index)
            {
                using var value = _backend.CaptureDelegate(index);
                if (value is not null && _backend.TargetsController(index, value, _controller.Identity))
                    return new(obligation, NativeLocalActiveConsumerProofState.Pending, "Current " + FieldNames[index] + " still targets this exact retired controller.");
            }
            if (!_backend.IsCurrentOwner || _backend.ReadControllerSet() != _baselineFlag)
                return new(obligation, NativeLocalActiveConsumerProofState.Pending, "The exact global owner/controllerSet did not remain at its restored baseline.");
            return new(obligation, NativeLocalActiveConsumerProofState.Proven,
                "The current retained manager is unchanged; all six current delegate targets exclude the exact old controller and controllerSet matches baseline.");
        }
        catch (Exception error) { _cleanupUnknown = true; _failure = "Global closure observation or temporary-root cleanup is unconfirmed: " + error.Message; throw; }
        finally { _busy = false; }
    }

    internal sealed class ConsumersEndedToken
    {
        internal readonly OwnedNativeControllerGlobals Owner;
        internal readonly string Evidence;
        internal ConsumersEndedToken(OwnedNativeControllerGlobals owner, string evidence) => (Owner, Evidence) = (owner, evidence);
    }
    internal ConsumersEndedToken? VerifyConsumersEnded(IControllerGlobalsConsumersEndedVerifier verifier)
    {
        Guard(); ArgumentNullException.ThrowIfNull(verifier); _busy = true;
        try
        {
            var request = new ControllerGlobalsRetirementRequest(_ownerIdentity, Id(_controller), _captureAttempted, _restoration);
            if (!verifier.Verify(request, out var evidence)) return null;
            if (string.IsNullOrWhiteSpace(evidence) || evidence.Length > 1024 || evidence.Any(char.IsControl))
                throw new ArgumentException("Consumer-end verification must supply a bounded explanation.");
            return new(this, evidence);
        }
        finally { _busy = false; }
    }
    internal void ReleaseAfterConsumersEnded(ConsumersEndedToken token)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(token);
        if (!ReferenceEquals(token.Owner, this)) throw new InvalidOperationException("Consumer-end proof belongs to another globals owner.");
        if (_busy) throw new InvalidOperationException("Reentrant globals release is rejected.");
        ReleaseRoots();
    }
    internal void ReleaseAfterActiveConsumersEnded(ConsumersEndedToken token, LocalRunLaunchCommitState exactLaunch,
        NativeLocalRunLaunchCoordinator actualOwner)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(token); ArgumentNullException.ThrowIfNull(actualOwner);
        if (!ReferenceEquals(token.Owner, this) || !ReferenceEquals(_activeConsumerLaunch, exactLaunch) || _busy)
            throw new InvalidOperationException("Active globals cleanup requires its exact owner, launch and verified token.");
        actualOwner.VerifyActiveGlobalsRetirement(this, exactLaunch);
        ReleaseRoots(() => actualOwner.VerifyActiveGlobalsRetirement(this, exactLaunch));
    }
    public void Dispose()
    {
        _main.VerifyAccess();
        if (_closed) return;
        if (_busy || _captureAttempted) throw new InvalidOperationException("Published controller globals require external consumer-end proof before root release.");
        ReleaseRoots();
    }
    private void ReleaseRoots(Action? verifyActiveOwner = null)
    {
        if (_closed) return;
        if (_releaseAttempted) throw new InvalidOperationException("Controller-global root cleanup is unknown and will not be retried.");
        var failures = new List<Exception>(20); _releaseAttempted = true; _busy = true;
        try
        {
            ReleaseArray(_comparison); ReleaseArray(_installed); ReleaseArray(_baseline);
            if (_controller is not null)
                try { verifyActiveOwner?.Invoke(); _controller.Dispose(); _controller = null; verifyActiveOwner?.Invoke(); } catch (Exception error) { failures.Add(error); }
            try { verifyActiveOwner?.Invoke(); _backend.Dispose(); _backendReleased = true; verifyActiveOwner?.Invoke(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0)
            {
                _cleanupUnknown = true; _failure = "Independent controller-global root cleanup is unconfirmed; failed frees will not be retried.";
                throw new AggregateException(_failure, failures);
            }
            _closed = true; lock (ProcessGate) ProcessOwners.Remove(this);
        }
        finally { _busy = false; }
        void ReleaseArray(IControllerGlobalsReference?[] roots)
        {
            for (var index = roots.Length - 1; index >= 0; index--)
                if (roots[index] is { } root)
                    try { verifyActiveOwner?.Invoke(); root.Dispose(); roots[index] = null; verifyActiveOwner?.Invoke(); } catch (Exception error) { failures.Add(error); }
        }
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (_closed || _releaseAttempted) throw new ObjectDisposedException(nameof(OwnedNativeControllerGlobals));
        if (_busy) throw new InvalidOperationException("Reentrant controller-global mutation is rejected without dropping responsibility.");
    }
    private static long Id(IControllerGlobalsReference? value) => value?.Identity ?? 0;
}
