using RizToolbox.Sdk;
using RizToolbox.Core.Diagnostics;

namespace RizToolbox.Core;

/// <summary>Events run synchronously on the thread that constructed the hub.</summary>
public sealed class EventHub : IEventBus
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly ILogger _log;
    private readonly Dictionary<Type, List<Subscription>> _subscriptions = new();
    private long _sequence;
    public CallbackPerformanceMonitor? Performance { get; set; }

    private sealed class Subscription : IDisposable
    {
        public Type Type = null!;
        public Action<object?> Handler = null!;
        public Action<Exception>? Fault;
        public int Priority;
        public long Sequence;
        public bool Active = true;
        public bool IsBoundary;
        public Action<Subscription> Remove = null!;
        public void Dispose() => Remove(this);
    }

    public EventHub(ILogger log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    public IDisposable Subscribe<T>(Action<T> handler, int priority = 0) => SubscribeOwned(handler, priority, null);

    internal IDisposable SubscribeOwned<T>(Action<T> handler, int priority, Action<Exception>? fault)
        => SubscribeCore(handler, priority, fault, false);

    internal IDisposable SubscribeBoundary<T>(Action<T> handler) => SubscribeCore(handler, 0, null, true);

    private IDisposable SubscribeCore<T>(Action<T> handler, int priority, Action<Exception>? fault, bool boundary)
    {
        VerifyAccess();
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        var subscription = new Subscription
        {
            Type = typeof(T), Handler = message => handler((T)message!), Priority = priority,
            Sequence = _sequence++, Fault = fault, Remove = Remove
            , IsBoundary = boundary
        };
        if (!_subscriptions.TryGetValue(typeof(T), out var entries))
            _subscriptions[typeof(T)] = entries = new();
        entries.Add(subscription);
        return subscription;
    }

    public void Publish<T>(T message)
    {
        VerifyAccess();
        if (!_subscriptions.TryGetValue(typeof(T), out var entries)) return;
        // Snapshot permits subscribe/dispose/disable while an event is being delivered.
        foreach (var entry in entries.OrderByDescending(x => x.IsBoundary).ThenByDescending(x => x.Priority).ThenBy(x => x.Sequence).ToArray())
        {
            if (!entry.Active) continue;
            try { entry.Handler(message); }
            catch (Exception ex)
            {
                _log.TryLog(LogLevel.Error, $"Event subscriber failed for '{typeof(T).FullName}'.", ex);
                entry.Dispose();
                try { entry.Fault?.Invoke(ex); }
                catch (Exception cleanupError) { _log.TryLog(LogLevel.Error, "Event fault cleanup failed.", cleanupError); }
            }
        }
    }

    private void Remove(Subscription subscription)
    {
        VerifyAccess();
        if (!subscription.Active) return;
        subscription.Active = false;
        if (_subscriptions.TryGetValue(subscription.Type, out var entries))
        {
            entries.Remove(subscription);
            if (entries.Count == 0) _subscriptions.Remove(subscription.Type);
        }
    }

    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("Event subscriptions and publication must run on the game main thread.");
    }
}
