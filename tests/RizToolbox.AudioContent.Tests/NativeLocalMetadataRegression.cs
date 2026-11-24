using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Pure ownership/admission checks. Native getter behavior is verified separately in the game.</summary>
internal static class NativeLocalMetadataRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("LOCAL metadata rejects invalid descriptors before reaching native or build bindings", InvalidDescriptors),
        ("LOCAL metadata accepts declared descriptor boundaries before its unsupported-build gate", DescriptorBoundaries),
        ("LOCAL metadata graph enforces 32 process owners and returns confirmed empty slots", GraphCapacity),
        ("LOCAL metadata graph rejects an eleventh root without invoking its allocation or consuming caller ownership", RootCapacity),
        ("LOCAL metadata allocation failure leaves no phantom root and preserves later allocation capacity", AllocationFailure),
        ("Wrong-thread LOCAL metadata disposal preserves the main-thread cleanup and read opportunity", WrongThread),
        ("LOCAL metadata disposes independent roots in reverse order exactly once", ReverseCleanup),
        ("Disposed LOCAL metadata rejects reads and native identity access before invoking its reader", DisposedOwner),
        // Must remain last: unknown native free responsibility is process-owned and intentionally cannot be reset.
        ("Unknown LOCAL metadata free attempts all independent roots once and blocks subsequent graphs", UnknownCleanup)
    };

    private static LocalNativeMetadataDescriptor Descriptor() => new(new("probe.provider", "song"), 1, "easy",
        "Same title", "Artist", "LOCAL", "3", "Designer", new Rgba(.2f, .3f, .4f, 1), 10, 1_000_000, 3);
    private static NativeLocalMetadataFactory UnavailableFactory(MainThreadDispatcher main)
        => new(null!, main, new AdapterBuildProfile("test", "", "test", "test", "test", "test",
            new Dictionary<string, TypeBinding>()), IntPtr.Zero);

    private static void InvalidDescriptors()
    {
        using var main = new MainThreadDispatcher();
        var factory = UnavailableFactory(main); var valid = Descriptor();
        Throws<ArgumentNullException>(() => factory.Create(null!));
        var invalid = new[] {
            valid with { LocalId = null! }, valid with { LocalId = new("bad/provider", "song") },
            valid with { LocalId = new("provider", "song/escape") }, valid with { ChartId = "" },
            valid with { ChartId = new string('c', 129) }, valid with { Revision = 0 }, valid with { Revision = -1 },
            valid with { Title = null! }, valid with { Title = "\t" }, valid with { Title = new string('t', 257) },
            valid with { Artist = " " }, valid with { ChartName = "bad\nname" }, valid with { DifficultyText = "" },
            valid with { Designer = "" }, valid with { Illustrator = null! }, valid with { Illustrator = "bad\0credit" },
            valid with { MaxHit = -1 }, valid with { MaxHit = 1_000_001 }, valid with { MaxScore = -1 },
            valid with { MaxScore = 1_000_000_001 }, valid with { Difficulty = float.NaN },
            valid with { Difficulty = float.PositiveInfinity }, valid with { Difficulty = 1000.1f },
            valid with { ThemeColor = new Rgba(-.1f, 0, 0, 1) },
            valid with { ThemeColor = new Rgba(0, float.NaN, 0, 1) },
            valid with { ThemeColor = new Rgba(0, 0, 0, 1.1f) }
        };
        // The factory has no NativeRuntime. An incorrectly reordered gate would throw
        // UnsupportedSignature/NullReference instead of the argument rejection asserted here.
        foreach (var descriptor in invalid) Throws<ArgumentException>(() => factory.Create(descriptor));
    }
    private static void DescriptorBoundaries()
    {
        using var main = new MainThreadDispatcher(); var factory = UnavailableFactory(main);
        var descriptor = Descriptor() with {
            LocalId = new(new string('n', 128), new string('s', 128)), ChartId = new string('c', 128),
            Revision = long.MaxValue, Title = new string('t', 256), Artist = new string('a', 256),
            ChartName = new string('c', 256), DifficultyText = new string('d', 256), Designer = new string('x', 256),
            Illustrator = "", MaxHit = 1_000_000, MaxScore = 1_000_000_000, Difficulty = 1000,
            ThemeColor = new Rgba(0, 1, 0, 1)
        };
        Throws<UnsupportedSignatureException>(() => factory.Create(descriptor));
        Throws<UnsupportedSignatureException>(() => factory.Create(Descriptor() with { MaxHit = 0, MaxScore = 0, Difficulty = 0 }));
    }
    private static void GraphCapacity()
    {
        using var main = new MainThreadDispatcher(); var held = new List<LocalNativeMetadataRootGraph>();
        try
        {
            for (var i = 0; i < 32; i++) held.Add(new(main));
            Throws<InvalidOperationException>(() => new LocalNativeMetadataRootGraph(main));
            Equal(32, held.Count); Check(held.All(x => x.RootCount == 0 && !x.IsDisposed));
            held[0].Dispose(); Check(held[0].IsDisposed);
            using var replacement = new LocalNativeMetadataRootGraph(main);
            Throws<InvalidOperationException>(() => new LocalNativeMetadataRootGraph(main));
        }
        finally { foreach (var graph in held) graph.Dispose(); }
        using var later = new LocalNativeMetadataRootGraph(main); // No phantom 33rd reservation remains.
    }
    private static void RootCapacity()
    {
        using var main = new MainThreadDispatcher(); using var graph = new LocalNativeMetadataRootGraph(main);
        var frees = new List<uint>();
        for (uint id = 1; id <= 10; id++)
        {
            var saved = id;
            graph.Allocate(() => new NativeRuntime.RootHandle(saved, frees.Add, _ => new IntPtr(saved)));
        }
        var callerFrees = 0; var allocations = 0;
        using var callerRoot = new NativeRuntime.RootHandle(99, _ => callerFrees++, _ => new IntPtr(99));
        Throws<InvalidOperationException>(() => graph.Allocate(() => { allocations++; return callerRoot; }));
        Equal(0, allocations); Equal(0, callerFrees); Equal(new IntPtr(99), callerRoot.Target); Equal(10, graph.RootCount);
        graph.Dispose(); Equal(0, graph.RootCount); Equal("10,9,8,7,6,5,4,3,2,1", string.Join(",", frees));
        callerRoot.Dispose(); Equal(1, callerFrees);
    }
    private static void AllocationFailure()
    {
        using var main = new MainThreadDispatcher(); using var graph = new LocalNativeMetadataRootGraph(main);
        var calls = 0; var frees = 0;
        Throws<IOException>(() => graph.Allocate(() => { calls++; throw new IOException("No root was returned."); }));
        Equal(1, calls); Equal(0, graph.RootCount); Check(!graph.IsDisposed && !graph.CleanupUnknown);
        graph.Allocate(() => new NativeRuntime.RootHandle(1, _ => frees++, _ => new IntPtr(42)));
        Equal(1, graph.RootCount); graph.Dispose(); Equal(1, frees); Equal(0, graph.RootCount);
    }
    private static void WrongThread()
    {
        using var main = new MainThreadDispatcher(); var frees = 0; var reads = 0;
        var graph = new LocalNativeMetadataRootGraph(main);
        var root = graph.Allocate(() => new NativeRuntime.RootHandle(1, _ => frees++, _ => new IntPtr(42)));
        using var owner = Owner(main, graph, root, () => reads++);
        owner.Read(); Exception? failure = null;
        var worker = new Thread(() => { try { owner.Dispose(); } catch (Exception error) { failure = error; } });
        worker.Start(); worker.Join(); Check(failure is InvalidOperationException);
        Check(!owner.IsDisposed && !owner.CleanupUnknown); Equal(1, owner.RootCount); Equal(0, frees);
        owner.Read(); Equal(2, reads); Equal(42L, owner.NativeIdentity);
        owner.Dispose(); Equal(1, frees); Equal(0, owner.RootCount); Check(owner.IsDisposed && !owner.CanLaunch);
    }
    private static void ReverseCleanup()
    {
        using var main = new MainThreadDispatcher(); using var graph = new LocalNativeMetadataRootGraph(main);
        var freed = new List<uint>();
        for (uint i = 1; i <= 3; i++)
        {
            var saved = i; graph.Allocate(() => new NativeRuntime.RootHandle(saved, freed.Add, _ => new IntPtr(saved)));
        }
        graph.Dispose(); graph.Dispose(); Equal("3,2,1", string.Join(",", freed));
        Equal(0, graph.RootCount); Check(graph.IsDisposed && !graph.CleanupUnknown);
    }
    private static void DisposedOwner()
    {
        using var main = new MainThreadDispatcher(); var reads = 0; var allocations = 0;
        var graph = new LocalNativeMetadataRootGraph(main);
        var root = graph.Allocate(() => new NativeRuntime.RootHandle(1, _ => { }, _ => new IntPtr(42)));
        using var owner = Owner(main, graph, root, () => reads++);
        var first = owner.Read(); Equal(first, owner.Observation); Equal(1, reads); Check(!owner.CanLaunch);
        owner.Dispose(); Throws<ObjectDisposedException>(() => owner.Read());
        Throws<ObjectDisposedException>(() => { _ = owner.Observation; });
        Throws<ObjectDisposedException>(() => { _ = owner.Pointer; });
        Throws<ObjectDisposedException>(() => { _ = owner.NativeIdentity; });
        Throws<ObjectDisposedException>(() => graph.Allocate(() => { allocations++; return root; }));
        Equal(0, allocations); Equal(1, reads); Equal(0, owner.RootCount);
    }
    private static void UnknownCleanup()
    {
        using var main = new MainThreadDispatcher(); var graph = new LocalNativeMetadataRootGraph(main);
        var freed = new List<uint>();
        for (uint i = 1; i <= 3; i++)
        {
            var saved = i;
            graph.Allocate(() => new NativeRuntime.RootHandle(saved, id => {
                freed.Add(id); if (id == 2) throw new IOException("Synthetic unknown native free outcome.");
            }, _ => new IntPtr(saved)));
        }
        Throws<AggregateException>(() => graph.Dispose());
        Equal("3,2,1", string.Join(",", freed)); Check(graph.IsDisposed && graph.CleanupUnknown); Equal(1, graph.RootCount);
        Throws<InvalidOperationException>(() => graph.Dispose());
        Throws<InvalidOperationException>(() => new LocalNativeMetadataRootGraph(main));
        Equal("3,2,1", string.Join(",", freed)); // No failed free was retried or independent free skipped.
    }
    private static OwnedNativeLocalMetadata Owner(MainThreadDispatcher main, LocalNativeMetadataRootGraph graph,
        NativeRuntime.RootHandle root, Action onRead)
        => new(main, Descriptor(), "local-level", "local-chart", graph, root, () => {
            onRead(); var descriptor = Descriptor();
            return new("native-source", descriptor.Title, descriptor.Artist, descriptor.ChartName,
                descriptor.ChartName, descriptor.DifficultyText, descriptor.Designer, 0, descriptor.ThemeColor);
        });
}
