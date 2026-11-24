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
    private static T Clone<T>(T value) where T : class => (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind,IntPtr target,IntPtr self,ulong bits,IntPtr method,out uint code);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetTime(IntPtr self,IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Version();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void MachineInvoke(uint kind,IntPtr target,IntPtr self,ulong bits,IntPtr method,IntPtr before,IntPtr after);
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
        if(mode.StartsWith("operation-",StringComparison.Ordinal))return RunChannelOperation(args,mode);
        try
        {
            Check(args.Length==(mode=="source-v2-rejected"?6:5),"Expected mode stock-fixture Clock-helper source-helper Dobby, plus legacy helper only for source-v2-rejected.");
            if(mode=="source-v2-rejected")
            {
                var current=NativeLibrary.Load(Path.GetFullPath(args[3]));var legacy=NativeLibrary.Load(Path.GetFullPath(args[5]));
                Check(Export<Version>(current,"RtPlaybackSourceVersion")()==4&&Export<Version>(current,"RtPlaybackWriterVersion")()==1,
                    "Current source V4 changed the independent writer V1 wire.");
                Check(Export<Version>(legacy,"RtPlaybackSourceVersion")()==2,"The rejection input is not an actual source V2 helper.");
                var hub=new NativePlaybackStockSourceHub(Path.GetFullPath(args[3]),Hash(args[3]),_=>{});
                Check(hub.ReadHealth() is{Known:true,Armed:false,Entered:0,Exited:0,InFlight:0},"V4 helper failed actual dormant construction.");
                UnsupportedSignatureException? legacyRefusal=null;
                try { _=new NativePlaybackStockSourceHub(Path.GetFullPath(args[5]),Hash(args[5]),_=>{}); }
                catch(UnsupportedSignatureException error) when(error.Message=="UnsupportedSignature: The independent typed source helper wire differs from v4.") { legacyRefusal=error; }
                Check(legacyRefusal is not null,"Managed V4 accepted a source V2 helper.");
                Console.WriteLine(JsonSerializer.Serialize(new{kind="stock-source-v4-contract-checks",mode,passed=true,checks=_checks,sourceVersion=4,writerVersion=1,
                    sourceV2Rejected=true,refusal=legacyRefusal!.Message,actualPrepare=0,actualCommit=0,gameExecuted=false,productionAdmission=false}));
                return 0;
            }
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
            Action<NativePlaybackSourceObservation>? setTimeBefore = null;
            var setTimeCalls = new List<NativePlaybackSourceObservation>();
            var setTimeReturned = new List<NativePlaybackSourceCompletion>();
            var setTimeFacts = new List<object>();
            object? setTimeFaultState = null;
            void CaptureSetTimeFacts()
            {
                foreach(var observation in setTimeCalls)
                {
                    var returned=setTimeReturned.Single(value=>ReferenceEquals(value.Observation,observation));
                    Check(!returned.PhysicalExited&&returned.Abnormal==(mode=="time-throw")&&returned.Returned!=(mode=="time-throw"),
                        "SetTime original return callback changed its normal/abnormal responsibility.");
                    Check(observation.TryReadCompletion(out var physical)&&physical is{PhysicalExited:true}
                        &&physical.Abnormal==(mode=="time-throw")&&physical.Returned!=(mode=="time-throw"),
                        "SetTime lacks the matching actual native finally physical-exit receipt.");
                    Check(physical!.SchedulingSequence==observation.SchedulingSequence&&physical.Unknown==(mode=="time-throw"),
                        "SetTime physical exit changed its exact Scheduling attempt or abnormal Unknown state.");
                    setTimeFacts.Add(new{role=(uint)observation.Role,observation.Sequence,observation.ParentSequence,
                        callerRva=observation.Caller.ToInt64()-native.ToInt64(),incomingMethod=observation.IncomingMethod.ToInt64(),
                        observation.ArgBits,observation.WriterSequence,observation.SourceSequence,observation.SchedulingSequence,
                        physicalExited=physical!.PhysicalExited,returned=physical.Returned,abnormal=physical.Abnormal,unknown=physical.Unknown});
                }
            }
            var sourceEvents = new List<(NativePlaybackSourceRole Role,ulong Sequence,ulong Parent,IntPtr Caller,IntPtr Method,ulong Bits)>();
            Action<NativePlaybackSourceCompletion>? beforeScheduleReadback = null;
            object? scheduleEvidence = null;
            var group=broker.BindPlaybackSourceGroup(targets,Path.GetFullPath(args[2]),Hash(args[2]),Path.GetFullPath(args[3]),Hash(args[3]),
                ()=>{nativeReaders++;nativeReaderClockMatches &= finalClock is not null && ReferenceEquals(finalClock,session!.Clock);return router.ReadClock();},
                fault=>{deliveredFaults.Add(fault);faultThread=Environment.CurrentManagedThreadId;router.OnGetTimeFault(fault);physicalFault.Set();},
                fault=>{deliveredFaults.Add(fault);faultThread=Environment.CurrentManagedThreadId;router.OnSourceFault(fault);physicalFault.Set();},
                observation=>{router.ObserveBefore(observation);sourceEvents.Add((observation.Role,observation.Sequence,observation.ParentSequence,observation.Caller,observation.IncomingMethod,observation.ArgBits));if(observation.Role==NativePlaybackSourceRole.SetTime){setTimeCalls.Add(observation);setTimeBefore?.Invoke(observation);}},
                completion=>{beforeScheduleReadback?.Invoke(completion);router.ObserveReturned(completion);if(completion.Observation.Role==NativePlaybackSourceRole.SetTime)setTimeReturned.Add(completion);},
                getTimeAfter:(frame,completion)=>{originalBits.Add(completion.OriginalBits);duringReturn?.Invoke();});
            router.Attach(group);
            var retainedGetTimeLease=group.GetTimeLease;
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
            using var provider=NativePlaybackStockRateProvider.AdoptControlledFixture(session);
            Check(provider.Probe().Supported&&!provider.ProductionAdmission,"Private controlled Session was not adopted.");
            Check(ReferenceEquals(provider.CaptureGetTimeHook(),group.GetTimeLease),"Provider returned a matching lease copy instead of its exact retained group lease.");
            Reject(()=>NativePlaybackStockRateProvider.AdoptControlledFixture(session),"A Session issued two logical provider owners.");
            Reject(()=>Clone(provider).Capture(origin),"A copied provider borrowed the registered Session.");
            Reject(()=>Clone(provider).Dispose(),"Copied provider cleanup disposed the actual route.");
            Check(provider.Probe().Supported,"Copied cleanup withdrew the legitimate provider.");
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
            if(mode=="worker")
            {
                var roots=api.ActiveRoots;
                Task.Run(()=>Reject(module.Release,"Worker released the owner-thread GameplayModule.")).GetAwaiter().GetResult();
                Check(module.Tick().Ready&&api.ActiveRoots==roots&&provider.Probe().Supported&&controls.GetState().Success,
                    "Rejected worker Module.Release lost the live Service or its roots before the owner could retry.");
            }
            if (mode is "schedule-origin-before-writer" or "schedule-expired-before-writer"
                or "schedule-origin-after-writer" or "schedule-origin-before-publication")
            {
                Check(Need(lease.Execute(request)).Modification.ConfirmedValue == 2, "Setup did not select an actual non-stock pitch.");
                var pitchCalls = Marshal.ReadInt32(api.GameplaySource, 20);
                var revision = finalClock.Capture().Revision;
                var early = mode is "schedule-origin-before-writer" or "schedule-expired-before-writer";
                var role = early ? NativePlaybackSourceRole.Replay : NativePlaybackSourceRole.Resume;
                var injected = 0;
                void ChangeOrigin()
                {
                    ++injected;
                    Check(Marshal.ReadByte(api.GameplayPlayer, 0x18) == 1, "The actual restart original did not establish a nullable origin.");
                    Marshal.WriteInt64(api.GameplayPlayer, 0x20, BitConverter.DoubleToInt64Bits(99));
                }
                beforeScheduleReadback = completion =>
                {
                    var frame = completion.Observation;
                    if (!completion.Returned) return;
                    if (early && frame.Role == role && frame.ParentSequence == 0)
                    {
                        if (mode == "schedule-expired-before-writer") { ++injected; api.SetRootedDsp(101); }
                        else ChangeOrigin();
                    }
                    else if (!early && frame.Role == NativePlaybackSourceRole.SetPitch && frame.ArgBits == 0x40000000)
                    {
                        if (mode == "schedule-origin-after-writer") ChangeOrigin();
                        else
                        {
                            var dspReads = 0;
                            api.DuringRootedDspRead = () =>
                            {
                                if (++dspReads != 2) return;
                                api.DuringRootedDspRead = null;
                                ChangeOrigin();
                            };
                        }
                    }
                };
                try { Call(role, .25f); }
                finally { beforeScheduleReadback = null; api.DuringRootedDspRead = null; }
                var after = finalClock.Capture();
                var scheduled = sourceEvents.Single(row => row.Role == NativePlaybackSourceRole.PlayScheduled);
                var start = BitConverter.Int64BitsToDouble(unchecked((long)scheduled.Bits));
                var actualPitchWrites = Marshal.ReadInt32(api.GameplaySource, 20) - pitchCalls;
                Check(injected == 1 && start == 100.25 && scheduled.Parent != 0,
                    "The test did not change one actual readback around the exact native schedule.");
                Check(actualPitchWrites == (early ? 1 : 2), "The rejected schedule ran an unexpected selected-pitch writer.");
                Check(after.Phase == NativePlaybackPhase.Withdrawn && after.Revision == revision + 1 && !provider.Probe().Supported,
                    "A conflicting native schedule reached the current Clock or left the provider enabled.");
                Check(router.LastFailure?.Contains(mode == "schedule-expired-before-writer" ? "DSP start" : "origin", StringComparison.Ordinal) == true,
                    "The actual schedule rejection lost its specific cause: " + router.LastFailure);
                _ = getTime(api.GameplayPlayer, targets.GetTime.MethodInfo);
                Check(nativeReaders == 0 && retainedGetTimeLease.ReadHealth().OriginalCalls == 1,
                    "The rejected schedule replaced the native original instead of withdrawing the policy.");
                var reading = clockView!.Read();
                Check(!reading.Success || reading.Value is { Playback.Available: false, Playback.Sample: null },
                    "The SDK continued to expose the withdrawn playback sample.");
                scheduleEvidence = new { role = role.ToString(), start, actualPitchWrites, phase = after.Phase.ToString(),
                    after.Revision, router.LastFailure, dsp = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(api.GameplayPlayer, 0x30)),
                    origin = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(api.GameplayPlayer, 0x20)) };
            }
            else if(mode=="wrong-origin")
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
            else if(mode.StartsWith("time-",StringComparison.Ordinal))
            {
                using var prepared=NativePlaybackRateParticipants.Prepare(request,finalClock,()=>provider.Capture(origin),()=>run.VerifyCurrent(binding));
                if(mode=="time-restore") { prepared.Commit();Check(prepared.Confirm().Observed==true,"SetTime restore setup did not commit."); }
                using var oldSource=provider.Capture(origin);
                // The prepared transaction now reserves the source's sole
                // continuous writer. Inspect that actual retained participant
                // instead of preparing a second writer alongside it.
                var participants = ChannelPrivate<IGameplayValueParticipant[]>(prepared, "_participants");
                var ratePlan = ChannelPrivate<object>(participants[0], "_plan");
                var oldWriter = ChannelPrivate<INativePlaybackPitchWrite>(ratePlan, "_pitchWrite");
                Reject(() => oldSource.PreparePitchWrite(), "A second prepared writer borrowed this transaction's source operation.");
                oldSource.VerifyAuthority();oldWriter.VerifyCurrent();
                Check(finalClock.Capture().Phase==NativePlaybackPhase.Paused&&group.GetTimeLease.ReadHealth().AuthorityActive,
                    "SetTime setup already lost its valid old source/writer/Clock authority.");
                var beforeTime=Marshal.ReadInt64(api.GameplaySource,24);
                var beforeCalls=Marshal.ReadInt32(api.GameplaySource,120);
                var beforeFence=group.ReadSourceHealth().SchedulingSequence;
                var beforeHits=0;
                Exception? beforeFailure=null;
                setTimeBefore=observation=>
                {
                    try
                    {
                    ++beforeHits;
                    Check(observation.Self==api.GameplaySource&&observation.ParentSequence==0&&observation.IncomingMethod==targets.SetTime.MethodInfo,
                        "External SetTime lost exact self/metadata/independent physical scope.");
                    Check(observation.ArgBits==unchecked((uint)BitConverter.SingleToInt32Bits(mode=="time-aba"&&beforeHits==1?7f:3f)),
                        "SetTime Before changed the actual incoming Single argument bits.");
                    if(beforeHits==1)Check(Marshal.ReadInt32(api.GameplaySource,120)==beforeCalls&&Marshal.ReadInt64(api.GameplaySource,24)==beforeTime,
                        "SetTime fence callback occurred after the native time store.");
                    Check(finalClock.Capture().Phase==NativePlaybackPhase.Withdrawn&&!group.GetTimeLease.ReadHealth().AuthorityActive,
                        "External SetTime did not withdraw final Clock/native policy before store.");
                    Reject(oldSource.VerifyAuthority,"SetTime Before retained old rooted rate-source authority.");
                    Reject(oldWriter.VerifyCurrent,"SetTime Before retained old pitch writer authority.");
                    if(mode=="time-restore")
                    {
                        var pitchBits=Marshal.ReadInt32(api.GameplaySource,16);var pitchCalls=Marshal.ReadInt32(api.GameplaySource,20);
                        prepared.Restore();
                        Check(!prepared.ConfirmRestored().OriginalStateRestored&&Marshal.ReadInt32(api.GameplaySource,16)==pitchBits&&Marshal.ReadInt32(api.GameplaySource,20)==pitchCalls,
                            "SetTime Before restored an already committed stale preparation or wrote a stale pitch.");
                    }
                    else Reject(prepared.Commit,"SetTime Before committed an old prepared transaction.");
                    }
                    catch(Exception error){beforeFailure=error;throw;}
                };
                if(mode=="time-throw")Marshal.WriteInt32(api.GameplaySource,68,64);
                void TimeWrite(float value)
                {
                    if(mode=="time-same-runtime"||mode=="time-aba"||mode=="time-restore")
                        api.GameplayApi.Call(api.GameplaySource,"set_time",NativeRuntime.Arg.Value(value));
                    else if(mode=="time-machine")
                    {
                        var before=Marshal.AllocHGlobal(240);var after=Marshal.AllocHGlobal(240);
                        try
                        {
                            Export<MachineInvoke>(native,"StockFixtureMachineInvoke")(2,targets.SetTime.Address,api.GameplaySource,
                                unchecked((uint)BitConverter.SingleToInt32Bits(value)),targets.SetTime.MethodInfo,before,after);
                            var a=new byte[240];var b=new byte[240];Marshal.Copy(before,a,0,a.Length);Marshal.Copy(after,b,0,b.Length);
                            Check(a.AsSpan(0,64).SequenceEqual(b.AsSpan(0,64))&&a.AsSpan(80,160).SequenceEqual(b.AsSpan(80,160)),
                                "SetTime typed bridge/original changed a nonvolatile GPR/XMM register.");
                            Check((Marshal.ReadInt64(before,64)&0x400)==0&&(Marshal.ReadInt64(after,64)&0x400)==0,
                                "SetTime typed bridge/original violated the Windows x64 direction flag contract.");
                        }
                        finally{Marshal.FreeHGlobal(after);Marshal.FreeHGlobal(before);}
                    }
                    else
                    {
                        var status=invoke(2,targets.SetTime.Address,api.GameplaySource,unchecked((uint)BitConverter.SingleToInt32Bits(value)),targets.SetTime.MethodInfo,out var code);
                        Check(mode=="time-throw"?status==2&&code==0xe2400019u:status==1&&code==0,"SetTime native original status/exception changed.");
                    }
                }
                TimeWrite(mode=="time-aba"?7f:3f);
                if(mode=="time-aba")TimeWrite(3f);
                // Copy diagnostics from the actual retained physical-exit receipt
                // before SDK pumping can release the observer; snapshots grant no authority.
                if(mode=="time-throw")CaptureSetTimeFacts();
                setTimeBefore=null;
                Check(beforeFailure is null,"A required SetTime Before-store assertion failed: "+beforeFailure?.Message);
                Check(beforeHits==(mode=="time-aba"?2:1),"SetTime same/ABA missed an actual Before attempt.");
                Check(Marshal.ReadInt32(api.GameplaySource,120)==beforeCalls+beforeHits&&Marshal.ReadIntPtr(api.GameplaySource,128)==targets.SetTime.MethodInfo,
                    "SetTime original was duplicated/skipped or changed incoming metadata.");
                Check(Marshal.ReadInt64(api.GameplaySource,24)==BitConverter.DoubleToInt64Bits(3)&&Marshal.ReadInt64(api.GameplaySource,136)==BitConverter.SingleToInt32Bits(3),
                    "SetTime native raw arguments/readback did not end at the original equal position.");
                Check(setTimeCalls[^1].SchedulingSequence==beforeFence+(ulong)beforeHits,"SetTime attempts did not advance the continuous native Scheduling fence exactly.");
                Check(!provider.Probe().Supported&&!lease.Execute(request).Success,"SetTime stale provider/SDK authority survived equal readback.");
                if(mode=="time-throw")
                {
                    Reject(group.VerifyCurrent,"A real native time-write-after-SEH retained a current physical group lease.");
                    // ReadHealth requires current authority; after the real SEH it must
                    // refuse. ReadActivity retains physical responsibility after withdrawal.
                    var healthRejected=false;
                    try { _=retainedGetTimeLease.ReadHealth(); }
                    catch(UnsupportedSignatureException error) when(error.Message=="UnsupportedSignature: The exact broker GetTime installation is withdrawn, faulted or incomplete.") { healthRejected=true; }
                    Check(healthRejected&&!retainedGetTimeLease.IsActive&&!group.IsActive,
                        "A real native time-write-after-SEH retained a current GetTime or group authority.");
                    var retainedActivity=retainedGetTimeLease.ReadActivity();var groupActivity=group.ReadActivity();
                    Check(retainedActivity is{Known:false,InFlight:0}&&groupActivity==retainedActivity,
                        "A real native time-write-after-SEH lost its physical exit or sticky Unknown responsibility.");
                    setTimeFaultState=new{healthRejected,getTimeActive=retainedGetTimeLease.IsActive,groupActive=group.IsActive,
                        retainedActivity.Known,retainedActivity.InFlight,groupKnown=groupActivity.Known,groupInFlight=groupActivity.InFlight};
                    module.Tick();
                    Check(setTimeCalls.All(observation=>observation.IsReleased),
                        "Owner Tick retained an exited SetTime observation after its diagnostic snapshot.");
                }
                else Check(group.ReadSourceHealth().Known,"A logical SetTime attempt poisoned the permanent physical group.");
                Check(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)==3,"Withdrawn SetTime policy did not forward actual original GetTime.");
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
                const float savedResumeMedia=.5625f;
                Marshal.WriteInt64(api.GameplaySource,24,BitConverter.DoubleToInt64Bits(savedResumeMedia));
                Call(NativePlaybackSourceRole.Pause);var resumeSetTimeCount=setTimeCalls.Count;Call(NativePlaybackSourceRole.Resume,.25f);
                Check(setTimeCalls.Count==resumeSetTimeCount+1&&setTimeCalls[^1].ParentSequence!=0&&setTimeCalls[^1].IncomingMethod==IntPtr.Zero
                    &&setTimeCalls[^1].Caller==group.StockLeafCaller(NativePlaybackSourceRole.Resume,NativePlaybackSourceRole.SetTime),
                    "Formal Resume did not consume one exact actual SetTime child/zero incoming MI/CALL return.");
                var resume=sourceEvents.Last(row=>row.Role==NativePlaybackSourceRole.Resume);
                var children=sourceEvents.Where(row=>row.Parent==resume.Sequence).ToArray();
                Check(children.Select(row=>row.Role).SequenceEqual(new[]{NativePlaybackSourceRole.SetTime,NativePlaybackSourceRole.SetPitch,NativePlaybackSourceRole.PlayScheduled,NativePlaybackSourceRole.SetPitch}),
                    "Formal Resume did not perform exact SetTime/reset-pitch/Schedule followed by its separately reserved rate restoration.");
                Check(children.Take(3).All(row=>row.Method==IntPtr.Zero&&row.Caller==group.StockLeafCaller(NativePlaybackSourceRole.Resume,row.Role)),
                    "Formal Resume used another incoming metadata/caller for a stock child.");
                Check(Marshal.ReadInt32(api.GameplaySource,120)==1&&Marshal.ReadIntPtr(api.GameplaySource,128)==IntPtr.Zero
                    &&setTimeCalls[^1].ArgBits==unchecked((uint)BitConverter.SingleToInt32Bits(savedResumeMedia))
                    &&Marshal.ReadInt64(api.GameplaySource,24)==BitConverter.DoubleToInt64Bits(savedResumeMedia),
                    "Resume bypassed the actual detoured native SetTime original.");
                Check(finalClock.Capture().DspAnchorSeconds==100.75&&finalClock.Capture().MediaAnchorSeconds==savedResumeMedia&&finalClock.Capture().Rate==1.5,"Actual stock Resume did not consume its exact leaf double/reset-restore/saved media.");
                api.SetRootedDsp(101);Check(Math.Abs(getTime(api.GameplayPlayer,targets.GetTime.MethodInfo)-.9375f)<.00001f,"Actual Resume final native GetTime lost selected rate or saved media.");
                Check(nativeReaderClockMatches&&nativeReaders>=3,"Actual native reads routed through another Clock instance.");
            }
            if(mode!="time-throw")CaptureSetTimeFacts();
            module.Release();router.PumpCompleted();
            Check(!provider.Probe().Supported,"Released module continued to advertise speed.");
            var retained=api.ActiveRoots;
            if(mode!="physical-fault"&&mode!="time-throw")Check(router.RootsReleased&&retained==0,"Module/provider cleanup leaked drained native/source/preparation roots.");
            else Check(!router.RootsReleased&&retained>=7,"Physical Unknown was treated as proof that native root responsibility drained.");
            Check(broker.ReadActualPlaybackDobbyCallCounts()==(12,12),"Provider/module consumption changed physical installation identity.");
            Console.WriteLine(JsonSerializer.Serialize(new{kind="stock-concrete-provider-module-sdk-checks",mode,passed=true,checks=_checks,nativeReaders,nativeReaderClockMatches,
                activeRoots=retained,physicalUnknownRetained=mode=="physical-fault"||mode=="time-throw",actualPrepare=12,actualCommit=12,metadataSynthetic=true,
                pinnedClockMappingAvailable=false,controlledProvider=true,completeProductionProvider=false,crossRunHandoff=false,productionAdmission=false,gameExecuted=false,installed=false,
                sourceVersion=4,writerVersion=1,setTimeFacts,setTimeFaultState,scheduleEvidence}));
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);Console.WriteLine(JsonSerializer.Serialize(new{kind="stock-concrete-provider-module-sdk-checks",mode,passed=false,checks=_checks,error=error.Message}));return 1;}
    }
}
