using System.Runtime.CompilerServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Empty native-bundle owners and real host-registry transactions only. No native graph is fabricated.</summary>
internal static class NativeLocalBundleOwnershipRegression
{
    private const long MiB = 1024L * 1024;
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Empty LOCAL bundles reject consumers and retain process responsibility until explicit completion", EmptyLifecycle),
        ("LOCAL bundle process capacity rejects owner 33 before native work and includes pending destruction", ProcessCapacity),
        ("LOCAL bundle upload reservation rejects invalid bounds without consuming its one successful reservation", ReservationRules),
        ("LOCAL bundle 128 MiB upload budget includes all pending retirement until confirmed completion", ProcessUploadBudget),
        ("LOCAL bundle wrong-thread operations preserve construction reservation and cleanup opportunities", WrongThread),
        ("Process responsibility keeps an abandoned reserved LOCAL bundle alive through managed collection", ProcessResponsibility),
        ("Failed LOCAL bundle construction preserves exact exception responsibility and reserved cleanup budget", FailureResponsibility),
        ("LOCAL pending bundle verification accepts only an adopted current claim without consuming ownership", PendingClaimAccepted),
        ("LOCAL pending bundle verification rejects forged cross-registry owner and metadata identities without side effects", PendingClaimIdentity),
        ("LOCAL bundle verification rejects prepared committed returning and closed transaction phases", PendingClaimPhases),
        ("LOCAL pending bundle verification checks thread admission before reading or consuming claim inputs", PendingClaimThread)
    };

    private static void EmptyLifecycle()
    {
        using var f = new BundleFixture(); var owner = f.New();
        Check(!owner.IsClosed && !owner.DestroyRequested && !owner.CleanupUnknown);
        Equal(0, owner.DirectRootRecords); Check(owner.Failure is null);
        Throws<ObjectDisposedException>(() => owner.Read());
        Throws<ObjectDisposedException>(() => { _ = owner.MetadataIdentity; });
        Throws<ObjectDisposedException>(() => { _ = owner.Selection; });
        Throws<ObjectDisposedException>(() => owner.RetainParametersForPendingSession(null!, null!));
        Check(!owner.TryCompleteDestroy()); Check(!owner.DestroyRequested);
        owner.Dispose(); owner.Dispose(); Check(owner.DestroyRequested && !owner.IsClosed);
        Check(owner.TryCompleteDestroy()); Check(owner.IsClosed); Check(owner.TryCompleteDestroy()); owner.Dispose();
        Throws<InvalidOperationException>(() => owner.ReserveUploads(1));
        f.NoNativeWork();
    }

    private static void ProcessCapacity()
    {
        using var f = new BundleFixture();
        var held = Enumerable.Range(0, 32).Select(_ => f.New()).ToArray();
        Throws<InvalidOperationException>(() => f.New()); f.NoNativeWork();
        held[0].Dispose(); Check(held[0].DestroyRequested && !held[0].IsClosed);
        Throws<InvalidOperationException>(() => f.New());
        Check(held[0].TryCompleteDestroy()); var replacement = f.New();
        Throws<InvalidOperationException>(() => f.New()); replacement.Dispose(); Check(replacement.TryCompleteDestroy());
        foreach (var owner in held) { owner.Dispose(); Check(owner.TryCompleteDestroy()); }
        // An entirely new full set proves successful completion returned every process slot.
        foreach (var owner in Enumerable.Range(0, 32).Select(_ => f.New()).ToArray())
        { owner.Dispose(); Check(owner.TryCompleteDestroy()); }
        f.NoNativeWork();
    }

    private static void ReservationRules()
    {
        using var f = new BundleFixture(); var owner = f.New();
        foreach (var bytes in new[] { long.MinValue, -1L, 0L, 64 * MiB + 1, long.MaxValue })
            Throws<InvalidOperationException>(() => owner.ReserveUploads(bytes));
        Check(!owner.DestroyRequested && !owner.IsClosed);
        owner.ReserveUploads(64 * MiB);
        Throws<InvalidOperationException>(() => owner.ReserveUploads(1));
        owner.Dispose(); Throws<InvalidOperationException>(() => owner.ReserveUploads(1));
        Check(owner.TryCompleteDestroy()); f.NoNativeWork();
    }

    private static void ProcessUploadBudget()
    {
        using var f = new BundleFixture(); var first = f.New(); var second = f.New(); var next = f.New(); var last = f.New();
        first.ReserveUploads(64 * MiB); second.ReserveUploads(64 * MiB);
        Throws<InvalidOperationException>(() => next.ReserveUploads(1));
        first.Dispose(); Check(!first.IsClosed);
        Throws<InvalidOperationException>(() => next.ReserveUploads(1));
        Check(first.TryCompleteDestroy()); next.ReserveUploads(64 * MiB);
        second.Dispose(); Throws<InvalidOperationException>(() => last.ReserveUploads(1));
        Check(second.TryCompleteDestroy()); last.ReserveUploads(64 * MiB);
        next.Dispose(); last.Dispose(); Check(!next.IsClosed && !last.IsClosed);
        Check(next.TryCompleteDestroy() && last.TryCompleteDestroy());
        var fresh = f.New(); fresh.ReserveUploads(64 * MiB); fresh.Dispose(); Check(fresh.TryCompleteDestroy());
        f.NoNativeWork();
    }

    private static void WrongThread()
    {
        using var f = new BundleFixture(); var owner = f.New();
        Task.Run(() => {
            Throws<InvalidOperationException>(() => new OwnedNativeLocalPlayBundle(f.Native.Api, f.Main));
            Throws<InvalidOperationException>(() => owner.ReserveUploads(64 * MiB));
            Throws<InvalidOperationException>(() => owner.MarkCreationFailed("worker must not mark failure"));
            Throws<InvalidOperationException>(() => owner.Dispose());
            Throws<InvalidOperationException>(() => owner.TryCompleteDestroy());
            Throws<InvalidOperationException>(() => owner.Read());
            Throws<InvalidOperationException>(() => { _ = owner.DirectRootRecords; });
            Throws<InvalidOperationException>(() => { _ = owner.IsClosed; });
            Throws<InvalidOperationException>(() => owner.RetainParametersForPendingSession(null!, null!));
        }).GetAwaiter().GetResult();
        Check(!owner.DestroyRequested && !owner.IsClosed && !owner.CleanupUnknown && owner.Failure is null);
        owner.ReserveUploads(64 * MiB); owner.Dispose();
        Task.Run(() => Throws<InvalidOperationException>(() => owner.TryCompleteDestroy())).GetAwaiter().GetResult();
        Check(!owner.IsClosed); Check(owner.TryCompleteDestroy()); f.NoNativeWork();
    }

    private static void ProcessResponsibility()
    {
        using var f = new BundleFixture(); var weak = Abandon(f);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(weak.TryGetTarget(out var owner)); Check(!owner!.IsClosed && !owner.DestroyRequested);
        var second = f.New(); second.ReserveUploads(64 * MiB);
        var blocked = f.New(); Throws<InvalidOperationException>(() => blocked.ReserveUploads(1));
        owner.Dispose(); Throws<InvalidOperationException>(() => blocked.ReserveUploads(1));
        Check(owner.TryCompleteDestroy()); blocked.ReserveUploads(1); f.NoNativeWork();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<OwnedNativeLocalPlayBundle> Abandon(BundleFixture f)
    {
        // Do not register this handle with the fixture: only the production process owner set
        // and its reserved budget retain it after this helper returns.
        var owner = new OwnedNativeLocalPlayBundle(f.Native.Api, f.Main); owner.ReserveUploads(64 * MiB);
        return new(owner);
    }

    private static void FailureResponsibility()
    {
        using var f = new BundleFixture(); var owner = f.New(); owner.ReserveUploads(64 * MiB);
        var cause = new IOException("Controlled construction rejection before native allocation.");
        owner.MarkCreationFailed(cause.Message); var failure = new NativeLocalPlayBundleCreationException(owner, cause);
        Check(ReferenceEquals(owner, failure.Responsibility) && ReferenceEquals(cause, failure.InnerException));
        Equal(cause.Message, owner.Failure); Check(!owner.IsClosed && !owner.DestroyRequested && !owner.CleanupUnknown);
        Throws<ObjectDisposedException>(() => failure.Responsibility.Read());
        Check(!failure.Responsibility.TryCompleteDestroy());
        var other = f.New(); other.ReserveUploads(64 * MiB); var blocked = f.New();
        Throws<InvalidOperationException>(() => blocked.ReserveUploads(1));
        failure.Responsibility.Dispose(); Throws<InvalidOperationException>(() => blocked.ReserveUploads(1));
        Check(failure.Responsibility.TryCompleteDestroy()); blocked.ReserveUploads(1); f.NoNativeWork();
    }

    private static void PendingClaimAccepted()
    {
        using var f = new RegistryFixture(); var run = f.Prepare(); var claim = f.Registry.BeginCommit(run.Session);
        var before = f.Registry.Read(run.Session);
        f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, run.Bundle);
        f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, run.Bundle);
        Equal(before, f.Registry.Read(run.Session)); Equal(0, run.Bundle.Attempts);
        Check(!f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, run.Metadata));
        var controller = new Lease(); f.Registry.BindPendingController(claim, run.Metadata + 1, controller);
        f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, run.Bundle);
        Equal(0, controller.Attempts); Equal(0, run.Bundle.Attempts); f.Registry.Commit(claim);
        Equal(LocalRunSessionPhase.Committed, f.Registry.Read(run.Session).Phase);
    }

    private static void PendingClaimIdentity()
    {
        using var f = new RegistryFixture(); using var foreign = new RegistryFixture();
        var run = f.Prepare(); var claim = f.Registry.BeginCommit(run.Session); var before = f.Registry.Read(run.Session);
        using var unadopted = new Lease(); var fake = new LocalRunSessionRegistry.CommitClaim(run.Session);
        Throws<InvalidOperationException>(() => f.Registry.VerifyPendingBundleClaim(fake, run.Metadata, run.Bundle));
        Throws<InvalidOperationException>(() => f.Registry.VerifyPendingBundleClaim(claim, run.Metadata + 1, run.Bundle));
        Throws<InvalidOperationException>(() => f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, unadopted));
        Throws<InvalidOperationException>(() => foreign.Registry.VerifyPendingBundleClaim(claim, run.Metadata, run.Bundle));
        Throws<ArgumentNullException>(() => f.Registry.VerifyPendingBundleClaim(null!, run.Metadata, run.Bundle));
        Throws<ArgumentNullException>(() => f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, null!));
        Equal(before, f.Registry.Read(run.Session)); Equal(0, run.Bundle.Attempts); Equal(0, unadopted.Attempts);
        f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, run.Bundle);
        var second = f.Prepare(); var secondClaim = f.Registry.BeginCommit(second.Session);
        Throws<InvalidOperationException>(() => f.Registry.VerifyPendingBundleClaim(secondClaim, run.Metadata, run.Bundle));
        Equal(0, second.Bundle.Attempts); Equal(0, run.Bundle.Attempts);
    }

    private static void PendingClaimPhases()
    {
        using var f = new RegistryFixture(); var run = f.Prepare();
        var fake = new LocalRunSessionRegistry.CommitClaim(run.Session);
        Throws<InvalidOperationException>(() => f.Registry.VerifyPendingBundleClaim(fake, run.Metadata, run.Bundle));
        Equal(LocalRunSessionPhase.Prepared, f.Registry.Read(run.Session).Phase); Equal(0, run.Bundle.Attempts);
        var claim = f.Registry.BeginCommit(run.Session); f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, run.Bundle);
        var controller = new Lease(); f.Registry.BindPendingController(claim, run.Metadata + 1, controller); f.Registry.Commit(claim);
        var committed = f.Registry.Read(run.Session);
        Throws<InvalidOperationException>(() => f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, run.Bundle));
        Equal(committed, f.Registry.Read(run.Session)); Equal(0, run.Bundle.Attempts); Equal(0, controller.Attempts);
        f.Registry.BeginReturn(run.Session); var returning = f.Registry.Read(run.Session);
        Throws<InvalidOperationException>(() => f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, run.Bundle));
        Equal(returning, f.Registry.Read(run.Session)); Equal(0, run.Bundle.Attempts); Equal(0, controller.Attempts);
        f.End(run); Equal(1, run.Bundle.Attempts); Equal(1, controller.Attempts);
        Throws<InvalidOperationException>(() => f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, run.Bundle));
        Equal(1, run.Bundle.Attempts); Equal(1, controller.Attempts);
    }

    private static void PendingClaimThread()
    {
        using var f = new RegistryFixture(); var run = f.Prepare(); var claim = f.Registry.BeginCommit(run.Session);
        var before = f.Registry.Read(run.Session);
        Task.Run(() => {
            Throws<InvalidOperationException>(() => f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, run.Bundle));
            Throws<InvalidOperationException>(() => f.Registry.VerifyPendingBundleClaim(null!, 0, null!));
        }).GetAwaiter().GetResult();
        Equal(before, f.Registry.Read(run.Session)); Equal(0, run.Bundle.Attempts);
        f.Registry.VerifyPendingBundleClaim(claim, run.Metadata, run.Bundle);
    }

    private sealed class BundleFixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly FakeNative Native = new();
        private readonly List<OwnedNativeLocalPlayBundle> _owners = new();
        internal OwnedNativeLocalPlayBundle New()
        { var owner = new OwnedNativeLocalPlayBundle(Native.Api, Main); _owners.Add(owner); return owner; }
        internal void NoNativeWork()
        {
            Equal(0, Native.ObjectAllocations); Equal(0, Native.ArrayAllocations); Equal(0, Native.Invocations);
            Equal(0, Native.NewRoots); Equal(0, Native.FreedRoots); Equal(0, Native.FieldReads); Equal(0, Native.Unboxes);
        }
        public void Dispose()
        {
            try { foreach (var owner in _owners) { owner.Dispose(); Check(owner.TryCompleteDestroy()); } }
            finally { Native.Dispose(); Main.Dispose(); }
        }
    }
    private sealed record Registered(LocalRunSessionRegistry.SessionHandle Session, long Metadata, Lease Bundle);
    private sealed class RegistryFixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly LocalRunSessionRegistry Registry;
        private readonly List<Registered> _runs = new();
        private static long _nextIdentity = 30_000_000;
        internal RegistryFixture() => Registry = new(Main);
        internal Registered Prepare()
        {
            var bundle = new Lease(); var metadata = Interlocked.Add(ref _nextIdentity, 100);
            var handle = Registry.RegisterPrepared(new(new("bundle.test", "song"), 1, "easy"), metadata, bundle);
            var run = new Registered(handle, metadata, bundle); _runs.Add(run); return run;
        }
        internal void End(Registered run)
        {
            if (Registry.Read(run.Session).Phase == LocalRunSessionPhase.Closed) return;
            Registry.BeginReturn(run.Session); var proof = Registry.VerifyConsumersEnded(run.Session, new Verifier());
            Check(proof is not null && Registry.Close(run.Session, proof));
        }
        public void Dispose() { try { foreach (var run in _runs) End(run); } finally { Main.Dispose(); } }
    }
    private sealed class Lease : IDisposable
    {
        internal int Attempts;
        public void Dispose() => Attempts++;
    }
    private sealed class Verifier : ILocalRunConsumersEndedVerifier
    {
        public bool Verify(LocalRunRetirementRequest request, out string evidence)
        { evidence = "CPU fixture performed no native invocation and confirms its own counted leases have no remaining consumers."; return true; }
    }
}
