using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

// Existing typed owner controlled harness reused to exercise the new actual handlers.
internal static class SingleOwnerIntegrationChecks
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("actual WorkOwner connects a rooted mid and child CALL into the original typed effects snapshot", Linked),
        ("actual WorkOwner empty mid is preserved after parent return without an effects completion ticket", Empty),
        ("actual WorkOwner missing-post completion preserves child abnormal and parent failure", Abnormal),
        ("actual WorkOwner refuses a copied context and an owned ancestor hidden beneath a nested parent", ParentMasks)
    };
    private static void Equal<T>(T expected,T actual){Check(EqualityComparer<T>.Default.Equals(expected,actual));}
    private static NativeTweenSingleEventFrame Frame(NativeOperationFrame p,NativeTweenSingleEventStage stage,uint site=3)
        => new(){Version=1,Size=200,Stage=stage,Site=site,ThreadId=19,Epoch=5,ParentEpoch=7,ParentManagedToken=91,
            ParentSequence=p.Sequence,Event=p.Self,List=new(3001),ParentMethod=p.Method,ParentCaller=p.Caller,ValueBits=p.ScalarBits,
            Invokable=stage==NativeTweenSingleEventStage.Mid?IntPtr.Zero:new(4001),ActualTarget=new(5001),
            ChildSequence=stage==NativeTweenSingleEventStage.Mid?0UL:201UL,ArgRcx=new(4001),SourceRsp=0x10000};
    private static NativeSingleExecutingList List(Fixture f,params long[] items)=>new(f.Backend.Frame.Self.ToInt64(),3001,3011,items.Length,1,items);
    private static void Linked()
    {
        using var f=new Fixture();var play=f.Begin(NativeTweenOperation.Play,f.Tween.Target);var context=f.Backend.Begin(0x7fc12345)!;
        var mid=Frame(f.Backend.Frame,NativeTweenSingleEventStage.Mid);var list=List(f,4001);f.Work.CaptureTypedExecutingList(context,mid,list);
        var before=Frame(f.Backend.Frame,NativeTweenSingleEventStage.Before,0);
        var target=new NativeSingleDispatchTarget(0,4001,5001,5002,before.ValueBits,0x80000000,true,0,Array.Empty<long>(),"controlled cached Single");
        f.Work.CaptureTypedDispatch(context,before,list,target);var after=before;after.Stage=NativeTweenSingleEventStage.Returned;f.Work.CompleteTypedDispatch(context,after);
        f.Backend.Complete(context,f.Backend.Frame,NativeOperationExitFlags.Returned);context.Dispose();f.End(play);
        var row=f.Work.ReadTypedEffects().Single();Check(row.Executing is{MidObserved:true,Count:1,ParentReturned:true,ParentDisposed:true}
            &&row.Executing.Dispatches.Single() is{ChildSequence:201,Returned:true,CachedSingle:true,EffectiveSingleBits:0x80000000}
            &&!f.Work.CanRetireWork()&&f.Roots.Freed==0);
    }
    private static void Empty()
    {
        using var f=new Fixture();var play=f.Begin(NativeTweenOperation.Play,f.Tween.Target);var context=f.Backend.Begin(1)!;
        f.Work.CaptureTypedExecutingList(context,Frame(f.Backend.Frame,NativeTweenSingleEventStage.Mid),List(f));
        f.Backend.Complete(context,f.Backend.Frame,NativeOperationExitFlags.Returned);context.Dispose();f.End(play);
        Check(f.Work.ReadTypedExecutingLists().Single() is{Count:0,MidObserved:true,ParentReturned:true,ParentDisposed:true}
            &&f.Work.ReadTypedEffects().Single().Executing!.Pending.Contains("writer")&&!f.Work.CanRetireWork()&&f.Roots.Freed==0);
    }
    private static void Abnormal()
    {
        using var f=new Fixture();var play=f.Begin(NativeTweenOperation.Play,f.Tween.Target);var context=f.Backend.Begin(1)!;
        var list=List(f,4001);f.Work.CaptureTypedExecutingList(context,Frame(f.Backend.Frame,NativeTweenSingleEventStage.Mid),list);
        var before=Frame(f.Backend.Frame,NativeTweenSingleEventStage.Before,1);
        f.Work.CaptureTypedDispatch(context,before,list,new(1,4001,5001,5002,before.ValueBits,null,false,0,Array.Empty<long>(),"zero arg"));
        var after=before;after.Stage=NativeTweenSingleEventStage.Abnormal;after.Flags=NativeTweenSingleEventFlags.MissingPost;
        f.Work.CompleteTypedDispatch(context,after);f.Backend.Complete(context,f.Backend.Frame,NativeOperationExitFlags.Abnormal);context.Dispose();f.End(play,NativeOperationExitFlags.Abnormal);
        Check(f.Work.Unknown&&f.Work.ReadTypedExecutingLists().Single().Dispatches.Single() is{Abnormal:true,Returned:false}&&f.Roots.Freed==0);
    }
    private static void ParentMasks()
    {
        using var f=new Fixture();var play=f.Begin(NativeTweenOperation.Play,f.Tween.Target);var context=f.Backend.Begin(1)!;
        var mid=Frame(f.Backend.Frame,NativeTweenSingleEventStage.Mid);using var copied=new MemoryStream();
        Throws<InvalidOperationException>(()=>f.Work.CaptureTypedExecutingList(copied,mid,List(f)));
        var nested=f.Begin(NativeTweenOperation.Play,f.Tween.Target);Throws<InvalidOperationException>(()=>f.Work.CaptureTypedExecutingList(context,mid,List(f)));f.End(nested);
        f.Work.CaptureTypedExecutingList(context,mid,List(f));f.Backend.Complete(context,f.Backend.Frame,NativeOperationExitFlags.Returned);context.Dispose();f.End(play);
        Check(f.Work.ReadTypedExecutingLists().Single().Count==0&&f.Roots.Freed==0);
    }
    private sealed class Fixture : IDisposable
    {
        internal const long Caller = 0x180395c07, Method = 0x777;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly RootTable Roots;
        internal readonly Backend Backend;
        internal readonly NativeLocalTweenWorkOwner Work;
        internal readonly NativeRuntime.RootHandle Tween, Event;
        private ulong _sequence;
        internal Fixture(params (long Tween, long Event)[] sources)
        {
            if (sources.Length == 0) sources = new[] { (1001L, 2001L) };
            Roots = new(Main); Backend = new(Main, Roots, sources);
            Tween = Roots.Root(new(1001)); Event = Roots.Root(new(2001));
            Work = NativeLocalTweenWorkOwner.CaptureBackend(Main, Backend); Backend.Owner = Work;
        }
        internal NativeLocalTweenWorkOwner.OperationContext Begin(NativeTweenOperation operation, IntPtr arg)
        {
            var frame = operation == NativeTweenOperation.Play
                ? new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Void0,
                    Self = arg, Method = new(700), Caller = new(900), Sequence = ++_sequence }
                : new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Reference1,
                    Self = new(1002), Arg0 = arg, Method = new(701), Caller = new(901), Sequence = ++_sequence };
            return Work.CaptureOperation(operation, frame)
                ?? throw new InvalidOperationException("The parent operation was not captured.");
        }
        internal void End(NativeLocalTweenWorkOwner.OperationContext context,
            NativeOperationExitFlags flags = NativeOperationExitFlags.Returned)
        { Work.CompleteOperation(context, context.Frame, flags); context.Dispose(); }
        public void Dispose() => Main.Dispose();
    }

    private sealed class Backend : INativeLocalTweenBackend, INativeTweenTypedEventSource, INativeTweenDynamicTypedEventSource
    {
        private readonly MainThreadDispatcher _main; private readonly RootTable _roots;
        private readonly (long Tween, long Event)[] _sources;
        private readonly Dictionary<long, (NativeRuntime.RootHandle Tween, NativeRuntime.RootHandle? Event)> _dynamic = new();
        internal Backend(MainThreadDispatcher main, RootTable roots, (long Tween, long Event)[] sources)
            => (_main, _roots, _sources) = (main, roots, sources);
        internal NativeLocalTweenWorkOwner Owner = null!;
        internal bool Bound;
        internal int MembershipCount, ProcessCapacity;
        internal int DynamicBorrowed;
        internal Func<NativeOperationFrame, IDisposable?> TypedCapture = null!;
        internal Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags> CompleteCallback = null!;
        internal Action<NativeOperationFrame, string> Fault = null!;
        internal NativeOperationFrame Frame = new();
        public bool CleanupUnknown => false; public int RootRecords => 2; public IntPtr PlayMethodInfo => new(700);
        public void Initialize(NativeLocalTweenWorkOwner owner)
        {
            Owner = owner;
            foreach (var source in _sources) owner.RegisterSource(source.Tween);
            owner.AdoptTypedEvents(this);
            Bound = true;
        }
        public NativeTweenWorkGraph Capture(NativeTweenOperation operation, NativeOperationFrame frame, bool after) => Refresh();
        public NativeTweenWorkGraph Refresh() => new(0, Array.Empty<NativeTweenDelegateObservation>(),
            Array.Empty<NativeTweenInfoObservation>(), Array.Empty<NativeTweenManagerObservation>());
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
        public void BorrowTypedTweenEvents(NativeLocalTweenWorkOwner owner,
            Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle?> adopt)
        {
            _main.VerifyAccess(); var roots = owner.RequireActualTypedEventBorrowing(this);
            Equal(_sources.Length, roots.Count);
            var field = new NativeExactReadField(new(0x98), new(301), new(302), new(303),
                "onTween", "UnityEngine.Events.UnityEvent<System.Single>", false, 0x98, 8, 0);
            foreach (var source in _sources)
                adopt(_roots.Root(new(source.Tween)), field, source.Event == 0 ? null : _roots.Root(new(source.Event)));
        }
        public void BindTypedTweenEvents(NativeLocalTweenWorkOwner owner, int processCapacity,
            Func<NativeOperationFrame, IDisposable?> capture,
            Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags> complete,
            Action<NativeOperationFrame, string> fault)
        {
            MembershipCount = owner.RequireTypedEventMemberships(this, processCapacity).Count;
            Equal(_sources.Count(value => value.Event != 0), MembershipCount); Equal(_sources.Length, processCapacity);
            ProcessCapacity = processCapacity;
            TypedCapture = capture; CompleteCallback = complete; Fault = fault;
        }
        internal NativeRuntime.RootHandle RegisterDynamic(long tween, long eventIdentity)
        {
            var root = _roots.Root(new(tween));
            _dynamic.Add(tween, (root, eventIdentity == 0 ? null : _roots.Root(new(eventIdentity)))); return root;
        }
        public void BorrowDynamicTypedTweenEvents(NativeLocalTweenWorkOwner owner, long tween,
            Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle?> adopt)
        {
            Check(ReferenceEquals(owner, Owner)); _main.VerifyAccess(); ++DynamicBorrowed;
            var field = new NativeExactReadField(new(0x98), new(301), new(302), new(303),
                "onTween", "UnityEngine.Events.UnityEvent<System.Single>", false, 0x98, 8, 0);
            var roots = _dynamic[tween]; adopt(roots.Tween, field, roots.Event);
        }
        internal IDisposable? Begin(uint bits, long eventIdentity = 2001)
        {
            Prepare(bits, eventIdentity);
            return TypedCapture(Frame);
        }
        internal void Prepare(uint bits, long eventIdentity = 2001)
        {
            Frame = new() { Version = 1, Size = 64, Kind = NativeOperationKind.Single1, Self = new(eventIdentity),
                Arg0 = new(unchecked((long)bits)), Method = new(Fixture.Method), Caller = new(Fixture.Caller), Sequence = 7 };
        }
        internal void Complete(IDisposable context, NativeOperationFrame frame, NativeOperationExitFlags flags)
            => CompleteCallback(context, frame, flags);
        internal void Invoke(uint bits, NativeOperationExitFlags flags) => InvokeEvent(2001, bits, flags);
        internal void InvokeEvent(long eventIdentity, uint bits, NativeOperationExitFlags flags)
        { var context = Begin(bits, eventIdentity) ?? throw new InvalidOperationException("No typed context.");
            try { CompleteCallback(context, Frame, flags); } finally { context.Dispose(); } }
    }
    private sealed class RootTable
    {
        private readonly MainThreadDispatcher _main; private readonly Dictionary<uint, IntPtr> _roots = new(); private uint _next;
        internal int Freed; internal RootTable(MainThreadDispatcher main) => _main = main;
        internal NativeRuntime.RootHandle Root(IntPtr value)
        { _main.VerifyAccess(); var key = ++_next; _roots.Add(key, value);
            return new(key, id => { _roots.Remove(id); ++Freed; }, id => _roots.GetValueOrDefault(id), _main.VerifyAccess); }
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Typed Tween event invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
