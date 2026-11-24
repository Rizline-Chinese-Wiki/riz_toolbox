namespace RizToolbox.Sdk;

public static class GameAppearanceCapabilityIds
{
    public const string Appearance = "riz.game.appearance";
}

/// <summary>Concrete game component properties. These are independent of tool UI, Image and TMP targets.</summary>
public static class GameAppearanceRoles
{
    public const string NoteRendererColor = "game.note.renderer.color";
    public const string TrackStartColor = "game.track.segment.start-color";
    public const string TrackEndColor = "game.track.segment.end-color";
    /// <summary>Active group judge ring. Its game cache and actual SpriteRenderer color must agree.</summary>
    public const string TrackRingColor = "game.track.group.ring-color";
}

public sealed record GameAppearanceProperty(string Role, string Property, string Source, string Evidence,
    ExternalWritePolicy ExternalWrites, bool Experimental = true);

/// <summary>A single current game object/property. Target.Generation and RunId must be reacquired after rebuilding a run.</summary>
public sealed record GameAppearanceTarget(TargetDescriptor Target, string RunId, long SceneGeneration,
    GameAppearanceProperty Property);

/// <summary>All query fields must match a unique target. RunId prevents an intent for an old run from writing a replacement.</summary>
public sealed record GameAppearanceColorOverrideRequest(TargetQuery Query, string RunId, ResourceColor Color,
    int Priority = 0, long? SceneGeneration = null);

/// <summary>Experimental, build-specific game appearance. Leases belong to the current plugin scope and never follow new objects.</summary>
public interface IGameAppearanceService
{
    Result<IReadOnlyList<GameAppearanceProperty>> GetSupportedProperties();
    Result<IReadOnlyList<GameAppearanceTarget>> GetTargets(TargetQuery? query = null);
    Result<IMutationLease<ResourceColor>> OverrideColor(GameAppearanceColorOverrideRequest request);
}
