using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeEngineInlineKind : uint { StartEntered, Begin, EndFree, EndRetained, StartExited }
[Flags] internal enum NativeEngineInlineFlags : uint
{
    SnapshotRequested = 1, SnapshotValid = 2, SnapshotFaulted = 4, ParentMatched = 8,
    Unknown = 16, MissingEnd = 32, Abnormal = 64, ObserverFaulted = 128,
    OutputRequested = 256, OutputRead = 512, OutputFaulted = 1024
}

// Independent side ABI1; the ordinary ABI2 frame/completion remain 112/32.
// Every pointer is a copied scalar identity. End never authorizes a node read.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeEngineInlineFrame
{
    internal uint Version, Size;
    internal NativeEngineInlineKind Kind;
    internal NativeEngineInlineFlags Flags;
    internal uint StartSlot, ProbeSlot, ThreadId, Depth;
    internal ulong Epoch, StartSequence, ParentStartSequence, EventSequence;
    internal IntPtr Node, Owner;
    internal ulong EnumeratorHandle;
    internal IntPtr EnumeratorCache;
    internal uint EnumeratorType;
    internal int ReferenceCount;
    internal IntPtr SitePc, RelayReturnPc, OutputSlot;
    internal NativeEngineNodeExitFlags StartExitFlags;
    internal uint RawStartReturn;
    internal IntPtr StartOutputNode;
    internal bool IsValid => Version == 1 && Size == 144 && StartSlot < 32 && ProbeSlot < 32
        && (Kind is NativeEngineInlineKind.StartEntered or NativeEngineInlineKind.Begin or NativeEngineInlineKind.EndFree
            or NativeEngineInlineKind.EndRetained or NativeEngineInlineKind.StartExited)
        && (Flags & ~(NativeEngineInlineFlags)2047) == 0 && ThreadId != 0 && Depth is > 0 and <= 128
        && Epoch is > 0 and <= long.MaxValue && StartSequence is > 0 and <= long.MaxValue
        && EventSequence is > 0 and <= long.MaxValue && ParentStartSequence < StartSequence
        && (Depth == 1 ? ParentStartSequence == 0 : ParentStartSequence != 0)
        && (StartExitFlags & ~(NativeEngineNodeExitFlags)15) == 0 && RawStartReturn <= byte.MaxValue;
    internal bool HasFault => (Flags & (NativeEngineInlineFlags.SnapshotFaulted | NativeEngineInlineFlags.Unknown
        | NativeEngineInlineFlags.MissingEnd | NativeEngineInlineFlags.Abnormal | NativeEngineInlineFlags.ObserverFaulted
        | NativeEngineInlineFlags.OutputFaulted)) != 0;
    internal bool HasSnapshot => (Flags & (NativeEngineInlineFlags.SnapshotRequested | NativeEngineInlineFlags.SnapshotValid))
        == (NativeEngineInlineFlags.SnapshotRequested | NativeEngineInlineFlags.SnapshotValid) && !HasFault;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEngineInlineSite
{
    internal IntPtr SitePc, RelayReturnPc;
    internal bool IsComplete => SitePc != IntPtr.Zero && RelayReturnPc != IntPtr.Zero && SitePc != RelayReturnPc;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeEngineInlineConfiguration
{
    internal uint Version, Size, StartSlot, MainThreadId;
    internal ulong ExpectedStartEpoch;
    internal IntPtr ExpectedStartTarget;
    internal ulong Reserved;
    internal NativeEngineInlineSite Begin, EndFree, EndRetained;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeEngineInlineInformation
{
    internal uint Version, Size, Slot, Configured, Attached, Unknown, MainThreadId, Reserved;
    internal ulong Epoch, StartEpoch;
    internal IntPtr StartTarget, Entry;
    internal ulong StartsEntered, StartsExited, StartsInFlight, Begins, EndsFree, EndsRetained,
        ProbesInFlight, Faults, SnapshotFaults, OrphanProbes, UnmatchedStarts, Events;
}

internal readonly record struct NativeEngineInlineChannelIdentity(uint Slot, ulong Epoch, uint MainThreadId,
    IntPtr Entry, NativeEngineNodeChannelIdentity Start, NativeEngineInlineSite Begin,
    NativeEngineInlineSite EndFree, NativeEngineInlineSite EndRetained)
{
    internal bool IsComplete => Slot < 32 && Epoch is > 0 and <= long.MaxValue && MainThreadId != 0
        && Entry != IntPtr.Zero && Start.IsComplete && Start.Kind == NativeEngineNodeKind.Start
        && Begin.IsComplete && EndFree.IsComplete && EndRetained.IsComplete
        && Begin.SitePc != EndFree.SitePc && Begin.SitePc != EndRetained.SitePc && EndFree.SitePc != EndRetained.SitePc
        && Begin.RelayReturnPc != EndFree.RelayReturnPc && Begin.RelayReturnPc != EndRetained.RelayReturnPc
        && EndFree.RelayReturnPc != EndRetained.RelayReturnPc;
    internal bool MatchesSite(NativeEngineInlineFrame frame)
    {
        var site = frame.Kind switch { NativeEngineInlineKind.Begin => Begin, NativeEngineInlineKind.EndFree => EndFree,
            NativeEngineInlineKind.EndRetained => EndRetained, _ => default };
        return frame.SitePc == site.SitePc && frame.RelayReturnPc == site.RelayReturnPc;
    }
}

internal interface INativeEngineInlineContextBridge
{
    NativeEngineInlineChannelIdentity Identity { get; }
    void Attach(Action<NativeEngineInlineFrame> observe);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
}

internal static class NativeEngineInlinePlatform
{
    internal static uint CurrentOsThreadId => OperatingSystem.IsWindows() ? GetCurrentThreadId()
        : unchecked((uint)Environment.CurrentManagedThreadId); // Controlled transports only off Windows.
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
