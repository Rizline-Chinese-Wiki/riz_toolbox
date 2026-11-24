using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackSingleReturnBridge
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct FreshSourceSnapshot
    {
        internal ulong Owner, OwnerEpoch, PlayerWatch, PlayerOrdinal, ProgressWatch, ProgressOrdinal;
        internal ulong BackingWatch, BackingOrdinal, Player, AudioSource, Progress, Clip;
        internal ulong SourceNative, ClipNative, SourceFence, AudioAttempt, Writer, Scheduling, Entered, Exited;
        internal int InstanceIdOffset, SourceId, ClipId;
        internal uint Reserved;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct FreshSourceInformation
    {
        internal uint Version, Size, Slot, Phase;
        internal IntPtr Receipt;
        internal ulong SourceEpoch;
        internal FreshSourceSnapshot Source;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint IssueFreshSourceNative(uint slot, IntPtr owner, IntPtr watch, out IntPtr receipt);
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct FreshSourceFailureInformation
    {
        internal uint Version, Size, Valid, Status, Origin, Action, SingleReturnMask, SourceBlockMask;
        internal uint SourceMaskValid, ScopeValid, Role, Kind, Depth, ChildFlags;
        internal ulong Sequence, ParentSequence, ArgBits;
        internal uint ParentRole, ParentKind, ParentStage, ParentFlags, ParentDepth;
        internal uint EngineChildTestedMask, EngineChildMatchedMask;
        internal uint ParentEngineBeginTestedMask, ParentEngineBeginMatchedMask;
        internal uint CallerInLowerBody, CallerLowerOffset, Reserved;
        internal ulong ParentArgBits;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadFreshSourceWithDiagnosticNative(
        IntPtr receipt, ref FreshSourceInformation info, ref FreshSourceFailureInformation failure);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint FreshSourceActionNative(IntPtr receipt);
    private FreshSourceReceipt? _freshSourceCapture;

    internal void RequireFreshSourceProtocol()
    {
        lock (_gate)
        {
            if (_fault is not null || Marshal.SizeOf<FreshSourceInformation>() != 208
                || Marshal.SizeOf<FreshSourceFailureInformation>() != 136
                || Export<LimitNative>("RizSingleReturnPrototype_FreshSourceVersion")() != 1
                || Export<LimitNative>("RizSingleReturnPrototype_FreshSourceDiagnosticVersion")() != 2)
                throw new UnsupportedSignatureException("The private native fresh-source protocol or V2 diagnostic is unavailable.");
            _ = Export<IssueFreshSourceNative>("RizSingleReturnPrototype_IssueFreshSource");
            _ = Export<ReadFreshSourceWithDiagnosticNative>("RizSingleReturnPrototype_ReadFreshSourceWithDiagnostic");
            _ = Export<FreshSourceActionNative>("RizSingleReturnPrototype_ClaimFreshSource");
            _ = Export<FreshSourceActionNative>("RizSingleReturnPrototype_ReleaseFreshSource");
        }
    }
    internal FreshSourceReceipt PrepareFreshSource(NativePlaybackStockSourceHub source,
        NativePlaybackStockSourceHub.PlayerWatch watch)
    {
        lock (_gate)
        {
            RequireFreshSourceProtocol();
            if (_freshSourceCapture is not null || _published is not null || _bootstrap is not null)
                throw new UnsupportedSignatureException("Only one registered initial fresh-source candidate belongs to this GetTime slot.");
            watch.VerifyPhase(watch.SourceOwner, 1);
            _freshSourceCapture = new(this, source, watch);
            return _freshSourceCapture;
        }
    }
    internal sealed partial class FreshSourceReceipt
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly NativePlaybackSingleReturnBridge _owner;
        private readonly NativePlaybackStockSourceHub _source;
        private readonly NativePlaybackStockSourceHub.PlayerWatch _watch;
        private readonly IssueFreshSourceNative _issue;
        private readonly ReadFreshSourceWithDiagnosticNative _read;
        private readonly FreshSourceActionNative _claim, _release;
        private IntPtr _receipt;
        private FreshSourceInformation? _basis;
        private bool _issued, _claimed, _released, _failed;
        internal FreshSourceReceipt(NativePlaybackSingleReturnBridge owner, NativePlaybackStockSourceHub source,
            NativePlaybackStockSourceHub.PlayerWatch watch)
        {
            (_owner, _source, _watch) = (owner, source, watch);
            _issue = owner.Export<IssueFreshSourceNative>("RizSingleReturnPrototype_IssueFreshSource");
            _read = owner.Export<ReadFreshSourceWithDiagnosticNative>("RizSingleReturnPrototype_ReadFreshSourceWithDiagnostic");
            _claim = owner.Export<FreshSourceActionNative>("RizSingleReturnPrototype_ClaimFreshSource");
            _release = owner.Export<FreshSourceActionNative>("RizSingleReturnPrototype_ReleaseFreshSource");
        }
        private void VerifyThread()
        {
            if (_thread != Environment.CurrentManagedThreadId) throw new UnsupportedSignatureException("Fresh source ticket belongs to its native owner thread.");
        }
        private void Registered()
        {
            VerifyThread();
            if (!ReferenceEquals(_owner._freshSourceCapture, this) || _owner._fault is not null || _released || _failed)
                throw new UnsupportedSignatureException("Copied, failed or released fresh-source receipt has no authority.");
        }
        internal void VerifyClaimedOperationRegistration()
        {
            lock (_owner._gate)
            {
                Registered();
                if (!_issued || !_claimed || _receipt == IntPtr.Zero || _basis is null)
                    throw new UnsupportedSignatureException("A running source operation requires the actual already claimed native receipt.");
            }
        }
        internal bool TryIssue()
        {
            lock (_owner._gate)
            {
                Registered();
                if (_issued) { Read(); return true; }
                var status = _issue(_owner._slot, _source.NativeOwner, _watch.NativeReceipt, out var receipt);
                if (receipt != IntPtr.Zero)
                {
                    if (_receipt != IntPtr.Zero && _receipt != receipt) { _failed = true; throw new UnsupportedSignatureException("Fresh-source retry replaced its actual pending native receipt."); }
                    _receipt = receipt;
                }
                if (status == 8) return false;
                if (status != 0 || _receipt == IntPtr.Zero) { _failed = true; throw new UnsupportedSignatureException($"Native fresh-source issue rejected: {status}."); }
                _issued = true; Read(); return true;
            }
        }
        internal FreshSourceInformation Read()
        {
            lock (_owner._gate)
            {
                Registered();
                if (!_issued || _receipt == IntPtr.Zero) throw new UnsupportedSignatureException("The same fresh-source candidate is still pending native drain.");
                var info = new FreshSourceInformation { Version = 1, Size = 208 };
                // Preserve the single native read and original rejection order.
                // A native refusal does not provide a valid output snapshot.
                var failure = new FreshSourceFailureInformation { Version = 2, Size = 136 };
                var status = _read(_receipt, ref info, ref failure);
                var expectedPhase = _claimed ? 2u : 1u;
                string? rejection = status != 0 ? "native-status"
                    : info.Version != 1 || info.Size != 208 ? "wire"
                    : info.Slot != _owner._slot ? "slot"
                    : info.Receipt != _receipt ? "receipt-identity"
                    : info.Phase != expectedPhase ? "phase"
                    : info.SourceEpoch == 0 || info.SourceEpoch > long.MaxValue ? "source-epoch"
                    : info.Source.Owner != unchecked((ulong)_source.NativeOwner.ToInt64()) || info.Source.OwnerEpoch != _source.Epoch ? "owner-identity"
                    : info.Source.PlayerWatch != unchecked((ulong)_watch.NativeReceipt.ToInt64()) ? "watch-identity"
                    : info.Source.PlayerOrdinal == 0 || info.Source.ProgressWatch == 0 || info.Source.ProgressOrdinal == 0
                        || info.Source.BackingWatch == 0 || info.Source.BackingOrdinal == 0 ? "watch-ordinals"
                    : info.Source.Reserved != 0 ? "reserved"
                    : _basis is { } basis && info.SourceEpoch != basis.SourceEpoch ? "basis-epoch"
                    : _basis is { } previous && !info.Source.Equals(previous.Source) ? "basis-snapshot"
                    : !ValidFailureWire(failure, status) || failure.Origin != 0
                        || failure.Action != 2 || failure.SourceMaskValid != 1
                        || failure.SingleReturnMask != 0 || failure.SourceBlockMask != 0 ? "diagnostic-wire"
                    : null;
                if (rejection is not null)
                {
                    _failed = true;
                    var phase = status == 0 ? $"; phase={info.Phase}; expectedPhase={expectedPhase}" : "";
                    throw new UnsupportedSignatureException("The actual joint native fresh-source receipt or its retained lifetime changed. "
                        + $"nativeStatus={status}; detail={rejection}{phase}{DescribeFailure(failure, status)}.");
                }
                _basis ??= info;
                return info;
            }
        }
        private static bool ValidFailureWire(FreshSourceFailureInformation value, uint status)
            => value.Version == 2 && value.Size == 136 && value.Valid == 1 && value.Status == status
                && value.Origin <= 3 && value.Action is >= 1 and <= 4
                && value.SourceMaskValid <= 1 && value.ScopeValid <= 1
                && (value.SingleReturnMask & ~3u) == 0 && (value.SourceBlockMask & ~31u) == 0
                && (value.SourceMaskValid != 0 || value.SourceBlockMask == 0)
                && value.Reserved == 0 && value.CallerInLowerBody <= 1
                && (value.CallerInLowerBody != 0 ? value.CallerLowerOffset <= 495 : value.CallerLowerOffset == 0)
                && (value.EngineChildTestedMask & ~0x1fffffffu) == 0
                && (value.ParentEngineBeginTestedMask & ~0x1fffffu) == 0
                && (value.EngineChildMatchedMask & ~value.EngineChildTestedMask) == 0
                && (value.ParentEngineBeginMatchedMask & ~value.ParentEngineBeginTestedMask) == 0
                && (value.ParentDepth != 0
                    ? value.ParentDepth < 64 && value.Depth == value.ParentDepth + 1
                    : value.ParentRole == 0 && value.ParentKind == 0 && value.ParentStage == 0
                        && value.ParentFlags == 0 && value.ParentArgBits == 0
                        && value.ParentEngineBeginTestedMask == 0 && value.ParentEngineBeginMatchedMask == 0)
                && (value.ScopeValid != 0 && value.Role == (uint)NativePlaybackSourceRole.EngineSetPitch
                    || NoEngineDiagnostic(value))
                && (value.ScopeValid != 0 || (value.Role == 0 && value.Kind == 0 && value.Depth == 0
                    && value.ChildFlags == 0 && value.Sequence == 0 && value.ParentSequence == 0 && value.ArgBits == 0));
        private static bool NoEngineDiagnostic(FreshSourceFailureInformation value)
            => value.ParentRole == 0 && value.ParentKind == 0 && value.ParentStage == 0 && value.ParentFlags == 0
                && value.ParentDepth == 0 && value.ParentArgBits == 0
                && value.EngineChildTestedMask == 0 && value.EngineChildMatchedMask == 0
                && value.ParentEngineBeginTestedMask == 0 && value.ParentEngineBeginMatchedMask == 0
                && value.CallerInLowerBody == 0 && value.CallerLowerOffset == 0;
        private static string DescribeFailure(FreshSourceFailureInformation value, uint status)
        {
            // These scalars explain a refusal, never source or Clock admission.
            // The V1 identity snapshot is not consumed on native failure.
            if (!ValidFailureWire(value, status)) return "; diagnostic=invalid";
            var origin = value.Origin switch { 0 => "none", 1 => "single-return", 2 => "source", 3 => "joint-commit", _ => "invalid" };
            var scalarArgument = (NativePlaybackSourceKind)value.Kind is NativePlaybackSourceKind.Single1
                or NativePlaybackSourceKind.Double1 or NativePlaybackSourceKind.Boolean1
                or NativePlaybackSourceKind.UInt64_1 or NativePlaybackSourceKind.IcallSingle1;
            var argument = scalarArgument ? value.ArgBits : 0;
            var parentScalar = (NativePlaybackSourceKind)value.ParentKind is NativePlaybackSourceKind.Single1
                or NativePlaybackSourceKind.Double1 or NativePlaybackSourceKind.Boolean1
                or NativePlaybackSourceKind.UInt64_1 or NativePlaybackSourceKind.IcallSingle1;
            var parentArgument = parentScalar ? value.ParentArgBits : 0;
            var engine = value.Role == (uint)NativePlaybackSourceRole.EngineSetPitch
                ? $"; parentRole={(NativePlaybackSourceRole)value.ParentRole}; parentKind={value.ParentKind}"
                    + $"; parentStage={value.ParentStage}; parentFlags=0x{value.ParentFlags:x}; parentDepth={value.ParentDepth}"
                    + $"; parentArgBits=0x{parentArgument:x}; engineTested=0x{value.EngineChildTestedMask:x}"
                    + $"; engineMatched=0x{value.EngineChildMatchedMask:x}; beginTested=0x{value.ParentEngineBeginTestedMask:x}"
                    + $"; beginMatched=0x{value.ParentEngineBeginMatchedMask:x}; callerInLower={value.CallerInLowerBody}"
                    + $"; callerLowerOffset=0x{value.CallerLowerOffset:x}"
                : "";
            return $"; origin={origin}; action={value.Action}; singleMask=0x{value.SingleReturnMask:x}"
                + $"; sourceMaskValid={value.SourceMaskValid}; sourceMask=0x{value.SourceBlockMask:x}"
                + $"; scopeValid={value.ScopeValid}; role={(NativePlaybackSourceRole)value.Role}; kind={value.Kind}"
                + $"; sequence={value.Sequence}; parent={value.ParentSequence}; depth={value.Depth}"
                + $"; argBits=0x{argument:x}; childFlags=0x{value.ChildFlags:x}{engine}";
        }
        internal bool TryClaim()
        {
            lock (_owner._gate)
            {
                Registered(); Read();
                if (_claimed) throw new UnsupportedSignatureException("A fresh-source native claim cannot be replayed.");
                var status = _claim(_receipt);
                if (status == 8) return false;
                if (status != 0) { _failed = true; throw new UnsupportedSignatureException($"Native fresh-source claim rejected: {status}."); }
                _claimed = true; Read(); return true;
            }
        }
        internal bool TryRelease()
        {
            lock (_owner._gate)
            {
                VerifyThread();
                // Cleanup uses the retained registered ticket even after a
                // failed read. A projection never substitutes for that ticket.
                if (!ReferenceEquals(_owner._freshSourceCapture, this)) throw new UnsupportedSignatureException("Foreign fresh-source cleanup owner.");
                if (_released) return true;
                if (_owner._freshClock is { IsClosed: false }) return false;
                if (_receipt != IntPtr.Zero)
                {
                    var status = _release(_receipt);
                    if (status == 8) return false;
                    if (status != 0) throw new UnsupportedSignatureException($"Native fresh-source release rejected: {status}.");
                }
                _released = true; return true;
            }
        }
    }
}
