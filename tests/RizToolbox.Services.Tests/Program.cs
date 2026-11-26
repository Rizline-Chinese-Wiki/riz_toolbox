using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using RizToolbox.Core;
using RizToolbox.Core.Services;
using RizToolbox.Sdk;

var tests = new (string Name, Action Run)[]
{
    ("SDK DispatchProxy service invocation, version compatibility and duplicate rejection", BasicServices),
    ("optional readiness initial/change notification and generation withdrawal", ReadinessAndGeneration),
    ("provider and consumer stop invalidate cached registrations and views", ScopeOwnership),
    ("main-thread guard rejects worker calls before reaching provider", MainThreadCalls),
    ("request and response sizes are bounded with correct exception ownership", PayloadLimits),
    ("request/response snapshots isolate mutable dictionary backing stores", SnapshotIsolation),
    ("provider exception faults provider once and preserves unrelated service", FaultOwnership),
    ("service recursion reports cycle path and faults only deepest provider", CycleAttribution),
    ("host-pinned shared contracts support bounded asynchronous calls", AsyncCalls),
    ("async timeout expires proxy and reports provider on main thread", AsyncTimeout),
    ("a saturated dispatcher cannot drop an asynchronous provider fault", SaturatedDispatcherFault),
    ("consumer cancellation does not fault provider", ConsumerCancellation),
    ("synchronous results cannot outlive the calling scope", SynchronousConsumerStop),
    ("consumer stop cancels a pending call without waiting for the provider", PendingConsumerStop),
    ("explicit withdrawal cancels pending calls without faulting the owner", PendingProviderWithdrawal),
    ("a queued fault from a retired registration cannot stop a new activation", StaleQueuedFault),
    ("a withdrawal watcher can restart a provider without receiving its retired fault", WatchRestartDuringFault),
    ("a stopping provider is unavailable before its disable callback returns", LookupWhileStopping),
    ("synchronous elapsed budget detects slow provider without claiming preemption", SynchronousBudget),
    ("private ALC lookalike contracts refused despite matching names and bytes", PrivateAlcIdentity),
    ("unsupported pointers/delegates/opaque return contracts refused", UnsafeContracts),
    ("read-only messages ordered, cloned per subscriber, versioned and automatically removed", MessageChannels),
    ("message recursion and callback faults do not break unrelated delivery", MessageFaults),
    ("availability callback fault belongs to observer, not service provider", WatchFault),
    ("registry disposal withdraws service and invalidates retained proxy", RegistryDisposal),
    ("formal PluginHost GetService path stops provider but preserves catching consumer", HostIntegration),
    ("nested provider fault preserves the soft consumer and its readiness subscription", WatchCallbackProviderFault),
    ("nested provider fault preserves the soft consumer and its message subscription", MessageCallbackProviderFault),
    ("independent readiness and message callback failures still stop their owners", IndependentCallbackFaults)
};
var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} inter-plugin service checks passed.");
return failed == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static T Need<T>(OperationResult<T> result) { Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing result"); return result.Value!; }
static T Proxy<T>(IPluginServices view, string id = "echo", int min = 1, int max = 2) where T : class
{
    var result = view.Lookup<T>(new(id, new(min, 0), new(max, 0)));
    Check(result.Available && result.Service is not null, result.Error?.Message ?? "Missing proxy"); return result.Service!;
}
static void Throws(Action action, string text)
{
    Exception? failure = null;
    try { action(); }
    catch (Exception ex) { failure = ex; }
    Check(failure is not null && failure.ToString().Contains(text), $"Expected rejection containing '{text}', got: {failure}");
}
static void Pin<T>(Fixture f) where T : class
    => Check(f.Registry.AllowSharedContract<T>(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(T).Assembly.Location)))).Success, "Host contract pin failed.");
static T Pump<T>(Fixture f, Task<T> task)
{
    var stop = DateTime.UtcNow.AddSeconds(3);
    while (!task.IsCompleted && DateTime.UtcNow < stop) { f.PumpHost(); Thread.Sleep(1); }
    f.PumpHost(); return task.GetAwaiter().GetResult();
}

static void BasicServices()
{
    using var f = new Fixture(); using var provider = f.Context("provider"); using var consumer = f.Context("consumer");
    var publication = f.Registry.ForPlugin(provider).Publish<IPluginDataService>("echo", new(1, 1), new Provider(r => OperationResult<ServiceData>.Ok(new(r.Operation))));
    var registration = Need(publication); var view = f.Registry.ForPlugin(consumer);
    Check(Proxy<IPluginDataService>(view).Invoke(new("hello")).Value!.Value == "hello", "DispatchProxy invocation failed.");
    Check(!view.Lookup<IPluginDataService>(new("echo", new(2, 0), new(3, 0))).Available, "Incompatible version accepted.");
    Check(!f.Registry.ForPlugin(consumer).Publish<IPluginDataService>("echo", new(1, 1), new Provider()).Success, "Duplicate service replaced owner.");
    Check(registration.Descriptor.ProviderId == "provider" && provider.Resources.Count == 1, "Registration owner missing.");
}

static void ReadinessAndGeneration()
{
    using var f = new Fixture(); using var provider = f.Context("provider"); using var consumer = f.Context("consumer");
    var view = f.Registry.ForPlugin(consumer); var changes = new List<ServiceAvailability>();
    view.Watch(new("echo", new(1, 0), new(2, 0)), changes.Add);
    Check(changes.Count == 1 && !changes[0].Available, "Initial optional snapshot missing.");
    var registration = Need(f.Registry.ForPlugin(provider).Publish<IPluginDataService>("echo", new(1, 0), new Provider()));
    var proxy = Proxy<IPluginDataService>(view); var generation = changes.Last().Service!.Generation;
    registration.Dispose(); registration.Dispose();
    Throws(() => proxy.Invoke(new("expired")), "ServiceUnavailable");
    Need(f.Registry.ForPlugin(provider).Publish<IPluginDataService>("echo", new(1, 1), new Provider()));
    Check(changes.Select(x => x.Available).SequenceEqual(new[] { false, true, false, true }) && changes.Last().Service!.Generation > generation, "Readiness generations incorrect.");
    Throws(() => proxy.Invoke(new("still expired")), "ServiceUnavailable");
    Check(Proxy<IPluginDataService>(view).Invoke(new("new")).Success, "Replacement proxy failed.");
}

static void ScopeOwnership()
{
    using var f = new Fixture(); using var provider = f.Context("provider"); using var consumer = f.Context("consumer");
    var p = f.Registry.ForPlugin(provider); var c = f.Registry.ForPlugin(consumer);
    Need(p.Publish<IPluginDataService>("echo", new(1, 0), new Provider())); var proxy = Proxy<IPluginDataService>(c);
    provider.Dispose(); Throws(() => proxy.Invoke(new("x")), "ServiceUnavailable");
    Throws(() => p.Publish<IPluginDataService>("later", new(1, 0), new Provider()), "ObjectDisposedException");
    consumer.Dispose(); Throws(() => c.Lookup<IPluginDataService>(new("echo", new(1, 0))), "ObjectDisposedException");
}

static void MainThreadCalls()
{
    using var f = new Fixture(); using var provider = f.Context("provider"); using var consumer = f.Context("consumer"); var calls = 0;
    Need(f.Registry.ForPlugin(provider).Publish<IPluginDataService>("echo", new(1, 0), new Provider(_ => { calls++; return OperationResult<ServiceData>.Ok(new("x")); })));
    var proxy = Proxy<IPluginDataService>(f.Registry.ForPlugin(consumer)); Exception? error = null;
    var worker = new Thread(() => { try { proxy.Invoke(new("x")); } catch (Exception ex) { error = ex; } }); worker.Start(); worker.Join();
    Check(error is InvalidOperationException && calls == 0 && f.Faults.Count == 0, "Wrong-thread call reached or faulted provider.");
}

static void PayloadLimits()
{
    using var f = new Fixture(new(MaximumPayloadBytes: 256)); using var provider = f.Context("provider"); using var consumer = f.Context("consumer"); var calls = 0;
    Need(f.Registry.ForPlugin(provider).Publish<IPluginDataService>("echo", new(1, 0), new Provider(_ => { calls++; return OperationResult<ServiceData>.Ok(new(new string('x', 400))); })));
    var proxy = Proxy<IPluginDataService>(f.Registry.ForPlugin(consumer));
    Throws(() => proxy.Invoke(new(new string('x', 400))), "ServiceRequestRejected");
    Check(calls == 0 && f.Faults.Count == 0, "Oversized client request faulted provider.");
    Throws(() => proxy.Invoke(new("small")), "ServicePayloadTooLarge");
    Check(calls == 1 && f.Faults.SequenceEqual(new[] { "provider" }), "Oversized response not attributed to provider.");
}

static void SnapshotIsolation()
{
    using var f = new Fixture(); using var provider = f.Context("provider"); using var consumer = f.Context("consumer");
    var source = new Dictionary<string, string> { ["value"] = "original" }; var response = new Dictionary<string, string> { ["value"] = "response" };
    Need(f.Registry.ForPlugin(provider).Publish<IPluginDataService>("echo", new(1, 0), new Provider(request =>
    { ((IDictionary<string, string>)request.Arguments!)["value"] = "changed"; return OperationResult<ServiceData>.Ok(new("ok", response)); })));
    var result = Proxy<IPluginDataService>(f.Registry.ForPlugin(consumer)).Invoke(new("x", source));
    Check(source["value"] == "original", "Provider changed caller's mutable DTO backing store.");
    ((IDictionary<string, string>)result.Value!.Fields!)["value"] = "consumer changed";
    Check(response["value"] == "response", "Consumer changed provider's mutable DTO backing store.");
}

static void FaultOwnership()
{
    using var f = new Fixture(); using var provider = f.Context("bad"); using var consumer = f.Context("consumer"); using var good = f.Context("good");
    Need(f.Registry.ForPlugin(provider).Publish<IPluginDataService>("bad", new(1, 0), new Provider(_ => throw new Exception("provider fault"))));
    Need(f.Registry.ForPlugin(good).Publish<IPluginDataService>("good", new(1, 0), new Provider()));
    var view = f.Registry.ForPlugin(consumer); var proxy = Proxy<IPluginDataService>(view, "bad");
    Throws(() => proxy.Invoke(new("x")), "provider fault"); Throws(() => proxy.Invoke(new("x")), "ServiceUnavailable");
    Check(f.Faults.SequenceEqual(new[] { "bad" }) && Proxy<IPluginDataService>(view, "good").Invoke(new("x")).Success, "Fault attribution or unrelated provider isolation failed.");
}

static void CycleAttribution()
{
    using var f = new Fixture(); using var a = f.Context("a"); using var b = f.Context("b"); using var caller = f.Context("caller");
    IPluginDataService? proxyA = null, proxyB = null;
    Need(f.Registry.ForPlugin(a).Publish<IPluginDataService>("a", new(1, 0), new Provider(request => proxyB!.Invoke(request))));
    Need(f.Registry.ForPlugin(b).Publish<IPluginDataService>("b", new(1, 0), new Provider(request => proxyA!.Invoke(request))));
    proxyA = Proxy<IPluginDataService>(f.Registry.ForPlugin(b), "a"); proxyB = Proxy<IPluginDataService>(f.Registry.ForPlugin(a), "b");
    Throws(() => Proxy<IPluginDataService>(f.Registry.ForPlugin(caller), "a").Invoke(new("x")), "ServiceCallCycle");
    Check(f.Faults.SequenceEqual(new[] { "b" }), "Cycle was blamed on every forwarding provider instead of the recursion initiator.");
}

static void AsyncCalls()
{
    using var f = new Fixture(); Pin<IAsyncEcho>(f); using var provider = f.Context("provider"); using var consumer = f.Context("consumer");
    Need(f.Registry.ForPlugin(provider).Publish<IAsyncEcho>("echo", new(1, 0), new AsyncEcho()));
    Check(Pump(f, Proxy<IAsyncEcho>(f.Registry.ForPlugin(consumer)).Echo(new("async"))).Value == "async", "Async service proxy failed.");
}

static void AsyncTimeout()
{
    using var f = new Fixture(new(TimeoutMilliseconds: 20)); Pin<IAsyncEcho>(f); using var provider = f.Context("provider"); using var consumer = f.Context("consumer");
    var completion = new TaskCompletionSource<ServiceData>();
    Need(f.Registry.ForPlugin(provider).Publish<IAsyncEcho>("echo", new(1, 0), new AsyncEcho(_ => completion.Task)));
    var proxy = Proxy<IAsyncEcho>(f.Registry.ForPlugin(consumer)); var pending = proxy.Echo(new("never"));
    Throws(() => Pump(f, pending), "Timeout"); f.PumpHost();
    Check(f.Faults.SequenceEqual(new[] { "provider" }), "Async timeout not attributed on main thread.");
    Throws(() => proxy.Echo(new("expired")), "ServiceUnavailable"); completion.TrySetResult(new("late"));
}

static void ConsumerCancellation()
{
    using var f = new Fixture(); Pin<IAsyncEcho>(f); using var provider = f.Context("provider"); using var consumer = f.Context("consumer");
    var completion = new TaskCompletionSource<ServiceData>();
    Need(f.Registry.ForPlugin(provider).Publish<IAsyncEcho>("echo", new(1, 0), new AsyncEcho(_ => completion.Task)));
    var task = Proxy<IAsyncEcho>(f.Registry.ForPlugin(consumer)).Echo(new("later")); consumer.Dispose(); completion.SetResult(new("later"));
    Throws(() => Pump(f, task), "ServiceConsumerStopped");
    Check(f.Faults.Count == 0, "Consumer stop faulted provider.");
}

static void SynchronousConsumerStop()
{
    using var f = new Fixture();
    using var provider = f.Context("provider");
    using var consumer = f.Context("consumer");
    Need(f.Registry.ForPlugin(provider).Publish<IPluginDataService>("echo", new(1, 0), new Provider(_ =>
    {
        consumer.Dispose();
        return OperationResult<ServiceData>.Ok(new("late"));
    })));
    var proxy = Proxy<IPluginDataService>(f.Registry.ForPlugin(consumer));
    Throws(() => proxy.Invoke(new("stop")), "ServiceConsumerStopped");
    Check(f.Faults.Count == 0, "Consumer expiry was treated as a provider error.");
}

static void PendingConsumerStop()
{
    using var f = new Fixture(new(TimeoutMilliseconds: 200));
    Pin<IAsyncEcho>(f);
    using var provider = f.Context("provider");
    using var consumer = f.Context("consumer");
    var completion = new TaskCompletionSource<ServiceData>();
    Need(f.Registry.ForPlugin(provider).Publish<IAsyncEcho>("echo", new(1, 0), new AsyncEcho(_ => completion.Task)));
    var pending = Proxy<IAsyncEcho>(f.Registry.ForPlugin(consumer)).Echo(new("never"));
    consumer.Dispose();
    Throws(() => Pump(f, pending), "ServiceConsumerStopped");
    Check(f.Main.PendingCount == 0 && f.Faults.Count == 0 && !completion.Task.IsCompleted,
        "Consumer cancellation faulted the provider or awaited its completion.");
}

static void PendingProviderWithdrawal()
{
    using var f = new Fixture(new(TimeoutMilliseconds: 200));
    Pin<IAsyncEcho>(f);
    using var provider = f.Context("provider");
    using var consumer = f.Context("consumer");
    var completion = new TaskCompletionSource<ServiceData>();
    var registration = Need(f.Registry.ForPlugin(provider).Publish<IAsyncEcho>("echo", new(1, 0), new AsyncEcho(_ => completion.Task)));
    var pending = Proxy<IAsyncEcho>(f.Registry.ForPlugin(consumer)).Echo(new("never"));
    registration.Dispose();
    Throws(() => Pump(f, pending), "ServiceUnavailable");
    Check(f.Faults.Count == 0 && !completion.Task.IsCompleted, "Explicit withdrawal faulted the owner or depended on the task completing.");
}

static PluginHost ServiceHost(Fixture f)
{
    var capabilities = new CapabilityRegistry(new("test", "test", "test", "test"));
    capabilities.RegisterScoped<IPluginServices>(InterPluginCapabilityIds.Services, context => f.Registry.ForPlugin(context));
    var host = new PluginHost(capabilities, f.Main, new EventHub(f.Log), f.Log);
    f.Registry.ProviderFaulted += (id, error) => host.ReportPluginFault(id, error);
    return host;
}

static void SaturatedDispatcherFault()
{
    using var f = new Fixture(new(TimeoutMilliseconds: 2000), maxPending: 1);
    using var host = ServiceHost(f);
    using var consumer = f.Context("consumer");
    Pin<IAsyncEcho>(f);
    var failure = new TaskCompletionSource<ServiceData>(TaskCreationOptions.RunContinuationsAsynchronously);
    var unfinished = new TaskCompletionSource<ServiceData>();
    int stopped = 0;
    host.Register(new HostPlugin("provider", context =>
        Need(context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!.Publish<IAsyncEcho>(
            "echo", new(1, 0), new AsyncEcho(request => request.Operation == "bad" ? failure.Task : unfinished.Task))),
        () => stopped++));
    Check(host.Enable("provider").Success, "Provider could not enable.");
    var view = f.Registry.ForPlugin(consumer);
    var changes = new List<bool>();
    view.Watch(new("echo", new(1, 0)), row => changes.Add(row.Available));
    var proxy = Proxy<IAsyncEcho>(view);
    var bad = proxy.Echo(new("bad"));
    var sibling = proxy.Echo(new("waiting"));

    int unrelated = 0;
    Task? queued = null;
    Task.Run(() => { queued = f.Main.InvokeAsync(() => unrelated++); }).GetAwaiter().GetResult();
    Check(f.Main.PendingCount == 1, "The ordinary queue was not saturated.");
    failure.SetException(new InvalidOperationException("provider failed during saturation"));
    Throws(() => bad.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(), "ServiceProviderFailed");
    Check(stopped == 0 && f.Faults.Count == 0, "The fault escaped the main-thread boundary.");
    Throws(() => proxy.Echo(new("again")), "ServiceUnavailable");
    f.PumpHost();
    queued!.GetAwaiter().GetResult();
    Check(stopped == 1 && f.Faults.SequenceEqual(new[] { "provider" }) && unrelated == 1
        && changes.SequenceEqual(new[] { true, false }) && host.Statuses.Single().State == PluginState.Faulted,
        "The saturated queue lost the provider fault or delayed its withdrawal notification.");
    Throws(() => sibling.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(), "ServiceUnavailable");
    Check(!unfinished.Task.IsCompleted, "The broker waited for the provider to finish its surviving task.");
}

static void StaleQueuedFault()
{
    using var f = new Fixture();
    using var host = ServiceHost(f);
    using var consumer = f.Context("consumer");
    Pin<IAsyncEcho>(f);
    var failure = new TaskCompletionSource<ServiceData>(TaskCreationOptions.RunContinuationsAsynchronously);
    int activations = 0;
    host.Register(new HostPlugin("provider", context =>
    {
        var activate = ++activations;
        Need(context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!.Publish<IAsyncEcho>(
            "echo", new(1, 0), new AsyncEcho(_ => activate == 1 ? failure.Task : Task.FromResult(new ServiceData("current")))));
    }));
    Check(host.Enable("provider").Success, "Provider could not enable.");
    var view = f.Registry.ForPlugin(consumer);
    var old = Proxy<IAsyncEcho>(view);
    var pending = old.Echo(new("bad"));
    failure.SetException(new InvalidOperationException("fault from first activation"));
    Throws(() => pending.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(), "fault from first activation");
    Check(f.Main.PendingCount == 0 && f.Faults.Count == 0, "Fault used the ordinary queue or was reported before the host tick.");
    Check(host.Disable("provider").Success && host.Enable("provider").Success && activations == 2, "Provider restart failed.");
    f.PumpHost();
    Check(host.Statuses.Single().State == PluginState.Enabled && f.Faults.Count == 0, "A queued fault stopped or reported the replacement activation.");
    Check(Pump(f, Proxy<IAsyncEcho>(view).Echo(new("good"))).Value == "current", "Replacement registration was withdrawn.");
    Throws(() => old.Echo(new("old")), "ServiceUnavailable");
}

static void WatchRestartDuringFault()
{
    using var f = new Fixture();
    using var host = ServiceHost(f);
    using var consumer = f.Context("consumer");
    int activations = 0;
    host.Register(new HostPlugin("provider", context =>
    {
        var activate = ++activations;
        Need(context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!.Publish<IPluginDataService>(
            "echo", new(1, 0), new Provider(_ => activate == 1 ? throw new InvalidOperationException("old synchronous fault") : OperationResult<ServiceData>.Ok(new("current")))));
    }));
    Check(host.Enable("provider").Success, "Provider could not enable.");
    var view = f.Registry.ForPlugin(consumer);
    var old = Proxy<IPluginDataService>(view);
    bool restarted = false;
    view.Watch(new("echo", new(1, 0)), current =>
    {
        if (!current.Available && activations == 1)
            restarted = host.Disable("provider").Success && host.Enable("provider").Success;
    });
    Throws(() => old.Invoke(new("bad")), "old synchronous fault");
    Check(restarted && host.Statuses.Single().State == PluginState.Enabled && f.Faults.Count == 0,
        "A fault report crossed the activation change in the withdrawal observer.");
    Check(Proxy<IPluginDataService>(view).Invoke(new("good")).Value?.Value == "current", "New registration did not survive the observer.");
}

static void LookupWhileStopping()
{
    using var f = new Fixture();
    using var host = ServiceHost(f);
    using var consumer = f.Context("consumer");
    var view = f.Registry.ForPlugin(consumer);
    ServiceResolution<IPluginDataService>? observed = null;
    host.Register(new HostPlugin("provider", context => Need(context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!
        .Publish<IPluginDataService>("echo", new(1, 0), new Provider())),
        () => observed = view.Lookup<IPluginDataService>(new("echo", new(1, 0)))));
    Check(host.Enable("provider").Success && host.Disable("provider").Success, "Provider could not run its shutdown callback.");
    Check(observed is { Available: false, Service: null, Error.Code: ErrorCode.ServiceUnavailable },
        "A deactivated provider was published as available during teardown.");
}

static void SynchronousBudget()
{
    using var f = new Fixture(new(TimeoutMilliseconds: 10)); using var provider = f.Context("provider"); using var consumer = f.Context("consumer"); var finished = false;
    Need(f.Registry.ForPlugin(provider).Publish<IPluginDataService>("echo", new(1, 0), new Provider(_ => { Thread.Sleep(25); finished = true; return OperationResult<ServiceData>.Ok(new("x")); })));
    Throws(() => Proxy<IPluginDataService>(f.Registry.ForPlugin(consumer)).Invoke(new("x")), "ServiceTimeout");
    Check(finished && f.Faults.SequenceEqual(new[] { "provider" }), "Synchronous budget behavior incorrect.");
}

static void PrivateAlcIdentity()
{
    using var f = new Fixture(); using var provider = f.Context("provider");
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IAsyncEcho).Assembly.Location)));
    Check(!f.Registry.AllowSharedContract<IAsyncEcho>(new string('0', 64)).Success, "Incorrect contract hash accepted.");
    Check(f.Registry.AllowSharedContract<IAsyncEcho>(hash).Success, "Exact host contract refused.");
    var alc = new AssemblyLoadContext("private-lookalike", true);
    try
    {
        var assembly = alc.LoadFromAssemblyPath(typeof(IAsyncEcho).Assembly.Location);
        var contract = assembly.GetType(typeof(IAsyncEcho).FullName!)!;
        var implementation = Activator.CreateInstance(assembly.GetType(typeof(PrivateProvider).FullName!)!)!;
        var view = f.Registry.ForPlugin(provider);
        var result = typeof(IPluginServices).GetMethod(nameof(IPluginServices.Publish))!.MakeGenericMethod(contract)
            .Invoke(view, new object[] { "private", new Version(1, 0), implementation })!;
        Check(!(bool)result.GetType().GetProperty("Success")!.GetValue(result)!, "Private ALC lookalike was cast to shared identity.");
    }
    finally { alc.Unload(); }
}

static void UnsafeContracts()
{
    using var f = new Fixture(); Pin<IAsyncEcho>(f); using var provider = f.Context("provider");
    var view = f.Registry.ForPlugin(provider);
    Check(!view.Publish<IUnsafe>("unsafe", new(1, 0), new UnsafeProvider()).Success, "Pointer contract accepted.");
    Check(!view.Publish<IInheritedUnsafe>("inherited-unsafe", new(1, 0), new InheritedUnsafeProvider()).Success, "Inherited pointer contract bypassed validation.");
    Check(!view.Publish<IOpaque>("opaque", new(1, 0), new OpaqueProvider()).Success, "Opaque return contract accepted.");
    Check(!view.Publish<ICallback>("callback", new(1, 0), new CallbackProvider()).Success, "Delegate contract accepted.");
}

static void MessageChannels()
{
    using var f = new Fixture(); using var publisher = f.Context("publisher"); using var a = f.Context("a"); using var b = f.Context("b");
    var channel = new MessageChannel<ServiceData>("notice", new(1, 0)); var order = new List<string>();
    Need(f.Registry.ForPlugin(a).SubscribeMessage(channel, data => { order.Add("a:" + data.Fields!["value"]); ((IDictionary<string, string>)data.Fields)["value"] = "edited"; }, 5));
    Need(f.Registry.ForPlugin(b).SubscribeMessage(channel, data => order.Add("b:" + data.Fields!["value"])));
    var data = new ServiceData("x", new Dictionary<string, string> { ["value"] = "original" });
    Check(f.Registry.ForPlugin(publisher).PublishMessage(channel, data).Success && order.SequenceEqual(new[] { "a:original", "b:original" }), "Messages not ordered or snapshot-isolated.");
    Check(!f.Registry.ForPlugin(b).SubscribeMessage(new MessageChannel<ServiceData>("notice", new(2, 0)), _ => { }).Success, "Message version mismatch accepted.");
    a.Dispose(); order.Clear(); f.Registry.ForPlugin(publisher).PublishMessage(channel, data);
    Check(order.SequenceEqual(new[] { "b:original" }), "Stopped message subscription leaked.");
}

static void MessageFaults()
{
    using var f = new Fixture(); using var publisher = f.Context("publisher"); using var broken = f.Context("broken"); using var healthy = f.Context("healthy");
    var channel = new MessageChannel<string>("message", new(1, 0)); var view = f.Registry.ForPlugin(publisher); var calls = 0; var recursionBlocked = false;
    Need(f.Registry.ForPlugin(broken).SubscribeMessage(channel, _ => throw new Exception("subscriber failed"), 10));
    Need(f.Registry.ForPlugin(healthy).SubscribeMessage(channel, _ => { calls++; recursionBlocked = !view.PublishMessage(channel, "recursive").Success; }));
    Check(view.PublishMessage(channel, "hello").Success && calls == 1 && recursionBlocked && f.Faults.SequenceEqual(new[] { "broken" }), "Message fault or recursion handling failed.");
}

static void WatchFault()
{
    using var f = new Fixture(); using var observer = f.Context("observer"); using var provider = f.Context("provider");
    f.Registry.ForPlugin(observer).Watch(new("echo", new(1, 0)), _ => throw new ObjectDisposedException("plugin callback bug"));
    Need(f.Registry.ForPlugin(provider).Publish<IPluginDataService>("echo", new(1, 0), new Provider()));
    Check(f.Faults.SequenceEqual(new[] { "observer" }), "Observer exception was swallowed or attributed to publisher.");
}

static void RegistryDisposal()
{
    using var f = new Fixture(); using var provider = f.Context("provider"); using var consumer = f.Context("consumer");
    Need(f.Registry.ForPlugin(provider).Publish<IPluginDataService>("echo", new(1, 0), new Provider()));
    var proxy = Proxy<IPluginDataService>(f.Registry.ForPlugin(consumer)); f.Registry.Dispose();
    Throws(() => proxy.Invoke(new("stale")), "ServiceUnavailable");
}

static void HostIntegration()
{
    using var f = new Fixture();
    var capabilities = new CapabilityRegistry(new("test", "test", "test", "test"));
    var events = new EventHub(f.Log);
    using var host = new PluginHost(capabilities, f.Main, events, f.Log);
    f.Registry.ProviderFaulted += (id, error) => Check(host.ReportPluginFault(id, error).Success, "Host rejected attributed active provider fault.");
    capabilities.RegisterScoped<IPluginServices>(InterPluginCapabilityIds.Services, context => f.Registry.ForPlugin(context));
    var caught = 0; var providerStopped = 0; IServiceRegistration? registration = null;
    host.Register(new HostPlugin("provider", context =>
    {
        var view = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services);
        Check(view.Available, view.Error?.Message ?? "Scoped broker unavailable.");
        registration = Need(view.Service!.Publish<IPluginDataService>("echo", new(1, 0), new Provider(_ => throw new Exception("formal-provider-fault"))));
    }, () => providerStopped++));
    host.Register(new HostPlugin("consumer", context =>
    {
        var view = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!;
        var proxy = Proxy<IPluginDataService>(view);
        context.Events.Subscribe<FrameUpdate>(_ =>
        {
            try { proxy.Invoke(new("fault")); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("formal-provider-fault")) { caught++; }
        });
    }));
    Check(host.Enable("provider").Success && host.Enable("consumer").Success, "Formal host plugins failed to activate.");
    events.Publish(new FrameUpdate(1));
    Check(caught == 1 && providerStopped == 1 && host.Statuses.Single(x => x.Id == "provider").State == PluginState.Faulted
        && host.Statuses.Single(x => x.Id == "consumer").State == PluginState.Enabled, "Provider fault stopped catching consumer or failed lifecycle cleanup.");
    registration!.Dispose();
    Check(!host.ReportPluginFault("missing", new Exception("unknown")).Success, "Unknown provider fault accepted.");
    host.Dispose();
    Check(!host.ReportPluginFault("consumer", new Exception("disposed")).Success, "Disposed host accepted external fault.");
}

static void WatchCallbackProviderFault()
{
    using var f = new Fixture();
    using var host = ServiceHost(f);
    var query = new ServiceQuery("echo", new(1, 0));
    var changes = new List<ServiceAvailability>();
    var replies = new List<string?>();
    var providerActivations = 0;
    var consumerActivations = 0;
    host.Register(new HostPlugin("provider", context =>
    {
        var firstActivation = ++providerActivations == 1;
        var services = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!;
        Need(services.Publish<IPluginDataService>(query.Id, new(1, 0), new Provider(_ => firstActivation
            ? throw new InvalidOperationException("provider failed in the readiness callback")
            : OperationResult<ServiceData>.Ok(new("replacement")))));
    }));
    host.Register(new HostPlugin("consumer", context =>
    {
        consumerActivations++;
        var services = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!;
        services.Watch(query, state =>
        {
            changes.Add(state);
            if (state.Available) replies.Add(Proxy<IPluginDataService>(services).Invoke(new("read")).Value?.Value);
        });
    }, optionalDependency: new("provider", new(1, 0))));

    Check(host.Enable("consumer").Success, "Soft consumer could not start before the provider.");
    Check(!host.Enable("provider").Success && host.Statuses.Single(x => x.Id == "provider").State == PluginState.Faulted,
        "The provider did not fault when the readiness observer invoked it.");
    Check(host.Statuses.Single(x => x.Id == "consumer").State == PluginState.Enabled && f.Faults.SequenceEqual(new[] { "provider" }),
        $"The readiness observer was faulted by the provider's exception. Reported owners: {string.Join(", ", f.Faults)}.");

    Check(host.Enable("provider").Success, "The provider could not restart.");
    Check(consumerActivations == 1 && changes.Select(x => x.Available).SequenceEqual(new[] { false, true, false, true })
        && changes[3].Service!.Generation > changes[1].Service!.Generation && replies.SequenceEqual(new[] { "replacement" }),
        "The original readiness subscription did not receive and invoke the replacement generation.");
}

static void MessageCallbackProviderFault()
{
    using var f = new Fixture();
    using var host = ServiceHost(f);
    var query = new ServiceQuery("echo", new(1, 0));
    var channel = new MessageChannel<string>("notice", new(1, 0));
    var attempts = new List<long>();
    var replies = new List<string?>();
    var delivered = new List<string>();
    var providerActivations = 0;
    var consumerActivations = 0;
    IPluginServices? publisher = null;
    host.Register(new HostPlugin("provider", context =>
    {
        var firstActivation = ++providerActivations == 1;
        var services = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!;
        Need(services.Publish<IPluginDataService>(query.Id, new(1, 0), new Provider(request => firstActivation
            ? throw new InvalidOperationException("provider failed in the message callback")
            : OperationResult<ServiceData>.Ok(new(request.Operation)))));
    }));
    host.Register(new HostPlugin("consumer", context =>
    {
        consumerActivations++;
        var services = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!;
        Need(services.SubscribeMessage(channel, message =>
        {
            var resolved = services.Lookup<IPluginDataService>(query);
            attempts.Add(resolved.Descriptor!.Generation);
            replies.Add(resolved.Service!.Invoke(new(message)).Value?.Value);
        }, priority: 10));
    }, optionalDependency: new("provider", new(1, 0))));
    host.Register(new HostPlugin("publisher", context =>
    {
        publisher = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!;
        Need(publisher.SubscribeMessage(channel, delivered.Add));
    }));

    Check(host.Enable("consumer").Success && host.Enable("provider").Success && host.Enable("publisher").Success,
        "The message participants could not enable.");
    Check(publisher!.PublishMessage(channel, "first").Success && delivered.SequenceEqual(new[] { "first" })
        && host.Statuses.Single(x => x.Id == "provider").State == PluginState.Faulted,
        "The failing provider was not stopped or later message delivery was interrupted.");
    Check(host.Statuses.Single(x => x.Id == "consumer").State == PluginState.Enabled && f.Faults.SequenceEqual(new[] { "provider" }),
        $"The message subscriber was faulted by the provider's exception. Reported owners: {string.Join(", ", f.Faults)}.");

    Check(host.Enable("provider").Success && publisher.PublishMessage(channel, "second").Success, "The provider could not restart or receive the second call.");
    Check(consumerActivations == 1 && attempts.Count == 2 && attempts[1] > attempts[0] && replies.SequenceEqual(new[] { "second" })
        && delivered.SequenceEqual(new[] { "first", "second" }), "The original message subscription did not invoke the replacement generation.");
}

static void IndependentCallbackFaults()
{
    using var f = new Fixture();
    using var host = ServiceHost(f);
    var query = new ServiceQuery("echo", new(1, 0));
    var channel = new MessageChannel<string>("notice", new(1, 0));
    IPluginServices? publisher = null;
    host.Register(new HostPlugin("publisher", context => publisher = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!));
    host.Register(new HostPlugin("watcher", context =>
    {
        var services = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!;
        services.Watch(query, state =>
        {
            if (state.Available) throw new InvalidOperationException("readiness observer failed independently");
        });
    }));
    host.Register(new HostPlugin("subscriber", context =>
    {
        var services = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services).Service!;
        Need(services.SubscribeMessage(channel, _ => throw new InvalidOperationException("message subscriber failed independently")));
    }));

    Check(host.Enable("publisher").Success && host.Enable("watcher").Success && host.Enable("subscriber").Success,
        "The callback participants could not enable.");
    Need(publisher!.Publish<IPluginDataService>(query.Id, new(1, 0), new Provider()));
    Check(publisher.PublishMessage(channel, "notify").Success, "Message delivery failed when an observer threw.");
    Check(f.Faults.SequenceEqual(new[] { "watcher", "subscriber" }) && host.Statuses.Single(x => x.Id == "watcher").State == PluginState.Faulted
        && host.Statuses.Single(x => x.Id == "subscriber").State == PluginState.Faulted
        && host.Statuses.Single(x => x.Id == "publisher").State == PluginState.Enabled,
        "Independent callback failures did not stop their own plugins while preserving the publisher.");
}

public interface IAsyncEcho { Task<ServiceData> Echo(ServiceRequest request); }
public sealed class PrivateProvider : IAsyncEcho { public Task<ServiceData> Echo(ServiceRequest request) => Task.FromResult(new ServiceData(request.Operation)); }
public interface IUnsafe { IntPtr Get(); }
public interface IInheritedUnsafe : IUnsafe { }
public interface IOpaque { object Get(); }
public interface ICallback { void Run(Action callback); }
sealed class UnsafeProvider : IUnsafe { public IntPtr Get() => IntPtr.Zero; }
sealed class InheritedUnsafeProvider : IInheritedUnsafe { public IntPtr Get() => IntPtr.Zero; }
sealed class OpaqueProvider : IOpaque { public object Get() => new(); }
sealed class CallbackProvider : ICallback { public void Run(Action callback) => callback(); }
sealed class AsyncEcho : IAsyncEcho
{
    private readonly Func<ServiceRequest, Task<ServiceData>> _call;
    public AsyncEcho(Func<ServiceRequest, Task<ServiceData>>? call = null) => _call = call ?? (request => Task.FromResult(new ServiceData(request.Operation)));
    public Task<ServiceData> Echo(ServiceRequest request) => _call(request);
}
sealed class Provider : IPluginDataService
{
    private readonly Func<ServiceRequest, OperationResult<ServiceData>> _call;
    public Provider(Func<ServiceRequest, OperationResult<ServiceData>>? call = null) => _call = call ?? (_ => OperationResult<ServiceData>.Ok(new("ok")));
    public OperationResult<ServiceData> Invoke(ServiceRequest request) => _call(request);
}
sealed class HostPlugin : IRizPlugin
{
    private readonly Action<IPluginContext> _enable; private readonly Action _disable;
    public HostPlugin(string id, Action<IPluginContext> enable, Action? disable = null, PluginDependency? optionalDependency = null)
    {
        Manifest = new(id, new(1, 0), new(1, 0)) { OptionalDependencies = optionalDependency is null ? null : new[] { optionalDependency } };
        _enable = enable;
        _disable = disable ?? (() => { });
    }
    public PluginManifest Manifest { get; }
    public void Enable(IPluginContext context) => _enable(context);
    public void Disable() => _disable();
}
sealed class Fixture : IDisposable
{
    public MainThreadDispatcher Main; public Logger Log = new(); public InterPluginServiceRegistry Registry; public List<string> Faults = new();
    public Fixture(ServiceCallLimits? limits = null, int maxPending = 256)
    { Main = new(maxPending); Registry = new(Main, Log, limits); Registry.ProviderFaulted += (owner, ex) => { Main.VerifyAccess(); Faults.Add(owner); }; }
    public void PumpHost() { Main.Pump(32); Registry.DrainProviderFaults(); }
    public Context Context(string id) => new(id, Main);
    public void Dispose() { Registry.Dispose(); Main.Dispose(); }
}
sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
sealed class Context : IScopedPluginContext, IMainThreadDispatcher, IDisposable
{
    private readonly MainThreadDispatcher _main; private bool _active = true; private readonly CancellationTokenSource _stop = new();
    public List<IDisposable> Resources = new(); public string PluginId { get; }
    public Context(string id, MainThreadDispatcher main) { PluginId = id; _main = main; }
    public CancellationToken Lifetime => _stop.Token;
    public IPluginScope CreateScope(string name, ScopeLifetime lifetime = ScopeLifetime.Manual) => throw new NotSupportedException();
    public ILogger Log { get; } = new Logger(); public IMainThreadDispatcher MainThread => this;
    public IEventBus Events => throw new NotSupportedException(); public GameBuildDescriptor GameBuild => new("test", "test", "test", "test");
    public CapabilityDescriptor GetCapability(string id) => throw new NotSupportedException();
    public ServiceLookup<T> GetService<T>(string id) where T : class => throw new NotSupportedException();
    public T Own<T>(T resource) where T : IDisposable { VerifyAccess(); Resources.Add(resource); return resource; }
    public bool IsMainThread => _main.IsMainThread;
    public void VerifyAccess() { _main.VerifyAccess(); if (!_active) throw new ObjectDisposedException(PluginId); }
    public Task InvokeAsync(Action action, CancellationToken token = default) => _main.InvokeAsync(() => { VerifyAccess(); action(); }, token);
    public void Dispose() { _main.VerifyAccess(); if (!_active) return; _active = false; _stop.Cancel(); foreach (var item in Resources.AsEnumerable().Reverse()) item.Dispose(); }
}
