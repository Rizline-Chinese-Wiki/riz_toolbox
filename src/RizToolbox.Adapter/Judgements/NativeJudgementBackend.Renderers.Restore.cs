using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekRendererAccess
    {
        private SeekRendererGraphSnapshot? _restorationBaseline;
        private bool _restorationEntered;
        public void PrepareRestore(SeekRendererGraphSnapshot baseline)
        {
            VerifySource();
            if (_restorationBaseline is not null || !SeekRendererState.Same(ReadSnapshot(), baseline))
                throw new InvalidOperationException("The native renderer inverse requires its once-captured actual baseline.");
            PrepareRetirementBaseline(baseline);
            _restorationBaseline = baseline;
        }
        public void Restore(SeekRendererGraphSnapshot expected, SeekRendererGraphSnapshot baseline,
            IReadOnlyList<SeekRendererGraphSnapshot> recognized, Action beforeDispatch)
        {
            ArgumentNullException.ThrowIfNull(beforeDispatch); VerifySource();
            if (_restorationEntered || !ReferenceEquals(_restorationBaseline, baseline))
                throw new InvalidOperationException("A foreign or repeated renderer inverse owns no original native storage.");
            _restorationEntered = true;
            if (!SeekRendererState.Same(ReadSnapshot(), expected))
                throw new InvalidOperationException("The actual renderer graph differs from its expected command result.");
            var retirements = RequireRestorationStorage(expected, baseline);
            VerifyRetirementMethods();
            foreach (var candidate in retirements) VerifyChildRetirement(candidate, requireRecognized: true);
            // Restore original transforms and storage before destroying any
            // precisely recognized clone. No new renderer command is dispatched.
            var transforms = expected.Transforms.ToDictionary(value => value.Reference);
            var ownedTransforms = baseline.Children.Select(child => child.Transform).ToHashSet();
            foreach (var original in baseline.Transforms.Where(value => ownedTransforms.Contains(value.Reference)))
            {
                var current = transforms[original.Reference];
                if (SeekRendererState.Same(current, original)) continue;
                RestoreTransform(current, original, beforeDispatch);
            }
            var children = expected.Children.ToDictionary(value => value.Reference);
            foreach (var original in baseline.Children)
            {
                var current = children[original.Reference]; var receiver = Root(original.Reference);
                RestoreReference(receiver, _childClass, "parentPool", "InLevel.EasyPool", 0x28,
                    current.ParentPool, original.ParentPool, beforeDispatch);
                RestoreValue(receiver, _childClass, "dirty", "System.Boolean", 0x38,
                    current.Dirty, original.Dirty, beforeDispatch);
            }
            var plans = expected.Plans.ToDictionary(value => value.Reference);
            foreach (var original in baseline.Plans)
            {
                var current = plans[original.Reference]; var receiver = Root(original.Reference);
                RestoreReference(receiver, _rendererClass, "poolChild", "InLevel.EasyPoolChild", 0x20,
                    current.Child, original.Child, beforeDispatch);
                RestoreValue(receiver, _rendererClass, "active", "System.Boolean", 0x30,
                    current.Active, original.Active, beforeDispatch);
            }
            var pools = expected.Pools.ToDictionary(value => value.Reference);
            foreach (var original in baseline.Pools)
            {
                var current = pools[original.Reference];
                RestoreList(current.Available, original.Available, recognized, beforeDispatch);
                RestoreList(current.Dirty, original.Dirty, recognized, beforeDispatch);
                RestoreValue(Root(original.Reference), _poolClass, "_childCount", "System.Int32", 0x2c,
                    current.ChildCount, original.ChildCount, beforeDispatch);
                RestoreValue(Root(original.Reference), _poolClass, "poolSize", "System.Int32", 0x28,
                    current.PoolSize, original.PoolSize, beforeDispatch);
            }
            // Known new children remain independently rooted and are still in
            // this access's snapshot closure after all game references detach.
            var originalTransforms = baseline.Transforms.ToDictionary(value => value.Reference);
            var pending = baseline with
            {
                Children = baseline.Children.Concat(retirements.Select(value => children[value.Child.Reference])).ToArray(),
                Transforms = expected.Transforms.Select(value => ownedTransforms.Contains(value.Reference)
                    ? originalTransforms[value.Reference] : value).ToArray()
            };
            foreach (var candidate in retirements.Reverse())
            {
                RequireRestorationGraph(pending);
                DestroyRetainedClone(candidate, beforeDispatch);
                var removedTransforms = candidate.Tree.Select(node => node.Transform.Reference).ToHashSet();
                pending = pending with
                {
                    Children = pending.Children.Where(value => value.Reference != candidate.Child.Reference).ToArray(),
                    Transforms = pending.Transforms.Where(value => !removedTransforms.Contains(value.Reference)).ToArray()
                };
                RequireRestorationGraph(pending);
            }
            VerifySource(); var actual = ReadSnapshot();
            if (!SeekRendererState.Same(actual, SeekRendererState.WithExternalTransforms(baseline, actual)))
                throw new InvalidOperationException("The original native renderer/pool graph was not completely restored.");
        }

        private void RequireRestorationGraph(SeekRendererGraphSnapshot expected)
        {
            VerifySource(); var actual = ReadSnapshot();
            if (!SeekRendererState.Same(actual, SeekRendererState.WithExternalTransforms(expected, actual)))
                throw new InvalidOperationException("The original renderer storage or still-owned clone changed during retirement.");
        }

        private ChildRetirement[] RequireRestorationStorage(SeekRendererGraphSnapshot current, SeekRendererGraphSnapshot original)
        {
            if (current.Identity != original.Identity || current.Notes.Count != original.Notes.Count
                || current.Plans.Count != original.Plans.Count || current.Pools.Count != original.Pools.Count)
                throw new InvalidOperationException("Renderer restoration cannot replace original note, plan or pool membership.");
            var plans = current.Plans.ToDictionary(value => value.Reference);
            var children = current.Children.ToDictionary(value => value.Reference);
            var pools = current.Pools.ToDictionary(value => value.Reference);
            var transforms = current.Transforms.Select(value => value.Reference).ToHashSet();
            var originalChildren = original.Children.Select(value => value.Reference).ToHashSet();
            var added = current.Children.Where(value => !originalChildren.Contains(value.Reference)).ToArray();
            var retirements = new List<ChildRetirement>();
            foreach (var child in added)
            {
                if (!_childRetirements.TryGetValue(child.Reference, out var candidate) || !candidate.Journal.Recognized
                    || candidate.Child.Transform != child.Transform || candidate.Child.ParentPool != child.ParentPool
                    || candidate.Child.TargetComponent != child.TargetComponent || candidate.Child.TargetComponentClass != child.TargetComponentClass)
                    throw new InvalidOperationException("A new renderer child has no exact recognized first-observation ownership.");
                retirements.Add(candidate);
            }
            if (current.Children.Count != original.Children.Count + added.Length
                || !transforms.SetEquals(original.Transforms.Select(value => value.Reference).Concat(added.Select(value => value.Transform))))
                throw new InvalidOperationException("Renderer growth added or removed unowned child/Transform membership.");
            var notes = current.Notes.ToDictionary(value => value.Reference);
            foreach (var prior in original.Notes)
                if (!notes.TryGetValue(prior.Reference, out var value) || value.Array != prior.Array || value.ArrayClass != prior.ArrayClass
                    || value.Index != prior.Index || value.IsActive != prior.IsActive || value.Destroyed != prior.Destroyed || value.Missed != prior.Missed
                    || !value.Plans.SequenceEqual(prior.Plans))
                    throw new InvalidOperationException("Restore the original typed note flags and visibility before renderer storage.");
            foreach (var prior in original.Plans)
                if (!plans.TryGetValue(prior.Reference, out var value) || value.Pool != prior.Pool
                    || value.PoolIndex != prior.PoolIndex || value.TargetParent != prior.TargetParent)
                    throw new InvalidOperationException("The original renderer plan changed its pool or target parent.");
            foreach (var prior in original.Children)
                if (!children.TryGetValue(prior.Reference, out var value) || value.Transform != prior.Transform
                    || value.TargetComponent != prior.TargetComponent || value.TargetComponentClass != prior.TargetComponentClass)
                    throw new InvalidOperationException("The original renderer child changed its component or Transform.");
            foreach (var prior in original.Pools)
            {
                if (!pools.TryGetValue(prior.Reference, out var value) || value.Prefab != prior.Prefab
                    || value.PoolSize != checked(prior.PoolSize + added.Count(child => child.ParentPool == prior.Reference)) || value.Transform != prior.Transform
                    || !SameListOwner(value.Available, prior.Available) || !SameListOwner(value.Dirty, prior.Dirty))
                    throw new NotSupportedException("The renderer inverse cannot replace original pool or List owners.");
            }
            if (original.Transforms.Any(value => !transforms.Contains(value.Reference)))
                throw new InvalidOperationException("The original renderer Transform membership changed.");
            return retirements.ToArray();
        }
        private static bool SameListOwner(SeekRendererPoolListState a, SeekRendererPoolListState b)
            => a.Reference == b.Reference && a.Class == b.Class && a.ArrayClass == b.ArrayClass;

        private void RestoreValue<T>(NativeRuntime.RootHandle receiver, IntPtr cls, string name, string type, int offset,
            T expected, T original, Action beforeDispatch) where T : struct
        {
            if (EqualityComparer<T>.Default.Equals(expected, original)) return;
            if (!EqualityComparer<T>.Default.Equals(Value<T>(receiver, cls, name, type, offset), expected))
                throw new InvalidOperationException("Another writer changed renderer field " + name + ".");
            Native(() =>
            {
                _owner._api.WriteExactGuarded(receiver, cls, name, type, A.Value(original), () => { VerifyLocal(); beforeDispatch(); }); return 0;
            });
            if (!EqualityComparer<T>.Default.Equals(Value<T>(receiver, cls, name, type, offset), original))
                throw new InvalidOperationException("Renderer field restoration did not read back: " + name);
        }
        private void RestoreReference(NativeRuntime.RootHandle receiver, IntPtr cls, string name, string type, int offset,
            long expected, long original, Action beforeDispatch)
        {
            if (expected == original) return;
            if (ReferenceToken(receiver, cls, name, type, offset) != expected)
                throw new InvalidOperationException("Another writer changed renderer reference " + name + ".");
            var target = original == 0 ? IntPtr.Zero : Target(Root(original));
            Native(() =>
            {
                _owner._api.WriteExactGuarded(receiver, cls, name, type, A.Ref(target), () => { VerifyLocal(); beforeDispatch(); }); return 0;
            });
            if (ReferenceToken(receiver, cls, name, type, offset) != original)
                throw new InvalidOperationException("Renderer reference restoration did not read back: " + name);
        }
        private void RestoreList(SeekRendererPoolListState expected, SeekRendererPoolListState original,
            IReadOnlyList<SeekRendererGraphSnapshot> recognized, Action beforeDispatch)
        {
            if (SeekRendererState.Same(expected, original)) return;
            var list = Root(original.Reference); var backing = Root(original.Backing);
            if (!SeekRendererState.Same(ReadRendererList(list), expected))
                throw new InvalidOperationException("Another writer changed the retained renderer pool List.");
            var layout = Native(() => _owner._api.InspectRendererChildListClass(ClassOf(list)));
            var arrayClass = Native(() => _owner._api.Class("mscorlib.dll", "System", "Array"));
            var set = Native(() => _owner._api.InspectDeclaredMethod(arrayClass, "SetValue",
                new[] { "System.Object", "System.Int32" }, "System.Void", IntPtr.Zero));
            var get = Native(() => _owner._api.InspectDeclaredMethod(arrayClass, "GetValue",
                new[] { "System.Int32" }, "System.Object", IntPtr.Zero));
            if (set.GenericMethod || set.InflatedMethod || (set.Flags & 0x10u) != 0)
                throw new UnsupportedSignatureException("Renderer slots require the actual instance Array.SetValue(Object, Int32).");
            var priorStorage = expected.Backing == original.Backing ? expected : RetiredBackingStorage(original);
            var expectedSlots = priorStorage.CapacitySlots.ToArray();
            if (!ReadRendererArray(backing, layout.ArrayClass, _childClass).SequenceEqual(expectedSlots))
                throw new InvalidOperationException("The original retained List array differs from its last recognized producer state.");
            long ReadSlot(int index)
            {
                long observed = 0; var published = false; var receiver = Target(backing);
                Native(() =>
                {
                    _owner._api.InvokeReferenceWithPublication(get.MethodInfo, receiver, pointer =>
                    {
                        if (published) throw new InvalidOperationException("A renderer slot result was published twice.");
                        published = true; observed = pointer.ToInt64(); VerifyLocal();
                    }, A.Value(index)); return 0;
                });
                if (!published) throw new InvalidOperationException("The renderer slot getter did not publish its actual reference.");
                return observed;
            }
            for (var index = 0; index < expectedSlots.Length; index++)
            {
                if (expectedSlots[index] == original.CapacitySlots[index]) continue;
                if (ReadSlot(index) != expectedSlots[index])
                    throw new InvalidOperationException("Another writer changed renderer pool capacity storage.");
                var target = original.CapacitySlots[index] == 0 ? IntPtr.Zero : Target(Root(original.CapacitySlots[index]));
                var receiver = Target(backing); var slot = index;
                Native(() =>
                {
                    _owner._api.InvokeVoidWithPublicationGuarded(set.MethodInfo, receiver,
                        () => { VerifyLocal(); beforeDispatch(); }, A.Ref(target), A.Value(slot)); return 0;
                });
                expectedSlots[index] = original.CapacitySlots[index];
                if (ReadSlot(index) != expectedSlots[index])
                    throw new InvalidOperationException("A restored renderer pool slot did not read back.");
            }
            RestoreValue(list, layout.Class, layout.SizeField.Name, "System.Int32", layout.SizeField.Offset,
                expected.Size, original.Size, beforeDispatch);
            RestoreReference(list, layout.Class, layout.BackingField.Name, layout.BackingField.TypeName,
                layout.BackingField.Offset, expected.Backing, original.Backing, beforeDispatch);
            RestoreValue(list, layout.Class, layout.VersionField.Name, "System.Int32", layout.VersionField.Offset,
                expected.Version, original.Version, beforeDispatch);
            if (!SeekRendererState.Same(ReadRendererList(list), original))
                throw new InvalidOperationException("The original complete renderer pool List was not restored.");
        }
        private void RestoreTransform(SeekRendererTransformState expected, SeekRendererTransformState original, Action beforeDispatch)
        {
            var root = Root(original.Reference);
            if (!SeekRendererState.Same(ReadTransform(root), expected))
                throw new InvalidOperationException("Another writer changed the renderer Transform before restoration.");
            void Invoke(string name, string[] types, params A[] arguments)
            {
                try
                {
                    var method = Native(() => _owner._api.InspectDeclaredMethod(_transformClass, name, types, "System.Void", IntPtr.Zero));
                    var receiver = Target(root);
                    Native(() => { _owner._api.InvokeVoidWithPublicationGuarded(method.MethodInfo, receiver,
                        () => { VerifyLocal(); beforeDispatch(); }, arguments); return 0; });
                }
                finally { foreach (var argument in arguments) argument.Dispose(); }
            }
            if (expected.Parent != original.Parent)
                Invoke("SetParent", new[] { "UnityEngine.Transform", "System.Boolean" },
                    A.Ref(original.Parent == 0 ? IntPtr.Zero : Target(Root(original.Parent))), A.Value(false));
            Invoke("set_localPosition", new[] { "UnityEngine.Vector3" }, A.Value(original.LocalPosition));
            Invoke("set_localRotation", new[] { "UnityEngine.Quaternion" }, A.Value(original.LocalRotation));
            Invoke("set_localScale", new[] { "UnityEngine.Vector3" }, A.Value(original.LocalScale));
            if (!SeekRendererState.Same(ReadTransform(root), original))
                throw new InvalidOperationException("The original renderer Transform pose did not read back.");
        }
    }
}
