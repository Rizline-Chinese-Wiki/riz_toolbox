using System.Diagnostics;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A=RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal interface IAudioClipObject
{
    string Identity { get; }
    bool IsAlive { get; }
    int SampleFrames { get; }
    int Channels { get; }
    int Frequency { get; }
    IDisposable Retain();
    void DestroyOwned();
}
internal sealed record AudioCandidate(string Identity,string Alias,TargetMetadata Metadata,IReadOnlyList<string> Aliases,
    Func<IAudioClipObject> Read,Action<IAudioClipObject> Write,Func<bool> IsAlive,Func<bool> IsEligible,
    Func<bool> IsPlaying,Action Stop,Func<float> ReadVolume,Action<float> WriteVolume,Func<float> ReadPitch,Action<float> WritePitch,Func<IDisposable> Retain);
internal interface IOwnAudioSource:IDisposable { bool IsPlaying { get; } void Stop(); }
internal interface IAudioBackend
{
    IEnumerable<AudioCandidate> Discover(int maximum);
    IAudioClipObject Create(PcmAudio audio);
    IOwnAudioSource Play(IAudioClipObject clip,float volume,bool loop);
    AudioClockSnapshot Clock();
}

internal sealed class NativeAudioBackend:IAudioBackend
{
    private readonly NativeRuntime _api;private readonly IMainThreadDispatcher _main;
    private readonly IntPtr _object,_resources,_source,_clip,_scene;
    internal NativeAudioBackend(NativeRuntime api,IMainThreadDispatcher main)
    {
        _api=api;_main=main;_object=api.Class("UnityEngine.CoreModule.dll","UnityEngine","Object");
        _resources=api.Class("UnityEngine.CoreModule.dll","UnityEngine","Resources");_scene=api.Class("UnityEngine.CoreModule.dll","UnityEngine.SceneManagement","Scene");
        _source=api.Class("UnityEngine.AudioModule.dll","UnityEngine","AudioSource");_clip=api.Class("UnityEngine.AudioModule.dll","UnityEngine","AudioClip");
    }
    private bool Alive(IntPtr value)=>value!=IntPtr.Zero&&_api.Unbox<bool>(_api.CallStatic(_object,"op_Implicit",A.Ref(value)));
    private bool Loaded(IntPtr value)
    {
        if(!Alive(value))return false;var go=_api.Call(value,"get_gameObject");var handle=_api.Unbox<int>(_api.Call(go,"get_scene"));
        return _api.Unbox<bool>(_api.CallStatic(_scene,"IsValidInternal",A.Value(handle)))&&_api.Unbox<bool>(_api.CallStatic(_scene,"GetIsLoadedInternal",A.Value(handle)));
    }
    public IEnumerable<AudioCandidate> Discover(int maximum)
    {
        _main.VerifyAccess();var found=_api.CallStatic(_resources,"FindObjectsOfTypeAll",A.Ref(_api.Type(_source)));var seen=0;
        foreach(var pointer in _api.ArrayItems(found))
        {
            if(seen++>=maximum)yield break;if(!Loaded(pointer)||!_api.Unbox<bool>(_api.Call(pointer,"get_isActiveAndEnabled")))continue;
            var go=_api.Call(pointer,"get_gameObject");var root=_api.Call(_api.Call(go,"get_transform"),"get_root");
            var rootName=_api.Text(_api.Call(root,"get_name"));if(rootName.StartsWith("RizToolbox.",StringComparison.Ordinal))continue;
            var name=_api.Text(_api.Call(go,"get_name"));var id=_api.Unbox<int>(_api.Call(pointer,"GetInstanceID"));var alias="game.audio/source/"+id;
            var handle=_api.Unbox<int>(_api.Call(go,"get_scene"));var sceneName=_api.Text(_api.CallStatic(_scene,"GetNameInternal",A.Value(handle)));
            var holder=new SourceRoot(_api,pointer);
            yield return new($"{id}:{pointer.ToInt64():x}",alias,new(Scene:sceneName,Role:"audio-source",ResourceId:"unity.audio-source.clip",Description:rootName+"/"+name),
                new[]{alias,"game.audio/name/"+name},()=>Wrap(_api.Call(holder.Pointer,"get_clip")),
                clip=>{if(!Loaded(holder.Pointer))throw new TargetExpiredException("Audio source scene is unloaded.");_api.Call(holder.Pointer,"set_clip",A.Ref(Pointer(clip)));},
                ()=>Loaded(holder.Pointer),()=>Loaded(holder.Pointer)&&_api.Unbox<bool>(_api.Call(holder.Pointer,"get_isActiveAndEnabled")),
                ()=>Alive(holder.Pointer)&&_api.Unbox<bool>(_api.Call(holder.Pointer,"get_isPlaying")),()=>_api.Call(holder.Pointer,"Stop"),
                ()=>_api.Unbox<float>(_api.Call(holder.Pointer,"get_volume")),v=>_api.Call(holder.Pointer,"set_volume",A.Value(v)),
                ()=>_api.Unbox<float>(_api.Call(holder.Pointer,"get_pitch")),v=>_api.Call(holder.Pointer,"set_pitch",A.Value(v)),holder.Retain);
        }
    }
    private IntPtr Pointer(IAudioClipObject clip)=>clip is NativeClip native&&ReferenceEquals(native.Backend,this)?native.Pointer:throw new InvalidOperationException("Audio backend mismatch.");
    private NativeClip Wrap(IntPtr pointer)=>new(this,pointer);
    public IAudioClipObject Create(PcmAudio audio)
    {
        _main.VerifyAccess();var method=_api.Method(_clip,"Create",5,true,new[]{"System.String","System.Int32","System.Int32","System.Int32","System.Boolean"});
        var pointer=_api.Invoke(method,IntPtr.Zero,_api.Str("RizToolbox.PCM"),A.Value(audio.SampleFrames),A.Value(audio.Channels),A.Value(audio.Frequency),A.Value(false));
        if(!Alive(pointer))throw new InvalidOperationException("AudioClip.Create did not return a live clip.");using var root=_api.Root(pointer);
        try
        {
            using var data=_api.CreateSingleArray(audio.Samples);
            if(!_api.Unbox<bool>(_api.CallTyped(root.Target,"SetData",new[]{"System.Single[]","System.Int32"},A.Ref(data.Target),A.Value(0))))throw new InvalidOperationException("AudioClip.SetData refused the decoded PCM samples.");
            var result=Wrap(root.Target);
            if(result.SampleFrames!=audio.SampleFrames||result.Channels!=audio.Channels||result.Frequency!=audio.Frequency)throw new InvalidOperationException("AudioClip creation read-back differs from PCM geometry.");
            result.Hold();return result;
        }
        catch{_api.CallStatic(_object,"Destroy",A.Ref(root.Target));throw;}
    }
    public IOwnAudioSource Play(IAudioClipObject clip,float volume,bool loop)
    {
        _main.VerifyAccess();var cls=_api.Class("UnityEngine.CoreModule.dll","UnityEngine","GameObject");var go=_api.New(cls,_api.Str("RizToolbox.Audio"));var root=_api.Root(go);
        try
        {
            var source=_api.Call(root.Target,"AddComponent",A.Ref(_api.Type(_source)));using var sourceRoot=_api.Root(source);
            _api.Call(source,"set_playOnAwake",A.Value(false));_api.Call(source,"set_spatialBlend",A.Value(0f));
            _api.Call(source,"set_loop",A.Value(loop));_api.Call(source,"set_volume",A.Value(volume));_api.Call(source,"set_clip",A.Ref(Pointer(clip)));
            _api.Call(source,"Play");return new OwnSource(this,root,_api.Root(source));
        }
        catch{try{if(Alive(root.Target))_api.CallStatic(_object,"Destroy",A.Ref(root.Target));}finally{root.Dispose();}throw;}
    }
    public AudioClockSnapshot Clock()
    {
        _main.VerifyAccess();var settings=_api.Class("UnityEngine.AudioModule.dll","UnityEngine","AudioSettings");
        return new(_api.Unbox<double>(_api.CallStatic(settings,"get_dspTime")),Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency);
    }
    private sealed class NativeClip:IAudioClipObject
    {
        internal readonly NativeAudioBackend Backend;private readonly IntPtr _pointer;private NativeRuntime.RootHandle? _seed;private bool _transferred;
        internal NativeClip(NativeAudioBackend backend,IntPtr pointer){Backend=backend;_pointer=pointer;Identity=pointer==IntPtr.Zero?"null":$"{pointer.ToInt64():x}:{backend._api.Unbox<int>(backend._api.Call(pointer,"GetInstanceID"))}";}
        internal IntPtr Pointer=>_seed?.Target??_pointer;public string Identity{get;}
        public bool IsAlive=>_pointer==IntPtr.Zero||Backend.Alive(Pointer);
        public int SampleFrames=>_pointer==IntPtr.Zero?0:Backend._api.Unbox<int>(Backend._api.Call(Pointer,"get_samples"));
        public int Channels=>_pointer==IntPtr.Zero?0:Backend._api.Unbox<int>(Backend._api.Call(Pointer,"get_channels"));
        public int Frequency=>_pointer==IntPtr.Zero?0:Backend._api.Unbox<int>(Backend._api.Call(Pointer,"get_frequency"));
        internal void Hold()=>_seed=Backend._api.Root(_pointer);
        public IDisposable Retain()
        {
            if(_pointer==IntPtr.Zero)return new ActionLease(()=>{});
            _seed??=Backend._api.Root(_pointer);if(!_transferred){_transferred=true;return _seed;}return Backend._api.Root(_seed.Target);
        }
        public void DestroyOwned(){try{if(_pointer!=IntPtr.Zero&&Backend.Alive(Pointer))Backend._api.CallStatic(Backend._object,"Destroy",A.Ref(Pointer));}finally{if(!_transferred)_seed?.Dispose();}}
    }
    private sealed class SourceRoot
    {
        private readonly NativeRuntime _api;private readonly IntPtr _pointer;private NativeRuntime.RootHandle? _root;
        internal SourceRoot(NativeRuntime api,IntPtr pointer){_api=api;_pointer=pointer;}internal IntPtr Pointer=>_root?.Target??_pointer;
        internal IDisposable Retain()=>_root=_api.Root(_pointer);
    }
    private sealed class OwnSource:IOwnAudioSource
    {
        private readonly NativeAudioBackend _backend;private NativeRuntime.RootHandle? _go,_source;
        internal OwnSource(NativeAudioBackend backend,NativeRuntime.RootHandle go,NativeRuntime.RootHandle source){_backend=backend;_go=go;_source=source;}
        public bool IsPlaying=>_source is not null&&_backend.Alive(_source.Target)&&_backend._api.Unbox<bool>(_backend._api.Call(_source.Target,"get_isPlaying"));
        public void Stop(){if(_source is not null&&_backend.Alive(_source.Target))_backend._api.Call(_source.Target,"Stop");}
        public void Dispose()
        {
            if(_go is null)return;
            if(IsPlaying)throw new InvalidOperationException("Owned audio voice is still playing; resource roots are retained.");
            if(_source is not null&&_backend.Alive(_source.Target))_backend._api.Call(_source.Target,"set_clip",A.Ref(IntPtr.Zero));
            if(_backend.Alive(_go.Target))_backend._api.CallStatic(_backend._object,"Destroy",A.Ref(_go.Target));
            _source?.Dispose();_source=null;_go.Dispose();_go=null;
        }
    }
}
