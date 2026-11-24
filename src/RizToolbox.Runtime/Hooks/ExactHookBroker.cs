using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal interface INativeDetourEngine { Delegate Install(VerifiedHookTarget target, Delegate replacement); }
internal interface IPreparedNativeDetourEngine
{
    IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement);
    void Commit(VerifiedHookTarget target);
}
internal sealed partial class DobbyDetourEngine : INativeDetourEngine, IPreparedNativeDetourEngine
{
    private const string PreparedImageSha256 = "8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded";
    private readonly string _path;
    private readonly IntPtr _library;
    private readonly DobbyHook _hook;
    private readonly DobbyDestroy _destroy;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DobbyPrepare(IntPtr target, IntPtr replacement, out IntPtr original);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DobbyCommit(IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DobbyHook(IntPtr target, IntPtr replacement, out IntPtr original);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DobbyDestroy(IntPtr target);
    internal DobbyDetourEngine(string path)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Exact hooks require Windows x64.");
        _path = Path.GetFullPath(path);
        _library = NativeLibrary.Load(_path); _hook = Marshal.GetDelegateForFunctionPointer<DobbyHook>(NativeLibrary.GetExport(_library, "DobbyHook"));
        _destroy = Marshal.GetDelegateForFunctionPointer<DobbyDestroy>(NativeLibrary.GetExport(_library, "DobbyDestroy"));
    }
    public Delegate Install(VerifiedHookTarget target, Delegate replacement)
    {
        var result = _hook(target.Address, Marshal.GetFunctionPointerForDelegate(replacement), out var original);
        if (result != 0 || original == IntPtr.Zero) throw new InvalidOperationException($"ExactHookInstallFailed: Dobby returned {result}.");
        // Library, original delegate and replacement delegate are retained for the entire process lifetime.
        try { return Marshal.GetDelegateForFunctionPointer(original, replacement.GetType()); }
        catch { _destroy(target.Address); throw; }
    }
    public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement)
    {
        // Prepare/Commit are not assumed to exist or have this order in other Dobby builds.
        using var stream = File.OpenRead(_path); using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != PreparedImageSha256)
            throw new UnsupportedSignatureException("UnsupportedBuild: two-phase native publication requires the inspected Dobby image.");
        var prepare = Marshal.GetDelegateForFunctionPointer<DobbyPrepare>(NativeLibrary.GetExport(_library, "DobbyPrepare"));
        var result = prepare(target.Address, replacement, out var original);
        if (result != 0 || original == IntPtr.Zero)
            throw new InvalidOperationException($"ExactHookPrepareFailed: Dobby returned {result}.");
        Interlocked.Increment(ref _actualPreparedCalls);
        return original;
    }
    public void Commit(VerifiedHookTarget target)
    {
        var commit = Marshal.GetDelegateForFunctionPointer<DobbyCommit>(NativeLibrary.GetExport(_library, "DobbyCommit"));
        var result = commit(target.Address);
        if (result != 0) throw new InvalidOperationException($"ExactHookCommitFailed: Dobby returned {result}.");
        Interlocked.Increment(ref _actualCommittedCalls);
    }
}

/// <summary>One native patch per process address. Releasing a binding removes logical delivery; safe native trampolines stay rooted.</summary>
internal sealed partial class ExactHookBroker
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void InstanceVoid(IntPtr instance, IntPtr methodInfo);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void InstanceEnum32(IntPtr instance, int value, IntPtr methodInfo);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void InstanceJudge(IntPtr instance, int judge, float delta, IntPtr methodInfo);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr InstanceReferenceReturn0(IntPtr instance,IntPtr methodInfo);
    private static readonly object Gate = new();
    private static readonly Dictionary<IntPtr, Entry> Entries = new();
    private readonly INativeDetourEngine _engine; private readonly IMainThreadDispatcher _main; private readonly ILogger _log;
    [ThreadStatic] private static HashSet<IntPtr>? Entered;

    internal sealed class Sink
    {
        public string Id = null!; public IMainThreadDispatcher Main = null!; public ILogger Log = null!;
        public IntPtr MethodInfo, DeclaringClass;
        public Func<IntPtr, bool> Matches = null!; public Action<HookPhase, long, int, float> Deliver = null!; public volatile bool Active = true;
        public Func<IntPtr,IDisposable>? RetainReceiver,RetainResult;
        public Func<IntPtr,IntPtr,NativeRuntime.RootHandle?>? ReplaceResult;
        public Action<IDisposable>? AfterRetained;
        internal bool CaptureBeforeDelivery;
        internal Func<NativeCoroutineFrame, IDisposable?>? CaptureCoroutine;
        internal Action<IDisposable, NativeCoroutineFrame, nuint>? AfterCoroutine;
        internal Action<NativeCoroutineFrame, string>? CoroutineFault;
        internal Func<NativeVoidFrame, IDisposable?>? CaptureNativeVoid;
        internal Action<IDisposable, NativeVoidFrame>? AfterNativeVoid;
        internal Action<IDisposable, NativeEventMidFrame, NativeVoidPhysicalIdentity>? ObserveNativeVoidMid;
        internal Action<IDisposable, NativeListenerObservation, NativeVoidPhysicalIdentity>? BeforeNativeListener;
        internal Action<IDisposable, NativeListenerObservation, bool>? AfterNativeListener;
        internal Action<NativeVoidFrame, string>? NativeVoidFault;
        internal Func<NativeOperationFrame, IDisposable?>? CaptureOperation;
        internal Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags>? CompleteOperation;
        internal Action<NativeOperationFrame, string>? OperationFault;
        public Func<IntPtr, int, float, IDisposable>? RetainJudge;
        public Action<IDisposable, long>? AfterRetainedJudge;
        public Func<NativeJudgeFrame, IDisposable?>? CaptureNativeJudge;
        public Action<IDisposable, NativeJudgeFrame>? AfterNativeJudge;
        public Action<IDisposable, IntPtr>? AfterRetainedReference;
        public Func<IntPtr, byte, IDisposable>? RetainBooleanArgument;
        public Action<IDisposable, byte>? AfterRetainedBooleanArgument;
        public LocalResultStartPolicy? LocalResultPolicy;
    }
    internal sealed partial class Entry
    {
        public VerifiedHookTarget Target = null!; public INativeDetourEngine Engine = null!;
        public Delegate Replacement = null!; public Delegate? Original;
        public readonly List<Sink> Sinks = new(); public long NativeCalls, OriginalCalls, Reentries, WrongThread;
        public long Invocation;
        public long VoidInFlight;
        internal BoundaryProbe[] Probes = Array.Empty<BoundaryProbe>();
        internal int ProbeFault;
        internal INativeCoroutineContextBridge? CoroutineBridge;
        internal IntPtr CoroutineMethodAlias;
        internal Sink[] CoroutineObservers = Array.Empty<Sink>();
        internal readonly Dictionary<nuint, CoroutineInvocation> CoroutineInvocations = new();
        internal int CoroutineUnknown;
        internal INativeOperationContextBridge? OperationBridge;
        internal Sink[] OperationObservers = Array.Empty<Sink>();
        internal readonly System.Collections.Concurrent.ConcurrentDictionary<nuint, OperationInvocation> OperationInvocations = new();
        internal int OperationUnknown, OperationCount;
        internal long OperationSequence;
        internal INativeVoidContextBridge? NativeVoidBridge;
        internal Sink[] NativeVoidObservers = Array.Empty<Sink>();
        internal readonly System.Collections.Concurrent.ConcurrentDictionary<nuint, NativeVoidInvocation> NativeVoidInvocations = new();
        internal int NativeVoidInvocationCount, NativeVoidUnknown;
        internal long NativeVoidSequence;
        internal NativeListenerContextBridge? NativeListenerBridge;
        internal NativeListenerAdoptionIdentity? NativeListenerIdentity;
        internal readonly System.Collections.Concurrent.ConcurrentDictionary<nuint, NativeListenerInvocation> NativeListenerInvocations = new();
        internal int NativeListenerInvocationCount;
        internal long NativeListenerSequence;
        internal int JudgeActivityUnknown;
        internal LocalProgressPolicy? LocalProgressPolicy;
        internal LocalInitializationPolicy? LocalInitializationPolicy;
        internal INativeInitializationContextBridge? NativeInitializationBridge;
        internal long InitializationInFlight;
        public INativeJudgeContextBridge? NativeJudgeBridge;
        public IMainThreadDispatcher? NativeMain; public ILogger? NativeLog;
        public string? NativeInstallFailure;
        public readonly Dictionary<nuint, NativeJudgeInvocation> NativeJudgeInvocations = new();
    }
    internal sealed partial class Binding : IDisposable
    {
        private readonly Entry _entry; private readonly Sink _sink;
        private Binding(Entry entry, Sink sink) { _entry = entry; _sink = sink; }
        internal bool IsDisposed { get { _sink.Main.VerifyAccess(); return !_sink.Active; } }
        internal HookDiagnostics Describe() => new(_sink.Id, Interlocked.Read(ref _entry.NativeCalls), Interlocked.Read(ref _entry.OriginalCalls), 0, 0,
            Interlocked.Read(ref _entry.Reentries), Interlocked.Read(ref _entry.WrongThread), 0);
        internal ManagedBoundaryActivity ReadActivity()
        {
            var count = Interlocked.Read(ref _entry.VoidInFlight); var known = _entry.Target.Abi is (ExactHookAbi.InstanceVoid or ExactHookAbi.InstanceBooleanVoid
                or ExactHookAbi.InstanceBooleanReturn0 or ExactHookAbi.InstanceEnum32SingleVoid or ExactHookAbi.InstanceInt32SingleVoid) && _entry.NativeInstallFailure is null;
            if (_entry.NativeJudgeBridge is { } judge)
            {
                var native = ReadJudgeActivity(_entry, judge); count = Math.Max(count, native.InFlight); known &= native.Known;
            }
            known &= Volatile.Read(ref _entry.JudgeActivityUnknown) == 0;
            if (_entry.CoroutineBridge is { } coroutine)
            { var native = ReadCoroutineActivity(_entry, coroutine); count = Math.Max(count, native.InFlight); known = native.Known && _entry.NativeInstallFailure is null; }
            if (_entry.NativeInitializationBridge is { } bridge)
            { var native = ReadInitializationActivity(_entry, bridge); count = Math.Max(count, native.InFlight); known &= native.Known; }
            if (_entry.NativeVoidBridge is { } nativeVoid)
            { var native = ReadNativeVoidActivity(_entry, nativeVoid); count = Math.Max(count, native.InFlight); known = native.Known && _entry.NativeInstallFailure is null; }
            if (_entry.OperationBridge is { } operation)
            { var native = ReadOperationActivity(_entry, operation); count = Math.Max(count, native.InFlight); known = native.Known && _entry.NativeInstallFailure is null; }
            return new(count, known);
        }
        public void Dispose() { _sink.Main.VerifyAccess(); if (!_sink.Active) return; _sink.Active = false; lock (Gate) {
            _entry.Sinks.Remove(_sink);
            if (_sink.CaptureCoroutine is not null) Volatile.Write(ref _entry.CoroutineObservers, _entry.Sinks.Where(s => s.Active && s.CaptureCoroutine is not null).ToArray());
            if (_sink.CaptureNativeVoid is not null) PublishNativeVoidObservers(_entry);
            if (_sink.CaptureOperation is not null) PublishOperationObservers(_entry);
        } }
        internal static Binding Create(Entry entry, Sink sink) => new(entry, sink);
    }
    internal sealed record ManagedBoundaryActivity(long InFlight, bool Known);

    internal ExactHookBroker(string dobbyPath, IMainThreadDispatcher main, ILogger log) : this(new DobbyDetourEngine(dobbyPath), main, log) { }
    internal ExactHookBroker(INativeDetourEngine engine, IMainThreadDispatcher main, ILogger log) { _engine = engine; _main = main; _log = log; }

    internal Binding Bind(VerifiedHookTarget target, Func<IntPtr, bool> matches, Action<HookPhase, long, int, float> deliver)
    {
        if(target.Abi is ExactHookAbi.StaticTimelineModeVoid or ExactHookAbi.StaticTimelineInstantaneousHandle or ExactHookAbi.StaticTimelineEndRangeVoid
            or ExactHookAbi.LocalInitializationReference1 or ExactHookAbi.LocalProgressPlayBooleanVoid or ExactHookAbi.StaticTimelineTooltipVoid or ExactHookAbi.CoroutineStartReference1 or ExactHookAbi.CoroutineFactoryReference0 or ExactHookAbi.NativeVoid0
            or ExactHookAbi.OperationVoid0 or ExactHookAbi.OperationReference1Void or ExactHookAbi.OperationReference2Void
            or ExactHookAbi.OperationSingle1Void or ExactHookAbi.InstanceSingleReturn0
            or ExactHookAbi.PlaybackVoid0 or ExactHookAbi.PlaybackSingle1Void or ExactHookAbi.PlaybackBoolean1Void
            or ExactHookAbi.PlaybackDouble1Void or ExactHookAbi.PlaybackPitchSingle1Void or ExactHookAbi.PlaybackReference1Void or ExactHookAbi.PlaybackUInt64_1Void
            or ExactHookAbi.PlaybackIcallSingle1Void)
            throw new UnsupportedSignatureException("Static Timeline hooks require the sealed internal progress isolation binding.");
        if(target.Abi==ExactHookAbi.InstanceReferenceReturn0)throw new UnsupportedSignatureException("Reference-return hooks require the internal rooted result binding.");
        if(target.Abi==ExactHookAbi.InstanceBooleanReturn0)throw new UnsupportedSignatureException("Boolean-return hooks require the internal retained observation binding.");
        if(target.Abi==ExactHookAbi.InstanceBooleanVoid)throw new UnsupportedSignatureException("Boolean-argument hooks require the internal retained observation binding.");
        return BindInternal(target,matches,deliver,null,null,null);
    }
    internal Binding BindReferenceResult(VerifiedHookTarget target,Func<IntPtr,bool> matches,
        Func<IntPtr,IDisposable> retainReceiver,Func<IntPtr,IDisposable> retainResult,
        Func<IntPtr,IntPtr,NativeRuntime.RootHandle?> replaceResult)
    {
        if(target.Abi!=ExactHookAbi.InstanceReferenceReturn0||target.ReturnClass==IntPtr.Zero||retainReceiver is null||retainResult is null||replaceResult is null)
            throw new UnsupportedSignatureException("A verified reference result, receiver/result roots and one internal replacement are required.");
        return BindInternal(target,matches,(_,_,_,_)=>{},retainReceiver,retainResult,replaceResult);
    }
    /// <summary>Observes the original reference return without any replacement authority.
    /// The typed entry context and validated original result stay rooted through after delivery.</summary>
    internal Binding BindRetainedReferenceObservation<T>(VerifiedHookTarget target, Func<IntPtr, bool> matches,
        Func<IntPtr, T> capture, Func<IntPtr, IDisposable> retainResult, Action<T, IntPtr> after) where T : class, IDisposable
    {
        if (target.Abi != ExactHookAbi.InstanceReferenceReturn0 || target.ReturnClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("Retained reference observation requires a verified reference-return ABI.");
        ArgumentNullException.ThrowIfNull(matches); ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(retainResult); ArgumentNullException.ThrowIfNull(after);
        return BindInternal(target, matches, (_, _, _, _) => { },
            receiver => capture(receiver) ?? throw new InvalidOperationException("Reference observation context is null."),
            retainResult, null, afterRetainedReference: (context, result) => after((T)context, result));
    }
    internal Binding BindRetainedObservation<T>(VerifiedHookTarget target, Func<IntPtr, bool> matches,
        Func<IntPtr, T> retain, Action<T> after) where T : class, IDisposable
    {
        if (target.Abi != ExactHookAbi.InstanceVoid)
            throw new UnsupportedSignatureException("Retained observations require an InstanceVoid target.");
        ArgumentNullException.ThrowIfNull(matches); ArgumentNullException.ThrowIfNull(retain); ArgumentNullException.ThrowIfNull(after);
        return BindInternal(target, matches, (_, _, _, _) => { },
            instance => retain(instance) ?? throw new InvalidOperationException("Retained observation context is null."),
            null, null, context => after((T)context));
    }
    internal Binding BindRetainedJudgeObservation<T>(VerifiedHookTarget target, Func<IntPtr, bool> matches,
        Func<IntPtr, int, float, T> retain, Action<T, long> after) where T : class, IDisposable
    {
        if (target.Abi is not (ExactHookAbi.InstanceEnum32SingleVoid or ExactHookAbi.InstanceInt32SingleVoid))
            throw new UnsupportedSignatureException("Retained judge observations require the instance int32/enum32 single void ABI.");
        ArgumentNullException.ThrowIfNull(matches); ArgumentNullException.ThrowIfNull(retain); ArgumentNullException.ThrowIfNull(after);
        return BindInternal(target, matches, (_, _, _, _) => { }, null, null, null, null,
            (receiver, judge, delta) => retain(receiver, judge, delta) ?? throw new InvalidOperationException("Retained judge context is null."),
            (context, invocation) => after((T)context, invocation));
    }
    private Binding BindInternal(VerifiedHookTarget target,Func<IntPtr,bool> matches,Action<HookPhase,long,int,float> deliver,
        Func<IntPtr,IDisposable>? retainReceiver,Func<IntPtr,IDisposable>? retainResult,Func<IntPtr,IntPtr,NativeRuntime.RootHandle?>? replaceResult,
        Action<IDisposable>? afterRetained = null, Func<IntPtr,int,float,IDisposable>? retainJudge = null,
        Action<IDisposable,long>? afterRetainedJudge = null, Action<IDisposable,IntPtr>? afterRetainedReference = null,
        LocalResultStartPolicy? localResultPolicy = null, Func<IntPtr,byte,IDisposable>? retainBooleanArgument = null,
        Action<IDisposable,byte>? afterRetainedBooleanArgument = null, bool captureBeforeDelivery = false)
    {
        _main.VerifyAccess();
        if (target.Address == IntPtr.Zero || !Enum.IsDefined(typeof(ExactHookAbi), target.Abi)) throw new UnsupportedSignatureException("Exact hook target address or ABI is invalid.");
        lock (Gate)
        {
            if (!Entries.TryGetValue(target.Address, out var entry))
            {
                entry = new() { Target = target, Engine = _engine };
                entry.Replacement=target.Abi switch{
                    ExactHookAbi.InstanceVoid=>new InstanceVoid((instance,method)=>InvokeVoid(entry,instance,method)),
                    ExactHookAbi.InstanceEnum32Void=>new InstanceEnum32((instance,value,method)=>InvokeEnum32(entry,instance,value,method)),
                    ExactHookAbi.InstanceEnum32SingleVoid or ExactHookAbi.InstanceInt32SingleVoid=>new InstanceJudge((instance,judge,delta,method)=>InvokeJudge(entry,instance,judge,delta,method)),
                    ExactHookAbi.InstanceReferenceReturn0=>new InstanceReferenceReturn0((instance,method)=>InvokeReference(entry,instance,method)),
                    ExactHookAbi.InstanceBooleanReturn0=>new InstanceBooleanReturn0((instance,method)=>InvokeBoolean(entry,instance,method)),
                    ExactHookAbi.InstanceBooleanVoid=>new InstanceBooleanVoid((instance,value,method)=>InvokeBooleanArgument(entry,instance,value,method)),
                    _=>throw new UnsupportedSignatureException("Undeclared exact hook ABI.")};
                // Root before installing: native threads may enter as soon as Dobby publishes the patch.
                Entries.Add(target.Address, entry);
                try
                {
                    var code = _engine is DobbyDetourEngine actualEngine
                        ? ReferenceCodeIdentity.TryCapture(entry, actualEngine, _main, _log) : null;
                    var original = _engine.Install(target, entry.Replacement);
                    // A confirmed original remains callable even if the later
                    // image-composition proof refuses this installed patch.
                    Volatile.Write(ref entry.Original, original);
                    // A denied composition proof leaves this ordinary hook
                    // usable. The complete canonical image still rejects any
                    // installed branch without a successfully published owner.
                    try { code?.Installed(original); }
                    catch (UnsupportedSignatureException error) { code!.RefuseComposition(_log, error); }
                }
                catch (Exception error) { entry.NativeInstallFailure = error.GetType().Name; throw; }
            }
            else if (entry.Target.Abi != target.Abi) throw new UnsupportedSignatureException("An address already has a different exact hook ABI.");
            else if(entry.Target.ReturnClass!=target.ReturnClass)throw new UnsupportedSignatureException("A shared native address has a different return reference class.");
            if (target.Abi == ExactHookAbi.InstanceBooleanVoid
                && (entry.Target.MethodInfo != target.MethodInfo || entry.Target.DeclaringClass != target.DeclaringClass))
                throw new UnsupportedSignatureException("A Boolean-argument observation address cannot share a different method identity.");
            if (entry.LocalProgressPolicy is not null)
            {
                RequireProgressTargetIdentity(entry, target);
                if (localResultPolicy is not null)
                    throw new InvalidOperationException("Progress.Update around isolation cannot share a LOCAL replacement policy.");
            }
            if (entry.LocalInitializationPolicy is not null)
            {
                RequireProgressTargetIdentity(entry, target);
                if (localResultPolicy is not null)
                    throw new InvalidOperationException("Initialization around isolation cannot share a LOCAL replacement policy.");
            }
            if ((localResultPolicy is not null || entry.Sinks.Any(x => x.Active && x.LocalResultPolicy is not null))
                && (entry.Target.MethodInfo != target.MethodInfo || entry.Target.DeclaringClass != target.DeclaringClass
                    || entry.Sinks.Any(x => x.Active && (x.MethodInfo != target.MethodInfo || x.DeclaringClass != target.DeclaringClass))))
                throw new UnsupportedSignatureException("The protected LOCAL boundary address cannot share an unrelated method identity.");
            if (entry.NativeInstallFailure is not null) throw new InvalidOperationException(entry.NativeInstallFailure);
            if(replaceResult is not null&&entry.Sinks.Any(x=>x.Active&&x.ReplaceResult is not null))
                throw new InvalidOperationException("Conflict: reference-return replacement already has one internal coordinator.");
            if (localResultPolicy is not null && entry.Sinks.Any(x => x.Active && x.LocalResultPolicy is not null))
                throw new InvalidOperationException("Conflict: this exact void boundary already has one internal LOCAL coordinator.");
            var sink = new Sink { Id = target.Id, Main = _main, Log = _log, MethodInfo = target.MethodInfo,
                DeclaringClass = target.DeclaringClass, Matches = matches, Deliver = deliver,
                RetainReceiver=retainReceiver,RetainResult=retainResult,ReplaceResult=replaceResult,AfterRetained=afterRetained,
                RetainJudge=retainJudge,AfterRetainedJudge=afterRetainedJudge,AfterRetainedReference=afterRetainedReference,
                LocalResultPolicy=localResultPolicy, RetainBooleanArgument=retainBooleanArgument,
                AfterRetainedBooleanArgument=afterRetainedBooleanArgument, CaptureBeforeDelivery=captureBeforeDelivery };
            entry.Sinks.Add(sink); return Binding.Create(entry, sink);
        }
    }
    private static IntPtr InvokeReference(Entry entry,IntPtr instance,IntPtr method)
    {
        var invocation=Interlocked.Increment(ref entry.Invocation);Interlocked.Increment(ref entry.NativeCalls);
        if (Original(entry) is not InstanceReferenceReturn0 original) return IntPtr.Zero;
        var sinks=Before(entry,instance,invocation,0,0,false);Sink? sink=sinks?.SingleOrDefault(x=>x.Active&&x.ReplaceResult is not null);
        var retained = new List<(Sink Sink, IDisposable Context)>();
        var resultRoots = new List<(Sink Sink, IDisposable Root)>();
        if (sinks is not null)
            foreach (var observer in sinks)
                if (observer.Active && observer.AfterRetainedReference is not null)
                    try
                    {
                        var context = observer.RetainReceiver!(instance);
                        try { retained.Add((observer, context)); } catch { context.Dispose(); throw; }
                    }
                    catch (Exception error) { SafeLog(observer, "Reference observation capture failed; forwarding original.", error); }
        IDisposable? receiver=null,originalRoot=null,validatedReplacement=null;NativeRuntime.RootHandle? replacement=null;var completed=false;
        if(sink is not null)try{receiver=sink.RetainReceiver!(instance);}catch(Exception e){SafeLog(sink,"Reference hook receiver retention failed; forwarding original.",e);sink=null;}
        if (sinks is not null) foreach (var observer in sinks) SafeDeliver(observer, HookPhase.Before, invocation, 0, 0);
        try
        {
            // The original stays outside every managed catch: failures propagate, never turn into a replacement or retry.
            Interlocked.Increment(ref entry.OriginalCalls);var result=original(instance,method);completed=true;
            if (result != IntPtr.Zero)
                foreach (var item in retained)
                    if (item.Sink.Active)
                        try
                        {
                            var root = item.Sink.RetainResult!(result)
                                ?? throw new InvalidOperationException("Reference observation result root is null.");
                            try { resultRoots.Add((item.Sink, root)); } catch { root.Dispose(); throw; }
                            if (item.Sink.Active) item.Sink.AfterRetainedReference!(item.Context, result);
                        }
                        catch (Exception error) { SafeLog(item.Sink, "Reference observation failed; original return is preserved.", error); }
            if(result==IntPtr.Zero||sink is null||!sink.Active)return result;
            try
            {
                originalRoot=sink.RetainResult!(result);replacement=sink.ReplaceResult!(instance,result);
                if(replacement is null||!sink.Active)return result;
                var candidate=replacement.Target;if(candidate==IntPtr.Zero)return result;
                validatedReplacement=sink.RetainResult(candidate);
                return sink.Active?candidate:result;
            }
            catch(Exception e){SafeLog(sink,"Reference hook replacement failed; original result is preserved.",e);return result;}
        }
        finally
        {
            Finish(entry,sinks,completed,invocation,0,0);
            // Every cleanup is isolated at the native boundary; one bad cleanup cannot skip the other roots.
            foreach(var lease in new IDisposable?[]{validatedReplacement,replacement,originalRoot,receiver})
                if(lease is not null)try{lease.Dispose();}catch(Exception e){if(sink is not null)SafeLog(sink,"Reference hook root release failed.",e);}
            for (var index = resultRoots.Count - 1; index >= 0; --index)
                try { resultRoots[index].Root.Dispose(); } catch (Exception error) { SafeLog(resultRoots[index].Sink, "Reference observation result-root release failed.", error); }
            for (var index = retained.Count - 1; index >= 0; --index)
                try { retained[index].Context.Dispose(); } catch (Exception error) { SafeLog(retained[index].Sink, "Reference observation context release failed.", error); }
        }
    }
    private static Delegate? Original(Entry entry)
    {
        var original = Volatile.Read(ref entry.Original);
        // An engine may have published the replacement before reporting failure.
        // Such entries remain rooted but have no confirmed original. Return no
        // callable target, including failure racing an already-entered waiter.
        while (original is null) { if (Volatile.Read(ref entry.NativeInstallFailure) is not null) return null; Thread.SpinWait(64); original = Volatile.Read(ref entry.Original); }
        return original;
    }
    private static void InvokeVoid(Entry entry, IntPtr instance, IntPtr method)
    {
        Interlocked.Increment(ref entry.VoidInFlight);
        var probes = EnterProbes(entry, instance, 0, method); var returned = false;
        LocalProgressFrame? frame = null;
        LocalInitializationFrame? initialization = null;
        try
        {
            initialization = EnterInitializationStart(entry, instance, method, out var skipInitialization);
            if (skipInitialization) return;
            if (initialization is not null && entry.NativeInstallFailure is not null)
            { InitializationFault(initialization.Policy, null, "Start trampoline installation is unknown."); return; }
            frame = EnterLocalProgress(entry, instance, method);
            InvokeVoidBody(entry, instance, method);
            returned = true;
        }
        catch (Exception error) when (initialization is { Claimed: true })
        { InitializationFault(initialization.Policy, null, "Initialization Start did not complete: " + error.GetType().Name); }
        finally
        {
            try { ExitLocalProgress(frame); }
            finally { try { ExitInitializationFrame(initialization); } finally {
                try { ExitProbes(entry, probes, instance, 0, method, returned); }
                finally { Interlocked.Decrement(ref entry.VoidInFlight); }
            } }
        }
    }
    private static void InvokeVoidBody(Entry entry, IntPtr instance, IntPtr method)
    {
        var invocation = Interlocked.Increment(ref entry.Invocation); Interlocked.Increment(ref entry.NativeCalls);
        if (Original(entry) is not InstanceVoid original) return;
        // A LOCAL claim must be resolved before ordinary observers, including
        // their pass-through rules for wrong-thread, failed filters and reentry.
        // Skipped invocations publish no fictitious original Before/After phases.
        if (SkipLocalResultStart(entry, instance, method)) return;
        var sinks = Before(entry, instance, invocation, 0, 0, false);
        var retained = RetainObservations(sinks, instance, early: true);
        if (sinks is not null) foreach (var sink in sinks) SafeDeliver(sink, HookPhase.Before, invocation, 0, 0);
        retained = RetainObservations(sinks, instance, retained);
        Interlocked.Increment(ref entry.OriginalCalls);
        var completed = false;
        try { original(instance, method); completed = true; } // Outside every managed catch. Exactly one call per native entry.
        finally
        {
            try
            {
                // Read direct postconditions before ordinary after observers can mutate the game again.
                if (completed && retained is not null)
                    foreach (var item in retained)
                        if (item.Sink.Active)
                            try { item.Sink.AfterRetained!(item.Context); }
                            catch (Exception ex) { SafeLog(item.Sink, "Retained hook observation failed.", ex); }
            }
            finally
            {
                try { Finish(entry, sinks, completed, invocation, 0, 0); }
                finally
                {
                    if (retained is not null)
                        for (var index = retained.Count - 1; index >= 0; index--)
                            try { retained[index].Context.Dispose(); }
                            catch (Exception ex) { SafeLog(retained[index].Sink, "Retained hook context release failed.", ex); }
                }
            }
        }
    }
    private static List<(Sink Sink, IDisposable Context)>? RetainObservations(Sink[]? sinks, IntPtr instance,
        List<(Sink Sink, IDisposable Context)>? retained = null, bool early = false)
    {
        if (sinks is null || sinks.Length == 0) return retained;
        foreach (var sink in sinks)
        {
            if (!sink.Active || sink.AfterRetained is null || sink.CaptureBeforeDelivery != early) continue;
            try
            {
                retained ??= new(sinks.Length);
                var context = sink.RetainReceiver!(instance);
                try { retained.Add((sink, context)); } catch { context.Dispose(); throw; }
            }
            catch (Exception ex) { SafeLog(sink, "Retained hook context capture failed; forwarding original.", ex); }
        }
        return retained;
    }
    private static void InvokeEnum32(Entry entry, IntPtr instance, int value, IntPtr method)
    {
        var invocation = Interlocked.Increment(ref entry.Invocation); Interlocked.Increment(ref entry.NativeCalls);
        if (Original(entry) is not InstanceEnum32 original) return;
        var sinks = Before(entry, instance, invocation, value, 0);
        Interlocked.Increment(ref entry.OriginalCalls);
        var completed = false;
        try { original(instance, value, method); completed = true; } // Exactly once; preserve enum and trailing MethodInfo*, including original failures.
        finally { Finish(entry, sinks, completed, invocation, value, 0); }
    }
    private static void InvokeJudge(Entry entry, IntPtr instance, int judge, float delta, IntPtr method)
    {
        Interlocked.Increment(ref entry.VoidInFlight);
        try { InvokeJudgeCore(entry, instance, judge, delta, method); }
        finally { Interlocked.Decrement(ref entry.VoidInFlight); }
    }
    private static void InvokeJudgeCore(Entry entry, IntPtr instance, int judge, float delta, IntPtr method)
    {
        var invocation = Interlocked.Increment(ref entry.Invocation); Interlocked.Increment(ref entry.NativeCalls);
        if (Original(entry) is not InstanceJudge original) return;
        var sinks = Before(entry, instance, invocation, judge, delta, false);
        List<(Sink Sink, IDisposable Context)>? retained = null;
        if (sinks is not null)
            foreach (var sink in sinks)
                if (sink.Active && sink.RetainJudge is not null)
                    try
                    {
                        retained ??= new(sinks.Length);
                        var context = sink.RetainJudge(instance, judge, delta);
                        try { retained.Add((sink, context)); }
                        catch { context.Dispose(); throw; }
                    }
                    catch (Exception error) { Interlocked.Exchange(ref entry.JudgeActivityUnknown, 1); SafeLog(sink, "Retained judge capture failed; forwarding original.", error); }
        if (sinks is not null) foreach (var sink in sinks) SafeDeliver(sink, HookPhase.Before, invocation, judge, delta);
        Interlocked.Increment(ref entry.OriginalCalls);
        var completed = false;
        try { original(instance, judge, delta, method); completed = true; } // Preserve all arguments including trailing MethodInfo*.
        finally
        {
            try
            {
                if (completed && retained is not null)
                    foreach (var item in retained)
                        if (item.Sink.Active)
                            try { item.Sink.AfterRetainedJudge!(item.Context, invocation); }
                            catch (Exception error) { Interlocked.Exchange(ref entry.JudgeActivityUnknown, 1); SafeLog(item.Sink, "Retained judge after observation failed.", error); }
            }
            finally
            {
                try { Finish(entry, sinks, completed, invocation, judge, delta); }
                finally { if (retained is not null) ReleaseNativeJudgeContexts(entry, retained); }
            }
        }
    }
    private static Sink[]? Before(Entry entry, IntPtr instance, long invocation, int judge, float delta, bool publishBefore = true)
    {
        try
        {
            Entered ??= new();
            if (!Entered.Add(entry.Target.Address)) { Interlocked.Increment(ref entry.Reentries); return null; }
            Sink[] sinks; lock (Gate) sinks = entry.Sinks.Where(x => x.Active).ToArray();
            var deliver = new List<Sink>();
            foreach (var sink in sinks)
            {
                if (!sink.Main.IsMainThread) { Interlocked.Increment(ref entry.WrongThread); continue; }
                try { if (sink.Matches(instance)) deliver.Add(sink); }
                catch (Exception ex) { SafeLog(sink, "Exact hook receiver filter failed.", ex); }
            }
            if (publishBefore) foreach (var sink in deliver) SafeDeliver(sink, HookPhase.Before, invocation, judge, delta);
            return deliver.ToArray();
        }
        catch { return Array.Empty<Sink>(); }
    }
    private static void Finish(Entry entry, Sink[]? sinks, bool completed, long invocation, int judge, float delta)
    {
        if (sinks is null) return; // Nested native entries call the original but do not inherit managed observers.
        try { if (completed) foreach (var sink in sinks) if (sink.Active) SafeDeliver(sink, HookPhase.After, invocation, judge, delta); }
        catch { }
        finally { Entered?.Remove(entry.Target.Address); }
    }
    private static void SafeDeliver(Sink sink, HookPhase phase, long invocation, int judge, float delta)
    { try { if (sink.Active) sink.Deliver(phase, invocation, judge, delta); } catch (Exception ex) { SafeLog(sink, "Exact hook logical dispatch failed.", ex); } }
    private static void SafeLog(Sink sink, string message, Exception error) { try { sink.Log.Log(LogLevel.Error, message, error); } catch { } }
}
