using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using static Assert;

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    internal static readonly (string Name, Action Test)[] MissingJudgeAdmissionCases = {
        ("A genuinely absent native Judge entry reproduces exact live LOCAL handler admission failure with valid registry sink and commands", MissingJudgeHostAdmission)
    };

    private static void MissingJudgeHostAdmission()
    {
        using var main = new MainThreadDispatcher();
        var host = LocalRunProtectionHost.Create(main, new Logger(), new Transport());
        var backend = new Backend(main);
        var bridge = NativeLocalRunTerminationBoundaryState.CreateBackend(main, host, backend);
        var engine = new Engine();
        var broker = new ExactHookBroker(engine, main, new Logger());
        var missing = new VerifiedHookTarget("local-admission.absent-native-judge", new(0x7D1234500), new(0x222), new(0x333), ExactHookAbi.InstanceInt32SingleVoid);
        var sink = new PendingSink(); var commands = new PendingCommands();
        var handler = new LocalRunTerminationHandler(main, host.Sessions, bridge, sink, commands);
        try
        {
            Check(bridge.IsReadyForHost(host.Sessions));
            var activity = broker.ReadExactActivity(missing, requireNativeJudgeTransport: true);
            Check(!activity.Known); Equal(0, engine.Installs);
            backend.JudgeActivityOverride = () => {
                var state = broker.ReadExactActivity(missing, requireNativeJudgeTransport: true);
                return new(state.InFlight, state.Known);
            };
            Check(!bridge.IsReadyForHost(host.Sessions));
            for (var attempt = 0; attempt < 3; attempt++)
            {
                InvalidOperationException? failure = null;
                try { host.InstallHandler(handler); }
                catch (InvalidOperationException error) { failure = error; }
                Equal("LOCAL admission requires verified native Progress/Timeline/Judge bridge readiness for this exact registry.", failure?.Message);
            }
            var hostState = host.Read(); var bridgeState = bridge.Describe();
            Check(hostState.AllGatesInstalled && !hostState.AcceptingNewRuns && !hostState.CleanupUnknown);
            Equal(5, hostState.RetainedBindings); Equal(0, hostState.UnretiredSessions);
            Check(bridgeState.BindingActive && !bridgeState.CleanupUnknown); Equal(0, bridgeState.Records);
            Equal(0, backend.Roots); Equal(0, backend.FrameReads); Equal(0, sink.Presents); Equal(0, engine.Installs);
        }
        finally
        {
            // Restore only this controlled fixture's healthy activity so its empty
            // Progress/Timeline binding can retire; no production entry is created.
            backend.JudgeActivityOverride = null;
            Check(bridge.TryStop()); host.RequestStop(); Check(host.Read().Stopped);
        }
    }
}
