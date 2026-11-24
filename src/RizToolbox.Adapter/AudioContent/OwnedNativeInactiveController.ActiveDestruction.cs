using RizToolbox.Runtime;

namespace RizToolbox.Adapter.AudioContent;

internal enum ActiveDestructionObject { Controller, CloneGameObject, CloneTransform, StagingGameObject, StagingTransform }

internal sealed partial class OwnedNativeInactiveController
{
    private bool _activeDestructionExportAttempted;
    private LocalRunLaunchCommitState? _activeDestructionLaunch;
    private ActiveDestructionIdentity? _activeDestructionIdentity;

    /// <summary>Borrow the factory's actual five root handles synchronously before
    /// activation. Each consumer publishes its new independent root immediately;
    /// no original root or destruction permission transfers out of this owner.</summary>
    internal void BorrowActiveDestructionRoots(LocalRunLaunchCommitState launch,
        Action<ActiveDestructionObject, NativeRuntime.RootHandle> adopt)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(adopt);
        launch.VerifyPendingOwnership(launch.Registry);
        var binding = launch.ParameterBinding;
        if (_activeDestructionExportAttempted || !_ready || _closed || _destroyRequested || CleanupUnknown || ActivationAttempted
            || _activationBackend is not NativeInactiveControllerActivationBackend || _creationAwakeWindow is null
            || _creationAwakeWindow.DisposeRequested || _creationAwakeWindow.CleanupUnknown || binding is null
            || !binding.IsBoundFor(launch.Registry, launch.Claim, launch.OwnedBundle, this))
            throw new InvalidOperationException("Destruction observers require the exact live bound production clone before activation, once only.");
        var observed = Read();
        if (observed.Controller != launch.Read().Controller || observed.LevelInformation != 0
            || observed.Parameters != binding.SetterBaseline.Parameters)
            throw new InvalidOperationException("The factory root graph differs from this launch's completed setter receipt.");
        _activeDestructionExportAttempted = true;
        _activeDestructionLaunch = launch;
        _activeDestructionIdentity = _roots.ReadActiveDestructionIdentity();
        _roots.BorrowActiveDestructionRoots(adopt);
        launch.VerifyPendingOwnership(launch.Registry); Read();
    }

    internal bool IsProductionActiveFactory => _activationBackend is NativeInactiveControllerActivationBackend;
    internal void ValidateActiveDestructionIdentity(LocalRunLaunchCommitState launch, ActiveDestructionIdentity identity)
    {
        _main.VerifyAccess();
        if (!_activeDestructionExportAttempted || !ReferenceEquals(_activeDestructionLaunch, launch)
            || _activeDestructionIdentity != identity || identity.Controller != launch.Read().Controller)
            throw new InvalidOperationException("Active factory retirement does not name this retained launch and exact exported five-object graph.");
    }
    internal void VerifyActiveDestructionRootsBeforeFreeze(LocalRunLaunchCommitState launch, ActiveDestructionIdentity identity)
    {
        ValidateActiveDestructionIdentity(launch, identity);
        if (_closed || CleanupUnknown || !ActivationAttempted || _roots.ReadActiveDestructionIdentity() != identity)
            throw new InvalidOperationException("The original retained factory roots changed before the independent death source was frozen.");
    }

    // Controlled-only capture never grants a production factory permission. It
    // exercises the same fixed identity and free transaction with fake handles.
    internal void BindControlledActiveDestructionIdentity(LocalRunLaunchCommitState launch)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch);
        if (IsProductionActiveFactory || _activeDestructionExportAttempted || !_ready || _closed || CleanupUnknown || ActivationAttempted)
            throw new InvalidOperationException("Controlled destruction capture cannot adopt a production or previously used factory.");
        launch.VerifyPendingOwnership(launch.Registry);
        var identity = _roots.ReadActiveDestructionIdentity();
        if (identity.Controller != launch.Read().Controller)
            throw new InvalidOperationException("Controlled factory identity differs from the exact pending registry controller.");
        _activeDestructionExportAttempted = true; _activeDestructionLaunch = launch; _activeDestructionIdentity = identity;
    }
}

internal sealed partial class InactiveControllerRootRecords
{
    internal ActiveDestructionIdentity ReadActiveDestructionIdentity()
    {
        _main.VerifyAccess();
        if (_attempted || _publishing || _allocationUnconfirmed || CleanupUnknown || _responsibility is null || _count != 8
            || _roots.Any(root => root is null))
            throw new InvalidOperationException("Only the complete retained factory graph has an active retirement identity.");
        var value = new ActiveDestructionIdentity(_roots[5]!.Target.ToInt64(), _roots[6]!.Target.ToInt64(),
            _roots[7]!.Target.ToInt64(), _roots[3]!.Target.ToInt64(), _roots[4]!.Target.ToInt64());
        var identities = new[] { value.Controller, value.CloneGameObject, value.CloneTransform, value.StagingGameObject, value.StagingTransform };
        if (identities.Any(id => id == 0) || identities.Distinct().Count() != 5)
            throw new InvalidOperationException("The original factory graph no longer has five distinct nonzero object identities.");
        return value;
    }
    internal void BorrowActiveDestructionRoots(Action<ActiveDestructionObject, NativeRuntime.RootHandle> adopt)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(adopt);
        if (_attempted || _publishing || _allocationUnconfirmed || CleanupUnknown || _responsibility is null || _count != 8
            || _roots.Any(root => root is null))
            throw new InvalidOperationException("Only the complete retained factory root graph can lend destruction observations.");
        // Factory order: source, sourceGO, parentName, parent, parentTransform,
        // controller, cloneGO, cloneTransform. Names avoid exporting raw indices.
        adopt(ActiveDestructionObject.Controller, _roots[5]!);
        adopt(ActiveDestructionObject.CloneGameObject, _roots[6]!);
        adopt(ActiveDestructionObject.CloneTransform, _roots[7]!);
        adopt(ActiveDestructionObject.StagingGameObject, _roots[3]!);
        adopt(ActiveDestructionObject.StagingTransform, _roots[4]!);
    }
}
