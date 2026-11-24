using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal readonly record struct GameplayObservationBoundary(string? SessionId, long SceneGeneration, string? RunId,
    bool CurrentRun, long Revision, long Controller = 0, long Information = 0);

internal sealed partial class GameplayService
{
    private const double ControlObservationIntervalSeconds = .25;
    private readonly Func<GameplayObservationBoundary>? _controlBoundary;
    private readonly Func<object?>? _controlSource;
    private readonly List<ControlSubscription> _controlSubscriptions = new();
    private GameplayControlObservationSnapshot? _lastControlObservation;
    private GameplayObservationBoundary _publishedControlBoundary;
    private object? _publishedControlSource;
    private long _lastControlTick, _controlObservationRevision;
    private double _nextControlObservation;
    private bool _observingControls, _controlObservationDirty, _controlObservationRevoked;

    private Result<IGameplayControlSubscription> SubscribeControls(IPluginContext context,
        Action<GameplayControlObservationSnapshot> handler, CancellationToken cancellation)
    {
        var error = Guard(context, cancellation);
        if (error is not null) return Result<IGameplayControlSubscription>.Fail(ControlError(error));
        if (handler is null) return Result<IGameplayControlSubscription>.Fail(ControlError(new(ErrorCode.InvalidArgument, "A control observation handler is required.")));
        ControlSubscription? subscription = null;
        try
        {
            var initial = CurrentControlObservation();
            subscription = new ControlSubscription(this, context, initial, handler, cancellation);
            // One publication on the shared host bus preserves scope, callback
            // performance accounting and plugin fault attribution for each subscriber.
            subscription.Events = context.Events.Subscribe<ControlObservationDelivery>(subscription.Receive);
            _controlSubscriptions.Add(subscription);
            return Result<IGameplayControlSubscription>.Ok(context.Own<IGameplayControlSubscription>(subscription));
        }
        catch (Exception ex)
        {
            subscription?.Dispose();
            return Result<IGameplayControlSubscription>.Fail(ControlError(ex is ObjectDisposedException
                ? new(ErrorCode.ScopeExpired, ex.Message) : Error(ex)));
        }
    }

    private GameplayObservationBoundary ControlBoundary()
    {
        if (_controlBoundary is not null) return _controlBoundary();
        var state = _lifecycle();
        return state is null ? default : new(state.SessionId, state.SceneGeneration, state.RunId,
            state.RunsAvailable && state.Phase is RunLifecyclePhase.Playing or RunLifecyclePhase.Paused, state.Revision);
    }

    private GameplayControlObservationSnapshot CurrentControlObservation()
    {
        if (_lastControlObservation is not { } published)
            return UnavailableControlObservation(ErrorCode.NotReady, "The host has not published a gameplay control sample.");
        if (ControlObservationExpired(ControlBoundary(), _controlSource?.Invoke()))
            return UnavailableControlObservation(ErrorCode.TargetExpired, "The sampled gameplay controller, run, or source has ended.");
        return published;
    }

    private GameplayControlObservationSnapshot UnavailableControlObservation(ErrorCode code, string message)
        => new(_controlObservationRevision, null, Array.Empty<GameplayControlAvailability>(), ControlError(new(code, message)));

    private bool ControlObservationExpired(GameplayObservationBoundary boundary, object? source)
    {
        if (boundary != _publishedControlBoundary || !ReferenceEquals(source, _publishedControlSource))
            _controlObservationRevoked = true;
        return _controlObservationRevoked;
    }

    private void RevokeControlObservation()
    {
        if (_lastControlObservation?.State is not null) _controlObservationRevoked = true;
    }

    /// <summary>Runs once per admitted host Tick after lifecycle and playback maintenance. Queries only read the result.</summary>
    internal void ObserveControlsAfterMaintenance(long hostTickSequence, double now, bool requested)
    {
        _main.VerifyAccess();
        if (_disposed || _executing || _observingControls || hostTickSequence <= _lastControlTick) return;
        _lastControlTick = hostTickSequence;
        for (var index = _controlSubscriptions.Count - 1; index >= 0; index--)
        {
            var subscriber = _controlSubscriptions[index];
            if (Guard(subscriber.Context, subscriber.Cancellation) is not null) subscriber.Dispose();
        }
        if (!requested && _controlSubscriptions.Count == 0)
        {
            _lastControlObservation = null;
            _publishedControlSource = null;
            _controlObservationRevoked = false;
            RetireUnobservedCapture(now);
            return;
        }

        var boundary = ControlBoundary();
        var source = _controlSource?.Invoke();
        var previous = _lastControlObservation;
        var expired = previous is not null && ControlObservationExpired(boundary, source);
        if (previous is not null && !expired && !_controlObservationDirty && now < _nextControlObservation) return;
        _nextControlObservation = now + ControlObservationIntervalSeconds;
        _observingControls = true;
        try
        {
            GameplayControlObservationSnapshot next;
            _executing = true;
            try
            {
                var state = CaptureControlState(out var operations, out var probeError);
                next = new(0, state.Value, operations, state.Error) { ProbeError = probeError };
            }
            finally { EndExecution(); }
            if (_disposed) return;
            var finalBoundary = ControlBoundary();
            var finalSource = _controlSource?.Invoke();
            if (finalBoundary != boundary || !ReferenceEquals(finalSource, source))
                next = UnavailableControlObservation(ErrorCode.TargetExpired, "Gameplay changed during control sampling.");

            var changed = previous is null || expired || _controlObservationRevoked || !SameControlObservation(previous, next);
            var revision = changed ? ++_controlObservationRevision : _controlObservationRevision;
            var stateWithStamp = next.State is { } value ? value with
            { ObservationRevision = revision, HostTickSequence = hostTickSequence, ObservedAtMonotonicSeconds = now } : null;
            var observation = next with
            { Revision = revision, State = stateWithStamp, HostTickSequence = hostTickSequence, ObservedAtMonotonicSeconds = now };
            _publishedControlBoundary = finalBoundary;
            _publishedControlSource = finalSource;
            _controlObservationRevoked = false;
            _controlObservationDirty = finalBoundary != boundary || !ReferenceEquals(finalSource, source);
            _lastControlObservation = observation;
            if (!changed) return;

            // All subscriptions in this service are hosted on one EventHub.
            // A single event reaches them in registration order.
            foreach (var subscription in _controlSubscriptions)
            {
                if (Guard(subscription.Context, subscription.Cancellation) is not null) continue;
                subscription.Context.Events.Publish(new ControlObservationDelivery(this, observation));
                break;
            }
        }
        finally { _observingControls = false; }
    }

    private void RetireUnobservedCapture(double now)
    {
        if (_retained is not { } retained || now < _nextControlObservation) return;
        _nextControlObservation = now + 1;
        _executing = true;
        try { if (!SameCapture(retained, requireAssociation: false)) ReleaseInactiveCapture(); }
        finally { EndExecution(); }
    }

    private void DisposeControlSubscriptions()
    {
        _lastControlObservation = null;
        _publishedControlSource = null;
        foreach (var subscription in _controlSubscriptions.ToArray())
        {
            try { subscription.Dispose(); }
            catch (Exception error) { _log?.TryLog(LogLevel.Error, "Gameplay control observation cleanup failed.", error); }
        }
    }

    private static bool SameControlObservation(GameplayControlObservationSnapshot first, GameplayControlObservationSnapshot second)
        => first.Error == second.Error && first.ProbeError == second.ProbeError
            && SameControlState(first.State, second.State) && SameControlOperations(first.Operations, second.Operations);

    private static bool SameControlState(GameplayControlSnapshot? first, GameplayControlSnapshot? second)
    {
        if (first is null || second is null) return first == second;
        return first.ControllerGeneration == second.ControllerGeneration && first.ObservedRunId == second.ObservedRunId
            && first.ControllerAvailable == second.ControllerAvailable && first.LoadOver == second.LoadOver
            && first.TimeReady == second.TimeReady && first.Playing == second.Playing && first.Paused == second.Paused
            && first.LevelOver == second.LevelOver && first.Crashed == second.Crashed && first.CanDoOperation == second.CanDoOperation
            && first.CurrentOwner == second.CurrentOwner && first.Modifications.SequenceEqual(second.Modifications);
    }

    private static bool SameControlOperations(IReadOnlyList<GameplayControlAvailability> first, IReadOnlyList<GameplayControlAvailability> second)
        => first.Count == second.Count && first.Zip(second).All(pair => pair.First.Action == pair.Second.Action
            && pair.First.Supported == pair.Second.Supported && pair.First.Evidence == pair.Second.Evidence
            && pair.First.Error == pair.Second.Error && pair.First.ValueUnits.SequenceEqual(pair.Second.ValueUnits)
            && pair.First.StateReadiness?.Success == pair.Second.StateReadiness?.Success
            && pair.First.StateReadiness?.Error == pair.Second.StateReadiness?.Error);

    private sealed record ControlObservationDelivery(GameplayService Source, GameplayControlObservationSnapshot Snapshot);

    private sealed class ControlSubscription : IGameplayControlSubscription
    {
        private GameplayService? _owner;
        private Action<GameplayControlObservationSnapshot>? _handler;
        internal IPluginContext Context { get; }
        internal CancellationToken Cancellation { get; }
        internal IDisposable? Events { get; set; }
        private long _lastRevision;
        public GameplayControlObservationSnapshot InitialSnapshot { get; }

        internal ControlSubscription(GameplayService owner, IPluginContext context,
            GameplayControlObservationSnapshot initial, Action<GameplayControlObservationSnapshot> handler, CancellationToken cancellation)
            => (_owner, Context, InitialSnapshot, _lastRevision, _handler, Cancellation) = (owner, context, initial, initial.Revision, handler, cancellation);

        internal void Receive(ControlObservationDelivery delivery)
        {
            if (_owner is not { } owner || !ReferenceEquals(delivery.Source, owner)) return;
            if (owner.Guard(Context, Cancellation) is not null) { Dispose(); return; }
            var next = delivery.Snapshot;
            if (next.Revision <= _lastRevision || !ReferenceEquals(next, owner._lastControlObservation)
                || owner.ControlObservationExpired(owner.ControlBoundary(), owner._controlSource?.Invoke())) return;
            _lastRevision = next.Revision;
            try { _handler?.Invoke(next); }
            catch
            {
                // Removal also stops maintenance polling when this was the last observer.
                // Rethrow so the owned EventHub entry attributes the original fault.
                Dispose(); throw;
            }
        }

        public void Dispose()
        {
            var owner = _owner;
            if (owner is null) return;
            owner._main.VerifyAccess();
            _owner = null; _handler = null;
            owner._controlSubscriptions.Remove(this);
            var events = Events; Events = null; events?.Dispose();
        }
    }
}
