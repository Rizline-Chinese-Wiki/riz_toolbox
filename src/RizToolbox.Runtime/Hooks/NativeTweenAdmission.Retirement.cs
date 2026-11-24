using System.Runtime.CompilerServices;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeTweenAdmissionRetirementPhase
{ Prepared, NativeCommitted, RootsReleased, Unknown }

internal sealed partial class NativeTweenAdmission
{
    private readonly object _retirementIssuer = new();
    // Weak identity preserves the permanently closed enrollment without keeping
    // every completed run alive through the process policy's static registry.
    private readonly ConditionalWeakTable<INativeTweenAdmissionOwner, object> _closedOwners = new();
    private readonly Dictionary<INativeTweenAdmissionOwner, RetirementBatch> _retirementBatches = new(ReferenceEqualityComparer.Instance);

    private void RequireOwnerEnrollment(INativeTweenAdmissionOwner owner)
    {
        if (_closedOwners.TryGetValue(owner, out _))
            throw new InvalidOperationException("This Tween owner's admission enrollment was permanently withdrawn.");
    }

    /// <summary>Freezes all this exact owner's retained claims. It grants no
    /// work/effect completion, native drain or root-free authority. The caller
    /// must later consume this same batch through the real synchronous Cut.</summary>
    internal RetirementBatch PrepareRetirement(INativeTweenAdmissionOwner owner)
    {
        _verifyAccess(); ArgumentNullException.ThrowIfNull(owner); RequireKnown(); owner.VerifyTweenAdmissionOwner();
        if (_retirementBatches.TryGetValue(owner, out var existing)) { existing.VerifyPrepared(); return existing; }
        RequireOwnerEnrollment(owner);
        _closedOwners.Add(owner, _retirementIssuer);
        var batch = new RetirementBatch(this, owner, _leases.Where(lease => ReferenceEquals(lease.Owner, owner)).ToArray(), _retirementIssuer);
        _retirementBatches.Add(owner, batch);
        try { batch.VerifyPrepared(); return batch; }
        catch (Exception error) { batch.Fail(error); throw; }
    }

    internal sealed class RetirementBatch
    {
        private readonly object _issuer;
        private readonly NativeTweenAdmissionLease[] _leases;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly NativeTweenRetirementClaim[] _claims;
        private NativeTweenAdmissionReceipt[]? _nativeReceipts;
        private bool _committing, _releasing;
        internal NativeTweenAdmission Authority { get; }
        internal INativeTweenAdmissionOwner Owner { get; }
        internal bool Production => Authority.Production;
        internal ulong ConfigurationEpoch { get; }
        internal IReadOnlyList<NativeTweenRetirementClaim> Claims { get; }
        internal NativeTweenAdmissionRetirementPhase Phase { get; private set; }
        internal string? Failure { get; private set; }
        internal bool Unknown => Phase == NativeTweenAdmissionRetirementPhase.Unknown || Authority.Unknown;
        internal IReadOnlyList<NativeTweenAdmissionReceipt> NativeReceipts
            => Array.AsReadOnly(_nativeReceipts?.ToArray() ?? Array.Empty<NativeTweenAdmissionReceipt>());

        internal RetirementBatch(NativeTweenAdmission authority, INativeTweenAdmissionOwner owner,
            NativeTweenAdmissionLease[] leases, object issuer)
        {
            if (!ReferenceEquals(issuer, authority._retirementIssuer)) throw new InvalidOperationException("Foreign admission retirement issuer.");
            (Authority, Owner, _leases, _issuer, ConfigurationEpoch) = (authority, owner, leases, issuer, authority._configurationEpoch);
            _claims = leases.Select(lease => new NativeTweenRetirementClaim {
                Slot = lease.Slot, Epoch = lease.Epoch, Object = lease.Object, ActualClass = authority._configuration.ExactTweenClass }).ToArray();
            Claims = Array.AsReadOnly(_claims);
        }

        private void VerifyIdentity(bool requireRegistered)
        {
            Authority._verifyAccess();
            if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_issuer, Authority._retirementIssuer)
                || !Authority._closedOwners.TryGetValue(Owner, out var closed) || !ReferenceEquals(closed, _issuer)
                || ConfigurationEpoch == 0 || ConfigurationEpoch != Authority._configurationEpoch || Unknown
                || requireRegistered && (!Authority._retirementBatches.TryGetValue(Owner, out var retained) || !ReferenceEquals(retained, this)))
                throw new InvalidOperationException("This admission retirement interval is foreign, ended or permanently unknown.");
        }

        internal void VerifyPrepared()
        {
            VerifyIdentity(requireRegistered: true);
            if (Phase != NativeTweenAdmissionRetirementPhase.Prepared || _releasing)
                throw new InvalidOperationException("The admission retirement batch is no longer prepared.");
            Owner.VerifyTweenAdmissionOwner(); Authority.RequireKnown(); Authority.VerifyInformation();
            var current = Authority._leases.Where(lease => ReferenceEquals(lease.Owner, Owner)).ToArray();
            if (current.Length != _leases.Length || current.Where((lease, index) => !ReferenceEquals(lease, _leases[index])).Any())
                throw new InvalidOperationException("The complete retained admission lease set changed before retirement.");
            var identities = new HashSet<(uint, ulong)>();
            for (var index = 0; index < _leases.Length; ++index)
            {
                var lease = _leases[index]; var claim = _claims[index];
                Authority.VerifyLease(lease);
                if (!lease.Claimed || lease.ClaimPending || lease.NativeRetired || !identities.Add((lease.Slot, lease.Epoch))
                    || claim.Reserved != 0 || claim.Slot != lease.Slot || claim.Epoch != lease.Epoch || claim.Object != lease.Object
                    || claim.ActualClass != Authority._configuration.ExactTweenClass)
                    throw new InvalidOperationException("A frozen admission claim is missing, duplicated or differs from its actual lease.");
                var observed = lease.Read();
                if (!observed.IsSealed || observed.CachedNative != 0 || observed.InFlight != 0)
                    throw new InvalidOperationException("A Tween retirement claim is still alive, active or has no actual sealed epoch.");
            }
            Owner.VerifyTweenAdmissionOwner(); Authority.VerifyInformation();
        }

        internal void CommitNative(NativeTweenRetirementContext.Cut cut)
        {
            ArgumentNullException.ThrowIfNull(cut);
            if (_committing) throw new InvalidOperationException("Admission native retirement cannot reenter its own commit.");
            VerifyPrepared(); cut.VerifyAdmissionBatch(this);
            _committing = true;
            try
            {
                // UnregisterBatch validates the entire native set before any
                // owner address is cleared. Retain all returned tuples before
                // parsing so an ambiguous result can never be retried/freed.
                _nativeReceipts = cut.UnregisterBatch(this).ToArray();
                if (_nativeReceipts.Length != _leases.Length)
                    throw new UnsupportedSignatureException("Native retirement did not return the complete exact receipt set.");
                for (var index = 0; index < _leases.Length; ++index)
                {
                    var lease = _leases[index]; var receipt = _nativeReceipts[index];
                    if (!RetiredReceiptMatches(receipt, _claims[index], lease))
                        throw new UnsupportedSignatureException("Native retirement returned a foreign, live or regressed Retired receipt.");
                }
                for (var index = 0; index < _leases.Length; ++index)
                    _leases[index].RecordNativeRetired(_nativeReceipts[index], Authority._leaseIssuer);
                Phase = NativeTweenAdmissionRetirementPhase.NativeCommitted;
                cut.VerifyAdmissionBatch(this); // No Read of now-retired slots.
            }
            catch (Exception error) { Fail(error); throw; }
            finally { _committing = false; }
        }

        /// <summary>Only admission-owned handles are freed here. A shared
        /// backend handle loses this borrow; its sole holder frees it later,
        /// after source and reference borrowers have also withdrawn.</summary>
        internal void ReleaseRoots(NativeTweenRetirementContext.Cut cut)
        {
            ArgumentNullException.ThrowIfNull(cut); VerifyIdentity(requireRegistered: true);
            if (Phase != NativeTweenAdmissionRetirementPhase.NativeCommitted || _committing || _releasing)
                throw new InvalidOperationException("Root release requires this exact successfully committed native batch.");
            cut.VerifyAdmissionBatch(this); _releasing = true;
            Exception? first = null;
            try
            {
                foreach (var lease in _leases)
                {
                    try { cut.VerifyAdmissionBatch(this); lease.ReleaseRetiredRoot(Authority._leaseIssuer); }
                    catch (Exception error) { first ??= error; }
                }
                if (first is not null) throw new InvalidOperationException("An admission root release has an unknown result; attempts will never repeat.", first);
                cut.VerifyAdmissionBatch(this);
                foreach (var lease in _leases) Authority._leases.Remove(lease);
                Phase = NativeTweenAdmissionRetirementPhase.RootsReleased;
                Authority._retirementBatches.Remove(Owner);
            }
            catch (Exception error) { Fail(error); throw; }
            finally { _releasing = false; }
        }

        internal void VerifyNativeCommittedIdentity()
        {
            VerifyIdentity(requireRegistered: Phase != NativeTweenAdmissionRetirementPhase.RootsReleased);
            if (Phase is not (NativeTweenAdmissionRetirementPhase.NativeCommitted or NativeTweenAdmissionRetirementPhase.RootsReleased))
                throw new InvalidOperationException("No complete native retirement receipt belongs to this batch.");
        }

        internal void Fail(Exception error)
        {
            Failure ??= error.Message; Phase = NativeTweenAdmissionRetirementPhase.Unknown;
            foreach (var lease in _leases) lease.Fault();
            Authority.Fault();
        }
    }

    private static bool RetiredReceiptMatches(NativeTweenAdmissionReceipt receipt, NativeTweenRetirementClaim claim,
        NativeTweenAdmissionLease lease)
        => receipt.Version == 1 && receipt.Size == 96 && receipt.Phase == NativeTweenAdmissionPhase.Retired
            && receipt.Slot == claim.Slot && receipt.Epoch == claim.Epoch && receipt.Object == claim.Object
            && receipt.ActualClass == claim.ActualClass && receipt.CachedNative == IntPtr.Zero
            && receipt.Unknown == 0 && receipt.Reserved == 0 && receipt.IssuersInFlight == 0 && receipt.ActiveEntries == 0
            && receipt.AdmittedIssuers >= lease.AdmittedIssuers && receipt.DeniedIssuers >= lease.DeniedIssuers
            && receipt.DeniedDead >= lease.DeniedDead;
}

internal sealed partial class NativeTweenAdmissionLease
{
    internal bool NativeRetired { get; private set; }
    internal bool OwnRootFreeAttempted { get; private set; }
    internal bool OwnRootFreeSucceeded { get; private set; }
    internal bool SharedBorrowReleased { get; private set; }
    internal NativeTweenAdmissionReceipt NativeRetirementReceipt { get; private set; }

    internal void RecordNativeRetired(NativeTweenAdmissionReceipt receipt, object issuer)
    {
        VerifyIssuer(issuer);
        if (NativeRetired || receipt.Phase != NativeTweenAdmissionPhase.Retired)
            throw new InvalidOperationException("This admission claim cannot consume native retirement twice.");
        NativeRetirementReceipt = receipt; Phase = NativeTweenAdmissionPhase.Retired; NativeRetired = true;
    }

    internal void ReleaseRetiredRoot(object issuer)
    {
        VerifyIssuer(issuer);
        if (!NativeRetired || Unknown || Root is null || OwnRootFreeAttempted || SharedBorrowReleased)
            throw new InvalidOperationException("This admission root is not eligible for one exact retired release.");
        if (RootRetention == NativeTweenAdmissionRootRetention.SharedRetainedProcessRoot)
        { SharedBorrowReleased = true; return; }
        OwnRootFreeAttempted = true; // Before the real free: a throw is never retried.
        try { Root.Dispose(); OwnRootFreeSucceeded = true; }
        catch { Fault(); throw; }
    }
}
