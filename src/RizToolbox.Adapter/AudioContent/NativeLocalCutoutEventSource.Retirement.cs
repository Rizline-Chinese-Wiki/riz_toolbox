namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalCutoutEventSource : INativeLocalObserverRetirementOwner
{
    private NativeLocalObserverRetirementTransaction? _activeObserverRetirement;
    internal NativeLocalObserverRetirementReceipt? TryRetireAfterActiveClose(NativeLocalActiveObserverRetirementContext context)
    {
        _main.VerifyAccess();
        if (_controlled || _launch is null || _backend is not NativeLocalCutoutEventSourceBackend backend)
            throw new InvalidOperationException("Production source retirement requires its retained actual backend.");
        _activeObserverRetirement ??= new(_main, this, context);
        void Facts()
        {
            if (ObservationAuthorityUnknown || _backend.CleanupUnknown || _busy || _source is not { Alive: false }
                || Interlocked.Read(ref _inFlight) != 0 || Interlocked.Read(ref _entered) != Interlocked.Read(ref _exited))
                throw new InvalidOperationException("Cutout source still owns live, active or Unknown dispatch responsibility.");
        }
        return _activeObserverRetirement.TryComplete(context, backend, Facts,
            () => { lock (Gate) Owners.Remove(this); }, Fail);
    }
    void INativeLocalObserverRetirementOwner.RequireRetirementTransaction(NativeLocalObserverRetirementTransaction transaction,
        NativeLocalActiveObserverRetirementContext context)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_activeObserverRetirement, transaction) || !ReferenceEquals(_launch, context.Launch)
            || !context.Production || _controlled || ObservationAuthorityUnknown || _backend.CleanupUnknown)
            throw new InvalidOperationException("Cutout source retirement transaction lost its actual owner or launch.");
    }
}

internal sealed partial class NativeLocalCutoutEventSourceBackend : INativeLocalObserverRetirementBackend
{
    private readonly bool[] _activeRetirementFreeAttempted = new bool[69];
    IDisposable[] INativeLocalObserverRetirementBackend.RetirementBindings()
        => new IDisposable[] { _binding ?? throw new InvalidOperationException("Source binding absent."),
            _probe ?? throw new InvalidOperationException("Source probe absent.") };
    RizToolbox.Runtime.Hooks.ExactHookBroker.ManagedBoundaryActivity INativeLocalObserverRetirementBackend.ReadRetirementActivity() => ReadActivity();
    int INativeLocalObserverRetirementBackend.RetirementRootSlots => _roots.Length;
    int INativeLocalObserverRetirementBackend.RetirementRootCount => RootRecords;
    bool INativeLocalObserverRetirementBackend.HasRetirementRoot(int slot) => _roots[slot] is not null;
    void INativeLocalObserverRetirementBackend.FreeRetirementRootOnce(int slot)
    {
        _main.VerifyAccess();
        if (_activeRetirementFreeAttempted[slot] || _roots[slot] is not { } root || !ObservationKnown)
            throw new InvalidOperationException("Source root is absent, Unknown or already attempted.");
        _activeRetirementFreeAttempted[slot] = true;
        try { root.Dispose(); _roots[slot] = null; } catch { CleanupUnknown = true; throw; }
    }
}
