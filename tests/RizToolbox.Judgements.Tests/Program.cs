using RizToolbox.Adapter.Judgements;
using RizToolbox.Core;
using RizToolbox.Sdk;
using RizToolbox.Runtime.Hooks;

var tests = new (string Name, Action Run)[] {
    ("All twelve pinned callers select their exact register list or coroutine source", () => {
        var sources = new SourceReader();
        Check(JudgementSourceResolver.Sites.Count == 12 && JudgementSourceResolver.Sites.Select(x => x.ReturnRva).Distinct().Count() == 12);
        Check(JudgementSourceResolver.Sites.Count(x => x.DeltaKind == JudgementDeltaKind.MeasuredTimeDifference) == 4);
        foreach (var site in JudgementSourceResolver.Sites) {
            var frame = Frame(site); var actual = JudgementSourceResolver.Resolve(site,frame,sources);
            Check(JudgementSourceResolver.Find(frame.ReturnAddress,0x10000000) == site && actual == new JudgementNoteSourceValue(50,60));
            Check(JudgementSourceResolver.Find(frame.ReturnAddress+1,0x10000000) is null);
        }
        Check(sources.TapReads == 2 && sources.DragReads == 3 && sources.HoldReads == 2 && sources.OwnerChecks == 12 && sources.NoteChecks == 12);
    }),
    ("Unknown caller invalid list index sentinel and nonfinite deltas do not synthesize a note", () => {
        var source = new SourceReader(); var site = JudgementSourceResolver.Sites[2]; var frame = Frame(site);
        Check(JudgementSourceResolver.Find(0x10000001,0x10000000) is null && JudgementSourceResolver.Find(1,0x10000000) is null);
        frame.Rdi = uint.MaxValue; MustThrow(() => JudgementSourceResolver.Resolve(site,frame,source));
        frame = Frame(site); frame.Xmm2.Low = 0; MustThrow(() => JudgementSourceResolver.Resolve(site,frame,source));
        frame = Frame(JudgementSourceResolver.Sites[0]); frame.Xmm2.Low = 0x7f800000;
        MustThrow(() => JudgementSourceResolver.Resolve(JudgementSourceResolver.Sites[0],frame,source));
    }),
    ("Actual inner return publishes distinct requests with one stable note and before-after counters", () => {
        using var f = new CoordinatorFixture(); var frame = Frame(JudgementSourceResolver.Sites[0]);
        using (var outer = f.Coordinator.CaptureOuter(frame)) {
            Check(outer is not null);
            using var one = f.Coordinator.CaptureInner(new(22),1,.0125f);
            f.Backend.Perfect++; f.Coordinator.CompleteInner(one,10);
            using var two = f.Coordinator.CaptureInner(new(22),4,.0125f);
            f.Backend.MaxPerfect++; f.Coordinator.CompleteInner(two,11);
            Check(f.Backend.RunReleases == 0 && f.Backend.SourceReleases == 0);
        }
        Check(f.Published.Count == 2 && f.Published[0].RuntimeNote == f.Published[1].RuntimeNote
            && f.Published[0].ChartNote == new GameplayChartNoteLocation(2,3)
            && f.Published[0].CountersBefore!.Perfect == 0 && f.Published[0].CountersAfter!.Perfect == 1
            && f.Published[1].CountersBefore!.MaxPerfect == 0 && f.Published[1].CountersAfter!.MaxPerfect == 1);
        Check(f.Published.All(x => x.OriginalReturned && x.DeltaKind == JudgementDeltaKind.MeasuredTimeDifference
            && x.DeltaUnit == GameplayClockUnit.RawGameUnits) && f.Backend.NoteRetains == 1 && f.Backend.InnerReleases == 2);
    }),
    ("Outer-only invocation and an abnormal inner original never publish a completed judgement", () => {
        using var f = new CoordinatorFixture();
        using (var outer = f.Coordinator.CaptureOuter(Frame(JudgementSourceResolver.Sites[0]))) { }
        using (var outer = f.Coordinator.CaptureOuter(Frame(JudgementSourceResolver.Sites[0])))
        using (var inner = f.Coordinator.CaptureInner(new(22),1,.1f)) { /* original throws: broker never calls CompleteInner */ }
        Check(f.Published.Count == 0 && f.Backend.SourceReleases == 2 && f.Backend.InnerReleases == 1 && f.Backend.NoteRetains == 1);
    }),
    ("Final judgement retains its origin through run completion and retires roots only after outer exit", () => {
        using var f = new CoordinatorFixture(); var outer = f.Coordinator.CaptureOuter(Frame(JudgementSourceResolver.Sites[11]));
        using (var inner = f.Coordinator.CaptureInner(new(22),1,-2)) {
            f.Backend.Current = null; f.Coordinator.Tick(); Check(f.Backend.RunReleases == 0 && f.Backend.NoteReleases == 0);
            f.Coordinator.CompleteInner(inner,99);
        }
        Check(f.Published.Single().RuntimeNote!.ObservedRunId == "run" && f.Published.Single().DeltaKind == JudgementDeltaKind.Sentinel
            && f.Published.Single().RawDelta == -2 && f.Backend.RunReleases == 0);
        outer!.Dispose(); Check(f.Backend.RunReleases == 1 && f.Backend.NoteReleases == 1);
    }),
    ("Suppressed nested native call cannot inherit the outer note even when the receiver matches", () => {
        using var f = new CoordinatorFixture(); using var outer = f.Coordinator.CaptureOuter(Frame(JudgementSourceResolver.Sites[0]));
        f.Depth = 2; using (var nested = f.Coordinator.CaptureInner(new(22),1,.1f)) f.Coordinator.CompleteInner(nested,1);
        Check(f.Backend.InnerCaptures == 0 && f.Published.Count == 0);
        f.Depth = 1; using (var valid = f.Coordinator.CaptureInner(new(22),1,.1f)) f.Coordinator.CompleteInner(valid,2);
        Check(f.Published.Count == 1);
    }),
    ("Unknown source withdraws the same run without repeatedly rebuilding its note identity", () => {
        using var f = new CoordinatorFixture(); var frame = Frame(JudgementSourceResolver.Sites[0]); frame.ReturnAddress++;
        Check(f.Coordinator.CaptureOuter(frame) is null);
        Check(f.Coordinator.CaptureOuter(Frame(JudgementSourceResolver.Sites[0])) is null && f.Backend.RunCaptures == 1);
        Check(!f.Coordinator.Status().RunIdentityAvailable && f.Coordinator.Status().UnavailableReason!.Contains("twelve-source"));
        f.Backend.Current = Fixture.Origin with { RunId = "next" };
        using var next = f.Coordinator.CaptureOuter(Frame(JudgementSourceResolver.Sites[0]));
        Check(next is not null && next.Lease.Identity.RunId == "next" && f.Backend.RunCaptures == 2);
    }),
    ("Wrong inner owner withdraws coverage and never publishes borrowed note identity", () => {
        using var f = new CoordinatorFixture(); var outer = f.Coordinator.CaptureOuter(Frame(JudgementSourceResolver.Sites[0]));
        using (var wrong = f.Coordinator.CaptureInner(new(999),1,.1f)) f.Coordinator.CompleteInner(wrong,1);
        Check(f.Published.Count == 0 && !f.Coordinator.Status().RunIdentityAvailable && f.Backend.RunReleases == 0);
        outer!.Dispose(); Check(f.Backend.RunReleases == 1);
    }),
    ("Failed post-original counter read withdraws this run and defers roots until the caller exits", () => {
        using var f = new CoordinatorFixture(); var outer = f.Coordinator.CaptureOuter(Frame(JudgementSourceResolver.Sites[0]));
        using (var inner = f.Coordinator.CaptureInner(new(22),1,.1f)) {
            f.Backend.FailCounters = true; f.Coordinator.CompleteInner(inner,1);
            Check(f.Published.Count == 0 && f.Coordinator.Status().UnassociatedCalls == 1
                && !f.Coordinator.Status().RunIdentityAvailable && f.Backend.RunReleases == 0);
        }
        outer!.Dispose(); Check(f.Backend.RunReleases == 1 && f.Backend.NoteReleases == 1 && f.Backend.InnerReleases == 1);
        f.Backend.FailCounters = false;
        Check(f.Coordinator.CaptureOuter(Frame(JudgementSourceResolver.Sites[0])) is null && f.Backend.RunCaptures == 1);
        f.Backend.Current = Fixture.Origin with { RunId = "next" };
        using var next = f.Coordinator.CaptureOuter(Frame(JudgementSourceResolver.Sites[0]));
        Check(next is not null && next.Lease.Identity.RunId == "next");
    }),
    ("Provider withdrawal skips later publication but releases already captured source and note roots", () => {
        using var f = new CoordinatorFixture(); var outer = f.Coordinator.CaptureOuter(Frame(JudgementSourceResolver.Sites[0]));
        using (var inner = f.Coordinator.CaptureInner(new(22),1,.1f)) {
            f.Coordinator.Dispose(); f.Coordinator.CompleteInner(inner,1);
            Check(f.Published.Count == 0 && f.Backend.RunReleases == 0);
        }
        outer!.Dispose(); Check(f.Backend.RunReleases == 1 && f.Backend.NoteReleases == 1 && f.Backend.SourceReleases == 1 && f.Backend.InnerReleases == 1);
    }),
    ("Hold head and tail retain one object identity without creating a second root", () => {
        using var f = new Fixture(new[] { new ChartNoteReference(100, 2, 3) });
        using var head = f.Get(10, 100); using var tail = f.Get(10, 100, f.Run with { Revision = 8 });
        Check(head.Identity == tail.Identity && f.Roots.Count == 1 && f.Registry.EntryCount == 1);
        Check(tail.ChartLocation == new ChartNoteLocation(2, 3) && tail.GraphAssociation == NoteGraphAssociation.Unique);
    }),
    ("Distinct runtime objects with identical chart references receive distinct runtime identities", () => {
        using var f = new Fixture(new[] { new ChartNoteReference(100, 0, 0) });
        using var one = f.Get(10, 100); using var two = f.Get(11, 100);
        Check(one.Identity != two.Identity && one.Identity.Sequence < two.Identity.Sequence && f.Roots.Count == 2);
    }),
    ("A graph alias is explicitly ambiguous while injected runtime notes have no chart ordinal", () => {
        using var f = new Fixture(new[] { new ChartNoteReference(100, 0, 0), new ChartNoteReference(100, 1, 0) });
        using var alias = f.Get(10, 100); using var injected = f.Get(11, 101);
        Check(alias.GraphAssociation == NoteGraphAssociation.Ambiguous && alias.ChartLocation is null);
        Check(injected.GraphAssociation == NoteGraphAssociation.Missing && injected.ChartLocation is null);
        Check(alias.Identity != injected.Identity);
    }),
    ("Stale session run scene information and controller are rejected before root acquisition", () => {
        using var f = new Fixture();
        var invalid = new[] { f.Run with { SessionId = "other" }, f.Run with { RunId = "other" },
            f.Run with { SceneGeneration = 2 }, f.Run with { Information = 12 }, f.Run with { Controller = 13 },
            f.Run with { Revision = 2 } };
        foreach (var run in invalid) Check(f.Registry.Acquire(run, 10, 100, () => throw new Exception("must not retain")).Error?.Code == ErrorCode.TargetExpired);
        Check(f.Registry.EntryCount == 0 && f.Registry.InFlight == 0);
    }),
    ("Ordinary revisions preserve identity while later stale observations cannot retarget", () => {
        using var f = new Fixture(); using var first = f.Get(10, 100); using var later = f.Get(10, 100, f.Run with { Revision = 100 });
        Check(first.Identity == later.Identity && later.CapturedRun.Revision == 100 && first.CapturedRun.Revision == 3);
        Check(f.Registry.Acquire(f.Run with { Revision = 99 }, 10, 100, () => throw new Exception()).Error?.Code == ErrorCode.TargetExpired);
    }),
    ("Retirement closes admission immediately and waits for the last original invocation", () => {
        using var f = new Fixture(); var first = f.Get(10, 100); var last = f.Get(11, 101);
        f.Registry.Retire("actual run ended inside the original judgement");
        Check(f.Registry.IsRetired && !f.Registry.ResourcesReleased && f.RunRoot.Disposals == 0);
        Check(f.Registry.Acquire(f.Run, 12, 102, () => throw new Exception()).Error?.Code == ErrorCode.ScopeExpired);
        first.Dispose(); Check(f.Roots.All(x => x.Disposals == 0));
        Check(last.CapturedRun.RunId == f.Run.RunId); last.Dispose(); last.Dispose();
        Check(f.Registry.InFlight == 0 && f.Registry.ResourcesReleased && f.Roots.All(x => x.Disposals == 1) && f.RunRoot.Disposals == 1);
        Check(f.Released.SequenceEqual(new[] { "note-11", "note-10", "run" }));
    }),
    ("Retirement during native retention releases the unadmitted root before the retained run", () => {
        using var f = new Fixture(); var root = new Root("capture", f.Released);
        var result = f.Registry.Acquire(f.Run, 10, 100, () => { f.Registry.Retire("withdraw during capture"); Check(!f.Registry.ResourcesReleased); return root; });
        Check(result.Error?.Code == ErrorCode.ScopeExpired && root.Disposals == 1 && f.Registry.ResourcesReleased);
        Check(f.Released.SequenceEqual(new[] { "capture", "run" }) && f.Registry.EntryCount == 0);
    }),
    ("Reentrant acquisition during retention is rejected without admitting a nested entry", () => {
        using var f = new Fixture(); var root = new Root("outer", f.Released); ErrorCode? nested = null;
        var result = f.Registry.Acquire(f.Run, 10, 100, () => {
            nested = f.Registry.Acquire(f.Run, 11, 101, () => throw new Exception("must not retain")).Error?.Code;
            return root;
        });
        Check(result.Success && nested == ErrorCode.Conflict && f.Registry.EntryCount == 1);
        result.Value!.Dispose(); f.Registry.Dispose(); Check(root.Disposals == 1);
    }),
    ("Failed and null native roots admit no identity and allow a later verified retry", () => {
        using var f = new Fixture();
        Check(f.Registry.Acquire(f.Run, 10, 100, () => throw new InvalidOperationException()).Error?.Code == ErrorCode.ObservationUnavailable);
        Check(f.Registry.Acquire(f.Run, 10, 100, () => null!).Error?.Code == ErrorCode.ObservationUnavailable);
        Check(f.Registry.EntryCount == 0 && f.Registry.InFlight == 0);
        using var retry = f.Get(10, 100); Check(retry.Identity.Sequence == 1 && f.Roots.Count == 1);
    }),
    ("A retained note changing its noteData withdraws coverage and keeps active roots until exit", () => {
        using var f = new Fixture(); var active = f.Get(10, 100);
        Check(f.Registry.Acquire(f.Run, 10, 101, () => throw new Exception()).Error?.Code == ErrorCode.ObservationUnavailable);
        Check(f.Registry.IsRetired && !f.Registry.ResourcesReleased && f.Roots.Single().Disposals == 0);
        active.Dispose(); Check(f.Registry.ResourcesReleased && f.Roots.Single().Disposals == 1);
    }),
    ("Capacity exhaustion withdraws the registry instead of producing partially identified observations", () => {
        using var f = new Fixture(capacity: 1); var active = f.Get(10, 100);
        Check(f.Registry.Acquire(f.Run, 11, 101, () => throw new Exception()).Error?.Code == ErrorCode.QuotaExceeded);
        Check(f.Registry.IsRetired && !f.Registry.ResourcesReleased && f.Registry.EntryCount == 1);
        active.Dispose(); Check(f.Registry.ResourcesReleased);
    }),
    ("Nested invocation capacity is bounded without losing already retained identities", () => {
        using var f = new Fixture(); var leases = new List<RuntimeNoteRegistry.InvocationLease>();
        try {
            for (var i = 0; i < RuntimeNoteRegistry.MaximumInFlight; i++) leases.Add(f.Get(10, 100));
            Check(f.Registry.Acquire(f.Run, 10, 100, () => throw new Exception()).Error?.Code == ErrorCode.QuotaExceeded);
            Check(!f.Registry.IsRetired && f.Roots.Count == 1);
        } finally { for (var i = leases.Count - 1; i >= 0; i--) leases[i].Dispose(); }
        Check(f.Registry.InFlight == 0);
    }),
    ("Cleanup faults and callbacks cannot skip later roots or release a root twice", () => {
        using var main = new MainThreadDispatcher(); var released = new List<string>(); RuntimeNoteRegistry? registry = null;
        var runRoot = new Root("run", released, () => throw new Exception("run cleanup"));
        registry = new(main, Fixture.Origin, runRoot, Array.Empty<ChartNoteReference>(), cleanupFailure: _ => { registry!.Dispose(); throw new Exception("log failed"); });
        var first = new Root("first", released, () => { registry.Dispose(); throw new Exception("first cleanup"); });
        var second = new Root("second", released, () => { registry.Dispose(); throw new Exception("second cleanup"); });
        registry.Acquire(Fixture.Origin, 10, 100, () => first).Value!.Dispose(); registry.Acquire(Fixture.Origin, 11, 101, () => second).Value!.Dispose();
        registry.Dispose(); registry.Dispose();
        Check(registry.ResourcesReleased && registry.CleanupFailures == 3 && released.SequenceEqual(new[] { "second", "first", "run" }));
        Check(first.Disposals == 1 && second.Disposals == 1 && runRoot.Disposals == 1);
    }),
    ("Worker access cannot acquire retire or consume a main-thread invocation lease", () => {
        using var f = new Fixture(); var lease = f.Get(10, 100); var failures = 0;
        var worker = new Thread(() => {
            foreach (Action action in new Action[] { () => f.Registry.Acquire(f.Run, 10, 100, () => throw new Exception()),
                () => f.Registry.Dispose(), () => lease.Dispose() }) try { action(); } catch (InvalidOperationException) { failures++; }
        }); worker.Start(); worker.Join();
        Check(failures == 3 && !f.Registry.IsRetired && f.Registry.InFlight == 1);
        lease.Dispose(); f.Registry.Dispose(); Check(f.Registry.InFlight == 0 && f.Registry.ResourcesReleased);
    }),
    ("The same runtime address in a later run is never the previous run identity", () => {
        using var first = new Fixture(); using var second = new Fixture(run: Fixture.Origin with { RunId = "next" });
        using var one = first.Get(10, 100); using var two = second.Get(10, 100);
        Check(one.Identity != two.Identity && one.Identity.Sequence == two.Identity.Sequence);
    }),
    ("Constructor validation and graph traversal failures release the run retention exactly once", () => {
        using var main = new MainThreadDispatcher();
        foreach (var graph in new IEnumerable<ChartNoteReference>[] { new[] { new ChartNoteReference(0, 0, 0) },
            new[] { new ChartNoteReference(100, -1, 0) }, ThrowsDuringGraph(), TooManyReferences() }) {
            var root = new Root("run", new()); var threw = false;
            try { using var registry = new RuntimeNoteRegistry(main, Fixture.Origin, root, graph); } catch { threw = true; }
            Check(threw && root.Disposals == 1);
        }
        foreach (var run in new[] { Fixture.Origin with { RunId = "bad\nrun" }, Fixture.Origin with { Information = 0 }, Fixture.Origin with { Revision = -1 } }) {
            var root = new Root("run", new()); var threw = false;
            try { using var registry = new RuntimeNoteRegistry(main, run, root, Array.Empty<ChartNoteReference>()); } catch (ArgumentException) { threw = true; }
            Check(threw && root.Disposals == 1);
        }
    }),
    ("Null or invalid live references fail before invoking a native retention callback", () => {
        using var f = new Fixture();
        Check(f.Registry.Acquire(f.Run, 0, 100, () => throw new Exception()).Error?.Code == ErrorCode.InvalidArgument);
        Check(f.Registry.Acquire(f.Run, 10, 0, () => throw new Exception()).Error?.Code == ErrorCode.InvalidArgument);
        Check(f.Registry.Acquire(f.Run, 10, 100, null!).Error?.Code == ErrorCode.InvalidArgument);
        Check(f.Registry.EntryCount == 0 && !f.Registry.IsRetired);
    })
};
tests = tests.Concat(HoldContextRegression.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekNoteFlagsChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekJudgeQueuesChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekFingerIdsChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekRendererCommandsChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekTargetStateChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekTargetStateChecks.InitializationTargetCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekTargetStateChecks.TimeTargetCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekTargetStateChecks.HoldModeCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekJudgeInitializationChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekNoteTimeChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.CanvasTargets.NativeSeekCanvasTargetChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekCanvasSourceChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekCanvasPositionChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekCanvasCacheChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.CanvasBasis.NativeSeekCanvasBasisChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineRingNumericChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineRingXNumericChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineEndpointLocalChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineChartOccurrenceChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.TargetProviderCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.EndpointWriterCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.LineBufferCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.RendererConsumerCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.ProducerControlCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekHoldStateChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekHoldGeometryChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.Cases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.StopCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.NativeOwnerCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.SourceGuardCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.AliveGuardCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.DiscoveryGuardCases).ToArray();
tests = tests.Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.SeekEpochCases).ToArray();
// Keep the original regression order and bodies. A fresh process per legacy
// case bounds the native fixture's lifetime without changing its cleanup rules.
var legacyTests = tests.Where(test => !test.Name.StartsWith("Canvas basis ", StringComparison.Ordinal)
    && !test.Name.StartsWith("Hold native owner ", StringComparison.Ordinal)
    && !test.Name.StartsWith("Hold source guard ", StringComparison.Ordinal)
    && !test.Name.StartsWith("Hold alive guard ", StringComparison.Ordinal)
    && !test.Name.StartsWith("Hold discovery guard ", StringComparison.Ordinal)
    && !test.Name.StartsWith("Line ring source ", StringComparison.Ordinal)
    && !test.Name.StartsWith("Line ring numeric ", StringComparison.Ordinal)
    && !test.Name.StartsWith("Line chart occurrence ", StringComparison.Ordinal)).ToArray();
var judgementChartLineTests = RizToolbox.Judgements.Tests.NoteFlags.NativeJudgementChartLineChecks.Cases;
if (args.Length == 1 && args[0] == "--list-legacy-cases")
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(legacyTests.Select((test, index) => new { index = index + 1, name = test.Name })));
    return 0;
}
if (args.Length == 2 && args[0] == "--legacy-case" && int.TryParse(args[1], out var legacyCase)
    && legacyCase >= 1 && legacyCase <= legacyTests.Length)
{
    tests = new[] { legacyTests[legacyCase - 1] };
}
else if (args.Length == 2 && args[0] == "--hold-discovery-guard-case" && int.TryParse(args[1], out var discoveryGuardCase)
    && discoveryGuardCase >= 1 && discoveryGuardCase <= RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.DiscoveryGuardCases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.DiscoveryGuardCases[discoveryGuardCase - 1] };
}
else if (args.Length == 2 && args[0] == "--hold-alive-guard-case" && int.TryParse(args[1], out var aliveGuardCase)
    && aliveGuardCase >= 1 && aliveGuardCase <= RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.AliveGuardCases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.AliveGuardCases[aliveGuardCase - 1] };
}
else if (args.Length == 2 && args[0] == "--hold-source-guard-case" && int.TryParse(args[1], out var sourceGuardCase)
    && sourceGuardCase >= 1 && sourceGuardCase <= RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.SourceGuardCases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.SourceGuardCases[sourceGuardCase - 1] };
}
else if (args.Length == 2 && args[0] == "--hold-native-owner-case" && int.TryParse(args[1], out var nativeOwnerCase)
    && nativeOwnerCase >= 1 && nativeOwnerCase <= RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.NativeOwnerCases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.NativeOwnerCases[nativeOwnerCase - 1] };
}
else if (args.Length == 2 && args[0] == "--hold-observation-case" && int.TryParse(args[1], out var holdObservationCase)
    && holdObservationCase >= 1 && holdObservationCase <= RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.Cases.Length + RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.StopCases.Length)
{
    tests = RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.Cases.Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.StopCases).Skip(holdObservationCase - 1).Take(1).ToArray();
}
else if (args.Length == 2 && args[0] == "--line-producer-control-case" && int.TryParse(args[1], out var producerCase)
    && producerCase >= 1 && producerCase <= RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.ProducerControlCases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.ProducerControlCases[producerCase - 1] };
}
else if (args.Length == 2 && args[0] == "--line-renderer-consumer-case" && int.TryParse(args[1], out var rendererCase)
    && rendererCase >= 1 && rendererCase <= RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.RendererConsumerCases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.RendererConsumerCases[rendererCase - 1] };
}
else if (args.Length == 2 && args[0] == "--line-ring-endpoint-writer-case" && int.TryParse(args[1], out var writerCase)
    && writerCase >= 1 && writerCase <= RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.EndpointWriterCases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.EndpointWriterCases[writerCase - 1] };
}
else if (args.Length == 2 && args[0] == "--line-ring-source-case" && int.TryParse(args[1], out var lineRingCase)
    && lineRingCase >= 1 && lineRingCase <= RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.Cases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.Cases[lineRingCase - 1] };
}
else if (args.Length == 1 && args[0] == "--seek-hold-mode-checks")
{
    tests = RizToolbox.Judgements.Tests.NoteFlags.NativeSeekTargetStateChecks.HoldModeCases;
}
else if (args.Length == 1 && args[0] == "--seek-hold-epoch-checks")
{
    tests = RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.SeekEpochCases;
}
else if (args.Length == 1 && args[0] == "--seek-game-state-checks")
{
    tests = RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.GameStateCases;
}
else if (args.Length == 2 && args[0] == "--seek-game-state-case" && int.TryParse(args[1], out var gameStateCase)
    && gameStateCase >= 1 && gameStateCase <= RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.GameStateCases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.GameStateCases[gameStateCase - 1] };
}
else if (args.Length == 1 && args[0] == "--seek-hold-lifecycle-checks")
{
    tests = RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.Cases
        .Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.StopCases)
        .Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.NativeOwnerCases)
        .Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.SourceGuardCases)
        .Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.AliveGuardCases)
        .Concat(RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.DiscoveryGuardCases).ToArray();
}
else if (args.Length == 2 && args[0] == "--seek-hold-epoch-case" && int.TryParse(args[1], out var seekEpochCase)
    && seekEpochCase >= 1 && seekEpochCase <= RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.SeekEpochCases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.SeekEpochCases[seekEpochCase - 1] };
}
else if (args.Length == 1 && args[0] == "--seek-target-state-checks")
{
    tests = RizToolbox.Judgements.Tests.NoteFlags.NativeSeekTargetStateChecks.Cases
        .Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekTargetStateChecks.InitializationTargetCases)
        .Concat(RizToolbox.Judgements.Tests.NoteFlags.NativeSeekTargetStateChecks.TimeTargetCases).ToArray();
}
else if (args.Length == 2 && args[0] == "--seek-hold-mode-case" && int.TryParse(args[1], out var holdModeCase)
    && holdModeCase >= 1 && holdModeCase <= RizToolbox.Judgements.Tests.NoteFlags.NativeSeekTargetStateChecks.HoldModeCases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.NoteFlags.NativeSeekTargetStateChecks.HoldModeCases[holdModeCase - 1] };
}
else if (args.Length == 2 && args[0] == "--canvas-basis-case" && int.TryParse(args[1], out var basisCase)
    && basisCase >= 1 && basisCase <= RizToolbox.Judgements.Tests.CanvasBasis.NativeSeekCanvasBasisChecks.Cases.Length)
{
    tests = new[] { RizToolbox.Judgements.Tests.CanvasBasis.NativeSeekCanvasBasisChecks.Cases[basisCase - 1] };
}
else if (args.Length == 1 && args[0] == "--judgement-chart-lines-checks")
{
    tests = judgementChartLineTests;
}
else if (args.Length != 0)
{
    if (args.Length == 1 && args[0] == "--hold-native-owner")
    {
        tests = RizToolbox.Judgements.Tests.NoteFlags.HoldLifecycleChecks.NativeOwnerCases;
    }
    else if (args.Length == 1 && args[0] == "--line-producer-control")
    {
        tests = RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.ProducerControlCases;
    }
    else if (args.Length == 1 && args[0] == "--line-renderer-consumer")
    {
        tests = RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.RendererConsumerCases;
    }
    else
    {
    if (args.Length != 1 || args[0] is not ("--canvas-native-source-checks" or "--canvas-position-checks" or "--canvas-cache-checks" or "--canvas-cache-tail-checks" or "--canvas-basis-checks" or "--canvas-basis-conflict-checks" or "--without-canvas-basis-checks" or "--line-ring-numeric-checks" or "--line-ring-ring-x-numeric-checks" or "--line-ring-endpoint-local-checks" or "--line-chart-occurrence-checks" or "--line-ring-target-provider-checks" or "--line-ring-endpoint-writer-checks" or "--line-ring-buffer-checks" or "--line-ring-source-checks" or "--line-ring-source-review-checks" or "--without-line-ring-source-checks"))
    {
        Console.Error.WriteLine("Usage: RizToolbox.Judgements.Tests [--canvas-native-source-checks|--canvas-position-checks|--canvas-cache-checks|--canvas-cache-tail-checks|--canvas-basis-checks|--canvas-basis-conflict-checks|--without-canvas-basis-checks|--canvas-basis-case N|--line-ring-numeric-checks|--line-ring-ring-x-numeric-checks|--line-ring-endpoint-local-checks|--line-chart-occurrence-checks|--line-ring-target-provider-checks|--line-ring-endpoint-writer-checks|--line-ring-endpoint-writer-case N|--line-ring-buffer-checks|--line-ring-source-checks|--line-ring-source-case N|--line-ring-source-review-checks|--without-line-ring-source-checks|--judgement-chart-lines-checks|--list-legacy-cases|--legacy-case N]");
        return 2;
    }
    tests = args[0] switch
    {
        "--canvas-position-checks" => RizToolbox.Judgements.Tests.NoteFlags.NativeSeekCanvasPositionChecks.Cases,
        "--canvas-cache-checks" => RizToolbox.Judgements.Tests.NoteFlags.NativeSeekCanvasCacheChecks.Cases,
        "--canvas-cache-tail-checks" => RizToolbox.Judgements.Tests.NoteFlags.NativeSeekCanvasCacheChecks.Cases.Skip(8).ToArray(),
        "--canvas-basis-checks" => RizToolbox.Judgements.Tests.CanvasBasis.NativeSeekCanvasBasisChecks.Cases,
        "--canvas-basis-conflict-checks" => RizToolbox.Judgements.Tests.CanvasBasis.NativeSeekCanvasBasisChecks.Cases.Skip(9).Take(1).ToArray(),
        "--without-canvas-basis-checks" => legacyTests,
        "--line-ring-numeric-checks" => RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineRingNumericChecks.Cases,
        "--line-ring-ring-x-numeric-checks" => RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineRingXNumericChecks.Cases,
        "--line-ring-endpoint-local-checks" => RizToolbox.Judgements.Tests.LineRingNumeric.NativeSeekLineEndpointLocalChecks.Cases,
        "--line-chart-occurrence-checks" => RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineChartOccurrenceChecks.Cases,
        "--line-ring-target-provider-checks" => RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.TargetProviderCases,
        "--line-ring-endpoint-writer-checks" => RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.EndpointWriterCases,
        "--line-ring-buffer-checks" => RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.LineBufferCases,
        "--line-ring-source-checks" => RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.Cases,
        "--line-ring-source-review-checks" => RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.SourceReviewCases,
        "--without-line-ring-source-checks" => tests.Where(test => !test.Name.StartsWith("Line ring source ", StringComparison.Ordinal)).ToArray(),
        _ => RizToolbox.Judgements.Tests.NoteFlags.NativeSeekCanvasSourceChecks.Cases
    };
    }
}
if (args.Length == 0)
{
    tests = tests.Concat(judgementChartLineTests).ToArray();
}
var failed = 0;
foreach (var test in tests) try { Console.WriteLine("RUN " + test.Name); test.Run(); Console.WriteLine("PASS " + test.Name); }
catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
Console.WriteLine($"Judgement registry checks: {tests.Length - failed}/{tests.Length} passed.");
return failed == 0 ? 0 : 1;
static void Check(bool value) { if (!value) throw new InvalidOperationException("Judgement identity invariant failed."); }
static void MustThrow(Action action) { var failed = false; try { action(); } catch (InvalidOperationException) { failed = true; } Check(failed); }
static NativeJudgeFrame Frame(JudgementCallSite site)
{
    var frame = new NativeJudgeFrame { AbiVersion=1,ByteSize=0x1a0,EntryStackPointer=0x1238,Rsp=0x1238,ReturnAddress=0x10000000+site.ReturnRva,
        Rcx=70,Rbx=50,Rdi=3,R14=60,Rsi=80,Xmm2=new(){Low=unchecked((uint)BitConverter.SingleToInt32Bits(site.Sentinel ?? .0125f))} };
    if (site.NoteSource == JudgementNoteSource.RbxRdi) { frame.Rbx=60; frame.Rdi=50; }
    if (site.NoteSource == JudgementNoteSource.HoldCoroutine) frame.Rdi=50;
    return frame;
}
static IEnumerable<ChartNoteReference> ThrowsDuringGraph() { yield return new(100, 0, 0); throw new InvalidOperationException("traversal failed"); }
static IEnumerable<ChartNoteReference> TooManyReferences() { for (var i = 0; i <= RuntimeNoteRegistry.MaximumNotes; i++) yield return new(i + 1, 0, i); }

sealed class Root : IDisposable {
    private readonly string _name; private readonly List<string> _released; private readonly Action? _cleanup;
    internal int Disposals;
    internal Root(string name, List<string> released, Action? cleanup = null) => (_name, _released, _cleanup) = (name, released, cleanup);
    public void Dispose() { Disposals++; _released.Add(_name); _cleanup?.Invoke(); }
}
sealed class Fixture : IDisposable {
    internal static readonly ObservedJudgementRun Origin = new("session", "run", 1, 3, 21, 22);
    internal readonly MainThreadDispatcher Main = new(); internal readonly List<string> Released = new();
    internal readonly List<Root> Roots = new(); internal readonly Root RunRoot; internal readonly RuntimeNoteRegistry Registry;
    internal readonly ObservedJudgementRun Run;
    internal Fixture(IEnumerable<ChartNoteReference>? graph = null, int capacity = 16384, ObservedJudgementRun? run = null) {
        Run = run ?? Origin; RunRoot = new("run", Released); Registry = new(Main, Run, RunRoot, graph ?? Array.Empty<ChartNoteReference>(), capacity);
    }
    internal RuntimeNoteRegistry.InvocationLease Get(long note, long data, ObservedJudgementRun? run = null) {
        var result = Registry.Acquire(run ?? Run, note, data, () => { var root = new Root("note-" + note, Released); Roots.Add(root); return root; });
        if (!result.Success) throw new InvalidOperationException(result.Error?.ToString()); return result.Value!;
    }
    public void Dispose() { Registry.Dispose(); Main.Dispose(); }
}
sealed class SourceReader : IJudgementNoteSourceReader
{
    internal int TapReads,DragReads,HoldReads,OwnerChecks,NoteChecks;
    public void RequireOwner(long owner) { if (owner != 50) throw new InvalidOperationException(); OwnerChecks++; }
    public void RequireNote(long note) { if (note != 60) throw new InvalidOperationException(); NoteChecks++; }
    public long ReadWaitingNote(long owner,bool drag,int index) { if (owner!=50 || index!=3) throw new InvalidOperationException(); if (drag) DragReads++; else TapReads++; return 60; }
    public long ReadHoldNote(long owner) { if (owner!=50) throw new InvalidOperationException(); HoldReads++; return 60; }
}
sealed class CoordinatorFixture : IDisposable
{
    internal readonly MainThreadDispatcher Main = new();
    internal readonly CoordinatorBackend Backend = new();
    internal readonly List<GameplayJudgementObservation> Published = new();
    internal readonly JudgementCoordinator Coordinator; internal int Depth = 1;
    internal CoordinatorFixture() => Coordinator = new(Backend,Main,new QuietLog(),0x10000000,() => Depth,() => Backend.Current?.RunId,Published.Add);
    public void Dispose() { Coordinator.Dispose(); Main.Dispose(); }
}
sealed class QuietLog : ILogger { public void Log(LogLevel level,string message,Exception? exception = null) { } }
sealed class CoordinatorBackend : IJudgementBackend
{
    internal ObservedJudgementRun? Current = Fixture.Origin;
    internal int RunCaptures,RunReleases,SourceReleases,NoteRetains,NoteReleases,InnerCaptures,InnerReleases,Perfect,MaxPerfect;
    internal bool FailCounters;
    public JudgementRunCapture? CaptureRun() { if(Current is null)return null; RunCaptures++; return new RunValue(this,Current); }
    public ObservedJudgementRun? CurrentRun(JudgementRunCapture run) => Current;
    public JudgementNoteCapture CaptureNote(JudgementRunCapture run,JudgementCallSite site,NativeJudgeFrame frame) => new NoteValue(this);
    public IDisposable RetainNote(long note) { NoteRetains++; return new Root("note",new(),() => NoteReleases++); }
    public JudgementInnerCapture CaptureInner(JudgementRunCapture run,IntPtr receiver) {
        if(receiver.ToInt64()!=run.Run.Controller)throw new InvalidOperationException("wrong controller");
        InnerCaptures++; return new Inner(this);
    }
    private sealed class RunValue : JudgementRunCapture {
        private readonly CoordinatorBackend _owner; internal override ObservedJudgementRun Run { get; }
        internal override IReadOnlyList<ChartNoteReference> Graph => new[] { new ChartNoteReference(100,2,3) };
        internal RunValue(CoordinatorBackend owner,ObservedJudgementRun run) => (_owner,Run)=(owner,run);
        public override void Dispose() => _owner.RunReleases++;
    }
    private sealed class NoteValue : JudgementNoteCapture {
        private readonly CoordinatorBackend _owner; internal NoteValue(CoordinatorBackend owner) => _owner=owner;
        internal override long Note => 60; internal override long Data => 100;
        public override void Dispose() => _owner.SourceReleases++;
    }
    private sealed class Inner : JudgementInnerCapture {
        private readonly CoordinatorBackend _owner; internal Inner(CoordinatorBackend owner) => _owner=owner;
        internal override GameplayJudgementCounters ReadCounters() => _owner.FailCounters
            ? throw new InvalidOperationException("counter read failed") : new(0,0,_owner.Perfect,0,0,_owner.MaxPerfect,0);
        public override void Dispose() => _owner.InnerReleases++;
    }
}
