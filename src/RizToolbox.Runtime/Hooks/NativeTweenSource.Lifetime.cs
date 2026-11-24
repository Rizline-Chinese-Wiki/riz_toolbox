namespace RizToolbox.Runtime.Hooks;

internal enum NativeTweenSourceRootRetention { ReaderOwned, OperationBorrow, ReferenceHubBorrow }

internal sealed partial class NativeTweenSource
{
    private NativeTweenSourceReferences.ReaderRegistration? _referenceRegistration;
    private SourceReferenceHub? _referenceHub;
    private readonly List<Withdrawal> _withdrawals = new();
    private ulong _withdrawalGeneration;

    internal bool IsReferenceObserver(NativeTweenSourceReferences references) => ReferenceEquals(_references, references);

    /// <summary>Permanently closes this exact root set's new borrowing and
    /// freezes its reader cutoff. This is not work completion, native rundown,
    /// an admission unregister receipt or permission to free any root.</summary>
    internal Withdrawal BeginWithdrawal(NativeTweenSourceRootSet roots)
    {
        _verify(); VerifySet(roots); RequireKnown(); roots.Owner.VerifyTweenSourceOwner();
        if (roots.Withdrawal is not null) throw new InvalidOperationException("This source root set already owns its one withdrawal transaction.");
        var transaction = new Withdrawal(this, roots, checked(++_withdrawalGeneration), _issuer);
        _withdrawals.Add(transaction); roots.BeginWithdrawal(transaction, _issuer);
        try
        {
            if (_referenceHub is { } hub && !_sets.Any(set => set.EnrollmentOpen)) { hub.Remove(this); _referenceHub = null; }
            var info = ReadInformation();
            transaction.Freeze(info.Events, _references?.ObserverEpoch ?? 0, _references?.LastDeliverySequence ?? 0,
                _referenceRegistration?.Generation ?? 0, _issuer);
            roots.Owner.VerifyTweenSourceOwner(); return transaction;
        }
        catch (Exception error) { transaction.Fail(error, _issuer); Fault(); throw; }
    }

    internal sealed class Withdrawal
    {
        private readonly object _issuer;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private bool _frozen, _committing;
        internal NativeTweenSource Reader { get; }
        internal NativeTweenSourceRootSet Roots { get; }
        internal INativeTweenSourceOwner Owner => Roots.Owner;
        internal ulong Generation { get; }
        internal ulong SourceEpoch => Reader._epoch;
        internal ulong JournalCutoff { get; private set; }
        internal ulong ReferenceObserverEpoch { get; private set; }
        internal ulong ReferenceDeliveryCutoff { get; private set; }
        internal ulong ReferenceReaderGeneration { get; private set; }
        internal bool Production => Reader.Production;
        internal bool Committed { get; private set; }
        internal bool Unknown { get; private set; }
        internal Exception? Failure { get; private set; }

        internal Withdrawal(NativeTweenSource reader, NativeTweenSourceRootSet roots, ulong generation, object issuer)
        {
            if (!reader.IsIssuer(issuer)) throw new InvalidOperationException("Foreign source withdrawal issuer.");
            (Reader, Roots, Generation, _issuer) = (reader, roots, generation, issuer);
        }
        internal void Freeze(ulong journal, ulong observer, ulong delivery, ulong registration, object issuer)
        {
            if (!ReferenceEquals(issuer, _issuer) || _frozen) throw new InvalidOperationException("Source withdrawal cutoff can be frozen only once.");
            (JournalCutoff, ReferenceObserverEpoch, ReferenceDeliveryCutoff, ReferenceReaderGeneration, _frozen)
                = (journal, observer, delivery, registration, true);
        }
        internal void VerifyCurrent() => VerifyIdentity(allowCommitting: false);
        private void VerifyIdentity(bool allowCommitting)
        {
            Reader._verify();
            if (_thread != Environment.CurrentManagedThreadId || !Reader.IsIssuer(_issuer) || !Reader._withdrawals.Contains(this)
                || !ReferenceEquals(Roots.Reader, Reader) || !ReferenceEquals(Roots.Withdrawal, this) || !_frozen
                || Unknown || Reader.Unknown || Roots.Retired || Committed || _committing && !allowCommitting)
                throw new InvalidOperationException("The source withdrawal is foreign, ended or permanently unknown.");
        }
        internal void VerifyForCut(object owner)
        {
            VerifyIdentity(allowCommitting: true);
            if (!ReferenceEquals(owner, Owner) || Roots.EnrollmentOpen || Roots.LastReadSequence != JournalCutoff
                || Reader._referenceRegistration is { } registration && registration.Generation != ReferenceReaderGeneration)
                throw new InvalidOperationException("The native cut belongs to another source owner, registration or frozen journal interval.");
        }

        internal void CommitRelease(NativeTweenRetirementContext.Cut cut)
        {
            ArgumentNullException.ThrowIfNull(cut); VerifyCurrent(); cut.VerifySourceWithdrawal(this);
            if (!cut.Committed || cut.Production != Production)
                throw new InvalidOperationException("Source root release requires this cut's exact complete native retirement commit.");
            void VerifyRelease() { cut.VerifySourceWithdrawal(this); if (Reader._references is { } references) cut.VerifyReferenceRoots(references); }
            VerifyRelease();
            if (!CommitCore(() => Reader._references?.ReclaimCompletedScopes(cut) ?? true,
                () => Reader._references?.ReclaimUnborrowedRootsAfterWithdrawal(cut), VerifyRelease))
                throw new InvalidOperationException("The native release cut still has active source reference borrowers.");
        }

        internal bool CommitReleaseForTests()
        {
            VerifyCurrent();
            if (Production) throw new InvalidOperationException("A controlled source withdrawal cannot release production roots without the exact native cut.");
            return CommitCore(() => Reader._references?.ReclaimCompletedScopesForTests() ?? true,
                () => Reader._references?.ReclaimUnborrowedRootsAfterWithdrawalForTests(), null);
        }
        private bool CommitCore(Func<bool> reclaimNativeEndedScopes, Action reclaimUnborrowedRoots, Action? verifyRelease)
        {
            VerifyCurrent();
            if (Roots.LastReadSequence != JournalCutoff)
                throw new InvalidOperationException("The source withdrawal has not consumed its complete frozen journal interval.");
            _committing = true;
            try
            {
                if (!reclaimNativeEndedScopes()) return false;
                Roots.ReleaseEntries(_issuer, verifyRelease);
                if (Reader._referenceRegistration is { } registration) registration.RemoveSet(Roots);
                reclaimUnborrowedRoots();
                Roots.FinishWithdrawal(_issuer); Reader._sets.Remove(Roots);
                if (Reader._sets.Count == 0) lock (ProcessOwners) ProcessOwners.Remove(Reader);
                Committed = true;
                return true;
            }
            catch (Exception error) { Fail(error, _issuer); Reader.Fault(); throw; }
            finally { _committing = false; }
        }
        internal void Fail(Exception error, object issuer)
        { if (!ReferenceEquals(issuer, _issuer)) throw new InvalidOperationException("Foreign source withdrawal failure issuer."); Unknown = true; Failure ??= error; }
    }
}

internal sealed partial class NativeTweenSourceRootSet
{
    internal NativeTweenSource.Withdrawal? Withdrawal { get; private set; }
    internal bool Retired { get; private set; }
    internal bool Releasing { get; private set; }
    internal bool EnrollmentOpen => Withdrawal is null && !Retired;
    internal void RequireEnrollmentOpen()
    { if (!EnrollmentOpen) throw new InvalidOperationException("The source root set permanently closed its borrowing enrollment."); }
    internal void BeginWithdrawal(NativeTweenSource.Withdrawal transaction, object issuer)
    {
        Verify(issuer); RequireEnrollmentOpen();
        if (!ReferenceEquals(transaction.Roots, this)) throw new InvalidOperationException("Foreign source withdrawal root set.");
        Withdrawal = transaction;
    }
    internal void ReleaseEntries(object issuer, Action? verifyRelease)
    {
        Verify(issuer);
        if (Withdrawal is null || Retired || Releasing) throw new InvalidOperationException("Source roots have no exact unused withdrawal transaction.");
        Releasing = true;
        // Invalidate every membership before any external Free callback. Own
        // handles are freed once; operation handles belong to the backend.
        foreach (var entry in _entries.Values) entry.OperationAnchor?.Withdraw(issuer);
        foreach (var entry in _entries.Values)
        {
            verifyRelease?.Invoke();
            if (entry.Retention == NativeTweenSourceRootRetention.ReferenceHubBorrow)
            {
                (entry.ReferenceBorrow ?? throw new InvalidOperationException("The hub root has no exact reader borrowing lease.")).Withdraw(this, issuer);
                entry.BorrowWithdrawn = true;
            }
            else if (entry.Retention == NativeTweenSourceRootRetention.OperationBorrow) entry.BorrowWithdrawn = true;
            else
            {
                if (entry.FreeAttempted || entry.Root is null) throw new InvalidOperationException("The initial source root has no unused independent handle.");
                entry.FreeAttempted = true; var root = entry.Root; entry.Root = null; entry.ReleaseHandle = root;
                try { root.Dispose(); entry.Freed = true; entry.ReleaseHandle = null; }
                catch (Exception error) { entry.ReleaseFailure = error; throw; }
            }
            entry.Root = null; entry.ReferenceBorrow = null; entry.ReferenceReceipt = null; entry.ReferenceCapture = null; entry.OperationAnchor = null;
        }
    }
    internal void FinishWithdrawal(object issuer) { Verify(issuer); Ready = false; Retired = true; Releasing = false; }
}
