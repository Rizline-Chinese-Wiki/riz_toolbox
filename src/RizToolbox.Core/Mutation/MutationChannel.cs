using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Core.Mutation;

/// <summary>One reversible target property. Value types must be immutable snapshots and transforms must be pure.</summary>
public sealed class MutationChannel<T> : MutationCoordinator.IChannel
{
    private readonly ITargetValueLease<T> _target;
    private readonly Action<T> _write;
    private readonly Func<T, string?>? _validate;
    private readonly IEqualityComparer<T> _comparer;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly Action<string, Exception> _fault;
    private readonly Action<MutationChannel<T>> _removed;
    private readonly List<Holder> _holders = new();
    private readonly MutationStrategy _strategy;
    private readonly ExternalWritePolicy _policy;
    private T _baseline, _effective, _lastWritten;
    private bool _valid = true, _owned, _busy, _dirty;
    private long _sequence;
    private OperationError? _lastError;
    public MutationKey<T> Key { get; }
    public bool Valid => _valid;
    public bool HasHolders
    {
        get { _main.VerifyAccess(); return _holders.Count != 0; }
    }

    internal MutationChannel(MutationKey<T> key, ITargetValueLease<T> target, Action<T> write,
        MutationStrategy strategy, ExternalWritePolicy policy, T initial, Func<T, string?>? validate,
        IEqualityComparer<T> comparer, IMainThreadDispatcher main, ILogger log,
        Action<string, Exception> fault, Action<MutationChannel<T>> removed)
    {
        Key = key; _target = target; _write = write; _strategy = strategy; _policy = policy; _baseline = initial;
        _effective = initial; _lastWritten = initial; _validate = validate; _comparer = comparer;
        _main = main; _log = log; _fault = fault; _removed = removed;
    }

    public OperationResult<IMutationLease<T>> Override(string owner, T value, int priority = 0, Action<Exception>? onFault = null)
        => Add(owner, MutationStrategy.PriorityOverride, value, null, priority, onFault);
    public OperationResult<IMutationLease<T>> Transform(string owner, Func<T, T> transform, int order = 0, Action<Exception>? onFault = null)
        => transform is null ? OperationResult<IMutationLease<T>>.Fail(Error(ErrorCode.InvalidArgument, "Transform is null."))
            : Add(owner, MutationStrategy.OrderedTransform, default!, transform, order, onFault);
    public OperationResult<IMutationLease<T>> AcquireExclusive(string owner, T initialValue, Action<Exception>? onFault = null)
        => Add(owner, MutationStrategy.Exclusive, initialValue, null, 0, onFault);

    private OperationResult<IMutationLease<T>> Add(string owner, MutationStrategy strategy, T value,
        Func<T, T>? transform, int priority, Action<Exception>? onFault)
    {
        _main.VerifyAccess();
        var error = CheckAvailable();
        if (error is not null) return OperationResult<IMutationLease<T>>.Fail(error);
        if (_strategy != strategy) return OperationResult<IMutationLease<T>>.Fail(Error(ErrorCode.MutationNotSupported, $"Property requires {_strategy}, not {strategy}."));
        if (string.IsNullOrWhiteSpace(owner)) return OperationResult<IMutationLease<T>>.Fail(Error(ErrorCode.InvalidArgument, "Owner is required."));
        if (_strategy == MutationStrategy.Exclusive && _holders.Count != 0)
            return OperationResult<IMutationLease<T>>.Fail(Error(ErrorCode.Conflict, "Target is controlled by " + string.Join(", ", _holders.Select(x => x.Owner).Distinct())));
        _busy = true;
        Holder? holder = null;
        try
        {
            var refresh = RefreshInternal();
            if (!refresh.Success) return OperationResult<IMutationLease<T>>.Fail(refresh.Error!);
            holder = new(this, owner, value, transform, priority, ++_sequence, onFault);
            if (transform is null)
            {
                var invalid = Validate(value);
                if (invalid is not null)
                {
                    ProviderFailed(holder, new InvalidOperationException(invalid));
                    return OperationResult<IMutationLease<T>>.Fail(_lastError!);
                }
            }
            _holders.Add(holder);
            var result = ApplyInternal();
            if (!result.Success || !holder.Active)
            {
                holder.Active = false; _holders.Remove(holder);
                if (_valid) ApplyInternal();
                return OperationResult<IMutationLease<T>>.Fail(result.Error ?? _lastError ?? Error(ErrorCode.InvalidArgument, "Provider was rejected."));
            }
            return OperationResult<IMutationLease<T>>.Ok(holder);
        }
        finally { _busy = false; }
    }

    public OperationResult Refresh()
    {
        _main.VerifyAccess();
        var error = CheckAvailable();
        if (error is not null) return OperationResult.Fail(error);
        _busy = true;
        try { return RefreshInternal(); }
        finally { _busy = false; }
    }

    private OperationResult RefreshInternal()
    {
        var actual = _target.Read();
        if (!actual.Success) { InvalidateInternal(); return Fail(actual.Error!); }
        if (_holders.Count == 0)
        {
            // Restoring a baseline ends write ownership. Later game writes on an idle channel are never reasserted.
            _baseline = actual.Value!; _effective = actual.Value!; _owned = false;
            return OperationResult.Ok();
        }
        if (_holders.Count > 0 && !_comparer.Equals(actual.Value!, _owned ? _lastWritten : _effective))
        {
            _owned = false;
            if (_policy == ExternalWritePolicy.PreserveExternalAndInvalidate)
            {
                _baseline = actual.Value!; _effective = actual.Value!;
                foreach (var holder in _holders) holder.Active = false;
                _holders.Clear(); _lastError = Error(ErrorCode.Conflict, "ExternalWrite: all leases ended; game value was preserved.");
                return OperationResult.Ok();
            }
            if (_policy == ExternalWritePolicy.AdoptAndReapply) _baseline = actual.Value!;
        }
        else if (!_owned) _baseline = actual.Value!;
        return ApplyInternal(actual.Value!);
    }

    private OperationResult ApplyInternal()
    {
        var actual = _target.Read();
        if (!actual.Success) { InvalidateInternal(); return Fail(actual.Error!); }
        return ApplyInternal(actual.Value!);
    }

    private OperationResult ApplyInternal(T actual)
    {
        if (!_valid) return Fail(ErrorCode.TargetExpired, "Mutation channel is invalid.");
        T desired;
        do
        {
            _dirty = false;
            desired = _baseline;
            if (_strategy is MutationStrategy.PriorityOverride or MutationStrategy.Exclusive)
            {
                foreach (var holder in _holders.OrderByDescending(x => x.Priority).ThenByDescending(x => x.Order).ToArray())
                {
                    if (!holder.Active) continue;
                    var error = Validate(holder.Value);
                    if (error is not null) { ProviderFailed(holder, new InvalidOperationException(error)); continue; }
                    holder.Candidate = holder.Value; desired = holder.Value; break;
                }
            }
            else
            {
                foreach (var holder in _holders.OrderBy(x => x.Priority).ThenBy(x => x.Order).ToArray())
                {
                    if (!holder.Active) continue;
                    try
                    {
                        var candidate = holder.Transform!(desired);
                        var error = Validate(candidate);
                        if (error is not null) throw new InvalidOperationException(error);
                        holder.Candidate = candidate; desired = candidate;
                    }
                    catch (Exception ex) { ProviderFailed(holder, ex); }
                    if (!_valid) return Fail(ErrorCode.TargetExpired, "Target invalidated during provider evaluation.");
                }
            }
        } while (_dirty && _valid);
        if (!_valid) return Fail(ErrorCode.TargetExpired, "Target invalidated during provider evaluation.");
        if (_holders.Count == 0 && !_owned)
        { _baseline = actual; _effective = actual; return OperationResult.Ok(); }
        if (_comparer.Equals(actual, desired)) { _effective = desired; if (_holders.Count == 0) _owned = false; return OperationResult.Ok(); }
        return Commit(actual, desired);
    }

    private OperationResult Commit(T before, T desired)
    {
        var priorOwned = _owned; var priorLast = _lastWritten; OperationError? sourceFailure = null;
        try
        {
            _write(desired);
            if (!_valid || !_target.IsValid) { InvalidateInternal(); return Fail(ErrorCode.TargetExpired, "Target invalidated while writing."); }
            var after = _target.Read();
            if (!after.Success) { sourceFailure = after.Error!; throw new InvalidOperationException(sourceFailure.Message); }
            if (!_comparer.Equals(after.Value!, desired)) throw new InvalidOperationException("MutationReadbackMismatch: adapter did not apply the requested value.");
            _lastWritten = desired; _effective = desired; _owned = _holders.Count > 0;
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            // A callback may mutate then throw. Restore only if read-back proves the value is still the one just requested.
            var rolledBack = false;
            if (_valid && _target.IsValid)
            {
                var after = _target.Read();
                if (after.Success && _comparer.Equals(after.Value!, before)) rolledBack = true;
                else if (after.Success && _comparer.Equals(after.Value!, desired))
                {
                    try
                    {
                        _write(before); var restored = _target.Read();
                        rolledBack = restored.Success && _comparer.Equals(restored.Value!, before);
                    }
                    catch (Exception rollbackError) { _log.TryLog(LogLevel.Error, "Mutation rollback failed.", rollbackError); }
                }
            }
            if (rolledBack) { _owned = priorOwned; _lastWritten = priorLast; _effective = before; }
            else InvalidateInternal();
            return sourceFailure is not null ? Fail(sourceFailure) : Fail(ErrorCode.MutationWriteFailed,
                ex.Message + (rolledBack ? " Previous value restored." : " Value ownership lost; leases invalidated without overwriting it."));
        }
    }

    private string? Validate(T value)
    {
        try { return _validate?.Invoke(value); }
        catch (Exception ex) { return "MutationValidationFailed: " + ex.Message; }
    }
    private void ProviderFailed(Holder holder, Exception error)
    {
        holder.Active = false; _holders.Remove(holder); _dirty = true;
        _lastError = Error(ErrorCode.InvalidArgument, error.Message);
        try { holder.Fault?.Invoke(error); }
        catch (Exception ex) { _log.TryLog(LogLevel.Error, "Mutation owner fault handler failed.", ex); }
        _fault(holder.Owner, error);
    }

    private OperationResult SetValue(Holder holder, T value)
    {
        _main.VerifyAccess();
        var error = CheckAvailable();
        if (error is not null || !holder.Active) return OperationResult.Fail(error ?? Error(ErrorCode.ScopeExpired, "Lease is not active."));
        if (_strategy == MutationStrategy.OrderedTransform) return OperationResult.Fail(Error(ErrorCode.MutationNotSupported, "Transform leases cannot set a candidate value."));
        _busy = true;
        try
        {
            var validation = Validate(value);
            if (validation is not null) return Fail(ErrorCode.InvalidArgument, validation);
            var refresh = RefreshInternal();
            if (!refresh.Success || !holder.Active) return refresh.Success ? OperationResult.Fail(Error(ErrorCode.ScopeExpired, "External game write ended this lease.")) : refresh;
            var previous = holder.Value; holder.Value = value;
            var result = ApplyInternal();
            if (!result.Success) { holder.Value = previous; if (_valid) ApplyInternal(); }
            return result;
        }
        finally { _busy = false; }
    }

    private void Release(Holder holder)
    {
        _main.VerifyAccess();
        if (!holder.Active) return;
        if (_busy) { holder.Active = false; _holders.Remove(holder); _dirty = true; return; }
        _busy = true;
        try
        {
            // Refresh before removal so an intervening game write is adopted or preserved according to policy.
            var result = _valid ? RefreshInternal() : OperationResult.Fail(Error(ErrorCode.TargetExpired, "Mutation target expired."));
            holder.Active = false; _holders.Remove(holder);
            if (result.Success && _valid) ApplyInternal();
        }
        finally { _busy = false; }
    }

    public MutationSnapshot<T> Describe()
    {
        _main.VerifyAccess();
        var winner = _strategy == MutationStrategy.OrderedTransform ? null : _holders.OrderByDescending(x => x.Priority).ThenByDescending(x => x.Order).FirstOrDefault();
        var holders = (_strategy == MutationStrategy.OrderedTransform
            ? _holders.OrderBy(x => x.Priority).ThenBy(x => x.Order)
            : _holders.OrderByDescending(x => x.Priority).ThenByDescending(x => x.Order))
            .Select(x => new MutationHolder<T>(x.LeaseId, x.Owner, _strategy, x.Priority, x.Order,
                x.Transform is null ? x.Value : x.Candidate, _strategy == MutationStrategy.OrderedTransform || ReferenceEquals(x, winner),
                _strategy == MutationStrategy.OrderedTransform ? "Transform applied in ascending order, then registration order."
                : ReferenceEquals(x, winner) ? "Highest priority; latest registration wins equal priority." : "Shadowed by a higher priority or later equal-priority lease.")).ToArray();
        return new(Key, _valid, _strategy, _policy, _baseline, _effective, holders, _lastError?.Message);
    }

    private OperationError? CheckAvailable() => !_valid ? Error(ErrorCode.TargetExpired, "Mutation binding is invalid.")
        : _busy ? Error(ErrorCode.MutationReentrancy, "Mutation providers cannot register or update mutations during evaluation.") : null;
    private OperationError Error(ErrorCode code, string message) => new(code, message, TargetCapabilityIds.Mutations, Key.Target.Key);
    private OperationResult Fail(ErrorCode code, string message) => Fail(Error(code, message));
    private OperationResult Fail(OperationError error) { _lastError = error; return OperationResult.Fail(error); }
    void MutationCoordinator.IChannel.Invalidate() { _main.VerifyAccess(); InvalidateInternal(); }
    private void InvalidateInternal()
    {
        if (!_valid) return;
        _valid = false; _owned = false;
        foreach (var holder in _holders) holder.Active = false;
        _holders.Clear(); _target.Dispose(); _removed(this);
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (!_valid) return;
        if (_busy) { InvalidateInternal(); return; }
        _busy = true;
        try
        {
            // Never restore over an external game write which this channel no longer owns.
            var actual = _target.Read();
            foreach (var holder in _holders) holder.Active = false;
            _holders.Clear();
            if (actual.Success && _owned && _comparer.Equals(actual.Value!, _lastWritten)) Commit(actual.Value!, _baseline);
            InvalidateInternal();
        }
        finally { _busy = false; }
    }

    private sealed class Holder : IMutationLease<T>
    {
        private readonly MutationChannel<T> _owner;
        public string Owner { get; }
        public T Value, Candidate;
        public Func<T, T>? Transform;
        public int Priority; public long Order; public bool Active = true;
        public Action<Exception>? Fault;
        public string LeaseId { get; } = Guid.NewGuid().ToString("N");
        public MutationKey<T> Key => _owner.Key;
        public bool IsActive { get { _owner._main.VerifyAccess(); return Active && _owner.Valid; } }
        public Holder(MutationChannel<T> owner, string plugin, T value, Func<T, T>? transform, int priority, long order, Action<Exception>? fault)
        { _owner = owner; Owner = plugin; Value = value; Candidate = value; Transform = transform; Priority = priority; Order = order; Fault = fault; }
        public OperationResult SetValue(T value) => _owner.SetValue(this, value);
        public void Dispose() => _owner.Release(this);
    }
}
