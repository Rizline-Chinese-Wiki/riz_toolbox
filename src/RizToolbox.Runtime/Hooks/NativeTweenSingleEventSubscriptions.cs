namespace RizToolbox.Runtime.Hooks;

internal readonly record struct NativeTweenSingleParentKey(ulong Epoch, ulong ParentEpoch, ulong Sequence, ulong ManagedToken,
    IntPtr Event, IntPtr Method, IntPtr Caller, uint ValueBits, uint Thread)
{
    internal static NativeTweenSingleParentKey From(NativeTweenSingleEventFrame frame) => new(frame.Epoch, frame.ParentEpoch,
        frame.ParentSequence, frame.ParentManagedToken, frame.Event, frame.ParentMethod, frame.ParentCaller, frame.ValueBits, frame.ThreadId);
}

/// <summary>Snapshot/withdrawal bookkeeping, not a native authority issuer. Production enrollment requires
/// the bridge's exact actual broker capture; notification requires its current native Delivery.</summary>
internal sealed class NativeTweenSingleEventSubscriptions<T> where T : class
{
    private readonly object _gate = new();
    private readonly HashSet<Subscription> _active = new();
    private readonly Dictionary<NativeTweenSingleParentKey, List<ParentLoan>> _parents = new();
    private const int ParentLimit = 4096;
    private int _loans;

    internal Subscription Subscribe(Action<T> observe, Action<string> fault)
    {
        ArgumentNullException.ThrowIfNull(observe); ArgumentNullException.ThrowIfNull(fault);
        lock (_gate) { var subscription = new Subscription(this, observe, fault); _active.Add(subscription); return subscription; }
    }
    internal ParentLoan Begin(Subscription subscription, NativeTweenSingleParentKey key)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(subscription.Owner, this) || !_active.Contains(subscription) || subscription.Closed || subscription.Unknown
                || subscription.Observe is null || subscription.Fault is null || key.Sequence == 0 || key.ManagedToken == 0)
                throw new InvalidOperationException("A withdrawn or Unknown Single subscription cannot borrow a new parent.");
            if (_loans >= ParentLimit) throw new InvalidOperationException("Single parent snapshot responsibility capacity exhausted.");
            if (!_parents.TryGetValue(key, out var parents)) _parents.Add(key, parents = new());
            if (parents.Any(parent => ReferenceEquals(parent.Subscription, subscription)))
                throw new InvalidOperationException("The same Single subscription captured one physical parent twice.");
            var loan = new ParentLoan(this, subscription, key, subscription.Observe, subscription.Fault);
            parents.Add(loan); ++subscription.Loans; ++_loans; return loan;
        }
    }
    internal uint Dispatch(NativeTweenSingleParentKey key, T value)
    {
        ParentLoan[] snapshot;
        lock (_gate)
        {
            if (!_parents.TryGetValue(key, out var parents)) return 0; // Foreign/new-run parent: no object callbacks.
            snapshot = parents.Where(parent => !parent.Released && !parent.ParentClosed).ToArray();
            foreach (var loan in snapshot) { ++loan.Callbacks; ++loan.Subscription.Callbacks; }
        }
        uint failed = 0;
        foreach (var loan in snapshot)
        {
            try
            {
                Action<T> callback;
                lock (_gate)
                {
                    // Every callback reserved by this snapshot still runs if a peer closes the parent
                    // or withdraws a scope. A later reentrant Dispatch cannot enroll the closed parent.
                    if (!loan.Bound || loan.CaptureFailed || loan.Observe is not { } actual)
                        throw new InvalidOperationException("A Single parent snapshot was not completely bound before native delivery.");
                    callback = actual;
                }
                callback(value);
            }
            catch (Exception error)
            {
                failed = 1; Action<string>? fault;
                lock (_gate) { loan.Failed = true; loan.Subscription.Unknown = true; fault = loan.Fault; }
                try { fault?.Invoke("Single captured callback failed: " + error.Message); } catch { }
            }
            finally
            {
                lock (_gate)
                {
                    --loan.Callbacks; --loan.Subscription.Callbacks;
                    TryRelease(loan);
                }
            }
        }
        return failed;
    }
    internal void FaultAll(string reason)
    {
        Action<string>[] callbacks;
        lock (_gate)
        {
            var active = _active.Select(subscription => subscription.Fault);
            var retained = _parents.Values.SelectMany(parents => parents).Select(parent => parent.Fault);
            callbacks = active.Concat(retained).Where(callback => callback is not null).Distinct().Cast<Action<string>>().ToArray();
            foreach (var subscription in _active) subscription.Unknown = true;
            foreach (var parent in _parents.Values.SelectMany(parents => parents)) parent.Subscription.Unknown = true;
        }
        foreach (var callback in callbacks) try { callback(reason); } catch { }
    }
    private void TryRelease(ParentLoan loan)
    {
        if (!loan.ParentClosed || loan.Callbacks != 0 || loan.CaptureFailed || loan.Released) return;
        var parents = _parents[loan.Key]; parents.Remove(loan); if (parents.Count == 0) _parents.Remove(loan.Key);
        --loan.Subscription.Loans; --_loans; loan.Released = true;
        // Clear every closure retaining the run/backend. The permanent native observer owns only this hub.
        loan.Observe = null; loan.Fault = null;
    }
    internal sealed class Subscription : IDisposable
    {
        internal readonly NativeTweenSingleEventSubscriptions<T> Owner;
        internal Action<T>? Observe;
        internal Action<string>? Fault;
        internal bool Closed, Unknown;
        internal int Loans, Callbacks;
        internal Subscription(NativeTweenSingleEventSubscriptions<T> owner, Action<T> observe, Action<string> fault)
            => (Owner, Observe, Fault) = (owner, observe, fault);
        internal ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        { lock (Owner._gate) return new(Math.Max(Loans, Callbacks), !Unknown); }
        internal bool IsWithdrawn { get { lock (Owner._gate) return Closed; } }
        internal bool IsDrained { get { lock (Owner._gate) return Closed && Loans == 0 && Callbacks == 0 && !Unknown; } }
        public void Dispose()
        {
            lock (Owner._gate)
            {
                if (Closed) return; Closed = true; Owner._active.Remove(this);
                Observe = null; Fault = null; // Existing ParentLoan snapshots retain their own exact cleanup callback.
            }
        }
    }
    internal sealed class ParentLoan
    {
        private readonly NativeTweenSingleEventSubscriptions<T> _owner;
        internal readonly Subscription Subscription;
        internal readonly NativeTweenSingleParentKey Key;
        internal Action<T>? Observe;
        internal Action<string>? Fault;
        internal bool Bound, CaptureFailed, Failed, ParentClosed, Released;
        internal int Callbacks;
        internal ParentLoan(NativeTweenSingleEventSubscriptions<T> owner, Subscription subscription, NativeTweenSingleParentKey key,
            Action<T> observe, Action<string> fault) => (_owner, Subscription, Key, Observe, Fault) = (owner, subscription, key, observe, fault);
        internal void Bind()
        {
            lock (_owner._gate)
            {
                if (Bound || CaptureFailed || ParentClosed || Released) throw new InvalidOperationException("Single capture snapshot cannot be bound twice or after failure.");
                Bound = true; // A scope withdrawal during capture does not erase an already-enrolled parent.
            }
        }
        internal void CaptureFault(string reason)
        {
            Action<string>? fault;
            lock (_owner._gate) { CaptureFailed = true; Failed = true; Subscription.Unknown = true; fault = Fault; }
            try { fault?.Invoke(reason); } catch { }
        }
        internal void FaultCompletion(string reason)
        {
            Action<string>? fault;
            lock (_owner._gate) { Failed = true; Subscription.Unknown = true; fault = Fault; }
            try { fault?.Invoke(reason); } catch { }
        }
        internal void Close()
        {
            lock (_owner._gate)
            {
                if (!Bound || ParentClosed || Released) throw new InvalidOperationException("Single parent snapshot has no once-only actual exit cleanup.");
                ParentClosed = true; _owner.TryRelease(this);
            }
        }
    }
}
