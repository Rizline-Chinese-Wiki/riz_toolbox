namespace RizToolbox.Adapter.Gameplay;

internal static partial class SeekRendererState
{
    internal static (int NewChildren, int NewBackings, int Capacity) Estimate(
        SeekRendererGraphSnapshot graph, IReadOnlyList<SeekRendererCommand> commands)
    {
        Validate(graph);
        var notes = graph.Notes.ToDictionary(row => row.Reference);
        var plans = graph.Plans.ToDictionary(row => row.Reference);
        var pools = graph.Pools.ToDictionary(row => row.Reference);
        var children = graph.Children.ToDictionary(row => row.Reference);
        var active = plans.ToDictionary(pair => pair.Key, pair => pair.Value.Active);
        var childState = plans.ToDictionary(pair => pair.Key, pair => pair.Value.Child == 0
            ? (Pool: 0L, Dirty: (bool?)null) : (Pool: children[pair.Value.Child].ParentPool, Dirty: (bool?)children[pair.Value.Child].Dirty));
        var lists = pools.Values.SelectMany(pool => new[] { pool.Available, pool.Dirty })
            .ToDictionary(list => list.Reference, list => new CapacityBudget(list.Size, list.CapacitySlots.Count));
        var grows = pools.Keys.ToDictionary(reference => reference, _ => 0);
        var newChildren = 0; var newBackings = 0;
        long capacity = graph.Notes.Sum(note => (long)note.Plans.Count)
            + pools.Values.SelectMany(pool => new[] { pool.Available, pool.Dirty })
                .GroupBy(list => list.Backing).Sum(group => (long)group.First().CapacitySlots.Count);
        void Add(long listReference, bool guaranteed)
        {
            var list = lists[listReference]; var size = checked(list.Size + 1);
            if (size > NativeSeekRendererCommands.MaximumArrayCapacity)
                throw new NotSupportedException("A possible renderer return exceeds its complete List observation bound.");
            if (size > list.Capacity)
            {
                var next = NextCapacity(list.Capacity);
                capacity = checked(capacity + next - list.Capacity);
                list.Capacity = next; ++newBackings;
            }
            list.Size = size;
            if (guaranteed) ++list.MinimumSize;
        }
        // A new child's dirty state has no observed baseline. Reserve either
        // branch if another aliased plan later releases this acquisition.
        foreach (var command in commands)
        {
            if (!notes.TryGetValue(command.Reference, out var note))
                throw new InvalidDataException("A renderer budget command has no actual note identity.");
            if (command.Kind == SeekRendererCommandKind.Preserve) continue;
            foreach (var reference in note.Plans)
            {
                var plan = plans[reference];
                if (command.Kind == SeekRendererCommandKind.Appear)
                {
                    if (active[reference]) continue;
                    active[reference] = true;
                    var available = lists[pools[plan.Pool].Available.Reference];
                    if (available.MinimumSize == 0)
                    {
                        ++newChildren; ++grows[plan.Pool];
                        if ((long)pools[plan.Pool].PoolSize + grows[plan.Pool] > NativeSeekRendererCommands.MaximumTotalCapacity)
                            throw new NotSupportedException("Possible renderer growth exceeds the complete pool-size bound.");
                    }
                    if (available.Capacity == 0)
                    {
                        available.Capacity = NextCapacity(0); ++newBackings;
                        capacity = checked(capacity + available.Capacity);
                    }
                    available.Size = Math.Max(0, available.Size - 1);
                    available.MinimumSize = Math.Max(0, available.MinimumSize - 1);
                    childState[reference] = (plan.Pool, null);
                }
                else if (command.Kind == SeekRendererCommandKind.Disappear)
                {
                    if (!active[reference]) continue;
                    active[reference] = false; var child = childState[reference];
                    if (child.Pool == 0 || !pools.TryGetValue(child.Pool, out var pool))
                        throw new InvalidDataException("An active renderer has no captured actual return pool.");
                    if (child.Dirty != true) Add(pool.Available.Reference, child.Dirty == false);
                    if (child.Dirty != false) Add(pool.Dirty.Reference, child.Dirty == true);
                }
                else throw new NotSupportedException("The renderer budget command kind is unsupported.");
                if (capacity > NativeSeekRendererCommands.MaximumTotalCapacity)
                    throw new NotSupportedException("Possible renderer effects exceed the aggregate capacity budget.");
            }
        }
        return (newChildren, newBackings, checked((int)capacity));
    }
    /// <summary>Recognizes full reachable states and whole-plan failure prefixes.
    /// Even an exact prefix leaves an entered throwing call Unknown in its owner;
    /// hidden allocations and detached arrays are not claimed to be undone.</summary>
    internal static (bool Recognized, bool Growth, bool Deferred, string? Failure) Recognize(
        SeekRendererGraphSnapshot globalBefore, SeekRendererGraphSnapshot before,
        SeekRendererGraphSnapshot after, SeekRendererCommand command, bool failed, IReadOnlySet<long> knownBefore)
        => Recognize(globalBefore, before, after, command, failed, knownBefore, out _);

    internal static (bool Recognized, bool Growth, bool Deferred, string? Failure) Recognize(
        SeekRendererGraphSnapshot globalBefore, SeekRendererGraphSnapshot before,
        SeekRendererGraphSnapshot after, SeekRendererCommand command, bool failed, IReadOnlySet<long> knownBefore,
        out IReadOnlyList<SeekRendererRetiredBackingState> retiredBackings)
    {
        retiredBackings = Array.Empty<SeekRendererRetiredBackingState>();
        try
        {
            Validate(before); Validate(after);
            if (!IsProjection(globalBefore, before) || before.Identity != after.Identity)
                return (false, false, false, "Renderer effect snapshots do not share the owned source.");
            ArgumentNullException.ThrowIfNull(knownBefore);
            var model = new RendererModel(globalBefore, before, after, knownBefore);
            if (failed && model.Matches()) return (true, false, false, null);
            var note = before.Notes.Single(row => row.Reference == command.Reference);
            if (command.Kind == SeekRendererCommandKind.Preserve)
                return (model.Matches(), false, false, model.Matches() ? null : "A preserved renderer graph changed.");
            foreach (var reference in note.Plans)
            {
                var plan = model.Plans[reference];
                if (command.Kind == SeekRendererCommandKind.Appear)
                {
                    if (plan.Active) continue;
                    model.Appear(plan);
                }
                else if (command.Kind == SeekRendererCommandKind.Disappear)
                {
                    if (!plan.Active) continue;
                    model.Disappear(plan);
                }
                else return (false, false, false, "Unsupported renderer command in native effect observation.");
                if (failed && model.Matches())
                { retiredBackings = model.RetiredBackings(); return (true, model.Growth, model.Deferred, null); }
            }
            if (model.Matches())
            { retiredBackings = model.RetiredBackings(); return (true, model.Growth, model.Deferred, null); }
            return (false, model.Growth, model.Deferred, "The graph is not an exact native renderer result or whole-plan prefix.");
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException
            or OverflowException or InvalidDataException or KeyNotFoundException)
        { return (false, false, false, error.Message); }
    }
    private sealed class RendererModel
    {
        private readonly SeekRendererGraphSnapshot _before, _after;
        internal readonly Dictionary<long, SeekRendererPlanState> Plans;
        private readonly Dictionary<long, SeekRendererPoolState> _pools, _afterPools;
        private readonly Dictionary<long, SeekRendererChildState> _children, _afterChildren;
        private readonly Dictionary<long, SeekRendererTransformState> _transforms, _afterTransforms;
        private readonly Dictionary<long, SeekRendererPlanState> _afterPlans;
        private readonly HashSet<long> _known, _newChildren = new(), _newTransforms = new();
        private readonly Dictionary<long, long> _newBackings = new();
        private readonly List<SeekRendererRetiredBackingState> _retiredBackings = new();
        internal bool Growth, Deferred;
        internal IReadOnlyList<SeekRendererRetiredBackingState> RetiredBackings()
            => Array.AsReadOnly(_retiredBackings.ToArray());
        internal RendererModel(SeekRendererGraphSnapshot global, SeekRendererGraphSnapshot before, SeekRendererGraphSnapshot after,
            IReadOnlySet<long> knownBefore)
        {
            _before = before; _after = after;
            Plans = before.Plans.ToDictionary(row => row.Reference);
            _pools = before.Pools.ToDictionary(row => row.Reference);
            _children = before.Children.ToDictionary(row => row.Reference);
            _transforms = before.Transforms.ToDictionary(row => row.Reference);
            _afterPlans = after.Plans.ToDictionary(row => row.Reference);
            _afterPools = after.Pools.ToDictionary(row => row.Reference);
            _afterChildren = after.Children.ToDictionary(row => row.Reference);
            _afterTransforms = after.Transforms.ToDictionary(row => row.Reference);
            _known = References(global); _known.UnionWith(knownBefore);
        }
        internal bool Matches() => Same(_after, new SeekRendererGraphSnapshot(_before.Identity, _before.Notes,
            Plans.Values.ToArray(), _pools.Values.ToArray(), _children.Values.ToArray(), _transforms.Values.ToArray()));
        private static HashSet<long> References(SeekRendererGraphSnapshot graph)
        {
            var known = new HashSet<long> { 0 };
            foreach (var note in graph.Notes) { known.Add(note.Reference); known.Add(note.Array); }
            foreach (var plan in graph.Plans) { known.Add(plan.Reference); known.Add(plan.Pool); known.Add(plan.Child); known.Add(plan.TargetParent); }
            foreach (var pool in graph.Pools)
            {
                known.Add(pool.Reference); known.Add(pool.Prefab); known.Add(pool.Transform);
                foreach (var list in new[] { pool.Available, pool.Dirty })
                { known.Add(list.Reference); known.Add(list.Backing); foreach (var child in list.CapacitySlots) known.Add(child); }
            }
            foreach (var child in graph.Children)
            { known.Add(child.Reference); known.Add(child.ParentPool); known.Add(child.TargetComponent); known.Add(child.Transform); }
            foreach (var transform in graph.Transforms) { known.Add(transform.Reference); known.Add(transform.Parent); }
            return known;
        }
        internal void Appear(SeekRendererPlanState plan)
        {
            var pool = _pools[plan.Pool]; var available = pool.Available;
            SeekRendererChildState child;
            if (pool.ChildCount == 0)
            {
                var reference = _afterPlans[plan.Reference].Child;
                if (_known.Contains(reference) || !_newChildren.Add(reference) || _newBackings.ContainsValue(reference)
                    || _newTransforms.Contains(reference) || !_afterChildren.TryGetValue(reference, out child!))
                    throw new InvalidDataException("An empty-pool result is not a distinct newly observed exact child.");
                if (child.ParentPool != pool.Reference || _known.Contains(child.Transform)
                    || _newChildren.Contains(child.Transform) || _newBackings.ContainsValue(child.Transform)
                    || !_newTransforms.Add(child.Transform) || !_afterTransforms.TryGetValue(child.Transform, out var transform))
                    throw new InvalidDataException("A new child has another pool or reuses an existing Transform identity.");
                _children.Add(reference, child); _transforms.Add(child.Transform, transform);
                available = Append(pool, available, reference);
                pool = pool with { PoolSize = checked(pool.PoolSize + 1), ChildCount = checked(pool.ChildCount + 1) };
                Growth = true;
            }
            else child = _children[available.CapacitySlots[0]];
            if (available.CapacitySlots[0] != child.Reference)
                throw new InvalidDataException("Renderer acquisition did not select its first available child.");
            available = RemoveFirst(available);
            _pools[pool.Reference] = pool with { Available = available, ChildCount = checked(pool.ChildCount - 1) };
            _children[child.Reference] = child with { ParentPool = pool.Reference };
            _transforms[child.Transform] = Canonical(child.Transform, plan.TargetParent, true);
            Plans[plan.Reference] = plan with { Child = child.Reference, Active = true };
        }
        internal void Disappear(SeekRendererPlanState plan)
        {
            var child = _children[plan.Child]; var pool = _pools[child.ParentPool];
            if (child.Dirty)
            {
                _pools[pool.Reference] = pool with { Dirty = Append(pool, pool.Dirty, child.Reference) };
                Deferred = true;
            }
            else
            {
                _transforms[child.Transform] = Canonical(child.Transform, pool.Transform, false);
                _pools[pool.Reference] = pool with { Available = Append(pool, pool.Available, child.Reference),
                    ChildCount = checked(pool.ChildCount + 1) };
            }
            Plans[plan.Reference] = plan with { Active = false };
        }
        private static SeekRendererTransformState Canonical(long transform, long parent, bool visible)
            => new(transform, parent, new(0, 0, 0), new(0, 0, 0, 1), visible ? new(1, 1, 1) : new(0, 0, 0));
        private SeekRendererPoolListState Append(SeekRendererPoolState pool, SeekRendererPoolListState list, long child)
        {
            var slots = list.CapacitySlots.ToArray(); var backing = list.Backing;
            if (list.Size == slots.Length)
            {
                var next = NextCapacity(slots.Length);
                var observed = pool.Available.Reference == list.Reference ? _afterPools[pool.Reference].Available : _afterPools[pool.Reference].Dirty;
                // Multiple growths inside one outer call can leave only the final
                // array reachable. Its identity represents the final model, not an
                // invented address for an unobserved intermediate allocation.
                if (!_newBackings.TryGetValue(list.Reference, out backing))
                {
                    // This is the actual retained pre-call array, after every
                    // earlier modeled append in this same native call. Later
                    // intermediate arrays have no observed address; never issue
                    // a retirement record for the final address in their place.
                    _retiredBackings.Add(new(pool.Reference, list with
                    { CapacitySlots = Array.AsReadOnly(slots.ToArray()) }));
                    backing = observed.Backing;
                    if (_known.Contains(backing) || _newBackings.ContainsValue(backing)
                        || _newChildren.Contains(backing) || _newTransforms.Contains(backing))
                        throw new InvalidDataException("Renderer List growth reused an existing graph or newly assigned storage identity.");
                    _newBackings.Add(list.Reference, backing);
                }
                var grown = new long[next]; Array.Copy(slots, grown, list.Size); slots = grown;
                Growth = true;
            }
            slots[list.Size] = child;
            return list with { Backing = backing, Size = checked(list.Size + 1), Version = unchecked(list.Version + 1),
                CapacitySlots = Array.AsReadOnly(slots) };
        }
        private static SeekRendererPoolListState RemoveFirst(SeekRendererPoolListState list)
        {
            if (list.Size <= 0) throw new InvalidDataException("A renderer FIFO removal has no first child.");
            var slots = list.CapacitySlots.ToArray(); var size = list.Size - 1;
            if (size != 0) Array.Copy(slots, 1, slots, 0, size);
            slots[size] = 0;
            return list with { Size = size, Version = unchecked(list.Version + 1), CapacitySlots = Array.AsReadOnly(slots) };
        }
    }
    private sealed class CapacityBudget
    {
        internal int Size, MinimumSize, Capacity;
        internal CapacityBudget(int size, int capacity) => (Size, MinimumSize, Capacity) = (size, size, capacity);
    }
    private static int NextCapacity(int capacity)
    {
        var next = capacity == 0 ? 4 : checked(capacity * 2);
        if (next > NativeSeekRendererCommands.MaximumArrayCapacity)
            throw new NotSupportedException("Official renderer List doubling exceeds the complete observation bound.");
        return next;
    }
}
