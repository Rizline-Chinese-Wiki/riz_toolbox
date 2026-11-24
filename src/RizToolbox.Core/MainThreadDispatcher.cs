using System.Collections.Concurrent;
using RizToolbox.Sdk;

namespace RizToolbox.Core;

public sealed class MainThreadDispatcher : IMainThreadDispatcher, IDisposable
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly int _maxPending;
    private readonly ConcurrentQueue<WorkItem> _queue = new();
    private readonly object _gate = new();
    private int _pending;
    private bool _disposed;
    private long _executed, _cancelled, _rejected;
    private int _peakPending;

    private sealed class WorkItem
    {
        public Action Action { get; init; } = null!;
        public CancellationToken Token { get; init; }
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Cancellation;
        private int _state; // 0 queued, 1 running, 2 complete, 3 cancelled before execution
        public bool IsCancelled => Volatile.Read(ref _state) == 3;
        public bool TryStart() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
        public bool Cancel()
        {
            if (Interlocked.CompareExchange(ref _state, 3, 0) != 0) return false;
            Completion.TrySetCanceled(Token.IsCancellationRequested ? Token : new CancellationToken(true));
            return true;
        }
        public bool CancelBeforeAction()
        {
            if (Interlocked.CompareExchange(ref _state, 3, 1) != 1) return false;
            Completion.TrySetCanceled(Token);
            return true;
        }
        public void Complete(Exception? error = null)
        {
            Volatile.Write(ref _state, 2);
            if (error is null) Completion.TrySetResult(true);
            else Completion.TrySetException(error);
        }
    }

    public MainThreadDispatcher(int maxPending = 256)
    {
        if (maxPending <= 0) throw new ArgumentOutOfRangeException(nameof(maxPending));
        _maxPending = maxPending;
    }

    public bool IsMainThread => Environment.CurrentManagedThreadId == _threadId;
    public int PendingCount => Volatile.Read(ref _pending);
    public DispatcherSnapshot GetSnapshot() => new(PendingCount, Volatile.Read(ref _peakPending),
        Interlocked.Read(ref _executed), Interlocked.Read(ref _cancelled), Interlocked.Read(ref _rejected));

    public void VerifyAccess()
    {
        if (!IsMainThread) throw new InvalidOperationException("This operation must run on the recorded game main thread.");
    }

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
        lock (_gate)
        {
            if (_disposed) return Task.FromException(new ObjectDisposedException(nameof(MainThreadDispatcher)));
            if (!IsMainThread)
            {
                // Cancelled heads no longer consume queue capacity until a future game frame.
                while (_queue.TryPeek(out var head) && head.IsCancelled)
                {
                    _queue.TryDequeue(out _); Interlocked.Decrement(ref _pending); head.Cancellation.Dispose();
                }
                if (_pending >= _maxPending)
                {
                    Interlocked.Increment(ref _rejected);
                    return Task.FromException(new DispatcherQueueFullException());
                }
                var item = new WorkItem { Action = action, Token = cancellationToken };
                item.Cancellation = cancellationToken.Register(() => { if (item.Cancel()) Interlocked.Increment(ref _cancelled); });
                Interlocked.Increment(ref _pending);
                _peakPending = Math.Max(_peakPending, _pending);
                _queue.Enqueue(item);
                return item.Completion.Task;
            }
        }
        try { action(); Interlocked.Increment(ref _executed); return Task.CompletedTask; }
        catch (Exception ex) { return Task.FromException(ex); }
    }

    /// <summary>Called from the loader's Update callback. Returns the number of dequeued requests.</summary>
    public int Pump(int maxWorkItems = 64)
    {
        VerifyAccess();
        if (maxWorkItems <= 0) throw new ArgumentOutOfRangeException(nameof(maxWorkItems));
        int count = 0;
        while (count < maxWorkItems)
        {
            WorkItem item;
            lock (_gate)
            {
                if (_disposed || !_queue.TryDequeue(out item!)) break;
                Interlocked.Decrement(ref _pending);
            }
            count++;
            try
            {
                if (!item.TryStart()) continue;
                // Token cancellation can be observable before its queued callback executes.
                if (item.Token.IsCancellationRequested)
                {
                    if (item.CancelBeforeAction()) Interlocked.Increment(ref _cancelled);
                    continue;
                }
                item.Action();
                Interlocked.Increment(ref _executed);
                item.Complete();
            }
            catch (Exception ex) { item.Complete(ex); }
            finally { item.Cancellation.Dispose(); }
        }
        return count;
    }

    public void Dispose()
    {
        VerifyAccess();
        lock (_gate)
        {
            _disposed = true;
            while (_queue.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref _pending);
                if (item.Cancel()) Interlocked.Increment(ref _cancelled);
                item.Cancellation.Dispose();
            }
        }
    }
}

public sealed class DispatcherQueueFullException : InvalidOperationException
{
    public DispatcherQueueFullException() : base("The main-thread request queue is full.") { }
}

public sealed record DispatcherSnapshot(int Pending, int PeakPending, long Executed, long Cancelled, long Rejected);
