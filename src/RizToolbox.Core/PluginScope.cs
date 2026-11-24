using RizToolbox.Sdk;
using RizToolbox.Core.Diagnostics;

namespace RizToolbox.Core;

internal sealed class PluginScope : IPluginScope
{
    private readonly CapabilityRegistry _capabilities;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly List<IDisposable> _resources = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _lifetime;
    private readonly EventHub _eventHub;
    private readonly ILogger _hostLog;
    private readonly string _dataRoot;
    private readonly PluginScope? _parent;
    private readonly List<PluginScope> _children = new();
    private readonly Action<Exception> _fault;
    private readonly Dictionary<(string Id, Type Contract, long Generation), object> _services = new();
    private volatile bool _active = true;
    private bool _disposed;
    private bool _deactivating, _disposeRequested;

    public PluginScope(string id, CapabilityRegistry capabilities, MainThreadDispatcher dispatcher,
        EventHub events, ILogger log, Action<Exception> fault, string dataRoot)
        : this(id, capabilities, dispatcher, events, log, fault, dataRoot, null, "activation") { }

    private PluginScope(string id, CapabilityRegistry capabilities, MainThreadDispatcher dispatcher,
        EventHub events, ILogger log, Action<Exception> fault, string dataRoot, PluginScope? parent, string name)
    {
        PluginId = id;
        _lifetime = _stop.Token;
        _eventHub = events; _hostLog = log; _dataRoot = dataRoot; _parent = parent;
        ScopeId = (parent?.ScopeId ?? id) + "/" + name + "@" + Guid.NewGuid().ToString("N");
        _capabilities = capabilities;
        _dispatcher = dispatcher;
        _fault = fault;
        Log = new ScopedLogger(id, log);
        MainThread = new ScopedDispatcher(this, dispatcher);
        Events = new ScopedEvents(this, events);
        Configuration = new PluginConfiguration(dataRoot, id, VerifyActive);
    }

    public string PluginId { get; }
    public string ScopeId { get; }
    public bool IsActive => _active;
    public CancellationToken Lifetime => _lifetime;

    public IPluginScope CreateScope(string name, ScopeLifetime lifetime = ScopeLifetime.Manual)
    {
        VerifyActive();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.')))
            throw new ArgumentException("Scope names must be 1-64 ASCII letters, digits, dots, underscores or hyphens.", nameof(name));
        if (!Enum.IsDefined(typeof(ScopeLifetime), lifetime)) throw new ArgumentOutOfRangeException(nameof(lifetime));
        var child = new PluginScope(PluginId, _capabilities, _dispatcher, _eventHub, _hostLog, _fault, _dataRoot, this, name);
        _children.Add(child); _resources.Add(child);
        try
        {
            if (lifetime == ScopeLifetime.Scene)
                child.Own(_eventHub.SubscribeBoundary<GameSceneChanged>(_ => child.Dispose()));
            if (lifetime == ScopeLifetime.Run)
                child.Own(new RunScopeBoundary(child)).Bind();
            return child;
        }
        catch { child.Dispose(); throw; }
    }
    public ILogger Log { get; }
    public IMainThreadDispatcher MainThread { get; }
    public IEventBus Events { get; }
    public IPluginConfiguration Configuration { get; }
    public GameBuildDescriptor GameBuild => _capabilities.GameBuild;
    public CapabilityDescriptor GetCapability(string id) { VerifyActive(); return _capabilities.GetCapability(id); }
    public ServiceLookup<T> GetService<T>(string id) where T : class
    {
        try { VerifyActive(); }
        catch (ObjectDisposedException error) { return new(false, null, new(ErrorCode.ScopeExpired, error.Message, id)); }
        catch (InvalidOperationException error) { return new(false, null, new(ErrorCode.WrongThread, error.Message, id)); }
        var generation = _capabilities.GetGeneration(id);
        var key = (id, typeof(T), generation);
        if (_services.TryGetValue(key, out var cached)) return new(true, (T)cached, null);
        if (!typeof(T).IsInterface) return new(false, null, new(ErrorCode.ContractIdentityMismatch, "Plugin service contracts must be interfaces.", id));
        var scopedFactory = _capabilities.IsScoped(id);
        var lookup = _capabilities.GetService<T>(id, this);
        if (!lookup.Available || lookup.Service is null) return lookup;
        FactoryOwner? owner = scopedFactory && lookup.Service is IDisposable disposable
            ? Own(new FactoryOwner(disposable, VerifyThread)) : null;
        try { VerifyService(id, generation); }
        catch (ObjectDisposedException ex) { owner?.Dispose(); return new(false, null, new(ErrorCode.ScopeExpired, ex.Message, id)); }
        void VerifyView()
        {
            VerifyService(id, generation);
            if (owner?.Disposed == true) throw new ObjectDisposedException($"Scoped service '{id}'");
        }
        var view = ScopedServiceViews.Create(this, lookup.Service, id, VerifyView, owner is null ? null : owner.Dispose);
        _services.Add(key, view);
        return new(true, view, null);
    }

    public T Own<T>(T resource) where T : IDisposable
    {
        _dispatcher.VerifyAccess();
        if (resource is null) throw new ArgumentNullException(nameof(resource));
        if (!_active)
        {
            resource.Dispose();
            throw new ObjectDisposedException($"Plugin scope '{PluginId}'");
        }
        if (!_resources.Any(item => ReferenceEquals(item, resource))) _resources.Add(resource);
        return resource;
    }

    internal void VerifyActive()
    {
        _dispatcher.VerifyAccess();
        if (!_active) throw new ObjectDisposedException($"Plugin scope '{PluginId}'");
    }

    internal void VerifyService(string id, long generation)
    {
        VerifyActive();
        if (_capabilities.GetGeneration(id) != generation)
            throw new ObjectDisposedException($"Capability '{id}' was withdrawn or replaced.");
    }

    internal bool Active => _active;
    internal void VerifyThread() => _dispatcher.VerifyAccess();
    internal void ReportFault(Exception error) => _fault(error);
    internal void InvokeCallback(string operation, Action callback)
    {
        if (_eventHub.Performance is { } performance) performance.Invoke(PluginId, operation, callback);
        else callback();
    }
    internal (int Scopes, int Resources, int Services) ResourceCounts()
    {
        VerifyThread();
        var children = _children.Select(x => x.ResourceCounts()).ToArray();
        return (1 + children.Sum(x => x.Scopes), _resources.Count + children.Sum(x => x.Resources), _services.Count + children.Sum(x => x.Services));
    }

    internal (int Scopes, int Resources, int Services) CaptureDiagnosticResourceCounts(ref int remaining, out bool truncated)
    {
        VerifyThread();
        truncated = remaining == 0;
        if (truncated) return (0, 0, 0);
        remaining--;
        var scopes = 1;
        long resources = _resources.Count, services = _services.Count;
        var path = new Stack<(PluginScope Scope, int Child)>();
        path.Push((this, 0));
        while (path.TryPop(out var next))
        {
            if (next.Child == next.Scope._children.Count) continue;
            if (remaining == 0) { truncated = true; break; }
            path.Push((next.Scope, next.Child + 1));
            var child = next.Scope._children[next.Child];
            remaining--; scopes++;
            resources += child._resources.Count; services += child._services.Count;
            path.Push((child, 0));
        }
        if (resources > int.MaxValue || services > int.MaxValue) truncated = true;
        return (scopes, (int)Math.Min(int.MaxValue, resources), (int)Math.Min(int.MaxValue, services));
    }

    private sealed class FactoryOwner : IDisposable
    {
        private IDisposable? _inner; private readonly Action _verifyThread;
        public bool Disposed => _inner is null;
        public FactoryOwner(IDisposable inner, Action verifyThread) { _inner = inner; _verifyThread = verifyThread; }
        public void Dispose() { if (_inner is null) return; _verifyThread(); var inner = _inner; _inner = null; inner.Dispose(); }
    }

    /// <summary>Uses the observer's identity, rather than treating every published result as this scope's run.</summary>
    private sealed class RunScopeBoundary : IDisposable
    {
        private readonly PluginScope _scope;
        private readonly List<IDisposable> _subscriptions = new();
        private Action? _registryChanged;
        private bool _disposed;
        private long _generation, _revision, _sceneGeneration;
        private string _session = "";
        private string? _run;

        internal RunScopeBoundary(PluginScope scope) => _scope = scope;

        internal void Bind()
        {
            var id = GameLifecycleCapabilityIds.Lifecycle;
            // Only hosts which never declared a lifecycle provider use the original uncorrelated contract.
            // A withdrawn/broken provider is not permission to attach this scope to any later result.
            if (!_scope._capabilities.Describe(id).Success)
            {
                _subscriptions.Add(_scope._eventHub.SubscribeBoundary<GameSceneChanged>(_ => _scope.Dispose()));
                _subscriptions.Add(_scope._eventHub.SubscribeBoundary<RunStarted>(_ => _scope.Dispose()));
                _subscriptions.Add(_scope._eventHub.SubscribeBoundary<RunFinished>(_ => _scope.Dispose()));
                return;
            }
            try
            {
                _generation = _scope._capabilities.GetGeneration(id);
                if (_generation < 0) { Invalidate("The lifecycle provider is unavailable; a Run scope cannot be safely bound."); return; }
                var lookup = _scope.GetService<IGameLifecycleService>(id);
                if (!lookup.Available || lookup.Service is null)
                { Invalidate("The lifecycle provider cannot be queried: " + lookup.Error?.Message); return; }
                var service = lookup.Service;
                var first = Read(service);
                if (first is null) return;
                _session = first.SessionId; _revision = first.Revision; _sceneGeneration = first.SceneGeneration;
                _run = IsLive(first) ? first.RunId : null;
                _registryChanged = () =>
                {
                    if (_scope._capabilities.GetGeneration(id) != _generation)
                        Invalidate("The lifecycle provider was withdrawn or replaced; the bound Run scope has expired.");
                };
                _scope._capabilities.Changed += _registryChanged;
                _subscriptions.Add(_scope._eventHub.SubscribeBoundary<GameLifecycleChange>(OnLifecycle));
                _subscriptions.Add(_scope._eventHub.SubscribeBoundary<GameSceneChanged>(OnScene));
                _subscriptions.Add(_scope._eventHub.SubscribeBoundary<RunStarted>(OnStart));
                _subscriptions.Add(_scope._eventHub.SubscribeBoundary<RunFinished>(OnFinish));
                // Main-thread providers cannot normally interleave here. Re-reading also closes a boundary
                // crossed by a reentrant provider/factory, rather than returning a scope bound to stale state.
                var current = Read(service);
                if (current is null || _disposed) return;
                if (current.SessionId != _session || current.SceneGeneration != _sceneGeneration || current.Revision < _revision ||
                    (_run is not null && (current.RunId != _run || current.Phase == RunLifecyclePhase.Finished)) ||
                    (_run is null && IsLive(current)))
                { Invalidate("The observed run changed while its scope was being created; create a new Run scope from the current lifecycle callback."); return; }
                _revision = current.Revision;
            }
            catch (Exception ex) { Invalidate("The lifecycle provider failed while binding the Run scope.", ex); }
        }

        private GameLifecycleSnapshot? Read(IGameLifecycleService service)
        {
            var result = service.GetSnapshot();
            if (_scope._capabilities.GetGeneration(GameLifecycleCapabilityIds.Lifecycle) != _generation || !result.Success || !Valid(result.Value))
            { Invalidate("The lifecycle snapshot is unavailable or invalid; the Run scope has expired. " + result.Error?.Message); return null; }
            return result.Value;
        }

        private static bool Valid(GameLifecycleSnapshot? state) => state is not null &&
            !string.IsNullOrWhiteSpace(state.SessionId) && state.SessionId.Length <= 128 && state.Revision >= 0 && state.SceneGeneration >= 0 &&
            (state.RunId is null || !string.IsNullOrWhiteSpace(state.RunId) && state.RunId.Length <= 256) && Enum.IsDefined(typeof(RunLifecyclePhase), state.Phase);
        private static bool IsLive(GameLifecycleSnapshot state) => state.RunsAvailable && state.RunId is not null &&
            state.Phase is RunLifecyclePhase.Playing or RunLifecyclePhase.Paused or RunLifecyclePhase.AwaitingResult or RunLifecyclePhase.ObservationLost;

        private bool Later(string? session, long revision, long scene) => !_disposed && session == _session && revision > _revision && scene >= _sceneGeneration;

        private void OnLifecycle(GameLifecycleChange change)
        {
            if (change is null || !Enum.IsDefined(typeof(GameLifecycleChangeKind), change.Kind) || !Valid(change.Snapshot)) return;
            var state = change.Snapshot;
            if (!Later(state.SessionId, state.Revision, state.SceneGeneration)) return;
            if (state.SceneGeneration > _sceneGeneration) { _scope.Dispose(); return; }
            // ResultObserved can carry the current live snapshot alongside an unrelated old result.
            if (change.Kind == GameLifecycleChangeKind.RunEnded && _run is not null && change.RelatedRunId == _run &&
                state.RunId == _run && state.Phase == RunLifecyclePhase.Finished &&
                (change.Result is null || Matches(change.Result, state.Revision, state.SceneGeneration)))
            { _scope.Dispose(); return; }
            if (IsLive(state) && state.RunId != _run)
            { _scope.Dispose(); return; }
            _revision = state.Revision;
        }

        private void OnScene(GameSceneChanged change)
        {
            if (change is not null && Later(change.SessionId, change.Revision, change.SceneGeneration) && change.SceneGeneration > _sceneGeneration)
                _scope.Dispose();
        }

        private void OnStart(RunStarted change)
        {
            if (change is null || string.IsNullOrWhiteSpace(change.RunId) || !Later(change.SessionId, change.Revision, change.SceneGeneration)) return;
            if (change.SceneGeneration > _sceneGeneration || change.RunId != _run)
                _scope.Dispose();
            else _revision = change.Revision;
        }

        private bool Matches(RunFinished result, long revision, long scene) => result.AssociatedWithObservedStart && result.RunId == _run &&
            result.SessionId == _session && result.Revision == revision && result.SceneGeneration == scene;

        private void OnFinish(RunFinished change)
        {
            if (change is not null && _run is not null && Later(change.SessionId, change.Revision, change.SceneGeneration) &&
                change.SceneGeneration == _sceneGeneration && Matches(change, change.Revision, change.SceneGeneration))
                _scope.Dispose();
        }

        private void Invalidate(string message, Exception? exception = null)
        {
            if (_disposed) return;
            _scope.Log.Log(LogLevel.Warning, message, exception);
            _scope.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_registryChanged is not null) _scope._capabilities.Changed -= _registryChanged;
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
        }
    }

    public void Deactivate()
    {
        _dispatcher.VerifyAccess();
        if (!_active) return;
        var expiring = new List<PluginScope>();
        MarkInactive(expiring);
        try
        {
            // Invalidate the whole tree before executing arbitrary cancellation callbacks.
            foreach (var scope in expiring)
            {
                try { scope._stop.Cancel(); }
                catch (Exception ex) { scope.Log.Log(LogLevel.Error, "Scope cancellation callback failed.", ex); }
            }
        }
        finally
        {
            foreach (var scope in expiring) scope._deactivating = false;
            foreach (var scope in expiring) if (scope._disposeRequested) scope.Dispose();
        }
    }

    private void MarkInactive(List<PluginScope> expiring)
    {
        if (!_active) return;
        _active = false; _deactivating = true; expiring.Add(this);
        foreach (var child in _children.ToArray()) child.MarkInactive(expiring);
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        if (_deactivating) { _disposeRequested = true; return; }
        _disposed = true;
        Deactivate();
        var resources = _resources.ToArray();
        _resources.Clear();
        for (int i = resources.Length - 1; i >= 0; i--)
        {
            try { resources[i].Dispose(); }
            catch (Exception ex) { Log.Log(LogLevel.Error, "Owned resource cleanup failed.", ex); }
        }
        _children.Clear();
        _parent?._resources.Remove(this);
        _parent?._children.Remove(this);
        _services.Clear();
        _stop.Dispose();
    }

    private sealed class ScopedLogger : ILogger
    {
        private readonly string _id;
        private readonly ILogger _inner;
        public ScopedLogger(string id, ILogger inner)
        {
            _id = id;
            _inner = inner is StructuredDiagnosticLogger structured
                ? structured.ForContext(new DiagnosticContext(PluginId: id)) : inner;
        }
        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            try { _inner.Log(level, $"[{_id}] {message}", exception); }
            catch { /* A diagnostic sink must never interrupt scope cancellation or cleanup. */ }
        }
    }

    private sealed class ScopedEvents : IEventBus
    {
        private readonly PluginScope _scope;
        private readonly EventHub _events;
        public ScopedEvents(PluginScope scope, EventHub events) { _scope = scope; _events = events; }
        public IDisposable Subscribe<T>(Action<T> handler, int priority = 0)
        {
            _scope.VerifyActive();
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            return _scope.Own(_events.SubscribeOwned<T>(message => { if (_scope._active) _scope.InvokeCallback("event:" + typeof(T).FullName, () => handler(message)); }, priority, _scope._fault));
        }
        public void Publish<T>(T message) { _scope.VerifyActive(); _events.Publish(message); }
    }

    private sealed class ScopedDispatcher : IMainThreadDispatcher
    {
        private readonly PluginScope _scope;
        private readonly MainThreadDispatcher _inner;
        public ScopedDispatcher(PluginScope scope, MainThreadDispatcher inner) { _scope = scope; _inner = inner; }
        public bool IsMainThread => _inner.IsMainThread;
        public void VerifyAccess() => _scope.VerifyActive();
        public async Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            if (action is null) throw new ArgumentNullException(nameof(action));
            if (!_scope._active) throw new ObjectDisposedException($"Plugin scope '{_scope.PluginId}'");
            // Linked cancellation keeps queued work from running after disable or activation rollback.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _scope.Lifetime);
            await _inner.InvokeAsync(() =>
            {
                if (!_scope._active) return;
                try { _scope.InvokeCallback("dispatch", action); }
                catch (Exception ex)
                {
                    _scope.Log.Log(LogLevel.Error, "Dispatched plugin callback failed.", ex);
                    _scope._fault(ex);
                    throw;
                }
            }, linked.Token).ConfigureAwait(false);
        }
    }
}
