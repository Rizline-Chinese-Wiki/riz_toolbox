using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

// Internal controlled prototype. These records confer no production capability
// and are deliberately absent from the SDK and frozen FirstInstall publication.
internal sealed record NativePlaybackSingleReturnIdentity(string RunId, long SceneGeneration, long ControllerGeneration,
    long SourceGeneration, IntPtr Controller, IntPtr Information, IntPtr Progress, IntPtr Player,
    IntPtr AudioSource, IntPtr Clip, IntPtr VerifiedGetTimeMethod)
{
    internal bool IsValid => !string.IsNullOrWhiteSpace(RunId) && SceneGeneration > 0 && ControllerGeneration > 0 && SourceGeneration > 0
        && Controller != IntPtr.Zero && Information != IntPtr.Zero && Progress != IntPtr.Zero && Player != IntPtr.Zero
        && AudioSource != IntPtr.Zero && Clip != IntPtr.Zero && VerifiedGetTimeMethod != IntPtr.Zero;
}
internal enum NativePlaybackSingleReturnPhase : uint { Paused, Playing, Scheduled }
internal sealed record NativePlaybackSingleReturnSample(NativePlaybackSingleReturnIdentity Identity, long Revision,
    NativePlaybackSingleReturnPhase Phase, double DspSeconds);
internal sealed record NativePlaybackSingleReturnFault(string Reason, Exception? Error = null);
internal sealed record NativePlaybackSingleReturnHealth(bool Known, bool AuthorityActive, long Revision, ulong InFlight,
    ulong OriginalCalls, ulong Applied, ulong Declined, ulong NativeEnterFaults, ulong NativeExitFaults,
    ulong NativeSourceFaults, string? FaultReason)
{
    internal bool ProductionAdmission => false;
}
[Flags] internal enum NativePlaybackSingleReturnFlags : uint
{ Returned = 1, Abnormal = 2, EnterFaulted = 4, AuthorityCaptured = 8, ObserverCaptured = 16 }
[StructLayout(LayoutKind.Sequential)]
internal struct NativePlaybackSingleReturnFrame
{
    internal uint Version, Size;
    internal ulong Sequence;
    internal IntPtr Self, IncomingMethod, Caller;
    internal ulong RunHigh, RunLow, ControllerGeneration, PolicyGeneration;
    internal uint Depth, ThreadId;
    internal bool IsValid => Version == 1 && Size == 80 && Sequence > 0 && Self != IntPtr.Zero && Caller != IntPtr.Zero && Depth > 0 && ThreadId > 0;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativePlaybackSingleReturnCompletion
{
    internal uint Version, Size;
    internal ulong Sequence;
    internal NativePlaybackSingleReturnFlags Flags;
    internal uint OriginalBits, CandidateBits, Disposition;
    internal ulong PolicyGeneration;
    internal nuint ObserverToken;
    // CandidateBits is provisional. A fault or conditional withdrawal during
    // this callback makes the native entry return OriginalBits instead.
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativePlaybackSingleReturnNativePolicy
{
    internal uint Version, Size;
    internal IntPtr Self, Method;
    internal ulong RunHigh, RunLow, ControllerGeneration, Generation;
    internal NativePlaybackSingleReturnPhase Phase;
    internal uint Reserved;
    internal double DspAnchor, MediaAnchor, Rate, StartDsp, ClipLength;
    internal IntPtr ReadClock, ClockOwner, PermittedNullCaller;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativePlaybackSingleReturnNativeSample
{
    internal uint Version, Size;
    internal IntPtr Self, Method;
    internal ulong RunHigh, RunLow, ControllerGeneration, Generation;
    internal NativePlaybackSingleReturnPhase Phase;
    internal uint Reserved;
    internal double DspNow;
}
