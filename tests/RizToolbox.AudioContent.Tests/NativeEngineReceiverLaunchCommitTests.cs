using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using static Assert;

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    internal static readonly (string Name, Action Test)[] ReceiverLifetimeLaunchCases = {
        ("Receiver readiness holds the actual launch pump before activation when either receiver is missing", ReceiverBeforeCommit),
        ("Receiver loss during actual activation verification blocks getters and shared Ready", ReceiverDuringActivation),
        ("Receiver loss during actual getter verification blocks initialized graph publication", ReceiverDuringGetters),
        ("Receiver loss during actual graph verification blocks the shared Ready publication", ReceiverDuringGraph),
        ("Independently confirmed receiver death never reopens launch Ready or releases pending protection", DeadReceiverCannotCommit)
    };
    private static PendingFixture ReceiverCommitFixture()
    {
        var f = new PendingFixture(); f.AttachController(); f.Launch.NoteActivationAttempt();
        f.Bridge.AttachInitializingGraph(f.Launch, f.Information); f.Backend.PendingControllerInitialized = true;
        return f;
    }
    private static void ReceiverHeld(PendingFixture f)
    {
        Check(!f.Launch.IsCommittedReady && !f.Launch.CoroutineCancellationPending && !f.Launch.Read().EarlyTerminal);
        Equal(LocalRunSessionPhase.PendingCommit, f.Host.Sessions.Read(f.Session).Phase);
        Check(f.Host.Sessions.IsProtected(LocalBoundaryKind.ControllerLevelOver, f.Controller));
        Check(!f.Host.Sessions.Read(f.Session).CleanupAttempted);
    }
    private static void ReceiverBeforeCommit()
    {
        foreach (var role in new[] { 0, 1 })
        {
            using var f = ReceiverCommitFixture(); var activation = 0; var getter = 0;
            var readiness = new[] { true, true }; readiness[role] = false;
            Check(!NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch,
                () => ++activation, () => { ++getter; return true; }, verifyReceiversReady: () => readiness.All(value => value)));
            Equal(0, activation); Equal(0, getter); Equal(0, f.Backend.Verifications); ReceiverHeld(f);
        }
    }
    private static void ReceiverDuringActivation()
    {
        using var f = ReceiverCommitFixture(); var ready = true; var getters = 0;
        Check(!NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch,
            () => ready = false, () => { ++getters; return true; }, verifyReceiversReady: () => ready));
        Equal(0, getters); Equal(0, f.Backend.Verifications); ReceiverHeld(f);
    }
    private static void ReceiverDuringGetters()
    {
        using var f = ReceiverCommitFixture(); var ready = true;
        Check(!NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch,
            () => { }, () => { ready = false; return true; }, verifyReceiversReady: () => ready));
        Equal(0, f.Backend.Verifications); ReceiverHeld(f);
    }
    private static void ReceiverDuringGraph()
    {
        using var f = ReceiverCommitFixture(); var ready = true; var activity = 0; var getterReturned = false;
        f.Backend.JudgeActivityOverride = () => { ++activity; if (getterReturned) ready = false; return new(0, true); };
        Check(!NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch,
            () => { }, () => { getterReturned = true; return true; }, verifyReceiversReady: () => ready));
        Check(activity > 0 && getterReturned); ReceiverHeld(f);
    }
    private static void DeadReceiverCannotCommit()
    {
        foreach (var role in new[] { 0, 1 })
        {
            using var f = ReceiverCommitFixture();
            var pairs = new[] { new NativeUnityCoroutineOwner(f.Controller, 0xA000, new(0xC000)),
                new NativeUnityCoroutineOwner(f.Controller + 10, 0xB000, new(0xD000)) };
            var destruction = new ActiveDestructionIdentity(f.Controller, f.Controller + 20, f.Controller + 21, f.Controller + 22, f.Controller + 23);
            var source = new NativeCutoutEventObservation(f.Controller, pairs[1].ManagedReceiver, f.Controller + 11,
                f.Controller + 12, f.Controller + 13, 1, true, Array.Empty<NativeCutoutTrigger>());
            var receivers = new NativeEngineCoroutineReceiverLifetime(f.Main, f.Launch.Identity, pairs, destruction, source);
            receivers.ObserveReceiver((NativeEngineCoroutineReceiverRole)role, new(pairs[role].ManagedReceiver, 0, pairs[role].ManagedClass));
            if (role == 0)
                receivers.ObserveControllerDeath(true, true, new(f.Launch.Identity, NativeLocalActiveDestructionPhase.CloneDestroyed,
                    destruction, 10, 11, -1, -1, false, true, false, false, false, 5, null));
            else
                receivers.ObserveEventTriggerDeath(true, new(f.Launch.Identity, source with { Alive = false }, false, false,
                    true, false, 0, 0, 0, 7, null));
            for (var pass = 0; pass < 2; ++pass)
            {
                Check(!NativeLocalRunLaunchCoordinator.TryCommitInitialized(f.Main, f.Host, f.Handler, f.Bridge, f.Launch,
                    () => throw new InvalidOperationException("Dead receiver must hold before activation verification."), () => true,
                    verifyReceiversReady: () => receivers.Read().MayCommit));
                ReceiverHeld(f); Check(!receivers.Unknown && receivers.Read().Pending);
            }
        }
    }
}
