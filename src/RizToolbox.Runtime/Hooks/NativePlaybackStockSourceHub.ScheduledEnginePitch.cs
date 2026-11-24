using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConfigureScheduledEnginePitchNative(IntPtr owner, uint scheduleSlot, uint engineSlot,
        IntPtr exactReturn, IntPtr reapplyBody, uint reapplyLength, IntPtr playCache, IntPtr nativePlayTarget);
    private bool _scheduledEnginePitchConfigured;

    internal void ConfigureScheduledEnginePitch(NativePlaybackStockSourceBridge scheduled,
        NativePlaybackStockSourceBridge engine, NativePlaybackLowerPitchTarget.ScheduledPitchPreflight profile)
    {
        VerifyThread(); _image.Verify(); profile.VerifySupportingEvidence();
        lock (_gate)
        {
            if (_armAttempted || _withdrawn || _fault is not null || !_enginePitchConfigured || _scheduledEnginePitchConfigured
                || !ReferenceEquals(scheduled.Hub, this) || !ReferenceEquals(engine.Hub, this)
                || !_channels.TryGetValue(scheduled.Slot, out var actualScheduled) || !ReferenceEquals(actualScheduled, scheduled)
                || !_channels.TryGetValue(engine.Slot, out var actualEngine) || !ReferenceEquals(actualEngine, engine)
                || scheduled.Slot == engine.Slot || scheduled.Role != NativePlaybackSourceRole.PlayScheduled
                || scheduled.Kind != NativePlaybackSourceKind.Double1 || engine.Role != NativePlaybackSourceRole.EngineSetPitch
                || engine.Kind != NativePlaybackSourceKind.IcallSingle1 || scheduled.TargetAddress != profile.ScheduledTarget
                || engine.TargetAddress != profile.EngineTarget || !scheduled.IsConfigured || !engine.IsConfigured
                || Export<VersionNative>("RtPlaybackScheduledEnginePitchVersion")() != 1)
                throw new UnsupportedSignatureException("Only the configured stock schedule and engine channels may bind this private native reapply profile before Arm.");
            VerifyChannel(scheduled); VerifyChannel(engine);
            if (Export<ConfigureScheduledEnginePitchNative>("RtPlaybackSourceConfigureScheduledEnginePitch")(
                    _owner, scheduled.Slot, engine.Slot, profile.ExactEngineCallReturn, profile.ReapplyBody,
                    profile.ReapplyLength, profile.PlayCache, profile.NativePlayTarget) != 1)
                throw new UnsupportedSignatureException("The source helper rejected the exact schedule-original engine child profile.");
            _scheduledEnginePitchConfigured = true;
        }
    }
}
