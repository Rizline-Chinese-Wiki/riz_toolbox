using RizToolbox.Adapter.Modules;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Settings;

/// <summary>Read operation is independent of whether the settings UI and override operation are ready.</summary>
internal sealed class SettingsModule : IAdapterModule
{
    private readonly Func<IGameSettingsBackend> _backend;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    internal GameSettingsService? Service { get; private set; }
    public double IntervalSeconds => 1;
    public AdapterModuleManifest Manifest { get; } = new("rizline.settings.read", new(1, 0),
        Array.AsReadOnly(new[] { GameSettingsCapabilityIds.Read, GameSettingsCapabilityIds.Changes }), Array.Empty<string>(), new[] { "*" },
        Array.AsReadOnly(new[] { new AdapterSymbolRequirement("settings.control", "Assembly-CSharp.dll", "", "SettingsControl") }),
        "Verified static SettingsControl float getters; settings UI is not required for reads. Individual unsupported getters are explicitly described.");

    internal SettingsModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log)
        : this(() => new NativeGameSettingsBackend(api, main), main, log) { }
    internal SettingsModule(Func<IGameSettingsBackend> backend, IMainThreadDispatcher main, ILogger log)
        => (_backend, _main, _log) = (backend, main, log);
    public ModuleProbe Probe()
    {
        Service ??= new(_main, _log, _backend());
        if (Service.ReadableCount == 0) return ModuleProbe.Unsupported("No verified SettingsControl float getters are supported.");
        var refreshed = Service.Refresh();
        return refreshed.Success ? ModuleProbe.Available : refreshed.Error!.Code is ErrorCode.OperationUnsupported or ErrorCode.UnsupportedSignature
            ? ModuleProbe.Unsupported(refreshed.Error.Message) : ModuleProbe.Waiting(refreshed.Error.Message);
    }
    public void Register(ModuleRegistration registration)
    {
        registration.RegisterScoped<IGameSettingsReadService>(GameSettingsCapabilityIds.Read, context => Service!.ReadForPlugin(context));
        registration.RegisterScoped<IGameSettingsChangeService>(GameSettingsCapabilityIds.Changes, context => Service!.ChangesForPlugin(context));
    }
    public ModuleProbe Tick() => Probe();
    public void Release() { var service = Service; Service = null; service?.Dispose(); }
    internal Result<IReadOnlyList<GameSettingSnapshot>> CaptureDataSettings()
        => Service is null ? Result<IReadOnlyList<GameSettingSnapshot>>.Fail(new(ErrorCode.NotReady, "Settings module is not ready.")) : Service.CaptureDataSettings();
}

/// <summary>A separate capability whose bounded probe waits until actual game sliders provide supported ranges.</summary>
internal sealed class SettingsOverrideModule : IAdapterModule
{
    private readonly SettingsModule _read;
    public double IntervalSeconds => 1;
    public AdapterModuleManifest Manifest { get; } = new("rizline.settings.override", new(1, 0),
        Array.AsReadOnly(new[] { GameSettingsCapabilityIds.Override }), Array.AsReadOnly(new[] { GameSettingsCapabilityIds.Read }),
        Array.Empty<string>(), Array.Empty<AdapterSymbolRequirement>(),
        "Only verified game-owned float setters with actual live settings Slider min/max; not arbitrary saves/gameplay settings. Leases use PriorityOverride/AdoptAndReapply.");
    internal SettingsOverrideModule(SettingsModule read) => _read = read;
    public ModuleProbe Probe()
    {
        if (_read.Service is null || !_read.Service.Ready) return ModuleProbe.Waiting("Settings read module is not ready.");
        var ready = _read.Service.RefreshRanges();
        return ready.Success && _read.Service.OverrideReady ? ModuleProbe.Available : ready.Error?.Code is ErrorCode.OperationUnsupported or ErrorCode.UnsupportedSignature
            ? ModuleProbe.Unsupported(ready.Error.Message) : ModuleProbe.Waiting(ready.Error?.Message ?? "No actual setting slider range is ready.");
    }
    public void Register(ModuleRegistration registration)
        => registration.RegisterScoped<IGameSettingsOverrideService>(GameSettingsCapabilityIds.Override, context => _read.Service!.OverrideForPlugin(context));
    public ModuleProbe Tick() => Probe();
    // The read module owns the shared property channels. Capability withdrawal stops scoped holders before this release.
    public void Release() { }
}
