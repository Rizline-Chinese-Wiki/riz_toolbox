using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    private static int RunTracker(string[] a,string mode)
    {
        try
        {
            Check(a.Length==5&&mode is "tracker" or "tracker-run-loss" or "tracker-stop-inflight","Unknown tracker mode.");
            var module=NativeLibrary.Load(a[1]);
            Check(Hash(a[1])=="9fe8cde32ae1ce1fe9426efafb46a758d4e37593007a64747a042fdcab51bd6e","Only the exact fixed owned fixture may execute.");
            using var api=new FakeNative();var e=api.ConfigureInitializer(module);
            var targets=PlaybackSourceGroupTargets.ResolveOwnedFixture(module);
            var state=new GameEventState();state.Scene("Level");state.SetAvailability(scenes:true,runs:true,pause:true);
            var started=state.Controller(api.GameplayInformation.ToInt64(),true,false,"initializer-rooted",api.GameplayController.ToInt64())!;
            var binding=new NativePlaybackBinding(started.RunId,state.SceneGeneration,1,1,api.GameplayController.ToInt64(),api.GameplayInformation.ToInt64(),
                api.GameplayProgress.ToInt64(),api.GameplayPlayer.ToInt64(),api.GameplaySource.ToInt64(),api.GameplayClip.ToInt64(),targets.GetTime.MethodInfo.ToInt64());
            var run=state.CapturePlaybackRun(binding.Information,binding.Controller);
            NativePlaybackInitializationTracker? tracker=null;
            var broker=new ExactHookBroker(a[4],new Main(),new Logger());
            var group=broker.BindPlaybackSourceGroup(targets,a[2],Hash(a[2]),a[3],Hash(a[3]),
                ()=>throw new InvalidOperationException("No Clock is admitted by rooted initializer proof."),_=>{},_=>{},
                observation=>{if(tracker is null||!tracker.ObserveBefore(observation))observation.Dispose();},
                completion=>tracker?.ObserveReturned(completion));
            tracker=new NativePlaybackInitializationTracker(api.GameplayApi,group,run,binding);
            var invoke=Export<InvokeStart>(module,"StockFixtureInvokeStart");
            uint Call(){Check(invoke(targets.ProgressStartMoveNext.Address,e,targets.ProgressStartMoveNext.MethodInfo,out var raw,out var code)==1&&code==0,
                "Actual rooted initializer failed: "+tracker.LastFailure);return raw;}
            Check(Call()==1&&tracker.RootsRetained&&api.ActiveRoots==2,"Yield did not independently retain exact enum and Progress roots.");
            var unavailable=false;
            try{tracker.CaptureCompleted();}catch(GameplayStateException error) when(error.Code==ErrorCode.NotReady){unavailable=true;}
            Check(unavailable,"Yield signed a completed initializer.");
            Task.Run(()=>{var rejected=false;try{tracker.Dispose();}catch(GameplayStateException error) when(error.Code==ErrorCode.WrongThread){rejected=true;}Check(rejected,"Worker freed initializer roots.");}).GetAwaiter().GetResult();
            Check(api.ActiveRoots==2,"Rejected worker cleanup changed roots.");
            if(mode=="tracker-run-loss")
            {
                state.ControllerUnavailable();
                var refused=false;try{tracker.CaptureCompleted();}catch(GameplayStateException){refused=true;}
                Check(refused&&tracker.LastFailure is not null,"Lost host run retained initializer completion authority.");
                tracker.Dispose();Check(!tracker.RootsRetained&&api.ActiveRoots==0,"Known drained cancellation did not release the two independent roots.");
            }
            else
            {
                Boundary boundary=(value,at)=>
                {
                    Check(value==e,"Boundary receiver changed.");
                    if(mode=="tracker-stop-inflight"&&at==2)
                    {
                        tracker.Dispose();
                        Check(tracker.RootsRetained&&api.ActiveRoots==2,"In-flight stop freed the initializer/Progress roots.");
                    }
                    else if(mode=="tracker")
                    {
                        var refused=false;try{tracker.CaptureCompleted();}catch(GameplayStateException error) when(error.Code==ErrorCode.NotReady){refused=true;}
                        Check(refused,"Inline store boundary signed a completion before parent physical exit.");
                    }
                };
                Export<SetBoundary>(module,"StockFixtureSetStartBoundary")(boundary);
                Check(Call()==0,"Rooted continuation did not preserve raw false.");
                if(mode=="tracker")
                {
                    var done=tracker.CaptureCompleted();done.VerifyCurrent();
                    Check(ReferenceEquals(done,tracker.CaptureCompleted())&&done.Player==Marshal.ReadIntPtr(api.GameplayProgress,0x38).ToInt64()
                        &&done.Source==api.GameplaySource.ToInt64()&&done.Clip==api.GameplayClip.ToInt64(),"Completion lost exact registered object/native graph provenance.");
                    Reject(group.RequireFreshAdmission,"Rooted completion automatically reopened control before a private transfer.");
                }
                tracker.Dispose();Check(!tracker.RootsRetained&&api.ActiveRoots==0,"Final known physical drain did not release owned initializer roots once.");
                GC.KeepAlive(boundary);
            }
            Check(broker.ReadActualPlaybackDobbyCallCounts()==(12,12),"Root retention installed duplicate physical hooks.");
            Console.WriteLine(JsonSerializer.Serialize(new{mode,passed=true,checks=_checks,sourceVersion=4,initializationProtocol=2,
                retainedInitializerRoots=2,finalRoots=api.ActiveRoots,hostRunAssociated=true,sevenRootRecapture=false,sameRunTransfer=false,
                gameExecuted=false,productionAdmission=false}));return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);Console.WriteLine(JsonSerializer.Serialize(new{mode,passed=false,checks=_checks,failure=error.ToString()}));return 1;}
    }
}
