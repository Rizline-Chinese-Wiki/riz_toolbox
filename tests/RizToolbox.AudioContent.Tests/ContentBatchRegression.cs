using RizToolbox.Adapter.AudioContent;
using RizToolbox.Sdk;
using static Assert;

internal static class ContentBatchRegression
{
    internal static readonly (string Name,Action Test)[] Cases={
        ("Batch forward dependencies publish one immutable revision and withdraw together",Forward),
        ("Batch duplicate IDs conflict without publishing preceding entries",Duplicates),
        ("Batch missing incompatible duplicate and cyclic dependencies publish nothing",InvalidDependencies),
        ("Batch parse failure releases staged native graphs and leaves old content intact",ParseFailure),
        ("Batch cancellation during parsing releases every graph before publication",Cancellation),
        ("Batch provider withdrawal during parsing prevents publication",ScopeWithdrawal),
        ("Batch parser withdrawal and restoration cannot publish stale graphs",ParserWithdrawal),
        ("Batch external dependency withdrawal rejects the complete transaction",DependencyWithdrawal),
        ("Batch retained consumer preserves its dependency graph after group withdrawal",Retained),
        ("Batch parser removal atomically withdraws charts and their grouped audio",GroupParserWithdrawal),
        ("Batch quotas include existing and retired entries and failed quota consumes no revision",Quotas),
        ("Batch cross-provider IDs coexist and wrong-thread expired views fail",Scopes),
        ("Batch ownership rejection and immediate disposal never expose partial publication",Ownership),
        ("Batch ownership callback cancellation releases staged graphs",OwnershipCancellation),
        ("Batch immutable dependency copies survive mutation during parsing",Snapshot),
        ("Batch reads untrusted collection count once and never invokes its enumerator",BoundedCollection),
        ("Single ownership callbacks observe unpublished data and failed ownership consumes no revision",SingleOwnership),
        ("Single dependency input uses one bounded indexed snapshot",SingleBoundedDependencies),
        ("Ownership callback dependency withdrawal rejects both registration interfaces",OwnershipDependencyWithdrawal),
        ("Staged cleanup reports every failure and keeps unresolved roots in retained quotas",StagedCleanupFailure),
        ("Cancellation preserves its code and reports staged cleanup failure for both interfaces",CancelledCleanupFailure),
        ("Dependency cleanup continues after every failing sibling root",DependencyCleanupFailure),
        ("Store disposal withdraws and releases later registrations after a cleanup failure",StoreCleanupFailure),
        ("Parser withdrawal withdraws and releases later groups after a cleanup failure",ParserCleanupFailure),
        ("Wrong-thread consumer disposal preserves its lease for main-thread release",WrongThreadConsumerDisposal),
        ("Failed consumer-root cleanup keeps its dependencies alive and still releases independent siblings",FailedRootRetainsDependencies),
        ("Module recovery cannot discard uncertain cleanup and reset its retained quota budget",ModuleCleanupRecovery),
        ("Module recovery waits for held consumer quotas and resumes only after the final release",ModuleRetainedRecovery)
    };
    private static LocalContentRequest Chart(string id,params LocalContentDependency[] deps)
        =>new(id,LocalContentKind.Chart,"rizline.chart.json",0,new(ContentFileArea.Package,"chart.json"),deps);
    private static LocalContentDependency Dep(string id,string ns="local.test",int version=0)=>new(new(ns,id),version);
    private static int Published(LocalContentService store,Context context)=>Need(store.ForPlugin(context).GetPublished()).Count;
    private static void Forward()
    {
        using var f=new ChartFixture();using var c=f.Context();var requests=new[]{Chart("chart",Dep("music")),Chart("music")};
        var group=Need(f.Content.BatchForPlugin(c).RegisterBatch(requests));
        requests[0]=Chart("changed");Equal(2,Published(f.Content,c));Equal("chart",group.Contents[0].Content.Id);
        Check(group.IsPublished&&group.Contents.All(x=>x.Revision==group.Revision));
        group.Dispose();Equal(0,Published(f.Content,c));Check(!group.IsPublished&&f.Parser.Created.All(x=>x.Disposed));
        group.Dispose();
    }
    private static void Duplicates()
    {
        using var f=new ChartFixture();using var c=f.Context();var batch=f.Content.BatchForPlugin(c);
        Equal(ErrorCode.Conflict,batch.RegisterBatch(new[]{Chart("same"),Chart("same")}).Error!.Code);
        Equal(0,Published(f.Content,c));var old=f.Register(c,"old");
        Equal(ErrorCode.Conflict,batch.RegisterBatch(new[]{Chart("new"),Chart("old")}).Error!.Code);Equal(1,Published(f.Content,c));Check(old.IsPublished);
    }
    private static void InvalidDependencies()
    {
        using var f=new ChartFixture();using var c=f.Context();var batch=f.Content.BatchForPlugin(c);
        foreach(var requests in new[]{
            new[]{Chart("x",Dep("absent"))},new[]{Chart("x",Dep("y",version:1)),Chart("y")},
            new[]{Chart("x",Dep("y"),Dep("y")),Chart("y")},new[]{Chart("x",Dep("y")),Chart("y",Dep("x"))},
            new[]{Chart("x",Dep("x"))}})
        {Equal(ErrorCode.ContentInvalid,batch.RegisterBatch(requests).Error!.Code);Equal(0,Published(f.Content,c));}
        Equal(0,f.Parser.Created.Count);
    }
    private static void ParseFailure()
    {
        using var f=new ChartFixture();using var c=f.Context();f.Register(c,"old");
        var result=f.Content.BatchForPlugin(c).RegisterBatch(new[]{Chart("one"),Chart("bad")with{File=new(ContentFileArea.Package,"missing.json")}});
        Equal(ErrorCode.ContentInvalid,result.Error!.Code);Equal(1,Published(f.Content,c));
        Check(!f.Parser.Created[0].Disposed&&f.Parser.Created[1].Disposed);
    }
    private static void Cancellation()
    {
        using var f=new ChartFixture();using var c=f.Context();using var cancel=new CancellationTokenSource();
        f.Content.SetChartParser(new CallbackParser(f.Parser,()=>cancel.Cancel()));
        Equal(ErrorCode.Cancelled,f.Content.BatchForPlugin(c).RegisterBatch(new[]{Chart("one"),Chart("two")},cancel.Token).Error!.Code);
        Equal(0,Published(f.Content,c));Check(f.Parser.Created.Single().Disposed);
    }
    private static void ScopeWithdrawal()
    {
        using var f=new ChartFixture();using var c=f.Context();using var reader=f.Context("reader");var batch=f.Content.BatchForPlugin(c);
        f.Content.SetChartParser(new CallbackParser(f.Parser,c.Dispose));
        Check(!batch.RegisterBatch(new[]{Chart("one"),Chart("two")}).Success);
        Equal(0,Published(f.Content,reader));Check(f.Parser.Created.All(x=>x.Disposed));
    }
    private static void ParserWithdrawal()
    {
        using var f=new ChartFixture();using var c=f.Context();
        CallbackParser? parser=null;parser=new(f.Parser,()=>{f.Content.SetChartParser(null);f.Content.SetChartParser(parser);});
        f.Content.SetChartParser(parser);
        Equal(ErrorCode.ContentInvalid,f.Content.BatchForPlugin(c).RegisterBatch(new[]{Chart("one")}).Error!.Code);
        Equal(0,Published(f.Content,c));Check(f.Parser.Created.Single().Disposed);
    }
    private static void DependencyWithdrawal()
    {
        using var f=new ChartFixture();using var c=f.Context();var old=f.Register(c,"old");
        f.Content.SetChartParser(new CallbackParser(f.Parser,old.Dispose));
        Equal(ErrorCode.ContentInvalid,f.Content.BatchForPlugin(c).RegisterBatch(new[]{Chart("new",Dep("old"))}).Error!.Code);
        Equal(0,Published(f.Content,c));Check(f.Parser.Created.All(x=>x.Disposed));
    }
    private static void Retained()
    {
        using var f=new ChartFixture();using var c=f.Context();
        var group=Need(f.Content.BatchForPlugin(c).RegisterBatch(new[]{Chart("child",Dep("parent")),Chart("parent")}));
        var run=Need(f.Content.AcquireValidated(new(c.PluginId,"child"),"run"));
        group.Dispose();Equal(0,Published(f.Content,c));Check(f.Parser.Created.All(x=>!x.Disposed));
        Equal(ErrorCode.TargetMissing,f.Content.AcquireValidated(new(c.PluginId,"child"),"new-run").Error!.Code);
        f.Content.Dispose();Check(f.Parser.Created.All(x=>!x.Disposed));run.Dispose();Check(f.Parser.Created.All(x=>x.Disposed));
    }
    private static void GroupParserWithdrawal()
    {
        using var f=new ChartFixture();using var c=f.Context();
        var wav=Path.Combine(f.Directory,"tone.wav");File.WriteAllBytes(wav,Wav());
        try
        {
            var group=Need(f.Content.BatchForPlugin(c).RegisterBatch(new[]{Chart("chart"),
                new LocalContentRequest("audio",LocalContentKind.Audio,"wav.pcm16",1,new(ContentFileArea.Package,"tone.wav"))}));
            f.Content.SetChartParser(null);Equal(0,Published(f.Content,c));Check(!group.IsPublished);
        }
        finally{File.Delete(wav);}
    }
    private static void Quotas()
    {
        using var f=new ChartFixture();using var c=f.Context();var batch=f.Content.BatchForPlugin(c);
        var group=Need(batch.RegisterBatch(Enumerable.Range(0,31).Select(i=>Chart("x"+i)).ToArray()));
        Equal(ErrorCode.QuotaExceeded,batch.RegisterBatch(new[]{Chart("a"),Chart("b")}).Error!.Code);Equal(31,Published(f.Content,c));
        var run=Need(f.Content.AcquireValidated(new(c.PluginId,"x0"),"run"));group.Dispose();
        Equal(ErrorCode.QuotaExceeded,batch.RegisterBatch(Enumerable.Range(0,32).Select(i=>Chart("y"+i)).ToArray()).Error!.Code);
        run.Dispose();var next=Need(batch.RegisterBatch(new[]{Chart("ok")}));Equal(group.Revision+1,next.Revision);
    }
    private static void Scopes()
    {
        using var f=new ChartFixture();using var a=f.Context("a");using var b=f.Context("b");
        var av=f.Content.BatchForPlugin(a);Need(av.RegisterBatch(new[]{Chart("same")}));
        Need(f.Content.BatchForPlugin(b).RegisterBatch(new[]{Chart("same")}));Equal(2,Published(f.Content,b));
        Equal(ErrorCode.WrongThread,Task.Run(()=>av.RegisterBatch(new[]{Chart("thread")})).Result.Error!.Code);
        a.Dispose();Equal(ErrorCode.ScopeExpired,av.RegisterBatch(new[]{Chart("late")}).Error!.Code);Equal(1,Published(f.Content,b));
    }
    private sealed class CallbackParser:ILocalChartParser
    {
        private readonly ILocalChartParser _inner;private readonly Action _callback;
        internal CallbackParser(ILocalChartParser inner,Action callback)=>(_inner,_callback)=(inner,callback);
        public IParsedLocalChart Parse(byte[] bytes,CancellationToken token){var parsed=_inner.Parse(bytes,token);_callback();return parsed;}
    }
    private static void Ownership()
    {
        foreach(var throwing in new[]{false,true})
        {
            using var f=new ChartFixture();using var c=f.Context();var batch=f.Content.BatchForPlugin(c);
            c.OnOwn=value=>{if(value is ILocalContentBatchRegistration){if(throwing)throw new IOException("ownership failed");value.Dispose();}};
            Check(!batch.RegisterBatch(new[]{Chart("one"),Chart("two")}).Success);
            Equal(0,Published(f.Content,c));Check(f.Parser.Created.All(x=>x.Disposed));
        }
    }
    private static void OwnershipCancellation()
    {
        using var f=new ChartFixture();using var c=f.Context();using var cancel=new CancellationTokenSource();
        var batch=f.Content.BatchForPlugin(c);
        c.OnOwn=value=>{if(value is ILocalContentBatchRegistration)cancel.Cancel();};
        Equal(ErrorCode.Cancelled,batch.RegisterBatch(new[]{Chart("one")},cancel.Token).Error!.Code);
        Equal(0,Published(f.Content,c));Check(f.Parser.Created.Single().Disposed);
    }
    private static void Snapshot()
    {
        using var f=new ChartFixture();using var c=f.Context();var dependencies=new[]{Dep("second")};
        var requests=new[]{Chart("first",dependencies),Chart("second")};
        f.Content.SetChartParser(new CallbackParser(f.Parser,()=>{dependencies[0]=Dep("missing");requests[1]=Chart("changed");}));
        var group=Need(f.Content.BatchForPlugin(c).RegisterBatch(requests));
        Equal("second",group.Contents[1].Content.Id);Equal("second",group.Contents[0].Dependencies.Single().Id);
    }
    private static void BoundedCollection()
    {
        using var f=new ChartFixture();using var c=f.Context();var list=new ChangingCount();
        Need(f.Content.BatchForPlugin(c).RegisterBatch(list));Equal(1,list.CountReads);Equal(1,Published(f.Content,c));
    }
    private sealed class ChangingCount:IReadOnlyList<LocalContentRequest>
    {
        internal int CountReads;
        public int Count=>++CountReads==1?1:int.MaxValue;
        public LocalContentRequest this[int index]=>index==0?Chart("bounded"):throw new IndexOutOfRangeException();
        public IEnumerator<LocalContentRequest> GetEnumerator()=>throw new InvalidOperationException("Unbounded enumerator must not execute.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    private static void SingleOwnership()
    {
        foreach(var throwing in new[]{false,true})
        {
            using var f=new ChartFixture();using var c=f.Context();var view=f.Content.ForPlugin(c);var observedPublished=false;
            c.OnOwn=value=>{if(value is ILocalContentRegistration registration){observedPublished=registration.IsPublished;if(throwing)throw new IOException("ownership rejected");registration.Dispose();}};
            var failed=view.Register(Chart("rejected"));
            Check(!observedPublished);Check(!failed.Success);Equal(AudioContentCapabilityIds.LocalContent,failed.Error!.CapabilityId);
            Equal(0,Published(f.Content,c));Check(f.Parser.Created.Single().Disposed);
            c.OnOwn=null;Equal(1L,Need(view.Register(Chart("accepted"))).Content.Revision);
        }
    }
    private static void SingleBoundedDependencies()
    {
        using var f=new ChartFixture();using var c=f.Context();f.Register(c,"parent");var dependencies=new IndexedDependencies(Dep("parent"));
        var registration=Need(f.Content.ForPlugin(c).Register(Chart("child")with{Dependencies=dependencies}));
        Equal(1,dependencies.CountReads);Equal("parent",registration.Content.Dependencies.Single().Id);
    }
    private sealed class IndexedDependencies:IReadOnlyList<LocalContentDependency>
    {
        private readonly LocalContentDependency _dependency;internal int CountReads;
        internal IndexedDependencies(LocalContentDependency dependency)=>_dependency=dependency;
        public int Count=>++CountReads==1?1:int.MaxValue;
        public LocalContentDependency this[int index]=>index==0?_dependency:throw new IndexOutOfRangeException();
        public IEnumerator<LocalContentDependency> GetEnumerator()=>throw new InvalidOperationException("Dependency enumeration must not execute.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    private static void OwnershipDependencyWithdrawal()
    {
        foreach(var single in new[]{false,true})
        {
            using var f=new ChartFixture();using var c=f.Context();var parent=f.Register(c,"parent");var view=f.Content.ForPlugin(c);var batch=f.Content.BatchForPlugin(c);
            c.OnOwn=value=>{if(value is ILocalContentRegistration or ILocalContentBatchRegistration)parent.Dispose();};
            if(single)Equal(ErrorCode.ContentInvalid,view.Register(Chart("child",Dep("parent"))).Error!.Code);
            else Equal(ErrorCode.ContentInvalid,batch.RegisterBatch(new[]{Chart("child",Dep("parent"))}).Error!.Code);
            Equal(0,Published(f.Content,c));Check(f.Parser.Created.All(x=>x.Disposed));
        }
    }
    private static void StagedCleanupFailure()
    {
        using var f=new ChartFixture();using var c=f.Context();var parser=new CleanupParser(0,1);f.Content.SetChartParser(parser);var batch=f.Content.BatchForPlugin(c);
        var result=batch.RegisterBatch(new[]{Chart("one"),Chart("two"),Chart("missing")with{File=new(ContentFileArea.Package,"missing.json")}});
        Equal(ErrorCode.ContentInvalid,result.Error!.Code);Check(result.Error.Message.Contains("cleanup-root-0")&&result.Error.Message.Contains("cleanup-root-1"));
        Equal(0,Published(f.Content,c));Check(parser.Created.All(x=>x.Attempts==1));
        Equal(ErrorCode.QuotaExceeded,batch.RegisterBatch(Enumerable.Range(0,31).Select(i=>Chart("quota"+i)).ToArray()).Error!.Code);
    }
    private static void CancelledCleanupFailure()
    {
        foreach(var single in new[]{false,true})
        {
            using var f=new ChartFixture();using var c=f.Context();using var cancel=new CancellationTokenSource();var parser=new CleanupParser(0);f.Content.SetChartParser(parser);
            var view=f.Content.ForPlugin(c);var batch=f.Content.BatchForPlugin(c);
            c.OnOwn=value=>{if(value is ILocalContentRegistration or ILocalContentBatchRegistration)cancel.Cancel();};
            var error=single?view.Register(Chart("one"),cancel.Token).Error:batch.RegisterBatch(new[]{Chart("one")},cancel.Token).Error;
            Check(error is not null);Equal(ErrorCode.Cancelled,error!.Code);Check(error.Message.Contains("cleanup-root-0"));
            Equal(0,Published(f.Content,c));Equal(1,parser.Created.Single().Attempts);
        }
    }
    private static void DependencyCleanupFailure()
    {
        using var f=new ChartFixture();using var c=f.Context();var parser=new CleanupParser(0,1);f.Content.SetChartParser(parser);
        var group=Need(f.Content.BatchForPlugin(c).RegisterBatch(new[]{Chart("a"),Chart("b"),Chart("root",Dep("a"),Dep("b"))}));
        Throws<AggregateException>(group.Dispose);Check(!group.IsPublished);Equal(0,Published(f.Content,c));
        Check(parser.Created.All(x=>x.Attempts==1));
        Equal(ErrorCode.QuotaExceeded,f.Content.BatchForPlugin(c).RegisterBatch(Enumerable.Range(0,31).Select(i=>Chart("quota"+i)).ToArray()).Error!.Code);
    }
    private static void StoreCleanupFailure()
    {
        using var f=new ChartFixture();using var c=f.Context();var parser=new CleanupParser(0);f.Content.SetChartParser(parser);
        var first=f.Register(c,"first");var second=f.Register(c,"second");
        Throws<AggregateException>(f.Content.Dispose);Check(!first.IsPublished&&!second.IsPublished);Check(parser.Created.All(x=>x.Attempts==1));
    }
    private static void ParserCleanupFailure()
    {
        using var f=new ChartFixture();using var c=f.Context();var parser=new CleanupParser(0);f.Content.SetChartParser(parser);
        var first=Need(f.Content.BatchForPlugin(c).RegisterBatch(new[]{Chart("first")}));
        var second=Need(f.Content.BatchForPlugin(c).RegisterBatch(new[]{Chart("second")}));
        Throws<AggregateException>(()=>f.Content.SetChartParser(null));Check(!first.IsPublished&&!second.IsPublished);
        Equal(0,Published(f.Content,c));Check(parser.Created.All(x=>x.Attempts==1));
    }
    private static void WrongThreadConsumerDisposal()
    {
        using var f=new ChartFixture();using var c=f.Context();var registration=f.Register(c,"held");
        var lease=Need(f.Content.AcquireValidated(new(c.PluginId,"held"),"run"));registration.Dispose();
        Task.Run(()=>Throws<InvalidOperationException>(lease.Dispose)).GetAwaiter().GetResult();
        Check(!f.Parser.Created.Single().Disposed);lease.Dispose();Check(f.Parser.Created.Single().Disposed);
    }
    private static void FailedRootRetainsDependencies()
    {
        using var f=new ChartFixture();using var c=f.Context();var parser=new CleanupParser(1);f.Content.SetChartParser(parser);
        var group=Need(f.Content.BatchForPlugin(c).RegisterBatch(new[]{Chart("dependency"),Chart("root",Dep("dependency")),Chart("independent")}));
        var consumer=Need(f.Content.AcquireValidated(new(c.PluginId,"root"),"run"));group.Dispose();
        Equal(0,parser.Created[0].Attempts);Equal(0,parser.Created[1].Attempts);Equal(1,parser.Created[2].Attempts);
        Throws<AggregateException>(consumer.Dispose);Equal(0,parser.Created[0].Attempts);Equal(1,parser.Created[1].Attempts);
        Equal(ErrorCode.QuotaExceeded,f.Content.BatchForPlugin(c).RegisterBatch(Enumerable.Range(0,31).Select(i=>Chart("quota"+i)).ToArray()).Error!.Code);
    }
    private sealed class CleanupParser:ILocalChartParser
    {
        private readonly HashSet<int> _fail;internal readonly List<CleanupChart> Created=new();
        internal CleanupParser(params int[] fail)=>_fail=new(fail);
        public IParsedLocalChart Parse(byte[] bytes,CancellationToken token)
        {var chart=new CleanupChart(ChartJsonValidator.Validate(bytes,token),Created.Count,_fail.Contains(Created.Count));Created.Add(chart);return chart;}
    }
    private static void ModuleCleanupRecovery()
    {
        using var f=new ChartFixture();using var c=f.Context();
        var module=new AudioContentModules.LocalContentModule(null!,f.Main,new Logger(),new ContentFileAccess(_=>f.Directory,_=>f.Directory));
        Check(module.Probe().Ready);var store=module.Service;var parser=new CleanupParser(0);store.SetChartParser(parser);
        var batch=store.BatchForPlugin(c);
        Check(!batch.RegisterBatch(new[]{Chart("one"),Chart("missing")with{File=new(ContentFileArea.Package,"missing.json")}}).Success);
        Check(store.HasUnconfirmedCleanup);
        Throws<InvalidOperationException>(module.Release);Check(ReferenceEquals(store,module.Service));
        var probe=module.Probe();Check(!probe.Ready&&probe.Permanent);Equal(1,parser.Created.Single().Attempts);
        Throws<InvalidOperationException>(module.Release);Check(ReferenceEquals(store,module.Service));
    }
    private static void ModuleRetainedRecovery()
    {
        using var f=new ChartFixture();using var c=f.Context();
        var module=new AudioContentModules.LocalContentModule(null!,f.Main,new Logger(),new ContentFileAccess(_=>f.Directory,_=>f.Directory));
        Check(module.Probe().Ready);var store=module.Service;store.SetChartParser(f.Parser);
        Need(store.ForPlugin(c).Register(Chart("held")));
        var consumer=Need(store.AcquireValidated(new(c.PluginId,"held"),"run"));
        module.Release();Check(store.IsDisposed&&store.RetainedCount==1&&!store.HasUnconfirmedCleanup);
        var probe=module.Probe();Check(!probe.Ready&&!probe.Permanent&&ReferenceEquals(store,module.Service));
        Check(!f.Parser.Created.Single().Disposed);
        consumer.Dispose();Equal(0,store.RetainedCount);Check(f.Parser.Created.Single().Disposed);
        Check(module.Probe().Ready&&!ReferenceEquals(store,module.Service));module.Release();
    }
    private sealed class CleanupChart:IParsedLocalChart
    {
        public ValidatedChartJson Source{get;}private readonly int _index;private readonly bool _fail;internal int Attempts;
        internal CleanupChart(ValidatedChartJson source,int index,bool fail)=>(Source,_index,_fail)=(source,index,fail);
        public void Dispose(){Attempts++;if(_fail)throw new IOException("cleanup-root-"+_index);}
    }
}
