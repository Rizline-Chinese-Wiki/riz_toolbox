using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Settings;

internal sealed partial class GameSettingsService
{
    private readonly string _settingsSession = Guid.NewGuid().ToString("N");
    private readonly List<SettingsSubscription> _settingsSubscriptions = new();
    private GameSettingsSnapshot? _settingsObservation;
    private bool _settingsObservationUsable, _refreshingSettings;
    private int _settingsDeliveryDepth;

    internal IGameSettingsChangeService ChangesForPlugin(IPluginContext context)
        => context.Own(new ChangesView(this, context));

    private Result ObserveSettingsChanges()
    {
        // Finish every getter before replacing the revision. A later getter failure must not publish earlier partial reads.
        var captured = GetAll();
        if (!captured.Success) return Result.Fail(captured.Error!);
        var now = DateTimeOffset.UtcNow;
        var values = captured.Value!.Select(value => value with { ObservedAtUtc = now }).ToArray();
        var previous = _settingsObservation;
        var old = previous?.Values.ToDictionary(value => value.Descriptor.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, GameFloatSettingValue>(StringComparer.Ordinal);
        var changed = new HashSet<string>(old.Keys, StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (old.TryGetValue(value.Descriptor.Id, out var prior)
                && prior.Value == value.Value && prior.Descriptor == value.Descriptor) changed.Remove(value.Descriptor.Id);
            else changed.Add(value.Descriptor.Id);
        }
        var revision = previous is null ? 1 : changed.Count == 0 ? previous.Revision : checked(previous.Revision + 1);
        var current = new GameSettingsSnapshot(_settingsSession, revision, now, values);
        _settingsObservation = current; _settingsObservationUsable = true;
        if (previous is not null && changed.Count != 0)
        {
            var notification = new GameSettingsChanged(current, false, changed.OrderBy(id => id, StringComparer.Ordinal));
            // New subscriptions receive this revision as their initial snapshot and are absent from this delivery snapshot.
            foreach (var subscription in _settingsSubscriptions.ToArray()) subscription.Deliver(notification);
        }
        return _disposeRequested ? Result.Fail(new(ErrorCode.ScopeExpired, "Settings were withdrawn during notification.")) : Result.Ok();
    }

    private void CloseSettingsChanges()
    {
        _settingsObservationUsable = false;
        foreach (var subscription in _settingsSubscriptions.ToArray())
        {
            try { subscription.Dispose(); }
            catch (Exception error) { _log.TryLog(LogLevel.Error, "Settings observation cleanup failed.", error); }
        }
    }

    // Private envelopes prevent an ordinary SDK event publication from impersonating a provider observation.
    // Both the exact subscription and the currently retained immutable subject are rechecked for every callback.
    private sealed record SettingsDelivery(SettingsSubscription Subscription, GameSettingsChanged Change);

    private sealed class ChangesView : IGameSettingsChangeService, IDisposable
    {
        internal readonly GameSettingsService Owner;
        internal readonly IPluginContext Context;
        private readonly ICapabilityMetadataProvider _metadata;
        private readonly long _generation;
        private bool _disposed;

        internal ChangesView(GameSettingsService owner, IPluginContext context)
        {
            (Owner, Context) = (owner, context);
            context.MainThread.VerifyAccess();
            var lookup = context.GetService<ICapabilityMetadataProvider>(CapabilityIds.Metadata);
            _metadata = lookup.Available && lookup.Service is not null ? lookup.Service
                : throw new InvalidOperationException("Settings notifications require actual capability-generation metadata.");
            var described = _metadata.Describe(GameSettingsCapabilityIds.Changes);
            if (!described.Success || described.Value is not { Available: true, Generation: > 0 })
                throw new InvalidOperationException("The settings observation capability is unavailable.");
            _generation = described.Value.Generation;
        }

        internal OperationError? Check()
        {
            if (Owner.Verify() is { } invalid) return invalid;
            if (_disposed || Context is IPluginScope { IsActive: false }) return Expired();
            try
            {
                Context.MainThread.VerifyAccess();
                var described = _metadata.Describe(GameSettingsCapabilityIds.Changes);
                return described.Success && described.Value is { Available: true } current && current.Generation == _generation
                    ? null : Expired();
            }
            catch (ObjectDisposedException) { return Expired(); }
            catch (Exception ex) { return new(ErrorCode.CapabilityUnavailable, "Cannot verify settings observation generation: " + ex.Message); }
        }

        private static OperationError Expired() => new(ErrorCode.ScopeExpired, "Settings observation capability or activation has expired.");
        public Result<GameSettingsSnapshot> GetSnapshot()
        {
            if (Check() is { } invalid) return Result<GameSettingsSnapshot>.Fail(invalid);
            return Owner.Ready && Owner._settingsObservationUsable && Owner._settingsObservation is { } snapshot
                ? Result<GameSettingsSnapshot>.Ok(snapshot)
                : Result<GameSettingsSnapshot>.Fail(new(ErrorCode.NotReady, "No complete current settings observation is available."));
        }

        public Result<IDisposable> Subscribe(Action<GameSettingsChanged> handler)
        {
            if (Check() is { } invalid) return Result<IDisposable>.Fail(invalid);
            if (handler is null) return Result<IDisposable>.Fail(new(ErrorCode.InvalidArgument, "A settings change handler is required."));
            SettingsSubscription? subscription = null;
            // Own may synchronously stop a scope or invoke host code. Keep the initial subject stable for the
            // entire registration transaction, including Own; protecting only Deliver would allow a delta before initial.
            ++Owner._settingsDeliveryDepth;
            try
            {
                var observed = GetSnapshot();
                if (!observed.Success) return Result<IDisposable>.Fail(observed.Error!);
                subscription = new(this, handler);
                Owner._settingsSubscriptions.Add(subscription);
                Context.Own(subscription);
                if (Check() is { } stopped) { subscription.Dispose(); return Result<IDisposable>.Fail(stopped); }
                if (subscription.IsDisposed) return Result<IDisposable>.Fail(Expired());
                subscription.Deliver(new(observed.Value!, true, Array.Empty<string>()));
                if (Check() is { } expired) { subscription.Dispose(); return Result<IDisposable>.Fail(expired); }
                if (subscription.IsDisposed) return Result<IDisposable>.Fail(Expired());
                return Result<IDisposable>.Ok(subscription);
            }
            catch (Exception ex)
            {
                subscription?.Dispose();
                return Result<IDisposable>.Fail(new(ErrorCode.ServiceUnavailable, "Cannot subscribe to settings observations: " + ex.Message));
            }
            finally { --Owner._settingsDeliveryDepth; }
        }

        public void Dispose()
        {
            Owner._main.VerifyAccess(); if (_disposed) return; _disposed = true;
            foreach (var subscription in Owner._settingsSubscriptions.Where(item => ReferenceEquals(item.View, this)).ToArray()) subscription.Dispose();
        }
    }

    private sealed class SettingsSubscription : IDisposable
    {
        internal readonly ChangesView View;
        private readonly IDisposable _events;
        private readonly Action<GameSettingsChanged> _handler;
        private bool _disposed;
        internal bool IsDisposed => _disposed;
        internal SettingsSubscription(ChangesView view, Action<GameSettingsChanged> handler)
        {
            (View, _handler) = (view, handler);
            // ScopedEvents owns exception reporting and plugin fault isolation, including the synchronous initial callback.
            _events = view.Context.Events.Subscribe<SettingsDelivery>(Receive);
        }
        private void Receive(SettingsDelivery delivery)
        {
            if (!ReferenceEquals(delivery.Subscription, this) || _disposed) return;
            var owner = View.Owner;
            if (View.Check() is not null || !owner.Ready || !owner._settingsObservationUsable
                || !ReferenceEquals(owner._settingsObservation, delivery.Change.Snapshot)
                || delivery.Change.Snapshot.SessionId != owner._settingsSession) { Dispose(); return; }
            _handler(delivery.Change);
        }
        internal void Deliver(GameSettingsChanged change)
        {
            if (_disposed) return;
            if (View.Check() is not null) { Dispose(); return; }
            var owner = View.Owner; ++owner._settingsDeliveryDepth;
            try { View.Context.Events.Publish(new SettingsDelivery(this, change)); }
            finally { --owner._settingsDeliveryDepth; }
        }
        public void Dispose()
        {
            View.Owner._main.VerifyAccess(); if (_disposed) return; _disposed = true;
            View.Owner._settingsSubscriptions.Remove(this); _events.Dispose();
        }
    }
}
