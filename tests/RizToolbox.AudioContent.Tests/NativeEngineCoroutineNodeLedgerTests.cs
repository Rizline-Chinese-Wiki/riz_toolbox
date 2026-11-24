using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;

internal static class NativeEngineCoroutineNodeLedgerTests
{
    internal static readonly (string Name,Action Test)[] Cases = {
        ("Engine node ledger binds synchronous first execution before Start output without engine cancel credit", SynchronousStart),
        ("Engine node ledger distinguishes detach final release and still active execution responsibility", ReleaseInsideExecution),
        ("Engine node ledger assigns reused addresses a new generation only after exact final cleanup and scope drain", Reuse),
        ("Engine node ledger refuses live address reuse changed enum handles and unobserved Start outputs", IdentityFailures),
        ("Engine node ledger retains inline cleanup uncertainty and foreign nested Starts cannot borrow parents", InlineAndForeign),
        ("Engine node ledger preserves abnormal and unreadable responsibility with exact disposal", Faults),
        ("Engine node ledger rejects duplicate wrong parent and foreign completions without losing original scope", FrameFailures),
        ("Engine node ledger bounds records preserves old snapshots and faults workers through scalars only", BoundsAndWorkers)
    };
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Engine node ledger check failed."); }
    private sealed class F
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly NativeEngineCoroutineNodeLedger Ledger;
        private ulong _next;
        internal F() => Ledger = new(Main);
        internal NativeEngineNodeFrame Start(long owner=101,long enumerator=201) => new() {
            Version=1,Size=112,Kind=NativeEngineNodeKind.Start,Arg0=new(owner),Arg1=new(enumerator),
            Arg3=new(0x9000),Caller=new(0x8000),Sequence=++_next };
        internal NativeEngineNodeFrame Node(NativeEngineNodeKind kind,long node=301,long owner=101,long enumerator=201,ulong handle=401) => new() {
            Version=1,Size=112,Kind=kind,SnapshotFlags=NativeEngineNodeSnapshotFlags.Requested|NativeEngineNodeSnapshotFlags.Valid,
            Arg0=new(node),Node=new(node),Owner=new(owner),EnumeratorCache=new(enumerator),EnumeratorHandle=handle,
            EnumeratorType=2,ReferenceCount=17,Caller=new(0x8000),Sequence=++_next };
        internal static NativeEngineNodeCompletion Done(NativeEngineNodeKind kind,uint raw=1,long output=0) => new() {
            Version=1,Size=32,Flags=NativeEngineNodeExitFlags.Returned | (kind==NativeEngineNodeKind.Detach?0:NativeEngineNodeExitFlags.ReturnByteValid),
            RawReturn=kind==NativeEngineNodeKind.Detach?0:raw,StartOutputNode=new(output),
            OutputFlags=kind==NativeEngineNodeKind.Start?NativeEngineNodeOutputFlags.Requested|NativeEngineNodeOutputFlags.Read:0 };
        internal void Return(NativeEngineCoroutineNodeLedger.Call c,uint raw=1,long output=0)
        { Ledger.Complete(c,c.Frame,Done(c.Frame.Kind,raw,output));c.Dispose(); }
        internal NativeEngineCoroutineNodeSnapshot One() => Ledger.Read().Nodes.Single();
        internal void Begin(long node=301)
        { var s=Ledger.Capture(Start());var e=Ledger.Capture(Node(NativeEngineNodeKind.Execute,node));Return(e);Return(s,output:node); }
    }
    private static void SynchronousStart()
    {
        var f=new F();var start=f.Ledger.Capture(f.Start());Check(f.Ledger.Read().Nodes.Count==0);
        var execute=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Execute));var before=f.Ledger.Read();
        Check(before.Outstanding==2 && before.Nodes.Single() is { ActiveExecutions:1, StartCompleted:false, Enumerator:201,NativeOwner:101 });
        f.Return(execute,raw:0xfe);Check(f.One().ActiveExecutions==0 && f.One().ExecuteCalls==1);
        f.Return(start,raw:0x80,output:301);var state=f.Ledger.Read();
        Check(!state.Unknown && state.Outstanding==0 && state.Pending && !state.EngineCancelled && f.One().StartCompleted);
        Check(before.Nodes.Single().ActiveExecutions==1 && !before.Nodes.Single().StartCompleted);
    }
    private static void ReleaseInsideExecution()
    {
        var f=new F();f.Begin();var e=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Execute));
        var d=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Detach));f.Return(d);
        Check(f.One() is {DetachObserved:true,DetachReturned:true,ActiveExecutions:1,FinalCleanupObserved:false});
        var r=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Release,owner:0));f.Return(r,raw:1);
        Check(f.One() is {FinalCleanupObserved:false,ActiveExecutions:1,LastReleaseByte:1});
        r=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Release,owner:0));f.Return(r,raw:0);
        Check(f.One() is {FinalCleanupObserved:true,ActiveExecutions:1});
        // Native original may have freed its node. Completion uses only the
        // captured frame; no read callback or pointer accessor exists here.
        f.Return(e,raw:0xff);Check(!f.Ledger.Unknown && f.One().ActiveExecutions==0
            && f.Ledger.Read() is {Pending:true,EngineCancelled:false,Outstanding:0});
    }
    private static void Reuse()
    {
        var f=new F();f.Begin();var old=f.One();var release=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Release));f.Return(release,raw:0);
        var s=f.Ledger.Capture(f.Start(owner:102,enumerator:202));var e=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Execute,owner:102,enumerator:202,handle:402));
        f.Return(e);f.Return(s,output:301);var rows=f.Ledger.Read().Nodes;
        Check(!f.Ledger.Unknown && rows.Count==2 && rows[0].Node==rows[1].Node && rows[1].Generation>rows[0].Generation
            && rows[0].FinalCleanupObserved && rows[1].Enumerator==202 && old.Enumerator==201);
    }
    private static void IdentityFailures()
    {
        var f=new F();f.Begin();var s=f.Ledger.Capture(f.Start());var e=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Execute));
        Check(f.Ledger.Unknown);f.Return(e);f.Return(s,output:301);Check(f.Ledger.Read().Outstanding==0);
        f=new F();f.Begin();e=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Execute,handle:999));Check(f.Ledger.Unknown);f.Return(e);
        Check(f.One().EnumeratorHandle==401 && f.Ledger.Read().Outstanding==0);
        f=new F();s=f.Ledger.Capture(f.Start());f.Return(s,output:301);Check(f.Ledger.Unknown && f.Ledger.Read().Nodes.Count==0);
    }
    private static void InlineAndForeign()
    {
        var f=new F();var s=f.Ledger.Capture(f.Start());var e=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Execute));f.Return(e);
        f.Return(s,raw:0,output:0);Check(!f.Ledger.Unknown && f.One().InlineCleanupUnproven && !f.One().FinalCleanupObserved);
        f=new F();s=f.Ledger.Capture(f.Start());var foreign=f.Ledger.Capture(f.Start(owner:999,enumerator:999));
        e=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Execute));f.Return(e);f.Return(foreign);f.Return(s);
        Check(!f.Ledger.Unknown && f.Ledger.Read().Nodes.Count==0);
        f=new F();e=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Execute));f.Return(e);
        Check(!f.Ledger.Unknown && f.Ledger.Read() is {Nodes.Count:0,Pending:false,EngineCancelled:false});
    }
    private static void Faults()
    {
        var f=new F();f.Begin();var e=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Execute));
        var bad=new NativeEngineNodeCompletion {Version=1,Size=32,Flags=NativeEngineNodeExitFlags.Abnormal};
        f.Ledger.Complete(e,e.Frame,bad);e.Dispose();Check(f.Ledger.Unknown && f.One().ActiveExecutions==0 && f.Ledger.Read().Outstanding==0);
        f=new F();var frame=f.Node(NativeEngineNodeKind.Execute);frame.SnapshotFlags=NativeEngineNodeSnapshotFlags.Requested|NativeEngineNodeSnapshotFlags.Faulted;
        frame.Owner=frame.EnumeratorCache=IntPtr.Zero;frame.EnumeratorHandle=frame.EnumeratorType=0;frame.ReferenceCount=0;
        e=f.Ledger.Capture(frame);f.Return(e);Check(f.Ledger.Unknown && f.Ledger.Read().Outstanding==0);
        f=new F();var s=f.Ledger.Capture(f.Start());bad=F.Done(NativeEngineNodeKind.Start);bad.OutputFlags=NativeEngineNodeOutputFlags.Requested|NativeEngineNodeOutputFlags.Faulted;
        f.Ledger.Complete(s,s.Frame,bad);s.Dispose();Check(f.Ledger.Unknown && f.Ledger.Read().Outstanding==0);
    }
    private static void FrameFailures()
    {
        var f=new F();var s=f.Ledger.Capture(f.Start());var e=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Execute));
        f.Ledger.Complete(s,s.Frame,F.Done(NativeEngineNodeKind.Start));Check(f.Ledger.Unknown && f.Ledger.Read().Outstanding==2);
        f.Return(e);f.Return(s);Check(f.Ledger.Read().Outstanding==0);s.Dispose();Check(f.Ledger.Unknown);
        f=new F();s=f.Ledger.Capture(f.Start());var wrong=s.Frame;wrong.Caller+=1;
        f.Ledger.Complete(s,wrong,F.Done(NativeEngineNodeKind.Start));Check(f.Ledger.Unknown && f.Ledger.Read().Outstanding==1);f.Return(s);
        f=new F();s=f.Ledger.Capture(f.Start());var other=new F();other.Ledger.Complete(s,s.Frame,F.Done(NativeEngineNodeKind.Start));
        Check(other.Ledger.Unknown && !f.Ledger.Unknown);f.Return(s);
    }
    private static void BoundsAndWorkers()
    {
        var f=new F();var frame=f.Start();Task.Run(()=>f.Ledger.ObserveFault(frame,"Worker observation rejected.")).GetAwaiter().GetResult();
        Check(f.Ledger.Unknown && f.Ledger.Read().Calls==0);
        var rejected=Task.Run(()=>{try{f.Ledger.Capture(frame);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult();Check(rejected);
        f=new F();for(var i=0;i<NativeEngineCoroutineNodeLedger.MaximumCalls;++i){var c=f.Ledger.Capture(f.Start());f.Return(c);}
        var saved=f.Ledger.Read();f.Ledger.Capture(f.Start());var full=f.Ledger.Read();
        Check(!saved.Unknown && saved.Outstanding==0 && full.Unknown && full.Calls==NativeEngineCoroutineNodeLedger.MaximumCalls
            && full.Untracked==1 && full.Outstanding==1);
    }
}
