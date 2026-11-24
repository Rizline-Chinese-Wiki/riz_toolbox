namespace RizToolbox.Adapter.Gameplay;

/// <summary>Owned historical target data from one retained source observation.
/// Chart occurrences and local endpoints share the same actual Canvas Plan and
/// physical source graph. This is not a live ticket, world target, writer, or
/// complete Seek admission; a later native operation needs fresh admission.</summary>
internal sealed record SeekLineRingLocalTargetProjection(SeekLineRingSourceSnapshot Source,
    NativeSeekLineChartOccurrenceMap ChartOccurrences, NativeSeekLineEndpointLocalTarget Endpoints)
{
    internal bool CompleteSeekPermitted => false;
}

internal sealed partial class NativeSeekLineRingSourceCapture
{
    /// <summary>Reads through this owner's independent original reader hold.
    /// Complete chart reference mapping includes lines with no runtime Note.
    /// Both projections run between two fresh complete source verifications in
    /// one shared scope. Mutable poses/scalars remain historical baselines.</summary>
    internal SeekLineRingLocalTargetProjection ReadLocalTargetProjection()
    {
        // A worker refusal must not touch even managed proof state belonging to
        // the original owner thread. Operation itself repeats this admission.
        VerifyThread();
        try
        {
            return Operation(() =>
            {
                var source = VerifyInOperation(baseline: false);
                var basis = _numericBasis ?? throw new InvalidOperationException(
                    "The original owned Canvas numeric basis was not captured.");
                Local(); basis.VerifyAssociation(source.Identity); Local();
                var occurrences = NativeSeekLineChartOccurrenceMap.Project(source, Local);
                occurrences.StrictResolve(); Local();
                var endpoints = NativeSeekLineEndpointLocalTarget.Project(basis, source, Local);
                Local();
                // Re-read after all projections; never publish a target whose
                // source drifted while managed callbacks/guards were running.
                VerifyInOperation(baseline: false);
                return new SeekLineRingLocalTargetProjection(source, occurrences, endpoints);
            });
        }
        catch
        {
            // Includes pure projection refusal and shared-scope cleanup failure
            // after an otherwise successful post-verification. History already
            // returned by earlier calls stays readable but grants no admission.
            _sourceVerified = false; _baselineVerified = false;
            throw;
        }
    }
}
