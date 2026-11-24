using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static class HubCases
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> Failures = new();
    private static readonly List<NativeRuntime.RootHandle> Roots = new();
    private static PlaybackSourceGroupLease Group = null!;
    private static NativePlaybackStockSourceHub Hub = null!;
    private static NativePlaybackSingleReturnBridge Bridge = null!;
    private static NativePlaybackSingleReturnBridge.FreshSourceReceipt? Ticket;
    private static NativePlaybackStockSourceHub.PlayerWatch Watch = null!;
    private static FakeNative Api = null!;
    private static readonly object Owner = new();
    private static int Checks, Rejects, ClockReads, CallbackMode, BusyCallbacks;
    private static IntPtr PendingReceipt, Single;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Bind(IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind,IntPtr target,IntPtr self,ulong bits,IntPtr method,out uint exception);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Read(IntPtr receipt,ref NativePlaybackSingleReturnBridge.FreshSourceInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ActionNative(IntPtr receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Issue(uint slot,IntPtr owner,IntPtr watch,out IntPtr receipt);
    [DllImport("kernel32",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    private static T Export<T>(IntPtr module,string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
    private static T Field<T>(object value,string name) => (T)value.GetType().GetField(name,Private)!.GetValue(value)!;
    private static JsonElement Input(string key) => Program.Binding.GetProperty("native").GetProperty(key);
    private static string PathOf(string key) => Input(key).GetProperty("path").GetString()!;
    private static string HashOf(string key) => Input(key).GetProperty("sha256").GetString()!;
    private static void Check(bool value,string reason) {++Checks;if(!value)throw new InvalidOperationException(reason);}
    private static void Reject(Action action,string reason)
    {
        ++Checks;try {action();} catch(UnsupportedSignatureException) {++Rejects;return;}
        catch(EntryPointNotFoundException) {++Rejects;return;}
        throw new InvalidOperationException(reason);
    }
    private sealed class MainThread : IMainThreadDispatcher
    {
        private readonly int _thread=Environment.CurrentManagedThreadId;
        public bool IsMainThread=>Environment.CurrentManagedThreadId==_thread;
        public void VerifyAccess(){if(!IsMainThread)throw new InvalidOperationException("Wrong thread");}
        public Task InvokeAsync(Action action,CancellationToken cancellationToken=default){VerifyAccess();action();return Task.CompletedTask;}
    }
    private sealed class Logger:ILogger {public void Log(LogLevel level,string message,Exception? error=null){}}
    private static uint NativeRead(IntPtr receipt)
    {
        var info=new NativePlaybackSingleReturnBridge.FreshSourceInformation{Version=1,Size=208};
        return Export<Read>(Single,"RizSingleReturnPrototype_ReadFreshSource")(receipt,ref info);
    }
    private static void GetTimeBefore(NativePlaybackSingleReturnFrame frame)
    {
        try
        {
            if(CallbackMode==1)
            {
                ++BusyCallbacks;
                Check(!Ticket!.TryIssue(),"Real GetTime in-flight prevents source epoch issue");
                PendingReceipt=Field<IntPtr>(Ticket,"_receipt");
                Check(PendingReceipt!=IntPtr.Zero && NativeRead(PendingReceipt)==8,"Pending read leaves candidate pending and without epoch");
            }
            else if(CallbackMode==2)
            {
                ++BusyCallbacks;Check(!Ticket!.TryRelease(),"Epoch revocation waits for actual GetTime borrower");
                Check(Ticket.Read().Phase==2,"Busy release preserves claimed epoch");
                Check(Api.ActiveRoots==4,"Busy release retains all test receiver roots");
            }
        }
        catch(Exception error){Failures.Add(error.ToString());}
    }
    internal static int Run(string mode)
    {
        bool passed=false,retired=false;Exception? failure=null;ulong epoch=0;DobbyDetourEngine? engine=null;
        try
        {
            Check(mode is "lifetime" or "release" or "old-helper","Known isolated fresh protocol case");
            Program.VerifyLoaded(typeof(NativeRuntime).Assembly);Program.VerifyLoaded(typeof(ILogger).Assembly);
            Check(GetModuleHandle("GameAssembly.dll")==IntPtr.Zero && GetModuleHandle("UnityPlayer.dll")==IntPtr.Zero,"No game engine loaded");
            foreach(var row in Program.Binding.GetProperty("native").EnumerateObject())
            {
                var p=row.Value.GetProperty("path").GetString()!;
                Check(Path.IsPathFullyQualified(p)&&new FileInfo(p).Length==row.Value.GetProperty("bytes").GetInt64()&&Program.Hash(p)==row.Value.GetProperty("sha256").GetString(),"Exact native input "+row.Name);
            }
            var wrapper=NativeLibrary.Load(PathOf("wrapper"));var lower=NativeLibrary.Load(PathOf("lower"));Single=NativeLibrary.Load(PathOf("single"));
            Check(Export<Bind>(wrapper,"StockFixtureBindLowerPitch")(NativeLibrary.GetExport(lower,"StockLowerSetPitch"))==1,"Exact pitch cache");
            Check(Export<Bind>(wrapper,"StockFixtureBindLowerTime")(NativeLibrary.GetExport(lower,"StockLowerSetTime"))==1,"Exact time cache");
            Api=new FakeNative();Api.ConfigureUnityBacking(wrapper);
            var targets=PlaybackSourceGroupTargets.ResolveOwnedLowerTimeFixture(wrapper,lower);
            engine=new DobbyDetourEngine(PathOf("dobby"));var broker=new ExactHookBroker(engine,new MainThread(),new Logger());
            Group=broker.BindPlaybackSourceGroup(targets,PathOf("single"),HashOf("single"),PathOf("source"),HashOf("source"),
                ()=>{++ClockReads;throw new InvalidOperationException("No Clock publication granted");},
                fault=>Failures.Add(fault.ToString()!),fault=>Failures.Add(fault.ToString()!),getTimeBefore:GetTimeBefore);
            var install=Field<object>(Group,"_owner");Hub=Field<NativePlaybackStockSourceHub>(install,"_hub");Bridge=Field<NativePlaybackSingleReturnBridge>(Field<object>(install,"_getTime"),"_bridge");
            Check(engine.ActualPreparedCallCounts==(23L,23L)&&Group.IsActive&&Hub.ReadHealth().Armed,"Actual 23 physical members committed and armed");
            Check(!Group.ProductionAdmission,"Owned fixture never receives production admission");
            Check(Api.ActiveRoots==0,"Dormant physical group has no graph roots");
            if(mode=="old-helper")
            {
                Reject(Hub.RequireFreshSourceProtocol,"Old source helper accepted fresh protocol");
                Reject(Bridge.RequireFreshSourceProtocol,"Old GetTime helper accepted fresh protocol");
                Check(Api.ActiveRoots==0,"Missing native protocol rejects before roots or watch stage");
            }
            else
            {
                Hub.RequireFreshSourceProtocol();Bridge.RequireFreshSourceProtocol();
                foreach(var p in new[]{Api.GameplayPlayer,Api.GameplaySource,Api.GameplayProgress,Api.GameplayClip})Roots.Add(Api.GameplayApi.Root(p));
                Watch=Hub.StageFreshSource(Owner,Api.GameplayApi,Roots[0],Roots[1],Roots[2],Roots[3]);
                Watch.VerifyPhase(Owner,1);Ticket=Bridge.PrepareFreshSource(Hub,Watch);
                Reject(()=>Bridge.PrepareFreshSource(Hub,Watch),"Second initial managed candidate accepted");
                var slot=Field<uint>(Bridge,"_slot");
                Task.Run(()=>
                {
                    Reject(()=>Ticket.TryIssue(),"Worker managed issue accepted");
                    Check(Export<Issue>(Single,"RizSingleReturnPrototype_IssueFreshSource")(slot,Hub.NativeOwner,Watch.NativeReceipt,out var foreign)==10 && foreign==IntPtr.Zero,"Worker native issue creates no candidate");
                }).GetAwaiter().GetResult();
                var invoke=Export<Invoke>(wrapper,"StockFixtureInvoke");CallbackMode=1;
                Check(invoke(6,targets.GetTime.Address,Api.GameplayPlayer,0,targets.GetTime.MethodInfo,out var ex)==1&&ex==0,"Real dormant GetTime returns original");CallbackMode=0;
                Check(BusyCallbacks==1&&Failures.Count==0,"Pending issue exercised actual physical callback");Watch.VerifyPhase(Owner,1);
                Check(Ticket.TryIssue(),"Same pending candidate issues after real drain");
                var issued=Ticket.Read();epoch=issued.SourceEpoch;
                Check(issued.Receipt==PendingReceipt&&issued.Phase==1&&epoch>0,"Same opaque receipt receives native epoch after drain");
                Group.VerifyActivePlayerWatch(Owner);Check(Ticket.TryClaim()&&Ticket.Read().Phase==2,"Actual registered native ticket claimed once");
                Reject(()=>Ticket.TryClaim(),"Replayed managed claim accepted");
                Check(Export<ActionNative>(Single,"RizSingleReturnPrototype_ClaimFreshSource")(issued.Receipt)==2,"Replayed native claim rejected without revoking owner");
                Check(Ticket.Read().Phase==2,"Rejected replay preserves real ticket");
                var clone=(NativePlaybackSingleReturnBridge.FreshSourceReceipt)typeof(object).GetMethod("MemberwiseClone",Private)!.Invoke(Ticket,null)!;
                Reject(()=>clone.Read(),"Cloned managed ticket became authority");Reject(()=>clone.TryRelease(),"Cloned managed cleanup retired owner");
                Check(NativeRead(new IntPtr(1))==1,"Unregistered native address rejected before dereference");
                Task.Run(()=>
                {
                    Reject(()=>Ticket.Read(),"Worker managed read accepted");Reject(()=>Ticket.TryRelease(),"Worker managed release accepted");
                    Check(NativeRead(issued.Receipt)==10,"Worker raw read rejected without tombstone");
                    Check(Export<ActionNative>(Single,"RizSingleReturnPrototype_ReleaseFreshSource")(issued.Receipt)==10,"Worker raw release rejected without revocation");
                }).GetAwaiter().GetResult();
                Check(Ticket.Read().SourceEpoch==epoch,"Owner retains ticket after worker attempts");
                var before=Hub.ReadHealth();var pause=targets.SourceTarget(NativePlaybackSourceRole.Pause);
                Check(invoke(1,pause.Address,Api.GameplayPlayer,0,pause.MethodInfo,out ex)==1&&ex==0,"Actual native Pause returns");Group.PumpCompleted();
                var after=Hub.ReadHealth();Check(after.SchedulingSequence>before.SchedulingSequence&&after.SourceSequence==before.SourceSequence,"Legitimate pause advances scheduling without source lifetime change");
                Check(Ticket.Read().SourceEpoch==epoch,"Fresh epoch survives legitimate scheduling progression");
                var pitch=targets.SourceTarget(NativePlaybackSourceRole.SetPitch);
                Check(invoke(2,pitch.Address,Api.GameplaySource,0x3f800000,pitch.MethodInfo,out ex)==1&&ex==0,"Actual native pitch wrapper returns");Group.PumpCompleted();
                Check(Hub.ReadHealth().WriterSequence>after.WriterSequence&&Hub.ReadHealth().SourceSequence==after.SourceSequence,"Actual wrapper advances writer without source lifetime change");
                Check(Ticket.Read().SourceEpoch==epoch,"Fresh epoch survives legitimate writer progression");
                if(mode=="lifetime")
                {
                    var ctor=targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor);
                    Check(invoke(4,ctor.Address,Api.GameplayPlayer,unchecked((ulong)Api.GameplaySource.ToInt64()),ctor.MethodInfo,out ex)==1&&ex==0,"Actual same-pointer constructor returns");Group.PumpCompleted();
                    Check(Marshal.ReadIntPtr(Api.GameplayPlayer,16)==Api.GameplaySource&&Hub.ReadHealth().SourceSequence>after.SourceSequence,"Observed constructor invalidates lifetime despite equal final audio pointer");
                    Reject(()=>Ticket.Read(),"Old epoch survived actual source fence change");Reject(()=>Ticket.Read(),"Terminal ticket revived on equal fields");
                    Check(NativeRead(issued.Receipt)!=0,"Native registry also rejects stale epoch");
                }
                else
                {
                    CallbackMode=2;Check(invoke(6,targets.GetTime.Address,Api.GameplayPlayer,0,targets.GetTime.MethodInfo,out ex)==1&&ex==0,"Actual claimed dormant GetTime returns");CallbackMode=0;
                    Check(BusyCallbacks==2&&Failures.Count==0,"Release tested real GetTime borrower");
                }
                Check(Ticket.TryRelease(),"Owner revokes registered ticket after actual drain");Reject(()=>Ticket.Read(),"Released ticket read accepted");
                Check(Group.ReadActivity().Known&&Group.ReadActivity().InFlight==0&&Group.GetTimeLease.ReadActivity().Known&&Group.GetTimeLease.ReadActivity().InFlight==0,"Both actual journals known and drained");
                retired=Group.TryRetirePlayerWatch(Owner);Check(retired,"Exact native watch trio retired");
                foreach(var root in Roots.AsEnumerable().Reverse())root.Dispose();Roots.Clear();Check(Api.ActiveRoots==0,"Receiver roots released only after ticket revoke, joint drain and watch retirement");
            }
            Check(ClockReads==0&&Failures.Count==0,"No Clock read or callback faults");Check(GetModuleHandle("GameAssembly.dll")==IntPtr.Zero&&GetModuleHandle("UnityPlayer.dll")==IntPtr.Zero,"No game execution");
            Api.ReleaseUnityBackingStorage();Api.Dispose();passed=true;
        }
        catch(Exception error){failure=error;}
        finally{GC.KeepAlive(Api);GC.KeepAlive(Ticket);GC.KeepAlive(Roots);}
        Console.WriteLine(JsonSerializer.Serialize(new{kind="fresh-source-owned-native",mode,passed,checks=Checks,rejections=Rejects,nativeSourceEpoch=epoch,busyCallbacks=BusyCallbacks,clockReads=ClockReads,watchRetired=retired,rootsRetained=Roots.Count,actualPrepared=engine?.ActualPreparedCallCounts.Item1,actualCommitted=engine?.ActualPreparedCallCounts.Item2,productionAdmission=false,stockRuntimeTested=false,gameExecuted=false,readOnlyReflectionOfIssuedObjects=true,fabricatedOwnerOrReceipt=false,failures=Failures,error=failure?.ToString()}));
        return passed?0:1;
    }
}
