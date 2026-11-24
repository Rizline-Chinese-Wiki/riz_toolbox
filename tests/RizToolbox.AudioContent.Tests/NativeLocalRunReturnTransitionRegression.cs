using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Controlled backend tests. These do not execute a Unity scene load,
/// certify native metadata, or prove that all run consumers have ended.</summary>
internal static class NativeLocalRunReturnTransitionRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Return derives its exact destination from the actual completed launch owner", Completion),
        ("Return waits for the original Level to unload after the target is active", WaitForUnload),
        ("Return refuses an incomplete launch and consumes one authorization across observer release", SourceOnce),
        ("Return rejects busy or foreign source state before issuing a request", SourceAdmission),
        ("Return retains partial capture and unknown native request or root outcomes without retry", UnknownRequests),
        ("Return rejects a successor manager operation scene or target handle", ForeignHandoffs),
        ("Return rejects premature slot clear and never releases in-flight observers", PrematureClear),
        ("Return independently rejects recycled or inconsistent original Level handles", OriginalLevelIdentity),
        ("Return brackets the original Level unload read with fresh operation and scene observations", BracketedUnload),
        ("Return worker and reentry rejection preserve the exact single native request", ThreadAndReentry),
        ("Return observer cleanup releases independently once and retains unknown free responsibility", Release),
        ("Return authority is rechecked after native request and on each completion attempt", AuthorityRevocation),
        ("Production return authority requires exact returning registry session and keeps protection", RegistryAuthority)
    };

    private static void Completion()
    {
        using var f = new Fixture();
        var launch = f.Source.CompletedReceipt;
        Check(f.Source.Read().Closed); Equal(0, f.LaunchBackend.RootRecords);
        f.Begin(); Equal(1, f.Backend.Captures); Equal(1, f.Backend.Requests);
        Equal(701L, f.Owner!.Read().AsyncOperation); Equal(3, f.Owner.RootRecords);
        Check(!f.Owner.Tick()); Equal(0, f.Backend.SceneReads);
        f.Backend.Complete(); var receipt = f.Owner.RequireCompleted();
        Equal(launch.Session, receipt.Session); Equal(launch.Controller, receipt.Controller);
        Equal(launch.Manager, receipt.Manager); Equal(701L, receipt.AsyncOperation);
        Equal(launch.Destination, receipt.OriginLevel); Equal(launch.Origin, receipt.OriginalMenu);
        Equal(new NativeLocalRunScene(30, "LevelList", true, true), receipt.ReturnedScene);
        Check(f.Owner.Completed && !f.Owner.CleanupUnknown); Equal(1, f.Backend.SceneReads);
        f.Owner.ReleaseAfterCompleted(); Equal(1, f.Backend.Releases); Equal(0, f.Owner.RootRecords);
        Check(f.Owner.Read().Closed); Equal(1, f.Backend.Requests);
    }
    private static void WaitForUnload()
    {
        using var f = new Fixture(); f.Begin();
        f.Backend.Complete(); f.Backend.OldLevel = new(20, "Level", true, true);
        Check(!f.Owner!.Tick()); Check(!f.Owner.Completed);
        Throws<InvalidOperationException>(() => f.Owner.RequireCompleted());
        Equal(0, f.Backend.Releases); Equal(1, f.Backend.Requests);
        f.Backend.OldLevel = new(20, "Level", true, false);
        Check(f.Owner.Tick()); Check(f.Owner.Completed);
    }
    private static void SourceOnce()
    {
        using (var pending = new Fixture(completeLaunch: false))
        {
            Throws<InvalidOperationException>(() => pending.Begin());
            Equal(0, pending.Backend.Captures); Equal(0, pending.Backend.Requests);
        }
        using var f = new Fixture(); f.Begin(); f.Backend.Complete(); f.Owner!.RequireCompleted(); f.Owner.ReleaseAfterCompleted();
        var successor = new Backend();
        Throws<InvalidOperationException>(() => NativeLocalRunReturnTransition.BeginBackend(f.Main, successor, f.Authority, f.Source));
        Equal(0, successor.Captures); Equal(0, successor.Requests);
        // A pure duplicate admission does not acquire a slot or revoke the old receipt.
        Equal(f.Source.CompletedReceipt.Origin, f.Owner.Read().OriginalMenu);
    }
    private static void SourceAdmission()
    {
        foreach (var mutate in new Action<Backend>[] {
            b => b.State = b.State with { Locked = true },
            b => b.State = b.State with { AsyncOperation = 800 },
            b => b.State = b.State with { Manager = 502 },
            b => b.State = b.State with { ActiveScene = new(21, "Level", true, true) }
        })
        {
            using var f = new Fixture(); mutate(f.Backend); f.BeginUnknown();
            Equal(0, f.Backend.Requests); Check(!f.Owner!.Attempted && f.Owner.CleanupUnknown);
            Equal(1, f.Authority.Unknowns); Equal(0, f.Backend.Releases);
        }
    }
    private static void UnknownRequests()
    {
        foreach (var fault in new[] { "capture", "void-only", "throw-after", "retention" })
        {
            using var f = new Fixture(); f.Backend.Fault = fault; f.BeginUnknown();
            Check(f.Owner!.CleanupUnknown); Equal(fault == "capture" ? 0 : 1, f.Backend.Requests);
            Check(f.Owner.RootRecords > 0); Equal(0, f.Backend.Releases);
            var captures = f.Backend.Captures; var requests = f.Backend.Requests;
            Throws<InvalidOperationException>(() => f.Owner.Tick());
            Throws<InvalidOperationException>(() => f.Owner.ReleaseAfterCompleted());
            Throws<InvalidOperationException>(() => f.Begin());
            Equal(captures, f.Backend.Captures); Equal(requests, f.Backend.Requests);
        }
    }
    private static void ForeignHandoffs()
    {
        foreach (var mutate in new Action<Backend>[] {
            b => b.State = b.State with { Manager = 502 },
            b => b.State = b.State with { CurrentManager = false },
            b => b.State = b.State with { AsyncOperation = 702 },
            b => b.State = b.State with { Locked = false },
            b => b.State = b.State with { ActiveScene = new(30, "Foreign", true, true) },
            b => b.State = b.State with { ActiveScene = new(20, "LevelList", true, true) }
        })
        {
            using var f = new Fixture(); f.Begin(); mutate(f.Backend); f.TickUnknown();
            Equal(1, f.Backend.Requests); Equal(0, f.Backend.Releases);
        }
        using var changed = new Fixture(); changed.Begin();
        changed.Backend.State = changed.Backend.State with { ActiveScene = new(30, "LevelList", true, true) };
        Check(!changed.Owner!.Tick());
        changed.Backend.State = changed.Backend.State with { ActiveScene = new(31, "LevelList", true, true) };
        changed.TickUnknown();
    }
    private static void PrematureClear()
    {
        using var f = new Fixture(); f.Begin();
        Throws<InvalidOperationException>(() => f.Owner!.ReleaseAfterCompleted()); Equal(0, f.Backend.Releases);
        f.Backend.Complete(); f.Backend.Done = false; f.TickUnknown();
        Equal(0, f.Backend.SceneReads); Equal(1, f.Backend.Requests);
    }
    private static void OriginalLevelIdentity()
    {
        foreach (var old in new[] {
            new NativeLocalRunScene(21, "Level", false, false),
            new NativeLocalRunScene(20, "Other", true, false),
            new NativeLocalRunScene(20, "", false, true)
        })
        {
            using var f = new Fixture(); f.Begin(); f.Backend.Complete(); f.Backend.OldLevel = old; f.TickUnknown();
            Equal(1, f.Backend.SceneReads); Equal(0, f.Backend.Releases);
        }
    }
    private static void BracketedUnload()
    {
        using (var f = new Fixture())
        {
            f.Begin(); f.Backend.Complete();
            f.Backend.DuringSceneRead = () => f.Backend.State = f.Backend.State with { AsyncOperation = 702 };
            f.TickUnknown(); Equal(1, f.Backend.SceneReads);
        }
        using (var f = new Fixture())
        {
            f.Begin(); f.Backend.Complete(); f.Backend.DuringSceneRead = () => f.Backend.Done = false;
            f.TickUnknown(); Equal(1, f.Backend.SceneReads);
        }
    }
    private static void ThreadAndReentry()
    {
        using var f = new Fixture(); var count = NativeLocalRunReturnTransition.RetainedProcessOwnerCount;
        Task.Run(() => Throws<InvalidOperationException>(() => f.Begin())).GetAwaiter().GetResult();
        Equal(count, NativeLocalRunReturnTransition.RetainedProcessOwnerCount); Equal(0, f.Backend.Captures);
        var reentries = 0;
        f.Backend.DuringRequest = () => { Throws<InvalidOperationException>(() => f.Begin()); reentries++; };
        f.Begin(); Equal(1, reentries); Equal(1, f.Backend.Requests);
        var reads = f.Backend.Reads;
        Task.Run(() => {
            Throws<InvalidOperationException>(() => f.Owner!.Tick());
            Throws<InvalidOperationException>(() => f.Owner!.ReleaseAfterCompleted());
        }).GetAwaiter().GetResult();
        Equal(reads, f.Backend.Reads); Check(!f.Owner!.CleanupUnknown);
    }
    private static void Release()
    {
        using var f = new Fixture(); f.Begin(); f.Backend.Complete(); f.Owner!.RequireCompleted();
        var count = NativeLocalRunReturnTransition.RetainedProcessOwnerCount;
        f.Backend.Fault = "free";
        Throws<NativeLocalRunReturnTransitionException>(() => f.Owner.ReleaseAfterCompleted());
        Check(f.Owner.CleanupUnknown && !f.Owner.Read().Closed); Equal(1, f.Owner.RootRecords);
        Equal(count, NativeLocalRunReturnTransition.RetainedProcessOwnerCount);
        Throws<InvalidOperationException>(() => f.Owner.ReleaseAfterCompleted()); Equal(1, f.Backend.Releases);
    }
    private static void AuthorityRevocation()
    {
        using (var f = new Fixture())
        {
            f.Backend.DuringRequest = () => f.Authority.Revoked = true; f.BeginUnknown();
            Equal(1, f.Backend.Requests); Equal(1, f.Backend.Retentions);
        }
        using (var f = new Fixture())
        {
            f.Begin(); f.Backend.Complete(); f.Authority.Revoked = true;
            var reads = f.Backend.Reads; f.TickUnknown(); Equal(reads, f.Backend.Reads);
        }
    }
    private static void RegistryAuthority()
    {
        using var main = new MainThreadDispatcher(); var registry = new LocalRunSessionRegistry(main);
        var lease = new Lease();
        var run = registry.RegisterPrepared(new(new("return.fixture", "song"), 1, "chart"), 8_900_001, lease);
        var commit = registry.BeginCommit(run); registry.BindPendingController(commit, 8_900_101, new Lease()); registry.Commit(commit);
        var identity = registry.Read(run).Identity;
        var launch = new NativeLocalRunSceneTransitionReceipt(identity, 8_900_101, 501, 601,
            new(10, "LevelList", true, true), new(20, "Level", true, true));
        var authority = new NativeLocalRunReturnTransition.Authority(registry, run);
        Throws<InvalidOperationException>(() => authority.VerifyReturning(launch));
        registry.BeginReturn(run); authority.VerifyReturning(launch);
        Throws<InvalidOperationException>(() => authority.VerifyReturning(launch with { Controller = 8_900_102 }));
        Throws<InvalidOperationException>(() => authority.VerifyReturning(launch with { Session = identity with { RunGeneration = identity.RunGeneration + 1 } }));
        authority.VerifyReturning(launch); authority.MarkUnknown("Controlled return fault");
        Throws<InvalidOperationException>(() => authority.VerifyReturning(launch));
        Equal(LocalRunSessionPhase.Returning, registry.Read(run).Phase);
        Check(registry.IsProtected(LocalBoundaryKind.ControllerLevelOver, 8_900_101)); Equal(0, lease.Frees);
        // Explicit fixture-only verifier; the return receipt never supplies this authority.
        var proof = registry.VerifyConsumersEnded(run, new FixtureVerifier()); Check(proof is not null);
        Check(registry.Close(run, proof!)); Equal(1, lease.Frees);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Backend LaunchBackend = new(launch: true), Backend = new();
        internal readonly LaunchAuthority LaunchAuthority = new();
        internal readonly ReturnAuthority Authority = new();
        internal readonly NativeLocalRunSceneTransition Source;
        internal NativeLocalRunReturnTransition? Owner;
        internal Fixture(bool completeLaunch = true)
        {
            Source = NativeLocalRunSceneTransition.CaptureBackend(Main, LaunchBackend, LaunchAuthority);
            if (!completeLaunch) return;
            var activation = new NativeInactiveControllerActivationReceipt(LaunchAuthority.Identity, 101, 201,
                new(101, 102, 103, 104, 105, true, false, false, true, true, 0, 201, 0, 30, 10, true, true),
                new("controlled", 101, true, true, 1, 1, 0, 0, 1, 1, 1, 1, 0, Array.Empty<ControllerAwakeLedgerRow>(), Array.Empty<string>()),
                new(501, true, true, new long[] { 1, 2, 3, 4, 5, 6 }));
            Source.StartControlled(() => activation); LaunchBackend.Complete(); Source.RequireCompleted(); Source.ReleaseAfterCompleted();
        }
        internal void Begin() => Owner = NativeLocalRunReturnTransition.BeginBackend(Main, Backend, Authority, Source);
        internal void BeginUnknown()
        {
            try { Begin(); throw new Exception("Expected unknown return responsibility"); }
            catch (NativeLocalRunReturnTransitionException error) { Owner = error.Responsibility; }
            Check(Owner!.CleanupUnknown);
        }
        internal void TickUnknown()
        { Throws<NativeLocalRunReturnTransitionException>(() => Owner!.Tick()); Check(Owner!.CleanupUnknown); Equal(1, Authority.Unknowns); }
        public void Dispose()
        {
            try
            {
                if (Owner is not null && !Owner.Read().Closed && !Owner.CleanupUnknown)
                { Backend.Complete(); Owner.RequireCompleted(); Owner.ReleaseAfterCompleted(); }
                if (!Source.Read().Closed && !Source.CleanupUnknown) Source.ReleaseBeforeAttempt();
            }
            finally { Main.Dispose(); }
        }
    }
    private sealed class Backend : ILocalRunSceneTransitionBackend
    {
        private readonly bool _launch;
        internal NativeLocalRunSceneTransitionObservation State;
        internal NativeLocalRunScene OldLevel = new(20, "", false, false);
        internal int Captures, Requests, Reads, SceneReads, Retentions, Releases;
        internal bool Done; internal string? Fault; internal Action? DuringRequest, DuringSceneRead;
        internal Backend(bool launch = false)
        { _launch = launch; State = new(501, true, false, 0, launch ? new(10, "LevelList", true, true) : new(20, "Level", true, true)); }
        public bool CleanupUnknown { get; private set; }
        public int RootRecords { get; private set; }
        public int RetainedArgumentRecords => Fault == "throw-after" ? 3 : 0;
        public long RetainedOperation { get; private set; }
        public void Capture()
        { Captures++; RootRecords = 2; if (Fault == "capture") throw new IOException("Controlled partial capture"); }
        public NativeLocalRunSceneTransitionObservation ReadState() { Reads++; return State; }
        public NativeLocalRunScene ReadScene(int handle) { SceneReads++; Equal(20, handle); DuringSceneRead?.Invoke(); return OldLevel; }
        public void RequestOnce()
        {
            Requests++; DuringRequest?.Invoke(); if (Fault == "void-only") return;
            State = State with { Locked = true, AsyncOperation = _launch ? 601 : 701 };
            if (Fault == "throw-after") { CleanupUnknown = true; throw new IOException("Controlled request entered then threw"); }
        }
        public void RetainOperation(long operation)
        { Retentions++; if (Fault == "retention") throw new IOException("Controlled root failure"); RetainedOperation = operation; RootRecords++; }
        public bool OperationDone() => Done;
        internal void Complete()
        { Done = true; State = State with { Locked = false, AsyncOperation = 0, ActiveScene = _launch ? new(20, "Level", true, true) : new(30, "LevelList", true, true) }; }
        public void ReleaseRoots()
        {
            Releases++; RootRecords = 0;
            if (Fault == "free") { RootRecords = 1; CleanupUnknown = true; throw new IOException("Controlled free failure"); }
        }
    }
    private static readonly LocalRunSessionIdentity ControlledIdentity = new("controlled-return", new("return.fixture", "song"), 1, "chart", 1);
    private sealed class LaunchAuthority : ILocalRunSceneTransitionAuthority
    {
        public LocalRunSessionIdentity Identity => ControlledIdentity;
        public long Controller => 101;
        public void VerifyBeforeCapture() { }
        public void VerifyActivated(NativeInactiveControllerActivationReceipt activation)
        { if (activation.Session != Identity || activation.Controller != Controller) throw new InvalidOperationException("Wrong activation"); }
        public void VerifyPending() { }
        public void MarkUnknown(string failure) => throw new Exception("Unexpected launch fault: " + failure);
    }
    private sealed class ReturnAuthority : ILocalRunReturnTransitionAuthority
    {
        internal bool Revoked; internal int Unknowns;
        public LocalRunSessionIdentity Identity => ControlledIdentity;
        public long Controller => 101;
        public void VerifyReturning(NativeLocalRunSceneTransitionReceipt launch)
        { if (Revoked || Unknowns != 0 || launch.Session != Identity || launch.Controller != Controller) throw new InvalidOperationException("Controlled return authority revoked"); }
        public void MarkUnknown(string failure) => Unknowns++;
    }
    private sealed class Lease : IDisposable { internal int Frees; public void Dispose() => Frees++; }
    private sealed class FixtureVerifier : ILocalRunConsumersEndedVerifier
    {
        public bool Verify(LocalRunRetirementRequest request, out string evidence)
        { evidence = "Controlled fixture has no native callbacks, scenes or controller; its independent fake leases can end."; return true; }
    }
}
