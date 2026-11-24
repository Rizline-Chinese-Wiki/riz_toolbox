using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConfigureEnginePitchNative(IntPtr owner, uint lowerSlot, uint engineSlot, IntPtr exactReturn);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadEnginePitchAssociationNative(IntPtr owner, ulong sequence, ulong token, ref EnginePitchAssociationInformation info);
    private bool _enginePitchConfigured;

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct EnginePitchAssociationInformation
    {
        internal uint Version, Size, Kind, State;
        internal ulong Owner, OwnerEpoch, Sequence, Token, Wrapper, WrapperClass, NativeSelf, Receipt, Ordinal;
        internal int InstanceIdOffset, SourceId;
    }

    internal void ConfigureEnginePitch(NativePlaybackStockSourceBridge lower, NativePlaybackStockSourceBridge native,
        NativePlaybackLowerPitchTarget.EnginePitchPreflight target)
    {
        VerifyThread(); _image.Verify(); target.VerifySupportingEvidence();
        lock (_gate)
        {
            if (_armAttempted || _withdrawn || _fault is not null || !_lowerPitchConfigured || _enginePitchConfigured
                || !ReferenceEquals(lower.Hub, this) || !ReferenceEquals(native.Hub, this)
                || !_channels.TryGetValue(lower.Slot, out var actualLower) || !ReferenceEquals(actualLower, lower)
                || !_channels.TryGetValue(native.Slot, out var actualNative) || !ReferenceEquals(actualNative, native)
                || lower.Slot == native.Slot || lower.Role != NativePlaybackSourceRole.LowerSetPitch
                || native.Role != NativePlaybackSourceRole.EngineSetPitch || lower.Kind != NativePlaybackSourceKind.IcallSingle1
                || native.Kind != NativePlaybackSourceKind.IcallSingle1 || lower.TargetAddress != target.Parent.Address
                || native.TargetAddress != target.Address || !lower.IsConfigured || !native.IsConfigured
                || Marshal.SizeOf<EnginePitchAssociationInformation>() != 96
                || Export<VersionNative>("RtPlaybackEnginePitchVersion")() != 1)
                throw new UnsupportedSignatureException("Only the fixed group's two configured native channels can bind engine pitch before Arm.");
            VerifyChannel(lower); VerifyChannel(native);
            if (Export<ConfigureEnginePitchNative>("RtPlaybackSourceConfigureEnginePitch")(
                    _owner, lower.Slot, native.Slot, target.OriginalLowerReturn) != 1)
                throw new UnsupportedSignatureException("The native engine extension rejected its exact configured members and fixed CALL return.");
            _enginePitchConfigured = true;
        }
    }

    // The wrapper comes from native's current private watch/initializer record.
    // A native receiver, copied frame or caller-supplied pair cannot create it.
    private EnginePitchAssociationInformation ReadEnginePitchAssociationActual(NativePlaybackStockSourceBridge channel,
        NativePlaybackSourceFrame expected)
    {
        VerifyThread();
        if (!_enginePitchConfigured || !ReferenceEquals(channel.Hub, this) || channel.Role != NativePlaybackSourceRole.EngineSetPitch
            || channel.Kind != NativePlaybackSourceKind.IcallSingle1
            || !_channels.TryGetValue(channel.Slot, out var actual) || !ReferenceEquals(channel, actual))
            throw new UnsupportedSignatureException("This engine association has no exact configured native channel.");
        var frame = ReadOperationCurrentActual(channel, expected, requireBefore: false, allowWithdrawnCompletion: false);
        var info = new EnginePitchAssociationInformation { Version = 1, Size = 96 };
        if (Export<ReadEnginePitchAssociationNative>("RtPlaybackSourceReadEnginePitchAssociation")(
                _owner, frame.Sequence, frame.Token, ref info) != 1
            || info.Version != 1 || info.Size != 96 || info.Kind is < 1 or > 3 || info.State != 1
            || info.Owner != unchecked((ulong)_owner.ToInt64()) || info.OwnerEpoch != Epoch
            || info.Sequence != frame.Sequence || info.Token != frame.Token
            || info.NativeSelf != unchecked((ulong)frame.Self.ToInt64()) || info.Wrapper == 0 || info.WrapperClass == 0
            || info.Receipt == 0 || info.Ordinal == 0 || info.SourceId == 0 || info.InstanceIdOffset is < 0 or > 1024)
            throw new UnsupportedSignatureException("The native engine call has no exact current private rooted wrapper association.");
        return info;
    }

    internal sealed class EnginePitchAssociation
    {
        private readonly NativePlaybackStockSourceHub _hub;
        private readonly NativePlaybackStockSourceBridge _channel;
        private readonly NativePlaybackSourceFrame _origin;
        private readonly EnginePitchAssociationInformation _issued;
        private EnginePitchAssociation(NativePlaybackStockSourceHub hub, NativePlaybackStockSourceBridge channel,
            NativePlaybackSourceFrame origin, EnginePitchAssociationInformation issued)
            => (_hub, _channel, _origin, _issued) = (hub, channel, origin, issued);
        internal IntPtr Wrapper => unchecked((IntPtr)(long)_issued.Wrapper);
        internal static EnginePitchAssociation CaptureActual(NativePlaybackStockSourceBridge channel, NativePlaybackSourceFrame frame)
        {
            var hub = channel.Hub;
            return new(hub, channel, frame, hub.ReadEnginePitchAssociationActual(channel, frame));
        }
        internal void VerifyCurrent()
        {
            var info = _hub.ReadEnginePitchAssociationActual(_channel, _origin);
            if (info.Kind != _issued.Kind || info.Wrapper != _issued.Wrapper || info.WrapperClass != _issued.WrapperClass
                || info.NativeSelf != _issued.NativeSelf || info.Receipt != _issued.Receipt || info.Ordinal != _issued.Ordinal
                || info.InstanceIdOffset != _issued.InstanceIdOffset || info.SourceId != _issued.SourceId)
                throw new UnsupportedSignatureException("The actual engine scope's retained wrapper association changed.");
        }
    }
}
