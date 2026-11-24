using RizToolbox.Adapter.Modules;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Hosting registers these modules with ModuleManager. No Loader-specific branch or process launch is needed.</summary>
internal sealed class AudioContentModules
{
    private readonly Shared _shared;
    private readonly LocalRunProtectionModule? _protection;
    internal IReadOnlyList<IAdapterModule> Modules { get; }
    internal bool RetainsResultUi => _protection?.RetainsResultUi == true;

    private AudioContentModules(Shared shared, IReadOnlyList<IAdapterModule> modules, LocalRunProtectionModule? protection)
        => (_shared, Modules, _protection) = (shared, modules, protection);

    internal void Maintain(double now, ModuleManager manager)
    {
        // The lifecycle modules have finished publishing this frame's observation.
        _protection?.ObserveRunModification();
        if (!_shared.Maintain(now)) return;
        // A long native retirement can outlast the normal probe retry window.
        // Wake waiting consumers when there is actual evidence of release.
        foreach (var module in Modules)
            if (OwnsAudioCapability(module)) manager.RequestProbe(module.Manifest.Id);
    }

    internal static AudioContentModules Create(NativeRuntime api,IMainThreadDispatcher main,ILogger log,
        Func<string,string?> packageDirectoryByPluginId,Func<string,string?> dataDirectoryByPluginId,
        AdapterBuildProfile? profile=null,string? dobbyPath=null,IGameLifecycleService? lifecycle=null,RunEffectRecorder? audit=null,
        Func<IOwnedUiService>? hostUi=null, Func<IAudioBackend>? audioBackend=null)
    {
        var files=new ContentFileAccess(packageDirectoryByPluginId,dataDirectoryByPluginId);
        var shared=new Shared(main,log,files,audioBackend ?? (() => new NativeAudioBackend(api,main)),audit);
        var content=new LocalContentModule(api,main,log,files);
        var protection = profile is not null && dobbyPath is not null && lifecycle is not null
            ? new LocalRunProtectionModule(api, main, log, profile, dobbyPath, hostUi,
                lifecycle is GameEventState state && audit is not null ? launch => new(state, audit, launch) : null) : null;
        var catalog = new LocalCatalogModule(api, main, log, () => content.Service, protection);
        var modules=new List<IAdapterModule>{new AudioModule(api,main,log,shared,AudioContentCapabilityIds.Playback),
            new AudioModule(api,main,log,shared,AudioContentCapabilityIds.Replacement),new AudioModule(api,main,log,shared,AudioContentCapabilityIds.Mix),
            new AudioModule(api,main,log,shared,AudioContentCapabilityIds.Clock),content,catalog,
            new LocalCatalogSelectionModule(api, main, log, catalog, requiresResultUi: protection is not null && hostUi is not null)};
        if(profile is not null&&dobbyPath is not null&&lifecycle is not null)
        {
            modules.Add(protection!);
            modules.Add(new WorkshopChartModule(api,main,log,profile,dobbyPath,lifecycle,()=>content.Service,audit));
        }
        return new(shared, modules.AsReadOnly(), protection);
    }
    private sealed class Shared
    {
        private readonly Func<IAudioBackend> _createBackend;
        private readonly IMainThreadDispatcher _main;
        private readonly ILogger _log;
        private readonly ContentFileAccess _files;
        private readonly RunEffectRecorder? _audit;
        private AudioService? _service;
        private double _nextRefresh, _nextDiscovery;
        private ModuleProbe? _maintenanceFailure;

        internal Shared(IMainThreadDispatcher main, ILogger log, ContentFileAccess files, Func<IAudioBackend> createBackend, RunEffectRecorder? audit)
            => (_main, _log, _files, _createBackend, _audit) = (main, log, files, createBackend, audit);

        internal ModuleProbe Activate(string cap)
        {
            _main.VerifyAccess();
            if (_service is { IsRetiring: true }) return ModuleProbe.Waiting("Previous audio resources are still retiring.");
            var service = _service ??= new(_main, _log, _createBackend(), _files, audit: _audit);
            if (cap == AudioContentCapabilityIds.Playback) service.ProbeUpload();
            if (cap == AudioContentCapabilityIds.Clock && !service.ProbeClock()) return ModuleProbe.Waiting("DSP clock is not ready.");
            var hadSources = service.HasSourceDemand;
            service.Enable(cap);
            if (!hadSources && service.HasSourceDemand)
            {
                _nextRefresh = _nextDiscovery = 0;
                _maintenanceFailure = null;
            }
            return ModuleProbe.Available;
        }

        internal object ForPlugin(IPluginContext context, string cap)
            => (_service ?? throw new InvalidOperationException("The audio service is not active.")).ForPlugin(context, cap);

        internal ModuleProbe Health(string cap)
            => cap is AudioContentCapabilityIds.Replacement or AudioContentCapabilityIds.Mix
                ? _maintenanceFailure ?? ModuleProbe.Available : ModuleProbe.Available;

        internal void Release(string cap)
        {
            if (_service is not { } service) return;
            service.Disable(cap);
            if (!service.HasDemand) service.Dispose();
            if (service.IsDisposed) _service = null;
        }

        internal bool Maintain(double now)
        {
            _main.VerifyAccess();
            if (_service is not { } service || !service.NeedsMaintenance || now < _nextRefresh) return false;
            _nextRefresh = now + 1;
            var discover = service.HasSourceDemand && now >= _nextDiscovery;
            if (discover) _nextDiscovery = now + 5;
            try { service.Refresh(discover); }
            catch (Exception error)
            {
                _maintenanceFailure = ModuleProbe.Waiting("Shared audio maintenance failed: " + error.Message);
                try { _log.Log(LogLevel.Warning, "Shared audio maintenance failed.", error); } catch { }
            }
            if (!service.IsDisposed) return false;
            _service = null;
            return true;
        }
    }
    internal static bool OwnsAudioCapability(IAdapterModule module) => module is AudioModule;

    private sealed class AudioModule:BuiltinModule
    {
        private readonly Shared _shared;private readonly string _cap;
        internal AudioModule(NativeRuntime api,IMainThreadDispatcher main,ILogger log,Shared shared,string cap):base(api,main,log,Define(cap)){_shared=shared;_cap=cap;}
        private static AdapterModuleManifest Define(string cap)
        {
            var symbols=new List<AdapterSymbolRequirement>();
            void Audio(string id,string type,string name,string result,bool stat=false,params string[] args)=>symbols.Add(Method(id,"UnityEngine.AudioModule.dll","UnityEngine",type,name,stat,args)with{ExpectedType=result});
            if(cap==AudioContentCapabilityIds.Clock)Audio("dsp.read","AudioSettings","get_dspTime","System.Double",true);
            else
            {
                symbols.Add(Method("object.alive","UnityEngine.CoreModule.dll","UnityEngine","Object","op_Implicit",true,"UnityEngine.Object")with{ExpectedType="System.Boolean"});
                if(cap==AudioContentCapabilityIds.Playback)
                {
                    Audio("clip.create","AudioClip","Create","UnityEngine.AudioClip",true,"System.String","System.Int32","System.Int32","System.Int32","System.Boolean");
                    Audio("clip.upload","AudioClip","SetData","System.Boolean",false,"System.Single[]","System.Int32");
                    Audio("clip.frames","AudioClip","get_samples","System.Int32");Audio("clip.channels","AudioClip","get_channels","System.Int32");Audio("clip.frequency","AudioClip","get_frequency","System.Int32");
                    Audio("source.play","AudioSource","Play","System.Void");Audio("source.stop","AudioSource","Stop","System.Void");Audio("source.playing","AudioSource","get_isPlaying","System.Boolean");
                    Audio("source.clip.write","AudioSource","set_clip","System.Void",false,"UnityEngine.AudioClip");Audio("source.volume.write","AudioSource","set_volume","System.Void",false,"System.Single");
                    foreach(var property in new[]{"loop","playOnAwake"})Audio("source."+property,"AudioSource","set_"+property,"System.Void",false,"System.Boolean");
                    Audio("source.spatial","AudioSource","set_spatialBlend","System.Void",false,"System.Single");
                    symbols.Add(Method("source.new","UnityEngine.CoreModule.dll","UnityEngine","GameObject",".ctor",false,"System.String")with{ExpectedType="System.Void"});
                    symbols.Add(Method("source.component","UnityEngine.CoreModule.dll","UnityEngine","GameObject","AddComponent",false,"System.Type")with{ExpectedType="UnityEngine.Component"});
                    symbols.Add(Method("object.destroy","UnityEngine.CoreModule.dll","UnityEngine","Object","Destroy",true,"UnityEngine.Object")with{ExpectedType="System.Void"});
                }
                else
                {
                    symbols.Add(Method("source.scan","UnityEngine.CoreModule.dll","UnityEngine","Resources","FindObjectsOfTypeAll",true,"System.Type")with{ExpectedType="UnityEngine.Object[]"});
                    Audio("clip.read","AudioSource","get_clip","UnityEngine.AudioClip");Audio("source.playing","AudioSource","get_isPlaying","System.Boolean");
                    Audio("source.stop","AudioSource","Stop","System.Void");
                    if(cap==AudioContentCapabilityIds.Replacement)Audio("clip.write","AudioSource","set_clip","System.Void",false,"UnityEngine.AudioClip");
                    if(cap==AudioContentCapabilityIds.Mix)foreach(var property in new[]{"volume","pitch"})
                    {Audio(property+".read","AudioSource","get_"+property,"System.Single");Audio(property+".write","AudioSource","set_"+property,"System.Void",false,"System.Single");}
                    symbols.Add(Method("source.active","UnityEngine.CoreModule.dll","UnityEngine","Behaviour","get_isActiveAndEnabled")with{ExpectedType="System.Boolean"});
                    symbols.Add(Method("scene.valid","UnityEngine.CoreModule.dll","UnityEngine.SceneManagement","Scene","IsValidInternal",true,"System.Int32")with{ExpectedType="System.Boolean"});
                    symbols.Add(Method("scene.loaded","UnityEngine.CoreModule.dll","UnityEngine.SceneManagement","Scene","GetIsLoadedInternal",true,"System.Int32")with{ExpectedType="System.Boolean"});
                }
            }
            return Definition("rizline."+cap[4..],new[]{cap},symbols.ToArray(),
                "Exact Unity AudioModule signatures from dump.cs 157080..157186; PCM upload probes one silent frame. Playback, AudioSource setters, delay and result effects still require real-game acceptance.",scenes:Array.Empty<string>());
        }
        public override ModuleProbe Probe() => _shared.Activate(_cap);
        public override void Register(ModuleRegistration registration)
        {
            switch(_cap)
            {
                case AudioContentCapabilityIds.Playback:registration.RegisterScoped<IAudioPlaybackService>(_cap,c=>(IAudioPlaybackService)_shared.ForPlugin(c,_cap));break;
                case AudioContentCapabilityIds.Replacement:registration.RegisterScoped<IAudioReplacementService>(_cap,c=>(IAudioReplacementService)_shared.ForPlugin(c,_cap));break;
                case AudioContentCapabilityIds.Mix:registration.RegisterScoped<IAudioMixService>(_cap,c=>(IAudioMixService)_shared.ForPlugin(c,_cap));break;
                case AudioContentCapabilityIds.Clock:registration.RegisterScoped<IAudioClockService>(_cap,c=>(IAudioClockService)_shared.ForPlugin(c,_cap));break;
            }
        }
        public override ModuleProbe Tick()=>_shared.Health(_cap);
        public override void Release()=>_shared.Release(_cap);
    }
    internal sealed class LocalContentModule:BuiltinModule
    {
        private readonly ContentFileAccess _files;private LocalContentService? _service;
        internal LocalContentModule(NativeRuntime api,IMainThreadDispatcher main,ILogger log,ContentFileAccess files):base(api,main,log,
            Definition("rizline.content.local",new[]{AudioContentCapabilityIds.LocalContent,AudioContentCapabilityIds.LocalContentBatch},Array.Empty<AdapterSymbolRequirement>(),
            "Host-side atomic validation and retained CPU content. Charts require the independent experimental known-build WorkshopLevel.LoadChart boundary; audio/cover registration alone does not publish a playable song.",scenes:Array.Empty<string>()))=>_files=files;
        internal LocalContentService Service=>_service??throw new InvalidOperationException("The content registry module is not active.");
        public override ModuleProbe Probe()
        {
            // A failed native free cannot be made safe by discarding the store
            // and starting a fresh quota budget during module recovery.
            if(_service is {IsDisposed:true})
            {
                if(_service.HasUnconfirmedCleanup)return ModuleProbe.Unsupported("Content cleanup remains unconfirmed; retained resources require a process restart before this provider can recover.");
                if(_service.RetainedCount!=0)return ModuleProbe.Waiting("Previous content consumers still retain resources; their quota reservations must end before this provider can recover.");
                _service=null;
            }
            _service??=new(Main,_files);return ModuleProbe.Available;
        }
        public override void Register(ModuleRegistration registration)
        {
            registration.RegisterScoped<ILocalContentProviderService>(AudioContentCapabilityIds.LocalContent,c=>_service!.ForPlugin(c));
            registration.RegisterScoped<ILocalContentBatchProviderService>(AudioContentCapabilityIds.LocalContentBatch,c=>_service!.BatchForPlugin(c));
        }
        public override ModuleProbe Tick()=>ModuleProbe.Available;
        public override void Release()
        {
            if(_service is null)return;
            _service.Dispose();
            if(_service.HasUnconfirmedCleanup)throw new InvalidOperationException("Content cleanup remains unconfirmed; retaining the store and its quota reservations until process exit.");
            if(_service.RetainedCount==0)_service=null;
        }
    }
}
