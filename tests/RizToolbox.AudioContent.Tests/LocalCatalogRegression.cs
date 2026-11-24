using System.Buffers.Binary;
using System.IO.Compression;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static Assert;

internal static partial class LocalCatalogRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Local catalog publishes complete namespaced songs and immutable chart metadata", Namespaces),
        ("Local catalog cancellation and missing content publish no item or revision", Cancellation),
        ("Local catalog rejects stale resources after the same content ID is reconstructed", Reconstruction),
        ("Local catalog ownership callback resource withdrawal prevents publication", OwnershipWithdrawal),
        ("Local selection changes generation and old disposal cannot revoke the new current selection", SelectionGeneration),
        ("Local catalog rejects publication if ownership changes the catalog revision", RevisionDuringOwnership),
        ("Prepared local play requests retain exact withdrawn resources until their final release", PreparedRetention),
        ("Prepared local retry forks preserve pinned resources after provider and catalog withdrawal", PreparedRetryWithdrawal),
        ("Prepared local retry forks never resolve a reconstructed song or chart revision", PreparedRetryRevision),
        ("Prepared local request wrong-thread access preserves its ownership and retry quota", PreparedThreadAdmission),
        ("Disposed prepared local requests reject resources and retry forks", PreparedExpired),
        ("Prepared local request quota counts forks and recovers only released slots", PreparedQuota),
        ("Failed late local selection preserves the previous generation and rejects stale preparation", LateSelection),
        ("Formal plugin scope exposes local catalog services and rejects withdrawn generations", FormalScope),
        ("Local catalog owner quota includes withdrawn selections until their final release", OwnerQuota),
        ("Local catalog global quota includes withdrawn selections across provider namespaces", GlobalQuota),
        ("Local catalog module waits for retired selected consumers and then recovers with a fresh service", ModuleRetainedRecovery),
        ("Local catalog module refuses recovery after unconfirmed content cleanup without retrying it", ModuleCleanupRecovery),
        ("Withdrawing only local selection expires old scoped views while optional consumers remain active", SelectionCapabilityWithdrawal)
    };
    private static void Namespaces()
    {
        using var f = new CatalogFixture(); using var a = f.Context("a"); using var b = f.Context("b");
        var ar = f.Resources(a); var br = f.Resources(b); var av = f.Catalog.ForPlugin(a); var bv = f.Catalog.ForPlugin(b);
        var mutable = new[] { new LocalSongChartRequest("easy", "Easy", ar.Charts[0].Content) };
        var first = Need(av.RegisterSong(ar with { Charts = mutable })); Need(bv.RegisterSong(br));
        mutable[0] = new("changed", "Changed", ar.Audio);
        var snapshot = Need(av.Read()); Equal(2, snapshot.Songs.Count); Equal("easy", first.Snapshot.Charts[0].Id);
        Check(snapshot.Songs.All(x => !x.AvailableForGamePlayback));
        Equal(ErrorCode.Conflict, av.RegisterSong(ar).Error!.Code); first.Dispose(); Equal(1, Need(av.Read()).Songs.Count);
    }
    private static void Cancellation()
    {
        using var f = new CatalogFixture(); using var c = f.Context(); var request = f.Resources(c); var view = f.Catalog.ForPlugin(c);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Equal(ErrorCode.Cancelled, view.RegisterSong(request, cancel.Token).Error!.Code);
        Equal(ErrorCode.ContentInvalid, view.RegisterSong(request with { Audio = new(c.PluginId, "absent") }).Error!.Code);
        Equal(0L, Need(view.Read()).Revision); Equal(0, Need(view.Read()).Songs.Count);
        Equal(1L, Need(view.RegisterSong(request)).Snapshot.Revision);
    }
    private static void Reconstruction()
    {
        using var f = new CatalogFixture(); using var c = f.Context(); var request = f.Resources(c); var view = f.Catalog.ForPlugin(c);
        Need(view.RegisterSong(request)); var selected = Need(f.Catalog.SelectionForPlugin(c).Select(new(c.PluginId, request.Id), "easy"));
        f.LastResources!.Dispose(); f.Resources(c); // same IDs, different validated entry objects and revision
        Equal(0, Need(view.Read()).Songs.Count); Check(!selected.IsCurrent);
        Equal(ErrorCode.TargetExpired, selected.RequestPlay().Error!.Code);
        Equal(ErrorCode.TargetMissing, f.Catalog.SelectionForPlugin(c).Select(new(c.PluginId, request.Id), "easy").Error!.Code);
    }
    private static void OwnershipWithdrawal()
    {
        using var f = new CatalogFixture(); using var c = f.Context(); var request = f.Resources(c); var view = f.Catalog.ForPlugin(c);
        c.OnOwn = item => { if (item is ILocalSongRegistration) f.LastResources!.Dispose(); };
        Equal(ErrorCode.ContentInvalid, view.RegisterSong(request).Error!.Code);
        Equal(0, Need(view.Read()).Songs.Count); Equal(0L, Need(view.Read()).Revision);
        c.OnOwn = null; f.Resources(c); Equal(1L, Need(view.RegisterSong(request)).Snapshot.Revision);
    }
    private static void SelectionGeneration()
    {
        using var f = new CatalogFixture(); using var c = f.Context(); var request = f.Resources(c); var provider = f.Catalog.ForPlugin(c);
        Need(provider.RegisterSong(request)); Need(provider.RegisterSong(request with { Id = "second" }));
        var selector = f.Catalog.SelectionForPlugin(c); var first = Need(selector.Select(new(c.PluginId, request.Id), "easy"));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Equal(ErrorCode.Cancelled, selector.Select(new(c.PluginId, "second"), "easy", cancel.Token).Error!.Code); Check(first.IsCurrent);
        var second = Need(selector.Select(new(c.PluginId, "second"), "easy")); Equal(2L, second.Snapshot.Generation);
        Check(!first.IsCurrent && second.IsCurrent); first.Dispose(); Check(second.IsCurrent);
        Equal("second", Need(selector.ReadCurrent())!.Song.Song.Id);
        Equal(ErrorCode.OperationUnsupported, second.RequestPlay().Error!.Code);
    }
    private static void RevisionDuringOwnership()
    {
        using var f = new CatalogFixture(); using var c = f.Context(); var request = f.Resources(c); var provider = f.Catalog.ForPlugin(c);
        var first = Need(provider.RegisterSong(request));
        c.OnOwn = item => { if (item is ILocalSongRegistration) first.Dispose(); };
        var result = provider.RegisterSong(request with { Id = "second" });
        Check(!result.Success); Equal(ErrorCode.Conflict, result.Error!.Code);
        c.OnOwn = null;
        Equal(2L, Need(provider.Read()).Revision); Equal(0, Need(provider.Read()).Songs.Count);
        Equal(3L, Need(provider.RegisterSong(request with { Id = "second" })).Snapshot.Revision);
    }
    private static void PreparedRetention()
    {
        using var f = new CatalogFixture(); using var providerContext = f.Context(); using var consumer = f.Context("consumer");
        var request = f.Resources(providerContext); var registration = Need(f.Catalog.ForPlugin(providerContext).RegisterSong(request));
        var selected = Need(f.Catalog.SelectionForPlugin(consumer).Select(registration.Snapshot.Song, "easy"));
        var prepared = Need(f.Catalog.Prepare(selected)); var payload = f.Parsed;
        Check(ReferenceEquals(payload, prepared.Resources.Single(x => x.Snapshot.Kind == LocalContentKind.Chart).Payload));
        f.LastResources!.Dispose(); registration.Dispose(); selected.Dispose();
        Check(!payload.Disposed); Equal(1, f.Catalog.RetainedCount);
        Task.Run(() => Throws<InvalidOperationException>(() => prepared.Dispose())).GetAwaiter().GetResult();
        Check(!payload.Disposed); prepared.Dispose(); prepared.Dispose(); Check(payload.Disposed); Equal(0, f.Catalog.RetainedCount);
    }
    private static void PreparedRetryWithdrawal()
    {
        using var f = new CatalogFixture(); using var provider = f.Context(); using var consumer = f.Context("consumer");
        var request = f.Resources(provider); var registration = Need(f.Catalog.ForPlugin(provider).RegisterSong(request));
        var selected = Need(f.Catalog.SelectionForPlugin(consumer).Select(registration.Snapshot.Song, "easy"));
        var prepared = Need(f.Catalog.Prepare(selected)); var payload = f.Parsed;
        provider.Dispose(); selected.Dispose(); f.Catalog.Dispose(); f.Content.Dispose();
        var retry = prepared.Retain(); prepared.Dispose();
        Check(!payload.Disposed); Check(ReferenceEquals(payload, retry.Resources.Single(x => x.Snapshot.Kind == LocalContentKind.Chart).Payload));
        Equal(1, f.Catalog.PreparedRequestCount); Equal(prepared.Selection, retry.Selection);
        var next = retry.Retain(); retry.Dispose(); Check(!payload.Disposed); next.Dispose();
        Check(payload.Disposed); Equal(0, f.Catalog.PreparedRequestCount); Equal(0, f.Catalog.RetainedCount);
    }
    private static void PreparedRetryRevision()
    {
        using var f = new CatalogFixture(); using var provider = f.Context(); using var consumer = f.Context("consumer");
        var request = f.Resources(provider); var view = f.Catalog.ForPlugin(provider);
        var first = Need(view.RegisterSong(request));
        var selected = Need(f.Catalog.SelectionForPlugin(consumer).Select(first.Snapshot.Song, "easy"));
        var prepared = Need(f.Catalog.Prepare(selected)); var oldPayload = f.Parsed;
        f.LastResources!.Dispose(); first.Dispose(); selected.Dispose(); f.Resources(provider);
        var second = Need(view.RegisterSong(request with { Title = "Reconstructed" })); var newPayload = f.Parsed;
        using var retry = prepared.Retain(); prepared.Dispose();
        Equal(first.Snapshot.Revision, retry.Selection.Song.Revision); Check(second.Snapshot.Revision != retry.Selection.Song.Revision);
        Equal("Same title", retry.Selection.Song.Title);
        Check(ReferenceEquals(oldPayload, retry.Resources.Single(x => x.Snapshot.Kind == LocalContentKind.Chart).Payload));
        Check(!ReferenceEquals(oldPayload, newPayload) && !oldPayload.Disposed && !newPayload.Disposed);
        retry.Dispose(); Check(oldPayload.Disposed && !newPayload.Disposed);
    }
    private static void PreparedThreadAdmission()
    {
        using var f = new CatalogFixture(); using var c = f.Context(); var request = f.Resources(c);
        var registered = Need(f.Catalog.ForPlugin(c).RegisterSong(request));
        using var selected = Need(f.Catalog.SelectionForPlugin(c).Select(registered.Snapshot.Song, "easy"));
        using var prepared = Need(f.Catalog.Prepare(selected));
        Task.Run(() => {
            Throws<InvalidOperationException>(() => prepared.Retain());
            Throws<InvalidOperationException>(() => { _ = prepared.Resources; });
            Throws<InvalidOperationException>(() => prepared.Dispose());
        }).GetAwaiter().GetResult();
        Equal(1, f.Catalog.PreparedRequestCount); Check(!prepared.IsDisposed);
        using var retry = prepared.Retain(); Equal(2, f.Catalog.PreparedRequestCount); Check(retry.Resources.Count == 3);
    }
    private static void PreparedExpired()
    {
        using var f = new CatalogFixture(); using var c = f.Context(); var request = f.Resources(c);
        var registered = Need(f.Catalog.ForPlugin(c).RegisterSong(request));
        using var selected = Need(f.Catalog.SelectionForPlugin(c).Select(registered.Snapshot.Song, "easy"));
        var prepared = Need(f.Catalog.Prepare(selected)); prepared.Dispose(); prepared.Dispose();
        Check(prepared.IsDisposed); Equal(0, f.Catalog.PreparedRequestCount);
        Throws<ObjectDisposedException>(() => prepared.Retain());
        Throws<ObjectDisposedException>(() => { _ = prepared.Resources; });
    }
    private static void PreparedQuota()
    {
        using var f = new CatalogFixture(); using var c = f.Context(); var request = f.Resources(c);
        var registered = Need(f.Catalog.ForPlugin(c).RegisterSong(request));
        using var selected = Need(f.Catalog.SelectionForPlugin(c).Select(registered.Snapshot.Song, "easy"));
        var requests = new List<LocalPlayRequest> { Need(f.Catalog.Prepare(selected)) };
        try
        {
            for (var i = 1; i < LocalCatalogService.MaximumPreparedRequests; i++) requests.Add(requests[0].Retain());
            Equal(LocalCatalogService.MaximumPreparedRequests, f.Catalog.PreparedRequestCount);
            Equal(ErrorCode.QuotaExceeded, f.Catalog.Prepare(selected).Error!.Code);
            Throws<InvalidOperationException>(() => requests[0].Retain());
            Equal(LocalCatalogService.MaximumPreparedRequests, f.Catalog.PreparedRequestCount);
            requests[1].Dispose(); requests[1].Dispose(); Equal(LocalCatalogService.MaximumPreparedRequests - 1, f.Catalog.PreparedRequestCount);
            requests.Add(requests[0].Retain()); Equal(LocalCatalogService.MaximumPreparedRequests, f.Catalog.PreparedRequestCount);
            Check(!requests[0].IsDisposed);
        }
        finally { foreach (var value in requests) value.Dispose(); }
        Equal(0, f.Catalog.PreparedRequestCount);
    }
    private static void LateSelection()
    {
        using var f = new CatalogFixture(); using var c = f.Context(); var request = f.Resources(c); var provider = f.Catalog.ForPlugin(c);
        var a = Need(provider.RegisterSong(request)); var b = Need(provider.RegisterSong(request with { Id = "second" }));
        var view = f.Catalog.SelectionForPlugin(c); var first = Need(view.Select(a.Snapshot.Song, "easy"));
        c.OnOwn = item => { if (item is ILocalCatalogSelection) b.Dispose(); };
        Equal(ErrorCode.TargetExpired, view.Select(b.Snapshot.Song, "easy").Error!.Code); c.OnOwn = null;
        Check(first.IsCurrent); Equal(1L, Need(view.ReadCurrent())!.Generation);
        Need(provider.RegisterSong(request with { Id = "second" }));
        var second = Need(view.Select(b.Snapshot.Song, "easy")); Equal(2L, second.Snapshot.Generation);
        Equal(ErrorCode.TargetExpired, f.Catalog.Prepare(first).Error!.Code);
        Equal(ErrorCode.WrongThread, Task.Run(() => second.RequestPlay().Error!.Code).Result);
    }
    private static void FormalScope()
    {
        using var f = new CatalogFixture(); using var resourcesContext = f.Context(); var request = f.Resources(resourcesContext);
        Need(f.Catalog.ForPlugin(resourcesContext).RegisterSong(request));
        var capabilities = new CapabilityRegistry(new("test", "test", "test", "test")); var log = new Logger(); var events = new EventHub(log);
        capabilities.RegisterScoped<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog, f.Catalog.ForPlugin);
        capabilities.RegisterScoped<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection, f.Catalog.SelectionForPlugin);
        using var host = new PluginHost(capabilities, f.Main, events, log); IPluginContext context = null!;
        host.Register(new CatalogPlugin(x => context = x)); Check(host.Enable("catalog.consumer").Success);
        var catalog = context.GetService<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog).Service!;
        var selector = context.GetService<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection).Service!;
        var selection = Need(selector.Select(new(resourcesContext.PluginId, request.Id), "easy")); Check(selection.IsCurrent);
        Check(ReferenceEquals(selector, context.GetService<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection).Service));
        capabilities.SetUnavailable(AudioContentCapabilityIds.LocalCatalog, "withdrawn");
        Equal(ErrorCode.ScopeExpired, catalog.Read().Error!.Code);
        Check(host.Disable("catalog.consumer").Success); Check(!selection.IsCurrent);
        Equal(ErrorCode.ScopeExpired, selector.ReadCurrent().Error!.Code);
        Equal(0, Need(f.Catalog.ForPlugin(resourcesContext).Read()).Songs.Count(x => x.Song.Namespace == "catalog.consumer"));
    }
    private static void OwnerQuota()
    {
        using var f = new CatalogFixture(); using var providerContext = f.Context(); using var consumer = f.Context("consumer");
        var request = f.Resources(providerContext); var provider = f.Catalog.ForPlugin(providerContext);
        var registrations = Enumerable.Range(0, 32).Select(i => Need(provider.RegisterSong(request with { Id = "song" + i }))).ToArray();
        Equal(32, Need(provider.Read()).Songs.Count);
        var selected = Need(f.Catalog.SelectionForPlugin(consumer).Select(registrations[0].Snapshot.Song, "easy"));
        registrations[0].Dispose(); Check(!selected.IsCurrent); Equal(31, Need(provider.Read()).Songs.Count);
        Equal(ErrorCode.QuotaExceeded, provider.RegisterSong(request with { Id = "overflow" }).Error!.Code);
        // A withdrawn item still owns its exact resources until the selected consumer releases it.
        selected.Dispose(); Need(provider.RegisterSong(request with { Id = "overflow" }));
        Equal(32, Need(provider.Read()).Songs.Count);
        Equal(ErrorCode.QuotaExceeded, provider.RegisterSong(request with { Id = "overflow-again" }).Error!.Code);
    }
    private static void GlobalQuota()
    {
        using var f = new CatalogFixture(); using var a = f.Context("a"); using var b = f.Context("b");
        using var c = f.Context("c"); using var d = f.Context("d"); using var extra = f.Context("extra");
        using var consumer = f.Context("consumer");
        ILocalSongRegistration? held = null;
        foreach (var context in new[] { a, b, c, d })
        {
            var request = f.Resources(context); var provider = f.Catalog.ForPlugin(context);
            for (var i = 0; i < 32; i++)
            {
                var registration = Need(provider.RegisterSong(request with { Id = "song" + i }));
                held ??= registration;
            }
        }
        var extraRequest = f.Resources(extra); var extraProvider = f.Catalog.ForPlugin(extra);
        Equal(128, Need(extraProvider.Read()).Songs.Count);
        var selected = Need(f.Catalog.SelectionForPlugin(consumer).Select(held!.Snapshot.Song, "easy"));
        held.Dispose(); Equal(127, Need(extraProvider.Read()).Songs.Count);
        Equal(ErrorCode.QuotaExceeded, extraProvider.RegisterSong(extraRequest).Error!.Code);
        selected.Dispose(); var replacement = Need(extraProvider.RegisterSong(extraRequest));
        Equal("extra", replacement.Snapshot.Song.Namespace); Equal(128, Need(extraProvider.Read()).Songs.Count);
        Equal(ErrorCode.QuotaExceeded, extraProvider.RegisterSong(extraRequest with { Id = "second" }).Error!.Code);
    }
    private static void ModuleRetainedRecovery()
    {
        using var f = new CatalogFixture(); using var providerContext = f.Context(); using var consumer = f.Context("consumer");
        var module = new LocalCatalogModule(null!, f.Main, new Logger(), () => f.Content);
        var capabilities = new CapabilityRegistry(new("test", "test", "test", "test"));
        Check(module.Probe().Ready); var registration = RegisterModule(module, capabilities);
        var selectionModule = new LocalCatalogSelectionModule(null!, f.Main, new Logger(), module, requiresResultUi: false);
        var selectionRegistration = new ModuleRegistration(capabilities, selectionModule.Manifest);
        selectionModule.Register(selectionRegistration); selectionRegistration.Complete();
        try
        {
            var request = f.Resources(providerContext);
            var provider = ModuleService<ILocalCatalogProviderService>(capabilities, AudioContentCapabilityIds.LocalCatalog, providerContext);
            var selector = ModuleService<ILocalCatalogSelectionService>(capabilities, AudioContentCapabilityIds.LocalCatalogSelection, consumer);
            var song = Need(provider.RegisterSong(request));
            // The SDK permits already selected consumers to retain exact withdrawn resources.
            // Leave the superseded handle alive while Release disposes the view's current handle.
            var retained = Need(selector.Select(song.Snapshot.Song, "easy"));
            var current = Need(selector.Select(song.Snapshot.Song, "easy")); var payload = f.Parsed;
            f.LastResources!.Dispose(); selectionRegistration.Withdraw("selection retirement"); registration.Withdraw("catalog test retirement"); module.Release();
            Check(!current.IsCurrent && !retained.IsCurrent && !payload.Disposed);
            Equal(ErrorCode.ScopeExpired, provider.Read().Error!.Code);
            var waiting = module.Probe(); Check(!waiting.Ready && !waiting.Permanent);
            retained.Dispose(); Check(payload.Disposed);
            Check(module.Probe().Ready); registration = RegisterModule(module, capabilities);
            var recovered = ModuleService<ILocalCatalogProviderService>(capabilities, AudioContentCapabilityIds.LocalCatalog, providerContext);
            Equal(0, Need(recovered.Read()).Songs.Count);
            Equal(ErrorCode.ScopeExpired, provider.Read().Error!.Code);
            var fresh = f.Resources(providerContext);
            Equal(1L, Need(recovered.RegisterSong(fresh)).Snapshot.Revision);
        }
        finally { selectionRegistration.Withdraw("selection test finished"); registration.Withdraw("catalog test finished"); module.Release(); }
    }
    private static void ModuleCleanupRecovery()
    {
        var parser = new FailingCleanupParser(); using var f = new CatalogFixture(parser); using var context = f.Context();
        var module = new LocalCatalogModule(null!, f.Main, new Logger(), () => f.Content);
        var capabilities = new CapabilityRegistry(new("test", "test", "test", "test"));
        Check(module.Probe().Ready); var registration = RegisterModule(module, capabilities);
        try
        {
            var request = f.Resources(context);
            var provider = ModuleService<ILocalCatalogProviderService>(capabilities, AudioContentCapabilityIds.LocalCatalog, context);
            Need(provider.RegisterSong(request)); var payload = parser.Created.Single();
            f.LastResources!.Dispose(); Equal(0, payload.Attempts);
            registration.Withdraw("catalog cleanup test retirement"); module.Release();
            Equal(1, payload.Attempts); Check(f.Content.HasUnconfirmedCleanup);
            Equal(ErrorCode.ScopeExpired, provider.Read().Error!.Code);
            var rejected = module.Probe(); Check(!rejected.Ready && rejected.Permanent);
            module.Release(); rejected = module.Probe();
            Check(!rejected.Ready && rejected.Permanent); Equal(1, payload.Attempts);
        }
        finally { registration.Withdraw("catalog cleanup test finished"); module.Release(); }
    }
    private static void SelectionCapabilityWithdrawal()
    {
        using var f = new CatalogFixture(); using var resourcesContext = f.Context(); var request = f.Resources(resourcesContext);
        var song = Need(f.Catalog.ForPlugin(resourcesContext).RegisterSong(request));
        var capabilities = new CapabilityRegistry(new("test", "test", "test", "test")); var log = new Logger(); var events = new EventHub(log);
        capabilities.RegisterScoped<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog, f.Catalog.ForPlugin);
        capabilities.RegisterScoped<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection, f.Catalog.SelectionForPlugin);
        using var host = new PluginHost(capabilities, f.Main, events, log); IPluginContext context = null!;
        // This plugin has no RequiredCapabilities, so withdrawing its optional selection service must not disable its scope.
        host.Register(new CatalogPlugin(x => context = x)); Check(host.Enable("catalog.consumer").Success);
        var lifetime = ((IScopedPluginContext)context).Lifetime;
        var catalog = context.GetService<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog).Service!;
        var oldView = context.GetService<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection).Service!;
        var oldSelection = Need(oldView.Select(song.Snapshot.Song, "easy"));
        capabilities.SetUnavailable(AudioContentCapabilityIds.LocalCatalogSelection, "selection provider withdrawn");
        Check(!lifetime.IsCancellationRequested); Check(capabilities.GetCapability(AudioContentCapabilityIds.LocalCatalog).Available);
        Equal(1, Need(catalog.Read()).Songs.Count);
        Equal(ErrorCode.ScopeExpired, oldView.ReadCurrent().Error!.Code);
        Equal(ErrorCode.ScopeExpired, oldView.Select(song.Snapshot.Song, "easy").Error!.Code);
        Check(!context.GetService<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection).Available);
        capabilities.RegisterScoped<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection, f.Catalog.SelectionForPlugin);
        var newView = context.GetService<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection).Service!;
        Check(!ReferenceEquals(oldView, newView)); Check(Need(newView.ReadCurrent()) is null);
        var newSelection = Need(newView.Select(song.Snapshot.Song, "easy")); Equal(1L, newSelection.Snapshot.Generation);
        oldSelection.Dispose(); Check(newSelection.IsCurrent); Equal(ErrorCode.ScopeExpired, oldView.ReadCurrent().Error!.Code);
        Equal(ErrorCode.OperationUnsupported, newSelection.RequestPlay().Error!.Code);
        Check(host.Disable("catalog.consumer").Success); Check(lifetime.IsCancellationRequested && !newSelection.IsCurrent);
        Equal(ErrorCode.ScopeExpired, newView.ReadCurrent().Error!.Code);
    }
    private static ModuleRegistration RegisterModule(LocalCatalogModule module, CapabilityRegistry capabilities)
    {
        var registration = new ModuleRegistration(capabilities, module.Manifest);
        module.Register(registration); registration.Complete(); return registration;
    }
    private static T ModuleService<T>(CapabilityRegistry capabilities, string id, IPluginContext context) where T : class
    {
        var logger = new Logger();
        var host = context.Own(new PluginHost(capabilities, (MainThreadDispatcher)context.MainThread, new EventHub(logger), logger));
        IPluginContext scoped = null!;
        host.Register(new CatalogPlugin(x => scoped = x, context.PluginId)); Check(host.Enable(context.PluginId).Success);
        var lookup = scoped.GetService<T>(id); Check(lookup.Available && lookup.Service is not null); return lookup.Service!;
    }
    private sealed class FailingCleanupParser : ILocalChartParser
    {
        internal readonly List<FailingCleanupChart> Created = new();
        public IParsedLocalChart Parse(byte[] bytes, CancellationToken token)
        {
            var chart = new FailingCleanupChart(ChartJsonValidator.Validate(bytes, token)); Created.Add(chart); return chart;
        }
    }
    private sealed class FailingCleanupChart : IParsedLocalChart
    {
        public ValidatedChartJson Source { get; } internal int Attempts;
        internal FailingCleanupChart(ValidatedChartJson source) => Source = source;
        public void Dispose() { Attempts++; throw new IOException("catalog test cleanup failure"); }
    }
    private sealed class CatalogPlugin : IRizPlugin
    {
        private readonly Action<IPluginContext> _enable; private readonly string _id;
        internal CatalogPlugin(Action<IPluginContext> enable, string id = "catalog.consumer") { _enable = enable; _id = id; }
        public PluginManifest Manifest => new(_id, new(1, 0), new(1, 0));
        public void Enable(IPluginContext context) => _enable(context); public void Disable() { }
    }
    private sealed class CatalogFixture : IDisposable
    {
        private readonly ChartFixture _f = new();
        internal LocalCatalogService Catalog { get; }
        internal LocalContentService Content => _f.Content;
        internal MainThreadDispatcher Main => _f.Main;
        internal FakeParsedChart Parsed => _f.Parser.Created.Last();
        internal ILocalContentBatchRegistration? LastResources;
        internal CatalogFixture(ILocalChartParser? parser = null, ILocalRunPlayDispatcher? play = null, string? chartJson = null)
        {
            File.WriteAllBytes(Path.Combine(_f.Directory, "tone.wav"), Wav());
            File.WriteAllBytes(Path.Combine(_f.Directory, "cover.png"), Png());
            if (chartJson is not null) File.WriteAllText(Path.Combine(_f.Directory, "chart.json"), chartJson);
            if (parser is not null) _f.Content.SetChartParser(parser);
            Catalog = new(_f.Main, _f.Content, new Logger(), play);
        }
        internal Context Context(string id = "local.test") => _f.Context(id);
        internal LocalSongRequest Resources(Context context)
        {
            LastResources = Need(_f.Content.BatchForPlugin(context).RegisterBatch(new[] {
                new LocalContentRequest("music", LocalContentKind.Audio, "wav.pcm16", 1, new(ContentFileArea.Package, "tone.wav")),
                new LocalContentRequest("cover", LocalContentKind.Cover, "png.rgba8", 1, new(ContentFileArea.Package, "cover.png")),
                new LocalContentRequest("chart", LocalContentKind.Chart, "rizline.chart.json", 0, new(ContentFileArea.Package, "chart.json")) }));
            return new("song", "Same title", "Artist", new(context.PluginId, "music"), new(context.PluginId, "cover"),
                new[] { new LocalSongChartRequest("easy", "Easy", new(context.PluginId, "chart")) });
        }
        public void Dispose()
        {
            Catalog.Dispose(); File.Delete(Path.Combine(_f.Directory, "tone.wav")); File.Delete(Path.Combine(_f.Directory, "cover.png")); _f.Dispose();
        }
        private static byte[] Png()
        {
            using var output = new MemoryStream(); output.Write(new byte[] { 137,80,78,71,13,10,26,10 });
            var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, 1); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1); header[8] = 8; header[9] = 6;
            Chunk(output, "IHDR", header); using var data = new MemoryStream(); using (var z = new ZLibStream(data, CompressionLevel.Fastest, true)) z.Write(new byte[] {0,255,255,255,255});
            Chunk(output, "IDAT", data.ToArray()); Chunk(output, "IEND", Array.Empty<byte>()); return output.ToArray();
        }
        private static void Chunk(Stream output, string kind, byte[] data)
        {
            var length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, data.Length); output.Write(length);
            var bytes = System.Text.Encoding.ASCII.GetBytes(kind).Concat(data).ToArray(); output.Write(bytes); var crc = uint.MaxValue;
            foreach (var value in bytes) { crc ^= value; for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
            BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); output.Write(length);
        }
    }
}
