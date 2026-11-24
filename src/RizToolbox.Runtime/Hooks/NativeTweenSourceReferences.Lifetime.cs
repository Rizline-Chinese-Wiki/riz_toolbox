namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeTweenSourceReferences
{
    private readonly List<ReaderRegistration> _readerRegistrations = new();
    private readonly List<ControlledConsumerRegistration> _controlledConsumers = new();
    private readonly List<Scope> _completedScopes = new();
    private readonly List<RootRecord> _releasedRootRecords = new();
    private ulong _rootGeneration, _readerGeneration, _highestScopeIdentity;
    private bool _releasingRoots;
    internal ulong ObserverEpoch { get { VerifyAccess(); return _observerEpoch; } }
    internal ulong LastDeliverySequence { get { VerifyAccess(); return _lastDelivery; } }
    internal int PendingNativeScopeRecords { get { VerifyAccess(); return _completedScopes.Count(value => !value.NativeEnded); } }
    internal int FreedRootRecords { get { VerifyAccess(); return _releasedRootRecords.Count(value => value.Freed); } }
    internal int FreeAttemptRecords { get { VerifyAccess(); return _releasedRootRecords.Count; } }

    internal sealed class ReaderRegistration
    {
        private readonly NativeTweenSourceReferences _owner;
        private readonly object _issuer;
        internal NativeTweenSource Reader { get; }
        internal ulong Generation { get; }
        private readonly HashSet<NativeTweenSourceRootSet> _sets = new();
        internal bool Withdrawn { get; private set; }
        internal bool HasConsumers => !Withdrawn && _sets.Any(set => set.EnrollmentOpen);
        internal IReadOnlyList<NativeTweenSourceRootSet> SnapshotRootSets()
            => Withdrawn ? Array.Empty<NativeTweenSourceRootSet>() : _sets.Where(set => set.EnrollmentOpen).ToArray();
        internal ReaderRegistration(NativeTweenSourceReferences owner, NativeTweenSource reader, ulong generation, object issuer)
        {
            if (!ReferenceEquals(issuer, owner._issuer)) throw new InvalidOperationException("Foreign source-reader registration issuer.");
            (_owner, Reader, Generation, _issuer) = (owner, reader, generation, issuer);
        }
        internal void AddSet(NativeTweenSourceRootSet roots)
        {
            _owner.VerifyAccess();
            if (Withdrawn || !ReferenceEquals(roots.Reader, Reader) || !_sets.Add(roots))
                throw new InvalidOperationException("A withdrawn or foreign source reader cannot enroll a root set.");
        }
        internal void RequireBorrow(NativeTweenSourceRootSet roots)
        {
            _owner.VerifyAccess();
            if (!ReferenceEquals(_issuer, _owner._issuer) || !_owner._readerRegistrations.Contains(this)
                || Withdrawn || !_sets.Contains(roots) || !roots.EnrollmentOpen)
                throw new InvalidOperationException("The exact source-reader generation has closed its root borrowing enrollment.");
        }
        internal void RemoveSet(NativeTweenSourceRootSet roots)
        {
            _owner.VerifyAccess();
            if (!_sets.Remove(roots)) throw new InvalidOperationException("The source-reader root-set borrower was already withdrawn.");
            if (_sets.Count == 0) { Withdrawn = true; _owner._readerRegistrations.Remove(this); }
        }
    }

    // A diagnostic consumer is a controlled fixture owner, never a production
    // substitute for a reader registration or a native retirement cut.
    internal sealed class ControlledConsumerRegistration
    {
        private readonly NativeTweenSourceReferences _owner;
        private readonly object _issuer;
        internal bool Withdrawn { get; private set; }
        internal ControlledConsumerRegistration(NativeTweenSourceReferences owner, object issuer)
        {
            if (owner.Production || !ReferenceEquals(issuer, owner._issuer)) throw new InvalidOperationException("Foreign controlled consumer issuer.");
            (_owner, _issuer) = (owner, issuer);
        }
        internal void Withdraw()
        {
            _owner.VerifyAccess();
            if (!ReferenceEquals(_issuer, _owner._issuer) || Withdrawn)
                throw new InvalidOperationException("The controlled source consumer was already withdrawn.");
            Withdrawn = true; _owner._controlledConsumers.Remove(this);
        }
    }

    internal ControlledConsumerRegistration RegisterControlledConsumerForTests()
    {
        VerifyAccess(); if (Production || Unknown) throw new InvalidOperationException("Production source references cannot enroll a controlled consumer.");
        var registration = new ControlledConsumerRegistration(this, _issuer); _controlledConsumers.Add(registration); return registration;
    }

    internal ReaderRegistration RegisterReader(NativeTweenSource reader, object readerIssuer)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(reader);
        if (Unknown || !reader.IsIssuer(readerIssuer) || !reader.IsReferenceObserver(this)
            || reader.Production != Production || _readerRegistrations.Any(row => ReferenceEquals(row.Reader, reader)))
            throw new InvalidOperationException("The source reader has no unused exact observer registration generation.");
        var registration = new ReaderRegistration(this, reader, checked(++_readerGeneration), _issuer);
        _readerRegistrations.Add(registration); return registration;
    }

    private IReadOnlyList<object> SnapshotConsumers()
        => _readerRegistrations.SelectMany(registration => registration.SnapshotRootSets()
                .Select(roots => (object)new ScopeReaderMembership(registration, roots)))
            .Concat(_controlledConsumers.Where(row => !row.Withdrawn)).ToArray();

    internal sealed record ScopeReaderMembership(ReaderRegistration Registration, NativeTweenSourceRootSet Roots);

    private NativeRuntime.RootHandle ReadCaptureRoot(RootedReference capture)
    {
        var record = capture.Record;
        if (record is null || record.FreeAttempted || record.Freed || record.Root is null
            || !_roots.TryGetValue(record.Identity, out var actual) || !ReferenceEquals(actual, record))
            throw new TargetExpiredException("The original synchronous source-root generation is no longer retained.");
        return record.Root;
    }

    internal sealed class RootBorrow
    {
        private readonly NativeTweenSourceReferences _owner;
        private readonly object _issuer;
        private readonly RootRecord _record;
        private readonly ReaderRegistration _registration;
        private readonly NativeTweenSourceRootSet _roots;
        internal ulong Generation => _record.Generation;
        internal bool Withdrawn { get; private set; }
        internal RootBorrow(NativeTweenSourceReferences owner, RootRecord record, ReaderRegistration registration,
            NativeTweenSourceRootSet roots, object issuer)
        {
            if (!ReferenceEquals(issuer, owner._issuer)) throw new InvalidOperationException("Foreign source-root borrower issuer.");
            (_owner, _record, _registration, _roots, _issuer) = (owner, record, registration, roots, issuer);
        }
        internal void VerifyRetained(NativeRuntime.RootHandle root)
        {
            _owner.VerifyAccess();
            if (!ReferenceEquals(_issuer, _owner._issuer) || Withdrawn || _roots.Retired || _registration.Withdrawn
                || !_record.Borrowers.Contains(this) || _record.FreeAttempted || !ReferenceEquals(_record.Root, root)
                || !_owner._roots.TryGetValue(_record.Identity, out var actual) || !ReferenceEquals(actual, _record))
                throw new TargetExpiredException("The exact source-reader/root generation borrowing interval has ended.");
        }
        internal void Withdraw(NativeTweenSourceRootSet roots, object readerIssuer)
        {
            _owner.VerifyAccess();
            if (!ReferenceEquals(roots, _roots) || !roots.Reader.IsIssuer(readerIssuer) || Withdrawn || !_record.Borrowers.Remove(this))
                throw new InvalidOperationException("A foreign or repeated source-root borrow withdrawal was rejected.");
            Withdrawn = true;
        }
    }

    internal RootBorrow BorrowRoot(Receipt receipt, RootedReference capture, ReaderRegistration registration,
        NativeTweenSourceRootSet roots, object readerIssuer)
    {
        VerifyAccess(); receipt.VerifyCurrent(this); registration.RequireBorrow(roots);
        if (Unknown || !roots.Reader.IsIssuer(readerIssuer) || !ReferenceEquals(registration.Reader, roots.Reader)
            || !receipt.IncludesConsumer(registration, roots) || !receipt.References.Contains(capture) || capture.Record is not { } record)
            throw new InvalidOperationException("Only this scope's frozen source-reader generation may borrow its direct capture.");
        _ = ReadCaptureRoot(capture);
        var borrow = new RootBorrow(this, record, registration, roots, _issuer); record.Borrowers.Add(borrow); return borrow;
    }

    private bool ReclaimCompletedScopesCore(Action? verifyRelease = null)
    {
        VerifyAccess();
        if (Unknown || _releasingRoots)
            throw new InvalidOperationException("Source-reference scope reclamation has active or unknown managed borrowers.");
        if (_current is not null || _scopes.Count != 0) return false;
        // The caller must already hold the common native cut. This exact info
        // check is an additional consistency check, never its replacement.
        var info = NewInformation();
        if (_native.GetInfo(ref info) != 0 || !InformationShape(info, _sourceEpoch) || info.ObserverEpoch != _observerEpoch
            || info.Unknown != 0)
            throw new UnsupportedSignatureException("The exact native reference scope observation became unknown.");
        if (info.ActiveScopes != 0 || info.RetainedScopes != 0 || info.CallbackInFlight != 0) return false;
        foreach (var scope in _completedScopes.Where(value => !value.NativeEnded))
        {
            if (!scope.Completed || scope.Failed) throw new UnsupportedSignatureException("A source scope has no successful managed completion.");
            scope.NativeEnded = true;
            foreach (var record in scope.Roots) record.Scopes.Remove(scope);
            scope.Roots.Clear(); scope.References.Clear(); scope.Consumers = Array.Empty<object>();
        }
        _completedScopes.RemoveAll(value => value.NativeEnded);
        ReclaimUnheldRoots(verifyRelease);
        return true;
    }

    private void ReclaimUnheldRoots(Action? verifyRelease = null)
    {
        if (Unknown) throw new InvalidOperationException("Unknown source root release is permanently retained.");
        _releasingRoots = true;
        try
        {
            foreach (var record in _roots.Values.Where(row => row.Scopes.Count == 0 && row.Borrowers.Count == 0).ToArray())
            {
                // Free may synchronously enter a new source callback that borrows
                // another row from this precomputed list. Recheck its real leases
                // immediately before releasing that handle.
                if (record.Scopes.Count != 0 || record.Borrowers.Count != 0) continue;
                verifyRelease?.Invoke();
                if (record.FreeAttempted || record.Failure is not null || record.Root is null)
                    throw new UnsupportedSignatureException("A source-root free has no known unused exact handle generation.");
                record.FreeAttempted = true; _releasedRootRecords.Add(record);
                var root = record.Root; record.Root = null;
                // Remove before external Free can throw; neither a retry nor a
                // later raw address may resurrect this uncertain generation.
                _roots.Remove(record.Identity);
                try { root.Dispose(); record.Freed = true; }
                catch (Exception error) { record.Failure = error; Fault(error); throw; }
            }
        }
        finally { _releasingRoots = false; }
    }

    internal bool ReclaimCompletedScopesForTests()
    {
        VerifyAccess(); if (Production) throw new InvalidOperationException("A controlled scope drain cannot release production source roots.");
        try { return ReclaimCompletedScopesCore(); }
        catch (Exception error) { Fault(error); throw; }
    }
    internal void ReclaimUnborrowedRootsAfterWithdrawalForTests()
    {
        VerifyAccess();
        if (Production) throw new InvalidOperationException("Production source roots require the real native release cut.");
        ReclaimUnheldRoots();
    }
    internal void VerifyRetirementCut(NativeTweenRetirementContext.Cut cut)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(cut); cut.VerifyCurrent();
        if (Unknown || cut.Production != Production || cut.SourceEpoch != _sourceEpoch || cut.ReferenceObserverEpoch != _observerEpoch
            || cut.ReferenceDeliveryCutoff > _lastDelivery || _current is not null || _scopes.Count != 0)
            throw new InvalidOperationException("The source-reference local borrowers do not belong to this exact native drained cut.");
    }
    internal bool ReclaimCompletedScopes(NativeTweenRetirementContext.Cut cut)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(cut); cut.VerifyReferenceRoots(this);
        if (!cut.Committed) throw new InvalidOperationException("Source roots cannot free before the native owner set commits retirement.");
        try { return ReclaimCompletedScopesCore(() => cut.VerifyReferenceRoots(this)); }
        catch (Exception error) { Fault(error); throw; }
    }
    internal void ReclaimUnborrowedRootsAfterWithdrawal(NativeTweenRetirementContext.Cut cut)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(cut); cut.VerifyReferenceRoots(this);
        if (!cut.Committed) throw new InvalidOperationException("Source roots cannot free before this exact native retirement commit.");
        try
        {
            // A real GC free can run nested foreign sources. Their immutable
            // scope leases must reach native End before any last-holder free.
            if (!ReclaimCompletedScopesCore(() => cut.VerifyReferenceRoots(this)))
                throw new InvalidOperationException("A nested source scope still retains its actual native borrower.");
        }
        catch (Exception error) { Fault(error); throw; }
    }
}
