using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeEngineSchedulerRowState { Registered, ConsumedPendingCallback, CallbackOpen, CleanupPending, CleanupOpen, DrainOpen, Retired, Unknown }
internal readonly record struct NativeEngineSchedulerCallerProfile(IntPtr PumpCallback,IntPtr RepeatCallback,IntPtr DrainCallback,
    IntPtr RemoveCleanup,IntPtr PumpCleanup,IntPtr DrainCleanup);
internal sealed record NativeEngineSchedulerRowSnapshot(long RegistrationGeneration,long NodeGeneration,ulong StartSequence,
    long Tree,long Manager,long Entry,long Payload,NativeEngineSchedulerRowState State,ulong ConsumeSequence,
    ulong CallbackRawRax,bool CleanupCompleted,bool RepeatCopy,bool NativeReferenceAdded);
internal sealed record NativeEngineSchedulerLedgerSnapshot(int Calls,int Outstanding,int Rows,bool Unknown,bool Pending,
    bool EngineCancelled,bool FutureClosed,string? Failure,IReadOnlyList<NativeEngineSchedulerRowSnapshot> Records);

// Tracks queue ownership separately from callback/cleanup duty. The only node
// resolver is the existing exclusive actual identity ledger. Retired records
// keep copied identities; no provider or native pointer is read at retirement.
internal sealed class NativeEngineSchedulerRowLedger
{
    internal const int MaximumCalls=2048,MaximumRows=512;
    private readonly IMainThreadDispatcher _main;
    private readonly NativeEngineCoroutineIdentityLedger _identities;
    private readonly NativeEngineCoroutineNodeLedger _nodes;
    private readonly NativeEngineSchedulerObserverDispatcher _dispatcher;
    private readonly IntPtr _callback,_cleanup;
    private readonly NativeEngineSchedulerCallerProfile _callers;
    private readonly Func<NativeEngineSchedulerObserverDispatcher.Observation, NativeEngineWaitLedger.CallbackContext?>? _waitCallback;
    private readonly object _cleanupIssuer=new();
    private readonly List<Context> _calls=new(); private readonly List<RowRecord> _rows=new();
    private int _capturedCalls;
    private readonly Dictionary<(long Tree,long Entry),RowRecord> _latest=new();
    private Context? _top; private long _generation; private int _unknown,_untracked; private string? _failure;
    internal bool Unknown=>Volatile.Read(ref _unknown)!=0 || _identities.Unknown || _nodes.Unknown || _dispatcher.Unknown;
    internal NativeEngineSchedulerRowLedger(IMainThreadDispatcher main,NativeEngineCoroutineIdentityLedger identities,
        NativeEngineCoroutineNodeLedger nodes,NativeEngineSchedulerObserverDispatcher dispatcher,IntPtr callback,IntPtr cleanup,
        NativeEngineSchedulerCallerProfile callers,
        Func<NativeEngineSchedulerObserverDispatcher.Observation, NativeEngineWaitLedger.CallbackContext?>? waitCallback = null)
    {
        ArgumentNullException.ThrowIfNull(main);main.VerifyAccess();ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(nodes);ArgumentNullException.ThrowIfNull(dispatcher);
        if(!identities.Wraps(nodes)||callback==IntPtr.Zero||cleanup==IntPtr.Zero||callback==cleanup
            ||new[]{callers.PumpCallback,callers.RepeatCallback,callers.DrainCallback,callers.RemoveCleanup,callers.PumpCleanup,callers.DrainCleanup}.Any(value=>value==IntPtr.Zero))
            throw new ArgumentException("Scheduler rows require the actual exclusive node identity owner and distinct callback/cleanup targets.");
        (_main,_identities,_nodes,_dispatcher,_callback,_cleanup,_callers)=(main,identities,nodes,dispatcher,callback,cleanup,callers);
        _waitCallback=waitCallback;
    }
    internal sealed class RowRecord
    {
        internal readonly long Generation; internal readonly NativeEngineCoroutineIdentityLedger.IdentityRecord Identity;
        internal readonly NativeEngineCoroutineNodeLedger.NodeRecord Node;
        internal readonly long Tree,Entry; internal long Manager;
        internal NativeEngineSchedulerRecord Record;
        internal NativeEngineSchedulerRowState State=NativeEngineSchedulerRowState.Registered;
        internal Context? Consume,Callback,Root; internal RowRecord? Copy;
        internal Context? Registration,CopyPump;
        internal bool RepeatCopy,CleanupCompleted,RetireCandidate;
        internal ulong CallbackRaw;
        internal RowRecord(long generation,NativeEngineCoroutineIdentityLedger.IdentityRecord identity,
            NativeEngineCoroutineNodeLedger.NodeRecord node,NativeEngineSchedulerFrame frame,long entry)
            =>(Generation,Identity,Node,Tree,Entry,Record)=(generation,identity,node,frame.Tree.ToInt64(),entry,frame.Record);
        internal NativeEngineSchedulerRowSnapshot Read(bool unknown)=>new(Generation,Node.Generation,Node.Start.Frame.Sequence,
            Tree,Manager,Entry,Node.Pointer,unknown&&State!=NativeEngineSchedulerRowState.Retired?NativeEngineSchedulerRowState.Unknown:State,
            Consume?.Frame.Sequence??0,CallbackRaw,CleanupCompleted,RepeatCopy,false);
    }
    internal sealed class Context:IDisposable
    {
        internal readonly NativeEngineSchedulerRowLedger Owner;
        internal readonly NativeEngineSchedulerObserverDispatcher.ScopeProof Scope;
        internal readonly NativeEngineSchedulerFrame Frame;
        internal readonly Context? Parent;
        internal RowRecord? _row;
        internal readonly List<RowRecord> _retire=new();
        internal readonly Dictionary<ulong,RowRecord> _pending=new();
        internal NativeEngineSchedulerRowFrame? _drainMask;
        internal RowRecord? _drainRow;
        internal NativeEngineCoroutineIdentityLedger.IdentityRecord? RegisterIdentity;
        internal NativeEngineCoroutineNodeLedger.NodeRecord? RegisterNode;
        internal bool Completed,Disposed,Failed;
        internal NativeEngineSchedulerCompletion Completion;
        internal NativeEngineWaitLedger.CallbackContext? WaitCallback;
        internal Context(NativeEngineSchedulerRowLedger owner,NativeEngineSchedulerObserverDispatcher.Observation receipt)
            =>(Owner,Scope,Frame,Parent)=(owner,receipt.Scope,receipt.Frame,owner._top);
        public void Dispose()=>Owner.Release(this);
    }
    internal Context Capture(NativeEngineSchedulerObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess();ArgumentNullException.ThrowIfNull(receipt);
        var call=new Context(this,receipt);
        if(_calls.Count==MaximumCalls){Interlocked.Increment(ref _untracked);Fault("Scheduler ledger call capacity exhausted.");return call;}
        _calls.Add(call);_top=call; // Foreign calls are retained physical masks too.
        if (_capturedCalls < int.MaxValue) ++_capturedCalls;
        try
        {
            if(!receipt.IsCurrent || receipt.Completion is not null || receipt.Row is not null || Unknown
                || call.Frame.HasFault || !call.Frame.IsValid || !ReferenceEquals(call.Scope.Parent,call.Parent?.Scope))
                throw new InvalidOperationException("Scheduler ledger Enter lacks its exact current physical receipt and parent mask.");
            var frame=call.Frame;
            switch(frame.Kind)
            {
                case NativeEngineSchedulerKind.Register:
                    // Freeze the existing actual node/identity references at
                    // registration entry; never resolve a reused address at Exit.
                    if(IsOwnedTuple(frame.Record,out var identity,out var node))
                    {call.RegisterIdentity=identity;call.RegisterNode=node;if(!HasRecord(frame.Flags)||!MatchesNode(frame,node!))throw new InvalidOperationException("Owned scheduler registration snapshot differs from its exact node generation.");}
                    break;
                case NativeEngineSchedulerKind.Remove:
                case NativeEngineSchedulerKind.Consume:
                    if(frame.Arg0!=frame.Manager||frame.Arg2!=frame.Entry||frame.SourceRecord.ToInt64()!=checked(frame.Entry.ToInt64()+0x20)
                        ||frame.Tree.ToInt64()!=checked(frame.Manager.ToInt64()+0x30)
                        ||frame.Kind==NativeEngineSchedulerKind.Remove&&frame.Arg1!=frame.SourceRecord)
                        throw new InvalidOperationException("Scheduler erase input does not match its actual manager/entry/record arguments.");
                    call._row=Find(frame.Tree,frame.Entry,frame.Record);
                    if(call._row is null)break;
                    if(!HasRecord(frame.Flags)||!MatchesNode(frame,call._row.Node))throw new InvalidOperationException("Scheduler row entry snapshot differs from its frozen node generation.");
                    if(call._row.Registration is not {Completed:true,Disposed:true})throw new InvalidOperationException("Scheduler row registration physical scope is still open.");
                    BindManager(call._row,frame.Manager);
                    if(frame.Kind==NativeEngineSchedulerKind.Remove)
                    {if(call._row.State!=NativeEngineSchedulerRowState.Registered)throw new InvalidOperationException("Remove cannot borrow an already consumed or open row duty.");call._row.Root=call;call._row.State=NativeEngineSchedulerRowState.CleanupPending;}
                    else Consume(call,call._row);
                    break;
                case NativeEngineSchedulerKind.Callback:
                    // Its private wait proof selects that duty while this M
                    // context remains an independent physical mask.
                    call.WaitCallback=_waitCallback?.Invoke(receipt);
                    if(call.WaitCallback is null)CaptureCallback(call);
                    break;
            }
        }
        catch(Exception error){call.Failed=true;Fault(error.Message);}
        return call;
    }
    private bool IsOwnedTuple(NativeEngineSchedulerRecord record,out NativeEngineCoroutineIdentityLedger.IdentityRecord? identity,
        out NativeEngineCoroutineNodeLedger.NodeRecord? node)
    {
        if(!_identities.TryGetSchedulerNode(record.Payload.ToInt64(),out identity,out node))
        {
            if(record.Payload!=IntPtr.Zero&&_nodes.Read().Nodes.Any(value=>value.Node==record.Payload.ToInt64()))
                throw new InvalidOperationException("A historical owned node address cannot lend a retired generation to a new scheduler row.");
            return false;
        }
        if(record.Callback!=_callback || record.Cleanup!=_cleanup)
            throw new InvalidOperationException("Known node was published with an unproved scheduler callback or cleanup target.");
        return true;
    }
    private static bool HasRecord(NativeEngineSchedulerFlags flags)=>(flags&(NativeEngineSchedulerFlags.SnapshotRequested|NativeEngineSchedulerFlags.SnapshotValid))
        ==(NativeEngineSchedulerFlags.SnapshotRequested|NativeEngineSchedulerFlags.SnapshotValid) && (flags&(NativeEngineSchedulerFlags.SnapshotFaulted|NativeEngineSchedulerFlags.Unknown))==0;
    private static bool SameTuple(NativeEngineSchedulerRecord left,NativeEngineSchedulerRecord right)=>left.Payload==right.Payload
        && left.Callback==right.Callback && left.Cleanup==right.Cleanup && left.Repeat==right.Repeat
        && left.OwnerId==right.OwnerId && left.Category==right.Category;
    private bool MatchesNode(NativeEngineSchedulerFrame frame,NativeEngineCoroutineNodeLedger.NodeRecord node)
        =>_nodes.IsLatest(node)&&!node.FinalCleanup&&frame.Record.Payload.ToInt64()==node.Pointer&&frame.NodeSnapshotFlags==3
            &&frame.EnumeratorHandle==node.Handle&&frame.EnumeratorCache.ToInt64()==node.Enumerator&&frame.EnumeratorType==2
            &&(frame.NodeOwner.ToInt64()==node.NativeOwner||frame.NodeOwner==IntPtr.Zero&&node.DetachObserved);
    private bool MatchesNode(NativeEngineSchedulerRowFrame frame,NativeEngineCoroutineNodeLedger.NodeRecord node)
        =>_nodes.IsLatest(node)&&!node.FinalCleanup&&frame.Record.Payload.ToInt64()==node.Pointer&&frame.NodeSnapshotFlags==3
            &&frame.EnumeratorHandle==node.Handle&&frame.EnumeratorCache.ToInt64()==node.Enumerator&&frame.EnumeratorType==2
            &&(frame.NodeOwner.ToInt64()==node.NativeOwner||frame.NodeOwner==IntPtr.Zero&&node.DetachObserved);
    private RowRecord? Find(IntPtr tree,IntPtr entry,NativeEngineSchedulerRecord record)
    {
        if(_latest.TryGetValue((tree.ToInt64(),entry.ToInt64()),out var row))
        {
            if(row.State==NativeEngineSchedulerRowState.Retired || !SameTuple(row.Record,record))
                throw new InvalidOperationException("Late or changed scheduler entry cannot borrow an old registration generation.");
            return row;
        }
        if(IsOwnedTuple(record,out _,out _))throw new InvalidOperationException("Owned scheduler entry has no actual observed registration generation.");
        return null;
    }
    private static void BindManager(RowRecord row,IntPtr manager)
    {if(manager==IntPtr.Zero || row.Manager!=0&&row.Manager!=manager.ToInt64())throw new InvalidOperationException("Scheduler row manager drifted.");row.Manager=manager.ToInt64();}
    private void Consume(Context call,RowRecord row)
    {
        var pump=call.Parent;
        if(row.State!=NativeEngineSchedulerRowState.Registered || pump?.Frame.Kind!=NativeEngineSchedulerKind.Pump
            || !ReferenceEquals(call.Scope.Parent,pump.Scope) || pump.Frame.Arg0!=call.Frame.Manager || call.Frame.SourceRecord.ToInt64()!=checked(row.Entry+0x20))
            throw new InvalidOperationException("Owned Consume has no exact current Pump and registered row.");
        if(row.Record.Repeat!=0 && (row.Copy is null||!ReferenceEquals(row.CopyPump,pump)||row.Copy.Registration is not {Completed:true,Disposed:true}))
            throw new InvalidOperationException("Repeat Consume has no earlier actual completed Register copy in this exact Pump.");
        if(row.Record.Repeat==0 && !row.Record.Equals(call.Frame.Record))throw new InvalidOperationException("Nonrepeat row changed before actual consumption.");
        row.Consume=call;row.Root=pump;row.State=NativeEngineSchedulerRowState.ConsumedPendingCallback;
        pump._pending.Add(call.Frame.Sequence,row);call._row=row;
    }
    private void CaptureCallback(Context call)
    {
        var frame=call.Frame;var parent=call.Parent;RowRecord? row=null;
        if((frame.Flags&NativeEngineSchedulerFlags.SourceConsume)!=0 && parent?.Frame.Kind==NativeEngineSchedulerKind.Pump
            && parent._pending.TryGetValue(frame.SourceSequence,out row))
        {
            if(row.Consume is not {Completed:true,Disposed:true} || row.State!=NativeEngineSchedulerRowState.ConsumedPendingCallback
                || (frame.Flags&NativeEngineSchedulerFlags.SnapshotCached)==0||!HasRecord(frame.Flags)
                || frame.Tree.ToInt64()!=row.Tree||frame.Manager.ToInt64()!=row.Manager||frame.Entry.ToInt64()!=row.Entry
                || frame.SourceRecord.ToInt64()!=checked(row.Entry+0x20)
                || frame.Caller!=(row.Record.Repeat!=0?_callers.RepeatCallback:_callers.PumpCallback))
                throw new InvalidOperationException("Scheduler callback did not follow its exact normal consumed generation.");
        }
        else if((frame.Flags&NativeEngineSchedulerFlags.SourceDrainRow)!=0 && parent?.Frame.Kind==NativeEngineSchedulerKind.Drain
            && parent._drainMask?.RowSequence==frame.SourceSequence)
        {
            row=parent._drainRow;
            if(row is not null && (frame.Caller!=_callers.DrainCallback || row.State!=NativeEngineSchedulerRowState.DrainOpen||(row.Record.Category&0x40)==0))
                throw new InvalidOperationException("Drain callback crossed its exact current row or caller.");
        }
        if(row is null)
        {
            if(_identities.TryGetSchedulerNode(frame.Arg1.ToInt64(),out _,out _))throw new InvalidOperationException("Known node callback has no exact Consume/Drain-row source.");
            return;
        }
        if(!_nodes.IsLatest(row.Node)||row.Node.FinalCleanup||frame.Arg1.ToInt64()!=row.Node.Pointer || frame.Record.Payload.ToInt64()!=row.Node.Pointer
            || frame.NodeSnapshotFlags!=3 || frame.EnumeratorHandle!=row.Node.Handle
            || frame.EnumeratorCache.ToInt64()!=row.Node.Enumerator || frame.EnumeratorType!=2
            || frame.NodeOwner.ToInt64()!=row.Node.NativeOwner&&!(frame.NodeOwner==IntPtr.Zero&&row.Node.DetachObserved) || frame.Record.Callback!=_callback
            || (frame.Flags&NativeEngineSchedulerFlags.SourceConsume)!=0&&!SameTuple(row.Record,frame.Record))
            throw new InvalidOperationException("Callback native node/cache differs from its frozen scheduler row generation.");
        call._row=row;row.Callback=call;row.State=NativeEngineSchedulerRowState.CallbackOpen;
    }
    internal void Complete(Context call,NativeEngineSchedulerObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess();
        try
        {
            if(!Exact(call)||call.Completed||!receipt.IsCurrent||!ReferenceEquals(receipt.Scope,call.Scope)||receipt.Completion is not { } completion)
                throw new InvalidOperationException("Scheduler Complete has no exact current retained physical context.");
            call.Completed=true;call.Completion=completion;
            if(!completion.Normal || call._drainMask is not null || call.Failed || Unknown)throw new InvalidOperationException("Scheduler native original/row completion is unconfirmed.");
            if(call.WaitCallback is{} waitCallback)
            {
                waitCallback.Complete(receipt);
                if(waitCallback.Owner.Unknown)throw new InvalidOperationException("The shared callback wait duty did not confirm its actual M completion.");
            }
            if(call.Frame.Kind==NativeEngineSchedulerKind.Register)Register(call,completion);
            else if(call._row is { } row && call.Frame.Kind==NativeEngineSchedulerKind.Callback)
            {
                row.CallbackRaw=completion.RawRax;
                row.State=row.Record.Repeat==0 && parentPump(call) && unchecked((uint)completion.RawRax)==1
                    ?NativeEngineSchedulerRowState.CleanupPending:NativeEngineSchedulerRowState.ConsumedPendingCallback;
            }
        }
        catch(Exception error){call.Failed=true;Fault(error.Message);}
        static bool parentPump(Context context)=>context.Parent?.Frame.Kind==NativeEngineSchedulerKind.Pump;
    }
    private void Register(Context call,NativeEngineSchedulerCompletion completion)
    {
        var frame=call.Frame;
        var identity=call.RegisterIdentity;var node=call.RegisterNode;
        if(identity is null||node is null)return;
        if(!_nodes.IsLatest(node)||node.FinalCleanup)throw new InvalidOperationException("Registration completed after its exact native node generation retired.");
        if(!HasRecord(frame.Flags)||frame.Tree==IntPtr.Zero||frame.SourceRecord!=frame.Arg2||frame.OutputSlot!=frame.Arg1
            || completion.OutputFlags!=3||completion.OutputEntry==IntPtr.Zero||completion.RawRax!=unchecked((ulong)frame.OutputSlot.ToInt64()))
            throw new InvalidOperationException("Register lacks the actual normal output-slot pointer/entry receipt.");
        var key=(frame.Tree.ToInt64(),completion.OutputEntry.ToInt64());
        if(_rows.Count==MaximumRows||_latest.TryGetValue(key,out var old)&&old.State!=NativeEngineSchedulerRowState.Retired)
            throw new InvalidOperationException("Scheduler registration capacity exhausted or a live entry address was reused.");
        var row=new RowRecord(checked(++_generation),identity!,node!,frame,key.Item2){Registration=call};_rows.Add(row);_latest[key]=row;
        if(call.Parent?.Frame.Kind==NativeEngineSchedulerKind.Pump)
        {
            var copies=_rows.Where(value=>!ReferenceEquals(value,row)&&value.State==NativeEngineSchedulerRowState.Registered
                && value.Tree==row.Tree&&value.Record.Repeat!=0&&frame.SourceRecord.ToInt64()==checked(value.Entry+0x20)&&SameTuple(value.Record,frame.Record)).ToArray();
            if(copies.Length==1){if(copies[0].Copy is not null)throw new InvalidOperationException("Repeat row copied more than once before consumption.");copies[0].Copy=row;copies[0].CopyPump=call.Parent;row.RepeatCopy=true;}
        }
    }
    internal void ObserveRow(Context call,NativeEngineSchedulerObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess();
        try
        {
            if(!Exact(call)||call.Frame.Kind!=NativeEngineSchedulerKind.Drain||!receipt.IsCurrent||!ReferenceEquals(receipt.Scope,call.Scope)
                ||receipt.Row is not { } frame||frame.HasFault)throw new InvalidOperationException("Drain row lacks its exact physical Drain receipt.");
            if(frame.Kind==NativeEngineSchedulerRowKind.Begin)
            {
                if(call._drainMask is not null)throw new InvalidOperationException("Drain row mask overlapped.");
                call._drainMask=frame;call._drainRow=Find(frame.Tree,frame.Entry,frame.Record);
                if(call._drainRow is { } row)
                {BindManager(row,frame.Manager);if(row.State!=NativeEngineSchedulerRowState.Registered||row.Registration is not {Completed:true,Disposed:true}
                    ||!HasRecord(frame.Flags)||!MatchesNode(frame,row.Node))throw new InvalidOperationException("Drain row is no longer completely registered with its exact node snapshot.");row.Root=call;row.State=NativeEngineSchedulerRowState.DrainOpen;}
            }
            else
            {
                if(call._drainMask is not { } begin||begin.RowSequence!=frame.RowSequence||begin.Entry!=frame.Entry)
                    throw new InvalidOperationException("Drain row End borrowed another row mask.");
                if(call._drainRow is { } row)
                {if(!row.CleanupCompleted||row.Callback is {Disposed:false})throw new InvalidOperationException("Drain row End precedes actual callback/cleanup disposal.");row.RetireCandidate=true;call._retire.Add(row);}
                call._drainMask=null;call._drainRow=null;
            }
        }
        catch(Exception error){call.Failed=true;Fault(error.Message);}
    }
    internal sealed class CleanupContext:IDisposable
    {
        private readonly NativeEngineSchedulerRowLedger _owner; private readonly Context _root; private readonly RowRecord _row;
        private readonly NativeEngineCoroutineNodeLedger.Call _native;
        private bool _completed,_disposed;
        private CleanupContext(NativeEngineSchedulerRowLedger owner,Context root,RowRecord row,NativeEngineCoroutineNodeLedger.Call native)
            =>(_owner,_root,_row,_native)=(owner,root,row,native);
        internal void Complete()
        {
            _owner._main.VerifyAccess();
            if(_completed||_disposed||!_owner.Exact(_root)||!ReferenceEquals(_native.Owner,_owner._nodes)||!_native.Completed||_native.Disposed
                ||!_native.Completion.IsValidFor(NativeEngineNodeKind.Release)
                ||_native.Completion.Flags!=(NativeEngineNodeExitFlags.Returned|NativeEngineNodeExitFlags.ReturnByteValid))
            { _owner.Fault("Scheduler cleanup lacks actual normal native Release completion.");return; }
            _completed=true;
        }
        public void Dispose()
        {
            _owner._main.VerifyAccess();
            if(!_completed||_disposed||!_native.Disposed||!_owner.Exact(_root)||_owner.Unknown)
            {_owner.Fault("Scheduler cleanup lacks actual Release Dispose/current parent.");return;}
            _disposed=true;_row.CleanupCompleted=true;_row.State=NativeEngineSchedulerRowState.CleanupPending;
            if(_root.Frame.Kind!=NativeEngineSchedulerKind.Drain){_row.RetireCandidate=true;_root._retire.Add(_row);}
        }
        internal static CleanupContext Create(NativeEngineSchedulerRowLedger owner,object issuer,Context root,object row,NativeEngineCoroutineNodeLedger.Call native)
        {
            if(!ReferenceEquals(issuer,owner._cleanupIssuer))throw new InvalidOperationException("Cleanup context issuer differs from its actual row ledger.");
            return new(owner,root,(RowRecord)row,native);
        }
    }
    internal CleanupContext? CaptureCleanup(NativeEngineCoroutineNodeLedger.Call actualNodeCall)
    {
        _main.VerifyAccess();ArgumentNullException.ThrowIfNull(actualNodeCall);
        try
        {
            var scope=_dispatcher.CurrentOriginalScope;var root=_top;
            if(root is null||scope is null||!ReferenceEquals(scope,root.Scope))return null;
            if(!ReferenceEquals(actualNodeCall.Owner,_nodes)||!_nodes.IsCurrentCall(actualNodeCall)||actualNodeCall.Frame.Kind!=NativeEngineNodeKind.Release)
                throw new InvalidOperationException("Scheduler cleanup was given a foreign/copied/completed native node call.");
            RowRecord? row=null;IntPtr caller=IntPtr.Zero;
            if(root.Frame.Kind==NativeEngineSchedulerKind.Remove){row=root._row;caller=_callers.RemoveCleanup;}
            else if(root.Frame.Kind==NativeEngineSchedulerKind.Drain){row=root._drainRow;caller=_callers.DrainCleanup;
                if(root._drainMask?.RowSequence!=scope.CurrentRow?.RowSequence)throw new InvalidOperationException("Drain cleanup crossed its actual row mask.");}
            else if(root.Frame.Kind==NativeEngineSchedulerKind.Pump)
            {
                var pending=root._pending.Values.Where(value=>value.Callback is {Completed:true,Disposed:true}
                    && value.Record.Repeat==0&&unchecked((uint)value.CallbackRaw)==1&&!value.CleanupCompleted).ToArray();
                if(pending.Length==1)row=pending[0];else if(pending.Length>1)throw new InvalidOperationException("Pump cleanup has ambiguous consumed rows.");caller=_callers.PumpCleanup;
            }
            if(row is null)return null;
            // A recursive Release inside the real cleanup original has the
            // same scheduler parent but a different native caller. It keeps
            // its own node-ledger duty and must not become a second row receipt.
            if(caller==IntPtr.Zero||actualNodeCall.Frame.Caller!=caller)return null;
            if(Unknown||actualNodeCall.Frame.Node.ToInt64()!=row.Node.Pointer
                ||!ReferenceEquals(actualNodeCall.Node,row.Node)||row.CleanupCompleted||row.State==NativeEngineSchedulerRowState.CleanupOpen
                ||actualNodeCall.Frame.EnumeratorHandle!=row.Node.Handle)
                throw new InvalidOperationException("Cleanup native call differs from its exact row generation, caller or path.");
            row.State=NativeEngineSchedulerRowState.CleanupOpen;return CleanupContext.Create(this,_cleanupIssuer,root,row,actualNodeCall);
        }
        catch(Exception error){Fault(error.Message);return null;}
    }
    private bool Exact(Context call)=>ReferenceEquals(call.Owner,this)&&ReferenceEquals(_top,call)&&!call.Disposed&&_calls.Contains(call)&&call.Scope.IsCurrent;
    private void Release(Context call)
    {
        _main.VerifyAccess();
        if(!Exact(call)||!call.Completed){Fault("Scheduler Dispose lacks exact completed physical context.");return;}
        if(!Unknown&&!call.Failed)
        {
            if(call.Frame.Kind==NativeEngineSchedulerKind.Callback&&call._row is { } callbackRow)
            {
                if(call.Parent?.Frame.Kind==NativeEngineSchedulerKind.Pump)
                {
                    if(callbackRow.Record.Repeat!=0||unchecked((uint)callbackRow.CallbackRaw)!=1)
                    {callbackRow.RetireCandidate=true;call.Parent._retire.Add(callbackRow);}
                }
                else callbackRow.State=NativeEngineSchedulerRowState.DrainOpen;
            }
            if(call.Frame.Kind==NativeEngineSchedulerKind.Remove&&call._row is { } removed&&!removed.CleanupCompleted)
                Fault("Remove returned without its actual cleanup completion/disposal.");
            if(call.Frame.Kind==NativeEngineSchedulerKind.Pump&&call._pending.Values.Any(row=>!row.RetireCandidate))
                Fault("Pump returned with a consumed callback/cleanup obligation still pending.");
            if(!Unknown)foreach(var row in call._retire.Distinct())if(row.RetireCandidate)row.State=NativeEngineSchedulerRowState.Retired;
        }
        call.Disposed=true;
        try
        {
            call.WaitCallback?.Dispose();
            if(call.WaitCallback?.Owner.Unknown==true)Fault("The shared callback wait duty did not confirm its actual M disposal.");
            // Rows keep the exact registration, consume and pump receipts they
            // need. Only open or failed physical scopes need a ledger slot.
            if (!Unknown && !call.Failed) _calls.RemoveAt(_calls.Count - 1);
        }
        finally{_top=call.Parent;}
    }
    internal void ObserveFault(NativeEngineSchedulerFrame frame,string reason){_=frame;Fault(reason);}
    private void Fault(string reason){Interlocked.CompareExchange(ref _failure,reason,null);Interlocked.Exchange(ref _unknown,1);}
    // Local observed-row readiness only. Other node generations do not lend
    // or withhold this record's receipt; absence of a row is not future-source
    // closure and does not authorize cancellation or bypass runtime idle gates.
    internal bool MayRetireNodeGeneration(NativeEngineCoroutineIdentitySnapshot selected)
    {
        _main.VerifyAccess();ArgumentNullException.ThrowIfNull(selected);
        if(Unknown||selected.Node==0||selected.EngineGeneration<=0||selected.StartSequence==0)return false;
        foreach(var row in _rows)
        {
            if(row.Node.Pointer!=selected.Node||row.Node.Generation!=selected.EngineGeneration
                ||row.Node.Start.Frame.Sequence!=selected.StartSequence)continue;
            if(row.State!=NativeEngineSchedulerRowState.Retired||row.Identity.Id!=selected.IdentityId
                ||row.Identity.StartSequence!=selected.StartSequence||!ReferenceEquals(row.Identity.Node,row.Node))return false;
        }
        return true;
    }
    internal NativeEngineSchedulerLedgerSnapshot Read()
    {
        _main.VerifyAccess();var outstanding=_untracked+_calls.Count(value=>!value.Disposed);var active=_rows.Count(row=>row.State!=NativeEngineSchedulerRowState.Retired);
        return new(_capturedCalls,outstanding,_rows.Count,Unknown,Unknown||active!=0||outstanding!=0,false,false,_failure,
            Array.AsReadOnly(_rows.Select(row=>row.Read(Unknown)).ToArray()));
    }
}
