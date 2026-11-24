using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Controlled scene/authority seam only. Actual Unity scene load, input
/// balance, native metadata and runtime callback scheduling need game acceptance.</summary>
internal static class NativeLocalRunSceneTransitionRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Scene transition captures origin before activation and requires an actual published operation", OperationPublication),
        ("Scene transition waits for exact async done, new loaded Level, cleared manager lock and slot", Completion),
        ("Scene transition rejects busy invalid or already-Level origins before native request", OriginAdmission),
        ("Scene transition rejects a different activation or changed origin without a native request", ExactActivation),
        ("Scene transition keeps void-only and throwing request outcomes unknown without retry", UnknownRequests),
        ("Scene transition rejects successor manager operation and foreign scene handoffs", ForeignState),
        ("Scene transition refuses cleared unfinished operations and premature observation release", PrematureClear),
        ("Scene transition worker and reentry rejection preserve one native request", ThreadAndReentry),
        ("Scene transition root release is independent, bounded and never retries unknown frees", Release)
    };
    private static void OperationPublication()
    {
        using var f = new Fixture(); Equal(new NativeLocalRunScene(10, "LevelList", true, true), f.Owner.Origin);
        Check(f.Owner.Read().Captured && !f.Owner.Attempted); Equal(0, f.Backend.Requests); Equal(2, f.Owner.RootRecords);
        f.Start(); Equal(1, f.Backend.Requests); Equal(601L, f.Owner.Read().AsyncOperation); Equal(601L, f.Backend.RetainedOperation);
        Check(f.Owner.Attempted && !f.Owner.Completed); Check(!f.Owner.Tick());
        Throws<InvalidOperationException>(() => f.Start()); Equal(1, f.Backend.Requests);
        Throws<InvalidOperationException>(() => f.Owner.ReleaseBeforeAttempt());
    }
    private static void Completion()
    {
        using var f = new Fixture(); f.Start();
        f.Backend.State = f.Backend.State with { ActiveScene = new(20, "Level", true, true) };
        Check(!f.Owner.Tick()); f.Backend.Done = true; Check(!f.Owner.Tick()); // Manager still owns its coroutine/slot.
        f.Backend.State = f.Backend.State with { Locked = false, AsyncOperation = 0 };
        Check(f.Owner.Tick()); var receipt = f.Owner.RequireCompleted();
        Equal(f.Authority.Identity, receipt.Session); Equal(101L, receipt.Controller); Equal(501L, receipt.Manager);
        Equal(601L, receipt.AsyncOperation); Equal(10, receipt.Origin.Handle); Equal(20, receipt.Destination.Handle);
        Equal(1, f.Backend.Retentions); Equal(1, f.Backend.Requests); Check(f.Owner.Completed);
    }
    private static void OriginAdmission()
    {
        foreach (var invalid in new[] {
            new NativeLocalRunSceneTransitionObservation(501, true, true, 0, new(10, "LevelList", true, true)),
            new NativeLocalRunSceneTransitionObservation(501, true, false, 601, new(10, "LevelList", true, true)),
            new NativeLocalRunSceneTransitionObservation(501, false, false, 0, new(10, "LevelList", true, true)),
            new NativeLocalRunSceneTransitionObservation(501, true, false, 0, new(10, "LevelList", true, false)),
            new NativeLocalRunSceneTransitionObservation(501, true, false, 0, new(10, "Level", true, true))
        })
        {
            using var main = new MainThreadDispatcher(); var backend = new Backend { State = invalid }; var authority = new Authority();
            NativeLocalRunSceneTransitionException? caught = null;
            try { NativeLocalRunSceneTransition.CaptureBackend(main, backend, authority); }
            catch (NativeLocalRunSceneTransitionException error) { caught = error; }
            Check(caught is not null && caught.Responsibility.CleanupUnknown); Equal(0, backend.Requests); Equal(1, authority.Unknowns);
        }
    }
    private static void ExactActivation()
    {
        using (var f = new Fixture())
        {
            f.Activation = f.Activation with { Controller = 999 };
            Throws<NativeLocalRunSceneTransitionException>(() => f.Start()); Equal(0, f.Backend.Requests); Check(f.Owner.CleanupUnknown);
        }
        using (var f = new Fixture())
        {
            f.Backend.State = f.Backend.State with { ActiveScene = new(11, "LevelList", true, true) };
            Throws<NativeLocalRunSceneTransitionException>(() => f.Start()); Equal(0, f.Backend.Requests);
        }
        using (var f = new Fixture())
        {
            f.Activation = f.Activation with { Graph = f.Activation.Graph with { ParentScene = 11 } };
            Throws<NativeLocalRunSceneTransitionException>(() => f.Start()); Equal(0, f.Backend.Requests);
        }
    }
    private static void UnknownRequests()
    {
        foreach (var mode in new[] { "void-only", "throw-after", "retention" })
        {
            using var f = new Fixture(); f.Backend.Fault = mode;
            Throws<NativeLocalRunSceneTransitionException>(() => f.Start()); Check(f.Owner.Attempted && f.Owner.CleanupUnknown);
            Check(!f.Owner.Completed); Equal(1, f.Backend.Requests); Equal(1, f.Authority.Unknowns);
            var reads = f.Backend.Reads;
            Throws<InvalidOperationException>(() => f.Start()); Throws<InvalidOperationException>(() => f.Owner.Tick());
            Throws<InvalidOperationException>(() => f.Owner.ReleaseAfterCompleted()); Equal(1, f.Backend.Requests); Equal(reads, f.Backend.Reads);
            Equal(0, f.Backend.Releases);
        }
    }
    private static void ForeignState()
    {
        foreach (var mutate in new Action<Backend>[] {
            b => b.State = b.State with { Manager = 502 },
            b => b.State = b.State with { AsyncOperation = 602 },
            b => b.State = b.State with { Locked = false },
            b => b.State = b.State with { ActiveScene = new(20, "MainMenu", true, true) },
            b => b.State = b.State with { ActiveScene = new(10, "Level", true, true) },
            b => b.State = b.State with { ActiveScene = new(20, "Level", true, false) }
        })
        {
            using var f = new Fixture(); f.Start(); mutate(f.Backend);
            Throws<NativeLocalRunSceneTransitionException>(() => f.Owner.Tick()); Check(f.Owner.CleanupUnknown);
            Equal(1, f.Authority.Unknowns); Equal(0, f.Backend.Releases);
        }
    }
    private static void PrematureClear()
    {
        using var f = new Fixture(); f.Start();
        Throws<InvalidOperationException>(() => f.Owner.ReleaseAfterCompleted()); Equal(0, f.Backend.Releases);
        f.Backend.State = f.Backend.State with { AsyncOperation = 0, Locked = false, ActiveScene = new(20, "Level", true, true) };
        Throws<NativeLocalRunSceneTransitionException>(() => f.Owner.Tick()); Check(f.Owner.CleanupUnknown);
        Equal(1, f.Backend.Requests); Equal(1, f.Backend.Retentions);
    }
    private static void ThreadAndReentry()
    {
        using var f = new Fixture(); var reads = f.Backend.Reads;
        Task.Run(() => { Throws<InvalidOperationException>(() => f.Start()); Throws<InvalidOperationException>(() => f.Owner.Tick()); })
            .GetAwaiter().GetResult(); Equal(reads, f.Backend.Reads); Equal(0, f.Backend.Requests);
        var rejected = 0; f.Backend.DuringRequest = () => {
            Throws<InvalidOperationException>(() => f.Start()); Throws<InvalidOperationException>(() => f.Owner.Tick()); rejected++;
        };
        f.Start(); Equal(1, rejected); Equal(1, f.Backend.Requests); Check(!f.Owner.CleanupUnknown);
    }
    private static void Release()
    {
        using (var f = new Fixture())
        {
            f.Owner.ReleaseBeforeAttempt(); Equal(1, f.Backend.Releases); Equal(0, f.Owner.RootRecords);
            Check(f.Owner.Read().Closed); Throws<InvalidOperationException>(() => f.Start());
        }
        using (var f = new Fixture())
        {
            f.Start(); f.Backend.Complete(); f.Owner.RequireCompleted(); f.Backend.Fault = "free";
            Throws<NativeLocalRunSceneTransitionException>(() => f.Owner.ReleaseAfterCompleted()); Check(f.Owner.CleanupUnknown);
            Equal(1, f.Backend.Releases); Equal(1, f.Owner.RootRecords);
            Throws<InvalidOperationException>(() => f.Owner.ReleaseAfterCompleted()); Equal(1, f.Backend.Releases);
        }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Backend Backend = new();
        internal readonly Authority Authority = new();
        internal readonly NativeLocalRunSceneTransition Owner;
        internal NativeInactiveControllerActivationReceipt Activation;
        internal Fixture()
        {
            Owner = NativeLocalRunSceneTransition.CaptureBackend(Main, Backend, Authority);
            Activation = new(Authority.Identity, 101, 201,
                new(101, 102, 103, 104, 105, true, false, false, true, true, 0, 201, 0, 30, 10, true, true),
                new("controlled", 101, true, true, 1, 1, 0, 0, 1, 1, 1, 1, 0, Array.Empty<ControllerAwakeLedgerRow>(), Array.Empty<string>()),
                new(501, true, true, new long[] { 1, 2, 3, 4, 5, 6 }));
        }
        internal void Start() { Authority.Activated = true; Owner.StartControlled(() => Activation); }
        public void Dispose()
        {
            try
            {
                if (Owner.Read().Closed || Owner.CleanupUnknown) return;
                if (!Owner.Attempted) Owner.ReleaseBeforeAttempt();
                else { Backend.Complete(); Owner.RequireCompleted(); Owner.ReleaseAfterCompleted(); }
            }
            finally { Main.Dispose(); }
        }
    }
    private sealed class Backend : ILocalRunSceneTransitionBackend
    {
        internal NativeLocalRunSceneTransitionObservation State = new(501, true, false, 0, new(10, "LevelList", true, true));
        internal int Requests, Reads, Retentions, Releases;
        internal bool Done; internal string? Fault; internal Action? DuringRequest;
        public bool CleanupUnknown { get; private set; }
        public int RootRecords { get; private set; }
        public int RetainedArgumentRecords => Fault == "throw-after" ? 3 : 0;
        public long RetainedOperation { get; private set; }
        public void Capture() => RootRecords = 2;
        public NativeLocalRunSceneTransitionObservation ReadState() { Reads++; return State; }
        public NativeLocalRunScene ReadScene(int handle) => State.ActiveScene.Handle == handle ? State.ActiveScene : new(handle, "", false, false);
        public void RequestOnce()
        {
            Requests++; DuringRequest?.Invoke();
            if (Fault == "void-only") return;
            State = State with { Locked = true, AsyncOperation = 601 };
            if (Fault == "throw-after") { CleanupUnknown = true; throw new IOException("Controlled native request outcome is unknown."); }
        }
        public void RetainOperation(long operation)
        {
            Retentions++; if (Fault == "retention") throw new IOException("Controlled operation root allocation failed.");
            RetainedOperation = operation; RootRecords++;
        }
        public bool OperationDone() => Done;
        internal void Complete() { Done = true; State = State with { Locked = false, AsyncOperation = 0, ActiveScene = new(20, "Level", true, true) }; }
        public void ReleaseRoots()
        {
            Releases++; RootRecords = 0;
            if (Fault == "free") { RootRecords = 1; CleanupUnknown = true; throw new IOException("Controlled independent free failed."); }
        }
    }
    private sealed class Authority : ILocalRunSceneTransitionAuthority
    {
        internal bool Activated; internal int Unknowns;
        public LocalRunSessionIdentity Identity { get; } = new("controlled-scene", new("scene.fixture", "song"), 1, "chart", 1);
        public long Controller => 101;
        public void VerifyBeforeCapture() { if (Activated) throw new InvalidOperationException("Capture must precede activation."); }
        public void VerifyActivated(NativeInactiveControllerActivationReceipt receipt)
        { if (!Activated || receipt.Session != Identity || receipt.Controller != Controller) throw new InvalidOperationException("Wrong controlled activation."); }
        public void VerifyPending() { if (Unknowns != 0) throw new InvalidOperationException("Controlled run is unknown."); }
        public void MarkUnknown(string failure) => Unknowns++;
    }
}
