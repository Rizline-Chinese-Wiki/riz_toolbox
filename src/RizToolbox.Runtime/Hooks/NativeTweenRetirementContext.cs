using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenRetirementClaim
{
    internal uint Slot, Reserved;
    internal ulong Epoch;
    internal IntPtr Object, ActualClass;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenRetirementRequest
{
    internal uint Version, Size;
    internal ulong ConfigurationEpoch, SourceEpoch, JournalCutoff;
    internal ulong ReferenceObserverEpoch, ReferenceDeliveryCutoff, ReferenceReaderGeneration;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenRetirementCutReceipt
{
    internal uint Version, Size, ThreadId, Phase;
    internal ulong Token, Generation, ConfigurationEpoch, SourceEpoch, JournalCutoff;
    internal ulong ReferenceObserverEpoch, ReferenceDeliveryCutoff, ReferenceReaderGeneration;
    internal uint ClaimCount, Reserved;
}
internal interface INativeTweenRetirementCutOwner : INativeTweenAdmissionOwner, INativeTweenSourceOwner
{
    void VerifyTweenRetirementCut(NativeTweenRetirementContext context);
    NativeTweenAdmission.RetirementBatch BorrowTweenAdmissionRetirementBatch(NativeTweenRetirementContext context);
}
internal interface INativeTweenRetirementContextTransport
{
    uint Acquire(ref NativeTweenRetirementRequest request, NativeTweenRetirementClaim[] claims, ref NativeTweenRetirementCutReceipt cut);
    uint Verify(ref NativeTweenRetirementCutReceipt cut);
    uint UnregisterBatch(ref NativeTweenRetirementCutReceipt cut, NativeTweenRetirementClaim[] claims, NativeTweenAdmissionReceipt[] receipts);
    uint Release(ref NativeTweenRetirementCutReceipt cut);
    uint VerifyReferenceRoots(ref NativeTweenRetirementCutReceipt cut);
}

/// <summary>A synchronous loan over an actually drained native generation.
/// The exact run must permanently withdraw its borrowers first. Later foreign
/// originals may execute in a new generation and cannot borrow the closed run.
/// No lock covers managed callbacks, original calls or GC handle frees.</summary>
internal sealed class NativeTweenRetirementContext
{
    private readonly INativeTweenRetirementContextTransport _native;
    private readonly INativeTweenRetirementCutOwner _owner;
    private readonly Action _verifyAccess, _verifyProducer;
    private readonly object _issuer = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly uint _nativeThread = NativeTweenSourceReferences.CurrentThreadId();
    private Cut? _current;
    private bool _busy, _unknown;
    internal bool Production { get; }
    internal bool Unknown => _unknown;

    private NativeTweenRetirementContext(INativeTweenRetirementContextTransport native, INativeTweenRetirementCutOwner owner,
        Action verifyAccess, Action verifyProducer, bool production)
        => (_native, _owner, _verifyAccess, _verifyProducer, Production) = (native, owner, verifyAccess, verifyProducer, production);

    internal static NativeTweenRetirementContext BindProduction(INativeTweenRetirementCutOwner owner, Action verifyAccess)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(verifyAccess); verifyAccess();
        var ready = NativeFirstInstall.RequireReady(); ready.RequireTweenSourceReady().VerifyReady();
        return new(new LoadedTransport(ready), owner, verifyAccess, ready.VerifyReady, true);
    }
    internal static NativeTweenRetirementContext CreateControlledForTests(INativeTweenRetirementContextTransport native,
        INativeTweenRetirementCutOwner owner, Action verifyAccess)
    {
        ArgumentNullException.ThrowIfNull(native); ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(verifyAccess); verifyAccess();
        return new(native, owner, verifyAccess, () => { }, false);
    }
    private void VerifyThread()
    {
        _verifyAccess();
        if (_thread != Environment.CurrentManagedThreadId || _nativeThread != NativeTweenSourceReferences.CurrentThreadId())
            throw new InvalidOperationException("A Tween retirement cut belongs to its actual lifecycle OS thread.");
    }
    internal bool WithCut(NativeTweenSource.Withdrawal withdrawal, Action<Cut> use)
    {
        VerifyThread(); ArgumentNullException.ThrowIfNull(withdrawal); ArgumentNullException.ThrowIfNull(use);
        if (_busy || _unknown) throw new InvalidOperationException("Tween retirement cannot reenter or retry an Unknown cut authority.");
        _owner.VerifyTweenRetirementCut(this); withdrawal.VerifyForCut(_owner);
        var batch = _owner.BorrowTweenAdmissionRetirementBatch(this);
        ArgumentNullException.ThrowIfNull(batch); batch.VerifyPrepared();
        if (!ReferenceEquals(batch.Owner, _owner) || batch.Production != Production || withdrawal.Production != Production
            || !ReferenceEquals(withdrawal.Owner, _owner))
            throw new InvalidOperationException("A retirement cut cannot cross its production kind, exact owner or private complete admission batch.");
        var claims = batch.Claims.ToArray();
        if (claims.Length > 128 || claims.Any(claim => claim.Reserved != 0 || claim.Epoch == 0
            || claim.Slot >= 128 || claim.Object == IntPtr.Zero || claim.ActualClass == IntPtr.Zero)
            || claims.Select(claim => claim.Slot).Distinct().Count() != claims.Length
            || claims.Select(claim => claim.Object).Distinct().Count() != claims.Length)
            throw new InvalidOperationException("The actual retirement batch has no complete unique native claim set.");
        ValidateLayout(); _verifyProducer();
        var request = new NativeTweenRetirementRequest { Version = 1, Size = 56, ConfigurationEpoch = batch.ConfigurationEpoch,
            SourceEpoch = withdrawal.SourceEpoch, JournalCutoff = withdrawal.JournalCutoff,
            ReferenceObserverEpoch = withdrawal.ReferenceObserverEpoch, ReferenceDeliveryCutoff = withdrawal.ReferenceDeliveryCutoff,
            ReferenceReaderGeneration = withdrawal.ReferenceReaderGeneration };
        var receipt = new NativeTweenRetirementCutReceipt { Version = 1, Size = 88 };
        _busy = true; var acquired = false;
        try
        {
            var expectedRequest = request;
            var expectedClaims = claims.ToArray(); var emptyReceipt = receipt;
            var status = _native.Acquire(ref request, claims, ref receipt);
            if (status == 3)
            {
                if (!request.Equals(expectedRequest) || !claims.SequenceEqual(expectedClaims) || !receipt.Equals(emptyReceipt))
                    throw new UnsupportedSignatureException("A pending native retirement acquisition changed its no-mutation input or output.");
                return false;
            }
            if (status != 0 || !request.Equals(expectedRequest) || !ReceiptMatches(receipt, request, claims.Length, _nativeThread))
                throw new UnsupportedSignatureException("The native Tween retirement cut is foreign, stale, malformed or Unknown: " + status);
            acquired = true;
            var cut = new Cut(this, withdrawal, batch, claims, receipt, _issuer);
            _current = cut; cut.VerifyAdmissionBatch(batch); use(cut); cut.VerifyCurrent(); return true;
        }
        catch { _unknown = true; throw; }
        finally
        {
            _current?.End(_issuer); _current = null;
            try
            {
                if (acquired && _native.Release(ref receipt) != 0)
                { _unknown = true; throw new UnsupportedSignatureException("The actual Tween retirement cut release was not confirmed."); }
            }
            catch { _unknown = true; throw; }
            finally { _busy = false; }
        }
    }

    internal sealed class Cut
    {
        private readonly NativeTweenRetirementContext _context;
        private readonly NativeTweenSource.Withdrawal _withdrawal;
        private readonly NativeTweenAdmission.RetirementBatch _batch;
        private readonly NativeTweenRetirementClaim[] _claims;
        private readonly object _issuer;
        private NativeTweenRetirementCutReceipt _receipt;
        private bool _ended, _batchCommitted, _committing;
        internal bool Production => _context.Production;
        internal bool Committed => _batchCommitted;
        internal ulong SourceEpoch => _receipt.SourceEpoch;
        internal ulong JournalCutoff => _receipt.JournalCutoff;
        internal ulong ReferenceObserverEpoch => _receipt.ReferenceObserverEpoch;
        internal ulong ReferenceDeliveryCutoff => _receipt.ReferenceDeliveryCutoff;
        internal ulong ReferenceReaderGeneration => _receipt.ReferenceReaderGeneration;
        internal INativeTweenRetirementCutOwner Owner => _context._owner;
        internal Cut(NativeTweenRetirementContext context, NativeTweenSource.Withdrawal withdrawal,
            NativeTweenAdmission.RetirementBatch batch, NativeTweenRetirementClaim[] claims,
            NativeTweenRetirementCutReceipt receipt, object issuer)
        {
            if (!ReferenceEquals(issuer, context._issuer) || !context._busy || context._current is not null)
                throw new InvalidOperationException("Only the actual synchronous native cut may issue this loan.");
            (_context, _withdrawal, _batch, _claims, _receipt, _issuer) = (context, withdrawal, batch, claims, receipt, issuer);
        }
        internal void VerifyCurrent()
        {
            _context.VerifyThread();
            if (_ended || !ReferenceEquals(_issuer, _context._issuer) || !ReferenceEquals(_context._current, this)
                || !_context._busy || _context._unknown)
                throw new InvalidOperationException("The private Tween retirement cut is no longer synchronously borrowed.");
            _context._owner.VerifyTweenRetirementCut(_context);
            _context._verifyProducer();
            var before = _receipt;
            if (_context._native.Verify(ref _receipt) != 0 || !_receipt.Equals(before))
                throw new UnsupportedSignatureException("The actual Tween native cut lost its exact drained generation.");
        }
        internal void VerifySourceWithdrawal(NativeTweenSource.Withdrawal withdrawal)
        {
            VerifyCurrent();
            if (!ReferenceEquals(_withdrawal, withdrawal) || withdrawal.Production != Production || withdrawal.Unknown
                || withdrawal.SourceEpoch != SourceEpoch || withdrawal.JournalCutoff != JournalCutoff
                || withdrawal.ReferenceObserverEpoch != ReferenceObserverEpoch || withdrawal.ReferenceDeliveryCutoff != ReferenceDeliveryCutoff
                || withdrawal.ReferenceReaderGeneration != ReferenceReaderGeneration)
                throw new InvalidOperationException("The native cut belongs to a different exact withdrawn source generation.");
            if (!withdrawal.Committed) withdrawal.VerifyForCut(Owner);
        }
        internal void VerifyReferenceRoots(NativeTweenSourceReferences references)
        {
            VerifyCurrent(); ArgumentNullException.ThrowIfNull(references);
            if (!_withdrawal.Reader.IsReferenceObserver(references) || references.Production != Production
                || references.Unknown || references.ObserverEpoch != ReferenceObserverEpoch
                || references.LastDeliverySequence < ReferenceDeliveryCutoff)
                throw new InvalidOperationException("The native cut has a foreign source-reference observer or delivery generation.");
            var before = _receipt;
            if (_context._native.VerifyReferenceRoots(ref _receipt) != 0 || !_receipt.Equals(before))
                throw new UnsupportedSignatureException("The source-reference native scopes have not actually ended.");
            references.VerifyRetirementCut(this);
        }
        internal void VerifyAdmissionBatch(NativeTweenAdmission.RetirementBatch batch)
        {
            VerifyCurrent();
            if (!ReferenceEquals(_batch, batch) || !ReferenceEquals(batch.Owner, Owner) || batch.Production != Production
                || batch.ConfigurationEpoch != _receipt.ConfigurationEpoch || batch.Unknown
                || !batch.Claims.SequenceEqual(_claims))
                throw new InvalidOperationException("The cut does not contain this exact complete private admission batch.");
            if (_batchCommitted)
            {
                // CommitNative records its frozen result after UnregisterBatch
                // returns. Never dereference old owner objects or retired roots.
                if (batch.Phase != NativeTweenAdmissionRetirementPhase.Prepared) batch.VerifyNativeCommittedIdentity();
            }
            else batch.VerifyPrepared();
        }
        internal IReadOnlyList<NativeTweenAdmissionReceipt> UnregisterBatch(NativeTweenAdmission.RetirementBatch batch)
        {
            VerifyAdmissionBatch(batch);
            if (_batchCommitted || _committing) throw new InvalidOperationException("This native admission batch already consumed its one cut commit.");
            _committing = true;
            try
            {
                var receipts = _claims.Select(_ => new NativeTweenAdmissionReceipt { Version = 1, Size = 96 }).ToArray();
                var before = _receipt;
                var status = _context._native.UnregisterBatch(ref _receipt, _claims, receipts);
                if (status != 0 || !_receipt.Equals(before))
                    throw new UnsupportedSignatureException("The native all-or-nothing Tween retirement batch was not confirmed: " + status);
                _batchCommitted = true; VerifyCurrent(); return Array.AsReadOnly(receipts);
            }
            catch { _context._unknown = true; throw; }
            finally { _committing = false; }
        }
        internal void End(object issuer)
        { if (!ReferenceEquals(issuer, _issuer)) throw new InvalidOperationException("Foreign cut end issuer."); _ended = true; }
    }
    private static bool ReceiptMatches(NativeTweenRetirementCutReceipt cut, NativeTweenRetirementRequest request, int count, uint thread)
        => cut.Version == 1 && cut.Size == 88 && cut.ThreadId == thread && cut.Phase == 1 && cut.Token != 0 && cut.Generation != 0
            && cut.ConfigurationEpoch == request.ConfigurationEpoch && cut.SourceEpoch == request.SourceEpoch && cut.JournalCutoff == request.JournalCutoff
            && cut.ReferenceObserverEpoch == request.ReferenceObserverEpoch && cut.ReferenceDeliveryCutoff == request.ReferenceDeliveryCutoff
            && cut.ReferenceReaderGeneration == request.ReferenceReaderGeneration && cut.ClaimCount == count && cut.Reserved == 0;
    internal static void ValidateLayout()
    {
        if (IntPtr.Size != 8 || Marshal.SizeOf<NativeTweenRetirementClaim>() != 32 || Marshal.SizeOf<NativeTweenRetirementRequest>() != 56
            || Marshal.SizeOf<NativeTweenRetirementCutReceipt>() != 88
            || Marshal.OffsetOf<NativeTweenRetirementCutReceipt>(nameof(NativeTweenRetirementCutReceipt.Token)).ToInt32() != 16)
            throw new UnsupportedSignatureException("The native retirement contract is not its exact x64 V1 layout.");
    }
    private sealed class LoadedTransport : INativeTweenRetirementContextTransport
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AcquireNative(ref NativeTweenRetirementRequest request,
            uint count, [In] NativeTweenRetirementClaim[] claims, ref NativeTweenRetirementCutReceipt cut);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CutNative(ref NativeTweenRetirementCutReceipt cut);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BatchNative(ref NativeTweenRetirementCutReceipt cut,
            uint count, [In] NativeTweenRetirementClaim[] claims, [In, Out] NativeTweenAdmissionReceipt[] receipts);
        private readonly AcquireNative _acquire;
        private readonly CutNative _verify, _release, _references;
        private readonly BatchNative _batch;
        internal LoadedTransport(NativeFirstInstall ready)
        {
            ready.VerifyReady();
            T Export<T>(string name) where T : Delegate
            { var value = NativeLibrary.GetExport(ready.HelperModule, name); HookTargetValidation.VerifyExecutable(value, ready.HelperModule); return Marshal.GetDelegateForFunctionPointer<T>(value); }
            _acquire = Export<AcquireNative>("RizTweenRetirement_Acquire"); _verify = Export<CutNative>("RizTweenRetirement_Verify");
            _batch = Export<BatchNative>("RizTweenRetirement_UnregisterBatch"); _release = Export<CutNative>("RizTweenRetirement_Release");
            _references = Export<CutNative>("RizTweenRetirement_VerifyReferenceRoots");
        }
        public uint Acquire(ref NativeTweenRetirementRequest request, NativeTweenRetirementClaim[] claims, ref NativeTweenRetirementCutReceipt cut)
            => _acquire(ref request, checked((uint)claims.Length), claims, ref cut);
        public uint Verify(ref NativeTweenRetirementCutReceipt cut) => _verify(ref cut);
        public uint UnregisterBatch(ref NativeTweenRetirementCutReceipt cut, NativeTweenRetirementClaim[] claims, NativeTweenAdmissionReceipt[] receipts)
            => _batch(ref cut, checked((uint)claims.Length), claims, receipts);
        public uint Release(ref NativeTweenRetirementCutReceipt cut) => _release(ref cut);
        public uint VerifyReferenceRoots(ref NativeTweenRetirementCutReceipt cut) => _references(ref cut);
    }
}
