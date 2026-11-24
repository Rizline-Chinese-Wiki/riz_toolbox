using System.Runtime.CompilerServices;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasTargetCapture
{
    /// <summary>Creates the public responsibility owner before acquiring a native
    /// source, so even a partially constructed reader with an unknown free has an
    /// exact retained public owner. Source creation and every exit are bounded.</summary>
    internal static NativeSeekCanvasTargetCapture Capture(Func<IRetainedSeekCanvasTargetReader> create,
        float chartTime, float speed)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (!float.IsFinite(chartTime) || !float.IsFinite(speed))
            throw new ArgumentOutOfRangeException(nameof(chartTime), "Canvas target chart-time and speed must be finite.");
        var value = new NativeSeekCanvasTargetCapture(); value.ReserveOwner();
        try
        {
            value._source = create() ?? throw new InvalidOperationException("The independent actual Canvas source was not created.");
            value._source.VerifyOwnerThread(); value._source.BindLifetimeGuard(value.Lifetime);
            value.Operation(() =>
            {
                value.Access(); var first = value.Read(); var second = value.Read();
                if (!SeekCanvasSourceState.Same(first, second))
                    throw new InvalidOperationException("The complete actual Canvas source changed between capture observations.");
                value._baseline = second;
                var moves = second.Moves.Select(move => new SeekCanvasTargetMove(move.Reference, move.Index,
                    move.Speed.Points, move.X.Points)).ToArray();
                var request = new SeekCanvasTargetRequest(chartTime, speed, second.ScreenWidth, second.Down, second.Right);
                value._plan = NativeSeekCanvasTargetPlan.Capture(Array.AsReadOnly(moves), request,
                    value.Interval, value.Ease, value.Local);
                value.CompareSource(); value.Local(); return 0;
            });
            return value;
        }
        catch (Exception error)
        {
            if (value._source is null && FindConstructionCleanup(error) is Exception cleanup)
            {
                value._disposed = true; value._constructionFailure = error;
                value._constructionOwner = FindConstructionOwner(error); value.RecordCleanup(cleanup);
                if (value._constructionOwner is not null) error.Data["SeekCanvasSourceNativeCleanupOwner"] = value._constructionOwner;
                AttachCleanup(error, value, cleanup);
            }
            else
            {
                try { value.Dispose(); }
                catch (Exception cleanupError) { AttachCleanup(error, value, cleanupError); }
            }
            throw;
        }
    }

    private static Exception? FindConstructionCleanup(Exception error)
    {
        foreach (var prefix in new[] { "SeekCanvasSource", "SeekNoteFlags", "SeekRuntimeNote", "SeekChart" })
            if (error.Data[prefix + "CleanupFailure"] is Exception cleanup) return cleanup;
        return null;
    }
    private static object? FindConstructionOwner(Exception error)
    {
        foreach (var prefix in new[] { "SeekCanvasSource", "SeekNoteFlags", "SeekRuntimeNote", "SeekChart" })
            if (error.Data[prefix + "CleanupOwner"] is object owner) return owner;
        return null;
    }
    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("The actual Canvas capture belongs to its owner thread.");
        _source?.VerifyOwnerThread();
    }
    private void Lifetime()
    {
        VerifyThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekCanvasTargetCapture));
        if (_faulted) throw new InvalidOperationException("The original actual Canvas source was already observed expired.");
    }
    private void Local() { Lifetime(); _source!.VerifyLocal(); Lifetime(); }
    private void Access() { Local(); _source!.VerifyAccess(); Local(); }
    private T Operation<T>(Func<T> operation)
    {
        VerifyThread();
        if (_busy || _releasing) throw new InvalidOperationException("An actual Canvas capture operation cannot synchronously reenter itself.");
        Lifetime(); _busy = true;
        try { Local(); var result = operation(); Local(); return result; }
        finally { _busy = false; }
    }
    private SeekCanvasSourceSnapshot Read()
    {
        Local(); var raw = _source!.ReadSnapshot(); Local();
        var copy = SeekCanvasSourceState.Copy(raw, Local); Local(); return copy;
    }
    private void CompareSource()
    {
        Access(); var first = Read(); var second = Read();
        if (!SeekCanvasSourceState.Same(first, second) || !SeekCanvasSourceState.Same(second, _baseline))
            throw new InvalidOperationException("The original retained Canvas arrays, Lists, keypoints, static vectors or initialized Ease source changed.");
        Local();
    }
    private float Interval(double from, double to)
    {
        CompareSource(); Local(); var duration = _source!.ChartTimeRange(from, to); Local();
        CompareSource(); return Finite(duration);
    }
    private float Ease(float progress, int type)
    {
        Local();
        if ((uint)type >= (uint)_baseline.Eases.Count)
            throw new InvalidDataException("The selected actual Ease row is not initialized.");
        var samples = _baseline.Eases[type].Samples;
        // Match native cvttss2si and the separate Single mul/sub/mul/add
        // sequence over the observed table. No native easing initializer runs.
        var scaled = Finite(progress * 100f);
        if ((double)scaled < int.MinValue || (double)scaled > int.MaxValue)
            throw new NotSupportedException("The actual Canvas Ease index is not representable by the native conversion.");
        var index = (int)scaled;
        if (index < 0) return samples[0];
        if (index >= 100) return samples[100];
        var fraction = Finite(scaled - (float)index);
        var difference = Finite(samples[index + 1] - samples[index]);
        var product = Finite(fraction * difference);
        var result = Finite(product + samples[index]); Local(); return result;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float Finite(float value)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException("An actual Canvas numerical producer returned a nonfinite or overflowed Single value.");
        return value;
    }
    internal void VerifySource() => Operation(() =>
    {
        try { CompareSource(); return 0; }
        catch (Exception error)
        {
            if (!SeekCanvasBasisCallerWithdrawal.Is(error)) _faulted = true;
            throw;
        }
    });
    private void ReserveOwner()
    {
        lock (OwnerGate)
        {
            if (Owners.Count >= MaximumUnclosedOwners)
                throw new NotSupportedException("The bounded actual Canvas source owner capacity is exhausted before native acquisition.");
            Owners.Add(this); _ownerReserved = true;
        }
    }
    private void ReleaseOwner()
    {
        lock (OwnerGate)
        {
            if (!_ownerReserved) return;
            Owners.Remove(this); _ownerReserved = false;
        }
    }
    private static void AttachCleanup(Exception error, NativeSeekCanvasTargetCapture owner, Exception cleanup)
    {
        error.Data["SeekCanvasSourceCleanupOwner"] = owner;
        error.Data["SeekCanvasSourceCleanupFailure"] = cleanup;
    }
    private void RecordCleanup(Exception error)
    { if (!_cleanupFailures.Contains(error)) _cleanupFailures.Add(error); }
    private Exception CleanupError()
    {
        var error = new AggregateException("The actual Canvas source retains unknown cleanup responsibility without native retry.", _cleanupFailures);
        AttachCleanup(error, this, _cleanupFailures[0]);
        if (_constructionOwner is not null) error.Data["SeekCanvasSourceNativeCleanupOwner"] = _constructionOwner;
        if (_constructionFailure is not null) error.Data["SeekCanvasSourceConstructionFailure"] = _constructionFailure;
        return error;
    }
    public void Dispose()
    {
        VerifyThread();
        // Nested disposal must not consume the valid outer verification owner.
        if (_busy || _releasing) throw new InvalidOperationException("Actual Canvas disposal cannot synchronously reenter an active capture operation.");
        var first = !_disposed; _disposed = true;
        if (first && _source is not null)
            try { _source.RequestDispose(); }
            catch (Exception error) { RecordCleanup(error); }
        if (!_released && !_releaseAttempted && _constructionFailure is null)
        {
            _releaseAttempted = true; _releasing = true;
            try { _source?.Dispose(); _released = true; }
            catch (Exception error) { RecordCleanup(error); }
            finally { _releasing = false; }
        }
        if (_cleanupFailures.Count != 0) throw CleanupError();
        ReleaseOwner();
    }
}
