using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Actual sealed native-void broker/probe delivery over a controlled bridge and graph snapshots.
/// Reference-return frames below are a controlled seam, not native game acceptance.</summary>
internal static partial class NativeLocalUnityEventLedgerRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("UnityEvent actual broker Invoke returns without Prepare and remains explicitly pending", InlineInvoke),
        ("UnityEvent independent Prepare captures its real return and caller outside any parent Invoke", IndependentPrepare),
        ("UnityEvent foreign physical child cannot inherit parent execution proof or Invoke identity", ForeignChild),
        ("UnityEvent worker and metadata probe faults perform no graph or root reads", WorkerAndMetadata),
        ("UnityEvent unknown guard preserves the first ledger failure and distinguishes source from cleanup", UnknownGuardDiagnostics),
        ("UnityEvent same-event reentry and original exceptions retain physical responsibility", ReentryAndException),
        ("UnityEvent source faults inside graph and result adoption stop publication and subsequent reads", SourceFaultReads),
        ("UnityEvent dirty rebuilding and list replacement produce distinct pre and post snapshots without closing mutation", DirtyAndMutation),
        ("UnityEvent independent return requires the actual same calls executing list and exact paired context", InvalidPrepare),
        ("UnityEvent shared calls ambiguity and independent Prepare reentry retain responsibility", AmbiguousPrepare),
        ("UnityEvent snapshots freeze rows leaves candidates and multicast container identities", ImmutableSnapshots),
        ("UnityEvent graph budgets cover events rows leaves and retained roots without truncation", Bounds),
        ("UnityEvent unknown activity and source pending prevent false idle or additional native reads", IdleAndActivity),
        ("UnityEvent installation faults retain adopted roots and controlled owners never claim production IsFor", CaptureFaults),
        ("UnityEvent native Invoke records its actual supplied frame and refuses a changed exit caller", NativeInvokeFrames),
        ("UnityEvent native Invoke checks shared transport authority before reading its receiver graph", NativeTransportAuthority),
        ("UnityEvent independent Prepare refuses unknown transport before adopting its result or publishing a graph", PrepareTransportAuthority),
        ("UnityEvent inline list uses its own current physical parent token and remains pending", InlineList),
        ("UnityEvent inline empty and rebuilt lists stay distinct from pre post and independent Prepare", InlineEmptyAndMutation),
        ("UnityEvent inline source ignores stale parent foreign child worker and unrelated sink identities", InlineIsolation),
        ("UnityEvent inline list rejects wrong return frame duplicate snapshots and replaced executing output", InlineRefusals),
        ("UnityEvent inline adoption faults preserve responsibility and never publish a completed dispatch", InlineAdoptionFaults),
        ("UnityEvent inline parent capability cannot be replayed into a reused Invoke context generation", InlineStaleCapability)
    };

    private static void InlineInvoke()
    {
        using var f = new Fixture(); Check(f.Owner.TryConfirmNativeIdle());
        var initial = f.Owner.Read(); Check(initial.Pending && !initial.MutationClosed && initial.InlineExecutionUnobserved && !initial.DispatchPathVerified);
        f.Backend.DuringInvoke = _ => { Check(!f.Owner.TryConfirmNativeIdle()); Equal(1L, f.Owner.Read().NativeInFlight); };
        f.Backend.Call(101); var state = f.Owner.Read();
        Equal(1, f.Backend.Originals); Equal(1L, state.Invocations); Equal(1L, state.ReturnedInvocations);
        Equal(0L, state.PreparedInvocations); Equal(0L, state.NativeInFlight); Check(!state.Unknown);
        var row = state.EventSnapshots.Single(); Equal(1L, row.Entered); Equal(1L, row.Exited); Equal(1L, row.Generation);
        Check(row.Returned && row.Pre is not null && row.Post is not null && row.IndependentPrepared is null);
        Check(!row.ChangedSinceCapture && state.Pending && state.InlineExecutionUnobserved && !state.DispatchPathVerified);
        f.Backend.DuringInvoke = null; f.Backend.Call(101);
        Equal(2L, f.Owner.Read().EventSnapshots.Single().Generation); Equal(2, f.Backend.Originals); Check(f.Owner.TryConfirmNativeIdle());
    }

    private static void IndependentPrepare()
    {
        using var f = new Fixture(); var graph = f.Backend.Graphs[0]; const long caller = 0x245679AB;
        f.Backend.Prepare(graph.Calls, graph.ExecutingCalls.Object, caller);
        f.NoPrepareError(); var state = f.Owner.Read(); var row = state.EventSnapshots.Single();
        Equal(0L, state.Invocations); Equal(0L, state.ReturnedInvocations); Equal(1L, state.PreparedInvocations);
        Equal(1L, row.PrepareGeneration); Equal(caller, row.PrepareCaller); Check(row.IndependentPrepared is not null);
        Equal(graph.ExecutingCalls.Object, row.IndependentPrepared!.Returned.Object); Equal(1, f.Backend.ResultAdoptions);
        Check(row.Pre is null && row.Post is null && !row.Returned && state.Pending && state.InlineExecutionUnobserved);
        f.Backend.Call(101); row = f.Owner.Read().EventSnapshots.Single();
        Check(row.Returned && row.IndependentPrepared is not null); Equal(1L, row.PrepareGeneration); Equal(1L, row.Generation);
        Check(f.Owner.Read().InlineExecutionUnobserved && !f.Owner.Read().DispatchPathVerified);
    }

    private static void ForeignChild()
    {
        using var f = new Fixture(); var callbacks = 0;
        f.Backend.DuringInvoke = receiver => {
            if (receiver == 101)
            {
                f.Backend.Call(999); callbacks++;
                Check(!f.Owner.TryConfirmNativeIdle());
            }
            else
            {
                // A foreign physical child may call the same rooted calls object.
                // It is only an independent observation, never the parent's execution.
                var graph = f.Backend.Graphs[0]; f.Backend.Prepare(graph.Calls, graph.ExecutingCalls.Object, 0x12345678);
                callbacks++;
            }
        };
        f.Backend.Call(101); f.NoPrepareError(); var state = f.Owner.Read();
        Equal(2, callbacks); Equal(2, f.Backend.Originals); Equal(1L, state.Invocations); Equal(1L, state.ReturnedInvocations);
        Equal(1L, state.PreparedInvocations); Check(!state.Unknown && state.InlineExecutionUnobserved && !state.DispatchPathVerified);
        Equal(0L, state.NativeInFlight); Check(f.Owner.TryConfirmNativeIdle());
        var reads = f.Backend.GraphReads; f.Backend.DuringInvoke = null; f.Backend.Call(998);
        Equal(reads, f.Backend.GraphReads); Equal(1L, f.Owner.Read().Invocations);
    }

    private static void WorkerAndMetadata()
    {
        using (var f = new Fixture())
        {
            var reads = f.Backend.GraphReads; var adopts = f.Backend.ResultAdoptions;
            Task.Run(() => f.Backend.Call(999)).GetAwaiter().GetResult(); Check(!f.Owner.Read().Unknown);
            Task.Run(() => f.Backend.Call(101)).GetAwaiter().GetResult();
            Check(f.Owner.Read().Unknown); Equal(reads, f.Backend.GraphReads); Equal(adopts, f.Backend.ResultAdoptions);
            Equal(2, f.Backend.Originals); Equal(1L, f.Owner.Read().EventSnapshots.Single().Entered);
            Equal(1L, f.Owner.Read().EventSnapshots.Single().Exited); Equal(0L, f.Owner.Read().NativeInFlight);
        }
        using (var f = new Fixture())
        {
            var reads = f.Backend.GraphReads; f.Backend.Call(101, new(0xBAD));
            Check(f.Owner.Read().Unknown); Equal(reads, f.Backend.GraphReads); Equal(1, f.Backend.Originals);
        }
        using (var f = new Fixture())
        {
            var graph = f.Backend.Graphs[0]; var reads = f.Backend.GraphReads;
            Task.Run(() => f.Owner.ObservePrepareFault(f.Backend.PrepareFrame(graph.Calls, 0x100), "actual calls worker")).GetAwaiter().GetResult();
            Check(f.Owner.Read().Unknown); Equal(reads, f.Backend.GraphReads); Equal(0, f.Backend.ResultAdoptions);
        }
    }

    private static void UnknownGuardDiagnostics()
    {
        const string guardMessage = "Listener/source authority became unknown during observation.";
        using (var f = new Fixture())
        {
            const string first = "Owned UnityEvent worker, metadata mismatch or physical reentry.";
            f.Backend.Call(101, new(0xBAD));
            Equal(1, f.Backend.Originals);
            Equal(first, f.Owner.Read().Failure);
            f.Owner.ObserveInvokeFault(new NativeVoidFrame { Self = new(101) }, "A later controlled observer failed.");
            f.Authority.Faulted = true;
            f.Backend.CleanupUnknown = true;
            ExpectGuard(f, $"{guardMessage} First ledger failure: {first}");
            Equal(first, f.Owner.Read().Failure);
        }
        using (var f = new Fixture())
        {
            f.Authority.Faulted = true;
            f.Backend.CleanupUnknown = true;
            ExpectGuard(f, $"{guardMessage} The retained listener/source association is unknown.");
            Check(f.Owner.Read().Failure is null);
        }
        using (var f = new Fixture())
        {
            f.Backend.CleanupUnknown = true;
            ExpectGuard(f, $"{guardMessage} Listener backend cleanup is unknown.");
            Check(f.Owner.Read().Failure is null);
        }

        static void ExpectGuard(Fixture fixture, string message)
        {
            var graphReads = fixture.Backend.GraphReads;
            var activityReads = fixture.Backend.ActivityReads;
            try { fixture.Owner.TryConfirmNativeIdle(); }
            catch (InvalidOperationException error)
            {
                Equal(message, error.Message);
                Equal(graphReads, fixture.Backend.GraphReads);
                Equal(activityReads, fixture.Backend.ActivityReads);
                return;
            }
            throw new InvalidOperationException("The unknown UnityEvent ledger must refuse a new idle confirmation.");
        }
    }

    private static void ReentryAndException()
    {
        using (var f = new Fixture())
        {
            var once = false; f.Backend.DuringInvoke = receiver => { if (!once) { once = true; f.Backend.Call(receiver); } };
            f.Backend.Call(101); Check(f.Owner.Read().Unknown); Equal(2, f.Backend.Originals);
            Equal(0L, f.Owner.Read().ReturnedInvocations); Equal(2L, f.Owner.Read().Invocations); Equal(0L, f.Owner.Read().NativeInFlight);
        }
        using (var f = new Fixture())
        {
            var expected = new IOException("Actual controlled original did not reach after."); f.Backend.DuringInvoke = _ => throw expected;
            try { f.Backend.Call(101); throw new Exception("Original exception was swallowed."); }
            catch (IOException error) { Check(ReferenceEquals(expected, error)); }
            Check(f.Owner.Read().Unknown); Equal(0L, f.Owner.Read().ReturnedInvocations); Equal(0L, f.Owner.Read().NativeInFlight);
        }
    }

    private static void SourceFaultReads()
    {
        using (var f = new Fixture())
        {
            var reads = f.Backend.GraphReads;
            f.Backend.DuringGraph = _ => f.Authority.Faulted = true; f.Backend.Call(101);
            Equal(reads + 1, f.Backend.GraphReads); Check(f.Owner.Read().Unknown); Equal(1, f.Backend.Originals);
            Equal(0L, f.Owner.Read().ReturnedInvocations); Check(f.Owner.Read().EventSnapshots.Single().Pre is null);
        }
        using (var f = new Fixture())
        {
            var graph = f.Backend.Graphs[0]; f.Backend.DuringAdopt = () => f.Authority.Faulted = true;
            f.Backend.Prepare(graph.Calls, graph.ExecutingCalls.Object, 0x987654);
            Check(f.Owner.Read().Unknown); Equal(1, f.Backend.ResultAdoptions); Equal(0L, f.Owner.Read().PreparedInvocations);
            Check(f.Owner.Read().EventSnapshots.Single().IndependentPrepared is null);
        }
    }

    private static void DirtyAndMutation()
    {
        using var f = new Fixture(); var initial = f.Backend.Graphs[0];
        f.Backend.Graphs[0] = initial with { CallsDirty = true, NeedsUpdate = true };
        f.Backend.DuringInvoke = _ => {
            var current = f.Backend.Graphs[0];
            f.Backend.Graphs[0] = current with { CallsDirty = false, NeedsUpdate = false,
                ExecutingCalls = current.ExecutingCalls with { Object = 777, Version = 9 } };
        };
        f.Backend.Call(101); var row = f.Owner.Read().EventSnapshots.Single();
        Check(row.Pre!.CallsDirty && row.Pre.NeedsUpdate); Check(!row.Post!.CallsDirty && !row.Post.NeedsUpdate);
        Equal(777L, row.Post.ExecutingCalls.Object); Equal(initial.ExecutingCalls.Object, row.Initial.ExecutingCalls.Object);
        Check(row.Returned && row.ChangedSinceCapture && !f.Owner.Read().Unknown);
        Check(f.Owner.Read().Pending && !f.Owner.Read().MutationClosed && f.Owner.Read().InlineExecutionUnobserved);
        f.Backend.DuringInvoke = _ => { var old = f.Backend.Graphs[0]; f.Backend.Graphs[0] = old with { NeedsUpdate = true }; f.Backend.Graphs[0] = old; };
        f.Backend.Call(101); Check(f.Owner.Read().Pending && !f.Owner.Read().MutationClosed);
    }

    private static void InvalidPrepare()
    {
        foreach (var stage in new[] { "null", "other-list", "calls", "order", "missing-after", "caller" })
        {
            using var f = new Fixture(); var graph = f.Backend.Graphs[0];
            if (stage == "other-list") f.Backend.PreparedOverride = new(graph, graph.ExecutingCalls with { Object = 888 });
            if (stage == "calls") f.Backend.PreparedOverride = new(graph with { Calls = 999 }, graph.ExecutingCalls);
            if (stage == "order") f.Backend.PreparedOverride = new(graph,
                graph.ExecutingCalls with { Rows = Array.Empty<NativeUnityEventInvokable>() });
            f.Backend.Prepare(graph.Calls, stage == "null" ? 0 : graph.ExecutingCalls.Object, 0xAE86,
                after: stage != "missing-after", changeAfterCaller: stage == "caller");
            Check(f.Owner.Read().Unknown); Equal(0L, f.Owner.Read().PreparedInvocations);
            var adopts = f.Backend.ResultAdoptions; Throws<InvalidOperationException>(() => f.Owner.TryConfirmNativeIdle());
            Equal(adopts, f.Backend.ResultAdoptions);
        }
    }

    private static void AmbiguousPrepare()
    {
        using (var f = new Fixture(events: 2, sharedCalls: true))
        {
            f.Backend.Prepare(f.Backend.Graphs[0].Calls, f.Backend.Graphs[0].ExecutingCalls.Object, 0xABC);
            Check(f.Owner.Read().Unknown); Equal(0, f.Backend.ResultAdoptions);
        }
        using (var f = new Fixture())
        {
            var graph = f.Backend.Graphs[0]; f.Backend.DuringPrepare = () => {
                f.Backend.DuringPrepare = null; f.Backend.Prepare(graph.Calls, graph.ExecutingCalls.Object, 0xDEF);
            };
            f.Backend.Prepare(graph.Calls, graph.ExecutingCalls.Object, 0xABC);
            Check(f.Owner.Read().Unknown); Equal(0, f.Backend.ResultAdoptions); Equal(2, f.Backend.PrepareOriginals);
        }
    }

    private static void ImmutableSnapshots()
    {
        using var f = new Fixture(); var initial = f.Owner.Read().EventSnapshots.Single().Initial;
        var graph = f.Backend.Graphs[0]; var rawRows = (NativeUnityEventInvokable[])graph.ExecutingCalls.Rows;
        var rawLeaves = (NativeUnityEventLeaf[])rawRows[0].Leaves; var originalDelegate = rawLeaves[0].Delegate;
        rawLeaves[0] = rawLeaves[0] with { Delegate = 9999 }; rawRows[0] = rawRows[0] with { Delegate = 9998, InvocationList = 9997 };
        Equal(originalDelegate, initial.ExecutingCalls.Rows[0].Leaves[0].Delegate);
        Equal(600L, initial.ExecutingCalls.Rows[0].Delegate); Equal(601L, initial.ExecutingCalls.Rows[0].InvocationList);
        Throws<NotSupportedException>(() => ((IList<NativeUnityEventInvokable>)initial.ExecutingCalls.Rows).Clear());
        Throws<NotSupportedException>(() => ((IList<NativeUnityEventLeaf>)initial.ExecutingCalls.Rows[0].Leaves).Clear());
        Throws<NotSupportedException>(() => ((IList<NativeUnityEventPersistent>)initial.Candidates).Clear());
        Throws<NotSupportedException>(() => ((IList<NativeUnityEventLedgerEventSnapshot>)f.Owner.Read().EventSnapshots).Clear());
        f.Backend.Call(101); Check(f.Owner.Read().EventSnapshots.Single().ChangedSinceCapture); Check(f.Owner.Read().Pending);
    }

    private static void Bounds()
    {
        foreach (var stage in new[] { "events", "rows", "leaves", "roots", "identity" })
        {
            using var main = new MainThreadDispatcher(); var authority = new Authority();
            var backend = new Backend(main, stage == "events" ? 33 : 1, false);
            if (stage == "rows") backend.Graphs[0] = backend.Graphs[0] with { Candidates = Enumerable.Range(0, 129).Select(i => Candidate(10000 + i)).ToArray() };
            if (stage == "leaves")
            {
                var row = new NativeUnityEventInvokable(501, 502, false, Enumerable.Range(0, 129).Select(i => Leaf(10000 + i)).ToArray());
                backend.Graphs[0] = backend.Graphs[0] with { ExecutingCalls = new(600, 1, new[] { row }) };
            }
            if (stage == "roots") backend.Roots = 2049;
            if (stage == "identity") backend.Graphs[0] = backend.Graphs[0] with { Event = 999 };
            NativeLocalUnityEventLedgerException? captured = null;
            try { NativeLocalUnityEventLedger.CaptureBackend(main, backend, authority, backend.Module); }
            catch (NativeLocalUnityEventLedgerException error) { captured = error; }
            Check(captured is not null && captured.Responsibility.Read().Unknown);
            Check(backend.RootRecords > 0); Equal(0, backend.Originals); Equal(0, backend.ResultAdoptions);
        }
        using var total = new Fixture(events: 2);
        var row80 = Enumerable.Range(0, 80).Select(i => Candidate(20000 + i)).ToArray();
        total.Backend.Graphs[0] = total.Backend.Graphs[0] with { Candidates = row80 };
        total.Backend.Call(101); Check(!total.Owner.Read().Unknown);
        total.Backend.Graphs[1] = total.Backend.Graphs[1] with { Candidates = row80 };
        total.Backend.Call(102); Check(total.Owner.Read().Unknown);
    }

    private static void IdleAndActivity()
    {
        using (var f = new Fixture())
        {
            var reads = f.Backend.ActivityReads; f.Authority.SourceIdle = false;
            Check(!f.Owner.TryConfirmNativeIdle()); Equal(reads, f.Backend.ActivityReads);
            f.Authority.SourceIdle = true; f.Backend.ForeignNative = 1; Check(!f.Owner.TryConfirmNativeIdle());
            f.Backend.ForeignNative = 0; Check(f.Owner.TryConfirmNativeIdle());
            f.Backend.Known = false; Throws<InvalidOperationException>(() => f.Owner.TryConfirmNativeIdle()); Check(f.Owner.Read().Unknown);
        }
        using (var f = new Fixture())
        {
            f.Backend.DuringActivity = () => f.Authority.Faulted = true;
            Throws<InvalidOperationException>(() => f.Owner.TryConfirmNativeIdle()); Check(f.Owner.Read().Unknown);
        }
    }

    private static void CaptureFaults()
    {
        using var main = new MainThreadDispatcher(); var authority = new Authority(); var backend = new Backend(main, 1, false) { FailInitialize = true };
        NativeLocalUnityEventLedgerException? captured = null;
        try { NativeLocalUnityEventLedger.CaptureBackend(main, backend, authority, backend.Module); }
        catch (NativeLocalUnityEventLedgerException error) { captured = error; }
        Check(captured is not null); Check(captured!.Responsibility.Read().Unknown); Check(!captured.Responsibility.IsFor(null!));
        Equal(1, backend.RootRecords); Throws<InvalidOperationException>(() => captured.Responsibility.TryConfirmNativeIdle());
    }
    private static void NativeInvokeFrames()
    {
        using (var f = new Fixture())
        {
            f.Backend.Call(101); var row = f.Owner.Read().EventSnapshots.Single();
            Check(row.NativeInvokeObserved && row.Returned && row.InvokeCaller == Backend.ControlledCaller && row.InvokeMethod == 0x123);
            Check(f.Owner.Read().InlineExecutionUnobserved && !f.Owner.Read().DispatchPathVerified);
        }
        using (var f = new Fixture())
        {
            f.Backend.ChangeExitCaller = true; f.Backend.Call(101);
            var state = f.Owner.Read(); Check(state.Unknown && state.Pending); Equal(0L, state.ReturnedInvocations);
            Equal(1, f.Backend.Originals);
        }
    }
    private static void NativeTransportAuthority()
    {
        using var f = new Fixture(); var reads = f.Backend.GraphReads;
        f.Backend.Known = false; f.Backend.Call(101);
        Check(f.Owner.Read().Unknown && f.Owner.Read().Pending); Equal(reads, f.Backend.GraphReads);
        Equal(1, f.Backend.Originals); Equal(0L, f.Owner.Read().ReturnedInvocations);
    }
    private static void PrepareTransportAuthority()
    {
        foreach (var stage in new[] { "entry", "original", "adoption" })
        {
            using var f = new Fixture(); var graph = f.Backend.Graphs[0]; var reads = f.Backend.GraphReads;
            if (stage == "entry") f.Backend.Known = false;
            if (stage == "original") f.Backend.DuringPrepare = () => f.Backend.Known = false;
            if (stage == "adoption") f.Backend.DuringAdopt = () => f.Backend.Known = false;
            f.Backend.Prepare(graph.Calls, graph.ExecutingCalls.Object, 0x71234568);
            var state = f.Owner.Read(); Check(state.Unknown && state.Pending && state.InlineExecutionUnobserved);
            Equal(1, f.Backend.PrepareOriginals); Equal(reads, f.Backend.GraphReads);
            Equal(stage == "adoption" ? 1 : 0, f.Backend.ResultAdoptions);
            Equal(0L, state.PreparedInvocations); Check(state.EventSnapshots.Single().IndependentPrepared is null);
        }
    }
    private static void InlineList()
    {
        using var f = new Fixture(); var graph = f.Backend.Graphs[0];
        Check(!f.Backend.Inline(101, graph.ExecutingCalls.Object)); Equal(0, f.Backend.InlineAdoptions);
        f.Backend.DuringInvoke = _ => Check(f.Backend.Inline(101, graph.ExecutingCalls.Object));
        f.Backend.Call(101); var state = f.Owner.Read(); var row = state.EventSnapshots.Single();
        Check(!state.Unknown && state.Pending && state.InlineExecutionUnobserved && !state.DispatchPathVerified);
        Check(row.Inline is not null && row.Inline.NativeToken > 0 && row.Returned);
        Equal(graph.ExecutingCalls.Object, row.Inline!.Executing.Object); Equal(Backend.ControlledCaller, row.Inline.ParentCaller);
        Equal(f.Backend.Module + 0x1564420, row.Inline.MidReturnPc); Equal(1, f.Backend.InlineAdoptions);
        Check(!f.Backend.Inline(101, graph.ExecutingCalls.Object)); Equal(1, f.Backend.InlineAdoptions);
        var oldToken = row.Inline.NativeToken; f.Backend.Call(101);
        Check(f.Owner.Read().EventSnapshots.Single().Inline!.NativeToken != oldToken);
    }
    private static void InlineEmptyAndMutation()
    {
        using var f = new Fixture(); var original = f.Backend.Graphs[0];
        f.Backend.DuringInvoke = _ => {
            var inline = original with { ExecutingCalls = new(901, 8, Array.Empty<NativeUnityEventInvokable>()) };
            f.Backend.Graphs[0] = inline; Check(f.Backend.Inline(101, 901));
            f.Backend.Graphs[0] = original with { ExecutingCalls = original.ExecutingCalls with { Object = 902 } };
        };
        f.Backend.Call(101); var row = f.Owner.Read().EventSnapshots.Single();
        Equal(original.ExecutingCalls.Object, row.Pre!.ExecutingCalls.Object); Equal(901L, row.Inline!.Executing.Object);
        Equal(0, row.Inline.Executing.Rows.Count); Equal(902L, row.Post!.ExecutingCalls.Object);
        Check(row.IndependentPrepared is null && f.Owner.Read().Pending && !f.Owner.Read().DispatchPathVerified);
        Throws<NotSupportedException>(() => ((IList<NativeUnityEventInvokable>)row.Inline.Executing.Rows).Clear());
    }
    private static void InlineIsolation()
    {
        using var f = new Fixture(); var list = f.Backend.Graphs[0].ExecutingCalls.Object;
        f.Backend.DuringInvoke = receiver => {
            if (receiver == 101)
            {
                Task.Run(() => Check(!f.Backend.Inline(101, list))).GetAwaiter().GetResult();
                f.Backend.Call(999); Check(f.Backend.Inline(101, list));
            }
            else { Check(!f.Backend.Inline(101, list)); Check(!f.Backend.Inline(999, list)); }
        };
        f.Backend.Call(101); Equal(1, f.Backend.InlineAdoptions); Check(!f.Owner.Read().Unknown);
    }
    private static void InlineRefusals()
    {
        foreach (var stage in new[] { "return", "shape", "list", "duplicate" })
        {
            using var f = new Fixture(); var graph = f.Backend.Graphs[0];
            if (stage == "list") f.Backend.InlineOverride = new(graph, graph.ExecutingCalls with { Object = 999 });
            f.Backend.DuringInvoke = _ => {
                try
                {
                    var frame = f.Backend.MidFrame(101, graph.ExecutingCalls.Object);
                    if (stage == "return") frame.ReturnPc = new(frame.ReturnPc.ToInt64() + 1);
                    if (stage == "shape") frame.Reserved1 = 1;
                    f.Backend.Inline(frame);
                    if (stage == "duplicate") f.Backend.Inline(frame);
                }
                catch (InvalidOperationException) { }
            };
            f.Backend.Call(101); Check(f.Owner.Read().Unknown && f.Owner.Read().Pending);
            Equal(stage is "list" or "duplicate" ? 1 : 0, f.Backend.InlineAdoptions);
            Equal(0L, f.Owner.Read().ReturnedInvocations);
        }
    }
    private static void InlineAdoptionFaults()
    {
        foreach (var authority in new[] { true, false })
        {
            using var f = new Fixture(); var list = f.Backend.Graphs[0].ExecutingCalls.Object;
            f.Backend.DuringInlineAdopt = () => { if (authority) f.Authority.Faulted = true; else f.Backend.Known = false; };
            f.Backend.DuringInvoke = _ => { try { f.Backend.Inline(101, list); } catch (InvalidOperationException) { } };
            f.Backend.Call(101); var state = f.Owner.Read();
            Check(state.Unknown && state.Pending && state.EventSnapshots.Single().Inline is null);
            Equal(1, f.Backend.InlineAdoptions); Equal(0L, state.ReturnedInvocations);
        }
    }
    private static void InlineStaleCapability()
    {
        using var f = new Fixture(); var list = f.Backend.Graphs[0].ExecutingCalls.Object;
        ExactHookBroker.NativeVoidPhysicalIdentity? stale = null;
        f.Backend.DuringInvoke = _ => { Check(f.Backend.Current(101, out var context, out stale)); Check(stale!.IsCurrentFor(context!)); };
        f.Backend.Call(101); Check(stale is not null);
        f.Backend.DuringInvoke = _ => {
            Check(f.Backend.Current(101, out var context, out var current));
            Check(current!.Token != stale!.Token && !stale.IsCurrentFor(context!));
            Throws<InvalidOperationException>(() => f.Owner.ObserveInlineList(context!, f.Backend.MidFrame(101, list), stale));
        };
        f.Backend.Call(101); Check(f.Owner.Read().Unknown); Equal(0, f.Backend.InlineAdoptions);
    }

    private static NativeUnityEventLeaf Leaf(long value = 701) => new(value, 702, 703, 704, 705, 706, 707, 708, 709, 710, false, true, "unknown listener");
    private static NativeUnityEventPersistent Candidate(long value = 801) => new(value, 802, 803, 804, 805, 7, 9, 806, 807, 808, 17, unchecked((int)0x80000000), 809, true);
    private static NativeUnityEventGraph Graph(int index, bool sharedCalls)
    {
        var delta = index * 1000L; var invokable = new NativeUnityEventInvokable(501 + delta, 502, true, new[] { Leaf(701 + delta) }, 600 + delta, 601 + delta);
        return new(101 + index, sharedCalls ? 201 : 201 + delta, 202 + delta, false, false,
            new(301 + delta, 1, Array.Empty<NativeUnityEventInvokable>()), new(302 + delta, 2, Array.Empty<NativeUnityEventInvokable>()),
            new(303 + delta, 3, new[] { invokable }), 304 + delta, 4, new[] { Candidate(801 + delta) }, 0);
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Authority Authority = new();
        internal readonly Backend Backend;
        internal readonly NativeLocalUnityEventLedger Owner;
        internal Fixture(int events = 1, bool sharedCalls = false, NativeUnityEventInvokable? initialInvokable = null)
        {
            Backend = new(Main, events, sharedCalls);
            if (initialInvokable is not null)
                Backend.Graphs[0] = Backend.Graphs[0] with {
                    ExecutingCalls = Backend.Graphs[0].ExecutingCalls with { Rows = new[] { initialInvokable } } };
            Owner = NativeLocalUnityEventLedger.CaptureBackend(Main, Backend, Authority, Backend.Module);
            Authority.Before = false;
        }
        internal void NoPrepareError() { Check(Backend.PrepareCaptureError is null && Backend.PrepareAfterError is null); Check(!Owner.Read().Unknown); }
        public void Dispose() => Main.Dispose(); // No false release or process-owner reset.
    }
    private sealed class Authority : ICutoutListenerAuthority
    {
        internal bool Before = true, Faulted, SourceIdle = true;
        internal int Faults;
        public bool Unknown => Faulted;
        public void VerifyBeforeCapture() { if (!Before || Faulted) throw new InvalidOperationException("Capture is outside the real preactivation source window."); }
        public void VerifyObservation() { if (Before || Faulted) throw new InvalidOperationException("Exact active source responsibility unavailable."); }
        public bool TryConfirmSourceIdle() => SourceIdle;
        public void ObserveFaultFromAnyThread() => Interlocked.Increment(ref Faults);
    }
    private sealed partial class Backend : ICutoutListenerBackend, INativeDetourEngine, IPreparedNativeDetourEngine
    {
        internal readonly long Module;
        internal const long ControlledCaller = 0x71234567;
        private static long NextTarget = 0x7A0000000;
        private readonly MainThreadDispatcher _main;
        private readonly ExactHookBroker _broker;
        private readonly VerifiedHookTarget _target;
        private readonly ControlledBridge _bridge = new();
        private ExactHookBroker.Binding? _binding;
        private ExactHookBroker.BoundaryProbeBinding? _probe;
        internal readonly NativeUnityEventGraph[] Graphs;
        internal NativeLocalUnityEventLedger Owner = null!;
        internal Action<long>? DuringInvoke;
        internal Action<int>? DuringGraph;
        internal Action? DuringAdopt, DuringActivity, DuringPrepare, DuringInlineAdopt;
        internal NativeUnityEventPrepared? PreparedOverride;
        internal NativeUnityEventPrepared? InlineOverride;
        internal Exception? PrepareCaptureError, PrepareAfterError;
        internal int Originals, GraphReads, ResultAdoptions, ActivityReads, PrepareOriginals, PrepareInFlight, ForeignNative, Roots;
        internal int InlineAdoptions;
        internal bool Known = true, FailInitialize, ChangeExitCaller;
        public bool CleanupUnknown { get; set; }
        public int RootRecords => Roots;
        internal Backend(MainThreadDispatcher main, int events, bool sharedCalls)
        {
            _main = main; _broker = new(this, main, new Logger());
            Module = Interlocked.Add(ref NextTarget, 0x10000000);
            _target = new("unity.event.ledger.fixture", new(Module + 0x15642E0), new(0x123), new(0x456), ExactHookAbi.NativeVoid0);
            Graphs = Enumerable.Range(0, events).Select(index => Graph(index, sharedCalls)).ToArray();
        }
        public void Initialize(NativeLocalUnityEventLedger owner)
        {
            Owner = owner;
            for (var index = 0; index < Graphs.Length; ++index)
            {
                Roots++; var slot = owner.RegisterEvent(101 + index); Equal(index, slot);
                if (FailInitialize) { CleanupUnknown = true; throw new IOException("Controlled native adoption failed after its first root publication."); }
            }
            _binding = _broker.BindNativeVoidObservation(_target, () => _bridge,
                frame => owner.ShouldObserveInvoke(frame.Self.ToInt64()) ? owner.CaptureNativeInvoke(frame) : null,
                owner.CompleteNativeInvoke, owner.ObserveInvokeFault);
            _probe = _broker.BindBoundaryProbe(_target,
                (pointer, _, method) => owner.ProbeEnter(pointer.ToInt64(), method == _target.MethodInfo),
                (pointer, _, _, returned) => owner.ProbeExit(pointer.ToInt64(), returned));
        }
        public NativeUnityEventGraph ReadGraph(int eventSlot)
        { _main.VerifyAccess(); ++GraphReads; DuringGraph?.Invoke(eventSlot); return Graphs[eventSlot]; }
        public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList)
        {
            _main.VerifyAccess(); ++ResultAdoptions; ++Roots; DuringAdopt?.Invoke();
            return PreparedOverride ?? new(Graphs[eventSlot], Graphs[eventSlot].ExecutingCalls);
        }
        public NativeUnityEventPrepared CaptureInline(int eventSlot, long actualList)
        {
            _main.VerifyAccess(); ++InlineAdoptions; ++Roots; DuringInlineAdopt?.Invoke();
            return InlineOverride ?? new(Graphs[eventSlot], Graphs[eventSlot].ExecutingCalls);
        }
        internal NativeEventMidFrame MidFrame(long receiver, long list) => new() {
            Version = 1, Size = 0x1b0, ReturnPc = new(Module + 0x1564420), EntryRsp = 0x10008, Rsp = 0x10010,
            Rsi = unchecked((ulong)receiver), Rbp = unchecked((ulong)list) };
        internal bool Inline(long receiver, long list) => Inline(MidFrame(receiver, list));
        internal bool Current(long receiver, out NativeLocalUnityEventLedger.InvokeContext? context,
            out ExactHookBroker.NativeVoidPhysicalIdentity? identity)
            => _binding!.TryGetCurrentNativeVoidContext(new(receiver), out context, out identity);
        internal bool Inline(NativeEventMidFrame frame)
        {
            if (!_binding!.TryGetCurrentNativeVoidContext<NativeLocalUnityEventLedger.InvokeContext>(frame.Event, out var context, out var identity)
                || context!._record is null) return false;
            Owner.ObserveInlineList(context, frame, identity!); return true;
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        {
            _main.VerifyAccess(); ++ActivityReads; DuringActivity?.Invoke();
            var actual = _binding?.ReadActivity() ?? new(0, false); var probe = _probe?.ReadActivity() ?? new(0, false);
            return new(Math.Max(actual.InFlight, probe.InFlight) + PrepareInFlight + ForeignNative, actual.Known && probe.Known && Known);
        }
        public Delegate Install(VerifiedHookTarget target, Delegate replacement)
            => throw new InvalidOperationException("Native void observation cannot install a managed-original transport.");
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement) => new(0x123456);
        public void Commit(VerifiedHookTarget target) { }
        internal void Call(long receiver, IntPtr? method = null)
        {
            var frame = new NativeVoidFrame { Version = 1, Size = 40, Self = new(receiver), Method = method ?? _target.MethodInfo, Caller = new(ControlledCaller) };
            _bridge.Call(frame, () => { Interlocked.Increment(ref Originals); DuringInvoke?.Invoke(receiver); }, ChangeExitCaller);
        }
        internal NativeCoroutineFrame PrepareFrame(long receiver, long caller) => new() {
            Version = 1, Size = 48, Kind = NativeCoroutineKind.Factory, Self = new(receiver), Caller = new(caller), Method = new(0x789) };
        internal void Prepare(long receiver, long returned, long caller, bool after = true, bool changeAfterCaller = false)
        {
            var frame = PrepareFrame(receiver, caller); NativeLocalUnityEventLedger.PrepareContext? context = null; ++PrepareInFlight;
            try
            {
                try { context = Owner.CapturePrepare(frame); } catch (Exception error) { PrepareCaptureError ??= error; }
                ++PrepareOriginals; DuringPrepare?.Invoke();
                if (context is not null && after)
                    try { var actual = frame; if (changeAfterCaller) actual.Caller = new(caller + 1); Owner.CompletePrepare(context, actual, unchecked((nuint)returned)); }
                    catch (Exception error) { PrepareAfterError ??= error; }
            }
            finally { try { context?.Dispose(); } finally { --PrepareInFlight; } }
        }
    }
    private sealed class ControlledBridge : INativeVoidContextBridge
    {
        public IntPtr EntryAddress => new(0x123451);
        private Func<NativeVoidFrame, nuint> _enter = null!;
        private Action<NativeVoidFrame, nuint, NativeVoidExitFlags> _exit = null!;
        private long _inFlight;
        public void Configure(IntPtr original, Func<NativeVoidFrame, nuint> enter,
            Action<NativeVoidFrame, nuint, NativeVoidExitFlags> exit)
            => (_enter, _exit) = (enter, exit);
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(Interlocked.Read(ref _inFlight), true);
        internal void Call(NativeVoidFrame frame, Action original, bool changeExitCaller)
        {
            Interlocked.Increment(ref _inFlight); nuint token = 0; var returned = false;
            try { token = _enter(frame); original(); returned = true; }
            finally
            {
                try
                {
                    if (changeExitCaller) frame.Caller = new(frame.Caller.ToInt64() + 1);
                    _exit(frame, token, returned ? NativeVoidExitFlags.Returned : NativeVoidExitFlags.Abnormal);
                }
                finally { Interlocked.Decrement(ref _inFlight); }
            }
        }
    }
    private sealed class Logger : ILogger
    { public void Log(LogLevel level, string message, Exception? error = null) { } }
}
