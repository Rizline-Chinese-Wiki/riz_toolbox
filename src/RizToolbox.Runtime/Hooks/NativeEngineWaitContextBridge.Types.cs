using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeEngineWaitKind:uint { Yield,Dispatch,Batch,NodeStart,NodeExecute,NodeRelease,NodeDetach,SchedulerRegister,SchedulerRemove,SchedulerConsume,SchedulerPump,SchedulerDrain,SchedulerCallback }
[Flags] internal enum NativeEngineWaitFlags:uint
{ SnapshotRequested=1,SnapshotValid=2,SnapshotFaulted=4,Unknown=8,MainThread=16,ParentMatched=32,SourceDispatch=64,SourceBatch=128,SnapshotCached=256,KnownCallback=512,CallbackExpected=1024,CleanupExpected=2048 }
[Flags] internal enum NativeEngineWaitExitFlags:uint
{ Returned=1,Abnormal=2,EnterFaulted=4,RawRaxValid=8,MissingPhase=16,Unknown=32 }
internal enum NativeEngineWaitPhase:uint
{ PublishBegin,OldReferenceDecision,OldReferenceReturned,PublishCommitted,DetachOwnerBegin,DetachNestedOwnerBegin,ReleaseClearBegin,ReleaseFieldsCleared,ReleaseReferenceReturned,StartClearBegin,StartFieldsCleared,StartReferenceReturned,DispatchOwnerResult,DispatchCallbackArmed,DispatchCleanupDecision,DispatchClearReached,BatchObjectBegin,BatchObjectEnd,Abnormal }
[Flags]internal enum NativeEngineWaitStageFlags:uint
{ BeforeWrite=1,FieldsWritten=2,SharedReferenceReturned=4,OwnerNull=8,CallbackClaimed=16,CallbackCompleted=32,CleanupClaimed=64,CleanupCompleted=128,CallbackSlotClearReached=256,ZeroOldReference=512,Committed=1024,MissingRequiredPhase=2048 }
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineWaitTuple
{
    internal IntPtr Callback,Cleanup,Payload;internal uint OwnerId,Reserved;
    internal bool IsValid=>Reserved==0;
    internal bool Empty=>Callback==IntPtr.Zero&&Cleanup==IntPtr.Zero&&Payload==IntPtr.Zero&&OwnerId==0;
}
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineWaitFrame
{
    internal uint Version,Size;internal NativeEngineWaitKind Kind;internal NativeEngineWaitFlags Flags;
    internal uint Slot,ThreadId,Depth,Reserved;
    internal ulong Epoch,Sequence,ParentSequence,RootSequence,ExternalSequence,SourceSequence,OperationSequence;
    internal IntPtr Arg0,Arg1,Arg2,Arg3,Caller,Target,Wait,Node,NodeOwner;
    internal ulong EnumeratorHandle;internal IntPtr EnumeratorCache;
    internal uint EnumeratorType;internal int ReferenceCount;internal uint NodeSnapshotFlags,Reserved1;
    internal NativeEngineWaitTuple Tuple;internal IntPtr Manager;internal ulong Index;
    internal bool IsValid=>Version==1&&Size==240&&(uint)Kind<13&&Slot<32&&ThreadId!=0&&Depth is >0 and <=128
        &&Reserved==0&&Reserved1==0&&Tuple.IsValid&&Epoch is >0 and <=long.MaxValue&&Sequence is >0 and <=long.MaxValue
        &&ParentSequence<Sequence&&RootSequence is >0&&RootSequence<=Sequence&&(Flags&~(NativeEngineWaitFlags)4095)==0
        &&(NodeSnapshotFlags&~7u)==0&&Caller!=IntPtr.Zero&&Target!=IntPtr.Zero
        &&((uint)Kind<3?ExternalSequence==0:ExternalSequence!=0)
        &&(Flags&(NativeEngineWaitFlags.SourceDispatch|NativeEngineWaitFlags.SourceBatch))!=(NativeEngineWaitFlags.SourceDispatch|NativeEngineWaitFlags.SourceBatch)
        &&((Flags&(NativeEngineWaitFlags.SourceDispatch|NativeEngineWaitFlags.SourceBatch))!=0)==(SourceSequence!=0);
    internal bool HasFault=>(Flags&(NativeEngineWaitFlags.SnapshotFaulted|NativeEngineWaitFlags.Unknown))!=0||(NodeSnapshotFlags&4)!=0;
}
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineWaitCompletion
{
    internal uint Version,Size;internal NativeEngineWaitExitFlags Flags;internal uint OutputFlags;
    internal ulong RawRax;internal uint ExternalFlags,ExternalRawReturn;internal IntPtr OutputNode;internal ulong Phases;
    internal bool Normal=>Version==1&&Size==48&&Flags==(NativeEngineWaitExitFlags.Returned|NativeEngineWaitExitFlags.RawRaxValid)&&(OutputFlags&~7u)==0;
    internal bool IsNormalFor(NativeEngineWaitKind kind)=>kind==NativeEngineWaitKind.NodeDetach
        ?Version==1&&Size==48&&Flags==NativeEngineWaitExitFlags.Returned&&RawRax==0&&ExternalFlags==(uint)NativeEngineNodeExitFlags.Returned
            &&ExternalRawReturn==0&&OutputFlags==0&&OutputNode==IntPtr.Zero
        :Normal;
}
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineWaitPhaseFrame
{
    internal uint Version,Size;internal NativeEngineWaitPhase Kind;internal NativeEngineWaitFlags Flags;
    internal uint Slot,ThreadId,Depth;internal NativeEngineWaitKind ScopeKind;
    internal ulong Epoch,Sequence,ExternalSequence,ParentSequence,RootSequence,OperationSequence,EventSequence,SourceSequence;
    internal IntPtr SitePc,RelayReturnPc,Wait,Node,Manager;internal ulong Index;
    internal IntPtr OldWait,ActualOwner,ActualCleanup,ActualPayload;internal NativeEngineWaitTuple Tuple;
    internal IntPtr NodeOwner;internal ulong EnumeratorHandle;internal IntPtr EnumeratorCache;
    internal uint EnumeratorType;internal int ReferenceCount;internal uint NodeSnapshotFlags;internal NativeEngineWaitStageFlags StageFlags;
    internal IntPtr ActualCallback,OriginalCaller;internal ulong RawRax;internal uint CompletionFlags,Reserved;
    internal ulong SourceExternalSequence;
    internal bool IsValid=>Version==1&&Size==288&&(uint)Kind<=18&&(uint)ScopeKind<13&&Slot<32&&ThreadId!=0&&Depth is >0 and <=128
        &&Epoch is >0 and <=long.MaxValue&&Sequence is >0&&OperationSequence is >0&&EventSequence is >0
        &&Tuple.IsValid&&Reserved==0&&(Flags&~(NativeEngineWaitFlags)4095)==0&&(NodeSnapshotFlags&~7u)==0&&(StageFlags&~(NativeEngineWaitStageFlags)4095)==0;
    internal bool HasFault=>Kind==NativeEngineWaitPhase.Abnormal||(StageFlags&NativeEngineWaitStageFlags.MissingRequiredPhase)!=0||(Flags&(NativeEngineWaitFlags.SnapshotFaulted|NativeEngineWaitFlags.Unknown))!=0||(NodeSnapshotFlags&4)!=0;
}
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineWaitSite
{ internal IntPtr SitePc,RelayReturnPc;internal bool Complete=>SitePc!=IntPtr.Zero&&RelayReturnPc!=IntPtr.Zero&&SitePc!=RelayReturnPc; }
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineWaitConfiguration
{
    internal uint Version,Size;
    internal IntPtr YieldTarget,DispatchTarget,BatchTarget,YieldOriginal,DispatchOriginal,BatchOriginal;
    internal IntPtr NodeStart,NodeExecute,NodeRelease,NodeDetach;
    internal IntPtr SchedulerRegister,SchedulerRemove,SchedulerConsume,SchedulerPump,SchedulerDrain,SchedulerCallback;
    internal uint MainThreadId,Reserved;
    [MarshalAs(UnmanagedType.ByValArray,SizeConst=18)] internal NativeEngineWaitSite[] Sites;
}
[StructLayout(LayoutKind.Sequential)] internal struct NativeEngineWaitInformation
{
    internal uint Version,Size,Slot,Configured,Attached,Unknown,MainThreadId,Reserved;
    internal ulong Epoch,Entered,Exited,InFlight,Faults,SnapshotFaults,Phases,Unmatched;
    internal IntPtr YieldTarget,DispatchTarget,BatchTarget,YieldOriginal,DispatchOriginal,BatchOriginal;
    internal IntPtr YieldEntry,DispatchEntry,BatchEntry,PhaseEntry;
}
internal sealed class NativeEngineWaitFamilyIdentity
{
    internal uint Slot{get;}internal ulong Epoch{get;}internal uint MainThreadId{get;}internal IntPtr PhaseEntry{get;}
    private readonly IntPtr[] _targets,_originals,_entries;private readonly NativeEngineWaitSite[] _sites;
    internal NativeEngineWaitFamilyIdentity(uint slot,ulong epoch,uint mainThreadId,IReadOnlyList<IntPtr> targets,
        IReadOnlyList<IntPtr> originals,IReadOnlyList<IntPtr> entries,IntPtr phaseEntry,IReadOnlyList<NativeEngineWaitSite> sites)
    {
        ArgumentNullException.ThrowIfNull(targets);ArgumentNullException.ThrowIfNull(originals);ArgumentNullException.ThrowIfNull(entries);ArgumentNullException.ThrowIfNull(sites);
        if(slot>=32||epoch is 0 or >long.MaxValue||mainThreadId==0||targets.Count!=13||originals.Count!=3||entries.Count!=3||sites.Count!=18
            ||targets.Any(value=>value==IntPtr.Zero)||originals.Any(value=>value==IntPtr.Zero)||entries.Any(value=>value==IntPtr.Zero)
            ||phaseEntry==IntPtr.Zero||sites.Any(value=>!value.Complete)||targets.Distinct().Count()!=13
            ||Enumerable.Range(0,3).Any(i=>targets[i]==originals[i]||targets[i]==entries[i]||originals[i]==entries[i]))
            throw new ArgumentException("An exact configured wait family requires all thirteen targets and eighteen source/relay pairs.");
        (Slot,Epoch,MainThreadId,PhaseEntry)=(slot,epoch,mainThreadId,phaseEntry);_targets=targets.ToArray();_originals=originals.ToArray();_entries=entries.ToArray();_sites=sites.ToArray();
    }
    internal IntPtr Target(NativeEngineWaitKind kind)=>_targets[(int)kind];
    internal IntPtr Original(int kind)=>_originals[kind];internal IntPtr Entry(int kind)=>_entries[kind];
    internal NativeEngineWaitSite Site(NativeEngineWaitPhase phase)=>_sites[(int)phase];
}
internal interface INativeEngineWaitContextBridge
{
    NativeEngineWaitFamilyIdentity Identity{get;}
    void Attach(Func<NativeEngineWaitFrame,nuint> enter,Action<NativeEngineWaitFrame,nuint,NativeEngineWaitCompletion> exit,Action<NativeEngineWaitPhaseFrame> phase);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
}
