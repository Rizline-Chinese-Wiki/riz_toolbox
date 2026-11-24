using System.Runtime.CompilerServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using static Assert;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

/// <summary>Actual runtime publication APIs with controlled native calls, plus failure
/// injection at the inactive owner's handoff. No Unity Instantiate or Awake claim.</summary>
internal static class InactiveControllerPublicationRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Inactive allocation publishes its fixed root record and destroy target before temporary cleanup", PublicationOrder),
        ("Inactive publication rejects foreign threads null callbacks and full storage before invoking producers", Admission),
        ("Inactive publication rejects nested allocation and disposal while preserving the outer handoff", Reentry),
        ("Inactive parent constructor publication transfers responsibility before native constructor entry", ConstructorOrder),
        ("Inactive clone reference publication records the returned root before runtime temporary cleanup", ReferenceOrder),
        // Remaining cases deliberately retain bounded unknown responsibility. Never reset it.
        ("Inactive runtime publication quota rejection keeps borrowed name rooted and value buffer caller-owned", PublicationQuotaRejection),
        ("Inactive failed temporary cleanup preserves published root and does not retry release or destruction proof", TemporaryCleanupFailure),
        ("Inactive failed constructor keeps its already published object under process-owned responsibility", ConstructorFailure),
        ("Inactive publisher notification failure cannot erase the fixed output record", NotificationFailure),
        ("Inactive missing publication blocks late callbacks further allocation and false cleanup completion", MissingPublication),
        ("Inactive abandoned failed publication retains owner and destroy closure through collection", FailedProcessResponsibility)
    };

    private static void PublicationOrder()
    {
        using var f = new Fixture(); NativeRuntime.RootHandle? target = null;
        f.Roots.AllocatePublished(publish => {
            publish(f.Root(1));
            Equal(1, f.Roots.Count); Equal(new IntPtr(1), target!.Target);
            Check(f.Roots.CleanupUnknown); Equal(0, f.Frees.Count);
        }, root => { Equal(1, f.Roots.Count); target = root; });
        Check(!f.Roots.CleanupUnknown); f.Owner.MarkReady(); f.Owner.Read();
        f.Owner.RequestDestroy(); Equal(1, f.DestroyRequests); Check(!f.Owner.TryCompleteDestroy());
        Equal(0, f.Frees.Count); f.Destroyed = true; Check(f.Owner.TryCompleteDestroy());
        Equal("1", string.Join(",", f.Frees)); f.Owner.Dispose(); Check(f.Owner.TryCompleteDestroy());
    }

    private static void Admission()
    {
        using var f = new Fixture(); var producers = 0; var notifications = 0;
        void Create(Action<NativeRuntime.RootHandle> publish) { producers++; publish(f.Root(99)); }
        Task.Run(() => Throws<InvalidOperationException>(() => f.Roots.AllocatePublished(Create, _ => notifications++))).GetAwaiter().GetResult();
        Throws<ArgumentNullException>(() => f.Roots.AllocatePublished(null!, _ => notifications++));
        Throws<ArgumentNullException>(() => f.Roots.AllocatePublished(Create, null!));
        Equal(0, producers); Equal(0, f.Roots.Count); Check(!f.Roots.CleanupUnknown);
        for (uint id = 1; id <= 8; id++) { var saved = id; f.Roots.AllocatePublished(p => p(f.Root(saved)), _ => notifications++); }
        Throws<InvalidOperationException>(() => f.Roots.AllocatePublished(Create, _ => notifications++));
        Equal(0, producers); Equal(8, notifications); Equal(8, f.Roots.Count);
        f.Roots.Dispose(); Equal("8,7,6,5,4,3,2,1", string.Join(",", f.Frees));
        Throws<ObjectDisposedException>(() => f.Roots.AllocatePublished(Create, _ => notifications++)); Equal(0, producers);
    }

    private static void Reentry()
    {
        using var f = new Fixture(); var nested = 0;
        f.Roots.AllocatePublished(publish => publish(f.Root(1)), _ => {
            Throws<InvalidOperationException>(() => f.Roots.AllocatePublished(p => { nested++; p(f.Root(2)); }, _ => nested++));
            Throws<InvalidOperationException>(() => f.Roots.Allocate(() => { nested++; return f.Root(3); }));
            Throws<InvalidOperationException>(() => f.Roots.Dispose());
        });
        Equal(0, nested); Equal(1, f.Roots.Count); Check(!f.Roots.CleanupUnknown);
        f.Roots.Allocate(() => f.Root(4)); f.Roots.Dispose(); Equal("4,1", string.Join(",", f.Frees));
    }

    private static void ConstructorOrder()
    {
        using var f = new Fixture(); using var native = new FakeNative { ConstructorAvailable = true };
        native.Parameters = new[] { native.Object };
        NativeRuntime.RootHandle? target = null;
        f.Roots.AllocatePublished(publish => native.Api.NewExactWithPublication(native.ClassOf(native.Receiver),
            new[] { "System.Object" }, publish, A.Ref(native.OtherObject)), root => {
                target = root; Equal(1, f.Roots.Count); Equal(0, native.Invocations);
                Check(native.HasRootFor(root.Target)); Equal(0, native.FreedRoots);
            });
        Equal(1, native.Invocations); Check(native.AllConstructorCallsRooted);
        Equal(native.LastObject, target!.Target); Equal(1, native.ActiveRoots);
        f.Roots.Dispose(); Equal(0, native.ActiveRoots);
    }

    private static void ReferenceOrder()
    {
        using var f = new Fixture(); using var native = new FakeNative();
        native.ReturnType = native.Object; native.Result = native.OtherObject;
        NativeRuntime.RootHandle? target = null;
        f.Roots.AllocatePublished(publish => native.Api.InvokeReferenceWithPublication(native.Method, native.Instance,
            pointer => publish(native.Api.Root(pointer))), root => {
                target = root; Equal(1, native.Invocations); Equal(1, f.Roots.Count);
                Equal(0, native.FreedRoots); Equal(native.Result, root.Target);
            });
        Equal(native.Result, target!.Target); Equal(1, native.ActiveRoots); Check(!f.Roots.CleanupUnknown);
        f.Roots.Dispose(); Equal(0, native.ActiveRoots);
    }

    private static void PublicationQuotaRejection()
    {
        using var f = new Fixture(); using var native = new FakeNative { ConstructorAvailable = true };
        var name = f.Roots.Allocate(() => native.Api.Root(native.OtherObject));
        var reservations = new List<NativePublicationCall>();
        try
        {
            while (NativePublicationCall.RetainedCallCount < NativePublicationCall.MaximumCalls)
                reservations.Add(new NativePublicationCall(f.Main.VerifyAccess, Array.Empty<A>()));
            A position;
            using (position = A.Value(false))
            {
                Throws<InvalidOperationException>(() => f.Roots.AllocatePublished(publish => native.Api.NewExactWithPublication(
                    native.ClassOf(native.Receiver), new[] { "System.Object", "System.Boolean" }, publish,
                    A.Ref(name.Target), position), _ => throw new Exception("Quota rejection must precede output publication.")));
                Check(position.Pointer != IntPtr.Zero); Equal(native.OtherObject, name.Target);
                Equal(0, native.ObjectAllocations); Equal(0, native.Invocations); Equal(1, f.Roots.Count);
            }
            Throws<ObjectDisposedException>(() => _ = position.Pointer);
            Check(f.Owner.CleanupUnknown); f.Owner.RequestDestroy(); f.Destroyed = true;
            Check(!f.Owner.TryCompleteDestroy()); Equal(0, f.DestroyedPolls); Equal(1, native.ActiveRoots);
            f.PreserveUnknown = true;
        }
        finally { foreach (var reservation in reservations) reservation.Dispose(); }
    }

    private static void TemporaryCleanupFailure()
    {
        using var f = new Fixture(); using var native = new FakeNative();
        native.ReturnType = native.Object; native.Result = native.OtherObject; native.FailFreeHandle = 1;
        NativeRuntime.RootHandle? target = null;
        Throws<AggregateException>(() => f.Roots.AllocatePublished(publish => native.Api.InvokeReferenceWithPublication(native.Method,
            native.Instance, pointer => publish(native.Api.Root(pointer))), root => target = root));
        Equal(1, f.Roots.Count); Equal(native.Result, target!.Target); Check(f.Owner.CleanupUnknown);
        var attempts = native.FreeHandleAttempts; f.Owner.MarkCreationFailed("Temporary cleanup failed after output publication.");
        f.Owner.RequestDestroy(); f.Destroyed = true; Check(!f.Owner.TryCompleteDestroy()); Check(!f.Owner.TryCompleteDestroy());
        Throws<InvalidOperationException>(() => f.Roots.Dispose());
        Equal(1, f.DestroyRequests); Equal(0, f.DestroyedPolls); Equal(attempts, native.FreeHandleAttempts);
        Equal(native.Result, target.Target); f.PreserveUnknown = true;
    }

    private static void ConstructorFailure()
    {
        using var f = new Fixture(); using var native = new FakeNative { ConstructorAvailable = true, ExceptionOnInvoke = new IntPtr(99) };
        NativeRuntime.RootHandle? target = null;
        Throws<InvalidOperationException>(() => f.Roots.AllocatePublished(publish => native.Api.NewExactWithPublication(
            native.ClassOf(native.Receiver), Array.Empty<string>(), publish), root => target = root));
        Equal(1, native.ObjectAllocations); Equal(1, native.Invocations); Equal(native.LastObject, target!.Target);
        Equal(1, f.Roots.Count); Equal(1, native.ActiveRoots); Check(f.Owner.CleanupUnknown);
        f.Owner.MarkCreationFailed("Native constructor failed after allocation.");
        f.Owner.RequestDestroy(); f.Destroyed = true; Check(!f.Owner.TryCompleteDestroy());
        Equal(0, f.DestroyedPolls); Equal(1, native.ActiveRoots); f.PreserveUnknown = true;
    }

    private static void NotificationFailure()
    {
        using var f = new Fixture(); var root = f.Root(1); var producers = 0;
        Throws<IOException>(() => f.Roots.AllocatePublished(p => { producers++; p(root); }, _ => {
            Equal(1, f.Roots.Count); throw new IOException("Caller notification failed.");
        }));
        Check(f.Owner.CleanupUnknown); Equal(1, f.Roots.Count); Equal(new IntPtr(1), root.Target);
        Throws<InvalidOperationException>(() => f.Roots.AllocatePublished(_ => producers++, _ => { }));
        Throws<InvalidOperationException>(() => f.Roots.Allocate(() => { producers++; return f.Root(2); }));
        Throws<InvalidOperationException>(() => f.Roots.Dispose()); Equal(1, producers); Equal(0, f.Frees.Count);
        f.Owner.RequestDestroy(); f.Destroyed = true; Check(!f.Owner.TryCompleteDestroy()); f.PreserveUnknown = true;
    }

    private static void MissingPublication()
    {
        using var f = new Fixture(); Action<NativeRuntime.RootHandle>? late = null; var notified = 0;
        Throws<InvalidOperationException>(() => f.Roots.AllocatePublished(p => late = p, _ => notified++));
        Check(f.Owner.CleanupUnknown); Equal(0, f.Roots.Count);
        using var caller = f.Root(7); Throws<InvalidOperationException>(() => late!(caller));
        Equal(0, notified); Equal(new IntPtr(7), caller.Target);
        f.Owner.RequestDestroy(); f.Destroyed = true; Check(!f.Owner.TryCompleteDestroy());
        Throws<InvalidOperationException>(() => f.Roots.Dispose()); Equal(0, f.Frees.Count); f.PreserveUnknown = true;
    }

    private static void FailedProcessResponsibility()
    {
        using var main = new MainThreadDispatcher(); var destroyed = new List<long>(); var freed = new List<uint>();
        var weak = Abandon(main, destroyed, freed);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(weak.TryGetTarget(out var owner)); Check(owner!.CleanupUnknown); Equal(1, owner.RootRecords);
        owner.RequestDestroy(); owner.RequestDestroy(); Equal("77", string.Join(",", destroyed));
        Check(!owner.TryCompleteDestroy()); Check(!owner.IsClosed); Equal(0, freed.Count);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<OwnedNativeInactiveController> Abandon(MainThreadDispatcher main, List<long> destroyed, List<uint> freed)
    {
        var roots = new InactiveControllerRootRecords(main); NativeRuntime.RootHandle? target = null;
        var owner = new OwnedNativeInactiveController(main, roots, Observation,
            () => destroyed.Add(target!.Target.ToInt64()), () => true, () => throw new InvalidOperationException());
        Throws<IOException>(() => roots.AllocatePublished(p => {
            p(new NativeRuntime.RootHandle(1, freed.Add, _ => new IntPtr(77), main.VerifyAccess));
            throw new IOException("Temporary cleanup failed after publication.");
        }, root => target = root));
        owner.MarkCreationFailed("Unknown cleanup."); return new(owner);
    }
    private static NativeInactiveControllerObservation Observation() => new(77, 88, 99, false, true, false, true, 0, 0);

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly InactiveControllerRootRecords Roots;
        internal readonly OwnedNativeInactiveController Owner;
        internal readonly List<uint> Frees = new();
        internal bool Destroyed, PreserveUnknown;
        internal int DestroyRequests, DestroyedPolls;
        internal Fixture()
        {
            Roots = new(Main);
            Owner = new(Main, Roots, Observation, () => DestroyRequests++,
                () => { DestroyedPolls++; return Destroyed; }, () => throw new InvalidOperationException());
        }
        internal NativeRuntime.RootHandle Root(uint id) => new(id, Frees.Add, _ => new IntPtr(id), Main.VerifyAccess);
        public void Dispose()
        {
            try { if (!PreserveUnknown) { Owner.Dispose(); Destroyed = true; Owner.TryCompleteDestroy(); } }
            finally { Main.Dispose(); }
        }
    }
}
