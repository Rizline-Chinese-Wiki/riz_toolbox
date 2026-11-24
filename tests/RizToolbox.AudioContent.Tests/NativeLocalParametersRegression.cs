using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

/// <summary>
/// Admission and new-root responsibility only; no positive native constructor/game acceptance.
/// Run before NativeLocalMetadataRegression, whose final unknown-free case blocks its own root graph.
/// </summary>
internal static class NativeLocalParametersRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Borrowed parameters reject null and mismatched input owners without consuming caller roots", Admission),
        ("Borrowed parameters reject unsupported builds before new native allocation", UnsupportedBuild),
        ("Borrowed parameters reject expired metadata and released wrappers", ExpiredInputs),
        ("Borrowed parameter wrong-thread admission leaves all caller inputs usable", FactoryThread),
        ("Borrowed parameter root reservation enforces 32 owners and recovers confirmed slots", OwnerQuota),
        ("Borrowed parameter temporary root capacity rejects allocation before caller ownership changes", RootQuota),
        ("Borrowed parameter creation keeps only its new output root and frees temporary guards in reverse order", KeepOutput),
        ("Borrowed parameter handle refuses expired inputs and wrong-thread cleanup while preserving its own root", BorrowedLiveness),
        ("Borrowed parameter observations preserve original scalar bits including negative zero", ScalarObservation),
        // Intentionally last: unknown free responsibility is process-owned and cannot be reset.
        ("Unknown temporary parameter root free also attempts the independent output and is never retried", UnknownCleanup)
    };

    private static LocalNativeMetadataDescriptor Descriptor() => new(new("probe.provider", "song"), 1, "easy",
        "Same title", "Artist", "LOCAL", "3", "Designer", new Rgba(.2f, .3f, .4f, 1), 10, 1_000_000, 3);
    private static void Admission()
    {
        using var f = new Inputs(); var before = f.Native.ActiveRoots;
        Throws<ArgumentNullException>(() => f.Create(metadata: null));
        Throws<ArgumentNullException>(() => f.Factory.CreateBorrowing(f.Metadata, null!, f.Cover, f.Audio, f.Chart, f.Judge));
        Throws<ArgumentNullException>(() => f.Factory.CreateBorrowing(f.Metadata, Descriptor(), null!, f.Audio, f.Chart, f.Judge));
        Throws<ArgumentNullException>(() => f.Factory.CreateBorrowing(f.Metadata, Descriptor(), f.Cover, null!, f.Chart, f.Judge));
        Throws<ArgumentNullException>(() => f.Factory.CreateBorrowing(f.Metadata, Descriptor(), f.Cover, f.Audio, null!, f.Judge));
        Throws<ArgumentNullException>(() => f.Factory.CreateBorrowing(f.Metadata, Descriptor(), f.Cover, f.Audio, f.Chart, null!));
        foreach (var expected in new[] {
            Descriptor() with { Revision = 2 }, Descriptor() with { LocalId = new("other", "song") },
            Descriptor() with { ChartId = "hard" }, Descriptor() with { Title = "Different" }
        }) Throws<ArgumentException>(() => f.Factory.CreateBorrowing(f.Metadata, expected, f.Cover, f.Audio, f.Chart, f.Judge));
        Throws<UnsupportedSignatureException>(() => f.Factory.CreateBorrowing(f.Metadata, Descriptor(), f.Audio, f.Cover, f.Chart, f.Judge));
        Equal(before, f.Native.ActiveRoots); Equal(0, f.Native.Invocations);
        Check(!f.Metadata.IsDisposed && !f.Cover.IsReleased && !f.Audio.IsReleased);
    }
    private static void UnsupportedBuild()
    {
        using var f = new Inputs(); var before = f.Native.ActiveRoots;
        Throws<UnsupportedSignatureException>(() => f.Create());
        Equal(before, f.Native.ActiveRoots); Equal(0, f.Native.Invocations); Equal(0, f.Native.FreedRoots);
        _ = f.Metadata.Pointer; _ = f.Cover.Pointer; _ = f.Audio.Pointer; _ = f.Chart.Target; _ = f.Judge.Target;
    }
    private static void ExpiredInputs()
    {
        using (var f = new Inputs())
        {
            f.Metadata.Dispose(); var before = f.Native.ActiveRoots;
            Throws<ObjectDisposedException>(() => f.Create());
            Equal(before, f.Native.ActiveRoots); Equal(0, f.Native.Invocations);
        }
        using (var f = new Inputs())
        {
            f.Cover.Dispose(); var before = f.Native.ActiveRoots; var invocations = f.Native.Invocations;
            Throws<ObjectDisposedException>(() => f.Create());
            Equal(before, f.Native.ActiveRoots); Equal(invocations, f.Native.Invocations); Check(!f.Audio.IsReleased);
        }
        using (var f = new Inputs())
        {
            f.Chart.Dispose(); var before = f.Native.ActiveRoots;
            // The unsupported build must win before consulting this disposed caller GC handle.
            Throws<UnsupportedSignatureException>(() => f.Create());
            Equal(before, f.Native.ActiveRoots); Equal(0, f.Native.Invocations);
        }
    }
    private static void FactoryThread()
    {
        using var f = new Inputs(); var before = f.Native.ActiveRoots;
        Task.Run(() => {
            Throws<InvalidOperationException>(() => f.Create());
            Throws<InvalidOperationException>(() => f.Factory.CreateBorrowing(null!, null!, null!, null!, null!, null!));
        }).GetAwaiter().GetResult();
        Equal(before, f.Native.ActiveRoots); Equal(0, f.Native.FreedRoots); Equal(0, f.Native.Invocations);
        Throws<UnsupportedSignatureException>(() => f.Create()); // Main-thread retry still reaches the ordinary build gate.
    }
    private static void OwnerQuota()
    {
        using var main = new MainThreadDispatcher(); var held = new List<BorrowedLocalParametersRoots>();
        try
        {
            for (var i = 0; i < 32; i++) held.Add(new(main));
            Throws<InvalidOperationException>(() => new BorrowedLocalParametersRoots(main));
            held[0].Dispose(); using var replacement = new BorrowedLocalParametersRoots(main);
            Throws<InvalidOperationException>(() => new BorrowedLocalParametersRoots(main));
        }
        finally { foreach (var roots in held) roots.Dispose(); }
        using var later = new BorrowedLocalParametersRoots(main);
    }
    private static void RootQuota()
    {
        using var main = new MainThreadDispatcher(); using var roots = new BorrowedLocalParametersRoots(main);
        var frees = 0; var allocations = 0;
        for (uint id = 1; id <= 8; id++)
        { var saved = id; roots.Allocate(() => new NativeRuntime.RootHandle(saved, _ => frees++, _ => new IntPtr(saved))); }
        using var caller = new NativeRuntime.RootHandle(99, _ => frees++, _ => new IntPtr(99));
        Throws<InvalidOperationException>(() => roots.Allocate(() => { allocations++; return caller; }));
        Equal(0, allocations); Equal(0, frees); Equal(new IntPtr(99), caller.Target);
        roots.Dispose(); Equal(8, frees); Equal(0, roots.RootCount); Equal(new IntPtr(99), caller.Target);
    }
    private static void KeepOutput()
    {
        using var main = new MainThreadDispatcher(); using var roots = new BorrowedLocalParametersRoots(main);
        var freed = new List<uint>(); NativeRuntime.RootHandle output = null!;
        for (uint id = 1; id <= 8; id++)
        { var saved = id; output = roots.Allocate(() => new NativeRuntime.RootHandle(saved, freed.Add, _ => new IntPtr(saved))); }
        roots.KeepOnly(output); Equal(1, roots.RootCount); Equal("7,6,5,4,3,2,1", string.Join(",", freed));
        Equal(new IntPtr(8), output.Target); roots.Dispose(); roots.Dispose();
        Equal("7,6,5,4,3,2,1,8", string.Join(",", freed)); Equal(0, roots.RootCount);
    }
    private static void BorrowedLiveness()
    {
        using var main = new MainThreadDispatcher(); var roots = new BorrowedLocalParametersRoots(main);
        var frees = 0; var reads = 0; var borrowedActive = true;
        var parameter = roots.Allocate(() => new NativeRuntime.RootHandle(1, _ => frees++, _ => new IntPtr(42)));
        using var owner = new BorrowedNativeLocalParameters(main, roots, parameter,
            () => { if (!borrowedActive) throw new ObjectDisposedException("borrowed input owner"); },
            () => { reads++; return new(1, 2, 3, 4, 5, 0, 0); });
        owner.Read(); Equal(1, reads); Equal(new IntPtr(42), owner.Pointer);
        borrowedActive = false;
        Throws<ObjectDisposedException>(() => owner.Read()); Throws<ObjectDisposedException>(() => { _ = owner.Pointer; });
        Throws<ObjectDisposedException>(() => { _ = owner.Observation; }); Equal(1, reads); Equal(0, frees);
        borrowedActive = true;
        Task.Run(() => {
            Throws<InvalidOperationException>(() => owner.Dispose());
            Throws<InvalidOperationException>(() => owner.Read());
        }).GetAwaiter().GetResult();
        Check(!owner.IsDisposed); Equal(0, frees); owner.Read(); Equal(2, reads);
        borrowedActive = false; owner.Dispose(); owner.Dispose(); Equal(1, frees); Check(owner.IsDisposed);
        Throws<ObjectDisposedException>(() => owner.Read());
    }
    private static void ScalarObservation()
    {
        var observed = new LocalNativeParametersObservation(1, 2, 3, 4, 5,
            BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), BitConverter.Int32BitsToSingle(0x7fc12345));
        Equal(unchecked((int)0x80000000), observed.SpeedBits); Equal(0x7fc12345, observed.OffsetBits);
    }
    private static void UnknownCleanup()
    {
        using var main = new MainThreadDispatcher(); var roots = new BorrowedLocalParametersRoots(main);
        var freed = new List<uint>(); var callerFrees = 0;
        using var caller = new NativeRuntime.RootHandle(99, _ => callerFrees++, _ => new IntPtr(99));
        NativeRuntime.RootHandle output = null!;
        for (uint id = 1; id <= 3; id++)
        {
            var saved = id; output = roots.Allocate(() => new NativeRuntime.RootHandle(saved, value => {
                freed.Add(value); if (value == 1) throw new IOException("Synthetic unknown temporary root free.");
            }, _ => new IntPtr(saved)));
        }
        Throws<AggregateException>(() => roots.KeepOnly(output));
        Equal("2,1,3", string.Join(",", freed)); Equal(1, roots.RootCount); Check(roots.IsDisposed && roots.CleanupUnknown);
        Throws<InvalidOperationException>(() => roots.Dispose()); Throws<InvalidOperationException>(() => new BorrowedLocalParametersRoots(main));
        Equal("2,1,3", string.Join(",", freed)); Equal(0, callerFrees); Equal(new IntPtr(99), caller.Target);
    }

    private sealed class Inputs : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly FakeNative Native = new();
        internal readonly NativeLocalParametersFactory Factory;
        internal readonly OwnedNativeLocalMetadata Metadata;
        internal readonly NativeResourceReferenceHandle Cover, Audio;
        internal readonly NativeRuntime.RootHandle Chart, Judge;
        internal Inputs()
        {
            Factory = new(null!, Main, new AdapterBuildProfile("test", "", "test", "test", "test", "test",
                new Dictionary<string, TypeBinding>()), IntPtr.Zero);
            var graph = new LocalNativeMetadataRootGraph(Main);
            var root = graph.Allocate(() => Native.Api.Root(Native.Instance)); var descriptor = Descriptor();
            Metadata = new(Main, descriptor, "level", "chart", graph, root,
                () => new("LevelList", descriptor.Title, descriptor.Artist, descriptor.ChartName, descriptor.ChartName,
                    descriptor.DifficultyText, descriptor.Designer, 0, descriptor.ThemeColor));
            Cover = Wrapper("UnityEngine.Sprite"); Audio = Wrapper("UnityEngine.AudioClip");
            Chart = Native.Api.Root(Native.Instance); Judge = Native.Api.Root(Native.Instance);
        }
        private NativeResourceReferenceHandle Wrapper(string type)
        {
            var reservation = NativeResourceReferenceHandle.Reserve();
            var value = new NativeResourceReferenceHandle(Native.Api, Main, Native.Api.Root(Native.Instance),
                Native.Api.Root(Native.OtherObject), type, Native.ClassOf(Native.String), Native.Method, Native.Method, false, reservation);
            reservation.Transfer(value); return value;
        }
        internal BorrowedNativeLocalParameters Create() => Factory.CreateBorrowing(Metadata, Descriptor(), Cover, Audio, Chart, Judge);
        internal BorrowedNativeLocalParameters Create(OwnedNativeLocalMetadata? metadata)
            => Factory.CreateBorrowing(metadata!, Descriptor(), Cover, Audio, Chart, Judge);
        public void Dispose() { Metadata.Dispose(); Cover.Dispose(); Audio.Dispose(); Chart.Dispose(); Judge.Dispose(); Native.Dispose(); Main.Dispose(); }
    }
}
