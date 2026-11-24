using System.Reflection;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static class HubCases
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ConcurrentQueue<string> Failures = new();
    private static readonly List<NativeRuntime.RootHandle> Roots = new();
    private static readonly List<object> ConsumerCalls = new();
    private static PlaybackSourceGroupLease Group = null!;
    private static NativePlaybackStockSourceHub Hub = null!;
    private static NativePlaybackSingleReturnBridge Bridge = null!;
    private static NativePlaybackSingleReturnBridge.FreshSourceReceipt Ticket = null!;
    private static NativePlaybackSingleReturnBridge.FreshClockReceipt Clock = null!;
    private static NativePlaybackStockOccurrenceProducer Producer = null!;
    private static NativePlaybackSingleReturnAuthority Authority = null!;
    private static NativePlaybackStockSourceHub.ProgressResourceTicket? InitializerResource;
    private static FakeNative Api = null!;
    private static readonly object Owner = new();
    private static int Checks, Rejects, ClockReads, CallbackMode, BusyCallbacks, SourceBefores, SourceReturns;
    private static NativePlaybackSingleReturnFrame LastGetTimeFrame;
    private static NativePlaybackSingleReturnCompletion LastGetTimeCompletion;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Bind(IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind, IntPtr target, IntPtr self, ulong bits, IntPtr method, out uint exception);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InvokeStart(IntPtr target, IntPtr self, IntPtr method, out uint raw, out uint exception);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float ReadPitch(IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float ReadGetTime(IntPtr player, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong ReadConstructorCount(uint field);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ActionNative(IntPtr receipt);
    [DllImport("kernel32", CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    private static T Export<T>(IntPtr module,string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
    private static T Field<T>(object value,string name) => (T)value.GetType().GetField(name,Private)!.GetValue(value)!;
    private static JsonElement Input(string key) => Program.Binding.GetProperty("native").GetProperty(key);
    private static string PathOf(string key) => Input(key).GetProperty("path").GetString()!;
    private static string HashOf(string key) => Input(key).GetProperty("sha256").GetString()!;
    private static void Check(bool value,string reason) { ++Checks; if(!value) throw new InvalidOperationException(reason); }
    private static void Reject(Action action,string reason)
    { ++Checks; try {action();} catch(UnsupportedSignatureException) {++Rejects;return;}
        catch(InvalidOperationException) {++Rejects;return;} throw new InvalidOperationException(reason); }
    private sealed class MainThread : IMainThreadDispatcher
    {
        private readonly int _thread=Environment.CurrentManagedThreadId;
        public bool IsMainThread=>Environment.CurrentManagedThreadId==_thread;
        public void VerifyAccess(){if(!IsMainThread)throw new InvalidOperationException("Wrong thread");}
        public Task InvokeAsync(Action action,CancellationToken cancellationToken=default){VerifyAccess();action();return Task.CompletedTask;}
    }
    private sealed class Logger:ILogger {public void Log(LogLevel level,string message,Exception? error=null){}}
    private static void GetTimeBefore(NativePlaybackSingleReturnFrame frame)
    {
        try
        {
            if (CallbackMode == 1) { ++BusyCallbacks; Check(!Clock.TryPrepare(), "Actual GetTime borrower prevents preparation"); }
            if (CallbackMode == 2) { ++BusyCallbacks; Check(!Clock.TryArm(), "Actual GetTime borrower prevents Arm"); }
            if (CallbackMode == 3)
            {
                Check(Clock.Withdraw(Authority.Revision), "Exact receipt withdraws inside actual GetTime");
                Check(!Ticket.TryRelease(), "Original ticket remains until actual GetTime finally");
                Check(Api.ActiveRoots == 4, "Actual borrower still owns all original test receiver roots");
            }
        }
        catch (Exception error) { Failures.Enqueue(error.ToString()); }
    }
    private static void SourceReturned(NativePlaybackSourceCompletion completion)
    {
        ++SourceReturns;
        Producer.ObserveReturned(completion);
        if (CallbackMode != 4 || completion.Observation.Role != NativePlaybackSourceRole.Pause) return;
        Check(!Producer.Current!.PhysicalExited, "Successor publication occurs after Returned and before actual finally");
        var next = Authority with { Revision=2, MediaAnchorSeconds=3 };
        Clock.Publish(1,next); Authority=next;
    }
    private static void SourceBefore(NativePlaybackSourceObservation observation)
    {
        ++SourceBefores;
        if (CallbackMode == 5 && observation.Role == NativePlaybackSourceRole.ProgressStartMoveNext
            && Group.ReadInitialization().Phase == 1)
            InitializerResource = Group.CaptureProgressResource(Owner, Api.GameplayApi);
        Producer.ObserveBefore(observation);
    }

    private static void VerifyConsumerCall(string variant, IntPtr exactCaller, IntPtr incomingMethod,
        bool sameCallsite, uint expectedDisposition, Action call)
    {
        const uint appliedCandidate = 1;
        var health = Bridge.ReadHealth();
        var originals = Api.UnityGetTimeOriginalCalls;
        var readers = ClockReads;
        var expectedApplications = expectedDisposition == appliedCandidate ? 1u : 0u;
        var sequence = LastGetTimeFrame.Sequence;
        call();
        var after = Bridge.ReadHealth();
        Check(LastGetTimeFrame.Sequence > sequence && LastGetTimeFrame.Sequence == LastGetTimeCompletion.Sequence
            && LastGetTimeFrame.Self == Api.GameplayPlayer && LastGetTimeFrame.IncomingMethod == incomingMethod
            && (LastGetTimeFrame.Caller == exactCaller) == sameCallsite && Api.UnityLastGetTimeMethod == incomingMethod,
            variant + " changed the actual call frame or rewrote the original MethodInfo argument");
        Check(after.Known && after.InFlight == 0 && after.OriginalCalls == health.OriginalCalls + 1
            && Api.UnityGetTimeOriginalCalls == originals + 1 && ClockReads == readers + expectedApplications
            && after.Applied == health.Applied + expectedApplications && LastGetTimeCompletion.Disposition == expectedDisposition,
            variant + " changed original execution, Clock admission or final native application");
        if (expectedApplications != 0)
            Check(LastGetTimeCompletion.CandidateBits == BitConverter.SingleToUInt32Bits((float)Authority.MediaAnchorSeconds)
                && LastGetTimeCompletion.CandidateBits != LastGetTimeCompletion.OriginalBits,
                "The exact null caller applied its retained Fresh media value");
        ConsumerCalls.Add(new { variant, sameCallsite, incomingNull = incomingMethod == IntPtr.Zero,
            originalCalls = after.OriginalCalls - health.OriginalCalls, clockReads = ClockReads - readers,
            applied = after.Applied - health.Applied, disposition = LastGetTimeCompletion.Disposition });
    }

    internal static int Run(string mode)
    {
        Exception? failure = null; bool passed = false, retired = false; ulong epoch = 0;
        try
        {
            var consumer = mode is "consumer" or "consumer-worker-watch";
            Check(consumer || mode is "arm" or "cancel" or "progress" or "handoff" or "initialization" or "cross-initialization" or "timewriter", "Known Fresh Clock case");
            Program.VerifyLoaded(typeof(NativeRuntime).Assembly); Program.VerifyLoaded(typeof(ILogger).Assembly);
            Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && GetModuleHandle("UnityPlayer.dll") == IntPtr.Zero, "Owned native fixture only");
            foreach (var row in Program.Binding.GetProperty("native").EnumerateObject())
            {
                var path=row.Value.GetProperty("path").GetString()!;
                Check(Path.IsPathFullyQualified(path) && new FileInfo(path).Length == row.Value.GetProperty("bytes").GetInt64()
                    && Program.Hash(path) == row.Value.GetProperty("sha256").GetString(), "Exact native input " + row.Name);
            }
            var wrapper=NativeLibrary.Load(PathOf("wrapper")); var lower=NativeLibrary.Load(PathOf("lower")); var single=NativeLibrary.Load(PathOf("single"));
            Check(Export<Bind>(wrapper,"StockFixtureBindLowerPitch")(NativeLibrary.GetExport(lower,"StockLowerSetPitch")) == 1, "Exact pitch cache");
            Check(Export<Bind>(wrapper,"StockFixtureBindLowerTime")(NativeLibrary.GetExport(lower,"StockLowerSetTime")) == 1, "Exact time cache");
            Api=new FakeNative(); Api.ConfigureUnityBacking(wrapper);
            var targets=PlaybackSourceGroupTargets.ResolveOwnedLowerTimeFixture(wrapper,lower);
            var engine=new DobbyDetourEngine(PathOf("dobby")); var broker=new ExactHookBroker(engine,new MainThread(),new Logger());
            Group=broker.BindPlaybackSourceGroup(targets,PathOf("single"),HashOf("single"),PathOf("source"),HashOf("source"),
                ()=> { ++ClockReads; return new(Authority.Identity, Authority.Revision, Authority.Phase, 10); },
                fault=>Failures.Enqueue(fault.ToString()!),fault=>Failures.Enqueue(fault.ToString()!),
                sourceBefore:SourceBefore, sourceAfter:SourceReturned, getTimeBefore:GetTimeBefore,
                getTimeAfter: (frame, completion) => { LastGetTimeFrame = frame; LastGetTimeCompletion = completion; });
            var install=Field<object>(Group,"_owner"); Hub=Field<NativePlaybackStockSourceHub>(install,"_hub");
            Bridge=Field<NativePlaybackSingleReturnBridge>(Field<object>(install,"_getTime"),"_bridge");
            Check(engine.ActualPreparedCallCounts == (23L,23L) && Group.IsActive, "Actual source and GetTime physical members installed");
            foreach(var p in new[]{Api.GameplayPlayer,Api.GameplaySource,Api.GameplayProgress,Api.GameplayClip}) Roots.Add(Api.GameplayApi.Root(p));
            var watch=Hub.StageFreshSource(Owner,Api.GameplayApi,Roots[0],Roots[1],Roots[2],Roots[3]);
            Ticket=Bridge.PrepareFreshSource(Hub,watch);
            Check(Ticket.TryIssue() && Ticket.TryClaim(), "Original fresh source issued and claimed"); epoch=Ticket.Read().SourceEpoch;
            Producer=new(Group,Api.GameplayPlayer,Api.GameplaySource,Api.GameplayProgress);
            var invoke=Export<Invoke>(wrapper,"StockFixtureInvoke");
            var role=mode == "progress" ? NativePlaybackSourceRole.ProgressPlay : NativePlaybackSourceRole.Pause;
            var pause=targets.SourceTarget(role);
            // Set only owned fixture input storage, then require the actual
            // detoured original, PauseLeaf and both native finally rows.
            Marshal.WriteInt32(Api.GameplaySource, Api.UnitySourceField(48), 1);
            Check(invoke(mode == "progress" ? 5u : 1u,pause.Address,mode == "progress" ? Api.GameplayProgress : Api.GameplayPlayer,
                mode == "progress" ? 1ul : 0ul,pause.MethodInfo,out var ex) == 1 && ex == 0, "Actual original Pause path returns");
            var execution=Producer.Current ?? throw new InvalidOperationException("Actual producer did not issue Pause: " + Producer.LastRejection);
            Check(execution.PhysicalExited, "Root and exact PauseLeaf finally journal completed");
            var exactCaller = IntPtr.Zero;
            if (consumer)
            {
                Check(invoke(6, targets.GetTime.Address, Api.GameplayPlayer, 0, IntPtr.Zero, out ex) == 1 && ex == 0,
                    "A dormant original supplies the fixture's actual native GetTime return address");
                exactCaller = LastGetTimeFrame.Caller;
                var offset = exactCaller.ToInt64() - wrapper.ToInt64();
                Check(offset > 0 && offset < 0x10000 && LastGetTimeFrame.IncomingMethod == IntPtr.Zero && ClockReads == 0,
                    "The retained caller must be the fixture's native call site and leave its null argument unchanged");
            }
            Authority=new(new("owned-fresh-clock",1,1,(long)epoch,Api.GameplayController,Api.GameplayInformation,
                Api.GameplayProgress,Api.GameplayPlayer,Api.GameplaySource,Api.GameplayClip,targets.GetTime.MethodInfo),
                1,NativePlaybackSingleReturnPhase.Paused,10,2,1,100,exactCaller);
            Reject(()=>Bridge.PrepareDormant(Authority), "Old dormant API bypassed Fresh ticket");
            Reject(()=>Bridge.Publish(0,Authority), "Old publish API bypassed Fresh ticket");
            Clock=Group.GetTimeLease.PrepareFreshClock(Ticket,execution,Authority);
            Check(!Clock.SupportsHandoff && !Ticket.TryRelease(), "Pending typed publication retains source ticket and disallows handoff");
            CallbackMode=1; Check(invoke(6,targets.GetTime.Address,Api.GameplayPlayer,0,targets.GetTime.MethodInfo,out ex)==1&&ex==0,"GetTime during preparation"); CallbackMode=0;
            Check(Failures.Count==0 && BusyCallbacks==1 && Clock.TryPrepare(), "Same Busy candidate prepares after actual drain");
            var info=Clock.Read(1); Check(info.Epoch==epoch && !Bridge.ReadCurrent().Active && ClockReads==0,"Dormant keeps original epoch without clock reads");
            Check(Export<ActionNative>(single,"RizSingleReturnPrototype_ReleaseFreshSource")(Ticket.Read().Receipt)==2,"Native dormant publication blocks source release");
            var clone=(NativePlaybackSingleReturnBridge.FreshClockReceipt)typeof(object).GetMethod("MemberwiseClone",Private)!.Invoke(Clock,null)!;
            Reject(()=>clone.Read(1),"Cloned publication accepted"); Reject(clone.Cancel,"Cloned cleanup accepted");
            Task.Run(()=> { Reject(()=>Clock.Read(1),"Worker read accepted"); Reject(Clock.Cancel,"Worker cancel accepted"); }).GetAwaiter().GetResult();
            if (mode == "cancel")
            {
                Clock.Cancel(); Check(Clock.IsClosed && ClockReads==0,"Dormant cancellation keeps Clock reader unused");
            }
            else
            {
                CallbackMode=2; Check(invoke(6,targets.GetTime.Address,Api.GameplayPlayer,0,targets.GetTime.MethodInfo,out ex)==1&&ex==0,"GetTime during Arm"); CallbackMode=0;
                Check(Failures.Count==0 && BusyCallbacks==2 && Clock.TryArm(),"Same native ticket arms after real borrower exits");
                Check(Clock.Read(2).Epoch==epoch && Bridge.ReadCurrent().Active,"Armed publication keeps original source epoch");
                Check(invoke(6,targets.GetTime.Address,Api.GameplayPlayer,0,targets.GetTime.MethodInfo,out ex)==1&&ex==0 && ClockReads==1,"Actual GetTime applies first Fresh Clock");
                void CallFixture(IntPtr method) => Check(invoke(6, targets.GetTime.Address, Api.GameplayPlayer, 0, method, out var code) == 1 && code == 0,
                    "The actual fixture GetTime original returned normally");
                if (consumer)
                {
                    const uint clockCandidate = 1, withdrawn = 6, wrongPhysicalIdentity = 8;
                    var otherCaller = Marshal.GetDelegateForFunctionPointer<ReadGetTime>(targets.GetTime.Address);
                    var foreignMethod = IntPtr.Add(targets.GetTime.MethodInfo, 8);
                    VerifyConsumerCall("foreign caller with null metadata", exactCaller, IntPtr.Zero, false, wrongPhysicalIdentity,
                        () => Check(BitConverter.SingleToUInt32Bits(otherCaller(Api.GameplayPlayer, IntPtr.Zero)) == LastGetTimeCompletion.OriginalBits,
                            "Foreign caller preserves the exact original Single return"));
                    VerifyConsumerCall("registered caller with foreign metadata", exactCaller, foreignMethod, true, wrongPhysicalIdentity,
                        () => CallFixture(foreignMethod));
                    VerifyConsumerCall("worker with registered null caller", exactCaller, IntPtr.Zero, true, withdrawn,
                        () => Task.Run(() => CallFixture(IntPtr.Zero)).GetAwaiter().GetResult());
                    VerifyConsumerCall("owner with registered null caller", exactCaller, IntPtr.Zero, true, clockCandidate,
                        () => CallFixture(IntPtr.Zero));
                    Check(Ticket.Read().SourceEpoch == epoch && Clock.Read(2).Epoch == epoch,
                        "Caller rejection and recovery preserve the one claimed source and Clock epoch");
                }
                if (mode == "consumer-worker-watch")
                {
                    var ctor = targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor);
                    var constructorCount = Export<ReadConstructorCount>(wrapper, "StockFixtureConstructorRead");
                    var originalEntries = constructorCount(0); var originalReturns = constructorCount(1);
                    var sourceBefore = Hub.ReadHealth();
                    var callbacks = (SourceBefores, SourceReturns);
                    var readers = ClockReads;
                    var player = Api.GameplayPlayer; var audio = Api.GameplaySource;
                    Check(Task.Run(() => invoke(4, ctor.Address, player, unchecked((ulong)audio.ToInt64()),
                        ctor.MethodInfo, out var code) == 1 && code == 0).GetAwaiter().GetResult(),
                        "The worker's actual rooted-player constructor returned normally");
                    var sourceAfter = Hub.ReadHealth();
                    Check(sourceAfter.Unknown && sourceAfter.InFlight == 0 && sourceAfter.Entered == sourceBefore.Entered + 1
                        && sourceAfter.Exited == sourceBefore.Exited + 1 && sourceAfter.SourceSequence == sourceBefore.SourceSequence + 1
                        && sourceAfter.WriterSequence == sourceBefore.WriterSequence && sourceAfter.SchedulingSequence == sourceBefore.SchedulingSequence,
                        "The watched worker call did not record the native source fault and complete its original physical journal");
                    Check(constructorCount(0) == originalEntries + 1 && constructorCount(1) == originalReturns + 1
                        && Marshal.ReadIntPtr(player, 16) == audio && (SourceBefores, SourceReturns) == callbacks,
                        "The original worker call or managed observer suppression changed");
                    // No managed owner pump before the exact null caller resumes.
                    var timeBefore = Bridge.ReadHealth();
                    CallFixture(IntPtr.Zero);
                    var timeAfter = Bridge.ReadHealth();
                    Check(LastGetTimeFrame.Caller == exactCaller && Api.UnityLastGetTimeMethod == IntPtr.Zero
                        && timeAfter.OriginalCalls == timeBefore.OriginalCalls + 1 && timeAfter.Applied == timeBefore.Applied
                        && !timeAfter.AuthorityActive && !timeAfter.Known && ClockReads == readers,
                        "The exact caller reused Fresh Clock authority after the watched worker invalidated its source");
                    Reject(() => Ticket.Read(), "The actual worker fault left the original Fresh source readable");
                    Reject(() => Clock.Read(2), "The faulted Clock ticket still reported active authority");
                    // Releasing the epoch ticket is separate from proving that
                    // both physical channels are known enough to retire roots.
                    Check(Clock.IsClosed && Ticket.TryRelease(), "Owner cleanup did not revoke the actual Clock and source tickets");
                    var activity = Group.ReadActivity();
                    Check(!activity.Known && activity.InFlight == 0 && Api.ActiveRoots == 4 && !Group.ProductionAdmission,
                        "Ticket revocation converted physical Unknown into a root retirement proof");
                    Console.WriteLine(JsonSerializer.Serialize(new { kind = "fresh-clock-owned-native", mode, passed = true, checks = Checks,
                        nativeSourceEpoch = epoch, consumerCalls = ConsumerCalls, workerOriginalCalls = constructorCount(0) - originalEntries,
                        workerPhysicalExits = sourceAfter.Exited - sourceBefore.Exited, workerManagedCallbacks = 0,
                        clockReadsAfterFault = ClockReads - readers, retainedRoots = Api.ActiveRoots,
                        nativeUnknown = sourceAfter.Unknown && !timeAfter.Known, productionAdmission = false, stockRuntimeTested = false, gameExecuted = false }));
                    return 0; // Process teardown owns the still retained Unknown fixture resources.
                }
                var successor=targets.SourceTarget(NativePlaybackSourceRole.Pause);
                Marshal.WriteInt32(Api.GameplaySource, Api.UnitySourceField(48), 1);
                CallbackMode=4; Check(invoke(1,successor.Address,Api.GameplayPlayer,0,successor.MethodInfo,out ex)==1&&ex==0,"Actual successor Pause publishes before finally"); CallbackMode=0;
                Check(Clock.Read(2).CurrentGeneration==2 && !Clock.Withdraw(1),"Conditional publish preserves exact ownership and refuses old revision cleanup");
                if (mode == "timewriter")
                {
                    var actualPitch = Export<ReadPitch>(wrapper,"StockFixtureNativeGetPitch");
                    var paused = Producer.Current!;
                    var originalTime = Api.GameplayApi.Unbox<float>(Api.GameplayApi.Call(Api.GameplaySource,"get_time"));
                    var pitched = Producer.WritePitch(paused,1.5f);
                    var beforeTime = Group.ReadSourceHealth();
                    var ratePolicy = Authority with { Revision=3, Rate=1.5 };
                    Clock.Publish(Authority.Revision,ratePolicy); Authority=ratePolicy;
                    var positioned = pitched.WriteTime(4.25f);
                    Check(positioned.WrittenRole==NativePlaybackSourceRole.SetTime && positioned.TimeSeconds==4.25f
                        && positioned.RootPhysicalExited, "Actual time writer returns the same paused root's typed continuation");
                    Check(positioned.WriterSequence==beforeTime.WriterSequence && positioned.SourceSequence==beforeTime.SourceSequence
                        && positioned.SchedulingSequence==beforeTime.SchedulingSequence+1,
                        "Actual SetTime advances Scheduling once and preserves source/pitch fences");
                    Check(Api.GameplayApi.Unbox<float>(Api.GameplayApi.Call(Api.GameplaySource,"get_time"))==4.25f
                        && actualPitch(Api.GameplaySource)==1.5f,
                        "Independent time API and native backing getter observe seek position and retained selected rate");
                    var positionPolicy=Authority with { Revision=4, MediaAnchorSeconds=4.25 };
                    Clock.Publish(Authority.Revision,positionPolicy); Authority=positionPolicy;
                    Check(Clock.Read(2).CurrentGeneration==4,"Fresh Clock remains valid after its exact time continuation");
                    Reject(()=>Producer.WriteTime(paused,2),"Old execution bypassed the current writer continuation");
                    Reject(()=>positioned.WriteTime(float.NaN),"Nonfinite time acquired native authority");
                    Reject(()=>positioned.WriteTime(-1),"Negative time acquired native authority");
                    var copied=(NativePlaybackStockOccurrenceProducer.ContinuationReceipt)typeof(object)
                        .GetMethod("MemberwiseClone",Private)!.Invoke(positioned,null)!;
                    Reject(()=>copied.WriteTime(1),"A copied continuation restored audio time");
                    positioned.VerifyCurrent();
                    var restored=positioned.WriteTime(originalTime);
                    Check(Api.GameplayApi.Unbox<float>(Api.GameplayApi.Call(Api.GameplaySource,"get_time"))==originalTime
                        && actualPitch(Api.GameplaySource)==1.5f,
                        "The exact successful time continuation restores prior audio position without changing selected pitch");
                    var finalWriter=restored.WritePitch(1.5f).WriteTime(0);
                    Check(finalWriter.TimeSeconds==0 && Api.GameplayApi.Unbox<float>(Api.GameplayApi.Call(Api.GameplaySource,"get_time"))==0
                        && actualPitch(Api.GameplaySource)==1.5f,
                        "Mixed pitch/time successors retain one real native chain including zero time");
                }
                if (mode == "consumer")
                {
                    Check(Api.MutateUnityClip(4, -442), "Change only the actual native clip instance identity");
                    VerifyConsumerCall("registered null caller after backing invalidation", exactCaller, IntPtr.Zero, true, 6,
                        () => CallFixture(IntPtr.Zero));
                    Check(!Bridge.ReadCurrent().Active && Api.ActiveRoots == 4,
                        "Backing invalidation withdraws Clock authority while its original roots remain retained");
                }
                CallbackMode=3; Check(invoke(6,targets.GetTime.Address,Api.GameplayPlayer,0,targets.GetTime.MethodInfo,out ex)==1&&ex==0,"Withdrawal preserves actual GetTime original"); CallbackMode=0;
                Check(Clock.IsClosed && !Bridge.ReadCurrent().Active,"Exact Fresh Clock closed before source release");
            }
            Producer.Dispose(); Check(Producer.OwnResponsibilitiesDrained,"Actual producer journal released after closure");
            Check(Ticket.TryRelease(),"Original source ticket released after publication and actual drain");
            if (mode is not ("initialization" or "cross-initialization"))
            {
                retired=Group.TryRetirePlayerWatch(Owner); Check(retired,"Original three watches retired by original owner");
                foreach(var root in Roots.AsEnumerable().Reverse()) root.Dispose(); Roots.Clear(); Check(Api.ActiveRoots==0,"Original receiver roots released last");
            }
            if (mode is "handoff" or "initialization" or "cross-initialization")
            {
                Check(Clock.SupportsCrossRunHandoff && Clock.SupportsInitializationHandoff,
                    "Previously armed withdrawn Clock retains only its typed predecessor rights");
                Reject(()=>Group.GetTimeLease.BeginLogicalHandoff(Authority), "Legacy handoff bypassed the original Fresh Clock ticket");
                Task.Run(()=>Reject(()=>Clock.BeginHandoff(), "Worker Fresh handoff accepted")).GetAwaiter().GetResult();
                NativeRuntime.RootHandle? initializerRoot = null;
                NativePlaybackStockSourceHub.CompletedInitializationReceipt? initializer = null;
                if (mode != "handoff")
                {
                    initializerRoot = Api.GameplayApi.Root(Api.GameplayProgress);
                    Check(Group.TryCaptureCompletedInitializer(Api.GameplayApi, initializerRoot) is null,
                        "No real completed Start is pending, without issuing authority");
                    var start = Export<InvokeStart>(wrapper,"StockFixtureInvokeStart");
                    var startTarget = targets.ProgressStartMoveNext;
                    CallbackMode=5;
                    Check(start(startTarget.Address,Api.UnityInitialStart,startTarget.MethodInfo,out var first,out ex)==1
                        && ex==0 && first==1 && Group.ReadInitialization() is { Phase:2, Blocked:1 },
                        "Actual Start yields before the new run observer exists");
                    Check(Group.TryCaptureCompletedInitializer(Api.GameplayApi,initializerRoot) is null,
                        "The same retained Progress waits for the actual final Start");
                    Check(start(startTarget.Address,Api.UnityInitialStart,startTarget.MethodInfo,out var final,out ex)==1
                        && ex==0 && final==0 && Group.ReadInitialization() is { Phase:4, Blocked:1 },
                        "Actual Start finishes set_clip and new player with physical exits");
                    CallbackMode=0;
                    Api.AdoptUnityInitializedPlayer(); Api.AdoptUnityClip(Api.GameplayClip);
                    initializer = Group.TryCaptureCompletedInitializer(Api.GameplayApi,initializerRoot)
                        ?? throw new InvalidOperationException("Actual completed Start was not issued");
                    var completed = initializer.Read();
                    Check(completed.Player==unchecked((ulong)Api.GameplayPlayer.ToInt64())
                        && completed.Source==unchecked((ulong)Api.GameplaySource.ToInt64())
                        && completed.Clip==unchecked((ulong)Api.GameplayClip.ToInt64()),
                        "Typed completed ticket retains its actual new graph");
                    Check(ReferenceEquals(initializer,Group.TryCaptureCompletedInitializer(Api.GameplayApi,initializerRoot)),
                        "Rechecking the original root retains exactly one issuer object");
                    var copied=(NativePlaybackStockSourceHub.CompletedInitializationReceipt)typeof(object)
                        .GetMethod("MemberwiseClone",Private)!.Invoke(initializer,null)!;
                    Reject(()=>copied.Read(),"Copied initializer became authority");
                    Reject(()=>Clock.BeginHandoff(),"Ordinary transfer bypassed the blocked actual Start");
                }
                var transfer = mode == "handoff" ? Clock.BeginHandoff()
                    : mode == "initialization" ? Clock.BeginInitializationHandoff(initializer!)
                    : Clock.BeginCrossRunInitializationHandoff(initializer!);
                Check(Clock.HasTransferred && Clock.IsClosed && !Clock.SupportsHandoff && transfer.Read(1).OldEpoch == epoch,
                    "Actual transfer keeps the original Fresh predecessor epoch");
                Reject(()=>Clock.BeginHandoff(), "Fresh predecessor replay accepted");
                Check(transfer.TryAdmit(), "Actual two-entry drain admits successor");
                var admitted = transfer.Read(2);
                if (initializer is not null)
                {
                    initializer.VerifyTransferred(transfer);
                    Check(transfer.IsCrossRunInitialization == (mode == "cross-initialization"),
                        "Exact initializer handoff retains its actual run transition kind");
                    Reject(()=>initializer.Read(),"Consumed completed initializer could be recaptured");
                }
                Check(admitted.NewEpoch > epoch && admitted.InitialRevision == (ulong)Authority.Revision + 1,
                    "Actual successor receives new logical epoch and exact next revision");
                // These owned cases reuse the retained controller/Progress.
                // Initializer modes use the player allocated by actual Start;
                // no case claims a new-controller game run.
                var successorOwner = new object();
                var successorRoots = new[]{Api.GameplayPlayer,Api.GameplaySource,Api.GameplayProgress,Api.GameplayClip}.Select(Api.GameplayApi.Root).ToArray();
                Check(Group.StagePlayerWatch(successorOwner,Api.GameplayApi,successorRoots[0],successorRoots[1],successorRoots[2],successorRoots[3]),
                    "Successor stages its own watch trio under the actual closed transfer gate");
                Check(transfer.TryOpenSource(), "Actual transfer opens registered successor source");
                initializer?.VerifyOpened(transfer); initializerRoot?.Dispose();
                if (initializer is not null)
                {
                    InitializerResource!.Dispose();
                    retired=Group.TryRetirePlayerWatch(Owner); Check(retired,"Old initializer watches retire only after successor source-open");
                    foreach(var root in Roots.AsEnumerable().Reverse()) root.Dispose(); Roots.Clear();
                }
                Roots.AddRange(successorRoots);
                Group.VerifyActivePlayerWatch(successorOwner);
                Producer = new(Group,Api.GameplayPlayer,Api.GameplaySource,Api.GameplayProgress);
                Marshal.WriteInt32(Api.GameplaySource, Api.UnitySourceField(48), 1);
                var nextPause = targets.SourceTarget(NativePlaybackSourceRole.Pause);
                Check(invoke(1,nextPause.Address,Api.GameplayPlayer,0,nextPause.MethodInfo,out ex)==1&&ex==0
                    && Producer.Current is { PhysicalExited: true }, "Successor produces its own actual exited Pause baseline");
                var successorInitial = Authority with {
                    Identity = Authority.Identity with {
                        RunId=mode == "initialization" ? Authority.Identity.RunId : "owned-fresh-successor",
                        ControllerGeneration=mode == "initialization" ? Authority.Identity.ControllerGeneration : 2,
                        SourceGeneration=(long)admitted.NewEpoch, Player=Api.GameplayPlayer, AudioSource=Api.GameplaySource, Clip=Api.GameplayClip },
                    Revision=(long)admitted.InitialRevision };
                transfer.Prepare(successorInitial); Authority=successorInitial;
                Check(!Bridge.ReadCurrent().Active && transfer.Read(4).NewEpoch == admitted.NewEpoch,
                    "Transferred successor policy remains dormant");
                Check(transfer.TryArm(), "Actual transferred successor arms");
                Group.BindLogicalHandoff();
                Check(invoke(6,targets.GetTime.Address,Api.GameplayPlayer,0,targets.GetTime.MethodInfo,out ex)==1&&ex==0
                    && ClockReads==2, "Actual GetTime consumes successor publication after Fresh transfer");
                var next = Authority with { Revision=Authority.Revision+1, MediaAnchorSeconds=4 };
                Group.GetTimeLease.Publish(Authority.Revision,next); Authority=next;
                Reject(()=>Clock.Withdraw(Authority.Revision), "Old Fresh cleanup revoked successor publication");
                Check(Ticket.TryRelease() && Bridge.ReadCurrent().Active, "Old released source ticket cannot affect active successor");
                Check(Group.GetTimeLease.Withdraw(Authority.Revision), "Successor retains normal exact cleanup ownership");
                Producer.Dispose(); Check(Producer.OwnResponsibilitiesDrained, "Successor producer's actual journal drains");
                Check(Group.TryRetirePlayerWatch(successorOwner), "Successor retires only its own watches");
                foreach(var root in Roots.AsEnumerable().Reverse()) root.Dispose(); Roots.Clear();
                Check(Api.ActiveRoots==0, "Both successive logical sources released their roots after actual drain");
            }
            Check(Failures.Count==0 && Group.ReadActivity().Known && Group.ReadActivity().InFlight==0,"No callback faults or remaining physical borrowers");
            Check(!Group.ProductionAdmission && !Producer.ProductionAdmission,"Owned protocol check grants no production admission");
            Api.ReleaseUnityBackingStorage(); Api.Dispose(); passed=true;
        }
        catch(Exception error) { failure=error; }
        finally { GC.KeepAlive(Api); GC.KeepAlive(Clock); GC.KeepAlive(Ticket); GC.KeepAlive(Roots); }
        Console.WriteLine(JsonSerializer.Serialize(new {kind="fresh-clock-owned-native",mode,passed,checks=Checks,rejections=Rejects,
            nativeSourceEpoch=epoch,busyCallbacks=BusyCallbacks,clockReads=ClockReads,watchRetired=retired,rootsRetained=Roots.Count,
            consumerCalls=ConsumerCalls,productionAdmission=false,stockRuntimeTested=false,gameExecuted=false,failures=Failures,error=failure?.ToString()}));
        return passed?0:1;
    }
}
