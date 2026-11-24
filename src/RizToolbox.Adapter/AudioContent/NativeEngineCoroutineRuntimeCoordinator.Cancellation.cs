using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

// Neither a copied snapshot nor an SDK caller can instantiate a usable receipt.
// The constructor requires the actual private issuance interval and token of the
// same engine coordinator that owns all four concrete L/M/N/identity ledgers.
internal sealed class NativeCoroutineCancellationCompletionReceipt
{
    private readonly NativeEngineCoroutineRuntimeCoordinator _owner;
    private readonly NativeCoroutineCancellationLedger _ledger;
    private readonly LocalRunLaunchCommitState _launch;
    private readonly NativeLocalActiveSourceClosureSeal _sources;
    private readonly NativeCoroutineCancellationLedger.CompletionCandidate[] _candidates;
    internal long RequestVersion { get; }
    internal NativeCoroutineCancellationCompletionReceipt(NativeEngineCoroutineRuntimeCoordinator owner, object issuer,
        NativeCoroutineCancellationLedger ledger, LocalRunLaunchCommitState launch, NativeLocalActiveSourceClosureSeal sources,
        long requestVersion, IReadOnlyList<NativeCoroutineCancellationLedger.CompletionCandidate> candidates)
    {
        if (!owner.IsIssuingCancellationCompletion(issuer, ledger, launch))
            throw new InvalidOperationException("Only the actual engine coordinator may issue cancellation completion.");
        (_owner, _ledger, _launch, _sources, RequestVersion, _candidates) =
            (owner, ledger, launch, sources, requestVersion, candidates.ToArray());
    }
    internal bool IsCurrentFor(NativeCoroutineCancellationLedger ledger)
        => ReferenceEquals(ledger, _ledger) && _owner.ValidateCancellationCompletion(this, _ledger, _launch, _sources, _candidates);
    internal bool ValidateForLaunch(LocalRunLaunchCommitState launch)
        => ReferenceEquals(launch, _launch) && IsCurrentFor(_ledger);
    internal bool MatchesLaunchScalar(LocalRunLaunchCommitState launch)
        => ReferenceEquals(launch, _launch) && _owner.IsRetainedCancellationCompletion(this, _ledger, _launch);
    internal IReadOnlyList<NativeCoroutineCancellationCompletionSnapshot> ReadCompletedCandidates()
        => Array.AsReadOnly(_candidates.Select(candidate => candidate.Read()).ToArray());
}

internal sealed partial class NativeEngineCoroutineRuntimeCoordinator
{
    private readonly object _cancellationCompletionIssuer = new();
    private readonly List<NativeCoroutineCancellationCompletionReceipt> _cancellationCompletionRoots = new();
    private NativeCoroutineCancellationCompletionReceipt? _cancellationCompletion;
    private bool _issuingCancellationCompletion, _confirmingCancellationCompletion;

    internal bool TryConfirmCancellationCompletion(NativeLocalActiveSourceClosureSeal sourceSeal)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(sourceSeal);
        if (_confirmingCancellationCompletion) { Fault("Cancellation terminal verification reentered its exact coordinator."); return false; }
        _confirmingCancellationCompletion = true;
        try
        {
            if (Unknown || !_installed || _identities is null || _inline is null || _scheduler is null || _wait is null) return false;
            sourceSeal.ValidateFor(_launch);
            if (!sourceSeal.CoversEngineCancellationSources(this)) return false;
            var ledger = _cancellation.CompletionLedger;
            var requests = ledger.Read();
            if (requests.Unknown || requests.Outstanding != 0 || !requests.CanConfirmRequestIdle) return false;
            if (!requests.HasObservedRequests) return !requests.Pending; // No cancellation event is invented.
            if (_cancellationCompletion is not null && _cancellationCompletion.IsCurrentFor(ledger))
                return _launch.TryConsumeCancellationCompletion(_cancellationCompletion);
            var version = _launch.CoroutineCancellationRequestVersion;
            var candidates = ledger.ReadCompletionCandidates();
            if (!CanCompleteCancellation(ledger, candidates, sourceSeal)) return false;
            NativeCoroutineCancellationCompletionReceipt receipt;
            _issuingCancellationCompletion = true;
            try { receipt = new(this, _cancellationCompletionIssuer, ledger, _launch, sourceSeal, version, candidates); }
            finally { _issuingCancellationCompletion = false; }
            _cancellationCompletionRoots.Add(receipt); _cancellationCompletion = receipt;
            _cancellation.AcceptEngineCompletion(receipt);
            return _launch.TryConsumeCancellationCompletion(receipt);
        }
        catch (Exception error) { Fault(error.Message); return false; }
        finally { _confirmingCancellationCompletion = false; }
    }
    internal bool IsIssuingCancellationCompletion(object issuer, NativeCoroutineCancellationLedger ledger, LocalRunLaunchCommitState launch)
        => _issuingCancellationCompletion && ReferenceEquals(issuer, _cancellationCompletionIssuer)
            && ReferenceEquals(launch, _launch) && ReferenceEquals(ledger, _cancellation.CompletionLedger) && !Unknown;
    internal bool IsRetainedCancellationCompletion(NativeCoroutineCancellationCompletionReceipt receipt,
        NativeCoroutineCancellationLedger ledger, LocalRunLaunchCommitState launch)
        => ReferenceEquals(receipt, _cancellationCompletion) && _cancellationCompletionRoots.Contains(receipt)
            && ReferenceEquals(ledger, _cancellation.CompletionLedger) && ReferenceEquals(launch, _launch)
            && _launch.CoroutineCancellationRequestVersion == receipt.RequestVersion && !Unknown && !_cancellation.Unknown;
    internal bool ValidateCancellationCompletion(NativeCoroutineCancellationCompletionReceipt receipt,
        NativeCoroutineCancellationLedger ledger, LocalRunLaunchCommitState launch, NativeLocalActiveSourceClosureSeal sources,
        IReadOnlyList<NativeCoroutineCancellationLedger.CompletionCandidate> candidates)
    {
        _main.VerifyAccess();
        if (!IsRetainedCancellationCompletion(receipt, ledger, launch)) return false;
        try { sources.ValidateFor(_launch); return CanCompleteCancellation(ledger, candidates, sources); }
        catch (Exception error) { Fault(error.Message); return false; }
    }
    private bool CanCompleteCancellation(NativeCoroutineCancellationLedger ledger,
        IReadOnlyList<NativeCoroutineCancellationLedger.CompletionCandidate> candidates, NativeLocalActiveSourceClosureSeal sources)
    {
        if (Unknown || _identities is null || _inline is null || _scheduler is null || _wait is null
            || !sources.CoversEngineCancellationSources(this) || !ledger.HasCompleteRequestCoverage(candidates)) return false;
        return NativeEngineCancellationTerminalVerifier.Verify(ledger,candidates,_runtime,_identities,_inline,_scheduler,_wait);
    }
}
