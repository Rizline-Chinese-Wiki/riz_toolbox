using RizToolbox.Adapter.GameCamera;
using RizToolbox.Core;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Sdk;
using static CameraChecks;

var tests = new (string Name, Action Run)[] {
    ("An empty or failed camera scan waits for its interval and a new run can discover immediately", () => {
        double now = 0;
        using var f = new CameraFixture(monotonicSeconds: () => now);
        Equal(0, f.Service.Refresh());
        for (var i = 1; i <= 100; i++) { now = i / 500d; Equal(0, f.Service.Refresh()); }
        Equal(1, f.Backend.Discoveries);

        var source = f.Add(); f.Backend.FailAfter = 1; f.Log.FailWrites = true;
        now = GameCameraService.DiscoveryIntervalSeconds;
        Equal(0, f.Service.Refresh()); Equal(2, f.Backend.Discoveries); Equal(0, source.Outstanding);
        f.Backend.FailAfter = null;
        now = .49; Equal(0, f.Service.Refresh()); Equal(2, f.Backend.Discoveries);
        now = .50; Equal(1, f.Service.Refresh()); Equal(3, f.Backend.Discoveries);
        var previous = f.Resolve(source.Alias);
        now = .51; f.State = f.State! with { RunId = "next-run", SceneGeneration = 11 };
        Equal(1, f.Service.Refresh()); Equal(4, f.Backend.Discoveries);
        Check(!f.Targets.Acquire(previous).Success); Equal(1, source.Outstanding);
        f.Log.FailWrites = false;
    }),
    ("Camera restoration invalidates its view first and survives a failed invalidation listener and log sink", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh();
        var plugin = f.Plugin(); var camera = plugin.Camera!;
        using var lease = Need(camera.OverrideScalar(new(new(Key: source.Alias), "run", 3)));
        var peerInvalidations = 0;
        f.Targets.Invalidated += _ => throw new IOException("controlled invalidation failure");
        f.Targets.Invalidated += _ => peerInvalidations++;
        var blockedView = false;
        source.OnWrite = value =>
        {
            blockedView = camera.GetSupportedProperties().Error?.Code == ErrorCode.ScopeExpired;
            source.X = value;
        };
        f.Log.FailWrites = true;
        f.Service.Dispose();
        Check(blockedView); Equal(source.Original, source.X);
        Equal(1, peerInvalidations); Equal(0, source.Outstanding); Check(!lease.IsActive);
        f.Log.FailWrites = false;
    }),
    ("Scalar metadata reports local units, gameplay impact and no invented numeric bounds", () => {
        using var f = new CameraFixture(); f.Service.Refresh(true); var p = f.Plugin();
        var property = Need(p.Camera!.GetSupportedProperties()).Single();
        Equal("local-position-x", property.Property); Equal("unity-local-unit", property.Unit);
        Check(property.Minimum is null && property.Maximum is null && property.Experimental);
        Equal(ExternalWritePolicy.AdoptAndReapply, property.ExternalWrites); Check(property.Evidence.Contains("gameplay"));
        Equal(0, Need(p.Camera.GetTargets()).Count);
        Failure(p.Camera.OverrideScalar(new(new(Role: property.Role), "run", 1)), ErrorCode.TargetMissing, true);
    }),
    ("Current paused runs are admitted and loading/unobserved runs publish no target", () => {
        using var f = new CameraFixture(); var source = f.Add(); var current = f.State;
        f.State = null; Equal(0, f.Service.Refresh(true));
        f.State = current! with { Phase = RunLifecyclePhase.Loading }; Equal(0, f.Service.Refresh(true));
        f.State = current! with { Phase = RunLifecyclePhase.Paused, Paused = true }; Equal(1, f.Service.Refresh(true));
        var p = f.Plugin(); using var lease = Need(p.Camera!.OverrideScalar(new(new(Key: source.Alias), "run", 2)));
        Equal(2f, source.X); Equal(1, source.Outstanding);
    }),
    ("Camera and generic float leases share a channel across aliases and plugin scopes", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh(true);
        var a = f.Plugin("camera.low"); var b = f.Plugin("camera.high"); var target = f.Resolve(source.Alias);
        using var low = Need(a.Camera!.OverrideScalar(new(new(Key: source.Alias), "run", 2, 10)));
        var high = NeedOp(b.Mutations!.Override(new MutationKey<float>(f.Resolve(source.SecondAlias), "local-position-x"), 3, 20));
        Equal(3f, source.X); Equal(target, f.Resolve(source.SecondAlias));
        Equal(2, NeedOp(a.Mutations!.Describe(new MutationKey<float>(target, "local-position-x"))).Holders.Count);
        source.X = 7; source.Y = 17; source.Z = 19; f.Service.Refresh(); Equal(3f, source.X);
        f.Host.Disable("camera.high"); Equal(2f, source.X); Equal(17f, source.Y); Equal(19f, source.Z);
        f.Host.Disable("camera.low"); Equal(7f, source.X); Equal(17f, source.Y); Equal(19f, source.Z);
        var before = source.Writes;
        // The frozen dependency snapshot predates the host's new Result scope proxy.
        try { Check(!high.SetValue(4).Success); } catch (ObjectDisposedException) { }
        Equal(before, source.Writes);
    }),
    ("Scope restoration changes only X while preserving fresh external Y and Z", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        Need(p.Camera!.OverrideScalar(new(new(Key: source.Alias), "run", 20)));
        source.Y = 23; source.Z = 29; f.Host.Disable(p.Manifest.Id);
        Equal(source.Original, source.X); Equal(23f, source.Y); Equal(29f, source.Z); Equal(2, source.Writes);
    }),
    ("Fresh external X at scope cleanup remains untouched without a refresh tick", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        Need(p.Camera!.OverrideScalar(new(new(Key: source.Alias), "run", 20))); source.X = 37;
        f.Host.Disable(p.Manifest.Id); Equal(37f, source.X);
    }),
    ("Old run and scene intents are rejected before any write", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        Failure(p.Camera!.OverrideScalar(new(new(Key: source.Alias), "old-run", 1)), ErrorCode.TargetExpired);
        Failure(p.Camera.OverrideScalar(new(new(Key: source.Alias), "run", 1, SceneGeneration: 999)), ErrorCode.TargetExpired);
        Equal(0, source.Writes);
    }),
    ("A generic lease expires between lifecycle sampling and service refresh", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        var handle = f.Resolve(source.Alias); var lease = Need(p.Camera!.OverrideScalar(new(new(Key: source.Alias), "run", 2)));
        f.State = f.State! with { RunId = "replacement-run" }; var writes = source.Writes;
        Check(!lease.SetValue(3).Success); Equal(writes, source.Writes); Check(!f.Targets.Acquire(handle).Success);
        f.Service.Refresh(true); Check(f.Resolve(source.Alias).Generation > handle.Generation);
    }),
    ("Complete discovery rejects ambiguity and partial failures without leaking roots", () => {
        using var f = new CameraFixture(); var first = f.Add("first"); f.Add("second"); f.Backend.FailAfter = 1;
        Equal(0, f.Service.Refresh(true)); Equal(0, f.Sources.Sum(x => x.Outstanding));
        f.Backend.FailAfter = null; Equal(2, f.Service.Refresh(true)); var p = f.Plugin();
        Failure(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 1)), ErrorCode.AmbiguousTarget);
        using var lease = Need(p.Camera.OverrideScalar(new(new(Key: first.Alias), "run", 1))); Equal(1f, first.X);
    }),
    ("A foreign shared float provider cannot be selected through the camera service", () => {
        using var f = new CameraFixture(); f.Service.Refresh(true); var p = f.Plugin(); var actual = 5f;
        var foreign = NeedOp(f.Targets.Register("game.foreign/float", () => actual));
        using var channel = NeedOp(f.Mutations.Register(foreign, "local-position-x", x => actual = x));
        Failure(p.Camera!.OverrideScalar(new(new(Key: foreign.Key), "run", 3)), ErrorCode.TargetMissing, true); Equal(5f, actual);
    }),
    ("Scalar validation rejects nonfinite values and accepts finite values without fabricated bounds", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Failure(p.Camera!.OverrideScalar(new(new(Key: source.Alias), "run", value)), ErrorCode.InvalidArgument);
        Equal(0, source.Writes);
        using var lease = Need(p.Camera!.OverrideScalar(new(new(Key: source.Alias), "run", -100000)));
        Equal(-100000f, source.X); Check(lease.SetValue(100000).Success); Equal(100000f, source.X);
    }),
    ("Rendered readback mismatch cannot report a successful scalar mutation", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh(true); var p = f.Plugin(); source.OnWrite = _ => source.X = 41;
        Failure(p.Camera!.OverrideScalar(new(new(Key: source.Alias), "run", 2)), ErrorCode.MutationWriteFailed); Equal(41f, source.X);
    }),
    ("Mutate-then-throw rollback preserves fresh Y/Z and the previous scalar winner", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        using var low = Need(p.Camera!.OverrideScalar(new(new(Key: source.Alias), "run", 2, 10)));
        source.OnWrite = value => { source.X = value; source.Y = 31; source.Z = 43; if (value == 3) throw new InvalidOperationException("Controlled native failure"); };
        Failure(p.Camera.OverrideScalar(new(new(Key: source.Alias), "run", 3, 20)), ErrorCode.MutationWriteFailed);
        Equal(2f, source.X); Equal(31f, source.Y); Equal(43f, source.Z); Check(low.IsActive);
    }),
    ("Dead sources expire without restoration or retaining old object roots", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh(true); var p = f.Plugin();
        var lease = Need(p.Camera!.OverrideScalar(new(new(Key: source.Alias), "run", 2))); var before = source.Writes;
        source.Alive = false; Equal(0, f.Service.Refresh(true)); Equal(before, source.Writes); Equal(0, source.Outstanding); Check(!lease.IsActive);
    }),
    ("Registration getter failures and duplicate physical ownership publish no partial target", () => {
        using (var f = new CameraFixture()) { var source = f.Add(); source.FailRead = true; Equal(0, f.Service.Refresh(true)); Equal(0, source.Outstanding); }
        using (var f = new CameraFixture()) { var a = f.Add("first"); var b = f.Add("second"); b.Physical = a.Physical; Equal(0, f.Service.Refresh(true)); Equal(0, f.Sources.Sum(x => x.Outstanding)); }
    }),
    ("Disposing the camera service restores X and leaves unrelated shared targets available", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh(true); var p = f.Plugin(); var actual = 5f;
        var foreign = NeedOp(f.Targets.Register("game.foreign/float", () => actual)); using var channel = NeedOp(f.Mutations.Register(foreign, "value", x => actual = x));
        Need(p.Camera!.OverrideScalar(new(new(Key: source.Alias), "run", 2))); f.Service.Dispose();
        Equal(source.Original, source.X); Equal(0, source.Outstanding); Check(f.Targets.Acquire(foreign).Success);
        using var lease = NeedOp(p.Mutations!.Override(new MutationKey<float>(foreign, "value"), 9)); Equal(9f, actual);
    }),
    ("Expired cached service views and worker threads return structured errors", () => {
        using var f = new CameraFixture(); var source = f.Add(); f.Service.Refresh(true); var p = f.Plugin(); var view = p.Camera!;
        Task.Run(() => Failure(view.GetTargets(), ErrorCode.WrongThread)).GetAwaiter().GetResult();
        f.Host.Disable(p.Manifest.Id); Failure(view.GetTargets(), ErrorCode.ScopeExpired);
        Failure(view.GetSupportedProperties(), ErrorCode.ScopeExpired); Failure(view.OverrideScalar(new(new(Key: source.Alias), "run", 2)), ErrorCode.ScopeExpired);
        Equal(0, source.Writes);
    })
}.Concat(ScaleCameraChecks.Cases).Concat(NativeCameraChecks.Cases).ToArray();
var selected = tests.Where(test => args.Length == 0 || args.Any(filter => test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToArray();
var failed = 0;
foreach (var test in selected) try { test.Run(); Console.WriteLine("PASS " + test.Name); }
catch (Exception error) { ++failed; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
Console.WriteLine($"Game camera checks: {selected.Length - failed}/{selected.Length} passed.");
return failed == 0 ? 0 : 1;

static class CameraChecks
{
    internal static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
    internal static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}."); }
    internal static T Need<T>(Result<T> value) => value.Success ? value.Value! : throw new InvalidOperationException(value.Error?.ToString());
    internal static T NeedOp<T>(OperationResult<T> value) => value.Success ? value.Value! : throw new InvalidOperationException(value.Error?.ToString());
    internal static void Failure<T>(Result<T> value, ErrorCode code, bool retryable = false)
    { Check(!value.Success); Equal(code, value.Error!.Code); Equal(GameCameraCapabilityIds.Camera, value.Error.CapabilityId); Equal(retryable, value.Error.Retryable); }
    internal static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
sealed class CameraLogger : ILogger
{
    internal List<string> Messages = new();
    internal bool FailWrites;
    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (FailWrites) throw new IOException("controlled log failure");
        Messages.Add(message);
    }
}
sealed class CameraCleanup : IDisposable
{
    private Action? _action; internal CameraCleanup(Action action) => _action = action;
    public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
}
sealed class CameraSource
{
    internal readonly string Id; internal string Physical;
    internal GameCameraScalarProperty Property = NativeGameCameraBackend.LocalX;
    internal bool Alive = true, FailRead;
    internal float Original = .5f, X = .5f, Y = -2.4f, Z;
    internal int Retained, Released, Writes; internal int Outstanding => Retained - Released;
    internal Action<float>? OnWrite;
    internal string Alias => "game.camera/test/" + Id + "/" + Property.Property;
    internal string SecondAlias => "game.camera/test-alias/" + Id;
    internal CameraSource(string id) { Id = id; Physical = id; }
    internal GameCameraCandidate Candidate(GameCameraRun run) => new(Id + "/" + run.RunId + "/" + run.SceneGeneration, Alias, Physical, run,
        new(Scene: "Play", Role: Property.Role, ContentId: "chart", ResourceId: Property.Property),
        Property, new[] { Alias, SecondAlias },
        () => { if (FailRead || !Alive) throw new InvalidOperationException("Controlled camera getter expired."); return X; },
        value => { if (!Alive) throw new InvalidOperationException("Dead camera source."); ++Writes; if (OnWrite is null) X = value; else OnWrite(value); },
        () => Alive, () => Alive, () => { ++Retained; return new CameraCleanup(() => ++Released); });
}
sealed class CameraBackend : IGameCameraBackend
{
    internal List<CameraSource> Sources = new(); internal int? FailAfter;
    internal int Discoveries;
    internal IReadOnlyList<GameCameraScalarProperty> DeclaredProperties = new[] { NativeGameCameraBackend.LocalX };
    public IReadOnlyList<GameCameraScalarProperty> Properties => DeclaredProperties;
    public IEnumerable<GameCameraCandidate> Discover(GameCameraRun run, int maximumScanned)
    { Discoveries++; var count = 0; foreach (var source in Sources) { yield return source.Candidate(run); if (++count == FailAfter) throw new InvalidOperationException("Controlled full-source failure."); } }
}
sealed class CameraPlugin : IRizPlugin
{
    private readonly string _id; internal IGameCameraService? Camera; internal IMutationService? Mutations;
    internal CameraPlugin(string id) => _id = id;
    public PluginManifest Manifest => new(_id, new(1, 0), new(1, 0), RequiredCapabilities: new[] { GameCameraCapabilityIds.Camera, TargetCapabilityIds.Targets, TargetCapabilityIds.Mutations });
    public void Enable(IPluginContext context)
    { Camera = context.GetService<IGameCameraService>(GameCameraCapabilityIds.Camera).Service!; Mutations = context.GetService<IMutationService>(TargetCapabilityIds.Mutations).Service!; }
    public void Disable() { }
}
sealed class CameraFixture : IDisposable
{
    internal MainThreadDispatcher Main = new(); internal CameraLogger Log = new(); internal CameraBackend Backend = new(); internal List<CameraSource> Sources => Backend.Sources;
    internal GameLifecycleSnapshot? State = new("session", 1, 10, "Play", "run", "chart", RunLifecyclePhase.Playing, false, true, true, true, Array.Empty<OperationError>());
    internal SemanticTargetRegistry Targets; internal MutationCoordinator Mutations; internal GameCameraService Service; internal CapabilityRegistry Capabilities; internal PluginHost Host;
    internal CameraFixture(IGameCameraBackend? backend = null, Func<double>? monotonicSeconds = null)
    {
        if (backend is CameraBackend supplied) Backend = supplied;
        Targets = new(Main, Log); Mutations = new(Targets, Main, Log); Service = new(Main, Log, Targets, Mutations, backend ?? Backend, () => State, monotonicSeconds);
        Capabilities = new(new("test", "test", "test", "test", true));
        Capabilities.RegisterScoped<IGameCameraService>(GameCameraCapabilityIds.Camera, c => Service.ForPlugin(c));
        Capabilities.RegisterScoped<ITargetService>(TargetCapabilityIds.Targets, c => Targets.ForPlugin(c));
        Capabilities.RegisterScoped<IMutationService>(TargetCapabilityIds.Mutations, c => Mutations.ForPlugin(c));
        Host = new(Capabilities, Main, new EventHub(Log), Log);
    }
    internal CameraSource Add(string id = "rig") { var source = new CameraSource(id); Sources.Add(source); return source; }
    internal CameraPlugin Plugin(string id = "camera.probe") { var p = new CameraPlugin(id); Check(Host.Register(p).Success); Check(Host.Enable(id).Success); return p; }
    internal TargetHandle<float> Resolve(string alias) => Targets.Resolve<float>(new(Key: alias)).Target ?? throw new InvalidOperationException("No unique camera target: " + alias);
    public void Dispose() { Host.Dispose(); Service.Dispose(); Mutations.Dispose(); Targets.Dispose(); }
}
