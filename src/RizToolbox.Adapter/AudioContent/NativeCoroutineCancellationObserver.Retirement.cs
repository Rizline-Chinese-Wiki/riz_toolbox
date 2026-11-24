using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeCoroutineCancellationObserver : INativeLocalObserverRetirementOwner, INativeLocalObserverRetirementBackend
{
    private NativeLocalObserverRetirementTransaction? _activeObserverRetirement;
    internal NativeLocalObserverRetirementReceipt? TryRetireAfterActiveClose(NativeLocalActiveObserverRetirementContext context)
    {
        _main.VerifyAccess(); _activeObserverRetirement ??= new(_main, this, context);
        void Facts()
        {
            var value = _ledger.Read();
            if (Unknown || value.Unknown || value.Pending || value.Outstanding != 0 || !value.CanConfirmRequestIdle
                || value.HasObservedRequests && !value.EngineCancelled || _launch.CoroutineCancellationPending)
                throw new InvalidOperationException("Stop observers retain an exact request/engine-completion obligation.");
        }
        return _activeObserverRetirement.TryComplete(context, this, Facts,
            () => { lock (Gate) Owners.Remove(this); }, _ => Fault());
    }
    void INativeLocalObserverRetirementOwner.RequireRetirementTransaction(NativeLocalObserverRetirementTransaction transaction,
        NativeLocalActiveObserverRetirementContext context)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_activeObserverRetirement, transaction) || !ReferenceEquals(_launch, context.Launch)
            || !context.Production || Unknown || !_ready)
            throw new InvalidOperationException("Stop retirement lost its exact owner/launch transaction.");
    }
    IDisposable[] INativeLocalObserverRetirementBackend.RetirementBindings()
        => _bindings.Select(binding => (IDisposable)(binding ?? throw new InvalidOperationException("Stop binding absent."))).ToArray();
    ExactHookBroker.ManagedBoundaryActivity INativeLocalObserverRetirementBackend.ReadRetirementActivity() => ReadActivity();
    int INativeLocalObserverRetirementBackend.RetirementRootSlots => 0;
    int INativeLocalObserverRetirementBackend.RetirementRootCount => 0;
    bool INativeLocalObserverRetirementBackend.HasRetirementRoot(int slot) => throw new ArgumentOutOfRangeException(nameof(slot));
    void INativeLocalObserverRetirementBackend.FreeRetirementRootOnce(int slot) => throw new ArgumentOutOfRangeException(nameof(slot));
}
