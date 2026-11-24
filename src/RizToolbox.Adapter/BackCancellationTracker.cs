namespace RizToolbox.Adapter;

internal sealed record BackCancellationIdentity(ObservedRunCancellationToken Run, long Controller, long Pause);
internal sealed record BackCancellationUpdate(long Request, BackCancellationIdentity Identity);

/// <summary>One retained accepted Back request. Identity survives ordinary revisions; terminal evidence uses a fresh token.</summary>
internal sealed class BackCancellationTracker
{
    private readonly Func<long, ObservedRunCancellationToken?> _capture;
    private readonly Action<Exception>? _releaseFailure;
    private Pending? _pending;
    private long _serial;
    private sealed record Pending(long Serial, BackCancellationIdentity Identity, IDisposable Retention);
    internal BackCancellationTracker(Func<long, ObservedRunCancellationToken?> capture, Action<Exception>? releaseFailure = null)
        => (_capture, _releaseFailure) = (capture, releaseFailure);
    internal int Count => _pending is null ? 0 : 1;
    internal bool MatchesPause(long pause) => _pending?.Identity.Pause == pause;
    internal static bool SameRun(ObservedRunCancellationToken expected, ObservedRunCancellationToken? current)
        => current is not null && expected.SessionId == current.SessionId && expected.RunId == current.RunId
            && expected.SceneGeneration == current.SceneGeneration && expected.Information == current.Information;

    /// <summary>Always takes ownership, including rejected/duplicate requests.</summary>
    internal bool Accept(BackCancellationIdentity identity, IDisposable retention)
    {
        ArgumentNullException.ThrowIfNull(retention);
        var owned = false;
        try
        {
            Prune();
            var current = _capture(identity.Run.Information);
            if (_pending is not null || identity.Controller == 0 || identity.Pause == 0 || !SameRun(identity.Run, current)) return false;
            _pending = new(++_serial, identity with { Run = current! }, retention); owned = true; return true;
        }
        finally { if (!owned) Release(retention); }
    }
    internal BackCancellationUpdate? BeginUpdate(long controller, long pause, long information, bool terminated)
    {
        Prune(); var pending = _pending;
        if (pending is null || terminated || pending.Identity.Controller != controller || pending.Identity.Pause != pause
            || pending.Identity.Run.Information != information) return null;
        return new(pending.Serial, pending.Identity);
    }
    internal ObservedRunCancellationToken? CompleteUpdate(BackCancellationUpdate update, bool terminated, bool skipped)
    {
        Prune(); var pending = _pending;
        if (pending is null || update.Request != pending.Serial || update.Identity != pending.Identity || !terminated || !skipped) return null;
        var current = _capture(pending.Identity.Run.Information);
        if (!SameRun(pending.Identity.Run, current)) { Clear(); return null; }
        Clear(); return current;
    }
    internal void Prune()
    {
        if (_pending is { } pending && !SameRun(pending.Identity.Run, _capture(pending.Identity.Run.Information))) Clear();
    }
    internal void Clear()
    {
        var pending = _pending; _pending = null;
        if (pending is not null) Release(pending.Retention);
    }
    private void Release(IDisposable retention)
    {
        try { retention.Dispose(); }
        catch (Exception ex) { try { _releaseFailure?.Invoke(ex); } catch { } }
    }
}
