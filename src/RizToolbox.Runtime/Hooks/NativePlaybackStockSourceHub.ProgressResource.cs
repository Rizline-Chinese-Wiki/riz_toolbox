using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    // Orthogonal V1: no bytes of SourceV4, PlayerWatchV2 or ProgressWatchV1
    // are repurposed. This projection cannot issue capture or source authority.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct ProgressResourceInformation
    {
        internal uint Version, Size, Phase, Current;
        internal ulong OwnerAddress, OwnerEpoch, Receipt, Ordinal, ProgressWatchReceipt, Progress, Class;
        internal ulong ContinuityIdentity, Revision, Reference, Initializer, InitializerOrdinal;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadProgressResourceNative(IntPtr owner, IntPtr watch, ref ProgressResourceInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StageProgressResourceNative(IntPtr owner, IntPtr watch, ulong revision, IntPtr rootedReference, out IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadProgressResourceTicketNative(IntPtr owner, IntPtr receipt, ref ProgressResourceInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BindInitializationResourceNative(IntPtr owner, IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RetireProgressResourceNative(IntPtr owner, IntPtr receipt);
    private bool _progressResourceEnabled;
    private readonly object _progressResourceIssuer = new();
    private readonly List<ProgressResourceTicket> _progressResourceTickets = new();

    internal void ConfigureProgressResource()
    {
        VerifyThread(); _image.Verify();
        if (_progressResourceEnabled || _progressWatchClass == IntPtr.Zero
            || Marshal.SizeOf<ProgressResourceInformation>() != 112
            || Export<VersionNative>("RtPlaybackProgressResourceVersion")() != 1)
            throw new UnsupportedSignatureException("The private Progress resource V1 contract differs from its exact issuer.");
        _progressResourceEnabled = true;
    }

    internal ProgressResourceTicket CaptureProgressResource(object watchRootOwner, NativeRuntime api)
        => CaptureProgressResourceCore(watchRootOwner, api, auditImage: true);

    internal ProgressResourceTicket CaptureOperationProgressResource(object watchRootOwner, NativeRuntime api)
        => CaptureProgressResourceCore(watchRootOwner, api, auditImage: false);

    private ProgressResourceTicket CaptureProgressResourceCore(object watchRootOwner, NativeRuntime api, bool auditImage)
    {
        VerifyThread();
        if (auditImage) _image.Verify(); else _image.VerifyOperationModule();
        ArgumentNullException.ThrowIfNull(watchRootOwner); ArgumentNullException.ThrowIfNull(api);
        if (!_progressResourceEnabled) throw new UnsupportedSignatureException("This explicit historical source profile has no resource continuity credential.");
        var watches = _progressWatches.Where(value => ReferenceEquals(value.RootOwner, watchRootOwner)).ToArray();
        if (watches.Length != 1) throw new UnsupportedSignatureException("Resource capture requires one actual independently rooted Progress watch.");
        var watch = watches[0];
        if (auditImage) watch.Verify(watchRootOwner, active: false);
        else watch.VerifyOperation(watchRootOwner, active: false);
        var state = NewProgressResourceInformation();
        if (Export<ReadProgressResourceNative>("RtPlaybackSourceReadProgressResource")(_owner, watch.NativeReceipt, ref state) != 1
            || !ValidProgressResourceOwner(state) || state.Phase != 0 || state.Current != 1 || state.Receipt != 0
            || state.ProgressWatchReceipt != Bits(watch.NativeReceipt) || state.Progress != Bits(watch.ProgressIdentity)
            || state.Class != Bits(watch.ClassIdentity) || state.ContinuityIdentity == 0 || state.Revision == 0)
            throw new UnsupportedSignatureException("The native retained Progress watch cannot issue a current resource state.");
        var field = api.InspectProgressClipResourceField(watch.ClassIdentity);
        NativeRuntime.RootHandle? resourceRoot = null; ProgressResourceTicket? ticket = null;
        var reference = IntPtr.Zero;
        try
        {
            api.PublishExactReferenceField(watch.ProgressRoot, field, value =>
            {
                // Adopt the actual published field result before inspecting its
                // target, class or any metadata. Null owns no native GC root.
                reference = value;
                if (value != IntPtr.Zero) resourceRoot = api.Root(value);
            });
            if (Bits(reference) != state.Reference || resourceRoot is not null
                && (resourceRoot.Target != reference || !api.ExactHookReceiverMatches(resourceRoot.Target, field.FieldClass)))
                throw new UnsupportedSignatureException("Progress resource changed while its independent capture root was retained.");
            if (Export<StageProgressResourceNative>("RtPlaybackSourceStageProgressResource")(_owner, watch.NativeReceipt,
                state.Revision, reference, out var receipt) != 1 || receipt == IntPtr.Zero)
                throw new UnsupportedSignatureException("The native resource revision changed before the exact rooted candidate could stage.");
            ticket = ProgressResourceTicket.IssueActual(this, receipt, state, resourceRoot, _progressResourceIssuer);
            _progressResourceTickets.Add(ticket); resourceRoot = null;
            if (auditImage) ticket.VerifyCurrent(); else ticket.VerifyOperationCurrent();
            return ticket;
        }
        catch
        {
            // Once native Stage succeeded, only its actual retirement receipt
            // can release the root. Failed post-stage inspection stays retained.
            if (ticket is not null) ticket.Dispose(); else resourceRoot?.Dispose();
            throw;
        }
    }
    private static ulong Bits(IntPtr value) => unchecked((ulong)value.ToInt64());
    private static ProgressResourceInformation NewProgressResourceInformation() => new() { Version = 1, Size = 112 };
    private bool ValidProgressResourceOwner(ProgressResourceInformation value)
        => value.Version == 1 && value.Size == 112 && value.OwnerAddress == Bits(_owner) && value.OwnerEpoch == Epoch && value.Current <= 1;

    internal void PumpProgressResourceTickets()
    {
        if (_thread != Environment.CurrentManagedThreadId) return;
        VerifyThread();
        foreach (var ticket in _progressResourceTickets.Where(value => value.ReleaseRequested).ToArray()) ticket.TryRetire();
    }

    /// <summary>Only this hub can construct a resource candidate. Its independent
    /// reference root outlives invalidation until native physical retirement.
    /// Consumed receipts are completion evidence, never new source authority.</summary>
    internal sealed class ProgressResourceTicket : IDisposable
    {
        private readonly NativePlaybackStockSourceHub _owner;
        private readonly IntPtr _receipt;
        private readonly ProgressResourceInformation _basis;
        private NativeRuntime.RootHandle? _referenceRoot;
        private ulong _ordinal, _initializer, _initializerOrdinal;
        private bool _releaseRequested, _nativeRetired, _retired;
        private Exception? _releaseFailure;
        internal bool RootsRetained => _referenceRoot is not null;
        internal bool ReleaseRequested => _releaseRequested;
        internal ulong Revision => _basis.Revision;
        internal ulong ContinuityIdentity => _basis.ContinuityIdentity;
        private ProgressResourceTicket(NativePlaybackStockSourceHub owner, IntPtr receipt, ProgressResourceInformation basis, NativeRuntime.RootHandle? referenceRoot)
            => (_owner, _receipt, _basis, _referenceRoot) = (owner, receipt, basis, referenceRoot);
        internal static ProgressResourceTicket IssueActual(NativePlaybackStockSourceHub owner, IntPtr receipt,
            ProgressResourceInformation basis, NativeRuntime.RootHandle? referenceRoot, object issuer)
        {
            if (!ReferenceEquals(owner._progressResourceIssuer, issuer))
                throw new UnsupportedSignatureException("Only the actual rooted native resource capture can issue a ticket.");
            return new(owner, receipt, basis, referenceRoot);
        }

        internal ProgressResourceInformation Read()
            => ReadCore(auditImage: true);
        internal ProgressResourceInformation ReadOperation()
            => ReadCore(auditImage: false);
        private ProgressResourceInformation ReadCore(bool auditImage)
        {
            _owner.VerifyThread();
            if (auditImage) _owner._image.Verify(); else _owner._image.VerifyOperationModule();
            if (_retired || !_owner._progressResourceTickets.Contains(this))
                throw new ObjectDisposedException(nameof(ProgressResourceTicket));
            var value = NewProgressResourceInformation();
            if (_owner.Export<ReadProgressResourceTicketNative>("RtPlaybackSourceReadProgressResourceTicket")(_owner._owner, _receipt, ref value) != 1
                || !_owner.ValidProgressResourceOwner(value) || value.Receipt != Bits(_receipt) || value.Ordinal == 0
                || _ordinal != 0 && value.Ordinal != _ordinal || value.Phase is < 1 or > 5
                || value.ProgressWatchReceipt != _basis.ProgressWatchReceipt || value.Progress != _basis.Progress || value.Class != _basis.Class
                || value.ContinuityIdentity != _basis.ContinuityIdentity || value.Revision != _basis.Revision || value.Reference != _basis.Reference
                || _initializer != 0 && (value.Initializer != _initializer || value.InitializerOrdinal != _initializerOrdinal)
                || (value.Initializer == 0) != (value.InitializerOrdinal == 0)
                || value.Phase == 1 && value.Initializer != 0 || value.Phase is 2 or 3 && value.Initializer == 0
                || value.Phase is 3 or 4 or 5 && value.Current != 0)
                throw new UnsupportedSignatureException("The private resource ticket changed its exact issuer, revision, reference or initializer identity.");
            _ordinal = value.Ordinal;
            if (value.Initializer != 0) (_initializer, _initializerOrdinal) = (value.Initializer, value.InitializerOrdinal);
            return value;
        }
        internal void VerifyCurrent(bool allowConsumed = false)
            => VerifyCurrentCore(allowConsumed, auditImage: true);
        internal void VerifyOperationCurrent(bool allowConsumed = false)
            => VerifyCurrentCore(allowConsumed, auditImage: false);
        private void VerifyCurrentCore(bool allowConsumed, bool auditImage)
        {
            if (_releaseRequested) throw new ObjectDisposedException(nameof(ProgressResourceTicket));
            var value = ReadCore(auditImage);
            if (!(value.Phase is 1 or 2 && value.Current == 1 || allowConsumed && value.Phase == 3)
                || (_referenceRoot is null ? _basis.Reference != 0 : Bits(_referenceRoot.Target) != _basis.Reference))
                throw new UnsupportedSignatureException("The resource-dependent capture credential expired or has not reached its required native phase.");
        }
        internal void BindInitialization()
            => BindInitializationCore(auditImage: true);
        internal void BindOperationInitialization()
            => BindInitializationCore(auditImage: false);
        private void BindInitializationCore(bool auditImage)
        {
            VerifyCurrentCore(allowConsumed: false, auditImage: auditImage);
            if (_owner.Export<BindInitializationResourceNative>("RtPlaybackSourceBindInitializationResource")(_owner._owner, _receipt) != 1)
                throw new UnsupportedSignatureException("Only the actual state-zero Start Before can bind this exact resource credential.");
            var value = ReadCore(auditImage);
            if (value.Phase != 2 || value.Current != 1) throw new UnsupportedSignatureException("The resource credential did not bind its actual Start initializer.");
        }
        internal bool TryRetire()
        {
            _owner.VerifyThread();
            if (_releaseFailure is not null)
                throw new UnsupportedSignatureException("The resource root release previously failed and remains unresolved: " + _releaseFailure.Message);
            if (_retired) return true;
            _releaseRequested = true;
            if (!_nativeRetired)
            {
                if (_owner.Export<RetireProgressResourceNative>("RtPlaybackSourceRetireProgressResource")(_owner._owner, _receipt) != 1) return false;
                if (Read().Phase != 5) throw new UnsupportedSignatureException("Resource retirement lacks the native physical-drain receipt.");
                _nativeRetired = true;
            }
            try { _referenceRoot?.Dispose(); }
            catch (Exception error) { _releaseFailure = error; throw; }
            _referenceRoot = null; _retired = true; _owner._progressResourceTickets.Remove(this); return true;
        }
        public void Dispose() => TryRetire();
    }
}

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        internal NativePlaybackStockSourceHub.ProgressResourceTicket CaptureProgressResource(object watchRootOwner, NativeRuntime api)
        { VerifyCurrent(); return _hub!.CaptureProgressResource(watchRootOwner, api); }

        internal NativePlaybackStockSourceHub.ProgressResourceTicket CaptureOperationProgressResource(object watchRootOwner, NativeRuntime api)
        {
            VerifySourceOperationOwner();
            VerifySourceOperationRole(NativePlaybackSourceRole.ProgressStartMoveNext);
            return _hub!.CaptureOperationProgressResource(watchRootOwner, api);
        }
    }
}

internal sealed partial class PlaybackSourceGroupLease
{
    internal NativePlaybackStockSourceHub.ProgressResourceTicket CaptureProgressResource(object watchRootOwner, NativeRuntime api)
        => _owner.CaptureProgressResource(watchRootOwner, api);

    internal NativePlaybackStockSourceHub.ProgressResourceTicket CaptureOperationProgressResource(object watchRootOwner, NativeRuntime api)
    {
        VerifyOperationLease();
        return _owner.CaptureOperationProgressResource(watchRootOwner, api);
    }
}
