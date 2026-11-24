using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekRendererAccess
    {
        private const string RendererArrayType = "InLevel.NoteInformation.NoteRenderer[]";
        private const string ChildListType = "System.Collections.Generic.List<InLevel.EasyPoolChild>";
        private readonly Dictionary<long, SeekRendererChildState> _knownChildren = new();
        private readonly Dictionary<long, HashSet<long>> _knownPoolChildren = new();
        private readonly HashSet<long> _retainedPools = new(), _retainedTransforms = new();
        private readonly Dictionary<int, HashSet<long>> _selectedPools = new(), _selectedTransforms = new();

        private NativeRuntime.RootHandle RequiredReference(NativeRuntime.RootHandle receiver, IntPtr cls,
            string name, string type, int offset, IntPtr exactClass)
        {
            var root = Reference(receiver, cls, name, type, offset)
                ?? throw new InvalidDataException("A required actual renderer graph reference is absent.");
            if (ClassOf(root) != exactClass) throw new UnsupportedSignatureException("An actual renderer graph reference has another exact class.");
            return root;
        }
        private long NullableReference(NativeRuntime.RootHandle receiver, IntPtr cls, string name, string type,
            int offset, IntPtr? exactClass = null)
        {
            var root = Reference(receiver, cls, name, type, offset, required: false);
            if (root is null) return 0;
            if (exactClass is IntPtr expected && ClassOf(root) != expected)
                throw new UnsupportedSignatureException("A nullable renderer graph reference has another exact class.");
            return Target(root).ToInt64();
        }
        private long[] ReadRendererArray(NativeRuntime.RootHandle array, IntPtr arrayClass, IntPtr elementClass)
        {
            var observed = Native(() => _owner._api.ReadRootedRendererReferenceArrayWithPublication(array, arrayClass,
                elementClass, NativeSeekRendererCommands.MaximumArrayCapacity, (_, pointer) =>
                {
                    if (pointer != IntPtr.Zero) Adopt(pointer); // Ownership precedes metadata and lifecycle checks.
                    else VerifyLocal();
                }));
            if (observed.Array != Target(array) || observed.Class != arrayClass || observed.ElementClass != elementClass
                || observed.Length != observed.Items.Count)
                throw new InvalidOperationException("A complete rooted renderer array changed its exact identity or complete length.");
            var values = new long[observed.Length];
            for (var slot = 0; slot < values.Length; ++slot)
            {
                var pointer = observed.Items[slot]; values[slot] = pointer.ToInt64();
                if (pointer != IntPtr.Zero && ClassOf(Root(values[slot])) != elementClass)
                    throw new UnsupportedSignatureException("An actual renderer array slot has another exact element class.");
            }
            return values;
        }
        private void VerifyRendererNote(int ordinal)
        {
            if ((uint)ordinal >= (uint)_notes.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
            var note = _notes[ordinal]; var expected = _capturedNotes[ordinal];
            if (Target(note).ToInt64() != _noteRefs[ordinal] || ClassOf(note) != _owner._note || !Alive(note))
                throw new InvalidOperationException("The selected actual renderer note died or changed exact identity.");
            var data = Reference(note, _owner._note, "noteData", "InLevel.Note", 0x28);
            var line = Reference(note, _owner._note, "lineInformation", "InLevel.LineInformation", 0x20);
            if (data is null || line is null || Target(data).ToInt64() != expected.NoteData || Target(line).ToInt64() != expected.Line.Reference)
                throw new InvalidOperationException("The selected renderer note lost its captured data/line association.");
        }
        private SeekRendererNoteState ReadRendererNote(int ordinal)
        {
            VerifyRendererNote(ordinal); var note = _notes[ordinal];
            var descriptor = Native(() => _owner._api.InspectExactDeclaredReadField(_owner._note,
                "noteRenderers", RendererArrayType, false, 0x60));
            var array = RequiredReference(note, _owner._note, "noteRenderers", RendererArrayType, 0x60, descriptor.FieldClass);
            var plans = ReadRendererArray(array, descriptor.FieldClass, _rendererClass);
            if (plans.Any(reference => reference == 0)) throw new InvalidDataException("A native renderer array contains an absent plan.");
            return new(_noteRefs[ordinal], Target(array).ToInt64(), descriptor.FieldClass.ToInt64(),
                Value<int>(note, _owner._note, "_index", "System.Int32", 0x68),
                Value<bool>(note, _owner._note, "isActive", "System.Boolean", 0x30),
                Value<bool>(note, _owner._note, "destroyed", "System.Boolean", 0x31),
                Value<bool>(note, _owner._note, "missed", "System.Boolean", 0x55), Array.AsReadOnly(plans));
        }
        private SeekRendererPlanState ReadRendererPlan(long reference)
        {
            var plan = Root(reference);
            if (ClassOf(plan) != _rendererClass) throw new UnsupportedSignatureException("An actual renderer plan changed its exact nested class.");
            var pool = RequiredReference(plan, _rendererClass, "pool", "InLevel.EasyPool", 0x18, _poolClass);
            return new(reference, Value<int>(plan, _rendererClass, "poolIndex", "System.Int32", 0x10), Target(pool).ToInt64(),
                NullableReference(plan, _rendererClass, "poolChild", "InLevel.EasyPoolChild", 0x20, _childClass),
                NullableReference(plan, _rendererClass, "poolChildTransformPlan", "UnityEngine.Transform", 0x28),
                Value<bool>(plan, _rendererClass, "active", "System.Boolean", 0x30));
        }
        private SeekRendererPoolListState ReadRendererList(NativeRuntime.RootHandle list)
        {
            var layout = Native(() => _owner._api.InspectRendererChildListClass(ClassOf(list)));
            if (layout.ElementClass != _childClass) throw new UnsupportedSignatureException("A renderer pool List has another exact child element class.");
            var size = Native(() => _owner._api.ReadExactField<int>(list, layout.SizeField));
            var version = Native(() => _owner._api.ReadExactField<int>(list, layout.VersionField));
            var backing = RequiredReference(list, layout.Class, layout.BackingField.Name, layout.BackingField.TypeName,
                layout.BackingField.Offset, layout.ArrayClass);
            var slots = ReadRendererArray(backing, layout.ArrayClass, _childClass);
            if (size < 0 || size > slots.Length) throw new InvalidDataException("An actual renderer pool List size exceeds its complete capacity.");
            var currentBacking = Reference(list, layout.Class, layout.BackingField.Name, layout.BackingField.TypeName, layout.BackingField.Offset);
            if (currentBacking is null || Target(currentBacking) != Target(backing)
                || Native(() => _owner._api.ReadExactField<int>(list, layout.SizeField)) != size
                || Native(() => _owner._api.ReadExactField<int>(list, layout.VersionField)) != version
                || Native(() => _owner._api.InspectRendererChildListClass(ClassOf(list))) != layout)
                throw new InvalidOperationException("Renderer pool List backing/size/version changed during complete observation.");
            _observedRendererListBackings.Add((Target(list).ToInt64(), Target(backing).ToInt64()));
            return new(Target(list).ToInt64(), layout.Class.ToInt64(), Target(backing).ToInt64(), layout.ArrayClass.ToInt64(),
                size, version, Array.AsReadOnly(slots));
        }
        private SeekRendererPoolState ReadRendererPool(long reference)
        {
            var pool = Root(reference);
            if (ClassOf(pool) != _poolClass || !Alive(pool)) throw new InvalidOperationException("An actual renderer pool died or changed exact class.");
            var prefab = RequiredReference(pool, _poolClass, "easyPoolChild", "InLevel.EasyPoolChild", 0x20, _childClass);
            var available = Reference(pool, _poolClass, "_childList", ChildListType, 0x30)!;
            var dirty = Reference(pool, _poolClass, "_dirtyChildList", ChildListType, 0x38)!;
            var size = Value<int>(pool, _poolClass, "poolSize", "System.Int32", 0x28);
            var count = Value<int>(pool, _poolClass, "_childCount", "System.Int32", 0x2c);
            var first = ReadRendererList(available); var second = ReadRendererList(dirty);
            if (Value<int>(pool, _poolClass, "poolSize", "System.Int32", 0x28) != size
                || Value<int>(pool, _poolClass, "_childCount", "System.Int32", 0x2c) != count)
                throw new InvalidOperationException("Actual renderer pool counters changed during complete List observation.");
            return new(reference, Target(prefab).ToInt64(), size, count, Target(ComponentTransform(pool)).ToInt64(), first, second);
        }
        private SeekRendererChildState ReadRendererChild(long reference)
        {
            var child = Root(reference);
            if (ClassOf(child) != _childClass || !Alive(child)) throw new InvalidOperationException("A retained renderer child died or changed exact class.");
            var component = Reference(child, _childClass, "targetComponent", "UnityEngine.Component", 0x20, required: false);
            var observed = new SeekRendererChildState(reference, NullableReference(child, _childClass, "parentPool", "InLevel.EasyPool", 0x28, _poolClass),
                Value<bool>(child, _childClass, "dirty", "System.Boolean", 0x38), component is null ? 0 : Target(component).ToInt64(),
                component is null ? 0 : ClassOf(component).ToInt64(), Target(ComponentTransform(child)).ToInt64());
            ObserveNewChild(observed);
            return observed;
        }
        private SeekRendererGraphSnapshot ReadRendererSnapshot(int? selectedOrdinal)
        {
            var notes = new Dictionary<long, SeekRendererNoteState>(); var plans = new Dictionary<long, SeekRendererPlanState>();
            var pools = new Dictionary<long, SeekRendererPoolState>(); var children = new Dictionary<long, SeekRendererChildState>();
            var transforms = new Dictionary<long, SeekRendererTransformState>();
            var poolQueue = new Queue<long>(); var childQueue = new Queue<long>(); var transformQueue = new Queue<long>();
            long capacity = 0; var arrays = new HashSet<long>();
            void CheckObjects()
            {
                if (plans.Count > NativeSeekRendererCommands.MaximumTotalCapacity || pools.Count > NativeSeekRendererCommands.MaximumTotalCapacity
                    || children.Count > NativeSeekRendererCommands.MaximumTotalCapacity || transforms.Count > NativeSeekRendererCommands.MaximumTotalCapacity)
                    throw new NotSupportedException("The actual renderer closure exceeds its bounded object budget.");
            }
            void AddCapacity(long count)
            {
                capacity = checked(capacity + count);
                if (capacity > NativeSeekRendererCommands.MaximumTotalCapacity)
                    throw new NotSupportedException("The complete renderer closure exceeds its aggregate capacity budget.");
            }
            void QueuePool(long reference) { if (reference != 0 && !pools.ContainsKey(reference)) poolQueue.Enqueue(reference); }
            void QueueChild(long reference) { if (reference != 0 && !children.ContainsKey(reference)) childQueue.Enqueue(reference); }
            void QueueTransform(long reference) { if (reference != 0 && !transforms.ContainsKey(reference)) transformQueue.Enqueue(reference); }
            if (selectedOrdinal is int scoped)
            {
                if (_selectedPools.TryGetValue(scoped, out var priorPools)) foreach (var reference in priorPools) QueuePool(reference);
                if (_selectedTransforms.TryGetValue(scoped, out var priorTransforms)) foreach (var reference in priorTransforms) QueueTransform(reference);
            }
            else
            {
                foreach (var reference in _retainedPools) QueuePool(reference);
                foreach (var reference in _retainedTransforms) QueueTransform(reference);
            }
            IEnumerable<int> ordinals = selectedOrdinal is int selected ? new[] { selected } : Enumerable.Range(0, _notes.Length);
            foreach (var ordinal in ordinals)
            {
                var note = ReadRendererNote(ordinal); notes.Add(note.Reference, note); AddCapacity(note.Plans.Count);
                foreach (var reference in note.Plans)
                {
                    if (plans.ContainsKey(reference)) continue;
                    var plan = ReadRendererPlan(reference); plans.Add(reference, plan); CheckObjects();
                    QueuePool(plan.Pool); QueueChild(plan.Child); QueueTransform(plan.TargetParent);
                }
            }
            if (selectedOrdinal is null) foreach (var reference in _knownChildren.Keys) QueueChild(reference);
            var includedKnown = new HashSet<long>();
            while (poolQueue.Count != 0 || childQueue.Count != 0 || transformQueue.Count != 0)
            {
                while (poolQueue.Count != 0)
                {
                    var reference = poolQueue.Dequeue(); if (pools.ContainsKey(reference)) continue;
                    var pool = ReadRendererPool(reference); pools.Add(reference, pool); CheckObjects();
                    foreach (var list in new[] { pool.Available, pool.Dirty }) if (arrays.Add(list.Backing)) AddCapacity(list.CapacitySlots.Count);
                    QueueChild(pool.Prefab); QueueTransform(pool.Transform);
                    foreach (var member in pool.Available.CapacitySlots.Concat(pool.Dirty.CapacitySlots)) QueueChild(member);
                    if (_knownPoolChildren.TryGetValue(reference, out var known))
                        foreach (var child in known) if (includedKnown.Add(child)) QueueChild(child);
                }
                while (childQueue.Count != 0)
                {
                    var reference = childQueue.Dequeue(); if (children.ContainsKey(reference)) continue;
                    var child = ReadRendererChild(reference); children.Add(reference, child); CheckObjects();
                    QueuePool(child.ParentPool); QueueTransform(child.Transform);
                }
                while (transformQueue.Count != 0)
                {
                    var reference = transformQueue.Dequeue(); if (transforms.ContainsKey(reference)) continue;
                    transforms.Add(reference, ReadTransform(Root(reference))); CheckObjects();
                }
            }
            foreach (var child in children.Values)
            {
                if (_knownChildren.TryGetValue(child.Reference, out var prior) && prior.ParentPool != child.ParentPool
                    && _knownPoolChildren.TryGetValue(prior.ParentPool, out var oldMembers)) oldMembers.Remove(child.Reference);
                _knownChildren[child.Reference] = child;
                if (child.ParentPool == 0) continue;
                if (!_knownPoolChildren.TryGetValue(child.ParentPool, out var members)) _knownPoolChildren.Add(child.ParentPool, members = new());
                members.Add(child.Reference);
            }
            _retainedPools.UnionWith(pools.Keys); _retainedTransforms.UnionWith(transforms.Keys);
            if (selectedOrdinal is int selectedScope)
            {
                if (!_selectedPools.TryGetValue(selectedScope, out var seenPools)) _selectedPools.Add(selectedScope, seenPools = new());
                if (!_selectedTransforms.TryGetValue(selectedScope, out var seenTransforms)) _selectedTransforms.Add(selectedScope, seenTransforms = new());
                seenPools.UnionWith(pools.Keys); seenTransforms.UnionWith(transforms.Keys);
            }
            return new(_identity, Array.AsReadOnly(notes.Values.OrderBy(row => row.Reference).ToArray()),
                Array.AsReadOnly(plans.Values.OrderBy(row => row.Reference).ToArray()), Array.AsReadOnly(pools.Values.OrderBy(row => row.Reference).ToArray()),
                Array.AsReadOnly(children.Values.OrderBy(row => row.Reference).ToArray()), Array.AsReadOnly(transforms.Values.OrderBy(row => row.Reference).ToArray()));
        }
        public SeekRendererGraphSnapshot ReadSnapshot(int? selectedOrdinal = null)
        {
            if (selectedOrdinal is int ordinal && (uint)ordinal >= (uint)_notes.Length) throw new ArgumentOutOfRangeException(nameof(selectedOrdinal));
            VerifyReady(); var first = ReadRendererSnapshot(selectedOrdinal); VerifyReady();
            var second = ReadRendererSnapshot(selectedOrdinal); VerifyReady();
            if (!SeekRendererState.Same(first, second)) throw new InvalidOperationException("The complete selected renderer closure changed between observations.");
            return second;
        }
    }
}
