using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalActiveDestructionTracker
{
    private OwnedNativeInactiveController? _activeFactory;
    private bool _factoryRetirementFrozen;
    private FactoryRetirementSeal? _factoryRetirementSeal;
    private readonly object _factoryRetirementIssuer = new();
    private bool _frozenObservationReleaseBusy, _frozenObservationReleaseAttempted, _frozenObservationRootsReleased;
    internal bool FactoryRetirementFrozen { get { _main.VerifyAccess(); return _factoryRetirementFrozen; } }
    internal bool FrozenObservationRootsReleased { get { _main.VerifyAccess(); return _frozenObservationRootsReleased && !_unknown && !_backend.CleanupUnknown; } }

    /// <summary>Source-owned death observation only. It grants no whole-run
    /// consumers-ended permission. An independently constructed instance cannot
    /// pass ValidateFor: only this source's single published reference is valid.</summary>
    internal sealed class FactoryRetirementSeal
    {
        private readonly NativeLocalActiveDestructionTracker _source;
        internal LocalRunSessionIdentity Session { get; }
        internal ActiveDestructionIdentity Identity { get; }
        internal int CutoutFrame { get; }
        internal int CloneFrame { get; }
        internal int ConfirmedFrame { get; }
        internal bool Production { get; }
        internal FactoryRetirementSeal(NativeLocalActiveDestructionTracker source, object issuer, LocalRunSessionIdentity session,
            ActiveDestructionIdentity identity, int cutoutFrame, int cloneFrame, int confirmedFrame, bool production)
        {
            if (!ReferenceEquals(issuer, source._factoryRetirementIssuer))
                throw new InvalidOperationException("Only the retained destruction source can create its factory death seal.");
            (_source, Session, Identity, CutoutFrame, CloneFrame, ConfirmedFrame, Production) =
                (source, session, identity, cutoutFrame, cloneFrame, confirmedFrame, production);
        }
        internal void ValidateFor(LocalRunLaunchCommitState launch, OwnedNativeInactiveController owner)
            => _source.ValidateFactoryRetirementSeal(this, launch, owner);
        internal NativeLocalActiveSourceClosureSeal? TryAcquireSourceClosure(LocalRunLaunchCommitState launch,
            OwnedNativeInactiveController exactFactory, NativeEngineCoroutineRuntimeCoordinator exactEngine)
        {
            ArgumentNullException.ThrowIfNull(exactEngine); ValidateFor(launch, exactFactory);
            return exactEngine.TryAcquireActiveSourceClosure(_source, launch, exactFactory, this);
        }
    }

    internal static NativeLocalActiveDestructionTracker CaptureActiveFactoryBackend(IMainThreadDispatcher main,
        IActiveDestructionBackend backend, IActiveDestructionAuthority authority, LocalRunLaunchCommitState launch,
        OwnedNativeInactiveController factory)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(launch);
        if (backend is NativeLocalActiveDestructionBackend || factory.IsProductionActiveFactory)
            throw new InvalidOperationException("Controlled active retirement cannot adopt a production source or factory.");
        factory.BindControlledActiveDestructionIdentity(launch);
        return CaptureCore(main, backend, authority, launch, null, factory);
    }

    /// <summary>Called while the exact mapping is still protected and all five
    /// independent roots remain live. A null result keeps every root and permits
    /// a later poll. Success permanently stops this source's native read paths.</summary>
    internal FactoryRetirementSeal? FreezeForActiveFactoryRetirement(LocalRunLaunchCommitState launch,
        OwnedNativeInactiveController exactFactory)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(exactFactory);
        if (_factoryRetirementSeal is { } prior) { prior.ValidateFor(launch, exactFactory); return prior; }
        Guard();
        if (!ReferenceEquals(_launch, launch) || !ReferenceEquals(_activeFactory, exactFactory)
            || exactFactory.IsProductionActiveFactory != (_backend is NativeLocalActiveDestructionBackend)
            || exactFactory.CleanupUnknown || exactFactory.IsClosed || !exactFactory.ActivationAttempted
            || _identity is null || _authority.Identity != launch.Identity)
            throw new InvalidOperationException("Only this source's exact activated factory and launch can freeze its death observation.");
        if (_confirmedFrame < 0) return null;
        _busy = true;
        try
        {
            _authority.VerifyObservation(); var before = ReadReadiness();
            if (!before.Drained || !before.RequiredCutoutNaturallyEnded) return null;
            var frame = ReadFrame(before); var observed = _backend.ReadLiveness(); RequireIdentity(observed.Identity); RequireKnown();
            if (!observed.CloneDead || !observed.StagingDead)
                throw new InvalidOperationException("The five confirmed factory objects became live before the death observation was frozen.");
            exactFactory.VerifyActiveDestructionRootsBeforeFreeze(launch, _identity);
            RequireKnown();
            var after = ReadReadiness();
            if (!after.Drained || !after.RequiredCutoutNaturallyEnded) return null;
            if (ReadFrame(after) != frame || _cloneFrame <= _cutoutFrame || _confirmedFrame <= _cutoutFrame
                || _confirmedFrame > frame || _stagingAttempted && _confirmedFrame <= _stagingFrame)
                throw new InvalidOperationException("The final factory death observation lost its exact later-frame boundary.");
            exactFactory.ValidateActiveDestructionIdentity(launch, _identity);
            var seal = new FactoryRetirementSeal(this, _factoryRetirementIssuer, launch.Identity, _identity, _cutoutFrame, _cloneFrame, _confirmedFrame,
                _backend is NativeLocalActiveDestructionBackend);
            _liveness = observed; _factoryRetirementFrozen = true; _factoryRetirementSeal = seal;
            return seal;
        }
        catch (Exception error) { Fail(error); throw new NativeLocalActiveDestructionException(this, error); }
        finally { _busy = false; }
    }

    private void ValidateFactoryRetirementSeal(FactoryRetirementSeal seal, LocalRunLaunchCommitState launch,
        OwnedNativeInactiveController owner)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(owner);
        // Deliberately no Authority.VerifyObservation, RootHandle.Target, Frame
        // or Unity API here: registry mappings may already be withdrawn and a
        // retained source must not read from roots during/after factory release.
        if (!ReferenceEquals(_factoryRetirementSeal, seal) || !_factoryRetirementFrozen || _busy && !_frozenObservationReleaseBusy
            || _closed && !_frozenObservationRootsReleased || _releaseAttempted && !_frozenObservationReleaseAttempted
            || _unknown || _backend.CleanupUnknown || !ReferenceEquals(_launch, launch)
            || !ReferenceEquals(_activeFactory, owner) || seal.Session != launch.Identity || seal.Identity != _identity
            || seal.CutoutFrame != _cutoutFrame || seal.CloneFrame != _cloneFrame || seal.ConfirmedFrame != _confirmedFrame
            || seal.Production != (_backend is NativeLocalActiveDestructionBackend)
            || owner.IsProductionActiveFactory != seal.Production || launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown)
            throw new InvalidOperationException("The active factory death seal is foreign, superseded, unfrozen or permanently unknown.");
        owner.ValidateActiveDestructionIdentity(launch, seal.Identity);
    }

    internal bool TryReleaseFrozenObservationRootsAfterClose(LocalRunLaunchCommitState launch,
        OwnedNativeInactiveController exactFactory, FactoryRetirementSeal seal, NativeLocalRunLaunchCoordinator authority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(authority);
        if (!seal.Production || !exactFactory.IsProductionActiveFactory)
            throw new InvalidOperationException("Production frozen-source retirement requires its actual retained source and factory.");
        return TryReleaseFrozenObservationRootsCore(launch, exactFactory, seal,
            () => authority.VerifyActiveControllerFactoryRetirement(exactFactory, seal));
    }

    // Controlled backend exercises the same once-only transaction. It cannot
    // issue a production source seal, cancellation receipt or consumer Close.
    internal bool TryReleaseFrozenObservationRootsAfterCloseBackend(LocalRunLaunchCommitState launch,
        OwnedNativeInactiveController exactFactory, FactoryRetirementSeal seal, Action verifyControlledAuthority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(verifyControlledAuthority);
        if (seal.Production || exactFactory.IsProductionActiveFactory)
            throw new InvalidOperationException("Controlled frozen-source cleanup cannot consume production ownership.");
        return TryReleaseFrozenObservationRootsCore(launch, exactFactory, seal, verifyControlledAuthority);
    }

    private bool TryReleaseFrozenObservationRootsCore(LocalRunLaunchCommitState launch,
        OwnedNativeInactiveController factory, FactoryRetirementSeal seal, Action verifyAuthority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(seal);
        if (_frozenObservationReleaseBusy) throw new InvalidOperationException("Frozen source retirement cannot reenter.");
        seal.ValidateFor(launch, factory);
        if (_frozenObservationRootsReleased) return true;
        if (_frozenObservationReleaseAttempted || _releaseAttempted) return false;
        void Verify()
        {
            seal.ValidateFor(launch, factory);
            factory.VerifyActiveRootsClosedForObservationRetirement(launch, seal);
            verifyAuthority();
        }
        // Validate before consuming the sole release attempt. From this point
        // onward all checks are scalar/held receipts; no Unity call, Frame read
        // or RootHandle.Target is allowed on the frozen source.
        Verify();
        _frozenObservationReleaseBusy = _busy = true;
        _frozenObservationReleaseAttempted = _releaseAttempted = true;
        try
        {
            _backend.ReleaseObservationRoots(() => { Verify(); return true; });
            Verify();
            if (_backend.RootRecords != 0 || _backend.CleanupUnknown)
                throw new InvalidOperationException("Frozen source did not complete all five independent observation-root frees.");
            _closed = _frozenObservationRootsReleased = true;
            _phase = NativeLocalActiveDestructionPhase.Released;
            lock (Gate) Owners.Remove(this);
            return true;
        }
        catch (Exception error) { Fail(error); throw new NativeLocalActiveDestructionException(this, error); }
        finally { _frozenObservationReleaseBusy = _busy = false; }
    }
}
