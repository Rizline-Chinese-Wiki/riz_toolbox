using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

return StartChecks.Run(args);
internal static partial class StartChecks
{
 private static int _checks;
 private static void Check(bool value,string reason){++_checks;if(!value)throw new InvalidOperationException(reason);if(Environment.GetEnvironmentVariable("RIZ_INITIALIZER_TRACE")=="1")Console.WriteLine(JsonSerializer.Serialize(new{checkpoint=_checks,reason}));}
 private static void Reject(Action action,string reason){++_checks;try{action();}catch(UnsupportedSignatureException){return;}throw new InvalidOperationException(reason);}
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetPitch(IntPtr self,float value,IntPtr method);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetTime(IntPtr self,IntPtr method);
 private static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
 private static T Export<T>(IntPtr m,string n) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(m,n));
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateChild(IntPtr p);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateStart(IntPtr p,IntPtr clip);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InvokeStart(IntPtr target,IntPtr e,IntPtr method,out uint raw,out uint code);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Boundary(IntPtr e,uint at);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetBoundary(Boundary value);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Machine(uint kind,IntPtr target,IntPtr self,ulong bits,IntPtr method,IntPtr before,IntPtr after);
 private sealed class Main:IMainThreadDispatcher
 {
  private readonly int _t=Environment.CurrentManagedThreadId;public bool IsMainThread=>_t==Environment.CurrentManagedThreadId;
  public void VerifyAccess(){if(!IsMainThread)throw new InvalidOperationException("Wrong thread");}
  public Task InvokeAsync(Action action,CancellationToken cancellationToken=default){VerifyAccess();action();return Task.CompletedTask;}
 }
 private sealed class Logger:ILogger { public void Log(LogLevel l,string s,Exception? e=null){} }
 internal static int Run(string[] a)
 {
  var mode=a.Length==5?a[0]:"invalid";
  if(mode.StartsWith("seek-progress-",StringComparison.Ordinal))return RunSeekProgress(a,mode);
  if(mode.StartsWith("seek-source-",StringComparison.Ordinal))return RunSeekSource(a,mode);
  if(mode.StartsWith("seek-time-",StringComparison.Ordinal))return RunSeekTimeWriter(a,mode);
  if(mode.StartsWith("contract-phase6-",StringComparison.Ordinal))return RunContractPhase6NoPending(a,mode);
  if(mode=="lower-pitch-sdk-recover")return RunLowerPitchRecovery(a,mode);
  if(mode is "lower-pitch-first-pending" or "lower-pitch-transfer-pending")return RunLowerPitchPending(a,mode);
  if(mode.StartsWith("lower-pitch-image-",StringComparison.Ordinal))return RunLowerPitchManagedImageGuards(a,mode);
  if(mode is "lower-pitch-worker" or "lower-pitch-foreign")return RunLowerWorkerIsolation(a,mode);
  if(mode.StartsWith("lower-pitch-",StringComparison.Ordinal))return RunLowerPitch(a,mode);
  if(mode=="unity-backing-first-pending-id-recover")return RunUnityBackingFirstPending(a,mode);
  if(mode is "unity-backing-pending-recover" or "unity-backing-pending-source-recover")return RunUnityBackingPending(a,mode);
  if(mode.StartsWith("unity-backing-",StringComparison.Ordinal))return RunUnityBacking(a,mode);
  if(mode.StartsWith("resource-",StringComparison.Ordinal))return RunProgressResource(a,mode);
  if(mode is "stock-stop-replay" or "stock-stop-resume")return RunStockStop(a,mode);
  if(mode=="progress-foreign")return RunProgressForeign(a,mode);
  if(mode=="progress-first-hold")return RunFirstCapture(a,"first-hold",constructor:true,directSourceWatch:true,progressForeign:true);
  if(mode=="player-sdk-normal")return RunPlayerControlNormal(a,mode);
  if(mode.StartsWith("legacy-current-",StringComparison.Ordinal))return RunLegacyAudioCurrent(a,mode);
  if(mode is "direct-source-same" or "direct-source-cross")return RunDirectAudioHandoff(a,mode);
  if(mode.StartsWith("direct-",StringComparison.Ordinal))return RunDirectAudio(a,mode);
  if(mode.StartsWith("ctor-source-",StringComparison.Ordinal))return RunConstructorSourceOpen(a,mode);
  if(mode is "ctor-sdk-reentry" or "ctor-sdk-inflight")return RunConstructorFaultLifecycle(a,mode);
  if(mode.StartsWith("ctor-first-",StringComparison.Ordinal))return RunFirstCapture(a,mode.Substring(5),true);
  if(mode is "ctor-root-drain" or "ctor-root-free-fault")return RunConstructorRootDrain(a,mode);
  if(mode is "ctor-owner-fault-deferred" or "ctor-host-fault-deferred")return RunDeferredOwnerFault(a,mode);
  if(mode=="ctor-sdk-worker")return RunConstructorWorker(a,mode);
  if(mode=="ctor-sdk-crossrun")return RunConstructorCrossRun(a,mode);
  if(mode.StartsWith("ctor-",StringComparison.Ordinal))return RunConstructor(a,mode);
  if(mode.StartsWith("first-",StringComparison.Ordinal))return RunFirstCapture(a,mode);
  if(mode.StartsWith("handoff",StringComparison.Ordinal))return RunNativeHandoff(a,mode);
  if(mode.StartsWith("router",StringComparison.Ordinal))return RunRouter(a,mode);
  if(mode.StartsWith("tracker",StringComparison.Ordinal))return RunTracker(a,mode);
  try{
   Check(a.Length==5,"Expected mode, fixture, SingleReturn helper, SourceV4 helper, Dobby.");
   Check(mode is "raw" or "seh" or "hold" or "late" or "interference","Unknown mode.");
   var module=NativeLibrary.Load(Path.GetFullPath(a[1]));
   Check(Hash(a[1])=="9fe8cde32ae1ce1fe9426efafb46a758d4e37593007a64747a042fdcab51bd6e","Only the exact owned Start fixture may execute.");
   var source=Export<Create>(module,"StockFixtureCreateSource")();
   Marshal.Copy(new[]{3.0},0,IntPtr.Add(source,24),1);
   var player=Export<CreateChild>(module,"StockFixtureCreatePlayer")(source);
   var progress=Export<CreateChild>(module,"StockFixtureCreateProgress")(player);
   var clip=Marshal.AllocHGlobal(32);
   var e=Export<CreateStart>(module,"StockFixtureCreateStart")(progress,clip);
   var targets=PlaybackSourceGroupTargets.ResolveOwnedFixture(module);
   var observations=new List<NativePlaybackSourceObservation>();var returned=new List<NativePlaybackSourceCompletion>();
   var callbackRaw=new List<byte>();var parentReturnedStillInflight=0;
   PlaybackSourceGroupLease? group=null;
   var binding=new NativePlaybackBinding("initializer-hold",1,2,3,0x101,0x102,progress.ToInt64(),player.ToInt64(),source.ToInt64(),clip.ToInt64(),targets.GetTime.MethodInfo.ToInt64());
   var initial=new NativePlaybackClockState(binding,1,NativePlaybackPhase.Paused,100,6,2,200);
   NativePlaybackSingleReturnSample Sample()=>new(NativePlaybackClockPublication.Identity(binding),1,NativePlaybackClockPublication.Phase(initial),101);
   var freshBefore=0;var freshReturned=0;
   var broker=new ExactHookBroker(Path.GetFullPath(a[4]),new Main(),new Logger());
   group=broker.BindPlaybackSourceGroup(targets,Path.GetFullPath(a[2]),Hash(a[2]),Path.GetFullPath(a[3]),Hash(a[3]),
    Sample,_=>{},_=>{},
    o=>{o.VerifyCurrent();observations.Add(o);
     if(o.Role==NativePlaybackSourceRole.ProgressStartMoveNext){Reject(group!.RequireFreshAdmission,"Start Before left fresh admission open.");++freshBefore;}
    },c=>{
     c.VerifyCurrent();returned.Add(c);
     Check(!c.PhysicalExited,"Returned callback fabricated PhysicalExit.");
     if(c.Observation.Role==NativePlaybackSourceRole.ProgressStartMoveNext&&c.Returned){
      Reject(group!.RequireFreshAdmission,"Start Returned opened fresh admission before physical exit.");++freshReturned;
      Check(c.HasRawReturnByte,"A real normal BooleanReturn0 lacks AL.");callbackRaw.Add(c.RawReturnByte);
      Check(!c.Observation.TryReadCompletion(out _),"Parent journal completed inside Returned callback.");++parentReturnedStillInflight;
     }
    });
   Check(broker.ReadActualPlaybackDobbyCallCounts()==(12,12),"All 12 actual Dobby prepares/commits are required.");
   var start=targets.ProgressStartMoveNext;
   Check(group.StockLeafCaller(NativePlaybackSourceRole.ProgressStartMoveNext,NativePlaybackSourceRole.SetClip)==IntPtr.Add(module,0x2c41),"Actual exact Start clip CALL differs.");
   var lease=group.SourceLease(NativePlaybackSourceRole.ProgressStartMoveNext);
   Check(lease.RelocatedByteCount==6&&lease.PatchedByteCount<=6&&lease.TrampolineUnwindAddress!=IntPtr.Zero,"Start lacks actual closed RBP/RSI Dobby/private unwind proof.");
   group.RequireFreshAdmission();
   var publication=new NativePlaybackClockPublication(group.GetTimeLease,initial,IntPtr.Zero,()=>group.VerifyCurrent());
   var nativeTime=Marshal.GetDelegateForFunctionPointer<GetTime>(targets.GetTime.Address);
   Check(nativeTime(player,targets.GetTime.MethodInfo)==6,"Native GetTime positive was unavailable before initialization.");
   var initialGroupHealth=group.ReadSourceHealth();
   if(mode=="late")Marshal.WriteInt32(e,0x10,1);
   var invoke=Export<InvokeStart>(module,"StockFixtureInvokeStart");
   uint Call(){Check(invoke(start.Address,e,start.MethodInfo,out var raw,out var code)==1&&code==0,"Actual Start failed.");return raw;}
   if(mode!="late"){
    Check(Call()==1&&Marshal.ReadInt32(e,0x10)==1,"Actual state0 did not produce raw yield1/state1.");
    var state0=observations.Single(o=>o.Role==NativePlaybackSourceRole.ProgressStartMoveNext);
    Check(state0.TryReadCompletion(out var zero)&&zero is{PhysicalExited:true,HasRawReturnByte:true,RawReturnByte:1},"Yield has no actual physical-exit AL1 receipt.");
    var yielded=group.ReadInitialization();
    Check(yielded.Phase==2&&yielded.Blocked==1&&yielded.YieldSequence==state0.Sequence&&yielded.Receipt==0,
     "Yield failed to retain its exact native initialization association/hold.");
    Check(group.ReadSourceHealth() is{Known:true,Armed:true,InFlight:0},"Fresh hold changed observer Armed/physical health.");
    Reject(group.RequireFreshAdmission,"Zero in-flight yield reopened fresh capture.");
    Check(nativeTime(player,targets.GetTime.MethodInfo)==3,"Native GetTime still applied old policy during yield.");
    if(mode=="hold"){
     var refused=false;
     try{publication.Publish(initial,initial with{Revision=2,Rate=3});}
     catch(InvalidOperationException error) when(error.Message=="Controlled Single clock publication rejected: 4."){refused=true;}
     Check(refused,"A valid next Clock policy crossed the native initialization hold.");
    }
   }
   if(mode=="interference"){
    var setter=Marshal.GetDelegateForFunctionPointer<SetPitch>(targets.PitchInstance.Address);setter(source,1,targets.PitchInstance.MethodInfo);
   }
   var boundaries=new List<uint>();
   Boundary boundary=(value,at)=>{
    Check(value==e,"Native boundary used another enumerator.");boundaries.Add(at);
    Reject(group!.RequireFreshAdmission,"An inline-store boundary reopened fresh admission.");
    Check(nativeTime(player,targets.GetTime.MethodInfo)==3,"Native old GetTime crossed the initialization gate.");
    Check(group.ReadInitialization().Receipt==0,"Initializer receipt existed before the parent really exited.");
    var parent=observations.Last(o=>o.Role==NativePlaybackSourceRole.ProgressStartMoveNext);
    Check(!parent.TryReadCompletion(out _),"Parent completed before its inline stores/normal return.");
    if(at>=2){var child=observations.Single(o=>o.Role==NativePlaybackSourceRole.SetClip);
     Check(child.ParentSequence==parent.Sequence&&child.Depth==parent.Depth+1&&child.ThreadId==parent.ThreadId
      &&child.OwnerEpoch==parent.OwnerEpoch&&child.Caller==IntPtr.Add(module,0x2c41)&&child.Self==source&&child.ReferenceArgument==clip&&child.IncomingMethod==IntPtr.Zero,"Real clip parent/argument/caller differs.");
     Check(child.TryReadCompletion(out var physical)&&physical is{PhysicalExited:true,Returned:true,Abnormal:false},"Child was not really exited before inline stores.");
    }
   };
   Export<SetBoundary>(module,"StockFixtureSetStartBoundary")(boundary);
   if(mode=="seh")Marshal.WriteInt32(e,0x34,3);
   if(mode=="seh"){
    Check(invoke(start.Address,e,start.MethodInfo,out var raw,out var code)==2&&code==0xe2400033&&raw==0,"Actual parent SEH did not propagate precisely.");
    var parent=observations.Last(o=>o.Role==NativePlaybackSourceRole.ProgressStartMoveNext);
    Check(parent.TryReadCompletion(out var physical)&&physical is{PhysicalExited:true,Abnormal:true,Returned:false,HasRawReturnByte:false},"SEH lacks abnormal finally receipt or retained an AL witness.");
    Check(!group.IsActive,"Actual abnormal Start did not preserve physical sticky withdrawal.");
   }else{
    Check(Call()==0&&boundaries.SequenceEqual(new uint[]{1,2,3,4}),"Actual state1 did not run the full direct clip/store sequence/raw0.");
    var parent=observations.Last(o=>o.Role==NativePlaybackSourceRole.ProgressStartMoveNext);
    Check(parent.TryReadCompletion(out var physical)&&physical is{PhysicalExited:true,Returned:true,Abnormal:false,RawReturnByte:0},"Normal actual state1 lacks final AL0 physical-exit receipt.");
    var nextPlayer=Marshal.ReadIntPtr(progress,0x38);
    Check(nextPlayer!=player&&Marshal.ReadIntPtr(nextPlayer,0x10)==source&&Marshal.ReadIntPtr(source,40)==clip,"Two actual direct stores did not produce the expected graph.");
    var initialized=group.ReadInitialization();
    Check(initialized.Blocked==1&&group.ReadSourceHealth() is{Known:true,Armed:true,InFlight:0},"Final exit either cleared hold or stopped observing physical sources.");
    if(mode is "late" or "interference")Check(initialized.Phase==5&&initialized.Receipt==0,"Incomplete/contended initialization signed a fresh candidate.");
    else Check(initialized.Phase==4&&initialized.Receipt!=0&&initialized.Player==unchecked((ulong)nextPlayer.ToInt64())
      &&initialized.Source==unchecked((ulong)source.ToInt64())&&initialized.Clip==unchecked((ulong)clip.ToInt64()),"Exact state0/state1/direct-child/graph did not yield a native candidate after physical exit.");
    Reject(group.RequireFreshAdmission,"Diagnostic initializer receipt automatically granted fresh capture.");
    Check(nativeTime(player,targets.GetTime.MethodInfo)==3,"Completed-but-unadmitted initialization revived old Clock.");
    if(mode is "raw")foreach(var raw in new uint[]{0,1,128,255}){Marshal.WriteInt32(e,0x10,-1);Marshal.WriteInt32(e,0x28,1);Marshal.WriteInt32(e,0x2c,(int)raw);Check(Call()==raw,"AL was normalized or changed.");Check(callbackRaw[^1]==raw,"Observer AL differs from returned raw byte.");}
    if(mode is "raw"){var before=Marshal.AllocHGlobal(256);var after=Marshal.AllocHGlobal(256);
    try{Export<Machine>(module,"StockFixtureMachineInvoke")(7,start.Address,e,0,start.MethodInfo,before,after);
     var b=new byte[256];var f=new byte[256];Marshal.Copy(before,b,0,256);Marshal.Copy(after,f,0,256);
     Check(b.AsSpan(0,64).SequenceEqual(f.AsSpan(0,64))&&b.AsSpan(80,160).SequenceEqual(f.AsSpan(80,160)),"Actual Start/private relocation changed preserved RBP/RSI/other GPR/XMM state.");
    }finally{Marshal.FreeHGlobal(before);Marshal.FreeHGlobal(after);}
    Check(Marshal.ReadInt32(e,0x30)==7&&parentReturnedStillInflight==7,"Start original did not execute exactly once per actual native call.");
    }
    Check(group.ReadSourceHealth() is{Known:true,InFlight:0},"Native scopes/journals did not really drain.");
   }
   foreach(var o in observations)o.Dispose();group.PumpCompleted();
   GC.KeepAlive(boundary);
   Console.WriteLine(JsonSerializer.Serialize(new{mode,passed=true,checks=_checks,sourceVersion=4,frameBytes=168,infoBytes=136,writerVersion=1,initializationProtocol=2,freshBefore,freshReturned,completeRecapture=false,sameRunTransfer=false,actualPrepare=12,actualCommit=12,rawBytes=callbackRaw,actualWindowsUnwindBoundaries=new[]{0,5,6,16},gameExecuted=false,productionAdmission=false}));return 0;
  }catch(Exception error){Console.Error.WriteLine(error);Console.WriteLine(JsonSerializer.Serialize(new{mode,passed=false,checks=_checks,failure=error.ToString()}));return 1;}
 }
}
