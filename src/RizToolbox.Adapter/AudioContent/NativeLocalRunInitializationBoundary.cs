using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalRunTerminationBoundaryState
{
    private ExactHookBroker.LocalInitializationIsolationBinding? _initializationBinding;
    // Fixed publication slots belong to the existing 32 graph records. Native
    // classifiers only read immutable identities from records published here.
    private readonly Record?[] _initializationControllers = new Record?[MaximumProcessOwners];
    private int _initializationFault;

    private void BindInitialization()
    {
        _initializationBinding = _backend.BindInitialization(_binding!, ClassifyInitializationController,
            new(BeforeInitializationGetter, AfterInitializationGetter, InitializationFault));
        if (_backend.RequiresInitializationIsolation && _initializationBinding?.IsActive != true)
            throw new InvalidOperationException("The complete exact initialization boundary must be active before launch reservation.");
    }
    private bool InitializationHealthy()
    {
        if (Volatile.Read(ref _initializationFault) != 0) return false;
        if (_initializationBinding is null) return !_backend.RequiresInitializationIsolation;
        return _initializationBinding.IsActive && _initializationBinding.Describe().UnknownObserved == 0;
    }
    private (long InFlight, long UnknownInFlight, bool Known) ReadInitializationActivity()
    {
        if (_initializationBinding is null) return (0, 0, !_backend.RequiresInitializationIsolation);
        var value = _initializationBinding.Read(null);
        return (value.InFlight, value.UnknownInFlight, value.Known && InitializationHealthy());
    }
    private bool InitializationDrained()
    {
        var value = ReadInitializationActivity();
        return InitializationHealthy() && value.Known && value.InFlight == 0 && value.UnknownInFlight == 0;
    }
    private void PublishInitializationController(Record entry)
    {
        if (!ReferenceEquals(_records[entry.Slot], entry) || entry.Controller == 0 || entry.Launch is null
            || Volatile.Read(ref _initializationControllers[entry.Slot]) is not null)
            throw new InvalidOperationException("An exact pending graph record is required before controller classification.");
        entry.ReservedInitializationIdentity.InitializeReservedController(entry.Controller);
        entry.InitializationIdentity = entry.ReservedInitializationIdentity;
        Volatile.Write(ref _initializationControllers[entry.Slot], entry);
    }
    private void UnpublishInitializationController(Record entry)
    {
        if (ReferenceEquals(Volatile.Read(ref _initializationControllers[entry.Slot]), entry))
            Volatile.Write(ref _initializationControllers[entry.Slot], null);
    }
    private ExactHookBroker.LocalInitializationIdentity? ClassifyInitializationController(IntPtr receiver)
    {
        // No native object/metadata/root targets or main-thread callbacks here.
        var pointer = receiver.ToInt64();
        for (var index = 0; pointer != 0 && index < _initializationControllers.Length; ++index)
        {
            var entry = Volatile.Read(ref _initializationControllers[index]);
            if (entry?.InitializationIdentity is { } identity && identity.ControllerIdentity == pointer) return identity;
        }
        return null;
    }
    private Record RequireInitializingEntry(ExactHookBroker.LocalInitializationArguments args)
    {
        _main.VerifyAccess();
        if (_busy || !InitializationHealthy()) throw new InvalidOperationException("Initialization ownership is busy or unknown.");
        var entry = _records.FirstOrDefault(item => item?.InitializationIdentity == args.Identity)
            ?? throw new InvalidOperationException("The exact pending native controller has no reserved initialization owner.");
        var launch = entry.Launch ?? throw new InvalidOperationException("Initialization requires the same pre-reserved launch.");
        launch.VerifyPendingOwnership(_host.Sessions); var state = launch.Read();
        if (entry.Unknown || !state.ActivationAttempted || state.EarlyTerminal || state.Controller != args.Controller.ToInt64()
            || entry.Controller != args.Controller.ToInt64() || entry.Roots[0] is null
            || args.StartReceiver == IntPtr.Zero || args.Information == IntPtr.Zero)
            throw new InvalidOperationException("The actual initializing invocation differs from the exact pending activation.");
        return entry;
    }
    private bool BeforeInitializationGetter(ExactHookBroker.LocalInitializationArguments args)
    {
        var entry = RequireInitializingEntry(args);
        if (entry.GetterAttempted || entry.Roots[3] is not null || entry.Roots[4] is not null)
            throw new InvalidOperationException("The actual initialization getter is once-only for this native run.");
        entry.GetterAttempted = true;
        _busy = true;
        try
        {
            entry.SettingsReceipt.BeginAttempt(entry.Identity, entry.Controller);
            var settings = _backend as INativeLocalInitializationSettingsBackend
                ?? throw new UnsupportedSignatureException("No verified settings source is available for the actual initialization getter.");
            var launch = entry.Launch!;
            var binding = launch.ParameterBinding
                ?? throw new InvalidOperationException("Actual initialization requires this launch's adopted inactive setter receipt.");
            if (!binding.IsBoundFor(launch.Registry, launch.Claim, launch.OwnedBundle, binding.ControllerResponsibilityForBinding))
                throw new InvalidOperationException("The adopted inactive setter receipt no longer belongs to this exact launch.");
            // SetterBaseline is the immutable successful read-back. Calling Read()
            // here would incorrectly demand that the now active clone stay inactive.
            var setterBaseline = binding.SetterBaseline;
            // Store each returned independent root before the next native read.
            entry.Roots[3] = _backend.RetainInitializationStart(args.StartReceiver, args.Information);
            if (entry.Roots[3]!.Target != args.StartReceiver)
                throw new InvalidOperationException("The retained Start receiver differs from the actual parent invocation.");
            AttachInitializingGraphCore(entry.Launch!, () => _backend.RetainInitializationInformation(args.Information));
            _backend.VerifyInitializationInput(entry.Roots[0]!, entry.Roots[1]!, entry.Roots[2]!, entry.Roots[3]!, entry.Launch!.OwnedBundle);
            entry.SettingsReceipt.RecordBefore(setterBaseline,
                settings.ReadInitializationParameterState(entry.Launch.OwnedBundle), settings.ReadInitializationSettings());
            // The original getter may now write controller.Info. Shared readiness
            // remains held until its exact return and all native frames leave.
            return true;
        }
        catch (Exception error) { entry.SettingsReceipt.MarkUnknown(); entry.Unknown = true; Fail(error); throw; }
        finally { _busy = false; }
    }
    private bool AfterInitializationGetter(ExactHookBroker.LocalInitializationArguments args, IntPtr returned)
    {
        var entry = RequireInitializingEntry(args);
        if (!entry.GetterAttempted || entry.GetterReturnedVerified || entry.Roots[3] is null || entry.Roots[4] is not null)
            throw new InvalidOperationException("No exact original getter return is awaiting verification.");
        _busy = true;
        try
        {
            entry.Roots[4] = _backend.RetainInitializationReturn(returned);
            if (entry.Roots[4]!.Target != returned || entry.Information != args.Information.ToInt64())
                throw new InvalidOperationException("The actual getter return/information changed before adoption.");
            _backend.VerifyInitializationReturn(entry.Roots[0]!, entry.Roots[1]!, entry.Roots[2]!, entry.Roots[3]!, entry.Roots[4]!, entry.Launch!.OwnedBundle);
            var settings = _backend as INativeLocalInitializationSettingsBackend
                ?? throw new UnsupportedSignatureException("The actual getter return has no settings receipt source.");
            entry.SettingsReceipt.VerifyReturned(entry.Identity, entry.Controller, returned.ToInt64(),
                settings.ReadInitializationParameterState(entry.Launch.OwnedBundle), settings.ReadInitializationSettings());
            entry.GetterReturnedVerified = true;
            return true;
        }
        catch (Exception error) { entry.SettingsReceipt.MarkUnknown(); entry.Unknown = true; Fail(error); throw; }
        finally { _busy = false; }
    }
    private void InitializationFault(ExactHookBroker.LocalInitializationArguments? args, string reason)
    {
        // Fault can arrive on a worker before callbacks are permitted. A sticky
        // atomic marker blocks admission/drain; the main pump later retains all
        // affected launches through its normal Fail path. Never resolve roots here.
        Interlocked.Exchange(ref _initializationFault, 1);
        for (var index = 0; index < _initializationControllers.Length; ++index)
            Volatile.Read(ref _initializationControllers[index])?.Launch?.ObserveNativeFaultFromAnyThread(reason);
    }
}

internal sealed partial class NativeLocalRunTerminationBoundaryBackend
{
    private ExactHookBroker.LocalInitializationIsolationTargets? _initializationTargets;
    private ExactHookBroker.LocalProgressPlayReservation? _progressPlayReservation;
    private IntPtr _levelControl;
    public bool RequiresInitializationIsolation => true;
    public ExactHookBroker.LocalInitializationIsolationBinding? BindInitialization(
        ExactHookBroker.LocalProgressIsolationBinding progress,
        Func<IntPtr, ExactHookBroker.LocalInitializationIdentity?> classify,
        ExactHookBroker.LocalInitializationCallbacks callbacks)
    {
        RequireSupportedBuild();
        if (_initializationTargets is null || _progressPlayReservation is null)
            throw new InvalidOperationException("LOCAL initialization requires all verified targets and the reserved Play owner.");
        try
        {
            InstallationStage = "bind initialization isolation";
            var binding = _broker.BindLocalInitializationIsolation(progress, _initializationTargets, classify, callbacks,
                playReservation: _progressPlayReservation);
            InstallationStage = null;
            return binding;
        }
        catch { CleanupUnknown = true; throw; }
    }
    public NativeRuntime.RootHandle RetainInitializationStart(IntPtr start, IntPtr information)
        => Observe(() => {
            if (start == IntPtr.Zero || information == IntPtr.Zero || _api.ObjectClass(start) != _levelControl
                || _api.ReadWithPublication<IntPtr>(start, "levelInformation") != information)
                throw new InvalidOperationException("The actual LevelControl.Start receiver does not own this getter's information argument.");
            if (!_api.InvokeValueWithPublication<bool>(_alive, IntPtr.Zero, NativeRuntime.Arg.Ref(start)))
                throw new TargetExpiredException("The actual initialization Start receiver was destroyed.");
            return _api.Root(start);
        });
    public NativeRuntime.RootHandle RetainInitializationInformation(IntPtr information)
        => Observe(() => RetainExact(information, _information));
    public NativeRuntime.RootHandle RetainInitializationReturn(IntPtr parameters)
        => Observe(() => RetainExact(parameters, _parameters));
    public void VerifyInitializationReturn(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
        NativeRuntime.RootHandle progress, NativeRuntime.RootHandle start, NativeRuntime.RootHandle parameters, IDisposable ownedBundle)
        => Observe(() => {
            var expected = VerifyOwnedInitializationInput(controller, information, progress, start, ownedBundle, true);
            if (parameters.Target.ToInt64() != expected.ParametersIdentity
                || _api.ObjectClass(parameters.Target) != _parameters)
                throw new InvalidOperationException("The original getter did not return the owned params or preserve its actual Start/controller graph.");
            return true;
        });
    public void VerifyInitializationInput(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
        NativeRuntime.RootHandle progress, NativeRuntime.RootHandle start, IDisposable ownedBundle)
        => Observe(() => VerifyOwnedInitializationInput(controller, information, progress, start, ownedBundle, false));
    private LocalNativePlayBundleObservation VerifyOwnedInitializationInput(NativeRuntime.RootHandle controller,
        NativeRuntime.RootHandle information, NativeRuntime.RootHandle progress, NativeRuntime.RootHandle start,
        IDisposable ownedBundle, bool requireControllerInformation)
    {
        if (ownedBundle is not OwnedNativeLocalPlayBundle bundle)
            throw new InvalidOperationException("The production initialization boundary requires its actual owned native bundle.");
        // Read revalidates the five original references, wrapper outputs and native
        // resources against this bundle's still-owned construction closure.
        var expected = bundle.Read();
        var parameters = _api.ReadWithPublication<IntPtr>(controller.Target, "LevelParams");
        if (parameters.ToInt64() != expected.ParametersIdentity || _api.ObjectClass(parameters) != _parameters
            || _api.ReadWithPublication<IntPtr>(start.Target, "levelInformation") != information.Target)
            throw new InvalidOperationException("The actual initialization input is not this owned bundle's exact parameter and Start graph.");
        VerifyGraphCore(controller, information, progress, bundle.MetadataIdentity, requireControllerInformation);
        return expected;
    }
    public void VerifyInitializationCompletedGraph(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
        NativeRuntime.RootHandle progress, long metadata)
        => Observe(() => {
            VerifyGraphCore(controller, information, progress, metadata, true);
            if (!_api.ReadWithPublication<bool>(information.Target, "loadOver"))
                throw new InvalidOperationException("The actual Start has not published its completed load state.");
            return true;
        });
}
