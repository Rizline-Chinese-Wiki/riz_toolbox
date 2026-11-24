using System.Diagnostics;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter;

/// <summary>Adapter-private discovery seam. Roots transfer only after target registration succeeds.</summary>
internal sealed record GameColorCandidate(string Identity,string CanonicalBase,TargetMetadata Metadata,
    IReadOnlyList<string> Aliases,Func<ResourceColor> Read,Action<ResourceColor> Write,
    Func<bool> IsAlive,Func<bool> IsEligible,Func<IDisposable> Retain);
internal interface IGameColorBackend { IEnumerable<GameColorCandidate> Discover(int maximumScanned); }

/// <summary>All real game Image.color and TMP_Text.color paths share the same registry and per-property mutation channel.</summary>
internal sealed class GameResourceService : IGameResourceService,IDisposable
{
    internal const int MaximumTargets=512,MaximumScannedImages=4096;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly IGameColorBackend _backend;
    private readonly Dictionary<string,BoundColor> _bindings=new(StringComparer.Ordinal);
    private readonly SemanticTargetRegistry _targets;
    private readonly MutationCoordinator _mutations;
    private readonly bool _ownsSharedServices;
    private readonly Stopwatch _clock=Stopwatch.StartNew();
    private double _nextDiscovery;
    private bool _disposed,_limitLogged;
    internal SemanticTargetRegistry Targets=>_targets;
    internal MutationCoordinator Mutations=>_mutations;
    private sealed class BoundColor
    {
        internal GameColorCandidate Source=null!;
        internal TargetHandle<ResourceColor> Target=null!;
        internal MutationChannel<ResourceColor> Channel=null!;
        internal string? LastReport;
    }

    internal GameResourceService(NativeRuntime api,IMainThreadDispatcher main,ILogger log,IntPtr managerType)
        :this(main,log,new NativeGameColorBackend(api,main,log,managerType)) { }
    internal GameResourceService(NativeRuntime api,IMainThreadDispatcher main,ILogger log,IntPtr managerType,
        SemanticTargetRegistry targets,MutationCoordinator mutations)
        :this(main,log,new NativeGameColorBackend(api,main,log,managerType),targets,mutations) { }
    internal GameResourceService(IMainThreadDispatcher main,ILogger log,IGameColorBackend backend,
        SemanticTargetRegistry? targets=null,MutationCoordinator? mutations=null)
    {
        _main=main;_log=log;_backend=backend;_main.VerifyAccess();
        if((targets is null)!=(mutations is null))throw new ArgumentException("Shared target registry and coordinator must be supplied together.");
        _ownsSharedServices=targets is null;
        _targets=targets??new(main,log);_mutations=mutations??new(_targets,main,log);_targets.Invalidated+=OnInvalidated;
    }
    internal ITargetService TargetsForPlugin(IPluginContext context)=>_targets.ForPlugin(context);
    internal IMutationService MutationsForPlugin(IPluginContext context)=>_mutations.ForPlugin(context);
    internal IGameResourceService ForPlugin(IPluginContext context)=>new LegacyView(this,context);

    internal int Refresh(bool forceDiscovery=false)
    {
        _main.VerifyAccess();if(_disposed)return 0;
        foreach(var binding in _bindings.Values.ToArray())
        {
            if(!Safe(binding.Source.IsAlive)) Remove(binding,false);
            else if(!Safe(binding.Source.IsEligible)||!binding.Channel.Valid) Remove(binding,true);
        }
        if(forceDiscovery||_bindings.Count==0||_clock.Elapsed.TotalSeconds>=_nextDiscovery)
        {
            Discover();_nextDiscovery=_clock.Elapsed.TotalSeconds+5;
        }
        foreach(var binding in _bindings.Values.ToArray())
        {
            var result=binding.Channel.Refresh();
            if(!result.Success)_log.Log(LogLevel.Warning,"Game color coordination failed: "+result.Error);
        }
        foreach(var binding in _bindings.Values.ToArray())
            if(binding.Channel.Valid)Report(binding);
        return _bindings.Count;
    }

    private void Discover()
    {
        var count=0;
        foreach(var candidate in _backend.Discover(MaximumScannedImages))
        {
            if(++count>MaximumScannedImages)break;
            if(candidate is null||candidate.Metadata.Template||!candidate.Metadata.Active||!Safe(candidate.IsAlive)||!Safe(candidate.IsEligible))continue;
            if(_bindings.TryGetValue(candidate.Identity,out var existing))
            {
                var aliases=_targets.SetAliases(existing.Target,candidate.Aliases);
                var metadata=_targets.UpdateMetadata(existing.Target,candidate.Metadata);
                if(!aliases.Success||!metadata.Success)Remove(existing,false);
                continue;
            }
            if(_bindings.Count>=MaximumTargets)
            {
                if(!_limitLogged){_limitLogged=true;_log.Log(LogLevel.Warning,$"Game color target cache is bounded at {MaximumTargets}; additional color receivers are not exposed.");}
                continue;
            }
            Bind(candidate);
        }
    }

    private void Bind(GameColorCandidate candidate)
    {
        IDisposable? lifetime=null;TargetHandle<ResourceColor>? target=null;
        try
        {
            // The opaque binding suffix also prevents an old token matching a replacement registry after module recovery.
            var key=candidate.CanonicalBase+"/binding/"+Guid.NewGuid().ToString("N")+"/color";
            lifetime=candidate.Retain();
            var bound=_targets.Register(key,candidate.Read,candidate.IsAlive,lifetime,candidate.Metadata,candidate.Aliases);
            if(!bound.Success)throw new AdapterOperationException(bound.Error!);
            target=bound.Value!;lifetime=null;
            var channel=_mutations.Register(target,"color",candidate.Write,MutationStrategy.PriorityOverride,
                ExternalWritePolicy.AdoptAndReapply,ValidateColor,ColorComparer.Instance);
            if(!channel.Success)throw new AdapterOperationException(channel.Error!);
            _bindings.Add(candidate.Identity,new(){Source=candidate,Target=target,Channel=channel.Value!});
            _log.Log(LogLevel.Info,$"Real game resource discovered: {key}; generation={target.Generation}; aliases={string.Join(",",candidate.Aliases)}.");
        }
        catch(Exception ex)
        {
            if(target is not null)_targets.Invalidate(target);
            lifetime?.Dispose();_log.Log(LogLevel.Warning,"Game color binding was refused: "+ex.Message);
        }
    }
    private void Remove(BoundColor binding,bool restore)
    {
        if(restore)binding.Channel.Dispose();
        _targets.Invalidate(binding.Target);
        _bindings.Remove(binding.Source.Identity);
    }
    private void OnInvalidated(TargetIdentity identity)
    {
        var item=_bindings.FirstOrDefault(x=>x.Value.Target.Key==identity.Key&&x.Value.Target.Generation==identity.Generation);
        if(item.Key is not null)_bindings.Remove(item.Key);
    }
    private static bool Safe(Func<bool> value){try{return value();}catch{return false;}}
    private static string? ValidateColor(ResourceColor color)=>color is null||new[]{color.R,color.G,color.B,color.A}.Any(v=>!float.IsFinite(v)||v<0||v>1)
        ?"RGBA channels must be finite values in [0,1].":null;

    public OperationResult<IReadOnlyList<GameResourceTarget>> GetTargets()
    {
        if(!_main.IsMainThread)return OperationResult<IReadOnlyList<GameResourceTarget>>.Fail(ErrorCode.WrongThread,"Game resource reads require the main thread.",GameCapabilityIds.Resources);
        if(_disposed)return OperationResult<IReadOnlyList<GameResourceTarget>>.Fail(ErrorCode.ScopeExpired,"Game resource service is disposed.",GameCapabilityIds.Resources);
        var own=_bindings.Values.Select(value=>new TargetIdentity(value.Target.Key,value.Target.Generation)).ToHashSet();
        var targets=_targets.GetTargets().Where(x=>own.Contains(new(x.Key,x.Generation))&&x.ValueType==typeof(ResourceColor)&&x.Metadata.Active&&!x.Metadata.Template)
            .SelectMany(x=>x.Aliases.Prepend(x.Key).Select(key=>new GameResourceTarget(key,x.Metadata.ResourceId??"unity.image.color",x.Metadata.Description??x.Key)))
            .DistinctBy(x=>x.Key).OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray();
        return OperationResult<IReadOnlyList<GameResourceTarget>>.Ok(Array.AsReadOnly(targets));
    }
    public OperationResult<IResourceOverride> OverrideColor(GameColorOverrideRequest request)=>Override(request,null);
    private OperationResult<IResourceOverride> Override(GameColorOverrideRequest request,IPluginContext? context)
    {
        if(!_main.IsMainThread)return OperationResult<IResourceOverride>.Fail(ErrorCode.WrongThread,"Game resource writes require the main thread.",GameCapabilityIds.Resources,request?.ResourceKey);
        if(_disposed)return OperationResult<IResourceOverride>.Fail(ErrorCode.ScopeExpired,"Game resource service is disposed.",GameCapabilityIds.Resources,request?.ResourceKey);
        if(request is null||string.IsNullOrWhiteSpace(request.ResourceKey)||ValidateColor(request.Color) is not null)
            return OperationResult<IResourceOverride>.Fail(ErrorCode.InvalidArgument,"Resource key and finite RGBA values are required.",GameCapabilityIds.Resources,request?.ResourceKey);
        var match=_targets.Resolve<ResourceColor>(new(Key:request.ResourceKey));
        if(match.Kind!=TargetMatchKind.Unique)return OperationResult<IResourceOverride>.Fail(match.Error!);
        // The shared registry also contains non-UI properties. This service may
        // issue layers only for generations owned by its own discovery provider.
        var binding=_bindings.Values.SingleOrDefault(x=>x.Target==match.Target);
        if(binding is null)return OperationResult<IResourceOverride>.Fail(ErrorCode.TargetMissing,
            "The requested color target does not belong to this game UI provider.",GameCapabilityIds.Resources,request.ResourceKey,true);
        var key=new MutationKey<ResourceColor>(match.Target!,"color");
        OperationResult<IMutationLease<ResourceColor>> result;
        if(context is not null)result=_mutations.ForPlugin(context).Override(key,request.Color,request.Priority);
        else
        {
            result=binding.Channel.Override("adapter.legacy",request.Color,request.Priority);
        }
        if(!result.Success)return OperationResult<IResourceOverride>.Fail(result.Error!);
        var active=_bindings.Values.SingleOrDefault(x=>x.Target==match.Target);if(active is not null)Report(active);
        return OperationResult<IResourceOverride>.Ok(new LegacyOverride(request.ResourceKey,result.Value!));
    }
    private void Report(BoundColor binding)
    {
        var snapshot=binding.Channel.Describe();
        var winner=snapshot.Holders.FirstOrDefault(x=>x.Winner);
        var report=$"{snapshot.Holders.Count}:{winner?.Owner}:{snapshot.Baseline}:{snapshot.Effective}";
        if(report==binding.LastReport)return;binding.LastReport=report;
        _log.Log(LogLevel.Info,$"Game resource read-back verified: {binding.Target.Key}; layers={snapshot.Holders.Count}; topPriority={winner?.Priority.ToString()??"baseline"}; owners={string.Join(",",snapshot.Holders.Select(x=>x.Owner))}; generation={binding.Target.Generation}; baseline={snapshot.Baseline}; effective={snapshot.Effective}.");
    }
    public void Dispose()
    {
        _main.VerifyAccess();if(_disposed)return;_disposed=true;
        // Channel disposal restores only values still owned; registry then invalidates leases and releases roots.
        _targets.Invalidated-=OnInvalidated;
        if(_ownsSharedServices)
        {
            try{_mutations.Dispose();}finally{_targets.Dispose();_bindings.Clear();}
        }
        else
        {
            // A UI provider ending must not withdraw another provider's track,
            // note or camera targets from the same public registry.
            foreach(var binding in _bindings.Values.ToArray())
            {
                try{binding.Channel.Dispose();}
                finally{_targets.Invalidate(binding.Target);_bindings.Remove(binding.Source.Identity);}
            }
        }
    }
    private sealed class LegacyOverride:IResourceOverride
    {
        private IMutationLease<ResourceColor>? _lease;public string ResourceKey{get;}
        internal LegacyOverride(string key,IMutationLease<ResourceColor> lease){ResourceKey=key;_lease=lease;}
        public void Dispose(){var lease=_lease;if(lease is null)return;lease.Dispose();_lease=null;}
    }
    private sealed class LegacyView:IGameResourceService
    {
        private readonly GameResourceService _owner;private readonly IPluginContext _context;
        internal LegacyView(GameResourceService owner,IPluginContext context){_owner=owner;_context=context;}
        public OperationResult<IReadOnlyList<GameResourceTarget>> GetTargets(){_context.MainThread.VerifyAccess();return _owner.GetTargets();}
        public OperationResult<IResourceOverride> OverrideColor(GameColorOverrideRequest request)
        { _context.MainThread.VerifyAccess();return _owner.Override(request,_context); }
    }
    private sealed class ColorComparer:IEqualityComparer<ResourceColor>
    {
        internal static ColorComparer Instance{get;}=new();
        public bool Equals(ResourceColor? a,ResourceColor? b)=>a is not null&&b is not null&&Math.Abs(a.R-b.R)<.0001f&&Math.Abs(a.G-b.G)<.0001f&&Math.Abs(a.B-b.B)<.0001f&&Math.Abs(a.A-b.A)<.0001f;
        public int GetHashCode(ResourceColor value)=>0;
    }
}

/// <summary>Only this backend knows Unity native handles. Template/invalid/unloaded scenes are excluded.</summary>
internal sealed class NativeGameColorBackend:IGameColorBackend
{
    private readonly NativeRuntime _api;private readonly IMainThreadDispatcher _main;private readonly ILogger _log;
    private readonly IntPtr _manager,_unityObject,_resources,_scene;
    private readonly NativeGameColorAccessors _image;
    private readonly NativeGameColorAccessors? _tmp;
    private readonly NativeArrayScanCursor _imageCursor=new(),_tmpCursor=new();
    private int _lastScanned=-1;
    internal NativeGameColorBackend(NativeRuntime api,IMainThreadDispatcher main,ILogger log,IntPtr manager)
    {
        _api=api;_main=main;_log=log;_manager=manager;
        _unityObject=api.Class("UnityEngine.CoreModule.dll","UnityEngine","Object");
        _resources=api.Class("UnityEngine.CoreModule.dll","UnityEngine","Resources");
        _image=NativeGameColorAccessors.Bind(api,NativeGameColorSlot.Image);
        _scene=api.Class("UnityEngine.CoreModule.dll","UnityEngine.SceneManagement","Scene");
        try{_tmp=NativeGameColorAccessors.Bind(api,NativeGameColorSlot.TmpText);}
        catch(Exception error)when(error is MissingMemberException or UnsupportedSignatureException or System.Reflection.AmbiguousMatchException)
        {var detail=error.Message;log.Log(LogLevel.Warning,"TMP color slot unavailable; TMP receivers are excluded: "+(detail.Length<=500?detail:detail[..500]));}
    }
    private bool Alive(IntPtr image)=>image!=IntPtr.Zero&&_api.Unbox<bool>(_api.CallStatic(_unityObject,"op_Implicit",A.Ref(image)));
    private bool Loaded(IntPtr image)
    {
        if(!Alive(image))return false;
        var go=_api.Call(image,"get_gameObject");var handle=_api.Unbox<int>(_api.Call(go,"get_scene"));
        return _api.Unbox<bool>(_api.CallStatic(_scene,"IsValidInternal",A.Value(handle)))&&
            _api.Unbox<bool>(_api.CallStatic(_scene,"GetIsLoadedInternal",A.Value(handle)));
    }
    private bool Eligible(IntPtr image)=>Loaded(image)&&_api.Unbox<bool>(_api.Call(image,"get_isActiveAndEnabled"));
    public IEnumerable<GameColorCandidate> Discover(int maximumScanned)
    {
        _main.VerifyAccess();if(maximumScanned<0)throw new ArgumentOutOfRangeException(nameof(maximumScanned));
        if(maximumScanned==0)yield break;
        var aliases=MenuAliases();var seen=new HashSet<IntPtr>();var count=0;
        foreach(var receiver in Receivers(aliases,maximumScanned))
        {
            var image=receiver.Pointer;var slot=receiver.Slot;var accessors=receiver.Accessors;
            if(!seen.Add(image)||!Eligible(image))continue;
            var go=_api.Call(image,"get_gameObject");var transform=_api.Call(go,"get_transform");
            var rootName=_api.Text(_api.Call(_api.Call(transform,"get_root"),"get_name"));
            if(rootName.StartsWith("RizToolbox.",StringComparison.Ordinal))continue;
            var id=_api.Unbox<int>(_api.Call(image,"GetInstanceID"));
            var sceneHandle=_api.Unbox<int>(_api.Call(go,"get_scene"));
            var sceneName=_api.Text(_api.CallStatic(_scene,"GetNameInternal",A.Value(sceneHandle)));
            var name=_api.Text(_api.Call(go,"get_name"));
            var holder=new NativeColorReceiver(_api,image);
            var names=(slot==NativeGameColorSlot.Image&&aliases.TryGetValue(image,out var menu)?menu:Enumerable.Empty<string>())
                .Append($"game.ui/{slot.AliasKind}/{id}/color").ToArray();
            // Pointer/instance identity is shared across aliases and scans. The same receiver never creates competing color channels.
            yield return new($"{id}:{image.ToInt64():x}",$"game.ui/{slot.AliasKind}/{id}",
                new(Scene:sceneName,Role:slot.Role,ResourceId:slot.ResourceId,Description:$"Live game {slot.ReceiverType}.color: {rootName}/{name}"),
                names,()=>Read(holder.Pointer,accessors),value=>Write(holder.Pointer,accessors,value),()=>Loaded(holder.Pointer),()=>Eligible(holder.Pointer),holder.Retain);
            count++;
        }
        if(_lastScanned!=count){_lastScanned=count;_log.Log(LogLevel.Debug,$"Game UI resource scan: {count} live non-template Image/TMP color receivers eligible.");}
    }
    private sealed record Receiver(IntPtr Pointer,NativeGameColorSlot Slot,NativeGameColorAccessors Accessors);
    private IEnumerable<Receiver> Receivers(Dictionary<IntPtr,List<string>> aliases,int maximum)
    {
        // Menu aliases are direct game fields; the remaining shared scan budget is split and interleaved by receiver type.
        // Cursors rotate through actual array indexes so a large prefix of templates cannot permanently hide late receivers.
        var menu=aliases.Keys.Take(maximum).ToArray();
        foreach(var pointer in menu)if(_api.ExactHookReceiverMatches(pointer,_image.ReceiverType))yield return new(pointer,NativeGameColorSlot.Image,_image);
        var remaining=maximum-menu.Length;var imageBudget=_tmp is null?remaining:(remaining+1)/2;var tmpBudget=remaining-imageBudget;
        using var images=Scan(NativeGameColorSlot.Image,_image,_imageCursor,imageBudget).GetEnumerator();
        using var texts=(_tmp is null?Enumerable.Empty<Receiver>():Scan(NativeGameColorSlot.TmpText,_tmp,_tmpCursor,tmpBudget)).GetEnumerator();
        var moreImages=true;var moreTexts=true;
        while(moreImages||moreTexts)
        {
            if(moreImages){moreImages=images.MoveNext();if(moreImages)yield return images.Current;}
            if(moreTexts){moreTexts=texts.MoveNext();if(moreTexts)yield return texts.Current;}
        }
    }
    private IEnumerable<Receiver> Scan(NativeGameColorSlot slot,NativeGameColorAccessors accessors,NativeArrayScanCursor cursor,int maximum)
    {
        if(maximum==0)yield break;
        var array=_api.CallStatic(_resources,"FindObjectsOfTypeAll",A.Ref(_api.Type(accessors.ReceiverType)));
        foreach(var pointer in _api.ArrayItems(array,cursor,maximum))
            if(pointer!=IntPtr.Zero&&_api.ExactHookReceiverMatches(pointer,accessors.ReceiverType))yield return new(pointer,slot,accessors);
    }
    private Dictionary<IntPtr,List<string>> MenuAliases()
    {
        var result=new Dictionary<IntPtr,List<string>>();var gm=_api.ReadStatic<IntPtr>(_manager,"Instance");if(gm==IntPtr.Zero)return result;
        var menu=_api.Read<IntPtr>(gm,"mainMenuControl");if(!Alive(menu))return result;
        var buttons=_api.Read<IntPtr>(menu,"mainPageButtonControls");if(buttons==IntPtr.Zero)return result;
        var count=_api.Unbox<int>(_api.Call(buttons,"get_Count"));if(count<0||count>128)throw new InvalidDataException("Unexpected main menu button count.");
        for(var i=0;i<count;i++)
        {
            var button=_api.Call(buttons,"get_Item",A.Value(i));if(!Alive(button))continue;
            var image=_api.Read<IntPtr>(button,"mainColor");if(!Eligible(image))continue;
            if(!result.TryGetValue(image,out var names))result.Add(image,names=new());
            names.Add($"game.menu/button/{_api.Read<int>(button,"pageIndex")}/color");
        }
        return result;
    }
    private ResourceColor Read(IntPtr image,NativeGameColorAccessors accessors)
    {var value=_api.Unbox<Rgba>(_api.Invoke(accessors.Read,image));return new(value.R,value.G,value.B,value.Alpha);}
    private void Write(IntPtr image,NativeGameColorAccessors accessors,ResourceColor value)
    {if(!Loaded(image))throw new TargetExpiredException("Game color receiver scene is invalid or unloaded.");_api.Invoke(accessors.Write,image,A.Value(new Rgba(value.R,value.G,value.B,value.A)));}
    private sealed class NativeColorReceiver
    {
        private readonly NativeRuntime _api;private readonly IntPtr _original;private NativeRuntime.RootHandle? _root;
        internal NativeColorReceiver(NativeRuntime api,IntPtr image){_api=api;_original=image;}
        internal IntPtr Pointer=>_root?.Target??_original;
        internal IDisposable Retain(){if(_root is not null)throw new InvalidOperationException("Color receiver root already retained.");return _root=_api.Root(_original);}
    }
}
