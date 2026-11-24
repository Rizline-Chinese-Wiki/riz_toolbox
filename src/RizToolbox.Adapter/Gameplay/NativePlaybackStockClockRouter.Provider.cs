using System.Collections.Concurrent;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    // These delegates are installed as the physical group's callbacks. Broker
    // notifications can arrive while a broker lock is held on a worker. Retain
    // their exact objects; never acquire a Clock/publication lock on that worker.
    private readonly ConcurrentQueue<object> _physicalFaults = new();
    private object? _firstPhysicalFault;
    private string? _physicalFailure;
    private bool _pumpingPhysicalFaults;
    internal void OnGetTimeFault(NativePlaybackSingleReturnFault fault) { VerifyRegisteredOwner(); _physicalFaults.Enqueue(fault); }
    internal void OnSourceFault(NativePlaybackSourceFault fault) { VerifyRegisteredOwner(); _physicalFaults.Enqueue(fault); }
    internal bool PhysicalFaultPending => !_physicalFaults.IsEmpty;
    internal object? FirstPhysicalFault => _firstPhysicalFault;

    private void PumpPhysicalFaults()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new GameplayStateException(ErrorCode.WrongThread, "Physical playback faults must be consumed on their owner thread.");
        if (_pumpingPhysicalFaults) return;
        _pumpingPhysicalFaults = true;
        try
        {
            // Native worker entries suppress owner-thread-only callbacks. Their
            // sticky POD health must be observed before draining this queue.
            if (_physicalFailure is null) _physicalFailure = _group?.PollOperationNativeFaults();
            while (_physicalFaults.TryDequeue(out var fault))
            {
                _firstPhysicalFault ??= fault;
                _physicalFailure ??= fault switch
                {
                    NativePlaybackSingleReturnFault value => value.Reason,
                    NativePlaybackSourceFault value => value.Reason,
                    _ => "An unrecognized physical playback fault was delivered."
                };
            }
            if (_physicalFailure is null) return;
            // Check the same retained group, even for a delayed notification.
            // A permanent physical fault applies to its current logical session.
            try { _group?.VerifyCurrent(); }
            catch { /* Preserve the original exact fault; physical failure remains sticky. */ }
            _session?.Withdraw("The physical playback group faulted: " + _physicalFailure);
        }
        finally { _pumpingPhysicalFaults = false; }
    }

    internal sealed partial class Session
    {
        private NativePlaybackStockRateProvider? _rateProvider;
        private bool _formalProvider;
        internal void RegisterFormalProvider(NativePlaybackStockRateProvider provider)
        {
            Registered();
            if (!_owner._formalRateHost || _owner._rateHost is null || _rateProvider is not null)
                throw new GameplayStateException(ErrorCode.Conflict, "Only the registered route's sole formal host can issue its concrete provider.");
            VerifyFormalClockAuthority();
            _owner.VerifySource();
            _formalProvider = true;
            _rateProvider = provider;
        }
        internal void RegisterControlledProvider(NativePlaybackStockRateProvider provider)
        {
            Registered();
            if (!_armed || _owner._group is not { OwnedFixture: true })
                throw new GameplayStateException(ErrorCode.NotReady, "Only this actual armed owned-fixture session can issue a controlled stock provider.");
            if (_rateProvider is not null)
                throw new GameplayStateException(ErrorCode.Conflict, "This logical stock session already transferred its provider ownership.");
            _owner.VerifySource();
            if (!_publication!.IsCurrent(Clock.Capture()))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The unique final Clock has no current native publication.");
            _rateProvider = provider;
        }
        internal void VerifyControlledProvider(NativePlaybackStockRateProvider provider)
        {
            VerifyControlledProviderIdentity(provider);
            try
            {
                var receipt = _owner.ObserveCurrent();
                _owner._source!.VerifyCurrentOperationReceipt(receipt);
                VerifyRateOperationPolicy();
                VerifyControlledProviderIdentity(provider);
                if (!ReferenceEquals(_owner.ObserveCurrent(), receipt))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The registered source changed its receipt during provider policy verification.");
                _owner._source!.VerifyCurrentOperationReceipt(receipt);
            }
            catch (Exception error)
            {
                // A live native channel failure ends this session on the same
                // owner turn; a copied provider is rejected before this region.
                Withdraw("The registered stock provider lost current authority: " + error.Message);
                throw;
            }
        }
        internal void VerifyControlledProviderIdentity(NativePlaybackStockRateProvider provider)
        {
            VerifyRateOperationIdentity();
            if (!ReferenceEquals(_rateProvider, provider)
                || (_formalProvider ? _owner._group is not { OwnedFixture: false }
                    || !_owner._formalRateHost
                    : _owner._group is not { OwnedFixture: true }))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied provider or another session cannot borrow this private stock registration.");
            _ = _owner.ObserveCurrent();
        }
        internal NativePlaybackGetTimeHookLease CaptureProviderHook(NativePlaybackStockRateProvider provider)
        {
            VerifyControlledProvider(provider);
            var hook = _owner._group!.GetTimeLease;
            if (!hook.IsActive || !_owner._group.ReadOperationClockPolicy(hook).Active)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The same physical GetTime lease is no longer current.");
            VerifyControlledProviderIdentity(provider);
            return hook;
        }
        internal NativePlaybackRateSourceDescriptor CaptureRateSourceDescriptor(NativePlaybackStockRateProvider provider)
        {
            VerifyControlledProvider(provider);
            var descriptor = new NativePlaybackRateSourceDescriptor(_initial.Binding, _owner._origin.Pause);
            VerifyControlledProviderIdentity(provider);
            return descriptor;
        }
        internal void PumpProvider(NativePlaybackStockRateProvider provider)
        {
            if (_owner._thread != Environment.CurrentManagedThreadId)
                throw new GameplayStateException(ErrorCode.WrongThread, "The stock provider is owner-thread bound.");
            if (!ReferenceEquals(_rateProvider, provider))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the registered provider owns this logical session.");
            _owner.PumpCompleted();
        }
        internal void DisposeProvider(NativePlaybackStockRateProvider provider)
        {
            if (_owner._thread != Environment.CurrentManagedThreadId)
                throw new GameplayStateException(ErrorCode.WrongThread, "Stock provider cleanup must retain roots for the owner thread.");
            if (!ReferenceEquals(_rateProvider, provider))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Copied provider cleanup owns no logical session.");
            // Logical withdrawal/drain only. The physical group retains its
            // installed detours and address ownership for the adapter host.
            if (ReferenceEquals(_owner._session, this)) _owner.Dispose();
            else { Withdraw("The retired logical provider was disposed."); _owner.PumpCompleted(); }
        }
    }
}
