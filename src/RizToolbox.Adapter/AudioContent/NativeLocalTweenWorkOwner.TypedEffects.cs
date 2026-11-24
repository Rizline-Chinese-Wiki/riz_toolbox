using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeTweenTypedEffectSnapshot(ulong Invocation, long Tween, long? Occurrence,
    bool Instant, bool Returned, bool Disposed, string Pending)
{
    internal NativeTweenSingleListSnapshot? Executing { get; init; }
}

internal sealed partial class NativeLocalTweenWorkOwner
{
    private sealed record TypedEffect(TypedEventInvocation Invocation, SourceOccurrence? Work);
    private readonly List<TypedEffect> _typedEffects = new();

    private void CaptureTypedEffect(TypedEventInvocation invocation)
    {
        // Poll the real issuer Before before choosing a label. Equal Tween
        // pointers in two slots never supply an occurrence association.
        PollSourceJournal(); SourceOccurrence? work = null;
        if (_sourceOperations.TryGetValue(invocation.Parent, out var source))
        {
            if (invocation.Parent.Operation == NativeTweenOperation.Play) work = source.Instant;
            else
            {
                var parent = SourceAncestors(invocation.Parent).FirstOrDefault();
                if (parent is not null && _sourceOperations.TryGetValue(parent, out var update) && update.PairOrder.Count != 0)
                {
                    var pair = update.Pairs[update.PairOrder[^1]];
                    if (IsExecuteSite(pair.Before.Site) && pair.Before.Tween.ToInt64() == invocation.Membership.Tween
                        && pair.Before.RelayReturnPc.ToInt64() + 5 == invocation.Parent.Frame.Caller.ToInt64()) work = pair.Occurrence;
                }
            }
        }
        _typedEffects.Add(new(invocation, work));
    }

    private string TypedEffectPending(TypedEffect effect) => effect.Work is null
        || effect.Invocation.Parent.Skipped || !effect.Invocation.Parent.NativeReturned
        || !effect.Invocation.Parent.Completed || !effect.Invocation.Parent.Disposed
        || !_sourceOccurrences.TryGetValue(effect.Work.Id, out var current)
        || !ReferenceEquals(current, effect.Work)
        ? "Typed invocation has no exact source occurrence/InstantWork issuer."
        : ReadTypedListenerEffects(effect.Invocation).Pending;

    internal IReadOnlyList<NativeTweenTypedEffectSnapshot> ReadTypedEffects()
    {
        _main.VerifyAccess(); return _typedEffects.Select(effect => new NativeTweenTypedEffectSnapshot(
            effect.Invocation.Frame.Sequence, effect.Invocation.Membership.Tween, effect.Work?.Id,
            effect.Work?.Instant == true, effect.Invocation.Returned, effect.Invocation.Disposed, TypedEffectPending(effect))
            { Executing = ReadTypedListenerEffects(effect.Invocation) }).ToArray();
    }
}
