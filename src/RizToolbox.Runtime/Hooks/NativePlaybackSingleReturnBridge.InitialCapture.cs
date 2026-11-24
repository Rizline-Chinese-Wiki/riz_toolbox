using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackSingleReturnBridge
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct InitialCaptureInformation
    {
        internal uint Version, Size, Phase, Reserved;
        internal IntPtr Receipt;
        internal ulong Epoch, GetTimeInFlight, SourceInFlight, SourceOwner, PhysicalEpoch;
        internal ulong Writer, SourceFence, Scheduling, Initializer, Player, Source, Clip;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InitialCaptureVersionNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BindInitialCaptureNative(uint slot, IntPtr library, IntPtr owner);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BeginInitialCaptureNative(uint slot, IntPtr initializer, out IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ActInitialCaptureNative(IntPtr receipt, uint action, IntPtr owner, ulong player, ulong source, ulong clip);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadInitialCaptureNative(IntPtr receipt, ref InitialCaptureInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint RetryInitializationNative(IntPtr priorReceipt, IntPtr freshInitializer, out IntPtr newReceipt);
    private NativePlaybackStockSourceHub? _initialCaptureSource;
    private InitialCapture? _initialCapture;

    internal void BindInitialCaptureSource(NativePlaybackStockSourceHub source)
    {
        lock (_gate)
        {
            if (_fault is not null || _published is not null || _bootstrap is not null || _logicalBound
                || _initialCaptureSource is not null || Marshal.SizeOf<InitialCaptureInformation>() != 120
                || Export<InitialCaptureVersionNative>("RizSingleReturnPrototype_InitialCaptureVersion")() != 1
                || Export<BindInitialCaptureNative>("RizSingleReturnPrototype_BindInitialCaptureSource")(_slot, source.NativeLibraryHandle, source.NativeOwner) != 0)
                throw new UnsupportedSignatureException("The fresh physical owners could not bind private first-capture V1 without a predecessor publication.");
            _initialCaptureSource = source;
        }
    }
    internal InitialCapture BeginInitialCapture(IntPtr initializer)
    {
        lock (_gate)
        {
            if (_fault is not null || _initialCaptureSource is null || _initialCapture is not null || _published is not null
                || _bootstrap is not null || _logicalHandoff is not null || _logicalBound || initializer == IntPtr.Zero)
                throw new UnsupportedSignatureException("Only a fresh registered joint owner can consume the actual first initializer.");
            var result = Export<BeginInitialCaptureNative>("RizSingleReturnPrototype_BeginInitialCapture")(_slot, initializer, out var pointer);
            if (result != 0 || pointer == IntPtr.Zero)
                throw new UnsupportedSignatureException($"Actual native first capture rejected: {result}.");
            var receipt = InitialCapture.Issue(this, _initialCaptureSource, pointer);
            _initialCapture = receipt; receipt.Read(1); return receipt;
        }
    }
    // A distinct type deliberately has no predecessor authority/revision, and
    // does not publish a policy. The later real Pause uses ordinary Dormant.
    internal sealed class InitialCapture
    {
        private readonly NativePlaybackSingleReturnBridge _owner;
        private readonly NativePlaybackStockSourceHub _source;
        private readonly IntPtr _native;
        private InitialCaptureInformation? _identity;
        private ulong _epoch;
        private InitialCapture(NativePlaybackSingleReturnBridge owner, NativePlaybackStockSourceHub source, IntPtr native)
            => (_owner, _source, _native) = (owner, source, native);
        internal static InitialCapture Issue(NativePlaybackSingleReturnBridge owner, NativePlaybackStockSourceHub source, IntPtr native)
            => new(owner, source, native);
        private void Registered()
        {
            if (!ReferenceEquals(_owner._initialCapture, this) || !ReferenceEquals(_owner._initialCaptureSource, _source) || _owner._fault is not null)
                throw new UnsupportedSignatureException("A copied, stale or faulted first-capture receipt has no registered owner.");
        }
        internal InitialCaptureInformation Read(uint phase = 0)
        {
            lock (_owner._gate)
            {
                Registered(); var info = new InitialCaptureInformation { Version = 1, Size = 120 };
                if (_owner.Export<ReadInitialCaptureNative>("RizSingleReturnPrototype_ReadInitialCapture")(_native, ref info) != 0
                    || info.Version != 1 || info.Size != 120 || info.Reserved != 0 || info.Receipt != _native
                    || info.Phase is < 1 or > 4 || phase != 0 && info.Phase != phase
                    || info.SourceOwner != unchecked((ulong)_source.NativeOwner.ToInt64()) || info.PhysicalEpoch != _source.Epoch
                    || info.Initializer == 0 || info.Player == 0 || info.Source == 0 || info.Clip == 0
                    || info.Phase == 1 && info.Epoch != 0 || info.Phase >= 2 && info.Epoch == 0
                    || _epoch != 0 && info.Epoch != _epoch
                    || _identity is { } previous && (info.SourceOwner != previous.SourceOwner || info.PhysicalEpoch != previous.PhysicalEpoch
                        || info.Initializer != previous.Initializer || info.Player != previous.Player || info.Source != previous.Source || info.Clip != previous.Clip))
                    throw new UnsupportedSignatureException("The actual native first-capture V1 owner, initialized graph or epoch changed.");
                _identity ??= info; if (info.Epoch != 0) _epoch = info.Epoch;
                return info;
            }
        }
        private uint Act(uint action, long player = 0, long source = 0, long clip = 0)
            => _owner.Export<ActInitialCaptureNative>("RizSingleReturnPrototype_ActInitialCapture")(_native, action,
                _source.NativeOwner, checked((ulong)player), checked((ulong)source), checked((ulong)clip));
        internal bool TryAdmit()
        {
            lock (_owner._gate)
            {
                Read(1); var result = Act(8);
                if (result == 8) return false;
                if (result != 0) throw new UnsupportedSignatureException($"Native first-capture joint drain rejected: {result}.");
                Read(2); return true;
            }
        }
        internal InitialCapture RetryResourceInitialization(IntPtr freshInitializer)
        {
            lock (_owner._gate)
            {
                var prior = Read();
                if (prior.Phase is not (1 or 2) || freshInitializer == IntPtr.Zero
                    || unchecked((ulong)freshInitializer.ToInt64()) == prior.Initializer)
                    throw new UnsupportedSignatureException("A resource retry requires a distinct actual completed initializer and a pending first-capture receipt.");
                var result = _owner.Export<RetryInitializationNative>("RizSingleReturnPrototype_RetryResourceInitialCapture")(
                    _native, freshInitializer, out var pointer);
                if (result != 0 || pointer == IntPtr.Zero || pointer == _native)
                    throw new UnsupportedSignatureException($"Native exact first-capture resource retry rejected: {result}.");
                // Replace the registered object only after native minted a new
                // opaque record. The prior object can never pass Registered.
                var fresh = Issue(_owner, _source, pointer);
                _owner._initialCapture = fresh;
                var current = fresh.Read(1);
                if (current.Initializer != unchecked((ulong)freshInitializer.ToInt64()))
                    throw new UnsupportedSignatureException("The new first-capture receipt does not bind the actual retry initializer.");
                return fresh;
            }
        }
        internal InitialCapture? RetryBackingInitialization(IntPtr freshInitializer)
        {
            lock (_owner._gate)
            {
                var prior = Read();
                if (prior.Phase is not (1 or 2) || freshInitializer == IntPtr.Zero
                    || unchecked((ulong)freshInitializer.ToInt64()) == prior.Initializer)
                    throw new UnsupportedSignatureException("A backing retry requires a distinct actual completed initializer and the retained pending first-capture receipt.");
                // Native independently authenticates the exact invalidated
                // watch badge. No managed bool/data grants this replacement.
                var result = _owner.Export<RetryInitializationNative>("RizSingleReturnPrototype_RetryBackingInitialCapture")(
                    _native, freshInitializer, out var pointer);
                if (result == 8) return null;
                if (result != 0 || pointer == IntPtr.Zero || pointer == _native)
                    throw new UnsupportedSignatureException($"Native exact first-capture backing retry rejected: {result}.");
                var fresh = Issue(_owner, _source, pointer);
                _owner._initialCapture = fresh;
                if (fresh.Read(1).Initializer != unchecked((ulong)freshInitializer.ToInt64()))
                    throw new UnsupportedSignatureException("The replacement first capture does not bind the actual backing-retry initializer.");
                return fresh;
            }
        }
        internal InitialCapture? RetryLowerPitchInitialization(IntPtr freshInitializer)
        {
            lock (_owner._gate)
            {
                var prior = Read();
                if (prior.Phase is not (1 or 2) || freshInitializer == IntPtr.Zero
                    || unchecked((ulong)freshInitializer.ToInt64()) == prior.Initializer)
                    throw new UnsupportedSignatureException("A lower-pitch retry requires the retained pending first capture and a distinct actual completed initializer.");
                var result = _owner.Export<RetryInitializationNative>("RizSingleReturnPrototype_RetryLowerPitchInitialCapture")(
                    _native, freshInitializer, out var pointer);
                if (result == 8) return null;
                if (result != 0 || pointer == IntPtr.Zero || pointer == _native)
                    throw new UnsupportedSignatureException($"Native exact first-capture lower-pitch retry rejected: {result}.");
                var fresh = Issue(_owner, _source, pointer);
                _owner._initialCapture = fresh;
                if (fresh.Read(1).Initializer != unchecked((ulong)freshInitializer.ToInt64()))
                    throw new UnsupportedSignatureException("The replacement first capture does not bind the actual lower-pitch retry initializer.");
                return fresh;
            }
        }
        internal InitialCapture? RetryAudioInterruptedInitialization(IntPtr freshInitializer)
        {
            lock (_owner._gate)
            {
                var prior = Read();
                if (prior.Phase is not (1 or 2) || freshInitializer == IntPtr.Zero
                    || unchecked((ulong)freshInitializer.ToInt64()) == prior.Initializer)
                    throw new UnsupportedSignatureException("An audio retry requires the retained pending first capture and a distinct actual initializer.");
                var result = _owner._retryAudioInterruptedInitialCapture(_native, freshInitializer, out var pointer);
                if (result == 8) return null;
                if (result != 0 || pointer == IntPtr.Zero || pointer == _native)
                    throw new UnsupportedSignatureException($"Native exact first-capture audio-interruption retry rejected: {result}.");
                var fresh = Issue(_owner, _source, pointer);
                _owner._initialCapture = fresh;
                if (fresh.Read(1).Initializer != unchecked((ulong)freshInitializer.ToInt64()))
                    throw new UnsupportedSignatureException("The replacement first capture does not bind the actual audio-retry initializer.");
                return fresh;
            }
        }
        internal void VerifyCapture(NativePlaybackStockSourceHub source, long player, long audioSource, long clip)
        {
            lock (_owner._gate)
            {
                Read(2);
                if (!ReferenceEquals(source, _source) || Act(9, player, audioSource, clip) != 0)
                    throw new UnsupportedSignatureException("Only the exact first initializer and initialized graph grant conditional capture.");
            }
        }
        internal bool TryOpenSource()
        {
            lock (_owner._gate)
            {
                Read(2); var result = Act(10);
                if (result == 8) return false;
                if (result != 0) throw new UnsupportedSignatureException($"Native first-capture source-open rejected: {result}.");
                Read(3); return true;
            }
        }
    }
}

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackOwner
    {
        internal void BindInitialCaptureSource(NativePlaybackStockSourceHub hub)
        { lock (_gate) { VerifyCurrent(_module, TargetAddress, _target.MethodInfo); _bridge!.BindInitialCaptureSource(hub); } }
        internal NativePlaybackSingleReturnBridge.InitialCapture BeginInitialCapture(IntPtr initializer)
        { lock (_gate) { VerifyCurrent(_module, TargetAddress, _target.MethodInfo); return _bridge!.BeginInitialCapture(initializer); } }
    }
    internal sealed partial class PlaybackSourceInstallGroup
    {
        internal void BindInitialCaptureSource() { VerifyCurrent(); _getTime!.BindInitialCaptureSource(_hub!); }
        internal NativePlaybackSingleReturnBridge.InitialCapture BeginInitialCapture(IntPtr initializer)
        { VerifyCurrent(); return _getTime!.BeginInitialCapture(initializer); }
        internal void VerifyInitialCapture(NativePlaybackSingleReturnBridge.InitialCapture receipt, long player, long source, long clip)
        { VerifyCurrent(); receipt.VerifyCapture(_hub!, player, source, clip); }
    }
}
