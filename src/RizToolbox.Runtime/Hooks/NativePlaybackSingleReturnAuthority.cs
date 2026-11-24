using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Data from one immutable playback-clock snapshot. Only the fixed
/// DSP/media affine policy is publishable; there is no arbitrary return callback.</summary>
internal sealed record NativePlaybackSingleReturnAuthority(NativePlaybackSingleReturnIdentity Identity, long Revision,
    NativePlaybackSingleReturnPhase Phase, double DspAnchorSeconds, double MediaAnchorSeconds, double Rate,
    double ClipLengthSeconds, IntPtr PermittedNullCaller)
{
    internal bool IsValid => Identity.IsValid && Revision > 0 && Enum.IsDefined(typeof(NativePlaybackSingleReturnPhase), Phase)
        && double.IsFinite(DspAnchorSeconds) && DspAnchorSeconds >= 0 && double.IsFinite(MediaAnchorSeconds)
        && MediaAnchorSeconds >= 0 && MediaAnchorSeconds <= ClipLengthSeconds && double.IsFinite(ClipLengthSeconds)
        && ClipLengthSeconds > 0 && ClipLengthSeconds <= float.MaxValue && double.IsFinite(Rate)
        && Rate > 0 && float.IsFinite((float)Rate) && (float)Rate > 0 && (double)(float)Rate == Rate;

    internal (ulong High, ulong Low) RunFingerprint()
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(Identity.RunId));
        return (BinaryPrimitives.ReadUInt64LittleEndian(digest), BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(8)));
    }
    internal NativePlaybackSingleReturnNativePolicy ToNative(IntPtr reader, IntPtr owner)
    {
        if (!IsValid) throw new ArgumentException("The controlled clock publication requires an exact identity, positive finite Single rate and finite media/DSP anchors.");
        var run = RunFingerprint();
        return new()
        {
            Version = 1, Size = 128, Self = Identity.Player, Method = Identity.VerifiedGetTimeMethod,
            RunHigh = run.High, RunLow = run.Low, ControllerGeneration = checked((ulong)Identity.ControllerGeneration), Generation = checked((ulong)Revision),
            Phase = Phase, DspAnchor = DspAnchorSeconds, MediaAnchor = MediaAnchorSeconds, Rate = Rate,
            StartDsp = DspAnchorSeconds, ClipLength = ClipLengthSeconds, ReadClock = reader, ClockOwner = owner,
            PermittedNullCaller = PermittedNullCaller
        };
    }
    internal bool MatchesNative(NativePlaybackSingleReturnNativePolicy value, IntPtr reader, IntPtr owner)
    {
        var expected = ToNative(reader, owner);
        return value.Version == expected.Version && value.Size == expected.Size && value.Self == expected.Self && value.Method == expected.Method
            && value.RunHigh == expected.RunHigh && value.RunLow == expected.RunLow && value.ControllerGeneration == expected.ControllerGeneration
            && value.Generation == expected.Generation && value.Phase == expected.Phase && value.Reserved == 0
            && BitConverter.DoubleToInt64Bits(value.DspAnchor) == BitConverter.DoubleToInt64Bits(expected.DspAnchor)
            && BitConverter.DoubleToInt64Bits(value.MediaAnchor) == BitConverter.DoubleToInt64Bits(expected.MediaAnchor)
            && BitConverter.DoubleToInt64Bits(value.Rate) == BitConverter.DoubleToInt64Bits(expected.Rate)
            && BitConverter.DoubleToInt64Bits(value.StartDsp) == BitConverter.DoubleToInt64Bits(expected.StartDsp)
            && BitConverter.DoubleToInt64Bits(value.ClipLength) == BitConverter.DoubleToInt64Bits(expected.ClipLength)
            && value.ReadClock == reader && value.ClockOwner == owner && value.PermittedNullCaller == expected.PermittedNullCaller;
    }
}
