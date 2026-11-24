using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

// Adoption observes a previously configured self-owned family. No installer,
// configure export or production publication credential is exposed here.
internal sealed class NativeEngineWaitContextBridge:INativeEngineWaitContextBridge
{
    [StructLayout(LayoutKind.Sequential)]private struct Observer
    {internal uint Version,Size;internal IntPtr Enter,Exit,Phase,User;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint ScalarNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint InfoNative(uint slot,ref NativeEngineWaitInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint AttachNative(uint slot,ulong epoch,ref Observer observer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate nuint EnterNative(IntPtr frame,IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void ExitNative(IntPtr frame,nuint token,IntPtr completion,IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void PhaseNative(IntPtr frame,IntPtr user);
    private static readonly object Gate=new();
    private static readonly Dictionary<(IntPtr Module,uint Slot),NativeEngineWaitContextBridge> Owners=new();
    private readonly INativeEnginePublicationLifetime? _publicationOwner;
    private readonly IntPtr _module;private readonly InfoNative _info;private readonly AttachNative _attach;private readonly ILogger _log;
    private readonly EnterNative _nativeEnter;private readonly ExitNative _nativeExit;private readonly PhaseNative _nativePhase;
    private Func<NativeEngineWaitFrame,nuint>? _enter;private Action<NativeEngineWaitFrame,nuint,NativeEngineWaitCompletion>? _exit;
    private Action<NativeEngineWaitPhaseFrame>? _phase;private int _attempted,_unknown;
    public NativeEngineWaitFamilyIdentity Identity{get;}
    internal static NativeEngineWaitContextBridge AdoptConfigured(string absoluteHelperPath,NativeEngineWaitFamilyIdentity identity,ILogger log)
    {
        ArgumentNullException.ThrowIfNull(absoluteHelperPath);ArgumentNullException.ThrowIfNull(identity);ArgumentNullException.ThrowIfNull(log);
        if(!Path.IsPathFullyQualified(absoluteHelperPath))throw new ArgumentException("A fully qualified configured helper is required.");
        if(!OperatingSystem.IsWindows()||IntPtr.Size!=8)throw new PlatformNotSupportedException("Wait transport requires Windows x64.");
        var module=NativeLibrary.Load(Path.GetFullPath(absoluteHelperPath));
        lock(Gate){var key=(module,identity.Slot);if(Owners.ContainsKey(key))throw new InvalidOperationException("Wait family already has a permanent managed owner.");
            var owner=new NativeEngineWaitContextBridge(module,identity,log);Owners.Add(key,owner);return owner;}
    }
    internal static NativeEngineWaitContextBridge AdoptPublished(NativeEngineTransportFirstInstall owner,ILogger log)
    {
        ArgumentNullException.ThrowIfNull(owner);ArgumentNullException.ThrowIfNull(log);
        try
        {
            owner.VerifyReady();var identity=owner.WaitIdentity();var module=owner.KernelModule;
            lock(Gate){var key=(module,identity.Slot);if(Owners.ContainsKey(key))throw new InvalidOperationException("The V5 Wait family already has a permanent managed owner.");
                var bridge=new NativeEngineWaitContextBridge(module,identity,log,owner);Owners.Add(key,bridge);return bridge;}
        }
        catch{owner.MarkUnknown();throw;}
    }
    private NativeEngineWaitContextBridge(IntPtr module,NativeEngineWaitFamilyIdentity identity,ILogger log,INativeEnginePublicationLifetime? publicationOwner=null)
    {
        (_module,Identity,_log)=(module,identity,log);_publicationOwner=publicationOwner;_publicationOwner?.VerifyReady();_info=Export<InfoNative>("GetInfo");_attach=Export<AttachNative>("Attach");
        _nativeEnter=Enter;_nativeExit=Exit;_nativePhase=Phase;
        if(Export<ScalarNative>("GetAbiVersion")()!=1||Export<ScalarNative>("GetLimit")()!=32||Export<ScalarNative>("GetFrameSize")()!=240
            ||Export<ScalarNative>("GetCompletionSize")()!=48||Export<ScalarNative>("GetPhaseFrameSize")()!=288
            ||Marshal.SizeOf<NativeEngineWaitFrame>()!=240||Marshal.SizeOf<NativeEngineWaitCompletion>()!=48
            ||Marshal.SizeOf<NativeEngineWaitPhaseFrame>()!=288||Marshal.SizeOf<NativeEngineWaitConfiguration>()!=432
            ||Marshal.SizeOf<NativeEngineWaitInformation>()!=176||Marshal.SizeOf<Observer>()!=40)
            throw new UnsupportedSignatureException("Wait independent scalar ABI1 differs.");
        RequireInformation(false);
    }
    private T Export<T>(string suffix)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module,"RizEngineWaitContext_"+suffix));
    internal static bool MatchesInformation(NativeEngineWaitInformation value,NativeEngineWaitFamilyIdentity identity,bool attached)
        =>value.Version==1&&value.Size==176&&value.Slot==identity.Slot&&value.Configured==1&&value.Attached==(attached?1u:0u)
            &&value.Unknown==0&&value.Reserved==0&&value.MainThreadId==identity.MainThreadId&&value.Epoch==identity.Epoch
            &&value.YieldTarget==identity.Target(NativeEngineWaitKind.Yield)&&value.DispatchTarget==identity.Target(NativeEngineWaitKind.Dispatch)
            &&value.BatchTarget==identity.Target(NativeEngineWaitKind.Batch)&&value.YieldOriginal==identity.Original(0)
            &&value.DispatchOriginal==identity.Original(1)&&value.BatchOriginal==identity.Original(2)
            &&value.YieldEntry==identity.Entry(0)&&value.DispatchEntry==identity.Entry(1)&&value.BatchEntry==identity.Entry(2)
            &&value.PhaseEntry==identity.PhaseEntry&&value.Faults==0&&value.SnapshotFaults==0&&value.Unmatched==0&&value.InFlight<=long.MaxValue;
    private NativeEngineWaitInformation RequireInformation(bool attached)
    {
        _publicationOwner?.VerifyReady();var value=new NativeEngineWaitInformation{Version=1,Size=176};
        if(_info(Identity.Slot,ref value)!=0||!MatchesInformation(value,Identity,attached))
            throw new UnsupportedSignatureException("Wait actual configured identity or native responsibility changed.");
        _publicationOwner?.VerifyReady();return value;
    }
    public void Attach(Func<NativeEngineWaitFrame,nuint> enter,Action<NativeEngineWaitFrame,nuint,NativeEngineWaitCompletion> exit,Action<NativeEngineWaitPhaseFrame> phase)
    {
        ArgumentNullException.ThrowIfNull(enter);ArgumentNullException.ThrowIfNull(exit);ArgumentNullException.ThrowIfNull(phase);
        if(Interlocked.Exchange(ref _attempted,1)!=0)throw new InvalidOperationException("Wait Attach is one permanent attempt.");
        (_enter,_exit,_phase)=(enter,exit,phase);
        try{RequireInformation(false);var observer=new Observer{Version=1,Size=40,Enter=Marshal.GetFunctionPointerForDelegate(_nativeEnter),
            Exit=Marshal.GetFunctionPointerForDelegate(_nativeExit),Phase=Marshal.GetFunctionPointerForDelegate(_nativePhase)};
            if(_attach(Identity.Slot,Identity.Epoch,ref observer)!=0)throw new InvalidOperationException("Native wait Attach rejected.");RequireInformation(true);}
        catch{Interlocked.Exchange(ref _unknown,1);_publicationOwner?.MarkUnknown();throw;}
    }
    private unsafe nuint Enter(IntPtr frame,IntPtr user)
    {try{if(frame==IntPtr.Zero||user!=IntPtr.Zero)throw new InvalidOperationException("Invalid wait Enter pointer/user.");return _enter!(*(NativeEngineWaitFrame*)frame);}catch(Exception error){Fault(error);return 0;}}
    private unsafe void Exit(IntPtr frame,nuint token,IntPtr completion,IntPtr user)
    {try{if(frame==IntPtr.Zero||completion==IntPtr.Zero||user!=IntPtr.Zero)throw new InvalidOperationException("Invalid wait Exit pointer/user.");_exit!(*(NativeEngineWaitFrame*)frame,token,*(NativeEngineWaitCompletion*)completion);}catch(Exception error){Fault(error);}}
    private unsafe void Phase(IntPtr frame,IntPtr user)
    {try{if(frame==IntPtr.Zero||user!=IntPtr.Zero)throw new InvalidOperationException("Invalid wait Phase pointer/user.");_phase!(*(NativeEngineWaitPhaseFrame*)frame);}catch(Exception error){Fault(error);}}
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {try{var value=RequireInformation(true);return new((long)value.InFlight,Volatile.Read(ref _unknown)==0);}catch(Exception error){Fault(error);return new(0,false);}}
    private void Fault(Exception error){Interlocked.Exchange(ref _unknown,1);_publicationOwner?.MarkUnknown();try{_log.Log(LogLevel.Error,"Native wait transport failed.",error);}catch{}}
}
