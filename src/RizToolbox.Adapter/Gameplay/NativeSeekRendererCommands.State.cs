using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal static partial class SeekRendererState
{
    internal static bool Float(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    internal static bool Same(NativeVector3 a, NativeVector3 b) => Float(a.X, b.X) && Float(a.Y, b.Y) && Float(a.Z, b.Z);
    internal static bool Same(NativeRendererQuaternion a, NativeRendererQuaternion b)
        => Float(a.X, b.X) && Float(a.Y, b.Y) && Float(a.Z, b.Z) && Float(a.W, b.W);
    internal static bool Same(SeekRendererTransformState a, SeekRendererTransformState b)
        => a.Reference == b.Reference && a.Parent == b.Parent && Same(a.LocalPosition, b.LocalPosition)
            && Same(a.LocalRotation, b.LocalRotation) && Same(a.LocalScale, b.LocalScale);
    internal static bool Same(SeekRendererPoolListState a, SeekRendererPoolListState b)
        => a.Reference == b.Reference && a.Class == b.Class && a.Backing == b.Backing && a.ArrayClass == b.ArrayClass
            && a.Size == b.Size && a.Version == b.Version && a.CapacitySlots.SequenceEqual(b.CapacitySlots);
    internal static bool Same(SeekRendererPoolState a, SeekRendererPoolState b)
        => a.Reference == b.Reference && a.Prefab == b.Prefab && a.PoolSize == b.PoolSize && a.ChildCount == b.ChildCount
            && a.Transform == b.Transform && Same(a.Available, b.Available) && Same(a.Dirty, b.Dirty);
    internal static bool Same(SeekRendererNoteState a, SeekRendererNoteState b)
        => a.Reference == b.Reference && a.Array == b.Array && a.ArrayClass == b.ArrayClass && a.Index == b.Index
            && a.IsActive == b.IsActive && a.Destroyed == b.Destroyed && a.Missed == b.Missed && a.Plans.SequenceEqual(b.Plans);
    private static bool EqualMap<T>(IReadOnlyList<T> first, IReadOnlyList<T> second, Func<T, long> key, Func<T, T, bool> equal)
    {
        if (first.Count != second.Count) return false;
        var map = first.ToDictionary(key);
        return second.All(row => map.TryGetValue(key(row), out var original) && equal(original, row));
    }
    internal static bool Same(SeekRendererGraphSnapshot a, SeekRendererGraphSnapshot b)
        => a.Identity == b.Identity && EqualMap(a.Notes, b.Notes, row => row.Reference, Same)
            && EqualMap(a.Plans, b.Plans, row => row.Reference, (x, y) => x == y)
            && EqualMap(a.Pools, b.Pools, row => row.Reference, Same)
            && EqualMap(a.Children, b.Children, row => row.Reference, (x, y) => x == y)
            && EqualMap(a.Transforms, b.Transforms, row => row.Reference, Same);
    private static bool Projection<T>(IReadOnlyList<T> all, IReadOnlyList<T> selected, Func<T, long> key, Func<T, T, bool> equal)
    {
        var map = all.ToDictionary(key);
        return selected.All(row => map.TryGetValue(key(row), out var original) && equal(original, row));
    }
    internal static bool IsProjection(SeekRendererGraphSnapshot all, SeekRendererGraphSnapshot selected)
        => all.Identity == selected.Identity && Projection(all.Notes, selected.Notes, row => row.Reference, Same)
            && Projection(all.Plans, selected.Plans, row => row.Reference, (x, y) => x == y)
            && Projection(all.Pools, selected.Pools, row => row.Reference, Same)
            && Projection(all.Children, selected.Children, row => row.Reference, (x, y) => x == y)
            && Projection(all.Transforms, selected.Transforms, row => row.Reference, Same);
    private static IReadOnlyList<T> MergeMap<T>(IReadOnlyList<T> all, IReadOnlyList<T> changed, Func<T, long> key)
    {
        var map = all.ToDictionary(key); foreach (var row in changed) map[key(row)] = row;
        return Array.AsReadOnly(map.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray());
    }
    internal static SeekRendererGraphSnapshot Merge(SeekRendererGraphSnapshot all, SeekRendererGraphSnapshot changed)
        => new(all.Identity, MergeMap(all.Notes, changed.Notes, row => row.Reference), MergeMap(all.Plans, changed.Plans, row => row.Reference),
            MergeMap(all.Pools, changed.Pools, row => row.Reference), MergeMap(all.Children, changed.Children, row => row.Reference),
            MergeMap(all.Transforms, changed.Transforms, row => row.Reference));
    private static T[] CopyRows<T>(IReadOnlyList<T> input, int maximum, Func<T, T> copy, Action guard)
    {
        ArgumentNullException.ThrowIfNull(input); guard(); var count = input.Count; guard();
        if (count < 0 || count > maximum) throw new NotSupportedException("A complete renderer graph exceeds its declared bound.");
        var rows = new T[count];
        for (var index = 0; index < count; ++index) { guard(); rows[index] = copy(input[index]); guard(); }
        guard(); if (input.Count != count) throw new InvalidOperationException("A complete renderer graph changed during copying."); guard();
        return rows;
    }
    internal static SeekRendererGraphSnapshot Copy(SeekRendererGraphSnapshot input, Action guard)
    {
        guard();
        if (input is null || input.Identity is null || input.Identity.Chart is null || input.Identity.Chart.Run is null)
            throw new InvalidDataException("The exact renderer source identity is absent.");
        IReadOnlyList<long> CopySlots(IReadOnlyList<long> slots)
            => Array.AsReadOnly(CopyRows(slots, NativeSeekRendererCommands.MaximumArrayCapacity, value => value, guard));
        SeekRendererPoolListState CopyList(SeekRendererPoolListState list)
        {
            if (list is null || list.Reference == 0 || list.Class == 0 || list.Backing == 0 || list.ArrayClass == 0)
                throw new InvalidDataException("Complete actual renderer pool List storage is absent.");
            var slots = CopySlots(list.CapacitySlots);
            if (list.Size < 0 || list.Size > slots.Count) throw new InvalidDataException("A renderer pool List size exceeds its full capacity.");
            return list with { CapacitySlots = slots };
        }
        var notes = CopyRows(input.Notes, NativeSeekRendererCommands.MaximumNotes, row => row is null
            ? throw new InvalidDataException("An actual renderer note row is absent.") : row with { Plans = CopySlots(row.Plans) }, guard);
        var plans = CopyRows(input.Plans, NativeSeekRendererCommands.MaximumTotalCapacity,
            row => row is null ? throw new InvalidDataException("An actual renderer plan is absent.") : row with { }, guard);
        var pools = CopyRows(input.Pools, NativeSeekRendererCommands.MaximumTotalCapacity, row => row is null
            ? throw new InvalidDataException("An actual renderer pool is absent.") : row with { Available = CopyList(row.Available), Dirty = CopyList(row.Dirty) }, guard);
        var children = CopyRows(input.Children, NativeSeekRendererCommands.MaximumTotalCapacity,
            row => row is null ? throw new InvalidDataException("An actual renderer child is absent.") : row with { }, guard);
        var transforms = CopyRows(input.Transforms, NativeSeekRendererCommands.MaximumTotalCapacity,
            row => row is null ? throw new InvalidDataException("An actual renderer Transform is absent.") : row with { }, guard);
        var result = new SeekRendererGraphSnapshot(input.Identity, Array.AsReadOnly(notes), Array.AsReadOnly(plans), Array.AsReadOnly(pools),
            Array.AsReadOnly(children), Array.AsReadOnly(transforms));
        guard(); return result;
    }
    internal static void Validate(SeekRendererGraphSnapshot graph, IReadOnlyList<long>? completeNotes = null)
    {
        var notes = graph.Notes.ToDictionary(row => row.Reference); var plans = graph.Plans.ToDictionary(row => row.Reference);
        var pools = graph.Pools.ToDictionary(row => row.Reference); var children = graph.Children.ToDictionary(row => row.Reference);
        var transforms = graph.Transforms.ToDictionary(row => row.Reference);
        if (notes.ContainsKey(0) || plans.ContainsKey(0) || pools.ContainsKey(0) || children.ContainsKey(0) || transforms.ContainsKey(0)
            || completeNotes is not null && (completeNotes.Count != notes.Count || completeNotes.Any(reference => !notes.ContainsKey(reference))))
            throw new InvalidDataException("The complete actual renderer graph contains an absent, duplicate or missing identity.");
        long capacity = 0; var arrays = new Dictionary<long, SeekRendererPoolListState>(); var listOwners = new HashSet<long>();
        var members = new Dictionary<long, long>();
        foreach (var note in notes.Values)
        {
            if (note.Array == 0 || note.ArrayClass == 0 || note.Plans.Any(reference => reference == 0 || !plans.ContainsKey(reference)))
                throw new InvalidDataException("An actual note lacks its complete ordered renderer array.");
            capacity = checked(capacity + note.Plans.Count);
        }
        foreach (var pool in pools.Values)
        {
            if (!children.ContainsKey(pool.Prefab) || !transforms.ContainsKey(pool.Transform)
                || pool.PoolSize < 0 || pool.PoolSize > NativeSeekRendererCommands.MaximumTotalCapacity
                || pool.ChildCount < 0 || pool.ChildCount != pool.Available.Size || pool.ChildCount > pool.PoolSize
                || (long)pool.ChildCount + pool.Dirty.Size > pool.PoolSize)
                throw new InvalidDataException("An actual renderer pool has inconsistent prefab, count or complete membership.");
            foreach (var list in new[] { pool.Available, pool.Dirty })
            {
                if (!listOwners.Add(list.Reference)) throw new NotSupportedException("Aliased renderer pool Lists are not supported by the native transition model.");
                if (arrays.TryGetValue(list.Backing, out var other))
                {
                    if (list.CapacitySlots.Count != 0) throw new NotSupportedException("Distinct renderer pool Lists sharing nonempty backing storage are unsupported.");
                    if (other.ArrayClass != list.ArrayClass || !other.CapacitySlots.SequenceEqual(list.CapacitySlots))
                        throw new InvalidDataException("Aliased empty renderer backing storage is contradictory.");
                }
                else { arrays.Add(list.Backing, list); capacity = checked(capacity + list.CapacitySlots.Count); }
                foreach (var member in list.CapacitySlots.Take(list.Size))
                    if (member == 0 || !children.TryGetValue(member, out var child)
                        || list.Reference == pool.Dirty.Reference && child.ParentPool != pool.Reference || !members.TryAdd(member, pool.Reference))
                        throw new InvalidDataException("An active renderer pool child has absent, duplicate or contradictory membership.");
            }
        }
        var activeOwners = new Dictionary<long, long>();
        foreach (var plan in plans.Values)
        {
            if (!pools.ContainsKey(plan.Pool) || plan.TargetParent != 0 && !transforms.ContainsKey(plan.TargetParent)
                || plan.Child != 0 && !children.ContainsKey(plan.Child))
                throw new InvalidDataException("An actual renderer plan lost its complete pool/child/target-parent closure.");
            if (plan.Active && (plan.Child == 0 || children[plan.Child].ParentPool == 0 || members.ContainsKey(plan.Child)
                || !activeOwners.TryAdd(plan.Child, plan.Reference)))
                throw new InvalidDataException("An active child has contradictory renderer ownership or available/dirty membership.");
        }
        foreach (var child in children.Values)
            if (!transforms.ContainsKey(child.Transform) || child.ParentPool != 0 && !pools.ContainsKey(child.ParentPool)
                || (child.TargetComponent == 0) != (child.TargetComponentClass == 0))
                throw new InvalidDataException("A renderer child lacks its exact parent-pool/component/Transform closure.");
        foreach (var transform in transforms.Values)
            if (!float.IsFinite(transform.LocalPosition.X) || !float.IsFinite(transform.LocalPosition.Y) || !float.IsFinite(transform.LocalPosition.Z)
                || !float.IsFinite(transform.LocalRotation.X) || !float.IsFinite(transform.LocalRotation.Y) || !float.IsFinite(transform.LocalRotation.Z)
                || !float.IsFinite(transform.LocalRotation.W) || !float.IsFinite(transform.LocalScale.X)
                || !float.IsFinite(transform.LocalScale.Y) || !float.IsFinite(transform.LocalScale.Z))
                throw new InvalidDataException("A renderer Transform contains nonfinite native components.");
        if (capacity > NativeSeekRendererCommands.MaximumTotalCapacity)
            throw new NotSupportedException("The complete renderer graph exceeds its aggregate capacity budget.");
    }
}
