using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeNeverActivatedRetirementPhase
{ Ready, RetirementRequested, DestroyRequested, DestructionConfirmed, BundleDestroyPending, Closed, CleanupUnknown }

internal sealed record NativeNeverActivatedRetirementSnapshot(LocalRunSessionIdentity Session,
    NativeNeverActivatedRetirementPhase Phase, int DestroyFrame, int ConfirmedFrame, string? Failure);
internal sealed record NativeNeverActivatedRetirementWork(int Pending, int CleanupUnknown);

// This is a controlled transport seam, like the inactive factory's lifecycle seam. The production
// entry point below constructs its own NativeRuntime backend and never accepts lifecycle predicates.
internal interface INativeNeverActivatedRetirementBackend
{
    IDisposable BundleResponsibility { get; }
    bool ControllerBound { get; }
    bool CleanupUnknown { get; }
    bool InjectionFailureCanRetire { get; }
    bool ControllerClosed { get; }
    long ControllerIdentity { get; }
    long ParametersIdentity { get; }
    int Frame();
    void CreateClosedTransaction(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim);
    void Inject(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim);
    NativeInactiveControllerObservation ReadInactive();
    ControllerAwakeLedgerSnapshot ReadAwake();
    ControllerGlobalsSnapshot BaselineGlobals { get; }
    ControllerGlobalsSnapshot ReadGlobals();
    void VerifyRetainedIdentitiesAndSource();
    void RequestControllerDestroy();
    bool TryCompleteControllerDestroy();
    void ReleaseAfterSessionClosed();
    bool TryCompleteBundleDestroy();
}

internal delegate bool NativeNeverActivatedRetirementIssuer(LocalRunSessionRegistry.SessionHandle session,
    ILocalRunConsumersEndedVerifier verifier, out bool closed);

/// <summary>
/// A sealed, never-activated transaction, rather than a verifier over caller-supplied assertions.
/// It privately creates and binds the clone, performs the one-shot parameter injection, and exposes
/// no callable clone or activation API. Only this transaction can request its own retirement proof.
/// Native allocation/mutation/free uncertainty is sticky and retains its process responsibility.
/// </summary>
internal sealed class NativeNeverActivatedRetirement : ILocalRunConsumersEndedVerifier
{
    private const int MaximumOwners = 32;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<NativeNeverActivatedRetirement> ProcessOwners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly LocalRunSessionRegistry _registry;
    private readonly LocalRunSessionRegistry.CommitClaim _claim;
    private readonly LocalRunSessionIdentity _identity;
    private readonly INativeNeverActivatedRetirementBackend _backend;
    private readonly NativeNeverActivatedRetirementIssuer _issue;
    private NativeNeverActivatedRetirementPhase _phase;
    private LocalRunRetirementRequest? _request;
    private long _controller;
    private int _lastFrame = -1, _requestFrame = -1, _destroyFrame = -1, _confirmedFrame = -1, _closeFrame = -1;
    private bool _busy, _issuing, _verifierUsed, _releaseAttempted, _cancelRequested;
    private string? _failure;

    private NativeNeverActivatedRetirement(IMainThreadDispatcher main, LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.CommitClaim claim, LocalRunSessionIdentity identity,
        INativeNeverActivatedRetirementBackend backend, NativeNeverActivatedRetirementIssuer issue)
        => (_main, _registry, _claim, _identity, _backend, _issue) = (main, registry, claim, identity, backend, issue);

    /// <summary>The only production entry point. Neither native clone pointers nor caller-declared
    /// neverActivated/closed flags enter this transaction. The host supplies the real issuance barrier.</summary>
    internal static NativeNeverActivatedRetirement Start(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker, LocalRunProtectionHost host,
        OwnedNativeLocalPlayBundle bundle, LocalRunSessionRegistry.CommitClaim claim)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(host);
        return StartCore(main, host.Sessions, claim,
            new NativeNeverActivatedRetirementBackend(api, main, profile, module, broker, bundle),
            host.TryRetireNeverActivated);
    }

    internal static NativeNeverActivatedRetirement StartCore(IMainThreadDispatcher main,
        LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim,
        INativeNeverActivatedRetirementBackend backend, NativeNeverActivatedRetirementIssuer issue)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(backend); ArgumentNullException.ThrowIfNull(issue);
        var session = registry.Read(claim.Session);
        registry.VerifyPendingBundleClaim(claim, session.MetadataIdentity, backend.BundleResponsibility);
        if (session.ControllerIdentity != 0 || session.ResultSceneIdentity != 0 || session.ResultSceneGeneration != 0
            || session.Result is not null || session.ResultCallClaimed || session.CleanupAttempted || session.ProtectionRetired)
            throw new InvalidOperationException("A never-activated transaction requires a fresh, exact pending bundle claim.");
        var owner = new NativeNeverActivatedRetirement(main, registry, claim, session.Identity, backend, issue);
        lock (ProcessGate)
        {
            if (ProcessOwners.Count >= MaximumOwners || ProcessOwners.Any(value => ReferenceEquals(value._claim.Session, claim.Session)))
                throw new InvalidOperationException("Never-activated transaction responsibility is already retained or capacity is exhausted.");
            ProcessOwners.Add(owner);
        }
        owner._busy = true; var injectionFailed = false;
        try
        {
            // The backend opens its Awake window and captures globals BEFORE Instantiate. It owns
            // the unpublished clone throughout; publication below is only root-free host classification.
            backend.CreateClosedTransaction(registry, claim);
            owner._controller = backend.ControllerIdentity;
            owner.RequirePending(); owner.RequireInactive(); owner.RequireContinuedProof();
            try { backend.Inject(registry, claim); }
            catch { injectionFailed = true; throw; }
            owner.RequirePending(); owner.RequireInactive(); owner.RequireContinuedProof();
        }
        catch (Exception failure)
        {
            owner._failure = BoundedFailure(failure);
            // A known pre-setter injection rejection can retire the still-owned inactive clone.
            // An unknown allocation, setter result, argument cleanup, or incomplete controller cannot.
            if (injectionFailed && backend.InjectionFailureCanRetire && backend.ControllerBound && !backend.CleanupUnknown)
            {
                try
                {
                    owner._controller = backend.ControllerIdentity;
                    owner.RequirePending(); owner.RequireInactive(); owner.RequireContinuedProof();
                    owner.BeginRetirement();
                }
                catch (Exception verification) { owner.Fail(verification); }
            }
            else owner.Fail(failure);
        }
        finally { owner._busy = false; }
        return owner;
    }

    internal bool HasPendingWork { get { _main.VerifyAccess(); return _phase != NativeNeverActivatedRetirementPhase.Closed; } }
    internal bool CleanupUnknown { get { _main.VerifyAccess(); return _phase == NativeNeverActivatedRetirementPhase.CleanupUnknown; } }
    internal NativeNeverActivatedRetirementSnapshot Read()
    { _main.VerifyAccess(); return new(_identity, _phase, _destroyFrame, _confirmedFrame, _failure); }

    internal static void TickForHost(LocalRunProtectionHost host)
    {
        NativeNeverActivatedRetirement[] snapshot;
        lock (ProcessGate) snapshot = ProcessOwners.Where(value => ReferenceEquals(value._registry, host.Sessions)).ToArray();
        foreach (var owner in snapshot)
        {
            // A nested native callback can reach process Tick while Start is still constructing.
            // Defer that owner rather than touching its half-published state or losing other work.
            if (owner._busy) continue;
            try
            {
                if (owner._cancelRequested && owner._phase == NativeNeverActivatedRetirementPhase.Ready) owner.RequestRetirement();
                owner.Tick();
            }
            catch (Exception failure) { owner.Fail(failure); }
        }
    }

    internal static bool HasPendingWorkForHost(LocalRunProtectionHost host)
        => ReadWorkForHost(host).Pending != 0;

    internal static NativeNeverActivatedRetirementWork ReadWorkForHost(LocalRunProtectionHost host)
    {
        lock (ProcessGate)
        {
            var owners = ProcessOwners.Where(value => ReferenceEquals(value._registry, host.Sessions)).ToArray();
            return new(owners.Count(value => value._phase != NativeNeverActivatedRetirementPhase.Closed),
                owners.Count(value => value._phase == NativeNeverActivatedRetirementPhase.CleanupUnknown));
        }
    }

    // Stop may be requested from a callback inside native construction. Set only managed intent
    // here; the next retained host Tick performs validation and requests Destroy outside that call.
    internal static void RequestCancellationForHost(LocalRunProtectionHost host)
    {
        lock (ProcessGate)
            foreach (var owner in ProcessOwners.Where(value => ReferenceEquals(value._registry, host.Sessions))) owner._cancelRequested = true;
    }

    internal void RequestRetirement()
    {
        Guard();
        if (_phase != NativeNeverActivatedRetirementPhase.Ready) return;
        _busy = true;
        try { RequirePending(); RequireInactive(); RequireContinuedProof(); BeginRetirement(); }
        catch (Exception failure) { Fail(failure); }
        finally { _busy = false; }
    }

    private void BeginRetirement()
    {
        var value = _registry.Read(_claim.Session);
        _request = new(value.Identity, value.ControllerIdentity, 0, 0, 0);
        _requestFrame = Frame();
        _registry.BeginReturn(_claim.Session);
        _phase = NativeNeverActivatedRetirementPhase.RetirementRequested;
    }

    /// <summary>Call from the retained host's Tick. No proof can issue on the requesting frame,
    /// the Destroy frame, or the same poll which first confirms the five owned Unity objects dead.</summary>
    internal void Tick()
    {
        Guard();
        if (_phase is NativeNeverActivatedRetirementPhase.Ready or NativeNeverActivatedRetirementPhase.Closed
            or NativeNeverActivatedRetirementPhase.CleanupUnknown) return;
        _busy = true;
        try
        {
            if (_backend.CleanupUnknown) throw new InvalidOperationException("Native transaction cleanup or mutation is unknown.");
            var frame = Frame();
            if (_phase == NativeNeverActivatedRetirementPhase.BundleDestroyPending)
            {
                if (frame <= _closeFrame || !_backend.TryCompleteBundleDestroy()) return;
                _phase = NativeNeverActivatedRetirementPhase.Closed;
                lock (ProcessGate) ProcessOwners.Remove(this);
                return;
            }
            RequireExactReturningSession(); RequireContinuedProof();
            if (_phase == NativeNeverActivatedRetirementPhase.RetirementRequested)
            {
                if (frame <= _requestFrame) return;
                RequireInactive();
                _backend.RequestControllerDestroy();
                _destroyFrame = Frame();
                RequireContinuedProof();
                _phase = NativeNeverActivatedRetirementPhase.DestroyRequested;
                return;
            }
            if (_phase == NativeNeverActivatedRetirementPhase.DestroyRequested)
            {
                if (frame <= _destroyFrame || !_backend.TryCompleteControllerDestroy()) return;
                if (!_backend.ControllerClosed || _backend.CleanupUnknown)
                    throw new InvalidOperationException("The owned inactive clone has not confirmed all five Unity objects dead and roots freed.");
                RequireContinuedProof();
                _confirmedFrame = Frame();
                _phase = NativeNeverActivatedRetirementPhase.DestructionConfirmed;
                return;
            }
            _issuing = true; _verifierUsed = false;
            bool attempted, closed;
            try { attempted = _issue(_claim.Session, this, out closed); }
            finally { _issuing = false; }
            var snapshot = _registry.Read(_claim.Session);
            if (!attempted)
            {
                if (snapshot.CleanupAttempted || snapshot.ProtectionRetired)
                    throw new InvalidOperationException("Issuance returned pending after changing exact session ownership.");
                return;
            }
            // The issuer's return value is never accepted as a substitute for the registry's
            // exact token/Close transaction. In particular a fake closed=true cannot release roots.
            if (!_verifierUsed || !closed || snapshot.Identity != _identity || snapshot.ControllerIdentity != _controller
                || snapshot.Phase != LocalRunSessionPhase.Closed || !snapshot.CleanupAttempted || !snapshot.ProtectionRetired
                || string.IsNullOrWhiteSpace(snapshot.ConsumerEndEvidence))
                throw new InvalidOperationException("Exact native retirement or registry ownership cleanup is unconfirmed.");
            _closeFrame = Frame();
            if (_releaseAttempted) throw new InvalidOperationException("Never-activated observation cleanup is one-shot.");
            _releaseAttempted = true;
            _backend.ReleaseAfterSessionClosed();
            _phase = NativeNeverActivatedRetirementPhase.BundleDestroyPending;
        }
        catch (Exception failure) { Fail(failure); }
        finally { _busy = false; }
    }

    bool ILocalRunConsumersEndedVerifier.Verify(LocalRunRetirementRequest request, out string evidence)
    {
        _main.VerifyAccess(); evidence = "";
        if (!_issuing || _verifierUsed || _phase != NativeNeverActivatedRetirementPhase.DestructionConfirmed
            || _request is null || request != _request || request.ControllerIdentity == 0
            || request.ResultSceneIdentity != 0 || request.ResultSceneGeneration != 0 || request.ResultIdentity != 0) return false;
        _verifierUsed = true;
        RequireExactReturningSession(); RequireContinuedProof();
        if (!_backend.ControllerClosed || _backend.CleanupUnknown || Frame() <= _destroyFrame) return false;
        evidence = $"never-active session={_identity.SessionId} generation={_identity.RunGeneration} controller={_controller} "
            + $"destroyFrame={_destroyFrame} deadFrame={_confirmedFrame} Awake=0 globals=same-owner-six-and-flag "
            + "fiveUnityObjects=dead nativePublication=none resultScene=0 result=0 callbackDrain=host-issuance-barrier";
        return true;
    }

    private void RequirePending()
    {
        var value = _registry.Read(_claim.Session);
        _registry.VerifyPendingBundleClaim(_claim, value.MetadataIdentity, _backend.BundleResponsibility);
        if (value.Identity != _identity || value.Phase != LocalRunSessionPhase.PendingCommit || value.ControllerIdentity != _controller
            || value.ReturnRequested || value.ResultSceneIdentity != 0 || value.ResultSceneGeneration != 0 || value.Result is not null
            || value.ResultCallClaimed || value.CleanupAttempted || value.ProtectionRetired
            || !ReferenceEquals(_registry.FindProtectedSession(LocalBoundaryKind.ControllerRetry, _controller), _claim.Session))
            throw new InvalidOperationException("Only this exact still-pending, never-committed session can enter inactive retirement.");
    }

    private void RequireExactReturningSession()
    {
        var value = _registry.Read(_claim.Session);
        if (_request is null || value.Identity != _identity || value.ControllerIdentity != _controller
            || value.Phase != LocalRunSessionPhase.Returning || !value.ReturnRequested || value.ResultSceneIdentity != 0
            || value.ResultSceneGeneration != 0 || value.Result is not null || value.ResultCallClaimed
            || value.CleanupAttempted || value.ProtectionRetired
            || !ReferenceEquals(_registry.FindProtectedSession(LocalBoundaryKind.ControllerRetry, _controller), _claim.Session))
            throw new InvalidOperationException("Never-active retirement lost its exact returning session or acquired result responsibilities.");
    }

    private void RequireInactive()
    {
        var value = _backend.ReadInactive();
        if (value.Controller != _controller || _controller == 0 || value.ControllerGameObject == 0 || value.ParentGameObject == 0
            || value.ControllerGameObject == value.ParentGameObject || value.ParentActiveSelf || value.ControllerActiveInHierarchy
            || !value.ParentMatches || value.LevelInformation != 0
            || value.Parameters != 0 && value.Parameters != _backend.ParametersIdentity)
            throw new InvalidOperationException("Retirement requires the exact owned inactive graph, null info and zero or exact bundle parameters.");
    }

    private void RequireContinuedProof()
    {
        if (_backend.CleanupUnknown || !_backend.ControllerBound || _backend.ControllerIdentity != _controller)
            throw new InvalidOperationException("Never-active transaction has unknown native responsibility or changed controller ownership.");
        _backend.VerifyRetainedIdentitiesAndSource();
        var awake = _backend.ReadAwake();
        if (!awake.ExactNormalClone || awake.CloneIdentity != _controller || !awake.TimingVerifiable
            || awake.NativeCalls != 0 || awake.OriginalCalls != 0 || awake.SuppressedReentries != 0 || awake.SuppressedWrongThread != 0
            || awake.WindowBefore != 0 || awake.WindowAfter != 0 || awake.CloneBefore != 0 || awake.CloneAfter != 0
            || awake.InFlight != 0 || awake.Failures.Count != 0 || awake.CloneRows.Count != 0)
            throw new InvalidOperationException("The pre-Instantiate Awake window must remain complete and continuously zero through destruction.");
        var baseline = _backend.BaselineGlobals; var current = _backend.ReadGlobals();
        if (!baseline.IsCurrentOwner || !current.IsCurrentOwner || baseline.OwnerIdentity == 0
            || current.OwnerIdentity != baseline.OwnerIdentity || baseline.ControllerSet != current.ControllerSet
            || baseline.DelegateIdentities.Count != 6 || current.DelegateIdentities.Count != 6
            || !baseline.DelegateIdentities.SequenceEqual(current.DelegateIdentities))
            throw new InvalidOperationException("Same-manager six-global identities and controllerSet must remain exactly at their pre-clone baseline.");
        Frame();
    }

    private int Frame()
    {
        var frame = _backend.Frame();
        if (frame < 0 || frame < _lastFrame) throw new InvalidOperationException("Unity frame observation is unknown or regressed.");
        return _lastFrame = frame;
    }
    private void Guard()
    { _main.VerifyAccess(); if (_busy) throw new InvalidOperationException("Reentrant never-active retirement is rejected without dropping responsibility."); }
    private void Fail(Exception failure)
    { _phase = NativeNeverActivatedRetirementPhase.CleanupUnknown; _failure = BoundedFailure(failure); }
    private static string BoundedFailure(Exception failure)
    {
        var text = failure.GetType().Name + ": " + failure.Message;
        text = new string(text.Where(character => !char.IsControl(character)).ToArray());
        return text.Length <= 768 ? text : text[..768];
    }
}

/// <summary>Real, pinned NativeRuntime implementation. Roots, actual Awake ledger, exact manager
/// baseline and the factory's five-object Destroy readback are all owned by this closed transaction.</summary>
internal sealed class NativeNeverActivatedRetirementBackend : INativeNeverActivatedRetirementBackend
{
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly ExactHookBroker _broker;
    private readonly OwnedNativeLocalPlayBundle _bundle;
    private OwnedNativeControllerPrefab? _prefab;
    private OwnedNativeControllerGlobals? _globals;
    private NativeControllerAwakeLedger? _ledger;
    private NativeControllerAwakeLedger.CaptureWindow? _window;
    private OwnedNativeInactiveController? _clone;
    private NativeInactiveControllerParameterBinding? _binding;
    private NativeRuntime.RootHandle? _source, _manager, _control, _observation, _pendingRegistryRoot;
    private LocalNativePlayBundleObservation? _inputs;
    private ControllerGlobalsSnapshot? _baseline;
    private IntPtr _time, _unityObject, _frameMethod, _aliveMethod;
    private long _controller;
    private bool _started, _bound, _unknown, _releaseAttempted;

    internal NativeNeverActivatedRetirementBackend(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker, OwnedNativeLocalPlayBundle bundle)
        => (_api, _main, _profile, _module, _broker, _bundle) = (api, main, profile, module, broker, bundle);

    public IDisposable BundleResponsibility => _bundle;
    public bool ControllerBound => _bound;
    public long ControllerIdentity => _observation?.Target.ToInt64() ?? 0;
    public long ParametersIdentity => _inputs?.ParametersIdentity ?? 0;
    public bool ControllerClosed => _clone?.IsClosed == true;
    public bool InjectionFailureCanRetire => _binding is not null && !_binding.RetentionAttempted
        && !_binding.ParametersExportAttempted && !_binding.MutationAttempted && !CleanupUnknown;
    public bool CleanupUnknown => _unknown || _clone?.CleanupUnknown == true || _bundle.CleanupUnknown
        || _globals?.CleanupUnknown == true || _window?.CleanupUnknown == true
        || _binding?.CleanupUnknown == true || _binding?.MutationOutcomeUnknown == true;
    public ControllerGlobalsSnapshot BaselineGlobals => _baseline ?? throw new InvalidOperationException("No pre-clone globals baseline.");

    public void CreateClosedTransaction(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim)
    {
        _main.VerifyAccess();
        if (_started) throw new InvalidOperationException("Never-active native construction is one-shot.");
        _started = true; RequireBuild();
        _inputs = _bundle.Read();
        registry.VerifyPendingBundleClaim(claim, _inputs.Parameters.Metadata, _bundle);
        var resolved = new NativeControllerPrefabResolver(_api, _main, _profile, _module).Resolve();
        _prefab = resolved.Prefab;
        if (resolved.Status != ControllerPrefabResolutionStatus.Ready || _prefab is null)
            throw new InvalidOperationException("Never-active native prefab admission refused: " + resolved.Status + ": " + resolved.Reason);
        _source = _api.Root(_prefab.BorrowPrefabPointer);
        _unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        _time = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time");
        var frame = _api.InspectDeclaredStaticMethod(_time, "get_frameCount", Array.Empty<string>(), "System.Int32", _module);
        if (frame.Entry is not { Rva: 0x155EFD0 } || (frame.Flags & 0x10u) == 0 || (frame.Flags & 0x400u) != 0
            || frame.GenericMethod || frame.InflatedMethod)
            throw new UnsupportedSignatureException("Unity frame getter must be the exact non-generic static Int32 entry at RVA 155EFD0.");
        _frameMethod = frame.MethodInfo;
        _aliveMethod = _api.Method(_unityObject, "op_Implicit", 1, true, new[] { "UnityEngine.Object" });
        if (_api.DescribeReturnType(_aliveMethod) != "System.Boolean") throw new UnsupportedSignatureException("Unity liveness getter signature changed.");
        var managerClass = _api.Class("Assembly-CSharp.dll", "", "GameManager");
        if (_api.DescribeFieldType(managerClass, "Instance") != "GameManager")
            throw new UnsupportedSignatureException("Never-active GameManager singleton field metadata changed.");
        _api.RequireInstanceFieldLayout(managerClass, "inLevelControl", "InLevelControl", 0x38);
        _manager = _api.Root(_api.ReadStatic<IntPtr>(managerClass, "Instance"));
        if (_api.ObjectClass(_manager.Target) != managerClass)
            throw new UnsupportedSignatureException("Never-active singleton root is not the exact GameManager class.");
        _control = _api.Root(_api.ReadWithPublication<IntPtr>(_manager.Target, "inLevelControl"));
        _globals = OwnedNativeControllerGlobals.Capture(_api, _main, _profile, _module, _manager, _control);
        _baseline = _globals.BaselineSnapshot();
        _ledger = NativeControllerAwakeLedger.Bind(_api, _main, _profile, _module, _broker, Frame);
        _window = _ledger.BeginCaptureWindow("never-activated-retirement");
        _prefab.VerifyStillCurrent();
        try { _clone = new NativeInactiveControllerFactory(_api, _main, _profile, _module).CreateBorrowing(_source); }
        catch (NativeInactiveControllerCreationException failure) { _clone = failure.Responsibility; _unknown = true; throw; }
        _observation = _clone.RetainController(); _controller = _observation.Target.ToInt64();
        // Registry protection gets its own root. The independent observation root survives factory
        // TryCompleteDestroy and is kept until exact registry Close has retired classification.
        _pendingRegistryRoot = _clone.RetainController();
        registry.BindPendingController(claim, _pendingRegistryRoot.Target.ToInt64(), _pendingRegistryRoot);
        _pendingRegistryRoot = null; _bound = true;
    }

    public void Inject(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim)
    {
        _main.VerifyAccess();
        try { _binding = NativeInactiveControllerParameterBinding.Bind(_api, _main, _profile, _module, registry, claim, _bundle, _clone!); }
        catch (NativeInactiveControllerParameterBindingException failure) { _binding = failure.Responsibility; throw; }
    }
    public NativeInactiveControllerObservation ReadInactive() => _clone!.Read();
    public ControllerAwakeLedgerSnapshot ReadAwake() => _window!.SnapshotFor(_observation!);
    public ControllerGlobalsSnapshot ReadGlobals() => _globals!.ObserveCurrent();
    public int Frame()
    {
        _main.VerifyAccess();
        try { return _api.InvokeValueWithPublication<int>(_frameMethod, IntPtr.Zero); }
        catch { _unknown = true; throw; }
    }
    public void VerifyRetainedIdentitiesAndSource()
    {
        _main.VerifyAccess();
        if (!_bound || _observation!.Target.ToInt64() != _controller || _controller == 0)
            throw new TargetExpiredException("Never-active exact controller observation root changed.");
        try
        {
            _prefab!.VerifyStillCurrent();
            if (_prefab.BorrowPrefabPointer != _source!.Target || _source.Target == _observation.Target
                || !_api.InvokeValueWithPublication<bool>(_aliveMethod, IntPtr.Zero, A.Ref(_source.Target))
                || _api.ReadWithPublication<IntPtr>(_source.Target, "LevelParams") != IntPtr.Zero
                || _api.ReadWithPublication<IntPtr>(_source.Target, "LevelInformation") != IntPtr.Zero)
                throw new InvalidOperationException("The borrowed source prefab changed; it is never owned or destroyed by retirement.");
        }
        catch { _unknown = true; throw; }
    }
    public void RequestControllerDestroy() => _clone!.RequestDestroy();
    public bool TryCompleteControllerDestroy() => _clone!.TryCompleteDestroy();
    public bool TryCompleteBundleDestroy() => _bundle.TryCompleteDestroy();

    public void ReleaseAfterSessionClosed()
    {
        _main.VerifyAccess();
        if (_releaseAttempted) throw new InvalidOperationException("Never-active native observation frees cannot be retried.");
        _releaseAttempted = true;
        var failures = new List<Exception>(9);
        void Attempt(Action action) { try { action(); } catch (Exception failure) { failures.Add(failure); } }
        // Each owner has independent one-shot cleanup. A failed record stays rooted by this backend
        // and the process coordinator; later ticks never retry a free or return its capacity.
        if (_binding is not null) Attempt(_binding.Dispose);
        if (_window is not null) Attempt(_window.Dispose);
        if (_ledger is not null) Attempt(_ledger.Dispose);
        if (_globals is not null) Attempt(_globals.Dispose);
        if (_observation is not null) Attempt(_observation.Dispose);
        if (_source is not null) Attempt(_source.Dispose);
        if (_prefab is not null) Attempt(_prefab.Dispose);
        if (_control is not null) Attempt(_control.Dispose);
        if (_manager is not null) Attempt(_manager.Dispose);
        if (failures.Count != 0)
        { _unknown = true; throw new AggregateException("Never-active native observation cleanup is unknown; failed roots remain process-owned.", failures); }
    }

    private void RequireBuild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Never-active retirement requires the pinned Windows x64 Unity build.");
        var path = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using var file = File.OpenRead(path); using var hash = SHA256.Create();
        if (Convert.ToHexString(hash.ComputeHash(file)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
            throw new UnsupportedSignatureException("Never-active retirement image differs from the pinned native build.");
    }
}
