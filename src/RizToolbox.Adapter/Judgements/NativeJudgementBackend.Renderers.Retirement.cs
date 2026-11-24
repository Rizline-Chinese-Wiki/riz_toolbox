using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekRendererAccess
    {
        private sealed record RetirementNode(long GameObject, SeekRendererTransformState Transform, IReadOnlyList<long> Children);
        private sealed class ChildRetirement
        {
            internal readonly SeekRendererChildState Child;
            internal readonly RetirementNode[] Tree;
            internal SeekRendererChildRetirement Journal;
            internal ChildRetirement(SeekRendererChildState child, RetirementNode[] tree)
            {
                Child = child; Tree = tree;
                Journal = new(child.Reference, tree[0].GameObject, child.Transform, child.TargetComponent,
                    Array.AsReadOnly(tree.Select(node => node.GameObject).ToArray()),
                    Array.AsReadOnly(tree.Select(node => node.Transform.Reference).ToArray()), false, false, false, false, null);
            }
        }
        private readonly Dictionary<long, ChildRetirement> _childRetirements = new();
        private readonly HashSet<long> _originalRendererChildren = new(), _protectedRendererTransforms = new(), _protectedRendererObjects = new();
        private IntPtr _retirementGameObjectClass;
        private NativeMetadataMethod? _retirementGameObject, _retirementTransform, _retirementChildCount, _retirementGetChild, _retirementDestroy;

        public IReadOnlyList<SeekRendererChildRetirement> Retirements
        {
            get { VerifyOwnerThread(); return Array.AsReadOnly(_childRetirements.Values.Select(value => value.Journal).ToArray()); }
        }

        private void PrepareRetirementBaseline(SeekRendererGraphSnapshot baseline)
        {
            _retirementGameObjectClass = Native(() => _owner._api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "GameObject"));
            _retirementGameObject = RetirementMethod(_componentClass, "get_gameObject", "UnityEngine.GameObject");
            _retirementTransform = RetirementMethod(_retirementGameObjectClass, "get_transform", "UnityEngine.Transform");
            _retirementChildCount = RetirementMethod(_transformClass, "get_childCount", "System.Int32");
            _retirementGetChild = RetirementMethod(_transformClass, "GetChild", "UnityEngine.Transform", "System.Int32");
            _retirementDestroy = Native(() => _owner._api.InspectDeclaredStaticMethod(_owner._object, "DestroyImmediate",
                new[] { "UnityEngine.Object", "System.Boolean" }, "System.Void", IntPtr.Zero));
            if (_retirementDestroy.GenericMethod || _retirementDestroy.InflatedMethod || (_retirementDestroy.Flags & 0x10u) == 0)
                throw new UnsupportedSignatureException("Renderer retirement requires actual Object.DestroyImmediate(Object, Boolean).");
            _originalRendererChildren.UnionWith(baseline.Children.Select(child => child.Reference));
            _protectedRendererTransforms.UnionWith(baseline.Transforms.Select(transform => transform.Reference));
            // Protect the actual original component/scene identities as well as
            // the model's Transform closure. No whole-scene scan is performed.
            var components = baseline.Notes.Select(note => note.Reference).Concat(baseline.Pools.Select(pool => pool.Reference))
                .Concat(baseline.Children.SelectMany(child => new[] { child.Reference, child.TargetComponent }))
                .Concat(baseline.Transforms.Select(transform => transform.Reference))
                .Concat(new[] { _controllerAddress, _informationAddress, _progressAddress, _pauseAddress, _controlAddress });
            foreach (var reference in components.Where(reference => reference != 0).Distinct())
            {
                var component = Root(reference); var transform = ComponentTransform(component);
                _protectedRendererTransforms.Add(Target(transform).ToInt64());
                _protectedRendererObjects.Add(Target(ComponentGameObject(component)).ToInt64());
            }
            // Existing children include each original prefab. Freeze their
            // descendants too: a new clone cannot adopt an original subtree.
            foreach (var child in baseline.Children)
                foreach (var node in ReadRetirementTree(child.Transform))
                { _protectedRendererTransforms.Add(node.Transform.Reference); _protectedRendererObjects.Add(node.GameObject); }
            VerifySource();
        }

        private NativeMetadataMethod RetirementMethod(IntPtr cls, string name, string returns, params string[] parameters)
        {
            var method = Native(() => _owner._api.InspectDeclaredMethod(cls, name, parameters, returns, IntPtr.Zero));
            if (method.GenericMethod || method.InflatedMethod || (method.Flags & 0x10u) != 0)
                throw new UnsupportedSignatureException("The actual renderer retirement method changed signature: " + name);
            return method;
        }
        private void VerifyRetirementMethods()
        {
            foreach (var saved in new[] { _retirementGameObject!, _retirementTransform!, _retirementChildCount!, _retirementGetChild!, _retirementDestroy! })
            {
                var current = Native(() => (saved.Flags & 0x10u) != 0
                    ? _owner._api.InspectDeclaredStaticMethod(saved.DeclaringClass.Class, saved.Name, saved.ParameterTypes.ToArray(), saved.ReturnType, IntPtr.Zero)
                    : _owner._api.InspectDeclaredMethod(saved.DeclaringClass.Class, saved.Name, saved.ParameterTypes.ToArray(), saved.ReturnType, IntPtr.Zero));
                if (current.MethodInfo != saved.MethodInfo || current.DeclaringClass != saved.DeclaringClass
                    || current.Entry != saved.Entry || current.Flags != saved.Flags || current.ImplementationFlags != saved.ImplementationFlags
                    || current.GenericMethod || current.InflatedMethod)
                    throw new UnsupportedSignatureException("An actual renderer retirement method was replaced: " + saved.Name);
            }
        }
        private NativeRuntime.RootHandle RetirementReference(NativeRuntime.RootHandle receiver, NativeMetadataMethod method,
            IntPtr expectedClass, params A[] arguments)
        {
            NativeRuntime.RootHandle? result = null; var published = false;
            try
            {
                var target = Target(receiver);
                Native(() =>
                {
                    _owner._api.InvokeReferenceWithPublication(method.MethodInfo, target, pointer =>
                    {
                        if (published) throw new InvalidOperationException("A renderer retirement getter published twice.");
                        published = true;
                        if (pointer != IntPtr.Zero) result = Adopt(pointer); else VerifyLocal();
                    }, arguments); return 0;
                });
                if (!published || result is null || ClassOf(result) != expectedClass || !Alive(result))
                    throw new InvalidOperationException("A renderer retirement getter did not return its exact live object.");
                return result;
            }
            finally { foreach (var argument in arguments) argument.Dispose(); }
        }
        private NativeRuntime.RootHandle ComponentGameObject(NativeRuntime.RootHandle component)
        {
            Native(() => { _owner._api.RequireAssignable(_componentClass, ClassOf(component)); return 0; });
            if (!Alive(component)) throw new InvalidOperationException("A renderer retirement Component has died.");
            return RetirementReference(component, _retirementGameObject!, _retirementGameObjectClass);
        }
        private RetirementNode[] ReadRetirementTree(long rootTransform)
        {
            var queue = new Queue<(long Transform, long? Parent)>(); queue.Enqueue((rootTransform, null));
            var seen = new HashSet<long>(); var objects = new HashSet<long>(); var nodes = new List<RetirementNode>();
            while (queue.Count != 0)
            {
                var item = queue.Dequeue();
                if (!seen.Add(item.Transform) || seen.Count > NativeSeekRendererCommands.MaximumTotalCapacity)
                    throw new InvalidOperationException("The actual renderer subtree is cyclic, shared or exceeds its complete bound.");
                var transform = Root(item.Transform); var state = ReadTransform(transform);
                if (item.Parent is long parent && state.Parent != parent)
                    throw new InvalidOperationException("A renderer subtree child no longer belongs to its observed parent.");
                var gameObject = ComponentGameObject(transform); var objectId = Target(gameObject).ToInt64();
                if (!objects.Add(objectId) || Target(RetirementReference(gameObject, _retirementTransform!, _transformClass)).ToInt64() != item.Transform)
                    throw new InvalidOperationException("A renderer subtree reuses another GameObject or Transform.");
                var count = Native(() => _owner._api.InvokeValueWithPublication<int>(_retirementChildCount!.MethodInfo, Target(transform)));
                if (count < 0 || count > NativeSeekRendererCommands.MaximumTotalCapacity - seen.Count - queue.Count)
                    throw new NotSupportedException("The complete renderer subtree exceeds its retained object budget.");
                var children = new long[count];
                for (var index = 0; index < count; index++)
                {
                    var child = RetirementReference(transform, _retirementGetChild!, _transformClass, A.Value(index));
                    children[index] = Target(child).ToInt64(); queue.Enqueue((children[index], item.Transform));
                }
                if (Native(() => _owner._api.InvokeValueWithPublication<int>(_retirementChildCount!.MethodInfo, Target(transform))) != count
                    || !SeekRendererState.Same(ReadTransform(transform), state))
                    throw new InvalidOperationException("The actual renderer subtree changed during complete capture.");
                nodes.Add(new(objectId, state, Array.AsReadOnly(children)));
            }
            return nodes.ToArray();
        }
        private static bool SameRetirementTree(IReadOnlyList<RetirementNode> expected, IReadOnlyList<RetirementNode> actual, bool allowRootPose = false)
            => expected.Count == actual.Count && expected.Zip(actual).Select((pair, index) => pair.First.GameObject == pair.Second.GameObject
                && (allowRootPose && index == 0 ? pair.First.Transform.Reference == pair.Second.Transform.Reference
                    : SeekRendererState.Same(pair.First.Transform, pair.Second.Transform))
                && pair.First.Children.SequenceEqual(pair.Second.Children)).All(value => value);

        private void ObserveNewChild(SeekRendererChildState child)
        {
            if (_restorationBaseline is null || _originalRendererChildren.Contains(child.Reference) || _childRetirements.ContainsKey(child.Reference)) return;
            // Capture on the first actual post-dispatch observation, before its
            // producer model can accept the new child. Restore never invents a
            // replacement ownership tree from whatever is attached later.
            var tree = ReadRetirementTree(child.Transform);
            var candidate = new ChildRetirement(child, tree); _childRetirements.Add(child.Reference, candidate);
            try
            {
                VerifyChildRetirement(candidate, requireRecognized: false);
                if (!SameRetirementTree(tree, ReadRetirementTree(child.Transform)))
                    throw new InvalidOperationException("The first observed clone subtree changed while its ownership was captured.");
            }
            catch (Exception error) { candidate.Journal = candidate.Journal with { Failure = error.Message }; throw; }
        }
        public void AcceptRecognizedAllocations(SeekRendererGraphSnapshot observed, IReadOnlyList<SeekRendererRetiredBackingState> retiredBackings)
        {
            VerifyLocal();
            if (_restorationBaseline is null || observed.Identity != _restorationBaseline.Identity || _restorationEntered)
                throw new InvalidOperationException("Only the prepared renderer producer can accept its observed clone ownership.");
            foreach (var child in observed.Children.Where(child => !_originalRendererChildren.Contains(child.Reference)))
            {
                if (!_childRetirements.TryGetValue(child.Reference, out var candidate) || candidate.Journal.Failure is not null
                    || candidate.Child.Transform != child.Transform || candidate.Child.ParentPool != child.ParentPool
                    || candidate.Child.TargetComponent != child.TargetComponent || candidate.Child.TargetComponentClass != child.TargetComponentClass)
                    throw new InvalidOperationException("The recognized child has no matching first-observation subtree owner.");
                candidate.Journal = candidate.Journal with { Recognized = true };
            }
            AcceptRetiredBackings(observed, retiredBackings);
        }
        private void VerifyChildRetirement(ChildRetirement candidate, bool requireRecognized)
        {
            if (candidate.Journal.Failure is not null || requireRecognized && !candidate.Journal.Recognized
                || candidate.Journal.NativeEntered || _originalRendererChildren.Contains(candidate.Child.Reference))
                throw new InvalidOperationException("This child has no current, unconsumed recognized clone retirement.");
            var tree = candidate.Tree;
            if (tree.Any(node => _protectedRendererTransforms.Contains(node.Transform.Reference) || _protectedRendererObjects.Contains(node.GameObject))
                || _childRetirements.Values.Where(other => !ReferenceEquals(other, candidate))
                    .Any(other => other.Tree.Any(node => tree.Any(own => own.Transform.Reference == node.Transform.Reference || own.GameObject == node.GameObject))))
                throw new InvalidOperationException("A new renderer subtree overlaps an original object, prefab or another clone owner.");
            var child = Root(candidate.Child.Reference);
            if (Target(ComponentGameObject(child)).ToInt64() != candidate.Journal.GameObject
                || Target(ComponentTransform(child)).ToInt64() != candidate.Child.Transform)
                throw new InvalidOperationException("The original new child changed its GameObject or Transform.");
            if (candidate.Child.TargetComponent != 0)
            {
                var component = Root(candidate.Child.TargetComponent);
                if (ClassOf(component).ToInt64() != candidate.Child.TargetComponentClass
                    || !tree.Any(node => node.GameObject == Target(ComponentGameObject(component)).ToInt64()
                        && node.Transform.Reference == Target(ComponentTransform(component)).ToInt64()))
                    throw new InvalidOperationException("The clone target component is outside its originally observed subtree.");
            }
            // The native Appear/Disappear model owns the root's parent/TRS and
            // the surrounding exact snapshot verifies its latest recognized pose.
            // Descendant membership and local poses stay on their first record.
            if (!SameRetirementTree(tree, ReadRetirementTree(candidate.Child.Transform), allowRootPose: true))
                throw new InvalidOperationException("The clone subtree differs from its first observed owned shape.");
        }

        private void DestroyRetainedClone(ChildRetirement candidate, Action beforeDispatch)
        {
            try
            {
                VerifyReady(); VerifyRetirementMethods(); VerifyChildRetirement(candidate, requireRecognized: true);
                // Unity recommends delayed Destroy at runtime. This experimental
                // synchronous inverse is confined to this ready-paused owner-thread
                // transaction; callback-stage restrictions may still reject it.
                // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.DestroyImmediate.html
                // A rejected/unknown call is never retried. Returned is not death.
                using var target = A.Ref(Target(Root(candidate.Journal.GameObject)));
                using var allowAssets = A.Value(false);
                Native(() =>
                {
                    _owner._api.InvokeVoidWithPublicationAdmission(_retirementDestroy!.MethodInfo, IntPtr.Zero, VerifyLocal, () =>
                    {
                        VerifyLocal();
                        if (candidate.Journal.NativeEntered) throw new InvalidOperationException("Clone retirement may dispatch only once.");
                        beforeDispatch(); candidate.Journal = candidate.Journal with { NativeEntered = true };
                    }, target, allowAssets);
                    candidate.Journal = candidate.Journal with { NativeReturned = true }; return 0;
                });
                var dead = true;
                foreach (var reference in candidate.Tree.SelectMany(node => new[] { node.GameObject, node.Transform.Reference })
                    .Concat(new[] { candidate.Child.Reference, candidate.Child.TargetComponent }).Where(reference => reference != 0).Distinct())
                    if (Alive(Root(reference))) dead = false; // Read every retained object independently.
                if (!dead) throw new InvalidOperationException("DestroyImmediate returned without independent death of the complete retained clone.");
                VerifyReady(); candidate.Journal = candidate.Journal with { DeathObserved = true };
                // Only an actually dead owned clone leaves snapshot membership.
                // Its independent roots remain in this access until normal Dispose.
                _knownChildren.Remove(candidate.Child.Reference);
                foreach (var members in _knownPoolChildren.Values) members.Remove(candidate.Child.Reference);
                foreach (var node in candidate.Tree)
                {
                    _retainedTransforms.Remove(node.Transform.Reference);
                    foreach (var members in _selectedTransforms.Values) members.Remove(node.Transform.Reference);
                }
            }
            catch (Exception error) { candidate.Journal = candidate.Journal with { Failure = error.Message }; throw; }
        }
    }
}
