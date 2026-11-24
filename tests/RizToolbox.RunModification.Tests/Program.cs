using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Sdk;

var tests=new (string Name,Action Run)[]{
    ("Accepted Skip records its dispatch before the deferred Skipped boundary without fabricating a result",()=>{
        using var f=new Fixture();f.Start();var origin=f.State.LifecycleSnapshot();var queue=new VerifiedRunCancellationQueue(f.State);
        GameLifecycleChange? ended=null;using var subscription=f.State.Subscribe(x=>{if(x.Kind==GameLifecycleChangeKind.RunEnded)ended=x;}).Value!;
        Check(queue.Enqueue(f.State.CaptureCancellationToken(1)!,"accepted original Skip terminal edge",RunEndReason.Skipped),"Skip observation was not queued.");
        f.Journal.Record(Input()with{Source="riz.gameplay.controls",Operation="Skip",ObservedRunId=origin.RunId,
            ObservedSessionId=origin.SessionId,ObservedLifecycleRevision=origin.Revision});
        Check(queue.Drain()==1&&queue.Drain()==0,"Skip boundary was lost or delivered twice.");var s=f.Read();
        Check(s.Records.Single().RunId==origin.RunId&&s.Records.Single().Association==RunModificationAssociation.ExplicitObservedRun&&
            s.Runs.Single().ClosedToNewEntries&&s.Runs.Single().EndReason==RunEndReason.Skipped&&
            ended is { Result:null, EndReason:RunEndReason.Skipped }&&ended.Observation.Source==LifecycleObservationSource.VerifiedCallback,
            "Skip lost its dispatch association, changed reason, or fabricated an actual result.");
    }),
    ("Accepted Retry records its dispatch before the deferred verified cancellation closes the run",()=>{
        using var f=new Fixture();f.Start();var origin=f.State.LifecycleSnapshot();var queue=new VerifiedRunCancellationQueue(f.State);
        Check(queue.Enqueue(f.State.CaptureCancellationToken(1)!,"accepted original Retry edge"),"Retry observation was not queued.");
        f.Journal.Record(Input()with{Source="riz.gameplay.controls",Operation="Retry",ObservedRunId=origin.RunId,
            ObservedSessionId=origin.SessionId,ObservedLifecycleRevision=origin.Revision});
        Check(queue.Drain()==1,"Accepted Retry did not end its observed run.");var s=f.Read();
        Check(s.Records.Single().RunId==origin.RunId&&s.Records.Single().Association==RunModificationAssociation.ExplicitObservedRun&&
            s.Runs.Single().ClosedToNewEntries&&s.Runs.Single().EndReason==RunEndReason.Aborted,
            "Retry dispatch was lost or moved to unassociated history before cancellation.");
    }),
    ("Menu records remain unassociated without fabricated run identities",()=>{
        using var f=new Fixture();f.Journal.Record(Input());var s=f.Read();Check(s.Records.Single().RunId is null&&s.Runs.Count==0,"A menu operation fabricated a run.");
    }),
    ("Production lifecycle start associates exact session run and revision",()=>{
        using var f=new Fixture();f.Start();var state=f.State.LifecycleSnapshot();f.Journal.Record(Input()with{ObservedRunId=state.RunId,ObservedSessionId=state.SessionId,ObservedLifecycleRevision=state.Revision});
        var s=f.Read();var r=s.Records.Single();Check(r.RunId==state.RunId&&r.SessionId==state.SessionId&&r.LifecycleRevision==state.Revision&&r.Association==RunModificationAssociation.ExplicitObservedRun,"Real run association was lost.");
    }),
    ("Active pre-run effects are copied into the real start exactly once",()=>{
        using var f=new Fixture();using var effect=f.Journal.BeginEffect(Input()with{Category=RunModificationCategory.Audio});f.Start();f.State.ObservePause(false);f.State.ObservePause(true);f.State.ObservePause(false);
        var s=f.Read();Check(s.Records.Count(x=>x.Outcome==RunModificationOutcome.EffectCarriedIntoRun)==1&&s.Records.Single(x=>x.Outcome==RunModificationOutcome.EffectCarriedIntoRun).RunId==f.State.RunId,"Active effect carry duplicated or missed the real run.");
    }),
    ("Effect release is idempotent and does not assert restoration",()=>{
        using var f=new Fixture();f.Start();var effect=f.Journal.BeginEffect(Input());effect.Dispose();effect.Dispose();var s=f.Read();
        Check(s.ActiveEffects.Count==0&&s.Records.Count(x=>x.Outcome==RunModificationOutcome.EffectReleased)==1&&!s.Records.Any(x=>x.Outcome==RunModificationOutcome.Restored),"Release fabricated successful baseline restoration.");
    }),
    ("An observed run effect needs current run session and revision together", () =>
    {
        using var fixture = new Fixture();
        fixture.Start();
        var input = ObservedInput(fixture);
        Check(fixture.Journal.BeginObservedRunEffect(input with { ObservedRunId = "earlier-run" }) is null, "A stale run acquired an effect.");
        Check(fixture.Journal.BeginObservedRunEffect(input with { ObservedSessionId = "other-session" }) is null, "An unrelated session acquired an effect.");
        Check(fixture.Journal.BeginObservedRunEffect(input with { ObservedLifecycleRevision = 0 }) is null, "A stale revision acquired an effect.");
        Check(fixture.Journal.BeginObservedRunEffect(input with { ObservedLifecycleRevision = null }) is null, "An incomplete observation acquired an effect.");
        fixture.Finish();
        Check(fixture.Journal.BeginObservedRunEffect(input) is null, "A finished run acquired a new effect.");
        var snapshot = fixture.Read();
        Check(snapshot.Records.Count == 0 && snapshot.ActiveEffects.Count == 0, "Rejected association published an unassociated effect.");
    }),
    ("Deferred run effect release stays on the closed originating run", () =>
    {
        using var fixture = new Fixture();
        fixture.Start();
        var originalRun = fixture.State.RunId!;
        var recorder = new RunEffectRecorder(fixture.Journal.Record, fixture.Journal.BeginEffect, fixture.Journal.BeginObservedRunEffect);
        using var effect = recorder.BeginObservedRun(ObservedInput(fixture));
        Check(effect is not null, "Current run failed to acquire an effect.");
        var activated = fixture.Read().Records.Single();
        fixture.Finish();
        fixture.Start();
        var nextRun = fixture.State.RunId!;
        fixture.Log.OnWrite = () => effect!.Dispose();
        fixture.Journal.Record(Input());
        fixture.Log.OnWrite = null;

        var original = fixture.Read(new(RunId: originalRun));
        var release = original.Records.Last();
        Check(original.Records.Count == 2 && release.Outcome == RunModificationOutcome.EffectReleased
            && release.Association == RunModificationAssociation.ExplicitObservedRun && release.EffectId == activated.EffectId
            && original.ActiveEffects.Count == 0, "Deferred release lost the original effect or its association.");
        var next = fixture.Read(new(RunId: nextRun));
        Check(next.Records.Count == 1 && next.Records.Single().Outcome == RunModificationOutcome.Applied,
            "The previously bound effect carried into or released on the next run.");
    }),
    ("A departed run effect leaves active views after its original history is evicted", () =>
    {
        using var fixture = new Fixture(new(MaximumRuns: 1));
        fixture.Start();
        using var effect = fixture.Journal.BeginObservedRunEffect(ObservedInput(fixture));
        Check(effect is not null, "Current run failed to acquire an effect.");
        fixture.Finish();
        fixture.Start();
        effect!.Dispose();
        var snapshot = fixture.Read();
        Check(snapshot.Records.Count == 0 && snapshot.ActiveEffects.Count == 0 && snapshot.Counters.EvictedRuns == 1,
            "An evicted originating run caused a replacement or unassociated release entry.");
    }),
    ("Real result closes the run while later operations remain unassociated",()=>{
        using var f=new Fixture();f.Start();var run=f.State.RunId!;f.Journal.Record(Input());f.Finish();f.Journal.Record(Input()with{ObservedRunId=run});var s=f.Read();
        Check(s.Runs.Single().ClosedToNewEntries&&s.Runs.Single().EndReason==RunEndReason.Completed&&s.Records.First().RunId==run&&s.Records.Last().RunId is null,"Post-result audit changed a finished run.");
    }),
    ("Late unassociated result cannot close a newer active run",()=>{
        using var f=new Fixture();f.Start();var run=f.State.RunId!;f.State.Finish(new(null,"other-chart",1,1,1,0,0,0,false,false));f.Journal.Record(Input());
        var s=f.Read();Check(s.Runs.Single().RunId==run&&!s.Runs.Single().ClosedToNewEntries&&s.Records.Single().RunId==run,"A late different-chart result was assigned to the current run.");
    }),
    ("Mismatched explicit run session or revision never silently retargets",()=>{
        using var f=new Fixture();f.Start();f.Journal.Record(Input()with{ObservedRunId="old-run"});f.Journal.Record(Input()with{ObservedSessionId="other-session"});f.Journal.Record(Input()with{ObservedLifecycleRevision=0});
        Check(f.Read().Records.All(x=>x.RunId is null)&&f.Read().Counters.Unassociated==3,"Explicit stale association was silently rewritten.");
    }),
    ("Mid-run subscription is a baseline and does not fabricate a start",()=>{
        using var main=new MainThreadDispatcher();var log=new RecordingLog();var state=new GameEventState();state.SetAvailability(scenes:true,runs:true);state.Scene("Level");state.Controller(1,true,false,"chart");
        using var journal=new RunModificationJournal(main,log,state);using var context=new Context(main,log);journal.Record(Input());var result=journal.ForPlugin(context).Read();
        Check(result.Success&&result.Value!.Runs.Single().StartRevision is null&&result.Value.Records.Single().RunId==state.RunId,"Baseline was converted to historical start.");
    }),
    ("Closed-run history and returned snapshots stay immutable",()=>{
        using var f=new Fixture();f.Start();f.Journal.Record(Input());var first=f.Read();f.Finish();f.Start();f.Journal.Record(Input()with{Owner="two"});
        Check(first.Records.Count==1&&first.Runs.Count==1&&!first.Runs.Single().ClosedToNewEntries&&first.Records.Single().Owner=="one","New observations mutated an old snapshot.");
        bool refused=false;try{((IList<RunModificationRecord>)first.Records).Clear();}catch(NotSupportedException){refused=true;}Check(refused,"Snapshot records were mutable.");
    }),
    ("Run entry and query retention are bounded with explicit eviction counters",()=>{
        using var f=new Fixture(new(MaximumRuns:2,MaximumEntriesPerRun:2,MaximumUnassociatedEntries:2,MaximumActiveEffects:1,MaximumQueryEntries:2));
        for(int i=0;i<3;i++){f.Start();for(int j=0;j<3;j++)f.Journal.Record(Input());f.Finish();}
        var s=f.Read(new(MaximumEntries:2));Check(s.Runs.Count==2&&s.Records.Count==2&&s.Truncated&&s.Counters.EvictedRuns==1&&s.Counters.EvictedRecords>0,"Retention cap or eviction evidence missing.");
        Check(!f.View.Read(new(MaximumEntries:3)).Success,"Oversized query was accepted.");
    }),
    ("Active-effect capacity and invalid producer inputs are reported",()=>{
        using var f=new Fixture(new(MaximumActiveEffects:1));using var one=f.Journal.BeginEffect(Input());using var two=f.Journal.BeginEffect(Input()with{Owner="two"});f.Journal.Record(Input()with{Owner="bad\nowner"});f.Journal.Record(Input()with{Outcome=RunModificationOutcome.EffectActivated});
        var s=f.Read();Check(s.ActiveEffects.Count==1&&s.Counters.DroppedCapacity==1&&s.Counters.DroppedInvalid==2,"Unbounded/invalid producer input reached audit.");
    }),
    ("Logging reentry is rejected without changing an accepted operation",()=>{
        using var f=new Fixture();bool nested=false;ErrorCode? readError=null;f.Log.OnWrite=()=>{if(nested)return;nested=true;f.Journal.Record(Input());readError=f.View.Read().Error?.Code;};f.Journal.Record(Input());f.Log.OnWrite=null;
        var s=f.Read();Check(s.Records.Count==1&&s.Counters.DroppedReentrant==1&&readError==ErrorCode.Conflict,"Logging reentry poisoned the native-operation audit.");
    }),
    ("Wrong-thread producers and readers cannot touch lifecycle state",()=>{
        using var f=new Fixture();Result<RunModificationJournalSnapshot>? read=null;var thread=new Thread(()=>{f.Journal.Record(Input());read=f.View.Read();});thread.Start();thread.Join();
        Check(read?.Error?.Code==ErrorCode.WrongThread&&f.Read().Records.Count==0&&f.Read().Counters.RejectedWrongThread==1,"Worker access reached lifecycle.");
    }),
    ("Formal host scopes isolate same-owner effects and reject stopped views",()=>{
        using var f=new Fixture();var caps=new CapabilityRegistry(new("test","test","test","test"));caps.RegisterScoped<IRunModificationService>(RunModificationCapabilityIds.Read,f.Journal.ForPlugin);var events=new EventHub(f.Log);using var host=new PluginHost(caps,f.Main,events,f.Log);
        IRunModificationService? a=null,b=null;var children=new List<IPluginScope>();
        host.Register(new Plugin("owner",c=>{foreach(var name in new[]{"a","b"}){var child=((IScopedPluginContext)c).CreateScope(name);children.Add(child);var view=child.GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service!;if(name=="a")a=view;else b=view;child.Own(f.Journal.BeginEffect(Input()with{Owner=c.PluginId,TargetId=name}));}}));
        Check(host.Enable("owner").Success&&f.Read().ActiveEffects.Count==2,"Host child effects were not registered.");children[0].Dispose();Check(a!.Read().Error?.Code==ErrorCode.ScopeExpired&&b!.Read().Success&&f.Read().ActiveEffects.Count==1,"One child invalidated its sibling or retained effect.");
        host.Disable("owner");Check(!b!.Read().Success&&f.Read().ActiveEffects.Count==0,"Host stop retained a view/effect.");
    }),
    ("Disposed journal rejects old views and producer callbacks safely",()=>{
        using var f=new Fixture();f.Journal.Dispose();f.Journal.Record(Input());using var effect=f.Journal.BeginEffect(Input());Check(f.View.Read().Error?.Code==ErrorCode.ScopeExpired&&f.Journal.Counters.DroppedExpired>=2,"Disposed audit accepted callbacks.");
    })
};
int failed=0;foreach(var test in tests)try{test.Run();Console.WriteLine("PASS "+test.Name);}catch(Exception ex){failed++;Console.Error.WriteLine("FAIL "+test.Name+": "+ex);}
Console.WriteLine($"Run modification checks: {tests.Length-failed}/{tests.Length} passed.");return failed==0?0:1;
static RunModificationInput Input()=>new("one","test.source","apply","test.target",RunModificationCategory.GameplayControl,RunModificationOutcome.Applied,"Result and submission effects are unverified.");
static RunModificationInput ObservedInput(Fixture fixture)
{
    var observation = fixture.State.LifecycleSnapshot();
    return Input() with { Category = RunModificationCategory.LocalContent, ObservedRunId = observation.RunId,
        ObservedSessionId = observation.SessionId, ObservedLifecycleRevision = observation.Revision };
}
static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
sealed class RecordingLog:ILogger {public Action? OnWrite;public void Log(LogLevel level,string message,Exception? exception=null)=>OnWrite?.Invoke();}
sealed class Plugin:IRizPlugin
{
    private readonly Action<IPluginContext> _enable;public PluginManifest Manifest{get;}
    public Plugin(string id,Action<IPluginContext> enable){Manifest=new(id,new(1,0),new(1,2));_enable=enable;}
    public void Enable(IPluginContext context)=>_enable(context);public void Disable(){}
}
sealed class Context: IPluginContext,IDisposable
{
    private readonly List<IDisposable> _owned=new();public string PluginId=>"test";public ILogger Log{get;}public IMainThreadDispatcher MainThread{get;}public IEventBus Events{get;}public GameBuildDescriptor GameBuild{get;}=new("test","test","test","test");
    public Context(IMainThreadDispatcher main,ILogger log){MainThread=main;Log=log;Events=new EventHub(log);}
    public CapabilityDescriptor GetCapability(string id)=>new(id,false);public ServiceLookup<T> GetService<T>(string id)where T:class=>new(false,null,new(ErrorCode.CapabilityUnavailable,"test context",id));public T Own<T>(T value)where T:IDisposable{_owned.Add(value);return value;}
    public void Dispose(){foreach(var item in _owned.AsEnumerable().Reverse())item.Dispose();_owned.Clear();}
}
sealed class Fixture:IDisposable
{
    public MainThreadDispatcher Main{get;}=new();public RecordingLog Log{get;}=new();public GameEventState State{get;}=new();public RunModificationJournal Journal{get;}public Context Context{get;}public IRunModificationService View{get;}private long _identity;
    public Fixture(RunModificationJournalLimits? limits=null){State.SetAvailability(scenes:true,runs:true);State.ResultOrigins.Activate();State.Scene("MainMenu");Journal=new(Main,Log,State,limits);Context=new(Main,Log);View=Journal.ForPlugin(Context);}
    public void Start(){State.Scene("Level");State.Controller(++_identity,false,false,"chart",_identity+100);State.Controller(_identity,true,false,"chart",_identity+100);}
    public void Finish()
    {
        var entry=State.CaptureResultOrigin(_identity,_identity+100)??throw new InvalidOperationException("Fixture has no actual-entry origin.");
        State.Controller(_identity,true,true,"chart",_identity+100);
        if(!State.ResultOrigins.Register(entry,new ResultRetention(_identity+1000,_identity,_identity+100),out var reason))throw new InvalidOperationException(reason);
        var origin=State.ResultOrigins.Consume(_identity+1000,out var duplicate,out reason);
        if(origin is null||duplicate)throw new InvalidOperationException("Fixture failed to resolve actual returned result: "+reason);
        State.Finish(new(null,"chart",1,1,1,0,0,0,false,false),origin,reason);
    }
    public RunModificationJournalSnapshot Read(RunModificationQuery? query=null){var result=View.Read(query);if(!result.Success)throw new InvalidOperationException(result.Error?.ToString());return result.Value!;}
    public void Dispose(){State.ResultOrigins.Withdraw("fixture end");Context.Dispose();Journal.Dispose();Main.Dispose();}
}
sealed record ResultRetention(long Result,long Information,long Controller):IResultOriginRetention { public void Dispose(){} }
