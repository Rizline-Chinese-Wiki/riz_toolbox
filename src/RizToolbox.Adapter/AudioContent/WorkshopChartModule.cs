using System.Runtime.InteropServices;
using RizToolbox.Adapter.Modules;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed class WorkshopChartModule:IAdapterModule
{
    internal const string Fingerprint="853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private readonly NativeRuntime _api;private readonly IMainThreadDispatcher _main;private readonly ILogger _log;private readonly AdapterBuildProfile _profile;
    private readonly string _dobby;private readonly IGameLifecycleService _lifecycle;private readonly Func<LocalContentService> _content;
    private readonly RunEffectRecorder? _audit;
    private ExactHookBroker.Binding? _hook;private WorkshopChartService? _service;
    private bool _referenceEvidenceCaptured;
    private NativeResourceReferenceFactoryProbe? _referenceFactoryProbe;
    private LocalNativeMetadataProbe? _localMetadataProbe;
    private LocalNativeParametersProbe? _localParametersProbe;
    private LocalInactiveControllerProbe? _inactiveControllerProbe;
    private LocalNativeBundleProbe? _nativeBundleProbe;
    internal WorkshopChartModule(NativeRuntime api,IMainThreadDispatcher main,ILogger log,AdapterBuildProfile profile,string dobby,
        IGameLifecycleService lifecycle,Func<LocalContentService> content,RunEffectRecorder? audit=null){_api=api;_main=main;_log=log;_profile=profile;_dobby=dobby;_lifecycle=lifecycle;_content=content;_audit=audit;}
    public AdapterModuleManifest Manifest{get;}=new("rizline.content.workshop-chart",new(1,0),new[]{AudioContentCapabilityIds.WorkshopChartReplacement},
        new[]{AudioContentCapabilityIds.LocalContent},new[]{"*"},new[]{
            new AdapterSymbolRequirement("workshop.chart","Assembly-CSharp.dll","","WorkshopLevel",AdapterSymbolKind.Method,"LoadChart",false,Array.Empty<string>()){ExpectedType="InLevel.Chart"},
            new AdapterSymbolRequirement("workshop.ids","Assembly-CSharp.dll","","WorkshopLevels",AdapterSymbolKind.Method,"get_LevelsById",true,Array.Empty<string>()){ExpectedType="System.Collections.Generic.IReadOnlyDictionary<System.UInt64,WorkshopLevel>"},
            new AdapterSymbolRequirement("chart.version","Assembly-CSharp.dll","InLevel","Chart",AdapterSymbolKind.Field,"fileVersion"){ExpectedType="System.Int32"},
            new AdapterSymbolRequirement("chart.bpm","Assembly-CSharp.dll","InLevel","Chart",AdapterSymbolKind.Field,"bPM"){ExpectedType="System.Single"},
            new AdapterSymbolRequirement("chart.lines","Assembly-CSharp.dll","InLevel","Chart",AdapterSymbolKind.Field,"lines"){ExpectedType="System.Collections.Generic.List<InLevel.Line>"},
            new AdapterSymbolRequirement("chart.parse","UnityEngine.JSONSerializeModule.dll","UnityEngine","JsonUtility",AdapterSymbolKind.Method,"FromJson",true,new[]{"System.String","System.Type"}){ExpectedType="System.Object"}},
        "Experimental existing workshop chart replacement only. Known GameAssembly SHA256 and MethodInfo+RVA 3E0D00; static direct call at 3C1138 passes result to InsideLevelParams.ctor at 3C1251. No new catalog entries or local music/cover resolution is declared.");
    public double IntervalSeconds=>1;
    public ModuleProbe Probe()
    {
        _main.VerifyAccess();if(_hook is not null)return ModuleProbe.Available;
        if(_profile.GameAssemblySha256!=Fingerprint||_profile.UnityVersion!="2022.3.62f2"||!OperatingSystem.IsWindows()||IntPtr.Size!=8)
            return ModuleProbe.Unsupported("UnsupportedBuild: the workshop parser hook is pinned to Steam 25977441 Windows x64.");
        var file=Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!,"GameAssembly.dll");
        if(FileHash.ComputeSha256(file)!=Fingerprint)return ModuleProbe.Unsupported("UnsupportedBuild: on-disk GameAssembly differs from the parser hook profile.");
        // This marker remains metadata-only; native resource operations require their own marker.
        var referenceMarker=Path.Combine(Path.GetDirectoryName(file)!,"BepInEx","RizToolbox","probes","local-resource-reference.enabled");
        if(!_referenceEvidenceCaptured&&File.Exists(referenceMarker))
        {
            _referenceEvidenceCaptured=true;
            try
            {
                var evidence=new LocalResourceReferenceDiagnostics(_api,_profile).CaptureMetadata(GetModuleHandle("GameAssembly.dll"));
                _log.Log(LogLevel.Info,"LocalResourceReferenceProbe metadata "+System.Text.Json.JsonSerializer.Serialize(new{evidence.Stage,evidence.Build,evidence.CapturedUtc,count=evidence.Evidence.Count}));
                // The structured logger bounds each message at 2048 characters. One row per
                // record keeps exact signatures and addresses intact instead of truncating a report.
                for(var index=0;index<evidence.Evidence.Count;index++)
                {
                    var row=System.Text.Json.JsonSerializer.Serialize(new{index,evidence=evidence.Evidence[index]});
                    if(row.Length>1800)throw new InvalidDataException("Resource reference evidence row exceeds its diagnostic bound: "+index);
                    _log.Log(LogLevel.Info,"LocalResourceReferenceProbe row "+row);
                }
            }
            catch(Exception error){_log.Log(LogLevel.Warning,"LocalResourceReferenceProbe metadata unavailable: "+error.GetType().Name+": "+error.Message);}
            try
            {
                var closed=new LocalResourceReferenceDiagnostics(_api,_profile).CaptureClosedWrapperMetadata(GetModuleHandle("GameAssembly.dll"));
                LocalResourceReferenceOwnerProbe.WriteReport(_log,closed,"closed-wrapper","wrapper-row");
            }
            catch(Exception error){try{_log.Log(LogLevel.Warning,"LocalResourceReferenceProbe closed-wrapper unavailable: "+error.GetType().Name+": "+error.Message);}catch{}}
        }
        var factoryMarker=Path.Combine(Path.GetDirectoryName(file)!,"BepInEx","RizToolbox","probes","local-resource-reference-factory.enabled");
        if(_referenceFactoryProbe is null&&File.Exists(factoryMarker))
        {
            _referenceFactoryProbe = new(_api, _main, _log, _profile);
            _referenceFactoryProbe.TryStart();
        }
        var metadataMarker=Path.Combine(Path.GetDirectoryName(file)!,"BepInEx","RizToolbox","probes","local-native-metadata.enabled");
        if(_localMetadataProbe is null&&File.Exists(metadataMarker))
        {
            _localMetadataProbe = new(_api, _main, _log, _profile, GetModuleHandle("GameAssembly.dll"));
            _localMetadataProbe.TryStart();
        }
        var parametersMarker=Path.Combine(Path.GetDirectoryName(file)!,"BepInEx","RizToolbox","probes","local-native-parameters.enabled");
        if(_localParametersProbe is null&&File.Exists(parametersMarker))
        {
            _localParametersProbe = new(_api, _main, _log, _profile, GetModuleHandle("GameAssembly.dll"));
            _localParametersProbe.TryStart();
        }
        var inactiveMarker=Path.Combine(Path.GetDirectoryName(file)!,"BepInEx","RizToolbox","probes","local-inactive-controller.enabled");
        if(_inactiveControllerProbe is null&&File.Exists(inactiveMarker))
        {
            _inactiveControllerProbe = new(_api, _main, _log, _profile, GetModuleHandle("GameAssembly.dll"), new ExactHookBroker(_dobby, _main, _log));
            _inactiveControllerProbe.TryStart();
        }
        var bundleMarker=Path.Combine(Path.GetDirectoryName(file)!,"BepInEx","RizToolbox","probes","local-native-bundle.enabled");
        if(_nativeBundleProbe is null&&File.Exists(bundleMarker))
        {
            _nativeBundleProbe = new(_api, _main, _log, _profile, GetModuleHandle("GameAssembly.dll"));
            _nativeBundleProbe.TryStart();
        }
        var declaring=_api.Class("Assembly-CSharp.dll","","WorkshopLevel");
        var target=_api.ResolveExactHook("game.workshop.chart.resolve",GetModuleHandle("GameAssembly.dll"),declaring,"LoadChart",Array.Empty<string>(),0x3E0D00,ExactHookAbi.InstanceReferenceReturn0);
        var parser=new NativeChartParser(_api);var backend=new NativeWorkshopChartBackend(_api);var store=_content();
        try
        {
            _service=new(_main,_log,store,_lifecycle,backend.Resolve,template=>parser.Instantiate(template.Source),
                chart=>chart is ParsedNativeChart native?native.Retain():throw new InvalidDataException("Chart belongs to another parser backend."),backend.IsCurrent,_audit);
            var service=_service;
            _hook=new ExactHookBroker(_dobby,_main,_log).BindReferenceResult(target,
                receiver=>_api.ExactHookReceiverMatches(receiver,declaring),receiver=>_api.Root(receiver),value=>_api.RetainExactHookResult(target,value),
                (receiver,original)=>service.AdoptResult(receiver));
            store.SetChartParser(parser);return ModuleProbe.Available;
        }
        catch{Release();throw;}
    }
    public void Register(ModuleRegistration registration)=>registration.RegisterScoped<ILocalChartReplacementService>(AudioContentCapabilityIds.WorkshopChartReplacement,c=>_service!.ForPlugin(c));
    public ModuleProbe Tick(){_nativeBundleProbe?.Tick();_inactiveControllerProbe?.Tick();_localParametersProbe?.Tick();_referenceFactoryProbe?.Tick();_service!.Refresh();return ModuleProbe.Available;}
    public void Release()
    {
        _nativeBundleProbe?.Dispose();_nativeBundleProbe=null;
        _inactiveControllerProbe?.Dispose();_inactiveControllerProbe=null;
        _localParametersProbe?.Dispose();_localParametersProbe=null;
        _localMetadataProbe?.Dispose();_localMetadataProbe=null;
        _referenceFactoryProbe?.Dispose();_referenceFactoryProbe=null;
        _hook?.Dispose();_hook=null;try{_content().SetChartParser(null);}catch(InvalidOperationException){}
        _service?.Dispose();_service=null;
    }
    [DllImport("kernel32",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string name);
}
