namespace RizToolbox.Sdk;

public enum CapabilityStability { Unknown = 0, Experimental = 1, Stable = 2 }
public enum OperationThread { Unknown = 0, Any = 1, MainThread = 2, Background = 3 }
public enum ObservationMode { Unknown = 0, Snapshot = 1, Sampled = 2, DirectCallback = 3, NotApplicable = 4 }
public enum CleanupResponsibility { Unknown = 0, None = 1, EnableScope = 2, Caller = 3, Process = 4 }

/// <summary>One operation's documented contract. Null sampling interval means no measured/declared interval.</summary>
public sealed record CapabilityOperationInfo
{
    public string Id { get; }
    public OperationThread Thread { get; }
    public ObservationMode Observation { get; }
    public double? SamplingIntervalSeconds { get; }
    public CleanupResponsibility Cleanup { get; }
    public bool ModifiesGameplay { get; }
    /// <summary>Empty means no scene has been declared, not universal support. Use "*" explicitly for scene-independent operations.</summary>
    public IReadOnlyList<string> SupportedScenes { get; }
    public string Description { get; }

    public CapabilityOperationInfo(string id, OperationThread thread, ObservationMode observation,
        CleanupResponsibility cleanup, bool modifiesGameplay, IEnumerable<string> supportedScenes,
        string description, double? samplingIntervalSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An operation ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("An operation description is required.", nameof(description));
        ArgumentNullException.ThrowIfNull(supportedScenes);
        if (samplingIntervalSeconds is not null && (!double.IsFinite(samplingIntervalSeconds.Value) || samplingIntervalSeconds <= 0 || observation != ObservationMode.Sampled))
            throw new ArgumentOutOfRangeException(nameof(samplingIntervalSeconds), "Only sampled observations may declare a positive finite interval.");
        var scenes = supportedScenes.ToArray();
        if (scenes.Any(string.IsNullOrWhiteSpace) || scenes.Distinct(StringComparer.Ordinal).Count() != scenes.Length)
            throw new ArgumentException("Scenes must be nonempty and unique.", nameof(supportedScenes));
        (Id, Thread, Observation, Cleanup, ModifiesGameplay, Description, SamplingIntervalSeconds) =
            (id, thread, observation, cleanup, modifiesGameplay, description, samplingIntervalSeconds);
        SupportedScenes = Array.AsReadOnly(scenes);
    }
}

/// <summary>Immutable availability snapshot. Generation identifies provider availability, not game-object lifetime.</summary>
public sealed record CapabilityInfo
{
    public string Id { get; }
    public Version Version { get; }
    public CapabilityStability Stability { get; }
    public bool Available { get; }
    public long Generation { get; }
    public string? UnavailableReason { get; }
    public IReadOnlyList<CapabilityOperationInfo> Operations { get; }

    public CapabilityInfo(string id, Version version, CapabilityStability stability, bool available,
        long generation, IEnumerable<CapabilityOperationInfo> operations, string? unavailableReason = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A capability ID is required.", nameof(id));
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(operations);
        if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
        if (!available && string.IsNullOrWhiteSpace(unavailableReason)) throw new ArgumentException("An unavailable capability needs a reason.", nameof(unavailableReason));
        var copied = operations.ToArray();
        if (copied.Any(x => x is null) || copied.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != copied.Length)
            throw new ArgumentException("Operation IDs must be unique and entries nonnull.", nameof(operations));
        (Id, Version, Stability, Available, Generation, UnavailableReason) =
            (id, version, stability, available, generation, available ? null : unavailableReason);
        Operations = Array.AsReadOnly(copied);
    }

}

/// <summary>Optional query contract. A capability being declared here does not grant or activate its service.</summary>
public interface ICapabilityMetadataProvider
{
    IReadOnlyList<CapabilityInfo> GetAll();
    Result<CapabilityInfo> Describe(string capabilityId);
}

/// <summary>A deterministic immutable catalog for a host/adapter availability snapshot; safe to read from any thread.</summary>
public sealed class CapabilityMetadataCatalog : ICapabilityMetadataProvider
{
    private readonly IReadOnlyList<CapabilityInfo> _items;
    private readonly IReadOnlyDictionary<string, CapabilityInfo> _byId;

    public CapabilityMetadataCatalog(IEnumerable<CapabilityInfo> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var byId = new Dictionary<string, CapabilityInfo>(StringComparer.Ordinal);
        foreach (var item in capabilities)
        {
            if (item is null || !byId.TryAdd(item.Id, item)) throw new ArgumentException("Capability IDs must be unique and entries nonnull.", nameof(capabilities));
        }
        _byId = byId;
        _items = Array.AsReadOnly(byId.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<CapabilityInfo> GetAll() => _items;

    public Result<CapabilityInfo> Describe(string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(capabilityId)) return Result<CapabilityInfo>.Fail(new(ErrorCode.InvalidArgument, "A capability ID is required."));
        return _byId.TryGetValue(capabilityId, out var value) ? Result<CapabilityInfo>.Ok(value) :
            Result<CapabilityInfo>.Fail(new(ErrorCode.CapabilityUnavailable, "This adapter has not declared capability metadata.", CapabilityId: capabilityId));
    }
}
