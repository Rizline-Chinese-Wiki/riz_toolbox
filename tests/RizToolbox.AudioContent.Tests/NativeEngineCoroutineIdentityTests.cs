using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;

// Actual identity/node ledgers and actual three tracker implementations. Native
// owner pairs, engine frames and backend objects remain controlled test inputs;
// these cases neither execute Unity nor prove production owner verification.
internal static class NativeEngineCoroutineIdentityTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Engine identity binds actual initialization tracker before native first Execute and upgrades returned handle through scalar providers", Initialization),
        ("Engine identity binds actual terminal tracker slot generation independently from engine node generation", Terminal),
        ("Engine identity binds actual cutout tracker during synchronous Start without extra backend reads or roots", Cutout),
        ("Engine identity keeps managed receiver native owner and independently reused tracker engine generations separate", Generations),
        ("Engine identity ignores global foreign coroutines and nearest foreign Start masks outer node association", Foreign),
        ("Engine identity rejects unknown owned Enumerators and duplicate provider matches with retained physical responsibility", UnknownAndDuplicates),
        ("Engine identity registers responsibility before provider faults and freezes input tables and previous snapshots", ProviderFaults),
        ("Engine identity permits only monotonic same-record handle upgrade and rejects retired changed or reused tracker identities", HandleUpgrade),
        ("Engine identity abnormal wrong-frame parent and duplicate completions cannot erase exact context duties", Completion),
        ("Engine identity workers only inspect immutable scalars and provider reentry preserves Unknown responsibility", WorkersAndReentry),
        ("Engine identity bounded records preserve overflow and unknown state never resumes provider reads", Bounds),
        ("Engine identity bounds active foreign masks and overflow cannot borrow retained outer scopes after unwind", PhysicalBounds),
        ("Engine identity registration rejects aliased unpaired duplicate owners and invalid receiver mappings", Registration)
    };
    private static void Check(bool condition, string message = "Engine tracker identity check failed.")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Provider
    {
        internal readonly object Owner = new(); internal readonly long Receiver;
        internal readonly List<NativeCoroutineCancellationIdentity> Rows = new();
        internal int Reads, CurrentReads; internal bool Fail; internal Action? DuringRead;
        internal Provider(long receiver = 101) => Receiver = receiver;
        internal NativeCoroutineCancellationIdentity Add(long generation = 9, long enumerator = 1001, int slot = 0)
        { var row = new NativeCoroutineCancellationIdentity(Owner, Receiver, slot, generation, enumerator, 0, false, false, true, false, false, true); Rows.Add(row); return row; }
        internal NativeCoroutineCancellationIdentityProvider Description => new(Owner, Receiver, () => {
            ++Reads; DuringRead?.Invoke(); if (Fail) throw new IOException("Controlled scalar read failure."); return Rows.ToArray();
        }, identity => { ++CurrentReads; return Rows.Any(row => row.SameRecord(identity)); });
    }
    private sealed class Harness : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Provider Provider = new();
        internal readonly NativeEngineCoroutineNodeLedger Nodes;
        internal readonly NativeEngineCoroutineIdentityLedger Ledger;
        internal const long NativeOwner = 0x12340000;
        private ulong _next;
        internal Harness(bool add = true)
        { if (add) Provider.Add(); Nodes = new(Main); Ledger = new(Main, Nodes, new[] { Provider.Description }, new[] { new NativeEngineCoroutineOwnerPair(101, NativeOwner) }); }
        internal NativeEngineNodeFrame Start(long owner = NativeOwner, long enumerator = 1001) => new() {
            Version=1,Size=112,Kind=NativeEngineNodeKind.Start,Arg0=new(owner),Arg1=new(enumerator),Arg3=new(0x9000),Caller=new(0x8000),Sequence=++_next };
        internal NativeEngineNodeFrame Node(NativeEngineNodeKind kind = NativeEngineNodeKind.Execute, long node = 301,
            long owner = NativeOwner, long enumerator = 1001, ulong handle = 401) => new() {
                Version=1,Size=112,Kind=kind,SnapshotFlags=NativeEngineNodeSnapshotFlags.Requested|NativeEngineNodeSnapshotFlags.Valid,
                Arg0=new(node),Node=new(node),Owner=new(owner),EnumeratorCache=new(enumerator),EnumeratorHandle=handle,EnumeratorType=2,
                ReferenceCount=17,Caller=new(0x8000),Sequence=++_next };
        internal NativeEngineCoroutineIdentityLedger.Call Begin(long node = 301)
        { var start=Ledger.Capture(Start());var execute=Ledger.Capture(Node(node:node));Return(execute);Return(start,output:node);return start; }
        internal void Return(NativeEngineCoroutineIdentityLedger.Call call, uint? raw = null, long output = 0)
        { Ledger.Complete(call,call.Frame,Done(call.Frame.Kind,raw,output));call.Dispose(); }
        public void Dispose() => Main.Dispose();
    }
    private static NativeEngineNodeCompletion Done(NativeEngineNodeKind kind,uint? raw = null,long output = 0) => new() {
        Version=1,Size=32,Flags=NativeEngineNodeExitFlags.Returned|(kind==NativeEngineNodeKind.Detach?0:NativeEngineNodeExitFlags.ReturnByteValid),
        RawReturn=kind==NativeEngineNodeKind.Detach?0u:raw??(kind==NativeEngineNodeKind.Execute?0x12345678u:1u),StartOutputNode=new(output),
        OutputFlags=kind==NativeEngineNodeKind.Start?NativeEngineNodeOutputFlags.Requested|NativeEngineNodeOutputFlags.Read:0 };
    private static void RealStart(NativeEngineCoroutineIdentityLedger ledger,long owner,long enumerator,ref ulong sequence,long node = 301)
    {
        var start = new NativeEngineNodeFrame {Version=1,Size=112,Kind=NativeEngineNodeKind.Start,Arg0=new(owner),Arg1=new(enumerator),Arg3=new(0x9000),Caller=new(0x8000),Sequence=++sequence};
        var a=ledger.Capture(start);
        var execute = new NativeEngineNodeFrame {Version=1,Size=112,Kind=NativeEngineNodeKind.Execute,Arg0=new(node),Node=new(node),Owner=new(owner),
            EnumeratorCache=new(enumerator),EnumeratorHandle=401,EnumeratorType=2,ReferenceCount=17,Caller=new(0x8000),Sequence=++sequence,
            SnapshotFlags=NativeEngineNodeSnapshotFlags.Requested|NativeEngineNodeSnapshotFlags.Valid};
        var b=ledger.Capture(execute);Check(ledger.Read().Identities.Single() is {Node:301,EngineGeneration:1,Tracker.StartReturned:false});
        ledger.Complete(b,execute,Done(execute.Kind));b.Dispose();ledger.Complete(a,start,Done(start.Kind,output:node));a.Dispose();
    }
    private static void Initialization()
    {
        using var f=new NativeLocalInitializationCoroutineRegression.Fixture();var nodes=new NativeEngineCoroutineNodeLedger(f.Main);
        var ledger=new NativeEngineCoroutineIdentityLedger(f.Main,nodes,new[] {new NativeCoroutineCancellationIdentityProvider(f.Owner,f.Owner.ControllerIdentity,
            f.Owner.ReadCancellationIdentities,f.Owner.IsCurrentCancellationIdentity)},new[] {new NativeEngineCoroutineOwnerPair(101,0x12340000)});
        ulong sequence=0;f.Backend.Start(InitializationCoroutineRole.Delay,()=> {
            var reads=f.Backend.Reads;var roots=f.Backend.RootRecords;var activity=f.Backend.ActivityReads;
            RealStart(ledger,0x12340000,1001,ref sequence);Check(f.Backend.Reads==reads&&f.Backend.RootRecords==roots&&f.Backend.ActivityReads==activity);
        });
        var before=ledger.Read();Check(before.Identities.Single().Tracker.ObservedHandle==0&&before.Identities.Single().NativeStartReturned);
        var r=f.Backend.Reads;var a=f.Backend.ActivityReads;ledger.RefreshTrackedIdentities();var after=ledger.Read();
        Check(!after.Unknown&&after.Pending&&!after.EngineCancelled&&after.Outstanding==0&&after.Identities.Single() is {HandleUpgraded:true,Tracker.Generation:0,Tracker.ObservedHandle:5001,Tracker.HandleConfirmed:true});
        Check(before.Identities.Single().Tracker.ObservedHandle==0&&f.Backend.Reads==r&&f.Backend.ActivityReads==a&&f.Backend.FreeCalls==0);
    }
    private static void Terminal()
    {
        using var f=new NativeLocalTerminalCoroutineRegression.Fixture();var nodes=new NativeEngineCoroutineNodeLedger(f.Main);
        var ledger=new NativeEngineCoroutineIdentityLedger(f.Main,nodes,new[] {new NativeCoroutineCancellationIdentityProvider(f.Owner,101,
            f.Owner.ReadCancellationIdentities,f.Owner.IsCurrentCancellationIdentity)},new[] {new NativeEngineCoroutineOwnerPair(101,0x12340000)});
        var value=f.Backend.Factory(TerminalCoroutineRole.Fade,7001);ulong sequence=0;
        f.Backend.EnumStart(TerminalCoroutineRole.Fade,value,()=>RealStart(ledger,0x12340000,value,ref sequence));
        var reads=f.Backend.Reads;var roots=f.Backend.Roots;ledger.RefreshTrackedIdentities();var row=ledger.Read().Identities.Single();
        Check(!ledger.Unknown&&row.Tracker.Generation>0&&row.Tracker.ObservedHandle==5002&&row.Tracker.HandleConfirmed&&row.EngineGeneration==1
            &&row.NativeOwner!=row.Tracker.Receiver&&ReferenceEquals(row.Tracker.Owner,f.Owner)&&f.Backend.Reads==reads&&f.Backend.Roots==roots&&f.Backend.FreeCalls==0);
    }
    private static void Cutout()
    {
        using var main=new MainThreadDispatcher();var authority=new CutoutAuthority();var backend=new CutoutBackend(authority);
        var tracker=NativeLocalCutoutCoroutineTracker.CaptureBackend(main,backend,authority,CutoutBackend.Module);authority.Before=false;
        var nodes=new NativeEngineCoroutineNodeLedger(main);var ledger=new NativeEngineCoroutineIdentityLedger(main,nodes,
            new[] {new NativeCoroutineCancellationIdentityProvider(tracker,201,tracker.ReadCancellationIdentities,tracker.IsCurrentCancellationIdentity)},
            new[] {new NativeEngineCoroutineOwnerPair(201,0x12340000)});ulong sequence=0;
        backend.Start(()=>RealStart(ledger,0x12340000,8001,ref sequence));
        var reads=backend.Reads;var activity=backend.Activity;var roots=backend.RootRecords;ledger.RefreshTrackedIdentities();var row=ledger.Read().Identities.Single();
        Check(!ledger.Unknown&&row.Tracker is {Receiver:201,Enumerator:8001,ObservedHandle:9001,HandleConfirmed:true}
            &&ReferenceEquals(row.Tracker.Owner,tracker)&&row.NativeOwner==0x12340000&&roots==2&&backend.Reads==reads&&backend.Activity==activity&&backend.Frees==0);
    }
    private static void Generations()
    {
        using var f=new Harness();f.Begin();f.Provider.Rows[0]=f.Provider.Rows[0] with {StartReturned=true,ObservedHandle=5001,HandleConfirmed=true};f.Ledger.RefreshTrackedIdentities();
        var old=f.Ledger.Read().Identities.Single();var release=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Release));f.Return(release,raw:0);
        f.Provider.Rows[0]=f.Provider.Rows[0] with {Generation=10,StartReturned=false,ObservedHandle=0,HandleConfirmed=false};f.Begin();
        var rows=f.Ledger.Read().Identities;Check(!f.Ledger.Unknown&&rows.Count==2&&rows[0].Node==rows[1].Node&&rows[0].EngineGeneration==1
            &&rows[1].EngineGeneration==2&&rows[0].Tracker.Generation==9&&rows[1].Tracker.Generation==10&&old.Tracker.Generation==9);
        f.Ledger.RefreshTrackedIdentities();Check(!f.Ledger.Unknown&&f.Ledger.Read().Pending&&!f.Ledger.Read().EngineCancelled);
    }
    private static void Foreign()
    {
        using(var f=new Harness()) {
            for(var i=0;i<10;i++) {
                var s=f.Ledger.Capture(f.Start(999,999));var e=f.Ledger.Capture(f.Node(owner:999,enumerator:999));f.Return(e);f.Return(s,output:301);
            }
            Check(f.Provider.Reads==0&&f.Ledger.Read() is {Calls:0,Outstanding:0,Unknown:false,Pending:false,Nodes.Calls:0});
            var outer=f.Ledger.Capture(f.Start());var foreign=f.Ledger.Capture(f.Start(999,999));var reads=f.Provider.Reads;
            var masked=f.Ledger.Capture(f.Node());
            Check(masked.Relevant&&masked.NodeCall is null&&masked.Identity is null
                &&f.Ledger.Read() is {Unknown:true,Pending:true,Calls:2,Outstanding:2,Nodes.Calls:1});
            f.Return(masked);f.Return(foreign);f.Return(outer);
            Check(f.Ledger.Unknown&&f.Provider.Reads==reads&&f.Ledger.Read() is {Outstanding:0,Nodes.Nodes.Count:0}
                &&f.Ledger.Read().Identities.Single().Node==0);
        }
        using(var f=new Harness()) {
            var foreign=f.Ledger.Capture(f.Start(999,999));var masked=f.Ledger.Capture(f.Node());
            Check(masked.Relevant&&masked.NodeCall is null&&masked.Identity is null&&f.Provider.Reads==0
                &&f.Ledger.Read() is {Unknown:true,Pending:true,Calls:1,Outstanding:1,Nodes.Calls:0,Identities.Count:0});
            f.Return(masked);f.Return(foreign);
            Check(f.Ledger.Read() is {Unknown:true,Pending:true,Outstanding:0,Nodes.Calls:0}&&f.Provider.Reads==0);
        }
        using(var f=new Harness()) {
            f.Begin();var release=f.Ledger.Capture(f.Node(NativeEngineNodeKind.Release));f.Return(release,raw:0);
            var prior=f.Ledger.Read();var reads=f.Provider.Reads;
            // A freed and fully drained historical address can belong to an
            // unrelated engine coroutine later. It is not our live identity.
            var frame=f.Node(owner:999,enumerator:999);Check(!f.Ledger.IsRelevantFrame(frame));var c=f.Ledger.Capture(frame);f.Return(c);
            Check(!f.Ledger.Unknown&&f.Ledger.Read().Calls==prior.Calls&&f.Provider.Reads==reads);
        }
    }
    private static void UnknownAndDuplicates()
    {
        using(var f=new Harness()) {var c=f.Ledger.Capture(f.Start(enumerator:1002));Check(f.Ledger.Unknown&&f.Ledger.Read().Outstanding==1);f.Return(c);Check(f.Ledger.Read().Outstanding==0);}
        using(var f=new Harness()) {var c=f.Ledger.Capture(f.Node());Check(f.Ledger.Unknown&&f.Provider.Reads==0);f.Return(c);Check(f.Ledger.Read().Outstanding==0);}
        using(var f=new Harness()) {f.Begin();var c=f.Ledger.Capture(f.Start());Check(f.Ledger.Unknown&&f.Ledger.Read().Identities.Count==1);f.Return(c);}
        using(var f=new Harness()) {f.Provider.Rows.Add(f.Provider.Rows[0]);var c=f.Ledger.Capture(f.Start());Check(f.Ledger.Unknown);f.Return(c);}
        using(var main=new MainThreadDispatcher()) {
            var a=new Provider();a.Add();var b=new Provider();b.Add();var nodes=new NativeEngineCoroutineNodeLedger(main);
            var ledger=new NativeEngineCoroutineIdentityLedger(main,nodes,new[] {a.Description,b.Description},new[] {new NativeEngineCoroutineOwnerPair(101,0x12340000)});
            var frame=new NativeEngineNodeFrame {Version=1,Size=112,Kind=NativeEngineNodeKind.Start,Arg0=new(0x12340000),Arg1=new(1001),Arg3=new(9000),Caller=new(8000),Sequence=1};
            var c=ledger.Capture(frame);Check(ledger.Unknown&&ledger.Read().Identities.Count==0);ledger.Complete(c,frame,Done(frame.Kind));c.Dispose();
        }
    }
    private static void ProviderFaults()
    {
        using(var f=new Harness()) {
            f.Provider.DuringRead=()=>Check(f.Ledger.Read().Outstanding==1);f.Provider.Fail=true;
            var c=f.Ledger.Capture(f.Start());Check(f.Ledger.Unknown&&f.Ledger.Read().Outstanding==1);f.Return(c);var reads=f.Provider.Reads;
            f.Provider.Fail=false;c=f.Ledger.Capture(f.Start());f.Return(c);f.Ledger.RefreshTrackedIdentities();Check(f.Provider.Reads==reads&&f.Ledger.Read().Outstanding==0);
        }
        using(var main=new MainThreadDispatcher()) {
            var original=new Provider();original.Add();var replacement=new Provider();replacement.Add(enumerator:9999);
            var providers=new[] {original.Description};var pairs=new[] {new NativeEngineCoroutineOwnerPair(101,0x12340000)};
            var ledger=new NativeEngineCoroutineIdentityLedger(main,new(main),providers,pairs);providers[0]=replacement.Description;pairs[0]=new(101,9999);
            var frame=new NativeEngineNodeFrame {Version=1,Size=112,Kind=NativeEngineNodeKind.Start,Arg0=new(0x12340000),Arg1=new(1001),Arg3=new(9000),Caller=new(8000),Sequence=1};
            var c=ledger.Capture(frame);Check(!ledger.Unknown&&original.Reads==1&&replacement.Reads==0);
            ledger.Complete(c,frame,Done(frame.Kind));c.Dispose();var old=ledger.Read();
            original.Rows[0]=original.Rows[0] with {StartReturned=true,ObservedHandle=7001,HandleConfirmed=true};ledger.RefreshTrackedIdentities();
            Check(!ledger.Unknown&&old.Identities.Single().Tracker.ObservedHandle==0&&ledger.Read().Identities.Single().Tracker.ObservedHandle==7001);
        }
    }
    private static void HandleUpgrade()
    {
        foreach(var mutation in new[] {"generation","enum","handle","retired","returned"}) {
            using var f=new Harness();f.Begin();f.Provider.Rows[0]=f.Provider.Rows[0] with {StartReturned=true,ObservedHandle=5001,HandleConfirmed=true};
            f.Ledger.RefreshTrackedIdentities();Check(!f.Ledger.Unknown);var row=f.Provider.Rows[0];
            f.Provider.Rows[0]=mutation switch {
                "generation"=>row with {Generation=10},"enum"=>row with {Enumerator=1002},"handle"=>row with {ObservedHandle=5002},
                "retired"=>row with {Retiring=true},_=>row with {StartReturned=false}};
            f.Ledger.RefreshTrackedIdentities();Check(f.Ledger.Unknown&&f.Ledger.Read().Identities.Single().Tracker.ObservedHandle==5001);
        }
    }
    private static void Completion()
    {
        using var f=new Harness();var s=f.Ledger.Capture(f.Start());var e=f.Ledger.Capture(f.Node());
        f.Ledger.Complete(s,s.Frame,Done(s.Frame.Kind));Check(f.Ledger.Unknown&&f.Ledger.Read().Outstanding==2);
        var wrong=e.Frame;wrong.Caller+=1;f.Ledger.Complete(e,wrong,Done(e.Frame.Kind));Check(f.Ledger.Read().Outstanding==2);
        var abnormal=new NativeEngineNodeCompletion {Version=1,Size=32,Flags=NativeEngineNodeExitFlags.Abnormal};
        f.Ledger.Complete(e,e.Frame,abnormal);e.Dispose();f.Return(s);Check(f.Ledger.Read().Outstanding==0&&f.Ledger.Read().Nodes.Nodes.Single().ActiveExecutions==0);
        e.Dispose();Check(f.Ledger.Unknown&&f.Ledger.Read().Outstanding==0);
    }
    private static void WorkersAndReentry()
    {
        using(var f=new Harness()) {
            var owned=f.Start();var foreign=f.Start(999,999);Task.Run(()=> {
                Check(f.Ledger.IsRelevantFrame(owned)&&!f.Ledger.IsRelevantFrame(foreign));f.Ledger.ObserveFault(foreign,"Foreign fault");
                Throws<InvalidOperationException>(()=>f.Ledger.Capture(owned));f.Ledger.ObserveFault(owned,"Owned worker fault");
            }).GetAwaiter().GetResult();Check(f.Ledger.Unknown&&f.Provider.Reads==0&&f.Ledger.Read().Calls==0);
        }
        using(var f=new Harness()) {
            var entered=false;f.Provider.DuringRead=()=> {if(entered)return;entered=true;var nested=f.Ledger.Capture(f.Start());f.Return(nested);};
            var c=f.Ledger.Capture(f.Start());f.Return(c);Check(f.Ledger.Unknown&&f.Ledger.Read().Outstanding==0&&f.Provider.Reads==1);
        }
    }
    private static void Bounds()
    {
        using var f=new Harness(add:false);for(var i=0;i<NativeEngineCoroutineIdentityLedger.MaximumCalls;i++) {var c=f.Ledger.Capture(f.Start());f.Return(c);}
        var saved=f.Ledger.Read();var last=f.Ledger.Capture(f.Start());f.Return(last);var full=f.Ledger.Read();
        Check(saved.Calls==NativeEngineCoroutineIdentityLedger.MaximumCalls&&saved.Outstanding==0&&full.Untracked==1&&full.Outstanding==1
            &&full.Unknown&&full.Pending&&!full.EngineCancelled&&f.Provider.Reads==1);
    }
    private static void PhysicalBounds()
    {
        using var f=new Harness();var calls=new List<NativeEngineCoroutineIdentityLedger.Call>();
        for(var index=0;index<NativeEngineCoroutineIdentityLedger.MaximumPhysicalDepth;++index)
            calls.Add(f.Ledger.Capture(f.Start(999,999)));
        Check(f.Provider.Reads==0&&!f.Ledger.Unknown&&f.Ledger.Read().Calls==0);
        Throws<InvalidOperationException>(()=>f.Ledger.Capture(f.Start()));
        Check(f.Ledger.Read() is {Unknown:true,Pending:true,Untracked:1,Outstanding:1,Calls:0}&&f.Provider.Reads==0);
        // Exact retained masks can unwind; the overflow is never mistaken for
        // one of them and its permanent obligation cannot be cleared by unwind.
        for(var index=calls.Count-1;index>=0;--index)f.Return(calls[index]);
        Check(f.Ledger.Read() is {Unknown:true,Outstanding:1,Nodes.Calls:0}&&f.Provider.Reads==0);
        Throws<InvalidOperationException>(()=>f.Ledger.Capture(f.Start()));
        Check(f.Ledger.Read().Outstanding==2&&f.Provider.Reads==0);
    }
    private static void Registration()
    {
        using var main=new MainThreadDispatcher();var p=new Provider();p.Add();var nodes=new NativeEngineCoroutineNodeLedger(main);
        foreach(var pair in new[] {new NativeEngineCoroutineOwnerPair(101,101),new NativeEngineCoroutineOwnerPair(0,1),new NativeEngineCoroutineOwnerPair(101,0),new NativeEngineCoroutineOwnerPair(102,300)})
            Throws<ArgumentException>(()=>new NativeEngineCoroutineIdentityLedger(main,nodes,new[] {p.Description},new[] {pair}));
        Throws<ArgumentException>(()=>new NativeEngineCoroutineIdentityLedger(main,nodes,new[] {p.Description,p.Description},new[] {new NativeEngineCoroutineOwnerPair(101,300)}));
    }
    private sealed class CutoutAuthority : ICutoutCoroutineAuthority
    {
        internal bool Before=true;internal int Active;internal long Generation;
        public bool Unknown=>false;public long Host=>201;
        public NativeCutoutEventObservation Source {get;}=new(101,201,202,203,204,1,true,new[] {new NativeCutoutTrigger(301,401,BitConverter.SingleToInt32Bits(1f))});
        public void VerifyBeforeCapture(){Check(Before);}public void VerifyObservation(){Check(!Before);}
        public bool TryGetDispatch(out long generation){generation=Generation;return Active==1;}public bool TryConfirmSourceIdle()=>Active==0;public void ObserveFaultFromAnyThread(){}
    }
    private sealed class CutoutBackend : ICutoutCoroutineBackend
    {
        internal const long Module=0x20000000;private readonly CutoutAuthority _authority;private NativeLocalCutoutCoroutineTracker _owner=null!;
        internal int Reads,Activity,Frees;private int _roots;public int RootRecords=>_roots;public bool CleanupUnknown=>false;
        internal CutoutBackend(CutoutAuthority authority)=>_authority=authority;
        public void Initialize(NativeLocalCutoutCoroutineTracker owner)=>_owner=owner;
        public void RetainEnumerator(int slot,CutoutCoroutineRole role,long enumerator)=>++_roots;
        public CutoutCoroutineObservation ReadEnumerator(int slot,CutoutCoroutineRole role){++Reads;return new(8001,0,301,401,BitConverter.SingleToInt32Bits(1f),0,0,0);}
        public void RetainCoroutine(int slot,long coroutine)=>++_roots;
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity(){++Activity;return new(_authority.Active,true);}
        public void ReleaseEndedRoots(int slot,long enumerator,long generation){++Frees;throw new InvalidOperationException("Identity observer cannot release tracker roots.");}
        internal void Start(Action original)
        {
            ++_authority.Generation;++_authority.Active;var frame=new NativeCoroutineFrame {Version=1,Size=48,Kind=NativeCoroutineKind.Start,
                Self=new(201),Enumerator=new(8001),Method=new(1),Caller=new(Module+0x38a6b2)};
            var c=_owner.CaptureStart(frame)??throw new InvalidOperationException("Actual cutout tracker did not publish.");
            try{original();_owner.CompleteStart(c,frame,9001);}finally{c.Dispose();--_authority.Active;}
        }
    }
}
