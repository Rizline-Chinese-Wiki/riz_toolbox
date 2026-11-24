using System.Text.Json;
using System.Text.RegularExpressions;
using RizToolbox.Sdk;

namespace RizToolbox.Core;

public sealed record PluginActivationIntent(string Id, bool DesiredEnabled, bool ExplicitlyDisabled = false);

/// <summary>Persists explicit choices independently of current capability availability and runtime plugin faults.</summary>
public sealed class PluginActivationStore
{
    private readonly AtomicSchemaStore _store;
    public PluginActivationStore(string path) => _store = new(path);

    public Result<IReadOnlyList<PluginActivationIntent>> Load()
    {
        var loaded = _store.Load(1, () => Array.Empty<PluginActivationIntent>(),
            new[] { new JsonSchemaMigration(0, 1, UpgradeLegacy) }, Validate);
        return loaded.Success ? Result<IReadOnlyList<PluginActivationIntent>>.Ok(Array.AsReadOnly(loaded.Value!.Value)) :
            Result<IReadOnlyList<PluginActivationIntent>>.Fail(loaded.Error!);
    }

    public Result Save(IEnumerable<PluginActivationIntent> intents)
    {
        try
        {
            var snapshot = intents.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
            return _store.Save(1, snapshot, Validate);
        }
        catch (Exception ex) { return Result.Fail(new(ErrorCode.ConfigInvalid, "Invalid activation choices: " + ex.Message)); }
    }

    /// <summary>Load once after registration, then persist subsequent explicit changes. Does not call EnableAll.</summary>
    public Result<IDisposable> Bind(PluginHost host, bool enableUnspecified = false, Action<OperationError>? onSaveFailure = null)
    {
        var loaded = Load();
        if (!loaded.Success) return Result<IDisposable>.Fail(loaded.Error!);
        var restored = host.RestoreActivationIntents(loaded.Value!, enableUnspecified);
        if (!restored.Success) return Result<IDisposable>.Fail(restored.Error!);
        var binding = new Binding(this, host, onSaveFailure);
        return Result<IDisposable>.Ok(binding);
    }

    internal static string? Validate(IReadOnlyList<PluginActivationIntent> intents)
    {
        if (intents is null || intents.Count > 10000) return "Invalid activation intent count.";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in intents)
        {
            if (item is null || item.Id is null || item.Id.EndsWith('.') || !Regex.IsMatch(item.Id, "^[a-z0-9][a-z0-9._-]{0,127}$") || !seen.Add(item.Id)) return "Invalid or duplicate plugin ID in activation choices.";
            if (item.DesiredEnabled && item.ExplicitlyDisabled) return "Activation intent cannot be simultaneously enabled and explicitly disabled.";
        }
        return null;
    }

    private static JsonElement UpgradeLegacy(JsonElement legacy)
    {
        if (!legacy.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.Object)
            throw new JsonException("Legacy activation schema requires an enabled object.");
        var intents = enabled.EnumerateObject().Select(x => new PluginActivationIntent(x.Name, x.Value.GetBoolean(), !x.Value.GetBoolean())).ToArray();
        return JsonSerializer.SerializeToElement(intents, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private sealed class Binding : IDisposable
    {
        private readonly PluginActivationStore _store; private PluginHost? _host; private readonly Action<OperationError>? _failure;
        public Binding(PluginActivationStore store, PluginHost host, Action<OperationError>? failure)
        { _store = store; _host = host; _failure = failure; host.ActivationIntentsChanged += Save; }
        private void Save()
        {
            if (_host is null) return;
            var saved = _store.Save(_host.CaptureActivationIntents());
            if (!saved.Success) _failure?.Invoke(saved.Error!);
        }
        public void Dispose()
        { var host = Interlocked.Exchange(ref _host, null); if (host is not null) host.ActivationIntentsChanged -= Save; }
    }
}
