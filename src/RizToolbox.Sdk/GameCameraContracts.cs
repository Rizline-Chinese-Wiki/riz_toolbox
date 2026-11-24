namespace RizToolbox.Sdk;

public static class GameCameraCapabilityIds
{
    public const string Camera = "riz.game.camera";
}

public static class GameCameraRoles
{
    public const string RigLocalPositionX = "game.camera.rig.local-position-x";
    public const string RigLocalScaleX = "game.camera.rig.local-scale-x";
}

/// <summary>A game-owned scalar. Null bounds mean no numeric game range has been established.</summary>
public sealed record GameCameraScalarProperty(string Role, string Property, string Unit, float? Minimum, float? Maximum,
    string RangeEvidence, string Source, string Evidence, ExternalWritePolicy ExternalWrites, bool Experimental = true);

/// <summary>A current game camera rig property; handles and run identity never follow replacements.</summary>
public sealed record GameCameraTarget(TargetDescriptor Target, string RunId, long SceneGeneration,
    GameCameraScalarProperty Property);

public sealed record GameCameraScalarOverrideRequest(TargetQuery Query, string RunId, float Value,
    int Priority = 0, long? SceneGeneration = null);

/// <summary>Experimental camera rig mutation. Position and scale can affect input projection and gameplay.</summary>
public interface IGameCameraService
{
    Result<IReadOnlyList<GameCameraScalarProperty>> GetSupportedProperties();
    Result<IReadOnlyList<GameCameraTarget>> GetTargets(TargetQuery? query = null);
    Result<IMutationLease<float>> OverrideScalar(GameCameraScalarOverrideRequest request);
}
