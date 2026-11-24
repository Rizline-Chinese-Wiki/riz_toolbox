using System.Runtime.InteropServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Controlled scalar bridges exercise the actual private dispatcher and actual
// identity/node/wait ledgers. These are not native or production admission tests.
internal static class NativeEngineWaitTests
{
    internal static readonly(string Name,Action Test)[] Cases={
        ("Wait independent scalar layouts and source offsets match ABI1",Layout),
        ("Wait four-phase publication remains a per-node obligation without Unknown",Publication),
        ("Wait partial publication and missing old-reference stage retain duty",PartialPublication),
        ("Wait owner null still requires real cleanup and outer Dispatch Dispose",OwnerNullCleanup),
        ("Wait releases completed physical masks while a Dispatch still owns cleanup",CompletedPhysicalMasks),
        ("Wait reports its first phase or typed node failure before later observer faults",FirstFailure),
        ("Wait Detach revokes only owner and preserves callback reference responsibilities",Detach),
        ("Wait old reference return does not clear old publication callback",OldReference),
        ("Wait Release requires fields reference return and real outer node completion",ReleaseStages),
        ("Wait batch objects retain independent cleanup and cached End obligations",Batch),
        ("Wait retired publication permits only exact callback-null batch object release",RetiredBatch),
        ("Wait late callback-null Dispatch preserves its physical scope without borrowing a retired node",RetiredDispatch),
        ("Wait copied receipts foreign scope and observer reentry cannot borrow phases",PhysicalMasks),
        ("Wait configured identity and worker mismatch fail closed",Information),
        ("Wait shared callback claim requires exact actual M source and full lifecycle",Callback),
        ("Wait callback observed field clearing permits the actual empty reread",CallbackClearsFields),
        ("Wait shared callback cannot steal a scheduler Consume source",ConflictingCallbackSource),
        ("Wait callback payload mutation without observed writer remains Unknown",ChangedTuple),
        ("Wait callback actual new publication routes cleanup to its distinct node generation",RepublishedCleanup),
        ("WaitUntil child binds only its physical parent Yield and retains independent cleanup", WaitUntilChild),
        ("WaitUntil child rejects wrong source frame caller duplicate and Current", WaitUntilChildRejections),
        ("Wait Start cleanup waits for exact typed Complete and Dispose",StartClear),
        ("Wait abnormal Release and missing batch End preserve obligations",Abnormal)
    };
    private static void Check(bool value,string reason="Controlled wait regression failed."){if(!value)throw new InvalidOperationException(reason);}
    private sealed class Logger:ILogger{public void Log(LogLevel level,string message,Exception? error=null){}}
    private sealed class Bridge:INativeEngineWaitContextBridge
    {
        internal Func<NativeEngineWaitFrame,nuint> Enter=null!;internal Action<NativeEngineWaitFrame,nuint,NativeEngineWaitCompletion> Exit=null!;
        internal Action<NativeEngineWaitPhaseFrame> Phase=null!;public NativeEngineWaitFamilyIdentity Identity{get;}
        internal Bridge()
        {Identity=new(3,17,NativeEngineInlinePlatform.CurrentOsThreadId,Enumerable.Range(0,13).Select(i=>new IntPtr(0x10000+i*0x100)).ToArray(),
            Enumerable.Range(0,3).Select(i=>new IntPtr(0x20000+i*0x100)).ToArray(),Enumerable.Range(0,3).Select(i=>new IntPtr(0x30000+i*0x100)).ToArray(),new(0x40000),
            Enumerable.Range(0,18).Select(i=>new NativeEngineWaitSite{SitePc=new(0x50000+i*0x10),RelayReturnPc=new(0x60000+i*0x10)}).ToArray());}
        public void Attach(Func<NativeEngineWaitFrame,nuint> enter,Action<NativeEngineWaitFrame,nuint,NativeEngineWaitCompletion> exit,Action<NativeEngineWaitPhaseFrame> phase)
        {Check(Enter is null);(Enter,Exit,Phase)=(enter,exit,phase);}
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()=>new(0,true);
    }
    private sealed class MBridge:INativeEngineSchedulerContextBridge
    {
        internal Func<NativeEngineSchedulerFrame,nuint> Enter=null!;internal Action<NativeEngineSchedulerFrame,nuint,NativeEngineSchedulerCompletion> Exit=null!;
        public NativeEngineSchedulerChannelIdentity Identity{get;}
        internal MBridge(int kind,IntPtr callback)
        {Identity=new((NativeEngineSchedulerKind)kind,2,(ulong)kind+20,NativeEngineInlinePlatform.CurrentOsThreadId,
            kind==5?callback:new(0x70000+kind*0x100),new(0x80000+kind*0x100),new(0x90000+kind*0x100),new(0xa0000),
            kind==4?new(){SitePc=new(0xb0000),RelayReturnPc=new(0xc0000)}:default,
            kind==4?new(){SitePc=new(0xb0010),RelayReturnPc=new(0xc0010)}:default);}
        public void Attach(Func<NativeEngineSchedulerFrame,nuint> enter,Action<NativeEngineSchedulerFrame,nuint,NativeEngineSchedulerCompletion> exit,Action<NativeEngineSchedulerRowFrame>? row)
        {Check(Enter is null);(Enter,Exit)=(enter,exit);}
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()=>new(0,true);
    }
    private sealed record Physical(NativeEngineWaitFrame Frame,nuint Token);
    private sealed class F:IDisposable
    {
        internal const long Node=301,Owner=0x12340000,Wait=701;
        internal readonly MainThreadDispatcher Main=new();internal readonly Bridge Bridge=new();
        internal readonly NativeEngineWaitObserverDispatcher Dispatcher;internal readonly NativeEngineCoroutineNodeLedger Nodes;
        internal readonly NativeEngineCoroutineIdentityLedger Identities;internal readonly NativeEngineWaitLedger Ledger;
        internal readonly NativeEngineWaitObserverDispatcher.Binding Binding;internal readonly List<Physical> Open=new();
        internal NativeEngineWaitObserverDispatcher.Observation? LastReceipt;
        internal readonly object Tracker=new();internal int ProviderReads;
        internal readonly NativeCoroutineCancellationIdentityProvider Provider;
        private readonly List<NativeCoroutineCancellationIdentity> _providerRows=new();
        private long _currentNode=Node,_currentEnumerator=1001;
        private ulong _sequence,_nodeSequence,_event,_operation=100,_mSequence;
        internal F(bool startClearsWait=false, NativeLocalInitializationCoroutineTracker? initialization=null, Action<string>? reportFailure=null)
        {
            _providerRows.Add(new(Tracker,101,0,1,1001,0,false,false,true,false,false,true));
            Provider=initialization is null ? new(Tracker,101,
                ()=>{++ProviderReads;return _providerRows.ToArray();},value=>_providerRows.Any(row=>row.SameRecord(value)))
                : new(initialization,initialization.ControllerIdentity,initialization.ReadCancellationIdentities,initialization.IsCurrentCancellationIdentity);
            Nodes=new(Main);Identities=new(Main,Nodes,new[]{Provider},new[]{new NativeEngineCoroutineOwnerPair(101,Owner)});
            Dispatcher=new(Bridge,new Logger());Ledger=new(Main,Identities,Nodes,Dispatcher,reportFailure);
            Binding=Dispatcher.Bind(receipt=>{LastReceipt=receipt;return Ledger.Capture(receipt);},Ledger.Complete,Ledger.ObservePhase,Ledger.ObserveFault);
            if (initialization is not null)
            {
                _currentEnumerator=1002;
                Identities.BindWaitUntilYieldSource(Ledger,initialization);
                return;
            }
            var start=BeginNode(NativeEngineNodeKind.Start);var execute=BeginNode(NativeEngineNodeKind.Execute);
            EndNode(execute);
            if(startClearsWait){PublishOnCurrentNode();Clear(start);}
            EndNode(start,output:startClearsWait?0:Node);
        }
        internal void SetNode(long node, long enumerator) => (_currentNode, _currentEnumerator)=(node,enumerator);
        internal NativeEngineWaitTuple Tuple(long payload=0,uint owner=7)=>new(){Callback=Bridge.Identity.Target(NativeEngineWaitKind.SchedulerCallback),Cleanup=Bridge.Identity.Target(NativeEngineWaitKind.NodeRelease),Payload=new(payload==0?_currentNode:payload),OwnerId=owner};
        internal NativeEngineWaitFrame Frame(NativeEngineWaitKind kind,long wait=Wait,ulong external=0,ulong source=0,ulong operation=0,NativeEngineWaitFlags extra=0)
        {
            var parent=Open.LastOrDefault();var sequence=++_sequence;
            if(kind==NativeEngineWaitKind.Dispatch){extra|=NativeEngineWaitFlags.SnapshotRequested|NativeEngineWaitFlags.SnapshotValid;if(operation==0)operation=++_operation;}
            return new(){Version=1,Size=240,Kind=kind,Flags=NativeEngineWaitFlags.MainThread|NativeEngineWaitFlags.ParentMatched|extra,
                Slot=Bridge.Identity.Slot,ThreadId=Bridge.Identity.MainThreadId,Depth=(uint)Open.Count+1,Epoch=Bridge.Identity.Epoch,
                Sequence=sequence,ParentSequence=parent?.Frame.Sequence??0,RootSequence=parent?.Frame.RootSequence??sequence,ExternalSequence=external,
                SourceSequence=source,OperationSequence=operation,Target=Bridge.Identity.Target(kind),Caller=new(0xd000),Arg0=new(kind==NativeEngineWaitKind.Yield?_currentNode:wait),Wait=new(wait),
                Node=kind==NativeEngineWaitKind.Dispatch?IntPtr.Zero:new(_currentNode),NodeOwner=kind==NativeEngineWaitKind.Dispatch?IntPtr.Zero:new(Owner),
                EnumeratorHandle=kind==NativeEngineWaitKind.Dispatch?0:(ulong)(_currentEnumerator-600),EnumeratorCache=kind==NativeEngineWaitKind.Dispatch?IntPtr.Zero:new(_currentEnumerator),
                EnumeratorType=kind==NativeEngineWaitKind.Dispatch?0u:2u,ReferenceCount=kind==NativeEngineWaitKind.Dispatch?0:2,NodeSnapshotFlags=kind==NativeEngineWaitKind.Dispatch?0u:3u,Tuple=Tuple()};
        }
        internal Physical Begin(NativeEngineWaitFrame frame){var call=new Physical(frame,Bridge.Enter(frame));Open.Add(call);return call;}
        internal void End(Physical call,ulong raw=0,uint externalFlags=0,uint externalRaw=0,long output=0,uint outputFlags=0,bool abnormal=false)
        {Bridge.Exit(call.Frame,call.Token,new(){Version=1,Size=48,Flags=abnormal?NativeEngineWaitExitFlags.Abnormal:call.Frame.Kind==NativeEngineWaitKind.NodeDetach?NativeEngineWaitExitFlags.Returned:NativeEngineWaitExitFlags.Returned|NativeEngineWaitExitFlags.RawRaxValid,
            RawRax=raw,ExternalFlags=externalFlags,ExternalRawReturn=externalRaw,OutputNode=new(output),OutputFlags=outputFlags,Phases=(ulong)_phaseCounts.GetValueOrDefault(call.Frame.Sequence)});Open.Remove(call);}
        private readonly Dictionary<ulong,int> _phaseCounts=new();
        internal ulong Operation()=>Open.LastOrDefault() is{Frame.Kind:NativeEngineWaitKind.Dispatch} dispatch?dispatch.Frame.OperationSequence:++_operation;
        internal void Phase(Physical call,NativeEngineWaitPhase kind,ulong operation,long wait=Wait,NativeEngineWaitTuple? tuple=null,
            NativeEngineWaitStageFlags stage=0,long old=0,long owner=Owner,long cleanup=-1,long payload=-1)
        {
            var site=Bridge.Identity.Site(kind);var value=tuple??Tuple();
            var phase=new NativeEngineWaitPhaseFrame{Version=1,Size=288,Kind=kind,Flags=NativeEngineWaitFlags.MainThread|NativeEngineWaitFlags.ParentMatched|NativeEngineWaitFlags.SnapshotRequested|NativeEngineWaitFlags.SnapshotValid
                    |(kind==NativeEngineWaitPhase.BatchObjectBegin&&value.Callback!=IntPtr.Zero?NativeEngineWaitFlags.CleanupExpected:0),
                Slot=Bridge.Identity.Slot,ThreadId=Bridge.Identity.MainThreadId,Depth=call.Frame.Depth,ScopeKind=call.Frame.Kind,Epoch=Bridge.Identity.Epoch,Sequence=call.Frame.Sequence,
                ExternalSequence=call.Frame.ExternalSequence,ParentSequence=call.Frame.ParentSequence,RootSequence=call.Frame.RootSequence,OperationSequence=operation,EventSequence=++_event,
                SitePc=site.SitePc,RelayReturnPc=site.RelayReturnPc,Wait=new(wait),Node=kind==NativeEngineWaitPhase.DispatchOwnerResult?IntPtr.Zero:new(_currentNode),OldWait=new(old),ActualOwner=new(owner),
                ActualCallback=value.Callback,ActualCleanup=cleanup<0?value.Cleanup:new(cleanup),ActualPayload=new(payload<0?_currentNode:payload),Tuple=value,
                NodeOwner=new(Owner),EnumeratorHandle=(ulong)(_currentEnumerator-600),EnumeratorCache=new(_currentEnumerator),EnumeratorType=2,ReferenceCount=2,NodeSnapshotFlags=3,StageFlags=stage,OriginalCaller=call.Frame.Caller};
            if(kind==NativeEngineWaitPhase.DispatchOwnerResult||kind==NativeEngineWaitPhase.BatchObjectBegin&&value.Callback==IntPtr.Zero
                ||kind==NativeEngineWaitPhase.DispatchCleanupDecision&&phase.ActualCleanup==IntPtr.Zero)
            {phase.NodeOwner=IntPtr.Zero;phase.EnumeratorHandle=0;phase.EnumeratorCache=IntPtr.Zero;phase.EnumeratorType=0;phase.ReferenceCount=0;phase.NodeSnapshotFlags=0;
                if(kind==NativeEngineWaitPhase.DispatchCleanupDecision)phase.Node=phase.ActualPayload;}
            if(call.Frame.Kind==NativeEngineWaitKind.Batch){phase.Manager=call.Frame.Arg0;phase.Index=operation;}
            _phaseCounts[call.Frame.Sequence]=_phaseCounts.GetValueOrDefault(call.Frame.Sequence)+1;Bridge.Phase(phase);
        }
        internal (Physical Wait,NativeEngineCoroutineIdentityLedger.Call Identity) BeginNode(NativeEngineNodeKind kind,ulong source=0,ulong operation=0,
            NativeEngineWaitFlags flags=0,IntPtr? caller=null,long nameArgument=0)
        {
            var frame=new NativeEngineNodeFrame{Version=1,Size=112,Kind=kind,Sequence=++_nodeSequence,Caller=caller??new(0xd000)};
            if(kind==NativeEngineNodeKind.Start){frame.Arg0=new(Owner);frame.Arg1=new(_currentEnumerator);frame.Arg2=new(nameArgument);frame.Arg3=new(0xe000);}
            else{frame.Arg0=frame.Node=new(_currentNode);frame.Owner=new(Owner);frame.EnumeratorHandle=(ulong)(_currentEnumerator-600);frame.EnumeratorCache=new(_currentEnumerator);frame.EnumeratorType=2;frame.ReferenceCount=2;frame.SnapshotFlags=NativeEngineNodeSnapshotFlags.Requested|NativeEngineNodeSnapshotFlags.Valid;}
            var boundary=Frame((NativeEngineWaitKind)((uint)kind+3),external:frame.Sequence,source:source,operation:operation,extra:flags);
            boundary.Arg0=frame.Arg0;boundary.Arg1=frame.Arg1;boundary.Arg2=frame.Arg2;boundary.Arg3=frame.Arg3;boundary.Caller=frame.Caller;
            var physical=Begin(boundary);var actual=Identities.Capture(frame);Ledger.BindNodeCall(actual.NodeCall!);return(physical,actual);
        }
        internal void EndNode((Physical Wait,NativeEngineCoroutineIdentityLedger.Call Identity) call,uint raw=1,long output=0,NativeEngineWaitLedger.CleanupContext? cleanup=null,bool abnormal=false)
        {
            var kind=call.Identity.Frame.Kind;if(kind==NativeEngineNodeKind.Detach)raw=0;var done=new NativeEngineNodeCompletion{Version=1,Size=32,Flags=abnormal?NativeEngineNodeExitFlags.Abnormal:kind==NativeEngineNodeKind.Detach?NativeEngineNodeExitFlags.Returned:NativeEngineNodeExitFlags.Returned|NativeEngineNodeExitFlags.ReturnByteValid,
                RawReturn=raw,StartOutputNode=new(output),OutputFlags=kind==NativeEngineNodeKind.Start?NativeEngineNodeOutputFlags.Requested|NativeEngineNodeOutputFlags.Read:0};
            Identities.Complete(call.Identity,call.Identity.Frame,done);cleanup?.Complete();call.Identity.Dispose();cleanup?.Dispose();
            End(call.Wait,raw,(uint)done.Flags,done.RawReturn,output,(uint)done.OutputFlags,abnormal);
        }
        internal void Publish(long wait=Wait,long old=0,bool missing=false)
        {
            var execute=BeginNode(NativeEngineNodeKind.Execute);PublishOnCurrentNode(wait,old,missing);EndNode(execute);
        }
        private void PublishOnCurrentNode(long wait=Wait,long old=0,bool missing=false)
        {
            var yield=Begin(Frame(NativeEngineWaitKind.Yield,wait));var op=Operation();
            Phase(yield,NativeEngineWaitPhase.PublishBegin,op,wait,default(NativeEngineWaitTuple),NativeEngineWaitStageFlags.BeforeWrite);
            Phase(yield,NativeEngineWaitPhase.OldReferenceDecision,op,wait,old:old);
            if(!missing)Phase(yield,NativeEngineWaitPhase.OldReferenceReturned,op,wait,old:old,stage:old==0?NativeEngineWaitStageFlags.ZeroOldReference:NativeEngineWaitStageFlags.SharedReferenceReturned);
            Phase(yield,NativeEngineWaitPhase.PublishCommitted,op,wait,stage:NativeEngineWaitStageFlags.Committed|NativeEngineWaitStageFlags.FieldsWritten);
            End(yield);
        }
        internal void Clear((Physical Wait,NativeEngineCoroutineIdentityLedger.Call Identity) call,long wait=Wait,bool missing=false)
        {
            var op=Operation();var start=call.Identity.Frame.Kind==NativeEngineNodeKind.Start;
            Phase(call.Wait,start?NativeEngineWaitPhase.StartClearBegin:NativeEngineWaitPhase.ReleaseClearBegin,op,wait,stage:NativeEngineWaitStageFlags.BeforeWrite);
            Phase(call.Wait,start?NativeEngineWaitPhase.StartFieldsCleared:NativeEngineWaitPhase.ReleaseFieldsCleared,op,wait,default(NativeEngineWaitTuple),NativeEngineWaitStageFlags.FieldsWritten);
            if(!missing)Phase(call.Wait,start?NativeEngineWaitPhase.StartReferenceReturned:NativeEngineWaitPhase.ReleaseReferenceReturned,op,wait,default(NativeEngineWaitTuple),NativeEngineWaitStageFlags.SharedReferenceReturned|NativeEngineWaitStageFlags.BeforeWrite);
        }
        internal void NewNodeAndPublish()
        {
            _currentNode=302;_currentEnumerator=1002;_providerRows.Add(new(Tracker,101,1,1,_currentEnumerator,0,false,false,true,false,false,true));
            var start=BeginNode(NativeEngineNodeKind.Start);var execute=BeginNode(NativeEngineNodeKind.Execute);EndNode(execute);
            PublishOnCurrentNode();EndNode(start,output:_currentNode);
        }
        internal void Release(Physical source,ulong operation,long wait=Wait,bool clear=true,bool abnormal=false)
        {
            var flag=source.Frame.Kind==NativeEngineWaitKind.Batch?NativeEngineWaitFlags.SourceBatch:NativeEngineWaitFlags.SourceDispatch;
            var call=BeginNode(NativeEngineNodeKind.Release,source.Frame.Sequence,operation,flag);var cleanup=Ledger.CaptureCleanup(call.Identity.NodeCall!);Check(cleanup is not null);
            if(clear)Clear(call,wait);EndNode(call,clear?0u:1u,cleanup:cleanup,abnormal:abnormal);
        }
        internal void Callback(Physical dispatch,ulong operation,Action? original=null,bool conflicting=false)
        {
            var bridges=Enumerable.Range(0,6).Select(i=>new MBridge(i,Bridge.Identity.Target(NativeEngineWaitKind.SchedulerCallback))).ToArray();
            var m=new NativeEngineSchedulerObserverDispatcher(bridges,new Logger());
            var rows=new NativeEngineSchedulerRowLedger(Main,Identities,Nodes,m,Bridge.Identity.Target(NativeEngineWaitKind.SchedulerCallback),Bridge.Identity.Target(NativeEngineWaitKind.NodeRelease),
                new(new(0x9100),new(0x9110),new(0x9120),new(0x9200),new(0x9210),new(0x9220)),Ledger.TryClaimCallback);
            using var binding=m.Bind(rows.Capture,rows.Complete,rows.ObserveRow,rows.ObserveFault);
            var external=++_mSequence;var side=Frame(NativeEngineWaitKind.SchedulerCallback,external:external,source:dispatch.Frame.Sequence,operation:operation,extra:NativeEngineWaitFlags.SourceDispatch);
            side.Arg0=new(Owner);side.Arg1=new(Node);var call=Begin(side);var identity=bridges[5].Identity;
            var mf=new NativeEngineSchedulerFrame{Version=1,Size=256,Kind=NativeEngineSchedulerKind.Callback,Flags=NativeEngineSchedulerFlags.MainThread|NativeEngineSchedulerFlags.ParentMatched,
                Slot=identity.Slot,ThreadId=identity.MainThreadId,Depth=1,Epoch=identity.Epoch,Sequence=external,RootSequence=external,Arg0=side.Arg0,Arg1=side.Arg1,Caller=side.Caller};
            if(conflicting){mf.Flags|=NativeEngineSchedulerFlags.SourceConsume|NativeEngineSchedulerFlags.SnapshotCached;mf.SourceSequence=77;}
            mf.Record=new(){Payload=new(Node),Callback=Bridge.Identity.Target(NativeEngineWaitKind.SchedulerCallback),Cleanup=Bridge.Identity.Target(NativeEngineWaitKind.NodeRelease),OwnerId=7};
            mf.NodeOwner=new(Owner);mf.EnumeratorHandle=401;mf.EnumeratorCache=new(1001);mf.EnumeratorType=2;mf.ReferenceCount=2;mf.NodeSnapshotFlags=3;
            var token=bridges[5].Enter(mf);if(!conflicting)Check(!rows.Unknown&&rows.Read().Rows==0&&Ledger.Read().Records.Any(value=>value.OpenCallbacks==1));
            original?.Invoke();bridges[5].Exit(mf,token,new(){Version=1,Size=48,Flags=NativeEngineSchedulerExitFlags.Returned|NativeEngineSchedulerExitFlags.RawRaxValid,RawRax=0x10001});
            End(call,0x10001,externalFlags:9,externalRaw:0x10001);if(!conflicting)Check(!rows.Unknown&&rows.Read().Outstanding==0&&rows.Read().Rows==0);
        }
        public void Dispose(){foreach(var call in Open.AsEnumerable().Reverse().ToArray())End(call,abnormal:true);Binding.Dispose();Main.Dispose();}
    }
    private static void WaitUntilChild()
    {
        using var getter = new NativeLocalInitializationCoroutineRegression.Fixture();
        using var f = new F(initialization: getter.Owner);
        const long childNode = 302, childEnumerator = NativeLocalInitializationCoroutineRegression.Backend.WaitChild;
        var role = InitializationCoroutineRole.WaitUntil;
        getter.Backend.Start(role, () => {
            var start = f.BeginNode(NativeEngineNodeKind.Start);
            var execute = f.BeginNode(NativeEngineNodeKind.Execute, caller: f.Ledger.InitialExecuteCaller);
            getter.Backend.Move(role, 1, 1);
            var frame = f.Frame(NativeEngineWaitKind.Yield);
            frame.Caller = f.Ledger.CoroutineYieldCaller; frame.Arg1 = new(childEnumerator);
            var yield = f.Begin(frame);

            f.SetNode(childNode, childEnumerator);
            var child = f.BeginNode(NativeEngineNodeKind.Start, caller: f.Ledger.WaitUntilChildCaller);
            var source = child.Identity.Identity?.WaitUntilSource;
            Check(source is not null && source.ParentIdentityId == start.Identity.Identity!.Id
                && source.ParentExecuteSequence == execute.Identity.Frame.Sequence && source.YieldSequence == yield.Frame.Sequence);
            Check(getter.Backend.CurrentReads == 2 && child.Identity.Identity!.Provider is null);
            var childExecute = f.BeginNode(NativeEngineNodeKind.Execute, caller: f.Ledger.InitialExecuteCaller);
            f.EndNode(childExecute); f.EndNode(child, output: childNode);
            Check(f.Identities.TryGetSchedulerNode(childNode, out var identity, out var node) && identity!.Enumerator == childEnumerator
                && node!.Generation != execute.Identity.Identity!.Node!.Generation && !node.FinalCleanup);
            f.End(yield);
            f.SetNode(F.Node, 1002);
            f.EndNode(execute); f.EndNode(start, output: F.Node);
        });
        Check(getter.Backend.AfterError is null && !getter.Owner.Unknown && !f.Ledger.Unknown && !f.Identities.Unknown);
        getter.Backend.Move(role, -1, 0);
        f.Identities.RefreshTrackedIdentities();
        f.EndNode(f.BeginNode(NativeEngineNodeKind.Release), raw: 0);
        var parent = getter.Owner.ReadCancellationIdentities().Single();
        var childRecordChecks = 0;
        var retirement = NativeEngineCoroutineObserverRetirementAuthority.ForControlled(f.Main, f.Identities, new[] { f.Provider },
            () => new(0, true), () => !f.Ledger.Unknown, _ => throw new InvalidOperationException("Unexpected retirement fault."), selected => {
                if (selected.WaitUntilSource is not null) ++childRecordChecks;
                return f.Ledger.MayRetireNodeGeneration(selected);
            });
        Check(parent is { NaturalTerminal: true, StartReturned: true } && !retirement.TryBegin(parent, out _));
        Check(!NativeEngineCoroutineObserverRetirement.MayRetire(new(0, true), f.Identities.Read(), new[] { parent }));

        f.SetNode(childNode, childEnumerator);
        f.EndNode(f.BeginNode(NativeEngineNodeKind.Release), raw: 0);
        var final = f.Identities.Read(); var yielded = final.Identities.Single(identity => identity.WaitUntilSource is not null);
        Check(!f.Ledger.Unknown && !f.Identities.Unknown && final.Outstanding == 0 && final.Identities.Count == 2);
        Check(yielded.Enumerator == childEnumerator && yielded.Tracker.Enumerator == 0 && !yielded.Tracker.HandleConfirmed
            && yielded is { NativeStartReturned: true, StartDisposed: true } && !f.Identities.TryGetSchedulerNode(childNode, out _, out _));
        Check(NativeEngineCoroutineObserverRetirement.MayRetire(new(0, true), final, new[] { parent }));
        Check(retirement.TryBegin(parent, out _) && childRecordChecks > 0);
    }

    private static void WaitUntilChildRejections()
    {
        foreach (var variant in new[] { "no-yield", "yield-caller", "execute-caller", "child-caller", "named-start",
            "yield-value", "parent-node", "no-current", "stale-current", "duplicate" })
        {
            using var getter = new NativeLocalInitializationCoroutineRegression.Fixture();
            using var f = new F(initialization: getter.Owner);
            const long childNode = 302, childEnumerator = NativeLocalInitializationCoroutineRegression.Backend.WaitChild;
            var role = InitializationCoroutineRole.WaitUntil;
            getter.Backend.Start(role, () => {
                var start = f.BeginNode(NativeEngineNodeKind.Start);
                var execute = f.BeginNode(NativeEngineNodeKind.Execute, caller: variant == "execute-caller" ? new(0xdead) : f.Ledger.InitialExecuteCaller);
                if (variant != "no-current") getter.Backend.Move(role, 1, 1);
                if (variant == "stale-current") getter.Backend.Move(role, -1, 0);
                var frame = f.Frame(NativeEngineWaitKind.Yield);
                frame.Caller = variant == "yield-caller" ? new(0xdead) : f.Ledger.CoroutineYieldCaller;
                frame.Arg1 = new(variant == "yield-value" ? childEnumerator + 1 : childEnumerator);
                if (variant == "parent-node") frame.Arg0 = frame.Node = new(999);
                var yield = variant == "no-yield" ? null : f.Begin(frame);

                f.SetNode(childNode, childEnumerator);
                var reads = getter.Backend.CurrentReads;
                if (variant == "duplicate")
                {
                    var first = f.BeginNode(NativeEngineNodeKind.Start, caller: f.Ledger.WaitUntilChildCaller);
                    Check(first.Identity.Identity?.WaitUntilSource is not null);
                    f.EndNode(f.BeginNode(NativeEngineNodeKind.Execute)); f.EndNode(first, output: childNode);
                    reads = getter.Backend.CurrentReads;
                }
                var child = f.BeginNode(NativeEngineNodeKind.Start,
                    caller: variant == "child-caller" ? new(0xdead) : f.Ledger.WaitUntilChildCaller,
                    nameArgument: variant == "named-start" ? 1 : 0);
                Check(child.Identity.Identity is null && f.Identities.Unknown && f.Ledger.Unknown
                    && f.Identities.Read().Pending && getter.Backend.CurrentReads == reads, variant);
                f.EndNode(child);
                if (yield is not null) f.End(yield);
                f.SetNode(F.Node, 1002);
                f.EndNode(execute); f.EndNode(start, output: F.Node);
            });
            Check(f.Identities.Unknown && f.Ledger.Unknown, variant);
        }
    }

    private static void Layout()
    {Check(Marshal.SizeOf<NativeEngineWaitTuple>()==32&&Marshal.SizeOf<NativeEngineWaitFrame>()==240&&Marshal.SizeOf<NativeEngineWaitCompletion>()==48
        &&Marshal.SizeOf<NativeEngineWaitPhaseFrame>()==288&&Marshal.SizeOf<NativeEngineWaitConfiguration>()==432&&Marshal.SizeOf<NativeEngineWaitInformation>()==176);
        Check(Marshal.OffsetOf<NativeEngineWaitFrame>(nameof(NativeEngineWaitFrame.ExternalSequence)).ToInt32()==64
            &&Marshal.OffsetOf<NativeEngineWaitFrame>(nameof(NativeEngineWaitFrame.Tuple)).ToInt32()==192
            &&Marshal.OffsetOf<NativeEngineWaitPhaseFrame>(nameof(NativeEngineWaitPhaseFrame.SourceExternalSequence)).ToInt32()==280);}
    private static void Publication()
    {using var f=new F();f.Publish();var value=f.Ledger.Read();Check(!value.Unknown&&value.Pending&&value.Records.Single() is{Committed:true,ReferenceReturned:false,State:NativeEngineWaitState.Published});
        Check(!f.Ledger.MayRetireNodeGeneration(f.Identities.Read().Identities.Single())&&!value.EngineCancelled&&!value.FutureClosed);}
    private static void PartialPublication()
    {using var f=new F();f.Publish(missing:true);Check(f.Ledger.Unknown&&f.Ledger.Read().Pending&&f.Ledger.Read().Records.Single().State!=NativeEngineWaitState.Retired);}
    private static void OwnerNullCleanup()
    {using var f=new F();f.Publish();var d=f.Begin(f.Frame(NativeEngineWaitKind.Dispatch));var op=f.Operation();
        f.Phase(d,NativeEngineWaitPhase.DispatchOwnerResult,op,owner:0,stage:NativeEngineWaitStageFlags.OwnerNull);
        f.Phase(d,NativeEngineWaitPhase.DispatchCleanupDecision,op);f.Release(d,op);
        f.Phase(d,NativeEngineWaitPhase.DispatchClearReached,op,stage:NativeEngineWaitStageFlags.CallbackSlotClearReached);
        Check(!f.Ledger.Unknown&&f.Ledger.Read().Records.Single().State!=NativeEngineWaitState.Retired);f.End(d);
        Check(!f.Ledger.Unknown&&f.Ledger.Read().Records.Single().State==NativeEngineWaitState.Retired);}

    private static void CompletedPhysicalMasks()
    {
        using var f = new F();
        f.Publish();
        var dispatch = f.Begin(f.Frame(NativeEngineWaitKind.Dispatch));
        var operation = f.Operation();
        f.Phase(dispatch, NativeEngineWaitPhase.DispatchOwnerResult, operation, owner: 0, stage: NativeEngineWaitStageFlags.OwnerNull);

        for (var index = 0; index < NativeEngineWaitLedger.MaximumCalls + 1; ++index)
            f.End(f.Begin(f.Frame(NativeEngineWaitKind.SchedulerPump, external: (ulong)index + 1)));

        var pending = f.Ledger.Read();
        Check(!pending.Unknown && pending.Outstanding == 1 && pending.Calls > NativeEngineWaitLedger.MaximumCalls,
            $"Completed masks exhausted the Wait ledger: {pending.Failure}");
        Check(pending.Records.Single() is { State: NativeEngineWaitState.Published, OpenDispatches: 1 });
        f.Phase(dispatch, NativeEngineWaitPhase.DispatchCleanupDecision, operation);
        f.Release(dispatch, operation);
        f.Phase(dispatch, NativeEngineWaitPhase.DispatchClearReached, operation, stage: NativeEngineWaitStageFlags.CallbackSlotClearReached);
        Check(f.Ledger.Read().Pending);
        f.End(dispatch);

        var final = f.Ledger.Read();
        Check(final is { Unknown: false, Outstanding: 0, Pending: false });
        Check(final.Records.Single() is { State: NativeEngineWaitState.Retired, OpenDispatches: 0, OpenCleanups: 0 });
    }

    private static void FirstFailure()
    {
        var failures = new List<string>();
        using (var f = new F(reportFailure: failures.Add))
        {
            f.Publish();
            var dispatch = f.Begin(f.Frame(NativeEngineWaitKind.Dispatch));
            f.Phase(dispatch, NativeEngineWaitPhase.DispatchCleanupDecision, f.Operation());

            var first = failures.Single();
            Check(first.StartsWith("Wait operation reached an end before its real Begin.", StringComparison.Ordinal)
                && first.Contains($"sequence={dispatch.Frame.Sequence}", StringComparison.Ordinal)
                && first.Contains("Phase=DispatchCleanupDecision", StringComparison.Ordinal));
            Check(f.Ledger.Read() is { Unknown: true, Pending: true, Outstanding: 1 });
            f.End(dispatch);

            var final = f.Ledger.Read();
            Check(final.Unknown && final.Pending && final.Failure == first && failures.Count == 1);
            Check(final.Records.Single() is { State: NativeEngineWaitState.Unknown, OpenDispatches: 1 });
        }

        failures.Clear();
        using (var f = new F(reportFailure: failures.Add))
        {
            f.SetNode(F.Node, 2001);
            var release = f.BeginNode(NativeEngineNodeKind.Release);
            var first = failures.Single();
            Check(f.Nodes.FirstFailure is { } nodeFailure && first.StartsWith(nodeFailure, StringComparison.Ordinal)
                && first.Contains("Wait NodeRelease", StringComparison.Ordinal));
            f.EndNode(release);
            Check(f.Ledger.Read().Failure == first && failures.Count == 1);
        }
    }
    private static void Detach()
    {using var f=new F();f.Publish();var detach=f.BeginNode(NativeEngineNodeKind.Detach);f.Phase(detach.Wait,NativeEngineWaitPhase.DetachOwnerBegin,f.Operation(),stage:NativeEngineWaitStageFlags.BeforeWrite);f.EndNode(detach);
        Check(!f.Ledger.Unknown&&f.Ledger.Read().Records.Single() is{OwnerRevoked:true,FieldsCleared:false,ReferenceReturned:false,CallbackSlotCleared:false});}
    private static void OldReference()
    {using var f=new F();f.Publish();f.Publish(702,F.Wait);var rows=f.Ledger.Read();Check(!rows.Unknown&&rows.Records.Count==2
        &&rows.Records[0] is{ReferenceReturned:true,NodeWaitCleared:true,CallbackSlotCleared:false,State:NativeEngineWaitState.Published}&&rows.Pending);}
    private static void ReleaseStages()
    {using(var f=new F()){f.Publish();var call=f.BeginNode(NativeEngineNodeKind.Release);f.Clear(call);Check(!f.Ledger.Read().Records.Single().ReferenceReturned);f.EndNode(call,0);
            Check(!f.Ledger.Unknown&&f.Ledger.Read().Records.Single().State==NativeEngineWaitState.Retired&&f.Ledger.MayRetireNodeGeneration(f.Identities.Read().Identities.Single()));}
        using(var f=new F()){f.Publish();var call=f.BeginNode(NativeEngineNodeKind.Release);f.Clear(call,missing:true);f.EndNode(call,0);Check(f.Ledger.Unknown&&f.Ledger.Read().Pending);}}
    private static void Batch()
    {
        using(var f=new F())
        {
            f.Publish();var d=f.Begin(f.Frame(NativeEngineWaitKind.Dispatch));var dispatchOperation=f.Operation();
            f.Phase(d,NativeEngineWaitPhase.DispatchOwnerResult,dispatchOperation,owner:0,stage:NativeEngineWaitStageFlags.OwnerNull);
            f.Phase(d,NativeEngineWaitPhase.DispatchCleanupDecision,dispatchOperation);f.Release(d,dispatchOperation,clear:false);
            f.Phase(d,NativeEngineWaitPhase.DispatchClearReached,dispatchOperation,stage:NativeEngineWaitStageFlags.CallbackSlotClearReached);f.End(d);
            var b=f.Begin(f.Frame(NativeEngineWaitKind.Batch));var op=f.Operation();var tuple=f.Tuple();tuple.Callback=IntPtr.Zero;
            f.Phase(b,NativeEngineWaitPhase.BatchObjectBegin,op,tuple:tuple);
            f.Phase(b,NativeEngineWaitPhase.BatchObjectEnd,op,tuple:tuple,stage:NativeEngineWaitStageFlags.SharedReferenceReturned);f.End(b);
            Check(!f.Ledger.Unknown&&f.Ledger.Read().Records.Single() is{CallbackSlotCleared:true,ReferenceReturned:false,NodeWaitCleared:false}
                &&f.Ledger.Read().Pending);
        }
        using(var f=new F()){f.Publish();f.Publish(702,F.Wait);var b=f.Begin(f.Frame(NativeEngineWaitKind.Batch));var op=f.Operation();
        f.Phase(b,NativeEngineWaitPhase.BatchObjectBegin,op);f.Release(b,op,clear:false);f.Phase(b,NativeEngineWaitPhase.BatchObjectEnd,op,
            stage:NativeEngineWaitStageFlags.SharedReferenceReturned|NativeEngineWaitStageFlags.CallbackSlotClearReached);Check(f.Ledger.Read().Records[0].State!=NativeEngineWaitState.Retired);f.End(b);
        Check(!f.Ledger.Unknown&&f.Ledger.Read().Records[0].State==NativeEngineWaitState.Retired&&f.Ledger.Read().Records[1].State==NativeEngineWaitState.Published);}}
    private static void RetiredBatch()
    {
        foreach(var mutation in new[]{0,1,2})
        {
            using var f=new F();f.Publish();f.Publish(702,F.Wait);
            var first=f.Begin(f.Frame(NativeEngineWaitKind.Batch));var firstOperation=f.Operation();
            f.Phase(first,NativeEngineWaitPhase.BatchObjectBegin,firstOperation);f.Release(first,firstOperation,clear:false);
            f.Phase(first,NativeEngineWaitPhase.BatchObjectEnd,firstOperation,
                stage:NativeEngineWaitStageFlags.SharedReferenceReturned|NativeEngineWaitStageFlags.CallbackSlotClearReached);f.End(first);
            Check(!f.Ledger.Unknown&&f.Ledger.Read().Records[0].State==NativeEngineWaitState.Retired);
            var tuple=f.Tuple();tuple.Callback=IntPtr.Zero;
            if(mutation==1)tuple.Payload=new IntPtr(999);if(mutation==2)tuple.Cleanup=new IntPtr(998);
            var later=f.Begin(f.Frame(NativeEngineWaitKind.Batch));var laterOperation=f.Operation();
            f.Phase(later,NativeEngineWaitPhase.BatchObjectBegin,laterOperation,tuple:tuple,payload:tuple.Payload.ToInt64());
            f.Phase(later,NativeEngineWaitPhase.BatchObjectEnd,laterOperation,tuple:tuple,payload:tuple.Payload.ToInt64(),
                stage:NativeEngineWaitStageFlags.SharedReferenceReturned);f.End(later);
            var snapshot=f.Ledger.Read();Check(snapshot.Unknown==(mutation!=0));
            Check(snapshot.Records[0] is{State:NativeEngineWaitState.Retired,OpenDispatches:0,OpenCallbacks:0,OpenCleanups:0});
            Check(snapshot.Pending&&!snapshot.EngineCancelled&&!snapshot.FutureClosed);
            if(mutation==0)Check(snapshot.Records[1].State==NativeEngineWaitState.Published);
        }
    }
    private static void RetiredDispatch()
    {
        foreach(var mutation in new[]{0,1,2,3})
        {
            using var f=new F();f.Publish();
            if(mutation==3)
            {var release=f.BeginNode(NativeEngineNodeKind.Release);f.Clear(release);f.EndNode(release,0);}
            else
            {
                f.Publish(702,F.Wait);var batch=f.Begin(f.Frame(NativeEngineWaitKind.Batch));var operation=f.Operation();
                f.Phase(batch,NativeEngineWaitPhase.BatchObjectBegin,operation);f.Release(batch,operation,clear:false);
                f.Phase(batch,NativeEngineWaitPhase.BatchObjectEnd,operation,
                    stage:NativeEngineWaitStageFlags.SharedReferenceReturned|NativeEngineWaitStageFlags.CallbackSlotClearReached);f.End(batch);
            }
            Check(!f.Ledger.Unknown&&f.Ledger.Read().Records[0].State==NativeEngineWaitState.Retired);
            var frame=f.Frame(NativeEngineWaitKind.Dispatch);frame.Tuple.Callback=IntPtr.Zero;
            if(mutation==1)frame.Tuple.Payload=new IntPtr(999);if(mutation==2)frame.Tuple.Cleanup=new IntPtr(998);
            var dispatch=f.Begin(frame);var during=f.Ledger.Read();
            Check(during.Outstanding>0&&during.Pending&&during.Unknown==(mutation!=0));
            Check(during.Records[0] is{State:NativeEngineWaitState.Retired,OpenDispatches:0,OpenCallbacks:0,OpenCleanups:0});
            f.End(dispatch);var after=f.Ledger.Read();Check(after.Outstanding==0&&after.Unknown==(mutation!=0));
            Check(!after.EngineCancelled&&!after.FutureClosed);
        }
    }
    private static void PhysicalMasks()
    {using(var f=new F()){f.Publish();var stale=f.LastReceipt!;f.Ledger.Capture(stale);Check(f.Ledger.Unknown);}
        using(var f=new F()){f.Publish();var d=f.Begin(f.Frame(NativeEngineWaitKind.Dispatch));var foreign=f.Begin(f.Frame(NativeEngineWaitKind.Dispatch,999));
            var node=f.BeginNode(NativeEngineNodeKind.Release,d.Frame.Sequence,1,NativeEngineWaitFlags.SourceDispatch);Check(f.Ledger.CaptureCleanup(node.Identity.NodeCall!) is null&&f.Ledger.Unknown);f.EndNode(node);f.End(foreign);f.End(d);}}
    private static void Information()
    {using var f=new F();var id=f.Bridge.Identity;var info=new NativeEngineWaitInformation{Version=1,Size=176,Slot=id.Slot,Configured=1,Epoch=id.Epoch,MainThreadId=id.MainThreadId,
        YieldTarget=id.Target(NativeEngineWaitKind.Yield),DispatchTarget=id.Target(NativeEngineWaitKind.Dispatch),BatchTarget=id.Target(NativeEngineWaitKind.Batch),
        YieldOriginal=id.Original(0),DispatchOriginal=id.Original(1),BatchOriginal=id.Original(2),YieldEntry=id.Entry(0),DispatchEntry=id.Entry(1),BatchEntry=id.Entry(2),PhaseEntry=id.PhaseEntry};
        Check(NativeEngineWaitContextBridge.MatchesInformation(info,id,false));info.Unknown=1;Check(!NativeEngineWaitContextBridge.MatchesInformation(info,id,false));
        var frame=f.Frame(NativeEngineWaitKind.Batch);
        Task.Run(()=>{var token=f.Bridge.Enter(frame);f.Bridge.Exit(frame,token,new(){Version=1,Size=48,Flags=NativeEngineWaitExitFlags.Returned|NativeEngineWaitExitFlags.RawRaxValid});}).GetAwaiter().GetResult();
        Check(f.Dispatcher.Unknown); }
    private static void Callback()
    {using var f=new F();f.Publish();var d=f.Begin(f.Frame(NativeEngineWaitKind.Dispatch));var op=f.Operation();f.Phase(d,NativeEngineWaitPhase.DispatchOwnerResult,op);
        f.Phase(d,NativeEngineWaitPhase.DispatchCallbackArmed,op);f.Callback(d,op);f.Phase(d,NativeEngineWaitPhase.DispatchCleanupDecision,op);f.Release(d,op);
        f.Phase(d,NativeEngineWaitPhase.DispatchClearReached,op,stage:NativeEngineWaitStageFlags.CallbackSlotClearReached);f.End(d);
        Check(!f.Ledger.Unknown&&f.Ledger.Read().Records.Single().State==NativeEngineWaitState.Retired);}
    private static void CallbackClearsFields()
    {using var f=new F();f.Publish();var d=f.Begin(f.Frame(NativeEngineWaitKind.Dispatch));var op=f.Operation();f.Phase(d,NativeEngineWaitPhase.DispatchOwnerResult,op);f.Phase(d,NativeEngineWaitPhase.DispatchCallbackArmed,op);
        f.Callback(d,op,()=>{var release=f.BeginNode(NativeEngineNodeKind.Release);f.Clear(release);f.EndNode(release,0);});
        f.Phase(d,NativeEngineWaitPhase.DispatchCleanupDecision,op,tuple:default(NativeEngineWaitTuple),cleanup:0,payload:0);
        f.Phase(d,NativeEngineWaitPhase.DispatchClearReached,op,stage:NativeEngineWaitStageFlags.CallbackSlotClearReached);f.End(d);
        Check(!f.Ledger.Unknown&&f.Ledger.Read().Records.Single().State==NativeEngineWaitState.Retired);}
    private static void ConflictingCallbackSource()
    {using var f=new F();f.Publish();var d=f.Begin(f.Frame(NativeEngineWaitKind.Dispatch));var op=f.Operation();f.Phase(d,NativeEngineWaitPhase.DispatchOwnerResult,op);f.Phase(d,NativeEngineWaitPhase.DispatchCallbackArmed,op);
        f.Callback(d,op,conflicting:true);f.Phase(d,NativeEngineWaitPhase.DispatchCleanupDecision,op);Check(f.Ledger.Unknown);f.End(d);}
    private static void ChangedTuple()
    {using var f=new F();f.Publish();var d=f.Begin(f.Frame(NativeEngineWaitKind.Dispatch));var op=f.Operation();f.Phase(d,NativeEngineWaitPhase.DispatchOwnerResult,op,owner:0,stage:NativeEngineWaitStageFlags.OwnerNull);
        f.Phase(d,NativeEngineWaitPhase.DispatchCleanupDecision,op,tuple:f.Tuple(999),payload:999);Check(f.Ledger.Unknown);f.End(d);}
    private static void RepublishedCleanup()
    {
        using var f=new F();f.Publish();var d=f.Begin(f.Frame(NativeEngineWaitKind.Dispatch));var op=f.Operation();
        f.Phase(d,NativeEngineWaitPhase.DispatchOwnerResult,op);f.Phase(d,NativeEngineWaitPhase.DispatchCallbackArmed,op);
        f.Callback(d,op,()=>{var old=f.BeginNode(NativeEngineNodeKind.Release);f.Clear(old);f.EndNode(old,0);f.NewNodeAndPublish();});
        f.Phase(d,NativeEngineWaitPhase.DispatchCleanupDecision,op);f.Release(d,op);
        Check(!f.Ledger.Unknown&&f.Ledger.Read().Records.All(value=>value.State!=NativeEngineWaitState.Retired));
        f.Phase(d,NativeEngineWaitPhase.DispatchClearReached,op,stage:NativeEngineWaitStageFlags.CallbackSlotClearReached);f.End(d);
        var rows=f.Ledger.Read();Check(!rows.Unknown&&rows.Records.Count==2&&rows.Records.Select(value=>value.NodeGeneration).Distinct().Count()==2
            &&rows.Records.All(value=>value.State==NativeEngineWaitState.Retired)&&!rows.EngineCancelled&&!rows.FutureClosed);
    }
    private static void StartClear()
    {using var f=new F(startClearsWait:true);Check(!f.Ledger.Unknown&&f.Ledger.Read().Records.Single().State==NativeEngineWaitState.Retired
        &&f.Nodes.Read().Nodes.Single().InlineCleanupUnproven&&!f.Nodes.Read().Nodes.Single().FinalCleanupObserved&&!f.Ledger.Read().EngineCancelled);}
    private static void Abnormal()
    {using(var f=new F()){f.Publish();var release=f.BeginNode(NativeEngineNodeKind.Release);f.Clear(release);f.EndNode(release,0,abnormal:true);Check(f.Ledger.Unknown&&f.Ledger.Read().Pending);}
        using(var f=new F()){f.Publish();var b=f.Begin(f.Frame(NativeEngineWaitKind.Batch));f.Phase(b,NativeEngineWaitPhase.BatchObjectBegin,f.Operation());f.End(b);Check(f.Ledger.Unknown);}}
}
