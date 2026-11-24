using RizToolbox.Sdk;

namespace RizToolbox.Adapter.UiInput;

internal sealed record InputActionEdge(InputBindingOwner Owner, long Epoch);
// Capture-time permission can only remove edges. Dispatch still checks current scope,
// scene and focus, so ending text editing/closing a modal cannot replay a blocked key.
internal sealed record InputCapturePolicy(InputActionContext Context, bool TextEditing, bool ApplicationFocused,
    IReadOnlyList<InputFocusInfo> Focus)
{
    internal bool Allows(InputBindingOwner owner)
    {
        if (!ApplicationFocused || TextEditing || Focus.Any(x => x.TextEditing)) return false;
        if (!owner.Context.HasFlag(InputActionContext.Global) && (owner.Context & Context) == 0) return false;
        var modal = Focus.LastOrDefault(x => x.Modal);
        if (modal is not null && owner.WindowId != modal.WindowId) return false;
        return owner.WindowId is null || Focus.LastOrDefault()?.WindowId == owner.WindowId;
    }
}
internal sealed record InputUpdateSample(long Invocation, int UpdateType, IReadOnlyList<InputActionEdge> Actions,
    UiPoint? Pointer, bool LeftDown, bool LeftUp)
{
    internal uint? UpdateCount { get; init; }
}

/// <summary>Main-thread bounded copies only. No callback, Unity object, native pointer or device state is retained.</summary>
internal sealed class InputUpdateEdgeCollector : IDisposable
{
    private readonly int _capacity, _edgeCapacity;
    private readonly Queue<InputUpdateSample> _updates = new();
    private readonly Dictionary<string, long> _epochs = new(StringComparer.Ordinal);
    private long _nextEpoch;
    private int _edges;
    private long _lastInvocation;
    private uint? _lastUpdateCount;
    private int _lastUpdateType;
    internal bool Retired { get; private set; }
    internal string? Failure { get; private set; }
    internal int Pending => _updates.Count;
    internal long Duplicates { get; private set; }
    internal long OverflowCount { get; private set; }
    internal InputUpdateEdgeCollector(int capacity = 256, int edgeCapacity = 4096)
    {
        if (capacity is < 1 or > 1024 || edgeCapacity is < 1 or > 16384) throw new ArgumentOutOfRangeException(nameof(capacity));
        (_capacity, _edgeCapacity) = (capacity, edgeCapacity);
    }
    internal bool Add(long invocation, int updateType, IReadOnlyList<InputBindingOwner> registered,
        IReadOnlyCollection<InputBinding> pressed, UiPoint? pointer, bool leftDown, bool leftUp, uint? updateCount = null,
        InputCapturePolicy? policy = null)
    {
        if (Retired) return false;
        if (invocation <= _lastInvocation) { Duplicates++; return false; }
        _lastInvocation = invocation;
        if (updateCount is not null && updateCount == _lastUpdateCount && updateType == _lastUpdateType) { Duplicates++; return false; }
        _lastUpdateCount = updateCount; _lastUpdateType = updateType;
        if (registered.Count > 512) { Retire("Input action registration snapshot exceeded its declared bound."); return false; }
        var currentIds = registered.Select(x => x.ActionId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _epochs.Keys.Where(x => !currentIds.Contains(x)).ToArray()) _epochs.Remove(id);
        foreach (var id in currentIds) if (!_epochs.ContainsKey(id)) _epochs.Add(id, ++_nextEpoch);
        var actions = registered.Where(x => pressed.Contains(x.Binding) && (policy?.Allows(x) ?? true))
            .Distinct().Select(x => new InputActionEdge(x, _epochs[x.ActionId])).ToArray();
        if (policy?.ApplicationFocused == false) { pointer = null; leftDown = leftUp = false; }
        if (actions.Length == 0 && !leftDown && !leftUp) return true;
        if (_updates.Count >= _capacity || actions.Length > _edgeCapacity - _edges)
        { OverflowCount++; Retire("Input update edge queue overflowed; queued edges were discarded and raw input capture was withdrawn."); return false; }
        _updates.Enqueue(new(invocation, updateType, Array.AsReadOnly(actions), pointer, leftDown, leftUp) { UpdateCount = updateCount });
        _edges += actions.Length; return true;
    }
    internal IReadOnlyList<InputUpdateSample> Drain()
    {
        var result = _updates.ToArray(); Clear(); return Array.AsReadOnly(result);
    }
    internal void InvalidateAction(string actionId)
    {
        _epochs.Remove(actionId);
        var saved = _updates.ToArray(); Clear();
        foreach (var update in saved)
        {
            var actions = update.Actions.Where(x => x.Owner.ActionId != actionId).ToArray();
            if (actions.Length == 0 && !update.LeftDown && !update.LeftUp) continue;
            _updates.Enqueue(update with { Actions = Array.AsReadOnly(actions) }); _edges += actions.Length;
        }
    }
    internal bool IsCurrent(InputActionEdge action) => !Retired && _epochs.TryGetValue(action.Owner.ActionId, out var epoch) && epoch == action.Epoch;
    internal void Clear() { _updates.Clear(); _edges = 0; }
    internal void Reset() { Clear(); _epochs.Clear(); }
    internal void Retire(string reason) { if (Retired) return; Retired = true; Failure = reason; Clear(); }
    public void Dispose() { Retire("Input update capture has been released."); _epochs.Clear(); }
}
