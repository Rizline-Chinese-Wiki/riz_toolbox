using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeLocalResultSource(long Controller, long Information, long Parameters, long Metadata);
internal sealed record NativeLocalResultValues(long Metadata, long Score, double RawCompletion,
    int Perfect, int MaxPerfect, int Bad, int Miss, bool Crashed, bool Skipped);
internal interface INativeLocalResultCaptureBackend
{
    bool CleanupUnknown { get; }
    void RequireSupportedBuild();
    NativeLocalResultSource ReadSource();
    NativeRuntime.RootHandle RetainController();
    NativeRuntime.RootHandle RetainInformation();
    void InvokeControllerOver(Action<NativeRuntime.RootHandle> publish);
    NativeLocalResultValues ReadResult(NativeRuntime.RootHandle result);
}

internal sealed class NativeLocalResultCaptureException : InvalidOperationException
{
    internal NativeLocalResultCapture Responsibility { get; }
    internal NativeLocalResultCaptureException(NativeLocalResultCapture owner, Exception cause)
        : base("LOCAL actual-result capture failed; its once-only claim and bounded native responsibility remain retained.", cause)
        => Responsibility = owner;
}

/// <summary>Host-only once-only actual ControllerOver capture. There is no stock Start,
/// save, achievement, fake result constructor, implicit retry or lifecycle-start publication.
/// Admission requires a committed exact run; activation/termination handlers must supply it.</summary>
internal sealed partial class NativeLocalResultCapture : IDisposable
{
    private const int MaximumCaptures = 32;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<NativeLocalResultCapture> ProcessOwners = new();
    private readonly IMainThreadDispatcher _main;
    private INativeLocalResultCaptureBackend? _backend;
    private readonly LocalRunSessionRegistry _registry;
    private readonly LocalRunSessionRegistry.SessionHandle _session;
    private LocalRunSessionSnapshot _entry;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[3]; // controller/info/unadopted result
    private NativeLocalResultSource? _source;
    private NativeLocalTerminalCoroutineTracker? _terminalCoroutines;
    private bool _busy, _invocationAttempted, _invocationReturned, _unknown, _freeAttempted, _closed;
    internal string? Failure { get; private set; }
    internal bool CleanupUnknown { get { _main.VerifyAccess(); return _unknown; } }
    internal bool InvocationAttempted { get { _main.VerifyAccess(); return _invocationAttempted; } }
    internal bool IsClosed { get { _main.VerifyAccess(); return _closed; } }
    internal int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null); } }
    internal static int RetainedProcessCaptureCount { get { lock (ProcessGate) return ProcessOwners.Count; } }
    internal void AttachTerminalCoroutines(NativeLocalTerminalCoroutineTracker tracker)
    {
        Guard(false); ArgumentNullException.ThrowIfNull(tracker);
        if (_terminalCoroutines is not null || _invocationAttempted || _unknown || _closed || tracker.ControllerIdentity != _entry.ControllerIdentity
            || (_backend is NativeLocalResultCaptureBackend ? !tracker.IsFor(_registry, _session) : !tracker.IsControlled))
            throw new InvalidOperationException("Actual result capture requires its own once-attached terminal coroutine observer.");
        _terminalCoroutines = tracker;
    }

    private NativeLocalResultCapture(IMainThreadDispatcher main, INativeLocalResultCaptureBackend? backend,
        LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session, LocalRunSessionSnapshot entry)
        => (_main, _backend, _registry, _session, _entry) = (main, backend, registry, session, entry);

    internal static NativeLocalResultCapture Create(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.SessionHandle session, NativeRuntime.RootHandle borrowedController)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(borrowedController);
        return CreateBackend(main, new NativeLocalResultCaptureBackend(api, main, profile, module, borrowedController), registry, session);
    }
    internal static NativeLocalResultCapture CreateBackend(IMainThreadDispatcher main, INativeLocalResultCaptureBackend backend,
        LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend); ArgumentNullException.ThrowIfNull(registry);
        backend.RequireSupportedBuild(); // No target read or backend-native observation before this gate.
        var entry = registry.Read(session);
        if (entry.Phase != LocalRunSessionPhase.Committed || entry.ReturnRequested || entry.ControllerIdentity == 0
            || entry.ProtectionRetired || entry.ResultCallClaimed
            || !ReferenceEquals(registry.FindProtectedSession(LocalBoundaryKind.ControllerRetry, entry.ControllerIdentity), session))
            throw new InvalidOperationException("Actual result capture requires an uncaptured committed exact LOCAL controller.");
        var owner = new NativeLocalResultCapture(main, backend, registry, session, entry);
        lock (ProcessGate)
        {
            if (ProcessOwners.Count >= MaximumCaptures || ProcessOwners.Any(item => ReferenceEquals(item._session, session)))
                throw new InvalidOperationException("This run already has a capture owner or the 32-owner responsibility bound is exhausted.");
            ProcessOwners.Add(owner);
        }
        owner._busy = true;
        try
        {
            owner._nativeAttempted = true; owner._initializationAttempted = true;
            owner._source = backend.ReadSource(); owner.RequireSource(owner._source);
            owner._roots[0] = backend.RetainController(); owner._roots[1] = backend.RetainInformation();
            owner.VerifyRootsAndSource(); owner._initialized = true; return owner;
        }
        catch (Exception error) { owner._unknown |= backend.CleanupUnknown; owner.Failure = error.Message; throw new NativeLocalResultCaptureException(owner, error); }
        finally { owner._busy = false; }
    }

    internal LocalRunCapturedResult? Capture()
    {
        Guard(false); if (_closed || _unknown) throw new InvalidOperationException("Retired or unknown result capture cannot be invoked again.");
        var snapshot = _registry.Read(_session);
        if (snapshot.ProtectionRetired || snapshot.Phase == LocalRunSessionPhase.Closed) return null;
        if (snapshot.Result is not null) return snapshot.Result;
        if (_invocationAttempted) return null;
        if (snapshot.Phase != LocalRunSessionPhase.Committed || snapshot.ReturnRequested || snapshot.ResultCallClaimed) return null;
        if (_backend is NativeLocalResultCaptureBackend && _terminalCoroutines?.IsFor(_registry, _session) != true)
            throw new InvalidOperationException("Production ControllerOver capture requires this session's terminal coroutine tracker before any invocation.");
        if (!_initialized) InitializeWhenCommitted();
        else _backend!.RequireSupportedBuild();
        _busy = true; LocalRunSessionRegistry.ResultCaptureClaim? claim = null;
        try
        {
            if (snapshot.Phase != LocalRunSessionPhase.Committed || snapshot.ReturnRequested || snapshot.ResultCallClaimed)
                return null;
            VerifyRootsAndSource();
            var outcome = _registry.TryClaimResult(_session);
            if (outcome.Kind != LocalRunResultClaimKind.InvokeOnce) return outcome.Cached;
            claim = outcome.Claim!;
            _invocationAttempted = true; // Consume before the actual native call, including its reentry.
            using (var terminalWindow = _terminalCoroutines?.BeginControllerOver())
            {
                _backend!.InvokeControllerOver(root => {
                    _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(root);
                    if (_roots[2] is not null || !_busy || _invocationReturned)
                        throw new InvalidOperationException("The actual result must publish once synchronously before temporary cleanup.");
                    _roots[2] = root;
                });
                terminalWindow?.Complete();
            }
            _invocationReturned = true;
            if (_roots[2] is null) throw new InvalidOperationException("ControllerOver returned without a rooted actual result.");
            VerifyRootsAndSource();
            var values = _backend!.ReadResult(_roots[2]!);
            if (values.Metadata != _entry.MetadataIdentity || !double.IsFinite(values.RawCompletion)
                || values.Perfect < 0 || values.MaxPerfect < 0 || values.Bad < 0 || values.Miss < 0)
                throw new InvalidOperationException("Actual result metadata/counters do not form this run's finite copied result.");
            // A capture is not evidence that a real playback start was observed. Keep RunId
            // null/AssociatedWithObservedStart false until the separate lifecycle source proves it.
            var copy = new RunFinished(null, null, values.Score, values.RawCompletion,
                values.Perfect, values.MaxPerfect, values.Bad, values.Miss, values.Crashed, values.Skipped)
            { EndReason = GameEventState.CapturedEndReason(values.Crashed, values.Skipped) };
            var captured = _registry.CompleteResult(claim, _roots[2]!.Target.ToInt64(), values.Metadata, copy, _roots[2]!);
            _roots[2] = null; return captured; // Registry now owns the exact root.
        }
        catch (Exception error)
        {
            Failure = error.Message;
            if (_invocationAttempted || _backend!.CleanupUnknown) _unknown = true;
            if (claim is not null)
            {
                var current = _registry.Read(_session);
                if (current.Phase == LocalRunSessionPhase.ResultCapturing)
                    _registry.FailResult(claim, "Actual ControllerOver capture failed; no retry or stock fallback is permitted.");
            }
            throw new NativeLocalResultCaptureException(this, error);
        }
        finally { _busy = false; }
    }
    private void RequireSource(NativeLocalResultSource source)
    {
        if (source.Controller != _entry.ControllerIdentity || source.Metadata != _entry.MetadataIdentity
            || source.Parameters == 0 || source.Information == 0 || _source is not null && source != _source)
            throw new InvalidOperationException("Actual result source changed its exact controller/information/parameters/metadata graph.");
    }
    private void VerifyRootsAndSource()
    {
        var current = _registry.Read(_session);
        if (current.Identity != _entry.Identity || current.ControllerIdentity != _entry.ControllerIdentity
            || current.MetadataIdentity != _entry.MetadataIdentity || current.ProtectionRetired
            || _roots[0]?.Target.ToInt64() != _entry.ControllerIdentity || _roots[1]?.Target.ToInt64() != _source!.Information)
            throw new InvalidOperationException("Result source roots no longer belong to this exact protected run.");
        RequireSource(_backend!.ReadSource());
    }
    private void Guard(bool requireBuild = true)
    {
        _main.VerifyAccess(); if (_busy) throw new InvalidOperationException("Result capture reentry cannot repeat the native invocation or release its roots.");
        if (requireBuild) _backend!.RequireSupportedBuild();
    }
    public void Dispose()
    {
        Guard(false); if (_closed) return;
        if (_unknown || _freeAttempted) throw new InvalidOperationException("Unknown actual-result responsibility cannot be retried or forgotten.");
        if (_pendingClaim is null) _backend!.RequireSupportedBuild();
        var current = _registry.Read(_session);
        if (current.Identity != _entry.Identity || current.Phase != LocalRunSessionPhase.Closed || !current.ProtectionRetired || !current.CleanupAttempted
            || string.IsNullOrWhiteSpace(current.ConsumerEndEvidence))
            throw new InvalidOperationException("Result capture roots require this exact run's verified native consumer retirement.");
        _busy = true; _freeAttempted = true;
        try
        {
            var failures = new List<Exception>(3);
            for (var index = 2; index >= 0; index--)
                if (_roots[index] is { } root)
                    try { root.Dispose(); _roots[index] = null; } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) { _unknown = true; throw new AggregateException("Actual result root cleanup is unknown and will not be retried.", failures); }
            _closed = true; lock (ProcessGate) ProcessOwners.Remove(this);
        }
        finally { _busy = false; }
    }
}

/// <summary>Production NativeRuntime implementation. Invokes the original exact MethodInfo;
/// callers must already isolate termination/stock result/save paths before admitting a run.</summary>
internal sealed class NativeLocalResultCaptureBackend : INativeLocalResultCaptureBackend
{
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile; private readonly IntPtr _module;
    private readonly NativeRuntime.RootHandle _controller;
    private NativeRuntime.RootHandle? _retainedController;
    public bool CleanupUnknown { get; private set; }
    private bool _verified; private IntPtr _normal, _info, _params, _result, _over, _score, _completion, _alive;
    internal NativeLocalResultCaptureBackend(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, NativeRuntime.RootHandle controller)
        => (_api, _main, _profile, _module, _controller) = (api, main, profile, module, controller);
    public void RequireSupportedBuild()
    {
        _main.VerifyAccess();
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Actual LOCAL result capture requires the pinned Windows x64 Unity build.");
        if (_verified) return;
        using var image = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll"));
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(image)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
            throw new UnsupportedSignatureException("Actual result capture image differs from its pinned build.");
        _normal = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        _params = _api.Class("Assembly-CSharp.dll", "", "InsideLevelParams");
        _info = _api.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation");
        _result = _api.Class("Assembly-CSharp.dll", "", "InsideLevelResult");
        _alive = _api.Method(_api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object"),
            "op_Implicit", 1, true, new[] { "UnityEngine.Object" });
        if (_api.DescribeReturnType(_alive) != "System.Boolean")
            throw new UnsupportedSignatureException("Result-source Unity liveness return metadata changed.");
        var controllerBase = _api.Class("Assembly-CSharp.dll", "", "InLevelController");
        _api.RequireInstanceFieldLayout(controllerBase, "LevelParams", "InsideLevelParams", 0x20);
        _api.RequireInstanceFieldLayout(controllerBase, "LevelInformation", "InLevel.LevelInformation", 0x28);
        _api.RequireInstanceFieldLayout(_params, "metadata", "ILevelMetadata", 0x10);
        _api.RequireInstanceFieldLayout(_result, "metadata", "ILevelMetadata", 0x10);
        foreach (var (field, offset) in new[] { ("miss", 0x3C), ("bad", 0x40), ("perfect", 0x44), ("maxPerfect", 0x58) })
            _api.RequireInstanceFieldLayout(_result, field, "System.Int32", offset);
        _api.RequireInstanceFieldLayout(_result, "crashed", "System.Boolean", 0x60);
        _api.RequireInstanceFieldLayout(_result, "skip", "System.Boolean", 0x61);
        _over = Exact(_normal, "ControllerOver", "InsideLevelResult", 0x3BEA60);
        _score = Exact(_result, "get_Score", "System.Int32", 0x3757C0);
        _completion = Exact(_result, "get_RawCompleteRate", "System.Single", 0x3757B0);
        _verified = true;
    }
    private IntPtr Exact(IntPtr type, string name, string returns, long rva)
    {
        var method = _api.InspectDeclaredMethod(type, name, Array.Empty<string>(), returns, _module);
        if (method.Entry?.Rva != rva || (method.Flags & (0x10 | 0x400)) != 0 || method.GenericMethod || method.InflatedMethod)
            throw new UnsupportedSignatureException("Actual result entry has no verified exact instance metadata: " + name);
        return method.MethodInfo;
    }
    public NativeLocalResultSource ReadSource()
    {
        RequireSupportedBuild();
        if (CleanupUnknown) throw new InvalidOperationException("Actual result-source read has an unconfirmed prior native outcome.");
        try { return ReadSourceCore(); }
        catch { CleanupUnknown = true; throw; }
    }
    private NativeLocalResultSource ReadSourceCore()
    {
        var controller = (_retainedController ?? _controller).Target;
        if (_api.ObjectClass(controller) != _normal) throw new UnsupportedSignatureException("Result source controller class differs.");
        var parameters = _api.ReadWithPublication<IntPtr>(controller, "LevelParams"); var information = _api.ReadWithPublication<IntPtr>(controller, "LevelInformation");
        if (parameters == IntPtr.Zero || information == IntPtr.Zero || _api.ObjectClass(parameters) != _params || _api.ObjectClass(information) != _info)
            throw new UnsupportedSignatureException("Actual ControllerOver requires its exact initialized parameter/information graph.");
        if (!_api.InvokeValueWithPublication<bool>(_alive, IntPtr.Zero, A.Ref(controller))
            || !_api.InvokeValueWithPublication<bool>(_alive, IntPtr.Zero, A.Ref(information)))
            throw new TargetExpiredException("Actual result source controller or LevelInformation was destroyed.");
        return new(controller.ToInt64(), information.ToInt64(), parameters.ToInt64(), _api.ReadWithPublication<IntPtr>(parameters, "metadata").ToInt64());
    }
    public NativeRuntime.RootHandle RetainController()
    {
        RequireSupportedBuild();
        if (_retainedController is not null) throw new InvalidOperationException("Capture controller rooting is one-shot.");
        return _retainedController = _api.Root(_controller.Target);
    }
    public NativeRuntime.RootHandle RetainInformation()
    { RequireSupportedBuild(); return _api.Root(new IntPtr(ReadSource().Information)); }
    public void InvokeControllerOver(Action<NativeRuntime.RootHandle> publish)
    {
        RequireSupportedBuild();
        _api.InvokeReferenceWithPublication(_over, (_retainedController ?? throw new InvalidOperationException("No independent capture root.")).Target,
            pointer => publish(_api.Root(pointer)));
    }
    public NativeLocalResultValues ReadResult(NativeRuntime.RootHandle result)
    {
        RequireSupportedBuild();
        if (CleanupUnknown) throw new InvalidOperationException("Actual result decode has an unconfirmed prior native outcome.");
        try { return ReadResultCore(result); }
        catch { CleanupUnknown = true; throw; }
    }
    private NativeLocalResultValues ReadResultCore(NativeRuntime.RootHandle result)
    {
        var pointer = result.Target;
        if (_api.ObjectClass(pointer) != _result) throw new UnsupportedSignatureException("ControllerOver did not return the exact actual result class.");
        return new(_api.ReadWithPublication<IntPtr>(pointer, "metadata").ToInt64(), _api.InvokeValueWithPublication<int>(_score, pointer),
            _api.InvokeValueWithPublication<float>(_completion, pointer), _api.ReadWithPublication<int>(pointer, "perfect"),
            _api.ReadWithPublication<int>(pointer, "maxPerfect"), _api.ReadWithPublication<int>(pointer, "bad"), _api.ReadWithPublication<int>(pointer, "miss"),
            _api.ReadWithPublication<bool>(pointer, "crashed"), _api.ReadWithPublication<bool>(pointer, "skip"));
    }
}
