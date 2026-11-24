using RizToolbox.Adapter;
using RizToolbox.Adapter.Modules;
using RizToolbox.Adapter.Settings;
using RizToolbox.Core;
using RizToolbox.Sdk;

internal static class SettingsChangeRegression
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("optional settings changes capability preserves the old read interface and declares sampled semantics", OptionalContract);
        yield return ("actual module ticks publish initial snapshot and one complete revision for multiple changes", SampledRevisions);
        yield return ("settings snapshots and change lists remain copied and immutable", ImmutableSnapshots);
        yield return ("subscribe during delivery receives the current revision once without a gap", SubscribeDuringDelivery);
        yield return ("scope Own reentry cannot refresh before initial or lose nested subscriptions", OwnReentry);
        yield return ("scope Own self-stop cannot return a successful dead subscription", OwnSelfStop);
        yield return ("scope Own disposal cannot return a successful disposed subscription", OwnDisposesSubscription);
        yield return ("failed later getter publishes no partial snapshot or revision", AtomicCaptureFailure);
        yield return ("settings range availability changes publish descriptors without inventing value changes", DescriptorChanges);
        yield return ("settings callback failure faults its real plugin and keeps other subscribers running", CallbackFault);
        yield return ("initial callback failure is attributed to the actual subscribing activation", InitialFault);
        yield return ("disabling another plugin during delivery suppresses its queued settings callback", DisableDuringDelivery);
        yield return ("withdraw and re-register same provider cannot revive old settings subscriptions", CapabilityGeneration);
        yield return ("reentrant refresh cannot replace the subject being delivered", ReentrantRefresh);
        yield return ("module disposal in callback prevents all remaining callbacks and expires snapshots", DisposeDuringDelivery);
        yield return ("wrong-thread subscriptions and disposal cannot change the active subscription", WrongThread);
        yield return ("ordinary SDK event publication cannot forge a settings notification", PublicEventIsNotAuthority);
        yield return ("real override and restoration are observed through module ticks", OverrideNotifications);
        yield return ("an adjacent external volume becomes the latest baseline on a module tick", () => AdjacentExternalBaseline(sampled: true));
        yield return ("an adjacent external volume is preserved when a plugin stops before the next tick", () => AdjacentExternalBaseline(sampled: false));
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static float Value(GameSettingsSnapshot snapshot, string id) => snapshot.Values.Single(value => value.Descriptor.Id == id).Value;
    private static IDisposable Subscribe(IGameSettingsChangeService service, Action<GameSettingsChanged> handler)
    {
        var subscribed = service.Subscribe(handler);
        Check(subscribed.Success && subscribed.Value is not null, subscribed.Error?.Message ?? "Subscription failed.");
        return subscribed.Value!;
    }

    private static void OptionalContract()
    {
        // Compiles a legacy implementation with its original three members; the new API is a separate interface.
        IGameSettingsReadService old = new LegacyRead();
        Check(old.Describe().Success && typeof(IGameSettingsReadService).GetMethods().Length == 3, "The old read contract changed.");
        using var fixture = new ChangeFixture(); var service = fixture.Start("reader");
        Check(service.GetSnapshot().Success && fixture.Read.Manifest.Capabilities.Contains(GameSettingsCapabilityIds.Changes), "Actual module omitted its optional capability.");
        var metadata = fixture.Capabilities.Describe(GameSettingsCapabilityIds.Changes).Value!;
        var operation = metadata.Operations.Single();
        Check(metadata.Available && operation.Thread == OperationThread.MainThread && operation.Observation == ObservationMode.Sampled
            && operation.SamplingIntervalSeconds == 1 && operation.Cleanup == CleanupResponsibility.EnableScope && !operation.ModifiesGameplay,
            "Notification metadata hides sampling, thread or ownership semantics.");
    }

    private static void SampledRevisions()
    {
        using var fixture = new ChangeFixture(); var seen = new List<GameSettingsChanged>(); var service = fixture.Start("reader");
        using var subscription = Subscribe(service, seen.Add);
        Check(seen.Count == 1 && seen[0].IsInitial && seen[0].ChangedIds.Count == 0 && seen[0].Snapshot.Revision == 1, "Initial snapshot is missing or pretends to be a change.");
        fixture.Tick(); Check(seen.Count == 1, "Unchanged refresh generated another revision.");
        fixture.Backend.Values["music"] = .25f; fixture.Backend.Values["sfx"] = .4f; fixture.Tick();
        Check(seen.Count == 2 && !seen[1].IsInitial && seen[1].Snapshot.Revision == 2
            && seen[1].ChangedIds.SequenceEqual(new[] { "music", "sfx" }) && Value(seen[1].Snapshot, "music") == .25f
            && Value(seen[1].Snapshot, "sfx") == .4f && service.GetSnapshot().Value!.Revision == 2, "A complete sampled revision was lost or split.");
        fixture.Tick(); Check(seen.Count == 2, "The same revision was delivered twice.");
        subscription.Dispose(); fixture.Backend.Values["music"] = .3f; fixture.Tick(); Check(seen.Count == 2, "Disposed subscription delivered again.");
    }

    private static void ImmutableSnapshots()
    {
        using var fixture = new ChangeFixture(); var seen = new List<GameSettingsChanged>();
        using var subscription = Subscribe(fixture.Start("reader"), seen.Add);
        var original = seen[0].Snapshot; fixture.Backend.Values["music"] = .1f; fixture.Tick();
        Check(Value(original, "music") == .8f && Value(seen[1].Snapshot, "music") == .1f, "A prior snapshot changed with backend storage.");
        try { ((IList<GameFloatSettingValue>)original.Values)[0] = original.Values[0] with { Value = 99 }; throw new InvalidOperationException("Snapshot list is writable."); }
        catch (NotSupportedException) { }
        try { ((IList<string>)seen[1].ChangedIds)[0] = "foreign"; throw new InvalidOperationException("Changed IDs are writable."); }
        catch (NotSupportedException) { }
        Check(original.Values.All(value => value.ObservedAtUtc == original.ObservedAtUtc), "One revision contains mixed observation timestamps.");
    }

    private static void SubscribeDuringDelivery()
    {
        using var fixture = new ChangeFixture(); var service = fixture.Start("reader");
        var first = new List<GameSettingsChanged>(); var second = new List<GameSettingsChanged>(); IDisposable? added = null;
        using var subscription = Subscribe(service, change =>
        {
            first.Add(change);
            if (!change.IsInitial && added is null) added = Subscribe(service, second.Add);
        });
        fixture.Backend.Values["music"] = .2f; fixture.Tick(); fixture.Tick();
        Check(second.Count == 1 && second[0].IsInitial && second[0].Snapshot.Revision == 2, "Nested subscription missed or duplicated its initial revision.");
        fixture.Backend.Values["music"] = .3f; fixture.Tick();
        Check(first.Select(value => value.Snapshot.Revision).SequenceEqual(new long[] { 1, 2, 3 })
            && second.Select(value => value.Snapshot.Revision).SequenceEqual(new long[] { 2, 3 }) && !second[1].IsInitial, "Nested subscription has a revision gap.");
        added!.Dispose();
    }

    private static void AtomicCaptureFailure()
    {
        using var fixture = new ChangeFixture(); var service = fixture.Start("reader"); var seen = new List<GameSettingsChanged>();
        using var subscription = Subscribe(service, seen.Add);
        fixture.Backend.Values["music"] = .2f; fixture.Backend.Values["sfx"] = .3f;
        var original = fixture.Backend.List[1]; fixture.Backend.List[1] = original with { Read = () => throw new InvalidOperationException("late getter failure") };
        Check(!fixture.Read.Tick().Ready && seen.Count == 1 && service.GetSnapshot().Error?.Code == ErrorCode.NotReady, "Partial failed read was published as current data.");
        fixture.Backend.List[1] = original; fixture.Tick();
        Check(seen.Count == 2 && seen[1].Snapshot.Revision == 2 && seen[1].ChangedIds.SequenceEqual(new[] { "music", "sfx" }), "Failed capture advanced the baseline or revision.");
    }

    private static void OwnReentry()
    {
        using var fixture = new ChangeFixture(); var service = fixture.StartWrapped("reader", out var context);
        var outer = new List<GameSettingsChanged>(); var nested = new List<GameSettingsChanged>(); IDisposable? added = null; bool refreshRejected = false;
        context.OnNextOwn = _ =>
        {
            int reads = fixture.Backend.Reads;
            refreshRejected = fixture.Service.Refresh().Error?.Code == ErrorCode.Conflict && reads == fixture.Backend.Reads;
            added = Subscribe(service, nested.Add);
        };
        using var subscription = Subscribe(service, outer.Add);
        Check(refreshRejected && outer.Count == 1 && outer[0].IsInitial && nested.Count == 1 && nested[0].IsInitial
            && outer[0].Snapshot.Revision == nested[0].Snapshot.Revision, "Own reentry lost an initial or delivered an earlier delta.");
        fixture.Backend.Values["music"] = .2f; fixture.Tick();
        Check(outer.Count == 2 && nested.Count == 2 && outer[1].Snapshot.Revision == 2 && nested[1].Snapshot.Revision == 2,
            "Own reentry returned a dead subscription or left a revision gap.");
        added!.Dispose();
    }

    private static void OwnSelfStop()
    {
        using var fixture = new ChangeFixture(); var service = fixture.StartWrapped("reader", out var context); int calls = 0;
        context.OnNextOwn = _ => fixture.Host.Disable("reader");
        var subscribed = service.Subscribe(_ => calls++);
        Check(!subscribed.Success && calls == 0 && fixture.Host.Statuses.Single(status => status.Id == "reader").State == PluginState.Disabled,
            "Own self-stop delivered initial or returned a successful dead handle.");
        fixture.Backend.Values["music"] = .2f; fixture.Tick(); Check(calls == 0, "Stopped registration remained subscribed.");
    }

    private static void OwnDisposesSubscription()
    {
        using var fixture = new ChangeFixture(); var service = fixture.StartWrapped("reader", out var context); int calls = 0;
        context.OnNextOwn = resource => resource.Dispose();
        var subscribed = service.Subscribe(_ => calls++);
        Check(!subscribed.Success && calls == 0 && service.GetSnapshot().Success, "Own returned a successful disposed handle or incorrectly killed its provider.");
        fixture.Backend.Values["music"] = .2f; fixture.Tick(); Check(calls == 0, "Disposed-in-Own registration remained subscribed.");
    }

    private static void DescriptorChanges()
    {
        using var fixture = new ChangeFixture(); var seen = new List<GameSettingsChanged>();
        using var subscription = Subscribe(fixture.Start("reader"), seen.Add);
        fixture.Backend.UiReady = false; fixture.Tick();
        Check(seen.Count == 2 && seen[1].Snapshot.Values.All(value => !value.Descriptor.OverrideAvailable)
            && Value(seen[1].Snapshot, "music") == .8f, "Actual UI availability was omitted or fabricated a value change.");
        fixture.Backend.UiReady = true; fixture.Tick();
        Check(seen.Count == 3 && seen[2].Snapshot.Values.All(value => value.Descriptor.OverrideAvailable), "Recovered slider descriptors were not published.");
    }

    private static void CallbackFault()
    {
        using var fixture = new ChangeFixture(); int broken = 0, healthy = 0;
        using var first = Subscribe(fixture.Start("broken"), change => { if (!change.IsInitial) { broken++; throw new InvalidOperationException("actual plugin callback"); } });
        using var second = Subscribe(fixture.Start("healthy"), change => { if (!change.IsInitial) healthy++; });
        fixture.Backend.Values["music"] = .2f; fixture.Tick(); fixture.Backend.Values["music"] = .3f; fixture.Tick();
        Check(broken == 1 && healthy == 2 && fixture.Host.Statuses.Single(status => status.Id == "broken").State == PluginState.Faulted
            && fixture.Host.Statuses.Single(status => status.Id == "healthy").State == PluginState.Enabled, "Callback fault was assigned to the provider or blocked another plugin.");
    }

    private static void InitialFault()
    {
        using var fixture = new ChangeFixture(); var service = fixture.Start("broken"); int healthy = 0;
        var failed = service.Subscribe(_ => throw new InvalidOperationException("initial callback"));
        using var other = Subscribe(fixture.Start("healthy"), _ => healthy++);
        fixture.Backend.Values["music"] = .2f; fixture.Tick();
        Check(!failed.Success && fixture.Host.Statuses.Single(status => status.Id == "broken").State == PluginState.Faulted && healthy == 2,
            "Initial callback escaped actual scoped fault attribution.");
    }

    private static void DisableDuringDelivery()
    {
        using var fixture = new ChangeFixture(); int second = 0;
        var firstService = fixture.Start("first"); var secondService = fixture.Start("second");
        using var first = Subscribe(firstService, change => { if (!change.IsInitial) fixture.Host.Disable("second"); });
        using var later = Subscribe(secondService, change => { if (!change.IsInitial) second++; });
        fixture.Backend.Values["music"] = .2f; fixture.Tick();
        Check(second == 0 && secondService.GetSnapshot().Error?.Code == ErrorCode.ScopeExpired, "Queued callback ran after its activation ended.");
    }

    private static void CapabilityGeneration()
    {
        using var fixture = new ChangeFixture(); int later = 0; bool replaced = false;
        var firstService = fixture.Start("first"); var old = fixture.Start("second");
        using var first = Subscribe(firstService, change =>
        {
            if (change.IsInitial || replaced) return; replaced = true;
            fixture.Capabilities.SetUnavailable(GameSettingsCapabilityIds.Changes, "actual provider withdrawal");
            fixture.Capabilities.RegisterScoped<IGameSettingsChangeService>(GameSettingsCapabilityIds.Changes, context => fixture.Service.ChangesForPlugin(context));
        });
        using var previous = Subscribe(old, change => { if (!change.IsInitial) later++; });
        fixture.Backend.Values["music"] = .2f; fixture.Tick();
        Check(later == 0 && old.GetSnapshot().Error?.Code == ErrorCode.ScopeExpired, "Same-provider replacement revived a stale callback or service view.");
        var fresh = fixture.Reacquire("second"); var received = new List<GameSettingsChanged>();
        using var current = Subscribe(fresh, received.Add);
        fixture.Backend.Values["music"] = .3f; fixture.Tick();
        Check(later == 0 && received.Count == 2 && received[0].IsInitial && received[0].Snapshot.Revision == 2
            && received[1].Snapshot.Revision == 3, "New generation did not establish a fresh initial observation.");
    }

    private static void ReentrantRefresh()
    {
        using var fixture = new ChangeFixture(); var service = fixture.Start("reader"); bool rejected = false;
        using var subscription = Subscribe(service, change =>
        {
            if (change.IsInitial) return; int reads = fixture.Backend.Reads;
            rejected = fixture.Service.Refresh().Error?.Code == ErrorCode.Conflict && fixture.Backend.Reads == reads
                && service.GetSnapshot().Value!.Revision == change.Snapshot.Revision;
        });
        fixture.Backend.Values["music"] = .2f; fixture.Tick(); Check(rejected, "Reentrant refresh changed or invalidated a notification subject.");
    }

    private static void DisposeDuringDelivery()
    {
        using var fixture = new ChangeFixture(); int later = 0;
        var firstService = fixture.Start("first"); var secondService = fixture.Start("second");
        using var first = Subscribe(firstService, change => { if (!change.IsInitial) fixture.Read.Release(); });
        using var second = Subscribe(secondService, change => { if (!change.IsInitial) later++; });
        fixture.Backend.Values["music"] = .2f;
        Check(!fixture.Read.Tick().Ready && later == 0 && fixture.Backend.DisposeCount == 1
            && secondService.GetSnapshot().Error?.Code == ErrorCode.ScopeExpired, "Module withdrawal left a queued callback, live snapshot or duplicate backend disposal.");
    }

    private static void WrongThread()
    {
        using var fixture = new ChangeFixture(); var service = fixture.Start("reader"); int calls = 0;
        using var subscription = Subscribe(service, _ => calls++); int reads = fixture.Backend.Reads;
        var wrong = Task.Run(() => (service.GetSnapshot().Error?.Code, service.Subscribe(_ => { }).Error?.Code)).GetAwaiter().GetResult();
        bool disposalRejected = Task.Run(() => { try { subscription.Dispose(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
        Check(wrong == (ErrorCode.WrongThread, ErrorCode.WrongThread) && disposalRejected && reads == fixture.Backend.Reads, "Wrong-thread call entered the provider or changed subscriptions.");
        fixture.Backend.Values["music"] = .2f; fixture.Tick(); Check(calls == 2, "Rejected worker disposal still removed the subscription.");
    }

    private static void PublicEventIsNotAuthority()
    {
        using var fixture = new ChangeFixture(); var service = fixture.Start("reader"); int seen = 0;
        using var subscription = Subscribe(service, _ => seen++);
        fixture.Contexts["reader"].Events.Publish(new GameSettingsChanged(service.GetSnapshot().Value!, false, new[] { "music" }));
        Check(seen == 1, "An ordinary plugin event impersonated a private provider observation.");
    }

    private static void OverrideNotifications()
    {
        using var fixture = new ChangeFixture(); var service = fixture.Start("reader"); var seen = new List<GameSettingsChanged>();
        using var subscription = Subscribe(service, seen.Add);
        var writer = fixture.Contexts["reader"].GetService<IGameSettingsOverrideService>(GameSettingsCapabilityIds.Override).Service!;
        var overridden = writer.Override(new("music", .2f)); Check(overridden.Success, "Real setting lease failed.");
        Check(seen.Count == 1, "Notification pretended to be a direct setter hook.");
        fixture.Tick(); overridden.Value!.Dispose(); fixture.Tick();
        Check(seen.Count == 3 && Value(seen[1].Snapshot, "music") == .2f && Value(seen[2].Snapshot, "music") == .8f,
            "Actual override/restoration did not reach sampled settings observations.");
    }

    private static void AdjacentExternalBaseline(bool sampled)
    {
        using var fixture = new ChangeFixture();
        var observed = fixture.Start("reader");
        fixture.Start("writer");
        var writer = fixture.Contexts["writer"].GetService<IGameSettingsOverrideService>(GameSettingsCapabilityIds.Override).Service!;
        const float overrideValue = .2f;
        float externalValue = MathF.BitIncrement(overrideValue);
        var overridden = writer.Override(new("music", overrideValue));
        Check(overridden.Success, overridden.Error?.Message ?? "Volume override failed.");
        fixture.Backend.Values["music"] = externalValue;

        if (sampled)
        {
            fixture.Tick();
            Check(Value(observed.GetSnapshot().Value!, "music") == overrideValue,
                "The module did not reapply the override after an adjacent external write.");
            Check(writer.Describe("music").Value!.Baseline == externalValue,
                "The refreshed lease missed the adjacent external volume.");
        }

        Check(fixture.Host.Disable("writer").Success, "Writer plugin did not stop.");
        fixture.Tick();
        Check(Value(observed.GetSnapshot().Value!, "music") == externalValue,
            "Releasing the lease restored a stale volume over the adjacent external write.");
    }

    private sealed class LegacyRead : IGameSettingsReadService
    {
        public Result<IReadOnlyList<GameFloatSettingDescriptor>> Describe() => Result<IReadOnlyList<GameFloatSettingDescriptor>>.Ok(Array.Empty<GameFloatSettingDescriptor>());
        public Result<IReadOnlyList<GameFloatSettingValue>> GetSettings() => Result<IReadOnlyList<GameFloatSettingValue>>.Ok(Array.Empty<GameFloatSettingValue>());
        public Result<GameFloatSettingValue> Read(string id) => Result<GameFloatSettingValue>.Fail(new(ErrorCode.OperationUnsupported, id));
    }

    private sealed class ChangeFixture : IDisposable
    {
        private readonly MainThreadDispatcher _main = new();
        private readonly SettingsOverrideModule _write;
        internal readonly FakeBackend Backend = new();
        internal readonly CapabilityRegistry Capabilities = new(new("test", "test", "test", "test"));
        internal readonly SettingsModule Read;
        internal readonly PluginHost Host;
        internal readonly Dictionary<string, IPluginContext> Contexts = new(StringComparer.Ordinal);
        internal GameSettingsService Service => Read.Service!;
        internal ChangeFixture()
        {
            var logger = new TestLogger(); AdapterCapabilityMetadata.Declare(Capabilities);
            Read = new(() => Backend, _main, logger); _write = new(Read);
            Check(Read.Probe().Ready && _write.Probe().Ready, "Actual settings module probe failed.");
            Read.Register(new(Capabilities, Read.Manifest)); _write.Register(new(Capabilities, _write.Manifest));
            Host = new(Capabilities, _main, new EventHub(logger), logger);
        }
        internal IGameSettingsChangeService Start(string id)
        {
            Check(Host.Register(new Probe(id, context => Contexts[id] = context)).Success && Host.Enable(id).Success, "Plugin activation failed.");
            return Reacquire(id);
        }
        internal IGameSettingsChangeService Reacquire(string id)
        {
            var lookup = Contexts[id].GetService<IGameSettingsChangeService>(GameSettingsCapabilityIds.Changes);
            Check(lookup.Available && lookup.Service is not null, lookup.Error?.Message ?? "Scoped settings changes lookup failed.");
            return lookup.Service!;
        }
        internal IGameSettingsChangeService StartWrapped(string id, out OwnReentryContext context)
        {
            OwnReentryContext? captured = null;
            Capabilities.SetUnavailable(GameSettingsCapabilityIds.Changes, "Re-register through a forwarding ownership context.");
            Capabilities.RegisterScoped<IGameSettingsChangeService>(GameSettingsCapabilityIds.Changes, actual =>
                Service.ChangesForPlugin(captured = new OwnReentryContext(actual)));
            var service = Start(id); context = captured!; return service;
        }
        internal void Tick() => Check(Read.Tick().Ready, "Actual settings module tick failed.");
        public void Dispose() { Host.Dispose(); _write.Release(); Read.Release(); _main.Dispose(); }
    }

    // Real host activation, dispatcher, metadata, events and resource ownership remain in use. Only the host-owned
    // Own call is wrapped to expose legal synchronous reentry; this does not manufacture a generation or release proof.
    private sealed class OwnReentryContext : IPluginContext
    {
        private readonly IPluginContext _actual;
        internal Action<IDisposable>? OnNextOwn;
        internal OwnReentryContext(IPluginContext actual) => _actual = actual;
        public string PluginId => _actual.PluginId;
        public ILogger Log => _actual.Log;
        public IMainThreadDispatcher MainThread => _actual.MainThread;
        public IEventBus Events => _actual.Events;
        public IPluginConfiguration Configuration => _actual.Configuration;
        public GameBuildDescriptor GameBuild => _actual.GameBuild;
        public CapabilityDescriptor GetCapability(string id) => _actual.GetCapability(id);
        public ServiceLookup<T> GetService<T>(string id) where T : class => _actual.GetService<T>(id);
        public T Own<T>(T resource) where T : IDisposable
        {
            var owned = _actual.Own(resource); var reenter = OnNextOwn; OnNextOwn = null; reenter?.Invoke(owned); return owned;
        }
    }
}
