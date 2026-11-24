using RizToolbox.Adapter.AudioContent;
using static Assert;

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    internal static readonly (string Name, Action Test)[] LaunchCommitPumpCases = {
        ("Production launch pump waits for real graph adoption and actual native drain before shared commit", LaunchPumpDrain),
        ("Production launch pump rejects a different bridge and worker before activation observation", LaunchPumpAuthority),
        ("Production launch pump rejects an early terminal arriving during activation verification", LaunchPumpTerminalRace),
        ("Production launch pump preserves pending protection when activation verification throws", LaunchPumpActivationFailure),
        ("Production launch pump never commits after the retained host has requested stop", LaunchPumpStop),
        ("Production launch pump waits for both actual getter enumerators and their complete native exits", LaunchPumpCoroutines),
        ("Production launch pump rechecks pending authority after coroutine completion verification", LaunchPumpCoroutineRace)
    };

    private static bool Pump(PendingFixture f, Action verify)
        => NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch, verify, () => true);

    private static void LaunchPumpDrain()
    {
        using var f = new PendingFixture(); f.AttachController(); f.Launch.NoteActivationAttempt();
        var observations = 0;
        Check(!Pump(f, () => observations++)); Equal(1, observations);
        Equal(LocalRunSessionPhase.PendingCommit, f.Host.Sessions.Read(f.Session).Phase);
        f.Bridge.AttachInitializingGraph(f.Launch, f.Information); f.Backend.PendingControllerInitialized = true;
        f.Transport.InFlight = 1;
        Check(!Pump(f, () => observations++)); Equal(0, f.Backend.Verifications);
        Equal(LocalRunSessionPhase.PendingCommit, f.Host.Sessions.Read(f.Session).Phase);
        f.Transport.InFlight = 0;
        Check(Pump(f, () => observations++)); Equal(3, observations);
        Check(f.Launch.IsCommittedReady); Equal(LocalRunSessionPhase.Committed, f.Host.Sessions.Read(f.Session).Phase);
        Equal(1, f.Backend.Verifications); Equal(0, f.ResultBackend.Invocations);
        Throws<InvalidOperationException>(() => Pump(f, () => observations++)); Equal(3, observations);
    }

    private static void LaunchPumpAuthority()
    {
        using var f = new PendingFixture(); using var other = new PendingFixture();
        f.AttachController(); f.Launch.NoteActivationAttempt(); var calls = 0;
        Throws<InvalidOperationException>(() => NativeLocalRunLaunchCoordinator.TryCommitInitialized(
            f.Main, f.Host, f.Handler, other.Bridge, f.Launch, () => calls++, () => true));
        Throws<InvalidOperationException>(() => NativeLocalRunLaunchCoordinator.TryCommitInitialized(
            f.Main, other.Host, f.Handler, f.Bridge, f.Launch, () => calls++, () => true));
        Task.Run(() => Throws<InvalidOperationException>(() => Pump(f, () => Interlocked.Increment(ref calls)))).GetAwaiter().GetResult();
        Equal(0, calls); Equal(LocalRunSessionPhase.PendingCommit, f.Host.Sessions.Read(f.Session).Phase);
        Equal(0, f.Backend.Verifications);
    }

    private static void LaunchPumpTerminalRace()
    {
        using var f = new PendingFixture(); f.AttachController(); f.Launch.NoteActivationAttempt();
        f.Bridge.AttachInitializingGraph(f.Launch, f.Information); f.Backend.PendingControllerInitialized = true;
        Throws<InvalidOperationException>(() => Pump(f, () => f.Launch.ObserveTerminalClaimFromAnyThread(f.Controller)));
        Check(f.Launch.Read().EarlyTerminal); Check(!f.Launch.IsCommittedReady);
        Equal(LocalRunSessionPhase.PendingCommit, f.Host.Sessions.Read(f.Session).Phase);
        Equal(0, f.Backend.Verifications); Equal(0, f.ResultBackend.Invocations);
    }

    private static void LaunchPumpActivationFailure()
    {
        using var f = new PendingFixture(); f.AttachController(); f.Launch.NoteActivationAttempt();
        f.Bridge.AttachInitializingGraph(f.Launch, f.Information); f.Backend.PendingControllerInitialized = true;
        var failure = new InvalidOperationException("Actual activation identity changed.");
        try { Pump(f, () => throw failure); throw new Exception("Expected activation rejection."); }
        catch (InvalidOperationException error) { Check(ReferenceEquals(failure, error)); }
        Equal(LocalRunSessionPhase.PendingCommit, f.Host.Sessions.Read(f.Session).Phase);
        Check(!f.Launch.IsCommittedReady); Equal(0, f.Backend.Verifications);
        Check(f.Host.Sessions.IsProtected(LocalBoundaryKind.ControllerLevelOver, f.Controller));
    }

    private static void LaunchPumpStop()
    {
        using var f = new PendingFixture(); f.AttachController(); f.Launch.NoteActivationAttempt();
        f.Bridge.AttachInitializingGraph(f.Launch, f.Information); f.Backend.PendingControllerInitialized = true;
        Check(!f.Host.RequestStop()); var calls = 0;
        Throws<InvalidOperationException>(() => Pump(f, () => calls++)); Equal(0, calls);
        Equal(LocalRunSessionPhase.PendingCommit, f.Host.Sessions.Read(f.Session).Phase);
        Check(f.Host.Sessions.IsProtected(LocalBoundaryKind.ControllerLevelOver, f.Controller));
    }
    private static void LaunchPumpCoroutines()
    {
        using var f = new PendingFixture(); using var coroutines = new NativeLocalInitializationCoroutineRegression.Fixture();
        f.AttachController(); f.Launch.NoteActivationAttempt(); f.Bridge.AttachInitializingGraph(f.Launch, f.Information); f.Backend.PendingControllerInitialized = true;
        Check(!NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch, () => { }, coroutines.Owner.TryConfirmEnded));
        Equal(0, f.Backend.Verifications); Check(!f.Launch.IsCommittedReady);
        coroutines.Finish(); coroutines.Backend.InFlight = 1;
        Check(!NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch, () => { }, coroutines.Owner.TryConfirmEnded));
        Equal(0, f.Backend.Verifications); coroutines.Backend.InFlight = 0;
        Check(NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch, () => { }, coroutines.Owner.TryConfirmEnded));
        Equal(1, f.Backend.Verifications); Check(f.Launch.IsCommittedReady);
        Check(!coroutines.Owner.IsFor(f.Launch)); // Controlled completion can exercise the seam, never authorize a production menu capture.
    }
    private static void LaunchPumpCoroutineRace()
    {
        using var f = new PendingFixture(); f.AttachController(); f.Launch.NoteActivationAttempt();
        f.Bridge.AttachInitializingGraph(f.Launch, f.Information); f.Backend.PendingControllerInitialized = true;
        const string cause = "Controlled observation fault during coroutine verification.";
        Throws<InvalidOperationException>(() => NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch,
            () => { }, () => { f.Launch.ObserveNativeFaultFromAnyThread(cause); return true; }));
        Equal(cause, f.Launch.Read().Failure);
        Check(!f.Launch.IsCommittedReady); Equal(0, f.Backend.Verifications); Equal(LocalRunSessionPhase.PendingCommit, f.Host.Sessions.Read(f.Session).Phase);
    }
}
