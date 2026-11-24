using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeTweenAdmissionPhase : uint { Empty, Open, Closing, Sealed, Unknown, Retired }
internal enum NativeTweenAdmissionRootRetention { IndependentClaimRoot, SharedRetainedProcessRoot }

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenAdmissionConfiguration
{
    internal uint Version, Size;
    internal IntPtr ExactTweenClass, CachedPointerField;
    internal uint CachedPointerOffset, Reserved;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenAdmissionReceipt
{
    internal uint Version, Size, Slot;
    internal NativeTweenAdmissionPhase Phase;
    internal ulong Epoch;
    internal IntPtr Object, ActualClass, CachedNative;
    internal ulong IssuersInFlight, ActiveEntries, AdmittedIssuers, DeniedIssuers, DeniedDead;
    internal uint Unknown, Reserved;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenAdmissionInformation
{
    internal uint Version, Size, Configured, Unknown;
    internal IntPtr ExactTweenClass, CachedPointerField;
    internal uint CachedPointerOffset, Capacity;
    internal ulong ConfigurationEpoch, Calls, OriginalCalls, SkippedDead, SkippedSealed, UnknownReads, Claims, ActiveOwners;
}

/// <summary>A real retained Adapter owner verifies its current thread and known
/// ownership. It supplies no boolean completion, death or native-seal receipt.</summary>
internal interface INativeTweenAdmissionOwner { void VerifyTweenAdmissionOwner(); }

/// <summary>Diagnostic copy only. Sealed closes Play/Add admission; it does not
/// assert queue exit, listener effects completion, Unity death or root retirement.</summary>
internal sealed record NativeTweenAdmissionSnapshot(bool Known, NativeTweenAdmissionPhase Phase, uint Slot, ulong Epoch,
    long Object, long ActualClass, long CachedNative, ulong IssuersInFlight, ulong ActiveEntries,
    ulong AdmittedIssuers, ulong DeniedIssuers, ulong DeniedDead)
{
    internal bool IsSealed => Known && Phase == NativeTweenAdmissionPhase.Sealed && IssuersInFlight == 0;
    internal ulong InFlight => ActiveEntries;
}

internal interface INativeTweenAdmissionTransport
{
    uint Configure(ref NativeTweenAdmissionConfiguration configuration);
    uint GetInfo(ref NativeTweenAdmissionInformation information);
    uint Claim(IntPtr actualTween, ref NativeTweenAdmissionReceipt receipt);
    uint Read(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt);
    uint Seal(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt);
    uint Unregister(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt);
}

/// <summary>Owns the real native configuration and records each independent or
/// shared process-retained claim root. Shared roots remain the backend's same
/// handle; admission owns no separate free operation for them. There
/// is intentionally no IDisposable or accessible Unregister path: no production
/// queue/death completion interval exists yet. Unknown outcomes retain every root.</summary>
internal sealed partial class NativeTweenAdmission
{
    private static readonly object ProcessGate = new();
    private static readonly HashSet<NativeTweenAdmission> ProcessOwners = new();
    private readonly INativeTweenAdmissionTransport _native;
    private readonly Action _verifyAccess, _verifyProducer;
    private readonly Func<IntPtr, NativeRuntime.RootHandle> _adopt;
    private readonly Func<NativeRuntime.RootHandle, IntPtr> _verifyReceiver;
    private readonly NativeTweenAdmissionConfiguration _configuration;
    private readonly List<NativeTweenAdmissionLease> _leases = new();
    private readonly object _leaseIssuer = new();
    private ulong _configurationEpoch;
    private int _unknown;
    private bool _configured;
    internal bool Production { get; }
    internal bool Unknown => Volatile.Read(ref _unknown) != 0;
    internal int RootRecords { get { _verifyAccess(); return _leases.Count(lease => lease.RootRecorded); } }
    internal int IndependentRootRecords { get { _verifyAccess(); return _leases.Count(lease => lease.RootRecorded && lease.RootRetention == NativeTweenAdmissionRootRetention.IndependentClaimRoot); } }
    internal int SharedRootRecords { get { _verifyAccess(); return _leases.Count(lease => lease.RootRecorded && lease.RootRetention == NativeTweenAdmissionRootRetention.SharedRetainedProcessRoot); } }

    private NativeTweenAdmission(INativeTweenAdmissionTransport native, NativeTweenAdmissionConfiguration configuration,
        Action verifyAccess, Action verifyProducer, Func<IntPtr, NativeRuntime.RootHandle> adopt,
        Func<NativeRuntime.RootHandle, IntPtr> verifyReceiver, bool production)
    {
        _native = native; _configuration = configuration; _verifyAccess = verifyAccess; _verifyProducer = verifyProducer;
        _adopt = adopt; _verifyReceiver = verifyReceiver; Production = production;
    }

    internal static NativeTweenAdmission BindProduction(NativeRuntime runtime, Action verifyAccess)
    {
        ArgumentNullException.ThrowIfNull(runtime); ArgumentNullException.ThrowIfNull(verifyAccess); verifyAccess();
        var ready = NativeFirstInstall.RequireReady(); ready.VerifyReady();
        if (!NativeOperationPublication.HasShape(ready.OperationPublication))
            throw new UnsupportedSignatureException("Tween admission requires the actual complete 26-operation 64-write producer.");
        var metadata = runtime.InspectTweenAdmissionMetadata(ready.Module);
        var configuration = new NativeTweenAdmissionConfiguration { Version = 1, Size = 32,
            ExactTweenClass = metadata.TweenClass, CachedPointerField = metadata.CachedPointerField.Field, CachedPointerOffset = 16 };
        var owner = new NativeTweenAdmission(new LoadedTransport(ready), configuration, verifyAccess, ready.VerifyReady,
            runtime.Root, root => runtime.ValidateRootedTweenAdmissionClaim(root, metadata, ready.Module), true);
        lock (ProcessGate) ProcessOwners.Add(owner); // Configure may publish permanent policy before returning.
        owner.Configure(); return owner;
    }

    /// <summary>A controlled semantic seam only. It never produces a production
    /// authority or loads/configures a native helper, and cannot be used by the
    /// production Adapter backend in place of BindTweenAdmission.</summary>
    internal static NativeTweenAdmission CreateControlledForTests(INativeTweenAdmissionTransport native,
        NativeTweenAdmissionConfiguration configuration, Action verifyAccess,
        Func<IntPtr, NativeRuntime.RootHandle> adopt, Func<NativeRuntime.RootHandle, IntPtr> verifyReceiver)
    {
        ArgumentNullException.ThrowIfNull(native); ArgumentNullException.ThrowIfNull(verifyAccess);
        ArgumentNullException.ThrowIfNull(adopt); ArgumentNullException.ThrowIfNull(verifyReceiver); verifyAccess();
        var owner = new NativeTweenAdmission(native, configuration, verifyAccess, () => { }, adopt, verifyReceiver, false);
        lock (ProcessGate) ProcessOwners.Add(owner);
        owner.Configure(); return owner;
    }

    private void Configure()
    {
        _verifyAccess();
        try
        {
            ValidateLayout(); _verifyProducer(); var configuration = _configuration;
            if (configuration.Version != 1 || configuration.Size != 32 || configuration.ExactTweenClass == IntPtr.Zero
                || configuration.CachedPointerField == IntPtr.Zero || configuration.CachedPointerOffset != 16 || configuration.Reserved != 0)
                throw new UnsupportedSignatureException("Tween admission configuration has no exact fixed metadata shape.");
            var result = _native.Configure(ref configuration);
            // Native Configure is once-only. Status 4 is admitted only when the
            // actual immutable policy already matches this process configuration.
            if (result is not (0 or 4) || !configuration.Equals(_configuration))
                throw new UnsupportedSignatureException("Native Tween admission Configure failed or changed its input: " + result);
            var info = NewInformation();
            if (_native.GetInfo(ref info) != 0 || !InformationMatches(info, _configuration))
                throw new UnsupportedSignatureException("Native Tween admission has no matching actual configured receipt.");
            _verifyProducer();
            _configurationEpoch = info.ConfigurationEpoch; _configured = true;
        }
        catch { Fault(); throw; }
    }

    internal NativeTweenAdmissionLease Claim(NativeRuntime.RootHandle actualTween, INativeTweenAdmissionOwner actualOwner)
    {
        ArgumentNullException.ThrowIfNull(actualTween);
        _verifyAccess(); ArgumentNullException.ThrowIfNull(actualOwner); RequireOwnerEnrollment(actualOwner);
        return ClaimCore(actualTween, actualTween.Target, actualOwner, adoptFromRaw: true);
    }

    /// <summary>Claims through a root already retained and returned by this
    /// authority's owning backend. The same handle remains process-retained by
    /// that backend and this lease; neither a seal nor a failed claim transfers
    /// or authorizes its disposal. The caller cannot supply a raw address.</summary>
    internal NativeTweenAdmissionLease ClaimRetainedRoot(NativeRuntime.RootHandle actualTween,
        INativeTweenAdmissionOwner actualOwner)
    {
        ArgumentNullException.ThrowIfNull(actualTween);
        _verifyAccess(); ArgumentNullException.ThrowIfNull(actualOwner); RequireOwnerEnrollment(actualOwner);
        return ClaimCore(actualTween, actualTween.Target, actualOwner, adoptFromRaw: false);
    }

    private NativeTweenAdmissionLease ClaimCore(NativeRuntime.RootHandle actualTween, IntPtr value,
        INativeTweenAdmissionOwner actualOwner, bool adoptFromRaw)
    {
        _verifyAccess(); ArgumentNullException.ThrowIfNull(actualTween); ArgumentNullException.ThrowIfNull(actualOwner);
        RequireKnown(); actualOwner.VerifyTweenAdmissionOwner();
        RequireOwnerEnrollment(actualOwner);
        if (_leases.Count >= 128) throw new InvalidOperationException("Tween claim responsibility exceeds its fixed 128-record bound.");
        var lease = new NativeTweenAdmissionLease(this, actualOwner, value, _leaseIssuer);
        _leases.Add(lease); // Record before allocating the independent strong root or entering native Claim.
        try
        {
            lease.RecordRoot(adoptFromRaw ? _adopt(value) : actualTween, adoptFromRaw
                ? NativeTweenAdmissionRootRetention.IndependentClaimRoot
                : NativeTweenAdmissionRootRetention.SharedRetainedProcessRoot, _leaseIssuer);
            if (_verifyReceiver(lease.Root!) != value)
                throw new TargetExpiredException("The independent Tween claim root changed its actual identity.");
            RequireKnown(); actualOwner.VerifyTweenAdmissionOwner(); VerifyInformation();
            TryClaim(lease); actualOwner.VerifyTweenAdmissionOwner(); return lease;
        }
        catch { lease.Fault(); Fault(); throw; }
    }

    internal NativeTweenAdmissionSnapshot Observe(NativeTweenAdmissionLease lease, bool seal)
    {
        _verifyAccess(); VerifyLease(lease);
        try
        {
            lease.Owner.VerifyTweenAdmissionOwner(); RequireKnown(); VerifyInformation();
            if (_verifyReceiver(lease.Root!) != lease.Object)
                throw new TargetExpiredException("The retained Tween admission root changed its actual identity.");
            if (!lease.Claimed && !TryClaim(lease))
            {
                lease.Owner.VerifyTweenAdmissionOwner();
                return new(true, NativeTweenAdmissionPhase.Empty, uint.MaxValue, 0, lease.Object.ToInt64(),
                    _configuration.ExactTweenClass.ToInt64(), 0, 0, 0, 0, 0, 0);
            }
            var receipt = NewReceipt();
            var status = seal ? _native.Seal(lease.Slot, lease.Epoch, ref receipt) : _native.Read(lease.Slot, lease.Epoch, ref receipt);
            if (status != 0 && !(seal && status == 3)
                || !ReceiptMatches(receipt, lease.Object, _configuration.ExactTweenClass, lease.Slot, lease.Epoch)
                || !CanAdvance(lease.Phase, receipt.Phase, seal)
                || receipt.AdmittedIssuers < lease.AdmittedIssuers || receipt.DeniedIssuers < lease.DeniedIssuers || receipt.DeniedDead < lease.DeniedDead)
                throw new UnsupportedSignatureException("Native Tween admission observation is foreign, regressed or unknown: " + status);
            VerifyInformation(); lease.Accept(receipt, _leaseIssuer); lease.Owner.VerifyTweenAdmissionOwner(); return Snapshot(receipt);
        }
        catch { lease.Fault(); Fault(); throw; }
    }

    private void VerifyLease(NativeTweenAdmissionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(lease.Authority, this) || !_leases.Contains(lease) || lease.Unknown || !lease.RootRecorded || lease.NativeRetired)
            throw new InvalidOperationException("The Tween admission lease is foreign, unclaimed or permanently unknown.");
    }
    private bool TryClaim(NativeTweenAdmissionLease lease)
    {
        var receipt = NewReceipt(); var status = _native.Claim(lease.Object, ref receipt);
        lease.RecordAttempt(receipt, _leaseIssuer);
        VerifyInformation();
        // Status 6 is an independently classified no-mutation physical busy
        // preflight. Duplicate status 3 is permanent ambiguity, never retried.
        if (status == 6) return false;
        if (status != 0 || !ReceiptMatches(receipt, lease.Object, _configuration.ExactTweenClass, null, null)
            || receipt.Phase != NativeTweenAdmissionPhase.Open)
            throw new UnsupportedSignatureException("Native Tween claim was rejected or has an unknown actual identity: " + status);
        lease.Accept(receipt, _leaseIssuer); return true;
    }
    internal bool IsLeaseIssuer(object issuer) => ReferenceEquals(issuer, _leaseIssuer);
    private void RequireKnown()
    { if (!_configured || Unknown) throw new InvalidOperationException("Tween admission responsibility is permanently unknown or not configured."); }
    private void VerifyInformation()
    {
        _verifyProducer(); var info = NewInformation();
        if (_native.GetInfo(ref info) != 0 || !InformationMatches(info, _configuration) || info.ConfigurationEpoch != _configurationEpoch)
            throw new UnsupportedSignatureException("The permanent Tween admission policy changed or became unknown.");
    }
    private void Fault() => Interlocked.Exchange(ref _unknown, 1);
    private static NativeTweenAdmissionInformation NewInformation() => new() { Version = 1, Size = 104 };
    private static NativeTweenAdmissionReceipt NewReceipt() => new() { Version = 1, Size = 96 };

    internal static void ValidateLayout()
    {
        if (IntPtr.Size != 8 || Marshal.SizeOf<NativeTweenAdmissionConfiguration>() != 32
            || Marshal.SizeOf<NativeTweenAdmissionReceipt>() != 96 || Marshal.SizeOf<NativeTweenAdmissionInformation>() != 104
            || Marshal.OffsetOf<NativeTweenAdmissionReceipt>(nameof(NativeTweenAdmissionReceipt.Epoch)).ToInt32() != 16
            || Marshal.OffsetOf<NativeTweenAdmissionReceipt>(nameof(NativeTweenAdmissionReceipt.Object)).ToInt32() != 24
            || Marshal.OffsetOf<NativeTweenAdmissionReceipt>(nameof(NativeTweenAdmissionReceipt.CachedNative)).ToInt32() != 40
            || Marshal.OffsetOf<NativeTweenAdmissionReceipt>(nameof(NativeTweenAdmissionReceipt.IssuersInFlight)).ToInt32() != 48
            || Marshal.OffsetOf<NativeTweenAdmissionReceipt>(nameof(NativeTweenAdmissionReceipt.Unknown)).ToInt32() != 88)
            throw new UnsupportedSignatureException("The native Tween admission wire differs from its fixed V1 layout.");
    }
    internal static bool InformationMatches(NativeTweenAdmissionInformation info, NativeTweenAdmissionConfiguration configuration)
        => info.Version == 1 && info.Size == 104 && info.Configured == 1 && info.Unknown == 0 && info.UnknownReads == 0
            && info.ExactTweenClass == configuration.ExactTweenClass && info.CachedPointerField == configuration.CachedPointerField
            && info.CachedPointerOffset == 16 && info.Capacity == 128 && info.ConfigurationEpoch != 0
            && info.ActiveOwners <= 128 && info.OriginalCalls <= info.Calls && info.SkippedDead <= info.Calls && info.SkippedSealed <= info.Calls;
    internal static bool ReceiptMatches(NativeTweenAdmissionReceipt receipt, IntPtr value, IntPtr exactClass, uint? slot, ulong? epoch)
        => receipt.Version == 1 && receipt.Size == 96 && receipt.Slot < 128 && receipt.Epoch != 0
            && receipt.Object == value && value != IntPtr.Zero && receipt.ActualClass == exactClass && exactClass != IntPtr.Zero
            && (slot is null || receipt.Slot == slot.Value) && (epoch is null || receipt.Epoch == epoch.Value)
            && receipt.Unknown == 0 && receipt.Reserved == 0 && (receipt.Phase is NativeTweenAdmissionPhase.Open or NativeTweenAdmissionPhase.Closing or NativeTweenAdmissionPhase.Sealed)
            && receipt.IssuersInFlight <= receipt.ActiveEntries && receipt.ActiveEntries <= long.MaxValue
            && (receipt.Phase != NativeTweenAdmissionPhase.Sealed || receipt.IssuersInFlight == 0);
    internal static bool CanAdvance(NativeTweenAdmissionPhase old, NativeTweenAdmissionPhase next, bool sealing)
        => (uint)next >= (uint)old && (next is NativeTweenAdmissionPhase.Open or NativeTweenAdmissionPhase.Closing or NativeTweenAdmissionPhase.Sealed)
            && (!sealing || next is NativeTweenAdmissionPhase.Closing or NativeTweenAdmissionPhase.Sealed);
    private static NativeTweenAdmissionSnapshot Snapshot(NativeTweenAdmissionReceipt receipt)
        => new(true, receipt.Phase, receipt.Slot, receipt.Epoch, receipt.Object.ToInt64(), receipt.ActualClass.ToInt64(), receipt.CachedNative.ToInt64(),
            receipt.IssuersInFlight, receipt.ActiveEntries, receipt.AdmittedIssuers, receipt.DeniedIssuers, receipt.DeniedDead);

    private sealed class LoadedTransport : INativeTweenAdmissionTransport
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(ref NativeTweenAdmissionConfiguration value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(ref NativeTweenAdmissionInformation value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ClaimNative(IntPtr value, ref NativeTweenAdmissionReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint OperationNative(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt);
        private readonly ConfigureNative _configure; private readonly InfoNative _info; private readonly ClaimNative _claim;
        private readonly OperationNative _read, _seal, _unregister;
        internal LoadedTransport(NativeFirstInstall actual)
        {
            actual.VerifyReady();
            T Export<T>(string name) where T : Delegate
            { var address = NativeLibrary.GetExport(actual.HelperModule, name); HookTargetValidation.VerifyExecutable(address, actual.HelperModule); return Marshal.GetDelegateForFunctionPointer<T>(address); }
            _configure = Export<ConfigureNative>("RizTweenAdmission_Configure"); _info = Export<InfoNative>("RizTweenAdmission_GetInfo");
            _claim = Export<ClaimNative>("RizTweenAdmission_Claim"); _read = Export<OperationNative>("RizTweenAdmission_Read");
            _seal = Export<OperationNative>("RizTweenAdmission_Seal"); _unregister = Export<OperationNative>("RizTweenAdmission_Unregister");
        }
        public uint Configure(ref NativeTweenAdmissionConfiguration configuration) => _configure(ref configuration);
        public uint GetInfo(ref NativeTweenAdmissionInformation information) => _info(ref information);
        public uint Claim(IntPtr actualTween, ref NativeTweenAdmissionReceipt receipt) => _claim(actualTween, ref receipt);
        public uint Read(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) => _read(slot, epoch, ref receipt);
        public uint Seal(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) => _seal(slot, epoch, ref receipt);
        public uint Unregister(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) => _unregister(slot, epoch, ref receipt);
    }
}

internal sealed partial class NativeTweenAdmissionLease
{
    internal NativeTweenAdmission Authority { get; }
    internal INativeTweenAdmissionOwner Owner { get; }
    internal IntPtr Object { get; }
    internal NativeRuntime.RootHandle? Root { get; private set; }
    internal bool RootRecorded => Root is not null && !OwnRootFreeSucceeded && !SharedBorrowReleased;
    internal NativeTweenAdmissionRootRetention RootRetention { get; private set; }
    internal bool Claimed { get; private set; }
    internal bool ClaimPending => !Claimed && RootRecorded && !Unknown;
    internal bool Unknown { get; private set; }
    internal uint Slot { get; private set; }
    internal ulong Epoch { get; private set; }
    internal NativeTweenAdmissionPhase Phase { get; private set; }
    internal NativeTweenAdmissionReceipt LastClaimAttempt { get; private set; }
    internal ulong AdmittedIssuers { get; private set; }
    internal ulong DeniedIssuers { get; private set; }
    internal ulong DeniedDead { get; private set; }
    // Authority is validated against its private lease registry before any use.
    internal NativeTweenAdmissionLease(NativeTweenAdmission authority, INativeTweenAdmissionOwner owner, IntPtr value, object issuer)
    { if (!authority.IsLeaseIssuer(issuer)) throw new InvalidOperationException("Only the private admission authority may issue a lease."); (Authority, Owner, Object) = (authority, owner, value); }
    internal void RecordRoot(NativeRuntime.RootHandle root, NativeTweenAdmissionRootRetention retention, object issuer)
    {
        VerifyIssuer(issuer); ArgumentNullException.ThrowIfNull(root);
        if (Root is not null || retention is not (NativeTweenAdmissionRootRetention.IndependentClaimRoot or NativeTweenAdmissionRootRetention.SharedRetainedProcessRoot))
            throw new InvalidOperationException("Tween lease root is already recorded or has no exact retention owner.");
        Root = root; RootRetention = retention;
    }
    internal void RecordAttempt(NativeTweenAdmissionReceipt receipt, object issuer) { VerifyIssuer(issuer); LastClaimAttempt = receipt; }
    internal void Accept(NativeTweenAdmissionReceipt receipt, object issuer)
    { VerifyIssuer(issuer); Slot = receipt.Slot; Epoch = receipt.Epoch; Phase = receipt.Phase; AdmittedIssuers = receipt.AdmittedIssuers; DeniedIssuers = receipt.DeniedIssuers; DeniedDead = receipt.DeniedDead; Claimed = true; }
    internal void Fault() => Unknown = true;
    private void VerifyIssuer(object issuer) { if (!Authority.IsLeaseIssuer(issuer)) throw new InvalidOperationException("Foreign Tween lease issuer."); }
    internal NativeTweenAdmissionSnapshot Read() => Authority.Observe(this, false);
    internal NativeTweenAdmissionSnapshot Seal() => Authority.Observe(this, true);
}
