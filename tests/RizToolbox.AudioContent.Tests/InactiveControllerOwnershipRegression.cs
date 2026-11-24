using System.Runtime.CompilerServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using static Assert;

/// <summary>Controlled lifecycle predicates only; does not prove Unity Destroy timing or native clone safety.</summary>
internal static class InactiveControllerOwnershipRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Inactive controller Dispose requests Destroy and retains roots until a later confirmed destruction poll", PendingDestroy),
        ("Inactive controller refuses Read and Retain after destruction was requested without invoking consumers", PostRequest),
        ("Inactive controller Retain returns a separate caller root without transferring its owned graph", IndependentRetain),
        ("Inactive controller wrong-thread lifecycle calls leave main-thread read and cleanup available", WrongThread),
        ("Partially created inactive controller refuses consumers while preserving explicit destruction responsibility", PartialCreation),
        ("Inactive controller eight-root bound rejects before invoking an allocator or consuming caller ownership", RootCapacity),
        ("Inactive controller failed allocation adds no phantom root and keeps subsequent allocation usable", AllocationFailure),
        ("Inactive controller confirmed destruction frees independent roots in reverse order exactly once", ReverseFree),
        ("Process-owned inactive controller roots keep an abandoned pending owner alive through collection", ProcessResponsibility),
        // Last two intentionally retain process responsibility. Do not reset unknown native outcomes.
        ("Inactive controller Destroy failure retains roots and never retries the request or assumes later success", DestroyFailure),
        ("Inactive controller unknown root free still attempts independent roots once and preserves failed responsibility", UnknownFree)
    };

    private static void PendingDestroy()
    {
        using var f = new OwnershipFixture(); f.Owner.Read();
        Check(!f.Owner.TryCompleteDestroy()); Equal(0, f.DestroyedPolls);
        f.Owner.Dispose(); f.Owner.Dispose(); Equal(1, f.DestroyRequests); Check(f.Owner.DestroyRequested);
        Equal(3, f.Owner.RootRecords); Equal(0, f.Frees.Count); Check(!f.Owner.IsClosed);
        Check(!f.Owner.TryCompleteDestroy()); Equal(1, f.DestroyedPolls); Equal(3, f.Owner.RootRecords);
        // The fake represents a later Unity frame acknowledging delayed Destroy; the owner
        // never treats the request's return as such an acknowledgement.
        f.Destroyed = true; Check(f.Owner.TryCompleteDestroy()); Check(f.Owner.IsClosed);
        Equal(0, f.Owner.RootRecords); Equal(3, f.Frees.Count); Equal(1, f.DestroyRequests);
    }
    private static void PostRequest()
    {
        using var f = new OwnershipFixture(); f.Owner.Read(); Equal(1, f.Inspections);
        f.Owner.RequestDestroy();
        Throws<ObjectDisposedException>(() => f.Owner.Read());
        Throws<ObjectDisposedException>(() => f.Owner.RetainController());
        Equal(1, f.Inspections); Equal(0, f.Borrows); Equal(0, f.Frees.Count);
    }
    private static void IndependentRetain()
    {
        using var f = new OwnershipFixture(); var caller = f.Owner.RetainController();
        Equal(1, f.Borrows); Equal(new IntPtr(77), caller.Target); Equal(3, f.Owner.RootRecords);
        caller.Dispose(); caller.Dispose(); Equal(1, f.CallerFrees); Equal(0, f.Frees.Count);
        Equal(77L, f.Owner.Read().Controller); f.Owner.Dispose(); Equal(0, f.Frees.Count);
    }
    private static void WrongThread()
    {
        using var f = new OwnershipFixture(); var allocations = 0;
        Task.Run(() => {
            Throws<InvalidOperationException>(() => f.Owner.Dispose());
            Throws<InvalidOperationException>(() => f.Owner.Read());
            Throws<InvalidOperationException>(() => f.Owner.RetainController());
            Throws<InvalidOperationException>(() => f.Owner.TryCompleteDestroy());
            Throws<InvalidOperationException>(() => f.Roots.Allocate(() => { allocations++; return f.Root(50); }));
        }).GetAwaiter().GetResult();
        Check(!f.Owner.DestroyRequested && !f.Owner.CleanupUnknown); Equal(0, f.DestroyRequests);
        Equal(0, f.Inspections); Equal(0, allocations); Equal(0, f.Frees.Count); f.Owner.Read();
        f.Owner.RequestDestroy(); f.Destroyed = true;
        Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.TryCompleteDestroy())).GetAwaiter().GetResult();
        Check(!f.Owner.IsClosed); Equal(0, f.Frees.Count); Check(f.Owner.TryCompleteDestroy()); Equal(3, f.Frees.Count);
    }
    private static void PartialCreation()
    {
        using var f = new OwnershipFixture(); f.Owner.MarkCreationFailed("Clone allocation occurred but validation was incomplete.");
        var original = new IOException("Synthetic partial native creation failure.");
        var failure = new NativeInactiveControllerCreationException(f.Owner, original);
        Check(ReferenceEquals(f.Owner, failure.Responsibility) && ReferenceEquals(original, failure.InnerException));
        Throws<ObjectDisposedException>(() => failure.Responsibility.Read());
        Throws<ObjectDisposedException>(() => failure.Responsibility.RetainController());
        Equal(0, f.Inspections); Equal(0, f.Borrows); Equal(3, f.Owner.RootRecords);
        failure.Responsibility.Dispose(); Equal(1, f.DestroyRequests); Check(!f.Owner.TryCompleteDestroy());
        f.Destroyed = true; Check(f.Owner.TryCompleteDestroy()); Equal(3, f.Frees.Count);
    }
    private static void RootCapacity()
    {
        using var main = new MainThreadDispatcher(); using var roots = new InactiveControllerRootRecords(main);
        var frees = 0; var allocations = 0;
        for (uint id = 1; id <= 8; id++)
        { var saved = id; roots.Allocate(() => new NativeRuntime.RootHandle(saved, _ => frees++, _ => new IntPtr(saved), main.VerifyAccess)); }
        using var caller = new NativeRuntime.RootHandle(99, _ => frees++, _ => new IntPtr(99), main.VerifyAccess);
        Throws<InvalidOperationException>(() => roots.Allocate(() => { allocations++; return caller; }));
        Equal(0, allocations); Equal(0, frees); Equal(new IntPtr(99), caller.Target); Equal(8, roots.Count);
        roots.Dispose(); Equal(8, frees); Equal(0, roots.Count); Equal(new IntPtr(99), caller.Target);
        Throws<ObjectDisposedException>(() => roots.Allocate(() => { allocations++; return caller; })); Equal(0, allocations);
    }
    private static void AllocationFailure()
    {
        using var main = new MainThreadDispatcher(); using var roots = new InactiveControllerRootRecords(main);
        var allocations = 0; var frees = 0;
        Throws<IOException>(() => roots.Allocate(() => { allocations++; throw new IOException("No root was returned."); }));
        Equal(1, allocations); Equal(0, roots.Count);
        roots.Allocate(() => new NativeRuntime.RootHandle(1, _ => frees++, _ => new IntPtr(77), main.VerifyAccess));
        Equal(1, roots.Count); roots.Dispose(); Equal(1, frees); Equal(0, roots.Count);
    }
    private static void ReverseFree()
    {
        using var f = new OwnershipFixture(); f.Owner.Dispose(); f.Destroyed = true;
        Check(f.Owner.TryCompleteDestroy()); Check(f.Owner.TryCompleteDestroy()); f.Owner.Dispose();
        f.Roots.Dispose(); Equal("3,2,1", string.Join(",", f.Frees)); Equal(1, f.DestroyRequests);
        Check(f.Owner.IsClosed && !f.Owner.CleanupUnknown);
    }
    private static void ProcessResponsibility()
    {
        using var main = new MainThreadDispatcher(); var freed = new List<uint>();
        var weak = Abandon(main, freed);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(weak.TryGetTarget(out var owner)); Equal(1, owner!.RootRecords); Equal(0, freed.Count);
        // The static root graph must keep the owner and its destruction closures reachable.
        // This controlled predicate reports destruction only after the explicit request.
        owner.Dispose(); Check(owner.TryCompleteDestroy()); Equal("1", string.Join(",", freed));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<OwnedNativeInactiveController> Abandon(MainThreadDispatcher main, List<uint> freed)
    {
        var roots = new InactiveControllerRootRecords(main); var requested = false;
        roots.Allocate(() => new NativeRuntime.RootHandle(1, freed.Add, _ => new IntPtr(77), main.VerifyAccess));
        var owner = new OwnedNativeInactiveController(main, roots, Observation, () => requested = true, () => requested,
            () => new NativeRuntime.RootHandle(2, freed.Add, _ => new IntPtr(77), main.VerifyAccess));
        owner.MarkReady(); return new(owner);
    }
    private static void DestroyFailure()
    {
        using var f = new OwnershipFixture(); f.FailDestroy = true;
        Throws<IOException>(() => f.Owner.Dispose()); Check(f.Owner.DestroyRequested && f.Owner.CleanupUnknown);
        Equal(1, f.DestroyRequests); Equal(3, f.Owner.RootRecords); Equal(0, f.Frees.Count);
        f.FailDestroy = false; f.Destroyed = true;
        f.Owner.Dispose(); Check(!f.Owner.TryCompleteDestroy());
        Throws<ObjectDisposedException>(() => f.Owner.Read()); Throws<ObjectDisposedException>(() => f.Owner.RetainController());
        Equal(1, f.DestroyRequests); Equal(0, f.DestroyedPolls); Equal(0, f.Frees.Count); Equal(3, f.Owner.RootRecords);
        // Intentionally no reset/force-free: the process graph still owns this unknown outcome.
        f.PreserveUnknown = true;
    }
    private static void UnknownFree()
    {
        using var f = new OwnershipFixture(); f.Owner.Dispose(); f.Destroyed = true; f.FailFreeId = 2;
        Throws<AggregateException>(() => f.Owner.TryCompleteDestroy());
        Check(f.Owner.CleanupUnknown && f.Roots.CleanupUnknown && !f.Owner.IsClosed);
        Equal("3,2,1", string.Join(",", f.Frees)); Equal(1, f.Owner.RootRecords);
        Check(!f.Owner.TryCompleteDestroy()); f.Owner.Dispose();
        Throws<InvalidOperationException>(() => f.Roots.Dispose());
        Equal("3,2,1", string.Join(",", f.Frees)); Equal(1, f.Owner.RootRecords); Equal(1, f.DestroyRequests);
        f.PreserveUnknown = true;
    }
    private static NativeInactiveControllerObservation Observation() => new(77, 88, 99, false, true, false, true, 0, 0);

    private sealed class OwnershipFixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly InactiveControllerRootRecords Roots;
        internal readonly OwnedNativeInactiveController Owner;
        internal readonly List<uint> Frees = new();
        internal bool Destroyed, FailDestroy, PreserveUnknown;
        internal uint FailFreeId;
        internal int DestroyRequests, DestroyedPolls, Inspections, Borrows, CallerFrees;
        internal OwnershipFixture()
        {
            Roots = new(Main);
            for (uint id = 1; id <= 3; id++) { var saved = id; Roots.Allocate(() => Root(saved)); }
            Owner = new(Main, Roots,
                () => { Inspections++; return Observation(); },
                () => { DestroyRequests++; if (FailDestroy) throw new IOException("Unknown Destroy outcome."); },
                () => { DestroyedPolls++; return Destroyed; },
                () => { Borrows++; return new NativeRuntime.RootHandle(101, _ => CallerFrees++, _ => new IntPtr(77), Main.VerifyAccess); });
            Owner.MarkReady();
        }
        internal NativeRuntime.RootHandle Root(uint id) => new(id, value => {
            Frees.Add(value); if (value == FailFreeId) throw new IOException("Unknown native root free.");
        }, _ => new IntPtr(id), Main.VerifyAccess);
        public void Dispose()
        {
            try
            {
                if (!PreserveUnknown)
                { FailDestroy = false; Owner.Dispose(); Destroyed = true; Owner.TryCompleteDestroy(); }
            }
            finally { Main.Dispose(); }
        }
    }
}
