using System.Buffers.Binary;
using System.IO.Compression;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Visual;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static Assert;

var tests=new (string Name,Action Run)[]{
    ("Visual failures carry explicit codes capability targets and retry policy without interpreting message prefixes",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var texture=Need(view.LoadPng(new(VisualFileArea.Package,"skin.png")));var sprite=Need(view.CreateSprite(texture.Asset));var bound=f.Target(view);
        var absent=new TargetHandle<VisualAsset>("absent-binding",999);
        Failure(view.Override(absent,sprite.Asset).Error,ErrorCode.TargetMissing,VisualCapabilityIds.Sprites,absent.Key);
        Failure(view.CreateSprite(texture.Asset,float.NaN).Error,ErrorCode.InvalidArgument,VisualCapabilityIds.Sprites,texture.Asset.Id);
        Failure(view.Override(bound,texture.Asset).Error,ErrorCode.TargetTypeMismatch,VisualCapabilityIds.Sprites,bound.Key);
        Failure(view.LoadBuiltinFont("custom.ttf").Error,ErrorCode.OperationUnsupported,VisualCapabilityIds.Sprites,"custom.ttf");
        Failure(view.Resolve(new(Key:"unseen")).Error,ErrorCode.TargetMissing,TargetCapabilityIds.Targets,"unseen",true);
        var extra=f.Add("other");target.Aliases.Add("shared");extra.Aliases.Add("shared");f.Refresh();
        Failure(view.Resolve(new(Key:"shared")).Error,ErrorCode.AmbiguousTarget,TargetCapabilityIds.Targets,"shared");
        target.OnWrite=_=>throw new IOException("TargetMissing: misleading backend text is only a message.");
        var writeFailure=view.Override(bound,sprite.Asset).Error;
        Failure(writeFailure,ErrorCode.MutationWriteFailed,TargetCapabilityIds.Mutations,bound.Key);
        Check(writeFailure!.Message.Contains("TargetMissing: misleading backend text",StringComparison.Ordinal));
        target.OnWrite=null;
    }),
    ("Null visual arguments return InvalidArgument before backend file allocation target or liveness access",()=>{
        foreach(var kind in Enum.GetValues<VisualTargetKind>())
        {
            using var f=new Fixture(kind);f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var target=f.Target(view);
            var asset=f.Asset(view);var before=f.AccessSnapshot();var allocations=(f.Budget.AssetCount,f.Budget.EstimatedBytes,f.Retirement.Reserved);
            RefusedNullVisualResults(view,ErrorCode.InvalidArgument,VisualModule.Capability(kind),asset.Asset,target);
            f.VerifyNoAccess(before,allocations,kind+" live null batch");
        }
    }),
    ("Visual thread scope and provider guards take precedence over null arguments without native access",()=>{
        foreach(var kind in Enum.GetValues<VisualTargetKind>())foreach(var ending in new[]{"worker","scope","provider"})
        {
            using var f=new Fixture(kind);f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var target=f.Target(view);var asset=f.Asset(view);
            if(ending=="scope")c.Dispose();else if(ending=="provider")f.Service.Dispose();
            var before=f.AccessSnapshot();var allocations=(f.Budget.AssetCount,f.Budget.EstimatedBytes,f.Retirement.Reserved);
            if(ending=="worker")Task.Run(()=>RefusedNullVisualResults(view,ErrorCode.WrongThread,VisualModule.Capability(kind),asset.Asset,target)).GetAwaiter().GetResult();
            else RefusedNullVisualResults(view,ErrorCode.ScopeExpired,VisualModule.Capability(kind),asset.Asset,target);
            f.VerifyNoAccess(before,allocations,kind+" "+ending+" null batch");
        }
    }),
    ("Visual override SetValue reports lease scope provider and thread failures before its unsupported operation",()=>{
        foreach(var kind in Enum.GetValues<VisualTargetKind>())foreach(var ending in new[]{"lease","scope","provider"})
        {
            using var f=new Fixture(kind);f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var target=f.Target(view);var asset=f.Asset(view);
            var lease=Need(view.Override(target,asset.Asset));var capability=VisualModule.Capability(kind);var before=f.AccessSnapshot();
            Failure(lease.SetValue(asset.Asset).Error,ErrorCode.OperationUnsupported,capability,target.Key);
            Failure(lease.SetValue(null!).Error,ErrorCode.OperationUnsupported,capability,target.Key);
            Task.Run(()=>{
                Failure(lease.SetValue(asset.Asset).Error,ErrorCode.WrongThread,capability,target.Key);
                Failure(lease.SetValue(null!).Error,ErrorCode.WrongThread,capability,target.Key);
            }).GetAwaiter().GetResult();
            Equal(before,f.AccessSnapshot());
            if(ending=="lease")lease.Dispose();else if(ending=="scope")c.Dispose();else f.Service.Dispose();
            before=f.AccessSnapshot();var allocations=(f.Budget.AssetCount,f.Budget.EstimatedBytes,f.Retirement.Reserved);
            Failure(lease.SetValue(asset.Asset).Error,ErrorCode.ScopeExpired,capability,target.Key);
            Failure(lease.SetValue(null!).Error,ErrorCode.ScopeExpired,capability,target.Key);
            Equal(before,f.AccessSnapshot());Equal(allocations,(f.Budget.AssetCount,f.Budget.EstimatedBytes,f.Retirement.Reserved));
        }
    }),
    ("Visual override SetValue reports an expired target generation without native access",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var handle=f.Target(view);
        var lease=Need(view.Override(handle,asset.Asset));target.Alive=false;f.Refresh();var before=f.AccessSnapshot();
        Failure(lease.SetValue(asset.Asset).Error,ErrorCode.TargetExpired,VisualCapabilityIds.Sprites,handle.Key);
        Equal(before,f.AccessSnapshot());
    }),
    ("TMP receivers never expose Graphic.material when font-shared accessors are missing",()=>{
        Check(VisualMaterialSlots.Select(true,false,false) is null);Check(VisualMaterialSlots.Select(true,false,true) is null);
        var slot=VisualMaterialSlots.Select(true,true,true)!;Equal("get_fontSharedMaterial",slot.ReadMethod);Equal("set_fontSharedMaterial",slot.WriteMethod);
        Equal("unity.tmp.font-shared-material",slot.ResourceId);Equal("material",slot.Role);
    }),
    ("Translucent and generic UI keep the Graphic material slot without a shader-name grant",()=>{
        var translucent=VisualMaterialSlots.Select(false,false,true)!;var ordinary=VisualMaterialSlots.Select(false,true,false)!;
        Equal("get_material",translucent.ReadMethod);Equal("set_material",translucent.WriteMethod);Equal("graphic",translucent.Id);
        Equal("graphic",ordinary.Id);Equal("unity.ui.material",ordinary.ResourceId);Equal("material",translucent.Role);
        Check(translucent.ResourceId!=ordinary.ResourceId);
    }),
    ("A busy ordinary Graphic scan cannot consume the initial quota ahead of later real component slots",()=>{
        var inputs=Enumerable.Range(0,200).Select(i=>(Candidate:"ordinary-"+i,Slot:VisualMaterialSlots.Graphic,ScanOrder:i)).ToList();
        inputs.Add(("tmp",VisualMaterialSlots.TmpFontShared,210));inputs.Add(("translucent",VisualMaterialSlots.Translucent,211));
        var bounded=VisualMaterialSlots.Prioritize(inputs).Take(VisualResourceService.MaximumTargets).ToArray();
        Equal(VisualResourceService.MaximumTargets,bounded.Length);Equal("tmp",bounded[0]);Equal("translucent",bounded[1]);Equal("ordinary-0",bounded[2]);Check(!bounded.Contains("ordinary-199"));
    }),
    ("Slot selection depends on verified receiver identity while profile parameter validation remains independent",()=>{
        var slot=VisualMaterialSlots.Select(true,true,false)!;Equal("tmp-font-shared",slot.Id);
        var request=new VisualMaterialRequest(new("tmp-slot",1),Floats:new[]{new MaterialFloatProperty("_Brightness",.7f)});
        var candidates=VisualMaterialProfiles.Candidates(request);Equal(1,candidates.Count);Equal(VisualMaterialProfiles.Translucent,candidates[0]);
        // Being a valid TMP slot never authorizes a Translucent property profile on its own.
        Check(candidates.All(x=>x.Id!=VisualMaterialProfiles.TextOutline.Id));
    }),
    ("Resource PNG budgets permit usable 128px skins while old icon API stays 64px",()=>{
        var png=Png(128,96);Throws<InvalidDataException>(()=>PngDecoder.Decode(png));var decoded=PngDecoder.Decode(png,2048,2048*2048,64L*1024*1024,16*1024*1024);
        Equal(128,decoded.Width);Equal(96,decoded.Height);Equal(128*96,decoded.Pixels.Length);
    }),
    ("PNG pixels, side, decoded bytes and checksums reject malformed allocations",()=>{
        var png=Png(128,128);Throws<InvalidDataException>(()=>PngDecoder.Decode(png,64,1000000,64000000,1000000));
        Throws<InvalidDataException>(()=>PngDecoder.Decode(png,2048,100,64000000,1000000));Throws<InvalidDataException>(()=>PngDecoder.Decode(png,2048,1000000,100,1000000));
        png[^5]^=1;Throws<InvalidDataException>(()=>PngDecoder.Decode(png,2048,1000000,64000000,1000000));
    }),
    ("PNG subset refuses unsupported transparency, unknown critical chunks and trailing data",()=>{
        var png=Png(2,2);Throws<InvalidDataException>(()=>PngDecoder.Decode(png.Concat(new byte[]{0}).ToArray()));
        foreach(var kind in new[]{"tRNS","ABCD"}){using var output=new MemoryStream();output.Write(png.AsSpan(0,33));Chunk(output,kind,Array.Empty<byte>());output.Write(png.AsSpan(33));Throws<InvalidDataException>(()=>PngDecoder.Decode(output.ToArray()));}
    }),
    ("Package and data paths reject traversal, absolute paths, ADS and wrong extensions",()=>{
        using var f=new Fixture();var files=f.Files;foreach(var path in new[]{"../a.png","a/../a.png","C:/a.png","a.png:stream","./a.png","a//b.png","a.jpg"})
            Throws<InvalidDataException>(()=>files.Read("test.a",new(VisualFileArea.Package,path)));
        Equal(Png(2,2).Length,files.Read("test.a",new(VisualFileArea.Package,"skin.png")).Length);
        Equal(Png(2,2).Length,files.Read("test.a",new(VisualFileArea.PluginData,"skin.png")).Length);
        Throws<InvalidDataException>(()=>new VisualFileAccess(_=>null,_=>null).Read("test.a",new(VisualFileArea.Package,"skin.png")));
    }),
    ("Texture and sprite remain retained after user assets are released while override is active",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();var context=f.Context();var view=f.Service.ForPlugin(context);
        var texture=Need(view.LoadPng(new(VisualFileArea.Package,"skin.png")));var sprite=Need(view.CreateSprite(texture.Asset));var native=f.Backend.Created.Last();var lease=Need(view.Override(f.Target(view),sprite.Asset));
        texture.Dispose();sprite.Dispose();Equal(native,target.Current);Equal(0,native.Destroyed);Equal(0,f.Backend.Created[0].Destroyed);
        lease.Dispose();Equal(target.Original,target.Current);Equal(1,native.Destroyed);Equal(1,f.Backend.Created[0].Destroyed);
    }),
    ("Three plugin visual replacements share one reversible priority stack",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();var a=f.Context("test.a");var b=f.Context("test.b");var c=f.Context("test.c");
        var av=f.Service.ForPlugin(a);var bv=f.Service.ForPlugin(b);var cv=f.Service.ForPlugin(c);var aa=f.Sprite(av);var ba=f.Sprite(bv);var ca=f.Sprite(cv);
        Need(av.Override(f.Target(av),aa.Asset,10));Need(bv.Override(f.Target(bv),ba.Asset,20));Need(cv.Override(f.Target(cv),ca.Asset,30));
        Equal(ca.Asset.Id,f.Backend.Created.Last().Token!);c.Dispose();Check(target.Current!=target.Original);b.Dispose();Check(target.Current!=target.Original);a.Dispose();Equal(target.Original,target.Current);
        Check(f.Backend.Created.All(x=>x.Destroyed==1));
    }),
    ("A real game write becomes the eventual baseline without destroying game objects",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var context=f.Context();var view=f.Service.ForPlugin(context);var asset=f.Sprite(view);Need(view.Override(f.Target(view),asset.Asset));
        var game=new FakeObject("external",VisualAssetKind.Sprite);target.Current=game;f.Refresh();Check(target.Current!=game);context.Dispose();Equal(game,target.Current);Equal(0,game.Destroyed);Equal(0,target.Original.Destroyed);
    }),
    ("Adopted framework assets stay alive while an inactive native receiver still references them",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var a=f.Context("test.a");using var b=f.Context("test.b");var av=f.Service.ForPlugin(a);var bv=f.Service.ForPlugin(b);
        var assetA=f.Sprite(av);var nativeA=f.Backend.Created.Last();var assetB=f.Sprite(bv);var lease=Need(bv.Override(f.Target(bv),assetB.Asset));target.Current=nativeA;f.Refresh();
        a.Dispose();Equal(0,nativeA.Destroyed);lease.Dispose();Equal(nativeA,target.Current);Equal(0,nativeA.Destroyed);target.Active=false;f.Refresh();Equal(0,nativeA.Destroyed);
        target.Current=target.Original;f.FlushCleanup();Equal(1,nativeA.Destroyed);
    }),
    ("Module withdrawal restores and destroys created objects with late Dispose safe",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var lease=Need(view.Override(f.Target(view),asset.Asset));
        f.Service.Dispose();Equal(target.Original,target.Current);Check(!asset.IsValid&&!lease.IsActive);Check(f.Backend.Created.All(x=>x.Destroyed==1));Equal(1,target.Released);c.Dispose();
    }),
    ("Destroyed targets are invalidated without a setter into dead native objects",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var old=f.Target(view);var asset=f.Sprite(view);var lease=Need(view.Override(old,asset.Asset));var writes=target.Writes;
        target.Alive=false;f.Refresh();Equal(writes,target.Writes);Equal(1,target.Released);Check(!lease.IsActive);Check(!view.Override(old,asset.Asset).Success);
    }),
    ("Inactive and template objects are excluded and active leases restore before release",()=>{
        using var f=new Fixture();var active=f.Add();f.Add("template").Template=true;f.Add("inactive").Active=false;f.Add("unloaded").Loaded=false;f.Refresh();
        using var c=f.Context();var v=f.Service.ForPlugin(c);Equal(1,v.GetTargets().Count);var asset=f.Sprite(v);Need(v.Override(f.Target(v),asset.Asset));active.Active=false;f.Refresh();Equal(active.Original,active.Current);Equal(1,active.Released);
    }),
    ("Reconstructed objects reject previous generation even with the same aliases",()=>{
        using var f=new Fixture();var old=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var first=f.Target(v);old.Alive=false;f.Refresh();f.Backend.Targets.Clear();f.Add();f.Refresh();var next=f.Target(v);
        Check(next.Key!=first.Key);var asset=f.Sprite(v);Check(!v.Override(first,asset.Asset).Success);Need(v.Override(next,asset.Asset));
    }),
    ("Aliases of one object cannot split layers and duplicate names are ambiguous",()=>{
        using var f=new Fixture();var target=f.Add();target.Aliases.Add("shared");f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);Equal(v.Resolve(new(Key:"shared")).Target,v.Resolve(new(Key:target.Alias)).Target);
        f.Add("second").Aliases.Add("shared");f.Refresh();Equal(TargetMatchKind.Ambiguous,v.Resolve(new(Key:"shared")).Kind);Equal(TargetMatchKind.Missing,v.Resolve(new(Key:"absent")).Kind);
    }),
    ("Two plugins borrow one built-in font and neither destroys it",()=>{
        using var f=new Fixture(VisualTargetKind.Font);var target=f.Add();f.Refresh();using var a=f.Context("test.a");using var b=f.Context("test.b");var av=f.Service.ForPlugin(a);var bv=f.Service.ForPlugin(b);
        var af=Need(av.LoadBuiltinFont("Arial.ttf"));var bf=Need(bv.LoadBuiltinFont("Arial.ttf"));Need(av.Override(f.Target(av),af.Asset,1));Need(bv.Override(f.Target(bv),bf.Asset,2));a.Dispose();Equal(f.Backend.Font,target.Current);b.Dispose();Equal(target.Original,target.Current);Equal(0,f.Backend.Font.Destroyed);
    }),
    ("Another plugin cannot spend an asset token it has not leased",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var a=f.Context("test.a");using var b=f.Context("test.b");var av=f.Service.ForPlugin(a);var bv=f.Service.ForPlugin(b);var asset=f.Sprite(av);
        Check(!bv.Override(f.Target(bv),asset.Asset).Success);Equal(target.Original,target.Current);asset.Dispose();Check(!av.Override(f.Target(av),asset.Asset).Success);
    }),
    ("Failed loads and unsupported fonts leave original game resources untouched",()=>{
        using var f=new Fixture(VisualTargetKind.Font);var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);
        Check(!v.LoadBuiltinFont("arbitrary.ttf").Success);f.Backend.FailFont=true;Check(!v.LoadBuiltinFont("Arial.ttf").Success);Equal(target.Original,target.Current);Equal(0,target.Writes);
    }),
    ("Decode and upload failures report ResourceLoadFailed and keep the applied asset",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var asset=f.Sprite(v);Need(v.Override(f.Target(v),asset.Asset));var before=target.Current;var writes=target.Writes;
        File.WriteAllBytes(f.FilePath,new byte[40]);var bad=v.LoadPng(new(VisualFileArea.Package,"skin.png"));Check(!bad.Success);Failure(bad.Error,ErrorCode.ResourceLoadFailed,VisualCapabilityIds.Sprites,"skin.png");Equal(before,target.Current);
        File.WriteAllBytes(f.FilePath,Png(2,2));f.Backend.FailTexture=true;var failed=v.LoadPng(new(VisualFileArea.Package,"skin.png"));Check(!failed.Success);Failure(failed.Error,ErrorCode.ResourceLoadFailed,VisualCapabilityIds.Sprites,"skin.png");Equal(before,target.Current);Equal(writes,target.Writes);
    }),
    ("Unmodified clones need no schema and familiar property names do not establish one",()=>{
        var target=new TargetHandle<VisualAsset>("test",1);var red=new ResourceColor(1,0,0);
        Equal(0,VisualMaterialProfiles.Candidates(new(target)).Count);
        Equal(0,VisualMaterialProfiles.Candidates(new(target,Array.Empty<MaterialColorProperty>(),Array.Empty<MaterialFloatProperty>())).Count);
        Throws<NotSupportedException>(()=>VisualMaterialProfiles.Candidates(new(target,new[]{new MaterialColorProperty("_Color",red)})));
        Throws<NotSupportedException>(()=>VisualMaterialProfiles.Candidates(new(target,Floats:new[]{new MaterialFloatProperty("_Glossiness",1)})));
        Throws<InvalidDataException>(()=>VisualMaterialProfiles.Candidates(new(target,new[]{new MaterialColorProperty("_Color",new(float.NaN,0,0))})));
    }),
    ("Unmodified material clones are independent and release restores the shared original",()=>{
        using var f=new Fixture(VisualTargetKind.Material);var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var red=new ResourceColor(1,0,0);
        var asset=Need(v.CreateMaterial(new(f.Target(v))));var clone=f.Backend.Created.Last();Check(clone!=target.Original);Equal(target.Original.Color,clone.Color);Need(v.Override(f.Target(v),asset.Asset));Equal(clone,target.Current);clone.Color=red;Check(target.Original.Color!=red);
        c.Dispose();Equal(target.Original,target.Current);Equal(1,clone.Destroyed);Equal(0,target.Original.Destroyed);
    }),
    ("Missing material schema evidence refuses parameter writes before creating a clone",()=>{
        using var f=new Fixture(VisualTargetKind.Material);var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);
        var result=v.CreateMaterial(new(f.Target(v),new[]{new MaterialColorProperty("_Color",new(1,0,0))}));Check(!result.Success);Failure(result.Error,ErrorCode.UnsupportedSignature,VisualCapabilityIds.Materials,f.Target(v).Key);Equal(0,f.Backend.Created.Count);Equal(0,target.Writes);
    }),
    ("Pinned material profiles select evidenced Color and Range properties without shader names",()=>{
        var target=new TargetHandle<VisualAsset>("source",1);
        var profiles=VisualMaterialProfiles.Candidates(new(target,new[]{new MaterialColorProperty("_FaceColor",new(1,.5f,0))},new[]{new MaterialFloatProperty("_OutlineWidth",.75f),new MaterialFloatProperty("_FaceDilate",-.5f)}));
        Equal(1,profiles.Count);Equal(VisualMaterialProfiles.TextOutline,profiles[0]);Equal("fonts & materials/liberationsans sdf - outline",profiles[0].ReferenceResource);Equal(VisualMaterialReferenceKind.Material,profiles[0].ReferenceKind);
        Equal(VisualMaterialProfiles.GameAssemblyHash,profiles[0].RequiredFiles["GameAssembly.dll"]);Check(profiles[0].RequiredFiles.ContainsKey("Rizline_Data/sharedassets0.assets"));
        var translucent=VisualMaterialProfiles.Candidates(new(target,Floats:new[]{new MaterialFloatProperty("_Brightness",-.5f)}));Equal(VisualMaterialProfiles.Translucent,translucent.Single());Equal(VisualMaterialReferenceKind.Shader,translucent[0].ReferenceKind);
    }),
    ("Material profile validation rejects wrong types, mixed schemas, duplicate and nonfinite parameters",()=>{
        var target=new TargetHandle<VisualAsset>("source",1);
        Throws<NotSupportedException>(()=>VisualMaterialProfiles.Candidates(new(target,Floats:new[]{new MaterialFloatProperty("_FaceColor",1)})));
        Throws<NotSupportedException>(()=>VisualMaterialProfiles.Candidates(new(target,new[]{new MaterialColorProperty("_FaceColor",new(1,1,1))},new[]{new MaterialFloatProperty("_Brightness",0)})));
        Throws<InvalidDataException>(()=>VisualMaterialProfiles.Candidates(new(target,new[]{new MaterialColorProperty("_FaceColor",new(1,1,1))},new[]{new MaterialFloatProperty("_FaceColor",0)})));
        Throws<InvalidDataException>(()=>VisualMaterialProfiles.Candidates(new(target,Floats:new[]{new MaterialFloatProperty("_OutlineWidth",float.NaN)})));
        Throws<InvalidDataException>(()=>VisualMaterialProfiles.Candidates(new(target,new[]{new MaterialColorProperty("_FaceColor",new(2,1,1))})));
    }),
    ("Material requests reject null entries, empty names, repeated values and oversized parameter lists",()=>{
        var target=new TargetHandle<VisualAsset>("source",1);var color=new MaterialColorProperty("_FaceColor",new(1,1,1));var value=new MaterialFloatProperty("_Brightness",0);
        Throws<ArgumentNullException>(()=>VisualMaterialProfiles.Candidates(null!));
        foreach(var invalid in new MaterialColorProperty[]{null!,new("",new(1,1,1)),new("_FaceColor",null!)})
            Throws<InvalidDataException>(()=>VisualMaterialProfiles.Candidates(new(target,new[]{invalid})));
        foreach(var invalid in new MaterialFloatProperty[]{null!,new(" ",0),new("_Brightness",float.PositiveInfinity)})
            Throws<InvalidDataException>(()=>VisualMaterialProfiles.Candidates(new(target,Floats:new[]{invalid})));
        Throws<InvalidDataException>(()=>VisualMaterialProfiles.Candidates(new(target,new[]{color,color})));
        Throws<InvalidDataException>(()=>VisualMaterialProfiles.Candidates(new(target,Floats:new[]{value,value})));
        Throws<InvalidDataException>(()=>VisualMaterialProfiles.Candidates(new(target,Floats:Enumerable.Range(0,17).Select(i=>new MaterialFloatProperty("_value"+i,0)).ToArray())));
        Throws<NotSupportedException>(()=>VisualMaterialProfiles.Candidates(new(target,new[]{new MaterialColorProperty("_Brightness",new(1,1,1))})));
    }),
    ("Serialized shader Range endpoints are accepted and values outside them are refused",()=>{
        var target=new TargetHandle<VisualAsset>("source",1);
        foreach(var width in new[]{0f,1f})VisualMaterialProfiles.Candidates(new(target,Floats:new[]{new MaterialFloatProperty("_OutlineWidth",width)}));
        foreach(var dilate in new[]{-1f,1f})VisualMaterialProfiles.Candidates(new(target,Floats:new[]{new MaterialFloatProperty("_FaceDilate",dilate)}));
        Throws<InvalidDataException>(()=>VisualMaterialProfiles.Candidates(new(target,Floats:new[]{new MaterialFloatProperty("_OutlineWidth",1.001f)})));
        Throws<InvalidDataException>(()=>VisualMaterialProfiles.Candidates(new(target,Floats:new[]{new MaterialFloatProperty("_FaceDilate",-1.001f)})));
    }),
    ("Material evidence files require exact content hashes and release read handles on success or failure",()=>{
        var directory=Path.Combine(Path.GetTempPath(),"RizToolbox.MaterialEvidence-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var path=Path.Combine(directory,"evidence.bin");File.WriteAllText(path,"known bytes");
        try
        {
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            var profile=VisualMaterialProfiles.TextOutline with{RequiredFiles=new Dictionary<string,string>{{"evidence.bin",hash}}};
            using(var held=VisualMaterialProfiles.VerifyFiles(profile,directory))Throws<IOException>(()=>{using var stream=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);});
            File.WriteAllText(path,"changed bytes");Throws<NotSupportedException>(()=>VisualMaterialProfiles.VerifyFiles(profile,directory));File.WriteAllText(path,"write after refused evidence proves cleanup");
            Throws<NotSupportedException>(()=>VisualMaterialProfiles.VerifyFiles(profile,Path.Combine(directory,"missing")));
        }
        finally{File.Delete(path);Directory.Delete(directory);}
    }),
    ("A later missing or corrupt evidence file releases all earlier handles and empty files are refused",()=>{
        var directory=Path.Combine(Path.GetTempPath(),"RizToolbox.MaterialEvidence-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var first=Path.Combine(directory,"first.bin");var second=Path.Combine(directory,"second.bin");File.WriteAllText(first,"first evidence");
        try
        {
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(first)));
            var profile=VisualMaterialProfiles.TextOutline with{RequiredFiles=new Dictionary<string,string>{{"first.bin",hash},{"second.bin",hash}}};
            Throws<NotSupportedException>(()=>VisualMaterialProfiles.VerifyFiles(profile,directory));
            using(var unlocked=new FileStream(first,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(unlocked.CanWrite);
            File.WriteAllText(second,"wrong evidence");Throws<NotSupportedException>(()=>VisualMaterialProfiles.VerifyFiles(profile,directory));
            using(var unlocked=new FileStream(first,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(unlocked.CanWrite);
            File.Copy(first,second,true);using(var held=VisualMaterialProfiles.VerifyFiles(profile,directory))
                Throws<IOException>(()=>{using var blocked=new FileStream(second,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);});
            File.WriteAllBytes(second,Array.Empty<byte>());Throws<NotSupportedException>(()=>VisualMaterialProfiles.VerifyFiles(profile,directory));
            using var afterFailure=new FileStream(second,FileMode.Open,FileAccess.ReadWrite,FileShare.None);Check(afterFailure.CanWrite);
        }
        finally{File.Delete(first);File.Delete(second);Directory.Delete(directory);}
    }),
    ("Two plugins parameterize independent clones through the production visual service stack",()=>{
        using var f=new Fixture(VisualTargetKind.Material);f.Backend.MaterialProfile=VisualMaterialProfiles.TextOutline;var target=f.Add();f.Refresh();using var a=f.Context("test.a");using var b=f.Context("test.b");var av=f.Service.ForPlugin(a);var bv=f.Service.ForPlugin(b);
        var first=Need(av.CreateMaterial(new(f.Target(av),new[]{new MaterialColorProperty("_FaceColor",new(1,0,0))},new[]{new MaterialFloatProperty("_OutlineWidth",.2f)})));var firstNative=f.Backend.Created.Last();
        var second=Need(bv.CreateMaterial(new(f.Target(bv),new[]{new MaterialColorProperty("_FaceColor",new(0,0,1))},new[]{new MaterialFloatProperty("_OutlineWidth",.8f)})));var secondNative=f.Backend.Created.Last();
        var low=Need(av.Override(f.Target(av),first.Asset,10));var high=Need(bv.Override(f.Target(bv),second.Asset,20));Equal(secondNative,target.Current);Equal(.8f,secondNative.Float);Equal(.2f,firstNative.Float);Equal(new ResourceColor(1,1,1),target.Original.Color);
        b.Dispose();Equal(firstNative,target.Current);Equal(1,secondNative.Destroyed);a.Dispose();Equal(target.Original,target.Current);Equal(1,firstNative.Destroyed);Equal(0,target.Original.Destroyed);
    }),
    ("A matching property list cannot authorize a different source shader in the backend seam",()=>{
        using var f=new Fixture(VisualTargetKind.Material);f.Backend.MaterialProfile=VisualMaterialProfiles.Translucent;var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);
        var failed=v.CreateMaterial(new(f.Target(v),new[]{new MaterialColorProperty("_FaceColor",new(1,0,0))}));Check(!failed.Success);Failure(failed.Error,ErrorCode.UnsupportedSignature,VisualCapabilityIds.Materials,f.Target(v).Key);Equal(0,f.Backend.Created.Count);Equal(0,target.Writes);
    }),
    ("Wrong asset type and invalid sprite geometry never mutate the target",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var texture=Need(v.LoadPng(new(VisualFileArea.Package,"skin.png")));
        Check(!v.Override(f.Target(v),texture.Asset).Success);Check(!v.CreateSprite(texture.Asset,float.NaN).Success);Check(!v.CreateSprite(texture.Asset,0).Success);Equal(0,target.Writes);
    }),
    ("Cached visual service rejects all operations after its plugin scope stops",()=>{
        foreach(var stopProvider in new[]{false,true})
        {
            using var f=new Fixture();f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
            var asset=f.Sprite(view);var target=f.Target(view);
            if(stopProvider)f.Service.Dispose();else c.Dispose();
            var before=f.AccessSnapshot();
            Throws<ObjectDisposedException>(()=>view.GetTargets());
            RefusedVisualResults(view,ErrorCode.ScopeExpired,asset.Asset,target);
            Equal(before,f.AccessSnapshot());
        }
    }),
    ("Formal host deactivation expires visual views before cleanup and releases parent and child resources without scope errors",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();
        var log=new CaptureLog();var capabilities=new CapabilityRegistry(new("test","test","test","test"));var events=new EventHub(log);
        capabilities.RegisterScoped<IVisualResourceService>("test.visual",f.Service.ForPlugin);
        using var host=new PluginHost(capabilities,f.Main,events,log);IScopedPluginContext context=null!;
        host.Register(new HostProbe(value=>context=(IScopedPluginContext)value));Check(host.Enable("visual.probe").Success);
        var parent=context.GetService<IVisualResourceService>("test.visual").Service!;var directParent=f.Service.ForPlugin(context);
        var parentAsset=f.Sprite(parent);var parentOverride=Need(directParent.Override(f.Target(parent),parentAsset.Asset,10));
        var child=context.CreateScope("visual-child");
        var childView=child.GetService<IVisualResourceService>("test.visual").Service!;var directChild=f.Service.ForPlugin(child);
        var childAsset=f.Sprite(childView);var childOverride=Need(directChild.Override(f.Target(childView),childAsset.Asset,20));
        var targetHandle=f.Target(directParent);
        var rejectedDuringCleanup=false;
        context.Own(new ActionDisposable(()=>{
            Throws<ObjectDisposedException>(()=>parent.GetTargets());Throws<ObjectDisposedException>(()=>childView.GetTargets());
            var before=f.AccessSnapshot();
            RefusedVisualResults(directParent,ErrorCode.ScopeExpired,parentAsset.Asset,targetHandle);
            RefusedVisualResults(directChild,ErrorCode.ScopeExpired,childAsset.Asset,targetHandle);
            Failure(parentOverride.SetValue(parentAsset.Asset).Error,ErrorCode.ScopeExpired,VisualCapabilityIds.Sprites,targetHandle.Key);
            Failure(childOverride.SetValue(childAsset.Asset).Error,ErrorCode.ScopeExpired,VisualCapabilityIds.Sprites,targetHandle.Key);
            Equal(before,f.AccessSnapshot());
            rejectedDuringCleanup=true;
        }));
        Check(host.Disable("visual.probe").Success);Check(host.Disable("visual.probe").Success);
        Check(rejectedDuringCleanup && !child.IsActive && ReferenceEquals(target.Original,target.Current));
        Equal(0,log.Errors.Count);Equal(0,f.Budget.AssetCount);Check(f.Backend.Created.All(x=>x.Destroyed==1&&x.Released==1));
        Check(host.Enable("visual.probe").Success);var replacement=context.GetService<IVisualResourceService>("test.visual").Service!;
        Equal(1,replacement.GetTargets().Count);Throws<ObjectDisposedException>(()=>parent.GetTargets());
        Check(host.Disable("visual.probe").Success);Equal(0,log.Errors.Count);
    }),
    ("Scene retirement during refresh is a transient refusal, not a plugin callback fault",()=>{
        using var f=new Fixture(VisualTargetKind.Material);var retired=f.Add("retired");f.Add("remaining");f.Refresh();
        var log=new CaptureLog();var capabilities=new CapabilityRegistry(new("test","test","test","test"));var events=new EventHub(log);
        capabilities.RegisterScoped<IVisualResourceService>("test.visual",f.Service.ForPlugin);
        using var host=new PluginHost(capabilities,f.Main,events,log);IPluginContext context=null!;
        host.Register(new HostProbe(value=>context=value));Check(host.Enable("visual.probe").Success);
        var view=context.GetService<IVisualResourceService>("test.visual").Service!;
        var target=view.GetTargets().First(x=>x.Target.Key.Contains("retired",StringComparison.Ordinal)).Target;
        var asset=Need(view.CreateMaterial(new(target)));Need(view.Override(target,asset.Asset));
        // A scene unload makes a receiver ineligible while its native liveness/read-back
        // remains uncertain. Another live target keeps Refresh's target count nonzero.
        retired.Loaded=false;retired.ThrowRead=true;Equal(1,f.Service.Refresh());
        Check(f.Retirement.HasPending(VisualTargetKind.Material));var writes=f.Backend.Targets.Sum(x=>x.Writes);var created=f.Backend.Created.Count;
        var callbacks=0;context.Events.Subscribe<FrameUpdate>(_=>{
            Equal(0,view.GetTargets().Count);
            var match=view.Resolve(new());Equal(TargetMatchKind.Missing,match.Kind);Failure(match.Error,ErrorCode.CapabilityUnavailable,VisualCapabilityIds.Materials,null,true);
            var create=view.CreateMaterial(new(target));Check(!create.Success);Failure(create.Error,ErrorCode.CapabilityUnavailable,VisualCapabilityIds.Materials,target.Key,true);
            var assign=view.Override(target,asset.Asset);Check(!assign.Success);Failure(assign.Error,ErrorCode.CapabilityUnavailable,VisualCapabilityIds.Materials,target.Key,true);
            Failure(view.LoadPng(new(VisualFileArea.Package,"skin.png")).Error,ErrorCode.CapabilityUnavailable,VisualCapabilityIds.Materials,"skin.png",true);
            Failure(view.CreateSprite(asset.Asset).Error,ErrorCode.CapabilityUnavailable,VisualCapabilityIds.Materials,asset.Asset.Id,true);
            Failure(view.LoadBuiltinFont("Arial.ttf").Error,ErrorCode.CapabilityUnavailable,VisualCapabilityIds.Materials,"Arial.ttf",true);
            callbacks++;
        });
        events.Publish(new FrameUpdate(1));Equal(1,callbacks);Equal(0,log.Errors.Count);Equal(PluginState.Enabled,host.Statuses.Single().State);
        Equal(created,f.Backend.Created.Count);Equal(writes,f.Backend.Targets.Sum(x=>x.Writes));Equal(0,f.Backend.Created.Single().Destroyed);
        retired.ThrowRead=false;retired.Alive=false;f.FlushCleanup();f.Refresh();Equal(1,view.GetTargets().Count);
        Check(host.Disable("visual.probe").Success);Equal(0,log.Errors.Count);
    }),
    ("Module reconciliation withdraws in the same refresh that begins native retirement and recovers after cleanup",()=>{
        using var f=new Fixture(VisualTargetKind.Material);var retired=f.Add("retired");f.Add("remaining");f.Refresh();
        retired.Loaded=false;retired.ThrowRead=true;
        var pending=VisualModule.Reconcile(f.Service,false);
        Check(!pending.Ready&&!pending.Permanent&&pending.Reason!.Contains("VisualCleanupPending",StringComparison.Ordinal));
        Equal(0,retired.Released);Check(f.Retirement.HasPending(VisualTargetKind.Material));
        retired.ThrowRead=false;retired.Alive=false;f.FlushCleanup();
        Check(VisualModule.Reconcile(f.Service,true).Ready);Equal(1,retired.Released);
    }),
    ("Background service calls and live asset probes fail before native backend access",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var handle=f.Target(view);var before=f.AccessSnapshot();
        Task.Run(()=>{Throws<InvalidOperationException>(()=>view.GetTargets());RefusedVisualResults(view,ErrorCode.WrongThread,asset.Asset,handle);Throws<InvalidOperationException>(()=>{_ = asset.IsValid;});Throws<InvalidOperationException>(()=>asset.Dispose());}).GetAwaiter().GetResult();
        Equal(before,f.AccessSnapshot());Equal(0,target.Writes);Check(asset.IsValid);
    }),
    ("Scope revocation during texture creation discards and destroys the result",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);f.Backend.AfterTexture=c.Dispose;
        var result=v.LoadPng(new(VisualFileArea.Package,"skin.png"));Check(!result.Success);Equal(1,f.Backend.Created.Single().Destroyed);Equal(1,f.Backend.Created.Single().Released);Equal(0,target.Writes);Equal(target.Original,target.Current);
    }),
    ("Module withdrawal during texture creation destroys the unattached result without root leaks",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);f.Backend.AfterTexture=f.Service.Dispose;
        var result=v.LoadPng(new(VisualFileArea.Package,"skin.png"));Check(!result.Success);Equal(1,f.Backend.Created.Single().Destroyed);Equal(1,f.Backend.Created.Single().Retained);Equal(1,f.Backend.Created.Single().Released);Equal(1,target.Released);
    }),
    ("Sprite creation retains its input texture across reentrant scope revocation",()=>{
        using var f=new Fixture();f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var texture=Need(v.LoadPng(new(VisualFileArea.Package,"skin.png")));var input=f.Backend.Created.Single();
        f.Backend.AfterSprite=()=>{c.Dispose();Equal(0,input.Destroyed);};var result=v.CreateSprite(texture.Asset);Check(!result.Success);Check(f.Backend.Created.All(x=>x.Destroyed==1&&x.Released==1));
    }),
    ("Scope revocation inside a setter rolls back the pending replacement",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var asset=f.Sprite(v);var candidate=f.Backend.Created.Last();
        target.OnWrite=x=>{target.Current=x;if(x==candidate)c.Dispose();};var result=v.Override(f.Target(v),asset.Asset);
        Check(!result.Success);Equal(target.Original,target.Current);Check(f.Backend.Created.All(x=>x.Destroyed==1));
    }),
    ("Reentrant override is refused without splitting the target stack",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var asset=f.Sprite(v);OperationResult<IMutationLease<VisualAsset>>? nested=null;
        target.OnWrite=x=>{target.Current=x;if(nested is null)nested=v.Override(f.Target(v),asset.Asset,2);};var lease=Need(v.Override(f.Target(v),asset.Asset));Check(nested is { Success:false });Failure(nested!.Error,ErrorCode.MutationReentrancy,TargetCapabilityIds.Mutations,f.Target(v).Key);
        target.OnWrite=null;lease.Dispose();Equal(target.Original,target.Current);
    }),
    ("Visual reservations bound owner bytes and counts and can be reused after release",()=>{
        var budget=new VisualResourceBudget();var leases=new List<IDisposable>();for(var i=0;i<32;i++)leases.Add(budget.Reserve("test",1));Throws<VisualOperationException>(()=>budget.Reserve("test",1));foreach(var lease in leases)lease.Dispose();
        using var one=budget.Reserve("test",VisualResourceBudget.MaximumPluginBytes);Throws<VisualOperationException>(()=>budget.Reserve("test",1));using var other=budget.Reserve("other",VisualResourceBudget.MaximumPluginBytes);
    }),
    ("Repeated shared-font leases still obey the per-plugin asset count limit",()=>{
        using var f=new Fixture(VisualTargetKind.Font);f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var leases=new List<IVisualAssetLease>();for(var i=0;i<32;i++)leases.Add(Need(v.LoadBuiltinFont("Arial.ttf")));
        var exhausted=v.LoadBuiltinFont("Arial.ttf");Check(!exhausted.Success);Failure(exhausted.Error,ErrorCode.QuotaExceeded,VisualCapabilityIds.Fonts,"Arial.ttf");leases[10].Dispose();Need(v.LoadBuiltinFont("Arial.ttf"));
    }),
    ("Borrowed asset and owned sprite roots are each released exactly once",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var asset=f.Sprite(v);Need(v.Override(f.Target(v),asset.Asset));
        Equal(1,target.Retained);Equal(1,target.Original.Retained);Check(f.Backend.Created.All(x=>x.Retained==1));c.Dispose();Check(f.Backend.Created.All(x=>x.Released==1&&x.Destroyed==1));Equal(0,target.Original.Released);
        f.Service.Dispose();Equal(1,target.Released);Equal(1,target.Original.Released);Equal(0,target.Original.Destroyed);Check(f.Backend.Created.All(x=>x.Released==1&&x.Destroyed==1));
    }),
    ("Destruction reentry and destruction failure do not skip roots or destroy borrowed resources",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var asset=f.Sprite(v);var sprite=f.Backend.Created.Last();sprite.OnDestroy=()=>{f.Service.Dispose();throw new IOException("destroy failed after callback");};
        asset.Dispose();Check(f.Backend.Created.All(x=>x.Released==1&&x.Destroyed==1));Equal(1,target.Released);Equal(1,target.Original.Released);Equal(0,target.Original.Destroyed);
    }),
    ("Read-back mismatch refuses ownership and does not overwrite an external asset",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var asset=f.Sprite(v);var external=new FakeObject("external",VisualAssetKind.Sprite);target.OnWrite=_=>target.Current=external;
        Check(!v.Override(f.Target(v),asset.Asset).Success);Equal(external,target.Current);f.Service.Dispose();Equal(external,target.Current);Equal(0,external.Destroyed);
    }),
    ("Setter failure after writing rolls back only the attempted replacement",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var v=f.Service.ForPlugin(c);var asset=f.Sprite(v);var candidate=f.Backend.Created.Last();target.OnWrite=x=>{target.Current=x;if(x==candidate)throw new IOException("setter failed");};
        Check(!v.Override(f.Target(v),asset.Asset).Success);Equal(target.Original,target.Current);c.Dispose();Check(f.Backend.Created.All(x=>x.Destroyed==1));
    }),
    ("Module restoration failure retains attached resources and budgets after logical withdrawal",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var asset=f.Sprite(view);var clone=f.Backend.Created.Last();var lease=Need(view.Override(f.Target(view),asset.Asset));
        var fail=true;target.OnWrite=value=>{if(value==target.Original&&fail)throw new IOException("restore blocked");target.Current=value;};
        f.Service.Dispose();c.Dispose();Check(!asset.IsValid&&!lease.IsActive);Equal(clone,target.Current);
        Equal(0,clone.Destroyed);Equal(0,clone.Released);Equal(0,target.Released);Equal(2,f.Budget.AssetCount);Check(f.Retirement.HasPending(VisualTargetKind.Sprite));
        fail=false;f.FlushCleanup();Equal(target.Original,target.Current);Equal(1,clone.Destroyed);Equal(1,clone.Released);Equal(1,target.Released);Equal(0,f.Budget.AssetCount);
    }),
    ("Failed final override restoration cannot be adopted as the next idle baseline",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var asset=f.Sprite(view);var clone=f.Backend.Created.Last();var lease=Need(view.Override(f.Target(view),asset.Asset));
        var fail=true;target.OnWrite=value=>{if(value==target.Original&&fail)throw new IOException("restore blocked");target.Current=value;};
        lease.Dispose();asset.Dispose();c.Dispose();Equal(clone,target.Current);Equal(0,clone.Destroyed);Check(f.Retirement.HasPending(VisualTargetKind.Sprite));
        f.Refresh();Equal(clone,target.Current);Equal(0,clone.Destroyed);
        using(var next=new VisualResourceService(f.Main,new Logger(),VisualTargetKind.Sprite,f.Backend,f.Files,f.Budget,new(f.Main,new Logger()))) {
            Equal(0,next.Refresh());Throws<InvalidOperationException>(()=>next.ForPlugin(new Context("new",f.Main)));
        }
        fail=false;f.FlushCleanup();Equal(target.Original,target.Current);Equal(1,clone.Destroyed);f.Refresh();
        using var fresh=f.Context("fresh");Equal(1,f.Service.ForPlugin(fresh).GetTargets().Count);
    }),
    ("A setter that writes and throws with a failed rollback keeps its attached clone rooted",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var asset=f.Sprite(view);var clone=f.Backend.Created.Last();var fail=true;
        target.OnWrite=value=>{if(fail){if(value==clone)target.Current=value;throw new IOException("setter or rollback failed");}target.Current=value;};
        Check(!view.Override(f.Target(view),asset.Asset).Success);asset.Dispose();c.Dispose();
        Equal(clone,target.Current);Equal(0,clone.Destroyed);Equal(0,clone.Released);Check(f.Retirement.HasPending(VisualTargetKind.Sprite));
        fail=false;f.FlushCleanup();Equal(target.Original,target.Current);Equal(1,clone.Destroyed);Equal(1,clone.Released);
    }),
    ("Retirement preserves a later game write instead of overwriting it with an old baseline",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var asset=f.Sprite(view);var clone=f.Backend.Created.Last();Need(view.Override(f.Target(view),asset.Asset));
        target.OnWrite=_=>throw new IOException("restore failed");f.Service.Dispose();c.Dispose();
        var foreign=new FakeObject("new-game-write",VisualAssetKind.Sprite);target.Current=foreign;var writes=target.Writes;
        f.FlushCleanup();Equal(foreign,target.Current);Equal(writes,target.Writes);Equal(0,foreign.Destroyed);Equal(1,clone.Destroyed);
    }),
    ("Unreadable targets and unloaded scenes are not evidence that a native receiver died",()=>{
        using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var asset=f.Sprite(view);var clone=f.Backend.Created.Last();Need(view.Override(f.Target(view),asset.Asset));
        target.ThrowRead=true;target.ThrowLiveness=true;f.Service.Dispose();c.Dispose();f.FlushCleanup();
        Equal(0,clone.Destroyed);Equal(0,target.Released);Check(f.Retirement.Pending>0);
        target.ThrowLiveness=false;target.Alive=false;f.FlushCleanup();Equal(1,clone.Destroyed);Equal(1,target.Released);
    }),
    ("Deferred destruction holds dependencies and budget until native death without repeated Destroy",()=>{
        using var f=new Fixture();f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var asset=f.Sprite(view);var clone=f.Backend.Created.Last();clone.DeferDestruction=true;
        c.Dispose();Equal(1,clone.Destroyed);Equal(0,clone.Released);Equal(0,f.Backend.Created.First().Destroyed);Equal(2,f.Budget.AssetCount);
        f.FlushCleanup();Equal(1,clone.Destroyed);Equal(0,clone.Released);clone.NativeDead=true;f.FlushCleanup();
        Equal(1,clone.Released);Equal(1,f.Backend.Created.First().Destroyed);Equal(0,f.Budget.AssetCount);
    }),
    ("Destruction throwing before native deletion is retried with roots and dependencies retained",()=>{
        using var f=new Fixture();f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var asset=f.Sprite(view);var clone=f.Backend.Created.Last();var fail=true;clone.BeforeDestroy=()=>{if(fail)throw new IOException("native destroy failed");};
        c.Dispose();Equal(0,clone.Destroyed);Equal(0,clone.Released);Equal(2,f.Budget.AssetCount);
        fail=false;f.FlushCleanup();Equal(1,clone.Destroyed);Equal(1,clone.Released);Equal(0,f.Budget.AssetCount);
    }),
    ("Retirement completion releases capacity before a throwing diagnostic sink and never repeats cleanup",()=>{
        using var main=new MainThreadDispatcher();var log=new RetirementLog();var retirement=new VisualResourceRetirement(main,log,1);
        using var ticket=retirement.Reserve(VisualTargetKind.Material);var ready=false;var cleanups=0;var observedReleased=false;
        log.OnLog=level=>{if(level==LogLevel.Info)observedReleased=retirement.Reserved==0&&retirement.Pending==0;throw new IOException("diagnostic sink failed");};
        ticket.Retire("deferred cleanup fixture",()=>{cleanups++;return ready;});
        Equal(1,retirement.Reserved);Equal(1,retirement.Pending);Equal(1,cleanups);Equal(0,log.Levels.Count(x=>x==LogLevel.Info));
        ready=true;retirement.Tick(.25);
        Equal(0,retirement.Reserved);Equal(0,retirement.Pending);Equal(2,cleanups);Equal(2L,ticket.Attempts);
        Check(observedReleased);Equal(1,log.Levels.Count(x=>x==LogLevel.Info));
        retirement.Tick(31);Equal(2,cleanups);Equal(1,log.Levels.Count(x=>x==LogLevel.Info));
        using var recovered=retirement.Reserve(VisualTargetKind.Material);Equal(1,retirement.Reserved);
    }),
    ("Retirement reservations bound process growth and throttle retry work",()=>{
        using var main=new MainThreadDispatcher();var one=new VisualResourceRetirement(main,new Logger(),3);var tickets=new List<VisualResourceRetirement.Ticket>();
        try {
            for(var i=0;i<3;i++)tickets.Add(one.Reserve(VisualTargetKind.Material,true));
            Throws<VisualOperationException>(()=>one.Reserve(VisualTargetKind.Material));
            var attempts=0;foreach(var ticket in tickets)ticket.Retire("failure fixture",()=>{attempts++;return false;});
            Equal(3,attempts);one.Tick(0);Equal(3,attempts);one.Tick(.25);Equal(6,attempts);one.Tick(.3);Equal(6,attempts);
            Check(one.HasPending(VisualTargetKind.Material));Check(!one.HasPending(VisualTargetKind.Font));
        } finally { foreach(var ticket in tickets)ticket.Dispose(); }
        Equal(0,one.Reserved);Check(!one.HasPending(VisualTargetKind.Material));
    }),
    ("A full cache still refreshes existing aliases and metadata after encountering a new target",()=>{
        using var f=new Fixture();for(var i=0;i<128;i++)f.Add("old-"+i);f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var old=NeedTarget(view,"game.visual/old-127");var last=f.Backend.Targets[127];last.Aliases.Add("renamed");last.Scene="new-scene";
        var late=f.Add("late");late.DiscoveryPriority=100;f.Refresh();
        Equal(old,NeedTarget(view,"renamed"));Equal(old,view.Resolve(new(Scene:"new-scene")).Target!);Equal(128,view.GetTargets().Count);
    }),
    ("Late high priority targets replace idle lower priority bindings with fresh handles",()=>{
        using var f=new Fixture(VisualTargetKind.Material);for(var i=0;i<128;i++)f.Add("old-"+i);f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var old=NeedTarget(view,"game.visual/old-0");var late=f.Add("late-tmp");late.DiscoveryPriority=100;f.Refresh();
        var current=NeedTarget(view,late.Alias);Equal(128,view.GetTargets().Count);Equal(1,f.Backend.Targets[0].Released);Equal(0,f.Backend.Targets.Sum(x=>x.Writes));
        Check(!view.CreateMaterial(new(old)).Success);Need(view.CreateMaterial(new(current)));Check(!f.Service.CleanupPending);
    }),
    ("Equal priority admission rotates at most one old binding and eventually admits the waiting tail",()=>{
        using var f=new Fixture();for(var i=0;i<134;i++)f.Add("target-"+i);f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var seen=new HashSet<string>();var previous=view.GetTargets().Select(x=>x.Target).ToHashSet();
        for(var tick=0;tick<6;tick++){
            f.Refresh();var now=view.GetTargets();Equal(128,now.Count);Equal(1,previous.Except(now.Select(x=>x.Target)).Count());
            foreach(var item in now)foreach(var alias in item.Aliases)seen.Add(alias);previous=now.Select(x=>x.Target).ToHashSet();
        }
        for(var i=128;i<134;i++)Check(seen.Contains("game.visual/target-"+i));
        Equal(0,f.Backend.Targets.Sum(x=>x.Writes));
    }),
    ("Previously admitted identities beyond one scan window cannot masquerade as an unseen waiting tail",()=>{
        using var f=new Fixture();FakeTarget? previous=null;
        // Keep native work per pass small while exercising 5000 actual service admissions.
        // The objects stay alive and merely toggle eligibility, as pooled UI objects can.
        for(var i=0;i<5000;i++){
            if(previous is not null)previous.Active=false;
            var next=f.Add("history-"+i);f.Backend.DiscoveryTargets=()=>new[]{next};f.Refresh();previous=next;
        }
        previous!.Active=false;f.Service.Refresh(false);
        var occupied=f.Backend.Targets.Skip(4800).Take(128).ToArray();foreach(var target in occupied)target.Active=true;
        f.Backend.DiscoveryTargets=()=>occupied;f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);Equal(128,view.GetTargets().Count);
        var prefix=f.Backend.Targets[0];prefix.Active=true;var unseen=f.Add("never-admitted");
        f.Backend.DiscoveryTargets=()=>new[]{prefix,unseen};f.Refresh();
        NeedTarget(view,unseen.Alias);Check(view.Resolve(new(Key:prefix.Alias)).Kind==TargetMatchKind.Missing);Equal(128,view.GetTargets().Count);
    }),
    ("Active overrides block admission until a holder releases without disturbing surviving owners",()=>{
        using var f=new Fixture();for(var i=0;i<128;i++)f.Add("old-"+i);f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);
        var handles=Enumerable.Range(0,128).Select(i=>NeedTarget(view,"game.visual/old-"+i)).ToArray();
        var holders=handles.Select(x=>Need(view.Override(x,asset.Asset))).ToArray();var late=f.Add("late");late.DiscoveryPriority=100;
        f.Refresh();Check(view.Resolve(new(Key:late.Alias)).Kind==TargetMatchKind.Missing);Check(holders.All(x=>x.IsActive));Equal(0,f.Backend.Targets.Sum(x=>x.Released));
        holders[127].Dispose();f.Refresh();NeedTarget(view,late.Alias);Check(holders.Take(127).All(x=>x.IsActive));Equal(1,f.Backend.Targets[127].Released);
        for(var i=0;i<127;i++)Equal(handles[i],NeedTarget(view,"game.visual/old-"+i));
    }),
    ("Lower priority arrivals never evict an idle higher priority cache",()=>{
        using var f=new Fixture();for(var i=0;i<128;i++)f.Add("high-"+i).DiscoveryPriority=100;f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var before=view.GetTargets().Select(x=>x.Target).ToHashSet();f.Add("low");for(var i=0;i<4;i++)f.Refresh();
        Check(before.SetEquals(view.GetTargets().Select(x=>x.Target)));Equal(0,f.Backend.Targets.Sum(x=>x.Released));
    }),
    ("An idle receiver still holding an owned game baseline is protected until proven detached",()=>{
        using var f=new Fixture();var target=f.Add("protected");for(var i=1;i<128;i++)f.Add("high-"+i).DiscoveryPriority=100;f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var native=f.Backend.Created.Last();
        target.Current=native;f.Service.Refresh(false);asset.Dispose();var late=f.Add("late");late.DiscoveryPriority=50;f.Refresh();
        Check(view.Resolve(new(Key:late.Alias)).Kind==TargetMatchKind.Missing);Equal(0,target.Released);Equal(0,native.Destroyed);
        target.Current=target.Original;f.Refresh();NeedTarget(view,late.Alias);Equal(1,target.Released);Equal(1,native.Destroyed);
    }),
    ("Admission failure during native retirement retains the victim and stops every new binding",()=>{
        using var f=new Fixture();var target=f.Add("victim");for(var i=1;i<128;i++)f.Add("high-"+i).DiscoveryPriority=100;f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);
        var reads=0;target.OnRead=()=>{if(++reads>=2)throw new IOException("retirement read failed");};
        var late=f.Add("late");late.DiscoveryPriority=50;var other=f.Add("later");other.DiscoveryPriority=40;f.Refresh();
        Check(f.Service.CleanupPending);Equal(0,target.Released);Equal(0,view.GetTargets().Count);
        Equal(late.Retained,late.Released);Equal(other.Retained,other.Released);Equal(0,late.Reads);Equal(0,other.Reads);
        target.OnRead=null;f.FlushCleanup();f.Refresh();NeedTarget(view,late.Alias);Equal(1,target.Released);
    }),
    ("A discovery iterator fault releases its selected candidate root and preserves existing bindings",()=>{
        using var f=new Fixture();for(var i=0;i<128;i++)f.Add("old-"+i);f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var before=view.GetTargets().Select(x=>x.Target).ToHashSet();
        var late=f.Add("late");late.DiscoveryPriority=100;f.Backend.ThrowAfterYield=1;Throws<IOException>(()=>f.Refresh());
        Equal(late.Retained,late.Released);Check(before.SetEquals(view.GetTargets().Select(x=>x.Target)));Check(!f.Service.CleanupPending);
        f.Backend.ThrowAfterYield=null;f.Refresh();NeedTarget(view,late.Alias);
    }),
    ("Refresh inside a revoked native setter never evicts that in flight receiver",()=>{
        using var f=new Fixture();var target=f.Add("inflight");for(var i=1;i<128;i++)f.Add("high-"+i).DiscoveryPriority=100;f.Refresh();
        using var owner=f.Context();using var observer=f.Context("observer");var view=f.Service.ForPlugin(owner);var readOnly=f.Service.ForPlugin(observer);var handle=NeedTarget(view,target.Alias);var asset=f.Sprite(view);var late=f.Add("late");late.DiscoveryPriority=50;var checkedInside=false;
        target.OnWrite=x=>{target.Current=x;if(checkedInside)return;checkedInside=true;owner.Dispose();f.Refresh();Equal(0,target.Released);Equal(handle,NeedTarget(readOnly,target.Alias));Check(readOnly.Resolve(new(Key:late.Alias)).Kind==TargetMatchKind.Missing);};
        Check(!view.Override(handle,asset.Asset).Success);Check(checkedInside);target.OnWrite=null;Equal(target.Original,target.Current);
        f.Refresh();NeedTarget(readOnly,late.Alias);Equal(1,target.Released);
    }),
    ("Target cache is bounded and every retained game root is released",()=>{
        using var f=new Fixture();for(var i=0;i<VisualResourceService.MaximumTargets+10;i++)f.Add(i.ToString());f.Refresh();using var c=f.Context();Equal(VisualResourceService.MaximumTargets,f.Service.ForPlugin(c).GetTargets().Count);
        f.Service.Dispose();Equal(VisualResourceService.MaximumTargets,f.Backend.Targets.Sum(x=>x.Released));Check(f.Backend.Targets.All(x=>x.Original.Destroyed==0));
    }),
    ("Service responsibility admission fails before native roots and returns its slot after normal shutdown",()=>{
        using var main=new MainThreadDispatcher();var retirement=new VisualResourceRetirement(main,new Logger(),1);
        var backend=new Backend(VisualTargetKind.Sprite);var target=new FakeTarget("one",VisualTargetKind.Sprite);backend.Targets.Add(target);
        var files=new VisualFileAccess(_=>null,_=>null);
        using(var service=new VisualResourceService(main,new Logger(),VisualTargetKind.Sprite,backend,files,retirement:retirement)){
            Equal(1,retirement.Reserved);
            var refusal=Throws<VisualOperationException>(()=>new VisualResourceService(main,new Logger(),VisualTargetKind.Sprite,backend,files,retirement:retirement));
            Equal(ErrorCode.QuotaExceeded,refusal.Code);Check(!refusal.Retryable);
            Equal(0,target.Retained);Equal(1,retirement.Reserved);
        }
        Equal(0,retirement.Reserved);
        using var recovered=new VisualResourceService(main,new Logger(),VisualTargetKind.Sprite,backend,files,retirement:retirement);Equal(1,retirement.Reserved);
    }),
    // Unknown free outcomes deliberately remain process-rooted until exit. Keep this last:
    // clearing production static state for the next test would hide the isolation guarantee.
    ("Unknown owned and borrowed root frees retain bounded cleanup responsibility without retry or quota reset",()=>{
        using var owned=new Fixture();using var borrowed=new Fixture(VisualTargetKind.Font);
        borrowed.Add("borrowed-binding");borrowed.Refresh();
        using var targetFixture=new Fixture(VisualTargetKind.Material);var failedTarget=targetFixture.Add();targetFixture.Refresh();
        failedTarget.BeforeRelease=()=>throw new IOException("unknown target root free");
        using var candidateFixture=new Fixture(VisualTargetKind.Material);
        for(var i=0;i<VisualResourceService.MaximumTargets;i++)candidateFixture.Add("candidate-victim-"+i);candidateFixture.Refresh();
        using var c=owned.Context();using var fontContext=borrowed.Context();
        var ownedView=owned.Service.ForPlugin(c);var fontView=borrowed.Service.ForPlugin(fontContext);
        owned.Sprite(ownedView);var independent=owned.Backend.Created.ToArray();
        owned.Sprite(ownedView);var failedSprite=owned.Backend.Created.Last();var failedTexture=owned.Backend.Created[^2];
        var releaseAttempts=0;failedSprite.BeforeRelease=()=>{releaseAttempts++;throw new IOException("unknown native root free");};
        Need(fontView.LoadBuiltinFont("Arial.ttf"));var fontReleaseAttempts=0;
        borrowed.Backend.Font.BeforeRelease=()=>{fontReleaseAttempts++;throw new IOException("unknown borrowed root free");};
        var candidate=candidateFixture.Add("candidate-with-unknown-root");candidate.DiscoveryPriority=100;
        candidate.BeforeRelease=()=>throw new IOException("unknown candidate root free");
        candidateFixture.Backend.Targets[0].OnRead=()=>candidate.Active=false;
        candidateFixture.Refresh();Equal(1,candidate.RootDisposals);Equal(0,candidate.Released);Equal(0,candidate.Reads);
        using var observerContext=owned.Context("quarantine-observer");var observerView=owned.Service.ForPlugin(observerContext);var observerAsset=owned.Sprite(observerView);var observerIntent=Need(observerView.BindOverride(new(Key:"pending-quarantine"),observerAsset));
        c.Dispose();fontContext.Dispose();targetFixture.Service.Dispose();
        owned.Service.Refresh(false);Equal(VisualBindingState.Stopped,observerIntent.LastSnapshot.State);Failure(observerIntent.LastSnapshot.Error,ErrorCode.CapabilityUnavailable,VisualCapabilityIds.Sprites,"pending-quarantine");observerContext.Dispose();
        Equal(1,releaseAttempts);Equal(1,fontReleaseAttempts);Equal(1,failedSprite.Destroyed);Equal(0,failedSprite.Released);
        Equal(0,failedTexture.Destroyed);Equal(2,owned.Budget.AssetCount);Equal(1,borrowed.Budget.AssetCount);
        Check(independent.All(x=>x.Destroyed==1&&x.Released==1));
        Check(owned.Retirement.Pending>0&&borrowed.Retirement.Pending>0);
        Check(owned.Service.CleanupPending&&borrowed.Service.CleanupPending);
        Check(owned.Retirement.HasQuarantined(VisualTargetKind.Sprite)&&borrowed.Retirement.HasQuarantined(VisualTargetKind.Font));
        var module=VisualModule.Reconcile(owned.Service,true);Check(!module.Ready&&module.Permanent);
        Equal(1,failedTarget.RootDisposals);Equal(0,failedTarget.Released);Equal(0,failedTarget.Original.Released);
        owned.Service.Dispose();borrowed.Service.Dispose();candidateFixture.Service.Dispose();
        owned.FlushCleanup();borrowed.FlushCleanup();targetFixture.FlushCleanup();candidateFixture.FlushCleanup();
        Check(borrowed.Retirement.HasQuarantined(VisualTargetKind.Font));
        Equal(1,releaseAttempts);Equal(1,fontReleaseAttempts);Equal(2,owned.Budget.AssetCount);Equal(1,borrowed.Budget.AssetCount);
        Equal(1,failedSprite.RootDisposals);Equal(1,borrowed.Backend.Font.RootDisposals);Equal(1,failedTarget.RootDisposals);Equal(1,candidate.RootDisposals);
        using var replacement=new VisualResourceService(owned.Main,new Logger(),VisualTargetKind.Sprite,new Backend(VisualTargetKind.Sprite),owned.Files,owned.Budget,new(owned.Main,new Logger()));
        Throws<InvalidOperationException>(()=>replacement.ForPlugin(new Context("replacement",owned.Main)));
    })
};
tests=tests.Take(tests.Length-1).Concat(VisualBindingChecks.Tests).Append(tests[^1]).ToArray();
var failed=0;foreach(var test in tests)try{test.Run();Console.WriteLine("PASS "+test.Name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+test.Name+"\n"+e);}Console.WriteLine($"{tests.Length-failed}/{tests.Length} visual checks passed.");return failed==0?0:1;

static TargetHandle<VisualAsset> NeedTarget(IVisualResourceService view,string alias)
{var match=view.Resolve(new(Key:alias));Check(match.Kind==TargetMatchKind.Unique);return match.Target!;}

sealed class Fixture:IDisposable
{
    public MainThreadDispatcher Main=new();public Backend Backend;public VisualFileAccess Files;public VisualResourceService Service;public VisualResourceBudget Budget=new();public VisualResourceRetirement Retirement;private readonly string _directory;private double _time;private int _fileRootRequests;
    public string FilePath=>Path.Combine(_directory,"skin.png");
    public Fixture(VisualTargetKind kind=VisualTargetKind.Sprite)
    {_directory=Path.Combine(Path.GetTempPath(),"RizToolbox.Visual.Tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(_directory);File.WriteAllBytes(Path.Combine(_directory,"skin.png"),Png(2,2));Backend=new(kind);Files=new(ReadFileRoot,ReadFileRoot);Retirement=new(Main,new Logger());Service=new(Main,new Logger(),kind,Backend,Files,Budget,Retirement);}
    private string ReadFileRoot(string owner){_fileRootRequests++;return _directory;}
    public (int Backend,int Created,int FileRoots,int Reads,int Writes,int AliveReads) AccessSnapshot()=>(Backend.Calls,Backend.Created.Count,_fileRootRequests,Backend.Targets.Sum(x=>x.Reads),Backend.Targets.Sum(x=>x.Writes),Backend.Created.Sum(x=>x.AliveReads)+Backend.Targets.Sum(x=>x.Original.AliveReads)+Backend.Font.AliveReads);
    public void VerifyNoAccess((int Backend,int Created,int FileRoots,int Reads,int Writes,int AliveReads) before,(int Assets,long Bytes,int Reservations) allocations,string batch)
    {
        var after=AccessSnapshot();Equal(before,after);Equal(allocations,(Budget.AssetCount,Budget.EstimatedBytes,Retirement.Reserved));
        Console.WriteLine($"ACCESS {batch}: backend={after.Backend-before.Backend}, created={after.Created-before.Created}, fileRoots={after.FileRoots-before.FileRoots}, targetReads={after.Reads-before.Reads}, targetWrites={after.Writes-before.Writes}, aliveReads={after.AliveReads-before.AliveReads}, budgetAssets={Budget.AssetCount-allocations.Assets}, budgetBytes={Budget.EstimatedBytes-allocations.Bytes}, reservations={Retirement.Reserved-allocations.Reservations}");
    }
    public FakeTarget Add(string id="one"){var target=new FakeTarget(id,Backend.Kind);Backend.Targets.Add(target);return target;}
    public void Refresh()=>Service.Refresh();public Context Context(string id="test.a")=>new(id,Main);
    public TargetHandle<VisualAsset> Target(IVisualResourceService view)=>view.GetTargets()[0].Target;
    public IVisualAssetLease Sprite(IVisualResourceService view){var texture=Need(view.LoadPng(new(VisualFileArea.Package,"skin.png")));var sprite=Need(view.CreateSprite(texture.Asset));Backend.Created.Last().Token=sprite.Asset.Id;return sprite;}
    public IVisualAssetLease Asset(IVisualResourceService view)=>Backend.Kind switch{VisualTargetKind.Sprite=>Sprite(view),VisualTargetKind.Font=>Need(view.LoadBuiltinFont("Arial.ttf")),_=>Need(view.CreateMaterial(new(Target(view))))};
    public void FlushCleanup(){for(var i=0;i<8;i++)Retirement.Tick(_time+=31);}
    public void Dispose(){Service.Dispose();foreach(var target in Backend.Targets){target.Alive=false;target.ThrowLiveness=false;}foreach(var asset in Backend.Created)asset.NativeDead=true;FlushCleanup();Main.Dispose();File.Delete(Path.Combine(_directory,"skin.png"));Directory.Delete(_directory);}
}
sealed class Backend:IVisualBackend
{
    public VisualTargetKind Kind;public List<FakeTarget> Targets=new();public List<FakeObject> Created=new();public FakeObject Font=new("builtin",VisualAssetKind.Font);public bool FailFont,FailTexture;public Action? AfterTexture,AfterSprite;public VisualMaterialProfile? MaterialProfile;public int? ThrowAfterYield;public Func<IEnumerable<FakeTarget>>? DiscoveryTargets;public int Calls;
    public Backend(VisualTargetKind kind)=>Kind=kind;
    public IEnumerable<VisualCandidate> Discover(int maximum){
        Calls++;var yielded=0;foreach(var x in (DiscoveryTargets?.Invoke()??Targets).OrderByDescending(x=>x.DiscoveryPriority).Take(maximum)){
            yield return new VisualCandidate(x.Id,x.Alias,new(Scene:x.Scene,Role:x.Role,ContentId:x.ContentId,ResourceId:x.ResourceId,Active:x.Active,Template:x.Template),x.Aliases,x.Read,v=>{x.Writes++;if(x.OnWrite is not null)x.OnWrite((FakeObject)v);else x.Current=(FakeObject)v;},x.IsAlive,()=>x.Active&&x.Alive&&x.Loaded,x.Retain,()=>x.ThrowLiveness?throw new IOException("liveness failed"):x.Alive,x.DiscoveryPriority);
            if(++yielded==ThrowAfterYield)throw new IOException("discovery interrupted");
        }
    }
    private FakeObject New(VisualAssetKind kind,int w=0,int h=0,long bytes=0){var value=new FakeObject(Guid.NewGuid().ToString(),kind,w,h,bytes);Created.Add(value);return value;}
    public IVisualObject CreateTexture(PngDecoder.Image image){Calls++;if(FailTexture)throw new IOException("upload failed");var result=New(VisualAssetKind.Texture2D,image.Width,image.Height,(long)image.Width*image.Height*4);AfterTexture?.Invoke();return result;}
    public IVisualObject CreateSprite(IVisualObject texture,float ppu){Calls++;var result=New(VisualAssetKind.Sprite,texture.Width,texture.Height,1024);AfterSprite?.Invoke();return result;}
    public IVisualObject LoadFont(string name){Calls++;return FailFont?throw new IOException("builtin unavailable"):Font;}
    public IVisualObject CreateMaterial(IVisualObject source,VisualMaterialRequest request){Calls++;var profiles=VisualMaterialProfiles.Candidates(request);if(profiles.Count>0&&!profiles.Contains(MaterialProfile))throw new NotSupportedException("UnsupportedShaderSchema: source shader is not the exact trusted resource shader.");var clone=New(VisualAssetKind.Material,bytes:65536);clone.Color=((FakeObject)source).Color;foreach(var color in request.Colors??Array.Empty<MaterialColorProperty>())clone.Color=color.Value;foreach(var value in request.Floats??Array.Empty<MaterialFloatProperty>())clone.Float=value.Value;return clone;}
}
sealed class FakeTarget
{
    public string Id;public string Alias=>"game.visual/"+Id;public List<string> Aliases=new();public bool Alive=true,Active=true,Loaded=true,Template;public int Retained,Released,Writes,Reads,DiscoveryPriority,RootDisposals;public string? Scene,Role,ContentId,ResourceId;
    public FakeObject Original,Current;public Action<FakeObject>? OnWrite;public Action? OnRead,BeforeRelease,OnLiveness;public bool ThrowRead,ThrowLiveness;
    public bool IsAlive(){OnLiveness?.Invoke();return Alive&&Loaded;}
    public IVisualObject Read(){Reads++;OnRead?.Invoke();return ThrowRead?throw new IOException("native read failed"):Current;}
    public FakeTarget(string id,VisualTargetKind kind){Id=id;Aliases.Add(Alias);Original=Current=new("game-"+id,kind switch{VisualTargetKind.Sprite=>VisualAssetKind.Sprite,VisualTargetKind.Font=>VisualAssetKind.Font,_=>VisualAssetKind.Material});}
    public IDisposable Retain(){Retained++;return new TestRoot(()=>RootDisposals++,()=>{BeforeRelease?.Invoke();Released++;});}
}
sealed class FakeObject:IVisualObject
{
    public string Identity{get;}public VisualAssetKind Kind{get;}public int Width{get;}public int Height{get;}public long EstimatedBytes{get;}public bool IsAlive{get{AliveReads++;return !NativeDead&&(Destroyed==0||DeferDestruction);}}public int AliveReads;
    public int Retained,Released,Destroyed,RootDisposals;public string? Token;public ResourceColor Color=new(1,1,1);public float Float;public Action? OnDestroy,BeforeDestroy,BeforeRelease;public bool DeferDestruction,NativeDead;
    public FakeObject(string id,VisualAssetKind kind,int width=0,int height=0,long bytes=0){Identity=id;Kind=kind;Width=width;Height=height;EstimatedBytes=bytes;}
    public IDisposable Retain(){Retained++;return new TestRoot(()=>RootDisposals++,()=>{BeforeRelease?.Invoke();Released++;});}public void DestroyOwned(){BeforeDestroy?.Invoke();Destroyed++;OnDestroy?.Invoke();}
}
sealed class TestRoot:IDisposable
{
    private readonly Action _called;private Action? _release;
    internal TestRoot(Action called,Action release){_called=called;_release=release;}
    public void Dispose(){_called();var release=_release;_release=null;release?.Invoke();}
}
sealed class Context:IPluginScope
{
    private readonly Stack<IDisposable> _owned=new();private readonly CancellationTokenSource _stop=new();private bool _active=true;public string PluginId{get;}public IMainThreadDispatcher MainThread{get;}
    public string ScopeId{get;}=Guid.NewGuid().ToString("N");public bool IsActive=>_active;public CancellationToken Lifetime=>_stop.Token;
    public IPluginScope CreateScope(string name,ScopeLifetime lifetime=ScopeLifetime.Manual)=>throw new NotSupportedException("Use the formal PluginHost for child scope checks.");
    public Action<IDisposable>? OnOwn;
    public Context(string id,IMainThreadDispatcher main){PluginId=id;MainThread=main;}public ILogger Log=>new Logger();public IEventBus Events=>throw new NotSupportedException();public GameBuildDescriptor GameBuild=>new("test","test","test","test");
    public CapabilityDescriptor GetCapability(string id)=>new(id,false);public ServiceLookup<T> GetService<T>(string id)where T:class=>new(false,null,new(ErrorCode.CapabilityUnavailable,"Test service is unavailable.",id));
    public T Own<T>(T value)where T:IDisposable{if(!_active)throw new ObjectDisposedException("Context");_owned.Push(value);OnOwn?.Invoke(value);return value;}
    public void Dispose(){if(!_active)return;_active=false;_stop.Cancel();while(_owned.Count>0)_owned.Pop().Dispose();}
}
sealed class Logger:ILogger{public void Log(LogLevel level,string message,Exception? exception=null){}}
sealed class RetirementLog:ILogger
{
    internal readonly List<LogLevel> Levels=new();internal Action<LogLevel>? OnLog;
    public void Log(LogLevel level,string message,Exception? exception=null){Levels.Add(level);OnLog?.Invoke(level);}
}
sealed class CaptureLog:ILogger{internal readonly List<string> Errors=new();public void Log(LogLevel level,string message,Exception? exception=null){if(level>=LogLevel.Error)Errors.Add(message+" "+exception);}}
sealed class HostProbe:IRizPlugin
{
    private readonly Action<IPluginContext> _enable;internal HostProbe(Action<IPluginContext> enable)=>_enable=enable;
    public PluginManifest Manifest{get;}=new("visual.probe",new(1,0),new(1,0));
    public void Enable(IPluginContext context)=>_enable(context);
    public void Disable(){}
}
sealed class ActionDisposable:IDisposable{private Action? _action;public ActionDisposable(Action action)=>_action=action;public void Dispose(){var a=_action;_action=null;a?.Invoke();}}
static class Assert
{
    public static void Check(bool condition){if(!condition)throw new Exception("Assertion failed.");}
    public static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Expected {expected}; actual {actual}.");}
    public static T Need<T>(OperationResult<T> result){if(!result.Success)throw new Exception(result.Error!.Message);return result.Value!;}
    public static void Failure(OperationError? error,ErrorCode code,string capability,string? target,bool retryable=false)
    {Check(error is not null);Equal(code,error!.Code);Equal(capability,error.CapabilityId);Equal(target,error.TargetId);Equal(retryable,error.Retryable);Check(!string.IsNullOrWhiteSpace(error.Message));}
    public static void RefusedVisualResults(IVisualResourceService view,ErrorCode code,VisualAsset asset,TargetHandle<VisualAsset> target)
    {
        Failure(view.Resolve(new(Key:target.Key)).Error,code,VisualCapabilityIds.Sprites,target.Key);
        Failure(view.LoadPng(new(VisualFileArea.Package,"skin.png")).Error,code,VisualCapabilityIds.Sprites,"skin.png");
        Failure(view.CreateSprite(asset).Error,code,VisualCapabilityIds.Sprites,asset.Id);
        Failure(view.LoadBuiltinFont("Arial.ttf").Error,code,VisualCapabilityIds.Sprites,"Arial.ttf");
        Failure(view.CreateMaterial(new(target)).Error,code,VisualCapabilityIds.Sprites,target.Key);
        Failure(view.Override(target,asset).Error,code,VisualCapabilityIds.Sprites,target.Key);
    }
    public static void RefusedNullVisualResults(IVisualResourceService view,ErrorCode code,string capability,VisualAsset asset,TargetHandle<VisualAsset> target)
    {
        var match=view.Resolve(null!);Equal(TargetMatchKind.Missing,match.Kind);Check(match.Target is null);Equal(0,match.Candidates.Count);Failure(match.Error,code,capability,null);
        Failure(view.LoadPng(null!).Error,code,capability,null);
        Failure(view.CreateSprite(null!).Error,code,capability,null);
        Failure(view.LoadBuiltinFont(null!).Error,code,capability,null);
        Failure(view.CreateMaterial(null!).Error,code,capability,null);
        Failure(view.CreateMaterial(new(null!)).Error,code,capability,null);
        Failure(view.Override(null!,asset).Error,code,capability,null);
        Failure(view.Override(target,null!).Error,code,capability,target.Key);
        Failure(view.Override(null!,null!).Error,code,capability,null);
    }
    public static T Throws<T>(Action action)where T:Exception{try{action();}catch(T error){return error;}throw new Exception("Expected "+typeof(T).Name);}
    public static byte[] Png(int width,int height)
    {
        using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10});var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);header[8]=8;header[9]=6;Chunk(output,"IHDR",header);
        using var packed=new MemoryStream();using(var z=new ZLibStream(packed,CompressionLevel.Fastest,true)){var row=new byte[width*4+1];for(var i=1;i<row.Length;i++)row[i]=255;for(var y=0;y<height;y++)z.Write(row);}Chunk(output,"IDAT",packed.ToArray());Chunk(output,"IEND",Array.Empty<byte>());return output.ToArray();
    }
    public static void Chunk(Stream output,string type,byte[] data){var length=new byte[4];BinaryPrimitives.WriteInt32BigEndian(length,data.Length);output.Write(length);var bytes=System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();output.Write(bytes);var crc=uint.MaxValue;foreach(var b in bytes){crc^=b;for(var i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}BinaryPrimitives.WriteUInt32BigEndian(length,~crc);output.Write(length);}
}
