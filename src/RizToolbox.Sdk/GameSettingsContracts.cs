namespace RizToolbox.Sdk;

public static class GameSettingsCapabilityIds
{
    public const string Read = "riz.settings.read";
    public const string Changes = "riz.settings.changes";
    public const string Override = "riz.settings.override";
}

/// <summary>Only adapter-declared float settings. Unit, Minimum, Maximum and WholeNumbers describe
/// values read from and passed to the actual setter. Display slider units or integer display steps
/// need not match these units or constrain a continuous setter. No universal normalization is assumed.</summary>
public sealed record GameFloatSettingDescriptor(string Id, string Source, string Unit, bool ReadAvailable,
    bool OverrideAvailable, float? Minimum = null, float? Maximum = null, bool WholeNumbers = false,
    string? OverrideUnavailableReason = null);
public sealed record GameFloatSettingValue(GameFloatSettingDescriptor Descriptor, float Value, DateTimeOffset ObservedAtUtc);
public sealed record GameSettingOverrideRequest(string Id, float Value, int Priority = 0);

/// <summary>A complete copied observation of the verified settings. Revision changes only when values or
/// descriptors change; SessionId distinguishes a replacement settings provider. This contains no native handles.</summary>
public sealed class GameSettingsSnapshot
{
    public string SessionId { get; }
    public long Revision { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public IReadOnlyList<GameFloatSettingValue> Values { get; }
    public GameSettingsSnapshot(string sessionId, long revision, DateTimeOffset observedAtUtc, IEnumerable<GameFloatSettingValue> values)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("A provider session is required.", nameof(sessionId));
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(values);
        var copied = values.ToArray();
        if (copied.Any(value => value is null)) throw new ArgumentException("Setting values cannot contain null.", nameof(values));
        (SessionId, Revision, ObservedAtUtc, Values) = (sessionId, revision, observedAtUtc, Array.AsReadOnly(copied));
    }
}

/// <summary>Initial delivery contains the complete current snapshot and an empty ChangedIds list.
/// Later deliveries identify changed values/descriptors, including removed IDs, against the preceding revision.</summary>
public sealed class GameSettingsChanged
{
    public GameSettingsSnapshot Snapshot { get; }
    public bool IsInitial { get; }
    public IReadOnlyList<string> ChangedIds { get; }
    public GameSettingsChanged(GameSettingsSnapshot snapshot, bool isInitial, IEnumerable<string> changedIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(changedIds);
        Snapshot = snapshot; IsInitial = isInitial; ChangedIds = Array.AsReadOnly(changedIds.ToArray());
    }
}

/// <summary>Optional, separately discoverable settings observation capability; existing read implementations stay compatible.
/// Calls and callbacks require the verified main thread. Subscribe synchronously delivers exactly one initial snapshot,
/// followed by changes sampled by the settings module (one-second interval), not every intermediate setter call.
/// A failed capture publishes no partial revision. Provider replacement and activation expiry invalidate old views/subscriptions.</summary>
public interface IGameSettingsChangeService
{
    Result<GameSettingsSnapshot> GetSnapshot();
    /// <summary>Activation-owned subscription. Handler failures are attributed to the subscribing plugin.
    /// Disposing the returned handle stops delivery; registering during a callback receives the current revision once.</summary>
    Result<IDisposable> Subscribe(Action<GameSettingsChanged> handler);
}

/// <summary>Read-only, main-thread game settings. An unavailable settings page does not prevent verified static getter reads.</summary>
public interface IGameSettingsReadService
{
    Result<IReadOnlyList<GameFloatSettingDescriptor>> Describe();
    Result<IReadOnlyList<GameFloatSettingValue>> GetSettings();
    Result<GameFloatSettingValue> Read(string id);
}

/// <summary>Main-thread, activation-owned reversible setting overrides. Only declared setters and actual game UI ranges are accepted.
/// The game may persist values through its own setter implementation; this service does not read or write arbitrary save/config files.</summary>
public interface IGameSettingsOverrideService
{
    Result<IGameSettingLease> Override(GameSettingOverrideRequest request);
    Result<MutationSnapshot<float>> Describe(string id);
}

public interface IGameSettingLease : IDisposable
{
    string SettingId { get; }
    string OwnerId { get; }
    bool IsActive { get; }
    Result SetValue(float value);
}
