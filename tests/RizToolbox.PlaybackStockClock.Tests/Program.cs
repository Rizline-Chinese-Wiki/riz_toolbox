using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

return StockClockChecks.Run(args);

internal static class StockClockChecks
{
    private static int _checks;
    private static void Check(bool value, string reason) { ++_checks; if (!value) throw new InvalidOperationException(reason); }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static void Reject(Action action, string reason)
    { ++_checks; try { action(); } catch (Exception error) when (error is InvalidOperationException or UnsupportedSignatureException or ObjectDisposedException) { return; } throw new Exception(reason); }
    private static T Clone<T>(T value) where T : class => (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind,IntPtr target,IntPtr self,ulong bits,IntPtr method,out uint code);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetTime(IntPtr self,IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ProgressPlay(IntPtr self, byte fromBeginning, IntPtr method);
    private sealed class Main : IMainThreadDispatcher
    {
        private readonly int _thread=Environment.CurrentManagedThreadId;
        public bool IsMainThread => _thread==Environment.CurrentManagedThreadId;
        public void VerifyAccess(){if(!IsMainThread)throw new InvalidOperationException("Wrong installation thread.");}
        public Task InvokeAsync(Action action,CancellationToken cancellationToken=default){VerifyAccess();action();return Task.CompletedTask;}
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level,string message,Exception? error=null){} }
    internal static int Run(string[] args)
    {
        var mode=args.Length>0?args[0]:"invalid";
        try
        {
            Check(args.Length==5,"Expected mode stock-fixture new-clock-helper source-helper Dobby.");
            var module=NativeLibrary.Load(Path.GetFullPath(args[1]));
            using var api=new FakeNative();api.ConfigureRootedStock(module);
            api.SetRootedGraphValue("progress.now",2.8);
            api.SetRootedGraphValue("info.nowTime",5.6f);
            var targets=PlaybackSourceGroupTargets.ResolveOwnedFixture(module);
            var state=new GameEventState();state.Scene("Level");state.SetAvailability(scenes:true,runs:true,pause:true);
            var started=state.Controller(api.GameplayInformation.ToInt64(),true,false,"stock-clock-chart",api.GameplayController.ToInt64())!;
            state.ObservePause(true);
            var binding=new NativePlaybackBinding(started.RunId,state.SceneGeneration,1,1,api.GameplayController.ToInt64(),api.GameplayInformation.ToInt64(),
                api.GameplayProgress.ToInt64(),api.GameplayPlayer.ToInt64(),api.GameplaySource.ToInt64(),api.GameplayClip.ToInt64(),targets.GetTime.MethodInfo.ToInt64());
            var origin=new NativePlaybackRateOrigin(binding.RunId,1,binding.Controller,binding.Information,binding.Progress,api.GameplayPause.ToInt64());
            var run=state.CapturePlaybackRun(binding.Information,binding.Controller);
            using var router=new NativePlaybackStockClockRouter(api.GameplayApi,binding,origin,run);
            var main=new Main();var broker=new ExactHookBroker(Path.GetFullPath(args[4]),main,new Logger());
            var readerCalls=0;var completions=0;var originals=new List<uint>();
            NativePlaybackStockClockRouter.Session? session=null;Action? duringReturn=null;
            Action<NativePlaybackSourceObservation>? duringSourceBefore=null;
            var group=broker.BindPlaybackSourceGroup(targets,Path.GetFullPath(args[2]),Hash(args[2]),Path.GetFullPath(args[3]),Hash(args[3]),
                ()=>{++readerCalls;return router.ReadClock();},_=>{},_=>{},
                observation=>{router.ObserveBefore(observation);duringSourceBefore?.Invoke(observation);},router.ObserveReturned,
                getTimeAfter:(frame,completion)=>{++completions;originals.Add(completion.OriginalBits);duringReturn?.Invoke();});
            router.Attach(group);
            var invoke=Export<Invoke>(module,"StockFixtureInvoke");
            var getTime=Marshal.GetDelegateForFunctionPointer<GetTime>(targets.GetTime.Address);
            void Call(NativePlaybackSourceRole role,float argument=0)
            {
                var target=targets.SourceTarget(role);
                var self=role==NativePlaybackSourceRole.ProgressPlay?api.GameplayProgress:api.GameplayPlayer;
                var kind=role==NativePlaybackSourceRole.Pause?1u:role==NativePlaybackSourceRole.ProgressPlay?5u:2u;
                var bits=role==NativePlaybackSourceRole.ProgressPlay?0ul:unchecked((uint)BitConverter.SingleToInt32Bits(argument));
                Check(invoke(kind,target.Address,self,bits,target.MethodInfo,out var error)==1&&error==0,"Actual stock original failed: "+role);
            }
            if (mode == "root-retirement-in-flight")
            {
                (bool Idle, long FullImages, int Roots) ProbeDrain()
                {
                    var before = NativePlaybackExecutableSeal.ReadThreadFullImageAudits();
                    var idle = group.TryVerifyIdleForRootRelease();
                    return (idle, NativePlaybackExecutableSeal.ReadThreadFullImageAudits() - before, api.ActiveRoots);
                }
                (bool Idle, long FullImages, int Roots)? sourceDrain = null;
                (bool Idle, long FullImages, int Roots)? clockDrain = null;
                duringSourceBefore = observation =>
                {
                    if (observation.Role == NativePlaybackSourceRole.Pause) sourceDrain = ProbeDrain();
                };
                duringReturn = () => clockDrain = ProbeDrain();
                Marshal.WriteInt32(api.GameplaySource,48,1);
                Call(NativePlaybackSourceRole.Pause);
                Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3,
                    "The real GetTime original did not return while its Clock was dormant.");
                var idleDrain = ProbeDrain();
                Console.WriteLine($"Retirement full image audits: source={sourceDrain?.FullImages}, GetTime={clockDrain?.FullImages}, drained={idleDrain.FullImages}.");
                Check(sourceDrain is { Idle: false, FullImages: 0, Roots: 7 }
                    && clockDrain is { Idle: false, FullImages: 0, Roots: 7 },
                    "An in-flight source or GetTime callback entered the full root retirement audit.");
                Check(idleDrain is { Idle: true, FullImages: > 0, Roots: 7 },
                    "The drained owner skipped the required complete physical audit.");
                router.Dispose(); router.PumpCompleted();
                Check(router.RootsReleased && api.ActiveRoots == 0,
                    "The actual owner did not release its roots after both callbacks exited.");
                group.Dispose();
                Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                    sourceAudits = sourceDrain?.FullImages, getTimeAudits = clockDrain?.FullImages,
                    drainedAudits = idleDrain.FullImages, gameExecuted = false, productionAdmission = false }));
                return 0;
            }
            Marshal.WriteInt32(api.GameplaySource,48,1);Call(NativePlaybackSourceRole.Pause);
            Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3&&readerCalls==0,"Unpublished original was not forwarded exactly.");
            session=router.PreparePausedClock();
            Check(!group.GetTimeLease.ReadHealth().AuthorityActive&&group.GetTimeLease.ReadCurrent().Authority?.Revision==1,
                "Dormant initial policy was activated during construction.");
            Reject(()=>{_=session.Clock;},"A dormant session publicly exposed its final Clock.");
            var dspReads=api.RootedDspReads;
            Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3&&readerCalls==0&&api.RootedDspReads==dspReads,
                "Dormant native entry called an unassigned Clock/source reader.");
            Reject(()=>Clone(session).TryArm(),"A copied session armed a native publication.");
            if(mode=="cancelled-dormant")
            {
                Check(router.CaptureUnavailableTransitionIdleHint(run) is null, "An armable session skipped admission.");
                session.Withdraw("Cancel actual construction.");
                var idle = router.CaptureUnavailableTransitionIdleHint(run);
                Check(idle is not null && idle.StillCurrent(router), "The withdrawn session would repeat full admission on every pump.");
                router.PumpCompleted();
                Check(idle!.StillCurrent(router), "An empty completion pump woke a withdrawn session.");
                Reject(()=>session.TryArm(),"Cancelled construction was rearmed.");
                Check(!group.GetTimeLease.ReadHealth().AuthorityActive&&getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3,
                    "Cancelled dormant policy replaced the native original.");
                state.ControllerUnavailable();
                state.Controller(binding.Information, true, false, "next-chart", binding.Controller);
                var successor = state.CaptureCurrentPlaybackRun();
                Check(!idle.StillCurrent(router) && router.CaptureUnavailableTransitionIdleHint(successor) is null,
                    "A subsequent run inherited the previous run's refusal.");
            }
            else if(mode=="run-lost-dormant")
            {
                state.ControllerUnavailable();
                Reject(()=>session.TryArm(),"A host-withdrawn dormant policy was armed.");
                Check(!group.GetTimeLease.ReadHealth().AuthorityActive&&readerCalls==0,"Run withdrawal consumed an unready Clock.");
            }
            else if(mode=="transition-dormant")
            {
                Call(NativePlaybackSourceRole.Replay,.125f);
                Reject(()=>session.TryArm(),"A replaced paused baseline was armed.");
                Check(!group.GetTimeLease.ReadHealth().AuthorityActive,"A stock transition left a stale initial policy active.");
            }
            else
            {
                Check(session.TryArm(),"The actual native dormant gate did not arm: "+router.LastFailure);
                var clock=session.Clock;
                Check(ReferenceEquals(clock,session.Clock)&&group.GetTimeLease.ReadHealth().AuthorityActive,"The final Clock changed identity or lacks active native policy.");
                Clone(session).Withdraw("Copied late cleanup");
                Check(group.GetTimeLease.ReadHealth().AuthorityActive,"Copied cleanup withdrew the legitimate Clock.");
                using(var source=session.CaptureRateSource(origin))
                    Check(ReferenceEquals(source.Clock,clock)&&api.ActiveRoots==14,"A prepared source lost independent seven roots or the unique final Clock.");
                Check(api.ActiveRoots==7,"Prepared-source Dispose changed observer roots.");
                Reject(()=>session.TryArm(),"Already armed Clock constructed another final instance.");
                Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3&&readerCalls==1,"Armed native entry did not read the final Clock.");
                if (mode is "resume-after-countdown" or "resume-after-untrusted-progress" or "resume-after-wrong-caller"
                    or "resume-progress-with-pause-leaf" or "resume-after-countdown-run-loss")
                {
                    if (mode == "resume-after-countdown")
                    {
                        var request = new GameplayControlRequest(GameplayControlAction.SetSpeed, 1, binding.RunId, 2,
                            GameplayControlValueUnit.PlaybackRateMultiplier);
                        using var prepared = NativePlaybackRateParticipants.Prepare(request, clock,
                            () => session.CaptureRateSource(origin), () => run.VerifyCurrent(binding));
                        prepared.Commit();
                        Check(prepared.Confirm().Observed == true && clock.Capture().Rate == 2,
                            "The fixture did not establish the selected paused rate.");
                    }

                    var before = clock.Capture();
                    var progress = targets.SourceTarget(NativePlaybackSourceRole.ProgressPlay);
                    var withPauseLeaf = mode == "resume-progress-with-pause-leaf";
                    // The pinned native fixture skips PauseLeaf when Play's Boolean matches this byte.
                    Marshal.WriteByte(api.GameplayProgress, 0x42, withPauseLeaf ? (byte)0 : (byte)1);
                    api.SetRootedGraphValue("pause.paused", withPauseLeaf);
                    api.SetRootedGraphValue("progress.preparing", !withPauseLeaf);
                    NativePlaybackSourceObservation? play = null;
                    var pauseLeaves = 0;
                    duringSourceBefore = observation =>
                    {
                        if (observation.Role == NativePlaybackSourceRole.ProgressPlay) play = observation;
                        if (observation.Role == NativePlaybackSourceRole.PauseLeaf) ++pauseLeaves;
                    };
                    var method = mode == "resume-after-untrusted-progress" ? progress.MethodInfo : IntPtr.Zero;
                    var wrongCaller = mode == "resume-after-wrong-caller";
                    if (wrongCaller)
                        Marshal.GetDelegateForFunctionPointer<ProgressPlay>(progress.Address)(api.GameplayProgress, 1, method);
                    else
                        Check(invoke(5, progress.Address, api.GameplayProgress, 1, method, out var error) == 1 && error == 0,
                            "The fixture's Play(true) did not return.");
                    duringSourceBefore = null;
                    Check(play is { ParentSequence: 0, Depth: 1, ArgBits: 1 } && play.IncomingMethod == method
                            && (play.Caller == IntPtr.Add(module, 0x1763)) == !wrongCaller
                            && pauseLeaves == (withPauseLeaf ? 1 : 0),
                        "The fixture did not exercise the intended actual caller, metadata and leaf sequence.");

                    if (mode is "resume-after-untrusted-progress" or "resume-after-wrong-caller")
                    {
                        Check(router.LastFailure is not null && group.GetTimeLease.ReadHealth() is { Known: true, AuthorityActive: false },
                            "An untrusted leafless progress call retained the old Clock.");
                        Check(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 3 && readerCalls == 1,
                            "A rejected call still entered the old Clock reader.");
                    }
                    else if (withPauseLeaf)
                    {
                        Check(clock.Capture() is { Phase: NativePlaybackPhase.Paused } paused && paused.Revision == before.Revision + 1
                                && group.GetTimeLease.ReadHealth().AuthorityActive,
                            "Play(true) with a verified PauseLeaf did not publish its own pause: " + router.LastFailure);
                        using var source = session.CaptureRateSource(origin);
                        Check(ReferenceEquals(source.Clock, clock), "The completed real pause did not supply a current readback.");
                    }
                    else
                    {
                        Check(ReferenceEquals(before, clock.Capture()) && group.GetTimeLease.ReadHealth().AuthorityActive,
                            "Leafless resume preparation withdrew the armed Clock: " + router.LastFailure);
                        Reject(() => session.CaptureRateSource(origin), "A leafless countdown reused the previous paused readback.");
                        router.PumpCompleted();
                        var applied = group.GetTimeLease.ReadHealth().Applied;
                        api.SetRootedDsp(100.01);
                        Check(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 3
                                && group.GetTimeLease.ReadHealth() is { Known: true, AuthorityActive: true } countdownHealth
                                && countdownHealth.Applied == applied + 1,
                            "A native GetTime between countdown and Resume invalidated or bypassed the paused Clock.");
                        api.SetRootedDsp(100.02);
                        router.PumpCompleted();
                        Check(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 3
                                && group.GetTimeLease.ReadHealth().Applied == applied + 2,
                            "The paused Clock did not survive a later countdown frame.");

                        if (mode == "resume-after-countdown-run-loss")
                        {
                            state.ControllerUnavailable();
                            Check(group.GetTimeLease.ReadHealth() is { Known: true, AuthorityActive: false },
                                "Run withdrawal left the previous countdown Clock active.");
                            Check(getTime(api.GameplayPlayer, targets.GetTime.MethodInfo) == 3 && readerCalls == 3,
                                "Run withdrawal still invoked the previous countdown reader.");
                        }
                        else
                        {
                            Call(NativePlaybackSourceRole.Resume, .25f);
                            var scheduled = clock.Capture();
                            Check(scheduled is { Phase: NativePlaybackPhase.Scheduled, MediaAnchorSeconds: 3, Rate: 2 }
                                    && scheduled.Revision == before.Revision + 1 && Math.Abs(scheduled.DspAnchorSeconds - 100.27) < .00001,
                                "The completed Resume did not publish its own schedule and restore the selected rate: " + router.LastFailure);
                            api.SetRootedGraphValue("progress.preparing", false);
                            api.SetRootedGraphValue("progress.playing", true);
                            api.SetRootedDsp(100.77);
                            var activeMedia = getTime(api.GameplayPlayer, targets.GetTime.MethodInfo);
                            Check(Math.Abs(activeMedia - 4f) < .00001f
                                    && group.GetTimeLease.ReadHealth() is { Known: true, AuthorityActive: true },
                                "The resumed time did not advance from the paused anchor at the selected rate.");
                            Console.WriteLine(FormattableString.Invariant(
                                $"Paused media={before.MediaAnchorSeconds:R}, scheduled DSP={scheduled.DspAnchorSeconds:R}, resumed media={activeMedia:R}, rate={scheduled.Rate:R}."));
                        }
                    }
                }
                else if(mode=="dispose-reader")
                {
                    var injected=false;var retained=false;var rootsDuringReader=0;
                    api.DuringRootedPitchRead=()=>
                    {
                        if(injected)return;injected=true;
                        try { router.Dispose(); } catch(Exception error) { Console.Error.WriteLine("Reader Dispose diagnostic: "+error); throw; }
                        // runtime_invoke may temporarily root its receiver too;
                        // all seven independently owned graph roots must remain.
                        rootsDuringReader=api.ActiveRoots;
                        retained=rootsDuringReader>=7&&!router.RootsReleased;
                    };
                    var readerReturn=getTime(api.GameplayPlayer,targets.GetTime.MethodInfo);
                    Console.WriteLine($"Reader Dispose returned={readerReturn:R} injected={injected} retained={retained} duringRoots={rootsDuringReader} roots={api.ActiveRoots} released={router.RootsReleased}");
                    Check(readerReturn==3&&injected&&retained,
                        "Captured GetTime reader cleanup released roots or replaced the original before real native exit.");
                    api.DuringRootedPitchRead=null;
                    Check(group.GetTimeLease.ReadHealth() is {Known:true,AuthorityActive:false,InFlight:0},
                        "Logical withdrawal inside the actual Clock reader poisoned physical health or lost actual drain.");
                    router.PumpCompleted();
                }
                else if(mode=="external-after-arm")
                {
                    api.GameplayApi.Call(api.GameplaySource,"set_pitch",NativeRuntime.Arg.Value(1f));
                    Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3,"Unknown writer did not preserve stock raw return.");
                    Check(!group.GetTimeLease.ReadHealth().AuthorityActive,"An external same-value writer left stale native Clock authority.");
                }
                else if(mode=="run-loss-after-arm")
                {
                    state.ControllerUnavailable();
                    Check(!group.GetTimeLease.ReadHealth().AuthorityActive,"Host withdrawal did not synchronously close the exact native policy.");
                    Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3&&readerCalls==1,
                        "A withdrawn run still dispatched the Clock reader instead of forwarding the native original.");
                }
                else if(mode=="noop-after-arm")
                {
                    Marshal.WriteInt32(api.GameplaySource,48,1);Call(NativePlaybackSourceRole.Replay,.125f);
                    Check(router.LastFailure is not null&&!group.GetTimeLease.ReadHealth().AuthorityActive,
                        "A real no-op stock root left the pre-existing Clock policy active without new occurrence evidence.");
                    Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3&&readerCalls==1,
                        "A no-op stock root silently reused the old Clock baseline.");
                }
                else
                {
                    var request=new GameplayControlRequest(GameplayControlAction.SetSpeed,1,binding.RunId,2,GameplayControlValueUnit.PlaybackRateMultiplier);
                    using(var prepared=NativePlaybackRateParticipants.Prepare(request,clock,()=>session.CaptureRateSource(origin),()=>run.VerifyCurrent(binding)))
                    {
                        prepared.Commit();Check(prepared.Confirm().Observed==true,"Actual native pitch and final Clock participants failed confirmation.");
                        Check(clock.Capture().Rate==2&&BitConverter.Int32BitsToSingle(Marshal.ReadInt32(api.GameplaySource,16))==2,
                            "Actual native pitch and immutable Clock did not both commit selected rate two.");
                        if(mode=="rate-rollback")
                        {
                            prepared.Restore();Check(prepared.ConfirmRestored().OriginalStateRestored&&clock.Capture().Rate==1,
                                "Exact native/Clock restoration did not complete.");
                        }
                    }
                    if(mode!="rate-rollback")
                    {
                        Call(NativePlaybackSourceRole.Replay,.125f);
                        Check(clock.Capture().Phase==NativePlaybackPhase.Scheduled&&clock.Capture().DspAnchorSeconds==100.125&&clock.Capture().Rate==2,
                            "Actual Replay schedule/raw double and selected pitch were not published to the final Clock: "+router.LastFailure);
                        Check(Math.Abs(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)+.125f)<.00001f,"Native scheduled pre-roll was incorrectly scaled.");
                        Check(BitConverter.Int32BitsToSingle(unchecked((int)originals[^1]))==0,"The actual stock original's pre-roll bits were not preserved separately from the Clock candidate.");
                        api.SetRootedDsp(100.5);
                        var active=getTime(api.GameplayPlayer,targets.GetTime.MethodInfo);
                        Check(Math.Abs(active-.75f)<.00001f&&group.GetTimeLease.ReadHealth().OriginalCalls==(ulong)completions,
                            "Actual native original-call completion and selected-rate clock replacement differ.");
                        Call(NativePlaybackSourceRole.Pause);Call(NativePlaybackSourceRole.Resume,.25f);
                        Check(clock.Capture().DspAnchorSeconds==100.75&&clock.Capture().Rate==2,"Actual Resume leaf schedule was not consumed.");
                        if(mode=="withdraw-return")
                        {
                            duringReturn=()=>session.Withdraw("Actual GetTime Returned withdrawal.");
                            var returned=getTime(api.GameplayPlayer,targets.GetTime.MethodInfo);
                            Check(BitConverter.SingleToInt32Bits(returned)==unchecked((int)originals[^1])&&!group.GetTimeLease.ReadHealth().AuthorityActive,
                                "Late native final gate returned a withdrawn provisional candidate.");
                            duringReturn=null;
                        }
                    }
                }
            }
            router.Dispose();router.PumpCompleted();
            Check(router.RootsReleased&&api.ActiveRoots==0,"Clock/source cleanup released or retained roots outside its actual drain.");
            Check(broker.ReadActualPlaybackDobbyCallCounts()==(12,12),"A logical Clock session reinstalled physical hooks.");
            Console.WriteLine(JsonSerializer.Serialize(new{kind="stock-final-clock-native-bootstrap-checks",mode,passed=true,checks=_checks,readerCalls,completions,
                api.RootedDspReads,api.RootedPitchReads,api.ActiveRoots,actualPrepare=12,actualCommit=12,metadataSynthetic=true,
                uniqueFinalClock=true,completeProvider=false,crossRunHandoff=false,productionAdmission=false,gameExecuted=false,installed=false}));
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);Console.WriteLine(JsonSerializer.Serialize(new{kind="stock-final-clock-native-bootstrap-checks",mode,passed=false,checks=_checks,error=error.Message}));return 1;}
    }
}
