namespace RizToolbox.Sdk;

public enum VisualBindingState { Waiting, Bound, Ambiguous, Stopped }

/// <summary>The semantic values captured from the first unique match, including absent values.
/// Instance IDs, native addresses and binding generations are not part of this identity.</summary>
public sealed record VisualBindingIdentity(string? Scene, string? ContentId, string? Role, string? ResourceId);

/// <summary>An immutable observation. Bound means a live priority layer was acquired, not that it won arbitration.
/// Target contains the current non-reused generation; it is null outside Bound. Stopped is terminal.</summary>
public sealed record VisualBindingSnapshot(string IntentId, long Revision, VisualBindingState State,
    TargetQuery Query, VisualAsset Asset, int Priority, VisualBindingIdentity? Identity,
    TargetHandle<VisualAsset>? Target, int CandidateCount, OperationError? Error = null);

/// <summary>A scope-owned semantic override intent. It retains no independent asset lease.
/// Dispose the input asset lease, this intent, its scope or its provider to stop it permanently.</summary>
public interface IVisualBindingIntent : IDisposable
{
    string IntentId { get; }
    /// <summary>Last immutable managed state, readable after stop and on any thread. This never probes a backend.</summary>
    VisualBindingSnapshot LastSnapshot { get; }
    /// <summary>Main-thread, live-scope/provider read of the last state. Does not discover or rebind targets.</summary>
    OperationResult<VisualBindingSnapshot> GetSnapshot();
    /// <summary>Ends the current priority layer and permanently stops this intent. The input asset lease remains caller-owned.</summary>
    OperationResult Stop();
}
