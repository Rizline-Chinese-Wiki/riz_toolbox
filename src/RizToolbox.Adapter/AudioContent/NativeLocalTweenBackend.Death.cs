using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTweenBackend : INativeLocalTweenDeathBackend
{
    public void VerifyTweenDeathOwner(NativeLocalTweenWorkOwner owner)
    {
        _main.VerifyAccess(); RequireKnown();
        if (!ReferenceEquals(_owner, owner) || !_attempted || !_listeners.IsFor(_launch)
            || _admissionAuthority is null || !_admissionAuthority.Production)
            throw new InvalidOperationException("Tween death observation crossed its actual source backend and native admission owner.");
    }
    public void BorrowTweenDeathSources(NativeLocalTweenWorkOwner owner,
        Action<NativeRuntime.RootHandle, NativeTweenAdmissionLease> borrow)
    {
        VerifyTweenDeathOwner(owner); ArgumentNullException.ThrowIfNull(borrow);
        if (_ready || _launch.Read().ActivationAttempted)
            throw new InvalidOperationException("Tween death live baselines must be retained before activation.");
        foreach (var source in owner.RequireActualTweenDeathCapture(this))
        {
            if (!_sourceClaims.TryGetValue(source, out var lease))
                throw new InvalidOperationException("The actual Tween source lost its independent native admission lease.");
            borrow(Root(source), lease); RequireKnown();
        }
    }
    public NativeTweenDeathObservation ReadTweenDeathObservation(NativeRuntime.RootHandle root)
    {
        _main.VerifyAccess(); RequireKnown(); ArgumentNullException.ThrowIfNull(root);
        if (_owner is null)
            throw new InvalidOperationException("Tween death backend has no retained actual work owner.");
        VerifyTweenDeathOwner(_owner);
        var identity = root.Target.ToInt64();
        if (!_slots.TryGetValue(identity, out var slot) || !ReferenceEquals(_roots[slot], root) || !_sourceClaims.ContainsKey(identity))
            throw new InvalidOperationException("Tween death cannot read a substituted root or an unowned source.");
        return Native(() => _api.ObserveRootedTweenDeath(root, _module));
    }
}
