using RizToolbox.Core;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>A CPU content store, intentionally distinct from the unadapted game chart resolver.</summary>
internal sealed partial class LocalContentService:IDisposable
{
    internal const int MaximumOwnerItems=32,MaximumItems=128;
    internal const long MaximumOwnerBytes=64L*1024*1024,MaximumBytes=128L*1024*1024;
    private readonly IMainThreadDispatcher _main;private readonly ContentFileAccess _files;
    private Dictionary<LocalContentId,Entry> _published=new();private HashSet<Entry> _retained=new();
    private long _revision;private bool _disposed,_busy;
    private ILocalChartParser? _chartParser;
    private object _parserEpoch=new();
    internal bool IsDisposed=>_disposed;
    internal bool HasUnconfirmedCleanup=>_retained.Any(x=>x.References==0&&x.Payload is not null);
    internal int RetainedCount=>_retained.Count;
    private sealed class Entry
    {
        internal LocalContentSnapshot Snapshot=null!;internal object? Payload;internal long Bytes;internal int References=1;internal bool Published=true;
        internal Entry[] Dependencies=Array.Empty<Entry>();
        internal Entry[]? Group;
    }
    internal LocalContentService(IMainThreadDispatcher main,ContentFileAccess files){_main=main;_files=files;}
    internal ILocalContentProviderService ForPlugin(IPluginContext context)
    {_main.VerifyAccess();var gate=context.Own(new Gate(context));return new View(this,context,gate);}
    internal void SetChartParser(ILocalChartParser? parser)
    {
        _main.VerifyAccess();_chartParser=parser;_parserEpoch=new();
        if(parser is null)WithdrawBatch(_published.Values.Where(x=>x.Snapshot.Kind==LocalContentKind.Chart)
            .SelectMany(x=>x.Group??new[]{x}).Distinct().ToArray());
    }
    private OperationError? Guard(Gate? gate=null)
    {
        if(!_main.IsMainThread)return new(ErrorCode.WrongThread,"Local content operations require the main thread.");
        if(_disposed||gate is {Active:false})return new(ErrorCode.ScopeExpired,"Local content scope has expired.");
        return _busy?new(ErrorCode.Conflict,"Reentrant content registration is rejected."):null;
    }
    private Result<ILocalContentRegistration> Register(IPluginContext context,Gate gate,LocalContentRequest request,CancellationToken token)
    {
        var result=RegisterTransaction(context,gate,new[]{request},token,AudioContentCapabilityIds.LocalContent,true);
        return result.Success?Result<ILocalContentRegistration>.Ok(result.Value!.SingleRegistration!):Result<ILocalContentRegistration>.Fail(result.Error!);
    }
    /// <summary>Trusted host consumer seam. It retains already validated CPU data only; this is not a game injection API.</summary>
    internal Result<ValidatedContentLease> AcquireValidated(LocalContentId id,string consumerRunId)
    {
        var guard=Guard();if(guard is not null)return Result<ValidatedContentLease>.Fail(guard);
        if(string.IsNullOrWhiteSpace(consumerRunId)||consumerRunId.Length>128)return Result<ValidatedContentLease>.Fail(new(ErrorCode.InvalidArgument,"A bounded host consumer/run identity is required."));
        if(!_published.TryGetValue(id,out var entry))return Result<ValidatedContentLease>.Fail(new(ErrorCode.TargetMissing,"Content is withdrawn or absent."));
        entry.References++;return Result<ValidatedContentLease>.Ok(new(entry.Snapshot,entry.Payload!,consumerRunId,
            new ActionLease(()=>Release(entry)),_main.VerifyAccess));
    }
    internal Result<ValidatedContentLease> ResolveGameChart(LocalContentId id,string runId)
        =>Result<ValidatedContentLease>.Fail(new(ErrorCode.OperationUnsupported,"This build has no adapted game chart resolution boundary.",AudioContentCapabilityIds.LocalContent,id.Namespace+":"+id.Id));
    private void Withdraw(Entry entry)
    {
        if(entry.Group is not null){WithdrawBatch(entry.Group);return;}
        if(!entry.Published)return;entry.Published=false;
        if(_published.TryGetValue(entry.Snapshot.Content,out var current)&&ReferenceEquals(current,entry))_published.Remove(entry.Snapshot.Content);
        Release(entry);
    }
    private void Release(Entry entry)
    {
        if(--entry.References!=0)return;
        // A failed free may leave this root using its dependencies. Keep the
        // complete retained graph and its quota reservation until process exit.
        // Group withdrawal still attempts all independent publication references.
        try{CleanupPayload(entry);}catch(Exception e){throw new AggregateException(e);}
        var dependencies=entry.Dependencies;entry.Dependencies=Array.Empty<Entry>();entry.Group=null;
        List<Exception>? failures=null;
        foreach(var dependency in dependencies)try{Release(dependency);}catch(Exception e){(failures??=new()).Add(e);}
        if(failures is not null)throw new AggregateException(failures);
    }
    private void CleanupPayload(Entry entry)
    {
        // A failed native free is unconfirmed. Keep its payload and budget reservation;
        // do not retry an arbitrary IDisposable whose first call may already have freed it.
        try{(entry.Payload as IDisposable)?.Dispose();}
        catch{_retained.Add(entry);throw;}
        entry.Payload=null;_retained.Remove(entry);
    }
    public void Dispose()
    {
        _main.VerifyAccess();if(_disposed)return;_disposed=true;WithdrawBatch(_published.Values.ToArray());
        // Host consumer leases own retained content until their run ends, even after module/provider withdrawal.
    }
    private static bool Id(string? id)=>id is {Length:>0 and <=128}&&id.All(c=>c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');
    private sealed class Gate:IDisposable
    {private bool _active=true;private readonly IPluginContext _context;internal Gate(IPluginContext context)=>_context=context;internal bool Active=>_active&&(_context is not IScopedPluginContext scoped||!scoped.Lifetime.IsCancellationRequested);public void Dispose()=>_active=false;}
    private sealed class View:ILocalContentProviderService
    {
        private readonly LocalContentService _owner;private readonly IPluginContext _context;private readonly Gate _gate;
        internal View(LocalContentService owner,IPluginContext context,Gate gate){_owner=owner;_context=context;_gate=gate;}
        public Result<ILocalContentRegistration> Register(LocalContentRequest request,CancellationToken cancellationToken=default)=>_owner.Register(_context,_gate,request,cancellationToken);
        public Result<IReadOnlyList<LocalContentSnapshot>> GetPublished()
        {
            var guard=_owner.Guard(_gate);return guard is not null?Result<IReadOnlyList<LocalContentSnapshot>>.Fail(guard):Result<IReadOnlyList<LocalContentSnapshot>>.Ok(
                Array.AsReadOnly(_owner._published.Values.Select(x=>x.Snapshot).OrderBy(x=>x.Content.Namespace,StringComparer.Ordinal).ThenBy(x=>x.Content.Id,StringComparer.Ordinal).ToArray()));
        }
    }
    private sealed class Registration:ILocalContentRegistration
    {
        private readonly BatchRegistration _group;internal Registration(BatchRegistration group)=>_group=group;
        public LocalContentSnapshot Content=>_group.Contents[0];public bool IsPublished=>_group.IsPublished;
        public void Dispose()=>_group.Dispose();
    }
}
internal sealed class ValidatedContentLease:IDisposable
{
    internal LocalContentSnapshot Snapshot{get;}internal object Payload{get;}internal string ConsumerRunId{get;}private IDisposable? _release;
    private readonly Action _verifyAccess;
    internal ValidatedContentLease(LocalContentSnapshot snapshot,object payload,string runId,IDisposable release,Action verifyAccess)
    {Snapshot=snapshot;Payload=payload;ConsumerRunId=runId;_release=release;_verifyAccess=verifyAccess;}
    public void Dispose(){_verifyAccess();var release=_release;_release=null;release?.Dispose();}
}
