using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeTweenSingleDispatchSnapshot(ulong ChildSequence, uint Site, int Index, long Invokable,
    long Target, long Method, uint ValueBits, uint? EffectiveSingleBits, bool CachedSingle, bool Returned,
    bool Abnormal, bool RootedBefore, IReadOnlyList<long> CreatedOccurrences, string Pending)
{
    internal long Delegate { get; init; }
    internal NativeTweenTypedEffectTransferSnapshot? Transfer { get; init; }
}
internal sealed record NativeTweenSingleListSnapshot(ulong ParentSequence, bool MidObserved, long List, int? Count,
    int? Version, long? BackingArray, bool ParentReturned, bool ParentDisposed, bool ParentAbnormal,
    IReadOnlyList<NativeTweenSingleDispatchSnapshot> Dispatches, string Pending);

/// <summary>Actual copied facts and causal work links, with no Ready/root/effects-completion issuer.
/// Production only feeds this after current native authority, independent roots and exact metadata checks.</summary>
internal sealed class NativeLocalTweenSingleEventTracker
{
    private readonly NativeOperationFrame _parent;
    private NativeSingleExecutingList? _list;
    private ulong _epoch, _parentEpoch, _token;
    internal ulong NativeToken => _token;
    private readonly Dictionary<ulong, Child> _children = new();
    private bool _returned, _disposed, _abnormal;
    private string? _failure;
    private sealed class Child
    {
        internal readonly NativeTweenSingleEventFrame Before;
        internal readonly NativeSingleDispatchTarget Target;
        internal readonly HashSet<long> Baseline;
        internal readonly List<long> Created = new();
        internal bool Returned, Abnormal;
        internal Child(NativeTweenSingleEventFrame before, NativeSingleDispatchTarget target, IEnumerable<long> baseline)
            => (Before, Target, Baseline) = (before, target, baseline.ToHashSet());
    }
    internal NativeLocalTweenSingleEventTracker(NativeOperationFrame parent)
    {
        if (!parent.IsValid || parent.Kind != NativeOperationKind.Single1) throw new InvalidOperationException("A Single tracker requires its actual operation parent.");
        _parent = parent;
    }
    private void RequireParent(NativeTweenSingleEventFrame frame)
    {
        if (_disposed || _returned || _abnormal || frame.ParentSequence != _parent.Sequence || frame.Event != _parent.Self
            || frame.ParentMethod != _parent.Method || frame.ParentCaller != _parent.Caller || frame.ValueBits != _parent.ScalarBits
            || frame.ParentManagedToken == 0 || _list is not null && (frame.Epoch != _epoch || frame.ParentEpoch != _parentEpoch || frame.ParentManagedToken != _token))
            throw new InvalidOperationException("Single list/dispatch crossed its exact open parent sequence/token/payload.");
    }
    internal void Mid(NativeTweenSingleEventFrame frame, NativeSingleExecutingList list)
    {
        RequireParent(frame);
        if (_list is not null || frame.Stage != NativeTweenSingleEventStage.Mid || frame.ChildSequence != 0
            || list.Event != _parent.Self.ToInt64() || list.List != frame.List.ToInt64() || list.Count < 0 || list.Count > list.Items.Count)
            throw new InvalidOperationException("Single mid is not one unique complete actual returned executing list.");
        (_list, _epoch, _parentEpoch, _token) = (list, frame.Epoch, frame.ParentEpoch, frame.ParentManagedToken);
    }
    private void RequireList(NativeTweenSingleEventFrame frame, NativeSingleExecutingList list)
    {
        RequireParent(frame);
        if (_list is null || list.Event != _list.Event || list.List != _list.List || list.Array != _list.Array
            || list.Count != _list.Count || list.Version != _list.Version || !list.Items.SequenceEqual(_list.Items)
            || frame.List.ToInt64() != list.List || frame.Index >= (ulong)list.Count || list.Items[(int)frame.Index] != frame.Invokable.ToInt64())
            throw new InvalidOperationException("Single listener occurrence does not match the retained actual list/vector/version/index.");
    }
    internal void Before(NativeTweenSingleEventFrame frame, NativeSingleExecutingList list, NativeSingleDispatchTarget target, IEnumerable<long> occurrences)
    {
        RequireList(frame, list);
        if (frame.Stage != NativeTweenSingleEventStage.Before || frame.ChildSequence == 0 || _children.ContainsKey(frame.ChildSequence)
            || _children.Values.Any(value => !value.Returned && !value.Abnormal) || frame.Index != (ulong)_children.Count
            || target.Site != frame.Site || target.Invokable != frame.Invokable.ToInt64() || target.Target != frame.ActualTarget.ToInt64()
            || target.OriginalValueBits != frame.ValueBits || frame.Site == 1 && target.EffectiveSingleBits is not null)
            throw new InvalidOperationException("Single CALL generation/ABI is duplicate, out of order or differs from its rooted target.");
        _children.Add(frame.ChildSequence, new(frame, target, occurrences));
    }
    internal void Complete(NativeTweenSingleEventFrame frame, IEnumerable<long> occurrences)
    {
        RequireParent(frame);
        if (!_children.TryGetValue(frame.ChildSequence, out var child))
        {
            // An actual target-read fault can precede Before delivery. It is unknown responsibility,
            // never an invented successful dispatch or a count=0 list.
            _failure ??= "Native abnormal/target-read completion had no successfully rooted managed Before.";
            return;
        }
        if (!NativeTweenSingleEventValidation.SameChild(child.Before, frame) || child.Returned || child.Abnormal)
            throw new InvalidOperationException("Single listener completion differs from its exact native child occurrence.");
        child.Returned = frame.Stage == NativeTweenSingleEventStage.Returned && frame.Flags == 0;
        child.Abnormal = !child.Returned;
        child.Created.AddRange(occurrences.Where(value => !child.Baseline.Contains(value)).Distinct());
        if (child.Abnormal) _failure ??= "Listener abnormal/observer fault/missing post retains all typed child effects and roots.";
    }
    internal void ParentComplete(bool returned)
    {
        if (_returned || _abnormal || _disposed) throw new InvalidOperationException("Single parent completed more than once.");
        _returned = returned; _abnormal = !returned;
        if (!returned || _list is null || _children.Values.Any(value => !value.Returned) || _children.Count != _list.Count)
            _failure ??= "Parent exit has no complete actual executing-list mid/listener-return coverage; retained responsibility remains pending.";
    }
    internal void ParentDispose()
    {
        if (_disposed || _returned == _abnormal) throw new InvalidOperationException("Single parent disposal has no unique native completion.");
        _disposed = true;
    }
    internal NativeTweenSingleListSnapshot Read()
    {
        var pending = _failure ?? (_list is null ? "No actual native executing-list mid was observed."
            : !_returned || !_disposed ? "The actual Single parent still owns in-flight responsibility."
            : _list.Count == 0 ? "Actual empty list observed; requires verified Pre/Mid/Post event graph, writer-generation stability, and final native drain receipt."
            : "Rooted listener CALLs are observed; opaque/setter/coroutine effects require their own end/transfer issuer.");
        return new(_parent.Sequence, _list is not null, _list?.List ?? 0, _list?.Count, _list?.Version, _list?.Array,
            _returned, _disposed, _abnormal, Array.AsReadOnly(_children.Values.Select(child => new NativeTweenSingleDispatchSnapshot(
                child.Before.ChildSequence, child.Before.Site, (int)child.Before.Index, child.Target.Invokable, child.Target.Target,
                child.Target.Method, child.Before.ValueBits, child.Target.EffectiveSingleBits, child.Target.CachedSingle, child.Returned,
                child.Abnormal, true, Array.AsReadOnly(child.Created.ToArray()), child.Created.Count != 0
                    ? "New source occurrences remain owned by their exact existing work ledger; typed return is no completion."
                    : "No certified listener effect end/transfer fact has been issued.")
            { Delegate = child.Target.Delegate }).ToArray()), pending);
    }
}
