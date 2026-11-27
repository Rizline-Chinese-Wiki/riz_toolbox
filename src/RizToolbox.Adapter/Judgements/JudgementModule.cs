using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class JudgementModule : IAdapterModule
{
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main; private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile; private readonly GameEventState _state; private readonly EventHub _events;
    private readonly string _dobby, _helper;
    private ExactHookBroker.Binding? _outer, _inner, _hold, _holdStart;
    private readonly ExactHookBroker.Binding?[] _holdStops = new ExactHookBroker.Binding?[6];
    private NativeJudgementBackend? _backend;
    private JudgementCoordinator? _coordinator;
    private HoldLifecycleDiagnostics? _holdDiagnostics;
    private long _generation;
    public double IntervalSeconds => .25;
    public AdapterModuleManifest Manifest { get; } = new("rizline.judgement-identity",new(0,1),
        new[] { JudgementCapabilityIds.Observe },new[] { GameLifecycleCapabilityIds.Lifecycle },
        Array.Empty<string>(),new[] {
            new AdapterSymbolRequirement("outer","Assembly-CSharp.dll","InLevel","GetJudgeControl",AdapterSymbolKind.Method,
                "GetJudge",false,new[] { "System.Int32","System.Single" }) { ExpectedType = "System.Void" },
            new AdapterSymbolRequirement("note","Assembly-CSharp.dll","InLevel","NoteInformation")
        },"Pinned 12-source native caller context, per-run rooted note graph, exact inner original-return counter snapshots. Source coverage and time calibration remain independently observable.");
    internal JudgementModule(NativeRuntime api,IMainThreadDispatcher main,ILogger log,AdapterBuildProfile profile,
        GameEventState state,EventHub events,string dobby,string helper)
        => (_api,_main,_log,_profile,_state,_events,_dobby,_helper) = (api,main,log,profile,state,events,dobby,helper);
    public ModuleProbe Probe()
    {
        _main.VerifyAccess(); if (_coordinator is not null) return ModuleProbe.Available;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _profile.GameAssemblySha256 != Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            return ModuleProbe.Unsupported("UnsupportedBuild: judgement identity requires the pinned Windows x64 build.");
        if (!File.Exists(_helper)) return ModuleProbe.Unsupported("Native judge context helper is absent from the package.");
        var timer = Stopwatch.StartNew();
        using var file = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!,"GameAssembly.dll"));
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant() != Fingerprint)
            return ModuleProbe.Unsupported("UnsupportedBuild: judgement identity GameAssembly fingerprint differs.");
        var fingerprintMs = timer.ElapsedMilliseconds;
        var module = GetModuleHandle("GameAssembly.dll");
        var outer = _api.ResolveExactHook("framework.judgements.caller",module,_api.Class("Assembly-CSharp.dll","InLevel","GetJudgeControl"),
            "GetJudge",new[] { "System.Int32","System.Single" },0x3e8030,ExactHookAbi.InstanceInt32SingleVoid);
        var inner = _api.ResolveExactHook("framework.judgements.inner",module,_api.Class("Assembly-CSharp.dll","","NormalModeInLevelController"),
            "GetJudge",new[] { "InLevelController.JudgeType","System.Single" },0x3c0310,ExactHookAbi.InstanceEnum32SingleVoid);
        var hold = _api.ResolveExactHook("framework.judgements.hold-context",module,
            _api.NestedClass(_api.Class("Assembly-CSharp.dll","InLevel","NoteJudgeControl"),"<CheckHold>d__32"),
            "MoveNext",Array.Empty<string>(),0x402670,ExactHookAbi.InstanceBooleanReturn0);
        var holdStart = _api.ResolveCoroutineStartHook(module);
        var holdStartAlias = _api.ResolveCoroutineStartHook(module, automaticAlias: true);
        ValidateCallSites(module);
        var targetsReadyMs = timer.ElapsedMilliseconds;
        var generation = ++_generation;
        var backend = new NativeJudgementBackend(_api,_state);
        var coordinator = new JudgementCoordinator(backend,_main,_log,unchecked((ulong)module.ToInt64()),
            () => ExactHookBroker.NativeJudgeContextDepth,() => _state.RunId,dto =>
            {
                _events.Publish(new Delivery(this,generation,dto));
                // Bounded first samples per source are captured by the existing diagnostic logger.
                var status = _coordinator?.Status();
                if (status?.Sources.First(x => x.Id == dto.SourceId).CapturedCalls <= 2)
                    _log.Log(LogLevel.Info,"Judgement identity sample: " + System.Text.Json.JsonSerializer.Serialize(dto));
            });
        var broker = new ExactHookBroker(_dobby,_main,_log);
        try
        {
            _coordinator = coordinator; _backend = backend; _holdDiagnostics = new(_log);
            var backendReadyMs = timer.ElapsedMilliseconds;
            backend.InitializeHoldLifecycle(_main);
            var lifecycleReadyMs = timer.ElapsedMilliseconds;
            TryInstallHoldStops(broker, module, backend);
            var stopsReadyMs = timer.ElapsedMilliseconds;
            // Native owns the first MoveNext patch and delivers the raw AL
            // result; its capture also retains synchronous GetJudge context.
            _hold = broker.BindNativeCoroutineObservation(hold, NativeCoroutineKind.MoveNext,
                () => new NativeCoroutineContextBridge(_helper, NativeCoroutineKind.MoveNext, _log),
                backend.CaptureHoldMove, backend.CompleteHoldMove, backend.ObserveHoldLifecycleFault);
            _holdStart = broker.BindNativeCoroutineObservation(holdStart, NativeCoroutineKind.Start,
                () => new NativeCoroutineContextBridge(_helper, NativeCoroutineKind.Start, _log),
                backend.CaptureHoldStart, backend.CompleteHoldStart, backend.ObserveHoldLifecycleFault, holdStartAlias);
            _inner = broker.BindRetainedJudgeObservation(inner,receiver => _api.ExactHookReceiverMatches(receiver,inner.DeclaringClass),
                coordinator.CaptureInner,coordinator.CompleteInner);
            _outer = broker.BindNativeJudgeContext(outer,() => new NativeJudgeContextBridge(_helper,_log),
                coordinator.CaptureOuter,(_,_) => { });
            var observersReadyMs = timer.ElapsedMilliseconds;
            if (observersReadyMs >= 100)
                _log.Log(LogLevel.Warning, $"Judgement activation timings (ms): fingerprint={fingerprintMs}, " +
                    $"resolveAndValidate={targetsReadyMs - fingerprintMs}, backendSetup={backendReadyMs - targetsReadyMs}, " +
                    $"holdLifecycle={lifecycleReadyMs - backendReadyMs}, holdStops={stopsReadyMs - lifecycleReadyMs}, " +
                    $"observers={observersReadyMs - stopsReadyMs}.");
            _log.Log(LogLevel.Info,"Judgement identity installed: native caller@3E8030; inner@3C0310; Hold Start@153D380 and raw MoveNext@402670. Stop request availability is separate; engine cancellation and full Hold coverage remain unconfirmed.");
            return ModuleProbe.Available;
        }
        catch { Release(); throw; }
    }
    private void TryInstallHoldStops(ExactHookBroker broker, IntPtr module, NativeJudgementBackend backend)
    {
        try
        {
            // Reuse the native first-install channels. A missing bootstrap is
            // an unavailable Stop observer, never permission for a late patch.
            foreach (var row in CoroutineStopProfile.All)
            {
                var operation = row.Operation;
                var target = _api.ResolveCoroutineStopHook(module, operation);
                _holdStops[(int)operation] = broker.BindNativeOperationObservation(target, row.Kind,
                    frame => backend.CaptureHoldStop(operation, frame), backend.CompleteHoldStop,
                    (frame, reason) => backend.ObserveHoldStopFault(operation, frame, reason));
            }
            backend.CompleteHoldStopInstallation();
        }
        catch (Exception error)
        {
            // Keep partial bindings owned until Release. Judgement and its
            // independent Start/Move observations continue without Stop credit.
            backend.MarkHoldStopsUnavailable(error.Message);
            try { _log.Log(LogLevel.Warning, "Hold StopUnavailable: " + error.Message); } catch { }
        }
    }
    private void ReleaseHoldStops()
    {
        List<Exception>? failures = null;
        for (var index = _holdStops.Length - 1; index >= 0; --index)
        {
            try { _holdStops[index]?.Dispose(); }
            catch (Exception error) { (failures ??= new()).Add(error); }
            finally { _holdStops[index] = null; }
        }
        if (failures is not null) throw new AggregateException("Hold Stop binding withdrawal is unknown.", failures);
    }
    private static void ValidateCallSites(IntPtr module)
    {
        foreach (var site in JudgementSourceResolver.Sites)
        {
            var callRva = checked((int)site.ReturnRva - 5); var address = IntPtr.Add(module,callRva);
            HookTargetValidation.VerifyExecutable(address,module); HookTargetValidation.VerifyReadable(address,5);
            if (Marshal.ReadByte(address) != 0xe8 || (long)site.ReturnRva + Marshal.ReadInt32(address,1) != 0x3e8030)
                throw new UnsupportedSignatureException("The native judgement caller profile changed: " + site.Id);
        }
    }
    public void Register(ModuleRegistration registration)
        => registration.RegisterScoped<IGameplayJudgementService>(JudgementCapabilityIds.Observe,
            context => context.Own(new View(this,context,_generation)));
    public ModuleProbe Tick()
    {
        if (_coordinator is null) return ModuleProbe.Waiting("Judgement observer is detached.");
        _holdDiagnostics!.Sample(_backend!.HoldLifecycle);
        _backend.TickHoldLifecycle(); _coordinator.Tick(); return ModuleProbe.Available;
    }
    public void Release()
    {
        _main.VerifyAccess(); var coordinator = _coordinator; _coordinator = null; _holdDiagnostics = null; ++_generation;
        try { ReleaseHoldStops(); }
        finally { ReleaseJudgementBindings(coordinator); }
    }
    private void ReleaseJudgementBindings(JudgementCoordinator? coordinator)
    {
        try { _outer?.Dispose(); _outer = null; }
        finally
        {
            try { _inner?.Dispose(); _inner = null; }
            finally
            {
                try { _holdStart?.Dispose(); _holdStart = null; }
                finally
                {
                    try { _hold?.Dispose(); _hold = null; }
                    finally { try { _backend?.Dispose(); _backend = null; } finally { coordinator?.Dispose(); } }
                }
            }
        }
    }
    private sealed record Delivery(JudgementModule Owner,long Generation,GameplayJudgementObservation Value);
    private sealed class View : IGameplayJudgementService, IDisposable
    {
        private readonly JudgementModule _owner; private readonly IPluginContext _context; private readonly long _generation;
        private bool _disposed;
        internal View(JudgementModule owner,IPluginContext context,long generation) => (_owner,_context,_generation) = (owner,context,generation);
        private void Verify()
        {
            _owner._main.VerifyAccess(); _context.MainThread.VerifyAccess();
            if (_disposed || _owner._coordinator is null || _owner._generation != _generation)
                throw new ObjectDisposedException("Judgement provider generation expired.");
        }
        public Result<JudgementObserverStatus> GetStatus()
        {
            try { Verify(); return Result<JudgementObserverStatus>.Ok(_owner._coordinator!.Status()); }
            catch (Exception error) { return Result<JudgementObserverStatus>.Fail(Error(error)); }
        }
        public Result<IDisposable> Subscribe(Action<GameplayJudgementObservation> observer,int priority = 0)
        {
            try
            {
                Verify(); ArgumentNullException.ThrowIfNull(observer);
                return Result<IDisposable>.Ok(_context.Own(_context.Events.Subscribe<Delivery>(delivery =>
                {
                    if (!_disposed && _owner._generation == _generation && ReferenceEquals(delivery.Owner,_owner) && delivery.Generation == _generation)
                        observer(delivery.Value);
                },priority)));
            }
            catch (Exception error) { return Result<IDisposable>.Fail(Error(error)); }
        }
        private static OperationError Error(Exception error) => new(error is ObjectDisposedException ? ErrorCode.ScopeExpired
            : error is ArgumentException ? ErrorCode.InvalidArgument : ErrorCode.WrongThread,error.Message);
        public void Dispose() { _owner._main.VerifyAccess(); _disposed = true; }
    }
    [DllImport("kernel32",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}

internal sealed class HoldLifecycleDiagnostics
{
    private readonly ILogger _log;
    private (string? Session, string? Run, long? Scene) _run;
    private bool _startReported, _naturalExitReported, _faultReported;

    internal HoldLifecycleDiagnostics(ILogger log) => _log = log;

    // The existing Judgement module tick samples managed evidence. This path
    // never calls a native getter or emits from Start/Move callbacks.
    internal void Sample(HoldCoroutineLifecycle lifecycle)
    {
        if (_faultReported) return;
        // Once both events have been reported, only check for a new run or a fault.
        var sample = lifecycle.ReadDiagnosticSample(_startReported && _naturalExitReported ? _run : null);
        var run = (sample.SessionId, sample.RunId, sample.SceneGeneration);
        if (_run != run)
        {
            _run = run;
            _startReported = false;
            _naturalExitReported = false;
        }
        long? start = _startReported ? null : sample.StartReturnedAndExitedSequence;
        long? naturalExit = _naturalExitReported ? null : sample.NaturalExitSequence;
        if (start is null && naturalExit is null && !sample.Fault) return;
        _startReported |= start.HasValue;
        _naturalExitReported |= naturalExit.HasValue;
        _faultReported = sample.Fault;
        var reason = sample.Reason is { Length: > 200 } note ? note[..200] : sample.Reason;
        try
        {
            _log.Log(sample.Fault ? LogLevel.Warning : LogLevel.Info, "Hold lifecycle sample: " + JsonSerializer.Serialize(new
            {
                sample.SessionId, sample.RunId, sample.SceneGeneration, StartReturnedAndExitedSequence = start,
                NaturalExitSequence = naturalExit, sample.Fault, Reason = reason
            }));
        }
        catch { }
    }
}
