using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Sdk;
using System.Runtime.CompilerServices;
using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Managed ownership layer. Borrowed clips are rooted but never destroyed; active source pins outlive provider leases.</summary>
internal sealed class AudioService:IDisposable
{
    internal const int MaximumTargets=128,MaximumScanned=4096,MaximumPlaying=32;
    private readonly IMainThreadDispatcher _main;private readonly ILogger _log;private readonly IAudioBackend _backend;private readonly ContentFileAccess _files;
    private readonly AudioBudget _budget;private readonly SemanticTargetRegistry _targets;private readonly MutationCoordinator _mutations;
    private readonly RunEffectRecorder? _audit;
    private readonly Dictionary<string,Entry> _assets=new(StringComparer.Ordinal),_identities=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Binding> _bindings=new(StringComparer.Ordinal);private readonly HashSet<PlaybackLease> _playbacks=new();
    private readonly Dictionary<string,long> _epochs=new(StringComparer.Ordinal);private readonly HashSet<string> _enabled=new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<IPluginContext,ScopeIdentity> _scopeIds=new();
    private bool _disposed, _retiring, _busy;
    internal bool IsDisposed => _disposed;
    internal bool IsRetiring => _retiring;
    internal bool HasDemand => _enabled.Count != 0;
    internal bool HasSourceDemand => _enabled.Contains(AudioContentCapabilityIds.Replacement) || _enabled.Contains(AudioContentCapabilityIds.Mix);
    internal bool NeedsMaintenance => !_disposed && (HasSourceDemand || _playbacks.Count != 0 || _bindings.Count != 0 || _retiring);
    private sealed class ScopeIdentity{internal readonly string Id=Guid.NewGuid().ToString("N");}
    private string ScopeOwner(IPluginContext context)=>_scopeIds.GetValue(context,_=>new()).Id;
    private sealed class Entry
    {
        internal AudioAsset Asset=null!;internal IAudioClipObject Native=null!;internal IDisposable Root=null!;internal IDisposable? Budget;
        internal bool Owned, CleanupStarted;internal int References,Frames,Channels,Frequency;internal Dictionary<string,int> Owners=new(StringComparer.Ordinal);
    }
    private sealed class Binding
    {
        internal AudioCandidate Source=null!;internal TargetHandle<AudioAsset> Target=null!;internal TargetHandle<float>? VolumeTarget,PitchTarget;
        internal MutationChannel<AudioAsset>? Clip;internal MutationChannel<float>? Volume,Pitch;
        internal bool Retiring, Restore, StopIssued;
        internal HashSet<Entry> Pins=new(),PlaybackPins=new();
        internal AuditSlot ClipAudit=new(),VolumeAudit=new(),PitchAudit=new();
    }
    private sealed class AuditSlot { internal string? LeaseId; internal object? Value; internal IDisposable? Effect; }
    internal AudioService(IMainThreadDispatcher main,ILogger log,IAudioBackend backend,ContentFileAccess files,AudioBudget? budget=null,RunEffectRecorder? audit=null)
    {_main=main;_log=log;_backend=backend;_files=files;_budget=budget??new();_targets=new(main,log);_mutations=new(_targets,main,log);_audit=audit;}
    private void SyncAudit()
    {
        if(_audit is null||!_main.IsMainThread)return;
        foreach(var binding in _bindings.Values.ToArray())
            try { if(binding.Clip is not null)Sync(binding.Clip,binding.ClipAudit,"replace-clip");if(binding.Volume is not null)Sync(binding.Volume,binding.VolumeAudit,"volume");if(binding.Pitch is not null)Sync(binding.Pitch,binding.PitchAudit,"pitch"); }
            catch { /* An audit failure never changes an already completed audio mutation. */ }
    }
    private void Sync<T>(MutationChannel<T> channel,AuditSlot slot,string operation)
    {
        var state=channel.Describe();var winner=state.Valid?state.Holders.SingleOrDefault(x=>x.Winner):null;
        object? nextValue=winner is null?null:winner.Candidate;
        if(slot.LeaseId==winner?.LeaseId&&Equals(slot.Value,nextValue))return;
        slot.Effect?.Dispose();slot.Effect=null;slot.LeaseId=winner?.LeaseId;slot.Value=nextValue;
        if(winner is not null)slot.Effect=_audit!.Begin(new(winner.Owner,"riz.audio",operation,state.Key.Target.Key,
            RunModificationCategory.Audio,RunModificationOutcome.Applied,"Verified mutation channel winner; priority="+winner.Priority+". Audio latency and result/submission effects remain unverified."));
    }
    private OperationError? Guard(Gate? gate=null)
    {
        if(!_main.IsMainThread)return new(ErrorCode.WrongThread,"Audio operations require the verified main thread.");
        if(_disposed||_retiring||gate is {Active:false})return new(ErrorCode.ScopeExpired,"Audio service or caller scope has expired.");
        if(_busy)return new(ErrorCode.Conflict,"Reentrant audio operations are rejected.");return null;
    }
    private Result<T> Execute<T>(Gate gate,Func<T> action)
    {
        var error=Guard(gate);if(error is not null)return Result<T>.Fail(error);_busy=true;
        try{return Result<T>.Ok(action());}catch(Exception e){return Result<T>.Fail(Error(e));}finally{_busy=false;Sweep();}
    }
    private static OperationError Error(Exception e)=> e is AdapterOperationException failure ? failure.Error : new(e switch{OperationCanceledException=>ErrorCode.Cancelled,AudioQuotaException=>ErrorCode.QuotaExceeded,
        InvalidDataException or FileNotFoundException or DirectoryNotFoundException=>ErrorCode.ContentInvalid,UnsupportedAudioException=>ErrorCode.OperationUnsupported,
        AudioExpiredException=>ErrorCode.TargetExpired,AudioScopeException=>ErrorCode.ScopeExpired,_=>ErrorCode.ResourceLoadFailed},e.Message);
    internal object ForPlugin(IPluginContext context,string capability)
    {
        _main.VerifyAccess();var epoch=_epochs.GetValueOrDefault(capability);
        var gate=context.Own(new Gate(context,()=>_enabled.Contains(capability)&&_epochs.GetValueOrDefault(capability)==epoch));var view=new View(this,context,gate);
        return capability switch {AudioContentCapabilityIds.Playback=>new PlaybackView(view),AudioContentCapabilityIds.Replacement=>new ReplacementView(view),
            AudioContentCapabilityIds.Mix=>new MixView(view),AudioContentCapabilityIds.Clock=>new ClockView(view),_=>throw new ArgumentException("Unknown audio capability.",nameof(capability))};
    }
    internal void Enable(string capability){_main.VerifyAccess();_enabled.Add(capability);}
    internal void ProbeUpload(){_main.VerifyAccess();var clip=_backend.Create(new(1,1,8000,new[]{0f}));using var root=clip.Retain();clip.DestroyOwned();}
    internal bool ProbeClock()
    {
        _main.VerifyAccess();
        var value = _backend.Clock();
        return double.IsFinite(value.DspSeconds) && value.DspSeconds >= 0 && double.IsFinite(value.HostMonotonicSeconds);
    }
    internal void Disable(string capability)
    {
        _main.VerifyAccess();
        if (!_enabled.Remove(capability)) return;
        _epochs[capability]=_epochs.GetValueOrDefault(capability)+1;
        if(capability==AudioContentCapabilityIds.Playback)StopPlayback();
        if (capability is not (AudioContentCapabilityIds.Replacement or AudioContentCapabilityIds.Mix)) return;
        if (!HasSourceDemand) { ReleaseTargets(); return; }
        foreach (var binding in _bindings.Values)
        {
            if (binding.Retiring) continue;
            if (capability == AudioContentCapabilityIds.Replacement) ReleaseClip(binding);
            else ReleaseMix(binding);
        }
        Sweep();
    }
    private Entry Observe(IAudioClipObject clip)
    {
        if(_identities.TryGetValue(clip.Identity,out var entry))return entry;
        if(_assets.Count>=MaximumTargets*8+AudioBudget.MaximumTotalAssets)throw new AudioQuotaException("Retained audio identity cache is full.");
        var frames=clip.SampleFrames;var channels=clip.Channels;var frequency=clip.Frequency;
        entry=new(){Asset=new(Guid.NewGuid().ToString("N")),Native=clip,Root=clip.Retain(),Frames=frames,Channels=channels,Frequency=frequency};_assets.Add(entry.Asset.Id,entry);_identities.Add(clip.Identity,entry);return entry;
    }
    private Entry Find(AudioAsset asset,string? owner=null)
    {
        if(asset is null||!_assets.TryGetValue(asset.Id,out var entry)||entry.Asset!=asset||entry.CleanupStarted||!entry.Native.IsAlive)throw new AudioExpiredException("Audio asset is no longer alive.");
        if(owner is not null&&entry.Owners.GetValueOrDefault(owner)<=0)throw new AudioExpiredException("Caller has no live lease for this audio asset.");return entry;
    }
    private IAudioAssetLease Create(IPluginContext context,Gate gate,ContentFile file,CancellationToken token)
    {
        if(!string.Equals(Path.GetExtension(file?.RelativePath),".wav",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Only .wav PCM16 audio is supported.");
        var bytes=_files.Read(context.PluginId,file!,token);var audio=WavDecoder.Decode(bytes,token);using var pending=new Pending(_budget.Reserve(context.PluginId,audio.EstimatedBytes));
        token.ThrowIfCancellationRequested();var native=_backend.Create(audio);
        try
        {
            if(!gate.Active)throw new AudioScopeException("Caller scope ended during audio upload.");
            token.ThrowIfCancellationRequested();if(_identities.ContainsKey(native.Identity))throw new InvalidDataException("A created clip reused a live identity.");
            var entry=Observe(native);entry.Owned=true;entry.Budget=pending.Take();entry.References++;var scope=ScopeOwner(context);entry.Owners[scope]=1;
            var lease=new AssetLease(this,entry,scope);
            try{return context.Own<IAudioAssetLease>(lease);}catch{lease.Dispose();throw;}
        }
        catch{if(!_identities.ContainsKey(native.Identity))native.DestroyOwned();throw;}
    }
    internal void Refresh(bool discover=true)
    {
        _main.VerifyAccess();if(_disposed)return;if(_busy)throw new InvalidOperationException("Reentrant audio refresh.");_busy=true;
        try
        {
            foreach(var playback in _playbacks.ToArray())
                TryMaintain(() => playback.ObserveCompletion(), "Playback retirement");
            foreach(var binding in _bindings.Values.ToArray())
                TryMaintain(() =>
                {
                    if (binding.Retiring) Remove(binding, binding.Restore);
                    else if (!binding.Source.IsAlive()) Remove(binding, false);
                    else if (!binding.Source.IsEligible() || binding.Clip is { Valid: false }) Remove(binding, true);
                }, "Audio source retirement");
            if(discover && HasSourceDemand)foreach(var candidate in _backend.Discover(MaximumScanned).Take(MaximumScanned))
            {
                if(_bindings.ContainsKey(candidate.Identity)||candidate.Metadata.Template||!candidate.Metadata.Active||!Safe(candidate.IsAlive)||!Safe(candidate.IsEligible))continue;
                if(_bindings.Count>=MaximumTargets)break;Bind(candidate);
            }
            foreach (var binding in _bindings.Values)
            {
                if (binding.Retiring) continue;
                Pin(binding);
                RefreshChannel(binding.Clip);
                RefreshChannel(binding.Volume);
                RefreshChannel(binding.Pitch);
                Pin(binding);
            }
            SyncAudit();
        }
        finally{_busy=false;Sweep();CompleteRetirement();}
    }
    private void RefreshChannel<T>(MutationChannel<T>? channel)
    {
        if (channel?.Refresh() is { Success: false } result)
            _log.Log(LogLevel.Warning, "Audio mutation refresh: " + result.Error);
    }
    private void Bind(AudioCandidate candidate)
    {
        IDisposable? root=null;TargetHandle<AudioAsset>? target=null;
        try
        {
            root=candidate.Retain();var key=candidate.Alias+"/binding/"+Guid.NewGuid().ToString("N");
            target=Need(_targets.Register(key,()=>Observe(candidate.Read()).Asset,candidate.IsAlive,root,candidate.Metadata,candidate.Aliases));root=null;
            _bindings.Add(candidate.Identity,new Binding{Source=candidate,Target=target});
        }
        catch(Exception e){if(target is not null)_targets.Invalidate(target);root?.Dispose();_log.Log(LogLevel.Warning,"Audio source binding refused: "+e.Message);}
    }
    private MutationChannel<AudioAsset> ClipChannel(Binding binding)
        => binding.Clip ??= Need(_mutations.Register(binding.Target, "clip", value => binding.Source.Write(Find(value).Native),
            validate: value =>
            {
                try { return Find(value).Native.IsAlive ? null : "Audio clip expired."; }
                catch (Exception error) { return error.Message; }
            }));
    private MutationChannel<float> MixChannel(Binding binding,AudioMixParameter parameter)
    {
        if(parameter==AudioMixParameter.Volume&&binding.Volume is not null)return binding.Volume;
        if(parameter==AudioMixParameter.Pitch&&binding.Pitch is not null)return binding.Pitch;
        var source=binding.Source;var property=parameter==AudioMixParameter.Volume?"volume":"pitch";
        var target=Need(_targets.Register(binding.Target.Key+"/"+property,parameter==AudioMixParameter.Volume?source.ReadVolume:source.ReadPitch,source.IsAlive,metadata:source.Metadata));
        try
        {
            var channel=Need(_mutations.Register(target,property,parameter==AudioMixParameter.Volume?source.WriteVolume:source.WritePitch,validate:x=>MixValid(parameter,x)?null:"Invalid source mix value."));
            if(parameter==AudioMixParameter.Volume){binding.VolumeTarget=target;binding.Volume=channel;}else{binding.PitchTarget=target;binding.Pitch=channel;}return channel;
        }
        catch{_targets.Invalidate(target);throw;}
    }
    private void Pin(Binding binding)
    {
        if (binding.Retiring || (binding.Clip is null && binding.PlaybackPins.Count == 0 && binding.Pins.Count == 0)) return;
        var current = new HashSet<Entry>();
        if (binding.Clip?.Describe() is { } snapshot)
            foreach(var asset in snapshot.Holders.Select(x=>x.Candidate).Append(snapshot.Baseline).Append(snapshot.Effective))
                if(asset is not null&&_assets.TryGetValue(asset.Id,out var entry))current.Add(entry);
        bool? playing = null;
        TryMaintain(() => playing = binding.Source.IsPlaying(), "Audio voice readback");
        if(playing != false)
        {
            // Keep every observed clip until a verified stopped edge. Changing AudioSource.clip is not proof that the old native voice ended.
            TryMaintain(() =>
            {
                var clip = Observe(binding.Source.Read());
                if(binding.PlaybackPins.Add(clip))clip.References++;
            }, "Audio playing clip readback");
            foreach(var pin in current)if(binding.PlaybackPins.Add(pin))pin.References++;
        }
        else{foreach(var pin in binding.PlaybackPins)pin.References--;binding.PlaybackPins.Clear();}
        foreach(var entry in current.Except(binding.Pins))entry.References++;
        foreach(var entry in binding.Pins.Except(current))entry.References--;binding.Pins=current;
    }
    private void Remove(Binding binding,bool restore)
    {
        if (!binding.Retiring) { binding.Retiring = true; binding.Restore = restore; }
        if(binding.PlaybackPins.Any(x=>x.Owned) && binding.Source.IsPlaying())
        {
            if (!binding.StopIssued) { binding.StopIssued = true; binding.Source.Stop(); }
            if(binding.Source.IsPlaying()) return;
        }
        if(binding.Restore && binding.Source.IsAlive()){ReleaseClip(binding);ReleaseMix(binding);}
        _targets.Invalidate(binding.Target);if(binding.VolumeTarget is not null)_targets.Invalidate(binding.VolumeTarget);if(binding.PitchTarget is not null)_targets.Invalidate(binding.PitchTarget);_bindings.Remove(binding.Source.Identity);
        binding.ClipAudit.Effect?.Dispose();binding.VolumeAudit.Effect?.Dispose();binding.PitchAudit.Effect?.Dispose();
        // An inactive but live source can still play. Explicit cancellation is required before releasing active pins.
        foreach(var entry in binding.Pins)entry.References--;foreach(var entry in binding.PlaybackPins)entry.References--;binding.Pins.Clear();binding.PlaybackPins.Clear();
    }
    private void ReleaseClip(Binding binding)
    {
        binding.Clip?.Dispose(); binding.Clip = null;
        EndAudit(binding.ClipAudit);
        Pin(binding);
    }
    private void ReleaseMix(Binding binding)
    {
        binding.Volume?.Dispose(); binding.Pitch?.Dispose();
        if (binding.VolumeTarget is { } volume) _targets.Invalidate(volume);
        if (binding.PitchTarget is { } pitch) _targets.Invalidate(pitch);
        binding.Volume = null; binding.Pitch = null; binding.VolumeTarget = null; binding.PitchTarget = null;
        EndAudit(binding.VolumeAudit); EndAudit(binding.PitchAudit);
    }
    private static void EndAudit(AuditSlot slot)
    {
        slot.Effect?.Dispose(); slot.Effect = null; slot.LeaseId = null; slot.Value = null;
    }
    private Result<IMutationLease<AudioAsset>> Replace(IPluginContext context,Gate gate,TargetHandle<AudioAsset> target,AudioAsset asset,int priority)
    {
        var error=Guard(gate);if(error is not null)return Result<IMutationLease<AudioAsset>>.Fail(error);_busy=true;Entry? entry=null;
        try
        {
            entry=Find(asset,ScopeOwner(context));var binding=_bindings.Values.SingleOrDefault(x=>x.Target==target);
            if(binding is null || binding.Retiring)return Result<IMutationLease<AudioAsset>>.Fail(new(ErrorCode.TargetExpired,"Audio source generation is absent."));
            var channel = ClipChannel(binding);
            Pin(binding);entry.References++;var result=channel.Override(context.PluginId,asset,priority);
            if(!result.Success){entry.References--;return Result<IMutationLease<AudioAsset>>.Fail(result.Error!);}
            Pin(binding);var lease=new ReplacementLease(this,entry,result.Value!);entry=null;
            if(!gate.Active){lease.Dispose();return Result<IMutationLease<AudioAsset>>.Fail(new(ErrorCode.ScopeExpired,"Caller ended during clip replacement."));}
            try{return Result<IMutationLease<AudioAsset>>.Ok(context.Own<IMutationLease<AudioAsset>>(lease));}catch{lease.Dispose();throw;}
        }
        catch(Exception e){return Result<IMutationLease<AudioAsset>>.Fail(Error(e));}finally{_busy=false;SyncAudit();Sweep();}
    }
    private void Sweep()
    {
        if(_busy)return;
        foreach(var entry in _assets.Values.Where(x=>x.References==0 && !x.CleanupStarted).ToArray())
        {
            // An exception can follow an actual native destruction. Keep the
            // record, pin and quota on uncertainty and never issue it again.
            entry.CleanupStarted = true;
            TryMaintain(() =>
            {
                if (entry.Owned) entry.Native.DestroyOwned();
                entry.Root.Dispose(); entry.Budget?.Dispose();
                _assets.Remove(entry.Asset.Id); _identities.Remove(entry.Native.Identity);
            }, "Audio clip cleanup");
        }
    }
    private void TryMaintain(Action action, string stage)
    {
        try { action(); }
        catch (Exception error)
        {
            try { _log.Log(LogLevel.Warning, stage + " failed; unconfirmed resources remain owned.", error); } catch { }
        }
    }
    internal void StopPlayback(){_main.VerifyAccess();foreach(var playback in _playbacks.ToArray())TryMaintain(playback.BeginRetirement,"Playback stop");Sweep();}
    internal void ReleaseTargets(){_main.VerifyAccess();foreach(var binding in _bindings.Values.ToArray())TryMaintain(()=>Remove(binding,true),"Audio source release");Sweep();}
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed || _retiring) return;
        _retiring = true; _enabled.Clear();
        StopPlayback(); ReleaseTargets(); CompleteRetirement();
    }
    private void CompleteRetirement()
    {
        if (!_retiring || _disposed || _playbacks.Count != 0 || _bindings.Count != 0) return;
        foreach(var entry in _assets.Values)entry.References=0;
        Sweep();
        if (_assets.Count != 0) return;
        _mutations.Dispose(); _targets.Dispose(); _disposed = true;
    }
    private static bool Safe(Func<bool> read){try{return read();}catch{return false;}}
    private static T Need<T>(OperationResult<T> result)=>result.Success?result.Value!:throw new AdapterOperationException(result.Error!);
    private static bool MixValid(AudioMixParameter p,float v)=>float.IsFinite(v)&&(p==AudioMixParameter.Volume?v is >=0 and <=1:p==AudioMixParameter.Pitch&&v is >=.25f and <=3);
    private sealed class Gate:IDisposable
    {
        private readonly IPluginContext _context;private readonly Func<bool> _current;private bool _active=true;internal Gate(IPluginContext context,Func<bool> current){_context=context;_current=current;}
        internal bool Active=>_active&&_current()&&(_context is not IScopedPluginContext scoped||!scoped.Lifetime.IsCancellationRequested);public void Dispose()=>_active=false;
    }
    private sealed class PlaybackView:IAudioPlaybackService
    {private readonly View _view;internal PlaybackView(View view)=>_view=view;public Result<IAudioAssetLease> LoadWav(ContentFile file,CancellationToken cancellationToken=default)=>_view.LoadWav(file,cancellationToken);public Result<IAudioPlaybackLease> Play(AudioAsset clip,float volume=1,bool loop=false)=>_view.Play(clip,volume,loop);}
    private sealed class ReplacementView:IAudioReplacementService
    {private readonly View _view;internal ReplacementView(View view)=>_view=view;public Result<IReadOnlyList<AudioTarget>> GetTargets()=>_view.GetTargets();public Result<IMutationLease<AudioAsset>> Replace(TargetHandle<AudioAsset> target,AudioAsset clip,int priority=0)=>_view.Replace(target,clip,priority);}
    private sealed class MixView:IAudioMixService
    {private readonly View _view;internal MixView(View view)=>_view=view;public Result<IMutationLease<float>> Override(TargetHandle<AudioAsset> target,AudioMixParameter parameter,float value,int priority=0)=>_view.Override(target,parameter,value,priority);}
    private sealed class ClockView:IAudioClockService
    {private readonly View _view;internal ClockView(View view)=>_view=view;public Result<AudioClockSnapshot> Read()=>_view.Read();}
    private sealed class View:IAudioPlaybackService,IAudioReplacementService,IAudioMixService,IAudioClockService
    {
        private readonly AudioService _owner;private readonly IPluginContext _context;private readonly Gate _gate;
        internal View(AudioService owner,IPluginContext context,Gate gate){_owner=owner;_context=context;_gate=gate;}
        public Result<IAudioAssetLease> LoadWav(ContentFile file,CancellationToken cancellationToken=default)=>_owner.Execute(_gate,()=>_owner.Create(_context,_gate,file,cancellationToken));
        public Result<IAudioPlaybackLease> Play(AudioAsset clip,float volume=1,bool loop=false)=>_owner.Execute<IAudioPlaybackLease>(_gate,()=>
        {
            if(!MixValid(AudioMixParameter.Volume,volume))throw new InvalidDataException("Volume must be finite and in [0,1].");
            var entry=_owner.Find(clip,_owner.ScopeOwner(_context));if(_owner._playbacks.Count>=MaximumPlaying)throw new AudioQuotaException("At most 32 framework playback sources are permitted.");
            entry.References++;IOwnAudioSource source;try{source=_owner._backend.Play(entry.Native,volume,loop);}catch{entry.References--;throw;}
            var lease=new PlaybackLease(_owner,entry,source);_owner._playbacks.Add(lease);
            lease.Audit=_owner._audit?.Begin(new(_context.PluginId,"riz.audio.playback","play",entry.Asset.Id,RunModificationCategory.Audio,RunModificationOutcome.Applied,"Owned AudioSource returned; not a verified latency or result/submission guarantee."));
            if(!_gate.Active){lease.Dispose();throw new AudioScopeException("Caller ended during playback creation.");}
            try{return _context.Own<IAudioPlaybackLease>(lease);}catch{lease.Dispose();throw;}
        });
        public Result<IReadOnlyList<AudioTarget>> GetTargets()=>_owner.Execute<IReadOnlyList<AudioTarget>>(_gate,()=>_owner._bindings.Values.Where(x=>!x.Retiring).Select(x=>new AudioTarget(x.Target,x.Source.Metadata,Array.AsReadOnly(x.Source.Aliases.ToArray()))).ToArray());
        public Result<IMutationLease<AudioAsset>> Replace(TargetHandle<AudioAsset> target,AudioAsset clip,int priority=0)=>_owner.Replace(_context,_gate,target,clip,priority);
        public Result<IMutationLease<float>> Override(TargetHandle<AudioAsset> target,AudioMixParameter parameter,float value,int priority=0)
        {
            var guard=_owner.Guard(_gate);if(guard is not null)return Result<IMutationLease<float>>.Fail(guard);
            if(!MixValid(parameter,value))return Result<IMutationLease<float>>.Fail(new(ErrorCode.InvalidArgument,"Undeclared mix parameter or invalid finite value."));
            var binding=_owner._bindings.Values.SingleOrDefault(x=>x.Target==target);if(binding is null || binding.Retiring)return Result<IMutationLease<float>>.Fail(new(ErrorCode.TargetExpired,"Audio source generation is absent."));
            _owner._busy=true;try
            {
                var result=_owner.MixChannel(binding,parameter).Override(_context.PluginId,value,priority);
                if(!result.Success)return Result<IMutationLease<float>>.Fail(result.Error!);
                if(!_gate.Active){result.Value!.Dispose();return Result<IMutationLease<float>>.Fail(new(ErrorCode.ScopeExpired,"Caller ended during mix mutation."));}
                var lease=new MixLease(_owner,result.Value!,_gate,binding);
                try{return Result<IMutationLease<float>>.Ok(_context.Own<IMutationLease<float>>(lease));}catch{lease.Dispose();throw;}
            }
            catch(Exception e){return Result<IMutationLease<float>>.Fail(Error(e));}finally{_owner._busy=false;_owner.SyncAudit();_owner.Sweep();}
        }
        public Result<AudioClockSnapshot> Read()=>_owner.Execute(_gate,()=>{var clock=_owner._backend.Clock();if(!double.IsFinite(clock.DspSeconds)||clock.DspSeconds<0||!double.IsFinite(clock.HostMonotonicSeconds))throw new InvalidDataException("Invalid audio clock reading.");return clock;});
    }
    private sealed class AssetLease:IAudioAssetLease
    {
        private AudioService? _owner;private readonly Entry _entry;private readonly string _plugin;
        internal AssetLease(AudioService owner,Entry entry,string plugin){_owner=owner;_entry=entry;_plugin=plugin;}
        public AudioAsset Asset=>_entry.Asset;public int SampleFrames=>_entry.Frames;public int Channels=>_entry.Channels;public int Frequency=>_entry.Frequency;
        public bool IsValid=>_owner is {_disposed:false,_retiring:false}&&!_entry.CleanupStarted&&_entry.Owners.GetValueOrDefault(_plugin)>0;
        public void Dispose(){var owner=_owner;if(owner is null)return;owner._main.VerifyAccess();_owner=null;if(owner._disposed||_entry.CleanupStarted)return;_entry.Owners[_plugin]--;_entry.References--;owner.Sweep();}
    }
    private sealed class PlaybackLease:IAudioPlaybackLease
    {
        private AudioService? _owner;private readonly Entry _entry;internal readonly IOwnAudioSource Source;
        private bool _retiring, _stopIssued, _cleanupStarted;
        internal IDisposable? Audit;
        internal PlaybackLease(AudioService owner,Entry entry,IOwnAudioSource source){_owner=owner;_entry=entry;Source=source;}
        public bool IsActive=>!_retiring&&_owner is {_disposed:false}&&Safe(()=>Source.IsPlaying);
        public Result Stop(){if(_owner is null)return Result.Ok();if(!_owner._main.IsMainThread)return Result.Fail(new(ErrorCode.WrongThread,"Audio stop requires the main thread."));try{Dispose();return Result.Ok();}catch(Exception e){return Result.Fail(Error(e));}}
        public void Dispose()
        {
            var owner=_owner;if(owner is null)return;owner._main.VerifyAccess();
            if (!_retiring && !_cleanupStarted && Source.IsPlaying) { _stopIssued = true; Source.Stop(); }
            ObserveCompletion();
            if (_owner is not null) throw new InvalidOperationException("Playback stop or cleanup is unconfirmed; clip remains rooted.");
        }
        internal void BeginRetirement()
        {
            _retiring = true;
            if (!_stopIssued && !_cleanupStarted && Source.IsPlaying) { _stopIssued = true; Source.Stop(); }
            ObserveCompletion();
        }
        internal void ObserveCompletion()
        {
            if (_owner is not { } owner || _cleanupStarted || Source.IsPlaying) return;
            _cleanupStarted = true;
            Source.Dispose(); Audit?.Dispose(); Audit = null;
            _owner = null; owner._playbacks.Remove(this); _entry.References--; owner.Sweep();
        }
    }
    private sealed class ReplacementLease:IMutationLease<AudioAsset>
    {
        private AudioService? _owner;private readonly Entry _entry;private readonly IMutationLease<AudioAsset> _inner;
        internal ReplacementLease(AudioService owner,Entry entry,IMutationLease<AudioAsset> inner){_owner=owner;_entry=entry;_inner=inner;}
        public string LeaseId=>_inner.LeaseId;public MutationKey<AudioAsset> Key=>_inner.Key;public bool IsActive=>_owner is {_disposed:false}&&_inner.IsActive;
        public OperationResult SetValue(AudioAsset value)
        {
            var target=Key.Target.Key;
            if(_owner is null||_owner._disposed)return OperationResult.Fail(ErrorCode.ScopeExpired,"Replacement lease is disposed.",AudioContentCapabilityIds.Replacement,target);
            if(!_owner._main.IsMainThread)return OperationResult.Fail(ErrorCode.WrongThread,"Audio operations require the main thread.",AudioContentCapabilityIds.Replacement,target);
            if(!_inner.IsActive)return OperationResult.Fail(ErrorCode.TargetExpired,"Replacement target is no longer active.",AudioContentCapabilityIds.Replacement,target);
            return OperationResult.Fail(ErrorCode.OperationUnsupported,"Replace a clip by disposing and acquiring a new replacement lease.",AudioContentCapabilityIds.Replacement,target);
        }
        public void Dispose()
        {
            var owner=_owner;if(owner is null)return;owner._main.VerifyAccess();_owner=null;if(owner._disposed||_entry.CleanupStarted)return;
            try{_inner.Dispose();}finally{_entry.References--;foreach(var binding in owner._bindings.Values)owner.Pin(binding);owner.SyncAudit();owner.Sweep();}
        }
    }
    private sealed class MixLease:IMutationLease<float>
    {
        private readonly AudioService _owner;private readonly IMutationLease<float> _inner;private readonly Gate _gate;private readonly Binding _binding;
        internal MixLease(AudioService owner,IMutationLease<float> inner,Gate gate,Binding binding)=>(_owner,_inner,_gate,_binding)=(owner,inner,gate,binding);
        public string LeaseId=>_inner.LeaseId;public MutationKey<float> Key=>_inner.Key;public bool IsActive=>!_binding.Retiring&&_gate.Active&&_inner.IsActive;
        public OperationResult SetValue(float value)
        {
            var error = _owner.Guard(_gate);
            if (error is not null) return OperationResult.Fail(error);
            if (_binding.Retiring) return OperationResult.Fail(ErrorCode.TargetExpired,"Audio source is retiring.");
            try{return _inner.SetValue(value);}finally{_owner.SyncAudit();}
        }
        public void Dispose(){try{_inner.Dispose();}finally{_owner.SyncAudit();}}
    }
    private sealed class Pending:IDisposable { private IDisposable? _value;internal Pending(IDisposable value)=>_value=value;internal IDisposable Take(){var value=_value!;_value=null;return value;}public void Dispose()=>_value?.Dispose(); }
}
internal sealed class UnsupportedAudioException:Exception{internal UnsupportedAudioException(string message):base(message){}}
internal sealed class AudioExpiredException:Exception{internal AudioExpiredException(string message):base(message){}}
internal sealed class AudioScopeException:Exception{internal AudioScopeException(string message):base(message){}}
