using RizToolbox.Adapter;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static TestColors;

var tests=new (string Name,Action Run)[]{
    ("Color metadata requires exact parameter, declaring-class and return signatures",()=>{
        var calls=new List<string>();
        var bound=NativeGameColorAccessors.Bind(new IntPtr(10),(name,parameters)=>{calls.Add(name+"("+string.Join(",",parameters)+")");return new(name=="get_color"?11:12);},
            (method,name)=>(method==new IntPtr(11)&&name=="get_color")||(method==new IntPtr(12)&&name=="set_color"),method=>method==new IntPtr(11)?"UnityEngine.Color":"System.Void");
        Equal(new IntPtr(10),bound.ReceiverType);Equal(new IntPtr(11),bound.Read);Equal(new IntPtr(12),bound.Write);
        Check(calls.SequenceEqual(new[]{"get_color()","set_color(UnityEngine.Color)"}));
    }),
    ("TMP color cannot fall back to inherited Graphic accessors or changed returns",()=>{
        foreach(var inherited in new[]{"get_color","set_color"})
            Throws<NotSupportedException>(()=>NativeGameColorAccessors.Bind(new IntPtr(10),(name,parameters)=>new(name=="get_color"?11:12),
                (method,name)=>name!=inherited,method=>method==new IntPtr(11)?"UnityEngine.Color":"System.Void"));
        foreach(var changed in new[]{11,12})
            Throws<NotSupportedException>(()=>NativeGameColorAccessors.Bind(new IntPtr(10),(name,parameters)=>new(name=="get_color"?11:12),
                (method,name)=>true,method=>method==new IntPtr(changed)?"System.Single":method==new IntPtr(11)?"UnityEngine.Color":"System.Void"));
    }),
    ("TMP and Image expose precise resource kinds in the same target registry",()=>{
        using var f=new Fixture();var image=f.Add();var text=f.AddTmp();f.Service.Refresh(true);
        var targets=Need(f.Service.GetTargets());Equal("unity.image.color",targets.Single(x=>x.Key==image.GenericAlias).Kind);
        Equal("unity.tmp-text.color",targets.Single(x=>x.Key==text.GenericAlias).Kind);
        var match=f.Service.Targets.Resolve<ResourceColor>(new(Role:"tmp-text-color",ResourceId:"unity.tmp-text.color"));
        Equal(TargetMatchKind.Unique,match.Kind);Equal(f.Resolve(text.GenericAlias),match.Target);
        Equal(2,f.Service.Targets.GetTargets().Count);
    }),
    ("TMP two-plugin holders restore their own adopted baseline independently of Image",()=>{
        using var f=new Fixture();var image=f.Add();var text=f.AddTmp();f.Service.Refresh(true);
        using var imageOverride=Need(f.Service.OverrideColor(new(image.GenericAlias,Green)));
        var low=f.Plugin("test.tmp.low",10,Red,legacy:true,resourceKey:text.GenericAlias);
        var high=f.Plugin("test.tmp.high",20,Blue,resourceKey:text.GenericAlias);
        var key=new MutationKey<ResourceColor>(f.Resolve(text.GenericAlias),"color");
        var textBaseline=new ResourceColor(.6f,.2f,.7f);var imageBaseline=new ResourceColor(.8f,.7f,.1f);
        text.Color=textBaseline;image.Color=imageBaseline;f.Service.Refresh();Equal(Blue,text.Color);Equal(Green,image.Color);
        var snapshot=Need(high.Mutations!.Describe(key));Equal(2,snapshot.Holders.Count);Equal(textBaseline,snapshot.Baseline);Equal(Blue,snapshot.Effective);
        f.Host.Disable("test.tmp.high");Equal(Red,text.Color);Equal(Green,image.Color);
        f.Host.Disable("test.tmp.low");Equal(textBaseline,text.Color);Equal(Green,image.Color);
        imageOverride.Dispose();Equal(imageBaseline,image.Color);f.Service.Refresh();Equal(textBaseline,text.Color);
    }),
    ("Repeated TMP aliases and discovery paths keep exactly one color generation",()=>{
        using var f=new Fixture();var text=f.AddTmp();text.Aliases.Add("game.test/tmp/semantic-color");f.Images.Add(text);f.Service.Refresh(true);
        var first=f.Resolve(text.GenericAlias);Equal(first,f.Resolve("game.test/tmp/semantic-color"));Equal(1,text.Retained);Equal(1,f.Service.Targets.GetTargets().Count);
        using var low=Need(f.Service.OverrideColor(new(text.GenericAlias,Red,10)));
        using(var high=Need(f.Service.OverrideColor(new("game.test/tmp/semantic-color",Blue,20)))){Equal(Blue,text.Color);f.Service.Refresh(true);Equal(first,f.Resolve(text.GenericAlias));}
        Equal(Red,text.Color);low.Dispose();Equal(text.Original,text.Color);
    }),
    ("TMP read-back failure preserves the observed value and leaves Image ownership intact",()=>{
        using var f=new Fixture();var image=f.Add();var text=f.AddTmp();f.Service.Refresh(true);
        using var imageOverride=Need(f.Service.OverrideColor(new(image.GenericAlias,Red)));
        text.OnWrite=value=>text.Color=Green;
        var failed=f.Service.OverrideColor(new(text.GenericAlias,Blue));Check(!failed.Success);Equal(Green,text.Color);Equal(Red,image.Color);
        f.Service.Refresh(true);Equal(Red,image.Color);imageOverride.Dispose();Equal(image.Original,image.Color);
    }),
    ("TMP destruction invalidates old generations and inactive release restores its baseline",()=>{
        using var f=new Fixture();var text=f.AddTmp();f.Service.Refresh(true);var old=f.Resolve(text.GenericAlias);
        using var lease=Need(f.Service.Targets.Acquire(old));using var first=Need(f.Service.OverrideColor(new(text.GenericAlias,Red)));
        var writes=text.Writes;text.Alive=false;f.Service.Refresh(true);Check(!lease.IsValid);Equal(writes,text.Writes);Equal(1,text.Released);
        var next=new Image(text.Identity){Slot=NativeGameColorSlot.TmpText};f.Images.Clear();f.Images.Add(next);f.Service.Refresh(true);var current=f.Resolve(next.GenericAlias);
        Check(current.Key!=old.Key&&current.Generation>old.Generation);Check(!f.Service.Targets.Acquire(old).Success);
        using var second=Need(f.Service.OverrideColor(new(next.GenericAlias,Blue)));var baseline=new ResourceColor(.4f,.6f,.8f);next.Color=baseline;f.Service.Refresh();Equal(Blue,next.Color);
        next.Active=false;f.Service.Refresh(true);Equal(baseline,next.Color);Equal(1,next.Released);Equal(0,f.Service.Targets.GetTargets().Count);
    }),
    ("Native-template, unloaded-scene and inactive candidates never become targets",()=>{
        using var f=new Fixture();f.Images.AddRange(new[]{new Image("template"){Template=true},new Image("inactive"){Active=false},new Image("unloaded"){Loaded=false},new Image("invalid-scene"){SceneValid=false},new Image("live")});
        Equal(1,f.Service.Refresh(true));Equal(1,f.Service.Targets.GetTargets().Count);Equal(1,f.Images.Sum(x=>x.Retained));
    }),
    ("Late menu aliases resolve to the same canonical object generation",()=>{
        using var f=new Fixture();var image=f.Add();f.Service.Refresh(true);var first=f.Resolve(image.GenericAlias);
        image.Aliases.Add("game.menu/button/0/color");f.Service.Refresh(true);
        Equal(first,f.Resolve("game.menu/button/0/color"));Equal(1,image.Retained);Equal(1,f.Service.Targets.GetTargets().Count);
    }),
    ("Legacy and new mutation requests share one three-owner color channel",()=>{
        using var f=new Fixture();var image=f.Add();image.Aliases.Add("game.menu/button/0/color");f.Service.Refresh(true);
        var key=new MutationKey<ResourceColor>(f.Resolve(image.GenericAlias),"color");
        var low=f.Plugin("test.low",10,Red,legacy:true);var middle=f.Plugin("test.middle",20,Green);var high=f.Plugin("test.high",30,Blue);
        Equal(Blue,image.Color);var status=Need(high.Mutations!.Describe(key));Equal(3,status.Holders.Count);
        Check(status.Holders.Select(x=>x.Owner).ToHashSet().SetEquals(new[]{"test.low","test.middle","test.high"}));
        f.Host.Disable("test.high");Equal(Green,image.Color);f.Host.Disable("test.middle");Equal(Red,image.Color);f.Host.Disable("test.low");Equal(image.Original,image.Color);
    }),
    ("A game write becomes the restoration baseline through the production adapter",()=>{
        using var f=new Fixture();var image=f.Add();f.Service.Refresh(true);f.Plugin("test.color",10,Red,legacy:true);
        image.Color=Green;f.Service.Refresh();Equal(Red,image.Color);f.Host.Disable("test.color");Equal(Green,image.Color);
    }),
    ("Idle channels do not overwrite later game changes",()=>{
        using var f=new Fixture();var image=f.Add();f.Service.Refresh(true);f.Plugin("test.color",10,Red);f.Host.Disable("test.color");
        image.Color=Green;var writes=image.Writes;f.Service.Refresh();Equal(Green,image.Color);Equal(writes,image.Writes);
    }),
    ("Destroyed targets invalidate holders before releasing exactly one root",()=>{
        using var f=new Fixture();var image=f.Add();f.Service.Refresh(true);var target=f.Resolve(image.GenericAlias);var lease=Need(f.Service.Targets.Acquire(target));f.Plugin("test.color",10,Red);
        var writes=image.Writes;image.Alive=false;f.Service.Refresh(true);
        Check(!lease.IsValid);Equal(1,image.Released);Equal(writes,image.Writes);Equal(TargetMatchKind.Missing,f.Service.Targets.Resolve<ResourceColor>(new(Key:image.GenericAlias)).Kind);
        lease.Dispose();f.Host.Disable("test.color");Equal(1,image.Released);
    }),
    ("Inactive live objects restore owned color then leave the registry",()=>{
        using var f=new Fixture();var image=f.Add();f.Service.Refresh(true);f.Plugin("test.color",10,Red);
        image.Active=false;f.Service.Refresh(true);Equal(image.Original,image.Color);Equal(1,image.Released);Equal(0,f.Service.Targets.GetTargets().Count);
        image.Active=true;f.Service.Refresh(true);Equal(2,image.Retained);Equal(1,f.Service.Targets.GetTargets().Count);
    }),
    ("Rebuilt objects never accept handles from the destroyed binding",()=>{
        using var f=new Fixture();var first=f.Add();f.Service.Refresh(true);var old=f.Resolve(first.GenericAlias);
        first.Alive=false;f.Service.Refresh(true);var next=new Image(first.Identity);f.Images.Clear();f.Images.Add(next);f.Service.Refresh(true);
        var current=f.Resolve(next.GenericAlias);Check(current.Key!=old.Key&&current.Generation>old.Generation);Check(!f.Service.Targets.Acquire(old).Success);
    }),
    ("Opaque canonical keys also prevent token revival across module instances",()=>{
        var main=new MainThreadDispatcher();var log=new Logger();var backend=new Backend();var image=new Image("same");backend.Images.Add(image);
        TargetHandle<ResourceColor> old;
        using(var first=new GameResourceService(main,log,backend)){first.Refresh(true);old=first.Targets.Resolve<ResourceColor>(new(Key:image.GenericAlias)).Target!;}
        using var next=new GameResourceService(main,log,backend);next.Refresh(true);var current=next.Targets.Resolve<ResourceColor>(new(Key:image.GenericAlias)).Target!;
        Check(old.Key!=current.Key);Check(!next.Targets.Acquire(old).Success);
    }),
    ("Ambiguous menu aliases fail explicitly without changing either object",()=>{
        using var f=new Fixture();var first=f.Add("first");var second=f.Add("second");first.Aliases.Add("game.menu/button/0/color");second.Aliases.Add("game.menu/button/0/color");f.Service.Refresh(true);
        var match=f.Service.Targets.Resolve<ResourceColor>(new(Key:"game.menu/button/0/color"));Equal(TargetMatchKind.Ambiguous,match.Kind);
        var failed=f.Service.OverrideColor(new("game.menu/button/0/color",Red));Check(!failed.Success&&failed.Error!.Code==ErrorCode.AmbiguousTarget);Equal(first.Original,first.Color);Equal(second.Original,second.Color);
        var missing=f.Service.OverrideColor(new("missing",Red));Check(!missing.Success&&missing.Error!.Code==ErrorCode.TargetMissing);
    }),
    ("Game resource wrong-thread failures carry machine context and never change a native slot",()=>{
        using var f=new Fixture();var image=f.Add();f.Service.Refresh(true);var retained=image.Retained;
        var failure=Task.Run(()=>f.Service.OverrideColor(new(image.GenericAlias,Red))).GetAwaiter().GetResult();
        var readFailure=Task.Run(()=>f.Service.GetTargets()).GetAwaiter().GetResult();
        Check(!failure.Success && failure.Error is {Code:ErrorCode.WrongThread,CapabilityId:GameCapabilityIds.Resources,Retryable:false});
        Equal(image.GenericAlias,failure.Error!.TargetId);Equal(ErrorCode.WrongThread,readFailure.Error!.Code);
        Equal(0,image.Writes);Equal(retained,image.Retained);Equal(image.Original,image.Color);
    }),
    ("Disposed game resource operations return ScopeExpired without recreating or writing a target",()=>{
        using var f=new Fixture();var image=f.Add();f.Service.Refresh(true);f.Service.Dispose();var retained=image.Retained;var writes=image.Writes;
        var failure=f.Service.OverrideColor(new(image.GenericAlias,Red));var readFailure=f.Service.GetTargets();
        Check(!failure.Success && failure.Error is {Code:ErrorCode.ScopeExpired,CapabilityId:GameCapabilityIds.Resources});
        Equal(image.GenericAlias,failure.Error!.TargetId);Equal(ErrorCode.ScopeExpired,readFailure.Error!.Code);
        Equal(writes,image.Writes);Equal(retained,image.Retained);Equal(image.Original,image.Color);
    }),
    ("Alias removal does not recreate the object or split active holders",()=>{
        using var f=new Fixture();var image=f.Add();image.Aliases.Add("game.menu/button/0/color");f.Service.Refresh(true);
        var canonical=f.Resolve(image.GenericAlias);var lease=Need(f.Service.OverrideColor(new("game.menu/button/0/color",Red)));
        image.Aliases.Remove("game.menu/button/0/color");f.Service.Refresh(true);Equal(canonical,f.Resolve(image.GenericAlias));Equal(Red,image.Color);
        Equal(TargetMatchKind.Missing,f.Service.Targets.Resolve<ResourceColor>(new(Key:"game.menu/button/0/color")).Kind);lease.Dispose();Equal(image.Original,image.Color);
    }),
    ("Invalid color and unsupported mutation strategies preserve the winner",()=>{
        using var f=new Fixture();var image=f.Add();f.Service.Refresh(true);var plugin=f.Plugin("test.color",10,Red);var key=new MutationKey<ResourceColor>(f.Resolve(image.GenericAlias),"color");
        var invalid=f.Service.OverrideColor(new(image.GenericAlias,new(float.NaN,0,0)));Check(!invalid.Success);
        Check(!plugin.Mutations!.Transform(key,v=>Green).Success&&!plugin.Mutations.AcquireExclusive(key,Green).Success);Equal(Red,image.Color);
    }),
    ("Read-back mismatch never claims successful ownership",()=>{
        using var f=new Fixture();var image=f.Add();image.OnWrite=value=>image.Color=Green;f.Service.Refresh(true);
        var lease=f.Service.OverrideColor(new(image.GenericAlias,Red));Check(!lease.Success);Equal(Green,image.Color);
        f.Service.Dispose();Equal(Green,image.Color);Equal(1,image.Released);
    }),
    ("A setter that writes then throws rolls back only its own candidate",()=>{
        using var f=new Fixture();var image=f.Add();f.Service.Refresh(true);
        image.OnWrite=value=>{image.Color=value;if(value==Red)throw new InvalidOperationException("setter failed after write");};
        var lease=f.Service.OverrideColor(new(image.GenericAlias,Red));Check(!lease.Success);Equal(image.Original,image.Color);
    }),
    ("Resource cache has a fixed maximum and releases all roots",()=>{
        using var f=new Fixture();for(var i=0;i<GameResourceService.MaximumTargets+10;i++)f.Add(i.ToString());f.Service.Refresh(true);
        Equal(GameResourceService.MaximumTargets,f.Service.Targets.GetTargets().Count);Equal(GameResourceService.MaximumTargets,f.Images.Sum(x=>x.Retained));
        f.Service.Dispose();Equal(GameResourceService.MaximumTargets,f.Images.Sum(x=>x.Released));
    }),
    ("Scoped service factories forbid cached mutation after plugin stop",()=>{
        using var f=new Fixture();var image=f.Add();f.Service.Refresh(true);var plugin=f.Plugin("test.color",10,Red);var targets=plugin.Targets!;var mutations=plugin.Mutations!;var key=new MutationKey<ResourceColor>(f.Resolve(image.GenericAlias),"color");
        f.Host.Disable("test.color");Throws<ObjectDisposedException>(()=>targets.GetTargets());
        var expired=mutations.Override(key,Green);Check(!expired.Success && expired.Error?.Code==ErrorCode.ScopeExpired);
        Equal(image.Original,image.Color);
    }),
    ("Main-thread checks reject resource discovery and cached access",()=>{
        using var f=new Fixture();f.Add();f.Service.Refresh(true);Task.Run(()=>Throws<InvalidOperationException>(()=>f.Service.Refresh(true))).GetAwaiter().GetResult();
    }),
    ("Disposing adapter after a fresh game write preserves the latest baseline",()=>{
        using var f=new Fixture();var image=f.Add();f.Service.Refresh(true);f.Plugin("test.color",10,Red);image.Color=Blue;
        f.Service.Dispose();Equal(Blue,image.Color);Equal(1,image.Released);f.Service.Dispose();Equal(1,image.Released);
    })
}.Concat(SharedGameTargetChecks.Cases).ToArray();
var failed=0;foreach(var test in tests)try{test.Run();Console.WriteLine("PASS "+test.Name);}catch(Exception ex){failed++;Console.Error.WriteLine("FAIL "+test.Name+": "+ex);}
Console.WriteLine($"Game color integration checks: {tests.Length-failed}/{tests.Length} passed.");return failed==0?0:1;

static void Check(bool condition){if(!condition)throw new InvalidOperationException("Assertion failed.");}
static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new InvalidOperationException($"Expected {expected}, got {actual}.");}
static T Need<T>(OperationResult<T> result)=>result.Success?result.Value!:throw new InvalidOperationException(result.Error?.Message);
static void Throws<T>(Action run)where T:Exception{try{run();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}

sealed class Logger:ILogger{public void Log(LogLevel level,string message,Exception? exception=null){}}
static class TestColors
{
    internal static ResourceColor Red=>new(1,0,0);
    internal static ResourceColor Green=>new(0,1,0);
    internal static ResourceColor Blue=>new(0,0,1);
}
sealed class Cleanup:IDisposable{private Action? _run;internal Cleanup(Action run)=>_run=run;public void Dispose()=>Interlocked.Exchange(ref _run,null)?.Invoke();}
sealed class Image
{
    internal string Identity;internal ResourceColor Original=new(.2f,.3f,.4f);internal ResourceColor Color;
    internal bool Active=true,Template,Loaded=true,SceneValid=true,Alive=true;internal string Scene="Menu";
    internal List<string> Aliases=new();internal int Retained,Released,Writes;internal Action<ResourceColor>? OnWrite;
    internal NativeGameColorSlot Slot=NativeGameColorSlot.Image;
    internal string GenericAlias=>$"game.ui/{Slot.AliasKind}/{Identity}/color";
    internal Image(string id){Identity=id;Color=Original;}
    internal GameColorCandidate Candidate()=>new(Identity,$"game.ui/{Slot.AliasKind}/{Identity}",new(Scene:Scene,Role:Slot.Role,ResourceId:Slot.ResourceId,Active:Active,Template:Template),
        Aliases.Append(GenericAlias).ToArray(),()=>Color,value=>{Writes++;if(OnWrite is not null)OnWrite(value);else Color=value;},
        ()=>Alive&&Loaded&&SceneValid,()=>Active&&Alive&&Loaded&&SceneValid,
        ()=>{Retained++;return new Cleanup(()=>Released++);});
}
sealed class Backend:IGameColorBackend{internal List<Image> Images=new();public IEnumerable<GameColorCandidate> Discover(int maximumScanned)=>Images.Take(maximumScanned).Select(x=>x.Candidate()).ToArray();}
sealed class ColorPlugin:IRizPlugin
{
    private readonly string _id;private readonly int _priority;private readonly ResourceColor _color;private readonly bool _legacy;private readonly string? _resourceKey;
    internal ITargetService? Targets;internal IMutationService? Mutations;
    public PluginManifest Manifest=>new(_id,new(1,0),new(1,0),RequiredCapabilities:new[]{TargetCapabilityIds.Targets,TargetCapabilityIds.Mutations,GameCapabilityIds.Resources});
    internal ColorPlugin(string id,int priority,ResourceColor color,bool legacy,string? resourceKey){_id=id;_priority=priority;_color=color;_legacy=legacy;_resourceKey=resourceKey;}
    public void Enable(IPluginContext context)
    {
        Targets=context.GetService<ITargetService>(TargetCapabilityIds.Targets).Service!;Mutations=context.GetService<IMutationService>(TargetCapabilityIds.Mutations).Service!;
        var target=_resourceKey is null?Targets.GetTargets().First():Targets.GetTargets().Single(x=>x.Key==Targets.Resolve<ResourceColor>(new(Key:_resourceKey)).Target!.Key);
        if(_legacy){var result=context.GetService<IGameResourceService>(GameCapabilityIds.Resources).Service!.OverrideColor(new(target.Aliases.FirstOrDefault()??target.Key,_color,_priority));if(!result.Success)throw new InvalidOperationException(result.Error?.Message);}
        else{var result=Mutations.Override(new MutationKey<ResourceColor>(new(target.Key,target.Generation),"color"),_color,_priority);if(!result.Success)throw new InvalidOperationException(result.Error?.Message);}
    }
    public void Disable(){}
}
sealed class Fixture:IDisposable
{
    internal MainThreadDispatcher Main=new();internal Logger Log=new();internal Backend Backend=new();internal List<Image> Images=>Backend.Images;
    internal GameResourceService Service;internal CapabilityRegistry Capabilities;internal PluginHost Host;
    internal Fixture()
    {
        Service=new(Main,Log,Backend);Capabilities=new(new("test","test","test","test",true));
        Capabilities.RegisterScoped<ITargetService>(TargetCapabilityIds.Targets,c=>Service.TargetsForPlugin(c));
        Capabilities.RegisterScoped<IMutationService>(TargetCapabilityIds.Mutations,c=>Service.MutationsForPlugin(c));
        Capabilities.RegisterScoped<IGameResourceService>(GameCapabilityIds.Resources,c=>Service.ForPlugin(c));
        Host=new(Capabilities,Main,new EventHub(Log),Log);
    }
    internal Image Add(string id="one"){var image=new Image(id);Images.Add(image);return image;}
    internal Image AddTmp(string id="text"){var text=new Image(id){Slot=NativeGameColorSlot.TmpText};Images.Add(text);return text;}
    internal TargetHandle<ResourceColor> Resolve(string key)=>Service.Targets.Resolve<ResourceColor>(new(Key:key)).Target??throw new InvalidOperationException("No unique target: "+key);
    internal ColorPlugin Plugin(string id,int priority,ResourceColor color,bool legacy=false,string? resourceKey=null){var p=new ColorPlugin(id,priority,color,legacy,resourceKey);if(!Host.Register(p).Success||!Host.Enable(id).Success)throw new InvalidOperationException("Plugin activation failed: "+id);return p;}
    public void Dispose(){Host.Dispose();Service.Dispose();}
}
