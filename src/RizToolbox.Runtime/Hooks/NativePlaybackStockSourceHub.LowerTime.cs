using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConfigureLowerTimeNative(IntPtr owner, uint wrapperSlot, uint lowerSlot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadAudioInterruptionNative(IntPtr owner, IntPtr enumerator, ulong yieldSequence,
        ref AudioInterruptionInformation information);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint RetryAudioInterruptedInitializationNative(IntPtr logicalGate, IntPtr priorInitializer,
        IntPtr freshInitializer, uint action, IntPtr request);

    private readonly LowerTimeNativeApi _lowerTime;
    private bool _lowerTimeConfigured;
    private NativePlaybackStockSourceBridge? _lowerTimeWrapper, _lowerTimeLower;

    // All required exports are resolved together before native owner creation.
    // Retry stays native-to-native: no managed POD can replace its joint request.
    private sealed class LowerTimeNativeApi
    {
        internal readonly ConfigureLowerTimeNative Configure;
        internal readonly ReadAudioInterruptionNative Read;
        internal readonly RetryAudioInterruptedInitializationNative Retry;
        internal readonly uint LowerTimeVersion, AudioInterruptionVersion;

        internal LowerTimeNativeApi(NativePlaybackStockSourceHub hub)
        {
            VerifyAudioInterruptionLayout();
            var lowerVersion = hub.Export<VersionNative>("RtPlaybackLowerTimeVersion");
            var audioVersion = hub.Export<VersionNative>("RtPlaybackAudioInterruptionVersion");
            Configure = hub.Export<ConfigureLowerTimeNative>("RtPlaybackSourceConfigureLowerTime");
            Read = hub.Export<ReadAudioInterruptionNative>("RtPlaybackSourceReadAudioInterruption");
            Retry = hub.Export<RetryAudioInterruptedInitializationNative>("RtPlaybackSourceRetryAudioInterruptedInitialization");
            LowerTimeVersion = lowerVersion(); AudioInterruptionVersion = audioVersion();
            if (LowerTimeVersion != 1 || AudioInterruptionVersion != 1)
                throw new UnsupportedSignatureException("The private lower-time and audio-interruption helpers require V1.");
        }
    }

    // Only the group installer supplies its actual adopted, configured channels.
    internal void ConfigureLowerTime(NativePlaybackStockSourceBridge wrapper, NativePlaybackStockSourceBridge lower)
    {
        VerifyThread(); _image.Verify();
        lock (_gate)
        {
            if (_armAttempted || _withdrawn || _fault is not null || _lowerTimeConfigured
                || !ReferenceEquals(wrapper.Hub, this) || !ReferenceEquals(lower.Hub, this)
                || !_channels.TryGetValue(wrapper.Slot, out var actualWrapper) || !ReferenceEquals(actualWrapper, wrapper)
                || !_channels.TryGetValue(lower.Slot, out var actualLower) || !ReferenceEquals(actualLower, lower)
                || wrapper.Slot == lower.Slot || wrapper.Kind != NativePlaybackSourceKind.Single1
                || wrapper.Role != NativePlaybackSourceRole.SetTime || lower.Kind != NativePlaybackSourceKind.IcallSingle1
                || lower.Role != NativePlaybackSourceRole.LowerSetTime || !wrapper.IsConfigured || !lower.IsConfigured
                || wrapper.TargetAddress == lower.TargetAddress || wrapper.OriginalAddress == lower.OriginalAddress)
                throw new UnsupportedSignatureException("Lower-time binding requires this dormant owner's two actual configured channels.");
            VerifyChannel(wrapper); VerifyChannel(lower);
            if (_lowerTime.Configure(_owner, wrapper.Slot, lower.Slot) != 1)
                throw new UnsupportedSignatureException("The native lower-time helper rejected its exact configured slot pair.");
            _lowerTimeWrapper = wrapper;
            _lowerTimeLower = lower;
            _lowerTimeConfigured = true;
        }
    }

    // Current and receipt readers authenticate the native frame. This structural
    // check binds it to the two channels accepted by the native configuration.
    private bool IsConfiguredLowerTimeFrame(NativePlaybackSourceFrame frame)
    {
        lock (_gate)
        {
            if (!_lowerTimeConfigured || _lowerTimeWrapper is not { IsConfigured: true } wrapper
                || _lowerTimeLower is not { IsConfigured: true } lower)
                return false;
            return ReferenceEquals(wrapper.Hub, this) && ReferenceEquals(lower.Hub, this)
                && wrapper.Role == NativePlaybackSourceRole.SetTime && wrapper.Kind == NativePlaybackSourceKind.Single1
                && lower.Role == NativePlaybackSourceRole.LowerSetTime && lower.Kind == NativePlaybackSourceKind.IcallSingle1
                && wrapper.Slot != lower.Slot
                && _channels.TryGetValue(wrapper.Slot, out var actualWrapper) && ReferenceEquals(actualWrapper, wrapper)
                && _channels.TryGetValue(lower.Slot, out var actualLower) && ReferenceEquals(actualLower, lower)
                && frame.Owner == _owner && frame.OwnerEpoch == Epoch
                && (frame.Slot == wrapper.Slot && frame.Role == wrapper.Role && frame.Kind == wrapper.Kind
                    || frame.Slot == lower.Slot && frame.Role == lower.Role && frame.Kind == lower.Kind
                        && frame.IncomingMethod == IntPtr.Zero);
        }
    }
}
