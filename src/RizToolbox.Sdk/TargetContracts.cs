namespace RizToolbox.Sdk;

public static class TargetCapabilityIds
{
    public const string Targets = "riz.targets";
    public const string Mutations = "riz.mutations";
}

/// <summary>A semantic canonical key and a process-local, non-reused binding generation. No native address is exposed.</summary>
public sealed record TargetHandle<T>(string Key, long Generation);
public sealed record TargetIdentity(string Key, long Generation);

public sealed record TargetMetadata(string? Scene = null, string? Role = null, string? ContentId = null,
    string? ResourceId = null, bool Active = true, bool Template = false, string? Description = null);

/// <summary>All supplied fields must match. Names are exact, ordinal, case-sensitive semantic identifiers.</summary>
public sealed record TargetQuery(string? Key = null, string? Scene = null, string? Role = null,
    string? ContentId = null, string? ResourceId = null, bool IncludeTemplates = false, bool ActiveOnly = true);

public sealed record TargetDescriptor(string Key, long Generation, Type ValueType, TargetMetadata Metadata,
    IReadOnlyList<string> Aliases);

public enum TargetMatchKind { Unique, Missing, Ambiguous }
public sealed record TargetMatch<T>(TargetMatchKind Kind, TargetHandle<T>? Target,
    IReadOnlyList<TargetDescriptor> Candidates, OperationError? Error = null);

/// <summary>Read-only values must be ordinary immutable snapshots. This lease never follows a replacement generation.</summary>
public interface ITargetValueLease<T> : IDisposable
{
    TargetHandle<T> Target { get; }
    bool IsValid { get; }
    OperationResult<T> Read();
}

public interface ITargetService
{
    TargetMatch<T> Resolve<T>(TargetQuery query);
    IReadOnlyList<TargetDescriptor> GetTargets();
    /// <summary>The current plugin scope owns the returned lease automatically.</summary>
    OperationResult<ITargetValueLease<T>> Acquire<T>(TargetHandle<T> target);
}
