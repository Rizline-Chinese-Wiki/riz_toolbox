using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackFreshSourceCapture
{
    private void HostWithdrawn(string reason)
    {
        Registered();
        try { _adoption?.HostWithdrawn(reason); }
        finally { Invalidate(reason); }
    }

    internal sealed partial class Adoption
    {
        internal bool CanRetainInitialization { get { Registered(); return !_capture._closed; } }

        internal void HostWithdrawn(string reason)
        { Registered(); _router.FreshHostWithdrawn(this, reason); }

        internal object InitializationWatchOwner(NativePlaybackStockRootedSource source)
        { VerifySourceRegistration(source); return _capture; }

        internal IDisposable RetainForInitialization(NativePlaybackStockClockRouter router,
            NativePlaybackInitializationTracker initializer)
        {
            Registered();
            if (!ReferenceEquals(_router, router))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Another route cannot retain the initializer's predecessor roots.");
            router.VerifyFreshInitializer(this, initializer);
            return _capture.EnterRead();
        }
    }
}
