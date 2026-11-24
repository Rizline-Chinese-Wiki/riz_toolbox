using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal sealed partial class TweenCompletionFixture
{
    internal sealed partial class CompletionBackend
    {
        private readonly Dictionary<long, long> _identities = new();
        internal IntPtr ActualPlayMethod = new(700);
        internal Func<NativeLocalTweenWorkOwner, IDisposable, NativeTweenSingleEventFrame,
            NativeSingleDispatchTarget, NativeTweenTypedPlayConsumer?>? NativePlayReader;
        internal long Identity(long value) => _identities.GetValueOrDefault(value, value);
        internal void UseNativeIdentities(long tween, long singleEvent, IntPtr playMethod)
        {
            if (_owner is not null) throw new InvalidOperationException("Fixture identities must be selected before owner initialization.");
            _identities.Add(101, tween); _identities.Add(107, singleEvent); ActualPlayMethod = playMethod;
            foreach (var key in _roots.Keys.ToArray()) _roots[key] = new(Identity(_roots[key].ToInt64()));
            var info = Graph.Tweens.Single();
            Graph = Graph with { Tweens = new[] { info with { Object = tween, OnTween = singleEvent } },
                Managers = Graph.Managers.Select(manager => manager with {
                    Playing = manager.Playing! with { CapacityItems = manager.Playing.CapacityItems.Select(Identity).ToArray() },
                    Ready = manager.Ready! with { CapacityItems = manager.Ready.CapacityItems.Select(Identity).ToArray() }
                }).ToArray() };
            Native.Current.Object = new(tween); TweenObserved = TweenObserved with { ManagedReceiver = tween };
        }
    }
}
