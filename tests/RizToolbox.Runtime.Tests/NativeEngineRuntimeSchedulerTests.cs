using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Controlled transports exercise the actual shared runtime coordinator and
// six-channel dispatcher. No DLL, native target or production credential runs.
internal static class NativeEngineRuntimeSchedulerTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Engine runtime scheduler shares six physical attachments and one actual dispatcher", SharedSixChannels),
        ("Engine runtime scheduler freezes the initial bridge members against caller list mutation", FrozenBridgeMembers),
        ("Engine runtime scheduler rejects late and replacement responsibility without extra Attach", RefuseLateAndReplacement),
        ("Engine runtime activity retains actual scheduler scopes and native transport activity", ActivityIncludesScheduler),
        ("Engine runtime scheduler transport Unknown reaches owner while captured scopes still finish", StickySchedulerUnknown),
        ("Engine runtime missing scheduler refuses that capability while four node channels remain usable", MissingScheduler),
        ("Engine runtime wait shares one exact thirteen-target family without production credentials", SharedWait),
        ("Engine runtime wait refuses late and replacement family attachments", WaitReplacement),
        ("Engine runtime wait refuses mismatched actual node targets before side Attach", WaitMismatch),
        ("Engine runtime wait includes native activity and retains captured completion after withdrawal", WaitActivity),
        ("Engine runtime missing wait preserves scheduler capability and refuses fixed wait image", MissingWait)
    };
    private static void Check(bool value, string reason = "Engine runtime scheduler regression failed.")
    { if (!value) throw new InvalidOperationException(reason); }
    private static void Refuse(Action action)
    {
        try { action(); }
        catch (NotSupportedException) { return; }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected unavailable scheduler capability refusal.");
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
    private sealed class Scope : IDisposable
    {
        internal int Completed, Disposed;
        public void Dispose() => ++Disposed;
    }
    private sealed class NodeBridge : INativeEngineNodeContextBridge
    {
        private Func<NativeEngineNodeFrame, nuint>? _enter;
        private Action<NativeEngineNodeFrame, nuint, NativeEngineNodeCompletion>? _exit;
        private ulong _sequence;
        private long _inFlight;
        internal int Attaches;
        public NativeEngineNodeChannelIdentity Identity { get; }
        internal NodeBridge(NativeEngineNodeKind kind) => Identity = new(kind, (uint)kind + 4,
            new(0x1000 + (int)kind * 0x100), new(0x2000 + (int)kind * 0x100), new(0x3000 + (int)kind * 0x100), 7);
        public void Attach(Func<NativeEngineNodeFrame, nuint> enter, Action<NativeEngineNodeFrame, nuint, NativeEngineNodeCompletion> exit)
        { Check(++Attaches == 1); (_enter, _exit) = (enter, exit); }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(_inFlight, true);
        internal void Call()
        {
            var kind = Identity.Kind;
            var frame = new NativeEngineNodeFrame { Version = 1, Size = 112, Kind = kind,
                Arg0 = new(301), Caller = new(0x8000), Sequence = ++_sequence };
            if (kind == NativeEngineNodeKind.Start) { frame.Arg1 = new(1001); frame.Arg3 = new(0x9000); }
            else
            {
                frame.Node = frame.Arg0; frame.Owner = new(101); frame.EnumeratorHandle = 401;
                frame.EnumeratorCache = new(1001); frame.EnumeratorType = 2; frame.ReferenceCount = 1;
                frame.SnapshotFlags = NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Valid;
            }
            ++_inFlight; var token = _enter!(frame);
            _exit!(frame, token, new() { Version = 1, Size = 32,
                Flags = NativeEngineNodeExitFlags.Returned | (kind == NativeEngineNodeKind.Detach ? 0 : NativeEngineNodeExitFlags.ReturnByteValid),
                RawReturn = kind == NativeEngineNodeKind.Detach ? 0u : 0x80u,
                OutputFlags = kind == NativeEngineNodeKind.Start ? NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read : 0 });
            --_inFlight;
        }
    }
    private sealed class SchedulerBridge : INativeEngineSchedulerContextBridge
    {
        private Func<NativeEngineSchedulerFrame, nuint>? _enter;
        private Action<NativeEngineSchedulerFrame, nuint, NativeEngineSchedulerCompletion>? _exit;
        internal int Attaches;
        internal long NativeInFlight;
        internal bool Known = true;
        public NativeEngineSchedulerChannelIdentity Identity { get; }
        internal SchedulerBridge(NativeEngineSchedulerKind kind) => Identity = new(kind, 3, (ulong)kind + 10,
            NativeEngineInlinePlatform.CurrentOsThreadId, new(0x10000 + (int)kind * 0x100),
            new(0x20000 + (int)kind * 0x100), new(0x30000 + (int)kind * 0x100), new(0x40000),
            kind == NativeEngineSchedulerKind.Drain ? new() { SitePc = new(0x50000), RelayReturnPc = new(0x60000) } : default,
            kind == NativeEngineSchedulerKind.Drain ? new() { SitePc = new(0x50010), RelayReturnPc = new(0x60010) } : default);
        public void Attach(Func<NativeEngineSchedulerFrame, nuint> enter,
            Action<NativeEngineSchedulerFrame, nuint, NativeEngineSchedulerCompletion> exit, Action<NativeEngineSchedulerRowFrame>? row)
        {
            Check(++Attaches == 1);
            Check((row is not null) == (Identity.Kind == NativeEngineSchedulerKind.Drain));
            (_enter, _exit) = (enter, exit);
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(NativeInFlight, Known);
        internal nuint Begin(NativeEngineSchedulerFrame frame) => _enter!(frame);
        internal void End(NativeEngineSchedulerFrame frame, nuint token)
            => _exit!(frame, token, new() { Version = 1, Size = 48,
                Flags = NativeEngineSchedulerExitFlags.Returned | NativeEngineSchedulerExitFlags.RawRaxValid,
                RawRax = frame.Kind == NativeEngineSchedulerKind.Register ? (ulong)frame.OutputSlot.ToInt64() : 0xabcdef0100000001,
                OutputFlags = frame.Kind == NativeEngineSchedulerKind.Register ? 3u : 0u,
                OutputEntry = frame.Kind == NativeEngineSchedulerKind.Register ? new(701) : IntPtr.Zero });
    }
    private sealed record Physical(NativeEngineSchedulerFrame Frame, nuint Token);
    private sealed class Fixture
    {
        internal readonly object Key = new();
        internal readonly NodeBridge[] Nodes = Enum.GetValues<NativeEngineNodeKind>().Select(kind => new NodeBridge(kind)).ToArray();
        internal readonly SchedulerBridge[] Scheduler = Enum.GetValues<NativeEngineSchedulerKind>().Select(kind => new SchedulerBridge(kind)).ToArray();
        private readonly List<Physical> _open = new();
        private ulong _sequence;
        internal NativeEngineRuntimeCoordinator Share(IReadOnlyList<INativeEngineSchedulerContextBridge>? bridges)
            => NativeEngineRuntimeCoordinator.ShareControlled(Key, Nodes, new Logger(), schedulerBridges: bridges);
        internal Physical Begin(NativeEngineSchedulerKind kind)
        {
            var identity = Scheduler[(int)kind].Identity; var parent = _open.LastOrDefault(); var sequence = ++_sequence;
            var frame = new NativeEngineSchedulerFrame { Version = 1, Size = 256, Kind = kind,
                Slot = identity.Slot, ThreadId = identity.MainThreadId, Epoch = identity.Epoch, Sequence = sequence,
                ParentSequence = parent?.Frame.Sequence ?? 0, RootSequence = parent?.Frame.RootSequence ?? sequence,
                Depth = checked((uint)_open.Count + 1), Caller = new(0x70000),
                Flags = NativeEngineSchedulerFlags.MainThread | NativeEngineSchedulerFlags.ParentMatched };
            if (kind == NativeEngineSchedulerKind.Register)
            { frame.Arg0 = frame.Tree = new(0x8030); frame.Arg1 = frame.OutputSlot = new(0x9000); frame.Arg2 = frame.SourceRecord = new(0xa000); }
            else if (kind is NativeEngineSchedulerKind.Remove or NativeEngineSchedulerKind.Consume)
            { frame.Arg0 = frame.Manager = new(0x8000); frame.Tree = new(0x8030); frame.Arg2 = frame.Entry = new(701);
                frame.SourceRecord = new(733); if (kind == NativeEngineSchedulerKind.Remove) frame.Arg1 = frame.SourceRecord; }
            else if (kind == NativeEngineSchedulerKind.Callback)
            { frame.Arg0 = frame.NodeOwner = new(101); frame.Arg1 = frame.Record.Payload = new(301);
                frame.Record.Callback = identity.Target; frame.NodeSnapshotFlags = 3; frame.EnumeratorType = 2;
                frame.EnumeratorHandle = 401; frame.EnumeratorCache = new(1001); frame.ReferenceCount = 1; }
            else { frame.Arg0 = frame.Manager = new(0x8000); frame.Tree = new(0x8030); }
            if (kind is NativeEngineSchedulerKind.Register or NativeEngineSchedulerKind.Remove or NativeEngineSchedulerKind.Consume)
            { frame.Flags |= NativeEngineSchedulerFlags.SnapshotRequested | NativeEngineSchedulerFlags.SnapshotValid;
                frame.Record.Payload = new(0xb000); frame.Record.Callback = new(0xc000); frame.Record.Cleanup = new(0xd000); }
            var call = new Physical(frame, Scheduler[(int)kind].Begin(frame)); _open.Add(call); return call;
        }
        internal void End(Physical call) { Scheduler[(int)call.Frame.Kind].End(call.Frame, call.Token); _open.Remove(call); }
    }
    private sealed class WaitBridge : INativeEngineWaitContextBridge
    {
        private Func<NativeEngineWaitFrame,nuint>? _enter;
        private Action<NativeEngineWaitFrame,nuint,NativeEngineWaitCompletion>? _exit;
        internal int Attaches;
        internal long InFlight;
        internal bool Known=true;
        public NativeEngineWaitFamilyIdentity Identity { get; }
        internal WaitBridge(Fixture fixture,bool wrongNode=false)
        {
            var targets=new[]{new IntPtr(0x110000),new IntPtr(0x110100),new IntPtr(0x110200)}
                .Concat(fixture.Nodes.Select(bridge=>bridge.Identity.Target))
                .Concat(fixture.Scheduler.Select(bridge=>bridge.Identity.Target)).ToArray();
            if(wrongNode)targets[4]=new(0xdead);
            Identity=new(3,77,NativeEngineInlinePlatform.CurrentOsThreadId,targets,
                Enumerable.Range(0,3).Select(index=>new IntPtr(0x120000+index*0x100)).ToArray(),
                Enumerable.Range(0,3).Select(index=>new IntPtr(0x130000+index*0x100)).ToArray(),new(0x140000),
                Enumerable.Range(0,18).Select(index=>new NativeEngineWaitSite{SitePc=new(0x150000+index*0x10),RelayReturnPc=new(0x160000+index*0x10)}).ToArray());
        }
        public void Attach(Func<NativeEngineWaitFrame,nuint> enter,
            Action<NativeEngineWaitFrame,nuint,NativeEngineWaitCompletion> exit,Action<NativeEngineWaitPhaseFrame> phase)
        {Check(++Attaches==1);(_enter,_exit)=(enter,exit);}
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()=>new(InFlight,Known);
        internal (NativeEngineWaitFrame Frame,nuint Token) Begin()
        {
            var frame=new NativeEngineWaitFrame{Version=1,Size=240,Kind=NativeEngineWaitKind.Batch,Slot=Identity.Slot,
                ThreadId=Identity.MainThreadId,Depth=1,Epoch=Identity.Epoch,Sequence=1,RootSequence=1,
                Flags=NativeEngineWaitFlags.MainThread|NativeEngineWaitFlags.ParentMatched,Arg0=new(0x170000),Manager=new(0x170000),
                Caller=new(0x180000),Target=Identity.Target(NativeEngineWaitKind.Batch)};
            ++InFlight;return(frame,_enter!(frame));
        }
        internal void End((NativeEngineWaitFrame Frame,nuint Token) call)
        {
            _exit!(call.Frame,call.Token,new(){Version=1,Size=48,
                Flags=NativeEngineWaitExitFlags.Returned|NativeEngineWaitExitFlags.RawRaxValid,RawRax=0xfedcba9800000001});
            --InFlight;
        }
    }
    private static NativeEngineRuntimeCoordinator ShareWait(Fixture f,WaitBridge? wait)
        => NativeEngineRuntimeCoordinator.ShareControlled(f.Key,f.Nodes,new Logger(),schedulerBridges:f.Scheduler,waitBridge:wait);
    private static void SharedWait()
    {
        var f=new Fixture();var wait=new WaitBridge(f);var owner=ShareWait(f,wait);var dispatcher=owner.RequireWaitObservation();
        Check(ReferenceEquals(owner,ShareWait(f,wait))&&ReferenceEquals(dispatcher,owner.RequireWaitObservation())&&wait.Attaches==1);
        Refuse(()=>owner.RequirePublishedWaitImage());
        Check(!owner.Unknown&&owner.ReadActivity() is {Known:true,InFlight:0});
    }
    private static void WaitReplacement()
    {
        var late=new Fixture();var without=ShareWait(late,null);var lateWait=new WaitBridge(late);
        Refuse(()=>ShareWait(late,lateWait));Check(lateWait.Attaches==0&&!without.Unknown);
        var f=new Fixture();var wait=new WaitBridge(f);var owner=ShareWait(f,wait);var replacement=new WaitBridge(f);
        Refuse(()=>ShareWait(f,replacement));Check(wait.Attaches==1&&replacement.Attaches==0&&!owner.Unknown);
    }
    private static void WaitMismatch()
    {
        var f=new Fixture();var wait=new WaitBridge(f,true);Refuse(()=>ShareWait(f,wait));Refuse(()=>ShareWait(f,wait));
        Check(wait.Attaches==0&&f.Nodes.All(bridge=>bridge.Attaches==1)&&f.Scheduler.All(bridge=>bridge.Attaches==1));
    }
    private static void WaitActivity()
    {
        var f=new Fixture();var wait=new WaitBridge(f);var owner=ShareWait(f,wait);var calls=new List<Scope>();
        var binding=owner.RequireWaitObservation().Bind(_=>{var scope=new Scope();calls.Add(scope);return scope;},
            (scope,_)=>++scope.Completed,(_,_)=>{},(_,_)=>{});
        var actual=wait.Begin();Check(owner.ReadActivity() is {Known:true,InFlight:>0});binding.Dispose();
        wait.End(actual);Completed(calls);Check(calls.Count==1&&owner.ReadActivity() is {Known:true,InFlight:0});
        wait.InFlight=7;Check(owner.ReadActivity() is {Known:true,InFlight:>=7});wait.InFlight=0;wait.Known=false;
        Check(!owner.ReadActivity().Known&&owner.Unknown);wait.Known=true;Refuse(()=>owner.RequireWaitObservation());
    }
    private static void MissingWait()
    {
        var f=new Fixture();var owner=ShareWait(f,null);Refuse(()=>owner.RequireWaitObservation());Refuse(()=>owner.RequirePublishedWaitImage());
        Check(owner.RequireSchedulerObservation() is not null&&!owner.Unknown);
        var actual=f.Begin(NativeEngineSchedulerKind.Pump);f.End(actual);Check(owner.ReadActivity() is {Known:true,InFlight:0});
    }
    private static void Completed(List<Scope> scopes) => Check(scopes.All(scope => scope.Completed == 1 && scope.Disposed == 1));

    private static void SharedSixChannels()
    {
        var f = new Fixture(); var owner = f.Share(f.Scheduler); var dispatcher = owner.RequireSchedulerObservation();
        var publicationRefused = false;
        try { _ = owner.RequirePublishedSchedulerImage(); }
        catch (UnsupportedSignatureException) { publicationRefused = true; }
        Check(publicationRefused && !owner.Unknown);
        Check(ReferenceEquals(owner, f.Share(f.Scheduler)) && ReferenceEquals(dispatcher, owner.RequireSchedulerObservation()));
        Check(f.Nodes.All(bridge => bridge.Attaches == 1) && f.Scheduler.All(bridge => bridge.Attaches == 1));
        var scopes = new List<Scope>(); var faults = 0;
        using var binding = dispatcher.Bind(_ => { var scope = new Scope(); scopes.Add(scope); return scope; },
            (scope, _) => ++scope.Completed, (_, _) => { }, (_, _) => ++faults);
        foreach (var kind in Enum.GetValues<NativeEngineSchedulerKind>()) { var call = f.Begin(kind); f.End(call); }
        Check(scopes.Count == 6 && faults == 0 && owner.ReadActivity() is { Known: true, InFlight: 0 }); Completed(scopes);
        Check(f.Scheduler.All(bridge => bridge.Attaches == 1));
    }
    private static void FrozenBridgeMembers()
    {
        var f = new Fixture(); INativeEngineSchedulerContextBridge[] supplied = f.Scheduler.ToArray();
        var owner = f.Share(supplied); var original = owner.RequireSchedulerObservation();
        var replacement = new SchedulerBridge(NativeEngineSchedulerKind.Callback); supplied[5] = replacement;
        Check(ReferenceEquals(owner, f.Share(f.Scheduler)) && ReferenceEquals(original, owner.RequireSchedulerObservation()));
        Refuse(() => f.Share(supplied));
        Check(replacement.Attaches == 0 && f.Scheduler.All(bridge => bridge.Attaches == 1) && !owner.Unknown);
        var call = f.Begin(NativeEngineSchedulerKind.Callback); f.End(call);
        Check(owner.ReadActivity() is { Known: true, InFlight: 0 });
    }
    private static void RefuseLateAndReplacement()
    {
        var late = new Fixture(); var without = late.Share(null); Refuse(() => late.Share(late.Scheduler));
        Check(late.Scheduler.All(bridge => bridge.Attaches == 0) && !without.Unknown);
        var f = new Fixture(); var owner = f.Share(f.Scheduler);
        var replacement = Enum.GetValues<NativeEngineSchedulerKind>().Select(kind => new SchedulerBridge(kind)).ToArray();
        Refuse(() => f.Share(replacement));
        Check(replacement.All(bridge => bridge.Attaches == 0) && f.Scheduler.All(bridge => bridge.Attaches == 1));
        Check(ReferenceEquals(owner, f.Share(f.Scheduler)) && !owner.Unknown);
    }
    private static void ActivityIncludesScheduler()
    {
        var f = new Fixture(); var owner = f.Share(f.Scheduler); var dispatcher = owner.RequireSchedulerObservation();
        var pump = f.Begin(NativeEngineSchedulerKind.Pump);
        Check(dispatcher.CurrentOriginalScope is { } proof && proof.Frame.Sequence == pump.Frame.Sequence);
        Check(owner.ReadActivity() is { Known: true, InFlight: > 0 });
        f.Scheduler[1].NativeInFlight = 7; Check(owner.ReadActivity() is { Known: true, InFlight: >= 7 });
        f.Scheduler[1].NativeInFlight = 0; f.End(pump);
        Check(dispatcher.CurrentOriginalScope is null && owner.ReadActivity() is { Known: true, InFlight: 0 });
    }
    private static void StickySchedulerUnknown()
    {
        var f = new Fixture(); var owner = f.Share(f.Scheduler); var scopes = new List<Scope>();
        using var binding = owner.RequireSchedulerObservation().Bind(_ => { var scope = new Scope(); scopes.Add(scope); return scope; },
            (scope, _) => ++scope.Completed, (_, _) => { }, (_, _) => { });
        var pump = f.Begin(NativeEngineSchedulerKind.Pump); f.Scheduler[2].Known = false;
        Check(!owner.ReadActivity().Known && owner.Unknown); f.Scheduler[2].Known = true;
        f.End(pump); Completed(scopes); Check(scopes.Count == 1 && !owner.ReadActivity().Known);
        Refuse(() => owner.RequireSchedulerObservation()); Refuse(() => f.Share(f.Scheduler));
        Check(f.Nodes.All(bridge => bridge.Attaches == 1) && f.Scheduler.All(bridge => bridge.Attaches == 1));
    }
    private static void MissingScheduler()
    {
        var f = new Fixture(); var owner = f.Share(null); Refuse(() => owner.RequireSchedulerObservation());
        Check(!owner.Unknown && f.Scheduler.All(bridge => bridge.Attaches == 0));
        var scopes = new List<Scope>(); var faults = 0;
        using var binding = owner.Bind(_ => { var scope = new Scope(); scopes.Add(scope); return scope; },
            (scope, _, _) => ++scope.Completed, (_, _) => ++faults);
        foreach (var bridge in f.Nodes) bridge.Call();
        Check(scopes.Count == 4 && faults == 0 && owner.ReadActivity() is { Known: true, InFlight: 0 }); Completed(scopes);
    }
}
