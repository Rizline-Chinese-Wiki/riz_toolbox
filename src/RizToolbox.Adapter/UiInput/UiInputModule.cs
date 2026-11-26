using RizToolbox.Adapter.Modules;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.UiInput;

/// <summary>Frame sampling is subscribed once; ModuleManager's bounded probe clock is not used as an input frame clock.</summary>
internal sealed class UiInputModule : IAdapterModule
{
    private readonly Func<IUiInputBackend> _backend;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly IEventBus _events;
    private UiInputService? _service;
    private UiInputService.View? _hostUi;
    private HostUiContext? _hostContext;
    private IDisposable? _frame;
    private Exception? _cleanupFailure;
    public double IntervalSeconds => 1;
    public AdapterModuleManifest Manifest { get; } = new("rizline.ui-input", new(0, 1),
        Array.AsReadOnly(new[] { UiInputCapabilityIds.Windows, UiInputCapabilityIds.Actions, UiInputCapabilityIds.Navigation, UiInputCapabilityIds.OfficialChartSelection }),
        Array.Empty<string>(), new[] { "*" }, NativeUiInputBackend.EssentialSymbols,
        "Experimental owned UGUI and read-only Input System Keyboard/Mouse/Gamepad digital edge capture; exact metadata checked. Analog axes, modifiers, game input suppression and arbitrary page navigation are unsupported. Hardware acceptance is separate from probing.", AdapterVerificationTier.Declared);
    internal UiInputModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log, IEventBus events,
        bool developerDiagnostics = false, AdapterBuildProfile? profile = null)
        : this(() => new NativeUiInputBackend(api, main, log, developerDiagnostics, profile), main, log, events) { }
    internal UiInputModule(Func<IUiInputBackend> backend, IMainThreadDispatcher main, ILogger log, IEventBus events)
        => (_backend, _main, _log, _events) = (backend, main, log, events);
    // One internal owner on the same service, focus stack and native backend as
    // plugin windows. It creates neither a second input collector nor a plugin.
    internal IOwnedUiService GetHostUi()
    {
        _main.VerifyAccess();
        if (_service is null || _service.IsDisposed) throw new InvalidOperationException("The shared owned UI module is not active.");
        _hostContext ??= new(this);
        return _hostUi ??= _service.ForPlugin(_hostContext);
    }
    public ModuleProbe Probe()
    {
        if (_cleanupFailure is not null) return ModuleProbe.Unsupported("Previous UI cleanup is unconfirmed; process restart is required.");
        try { _service ??= new(_main, _log, _backend()); _service.Tick(); return ModuleProbe.Available; }
        catch (NotSupportedException ex) { return ModuleProbe.Unsupported(ex.Message); }
        catch (Exception ex) { return ModuleProbe.Waiting(ex.Message); }
    }
    public void Register(ModuleRegistration registration)
    {
        registration.RegisterScoped<IOwnedUiService>(UiInputCapabilityIds.Windows, context => _service!.ForPlugin(context));
        registration.RegisterScoped<IInputActionService>(UiInputCapabilityIds.Actions, context => _service!.ForPlugin(context));
        registration.RegisterScoped<IGameNavigationService>(UiInputCapabilityIds.Navigation, context => _service!.ForPlugin(context));
        registration.RegisterScoped<IOfficialChartSelectionService>(UiInputCapabilityIds.OfficialChartSelection, context => _service!.ForPlugin(context));
        _frame = _events.Subscribe<FrameUpdate>(_ =>
        {
            try { _service?.Tick(); }
            catch (Exception ex) { try { _log.Log(LogLevel.Warning, "Owned UI/input frame failed; retiring all owned UI before retry.", ex); } catch { } finally { Release(); } }
        });
    }
    public ModuleProbe Tick() => _service is { IsDisposed: false } ? ModuleProbe.Available
        : ModuleProbe.Waiting("Owned UI/input was retired after a frame failure; module recovery required.");
    public void Release()
    {
        if (_cleanupFailure is not null) throw new InvalidOperationException("Previous UI cleanup is unconfirmed.", _cleanupFailure);
        _frame?.Dispose(); _frame = null; _hostUi = null;
        try { _service?.Dispose(); _service = null; }
        catch (Exception error) { _cleanupFailure = error; throw; }
    }
    private sealed class HostUiContext : IPluginContext
    {
        private readonly UiInputModule _owner;
        internal HostUiContext(UiInputModule owner) => _owner = owner;
        public string PluginId => "framework.local-result";
        public ILogger Log => _owner._log;
        public IMainThreadDispatcher MainThread => _owner._main;
        public IEventBus Events => _owner._events;
        public GameBuildDescriptor GameBuild => throw new NotSupportedException("Internal UI ownership is not a plugin build query.");
        public CapabilityDescriptor GetCapability(string id) => new(id,
            id == UiInputCapabilityIds.Windows && _owner._service is not null);
        public ServiceLookup<T> GetService<T>(string id) where T : class => new(false, null, new(ErrorCode.CapabilityUnavailable,"Internal UI context does not resolve plugin services.",id));
        public T Own<T>(T resource) where T : IDisposable
        {
            MainThread.VerifyAccess();
            if (resource is not UiInputService.View || _owner._service is null)
                throw new InvalidOperationException("This internal context owns only its module-retained shared UI view.");
            return resource; // The module field and live service windows retain it.
        }
    }
}
