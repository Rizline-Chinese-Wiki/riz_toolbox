namespace RizToolbox.Sdk;

public static class HookCapabilityIds { public const string Observe = "riz.hooks.observe"; }
public enum HookPhase { Before, After }
public sealed record HookPoint<T>(string Id, Version Version);
public sealed record HookTargetDescriptor(string Id, Version Version, Type PayloadType, string Signature,
    string BuildFingerprint, string Evidence, bool NativeInstalled = false);
public sealed record HookDiagnostics(string Id, long NativeCalls, long OriginalCalls, long BeforeDeliveries,
    long AfterDeliveries, long SuppressedReentries, long SuppressedWrongThread, int Subscribers);

/// <summary>Ordinary snapshots only. Arguments and original-call control cannot be changed by subscribers.</summary>
public interface IHookService
{
    IReadOnlyList<HookTargetDescriptor> GetTargets();
    IReadOnlyList<HookDiagnostics> GetDiagnostics();
    OperationResult<IDisposable> Subscribe<T>(HookPoint<T> point, HookPhase phase, Action<T> handler, int priority = 0);
    OperationResult RequestMutation(string targetId);
}

public sealed record JudgeHookObservation(long Invocation, int JudgeType, float DeltaTime);
public sealed record ControllerHookObservation(long Invocation);
public static class GameHookPoints
{
    public static HookPoint<JudgeHookObservation> Judge { get; } = new("game.normal.judge", new(1, 0));
    public static HookPoint<ControllerHookObservation> Pause { get; } = new("game.normal.pause", new(1, 0));
    public static HookPoint<ControllerHookObservation> Resume { get; } = new("game.normal.resume", new(1, 0));
}
