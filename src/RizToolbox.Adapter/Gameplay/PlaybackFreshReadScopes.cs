using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

// Counts actual managed read scopes only. This object issues no source, watch,
// root or Clock authority; the capture privately owns it alongside those owners.
internal sealed class PlaybackFreshReadScopes
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly HashSet<Scope> _active = new(ReferenceEqualityComparer.Instance);
    private bool _closed;
    internal int Active { get { VerifyThread(); return _active.Count; } }
    private void VerifyThread()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new GameplayStateException(ErrorCode.WrongThread, "Fresh graph reads belong to the capture owner thread.");
    }
    internal IDisposable Enter()
    {
        VerifyOpen();
        var scope = new Scope(this); _active.Add(scope); return scope;
    }
    internal void VerifyOpen()
    {
        VerifyThread();
        if (_closed) throw new GameplayStateException(ErrorCode.TargetExpired, "Fresh graph reads have closed.");
    }
    internal void Close() { VerifyThread(); _closed = true; }
    private sealed class Scope : IDisposable
    {
        private readonly PlaybackFreshReadScopes _owner;
        private readonly Scope _registered;
        private bool _released;
        internal Scope(PlaybackFreshReadScopes owner) => (_owner, _registered) = (owner, this);
        public void Dispose()
        {
            _owner.VerifyThread();
            if (!ReferenceEquals(_registered, this)) throw new GameplayStateException(ErrorCode.TargetExpired, "A copied read scope cannot release its owner.");
            if (_released) return;
            if (!_owner._active.Remove(this)) throw new GameplayStateException(ErrorCode.TargetExpired, "The actual read scope is not registered.");
            _released = true;
        }
    }
}
