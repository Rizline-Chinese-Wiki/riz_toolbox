using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Controlled complete-scan/ownership tests; no Unity discovery or prefab mutation is performed.</summary>
internal static class ControllerPrefabResolverRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Empty prefab discovery releases both source roots before offering a retry", EmptySourcesPreflight),
        ("Populated normal and Workshop sources require a fresh complete prefab scan", PopulatedSourcesPreflight),
        ("Incomplete prefab preflight cannot confirm absence", IncompletePreflight),
        ("Unknown prefab preflight cleanup retains its failed source root", UnknownPreflightCleanup),
        ("Prefab discovery waits when neither active loaded card source contains a prefab", NotReady),
        ("A clean missing launch prefab releases preparation for the same host to try again", RetryAfterCleanAbsence),
        ("Prefab identity drift stays strict even when discovery labels it NotReady", DriftIsNotCleanAbsence),
        ("Prefab native array admission accepts declared Object arrays and exact source-card covariance", CovariantArrays),
        ("Prefab native array admission rejects wrong-source unrelated and multidimensional arrays", InvalidArrays),
        ("Prefab discovery merges matching card references only after both complete source scans", SharedPrefab),
        ("Prefab discovery rejects distinct prefabs across ordinary and Workshop cards", Ambiguous),
        ("Prefab discovery inspects the final allowed element rather than choosing the first prefab", FinalElement),
        ("Prefab discovery rejects source totals above 4096 without inspecting a partial prefix", ScanLimit),
        ("Prefab discovery rejects incomplete scans and wrong exact card or prefab classes", IncompleteOrWrongClass),
        ("Prefab owner keeps independent card and prefab roots after discovery arrays and temporary roots end", RetainedRoots),
        ("Prefab owner revalidation rejects field replacement inactive owners unloaded scenes and destroyed prefabs", ChangedOwner),
        ("Prefab resolver and owner reject foreign-thread use before discovery or root cleanup", WrongThread),
        ("Prefab resolver and owner reject reentry without abandoning retained ownership", Reentry),
        ("Prefab failure cleanup attempts independent roots once and retains unknown free records", UnknownCleanup),
        ("Prefab metadata/build rejection cleans discovery responsibility without returning a selection", Unsupported)
    };
    private static void CovariantArrays()
    {
        NativeControllerPrefabDiscovery.VerifyArrayShape(0, "UnityEngine.Object[]", 1);
        NativeControllerPrefabDiscovery.VerifyArrayShape(1, "UnityEngine.Object[]", 1);
        NativeControllerPrefabDiscovery.VerifyArrayShape(0, "LevelCardControl[]", 1);
        NativeControllerPrefabDiscovery.VerifyArrayShape(1, "WorkshopLevelCardControl[]", 1);
    }
    private static void InvalidArrays()
    {
        foreach (var type in new[] { "System.Object[]", "WorkshopLevelCardControl[]", "LevelCardControl[,]", "System.IntPtr[]", "LevelCardControl" })
            Throws<UnsupportedSignatureException>(() => NativeControllerPrefabDiscovery.VerifyArrayShape(0, type, 1));
        Throws<UnsupportedSignatureException>(() => NativeControllerPrefabDiscovery.VerifyArrayShape(1, "LevelCardControl[]", 1));
        Throws<UnsupportedSignatureException>(() => NativeControllerPrefabDiscovery.VerifyArrayShape(0, "LevelCardControl[]", 2));
        Throws<ArgumentOutOfRangeException>(() => NativeControllerPrefabDiscovery.VerifyArrayShape(2, "LevelCardControl[]", 1));
    }
    private static void EmptySourcesPreflight()
    {
        using var main = new MainThreadDispatcher();
        var empty = new Backend(main, Array.Empty<Card?>(), Array.Empty<Card?>());
        var card = new Card(1, 700);
        var available = new Backend(main, Array.Empty<Card?>(), new Card?[] { card });
        var attempts = new Queue<Backend>(new[] { empty, available });
        var resolver = new NativeControllerPrefabResolver(main, attempts.Dequeue);
        var result = resolver.CheckEmptySources();
        Equal(ControllerPrefabResolutionStatus.NotReady, result.Status);
        Check(result.Prefab is null && result.ConfirmedAbsent);
        Equal(ErrorCode.NotReady, result.ConfirmedAbsenceError!.Code);
        Equal(AudioContentCapabilityIds.LocalCatalogSelection, result.ConfirmedAbsenceError.CapabilityId);
        Check(result.ConfirmedAbsenceError.Retryable);
        Equal(2, empty.Discoveries); Equal(0, empty.CardReads);
        Check(empty.Completed && empty.Disposed); Equal(0, empty.OutstandingRootRecords);
        Check(empty.Roots.All(root => root.Attempts == 1));

        var later = resolver.Resolve();
        Equal(ControllerPrefabResolutionStatus.Ready, later.Status);
        using var owner = later.Prefab!;
        Equal(new IntPtr(700), owner.BorrowPrefabPointer);
        card.Prefab = 701;
        Throws<TargetExpiredException>(() => { _ = owner.BorrowPrefabPointer; });
    }
    private static void PopulatedSourcesPreflight()
    {
        using var main = new MainThreadDispatcher();
        foreach (var workshop in new[] { false, true })
        {
            var card = new Card(1, 700);
            var normal = workshop ? Array.Empty<Card?>() : new Card?[] { card };
            var custom = workshop ? new Card?[] { card } : Array.Empty<Card?>();
            var first = new Backend(main, normal, custom);
            var next = new Backend(main, normal, custom);
            var attempts = new Queue<Backend>(new[] { first, next });
            var resolver = new NativeControllerPrefabResolver(main, attempts.Dequeue);

            var result = resolver.CheckEmptySources();
            Equal(ControllerPrefabResolutionStatus.ScanRequired, result.Status);
            Check(result.Prefab is null && result.ConfirmedAbsenceError is null);
            Equal(2, first.Discoveries); Equal(0, first.CardReads);
            Check(first.Completed && first.Disposed); Equal(0, first.OutstandingRootRecords);

            card.Prefab = 701;
            var resolved = resolver.Resolve();
            Equal(ControllerPrefabResolutionStatus.Ready, resolved.Status);
            using var owner = resolved.Prefab!;
            Equal(new IntPtr(701), owner.BorrowPrefabPointer);
        }
    }
    private static void IncompletePreflight()
    {
        using var main = new MainThreadDispatcher();
        var backend = new Backend(main, Array.Empty<Card?>(), Array.Empty<Card?>()) { FailDiscoverySource = 1 };
        var result = new NativeControllerPrefabResolver(main, () => backend).CheckEmptySources();
        Equal(ControllerPrefabResolutionStatus.Unsupported, result.Status);
        Check(result.Prefab is null && result.ConfirmedAbsenceError is null);
        Equal(2, backend.Discoveries); Equal(0, backend.CardReads);
        Check(backend.Disposed); Equal(0, backend.OutstandingRootRecords);
    }
    private static void UnknownPreflightCleanup()
    {
        using var main = new MainThreadDispatcher();
        var backend = new Backend(main, Array.Empty<Card?>(), Array.Empty<Card?>()) { FailFreeIdentity = 100_001 };
        var result = new NativeControllerPrefabResolver(main, () => backend).CheckEmptySources();
        Equal(ControllerPrefabResolutionStatus.CleanupUnknown, result.Status);
        Check(result.Prefab is null && result.ConfirmedAbsenceError is null);
        Check(backend.Roots.All(root => root.Attempts == 1));
        Equal(1, backend.OutstandingRootRecords); Equal(1, backend.DisposeCalls);
    }
    private static void NotReady()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main,
            new Card?[] { null, new(1, 700) { Active = false }, new(2, 700) { Loaded = false } }, new Card?[] { new(3, 0) });
        var result = new NativeControllerPrefabResolver(main, () => backend).Resolve();
        Equal(ControllerPrefabResolutionStatus.NotReady, result.Status); Check(result.Prefab is null && result.ConfirmedAbsent);
        Equal(4, result.ScannedElements); Equal(0, result.MatchingCards); Equal(2, backend.Discoveries);
        Check(backend.Completed && backend.Disposed); Equal(0, backend.OutstandingRootRecords);
    }
    private static void RetryAfterCleanAbsence()
    {
        using var main = new MainThreadDispatcher();
        var absent = new Backend(main, Array.Empty<Card?>(), Array.Empty<Card?>());
        var present = new Backend(main, new Card?[] { new(1, 700) }, Array.Empty<Card?>());
        var attempts = new Queue<Backend>(new[] { absent, present });
        var resolver = new NativeControllerPrefabResolver(main, attempts.Dequeue);
        OwnedNativeControllerPrefab? prepared = null;
        var releases = 0;
        var first = NativeLocalRunLaunchCoordinator.CompletePrefabPreparation(resolver.Resolve(),
            prefab => prepared = prefab, () => releases++);
        Check(!first.Success);
        Equal(ErrorCode.NotReady, first.Error!.Code);
        Check(first.Error.Retryable);
        Check(absent.Disposed && absent.Completed && prepared is null);
        Equal(0, absent.OutstandingRootRecords);
        Equal(1, releases);

        var second = resolver.Resolve();
        var ready = NativeLocalRunLaunchCoordinator.CompletePrefabPreparation(second,
            prefab => prepared = prefab, () => releases++);
        Check(ready.Success && prepared is not null && ReferenceEquals(second.Prefab, prepared));
        using var owner = prepared!;
        Equal(700L, owner.PrefabIdentity);
        Equal(new IntPtr(700), owner.BorrowPrefabPointer);
        Equal(2, present.Discoveries);
        Equal(1, releases);
    }

    private static void DriftIsNotCleanAbsence()
    {
        using var main = new MainThreadDispatcher();
        var card = new Card(1, 700);
        var backend = new Backend(main, new Card?[] { card }, Array.Empty<Card?>());
        backend.BeforeVerify = () => card.Prefab = 701;
        var result = new NativeControllerPrefabResolver(main, () => backend).Resolve();
        Equal(ControllerPrefabResolutionStatus.NotReady, result.Status);
        Check(!result.ConfirmedAbsent && backend.Disposed);
        RejectUnconfirmedPreparation(result);
    }

    private static void RejectUnconfirmedPreparation(ControllerPrefabResolution result)
    {
        var retained = false;
        var released = false;
        Throws<InvalidOperationException>(() => NativeLocalRunLaunchCoordinator.CompletePrefabPreparation(result,
            _ => retained = true, () => released = true));
        Check(!retained && !released);
    }
    private static void SharedPrefab()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main,
            new Card?[] { new(1, 700), new(2, 700), new(3, 700) }, new Card?[] { new(4, 700) });
        var result = new NativeControllerPrefabResolver(main, () => backend).Resolve();
        Equal(ControllerPrefabResolutionStatus.Ready, result.Status); Equal(4, result.ScannedElements); Equal(4, result.MatchingCards);
        using var owner = result.Prefab!; Equal(700L, owner.PrefabIdentity); Equal(new IntPtr(700), owner.BorrowPrefabPointer);
        Equal(1L, owner.CardOwnerIdentity); Equal(2, owner.OutstandingRootRecords);
        Equal(5, backend.CardReads); // All four snapshot elements plus one independent retained owner.
        Check(backend.Completed); Check(!backend.Disposed); owner.Dispose(); Equal(0, backend.OutstandingRootRecords);
        Check(backend.Roots.All(x => x.Attempts == 1));
    }
    private static void Ambiguous()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main,
            new Card?[] { new(1, 700) }, new Card?[] { new(2, 701), new(3, 700) });
        var result = new NativeControllerPrefabResolver(main, () => backend).Resolve();
        Equal(ControllerPrefabResolutionStatus.Ambiguous, result.Status); Check(result.Prefab is null);
        Equal(3, result.ScannedElements); Equal(3, result.MatchingCards);
        Check(backend.Completed && backend.Disposed); Equal(0, backend.OutstandingRootRecords);
        RejectUnconfirmedPreparation(result);
    }
    private static void FinalElement()
    {
        using var main = new MainThreadDispatcher(); var prefix = Enumerable.Range(1, 4095).Select(index => (Card?)new Card(10_000 + index, 700)).ToArray();
        var backend = new Backend(main, prefix, new Card?[] { new(9000, 701) });
        var result = new NativeControllerPrefabResolver(main, () => backend).Resolve();
        Equal(ControllerPrefabResolutionStatus.Ambiguous, result.Status); Equal(4096, result.ScannedElements);
        Equal(4096, result.MatchingCards); Check(result.Prefab is null && backend.Completed);
        Equal(0, backend.OutstandingRootRecords);
    }
    private static void ScanLimit()
    {
        using var main = new MainThreadDispatcher();
        foreach (var counts in new[] { (4096, 1), (4097, 0), (-1, 0) })
        {
            var backend = new Backend(main, new Card?[] { new(1, 700) }, Array.Empty<Card?>()) { DeclaredCounts = new[] { counts.Item1, counts.Item2 } };
            var result = new NativeControllerPrefabResolver(main, () => backend).Resolve();
            Equal(ControllerPrefabResolutionStatus.ScanLimit, result.Status); Equal(0, result.ScannedElements);
            Equal(0, backend.CardReads); Check(result.Prefab is null && backend.Disposed); Equal(0, backend.OutstandingRootRecords);
        }
    }
    private static void IncompleteOrWrongClass()
    {
        using var main = new MainThreadDispatcher();
        foreach (var scenario in new[] { 0, 1, 2 })
        {
            var first = new Card(1, 700); var second = new Card(2, 700);
            var backend = new Backend(main, new Card?[] { first, second }, Array.Empty<Card?>());
            if (scenario == 0) backend.FailCardIndex = 1;
            if (scenario == 1) second.ExactCard = false;
            if (scenario == 2) second.ExactPrefab = false;
            var result = new NativeControllerPrefabResolver(main, () => backend).Resolve();
            Equal(ControllerPrefabResolutionStatus.Unsupported, result.Status); Check(result.Prefab is null && backend.Disposed);
            Equal(2, result.ScannedElements); Equal(0, backend.OutstandingRootRecords);
        }
    }
    private static void RetainedRoots()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main, new Card?[] { new(1, 700) }, Array.Empty<Card?>());
        var result = new NativeControllerPrefabResolver(main, () => backend).Resolve(); var owner = result.Prefab!;
        Check(backend.Roots.Where(x => x.Kind == "source").All(x => x.Attempts == 1));
        Equal(2, backend.OutstandingRootRecords); GC.Collect(); owner.VerifyStillCurrent(); Equal(new IntPtr(700), owner.BorrowPrefabPointer);
        owner.Dispose(); owner.Dispose(); Check(owner.IsDisposed); Equal(0, backend.OutstandingRootRecords);
        Throws<ObjectDisposedException>(() => owner.VerifyStillCurrent());
        Throws<ObjectDisposedException>(() => { _ = owner.BorrowPrefabPointer; });
        Check(backend.Roots.All(x => x.Attempts == 1));
    }
    private static void ChangedOwner()
    {
        using var main = new MainThreadDispatcher();
        foreach (var change in new Action<Card>[] { c => c.Prefab = 701, c => c.Active = false, c => c.Loaded = false, c => c.LivePrefab = false })
        {
            var card = new Card(1, 700); var backend = new Backend(main, new Card?[] { card }, Array.Empty<Card?>());
            var result = new NativeControllerPrefabResolver(main, () => backend).Resolve(); using var owner = result.Prefab!;
            change(card); Throws<InvalidOperationException>(() => owner.VerifyStillCurrent());
            Equal(2, backend.OutstandingRootRecords); Check(!owner.IsDisposed); owner.Dispose(); Equal(0, backend.OutstandingRootRecords);
        }
    }
    private static void WrongThread()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main, new Card?[] { new(1, 700) }, Array.Empty<Card?>());
        var resolver = new NativeControllerPrefabResolver(main, () => backend); Exception? failure = null;
        var worker = new Thread(() => { try { resolver.Resolve(); } catch (Exception error) { failure = error; } });
        worker.Start(); worker.Join(); Check(failure is InvalidOperationException); Equal(0, backend.Discoveries);
        var result = resolver.Resolve(); using var owner = result.Prefab!; var errors = new List<Exception>();
        worker = new Thread(() => {
            foreach (var operation in new Action[] { owner.VerifyStillCurrent, owner.Dispose, () => { _ = owner.BorrowPrefabPointer; } })
                try { operation(); } catch (Exception error) { errors.Add(error); }
        });
        worker.Start(); worker.Join(); Equal(3, errors.Count); Equal(2, backend.OutstandingRootRecords);
        Check(!owner.IsDisposed); owner.VerifyStillCurrent();
    }
    private static void Reentry()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main, new Card?[] { new(1, 700) }, Array.Empty<Card?>());
        var resolver = new NativeControllerPrefabResolver(main, () => backend); var refused = 0;
        backend.BeforeDiscovery = () => { Throws<InvalidOperationException>(() => resolver.Resolve()); refused++; };
        var result = resolver.Resolve(); using var owner = result.Prefab!; Equal(2, refused);
        backend.BeforeVerify = () => { Throws<InvalidOperationException>(() => owner.Dispose()); refused++; };
        owner.VerifyStillCurrent(); Equal(3, refused); Equal(2, backend.OutstandingRootRecords);
        backend.BeforeVerify = null;
    }
    private static void UnknownCleanup()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main, new Card?[] { new(1, 700) }, Array.Empty<Card?>()) { FailFreeIdentity = 1 };
        var result = new NativeControllerPrefabResolver(main, () => backend).Resolve();
        Equal(ControllerPrefabResolutionStatus.CleanupUnknown, result.Status); Check(result.Prefab is null);
        Check(backend.Roots.All(x => x.Attempts == 1)); Check(backend.OutstandingRootRecords > 0);
        Equal(1, backend.DisposeCalls); Throws<InvalidOperationException>(() => backend.Dispose());
        Check(backend.Roots.All(x => x.Attempts == 1));
        RejectUnconfirmedPreparation(result);
    }
    private static void Unsupported()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main, new Card?[] { new(1, 700) }, Array.Empty<Card?>()) { FailInitialize = true };
        var result = new NativeControllerPrefabResolver(main, () => backend).Resolve();
        Equal(ControllerPrefabResolutionStatus.Unsupported, result.Status); Check(result.Prefab is null);
        Equal(0, backend.Discoveries); Equal(0, backend.CardReads); Equal(1, backend.DisposeCalls); Equal(0, backend.OutstandingRootRecords);
    }

    private sealed class Card
    {
        internal readonly long Identity;
        internal long Prefab;
        internal bool Active = true, Loaded = true, ExactCard = true, ExactPrefab = true, LivePrefab = true;
        internal Card(long identity, long prefab) => (Identity, Prefab) = (identity, prefab);
    }
    private sealed class Reference : IControllerPrefabReference
    {
        private readonly Backend _owner; private readonly long _identity;
        internal readonly string Kind; internal int Attempts; internal bool Failed;
        internal Reference(Backend owner, long identity, string kind) => (_owner, _identity, Kind) = (owner, identity, kind);
        public long Identity { get { _owner.Main.VerifyAccess(); if (Attempts != 0) throw new ObjectDisposedException(nameof(Reference)); return _identity; } }
        public void Dispose()
        {
            _owner.Main.VerifyAccess();
            if (Attempts != 0) { if (Failed) throw new InvalidOperationException("Unknown free is not retried."); return; }
            Attempts++;
            if (_owner.FailFreeIdentity == _identity) { Failed = true; _owner.Unknown = true; throw new IOException("Controlled unknown free."); }
            _owner.Live.Remove(this);
        }
    }
    private sealed class Backend : IControllerPrefabBackend
    {
        internal readonly MainThreadDispatcher Main;
        private readonly Card?[][] _cards;
        internal readonly List<Reference> Roots = new(); internal readonly HashSet<Reference> Live = new();
        private readonly Reference?[] _sources = new Reference?[2];
        internal int[]? DeclaredCounts; internal int FailCardIndex = -1, FailDiscoverySource = -1, Discoveries, CardReads, DisposeCalls;
        internal bool Completed, Disposed, FailInitialize, Unknown;
        internal long FailFreeIdentity = -1;
        internal Action? BeforeDiscovery, BeforeVerify;
        internal Backend(MainThreadDispatcher main, Card?[] normal, Card?[] workshop) => (Main, _cards) = (main, new[] { normal, workshop });
        public int OutstandingRootRecords { get { Main.VerifyAccess(); return Live.Count; } }
        public void Initialize() { Check(); if (FailInitialize) throw new UnsupportedSignatureException("Controlled changed build or metadata."); }
        private Reference Root(long identity, string kind) { Check(); var root = new Reference(this, identity, kind); Roots.Add(root); Live.Add(root); return root; }
        public int DiscoverSource(int source)
        {
            Check(); BeforeDiscovery?.Invoke(); Discoveries++;
            if (source == FailDiscoverySource) throw new IOException("Incomplete source discovery.");
            _sources[source] = Root(100_000 + source, "source");
            return DeclaredCounts?[source] ?? _cards[source].Length;
        }
        public IControllerPrefabReference? RetainCard(int source, int index)
        { Check(); CardReads++; if (source == 0 && index == FailCardIndex) throw new IOException("Incomplete scan."); return _cards[source][index] is { } card ? Root(card.Identity, "card") : null; }
        private Card Get(int source, IControllerPrefabReference card) => _cards[source].First(x => x?.Identity == card.Identity)!;
        public bool IsActiveLoadedCard(int source, IControllerPrefabReference card)
        { Check(); var value = Get(source, card); if (!value.ExactCard) throw new UnsupportedSignatureException("Wrong exact card."); return value.Active && value.Loaded; }
        public IControllerPrefabReference? RetainPrefab(int source, IControllerPrefabReference card)
        { Check(); var prefab = Get(source, card).Prefab; return prefab == 0 ? null : Root(prefab, "prefab"); }
        public void VerifyCardPrefab(int source, IControllerPrefabReference card, IControllerPrefabReference prefab)
        {
            Check(); BeforeVerify?.Invoke(); var value = Get(source, card);
            if (!value.ExactPrefab) throw new UnsupportedSignatureException("Wrong exact prefab.");
            if (!value.Active || !value.Loaded || !value.LivePrefab || value.Prefab != prefab.Identity) throw new TargetExpiredException("Owner or prefab changed.");
        }
        public void CompleteDiscovery()
        { Check(); foreach (var root in _sources) root?.Dispose(); Completed = true; }
        public void Dispose()
        {
            Main.VerifyAccess(); if (Disposed) throw new InvalidOperationException("Cleanup already attempted.");
            Disposed = true; DisposeCalls++; var errors = new List<Exception>();
            foreach (var root in Live.ToArray())
            {
                if (root.Attempts != 0) { errors.Add(new IOException("Previous unknown free.")); continue; }
                try { root.Dispose(); } catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count != 0) throw new AggregateException(errors);
        }
        private void Check() { Main.VerifyAccess(); if (Disposed || Unknown) throw new InvalidOperationException("Backend stopped or cleanup unknown."); }
    }
}
