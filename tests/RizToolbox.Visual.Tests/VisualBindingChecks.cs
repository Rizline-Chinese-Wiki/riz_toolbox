using RizToolbox.Adapter.Visual;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static Assert;

static class VisualBindingChecks
{
    internal static readonly (string Name,Action Run)[] Tests={
        ("N4 missing unique ambiguous and rebuilt targets reuse assets and never revive an old generation",()=>{
            using var f=new Fixture();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var native=f.Backend.Created.Last();
            var intent=Need(view.BindOverride(new(Key:"appearance"),asset,20));var first=intent.LastSnapshot;
            Equal(VisualBindingState.Waiting,first.State);Failure(first.Error,ErrorCode.TargetMissing,VisualCapabilityIds.Sprites,"appearance",true);
            var target=f.Add("address-41");target.Aliases.Add("appearance");Semantic(target);f.Refresh();
            var bound=Need(intent.GetSnapshot());Equal(VisualBindingState.Bound,bound.State);Equal(native,target.Current);Check(bound.Revision>first.Revision);Equal(new VisualBindingIdentity("Play","song-a","note","test.sprite"),bound.Identity);
            var duplicate=f.Add("second");Semantic(duplicate);duplicate.Aliases.Add("appearance");f.Refresh();
            Equal(VisualBindingState.Ambiguous,intent.LastSnapshot.State);Equal(2,intent.LastSnapshot.CandidateCount);Check(intent.LastSnapshot.Target is null);Failure(intent.LastSnapshot.Error,ErrorCode.AmbiguousTarget,VisualCapabilityIds.Sprites,"appearance");
            Equal(target.Original,target.Current);Equal(duplicate.Original,duplicate.Current);
            var writes=target.Writes;target.Alive=false;duplicate.Alive=false;f.Refresh();Equal(VisualBindingState.Waiting,intent.LastSnapshot.State);Equal(0,native.Destroyed);
            f.Backend.Targets.Clear();var rebuilt=f.Add("address-41");Semantic(rebuilt);rebuilt.Aliases.Add("appearance");f.Refresh();
            var rebound=intent.LastSnapshot;Equal(VisualBindingState.Bound,rebound.State);Check(rebound.Target!.Generation!=bound.Target!.Generation);Equal(native,rebuilt.Current);Equal(writes,target.Writes);
            Failure(view.Override(bound.Target,asset.Asset).Error,ErrorCode.TargetMissing,VisualCapabilityIds.Sprites,bound.Target.Key);Check(rebound.Revision>bound.Revision);
            Equal(2,f.Backend.Created.Count);Equal(2,f.Budget.AssetCount);intent.Dispose();Equal(rebuilt.Original,rebuilt.Current);Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Check(asset.IsValid);
        }),
        ("N4 query reads and ordinary refreshes do not discover or rebind outside the existing discovery boundary",()=>{
            using var f=new Fixture();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var before=f.Backend.Calls;
            var intent=Need(view.BindOverride(new(Key:"late"),asset));Equal(before,f.Backend.Calls);
            var target=f.Add();target.Aliases.Add("late");for(var i=0;i<6;i++){f.Service.Refresh(false);Need(intent.GetSnapshot());}
            Equal(before,f.Backend.Calls);Equal(0,target.Writes);Equal(VisualBindingState.Waiting,intent.LastSnapshot.State);
            f.Refresh();Equal(before+1,f.Backend.Calls);Equal(VisualBindingState.Bound,intent.LastSnapshot.State);
            var revision=intent.LastSnapshot.Revision;for(var i=0;i<6;i++)f.Service.Refresh(false);Equal(before+1,f.Backend.Calls);Equal(revision,intent.LastSnapshot.Revision);
            target.Alive=false;f.Service.Refresh(false);Equal(VisualBindingState.Waiting,intent.LastSnapshot.State);
            var rebuilt=f.Add("rebuilt");rebuilt.Aliases.Add("late");f.Service.Refresh(false);Equal(0,rebuilt.Writes);f.Refresh();Equal(VisualBindingState.Bound,intent.LastSnapshot.State);
        }),
        ("N4 locked scene content role and resource including null prevent broad same-name rebinding",()=>{
            foreach(var absent in new[]{false,true})
            {
                using var f=new Fixture();var original=f.Add("old");original.Aliases.Add("stable");if(!absent)Semantic(original);f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);
                var intent=Need(view.BindOverride(new(Key:"stable"),asset));var identity=intent.LastSnapshot.Identity;original.Alive=false;f.Refresh();
                foreach(var field in new[]{"Scene","ContentId","Role","ResourceId"})
                {
                    var other=f.Add("foreign-"+field);other.Aliases.Add("stable");if(!absent)Semantic(other);
                    switch(field){case "Scene":other.Scene="other-scene";break;case "ContentId":other.ContentId="other-song";break;case "Role":other.Role="other-role";break;default:other.ResourceId="other-resource";break;}
                }
                var template=f.Add("template");template.Aliases.Add("stable");template.Template=true;
                var inactive=f.Add("inactive");inactive.Aliases.Add("stable");inactive.Active=false;
                f.Refresh();Equal(VisualBindingState.Waiting,intent.LastSnapshot.State);Equal(identity,intent.LastSnapshot.Identity);Check(f.Backend.Targets.Skip(1).All(x=>x.Writes==0));
                var correct=f.Add("right");correct.Aliases.Add("stable");if(!absent)Semantic(correct);f.Refresh();Equal(VisualBindingState.Bound,intent.LastSnapshot.State);Equal(1,intent.LastSnapshot.CandidateCount);Equal(f.Backend.Created.Last(),correct.Current);
            }
        }),
        ("N4 initial ambiguity acquires no priority layer until a unique matching semantic target exists",()=>{
            using var f=new Fixture();var a=f.Add("a");var b=f.Add("b");a.Aliases.Add("shared");b.Aliases.Add("shared");f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);
            var intent=Need(view.BindOverride(new(Key:"shared"),asset));Equal(VisualBindingState.Ambiguous,intent.LastSnapshot.State);Check(intent.LastSnapshot.Identity is null);Equal(0,a.Writes+b.Writes);
            b.Aliases.Remove("shared");f.Refresh();Equal(VisualBindingState.Bound,intent.LastSnapshot.State);Equal(f.Backend.Created.Last(),a.Current);intent.Stop();Equal(a.Original,a.Current);
        }),
        ("N4 two plugins and one-shot aliases share arbitration and restore the latest external baseline",()=>{
            using var f=new Fixture();var target=f.Add();target.Aliases.Add("appearance");f.Refresh();using var a=f.Context("a");using var b=f.Context("b");var av=f.Service.ForPlugin(a);var bv=f.Service.ForPlugin(b);
            var aa=f.Sprite(av);var an=f.Backend.Created.Last();var ba=f.Sprite(bv);var bn=f.Backend.Created.Last();
            var low=Need(av.BindOverride(new(Key:target.Alias),aa,10));var high=Need(bv.BindOverride(new(Key:"appearance"),ba,20));Equal(low.LastSnapshot.Target,high.LastSnapshot.Target);Equal(bn,target.Current);
            var generation=low.LastSnapshot.Target!.Generation;target.Alive=false;f.Refresh();Equal(VisualBindingState.Waiting,low.LastSnapshot.State);Equal(VisualBindingState.Waiting,high.LastSnapshot.State);
            var rebuilt=f.Add("rebuilt");rebuilt.Aliases.Add(target.Alias);rebuilt.Aliases.Add("appearance");f.Refresh();target=rebuilt;Equal(VisualBindingState.Bound,low.LastSnapshot.State);Equal(low.LastSnapshot.Target,high.LastSnapshot.Target);Check(low.LastSnapshot.Target!.Generation!=generation);Equal(bn,target.Current);
            var external=new FakeObject("external",VisualAssetKind.Sprite);target.Current=external;f.Service.Refresh(false);Equal(bn,target.Current);
            Check(high.Stop().Success);Equal(an,target.Current);var direct=Need(bv.Override(low.LastSnapshot.Target!,ba.Asset,30));Equal(bn,target.Current);direct.Dispose();Equal(an,target.Current);
            low.Dispose();Equal(external,target.Current);Equal(VisualBindingState.Stopped,low.LastSnapshot.State);Equal(0,an.Destroyed+bn.Destroyed);aa.Dispose();ba.Dispose();Equal(1,an.Destroyed);Equal(1,bn.Destroyed);
        }),
        ("N4 all three visual resource kinds bind and rebuild through the production service",()=>{
            foreach(var kind in Enum.GetValues<VisualTargetKind>())
            {
                using var f=new Fixture(kind);var target=f.Add();target.Aliases.Add("skin");f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Asset(view);
                var intent=Need(view.BindOverride(new(Key:"skin"),asset));var previous=intent.LastSnapshot.Target;var native=target.Current;target.Alive=false;f.Refresh();
                var rebuilt=f.Add("new");rebuilt.Aliases.Add("skin");f.Refresh();Equal(VisualBindingState.Bound,intent.LastSnapshot.State);Check(intent.LastSnapshot.Target!.Generation!=previous!.Generation);Equal(native,rebuilt.Current);
                intent.Dispose();Equal(rebuilt.Original,rebuilt.Current);Check(asset.IsValid);
            }
        }),
        ("N4 exact child scope ownership rejects parent sibling foreign and wrapped leases without getters",()=>{
            using var f=new Fixture();f.Add();f.Refresh();var log=new CaptureLog();var caps=new CapabilityRegistry(new("test","test","test","test"));var events=new EventHub(log);
            caps.RegisterScoped<IVisualResourceService>(VisualCapabilityIds.Sprites,f.Service.ForPlugin);using var host=new PluginHost(caps,f.Main,events,log);IScopedPluginContext parent=null!;host.Register(new HostProbe(c=>parent=(IScopedPluginContext)c));Check(host.Enable("visual.probe").Success);
            var pv=parent.GetService<IVisualResourceService>(VisualCapabilityIds.Sprites).Service!;var pa=f.Sprite(pv);var child=parent.CreateScope("child");var sibling=parent.CreateScope("sibling");var cv=child.GetService<IVisualResourceService>(VisualCapabilityIds.Sprites).Service!;var sv=sibling.GetService<IVisualResourceService>(VisualCapabilityIds.Sprites).Service!;var ca=f.Sprite(cv);
            var query=new TargetQuery(Key:"pending");var before=f.AccessSnapshot();
            Failure(cv.BindOverride(query,pa).Error,ErrorCode.InvalidArgument,VisualCapabilityIds.Sprites,"pending");Failure(sv.BindOverride(query,ca).Error,ErrorCode.InvalidArgument,VisualCapabilityIds.Sprites,"pending");
            var wrapper=new ProbeVisualLease(ca);Failure(cv.BindOverride(query,wrapper).Error,ErrorCode.InvalidArgument,VisualCapabilityIds.Sprites,"pending");Equal(0,wrapper.GetterCalls);Equal(before,f.AccessSnapshot());
            var directSame=f.Service.ForPlugin(child);var intent=Need(directSame.BindOverride(query,ca));var parentIntent=Need(pv.BindOverride(query,pa));child.Dispose();Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Failure(intent.LastSnapshot.Error,ErrorCode.ScopeExpired,VisualCapabilityIds.Sprites,"pending");Equal(VisualBindingState.Waiting,parentIntent.LastSnapshot.State);Check(pa.IsValid);
            var target=f.Add("pending");target.Aliases.Add("pending");f.Refresh();Equal(VisualBindingState.Bound,parentIntent.LastSnapshot.State);Check(host.Disable("visual.probe").Success);Equal(VisualBindingState.Stopped,parentIntent.LastSnapshot.State);Equal(0,log.Errors.Count);
        }),
        ("N4 invalid queries leases and worker calls fail before backend file target and resource access",()=>{
            using var f=new Fixture();f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var query=new TargetQuery(Key:"valid");var probe=new ProbeVisualLease(asset);var before=f.AccessSnapshot();var allocations=(f.Budget.AssetCount,f.Budget.EstimatedBytes,f.Retirement.Reserved);
            foreach(var invalid in new TargetQuery[]{null!,new(),new(Scene:"Play"),new(Key:""),new(Key:"bad\n"),new(Key:"valid",IncludeTemplates:true),new(Key:"valid",ActiveOnly:false)})Failure(view.BindOverride(invalid,probe).Error,ErrorCode.InvalidArgument,VisualCapabilityIds.Sprites,invalid?.Key);
            Failure(view.BindOverride(query,null!).Error,ErrorCode.InvalidArgument,VisualCapabilityIds.Sprites,"valid");Failure(view.BindOverride(query,probe).Error,ErrorCode.InvalidArgument,VisualCapabilityIds.Sprites,"valid");
            Task.Run(()=>Failure(view.BindOverride(query,probe).Error,ErrorCode.WrongThread,VisualCapabilityIds.Sprites,"valid")).GetAwaiter().GetResult();Equal(0,probe.GetterCalls);f.VerifyNoAccess(before,allocations,"N4 invalid binding batch");
            using(var foreign=new Fixture())
            {
                var foreignView=foreign.Service.ForPlugin(c);var foreignAsset=foreign.Sprite(foreignView);var foreignBefore=foreign.AccessSnapshot();before=f.AccessSnapshot();
                Failure(view.BindOverride(query,foreignAsset).Error,ErrorCode.InvalidArgument,VisualCapabilityIds.Sprites,"valid");Equal(before,f.AccessSnapshot());Equal(foreignBefore,foreign.AccessSnapshot());
            }
            var intent=Need(view.BindOverride(query,asset));before=f.AccessSnapshot();Task.Run(()=>{Failure(intent.GetSnapshot().Error,ErrorCode.WrongThread,VisualCapabilityIds.Sprites,"valid");Failure(intent.Stop().Error,ErrorCode.WrongThread,VisualCapabilityIds.Sprites,"valid");Equal(VisualBindingState.Waiting,intent.LastSnapshot.State);}).GetAwaiter().GetResult();Equal(before,f.AccessSnapshot());
            c.Dispose();before=f.AccessSnapshot();Failure(view.BindOverride(query,probe).Error,ErrorCode.ScopeExpired,VisualCapabilityIds.Sprites,"valid");Failure(intent.GetSnapshot().Error,ErrorCode.ScopeExpired,VisualCapabilityIds.Sprites,"valid");Failure(intent.Stop().Error,ErrorCode.ScopeExpired,VisualCapabilityIds.Sprites,"valid");Equal(before,f.AccessSnapshot());
        }),
        ("N4 scope withdrawal inside Own Resolve and Override leaves no intent layer or owned object",()=>{
            foreach(var boundary in new[]{"Own","Resolve","Override"})
            {
                using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);IVisualBindingIntent? observed=null;
                c.OnOwn=value=>{if(value is IVisualBindingIntent intent){observed=intent;if(boundary=="Own")c.Dispose();}};
                if(boundary=="Resolve")target.OnLiveness=()=>{target.OnLiveness=null;c.Dispose();};
                if(boundary=="Override")target.OnWrite=value=>{target.Current=value;target.OnWrite=null;c.Dispose();};
                var result=view.BindOverride(new(Key:target.Alias),asset);Check(!result.Success);Failure(result.Error,ErrorCode.ScopeExpired,VisualCapabilityIds.Sprites,target.Alias);Check(observed is not null);Equal(VisualBindingState.Stopped,observed!.LastSnapshot.State);Equal(0,f.Service.BindingIntentCount);Equal(target.Original,target.Current);Equal(0,f.Budget.AssetCount);Check(f.Backend.Created.All(x=>x.Destroyed==1&&x.Released==1));
            }
        }),
        ("N4 a failed new-generation layer preserves the new baseline and retries only at a later discovery boundary",()=>{
            using var f=new Fixture();var old=f.Add("old");old.Aliases.Add("key");f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var intent=Need(view.BindOverride(new(Key:"key"),asset));old.Alive=false;f.Refresh();
            var next=f.Add("next");next.Aliases.Add("key");next.OnWrite=_=>throw new IOException("new setter refused");f.Refresh();Equal(VisualBindingState.Waiting,intent.LastSnapshot.State);Check(intent.LastSnapshot.Target is null);Failure(intent.LastSnapshot.Error,ErrorCode.MutationWriteFailed,TargetCapabilityIds.Mutations,f.Target(view).Key);Equal(next.Original,next.Current);
            var attempts=next.Writes;next.OnWrite=null;f.Service.Refresh(false);Equal(attempts,next.Writes);Equal(VisualBindingState.Waiting,intent.LastSnapshot.State);f.Refresh();Equal(VisualBindingState.Bound,intent.LastSnapshot.State);Equal(f.Backend.Created.Last(),next.Current);
        }),
        ("N4 scope asset and provider withdrawal during a renewed setter safely release its unpublished layer once",()=>{
            foreach(var boundary in new[]{"scope","asset","provider"})
            {
                using var f=new Fixture();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var intent=Need(view.BindOverride(new(Key:"rebuilt"),asset));var target=f.Add();target.Aliases.Add("rebuilt");
                target.OnWrite=value=>{target.Current=value;target.OnWrite=null;if(boundary=="scope")c.Dispose();else if(boundary=="asset")asset.Dispose();else f.Service.Dispose();};
                f.Refresh();Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Equal(boundary=="asset"?ErrorCode.TargetExpired:ErrorCode.ScopeExpired,intent.LastSnapshot.Error!.Code);Equal(0,f.Service.BindingIntentCount);
                if(boundary=="provider")
                {
                    // The provider ended inside its native write. Its binding retirement
                    // cannot restore/destroy while that write is still on the stack.
                    Check(f.Service.CleanupPending);Check(f.Backend.Created.All(x=>x.Destroyed==0));f.FlushCleanup();Check(!f.Service.CleanupPending);
                }
                Equal(target.Original,target.Current);
                if(boundary!="asset")Check(f.Backend.Created.All(x=>x.Destroyed==1&&x.Released==1));else{asset.Dispose();Check(f.Backend.Created.All(x=>x.Destroyed<=1&&x.Released<=1));}
                c.Dispose();f.FlushCleanup();Check(f.Backend.Created.All(x=>x.Destroyed==1&&x.Released==1));Equal(0,f.Budget.AssetCount);
            }
        }),
        ("N4 an uncertain old-layer release permanently stops instead of acquiring a replacement object",()=>{
            using var f=new Fixture();var old=f.Add("old");old.Aliases.Add("key");f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var intent=Need(view.BindOverride(new(Key:"key"),asset));var native=old.Current;
            old.OnWrite=_=>throw new IOException("cannot restore old baseline");old.Aliases.Remove("key");var next=f.Add("next");next.Aliases.Add("key");f.Refresh();
            Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Failure(intent.LastSnapshot.Error,ErrorCode.CapabilityUnavailable,VisualCapabilityIds.Sprites,"key");Equal(native,old.Current);Equal(0,next.Writes);Check(f.Service.CleanupPending);Equal(0,native.Destroyed);
            old.OnWrite=null;old.Alive=false;f.FlushCleanup();f.Refresh();Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Equal(0,next.Writes);Equal(0,f.Service.BindingIntentCount);
        }),
        ("N4 stopping a waiting intent preserves caller assets while asset disposal stops pending and bound intents once",()=>{
            foreach(var bound in new[]{false,true})
            {
                using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var native=f.Backend.Created.Last();
                var query=new TargetQuery(Key:bound?target.Alias:"later");var stopped=Need(view.BindOverride(query,asset));Check(stopped.Stop().Success);Check(stopped.Stop().Success);Equal(VisualBindingState.Stopped,stopped.LastSnapshot.State);Check(asset.IsValid);Equal(0,native.Destroyed);
                var intent=Need(view.BindOverride(query,asset));asset.Dispose();asset.Dispose();Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Equal(ErrorCode.TargetExpired,intent.LastSnapshot.Error!.Code);Equal(target.Original,target.Current);Equal(1,native.Destroyed);Equal(1,native.Released);Equal(0,f.Service.BindingIntentCount);
                f.Refresh();Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Failure(view.BindOverride(query,asset).Error,ErrorCode.TargetExpired,VisualCapabilityIds.Sprites,query.Key);
            }
        }),
        ("N4 native asset expiration and provider withdrawal are terminal across later object discovery",()=>{
            foreach(var provider in new[]{false,true})
            {
                using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var native=f.Backend.Created.Last();var intent=Need(view.BindOverride(new(Key:"later"),asset));
                if(provider)f.Service.Dispose();else{native.NativeDead=true;f.Service.Refresh(false);}
                Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Equal(provider?ErrorCode.ScopeExpired:ErrorCode.TargetExpired,intent.LastSnapshot.Error!.Code);var before=f.AccessSnapshot();Task.Run(()=>{_ = intent.LastSnapshot;}).GetAwaiter().GetResult();Equal(before,f.AccessSnapshot());
                if(!provider){target.Aliases.Add("later");f.Refresh();Equal(0,target.Writes);}
            }
        }),
        ("N4 cleanup pending stops a bound intent while retaining uncertain assignments and budgets",()=>{
            using var f=new Fixture();var target=f.Add();f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var intent=Need(view.BindOverride(new(Key:target.Alias),asset));var native=target.Current;
            target.Loaded=false;target.ThrowRead=true;f.Refresh();Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Equal(ErrorCode.CapabilityUnavailable,intent.LastSnapshot.Error!.Code);Check(!intent.LastSnapshot.Error.Retryable);Equal(0,native.Destroyed);Check(f.Budget.AssetCount>0);
            target.ThrowRead=false;target.Alive=false;f.FlushCleanup();f.Refresh();Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Equal(0,f.Service.BindingIntentCount);
        }),
        ("N4 verified empty providers stay available and backend discovery failure is never reported ready",()=>{
            using var f=new Fixture();Check(VisualModule.Reconcile(f.Service,true).Ready);using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var intent=Need(view.BindOverride(new(Key:"first"),asset));Equal(VisualBindingState.Waiting,intent.LastSnapshot.State);
            var target=f.Add("first");target.Aliases.Add("first");f.Backend.ThrowAfterYield=1;Throws<IOException>(()=>VisualModule.Reconcile(f.Service,true));Equal(VisualBindingState.Waiting,intent.LastSnapshot.State);Equal(0,target.Writes);
            f.Backend.ThrowAfterYield=null;Check(VisualModule.Reconcile(f.Service,true).Ready);Equal(VisualBindingState.Bound,intent.LastSnapshot.State);
        }),
        ("N4 formal capability withdrawal and provider release stop an optional plugin intent without disabling its scope",()=>{
            using var f=new Fixture();var target=f.Add();f.Refresh();var log=new CaptureLog();var caps=new CapabilityRegistry(new("test","test","test","test"));var events=new EventHub(log);caps.RegisterScoped<IVisualResourceService>(VisualCapabilityIds.Sprites,f.Service.ForPlugin);
            using var host=new PluginHost(caps,f.Main,events,log);IScopedPluginContext context=null!;host.Register(new HostProbe(c=>context=(IScopedPluginContext)c));Check(host.Enable("visual.probe").Success);
            var view=context.GetService<IVisualResourceService>(VisualCapabilityIds.Sprites).Service!;var asset=f.Sprite(view);var intent=Need(view.BindOverride(new(Key:target.Alias),asset));
            caps.SetUnavailable(VisualCapabilityIds.Sprites,"Visual module withdrawn.");f.Service.Dispose();Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Equal(ErrorCode.ScopeExpired,intent.LastSnapshot.Error!.Code);Equal(target.Original,target.Current);Equal(PluginState.Enabled,host.Statuses.Single().State);
            var before=f.AccessSnapshot();Failure(view.BindOverride(new(Key:target.Alias),asset).Error,ErrorCode.ScopeExpired,VisualCapabilityIds.Sprites,target.Alias);Failure(intent.GetSnapshot().Error,ErrorCode.ScopeExpired,VisualCapabilityIds.Sprites,target.Alias);Equal(before,f.AccessSnapshot());Equal(0,log.Errors.Count);
        }),
        ("N4 intent quotas are bounded independently of asset budgets and stopping returns the intent slot",()=>{
            using var f=new Fixture();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var intents=new List<IVisualBindingIntent>();for(var i=0;i<VisualResourceService.MaximumPluginBindingIntents;i++)intents.Add(Need(view.BindOverride(new(Key:"late-"+i),asset)));
            Equal(VisualResourceService.MaximumPluginBindingIntents,f.Service.BindingIntentCount);var before=f.AccessSnapshot();Failure(view.BindOverride(new(Key:"overflow"),asset).Error,ErrorCode.QuotaExceeded,VisualCapabilityIds.Sprites,"overflow");Equal(before,f.AccessSnapshot());Equal(2,f.Budget.AssetCount);
            intents[0].Dispose();Need(view.BindOverride(new(Key:"replacement"),asset));Equal(VisualResourceService.MaximumPluginBindingIntents,f.Service.BindingIntentCount);c.Dispose();Equal(0,f.Service.BindingIntentCount);Equal(0,f.Budget.AssetCount);
        }),
        ("N4 content role and resource selectors bind without an instance-specific key",()=>{
            using var f=new Fixture();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var query=new TargetQuery(Scene:"Play",ContentId:"song-a",Role:"note",ResourceId:"test.sprite");var intent=Need(view.BindOverride(query,asset));
            var wrong=f.Add("other-song");Semantic(wrong);wrong.ContentId="song-b";f.Refresh();Equal(VisualBindingState.Waiting,intent.LastSnapshot.State);Equal(0,wrong.Writes);
            var first=f.Add("instance-1");Semantic(first);f.Refresh();Equal(VisualBindingState.Bound,intent.LastSnapshot.State);var generation=intent.LastSnapshot.Target!.Generation;
            first.Alive=false;f.Refresh();var next=f.Add("instance-2");Semantic(next);f.Refresh();Equal(VisualBindingState.Bound,intent.LastSnapshot.State);Check(intent.LastSnapshot.Target!.Generation!=generation);Equal(f.Backend.Created.Last(),next.Current);Equal(0,wrong.Writes);
        }),
        ("N4 provider intent capacity stays bounded across independent plugin scopes",()=>{
            using var f=new Fixture();var contexts=new List<Context>();IVisualBindingIntent? first=null;
            try
            {
                for(var owner=0;owner<4;owner++)
                {
                    var context=f.Context("owner-"+owner);contexts.Add(context);var view=f.Service.ForPlugin(context);var asset=f.Sprite(view);
                    for(var i=0;i<VisualResourceService.MaximumPluginBindingIntents;i++){var intent=Need(view.BindOverride(new(Key:"pending-"+i),asset));first??=intent;}
                }
                Equal(VisualResourceService.MaximumBindingIntents,f.Service.BindingIntentCount);using var extra=f.Context("overflow-owner");var extraView=f.Service.ForPlugin(extra);var extraAsset=f.Sprite(extraView);var before=f.AccessSnapshot();
                Failure(extraView.BindOverride(new(Key:"overflow"),extraAsset).Error,ErrorCode.QuotaExceeded,VisualCapabilityIds.Sprites,"overflow");Equal(before,f.AccessSnapshot());first!.Stop();Need(extraView.BindOverride(new(Key:"returned-slot"),extraAsset));Equal(VisualResourceService.MaximumBindingIntents,f.Service.BindingIntentCount);
            }
            finally{foreach(var context in contexts)context.Dispose();}
            Equal(0,f.Service.BindingIntentCount);Equal(0,f.Budget.AssetCount);Check(f.Backend.Created.All(x=>x.Destroyed==1&&x.Released==1));
        }),
        ("N4 ending one input lease stops all its bound and waiting intents and releases the resource once",()=>{
            using var f=new Fixture();var target=f.Add();target.Aliases.Add("same");f.Refresh();using var c=f.Context();var view=f.Service.ForPlugin(c);var asset=f.Sprite(view);var native=f.Backend.Created.Last();
            var first=Need(view.BindOverride(new(Key:target.Alias),asset,10));var second=Need(view.BindOverride(new(Key:"same"),asset,20));var waiting=Need(view.BindOverride(new(Key:"later"),asset));Equal(3,f.Service.BindingIntentCount);Equal(native,target.Current);
            asset.Dispose();asset.Dispose();foreach(var intent in new[]{first,second,waiting}){Equal(VisualBindingState.Stopped,intent.LastSnapshot.State);Equal(ErrorCode.TargetExpired,intent.LastSnapshot.Error!.Code);intent.Dispose();}
            Equal(target.Original,target.Current);Equal(0,f.Service.BindingIntentCount);Equal(1,native.Destroyed);Equal(1,native.Released);c.Dispose();Check(f.Backend.Created.All(x=>x.Destroyed==1&&x.Released==1));Equal(0,f.Budget.AssetCount);
        })
    };

    private static void Semantic(FakeTarget target){target.Scene="Play";target.ContentId="song-a";target.Role="note";target.ResourceId="test.sprite";}
}

sealed class ProbeVisualLease:IVisualAssetLease
{
    private readonly IVisualAssetLease _inner;internal int GetterCalls;
    internal ProbeVisualLease(IVisualAssetLease inner)=>_inner=inner;
    public VisualAsset Asset {get{GetterCalls++;return _inner.Asset;}}
    public int Width {get{GetterCalls++;return _inner.Width;}}
    public int Height {get{GetterCalls++;return _inner.Height;}}
    public long EstimatedBytes {get{GetterCalls++;return _inner.EstimatedBytes;}}
    public bool IsValid {get{GetterCalls++;return _inner.IsValid;}}
    public void Dispose()=>_inner.Dispose();
}
