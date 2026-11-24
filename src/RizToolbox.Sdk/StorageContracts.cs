using System.Text.Json;

namespace RizToolbox.Sdk;

public static class StorageCapabilityIds
{
    public const string Storage = "riz.plugin-storage";
    public const string Localization = "riz.localization";
}

public enum PluginStorageArea { Configuration, Data, Cache }
public enum PluginStorageChangeKind { Saved, Deleted, Migrated }
public sealed record PluginStorageQuota(long UsedBytes, long LimitBytes, int MaximumFileBytes);
public sealed record PluginStorageEntry(PluginStorageArea Area, string Name, long Bytes);
public sealed record PluginStorageChanged(string PluginId, PluginStorageArea Area, string Name,
    PluginStorageChangeKind Kind, long Revision);
public sealed record ConfigurationMigration(int FromVersion, int ToVersion, Func<JsonElement, JsonElement> Transform);
public sealed record ConfigurationSnapshot<T>(int SchemaVersion, T Value, bool Migrated = false);

/// <summary>Activation-owned storage for this plugin only. All calls are synchronous on the verified main thread.
/// Configuration migrations are pure callbacks; their failures preserve the original. Quotas count committed files, including migration backups.
/// Data/cache methods accept relative logical names, never game save paths; there is no arbitrary game setting/save mutation API.</summary>
public interface IPluginStorageService
{
    Result<ConfigurationSnapshot<T>> LoadConfiguration<T>(string name, int schemaVersion, Func<T> defaults,
        IReadOnlyList<ConfigurationMigration>? migrations = null, Func<T, string?>? validate = null);
    Result SaveConfiguration<T>(string name, int schemaVersion, T value, Func<T, string?>? validate = null);
    Result<byte[]> Read(PluginStorageArea area, string name);
    Result Write(PluginStorageArea area, string name, byte[] data);
    Result Delete(PluginStorageArea area, string name);
    Result<IReadOnlyList<PluginStorageEntry>> List(PluginStorageArea area);
    Result<PluginStorageQuota> GetQuota();
    /// <summary>Subscription automatically expires with the activation. Only changes to this plugin's namespace are delivered.</summary>
    Result<IDisposable> Subscribe(Action<PluginStorageChanged> handler);
}

public sealed record LocalizationState(string Language, string? FallbackLanguage, long Revision);
public sealed record PluginLanguageChanged(string PluginId, string Language, long Revision);

/// <summary>Owned language tables use this plugin's local keys. Exact active language falls back to this plugin's designated fallback language.
/// All methods require the verified main thread; tables, subscriptions and cached service views expire with their activation.</summary>
public interface IPluginLocalizationService
{
    Result<IDisposable> Register(string language, IReadOnlyDictionary<string, string> messages, bool fallback = false);
    Result<string> Resolve(string localKey);
    Result<LocalizationState> GetState();
    Result<IDisposable> Subscribe(Action<PluginLanguageChanged> handler);
}
