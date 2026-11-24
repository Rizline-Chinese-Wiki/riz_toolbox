using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTweenBackend : INativeLocalTweenDynamicSourceBackend,
    INativeLocalTweenDynamicChildEventBackend, INativeLocalTweenDynamicManagerSourceBackend
{
    NativeTweenCurrentManagerSourceRoots INativeLocalTweenDynamicManagerSourceBackend.BorrowCurrentManagerSourceRoots(
        NativeLocalTweenWorkOwner owner, NativeOperationFrame frame)
    {
        _main.VerifyAccess(); RequireKnown(); owner.RequireCurrentManagerSourceBorrowing(this, frame);
        var manager = Root(frame.Self.ToInt64()); var tween = Root(frame.Arg0.ToInt64());
        if (!_ready || !ReferenceEquals(owner, _owner) || manager.Target != frame.Self || tween.Target != frame.Arg0
            || ObjectClass(frame.Self) != _managerClass || ObjectClass(frame.Arg0) != _tweenClass || !_sourceClaims.ContainsKey(frame.Arg0.ToInt64()))
            throw new InvalidOperationException("The current manager source lost its exact retained receiver, owned Tween argument or admission responsibility.");
        return new(manager, tween);
    }

    NativeTweenDynamicSourceLease INativeLocalTweenDynamicSourceBackend.AdoptDynamicSource(
        NativeLocalTweenWorkOwner owner, long tween)
    {
        _main.VerifyAccess(); RequireKnown();
        if (!ReferenceEquals(owner, _owner) || !_ready || _admissionAuthority is null || !_admissionAuthority.Production)
            throw new InvalidOperationException("Dynamic Tween promotion requires this exact active production backend.");
        owner.RequireCurrentDynamicSourceAdoption(this, tween);
        if (!_slots.TryGetValue(tween, out var slot) || _roots[slot] is not { } root || Root(tween).Target.ToInt64() != tween)
            throw new InvalidOperationException("Dynamic Tween is not an already independently retained graph object.");
        if (ObjectClass(Pointer(tween)) != _tweenClass || _sourceClaims.ContainsKey(tween))
            throw new UnsupportedSignatureException("Dynamic Tween changed class or already owns an admission lease.");
        var lease = _admissionAuthority.ClaimRetainedRoot(root, owner);
        _sourceClaims.Add(tween, lease);
        CacheAdmission(tween, lease.Read()); RequireKnown();
        return new(root, lease);
    }

    void INativeLocalTweenDynamicChildEventBackend.BorrowDynamicTweenChildEvents(
        NativeLocalTweenWorkOwner owner, long tween,
        Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle> adopt)
    {
        _main.VerifyAccess(); RequireKnown(); ArgumentNullException.ThrowIfNull(adopt);
        if (!ReferenceEquals(owner, _owner) || !_ready || !_listeners.IsFor(_launch)
            || !_slots.TryGetValue(tween, out var slot) || _roots[slot] is not { } tweenRoot)
            throw new InvalidOperationException("Dynamic child borrowing requires the active Tween backend and its existing graph root.");
        foreach (var name in new[] { "onStart", "onComplete" })
        {
            var field = _tweenFields[name];
            var identity = Reference(tween, field, nullable: true);
            if (identity != 0) adopt(tweenRoot, field, Root(identity));
            RequireKnown();
        }
    }
}
