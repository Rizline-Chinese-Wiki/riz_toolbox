using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using static Assert;

/// <summary>Exercises the actual native-wrapper owner through synthetic native exports and
/// counted roots. These tests do not load Unity or claim native destruction has occurred.</summary>
internal static class FactoryCleanupRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Native wrapper admission enforces 64 slots and returns every unused reservation", () => {
            Equal(64, AvailableReservations()); Equal(64, AvailableReservations());
        }),
        ("Native wrapper Release executes once and releases both independently owned roots", SuccessfulRelease),
        ("Borrowed native wrapper resources keep an independent root after wrapper release", BorrowedRoot),
        ("Disposed native wrapper getters and pointers reject before another native invocation", DisposedAccess),
        ("Wrong-thread native wrapper cleanup leaves its main-thread release opportunity intact", WrongThread),
        ("Native Release exceptions are retained and a later no-op cannot be mistaken for cleanup", NativeReleaseFailure),
        ("Managed Release faults retain both roots and are never retried", ManagedReleaseFailure),
        ("One failing root free does not skip the other free or reenter native Release", RootFreeFailure),
        ("Native wrapper capacity counts failed cleanup and returns untransferred reservations", Capacity)
    };

    private static void SuccessfulRelease()
    {
        using var fixture = new WrapperFixture();
        var handle = fixture.Create(canDestroy: true);
        Equal(fixture.Native.Instance, handle.Pointer);
        Check(handle.CanDestroy); Check(!handle.IsReleased); Check(!handle.CleanupFailed);
        handle.Dispose(); handle.Dispose();
        Equal(1, fixture.Native.Invocations);
        Equal(1, fixture.WrapperFrees); Equal(1, fixture.ResourceFrees);
        Equal(0, fixture.Native.ActiveRoots); Check(handle.IsReleased); Check(!handle.CleanupFailed);
    }

    private static void BorrowedRoot()
    {
        using var fixture = new WrapperFixture();
        var handle = fixture.Create();
        fixture.Native.ReturnType = fixture.Native.String;
        fixture.Native.Result = fixture.Native.OtherObject;
        using var borrowed = handle.BorrowResource();
        Equal(fixture.Native.OtherObject, borrowed.Target); Equal(1, fixture.Native.ActiveRoots);
        fixture.Native.ReturnType = fixture.Native.Void;
        handle.Dispose();
        Equal(fixture.Native.OtherObject, borrowed.Target);
        Equal(1, fixture.Native.ActiveRoots); Equal(1, fixture.ResourceFrees);
        borrowed.Dispose(); Equal(0, fixture.Native.ActiveRoots);
    }

    private static void DisposedAccess()
    {
        using var fixture = new WrapperFixture();
        var handle = fixture.Create(); handle.Dispose();
        Throws<ObjectDisposedException>(() => handle.BorrowResource());
        Throws<ObjectDisposedException>(() => { _ = handle.Pointer; });
        Equal(1, fixture.Native.Invocations);
    }

    private static void WrongThread()
    {
        using var fixture = new WrapperFixture();
        var handle = fixture.Create(); Exception? failure = null;
        var worker = new Thread(() => { try { handle.Dispose(); } catch (Exception error) { failure = error; } });
        worker.Start(); worker.Join();
        Check(failure is InvalidOperationException);
        Equal(0, fixture.Native.Invocations); Equal(0, fixture.WrapperFrees); Equal(0, fixture.ResourceFrees);
        Check(!handle.IsReleased); Check(!handle.CleanupFailed);
        Equal(fixture.Native.Instance, handle.Pointer);
        handle.Dispose(); Equal(1, fixture.Native.Invocations); Equal(1, fixture.WrapperFrees); Equal(1, fixture.ResourceFrees);
    }

    private static void NativeReleaseFailure()
    {
        using var fixture = new WrapperFixture();
        var handle = fixture.Create(canDestroy: true);
        fixture.Native.ExceptionOnInvoke = new IntPtr(99);
        Throws<InvalidOperationException>(() => handle.Dispose());
        // Model a native released flag which would make a repeated call return normally.
        fixture.Native.ExceptionOnInvoke = IntPtr.Zero;
        Throws<InvalidOperationException>(() => handle.Dispose());
        Equal(1, fixture.Native.Invocations); Check(!handle.IsReleased); Check(handle.CleanupFailed);
        Equal(0, fixture.WrapperFrees); Equal(0, fixture.ResourceFrees); Equal(0, fixture.Native.ActiveRoots);
        Throws<ObjectDisposedException>(() => handle.BorrowResource());
        Throws<ObjectDisposedException>(() => { _ = handle.Pointer; });
    }

    private static void ManagedReleaseFailure()
    {
        using var fixture = new WrapperFixture();
        var handle = fixture.Create(); fixture.Native.ThrowOnInvoke = true;
        Throws<InvalidOperationException>(() => handle.Dispose());
        fixture.Native.ThrowOnInvoke = false;
        Throws<InvalidOperationException>(() => handle.Dispose());
        Equal(1, fixture.Native.Invocations); Check(!handle.IsReleased); Check(handle.CleanupFailed);
        Equal(0, fixture.WrapperFrees); Equal(0, fixture.ResourceFrees); Equal(0, fixture.Native.ActiveRoots);
    }

    private static void RootFreeFailure()
    {
        using var fixture = new WrapperFixture { FailWrapperFree = true };
        var handle = fixture.Create();
        Throws<AggregateException>(() => handle.Dispose());
        Equal(1, fixture.Native.Invocations); Equal(1, fixture.WrapperFrees); Equal(1, fixture.ResourceFrees);
        Check(handle.IsReleased); Check(handle.CleanupFailed);
        Throws<InvalidOperationException>(() => handle.Dispose());
        Equal(1, fixture.Native.Invocations); Equal(1, fixture.WrapperFrees); Equal(1, fixture.ResourceFrees);
        Throws<ObjectDisposedException>(() => handle.BorrowResource());
    }

    private static void Capacity()
    {
        var availableBefore = AvailableReservations();
        Check(availableBefore > 0 && availableBefore <= 64);
        using var fixture = new WrapperFixture();
        var handle = fixture.Create(); fixture.Native.ExceptionOnInvoke = new IntPtr(99);
        Throws<InvalidOperationException>(() => handle.Dispose());
        Equal(availableBefore - 1, AvailableReservations());
        // A failed owner cannot return its reservation through an optimistic repeated Dispose.
        fixture.Native.ExceptionOnInvoke = IntPtr.Zero;
        Throws<InvalidOperationException>(() => handle.Dispose());
        Equal(availableBefore - 1, AvailableReservations()); Equal(1, fixture.Native.Invocations);
    }

    private static int AvailableReservations()
    {
        var held = new List<NativeResourceReferenceHandle.Reservation>();
        try
        {
            for (var i = 0; i <= 64; i++)
            {
                try { held.Add(NativeResourceReferenceHandle.Reserve()); }
                catch (InvalidOperationException) { return held.Count; }
            }
            throw new InvalidOperationException("Native wrapper reservation capacity was not enforced.");
        }
        finally { foreach (var reservation in held) { reservation.Dispose(); reservation.Dispose(); } }
    }

    private sealed class WrapperFixture : IDisposable
    {
        internal readonly FakeNative Native = new();
        private readonly MainThreadDispatcher _main = new();
        internal int WrapperFrees, ResourceFrees;
        internal bool FailWrapperFree;

        internal NativeResourceReferenceHandle Create(bool canDestroy = false)
        {
            using var reservation = NativeResourceReferenceHandle.Reserve();
            var wrapperRoot = new NativeRuntime.RootHandle(1, _ => {
                WrapperFrees++;
                if (FailWrapperFree) throw new IOException("Synthetic unknown wrapper-root free result.");
            }, _ => Native.Instance);
            var resourceRoot = new NativeRuntime.RootHandle(2, _ => ResourceFrees++, _ => Native.OtherObject);
            var handle = new NativeResourceReferenceHandle(Native.Api, _main, wrapperRoot, resourceRoot,
                "System.String", Native.ClassOf(Native.String), Native.Method, Native.Method, canDestroy, reservation);
            reservation.Transfer(handle);
            return handle;
        }
        public void Dispose() { Native.Dispose(); _main.Dispose(); }
    }
}
