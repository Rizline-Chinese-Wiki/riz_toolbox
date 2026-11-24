using RizToolbox.Runtime.Hooks;
using RizToolbox.Runtime;
using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class HoldLifecycleChecks
{
    internal static readonly (string Name, Action Run)[] SeekEpochCases =
    {
        ("Hold Seek executes registered Stop target Start Stop and exact predecessor inverse", SeekEpochNormal),
        ("Hold Seek pre-target check failure keeps the proven stopped predecessor recoverable", SeekEpochBeforeTargetFailure),
        ("Hold Seek rejects the second factory before dispatch and restores known partial targets", SeekEpochPartialTargetFailure),
        ("Hold Seek a dispatched abnormal target Start retains its exact unknown roots", SeekEpochAbnormalStart),
        ("Hold Seek first Start metadata rejection restores time and unscheduled factory output", SeekEpochBeforeStartFailure),
        ("Hold Seek post-store read failure retains unconfirmed native field responsibility", SeekEpochPostStoreFailure),
        ("Hold Seek partial target recovery refuses a changed actual counter before inverse dispatch", SeekEpochPartialChanged),
        ("Hold Seek factory output withdrawal keeps its exact unconfirmed publication owner", SeekEpochFactoryWithdrawal),
        ("Hold Seek original Judge queues accept only the same epoch count and restore exact storage", SeekEpochJudgeQueues),
        ("Hold Seek failed complete target readback never becomes a known partial scheduling recovery", SeekEpochFinalReadFailure)
    };
    private static void RestoreEpoch(EpochFixture f)
    {
        try
        {
            f.Epoch.StopTargetHolds(); f.Drained();
            Check(f.Native.JudgeHoldValue == 0, "The actual known target Stop did not reset its owned count.");
            f.Epoch.RestorePredecessors(); f.Drained();
            Check(f.Epoch.VerifyRestored() && !f.Epoch.SideEffectsUnknown && f.Native.JudgeHoldValue == f.Predecessors.Length
                && f.Native.TargetFloat(f.Native.JudgeOwner, "_nowTime") == .5f
                && f.Predecessors.All(iterator => f.Native.TargetInt(iterator, "<>1__state") == 2),
                "The actual original epoch did not independently restore count, time and paused iterator states.");
        }
        catch (Exception error) { Console.Error.WriteLine("ACTUAL EPOCH RECOVERY FAILURE: " + error); f.Retained = f.Epoch.SideEffectsUnknown; throw; }
    }
    private static void SeekEpochNormal()
    {
        foreach (var pair in new[] { (Auto: true, Free: false), (Auto: false, Free: true) })
        {
            using var f = new EpochFixture(pair.Auto, pair.Free);
            f.Epoch.StopPredecessors(); f.Epoch.VerifyPredecessorsStopped(); f.Drained();
            Check(f.Native.SeekHoldStops == 1 && f.Native.JudgeHoldValue == 0, "The actual original Stop was not observed once.");
            try { f.Epoch.StartTargetHolds(); f.Drained(); }
            catch (Exception error) { Console.Error.WriteLine("ACTUAL TARGET START FAILURE: " + error); f.Retained = true; throw; }
            Check(f.Epoch.VerifyTarget() && !f.Epoch.SideEffectsUnknown && f.Native.SeekHoldFactories == 1
                && f.Native.SeekHoldStarts == 1 && f.Native.JudgeHoldValue == 1 && f.Native.TargetFloat(f.Native.JudgeOwner, "_nowTime") == 1.25f
                && !f.Epoch.EngineCancellationProven, "The controlled original did not reach its recognized target continuation.");
            try { RestoreEpoch(f); }
            catch (Exception error) { Console.Error.WriteLine("ACTUAL ORIGINAL RESTORE FAILURE: " + error); f.Retained = true; throw; }
            Check(f.Native.SeekHoldStops == 2 && f.Native.SeekHoldStarts == 2 && f.Start.Originals == 3 && f.Move.Originals == 3,
                "The epoch repeated an original or borrowed a fabricated Start/Move completion.");
        }
    }
    private static void SeekEpochBeforeTargetFailure()
    {
        using var f = new EpochFixture(); f.Epoch.StopPredecessors(); f.Drained();
        var failure = new IOException("Controlled read failed before target native dispatch.");
        f.Profile.OnVerify = () => throw failure;
        var actual = Expect<IOException>(f.Epoch.StartTargetHolds); f.Profile.OnVerify = null;
        f.Retained = f.Epoch.SideEffectsUnknown;
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { scenario = "pre-target-check", unknown = f.Epoch.SideEffectsUnknown,
            factoryDispatches = f.Native.SeekHoldFactories, startDispatches = f.Native.SeekHoldStarts,
            stopDispatches = f.Native.SeekHoldStops, actualCount = f.Native.JudgeHoldValue,
            actualJudgeTime = f.Native.TargetFloat(f.Native.JudgeOwner, "_nowTime"), holdInFlight = f.Backend.HoldLifecycle.Snapshot().InFlight }));
        Check(ReferenceEquals(actual, failure) && !f.Epoch.SideEffectsUnknown && f.Native.SeekHoldFactories == 0
            && f.Native.SeekHoldStarts == 0 && f.Native.SeekHoldStops == 1 && f.Native.JudgeHoldValue == 0
            && f.Native.TargetFloat(f.Native.JudgeOwner, "_nowTime") == .5f,
            "A pre-dispatch check failure poisoned the already proven predecessor Stop.");
        RejectTargetRetry(f);
        RestoreEpoch(f);
    }
    private static void SeekEpochPartialTargetFailure()
    {
        using var f = new EpochFixture(twoHolds: true); f.Epoch.StopPredecessors(); f.Drained();
        var failure = new IOException("Controlled second factory metadata rejected before dispatch.");
        f.Native.BeforeSeekHoldFactoryMetadata = () => { if (f.Native.SeekHoldFactories == 1) throw failure; };
        var actual = Expect<IOException>(f.Epoch.StartTargetHolds); f.Native.BeforeSeekHoldFactoryMetadata = null;
        f.Retained = f.Epoch.SideEffectsUnknown;
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { scenario = "partial-target-pre-dispatch", unknown = f.Epoch.SideEffectsUnknown,
            factoryDispatches = f.Native.SeekHoldFactories, startDispatches = f.Native.SeekHoldStarts,
            stopDispatches = f.Native.SeekHoldStops, actualCount = f.Native.JudgeHoldValue,
            holdInFlight = f.Backend.HoldLifecycle.Snapshot().InFlight, startInFlight = f.Start.InFlight, moveInFlight = f.Move.InFlight }));
        Check(ReferenceEquals(actual, failure) && !f.Epoch.SideEffectsUnknown && f.Native.SeekHoldFactories == 1
            && f.Native.SeekHoldStarts == 1 && f.Native.JudgeHoldValue == 1
            && f.Backend.HoldLifecycle.Snapshot().InFlight == 0,
            "A second factory pre-dispatch failure discarded the first actual known target identity.");
        RejectTargetRetry(f);
        f.Drained(); RestoreEpoch(f);
        Check(f.Native.SeekHoldStops == 2 && f.Native.SeekHoldStarts == 3 && f.Start.Originals == 5 && f.Move.Originals == 5,
            "Partial target recovery retried its rejected factory or failed to restart both original iterators once.");
    }
    private static void SeekEpochAbnormalStart()
    {
        using var f = new EpochFixture(); f.Epoch.StopPredecessors();
        f.Start.Original = frame =>
        {
            f.Move.Invoke(new NativeCoroutineFrame { Version = 1, Size = 0x30, Kind = NativeCoroutineKind.MoveNext,
                Self = frame.Enumerator, Method = f.MoveTarget.MethodInfo, Caller = new(0x12346) });
            throw new IOException("Controlled actual Start failed after its synchronous Move.");
        };
        Expect<InvalidOperationException>(f.Epoch.StartTargetHolds);
        Check(f.Epoch.SideEffectsUnknown && f.Backend.HoldLifecycle.Snapshot().Unknown && f.Native.SeekHoldFactories == 1
            && f.Native.SeekHoldStarts == 1 && f.Start.InFlight == 0 && f.Move.InFlight == 0 && f.Native.JudgeHoldValue == 1,
            "An abnormal dispatched Start was relabeled recoverable after its physical exit.");
        var stops = f.Native.SeekHoldStops; var starts = f.Native.SeekHoldStarts; var writes = f.Native.SeekHoldWrites.Count;
        Expect<InvalidOperationException>(f.Epoch.StopTargetHolds); Expect<InvalidOperationException>(f.Epoch.RestorePredecessors);
        Check(f.Native.SeekHoldStops == stops && f.Native.SeekHoldStarts == starts && f.Native.SeekHoldWrites.Count == writes,
            "Unknown target responsibility permitted an inverse setter or rescheduling attempt.");
        f.Retained = true;
    }
    private static void RejectTargetRetry(EpochFixture f)
    {
        var factories = f.Native.SeekHoldFactories; var starts = f.Native.SeekHoldStarts; var writes = f.Native.SeekHoldWrites.Count;
        Expect<InvalidOperationException>(f.Epoch.StartTargetHolds);
        Check(!f.Epoch.SideEffectsUnknown && f.Native.SeekHoldFactories == factories && f.Native.SeekHoldStarts == starts
            && f.Native.SeekHoldWrites.Count == writes,
            "A rejected target attempt replayed construction or revoked its legitimate recovery owner.");
    }
    private static void SeekEpochBeforeStartFailure()
    {
        using var f = new EpochFixture(); f.Epoch.StopPredecessors();
        var failure = new IOException("Controlled first Start metadata rejected before scheduling.");
        f.Native.BeforeSeekHoldStartMetadata = () => throw failure;
        var actual = Expect<IOException>(f.Epoch.StartTargetHolds); f.Native.BeforeSeekHoldStartMetadata = null;
        f.Retained = f.Epoch.SideEffectsUnknown;
        Check(ReferenceEquals(actual, failure) && !f.Epoch.SideEffectsUnknown && f.Native.SeekHoldFactories == 1
            && f.Native.SeekHoldStarts == 0 && f.Native.JudgeHoldValue == 0
            && f.Native.TargetFloat(f.Native.JudgeOwner, "_nowTime") == 1.25f
            && f.Backend.HoldLifecycle.Snapshot().Records == f.Predecessors.Length,
            "A factory with no dispatched Start lost its unscheduled output or blocked owned time recovery.");
        RejectTargetRetry(f); RestoreEpoch(f);
        Check(f.Native.SeekHoldFactories == 1 && f.Native.SeekHoldStarts == 1 && f.Native.SeekHoldStops == 1,
            "Recovery retried the unscheduled target or dispatched an unnecessary second Stop.");
    }
    private static void SeekEpochPostStoreFailure()
    {
        using var f = new EpochFixture(); f.Epoch.StopPredecessors();
        var failure = new IOException("Controlled immediate readback after the actual Judge-time store failed.");
        f.Native.AfterSeekHoldStore = (name, _) =>
        {
            if (name == "_nowTime") f.Native.AfterTargetFieldRead = (field, _) => { if (field == "_nowTime") throw failure; };
        };
        var actual = Expect<IOException>(f.Epoch.StartTargetHolds);
        f.Native.AfterSeekHoldStore = null; f.Native.AfterTargetFieldRead = null;
        Check(ReferenceEquals(actual, failure) && f.Epoch.SideEffectsUnknown && f.Native.SeekHoldFactories == 0
            && f.Native.SeekHoldStarts == 0 && f.Native.TargetFloat(f.Native.JudgeOwner, "_nowTime") == 1.25f,
            "An unconfirmed native field write was treated as a recoverable pre-dispatch rejection.");
        var writes = f.Native.SeekHoldWrites.Count; Expect<InvalidOperationException>(f.Epoch.StopTargetHolds);
        Check(f.Native.SeekHoldWrites.Count == writes && f.Native.SeekHoldStops == 1,
            "An unconfirmed target time permitted an inverse dispatch."); f.Retained = true;
    }
    private static void SeekEpochPartialChanged()
    {
        using var f = new EpochFixture(twoHolds: true); f.Epoch.StopPredecessors();
        f.Native.BeforeSeekHoldFactoryMetadata = () => { if (f.Native.SeekHoldFactories == 1) throw new IOException("Controlled second factory rejection."); };
        Expect<IOException>(f.Epoch.StartTargetHolds); f.Native.BeforeSeekHoldFactoryMetadata = null;
        Check(!f.Epoch.SideEffectsUnknown && f.Native.JudgeHoldValue == 1, "The known partial target was unavailable before external mutation.");
        f.Native.TargetSet(f.Native.JudgeOwner, "holdOnJudgeCount", 2);
        var writes = f.Native.SeekHoldWrites.Count; var stops = f.Native.SeekHoldStops; var starts = f.Native.SeekHoldStarts;
        Expect<InvalidOperationException>(f.Epoch.StopTargetHolds);
        Check(f.Epoch.SideEffectsUnknown && f.Native.JudgeHoldValue == 2 && f.Native.SeekHoldWrites.Count == writes
            && f.Native.SeekHoldStops == stops && f.Native.SeekHoldStarts == starts,
            "Recovery rebased a foreign count or stopped a changed target without independent complete readback."); f.Retained = true;
    }
    private static void SeekEpochFactoryWithdrawal()
    {
        using var f = new EpochFixture(); f.Epoch.StopPredecessors();
        var publications = NativePublicationCall.UnconfirmedPublicationCount;
        var retained = NativePublicationCall.RetainedCallCount;
        var temporary = NativePublicationCall.RetainedRootRecordCount;
        f.Native.AfterSeekHoldFactory = _ => StateWithdrawal(f);
        Expect<InvalidOperationException>(f.Epoch.StartTargetHolds);
        Check(f.Epoch.SideEffectsUnknown && f.Native.SeekHoldFactories == 1 && f.Native.SeekHoldStarts == 0
            && NativePublicationCall.UnconfirmedPublicationCount == publications + 1
            && NativePublicationCall.RetainedCallCount == retained + 1 && NativePublicationCall.RetainedRootRecordCount == temporary,
            "A returned factory token disappeared before its publication owner could retain the withdrawn output.");
        var starts = f.Native.SeekHoldStarts; var stops = f.Native.SeekHoldStops;
        Expect<InvalidOperationException>(f.Epoch.RestorePredecessors);
        Check(f.Native.SeekHoldStarts == starts && f.Native.SeekHoldStops == stops,
            "A withdrawn actual run permitted target or predecessor dispatch."); f.Retained = true;
    }
    private static void StateWithdrawal(EpochFixture f)
    {
        f.State.Scene("Play-other");
        f.State.Controller(f.Native.GameplayInformation.ToInt64(), true, false, "controlled-next-epoch", f.Native.GameplayController.ToInt64());
    }
    private static void SeekEpochJudgeQueues()
    {
        using var f = new EpochFixture(); var n = f.Native;
        var continuation = f.Epoch.RequiresContinuation.ToHashSet(); var projected = f.TargetPlan.QueueTargets;
        var target = new SeekJudgeQueueTargets(projected.Notes.Where(row => !continuation.Contains(row)).ToArray(),
            projected.TapWaiting.Where(row => !continuation.Contains(row)).ToArray(),
            projected.DragWaiting.Where(row => !continuation.Contains(row)).ToArray());
        var original = n.JudgeQueueSnapshot();
        using var queue = f.Run.PrepareSeekJudgeQueues(n.TargetProfile, target);
        queue.BindHoldEpoch(f.Epoch); queue.VerifyPrepared();
        f.Epoch.StopPredecessors(); f.Drained();
        queue.VerifyPrepared(); queue.Commit();
        Check(queue.Confirm().Observed == true && n.JudgeHoldValue == 0
            && n.JudgeQueueActive()[0].SequenceEqual(target.Notes) && n.JudgeQueueActive()[1].SequenceEqual(target.TapWaiting)
            && n.JudgeQueueActive()[2].SequenceEqual(target.DragWaiting),
            "The original prepared queues rejected the actual owner Stop or adopted another target order.");
        f.Epoch.StartTargetHolds(); f.Drained();
        Check(f.Epoch.VerifyTarget() && queue.Confirm().Observed == true && n.JudgeHoldValue == 1,
            "The original queue handoff did not independently accept its same epoch's target Start count.");
        f.Epoch.StopTargetHolds(); f.Drained(); queue.Restore();
        Check(queue.ConfirmRestored().OriginalStateRestored && n.JudgeQueueStorageEquals(original) && n.JudgeHoldValue == 0,
            "The game-state queue inverse did not restore exact original backing, tail slots, size and signed version after target Stop.");
        f.Epoch.RestorePredecessors(); f.Drained();
        Check(f.Epoch.VerifyRestored() && queue.ConfirmRestored().OriginalStateRestored && n.JudgeHoldValue == 1
            && n.JudgeQueueStorageEquals(original), "The restored original Hold counter rebased unrelated restored Judge storage.");
        queue.Dispose(); f.VerifiedQueueWrites = n.JudgeQueueWrites.Count;
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { scenario = "same-epoch-judge-queues",
            countPhases = new[] { 1, 0, 1, 0, 1 }, originalStorageRestored = n.JudgeQueueStorageEquals(original),
            actualQueueWrites = n.JudgeQueueWrites.Count, factories = n.SeekHoldFactories, startDispatches = n.SeekHoldStarts,
            stopDispatches = n.SeekHoldStops, originalStartCalls = f.Start.Originals, originalMoveCalls = f.Move.Originals,
            actualEpochRestored = f.Epoch.VerifyRestored(), callbackInFlight = f.Backend.HoldLifecycle.Snapshot().InFlight,
            fixedBodyVerified = false, completeSeekExecuted = false }));
    }
    private static void SeekEpochFinalReadFailure()
    {
        using var f = new EpochFixture(); f.Epoch.StopPredecessors();
        var failure = new IOException("Controlled final complete target counter read failed.");
        f.Native.AfterTargetFieldRead = (name, _) =>
        {
            if (name == "holdOnJudgeCount" && (bool)EpochFixture.Get(f.Epoch, "_targetStarted")) throw failure;
        };
        var actual = Expect<IOException>(f.Epoch.StartTargetHolds); f.Native.AfterTargetFieldRead = null;
        Check(ReferenceEquals(actual, failure) && f.Epoch.SideEffectsUnknown && f.Native.SeekHoldFactories == 1
            && f.Native.SeekHoldStarts == 1 && f.Native.JudgeHoldValue == 1 && f.Start.InFlight == 0 && f.Move.InFlight == 0,
            "An unconfirmed full target readback was relabeled a known partial dispatch.");
        var stops = f.Native.SeekHoldStops; Expect<InvalidOperationException>(f.Epoch.StopTargetHolds);
        Check(f.Native.SeekHoldStops == stops, "Failed complete target confirmation permitted a recovery Stop."); f.Retained = true;
    }
}
