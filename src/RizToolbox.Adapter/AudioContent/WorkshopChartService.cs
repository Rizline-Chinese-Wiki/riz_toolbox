using RizToolbox.Core.Mutation;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core.Targets;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>The only hook-facing selection path. B08 owns selection; provider scopes never own an already adopted run payload.</summary>
internal sealed class WorkshopChartService:IDisposable
{
    internal const int MaximumBindings=32,MaximumSessions=32;
    private static readonly LocalContentId Original=new("game","original");
    private readonly IMainThreadDispatcher _main;private readonly ILogger _log;private readonly LocalContentService _content;
    private readonly Func<ulong,NativeRuntime.RootHandle> _resolve;private readonly Func<IParsedLocalChart,IParsedLocalChart> _instantiate;
    private readonly Func<IParsedLocalChart,NativeRuntime.RootHandle> _retain;private readonly Func<IParsedLocalChart,bool> _isCurrent;
    private readonly SemanticTargetRegistry _targets;private readonly MutationCoordinator _mutations;private readonly List<Binding> _bindings=new();private readonly List<Session> _sessions=new();
    private IGameLifecycleSubscription? _subscription;private bool _disposed,_busy;
    private readonly RunEffectRecorder? _audit;private readonly IGameLifecycleService _lifecycle;
    private sealed class Binding
    {
        internal ulong Id;internal NativeRuntime.RootHandle Root=null!;internal TargetHandle<LocalContentId> Target=null!;internal MutationChannel<LocalContentId> Channel=null!;internal LocalContentId Selected=Original;
    }
    private sealed class Session:IDisposable
    {
        internal ValidatedContentLease Content=null!;internal IParsedLocalChart Chart=null!;internal string? RunId;internal bool Disposed;
        internal IDisposable? Audit;
        public void Dispose(){if(Disposed)return;Disposed=true;try{Chart.Dispose();}finally{try{Content.Dispose();}finally{Audit?.Dispose();Audit=null;}}}
    }
    internal WorkshopChartService(IMainThreadDispatcher main,ILogger log,LocalContentService content,IGameLifecycleService lifecycle,
        Func<ulong,NativeRuntime.RootHandle> resolve,Func<IParsedLocalChart,IParsedLocalChart> instantiate,
        Func<IParsedLocalChart,NativeRuntime.RootHandle> retain,Func<IParsedLocalChart,bool> isCurrent,RunEffectRecorder? audit=null)
    {
        _main=main;_log=log;_content=content;_resolve=resolve;_instantiate=instantiate;_retain=retain;_isCurrent=isCurrent;_audit=audit;_lifecycle=lifecycle;
        _targets=new(main,log);_mutations=new(_targets,main,log);var subscribed=lifecycle.Subscribe(OnLifecycle);
        if(!subscribed.Success)throw new InvalidOperationException(subscribed.Error!.Message);_subscription=subscribed.Value;
    }
    internal ILocalChartReplacementService ForPlugin(IPluginContext context)
    {_main.VerifyAccess();var gate=context.Own(new Gate(context));return new View(this,context,gate);}
    private Result<ILocalChartBinding> Bind(IPluginContext context,Gate gate,ulong id,LocalContentId content,bool experimental)
    {
        if(!_main.IsMainThread)return Fail(ErrorCode.WrongThread,"Chart bindings require the main thread.");
        if(_disposed||!gate.Active)return Fail(ErrorCode.ScopeExpired,"Chart binding scope has expired.");
        if(_busy)return Fail(ErrorCode.Conflict,"Reentrant chart binding is rejected.");
        if(!experimental)return Fail(ErrorCode.OperationUnsupported,"Explicit allowExperimental=true is required; game result/submission effects are unverified.");
        if(id==0||content is null||content.Namespace!=context.PluginId)return Fail(ErrorCode.InvalidArgument,"A nonzero existing workshop ID and this provider's content ID are required.");
        _busy=true;ValidatedContentLease? retained=null;NativeRuntime.RootHandle? root=null;
        try
        {
            var local=_content.AcquireValidated(content,"binding-validation");if(!local.Success)return Result<ILocalChartBinding>.Fail(local.Error!);retained=local.Value;
            if(retained!.Payload is not IParsedLocalChart parsed)return Fail(ErrorCode.ContentInvalid,"The selected content is not a validated native chart.");
            if (parsed.Source.PlaybackLineError is { } reason) return Fail(ErrorCode.ContentInvalid, reason);
            root=_resolve(id);var binding=_bindings.SingleOrDefault(x=>x.Root.Target==root.Target);
            if(binding is null)
            {
                if(_bindings.Count>=MaximumBindings)return Fail(ErrorCode.QuotaExceeded,"At most 32 live workshop object bindings are retained.");
                binding=new(){Id=id,Root=root};root=null;
                var registered=_targets.Register("game.workshop-chart/"+id+"/"+Guid.NewGuid().ToString("N"),()=>binding.Selected,lifetime:binding.Root,
                    metadata:new(Role:"workshop-chart",ContentId:"steam.workshop:"+id),aliases:new[]{"game.workshop-chart/"+id});
                if(!registered.Success){binding.Root.Dispose();throw new AdapterOperationException(registered.Error!);}binding.Target=registered.Value!;
                var channel=_mutations.Register(binding.Target,"selected-local-chart",value=>binding.Selected=value,MutationStrategy.Exclusive,
                    validate:value=>value==Original||value.Namespace.Length>0&&value.Id.Length>0?null:"Invalid local selection.");
                if(!channel.Success){_targets.Invalidate(binding.Target);throw new AdapterOperationException(channel.Error!);}binding.Channel=channel.Value!;_bindings.Add(binding);
            }
            var acquired=binding.Channel.AcquireExclusive(context.PluginId,content);
            if(!acquired.Success)return Result<ILocalChartBinding>.Fail(acquired.Error!);
            var lease=new Lease(acquired.Value!,id,content);
            if(!gate.Active){lease.Dispose();return Fail(ErrorCode.ScopeExpired,"Provider ended while chart selection was being acquired.");}
            try{return Result<ILocalChartBinding>.Ok(context.Own<ILocalChartBinding>(lease));}catch{lease.Dispose();throw;}
        }
        catch(AdapterOperationException e){return Result<ILocalChartBinding>.Fail(e.Error);}
        catch(KeyNotFoundException e){return Fail(ErrorCode.TargetMissing,e.Message);}
        catch(Exception e){return Fail(ErrorCode.ContentInvalid,e.Message);}
        finally{retained?.Dispose();root?.Dispose();_busy=false;}
    }
    /// <summary>Called only after the real loader completed with a non-null original result. The broker owns the extra returned root.</summary>
    internal NativeRuntime.RootHandle? AdoptResult(IntPtr receiver)
    {
        _main.VerifyAccess();if(_disposed||_busy||_sessions.Count>=MaximumSessions)return null;
        var binding=_bindings.SingleOrDefault(x=>x.Root.Target==receiver&&x.Channel.Valid);if(binding is null||binding.Selected==Original)return null;
        var acquired=_content.AcquireValidated(binding.Selected,"pending-workshop:"+binding.Id);if(!acquired.Success)return null;
        var content=acquired.Value!;IParsedLocalChart? chart=null;NativeRuntime.RootHandle? root=null;_busy=true;
        try
        {
            // A provider can republish the same ID between binding and the game's next lookup.
            if (content.Payload is not IParsedLocalChart template || template.Source.PlaybackLineError is not null) return null;
            // Chart.Mirror and subsequent game processing can mutate Chart. Each actual resolution receives a fresh native graph.
            chart=_instantiate(template);root=_retain(chart);
            if(_disposed||binding.Selected!=content.Snapshot.Content)return null;
            _sessions.Add(new(){Content=content,Chart=chart});content=null!;chart=null;var result=root;root=null;return result;
        }
        finally{root?.Dispose();chart?.Dispose();content?.Dispose();_busy=false;}
    }
    internal void Refresh()
    {
        _main.VerifyAccess();if(_disposed||_busy)return;
        foreach(var binding in _bindings.ToArray())
        {
            NativeRuntime.RootHandle? now=null;bool current;
            try{now=_resolve(binding.Id);current=now.Target==binding.Root.Target;}catch{current=false;}finally{now?.Dispose();}
            if(!current){binding.Channel.Dispose();_targets.Invalidate(binding.Target);_bindings.Remove(binding);}
        }
        var snapshot=_lifecycle.GetSnapshot();if(snapshot.Success&&snapshot.Value is not null)Associate(snapshot.Value);
    }
    private void Associate(GameLifecycleSnapshot state)
    {
        if(!state.RunsAvailable||state.RunId is null||state.Phase is not (RunLifecyclePhase.Playing or RunLifecyclePhase.Paused))return;
        foreach(var session in _sessions.Where(x=>x.RunId is null))
            try
            {
                if(!_isCurrent(session.Chart))continue;
                session.RunId=state.RunId;
                var content=session.Content.Snapshot.Content;
                session.Audit=_audit?.Begin(new(content.Namespace,"riz.content.workshop-chart","resolved-current-chart",content.Namespace+":"+content.Id,
                    RunModificationCategory.LocalContent,RunModificationOutcome.Applied,"Loaded chart graph matches the active game's LevelInformation; result/submission effects remain unverified.",
                    state.RunId,state.SessionId,state.Revision));
            }
            catch(Exception e){try{_log.Log(LogLevel.Warning,"Local chart run association could not be verified.",e);}catch{}}
    }
    private void OnLifecycle(GameLifecycleChange change)
    {
        _main.VerifyAccess();
        Associate(change.Snapshot);
        var stopAll=change.Kind==GameLifecycleChangeKind.SceneChanged&&change.Snapshot.Scene!="Level"||change.Snapshot.Phase==RunLifecyclePhase.ObservationLost;
        foreach(var session in _sessions.Where(x=>stopAll||change.Kind==GameLifecycleChangeKind.RunEnded&&x.RunId is not null&&x.RunId==change.RelatedRunId).ToArray())
        {try{session.Dispose();}catch(Exception e){_log.Log(LogLevel.Warning,"Retained local chart release failed.",e);}finally{_sessions.Remove(session);}}
        if(_disposed&&_sessions.Count==0){_subscription?.Dispose();_subscription=null;}
    }
    public void Dispose()
    {
        _main.VerifyAccess();if(_disposed)return;_disposed=true;_mutations.Dispose();_targets.Dispose();_bindings.Clear();
        // The lifecycle subscription keeps adopted run graphs alive after provider/capability withdrawal.
        if(_sessions.Count==0){_subscription?.Dispose();_subscription=null;}
    }
    private static Result<ILocalChartBinding> Fail(ErrorCode code,string message)=>Result<ILocalChartBinding>.Fail(new(code,message,AudioContentCapabilityIds.WorkshopChartReplacement));
    private sealed class Gate:IDisposable
    {private bool _active=true;private readonly IPluginContext _context;internal Gate(IPluginContext context)=>_context=context;internal bool Active=>_active&&(_context is not IScopedPluginContext scope||!scope.Lifetime.IsCancellationRequested);public void Dispose()=>_active=false;}
    private sealed class View:ILocalChartReplacementService
    {private readonly WorkshopChartService _owner;private readonly IPluginContext _context;private readonly Gate _gate;internal View(WorkshopChartService owner,IPluginContext context,Gate gate){_owner=owner;_context=context;_gate=gate;}public Result<ILocalChartBinding> BindWorkshopChart(ulong workshopId,LocalContentId content,bool allowExperimental=false)=>_owner.Bind(_context,_gate,workshopId,content,allowExperimental);}
    private sealed class Lease:ILocalChartBinding
    {private IMutationLease<LocalContentId>? _inner;public ulong WorkshopId{get;}public LocalContentId Content{get;}internal Lease(IMutationLease<LocalContentId> inner,ulong id,LocalContentId content){_inner=inner;WorkshopId=id;Content=content;}public bool IsActive=>_inner?.IsActive??false;public void Dispose(){var inner=_inner;_inner=null;inner?.Dispose();}}
}
