using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Visual;

internal sealed partial class VisualResourceService
{
    internal const int MaximumBindingIntents=128,MaximumPluginBindingIntents=32;
    private readonly HashSet<BindingIntent> _bindingIntents=new();
    private bool _refreshingIntents;
    internal int BindingIntentCount { get { _main.VerifyAccess();return _bindingIntents.Count; } }

    private OperationResult<IVisualBindingIntent> CreateIntent(IPluginContext context,ScopeGate gate,
        TargetQuery query,IVisualAssetLease asset,int priority)
    {
        // The public view checks thread/scope/provider before any argument or caller-defined getter.
        if(!ValidIntentQuery(query))return OperationResult<IVisualBindingIntent>.Fail(Error(ErrorCode.InvalidArgument,
            "A stable key, role, content or resource query for active non-template targets is required.",query?.Key));
        if(asset is not AssetLease resource||!resource.BelongsTo(this,gate))return OperationResult<IVisualBindingIntent>.Fail(Error(ErrorCode.InvalidArgument,
            "An unwrapped visual asset lease from this provider and exact plugin scope is required.",query.Key));
        if(resource.HasEnded)return OperationResult<IVisualBindingIntent>.Fail(Error(ErrorCode.TargetExpired,"The input visual asset lease has ended.",query.Key));
        if(CleanupPending)return OperationResult<IVisualBindingIntent>.Fail(CleanupError(query.Key));
        if(_bindingIntents.Count>=MaximumBindingIntents||_bindingIntents.Count(x=>x.PluginId==context.PluginId)>=MaximumPluginBindingIntents)
            return OperationResult<IVisualBindingIntent>.Fail(Error(ErrorCode.QuotaExceeded,"Too many live semantic visual binding intents.",query.Key));
        if(resource.Entry.Handle.Kind!=ExpectedKind)return OperationResult<IVisualBindingIntent>.Fail(Error(ErrorCode.TargetTypeMismatch,
            "This visual binding requires "+ExpectedKind+".",query.Key));

        var intent=new BindingIntent(this,context,gate,resource,query,priority);
        _bindingIntents.Add(intent);resource.Attach(intent);
        try
        {
            // Ownership precedes Resolve/Override: scope revocation inside either callback
            // already knows this intent, including a priority layer which has not returned yet.
            context.Own(intent);
            if(intent.CheckLifetime(true)&&!_refreshing&&!_refreshingIntents)intent.Reconcile();
            return intent.LastSnapshot.State==VisualBindingState.Stopped
                ?OperationResult<IVisualBindingIntent>.Fail(intent.LastSnapshot.Error!)
                :OperationResult<IVisualBindingIntent>.Ok(intent);
        }
        catch(Exception error)
        {
            var failure=Failure(error,ErrorCode.InternalError,query.Key);intent.StopCore(failure);
            return OperationResult<IVisualBindingIntent>.Fail(failure);
        }
    }

    private static bool ValidIntentQuery(TargetQuery? query)
    {
        if(query is null||query.IncludeTemplates||!query.ActiveOnly||
            query.Key is null&&query.Role is null&&query.ContentId is null&&query.ResourceId is null)return false;
        return new[]{query.Key,query.Scene,query.Role,query.ContentId,query.ResourceId}.All(value=>
            value is null||!string.IsNullOrWhiteSpace(value)&&value.Length<=512&&!value.Any(char.IsControl));
    }

    private void RefreshIntents(bool discover)
    {
        if(_refreshingIntents)return;_refreshingIntents=true;
        try
        {
            foreach(var intent in _bindingIntents.ToArray())
            {
                if(!intent.CheckLifetime(true))continue;
                intent.CheckCurrentLayer();
                // Resolve reads the existing registry. Only the adapter's existing bounded
                // discovery boundary is allowed to choose a replacement generation.
                if(discover)intent.Reconcile();
            }
            if(!_disposed&&CleanupPending)StopIntents(CleanupError() with {Retryable=false});
        }
        finally{_refreshingIntents=false;}
    }

    private void OnIntentTargetInvalidated(TargetIdentity target)
    {
        foreach(var intent in _bindingIntents.ToArray())intent.TargetInvalidated(target);
    }

    private void StopIntents(OperationError reason)
    {
        foreach(var intent in _bindingIntents.ToArray())
            intent.StopCore(reason with {TargetId=intent.LastSnapshot.Target?.Key??intent.LastSnapshot.Query.Key});
    }

    private sealed class BindingIntent:IVisualBindingIntent
    {
        private readonly VisualResourceService _owner;private readonly IPluginContext _context;private readonly ScopeGate _gate;
        private readonly AssetLease _asset;private readonly TargetQuery _query;private readonly int _priority;
        private VisualBindingSnapshot _snapshot;private VisualBindingIdentity? _identity;private OverrideLease? _layer;
        private bool _stopped,_resolving;
        internal string PluginId=>_context.PluginId;
        public string IntentId {get;}=Guid.NewGuid().ToString("N");
        public VisualBindingSnapshot LastSnapshot=>Volatile.Read(ref _snapshot);

        internal BindingIntent(VisualResourceService owner,IPluginContext context,ScopeGate gate,AssetLease asset,TargetQuery query,int priority)
        {
            _owner=owner;_context=context;_gate=gate;_asset=asset;_query=query;_priority=priority;
            _snapshot=new(IntentId,1,VisualBindingState.Waiting,query,asset.Entry.Handle,priority,null,null,0,
                owner.Error(ErrorCode.TargetMissing,"A unique semantic visual target has not appeared.",query.Key,true));
        }

        private OperationError? AccessError()
        {
            if(_gate.Check() is { } code)return _owner.Error(code,code==ErrorCode.WrongThread?
                "Visual binding operations require the Unity main thread.":"The visual plugin scope has ended.",_query.Key);
            return _owner._disposed?_owner.Error(ErrorCode.ScopeExpired,"The visual provider has ended.",_query.Key):null;
        }

        public OperationResult<VisualBindingSnapshot> GetSnapshot()
            =>AccessError() is { } error?OperationResult<VisualBindingSnapshot>.Fail(error):OperationResult<VisualBindingSnapshot>.Ok(LastSnapshot);

        public OperationResult Stop()
        {
            if(AccessError() is { } error)return OperationResult.Fail(error);
            StopCore(_owner.Error(ErrorCode.Cancelled,"The semantic visual binding intent was stopped.",_snapshot.Target?.Key??_query.Key));
            return OperationResult.Ok();
        }

        public void Dispose()
        {
            _owner._main.VerifyAccess();if(_stopped)return;
            StopCore(AccessError()??_owner.Error(ErrorCode.Cancelled,"The semantic visual binding intent was disposed.",_snapshot.Target?.Key??_query.Key));
        }

        internal void StopCore(OperationError reason)
        {
            if(_stopped)return;_stopped=true;
            var previous=_layer;_layer=null;_owner._bindingIntents.Remove(this);_asset.Detach(this);
            Publish(VisualBindingState.Stopped,null,0,reason);
            // Publish/removal precede native restoration. Reentrant scope/provider/asset
            // disposal therefore observes a terminal intent and cannot release twice.
            if(previous is not null)
                try{previous.Dispose();}
                catch(Exception error){Publish(VisualBindingState.Stopped,null,0,_owner.Failure(error,ErrorCode.MutationWriteFailed,reason.TargetId));}
        }

        internal bool CheckLifetime(bool inspectAsset)
        {
            if(_stopped)return false;
            if(AccessError() is { } error){StopCore(error);return false;}
            if(_owner.CleanupPending){StopCore(_owner.CleanupError(_snapshot.Target?.Key??_query.Key) with {Retryable=false});return false;}
            var entry=_asset.Entry;
            if(_asset.HasEnded||entry.Releasing||entry.Released||entry.CleanupUnconfirmed)
            {StopCore(_owner.Error(ErrorCode.TargetExpired,"The input visual asset lease is no longer live.",_snapshot.Target?.Key??_query.Key));return false;}
            if(!inspectAsset)return true;
            try
            {
                if(!entry.Native.IsAlive){StopCore(_owner.Error(ErrorCode.TargetExpired,"The input visual asset is no longer alive.",_snapshot.Target?.Key??_query.Key));return false;}
            }
            catch(Exception failure){StopCore(_owner.Failure(failure,ErrorCode.ObservationUnavailable,_snapshot.Target?.Key??_query.Key));return false;}
            // Liveness/registry/setter callbacks may revoke the whole scope tree.
            return CheckLifetime(false);
        }

        internal void CheckCurrentLayer()
        {
            if(!_stopped&&_layer is not null&&!_layer.IsActive)
                Detach(VisualBindingState.Waiting,0,_owner.Error(ErrorCode.TargetExpired,"The previous visual target generation has ended.",_snapshot.Target?.Key??_query.Key,true));
        }

        internal void TargetInvalidated(TargetIdentity target)
        {
            if(_stopped||_snapshot.Target is not { } current||current.Key!=target.Key||current.Generation!=target.Generation)return;
            Detach(VisualBindingState.Waiting,0,_owner.Error(ErrorCode.TargetExpired,"The previous visual target generation has ended.",target.Key,true));
        }

        private void Detach(VisualBindingState state,int candidates,OperationError? error)
        {
            if(_stopped)return;
            var previous=_layer;_layer=null;Publish(state,null,candidates,error);
            if(previous is not null)
                try{previous.Dispose();}
                catch(Exception failure){StopCore(_owner.Failure(failure,ErrorCode.MutationWriteFailed,error?.TargetId??_query.Key));}
            CheckLifetime(false);
        }

        internal void Reconcile()
        {
            if(_stopped||_resolving)return;_resolving=true;
            try
            {
                if(!CheckLifetime(true))return;
                var match=_owner._targets.Resolve<VisualAsset>(_query);
                if(!CheckLifetime(false))return;
                var candidates=match.Candidates.Where(candidate=>candidate.Metadata.Active&&!candidate.Metadata.Template&&
                    (_identity is null||_identity==Identity(candidate.Metadata))).ToArray();
                if(candidates.Length!=1)
                {
                    var ambiguous=candidates.Length>1;
                    Detach(ambiguous?VisualBindingState.Ambiguous:VisualBindingState.Waiting,candidates.Length,
                        _owner.Error(ambiguous?ErrorCode.AmbiguousTarget:ErrorCode.TargetMissing,ambiguous?
                            "The semantic query matches multiple visual targets.":"No active target matches the locked visual identity.",_query.Key,!ambiguous));
                    return;
                }
                var candidate=candidates[0];var target=new TargetHandle<VisualAsset>(candidate.Key,candidate.Generation);
                _identity??=Identity(candidate.Metadata);
                if(_layer is not null&&_snapshot.Target==target&&_layer.IsActive)
                {Publish(VisualBindingState.Bound,target,1,null);return;}

                // Finish the old layer before asking the same public coordinator for a new
                // generation. A failed/uncertain restoration makes CheckLifetime terminal.
                if(_layer is not null)Detach(VisualBindingState.Waiting,1,
                    _owner.Error(ErrorCode.TargetExpired,"The visual target generation changed.",_snapshot.Target?.Key??_query.Key,true));
                if(!CheckLifetime(false))return;
                var result=_owner.Override(_context,_gate,target,_asset.Entry.Handle,_priority);
                if(!CheckLifetime(true))
                {
                    if(result.Success)ReleaseUnpublished(result.Value!,target.Key);
                    return;
                }
                if(!result.Success){Publish(VisualBindingState.Waiting,null,1,_owner.WithContext(result.Error!,target.Key));return;}
                var layer=(OverrideLease)result.Value!;
                if(!layer.IsActive)
                {
                    ReleaseUnpublished(layer,target.Key);
                    if(!_stopped)Publish(VisualBindingState.Waiting,null,0,_owner.Error(ErrorCode.TargetExpired,"The new visual target generation ended during acquisition.",target.Key,true));
                    return;
                }
                _layer=layer;Publish(VisualBindingState.Bound,target,1,null);
            }
            catch(Exception failure)
            {
                if(!_stopped)StopCore(_owner.Failure(failure,ErrorCode.InternalError,_snapshot.Target?.Key??_query.Key));
            }
            finally{_resolving=false;}
        }

        private void ReleaseUnpublished(IMutationLease<VisualAsset> layer,string target)
        {
            try{layer.Dispose();}
            catch(Exception failure){StopCore(_owner.Failure(failure,ErrorCode.MutationWriteFailed,target));}
        }

        private static VisualBindingIdentity Identity(TargetMetadata metadata)=>new(metadata.Scene,metadata.ContentId,metadata.Role,metadata.ResourceId);

        private void Publish(VisualBindingState state,TargetHandle<VisualAsset>? target,int candidates,OperationError? error)
        {
            var previous=_snapshot;
            var next=new VisualBindingSnapshot(IntentId,previous.Revision,state,_query,_asset.Entry.Handle,_priority,_identity,target,candidates,error);
            if(next==previous)return;
            // No observer callback executes inside a partially acquired/disposed native layer.
            // The published record contains SDK values only and can be read after withdrawal.
            Volatile.Write(ref _snapshot,next with {Revision=checked(previous.Revision+1)});
        }
    }
}
