namespace RizToolbox.Sdk;

/// <summary>Observation only. Subscribers cannot change arguments, skip the target or replace its return value.</summary>
public enum ExtensionPointPhase { Before, After }

public sealed record ExtensionPoint<T>(string Id, Version Version);
public sealed record ExtensionPointDescriptor(
    string Id, Version Version, Type PayloadType, ExtensionPointPhase Phase, string? Description = null);

/// <summary>The adapter declares extension points; plugins may subscribe only to exact declared versions and payload types.</summary>
public interface IExtensionService
{
    IReadOnlyList<ExtensionPointDescriptor> GetAll();
    OperationResult<ExtensionPointDescriptor> Describe(string id);
    /// <summary>Higher priorities run first; equal priorities run in registration order. The enable scope owns this subscription.</summary>
    OperationResult<IDisposable> Subscribe<T>(ExtensionPoint<T> point, Action<T> handler, int priority = 0);
}

public interface IPluginConfiguration
{
    /// <summary>Loads a named JSON document, or returns defaults when the document does not exist. Invalid JSON is an error.</summary>
    OperationResult<T> Load<T>(string name, Func<T> defaults);
    /// <summary>Atomically replaces a named document. Names contain only ASCII letters, digits, hyphen and underscore.</summary>
    OperationResult Save<T>(string name, T value);
}
