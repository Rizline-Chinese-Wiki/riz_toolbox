using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

internal sealed partial class GamePostProcessingModule
{
    private const int MaximumIntents = 128, MaximumIntentsPerPlugin = 32;
    // Reacquire in registration order so the coordinator keeps equal-priority precedence.
    private readonly List<BlurIntent> _intents = new();
    private bool _ticking;

    private OperationError? AccessError(object registration, IPluginContext context)
    {
        if (!_main.IsMainThread) return Error(ErrorCode.WrongThread, "The blur target belongs to the host thread.");
        if (!ReferenceEquals(registration, _registration) || context is IScopedPluginContext scoped && scoped.Lifetime.IsCancellationRequested)
            return Error(ErrorCode.ScopeExpired, "The post-processing provider or plugin scope ended.");
        return null;
    }

    private Result<IGameBlurBindingIntent> BindDownsample(object registration, IPluginContext context,
        TargetQuery query, int value, int priority)
    {
        if (AccessError(registration, context) is { } access) return Result<IGameBlurBindingIntent>.Fail(access);
        const string role = GamePostProcessingRoles.MainCameraBlurDownsample;
        if (query is null || query.Scene is not ("MainMenu" or "GameStart") || query.IncludeTemplates || !query.ActiveOnly
            || query.ContentId is not null || query.Role is not (null or role) || query.ResourceId is not (null or role)
            || (query.Role is null && query.Key is null && query.ResourceId is null)
            || (query.Key is not (null or role) && query.Key != role + "/" + query.Scene) || value is < Minimum or > Maximum)
            return Result<IGameBlurBindingIntent>.Fail(Error(ErrorCode.InvalidArgument,
                "Select the active blur role or stable key in GameStart or MainMenu, with a downsample from 0 to 2."));
        if (_intents.Count >= MaximumIntents || _intents.Count(intent => intent.PluginId == context.PluginId) >= MaximumIntentsPerPlugin)
            return Result<IGameBlurBindingIntent>.Fail(Error(ErrorCode.QuotaExceeded, "Too many live blur binding intents."));

        var intent = new BlurIntent(this, registration, context, query, value, priority);
        _intents.Add(intent);
        try { return Result<IGameBlurBindingIntent>.Ok(context.Own(intent)); }
        catch (Exception error)
        {
            var failure = AccessError(registration, context) ?? Error(ErrorCode.InternalError, error.Message);
            intent.End(failure);
            return Result<IGameBlurBindingIntent>.Fail(failure);
        }
    }

    private void ReconcileIntents()
    {
        foreach (var intent in _intents.ToArray()) intent.Reconcile(_binding, _unavailable);
    }

    private void StopIntents()
    {
        var reason = Error(ErrorCode.ScopeExpired, "The post-processing provider ended.");
        foreach (var intent in _intents.ToArray()) intent.End(reason);
    }

    private void StopInvalidatedIntents(TargetHandle<int> target, OperationError reason)
    {
        foreach (var intent in _intents.ToArray())
            if (intent.LastSnapshot.Target == target) intent.End(reason);
    }

    private sealed class BlurIntent : IGameBlurBindingIntent
    {
        private readonly GamePostProcessingModule _owner;
        private readonly object _registration;
        private readonly IPluginContext _context;
        private IMutationLease<int>? _layer;
        private GameBlurBindingSnapshot _snapshot;
        private bool _stopped;
        internal string PluginId => _context.PluginId;
        public GameBlurBindingSnapshot LastSnapshot => Volatile.Read(ref _snapshot);

        internal BlurIntent(GamePostProcessingModule owner, object registration, IPluginContext context,
            TargetQuery query, int value, int priority)
        {
            (_owner, _registration, _context) = (owner, registration, context);
            _snapshot = new(1, VisualBindingState.Waiting, query, value, priority, null,
                Error(ErrorCode.NotReady, "The next blur discovery will resolve this intent.", true));
        }

        public Result Stop()
        {
            if (!_owner._main.IsMainThread) return Result.Fail(Error(ErrorCode.WrongThread, "Blur binding stops on the host thread."));
            End(_owner.AccessError(_registration, _context) ?? Error(ErrorCode.Cancelled, "The blur binding was stopped."));
            return LastSnapshot.Error is { Code: ErrorCode.MutationWriteFailed } failure ? Result.Fail(failure) : Result.Ok();
        }

        public void Dispose()
        {
            _owner._main.VerifyAccess();
            _ = Stop();
        }

        internal void End(OperationError reason)
        {
            if (_stopped) return;
            _stopped = true;
            _owner._intents.Remove(this);
            Publish(VisualBindingState.Stopped, null, reason);
            ReleaseLayer();
        }

        internal void Reconcile(Binding? binding, OperationError? unavailable)
        {
            if (!StillLive()) return;
            try
            {
                var scene = _snapshot.Query.Scene;
                if (binding is null || binding.Source.Identity.Scene != scene)
                {
                    var ambiguous = unavailable?.Code == ErrorCode.AmbiguousTarget
                        && unavailable.TargetId == GamePostProcessingRoles.MainCameraBlurDownsample + "/" + scene;
                    var observationFailure = unavailable?.Code != ErrorCode.AmbiguousTarget ? unavailable : null;
                    Publish(ambiguous ? VisualBindingState.Ambiguous : VisualBindingState.Waiting, null,
                        (ambiguous ? unavailable : observationFailure)
                        ?? Error(ErrorCode.TargetMissing, "A unique active blur target has not appeared in the selected scene.", true));
                    ReleaseLayer();
                    return;
                }

                if (_layer is { } previous && _snapshot.Target == binding.Handle)
                {
                    if (!previous.IsActive)
                        End(Error(ErrorCode.Conflict, "The current blur layer ended while its target was still selected."));
                    return;
                }

                Publish(VisualBindingState.Waiting, null, Error(ErrorCode.NotReady, "Acquiring the replacement blur target.", true));
                ReleaseLayer();
                if (!StillLive() || !ReferenceEquals(binding, _owner._binding) || !binding.Channel.Valid) return;

                // The registered intent owns this layer directly. Plugin scope
                // retirement already knows the intent even if the setter reenters.
                var result = binding.Channel.Override(PluginId, _snapshot.Value, _snapshot.Priority);
                if (result.Success) _layer = result.Value!;
                if (!StillLive() || !ReferenceEquals(binding, _owner._binding))
                {
                    ReleaseLayer();
                    return;
                }
                if (!result.Success)
                {
                    var error = result.Error! with { CapabilityId = Capability };
                    if (error.Code is ErrorCode.TargetExpired or ErrorCode.MutationReentrancy)
                        Publish(VisualBindingState.Waiting, null, error);
                    else End(error);
                    return;
                }
                if (_layer!.IsActive) Publish(VisualBindingState.Bound, binding.Handle, null);
                else End(Error(ErrorCode.Conflict, "The blur layer ended before its acquisition could be published."));
            }
            catch (Exception error) { End(Error(ErrorCode.MutationWriteFailed, error.Message)); }
        }

        private bool StillLive()
        {
            if (_stopped) return false;
            if (_owner.AccessError(_registration, _context) is not { } error) return true;
            End(error);
            return false;
        }

        private void ReleaseLayer()
        {
            var previous = _layer;
            _layer = null;
            try { previous?.Dispose(); }
            catch (Exception error)
            {
                var failure = Error(ErrorCode.MutationWriteFailed, error.Message);
                if (_stopped) Publish(VisualBindingState.Stopped, null, failure);
                else End(failure);
            }
        }

        private void Publish(VisualBindingState state, TargetHandle<int>? target, OperationError? error)
        {
            var current = _snapshot;
            if (current.State != state || current.Target != target || current.Error != error)
                Volatile.Write(ref _snapshot, current with { Revision = current.Revision + 1, State = state, Target = target, Error = error });
        }
    }
}
