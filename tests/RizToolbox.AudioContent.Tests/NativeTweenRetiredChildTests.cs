using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using static Assert;

// Exercise the actual child adoption and E retirement state machines. The
// listener owns independent RootHandles; no final receipt is copied or forged.
internal static class NativeTweenRetiredChildTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Retired Tween child registrations keep the listener independent roots and reject borrowing the freed lender", IndependentHolder),
        ("Retired Tween child registrations reject Free reentry and a failed final native cut release without touching lender roots", UnconfirmedRelease),
        ("Retired Tween child registrations reject foreign listeners and wrong slots after the actual final receipt", ForeignRegistration)
    };

    private static void IndependentHolder()
    {
        using var f = new TweenCompletionFixture();
        using var events = Adopt(f);
        Check(events.Memberships.Count == 2 && events.RootRecords == 3);
        foreach (var membership in events.Memberships)
            membership.RequireIndependentRegistration(events.Owner, membership.EventSlot!.Value);

        f.ConfirmDeaths(); f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Roots.Retired && f.Backend.RootRecords == 0
            && f.Backend.CutNative.Commits == 1 && f.Backend.CutNative.Releases == 1
            && f.Backend.RootManifest.All(row => row.Freed && row.FreeAttempts == 1));
        f.Backend.RefuseRootReads = true;
        foreach (var membership in events.Memberships)
        {
            var slot = membership.EventSlot!.Value;
            membership.RequireIndependentRegistration(events.Owner, slot);
            Check(events.ReadGraph(slot).Event == membership.Event);
            RejectLenderBorrow(() => membership.RequireFor(events.Owner));
            RejectLenderBorrow(() => { _ = membership.BorrowEventRoot(events.Owner); });
        }
        Check(!events.Owner.Unknown && events.Owner.Read().Events == 3 && events.RootRecords == 3);
    }

    private static void UnconfirmedRelease()
    {
        using var f = new TweenCompletionFixture();
        using var events = Adopt(f);
        f.ConfirmDeaths(); var freeCallbacks = 0;
        f.Backend.BeforeRootFree = released => {
            Check(f.Owner.RetiredRootsUnavailable && !f.Owner.CanRetireWork());
            f.Backend.RefuseRootReads = true;
            foreach (var membership in events.Memberships)
            {
                RejectRegistration(() => membership.RequireIndependentRegistration(events.Owner, membership.EventSlot!.Value),
                    "Tween child registration cannot cross an unconfirmed retirement.");
                RejectLenderBorrow(() => membership.RequireFor(events.Owner));
                RejectLenderBorrow(() => { _ = membership.BorrowEventRoot(events.Owner); });
            }
            ++freeCallbacks;
        };
        var releaseChecked = false;
        f.Backend.CutNative.BeforeRelease = () => {
            Check(f.Backend.RootManifest.All(row => row.Freed) && !f.Owner.CanRetireWork());
            foreach (var membership in events.Memberships)
                RejectRegistration(() => membership.RequireIndependentRegistration(events.Owner, membership.EventSlot!.Value),
                    "Tween child registration cannot cross an unconfirmed retirement.");
            releaseChecked = true;
        };
        f.Backend.CutNative.ReleaseFailure = true;
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.Unknown && !f.Owner.CanRetireWork() && releaseChecked && freeCallbacks == f.Backend.RootManifest.Count
            && f.Backend.CutNative.Releases == 1 && f.Backend.RootRecords == 0);
        foreach (var membership in events.Memberships)
        {
            RejectRegistration(() => membership.RequireIndependentRegistration(events.Owner, membership.EventSlot!.Value),
                "Tween child registration has no exact published listener slot.");
            Check(events.ReadGraph(membership.EventSlot!.Value).Event == membership.Event);
        }
        var attempts = freeCallbacks; f.Owner.PumpControlledTweenRetirementForTests();
        Check(freeCallbacks == attempts && f.Backend.CutNative.Acquires == 1 && !events.Owner.Unknown);
    }

    private static void ForeignRegistration()
    {
        using var f = new TweenCompletionFixture();
        using var events = Adopt(f);
        using var foreign = new ListenerBackend(f.Main);
        f.ConfirmDeaths(); f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.CanRetireWork()); f.Backend.RefuseRootReads = true;
        foreach (var membership in events.Memberships)
        {
            membership.RequireIndependentRegistration(events.Owner, membership.EventSlot!.Value);
            RejectRegistration(() => membership.RequireIndependentRegistration(foreign.Owner, membership.EventSlot!.Value),
                "Tween child membership crossed its private owner, launch, source roots or event identity.");
            RejectRegistration(() => membership.RequireIndependentRegistration(events.Owner, -1),
                "Tween child registration has no exact published listener slot.");
            RejectRegistration(() => membership.RequireIndependentRegistration(events.Owner, membership.EventSlot!.Value + 10),
                "Tween child registration has no exact published listener slot.");
        }
        Check(!f.Owner.Unknown && !events.Owner.Unknown && !foreign.Owner.Unknown);
    }

    private static ListenerBackend Adopt(TweenCompletionFixture f)
    {
        var events = new ListenerBackend(f.Main);
        f.Owner.AdoptControlledTweenChildEventsForTests(events.Owner);
        Check(events.Owner.ReadTweenChildRegistrations().Count == 2
            && events.Owner.ReadTweenChildRegistrations().All(row => row.RootAdopted && row.SlotRetained && row.GraphCaptured && !row.Failed));
        return events;
    }
    private static void RejectLenderBorrow(Action read)
        => RejectRegistration(read, "Tween source observation is Unknown or its retained roots have entered retirement.");
    private static void RejectRegistration(Action read, string expected)
    {
        try { read(); }
        catch (InvalidOperationException error) when (error.Message == expected) { return; }
        throw new Exception("The child registration did not reject before consulting its former lender roots.");
    }

    private sealed class ListenerBackend : ICutoutListenerBackend, INativeTweenChildEventBackend, IDisposable
    {
        private static long _module = 0x7D0000000;
        private readonly MainThreadDispatcher _main;
        private readonly Dictionary<uint, IntPtr> _handles = new();
        private readonly Dictionary<long, NativeRuntime.RootHandle> _roots = new();
        private readonly Dictionary<int, long> _events = new();
        private uint _next;
        internal readonly NativeLocalUnityEventLedger Owner;
        internal readonly List<NativeLocalTweenWorkOwner.ChildEventMembership> Memberships = new();
        public bool CleanupUnknown => false;
        public int RootRecords => _roots.Count;
        internal ListenerBackend(MainThreadDispatcher main)
        {
            _main = main;
            Owner = NativeLocalUnityEventLedger.CaptureBackend(main, this, new Authority(), Interlocked.Add(ref _module, 0x10000000));
        }
        public void Initialize(NativeLocalUnityEventLedger owner)
        { _events.Add(owner.RegisterEvent(901), 901); Retain(901); }
        private void Retain(long identity)
        {
            if (_roots.ContainsKey(identity)) return;
            var key = ++_next; _handles.Add(key, new(identity));
            _roots.Add(identity, new(key, id => { if (!_handles.Remove(id)) throw new InvalidOperationException("Listener root already freed."); },
                id => _handles.GetValueOrDefault(id), _main.VerifyAccess));
        }
        public long AdoptBorrowedTweenChildEventRoot(NativeLocalUnityEventLedger owner, NativeLocalTweenWorkOwner.ChildEventMembership membership)
        {
            Check(ReferenceEquals(owner, Owner));
            var identity = membership.BorrowEventRoot(owner).Target.ToInt64();
            Retain(identity); Memberships.Add(membership); return identity;
        }
        public void RetainTweenChildEventSlot(NativeLocalUnityEventLedger owner, NativeLocalTweenWorkOwner.ChildEventMembership membership, int slot, long identity)
        { membership.RequireFor(owner); Check(ReferenceEquals(owner, Owner) && _roots.ContainsKey(identity)); _events[slot] = identity; }
        public NativeUnityEventGraph ReadGraph(int eventSlot)
        {
            var identity = _events[eventSlot]; Check(_roots[identity].Target.ToInt64() == identity);
            return new(identity, identity + 1000, identity + 2000, false, false,
                new(identity + 3000, 1, Array.Empty<NativeUnityEventInvokable>()),
                new(identity + 4000, 1, Array.Empty<NativeUnityEventInvokable>()),
                new(identity + 5000, 1, Array.Empty<NativeUnityEventInvokable>()),
                identity + 6000, 1, Array.Empty<NativeUnityEventPersistent>(), 0);
        }
        public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList)
        { var graph = ReadGraph(eventSlot); Check(graph.ExecutingCalls.Object == returnedList); return new(graph, graph.ExecutingCalls); }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
        public void Dispose() { foreach (var root in _roots.Values) root.Dispose(); _roots.Clear(); }
    }
    private sealed class Authority : ICutoutListenerAuthority
    {
        public bool Unknown => false;
        public void VerifyBeforeCapture() { }
        public void VerifyObservation() { }
        public bool TryConfirmSourceIdle() => true;
        public void ObserveFaultFromAnyThread() { }
    }
}

internal sealed partial class TweenCompletionFixture
{
    internal sealed partial class CompletionBackend : INativeTweenChildEventSource
    {
        public void BorrowTweenChildEvents(NativeLocalTweenWorkOwner owner,
            Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle> adopt)
        {
            _main.VerifyAccess(); Check(ReferenceEquals(owner, _owner));
            var field = new NativeExactReadField(new(0x88), new(301), new(302), new(303),
                "onStart", "UnityEngine.Events.UnityEvent", false, 0x88, 8, 0);
            adopt(Tween, field, Root(new(105)));
            adopt(Tween, field with { Field = new(0x90), Name = "onComplete", Offset = 0x90 }, Root(new(106)));
        }
    }
}
