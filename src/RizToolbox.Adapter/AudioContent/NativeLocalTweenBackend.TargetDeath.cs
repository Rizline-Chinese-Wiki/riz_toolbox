using RizToolbox.Runtime;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTweenBackend : INativeTweenTargetDeathBackend
{
    public NativeTweenTargetBinding BorrowTweenTargetBinding(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle tweenRoot)
    {
        _main.VerifyAccess(); RequireKnown();
        if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(Root(tweenRoot.Target.ToInt64()), tweenRoot))
            throw new InvalidOperationException("Target borrowing crossed its exact retained Tween/backend root.");
        owner.RequireTweenTargetBaseline(tweenRoot);
        var component = Reference(tweenRoot.Target.ToInt64(), _tweenFields["targetComponent"], true);
        if (component == 0) return new(tweenRoot, null, null, null, 0);
        var componentRoot = Root(component);
        var sample = Native(() => _api.ObserveRootedTweenTargetDeath(componentRoot, _module));
        if (!sample.Alive) return new(tweenRoot, componentRoot, null, null, 0);
        var links = ReadTargetLinks(componentRoot);
        return new(tweenRoot, componentRoot, Root(links.GameObject), Root(links.Transform), links.Parent);
    }

    private (long GameObject, long Transform, long Parent) ReadTargetLinks(NativeRuntime.RootHandle componentRoot)
    {
        var component = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component"));
        var gameObject = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "GameObject"));
        var transform = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform"));
        _api.RequireAssignable(component, ObjectClass(componentRoot.Target)); RequireKnown();
        long Call(NativeRuntime.RootHandle root, IntPtr cls, string name, string returns, int rva, bool nullable = false)
        {
            var method = Native(() => _api.InspectDeclaredMethod(cls, name, Array.Empty<string>(), returns, _module));
            if (method.Entry?.Rva != rva || method.GenericMethod || method.InflatedMethod || (method.Flags & (0x10u | 0x400u)) != 0)
                throw new UnsupportedSignatureException("Target hierarchy getter differs from the pinned non-generic instance declaration: " + name);
            long result = 0;
            _api.InvokeReferenceWithPublication(method.MethodInfo, root.Target,
                pointer => result = Hold(pointer, IntPtr.Zero, nullable, published: true)); RequireKnown();
            return result;
        }
        var go = Call(componentRoot, component, "get_gameObject", "UnityEngine.GameObject", 0x1536400);
        var tr = Call(Root(go), gameObject, "get_transform", "UnityEngine.Transform", 0x1539010);
        var parent = Call(Root(tr), transform, "get_parent", "UnityEngine.Transform", 0x1560470, true);
        return (go, tr, parent);
    }

    public NativeTweenTargetDeathObservation ReadTweenTargetDeath(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle root)
    {
        _main.VerifyAccess(); RequireKnown();
        if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(Root(root.Target.ToInt64()), root))
            throw new InvalidOperationException("Target death cannot substitute a handle or foreign backend.");
        return Native(() => _api.ObserveRootedTweenTargetDeath(root, _module));
    }

    public void VerifyTweenTargetBinding(NativeLocalTweenWorkOwner owner, NativeTweenTargetBinding binding)
    {
        _main.VerifyAccess(); RequireKnown();
        if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(Root(binding.TweenRoot.Target.ToInt64()), binding.TweenRoot)
            || Reference(binding.TweenRoot.Target.ToInt64(), _tweenFields["targetComponent"], true) != (binding.Component?.Target.ToInt64() ?? 0))
            throw new InvalidOperationException("The actual Tween targetComponent field changed its captured target identity.");
        if (binding.Component is null) return;
        var current = ReadTweenTargetDeath(owner, binding.Component);
        if (!current.Alive) return; // Never call a getter on a dead native object.
        var links = ReadTargetLinks(binding.Component);
        if (binding.GameObject?.Target.ToInt64() != links.GameObject || binding.Transform?.Target.ToInt64() != links.Transform
            || links.Parent != binding.ParentTransform)
            throw new InvalidOperationException("The concrete target changed GameObject/Transform or was reparented.");
    }
}
