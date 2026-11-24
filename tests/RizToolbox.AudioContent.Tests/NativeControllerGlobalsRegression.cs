using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using static Assert;

/// <summary>Controlled responsibility backend only; does not prove native delegate metadata, Unity liveness or consumer end.</summary>
internal static class NativeControllerGlobalsRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Controller globals capture roots all six baselines and preserve caller ownership", Baseline),
        ("Controller globals snapshots retain complete delegate reads then return immutable baseline and current copies", Snapshots),
        ("Controller globals current snapshots reject wrong threads and reentry without native writes", SnapshotAdmission),
        ("Controller globals require six exact single-cast controller targets before complete installation", InstalledTargets),
        ("Controller globals restore only exact installed delegates and restore controllerSet last", Restore),
        ("Controller globals preserve successor delegates and never overwrite a replacement manager", Successors),
        ("Controller globals reject destroyed baseline targets while restoring independent safe fields", DeadBaseline),
        ("Controller globals recheck six baseline identities before changing controllerSet", FlagRace),
        ("Controller globals retain unknown setter outcomes and never repeat restoration writes", SetterFailure),
        ("Controller globals reject wrong-thread capture restore and release without consuming roots", WrongThread),
        ("Published controller globals require external consumer-end proof and reject cross-owner tokens", ConsumerProof),
        ("Controller globals verifier and setter reentry preserve host responsibility", Reentry),
        ("Native globals validation rejects retain failed root frees in bounded records without retry", RegisteredValidationFailure),
        ("Controller globals independently release roots and retain unknown free responsibility without retry", CleanupFailure)
    };

    private static void Baseline()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main);
        var owner = OwnedNativeControllerGlobals.CaptureBackend(main, backend);
        Equal(7, owner.OutstandingRootRecords); Equal(6, backend.Roots.Count); Equal(0, backend.Writes.Count);
        Check(!owner.InstalledCaptureComplete); owner.Dispose(); owner.Dispose(); Check(owner.IsClosed);
        Equal(1, backend.Disposals); Check(backend.Roots.All(x => x.Frees == 1));
        var invalid = new Backend(main); invalid.DeadTargets.Add(101);
        Throws<InvalidOperationException>(() => OwnedNativeControllerGlobals.CaptureBackend(main, invalid));
        Equal(1, invalid.Disposals); Check(invalid.Roots.All(x => x.Frees == 1));
    }
    private static void InstalledTargets()
    {
        using var f = new Fixture(); f.Backend.Install(); f.Backend.Targets[202] = 999;
        Throws<AggregateException>(() => f.Capture()); Check(!f.Owner.InstalledCaptureComplete && f.Owner.CleanupUnknown);
        Equal(13, f.Backend.Roots.Count); Equal(0, f.Backend.Writes.Count);
        Throws<InvalidOperationException>(() => f.Capture());
        var report = f.Owner.RestoreIfOwned();
        Check(!report.InstalledCaptureComplete); Equal(5, f.Backend.Writes.Count); Equal(0, f.Backend.FlagWrites);
        Equal(202L, f.Backend.Fields[2]);
    }
    private static void Snapshots()
    {
        using var f = new Fixture(); var baseline = f.Owner.BaselineSnapshot();
        Check(baseline.IsCurrentOwner && !baseline.ControllerSet); Equal(f.Backend.OwnerIdentity, baseline.OwnerIdentity);
        Check(baseline.DelegateIdentities.SequenceEqual(Enumerable.Range(100, 6).Select(x => (long)x)));
        f.Backend.Install(); var readsBefore = f.Backend.Roots.Count;
        var current = f.Owner.ObserveCurrent(); Check(current.IsCurrentOwner && current.ControllerSet);
        Check(current.DelegateIdentities.SequenceEqual(Enumerable.Range(200, 6).Select(x => (long)x)));
        Equal(readsBefore + 6, f.Backend.Roots.Count); Check(f.Backend.Roots.Skip(readsBefore).All(x => x.Frees == 1));
        Equal(0, f.Backend.Writes.Count); Equal(0, f.Backend.FlagWrites);
        f.Backend.Fields[0] = 999;
        Equal(100L, baseline.DelegateIdentities[0]); Equal(200L, current.DelegateIdentities[0]);
        // The test backend externally retires globals; the snapshot itself confers no proof.
        f.End();
    }
    private static void SnapshotAdmission()
    {
        using var f = new Fixture(); Exception? failure = null; var rootCount = f.Backend.Roots.Count;
        var worker = new Thread(() => { try { f.Owner.ObserveCurrent(); } catch (Exception error) { failure = error; } });
        worker.Start(); worker.Join(); Check(failure is InvalidOperationException); Equal(rootCount, f.Backend.Roots.Count);
        var rejected = 0;
        f.Backend.BeforeCapture = () => { Throws<InvalidOperationException>(() => f.Owner.ObserveCurrent()); rejected++; };
        f.Owner.ObserveCurrent(); Equal(6, rejected); Equal(0, f.Backend.Writes.Count); Equal(0, f.Backend.FlagWrites);
        f.Backend.BeforeCapture = null;
    }
    private static void Restore()
    {
        using var f = new Fixture(); f.InstallAndCapture();
        var report = f.Owner.RestoreIfOwned(); Check(report.AllDelegatesDetached && report.ControllerSetRestored);
        Equal("0,1,2,3,4,5", string.Join(",", f.Backend.Writes));
        Check(f.Backend.Fields.SequenceEqual(Enumerable.Range(100, 6).Select(x => (long)x)));
        Check(!f.Backend.ControllerSet); Equal(1, f.Backend.FlagWrites);
        Equal(report, f.Owner.RestoreIfOwned()); Equal(6, f.Backend.Writes.Count); Equal(1, f.Backend.FlagWrites);
        Throws<InvalidOperationException>(() => f.Owner.Dispose()); Check(!f.Owner.IsClosed);
    }
    private static void Successors()
    {
        using (var f = new Fixture())
        {
            f.InstallAndCapture(); f.Backend.Fields[2] = 302; f.Backend.Targets[302] = 888;
            var report = f.Owner.RestoreIfOwned(); Equal(302L, f.Backend.Fields[2]);
            Equal(ControllerGlobalRestoreState.SuccessorPreserved, report.Fields[2].State);
            Equal(5, f.Backend.Writes.Count); Equal(0, f.Backend.FlagWrites); Check(f.Backend.ControllerSet);
        }
        using (var f = new Fixture())
        {
            f.InstallAndCapture(); f.Backend.Fields[2] = 302; f.Backend.Targets[302] = 777;
            var report = f.Owner.RestoreIfOwned(); Check(!report.AllDelegatesDetached);
            Equal(ControllerGlobalRestoreState.OwnedDelegateChanged, report.Fields[2].State); Equal(302L, f.Backend.Fields[2]);
        }
        using (var f = new Fixture())
        {
            f.InstallAndCapture(); f.Backend.Current = false;
            var report = f.Owner.RestoreIfOwned(); Check(!report.SameManagerOwner);
            Equal(0, f.Backend.Writes.Count); Equal(0, f.Backend.FlagWrites);
        }
    }
    private static void DeadBaseline()
    {
        using var f = new Fixture(); f.InstallAndCapture(); f.Backend.DeadTargets.Add(102);
        var report = f.Owner.RestoreIfOwned(); Check(!report.AllDelegatesDetached && !report.ControllerSetRestored);
        Equal(ControllerGlobalRestoreState.BaselineUnsafe, report.Fields[2].State);
        Equal(202L, f.Backend.Fields[2]); Equal(5, f.Backend.Writes.Count); Equal(0, f.Backend.FlagWrites);
        Check(f.Owner.CleanupUnknown);
    }
    private static void FlagRace()
    {
        using var f = new Fixture(); f.InstallAndCapture(); f.Backend.BeforeFlag = () => {
            f.Backend.Fields[0] = 400; f.Backend.Targets[400] = 999;
        };
        var report = f.Owner.RestoreIfOwned(); Check(!report.ControllerSetRestored);
        Equal(400L, f.Backend.Fields[0]); Check(f.Backend.ControllerSet); Equal(0, f.Backend.FlagWrites);
    }
    private static void SetterFailure()
    {
        using var f = new Fixture(); f.InstallAndCapture(); f.Backend.ThrowAfterWrite = 2;
        var report = f.Owner.RestoreIfOwned(); Check(!report.AllDelegatesDetached && f.Owner.CleanupUnknown);
        Equal(ControllerGlobalRestoreState.WriteUnknown, report.Fields[2].State);
        Equal(6, f.Backend.Writes.Count); Equal(102L, f.Backend.Fields[2]); Equal(0, f.Backend.FlagWrites);
        f.Owner.RestoreIfOwned(); Equal(6, f.Backend.Writes.Count);
    }
    private static void WrongThread()
    {
        using var f = new Fixture(); f.Backend.Install(); var errors = new List<Exception>();
        var worker = new Thread(() => {
            try { f.Owner.CaptureInstalledFor(f.Controller); } catch (Exception error) { errors.Add(error); }
            try { f.Owner.Dispose(); } catch (Exception error) { errors.Add(error); }
        });
        worker.Start(); worker.Join(); Equal(2, errors.Count); Equal(6, f.Backend.Roots.Count);
        f.Capture(); worker = new Thread(() => {
            try { f.Owner.RestoreIfOwned(); } catch (Exception error) { errors.Add(error); }
            try { f.Owner.VerifyConsumersEnded(new Verifier(true)); } catch (Exception error) { errors.Add(error); }
        });
        worker.Start(); worker.Join(); Equal(4, errors.Count); Equal(0, f.Backend.Writes.Count);
        Check(f.Backend.Roots.All(x => x.Frees == 0)); f.Owner.RestoreIfOwned();
    }
    private static void ConsumerProof()
    {
        using var f = new Fixture(); using var other = new Fixture(); f.InstallAndCapture(); other.InstallAndCapture();
        Throws<InvalidOperationException>(() => f.Owner.Dispose());
        Check(f.Owner.VerifyConsumersEnded(new Verifier(false)) is null); Check(f.Backend.Roots.All(x => x.Frees == 0));
        var otherToken = other.Owner.VerifyConsumersEnded(new Verifier(true))!;
        Throws<InvalidOperationException>(() => f.Owner.ReleaseAfterConsumersEnded(otherToken));
        Equal(0, f.Backend.Disposals); Check(f.Backend.Roots.All(x => x.Frees == 0));
        f.End(); Check(f.Owner.IsClosed); Check(f.Backend.Roots.All(x => x.Frees == 1));
    }
    private static void Reentry()
    {
        using var f = new Fixture(); f.InstallAndCapture(); var rejected = 0;
        f.Backend.BeforeWrite = () => { Throws<InvalidOperationException>(() => f.Owner.RestoreIfOwned()); rejected++; };
        f.Owner.RestoreIfOwned(); Equal(6, rejected);
        var proof = f.Owner.VerifyConsumersEnded(new Verifier(true, () => {
            Throws<InvalidOperationException>(() => f.Owner.Dispose()); rejected++;
        }))!;
        f.Owner.ReleaseAfterConsumersEnded(proof); Equal(7, rejected); Check(f.Owner.IsClosed);
    }
    private static void CleanupFailure()
    {
        using var f = new Fixture(); f.InstallAndCapture(); f.Owner.RestoreIfOwned();
        var failed = f.Backend.Roots[3]; failed.FailFree = true;
        var proof = f.Owner.VerifyConsumersEnded(new Verifier(true))!;
        Throws<AggregateException>(() => f.Owner.ReleaseAfterConsumersEnded(proof));
        Check(f.Owner.CleanupUnknown && !f.Owner.IsClosed); Equal(1, f.Owner.OutstandingRootRecords);
        Check(f.Backend.Roots.All(x => x.Attempts == 1)); Equal(1, f.Backend.Disposals);
        Throws<InvalidOperationException>(() => f.Owner.ReleaseAfterConsumersEnded(proof)); Equal(1, failed.Attempts);
        f.IgnoreUnknown = true;
    }
    private static void RegisteredValidationFailure()
    {
        using var main = new MainThreadDispatcher(); var freeCalls = 0;
        var profile = new AdapterBuildProfile("test", "", "test", "test", "test", "test", new Dictionary<string, TypeBinding>());
        var backend = new NativeControllerGlobalsBackend(null!, main, profile, IntPtr.Zero,
            value => new NativeRuntime.RootHandle(1, _ => { freeCalls++; throw new IOException("Unknown root free after validation rejection."); }, _ => value, main.VerifyAccess));
        Throws<AggregateException>(() => backend.RetainValidated(new IntPtr(123), _ => throw new InvalidDataException("Rejected exact native class.")));
        Equal(1, freeCalls); Equal(1, backend.OutstandingRootRecords);
        Throws<InvalidOperationException>(() => backend.RetainValidated(new IntPtr(124), _ => { }));
        Throws<AggregateException>(() => backend.Dispose()); Equal(1, freeCalls); Equal(1, backend.OutstandingRootRecords);
        Throws<InvalidOperationException>(() => backend.Dispose()); Equal(1, freeCalls);
        // A production backend is already rooted by its process-owned coordinator before a native
        // root can be created. This injected standalone allocator deliberately performs no native work.
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Backend Backend;
        internal readonly OwnedNativeControllerGlobals Owner;
        internal readonly NativeRuntime.RootHandle Controller;
        internal bool IgnoreUnknown;
        internal Fixture()
        {
            Backend = new(Main); Owner = OwnedNativeControllerGlobals.CaptureBackend(Main, Backend);
            Controller = new(1, _ => { }, _ => new IntPtr(777), Main.VerifyAccess);
        }
        internal void Capture() => Owner.CaptureInstalledFor(Controller);
        internal void InstallAndCapture() { Backend.Install(); Capture(); }
        internal void End()
        {
            if (Owner.IsClosed || IgnoreUnknown) return;
            if (Backend.Installed)
            {
                var proof = Owner.VerifyConsumersEnded(new Verifier(true))!; Owner.ReleaseAfterConsumersEnded(proof);
            }
            else Owner.Dispose();
        }
        public void Dispose() { try { End(); } finally { Controller.Dispose(); Main.Dispose(); } }
    }
    private sealed class Ref : IControllerGlobalsReference
    {
        private readonly MainThreadDispatcher _main;
        private readonly long _identity;
        internal int Frees, Attempts; internal bool FailFree;
        internal Ref(MainThreadDispatcher main, long identity) => (_main, _identity) = (main, identity);
        public long Identity { get { _main.VerifyAccess(); if (Attempts != 0) throw new ObjectDisposedException(nameof(Ref)); return _identity; } }
        public void Dispose() { _main.VerifyAccess(); Attempts++; if (Attempts > 1) throw new InvalidOperationException("Repeated root free."); if (FailFree) throw new IOException("Unknown native free."); Frees++; }
    }
    private sealed class Backend : IControllerGlobalsBackend
    {
        private static long _nextOwner = 1000;
        private readonly MainThreadDispatcher _main;
        public long OwnerIdentity { get; } = Interlocked.Increment(ref _nextOwner);
        internal bool Current = true, ControllerSet, Installed;
        internal readonly long[] Fields = Enumerable.Range(100, 6).Select(x => (long)x).ToArray();
        internal readonly Dictionary<long, long> Targets = Enumerable.Range(100, 6).ToDictionary(x => (long)x, x => (long)x);
        internal readonly HashSet<long> DeadTargets = new();
        internal readonly List<Ref> Roots = new(); internal readonly List<int> Writes = new();
        internal Action? BeforeWrite, BeforeFlag, BeforeCapture;
        internal int ThrowAfterWrite = -1, FlagWrites, Disposals;
        internal Backend(MainThreadDispatcher main) => _main = main;
        public bool IsCurrentOwner { get { _main.VerifyAccess(); return Current; } }
        internal void Install()
        { for (var index = 0; index < 6; index++) { Fields[index] = 200 + index; Targets[200 + index] = 777; } ControllerSet = true; Installed = true; }
        private Ref Root(long identity) { var root = new Ref(_main, identity); Roots.Add(root); return root; }
        public IControllerGlobalsReference RetainController(IntPtr controller) { _main.VerifyAccess(); Equal(777L, controller.ToInt64()); return Root(777); }
        public IControllerGlobalsReference? CaptureDelegate(int index) { _main.VerifyAccess(); BeforeCapture?.Invoke(); return Fields[index] == 0 ? null : Root(Fields[index]); }
        public void VerifyBaseline(int index, IControllerGlobalsReference? value)
        { _main.VerifyAccess(); if (value is not null && DeadTargets.Contains(Targets[value.Identity])) throw new InvalidOperationException("Destroyed baseline target."); }
        public bool TargetsController(int index, IControllerGlobalsReference value, long controller)
        { _main.VerifyAccess(); return Targets[value.Identity] == controller; }
        public bool ReadControllerSet() { _main.VerifyAccess(); return ControllerSet; }
        public bool WriteDelegateIfSame(int index, long expected, IControllerGlobalsReference? baseline)
        {
            _main.VerifyAccess(); BeforeWrite?.Invoke(); if (!Current || Fields[index] != expected) return false;
            Writes.Add(index); Fields[index] = baseline?.Identity ?? 0;
            if (ThrowAfterWrite == index) throw new IOException("Native setter threw after mutation.");
            return true;
        }
        public bool WriteControllerSetIfSame(bool expected, bool baseline, IReadOnlyList<long> expectedDelegates)
        {
            _main.VerifyAccess(); BeforeFlag?.Invoke();
            if (!Current || ControllerSet != expected || !Fields.SequenceEqual(expectedDelegates)) return false;
            FlagWrites++; ControllerSet = baseline; return true;
        }
        public void Dispose() { _main.VerifyAccess(); Disposals++; if (Disposals > 1) throw new InvalidOperationException("Backend cleanup repeated."); }
    }
    private sealed class Verifier : IControllerGlobalsConsumersEndedVerifier
    {
        private readonly bool _result; private readonly Action? _check;
        internal Verifier(bool result, Action? check = null) => (_result, _check) = (result, check);
        public bool Verify(ControllerGlobalsRetirementRequest request, out string evidence)
        { _check?.Invoke(); evidence = "Controlled backend ends all controller/global consumers; no native Unity verification is claimed."; return _result; }
    }
}
