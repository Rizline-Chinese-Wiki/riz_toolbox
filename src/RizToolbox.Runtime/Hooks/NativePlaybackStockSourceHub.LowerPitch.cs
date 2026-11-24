using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConfigureLowerPitchNative(IntPtr owner, uint wrapperSlot, uint lowerSlot);
    private bool _lowerPitchConfigured;

    internal void ConfigureLowerPitch(NativePlaybackStockSourceBridge wrapper, NativePlaybackStockSourceBridge lower)
    {
        VerifyThread(); _image.Verify();
        lock (_gate)
        {
            if (_armAttempted || _withdrawn || _fault is not null || _lowerPitchConfigured
                || !ReferenceEquals(wrapper.Hub, this) || !ReferenceEquals(lower.Hub, this)
                || !_channels.TryGetValue(wrapper.Slot, out var actualWrapper) || !ReferenceEquals(actualWrapper, wrapper)
                || !_channels.TryGetValue(lower.Slot, out var actualLower) || !ReferenceEquals(actualLower, lower)
                || wrapper.Slot == lower.Slot || wrapper.Kind != NativePlaybackSourceKind.Single1
                || wrapper.Role != NativePlaybackSourceRole.SetPitch || lower.Kind != NativePlaybackSourceKind.IcallSingle1
                || lower.Role != NativePlaybackSourceRole.LowerSetPitch
                || Export<VersionNative>("RtPlaybackLowerPitchVersion")() != 1)
                throw new UnsupportedSignatureException("Only this dormant owner's actual wrapper/lower channels can configure the fixed lower-pitch extension.");
            VerifyChannel(wrapper); VerifyChannel(lower);
            if (Export<ConfigureLowerPitchNative>("RtPlaybackSourceConfigureLowerPitch")(_owner, wrapper.Slot, lower.Slot) != 1)
                throw new UnsupportedSignatureException("The native lower-pitch extension rejected the exact configured slot pair.");
            _lowerPitchConfigured = true;
        }
    }
}
