using RizToolbox.Sdk;

namespace RizToolbox.Adapter;

/// <summary>Only accepted native edges enter here. Drain before sampled lifecycle polling on the next host update,
/// after the originating SDK command can record its successful dispatch against the still-open run.</summary>
internal sealed class VerifiedRunCancellationQueue
{
    private readonly GameEventState _state;
    private readonly Queue<(ObservedRunCancellationToken Token, string Evidence, RunEndReason Reason)> _pending = new();
    internal VerifiedRunCancellationQueue(GameEventState state) => _state = state;
    internal int Count => _pending.Count;
    internal bool Enqueue(ObservedRunCancellationToken token, string evidence, RunEndReason reason = RunEndReason.Aborted)
    {
        if (reason is not (RunEndReason.Aborted or RunEndReason.Skipped) || token is null ||
            string.IsNullOrWhiteSpace(evidence) || _state.CaptureCancellationToken(token.Information) != token) return false;
        foreach (var pending in _pending) if (pending.Token == token) return pending.Reason == reason;
        if (_pending.Count >= 8) return false;
        _pending.Enqueue((token, evidence, reason)); return true;
    }
    internal int Drain()
    {
        var applied = 0; var count = Math.Min(8, _pending.Count);
        for (var i = 0; i < count; i++)
        {
            var pending = _pending.Dequeue();
            if (_state.EndFromVerifiedCallback(pending.Token, pending.Reason, pending.Evidence)) applied++;
        }
        return applied;
    }
    internal void Clear() => _pending.Clear();
}
