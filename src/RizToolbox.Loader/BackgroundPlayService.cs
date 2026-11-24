using RizToolbox.Sdk;

namespace RizToolbox.Loader;

internal sealed class BackgroundPlayService
{
    private Request? _active;
    internal IBackgroundPlayService ForPlugin(IPluginContext context) => new Request(this, context);

    private sealed class Request : IBackgroundPlayService, IDisposable
    {
        private readonly BackgroundPlayService _owner;
        private readonly IPluginContext _context;
        private GameWindowActivation? _window;
        private IGameLifecycleSubscription? _subscription;

        internal Request(BackgroundPlayService owner, IPluginContext context)
        { _owner = owner; _context = context; }

        public Result EnableForScope()
        {
            if (!_context.MainThread.IsMainThread) return Fail(ErrorCode.WrongThread, "Enable on the Unity host thread.");
            if (_context is not IScopedPluginContext scoped || scoped.Lifetime.IsCancellationRequested)
                return Fail(ErrorCode.ScopeExpired, "The plugin scope has ended.");
            if (_owner._active is { } current)
                return ReferenceEquals(current._context, _context) ? Result.Ok() : Fail(ErrorCode.Conflict, "Another plugin owns background play.");
            var lifecycle = _context.GetService<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle);
            if (lifecycle.Service is null) return Result.Fail(lifecycle.Error!);
            try
            {
                _window = GameWindowActivation.Attach();
                _owner._active = this;
                var subscribed = lifecycle.Service.Subscribe(change => _window?.SetScene(change.Snapshot.Scene));
                if (!subscribed.Success)
                {
                    Dispose();
                    return Result.Fail(subscribed.Error!);
                }
                _subscription = subscribed.Value!;
                _window.SetScene(_subscription.InitialSnapshot.Scene);
                _context.Own(this);
                return Result.Ok();
            }
            catch (Exception error)
            {
                Dispose();
                return Fail(ErrorCode.CapabilityUnavailable, error.Message);
            }
        }

        private static Result Fail(ErrorCode code, string message) => Result.Fail(new OperationError(code, message, BackgroundPlayCapability.Id));

        public void Dispose()
        {
            try { _subscription?.Dispose(); }
            finally
            {
                _subscription = null;
                _window?.Dispose();
                _window = null;
                if (ReferenceEquals(_owner._active, this)) _owner._active = null;
            }
        }
    }
}
