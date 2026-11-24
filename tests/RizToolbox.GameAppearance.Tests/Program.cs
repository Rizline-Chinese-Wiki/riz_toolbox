using RizToolbox.Adapter.GameAppearance;
using RizToolbox.Core;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Sdk;
using static Checks;

var tests = new (string Name, Action Run)[] {
    ("Property degradation reports the missing role as Unsupported while available note targets still mutate and restore", () => {
        using var f = new Fixture();
        f.Backend.Unsupported[GameAppearanceRoles.TrackStartColor] = "Exact get_startColor metadata is absent.";
        var excluded = f.Add("missing-line", GameAppearanceRoles.TrackStartColor);
        var note = f.Add("live-note", GameAppearanceRoles.NoteRendererColor);
        Equal(1, f.Service.Refresh(true)); var p = f.Plugin();
        var supported = Need(p.Appearance!.GetSupportedProperties());
        Equal(3, supported.Count); Check(supported.All(value => value.Role != GameAppearanceRoles.TrackStartColor));
        Failure(p.Appearance.GetTargets(new(Role: GameAppearanceRoles.TrackStartColor)), ErrorCode.OperationUnsupported);
        Failure(p.Appearance.OverrideColor(new(new(Role: GameAppearanceRoles.TrackStartColor), f.RunId, Red)), ErrorCode.OperationUnsupported);
        Equal(0, excluded.Outstanding); Equal(0, excluded.Writes);
        using (var lease = Need(p.Appearance.OverrideColor(new(new(Role: GameAppearanceRoles.NoteRendererColor), f.RunId, Red))))
            Equal(Red, note.Color);
        Equal(note.Original, note.Color);
    }),
    ("Known game properties are experimental independently of whether objects have appeared", () => {
        using var f = new Fixture(); f.Service.Refresh(true); var p = f.Plugin(); Equal(4, Need(p.Appearance!.GetSupportedProperties()).Count);
        Check(Need(p.Appearance.GetSupportedProperties()).All(x => x.Experimental && x.ExternalWrites == ExternalWritePolicy.AdoptAndReapply));
        Equal(0, Need(p.Appearance.GetTargets()).Count);
        var missing = p.Appearance.OverrideColor(new(new(Role: GameAppearanceRoles.TrackStartColor), f.RunId, Red));
        Failure(missing, ErrorCode.TargetMissing, true); Equal(0, f.Sources.Sum(x => x.Writes));
    }),
    ("Discovery requires observed run/content identity and permits a paused current run", () => {
        using var f = new Fixture(); var x = f.Add(); var snapshot = f.State; f.State = null; Equal(0, f.Service.Refresh(true));
        f.State = snapshot! with { Phase = RunLifecyclePhase.Loading }; Equal(0, f.Service.Refresh(true));
        f.State = snapshot! with { ChartId = null }; Equal(0, f.Service.Refresh(true));
        f.State = snapshot! with { Phase = RunLifecyclePhase.Paused, Paused = true }; Equal(1, f.Service.Refresh(true));
        var p = f.Plugin(); using var lease = Need(p.Appearance!.OverrideColor(new(new(Key: x.Alias), f.RunId, Red))); Equal(Red, x.Color);
    }),
    ("Only declared active game roles with matching content/scene are admitted", () => {
        using var f = new Fixture(); f.Add("template").Template = true; f.Add("inactive").Active = false;
        f.Add("old-chart").Content = "foreign"; f.Add("old-scene").Scene = "foreign"; f.Add("dead").Alive = false;
        var live = f.Add("live"); Equal(1, f.Service.Refresh(true)); Equal(1, f.Targets.GetTargets().Count); Equal(1, live.Outstanding);
        Check(f.Sources.Where(x => x != live).All(x => x.Outstanding == 0));
    }),
    ("Two independent plugin scopes and two aliases share the exact same native property channel", () => {
        using var f = new Fixture(); var x = f.Add(); f.Service.Refresh(true);
        var low = f.Plugin("appearance.low"); var high = f.Plugin("appearance.high");
        var target = f.Resolve(x.Alias); Equal(target, f.Resolve(x.SecondAlias));
        var lowLease = Need(low.Appearance!.OverrideColor(new(new(Key: x.Alias), f.RunId, Red, 10)));
        var highLease = NeedOp(high.Mutations!.Override(new MutationKey<ResourceColor>(f.Resolve(x.SecondAlias), "color"), Blue, 20));
        Equal(Blue, x.Color); var snapshot = NeedOp(low.Mutations!.Describe(new MutationKey<ResourceColor>(target, "color")));
        Equal(2, snapshot.Holders.Count); Check(snapshot.Holders.Select(h => h.Owner).ToHashSet().SetEquals(new[] { "appearance.low", "appearance.high" }));
        x.Color = Green; f.Service.Refresh(); Equal(Blue, x.Color);
        f.Host.Disable("appearance.high"); Equal(Red, x.Color); Throws<ObjectDisposedException>(() => _ = highLease.IsActive);
        f.Host.Disable("appearance.low"); Equal(Green, x.Color); Throws<ObjectDisposedException>(() => _ = lowLease.IsActive);
    }),
    ("Concrete note, track start and track end channels are independent", () => {
        using var f = new Fixture(); var start = f.Add(); var end = f.Add("end", GameAppearanceRoles.TrackEndColor);
        var note = f.Add("note", GameAppearanceRoles.NoteRendererColor); f.Service.Refresh(true); var p = f.Plugin();
        using var a = Need(p.Appearance!.OverrideColor(new(new(Role: GameAppearanceRoles.TrackStartColor), f.RunId, Red)));
        using var b = Need(p.Appearance.OverrideColor(new(new(Role: GameAppearanceRoles.TrackEndColor), f.RunId, Blue)));
        using var c = Need(p.Appearance.OverrideColor(new(new(Role: GameAppearanceRoles.NoteRendererColor), f.RunId, Green)));
        Equal(Red, start.Color); Equal(Blue, end.Color); Equal(Green, note.Color);
        Equal(3, Need(p.Appearance.GetTargets()).Count);
    }),
    ("A broad multi-candidate query is rejected and a precise alias succeeds", () => {
        using var f = new Fixture(); var a = f.Add("first"); var b = f.Add("second"); f.Service.Refresh(true); var p = f.Plugin();
        var result = p.Appearance!.OverrideColor(new(new(Role: a.Role), f.RunId, Red)); Failure(result, ErrorCode.AmbiguousTarget);
        Equal(a.Original, a.Color); Equal(b.Original, b.Color);
        using var lease = Need(p.Appearance.OverrideColor(new(new(Key: b.Alias, Role: b.Role), f.RunId, Blue))); Equal(Blue, b.Color);
    }),
    ("A changed query cannot write a foreign shared UI target", () => {
        using var f = new Fixture(); f.Service.Refresh(true); var p = f.Plugin(); var value = Green;
        var other = NeedOp(f.Targets.Register("game.ui/image/test/color", () => value, metadata: new(Role: "image-color")));
        using var channel = NeedOp(f.Mutations.Register(other, "color", x => value = x));
        var refused = p.Appearance!.OverrideColor(new(new(Key: other.Key), f.RunId, Red)); Failure(refused, ErrorCode.TargetMissing, true); Equal(Green, value);
    }),
    ("Stale expected run or scene generation is rejected without writing", () => {
        using var f = new Fixture(); var x = f.Add(); f.Service.Refresh(true); var p = f.Plugin(); var before = x.Writes;
        Failure(p.Appearance!.OverrideColor(new(new(Key: x.Alias), "old-run", Red)), ErrorCode.TargetExpired);
        Failure(p.Appearance.OverrideColor(new(new(Key: x.Alias), f.RunId, Red, SceneGeneration: 999)), ErrorCode.TargetExpired);
        Equal(before, x.Writes);
    }),
    ("Old generic mutation leases expire immediately when lifecycle changes before Refresh", () => {
        using var f = new Fixture(); var x = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        var handle = f.Resolve(x.Alias); var lease = Need(p.Appearance!.OverrideColor(new(new(Key: x.Alias), f.RunId, Red)));
        f.State = f.State! with { RunId = "next-run" }; var before = x.Writes;
        var result = lease.SetValue(Blue); Check(!result.Success); Check(!lease.IsActive); Equal(before, x.Writes);
        Check(!f.Targets.Acquire(handle).Success); f.Service.Refresh(true);
        Check(f.Resolve(x.Alias).Generation > handle.Generation);
    }),
    ("Scene/object replacement never revives an old handle or writes a dead native object", () => {
        using var f = new Fixture(); var first = f.Add(); f.Service.Refresh(true); var p = f.Plugin(); var handle = f.Resolve(first.Alias);
        var lease = Need(p.Appearance!.OverrideColor(new(new(Key: first.Alias), f.RunId, Red)));
        first.Alive = false; var before = first.Writes; f.Service.Refresh(true); Equal(before, first.Writes); Equal(0, first.Outstanding); Check(!lease.IsActive);
        var next = new Source(first.Id, first.Role); f.Sources.Clear(); f.Sources.Add(next); f.Service.Refresh(true); var rebuilt = f.Resolve(next.Alias);
        Check(rebuilt.Key != handle.Key && rebuilt.Generation > handle.Generation); Check(!f.Targets.Acquire(handle).Success);
        f.State = f.State! with { SceneGeneration = f.State.SceneGeneration + 1 }; f.Service.Refresh(true);
        Check(f.Resolve(next.Alias).Generation > rebuilt.Generation); Check(!f.Targets.Acquire(rebuilt).Success);
    }),
    ("Pool ownership transfer expires a role even when the renderer object remains alive", () => {
        using var f = new Fixture(); var x = f.Add("note", GameAppearanceRoles.NoteRendererColor); f.Service.Refresh(true); var p = f.Plugin();
        var handle = f.Resolve(x.Alias); var lease = Need(p.Appearance!.OverrideColor(new(new(Key: x.Alias), f.RunId, Blue)));
        var writes = x.Writes; x.OwnershipCurrent = false; f.Service.Refresh(true); Equal(writes, x.Writes);
        Check(!lease.IsActive); Check(!f.Targets.Acquire(handle).Success); Equal(0, x.Outstanding);
    }),
    ("Discovery order and alias changes preserve the same object generation and holders", () => {
        using var f = new Fixture(); var a = f.Add("first"); f.Add("second"); f.Service.Refresh(true); var handle = f.Resolve(a.Alias); var p = f.Plugin();
        using var lease = Need(p.Appearance!.OverrideColor(new(new(Key: a.Alias), f.RunId, Red)));
        f.Sources.Reverse(); a.ExtraAliases.Add("game.track/late-alias"); f.Service.Refresh(true);
        Equal(handle, f.Resolve(a.Alias)); Equal(handle, f.Resolve("game.track/late-alias")); Equal(1, a.Outstanding); Equal(Red, a.Color);
        a.ExtraAliases.Clear(); f.Service.Refresh(true); Equal(handle, f.Resolve(a.Alias)); Equal(Red, a.Color);
    }),
    ("Duplicate physical property ownership is refused without creating parallel channels", () => {
        using var f = new Fixture(); var a = f.Add("owner-a"); var b = f.Add("owner-b"); b.Physical = a.Physical;
        Equal(0, f.Service.Refresh(true)); Equal(0, f.Targets.GetTargets().Count); Equal(0, a.Outstanding); Equal(0, b.Outstanding);
        Check(f.Log.Messages.Any(x => x.Contains("physical property", StringComparison.Ordinal)));
    }),
    ("Ring and note roles cannot coordinate the same physical SpriteRenderer color independently", () => {
        using var f = new Fixture(); var ring = f.Add("group", GameAppearanceRoles.TrackRingColor);
        var note = f.Add("note", GameAppearanceRoles.NoteRendererColor); note.Physical = ring.Physical;
        Equal(0, f.Service.Refresh(true)); Equal(0, f.Targets.GetTargets().Count);
        Equal(0, ring.Outstanding); Equal(0, note.Outstanding); Equal(0, ring.Writes); Equal(0, note.Writes);
    }),
    ("A discovery failure after yielding candidates publishes none and releases temporary roots", () => {
        using var f = new Fixture(); var a = f.Add(); f.Backend.FailAfter = 1; Equal(0, f.Service.Refresh(true));
        Equal(0, a.Outstanding); Equal(0, f.Targets.GetTargets().Count); Equal(0, a.Writes);
        f.Backend.FailAfter = null; Equal(1, f.Service.Refresh(true)); Equal(1, a.Outstanding);
    }),
    ("Inactive live native receivers restore the owned baseline before invalidation", () => {
        using var f = new Fixture(); var x = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        var lease = Need(p.Appearance!.OverrideColor(new(new(Key: x.Alias), f.RunId, Red))); x.Color = Green; f.Service.Refresh();
        x.Active = false; f.Service.Refresh(true); Equal(Green, x.Color); Check(!lease.IsActive); Equal(0, x.Outstanding);
    }),
    ("A fresh external game write is preserved during scope cleanup without a discovery tick", () => {
        using var f = new Fixture(); var x = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        Need(p.Appearance!.OverrideColor(new(new(Key: x.Alias), f.RunId, Red))); x.Color = Blue;
        f.Host.Disable(p.Manifest.Id); Equal(Blue, x.Color);
    }),
    ("Color validation rejects nonfinite/out-of-range values while preserving the current winner", () => {
        using var f = new Fixture(); var x = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        using var lease = Need(p.Appearance!.OverrideColor(new(new(Key: x.Alias), f.RunId, Red)));
        foreach (var bad in new[] { new ResourceColor(float.NaN, 0, 0), new ResourceColor(0, float.PositiveInfinity, 0), new ResourceColor(2, 0, 0) })
            Failure(p.Appearance.OverrideColor(new(new(Key: x.Alias), f.RunId, bad)), ErrorCode.InvalidArgument);
        Equal(Red, x.Color); Check(lease.IsActive);
    }),
    ("A setter that mutates then throws restores the prior coordinated value", () => {
        using var f = new Fixture(); var x = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        using var old = Need(p.Appearance!.OverrideColor(new(new(Key: x.Alias), f.RunId, Green, 5)));
        x.OnWrite = v => { x.Color = v; if (v == Red) throw new InvalidOperationException("controlled setter failure"); };
        Failure(p.Appearance.OverrideColor(new(new(Key: x.Alias), f.RunId, Red, 10)), ErrorCode.MutationWriteFailed);
        Equal(Green, x.Color); Check(old.IsActive);
    }),
    ("Rendered read-back mismatch refuses success and preserves the actual external value", () => {
        using var f = new Fixture(); var x = f.Add(); f.Service.Refresh(true); var p = f.Plugin(); x.OnWrite = v => x.Color = Blue;
        Failure(p.Appearance!.OverrideColor(new(new(Key: x.Alias), f.RunId, Red)), ErrorCode.MutationWriteFailed);
        Equal(Blue, x.Color); f.Service.Dispose(); Equal(Blue, x.Color); Equal(0, x.Outstanding);
    }),
    ("A failed baseline getter releases registration roots and exposes no partial target", () => {
        using var f = new Fixture(); var x = f.Add(); x.FailRead = true; Equal(0, f.Service.Refresh(true));
        Equal(0, f.Targets.GetTargets().Count); Equal(0, x.Outstanding); Equal(0, x.Writes);
    }),
    ("The cache bound releases unregistered candidate roots and retains only published objects", () => {
        using var f = new Fixture(); for (var i = 0; i < GameAppearanceService.MaximumTargets + 5; ++i) f.Add(i.ToString());
        Equal(GameAppearanceService.MaximumTargets, f.Service.Refresh(true)); Equal(GameAppearanceService.MaximumTargets, f.Sources.Sum(x => x.Outstanding));
        f.Service.Dispose(); Equal(0, f.Sources.Sum(x => x.Outstanding));
    }),
    ("Disposing appearance leaves shared UI targets and channels available", () => {
        using var f = new Fixture(); var x = f.Add(); f.Service.Refresh(true); var p = f.Plugin(); var value = Green;
        var target = NeedOp(f.Targets.Register("shared.foreign", () => value)); using var channel = NeedOp(f.Mutations.Register(target, "color", v => value = v));
        Need(p.Appearance!.OverrideColor(new(new(Key: x.Alias), f.RunId, Red))); f.Service.Dispose(); Equal(x.Original, x.Color); Equal(0, x.Outstanding);
        Check(f.Targets.Acquire(target).Success); using var lease = NeedOp(p.Mutations!.Override(new MutationKey<ResourceColor>(target, "color"), Blue)); Equal(Blue, value);
    }),
    ("Expired cached service views return structured scope failures and cannot recreate targets", () => {
        using var f = new Fixture(); var x = f.Add(); f.Service.Refresh(true); var p = f.Plugin(); var view = p.Appearance!;
        f.Host.Disable(p.Manifest.Id); var writes = x.Writes; var roots = x.Outstanding;
        Failure(view.GetTargets(), ErrorCode.ScopeExpired); Failure(view.GetSupportedProperties(), ErrorCode.ScopeExpired);
        Failure(view.OverrideColor(new(new(Key: x.Alias), f.RunId, Red)), ErrorCode.ScopeExpired); Equal(writes, x.Writes); Equal(roots, x.Outstanding);
    }),
    ("Wrong-thread access returns a capability-scoped error without touching receivers", () => {
        using var f = new Fixture(); var x = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        Task.Run(() => { Failure(p.Appearance!.GetTargets(), ErrorCode.WrongThread); Failure(p.Appearance.OverrideColor(new(new(Key: x.Alias), f.RunId, Red)), ErrorCode.WrongThread); }).GetAwaiter().GetResult();
        Equal(0, x.Writes);
    }),
    ("Accessor metadata requires declared receivers and exact Color/void signatures", () => {
        var calls = new List<string>();
        var bound = NativeAppearanceColorAccessors.Bind((n, p, w) => { calls.Add(n + "(" + string.Join(",", p) + ")/" + w); return new(w ? 2 : 1); },
            (_, _, _) => true, m => m == new IntPtr(1) ? "UnityEngine.Color" : "System.Void", "get_startColor", "set_startColor");
        Equal(new IntPtr(1), bound.Read); Equal(new IntPtr(2), bound.Write);
        Check(calls.SequenceEqual(new[] { "get_startColor()/False", "set_startColor(UnityEngine.Color)/True" }));
        foreach (var phase in new[] { false, true })
            Throws<NotSupportedException>(() => NativeAppearanceColorAccessors.Bind((_, _, w) => new(w ? 2 : 1), (_, _, w) => w != phase,
                m => m == new IntPtr(1) ? "UnityEngine.Color" : "System.Void", "get_color", "set_color"));
        Throws<NotSupportedException>(() => NativeAppearanceColorAccessors.Bind((_, _, w) => new(w ? 2 : 1), (_, _, _) => true,
            m => m == new IntPtr(1) ? "UnityEngine.Color32" : "System.Void", "get_color", "set_color"));
    })
}.Concat(NativeAppearanceChecks.Cases).Concat(NativeRingColorChecks.Cases).Concat(NativeGraphChecks.Cases)
    .Where(test => args.Length == 0 || args.Any(filter => test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToArray();
var failed = 0;
foreach (var test in tests) try { test.Run(); Console.WriteLine("PASS " + test.Name); }
catch (Exception error) { ++failed; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
Console.WriteLine($"Game appearance checks: {tests.Length - failed}/{tests.Length} passed.");
return failed == 0 ? 0 : 1;

static class Checks
{
    internal static ResourceColor Red => new(1, 0, 0); internal static ResourceColor Green => new(0, 1, 0); internal static ResourceColor Blue => new(0, 0, 1);
    internal static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
    internal static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}."); }
    internal static T Need<T>(Result<T> value) => value.Success ? value.Value! : throw new InvalidOperationException(value.Error?.ToString());
    internal static T NeedOp<T>(OperationResult<T> value) => value.Success ? value.Value! : throw new InvalidOperationException(value.Error?.ToString());
    internal static void Failure<T>(Result<T> value, ErrorCode code, bool retryable = false)
    { Check(!value.Success); Equal(code, value.Error!.Code); Equal(GameAppearanceCapabilityIds.Appearance, value.Error.CapabilityId); Equal(retryable, value.Error.Retryable); }
    internal static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
sealed class Logger : ILogger
{
    internal List<string> Messages = new();
    public void Log(LogLevel level, string message, Exception? exception = null) => Messages.Add(message);
}
sealed class Cleanup : IDisposable
{
    private Action? _action; internal Cleanup(Action action) => _action = action;
    public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
}
sealed class Source
{
    internal readonly string Id, Role; internal string Physical, Content = "chart", Scene = "Play";
    internal bool Alive = true, Active = true, Template, OwnershipCurrent = true, FailRead;
    internal ResourceColor Original = new(.2f, .3f, .4f), Color;
    internal int Retained, Released, Writes; internal int Outstanding => Retained - Released;
    internal Action<ResourceColor>? OnWrite; internal List<string> ExtraAliases = new();
    internal string Alias => "game.appearance/test/" + Id + "/" + Role;
    internal string SecondAlias => "game.test.alias/" + Id + "/" + Role;
    internal Source(string id, string role) { Id = id; Role = role; Physical = id + "/" + role; Color = Original; }
    internal GameAppearanceCandidate Candidate(GameAppearanceRun run) => new(Id + "/" + Role + "/" + run.RunId + "/" + run.SceneGeneration,
        Alias, Physical, run, new(Scene: Scene, Role: Role, ContentId: Content, ResourceId: Role, Active: Active, Template: Template),
        Backend.Property(Role), ExtraAliases.Append(Alias).Append(SecondAlias).ToArray(),
        () => { if (FailRead || !Alive || !OwnershipCurrent) throw new InvalidOperationException("Controlled source getter expired."); return Color; },
        v => { if (!Alive || !OwnershipCurrent) throw new InvalidOperationException("Controlled source owner expired."); ++Writes; if (OnWrite is null) Color = v; else OnWrite(v); },
        () => Alive && OwnershipCurrent, () => Alive && Active && OwnershipCurrent,
        () => { ++Retained; return new Cleanup(() => ++Released); });
}
sealed class Backend : IGameAppearanceBackend
{
    internal List<Source> Sources = new(); internal int? FailAfter;
    internal Dictionary<string, string> Unsupported = new(StringComparer.Ordinal);
    internal static GameAppearanceProperty Property(string role) => new(role, "color", "controlled source " + role, "Controlled host only; no real visible acceptance.", ExternalWritePolicy.AdoptAndReapply);
    public IReadOnlyList<GameAppearanceProperty> Properties => new[] { Property(GameAppearanceRoles.TrackStartColor), Property(GameAppearanceRoles.TrackEndColor),
        Property(GameAppearanceRoles.TrackRingColor), Property(GameAppearanceRoles.NoteRendererColor) }
        .Where(property => !Unsupported.ContainsKey(property.Role)).ToArray();
    public string? UnsupportedReason(string role) => Unsupported.GetValueOrDefault(role);
    public IEnumerable<GameAppearanceCandidate> Discover(GameAppearanceRun run, int maximumScanned)
    {
        var count = 0;
        foreach (var source in Sources)
        { yield return source.Candidate(run); if (++count == FailAfter) throw new InvalidOperationException("Controlled complete-source enumeration failed."); }
    }
}
sealed class ProbePlugin : IRizPlugin
{
    private readonly string _id; internal IGameAppearanceService? Appearance; internal ITargetService? Targets; internal IMutationService? Mutations;
    internal ProbePlugin(string id) => _id = id;
    public PluginManifest Manifest => new(_id, new(1, 0), new(1, 0), RequiredCapabilities: new[] { GameAppearanceCapabilityIds.Appearance, TargetCapabilityIds.Targets, TargetCapabilityIds.Mutations });
    public void Enable(IPluginContext context)
    { Appearance = context.GetService<IGameAppearanceService>(GameAppearanceCapabilityIds.Appearance).Service!; Targets = context.GetService<ITargetService>(TargetCapabilityIds.Targets).Service!; Mutations = context.GetService<IMutationService>(TargetCapabilityIds.Mutations).Service!; }
    public void Disable() { }
}
sealed class Fixture : IDisposable
{
    internal MainThreadDispatcher Main = new(); internal Logger Log = new(); internal Backend Backend = new(); internal List<Source> Sources => Backend.Sources;
    internal GameLifecycleSnapshot? State = new("session", 1, 10, "Play", "run", "chart", RunLifecyclePhase.Playing, false, true, true, true, Array.Empty<OperationError>());
    internal string RunId => State!.RunId!; internal SemanticTargetRegistry Targets; internal MutationCoordinator Mutations;
    internal GameAppearanceService Service; internal CapabilityRegistry Capabilities; internal PluginHost Host;
    internal Fixture()
    {
        Targets = new(Main, Log); Mutations = new(Targets, Main, Log); Service = new(Main, Log, Targets, Mutations, Backend, () => State);
        Capabilities = new(new("test", "test", "test", "test", true));
        Capabilities.RegisterScoped<IGameAppearanceService>(GameAppearanceCapabilityIds.Appearance, c => Service.ForPlugin(c));
        Capabilities.RegisterScoped<ITargetService>(TargetCapabilityIds.Targets, c => Targets.ForPlugin(c));
        Capabilities.RegisterScoped<IMutationService>(TargetCapabilityIds.Mutations, c => Mutations.ForPlugin(c));
        Host = new(Capabilities, Main, new EventHub(Log), Log);
    }
    internal Source Add(string id = "segment", string role = GameAppearanceRoles.TrackStartColor) { var source = new Source(id, role); Sources.Add(source); return source; }
    internal ProbePlugin Plugin(string id = "appearance.probe") { var p = new ProbePlugin(id); Check(Host.Register(p).Success); Check(Host.Enable(id).Success); return p; }
    internal TargetHandle<ResourceColor> Resolve(string alias) => Targets.Resolve<ResourceColor>(new(Key: alias)).Target ?? throw new InvalidOperationException("No unique target: " + alias);
    public void Dispose() { Host.Dispose(); Service.Dispose(); Mutations.Dispose(); Targets.Dispose(); }
}
