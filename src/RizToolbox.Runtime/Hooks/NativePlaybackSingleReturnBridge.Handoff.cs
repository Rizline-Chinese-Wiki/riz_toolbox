using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackSingleReturnBridge
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct HandoffInformation
    {
        internal uint Version, Size, Phase, Reserved;
        internal IntPtr Receipt;
        internal ulong OldEpoch, NewEpoch, OldRevision, InitialRevision, CurrentRevision;
        internal ulong GetTimeInFlight, SourceInFlight, SourceWriter, SourceGeneration, SourceScheduling;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BindLogicalNative(uint slot, IntPtr library, IntPtr owner);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BeginLogicalNative(uint slot, ulong revision, out IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BeginInitializationNative(uint slot, ulong revision, IntPtr initializer, out IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint VerifyInitializationCaptureNative(IntPtr receipt, IntPtr sourceOwner, ulong player, ulong source, ulong clip);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint LogicalActionNative(IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint PrepareTransferredNative(IntPtr receipt, ref NativePlaybackSingleReturnNativePolicy policy);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadLogicalNative(IntPtr receipt, ref HandoffInformation info);
    private readonly HashSet<LogicalHandoff> _handoffs = new(ReferenceEqualityComparer.Instance);
    private LogicalHandoff? _logicalHandoff;
    private bool _logicalBound;

    internal void BindLogicalSource(NativePlaybackStockSourceHub hub)
    {
        lock (_gate)
        {
            if (_fault is null && _logicalBound && _freshClock is { HasLogicalBinding: true }
                && _freshSourceCapture?.OwnsLogicalHub(hub) == true) return;
            if (_fault is not null || _logicalBound || Marshal.SizeOf<HandoffInformation>() != 104)
                throw new UnsupportedSignatureException("The actual logical source binding is unavailable or already issued.");
            if (Export<BindLogicalNative>("RizSingleReturnPrototype_BindLogicalSource")(_slot, hub.NativeLibraryHandle, hub.NativeOwner) != 0)
                throw new UnsupportedSignatureException("The native registered source owner did not bind the joint logical gate.");
            _logicalBound = true;
        }
    }
    internal LogicalHandoff BeginLogicalHandoff(NativePlaybackSingleReturnAuthority exact)
    {
        lock (_gate)
        {
            if (_freshClock is { HasTransferred: false })
                throw new UnsupportedSignatureException("The Fresh predecessor requires its exact typed Clock handoff entry.");
            if (_fault is not null || !_logicalBound || !ReferenceEquals(_published, exact))
                throw new UnsupportedSignatureException("A handoff requires this physical owner's exact retained publication object.");
            if (_logicalHandoff is { } pending && pending.Read().Phase != 5)
                throw new UnsupportedSignatureException("The prior logical transfer is still pending.");
            var result = Export<BeginLogicalNative>("RizSingleReturnPrototype_BeginLogicalHandoff")(_slot, (ulong)exact.Revision, out var native);
            if (result != 0 || native == IntPtr.Zero)
                throw new UnsupportedSignatureException($"Native exact logical withdrawal rejected: {result}.");
            var receipt = LogicalHandoff.Issue(this, native, exact);
            _handoffs.Add(receipt); _logicalHandoff = receipt; receipt.Read(1); return receipt;
        }
    }
    internal LogicalHandoff BeginInitializationHandoff(NativePlaybackSingleReturnAuthority exact, IntPtr initializer)
    {
        lock (_gate)
        {
            if (_freshClock is { HasTransferred: false })
                throw new UnsupportedSignatureException("The Fresh predecessor requires its exact typed initializer handoff entry.");
            if (_fault is not null || !_logicalBound || !ReferenceEquals(_published, exact) || initializer == IntPtr.Zero)
                throw new UnsupportedSignatureException("Initialization handoff requires this registered physical publication and a retained actual initializer.");
            if (_logicalHandoff is { } prior && prior.Read().Phase != 5)
                throw new UnsupportedSignatureException("Another registered transfer is still pending.");
            var result = Export<BeginInitializationNative>("RizSingleReturnPrototype_BeginInitializationHandoff")(_slot, (ulong)exact.Revision, initializer, out var pointer);
            if (result != 0 || pointer == IntPtr.Zero)
                throw new UnsupportedSignatureException($"Actual native initializer handoff rejected: {result}.");
            var handoff = LogicalHandoff.IssueInitialization(this, pointer, exact);
            _handoffs.Add(handoff); _logicalHandoff = handoff; handoff.Read(1); return handoff;
        }
    }
    internal LogicalHandoff BeginCrossRunInitializationHandoff(NativePlaybackSingleReturnAuthority exact,
        NativePlaybackStockSourceHub.CompletedInitializationReceipt initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        lock (_gate)
        {
            if (_freshClock is { HasTransferred: false } || _freshSourceCapture is null
                || _fault is not null || !_logicalBound || !ReferenceEquals(_published, exact))
                throw new UnsupportedSignatureException("Cross-run initialization requires this actual registered predecessor and its original physical source hub.");
            if (_logicalHandoff is { } prior && prior.Read().Phase != 5)
                throw new UnsupportedSignatureException("Another registered transfer is still pending.");
            var identity = _freshSourceCapture.CompletedInitializerIdentity(initializer);
            var result = Export<BeginInitializationNative>("RizSingleReturnPrototype_BeginCrossRunInitializationHandoff")(
                _slot, checked((ulong)exact.Revision), identity, out var pointer);
            if (result != 0 || pointer == IntPtr.Zero)
                throw new UnsupportedSignatureException($"Actual cross-run initializer handoff rejected: {result}.");
            var handoff = LogicalHandoff.IssueCompletedInitialization(this, pointer, exact, initializer, true);
            _handoffs.Add(handoff); _logicalHandoff = handoff;
            initializer.BindHandoff(handoff); handoff.Read(1); return handoff;
        }
    }
    internal sealed class LogicalHandoff
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly NativePlaybackSingleReturnBridge _owner;
        private readonly IntPtr _native;
        private readonly NativePlaybackSingleReturnAuthority _old;
        private NativePlaybackSingleReturnAuthority? _initial;
        private ulong _oldEpoch, _newEpoch;
        private bool _initialization;
        private bool _crossRunInitialization;
        private NativePlaybackStockSourceHub.CompletedInitializationReceipt? _completedInitializer;
        private LogicalHandoff(NativePlaybackSingleReturnBridge owner, IntPtr native, NativePlaybackSingleReturnAuthority old)
            => (_owner, _native, _old) = (owner, native, old);
        internal static LogicalHandoff Issue(NativePlaybackSingleReturnBridge owner, IntPtr native, NativePlaybackSingleReturnAuthority old)
            => new(owner, native, old);
        // This local origin tag never grants permission: the native record
        // independently checks initialization, exact source owner and graph.
        internal static LogicalHandoff IssueInitialization(NativePlaybackSingleReturnBridge owner, IntPtr native, NativePlaybackSingleReturnAuthority old)
            => new(owner, native, old) { _initialization = true };
        internal static LogicalHandoff IssueCompletedInitialization(NativePlaybackSingleReturnBridge owner, IntPtr native,
            NativePlaybackSingleReturnAuthority old, NativePlaybackStockSourceHub.CompletedInitializationReceipt initializer, bool crossRun)
            => new(owner, native, old) { _initialization = true, _crossRunInitialization = crossRun, _completedInitializer = initializer };
        private LogicalHandoff IssueInitializationRetry(IntPtr native)
            => new(_owner, native, _old) { _initialization = true, _crossRunInitialization = _crossRunInitialization };
        internal bool IsCrossRunInitialization { get { Registered(); return _crossRunInitialization; } }
        internal void VerifyCompletedInitializerOwner(NativePlaybackStockSourceHub.CompletedInitializationReceipt initializer)
        {
            Registered();
            if (!_initialization || !ReferenceEquals(_completedInitializer, initializer))
                throw new UnsupportedSignatureException("This logical handoff did not consume the exact registered completed initializer.");
        }
        internal void VerifyInitializationCapture(NativePlaybackStockSourceHub source, long player, long audioSource, long clip)
        {
            lock (_owner._gate)
            {
                if (!_initialization) throw new UnsupportedSignatureException("An ordinary cross-run transfer cannot grant initialization capture.");
                Read(2);
                var result = _owner.Export<VerifyInitializationCaptureNative>("RizSingleReturnPrototype_VerifyInitializationCapture")(
                    _native, source.NativeOwner, checked((ulong)player), checked((ulong)audioSource), checked((ulong)clip));
                if (result != 0) throw new UnsupportedSignatureException($"The actual initializer/graph no longer grants conditional capture: {result}.");
            }
        }
        private void Registered(bool current = true)
        {
            if (_thread != Environment.CurrentManagedThreadId || !_owner._handoffs.Contains(this)
                || current && !ReferenceEquals(_owner._logicalHandoff, this) || _owner._fault is not null)
                throw new UnsupportedSignatureException("A copied, stale, or physically faulted logical receipt has no transfer authority.");
        }
        internal HandoffInformation Read(uint phase = 0)
        {
            lock (_owner._gate)
            {
                Registered(); var info = new HandoffInformation { Version = 1, Size = 104 };
                if (_owner.Export<ReadLogicalNative>("RizSingleReturnPrototype_ReadLogicalHandoff")(_native, ref info) != 0
                    || info.Version != 1 || info.Size != 104 || info.Receipt != _native || info.Reserved != 0
                    || info.OldEpoch == 0 || _oldEpoch != 0 && info.OldEpoch != _oldEpoch || info.OldRevision != (ulong)_old.Revision
                    || phase != 0 && info.Phase != phase || info.Phase is < 1 or > 5
                    || info.Phase >= 2 && (info.NewEpoch <= info.OldEpoch || info.InitialRevision != info.OldRevision + 1)
                    || _newEpoch != 0 && info.NewEpoch != _newEpoch)
                    throw new UnsupportedSignatureException("The actual native logical receipt readback changed.");
                _oldEpoch = info.OldEpoch; if (info.NewEpoch != 0) _newEpoch = info.NewEpoch;
                return info;
            }
        }
        internal bool TryAdmit()
        {
            lock (_owner._gate)
            {
                Read(1);
                var result = _owner.Export<LogicalActionNative>("RizSingleReturnPrototype_AdmitLogicalHandoff")(_native);
                if (result == 8) return false;
                if (result != 0) throw new UnsupportedSignatureException($"Native joint GetTime/source transfer rejected: {result}.");
                Read(2); return true;
            }
        }
        internal LogicalHandoff RetryResourceInitialization(IntPtr freshInitializer)
        {
            lock (_owner._gate)
            {
                var prior = Read();
                if (!_initialization || prior.Phase is not (1 or 2) || freshInitializer == IntPtr.Zero
                    || !ReferenceEquals(_owner._published, _old))
                    throw new UnsupportedSignatureException("A resource retry requires the current pending initialization handoff and its exact predecessor policy.");
                var result = _owner.Export<RetryInitializationNative>("RizSingleReturnPrototype_RetryResourceInitializationHandoff")(
                    _native, freshInitializer, out var pointer);
                if (result != 0 || pointer == IntPtr.Zero || pointer == _native)
                    throw new UnsupportedSignatureException($"Native exact handoff resource retry rejected: {result}.");
                var fresh = IssueInitializationRetry(pointer);
                _owner._handoffs.Add(fresh); _owner._logicalHandoff = fresh;
                // The fresh read owns its newly issued epoch/identity. Neither
                // graph nor epoch is rewritten on the predecessor object.
                fresh.Read(1); return fresh;
            }
        }
        internal LogicalHandoff? RetryBackingInitialization(IntPtr freshInitializer)
        {
            lock (_owner._gate)
            {
                var prior = Read();
                if (!_initialization || prior.Phase is not (1 or 2) || freshInitializer == IntPtr.Zero
                    || !ReferenceEquals(_owner._published, _old))
                    throw new UnsupportedSignatureException("A backing retry requires the retained pending initialization handoff and its exact predecessor policy.");
                var result = _owner.Export<RetryInitializationNative>("RizSingleReturnPrototype_RetryBackingInitializationHandoff")(
                    _native, freshInitializer, out var pointer);
                if (result == 8) return null;
                if (result != 0 || pointer == IntPtr.Zero || pointer == _native)
                    throw new UnsupportedSignatureException($"Native exact handoff backing retry rejected: {result}.");
                var fresh = IssueInitializationRetry(pointer);
                _owner._handoffs.Add(fresh); _owner._logicalHandoff = fresh;
                fresh.Read(1); return fresh;
            }
        }
        internal LogicalHandoff? RetryLowerPitchInitialization(IntPtr freshInitializer)
        {
            lock (_owner._gate)
            {
                var prior = Read();
                if (!_initialization || prior.Phase is not (1 or 2) || freshInitializer == IntPtr.Zero
                    || !ReferenceEquals(_owner._published, _old))
                    throw new UnsupportedSignatureException("A lower-pitch retry requires the retained pending handoff and a distinct actual completed initializer.");
                var result = _owner.Export<RetryInitializationNative>("RizSingleReturnPrototype_RetryLowerPitchInitializationHandoff")(
                    _native, freshInitializer, out var pointer);
                if (result == 8) return null;
                if (result != 0 || pointer == IntPtr.Zero || pointer == _native)
                    throw new UnsupportedSignatureException($"Native exact handoff lower-pitch retry rejected: {result}.");
                var fresh = IssueInitializationRetry(pointer);
                _owner._handoffs.Add(fresh); _owner._logicalHandoff = fresh;
                fresh.Read(1);
                return fresh;
            }
        }
        internal LogicalHandoff? RetryAudioInterruptedInitialization(IntPtr freshInitializer)
        {
            lock (_owner._gate)
            {
                var prior = Read();
                if (!_initialization || prior.Phase is not (1 or 2) || freshInitializer == IntPtr.Zero
                    || !ReferenceEquals(_owner._published, _old))
                    throw new UnsupportedSignatureException("An audio retry requires the retained pending initialization handoff and its exact predecessor policy.");
                var result = _owner._retryAudioInterruptedInitializationHandoff(_native, freshInitializer, out var pointer);
                if (result == 8) return null;
                if (result != 0 || pointer == IntPtr.Zero || pointer == _native)
                    throw new UnsupportedSignatureException($"Native exact handoff audio-interruption retry rejected: {result}.");
                var fresh = IssueInitializationRetry(pointer);
                _owner._handoffs.Add(fresh); _owner._logicalHandoff = fresh;
                fresh.Read(1); return fresh;
            }
        }
        internal long AdmittedRevision
        {
            get { var info = Read(); if (info.Phase is not (2 or 3 or 4) || info.InitialRevision > long.MaxValue) throw new UnsupportedSignatureException("No admitted baseline belongs to this receipt."); return (long)info.InitialRevision; }
        }
        internal bool TryOpenSource()
        {
            lock (_owner._gate)
            {
                Read(2);
                var result = _owner.Export<LogicalActionNative>("RizSingleReturnPrototype_OpenTransferredSource")(_native);
                if (result == 8) return false;
                if (result != 0) throw new UnsupportedSignatureException($"Native registered source-open rejected: {result}.");
                Read(3); return true;
            }
        }
        internal void Prepare(NativePlaybackSingleReturnAuthority initial)
        {
            lock (_owner._gate)
            {
                Read(3);
                if (_initial is not null || !ReferenceEquals(_owner._published, _old) || !initial.IsValid || initial.Revision != AdmittedRevision)
                    throw new UnsupportedSignatureException("A transferred policy requires the retained predecessor and its native admitted revision.");
                var value = initial.ToNative(_owner._readerPointer, _owner._owner);
                var result = _owner.Export<PrepareTransferredNative>("RizSingleReturnPrototype_PrepareTransferredClock")(_native, ref value);
                if (result != 0) throw new UnsupportedSignatureException($"Native transferred dormant publication rejected: {result}.");
                _initial = initial; _owner._published = initial;
                Read(4);
                if (_owner.ReadCurrentCore().Active) throw new UnsupportedSignatureException("Transferred initial policy activated before its final Clock.");
            }
        }
        internal bool TryArm()
        {
            lock (_owner._gate)
            {
                Read(4);
                if (!ReferenceEquals(_owner._published, _initial)) throw new UnsupportedSignatureException("The transferred initial publication changed.");
                var result = _owner.Export<LogicalActionNative>("RizSingleReturnPrototype_ArmTransferredClock")(_native);
                if (result == 8) return false;
                if (result != 0) throw new UnsupportedSignatureException($"Native joint logical Arm rejected: {result}.");
                Read(5);
                if (!_owner.ReadCurrentCore().Active) throw new UnsupportedSignatureException("Joint logical Arm did not read back active.");
                return true;
            }
        }
    }
}
