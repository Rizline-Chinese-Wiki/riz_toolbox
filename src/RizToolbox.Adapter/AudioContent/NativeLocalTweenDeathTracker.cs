using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeLocalTweenDeathPhase { AwaitingLiveBaseline, LiveObserved, AwaitingReturnFrame, AwaitingSealedSource,
    AwaitingLaterDeath, FirstDeathObserved, Confirmed, Unknown }
internal sealed record NativeLocalTweenDeathSnapshot(long Tween, bool Production, NativeLocalTweenDeathPhase Phase,
    long ManagedClass, long CachedPointerField, long OriginalNative, int LiveFrame, int ReturnFrame, int FirstDeathFrame,
    int ConfirmedFrame, int LastFrame, ulong SourceEpoch, bool Unknown, string? Failure);

/// <summary>Retains one concrete rooted Tween's live-to-dead interval. Its private
/// receipt proves only that Unity native object died; queue aliases, effects,
/// wrapper GC lifetime and all independent roots remain their owners' duties.</summary>
internal sealed class NativeLocalTweenDeathTracker
{
    private readonly IMainThreadDispatcher _main;
    private readonly NativeLocalTweenWorkOwner.TweenDeathSource _source;
    private readonly object _receiptIssuer = new();
    private NativeTweenDeathObservation? _previous;
    private NativeLocalTweenWorkOwner.TweenDeathReturnSource? _return;
    private int _liveFrame = -1, _returnFrame = -1, _firstDeathFrame = -1, _confirmedFrame = -1, _lastFrame = -1;
    private long _native, _class, _field;
    private ulong _epoch;
    private bool _captured, _busy, _unknown;
    private string? _failure;
    private DeathReceipt? _receipt;
    internal bool Production => _source.Production;
    internal bool Unknown => _unknown || _source.Unknown;

    internal sealed class DeathReceipt
    {
        private readonly object _issuer;
        private readonly NativeLocalTweenDeathTracker _tracker;
        private readonly NativeLocalTweenWorkOwner.TweenDeathSource _source;
        private readonly NativeLocalTweenWorkOwner.TweenDeathReturnSource _return;
        private readonly ulong _epoch;
        private readonly int _confirmedFrame;
        internal bool Production => _source.Production;
        internal ulong SourceEpoch => _epoch;
        internal DeathReceipt(object issuer, NativeLocalTweenDeathTracker tracker)
        {
            if (!ReferenceEquals(issuer, tracker._receiptIssuer) || !tracker.CanIssueReceipt || tracker._return is null)
                throw new InvalidOperationException("Only the actual completed rooted death interval can issue its private receipt.");
            (_issuer, _tracker, _source, _return, _epoch, _confirmedFrame) =
                (issuer, tracker, tracker._source, tracker._return, tracker._epoch, tracker._confirmedFrame);
        }
        internal bool IsFor(NativeLocalTweenWorkOwner work, NativeRuntime.RootHandle root,
            LocalRunLaunchCommitState? launch, ulong sourceEpoch)
        {
            try { VerifyCurrent(work, root, launch, sourceEpoch); return true; }
            catch { return false; }
        }
        internal void VerifyCurrent(NativeLocalTweenWorkOwner work, NativeRuntime.RootHandle root,
            LocalRunLaunchCommitState? launch, ulong sourceEpoch)
        {
            _tracker._main.VerifyAccess();
            if (!ReferenceEquals(_issuer, _tracker._receiptIssuer) || !ReferenceEquals(_source, _tracker._source)
                || !ReferenceEquals(_return, _tracker._return) || _epoch != sourceEpoch || _epoch == 0
                || _epoch != _tracker._epoch || _confirmedFrame != _tracker._confirmedFrame
                || _tracker._busy || !_tracker.CanIssueReceipt
                || !_source.IsFor(work, root, launch, sourceEpoch) || !_return.IsFor(work, launch))
                throw new InvalidOperationException("A Tween death receipt is stale or belongs to another retained interval.");
            try { _source.VerifyReceiptCurrent(work, root, launch, sourceEpoch); }
            catch (Exception error)
            {
                // Identity checks passed, so this is a real failure of the
                // previously confirmed interval rather than a foreign query.
                // Permanently retain it before reporting the receipt stale.
                _tracker.Fail(error); throw;
            }
        }
    }
    internal NativeLocalTweenDeathTracker(IMainThreadDispatcher main, NativeLocalTweenWorkOwner.TweenDeathSource source)
    { _main = main; _source = source; source.RequireRetained(); }

    internal void CaptureLiveBaseline()
    {
        _main.VerifyAccess();
        if (_captured || _busy || Unknown) throw new InvalidOperationException("The same Tween death baseline cannot be recaptured or entered after a fault.");
        _busy = true;
        try
        {
            var observed = _source.TryReadObservation(baseline: true)
                ?? throw new InvalidOperationException("The actual preactivation source omitted its live baseline observation.");
            AcceptObservation(observed);
            // An initially zero wrapper remains unproven. It is retained and
            // cannot later become a live baseline for this same identity.
            if (observed.Alive) { _native = observed.CachedNative; _liveFrame = observed.Frame; }
            _captured = true;
        }
        catch (Exception error) { Fail(error); throw; }
        finally { _busy = false; }
    }
    internal bool Tick(NativeLocalTweenWorkOwner.TweenDeathReturnSource? request)
    {
        _main.VerifyAccess();
        if (!_captured || _busy || Unknown) throw new InvalidOperationException("Rooted Tween death observation is unavailable, unknown or reentered.");
        if (request is null) return false; // No reads can manufacture a classified Return source.
        _busy = true;
        try
        {
            _source.RequireReturn(request);
            if (_return is not null && !ReferenceEquals(_return, request)) throw new InvalidOperationException("Tween death interval crossed its actual classified Return source.");
            _return ??= request;
            var sealedSource = _source.ReadSealedEpoch();
            if (sealedSource != 0)
            {
                if (_epoch != 0 && _epoch != sealedSource) throw new InvalidOperationException("Tween death interval crossed the actual source slot/epoch.");
                _epoch = sealedSource;
            }
            if (_returnFrame < 0)
            {
                var pending = _source.TryReadObservation(baseline: false); if (pending is not { } requested) return false;
                AcceptObservation(requested);
                _returnFrame = requested.Frame;
                return false; // The request-observation frame never proves death.
            }
            if (sealedSource == 0) return false;
            var sample = _source.TryReadObservation(baseline: false); if (sample is not { } observed) return false;
            AcceptObservation(observed);
            _source.RequireReturn(request);
            if (_source.ReadSealedEpoch() != _epoch) throw new InvalidOperationException("The actual sealed source changed across the rooted death sample.");
            if (_liveFrame < 0 || observed.Frame <= _liveFrame || observed.Frame <= _returnFrame || observed.Alive) return false;
            if (_firstDeathFrame < 0) { _firstDeathFrame = observed.Frame; return false; }
            if (observed.Frame <= _firstDeathFrame) return false;
            if (_confirmedFrame < 0) _confirmedFrame = observed.Frame;
            _receipt ??= new(_receiptIssuer, this); return CanIssueReceipt;
        }
        catch (Exception error) { Fail(error); throw; }
        finally { _busy = false; }
    }
    private void AcceptObservation(NativeTweenDeathObservation observed)
    {
        if (observed.ManagedReceiver != _source.Tween || observed.ManagedClass == IntPtr.Zero || observed.CachedPointerField == IntPtr.Zero
            || observed.Frame < 0 || observed.Frame < _lastFrame || observed.Alive != (observed.CachedNative != 0)
            || _previous is { } previous && (previous.ManagedClass != observed.ManagedClass || previous.CachedPointerField != observed.CachedPointerField
                || !previous.Alive && observed.Alive || previous.Alive && observed.Alive && previous.CachedNative != observed.CachedNative))
            throw new InvalidOperationException("A retained Tween root, actual class/field, monotonic frame or live/dead native identity changed.");
        _class = observed.ManagedClass.ToInt64(); _field = observed.CachedPointerField.ToInt64();
        _previous = observed; _lastFrame = observed.Frame;
    }
    private bool CanIssueReceipt => !Unknown && _captured && _liveFrame >= 0 && _native != 0 && _epoch != 0 && _return is not null
        && _returnFrame >= _liveFrame && _firstDeathFrame > _returnFrame && _confirmedFrame > _firstDeathFrame
        && _previous is { Alive: false, CachedNative: 0 } && _source.ConfirmedEpoch == _epoch;
    internal DeathReceipt? ReadReceipt()
    { _main.VerifyAccess(); return CanIssueReceipt ? _receipt : null; }
    internal NativeLocalTweenDeathSnapshot Read()
    {
        _main.VerifyAccess();
        var phase = Unknown ? NativeLocalTweenDeathPhase.Unknown : _liveFrame < 0 ? NativeLocalTweenDeathPhase.AwaitingLiveBaseline
            : _return is null ? NativeLocalTweenDeathPhase.LiveObserved : _returnFrame < 0 ? NativeLocalTweenDeathPhase.AwaitingReturnFrame
            : _epoch == 0 ? NativeLocalTweenDeathPhase.AwaitingSealedSource : _confirmedFrame >= 0 ? NativeLocalTweenDeathPhase.Confirmed
            : _firstDeathFrame >= 0 ? NativeLocalTweenDeathPhase.FirstDeathObserved : NativeLocalTweenDeathPhase.AwaitingLaterDeath;
        return new(_source.Tween, Production, phase, _class, _field, _native, _liveFrame, _returnFrame, _firstDeathFrame,
            _confirmedFrame, _lastFrame, _epoch, Unknown, _failure);
    }
    private void Fail(Exception error)
    {
        _unknown = true; _failure ??= error.Message;
        _source.RetainFault("Tween death interval: " + error.Message);
    }
}
