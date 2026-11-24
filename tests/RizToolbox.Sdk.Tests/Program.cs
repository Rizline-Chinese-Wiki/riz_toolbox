using System.Text.Json;
using RizToolbox.Core;
using RizToolbox.Sdk;

// Current contracts and their actual host behavior. Current package loading
// and assembly isolation are covered by RizToolbox.Hosting.Tests.
var tests = new (string Name, Action Run)[]
{
    ("structured results preserve success values and complete failure metadata", ResultInvariants),
    ("public operation results permit only consistent states and preserve declared failures", OperationResultInvariants),
    ("public operation result JSON preserves nested values and errors and refuses contradictory states", OperationResultTransport),
    ("public service views preserve provider errors and reject expired and wrong thread access before entry", PublicServiceFailures),
    ("unknown error numeric values retain raw value and degrade safely", UnknownError),
    ("capability metadata snapshots copy inputs and report precise semantics", MetadataSnapshot),
    ("metadata lookup is deterministic case sensitive and safe for concurrent readers", MetadataLookup),
    ("metadata rejects contradictory and duplicate declarations", MetadataRejection),
    ("current plugin scopes release resources and callbacks and allow a fresh activation", CurrentPluginScope),
    ("real plugin host rejects invalid manifests and unavailable SDK ranges independently", CurrentManifestValidation)
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} SDK checks passed.");
TempTree.Cleanup();
return failures == 0 ? 0 : 1;

static void ResultInvariants()
{
    var error = new OperationError(ErrorCode.NotReady, "Still loading", "test.catalog", "chart/1", true);
    var failure = Result<int>.Fail(error);
    Check(!failure.Success && failure.Value == default && ReferenceEquals(failure.Error, error), "Generic failure lost its value/error invariants.");
    Check(failure.Error!.KnownCode == ErrorCode.NotReady && failure.Error.CapabilityId == "test.catalog"
        && failure.Error.TargetId == "chart/1" && failure.Error.Retryable, "Failure metadata changed.");
    var voidFailure = Result.Fail(error);
    Check(!voidFailure.Success && ReferenceEquals(voidFailure.Error, error), "Void failure replaced the declared structured error.");
    var success = Result<int>.Ok(42);
    Check(success.Success && success.Value == 42 && success.Error is null, "Successful value gained an error or changed.");
    Check(Result.Ok().Success && Result.Ok().Error is null, "Void success must not carry an error.");
    var nullable = Result<string?>.Ok(null);
    Check(nullable.Success && nullable.Value is null && nullable.Error is null, "Null cannot be used as an implicit failure signal.");
    Throws<ArgumentNullException>(() => Result.Fail(null!));
    Throws<ArgumentNullException>(() => Result<int>.Fail(null!));
    Throws<ArgumentException>(() => Result.Fail(new(ErrorCode.Conflict, " ")));
    Throws<ArgumentException>(() => Result<int>.Fail(new(ErrorCode.Conflict, null!)));
}

static void UnknownError()
{
    var unknown = new OperationError((ErrorCode)987654, "Future error", "test.capability", "target/1", true);
    var read = JsonSerializer.Deserialize<OperationError>(JsonSerializer.Serialize(unknown))!;
    Check(read == unknown && (int)read.Code == 987654 && read.KnownCode == ErrorCode.Unknown, "Unknown numeric code was lost or treated as success.");
    Check(ErrorCodes.Normalize(ErrorCode.SdkIncompatible) == ErrorCode.SdkIncompatible, "Known error code changed.");
}

static void OperationResultInvariants()
{
    var error = new OperationError(ErrorCode.NotReady, "Still loading", "test.catalog", "chart/1", true);
    var failure = OperationResult<int>.Fail(error); var empty = OperationResult.Fail(error);
    Check(!failure.Success && failure.Value == default && ReferenceEquals(failure.Error, error)
        && !empty.Success && ReferenceEquals(empty.Error, error), "An operation failure replaced its declared error or retained a value.");
    var success = OperationResult<int>.Ok(42); var nullable = OperationResult<string?>.Ok(null);
    Check(success.Success && success.Value == 42 && success.Error is null && nullable.Success && nullable.Value is null && nullable.Error is null,
        "Success/null-value invariants changed.");
    Check(OperationResult.Ok().Success && OperationResult.Ok().Error is null, "Void success retained an error.");
    Check(typeof(OperationResult).GetConstructors().Length == 0 && typeof(OperationResult<int>).GetConstructors().Length == 0,
        "A public constructor can fabricate contradictory result state.");
    foreach (var type in new[] { typeof(OperationResult), typeof(OperationResult<int>) })
        Check(type.GetMethods().Where(method => method.Name == "Fail").All(method => method.GetParameters()[0].ParameterType != typeof(string)),
            "A text-only failure overload remains.");
    Throws<ArgumentNullException>(() => OperationResult.Fail((OperationError)null!));
    Throws<ArgumentNullException>(() => OperationResult<int>.Fail((OperationError)null!));
    Throws<ArgumentException>(() => OperationResult.Fail(ErrorCode.InvalidArgument, " "));
    Throws<ArgumentException>(() => OperationResult<int>.Fail(ErrorCode.InvalidArgument, null!));
    var future = OperationResult.Fail((ErrorCode)987654, "Future error", "test.cap", "target/1", true);
    Check((int)future.Error!.Code == 987654 && future.Error.KnownCode == ErrorCode.Unknown && future.Error.Message == "Future error"
        && future.Error.CapabilityId == "test.cap" && future.Error.TargetId == "target/1" && future.Error.Retryable,
        "An unknown numeric error was parsed or discarded.");
}

static void OperationResultTransport()
{
    var error = new OperationError(ErrorCode.Conflict, "Declared conflict", "test.contract", "target/7", true);
    foreach (var options in new[] { new JsonSerializerOptions(), new JsonSerializerOptions(JsonSerializerDefaults.Web) })
    {
        var failed = JsonSerializer.Deserialize<OperationResult<int>>(JsonSerializer.Serialize(OperationResult<int>.Fail(error), options), options)!;
        Check(!failed.Success && failed.Value == 0 && failed.Error == error, "Transport lost structured failure metadata.");
        var future = error with { Code = (ErrorCode)987654 };
        var futureCopy = JsonSerializer.Deserialize<OperationResult<int>>(JsonSerializer.Serialize(OperationResult<int>.Fail(future), options), options)!;
        Check(futureCopy.Error == future && futureCopy.Error!.KnownCode == ErrorCode.Unknown, "Transport lost an unknown numeric code or its failure metadata.");
        var domain = JsonSerializer.Deserialize<Result<ServiceData>>(JsonSerializer.Serialize(Result<ServiceData>.Fail(error), options), options)!;
        Check(!domain.Success && domain.Value is null && domain.Error == error, "Existing domain results lost their shared failure contract.");
        var nullable = JsonSerializer.Deserialize<OperationResult<string?>>(JsonSerializer.Serialize(OperationResult<string?>.Ok(null), options), options)!;
        Check(nullable.Success && nullable.Value is null && nullable.Error is null, "Nullable success changed during transport.");
        var envelope = new ResultEnvelope(OperationResult<ServiceData>.Ok(new("value", new Dictionary<string, string> { ["field"] = "copied" })));
        var copied = JsonSerializer.Deserialize<ResultEnvelope>(JsonSerializer.Serialize(envelope, options), options)!;
        Check(copied.Operation.Success && copied.Operation.Value!.Fields!["field"] == "copied" && copied.Operation.Error is null,
            "A result nested in a shared DTO could not be deserialized.");
    }
    Throws<JsonException>(() => JsonSerializer.Deserialize<OperationResult>("{\"Success\":false}"));
    Throws<JsonException>(() => JsonSerializer.Deserialize<OperationResult>("{\"Success\":true,\"Error\":{\"Code\":15,\"Message\":\"conflict\"}}"));
    Throws<JsonException>(() => JsonSerializer.Deserialize<OperationResult<int>>("{\"Success\":false,\"Value\":42,\"Error\":{\"Code\":15,\"Message\":\"conflict\"}}"));
    Throws<JsonException>(() => JsonSerializer.Deserialize<OperationResult<string?>>("{\"Success\":true}"));
}

static void PublicServiceFailures()
{
    using var tree = new TempTree(); using var dispatch = new MainThreadDispatcher(); var log = new TestLog(); var events = new EventHub(log);
    var capabilities = new CapabilityRegistry(new("fixture", "fixture", "test", "structured"));
    var provider = new PublicFailureProvider(); capabilities.Register<IPublicFailureProbe>("test.public", provider);
    using var host = new PluginHost(capabilities, dispatch, events, log, tree.Path);
    IPluginContext? saved = null; IPublicFailureProbe? view = null;
    var plugin = new TestPlugin(CurrentManifest("current.errors"), context => { saved = context; view = context.GetService<IPublicFailureProbe>("test.public").Service!; });
    Check(host.Register(plugin).Success && host.Enable(plugin.Manifest.Id).Success, "Current public result plugin did not enable.");
    var declared = view!.Read();
    Check(ReferenceEquals(declared.Error, provider.Error) && !declared.Success && declared.Value == 0 && provider.Calls == 1,
        "A scoped service replaced or flattened the provider's exact structured failure.");
    var worker = Task.Run(() => view.Read()).GetAwaiter().GetResult();
    Check(worker.Error?.Code == ErrorCode.WrongThread && worker.Error.CapabilityId == "test.public" && provider.Calls == 1,
        "Wrong-thread rejection entered the provider or omitted capability context.");
    Check(host.Disable(plugin.Manifest.Id).Success, "Current public result plugin did not disable.");
    var stale = view.Read(); var lookup = saved!.GetService<IPublicFailureProbe>("test.public");
    Check(stale.Error?.Code == ErrorCode.ScopeExpired && stale.Error.CapabilityId == "test.public" && provider.Calls == 1,
        "An expired service entered the provider or flattened its scope failure.");
    Check(!lookup.Available && lookup.Service is null && lookup.Error?.Code == ErrorCode.ScopeExpired && lookup.Error.CapabilityId == "test.public",
        "An expired GetService request omitted its structured failure.");
}

static void MetadataSnapshot()
{
    var scenes = new[] { "Level", "LevelOver" };
    var operation = new CapabilityOperationInfo("observe", OperationThread.MainThread, ObservationMode.Sampled,
        CleanupResponsibility.EnableScope, false, scenes, "Normal-mode state sampling", 0.25);
    scenes[0] = "tampered";
    var operations = new[] { operation };
    var capability = new CapabilityInfo("test.runs", new(1, 0), CapabilityStability.Experimental, true, 7, operations, "stale unavailable reason");
    operations[0] = null!;
    Check(capability.Operations.Single() == operation && operation.SupportedScenes[0] == "Level", "Input mutation changed metadata.");
    Check(operation.SamplingIntervalSeconds == 0.25 && operation.Thread == OperationThread.MainThread
        && operation.Observation == ObservationMode.Sampled && operation.Cleanup == CleanupResponsibility.EnableScope && !operation.ModifiesGameplay, "Operation semantics lost.");
    Check(capability.Id == "test.runs" && capability.Generation == 7 && capability.Version == new Version(1, 0)
        && capability.Stability == CapabilityStability.Experimental && capability.Available && capability.UnavailableReason is null,
        "Availability snapshot lost its identity, generation or normalized reason.");
    Throws<NotSupportedException>(() => ((IList<string>)operation.SupportedScenes)[0] = "tampered");
    Throws<NotSupportedException>(() => ((IList<CapabilityOperationInfo>)capability.Operations).Clear());
}

static void MetadataLookup()
{
    var entries = new[] { Cap("z", false), Cap("a", true) };
    var catalog = new CapabilityMetadataCatalog(entries);
    entries[0] = Cap("other", true);
    Check(catalog.GetAll().Select(x => x.Id).SequenceEqual(new[] { "a", "z" }), "Catalog ordering/snapshot failed.");
    Check(catalog.Describe("z").Success && catalog.Describe("z").Value!.UnavailableReason == "Not ready", "Declared unavailable metadata should remain inspectable.");
    var missing = catalog.Describe("A");
    Check(!missing.Success && missing.Error?.Code == ErrorCode.CapabilityUnavailable && missing.Error.CapabilityId == "A",
        "Missing case-sensitive capability must report the requested identity.");
    Check(catalog.Describe("").Error?.Code == ErrorCode.InvalidArgument, "Empty ID accepted.");
    Throws<NotSupportedException>(() => ((IList<CapabilityInfo>)catalog.GetAll()).Clear());
    Parallel.For(0, 100, _ => Check(catalog.Describe("a").Success, "Concurrent immutable catalog read failed."));
}

static void MetadataRejection()
{
    Throws<ArgumentException>(() => new CapabilityMetadataCatalog(new[] { Cap("a", true), Cap("a", false) }));
    Throws<ArgumentException>(() => new CapabilityInfo("a", new(1, 0), CapabilityStability.Stable, false, 0, Array.Empty<CapabilityOperationInfo>()));
    Throws<ArgumentOutOfRangeException>(() => new CapabilityInfo("a", new(1, 0), CapabilityStability.Stable, true, -1, Array.Empty<CapabilityOperationInfo>()));
    Throws<ArgumentOutOfRangeException>(() => new CapabilityOperationInfo("read", OperationThread.MainThread, ObservationMode.Snapshot, CleanupResponsibility.None, false, new[] { "*" }, "Read", 0.1));
    Throws<ArgumentOutOfRangeException>(() => new CapabilityOperationInfo("observe", OperationThread.MainThread, ObservationMode.Sampled, CleanupResponsibility.None, false, new[] { "Level" }, "Observe", double.NaN));
    Throws<ArgumentException>(() => new CapabilityOperationInfo("read", OperationThread.MainThread, ObservationMode.Snapshot,
        CleanupResponsibility.None, false, new[] { "Level", "Level" }, "Read"));
    var op = new CapabilityOperationInfo("read", OperationThread.MainThread, ObservationMode.Snapshot, CleanupResponsibility.None, false, new[] { "*" }, "Read");
    Throws<ArgumentException>(() => new CapabilityInfo("a", new(1, 0), CapabilityStability.Stable, true, 0, new[] { op, op }));
}

static void CurrentPluginScope()
{
    using var tree = new TempTree();
    using var dispatch = new MainThreadDispatcher();
    var log = new TestLog(); var events = new EventHub(log);
    var capabilities = new CapabilityRegistry(new("fixture", "fixture", "test", "current-sdk"));
    var ui = new TestUi(); capabilities.Register<IUiService>(CapabilityIds.Ui, ui);
    using var host = new PluginHost(capabilities, dispatch, events, log, tree.Path);
    IPanelHandle? panel = null; CancellationToken firstLifetime = default; int callbacks = 0, activations = 0;
    var plugin = new TestPlugin(CurrentManifest("current.scope") with { RequiredCapabilities = new[] { CapabilityIds.Ui } }, context =>
    {
        activations++;
        var scope = ((IScopedPluginContext)context).CreateScope("panel");
        if (activations == 1) firstLifetime = scope.Lifetime;
        panel = scope.GetService<IUiService>(CapabilityIds.Ui).Service!.CreatePanel(new("status", "Test", "Ready")).Value!;
        scope.Events.Subscribe<FrameUpdate>(_ => { callbacks++; panel.SetText("Updated"); });
    });
    Check(host.Register(plugin).Success && host.Enable(plugin.Manifest.Id).Success, "Current plugin did not activate.");
    events.Publish(new FrameUpdate(1));
    Check(ui.Created == 1 && ui.TextUpdates == 1 && callbacks == 1 && !firstLifetime.IsCancellationRequested, "Current activation did not own a live scope.");
    Check(host.Disable(plugin.Manifest.Id).Success && ui.Disposed == 1 && firstLifetime.IsCancellationRequested, "Stopping the plugin did not expire its child scope and panel.");
    events.Publish(new FrameUpdate(2));
    Check(callbacks == 1 && !panel!.SetText("stale").Success, "Stopped activation retained its callback or writable panel.");
    Check(host.Enable(plugin.Manifest.Id).Success && ui.Created == 2, "Fresh activation did not recreate its resources.");
    events.Publish(new FrameUpdate(3));
    Check(callbacks == 2 && ui.TextUpdates == 2, "Fresh activation duplicated or lost its subscription.");
    Check(host.Disable(plugin.Manifest.Id).Success && ui.Disposed == 2, "Reactivated resources were not released exactly once.");
}

static void CurrentManifestValidation()
{
    using var tree = new TempTree(); using var dispatch = new MainThreadDispatcher();
    var log = new TestLog(); var events = new EventHub(log);
    var capabilities = new CapabilityRegistry(new("fixture", "fixture", "test", "current-sdk"));
    using var host = new PluginHost(capabilities, dispatch, events, log, tree.Path); int enabled = 0;
    var valid = CurrentManifest("current.valid");
    Check(host.Register(new TestPlugin(valid, _ => enabled++)).Success, "Current manifest was rejected.");
    Check(!host.Register(new TestPlugin(valid)).Success, "Duplicate plugin ID accepted.");
    Check(!host.Register(new TestPlugin(valid with { Id = "Bad ID" })).Success, "Unsafe plugin ID accepted.");
    Check(!host.Register(new TestPlugin(valid with { Id = "current.invalid", MaxSdkExclusive = SdkInfo.Version })).Success,
        "An empty SDK range was accepted.");
    var future = valid with { Id = "current.future", MinSdk = new(99, 0), MaxSdkExclusive = null };
    var excluded = valid with { Id = "current.excluded", MinSdk = new(1, 0), MaxSdkExclusive = SdkInfo.Version };
    Check(host.Register(new TestPlugin(future, _ => throw new InvalidOperationException("Future plugin executed."))).Success
        && host.Register(new TestPlugin(excluded, _ => throw new InvalidOperationException("Excluded plugin executed."))).Success,
        "Valid but unavailable SDK declarations should remain inspectable.");
    foreach (var id in new[] { future.Id, excluded.Id })
    {
        var result = host.Enable(id);
        Check(!result.Success && result.Error?.Code == ErrorCode.SdkIncompatible && result.Error.TargetId == id && !result.Error.Retryable,
            "Actual host did not reject an unavailable SDK range with its declared code and plugin identity.");
    }
    Check(host.Enable(valid.Id).Success && enabled == 1, "An unrelated rejected manifest blocked the valid current plugin.");
}

static PluginManifest CurrentManifest(string id) => new(id, new(1, 0), SdkInfo.Version, new(2, 0));

static CapabilityInfo Cap(string id, bool available) => new(id, new(1, 0), CapabilityStability.Experimental,
    available, 1, Array.Empty<CapabilityOperationInfo>(), available ? null : "Not ready");

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

sealed class TestPlugin : IRizPlugin
{
    private readonly Action<IPluginContext>? _enable;
    public PluginManifest Manifest { get; }
    public TestPlugin(PluginManifest manifest, Action<IPluginContext>? enable = null) => (Manifest, _enable) = (manifest, enable);
    public void Enable(IPluginContext context) => _enable?.Invoke(context);
    public void Disable() { }
}

sealed class TestLog : ILogger
{
    public readonly List<string> Messages = new();
    public void Log(LogLevel level, string message, Exception? exception = null) => Messages.Add(message);
}

sealed class TestUi : IUiService
{
    public int Created, Disposed, TextUpdates;
    public string LastText = "";
    public OperationResult<IPanelHandle> CreatePanel(PanelDefinition definition)
    {
        Created++;
        return OperationResult<IPanelHandle>.Ok(new Panel(this, definition.Id));
    }
    private sealed class Panel : IPanelHandle
    {
        private readonly TestUi _owner;
        private bool _disposed;
        public Panel(TestUi owner, string id) => (_owner, Id) = (owner, id);
        public string Id { get; }
        public OperationResult SetText(string text) { if (_disposed) return OperationResult.Fail(ErrorCode.TargetExpired, "Disposed", CapabilityIds.Ui, Id); _owner.TextUpdates++; _owner.LastText = text; return OperationResult.Ok(); }
        public void Dispose() { if (_disposed) return; _disposed = true; _owner.Disposed++; }
    }
}

public interface IPublicFailureProbe { OperationResult<int> Read(); }
sealed class PublicFailureProvider : IPublicFailureProbe
{
    public readonly OperationError Error = new(ErrorCode.NotReady, "Provider is preparing data.", "test.public", "target/3", true);
    public int Calls;
    public OperationResult<int> Read() { Calls++; return OperationResult<int>.Fail(Error); }
}
sealed record ResultEnvelope(OperationResult<ServiceData> Operation);

sealed class TempTree : IDisposable
{
    private static readonly List<string> Retired = new();
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "riz-sdk-check-" + Guid.NewGuid().ToString("N"));
    public TempTree() => Directory.CreateDirectory(Path);
    public void Dispose() => Retired.Add(Path);
    public static void Cleanup()
    {
        // Each retired tree was generated for this test host. Validate its exact temp prefix before cleanup.
        foreach (var path in Retired)
        {
            var resolved = System.IO.Path.GetFullPath(path);
            if (!resolved.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !System.IO.Path.GetFileName(resolved).StartsWith("riz-sdk-check-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe temporary cleanup target.");
            try { Directory.Delete(resolved, recursive: true); }
            catch (IOException) { /* Leave a locked test artifact available for diagnosis. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
