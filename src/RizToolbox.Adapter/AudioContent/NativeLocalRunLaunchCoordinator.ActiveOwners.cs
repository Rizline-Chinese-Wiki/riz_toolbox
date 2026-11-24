namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalRunLaunchCoordinator
{
    private NativeLocalActiveObserverRetirementContext? _activeObserverContext;
    private readonly NativeLocalObserverRetirementReceipt?[] _activeObserverReceipts = new NativeLocalObserverRetirementReceipt?[5];
    private bool _wholeLaunchRetired, _activePrefabRootFreeAttempted, _consumingTerminationReceipt, _activeTerminationReceiptConsumed;
    private string? _activeFinalOwnerEvidence;
    internal string? ActiveFinalOwnerEvidence { get { _main.VerifyAccess(); return _activeFinalOwnerEvidence; } }

    private bool TryRetireActiveObserverOwners()
    {
        if (_activeSourceClosure is null) return false;
        _activeObserverContext ??= new(_reservation!.State, _controller!, _factoryRetirementSeal!, _activeSourceClosure, this);
        var context = _activeObserverContext;
        _activeObserverReceipts[0] ??= _terminalCoroutines!.TryRetireAfterActiveClose(context);
        if (_activeObserverReceipts[0] is null) return false;
        _activeObserverReceipts[0]!.ValidateFor(context, _terminalCoroutines!);
        _activeObserverReceipts[1] ??= _cutoutCoroutines!.TryRetireAfterActiveClose(context);
        if (_activeObserverReceipts[1] is null) return false;
        _activeObserverReceipts[1]!.ValidateFor(context, _cutoutCoroutines!);
        _activeObserverReceipts[2] ??= _unityEvents!.TryRetireAfterActiveClose(context);
        if (_activeObserverReceipts[2] is null) return false;
        _activeObserverReceipts[2]!.ValidateFor(context, _unityEvents!);
        _activeObserverReceipts[3] ??= _cutoutEvents!.TryRetireAfterActiveClose(context);
        if (_activeObserverReceipts[3] is null) return false;
        _activeObserverReceipts[3]!.ValidateFor(context, _cutoutEvents!);
        _activeObserverReceipts[4] ??= _coroutineCancellation!.TryRetireAfterActiveClose(context);
        if (_activeObserverReceipts[4] is null) return false;
        _activeObserverReceipts[4]!.ValidateFor(context, _coroutineCancellation!);
        VerifyActiveObserverReceiptTable(); return true;
    }

    private void VerifyActiveObserverReceiptTable()
    {
        if (_activeObserverContext is null) throw new InvalidOperationException("Active observer cleanup lost its one exact context.");
        object[] owners = { _terminalCoroutines!, _cutoutCoroutines!, _unityEvents!, _cutoutEvents!, _coroutineCancellation! };
        if (owners.Any(owner => owner is null) || owners.Distinct(ReferenceEqualityComparer.Instance).Count() != 5)
            throw new InvalidOperationException("Active observer owners are absent or aliased.");
        for (var index = 0; index < owners.Length; ++index)
        {
            var receipt = _activeObserverReceipts[index]
                ?? throw new InvalidOperationException("An actual observer owner has no completed retirement receipt.");
            receipt.ValidateFor(_activeObserverContext, owners[index]);
            if (receipt.Session != _reservation!.State.Identity)
                throw new InvalidOperationException("Observer retirement receipt changed its exact run generation.");
        }
    }

    private void TickActiveOwnerRetirement()
    {
        if (_wholeLaunchRetired) return;
        VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
        VerifyActiveObserverReceiptTable();
        var launch = _reservation!.State;
        if (!_globals!.IsClosed)
        {
            var verifier = new ActiveGlobalsRetirementVerifier(this, _globals, launch);
            var token = _globals.VerifyConsumersEnded(verifier);
            if (token is null) return;
            _globals.ReleaseAfterActiveConsumersEnded(token, launch, this);
        }
        VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
        if (!_menu!.Read().Closed && !_menu.TryReleaseAfterRestored()) return;
        VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
        _window!.Dispose();
        if (_window.CleanupUnknown || _window.RootRecords != 0 || !_window.DisposeRequested) return;
        if (_prefabRoot is not null)
        {
            if (_activePrefabRootFreeAttempted) throw new InvalidOperationException("Prefab root cleanup is Unknown and cannot be retried.");
            VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
            _activePrefabRootFreeAttempted = true; _prefabRoot.Dispose(); _prefabRoot = null;
            VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
        }
        if (!_prefab!.IsDisposed) _prefab.Dispose();
        VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
        // Registry Close requests asynchronous Unity destruction. Keep pumping
        // its actual resource owner until later independent liveness reads prove
        // destruction; a managed Dispose return does not mean final retirement.
        if (!_bundle!.TryCompleteDestroy()) return;
        if (!_handler.HasRetiredActiveConsumers(launch)) return;
        var rows = ReadFinalActiveOwnerTable();
        if (!rows.All(row => row.Completed)) return;
        VerifyActiveObserverReceiptTable();
        VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
        _activeFinalOwnerEvidence = string.Join("; ", rows.Select(row => row.Name + "=completed"));
        _consumingTerminationReceipt = true;
        try { _handler.ConsumeRetiredActiveLaunch(launch, this); _activeTerminationReceiptConsumed = true; }
        finally { _consumingTerminationReceipt = false; }
        _wholeLaunchRetired = true;
        // Only this exact run owner is removed. Permanent process-wide native
        // callback owners and their registered code/unwind remain retained.
        lock (Gate) ProcessOwners.Remove(this);
        _modification?.ReleaseAfterReturn();
    }

    private IReadOnlyList<(string Name, bool Completed)> ReadFinalActiveOwnerTable()
    {
        var launch = _reservation!.State; var current = _host.Sessions.Read(_session!);
        var init = _initialCoroutines!.Read(); var menu = _menu!.Read(); var death = _activeDestruction!.Read();
        return new (string, bool)[] {
            ("registry", current.Identity == launch.Identity && current.Phase == LocalRunSessionPhase.Closed
                && current.ProtectionRetired && current.CleanupAttempted && current.OutstandingOwnershipRecords == 0),
            ("factory", _controller!.IsClosed && !_controller.CleanupUnknown),
            ("parameters", _parameters!.IsClosed && !_parameters.CleanupUnknown),
            ("initialization", init.Closed && !init.Unknown && init.Roots == 0),
            ("five-observer-receipts", _activeObserverReceipts.All(receipt => receipt is not null)),
            ("engine", _engineCoroutines!.ActiveObservationRootsReleased),
            ("death-source", death.Closed && !death.Unknown && death.Roots == 0),
            ("return-scene-manager", _activeReturnObserversReleased && _activeReturnManager is null
                && _activeReturnOperation?.Read().Closed == true && _sceneTransition!.Read().Closed),
            ("reservation", _reservation.UntransferredCleanupCompleted),
            ("globals", _globals!.IsClosed && !_globals.CleanupUnknown && _globals.OutstandingRootRecords == 0),
            ("menu", menu.Closed && !menu.Unknown && menu.Roots == 0 && !menu.NormalCreditHeld && !menu.BackCreditHeld),
            ("awake-window", _window!.DisposeRequested && !_window.CleanupUnknown && _window.RootRecords == 0),
            ("prefab", _prefabRoot is null && _prefab!.IsDisposed && !_prefab.CleanupUnknown && _prefab.OutstandingRootRecords == 0),
            ("bundle", _bundle!.IsClosed && !_bundle.CleanupUnknown && _bundle.DirectRootRecords == 0
                && _bundle.RetainedCallArgumentRecords == 0),
            ("termination", _activeTerminationReceiptConsumed || _handler.HasRetiredActiveConsumers(launch))
        };
    }

    internal void VerifyActiveTerminationReceiptConsumption(LocalRunTerminationHandler actualHandler, LocalRunLaunchCommitState exactLaunch)
    {
        if (!_consumingTerminationReceipt || _activeTerminationReceiptConsumed || _wholeLaunchRetired
            || !ReferenceEquals(_handler, actualHandler) || !ReferenceEquals(_reservation?.State, exactLaunch)
            || !_factoryRootsReleased || string.IsNullOrWhiteSpace(_activeFinalOwnerEvidence))
            throw new InvalidOperationException("Only the actual final all-owner completion interval may consume its exact termination receipt.");
        VerifyActiveObserverReceiptTable(); VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
        if (!ReadFinalActiveOwnerTable().All(row => row.Completed))
            throw new InvalidOperationException("A native owner became incomplete before final termination receipt consumption.");
    }

    internal void VerifyActiveGlobalsRetirement(OwnedNativeControllerGlobals exactOwner, LocalRunLaunchCommitState launch)
    {
        if (!ReferenceEquals(_globals, exactOwner) || !ReferenceEquals(_reservation?.State, launch)
            || !_factoryRootsReleased || _wholeLaunchRetired || !_activeReturnMenuRestored)
            throw new InvalidOperationException("Global roots require this actual retained return's exact post-Close owner.");
        VerifyActiveObserverReceiptTable(); VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
    }
    private sealed class ActiveGlobalsRetirementVerifier : IControllerGlobalsConsumersEndedVerifier
    {
        private readonly NativeLocalRunLaunchCoordinator _owner;
        private readonly OwnedNativeControllerGlobals _globals;
        private readonly LocalRunLaunchCommitState _launch;
        internal ActiveGlobalsRetirementVerifier(NativeLocalRunLaunchCoordinator owner,
            OwnedNativeControllerGlobals globals, LocalRunLaunchCommitState launch) => (_owner, _globals, _launch) = (owner, globals, launch);
        public bool Verify(ControllerGlobalsRetirementRequest request, out string evidence)
        {
            evidence = string.Empty; _owner.VerifyActiveGlobalsRetirement(_globals, _launch);
            if (request.Controller != _launch.Read().Controller || !request.InstalledCaptureAttempted
                || request.Restoration is not { AllDelegatesDetached: true, SameManagerOwner: true }) return false;
            var proof = _globals.ReadActiveConsumerClosure(_launch, NativeLocalActiveConsumerObligation.GlobalReferences);
            if (proof.State != NativeLocalActiveConsumerProofState.Proven) return false;
            _owner.VerifyActiveGlobalsRetirement(_globals, _launch);
            evidence = "Exact active return closed all consumers; current retained manager and six globals independently exclude the old controller.";
            return true;
        }
    }
}
