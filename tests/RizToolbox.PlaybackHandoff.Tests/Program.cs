using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

return HandoffChecks.Run(args);

internal static class HandoffChecks
{
    private static int _checks;
    private static void Check(bool value,string reason) { ++_checks; if(!value)throw new InvalidOperationException(reason); }
    private static void Reject(Action action,string reason) { ++_checks;try{action();}catch(Exception error)when(error is InvalidOperationException or UnsupportedSignatureException or ObjectDisposedException){return;}throw new Exception(reason); }
    private static T Clone<T>(T value)where T:class => (T)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(value,null)!;
    private static T Private<T>(object owner,string name) => (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private static string Hash(string path) { using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    private static T Export<T>(IntPtr module,string name)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind,IntPtr target,IntPtr self,ulong bits,IntPtr method,out uint code);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetTime(IntPtr self,IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ActionNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint WaitNative();
    [DllImport("kernel32",SetLastError=true)] private static extern bool VirtualProtect(IntPtr address,nuint size,uint protection,out uint old);
    private sealed class Main:IMainThreadDispatcher
    {
        private readonly int _thread=Environment.CurrentManagedThreadId;
        public bool IsMainThread=>_thread==Environment.CurrentManagedThreadId;
        public void VerifyAccess(){if(!IsMainThread)throw new InvalidOperationException("Wrong installation thread.");}
        public Task InvokeAsync(Action action,CancellationToken cancellationToken=default){VerifyAccess();action();return Task.CompletedTask;}
    }
    private sealed class Logger:ILogger { public void Log(LogLevel level,string message,Exception? error=null){} }
    internal static int Run(string[] args)
    {
        var mode=args.Length>0?args[0]:"invalid";
        try
        {
            Check(args.Length==5,"Expected mode fixture gettime-helper source-helper Dobby.");
            var module=NativeLibrary.Load(Path.GetFullPath(args[1]));
            using var api=new FakeNative();api.ConfigureRootedStock(module);
            api.SetRootedGraphValue("progress.now",2.8);api.SetRootedGraphValue("info.nowTime",5.6f);
            var targets=PlaybackSourceGroupTargets.ResolveOwnedFixture(module);
            var state=new GameEventState();state.Scene("Level");state.SetAvailability(scenes:true,runs:true,pause:true);
            var first=state.Controller(api.GameplayInformation.ToInt64(),true,false,"handoff-chart",api.GameplayController.ToInt64())!;state.ObservePause(true);
            NativePlaybackBinding Graph(string id,long generation)=>new(id,state.SceneGeneration,generation,1,api.GameplayController.ToInt64(),api.GameplayInformation.ToInt64(),
                api.GameplayProgress.ToInt64(),api.GameplayPlayer.ToInt64(),api.GameplaySource.ToInt64(),api.GameplayClip.ToInt64(),targets.GetTime.MethodInfo.ToInt64());
            var binding=Graph(first.RunId,1);var origin=new NativePlaybackRateOrigin(binding.RunId,1,binding.Controller,binding.Information,binding.Progress,api.GameplayPause.ToInt64());
            var run=state.CapturePlaybackRun(binding.Information,binding.Controller);
            using var router=new NativePlaybackStockClockRouter(api.GameplayApi,binding,origin,run);
            var broker=new ExactHookBroker(Path.GetFullPath(args[4]),new Main(),new Logger());
            var reads=0;var sourceObservations=0;Action<NativePlaybackSourceObservation>? sourceInjection=null;
            NativePlaybackSourceCompletion? retainedOldCompletion=null;
            var group=broker.BindPlaybackSourceGroup(targets,Path.GetFullPath(args[2]),Hash(args[2]),Path.GetFullPath(args[3]),Hash(args[3]),
                ()=>{++reads;return router.ReadClock();},_=>{},_=>{},
                observation=>{++sourceObservations;router.ObserveBefore(observation);sourceInjection?.Invoke(observation);},
                completion=>{retainedOldCompletion??=completion;router.ObserveReturned(completion);});
            router.Attach(group);
            var physical=(group.Epoch,group.GetTimeLease.EntryAddress,group.GetTimeLease.OriginalAddress,group.SetPitchLease.EntryAddress,group.SetPitchLease.OriginalAddress);
            var invoke=Export<Invoke>(module,"StockFixtureInvoke");var getTime=Marshal.GetDelegateForFunctionPointer<GetTime>(targets.GetTime.Address);
            void Call(NativePlaybackSourceRole role,float argument=0)
            {
                var target=targets.SourceTarget(role);var self=role==NativePlaybackSourceRole.ProgressPlay?api.GameplayProgress:api.GameplayPlayer;
                var kind=role==NativePlaybackSourceRole.Pause?1u:role==NativePlaybackSourceRole.ProgressPlay?5u:2u;
                Check(invoke(kind,target.Address,self,unchecked((uint)BitConverter.SingleToInt32Bits(argument)),target.MethodInfo,out var error)==1&&error==0,"Actual stock call failed: "+role);
            }
            void InitialPause(){Marshal.WriteInt32(api.GameplaySource,48,1);Call(NativePlaybackSourceRole.Pause);}
            InitialPause();var session=router.PreparePausedClock();Check(session.TryArm(),"First final Clock did not arm.");
            void Exercise(NativePlaybackStockClockRouter.Session current,NativePlaybackBinding graph,NativePlaybackRateOrigin rateOrigin,GameEventState.PlaybackRunAssociation association)
            {
                var clock=current.Clock;
                Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3,"Actual native initial media return differs.");
                var request=new GameplayControlRequest(GameplayControlAction.SetSpeed,graph.ControllerGeneration,graph.RunId,2,GameplayControlValueUnit.PlaybackRateMultiplier);
                using(var prepared=NativePlaybackRateParticipants.Prepare(request,clock,()=>current.CaptureRateSource(rateOrigin),()=>association.VerifyCurrent(graph)))
                {prepared.Commit();Check(prepared.Confirm().Observed==true&&clock.Capture().Rate==2,"Actual pitch/Clock participants failed.");}
                Call(NativePlaybackSourceRole.Replay,.125f);
                Check(clock.Capture().Phase==NativePlaybackPhase.Scheduled&&clock.Capture().Rate==2,"Stock Replay failed to restore selected rate: "+router.LastFailure);
                Check(Math.Abs(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)+.125f)<.00001f,"Native scheduled preroll differs.");
                var dsp=clock.Capture().DspAnchorSeconds+.375;api.SetRootedDsp(dsp);
                Check(Math.Abs(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)-.75f)<.00001f,"Native selected-rate media differs.");
                Call(NativePlaybackSourceRole.Pause);Call(NativePlaybackSourceRole.Resume,.25f);
                Check(clock.Capture().Rate==2&&Math.Abs(clock.Capture().DspAnchorSeconds-(dsp+.25))<.00001,"Stock Resume failed.");
            }
            Exercise(session,binding,origin,run);
            if(mode=="sticky-fault")
            {
                var address=IntPtr.Add(targets.GetTime.Address,128);var before=Marshal.ReadByte(address);
                Check(VirtualProtect(address,1,0x40,out var protection),"Fixture code page unavailable.");Marshal.WriteByte(address,(byte)(before^1));
                Reject(group.VerifyCurrent,"Actual body mutation did not revoke the physical group.");
                Marshal.WriteByte(address,before);VirtualProtect(address,1,protection,out _);
                Reject(()=>router.BeginHandoff(session),"Restored bytes revived a sticky physical owner.");
                Check(!group.IsActive&&!group.GetTimeLease.IsActive,"Physical fault did not remain sticky.");
                Reject(()=>group.GetTimeLease.ReadHealth(),"Restored bytes revived the physical GetTime lease.");
            }
            else
            {
                var sessions=mode=="long-sequence"?66:2;var epochs=new List<ulong>();var revisions=new List<long>();var receipts=new List<IntPtr>();
                for(var index=2;index<=sessions;index++)
                {
                    var old=session;var oldClock=old.Clock;var oldState=oldClock.Capture();var oldSource=old.CaptureRateSource(origin);
                    var oldPublication=Private<NativePlaybackClockPublication>(old,"_publication");
                    var oldRooted=Private<NativePlaybackStockRootedSource>(router,"_source");
                    var lateWithdrawal=Private<Action<string>>(oldRooted,"_withdrawn");
                    var lateReturned=Private<Action<NativePlaybackStockRootedSource.ReadbackReceipt>>(oldRooted,"_returned");
                    var lateReceipt=oldRooted.Current!;
                    var oldAuthority=group.GetTimeLease.ReadCurrent().Authority!;
                    NativePlaybackStockClockRouter.Handoff? handoff=null;
                    GameEventState.PlaybackRunAssociation? nextRun=null;NativePlaybackBinding? nextGraph=null;
                    void NewRun()
                    {
                        state.Scene("Menu");state.Scene("Level");state.SetAvailability(scenes:true,runs:true,pause:true);
                        api.NextRootedStockGraph();
                        var started=state.Controller(api.GameplayInformation.ToInt64(),true,false,"handoff-chart-"+index,api.GameplayController.ToInt64());state.ObservePause(true);
                        Check(started is not null,"The host did not observe an independent information/controller graph for the next run.");
                        nextGraph=Graph(started!.RunId,index);nextRun=state.CapturePlaybackRun(nextGraph.Information,nextGraph.Controller);
                    }
                    if(mode=="gettime-busy"&&index==2)
                    {
                        Export<ActionNative>(module,"StockFixtureBlockGetTime")();
                        var blocked=Task.Run(()=>getTime(api.GameplayPlayer,targets.GetTime.MethodInfo));
                        Check(Export<WaitNative>(module,"StockFixtureWaitGetTime")()==0,"Actual native GetTime original did not block.");
                        handoff=router.BeginHandoff(old);NewRun();
                        Check(!handoff.TryAdmit(nextRun!,nextGraph!,api.GameplayPause.ToInt64()),"Actual blocked GetTime was bypassed by a managed-zero handoff.");
                        Check(group.GetTimeLease.ReadHealth() is {Known:true,AuthorityActive:false,InFlight:1}&&api.ActiveRoots>=14,"Old GetTime/root responsibility vanished before actual native finally.");
                        Export<ActionNative>(module,"StockFixtureReleaseGetTime")();Check(blocked.Wait(10000),"Blocked actual original did not complete.");
                        Check(float.IsFinite(blocked.Result),"Withdrawn blocked original lost its stock raw result.");
                    }
                    else if(mode=="source-busy"&&index==2)
                    {
                        var injected=false;
                        sourceInjection=observation=>
                        {
                            if(injected||observation.Role!=NativePlaybackSourceRole.Pause||observation.ParentSequence!=0)return;
                            injected=true;handoff=router.BeginHandoff(old);NewRun();
                            Check(!handoff.TryAdmit(nextRun!,nextGraph!,api.GameplayPause.ToInt64()),"Actual source entry borrower was bypassed by joint handoff.");
                            Check(group.ReadSourceHealth().InFlight>0&&api.ActiveRoots>=14,"Old actual source roots were released inside its native frame.");
                        };
                        Call(NativePlaybackSourceRole.Pause);sourceInjection=null;Check(injected,"The actual source entry did not execute its handoff interleave.");
                    }
                    else {handoff=router.BeginHandoff(old);NewRun();}
                    Check(!group.GetTimeLease.ReadHealth().AuthorityActive,"Old logical policy remained active while handing off.");
                    Reject(()=>old.CaptureRateSource(origin),"An ended logical session issued a new source capture.");
                    Reject(()=>Clone(handoff!).TryAdmit(nextRun!,nextGraph!,api.GameplayPause.ToInt64()),"A copied route handoff admitted a new session.");
                    var nativeReceipt=Private<NativePlaybackSingleReturnBridge.LogicalHandoff>(handoff!,"_native");
                    Reject(()=>Clone(nativeReceipt).TryAdmit(),"A copied native receipt acquired source/run transfer authority.");
                    // New-run fixture storage is prepared while the native source gate
                    // is closed. The actual setter still advances native fences.
                    api.GameplayApi.Call(api.GameplaySource,"set_pitch",NativeRuntime.Arg.Value(1f));
                    Marshal.WriteInt64(api.GameplaySource,24,BitConverter.DoubleToInt64Bits(3));
                    var cutInjected=false;
                    if(mode=="cut-source-enter") api.DuringRootedDspRead=()=>
                    {
                        if(cutInjected)return;cutInjected=true;var observed=sourceObservations;
                        Check(handoff!.Read().Phase==2,"The successor constructor ran before the admitted source-closed phase.");
                        Call(NativePlaybackSourceRole.Pause);
                        Check(sourceObservations==observed&&group.ReadSourceHealth().InFlight==0,
                            "A cut-after-entry source call borrowed the predecessor or a half-constructed successor.");
                        Check(!group.GetTimeLease.ReadHealth().AuthorityActive&&api.ActiveRoots>=14,
                            "The source-closed construction gap activated policy or dropped old/new independent roots.");
                    };
                    Check(handoff!.TryAdmit(nextRun!,nextGraph!,api.GameplayPause.ToInt64()),"Actual drained joint handoff did not admit: "+router.LastFailure);
                    api.DuringRootedDspRead=null;
                    if(mode=="cut-source-enter")Check(cutInjected,"The source-closed constructor interleave did not execute.");
                    oldSource.Dispose();Check(api.ActiveRoots==7,"Only the newly associated source's seven roots should remain after joint drain.");
                    InitialPause();session=router.PreparePausedClock();var dormant=group.GetTimeLease.ReadCurrent();
                    Check(!dormant.Active&&dormant.Authority!.Revision>oldState.Revision,"Successor initial policy reset revision or activated before final construction.");
                    Check(session.TryArm(),"Successor final Clock did not consume its native admitted baseline.");
                    var current=session.Clock.Capture();binding=current.Binding;origin=new(binding.RunId,binding.ControllerGeneration,binding.Controller,binding.Information,binding.Progress,api.GameplayPause.ToInt64());run=nextRun!;
                    Check(current.Binding.RunId!=oldState.Binding.RunId&&current.Binding.SourceGeneration>oldState.Binding.SourceGeneration
                        &&!ReferenceEquals(session.Clock,oldClock)&&current.Revision>oldState.Revision,"New run/source epoch/Clock/revision were not independently associated.");
                    epochs.Add((ulong)binding.SourceGeneration);revisions.Add(current.Revision);
                    receipts.Add(nativeReceipt.Read(5).Receipt);
                    old.Withdraw("Queued old logical notification");Clone(old).Withdraw("Copied old cleanup");
                    oldPublication.Withdraw("Queued exact predecessor publication cleanup");
                    lateWithdrawal("Queued old rooted source notification");lateReturned(lateReceipt);
                    Check(!group.GetTimeLease.OwnsPublication(oldAuthority)&&!group.GetTimeLease.OwnsPublication(oldAuthority with{}),
                        "An equal or retained predecessor authority owned the lawful successor publication.");
                    Reject(()=>nativeReceipt.TryAdmit(),"A consumed native transfer receipt admitted a second successor.");
                    Reject(()=>oldSource.VerifyAuthority(),"Late old rate source reacquired successor authority.");
                    Check(group.GetTimeLease.ReadHealth().AuthorityActive,"Old publication cleanup withdrew the lawful successor.");
                    if(retainedOldCompletion is { } late)router.ObserveReturned(late);
                    Check(group.GetTimeLease.ReadHealth().AuthorityActive,"A delivered old completion withdrew the successor.");
                    if(mode!="long-sequence"||index==sessions)Exercise(session,binding,origin,run);
                    else Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3,"An append-only handoff failed its actual native paused return.");
                    Check((group.Epoch,group.GetTimeLease.EntryAddress,group.GetTimeLease.OriginalAddress,group.SetPitchLease.EntryAddress,group.SetPitchLease.OriginalAddress)==physical
                        &&broker.ReadActualPlaybackDobbyCallCounts()==(12,12),"A cross-run logical session reinstalled or replaced its physical installation.");
                }
                Check(epochs.Distinct().Count()==sessions-1&&receipts.Distinct().Count()==sessions-1
                    &&revisions.Zip(revisions.Skip(1)).All(pair=>pair.First<pair.Second),"Continuous logical sessions reused epochs, retained receipts or policy revisions.");
            }
            router.Dispose();router.PumpCompleted();
            if(mode=="sticky-fault")Check(api.ActiveRoots>=7&&!router.RootsReleased,"Unknown physical drain fabricated root release.");
            else Check(api.ActiveRoots==0&&router.RootsReleased,"Final actual drain failed to release all independent roots.");
            Check(broker.ReadActualPlaybackDobbyCallCounts()==(12,12),"Logical cleanup changed physical Commit count.");
            Console.WriteLine(JsonSerializer.Serialize(new{kind="joint-native-logical-handoff",mode,passed=true,checks=_checks,readerCalls=reads,
                actualPrepare=12,actualCommit=12,twoRunPositive=mode!="sticky-fault",jointGetTimeSourceGate=true,productionAdmission=false,metadataSynthetic=true,gameExecuted=false,installed=false}));return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);Console.WriteLine(JsonSerializer.Serialize(new{kind="joint-native-logical-handoff",mode,passed=false,checks=_checks,error=error.Message}));return 1;}
    }
}
