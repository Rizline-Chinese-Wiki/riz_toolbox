using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Host transaction tests only. No game clone, result call, scene unload or Steam operation is performed.</summary>
internal static class LocalRunSessionRegistryRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("LOCAL sessions require pending commit before binding/committing an exact controller", PendingBeforeCommit),
        ("LOCAL classifier publishes only exact bound roles and never intercepts unknown ordinary instances", ExactClassification),
        ("LOCAL worker classification reads immutable pointer tokens without touching native GC roots", WorkerSnapshot),
        ("Foreign-thread LOCAL creation and mutations preserve ownership and existing protection", ThreadRejections),
        ("LOCAL process capacity includes retained sessions and refuses ownership before exceeding its bound", Capacity),
        ("LOCAL result claim is consumed before external invocation and reentrant requests cannot duplicate it", CaptureOnce),
        ("LOCAL cached result preserves immutable counters and exact provider revision/run identity", CachedIdentity),
        ("LOCAL result failure and mismatched metadata never reopen ControllerOver capture", CaptureFailure),
        ("LOCAL stale/cross-registry claims and duplicate native aliases cannot mutate another run", StaleClaims),
        ("LOCAL identities and ownership cannot be adopted twice across separate registries", CrossRegistryOwnership),
        ("Concurrent LOCAL registry writers atomically reserve one process-wide metadata identity", ConcurrentRegistration),
        ("Provider withdrawal retains committed LOCAL protection, result cache and host bundle", ProviderWithdrawal),
        ("LOCAL retirement requires a matching externally verified consumer-end token", RetirementProof),
        ("LOCAL verifier and cleanup reentry cannot remove protection or submit another result", ReentrantRetirement),
        ("LOCAL return closes independent ownership in reverse order once and then withdraws exact protection", ReverseCleanup),
        ("Verified retirement removes old pointer classification before frees and retains unknown cleanup responsibility", UnknownCleanup)
    };

    private static LocalRunSessionDescriptor Descriptor(string provider = "provider.a", long revision = 1)
        => new(new(provider, "same-song"), revision, "chart-a");
    private static RunFinished Result() => new(null, "riztoolbox.local/chart-a", 12345, .625, 4, 7, 2, 3, false, false);

    private static void PendingBeforeCommit()
    {
        using var f = new Fixture(); var run = f.Prepare();
        Equal(LocalRunSessionPhase.Prepared, f.Registry.Read(run).Phase);
        Equal(LocalRunResultClaimKind.Unavailable, f.Claim(run).Kind);
        var fakeClaim = new LocalRunSessionRegistry.CommitClaim(run);
        Throws<InvalidOperationException>(() => f.Registry.Commit(fakeClaim));
        var commit = f.Registry.BeginCommit(run);
        Equal(LocalRunSessionPhase.PendingCommit, f.Registry.Read(run).Phase);
        Throws<InvalidOperationException>(() => f.Registry.Commit(commit));
        var root = new Lease();
        Throws<InvalidOperationException>(() => f.Registry.BindPendingController(fakeClaim, 200, root));
        Equal(0, root.Frees);
        f.Registry.BindPendingController(commit, 200, root);
        Check(f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 200));
        Equal(LocalRunSessionPhase.PendingCommit, f.Registry.Read(run).Phase);
        f.Registry.Commit(commit); Equal(LocalRunSessionPhase.Committed, f.Registry.Read(run).Phase);
        Throws<InvalidOperationException>(() => f.Registry.Commit(commit));
    }

    private static void ExactClassification()
    {
        using var f = new Fixture(); var run = f.Prepare(); var pending = f.Registry.BeginCommit(run);
        foreach (var boundary in Enum.GetValues<LocalBoundaryKind>()) Check(!f.Registry.IsProtected(boundary, 1234));
        f.Registry.BindPendingController(pending, 201, new Lease()); f.Registry.Commit(pending);
        Check(f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 201));
        Check(!f.Registry.IsProtected(LocalBoundaryKind.ResultStart, 201));
        Check(!f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 202));
        f.Registry.BindResultScene(run, 301, 12, new Lease());
        foreach (var boundary in new[] { LocalBoundaryKind.ResultStart, LocalBoundaryKind.ResultRetry, LocalBoundaryKind.ResultBack })
        { Check(f.Registry.IsProtected(boundary, 301)); Check(!f.Registry.IsProtected(boundary, 302)); }
        Check(!f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 301));
        Check(!f.Registry.IsProtected(LocalBoundaryKind.ResultStart, 0));
        Check(!f.Registry.IsProtected((LocalBoundaryKind)999, 301));
    }

    private static void WorkerSnapshot()
    {
        using var f = new Fixture(); var run = f.Prepare(); var pending = f.Registry.BeginCommit(run);
        var reads = 0; var frees = 0;
        using var root = new NativeRuntime.RootHandle(1, _ => frees++, _ => {
            reads++; f.Main.VerifyAccess(); return new IntPtr(220);
        }, f.Main.VerifyAccess);
        f.Registry.BindPendingController(pending, 220, root); f.Registry.Commit(pending);
        f.Registry.BindResultScene(run, 320, 9, new Lease());
        Exception? error = null; var correct = false;
        var worker = new Thread(() => {
            try
            {
                for (var i = 0; i < 1000; i++)
                    if (!f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 220)
                        || !f.Registry.IsProtected(LocalBoundaryKind.ResultStart, 320)
                        || f.Registry.IsProtected(LocalBoundaryKind.ResultStart, 999)) return;
                correct = true;
            }
            catch (Exception caught) { error = caught; }
        });
        worker.Start(); worker.Join(); Check(error is null && correct); Equal(0, reads); Equal(0, frees);
        f.End(run); Equal(1, frees); Equal(0, reads);
    }

    private static void ThreadRejections()
    {
        using var f = new Fixture(); var run = f.Committed(230); f.Registry.BindResultScene(run, 330, 8, new Lease());
        var returning = f.Committed(231); f.Registry.BeginReturn(returning);
        var proof = f.Registry.VerifyConsumersEnded(returning, new Verifier(true))!;
        using var unadopted = new Lease(); var errors = new List<Exception>();
        var worker = new Thread(() => {
            foreach (var attempt in new Action[] {
                () => new LocalRunSessionRegistry(f.Main),
                () => f.Registry.RegisterPrepared(Descriptor(), 900, unadopted),
                () => f.Registry.MarkProviderWithdrawn(run),
                () => f.Registry.BeginReturn(run),
                () => f.Registry.TryClaimResult(run),
                () => f.Registry.Read(run),
                () => f.Registry.VerifyConsumersEnded(returning, new Verifier(true)),
                () => f.Registry.Close(returning, proof)
            }) try { attempt(); } catch (Exception error) { errors.Add(error); }
        });
        worker.Start(); worker.Join(); Equal(8, errors.Count); Check(errors.All(x => x is InvalidOperationException));
        Equal(0, unadopted.Frees); Equal(LocalRunSessionPhase.Committed, f.Registry.Read(run).Phase);
        Check(f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 230));
        Check(f.Registry.IsProtected(LocalBoundaryKind.ResultStart, 330));
        Check(f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 231));
        Check(!f.Registry.Read(returning).CleanupAttempted);
    }

    private static void Capacity()
    {
        using var f = new Fixture(); var remaining = LocalRunSessionRegistry.MaximumProcessSessions - LocalRunSessionRegistry.RetainedProcessSessionCount;
        Check(remaining > 0); var held = new List<LocalRunSessionRegistry.SessionHandle>();
        for (var index = 0; index < remaining; index++) held.Add(f.Prepare());
        using var refused = new Lease();
        Throws<InvalidOperationException>(() => f.Registry.RegisterPrepared(Descriptor(), 9000, refused));
        Equal(0, refused.Frees); Equal(LocalRunSessionRegistry.MaximumProcessSessions, LocalRunSessionRegistry.RetainedProcessSessionCount);
        f.End(held[0]); var replacement = f.Prepare();
        Equal(LocalRunSessionPhase.Prepared, f.Registry.Read(replacement).Phase);
        Equal(LocalRunSessionRegistry.MaximumProcessSessions, LocalRunSessionRegistry.RetainedProcessSessionCount);
    }

    private static void CaptureOnce()
    {
        using var f = new Fixture(); var run = f.Committed(240); var externalCalls = 0;
        var claim = f.Claim(run); Equal(LocalRunResultClaimKind.InvokeOnce, claim.Kind);
        Equal(LocalRunSessionPhase.ResultCapturing, f.Registry.Read(run).Phase);
        Check(f.Registry.Read(run).ResultCallClaimed);
        // The native adapter would invoke ControllerOver here; this fake demonstrates that
        // its synchronous reentry sees the consumed claim before any result is returned.
        externalCalls++; Equal(LocalRunResultClaimKind.InProgress, f.Claim(run).Kind);
        Throws<InvalidOperationException>(() => f.Registry.BeginReturn(run));
        var actual = f.Registry.CompleteResult(claim.Claim!, 440, f.Registry.Read(run).MetadataIdentity, Result(), new Lease());
        Equal(LocalRunResultClaimKind.Cached, f.Claim(run).Kind); Equal(actual, f.Claim(run).Cached);
        Equal(1, externalCalls); Equal(LocalRunSessionPhase.ResultReady, f.Registry.Read(run).Phase);
        Throws<InvalidOperationException>(() => f.Registry.CompleteResult(claim.Claim!, 441, f.Registry.Read(run).MetadataIdentity, Result(), new Lease()));
    }

    private static void CachedIdentity()
    {
        using var f = new Fixture(); var a = f.Committed(250, Descriptor("provider.a", 1));
        var b = f.Committed(251, Descriptor("provider.b", 1)); var revision = f.Committed(252, Descriptor("provider.a", 2));
        var first = Result(); var request = f.Claim(a);
        var cached = f.Registry.CompleteResult(request.Claim!, 450, f.Registry.Read(a).MetadataIdentity, first, new Lease());
        first = first with { Perfect = 999, MaxPerfect = 888, Score = -1 };
        Equal(4, cached.Copy.Perfect); Equal(7, cached.Copy.MaxPerfect); Equal(12345L, cached.Copy.Score);
        Equal("provider.a", cached.Session.Song.Namespace); Equal(1L, cached.Session.SongRevision);
        Check(f.Registry.Read(a).Identity != f.Registry.Read(b).Identity);
        Check(f.Registry.Read(a).Identity.RunGeneration != f.Registry.Read(revision).Identity.RunGeneration);
        Equal(2L, f.Registry.Read(revision).Identity.SongRevision);
        Check(cached.Copy.RunId is null && !cached.Copy.AssociatedWithObservedStart);
    }

    private static void CaptureFailure()
    {
        using var f = new Fixture(); var run = f.Committed(260); var claim = f.Claim(run);
        f.Registry.FailResult(claim.Claim!, "ControllerOver exception; side effects unknown.");
        Equal(LocalRunSessionPhase.CleanupUnknown, f.Registry.Read(run).Phase);
        Check(!f.Registry.Read(run).ProtectionRetired);
        for (var i = 0; i < 3; i++) Equal(LocalRunResultClaimKind.Failed, f.Claim(run).Kind);
        Check(f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 260));
        Throws<InvalidOperationException>(() => f.Registry.CompleteResult(claim.Claim!, 460, f.Registry.Read(run).MetadataIdentity, Result(), new Lease()));
        var other = f.Committed(261); var second = f.Claim(other); using var root = new Lease();
        Throws<InvalidOperationException>(() => f.Registry.CompleteResult(second.Claim!, 461, 9999, Result(), root));
        Equal(0, root.Frees); Equal(LocalRunResultClaimKind.Failed, f.Claim(other).Kind);
    }

    private static void StaleClaims()
    {
        using var f = new Fixture(); using var other = new Fixture(); var first = f.Committed(270); var second = f.Committed(271);
        var claim = f.Claim(first);
        Throws<InvalidOperationException>(() => other.Registry.FailResult(claim.Claim!, "Wrong registry."));
        using var root = new Lease();
        Throws<InvalidOperationException>(() => f.Registry.BindResultScene(second, 270, 10, root)); Equal(0, root.Frees);
        var pending = f.Prepare(); var commit = f.Registry.BeginCommit(pending);
        Throws<InvalidOperationException>(() => f.Registry.BindPendingController(commit, 270, root)); Equal(0, root.Frees);
        f.Registry.FailResult(claim.Claim!, "End first claim without invoking a native operation.");
        Throws<InvalidOperationException>(() => f.Registry.FailResult(claim.Claim!, "Already failed."));
        Equal(LocalRunSessionPhase.Committed, f.Registry.Read(second).Phase);
    }

    private static void ProviderWithdrawal()
    {
        using var f = new Fixture(); var run = f.Committed(280); f.Registry.BindResultScene(run, 380, 11, new Lease());
        var claim = f.Claim(run); var result = f.Registry.CompleteResult(claim.Claim!, 480, f.Registry.Read(run).MetadataIdentity, Result(), new Lease());
        f.Registry.MarkProviderWithdrawn(run);
        var snapshot = f.Registry.Read(run); Check(snapshot.ProviderWithdrawn); Equal(4, snapshot.OutstandingOwnershipRecords);
        Check(f.Registry.IsProtected(LocalBoundaryKind.ResultStart, 380)); Equal(result, f.Claim(run).Cached);
        Check(f.Leases.All(x => x.Frees == 0));
    }

    private static void CrossRegistryOwnership()
    {
        using var f = new Fixture(); using var other = new Fixture(); var first = f.Committed(275);
        var metadata = f.Registry.Read(first).MetadataIdentity; using var refused = new Lease();
        Throws<InvalidOperationException>(() => other.Registry.RegisterPrepared(Descriptor(), metadata, refused));
        Equal(0, refused.Frees);
        Throws<InvalidOperationException>(() => other.Registry.RegisterPrepared(Descriptor(), 999_001, f.Leases.First()));
        Equal(0, f.Leases.First().Frees);
        var second = other.Prepare(); var pending = other.Registry.BeginCommit(second);
        Throws<InvalidOperationException>(() => other.Registry.BindPendingController(pending, 275, refused));
        Equal(0, refused.Frees);
        other.Registry.BindPendingController(pending, 276, new Lease()); other.Registry.Commit(pending);
        f.Registry.BindResultScene(first, 375, 4, new Lease());
        Throws<InvalidOperationException>(() => other.Registry.BindResultScene(second, 375, 4, refused));
        Equal(0, refused.Frees); Check(!other.Registry.IsProtected(LocalBoundaryKind.ResultStart, 375));
        Check(f.Registry.IsProtected(LocalBoundaryKind.ResultStart, 375));
    }

    private static void ConcurrentRegistration()
    {
        using var barrier = new Barrier(2); var accepted = 0; var refused = 0;
        var errors = new Exception?[2]; var threads = new Thread[2];
        for (var index = 0; index < threads.Length; index++)
        {
            var slot = index;
            threads[index] = new Thread(() => {
                using var main = new MainThreadDispatcher(); var registry = new LocalRunSessionRegistry(main);
                using var bundle = new Lease(); LocalRunSessionRegistry.SessionHandle? run = null;
                try
                {
                    barrier.SignalAndWait();
                    try
                    {
                        try { run = registry.RegisterPrepared(Descriptor(), 999_002, bundle); Interlocked.Increment(ref accepted); }
                        catch (InvalidOperationException) { Equal(0, bundle.Frees); Interlocked.Increment(ref refused); }
                    }
                    finally
                    {
                        // Keep the winner registered until both competing attempts end, also
                        // releasing its peer if a regression assertion in the loser fails.
                        barrier.SignalAndWait();
                    }
                    if (run is not null)
                    {
                        registry.BeginReturn(run); var proof = registry.VerifyConsumersEnded(run, new Verifier(true));
                        Check(proof is not null && registry.Close(run, proof));
                    }
                }
                catch (Exception error) { errors[slot] = error; }
            });
        }
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();
        Check(errors.All(x => x is null)); Equal(1, accepted); Equal(1, refused);
    }

    private static void RetirementProof()
    {
        using var f = new Fixture(); var run = f.Committed(290); var other = f.Committed(291);
        Throws<InvalidOperationException>(() => f.Registry.VerifyConsumersEnded(run, new Verifier(true)));
        f.Registry.BeginReturn(run); f.Registry.BeginReturn(other);
        Check(f.Registry.VerifyConsumersEnded(run, new Verifier(false)) is null);
        Equal(0, f.Leases.Sum(x => x.Frees)); Check(f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 290));
        var otherProof = f.Registry.VerifyConsumersEnded(other, new Verifier(true))!;
        Throws<InvalidOperationException>(() => f.Registry.Close(run, otherProof));
        Equal(0, f.Leases.Sum(x => x.Frees));
        var proof = f.Registry.VerifyConsumersEnded(run, new Verifier(true))!;
        Check(f.Registry.Close(run, proof)); Check(f.Registry.Close(run, proof));
        Equal(LocalRunSessionPhase.Closed, f.Registry.Read(run).Phase);
        Equal(LocalRunResultClaimKind.Unavailable, f.Claim(run).Kind);
        Check(!f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 290));
        Check(f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 291));
    }

    private static void ReentrantRetirement()
    {
        using var f = new Fixture(); var run = f.Committed(300); f.Registry.BindResultScene(run, 400, 12, new Lease());
        f.Registry.BeginReturn(run); var rejected = 0;
        var verifier = new Verifier(true, () => {
            Throws<InvalidOperationException>(() => f.Registry.MarkProviderWithdrawn(run)); rejected++;
            Throws<InvalidOperationException>(() => f.Registry.TryClaimResult(run)); rejected++;
            Check(f.Registry.IsProtected(LocalBoundaryKind.ResultStart, 400));
        });
        var proof = f.Registry.VerifyConsumersEnded(run, verifier)!;
        var bundle = f.Leases.First(); bundle.BeforeFree = () => {
            Throws<InvalidOperationException>(() => f.Registry.Close(run, proof)); rejected++;
            Check(!f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 300));
            Check(f.Registry.Read(run).ProtectionRetired);
        };
        Check(f.Registry.Close(run, proof)); Equal(3, rejected);
        Check(!f.Registry.IsProtected(LocalBoundaryKind.ResultStart, 400));
    }

    private static void ReverseCleanup()
    {
        using var f = new Fixture(); var freed = new List<int>(); var run = f.Prepare(bundle: new Lease(() => freed.Add(0)));
        var commit = f.Registry.BeginCommit(run); f.Registry.BindPendingController(commit, 310, new Lease(() => freed.Add(1))); f.Registry.Commit(commit);
        f.Registry.BindResultScene(run, 410, 4, new Lease(() => freed.Add(2)));
        var capture = f.Claim(run); f.Registry.CompleteResult(capture.Claim!, 510, f.Registry.Read(run).MetadataIdentity, Result(), new Lease(() => freed.Add(3)));
        Equal(4, f.Registry.Read(run).OutstandingOwnershipRecords); f.End(run);
        Equal("3,2,1,0", string.Join(",", freed)); Equal(0, f.Registry.Read(run).OutstandingOwnershipRecords);
        Check(!f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 310));
        Check(!f.Registry.IsProtected(LocalBoundaryKind.ResultStart, 410));
    }

    private static void UnknownCleanup()
    {
        using var f = new Fixture(); var order = new List<int>(); var run = f.Prepare(bundle: new Lease(() => order.Add(0)));
        var failed = new Lease(() => { order.Add(1); throw new IOException("Unknown native free outcome."); });
        var commit = f.Registry.BeginCommit(run); f.Registry.BindPendingController(commit, 320, failed); f.Registry.Commit(commit);
        f.Registry.BindResultScene(run, 420, 5, new Lease(() => order.Add(2)));
        f.Registry.BeginReturn(run); var proof = f.Registry.VerifyConsumersEnded(run, new Verifier(true))!;
        Check(!f.Registry.Close(run, proof)); Equal("2,1,0", string.Join(",", order));
        Equal(1, failed.Frees); Equal(LocalRunSessionPhase.CleanupUnknown, f.Registry.Read(run).Phase);
        Equal(1, f.Registry.Read(run).OutstandingOwnershipRecords);
        Check(f.Registry.Read(run).ProtectionRetired);
        Check(!string.IsNullOrWhiteSpace(f.Registry.Read(run).ConsumerEndEvidence));
        Check(!f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, 320));
        Check(!f.Registry.IsProtected(LocalBoundaryKind.ResultStart, 420));
        Equal(LocalRunResultClaimKind.Unavailable, f.Claim(run).Kind);
        Check(!f.Registry.Close(run, proof)); Equal(1, failed.Frees); Equal(1, failed.Attempts);
        Throws<InvalidOperationException>(() => f.Registry.VerifyConsumersEnded(run, new Verifier(true)));
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly LocalRunSessionRegistry Registry;
        internal readonly List<Lease> Leases = new();
        private readonly List<LocalRunSessionRegistry.SessionHandle> _runs = new();
        private readonly Dictionary<LocalRunSessionRegistry.SessionHandle, LocalRunSessionRegistry.ResultCaptureClaim> _claims = new();
        private static long _nextMetadata = 10_000;
        private long _metadata = Interlocked.Add(ref _nextMetadata, 1000);
        internal Fixture() => Registry = new(Main);
        internal LocalRunSessionRegistry.SessionHandle Prepare(LocalRunSessionDescriptor? descriptor = null, Lease? bundle = null)
        {
            bundle ??= new(); Leases.Add(bundle);
            var result = Registry.RegisterPrepared(descriptor ?? Descriptor(), ++_metadata, bundle); _runs.Add(result); return result;
        }
        internal LocalRunSessionRegistry.SessionHandle Committed(long controller, LocalRunSessionDescriptor? descriptor = null)
        {
            var run = Prepare(descriptor); var pending = Registry.BeginCommit(run);
            Registry.BindPendingController(pending, controller, new Lease()); Registry.Commit(pending); return run;
        }
        internal LocalRunSessionRegistry.ResultClaim Claim(LocalRunSessionRegistry.SessionHandle run)
        {
            var outcome = Registry.TryClaimResult(run); if (outcome.Claim is not null) _claims[run] = outcome.Claim; return outcome;
        }
        internal void End(LocalRunSessionRegistry.SessionHandle run)
        {
            var snapshot = Registry.Read(run);
            if (snapshot.Phase == LocalRunSessionPhase.Closed || snapshot.CleanupAttempted) return;
            if (snapshot.Phase == LocalRunSessionPhase.ResultCapturing)
                Registry.FailResult(_claims[run], "Fixture confirms it performed no native invocation.");
            Registry.BeginReturn(run); var proof = Registry.VerifyConsumersEnded(run, new Verifier(true));
            Check(proof is not null && Registry.Close(run, proof));
        }
        public void Dispose()
        {
            try { foreach (var run in _runs) End(run); }
            finally { Main.Dispose(); }
        }
    }
    private sealed class Lease : IDisposable
    {
        private readonly Action? _free;
        internal Action? BeforeFree;
        internal int Frees, Attempts;
        internal Lease(Action? free = null) => _free = free;
        public void Dispose() { Attempts++; if (Frees != 0) return; Frees++; BeforeFree?.Invoke(); _free?.Invoke(); }
    }
    private sealed class Verifier : ILocalRunConsumersEndedVerifier
    {
        private readonly bool _result; private readonly Action? _check;
        internal Verifier(bool result, Action? check = null) => (_result, _check) = (result, check);
        public bool Verify(LocalRunRetirementRequest request, out string evidence)
        { _check?.Invoke(); evidence = "Controlled backend confirms controller/result Unity destruction, old callbacks and scene/result consumers ended, and globals released for this exact run."; return _result; }
    }
}
