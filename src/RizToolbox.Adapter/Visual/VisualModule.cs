using RizToolbox.Adapter.Modules;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Visual;

/// <summary>Each visual resource class probes and recovers independently. Hosting supplies approved file roots.</summary>
internal sealed class VisualModule:BuiltinModule
{
    private readonly VisualTargetKind _kind;private readonly VisualFileAccess _files;private readonly VisualResourceBudget _budget;
    private readonly VisualResourceRetirement _retirement;
    private VisualResourceService? _service;private double _ticks;
    internal VisualModule(NativeRuntime api,IMainThreadDispatcher main,ILogger log,VisualTargetKind kind,
        Func<string,string?> packageDirectoryByPluginId,Func<string,string?> dataDirectoryByPluginId,VisualResourceBudget? sharedBudget=null,VisualResourceRetirement? retirement=null)
        :base(api,main,log,Define(kind))
    {_kind=kind;_files=new(packageDirectoryByPluginId,dataDirectoryByPluginId);_budget=sharedBudget??new();_retirement=retirement??new(main,log);}
    internal static string Capability(VisualTargetKind kind)=>kind switch{VisualTargetKind.Sprite=>VisualCapabilityIds.Sprites,VisualTargetKind.Font=>VisualCapabilityIds.Fonts,_=>VisualCapabilityIds.Materials};
    private static AdapterModuleManifest Define(VisualTargetKind kind)
    {
        var component=kind switch{VisualTargetKind.Sprite=>"Image",VisualTargetKind.Font=>"Text",_=>"Graphic"};var property=kind.ToString().ToLowerInvariant();
        var value=kind switch{VisualTargetKind.Sprite=>"UnityEngine.Sprite",VisualTargetKind.Font=>"UnityEngine.Font",_=>"UnityEngine.Material"};
        var symbols=new List<AdapterSymbolRequirement>{
            Method("targets.scan","UnityEngine.CoreModule.dll","UnityEngine","Resources","FindObjectsOfTypeAll",true,"System.Type") with {ExpectedType="UnityEngine.Object[]"},
            Method("target.read","UnityEngine.UI.dll","UnityEngine.UI",component,"get_"+property) with {ExpectedType=value},
            Method("target.write","UnityEngine.UI.dll","UnityEngine.UI",component,"set_"+property,false,value) with {ExpectedType="System.Void"},
            Method("target.active","UnityEngine.CoreModule.dll","UnityEngine","Behaviour","get_isActiveAndEnabled") with {ExpectedType="System.Boolean"},
            Method("scene.valid","UnityEngine.CoreModule.dll","UnityEngine.SceneManagement","Scene","IsValidInternal",true,"System.Int32") with {ExpectedType="System.Boolean"},
            Method("scene.loaded","UnityEngine.CoreModule.dll","UnityEngine.SceneManagement","Scene","GetIsLoadedInternal",true,"System.Int32") with {ExpectedType="System.Boolean"}};
        if(kind==VisualTargetKind.Sprite)symbols.AddRange(new[]{
            Method("texture.new","UnityEngine.CoreModule.dll","UnityEngine","Texture2D",".ctor",false,"System.Int32","System.Int32") with {ExpectedType="System.Void"},
            Method("texture.format","UnityEngine.CoreModule.dll","UnityEngine","Texture2D","Reinitialize",false,"System.Int32","System.Int32","UnityEngine.TextureFormat","System.Boolean") with {ExpectedType="System.Boolean"},
            Method("texture.upload","UnityEngine.CoreModule.dll","UnityEngine","Texture2D","LoadRawTextureData",false,"System.IntPtr","System.Int32") with {ExpectedType="System.Void"},
            Method("texture.apply","UnityEngine.CoreModule.dll","UnityEngine","Texture2D","Apply",false,"System.Boolean","System.Boolean") with {ExpectedType="System.Void"},
            Method("sprite.new","UnityEngine.CoreModule.dll","UnityEngine","Sprite","Create",true,"UnityEngine.Rect","UnityEngine.Vector2","System.Single","UnityEngine.Texture2D") with {ExpectedType="UnityEngine.Sprite"}});
        if(kind==VisualTargetKind.Font)symbols.AddRange(new[]{Type("font.type","UnityEngine.TextRenderingModule.dll","UnityEngine","Font"),
            Method("font.builtin","UnityEngine.CoreModule.dll","UnityEngine","Resources","GetBuiltinResource",true,"System.Type","System.String") with {ExpectedType="UnityEngine.Object"}});
        if(kind==VisualTargetKind.Material)symbols.AddRange(new[]{
            Method("material.clone","UnityEngine.CoreModule.dll","UnityEngine","Object","Instantiate",true,"UnityEngine.Object") with {ExpectedType="UnityEngine.Object"},
            Method("material.shader","UnityEngine.CoreModule.dll","UnityEngine","Material","get_shader") with {ExpectedType="UnityEngine.Shader"},
            Method("material.has","UnityEngine.CoreModule.dll","UnityEngine","Material","HasProperty",false,"System.String") with {ExpectedType="System.Boolean"},
            Method("material.color.read","UnityEngine.CoreModule.dll","UnityEngine","Material","GetColor",false,"System.String") with {ExpectedType="UnityEngine.Color"},
            Method("material.color.write","UnityEngine.CoreModule.dll","UnityEngine","Material","SetColor",false,"System.String","UnityEngine.Color") with {ExpectedType="System.Void"},
            Method("material.float.read","UnityEngine.CoreModule.dll","UnityEngine","Material","GetFloat",false,"System.String") with {ExpectedType="System.Single"},
            Method("material.float.write","UnityEngine.CoreModule.dll","UnityEngine","Material","SetFloat",false,"System.String","System.Single") with {ExpectedType="System.Void"},
            Method("material.reference.load","UnityEngine.CoreModule.dll","UnityEngine","Resources","Load",true,"System.String","System.Type") with {ExpectedType="UnityEngine.Object"},
            Method("material.shader.equals","UnityEngine.CoreModule.dll","UnityEngine","Object","op_Equality",true,"UnityEngine.Object","UnityEngine.Object") with {ExpectedType="System.Boolean"}});
        return Definition("rizline.visual."+property,new[]{Capability(kind)},symbols.ToArray(),
            "Exact dump signatures and bounded managed tests; native visual setters need separate runtime acceptance. Optional typed TMP fontSharedMaterial accessors are validated separately and missing slots are excluded. Material parameters require a pinned serialized-asset profile, exact typed resource shader identity and clone read-back; unknown schemas are refused.",scenes:Array.Empty<string>());
    }
    public override ModuleProbe Probe()
    {
        if(_retirement.HasQuarantined(_kind))return ModuleProbe.Unsupported(VisualResourceService.CleanupQuarantinedReason);
        if(_retirement.HasPending(_kind))return ModuleProbe.Waiting("Visual cleanup pending; previous native attachments remain isolated and rooted.");
        _service??=new(Main,Log,_kind,new NativeVisualBackend(Api,Main,_kind,Log),_files,_budget,_retirement);
        return Reconcile(_service,true);
    }
    public override void Register(ModuleRegistration registration)=>registration.RegisterScoped<IVisualResourceService>(Capability(_kind),context=>_service!.ForPlugin(context));
    public override ModuleProbe Tick()
    {
        if(_retirement.HasQuarantined(_kind))return ModuleProbe.Unsupported(VisualResourceService.CleanupQuarantinedReason);
        if(_retirement.HasPending(_kind))return ModuleProbe.Waiting("Visual cleanup pending; previous native attachments remain isolated and rooted.");
        // Full Resources scan every five ticks; ownership/read-back reconciliation still runs every tick.
        return Reconcile(_service!,++_ticks%5==0);
    }
    internal static ModuleProbe Reconcile(VisualResourceService service,bool discover)
    {
        if(service.CleanupQuarantined)return ModuleProbe.Unsupported(VisualResourceService.CleanupQuarantinedReason);
        if(service.CleanupPending)return ModuleProbe.Waiting(VisualResourceService.CleanupPendingReason);
        service.Refresh(discover);
        // Refresh may retire an unreadable/unloaded target while others remain. Withdraw
        // in this same adapter tick, before host FrameUpdate subscribers can query it.
        // Probe reaches this point only after the required symbols, backend construction and
        // bounded discovery succeeded. No eligible receiver is a normal Waiting intent state;
        // withdrawing that verified provider would otherwise destroy every pending intent.
        return service.CleanupQuarantined?ModuleProbe.Unsupported(VisualResourceService.CleanupQuarantinedReason):
            service.CleanupPending?ModuleProbe.Waiting(VisualResourceService.CleanupPendingReason):
            ModuleProbe.Available;
    }
    public override void Release()
    {
        _service?.Dispose();
        _service = null;
        _ticks = 0;
    }
}
