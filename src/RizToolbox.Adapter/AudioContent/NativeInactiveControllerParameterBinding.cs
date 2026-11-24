using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record InactiveControllerParameterInputs(long ParametersIdentity, LocalNativeParametersObservation Parameters);
internal sealed record NativeInactiveControllerParameterBindingObservation(long Controller, long Parameters,
    LocalNativeParametersObservation ReferencesAndScalars, NativeInactiveControllerObservation InactiveController,
    bool SettingsInitializationVerified = false, bool CanActivate = false);

/// <summary>Small fault-injection boundary. Production construction always supplies the native backend below.
/// Responsibility properties are managed identities; no property may resolve a GC handle before RequireSupportedBuild.</summary>
internal interface IInactiveControllerParameterBindingBackend
{
    IDisposable BundleResponsibility { get; }
    IDisposable ControllerResponsibility { get; }
    void RequireSupportedBuild();
    InactiveControllerParameterInputs ReadBundle();
    NativeInactiveControllerObservation ReadController();
    void ValidateNativeMetadata(IntPtr controller, IntPtr parameters, LocalNativeParametersObservation expected);
    NativeRuntime.RootHandle RetainController();
    NativeRuntime.RootHandle RetainParameters(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim);
    LocalNativeParametersObservation ReadParameters(IntPtr parameters);
    void SetParameters(IntPtr controller, IntPtr parameters);
    bool ControllerClosed { get; }
    bool ControllerCleanupUnknown { get; }
    bool CallCleanupUnknown { get; }
    int RetainedCallArgumentRecords { get; }
}

/// <summary>
/// One-shot reference injection into an already bound, inactive exact controller. This is a pending
/// launch step only: it never activates, commits, calls GetInsideLevelParams, or initializes settings.
/// Two independent roots and both original owners remain strongly process-owned through retirement.
/// </summary>
internal sealed class NativeInactiveControllerParameterBinding : IDisposable
{
    private const int MaximumBindings = 32;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<NativeInactiveControllerParameterBinding> ProcessOwners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly IInactiveControllerParameterBindingBackend _backend;
    private readonly LocalRunSessionRegistry _registry;
    private readonly LocalRunSessionRegistry.CommitClaim _claim;
    private readonly LocalRunSessionIdentity _sessionIdentity;
    private readonly long _metadataIdentity, _controllerIdentity;
    // Fixed storage exists before retaining either root. A failed free keeps its record and quota.
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[2];
    private InactiveControllerParameterInputs? _inputs;
    private NativeInactiveControllerParameterBindingObservation? _observation;
    private NativeInactiveControllerParameterBindingObservation? _setterBaseline;
    private bool _busy, _retentionAttempted, _exportAttempted, _mutationAttempted, _mutationReturned, _bound;
    private bool _releaseAttempted, _closed, _freeUnknown;
    internal string? Failure { get; private set; }

    private NativeInactiveControllerParameterBinding(IMainThreadDispatcher main,
        IInactiveControllerParameterBindingBackend backend, LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.CommitClaim claim, LocalRunSessionSnapshot session)
    {
        (_main, _backend, _registry, _claim) = (main, backend, registry, claim);
        (_sessionIdentity, _metadataIdentity, _controllerIdentity) = (session.Identity, session.MetadataIdentity, session.ControllerIdentity);
    }

    internal static NativeInactiveControllerParameterBinding Bind(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr gameAssemblyModule, LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.CommitClaim claim, OwnedNativeLocalPlayBundle bundle,
        OwnedNativeInactiveController controller)
    {
        main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(bundle); ArgumentNullException.ThrowIfNull(controller);
        return BindBackend(main, new NativeInactiveControllerParameterBindingBackend(api, main, profile,
            gameAssemblyModule, bundle, controller), registry, claim);
    }

    /// <summary>Testable transaction algorithm. No fake backend is selected by the native entry point.</summary>
    internal static NativeInactiveControllerParameterBinding BindBackend(IMainThreadDispatcher main,
        IInactiveControllerParameterBindingBackend backend, LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.CommitClaim claim)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(claim);
        // This precedes every backend owner observation, root target, metadata call and allocation.
        backend.RequireSupportedBuild();
        var snapshot = registry.Read(claim.Session);
        registry.VerifyPendingBundleClaim(claim, snapshot.MetadataIdentity, backend.BundleResponsibility);
        if (snapshot.ControllerIdentity == 0 || snapshot.ProtectionRetired
            || !ReferenceEquals(registry.FindProtectedSession(LocalBoundaryKind.ControllerRetry, snapshot.ControllerIdentity), claim.Session))
            throw new InvalidOperationException("Parameter export requires this pending session's already bound exact controller.");

        var owner = new NativeInactiveControllerParameterBinding(main, backend, registry, claim, snapshot);
        lock (ProcessGate)
        {
            if (ProcessOwners.Count >= MaximumBindings)
                throw new InvalidOperationException("Inactive parameter binding exhausts its bounded 32-owner process responsibility.");
            if (ProcessOwners.Any(held => ReferenceEquals(held._backend.BundleResponsibility, backend.BundleResponsibility)
                || ReferenceEquals(held._backend.ControllerResponsibility, backend.ControllerResponsibility)
                || held._controllerIdentity == snapshot.ControllerIdentity))
                throw new InvalidOperationException("The bundle/controller already has a live or unknown parameter binding attempt.");
            ProcessOwners.Add(owner);
        }
        owner._busy = true;
        try
        {
            var before = backend.ReadController(); owner.RequireController(before, 0);
            var inputs = backend.ReadBundle(); owner.RequireInputs(inputs);
            owner._inputs = inputs;
            backend.ValidateNativeMetadata(new IntPtr(before.Controller), new IntPtr(inputs.ParametersIdentity), inputs.Parameters);
            owner._retentionAttempted = true;
            owner._roots[0] = backend.RetainController();
            if (owner._roots[0]!.Target.ToInt64() != owner._controllerIdentity)
                throw new TargetExpiredException("The independent controller root differs from this bound inactive clone.");

            owner.RequirePending();
            owner.RequireController(backend.ReadController(), 0);
            // Bundle export records the exact pending session before it attempts a GC handle.
            // Even a throwing export is one-shot and remains this owner's responsibility.
            owner._exportAttempted = true;
            owner._roots[1] = backend.RetainParameters(registry, claim);
            if (owner._roots[1]!.Target.ToInt64() != inputs.ParametersIdentity)
                throw new TargetExpiredException("The exported parameter root differs from the adopted bundle.");
            owner.RequireSameParameters(backend.ReadParameters(owner._roots[1]!.Target));
            owner.RequireSameInputs(backend.ReadBundle());
            owner.RequirePending();
            owner.RequireController(backend.ReadController(), 0);
            backend.ValidateNativeMetadata(owner._roots[0]!.Target, owner._roots[1]!.Target, inputs.Parameters);
            // No retry after this boundary, including a throw before native invocation can report
            // whether it entered. Retaining ownership is preferable to guessing mutation success.
            owner._mutationAttempted = true;
            backend.SetParameters(owner._roots[0]!.Target, owner._roots[1]!.Target);
            owner._mutationReturned = true;
            owner._observation = owner.InspectBound(); owner._setterBaseline = owner._observation; owner._bound = true;
            return owner;
        }
        catch (Exception failure)
        {
            owner.Failure = failure.Message;
            // Do not free/Destroy on a failed read, export, invocation, or read-back. The original
            // owners and every obtained root remain process-owned, even if this exception is dropped.
            throw new NativeInactiveControllerParameterBindingException(owner, failure);
        }
        finally { owner._busy = false; }
    }

    internal bool IsClosed { get { _main.VerifyAccess(); return _closed; } }
    internal bool RetentionAttempted { get { _main.VerifyAccess(); return _retentionAttempted; } }
    internal bool MutationAttempted { get { _main.VerifyAccess(); return _mutationAttempted; } }
    internal bool MutationOutcomeUnknown { get { _main.VerifyAccess(); return _mutationAttempted && (!_mutationReturned || !_bound); } }
    internal bool ParametersExportAttempted { get { _main.VerifyAccess(); return _exportAttempted; } }
    internal bool CleanupUnknown { get { _main.VerifyAccess(); return _freeUnknown || _backend.CallCleanupUnknown; } }
    internal int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null); } }
    internal int RetainedCallArgumentRecords { get { _main.VerifyAccess(); return _backend.RetainedCallArgumentRecords; } }
    internal NativeInactiveControllerParameterBindingObservation SetterBaseline
    {
        get
        {
            _main.VerifyAccess();
            if (!_bound || _closed || _releaseAttempted || Failure is not null || _setterBaseline is null || CleanupUnknown)
                throw new InvalidOperationException("There is no usable immutable completed inactive setter receipt.");
            return _setterBaseline;
        }
    }
    internal IDisposable ControllerResponsibilityForBinding { get { _main.VerifyAccess(); return _backend.ControllerResponsibility; } }
    // Pure main-thread identity/state query for the activation coordinator. This
    // is the completed inactive setter receipt, not settings or activation proof.
    internal bool IsBoundFor(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim,
        IDisposable bundle, IDisposable ownedController)
    {
        _main.VerifyAccess();
        return ReferenceEquals(_registry, registry) && ReferenceEquals(_claim, claim)
            && ReferenceEquals(_claim.Session, claim.Session) && ReferenceEquals(_backend.BundleResponsibility, bundle)
            && ReferenceEquals(_backend.ControllerResponsibility, ownedController)
            && _bound && !_busy && !_closed && !_releaseAttempted && Failure is null && !_freeUnknown && !_backend.CallCleanupUnknown;
    }

    internal NativeInactiveControllerParameterBindingObservation Read()
    {
        Guard();
        if (!_bound || _releaseAttempted || _closed || CleanupUnknown)
            throw new InvalidOperationException("An incomplete or retired parameter injection cannot be read as verified.");
        _busy = true;
        try { return _observation = InspectBound(); }
        catch (Exception failure) { _bound = false; _observation = null; Failure = failure.Message; throw; }
        finally { _busy = false; }
    }

    private NativeInactiveControllerParameterBindingObservation InspectBound()
    {
        RequirePending();
        var controller = _roots[0]!.Target; var parameters = _roots[1]!.Target;
        if (controller.ToInt64() != _controllerIdentity || parameters.ToInt64() != _inputs!.ParametersIdentity)
            throw new TargetExpiredException("Retained injection roots changed their exact native identities.");
        var observedController = _backend.ReadController(); RequireController(observedController, parameters.ToInt64());
        _backend.ValidateNativeMetadata(controller, parameters, _inputs.Parameters);
        var observedParameters = _backend.ReadParameters(parameters); RequireSameParameters(observedParameters);
        RequireSameInputs(_backend.ReadBundle());
        RequirePending();
        // Settings scalars are copied observations only. SetInsideLevelParams does not initialize
        // SettingsControl, and GetInsideLevelParams has gameplay/UI/coroutine side effects.
        return new(controller.ToInt64(), parameters.ToInt64(), observedParameters, observedController);
    }

    private void RequirePending()
    {
        _registry.VerifyPendingBundleClaim(_claim, _metadataIdentity, _backend.BundleResponsibility);
        var snapshot = _registry.Read(_claim.Session);
        if (snapshot.Identity != _sessionIdentity || snapshot.ControllerIdentity != _controllerIdentity
            || snapshot.ProtectionRetired || snapshot.CleanupAttempted
            || !ReferenceEquals(_registry.FindProtectedSession(LocalBoundaryKind.ControllerRetry, _controllerIdentity), _claim.Session))
            throw new InvalidOperationException("The injection no longer has its exact bound pending session.");
    }
    private void RequireController(NativeInactiveControllerObservation value, long parameters)
    {
        if (value.Controller != _controllerIdentity || value.ControllerGameObject == 0 || value.ParentGameObject == 0
            || value.ParentActiveSelf || value.ControllerActiveInHierarchy || !value.ParentMatches
            || value.Parameters != parameters || value.LevelInformation != 0)
            throw new InvalidOperationException("The bound controller must remain under its inactive parent with exact parameters and null LevelInformation.");
    }
    private void RequireInputs(InactiveControllerParameterInputs value)
    {
        ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(value.Parameters);
        var references = value.Parameters;
        if (value.ParametersIdentity == 0 || references.Metadata != _metadataIdentity || references.CoverReference == 0
            || references.MusicReference == 0 || references.Chart == 0 || references.JudgeData == 0)
            throw new InvalidOperationException("The adopted bundle must expose its exact complete five-reference parameter graph.");
    }
    private void RequireSameInputs(InactiveControllerParameterInputs value)
    {
        RequireInputs(value);
        if (value.ParametersIdentity != _inputs!.ParametersIdentity)
            throw new TargetExpiredException("The adopted bundle changed its parameter identity.");
        RequireSameParameters(value.Parameters);
    }
    private void RequireSameParameters(LocalNativeParametersObservation value)
    {
        ArgumentNullException.ThrowIfNull(value); var expected = _inputs!.Parameters;
        if (value.Metadata != expected.Metadata || value.CoverReference != expected.CoverReference
            || value.MusicReference != expected.MusicReference || value.Chart != expected.Chart || value.JudgeData != expected.JudgeData
            || value.SpeedBits != expected.SpeedBits || value.OffsetBits != expected.OffsetBits)
            throw new InvalidOperationException("Parameter injection must preserve every borrowed reference and both observed scalar bit patterns.");
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (_busy) throw new InvalidOperationException("Reentrant inactive parameter-binding operations are rejected without releasing responsibility.");
        _backend.RequireSupportedBuild();
    }

    /// <summary>Exact registry retirement plus confirmed clone destruction are both required. A
    /// successful setter, Dispose request, scene name or inactive flag is never consumer-end proof.</summary>
    public void Dispose()
    {
        Guard(); if (_closed) return;
        if (_releaseAttempted || CleanupUnknown)
            throw new InvalidOperationException("Unknown parameter-binding cleanup is retained and will not be retried.");
        var snapshot = _registry.Read(_claim.Session);
        if (snapshot.Identity != _sessionIdentity || snapshot.MetadataIdentity != _metadataIdentity
            || snapshot.ControllerIdentity != _controllerIdentity || !snapshot.ProtectionRetired || !snapshot.CleanupAttempted
            || string.IsNullOrWhiteSpace(snapshot.ConsumerEndEvidence)
            || !_backend.ControllerClosed || _backend.ControllerCleanupUnknown)
            throw new InvalidOperationException("Parameter-binding roots require this exact session's verified retirement and confirmed controller destruction.");
        _busy = true; _releaseAttempted = true; _bound = false; _observation = null;
        try
        {
            var failures = new List<Exception>(2);
            for (var index = _roots.Length - 1; index >= 0; index--)
                if (_roots[index] is { } root)
                    try { root.Dispose(); _roots[index] = null; }
                    catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0)
            {
                _freeUnknown = true; Failure = "Independent injection root cleanup is unknown; failed records and quota remain process-owned.";
                throw new AggregateException(Failure, failures);
            }
            _closed = true; lock (ProcessGate) ProcessOwners.Remove(this);
        }
        finally { _busy = false; }
    }
}

internal sealed class NativeInactiveControllerParameterBindingException : InvalidOperationException
{
    internal NativeInactiveControllerParameterBinding Responsibility { get; }
    internal NativeInactiveControllerParameterBindingException(NativeInactiveControllerParameterBinding responsibility, Exception cause)
        : base("Inactive controller parameter injection failed; bounded owner/root responsibility is retained.", cause)
        => Responsibility = responsibility;
}

/// <summary>Real NativeRuntime metadata and invocation backend. No setter-address detour is installed.</summary>
internal sealed class NativeInactiveControllerParameterBindingBackend : IInactiveControllerParameterBindingBackend
{
    private static readonly string[] ReferenceNames = { "metadata", "coverReference", "musicReference", "chart", "judgeData" };
    private static readonly string[] ReferenceTypes = { "ILevelMetadata", "IResourceReference<UnityEngine.Sprite>",
        "IResourceReference<UnityEngine.AudioClip>", "InLevel.Chart", "JudgeData" };
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly OwnedNativeLocalPlayBundle _bundle;
    private readonly OwnedNativeInactiveController _controller;
    private readonly NativePublicationArgumentRecords _callArguments;
    private IntPtr _parametersClass, _setter;
    private bool _buildVerified, _setterAttempted;

    internal NativeInactiveControllerParameterBindingBackend(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, OwnedNativeLocalPlayBundle bundle, OwnedNativeInactiveController controller)
    {
        (_api, _main, _profile, _module, _bundle, _controller) = (api, main, profile, module, bundle, controller);
        _callArguments = new(main);
    }
    public IDisposable BundleResponsibility => _bundle;
    public IDisposable ControllerResponsibility => _controller;
    public bool ControllerClosed => _controller.IsClosed;
    public bool ControllerCleanupUnknown => _controller.CleanupUnknown;
    public bool CallCleanupUnknown => _callArguments.CleanupUnknown;
    public int RetainedCallArgumentRecords => _callArguments.Count;

    public void RequireSupportedBuild()
    {
        _main.VerifyAccess();
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Inactive parameter injection requires the pinned Windows x64 Unity 2022.3.62f2 build.");
        if (_buildVerified) return;
        var image = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using var file = File.OpenRead(image); using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
            throw new UnsupportedSignatureException("Inactive parameter injection image differs from the supported build.");
        _buildVerified = true;
    }
    public InactiveControllerParameterInputs ReadBundle()
    { RequireSupportedBuild(); var value = _bundle.Read(); return new(value.ParametersIdentity, value.Parameters); }
    public NativeInactiveControllerObservation ReadController()
    { RequireSupportedBuild(); return _controller.Read(); }
    public NativeRuntime.RootHandle RetainController()
    { RequireSupportedBuild(); return _controller.RetainController(); }
    public NativeRuntime.RootHandle RetainParameters(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim)
    { RequireSupportedBuild(); return _bundle.RetainParametersForPendingSession(registry, claim); }

    public void ValidateNativeMetadata(IntPtr controller, IntPtr parameters, LocalNativeParametersObservation expected)
    {
        RequireSupportedBuild();
        var normal = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        var declaring = _api.Class("Assembly-CSharp.dll", "", "InLevelController");
        var parameterClass = _api.Class("Assembly-CSharp.dll", "", "InsideLevelParams");
        if (_api.ObjectClass(controller) != normal || _api.ObjectClass(parameters) != parameterClass)
            throw new UnsupportedSignatureException("Injection requires exact NormalModeInLevelController and InsideLevelParams objects.");
        _api.RequireAssignable(declaring, normal);
        _api.RequireInstanceFieldLayout(declaring, "LevelParams", "InsideLevelParams", 0x20);
        _api.RequireInstanceFieldLayout(declaring, "LevelInformation", "InLevel.LevelInformation", 0x28);
        if (_api.InspectMetadataFieldClass(declaring, "LevelParams", "InsideLevelParams").FieldClass.Class != parameterClass)
            throw new UnsupportedSignatureException("Controller LevelParams metadata disagrees with the exact parameter class.");
        _api.InspectMetadataFieldClass(declaring, "LevelInformation", "InLevel.LevelInformation");
        var references = new[] { expected.Metadata, expected.CoverReference, expected.MusicReference, expected.Chart, expected.JudgeData };
        for (var index = 0; index < ReferenceNames.Length; index++)
        {
            _api.RequireInstanceFieldLayout(parameterClass, ReferenceNames[index], ReferenceTypes[index], 0x10 + index * 8);
            var fieldClass = _api.InspectMetadataFieldClass(parameterClass, ReferenceNames[index], ReferenceTypes[index]).FieldClass.Class;
            _api.RequireAssignable(fieldClass, _api.ObjectClass(new IntPtr(references[index])));
        }
        _api.RequireInstanceFieldLayout(parameterClass, "speed", "System.Single", 0x38);
        _api.RequireInstanceFieldLayout(parameterClass, "offset", "System.Single", 0x3C);
        if (_api.ObjectClass(new IntPtr(expected.Metadata)) != _api.Class("Assembly-CSharp.dll", "", "LevelMetadata")
            || _api.ObjectClass(new IntPtr(expected.Chart)) != _api.Class("Assembly-CSharp.dll", "InLevel", "Chart")
            || _api.ObjectClass(new IntPtr(expected.JudgeData)) != _api.Class("Assembly-CSharp.dll", "", "JudgeData")
            || _api.InspectMetadataClass(_api.ObjectClass(new IntPtr(expected.CoverReference))).TypeName != "ResourceReference<UnityEngine.Sprite>"
            || _api.InspectMetadataClass(_api.ObjectClass(new IntPtr(expected.MusicReference))).TypeName != "ResourceReference<UnityEngine.AudioClip>")
            throw new UnsupportedSignatureException("Injection input classes differ from the bundle's exact ordinary metadata/wrapper/Chart/JudgeData graph.");
        var setter = _api.InspectDeclaredMethod(declaring, "SetInsideLevelParams", new[] { "InsideLevelParams" }, "System.Void", _module);
        if ((setter.Flags & (0x10u | 0x400u)) != 0 || setter.GenericMethod || setter.InflatedMethod
            || setter.Entry is not { Rva: 0x36B0E0 }
            || _api.InspectMethodParameterClass(setter.MethodInfo, 0, "InsideLevelParams").Class != parameterClass
            || _setter != IntPtr.Zero && _setter != setter.MethodInfo
            || _parametersClass != IntPtr.Zero && _parametersClass != parameterClass)
            throw new UnsupportedSignatureException("SetInsideLevelParams must be the exact non-generic instance void MethodInfo at RVA 36B0E0.");
        _parametersClass = parameterClass; _setter = setter.MethodInfo;
    }
    public LocalNativeParametersObservation ReadParameters(IntPtr parameters)
    {
        RequireSupportedBuild();
        if (_parametersClass == IntPtr.Zero || _api.ObjectClass(parameters) != _parametersClass)
            throw new UnsupportedSignatureException("Parameter read-back has no verified exact native class/layout.");
        var values = ReferenceNames.Select(name => _api.Read<IntPtr>(parameters, name).ToInt64()).ToArray();
        return new(values[0], values[1], values[2], values[3], values[4],
            _api.Read<float>(parameters, "speed"), _api.Read<float>(parameters, "offset"));
    }
    public void SetParameters(IntPtr controller, IntPtr parameters)
    {
        RequireSupportedBuild();
        if (_setter == IntPtr.Zero || _parametersClass == IntPtr.Zero || _setterAttempted)
            throw new InvalidOperationException("The validated exact parameter setter is one-shot.");
        _setterAttempted = true;
        var arguments = new[] { A.Ref(parameters) };
        _callArguments.Begin(arguments);
        try { _api.InvokeVoidWithPublication(_setter, controller, arguments); }
        catch { _callArguments.Fail(); throw; }
        _callArguments.Complete();
    }
}
