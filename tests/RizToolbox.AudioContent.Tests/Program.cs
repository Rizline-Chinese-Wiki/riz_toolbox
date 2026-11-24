using System.Buffers.Binary;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Sdk;
using RizToolbox.Runtime;
using static Assert;

if (args.Length > 0 && args[0] == "--b11-local-first-joint")
    return LocalCatalogRegression.LocalFirstSharedPlay(args.Skip(1).ToArray());

if (args.Length > 0 && args[0] == "--b11-official-first-joint")
    return LocalCatalogRegression.OfficialFirstSharedPlay(args.Skip(1).ToArray());

if (args.Length == 1 && args[0] == "--local-frame-count-metadata-checks")
    return NativeLocalFrameCountMetadataChecks.Run();

if (args.Length == 1 && args[0] == "--local-admission-diagnostic-checks")
    return LocalAdmissionDiagnosticsChecks.Run();

if (args.Length > 0 && args[0] == "--single-play-joined")
    return NativeTweenTypedPlayNativeReaderTests.RunJoined(args);

if (args.Length > 0 && args[0] == NativeEngineInlineManagedIntegration.ModeArgument)
    return NativeEngineInlineManagedIntegration.RunChild(args);
if (args.Length > 0 && args[0] == NativeEngineSchedulerManagedIntegration.ModeArgument)
    return NativeEngineSchedulerManagedIntegration.RunChild(args);
if (args.Length > 0 && args[0] == NativeEngineWaitManagedIntegration.ModeArgument)
    return NativeEngineWaitManagedIntegration.RunChild(args);
if (args.Length > 0 && args[0] == NativeEngineCancellationManagedIntegration.ModeArgument)
    return NativeEngineCancellationManagedIntegration.RunChild(args);

var tests=new (string Name,Action Test)[]{
    ("PCM16 decoder validates exact RIFF/chunks, geometry and signed samples",()=>{
        var bytes=Wav();var audio=WavDecoder.Decode(bytes);Equal(4,audio.SampleFrames);Equal(1,audio.Channels);Equal(8000,audio.Frequency);Equal(-1f,audio.Samples[0]);Equal(32767/32768f,audio.Samples[3]);
        var bad=bytes.ToArray();bad[4]--;Throws<InvalidDataException>(()=>WavDecoder.Decode(bad));bad=bytes.ToArray();bad[22]=3;Throws<InvalidDataException>(()=>WavDecoder.Decode(bad));bad=bytes.ToArray();bad[34]=32;Throws<InvalidDataException>(()=>WavDecoder.Decode(bad));bad=bytes.ToArray();bad[40]=255;Throws<InvalidDataException>(()=>WavDecoder.Decode(bad));
    }),
    ("Decoder cancellation prevents any native upload",()=>{using var f=new Fixture();using var c=f.Context();using var cancel=new CancellationTokenSource();cancel.Cancel();Equal(ErrorCode.Cancelled,f.Play(c).LoadWav(f.File,cancel.Token).Error!.Code);Equal(0,f.Backend.Created.Count);}),
    ("Owned playback pins a disposed clip until Stop and then releases all roots",()=>{
        using var f=new Fixture();using var c=f.Context();var view=f.Play(c);var asset=Need(view.LoadWav(f.File));var clip=f.Backend.Created.Single();var playing=Need(view.Play(asset.Asset));asset.Dispose();Equal(0,clip.Destroyed);Check(playing.IsActive);Need(playing.Stop());Equal(1,clip.Destroyed);Equal(clip.Retained,clip.Released);playing.Dispose();Equal(1,clip.Destroyed);
    }),
    ("Natural playback completion disposes source and permits clip collection",()=>{
        using var f=new Fixture();using var c=f.Context();var view=f.Play(c);var asset=Need(view.LoadWav(f.File));var source=Need(view.Play(asset.Asset));asset.Dispose();f.Backend.Sources.Single().Playing=false;f.Audio.Refresh(false);Check(!source.IsActive);Equal(1,f.Backend.Created.Single().Destroyed);Equal(1,f.Backend.Sources.Single().Disposed);
    }),
    ("A failed native Stop keeps a live voice and clip rooted until retry succeeds",()=>{
        using var f=new Fixture();using var c=f.Context();var view=f.Play(c);var asset=Need(view.LoadWav(f.File));var playback=Need(view.Play(asset.Asset));asset.Dispose();f.Backend.Sources[0].FailStop=true;Check(!playback.Stop().Success);Equal(0,f.Backend.Created[0].Destroyed);f.Backend.Sources[0].FailStop=false;Need(playback.Stop());Equal(1,f.Backend.Created[0].Destroyed);
    }),
    ("Game source replacement restores original and never destroys borrowed clips",()=>{
        using var f=new Fixture();var target=f.Add();f.Audio.Refresh();using var c=f.Context();var a=Need(f.Play(c).LoadWav(f.File));var replacement=Need(f.Replace(c).Replace(f.Target(c),a.Asset));Equal(f.Backend.Created[0],target.Current);replacement.Dispose();Equal(target.Original,target.Current);a.Dispose();Equal(1,f.Backend.Created[0].Destroyed);Equal(0,target.Original.Destroyed);
    }),
    ("Replacement lease errors preserve target context and reject worker or expired calls without changing the clip",()=>{
        using var f=new Fixture();var target=f.Add();f.Audio.Refresh();var c=f.Context();var clip=Need(f.Play(c).LoadWav(f.File));
        var lease=Need(f.Replace(c).Replace(f.Target(c),clip.Asset));var key=lease.Key.Target.Key;
        var unsupported=lease.SetValue(clip.Asset);Check(!unsupported.Success);Equal(ErrorCode.OperationUnsupported,unsupported.Error!.Code);
        Equal(AudioContentCapabilityIds.Replacement,unsupported.Error.CapabilityId);Equal(key,unsupported.Error.TargetId);
        var worker=Task.Run(()=>lease.SetValue(clip.Asset)).GetAwaiter().GetResult();Equal(ErrorCode.WrongThread,worker.Error!.Code);Equal(key,worker.Error.TargetId);
        Equal(f.Backend.Created[0],target.Current);c.Dispose();var expired=lease.SetValue(clip.Asset);
        Equal(ErrorCode.ScopeExpired,expired.Error!.Code);Equal(key,expired.Error.TargetId);Equal(target.Original,target.Current);
    }),
    ("Retired game playback holds the replacement after provider scope withdrawal",()=>{
        using var f=new Fixture();var target=f.Add();target.Playing=true;f.Audio.Refresh();var c=f.Context();var asset=Need(f.Play(c).LoadWav(f.File));Need(f.Replace(c).Replace(f.Target(c),asset.Asset));var native=f.Backend.Created[0];c.Dispose();Equal(target.Original,target.Current);Equal(0,native.Destroyed);Check(target.Original.Retained>target.Original.Released);target.Playing=false;f.Audio.Refresh(false);Equal(1,native.Destroyed);
    }),
    ("Three plugin priority layers unwind and equal priority uses latest registration",()=>{
        using var f=new Fixture();var target=f.Add();f.Audio.Refresh();using var a=f.Context("a");using var b=f.Context("b");using var c=f.Context("c");
        var aa=Need(f.Play(a).LoadWav(f.File));var ba=Need(f.Play(b).LoadWav(f.File));var ca=Need(f.Play(c).LoadWav(f.File));Need(f.Replace(a).Replace(f.Target(a),aa.Asset,1));Need(f.Replace(b).Replace(f.Target(b),ba.Asset,1));Need(f.Replace(c).Replace(f.Target(c),ca.Asset,2));Equal(f.Backend.Created[2],target.Current);c.Dispose();Equal(f.Backend.Created[1],target.Current);b.Dispose();Equal(f.Backend.Created[0],target.Current);a.Dispose();Equal(target.Original,target.Current);
    }),
    ("Game clip writes become the eventual baseline and adopted owned clips stay pinned",()=>{
        using var f=new Fixture();var target=f.Add();f.Audio.Refresh();using var a=f.Context("a");using var b=f.Context("b");var aa=Need(f.Play(a).LoadWav(f.File));var ba=Need(f.Play(b).LoadWav(f.File));var lease=Need(f.Replace(b).Replace(f.Target(b),ba.Asset));target.Current=f.Backend.Created[0];f.Audio.Refresh(false);a.Dispose();Equal(0,f.Backend.Created[0].Destroyed);lease.Dispose();Equal(f.Backend.Created[0],target.Current);target.Active=false;f.Audio.Refresh(false);Equal(1,f.Backend.Created[0].Destroyed);
    }),
    ("Setter failure after mutation rolls back and rejects ownership",()=>{
        using var f=new Fixture();var target=f.Add();f.Audio.Refresh();using var c=f.Context();var a=Need(f.Play(c).LoadWav(f.File));var native=f.Backend.Created[0];target.OnWrite=x=>{target.Current=x;if(x==native)throw new IOException("setter failed");};Check(!f.Replace(c).Replace(f.Target(c),a.Asset).Success);Equal(target.Original,target.Current);a.Dispose();Equal(1,native.Destroyed);
    }),
    ("Wrong owner, dead target generation and malformed WAV never change game source",()=>{
        using var f=new Fixture();var target=f.Add();f.Audio.Refresh();using var a=f.Context("a");using var b=f.Context("b");var asset=Need(f.Play(a).LoadWav(f.File));Check(!f.Replace(b).Replace(f.Target(b),asset.Asset).Success);var old=f.Target(a);target.Alive=false;f.Audio.Refresh(false);Equal(ErrorCode.TargetExpired,f.Replace(a).Replace(old,asset.Asset).Error!.Code);Equal(0,target.Writes);File.WriteAllBytes(Path.Combine(f.Directory,"bad.wav"),new byte[]{1,2});Equal(ErrorCode.ContentInvalid,f.Play(a).LoadWav(new(ContentFileArea.Package,"bad.wav")).Error!.Code);
    }),
    ("Scope expiration and wrong thread return structured errors; capability views cannot be cast",()=>{
        using var f=new Fixture();var c=f.Context();var view=f.Play(c);Check(view is not IAudioReplacementService);c.Dispose();Equal(ErrorCode.ScopeExpired,view.LoadWav(f.File).Error!.Code);using var other=f.Context();var fresh=f.Play(other);Equal(ErrorCode.WrongThread,Task.Run(()=>fresh.LoadWav(f.File).Error!.Code).Result);
    }),
    ("Capability withdrawal invalidates old views even when other audio capabilities remain",()=>{
        using var f=new Fixture();using var c=f.Context();var old=f.Play(c);f.Audio.Disable(AudioContentCapabilityIds.Playback);f.Audio.Enable(AudioContentCapabilityIds.Playback);Equal(ErrorCode.ScopeExpired,old.LoadWav(f.File).Error!.Code);Need(f.Play(c).LoadWav(f.File));
    }),
    ("Sibling scopes with the same plugin ID cannot spend another scope's audio lease",()=>{
        using var f=new Fixture();using var a=f.Context("same.plugin");using var b=f.Context("same.plugin");var asset=Need(f.Play(a).LoadWav(f.File));Equal(ErrorCode.TargetExpired,f.Play(b).Play(asset.Asset).Error!.Code);Need(f.Play(a).Play(asset.Asset));
    }),
    ("Reentrant source mutation is rejected and callback scope disable rolls back safely",()=>{
        using var f=new Fixture();var target=f.Add();f.Audio.Refresh();var c=f.Context();var a=Need(f.Play(c).LoadWav(f.File));var view=f.Replace(c);var handle=f.Target(c);Result<IMutationLease<AudioAsset>>? nested=null;target.OnWrite=x=>{target.Current=x;nested??=view.Replace(handle,a.Asset);c.Dispose();};Check(!view.Replace(handle,a.Asset).Success);Equal(ErrorCode.Conflict,nested!.Error!.Code);Equal(target.Original,target.Current);Equal(1,f.Backend.Created[0].Destroyed);
    }),
    ("Volume and pitch validate independently and restore adopted game values",()=>{
        using var f=new Fixture();var target=f.Add();f.Audio.Refresh();using var c=f.Context();var mix=f.Mix(c);var handle=f.Target(c);Equal(ErrorCode.InvalidArgument,mix.Override(handle,AudioMixParameter.Volume,float.NaN).Error!.Code);Equal(ErrorCode.InvalidArgument,mix.Override(handle,AudioMixParameter.Pitch,0).Error!.Code);var lease=Need(mix.Override(handle,AudioMixParameter.Volume,.3f));Equal(.3f,target.Volume);target.Volume=.7f;f.Audio.Refresh(false);Equal(.3f,target.Volume);lease.Dispose();Equal(.7f,target.Volume);var pitch=Need(mix.Override(handle,AudioMixParameter.Pitch,2));Equal(2f,target.Pitch);pitch.Dispose();Equal(1f,target.Pitch);
    }),
    ("DSP clock reports finite seconds and rejects invalid readings",()=>{using var f=new Fixture();using var c=f.Context();var clock=f.Clock(c);Equal(1.25,Need(clock.Read()).DspSeconds);f.Backend.Dsp=double.NaN;Check(!clock.Read().Success);}),
    ("File roots reject traversal, absolute paths and reparse-point escape",()=>{
        using var f=new Fixture();using var c=f.Context();var view=f.Play(c);foreach(var path in new[]{"../tone.wav","a/../tone.wav","C:\\tone.wav","x:evil.wav","a//tone.wav"})Equal(ErrorCode.ContentInvalid,view.LoadWav(new(ContentFileArea.Package,path)).Error!.Code);
    }),
    ("Asset and source budgets release reservation capacity after disposal",()=>{
        var budget=new AudioBudget();var leases=new List<IDisposable>();for(var i=0;i<AudioBudget.MaximumOwnerAssets;i++)leases.Add(budget.Reserve("one",1));Throws<AudioQuotaException>(()=>budget.Reserve("one",1));foreach(var lease in leases)lease.Dispose();using var full=budget.Reserve("one",AudioBudget.MaximumOwnerBytes);Throws<AudioQuotaException>(()=>budget.Reserve("one",1));
        using var f=new Fixture();using var c=f.Context();var v=f.Play(c);var assets=new List<IAudioAssetLease>();for(var i=0;i<16;i++)assets.Add(Need(v.LoadWav(f.File)));Equal(ErrorCode.QuotaExceeded,v.LoadWav(f.File).Error!.Code);assets[0].Dispose();Need(v.LoadWav(f.File));
    }),
    ("Cancellation during native upload destroys pending clip and preserves budget",()=>{
        using var f=new Fixture();using var c=f.Context();using var cancel=new CancellationTokenSource();f.Backend.OnCreate=()=>cancel.Cancel();var v=f.Play(c);Equal(ErrorCode.Cancelled,v.LoadWav(f.File,cancel.Token).Error!.Code);Equal(1,f.Backend.Created[0].Destroyed);f.Backend.OnCreate=null;Need(v.LoadWav(f.File));
    }),
    ("Local IDs are provider namespaced and duplicate IDs conflict",()=>{
        using var f=new Fixture();using var a=f.Context("a");using var b=f.Context("b");var av=f.Content.ForPlugin(a);var bv=f.Content.ForPlugin(b);var request=f.Request("same");var ar=Need(av.Register(request));Need(bv.Register(request));Equal(2,Need(av.GetPublished()).Count);Equal(ErrorCode.Conflict,av.Register(request).Error!.Code);Check(Need(av.GetPublished()).All(x=>!x.AvailableForGameResolution));ar.Dispose();Equal(1,Need(av.GetPublished()).Count);
    }),
    ("Bad format, missing resource/dependency and chart boundary publish nothing",()=>{
        using var f=new Fixture();using var c=f.Context();var view=f.Content.ForPlugin(c);Equal(ErrorCode.ContentInvalid,view.Register(f.Request("one")with{FormatVersion=2}).Error!.Code);Equal(ErrorCode.ContentInvalid,view.Register(f.Request("two")with{File=new(ContentFileArea.Package,"absent.wav")}).Error!.Code);Equal(ErrorCode.ContentInvalid,view.Register(f.Request("three")with{Dependencies=new[]{new LocalContentDependency(new("x","absent"))}}).Error!.Code);Equal(ErrorCode.OperationUnsupported,view.Register(f.Request("chart")with{Kind=LocalContentKind.Chart,Format="rizline.json"}).Error!.Code);Equal(0,Need(view.GetPublished()).Count);Equal(ErrorCode.OperationUnsupported,f.Content.ResolveGameChart(new("x","chart"),"run").Error!.Code);
    }),
    ("Withdrawn content and dependencies stay valid until the host consumer run ends",()=>{
        using var f=new Fixture();var a=f.Context("a");var b=f.Context("b");var av=f.Content.ForPlugin(a);var bv=f.Content.ForPlugin(b);Need(av.Register(f.Request("music")));Need(bv.Register(f.Request("entry")with{Dependencies=new[]{new LocalContentDependency(new("a","music"))}}));var run=Need(f.Content.AcquireValidated(new("b","entry"),"observed-run"));a.Dispose();b.Dispose();Equal(0,Need(f.Content.ForPlugin(f.Context("reader")).GetPublished()).Count);Check(run.Payload is PcmAudio);Equal(ErrorCode.TargetMissing,f.Content.AcquireValidated(new("b","entry"),"new-run").Error!.Code);f.Content.Dispose();Check(run.Payload is PcmAudio);run.Dispose();run.Dispose();
    }),
    ("Content scope, cancellation and per-owner entry quota are executable",()=>{
        using var f=new Fixture();var c=f.Context();var view=f.Content.ForPlugin(c);using var cancel=new CancellationTokenSource();cancel.Cancel();Equal(ErrorCode.Cancelled,view.Register(f.Request("cancel"),cancel.Token).Error!.Code);var registrations=new List<ILocalContentRegistration>();for(var i=0;i<32;i++)registrations.Add(Need(view.Register(f.Request("x"+i))));Equal(ErrorCode.QuotaExceeded,view.Register(f.Request("overflow")).Error!.Code);registrations[0].Dispose();Need(view.Register(f.Request("overflow")));c.Dispose();Equal(ErrorCode.ScopeExpired,view.Register(f.Request("late")).Error!.Code);
    }),
    ("Module disposal restores targets, stops owned playback and releases owned native objects",()=>{
        using var f=new Fixture();var target=f.Add();target.Playing=true;f.Audio.Refresh();using var c=f.Context();var a=Need(f.Play(c).LoadWav(f.File));Need(f.Play(c).Play(a.Asset));Need(f.Replace(c).Replace(f.Target(c),a.Asset));f.Audio.Dispose();Equal(target.Original,target.Current);Check(!target.Playing);Equal(1,f.Backend.Created[0].Destroyed);Check(f.Backend.Sources.All(x=>x.Disposed==1));Equal(target.Retained,target.Released);c.Dispose();
    }),
    ("Chart format validator checks v0, finite values, note layouts, duplicate keys and cancellation",()=>{
        var json=ChartJson();var parsed=ChartJsonValidator.Validate(System.Text.Encoding.UTF8.GetBytes(json));Equal(1,parsed.Lines);Equal(1,parsed.Notes);Equal(120f,parsed.Bpm);
        foreach(var bad in new[]{json.Replace("\"fileVersion\":0","\"fileVersion\":1"),json.Replace("\"bPM\":120","\"bPM\":0"),json.Replace("\"type\":0","\"type\":2"),json.Replace("\"fileVersion\":0","\"fileVersion\":0,\"fileVersion\":0")})Throws<InvalidDataException>(()=>ChartJsonValidator.Validate(System.Text.Encoding.UTF8.GetBytes(bad)));
        using var cancel=new CancellationTokenSource();cancel.Cancel();Throws<OperationCanceledException>(()=>ChartJsonValidator.Validate(System.Text.Encoding.UTF8.GetBytes(json),cancel.Token));
    }),
    ("Experimental chart boundary requires a parsed chart and explicit opt-in",()=>{
        using var f=new ChartFixture();using var c=f.Context();var content=f.Register(c,"one");var view=f.Service.ForPlugin(c);Equal(ErrorCode.OperationUnsupported,view.BindWorkshopChart(111,content.Content.Content).Error!.Code);Equal(ErrorCode.TargetMissing,view.BindWorkshopChart(999,content.Content.Content,true).Error!.Code);var binding=Need(view.BindWorkshopChart(111,content.Content.Content,true));Check(binding.IsActive);Equal(ErrorCode.Conflict,view.BindWorkshopChart(111,content.Content.Content,true).Error!.Code);
    }),
    ("Workshop aliases normalize one object into one B08 exclusive selection",()=>{
        using var f=new ChartFixture();using var a=f.Context("a");using var b=f.Context("b");var ac=f.Register(a,"one");var bc=f.Register(b,"two");var av=f.Service.ForPlugin(a);var bv=f.Service.ForPlugin(b);var first=Need(av.BindWorkshopChart(111,ac.Content.Content,true));Equal(ErrorCode.Conflict,bv.BindWorkshopChart(222,bc.Content.Content,true).Error!.Code);first.Dispose();Need(bv.BindWorkshopChart(222,bc.Content.Content,true));
    }),
    ("Each game chart resolution receives a fresh graph and held runs survive provider withdrawal",()=>{
        using var f=new ChartFixture();var c=f.Context();var registration=f.Register(c,"one");Need(f.Service.ForPlugin(c).BindWorkshopChart(111,registration.Content.Content,true));var first=NeedRoot(f.Service.AdoptResult(new(10)));var firstChart=f.Instances[0];first.Dispose();var second=NeedRoot(f.Service.AdoptResult(new(10)));var secondChart=f.Instances[1];second.Dispose();Check(firstChart!=secondChart);f.Current=secondChart;f.Events.Emit(GameLifecycleChangeKind.RunStarted,"actual-run",RunLifecyclePhase.Playing);c.Dispose();Check(!registration.IsPublished&&!secondChart.Disposed);Check(f.Service.AdoptResult(new(10)) is null);f.Events.Emit(GameLifecycleChangeKind.RunEnded,"actual-run",RunLifecyclePhase.Finished);Check(secondChart.Disposed&&!firstChart.Disposed);f.Events.Emit(GameLifecycleChangeKind.SceneChanged,null,RunLifecyclePhase.Unknown,"Menu");Check(firstChart.Disposed&&f.Parser.Created.All(x=>x.Disposed));
    }),
    ("Module withdrawal keeps adopted chart resources until the observed run ends",()=>{
        using var f=new ChartFixture();using var c=f.Context();var content=f.Register(c,"one");Need(f.Service.ForPlugin(c).BindWorkshopChart(111,content.Content.Content,true));using var root=NeedRoot(f.Service.AdoptResult(new(10)));var chart=f.Instances.Single();f.Current=chart;f.Events.Emit(GameLifecycleChangeKind.RunStarted,"run",RunLifecyclePhase.Playing);f.Service.Dispose();f.Content.SetChartParser(null);Check(!chart.Disposed);f.Events.Emit(GameLifecycleChangeKind.RunEnded,"run",RunLifecyclePhase.Finished);Check(chart.Disposed);Equal(0,f.Events.Subscribers);
    }),
    ("Workshop object reconstruction expires old lease and pending session budget rejects further adoption",()=>{
        using var f=new ChartFixture();using var c=f.Context();var content=f.Register(c,"one");var view=f.Service.ForPlugin(c);var binding=Need(view.BindWorkshopChart(111,content.Content.Content,true));f.Pointer=20;f.Service.Refresh();Check(!binding.IsActive&&f.Service.AdoptResult(new(10)) is null);Need(view.BindWorkshopChart(111,content.Content.Content,true));for(var i=0;i<WorkshopChartService.MaximumSessions;i++)NeedRoot(f.Service.AdoptResult(new(20))).Dispose();Check(f.Service.AdoptResult(new(20)) is null);f.Events.Emit(GameLifecycleChangeKind.SceneChanged,null,RunLifecyclePhase.Unknown,"Menu");Check(f.Instances.All(x=>x.Disposed));
    })
    ,("Owned playback audit stays active through failed Stop and ends after verified release",()=>{
        var audit=new AuditSink();using var f=new Fixture(audit.Recorder());using var c=f.Context();var clip=Need(f.Play(c).LoadWav(f.File));var play=Need(f.Play(c).Play(clip.Asset));Equal(1,audit.Active);Equal(RunModificationCategory.Audio,audit.Begun.Single().Category);
        f.Backend.Sources[0].FailStop=true;Check(!play.Stop().Success);Equal(1,audit.Active);f.Backend.Sources[0].FailStop=false;Need(play.Stop());Equal(0,audit.Active);
    }),
    ("Audio audit tracks the actual priority winner instead of hidden candidates",()=>{
        var audit=new AuditSink();using var f=new Fixture(audit.Recorder());f.Add();f.Audio.Refresh();using var a=f.Context("a");using var b=f.Context("b");using var c=f.Context("c");
        var aa=Need(f.Play(a).LoadWav(f.File));var bb=Need(f.Play(b).LoadWav(f.File));var cc=Need(f.Play(c).LoadWav(f.File));var low=Need(f.Replace(a).Replace(f.Target(a),aa.Asset,1));var high=Need(f.Replace(b).Replace(f.Target(b),bb.Asset,2));var hidden=Need(f.Replace(c).Replace(f.Target(c),cc.Asset,0));
        Equal(2,audit.Begun.Count);Equal(1,audit.Active);Equal("b",audit.Begun.Last().Owner);hidden.Dispose();Equal(2,audit.Begun.Count);high.Dispose();Equal("a",audit.Begun.Last().Owner);low.Dispose();Equal(0,audit.Active);
    }),
    ("Mix changes update one audited effect and ending a lease removes it",()=>{
        var audit=new AuditSink();using var f=new Fixture(audit.Recorder());f.Add();f.Audio.Refresh();using var c=f.Context();var mix=(IAudioMixService)f.Audio.ForPlugin(c,AudioContentCapabilityIds.Mix);
        var lease=Need(mix.Override(f.Target(c),AudioMixParameter.Volume,.5f));Equal(1,audit.Active);Check(lease.SetValue(.6f).Success);Equal(2,audit.Begun.Count);Equal(1,audit.Active);lease.Dispose();Equal(0,audit.Active);
    }),
    ("Throwing audit callbacks cannot alter returned playback or clip replacement",()=>{
        var audit=new RunEffectRecorder(_=>throw new IOException("audit"),_=>throw new IOException("audit"));using var f=new Fixture(audit);var target=f.Add();f.Audio.Refresh();using var c=f.Context();var clip=Need(f.Play(c).LoadWav(f.File));using var play=Need(f.Play(c).Play(clip.Asset));using var replace=Need(f.Replace(c).Replace(f.Target(c),clip.Asset));Equal(f.Backend.Created[0],target.Current);Check(play.IsActive);
    }),
    ("Local chart audit requires actual current-graph association and survives provider withdrawal",()=>{
        var audit=new AuditSink();using var f=new ChartFixture(audit.Recorder());var c=f.Context();var content=f.Register(c,"one");Need(f.Service.ForPlugin(c).BindWorkshopChart(111,content.Content.Content,true));NeedRoot(f.Service.AdoptResult(new(10))).Dispose();Equal(0,audit.Active);c.Dispose();Equal(0,audit.Active);
        f.Current=f.Instances.Single();f.Events.Emit(GameLifecycleChangeKind.RunStarted,"real-run",RunLifecyclePhase.Playing);Equal(1,audit.Active);Equal("real-run",audit.Begun.Single().ObservedRunId);Equal("session",audit.Begun.Single().ObservedSessionId);Check(!audit.Begun.Single().SubmissionBehaviorVerified);f.Events.Emit(GameLifecycleChangeKind.RunEnded,"real-run",RunLifecyclePhase.Finished);Equal(0,audit.Active);
    })
};
tests=tests.Concat(ContentBatchRegression.Cases).ToArray();
tests=tests.Concat(ChartImportRegression.Cases).ToArray();
tests=tests.Concat(AudioDemandLifecycleChecks.Cases).ToArray();
tests=tests.Concat(LocalCatalogRegression.Cases).ToArray();
tests=tests.Concat(LocalCatalogRegression.PlayCompositionCases).ToArray();
tests=tests.Concat(LocalCatalogRegression.PlaybackMetadataCases).ToArray();
tests=tests.Concat(NativeLocalPlayLaunchSourceRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalPlayLaunchSourceRegression.PublicationCases).ToArray();
tests=tests.Concat(NativeLocalPlayRetrySourceRefreshRegression.Cases).ToArray();
tests=tests.Concat(LocalReferenceProbeRegression.Cases).ToArray();
tests=tests.Concat(FactoryCleanupRegression.Cases).ToArray();
tests=tests.Concat(LocalRunSessionRegistryRegression.Cases).ToArray();
tests=tests.Concat(LocalRunProtectionHostRegression.Cases).ToArray();
tests=tests.Concat(NativeControllerGlobalsRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalParametersRegression.Cases).ToArray();
tests=tests.Concat(ControllerPrefabResolverRegression.Cases).ToArray();
tests=tests.Concat(NativeControllerAwakeLedgerRegression.Cases).ToArray();
tests=tests.Concat(InactiveControllerOwnershipRegression.Cases).ToArray();
tests=tests.Concat(InactiveControllerPublicationRegression.Cases).ToArray();
tests=tests.Concat(InactiveControllerParameterBindingRegression.Cases).ToArray();
tests=tests.Concat(NativeInactiveControllerActivationRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalRunSceneTransitionRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalRunReturnTransitionRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalMenuLedgerRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalInitializationCoroutineRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalTerminalCoroutineRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalTerminalRecycleRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalActiveDestructionRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalCutoutEventSourceRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalCutoutCoroutineRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalUnityEventLedgerRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalUnityEventLedgerRegression.ListenerEffectsCases).ToArray();
tests=tests.Concat(NativeLocalUnityEventLedgerRegression.ListenerWriterSourceCases).ToArray();
tests=tests.Concat(NativeUnityEventMidIntegrationTests.Cases).ToArray();
tests=tests.Concat(NativeUnityEventMutationTests.Cases).ToArray();
tests=tests.Concat(NativeUnityEventDelegateWriterTests.Cases).ToArray();
tests=tests.Concat(NativeUnityEventCachedBooleanTests.Cases).ToArray();
tests=tests.Concat(NativeTweenWorkObservationTests.Cases).ToArray();
tests=tests.Concat(NativeTweenWorkObservationTests.SourceReplayCases).ToArray();
tests=tests.Concat(NativeTweenChildEventTests.Cases).ToArray();
tests=tests.Concat(NativeTweenDeathTests.Cases).ToArray();
tests=tests.Concat(NativeTweenTypedEventTests.Cases).ToArray();
tests=tests.Concat(NativeTweenDynamicSourceTests.Cases).ToArray();
tests=tests.Concat(NativeTweenCompletionTests.Cases).Concat(NativeTweenTargetDeathTests.Cases).ToArray();
tests=tests.Concat(NativeTweenRetirementTests.Cases).ToArray();
tests=tests.Concat(NativeTweenRetiredChildTests.Cases).ToArray();
tests=tests.Concat(NativeCoroutineCancellationLedgerTests.Cases).ToArray();
tests=tests.Concat(NativeEngineCoroutineNodeLedgerTests.Cases).Concat(NativeEngineCoroutineIdentityTests.Cases).ToArray();
tests=tests.Concat(NativeEngineCoroutineReceiverLifetimeTests.Cases).ToArray();
tests=tests.Concat(NativeLocalRunTerminationBoundaryStateRegression.ReceiverLifetimeLaunchCases).ToArray();
tests=tests.Concat(NativeEngineObserverRetirementTests.Cases).ToArray();
tests=tests.Concat(ActiveControllerFactoryRetirementRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalActiveConsumersEndedTests.Cases).ToArray();
tests=tests.Concat(FrozenActiveSourceRetirementRegression.Cases).ToArray();
tests=tests.Concat(ObserverRetirementTransactionRegression.Cases).ToArray();
tests=tests.Concat(NativeCoroutineCancellationEngineAssociationTests.Cases).ToArray();
tests=tests.Concat(NativeEngineCoroutineInlineTests.Cases).ToArray();
tests=tests.Concat(NativeEngineSchedulerRowTests.Cases).ToArray();
tests=tests.Concat(NativeEngineWaitTests.Cases).ToArray();
tests=tests.Concat(NativeCoroutineCancellationRetirementTests.Cases).ToArray();
tests=tests.Concat(NativeLocalRunTerminationBoundaryStateRegression.CancellationLaunchCases).ToArray();
tests=tests.Concat(NativeCoroutineCancellationIdentityTests.Cases).ToArray();
tests=tests.Concat(NativeUnityEventDispatchAdoptionTests.Cases).ToArray();
tests=tests.Concat(NativeLocalRunResultSinkRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalRunResultSinkRegression.CommandCompositionCases).ToArray();
tests=tests.Concat(LocalRunTerminationHandlerRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalRunTerminationCaptureFactoryPendingRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalRunTerminationBoundaryStateRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalRunTerminationBoundaryStateRegression.JudgeDrainCases).ToArray();
tests=tests.Concat(NativeLocalRunTerminationBoundaryStateRegression.MissingJudgeAdmissionCases).ToArray();
tests=tests.Concat(LocalAdmissionDiagnosticsChecks.Cases).ToArray();
tests=tests.Concat(NativeLocalRunTerminationBoundaryStateRegression.InitializationCases).ToArray();
tests=tests.Concat(NativeLocalInitializationSettingsRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalFrameCountMetadataChecks.Cases).ToArray();
tests=tests.Concat(NativeLocalRunTerminationBoundaryStateRegression.InitializationSettingsCases).ToArray();
tests=tests.Concat(NativeLocalRunTerminationBoundaryStateRegression.LaunchCommitPumpCases).ToArray();
tests=tests.Concat(NativeLocalRunTerminationBoundaryStateRegression.RunModificationCases).ToArray();
tests=tests.Concat(NativeLocalRunTerminationBoundaryStateRegression.PublicationCases).ToArray();
tests=tests.Concat(NativeNeverActivatedRetirementRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalResultCaptureRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalBundleOwnershipRegression.Cases).ToArray();
tests=tests.Concat(NativeBundleUploadCleanupRegression.Cases).ToArray();
tests=tests.Concat(NativeBundlePublicationArgumentsRegression.Cases).ToArray();
tests=tests.Concat(LocalPlayBundleInputsRegression.Cases).ToArray();
tests=tests.Concat(WorkshopChartPlayabilityChecks.Cases).ToArray();
tests=tests.Concat(ContentFileByteLimitRegression.Cases).ToArray();
tests=tests.Concat(NativeLocalMetadataRegression.Cases).ToArray(); // Metadata unknown-free case intentionally runs last.
if (args.Length == 2 && args[0] == "--isolated-case")
{
    tests = tests.Where(test => test.Name == args[1]).ToArray();
    if (tests.Length != 1) throw new ArgumentException("An isolated case must name exactly one registered regression.");
}
else
{
    // Fault regressions deliberately retain their bounded process responsibilities.
    // Give each recycling scenario its own process rather than exhausting the
    // production owner quota with unrelated scenarios in this test executable.
    var isolated = NativeLocalRunTerminationBoundaryStateRegression.InitializationSettingsCases
        .Concat(NativeLocalInitializationCoroutineRegression.Cases)
        .Concat(NativeLocalTerminalCoroutineRegression.Cases.Where(test => test.Name == "Terminal external launch or source faults stop post-root reads terminal publication and independent cleanup"))
        .Concat(NativeLocalTerminalRecycleRegression.Cases)
        .Concat(NativeLocalActiveDestructionRegression.Cases)
        .Concat(NativeLocalCutoutEventSourceRegression.Cases)
        .Concat(NativeLocalCutoutCoroutineRegression.Cases)
        .Concat(NativeLocalUnityEventLedgerRegression.Cases)
        .Concat(NativeLocalUnityEventLedgerRegression.ListenerEffectsCases)
        .Concat(NativeLocalUnityEventLedgerRegression.ListenerWriterSourceCases)
        .Concat(NativeLocalPlayLaunchSourceRegression.PublicationCases.Where(test => test.Name == "Actual judge root adoption failure remains Unknown rather than a retryable null read"))
        .Concat(NativeUnityEventMidIntegrationTests.Cases)
        .Concat(NativeUnityEventMutationTests.Cases)
        .Concat(NativeUnityEventDelegateWriterTests.Cases)
        .Concat(NativeUnityEventCachedBooleanTests.Cases)
        .Concat(NativeTweenWorkObservationTests.Cases)
        .Concat(NativeTweenWorkObservationTests.SourceReplayCases)
        .Concat(NativeTweenChildEventTests.Cases)
        .Concat(NativeTweenDeathTests.Cases)
        .Concat(NativeTweenTypedEventTests.Cases)
        .Concat(NativeTweenDynamicSourceTests.Cases)
        .Concat(NativeTweenCompletionTests.Cases).Concat(NativeTweenTargetDeathTests.Cases)
        .Concat(NativeTweenRetirementTests.Cases)
        .Concat(NativeTweenRetiredChildTests.Cases)
        .Concat(NativeCoroutineCancellationLedgerTests.Cases)
        .Concat(NativeCoroutineCancellationRetirementTests.Cases)
        .Concat(NativeLocalRunTerminationBoundaryStateRegression.CancellationLaunchCases)
        .Concat(NativeCoroutineCancellationIdentityTests.Cases)
        .Concat(NativeEngineObserverRetirementTests.Cases)
        .Concat(ActiveControllerFactoryRetirementRegression.Cases)
        .Concat(NativeCoroutineCancellationEngineAssociationTests.Cases)
        .Concat(NativeUnityEventDispatchAdoptionTests.Cases).Select(test => test.Name).ToHashSet(StringComparer.Ordinal);
    tests = tests.Select(test => isolated.Contains(test.Name)
        ? (test.Name, (Action)(() => IsolatedAudioRegression.Run(test.Name))) : test).ToArray();
}
var failed=0;foreach(var test in tests)try{test.Test();Console.WriteLine("PASS "+test.Name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+test.Name+"\n"+e);}Console.WriteLine($"{tests.Length-failed}/{tests.Length} audio/content checks passed.");return failed==0?0:1;

sealed class Fixture:IDisposable
{
    public readonly MainThreadDispatcher Main=new();public readonly Backend Backend=new();public readonly AudioService Audio;public readonly LocalContentService Content;public readonly string Directory;
    public ContentFile File=>new(ContentFileArea.Package,"tone.wav");
    public Fixture(RunEffectRecorder? audit=null){Directory=Path.Combine(Path.GetTempPath(),"RizToolbox.AudioContent.Tests-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(Directory);System.IO.File.WriteAllBytes(Path.Combine(Directory,"tone.wav"),Wav());var files=new ContentFileAccess(_=>Directory,_=>Directory);Audio=new(Main,new Logger(),Backend,files,audit:audit);Content=new(Main,files);foreach(var cap in new[]{AudioContentCapabilityIds.Playback,AudioContentCapabilityIds.Replacement,AudioContentCapabilityIds.Mix,AudioContentCapabilityIds.Clock})Audio.Enable(cap);}
    public FakeTarget Add(){var target=new FakeTarget();Backend.Targets.Add(target);return target;}
    public Context Context(string id="test.a")=>new(id,Main);public IAudioPlaybackService Play(Context c)=>(IAudioPlaybackService)Audio.ForPlugin(c,AudioContentCapabilityIds.Playback);
    public IAudioReplacementService Replace(Context c)=>(IAudioReplacementService)Audio.ForPlugin(c,AudioContentCapabilityIds.Replacement);public IAudioMixService Mix(Context c)=>(IAudioMixService)Audio.ForPlugin(c,AudioContentCapabilityIds.Mix);public IAudioClockService Clock(Context c)=>(IAudioClockService)Audio.ForPlugin(c,AudioContentCapabilityIds.Clock);
    public TargetHandle<AudioAsset> Target(Context c)=>Need(Replace(c).GetTargets())[0].Target;
    public LocalContentRequest Request(string id)=>new(id,LocalContentKind.Audio,"wav.pcm16",1,File);
    public void Dispose(){Audio.Dispose();Content.Dispose();Main.Dispose();foreach(var file in System.IO.Directory.GetFiles(Directory))System.IO.File.Delete(file);System.IO.Directory.Delete(Directory);}
}
sealed class Backend:IAudioBackend
{
    public List<FakeTarget> Targets=new();public List<FakeClip> Created=new();public List<FakeSource> Sources=new();public Action? OnCreate;public double Dsp=1.25;
    public int Discoveries;
    public IEnumerable<AudioCandidate> Discover(int max)
    {
        Discoveries++;
        return Targets.Take(max).Select(x=>new AudioCandidate(x.Id,x.Id,new(Active:x.Active),new[]{x.Id},()=>x.Current,v=>{x.Writes++;if(x.OnWrite is not null)x.OnWrite((FakeClip)v);else x.Current=(FakeClip)v;},()=>x.Alive,()=>x.Alive&&x.Active,()=>x.Playing,()=>x.Playing=false,()=>x.Volume,v=>x.Volume=v,()=>x.Pitch,v=>x.Pitch=v,x.Retain));
    }
    public IAudioClipObject Create(PcmAudio audio){var clip=new FakeClip(Guid.NewGuid().ToString("N"));Created.Add(clip);OnCreate?.Invoke();return clip;}
    public IOwnAudioSource Play(IAudioClipObject clip,float volume,bool loop){var source=new FakeSource();Sources.Add(source);return source;}
    public AudioClockSnapshot Clock()=>new(Dsp,12);
}
sealed class FakeTarget
{
    public string Id=Guid.NewGuid().ToString("N");public bool Alive=true,Active=true,Playing;public float Volume=1,Pitch=1;public int Writes,Retained,Released;
    public FakeClip Original=new("game"),Current;public Action<FakeClip>? OnWrite;public FakeTarget()=>Current=Original;
    public IDisposable Retain(){Retained++;return new ActionLease(()=>Released++);}
}
sealed class FakeClip:IAudioClipObject
{
    public string Identity{get;}public int Destroyed,Retained,Released;public bool IsAlive=>Destroyed==0;public int SampleFrames=>4;public int Channels=>1;public int Frequency=>8000;
    public FakeClip(string id)=>Identity=id;public IDisposable Retain(){Retained++;return new ActionLease(()=>Released++);}public void DestroyOwned(){Destroyed++;}
}
sealed class FakeSource:IOwnAudioSource
{
    public bool Playing=true,FailStop;public int Disposed,Stops;public bool IsPlaying=>Playing;
    public void Stop(){Stops++;if(FailStop)throw new IOException("native stop failed");Playing=false;}
    public void Dispose(){if(Playing)throw new InvalidOperationException("Voice is still playing.");Disposed++;}
}
sealed class Context:IPluginContext,IDisposable
{
    private readonly Stack<IDisposable> _owned=new();private bool _active=true;public string PluginId{get;}public IMainThreadDispatcher MainThread{get;}public Action<IDisposable>? OnOwn;
    public Context(string id,IMainThreadDispatcher main){PluginId=id;MainThread=main;}public ILogger Log=>new Logger();public IEventBus Events=>throw new NotSupportedException();public GameBuildDescriptor GameBuild=>new("test","test","test","test");
    public CapabilityDescriptor GetCapability(string id)=>new(id,false);public ServiceLookup<T> GetService<T>(string id)where T:class=>new(false,null,new(ErrorCode.CapabilityUnavailable,"test unavailable",id));
    public T Own<T>(T value)where T:IDisposable{if(!_active)throw new ObjectDisposedException("Context");OnOwn?.Invoke(value);if(!_active)throw new ObjectDisposedException("Context");_owned.Push(value);return value;}
    public void Dispose(){if(!_active)return;_active=false;while(_owned.Count>0)_owned.Pop().Dispose();}
}
sealed class Logger:ILogger{public void Log(LogLevel level,string message,Exception? exception=null){}}
sealed class ChartFixture:IDisposable
{
    public MainThreadDispatcher Main=new();public string Directory;public LocalContentService Content;public WorkshopChartService Service;public FakeChartParser Parser=new();public FakeLifecycle Events=new();public List<FakeParsedChart> Instances=new();public FakeParsedChart? Current;public long Pointer=10;
    public ChartFixture(RunEffectRecorder? audit=null)
    {
        Directory=Path.Combine(Path.GetTempPath(),"RizToolbox.Chart.Tests-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(Directory);File.WriteAllText(Path.Combine(Directory,"chart.json"),ChartJson());Content=new(Main,new ContentFileAccess(_=>Directory,_=>Directory));Content.SetChartParser(Parser);
        Service=new(Main,new Logger(),Content,Events,id=>{if(id is not (111 or 222))throw new KeyNotFoundException("Unknown workshop ID.");var captured=Pointer;return new NativeRuntime.RootHandle(1,_=>{},_=>new(captured));},
            chart=>{var instance=new FakeParsedChart(chart.Source);Instances.Add(instance);return instance;},chart=>new NativeRuntime.RootHandle(1,_=>{},_=>new(((FakeParsedChart)chart).Pointer)),chart=>ReferenceEquals(chart,Current),audit);
    }
    public Context Context(string id="local.test")=>new(id,Main);
    public ILocalContentRegistration Register(Context context,string id)=>Need(Content.ForPlugin(context).Register(new(id,LocalContentKind.Chart,"rizline.chart.json",0,new(ContentFileArea.Package,"chart.json"))));
    public void Dispose(){Service.Dispose();Events.Emit(GameLifecycleChangeKind.SceneChanged,null,RunLifecyclePhase.Unknown,"Menu");Content.Dispose();Main.Dispose();File.Delete(Path.Combine(Directory,"chart.json"));System.IO.Directory.Delete(Directory);}
}
sealed class FakeChartParser:ILocalChartParser
{public List<FakeParsedChart> Created=new();public IParsedLocalChart Parse(byte[] bytes,CancellationToken token){var chart=new FakeParsedChart(ChartJsonValidator.Validate(bytes,token));Created.Add(chart);return chart;}}
sealed class FakeParsedChart:IParsedLocalChart
{private static long Next=100;public long Pointer=Interlocked.Increment(ref Next);public ValidatedChartJson Source{get;}public bool Disposed;public FakeParsedChart(ValidatedChartJson source)=>Source=source;public void Dispose()=>Disposed=true;}
sealed class FakeLifecycle:IGameLifecycleService
{
    private readonly List<Action<GameLifecycleChange>> _listeners=new();private long _revision;public int Subscribers=>_listeners.Count;
    private GameLifecycleSnapshot Snapshot(string? run=null,RunLifecyclePhase phase=RunLifecyclePhase.Unknown,string scene="Level")=>new("session",++_revision,1,scene,run,null,phase,false,true,true,false,Array.Empty<OperationError>());
    public Result<GameLifecycleSnapshot> GetSnapshot()=>Result<GameLifecycleSnapshot>.Ok(Snapshot());
    public Result<IGameLifecycleSubscription> Subscribe(Action<GameLifecycleChange> callback){_listeners.Add(callback);return Result<IGameLifecycleSubscription>.Ok(new Subscription(Snapshot(),new ActionLease(()=>_listeners.Remove(callback))));}
    public void Emit(GameLifecycleChangeKind kind,string? run,RunLifecyclePhase phase,string scene="Level"){var change=new GameLifecycleChange(kind,RunLifecyclePhase.Unknown,Snapshot(run,phase,scene),new(DateTimeOffset.UtcNow,LifecycleObservationSource.SampledGameState,250,"test"));foreach(var callback in _listeners.ToArray())callback(change);}
    private sealed class Subscription:IGameLifecycleSubscription{private IDisposable _release;public GameLifecycleSnapshot InitialSnapshot{get;}public Subscription(GameLifecycleSnapshot snapshot,IDisposable release){InitialSnapshot=snapshot;_release=release;}public void Dispose()=>_release.Dispose();}
}
sealed class AuditSink
{
    public List<RunModificationInput> Begun=new();public int Active;
    public RunEffectRecorder Recorder()=>new(_=>{},input=>{Begun.Add(input);Active++;return new ActionLease(()=>Active--);});
}
static class Assert
{
    public static void Check(bool value){if(!value)throw new Exception("Assertion failed.");}public static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Expected {expected}; actual {actual}.");}
    public static T Need<T>(Result<T> result){if(!result.Success)throw new Exception(result.Error!.ToString());return result.Value!;}public static void Need(Result result){if(!result.Success)throw new Exception(result.Error!.ToString());}
    public static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    public static NativeRuntime.RootHandle NeedRoot(NativeRuntime.RootHandle? root)=>root??throw new Exception("Expected rooted chart adoption.");
    public static string ChartJson()=>"{\"fileVersion\":0,\"songsName\":\"test\",\"bPM\":120,\"themes\":[{\"colorsList\":[{\"r\":255,\"g\":255,\"b\":255,\"a\":255},{\"r\":255,\"g\":255,\"b\":255,\"a\":255},{\"r\":255,\"g\":255,\"b\":255,\"a\":255}]}],\"lines\":[{\"linePoints\":[{\"time\":0,\"xPosition\":0,\"color\":{\"r\":255,\"g\":255,\"b\":255,\"a\":255},\"easeType\":0,\"canvasIndex\":0,\"floorPosition\":0},{\"time\":2,\"xPosition\":0,\"color\":{\"r\":255,\"g\":255,\"b\":255,\"a\":255},\"easeType\":0,\"canvasIndex\":0,\"floorPosition\":2}],\"notes\":[{\"type\":0,\"time\":1,\"floorPosition\":1,\"otherInformations\":[]}],\"judgeRingColor\":[],\"lineColor\":[]}],\"challengeTimes\":[],\"bpmShifts\":[{\"time\":0,\"value\":1,\"easeType\":0,\"floorPosition\":0}],\"canvasMoves\":[{\"index\":0,\"xPositionKeyPoints\":[{\"time\":0,\"value\":0,\"easeType\":0,\"floorPosition\":0}],\"speedKeyPoints\":[{\"time\":0,\"value\":1,\"easeType\":0,\"floorPosition\":0}]}],\"cameraMove\":{\"scaleKeyPoints\":[{\"time\":0,\"value\":1,\"easeType\":0,\"floorPosition\":0}],\"xPositionKeyPoints\":[{\"time\":0,\"value\":0,\"easeType\":0,\"floorPosition\":0}]}}";
    public static byte[] Wav()
    {
        var bytes=new byte[52];System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes,0);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),44);System.Text.Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(bytes,8);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16),16);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20),1);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22),1);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24),8000);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28),16000);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32),2);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34),16);System.Text.Encoding.ASCII.GetBytes("data").CopyTo(bytes,36);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40),8);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44),short.MinValue);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(46),-1);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(48),0);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(50),short.MaxValue);return bytes;
    }
}
