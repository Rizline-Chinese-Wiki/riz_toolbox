using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeEngineWaitState{Publishing,Published,OwnerRevoked,CallbackOpen,CleanupOpen,WithdrawPending,Retired,Unknown}
internal sealed record NativeEngineWaitSnapshot(long PublicationGeneration,long Wait,long Node,long NodeGeneration,ulong StartSequence,
    NativeEngineWaitState State,bool Committed,bool OwnerRevoked,bool FieldsCleared,bool ReferenceReturned,bool NodeWaitCleared,
    bool CallbackSlotCleared,int OpenDispatches,int OpenCallbacks,int OpenCleanups);
internal sealed record NativeEngineWaitLedgerSnapshot(long Calls,int Outstanding,bool Unknown,bool Pending,bool EngineCancelled,bool FutureClosed,
    string? Failure,IReadOnlyList<NativeEngineWaitSnapshot> Records);

// Retains publication, shared-reference and physical-dispatch duties separately.
// All native values arrive through the actual dispatcher; this class reads no
// pointers and cannot cancel a coroutine or close a future producer.
internal sealed class NativeEngineWaitLedger
{
    internal const int MaximumCalls=2048,MaximumPublications=512;
    private readonly IMainThreadDispatcher _main;private readonly NativeEngineCoroutineIdentityLedger _identities;
    private readonly NativeEngineCoroutineNodeLedger _nodes;private readonly NativeEngineWaitObserverDispatcher _dispatcher;
    private readonly Action<string>? _reportFailure;
    private readonly IntPtr _callback,_cleanup;private readonly object _issuer=new();
    private readonly List<Context> _calls=new();private readonly List<Publication> _records=new();
    private readonly Dictionary<long,Publication> _latest=new();private Context? _top;private long _generation,_capturedCalls;private int _untracked;private string? _failure;
    internal bool Unknown=>Volatile.Read(ref _failure) is not null||_identities.Unknown||_nodes.Unknown||_dispatcher.Unknown;
    internal string? FirstFailure => Volatile.Read(ref _failure) ?? _nodes.FirstFailure ?? _identities.FirstFailure;
    internal NativeEngineWaitLedger(IMainThreadDispatcher main,NativeEngineCoroutineIdentityLedger identities,NativeEngineCoroutineNodeLedger nodes,
        NativeEngineWaitObserverDispatcher dispatcher,Action<string>? reportFailure=null)
    {
        ArgumentNullException.ThrowIfNull(main);main.VerifyAccess();ArgumentNullException.ThrowIfNull(identities);ArgumentNullException.ThrowIfNull(nodes);ArgumentNullException.ThrowIfNull(dispatcher);
        if(!identities.Wraps(nodes))throw new ArgumentException("Wait publications require the actual exclusive identity/node owner.");
        (_main,_identities,_nodes,_dispatcher)=(main,identities,nodes,dispatcher);_callback=dispatcher.Identity.Target(NativeEngineWaitKind.SchedulerCallback);
        _cleanup=dispatcher.Identity.Target(NativeEngineWaitKind.NodeRelease);_reportFailure=reportFailure;
    }
    internal sealed class Publication
    {
        internal readonly long Generation,Wait;internal readonly NativeEngineCoroutineIdentityLedger.IdentityRecord Identity;
        internal readonly NativeEngineCoroutineNodeLedger.NodeRecord Node;internal NativeEngineWaitTuple Tuple;
        internal bool Committed,OwnerRevoked,FieldsCleared,ReferenceReturned,NodeWaitCleared,CallbackCleared,Retired;
        internal int Dispatches,Callbacks,Cleanups;
        internal Publication(long generation,NativeEngineWaitPhaseFrame phase,NativeEngineCoroutineIdentityLedger.IdentityRecord identity,NativeEngineCoroutineNodeLedger.NodeRecord node)
            =>(Generation,Wait,Identity,Node,Tuple)=(generation,phase.Wait.ToInt64(),identity,node,phase.Tuple);
        internal bool MayRetire=>Committed&&(FieldsCleared||CallbackCleared)&&ReferenceReturned&&NodeWaitCleared&&Dispatches==0&&Callbacks==0&&Cleanups==0;
        internal NativeEngineWaitSnapshot Read(bool unknown)=>new(Generation,Wait,Node.Pointer,Node.Generation,Node.Start.Frame.Sequence,
            Retired?NativeEngineWaitState.Retired:unknown?NativeEngineWaitState.Unknown:!Committed?NativeEngineWaitState.Publishing:
                Cleanups>0?NativeEngineWaitState.CleanupOpen:Callbacks>0?NativeEngineWaitState.CallbackOpen:
                FieldsCleared||CallbackCleared?NativeEngineWaitState.WithdrawPending:OwnerRevoked?NativeEngineWaitState.OwnerRevoked:NativeEngineWaitState.Published,
            Committed,OwnerRevoked,FieldsCleared,ReferenceReturned,NodeWaitCleared,CallbackCleared,Dispatches,Callbacks,Cleanups);
    }
    internal sealed class Operation
    {
        internal readonly ulong Sequence;internal readonly NativeEngineWaitPhase Begin;internal readonly IntPtr Wait,Node;
        internal Publication? Record,Old,CleanupRecord;internal NativeEngineWaitPhaseFrame Last;
        internal bool Decision,ReferenceReturned,FieldsCleared,Committed,OwnerResult,CallbackArmed,CallbackDone,CleanupDecision,CleanupDone,ClearReached,Ended;
        internal IntPtr OldWait,Owner,Cleanup,Payload,Callback;internal CallbackContext? CallbackContext;internal CleanupContext? CleanupContext;
        internal Operation(NativeEngineWaitPhaseFrame phase)=>(Sequence,Begin,Wait,Node,Last)=(phase.OperationSequence,phase.Kind,phase.Wait,phase.Node,phase);
    }
    internal sealed class Context:IDisposable
    {
        internal readonly NativeEngineWaitLedger Owner;internal readonly NativeEngineWaitObserverDispatcher.ScopeProof Scope;
        internal readonly NativeEngineWaitFrame Frame;internal readonly Context? Parent;
        internal readonly Dictionary<ulong,Operation> _operations=new();
        internal Publication? _dispatch;internal NativeEngineCoroutineNodeLedger.Call? _nodeCall;
        internal CallbackContext? _callback;internal CleanupContext? _cleanup;
        internal bool Completed,Disposed,Failed;internal NativeEngineWaitCompletion Completion;
        internal Context(NativeEngineWaitLedger owner,NativeEngineWaitObserverDispatcher.Observation receipt)=>(Owner,Scope,Frame,Parent)=(owner,receipt.Scope,receipt.Frame,owner._top);
        public void Dispose()=>Owner.Release(this);
    }
    internal Context Capture(NativeEngineWaitObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess();ArgumentNullException.ThrowIfNull(receipt);var call=new Context(this,receipt);
        if(_calls.Count==MaximumCalls){Interlocked.Increment(ref _untracked);Fault("Wait physical call capacity exhausted.",call.Frame);return call;}
        _calls.Add(call);++_capturedCalls;_top=call;
        try
        {
            if(Unknown||!receipt.IsCurrent||receipt.Completion is not null||receipt.Phase is not null||!ReferenceEquals(call.Scope.Parent,call.Parent?.Scope))
                throw new InvalidOperationException("Wait ledger entry lacks its exact current delivery/parent.");
            if(call.Frame.Kind==NativeEngineWaitKind.Dispatch)
            {
                if(call.Frame.Arg0!=call.Frame.Wait||!HasSnapshot(call.Frame.Flags)||call.Frame.NodeSnapshotFlags!=0)
                    throw new InvalidOperationException("Wait Dispatch must retain its actual input tuple without borrowing a node snapshot.");
                call._dispatch=Find(call.Frame.Wait,call.Frame.Tuple,call.Frame.Tuple.Callback==IntPtr.Zero);
                if(call._dispatch is{} record)
                {if(!HasSnapshot(call.Frame.Flags))throw new InvalidOperationException("Wait Dispatch lacks its actual copied tuple.");++record.Dispatches;}
            }
        }
        catch(Exception error){call.Failed=true;Fault(error.Message,call.Frame);}return call;
    }
    // Invoked immediately after the old actual identity Capture, while its
    // NodeCall is the exact live stack top. Start may not have a node yet.
    internal void BindNodeCall(NativeEngineCoroutineNodeLedger.Call call)
    {
        _main.VerifyAccess();ArgumentNullException.ThrowIfNull(call);
        try
        {
            var current=RequireBoundary();var expected=(NativeEngineWaitKind)((uint)call.Frame.Kind+3);
            if(current.Frame.Kind!=expected||current.Frame.ExternalSequence!=call.Frame.Sequence||!_nodes.IsCurrentCall(call)
                ||!ReferenceEquals(call.Owner,_nodes)||current.Frame.Arg0!=call.Frame.Arg0||current.Frame.Arg1!=call.Frame.Arg1
                ||current.Frame.Arg2!=call.Frame.Arg2||current.Frame.Arg3!=call.Frame.Arg3||current.Frame.Caller!=call.Frame.Caller
                ||current._nodeCall is{} prior&&!ReferenceEquals(prior,call))throw new InvalidOperationException("Wait boundary is not the actual captured node call.");
            current._nodeCall=call;
        }
        catch (Exception error)
        {
            Fault($"{error.Message} Typed {call.Frame.Kind} sequence={call.Frame.Sequence}, caller=0x{call.Frame.Caller:x}.",
                _top?.Frame ?? default);
        }
    }

    internal bool IsForIdentityLedger(NativeEngineCoroutineIdentityLedger identities) => ReferenceEquals(_identities, identities);

    // Fixed UnityPlayer continuation returns in the verified image which owns
    // the published Yield target. The source proof still comes from its live frames.
    private IntPtr YieldRelativeSite(int rva) => new(checked(_dispatcher.Identity.Target(NativeEngineWaitKind.Yield).ToInt64() + rva - 0x78fc50));
    internal IntPtr WaitUntilChildCaller => YieldRelativeSite(0x78fea2);
    internal IntPtr CoroutineYieldCaller => YieldRelativeSite(0x78fc32);
    internal IntPtr InitialExecuteCaller => YieldRelativeSite(0x7924bf);

    internal bool TryReadWaitUntilYieldStart(NativeEngineCoroutineIdentityLedger.Call child,
        NativeEngineCoroutineIdentityLedger.Call parentExecute, out ulong sequence)
    {
        _main.VerifyAccess(); sequence = 0;
        if (Unknown || _top is not { Frame.Kind: NativeEngineWaitKind.NodeStart } boundary
            || !ReferenceEquals(_dispatcher.CurrentBoundaryScope, boundary.Scope)
            || boundary.Completed || boundary.Disposed || boundary.Failed || boundary._nodeCall is not null
            || boundary.Parent is not { Frame.Kind: NativeEngineWaitKind.Yield } yield
            || yield.Completed || yield.Disposed || yield.Failed || !ReferenceEquals(boundary.Scope.Parent, yield.Scope)
            || yield.Parent is not { Frame.Kind: NativeEngineWaitKind.NodeExecute } execute
            || execute.Completed || execute.Disposed || execute.Failed || !ReferenceEquals(yield.Scope.Parent, execute.Scope)
            || !ReferenceEquals(child.Owner, _identities) || !ReferenceEquals(parentExecute.Owner, _identities)
            || !ReferenceEquals(child.Parent, parentExecute) || !child.Relevant || !parentExecute.Relevant
            || child.Completed || child.Disposed || parentExecute.Completed || parentExecute.Disposed
            || child.NodeCall is not { } childScope || !_nodes.IsCurrentCall(childScope)
            || parentExecute.NodeCall is not { ExecuteEntered: true, Node: { } node } parentScope
            || !ReferenceEquals(childScope.Parent, parentScope) || !ReferenceEquals(execute._nodeCall, parentScope)
            || parentExecute.Identity is not { } parent || !ReferenceEquals(parent.Node, node)
            || !ReferenceEquals(parentExecute.Parent, parent.Start) || !ReferenceEquals(parent.Start.NodeCall, node.Start)
            || parent.Start.Completed || parent.Start.Disposed || node.Executions <= 0 || node.FinalCleanup)
            return false;

        var start = child.Frame; var parentFrame = parentExecute.Frame; var input = yield.Frame;
        if (start.Kind != NativeEngineNodeKind.Start || start.Caller != WaitUntilChildCaller || start.Arg0.ToInt64() != node.NativeOwner
            || start.Arg1 == IntPtr.Zero || start.Arg1.ToInt64() == node.Enumerator || start.Arg2 != IntPtr.Zero || start.Arg3 == IntPtr.Zero
            || boundary.Frame.ExternalSequence != start.Sequence || boundary.Frame.Caller != start.Caller
            || boundary.Frame.Arg0 != start.Arg0 || boundary.Frame.Arg1 != start.Arg1 || boundary.Frame.Arg2 != start.Arg2 || boundary.Frame.Arg3 != start.Arg3
            || parentFrame.Kind != NativeEngineNodeKind.Execute || parentFrame.Caller != InitialExecuteCaller
            || execute.Frame.ExternalSequence != parentFrame.Sequence || execute.Frame.Caller != parentFrame.Caller
            || input.Caller != CoroutineYieldCaller || input.Arg0.ToInt64() != node.Pointer || input.Arg1 != start.Arg1
            || input.Node != input.Arg0 || input.NodeSnapshotFlags != 3 || input.NodeOwner.ToInt64() != node.NativeOwner
            || input.EnumeratorHandle != node.Handle || input.EnumeratorCache.ToInt64() != node.Enumerator || input.EnumeratorType != 2)
            return false;
        sequence = input.Sequence; return true;
    }

    private Context RequireBoundary()
    {
        if(Unknown||_top is not{} current||!ReferenceEquals(_dispatcher.CurrentBoundaryScope,current.Scope)||current.Completed||current.Disposed)
            throw new InvalidOperationException("Wait ledger lacks its exact current private boundary.");return current;
    }
    private static bool HasSnapshot(NativeEngineWaitFlags flags)=>(flags&(NativeEngineWaitFlags.SnapshotRequested|NativeEngineWaitFlags.SnapshotValid))==(NativeEngineWaitFlags.SnapshotRequested|NativeEngineWaitFlags.SnapshotValid)
        &&(flags&(NativeEngineWaitFlags.SnapshotFaulted|NativeEngineWaitFlags.Unknown))==0;
    private void RequireNode(IntPtr pointer,IntPtr owner,ulong handle,IntPtr cache,uint type,uint flags,NativeEngineCoroutineNodeLedger.NodeRecord node)
    {
        if(!_nodes.IsLatest(node)||node.FinalCleanup||pointer.ToInt64()!=node.Pointer||flags!=3||handle!=node.Handle||cache.ToInt64()!=node.Enumerator||type!=2
            ||owner.ToInt64()!=node.NativeOwner&&!(owner==IntPtr.Zero&&node.DetachObserved))throw new InvalidOperationException("Wait scalar node differs from its retained exact node generation.");
    }
    private Publication? Find(IntPtr wait,NativeEngineWaitTuple tuple,bool allowRetiredCallbackNull=false)
    {
        if(!_latest.TryGetValue(wait.ToInt64(),out var record))return null;
        if(record.Retired&&tuple.Empty)return null; // A copied empty tuple lends no retired node identity.
        var cleared=record.FieldsCleared&&tuple.Empty;
        var callbackCleared=record.CallbackCleared&&tuple.Callback==IntPtr.Zero&&tuple.Cleanup==record.Tuple.Cleanup&&tuple.Payload==record.Tuple.Payload
            &&(tuple.OwnerId==record.Tuple.OwnerId||record.OwnerRevoked&&tuple.OwnerId==0);
        // The manager can still own the object after its node publication has
        // retired. Exact callback-null Dispatch/Batch calls skip that old node;
        // keep their full physical scopes without reacquiring its generation.
        if(record.Retired&&allowRetiredCallbackNull&&!record.FieldsCleared&&callbackCleared)return null;
        if(record.Retired||!record.Committed||!cleared&&!callbackCleared&&!SameTuple(record.Tuple,tuple,record.OwnerRevoked))throw new InvalidOperationException("Late or changed wait tuple cannot borrow an old publication generation.");return record;
    }
    private static bool SameTuple(NativeEngineWaitTuple expected,NativeEngineWaitTuple actual,bool ownerRevoked=false)
        =>expected.Callback==actual.Callback&&expected.Cleanup==actual.Cleanup&&expected.Payload==actual.Payload
            &&(expected.OwnerId==actual.OwnerId||ownerRevoked&&actual.OwnerId==0)&&actual.Reserved==0;
    private Publication? NewPublication(NativeEngineWaitPhaseFrame phase)
    {
        if(!_identities.TryGetSchedulerNode(phase.Node.ToInt64(),out var identity,out var node))
        {if(_nodes.Read().Nodes.Any(value=>value.Node==phase.Node.ToInt64()))throw new InvalidOperationException("Wait publication cannot borrow a historical owned node generation.");return null;}
        if(phase.Wait==IntPtr.Zero||phase.Node==IntPtr.Zero||_records.Count==MaximumPublications||!HasSnapshot(phase.Flags))throw new InvalidOperationException("Wait publication has no valid actual node snapshot/capacity.");
        RequireNode(phase.Node,phase.NodeOwner,phase.EnumeratorHandle,phase.EnumeratorCache,phase.EnumeratorType,phase.NodeSnapshotFlags,node!);
        // A Yield must descend from the actual currently bound Execute/Start;
        // a retained payload pointer alone cannot manufacture a publication.
        Context? parent=_top?.Parent;while(parent is not null&&((uint)parent.Frame.Kind<3||(uint)parent.Frame.Kind>6))parent=parent.Parent;
        if(parent?._nodeCall is not{} actual||!_nodes.IsCurrentCall(actual)&&actual.Frame.Kind!=NativeEngineNodeKind.Start
            ||actual.Frame.Kind is not(NativeEngineNodeKind.Start or NativeEngineNodeKind.Execute)
            ||actual.Frame.Kind==NativeEngineNodeKind.Execute&&!ReferenceEquals(actual.Node,node)
            ||actual.Frame.Kind==NativeEngineNodeKind.Start&&!actual.StartedNodes.Contains(node!))throw new InvalidOperationException("Yield publication lacks its actual bound node producer.");
        if(_latest.TryGetValue(phase.Wait.ToInt64(),out var old)&&!old.Retired&&!(old.FieldsCleared&&old.CallbackCleared&&old.ReferenceReturned&&old.NodeWaitCleared))throw new InvalidOperationException("A live wait address was republished without its own complete withdrawal.");
        var record=new Publication(checked(++_generation),phase,identity!,node!);_records.Add(record);_latest[record.Wait]=record;return record;
    }
    internal void ObservePhase(Context call,NativeEngineWaitObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess();
        try
        {
            if(!Exact(call,receipt)||receipt.Phase is not{} phase||receipt.Completion is not null||Unknown||phase.HasFault)throw new InvalidOperationException("Wait phase lacks its actual current ledger delivery.");
            var start=phase.Kind is NativeEngineWaitPhase.PublishBegin or NativeEngineWaitPhase.DetachOwnerBegin or NativeEngineWaitPhase.DetachNestedOwnerBegin
                or NativeEngineWaitPhase.ReleaseClearBegin or NativeEngineWaitPhase.StartClearBegin or NativeEngineWaitPhase.DispatchOwnerResult or NativeEngineWaitPhase.BatchObjectBegin;
            if(!call._operations.TryGetValue(phase.OperationSequence,out var operation))
            {if(!start)throw new InvalidOperationException("Wait operation reached an end before its real Begin.");operation=new(phase);call._operations.Add(phase.OperationSequence,operation);BeginOperation(call,operation,phase);}
            else AdvanceOperation(call,operation,phase);
            operation.Last=phase;
        }
        catch(Exception error){call.Failed=true;Fault(error.Message,call.Frame,receipt.Phase);}
    }
    private void BeginOperation(Context call,Operation operation,NativeEngineWaitPhaseFrame phase)
    {
        switch(phase.Kind)
        {
            case NativeEngineWaitPhase.PublishBegin:
                if(call.Frame.Kind!=NativeEngineWaitKind.Yield||phase.Node!=call.Frame.Arg0||!phase.StageFlags.HasFlag(NativeEngineWaitStageFlags.BeforeWrite))throw new InvalidOperationException("Wait publication Begin is not its actual Yield source.");
                operation.Record=NewPublication(phase);break;
            case NativeEngineWaitPhase.DetachOwnerBegin:case NativeEngineWaitPhase.DetachNestedOwnerBegin:
            case NativeEngineWaitPhase.ReleaseClearBegin:case NativeEngineWaitPhase.StartClearBegin:
                var kind=phase.Kind==NativeEngineWaitPhase.StartClearBegin?NativeEngineWaitKind.NodeStart:
                    phase.Kind==NativeEngineWaitPhase.ReleaseClearBegin?NativeEngineWaitKind.NodeRelease:NativeEngineWaitKind.NodeDetach;
                if(call.Frame.Kind!=kind||!phase.StageFlags.HasFlag(NativeEngineWaitStageFlags.BeforeWrite))
                    throw new InvalidOperationException("Wait withdrawal Begin lacks the actual bound node call.");
                operation.Record=Find(phase.Wait,phase.Tuple);if(operation.Record is{} withdrawn)
                {if(call._nodeCall is not{} actual||!_nodes.IsCurrentCall(actual)||phase.Kind!=NativeEngineWaitPhase.DetachNestedOwnerBegin&&!ReferenceEquals(actual.Node,withdrawn.Node)&&!(kind==NativeEngineWaitKind.NodeStart&&actual.StartedNodes.Contains(withdrawn.Node)))throw new InvalidOperationException("Wait withdrawal belongs to another node generation.");RequireNode(phase.Node,phase.NodeOwner,phase.EnumeratorHandle,phase.EnumeratorCache,phase.EnumeratorType,phase.NodeSnapshotFlags,withdrawn.Node);}
                break;
            case NativeEngineWaitPhase.DispatchOwnerResult:
                if(call.Frame.Kind!=NativeEngineWaitKind.Dispatch||phase.Wait!=call.Frame.Wait||phase.OperationSequence!=call.Frame.OperationSequence)throw new InvalidOperationException("Wait owner lookup crossed dispatch object.");
                operation.Record=call._dispatch;operation.OwnerResult=true;operation.Owner=phase.ActualOwner;
                if(phase.ActualOwner==IntPtr.Zero&&!phase.StageFlags.HasFlag(NativeEngineWaitStageFlags.OwnerNull))throw new InvalidOperationException("Null wait owner lacks its actual lookup outcome.");
                break;
            case NativeEngineWaitPhase.BatchObjectBegin:
                if(call.Frame.Kind!=NativeEngineWaitKind.Batch||phase.Manager!=call.Frame.Arg0||!HasSnapshot(phase.Flags))throw new InvalidOperationException("Wait batch object lacks its actual manager and Before snapshot.");
                if(phase.ActualCallback!=phase.Tuple.Callback||phase.ActualCleanup!=phase.Tuple.Cleanup||phase.ActualPayload!=phase.Tuple.Payload
                    ||phase.Tuple.Callback==IntPtr.Zero&&((phase.Flags&NativeEngineWaitFlags.CleanupExpected)!=0||phase.NodeSnapshotFlags!=0))
                    throw new InvalidOperationException("Wait Batch must retain its exact copied tuple without reading a callback-null node.");
                operation.Record=Find(phase.Wait,phase.Tuple,phase.Tuple.Callback==IntPtr.Zero);operation.Cleanup=phase.ActualCleanup;operation.Payload=phase.ActualPayload;
                operation.Callback=phase.ActualCallback;operation.CleanupDecision=phase.Tuple.Callback!=IntPtr.Zero;
                if(operation.Record is{} batched)
                {++batched.Dispatches;if((phase.Flags&NativeEngineWaitFlags.CleanupExpected)!=0)RequireNode(phase.Node,phase.NodeOwner,phase.EnumeratorHandle,phase.EnumeratorCache,phase.EnumeratorType,phase.NodeSnapshotFlags,batched.Node);
                    else if(phase.NodeSnapshotFlags!=0)throw new InvalidOperationException("A callback-null Batch must not borrow a live node snapshot.");}
                break;
            default:throw new InvalidOperationException("Unrecognized wait operation Begin.");
        }
    }
    private void AdvanceOperation(Context call,Operation operation,NativeEngineWaitPhaseFrame phase)
    {
        if(operation.Ended||operation.Wait!=phase.Wait||operation.Node!=IntPtr.Zero&&phase.Node!=operation.Node)throw new InvalidOperationException("Wait operation identity drifted or repeated after End.");
        switch(phase.Kind)
        {
            case NativeEngineWaitPhase.OldReferenceDecision:
                if(operation.Begin!=NativeEngineWaitPhase.PublishBegin||operation.Decision)throw new InvalidOperationException("Wait old-reference decision is out of order.");
                operation.Decision=true;operation.OldWait=phase.OldWait;
                if(phase.OldWait!=IntPtr.Zero&&_latest.TryGetValue(phase.OldWait.ToInt64(),out var old))
                {if(old.Retired||operation.Record is null||!ReferenceEquals(old.Node,operation.Record.Node))throw new InvalidOperationException("Old wait reference cannot borrow another publication/node.");operation.Old=old;}
                break;
            case NativeEngineWaitPhase.OldReferenceReturned:
                if(!operation.Decision||operation.ReferenceReturned||phase.OldWait!=operation.OldWait
                    ||(phase.OldWait==IntPtr.Zero?!phase.StageFlags.HasFlag(NativeEngineWaitStageFlags.ZeroOldReference):!phase.StageFlags.HasFlag(NativeEngineWaitStageFlags.SharedReferenceReturned)))
                    throw new InvalidOperationException("Wait old-reference return lacks its exact decision and cached identity.");
                operation.ReferenceReturned=true;break;
            case NativeEngineWaitPhase.PublishCommitted:
                if(operation.Begin!=NativeEngineWaitPhase.PublishBegin||!operation.ReferenceReturned||operation.Committed
                    ||!phase.StageFlags.HasFlag(NativeEngineWaitStageFlags.Committed)||operation.Record is{} published&&(phase.Tuple.Callback!=_callback||phase.Tuple.Cleanup!=_cleanup
                    ||phase.Tuple.Payload.ToInt64()!=published.Node.Pointer))throw new InvalidOperationException("Wait publication commit lacks its four actual stages and exact owned tuple.");
                if(operation.Record is{} committed)committed.Tuple=phase.Tuple;operation.Committed=true;break;
            case NativeEngineWaitPhase.ReleaseFieldsCleared:case NativeEngineWaitPhase.StartFieldsCleared:
                if(phase.Kind!=(operation.Begin==NativeEngineWaitPhase.ReleaseClearBegin?NativeEngineWaitPhase.ReleaseFieldsCleared:NativeEngineWaitPhase.StartFieldsCleared)
                    ||operation.FieldsCleared||!phase.Tuple.Empty||!phase.StageFlags.HasFlag(NativeEngineWaitStageFlags.FieldsWritten))throw new InvalidOperationException("Wait fields-cleared stage lacks its exact clear Begin.");
                operation.FieldsCleared=true;break;
            case NativeEngineWaitPhase.ReleaseReferenceReturned:case NativeEngineWaitPhase.StartReferenceReturned:
                if(!operation.FieldsCleared||operation.ReferenceReturned||!phase.StageFlags.HasFlag(NativeEngineWaitStageFlags.SharedReferenceReturned)
                    ||phase.Kind!=(operation.Begin==NativeEngineWaitPhase.ReleaseClearBegin?NativeEngineWaitPhase.ReleaseReferenceReturned:NativeEngineWaitPhase.StartReferenceReturned))
                    throw new InvalidOperationException("Wait shared reference return lacks its completed clear stage.");operation.ReferenceReturned=true;break;
            case NativeEngineWaitPhase.DispatchCallbackArmed:
                if(operation.Begin!=NativeEngineWaitPhase.DispatchOwnerResult||!operation.OwnerResult||operation.Owner==IntPtr.Zero||operation.CallbackArmed
                    ||phase.ActualOwner!=operation.Owner||operation.Record is{} armed&&(phase.ActualCallback!=_callback||phase.ActualPayload!=armed.Tuple.Payload))
                    throw new InvalidOperationException("Wait callback Armed lacks its exact actual lookup/tuple.");
                operation.CallbackArmed=true;operation.Callback=phase.ActualCallback;operation.Payload=phase.ActualPayload;break;
            case NativeEngineWaitPhase.DispatchCleanupDecision:
                if(operation.Begin!=NativeEngineWaitPhase.DispatchOwnerResult||operation.CleanupDecision||operation.Record is not null&&operation.CallbackArmed&&!operation.CallbackDone
                    ||!HasSnapshot(phase.Flags)||phase.ActualCleanup!=phase.Tuple.Cleanup||phase.ActualPayload!=phase.Tuple.Payload)
                    throw new InvalidOperationException("Wait cleanup decision is not the post-callback actual re-read.");
                if(operation.Record is{} known)
                {
                    if(known.FieldsCleared&&phase.Tuple.Empty)operation.CleanupRecord=known;
                    else
                    {
                        var cleanupRecord=Find(phase.Wait,phase.Tuple);
                        if(cleanupRecord is null||!cleanupRecord.Committed||phase.ActualCleanup!=IntPtr.Zero&&phase.ActualCleanup!=_cleanup
                            ||phase.ActualPayload.ToInt64()!=cleanupRecord.Node.Pointer)throw new InvalidOperationException("Wait callback changed cleanup/payload without an observed owned publication.");
                        RequireNode(phase.Node,phase.NodeOwner,phase.EnumeratorHandle,phase.EnumeratorCache,phase.EnumeratorType,phase.NodeSnapshotFlags,cleanupRecord.Node);
                        operation.CleanupRecord=cleanupRecord;if(!ReferenceEquals(cleanupRecord,known))++cleanupRecord.Dispatches;
                    }
                }
                operation.CleanupDecision=true;operation.Cleanup=phase.ActualCleanup;operation.Payload=phase.ActualPayload;break;
            case NativeEngineWaitPhase.DispatchClearReached:
                if(operation.Begin!=NativeEngineWaitPhase.DispatchOwnerResult||!operation.CleanupDecision||operation.ClearReached
                    ||operation.Record is not null&&operation.Cleanup!=IntPtr.Zero&&!operation.CleanupDone||!phase.StageFlags.HasFlag(NativeEngineWaitStageFlags.CallbackSlotClearReached))
                    throw new InvalidOperationException("Wait callback-slot clear lacks its completed actual cleanup/dispatch stages.");operation.ClearReached=true;break;
            case NativeEngineWaitPhase.BatchObjectEnd:
                if(phase.Manager!=operation.Last.Manager||phase.Index!=operation.Last.Index)throw new InvalidOperationException("Wait batch End crossed its cached manager/object index.");
                if(operation.Begin!=NativeEngineWaitPhase.BatchObjectBegin||operation.Record is not null&&operation.CleanupDecision&&operation.Cleanup!=IntPtr.Zero&&!operation.CleanupDone
                    ||!phase.StageFlags.HasFlag(NativeEngineWaitStageFlags.SharedReferenceReturned)||operation.Callback!=IntPtr.Zero&&!phase.StageFlags.HasFlag(NativeEngineWaitStageFlags.CallbackSlotClearReached))
                    throw new InvalidOperationException("Wait batch End lacks per-object cleanup, clear and cached reference return.");operation.Ended=true;break;
            default:throw new InvalidOperationException("Wait operation has an unexpected or duplicate phase.");
        }
    }
    internal sealed class CallbackContext:IDisposable
    {
        internal readonly NativeEngineWaitLedger Owner;internal readonly NativeEngineWaitObserverDispatcher.CallbackClaim Claim;
        internal readonly Context _boundary;internal readonly Operation _operation;internal bool _completed,_disposed;internal ulong _raw;internal uint _externalFlags;
        internal CallbackContext(NativeEngineWaitLedger owner,object issuer,NativeEngineWaitObserverDispatcher.CallbackClaim claim,Context boundary,object operation)
        {if(!ReferenceEquals(issuer,owner._issuer)||operation is not Operation actual)throw new InvalidOperationException("Wait callback issuer differs.");(Owner,Claim,_boundary,_operation)=(owner,claim,boundary,actual);}
        internal void Complete(NativeEngineSchedulerObserverDispatcher.Observation receipt)=>Owner.CompleteCallback(this,receipt);
        public void Dispose()=>Owner.DisposeCallback(this);
    }
    // Root passes this private route to the old row ledger so the same real
    // M callback remains a mask without inventing a scheduler Consume source.
    internal CallbackContext? TryClaimCallback(NativeEngineSchedulerObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess();var claim=_dispatcher.TryClaimCallback(receipt);if(claim is null)return null;
        try
        {
            var boundary=RequireBoundary();var source=boundary.Parent;
            if(source is null||!ReferenceEquals(source.Scope,claim.Source)||!source._operations.TryGetValue(claim.Frame.OperationSequence,out var operation))
                throw new InvalidOperationException("Wait callback private source lacks its exact operation.");
            if(operation.Record is not{} record)return null;
            RequireNode(claim.Frame.Node,claim.Frame.NodeOwner,claim.Frame.EnumeratorHandle,claim.Frame.EnumeratorCache,claim.Frame.EnumeratorType,claim.Frame.NodeSnapshotFlags,record.Node);
            if(operation.CallbackContext is not null||operation.CallbackDone||record.Retired
                ||claim.Frame.Node.ToInt64()!=record.Node.Pointer||claim.Frame.Arg1.ToInt64()!=record.Node.Pointer)
                throw new InvalidOperationException("Wait callback private source has no exact retained publication operation.");
            var context=new CallbackContext(this,_issuer,claim,boundary,operation);operation.CallbackContext=context;boundary._callback=context;++record.Callbacks;return context;
        }
        catch(Exception error){Fault(error.Message,claim.Frame);return null;}
    }
    private void CompleteCallback(CallbackContext context,NativeEngineSchedulerObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess();try
        {if(!ReferenceEquals(context.Owner,this)||context._completed||context._disposed||!context.Claim.IsFor(receipt)||receipt.Completion is not{} completion||!completion.Normal
                ||!ReferenceEquals(_top,context._boundary))throw new InvalidOperationException("Wait callback completion lacks its exact actual M Complete.");
            context._completed=true;context._raw=completion.RawRax;context._externalFlags=(uint)completion.Flags;}
        catch(Exception error){Fault(error.Message,context.Claim.Frame);}
    }
    private void DisposeCallback(CallbackContext context)
    {
        _main.VerifyAccess();try
        {if(!ReferenceEquals(context.Owner,this)||!context._completed||context._disposed||!ReferenceEquals(_top,context._boundary)||!context.Claim.IsCurrent)
                throw new InvalidOperationException("Wait callback Dispose lacks its completed actual M scope.");context._disposed=true;}
        catch(Exception error){Fault(error.Message,context.Claim.Frame);}
    }
    internal sealed class CleanupContext:IDisposable
    {
        internal readonly NativeEngineWaitLedger _owner;internal readonly Context _boundary;internal readonly Operation _operation;
        internal readonly NativeEngineCoroutineNodeLedger.Call _call;internal bool _completed,_disposed;
        internal CleanupContext(NativeEngineWaitLedger owner,object issuer,Context boundary,object operation,NativeEngineCoroutineNodeLedger.Call call)
        {if(!ReferenceEquals(issuer,owner._issuer)||operation is not Operation actual)throw new InvalidOperationException("Wait cleanup issuer differs.");(_owner,_boundary,_operation,_call)=(owner,boundary,actual,call);}
        internal void Complete()=>_owner.CompleteCleanup(this);public void Dispose()=>_owner.DisposeCleanup(this);
    }
    internal CleanupContext? CaptureCleanup(NativeEngineCoroutineNodeLedger.Call call)
    {
        _main.VerifyAccess();ArgumentNullException.ThrowIfNull(call);var boundary=_top;
        if(boundary is null||boundary.Frame.Kind!=NativeEngineWaitKind.NodeRelease||(boundary.Frame.Flags&(NativeEngineWaitFlags.SourceDispatch|NativeEngineWaitFlags.SourceBatch))==0)return null;
        try
        {
            BindNodeCall(call);boundary=RequireBoundary();var source=boundary.Parent;
            if(source is null||source.Frame.Sequence!=boundary.Frame.SourceSequence||!source._operations.TryGetValue(boundary.Frame.OperationSequence,out var operation))
                throw new InvalidOperationException("Wait cleanup private source lacks its exact operation.");
            if((operation.CleanupRecord??operation.Record) is not{} record)return null;
            var expectedSource=(boundary.Frame.Flags&NativeEngineWaitFlags.SourceDispatch)!=0?NativeEngineWaitKind.Dispatch:NativeEngineWaitKind.Batch;
            if(source.Frame.Kind!=expectedSource)throw new InvalidOperationException("Wait cleanup cannot borrow another physical source kind.");
            if(!operation.CleanupDecision||operation.CleanupDone||operation.CleanupContext is not null||operation.Cleanup!=_cleanup
                ||!ReferenceEquals(call.Node,record.Node)||operation.Payload.ToInt64()!=call.Node.Pointer||boundary.Frame.Arg0.ToInt64()!=call.Node.Pointer
                ||boundary.Frame.Target!=_cleanup||call.Frame.Caller!=boundary.Frame.Caller)
                throw new InvalidOperationException("Wait cleanup source lacks its actual current node Release and exact post-callback tuple.");
            var context=new CleanupContext(this,_issuer,boundary,operation,call);operation.CleanupContext=context;boundary._cleanup=context;++record.Cleanups;return context;
        }
        catch(Exception error){Fault(error.Message,boundary.Frame);return null;}
    }
    private void CompleteCleanup(CleanupContext context)
    {
        _main.VerifyAccess();try
        {var call=context._call;if(Unknown||context._completed||context._disposed||!ReferenceEquals(_top,context._boundary)||!ReferenceEquals(call.Owner,_nodes)
                ||!call.Completed||call.Disposed||call.Frame.Kind!=NativeEngineNodeKind.Release||!call.Completion.IsValidFor(call.Frame.Kind)
                ||call.Completion.Flags!=(NativeEngineNodeExitFlags.Returned|NativeEngineNodeExitFlags.ReturnByteValid))
                throw new InvalidOperationException("Wait cleanup Complete must follow the actual identity/Release Complete exactly once.");context._completed=true;}
        catch(Exception error){Fault(error.Message,context._boundary.Frame);}
    }
    private void DisposeCleanup(CleanupContext context)
    {
        _main.VerifyAccess();try
        {if(Unknown||!context._completed||context._disposed||!context._call.Disposed||!ReferenceEquals(_top,context._boundary))
                throw new InvalidOperationException("Wait cleanup Dispose must follow the same actual identity/Release Dispose.");context._disposed=true;}
        catch(Exception error){Fault(error.Message,context._boundary.Frame);}
    }
    internal void Complete(Context call,NativeEngineWaitObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess();try
        {
            if(!Exact(call,receipt)||call.Completed||call.Disposed||receipt.Completion is not{} completion||receipt.Phase is not null)throw new InvalidOperationException("Wait Complete lacks its exact private receipt.");
            call.Completed=true;call.Completion=completion;if(Unknown||!completion.IsNormalFor(call.Frame.Kind))throw new InvalidOperationException("Wait original exited abnormally or with missing phases.");
            if((uint)call.Frame.Kind>=3&&(uint)call.Frame.Kind<=6&&call._nodeCall is not null)
            {
                if(call._nodeCall is not{} node||!node.Completed||!node.Disposed||completion.ExternalFlags!=(uint)node.Completion.Flags
                    ||completion.ExternalRawReturn!=node.Completion.RawReturn||completion.OutputNode!=node.Completion.StartOutputNode
                    ||completion.OutputFlags!=(uint)node.Completion.OutputFlags)throw new InvalidOperationException("Wait node completion preceded or differs from the actual typed Complete/Dispose.");
            }
            if(call._callback is{} callback)
            {if(!callback._completed||!callback._disposed||completion.RawRax!=callback._raw||completion.ExternalFlags!=callback._externalFlags
                    ||completion.ExternalRawReturn!=unchecked((uint)callback._raw)||completion.OutputFlags!=0||completion.OutputNode!=IntPtr.Zero)throw new InvalidOperationException("Wait callback side Exit preceded actual M Complete/Dispose.");}
            if(call._cleanup is{} cleanup&&(!cleanup._completed||!cleanup._disposed))throw new InvalidOperationException("Wait cleanup side Exit preceded actual Release Complete/Dispose.");
            foreach(var operation in call._operations.Values)ValidateOperation(call,operation);
        }
        catch(Exception error){call.Failed=true;Fault(error.Message,call.Frame);}
    }
    private void ValidateOperation(Context call,Operation operation)
    {
        switch(operation.Begin)
        {
            case NativeEngineWaitPhase.PublishBegin:if(!operation.Decision||!operation.ReferenceReturned||!operation.Committed)throw new InvalidOperationException("Partial wait publication retains its responsibility.");break;
            case NativeEngineWaitPhase.ReleaseClearBegin:case NativeEngineWaitPhase.StartClearBegin:
                if(!operation.FieldsCleared||!operation.ReferenceReturned)throw new InvalidOperationException("Partial wait withdrawal retains its shared-reference duty.");break;
            case NativeEngineWaitPhase.DispatchOwnerResult:
                if(!operation.CleanupDecision||!operation.ClearReached||operation.Record is not null&&(operation.CallbackArmed&&!operation.CallbackDone||operation.Cleanup!=IntPtr.Zero&&!operation.CleanupDone))
                    throw new InvalidOperationException("Wait Dispatch lacks callback, re-read cleanup or callback-slot clear completion.");break;
            case NativeEngineWaitPhase.BatchObjectBegin:if(!operation.Ended)throw new InvalidOperationException("Wait Batch object lacks its own cached End.");break;
        }
    }
    private bool Exact(Context call,NativeEngineWaitObserverDispatcher.Observation receipt)=>ReferenceEquals(call.Owner,this)&&_calls.Contains(call)
        &&ReferenceEquals(_top,call)&&ReferenceEquals(call.Scope,receipt.Scope)&&receipt.IsCurrent&&call.Frame.Equals(receipt.Frame);
    private void Release(Context call)
    {
        _main.VerifyAccess();
        if(!ReferenceEquals(call.Owner,this)||!_calls.Contains(call)||!ReferenceEquals(_top,call)||!call.Completed||call.Disposed)
        {Fault("Wait Dispose lacks its completed exact physical context.",call.Frame);return;}
        call.Disposed=true;_top=call.Parent;
        if(Unknown||call.Failed)return;
        if(call._callback is{} callback){callback._operation.CallbackDone=true;--callback._operation.Record!.Callbacks;}
        if(call._cleanup is{} cleanup){cleanup._operation.CleanupDone=true;--(cleanup._operation.CleanupRecord??cleanup._operation.Record)!.Cleanups;}
        foreach(var operation in call._operations.Values)
        {
            var record=operation.Record;if(record is null)continue;
            switch(operation.Begin)
            {
                case NativeEngineWaitPhase.PublishBegin:
                    record.Committed=true;
                    if(operation.Old is{} old){old.ReferenceReturned=true;old.NodeWaitCleared=true;TryRetire(old);}break;
                case NativeEngineWaitPhase.DetachOwnerBegin:case NativeEngineWaitPhase.DetachNestedOwnerBegin:record.OwnerRevoked=true;break;
                case NativeEngineWaitPhase.ReleaseClearBegin:case NativeEngineWaitPhase.StartClearBegin:
                    record.OwnerRevoked=true;record.FieldsCleared=true;record.CallbackCleared=true;record.ReferenceReturned=true;record.NodeWaitCleared=true;break;
                case NativeEngineWaitPhase.DispatchOwnerResult:
                    record.CallbackCleared=true;
                    if(operation.CleanupRecord is{} cleanupRecord&&!ReferenceEquals(cleanupRecord,record))
                    {cleanupRecord.CallbackCleared=true;--cleanupRecord.Dispatches;TryRetire(cleanupRecord);}break;
                case NativeEngineWaitPhase.BatchObjectBegin:if(operation.Callback!=IntPtr.Zero)record.CallbackCleared=true;--record.Dispatches;break;
            }
            TryRetire(record);
        }
        if(call._dispatch is{} dispatched){--dispatched.Dispatches;TryRetire(dispatched);}
        // Successful scopes relinquish their physical slots. The separate
        // publication records still guard generation and retirement.
        _calls.Remove(call);
    }
    private static void TryRetire(Publication record){if(record.MayRetire)record.Retired=true;}
    internal bool MayRetireNodeGeneration(NativeEngineCoroutineIdentitySnapshot selected)
    {
        _main.VerifyAccess();ArgumentNullException.ThrowIfNull(selected);if(Unknown||selected.Node==0||selected.EngineGeneration<=0||selected.StartSequence==0)return false;
        foreach(var record in _records)if(record.Node.Pointer==selected.Node&&record.Node.Generation==selected.EngineGeneration&&record.Node.Start.Frame.Sequence==selected.StartSequence)
            if(!record.Retired||record.Identity.Id!=selected.IdentityId||!ReferenceEquals(record.Identity.Node,record.Node))return false;return true;
    }
    internal void ObserveFault(NativeEngineWaitFrame frame,string reason)=>Fault(reason,frame);

    private void Fault(string reason, NativeEngineWaitFrame frame, NativeEngineWaitPhaseFrame? phase = null)
    {
        if (Volatile.Read(ref _failure) is not null) return;
        var cause = _nodes.FirstFailure ?? _identities.FirstFailure ?? reason;
        if (cause.Length > 512) cause = cause[..512];
        var context = frame.Sequence == 0 ? string.Empty
            : $" Wait {frame.Kind} sequence={frame.Sequence}, parent={frame.ParentSequence}, external={frame.ExternalSequence}, caller=0x{frame.Caller:x}.";
        if (phase is { } source)
            context += $" Phase={source.Kind}, operation={source.OperationSequence}, event={source.EventSequence}.";
        var failure = cause + context;
        if (Interlocked.CompareExchange(ref _failure, failure, null) is null)
            _reportFailure?.Invoke(failure);
    }

    internal NativeEngineWaitLedgerSnapshot Read()
    {
        _main.VerifyAccess();
        var outstanding = _calls.Count(value => !value.Completed || !value.Disposed) + _untracked;
        var unknown = Unknown;
        return new(_capturedCalls, outstanding, unknown, unknown || outstanding != 0 || _records.Any(value => !value.Retired),
            false, false, FirstFailure, _records.Select(value => value.Read(unknown)).ToArray());
    }
}
