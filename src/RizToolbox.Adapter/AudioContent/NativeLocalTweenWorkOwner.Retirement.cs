using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal interface INativeLocalTweenRetirementBackend : INativeTweenCompletionWithdrawalBackend
{
    void VerifyTweenRetirementOwner(NativeLocalTweenWorkOwner owner);
    NativeTweenAdmission.RetirementBatch PrepareTweenRetirement(NativeLocalTweenWorkOwner owner);
    NativeTweenRetirementContext BindTweenRetirementContext(NativeLocalTweenWorkOwner owner);
    void WithdrawTweenObservationBindings(NativeLocalTweenWorkOwner owner);
    void ReleaseTweenBackendRoots(NativeLocalTweenWorkOwner owner, NativeTweenRetirementContext.Cut cut);
    bool TweenBackendRootsReleased { get; }
}

internal sealed partial class NativeLocalTweenWorkOwner : INativeTweenRetirementCutOwner
{
    private readonly object _retirementIssuer = new();
    private RetirementTransaction? _retirementTransaction;
    private RetirementReceipt? _retirementReceipt;
    internal bool RetirementStarted => _retirementTransaction is not null;
    // Set after the native batch commit and before the first C/Admission Free.
    // Backends must use this scalar before any legacy root or lease access,
    // including an external GC Free callback that reenters a read API.
    internal bool RetiredRootsUnavailable => _retirementTransaction?.RootsMayBeReleased == true;

    // This receipt can only be issued after WithCut's final Verify AND native
    // Release returned normally. Callback completion alone is not retirement.
    private sealed record RetirementReceipt(object Issuer, RetirementTransaction Transaction,
        NativeTweenSource.Withdrawal Withdrawal, NativeTweenAdmission.RetirementBatch Batch);

    internal void PumpTweenRetirement(LocalRunLaunchCommitState launch, NativeLocalRunLaunchCoordinator coordinator)
    {
        _main.VerifyAccess();
        if (_backend is not NativeLocalTweenBackend || !ReferenceEquals(_launch, launch) || !ReferenceEquals(_coordinator, coordinator))
            throw new InvalidOperationException("Tween retirement belongs to this exact production Return transaction.");
        PumpRetirementCore(consume => TryBorrowCompletionInterval(launch, coordinator, consume));
    }

    internal void PumpControlledTweenRetirementForTests()
    {
        _main.VerifyAccess();
        if (_backend is NativeLocalTweenBackend || _launch is not null || _coordinator is not null)
            throw new InvalidOperationException("Controlled retirement cannot authorize production work.");
        PumpRetirementCore(TryBorrowControlledCompletionIntervalForTests);
    }

    private void PumpRetirementCore(Func<Action<CompletionInterval>, bool> borrow)
    {
        if (Unknown || _retirementReceipt is not null) return;
        try
        {
            if (_retirementTransaction is { } pending)
            {
                if (pending.Frozen is null) throw new InvalidOperationException("Tween retirement preparation was not confirmed.");
                TryBorrowWithdrawnCompletion(pending.Frozen, pending.Commit);
                return;
            }
            borrow(interval => {
                if (_backend is not INativeLocalTweenRetirementBackend backend)
                    throw new InvalidOperationException("The actual Tween backend has no complete retirement responsibility.");
                var transaction = new RetirementTransaction(this, backend, _retirementIssuer);
                _retirementTransaction = transaction;
                transaction.Prepare(interval); transaction.Commit(interval);
            });
        }
        catch (Exception error) { Fault("Tween retirement: " + error.Message); }
    }

    public void VerifyTweenRetirementCut(NativeTweenRetirementContext context)
    {
        _main.VerifyAccess();
        var transaction = _retirementTransaction ?? throw new InvalidOperationException("No private Tween retirement transaction is current.");
        transaction.Verify(context);
    }
    public NativeTweenAdmission.RetirementBatch BorrowTweenAdmissionRetirementBatch(NativeTweenRetirementContext context)
    {
        VerifyTweenRetirementCut(context);
        return _retirementTransaction!.Batch!;
    }

    private bool HasRetirementReceipt()
    {
        var receipt = _retirementReceipt;
        return receipt is not null && ReferenceEquals(receipt.Issuer, _retirementIssuer)
            && ReferenceEquals(receipt.Transaction, _retirementTransaction) && receipt.Transaction.Finished
            && ReferenceEquals(receipt.Withdrawal, receipt.Transaction.Withdrawal) && receipt.Withdrawal.Committed
            && !receipt.Withdrawal.Unknown && ReferenceEquals(receipt.Batch, receipt.Transaction.Batch)
            && receipt.Batch.Phase == NativeTweenAdmissionRetirementPhase.RootsReleased && !receipt.Batch.Unknown
            && receipt.Transaction.Backend.TweenBackendRootsReleased && receipt.Transaction.Context?.Unknown == false;
    }

    private sealed class RetirementTransaction
    {
        private readonly NativeLocalTweenWorkOwner _owner;
        private readonly object _issuer;
        private CompletionInterval? _interval;
        private bool _committing;
        internal INativeLocalTweenRetirementBackend Backend { get; }
        internal NativeTweenAdmission.RetirementBatch? Batch { get; private set; }
        internal NativeTweenRetirementContext? Context { get; private set; }
        internal NativeTweenSource.Withdrawal? Withdrawal { get; private set; }
        internal FrozenCompletion? Frozen { get; private set; }
        internal bool SourcesWithdrawn { get; private set; }
        internal bool RootsMayBeReleased { get; private set; }
        internal bool Finished { get; private set; }
        internal RetirementTransaction(NativeLocalTweenWorkOwner owner, INativeLocalTweenRetirementBackend backend, object issuer)
        {
            if (!ReferenceEquals(issuer, owner._retirementIssuer)) throw new InvalidOperationException("Foreign Tween retirement issuer.");
            (_owner, Backend, _issuer) = (owner, backend, issuer);
        }
        internal void Prepare(CompletionInterval interval)
        {
            interval.ValidateCurrent(_owner, _owner._launch, _owner._coordinator);
            Backend.VerifyTweenRetirementOwner(_owner);
            Batch = Backend.PrepareTweenRetirement(_owner);
            if (!ReferenceEquals(Batch.Owner, _owner) || Batch.Production != interval.Production)
                throw new InvalidOperationException("The complete native batch belongs to another work owner or production kind.");
            Context = Backend.BindTweenRetirementContext(_owner);
            if (Context.Production != interval.Production || _owner._sourceJournal is null || _owner._sourceRootSet is null)
                throw new InvalidOperationException("Tween retirement has no exact source reader and native authority.");
            interval.PrepareWithdrawal();
            Backend.WithdrawTweenObservationBindings(_owner);
            Backend.VerifyTweenCompletionBindingsWithdrawn(_owner);
            Withdrawal = _owner._sourceJournal.BeginWithdrawal(_owner._sourceRootSet);
            Frozen = interval.PrepareWithdrawal(Withdrawal);
            SourcesWithdrawn = true;
        }
        internal void Verify(NativeTweenRetirementContext context)
        {
            if (!ReferenceEquals(_issuer, _owner._retirementIssuer) || !ReferenceEquals(_owner._retirementTransaction, this)
                || !ReferenceEquals(Context, context) || !_committing || _interval is null || Frozen is null || Withdrawal is null
                || Batch is null || Finished || _owner.Unknown || context.Unknown)
                throw new InvalidOperationException("Tween retirement lost its private synchronous transaction.");
            _interval.VerifyFrozenOwnership(_owner, _owner._launch, _owner._coordinator, Withdrawal);
            Backend.VerifyTweenCompletionBindingsWithdrawn(_owner);
        }
        internal void Commit(CompletionInterval interval)
        {
            if (_committing || Finished || Frozen is null || Withdrawal is null || Batch is null || Context is null)
                throw new InvalidOperationException("Tween retirement is incomplete, ended or reentered.");
            _interval = interval; _committing = true;
            try
            {
                Verify(Context);
                var completed = Context.WithCut(Withdrawal, cut => {
                    Batch.CommitNative(cut);
                    interval.ObserveNativeCommitted(cut);
                    RootsMayBeReleased = true;
                    _owner._listeners?.WithdrawTweenInlineWork(_owner, cut);
                    Withdrawal.CommitRelease(cut);
                    Batch.ReleaseRoots(cut);
                    Backend.ReleaseTweenBackendRoots(_owner, cut);
                    cut.VerifyCurrent();
                    if (!Withdrawal.Committed || Batch.Phase != NativeTweenAdmissionRetirementPhase.RootsReleased
                        || !Backend.TweenBackendRootsReleased)
                        throw new InvalidOperationException("Not every actual Tween root holder completed retirement.");
                });
                if (!completed) return; // No mutation; the same frozen transaction is reborrowed next frame.
                if (Context.Unknown) throw new InvalidOperationException("Tween native cut release is Unknown.");
                Finished = true;
                _owner._retirementReceipt = new(_issuer, this, Withdrawal, Batch);
                lock (Gate) Owners.Remove(_owner);
            }
            finally { _interval = null; _committing = false; }
        }
    }
}
