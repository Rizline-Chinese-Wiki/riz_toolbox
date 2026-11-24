namespace RizToolbox.Sdk;

public static class InterPluginCapabilityIds { public const string Services = "riz.services"; }

public sealed record ServiceQuery(string Id, Version MinVersion, Version? MaxVersionExclusive = null);
public sealed record PluginServiceDescriptor(string Id, Version Version, string ProviderId, Type Contract, long Generation);
public sealed record ServiceAvailability(ServiceQuery Query, bool Available, PluginServiceDescriptor? Service, OperationError? Error = null);
public sealed record ServiceResolution<T>(bool Available, T? Service, PluginServiceDescriptor? Descriptor, OperationError? Error = null) where T : class;
public sealed record ServiceCallLimits(int MaximumPayloadBytes = 65536, int TimeoutMilliseconds = 1000, int MaximumCallDepth = 16);

public interface IServiceRegistration : IDisposable { PluginServiceDescriptor Descriptor { get; } }
public sealed record MessageChannel<T>(string Id, Version Version);

/// <summary>Service providers and optional consumers share exact host-approved contracts, never private-ALC lookalikes.</summary>
public interface IPluginServices
{
    /// <summary>Registers one provider for this ID. Registration is owned by this enable scope.</summary>
    OperationResult<IServiceRegistration> Publish<T>(string id, Version version, T provider) where T : class;
    ServiceResolution<T> Lookup<T>(ServiceQuery query) where T : class;
    /// <summary>Delivers an initial snapshot and subsequent availability changes on the main thread.</summary>
    IDisposable Watch(ServiceQuery query, Action<ServiceAvailability> changed);
    /// <summary>Immutable data messages run synchronously on the main thread. Higher priority then registration order.</summary>
    OperationResult<IDisposable> SubscribeMessage<T>(MessageChannel<T> channel, Action<T> handler, int priority = 0);
    OperationResult PublishMessage<T>(MessageChannel<T> channel, T message);
}

/// <summary>A general shared SDK contract suitable for simple optional integrations.</summary>
public interface IPluginDataService
{
    OperationResult<ServiceData> Invoke(ServiceRequest request);
}
public sealed record ServiceRequest(string Operation, IReadOnlyDictionary<string, string>? Arguments = null);
public sealed record ServiceData(string Value, IReadOnlyDictionary<string, string>? Fields = null);
