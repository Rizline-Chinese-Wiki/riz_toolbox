using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Visual;

internal interface IVisualObject
{
    string Identity { get; }
    VisualAssetKind Kind { get; }
    int Width { get; }
    int Height { get; }
    long EstimatedBytes { get; }
    bool IsAlive { get; }
    IDisposable Retain();
    void DestroyOwned();
}
internal sealed record VisualCandidate(string Identity,string CanonicalBase,TargetMetadata Metadata,
    IReadOnlyList<string> Aliases,Func<IVisualObject> Read,Action<IVisualObject> Write,
    Func<bool> IsAlive,Func<bool> IsEligible,Func<IDisposable> Retain,Func<bool>? IsNativeAlive=null,int DiscoveryPriority=0);
internal interface IVisualBackend
{
    IEnumerable<VisualCandidate> Discover(int maximum);
    IVisualObject CreateTexture(PngDecoder.Image image);
    IVisualObject CreateSprite(IVisualObject texture,float pixelsPerUnit);
    IVisualObject LoadFont(string name);
    IVisualObject CreateMaterial(IVisualObject source,VisualMaterialRequest request);
}

/// <summary>Native objects never cross this boundary. Every replacement uses one reversible mutation channel.</summary>
internal sealed partial class VisualResourceService:IDisposable
{
    internal const int MaximumTargets=128,MaximumScanned=4096,MaximumSide=2048,MaximumPixels=2048*2048;
    private readonly IMainThreadDispatcher _main;private readonly ILogger _log;private readonly IVisualBackend _backend;
    private readonly VisualTargetKind _kind;private readonly VisualFileAccess _files;private readonly VisualResourceBudget _budget;
    private readonly SemanticTargetRegistry _targets;private readonly MutationCoordinator _mutations;
    private readonly Dictionary<string,Binding> _bindings=new(StringComparer.Ordinal);
    // History contains identities and counters only, never unrooted native receivers.
    // Remember recently evicted targets so they cannot jump ahead of a never-admitted tail.
    private readonly Dictionary<string,long> _admissionHistory=new(StringComparer.Ordinal);
    private long _admissionSequence;private string? _admissionNotice;
    private readonly Dictionary<string,AssetEntry> _assets=new(StringComparer.Ordinal);
    private readonly Dictionary<string,AssetEntry> _nativeAssets=new(StringComparer.Ordinal);
    private readonly HashSet<IDisposable> _additionalReservations=new();
    private readonly HashSet<IDisposable> _unconfirmedReservationReleases=new();
    private readonly HashSet<Binding> _retiring=new();
    private readonly VisualResourceRetirement _retirement;
    // One pre-reserved ticket protects this service's bounded asset ledger, including
    // borrowed objects that do not have an owned-object destruction ticket.
    private readonly VisualResourceRetirement.Ticket _responsibility;
    // At most the previous and selected admission candidate can fail after the first
    // failure blocks further acquisition. Target roots have their own binding tickets.
    private readonly List<IDisposable> _unconfirmedCandidateRoots=new(2);
    private bool _disposed,_sweeping,_refreshing;private int _creating;
    private sealed class AssetEntry
    {
        internal VisualAsset Handle=null!;internal IVisualObject Native=null!;internal IDisposable Root=null!;
        internal Dictionary<string,int> LeaseOwners=new(StringComparer.Ordinal);internal bool Owned,Releasing,Released,DestroyIssued,CleanupUnconfirmed;internal int References,ConsumerReferences;internal IDisposable? Budget;internal AssetEntry? Dependency;
        internal VisualResourceRetirement.Ticket? Retirement;
    }
    private sealed class Binding
    {
        internal VisualCandidate Source=null!;internal TargetHandle<VisualAsset> Target=null!;
        internal MutationChannel<VisualAsset> Channel=null!;internal HashSet<AssetEntry> Pins=new(),PossibleAssignments=new();
        internal AssetEntry? RecoveryBaseline,Actual;internal int NativeDepth;internal bool Retiring,Finished;
        internal long AdmissionOrder;internal int DiscoveryPriority;
        internal SharedTargetRoot Lifetime=null!;internal VisualResourceRetirement.Ticket Retirement=null!;
    }
    // Registry invalidation and physical retirement are separate owners of the same native root.
    private sealed class SharedTargetRoot:IDisposable
    {
        private IDisposable? _root;private int _references=1;private readonly VisualResourceRetirement.Ticket _retirement;private readonly string _description;
        internal bool CleanupUnconfirmed{get;private set;}
        internal SharedTargetRoot(IDisposable root,VisualResourceRetirement.Ticket retirement,string description){_root=root;_retirement=retirement;_description=description;}
        internal IDisposable Retain(){_references++;return new RootLease(this);}
        public void Dispose()
        {
            if(_references<=0||--_references!=0)return;
            try{_root?.Dispose();_root=null;}
            catch(Exception error){CleanupUnconfirmed=true;_retirement.Quarantine(_description,this,error);}
        }
        private sealed class RootLease:IDisposable{private SharedTargetRoot? _owner;internal RootLease(SharedTargetRoot owner)=>_owner=owner;public void Dispose(){var owner=_owner;_owner=null;owner?.Dispose();}}
    }
    internal VisualResourceService(IMainThreadDispatcher main,ILogger log,VisualTargetKind kind,IVisualBackend backend,
        VisualFileAccess files,VisualResourceBudget? budget=null,VisualResourceRetirement? retirement=null)
    {
        _main=main;_log=log;_kind=kind;_backend=backend;_files=files;_budget=budget??new();main.VerifyAccess();
        _targets=new(main,log);_mutations=new(_targets,main,log);_targets.Invalidated+=OnIntentTargetInvalidated;_retirement=retirement??new(main,log);
        _responsibility=_retirement.Reserve(_kind);
    }
    internal IVisualResourceService ForPlugin(IPluginContext context)
    {Verify();var gate=context.Own(new ScopeGate(_main,context));return new View(this,context,gate);}
    internal const string CleanupPendingReason="CapabilityUnavailable: VisualCleanupPending: previous native assignments are isolated; resources and budget are retained.";
    internal const string CleanupQuarantinedReason="CapabilityUnavailable: VisualCleanupQuarantined: an unknown root/budget free remains isolated; process restart is required.";
    internal bool CleanupPending { get { _main.VerifyAccess();return _retirement.HasPending(_kind); } }
    internal bool CleanupQuarantined { get { _main.VerifyAccess();return _retirement.HasQuarantined(_kind); } }
    private void Verify(bool allowPending=false){_main.VerifyAccess();if(_disposed)throw new ObjectDisposedException(nameof(VisualResourceService));if(!allowPending&&CleanupPending)throw new VisualOperationException(ErrorCode.CapabilityUnavailable,CleanupPendingReason,!CleanupQuarantined);}
    internal int Refresh(bool discover=true)
    {
        _main.VerifyAccess();if(_disposed)throw new ObjectDisposedException(nameof(VisualResourceService));
        if(_refreshing)return _bindings.Count;
        _refreshing=true;
        try
        {
            foreach(var binding in _bindings.Values.ToArray())
                if(!Safe(binding.Source.IsAlive))Remove(binding,false);else if(!Safe(binding.Source.IsEligible)||!binding.Channel.Valid)Remove(binding,true);
            if(discover&&!CleanupPending)DiscoverTargets();
            foreach(var result in _mutations.RefreshAll())if(!result.Success)_log.Log(LogLevel.Warning,"Visual resource refresh: "+result.Error);
            foreach(var binding in _bindings.Values.ToArray())
            {
                if(!binding.Channel.Valid)Remove(binding,true);
                else{CaptureActual(binding);PinSnapshot(binding);}
            }
            Sweep();RefreshIntents(discover);return _bindings.Count;
        }
        finally{_refreshing=false;MaybeReleaseResponsibility();}
    }
    private void DiscoverTargets()
    {
        // Only bindings present before this scan can be replaced. Initial discovery fills
        // capacity without immediately churning targets that were just made visible.
        var incumbents=_bindings.Values.Where(PotentiallyIdle).ToArray();
        VisualCandidate? waiting=null;IDisposable? waitingRoot=null;long waitingOrder=0;var deferred=0;
        try
        {
            var scanned=0;
            foreach(var candidate in _backend.Discover(MaximumScanned))
            {
                if(++scanned>MaximumScanned||_disposed)break;
                if(candidate.Metadata.Template||!candidate.Metadata.Active||!Safe(candidate.IsAlive)||!Safe(candidate.IsEligible))continue;
                if(_bindings.TryGetValue(candidate.Identity,out var existing))
                {
                    _targets.SetAliases(existing.Target,candidate.Aliases);_targets.UpdateMetadata(existing.Target,candidate.Metadata);
                    existing.DiscoveryPriority=candidate.DiscoveryPriority;continue;
                }
                // A retirement can start inside native discovery/retention callbacks.
                // Existing semantic updates may continue, but no additional root/binding may enter.
                if(CleanupPending){deferred++;continue;}
                if(_bindings.Count<MaximumTargets){Bind(candidate);continue;}
                deferred++;
                if(!incumbents.Any(x=>x.DiscoveryPriority<=candidate.DiscoveryPriority))continue;
                var order=_admissionHistory.GetValueOrDefault(candidate.Identity);
                if(waiting is not null&&(candidate.DiscoveryPriority<waiting.DiscoveryPriority
                    ||(candidate.DiscoveryPriority==waiting.DiscoveryPriority&&order>=waitingOrder)))continue;
                IDisposable root;
                try{root=candidate.Retain();}
                catch(Exception error){Log("Visual admission candidate retention failed.",error);continue;}
                var previous=waitingRoot;waiting=candidate;waitingRoot=root;waitingOrder=order;ReleaseCandidateRoot(previous);
            }
            // At most one replacement per discovery. Higher priority wins; equal-priority
            // waiting targets and victims rotate by their last successful admission.
            if(waiting is not null&&!_disposed&&!CleanupPending)
            {
                var victim=incumbents.Where(x=>x.DiscoveryPriority<=waiting.DiscoveryPriority)
                    .OrderBy(x=>x.DiscoveryPriority).ThenBy(x=>x.AdmissionOrder).FirstOrDefault(CanEvict);
                if(victim is not null&&!_disposed&&!CleanupPending&&Safe(waiting.IsAlive)&&Safe(waiting.IsEligible))
                {
                    Remove(victim,true);
                    if(!_disposed&&!CleanupPending&&_bindings.Count<MaximumTargets&&Safe(waiting.IsAlive)&&Safe(waiting.IsEligible))
                    {
                        var root=waitingRoot;waitingRoot=null;Bind(waiting,root);
                        if(_bindings.ContainsKey(waiting.Identity))deferred--;
                    }
                }
            }
            var notice=deferred==0?null:$"VisualTargetCapacity: {deferred} candidate(s) deferred; " +
                (CleanupPending?"native cleanup is pending.":"admission is bounded to one safe replacement; higher priority, held and uncertain assignments remain protected.");
            if(notice!=_admissionNotice){_admissionNotice=notice;if(notice is not null)Log(notice);}
        }
        finally{ReleaseCandidateRoot(waitingRoot);}
    }
    private bool PotentiallyIdle(Binding binding)=>!binding.Retiring&&!binding.Finished&&binding.NativeDepth==0
        &&binding.Channel.Valid&&binding.PossibleAssignments.Count==0&&binding.Channel.Describe().Holders.Count==0;
    private bool CanEvict(Binding binding)
    {
        if(_disposed||CleanupPending||!_bindings.TryGetValue(binding.Source.Identity,out var current)
            ||!ReferenceEquals(current,binding)||!PotentiallyIdle(binding))return false;
        var known=CaptureActual(binding);PinSnapshot(binding);
        // An owned resource can be a game-adopted baseline with no active mutation lease.
        // Native read-back and callbacks may also change ownership since the initial snapshot.
        return known&&!_disposed&&!CleanupPending&&PotentiallyIdle(binding)&&binding.Actual is { Owned:false }
            &&_bindings.TryGetValue(binding.Source.Identity,out current)&&ReferenceEquals(current,binding);
    }
    private void ReleaseCandidateRoot(IDisposable? root)
    {
        try{root?.Dispose();}
        catch(Exception error)
        {
            _unconfirmedCandidateRoots.Add(root!);
            _responsibility.Quarantine("admission candidate root",this,error);
        }
    }
    private void RememberAdmission(Binding binding)
    {
        _admissionSequence=binding.AdmissionOrder;_admissionHistory[binding.Source.Identity]=binding.AdmissionOrder;
        // A scan-window-sized history forgets early identities before a larger stable
        // native array has been served. Cover the complete supported array population.
        if(_admissionHistory.Count<=RizToolbox.Runtime.NativeArrayScanCursor.MaximumArrayLength)return;
        var oldest=_admissionHistory.Where(x=>!_bindings.ContainsKey(x.Key)).OrderBy(x=>x.Value).First();
        _admissionHistory.Remove(oldest.Key);
    }
    private AssetEntry Observe(IVisualObject native)
    {
        if(_nativeAssets.TryGetValue(native.Identity,out var entry))return entry;
        if(_assets.Count>=MaximumTargets*8+VisualResourceBudget.MaximumTotalAssets)throw new VisualOperationException(ErrorCode.QuotaExceeded,"Too many retained visual identities.");
        var root=native.Retain();
        entry=new(){Native=native,Handle=new(Guid.NewGuid().ToString("N"),native.Kind),Root=root};
        _assets.Add(entry.Handle.Id,entry);_nativeAssets.Add(native.Identity,entry);return entry;
    }
    private AssetEntry Find(VisualAsset asset)
    {
        if(asset is null)throw new VisualOperationException(ErrorCode.InvalidArgument,"A visual asset is required.");
        if(!_assets.TryGetValue(asset.Id,out var entry)||entry.Handle!=asset||entry.Releasing||!entry.Native.IsAlive)throw new VisualOperationException(ErrorCode.TargetExpired,"Visual asset is no longer alive.");
        return entry;
    }
    private AssetEntry FindOwned(string owner,VisualAsset asset)
    {
        var entry=Find(asset);if(!entry.LeaseOwners.TryGetValue(owner,out var leases)||leases<=0)throw new VisualOperationException(ErrorCode.TargetExpired,"Only the caller's live asset lease may be used.");return entry;
    }
    private void Bind(VisualCandidate candidate,IDisposable? retainedRoot=null)
    {
        SharedTargetRoot? lifetime=null;IDisposable? registryRoot=null;TargetHandle<VisualAsset>? handle=null;
        VisualResourceRetirement.Ticket? retirement=null;
        try
        {
            Verify();
            var admissionOrder=checked(_admissionSequence+1);
            retirement=_retirement.Reserve(_kind,true);lifetime=new(retainedRoot??candidate.Retain(),retirement,"target root "+candidate.Identity);retainedRoot=null;registryRoot=lifetime.Retain();
            Verify();
            var registered=_targets.Register(candidate.CanonicalBase+"/binding/"+Guid.NewGuid().ToString("N"),
                ()=>Observe(candidate.Read()).Handle,candidate.IsAlive,registryRoot,candidate.Metadata,candidate.Aliases);
            if(!registered.Success)throw new InvalidOperationException(registered.Error!.Message);handle=registered.Value!;registryRoot=null;
            var binding=new Binding{Source=candidate,Target=handle,Lifetime=lifetime,Retirement=retirement,AdmissionOrder=admissionOrder,DiscoveryPriority=candidate.DiscoveryPriority};
            var channel=_mutations.Register(handle,Property,value=>WriteTracked(binding,value),MutationStrategy.PriorityOverride,
                ExternalWritePolicy.AdoptAndReapply,value=>{try{return Find(value).Native.Kind==ExpectedKind?null:"AssetKindMismatch";}catch(Exception e){return e.Message;}});
            if(!channel.Success)throw new InvalidOperationException(channel.Error!.Message);
            binding.Channel=channel.Value!;binding.RecoveryBaseline=Observe(candidate.Read());binding.Actual=binding.RecoveryBaseline;Verify();
            _bindings.Add(candidate.Identity,binding);RememberAdmission(binding);PinSnapshot(binding);lifetime=null;retirement=null;
        }
        catch(Exception e){if(handle is not null)_targets.Invalidate(handle);registryRoot?.Dispose();lifetime?.Dispose();retirement?.Dispose();Log("Visual binding refused: "+e.Message,e);Sweep();}
        finally{ReleaseCandidateRoot(retainedRoot);}
    }
    private string Property=>_kind.ToString().ToLowerInvariant();
    private string Capability=>VisualModule.Capability(_kind);
    private OperationError Error(ErrorCode code,string message,string? targetId=null,bool retryable=false)
        =>new(code,message,Capability,targetId,retryable);
    private OperationError WithContext(OperationError error,string? targetId=null)
        =>error with {CapabilityId=error.CapabilityId??Capability,TargetId=error.TargetId??targetId};
    private OperationError Failure(Exception failure,ErrorCode fallback,string? targetId=null)
    {
        var code=failure switch {
            VisualOperationException known=>known.Code,
            ObjectDisposedException=>ErrorCode.ScopeExpired,
            RizToolbox.Runtime.TargetExpiredException=>ErrorCode.TargetExpired,
            NotSupportedException=>ErrorCode.UnsupportedSignature,
            ArgumentException or InvalidDataException=>ErrorCode.InvalidArgument,
            _=>fallback
        };
        return Error(code,code==ErrorCode.ResourceLoadFailed?LoadFailure(failure):failure.Message,targetId,
            failure is VisualOperationException {Retryable:true});
    }
    private OperationError CleanupError(string? targetId=null)
        =>Error(ErrorCode.CapabilityUnavailable,CleanupQuarantined?CleanupQuarantinedReason:CleanupPendingReason,targetId,!CleanupQuarantined);
    private VisualAssetKind ExpectedKind=>_kind switch{VisualTargetKind.Sprite=>VisualAssetKind.Sprite,VisualTargetKind.Font=>VisualAssetKind.Font,_=>VisualAssetKind.Material};
    private void PinSnapshot(Binding binding)
    {
        if(binding.Finished)return;
        var current=new HashSet<AssetEntry>(binding.PossibleAssignments);
        if(binding.RecoveryBaseline is not null)current.Add(binding.RecoveryBaseline);
        if(binding.Actual is not null)current.Add(binding.Actual);
        if(!binding.Retiring)
        {
            var snapshot=binding.Channel.Describe();var handles=snapshot.Holders.Select(x=>x.Candidate).Append(snapshot.Baseline).Append(snapshot.Effective).Where(x=>x is not null);
            foreach(var handle in handles)if(_assets.TryGetValue(handle!.Id,out var entry))current.Add(entry);
        }
        foreach(var entry in current.Except(binding.Pins))entry.References++;
        foreach(var entry in binding.Pins.Except(current))entry.References--;
        binding.Pins=current;
    }
    private void WriteTracked(Binding binding,VisualAsset value)
    {
        // Register every possible assignment before entering user/native code. Core's snapshot
        // intentionally loses mutation ownership on rollback failure; it is not an asset safety ledger.
        var desired=Find(value);binding.NativeDepth++;
        try
        {
            var before=Observe(binding.Source.Read());
            if(!binding.PossibleAssignments.Contains(before))binding.RecoveryBaseline=before;
            binding.Actual=before;if(desired.Owned)binding.PossibleAssignments.Add(desired);
            PinSnapshot(binding);binding.Source.Write(desired.Native);
        }
        finally
        {
            // A synchronous scope/module withdrawal may queue retirement while this write is running.
            CaptureActual(binding);binding.NativeDepth--;PinSnapshot(binding);
        }
    }
    private bool CaptureActual(Binding binding)
    {
        try
        {
            var actual=Observe(binding.Source.Read());binding.Actual=actual;
            if(!binding.PossibleAssignments.Contains(actual))binding.RecoveryBaseline=actual;
            // Other registered holders/baselines remain pinned by the channel. Only proven detached
            // write candidates leave this independent uncertain-assignment set.
            binding.PossibleAssignments.RemoveWhere(x=>!ReferenceEquals(x,actual));
            return true;
        }
        catch{return false;}
    }
    private void Remove(Binding binding,bool restore)
    {
        if(binding.Retiring||binding.Finished)return;
        binding.Retiring=true;_bindings.Remove(binding.Source.Identity);_retiring.Add(binding);
        // SharedTargetRoot's binding reference survives registry invalidation. Never treat a failed
        // liveness/read callback as proof that a native receiver has died.
        try{if(restore)binding.Channel.Dispose();}catch(Exception e){Log("Visual channel cleanup failed; native assignments retained.",e);}
        _targets.Invalidate(binding.Target);PinSnapshot(binding);
        binding.Retirement.Retire(_kind+"/"+binding.Source.Identity,()=>TryRetireBinding(binding),binding.NativeDepth==0);
    }
    private bool TryRetireBinding(Binding binding)
    {
        if(binding.Finished)return true;
        if(binding.NativeDepth!=0)return false;
        bool alive;
        try{alive=(binding.Source.IsNativeAlive??binding.Source.IsAlive)();}catch{return false;}
        if(alive)
        {
            if(!CaptureActual(binding)){PinSnapshot(binding);return false;}
            var current=binding.Actual!;var baseline=binding.RecoveryBaseline;
            if(current.Owned)
            {
                // An owned resource adopted as a game baseline cannot be destroyed while the
                // game still holds it. Wait for a foreign replacement or verified target death.
                if(!binding.PossibleAssignments.Contains(current)||baseline is null||ReferenceEquals(current,baseline))
                {PinSnapshot(binding);return false;}
                binding.NativeDepth++;
                try
                {
                    // Read again immediately before writing; do not overwrite an intervening game value.
                    var check=Observe(binding.Source.Read());
                    if(!ReferenceEquals(check,current)){CaptureActual(binding);PinSnapshot(binding);return false;}
                    binding.Source.Write(baseline.Native);
                }
                catch{CaptureActual(binding);PinSnapshot(binding);return false;}
                finally{binding.NativeDepth--;}
                if(!CaptureActual(binding)||binding.Actual!.Owned){PinSnapshot(binding);return false;}
            }
        }
        binding.Lifetime.Dispose();
        if(binding.Lifetime.CleanupUnconfirmed)return false;
        binding.Finished=true;_retiring.Remove(binding);
        foreach(var entry in binding.Pins)entry.References--;binding.Pins.Clear();binding.PossibleAssignments.Clear();
        binding.Actual=null;binding.RecoveryBaseline=null;Sweep();MaybeReleaseResponsibility();return true;
    }
    private void Log(string message,Exception? error=null){try{_log.Log(LogLevel.Warning,message,error);}catch{}}
    private void Sweep()
    {
        if(_sweeping)return;_sweeping=true;
        // Dependencies may become unreferenced when a sprite is destroyed, so drain repeatedly.
        try{while(true){var dead=_assets.Values.Where(x=>x.References==0&&!x.Releasing).ToArray();if(dead.Length==0)break;foreach(var entry in dead)ReleaseEntry(entry);}}
        finally{_sweeping=false;}
    }
    private void ReleaseEntry(AssetEntry entry)
    {
        if(entry.Releasing||entry.Released||entry.References!=0)return;
        entry.Releasing=true;
        if(entry.Owned)entry.Retirement!.Retire("owned "+entry.Native.Kind+"/"+entry.Handle.Id,()=>TryReleaseEntry(entry));
        else FinishReleaseEntry(entry);
    }
    private bool TryReleaseEntry(AssetEntry entry)
    {
        if(entry.Released)return true;if(entry.CleanupUnconfirmed)return false;
        // Destruction can be deferred by Unity or throw before it changes the native object.
        // Retain its root, dependency and reservation until native liveness proves it is gone.
        try
        {
            if(entry.Native.IsAlive&&!entry.DestroyIssued)
            {
                try{entry.Native.DestroyOwned();entry.DestroyIssued=true;}
                catch(Exception e){Log("Owned visual destruction failed; root and budget retained.",e);}
            }
            if(entry.Native.IsAlive)return false;
        }
        catch(Exception e){Log("Owned visual liveness is unavailable; root and budget retained.",e);return false;}
        return FinishReleaseEntry(entry);
    }
    private bool FinishReleaseEntry(AssetEntry entry)
    {
        if(entry.Released)return true;if(entry.CleanupUnconfirmed)return false;
        try{entry.Root.Dispose();}
        catch(Exception error){QuarantineEntry(entry,"asset root",error);return false;}
        try{entry.Budget?.Dispose();}
        catch(Exception error){QuarantineEntry(entry,"asset budget",error);return false;}
        entry.Released=true;
        _assets.Remove(entry.Handle.Id);_nativeAssets.Remove(entry.Native.Identity);
        if(entry.Dependency is not null){entry.Dependency.References--;entry.Dependency=null;}
        Sweep();MaybeReleaseResponsibility();return true;
    }
    private void QuarantineEntry(AssetEntry entry,string stage,Exception error)
    {
        entry.CleanupUnconfirmed=true;
        // Keep the original root/budget/dependency graph and its quota. Unknown free
        // outcomes must not be retried, even when the Unity object is already dead.
        (entry.Retirement??_responsibility).Quarantine(stage+" "+entry.Native.Kind+"/"+entry.Handle.Id,this,error);
    }
    private void MaybeReleaseResponsibility()
    {
        if(_disposed&&!_refreshing&&_creating==0&&_assets.Count==0&&_bindings.Count==0&&_retiring.Count==0&&_additionalReservations.Count==0&&_unconfirmedCandidateRoots.Count==0)
            _responsibility.Dispose();
    }
    private void ReleaseReservation(IDisposable reservation)
    {
        if(_unconfirmedReservationReleases.Contains(reservation))return;
        try{reservation.Dispose();_additionalReservations.Remove(reservation);}
        catch(Exception error)
        {
            _additionalReservations.Add(reservation);_unconfirmedReservationReleases.Add(reservation);
            _responsibility.Quarantine("additional asset budget",this,error);
        }
    }
    private OperationResult<IVisualAssetLease> Create(string owner,ScopeGate gate,long estimate,Func<IVisualObject> create,bool owned=true,AssetEntry? dependency=null,string? targetId=null)
    {
        IDisposable? reservation=null;VisualResourceRetirement.Ticket? retirement=null;AssetEntry? created=null;var dependencyHeld=false;
        _creating++;
        try
        {
            Verify();
            // Keep the input texture alive even if creation synchronously revokes the caller's asset lease.
            if(dependency is not null){dependency.References++;dependencyHeld=true;}
            reservation=_budget.Reserve(owner,estimate);if(owned)retirement=_retirement.Reserve(_kind);var native=create();
            // Several scopes may borrow the same built-in font; ownership is tracked per live caller lease.
            if(_nativeAssets.TryGetValue(native.Identity,out var existing))
            {
                if(owned)throw new VisualOperationException(ErrorCode.InternalError,"Created object reused a live identity.");
                Verify();if(existing.Releasing)throw new VisualOperationException(ErrorCode.TargetExpired,"Shared resource is retiring.");
                _additionalReservations.Add(reservation);var extra=reservation;reservation=null;
                existing.References++;existing.ConsumerReferences++;existing.LeaseOwners[owner]=existing.LeaseOwners.GetValueOrDefault(owner)+1;return OperationResult<IVisualAssetLease>.Ok(new AssetLease(this,gate,existing,owner,extra));
            }
            var entry=Observe(native);created=entry;entry.Owned=owned;entry.Budget=reservation;entry.Retirement=retirement;reservation=null;retirement=null;
            if(dependency is not null){entry.Dependency=dependency;dependencyHeld=false;}
            Verify();entry.LeaseOwners[owner]=1;entry.References++;entry.ConsumerReferences++;
            return OperationResult<IVisualAssetLease>.Ok(new AssetLease(this,gate,entry,owner));
        }
        catch(Exception e)
        {
            if(reservation is not null)ReleaseReservation(reservation);retirement?.Dispose();if(created is not null)ReleaseEntry(created);
            return OperationResult<IVisualAssetLease>.Fail(Failure(e,ErrorCode.ResourceLoadFailed,targetId));
        }
        finally{if(dependencyHeld){dependency!.References--;Sweep();}_creating--;MaybeReleaseResponsibility();}
    }
    private OperationResult<IMutationLease<VisualAsset>> Override(IPluginContext context,ScopeGate gate,TargetHandle<VisualAsset> target,VisualAsset asset,int priority)
    {
        Verify();AssetEntry entry;
        if(target is null)return OperationResult<IMutationLease<VisualAsset>>.Fail(Error(ErrorCode.InvalidArgument,"A visual target is required."));
        try{entry=FindOwned(context.PluginId,asset);if(entry.Native.Kind!=ExpectedKind)throw new VisualOperationException(ErrorCode.TargetTypeMismatch,"This target requires "+ExpectedKind);}
        catch(Exception e){return OperationResult<IMutationLease<VisualAsset>>.Fail(Failure(e,ErrorCode.InternalError,target?.Key));}
        var binding=_bindings.Values.SingleOrDefault(x=>x.Target==target);
        if(binding is null)return OperationResult<IMutationLease<VisualAsset>>.Fail(Error(ErrorCode.TargetMissing,"Visual target generation is absent.",target?.Key));
        entry.References++;entry.ConsumerReferences++;
        var result=binding.Channel.Override(context.PluginId,asset,priority);
        if(!result.Success){if(!_disposed){entry.References--;entry.ConsumerReferences--;}if(!binding.Channel.Valid)Remove(binding,false);else PinSnapshot(binding);Sweep();return OperationResult<IMutationLease<VisualAsset>>.Fail(WithContext(result.Error!,target.Key));}
        PinSnapshot(binding);return OperationResult<IMutationLease<VisualAsset>>.Ok(new OverrideLease(this,gate,entry,result.Value!));
    }
    public void Dispose()
    {
        _main.VerifyAccess();if(_disposed)return;_disposed=true;
        StopIntents(Error(ErrorCode.ScopeExpired,"The visual provider has ended."));
        foreach(var binding in _bindings.Values.ToArray())Remove(binding,true);_mutations.Dispose();_targets.Dispose();_admissionHistory.Clear();
        // Logical leases expire immediately. Physical attachment pins and failed destruction keep
        // their native roots/dependencies/budget in process retirement after this service is gone.
        foreach(var entry in _assets.Values){entry.References-=entry.ConsumerReferences;entry.ConsumerReferences=0;entry.LeaseOwners.Clear();}
        foreach(var reservation in _additionalReservations.ToArray())ReleaseReservation(reservation);
        Sweep();MaybeReleaseResponsibility();
    }
    private static bool Safe(Func<bool> predicate){try{return predicate();}catch{return false;}}
    private static string LoadFailure(Exception error)=>"资源加载失败，原资源保留。 "+error.Message;
    private sealed class ScopeGate:IDisposable
    {
        private readonly IMainThreadDispatcher _hostMain,_scopeMain;private readonly IPluginContext _context;private bool _active=true;
        private readonly IPluginScope? _scope;private readonly CancellationToken _lifetime;
        internal ScopeGate(IMainThreadDispatcher hostMain,IPluginContext context)
        { _hostMain=hostMain;_scopeMain=context.MainThread;_context=context;_scope=context as IPluginScope;_lifetime=context is IScopedPluginContext scoped?scoped.Lifetime:default; }
        internal bool SameScope(ScopeGate other)=>ReferenceEquals(_context,other._context);
        // IsMainThread, active state and the captured cancellation token are
        // scalar checks. In particular, an expired ScopedDispatcher.VerifyAccess
        // must not throw before a result-returning service can report its code.
        internal ErrorCode? Check()
        {
            if(!_hostMain.IsMainThread||!_scopeMain.IsMainThread)return ErrorCode.WrongThread;
            return !_active||_scope?.IsActive==false||_lifetime.IsCancellationRequested?ErrorCode.ScopeExpired:null;
        }
        internal void Verify(){_scopeMain.VerifyAccess();if(!_active)throw new ObjectDisposedException("VisualPluginScope");}
        // The host deactivates the whole scope tree before disposing owned resources.
        // Cleanup requires its thread, while user operations still require a live scope.
        public void Dispose(){_hostMain.VerifyAccess();_active=false;}
    }
    private sealed class View:IVisualResourceService
    {
        private readonly VisualResourceService _owner;private readonly IPluginContext _context;private readonly ScopeGate _gate;
        internal View(VisualResourceService owner,IPluginContext context,ScopeGate gate){_owner=owner;_context=context;_gate=gate;}
        private void Verify(){_gate.Verify();_owner.Verify(allowPending:true);}
        private OperationError? Check(string? targetId=null)
        {
            if(_gate.Check() is { } code)return _owner.Error(code,code==ErrorCode.WrongThread?
                "Visual operations require the Unity main thread.":"The visual plugin scope has ended.",targetId);
            return _owner._disposed?_owner.Error(ErrorCode.ScopeExpired,"The visual provider has ended.",targetId):null;
        }
        // Retirement can begin inside a lease disposal before the next adapter tick withdraws
        // the capability. Existing views refuse this expected transition without blaming the
        // plugin callback. GetTargets has no result envelope and retains its throwing
        // lifetime/thread contract; result-returning methods use the scalar Check first.
        public IReadOnlyList<VisualTarget> GetTargets(){Verify();return _owner.CleanupPending?Array.Empty<VisualTarget>():_owner._targets.GetTargets().Select(x=>new VisualTarget(new(x.Key,x.Generation),_owner._kind,x.Metadata,x.Aliases)).ToArray();}
        public TargetMatch<VisualAsset> Resolve(TargetQuery query)
        {
            if(Check(query?.Key) is { } error)return new(TargetMatchKind.Missing,null,Array.Empty<TargetDescriptor>(),error);
            if(query is null)return new(TargetMatchKind.Missing,null,Array.Empty<TargetDescriptor>(),_owner.Error(ErrorCode.InvalidArgument,"A visual target query is required."));
            if(_owner.CleanupPending)return new(TargetMatchKind.Missing,null,Array.Empty<TargetDescriptor>(),_owner.CleanupError(query?.Key));
            var result=_owner._targets.Resolve<VisualAsset>(query);
            return result.Error is null?result:result with {Error=_owner.WithContext(result.Error,query?.Key)};
        }
        private OperationResult<T> Own<T>(OperationResult<T> result,string? targetId=null)where T:IDisposable
        {
            if(!result.Success)return result;
            try{Verify();_owner.Verify();return OperationResult<T>.Ok(_context.Own(result.Value!));}
            catch(Exception error)
            {
                // Backend callbacks can revoke a scope before synchronous creation/assignment returns.
                try{result.Value!.Dispose();}catch(Exception cleanup){_owner._log.Log(LogLevel.Warning,"Visual scope rollback failed.",cleanup);}
                return OperationResult<T>.Fail(_owner.Failure(error,ErrorCode.ScopeExpired,targetId));
            }
        }
        public OperationResult<IVisualAssetLease> LoadPng(VisualFile file)
        {
            if(Check(file?.RelativePath) is { } error)return OperationResult<IVisualAssetLease>.Fail(error);
            if(file is null)return OperationResult<IVisualAssetLease>.Fail(_owner.Error(ErrorCode.InvalidArgument,"A visual resource file is required."));
            if(_owner.CleanupPending)return OperationResult<IVisualAssetLease>.Fail(_owner.CleanupError(file?.RelativePath));
            if(_owner._kind!=VisualTargetKind.Sprite)return OperationResult<IVisualAssetLease>.Fail(_owner.Error(ErrorCode.OperationUnsupported,"PNG assets belong to the sprite module.",file?.RelativePath));
            try{var encoded=_owner._files.Read(_context.PluginId,file);var image=PngDecoder.Decode(encoded,MaximumSide,MaximumPixels,64L*1024*1024,VisualFileAccess.MaximumFileBytes);
                return Own(_owner.Create(_context.PluginId,_gate,(long)image.Width*image.Height*4,()=>_owner._backend.CreateTexture(image),targetId:file.RelativePath),file.RelativePath);}
            catch(VisualOperationException e){return OperationResult<IVisualAssetLease>.Fail(_owner.Failure(e,ErrorCode.ResourceLoadFailed,file?.RelativePath));}
            catch(Exception e){return OperationResult<IVisualAssetLease>.Fail(_owner.Error(ErrorCode.ResourceLoadFailed,LoadFailure(e),file?.RelativePath));}
        }
        public OperationResult<IVisualAssetLease> CreateSprite(VisualAsset texture,float pixelsPerUnit=100)
        {
            if(Check(texture?.Id) is { } error)return OperationResult<IVisualAssetLease>.Fail(error);
            if(texture is null)return OperationResult<IVisualAssetLease>.Fail(_owner.Error(ErrorCode.InvalidArgument,"A texture asset is required."));
            if(_owner.CleanupPending)return OperationResult<IVisualAssetLease>.Fail(_owner.CleanupError(texture?.Id));
            if(_owner._kind!=VisualTargetKind.Sprite)return OperationResult<IVisualAssetLease>.Fail(_owner.Error(ErrorCode.OperationUnsupported,"Sprite creation belongs to the sprite module.",texture?.Id));
            if(!float.IsFinite(pixelsPerUnit)||pixelsPerUnit<=0||pixelsPerUnit>10000)return OperationResult<IVisualAssetLease>.Fail(_owner.Error(ErrorCode.InvalidArgument,"Finite pixelsPerUnit in (0,10000] is required.",texture?.Id));
            try{var entry=_owner.FindOwned(_context.PluginId,texture);if(entry.Native.Kind!=VisualAssetKind.Texture2D)throw new VisualOperationException(ErrorCode.TargetTypeMismatch,"A Texture2D lease is required.");
                return Own(_owner.Create(_context.PluginId,_gate,1024,()=>_owner._backend.CreateSprite(entry.Native,pixelsPerUnit),dependency:entry,targetId:texture.Id),texture.Id);}
            catch(Exception e){return OperationResult<IVisualAssetLease>.Fail(_owner.Failure(e,ErrorCode.ResourceLoadFailed,texture?.Id));}
        }
        public OperationResult<IVisualAssetLease> LoadBuiltinFont(string name)
        {
            if(Check(name) is { } error)return OperationResult<IVisualAssetLease>.Fail(error);
            if(name is null)return OperationResult<IVisualAssetLease>.Fail(_owner.Error(ErrorCode.InvalidArgument,"A built-in font name is required."));
            if(_owner.CleanupPending)return OperationResult<IVisualAssetLease>.Fail(_owner.CleanupError(name));
            if(_owner._kind!=VisualTargetKind.Font||name is not("Arial.ttf" or "LegacyRuntime.ttf"))return OperationResult<IVisualAssetLease>.Fail(_owner.Error(ErrorCode.OperationUnsupported,"Only Arial.ttf and LegacyRuntime.ttf are declared.",name));
            return Own(_owner.Create(_context.PluginId,_gate,0,()=>_owner._backend.LoadFont(name),false,targetId:name),name);
        }
        public OperationResult<IVisualAssetLease> CreateMaterial(VisualMaterialRequest request)
        {
            if(Check(request?.Source?.Key) is { } error)return OperationResult<IVisualAssetLease>.Fail(error);
            if(request?.Source is null)return OperationResult<IVisualAssetLease>.Fail(_owner.Error(ErrorCode.InvalidArgument,"A material source target is required."));
            if(_owner.CleanupPending)return OperationResult<IVisualAssetLease>.Fail(_owner.CleanupError(request?.Source?.Key));
            if(_owner._kind!=VisualTargetKind.Material)return OperationResult<IVisualAssetLease>.Fail(_owner.Error(ErrorCode.OperationUnsupported,"Material creation belongs to the material module.",request?.Source?.Key));
            try{var binding=_owner._bindings.Values.SingleOrDefault(x=>x.Target==request.Source)??throw new VisualOperationException(ErrorCode.TargetMissing,"Source generation is absent.");
                var source=binding.Source.Read();return Own(_owner.Create(_context.PluginId,_gate,64*1024,()=>_owner._backend.CreateMaterial(source,request),targetId:request.Source.Key),request.Source.Key);}
            catch(Exception e){return OperationResult<IVisualAssetLease>.Fail(_owner.Failure(e,ErrorCode.ResourceLoadFailed,request.Source.Key));}
        }
        public OperationResult<IMutationLease<VisualAsset>> Override(TargetHandle<VisualAsset> target,VisualAsset asset,int priority=0)
        {
            if(Check(target?.Key) is { } error)return OperationResult<IMutationLease<VisualAsset>>.Fail(error);
            if(target is null||asset is null)return OperationResult<IMutationLease<VisualAsset>>.Fail(_owner.Error(ErrorCode.InvalidArgument,"A visual target and asset are required.",target?.Key));
            return _owner.CleanupPending?OperationResult<IMutationLease<VisualAsset>>.Fail(_owner.CleanupError(target.Key)):Own(_owner.Override(_context,_gate,target,asset,priority),target.Key);
        }
        public OperationResult<IVisualBindingIntent> BindOverride(TargetQuery query,IVisualAssetLease asset,int priority=0)
        {
            if(Check(query?.Key) is { } error)return OperationResult<IVisualBindingIntent>.Fail(error);
            if(query is null||asset is null)return OperationResult<IVisualBindingIntent>.Fail(_owner.Error(ErrorCode.InvalidArgument,"A visual binding query and asset lease are required.",query?.Key));
            return _owner.CreateIntent(_context,_gate,query,asset,priority);
        }
    }
    private sealed class AssetLease:IVisualAssetLease
    {
        private VisualResourceService? _owner;private readonly VisualResourceService _provider;private readonly ScopeGate _gate;private readonly AssetEntry _entry;private readonly string _plugin;private readonly IDisposable? _extra;private readonly HashSet<BindingIntent> _intents=new();
        internal AssetLease(VisualResourceService owner,ScopeGate gate,AssetEntry entry,string plugin,IDisposable? extra=null){_owner=owner;_provider=owner;_gate=gate;_entry=entry;_plugin=plugin;_extra=extra;}
        internal bool BelongsTo(VisualResourceService provider,ScopeGate gate)=>ReferenceEquals(_provider,provider)&&_gate.SameScope(gate);
        internal bool HasEnded=>_owner is null||_provider._disposed;
        internal AssetEntry Entry=>_entry;
        internal void Attach(BindingIntent intent)=>_intents.Add(intent);
        internal void Detach(BindingIntent intent)=>_intents.Remove(intent);
        public VisualAsset Asset=>_entry.Handle;public int Width=>_entry.Native.Width;public int Height=>_entry.Native.Height;public long EstimatedBytes=>_entry.Native.EstimatedBytes;
        public bool IsValid{get{_owner?._main.VerifyAccess();return _owner is { _disposed:false }&&_entry.Native.IsAlive;}}
        public void Dispose()
        {
            var owner=_owner;if(owner is null)return;owner._main.VerifyAccess();_owner=null;
            foreach(var intent in _intents.ToArray())intent.StopCore(owner.Error(ErrorCode.TargetExpired,"The input visual asset lease has ended.",intent.LastSnapshot.Target?.Key??intent.LastSnapshot.Query.Key));
            if(owner._disposed)return;if(_extra is not null)owner.ReleaseReservation(_extra);_entry.References--;_entry.ConsumerReferences--;_entry.LeaseOwners[_plugin]--;owner.Sweep();
        }
    }
    private sealed class OverrideLease:IMutationLease<VisualAsset>
    {
        private VisualResourceService? _owner;private readonly ScopeGate _gate;private readonly AssetEntry _entry;private readonly IMutationLease<VisualAsset> _inner;private readonly string _capability;
        internal OverrideLease(VisualResourceService owner,ScopeGate gate,AssetEntry entry,IMutationLease<VisualAsset> inner){_owner=owner;_gate=gate;_entry=entry;_inner=inner;_capability=owner.Capability;}
        public string LeaseId=>_inner.LeaseId;public MutationKey<VisualAsset> Key=>_inner.Key;public bool IsActive=>_owner is { _disposed:false }&&_inner.IsActive;
        public OperationResult SetValue(VisualAsset value)
        {
            var target=Key.Target.Key;
            if(_owner is null||_owner._disposed)return OperationResult.Fail(ErrorCode.ScopeExpired,"Visual override lease is disposed.",_capability,target);
            if(_gate.Check() is { } code)return OperationResult.Fail(code,code==ErrorCode.WrongThread?
                "Visual operations require the Unity main thread.":"The visual plugin scope has ended.",_capability,target);
            if(!_inner.IsActive)return OperationResult.Fail(ErrorCode.TargetExpired,"Visual override target is no longer active.",_capability,target);
            return OperationResult.Fail(ErrorCode.OperationUnsupported,"Replace a visual override by disposing it and acquiring a new lease.",_capability,target);
        }
        public void Dispose()
        {
            var owner=_owner;if(owner is null)return;owner._main.VerifyAccess();_owner=null;if(owner._disposed)return;
            try{_inner.Dispose();}
            finally
            {
                if(!owner._disposed)
                {
                    _entry.References--;_entry.ConsumerReferences--;
                    foreach(var b in owner._bindings.Values.ToArray())
                    {
                        owner.CaptureActual(b);var snapshot=b.Channel.Describe();
                        // An idle core channel would otherwise adopt a failed restoration's owned
                        // clone as its next baseline. Preserve our independently captured baseline.
                        if(!snapshot.Valid||(snapshot.Holders.Count==0&&b.Actual is {Owned:true} actual&&
                            b.PossibleAssignments.Contains(actual)&&!ReferenceEquals(actual,b.RecoveryBaseline)))owner.Remove(b,false);
                        else owner.PinSnapshot(b);
                    }
                    owner.Sweep();
                }
            }
        }
    }
}
