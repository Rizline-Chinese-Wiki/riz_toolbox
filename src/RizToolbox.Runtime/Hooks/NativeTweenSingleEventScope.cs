namespace RizToolbox.Runtime.Hooks;

/// <summary>The actual operation broker retains this scope through native Exit and context disposal.
/// Logical subscription withdrawal cannot replace that once-only cleanup duty.</summary>
internal sealed class NativeTweenSingleEventScope<T> : IDisposable where T : class
{
    private readonly NativeTweenSingleEventSubscriptions<T>.ParentLoan _loan;
    private readonly NativeOperationFrame _frame;
    private bool _exited, _disposed;
    internal IDisposable Inner { get; }

    internal NativeTweenSingleEventScope(IDisposable inner, NativeOperationFrame frame,
        NativeTweenSingleEventSubscriptions<T>.ParentLoan loan)
    {
        ArgumentNullException.ThrowIfNull(inner); ArgumentNullException.ThrowIfNull(loan);
        (Inner, _frame, _loan) = (inner, frame, loan); loan.Bind();
    }
    internal void Complete(NativeOperationFrame frame, NativeOperationExitFlags flags,
        Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags> complete)
    {
        try
        {
            if (_exited || _disposed || !Same(frame, _frame)
                || flags is not (NativeOperationExitFlags.Returned or NativeOperationExitFlags.Abnormal or NativeOperationExitFlags.Skipped))
                throw new InvalidOperationException("Single scope completion is not its once-only actual parent exit.");
            _exited = true;
            complete(Inner, frame, flags);
        }
        catch (Exception error) { _loan.FaultCompletion("Single parent completion failed: " + error.Message); throw; }
    }
    public void Dispose()
    {
        if (_disposed)
        {
            _loan.FaultCompletion("Single parent context was disposed twice; cleanup cannot be repeated.");
            throw new InvalidOperationException("Single parent context was disposed twice.");
        }
        _disposed = true;
        try { Inner.Dispose(); }
        catch (Exception error) { _loan.FaultCompletion("Single parent context cleanup failed: " + error.Message); throw; }
        finally
        {
            if (_exited) _loan.Close();
            else _loan.CaptureFault("Single context disposal has no confirmed actual parent exit; responsibility remains retained.");
        }
    }
    private static bool Same(NativeOperationFrame a, NativeOperationFrame b)
        => a.Version == b.Version && a.Size == b.Size && a.Kind == b.Kind && a.Reserved == b.Reserved
            && a.Self == b.Self && a.Arg0 == b.Arg0 && a.Arg1 == b.Arg1 && a.Method == b.Method && a.Caller == b.Caller && a.Sequence == b.Sequence;
}
