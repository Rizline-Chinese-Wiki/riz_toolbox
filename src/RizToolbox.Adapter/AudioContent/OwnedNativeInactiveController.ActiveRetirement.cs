namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class OwnedNativeInactiveController
{
    private bool _activeRetirementBusy;
    private NativeLocalActiveDestructionTracker.FactoryRetirementSeal? _activeRetirementSeal;

    internal void VerifyActiveRootsClosedForObservationRetirement(LocalRunLaunchCommitState launch,
        NativeLocalActiveDestructionTracker.FactoryRetirementSeal seal)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(seal);
        seal.ValidateFor(launch, this);
        if (!_closed || CleanupUnknown || !_freeAttempted || !ReferenceEquals(_activeRetirementSeal, seal) || _roots.Count != 0)
            throw new InvalidOperationException("Independent death-source roots require this exact factory's successful original-root retirement.");
        var current = launch.Registry.Read(launch.Session);
        if (current.Identity != launch.Identity || current.ControllerIdentity != seal.Identity.Controller
            || current.MetadataIdentity != launch.MetadataIdentity || current.Phase != LocalRunSessionPhase.Closed
            || !current.ReturnRequested || !current.ProtectionRetired || !current.CleanupAttempted
            || current.OutstandingOwnershipRecords != 0 || string.IsNullOrWhiteSpace(current.ConsumerEndEvidence)
            || launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown)
            throw new InvalidOperationException("Frozen observation roots lost their exact successful registry closure.");
    }

    /// <summary>AfterClose action only. Whole-run native permission remains the
    /// actual launch coordinator's responsibility; this method never creates a
    /// registry token, stops a producer, destroys an object or frees a parameter.</summary>
    internal bool TryCompleteActiveDestroy(LocalRunLaunchCommitState launch,
        NativeLocalActiveDestructionTracker.FactoryRetirementSeal seal, NativeLocalRunLaunchCoordinator authority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(authority); ArgumentNullException.ThrowIfNull(seal);
        if (!IsProductionActiveFactory || !seal.Production)
            throw new InvalidOperationException("Production active retirement requires the actual factory and production destruction source.");
        return TryCompleteActiveDestroyCore(launch, seal, () => authority.VerifyActiveControllerFactoryRetirement(this, seal));
    }

    internal bool TryCompleteActiveDestroyBackend(LocalRunLaunchCommitState launch,
        NativeLocalActiveDestructionTracker.FactoryRetirementSeal seal, Action verifyControlledAuthority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(verifyControlledAuthority); ArgumentNullException.ThrowIfNull(seal);
        if (IsProductionActiveFactory || seal.Production)
            throw new InvalidOperationException("Controlled retirement cannot close a production factory or consume its seal.");
        return TryCompleteActiveDestroyCore(launch, seal, verifyControlledAuthority);
    }

    private bool TryCompleteActiveDestroyCore(LocalRunLaunchCommitState launch,
        NativeLocalActiveDestructionTracker.FactoryRetirementSeal seal, Action verifyAuthority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(seal);
        if (_activeRetirementBusy) throw new InvalidOperationException("Active factory retirement cannot reenter.");
        if (_closed)
        {
            if (!ReferenceEquals(_activeRetirementSeal, seal)) throw new InvalidOperationException("A closed factory cannot adopt another retirement seal.");
            seal.ValidateFor(launch, this);
            return true;
        }
        if (!ActivationAttempted || !_activeDestructionExportAttempted || _destroyRequested || CleanupUnknown || _freeAttempted)
            return false;
        _activeRetirementBusy = true;
        try
        {
            void Verify()
            {
                seal.ValidateFor(launch, this);
                var current = launch.Registry.Read(launch.Session);
                if (current.Identity != launch.Identity || current.ControllerIdentity != seal.Identity.Controller
                    || current.MetadataIdentity != launch.MetadataIdentity || !current.ReturnRequested || !current.ProtectionRetired
                    || !current.CleanupAttempted || current.Phase != LocalRunSessionPhase.Closed
                    || string.IsNullOrWhiteSpace(current.ConsumerEndEvidence) || current.OutstandingOwnershipRecords != 0
                    || launch.Registry.FindProtectedSession(LocalBoundaryKind.ControllerRetry, current.ControllerIdentity) is not null
                    || launch.Registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, current.ControllerIdentity) is not null
                    || current.ResultSceneIdentity != 0 && (
                        launch.Registry.FindProtectedSession(LocalBoundaryKind.ResultStart, current.ResultSceneIdentity) is not null
                        || launch.Registry.FindProtectedSession(LocalBoundaryKind.ResultRetry, current.ResultSceneIdentity) is not null
                        || launch.Registry.FindProtectedSession(LocalBoundaryKind.ResultBack, current.ResultSceneIdentity) is not null))
                    throw new InvalidOperationException("Original active factory roots require exact successful registry closure and withdrawn classifications.");
                verifyAuthority();
            }
            // Failed prerequisites consume no free attempt. Once any release can
            // occur, neither this transaction nor a failed individual free retries.
            Verify(); _freeAttempted = true; _activeRetirementSeal = seal; _ready = false;
            try { _roots.ReleaseActiveAfterClose(Verify); _closed = true; return true; }
            catch (Exception error) { _unknown = true; Failure = error.Message; throw; }
        }
        finally { _activeRetirementBusy = false; }
    }
}

internal sealed partial class InactiveControllerRootRecords
{
    internal void ReleaseActiveAfterClose(Action verifyAuthority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(verifyAuthority);
        if (_attempted || _publishing || _allocationUnconfirmed || CleanupUnknown || _responsibility is null || _count != 8
            || _roots.Any(root => root is null))
            throw new InvalidOperationException("The original complete eight-root factory graph is not available for one active retirement pass.");
        verifyAuthority(); _attempted = true; var failures = new List<Exception>(MaximumRoots);
        for (var index = _count - 1; index >= 0; --index)
        {
            try { verifyAuthority(); } catch (Exception error) { failures.Add(error); break; }
            if (_roots[index] is { } root)
                try { root.Dispose(); _roots[index] = null; } catch (Exception error) { failures.Add(error); }
            try { verifyAuthority(); } catch (Exception error) { failures.Add(error); break; }
        }
        if (failures.Count != 0)
        {
            _cleanupUnknown = true;
            throw new AggregateException("Active original factory-root cleanup is unknown; remaining roots and process capacity are retained.", failures);
        }
        lock (ProcessGate) ProcessOwners.Remove(this);
        _responsibility = null;
    }
}
