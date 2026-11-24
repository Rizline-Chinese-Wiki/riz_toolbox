namespace RizToolbox.Adapter.Gameplay;

/// <summary>Adapter composition seam: audit failures never replace an operation's result or native exception.</summary>
internal sealed class RunEffectRecorder
{
    private readonly Action<RunModificationInput> _record;
    private readonly Func<RunModificationInput, IDisposable> _begin;
    private readonly Func<RunModificationInput, IDisposable?>? _beginObservedRun;
    internal RunEffectRecorder(Action<RunModificationInput> record, Func<RunModificationInput, IDisposable> begin,
        Func<RunModificationInput, IDisposable?>? beginObservedRun = null)
        => (_record, _begin, _beginObservedRun) = (record, begin, beginObservedRun);
    internal void Record(RunModificationInput input) { try { _record(input); } catch { } }
    internal IDisposable Begin(RunModificationInput input)
    {
        try { return new SafeLease(_begin(input)); } catch { return new SafeLease(null); }
    }
    internal IDisposable? BeginObservedRun(RunModificationInput input)
    {
        try { return _beginObservedRun?.Invoke(input) is { } effect ? new SafeLease(effect) : null; } catch { return null; }
    }
    private sealed class SafeLease : IDisposable
    {
        private IDisposable? _inner;
        internal SafeLease(IDisposable? inner) => _inner = inner;
        public void Dispose() { try { Interlocked.Exchange(ref _inner, null)?.Dispose(); } catch { } }
    }
}
