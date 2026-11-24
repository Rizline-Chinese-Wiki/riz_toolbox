using System.Runtime.InteropServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

if (args.FirstOrDefault() == NativeSingleSubscriptionCallbackChecks.ModeArgument)
    return NativeSingleSubscriptionCallbackChecks.Run(args);

var tests = new List<(string Name, Action Run)>
{
    ("native wire keeps the independent 200B Single frame and 144B info layout", () => {
        Check(Marshal.SizeOf<NativeTweenSingleEventFrame>() == 200 && Marshal.SizeOf<NativeTweenSingleEventInformation>() == 144);
        Check(Marshal.OffsetOf<NativeTweenSingleEventFrame>("ParentSequence").ToInt32() == 48 && Marshal.OffsetOf<NativeTweenSingleEventFrame>("MachineFrame").ToInt32() == 192);
    }),
    ("native info distinguishes 128 retained parent records from the independent 32-level stack", () => {
        var id=Identity();var info=new NativeTweenSingleEventInformation{Version=1,Size=144,Configured=1,ParentCapacity=128,Epoch=id.Epoch,ParentEpoch=id.ParentEpoch};
        Check(NativeTweenSingleEventBridge.InformationShape(info,id));info.ParentCapacity=32;Check(!NativeTweenSingleEventBridge.InformationShape(info,id));
        info.ParentCapacity=128;info.ParentsEntered=2;info.ParentsExited=1;info.ParentsInFlight=1;info.Mids=2;Check(NativeTweenSingleEventBridge.InformationShape(info,id));
        info.Attached=1;info.Unknown=1;info.RetainedParents=1;Check(NativeTweenSingleEventBridge.InformationShape(info,id));
        info.ParentsExited=3;Check(!NativeTweenSingleEventBridge.InformationShape(info,id));
    }),
    ("the Single machine frame uses RBP event R15 list ESI index and XMM6 rawbits", () => {
        var good = Observe(NativeTweenSingleEventStage.Mid);
        Check(Valid(good));
        var m = good.Machine!.Value; m.Rsi = (ulong)good.Frame.Event.ToInt64(); m.Rbp = (ulong)good.Frame.List.ToInt64();
        Check(!Valid(good with { Machine = m }));
    }),
    ("mid requires its actual one-shot PC not a post or zero source PC", () => {
        var good = Observe(NativeTweenSingleEventStage.Mid); var f = good.Frame; f.SourceReturnPc = IntPtr.Zero; Check(!Valid(good with { Frame = f }));
    }),
    ("three sites validate distinct Single direct-void and object-array register ABIs", () => {
        for(uint site=0;site<3;site++) Check(Valid(Observe(NativeTweenSingleEventStage.Before,site)));
        var o=Observe(NativeTweenSingleEventStage.Before,1);var f=o.Frame;f.ArgRdx=new(44);Check(!Valid(o with{Frame=f}));
        o=Observe(NativeTweenSingleEventStage.Before,2);f=o.Frame;f.ArgRdx=IntPtr.Zero;Check(!Valid(o with{Frame=f}));
    }),
    ("direct-void site does not reinterpret arbitrary XMM1 bits as float payload", () => {
        var o=Observe(NativeTweenSingleEventStage.Before,1);var m=o.Machine!.Value;m.Xmm1.Low=0xdeadbeef;Check(Valid(o with{Machine=m}));
        o=Observe(NativeTweenSingleEventStage.Before,0);m=o.Machine!.Value;m.Xmm1.Low=0xdeadbeef;Check(!Valid(o with{Machine=m}));
    }),
    ("wrong native thread epochs parent metadata and reserved fields cannot pass validation", () => {
        var good=Observe(NativeTweenSingleEventStage.Before);
        for(var mode=0;mode<6;mode++){var f=good.Frame;if(mode==0)f.ThreadId++;if(mode==1)f.Epoch++;if(mode==2)f.ParentEpoch++;if(mode==3)f.ParentSequence=0;if(mode==4)f.ParentMethod=IntPtr.Zero;if(mode==5)f.Reserved2=1;Check(!Valid(good with{Frame=f}));}
    }),
    ("missing post admits only an abnormal retained frame and cannot resemble Returned", () => {
        var o=Observe(NativeTweenSingleEventStage.Abnormal);var f=o.Frame;f.Flags=NativeTweenSingleEventFlags.MissingPost;f.TransportReturnPc=f.SourceReturnPc;Check(Valid(new(f,null)));
        f.Stage=NativeTweenSingleEventStage.Returned;Check(!Valid(new(f,null)));
    }),
    ("a copied DTO cannot construct a live native Delivery authority", () => {
        Throws(()=>new NativeTweenSingleEventBridge.Delivery(new object(),null!,new(1),Observe(NativeTweenSingleEventStage.Mid),null));
    }),
    ("all actual object references are rooted before metadata and function pointers are never rooted", () => {
        var frame=Observe(NativeTweenSingleEventStage.Before,2).Frame;var retained=new List<long>();var handles=new List<GCHandle>();
        try{
            var batch=NativeTweenSingleEventRootBatch.Capture(frame,p=>{Check(p!=frame.ActualTarget&&p!=frame.ParentMethod&&p!=frame.ArgR8);handles.Add(GCHandle.Alloc(new object()));retained.Add(p.ToInt64());return p.ToInt64();});
            GC.Collect();GC.WaitForPendingFinalizers();Check(retained.SequenceEqual(new[]{frame.Event.ToInt64(),frame.List.ToInt64(),frame.Invokable.ToInt64(),frame.ArgRdx.ToInt64()}));Check(handles.All(h=>h.Target is not null)&&batch.ArgumentArray==frame.ArgRdx.ToInt64());
        }finally{foreach(var h in handles)h.Free();}
    }),
    ("partial native root batches preserve every preceding responsibility on failure", () => {
        var f=Observe(NativeTweenSingleEventStage.Before,2).Frame;var retained=new List<long>();
        Throws(()=>NativeTweenSingleEventRootBatch.Capture(f,p=>{if(retained.Count==3)throw new IOException("root failed");retained.Add(p.ToInt64());return p.ToInt64();}));Check(retained.Count==3);
    }),
    ("root identity replacement fails before metadata gets any input", () => {
        var f=Observe(NativeTweenSingleEventStage.Before).Frame;Throws(()=>NativeTweenSingleEventRootBatch.Capture(f,p=>p==f.List?999:p.ToInt64()));
    }),
    ("native Set/Configure or caller Ready inputs are absent from the managed observer front", () => {
        Check(!typeof(NativeTweenSingleEventBridge).GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Any(m=>m.Name.Contains("Configure")||m.Name=="AdoptConfigured"));
    }),
    ("one empty actual mid remains distinct from no mid and Return does not certify effects", Empty),
    ("a nonempty actual list drives all three sites and duplicate objects remain separate occurrences", Mixed),
    ("duplicate mid child generations and out-of-order actual indexes are rejected", Duplicates),
    ("direct field list version backing and item replacements cannot borrow the old mid", ChangedList),
    ("parent native sequence token payload and event aliases cannot borrow a retained list", ForeignParents),
    ("cached effective bits differ from dynamic payload without converting either float", Cached),
    ("actual returned child records newly created work but does not end or erase it", NewWork),
    ("missing post retains the original child and parent Return never repairs it", MissingPost),
    ("abnormal before any successfully rooted Before remains pending instead of empty", AbnormalNoBefore),
    ("parent completion and disposal are once-only and do not manufacture a child return", ParentEnd)
};
foreach(uint bits in new uint[]{0,0x80000000,0x7f800000,0xff800000,0x7fc01234,0xffc05678,0x7f800001,0xffa00001,1,0x3f800000})
{
    var raw=bits;tests.Add(($"all Single payload bit patterns survive exact frame validation {raw:x8}",()=>{
        var o=Observe(NativeTweenSingleEventStage.Before,0,raw);Check(Valid(o));var m=o.Machine!.Value;m.Xmm6.Low^=1;Check(!Valid(o with{Machine=m}));
    }));
}
tests.AddRange(SingleListRuntimeChecks.Cases);
tests.AddRange(SingleDispatchRuntimeChecks.Cases);
tests.AddRange(SingleOwnerIntegrationChecks.Cases);
tests.AddRange(SingleP4RetirementChecks.Cases);
tests.AddRange(SingleSubscriptionChecks.Cases);
var failures=0;foreach(var test in tests){try{test.Run();Console.WriteLine("PASS "+test.Name);}catch(Exception ex){failures++;Console.Error.WriteLine("FAIL "+test.Name+": "+ex);}}
Console.WriteLine($"{tests.Count-failures}/{tests.Count} Single managed source checks passed.");return failures==0?0:1;

static void Check(bool value){if(!value)throw new InvalidOperationException("Single source assertion failed.");}
static void Throws(Action action){try{action();}catch{return;}throw new InvalidOperationException("Expected rejection.");}
static NativeTweenSingleEventIdentity Identity()=>new(5,7,new(0x100),new(0x200),new(0x300),new(0x108c),new(0x110d),new(0x1162),new(0x1221),new(0x2005),new(0x2105),new(0x2205),new(0x154e5b0));
static NativeOperationFrame Parent(uint bits=0x7fc01234)=>new(){Version=1,Size=64,Kind=NativeOperationKind.Single1,Self=new(101),Arg0=new(unchecked((long)bits)),Method=new(111),Caller=new(121),Sequence=31};
static NativeTweenSingleEventObservation Observe(NativeTweenSingleEventStage stage,uint site=0,uint bits=0x7fc01234)
{
    var p=Parent(bits);var id=Identity();var mid=stage==NativeTweenSingleEventStage.Mid;
    var f=new NativeTweenSingleEventFrame{Version=1,Size=200,Stage=stage,Site=mid?3:site,ThreadId=19,Epoch=id.Epoch,ParentEpoch=id.ParentEpoch,ParentSequence=p.Sequence,ParentManagedToken=67,Event=p.Self,List=new(201),Invokable=mid?IntPtr.Zero:new(301),ActualTarget=mid?IntPtr.Zero:site==1?id.DirectTarget:new(401),ArgRcx=mid?IntPtr.Zero:new(301),ArgRdx=site==2?new(501):IntPtr.Zero,ArgR8=site==1?IntPtr.Zero:new(601),ParentMethod=p.Method,ParentCaller=p.Caller,SourceReturnPc=mid?id.MidReturnPc:id.Source(site),TransportReturnPc=mid?id.MidReturnPc:stage==NativeTweenSingleEventStage.Returned?id.Post(site):id.Source(site),SourceRsp=0x10000,Index=0,ValueBits=bits,ChildSequence=mid?0UL:91UL};
    var m=new NativeEventMidFrame{Version=1,Size=432,EntryRsp=0xfff8,Rsp=0x10000,ReturnPc=f.TransportReturnPc,Rbp=(ulong)p.Self.ToInt64(),R15=(ulong)f.List.ToInt64(),R13=(ulong)p.Method.ToInt64(),Rcx=(ulong)f.ArgRcx.ToInt64(),Rdx=(ulong)f.ArgRdx.ToInt64(),R8=(ulong)f.ArgR8.ToInt64(),R14=(ulong)f.Invokable.ToInt64(),Xmm1=new(){Low=bits},Xmm6=new(){Low=bits}};
    return new(f,m);
}
static bool Valid(NativeTweenSingleEventObservation o)=>NativeTweenSingleEventValidation.Valid(o,Identity(),19);
static NativeSingleExecutingList List(params long[] items)=>new(101,201,211,items.Length,9,Array.AsReadOnly(items));
static NativeSingleDispatchTarget Target(NativeTweenSingleEventFrame f,bool cached=false)=>new(f.Site,f.Invokable.ToInt64(),f.ActualTarget.ToInt64(),611,f.ValueBits,f.Site==0?(cached?0x80000000:f.ValueBits):null,cached,f.Site==2?501:0,Array.Empty<long>(),"controlled rooted exact target");
static NativeTweenSingleEventFrame Child(uint site,int index,ulong sequence=91){var f=Observe(NativeTweenSingleEventStage.Before,site).Frame;f.Index=(ulong)index;f.ChildSequence=sequence;return f;}
static NativeTweenSingleEventFrame Returned(NativeTweenSingleEventFrame f){f.Stage=NativeTweenSingleEventStage.Returned;f.TransportReturnPc=Identity().Post(f.Site);return f;}
static NativeLocalTweenSingleEventTracker Tracker(NativeSingleExecutingList list){var t=new NativeLocalTweenSingleEventTracker(Parent());t.Mid(Observe(NativeTweenSingleEventStage.Mid).Frame,list);return t;}
static void Empty(){var a=Tracker(List());a.ParentComplete(true);a.ParentDispose();Check(a.Read().MidObserved&&a.Read().Count==0&&a.Read().Pending.Contains("writer"));var b=new NativeLocalTweenSingleEventTracker(Parent());b.ParentComplete(true);b.ParentDispose();Check(!b.Read().MidObserved&&b.Read().Count is null);}
static void Mixed(){var l=List(301,301,301,301);var t=Tracker(l);for(var i=0;i<4;i++){var f=Child((uint)(i%3),i,(ulong)(91+i));t.Before(f,l,Target(f),Array.Empty<long>());t.Complete(Returned(f),Array.Empty<long>());}t.ParentComplete(true);t.ParentDispose();Check(t.Read().Dispatches.Count==4&&t.Read().Dispatches.Select(c=>c.ChildSequence).Distinct().Count()==4&&t.Read().Dispatches.All(c=>c.Returned&&c.RootedBefore));}
static void Duplicates(){var l=List(301,301);var t=Tracker(l);Throws(()=>t.Mid(Observe(NativeTweenSingleEventStage.Mid).Frame,l));var f=Child(0,1);Throws(()=>t.Before(f,l,Target(f),Array.Empty<long>()));f=Child(0,0);t.Before(f,l,Target(f),Array.Empty<long>());Throws(()=>t.Before(f,l,Target(f),Array.Empty<long>()));t.Complete(Returned(f),Array.Empty<long>());f.Index=1;Throws(()=>t.Before(f,l,Target(f),Array.Empty<long>()));}
static void ChangedList(){var l=List(301);for(var mode=0;mode<4;mode++){var t=Tracker(l);var f=Child(0,0);var changed=mode switch{0=>l with{Version=10},1=>l with{Array=999},2=>l with{Items=new long[]{302}},_=>l with{List=999}};Throws(()=>t.Before(f,changed,Target(f),Array.Empty<long>()));}}
static void ForeignParents(){var l=List(301);for(var mode=0;mode<4;mode++){var t=Tracker(l);var f=Child(0,0);if(mode==0)f.ParentSequence++;if(mode==1)f.ParentManagedToken++;if(mode==2)f.ValueBits++;if(mode==3)f.Event=new(102);Throws(()=>t.Before(f,l,Target(f),Array.Empty<long>()));}}
static void Cached(){var l=List(301);var t=Tracker(l);var f=Child(0,0);t.Before(f,l,Target(f,true),Array.Empty<long>());t.Complete(Returned(f),Array.Empty<long>());Check(t.Read().Dispatches[0].CachedSingle&&t.Read().Dispatches[0].ValueBits==0x7fc01234&&t.Read().Dispatches[0].EffectiveSingleBits==0x80000000);}
static void NewWork(){var l=List(301);var t=Tracker(l);var f=Child(1,0);t.Before(f,l,Target(f),new long[]{1,2});t.Complete(Returned(f),new long[]{1,2,3,4});t.ParentComplete(true);t.ParentDispose();Check(t.Read().Dispatches[0].CreatedOccurrences.SequenceEqual(new long[]{3,4})&&t.Read().Dispatches[0].Pending.Contains("owned"));}
static void MissingPost(){var l=List(301);var t=Tracker(l);var f=Child(0,0);t.Before(f,l,Target(f),Array.Empty<long>());f.Stage=NativeTweenSingleEventStage.Abnormal;f.Flags=NativeTweenSingleEventFlags.MissingPost;t.Complete(f,Array.Empty<long>());t.ParentComplete(true);t.ParentDispose();Check(!t.Read().Dispatches[0].Returned&&t.Read().Dispatches[0].Abnormal&&t.Read().Pending.Contains("retains"));}
static void AbnormalNoBefore(){var t=Tracker(List(301));var f=Child(2,0);f.Stage=NativeTweenSingleEventStage.Abnormal;f.Flags=NativeTweenSingleEventFlags.TargetReadFaulted|NativeTweenSingleEventFlags.MissingPost;t.Complete(f,Array.Empty<long>());t.ParentComplete(false);t.ParentDispose();Check(t.Read().Count==1&&t.Read().Dispatches.Count==0&&t.Read().Pending.Contains("no successfully rooted"));}
static void ParentEnd(){var t=Tracker(List(301));t.ParentComplete(true);t.ParentDispose();Throws(()=>t.ParentDispose());Check(t.Read().Dispatches.Count==0&&t.Read().Pending.Contains("no complete"));Throws(()=>t.ParentComplete(true));}
