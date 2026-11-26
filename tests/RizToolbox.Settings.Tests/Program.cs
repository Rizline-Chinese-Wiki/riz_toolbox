using System.Globalization;
using RizToolbox.Adapter.Settings;
using RizToolbox.Core;
using RizToolbox.Sdk;

if (args.Length == 1 && args[0] == "--measure-data-settings")
{
    DataSettingsProjectionRegression.Measure();
    return 0;
}

var checks = new (string Name, Action Run)[]
{
    ("getter/setter policy rejects wrong staticness, scalar types, returns and ref-like shape", Signatures),
    ("read operation remains ready when the actual settings UI is unavailable", ReadWithoutUi),
    ("raw game slider ranges are used without assuming normalized volume", ActualRanges),
    ("periodic refresh checks slider ranges once when idle and before reapplying a held setting", PeriodicRangeReads),
    ("read and override modules declare and publish separate operation capabilities", SeparateModules),
    ("same property double-package priority override restores next layer and original", PriorityLayers),
    ("same-priority setting layers use stable last-registration order", EqualPriority),
    ("external game user change is adopted and reapplied then restored as latest baseline", UserBaseline),
    ("external game write immediately before release is never replaced by stale baseline", ExternalBeforeRelease),
    ("invalid value leaves game and previously active layer unchanged", RejectedInputs),
    ("nonfinite or contradictory native UI ranges disable writes while getters stay readable", InvalidRanges),
    ("missing getter/setter remains explicitly unsupported without blocking verified neighbor", UnsupportedBindings),
    ("setter unknown read-back rejects layer and preserves unknown value without stale restoration", ReadBackMismatch),
    ("write-then-throw rolls back only while read-back proves ownership and retains prior layer", WriteThenThrow),
    ("live setting leases are automatically released on plugin stop and stale views reject", AutomaticOwnership),
    ("module release restores valid owned setting then expires old leases", ModuleRelease),
    ("scope stop during slider range capture refuses the next setting write", StopDuringRangeProbe),
    ("nested setting writes cannot borrow an unfinished slider probe", ReentrantRangeProbe),
    ("module stop inside a setting setter defers backend cleanup until readback and restoration", ReleaseDuringSetter),
    ("lease stop during range capture or value application preserves the original baseline", StopDuringLeaseUpdate),
    ("scope stop during periodic reapply restores the latest external baseline", StopDuringPeriodicReapply),
    ("backend not initialized refuses reads/writes without claiming readiness", BackendNotReady),
    ("UI disappearance rejects new writes but still permits existing owned cleanup", UiDisappearance),
    ("actual read snapshot source converts verified game floats for revisioned data", DataSnapshots),
    ("wrong-thread setting access does not touch native/backend reads or writes", ThreadAccess),
    ("unique native UI candidate remains rooted until range read and releases all roots", RootSelection),
    ("ambiguous native UI candidates release both roots and the enumeration", RootAmbiguity),
    ("bounded UI scan rejects incomplete selection and releases selected root", RootLimit),
    ("native range and liveness exceptions release roots and enumeration", RootFailures),
    ("missing native UI candidates give NotReady and leave no roots", RootMissing),
    ("nonfinite or throwing game getter returns explicit failure without writes", GetterFailures)
}.Concat(SettingsChangeRegression.Cases()).Concat(NativeVolumeUnitsRegression.Cases()).Concat(DataSettingsProjectionRegression.Cases()).ToArray();
int failures = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS " + check.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + check.Name + ": " + ex); }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} game settings checks passed.");
return failures == 0 ? 0 : 1;

static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
static void Equal(float expected, float actual) => Check(Math.Abs(expected - actual) < .00001, $"Expected {expected}, got {actual}.");
static IGameSettingLease Lease(SettingViews views, string id, float value, int priority = 0)
{
    var result = views.Write.Override(new(id, value, priority));
    Check(result.Success && result.Value is not null, result.Error?.Message ?? "Setting override failed."); return result.Value!;
}

static void Signatures()
{
    Check(GameSettingSignatures.Getter(new(true, "System.Single", Array.Empty<string>())) &&
        GameSettingSignatures.Setter(new(true, "System.Void", new[] { "System.Single" })), "Verified float property signatures refused.");
    foreach (var shape in new[] { new SettingMethodShape(false, "System.Single", Array.Empty<string>()), new(true, "System.Double", Array.Empty<string>()), new(true, "System.Single", new[] { "System.Single" }) })
        Check(!GameSettingSignatures.Getter(shape), "Invalid getter signature accepted.");
    foreach (var shape in new[] { new SettingMethodShape(false, "System.Void", new[] { "System.Single" }), new(true, "System.Single", new[] { "System.Single" }), new(true, "System.Void", new[] { "System.Double" }), new(true, "System.Void", new[] { "System.Single&" }) })
        Check(!GameSettingSignatures.Setter(shape), "Invalid setter signature accepted.");
}

static void ReadWithoutUi()
{
    using var f = new Fixture(); f.Backend.UiReady = false; var views = f.Start("reader");
    var snapshot = views.Read.GetSettings(); Check(snapshot.Success && snapshot.Value!.Count == 2 && snapshot.Value.All(x => !x.Descriptor.OverrideAvailable), "Read blocked by settings UI.");
    Equal(.8f, views.Read.Read("music").Value!.Value);
    Check(views.Write.Override(new("music", .2f)).Error?.Code == ErrorCode.NotReady, "Missing UI did not give an explicit NotReady write result.");
}

static void ActualRanges()
{
    using var f = new Fixture(); f.Backend.Values["music"] = 80; f.Backend.Ranges["music"] = new(0, 100, true); f.Service.Refresh();
    var views = f.Start("volume"); var lease = Lease(views, "music", 25);
    Equal(25, f.Backend.Values["music"]); var descriptor = views.Read.Read("music").Value!.Descriptor;
    Check(descriptor.Maximum == 100 && descriptor.WholeNumbers, "Actual game range was replaced by assumed normalized values.");
    Check(lease.SetValue(25.5f).Error?.Code == ErrorCode.InvalidArgument && lease.SetValue(101).Error?.Code == ErrorCode.InvalidArgument, "Actual integer/range constraints ignored.");
    lease.Dispose(); Equal(80, f.Backend.Values["music"]);
}

static void SeparateModules()
{
    using var main = new MainThreadDispatcher(); var backend = new FakeBackend { UiReady = false }; var logger = new TestLogger();
    var read = new SettingsModule(() => backend, main, logger); var write = new SettingsOverrideModule(read);
    try
    {
        Check(read.Manifest.Capabilities.SequenceEqual(new[] { GameSettingsCapabilityIds.Read, GameSettingsCapabilityIds.Changes }) && write.Manifest.Capabilities.SequenceEqual(new[] { GameSettingsCapabilityIds.Override }) &&
            write.Manifest.RequiredCapabilities.Contains(GameSettingsCapabilityIds.Read), "Operation capabilities or dependencies not split.");
        Check(read.Probe().Ready && !write.Probe().Ready && !write.Probe().Permanent, "Read readiness depends on UI or temporary UI absence advertised unsupported.");
        backend.UiReady = true; Check(write.Probe().Ready, "Late settings UI did not enable override probe.");
        backend.RangeUnsupported = true; Check(!write.Probe().Ready && write.Probe().Permanent && read.Probe().Ready, "Unsupported UI affected read operation.");
    }
    finally { write.Release(); read.Release(); }
}

static void PriorityLayers()
{
    using var f = new Fixture(); var low = f.Start("low"); var high = f.Start("high"); var a = Lease(low, "music", .3f, 10); var b = Lease(high, "music", .6f, 20);
    Equal(.6f, f.Backend.Values["music"]);
    var owners = high.Write.Describe("music").Value!; Check(owners.Holders.Count == 2 && owners.Holders.Single(x => x.Winner).Owner == "high", "Coordinator owners/winner incorrect.");
    b.Dispose(); Equal(.3f, f.Backend.Values["music"]); a.Dispose(); Equal(.8f, f.Backend.Values["music"]);
}

static void EqualPriority()
{
    using var f = new Fixture(); var a = f.Start("a"); var b = f.Start("b"); var first = Lease(a, "music", .2f, 10); var second = Lease(b, "music", .4f, 10);
    Equal(.4f, f.Backend.Values["music"]); first.SetValue(.25f); Equal(.4f, f.Backend.Values["music"]); second.Dispose(); Equal(.25f, f.Backend.Values["music"]);
}

static void PeriodicRangeReads()
{
    using var f = new Fixture();
    var owner = f.Start("mod");
    var before = f.Backend.RangeProbes;
    Check(f.Service.Refresh().Success, "Idle settings refresh failed.");
    Equal(1, f.Backend.RangeProbes - before);

    using var lease = Lease(owner, "music", .8f);
    f.Backend.Values["music"] = .4f;
    f.Backend.Ranges["music"] = new(0, .5f);
    var writes = f.Backend.Writes;
    Check(f.Service.Refresh().Success, "Refresh failed to reconcile the narrowed slider range.");
    Equal(.4f, f.Backend.Values["music"]);
    Equal(writes, f.Backend.Writes);
    Check(!lease.IsActive, "The invalid override remained active after its slider range narrowed.");
}

static void UserBaseline()
{
    using var f = new Fixture(); var a = f.Start("mod"); var lease = Lease(a, "music", .2f);
    f.Backend.Values["music"] = .65f; Check(f.Service.Refresh().Success, "External write refresh failed."); Equal(.2f, f.Backend.Values["music"]);
    Equal(.65f, a.Write.Describe("music").Value!.Baseline); lease.Dispose(); Equal(.65f, f.Backend.Values["music"]);
}

static void ExternalBeforeRelease()
{
    using var f = new Fixture(); var a = f.Start("mod"); var lease = Lease(a, "music", .2f);
    f.Backend.Values["music"] = .57f; lease.Dispose(); Equal(.57f, f.Backend.Values["music"]);
}

static void RejectedInputs()
{
    using var f = new Fixture(); var a = f.Start("mod"); var lease = Lease(a, "music", .3f);
    foreach (float value in new[] { float.NaN, float.PositiveInfinity, -1f, 2f })
    {
        Check(a.Write.Override(new("music", value)).Error?.Code == ErrorCode.InvalidArgument && lease.SetValue(value).Error?.Code == ErrorCode.InvalidArgument, "Invalid setting input accepted.");
        Equal(.3f, f.Backend.Values["music"]);
    }
    Check(a.Write.Override(new("unknown", .2f)).Error?.Code == ErrorCode.OperationUnsupported, "Unknown setting accepted.");
}

static void InvalidRanges()
{
    using var f = new Fixture(); var views = f.Start("mod");
    foreach (var range in new[] { new GameSettingRange(float.NaN, 1), new(2, 1), new(0, float.PositiveInfinity) })
    {
        f.Backend.Ranges["music"] = range;
        Check(!f.Service.RefreshRanges().Success && !f.Service.OverrideReady && views.Read.GetSettings().Success &&
            views.Write.Override(new("music", .2f)).Error?.Code == ErrorCode.UnsupportedSignature, "Invalid actual UI range allowed writes or prevented reads.");
    }
}

static void UnsupportedBindings()
{
    using var f = new Fixture(); var views = f.Start("mod");
    f.Backend.List.Add(new("unsupported", "fixture unsupported getter", null, null, "Wrong getter signature", "Wrong setter signature"));
    f.Backend.List.Add(new("readonly", "fixture read-only", () => .7f, null, WriteUnsupportedReason: "No verified setter"));
    Check(views.Read.Describe().Value!.Single(x => x.Id == "unsupported").ReadAvailable == false &&
        views.Read.Read("unsupported").Error?.Code == ErrorCode.OperationUnsupported, "Unsupported getter was not explicit.");
    Check(views.Read.GetSettings().Success && views.Write.Override(new("readonly", .5f)).Error?.Code == ErrorCode.OperationUnsupported, "One unsupported property poisoned verified neighbor.");
}

static void ReadBackMismatch()
{
    using var f = new Fixture(); var views = f.Start("mod"); f.Backend.ClampWrites = true;
    var failed = views.Write.Override(new("music", .2f));
    Check(failed.Error is { Code: ErrorCode.MutationWriteFailed, CapabilityId: TargetCapabilityIds.Mutations, Retryable: false }
        && failed.Error.TargetId is { } key && key.StartsWith("game.settings/music/binding/", StringComparison.Ordinal),
        "Unknown setter readback lost the coordinator's structured write failure.");
    Equal(.5f, f.Backend.Values["music"]);
    var state = views.Write.Describe("music").Value!;
    Check(!state.Valid && state.Holders.Count == 0 && !views.Read.Read("music").Value!.Descriptor.OverrideAvailable, "Failed setting lease leaked or descriptor claims an expired binding is writable.");
    int writes = f.Backend.Writes;
    Check(views.Write.Override(new("music", .3f)).Error?.Code == ErrorCode.TargetExpired && f.Backend.Writes == writes, "Expired binding allowed another write.");
    f.Service.Dispose(); Equal(.5f, f.Backend.Values["music"]);
}

static void WriteThenThrow()
{
    using var f = new Fixture(); var views = f.Start("mod"); var prior = Lease(views, "music", .3f, 10);
    f.Backend.ThrowOnceAfterWrite = true;
    var failed = views.Write.Override(new("music", .2f, 20));
    Check(failed.Error is { Code: ErrorCode.MutationWriteFailed, CapabilityId: TargetCapabilityIds.Mutations }
        && failed.Error.TargetId is { } key && key.StartsWith("game.settings/music/binding/", StringComparison.Ordinal),
        "Recoverable setter exception lost the coordinator's failure or target context.");
    Equal(.3f, f.Backend.Values["music"]);
    Check(prior.IsActive && views.Write.Describe("music").Value!.Holders.Count == 1, "Failed higher-priority transaction damaged prior layer or leaked holder.");
    f.Backend.ThrowOnceAfterWrite = true;
    var update = prior.SetValue(.4f);
    Check(update.Error?.Code == ErrorCode.MutationWriteFailed && update.Error.CapabilityId == TargetCapabilityIds.Mutations
        && update.Error.TargetId == failed.Error.TargetId && prior.IsActive, "Failed lease update lost its structured failure or valid owned prior candidate.");
    Equal(.3f, f.Backend.Values["music"]); prior.Dispose(); Equal(.8f, f.Backend.Values["music"]);
}

static void AutomaticOwnership()
{
    using var f = new Fixture(); var views = f.Start("mod"); var lease = Lease(views, "music", .2f);
    f.Host.Disable("mod"); Equal(.8f, f.Backend.Values["music"]); Check(!lease.IsActive && lease.SetValue(.5f).Error?.Code == ErrorCode.ScopeExpired && views.Read.GetSettings().Error?.Code == ErrorCode.ScopeExpired, "Stopped plugin retained game control.");
    lease.Dispose(); lease.Dispose(); f.Host.Enable("mod"); Check(f.Latest("mod").Read.GetSettings().Success, "Old scope blocked restarted plugin.");
}

static void ModuleRelease()
{
    using var f = new Fixture(); var views = f.Start("mod"); var lease = Lease(views, "music", .2f);
    f.Service.Dispose(); Equal(.8f, f.Backend.Values["music"]);
    Check(!lease.IsActive && views.Read.Read("music").Error?.Code == ErrorCode.ScopeExpired && f.Backend.DisposeCount == 1 && f.Backend.ValueAtDispose == .8f,
        "Module disposal failed to restore live baseline before backend cleanup/expire views.");
    f.Service.Dispose(); lease.Dispose(); Check(f.Backend.DisposeCount == 1, "Repeated module release double-freed backend.");
}

static void StopDuringRangeProbe()
{
    using var f = new Fixture(); var views = f.Start("mod"); var writes = f.Backend.Writes;
    f.Backend.BeforeRanges = () => f.Host.Disable("mod");
    var ended = views.Write.Override(new("music", .2f));
    Check(ended.Error?.Code == ErrorCode.ScopeExpired && f.Backend.Writes == writes,
        "The expired scope issued a setter after its slider probe returned.");
    Equal(.8f, f.Backend.Values["music"]);
}

static void ReentrantRangeProbe()
{
    using var f = new Fixture(); var first = f.Start("first"); var second = f.Start("second");
    Result<IGameSettingLease>? nested = null;
    var probes = f.Backend.RangeProbes;
    f.Backend.BeforeRanges = () => nested = second.Write.Override(new("sfx", .1f));
    using var acquired = Lease(first, "music", .2f);
    Check(nested?.Error?.Code == ErrorCode.Conflict && f.Backend.RangeProbes == probes + 1,
        "A nested override ran discovery or wrote before the outer probe published.");
    Equal(.6f, f.Backend.Values["sfx"]);
    Equal(.2f, f.Backend.Values["music"]);
}

static void ReleaseDuringSetter()
{
    using var f = new Fixture(); var views = f.Start("mod");
    var deferred = false;
    f.Backend.AfterWrite = () =>
    {
        f.Service.Dispose();
        deferred = f.Backend.DisposeCount == 0 && views.Read.Read("music").Error?.Code == ErrorCode.ScopeExpired;
    };
    var ended = views.Write.Override(new("music", .2f));
    Check(deferred && ended.Error?.Code == ErrorCode.ScopeExpired && f.Backend.DisposeCount == 1 && f.Backend.Writes == 2,
        "Module shutdown lost the in-flight write or released its backend before ownership readback.");
    Equal(.8f, f.Backend.Values["music"]);
    Equal(.8f, f.Backend.ValueAtDispose);
}

static void StopDuringLeaseUpdate()
{
    foreach (var duringSetter in new[] { false, true })
    {
        using var f = new Fixture(); var views = f.Start("mod"); using var lease = Lease(views, "music", .2f);
        var writes = f.Backend.Writes;
        if (duringSetter) f.Backend.AfterWrite = () => f.Host.Disable("mod");
        else f.Backend.BeforeRanges = lease.Dispose;
        var ended = lease.SetValue(.4f);
        Check(ended.Error?.Code == ErrorCode.ScopeExpired && !lease.IsActive
            && f.Backend.Writes == writes + (duringSetter ? 2 : 1),
            "An ended lease reported success, wrote during range capture, or lost its restoration.");
        Equal(.8f, f.Backend.Values["music"]);
    }
}

static void StopDuringPeriodicReapply()
{
    using var f = new Fixture(); var views = f.Start("mod"); using var lease = Lease(views, "music", .2f);
    f.Backend.Values["music"] = .65f;
    var writes = f.Backend.Writes;
    f.Backend.AfterWrite = () => f.Host.Disable("mod");
    Check(f.Service.Refresh().Success && !lease.IsActive && f.Backend.Writes == writes + 2,
        "Periodic reapplication lost its independent release after the setter returned.");
    Equal(.65f, f.Backend.Values["music"]);
}

static void BackendNotReady()
{
    using var f = new Fixture(); var views = f.Start("mod"); f.Backend.Initialized = false;
    Check(views.Read.GetSettings().Error?.Code == ErrorCode.NotReady && views.Write.Override(new("music", .3f)).Error?.Code == ErrorCode.NotReady && !f.Service.Refresh().Success,
        "Uninitialized settings falsely advertised ready.");
}

static void UiDisappearance()
{
    using var f = new Fixture(); var views = f.Start("mod"); var lease = Lease(views, "music", .3f);
    f.Backend.UiReady = false; Check(!f.Service.RefreshRanges().Success && views.Read.Read("music").Success && views.Write.Override(new("music", .4f)).Error?.Code == ErrorCode.NotReady, "Missing UI blocked reads or allowed new writes.");
    lease.Dispose(); Equal(.8f, f.Backend.Values["music"]);
}

static void DataSnapshots()
{
    using var f = new Fixture(); var snapshot = f.Service.CaptureDataSettings();
    Check(snapshot.Success && snapshot.Value!.Count == 2 && snapshot.Value.All(x => x.Source.Contains("getter") && x.Unit is not null), "Actual read source missing game setting semantics.");
    Equal(.8f, float.Parse(snapshot.Value!.Single(x => x.Id == "music").JsonValue, CultureInfo.InvariantCulture));
    f.Backend.Values["music"] = .4f; Equal(.8f, float.Parse(snapshot.Value!.Single(x => x.Id == "music").JsonValue, CultureInfo.InvariantCulture));
    Equal(.4f, float.Parse(f.Service.CaptureDataSettings().Value!.Single(x => x.Id == "music").JsonValue, CultureInfo.InvariantCulture));
}

static void ThreadAccess()
{
    using var f = new Fixture(); var views = f.Start("mod"); int reads = f.Backend.Reads, writes = f.Backend.Writes, ranges = f.Backend.RangeProbes;
    var codes = Task.Run(() => new[] { views.Read.Read("music").Error?.Code, views.Write.Override(new("music", .2f)).Error?.Code }).GetAwaiter().GetResult();
    Check(codes.All(x => x == ErrorCode.WrongThread) && f.Backend.Reads == reads && f.Backend.Writes == writes && f.Backend.RangeProbes == ranges, "Worker reached native setting callbacks.");
}

static void RootSelection()
{
    var roots = new ProbeRoots();
    var result = SettingsUiProbe.Capture(roots.Candidates(0, 1, 0), roots.Acquire, root => root.Id == 1, root =>
    {
        Check(roots.Live == 1 && !root.Disposed && roots.EnumerationDisposed, "Selected native root was freed before range calls or iterator leaked.");
        return new Dictionary<string, GameSettingRange> { ["music"] = new(0, 100) };
    });
    Check(result.Success && result.Value!["music"].Maximum == 100 && roots.Live == 0 && roots.Disposals == 3, "Unique candidate selection leaked roots.");
}

static void RootAmbiguity()
{
    var roots = new ProbeRoots(); bool read = false;
    var result = SettingsUiProbe.Capture(roots.Candidates(1, 2, 3), roots.Acquire, _ => true, root => { read = true; return new Dictionary<string, GameSettingRange>(); });
    Check(result.Error?.Code == ErrorCode.AmbiguousTarget && !read && roots.Live == 0 && roots.Disposals == 2 && roots.EnumerationDisposed, "Ambiguity selected first UI or leaked roots/enumerator.");
}

static void RootLimit()
{
    var roots = new ProbeRoots();
    var result = SettingsUiProbe.Capture(roots.Candidates(1, 0, 0), roots.Acquire, root => root.Id == 1,
        _ => throw new InvalidOperationException("Incomplete scan must not read ranges."), candidateLimit: 2);
    Check(result.Error?.Code == ErrorCode.QuotaExceeded && roots.Live == 0 && roots.Disposals == 2 && roots.EnumerationDisposed, "Truncated scan accepted UI or leaked roots.");
}

static void RootFailures()
{
    foreach (bool failLiveness in new[] { false, true })
    {
        var roots = new ProbeRoots();
        try
        {
            SettingsUiProbe.Capture(roots.Candidates(1), roots.Acquire, _ => failLiveness ? throw new InvalidOperationException("Native liveness call failed.") : true,
                _ => throw new InvalidOperationException("Native range call failed."));
            throw new Exception("Expected native probe exception.");
        }
        catch (InvalidOperationException) { }
        Check(roots.Live == 0 && roots.Disposals == 1 && roots.EnumerationDisposed, "Native exception leaked root or enumerator.");
    }
}

static void RootMissing()
{
    var roots = new ProbeRoots();
    var result = SettingsUiProbe.Capture(roots.Candidates(0, 0), roots.Acquire, _ => false, _ => new Dictionary<string, GameSettingRange>());
    Check(result.Error?.Code == ErrorCode.NotReady && roots.Live == 0 && roots.Disposals == 2 && roots.EnumerationDisposed, "Missing live UI was not temporary or leaked roots.");
}

static void GetterFailures()
{
    using var f = new Fixture(); var views = f.Start("reader"); int writes = f.Backend.Writes;
    f.Backend.Values["music"] = float.NaN;
    Check(views.Read.Read("music").Error?.Code == ErrorCode.CapabilityUnavailable, "Nonfinite native setting value was published.");
    f.Backend.List.Add(new("throwing", "throwing native getter", () => throw new InvalidOperationException("native getter failed"), null));
    Check(views.Read.Read("throwing").Error?.Code == ErrorCode.CapabilityUnavailable && f.Backend.Writes == writes, "Getter error was hidden or caused a write.");
}

sealed record SettingViews(IGameSettingsReadService Read, IGameSettingsOverrideService Write);
sealed class FakeBackend : IGameSettingsBackend
{
    public bool Initialized = true, UiReady = true, RangeUnsupported, ClampWrites, ThrowOnceAfterWrite;
    public int Reads, Writes, RangeProbes, DisposeCount;
    public Action? BeforeRanges, AfterWrite;
    public float ValueAtDispose;
    public Dictionary<string, float> Values { get; } = new(StringComparer.Ordinal) { ["music"] = .8f, ["sfx"] = .6f };
    public Dictionary<string, GameSettingRange> Ranges { get; } = new(StringComparer.Ordinal) { ["music"] = new(0, 1), ["sfx"] = new(0, 1) };
    public List<GameSettingBinding> List { get; } = new(); public IReadOnlyList<GameSettingBinding> Bindings => List;
    public bool Ready => Initialized;
    public FakeBackend()
    {
        foreach (string id in Values.Keys) List.Add(new(id, "fixture actual getter / setter", () => { Reads++; return Values[id]; }, value =>
        {
            Writes++; Values[id] = ClampWrites && value != .8f ? .5f : value;
            var callback = AfterWrite; AfterWrite = null; callback?.Invoke();
            if (ThrowOnceAfterWrite) { ThrowOnceAfterWrite = false; throw new InvalidOperationException("setter wrote then threw"); }
        }));
    }
    public Result<IReadOnlyDictionary<string, GameSettingRange>> GetWriteRanges()
    {
        RangeProbes++;
        var callback = BeforeRanges; BeforeRanges = null; callback?.Invoke();
        return RangeUnsupported ? Result<IReadOnlyDictionary<string, GameSettingRange>>.Fail(new(ErrorCode.OperationUnsupported, "Missing slider metadata")) :
            !UiReady ? Result<IReadOnlyDictionary<string, GameSettingRange>>.Fail(new(ErrorCode.NotReady, "Actual settings UI not ready")) : Result<IReadOnlyDictionary<string, GameSettingRange>>.Ok(Ranges);
    }
    public void Dispose() { DisposeCount++; ValueAtDispose = Values["music"]; }
}
sealed class ProbeRoots
{
    public int Live, Disposals; public bool EnumerationDisposed;
    public IEnumerable<int> Candidates(params int[] ids) { try { foreach (int id in ids) yield return id; } finally { EnumerationDisposed = true; } }
    public ProbeRoot Acquire(int id) { Live++; return new(this, id); }
    public sealed class ProbeRoot : IDisposable
    {
        private readonly ProbeRoots _owner; public int Id { get; } public bool Disposed { get; private set; }
        public ProbeRoot(ProbeRoots owner, int id) => (_owner, Id) = (owner, id);
        public void Dispose() { if (Disposed) throw new InvalidOperationException("Native root freed twice."); Disposed = true; _owner.Live--; _owner.Disposals++; }
    }
}
sealed class TestLogger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
sealed class Probe : IRizPlugin
{
    public PluginManifest Manifest { get; } private readonly Action<IPluginContext> _enable;
    public Probe(string id, Action<IPluginContext> enable) => (Manifest, _enable) = (new(id, new(1, 0, 0), new(1, 0, 0)), enable);
    public void Enable(IPluginContext context) => _enable(context); public void Disable() { }
}
sealed class Fixture : IDisposable
{
    public FakeBackend Backend { get; } = new(); public GameSettingsService Service { get; } public PluginHost Host { get; }
    private readonly Dictionary<string, SettingViews> _views = new(); private readonly MainThreadDispatcher _main = new();
    public Fixture()
    {
        var logger = new TestLogger(); Service = new(_main, logger, Backend);
        CheckInternal(Service.Refresh().Success); Host = new(new(new("test", "test", "test", "test")), _main, new EventHub(logger), logger);
    }
    public SettingViews Start(string id)
    {
        CheckInternal(Host.Register(new Probe(id, context => _views[id] = new(Service.ReadForPlugin(context), Service.OverrideForPlugin(context)))).Success);
        CheckInternal(Host.Enable(id).Success); return _views[id];
    }
    public SettingViews Latest(string id) => _views[id];
    private static void CheckInternal(bool ok) { if (!ok) throw new InvalidOperationException("Fixture initialization failed."); }
    public void Dispose() { Host.Dispose(); Service.Dispose(); _main.Dispose(); }
}
