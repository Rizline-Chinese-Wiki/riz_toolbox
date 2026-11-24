using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

/// <summary>Independent raw clock probes and explicitly experimental, live-state-checked controls.</summary>
internal sealed class GameplayModule : IAdapterModule
{
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main; private readonly Func<GameLifecycleSnapshot?> _lifecycle;
    private readonly Action<RunModificationInput>? _recordModification;
    private readonly Func<long, long, string, long, bool> _matchesCurrentGameplayRun;
    private readonly INativePlaybackRateProvider? _rateProvider;
    private readonly bool _pumpAfterObservation;
    private readonly Func<bool>? _playbackRequested;
    private readonly Func<GameplayObservationBoundary>? _controlBoundary;
    private readonly Func<NativeRuntime.RootHandle?>? _observedController;
    private readonly CallbackPerformanceMonitor? _performance;
    private readonly ILogger? _log;
    private GameplayService? _service;
    public AdapterModuleManifest Manifest { get; } = new("rizline.gameplay-primitives", new(1, 0),
        new[] { GameplayCapabilityIds.Clock, GameplayCapabilityIds.Controls }, Array.Empty<string>(), Array.Empty<string>(),
        new[] { new AdapterSymbolRequirement("normal-controller", "Assembly-CSharp.dll", "", "NormalModeInLevelController"),
            new AdapterSymbolRequirement("level-information", "Assembly-CSharp.dll", "InLevel", "LevelInformation") },
        "Metadata-validated raw clocks with independent unavailable reasons and request-driven three-sample game/DSP/host interpolation. Observation is not runtime calibration or audio latency measurement. Experimental runtime_invoke controls have live readiness/pause guards; full play-flow/results remain unverified.");
    public double IntervalSeconds => 1;
    internal GameplayModule(NativeRuntime api, IMainThreadDispatcher main, Func<GameLifecycleSnapshot?> lifecycle,
        Action<RunModificationInput>? recordModification, Func<long, long, string, long, bool> matchesCurrentGameplayRun,
        INativePlaybackRateProvider? rateProvider = null, bool pumpAfterObservation = false,
        Func<GameplayObservationBoundary>? controlBoundary = null, Func<NativeRuntime.RootHandle?>? observedController = null,
        CallbackPerformanceMonitor? performance = null, ILogger? log = null, Func<bool>? playbackRequested = null)
    {
        _api = api; _main = main; _lifecycle = lifecycle; _recordModification = recordModification;
        _matchesCurrentGameplayRun = matchesCurrentGameplayRun ?? throw new ArgumentNullException(nameof(matchesCurrentGameplayRun));
        _rateProvider = rateProvider;
        _pumpAfterObservation = pumpAfterObservation;
        _playbackRequested = playbackRequested;
        _controlBoundary = controlBoundary;
        _observedController = observedController;
        _performance = performance;
        _log = log;
    }
    public ModuleProbe Probe()
    {
        _service ??= new GameplayService(new GameplayNativeBackend(_api, _main, _rateProvider, _observedController), _main, _lifecycle,
            _recordModification, matchesCurrentGameplayRun: _matchesCurrentGameplayRun, controlBoundary: _controlBoundary,
            controlSource: _rateProvider is NativePlaybackStockRateHost stock ? () => stock.PublishedObservationSource : null,
            performance: _performance, log: _log);
        return ModuleProbe.Available;
    }
    public void Register(ModuleRegistration registration)
    {
        registration.RegisterScoped<IGameplayClockService>(GameplayCapabilityIds.Clock, context => _service!.ClockForPlugin(context));
        registration.RegisterScoped<IGameplayControlService>(GameplayCapabilityIds.Controls, context => _service!.ControlsForPlugin(context));
    }
    public ModuleProbe Tick()
    {
        if (_service is null) return ModuleProbe.Unsupported("Gameplay service is released.");
        if (!_pumpAfterObservation) MaintainPlayback();
        return ModuleProbe.Available;
    }
    internal void MaintainPlayback()
    {
        _main.VerifyAccess();
        if (_rateProvider is NativePlaybackStockRateHost stock
            && (_pumpAfterObservation && _service is null || _playbackRequested?.Invoke() == false))
        {
            stock.PumpInstallationAndRetirement();
            return;
        }
        (_rateProvider as INativePlaybackRateProviderLifetime)?.PumpCompleted();
    }
    internal void ObserveControls(long hostTickSequence, double now, bool requested)
        => _service?.ObserveControlsAfterMaintenance(hostTickSequence, now, requested);
    internal void PumpControlCommand() => _service?.PumpControlCommand();
    public void Release()
    {
        _main.VerifyAccess();
        var service = _service; _service = null;
        try { service?.Dispose(); }
        finally
        {
            // The adapter owns the early physical installation independently
            // of this publication. Standalone modules still own their provider.
            if (!_pumpAfterObservation) (_rateProvider as INativePlaybackRateProviderLifetime)?.Dispose();
        }
    }
}
