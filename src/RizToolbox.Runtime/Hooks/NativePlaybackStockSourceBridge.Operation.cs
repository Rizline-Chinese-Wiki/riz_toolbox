namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceBridge
{
    internal void VerifyOperationAuthority() => _hub.VerifyOperationMemberEvidence(this);
    internal void VerifyOperationKnown() => _hub.VerifyOperationMemberEvidence(this, requireArmed: false);

    internal void VerifyIssuedOperationBefore(NativePlaybackSourceObservation observation)
    {
        var origin = observation.ActualOrigin(this);
        lock (_gate)
            if (!_observations.TryGetValue(origin.Sequence, out var actual) || !ReferenceEquals(actual, observation))
                throw new UnsupportedSignatureException("The running source requires its exact issued observation object.");
        ReadOperationCurrentActual(origin);
        observation.VerifyEngineAssociationCurrent();
    }

    internal void VerifyIssuedOperationReturn(NativePlaybackSourceCompletion completion)
    {
        var origin = completion.Observation.ActualOrigin(this);
        lock (_gate)
            if (!_observations.TryGetValue(origin.Sequence, out var actual) || !ReferenceEquals(actual, completion.Observation)
                || !_returns.TryGetValue(origin.Sequence, out var returned) || !ReferenceEquals(returned, completion))
                throw new UnsupportedSignatureException("The running source requires its exact issued return object.");
        if (completion.PhysicalExited)
            throw new UnsupportedSignatureException("A journal projection cannot issue a running stock return.");
        completion.VerifyOperationAuthority();
    }

    internal NativePlaybackSourceFrame ReadOperationCurrentActual(NativePlaybackSourceFrame expected,
        bool requireBefore = true, bool allowWithdrawnCompletion = false)
        => _hub.ReadOperationCurrentActual(this, expected, requireBefore, allowWithdrawnCompletion);

    internal NativePlaybackSourceFrame ReadOperationReceiptActual(NativePlaybackSourceFrame origin)
    {
        if (!_hub.TryReadOperationReceipt(this, origin, out var actual))
            throw new UnsupportedSignatureException("The running source call has no actual retained physical-exit receipt yet.");
        return actual;
    }

    internal bool TryReadOperationCompletionActual(NativePlaybackSourceObservation observation, NativePlaybackSourceFrame origin,
        out NativePlaybackSourceCompletion? completion)
    {
        if (!observation.BelongsTo(this))
            throw new UnsupportedSignatureException("The running observation belongs to another source channel.");
        lock (_gate)
            if (!_observations.TryGetValue(origin.Sequence, out var issued) || !ReferenceEquals(issued, observation))
                throw new UnsupportedSignatureException("A copied running observation is not the exact issued Before.");
        if (!_hub.TryReadOperationReceipt(this, origin, out var actual)) { completion = null; return false; }
        completion = NativePlaybackSourceCompletion.CaptureOperationPhysicalActual(this, observation, actual, _issuer);
        return true;
    }

    internal void ReleaseOperationWhenExited(NativePlaybackSourceObservation observation)
    {
        lock (_gate)
        {
            if (observation.IsReleased) return;
            var origin = observation.ActualOrigin(this);
            if (!_observations.TryGetValue(origin.Sequence, out var actual) || !ReferenceEquals(actual, observation))
                throw new UnsupportedSignatureException("No running source Before owns this journal row.");
            if (!_hub.TryReadOperationReceipt(this, origin, out _)) return;
            if (!_hub.Release(origin))
                throw new UnsupportedSignatureException("The running source physical-exit row could not be released.");
            observation.MarkReleased(this); _observations.Remove(origin.Sequence); _returns.Remove(origin.Sequence);
        }
    }

    internal void PumpOperationCompleted()
    {
        NativePlaybackSourceObservation[] pending;
        lock (_gate) pending = _observations.Values.Where(value => value.ReleaseRequested).ToArray();
        foreach (var observation in pending) ReleaseOperationWhenExited(observation);
    }
}
