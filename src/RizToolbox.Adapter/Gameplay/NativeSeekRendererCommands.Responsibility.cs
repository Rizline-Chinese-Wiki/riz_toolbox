namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekRendererCommands
{
    internal const int MaximumUnclosedOwners = 256;
    private static readonly object ResponsibilityGate = new();
    private static readonly HashSet<NativeSeekRendererCommands> Responsibilities = new();
    private bool _ownerReserved;
    private void ReserveOwner()
    {
        lock (ResponsibilityGate)
        {
            if (Responsibilities.Count >= MaximumUnclosedOwners)
                throw new NotSupportedException("The bounded renderer effects-owner capacity is exhausted before any new native command.");
            Responsibilities.Add(this); _ownerReserved = true;
        }
    }
    private void ReleaseOwner()
    {
        lock (ResponsibilityGate)
        {
            if (!_ownerReserved) return;
            Responsibilities.Remove(this); _ownerReserved = false;
        }
    }
    private void Remember(SeekRendererGraphSnapshot graph)
    {
        void Add(long reference) { if (reference != 0) _knownReferences.Add(reference); }
        foreach (var note in graph.Notes)
        { Add(note.Reference); Add(note.Array); foreach (var plan in note.Plans) Add(plan); }
        foreach (var plan in graph.Plans)
        { Add(plan.Reference); Add(plan.Pool); Add(plan.Child); Add(plan.TargetParent); }
        foreach (var pool in graph.Pools)
        {
            Add(pool.Reference); Add(pool.Prefab); Add(pool.Transform);
            foreach (var list in new[] { pool.Available, pool.Dirty })
            { Add(list.Reference); Add(list.Backing); foreach (var child in list.CapacitySlots) Add(child); }
        }
        foreach (var child in graph.Children)
        { Add(child.Reference); Add(child.ParentPool); Add(child.TargetComponent); Add(child.Transform); }
        foreach (var transform in graph.Transforms) { Add(transform.Reference); Add(transform.Parent); }
    }
}
