using System.Diagnostics;

namespace RizToolbox.Core.Diagnostics;

/// <summary>One consumer with one replaceable pending copy. Callers publish immutable managed data only.</summary>
public sealed class LatestDiagnosticWriter<T> : IDisposable where T : class
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _available = new(0, 1);
    private readonly Func<T, Task> _persist;
    private readonly Task _worker;
    private T? _latest;
    private long _latestRevision, _lastPublished, _lastAttempt, _lastCompleted, _lastWritten;
    private long _published, _coalesced, _rejected, _failures;
    private int _running, _peak;
    private bool _closed, _completed;
    private string? _lastError;

    public LatestDiagnosticWriter(Func<T, Task> persist)
    {
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _worker = Task.Run(ConsumeAsync);
    }

    /// <summary>Accepts increasing positive revisions. A terminal copy seals input while pending work finishes.</summary>
    public bool Publish(long revision, T immutableCopy, bool terminal = false)
    {
        ArgumentNullException.ThrowIfNull(immutableCopy);
        lock (_gate)
        {
            if (_closed || revision <= _lastPublished) { _rejected++; return false; }
            if (_latest is not null) _coalesced++;
            _latest = immutableCopy;
            _latestRevision = _lastPublished = revision;
            _published++;
            _peak = Math.Max(_peak, _running + 1);
            if (terminal) _closed = true;
            if (_running == 0) WakeConsumer();
            return true;
        }
    }

    public LatestDiagnosticWriterSnapshot GetSnapshot()
    {
        lock (_gate)
            return new(_latest is null ? 0 : 1, _running, _peak, _published, _coalesced, _rejected, _failures,
                _lastPublished, _lastAttempt, _lastCompleted, _lastWritten, _closed, _completed, _lastError);
    }

    /// <summary>Waits without holding the bookkeeping lock. Attempt completion and successful persistence are distinct.</summary>
    public LatestDiagnosticWaitResult WaitThrough(long revision, TimeSpan timeout)
    {
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        var started = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            while (_lastCompleted < revision && !_completed)
            {
                var elapsed = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
                var remaining = timeout.TotalMilliseconds - elapsed * 1000;
                if (remaining <= 0) break;
                Monitor.Wait(_gate, (int)Math.Max(1, Math.Ceiling(remaining)));
            }
            return new(revision, _lastCompleted >= revision, _lastWritten >= revision, _completed,
                _lastCompleted, _lastWritten, _lastError);
        }
    }

    public void RequestComplete()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            if (_running == 0) WakeConsumer();
        }
    }

    /// <summary>A slow synchronous or asynchronous persistence operation keeps its resources until it actually returns.</summary>
    public void Dispose()
    {
        RequestComplete();
        _worker.Wait(TimeSpan.FromMilliseconds(250));
    }

    // All releases are under _gate; only the completed consumer disposes this semaphore.
    private void WakeConsumer()
    {
        if (_available.CurrentCount == 0) _available.Release();
    }

    private async Task ConsumeAsync()
    {
        try
        {
            while (true)
            {
                T? copy;
                long revision;
                lock (_gate)
                {
                    copy = _latest;
                    revision = _latestRevision;
                    if (copy is null)
                    {
                        if (_closed) return;
                    }
                    else
                    {
                        _latest = null;
                        _latestRevision = 0;
                        _running = 1;
                        _lastAttempt = revision;
                    }
                }
                if (copy is null) { await _available.WaitAsync().ConfigureAwait(false); continue; }

                string? failure = null;
                try { await _persist(copy).ConfigureAwait(false); }
                catch (Exception error) { failure = BoundError(error); }
                finally
                {
                    // Do not keep the previously completed copy alive while waiting for future input.
                    copy = null;
                    lock (_gate)
                    {
                        _running = 0;
                        _lastCompleted = revision;
                        if (failure is null) _lastWritten = revision;
                        else { _failures++; _lastError = failure; }
                        Monitor.PulseAll(_gate);
                    }
                }
            }
        }
        catch (Exception error)
        {
            var failure = BoundError(error);
            lock (_gate) { _failures++; _lastError = failure; }
        }
        finally
        {
            lock (_gate)
            {
                _closed = true;
                _running = 0;
                _latest = null;
                _latestRevision = 0;
                _available.Dispose();
                _completed = true;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private static string BoundError(Exception error)
    {
        try
        {
            var name = error.GetType().Name;
            if (name.Length > 128) name = name[..128];
            var message = error.Message ?? string.Empty;
            var length = Math.Min(message.Length, 512 - name.Length - 2);
            var text = name + ": " + message[..length];
            var characters = text.ToCharArray();
            for (var i = 0; i < characters.Length; i++) if (char.IsControl(characters[i])) characters[i] = ' ';
            return new string(characters);
        }
        catch { return "Diagnostic persistence failed (error text unavailable)."; }
    }
}

public sealed record LatestDiagnosticWriterSnapshot(int Queued, int Running, int Peak, long Published,
    long Coalesced, long Rejected, long Failures, long LastPublished, long LastAttempt, long LastCompleted,
    long LastWritten, bool Closed, bool Completed, string? LastError);

public sealed record LatestDiagnosticWaitResult(long Revision, bool Completed, bool Persisted, bool WriterCompleted,
    long LastCompleted, long LastWritten, string? LastError);
