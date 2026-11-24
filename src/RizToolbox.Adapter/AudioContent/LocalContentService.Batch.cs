using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class LocalContentService
{
    internal ILocalContentBatchProviderService BatchForPlugin(IPluginContext context)
    {
        _main.VerifyAccess();
        return new BatchView(this,context,context.Own(new Gate(context)));
    }

    private Result<ILocalContentBatchRegistration> RegisterBatch(IPluginContext context,Gate gate,
        IReadOnlyList<LocalContentRequest> requests,CancellationToken token)
    {
        var result=RegisterTransaction(context,gate,requests,token,AudioContentCapabilityIds.LocalContentBatch,false);
        return result.Success?Result<ILocalContentBatchRegistration>.Ok(result.Value!):Result<ILocalContentBatchRegistration>.Fail(result.Error!);
    }
    private Result<BatchRegistration> RegisterTransaction(IPluginContext context,Gate gate,
        IReadOnlyList<LocalContentRequest> requests,CancellationToken token,string capabilityId,bool single)
    {
        Result<BatchRegistration> FailBatch(ErrorCode code,string message)
            =>Result<BatchRegistration>.Fail(new(code,message,capabilityId));
        var guard=Guard(gate);
        if(guard is not null)return FailBatch(guard.Code,guard.Message);
        _busy=true;
        var pending=new List<Entry>();
        Entry[] stagedEntries=Array.Empty<Entry>();
        BatchRegistration? ownedRegistration=null;
        var committed=false;
        Result<BatchRegistration> Attempt()
        {
            token.ThrowIfCancellationRequested();
            var count=requests?.Count??0;
            if(count is <1 or >MaximumOwnerItems)
                return FailBatch(ErrorCode.ContentInvalid,"A content batch must contain 1..32 entries.");
            // Index bounded inputs instead of trusting a caller's enumerator or mutable arrays.
            var copied=new LocalContentRequest[count];
            for(var i=0;i<count;i++)
            {
                var request=requests![i];
                if(request is null||!Id(request.Id)||request.File is null||!Enum.IsDefined(typeof(LocalContentKind),request.Kind))
                    return FailBatch(ErrorCode.ContentInvalid,"Each entry requires a valid ID, kind and file.");
                var dependencyCount=request.Dependencies?.Count??0;
                if(dependencyCount is <0 or >16)return FailBatch(ErrorCode.ContentInvalid,"At most 16 dependencies are allowed per entry.");
                var dependencies=new LocalContentDependency[dependencyCount];
                for(var j=0;j<dependencyCount;j++)
                {
                    var dependency=request.Dependencies![j];
                    if(dependency is null||dependency.Content is null||!Id(dependency.Content.Namespace)||!Id(dependency.Content.Id)||dependency.MinimumFormatVersion<0)
                        return FailBatch(ErrorCode.ContentInvalid,"Invalid dependency declaration.");
                    dependencies[j]=dependency with{Content=dependency.Content with{}};
                }
                copied[i]=request with{File=request.File with{},Dependencies=Array.AsReadOnly(dependencies)};
            }
            var parser=_chartParser;var parserEpoch=_parserEpoch;
            var staged=new Dictionary<LocalContentId,Entry>();
            foreach(var request in copied)
            {
                var id=new LocalContentId(context.PluginId,request.Id);
                if(_published.ContainsKey(id)||staged.ContainsKey(id))
                    return FailBatch(ErrorCode.Conflict,"Duplicate published or in-batch content ID: "+request.Id);
                if(request.Kind==LocalContentKind.Chart&&parser is null)
                    return FailBatch(ErrorCode.OperationUnsupported,"The build-specific chart parser is unavailable.");
                if(request.Kind==LocalContentKind.Chart
                    ?request.Format!="rizline.chart.json"||request.FormatVersion!=0
                    :request.FormatVersion!=1||request.Format!=(request.Kind==LocalContentKind.Audio?"wav.pcm16":"png.rgba8"))
                    return FailBatch(ErrorCode.ContentInvalid,"Unsupported content format/version.");
                staged.Add(id,new Entry{Published=false,Snapshot=new(id,request.Kind,request.Format,request.FormatVersion,0,
                    request.Kind==LocalContentKind.Chart,Array.AsReadOnly(request.Dependencies!.Select(x=>x.Content).ToArray()))});
            }
            var entries=copied.Select(x=>staged[new(context.PluginId,x.Id)]).ToArray();
            stagedEntries=entries;
            var external=new HashSet<Entry>();
            for(var i=0;i<copied.Length;i++)
            {
                var deps=new List<Entry>();
                foreach(var dependency in copied[i].Dependencies!)
                {
                    Entry? found;
                    var local=staged.TryGetValue(dependency.Content,out found);
                    if(!local&&!_published.TryGetValue(dependency.Content,out found))
                        return FailBatch(ErrorCode.ContentInvalid,"A content dependency is missing.");
                    var version=local?copied[Array.IndexOf(entries,found!)].FormatVersion:found!.Snapshot.FormatVersion;
                    if(version<dependency.MinimumFormatVersion||deps.Contains(found!))
                        return FailBatch(ErrorCode.ContentInvalid,"A dependency version is incompatible or duplicated.");
                    deps.Add(found!);if(!local)external.Add(found!);
                }
                entries[i].Dependencies=deps.ToArray();
            }
            var marks=new Dictionary<Entry,int>();
            bool Visit(Entry entry)
            {
                if(marks.TryGetValue(entry,out var mark))return mark==2;
                marks[entry]=1;
                foreach(var dependency in entry.Dependencies)
                    if(Array.IndexOf(entries,dependency)>=0&&!Visit(dependency))return false;
                marks[entry]=2;return true;
            }
            if(entries.Any(x=>!Visit(x)))return FailBatch(ErrorCode.ContentInvalid,"Content dependency cycles are invalid.");
            var retained=_retained.ToArray();var owned=retained.Where(x=>x.Snapshot.Content.Namespace==context.PluginId).ToArray();
            if(owned.Length+count>MaximumOwnerItems||retained.Length+count>MaximumItems)
                return FailBatch(ErrorCode.QuotaExceeded,"Retained content count exceeds the provider or process budget.");
            long bytesAdded=0,ownedBytes=owned.Sum(x=>x.Bytes),totalBytes=retained.Sum(x=>x.Bytes);
            for(var i=0;i<count;i++)
            {
                token.ThrowIfCancellationRequested();
                if(_disposed||!gate.Active)return FailBatch(ErrorCode.ScopeExpired,"Provider ended during validation.");
                var request=copied[i];var extension=request.Kind switch{LocalContentKind.Audio=>".wav",LocalContentKind.Cover=>".png",_=>".json"};
                if(!string.Equals(Path.GetExtension(request.File.RelativePath),extension,StringComparison.OrdinalIgnoreCase))
                    return FailBatch(ErrorCode.ContentInvalid,"File extension differs from its content kind.");
                var bytes=_files.Read(context.PluginId,request.File,token);
                object payload;long decoded;
                if(request.Kind==LocalContentKind.Audio){var audio=WavDecoder.Decode(bytes,token);payload=audio;decoded=audio.EstimatedBytes;}
                else if(request.Kind==LocalContentKind.Cover){var image=PngDecoder.Decode(bytes,2048,2048*2048,64L*1024*1024,ContentFileAccess.MaximumFileBytes);payload=image;decoded=checked((long)image.Width*image.Height*16);}
                else{payload=parser!.Parse(bytes,token);decoded=checked((long)bytes.Length*8);}
                entries[i].Payload=payload;entries[i].Bytes=decoded;bytesAdded=checked(bytesAdded+decoded);
                if(payload is IDisposable)pending.Add(entries[i]);
                if(ownedBytes+bytesAdded>MaximumOwnerBytes||totalBytes+bytesAdded>MaximumBytes)
                    return FailBatch(ErrorCode.QuotaExceeded,"Retained content bytes exceed the provider or process budget.");
            }
            var revision=checked(_revision+1);
            for(var i=0;i<count;i++)
            {
                var request=copied[i];
                entries[i].Snapshot=new(new(context.PluginId,request.Id),request.Kind,request.Format,request.FormatVersion,revision,
                    request.Kind==LocalContentKind.Chart,Array.AsReadOnly(request.Dependencies!.Select(x=>x.Content).ToArray()));
                entries[i].Group=entries;
            }
            var registration=new BatchRegistration(this,entries,revision,single);
            ownedRegistration=registration;
            // Own may invoke caller code or immediately dispose an expired scope. Nothing is published yet.
            if(single)context.Own<ILocalContentRegistration>(registration.SingleRegistration!);
            else context.Own<ILocalContentBatchRegistration>(registration);
            token.ThrowIfCancellationRequested();
            if(_disposed||!gate.Active||registration.Disposed)
                return FailBatch(ErrorCode.ScopeExpired,"Provider ended before publication.");
            if(copied.Any(x=>x.Kind==LocalContentKind.Chart)&&!ReferenceEquals(parserEpoch,_parserEpoch))
                return FailBatch(ErrorCode.ContentInvalid,"Chart parser changed during validation.");
            if(external.Any(x=>!x.Published||!_published.TryGetValue(x.Snapshot.Content,out var current)||!ReferenceEquals(x,current)))
                return FailBatch(ErrorCode.ContentInvalid,"A dependency was withdrawn during validation.");
            // Prepare every allocating operation before the no-callback publication block.
            var published=new Dictionary<LocalContentId,Entry>(_published);
            var allRetained=new HashSet<Entry>(_retained);
            foreach(var entry in entries){published.Add(entry.Snapshot.Content,entry);allRetained.Add(entry);}
            var increments=entries.SelectMany(x=>x.Dependencies).GroupBy(x=>x).ToArray();
            foreach(var increment in increments)_=checked(increment.Key.References+increment.Count());
            foreach(var increment in increments)increment.Key.References+=increment.Count();
            foreach(var entry in entries)entry.Published=true;
            _retained=allRetained;_published=published;_revision=revision;
            committed=true;
            pending.Clear();
            return Result<BatchRegistration>.Ok(registration);
        }
        Result<BatchRegistration> result;
        try{result=Attempt();}
        catch(OperationCanceledException e){result=FailBatch(ErrorCode.Cancelled,e.Message);}
        catch(Exception e){result=FailBatch(ErrorCode.ContentInvalid,e.Message);}
        List<Exception>? failures=null;
        try
        {
            if(!committed)
            {
                try{ownedRegistration?.Dispose();}catch(Exception e){(failures??=new()).Add(e);}
                foreach(var entry in stagedEntries)
                {
                    entry.References=0;entry.Dependencies=Array.Empty<Entry>();entry.Group=null;
                    if(!pending.Contains(entry))entry.Payload=null;
                }
            }
            // Attempt every root and report every failure, retaining unconfirmed frees in quotas.
            foreach(var entry in pending)try{CleanupPayload(entry);}catch(Exception e){(failures??=new()).Add(e);}
        }
        finally{_busy=false;}
        return failures is null?result:FailBatch(result.Error?.Code??ErrorCode.InternalError,
            (result.Error?.Message??"Content registration failed.")+" Staged cleanup failed; unconfirmed resources remain retained: "+
            string.Join(" | ",failures.SelectMany(x=>x is AggregateException aggregate?(IEnumerable<Exception>)aggregate.Flatten().InnerExceptions:new[]{x}).Select(x=>x.Message)));
    }

    private void WithdrawBatch(Entry[] entries)
    {
        var published=entries.Where(x=>x.Published).ToArray();
        foreach(var entry in published)
        {
            entry.Published=false;
            if(_published.TryGetValue(entry.Snapshot.Content,out var current)&&ReferenceEquals(entry,current))
                _published.Remove(entry.Snapshot.Content);
        }
        List<Exception>? failures=null;
        foreach(var entry in published)try{Release(entry);}catch(Exception e){(failures??=new()).Add(e);}
        if(failures is not null)throw new AggregateException(failures);
    }
    private sealed class BatchView:ILocalContentBatchProviderService
    {
        private readonly LocalContentService _owner;private readonly IPluginContext _context;private readonly Gate _gate;
        internal BatchView(LocalContentService owner,IPluginContext context,Gate gate)=>(_owner,_context,_gate)=(owner,context,gate);
        public Result<ILocalContentBatchRegistration> RegisterBatch(IReadOnlyList<LocalContentRequest> requests,CancellationToken cancellationToken=default)
            =>_owner.RegisterBatch(_context,_gate,requests,cancellationToken);
    }
    private sealed class BatchRegistration:ILocalContentBatchRegistration
    {
        private readonly LocalContentService _owner;private readonly Entry[] _entries;internal bool Disposed;
        internal BatchRegistration(LocalContentService owner,Entry[] entries,long revision,bool single)
        {(_owner,_entries,Revision)=(owner,entries,revision);Contents=Array.AsReadOnly(entries.Select(x=>x.Snapshot).ToArray());if(single)SingleRegistration=new Registration(this);}
        internal ILocalContentRegistration? SingleRegistration{get;}
        public long Revision{get;}
        public IReadOnlyList<LocalContentSnapshot> Contents{get;}
        public bool IsPublished=>!Disposed&&!_owner._disposed&&_entries.All(x=>x.Published);
        public void Dispose(){_owner._main.VerifyAccess();if(Disposed)return;Disposed=true;_owner.WithdrawBatch(_entries);}
    }
}
