using RizToolbox.Adapter.AudioContent;
using static Assert;

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    internal static readonly (string Name, Action Test)[] CancellationLaunchCases = {
        ("Cancellation hold stops the actual launch pump before its first activation verification", CancellationBeforePump),
        ("Cancellation during actual activation verification blocks getter and graph readiness", CancellationDuringActivation),
        ("Cancellation during getter completion verification blocks initialized graph publication", CancellationDuringGetters),
        ("Cancellation during actual graph activity verification cannot commit the run", CancellationDuringGraph),
        ("Cancellation between prepared and committed readiness retains the pending registry", CancellationBeforeCommit),
        ("Cancellation after readiness revokes usable ready without forging terminal or cleanup", CancellationAfterReady)
    };
    private static PendingFixture CancellationFixture()
    {
        var f = new PendingFixture(); f.AttachController(); f.Launch.NoteActivationAttempt();
        f.Bridge.AttachInitializingGraph(f.Launch, f.Information); f.Backend.PendingControllerInitialized = true;
        return f;
    }
    private static void CancellationBeforePump()
    {
        using var f = CancellationFixture(); var activation = 0; var getter = 0;
        f.Launch.ObserveCoroutineCancellationRequestFromAnyThread();
        Check(!NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch,
            () => ++activation, () => { ++getter; return true; }));
        Equal(0, activation); Equal(0, getter); Equal(0, f.Backend.Verifications); CheckCancellationHeld(f);
    }
    private static void CancellationDuringActivation()
    {
        using var f = CancellationFixture(); var getter = 0;
        Check(!NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch,
            f.Launch.ObserveCoroutineCancellationRequestFromAnyThread, () => { ++getter; return true; }));
        Equal(0, getter); Equal(0, f.Backend.Verifications); CheckCancellationHeld(f);
    }
    private static void CancellationDuringGetters()
    {
        using var f = CancellationFixture();
        Check(!NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch,
            () => { }, () => { f.Launch.ObserveCoroutineCancellationRequestFromAnyThread(); return true; }));
        Equal(0, f.Backend.Verifications); CheckCancellationHeld(f);
    }
    private static void CancellationDuringGraph()
    {
        using var f = CancellationFixture(); var activity = 0;
        f.Backend.JudgeActivityOverride = () => {
            ++activity; f.Launch.ObserveCoroutineCancellationRequestFromAnyThread(); return new(0, true);
        };
        Check(!NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch, () => { }, () => true));
        Check(activity > 0); CheckCancellationHeld(f);
    }
    private static void CancellationBeforeCommit()
    {
        using var f = CancellationFixture(); Check(f.Bridge.VerifyInitializedGraph(f.Launch));
        f.Launch.PrepareCommit(); f.Launch.ObserveCoroutineCancellationRequestFromAnyThread();
        Throws<InvalidOperationException>(f.Launch.CommitPrepared); CheckCancellationHeld(f);
        Throws<InvalidOperationException>(f.Launch.PrepareCommit);
    }
    private static void CancellationAfterReady()
    {
        using var f = CancellationFixture();
        Check(NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch, () => { }, () => true));
        Check(f.Launch.IsCommittedReady);
        Task.Run(f.Launch.ObserveCoroutineCancellationRequestFromAnyThread).GetAwaiter().GetResult();
        Check(!f.Launch.IsCommittedReady && f.Launch.Read().CoroutineCancellationPending);
        Equal(LocalRunLaunchCommitPhase.CommittedReady, f.Launch.Read().Phase);
        Equal(LocalRunSessionPhase.Committed, f.Host.Sessions.Read(f.Session).Phase);
        Check(!f.Launch.Read().EarlyTerminal && !f.Host.Sessions.Read(f.Session).CleanupAttempted);
        Check(f.Host.Sessions.IsProtected(LocalBoundaryKind.ControllerLevelOver, f.Controller));
    }
    private static void CheckCancellationHeld(PendingFixture f)
    {
        Check(f.Launch.CoroutineCancellationPending && f.Launch.Read().CoroutineCancellationPending && !f.Launch.IsCommittedReady);
        Check(!f.Launch.Read().EarlyTerminal && f.Launch.Read().Phase != LocalRunLaunchCommitPhase.Unknown);
        Equal(LocalRunSessionPhase.PendingCommit, f.Host.Sessions.Read(f.Session).Phase);
        Check(!f.Host.Sessions.Read(f.Session).CleanupAttempted && !f.Launch.CanCancelPureReservation);
        Check(f.Host.Sessions.IsProtected(LocalBoundaryKind.ControllerLevelOver, f.Controller));
    }
}
