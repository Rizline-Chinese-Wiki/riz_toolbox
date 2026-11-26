using RizToolbox.Adapter.Modules;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed class LocalCatalogModule : BuiltinModule
{
    private readonly Func<LocalContentService> _content;
    private readonly ILocalRunPlayDispatcher? _play;
    private LocalCatalogService? _service;
    internal LocalCatalogModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log, Func<LocalContentService> content,
        ILocalRunPlayDispatcher? play = null)
        : base(api, main, log, Definition("rizline.content.local-catalog",
            new[] { AudioContentCapabilityIds.LocalCatalog },
            Array.Empty<AdapterSymbolRequirement>(),
            "Bounded LOCAL revisions retain exact validated resources without mounting an owned UI.",
            dependencies: new[] { AudioContentCapabilityIds.LocalContent }, scenes: Array.Empty<string>())) => (_content, _play) = (content, play);
    public override ModuleProbe Probe()
    {
        if (_service is { IsDisposed: true })
        {
            if (_service.HasUnconfirmedCleanup) return ModuleProbe.Unsupported("Catalog cleanup remains unconfirmed; a process restart is required.");
            if (_service.RetainedCount != 0) return ModuleProbe.Waiting("Prepared local-song consumers still retain the previous catalog resources.");
            _service = null;
        }
        _service ??= new(Main, _content(), Log, _play);
        return ModuleProbe.Available;
    }
    internal LocalCatalogService Service => _service ?? throw new InvalidOperationException("The local catalog is not active.");
    public override void Register(ModuleRegistration registration)
        => registration.RegisterScoped<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog, context => Service.ForPlugin(context));
    public override ModuleProbe Tick() { _service!.Refresh(); return ModuleProbe.Available; }
    public override void Release() { _service?.Dispose(); if (_service?.RetainedCount == 0) _service = null; }
}

internal sealed class LocalCatalogSelectionModule : BuiltinModule
{
    private readonly LocalCatalogModule _catalog;
    private LocalCatalogService? _activeCatalog;

    internal LocalCatalogSelectionModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        LocalCatalogModule catalog, bool requiresResultUi)
        : base(api, main, log, Definition("rizline.content.local-catalog-selection",
            new[] { AudioContentCapabilityIds.LocalCatalogSelection }, Array.Empty<AdapterSymbolRequirement>(),
            "Exact LOCAL selection and RequestPlay use the formal protection host. Startup admission remains required.",
            dependencies: requiresResultUi ? new[] { AudioContentCapabilityIds.LocalCatalog, UiInputCapabilityIds.Windows }
                : new[] { AudioContentCapabilityIds.LocalCatalog }, scenes: Array.Empty<string>()))
        => _catalog = catalog;

    public override ModuleProbe Probe() => ModuleProbe.Available;
    public override void Register(ModuleRegistration registration)
    {
        var catalog = _catalog.Service;
        _activeCatalog = catalog;
        registration.RegisterScoped<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection,
            (context, isRegistrationCurrent) => catalog.SelectionForPlugin(context, isRegistrationCurrent));
    }

    public override ModuleProbe Tick() => ModuleProbe.Available;

    public override void Release()
    {
        _activeCatalog?.RetireSelectionViews();
        _activeCatalog = null;
    }
}
