using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using static Assert;

internal static partial class NativeTweenWorkObservationTests
{
    // The shared owner/graph fixture supplies retained actual OperationContexts.
    // Every row still crosses the Runtime reader, its independent root set and
    // its non-consuming cursor. These fixtures never issue production receipts.
    internal static readonly (string Name, Action Test)[] SourceReplayCases = {
        ("Tween source replay preserves duplicate ready occurrences through transient inline start and exact Execute child", SourceDuplicateOccurrences),
        ("Tween source replay preserves a copied label until actual playing clear and indexed scheduled removals", SourceCopiesAndRemovals),
        ("Tween inline onStart reentrant Add creates a distinct admission while its prior occurrence remains transient", SourceReentrantAdmission),
        ("Tween source replay refuses nested and fabricated contexts while independent Play retains its own native operation", SourceActualContexts),
        ("Tween source replay retains Unknown when the final complete capacity differs beyond logical working count", SourceFinalCapacityMismatch),
        ("Tween source replay retains an unmatched Before after a bad native version or unfinished transient", SourceIncompleteTransaction),
        ("Tween source replay refuses late rooting of an unfamiliar journal pointer", SourceNoLateRoots),
        ("Tween skipped source calls close empty scalar operations without erasing existing work or inventing instant effects", SourceSkippedCalls),
        ("Tween shared manager foreign Execute retains only exact pre-rooted native scalar child history and rejects another parent", SourceForeignExecute),
        ("Tween shared manager drains empty native pops for more than a full history without losing the next owned call", SourceIdleManager),
        ("Tween foreign-only manager verifies scalar execution and refuses a missing source exit", SourceForeignOnlyManager),
        ("Tween manager capture promotes a new owned slot and ignores an unanchored foreign manager", SourceManagerOwnership),
        ("Tween activity on a second manager does not make the first manager's queue local", SourceTwoManagers),
        ("Tween shared manager keeps a foreign queue transfer and updates its full graph on return", SourceSharedTransfer),
        ("Tween shared manager replays ordinary Ready and nested Scheduled appends without local admission", SourceSharedAdds),
        ("Tween shared manager ordinary Add verifies full-capacity growth and respects skipped calls", SourceSharedAddGrowth),
        ("Tween shared manager reports the first slot or version difference at the actual Add or Update boundary", SourceSharedGraphDifference),
        ("Production Tween manager capture admits exact Add and Update frames only", SourceSharedManagerFrames),
        ("Tween shared manager refuses an ordinary Add with unproved generation capacity or other graph changes", SourceSharedGraphRefusals),
        ("Tween shared manager drops clean ordinary skipped calls past its operation bound", SourceSharedSkippedHistory)
    };

    private static void SourceDuplicateOccurrences()
    {
        using var f = new SourceFixture(SourceGraph(ready: new long[] { 101, 101, 0, 0 }, readyCount: 2));
        var update = f.Begin(NativeTweenOperation.Update);
        var initial = f.Owner.ReadSourceReplay().Occurrences.OrderBy(row => row.Locations.Single().Index).ToArray();
        Equal(2, initial.Length); Check(initial[0].Id != initial[1].Id);
        var pop = f.Before(update, NativeTweenSourceSite.ReadyPopFront, 302, 402, source: 1, count: 2, version: 1);
        f.Pair(pop, countAfter: 1, versionAfter: 2, raw: 1, flags: NativeTweenSourceFlags.PopSucceeded);
        var transient = f.Owner.ReadSourceReplay();
        Equal(initial[1].Id, transient.Operations.Single().TransientOccurrences[0]!.Value);
        Check(!transient.Occurrences.Single(row => row.Id == initial[1].Id).QueueExited);

        var start = f.Before(update, NativeTweenSourceSite.UpdateOnStartFront, 105);
        f.PublishBefore(start);
        var inline = f.Owner.ReadSourceReplay().InlineEvents!.Single();
        Equal(initial[1].Id, inline.Occurrence!.Value); Check(!inline.NativeCallReturned && !inline.EffectsEnded);
        f.PublishAfter(start);
        f.Pair(f.Before(update, NativeTweenSourceSite.PlayingInsertFront, 303, 303, destination: 0,
            count: 4, capacity: 4, version: 0, occurrence: pop.Transaction));

        var executeCall = f.Before(update, NativeTweenSourceSite.PlayingExecuteFront, 201, 303, source: 0);
        f.PublishBefore(executeCall);
        // One item still remains in ready. The physical child captures exactly
        // that actual state, including the stale working-count field.
        f.SetGraph(playing: new long[] { 101, 0, 0, 0 }, ready: new long[] { 101, 0, 0, 0 }, readyCount: 1, readyVersion: 2);
        var execute = f.Begin(NativeTweenOperation.ExecuteTween, executeCall.RelayReturnPc.ToInt64() + 5);
        f.Pair(f.Before(execute, NativeTweenSourceSite.ExecuteOnComplete, 106));
        f.End(execute); f.PublishAfter(executeCall);

        var appendPop = f.Before(update, NativeTweenSourceSite.ReadyPopAppend, 302, 402, source: 0, count: 1, version: 2);
        f.Pair(appendPop, countAfter: 0, versionAfter: 3, raw: 1, flags: NativeTweenSourceFlags.PopSucceeded);
        f.Pair(f.Before(update, NativeTweenSourceSite.PlayingInsertAppend, 303, 303, destination: 1,
            count: 4, capacity: 4, version: 0, occurrence: appendPop.Transaction));
        f.SetGraph(playing: new long[] { 101, 101, 0, 0 }, working: 2, ready: new long[4], readyCount: 0, readyVersion: 3);
        f.End(update);
        var state = f.Owner.ReadSourceReplay();
        Equal(0, state.Occurrences.Single(row => row.Id == initial[1].Id).Locations.Single().Index);
        Equal(1, state.Occurrences.Single(row => row.Id == initial[0].Id).Locations.Single().Index);
        Equal(initial[1].Id, state.InlineEvents!.Single(row => row.Site == NativeTweenSourceSite.ExecuteOnComplete).Occurrence!.Value);
        Check(state.Operations.All(row => row.Committed && !row.Production && !row.Failed) && !state.Unknown);
        Check(state.InlineEvents!.All(row => row.NativeCallReturned && !row.EffectsEnded) && !f.Owner.CanRetireWork());
    }

    private static void SourceCopiesAndRemovals()
    {
        using var f = new SourceFixture(SourceGraph(playing: new long[] { 0, 101, 101, 0 }, working: 3,
            scheduled: new long[] { 101, 101, 101, 0 }, scheduledCount: 3));
        var update = f.Begin(NativeTweenOperation.Update);
        var initial = f.Owner.ReadSourceReplay().Occurrences;
        var moved = initial.Single(row => row.Locations.Single() is { Kind: NativeTweenLocationKind.Playing, Index: 2 });
        var scheduled = initial.Where(row => row.Locations.Single().Kind == NativeTweenLocationKind.Scheduled)
            .OrderBy(row => row.Locations.Single().Index).ToArray();
        f.Pair(f.Before(update, NativeTweenSourceSite.PlayingMoveWrite, 303, 303, source: 2, destination: 0,
            count: 4, capacity: 4, version: 0));
        var copied = f.Owner.ReadSourceReplay();
        Check(copied.Operations.Single().PendingMove);
        Check(copied.Occurrences.Single(row => row.Id == moved.Id).Locations.Select(row => row.Index).SequenceEqual(new[] { 0, 2 }));
        f.Pair(f.Before(update, NativeTweenSourceSite.PlayingMoveClear, 303, 303, source: 2, prior: 101,
            count: 4, capacity: 4, version: 0));
        f.Pair(f.Before(update, NativeTweenSourceSite.PlayingTailClear, 303, 303, source: 1, prior: 101,
            count: 4, capacity: 4, version: 0));
        f.Pair(f.Before(update, NativeTweenSourceSite.ScheduledPushTail, 302, 402, source: 2, destination: 0,
            count: 0, version: 1), countAfter: 1, versionAfter: 2);
        f.Pair(f.Before(update, NativeTweenSourceSite.ScheduledSetItem, 301, 401, source: 2, destination: 0,
            prior: 101, count: 3, version: 1), versionAfter: 2);
        f.Pair(f.Before(update, NativeTweenSourceSite.ScheduledRemoveTail, 301, 401, source: 2, prior: 101,
            count: 3, version: 2), countAfter: 2, versionAfter: 3);
        f.Pair(f.Before(update, NativeTweenSourceSite.ScheduledRemoveAdvance, 301, 401, source: 1, prior: 101,
            count: 2, version: 3), countAfter: 1, versionAfter: 4);
        f.Pair(f.Before(update, NativeTweenSourceSite.ScheduledRemoveCurrent, 301, 401, source: 0, prior: 101,
            count: 1, version: 4), countAfter: 0, versionAfter: 5);
        f.SetGraph(playing: new long[] { 101, 0, 0, 0 }, working: 1, scheduled: new long[4], scheduledCount: 0,
            scheduledVersion: 5, ready: new long[] { 101, 0, 0, 0 }, readyCount: 1, readyVersion: 2);
        f.End(update);
        var final = f.Owner.ReadSourceReplay();
        Check(final.Occurrences.Single(row => row.Id == moved.Id).Locations.Single() is { Kind: NativeTweenLocationKind.Playing, Index: 0 });
        Check(final.Occurrences.Single(row => row.Id == scheduled[2].Id).Locations.Single() is { Kind: NativeTweenLocationKind.Ready, Index: 0 });
        Check(final.Occurrences.Where(row => row.Id == scheduled[0].Id || row.Id == scheduled[1].Id).All(row => row.QueueExited && row.ExitReason is not null));
        Check(final.Operations.Single().Committed && !final.Operations.Single().PendingMove && !final.Unknown);
    }

    private static void SourceReentrantAdmission()
    {
        using var f = new SourceFixture(SourceGraph(ready: new long[] { 101, 0, 0, 0 }, readyCount: 1));
        var update = f.Begin(NativeTweenOperation.Update);
        var prior = f.Owner.ReadSourceReplay().Occurrences.Single().Id;
        var pop = f.Before(update, NativeTweenSourceSite.ReadyPopFront, 302, 402, source: 0, count: 1, version: 1);
        f.Pair(pop, countAfter: 0, versionAfter: 2, raw: 1, flags: NativeTweenSourceFlags.PopSucceeded);
        f.SetGraph(ready: new long[4], readyCount: 0, readyVersion: 2);
        var start = f.Before(update, NativeTweenSourceSite.UpdateOnStartFront, 105); f.PublishBefore(start);
        var add = f.Begin(NativeTweenOperation.AddTweenToWaitingList);
        f.SetGraph(scheduled: new long[] { 101, 0, 0, 0 }, scheduledCount: 1, scheduledVersion: 2);
        f.End(add);
        var nested = f.Owner.ReadSourceReplay();
        var admitted = nested.Occurrences.Single(row => row.Admission is not null);
        Check(admitted.Id != prior && nested.Operations.Single().TransientOccurrences[0] == prior);
        f.PublishAfter(start);
        f.Pair(f.Before(update, NativeTweenSourceSite.PlayingInsertFront, 303, 303, destination: 0,
            count: 4, capacity: 4, version: 0, occurrence: pop.Transaction));
        f.SetGraph(playing: new long[] { 101, 0, 0, 0 }, working: 1); f.End(update);
        var final = f.Owner.ReadSourceReplay();
        Check(final.Occurrences.Single(row => row.Id == prior).Locations.Single().Kind == NativeTweenLocationKind.Playing);
        Check(final.Occurrences.Single(row => row.Id == admitted.Id).Locations.Single().Kind == NativeTweenLocationKind.Scheduled);
        Check(final.InlineEvents!.Single().Occurrence == prior && !final.Unknown && !f.Owner.CanRetireWork());
    }

    private static void SourceActualContexts()
    {
        using var f = new SourceFixture(SourceGraph(ready: new long[] { 101, 0, 0, 0 }, readyCount: 1));
        var update = f.Begin(NativeTweenOperation.Update);
        var play = f.Begin(NativeTweenOperation.Play);
        Throws<InvalidOperationException>(() => f.Owner.BeginSourceOperation(update));
        var copied = new NativeLocalTweenWorkOwner.OperationContext(f.Owner, NativeTweenOperation.Play, play.Frame, play.Observation);
        Throws<InvalidOperationException>(() => f.Owner.BeginSourceOperation(copied));
        f.Pair(f.Before(play, NativeTweenSourceSite.PlayOnStart, 105)); f.End(play); f.End(update);
        var independent = f.Begin(NativeTweenOperation.Play);
        var source = f.Before(independent, NativeTweenSourceSite.PlayOnStart, 105);
        Equal(IntPtr.Zero, source.Manager); Equal(0UL, source.ParentUpdateSequence);
        f.Pair(source); f.End(independent);
        var state = f.Owner.ReadSourceReplay();
        Check(!state.Unknown && state.Operations.Count == 3 && state.Operations.Count(row => row.Operation == NativeTweenOperation.Play) == 2);
        Check(state.InlineEvents!.All(row => state.Occurrences.Single(value => value.Id == row.Occurrence).Instant));
        Check(state.Occurrences.Single(row => !row.Instant).Locations.Single().Kind == NativeTweenLocationKind.Ready);
        Throws<InvalidOperationException>(() => new NativeLocalTweenWorkOwner.InlineEventSource(new object(), f.Owner, state.InlineEvents![0]));
    }

    private static void SourceFinalCapacityMismatch()
    {
        using var f = new SourceFixture(SourceGraph(ready: new long[] { 101, 0, 0, 0 }, readyCount: 1));
        var update = f.Begin(NativeTweenOperation.Update);
        var pop = f.Before(update, NativeTweenSourceSite.ReadyPopFront, 302, 402, source: 0, count: 1, version: 1);
        f.Pair(pop, countAfter: 0, versionAfter: 2, raw: 1, flags: NativeTweenSourceFlags.PopSucceeded);
        f.Pair(f.Before(update, NativeTweenSourceSite.PlayingInsertFront, 303, 303, destination: 0,
            count: 4, capacity: 4, version: 0, occurrence: pop.Transaction));
        f.SetGraph(playing: new long[] { 101, 0, 101, 0 }, working: 1, ready: new long[4], readyCount: 0, readyVersion: 2);
        f.Owner.CompleteOperation(update, update.Frame, NativeOperationExitFlags.Returned);
        Check(f.Owner.ReadSourceReplay().Unknown); update.Dispose();
        var state = f.Owner.ReadSourceReplay();
        Check(state.Unknown && state.Operations.Single() is { Failed: true, Committed: false });
        Check(f.Owner.Read().Operations.Single().NativeReturned && f.FreedRoots == 0 && !f.Owner.CanRetireWork());
    }

    private static void SourceIncompleteTransaction()
    {
        using (var f = new SourceFixture(SourceGraph(ready: new long[] { 101, 0, 0, 0 }, readyCount: 1)))
        {
            var update = f.Begin(NativeTweenOperation.Update);
            var pop = f.Before(update, NativeTweenSourceSite.ReadyPopFront, 302, 402, source: 0, count: 1, version: 1);
            f.PublishBefore(pop);
            Throws<InvalidOperationException>(() => f.PublishAfter(pop, countAfter: 0, versionAfter: 1,
                raw: 1, flags: NativeTweenSourceFlags.PopSucceeded));
            var state = f.Owner.ReadSourceReplay();
            Check(state.Unknown && state.Operations.Single() is { Failed: true, PendingPairs: 1, Committed: false });
            Check(state.Occurrences.Single().Locations.Single().Kind == NativeTweenLocationKind.Ready && f.FreedRoots == 0);
        }
        using (var f = new SourceFixture(SourceGraph(ready: new long[] { 101, 0, 0, 0 }, readyCount: 1)))
        {
            var update = f.Begin(NativeTweenOperation.Update);
            f.Pair(f.Before(update, NativeTweenSourceSite.ReadyPopFront, 302, 402, source: 0, count: 1, version: 1),
                countAfter: 0, versionAfter: 2, raw: 1, flags: NativeTweenSourceFlags.PopSucceeded);
            f.SetGraph(ready: new long[4], readyCount: 0, readyVersion: 2);
            f.Owner.CompleteOperation(update, update.Frame, NativeOperationExitFlags.Returned);
            Check(f.Owner.ReadSourceReplay().Unknown); update.Dispose();
            var state = f.Owner.ReadSourceReplay();
            Check(state.Unknown && state.Operations.Single().TransientOccurrences[0] is not null && !state.Occurrences.Single().QueueExited);
        }
    }

    private static void SourceNoLateRoots()
    {
        using var f = new SourceFixture(SourceGraph(ready: new long[] { 101, 0, 0, 0 }, readyCount: 1));
        var update = f.Begin(NativeTweenOperation.Update); var allocated = f.AllocatedRoots;
        var row = f.Before(update, NativeTweenSourceSite.ReadyPopFront, 302, 402, source: 0, count: 1, version: 1);
        row.Tween = new(0x9999);
        Throws<UnsupportedSignatureException>(() => f.PublishBefore(row));
        Check(f.Owner.ReadSourceReplay().Unknown && f.Reader.Unknown && allocated == f.AllocatedRoots && f.FreedRoots == 0);
    }

    private static void SourceSkippedCalls()
    {
        using var f = new SourceFixture(SourceGraph(ready: new long[] { 101, 0, 0, 0 }, readyCount: 1));
        var update = f.Begin(NativeTweenOperation.Update);
        var existing = f.Owner.ReadSourceReplay().Occurrences.Single().Id;
        var play = f.Begin(NativeTweenOperation.Play); f.End(play, NativeOperationExitFlags.Skipped);
        var execute = f.Begin(NativeTweenOperation.ExecuteTween); f.End(execute, NativeOperationExitFlags.Skipped);
        f.End(update);
        var state = f.Owner.ReadSourceReplay();
        Check(!state.Unknown && state.Occurrences.Single().Id == existing && state.Operations.Count(row => row.Skipped) == 2);
        Check(state.Operations.Where(row => row.Skipped).All(row => !row.Committed && !row.Failed && row.Steps.Count == 0));
        Check(state.InlineEvents!.Count == 0 && !f.Owner.CanRetireWork());
    }

    private static void SourceForeignExecute()
    {
        using (var f = new SourceFixture(SourceGraph(playing: new long[] { 101, 111, 0, 0 }, working: 2,
            ready: new long[] { 111, 0, 0, 0 }, readyCount: 1)))
        {
            var update = f.Begin(NativeTweenOperation.Update);
            var call = f.Before(update, NativeTweenSourceSite.PlayingExecuteExisting, 201, 303, source: 1, tween: 111);
            f.PublishBefore(call);
            var child = f.Before(update, NativeTweenSourceSite.ExecuteOnComplete, 115, tween: 111);
            child.Operation = NativeTweenSourceOperation.Execute; child.UpdateSequence = 900;
            child.ParentUpdateSequence = update.Frame.Sequence; child.Depth = 2;
            f.Pair(child); f.PublishAfter(call);
            var pop = f.Before(update, NativeTweenSourceSite.ReadyPopAppend, 302, 402, source: 0, tween: 111, count: 1, version: 1);
            f.Pair(pop, countAfter: 0, versionAfter: 2, raw: 1, flags: NativeTweenSourceFlags.PopSucceeded);
            f.Pair(f.Before(update, NativeTweenSourceSite.UpdateOnStartAppend, 115, tween: 111));
            f.Pair(f.Before(update, NativeTweenSourceSite.PlayingInsertAppend, 303, 303, destination: 2, tween: 111,
                count: 4, capacity: 4, version: 0, occurrence: pop.Transaction));
            f.SetGraph(playing: new long[] { 101, 111, 111, 0 }, working: 3, ready: new long[4], readyCount: 0, readyVersion: 2);
            f.End(update);
            var state = f.Owner.ReadSourceReplay(); var scalar = state.ForeignOperations!.Single();
            Check(!state.Unknown && state.InlineEvents!.Count == 0 && scalar is { NativeSequence: 900, NativeCallReturned: true, PendingPairs: 0 });
            Check(scalar.ParentSequence == update.Frame.Sequence && scalar.Tween == 111 && scalar.Steps.Count == 1);
            Check(state.Operations.Single().Operation == NativeTweenOperation.Update && state.Operations.Single().Committed);
            Check(state.Occurrences.Single(row => row.Id == scalar.Occurrence).Locations.Single().Index == 1 && !f.Owner.CanRetireWork());
        }
        using (var f = new SourceFixture(SourceGraph(playing: new long[] { 101, 111, 0, 0 }, working: 2)))
        {
            var update = f.Begin(NativeTweenOperation.Update);
            f.PublishBefore(f.Before(update, NativeTweenSourceSite.PlayingExecuteExisting, 201, 303, source: 1, tween: 111));
            var wrong = f.Before(update, NativeTweenSourceSite.ExecuteOnComplete, 115, tween: 111);
            wrong.Operation = NativeTweenSourceOperation.Execute; wrong.UpdateSequence = 900;
            wrong.ParentUpdateSequence = update.Frame.Sequence + 50; wrong.Depth = 2;
            Throws<InvalidOperationException>(() => f.PublishBefore(wrong));
            Check(f.Owner.ReadSourceReplay().Unknown && f.FreedRoots == 0);
        }
    }

    private static void SourceIdleManager()
    {
        using var f = new SourceFixture(SourceGraph());
        for (var i = 0; i < 1050; i++) f.DispatchEmptyUpdate();
        var replay = f.Owner.ReadSourceReplay();
        Check(!replay.Unknown && replay.LastSequence == 2100 && replay.Operations.Count == 0
            && replay.Occurrences.Count == 0 && f.Owner.Read().Operations.Count == 0);
        Equal((2100, 0, 0), f.BoundaryReads);
        var play = f.Begin(NativeTweenOperation.Play); f.End(play);
        Check(play.Observation > 1050 && f.Owner.ReadSourceReplay().Operations.Single().Committed);
    }

    private static void SourceForeignOnlyManager()
    {
        using (var f = new SourceFixture(SourceGraph(playing: new long[] { 111, 0, 0, 0 }, working: 1)))
        {
            var update = f.Begin(NativeTweenOperation.Update);
            var call = f.Before(update, NativeTweenSourceSite.PlayingExecuteExisting, 201, 303, source: 0, tween: 111);
            f.PublishBefore(call);
            var child = f.Before(update, NativeTweenSourceSite.ExecuteOnComplete, 115, tween: 111);
            child.Operation = NativeTweenSourceOperation.Execute; child.UpdateSequence = 900;
            child.ParentUpdateSequence = update.Frame.Sequence; child.Depth = 2;
            f.Pair(child); f.PublishAfter(call); f.End(update);
            var replay = f.Owner.ReadSourceReplay();
            Check(!replay.Unknown && replay.LastSequence == 4 && replay.Operations.Count == 0
                && replay.ForeignOperations!.Count == 0 && replay.InlineEvents!.Count == 0);
            Equal(111L, replay.Occurrences.Single().Tween);
        }
        using (var f = new SourceFixture(SourceGraph()))
        {
            f.DispatchEmptyUpdate(closePair: false);
            var replay = f.Owner.ReadSourceReplay();
            Check(replay.Unknown && replay.Operations.Single() is { Failed: true, PendingPairs: 1 }
                && f.FreedRoots == 0);
        }
        using (var f = new SourceFixture(SourceGraph(playing: new long[] { 111, 0, 0, 0 }, working: 1)))
        {
            // A discarded child sequence must never be accepted as a new physical
            // Execute. Keep receiving the new parent's real, ordered native rows.
            for (var i = 0; i < 2; i++)
            {
                var update = f.Begin(NativeTweenOperation.Update);
                var call = f.Before(update, NativeTweenSourceSite.PlayingExecuteExisting, 201, 303, source: 0, tween: 111);
                f.PublishBefore(call);
                var child = f.Before(update, NativeTweenSourceSite.ExecuteOnComplete, 115, tween: 111);
                child.Operation = NativeTweenSourceOperation.Execute; child.UpdateSequence = 900;
                child.ParentUpdateSequence = update.Frame.Sequence; child.Depth = 2;
                if (i == 0)
                {
                    f.Pair(child); f.PublishAfter(call); f.End(update);
                    Check(!f.Owner.Unknown);
                }
                else
                {
                    Throws<InvalidOperationException>(() => f.PublishBefore(child));
                    Check(f.Owner.Unknown && f.FreedRoots == 0);
                }
            }
        }
    }

    private static void SourceManagerOwnership()
    {
        using (var f = new SourceFixture(SourceGraph()))
        {
            f.SetGraph(playing: new long[] { 101, 0, 0, 0 }, working: 1);
            var update = f.Begin(NativeTweenOperation.Update);
            f.Pair(f.Before(update, NativeTweenSourceSite.PlayingExecuteExisting, 201, 303, source: 0));
            f.End(update);
            var replay = f.Owner.ReadSourceReplay();
            Check(!replay.Unknown && replay.Operations.Single().Committed && replay.Occurrences.Single().Tween == 101);
            Equal((1, 1, 1), f.BoundaryReads);
        }
        using (var f = new SourceFixture(SourceGraph(), managerAnchor: false))
        {
            f.DispatchEmptyUpdate();
            var replay = f.Owner.ReadSourceReplay();
            Check(!replay.Unknown && replay.LastSequence == 2 && replay.Operations.Count == 0);
            Equal((0, 0, 0), f.BoundaryReads);
        }
    }

    private static void SourceTwoManagers()
    {
        var empty = SourceGraph();
        var second = SourceGraph(playing: new long[] { 101, 0, 0, 0 }, working: 1).Managers.Single();
        second = second with { Object = 211, Playing = second.Playing! with { Object = 313, Array = 313 },
            Scheduled = second.Scheduled! with { Object = 311, Array = 411 }, Ready = second.Ready! with { Object = 312, Array = 412 } };
        using var f = new SourceFixture(empty with { Managers = new[] { empty.Managers.Single(), second } });
        f.DispatchEmptyUpdate();
        var replay = f.Owner.ReadSourceReplay();
        Check(!replay.Unknown && replay.LastSequence == 2 && replay.Operations.Count == 0);
        Equal((2, 0, 0), f.BoundaryReads);
        var update = f.Begin(NativeTweenOperation.Update, manager: 211);
        f.Pair(f.Before(update, NativeTweenSourceSite.PlayingExecuteExisting, 211, 313, source: 0));
        f.End(update);
        var secondReplay = f.Owner.ReadSourceReplay();
        Check(!secondReplay.Unknown && secondReplay.LastSequence == 4 && secondReplay.Operations.Single().Committed
            && secondReplay.Occurrences.Single().Locations.Single().Manager == 211);
        Equal((2, 1, 1), f.BoundaryReads);
    }

    private static void SourceSharedTransfer()
    {
        using var f = new SourceFixture(SourceGraph(ready: new long[] { 111, 0, 0, 0 }, readyCount: 1));
        var update = f.Begin(NativeTweenOperation.Update);
        var pop = f.Before(update, NativeTweenSourceSite.ReadyPopAppend, 302, 402, source: 0, tween: 111, count: 1, version: 1);
        f.Pair(pop, countAfter: 0, versionAfter: 2, raw: 1, flags: NativeTweenSourceFlags.PopSucceeded);
        f.Pair(f.Before(update, NativeTweenSourceSite.PlayingInsertAppend, 303, 303, destination: 0, tween: 111,
            count: 4, capacity: 4, version: 0, occurrence: pop.Transaction));
        f.SetGraph(playing: new long[] { 111, 0, 0, 0 }, working: 1, ready: new long[4], readyCount: 0, readyVersion: 2);
        f.End(update);
        var replay = f.Owner.ReadSourceReplay();
        var manager = f.Owner.Read().Current!.Managers.Single();
        Check(!replay.Unknown && replay.Operations.Single().Committed && replay.Operations.Single().Steps.Count == 2);
        Check(replay.Occurrences.Single().Locations.Single().Kind == NativeTweenLocationKind.Playing
            && manager.Ready!.Count == 0 && manager.Playing!.CapacityItems[0] == 111);
        Equal((2, 0, 1), f.BoundaryReads);
    }

    private static void SourceSharedAdds()
    {
        using var f = new SourceFixture(SourceGraph());
        f.DispatchEmptyUpdate();
        f.CallOrdinaryAdd();
        Equal(0, f.Owner.ReadSourceReplay().Occurrences.Count);
        f.CallOrdinaryAdd(() => f.SetGraph(ready: new long[] { 111, 0, 0, 0 }, readyCount: 1, readyVersion: 2));

        var update = f.Begin(NativeTweenOperation.Update);
        Equal<string?>(null, f.Owner.Read().Failure);
        var ready = f.Owner.ReadSourceReplay().Occurrences.Single();
        Check(ready is { Tween: 111, Admission: null } && ready.Locations.Single() is { Kind: NativeTweenLocationKind.Ready, Index: 0 });
        var pop = f.Before(update, NativeTweenSourceSite.ReadyPopAppend, 302, 402, source: 0, tween: 111, count: 1, version: 2);
        f.Pair(pop, countAfter: 0, versionAfter: 3, raw: 1, flags: NativeTweenSourceFlags.PopSucceeded);
        f.SetGraph(ready: new long[4], readyCount: 0, readyVersion: 3);
        var start = f.Before(update, NativeTweenSourceSite.UpdateOnStartAppend, 115, tween: 111);
        f.PublishBefore(start);
        f.CallOrdinaryAdd(() => f.SetGraph(scheduled: new long[] { 111, 0, 0, 0 }, scheduledCount: 1, scheduledVersion: 2));
        f.PublishAfter(start);
        f.Pair(f.Before(update, NativeTweenSourceSite.PlayingInsertAppend, 303, 303, destination: 0, tween: 111,
            count: 4, version: 0, occurrence: pop.Transaction));
        f.SetGraph(playing: new long[] { 111, 0, 0, 0 }, working: 1);
        f.End(update);

        var replay = f.Owner.ReadSourceReplay();
        var state = f.Owner.Read();
        Check(!replay.Unknown && replay.Operations.Single() is { Committed: true, Failed: false });
        Check(replay.Occurrences.Single(row => row.Id == ready.Id).Locations.Single() is { Kind: NativeTweenLocationKind.Playing, Index: 0 });
        var scheduled = replay.Occurrences.Single(row => row.Id != ready.Id);
        Check(scheduled is { Tween: 111, Admission: null } && scheduled.Locations.Single() is { Kind: NativeTweenLocationKind.Scheduled, Index: 0 });
        Check(!f.Owner.OwnsTweenSource(111) && state.Sources == 1 && state.Admissions.Count == 0);
        Check(replay.InlineEvents!.Count == 0 && replay.TypedEvents!.Count == 0 && state.Operations.Single().Operation == NativeTweenOperation.Update);
        Equal((10, 0, 1), f.BoundaryReads);
    }

    private static void SourceSharedAddGrowth()
    {
        using var f = new SourceFixture(SourceGraph(scheduled: new long[] { 101, 111, 111, 111 }, scheduledCount: 4));
        f.End(f.Begin(NativeTweenOperation.Update));
        var prior = f.Owner.ReadSourceReplay().Occurrences.OrderBy(row => row.Locations.Single().Index).ToArray();
        f.CallOrdinaryAdd(() => throw new InvalidOperationException("A skipped Add must not run its original."), skip: true);
        Equal(4, f.Owner.ReadSourceReplay().Occurrences.Count);
        f.CallOrdinaryAdd(() => f.SetGraph(scheduled: new long[] { 101, 111, 111, 111, 111, 0, 0, 0 },
            scheduledCount: 5, scheduledVersion: 2, scheduledArray: 403));
        f.End(f.Begin(NativeTweenOperation.Update));

        var replay = f.Owner.ReadSourceReplay();
        Equal<string?>(null, f.Owner.Read().Failure);
        var current = replay.Occurrences.OrderBy(row => row.Locations.Single().Index).ToArray();
        Check(prior.Select(row => row.Id).SequenceEqual(current.Take(4).Select(row => row.Id)));
        Check(current[4] is { Tween: 111, Admission: null } && current[4].Locations.Single() is { Kind: NativeTweenLocationKind.Scheduled, Index: 4 });
        Check(!f.Owner.OwnsTweenSource(111) && f.Owner.Read().Admissions.Count == 0 && !replay.Unknown);
        Equal((3, 2, 2), f.BoundaryReads);
    }

    private static void SourceSharedGraphDifference()
    {
        using (var f = new SourceFixture(SourceGraph()))
        {
            f.DispatchEmptyUpdate();
            f.CallOrdinaryAdd(() => f.SetGraph(scheduled: new long[] { 111, 0, 0, 101 }, scheduledCount: 1, scheduledVersion: 2));
            var state = f.Owner.Read();
            Check(state.Unknown && state.Failure!.Contains("AddTweenToWaitingList", StringComparison.Ordinal));
            Check(state.Failure!.Contains("Scheduled.Slot[3]", StringComparison.Ordinal));
            Check(f.Owner.ReadSourceReplay().Occurrences.Count == 0 && state.Admissions.Count == 0 && f.FreedRoots == 0);
        }
        using (var f = new SourceFixture(SourceGraph()))
        {
            f.DispatchEmptyUpdate();
            f.SetGraph(readyVersion: 2);
            var update = f.Begin(NativeTweenOperation.Update);
            var state = f.Owner.Read();
            Check(state.Unknown && state.Failure!.Contains($"Update sequence {update.Frame.Sequence}", StringComparison.Ordinal));
            Check(state.Failure!.Contains("Ready.Version expected 1, actual 2", StringComparison.Ordinal));
            f.End(update);
        }
    }

    private static void SourceSharedManagerFrames()
    {
        var managerClass = new IntPtr(901);
        var update = new VerifiedHookTarget("update", new(2001), new(700), managerClass, ExactHookBroker.OperationAbi(NativeOperationKind.Void0));
        var add = new VerifiedHookTarget("add", new(2002), new(701), managerClass, ExactHookBroker.OperationAbi(NativeOperationKind.Reference1));
        var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Void0,
            Self = new(201), Caller = new(999), Method = update.MethodInfo, Sequence = 1 };
        NativeLocalTweenBackend.RequireSharedManagerFrame(NativeTweenOperation.Update, frame, update, managerClass, true);
        frame.Kind = NativeOperationKind.Reference1; frame.Arg0 = new(111); frame.Method = add.MethodInfo;
        NativeLocalTweenBackend.RequireSharedManagerFrame(NativeTweenOperation.AddTweenToWaitingList, frame, add, managerClass, true);
        Throws<InvalidOperationException>(() => NativeLocalTweenBackend.RequireSharedManagerFrame(NativeTweenOperation.ExecuteTween, frame, add, managerClass, true));
        Throws<InvalidOperationException>(() => NativeLocalTweenBackend.RequireSharedManagerFrame(NativeTweenOperation.Update, frame, update, managerClass, true));
        Throws<InvalidOperationException>(() => NativeLocalTweenBackend.RequireSharedManagerFrame(NativeTweenOperation.AddTweenToWaitingList, frame, add, managerClass, false));
        Throws<InvalidOperationException>(() => NativeLocalTweenBackend.RequireSharedManagerFrame(NativeTweenOperation.AddTweenToWaitingList, frame, add, new(903), true));
        frame.Method = update.MethodInfo;
        Throws<InvalidOperationException>(() => NativeLocalTweenBackend.RequireSharedManagerFrame(NativeTweenOperation.AddTweenToWaitingList, frame, add, managerClass, true));
        frame.Method = IntPtr.Zero;
        NativeLocalTweenBackend.RequireSharedManagerFrame(NativeTweenOperation.AddTweenToWaitingList, frame, add, managerClass, true);
        frame.Arg1 = new(112);
        Throws<InvalidOperationException>(() => NativeLocalTweenBackend.RequireSharedManagerFrame(NativeTweenOperation.AddTweenToWaitingList, frame, add, managerClass, true));
        frame.Arg1 = IntPtr.Zero; frame.Arg0 = IntPtr.Zero;
        Throws<InvalidOperationException>(() => NativeLocalTweenBackend.RequireSharedManagerFrame(NativeTweenOperation.AddTweenToWaitingList, frame, add, managerClass, true));
    }

    private static void SourceSharedGraphRefusals()
    {
        using (var f = new SourceFixture(SourceGraph()))
        {
            f.DispatchEmptyUpdate();
            f.CallOrdinaryAdd(() => f.SetGraph(ready: new long[5]));
            RequireFailure(f, "Ready.Capacity expected 4, actual 5");
        }
        using (var f = new SourceFixture(SourceGraph()))
        {
            f.DispatchEmptyUpdate();
            f.CallOrdinaryAdd(() => {
                f.SetGraph(ready: new long[] { 111, 0, 0, 0 }, readyCount: 1, readyVersion: 2);
                f.ChangeManager(manager => manager with { TrackSize = manager.TrackSize + 1 });
            });
            RequireFailure(f, "Manager.TrackSize");
        }
        using (var f = new SourceFixture(SourceGraph()))
        {
            f.DispatchEmptyUpdate();
            f.ChangeManager(manager => manager with { Generation = manager.Generation + 1 });
            f.CallOrdinaryAdd();
            RequireFailure(f, "Manager.Generation");
        }
        using (var f = new SourceFixture(SourceGraph(ready: new long[] { 111, 0, 0, 0 }, readyCount: 1)))
        {
            f.End(f.Begin(NativeTweenOperation.Update));
            f.CallOrdinaryAdd(() => f.SetGraph(ready: new long[] { 101, 111, 0, 0 }, readyCount: 2, readyVersion: 2));
            RequireFailure(f, "Ready.Slot[0]", expectedOccurrences: 1);
        }
        using (var f = new SourceFixture(SourceGraph()))
        {
            f.DispatchEmptyUpdate();
            f.SetGraph(ready: new long[5]);
            var update = f.Begin(NativeTweenOperation.Update);
            Check(f.Owner.Read().Failure?.Contains("Ready.Capacity expected 4, actual 5", StringComparison.Ordinal) == true);
            f.End(update);
        }

        static void RequireFailure(SourceFixture f, string difference, int expectedOccurrences = 0)
        {
            var state = f.Owner.Read();
            Check(state.Unknown && state.Failure!.Contains("AddTweenToWaitingList", StringComparison.Ordinal)
                && state.Failure.Contains(difference, StringComparison.Ordinal));
            Check(f.Owner.ReadSourceReplay().Occurrences.Count == expectedOccurrences && state.Admissions.Count == 0 && f.FreedRoots == 0);
        }
    }

    private static void SourceSharedSkippedHistory()
    {
        using var f = new SourceFixture(SourceGraph());
        f.DispatchEmptyUpdate();
        for (var index = 0; index < 1100; ++index)
            f.CallOrdinaryAdd(() => throw new InvalidOperationException("A skipped Add must not execute."), skip: true);
        f.CallOrdinaryAdd(() => f.SetGraph(ready: new long[] { 111, 0, 0, 0 }, readyCount: 1, readyVersion: 2));
        Equal<string?>(null, f.Owner.Read().Failure);
        Check(f.Owner.ReadSourceReplay().Occurrences.Single() is { Tween: 111, Admission: null });
        Check(f.Owner.Read().Operations.Count == 0 && f.Owner.Read().Admissions.Count == 0);
    }

    private static NativeTweenWorkGraph SourceGraph(long[]? playing = null, int working = 0,
        long[]? scheduled = null, int scheduledCount = 0, long[]? ready = null, int readyCount = 0)
    {
        var graph = Graph(); var manager = graph.Managers.Single();
        return graph with { Managers = new[] { manager with { WorkingCount = working,
            Playing = manager.Playing! with { Count = working, CapacityItems = playing ?? new long[4] },
            Scheduled = manager.Scheduled! with { Count = scheduledCount, CapacityItems = scheduled ?? new long[4] },
            Ready = manager.Ready! with { Count = readyCount, CapacityItems = ready ?? new long[4] } } } };
    }

    private sealed class SourceFixture : IDisposable
    {
        private readonly Fixture _owner = new();
        private readonly SourceTransport _native = new();
        private readonly Dictionary<uint, IntPtr> _roots = new(); private uint _next; private ulong _transaction;
        internal readonly NativeTweenSource Reader;
        internal NativeLocalTweenWorkOwner Owner => _owner.Owner;
        internal int AllocatedRoots, FreedRoots;
        internal (int Manager, int FullBefore, int FullAfter) BoundaryReads
            => (_owner.Backend.SharedManagerReads, _owner.Backend.BeforeReads, _owner.Backend.AfterReads);
        internal SourceFixture(NativeTweenWorkGraph graph, bool managerAnchor = true)
        {
            _owner.Backend.Graph = graph; Owner.Tick();
            Reader = NativeTweenSource.CreateControlledForTests(_native, _owner.Main.VerifyAccess, Root, (root, _) => root.Target);
            var references = new List<(long, NativeTweenSourceObjectKind)> {
                (201L, NativeTweenSourceObjectKind.Manager), (101L, NativeTweenSourceObjectKind.Tween),
                (303L, NativeTweenSourceObjectKind.TweenArray), (401L, NativeTweenSourceObjectKind.TweenArray), (402L, NativeTweenSourceObjectKind.TweenArray),
                (301L, NativeTweenSourceObjectKind.ScheduledList), (302L, NativeTweenSourceObjectKind.ReadyStack),
                (105L, NativeTweenSourceObjectKind.UnityEvent), (106L, NativeTweenSourceObjectKind.UnityEvent), (107L, NativeTweenSourceObjectKind.SingleUnityEvent),
                (111L, NativeTweenSourceObjectKind.Tween), (115L, NativeTweenSourceObjectKind.UnityEvent)
            };
            foreach (var manager in graph.Managers.Where(row => row.Object != 201))
            {
                references.Add((manager.Object, NativeTweenSourceObjectKind.Manager));
                references.Add((manager.Playing!.Array, NativeTweenSourceObjectKind.TweenArray));
                references.Add((manager.Scheduled!.Object, NativeTweenSourceObjectKind.ScheduledList));
                references.Add((manager.Scheduled.Array, NativeTweenSourceObjectKind.TweenArray));
                references.Add((manager.Ready!.Object, NativeTweenSourceObjectKind.ReadyStack));
                references.Add((manager.Ready.Array, NativeTweenSourceObjectKind.TweenArray));
            }
            var retained = Reader.RetainRootedObjects(Owner, references.Select(row =>
                new NativeTweenSourceRootReference(Root(new(row.Item1)), row.Item2, OwnershipAnchor:
                    row.Item1 == 101 || row.Item2 == NativeTweenSourceObjectKind.Manager && managerAnchor)).ToArray());
            Owner.AttachSourceJournal(Reader, retained);
        }
        internal void DispatchEmptyUpdate(bool closePair = true)
        {
            _owner.Backend.CallUpdate(frame =>
            {
                var pop = new NativeTweenSourceRecord {
                    Version = 1, Size = 192, Site = NativeTweenSourceSite.ReadyPopFront, Stage = NativeTweenSourceStage.Before,
                    Flags = NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched,
                    ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Depth = 1, Epoch = 7,
                    Operation = NativeTweenSourceOperation.Update, UpdateSequence = frame.Sequence,
                    Transaction = ++_transaction, Occurrence = _transaction, SourcePc = new(0x100000), RelayReturnPc = new(0x200000),
                    Manager = frame.Self, Container = new(302), ArrayBefore = new(402), ArrayAfter = new(402),
                    SourceIndex = -1, DestinationIndex = -1, CountBefore = 0, CountAfter = 0,
                    CapacityBefore = 4, CapacityAfter = 4, VersionBefore = 1, VersionAfter = 1 };
                _native.Add(pop);
                if (!closePair) return;
                pop.Stage = NativeTweenSourceStage.After; pop.Flags |= NativeTweenSourceFlags.Paired;
                pop.RelayReturnPc = new(0x200008); _native.Add(pop);
            });
            if (!Owner.Unknown) Owner.PollSourceJournal();
        }
        internal NativeLocalTweenWorkOwner.OperationContext Begin(NativeTweenOperation operation, long? caller = null, long? manager = null)
        {
            var frame = _owner.Backend.Frame(operation); if (caller is { } address) frame.Caller = new(address);
            if (manager is { } receiver) frame.Self = new(receiver);
            var context = Owner.CaptureOperation(operation, frame) ?? throw new InvalidOperationException("Fixture operation was not actually retained.");
            return context;
        }
        internal void End(NativeLocalTweenWorkOwner.OperationContext context, NativeOperationExitFlags flags = NativeOperationExitFlags.Returned)
        {
            Owner.CompleteOperation(context, context.Frame, flags);
            context.Dispose();
        }
        internal void CallOrdinaryAdd(Action? original = null, bool skip = false)
            => _owner.Backend.Call(NativeTweenOperation.AddTweenToWaitingList, original, argument: 111, skip: skip);
        internal void ChangeManager(Func<NativeTweenManagerObservation, NativeTweenManagerObservation> change)
        {
            var graph = _owner.Backend.Graph;
            _owner.Backend.Graph = graph with { Managers = new[] { change(graph.Managers.Single()) } };
        }
        internal NativeTweenSourceRecord Before(NativeLocalTweenWorkOwner.OperationContext context, NativeTweenSourceSite site,
            long container, long array = 0, int source = -1, int destination = -1, long tween = 101, long prior = 0,
            int count = -1, int capacity = 4, int version = -1, ulong? occurrence = null)
        {
            static bool IsSource(NativeTweenOperation operation) => operation is NativeTweenOperation.Update or NativeTweenOperation.Play or NativeTweenOperation.ExecuteTween;
            var parents = new List<NativeLocalTweenWorkOwner.OperationContext>();
            for (var parent = context.Parent; parent is not null; parent = parent.Parent) if (IsSource(parent.Operation)) parents.Add(parent);
            var manager = context.Operation == NativeTweenOperation.Play
                ? parents.FirstOrDefault(row => row.Operation != NativeTweenOperation.Play)?.Frame.Self ?? IntPtr.Zero : context.Frame.Self;
            var transaction = ++_transaction; var snapshot = (uint)site <= 12;
            return new() { Version = 1, Size = 192, Site = site, Stage = NativeTweenSourceStage.Before,
                Flags = NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Depth = (uint)parents.Count + 1,
                Operation = context.Operation == NativeTweenOperation.Update ? NativeTweenSourceOperation.Update
                    : context.Operation == NativeTweenOperation.Play ? NativeTweenSourceOperation.Play : NativeTweenSourceOperation.Execute,
                Epoch = 7, UpdateSequence = context.Frame.Sequence, ParentUpdateSequence = parents.FirstOrDefault()?.Frame.Sequence ?? 0,
                Transaction = transaction, Occurrence = occurrence ?? transaction,
                SourcePc = new(0x100000 + (long)site * 32), RelayReturnPc = new(0x200000 + (long)site * 32),
                Manager = manager, Container = new(container), ArrayBefore = new(array), ArrayAfter = new(array), Tween = new(tween), Prior = new(prior),
                SourceIndex = source, DestinationIndex = destination, CountBefore = count, CountAfter = count,
                CapacityBefore = snapshot ? capacity : -1, CapacityAfter = snapshot ? capacity : -1, VersionBefore = version, VersionAfter = version };
        }
        internal void PublishBefore(NativeTweenSourceRecord before) { _native.Add(before); Owner.PollSourceJournal(); }
        internal void PublishAfter(NativeTweenSourceRecord before, int? countAfter = null, int? versionAfter = null,
            ulong raw = 0, NativeTweenSourceFlags flags = 0)
        {
            var after = before; after.Stage = NativeTweenSourceStage.After; after.Flags |= NativeTweenSourceFlags.Paired | flags;
            after.RelayReturnPc = new(before.RelayReturnPc.ToInt64() + 8); after.CountAfter = countAfter ?? before.CountBefore;
            after.VersionAfter = versionAfter ?? before.VersionBefore; after.RawResult = raw; _native.Add(after); Owner.PollSourceJournal();
        }
        internal void Pair(NativeTweenSourceRecord before, int? countAfter = null, int? versionAfter = null,
            ulong raw = 0, NativeTweenSourceFlags flags = 0)
        { PublishBefore(before); PublishAfter(before, countAfter, versionAfter, raw, flags); }
        internal void SetGraph(long[]? playing = null, int? working = null, long[]? scheduled = null, int? scheduledCount = null,
            int? scheduledVersion = null, long[]? ready = null, int? readyCount = null, int? readyVersion = null, long? scheduledArray = null)
        {
            var graph = _owner.Backend.Graph; var manager = graph.Managers.Single();
            _owner.Backend.Graph = graph with { Managers = new[] { manager with { WorkingCount = working ?? manager.WorkingCount,
                Playing = manager.Playing! with { Count = working ?? manager.Playing!.Count, CapacityItems = playing ?? manager.Playing!.CapacityItems },
                Scheduled = manager.Scheduled! with { Array = scheduledArray ?? manager.Scheduled!.Array,
                    Count = scheduledCount ?? manager.Scheduled!.Count, Version = scheduledVersion ?? manager.Scheduled!.Version,
                    CapacityItems = scheduled ?? manager.Scheduled!.CapacityItems },
                Ready = manager.Ready! with { Count = readyCount ?? manager.Ready!.Count, Version = readyVersion ?? manager.Ready!.Version,
                    CapacityItems = ready ?? manager.Ready!.CapacityItems } } } };
        }
        private NativeRuntime.RootHandle Root(IntPtr pointer)
        {
            _owner.Main.VerifyAccess(); var key = ++_next; _roots.Add(key, pointer); ++AllocatedRoots;
            return new(key, id => { _roots.Remove(id); ++FreedRoots; }, id => _roots.GetValueOrDefault(id), _owner.Main.VerifyAccess);
        }
        public void Dispose() { _owner.Dispose(); /* No fabricated retirement releases independently retained roots. */ }
    }
    private sealed class SourceTransport : INativeTweenSourceTransport
    {
        private NativeTweenSourceInformation _info = new() { Version = 1, Size = 96, Configured = 1, MetadataBound = 1,
            BindingCount = 43, JournalCapacity = 8192, Epoch = 7 };
        private readonly Dictionary<ulong, NativeTweenSourceRecord> _records = new();
        private readonly HashSet<ulong> _open = new();
        internal void Add(NativeTweenSourceRecord record)
        {
            record.EventSequence = ++_info.Events; _records.Add(record.EventSequence, record); _info.RetainedRecords = _info.Events;
            _info.Transactions = Math.Max(_info.Transactions, record.Transaction);
            if (record.Stage == NativeTweenSourceStage.Before) _open.Add(record.Transaction);
            else if (record.Stage == NativeTweenSourceStage.After) { _open.Remove(record.Transaction); ++_info.CompletedTransactions; }
            _info.PendingTransactions = (ulong)_open.Count;
        }
        public uint GetInfo(ref NativeTweenSourceInformation information) { information = _info; return 0; }
        public uint ReadRecord(ulong epoch, ulong eventSequence, ref NativeTweenSourceRecord record)
            => _records.TryGetValue(eventSequence, out record) && epoch == _info.Epoch ? 0u : 3u;
    }
}
