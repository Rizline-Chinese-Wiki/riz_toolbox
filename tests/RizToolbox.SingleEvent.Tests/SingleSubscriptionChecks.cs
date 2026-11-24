using System.Runtime.CompilerServices;
using RizToolbox.Runtime.Hooks;

// These execute the production subscription hub's delegates and the actual scope's
// complete/dispose paths. A copied frame alone cannot satisfy any of these tests.
internal static class SingleSubscriptionChecks
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Single subscription withdrawal rejects new parents and releases its idle run closures", IdleWithdrawal),
        ("Single borrowed parent keeps both callbacks after withdrawal and drains only after actual exit", BorrowedWithdrawal),
        ("Single one physical parent freezes distinct subscriptions and rejects duplicate enrollment", DistinctSubscribers),
        ("Single foreign and nested keys cannot dispatch an owned ancestor or an ended run", ForeignAndNested),
        ("Single self withdrawal and parent close retain the currently executing callback", SelfWithdrawal),
        ("Single peer withdrawal cannot erase another callback already selected by the snapshot", SelectedPeer),
        ("Single selected peer failure keeps its exact fault delegate after withdrawal and close", SelectedPeerFault),
        ("Single closed parent blocks reentrant dispatch while its selected callback finishes", ClosedReentry),
        ("Single nested callbacks restore outer responsibility and complete each scope once", NestedScopes),
        ("Single same receiver across two runs cannot read the retired run callback", TwoRuns),
        ("Single callback failure remains Unknown while already captured completion still executes once", CallbackFailure),
        ("Single faulted active subscription rejects new parents while its old scope still cleans up", UnknownEnrollment),
        ("Single incomplete capture remains retained after withdrawal and reports the retained fault", CaptureFailure),
        ("Single fault broadcast snapshots live and borrowed delegates despite reentrant withdrawal", FaultBroadcast),
        ("Single mismatched actual exit is rejected before invoking consumer completion", WrongExit),
        ("Single duplicate completion never repeats completion or cleanup and remains Unknown", DoubleComplete),
        ("Single duplicate disposal never repeats cleanup and remains Unknown", DoubleDispose),
        ("Single context disposal without actual exit cannot certify subscriber drainage", NoExit),
        ("Single consumer completion failure still attempts context cleanup exactly once", CompletionFailure),
        ("Single consumer disposal failure cannot repeat the cleanup attempt or report known drainage", DisposalFailure),
        ("Single suspended callback resumes after withdrawal without losing selected cleanup responsibility", SuspendedCallback),
        ("Single suspended callback fault survives parent closure and cannot become successful drainage", SuspendedFault),
        ("Single permanent hub cannot anchor a withdrawn and fully completed run", ReleasedRunCollection)
    };
    private sealed class Payload { }
    private sealed class Context : IDisposable
    {
        internal int Completed, Disposals;
        internal bool ThrowComplete, ThrowDispose;
        internal void Complete()
        { ++Completed; if (ThrowComplete) throw new IOException("consumer completion failed"); }
        public void Dispose()
        { ++Disposals; if (ThrowDispose) throw new IOException("consumer disposal failed"); }
    }
    private sealed class RunTarget
    {
        internal int Observations, Faults;
        internal void Observe(Payload _) => ++Observations;
        internal void Fault(string _) => ++Faults;
    }
    private static NativeTweenSingleParentKey Key(ulong sequence = 1, ulong token = 11)
        => new(5, 7, sequence, token, new(101), new(111), new(121), 0x7fc01234, 19);
    private static NativeOperationFrame Frame(ulong sequence = 1)
        => new() { Version = 1, Size = 64, Kind = NativeOperationKind.Single1, Self = new(101),
            Method = new(111), Caller = new(121), Arg0 = new(0x7fc01234), Sequence = sequence };
    private static NativeTweenSingleEventScope<Payload> Scope(Context context,
        NativeTweenSingleEventSubscriptions<Payload>.ParentLoan loan, ulong sequence = 1) => new(context, Frame(sequence), loan);
    private static void Complete(NativeTweenSingleEventScope<Payload> scope, ulong sequence = 1)
        => scope.Complete(Frame(sequence), NativeOperationExitFlags.Returned, (context, _, _) => ((Context)context).Complete());
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Single subscription invariant failed."); }
    private static void Throws(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } catch (IOException) { return; } throw new InvalidOperationException("Expected rejection."); }
    private static void Collect()
    { for (var i = 0; i < 3; ++i) { GC.Collect(); GC.WaitForPendingFinalizers(); } }

    private static void IdleWithdrawal()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>();
        var (subscription, target) = SubscribeTarget(hub);
        subscription.Dispose(); subscription.Dispose(); Collect();
        Check(subscription.IsDrained && !target.IsAlive && subscription.ReadActivity() is { Known: true, InFlight: 0 });
        Throws(() => hub.Begin(subscription, Key())); Check(hub.Dispatch(Key(), new()) == 0);
    }
    private static void BorrowedWithdrawal()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var observations = 0; var faults = 0;
        var subscription = hub.Subscribe(_ => ++observations, _ => ++faults);
        var loan = hub.Begin(subscription, Key()); var inner = new Context(); var scope = Scope(inner, loan);
        subscription.Dispose(); Throws(() => hub.Begin(subscription, Key(2, 12)));
        Check(!subscription.IsDrained && subscription.ReadActivity() is { Known: true, InFlight: 1 });
        Check(hub.Dispatch(Key(), new()) == 0 && hub.Dispatch(Key(), new()) == 0 && observations == 2);
        Complete(scope); Check(!subscription.IsDrained); scope.Dispose();
        Check(subscription.IsDrained && inner.Completed == 1 && inner.Disposals == 1 && faults == 0);
        Check(hub.Dispatch(Key(), new()) == 0 && observations == 2);
    }
    private static void DistinctSubscribers()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var a = 0; var b = 0;
        var first = hub.Subscribe(_ => ++a, _ => { }); var second = hub.Subscribe(_ => ++b, _ => { });
        var left = Scope(new(), hub.Begin(first, Key())); Throws(() => hub.Begin(first, Key()));
        var right = Scope(new(), hub.Begin(second, Key()));
        Check(hub.Dispatch(Key(), new()) == 0 && a == 1 && b == 1);
        first.Dispose(); Complete(left); left.Dispose(); Check(first.IsDrained && !second.IsWithdrawn);
        hub.Dispatch(Key(), new()); Check(a == 1 && b == 2);
        second.Dispose(); Complete(right); right.Dispose(); Check(second.IsDrained);
    }
    private static void ForeignAndNested()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var reads = 0;
        var subscription = hub.Subscribe(_ => ++reads, _ => { }); var scope = Scope(new(), hub.Begin(subscription, Key()));
        foreach (var foreign in new[] { Key(2, 12), Key() with { Event = new(102) }, Key() with { Thread = 20 }, Key() with { ParentEpoch = 8 } })
            Check(hub.Dispatch(foreign, new()) == 0 && reads == 0);
        hub.Dispatch(Key(), new()); subscription.Dispose(); Complete(scope); scope.Dispose();
        hub.Dispatch(Key(), new()); Check(reads == 1 && subscription.IsDrained);
    }
    private static void SelfWithdrawal()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var calls = 0;
        NativeTweenSingleEventSubscriptions<Payload>.Subscription subscription = null!;
        NativeTweenSingleEventScope<Payload> scope = null!;
        subscription = hub.Subscribe(_ => {
            ++calls; subscription.Dispose(); Complete(scope); scope.Dispose();
            Check(!subscription.IsDrained && subscription.ReadActivity().InFlight == 1);
        }, _ => { }); scope = Scope(new(), hub.Begin(subscription, Key()));
        Check(hub.Dispatch(Key(), new()) == 0 && calls == 1 && subscription.IsDrained);
    }
    private static void SelectedPeer() => Peer(false);
    private static void SelectedPeerFault() => Peer(true);
    private static void Peer(bool fail)
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var a = 0; var b = 0; var faults = 0;
        NativeTweenSingleEventSubscriptions<Payload>.Subscription second = null!;
        NativeTweenSingleEventScope<Payload> right = null!;
        var first = hub.Subscribe(_ => {
            ++a; second.Dispose(); Complete(right); right.Dispose();
            Check(!second.IsDrained && second.ReadActivity().InFlight == 1);
        }, _ => { });
        second = hub.Subscribe(_ => { ++b; if (fail) throw new IOException("already selected peer failed"); }, _ => ++faults);
        var left = Scope(new(), hub.Begin(first, Key())); right = Scope(new(), hub.Begin(second, Key()));
        Check(hub.Dispatch(Key(), new()) == (fail ? 1u : 0u) && a == 1 && b == 1 && faults == (fail ? 1 : 0));
        Check(second.ReadActivity().InFlight == 0 && second.ReadActivity().Known == !fail && second.IsDrained == !fail);
        first.Dispose(); Complete(left); left.Dispose(); Check(first.IsDrained);
    }
    private static void ClosedReentry()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var calls = 0;
        NativeTweenSingleEventScope<Payload> scope = null!;
        var subscription = hub.Subscribe(_ => { ++calls; Complete(scope); scope.Dispose(); Check(hub.Dispatch(Key(), new()) == 0); }, _ => { });
        scope = Scope(new(), hub.Begin(subscription, Key())); hub.Dispatch(Key(), new()); subscription.Dispose();
        Check(calls == 1 && subscription.IsDrained);
    }
    private static void NestedScopes()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var calls = 0; var depth = 0;
        NativeTweenSingleEventSubscriptions<Payload>.Subscription subscription = null!;
        subscription = hub.Subscribe(_ => {
            ++calls; if (depth++ == 0) {
                var innerContext = new Context(); var inner = Scope(innerContext, hub.Begin(subscription, Key(2, 12)), 2);
                hub.Dispatch(Key(2, 12), new()); Complete(inner, 2); inner.Dispose();
                Check(innerContext.Completed == 1 && innerContext.Disposals == 1 && subscription.ReadActivity().InFlight > 0);
            }
        }, _ => { });
        var outerContext = new Context(); var outer = Scope(outerContext, hub.Begin(subscription, Key()));
        hub.Dispatch(Key(), new()); subscription.Dispose(); Complete(outer); outer.Dispose();
        Check(calls == 2 && outerContext.Completed == 1 && outerContext.Disposals == 1 && subscription.IsDrained);
    }
    private static void TwoRuns()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var oldReads = 0; var newReads = 0;
        var old = hub.Subscribe(_ => ++oldReads, _ => { }); var left = Scope(new(), hub.Begin(old, Key()));
        hub.Dispatch(Key(), new()); old.Dispose(); Complete(left); left.Dispose();
        var next = hub.Subscribe(_ => ++newReads, _ => { }); var right = Scope(new(), hub.Begin(next, Key(2, 12)), 2);
        hub.Dispatch(Key(), new()); hub.Dispatch(Key(3, 13), new()); hub.Dispatch(Key(2, 12), new());
        next.Dispose(); Complete(right, 2); right.Dispose(); hub.Dispatch(Key(2, 12), new());
        Check(oldReads == 1 && newReads == 1 && old.IsDrained && next.IsDrained);
    }
    private static void CallbackFailure()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var faults = 0;
        var subscription = hub.Subscribe(_ => throw new IOException("selected observer failed"), _ => ++faults);
        var inner = new Context(); var scope = Scope(inner, hub.Begin(subscription, Key())); subscription.Dispose();
        Check(hub.Dispatch(Key(), new()) == 1); Complete(scope); scope.Dispose();
        Check(faults == 1 && inner.Completed == 1 && inner.Disposals == 1 && !subscription.IsDrained
            && subscription.ReadActivity() is { Known: false, InFlight: 0 });
    }
    private static void CaptureFailure()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var faults = 0; var calls = 0;
        var subscription = hub.Subscribe(_ => ++calls, _ => ++faults); var loan = hub.Begin(subscription, Key());
        subscription.Dispose(); loan.CaptureFault("capture failed"); hub.FaultAll("later native fault");
        Check(faults == 2 && calls == 0 && !subscription.IsDrained && subscription.ReadActivity() is { Known: false, InFlight: 1 });
        Throws(loan.Bind); Check(hub.Dispatch(Key(), new()) == 1 && calls == 0 && faults == 3);
    }
    private static void UnknownEnrollment()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var faults = 0;
        var subscription = hub.Subscribe(_ => { }, _ => ++faults); var inner = new Context(); var scope = Scope(inner, hub.Begin(subscription, Key()));
        hub.FaultAll("producer unknown while actual parent is borrowed");
        Check(!subscription.IsWithdrawn && !subscription.ReadActivity().Known);
        Throws(() => hub.Begin(subscription, Key(2, 12))); Complete(scope); scope.Dispose(); subscription.Dispose();
        Check(faults == 1 && inner.Completed == 1 && inner.Disposals == 1 && !subscription.IsDrained
            && subscription.ReadActivity() is { Known: false, InFlight: 0 });
    }
    private static void FaultBroadcast()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var a = 0; var b = 0;
        NativeTweenSingleEventSubscriptions<Payload>.Subscription second = null!;
        var first = hub.Subscribe(_ => { }, _ => { ++a; second.Dispose(); });
        second = hub.Subscribe(_ => { }, _ => { ++b; throw new IOException("fault observer failed"); });
        var scope = Scope(new(), hub.Begin(second, Key())); second.Dispose(); hub.FaultAll("producer fault");
        first.Dispose(); Complete(scope); scope.Dispose();
        Check(a == 1 && b == 1 && !first.ReadActivity().Known && !second.ReadActivity().Known && !second.IsDrained);
    }
    private static void WrongExit()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var faults = 0;
        var subscription = hub.Subscribe(_ => { }, _ => ++faults); var inner = new Context(); var scope = Scope(inner, hub.Begin(subscription, Key()));
        subscription.Dispose(); Throws(() => Complete(scope, 2)); scope.Dispose();
        Check(inner.Completed == 0 && inner.Disposals == 1 && faults == 2 && !subscription.IsDrained && !subscription.ReadActivity().Known);
    }
    private static void DoubleComplete()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var faults = 0;
        var subscription = hub.Subscribe(_ => { }, _ => ++faults); var inner = new Context(); var scope = Scope(inner, hub.Begin(subscription, Key()));
        subscription.Dispose(); Complete(scope); Throws(() => Complete(scope)); scope.Dispose();
        Check(inner.Completed == 1 && inner.Disposals == 1 && faults == 1 && !subscription.IsDrained && !subscription.ReadActivity().Known);
    }
    private static void DoubleDispose()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>();
        var subscription = hub.Subscribe(_ => { }, _ => { }); var inner = new Context(); var scope = Scope(inner, hub.Begin(subscription, Key()));
        subscription.Dispose(); Complete(scope); scope.Dispose(); Throws(scope.Dispose);
        Check(inner.Completed == 1 && inner.Disposals == 1 && !subscription.IsDrained && !subscription.ReadActivity().Known);
    }
    private static void NoExit()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var faults = 0;
        var subscription = hub.Subscribe(_ => { }, _ => ++faults); var inner = new Context(); var scope = Scope(inner, hub.Begin(subscription, Key()));
        subscription.Dispose(); scope.Dispose(); Check(inner.Completed == 0 && inner.Disposals == 1 && faults == 1
            && !subscription.IsDrained && subscription.ReadActivity() is { Known: false, InFlight: 1 });
    }
    private static void CompletionFailure()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var faults = 0;
        var subscription = hub.Subscribe(_ => { }, _ => ++faults); var inner = new Context { ThrowComplete = true }; var scope = Scope(inner, hub.Begin(subscription, Key()));
        subscription.Dispose(); Throws(() => Complete(scope)); scope.Dispose();
        Check(inner.Completed == 1 && inner.Disposals == 1 && faults == 1 && !subscription.IsDrained
            && subscription.ReadActivity() is { Known: false, InFlight: 0 });
    }
    private static void DisposalFailure()
    {
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var faults = 0;
        var subscription = hub.Subscribe(_ => { }, _ => ++faults); var inner = new Context { ThrowDispose = true }; var scope = Scope(inner, hub.Begin(subscription, Key()));
        subscription.Dispose(); Complete(scope); Throws(scope.Dispose); Throws(scope.Dispose);
        Check(inner.Completed == 1 && inner.Disposals == 1 && faults == 1 && !subscription.IsDrained && !subscription.ReadActivity().Known);
    }
    private static void SuspendedCallback() => Suspend(false);
    private static void SuspendedFault() => Suspend(true);
    private static void Suspend(bool fail)
    {
        using var entered = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim();
        var hub = new NativeTweenSingleEventSubscriptions<Payload>(); var calls = 0; var faults = 0;
        var subscription = hub.Subscribe(_ => {
            ++calls; entered.Set(); if (!resume.Wait(5000)) throw new TimeoutException("resume missing");
            if (fail) throw new IOException("suspended selected callback failed after exit");
        }, _ => ++faults);
        var inner = new Context(); var scope = Scope(inner, hub.Begin(subscription, Key()));
        var dispatch = Task.Run(() => hub.Dispatch(Key(), new()));
        try {
            Check(entered.Wait(5000)); subscription.Dispose(); Complete(scope); scope.Dispose(); Collect();
            Check(!subscription.IsDrained && subscription.ReadActivity() is { Known: true, InFlight: 1 });
            Check(hub.Dispatch(Key(), new()) == 0); Throws(() => hub.Begin(subscription, Key(2, 12)));
        }
        finally { resume.Set(); }
        Check(dispatch.Wait(5000) && dispatch.Result == (fail ? 1u : 0u));
        Check(calls == 1 && faults == (fail ? 1 : 0) && inner.Completed == 1 && inner.Disposals == 1
            && subscription.ReadActivity().InFlight == 0 && subscription.IsDrained == !fail && subscription.ReadActivity().Known == !fail);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (NativeTweenSingleEventSubscriptions<Payload>.Subscription Subscription, WeakReference Target) SubscribeTarget(
        NativeTweenSingleEventSubscriptions<Payload> hub)
    { var target = new RunTarget(); return (hub.Subscribe(target.Observe, target.Fault), new(target)); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (NativeTweenSingleEventSubscriptions<Payload>.Subscription Subscription, WeakReference Target) FinishRun(
        NativeTweenSingleEventSubscriptions<Payload> hub)
    {
        var target = new RunTarget(); var subscription = hub.Subscribe(target.Observe, target.Fault);
        var scope = Scope(new(), hub.Begin(subscription, Key())); subscription.Dispose();
        hub.Dispatch(Key(), new()); Complete(scope); scope.Dispose();
        Check(target.Observations == 1 && target.Faults == 0); return (subscription, new(target));
    }
    private static void ReleasedRunCollection()
    {
        var permanentHub = new NativeTweenSingleEventSubscriptions<Payload>(); var result = FinishRun(permanentHub);
        Collect(); Check(!result.Target.IsAlive && result.Subscription.IsDrained);
        permanentHub.FaultAll("later run"); Check(permanentHub.Dispatch(Key(), new()) == 0 && result.Subscription.ReadActivity().Known);
        GC.KeepAlive(permanentHub); GC.KeepAlive(result.Subscription);
    }
}
