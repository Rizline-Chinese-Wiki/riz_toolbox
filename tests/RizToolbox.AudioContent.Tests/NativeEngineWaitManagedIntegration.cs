using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Real native child transport. Controlled tracker providers identify actual
// Starts; they never send native frames or issue completion receipts.
internal static class NativeEngineWaitManagedIntegration
{
    internal const string ModeArgument="--engine-wait-managed";
    internal static readonly string[] Modes={"normal","owner-null","high-eax","start-clear","batch-null","callback-clear","replace","republish"};
    private static readonly List<object> Owners=new();
    [StructLayout(LayoutKind.Sequential)]private struct Descriptor
    {
        internal uint Version,Size,Mode,Slot,MainThread,Configured,Checks,Failures;
        internal IntPtr Image,Kernel,Relay,PhaseEntry,Owner,Enumerator,Manager;internal ulong Handle;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=4)]internal IntPtr[] NodeTargets;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=6)]internal IntPtr[] SchedulerTargets;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=3)]internal IntPtr[] WaitTargets;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=3)]internal IntPtr[] WaitOriginals;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=18)]internal NativeEngineWaitSite[] WaitSites;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=2)]internal NativeEngineSchedulerSite[] SchedulerSites;
        internal IntPtr CallbackCaller,CleanupCaller,BatchCleanupCaller,Enumerator2;internal ulong Handle2;
    }
    [StructLayout(LayoutKind.Sequential)]private struct Receipt
    {
        internal uint Version,Size,Mode,Checks,Failures,NodeStarts,NodeExecutes,NodeReleases,NodeDetaches,NodeFreed,WaitFreed,Publications;
        internal uint Dispatches,Batches,Callbacks,Cleanups,SharedReturns,OldReturns,ProbeCalls,LiveNodes,LiveWaits,Reserved0,Reserved1,Reserved2;
        internal IntPtr Image,Relay,Node,Wait0,Wait1;internal ulong CallbackRaw,YieldRaw,DispatchRaw,BatchRaw;internal IntPtr SecondNode;
    }
    [StructLayout(LayoutKind.Sequential)]private struct MemoryInformation
    {internal IntPtr BaseAddress,AllocationBase;internal uint AllocationProtect;internal ushort PartitionId;internal nuint RegionSize;internal uint State,Protect,Type;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint ScalarNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint ConfigureNative([MarshalAs(UnmanagedType.LPWStr)]string helper,uint mode,ref Descriptor descriptor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void ProbeNative(uint stage,IntPtr node,IntPtr wait,IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint RunNative(ProbeNative probe,IntPtr user,ref Receipt receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint NodeInfoNative(uint kind,uint slot,ref NativeEngineNodeInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint SchedulerInfoNative(uint kind,uint slot,ref NativeEngineSchedulerInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint WaitInfoNative(uint slot,ref NativeEngineWaitInformation info);
    private sealed class Logger:ILogger{public void Log(LogLevel level,string message,Exception? error=null){}}
    private sealed class Provider
    {
        internal readonly object Owner=new();private readonly Descriptor _descriptor;
        internal Provider(Descriptor descriptor)=>_descriptor=descriptor;
        private NativeCoroutineCancellationIdentity Identity(int slot)=>new(Owner,101,slot,1,
            (slot==0?_descriptor.Enumerator:_descriptor.Enumerator2).ToInt64(),0,false,false,true,false,false,true);
        internal NativeCoroutineCancellationIdentityProvider Description=>new(Owner,101,
            ()=>new[]{Identity(0),Identity(1)},value=>value.Slot is 0 or 1&&Identity(value.Slot).SameRecord(value));
    }
    private sealed class NodeScope:IDisposable
    {
        internal readonly NativeEngineCoroutineIdentityLedger.Call Identity;
        internal readonly NativeEngineWaitLedger.CleanupContext? WaitCleanup;
        internal readonly NativeEngineSchedulerRowLedger.CleanupContext? RowCleanup;
        private readonly Action<NodeScope> _after;
        internal NodeScope(NativeEngineCoroutineIdentityLedger.Call identity,NativeEngineWaitLedger.CleanupContext? waitCleanup,
            NativeEngineSchedulerRowLedger.CleanupContext? rowCleanup,Action<NodeScope> after)=>(Identity,WaitCleanup,RowCleanup,_after)=(identity,waitCleanup,rowCleanup,after);
        public void Dispose(){Identity.Dispose();RowCleanup?.Dispose();WaitCleanup?.Dispose();_after(this);}
    }
    private sealed class Scope<T>:IDisposable where T:IDisposable
    {
        internal readonly T Call;private readonly Action<T> _after;
        internal Scope(T call,Action<T> after)=>(Call,_after)=(call,after);
        public void Dispose(){Call.Dispose();_after(Call);}
    }
    internal static int RunChild(string[] args)
    {
        var mode=args.Length==2?args[1]:"invalid";var checks=0;var faults=new List<string>();
        void Check(bool value,string reason){++checks;if(!value)throw new InvalidOperationException(reason);}
        try
        {
            SetErrorMode(0x8003);var scenario=Array.IndexOf(Modes,mode);
            Check(args.Length==2&&args[0]==ModeArgument&&scenario>=0,"Use one exact owned wait integration child mode.");
            Check(OperatingSystem.IsWindows()&&IntPtr.Size==8&&GetModuleHandle("UnityPlayer.dll")==IntPtr.Zero&&GetModuleHandle("GameAssembly.dll")==IntPtr.Zero,
                "An isolated Windows x64 child without game modules is required.");
            Check(Marshal.SizeOf<Descriptor>()==584&&Marshal.SizeOf<Receipt>()==176
                &&Marshal.OffsetOf<Descriptor>(nameof(Descriptor.Enumerator2)).ToInt32()==568
                &&Marshal.OffsetOf<Receipt>(nameof(Receipt.SecondNode)).ToInt32()==168,"Owned wait fixture ABI layout differs.");
            var helperPath=AbsoluteEnvironment("RIZ_ENGINE_WAIT_MANAGED_HELPER");var fixturePath=AbsoluteEnvironment("RIZ_ENGINE_WAIT_MANAGED_FIXTURE");
            var files=new[]{helperPath,fixturePath}.ToDictionary(path=>path,Hash);
            foreach(var path in files.Keys)Owners.Add(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read));
            var helper=NativeLibrary.Load(helperPath);var fixture=NativeLibrary.Load(fixturePath);Owners.Add(helper);Owners.Add(fixture);
            var configure=Export<ConfigureNative>(fixture,"RizEngineWaitManaged_Configure");var run=Export<RunNative>(fixture,"RizEngineWaitManaged_Run");
            Check(Export<ScalarNative>(fixture,"RizEngineWaitManaged_GetDescriptorSize")()==584
                &&Export<ScalarNative>(fixture,"RizEngineWaitManaged_GetReceiptSize")()==176,"Owned DLL exported sizes differ from the CLR transport.");
            var readNode=Export<NodeInfoNative>(helper,"RizEngineNodeContext_GetInfo");
            var readScheduler=Export<SchedulerInfoNative>(helper,"RizEngineSchedulerContext_GetInfo");
            var readWait=Export<WaitInfoNative>(helper,"RizEngineWaitContext_GetInfo");
            var descriptor=new Descriptor{Version=1,Size=584,NodeTargets=new IntPtr[4],SchedulerTargets=new IntPtr[6],WaitTargets=new IntPtr[3],
                WaitOriginals=new IntPtr[3],WaitSites=new NativeEngineWaitSite[18],SchedulerSites=new NativeEngineSchedulerSite[2]};
            Check(configure(helperPath,(uint)scenario,ref descriptor)==0&&descriptor.Version==1&&descriptor.Size==584&&descriptor.Mode==(uint)scenario
                &&descriptor.Slot==26&&descriptor.Configured==1&&descriptor.Checks>0&&descriptor.Failures==0
                &&descriptor.Image==fixture&&descriptor.Kernel==helper&&descriptor.Relay!=IntPtr.Zero
                &&descriptor.Owner!=IntPtr.Zero&&descriptor.Enumerator!=IntPtr.Zero&&descriptor.Enumerator2!=IntPtr.Zero
                &&descriptor.Enumerator!=descriptor.Enumerator2&&descriptor.Handle!=0&&descriptor.Handle2!=0&&descriptor.Handle!=descriptor.Handle2
                &&descriptor.MainThread==NativeEngineInlinePlatform.CurrentOsThreadId,"Owned DLL did not configure exact unused real channel identities.");
            void OwnedCode(IntPtr address,string label)
            {Check(address!=IntPtr.Zero&&VirtualQuery(address,out var memory,MemorySize)==MemorySize&&memory.AllocationBase==fixture
                &&memory.Type==0x1000000&&(memory.Protect&0xf0)!=0,label+" is outside the owned DLL executable image.");}
            foreach(var target in descriptor.NodeTargets.Concat(descriptor.SchedulerTargets).Concat(descriptor.WaitTargets))OwnedCode(target,"Configured target");
            foreach(var site in descriptor.WaitSites){Check(site.Complete,"Incomplete exact Wait site pair.");OwnedCode(site.SitePc,"Wait source window");}
            foreach(var site in descriptor.SchedulerSites){Check(site.Complete,"Incomplete scheduler mask site.");OwnedCode(site.SitePc,"Scheduler source window");}
            foreach(var caller in new[]{descriptor.CallbackCaller,descriptor.CleanupCaller,descriptor.BatchCleanupCaller})OwnedCode(caller,"Actual indirect CALL return PC");
            var log=new Logger();var nodeBridges=new NativeEngineNodeContextBridge[4];
            for(var kind=0;kind<4;++kind)
            {
                var info=new NativeEngineNodeInformation{Version=1,Size=112};
                Check(readNode((uint)kind,descriptor.Slot,ref info)==0&&info.Kind==(uint)kind&&info.Slot==descriptor.Slot&&info.Attached==0&&info.Unknown==0
                    &&info.Entered==0&&info.Exited==0&&info.InFlight==0&&info.Target==descriptor.NodeTargets[kind],"Node channel was used or mismatched before CLR adoption.");
                nodeBridges[kind]=NativeEngineNodeContextBridge.AdoptConfigured(helperPath,info.Identity,log);
            }
            var schedulerBridges=new NativeEngineSchedulerContextBridge[6];
            for(var kind=0;kind<6;++kind)
            {
                var info=new NativeEngineSchedulerInformation{Version=1,Size=144};
                Check(readScheduler((uint)kind,descriptor.Slot,ref info)==0&&info.Configured==1&&info.Attached==0&&info.Unknown==0
                    &&info.Entered==0&&info.Exited==0&&info.InFlight==0&&info.RowsInFlight==0&&info.Target==descriptor.SchedulerTargets[kind],
                    "Scheduler channel was used or mismatched before CLR adoption.");
                schedulerBridges[kind]=NativeEngineSchedulerContextBridge.AdoptConfigured(helperPath,new((NativeEngineSchedulerKind)kind,descriptor.Slot,
                    info.Epoch,descriptor.MainThread,info.Target,info.Original,info.Entry,info.RowEntry,
                    kind==4?descriptor.SchedulerSites[0]:default,kind==4?descriptor.SchedulerSites[1]:default),log);
            }
            var wi=new NativeEngineWaitInformation{Version=1,Size=176};
            Check(readWait(descriptor.Slot,ref wi)==0&&wi.Configured==1&&wi.Attached==0&&wi.Unknown==0&&wi.Entered==0&&wi.Exited==0
                &&wi.InFlight==0&&wi.Faults==0&&wi.SnapshotFaults==0&&wi.Unmatched==0&&wi.PhaseEntry==descriptor.PhaseEntry,
                "Wait family was used or attached before CLR adoption.");
            var waitIdentity=new NativeEngineWaitFamilyIdentity(descriptor.Slot,wi.Epoch,descriptor.MainThread,
                descriptor.WaitTargets.Concat(descriptor.NodeTargets).Concat(descriptor.SchedulerTargets).ToArray(),descriptor.WaitOriginals,
                new[]{wi.YieldEntry,wi.DispatchEntry,wi.BatchEntry},descriptor.PhaseEntry,descriptor.WaitSites);
            Check(NativeEngineWaitContextBridge.MatchesInformation(wi,waitIdentity,false),"Wait descriptor and actual helper GetInfo differ.");
            var waitBridge=NativeEngineWaitContextBridge.AdoptConfigured(helperPath,waitIdentity,log);
            var runtime=NativeEngineRuntimeCoordinator.ShareControlled(new object(),nodeBridges,log,schedulerBridges:schedulerBridges,waitBridge:waitBridge);
            var scheduler=runtime.RequireSchedulerObservation();var wait=runtime.RequireWaitObservation();
            using var main=new MainThreadDispatcher();var nodes=new NativeEngineCoroutineNodeLedger(main);var provider=new Provider(descriptor);
            var identities=new NativeEngineCoroutineIdentityLedger(main,nodes,new[]{provider.Description},new[]{new NativeEngineCoroutineOwnerPair(101,descriptor.Owner.ToInt64())});
            var waits=new NativeEngineWaitLedger(main,identities,nodes,wait);
            var callers=new NativeEngineSchedulerCallerProfile(descriptor.CallbackCaller,descriptor.CallbackCaller,descriptor.CallbackCaller,
                descriptor.CleanupCaller,descriptor.CleanupCaller,descriptor.BatchCleanupCaller);
            var rows=new NativeEngineSchedulerRowLedger(main,identities,nodes,scheduler,descriptor.SchedulerTargets[5],descriptor.NodeTargets[2],callers,waits.TryClaimCallback);
            var nodeFrames=new List<NativeEngineNodeFrame>();var nodeCompletions=new Dictionary<ulong,NativeEngineNodeCompletion>();
            var schedulerFrames=new List<NativeEngineSchedulerFrame>();var schedulerCompletions=new Dictionary<ulong,NativeEngineSchedulerCompletion>();
            var waitFrames=new List<NativeEngineWaitFrame>();var waitCompletions=new Dictionary<ulong,NativeEngineWaitCompletion>();
            var phases=new List<NativeEngineWaitPhaseFrame>();var probes=new List<object>();var transitionReceipts=new List<object>();
            var nodeDisposes=0;var schedulerDisposes=0;var waitDisposes=0;var cleanupCaptures=0;var cleanupCompletes=0;var cleanupDisposes=0;
            var typedCleanupStillPending=0;var outerCompleteStillPending=0;var capturedCallbacks=0;var retirementGateChecks=0;
            var lateNullDispatches=new Dictionary<ulong,long>();
            var lateNullDispatchCaptures=0;var lateNullDispatchCompletes=0;var lateNullDispatchDisposes=0;
            void CheckLateNullRecord(NativeEngineWaitLedgerSnapshot snapshot,ulong sequence)
            {
                var record=snapshot.Records.Single(value=>value.PublicationGeneration==lateNullDispatches[sequence]);
                Check(!snapshot.Unknown&&record.State==NativeEngineWaitState.Retired&&!record.FieldsCleared
                    &&record.CallbackSlotCleared&&record.OpenDispatches==0&&record.OpenCallbacks==0&&record.OpenCleanups==0,
                    "A late callback-null Dispatch borrowed or changed its retired publication.");
            }
            using var waitBinding=wait.Bind(observation=>{
                Check(observation.IsCurrent&&observation.Completion is null&&observation.Phase is null,"Wait Enter lacks its current native-issued receipt.");
                OwnedCode(observation.Frame.Caller,"Wait caller");waitFrames.Add(observation.Frame);
                var call=waits.Capture(observation);
                if(observation.Frame.Kind==NativeEngineWaitKind.Dispatch&&observation.Frame.Tuple.Callback==IntPtr.Zero)
                {
                    var frame=observation.Frame;var snapshot=waits.Read();
                    var record=snapshot.Records.Last(value=>value.Wait==frame.Wait.ToInt64());
                    Check(mode=="replace"&&frame.Wait!=IntPtr.Zero&&frame.Wait.ToInt64()==record.Wait,
                        "A callback-null Dispatch must be the actual retired wait in the replace scenario.");
                    Check(frame.Node==IntPtr.Zero&&frame.NodeSnapshotFlags==0&&call._dispatch is null&&call._nodeCall is null
                        &&frame.Tuple.Cleanup==descriptor.NodeTargets[2]&&frame.Tuple.Payload.ToInt64()==record.Node
                        &&snapshot.Outstanding>0&&snapshot.Pending&&!call.Completed&&!call.Disposed,
                        "The callback-null Dispatch did not retain a separate physical duty without a node claim.");
                    lateNullDispatches.Add(frame.Sequence,record.PublicationGeneration);++lateNullDispatchCaptures;
                    CheckLateNullRecord(snapshot,frame.Sequence);
                }
                return new Scope<NativeEngineWaitLedger.Context>(call,context=>{
                    ++waitDisposes;Check(context.Disposed,"Wait Context Dispose did not finish.");
                    if(lateNullDispatches.ContainsKey(context.Frame.Sequence))
                    {
                        CheckLateNullRecord(waits.Read(),context.Frame.Sequence);
                        Check(context.Completed&&waits.Read().Outstanding==0,"Late callback-null physical duty survived actual Dispose.");
                        ++lateNullDispatchDisposes;
                    }
                    transitionReceipts.Add(new{sequence=context.Frame.Sequence,kind=(uint)context.Frame.Kind,afterDispose=true,
                        outstanding=waits.Read().Outstanding,records=waits.Read().Records});
                });
            },(context,observation)=>{
                Check(observation.IsCurrent&&observation.Completion is{} completion&&completion.IsNormalFor(observation.Frame.Kind),"Wait original exited abnormally.");
                var before=waits.Read();
                if(lateNullDispatches.ContainsKey(observation.Frame.Sequence))
                {
                    CheckLateNullRecord(before,observation.Frame.Sequence);
                    Check(before.Outstanding>0&&before.Pending&&!context.Call.Completed&&!context.Call.Disposed
                        &&observation.Completion!.Value.Phases==0&&!phases.Any(value=>value.Sequence==observation.Frame.Sequence),
                        "The callback-null original did not skip its phases while retaining the full physical scope.");
                    ++lateNullDispatchCompletes;
                }
                if(observation.Frame.Kind==NativeEngineWaitKind.Dispatch&&before.Records.Any(record=>record.Wait==observation.Frame.Wait.ToInt64()))
                {Check(before.Records.Where(record=>record.OpenDispatches>0).All(record=>record.State!=NativeEngineWaitState.Retired),"Wait retired while its actual Dispatch Complete was still open.");++outerCompleteStillPending;}
                waits.Complete(context.Call,observation);waitCompletions.Add(observation.Frame.Sequence,observation.Completion!.Value);
            },(context,observation)=>{
                Check(observation.IsCurrent&&observation.Phase is{} phase&&phase.IsValid&&!phase.HasFault,"Wait phase is not an exact real receipt.");
                phases.Add(observation.Phase!.Value);waits.ObservePhase(context.Call,observation);
            },(frame,reason)=>{faults.Add("Wait: "+reason);waits.ObserveFault(frame,reason);});
            using var nodeBinding=runtime.Bind(frame=>{
                Check(frame.IsValid,"Actual Node frame differs from ABI2.");OwnedCode(frame.Caller,"Node caller");nodeFrames.Add(frame);
                var call=identities.Capture(frame);NativeEngineWaitLedger.CleanupContext? waitCleanup=null;NativeEngineSchedulerRowLedger.CleanupContext? rowCleanup=null;
                if(call.NodeCall is{} actual){waits.BindNodeCall(actual);if(frame.Kind==NativeEngineNodeKind.Release){waitCleanup=waits.CaptureCleanup(actual);if(waitCleanup is null&&!waits.Unknown)rowCleanup=rows.CaptureCleanup(actual);}}
                if(waitCleanup is not null)++cleanupCaptures;
                return new NodeScope(call,waitCleanup,rowCleanup,context=>{
                    ++nodeDisposes;Check(context.Identity.Disposed,"Actual typed Identity Dispose did not finish.");
                    if(context.WaitCleanup is not null){++cleanupDisposes;Check(waits.Read().Records.Any(record=>record.OpenCleanups>0&&record.State!=NativeEngineWaitState.Retired),
                        "Typed cleanup Dispose bypassed the still-open actual Wait side boundary.");++typedCleanupStillPending;}
                });
            },(context,frame,completion)=>{
                Check(completion.IsValidFor(frame.Kind),"Actual Node completion differs from its return carrier.");
                identities.Complete(context.Identity,frame,completion);nodeCompletions.Add(frame.Sequence,completion);context.RowCleanup?.Complete();context.WaitCleanup?.Complete();
                if(context.WaitCleanup is not null)++cleanupCompletes;
            },(frame,reason)=>{faults.Add("Node: "+reason);identities.ObserveFault(frame,reason);});
            using var schedulerBinding=scheduler.Bind(observation=>{
                Check(observation.IsCurrent&&observation.Completion is null&&observation.Row is null,"M Enter lacks its current actual scope.");
                OwnedCode(observation.Frame.Caller,"M caller");schedulerFrames.Add(observation.Frame);var call=rows.Capture(observation);
                if(call.WaitCallback is not null)++capturedCallbacks;
                return new Scope<NativeEngineSchedulerRowLedger.Context>(call,context=>{++schedulerDisposes;Check(context.Disposed,"Actual M Context Dispose did not finish.");});
            },(context,observation)=>{
                Check(observation.IsCurrent&&observation.Completion is{Normal:true},"Actual M callback completion failed.");rows.Complete(context.Call,observation);
                schedulerCompletions.Add(observation.Frame.Sequence,observation.Completion!.Value);
            },(context,observation)=>rows.ObserveRow(context.Call,observation),(frame,reason)=>{faults.Add("Scheduler: "+reason);rows.ObserveFault(frame,reason);});
            ProbeNative probe=(stage,node,waitObject,user)=>{
                try
                {
                    Check(stage is >=1 and <=5&&user==IntPtr.Zero,"Owned native probe stage/user is invalid.");
                    var snapshot=waits.Read();Check(!snapshot.Unknown&&!rows.Unknown&&!identities.Unknown&&!nodes.Unknown,"Native probe observed Unknown.");
                    var selected=identities.Read().Identities.Where(identity=>identity.Node==node.ToInt64()&&identity.EngineGeneration>0).ToArray();
                    var readiness=selected.Select(identity=>waits.MayRetireNodeGeneration(identity)).ToArray();
                    if(snapshot.Records.Any(record=>record.Node==node.ToInt64()&&record.State!=NativeEngineWaitState.Retired))
                    {Check(selected.Length==1&&!readiness.Single()&&snapshot.Pending,"An actual live publication failed its exact private per-node gate.");++retirementGateChecks;}
                    probes.Add(new{stage,node=node.ToInt64(),wait=waitObject.ToInt64(),snapshot.Outstanding,snapshot.Pending,readiness,records=snapshot.Records});
                }
                catch(Exception error){faults.Add("Native probe inspection: "+error);}
            };
            Owners.AddRange(new object[]{nodeBridges,schedulerBridges,waitBridge,runtime,provider,identities,nodes,waits,rows,nodeBinding,schedulerBinding,waitBinding,probe});
            var native=new Receipt{Version=1,Size=176};
            Check(run(probe,IntPtr.Zero,ref native)==0&&native.Version==1&&native.Size==176&&native.Mode==(uint)scenario&&native.Checks>0&&native.Failures==0
                &&native.Image==fixture&&native.Relay==descriptor.Relay&&native.Reserved0==0&&native.Reserved1==0&&native.Reserved2==0,
                "Actual native Run failed or returned an incomplete receipt.");
            var final=waits.Read();var nodeFinal=identities.Read();var rowFinal=rows.Read();
            Check(faults.Count==0&&!final.Unknown&&!nodeFinal.Unknown&&!rowFinal.Unknown&&!runtime.Unknown
                &&runtime.ReadActivity() is{Known:true,InFlight:0}&&wait.ReadActivity() is{Known:true,InFlight:0}&&scheduler.ReadActivity() is{Known:true,InFlight:0},
                "Actual Native-to-CLR scopes failed: "+string.Join("; ",faults)+"; wait="+final.Failure+"; rows="+rowFinal.Failure+"; identities="+nodeFinal.Failure);
            var expectedPublications=mode is "replace" or "republish"?2:1;var expectedNodes=mode=="republish"?2:1;
            Check(final.Records.Count==expectedPublications&&final.Records.All(record=>record.State==NativeEngineWaitState.Retired)
                &&final.Outstanding==0&&!final.Pending&&!final.EngineCancelled&&!final.FutureClosed,"Wait publications did not drain after their actual full scopes.");
            Check(nodeFinal.Identities.Count==expectedNodes&&nodeFinal.Nodes.Nodes.Count==expectedNodes&&nodeFinal.Outstanding==0
                &&nodeFinal.Nodes.Nodes.All(node=>node.ActiveExecutions==0&&node.OpenCalls==0&&node.StartCompleted),"Actual Node generations did not complete/dispose.");
            foreach(var record in final.Records)
                Check(nodeFinal.Nodes.Nodes.Any(node=>node.Node==record.Node&&node.Generation==record.NodeGeneration&&(ulong)node.StartSequence==record.StartSequence),
                    "Wait borrowed a synthetic or different native Node generation.");
            Check(nodeFinal.Identities.All(identity=>waits.MayRetireNodeGeneration(identity))&&rowFinal.Records.Count==0&&rowFinal.Outstanding==0&&!rowFinal.Pending
                &&!rowFinal.EngineCancelled&&!rowFinal.FutureClosed,"Per-node Wait gate failed or M misclassified a Wait callback as a queue row.");
            uint[] expected=scenario switch
            {
                0=>new uint[]{1,2,2,0,1,1,1,1},1=>new uint[]{1,1,2,1,1,1,0,1},2=>new uint[]{1,2,2,0,1,1,1,1},
                3=>new uint[]{1,1,1,0,1,0,0,0},4=>new uint[]{1,2,2,0,1,1,1,1},5=>new uint[]{1,2,2,0,1,1,1,2},
                6=>new uint[]{1,4,3,0,2,3,2,2},7=>new uint[]{2,3,4,0,2,1,1,3},_=>throw new InvalidOperationException("Unexpected native scenario.")
            };
            Check(native.NodeStarts==expected[0]&&native.NodeExecutes==expected[1]&&native.NodeReleases==expected[2]&&native.NodeDetaches==expected[3]
                &&native.Publications==expected[4]&&native.Dispatches==expected[5]&&native.Callbacks==expected[6]&&native.Cleanups==expected[7]&&native.Batches==1,
                "Actual owned native mode counters differ from the exact expected originals.");
            Check(native.NodeStarts==expectedNodes&&native.Publications==expectedPublications&&native.NodeFreed==expectedNodes
                &&native.LiveNodes==0&&native.LiveWaits==0&&native.WaitFreed==(mode=="replace"?2u:1u)
                &&native.Callbacks==(uint)schedulerFrames.Count(frame=>frame.Kind==NativeEngineSchedulerKind.Callback)&&capturedCallbacks==native.Callbacks,
                "Native original/publication/free/callback counts differ from CLR observations.");
            Check(native.NodeExecutes==(uint)nodeFrames.Count(frame=>frame.Kind==NativeEngineNodeKind.Execute)
                &&native.NodeReleases==(uint)nodeFrames.Count(frame=>frame.Kind==NativeEngineNodeKind.Release)
                &&native.NodeDetaches==(uint)nodeFrames.Count(frame=>frame.Kind==NativeEngineNodeKind.Detach),"Native Node original counts differ from actual CLR captures.");
            Check(nodeFrames.Count==nodeCompletions.Count&&nodeDisposes==nodeFrames.Count&&schedulerFrames.Count==schedulerCompletions.Count
                &&schedulerDisposes==schedulerFrames.Count&&waitFrames.Count==waitCompletions.Count&&waitDisposes==waitFrames.Count
                &&cleanupCaptures==cleanupCompletes&&cleanupCaptures==cleanupDisposes&&typedCleanupStillPending==cleanupCaptures
                &&probes.Count==native.ProbeCalls,"Actual Complete/Dispose or cleanup/probe duties were omitted/repeated.");
            Check(lateNullDispatchCaptures==(mode=="replace"?1:0)&&lateNullDispatchCompletes==lateNullDispatchCaptures
                &&lateNullDispatchDisposes==lateNullDispatchCaptures,"The exact late callback-null original was omitted or repeated.");
            if(mode!="start-clear")Check(nodeFinal.Nodes.Nodes.All(node=>node.FinalCleanupObserved&&!node.InlineCleanupUnproven),"A real final Release was not observed for every Node.");
            else Check(nodeFinal.Nodes.Nodes.Single().InlineCleanupUnproven&&!nodeFinal.Nodes.Nodes.Single().FinalCleanupObserved
                &&phases.Count(value=>value.Kind is NativeEngineWaitPhase.StartClearBegin or NativeEngineWaitPhase.StartFieldsCleared or NativeEngineWaitPhase.StartReferenceReturned)==3,
                "Wait Start-clear must retain the separate unproven inline Node cleanup responsibility.");
            if(mode=="republish")Check(final.Records.Select(record=>record.NodeGeneration).Distinct().Count()==2
                &&final.Records[0].Wait==final.Records[1].Wait&&native.SecondNode!=IntPtr.Zero&&native.Node!=native.SecondNode,
                "Republish needs a second real Start/Execute Node at the same retained Wait object.");
            if(mode=="replace")Check(final.Records.Select(record=>record.NodeGeneration).Distinct().Count()==1
                &&final.Records[0].Wait!=final.Records[1].Wait&&native.OldReturns>0,"Replace conflated the same Node's distinct old/new Wait publications.");
            if(mode=="high-eax")Check(native.CallbackRaw>uint.MaxValue&&unchecked((uint)native.CallbackRaw)!=1
                &&unchecked((byte)native.CallbackRaw)==1&&cleanupCaptures>0,"Wait cleanup incorrectly depended on callback AL/EAX.");
            ulong nativeInFlight=0;
            for(var kind=0;kind<4;++kind)
            {
                var info=new NativeEngineNodeInformation{Version=1,Size=112};
                Check(readNode((uint)kind,descriptor.Slot,ref info)==0&&info.Attached==1&&info.Unknown==0&&info.Entered==info.Exited&&info.InFlight==0
                    &&info.Entered==(ulong)nodeFrames.Count(frame=>(uint)frame.Kind==(uint)kind),"Native Node channel is unbalanced against CLR observations.");nativeInFlight+=info.InFlight;
            }
            for(var kind=0;kind<6;++kind)
            {
                var info=new NativeEngineSchedulerInformation{Version=1,Size=144};
                Check(readScheduler((uint)kind,descriptor.Slot,ref info)==0&&info.Attached==1&&info.Unknown==0&&info.Entered==info.Exited&&info.InFlight==0
                    &&info.RowsInFlight==0&&info.UnmatchedRows==0&&info.Entered==(ulong)schedulerFrames.Count(frame=>(uint)frame.Kind==(uint)kind),"Native M channel is unbalanced against CLR observations.");nativeInFlight+=info.InFlight+info.RowsInFlight;
            }
            wi=new(){Version=1,Size=176};
            Check(readWait(descriptor.Slot,ref wi)==0&&wi.Attached==1&&wi.Unknown==0&&wi.Entered==wi.Exited&&wi.InFlight==0&&wi.Faults==0
                &&wi.SnapshotFaults==0&&wi.Unmatched==0&&wi.Entered==(ulong)waitFrames.Count&&wi.Phases==(ulong)phases.Count,
                "Native Wait family is unbalanced against CLR scopes/phases.");nativeInFlight+=wi.InFlight;
            Check(native.YieldRaw==0x1122334455667701UL&&native.BatchRaw==0x3344556677889903UL
                &&(native.Dispatches==0?native.DispatchRaw==0:native.DispatchRaw==0x2233445566778802UL)
                &&waitFrames.Where(frame=>(uint)frame.Kind<3).All(frame=>waitCompletions[frame.Sequence].RawRax==
                    (frame.Kind==NativeEngineWaitKind.Yield?native.YieldRaw:frame.Kind==NativeEngineWaitKind.Dispatch?native.DispatchRaw:native.BatchRaw)),
                "Actual Wait machine RAX carriers were normalized or lost in CLR transport.");
            foreach(var frame in nodeFrames.Where(frame=>frame.Kind==NativeEngineNodeKind.Execute))
                Check(((frame.EnumeratorCache==descriptor.Enumerator&&frame.EnumeratorHandle==descriptor.Handle)
                    ||(frame.EnumeratorCache==descriptor.Enumerator2&&frame.EnumeratorHandle==descriptor.Handle2))
                    &&nodeCompletions[frame.Sequence].RawReturn==(mode=="high-eax"?0x12340001u:1u),"Actual Node cache/handle or full Execute EAX differs from its native original.");
            Check(schedulerCompletions.Values.All(completion=>completion.RawRax==native.CallbackRaw),"Actual M callback full RAX differs from its native original.");
            Check(files.All(pair=>Hash(pair.Key)==pair.Value),"Native fixture input bytes changed.");
            Console.WriteLine(JsonSerializer.Serialize(new{
                schemaVersion=1,kind="native-engine-wait-managed-integration",mode,passed=true,checks,failures=0,ownedFixtureAbi=1,
                nativeChecks=native.Checks,nativeFailures=native.Failures,actualNativeToClrCallbacksVerified=true,actualNativeWaitClrIntegration=true,nativeReturned=true,drained=true,actualWaitDispatcherVerified=true,
                actualWaitLedgerVerified=true,schedulerMaskRouteConfigured=true,actualSchedulerMaskRouteVerified=schedulerFrames.Count>0&&capturedCallbacks==native.Callbacks,actualNodeIdentityLedgerVerified=true,sourceModuleIsOwnedDll=true,
                actualTypedCompleteDisposeVerified=true,actualPerGenerationRetirementVerified=true,trackerIdentityProvidersControlled=true,
                actualTrackerBackendExecuted=false,configuredNodeChannels=4,attachedNodeChannels=4,configuredSchedulerChannels=6,attachedSchedulerChannels=6,
                configuredWaitFamilies=1,attachedWaitFamilies=1,nativeStarts=native.NodeStarts,nativeExecutes=native.NodeExecutes,nativeReleases=native.NodeReleases,
                nativeDetaches=native.NodeDetaches,nativeNodesFreed=native.NodeFreed,nativeWaitsFreed=native.WaitFreed,nativePublications=native.Publications,
                nativeDispatches=native.Dispatches,nativeBatches=native.Batches,nativeCallbacks=native.Callbacks,nativeCleanups=native.Cleanups,
                nativeSharedReturns=native.SharedReturns,nativeOldReturns=native.OldReturns,nativeLiveNodes=native.LiveNodes,nativeLiveWaits=native.LiveWaits,nodeStorageReleaseKind="MEM_DECOMMIT within retained owned reservations",nodeStorageReleasedToSystem=false,
                callbackRaw=native.CallbackRaw,yieldRaw=native.YieldRaw,dispatchRaw=native.DispatchRaw,batchRaw=native.BatchRaw,
                nativeInFlight,ledgerOutstanding=final.Outstanding,ledgerPending=final.Pending,ledgerUnknown=final.Unknown,ledgerEngineCancelled=final.EngineCancelled,
                ledgerFutureClosed=final.FutureClosed,retirementGateChecks,outerCompleteStillPending,typedCleanupStillPending,nodeDisposes,schedulerDisposes,waitDisposes,
                cleanupCaptures,cleanupCompletes,cleanupDisposes,capturedCallbacks,actualProbeCount=probes.Count,probes,transitionReceipts,
                lateNullDispatchCaptures,lateNullDispatchCompletes,lateNullDispatchDisposes,
                actualLateNullDispatchVerified=lateNullDispatchCaptures==1&&lateNullDispatchCompletes==1&&lateNullDispatchDisposes==1,
                publications=final.Records,nodeGenerations=nodeFinal.Nodes.Nodes,
                phases=phases.Select(value=>new{kind=(uint)value.Kind,value.Sequence,value.ExternalSequence,value.OperationSequence,value.EventSequence,
                    wait=value.Wait.ToInt64(),node=value.Node.ToInt64(),oldWait=value.OldWait.ToInt64(),flags=(uint)value.Flags,stageFlags=(uint)value.StageFlags,
                    callback=value.ActualCallback.ToInt64(),cleanup=value.ActualCleanup.ToInt64(),payload=value.ActualPayload.ToInt64()}).ToArray(),
                waitCalls=waitFrames.Select(value=>new{kind=(uint)value.Kind,value.Sequence,value.ParentSequence,value.RootSequence,value.ExternalSequence,value.SourceSequence,
                    value.OperationSequence,flags=(uint)value.Flags,rawRax=waitCompletions[value.Sequence].RawRax}).ToArray(),
                engineCancelled=false,engineCancellationCompleted=false,futureProducerClosureVerified=false,schedulerClosureVerified=false,productionAdmissionVerified=false,
                productionPublishedAdoptionUsed=false,gameExecuted=false,installed=false,
                files=files.Select(pair=>new{path=pair.Key,beforeSha256=pair.Value,afterSha256=Hash(pair.Key)}).ToArray()
            }));return 0;
        }
        catch(Exception error)
        {
            Console.Error.WriteLine(error);Console.WriteLine(JsonSerializer.Serialize(new{schemaVersion=1,kind="native-engine-wait-managed-integration",mode,passed=false,
                checks,failures=1,failure=error.ToString(),callbackFaults=faults,actualNativeToClrCallbacksVerified=false,engineCancelled=false,
                engineCancellationCompleted=false,futureProducerClosureVerified=false,schedulerClosureVerified=false,productionAdmissionVerified=false,gameExecuted=false,installed=false}));return 1;
        }
    }
    private static string AbsoluteEnvironment(string name)
    {var value=Environment.GetEnvironmentVariable(name)??throw new InvalidOperationException("Missing "+name);if(!Path.IsPathFullyQualified(value)||!File.Exists(value))throw new InvalidOperationException("Existing absolute file required: "+name);return Path.GetFullPath(value);}
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static T Export<T>(IntPtr module,string name)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
    private static readonly nuint MemorySize=checked((nuint)Marshal.SizeOf<MemoryInformation>());
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")]private static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll")]private static extern nuint VirtualQuery(IntPtr address,out MemoryInformation information,nuint size);
}
