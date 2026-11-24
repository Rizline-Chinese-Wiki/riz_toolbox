using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackSingleReturnBridge
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct FreshClockBaseline
    {
        internal uint Version, Size;
        internal ulong OwnerEpoch, RootSequence, RootToken, LeafSequence, LeafToken;
        internal ulong Writer, Source, Scheduling, LeafCaller;
        internal uint RootRole, RootSlot, LeafSlot, Reserved;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct FreshClockInformation
    {
        internal uint Version, Size, Slot, Phase;
        internal ulong Epoch, InitialGeneration, CurrentGeneration, InFlight;
        internal IntPtr Receipt;
        internal uint Unknown, Reserved;
        internal NativePlaybackSingleReturnNativePolicy Policy;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint PrepareFreshClockNative(
        IntPtr source, ref FreshClockBaseline baseline, ref NativePlaybackSingleReturnNativePolicy policy, out IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadFreshClockNative(IntPtr receipt, ref FreshClockInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint PublishFreshClockNative(
        IntPtr receipt, ulong expected, ref NativePlaybackSingleReturnNativePolicy policy);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint WithdrawFreshClockNative(IntPtr receipt, ulong expected);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BeginFreshClockHandoffNative(IntPtr receipt, ulong expected, out IntPtr handoff);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BeginFreshClockInitializationNative(IntPtr receipt, ulong expected, IntPtr initializer, out IntPtr handoff);
    private FreshClockReceipt? _freshClock;

    internal FreshClockReceipt PrepareFreshClock(FreshSourceReceipt source,
        NativePlaybackStockOccurrenceProducer.ExecutionReceipt baseline, NativePlaybackSingleReturnAuthority initial)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(baseline); ArgumentNullException.ThrowIfNull(initial);
        lock (_gate)
        {
            var graph = source.VerifyClockOwner(this);
            if (_fault is not null || _freshClock is not null || _published is not null || _bootstrap is not null
                || !initial.IsValid || initial.Phase != NativePlaybackSingleReturnPhase.Paused)
                throw new UnsupportedSignatureException("Only the claimed source's first paused Clock can be prepared.");
            if ((ulong)initial.Identity.SourceGeneration != graph.SourceEpoch
                || unchecked((ulong)initial.Identity.Player.ToInt64()) != graph.Source.Player
                || unchecked((ulong)initial.Identity.AudioSource.ToInt64()) != graph.Source.AudioSource
                || unchecked((ulong)initial.Identity.Progress.ToInt64()) != graph.Source.Progress
                || unchecked((ulong)initial.Identity.Clip.ToInt64()) != graph.Source.Clip)
                throw new UnsupportedSignatureException("Initial Clock identity must preserve the original FreshSource epoch and receivers.");
            if (Marshal.SizeOf<FreshClockBaseline>() != 96 || Marshal.SizeOf<FreshClockInformation>() != 192
                || Marshal.SizeOf<HandoffInformation>() != 104
                || Export<LimitNative>("RizSingleReturnPrototype_FreshClockVersion")() != 2)
                throw new UnsupportedSignatureException("The joint FreshClock V2 native protocol is unavailable.");
            return _freshClock = new(this, source, baseline, initial);
        }
    }

    internal sealed partial class FreshSourceReceipt
    {
        internal FreshSourceInformation VerifyClockOwner(NativePlaybackSingleReturnBridge owner)
        {
            Registered();
            if (!ReferenceEquals(_owner, owner) || !_claimed)
                throw new UnsupportedSignatureException("Fresh Clock must consume this exact bridge's already claimed source ticket.");
            return Read();
        }
        internal uint PrepareClock(NativePlaybackSingleReturnBridge owner,
            NativePlaybackStockOccurrenceProducer.ExecutionReceipt baseline,
            ref NativePlaybackSingleReturnNativePolicy policy, out IntPtr receipt)
        {
            var info = VerifyClockOwner(owner);
            var selector = baseline.FreshClockBaseline(_source, info.Source);
            return owner.Export<PrepareFreshClockNative>("RizSingleReturnPrototype_PrepareFreshMusicClock")(
                _receipt, ref selector, ref policy, out receipt);
        }
        internal bool OwnsLogicalHub(NativePlaybackStockSourceHub hub)
        {
            VerifyThread();
            return ReferenceEquals(_owner._freshSourceCapture, this) && ReferenceEquals(_source, hub);
        }
        internal IntPtr CompletedInitializerIdentity(NativePlaybackStockSourceHub.CompletedInitializationReceipt receipt)
        {
            VerifyThread();
            if (!ReferenceEquals(_owner._freshSourceCapture, this) || !_claimed)
                throw new UnsupportedSignatureException("The logical initializer does not belong to the original registered source hub.");
            return receipt.NativeIdentityFor(_source);
        }
    }

    internal sealed class FreshClockReceipt
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly NativePlaybackSingleReturnBridge _owner;
        private readonly FreshSourceReceipt _source;
        private readonly NativePlaybackStockOccurrenceProducer.ExecutionReceipt _baseline;
        private readonly NativePlaybackSingleReturnAuthority _initial;
        private readonly ReadFreshClockNative _read;
        private readonly FreshSourceActionNative _arm, _cancel;
        private readonly PublishFreshClockNative _publish;
        private readonly WithdrawFreshClockNative _withdraw;
        private readonly BeginFreshClockHandoffNative _beginHandoff;
        private readonly BeginFreshClockInitializationNative _beginInitialization, _beginCrossRunInitialization;
        private IntPtr _receipt;
        private ulong _epoch;
        private bool _closed, _armCommitted, _transferred;
        internal FreshClockReceipt(NativePlaybackSingleReturnBridge owner, FreshSourceReceipt source,
            NativePlaybackStockOccurrenceProducer.ExecutionReceipt baseline, NativePlaybackSingleReturnAuthority initial)
        {
            (_owner, _source, _baseline, _initial) = (owner, source, baseline, initial);
            _read = owner.Export<ReadFreshClockNative>("RizSingleReturnPrototype_ReadFreshMusicClock");
            _arm = owner.Export<FreshSourceActionNative>("RizSingleReturnPrototype_ArmFreshMusicClock");
            _cancel = owner.Export<FreshSourceActionNative>("RizSingleReturnPrototype_CancelFreshMusicClock");
            _publish = owner.Export<PublishFreshClockNative>("RizSingleReturnPrototype_PublishFreshMusicClock");
            _withdraw = owner.Export<WithdrawFreshClockNative>("RizSingleReturnPrototype_WithdrawFreshMusicClock");
            _beginHandoff = owner.Export<BeginFreshClockHandoffNative>("RizSingleReturnPrototype_BeginFreshMusicClockHandoff");
            _beginInitialization = owner.Export<BeginFreshClockInitializationNative>("RizSingleReturnPrototype_BeginFreshMusicClockInitializationHandoff");
            _beginCrossRunInitialization = owner.Export<BeginFreshClockInitializationNative>("RizSingleReturnPrototype_BeginFreshMusicClockCrossRunInitializationHandoff");
        }
        // Availability belongs to the actual armed predecessor. Each begin
        // still needs the source helper's real joint cut or completed Start.
        internal bool SupportsCrossRunHandoff { get { Registered(); return _armCommitted && !_transferred && _owner._fault is null; } }
        internal bool SupportsInitializationHandoff => SupportsCrossRunHandoff;
        internal bool SupportsCrossRunInitializationHandoff => SupportsCrossRunHandoff;
        internal bool SupportsHandoff => SupportsCrossRunHandoff;
        internal bool HasTransferred => _transferred;
        internal bool HasLogicalBinding => _armCommitted;
        internal bool IsClosed { get { Registered(); return _closed; } }
        private void Registered()
        {
            if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_owner._freshClock, this))
                throw new UnsupportedSignatureException("Fresh Clock receipt is copied, foreign or consumed on another thread.");
        }
        private void Open()
        {
            Registered();
            if (_closed || _owner._fault is not null) throw new UnsupportedSignatureException("Fresh Clock publication is closed or faulted.");
        }
        internal bool TryPrepare()
        {
            lock (_owner._gate)
            {
                Open();
                if (_receipt != IntPtr.Zero) { Read(1); return true; }
                var info = _source.VerifyClockOwner(_owner);
                var policy = _initial.ToNative(_owner._readerPointer, _owner._owner);
                var status = _source.PrepareClock(_owner, _baseline, ref policy, out var receipt);
                if (status == 8) return false;
                if (status != 0 || receipt == IntPtr.Zero)
                    throw new UnsupportedSignatureException($"Native Fresh Clock prepare rejected: {status}.");
                // Retain exact cleanup ownership before any fallible readback.
                _receipt = receipt; _epoch = info.SourceEpoch; _owner._published = _initial;
                Read(1); return true;
            }
        }
        internal FreshClockInformation Read(uint phase)
        {
            lock (_owner._gate)
            {
                Open();
                if (_receipt == IntPtr.Zero) throw new UnsupportedSignatureException("Fresh Clock is still pending native preparation.");
                var info = new FreshClockInformation { Version = 1, Size = 192 };
                if (_read(_receipt, ref info) != 0 || info.Version != 1 || info.Size != 192 || info.Slot != _owner._slot
                    || info.Phase != phase || info.Epoch != _epoch || info.Receipt != _receipt
                    || info.InitialGeneration != (ulong)_initial.Revision || info.Unknown != 0 || info.Reserved != 0
                    || _owner._published is not { } current || info.CurrentGeneration != (ulong)current.Revision
                    || !current.MatchesNative(info.Policy, _owner._readerPointer, _owner._owner))
                    throw new UnsupportedSignatureException("The same native Fresh Clock ticket, source epoch or immutable policy changed.");
                var actual = _owner.ReadCurrentCore();
                if (actual.Active != (phase == 2) || !ReferenceEquals(actual.Authority, current))
                    throw new UnsupportedSignatureException("The actual Fresh Clock activation differs from its retained publication.");
                return info;
            }
        }
        internal bool TryArm()
        {
            lock (_owner._gate)
            {
                Open(); Read(1); _source.VerifyClockOwner(_owner); _baseline.VerifyCurrent();
                if (!_baseline.PhysicalExited) throw new UnsupportedSignatureException("Pause has not physically exited.");
                var status = _arm(_receipt);
                if (status == 8) return false;
                if (status != 0) throw new UnsupportedSignatureException($"Native Fresh Clock Arm rejected: {status}.");
                _armCommitted = true; _owner._logicalBound = true; Read(2); return true;
            }
        }
        internal void Publish(long expectedRevision, NativePlaybackSingleReturnAuthority next)
        {
            ArgumentNullException.ThrowIfNull(next);
            lock (_owner._gate)
            {
                Open(); Read(2);
                if (!next.IsValid || expectedRevision <= 0 || _owner._published is not { } prior
                    || prior.Revision != expectedRevision || next.Revision <= expectedRevision
                    || prior.Identity != next.Identity || prior.PermittedNullCaller != next.PermittedNullCaller)
                    throw new UnsupportedSignatureException("Fresh Clock expected revision or exact identity differs.");
                var policy = next.ToNative(_owner._readerPointer, _owner._owner);
                var status = _publish(_receipt, checked((ulong)expectedRevision), ref policy);
                if (status != 0) throw new UnsupportedSignatureException($"Native Fresh Clock publish rejected: {status}.");
                _owner._published = next; Read(2);
            }
        }
        internal bool Withdraw(long expectedRevision)
        {
            lock (_owner._gate)
            {
                Registered();
                if (_transferred) throw new UnsupportedSignatureException("A transferred Fresh Clock cannot withdraw its successor.");
                if (expectedRevision <= 0 || _receipt == IntPtr.Zero) throw new UnsupportedSignatureException("Fresh Clock cleanup requires its actual prepared revision.");
                var status = _withdraw(_receipt, checked((ulong)expectedRevision));
                if (status == 5) return false;
                if (status != 0) throw new UnsupportedSignatureException($"Native Fresh Clock withdrawal rejected: {status}.");
                _closed = true; return true;
            }
        }
        internal LogicalHandoff BeginHandoff() => BeginHandoffCore(IntPtr.Zero);
        internal LogicalHandoff BeginInitializationHandoff(IntPtr initializer)
        {
            if (initializer == IntPtr.Zero) throw new UnsupportedSignatureException("A real completed native initializer is required.");
            return BeginHandoffCore(initializer);
        }
        internal LogicalHandoff BeginInitializationHandoff(NativePlaybackStockSourceHub.CompletedInitializationReceipt initializer)
        {
            ArgumentNullException.ThrowIfNull(initializer);
            lock (_owner._gate) { Registered(); return BeginHandoffCore(_source.CompletedInitializerIdentity(initializer), initializer); }
        }
        internal LogicalHandoff BeginCrossRunInitializationHandoff(NativePlaybackStockSourceHub.CompletedInitializationReceipt initializer)
        {
            ArgumentNullException.ThrowIfNull(initializer);
            lock (_owner._gate) { Registered(); return BeginHandoffCore(_source.CompletedInitializerIdentity(initializer), initializer, true); }
        }
        private LogicalHandoff BeginHandoffCore(IntPtr initializer,
            NativePlaybackStockSourceHub.CompletedInitializationReceipt? completed = null, bool crossRun = false)
        {
            lock (_owner._gate)
            {
                Registered();
                if (!SupportsCrossRunHandoff || !_owner._logicalBound || _receipt == IntPtr.Zero || _owner._published is not { } exact)
                    throw new UnsupportedSignatureException("Only this previously armed Fresh Clock owns its retained logical predecessor.");
                if (_owner._logicalHandoff is { } prior && prior.Read().Phase != 5)
                    throw new UnsupportedSignatureException("The preceding logical transfer is still pending.");
                IntPtr pointer;
                var status = initializer == IntPtr.Zero
                    ? _beginHandoff(_receipt, checked((ulong)exact.Revision), out pointer)
                    : (crossRun ? _beginCrossRunInitialization : _beginInitialization)(_receipt, checked((ulong)exact.Revision), initializer, out pointer);
                if (status != 0 || pointer == IntPtr.Zero)
                    throw new UnsupportedSignatureException($"The actual Fresh predecessor handoff was rejected: {status}.");
                // Native commit ended this publisher, even if subsequent
                // receipt readback fails. Old cleanup cannot touch a successor.
                _transferred = true; _closed = true;
                var handoff = completed is not null ? LogicalHandoff.IssueCompletedInitialization(_owner, pointer, exact, completed, crossRun)
                    : initializer == IntPtr.Zero ? LogicalHandoff.Issue(_owner, pointer, exact)
                    : LogicalHandoff.IssueInitialization(_owner, pointer, exact);
                _owner._handoffs.Add(handoff); _owner._logicalHandoff = handoff;
                completed?.BindHandoff(handoff);
                var info = handoff.Read(1);
                if (info.OldEpoch != _epoch)
                    throw new UnsupportedSignatureException("The logical predecessor did not retain its original Fresh source epoch.");
                return handoff;
            }
        }
        internal void Cancel()
        {
            lock (_owner._gate)
            {
                Registered();
                if (_closed) return;
                if (_armCommitted) throw new UnsupportedSignatureException("An armed Fresh Clock requires its exact conditional withdrawal.");
                if (_receipt != IntPtr.Zero)
                {
                    var status = _cancel(_receipt);
                    if (status != 0) throw new UnsupportedSignatureException($"Native Fresh Clock cancel rejected: {status}.");
                }
                _closed = true;
            }
        }
    }
}
