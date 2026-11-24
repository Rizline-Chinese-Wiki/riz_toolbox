using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

// Configured self-owned transport only. There is deliberately no production
// publication/admission constructor or code configuration/patch operation.
internal sealed class NativeEngineSchedulerContextBridge : INativeEngineSchedulerContextBridge
{
    [StructLayout(LayoutKind.Sequential)] private struct Observer
    { internal uint Version,Size; internal IntPtr Enter,Exit,Row,User; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ScalarNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint kind,uint slot,ref NativeEngineSchedulerInformation value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AttachNative(uint kind,uint slot,ulong epoch,ref Observer value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint EnterNative(IntPtr frame,IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ExitNative(IntPtr frame,nuint token,IntPtr completion,IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void RowNative(IntPtr frame,IntPtr user);
    private static readonly object Gate=new();
    private static readonly Dictionary<(IntPtr Module,NativeEngineSchedulerKind Kind,uint Slot),NativeEngineSchedulerContextBridge> Owners=new();
    private readonly IntPtr _module; private readonly InfoNative _info; private readonly AttachNative _attach;
    private readonly EnterNative _enterNative; private readonly ExitNative _exitNative; private readonly RowNative _rowNative;
    private readonly ILogger _log; private readonly INativeEnginePublicationLifetime? _publicationOwner;
    private Func<NativeEngineSchedulerFrame,nuint>? _enter;
    private Action<NativeEngineSchedulerFrame,nuint,NativeEngineSchedulerCompletion>? _exit;
    private Action<NativeEngineSchedulerRowFrame>? _row;
    private int _attempted,_unknown;
    public NativeEngineSchedulerChannelIdentity Identity { get; }
    internal static NativeEngineSchedulerContextBridge AdoptConfigured(string absoluteHelperPath,NativeEngineSchedulerChannelIdentity identity,ILogger log)
    {
        ArgumentNullException.ThrowIfNull(absoluteHelperPath); ArgumentNullException.ThrowIfNull(log);
        if (!Path.IsPathFullyQualified(absoluteHelperPath) || !identity.Complete) throw new ArgumentException("Exact configured helper and complete scheduler identity required.");
        if (!OperatingSystem.IsWindows() || IntPtr.Size!=8) throw new PlatformNotSupportedException("Scheduler transport requires Windows x64.");
        var module=NativeLibrary.Load(Path.GetFullPath(absoluteHelperPath));
        lock(Gate)
        {
            var key=(module,identity.Kind,identity.Slot);
            if(Owners.ContainsKey(key)) throw new InvalidOperationException("Scheduler channel already has a permanent managed owner.");
            var owner=new NativeEngineSchedulerContextBridge(module,identity,log); Owners.Add(key,owner); return owner;
        }
    }
    internal static NativeEngineSchedulerContextBridge AdoptPublished(NativeEngineTransportFirstInstall owner, NativeEngineSchedulerKind kind, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(log);
        try
        {
            owner.VerifyReady(); var identity=owner.SchedulerIdentity(kind); var module=owner.KernelModule;
            lock(Gate)
            {
                var key=(module,kind,identity.Slot);
                if(Owners.ContainsKey(key)) throw new InvalidOperationException("The V5 Scheduler channel already has a permanent managed owner.");
                var bridge=new NativeEngineSchedulerContextBridge(module,identity,log,owner); Owners.Add(key,bridge); return bridge;
            }
        }
        catch{owner.MarkUnknown();throw;}
    }
    private NativeEngineSchedulerContextBridge(IntPtr module,NativeEngineSchedulerChannelIdentity identity,ILogger log,INativeEnginePublicationLifetime? publicationOwner=null)
    {
        (_module,Identity,_log)=(module,identity,log); _publicationOwner=publicationOwner; _publicationOwner?.VerifyReady();
        _info=Export<InfoNative>("GetInfo"); _attach=Export<AttachNative>("Attach");
        _enterNative=Enter; _exitNative=Exit; _rowNative=Row;
        if(Export<ScalarNative>("GetAbiVersion")()!=1 || Export<ScalarNative>("GetLimit")()!=32
            || Export<ScalarNative>("GetFrameSize")()!=256 || Export<ScalarNative>("GetCompletionSize")()!=48
            || Export<ScalarNative>("GetRowFrameSize")()!=224 || Marshal.SizeOf<NativeEngineSchedulerFrame>()!=256
            || Marshal.SizeOf<NativeEngineSchedulerCompletion>()!=48 || Marshal.SizeOf<NativeEngineSchedulerRowFrame>()!=224
            || Marshal.SizeOf<NativeEngineSchedulerConfiguration>()!=64 || Marshal.SizeOf<NativeEngineSchedulerInformation>()!=144
            || Marshal.SizeOf<Observer>()!=40) throw new UnsupportedSignatureException("Scheduler independent scalar ABI1 differs.");
        RequireInformation(false);
    }
    private T Export<T>(string suffix) where T:Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module,"RizEngineSchedulerContext_"+suffix));
    private NativeEngineSchedulerInformation NewInfo()=>new(){Version=1,Size=144};
    internal static bool MatchesInformation(NativeEngineSchedulerInformation value,NativeEngineSchedulerChannelIdentity identity,bool attached)
        => identity.Complete && value.Version==1 && value.Size==144 && value.Kind==(uint)identity.Kind && value.Slot==identity.Slot
            && value.Configured==1 && value.Attached==(attached?1u:0u) && value.Unknown==0 && value.MainThreadId==identity.MainThreadId
            && value.Target==identity.Target && value.Original==identity.Original && value.Entry==identity.Entry
            && value.RowEntry==identity.RowEntry && value.Epoch==identity.Epoch && value.Faults==0 && value.SnapshotFaults==0
            && value.UnmatchedRows==0 && value.InFlight<=long.MaxValue && value.RowsInFlight<=long.MaxValue;
    private NativeEngineSchedulerInformation RequireInformation(bool attached)
    {
        _publicationOwner?.VerifyReady(); var value=NewInfo();
        if(_info((uint)Identity.Kind,Identity.Slot,ref value)!=0 || !MatchesInformation(value,Identity,attached))
            throw new UnsupportedSignatureException("Scheduler actual configured identity or native responsibility changed.");
        _publicationOwner?.VerifyReady(); return value;
    }
    public void Attach(Func<NativeEngineSchedulerFrame,nuint> enter,Action<NativeEngineSchedulerFrame,nuint,NativeEngineSchedulerCompletion> exit,
        Action<NativeEngineSchedulerRowFrame>? row)
    {
        ArgumentNullException.ThrowIfNull(enter); ArgumentNullException.ThrowIfNull(exit);
        if((Identity.Kind==NativeEngineSchedulerKind.Drain)!=(row is not null)) throw new ArgumentException("Only Drain requires the actual row callback.");
        if(Interlocked.Exchange(ref _attempted,1)!=0) throw new InvalidOperationException("Scheduler Attach is one permanent attempt.");
        (_enter,_exit,_row)=(enter,exit,row);
        try
        {
            RequireInformation(false);
            var observer=new Observer{Version=1,Size=40,Enter=Marshal.GetFunctionPointerForDelegate(_enterNative),
                Exit=Marshal.GetFunctionPointerForDelegate(_exitNative),Row=row is null?IntPtr.Zero:Marshal.GetFunctionPointerForDelegate(_rowNative)};
            if(_attach((uint)Identity.Kind,Identity.Slot,Identity.Epoch,ref observer)!=0) throw new InvalidOperationException("Native scheduler Attach rejected.");
            RequireInformation(true);
        }
        catch{Interlocked.Exchange(ref _unknown,1);_publicationOwner?.MarkUnknown();throw;}
    }
    private unsafe nuint Enter(IntPtr pointer,IntPtr user)
    {
        try{if(pointer==IntPtr.Zero || user!=IntPtr.Zero) throw new InvalidOperationException("Invalid scheduler Enter pointer/user.");return _enter!(*(NativeEngineSchedulerFrame*)pointer);}
        catch(Exception error){Fault(error);return 0;}
    }
    private unsafe void Exit(IntPtr pointer,nuint token,IntPtr completion,IntPtr user)
    {
        try{if(pointer==IntPtr.Zero || completion==IntPtr.Zero || user!=IntPtr.Zero) throw new InvalidOperationException("Invalid scheduler Exit pointer/user.");_exit!(*(NativeEngineSchedulerFrame*)pointer,token,*(NativeEngineSchedulerCompletion*)completion);}
        catch(Exception error){Fault(error);}
    }
    private unsafe void Row(IntPtr pointer,IntPtr user)
    {
        try{if(pointer==IntPtr.Zero || user!=IntPtr.Zero) throw new InvalidOperationException("Invalid scheduler row pointer/user.");_row!(*(NativeEngineSchedulerRowFrame*)pointer);}
        catch(Exception error){Fault(error);}
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        try{var value=RequireInformation(true);if(value.InFlight>(ulong)long.MaxValue-value.RowsInFlight) throw new InvalidOperationException("Scheduler responsibility overflow.");
            return new((long)(value.InFlight+value.RowsInFlight),Volatile.Read(ref _unknown)==0);}
        catch(Exception error){Fault(error);return new(0,false);}
    }
    private void Fault(Exception error){Interlocked.Exchange(ref _unknown,1);_publicationOwner?.MarkUnknown();try{_log.Log(LogLevel.Error,"Native scheduler transport failed.",error);}catch{}}
}
