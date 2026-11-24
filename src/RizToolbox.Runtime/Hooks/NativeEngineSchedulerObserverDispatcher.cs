using System.Collections.Concurrent;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

// One permanent owner of six exact configured channels. Foreign calls occupy
// the same physical stack. Receipts can only be consumed during their actual
// delivery; ScopeProof is read-only and cannot be fabricated from a pointer.
internal sealed class NativeEngineSchedulerObserverDispatcher
{
    private const int CallLimit=4096,SinkLimit=32;
    private static readonly List<NativeEngineSchedulerObserverDispatcher> Owners=new();
    [ThreadStatic] private static Physical? _top;
    private sealed class Sink
    {
        internal Func<Observation,IDisposable?> Capture=null!;
        internal Action<IDisposable,Observation> Complete=null!,Row=null!;
        internal Action<NativeEngineSchedulerFrame,string> Fault=null!;
        internal bool Active=true;
    }
    private sealed class Physical
    {
        internal readonly NativeEngineSchedulerObserverDispatcher Owner;
        internal readonly NativeEngineSchedulerFrame Frame;
        internal readonly Physical? Parent=_top;
        internal readonly int Thread=Environment.CurrentManagedThreadId;
        internal readonly Sink[] Sinks;
        internal readonly List<(Sink Sink,IDisposable Context)> Contexts=new();
        internal readonly ScopeProof Proof;
        internal NativeEngineSchedulerRowFrame? OpenRow;
        internal ulong RowBegins,RowEnds;
        internal bool Original,Completing;
        internal Physical(NativeEngineSchedulerObserverDispatcher owner,NativeEngineSchedulerFrame frame,Sink[] sinks)
        { (Owner,Frame,Sinks)=(owner,frame,sinks);Proof=new(owner,owner._issuer,this); }
    }
    internal sealed class ScopeProof
    {
        private readonly NativeEngineSchedulerObserverDispatcher _owner;
        private readonly Physical _call;
        internal ScopeProof(NativeEngineSchedulerObserverDispatcher owner,object issuer,object physical)
        {
            if(!ReferenceEquals(issuer,owner._issuer) || physical is not Physical call || !ReferenceEquals(call.Owner,owner))
                throw new InvalidOperationException("Scheduler scope issuer is not its actual physical dispatcher.");
            (_owner,_call)=(owner,call);
        }
        internal NativeEngineSchedulerFrame Frame=>_call.Frame;
        internal ScopeProof? Parent=>_call.Parent?.Proof;
        internal NativeEngineSchedulerRowFrame? CurrentRow=>_call.OpenRow;
        internal bool IsCurrent=>ReferenceEquals(_top,_call) && _call.Thread==Environment.CurrentManagedThreadId
            && _owner._calls.Values.Any(value=>ReferenceEquals(value,_call));
        internal bool IsOriginal=>IsCurrent && _call.Original && !_call.Completing && !_owner.Unknown && _owner._delivery is null;
        internal bool ContainsFrozenSink(NativeEngineSchedulerObserverDispatcher owner,object sink)
            =>ReferenceEquals(_owner,owner)&&IsOriginal&&_call.Sinks.Any(value=>ReferenceEquals(value,sink));
    }
    internal sealed class Observation
    {
        private readonly NativeEngineSchedulerObserverDispatcher _owner;
        internal ScopeProof Scope{get;}
        internal NativeEngineSchedulerFrame Frame=>Scope.Frame;
        internal NativeEngineSchedulerCompletion? Completion{get;}
        internal NativeEngineSchedulerRowFrame? Row{get;}
        internal Observation(NativeEngineSchedulerObserverDispatcher owner,object issuer,ScopeProof scope,
            NativeEngineSchedulerCompletion? completion,NativeEngineSchedulerRowFrame? row)
        {
            if(!ReferenceEquals(issuer,owner._issuer)) throw new InvalidOperationException("Scheduler receipt issuer differs.");
            (_owner,Scope,Completion,Row)=(owner,scope,completion,row);
        }
        internal bool IsCurrent=>ReferenceEquals(_owner._delivery,this) && Scope.IsCurrent;
    }
    internal sealed class Binding:IDisposable
    {
        private readonly Action _detach; private readonly NativeEngineSchedulerObserverDispatcher _owner;
        private readonly Sink _sink; private int _disposed;
        internal Binding(NativeEngineSchedulerObserverDispatcher owner,object issuer,object sink,Action detach)
        {
            lock(owner._gate)
            {
                if(!ReferenceEquals(issuer,owner._issuer)||sink is not Sink actual||!owner._sinks.Contains(actual))
                    throw new InvalidOperationException("Scheduler logical binding lacks its actual private owner and frozen sink.");
                (_owner,_sink,_detach)=(owner,actual,detach);
            }
        }
        internal bool IsCapturedByCurrentOriginal=>_owner.CurrentOriginalScope?.ContainsFrozenSink(_owner,_sink)==true;
        public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)==0)_detach();}
    }
    private readonly object _gate=new(),_issuer=new();
    private readonly INativeEngineSchedulerContextBridge[] _bridges;
    private readonly NativeEngineSchedulerChannelIdentity[] _identities;
    private readonly ConcurrentDictionary<nuint,Physical> _calls=new();
    private readonly List<Sink> _sinks=new();
    private Sink[] _published=Array.Empty<Sink>();
    private Observation? _delivery;
    private long _tokens,_inFlight; private int _unknown; private ulong _lastRowEvent;
    internal bool Unknown=>Volatile.Read(ref _unknown)!=0;
    internal NativeEngineSchedulerObserverDispatcher(IReadOnlyList<INativeEngineSchedulerContextBridge> bridges,ILogger log)
    {
        ArgumentNullException.ThrowIfNull(bridges);ArgumentNullException.ThrowIfNull(log);
        if(bridges.Count!=6 || bridges.Where((bridge,index)=>bridge is null || !bridge.Identity.Complete || (uint)bridge.Identity.Kind!=index).Any())
            throw new ArgumentException("Six ordered exact actual scheduler channels required.");
        _bridges=bridges.ToArray();_identities=_bridges.Select(value=>value.Identity).ToArray();
        if(_identities.Any(value=>value.MainThreadId!=_identities[0].MainThreadId))throw new ArgumentException("Scheduler channels must share their actual OS main thread.");
        lock(Owners)Owners.Add(this);
        try{for(var index=0;index<6;++index){var kind=index;_bridges[index].Attach(frame=>Enter(kind,frame),(frame,token,result)=>Exit(kind,frame,token,result),
            kind==(int)NativeEngineSchedulerKind.Drain?Row:null);}}
        catch{Interlocked.Exchange(ref _unknown,1);throw;}
    }
    internal Binding Bind<T>(Func<Observation,T?> capture,Action<T,Observation> complete,Action<T,Observation> row,
        Action<NativeEngineSchedulerFrame,string> fault) where T:class,IDisposable
    {
        ArgumentNullException.ThrowIfNull(capture);ArgumentNullException.ThrowIfNull(complete);ArgumentNullException.ThrowIfNull(row);ArgumentNullException.ThrowIfNull(fault);
        var sink=new Sink{Capture=value=>capture(value),Complete=(context,value)=>complete((T)context,value),
            Row=(context,value)=>row((T)context,value),Fault=fault};
        lock(_gate){if(Unknown || _sinks.Count==SinkLimit)throw new InvalidOperationException("Scheduler logical observation unavailable or full.");_sinks.Add(sink);Publish();}
        return new(this,_issuer,sink,()=>{lock(_gate){sink.Active=false;Publish();}});
    }
    private void Publish()
    {
        _sinks.RemoveAll(sink=>!sink.Active && !_calls.Values.Any(call=>call.Sinks.Contains(sink)));
        Volatile.Write(ref _published,_sinks.ToArray());
    }
    internal ScopeProof? CurrentOriginalScope=>_top is { } call && ReferenceEquals(call.Owner,this) && call.Proof.IsOriginal?call.Proof:null;
    private bool Matches(NativeEngineSchedulerFrame frame,int kind)
    {
        var identity=_identities[kind];
        return _bridges[kind].Identity==identity && frame.IsValid && (int)frame.Kind==kind && frame.Slot==identity.Slot
            && frame.Epoch==identity.Epoch && frame.ThreadId==identity.MainThreadId
            && frame.ThreadId==NativeEngineInlinePlatform.CurrentOsThreadId
            && (frame.Flags&(NativeEngineSchedulerFlags.MainThread|NativeEngineSchedulerFlags.ParentMatched))
                ==(NativeEngineSchedulerFlags.MainThread|NativeEngineSchedulerFlags.ParentMatched);
    }
    private nuint Enter(int kind,NativeEngineSchedulerFrame frame)
    {
        var published=Volatile.Read(ref _published);Physical? call=null;nuint token=0;
        try
        {
            var parent=_top;
            var frozen=published.Where(sink=>parent is null?sink.Active:ReferenceEquals(parent.Owner,this)&&parent.Sinks.Contains(sink)).ToArray();
            if(_calls.Count==CallLimit)throw new InvalidOperationException("Scheduler physical capacity exhausted.");
            var next=Interlocked.Increment(ref _tokens);if(next<=0)throw new InvalidOperationException("Scheduler physical token exhausted.");
            token=checked((nuint)next);call=new(this,frame,frozen);
            if(!_calls.TryAdd(token,call))throw new InvalidOperationException("Scheduler token repeated.");
            _top=call;Interlocked.Increment(ref _inFlight);
            if(!Matches(frame,kind) || frame.HasFault || Unknown
                || frame.ParentSequence!=(parent?.Frame.Sequence??0) || frame.RootSequence!=(parent?.Frame.RootSequence??frame.Sequence)
                || frame.Depth!=(parent?.Frame.Depth??0)+1)
                throw new InvalidOperationException("Scheduler Enter crossed native physical parent, root, thread or snapshot.");
            Deliver(call,null,null,receipt=>{
                foreach(var sink in call.Sinks)try{var context=sink.Capture(receipt);if(context is not null)call.Contexts.Add((sink,context));}
                    catch{Fault(call.Sinks,frame,"Scheduler consumer Capture failed; responsibility remains.");}
            });
            call.Original=true;return token;
        }
        catch{Fault(call?.Sinks??published,frame,"Scheduler actual Enter is unconfirmed.");if(call is not null){call.Original=true;return token;}return 0;}
    }
    private void Exit(int kind,NativeEngineSchedulerFrame frame,nuint token,NativeEngineSchedulerCompletion completion)
    {
        if(token==0 || !_calls.TryGetValue(token,out var call) || !ReferenceEquals(_top,call)
            || call.Thread!=Environment.CurrentManagedThreadId || call.Completing || !call.Frame.Equals(frame))
        {Fault(Volatile.Read(ref _published),frame,"Scheduler Exit lacks its exact physical token, frame or parent.");return;}
        call.Completing=true;call.Original=false;
        try
        {
            if(!Matches(frame,kind) || !completion.Normal || call.OpenRow is not null
                ||completion.RowBegins!=call.RowBegins||completion.RowEnds!=call.RowEnds
                ||(frame.Kind==NativeEngineSchedulerKind.Register?completion.OutputFlags!=3:completion.OutputFlags!=0||completion.OutputEntry!=IntPtr.Zero))
                Fault(call.Sinks,frame,"Scheduler native completion or Drain row duty is unconfirmed.");
            Deliver(call,completion,null,receipt=>{foreach(var item in call.Contexts)try{item.Sink.Complete(item.Context,receipt);}
                catch{Fault(call.Sinks,frame,"Scheduler consumer Complete failed.");}});
        }
        finally
        {
            for(var index=call.Contexts.Count-1;index>=0;--index)try{call.Contexts[index].Context.Dispose();}
                catch{Fault(call.Sinks,frame,"Scheduler consumer Dispose failed.");}
            _calls.TryRemove(token,out _);_top=call.Parent;Interlocked.Decrement(ref _inFlight);lock(_gate)Publish();
        }
    }
    private void Row(NativeEngineSchedulerRowFrame frame)
    {
        var call=_top;
        try
        {
            var identity=_identities[(int)NativeEngineSchedulerKind.Drain];
            if(call is null || !ReferenceEquals(call.Owner,this) || call.Frame.Kind!=NativeEngineSchedulerKind.Drain || !call.Proof.IsOriginal
                || !frame.IsValid || frame.HasFault || frame.DispatchSequence!=call.Frame.Sequence || frame.ParentSequence!=call.Frame.ParentSequence
                || frame.RootSequence!=call.Frame.RootSequence || frame.Depth!=call.Frame.Depth || frame.ThreadId!=call.Frame.ThreadId
                || frame.Slot!=identity.Slot || frame.Epoch!=identity.Epoch || frame.EventSequence<=_lastRowEvent)
                throw new InvalidOperationException("Drain row has no actual current Drain scope.");
            _lastRowEvent=frame.EventSequence;
            var site=frame.Kind==NativeEngineSchedulerRowKind.Begin?identity.Begin:identity.End;
            if(frame.SitePc!=site.SitePc || frame.RelayReturnPc!=site.RelayReturnPc)throw new InvalidOperationException("Drain row relay/source mismatch.");
            if(frame.Kind==NativeEngineSchedulerRowKind.Begin)
            {if(call.OpenRow is not null)throw new InvalidOperationException("Drain row Begin overlaps an unclosed row.");call.OpenRow=frame;++call.RowBegins;}
            else if(call.OpenRow is not { } begin || begin.RowSequence!=frame.RowSequence || begin.Entry!=frame.Entry || begin.Tree!=frame.Tree
                || begin.Manager!=frame.Manager || !begin.Record.Equals(frame.Record)||begin.NodeOwner!=frame.NodeOwner
                ||begin.EnumeratorHandle!=frame.EnumeratorHandle||begin.EnumeratorCache!=frame.EnumeratorCache
                ||begin.EnumeratorType!=frame.EnumeratorType||begin.ReferenceCount!=frame.ReferenceCount
                ||begin.NodeSnapshotFlags!=frame.NodeSnapshotFlags||begin.Flags!=frame.Flags)
                throw new InvalidOperationException("Drain row End has no exact cached Begin.");
            Deliver(call,null,frame,receipt=>{foreach(var item in call.Contexts)try{item.Sink.Row(item.Context,receipt);}
                catch{Fault(call.Sinks,call.Frame,"Scheduler row observer failed.");}});
            if(frame.Kind==NativeEngineSchedulerRowKind.End){call.OpenRow=null;++call.RowEnds;}
        }
        catch{Fault(call?.Sinks??Volatile.Read(ref _published),call?.Frame??default,"Drain row physical observation is unconfirmed.");}
    }
    private void Deliver(Physical call,NativeEngineSchedulerCompletion? completion,NativeEngineSchedulerRowFrame? row,Action<Observation> deliver)
    {
        var previous=_delivery;var receipt=new Observation(this,_issuer,call.Proof,completion,row);_delivery=receipt;
        try{deliver(receipt);}finally{_delivery=previous;}
    }
    private void Fault(Sink[] sinks,NativeEngineSchedulerFrame frame,string reason)
    {Interlocked.Exchange(ref _unknown,1);foreach(var sink in sinks)try{sink.Fault(frame,reason);}catch{}}
    internal ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        long count=Math.Max(0,Volatile.Read(ref _inFlight));
        foreach(var bridge in _bridges)try{var value=bridge.ReadActivity();if(!value.Known || value.InFlight<0 || count>long.MaxValue-value.InFlight)Interlocked.Exchange(ref _unknown,1);else count+=value.InFlight;}
            catch{Interlocked.Exchange(ref _unknown,1);}
        return new(count,!Unknown);
    }
}
