using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using static Assert;

internal static class LocalAdmissionDiagnosticsChecks
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("LOCAL admission diagnostics identify registry mismatch before inspecting dependencies", Registry),
        ("LOCAL admission diagnostics identify absent result sink before bridge access", Sink),
        ("LOCAL admission diagnostics identify absent host commands before bridge access", Commands),
        ("LOCAL admission diagnostics identify unready native bridge without admitting a host", Bridge),
        ("LOCAL admission accepts the exact registry explicit dependencies and verified bridge only", Ready)
    };

    internal static int Run()
    {
        var failures = 0;
        foreach (var test in Cases)
            try { test.Test(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        Console.WriteLine($"{Cases.Length - failures}/{Cases.Length} LOCAL admission diagnostic checks passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Registry()
    {
        using var main = new MainThreadDispatcher(); var registry = new LocalRunSessionRegistry(main); var bridge = new Boundaries();
        var handler = new LocalRunTerminationHandler(main, registry, bridge, null, null);
        Reject(() => handler.ValidateHost(new LocalRunSessionRegistry(main)), "LOCAL admission requires this exact session registry.");
        Equal(0, bridge.Reads);
    }
    private static void Sink()
    {
        using var main = new MainThreadDispatcher(); var registry = new LocalRunSessionRegistry(main); var bridge = new Boundaries();
        var handler = new LocalRunTerminationHandler(main, registry, bridge, null, null);
        Reject(() => handler.ValidateHost(registry), "LOCAL admission requires an explicit result sink.");
        Equal(0, bridge.Reads);
    }
    private static void Commands()
    {
        using var main = new MainThreadDispatcher(); var registry = new LocalRunSessionRegistry(main); var bridge = new Boundaries();
        var handler = new LocalRunTerminationHandler(main, registry, bridge, new ResultSink(), null);
        Reject(() => handler.ValidateHost(registry), "LOCAL admission requires explicit host commands.");
        Equal(0, bridge.Reads);
    }
    private static void Bridge()
    {
        using var main = new MainThreadDispatcher(); var registry = new LocalRunSessionRegistry(main); var bridge = new Boundaries();
        var handler = new LocalRunTerminationHandler(main, registry, bridge, new ResultSink(), new HostCommands());
        Reject(() => handler.ValidateHost(registry), "LOCAL admission requires verified native Progress/Timeline/Judge bridge readiness for this exact registry.");
        Equal(1, bridge.Reads); Check(!handler.HasPendingWork);
    }
    private static void Ready()
    {
        using var main = new MainThreadDispatcher(); var registry = new LocalRunSessionRegistry(main); var bridge = new Boundaries { Ready = true };
        var handler = new LocalRunTerminationHandler(main, registry, bridge, new ResultSink(), new HostCommands());
        handler.ValidateHost(registry); Equal(1, bridge.Reads); Check(!handler.HasPendingWork);
    }
    private static void Reject(Action action, string message)
    {
        InvalidOperationException? observed = null;
        try { action(); } catch (InvalidOperationException error) { observed = error; }
        Equal(message, observed?.Message);
        var copied = LocalRunFailureDiagnostics.Describe("LOCAL composition responsibility retained without retry", observed!);
        Check(copied.Contains(message, StringComparison.Ordinal));
    }
    private sealed class Boundaries : ILocalRunTerminationBoundaryState
    {
        internal bool Ready; internal int Reads;
        public bool IsReadyForHost(LocalRunSessionRegistry registry) { Reads++; return Ready; }
        public bool MarkTerminal(LocalRunSessionRegistry.SessionHandle session, long controller) => throw new InvalidOperationException("Unexpected terminal operation.");
        public LocalRunTerminationBoundarySnapshot Read(LocalRunSessionRegistry.SessionHandle session, long controller) => throw new InvalidOperationException("Unexpected frame operation.");
    }
    private sealed class ResultSink : ILocalRunTerminationResultSink
    {
        public void Present(LocalRunSessionRegistry.SessionHandle session, LocalRunCapturedResult result, Action<ILocalRunTerminationResultPresentation> publish)
            => throw new InvalidOperationException("Unexpected result operation.");
    }
    private sealed class HostCommands : ILocalRunTerminationCommands
    {
        public void Execute(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long receiver)
            => throw new InvalidOperationException("Unexpected host command.");
    }
}
