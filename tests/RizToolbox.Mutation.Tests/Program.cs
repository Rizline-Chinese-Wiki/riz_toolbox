using RizToolbox.Core;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Sdk;

var tests = new (string Name, Action Run)[]
{
    ("semantic queries reject ambiguity, exclude templates and inactive objects", Queries),
    ("aliases share canonical identity and generations never revive old leases", AliasesAndGenerations),
    ("handles from another live or retired registry cannot read invalidate or mutate current bindings", CrossRegistryHandles),
    ("independent registry threads allocate distinct monotonic target generations", ConcurrentRegistryGenerations),
    ("registry liveness invalidates before roots release and cleanup is idempotent", TargetLifetimes),
    ("three plugins priority override and equal priority interleave deterministically", OverrideInterleavings),
    ("ordered transforms compose, isolate invalid provider and recompute", TransformFailures),
    ("invalid hidden overrides rejected without affecting winner", CandidateValidation),
    ("exclusive conflict lists owner and reopens after release", ExclusiveLeases),
    ("external writes adopt current game baseline and untouched targets remain untouched", ExternalBaseline),
    ("preserve external policy ends leases including initially equal value", PreserveExternal),
    ("explicit ownership policy reasserts and restores only owned writes", RejectExternal),
    ("write callback mutating then throwing rolls back and preserves older holders", WriteRollback),
    ("readback ownership mismatch invalidates without overwriting foreign writes", LostWriteOwnership),
    ("destroy and rebuild invalidates old mutations without writing dead objects", DestroyAndRebuild),
    ("plugin views auto own leases and reject cached operations after stop", ScopedServices),
    ("provider reentry rejected and callback-triggered disposal does not leave mutation", Reentrancy),
    ("main thread checks guard targets, channels and handles", ThreadChecks),
    ("invalidation during read or liveness cannot return stale generation", ReentrantTargetRead),
    ("registry shutdown blocks callback registration and releases all roots", ShutdownReentrancy),
    // This terminal boundary test intentionally exhausts the process allocator; it must run last.
    ("generation exhaustion refuses registration without wrapping or taking resource ownership", GenerationExhaustion)
};
var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} target and mutation checks passed.");
return failed == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static T Need<T>(OperationResult<T> result) { Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing result."); return result.Value!; }
static void Good(OperationResult result) => Check(result.Success, result.Error?.Message ?? "Operation failed.");
static TargetHandle<int> Target(Fixture f, string key = "ui/main/color", int value = 10)
{
    f.Value = value;
    return Need(f.Targets.Register(key, () => f.Value, () => f.Alive, new Cleanup(() => f.RootsFreed++),
        new TargetMetadata(Scene: "menu", Role: "accent", ResourceId: "accent"), new[] { "menu.accent" }));
}
static MutationChannel<int> Channel(Fixture f, TargetHandle<int> target, MutationStrategy strategy = MutationStrategy.PriorityOverride,
    ExternalWritePolicy policy = ExternalWritePolicy.AdoptAndReapply, Action<int>? write = null)
    => Need(f.Mutations.Register(target, "color", write ?? (v => { f.Writes++; f.Value = v; }), strategy, policy,
        value => value is < 0 or > 1000 ? "Value out of range." : null));

static void Queries()
{
    using var f = new Fixture();
    Need(f.Targets.Register("scene-a/action", () => 1, metadata: new(Scene: "a", Role: "action", ResourceId: "shared")));
    Need(f.Targets.Register("scene-b/action", () => 2, metadata: new(Scene: "b", Role: "action", ResourceId: "shared")));
    Need(f.Targets.Register("template/action", () => 3, metadata: new(Scene: "a", Role: "action", ResourceId: "shared", Template: true)));
    Need(f.Targets.Register("inactive/action", () => 4, metadata: new(Scene: "a", Role: "action", ResourceId: "shared", Active: false)));
    var result = f.Targets.Resolve<int>(new(ResourceId: "shared"));
    Check(result.Kind == TargetMatchKind.Ambiguous && result.Target is null && result.Candidates.Count == 2, "Ambiguous query selected first object.");
    result = f.Targets.Resolve<int>(new(Scene: "a", ResourceId: "shared"));
    Check(result.Kind == TargetMatchKind.Unique && result.Target!.Key == "scene-a/action", "Semantic query failed.");
    Check(f.Targets.Resolve<int>(new(Scene: "missing")).Kind == TargetMatchKind.Missing, "Absent query matched.");
    Check(f.Targets.Resolve<string>(new(Key: "scene-a/action")).Kind == TargetMatchKind.Missing, "Wrong value type matched.");
    Check(f.Targets.Resolve<int>(new(Scene: "a", IncludeTemplates: true, ActiveOnly: false)).Candidates.Count == 3, "Explicit template/inactive query failed.");
}

static void AliasesAndGenerations()
{
    using var f = new Fixture();
    var target = Target(f); var alias = f.Targets.Resolve<int>(new(Key: "menu.accent"));
    Check(alias.Target == target, "Alias did not canonicalize to the same generation.");
    var lease = Need(f.Targets.Acquire(target));
    Good(f.Targets.SetAliases(target, new[] { "menu.accent", "newly-discovered-menu-role" }));
    Check(f.Targets.Resolve<int>(new(Key: "newly-discovered-menu-role")).Target == target && lease.IsValid, "Late alias discovery invalidated existing leases.");
    Good(f.Targets.UpdateMetadata(target, new(Scene: "menu", Role: "accent", Active: false)));
    Check(f.Targets.Resolve<int>(new(Key: "menu.accent")).Kind == TargetMatchKind.Missing && lease.IsValid, "Activity metadata was ignored or unnecessarily invalidated generation.");
    Good(f.Targets.UpdateMetadata(target, new(Scene: "menu", Role: "accent")));
    Good(f.Targets.Invalidate(target));
    var replacement = Target(f);
    Check(replacement.Key == target.Key && replacement.Generation > target.Generation && !lease.IsValid && !lease.Read().Success, "Old generation revived after rebind.");
    Check(Need(f.Targets.Acquire(replacement)).Read().Value == 10, "Replacement lease failed.");
    Need(f.Targets.Register("other", () => 1, aliases: new[] { "menu.accent" }));
    Check(f.Targets.Resolve<int>(new(Key: "menu.accent")).Kind == TargetMatchKind.Ambiguous, "Shared alias silently selected one target.");
}

static void TargetLifetimes()
{
    using var f = new Fixture();
    var target = Target(f); var lease = Need(f.Targets.Acquire(target)); var beforeRoot = false;
    f.Targets.Invalidated += identity => { beforeRoot = !lease.IsValid && f.RootsFreed == 0; };
    f.Alive = false;
    Check(f.Targets.GetTargets().Count == 0 && beforeRoot && f.RootsFreed == 1, "Invalidation/root order failed.");
    Good(f.Targets.Invalidate(target)); lease.Dispose(); lease.Dispose(); f.Targets.Dispose();
    Check(f.RootsFreed == 1, "Root cleanup was not exactly once.");
    using var g = new Fixture();
    Need(g.Targets.Register("bad-root", () => 1, lifetime: new Cleanup(() => throw new Exception("root failure"))));
    Need(g.Targets.Register("good-root", () => 1, lifetime: new Cleanup(() => g.RootsFreed++)));
    g.Targets.Dispose(); Check(g.RootsFreed == 1, "One cleanup failure blocked other roots.");
}

static void CrossRegistryHandles()
{
    foreach (var retirePrevious in new[] { false, true })
    {
        using var previous = new Fixture(); var old = Target(previous, value: 11);
        using var oldLease = Need(previous.Targets.Acquire(old));
        if (retirePrevious) previous.Targets.Dispose();
        using var current = new Fixture(); var replacement = Target(current, value: 50);
        var channel = Channel(current, replacement);
        using var context = new Context("current-plugin", current.Main);

        var foreignRead = current.Targets.Acquire(old); foreignRead.Value?.Dispose();
        var foreignMetadata = current.Targets.UpdateMetadata(old, new(Scene: "foreign"));
        var foreignAliases = current.Targets.SetAliases(old, new[] { "foreign.alias" });
        var foreignMutation = current.Mutations.ForPlugin(context).Override(new MutationKey<int>(old, "color"), 70);
        foreignMutation.Value?.Dispose();
        Good(current.Targets.Invalidate(old)); // An absent generation is an idempotent no-op.

        Check(!foreignRead.Success && !foreignMetadata.Success && !foreignAliases.Success && !foreignMutation.Success,
            "A handle from another registry was accepted by the replacement registry.");
        Check(current.Writes == 0 && current.Value == 50 && channel.Valid && current.RootsFreed == 0,
            "An old handle wrote to or invalidated the replacement binding.");
        using var replacementLease = Need(current.Targets.Acquire(replacement));
        Check(replacementLease.Read().Value == 50
            && current.Targets.Resolve<int>(new(Key: "menu.accent", Scene: "menu")).Target == replacement
            && current.Targets.Resolve<int>(new(Key: "foreign.alias")).Kind == TargetMatchKind.Missing,
            "Replacement data or semantic metadata changed through a foreign handle.");
        Check(old.Key == replacement.Key && replacement.Generation > old.Generation,
            "Registry recreation reused or reversed a process binding generation.");
        Check(retirePrevious ? !oldLease.IsValid : oldLease.Read().Value == 11,
            "The original registry's independent lease was corrupted.");
    }
}

static void ConcurrentRegistryGenerations()
{
    var generations = new System.Collections.Concurrent.ConcurrentBag<long>();
    Parallel.For(0, 8, _ =>
    {
        // Each registry keeps its own verified thread; only generation allocation is shared.
        using var f = new Fixture(); long previous = 0;
        for (var i = 0; i < 32; i++)
        {
            var target = Need(f.Targets.Register("concurrent/" + i, () => i));
            Check(target.Generation > previous, "A registry's allocation order was not monotonic.");
            previous = target.Generation; generations.Add(target.Generation);
        }
    });
    Check(generations.Count == 256 && generations.Distinct().Count() == generations.Count,
        "Independent registry threads reused target generations.");
}

static void GenerationExhaustion()
{
    // Fault injection reaches the otherwise impractical boundary through the real Register path.
    // Never reset it: wrapping or restarting this allocator is the behavior under test.
    var counter = typeof(SemanticTargetRegistry).GetField("_generation",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Expected one shared target-generation allocator.");
    counter.SetValue(null, long.MaxValue - 1);
    using var f = new Fixture(); var final = Target(f, value: 42);
    using var finalLease = Need(f.Targets.Acquire(final));
    var refusedRootDisposals = 0;
    using var refusedRoot = new Cleanup(() => refusedRootDisposals++);
    var refused = f.Targets.Register("exhausted", () => 1, lifetime: refusedRoot);
    using var other = new Fixture();
    var again = other.Targets.Register("after-exhaustion", () => 2);
    Check(final.Generation == long.MaxValue && !refused.Success && !again.Success
        && refused.Error!.Code==ErrorCode.TargetGenerationExhausted
        && again.Error!.Code==ErrorCode.TargetGenerationExhausted,
        "Generation exhaustion threw, wrapped, or allowed a later registry to restart allocation.");
    Check(f.Targets.GetTargets().Count == 1 && other.Targets.GetTargets().Count == 0
        && finalLease.IsValid && finalLease.Read().Value == 42 && refusedRootDisposals == 0,
        "Refused registration transferred ownership or damaged the final valid binding.");
    Good(f.Targets.UpdateMetadata(final, new(Scene: "still-valid")));
    Check(f.Targets.Resolve<int>(new(Scene: "still-valid")).Target == final,
        "Generation exhaustion unnecessarily disabled metadata updates on an existing binding.");
    refusedRoot.Dispose();
    Check(refusedRootDisposals == 1, "The caller could not release a refused registration's lifetime.");
}

static void OverrideInterleavings()
{
    using var f = new Fixture(); var target = Target(f); var channel = Channel(f, target);
    var a = Need(channel.Override("a", 20, 1)); var b = Need(channel.Override("b", 30, 3)); var c = Need(channel.Override("c", 40, 3));
    Check(f.Value == 40, "Equal priority did not prefer latest registration.");
    var snapshot = channel.Describe();
    Check(snapshot.Holders.Count == 3 && snapshot.Holders.Single(x => x.Winner).Owner == "c", "Winner/holder diagnostics wrong.");
    b.Dispose(); Check(f.Value == 40, "Removing hidden layer changed winner.");
    c.Dispose(); Check(f.Value == 20, "Removing winner did not expose next layer.");
    Good(a.SetValue(21)); Check(f.Value == 21, "Live candidate update failed.");
    a.Dispose(); a.Dispose(); Check(f.Value == 10, "Last removal did not restore baseline or was not idempotent.");
}

static void TransformFailures()
{
    using var f = new Fixture(); var target = Target(f); var channel = Channel(f, target, MutationStrategy.OrderedTransform);
    var a = Need(channel.Transform("a", v => v + 2, 0));
    var b = Need(channel.Transform("b", v => v * 3, 10));
    var c = Need(channel.Transform("c", v => v + 1, 10));
    Check(f.Value == 37, "Transform ordering wrong.");
    var faults = 0; f.Mutations.ProviderFaulted += (owner, ex) => { if (owner == "broken") faults++; };
    Check(!channel.Transform("broken", _ => -1, 5).Success && faults == 1 && f.Value == 37, "Invalid transform affected surviving chain.");
    Check(!channel.Transform("broken", _ => throw new Exception("provider failure"), 5).Success && faults == 2 && f.Value == 37, "Throwing transform was not isolated.");
    b.Dispose(); Check(f.Value == 13, "Middle transform removal failed.");
    a.Dispose(); Check(f.Value == 11, "First transform removal failed.");
    c.Dispose(); Check(f.Value == 10, "Transform cleanup did not restore baseline.");
}

static void CandidateValidation()
{
    using var f = new Fixture(); var channel = Channel(f, Target(f));
    var winner = Need(channel.Override("winner", 80, 100));
    Check(!channel.Override("hidden-invalid", -10, -1).Success && f.Value == 80 && channel.Describe().Holders.Count == 1, "Invalid hidden candidate accepted.");
    Check(!winner.SetValue(-2).Success && f.Value == 80, "Invalid SetValue changed live value.");
}

static void ExclusiveLeases()
{
    using var f = new Fixture(); var channel = Channel(f, Target(f), MutationStrategy.Exclusive);
    var owner = Need(channel.AcquireExclusive("a", 20)); var conflict = channel.AcquireExclusive("b", 30);
    Check(!conflict.Success && conflict.Error!.Code==ErrorCode.Conflict && conflict.Error.Message.Contains("a") && f.Value == 20, "Exclusive conflict lacks owner or wrote value.");
    Check(!channel.Override("b", 30).Success, "Override bypassed exclusive strategy.");
    owner.Dispose(); var replacement = Need(channel.AcquireExclusive("b", 30)); replacement.Dispose();
    Check(f.Value == 10, "Exclusive lease did not release ownership.");
}

static void ExternalBaseline()
{
    using var f = new Fixture(); var channel = Channel(f, Target(f));
    f.Value = 12; Good(channel.Refresh()); Check(f.Writes == 0, "Idle refresh wrote stale registration baseline.");
    var a = Need(channel.Override("a", 20)); f.Value = 15; Good(channel.Refresh());
    Check(f.Value == 20 && channel.Describe().Baseline == 15, "Game write did not update baseline and reapply mutation.");
    a.Dispose(); Check(f.Value == 15, "Release restored old game baseline.");
    var writes = f.Writes; f.Value = 17; Good(channel.Refresh());
    Check(f.Value == 17 && f.Writes == writes && channel.Describe().Baseline == 17, "Idle channel overwrote a game write after its final lease ended.");
    channel.Dispose(); Check(f.Value == 17, "Dispose modified untouched external value.");
}

static void PreserveExternal()
{
    using var f = new Fixture(); var channel = Channel(f, Target(f), policy: ExternalWritePolicy.PreserveExternalAndInvalidate);
    var a = Need(channel.Override("a", 20)); f.Value = 15; Good(channel.Refresh());
    Check(f.Value == 15 && !a.IsActive && channel.Describe().Holders.Count == 0, "External value not preserved.");
    a.Dispose(); Check(f.Value == 15, "Expired holder restored stale value.");
    var equal = Need(channel.Override("a", 15)); f.Value = 16; Good(channel.Refresh());
    Check(f.Value == 16 && !equal.IsActive, "Equal initial candidate hid external write.");
}

static void RejectExternal()
{
    using var f = new Fixture(); var channel = Channel(f, Target(f), policy: ExternalWritePolicy.RejectExternalWhileHeld);
    var a = Need(channel.Override("a", 20)); f.Value = 15; Good(channel.Refresh());
    Check(f.Value == 20 && channel.Describe().Baseline == 10, "Explicit owned policy did not reassert mutation.");
    f.Value = 17; channel.Dispose(); Check(f.Value == 17 && !a.IsActive, "Dispose overwrote external value no longer owned.");
}

static void WriteRollback()
{
    using var f = new Fixture(); var target = Target(f); var fail = false;
    var channel = Channel(f, target, write: v => { f.Value = v; f.Writes++; if (fail) { fail = false; throw new Exception("writer failed after write"); } });
    var a = Need(channel.Override("a", 20)); fail = true;
    Check(!channel.Override("b", 30, 1).Success && f.Value == 20 && a.IsActive && channel.Valid, "Failed write did not restore existing holder.");
    fail = true; Check(!a.SetValue(25).Success && f.Value == 20, "Failed SetValue did not restore old candidate.");
    a.Dispose(); Check(f.Value == 10, "Failed write damaged original baseline.");
}

static void LostWriteOwnership()
{
    using var f = new Fixture(); var target = Target(f);
    var channel = Channel(f, target, write: v => { f.Value = 777; f.Writes++; });
    Check(!channel.Override("a", 20).Success && f.Value == 777 && !channel.Valid && f.Writes == 1, "Readback mismatch overwrote a foreign value or kept leases live.");
}

static void DestroyAndRebuild()
{
    using var f = new Fixture(); var target = Target(f); var channel = Channel(f, target); var a = Need(channel.Override("a", 20));
    var writes = f.Writes; f.Alive = false; f.Targets.GetTargets();
    Check(!a.IsActive && !channel.Valid && f.RootsFreed == 1 && f.Writes == writes, "Destroyed target was accessed during invalidation.");
    f.Alive = true; var replacement = Target(f, value: 50); var next = Channel(f, replacement);
    Check(!a.SetValue(60).Success && !f.Mutations.ForPlugin(new Context("outside", f.Main)).Override(new MutationKey<int>(target, "color"), 70).Success, "Old generation affected rebuilt target.");
    var b = Need(next.Override("b", 60)); b.Dispose(); Check(f.Value == 50, "New generation reused old baseline.");
}

static void ScopedServices()
{
    using var f = new Fixture(); var target = Target(f); Channel(f, target);
    var context = new Context("plugin", f.Main); var targets = f.Targets.ForPlugin(context); var service = f.Mutations.ForPlugin(context);
    var targetLease = Need(targets.Acquire(target)); var mutation = Need(service.Override(new(target, "color"), 20));
    Check(context.Resources.Count == 2 && f.Value == 20, "Plugin service did not auto-own leases.");
    context.Dispose(); Check(f.Value == 10 && !Need(f.Targets.Acquire(target)).Read().Value.Equals(20), "Scope cleanup failed.");
    bool rejected = false; try { mutation.SetValue(30); } catch (ObjectDisposedException) { rejected = true; }
    Check(rejected, "Cached mutation handle bypassed active context.");
    rejected = false; try { targetLease.Read(); } catch (ObjectDisposedException) { rejected = true; }
    Check(rejected, "Cached target handle bypassed active context.");
    rejected = false; try { service.Override(new(target, "color"), 30); } catch (ObjectDisposedException) { rejected = true; }
    Check(rejected && f.Value == 10, "Cached mutation service modified stopped scope.");
}

static void Reentrancy()
{
    using var f = new Fixture(); var channel = Channel(f, Target(f), MutationStrategy.OrderedTransform);
    var rejected = false;
    var a = Need(channel.Transform("a", value => { rejected = !channel.Transform("nested", v => v).Success; return value + 1; }));
    Check(rejected && f.Value == 11 && channel.Describe().Holders.Count == 1, "Provider reentry changed stack.");
    Check(!channel.Transform("bad", _ => throw new Exception("bad"), onFault: ex => a.Dispose()).Success && f.Value == 10, "Fault cleanup left stale transform state.");
}

static void ThreadChecks()
{
    using var f = new Fixture(); var target = Target(f); var channel = Channel(f, target); var holder = Need(channel.Override("a", 20));
    var failures = 0;
    var worker = new Thread(() =>
    {
        try { f.Targets.Resolve<int>(new()); } catch (InvalidOperationException) { failures++; }
        try { channel.Refresh(); } catch (InvalidOperationException) { failures++; }
        try { holder.Dispose(); } catch (InvalidOperationException) { failures++; }
    }); worker.Start(); worker.Join();
    Check(failures == 3 && holder.IsActive, "Wrong thread mutation was accepted or partially disposed handle.");
}

static void ReentrantTargetRead()
{
    using var f = new Fixture(); TargetHandle<int>? handle = null;
    handle = Need(f.Targets.Register("transient", () => { f.Targets.Invalidate(handle!); return 3; }));
    var lease = Need(f.Targets.Acquire(handle));
    Check(!lease.Read().Success && !lease.IsValid, "Read callback returned value from an invalidated generation.");
    TargetHandle<int>? aliveHandle = null; var invalidate = false;
    aliveHandle = Need(f.Targets.Register("liveness", () => 1, () => { if (invalidate) f.Targets.Invalidate(aliveHandle!); return true; }));
    invalidate = true;
    Check(!f.Targets.Acquire(aliveHandle).Success, "Reentrant liveness invalidation returned a live lease.");
}

static void ShutdownReentrancy()
{
    using var f = new Fixture(); Target(f); var accepted = true;
    f.Targets.Invalidated += _ => accepted = f.Targets.Register("unexpected", () => 1).Success;
    f.Targets.Dispose();
    Check(!accepted && f.RootsFreed == 1 && f.Targets.GetTargets().Count == 0, "Shutdown invalidation callback registered a leaked target.");
}

sealed class Fixture : IDisposable
{
    public MainThreadDispatcher Main = new(); public Logger Log = new();
    public SemanticTargetRegistry Targets; public MutationCoordinator Mutations;
    public int Value, Writes, RootsFreed; public bool Alive = true;
    public Fixture() { Targets = new(Main, Log); Mutations = new(Targets, Main, Log); }
    public void Dispose() { Mutations.Dispose(); Targets.Dispose(); Main.Dispose(); }
}
sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
sealed class Cleanup : IDisposable
{
    private Action? _action; public Cleanup(Action action) => _action = action;
    public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
}
sealed class Context : IPluginContext, IMainThreadDispatcher, IDisposable
{
    private readonly MainThreadDispatcher _main; private bool _active = true;
    public List<IDisposable> Resources = new(); public string PluginId { get; }
    public Context(string id, MainThreadDispatcher main) { PluginId = id; _main = main; }
    public ILogger Log { get; } = new Logger(); public IMainThreadDispatcher MainThread => this;
    public IEventBus Events => throw new NotSupportedException();
    public GameBuildDescriptor GameBuild => new("test", "test", "test", "test");
    public CapabilityDescriptor GetCapability(string id) => throw new NotSupportedException();
    public ServiceLookup<T> GetService<T>(string id) where T : class => throw new NotSupportedException();
    public T Own<T>(T resource) where T : IDisposable { VerifyAccess(); Resources.Add(resource); return resource; }
    public bool IsMainThread => _main.IsMainThread;
    public void VerifyAccess() { _main.VerifyAccess(); if (!_active) throw new ObjectDisposedException(PluginId); }
    public Task InvokeAsync(Action action, CancellationToken token = default) => _main.InvokeAsync(() => { VerifyAccess(); action(); }, token);
    public void Dispose() { _main.VerifyAccess(); if (!_active) return; _active = false; foreach (var item in Resources.AsEnumerable().Reverse()) item.Dispose(); }
}
