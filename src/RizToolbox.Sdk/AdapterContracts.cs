namespace RizToolbox.Sdk;

/// <summary>Operation-specific aliases; legacy capability IDs remain supported.</summary>
public static class AdapterOperationCapabilityIds
{
    public const string CatalogRead = "riz.catalog.read";
    public const string ScoresRead = "riz.scores.read";
    public const string UiCreate = "riz.ui.create";
    public const string ToolTextureOverride = "riz.resources.override";
    public const string GameResourceRead = "riz.game-resources.read";
    public const string GameResourceOverride = "riz.game-resources.override";
    public const string SceneRead = "riz.scenes.read";
    public const string SceneObserve = "riz.scenes.observe";
    public const string RunRead = "riz.runs.read";
    public const string RunObserve = "riz.runs.observe";
}

public enum AdapterModuleState { Waiting, Active, Unsupported, Suspended, Disposed, Dormant }
/// <summary>Evidence is scoped to this build/module. Runtime probes do not imply gameplay acceptance.</summary>
public enum AdapterVerificationTier { Declared, SymbolVerified, RuntimeProbed, GameplayVerified }
public enum AdapterSymbolKind { Type, Field, Method }
public sealed record AdapterSymbolRequirement(string Id, string Assembly, string Namespace, string Type,
    AdapterSymbolKind Kind = AdapterSymbolKind.Type, string? Member = null,
    bool IsStatic = false, IReadOnlyList<string>? ParameterTypes = null)
{
    /// <summary>Exact field type or method return type; no storage reads occur during this probe.</summary>
    public string? ExpectedType { get; init; }
}
public sealed record AdapterSymbolProbe(string Id, bool Available, string Signature, string? Failure);
public sealed record AdapterModuleManifest(string Id, Version Version, IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> RequiredCapabilities, IReadOnlyList<string> SupportedScenes,
    IReadOnlyList<AdapterSymbolRequirement> RequiredSymbols, string Evidence,
    AdapterVerificationTier VerifiedTier = AdapterVerificationTier.Declared);
public sealed record AdapterModuleStatus(AdapterModuleManifest Manifest, string ProfileId, string BuildFingerprint,
    AdapterModuleState State, AdapterVerificationTier VerificationTier, int ProbeAttempts,
    double? NextProbeSeconds, string? Failure, IReadOnlyList<AdapterSymbolProbe> Symbols,
    ErrorCode? FailureCode = null, int ConsecutiveFailures = 0);
/// <summary>Copied module diagnostics. An Active module can still have an experimental gameplay contract.</summary>
public interface IAdapterModuleStatusProvider
{
    IReadOnlyList<AdapterModuleStatus> GetModules();
}
