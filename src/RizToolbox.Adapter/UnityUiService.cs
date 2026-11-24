using RizToolbox.Runtime;
using RizToolbox.Sdk;
using System.Runtime.InteropServices;
using A=RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter;

[StructLayout(LayoutKind.Sequential)] internal readonly record struct Vec2(float X,float Y);
[StructLayout(LayoutKind.Sequential)] internal readonly record struct Rgba(float R,float G,float B,float Alpha);

/// <summary>Small UGUI backend. Resource overrides are limited to explicitly registered panel icon slots.</summary>
internal sealed class UnityUiService:IUiService,IResourceService,IDisposable
{
    private readonly NativeRuntime _api;private readonly IMainThreadDispatcher _main;private readonly ILogger _log;
    private readonly Dictionary<string,Panel> _panels=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Slot> _slots=new(StringComparer.Ordinal);
    private readonly IntPtr _canvasObject;
    private readonly NativeRuntime.RootHandle _canvasRoot;
    private long _order;
    internal UnityUiService(NativeRuntime api,IMainThreadDispatcher main,ILogger log) {
        _api=api;_main=main;_log=log;_main.VerifyAccess();
        _canvasObject=GameObject("RizToolbox.Canvas");_canvasRoot=_api.Root(_canvasObject);
        try {
            var canvas=Component(_canvasObject,"UnityEngine.UIModule.dll","UnityEngine","Canvas");
            _api.Call(canvas,"set_renderMode",A.Value(0));_api.Call(canvas,"set_sortingOrder",A.Value(30000));
            _api.CallStatic(C("UnityEngine.CoreModule.dll","UnityEngine","Object"),"DontDestroyOnLoad",A.Ref(_canvasObject));
        }catch{try{Destroy(_canvasObject);}finally{_canvasRoot.Dispose();}throw;}
    }
    private IntPtr C(string image,string ns,string name)=>_api.Class(image,ns,name);
    private IntPtr GameObject(string name)=>_api.New(C("UnityEngine.CoreModule.dll","UnityEngine","GameObject"),_api.Str(name));
    private IntPtr Component(IntPtr go,string image,string ns,string name)=>_api.Call(go,"AddComponent",A.Ref(_api.Type(C(image,ns,name))));
    private IntPtr Transform(IntPtr go)=>_api.Call(go,"get_transform");
    private void Destroy(IntPtr obj){if(obj!=IntPtr.Zero)_api.CallStatic(C("UnityEngine.CoreModule.dll","UnityEngine","Object"),"Destroy",A.Ref(obj));}
    private void Place(IntPtr go,IntPtr parent,float x,float y,float width,float height) {
        var rect=Component(go,"UnityEngine.CoreModule.dll","UnityEngine","RectTransform");
        _api.Call(rect,"SetParent",A.Ref(Transform(parent)),A.Value(false));
        _api.Call(rect,"set_anchorMin",A.Value(new Vec2(0,1)));_api.Call(rect,"set_anchorMax",A.Value(new Vec2(0,1)));
        _api.Call(rect,"set_pivot",A.Value(new Vec2(0,1)));_api.Call(rect,"set_sizeDelta",A.Value(new Vec2(width,height)));
        _api.Call(rect,"set_anchoredPosition",A.Value(new Vec2(x,y)));
    }
    public OperationResult<IPanelHandle> CreatePanel(PanelDefinition definition) {
        if(!_main.IsMainThread)return OperationResult<IPanelHandle>.Fail(ErrorCode.WrongThread,"UI operations require the main thread.",CapabilityIds.Ui);
        if(string.IsNullOrWhiteSpace(definition.Id))return OperationResult<IPanelHandle>.Fail(ErrorCode.InvalidArgument,"Panel ID is required.",CapabilityIds.Ui);
        if(_panels.ContainsKey(definition.Id))return OperationResult<IPanelHandle>.Fail(ErrorCode.Conflict,"Panel ID is already registered.",CapabilityIds.Ui,definition.Id);
        IntPtr go=IntPtr.Zero;
        try {
            go=GameObject("RizToolbox.Panel."+definition.Id);
            Place(go,_canvasObject,20,-20-_panels.Count*200,460,180);
            var background=Component(go,"UnityEngine.UI.dll","UnityEngine.UI","Image");
            _api.Call(background,"set_color",A.Value(new Rgba(.04f,.06f,.08f,.92f)));
            var textGo=GameObject("RizToolbox.Text");Place(textGo,go,12,-10,430,152);
            var text=Component(textGo,"UnityEngine.UI.dll","UnityEngine.UI","Text");
            var fontType=_api.Type(C("UnityEngine.TextRenderingModule.dll","UnityEngine","Font"));
            var resources=C("UnityEngine.CoreModule.dll","UnityEngine","Resources");
            IntPtr font;
            try{font=_api.CallStatic(resources,"GetBuiltinResource",A.Ref(fontType),_api.Str("LegacyRuntime.ttf"));}
            catch{font=_api.CallStatic(resources,"GetBuiltinResource",A.Ref(fontType),_api.Str("Arial.ttf"));}
            _api.Call(text,"set_font",A.Ref(font));_api.Call(text,"set_fontSize",A.Value(18));
            _api.Call(text,"set_color",A.Value(new Rgba(1,1,1,1)));
            _api.Call(text,"set_text",_api.Str(definition.Title+"\n"+definition.Text));
            var iconGo=GameObject("RizToolbox.Icon");Place(iconGo,go,400,-116,44,44);
            var rawImage=Component(iconGo,"UnityEngine.UI.dll","UnityEngine.UI","RawImage");
            var original=Texture(2,2,Enumerable.Repeat(new Rgba(.2f,.7f,.9f,1),4).ToArray());
            var key=$"ui.panel/{definition.Id}/icon";
            _api.Call(rawImage,"set_texture",A.Ref(original));
            _slots.Add(key,new Slot(rawImage,original,_api.Root(original)));
            var panel=new Panel(this,definition.Id,definition.Title,go,text,key,_api.Root(go));_panels.Add(panel.Id,panel);
            _log.Log(LogLevel.Info,$"UGUI panel created: {definition.Id}; resource slot {key}.");
            return OperationResult<IPanelHandle>.Ok(panel);
        }catch(Exception ex){Destroy(go);return OperationResult<IPanelHandle>.Fail(Error(ex,CapabilityIds.Ui,definition.Id));}
    }
    private IntPtr Texture(int width,int height,IReadOnlyList<Rgba> colors) {
        var texture=_api.New(C("UnityEngine.CoreModule.dll","UnityEngine","Texture2D"),A.Value(width),A.Value(height));
        using var root=_api.Root(texture);
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)_api.Call(texture,"SetPixel",A.Value(x),A.Value(y),A.Value(colors[(height-y-1)*width+x]));
        _api.Call(texture,"Apply");return texture;
    }
    public OperationResult<IResourceOverride> OverrideTexture(TextureOverrideRequest request) {
        if(!_main.IsMainThread)return OperationResult<IResourceOverride>.Fail(ErrorCode.WrongThread,"Resource operations require the main thread.",CapabilityIds.Resources);
        if(!_slots.TryGetValue(request.ResourceKey,out var slot))return OperationResult<IResourceOverride>.Fail(ErrorCode.TargetMissing,"Only registered panel icon slots are supported by this adapter.",CapabilityIds.Resources,request.ResourceKey,true);
        TextureHandle? handle=null;
        try {
            var decoded=PngDecoder.Decode(request.EncodedImage);
            var texture=Texture(decoded.Width,decoded.Height,decoded.Pixels);
            handle=new TextureHandle(this,request.ResourceKey,texture,request.Priority,++_order,_api.Root(texture));
            slot.Overrides.Add(handle);Apply(slot);return OperationResult<IResourceOverride>.Ok(handle);
        }catch(Exception ex){
            if(handle is not null){try{handle.Dispose();}catch(Exception cleanup){_log.Log(LogLevel.Warning,"Failed texture override rollback.",cleanup);}}
            return OperationResult<IResourceOverride>.Fail(Error(ex,CapabilityIds.Resources,request.ResourceKey));
        }
    }
    private void Apply(Slot slot) {
        var top=slot.Overrides.OrderByDescending(x=>x.Priority).ThenByDescending(x=>x.Order).FirstOrDefault();
        var expected=top?.Texture??slot.Original;
        _api.Call(slot.Image,"set_texture",A.Ref(expected));
        if(_api.Call(slot.Image,"get_texture")!=expected)throw new InvalidOperationException("Texture slot read-back mismatch.");
        _log.Log(LogLevel.Info,$"Texture slot read-back verified: {(top is null?"original restored":"override active")}; active overrides={slot.Overrides.Count}.");
    }
    private void Release(TextureHandle handle) {
        _main.VerifyAccess();
        try{if(_slots.TryGetValue(handle.ResourceKey,out var slot)){slot.Overrides.Remove(handle);Apply(slot);}}
        finally{try{Destroy(handle.Texture);}finally{handle.Root.Dispose();}}
    }
    private void Release(Panel panel) {
        _main.VerifyAccess();
        try{if(_slots.TryGetValue(panel.ResourceKey,out var slot)) {
            foreach(var handle in slot.Overrides.ToArray()){try{handle.Dispose();}catch(Exception ex){_log.Log(LogLevel.Warning,"Failed to release icon override.",ex);}}
            try{Destroy(slot.Original);}finally{slot.Root.Dispose();_slots.Remove(panel.ResourceKey);}
        }}finally{_panels.Remove(panel.Id);try{Destroy(panel.Object);}finally{panel.Root.Dispose();}}
    }
    public void Dispose(){_main.VerifyAccess();foreach(var panel in _panels.Values.ToArray())panel.Dispose();Destroy(_canvasObject);_canvasRoot.Dispose();}
    private sealed record Slot(IntPtr Image,IntPtr Original,NativeRuntime.RootHandle Root) {internal readonly List<TextureHandle> Overrides=new();}
    private sealed class Panel:IPanelHandle {
        private UnityUiService? _owner;private readonly string _title;private readonly IntPtr _text;
        public string Id{get;}internal IntPtr Object{get;}internal string ResourceKey{get;}internal NativeRuntime.RootHandle Root{get;}
        internal Panel(UnityUiService owner,string id,string title,IntPtr obj,IntPtr text,string key,NativeRuntime.RootHandle root){_owner=owner;Id=id;_title=title;Object=obj;_text=text;ResourceKey=key;Root=root;}
        public OperationResult SetText(string text){if(_owner is null)return OperationResult.Fail(ErrorCode.ScopeExpired,"Panel was disposed.",CapabilityIds.Ui,Id);if(!_owner._main.IsMainThread)return OperationResult.Fail(ErrorCode.WrongThread,"UI operations require the main thread.",CapabilityIds.Ui,Id);try{_owner._api.Call(_text,"set_text",_owner._api.Str(_title+"\n"+text));return OperationResult.Ok();}catch(Exception ex){return OperationResult.Fail(Error(ex,CapabilityIds.Ui,Id));}}
        public void Dispose(){var owner=_owner;if(owner is null)return;owner._main.VerifyAccess();_owner=null;owner.Release(this);}
    }
    private sealed class TextureHandle:IResourceOverride {
        private UnityUiService? _owner;public string ResourceKey{get;}internal IntPtr Texture{get;}internal int Priority{get;}internal long Order{get;}internal NativeRuntime.RootHandle Root{get;}
        internal TextureHandle(UnityUiService owner,string key,IntPtr texture,int priority,long order,NativeRuntime.RootHandle root){_owner=owner;ResourceKey=key;Texture=texture;Priority=priority;Order=order;Root=root;}
        public void Dispose(){var owner=_owner;if(owner is null)return;owner._main.VerifyAccess();_owner=null;owner.Release(this);}
    }
    private static OperationError Error(Exception error,string capability,string? target)
        => error is AdapterOperationException failure ? failure.Error : new(error switch {
            UnsupportedSignatureException or MissingMemberException => ErrorCode.UnsupportedSignature,
            TargetExpiredException => ErrorCode.TargetExpired,
            InvalidDataException => ErrorCode.ContentInvalid,
            ArgumentException => ErrorCode.InvalidArgument,
            _ => ErrorCode.ResourceLoadFailed },error.Message,capability,target);
}
