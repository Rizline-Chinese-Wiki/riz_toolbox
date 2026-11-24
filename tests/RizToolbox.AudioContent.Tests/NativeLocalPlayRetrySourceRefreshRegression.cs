using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using static Assert;

internal static class NativeLocalPlayRetrySourceRefreshRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Retry refresh adopts the reconstructed current menu control and current same-difficulty judge rather than old live roots", ReconstructedSource),
        ("An accepted retry waits through known source NotReady and adopts the next actual source generation", WaitForSource),
        ("Retry source reentry and worker access cannot fork or replace a second input table", RetryReentry),
        ("Partial retry fork failure releases acquired borrowed roots and permanently rejects another preparation", PartialFork),
        ("Retry source cleanup failure retains Unknown responsibility without another free or source acquisition", RetryFreeFailure),
        ("A foreign destination or changed source cannot receive a prepared retry input table", ChangedOrForeign)
    };
    private static void ReconstructedSource()
    {
        using var f = new Fixture(); f.Generation = 2;
        Check(f.Owner.TryPrepare()); Equal(2, f.Backends.Single().Generation); Equal(2, f.Backends.Single().JudgeIndex);
        var destination = new NativeRuntime.RootHandle?[3]; f.Owner.TransferRootsTo(destination);
        Equal(new IntPtr(204), destination[0]!.Target); Equal(new IntPtr(201), destination[1]!.Target);
        Equal(new IntPtr(202), destination[2]!.Target); Equal(0, f.Owner.RootRecords);
        f.Owner.VerifyStable(); f.Owner.ReleaseBorrowedSourceAfterBegin();
        Equal(1, f.SourceReleases); Equal(0, f.ForkFrees.Count);
        foreach (var root in destination) root!.Dispose(); Equal(3, f.ForkFrees.Count);
        Throws<InvalidOperationException>(() => f.Owner.TryPrepare());
    }
    private static void WaitForSource()
    {
        using var f = new Fixture(); f.NotReady = true;
        Check(!f.Owner.TryPrepare()); Check(!f.Owner.Unknown); Equal(0, f.Owner.RootRecords);
        Equal(1, f.SourceReleases); Equal("1,0", string.Join(",", f.Backends[0].Frees));
        f.NotReady = false; f.Generation = 3; Check(f.Owner.TryPrepare());
        Equal(2, f.Backends.Count); Equal(2, f.Backends[1].JudgeIndex);
        var destination = new NativeRuntime.RootHandle?[3]; f.Owner.TransferRootsTo(destination);
        Equal(new IntPtr(302), destination[2]!.Target); f.Owner.ReleaseBorrowedSourceAfterBegin();
        foreach (var root in destination) root!.Dispose(); Equal(2, f.SourceReleases);
    }
    private static void RetryReentry()
    {
        using var f = new Fixture();
        f.OnFork = () => Throws<InvalidOperationException>(() => f.Owner.TryPrepare());
        Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.TryPrepare())).GetAwaiter().GetResult();
        Equal(0, f.Backends.Count); Check(f.Owner.TryPrepare()); Equal(3, f.Forks);
        var destination = new NativeRuntime.RootHandle?[3]; f.Owner.TransferRootsTo(destination);
        Throws<InvalidOperationException>(() => f.Owner.TransferRootsTo(new NativeRuntime.RootHandle?[3]));
        f.Owner.ReleaseBorrowedSourceAfterBegin(); foreach (var root in destination) root!.Dispose();
    }
    private static void PartialFork()
    {
        using var f = new Fixture(); f.FailForkAt = 2;
        Throws<IOException>(() => f.Owner.TryPrepare()); Check(f.Owner.Unknown);
        Equal(0, f.Owner.RootRecords); Equal(1, f.ForkFrees.Count); Equal(1, f.SourceReleases);
        Throws<InvalidOperationException>(() => f.Owner.TryPrepare()); Equal(1, f.Backends.Count);
    }
    private static void RetryFreeFailure()
    {
        using var f = new Fixture(); f.FailForkAt = 3; f.FailFreeAt = 1;
        Throws<IOException>(() => f.Owner.TryPrepare()); Check(f.Owner.Unknown);
        Equal(1, f.Owner.RootRecords); Equal(2, f.ForkFrees.Count); Equal(1, f.SourceReleases);
        Throws<InvalidOperationException>(() => f.Owner.TryPrepare()); Equal(2, f.ForkFrees.Count);
    }
    private static void ChangedOrForeign()
    {
        using (var f = new Fixture())
        {
            Check(f.Owner.TryPrepare()); using var foreign = new NativeRuntime.RootHandle(777, _ => { }, _ => new IntPtr(999));
            Throws<InvalidOperationException>(() => f.Owner.TransferRootsTo(new NativeRuntime.RootHandle?[] { foreign, null, null }));
            Check(!f.Owner.Unknown); Equal(3, f.Owner.RootRecords);
            var destination = new NativeRuntime.RootHandle?[3]; f.Owner.TransferRootsTo(destination);
            f.Owner.ReleaseBorrowedSourceAfterBegin(); foreach (var root in destination) root!.Dispose();
        }
        using (var f = new Fixture())
        {
            Check(f.Owner.TryPrepare()); f.Backends[0].Drift = true;
            // A source that becomes known-not-ready before transfer can wait;
            // all newly borrowed forks must first complete actual disposal.
            Check(!f.Owner.TryPrepare()); Check(!f.Owner.Unknown); Equal(0, f.Owner.RootRecords);
            Equal(3, f.ForkFrees.Count); Equal(1, f.SourceReleases);
        }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly NativeLocalPlayRetrySourceRefresh Owner;
        internal readonly List<Backend> Backends = new();
        internal readonly List<int> ForkFrees = new();
        internal int Generation = 1, SourceReleases, Forks, FailForkAt = -1, FailFreeAt = -1;
        internal bool NotReady;
        internal Action? OnFork;
        internal Fixture()
        {
            Owner = new(Main, 2, () => {
                var backend = new Backend(Main, Generation) { NotReady = NotReady }; Backends.Add(backend);
                return NativeLocalPlayLaunchSource.CreateControlled(Main, backend);
            }, pointer => {
                var sequence = ++Forks; OnFork?.Invoke();
                if (sequence == FailForkAt) throw new IOException("Controlled partial retry root allocation.");
                return new NativeRuntime.RootHandle((uint)(100 + sequence), _ => {
                    ForkFrees.Add(sequence); if (sequence == FailFreeAt) throw new IOException("Unknown retry root free.");
                }, _ => pointer, Main.VerifyAccess);
            }, source => { Check(source.Released); SourceReleases++; });
        }
        public void Dispose() => Main.Dispose();
    }
    private sealed class Backend : INativeLocalPlayLaunchSourceBackend
    {
        private readonly MainThreadDispatcher _main;
        internal readonly int Generation;
        internal readonly List<int> Frees = new();
        internal bool NotReady, Drift;
        internal int JudgeIndex;
        internal Backend(MainThreadDispatcher main, int generation) => (_main, Generation) = (main, generation);
        public bool CleanupUnknown => false;
        public void Capture(int index, Action<int, Func<NativeRuntime.RootHandle>> retain)
        {
            JudgeIndex = index;
            for (var slot = 0; slot < 4; ++slot)
            {
                if (slot == 2 && NotReady) throw new NativeLocalPlaySourceNotReadyException("Menu source not ready yet.");
                var captured = slot;
                retain(slot, () => new NativeRuntime.RootHandle((uint)(10 + captured), _ => Frees.Add(captured),
                    _ => new IntPtr(Generation * 100 + captured + 1), _main.VerifyAccess));
            }
        }
        public void VerifyStable(int index, IReadOnlyList<NativeRuntime.RootHandle?> roots)
        { Equal(JudgeIndex, index); if (Drift) throw new NativeLocalPlaySourceNotReadyException("Actual source changed before transfer."); }
    }
}
