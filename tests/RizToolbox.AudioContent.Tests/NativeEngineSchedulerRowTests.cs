using System.Runtime.InteropServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Actual six-channel dispatcher and actual identity/node/row ledgers with
// controlled scalar bridges. No native execution or production authority.
internal static class NativeEngineSchedulerRowTests
{
    internal static readonly (string Name,Action Test)[] Cases={
        ("Scheduler independent ABI1 preserves source record frame completion DrainRow and config layouts",Layout),
        ("Scheduler actual registration output slot records independent rows for one exact known node",Registrations),
        ("Scheduler consumed queue row stays pending through the actual callback gap",ConsumedGap),
        ("Scheduler callback compares complete EAX rather than AL for conditional cleanup",FullEax),
        ("Scheduler normal callback EAX1 waits for actual Release Complete and Dispose before row retirement",ConditionalCleanup),
        ("Scheduler repeat registers a new row before consuming old without adding node references",Repeat),
        ("Scheduler Remove cleanup completes its own row duty without claiming node final release",Remove),
        ("Scheduler Drain row Begin End separates callback and cleanup and waits for outer finally",Drain),
        ("Scheduler missing callback cleanup or row End remains Unknown with retained obligation",Missing),
        ("Scheduler foreign physical Pump masks an older owned consumed row",ForeignMask),
        ("Scheduler exact registration and node generation reject tuple drift and late entry reuse",IdentityDrift),
        ("Scheduler worker and stale receipts cannot lend main thread physical completion",WorkerAndStale),
        ("Scheduler logical detach preserves captured original descendants and cleanup duties",Detach),
        ("Scheduler releases completed physical masks while retaining a pending consumed row",CompletedMasks),
        ("Scheduler refuses too many simultaneously open physical masks",Bounds),
        ("Scheduler configured information rejects helper identity drift and sticky native faults",Information)
        ,("Scheduler callback reentry can register another independent row without retiring its producer",CallbackReentry)
        ,("Scheduler owner mismatch callback EAX1 requires actual conditional cleanup without Execute",OwnerMismatch)
        ,("Scheduler retired tree entry address reuse creates another registration generation",EntryReuse)
        ,("Scheduler callback and cleanup abnormal originals retain row responsibility",Abnormal)
        ,("Scheduler recursive Release is not a second cleanup receipt and row delivery cannot forge original phase",CleanupBoundaries)
        ,("Scheduler per generation retirement waits for its real registered row callback cleanup and Pump Dispose",PerGenerationReadiness)
    };
    private static void Check(bool value,string reason="Controlled scheduler regression failed."){if(!value)throw new InvalidOperationException(reason);}
    private sealed class Logger:ILogger{public void Log(LogLevel level,string message,Exception? error=null){}}
    private sealed class Bridge:INativeEngineSchedulerContextBridge
    {
        private Func<NativeEngineSchedulerFrame,nuint>? _enter;
        private Action<NativeEngineSchedulerFrame,nuint,NativeEngineSchedulerCompletion>? _exit;
        private Action<NativeEngineSchedulerRowFrame>? _row;
        public NativeEngineSchedulerChannelIdentity Identity{get;}
        internal Bridge(int kind)
        {
            Identity=new((NativeEngineSchedulerKind)kind,4,(ulong)kind+10,NativeEngineInlinePlatform.CurrentOsThreadId,
                new(0x10000+kind*0x100),new(0x20000+kind*0x100),new(0x30000+kind*0x100),new(0x40000),
                kind==4?new(){SitePc=new(0x50000),RelayReturnPc=new(0x60000)}:default,
                kind==4?new(){SitePc=new(0x50010),RelayReturnPc=new(0x60010)}:default);
        }
        public void Attach(Func<NativeEngineSchedulerFrame,nuint> enter,Action<NativeEngineSchedulerFrame,nuint,NativeEngineSchedulerCompletion> exit,Action<NativeEngineSchedulerRowFrame>? row)
        {Check(_enter is null);(_enter,_exit,_row)=(enter,exit,row);}
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()=>new(0,true);
        internal nuint Enter(NativeEngineSchedulerFrame frame)=>_enter!(frame);
        internal void Exit(NativeEngineSchedulerFrame frame,nuint token,NativeEngineSchedulerCompletion completion)=>_exit!(frame,token,completion);
        internal void Row(NativeEngineSchedulerRowFrame frame)=>_row!(frame);
    }
    private sealed record Physical(NativeEngineSchedulerFrame Frame,nuint Token);
    private sealed class F:IDisposable
    {
        internal const long Node=301,NativeOwner=0x12340000,Manager=0x4000,Tree=Manager+0x30,Callback=0x10500,Cleanup=0x8000;
        internal readonly MainThreadDispatcher Main=new();
        internal readonly Bridge[] Bridges=Enumerable.Range(0,6).Select(value=>new Bridge(value)).ToArray();
        internal readonly NativeEngineSchedulerObserverDispatcher Dispatcher;
        internal readonly NativeEngineCoroutineNodeLedger Nodes;
        internal readonly NativeEngineCoroutineIdentityLedger Identities;
        internal readonly NativeEngineSchedulerRowLedger Rows;
        internal readonly NativeEngineSchedulerObserverDispatcher.Binding Binding;
        internal readonly object Tracker=new();
        internal readonly List<Physical> Open=new();
        internal NativeEngineSchedulerObserverDispatcher.Observation? LastReceipt;
        internal int ProviderReads;
        private ulong _sequence,_nodeSequence,_rowEvent;
        internal F()
        {
            var row=new NativeCoroutineCancellationIdentity(Tracker,101,0,1,1001,0,false,false,true,false,false,true);
            Nodes=new(Main);Identities=new(Main,Nodes,new[]{new NativeCoroutineCancellationIdentityProvider(Tracker,101,
                ()=>{++ProviderReads;return new[]{row};},value=>row.SameRecord(value))},new[]{new NativeEngineCoroutineOwnerPair(101,NativeOwner)});
            var s=Identities.Capture(NodeFrame(NativeEngineNodeKind.Start));var e=Identities.Capture(NodeFrame(NativeEngineNodeKind.Execute));
            Identities.Complete(e,e.Frame,NodeDone(NativeEngineNodeKind.Execute));e.Dispose();
            Identities.Complete(s,s.Frame,NodeDone(NativeEngineNodeKind.Start,output:Node));s.Dispose();
            Dispatcher=new(Bridges,new Logger());Rows=new(Main,Identities,Nodes,Dispatcher,new(Callback),new(Cleanup),
                new(new(0x9100),new(0x9110),new(0x9120),new(0x9200),new(0x9210),new(0x9220)));
            Binding=Dispatcher.Bind(receipt=>{LastReceipt=receipt;return Rows.Capture(receipt);},Rows.Complete,Rows.ObserveRow,Rows.ObserveFault);
        }
        internal NativeEngineSchedulerRecord Record(uint repeat=0)=>new(){DueTimeBits=123,Frame=42,IntervalBits=17,Repeat=repeat,
            Payload=new(Node),Callback=new(Callback),Cleanup=new(Cleanup),OwnerId=7,Category=1,CreationTag=9};
        internal NativeEngineSchedulerFrame Frame(NativeEngineSchedulerKind kind,long entry=701,NativeEngineSchedulerRecord? record=null,ulong source=0)
        {
            var parent=Open.LastOrDefault();var identity=Bridges[(int)kind].Identity;var frame=new NativeEngineSchedulerFrame{
                Version=1,Size=256,Kind=kind,Flags=NativeEngineSchedulerFlags.MainThread|NativeEngineSchedulerFlags.ParentMatched,
                Slot=identity.Slot,ThreadId=identity.MainThreadId,Depth=(uint)(Open.Count+1),Epoch=identity.Epoch,Sequence=++_sequence,
                ParentSequence=parent?.Frame.Sequence??0,RootSequence=parent?.Frame.RootSequence??_sequence,SourceSequence=source,
                Caller=new(0xa000),Tree=new(Tree),Manager=new(Manager),Entry=new(entry),Record=record??Record()};
            if(kind==NativeEngineSchedulerKind.Register){frame.Arg0=new(Tree);frame.Arg1=frame.OutputSlot=new(0xb000);frame.Arg2=frame.SourceRecord=new(0xc000);frame.Manager=frame.Entry=IntPtr.Zero;}
            else if(kind is NativeEngineSchedulerKind.Remove or NativeEngineSchedulerKind.Consume){frame.Arg0=new(Manager);frame.Arg2=new(entry);frame.SourceRecord=new(entry+0x20);if(kind==NativeEngineSchedulerKind.Remove)frame.Arg1=frame.SourceRecord;}
            else if(kind==NativeEngineSchedulerKind.Callback){frame.Arg0=new(NativeOwner);frame.Arg1=frame.Record.Payload;frame.SourceRecord=new(entry+0x20);frame.Caller=new(frame.Record.Repeat==0?0x9100:0x9110);frame.Flags|=NativeEngineSchedulerFlags.SourceConsume|NativeEngineSchedulerFlags.SnapshotCached;}
            else{frame.Arg0=new(Manager);frame.Entry=IntPtr.Zero;frame.Record=default;}
            if(kind is NativeEngineSchedulerKind.Register or NativeEngineSchedulerKind.Remove or NativeEngineSchedulerKind.Consume or NativeEngineSchedulerKind.Callback)
            {frame.Flags|=NativeEngineSchedulerFlags.SnapshotRequested|NativeEngineSchedulerFlags.SnapshotValid;frame.NodeSnapshotFlags=3;
                frame.NodeOwner=new(NativeOwner);frame.EnumeratorHandle=401;frame.EnumeratorCache=new(1001);frame.EnumeratorType=2;frame.ReferenceCount=2;}
            return frame;
        }
        internal Physical Begin(NativeEngineSchedulerFrame frame){var call=new Physical(frame,Bridges[(int)frame.Kind].Enter(frame));Open.Add(call);return call;}
        internal void End(Physical call,ulong raw=0,long output=0,ulong rowCount=0,bool abnormal=false)
        {
            var result=new NativeEngineSchedulerCompletion{Version=1,Size=48,Flags=abnormal?NativeEngineSchedulerExitFlags.Abnormal:
                NativeEngineSchedulerExitFlags.Returned|NativeEngineSchedulerExitFlags.RawRaxValid,RawRax=raw,OutputEntry=new(output),
                OutputFlags=call.Frame.Kind==NativeEngineSchedulerKind.Register?3u:0u,RowBegins=rowCount,RowEnds=rowCount};
            Bridges[(int)call.Frame.Kind].Exit(call.Frame,call.Token,result);Open.Remove(call);
        }
        internal void Register(long entry=701,uint repeat=0,long source=0)
        {var frame=Frame(NativeEngineSchedulerKind.Register,record:Record(repeat));if(source!=0)frame.Arg2=frame.SourceRecord=new(source);var call=Begin(frame);End(call,(ulong)frame.OutputSlot.ToInt64(),entry);}
        internal Physical Pump()=>Begin(Frame(NativeEngineSchedulerKind.Pump));
        internal ulong Consume(long entry=701,uint repeat=0)
        {var call=Begin(Frame(NativeEngineSchedulerKind.Consume,entry,Record(repeat)));End(call);return call.Frame.Sequence;}
        internal Physical CallbackCall(ulong consume,uint repeat=0)=>Begin(Frame(NativeEngineSchedulerKind.Callback,record:Record(repeat),source:consume));
        internal NativeEngineCoroutineNodeLedger.Call Release(long caller=0x9210,uint raw=1,bool dispose=true)
        {
            var frame=NodeFrame(NativeEngineNodeKind.Release);frame.Caller=new(caller);var call=Identities.Capture(frame);
            var context=Rows.CaptureCleanup(call.NodeCall!);Check(context is not null);
            Identities.Complete(call,frame,NodeDone(NativeEngineNodeKind.Release,raw));context!.Complete();
            Check(!Rows.Read().Records.Single(value=>value.State==NativeEngineSchedulerRowState.CleanupOpen).CleanupCompleted);
            if(dispose){call.Dispose();context.Dispose();}return call.NodeCall!;
        }
        internal void NestedCleanup()
        {
            var outer=Identities.Capture(ReleaseFrame(0x9210));var duty=Rows.CaptureCleanup(outer.NodeCall!);Check(duty is not null);
            var nested=Identities.Capture(ReleaseFrame(0x7777));Check(Rows.CaptureCleanup(nested.NodeCall!) is null);
            Identities.Complete(nested,nested.Frame,NodeDone(NativeEngineNodeKind.Release));nested.Dispose();
            Identities.Complete(outer,outer.Frame,NodeDone(NativeEngineNodeKind.Release));duty!.Complete();outer.Dispose();duty.Dispose();
        }
        internal void AbnormalCleanup()
        {
            var call=Identities.Capture(ReleaseFrame(0x9210));var context=Rows.CaptureCleanup(call.NodeCall!);Check(context is not null);
            Identities.Complete(call,call.Frame,new(){Version=1,Size=32,Flags=NativeEngineNodeExitFlags.Abnormal});
            context!.Complete();call.Dispose();context.Dispose();
        }
        private NativeEngineNodeFrame ReleaseFrame(long caller){var frame=NodeFrame(NativeEngineNodeKind.Release);frame.Caller=new(caller);return frame;}
        internal void Row(Physical drain,bool begin,ulong rowSequence=900,long entry=701,NativeEngineSchedulerRecord? record=null)
        {
            var identity=Bridges[4].Identity;var site=begin?identity.Begin:identity.End;
            Bridges[4].Row(new(){Version=1,Size=224,Kind=begin?NativeEngineSchedulerRowKind.Begin:NativeEngineSchedulerRowKind.End,
                Flags=NativeEngineSchedulerFlags.MainThread|NativeEngineSchedulerFlags.ParentMatched|NativeEngineSchedulerFlags.SnapshotRequested|NativeEngineSchedulerFlags.SnapshotValid,
                Slot=identity.Slot,ThreadId=identity.MainThreadId,Depth=drain.Frame.Depth,Epoch=identity.Epoch,DispatchSequence=drain.Frame.Sequence,
                ParentSequence=drain.Frame.ParentSequence,RootSequence=drain.Frame.RootSequence,RowSequence=rowSequence,EventSequence=++_rowEvent,
                Tree=new(Tree),Manager=new(Manager),Entry=new(entry),SitePc=site.SitePc,RelayReturnPc=site.RelayReturnPc,Record=record??Record(),
                NodeOwner=new(NativeOwner),EnumeratorHandle=401,EnumeratorCache=new(1001),EnumeratorType=2,ReferenceCount=2,NodeSnapshotFlags=3});
        }
        private NativeEngineNodeFrame NodeFrame(NativeEngineNodeKind kind)
        {
            var frame=new NativeEngineNodeFrame{Version=1,Size=112,Kind=kind,Sequence=++_nodeSequence,Caller=new(0xd000)};
            if(kind==NativeEngineNodeKind.Start){frame.Arg0=new(NativeOwner);frame.Arg1=new(1001);frame.Arg3=new(0xe000);}
            else{frame.Arg0=frame.Node=new(Node);frame.Owner=new(NativeOwner);frame.EnumeratorHandle=401;frame.EnumeratorCache=new(1001);
                frame.EnumeratorType=2;frame.ReferenceCount=2;frame.SnapshotFlags=NativeEngineNodeSnapshotFlags.Requested|NativeEngineNodeSnapshotFlags.Valid;}
            return frame;
        }
        private static NativeEngineNodeCompletion NodeDone(NativeEngineNodeKind kind,uint raw=1,long output=0)=>new(){Version=1,Size=32,
            Flags=NativeEngineNodeExitFlags.Returned|NativeEngineNodeExitFlags.ReturnByteValid,RawReturn=raw,StartOutputNode=new(output),
            OutputFlags=kind==NativeEngineNodeKind.Start?NativeEngineNodeOutputFlags.Requested|NativeEngineNodeOutputFlags.Read:0};
        public void Dispose(){foreach(var call in Open.AsEnumerable().Reverse().ToArray())End(call,abnormal:true);Binding.Dispose();Main.Dispose();}
    }
    private static void Layout()
    {
        Check(Marshal.SizeOf<NativeEngineSchedulerRecord>()==64&&Marshal.SizeOf<NativeEngineSchedulerFrame>()==256
            &&Marshal.SizeOf<NativeEngineSchedulerCompletion>()==48&&Marshal.SizeOf<NativeEngineSchedulerRowFrame>()==224
            &&Marshal.SizeOf<NativeEngineSchedulerConfiguration>()==64&&Marshal.SizeOf<NativeEngineSchedulerInformation>()==144);
        Check(Marshal.OffsetOf<NativeEngineSchedulerFrame>(nameof(NativeEngineSchedulerFrame.SourceSequence)).ToInt32()==64
            &&Marshal.OffsetOf<NativeEngineSchedulerFrame>(nameof(NativeEngineSchedulerFrame.Record)).ToInt32()==152
            &&Marshal.OffsetOf<NativeEngineSchedulerRowFrame>(nameof(NativeEngineSchedulerRowFrame.Record)).ToInt32()==120
            &&Marshal.OffsetOf<NativeEngineSchedulerCompletion>(nameof(NativeEngineSchedulerCompletion.RawRax)).ToInt32()==16);
    }
    private static void Registrations()
    {using var f=new F();f.Register();f.Register(702);var rows=f.Rows.Read();Check(!rows.Unknown&&rows.Rows==2&&rows.Outstanding==0&&rows.Pending
        &&rows.Records.Select(value=>value.RegistrationGeneration).Distinct().Count()==2&&rows.Records.All(value=>value.NodeGeneration==1&&!value.NativeReferenceAdded));}
    private static void ConsumedGap()
    {using var f=new F();f.Register();var p=f.Pump();var source=f.Consume();Check(f.Rows.Read().Records.Single().State==NativeEngineSchedulerRowState.ConsumedPendingCallback);
        var c=f.CallbackCall(source);Check(f.Rows.Read().Records.Single().State==NativeEngineSchedulerRowState.CallbackOpen);f.End(c,0);f.End(p);
        Check(!f.Rows.Unknown&&f.Rows.Read().Records.Single().State==NativeEngineSchedulerRowState.Retired&&!f.Rows.Read().EngineCancelled&&!f.Rows.Read().FutureClosed);}
    private static void FullEax()
    {using var f=new F();f.Register();var p=f.Pump();var source=f.Consume();var c=f.CallbackCall(source);f.End(c,0x10001);f.End(p);
        Check(!f.Rows.Unknown&&f.Rows.Read().Records.Single() is {State:NativeEngineSchedulerRowState.Retired,CallbackRawRax:0x10001,CleanupCompleted:false});}
    private static void ConditionalCleanup()
    {using var f=new F();f.Register();var p=f.Pump();var source=f.Consume();var c=f.CallbackCall(source);f.End(c,1);
        Check(f.Rows.Read().Records.Single().State==NativeEngineSchedulerRowState.CleanupPending);f.Release();
        Check(f.Rows.Read().Records.Single().State!=NativeEngineSchedulerRowState.Retired);f.End(p);
        Check(!f.Rows.Unknown&&f.Rows.Read().Records.Single() is {State:NativeEngineSchedulerRowState.Retired,CleanupCompleted:true}&&!f.Nodes.Read().Nodes.Single().FinalCleanupObserved);}
    private static void Repeat()
    {using var f=new F();f.Register(repeat:1);var p=f.Pump();f.Register(702,1,701+0x20);var source=f.Consume(repeat:1);var c=f.CallbackCall(source,1);f.End(c,1);f.End(p);
        var rows=f.Rows.Read();Check(!rows.Unknown&&rows.Records[0].State==NativeEngineSchedulerRowState.Retired
            &&rows.Records[1] is {State:NativeEngineSchedulerRowState.Registered,RepeatCopy:true,NativeReferenceAdded:false}&&rows.Pending);}
    private static void Remove()
    {using var f=new F();f.Register();var remove=f.Begin(f.Frame(NativeEngineSchedulerKind.Remove));f.Release(0x9200);f.End(remove);
        Check(!f.Rows.Unknown&&f.Rows.Read().Records.Single() is {State:NativeEngineSchedulerRowState.Retired,CleanupCompleted:true}&&!f.Nodes.Read().Nodes.Single().FinalCleanupObserved);}
    private static void Drain()
    {
        using(var f=new F()){f.Register();var d=f.Begin(f.Frame(NativeEngineSchedulerKind.Drain));f.Row(d,true);Check(f.Rows.Read().Records.Single().State==NativeEngineSchedulerRowState.DrainOpen);
        f.Release(0x9220);f.Row(d,false);Check(f.Rows.Read().Records.Single().State!=NativeEngineSchedulerRowState.Retired);f.End(d,rowCount:1);
        Check(!f.Rows.Unknown&&f.Rows.Read().Records.Single().State==NativeEngineSchedulerRowState.Retired);}
        using(var f=new F())
        {
            var record=f.Record();record.Category=0x40;var registration=f.Begin(f.Frame(NativeEngineSchedulerKind.Register,record:record));f.End(registration,0xb000,701);
            var d=f.Begin(f.Frame(NativeEngineSchedulerKind.Drain));f.Row(d,true,record:record);
            var callback=f.Frame(NativeEngineSchedulerKind.Callback,record:record,source:900);
            callback.Flags&=~(NativeEngineSchedulerFlags.SourceConsume|NativeEngineSchedulerFlags.SnapshotCached|NativeEngineSchedulerFlags.SnapshotRequested|NativeEngineSchedulerFlags.SnapshotValid);
            callback.Flags|=NativeEngineSchedulerFlags.SourceDrainRow;callback.Caller=new(0x9120);
            callback.Record=new(){Payload=new(F.Node),Callback=new(F.Callback)};
            var c=f.Begin(callback);f.End(c,0x10001);f.Release(0x9220);f.Row(d,false,record:record);f.End(d,rowCount:1);
            Check(!f.Rows.Unknown&&f.Rows.Read().Records.Single() is {State:NativeEngineSchedulerRowState.Retired,CleanupCompleted:true});
        }
    }
    private static void Missing()
    {
        using(var f=new F()){f.Register();var p=f.Pump();f.Consume();f.End(p);Check(f.Rows.Unknown&&f.Rows.Read().Pending);}
        using(var f=new F()){f.Register();var p=f.Pump();var source=f.Consume();var c=f.CallbackCall(source);f.End(c,1);f.End(p);Check(f.Rows.Unknown);}
        using(var f=new F()){f.Register();var d=f.Begin(f.Frame(NativeEngineSchedulerKind.Drain));f.Row(d,true);f.Release(0x9220);f.End(d);Check(f.Rows.Unknown&&f.Rows.Read().Records.Single().State!=NativeEngineSchedulerRowState.Retired);}
    }
    private static void ForeignMask()
    {using var f=new F();f.Register();var p=f.Pump();var source=f.Consume();var foreign=f.Pump();var c=f.CallbackCall(source);f.End(c);f.End(foreign);f.End(p);Check(f.Rows.Unknown);}
    private static void IdentityDrift()
    {
        using(var f=new F()){f.Register();var frame=f.Frame(NativeEngineSchedulerKind.Remove);frame.Record.Payload+=1;
            var c=f.Begin(frame);f.End(c);Check(f.Rows.Unknown);}
        using(var f=new F()){f.Register();var remove=f.Begin(f.Frame(NativeEngineSchedulerKind.Remove));f.Release(0x9200);f.End(remove);
            var late=f.Begin(f.Frame(NativeEngineSchedulerKind.Remove));f.End(late);Check(f.Rows.Unknown&&f.Rows.Read().Records.Single().State==NativeEngineSchedulerRowState.Retired);}
    }
    private static void WorkerAndStale()
    {
        using(var f=new F()){f.Register();var receipt=f.LastReceipt!;Check(!receipt.IsCurrent);f.Rows.Capture(receipt);Check(f.Rows.Unknown);}
        using(var f=new F()){f.Register();var p=f.Pump();var bad=f.Frame(NativeEngineSchedulerKind.Consume);
            Task.Run(()=>{var token=f.Bridges[2].Enter(bad);f.Bridges[2].Exit(bad,token,new(){Version=1,Size=48,
                Flags=NativeEngineSchedulerExitFlags.Returned|NativeEngineSchedulerExitFlags.RawRaxValid});}).GetAwaiter().GetResult();
            Check(f.Dispatcher.Unknown);f.End(p);}
    }
    private static void Detach()
    {using var f=new F();f.Register();var p=f.Pump();Check(f.Binding.IsCapturedByCurrentOriginal);f.Binding.Dispose();Check(f.Binding.IsCapturedByCurrentOriginal);
        var source=f.Consume();var c=f.CallbackCall(source);f.End(c,0);f.End(p);
        Check(!f.Binding.IsCapturedByCurrentOriginal&&!f.Rows.Unknown&&f.Rows.Read().Records.Single().State==NativeEngineSchedulerRowState.Retired);}
    private static void CompletedMasks()
    {
        using var f = new F();
        f.Register();
        var outer = f.Pump();
        var source = f.Consume();
        var reads = f.ProviderReads;
        for (var i = 0; i <= NativeEngineSchedulerRowLedger.MaximumCalls; i++)
        {
            var empty = f.Pump();
            f.End(empty);
        }
        var pending = f.Rows.Read();
        Check(!pending.Unknown && pending.Outstanding == 1 && pending.Calls > NativeEngineSchedulerRowLedger.MaximumCalls
            && pending.Records.Single().State == NativeEngineSchedulerRowState.ConsumedPendingCallback && f.ProviderReads == reads);
        f.End(f.CallbackCall(source), 0);
        f.End(outer);
        var retired = f.Rows.Read();
        Check(!retired.Unknown && retired.Outstanding == 0 && retired.Records.Single().State == NativeEngineSchedulerRowState.Retired);
    }
    private static void Bounds()
    {
        using var f = new F();
        // The native frame ABI rejects depth 129 before the ledger sees it.
        const int maximumNativeDepth = 128;
        for (var i = 0; i < maximumNativeDepth; i++) f.Pump();
        Check(!f.Rows.Unknown && f.Rows.Read().Outstanding == maximumNativeDepth);
        f.Pump();
        Check(f.Rows.Unknown && f.Rows.Read().Outstanding >= maximumNativeDepth);
        while (f.Open.Count > 0) f.End(f.Open[^1]);
        Check(f.Rows.Unknown && f.Rows.Read().Pending);
    }
    private static void Information()
    {var bridge=new Bridge(0);var id=bridge.Identity;var info=new NativeEngineSchedulerInformation{Version=1,Size=144,Kind=0,Slot=id.Slot,
        Configured=1,MainThreadId=id.MainThreadId,Target=id.Target,Original=id.Original,Entry=id.Entry,RowEntry=id.RowEntry,Epoch=id.Epoch};
        Check(NativeEngineSchedulerContextBridge.MatchesInformation(info,id,false));info.Unknown=1;Check(!NativeEngineSchedulerContextBridge.MatchesInformation(info,id,false));}
    private static void CallbackReentry()
    {using var f=new F();f.Register();var p=f.Pump();var source=f.Consume();var c=f.CallbackCall(source);f.Register(702);f.End(c,0);f.End(p);
        var rows=f.Rows.Read();Check(!rows.Unknown&&rows.Rows==2&&rows.Records[0].State==NativeEngineSchedulerRowState.Retired
            &&rows.Records[1].State==NativeEngineSchedulerRowState.Registered&&rows.Pending&&!rows.FutureClosed);}
    private static void OwnerMismatch()
    {using var f=new F();f.Register();var executes=f.Nodes.Read().Nodes.Single().ExecuteCalls;var p=f.Pump();var source=f.Consume();
        var frame=f.Frame(NativeEngineSchedulerKind.Callback,source:source);frame.Arg0=IntPtr.Zero;var c=f.Begin(frame);f.End(c,1);f.Release();f.End(p);
        Check(!f.Rows.Unknown&&f.Rows.Read().Records.Single().State==NativeEngineSchedulerRowState.Retired&&f.Nodes.Read().Nodes.Single().ExecuteCalls==executes);}
    private static void EntryReuse()
    {using var f=new F();f.Register();var p=f.Pump();var source=f.Consume();var c=f.CallbackCall(source);f.End(c,0);f.End(p);
        var old=f.Rows.Read();f.Register();var next=f.Rows.Read();Check(!next.Unknown&&next.Rows==2
            &&next.Records[0].Entry==next.Records[1].Entry&&next.Records[0].RegistrationGeneration<next.Records[1].RegistrationGeneration
            &&old.Records.Single().State==NativeEngineSchedulerRowState.Retired&&next.Records[1].State==NativeEngineSchedulerRowState.Registered);}
    private static void Abnormal()
    {
        using(var f=new F()){f.Register();var p=f.Pump();var source=f.Consume();var c=f.CallbackCall(source);f.End(c,abnormal:true);f.End(p);
            Check(f.Rows.Unknown&&f.Rows.Read().Records.Single().State!=NativeEngineSchedulerRowState.Retired);}
        using(var f=new F()){f.Register();var p=f.Pump();var source=f.Consume();var c=f.CallbackCall(source);f.End(c,1);f.AbnormalCleanup();f.End(p);
            Check(f.Rows.Unknown&&f.Rows.Read().Records.Single().State!=NativeEngineSchedulerRowState.Retired);}
    }
    private static void CleanupBoundaries()
    {
        using var f=new F();f.Register();var p=f.Pump();var source=f.Consume();var c=f.CallbackCall(source);f.End(c,1);f.NestedCleanup();f.End(p);
        Check(!f.Rows.Unknown&&f.Rows.Read().Records.Single() is {State:NativeEngineSchedulerRowState.Retired,CleanupCompleted:true});
        // The dispatcher exposes original-phase proof only outside its own
        // Enter/Row/Complete callbacks, preventing observer-created releases
        // from impersonating native scheduler cleanup children.
        var phases=new List<bool>();using var binding=f.Dispatcher.Bind(receipt=>{phases.Add(f.Dispatcher.CurrentOriginalScope is null);return new Scope();},
            (_,_)=>phases.Add(f.Dispatcher.CurrentOriginalScope is null),(_,_)=>phases.Add(f.Dispatcher.CurrentOriginalScope is null),(_,_)=>{});
        var call=f.Pump();Check(f.Dispatcher.CurrentOriginalScope is not null);f.End(call);Check(phases.Count==2&&phases.All(value=>value));
    }
    private sealed class Scope:IDisposable{public void Dispose(){}}
    private static void PerGenerationReadiness()
    {
        using var f=new F();var selected=f.Identities.Read().Identities.Single();
        f.Register();Check(!f.Rows.MayRetireNodeGeneration(selected)&&!f.Rows.Unknown);
        var p=f.Pump();var source=f.Consume();var c=f.CallbackCall(source);f.End(c,1);
        Check(!f.Rows.MayRetireNodeGeneration(selected)&&!f.Rows.Unknown);
        f.Release();Check(!f.Rows.MayRetireNodeGeneration(selected)&&!f.Rows.Unknown);
        f.End(p);Check(f.Rows.Read().Records.Single().State==NativeEngineSchedulerRowState.Retired
            &&f.Rows.MayRetireNodeGeneration(selected)&&!f.Rows.Unknown
            &&!f.Rows.Read().EngineCancelled&&!f.Rows.Read().FutureClosed);
    }
}
