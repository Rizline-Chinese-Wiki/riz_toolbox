namespace RizToolbox.Adapter.AudioContent;

/// <summary>One retained source/receiver closure, issued by the actual engine
/// owner. This is not a consumers-ended or root-free permission. Death alone
/// covers no scheduler row, wait publication, async or global consumer.</summary>
internal sealed class NativeLocalActiveSourceClosureSeal : INativeLocalActiveConsumerClosureSource
{
    private readonly NativeEngineCoroutineRuntimeCoordinator _engine;
    private readonly LocalRunLaunchCommitState _launch;
    private readonly OwnedNativeInactiveController _factory;
    private readonly NativeLocalActiveDestructionTracker.FactoryRetirementSeal _death;
    internal LocalRunSessionIdentity Session => _launch.Identity;
    internal bool Production => _death.Production;

    internal NativeLocalActiveSourceClosureSeal(NativeEngineCoroutineRuntimeCoordinator engine, object issuer,
        LocalRunLaunchCommitState launch, OwnedNativeInactiveController factory,
        NativeLocalActiveDestructionTracker.FactoryRetirementSeal death)
    {
        if (!engine.IsIssuingActiveSourceClosure(issuer, launch, factory, death))
            throw new InvalidOperationException("Only the actual retained engine/source issuance interval can create a source closure.");
        (_engine, _launch, _factory, _death) = (engine, launch, factory, death);
    }
    internal void ValidateFor(LocalRunLaunchCommitState exactLaunch) => ValidateFor(exactLaunch, _factory);
    internal void ValidateFor(LocalRunLaunchCommitState exactLaunch, OwnedNativeInactiveController exactFactory)
    {
        ArgumentNullException.ThrowIfNull(exactLaunch); ArgumentNullException.ThrowIfNull(exactFactory);
        if (!ReferenceEquals(_launch, exactLaunch) || !ReferenceEquals(_factory, exactFactory))
            throw new InvalidOperationException("Source closure belongs to another exact launch or factory generation.");
        _death.ValidateFor(exactLaunch, exactFactory);
        _engine.RequireIssuedActiveSourceClosure(this, exactLaunch, exactFactory, _death);
    }
    internal bool CoversEngineCancellationSources(NativeEngineCoroutineRuntimeCoordinator exactOwner)
    {
        ValidateFor(_launch);
        return ReferenceEquals(_engine, exactOwner)
            && _engine.ReadSourceClosureProof(this, NativeLocalActiveConsumerObligation.FutureNodeSource)
            && _engine.ReadSourceClosureProof(this, NativeLocalActiveConsumerObligation.FutureSchedulerSource)
            && _engine.ReadSourceClosureProof(this, NativeLocalActiveConsumerObligation.FutureWaitSource);
    }
    public NativeLocalActiveConsumerCheck ReadActiveConsumerClosure(LocalRunLaunchCommitState exactLaunch,
        NativeLocalActiveConsumerObligation obligation)
    {
        ValidateFor(exactLaunch);
        if (obligation is NativeLocalActiveConsumerObligation.FutureNodeSource
            or NativeLocalActiveConsumerObligation.FutureSchedulerSource or NativeLocalActiveConsumerObligation.FutureWaitSource)
        {
            var proven = _engine.ReadSourceClosureProof(this, obligation);
            return new(obligation, proven ? NativeLocalActiveConsumerProofState.Proven : NativeLocalActiveConsumerProofState.Pending,
                proven ? "Exact retained dead receivers reject new owned entry; all corresponding actual native generations and duties have completed."
                    : "A retained source or an exact native generation/side duty remains open; no future-source closure is inferred from idle.");
        }
        return new(obligation, NativeLocalActiveConsumerProofState.Missing,
            "This engine/death source has no authority over this independent owner; its actual typed completion is required.");
    }
}
