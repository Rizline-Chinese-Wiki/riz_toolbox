using System.Collections.Concurrent;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

// All thirteen native boundaries share a physical mask. Private proofs are
// issued for this exact callback delivery, not reconstructed from native IDs.
internal sealed class NativeEngineWaitObserverDispatcher
{
    private const int CallLimit=4096,SinkLimit=32;
    private static readonly List<NativeEngineWaitObserverDispatcher> Owners=new();
    [ThreadStatic]private static Physical? _top;
    private sealed class Sink
    {internal Func<Observation,IDisposable?> Capture=null!;internal Action<IDisposable,Observation> Complete=null!,Phase=null!;internal Action<NativeEngineWaitFrame,string> Fault=null!;internal bool Active=true;}
    private sealed class Physical
    {
        internal readonly NativeEngineWaitObserverDispatcher Owner;internal readonly NativeEngineWaitFrame Frame;
        internal readonly Physical? Parent=_top;internal readonly int Thread=Environment.CurrentManagedThreadId;internal readonly Sink[] Sinks;
        internal readonly List<(Sink Sink,IDisposable Context)> Contexts=new();internal readonly ScopeProof Proof;
        internal readonly Dictionary<ulong,NativeEngineWaitPhaseFrame> LastPhases=new();
        internal bool Closing;internal ulong Phases;
        internal Physical(NativeEngineWaitObserverDispatcher owner,NativeEngineWaitFrame frame,Sink[] sinks)
        {(Owner,Frame,Sinks)=(owner,frame,sinks);Proof=new(owner,owner._issuer,this);}
    }
    internal sealed class ScopeProof
    {
        private readonly NativeEngineWaitObserverDispatcher _owner;private readonly Physical _call;
        internal ScopeProof(NativeEngineWaitObserverDispatcher owner,object issuer,object call)
        {if(!ReferenceEquals(issuer,owner._issuer)||call is not Physical actual||!ReferenceEquals(actual.Owner,owner))throw new InvalidOperationException("Wait scope lacks its private physical issuer.");(_owner,_call)=(owner,actual);}
        internal NativeEngineWaitFrame Frame=>_call.Frame;internal ScopeProof? Parent=>_call.Parent?.Proof;
        internal bool IsCurrent=>!_owner.Unknown&&ReferenceEquals(_top,_call)&&_call.Thread==Environment.CurrentManagedThreadId
            &&_owner._calls.Values.Any(value=>ReferenceEquals(value,_call));
        internal bool IsBindingWindow=>IsCurrent&&!_call.Closing&&_owner._delivery is null;
        internal NativeEngineWaitPhaseFrame? LastPhase(ulong operation)=>_call.LastPhases.TryGetValue(operation,out var value)?value:null;
        internal bool Contains(object sink)=>IsBindingWindow&&_call.Sinks.Any(value=>ReferenceEquals(value,sink));
    }
    internal sealed class Observation
    {
        private readonly NativeEngineWaitObserverDispatcher _owner;
        internal ScopeProof Scope{get;}internal NativeEngineWaitFrame Frame=>Scope.Frame;
        internal NativeEngineWaitCompletion? Completion{get;}internal NativeEngineWaitPhaseFrame? Phase{get;}
        internal Observation(NativeEngineWaitObserverDispatcher owner,object issuer,ScopeProof scope,NativeEngineWaitCompletion? completion,NativeEngineWaitPhaseFrame? phase)
        {if(!ReferenceEquals(issuer,owner._issuer))throw new InvalidOperationException("Wait receipt issuer differs.");(_owner,Scope,Completion,Phase)=(owner,scope,completion,phase);}
        internal bool IsCurrent=>ReferenceEquals(_owner._delivery,this)&&Scope.IsCurrent;
    }
    internal sealed class Binding:IDisposable
    {
        private readonly NativeEngineWaitObserverDispatcher _owner;private readonly Sink _sink;private readonly Action _detach;private int _disposed;
        internal Binding(NativeEngineWaitObserverDispatcher owner,object issuer,object sink,Action detach)
        {if(!ReferenceEquals(issuer,owner._issuer)||sink is not Sink actual)throw new InvalidOperationException("Wait binding issuer differs.");(_owner,_sink,_detach)=(owner,actual,detach);}
        internal bool IsCapturedByCurrentBoundary=>_owner.CurrentBoundaryScope?.Contains(_sink)==true;
        public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)==0)_detach();}
    }
    // Claim is created only while the real M Capture is being delivered. The
    // frozen native direct-parent source and its armed operation must agree.
    internal sealed class CallbackClaim
    {
        private readonly NativeEngineWaitObserverDispatcher _owner;private readonly ScopeProof _boundary;
        internal NativeEngineSchedulerObserverDispatcher.ScopeProof SchedulerScope{get;}
        internal ScopeProof Source{get;}internal NativeEngineWaitFrame Frame=>_boundary.Frame;
        private CallbackClaim(NativeEngineWaitObserverDispatcher owner,ScopeProof boundary,ScopeProof source,NativeEngineSchedulerObserverDispatcher.ScopeProof scheduler)
            =>(_owner,_boundary,Source,SchedulerScope)=(owner,boundary,source,scheduler);
        internal bool IsCurrent=>!_owner.Unknown&&_boundary.IsBindingWindow&&SchedulerScope.IsCurrent;
        internal bool IsFor(NativeEngineSchedulerObserverDispatcher.Observation receipt)=>IsCurrent&&receipt.IsCurrent
            &&ReferenceEquals(SchedulerScope,receipt.Scope)&&receipt.Frame.Sequence==Frame.ExternalSequence;
        internal static CallbackClaim? Issue(NativeEngineWaitObserverDispatcher owner,NativeEngineSchedulerObserverDispatcher.Observation receipt)
        {
            ArgumentNullException.ThrowIfNull(receipt);var scope=owner.CurrentBoundaryScope;var frame=scope?.Frame??default;
            if(scope is null||frame.Kind!=NativeEngineWaitKind.SchedulerCallback||!receipt.IsCurrent||receipt.Completion is not null||receipt.Row is not null)return null;
            var source=scope.Parent;var flags=frame.Flags&(NativeEngineWaitFlags.SourceDispatch|NativeEngineWaitFlags.SourceBatch);
            if(source is null||flags==0)return null;
            var phase=source.LastPhase(frame.OperationSequence);
            if(source.Frame.Sequence!=frame.SourceSequence||source.Frame.Kind!=(flags==NativeEngineWaitFlags.SourceDispatch?NativeEngineWaitKind.Dispatch:NativeEngineWaitKind.Batch)
                ||phase is null||phase.Value.Kind!=(flags==NativeEngineWaitFlags.SourceDispatch?NativeEngineWaitPhase.DispatchCallbackArmed:NativeEngineWaitPhase.BatchObjectBegin)
                ||frame.ExternalSequence!=receipt.Frame.Sequence||receipt.Frame.Kind!=NativeEngineSchedulerKind.Callback
                ||receipt.Frame.SourceSequence!=0||(receipt.Frame.Flags&(NativeEngineSchedulerFlags.SourceConsume|NativeEngineSchedulerFlags.SourceDrainRow|NativeEngineSchedulerFlags.SnapshotCached))!=0
                ||frame.Arg0!=receipt.Frame.Arg0||frame.Arg1!=receipt.Frame.Arg1||frame.Caller!=receipt.Frame.Caller
                ||frame.Target!=owner.Identity.Target(NativeEngineWaitKind.SchedulerCallback)
                ||phase.Value.ActualCallback!=frame.Target||phase.Value.ActualPayload!=frame.Arg1||phase.Value.ActualOwner!=frame.Arg0)
                return null;
            return new(owner,scope,source,receipt.Scope);
        }
    }
    private readonly INativeEngineWaitContextBridge _bridge;private readonly object _gate=new(),_issuer=new();
    private readonly List<Sink> _sinks=new();private Sink[] _published=Array.Empty<Sink>();
    private readonly ConcurrentDictionary<nuint,Physical> _calls=new();private Observation? _delivery;
    private long _tokens,_inFlight;private ulong _lastSequence,_lastEvent;private int _unknown;
    internal NativeEngineWaitFamilyIdentity Identity{get;}internal bool Unknown=>Volatile.Read(ref _unknown)!=0;
    internal NativeEngineWaitObserverDispatcher(INativeEngineWaitContextBridge bridge,ILogger log)
    {
        ArgumentNullException.ThrowIfNull(bridge);ArgumentNullException.ThrowIfNull(log);(_bridge,Identity)=(bridge,bridge.Identity);
        lock(Owners)Owners.Add(this);try{bridge.Attach(Enter,Exit,Phase);}catch{Interlocked.Exchange(ref _unknown,1);throw;}
    }
    internal Binding Bind<T>(Func<Observation,T?> capture,Action<T,Observation> complete,Action<T,Observation> phase,Action<NativeEngineWaitFrame,string> fault)where T:class,IDisposable
    {
        ArgumentNullException.ThrowIfNull(capture);ArgumentNullException.ThrowIfNull(complete);ArgumentNullException.ThrowIfNull(phase);ArgumentNullException.ThrowIfNull(fault);
        var sink=new Sink{Capture=value=>capture(value),Complete=(context,value)=>complete((T)context,value),Phase=(context,value)=>phase((T)context,value),Fault=fault};
        lock(_gate){if(Unknown||_sinks.Count==SinkLimit)throw new InvalidOperationException("Wait logical observers unavailable or full.");_sinks.Add(sink);Publish();}
        return new(this,_issuer,sink,()=>{lock(_gate){sink.Active=false;Publish();}});
    }
    private void Publish(){_sinks.RemoveAll(sink=>!sink.Active&&!_calls.Values.Any(call=>call.Sinks.Contains(sink)));Volatile.Write(ref _published,_sinks.ToArray());}
    internal ScopeProof? CurrentBoundaryScope=>_top is{} call&&ReferenceEquals(call.Owner,this)&&call.Proof.IsBindingWindow?call.Proof:null;
    internal CallbackClaim? TryClaimCallback(NativeEngineSchedulerObserverDispatcher.Observation receipt)=>CallbackClaim.Issue(this,receipt);
    private bool Matches(NativeEngineWaitFrame frame)=>ReferenceEquals(_bridge.Identity,Identity)&&frame.IsValid&&!frame.HasFault
        &&frame.Slot==Identity.Slot&&frame.Epoch==Identity.Epoch&&frame.ThreadId==Identity.MainThreadId
        &&frame.ThreadId==NativeEngineInlinePlatform.CurrentOsThreadId&&frame.Target==Identity.Target(frame.Kind)
        &&(frame.Flags&(NativeEngineWaitFlags.MainThread|NativeEngineWaitFlags.ParentMatched))==(NativeEngineWaitFlags.MainThread|NativeEngineWaitFlags.ParentMatched);
    private nuint Enter(NativeEngineWaitFrame frame)
    {
        var published=Volatile.Read(ref _published);Physical? call=null;nuint token=0;
        try
        {
            var parent=_top;var frozen=published.Where(sink=>parent is null?sink.Active:ReferenceEquals(parent.Owner,this)&&parent.Sinks.Contains(sink)).ToArray();
            if(_calls.Count==CallLimit)throw new InvalidOperationException("Wait physical capacity exhausted.");
            var next=Interlocked.Increment(ref _tokens);if(next<=0)throw new InvalidOperationException("Wait token exhausted.");token=checked((nuint)next);
            call=new(this,frame,frozen);if(!_calls.TryAdd(token,call))throw new InvalidOperationException("Wait token repeated.");_top=call;Interlocked.Increment(ref _inFlight);
            if(Unknown||!Matches(frame)||frame.Sequence<=_lastSequence||frame.ParentSequence!=(parent?.Frame.Sequence??0)
                ||frame.RootSequence!=(parent?.Frame.RootSequence??frame.Sequence)||frame.Depth!=(parent?.Frame.Depth??0)+1)
                throw new InvalidOperationException("Wait Enter crossed its exact native parent, generation or thread.");
            _lastSequence=frame.Sequence;Deliver(call,null,null,receipt=>{foreach(var sink in call.Sinks)try{var context=sink.Capture(receipt);if(context is not null)call.Contexts.Add((sink,context));}catch{Fault(call.Sinks,frame,"Wait consumer Capture failed.");}});
            return token;
        }
        catch{Fault(call?.Sinks??published,frame,"Wait physical entry is unconfirmed.");return token;}
    }
    private void Exit(NativeEngineWaitFrame frame,nuint token,NativeEngineWaitCompletion completion)
    {
        if(token==0||!_calls.TryGetValue(token,out var call)||!ReferenceEquals(_top,call)||call.Closing||call.Thread!=Environment.CurrentManagedThreadId||!call.Frame.Equals(frame))
        {Fault(Volatile.Read(ref _published),frame,"Wait Exit lacks its exact physical token/frame.");return;}
        call.Closing=true;
        try
        {
            if(!Matches(frame)||!completion.IsNormalFor(frame.Kind)||completion.Phases!=call.Phases)Fault(call.Sinks,frame,"Wait original completion or phase count is unconfirmed.");
            Deliver(call,completion,null,receipt=>{foreach(var item in call.Contexts)try{item.Sink.Complete(item.Context,receipt);}catch{Fault(call.Sinks,frame,"Wait consumer Complete failed.");}});
        }
        finally
        {for(var i=call.Contexts.Count-1;i>=0;--i)try{call.Contexts[i].Context.Dispose();}catch{Fault(call.Sinks,frame,"Wait consumer Dispose failed.");}
            _calls.TryRemove(token,out _);_top=call.Parent;Interlocked.Decrement(ref _inFlight);lock(_gate)Publish();}
    }
    private void Phase(NativeEngineWaitPhaseFrame frame)
    {
        var call=_top;
        try
        {
            if(call is null||!ReferenceEquals(call.Owner,this)||!call.Proof.IsBindingWindow||!frame.IsValid||frame.HasFault
                ||frame.Sequence!=call.Frame.Sequence||frame.ExternalSequence!=call.Frame.ExternalSequence||frame.ScopeKind!=call.Frame.Kind
                ||frame.Slot!=Identity.Slot||frame.Epoch!=Identity.Epoch||frame.ThreadId!=call.Frame.ThreadId||frame.Depth!=call.Frame.Depth
                ||frame.ParentSequence!=call.Frame.ParentSequence||frame.RootSequence!=call.Frame.RootSequence||frame.EventSequence<=_lastEvent||!MatchesPhaseKind(frame.Kind,call.Frame.Kind))
                throw new InvalidOperationException("Wait phase lacks its current original native boundary.");
            var site=Identity.Site(frame.Kind);if(frame.SitePc!=site.SitePc||frame.RelayReturnPc!=site.RelayReturnPc)throw new InvalidOperationException("Wait phase site/relay differs.");
            _lastEvent=frame.EventSequence;++call.Phases;call.LastPhases[frame.OperationSequence]=frame;
            Deliver(call,null,frame,receipt=>{foreach(var item in call.Contexts)try{item.Sink.Phase(item.Context,receipt);}catch{Fault(call.Sinks,call.Frame,"Wait consumer phase failed.");}});
        }
        catch{Fault(call?.Sinks??Volatile.Read(ref _published),call?.Frame??default,"Wait source phase is unconfirmed.");}
    }
    private static bool MatchesPhaseKind(NativeEngineWaitPhase phase,NativeEngineWaitKind kind)=>phase switch
    {
        NativeEngineWaitPhase.PublishBegin or NativeEngineWaitPhase.OldReferenceDecision or NativeEngineWaitPhase.OldReferenceReturned or NativeEngineWaitPhase.PublishCommitted=>kind==NativeEngineWaitKind.Yield,
        NativeEngineWaitPhase.DetachOwnerBegin or NativeEngineWaitPhase.DetachNestedOwnerBegin=>kind==NativeEngineWaitKind.NodeDetach,
        NativeEngineWaitPhase.ReleaseClearBegin or NativeEngineWaitPhase.ReleaseFieldsCleared or NativeEngineWaitPhase.ReleaseReferenceReturned=>kind==NativeEngineWaitKind.NodeRelease,
        NativeEngineWaitPhase.StartClearBegin or NativeEngineWaitPhase.StartFieldsCleared or NativeEngineWaitPhase.StartReferenceReturned=>kind==NativeEngineWaitKind.NodeStart,
        NativeEngineWaitPhase.DispatchOwnerResult or NativeEngineWaitPhase.DispatchCallbackArmed or NativeEngineWaitPhase.DispatchCleanupDecision or NativeEngineWaitPhase.DispatchClearReached=>kind==NativeEngineWaitKind.Dispatch,
        NativeEngineWaitPhase.BatchObjectBegin or NativeEngineWaitPhase.BatchObjectEnd=>kind==NativeEngineWaitKind.Batch,
        _=>false
    };
    private void Deliver(Physical call,NativeEngineWaitCompletion? completion,NativeEngineWaitPhaseFrame? phase,Action<Observation> action)
    {var previous=_delivery;var receipt=new Observation(this,_issuer,call.Proof,completion,phase);_delivery=receipt;try{action(receipt);}finally{_delivery=previous;}}
    private void Fault(Sink[] sinks,NativeEngineWaitFrame frame,string reason)
    {Interlocked.Exchange(ref _unknown,1);foreach(var sink in sinks)try{sink.Fault(frame,reason);}catch{}}
    internal ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {long count=Math.Max(0,Volatile.Read(ref _inFlight));try{var value=_bridge.ReadActivity();if(!value.Known||value.InFlight<0||count>long.MaxValue-value.InFlight)Interlocked.Exchange(ref _unknown,1);else count+=value.InFlight;}catch{Interlocked.Exchange(ref _unknown,1);}return new(count,!Unknown);}
}
