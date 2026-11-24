using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

return StockProviderChecks.Run(args);

internal static partial class StockProviderChecks
{
    private static int _checks;
    private static void Check(bool value, string reason) { ++_checks; if (!value) throw new InvalidOperationException(reason); }
    private static T Need<T>(Result<T> value) { Check(value.Success && value.Value is not null, value.Error?.Message ?? "Missing SDK value."); return value.Value!; }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static void Reject(Action action, string reason)
    { ++_checks; try { action(); } catch (Exception error) when (error is InvalidOperationException or UnsupportedSignatureException or ObjectDisposedException) { return; } throw new Exception(reason); }
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind,IntPtr target,IntPtr self,ulong bits,IntPtr method,out uint code);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetTime(IntPtr self,IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint WireVersion();
    private sealed class Logger : ILogger { public void Log(LogLevel level,string message,Exception? error=null){} }
    private sealed class Plugin : IRizPlugin
    {
        internal IPluginContext Context = null!;
        internal Plugin(string id) => Manifest = new(id,new(1,0),new(1,0));
        public PluginManifest Manifest { get; }
        public void Enable(IPluginContext context) => Context = context;
        public void Disable() { }
    }
    internal static int Run(string[] args)
    {
        var mode=args.Length>0?args[0]:"invalid";
        try
        {
            Check(args.Length==5,"Expected mode stock-fixture Clock-helper source-helper Dobby.");
            var currentSourceHelper = NativeLibrary.Load(Path.GetFullPath(args[3]));
            var sourceProtocolVersion = Export<WireVersion>(currentSourceHelper, "RtPlaybackSourceVersion")();
            var frameSize = Marshal.SizeOf<NativePlaybackSourceFrame>();
            var informationSize = Marshal.SizeOf<NativePlaybackSourceInformation>();
            Check(sourceProtocolVersion == 4 && frameSize == 168 && informationSize == 136,
                "Current independent Source V4 managed/native wire differs.");
            var native=NativeLibrary.Load(Path.GetFullPath(args[1]));
            using var api=new FakeNative();api.ConfigureRootedStock(native);
            api.SetRootedGraphValue("progress.now",2.8);api.SetRootedGraphValue("info.nowTime",5.6f);
            using var main=new MainThreadDispatcher();var log=new Logger();
            var state=new GameEventState();state.Scene("Level");state.SetAvailability(scenes:true,runs:true,pause:true);
            var started=state.Controller(api.GameplayInformation.ToInt64(),true,false,"stock-provider-chart",api.GameplayController.ToInt64())!;
            state.ObservePause(true);
            var targets=PlaybackSourceGroupTargets.ResolveOwnedFixture(native);
            var binding=new NativePlaybackBinding(started.RunId,state.SceneGeneration,1,1,api.GameplayController.ToInt64(),api.GameplayInformation.ToInt64(),
                api.GameplayProgress.ToInt64(),api.GameplayPlayer.ToInt64(),api.GameplaySource.ToInt64(),api.GameplayClip.ToInt64(),targets.GetTime.MethodInfo.ToInt64());
            var origin=new NativePlaybackRateOrigin(binding.RunId,1,binding.Controller,binding.Information,binding.Progress,api.GameplayPause.ToInt64());
            var run=state.CapturePlaybackRun(binding.Information,binding.Controller);
            using var router=new NativePlaybackStockClockRouter(api.GameplayApi,binding,origin,run);
            var broker=new ExactHookBroker(Path.GetFullPath(args[4]),main,log);
            NativePlaybackStockClockRouter.Session? session=null;NativePlaybackClock? finalClock=null;
            var nativeReaderClockMatches=true;var nativeReaders=0;var faultThread=0;
            var physicalFault=new ManualResetEventSlim();var deliveredFaults=new System.Collections.Concurrent.ConcurrentBag<object>();Action? duringReturn=null;
            var originalBits=new List<uint>();
            NativePlaybackStockRateProvider? actualProvider=null;
            IntPtr expectedPreStore=api.GameplayPlayer;
            var playerBefore=new List<NativePlaybackSourceObservation>();
            var playerReturns=new List<NativePlaybackSourceCompletion>();
            ulong previousSourceFence=0;
            var preStoreRevocations=0;
            Action? verifyInvalidatedBeforeStore=null;
            var priorField=api.GameplayPlayer;
            var incomingMethods=new List<IntPtr>();
            if(mode=="pre-body-tamper") { Mutate(targets.ProgressMusicPlayer.Address,0x90); }
            if(mode=="pre-tail-tamper") { Mutate(IntPtr.Add(targets.ProgressMusicPlayer.Address,9),0x90); }

            var group=broker.BindPlaybackSourceGroup(targets,Path.GetFullPath(args[2]),Hash(args[2]),Path.GetFullPath(args[3]),Hash(args[3]),
                ()=>{nativeReaders++;nativeReaderClockMatches &= finalClock is not null && ReferenceEquals(finalClock,session!.Clock);return router.ReadClock();},
                fault=>{deliveredFaults.Add(fault);faultThread=Environment.CurrentManagedThreadId;router.OnGetTimeFault(fault);physicalFault.Set();},
                fault=>{deliveredFaults.Add(fault);faultThread=Environment.CurrentManagedThreadId;router.OnSourceFault(fault);physicalFault.Set();},
                observation=>{
                    if(observation.Role==NativePlaybackSourceRole.ProgressMusicPlayer) {
                        Check(Marshal.ReadIntPtr(api.GameplayProgress,0x38)==expectedPreStore,"Progress writer observer ran after the field store.");
                        Check(observation.SourceSequence==previousSourceFence+1,"Progress writer Before did not advance the native source fence unconditionally.");
                        previousSourceFence=observation.SourceSequence; playerBefore.Add(observation); incomingMethods.Add(observation.IncomingMethod);
                    }
                    router.ObserveBefore(observation);
                    if(observation.Role==NativePlaybackSourceRole.ProgressMusicPlayer) {
                        Check(finalClock is not null&&finalClock.Capture().Phase==NativePlaybackPhase.Withdrawn,"Old final Clock policy was not revoked before the actual field store.");
                        Check(actualProvider is not null&&!actualProvider.Probe().Supported,"Formal provider remained supported in the pre-store window.");
                        preStoreRevocations++;
                        verifyInvalidatedBeforeStore?.Invoke();
                    }
                },completion=>{router.ObserveReturned(completion);if(completion.Observation.Role==NativePlaybackSourceRole.ProgressMusicPlayer)playerReturns.Add(completion);},getTimeAfter:(frame,completion)=>{originalBits.Add(completion.OriginalBits);duringReturn?.Invoke();});
            router.Attach(group);
            var playerLease=group.SourceLease(NativePlaybackSourceRole.ProgressMusicPlayer);
            Check(playerLease.Kind==NativePlaybackSourceKind.Reference1&&playerLease.RelocatedByteCount==8&&playerLease.OriginalBodyLength==13,
                "Progress player did not consume its exact eight-byte leaf relocation and thirteen-byte body.");
            Check(playerLease.TrampolineUnwindAddress!=IntPtr.Zero&&playerLease.GroupEpoch==group.Epoch,"Progress player lacks its private same-epoch empty-unwind owner.");
            previousSourceFence=group.ReadSourceHealth().SourceSequence;
            var invoke=Export<Invoke>(native,"StockFixtureInvoke");var getTime=Marshal.GetDelegateForFunctionPointer<GetTime>(targets.GetTime.Address);
            void Call(NativePlaybackSourceRole role,float argument=0)
            {
                var target=targets.SourceTarget(role);var self=role==NativePlaybackSourceRole.ProgressPlay?api.GameplayProgress:api.GameplayPlayer;
                var kind=role==NativePlaybackSourceRole.Pause?1u:role==NativePlaybackSourceRole.ProgressPlay?5u:2u;
                var bits=role==NativePlaybackSourceRole.ProgressPlay?0ul:unchecked((uint)BitConverter.SingleToInt32Bits(argument));
                Check(invoke(kind,target.Address,self,bits,target.MethodInfo,out var error)==1&&error==0,"Actual stock original failed: "+role);
            }
            Marshal.WriteInt32(api.GameplaySource,48,1);Call(NativePlaybackSourceRole.Pause);
            session=router.PreparePausedClock();Check(session.TryArm(),"Actual dormant native Clock could not arm.");finalClock=session.Clock;
            using var provider=NativePlaybackStockRateProvider.AdoptControlledFixture(session);actualProvider=provider;
            Check(provider.Probe().Supported&&!provider.ProductionAdmission,"Private controlled Session was not adopted.");
            Check(ReferenceEquals(provider.CaptureGetTimeHook(),group.GetTimeLease),"Provider returned a matching lease copy instead of its exact retained group lease.");
            using(var access=provider.Capture(origin))
            {
                Check(api.ActiveRoots==14&&ReferenceEquals(access.Clock,finalClock),"Provider capture did not independently own seven roots and the same final Clock.");
                access.VerifyAuthority();Check(access.Read().Binding==binding,"Provider did not read its actual current source binding.");
                if(mode=="worker")
                {
                    var roots=api.ActiveRoots;
                    Task.Run(()=>{Reject(()=>provider.Capture(origin),"Worker captured owner-thread source.");Reject(provider.Dispose,"Worker disposed provider.");Reject(access.Dispose,"Worker freed independent source roots.");}).GetAwaiter().GetResult();
                    Check(api.ActiveRoots==roots&&provider.Probe().Supported,"Worker cleanup changed roots or provider support.");
                }
            }
            Check(api.ActiveRoots==7,"Preparation Dispose affected observer roots.");
            using(var backend=new GameplayNativeBackend(api.GameplayApi,main,provider))
            {
                var probe=backend.Probe();
                Check(probe.Operations.Single(row=>row.Action==GameplayControlAction.SetSpeed).Supported,"Formal Backend did not adopt concrete stock provider.");
                Check(probe.Sources["clockMapping"] is not null,"Controlled fixture bypassed pinned game converter/body verification.");
                Check(!probe.Operations.Single(row=>row.Action==GameplayControlAction.Seek).Supported,"Stock provider enabled arbitrary Seek.");
            }
            using var journal=new RunModificationJournal(main,log,state);
            var module=new GameplayModule(api.GameplayApi,main,()=>state.LifecycleSnapshot(),journal.Record,state.MatchesCurrentGameplayRun,provider);
            Check(module.Probe().Ready,"Formal GameplayModule failed to construct the actual Backend and Service.");
            var caps=new CapabilityRegistry(new("controlled-stock","fixture","synthetic","offline"));
            var registration=new ModuleRegistration(caps,module.Manifest);module.Register(registration);registration.Complete();
            caps.RegisterScoped<IRunModificationService>(RunModificationCapabilityIds.Read,journal.ForPlugin);
            using var host=new PluginHost(caps,main,new EventHub(log),log);
            var p1=new Plugin("stock-provider-one");var p2=new Plugin("stock-provider-two");
            Check(host.Register(p1).Success&&host.Register(p2).Success&&host.Enable(p1.Manifest.Id).Success&&host.Enable(p2.Manifest.Id).Success,"Formal PluginHost registration failed.");
            var controls=p1.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
            var other=p2.Context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
            var clockView=p1.Context.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!;
            module.ObserveControls(1, 1, requested: true);
            var observed=Need(controls.GetState());
            Check(clockView is not null&&observed.ObservedRunId==binding.RunId&&observed.Operations.Single(row=>row.Action==GameplayControlAction.SetSpeed).Supported,
                "SDK controls did not expose the actual shared observed run/provider.");
            var lease=Need(controls.Acquire(true));
            Check(!other.Acquire(true).Success,"Independent plugin bypassed exclusive SDK control ownership.");
            var request=new GameplayControlRequest(GameplayControlAction.SetSpeed,lease.ControllerGeneration,lease.ObservedRunId,2,GameplayControlValueUnit.PlaybackRateMultiplier);
            Check(request.ControllerGeneration==binding.ControllerGeneration,"Fixture Service generation differs from private stock association.");
            if(mode.StartsWith("player-",StringComparison.Ordinal))
            {
                var successful=Need(lease.Execute(request));
                Check(successful.Modification.TransactionState==GameplayControlTransactionState.Confirmed&&finalClock.Capture().Rate==2
                    &&BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource,16))==2,"Formal SDK did not first perform a real native/Clock speed transaction.");
                using var oldSource=provider.Capture(origin);
                using var oldWriter=oldSource.PreparePitchWrite();
                using var prepared=NativePlaybackRateParticipants.Prepare(request with{Value=1.5},finalClock,()=>provider.Capture(origin),()=>run.VerifyCurrent(binding));
                var participants=(IGameplayValueParticipant[])typeof(GameplayPreparedValueControl).GetField("_participants",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(prepared)!;
                Check(participants.Length==2,"Formal native/Clock preparation did not create its actual two participants.");
                foreach(var participant in participants) participant.VerifyPrepared();
                var beforePreparedRejects=0;
                verifyInvalidatedBeforeStore=()=>{
                    Check(!finalClock.TryGetTime(binding,100,out _),"Clock.TryGetTime still authorized old publication before store.");
                    Reject(oldSource.VerifyAuthority,"Old source was authorized inside the pre-store callback.");
                    Reject(oldWriter.VerifyCurrent,"Old writer was authorized inside the pre-store callback.");
                    foreach(var participant in participants) Reject(participant.VerifyPrepared,"An actual prepared participant remained current before store.");
                    if(beforePreparedRejects++==0) {
                        Reject(prepared.Commit,"Prepared commit wrote after the source fence advanced but before store.");
                        foreach(var participant in participants) Reject(participant.Restore,"An actual participant restored using superseded source authority before store.");
                    }
                };

                var oldClock=finalClock.Capture(); var sourceGeneration=binding.SourceGeneration;
                var beforeFence=group.ReadSourceHealth().SourceSequence;
                previousSourceFence=beforeFence;
                var target=targets.ProgressMusicPlayer;
                var read=Export<PlayerRead>(native,"StockProgressWriterRead");
                var startCalls=read(0);
                void WritePlayer(IntPtr value,IntPtr incoming,bool runtime=false,bool fail=false) {
                    expectedPreStore=Marshal.ReadIntPtr(api.GameplayProgress,0x38);
                    if(runtime) api.GameplayApi.Call(api.GameplayProgress,"set_MusicPlayer",NativeRuntime.Arg.Reference(value,api.GameplayApi.Root(value)));
                    else Check(invoke(4,target.Address,api.GameplayProgress,unchecked((ulong)value.ToInt64()),incoming,out var code)==(fail?2:1)
                        &&code==(fail?0xe2400021u:0u),"Actual independent native Progress writer normal/SEH result differs.");
                }
                if(mode=="player-same-runtime") WritePlayer(api.GameplayPlayer,target.MethodInfo,runtime:true);
                else if(mode=="player-aba") {
                    var alternate=Export<CreatePlayer>(native,"StockFixtureCreatePlayer")(api.GameplaySource);
                    WritePlayer(alternate,target.MethodInfo);WritePlayer(api.GameplayPlayer,IntPtr.Zero);
                }
                else if(mode=="player-null") WritePlayer(IntPtr.Zero,IntPtr.Zero);
                else if(mode=="player-throw") {
                    Export<PlayerMode>(native,"StockProgressWriterMode")(1);WritePlayer(api.GameplayPlayer,target.MethodInfo,fail:true);
                    Export<PlayerMode>(native,"StockProgressWriterMode")(0);
                }
                else if(mode=="player-same-native") WritePlayer(api.GameplayPlayer,IntPtr.Zero);
                else throw new InvalidOperationException("Unknown player mode.");
                Check(playerBefore.Count==(mode=="player-aba"?2:1)&&preStoreRevocations==playerBefore.Count,"A Progress attempt did not enter and revoke exactly once before store.");
                Check(read(0)==startCalls+(ulong)playerBefore.Count&&read(1)==unchecked((ulong)(api.GameplayProgress.ToInt64()+0x38)),"The actual Progress original/tail did not run once per attempt with the exact field pointer.");
                Check(read(2)==unchecked((ulong)Marshal.ReadIntPtr(api.GameplayProgress,0x38).ToInt64())
                    &&read(3)==unchecked((ulong)incomingMethods.Last().ToInt64()),"Original value or incoming MethodInfo was not forwarded unchanged.");
                Check(playerReturns.Count==playerBefore.Count&&playerReturns.All(row=>row.Returned==(mode!="player-throw")&&row.Abnormal==(mode=="player-throw")),"Returned/Abnormal facts were manufactured from readback.");
                foreach(var row in playerBefore) {
                    Check(row.TryReadCompletion(out var physical)&&physical is{PhysicalExited:true},"A Progress attempt lost its actual native finally/physical exit.");
                }
                Reject(oldSource.VerifyAuthority,"Old independently rooted rate source survived Progress writer.");
                Reject(oldWriter.VerifyCurrent,"Old prepared native writer survived Progress writer.");
                Check(!provider.Probe().Supported&&finalClock.Capture().Phase==NativePlaybackPhase.Withdrawn
                    &&finalClock.Capture().Binding.SourceGeneration==sourceGeneration,"Progress writer revived publication or manufactured another source generation.");
                Check(!lease.Execute(request).Success,"Formal SDK used stale source/prepared policy after Progress writer.");
                Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3,"Withdrawn source policy still replaced actual GetTime original.");
                Check(broker.ReadActualPlaybackDobbyCallCounts()==(12,12),"Progress writer was not retained in the concrete twelve-member private group.");
                Console.WriteLine(JsonSerializer.Serialize(new{kind="actual-progress-player-source-writer",mode,passed=true,checks=_checks,
                    actualPrepare=12,actualCommit=12,actualBefore=playerBefore.Count,actualOriginalCalls=read(0)-startCalls,
                    actualReturned=playerReturns.Count(row=>row.Returned),actualAbnormal=playerReturns.Count(row=>row.Abnormal),
                    preStoreRevocations,beforePreparedRejects,actualPreparedParticipantCount=participants.Length,
                    preStoreClockTryGetTimeRejected=true,preStorePreparedVerifyRejected=true,preStorePreparedCommitRejected=true,preStorePreparedRestoreRejected=true,
                    sourceFenceBefore=beforeFence,sourceFenceAfter=previousSourceFence,
                    sourceHealth=mode=="player-throw"?null:group.ReadSourceHealth(),
                    leaf=new{target=target.Address.ToInt64(),entry=playerLease.EntryAddress.ToInt64(),original=playerLease.OriginalAddress.ToInt64(),
                        dobbyOriginal=playerLease.DobbyOriginalAddress.ToInt64(),unwind=playerLease.TrampolineUnwindAddress.ToInt64(),playerLease.RelocatedByteCount,playerLease.OriginalBodyLength},
                    attempts=playerBefore.Select(row=>new{role=(uint)row.Role,self=row.Self.ToInt64(),argument=row.ArgBits,incomingMethod=row.IncomingMethod.ToInt64(),
                        caller=row.Caller.ToInt64(),row.SourceSequence,row.WriterSequence,row.SchedulingSequence,row.OwnerEpoch,row.ThreadId,row.Sequence}).ToArray(),
                    sameOrRestoredField=Marshal.ReadIntPtr(api.GameplayProgress,0x38)==priorField,
                    formalSdkPositiveConfirmed=true,oldClockWithdrawn=true,oldSourceRejected=true,oldPreparedWriterRejected=true,
                    sourceGenerationUnchanged=finalClock.Capture().Binding.SourceGeneration==sourceGeneration,
                    sourceProtocolVersion,frameSize,informationSize,leafRelocatedBytes=8,leafBodyBytes=13,
                    productionAdmission=false,completeSourceGeneration=false,completeLowerWriterCoverage=false,gameExecuted=false,installed=false}));
                return 0;
            }
            if(mode=="worker")
            {
                var roots=api.ActiveRoots;
                Task.Run(()=>Reject(module.Release,"Worker released the owner-thread GameplayModule.")).GetAwaiter().GetResult();
                Check(module.Tick().Ready&&api.ActiveRoots==roots&&provider.Probe().Supported&&controls.GetState().Success,
                    "Rejected worker Module.Release lost the live Service or its roots before the owner could retry.");
            }
            if(mode=="wrong-origin")
            {
                foreach(var wrong in new[]{origin with{RunId="foreign"},origin with{ControllerGeneration=2},origin with{Pause=origin.Pause+8},origin with{Controller=origin.Controller+8}})
                    Reject(()=>provider.Capture(wrong),"A matching scalar graph with the wrong run/origin captured stock authority.");
                Check(!lease.Execute(request with{ObservedRunId="foreign"}).Success&&provider.Probe().Supported,"Wrong SDK run mutated or withdrew the legitimate provider.");
            }
            else if(mode=="run-loss")
            {
                state.ControllerUnavailable();Check(!provider.Probe().Supported&&finalClock.Capture().Phase==NativePlaybackPhase.Withdrawn,"Host-run loss did not withdraw concrete provider/Clock.");
                Check(!lease.Execute(request).Success&&getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3,"Lost host run still executed speed or replaced native original.");
            }
            else if(mode=="external-equal")
            {
                api.GameplayApi.Call(api.GameplaySource,"set_pitch",NativeRuntime.Arg.Value(1f));
                Check(!provider.Probe().Supported&&finalClock.Capture().Phase==NativePlaybackPhase.Withdrawn,"Naked equal native writer left provider/Clock supported.");
                Check(!lease.Execute(request).Success&&getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3,"External writer allowed stale SDK transaction/native authority.");
            }
            else if(mode=="physical-fault")
            {
                duringReturn=()=>throw new InvalidOperationException("Actual completion observer fault for owner-thread withdrawal.");
                Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3,"Faulting actual native completion replaced original bits.");duringReturn=null;
                Check(physicalFault.Wait(TimeSpan.FromSeconds(10))&&faultThread!=Environment.CurrentManagedThreadId&&router.PhysicalFaultPending,"Actual physical fault was not queued from its broker worker.");
                Check(finalClock.Capture().Phase!=NativePlaybackPhase.Withdrawn&&router.FirstPhysicalFault is null,
                    "Physical fault callback performed logical Clock withdrawal before the owner-thread pump.");
                Check(module.Tick().Ready,"Formal GameplayModule tick did not consume the queued physical fault.");
                Check(!router.PhysicalFaultPending&&!provider.Probe().Supported&&finalClock.Capture().Phase==NativePlaybackPhase.Withdrawn,
                    "Owner Module.Tick ignored the queued permanent physical fault.");
                Check(deliveredFaults.Any(fault=>ReferenceEquals(router.FirstPhysicalFault,fault)),"Owner discarded/recreated the exact delivered physical fault.");
                Check(!lease.Execute(request).Success,"SDK executed after permanent physical fault.");
            }
            else if(mode=="release-prepared")
            {
                using var access=provider.Capture(origin);Check(api.ActiveRoots>=14,"Prepared access has no independent roots.");
                module.Release();Check(!provider.Probe().Supported&&finalClock.Capture().Phase==NativePlaybackPhase.Withdrawn,"Module Release did not withdraw its logical provider.");
                Reject(access.VerifyAuthority,"Prepared access survived module release.");
                Check(api.ActiveRoots==7,"Module release freed preparation roots or retained drained observer roots.");
            }
            else
            {
                var preparationReads=0;api.DuringRootedPitchRead=()=>{preparationReads++;Check(ReferenceEquals(finalClock,session.Clock),"Preparation/confirmation used another final Clock.");};
                var receipt=Need(lease.Execute(request));api.DuringRootedPitchRead=null;
                Check(preparationReads>0&&receipt.Modification.TransactionState==GameplayControlTransactionState.Confirmed&&receipt.Modification.ConfirmedValue==2,
                    "Formal SDK speed transaction did not read/confirm the actual native/Clock participants.");
                Check(finalClock.Capture().Rate==2&&BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource,16))==2,"Formal module transaction did not commit actual native pitch and unique Clock.");
                var audit=Need(p1.Context.GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service!.Read());
                Check(audit.Records.Count==1&&audit.Records[0].Owner==p1.Manifest.Id&&audit.Records[0].RunId==binding.RunId&&audit.Records[0].Outcome==RunModificationOutcome.Applied,
                    "Actual shared lifecycle journal did not retain the SDK control audit.");
                Check(!audit.Records[0].LocalResultBehaviorVerified&&!audit.Records[0].SubmissionBehaviorVerified,"Controlled SDK audit claimed result verification.");
                Check(host.Disable(p1.Manifest.Id).Success&&!lease.Execute(request).Success,"Stopped plugin retained live SDK control authority.");
                var successor=Need(other.Acquire(true));lease.Dispose();
                var second=Need(successor.Execute(request with{ControllerGeneration=successor.ControllerGeneration,ObservedRunId=successor.ObservedRunId,Value=1.5}));
                Check(second.Modification.TransactionState==GameplayControlTransactionState.Confirmed&&finalClock.Capture().Rate==1.5
                    &&BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource,16))==1.5f,
                    "Second plugin scope did not acquire released exclusivity or lost its actual native/Clock transaction to stale first-scope cleanup.");
                var secondAudit=Need(p2.Context.GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service!.Read());
                Check(secondAudit.Records.Count==2&&secondAudit.Records[0].Owner==p1.Manifest.Id&&secondAudit.Records[1].Owner==p2.Manifest.Id,
                    "Independent plugin scopes did not retain their own actual shared-journal ownership.");
                Call(NativePlaybackSourceRole.Replay,.125f);
                Check(finalClock.Capture() is{Phase:NativePlaybackPhase.Scheduled,DspAnchorSeconds:100.125,Rate:1.5},"Actual stock Replay did not restore selected rate and publish its leaf double.");
                Check(Math.Abs(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)+.125f)<.00001f,"Native GetTime lost unscaled pre-roll.");
                api.SetRootedDsp(100.5);Check(Math.Abs(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)-.5625f)<.00001f,"Actual GetTime did not return the second plugin's module-selected final Clock rate.");
                Check(group.GetTimeLease.ReadHealth().OriginalCalls==(ulong)originalBits.Count&&originalBits.Count==2
                    &&BitConverter.Int32BitsToSingle(unchecked((int)originalBits[0]))==0,
                    "Actual stock original call/return accounting or separately retained pre-roll bits differ.");
                Call(NativePlaybackSourceRole.Pause);Call(NativePlaybackSourceRole.Resume,.25f);
                Check(finalClock.Capture().DspAnchorSeconds==100.75&&finalClock.Capture().Rate==1.5,"Actual stock Resume did not consume its exact leaf double/reset-restore.");
                api.SetRootedDsp(101);Check(Math.Abs(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)-.375f)<.00001f,"Actual Resume final native GetTime lost selected rate.");
                Check(nativeReaderClockMatches&&nativeReaders>=3,"Actual native reads routed through another Clock instance.");
            }
            module.Release();router.PumpCompleted();
            Check(!provider.Probe().Supported,"Released module continued to advertise speed.");
            var retained=api.ActiveRoots;
            if(mode!="physical-fault")Check(router.RootsReleased&&retained==0,"Module/provider cleanup leaked drained native/source/preparation roots.");
            else Check(!router.RootsReleased&&retained>=7,"Physical Unknown was treated as proof that native root responsibility drained.");
            Check(broker.ReadActualPlaybackDobbyCallCounts()==(12,12),"Provider/module consumption changed physical installation identity.");
            Console.WriteLine(JsonSerializer.Serialize(new{kind="stock-concrete-provider-module-sdk-checks",mode,passed=true,checks=_checks,nativeReaders,nativeReaderClockMatches,
                activeRoots=retained,physicalUnknownRetained=mode=="physical-fault",actualPrepare=12,actualCommit=12,metadataSynthetic=true,
                pinnedClockMappingAvailable=false,controlledProvider=true,completeProductionProvider=false,crossRunHandoff=false,productionAdmission=false,gameExecuted=false,installed=false}));
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);Console.WriteLine(JsonSerializer.Serialize(new{kind="stock-concrete-provider-module-sdk-checks",mode,passed=false,checks=_checks,error=error.Message}));return 1;}
    }
}
