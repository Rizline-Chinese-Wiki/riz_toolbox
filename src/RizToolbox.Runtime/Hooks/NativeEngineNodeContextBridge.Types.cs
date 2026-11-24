using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeEngineNodeKind : uint { Start, Execute, Release, Detach }
[Flags] internal enum NativeEngineNodeSnapshotFlags : uint { Requested = 1, Valid = 2, Faulted = 4 }
[Flags] internal enum NativeEngineNodeOutputFlags : uint { Requested = 1, Read = 2, Faulted = 4 }
// Bit 8 retains its original name for the internal wire layout. Its value is
// kind-specific: Execute carries uint32 EAX, Start/Release carry uint8 AL.
[Flags] internal enum NativeEngineNodeExitFlags : uint { Returned = 1, Abnormal = 2, EnterFaulted = 4, ReturnByteValid = 8 }

/// <summary>A native-owned, copied scalar record. Pointer values are identities;
/// managed observers must never dereference them. These entries are UnityPlayer
/// native functions and have no IL2CPP hidden MethodInfo argument.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeEngineNodeFrame
{
    internal uint Version, Size;
    internal NativeEngineNodeKind Kind;
    internal NativeEngineNodeSnapshotFlags SnapshotFlags;
    internal IntPtr Arg0, Arg1, Arg2, Arg3, Caller;
    internal ulong Sequence;
    internal IntPtr Node, Owner;
    internal ulong EnumeratorHandle;
    internal IntPtr EnumeratorCache;
    internal uint EnumeratorType;
    internal int ReferenceCount;
    internal uint Reserved0, Reserved1;

    // Null actual parameters and unreadable-node facts remain observable. The
    // original alone decides whether those parameters are valid for execution.
    internal bool IsValid => Version == 1 && Size == 112 && Reserved0 == 0 && Reserved1 == 0
        && Kind is NativeEngineNodeKind.Start or NativeEngineNodeKind.Execute or NativeEngineNodeKind.Release or NativeEngineNodeKind.Detach
        && Caller != IntPtr.Zero && Sequence is > 0 and <= long.MaxValue
        && (Kind == NativeEngineNodeKind.Start
            ? SnapshotFlags == 0 && Node == IntPtr.Zero && Owner == IntPtr.Zero && EnumeratorHandle == 0
                && EnumeratorCache == IntPtr.Zero && EnumeratorType == 0 && ReferenceCount == 0
            : Arg2 == IntPtr.Zero && Arg3 == IntPtr.Zero && (Kind == NativeEngineNodeKind.Execute || Arg1 == IntPtr.Zero)
                && Node == Arg0 && (SnapshotFlags == (NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Valid)
                    || SnapshotFlags == (NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Faulted))
                && ((SnapshotFlags & NativeEngineNodeSnapshotFlags.Faulted) == 0
                    || Owner == IntPtr.Zero && EnumeratorHandle == 0 && EnumeratorCache == IntPtr.Zero && EnumeratorType == 0 && ReferenceCount == 0));
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEngineNodeCompletion
{
    internal uint Version, Size;
    internal NativeEngineNodeExitFlags Flags;
    internal uint RawReturn;
    internal IntPtr StartOutputNode;
    internal NativeEngineNodeOutputFlags OutputFlags;
    internal uint Reserved;

    internal bool IsValidFor(NativeEngineNodeKind kind)
    {
        if (kind is not (NativeEngineNodeKind.Start or NativeEngineNodeKind.Execute or NativeEngineNodeKind.Release or NativeEngineNodeKind.Detach)
            || Version != 1 || Size != 32 || Reserved != 0 || (Flags & ~(NativeEngineNodeExitFlags.Returned
            | NativeEngineNodeExitFlags.Abnormal | NativeEngineNodeExitFlags.EnterFaulted | NativeEngineNodeExitFlags.ReturnByteValid)) != 0)
            return false;
        var returned = (Flags & NativeEngineNodeExitFlags.Returned) != 0;
        var abnormal = (Flags & NativeEngineNodeExitFlags.Abnormal) != 0;
        var valueValid = (Flags & NativeEngineNodeExitFlags.ReturnByteValid) != 0;
        if (returned == abnormal || valueValid != (returned && kind != NativeEngineNodeKind.Detach)
            || (!valueValid ? RawReturn != 0 : kind != NativeEngineNodeKind.Execute && RawReturn > byte.MaxValue)) return false;
        if (kind != NativeEngineNodeKind.Start || !returned) return OutputFlags == 0 && StartOutputNode == IntPtr.Zero;
        return (OutputFlags == (NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read)
            || OutputFlags == (NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Faulted))
            && ((OutputFlags & NativeEngineNodeOutputFlags.Faulted) == 0 || StartOutputNode == IntPtr.Zero);
    }
}

internal readonly record struct NativeEngineNodeChannelIdentity(NativeEngineNodeKind Kind, uint Slot,
    IntPtr Target, IntPtr Original, IntPtr Entry, ulong Epoch)
{
    internal bool IsComplete => Kind is NativeEngineNodeKind.Start or NativeEngineNodeKind.Execute or NativeEngineNodeKind.Release or NativeEngineNodeKind.Detach
        && Slot < 32 && Target != IntPtr.Zero && Original != IntPtr.Zero && Entry != IntPtr.Zero && Epoch != 0
        && Target != Original && Target != Entry && Original != Entry;
}

internal interface INativeEngineNodeContextBridge
{
    NativeEngineNodeChannelIdentity Identity { get; }
    void Attach(Func<NativeEngineNodeFrame, nuint> enter,
        Action<NativeEngineNodeFrame, nuint, NativeEngineNodeCompletion> exit);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
}

internal static class NativeEngineNodeProfile
{
    internal static uint Rva(NativeEngineNodeKind kind) => kind switch {
        NativeEngineNodeKind.Start => 0x792240,
        NativeEngineNodeKind.Execute => 0x78F840,
        NativeEngineNodeKind.Release => 0x78F680,
        NativeEngineNodeKind.Detach => 0x793840,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)) };

    // These declarations preserve the actual register/return ABI; no bool
    // marshalling, generic void operation transport or hidden MethodInfo.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate byte Start(IntPtr owner, IntPtr enumerator, IntPtr nameArgument, IntPtr outputNode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint Execute(IntPtr node, IntPtr outputByte);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate byte Release(IntPtr node);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Detach(IntPtr node);
}
