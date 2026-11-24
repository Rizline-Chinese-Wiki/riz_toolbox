using RizToolbox.Runtime;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTweenBackend : INativeTweenChildEventSource
{
    public void BorrowTweenChildEvents(NativeLocalTweenWorkOwner owner,
        Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle> adopt)
    {
        _main.VerifyAccess(); RequireKnown(); ArgumentNullException.ThrowIfNull(adopt);
        if (!ReferenceEquals(_owner, owner) || !_attempted || _ready || !_listeners.IsFor(_launch)
            || _launch.Read().ActivationAttempted)
            throw new InvalidOperationException("Tween child event borrowing requires this exact preactivation backend.");
        foreach (var source in owner.RequireActualChildEventBorrowing(this))
        {
            var tween = Root(source);
            foreach (var name in new[] { "onStart", "onComplete" })
            {
                var field = _tweenFields[name];
                // Publish the actual field reference immediately into this
                // backend's root ledger before lending its existing strong root.
                var identity = Reference(source, field, nullable: true);
                if (identity != 0) adopt(tween, field, Root(identity));
                RequireKnown();
            }
        }
    }
}
