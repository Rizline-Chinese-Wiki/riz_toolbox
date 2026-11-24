using RizToolbox.Adapter.Gameplay;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter;

internal sealed partial class GameEventState
{
    private PlaybackRunAssociation? _playbackRunAssociation;
    private long _playbackRunAssociationEpoch;
    private readonly object _playbackRunIssuer = new();
    private int _playbackRunMutationDepth;
    private long _playbackRunObservedScene;

    internal GameplayObservationBoundary GameplayObservationBoundary()
    {
        Verify();
        var active = _playbackRunMutationDepth == 0 && _scenesAvailable && _playbackRunObservedScene == SceneGeneration
            && RunId is { } run && MatchesCurrentGameplayRun(_information, _controller, run, SceneGeneration);
        return new(SessionId, SceneGeneration, RunId, active, Revision, _controller, _information);
    }

    /// <summary>Issue from the shared host observer, never from a public snapshot or
    /// a caller's matching record. A recovered observation gets a different object.</summary>
    internal PlaybackRunAssociation CapturePlaybackRun(long information, long controller)
    {
        Verify();
        if (_playbackRunMutationDepth != 0)
            throw new GameplayStateException(ErrorCode.NotReady, "A host controller observation is still being committed.");
        RevalidatePlaybackRunAssociation();
        if (!_scenesAvailable || SceneGeneration <= 0 || _playbackRunObservedScene != SceneGeneration || RunId is null
            || !MatchesCurrentGameplayRun(information, controller, RunId, SceneGeneration))
            throw new GameplayStateException(ErrorCode.NotReady, "No current host-observed run/scene belongs to this controller and information.");
        return _playbackRunAssociation ??= new(this, checked(++_playbackRunAssociationEpoch),
            SessionId, RunId, SceneGeneration, information, controller);
    }

    private void RevalidatePlaybackRunAssociation()
    {
        if (!_scenesAvailable || !_runsAvailable) _playbackRunObservedScene = 0;
        if (_playbackRunAssociation is { } current && (!_scenesAvailable || _playbackRunObservedScene != SceneGeneration
            || !MatchesCurrentGameplayRun(current.Information, current.Controller, current.RunId, current.SceneGeneration)))
            WithdrawPlaybackRunAssociation("The host run/scene association is no longer current.");
    }

    private void WithdrawPlaybackRunAssociation(string reason)
    {
        var previous = _playbackRunAssociation;
        // Detach before calling listeners: reentry cannot revive or recapture this object.
        _playbackRunAssociation = null;
        previous?.Withdraw(reason, _playbackRunIssuer);
    }

    private PlaybackRunMutation BeginPlaybackRunMutation()
    { ++_playbackRunMutationDepth; return new(this); }

    private readonly struct PlaybackRunMutation : IDisposable
    {
        private readonly GameEventState _owner;
        internal PlaybackRunMutation(GameEventState owner) => _owner = owner;
        public void Dispose() { --_owner._playbackRunMutationDepth; _owner.RevalidatePlaybackRunAssociation(); }
    }

    private void VerifyPlaybackRunAssociation(PlaybackRunAssociation association, NativePlaybackBinding binding)
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new GameplayStateException(ErrorCode.WrongThread, "Host playback association is owner-thread bound.");
        RevalidatePlaybackRunAssociation();
        if (!ReferenceEquals(_playbackRunAssociation, association) || association.WithdrawalReason is not null
            || association.SessionId != SessionId || !binding.Valid
            || binding.RunId != association.RunId || binding.SceneGeneration != association.SceneGeneration
            || binding.Information != association.Information || binding.Controller != association.Controller)
            throw new GameplayStateException(ErrorCode.TargetExpired, "This exact host-issued playback run association is stale, copied, or belongs to another graph.");
    }

    private void VerifyPlaybackHostAssociation(PlaybackRunAssociation association)
    {
        Verify(); RevalidatePlaybackRunAssociation();
        if (!ReferenceEquals(_playbackRunAssociation, association) || association.WithdrawalReason is not null
            || association.SessionId != SessionId || association.SceneGeneration != SceneGeneration
            || !MatchesCurrentGameplayRun(association.Information, association.Controller, association.RunId, association.SceneGeneration))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The exact host-issued initialization run is stale or copied.");
    }

    /// <summary>Only registration in this GameEventState grants authority. It proves
    /// observed run/scene identity, not unobserved native field writers, source ABA,
    /// production first-install admission, or a future Clock publication.</summary>
    internal sealed class PlaybackRunAssociation
    {
        private readonly GameEventState _owner;
        private readonly List<WithdrawalSubscription> _subscriptions = new();
        internal long Epoch { get; }
        internal string SessionId { get; }
        internal string RunId { get; }
        internal long SceneGeneration { get; }
        internal long Information { get; }
        internal long Controller { get; }
        internal string? WithdrawalReason { get; private set; }

        internal PlaybackRunAssociation(GameEventState owner, long epoch, string sessionId, string runId,
            long sceneGeneration, long information, long controller)
            => (_owner, Epoch, SessionId, RunId, SceneGeneration, Information, Controller)
                = (owner, epoch, sessionId, runId, sceneGeneration, information, controller);

        internal void VerifyCurrent(NativePlaybackBinding binding) => _owner.VerifyPlaybackRunAssociation(this, binding);
        internal void VerifyHostCurrent() => _owner.VerifyPlaybackHostAssociation(this);
        internal IDisposable OnHostWithdrawn(Action<string> handler)
        {
            ArgumentNullException.ThrowIfNull(handler); VerifyHostCurrent();
            var subscription = new WithdrawalSubscription(this, handler); _subscriptions.Add(subscription); return subscription;
        }

        internal IDisposable OnWithdrawn(NativePlaybackBinding binding, Action<string> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            VerifyCurrent(binding);
            var subscription = new WithdrawalSubscription(this, handler);
            _subscriptions.Add(subscription);
            return subscription;
        }

        internal void Withdraw(string reason, object issuer)
        {
            _owner.Verify();
            // A copied token must not notify or clear the original token's list.
            if (!ReferenceEquals(_owner._playbackRunIssuer, issuer)
                || ReferenceEquals(_owner._playbackRunAssociation, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the issuing host may withdraw its detached exact run association.");
            if (WithdrawalReason is not null) return;
            WithdrawalReason = reason;
            foreach (var subscription in _subscriptions.ToArray()) subscription.Notify(reason);
            _subscriptions.Clear();
        }

        private sealed class WithdrawalSubscription : IDisposable
        {
            private readonly PlaybackRunAssociation _association;
            private Action<string>? _handler;
            internal WithdrawalSubscription(PlaybackRunAssociation association, Action<string> handler)
                => (_association, _handler) = (association, handler);
            internal void Notify(string reason)
            {
                var handler = _handler; _handler = null;
                try { handler?.Invoke(reason); } catch { /* Every independent owner must still be withdrawn. */ }
            }
            public void Dispose()
            {
                _association._owner.Verify();
                _handler = null; _association._subscriptions.Remove(this);
            }
        }
    }
}
