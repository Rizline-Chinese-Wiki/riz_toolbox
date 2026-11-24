using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A=RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Visual;

/// <summary>Exact Unity metadata signatures only; instantiated objects and temporary upload buffers remain adapter-owned.</summary>
internal sealed class NativeVisualBackend:IVisualBackend
{
    private readonly NativeRuntime _api;private readonly IMainThreadDispatcher _main;private readonly VisualTargetKind _kind;
    private readonly IntPtr _object,_resources,_component,_scene;
    private readonly IntPtr _tmp,_translucent,_tmpRead,_tmpWrite;
    private readonly NativeArrayScanCursor _discoveryCursor=new();
    internal NativeVisualBackend(NativeRuntime api,IMainThreadDispatcher main,VisualTargetKind kind,ILogger? log=null)
    {
        _api=api;_main=main;_kind=kind;_object=api.Class("UnityEngine.CoreModule.dll","UnityEngine","Object");
        _resources=api.Class("UnityEngine.CoreModule.dll","UnityEngine","Resources");_scene=api.Class("UnityEngine.CoreModule.dll","UnityEngine.SceneManagement","Scene");
        _component=api.Class("UnityEngine.UI.dll","UnityEngine.UI",kind switch{VisualTargetKind.Sprite=>"Image",VisualTargetKind.Font=>"Text",_=>"Graphic"});
        if(kind==VisualTargetKind.Material)
        {
            _tmp=OptionalClass("Unity.TextMeshPro.dll","TMPro","TMP_Text");
            _translucent=OptionalClass("LeTai.TranslucentImage.dll","LeTai.Asset.TranslucentImage","TranslucentImage");
            if(_tmp!=IntPtr.Zero)
            {
                try
                {
                    var read=api.Method(_tmp,"get_fontSharedMaterial",0,false,Array.Empty<string>());
                    var write=api.Method(_tmp,"set_fontSharedMaterial",1,false,new[]{"UnityEngine.Material"});
                    if(api.DescribeReturnType(read)!="UnityEngine.Material"||api.DescribeReturnType(write)!="System.Void")
                        throw new UnsupportedSignatureException("TMP fontSharedMaterial accessor return types differ from the declared slot.");
                    _tmpRead=read;_tmpWrite=write;
                }
                catch(Exception error)when(error is MissingMemberException or UnsupportedSignatureException or System.Reflection.AmbiguousMatchException)
                {var detail=error.Message;log?.Log(LogLevel.Warning,"TMP material slot unavailable; matching TMP receivers are excluded: "+(detail.Length<=500?detail:detail[..500]));}
            }
        }
    }
    private IntPtr OptionalClass(string assembly,string ns,string name){try{return _api.Class(assembly,ns,name);}catch(MissingMemberException){return IntPtr.Zero;}}
    private bool Alive(IntPtr value)=>value!=IntPtr.Zero&&_api.Unbox<bool>(_api.CallStatic(_object,"op_Implicit",A.Ref(value)));
    private bool Loaded(IntPtr value)
    {
        if(!Alive(value))return false;var go=_api.Call(value,"get_gameObject");var scene=_api.Unbox<int>(_api.Call(go,"get_scene"));
        return _api.Unbox<bool>(_api.CallStatic(_scene,"IsValidInternal",A.Value(scene)))&&_api.Unbox<bool>(_api.CallStatic(_scene,"GetIsLoadedInternal",A.Value(scene)));
    }
    private bool Eligible(IntPtr value)=>Loaded(value)&&_api.Unbox<bool>(_api.Call(value,"get_isActiveAndEnabled"));
    private string Property=>_kind.ToString().ToLowerInvariant();
    public IEnumerable<VisualCandidate> Discover(int maximum)
    {
        _main.VerifyAccess();
        if(maximum<0)throw new ArgumentOutOfRangeException(nameof(maximum));
        if(maximum==0)yield break;
        var array=_api.CallStatic(_resources,"FindObjectsOfTypeAll",A.Ref(_api.Type(_component)));
        if(array==IntPtr.Zero)
        {
            foreach(var unused in _api.ArrayItems(array,_discoveryCursor,0)){ }
            yield break;
        }
        using var arrayRoot=_api.Root(array);var count=0;
        var materialCandidates=new List<(VisualCandidate Candidate,VisualMaterialSlot Slot,int ScanOrder)>();
        foreach(var pointer in _api.ArrayItems(arrayRoot.Target,_discoveryCursor,Math.Min(maximum,VisualResourceService.MaximumScanned)))
        {
            count++;if(!Eligible(pointer))continue;
            VisualMaterialSlot? slot=null;
            if(_kind==VisualTargetKind.Material)
            {
                var isTmp=_tmp!=IntPtr.Zero&&_api.ExactHookReceiverMatches(pointer,_tmp);
                slot=VisualMaterialSlots.Select(isTmp,_tmpRead!=IntPtr.Zero&&_tmpWrite!=IntPtr.Zero,
                    _translucent!=IntPtr.Zero&&_api.ExactHookReceiverMatches(pointer,_translucent));
                if(slot is null)continue;
                if(slot==VisualMaterialSlots.TmpFontShared)
                {
                    // A missing shared font material cannot be meaningfully overridden and must not become a default Graphic-material target.
                    var shared=_api.Invoke(_tmpRead,pointer);if(!Alive(shared))continue;
                }
            }
            var go=_api.Call(pointer,"get_gameObject");var transform=_api.Call(go,"get_transform");var rootName=_api.Text(_api.Call(_api.Call(transform,"get_root"),"get_name"));
            if(rootName.StartsWith("RizToolbox.",StringComparison.Ordinal))continue;
            var id=_api.Unbox<int>(_api.Call(pointer,"GetInstanceID"));var name=_api.Text(_api.Call(go,"get_name"));var sceneHandle=_api.Unbox<int>(_api.Call(go,"get_scene"));
            var sceneName=_api.Text(_api.CallStatic(_scene,"GetNameInternal",A.Value(sceneHandle)));var holder=new ComponentRoot(_api,pointer);
            var alias=$"game.visual/{Property}/{id}";var semanticAlias=slot is null?alias:alias+"/slot/"+slot.Id;
            var localSlot=slot;
            var candidate=new VisualCandidate($"{id}:{pointer.ToInt64():x}:{slot?.Id??Property}",semanticAlias,
                new(Scene:sceneName,Role:slot?.Role??Property,ResourceId:slot?.ResourceId??$"unity.ui.{Property}",Description:$"{rootName}/{name}: {Property}"+(slot is null?"":$" slot={slot.Id}; source={slot.ResourceId}")),
                new[]{alias,semanticAlias,$"game.visual/{Property}/name/{name}"},
                ()=>Wrap(localSlot==VisualMaterialSlots.TmpFontShared?_api.Invoke(_tmpRead,holder.Pointer):_api.Call(holder.Pointer,"get_"+Property),ExpectedKind),
                value=>{if(!Loaded(holder.Pointer))throw new TargetExpiredException("Visual target scene has unloaded.");
                    if(localSlot==VisualMaterialSlots.TmpFontShared)_api.Invoke(_tmpWrite,holder.Pointer,A.Ref(Pointer(value)));
                    else _api.Call(holder.Pointer,"set_"+Property,A.Ref(Pointer(value)));},
                ()=>Loaded(holder.Pointer),()=>Eligible(holder.Pointer),holder.Retain,()=>Alive(holder.Pointer),slot?.DiscoveryPriority??0);
            if(slot is null)yield return candidate;else materialCandidates.Add((candidate,slot,count));
        }
        // The native array remains rooted across every yield and keeps each copied candidate receiver alive until binding owns its root.
        foreach(var candidate in VisualMaterialSlots.Prioritize(materialCandidates))yield return candidate;
    }
    private VisualAssetKind ExpectedKind=>_kind switch{VisualTargetKind.Sprite=>VisualAssetKind.Sprite,VisualTargetKind.Font=>VisualAssetKind.Font,_=>VisualAssetKind.Material};
    private IntPtr Pointer(IVisualObject value)=>value is NativeObject native&&ReferenceEquals(native.Backend,this)?native.Pointer:throw new InvalidOperationException("AssetBackendMismatch");
    private NativeObject Wrap(IntPtr pointer,VisualAssetKind kind,int width=0,int height=0,long bytes=0)=>new(this,pointer,kind,width,height,bytes);
    public IVisualObject CreateTexture(PngDecoder.Image image)
    {
        _main.VerifyAccess();var cls=_api.Class("UnityEngine.CoreModule.dll","UnityEngine","Texture2D");var pointer=_api.New(cls,A.Value(image.Width),A.Value(image.Height));
        using var root=_api.Root(pointer);var texture=Wrap(pointer,VisualAssetKind.Texture2D,image.Width,image.Height,(long)image.Width*image.Height*4);
        try
        {
            var ok=_api.Unbox<bool>(_api.CallTyped(root.Target,"Reinitialize",new[]{"System.Int32","System.Int32","UnityEngine.TextureFormat","System.Boolean"},
                A.Value(image.Width),A.Value(image.Height),A.Value(UnityEngine.TextureFormat.RGBA32),A.Value(false)));
            if(!ok||_api.Unbox<UnityEngine.TextureFormat>(_api.Call(root.Target,"get_format"))!=UnityEngine.TextureFormat.RGBA32)throw new InvalidOperationException("TextureFormatUnavailable: RGBA32 reinitialization was refused.");
            var bytes=new byte[checked(image.Width*image.Height*4)];
            // PNG rows are top-to-bottom; Unity raw texture rows start at the bottom.
            for(var y=0;y<image.Height;y++)for(var x=0;x<image.Width;x++)
            {var c=image.Pixels[y*image.Width+x];var p=((image.Height-1-y)*image.Width+x)*4;bytes[p]=(byte)Math.Round(c.R*255);bytes[p+1]=(byte)Math.Round(c.G*255);bytes[p+2]=(byte)Math.Round(c.B*255);bytes[p+3]=(byte)Math.Round(c.Alpha*255);}
            var buffer=Marshal.AllocHGlobal(bytes.Length);
            try{Marshal.Copy(bytes,0,buffer,bytes.Length);_api.CallTyped(root.Target,"LoadRawTextureData",new[]{"System.IntPtr","System.Int32"},A.Value(buffer),A.Value(bytes.Length));}
            finally{Marshal.FreeHGlobal(buffer);}
            _api.Call(root.Target,"Apply",A.Value(false),A.Value(true));
            if(_api.Unbox<int>(_api.Call(root.Target,"get_width"))!=image.Width||_api.Unbox<int>(_api.Call(root.Target,"get_height"))!=image.Height)throw new InvalidOperationException("TextureReadBackMismatch");
            texture.HoldUntilRetained();return texture;
        }
        catch{texture.DestroyOwned();throw;}
    }
    public IVisualObject CreateSprite(IVisualObject texture,float pixelsPerUnit)
    {
        _main.VerifyAccess();if(texture.Kind!=VisualAssetKind.Texture2D)throw new InvalidDataException("A Texture2D is required.");
        var cls=_api.Class("UnityEngine.CoreModule.dll","UnityEngine","Sprite");
        // dump.cs 113508: Rect, Vector2, Single, Texture2D. The other four-argument overload is not selected by arity.
        var method=_api.Method(cls,"Create",4,true,new[]{"UnityEngine.Rect","UnityEngine.Vector2","System.Single","UnityEngine.Texture2D"});
        var pointer=_api.Invoke(method,IntPtr.Zero,A.Value(new NativeRect(0,0,texture.Width,texture.Height)),A.Value(new Vec2(.5f,.5f)),A.Value(pixelsPerUnit),A.Ref(Pointer(texture)));
        if(!Alive(pointer))throw new InvalidOperationException("SpriteCreationFailed");var sprite=Wrap(pointer,VisualAssetKind.Sprite,texture.Width,texture.Height,1024);sprite.HoldUntilRetained();return sprite;
    }
    public IVisualObject LoadFont(string name)
    {
        _main.VerifyAccess();if(name is not("Arial.ttf" or "LegacyRuntime.ttf"))throw new NotSupportedException("UnsupportedFont");
        var cls=_api.Class("UnityEngine.TextRenderingModule.dll","UnityEngine","Font");var pointer=_api.CallStatic(_resources,"GetBuiltinResource",A.Ref(_api.Type(cls)),_api.Str(name));
        if(!Alive(pointer))throw new InvalidOperationException("BuiltinFontUnavailable: "+name);return Wrap(pointer,VisualAssetKind.Font);
    }
    public IVisualObject CreateMaterial(IVisualObject source,VisualMaterialRequest request)
    {
        _main.VerifyAccess();if(source.Kind!=VisualAssetKind.Material)throw new InvalidDataException("AssetKindMismatch: a material source is required.");var pointer=Pointer(source);if(!Alive(pointer))throw new InvalidOperationException("MaterialSourceMissing");
        ArgumentNullException.ThrowIfNull(request);
        // Only the bounded snapshot is validated and later written; callers may own mutable IReadOnlyList implementations.
        request=request with{Colors=request.Colors?.Take(17).ToArray(),Floats=request.Floats?.Take(17).ToArray()};
        using var sourceRoot=_api.Root(pointer);pointer=sourceRoot.Target;
        var profiles=VisualMaterialProfiles.Candidates(request);
        var sourceShader=profiles.Count>0?_api.Call(sourceRoot.Target,"get_shader"):IntPtr.Zero;
        if(profiles.Count>0&&!Alive(sourceShader))throw new NotSupportedException("UnsupportedShaderSchema: source shader is absent.");
        using var verifiedSourceShader=profiles.Count>0?_api.Root(sourceShader):null;
        if(profiles.Count>0)
        {
            var accepted=false;
            foreach(var profile in profiles)
            {
                var gameRoot=Path.GetDirectoryName(Environment.ProcessPath)??throw new NotSupportedException("UnsupportedShaderSchema: executable root is absent.");
                using var evidence=VisualMaterialProfiles.VerifyFiles(profile,gameRoot);
                var type=_api.Class("UnityEngine.CoreModule.dll","UnityEngine",profile.ReferenceKind==VisualMaterialReferenceKind.Material?"Material":"Shader");
                var load=_api.Method(_resources,"Load",2,true,new[]{"System.String","System.Type"});
                var reference=_api.Invoke(load,IntPtr.Zero,_api.Str(profile.ReferenceResource),A.Ref(_api.Type(type)));
                if(!Alive(reference))throw new NotSupportedException("UnsupportedShaderSchema: exact typed reference resource is unavailable.");
                using var referenceRoot=_api.Root(reference);
                var trustedShader=profile.ReferenceKind==VisualMaterialReferenceKind.Material?_api.Call(referenceRoot.Target,"get_shader"):referenceRoot.Target;
                if(!Alive(trustedShader))throw new NotSupportedException("UnsupportedShaderSchema: exact reference shader is unavailable.");
                using var trustedShaderRoot=_api.Root(trustedShader);
                var equals=_api.Method(_object,"op_Equality",2,true,new[]{"UnityEngine.Object","UnityEngine.Object"});
                if(_api.Unbox<bool>(_api.Invoke(equals,IntPtr.Zero,A.Ref(verifiedSourceShader!.Target),A.Ref(trustedShaderRoot.Target)))){accepted=true;break;}
            }
            if(!accepted)throw new NotSupportedException("UnsupportedShaderSchema: source shader is not the exact trusted resource shader.");
        }
        foreach(var color in request.Colors??Array.Empty<MaterialColorProperty>())
            if(!_api.Unbox<bool>(_api.CallTyped(sourceRoot.Target,"HasProperty",new[]{"System.String"},_api.Str(color.Name))))throw new NotSupportedException("UnsupportedProperty: material does not contain "+color.Name);
        foreach(var value in request.Floats??Array.Empty<MaterialFloatProperty>())
            if(!_api.Unbox<bool>(_api.CallTyped(sourceRoot.Target,"HasProperty",new[]{"System.String"},_api.Str(value.Name))))throw new NotSupportedException("UnsupportedProperty: material does not contain "+value.Name);
        var instantiate=_api.Method(_object,"Instantiate",1,true,new[]{"UnityEngine.Object"});var clone=_api.Invoke(instantiate,IntPtr.Zero,A.Ref(sourceRoot.Target));
        if(!Alive(clone))throw new InvalidOperationException("MaterialCloneFailed");NativeRuntime.RootHandle? root=null;NativeObject? result=null;
        try
        {
            root=_api.Root(clone);result=Wrap(root.Target,VisualAssetKind.Material,bytes:64*1024);
            if(verifiedSourceShader is not null)
            {
                var cloneShader=_api.Call(root.Target,"get_shader");using var cloneShaderRoot=_api.Root(cloneShader);
                var equals=_api.Method(_object,"op_Equality",2,true,new[]{"UnityEngine.Object","UnityEngine.Object"});
                if(!_api.Unbox<bool>(_api.Invoke(equals,IntPtr.Zero,A.Ref(verifiedSourceShader.Target),A.Ref(cloneShaderRoot.Target))))throw new InvalidOperationException("MaterialShaderReadBackMismatch");
            }
            foreach(var color in request.Colors??Array.Empty<MaterialColorProperty>())
            {
                var v=color.Value;_api.CallTyped(root.Target,"SetColor",new[]{"System.String","UnityEngine.Color"},_api.Str(color.Name),A.Value(new Rgba(v.R,v.G,v.B,v.A)));
                var read=_api.Unbox<Rgba>(_api.CallTyped(root.Target,"GetColor",new[]{"System.String"},_api.Str(color.Name)));
                if(new[]{read.R,read.G,read.B,read.Alpha}.Any(x=>!float.IsFinite(x))||Math.Abs(read.R-v.R)>.0001f||Math.Abs(read.G-v.G)>.0001f||Math.Abs(read.B-v.B)>.0001f||Math.Abs(read.Alpha-v.A)>.0001f)throw new InvalidOperationException("MaterialPropertyReadBackMismatch: "+color.Name);
            }
            foreach(var value in request.Floats??Array.Empty<MaterialFloatProperty>())
            {
                _api.CallTyped(root.Target,"SetFloat",new[]{"System.String","System.Single"},_api.Str(value.Name),A.Value(value.Value));
                var read=_api.Unbox<float>(_api.CallTyped(root.Target,"GetFloat",new[]{"System.String"},_api.Str(value.Name)));
                if(!float.IsFinite(read)||Math.Abs((double)read-value.Value)>.0001)throw new InvalidOperationException("MaterialPropertyReadBackMismatch: "+value.Name);
            }
            result.HoldUntilRetained();return result;
        }
        catch
        {
            if(result is not null)result.DestroyOwned();
            else{var orphan=root?.Target??clone;if(Alive(orphan))_api.CallStatic(_object,"Destroy",A.Ref(orphan));}
            throw;
        }
        finally{root?.Dispose();}
    }
    private sealed class NativeObject:IVisualObject
    {
        internal NativeVisualBackend Backend{get;}private readonly IntPtr _pointer;private NativeRuntime.RootHandle? _root;private bool _transferred;
        internal IntPtr Pointer=>_root?.Target??_pointer;public string Identity{get;}public VisualAssetKind Kind{get;}public int Width{get;}public int Height{get;}public long EstimatedBytes{get;}
        internal NativeObject(NativeVisualBackend backend,IntPtr pointer,VisualAssetKind kind,int width,int height,long bytes)
        {Backend=backend;_pointer=pointer;Kind=kind;Width=width;Height=height;EstimatedBytes=bytes;Identity=$"{kind}:{pointer.ToInt64():x}:"+(pointer==IntPtr.Zero?"null":backend._api.Unbox<int>(backend._api.Call(pointer,"GetInstanceID")).ToString());}
        public bool IsAlive=>_pointer==IntPtr.Zero||Backend.Alive(Pointer);
        internal void HoldUntilRetained(){if(_root is null&&_pointer!=IntPtr.Zero)_root=Backend._api.Root(_pointer);}
        public IDisposable Retain(){if(_transferred)throw new InvalidOperationException("Visual asset already retained.");_transferred=true;HoldUntilRetained();return _root is null?new Empty():_root;}
        public void DestroyOwned(){try{if(_pointer!=IntPtr.Zero&&Backend.Alive(Pointer))Backend._api.CallStatic(Backend._object,"Destroy",A.Ref(Pointer));}finally{if(!_transferred)_root?.Dispose();}}
    }
    private sealed class ComponentRoot
    {
        private readonly NativeRuntime _api;private readonly IntPtr _pointer;private NativeRuntime.RootHandle? _root;
        internal ComponentRoot(NativeRuntime api,IntPtr pointer){_api=api;_pointer=pointer;}internal IntPtr Pointer=>_root?.Target??_pointer;
        internal IDisposable Retain()=>_root=_api.Root(_pointer);
    }
    private sealed class Empty:IDisposable{public void Dispose(){}}
}
