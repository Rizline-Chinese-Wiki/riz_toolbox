namespace RizToolbox.Adapter.Judgements;

/// <summary>Retains the actual MoveNext receiver across its synchronous original
/// invocation, independently of register allocation at downstream call sites.</summary>
internal sealed class HoldCoroutineContext : IDisposable
{
    private readonly Func<int> _depth;
    private Scope? _current;
    private bool _disposed;
    internal HoldCoroutineContext(Func<int> depth) => _depth = depth;

    internal Scope Enter(long coroutine, IDisposable root)
    {
        ArgumentNullException.ThrowIfNull(root);
        try
        {
            if (_disposed || coroutine == 0 || _depth() <= 0)
                throw new InvalidOperationException("No active Boolean invocation can own the hold coroutine.");
            var scope = new Scope(this, _current, coroutine, _depth(), root);
            _current = scope;
            return scope;
        }
        catch { root.Dispose(); throw; }
    }

    internal long Current()
    {
        if (_disposed || _current is not { Disposed: false } scope || scope.Depth != _depth())
            throw new InvalidOperationException("The hold judgement has no matching retained MoveNext invocation.");
        return scope.Coroutine;
    }

    // Withdrawal denies reads immediately; in-flight roots are released only
    // by their original invocation's finally, never while native code uses them.
    public void Dispose() => _disposed = true;

    internal sealed class Scope : IDisposable
    {
        private readonly HoldCoroutineContext _owner;
        private readonly Scope? _previous;
        private readonly IDisposable _root;
        internal long Coroutine { get; }
        internal int Depth { get; }
        internal bool Disposed { get; private set; }
        internal Scope(HoldCoroutineContext owner, Scope? previous, long coroutine, int depth, IDisposable root)
            => (_owner, _previous, Coroutine, Depth, _root) = (owner, previous, coroutine, depth, root);
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            if (ReferenceEquals(_owner._current, this)) _owner._current = _previous;
            else _owner._disposed = true; // Out-of-order release cannot restore a stale context.
            _root.Dispose();
        }
    }
}
