using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

return InitializationRootFreeChecks.Run(args);

internal static partial class InitializationRootFreeChecks
{
    // Every mode deliberately leaves an Unknown root owner. Keep its unmanaged
    // backing buffers and the complete physical callback route alive for this
    // fixture process, including modes whose fake native handle count is zero.
    private static readonly List<UnknownContext> RetainedUnknownContexts = new();
    private sealed class UnknownContext
    {
        internal readonly IntPtr Module;
        internal readonly FakeNative Api;
        internal readonly GameEventState State;
        internal readonly GameEventState.PlaybackRunAssociation Run;
        internal readonly NativePlaybackInitializationHost Host;
        internal NativePlaybackStockClockRouter? Router;
        internal NativePlaybackInitializationTracker? Tracker;
        internal MainThreadDispatcher? Main;
        internal Logger? Logger;
        internal ExactHookBroker? Broker;
        internal PlaybackSourceGroupLease? Group;
        internal List<NativePlaybackSourceObservation>? Observations;
        internal InvokeStart? Start;
        internal UnknownContext(IntPtr module, FakeNative api, GameEventState state,
            GameEventState.PlaybackRunAssociation run, NativePlaybackInitializationHost host)
            => (Module, Api, State, Run, Host) = (module, api, state, run, host);
    }
    private static int _checks;
    private static void Check(bool value, string reason)
    { ++_checks; if (!value) throw new InvalidOperationException(reason); }
    private static string Hash(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static T Export<T>(IntPtr module, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    private static T Copy<T>(T owner) where T : class
        => (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null)!;
    private static void Unknown(Action action)
    {
        ++_checks;
        try { action(); }
        catch (IOException error) when (error.Message == "Synthetic unknown GC-root free outcome.") { return; }
        throw new InvalidOperationException("An unknown native free was reported as completed cleanup.");
    }
    private static void ResourceUnresolved(Action action)
    {
        ++_checks;
        try { action(); }
        catch (UnsupportedSignatureException error) when (error.Message ==
            "UnsupportedSignature: The resource root release previously failed and remains unresolved: Synthetic unknown GC-root free outcome.") { return; }
        throw new InvalidOperationException("An unresolved resource root did not refuse cleanup with its exact retained failure.");
    }
    private static void Rejected(Action action, ErrorCode code)
    {
        ++_checks;
        try { action(); }
        catch (GameplayStateException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("A copied or foreign-thread owner changed root responsibility.");
    }
    private sealed class Logger : ILogger
    { public void Log(LogLevel level, string message, Exception? error = null) { } }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InvokeStart(IntPtr target, IntPtr enumerator, IntPtr method, out uint raw, out uint exception);

    internal static int Run(string[] arguments)
    {
        var mode = arguments.Length == 5 ? arguments[0] : "invalid";
        try
        {
            Check(arguments.Length == 5 && mode is "host-unclaimed-free-fault" or "host-router-free-fault"
                or "tracker-progress-free-fault" or "tracker-enumerator-free-fault" or "tracker-resource-free-fault", "Unknown initialization root-free mode.");
            Check(Hash(arguments[1]) == PlaybackSourceGroupTargets.ProgressResourceFixtureSha256, "Only the exact frozen Progress resource fixture may execute.");
            var module = NativeLibrary.Load(Path.GetFullPath(arguments[1]));
            var api = new FakeNative();
            var enumerator = api.ConfigureInitializer(module); api.ConfigureSharedConstructor(module);
            api.ConfigureDirectAudio(module); api.ConfigureProgressResource(module);
            var state = new GameEventState(); state.Scene("Level"); state.SetAvailability(scenes: true, runs: true, pause: true);
            state.Controller(api.GameplayInformation.ToInt64(), true, false, "initialization-free-unknown", api.GameplayController.ToInt64());
            state.ObservePause(true);
            var run = state.CapturePlaybackRun(api.GameplayInformation.ToInt64(), api.GameplayController.ToInt64());
            var host = NativePlaybackInitializationHost.Capture(api.GameplayApi, run);
            var context = new UnknownContext(module, api, state, run, host);
            RetainedUnknownContexts.Add(context);
            Check(host.RootsRetained && api.ActiveRoots == 4, "The host did not retain four independent roots.");
            if (mode.StartsWith("host-", StringComparison.Ordinal)) Host(mode, api, run, host, context);
            else Tracker(arguments, mode, module, enumerator, api, run, host, context);
            Check(RetainedUnknownContexts.Contains(context) && ReferenceEquals(context.Api, api)
                && ReferenceEquals(context.Host, host) && (host.RootsRetained || context.Tracker is { RootsRetained: true }),
                "The complete Unknown owner context was discarded before this fixture process ended.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, retainedUnknown = true,
                nativeFreeReplayed = false, api.ActiveRoots, hostRootsRetained = host.RootsRetained, cleanupCompleted = false,
                resourceUnknownRetained = mode == "tracker-resource-free-fault", completeUnknownContextRetained = true,
                backingBuffersDisposed = false, gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
    }

    private static void Host(string mode, FakeNative api, GameEventState.PlaybackRunAssociation run,
        NativePlaybackInitializationHost host, UnknownContext context)
    {
        var before = api.FreeHandleAttempts;
        Task.Run(() => Rejected(host.Dispose, ErrorCode.WrongThread)).GetAwaiter().GetResult();
        Rejected(Copy(host).Dispose, ErrorCode.TargetExpired);
        Check(api.FreeHandleAttempts == before && api.ActiveRoots == 4 && host.RootsRetained,
            "Refused cleanup changed host root ownership.");
        api.FailFreeHandle = api.LatestInitializationRootFor(api.GameplayPause);
        NativePlaybackStockClockRouter? router = null;
        Action cleanup = host.Dispose;
        if (mode == "host-router-free-fault")
        {
            router = new(api.GameplayApi, host, run);
            context.Router = router;
            cleanup = router.Dispose;
        }
        before = api.FreeHandleAttempts; // Router claim verifies the graph using its own temporary return roots.
        Unknown(cleanup);
        Check(host.RootsRetained && api.ActiveRoots == 0 && api.FreeHandleAttempts == before + 4,
            "Unknown host free lost its ownership record or blocked independent successful frees.");
        if (router is not null) Check(!router.RootsReleased, "The Router reported unknown host roots as released.");
        var attempts = api.FreeHandleAttempts;
        api.FailFreeHandle = 0;
        for (var repeat = 0; repeat < 3; ++repeat)
        {
            if (router is null) Unknown(host.Dispose); else Unknown(router.PumpCompleted);
            Check(host.RootsRetained && api.FreeHandleAttempts == attempts && api.ActiveRoots == 0,
                "A later host no-op Dispose replayed free or promoted unknown cleanup to success.");
            if (router is not null) Check(!router.RootsReleased, "A repeated Router drain forgot its unknown host roots.");
        }
    }

    private static void Tracker(string[] arguments, string mode, IntPtr module, IntPtr enumerator, FakeNative api,
        GameEventState.PlaybackRunAssociation run, NativePlaybackInitializationHost host, UnknownContext context)
    {
        var targets = PlaybackSourceGroupTargets.ResolveOwnedProgressResourceFixture(module);
        var main = new MainThreadDispatcher(); context.Main = main;
        var logger = new Logger(); context.Logger = logger;
        var broker = new ExactHookBroker(Path.GetFullPath(arguments[4]), main, logger); context.Broker = broker;
        var router = new NativePlaybackStockClockRouter(api.GameplayApi, host, run);
        context.Router = router;
        NativePlaybackInitializationTracker? tracker = null;
        var observations = new List<NativePlaybackSourceObservation>();
        context.Observations = observations;
        var group = broker.BindPlaybackSourceGroup(targets, Path.GetFullPath(arguments[2]), Hash(arguments[2]),
            Path.GetFullPath(arguments[3]), Hash(arguments[3]),
            () => throw new InvalidOperationException("Stopped initialization created a Clock reader."), router.OnGetTimeFault, router.OnSourceFault,
            observation => { observations.Add(observation); if (!tracker!.ObserveBefore(observation)) observation.Dispose(); },
            completion => tracker!.ObserveReturned(completion));
        context.Group = group;
        router.Attach(group);
        Check(group.HasProgressResource && host.RootsRetained && !router.RootsReleased && api.ActiveRoots == 4,
            "The actual Router did not bind the four-root host Progress watch and current resource initialization gate.");
        tracker = new(api.GameplayApi, group, run, host);
        context.Tracker = tracker;
        var start = Export<InvokeStart>(module, "StockFixtureInvokeStart");
        context.Start = start;
        Check(start(targets.ProgressStartMoveNext.Address, enumerator, targets.ProgressStartMoveNext.MethodInfo,
            out var raw, out var code) == 1 && raw == 1 && code == 0, "The actual native initial-yield parent did not exit normally.");
        Check(api.ActiveRoots == 7 && tracker.RootsRetained && tracker.LastFailure is null && observations.Count == 1
            && api.HasRootFor(api.ProgressResourceA) && group.ReadActivity() is { Known: true, InFlight: 0 },
            "The actual yield did not leave three independent Progress, enumerator and resource tracker roots at physical drain.");
        var yieldObservation = observations.Single();
        var initialization = group.ReadInitialization();
        Check(yieldObservation.Role == NativePlaybackSourceRole.ProgressStartMoveNext && yieldObservation.Self == enumerator
            && yieldObservation.IncomingMethod == targets.ProgressStartMoveNext.MethodInfo
            && yieldObservation.ParentSequence == 0 && yieldObservation.Depth == 1
            && yieldObservation.TryReadCompletion(out var yieldCompletion) && yieldCompletion is
                { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false, HasRawReturnByte: true, RawReturnByte: 1 }
            && initialization.Blocked == 1 && initialization.Enumerator == unchecked((ulong)enumerator.ToInt64())
            && initialization.Progress == unchecked((ulong)api.GameplayProgress.ToInt64())
            && group.MatchesInitializationCall(yieldObservation, initialization.YieldSequence, initialization.YieldToken),
            "The retained roots have no exact actual Start Before and completed physical-yield receipt.");
        var resource = (NativePlaybackStockSourceHub.ProgressResourceTicket)typeof(NativePlaybackInitializationTracker)
            .GetField("_resource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tracker)!;
        var ticket = resource.Read();
        Check(resource.RootsRetained && ticket.Phase == 2 && ticket.Current == 1
            && ticket.Reference == unchecked((ulong)api.ProgressResourceA.ToInt64())
            && ticket.Progress == unchecked((ulong)api.GameplayProgress.ToInt64())
            && ticket.Initializer != 0 && ticket.InitializerOrdinal == initialization.Ordinal,
            "The third tracker root has no current actual resource ticket bound to this exact initializer and reference.");
        Check(broker.ReadActualPlaybackDobbyCallCounts() == (20, 20), "The resource test did not install its exact twenty physical members.");
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "root-free-resource-yield-proof", mode, api.ActiveRoots,
            observationCount = observations.Count, physicalYieldVerified = true, rawReturnByte = 1,
            resourcePhase = ticket.Phase, resourceCurrent = ticket.Current, resourceReference = ticket.Reference,
            initializerOrdinal = initialization.Ordinal, resourceInitializerOrdinal = ticket.InitializerOrdinal,
            actualPrepared = 20, actualCommitted = 20, cleanupCompleted = false }));
        var before = api.FreeHandleAttempts;
        Task.Run(() => Rejected(tracker.Dispose, ErrorCode.WrongThread)).GetAwaiter().GetResult();
        Rejected(Copy(tracker).Dispose, ErrorCode.TargetExpired);
        Check(api.FreeHandleAttempts == before && api.ActiveRoots == 7, "Refused tracker cleanup changed native root ownership.");
        api.FailFreeHandle = api.LatestInitializationRootFor(mode == "tracker-progress-free-fault" ? api.GameplayProgress
            : mode == "tracker-resource-free-fault" ? api.ProgressResourceA : enumerator);
        Unknown(tracker.Dispose);
        Check(tracker.RootsRetained && tracker.LastFailure is not null && api.ActiveRoots == 4
            && api.FreeHandleAttempts == before + 3 && observations.All(value => value.IsReleased),
            "Unknown tracker free lost its record, skipped an independent other root, or outlived its real observation.");
        var attempts = api.FreeHandleAttempts; api.FailFreeHandle = 0;
        for (var repeat = 0; repeat < 3; ++repeat)
        {
            Unknown(tracker.Dispose);
            if (mode == "tracker-resource-free-fault") ResourceUnresolved(group.PumpCompleted);
            else group.PumpCompleted();
            Check(tracker.RootsRetained && api.FreeHandleAttempts == attempts && api.ActiveRoots == 4,
                "A later tracker no-op Dispose replayed free or promoted unknown cleanup to success.");
        }
        if (mode == "tracker-resource-free-fault")
        {
            ResourceUnresolved(router.Dispose);
            Check(!router.RootsReleased && host.RootsRetained && tracker.RootsRetained && api.ActiveRoots == 4
                && api.FreeHandleAttempts == attempts,
                "Unresolved resource cleanup released host roots, forgot tracker responsibility or replayed native free.");
            for (var repeat = 0; repeat < 3; ++repeat)
            {
                ResourceUnresolved(router.PumpCompleted);
                Check(!router.RootsReleased && host.RootsRetained && tracker.RootsRetained && api.ActiveRoots == 4
                    && api.FreeHandleAttempts == attempts,
                    "A later Router pump promoted unresolved resource cleanup to completion or replayed a native free.");
            }
        }
        else
        {
            router.Dispose(); router.PumpCompleted();
            Check(router.RootsReleased && !host.RootsRetained && tracker.RootsRetained && api.ActiveRoots == 0,
                "Successful host cleanup inferred success for its independently retained tracker.");
        }
        Check(group.ReadInitialization() is { Blocked: 1 } && group.GetTimeLease.ReadCurrent().Authority is null
            && broker.ReadActualPlaybackDobbyCallCounts() == (20, 20), "Unknown tracker cleanup changed native fresh hold, policy or physical installation.");
    }
}
