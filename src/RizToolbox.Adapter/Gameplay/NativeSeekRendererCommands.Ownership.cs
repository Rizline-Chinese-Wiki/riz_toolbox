namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekRendererAllocationOwnership(long Pool, long Child, long Transform, long List, long Backing);

internal sealed partial class NativeSeekRendererCommands
{
    private bool _poolOwnershipAcknowledged;
    private IReadOnlyList<SeekRendererAllocationOwnership> _allocationOwnership = Array.Empty<SeekRendererAllocationOwnership>();
    private bool HasRecognizedAllocations => HasVisibilityAllocationResponsibility || _rows.Any(row => row is { GrowthObserved: true });

    // Records actual game-graph ownership only. Roots and the inverse journal
    // remain here until Dispose, because a later Clock stage can still fail.
    internal void AcknowledgeVerifiedPoolOwnership() => Operation(() =>
    {
        if (!_executionFinished || _restoreEntered || _unknown || _visibility is { Observed: false } or { Unknown: true })
            throw new InvalidOperationException("Only a fully observed renderer result may acknowledge its original pool owners.");
        VerifyExpectedPoolStateCore();
        var original = _baseline!; var current = _current!;
        var originalChildren = original.Children.Select(child => child.Reference).ToHashSet();
        var originalPools = original.Pools.ToDictionary(pool => pool.Reference);
        var records = new List<SeekRendererAllocationOwnership>();
        foreach (var child in current.Children.Where(child => !originalChildren.Contains(child.Reference)))
        {
            if (!originalPools.ContainsKey(child.ParentPool))
                throw new InvalidOperationException("A new renderer child has no original retained pool owner.");
            var pool = current.Pools.Single(value => value.Reference == child.ParentPool);
            var held = current.Plans.Any(plan => plan.Active && plan.Pool == pool.Reference && plan.Child == child.Reference)
                || pool.Available.CapacitySlots.Take(pool.Available.Size).Contains(child.Reference)
                || pool.Dirty.CapacitySlots.Take(pool.Dirty.Size).Contains(child.Reference);
            if (!held) throw new InvalidOperationException("A new renderer child is absent from its actual active plan and pool Lists.");
            records.Add(new(pool.Reference, child.Reference, child.Transform, 0, 0));
        }
        foreach (var pool in current.Pools)
        {
            var prior = originalPools[pool.Reference];
            foreach (var pair in new[] { (Current: pool.Available, Original: prior.Available), (Current: pool.Dirty, Original: prior.Dirty) })
            {
                if (pair.Current.Reference != pair.Original.Reference || pair.Current.Class != pair.Original.Class
                    || pair.Current.ArrayClass != pair.Original.ArrayClass)
                    throw new InvalidOperationException("An allocation changed the original retained pool List owner.");
                if (pair.Current.Backing != pair.Original.Backing)
                    records.Add(new(pool.Reference, 0, 0, pair.Current.Reference, pair.Current.Backing));
            }
        }
        VerifyExpectedPoolStateCore();
        _allocationOwnership = Array.AsReadOnly(records.ToArray());
        _poolOwnershipAcknowledged = true; return 0;
    });

    // No source/native reads: a withdrawn run must not prevent revocation
    // before recovery attempts its first source guard.
    internal void RevokePoolOwnershipAcknowledgement()
    {
        if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_registered, this))
            throw new InvalidOperationException("Only the original renderer thread can revoke its allocation handoff.");
        _poolOwnershipAcknowledged = false;
    }
}
