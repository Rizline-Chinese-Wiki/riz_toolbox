namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalCutoutCoroutineTracker : INativeLocalObserverRetirementOwner
{
    private NativeLocalObserverRetirementTransaction? _activeObserverRetirement;
    internal NativeLocalObserverRetirementReceipt? TryRetireAfterActiveClose(NativeLocalActiveObserverRetirementContext context)
    {
        _main.VerifyAccess();
        if (_launch is null || _backend is not NativeLocalCutoutCoroutineBackend backend)
            throw new InvalidOperationException("Production cutout retirement requires its actual retained backend.");
        _activeObserverRetirement ??= new(_main, this, context);
        void Facts()
        {
            if (Unknown || CoroutineCancellationPending || _recycling || _live != 0 || _backend.RootRecords != 0
                || _records.Any(record => record.Reserved || record.Start.Active || record.Move.Active))
                throw new InvalidOperationException("Every Cutout iterator generation must finish its exact existing root-retirement permit first.");
        }
        return _activeObserverRetirement.TryComplete(context, backend, Facts,
            () => { _ready = false; lock (Gate) Owners.Remove(this); }, Fault);
    }
    void INativeLocalObserverRetirementOwner.RequireRetirementTransaction(NativeLocalObserverRetirementTransaction transaction,
        NativeLocalActiveObserverRetirementContext context)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_activeObserverRetirement, transaction) || !ReferenceEquals(_launch, context.Launch)
            || !context.Production || Unknown)
            throw new InvalidOperationException("Cutout retirement lost its exact owner/launch transaction.");
    }
}

internal sealed partial class NativeLocalCutoutCoroutineBackend : INativeLocalObserverRetirementBackend
{
    IDisposable[] INativeLocalObserverRetirementBackend.RetirementBindings()
        => _bindings.Select(binding => (IDisposable)(binding ?? throw new InvalidOperationException("Cutout binding absent."))).ToArray();
    RizToolbox.Runtime.Hooks.ExactHookBroker.ManagedBoundaryActivity INativeLocalObserverRetirementBackend.ReadRetirementActivity() => ReadActivity();
    int INativeLocalObserverRetirementBackend.RetirementRootSlots => _roots.Length;
    int INativeLocalObserverRetirementBackend.RetirementRootCount => RootRecords;
    bool INativeLocalObserverRetirementBackend.HasRetirementRoot(int slot) => _roots[slot] is not null;
    void INativeLocalObserverRetirementBackend.FreeRetirementRootOnce(int slot)
        => throw new InvalidOperationException("Whole-owner closure cannot bypass a Cutout generation's actual engine retirement permit.");
}
