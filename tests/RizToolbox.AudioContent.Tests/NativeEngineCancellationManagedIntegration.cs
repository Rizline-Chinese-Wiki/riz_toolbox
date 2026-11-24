using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Isolated actual native callbacks. Owned source objects and receipts live in
// this test assembly and cannot be consumed by a production launch/issuer.
internal static class NativeEngineCancellationManagedIntegration
{
    internal const string ModeArgument="--engine-cancellation-managed";
    internal static readonly string[] Modes={"normal","missing-source","wrong-generation"};
    private static readonly List<object> Roots=new();
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
    [StructLayout(LayoutKind.Sequential)]private struct NativeReceipt
    {
        internal uint Version,Size,Mode,Checks,Failures,Starts,Executes,Releases,Detaches,NodesFreed,WaitsFreed;
        internal uint Publications,Dispatches,Batches,Callbacks,Cleanups,SharedReturns,OldReturns,ProbeCalls,LiveNodes,LiveWaits,Reserved0,Reserved1,Reserved2;
        internal IntPtr Image,Relay,Node,Wait0,Wait1;internal ulong CallbackRaw,YieldRaw,DispatchRaw,BatchRaw;internal IntPtr SecondNode;
    }
    [StructLayout(LayoutKind.Sequential)]private struct SourceRow
    {
        internal ulong Generation;internal IntPtr Object,CachedOwner;
        internal uint Role,State,References,ProducerReferences,OpenScopes,WithdrawAttempts,RootReleaseAttempts,Reserved;
        internal ulong Withdrawn,Dead;
    }
    [StructLayout(LayoutKind.Sequential)]private struct Extension
    {
        internal uint Version,Size,Configured,Ran,SourceCount,StopCalls,StopOriginalCalls,Closed;
        internal ulong EventSequence;internal IntPtr StopOriginal,StopEntry;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=2)]internal IntPtr[] Receivers;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=2)]internal IntPtr[] NativeOwners;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=3)]internal NativeEngineInlineSite[] InlineSites;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=5)]internal SourceRow[] Sources;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint ConfigureNative([MarshalAs(UnmanagedType.LPWStr)]string path,ref Descriptor output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint InfoNative(ref Extension output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint SetStopNative(IntPtr entry);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint TransitionNative(ulong generation);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void ProbeNative(uint stage,IntPtr node,IntPtr wait,IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint RunNative(ProbeNative probe,IntPtr user,ref NativeReceipt output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint NodeInfo(uint kind,uint slot,ref NativeEngineNodeInformation output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint InlineInfo(uint slot,ref NativeEngineInlineInformation output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint SchedulerInfo(uint kind,uint slot,ref NativeEngineSchedulerInformation output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint WaitInfo(uint slot,ref NativeEngineWaitInformation output);
    private sealed class Logger:ILogger{public void Log(LogLevel level,string message,Exception? error=null){} }
    private sealed class Scope<T>:IDisposable where T:IDisposable
    {internal readonly T Value;internal Scope(T value)=>Value=value;public void Dispose()=>Value.Dispose();}
    private sealed class NodeScope:IDisposable
    {
        internal readonly NativeEngineCoroutineIdentityLedger.Call Identity;
        internal readonly NativeEngineWaitLedger.CleanupContext? WaitCleanup;
        internal readonly NativeEngineSchedulerRowLedger.CleanupContext? RowCleanup;
        internal NodeScope(NativeEngineCoroutineIdentityLedger.Call identity,NativeEngineWaitLedger.CleanupContext? wait,
            NativeEngineSchedulerRowLedger.CleanupContext? row)=>(Identity,WaitCleanup,RowCleanup)=(identity,wait,row);
        public void Dispose(){Identity.Dispose();RowCleanup?.Dispose();WaitCleanup?.Dispose();}
    }
    private sealed class Provider
    {
        internal readonly object Owner=new();internal readonly long Receiver,Enumerator;internal bool Started;
        internal Provider(long receiver,long enumerator)=>(Receiver,Enumerator)=(receiver,enumerator);
        internal NativeCoroutineCancellationIdentity Identity=>new(Owner,Receiver,0,1,Enumerator,0,false,Started,true,false,false,true);
        internal NativeCoroutineCancellationIdentityProvider Description=>new(Owner,Receiver,()=>new[]{Identity},row=>row.SameRecord(Identity));
    }
    private sealed class OwnedSourceIssuer
    {
        private readonly InfoNative _read;private readonly SourceRow[] _before;private readonly IntPtr[] _receivers,_owners;
        private readonly object _key=new();private OwnedReceipt? _receipt;
        internal OwnedSourceIssuer(InfoNative read,Extension initial)
        {_read=read;_before=initial.Sources.ToArray();_receivers=initial.Receivers.ToArray();_owners=initial.NativeOwners.ToArray();}
        internal OwnedReceipt? TryIssue(ulong expectedGeneration,Func<bool> exactLedgers)
        {
            var now=ReadInfo(_read);
            if(expectedGeneration!=_before[0].Generation||now.Closed!=0||now.StopCalls!=1||now.StopOriginalCalls!=1||!exactLedgers())return null;
            for(var index=0;index<5;++index)
            {
                var previous=_before[index];var actual=now.Sources[index];
                if(previous.Generation==0||previous.Role!=index||previous.State!=0||previous.References!=2||previous.ProducerReferences!=1
                    ||actual.Generation!=previous.Generation||actual.Object!=previous.Object||actual.Role!=previous.Role
                    ||actual.State!=2||actual.References!=1||actual.ProducerReferences!=0||actual.OpenScopes!=0
                    ||actual.CachedOwner!=IntPtr.Zero||actual.WithdrawAttempts!=1||actual.RootReleaseAttempts!=0
                    ||actual.Withdrawn<=previous.Generation||actual.Dead<=actual.Withdrawn)return null;
            }
            if(!now.Receivers.SequenceEqual(_receivers)||!now.NativeOwners.SequenceEqual(_owners)||!exactLedgers())return null;
            _receipt??=new(this,_key,expectedGeneration,now.EventSequence);return _receipt;
        }
        internal bool Owns(OwnedReceipt receipt,object key)=>ReferenceEquals(key,_key)&&ReferenceEquals(receipt,_receipt);
        internal sealed class OwnedReceipt
        {
            private readonly OwnedSourceIssuer _issuer;private readonly object _key;
            internal ulong Generation{get;}internal ulong DeathSequence{get;}
            internal OwnedReceipt(OwnedSourceIssuer issuer,object key,ulong generation,ulong death)
                =>(_issuer,_key,Generation,DeathSequence)=(issuer,key,generation,death);
            internal bool IsIssued=>_issuer.Owns(this,_key);
        }
    }

    internal static int RunChild(string[] args)
    {
        var mode=args.Length==2?args[1]:"invalid";var checks=0;var faults=new List<string>();
        void Check(bool value,string reason){++checks;if(!value)throw new InvalidOperationException(reason);}
        try
        {
            Check(args.Length==2&&args[0]==ModeArgument&&Modes.Contains(mode),"Choose one owned cancellation integration mode.");
            Check(OperatingSystem.IsWindows()&&IntPtr.Size==8&&GetModuleHandle("UnityPlayer.dll")==IntPtr.Zero&&GetModuleHandle("GameAssembly.dll")==IntPtr.Zero,
                "Use an isolated owned process without game modules.");
            Check(Marshal.SizeOf<Descriptor>()==584&&Marshal.SizeOf<NativeReceipt>()==176&&Marshal.SizeOf<SourceRow>()==72&&Marshal.SizeOf<Extension>()==496,
                "Owned native layout differs.");
            string Env(string name){var value=Environment.GetEnvironmentVariable(name)??"";Check(Path.IsPathFullyQualified(value)&&File.Exists(value),"Exact absolute fixture path required: "+name);return value;}
            var kernelPath=Env("RIZ_ENGINE_CANCELLATION_KERNEL");var fixturePath=Env("RIZ_ENGINE_CANCELLATION_FIXTURE");var operationPath=Env("RIZ_ENGINE_CANCELLATION_OPERATION");
            var files=new[]{kernelPath,fixturePath,operationPath}.ToDictionary(path=>path,Hash);
            foreach(var path in files.Keys)Roots.Add(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read));
            var kernel=NativeLibrary.Load(kernelPath);var fixture=NativeLibrary.Load(fixturePath);Roots.Add(kernel);Roots.Add(fixture);
            var configure=Export<ConfigureNative>(fixture,"RizEngineCancellationManaged_Configure");
            var read=Export<InfoNative>(fixture,"RizEngineCancellationManaged_GetInfo");
            var run=Export<RunNative>(fixture,"RizEngineCancellationManaged_Run");
            var withdraw=Export<TransitionNative>(fixture,"RizEngineCancellationManaged_WithdrawSources");
            var release=Export<TransitionNative>(fixture,"RizEngineCancellationManaged_ReleaseSources");
            var setStop=Export<SetStopNative>(fixture,"RizEngineCancellationManaged_SetStopEntry");
            var descriptor=new Descriptor{Version=1,Size=584,NodeTargets=new IntPtr[4],SchedulerTargets=new IntPtr[6],WaitTargets=new IntPtr[3],
                WaitOriginals=new IntPtr[3],WaitSites=new NativeEngineWaitSite[18],SchedulerSites=new NativeEngineSchedulerSite[2]};
            Check(configure(kernelPath,ref descriptor)==0&&descriptor.Configured==1&&descriptor.Mode==3&&descriptor.Slot==26
                &&descriptor.Image==fixture&&descriptor.Kernel==kernel&&descriptor.Failures==0,"Owned actual Node/M/N/Inline configuration failed.");
            var initial=ReadInfo(read);var issuer=new OwnedSourceIssuer(read,initial);
            Check(initial.Configured==1&&initial.SourceCount==5&&initial.Sources.Select(row=>row.Object).Distinct().Count()==5,"Five distinct actual source objects required.");
            var log=new Logger();var readNode=Export<NodeInfo>(kernel,"RizEngineNodeContext_GetInfo");var readInline=Export<InlineInfo>(kernel,"RizEngineNodeContext_GetInlineInfo");
            var readScheduler=Export<SchedulerInfo>(kernel,"RizEngineSchedulerContext_GetInfo");var readWait=Export<WaitInfo>(kernel,"RizEngineWaitContext_GetInfo");
            var nodeBridges=new NativeEngineNodeContextBridge[4];
            for(uint kind=0;kind<4;++kind){var value=new NativeEngineNodeInformation{Version=1,Size=112};Check(readNode(kind,26,ref value)==0&&value.Attached==0&&value.Unknown==0,"Fresh Node tuple required.");nodeBridges[kind]=NativeEngineNodeContextBridge.AdoptConfigured(kernelPath,value.Identity,log);}
            var side=new NativeEngineInlineInformation{Version=1,Size=160};Check(readInline(26,ref side)==0&&side.Attached==0&&side.Unknown==0,"Fresh Inline family required.");
            var inlineBridge=NativeEngineInlineContextBridge.AdoptConfigured(kernelPath,new(26,side.Epoch,descriptor.MainThread,side.Entry,nodeBridges[0].Identity,
                initial.InlineSites[0],initial.InlineSites[1],initial.InlineSites[2]),log);
            var schedulerBridges=new NativeEngineSchedulerContextBridge[6];
            for(uint kind=0;kind<6;++kind){var value=new NativeEngineSchedulerInformation{Version=1,Size=144};Check(readScheduler(kind,26,ref value)==0&&value.Attached==0&&value.Unknown==0,"Fresh M tuple required.");
                schedulerBridges[kind]=NativeEngineSchedulerContextBridge.AdoptConfigured(kernelPath,new((NativeEngineSchedulerKind)kind,26,value.Epoch,descriptor.MainThread,value.Target,value.Original,value.Entry,value.RowEntry,
                    kind==4?descriptor.SchedulerSites[0]:default,kind==4?descriptor.SchedulerSites[1]:default),log);}
            var wi=new NativeEngineWaitInformation{Version=1,Size=176};Check(readWait(26,ref wi)==0&&wi.Attached==0&&wi.Unknown==0,"Fresh N tuple required.");
            var waitBridge=NativeEngineWaitContextBridge.AdoptConfigured(kernelPath,new(26,wi.Epoch,descriptor.MainThread,
                descriptor.WaitTargets.Concat(descriptor.NodeTargets).Concat(descriptor.SchedulerTargets).ToArray(),descriptor.WaitOriginals,
                new[]{wi.YieldEntry,wi.DispatchEntry,wi.BatchEntry},descriptor.PhaseEntry,descriptor.WaitSites),log);
            var runtime=NativeEngineRuntimeCoordinator.ShareControlled(new object(),nodeBridges,log,inlineBridge,schedulerBridges,waitBridge);
            using var main=new MainThreadDispatcher();var provider=new Provider(initial.Receivers[0].ToInt64(),descriptor.Enumerator.ToInt64());
            var description=provider.Description;var nodes=new NativeEngineCoroutineNodeLedger(main);
            var identities=new NativeEngineCoroutineIdentityLedger(main,nodes,new[]{description},new[]{new NativeEngineCoroutineOwnerPair(provider.Receiver,descriptor.Owner.ToInt64())});
            var requests=new NativeCoroutineCancellationLedger(main,new[]{description});var inline=new NativeEngineCoroutineInlineLedger(main,identities,nodes);
            var waitDispatcher=runtime.RequireWaitObservation();var schedulerDispatcher=runtime.RequireSchedulerObservation();
            var waits=new NativeEngineWaitLedger(main,identities,nodes,waitDispatcher);
            var callers=new NativeEngineSchedulerCallerProfile(descriptor.CallbackCaller,descriptor.CallbackCaller,descriptor.CallbackCaller,descriptor.CleanupCaller,descriptor.CleanupCaller,descriptor.BatchCleanupCaller);
            var rows=new NativeEngineSchedulerRowLedger(main,identities,nodes,schedulerDispatcher,descriptor.SchedulerTargets[5],descriptor.NodeTargets[2],callers,waits.TryClaimCallback);
            var sideEvents=new List<uint>();var nodeEvents=new List<uint>();var operationContexts=new Dictionary<nuint,NativeCoroutineCancellationLedger.RequestContext>();
            var earlyRejected=0;var stopCompletes=0;var stopDisposes=0;var stopCaptures=0;
            using var waitBinding=waitDispatcher.Bind(observation=>new Scope<NativeEngineWaitLedger.Context>(waits.Capture(observation)),
                (context,observation)=>waits.Complete(context.Value,observation),(context,observation)=>waits.ObservePhase(context.Value,observation),
                (frame,reason)=>{faults.Add(reason);waits.ObserveFault(frame,reason);});
            using var schedulerBinding=schedulerDispatcher.Bind(observation=>new Scope<NativeEngineSchedulerRowLedger.Context>(rows.Capture(observation)),
                (context,observation)=>rows.Complete(context.Value,observation),(context,observation)=>rows.ObserveRow(context.Value,observation),
                (frame,reason)=>{faults.Add(reason);rows.ObserveFault(frame,reason);});
            using var nodeBinding=runtime.BindWithInline(frame=>{
                nodeEvents.Add((uint)frame.Kind);var call=identities.Capture(frame);
                if(call.NodeCall is{} physical)waits.BindNodeCall(physical);
                if(frame.Kind==NativeEngineNodeKind.Detach){Check(requests.ObserveEngineDetach(call),"Actual Stop did not contain exact Detach: "+requests.Read().Failure);
                    Check(!requests.ReadCompletionCandidates().Single().TryReadTerminal(requests,out _),"An open actual Detach scope cannot be terminal.");++earlyRejected;}
                var cleanup=frame.Kind==NativeEngineNodeKind.Release&&call.NodeCall is{} n?waits.CaptureCleanup(n):null;
                var rowCleanup=cleanup is null&&frame.Kind==NativeEngineNodeKind.Release&&call.NodeCall is{} r?rows.CaptureCleanup(r):null;
                return new NodeScope(call,cleanup,rowCleanup);
            },(context,frame,completion)=>{identities.Complete(context.Identity,frame,completion);context.RowCleanup?.Complete();context.WaitCleanup?.Complete();},
                (frame,reason)=>{faults.Add(reason);identities.ObserveFault(frame,reason);},
                receipt=>inline.Capture(receipt),(context,receipt)=>{sideEvents.Add((uint)receipt.Frame.Kind);inline.Observe(context,receipt);},
                (frame,reason)=>{faults.Add(reason);inline.ObserveFault(frame,reason);});
            var operation=new NativeOperationContextBridge(operationPath,NativeOperationKind.Reference1,log);
            operation.Configure(initial.StopOriginal,frame=>{
                ++stopCaptures;var request=requests.Capture(NativeCoroutineCancellationOperation.StopEnumerator,frame)
                    ??throw new InvalidOperationException("Actual owned Stop receiver was not captured.");
                var token=checked((nuint)stopCaptures);operationContexts.Add(token,request);return token;
            },(frame,token,flags)=>{
                if(!operationContexts.TryGetValue(token,out var request))throw new InvalidOperationException("Unknown actual Stop callback token.");
                requests.Complete(request,frame,flags);++stopCompletes;
                Check(!requests.ReadCompletionCandidates().Single().TryReadTerminal(requests,out _),"Stop return before Start/Inline completion cannot be terminal.");
                request.Dispose();++stopDisposes;operationContexts.Remove(token);++earlyRejected;
            });
            Check(setStop(operation.EntryAddress)==0,"Owned Stop original must enter the real operation helper.");
            bool Terminal()=>NativeEngineCancellationTerminalVerifier.Verify(requests,requests.ReadCompletionCandidates(),runtime,identities,inline,rows,waits);
            Check(!Terminal(),"No request cannot manufacture a terminal proof.");
            ProbeNative probe=(stage,node,wait,user)=>{if(stage==1){provider.Started=true;identities.RefreshTrackedIdentities();}};
            Roots.AddRange(new object[]{runtime,requests,nodes,identities,inline,rows,waits,nodeBinding,schedulerBinding,waitBinding,operation,probe,issuer,operationContexts});
            var native=new NativeReceipt{Version=1,Size=176};
            Check(run(probe,IntPtr.Zero,ref native)==0&&native.Failures==0&&native.Detaches==1&&native.Starts==1&&native.Callbacks==1
                &&native.NodesFreed==1&&native.WaitsFreed==1&&native.LiveNodes==0&&native.LiveWaits==0,"Actual owned L/M/N/Stop execution failed.");
            Check(faults.Count==0&&!identities.Unknown&&!nodes.Unknown&&!inline.Unknown&&!rows.Unknown&&!waits.Unknown&&!requests.Unknown,
                "A real callback ledger became Unknown: "+string.Join(";",faults)+"; "+requests.Read().Failure+"; "+waits.Read().Failure);
            Check(sideEvents.SequenceEqual(new[]{1u,2u,4u})&&inline.HasCompletedStart(identities.Read().Identities.Single())
                &&stopCaptures==1&&stopCompletes==1&&stopDisposes==1&&operationContexts.Count==0&&earlyRejected==2,
                "Actual Begin/EndFree/StartExited and Stop Complete/Dispose evidence is incomplete.");
            Check(operation.ReadActivity() is{Known:true,InFlight:0}&&Terminal(),"Actual exact ledger terminal proof did not converge after full scopes disposed.");
            Check(issuer.TryIssue(initial.Sources[0].Generation,Terminal) is null,"Five live source objects cannot issue an owned terminal receipt.");
            OwnedSourceIssuer.OwnedReceipt? receipt=null;
            if(mode!="missing-source"){
                Check(withdraw(initial.Sources[0].Generation)==0,"Actual native producer withdrawal/death failed.");
                var requested=mode=="wrong-generation"?initial.Sources[0].Generation+1:initial.Sources[0].Generation;
                receipt=issuer.TryIssue(requested,Terminal);
            }
            Check((receipt is{IsIssued:true})==(mode=="normal"),"Owned issuer accepted a missing source or another generation.");
            var withdrawn=ReadInfo(read);
            Check(requests.Read() is{EngineCancelled:false,Pending:true},"Owned receipts cannot write production cancellation authority.");
            if(receipt is not null){Check(release(receipt.Generation)==0,"Actual independent source root release failed.");Check(release(receipt.Generation)!=0,"Source roots cannot be freed twice.");}
            var final=ReadInfo(read);
            Check(files.All(file=>Hash(file.Key)==file.Value),"Owned inputs changed during execution.");
            Console.WriteLine(JsonSerializer.Serialize(new{schemaVersion=1,kind="owned-engine-cancellation-managed-integration",passed=true,pid=Environment.ProcessId,mode,checks,
                nativeChecks=native.Checks,nativeFailures=native.Failures,actualLmnStopVerified=true,actualInlineMachineBeginEnd=true,
                actualStopScopeVerified=true,exactLedgerTerminalVerified=true,ownedSourceClosureVerified=receipt is not null,
                ownedCancellationTerminalVerified=receipt is{IsIssued:true},productionEngineCancelled=false,productionAdmissionGranted=false,
                gameExecuted=false,installed=false,stopCaptures,stopCompletes,stopDisposes,sideEvents,nodeEvents,
                nativeStarts=native.Starts,nativeDetaches=native.Detaches,nativeCallbacks=native.Callbacks,nativeNodesFreed=native.NodesFreed,
                nativeWaitsFreed=native.WaitsFreed,rootsReleased=final.Closed==1,sourceCount=5,
                sourceBefore=initial.Sources.Select(SourceJson),sourceAfter=withdrawn.Sources.Select(SourceJson),sourceFinal=final.Sources.Select(SourceJson),
                candidates=requests.Read().DetachCandidates,files=files.Select(file=>new{path=file.Key,sha256=file.Value}),faults}));return 0;
        }
        catch(Exception error){Console.WriteLine(JsonSerializer.Serialize(new{schemaVersion=1,kind="owned-engine-cancellation-managed-integration",passed=false,pid=Environment.ProcessId,mode,checks,error=error.ToString(),faults,productionEngineCancelled=false,productionAdmissionGranted=false,gameExecuted=false,installed=false}));return 1;}
    }
    private static object SourceJson(SourceRow value)=>new{value.Generation,@object=value.Object.ToInt64(),cachedOwner=value.CachedOwner.ToInt64(),value.Role,value.State,
        value.References,value.ProducerReferences,value.OpenScopes,value.WithdrawAttempts,value.RootReleaseAttempts,value.Withdrawn,value.Dead};
    private static Extension ReadInfo(InfoNative read)
    {
        var value=new Extension{Version=1,Size=496,Receivers=new IntPtr[2],NativeOwners=new IntPtr[2],InlineSites=new NativeEngineInlineSite[3],Sources=new SourceRow[5]};
        if(read(ref value)!=0)throw new InvalidOperationException("Actual native source receipt is unavailable.");return value;
    }
    private static T Export<T>(IntPtr module,string name)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    [DllImport("kernel32.dll",EntryPoint="GetModuleHandleW",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string name);
}
