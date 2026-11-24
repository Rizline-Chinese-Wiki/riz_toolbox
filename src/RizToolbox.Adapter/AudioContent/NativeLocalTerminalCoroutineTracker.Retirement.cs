namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTerminalCoroutineTracker : INativeLocalObserverRetirementOwner
{
    private NativeLocalObserverRetirementTransaction? _activeObserverRetirement;
    internal NativeLocalObserverRetirementReceipt? TryRetireAfterActiveClose(NativeLocalActiveObserverRetirementContext context)
    {
        _main.VerifyAccess();
        if (_launch is null || _backend is not NativeLocalTerminalCoroutineBackend backend)
            throw new InvalidOperationException("Production terminal retirement requires its actual retained backend.");
        _activeObserverRetirement ??= new(_main, this, context);
        void Facts()
        {
            if (Unknown || CoroutineCancellationPending || _recycling || _result.Active || _currentNamed is not null
                || _records.Any(record => record.Reserved || record.Factory.Active || record.Start.Active || record.Move.Active)
                || _backend.RootRecords != 0 || _count != 0 || !_resultReturned || !_cutoutNamedReturned || !_cutoutNaturallyEnded)
                throw new InvalidOperationException("Terminal generation roots must complete their actual per-record engine retirement before owner closure.");
        }
        return _activeObserverRetirement.TryComplete(context, backend, Facts,
            () => { _closed = true; lock (Gate) Owners.Remove(this); }, Fault);
    }
    void INativeLocalObserverRetirementOwner.RequireRetirementTransaction(NativeLocalObserverRetirementTransaction transaction,
        NativeLocalActiveObserverRetirementContext context)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_activeObserverRetirement, transaction) || !ReferenceEquals(_launch, context.Launch)
            || !context.Production || Unknown)
            throw new InvalidOperationException("Terminal retirement lost its exact owner/launch transaction.");
    }
}

internal sealed partial class NativeLocalTerminalCoroutineBackend : INativeLocalObserverRetirementBackend
{
    IDisposable[] INativeLocalObserverRetirementBackend.RetirementBindings()
        => _bindings.Select(binding => (IDisposable)(binding ?? throw new InvalidOperationException("Terminal binding absent."))).ToArray();
    RizToolbox.Runtime.Hooks.ExactHookBroker.ManagedBoundaryActivity INativeLocalObserverRetirementBackend.ReadRetirementActivity() => ReadActivity();
    int INativeLocalObserverRetirementBackend.RetirementRootSlots => _roots.Length;
    int INativeLocalObserverRetirementBackend.RetirementRootCount => RootRecords;
    bool INativeLocalObserverRetirementBackend.HasRetirementRoot(int slot) => _roots[slot] is not null;
    void INativeLocalObserverRetirementBackend.FreeRetirementRootOnce(int slot)
        => throw new InvalidOperationException("Whole-owner closure cannot bypass an iterator's existing exact engine retirement permit.");
}
