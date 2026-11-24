namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalUnityEventLedger : INativeLocalObserverRetirementOwner
{
    private NativeLocalObserverRetirementTransaction? _activeObserverRetirement;
    internal NativeLocalObserverRetirementReceipt? TryRetireAfterActiveClose(NativeLocalActiveObserverRetirementContext context)
    {
        _main.VerifyAccess();
        if (_launch is null || _backend is not NativeLocalUnityEventBackend backend)
            throw new InvalidOperationException("Production listener retirement requires its actual retained backend.");
        _activeObserverRetirement ??= new(_main, this, context);
        void Facts()
        {
            var value = Read();
            if (Unknown || _busy || _initializing || _physicalDepth != 0 || _current is not null || value.NativeInFlight != 0
                || value.Invocations != value.ReturnedInvocations || value.InvokableDispatches != value.ReturnedDispatches
                || value.ObservedMutations != value.ReturnedMutations || value.AbnormalDispatches != 0)
                throw new InvalidOperationException("UnityEvent owner retains a physical invocation, mutation or invokable completion duty.");
        }
        return _activeObserverRetirement.TryComplete(context, backend, Facts,
            () => { _ready = false; lock (Gate) Owners.Remove(this); }, FailActiveRetirement);
    }
    private void FailActiveRetirement(string failure)
    { _failure ??= failure; Interlocked.Exchange(ref _fault, 1); _authority.ObserveFaultFromAnyThread(); }
    void INativeLocalObserverRetirementOwner.RequireRetirementTransaction(NativeLocalObserverRetirementTransaction transaction,
        NativeLocalActiveObserverRetirementContext context)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_activeObserverRetirement, transaction) || !ReferenceEquals(_launch, context.Launch)
            || !context.Production || Unknown)
            throw new InvalidOperationException("UnityEvent retirement lost its exact owner/launch transaction.");
    }
}

internal sealed partial class NativeLocalUnityEventBackend : INativeLocalObserverRetirementBackend
{
    private readonly bool[] _activeRetirementFreeAttempted = new bool[MaximumRoots];
    IDisposable[] INativeLocalObserverRetirementBackend.RetirementBindings()
        => new IDisposable[] { _invokeBinding ?? throw new InvalidOperationException("Invoke binding absent."),
            _prepareBinding ?? throw new InvalidOperationException("Prepare binding absent."),
            _invokeProbe ?? throw new InvalidOperationException("Invoke probe absent.") }.Concat(_mutationBindings).ToArray();
    RizToolbox.Runtime.Hooks.ExactHookBroker.ManagedBoundaryActivity INativeLocalObserverRetirementBackend.ReadRetirementActivity() => ReadActivity();
    int INativeLocalObserverRetirementBackend.RetirementRootSlots => _roots.Length;
    int INativeLocalObserverRetirementBackend.RetirementRootCount => RootRecords;
    bool INativeLocalObserverRetirementBackend.HasRetirementRoot(int slot) => _roots[slot] is not null;
    void INativeLocalObserverRetirementBackend.FreeRetirementRootOnce(int slot)
    {
        _main.VerifyAccess();
        if (_activeRetirementFreeAttempted[slot] || _roots[slot] is not { } root || !ObservationKnown)
            throw new InvalidOperationException("Listener root is absent, Unknown or already attempted.");
        _activeRetirementFreeAttempted[slot] = true;
        try { root.Dispose(); _roots[slot] = null; } catch { CleanupUnknown = true; throw; }
    }
}
