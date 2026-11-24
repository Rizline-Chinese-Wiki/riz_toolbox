using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeEngineSchedulerKind : uint { Register, Remove, Consume, Pump, Drain, Callback }
[Flags] internal enum NativeEngineSchedulerFlags : uint
{ SnapshotRequested=1, SnapshotValid=2, SnapshotFaulted=4, Unknown=8, MainThread=16, ParentMatched=32,
    SourceDrainRow=512, SourceConsume=1024, SnapshotCached=2048 }
[Flags] internal enum NativeEngineSchedulerExitFlags : uint
{ Returned=1, Abnormal=2, EnterFaulted=4, RawRaxValid=8, MissingRow=16, Unknown=32 }
internal enum NativeEngineSchedulerRowKind : uint { Begin, End, Abnormal }
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineSchedulerRecord
{
    internal ulong DueTimeBits; internal long Frame; internal uint IntervalBits, Repeat;
    internal IntPtr Payload, Callback, Cleanup; internal uint OwnerId, Category, CreationTag, Reserved;
    internal bool IsValid => Reserved == 0 && Repeat <= byte.MaxValue;
}
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineSchedulerFrame
{
    internal uint Version, Size; internal NativeEngineSchedulerKind Kind; internal NativeEngineSchedulerFlags Flags;
    internal uint Slot, ThreadId, Depth, Reserved;
    internal ulong Epoch, Sequence, ParentSequence, RootSequence, SourceSequence;
    internal IntPtr Arg0, Arg1, Arg2, Arg3, Caller, Tree, Manager, Entry, SourceRecord, OutputSlot;
    internal NativeEngineSchedulerRecord Record;
    internal IntPtr NodeOwner; internal ulong EnumeratorHandle; internal IntPtr EnumeratorCache;
    internal uint EnumeratorType; internal int ReferenceCount; internal uint NodeSnapshotFlags, Reserved1;
    internal bool IsValid => Version==1 && Size==256 && (uint)Kind<6 && Slot<32 && ThreadId!=0 && Depth is >0 and <=128
        && Reserved==0 && Reserved1==0 && Record.IsValid && Epoch is >0 and <=long.MaxValue
        && Sequence is >0 and <=long.MaxValue && ParentSequence<Sequence && RootSequence is >0 && RootSequence<=Sequence
        && (Flags & ~(NativeEngineSchedulerFlags)3647)==0 && (NodeSnapshotFlags & ~7u)==0 && Caller!=IntPtr.Zero
        && (Flags&(NativeEngineSchedulerFlags.SourceDrainRow|NativeEngineSchedulerFlags.SourceConsume))
            !=(NativeEngineSchedulerFlags.SourceDrainRow|NativeEngineSchedulerFlags.SourceConsume)
        && ((Flags&(NativeEngineSchedulerFlags.SourceDrainRow|NativeEngineSchedulerFlags.SourceConsume))!=0)==(SourceSequence!=0)
        && ((Flags&NativeEngineSchedulerFlags.SnapshotCached)==0 || Kind==NativeEngineSchedulerKind.Callback
            && (Flags&NativeEngineSchedulerFlags.SourceConsume)!=0);
    internal bool HasFault => (Flags & (NativeEngineSchedulerFlags.Unknown|NativeEngineSchedulerFlags.SnapshotFaulted))!=0
        || (NodeSnapshotFlags&4)!=0;
}
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineSchedulerCompletion
{
    internal uint Version, Size; internal NativeEngineSchedulerExitFlags Flags; internal uint OutputFlags;
    internal ulong RawRax; internal IntPtr OutputEntry; internal ulong RowBegins, RowEnds;
    internal bool IsValid => Version==1 && Size==48 && (Flags & ~(NativeEngineSchedulerExitFlags)63)==0
        && (OutputFlags&~7u)==0 && ((Flags&NativeEngineSchedulerExitFlags.Returned)!=0) != ((Flags&NativeEngineSchedulerExitFlags.Abnormal)!=0)
        && ((Flags&NativeEngineSchedulerExitFlags.RawRaxValid)!=0)==((Flags&NativeEngineSchedulerExitFlags.Returned)!=0);
    internal bool Normal => IsValid && Flags==(NativeEngineSchedulerExitFlags.Returned|NativeEngineSchedulerExitFlags.RawRaxValid);
}
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineSchedulerRowFrame
{
    internal uint Version, Size; internal NativeEngineSchedulerRowKind Kind; internal NativeEngineSchedulerFlags Flags;
    internal uint Slot, ThreadId, Depth, Reserved;
    internal ulong Epoch, DispatchSequence, ParentSequence, RootSequence, RowSequence, EventSequence;
    internal IntPtr Tree, Manager, Entry, SitePc, RelayReturnPc; internal NativeEngineSchedulerRecord Record;
    internal IntPtr NodeOwner; internal ulong EnumeratorHandle; internal IntPtr EnumeratorCache;
    internal uint EnumeratorType; internal int ReferenceCount; internal uint NodeSnapshotFlags, Reserved1;
    internal bool IsValid => Version==1 && Size==224 && (uint)Kind<3 && Slot<32 && ThreadId!=0 && Depth is >0 and <=128
        && Reserved==0 && Reserved1==0 && Record.IsValid && Epoch is >0 and <=long.MaxValue
        && DispatchSequence is >0 and <=long.MaxValue && RowSequence is >0 and <=long.MaxValue
        && EventSequence is >0 and <=long.MaxValue && (Flags & ~(NativeEngineSchedulerFlags)3647)==0 && (NodeSnapshotFlags&~7u)==0;
    internal bool HasFault => Kind==NativeEngineSchedulerRowKind.Abnormal
        || (Flags&(NativeEngineSchedulerFlags.Unknown|NativeEngineSchedulerFlags.SnapshotFaulted))!=0 || (NodeSnapshotFlags&4)!=0;
}
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineSchedulerSite
{ internal IntPtr SitePc, RelayReturnPc; internal bool Complete => SitePc!=IntPtr.Zero && RelayReturnPc!=IntPtr.Zero && SitePc!=RelayReturnPc; }
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineSchedulerConfiguration
{
    internal uint Version, Size; internal IntPtr Target, Original; internal uint MainThreadId, Reserved;
    internal NativeEngineSchedulerSite Begin, End;
}
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineSchedulerInformation
{
    internal uint Version, Size, Kind, Slot, Configured, Attached, Unknown, MainThreadId;
    internal IntPtr Target, Original, Entry, RowEntry;
    internal ulong Epoch, Entered, Exited, InFlight, Faults, SnapshotFaults, RowBegins, RowEnds, RowsInFlight, UnmatchedRows;
}
internal readonly record struct NativeEngineSchedulerChannelIdentity(NativeEngineSchedulerKind Kind, uint Slot, ulong Epoch,
    uint MainThreadId, IntPtr Target, IntPtr Original, IntPtr Entry, IntPtr RowEntry, NativeEngineSchedulerSite Begin, NativeEngineSchedulerSite End)
{
    internal bool Complete => (uint)Kind<6 && Slot<32 && Epoch is >0 and <=long.MaxValue && MainThreadId!=0
        && Target!=IntPtr.Zero && Original!=IntPtr.Zero && Entry!=IntPtr.Zero && Target!=Original && Target!=Entry && Original!=Entry
        && (Kind==NativeEngineSchedulerKind.Drain ? RowEntry!=IntPtr.Zero && Begin.Complete && End.Complete
            && Begin.SitePc!=End.SitePc && Begin.RelayReturnPc!=End.RelayReturnPc
            : Begin.SitePc==IntPtr.Zero && Begin.RelayReturnPc==IntPtr.Zero && End.SitePc==IntPtr.Zero && End.RelayReturnPc==IntPtr.Zero);
}
internal interface INativeEngineSchedulerContextBridge
{
    NativeEngineSchedulerChannelIdentity Identity { get; }
    void Attach(Func<NativeEngineSchedulerFrame,nuint> enter,
        Action<NativeEngineSchedulerFrame,nuint,NativeEngineSchedulerCompletion> exit, Action<NativeEngineSchedulerRowFrame>? row);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
}
