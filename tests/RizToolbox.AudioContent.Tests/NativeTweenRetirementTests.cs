using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using System.Reflection;
using static Assert;

// A/B/C/D/E's actual managed state machines, exact private leases, journal
// replay, concrete death and actual RootHandle.Dispose. Native cut transport
// and graph publication are controlled, not game/helper integration evidence.
internal static class NativeTweenRetirementTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Tween retirement completes distinct duplicate exits commits actual managed native receipts and frees each holder once", CompleteTransaction),
        ("Tween retirement resumes a permanently withdrawn source after no-mutation native busy on the next pump", BusyThenRetry),
        ("Tween retirement cannot close enrollment while one same-pointer queue occurrence remains", WorkStillOwned),
        ("Tween retirement preserves all roots when a native batch throws or returns an unknown result", CommitFailure),
        ("Tween retirement rejects a malformed Retired tuple without freeing a holder or retrying native commit", BadRetiredTuple),
        ("Tween retirement marks each free attempt before calling it and never retries a failed native handle free", FreeFailure),
        ("Tween retirement withholds final authority when actual native cut release fails after frees", CutReleaseFailure),
        ("Tween retirement rejects production legacy read reentry from every actual source root Free before backend release", SourceFreeReentry),
        ("Tween retirement rejects production legacy read reentry from every actual admission root Free before backend release", AdmissionFreeReentry),
        ("Tween retirement drains only its frozen cutoff when unrelated source rows arrive during observation withdrawal", WithdrawalForeignRows),
        ("Tween retirement rejects owned source work arriving between completion preparation and source freeze", WithdrawalOwnedRows),
        ("Tween retirement rejects scheduling or concrete target changes during withdrawal even without new journal rows", WithdrawalOwnedGraph),
        ("Tween retirement reclaims actual work owner capacity across more than thirty two completed runs", RepeatedRuns),
        ("Tween retirement retains typed effects and concrete live targets before any native or root mutation", UnendedEffects),
        ("Tween retirement issues EmptySingleListenerEffectsReceipt and retires only when drained empty graph and writer stability hold", EmptySingleEventRetires),
        ("Tween retirement withholds EmptySingleListenerEffectsReceipt on missing Mid, graph/writer change, dirty flags, foreign child, active transport, abnormal exit, or setter", EmptySingleEventGatesRejectRetirement),
        ("Tween retirement keeps non-empty Site 0/1/2 Single listeners concrete Pending with rooted metadata even after Invoke return, Dispose, and listener removal", NonEmptySingleListenersRemainPendingAcrossSites),
        ("Tween retirement BusyThenRetry rejects late Single P4 invalidations between Busy and retry", EmptySingleEventBusyThenRetryInvalidations),
        ("Tween retirement writer ledger tracks onTween and m_Calls mutations per event without registering onTween as a Cutout event", OnTweenWriterLedgerTracksMutations),
        ("Tween retirement rejects Skipped Play and Skipped ExecuteTween parents that executed child TypedEventInvocation while allowing clean Skipped controls", SkippedParentWithTypedInvocationRejectsRetirement),
        ("Tween retirement allows valid non-empty Site 1 Play transfer across BusyThenRetry while rejecting null EmptyReceipt", Site1PlayTransferSurvivesBusyThenRetry)
    };
    private static void CompleteTransaction()
    {
        using var f = Ready(); var allocated = f.Backend.RootManifest.Count;
        var releaseObserved = false;
        f.Backend.CutNative.BeforeRelease = () => {
            Check(OwnerState(f.Owner, "_retirementReceipt") is null && !f.Owner.CanRetireWork()
                && f.Backend.RootManifest.All(row => row.Freed) && RegisteredOwners().Contains(f.Owner));
            releaseObserved = true;
        };
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.CutNative.Commits == 1 && f.Backend.CutNative.Acquires == 1
            && f.Backend.CutNative.Releases == 1 && f.Backend.Native.Current.Phase == NativeTweenAdmissionPhase.Retired
            && f.Backend.TweenBackendRootsReleased && f.Roots.Retired && f.Backend.RootRecords == 0 && f.Backend.Frees == allocated
            && releaseObserved && OwnerState(f.Owner, "_retirementReceipt") is not null && !RegisteredOwners().Contains(f.Owner));
        Check(f.Backend.RootManifest.All(row => row.FreeAttempts == 1 && row.Freed));
        var frees = f.Backend.Frees; var commits = f.Backend.CutNative.Commits;
        f.Owner.PumpControlledTweenRetirementForTests(); f.Owner.Tick();
        _ = f.Owner.Read(); _ = f.Owner.ReadSourceReplay(); _ = f.Owner.ReadWorkCompletion();
        Check(f.Owner.CanRetireWork() && f.Backend.Frees == frees && f.Backend.CutNative.Commits == commits);
    }
    private static void BusyThenRetry()
    {
        using var f = Ready(); f.Backend.CutNative.Busy = true;
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && !f.Roots.EnrollmentOpen && !f.Roots.Retired
            && f.Backend.ObservationBindingsClosed && f.Backend.CutNative.Acquires == 1 && f.Backend.CutNative.Commits == 0 && f.Backend.Frees == 0);
        var transaction = OwnerState(f.Owner, "_retirementTransaction");
        var frozen = OwnerState(f.Owner, "_preparedCompletion"); var withdrawal = f.Roots.Withdrawal;
        Check(transaction is not null && frozen is not null && withdrawal is not null && !f.Owner.RetiredRootsUnavailable);
        f.RefuseJournalReads(); f.Backend.DeadTween(13); f.Backend.DeadTargets(13); f.Owner.Tick();
        Check(!f.Owner.Unknown); f.Backend.CutNative.Busy = false;
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.CutNative.Acquires == 2
            && f.Backend.CutNative.Commits == 1 && f.Backend.RootRecords == 0 && f.Backend.RootManifest.All(row => row.FreeAttempts == 1)
            && ReferenceEquals(transaction, OwnerState(f.Owner, "_retirementTransaction"))
            && ReferenceEquals(frozen, OwnerState(f.Owner, "_preparedCompletion")) && ReferenceEquals(withdrawal, f.Roots.Withdrawal));
    }
    private static void WorkStillOwned()
    {
        using var f = new TweenCompletionFixture(playing: new long[] { 101, 101, 0, 0 }, working: 2);
        var update = f.Begin(NativeTweenOperation.Update); f.Clear(update, 1); f.EndUpdate(update, new long[] { 101, 0, 0, 0 }, 1);
        f.ConfirmDeaths(); f.Owner.PumpControlledTweenRetirementForTests();
        Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Roots.EnrollmentOpen && !f.Backend.ObservationBindingsClosed
            && f.Backend.CutNative.Acquires == 0 && f.Backend.Frees == 0);
    }
    private static void CommitFailure()
    {
        foreach (var mode in new[] { "throw", "status" })
        {
            using var f = Ready(); f.Backend.CutNative.CommitFailure = mode;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && f.Owner.Unknown && f.Backend.CutNative.Commits == 1 && f.Backend.Frees == 0
                && f.Backend.Native.Current.Phase == NativeTweenAdmissionPhase.Sealed);
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(f.Backend.CutNative.Commits == 1 && f.Backend.CutNative.Acquires == 1 && f.Backend.Frees == 0);
        }
    }
    private static void BadRetiredTuple()
    {
        using var f = Ready(); f.Backend.CutNative.CommitFailure = "tuple";
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(!f.Owner.CanRetireWork() && f.Owner.Unknown && f.Backend.CutNative.Commits == 1 && f.Backend.Frees == 0
            && f.Backend.Native.Current.Phase == NativeTweenAdmissionPhase.Retired);
        f.Owner.PumpControlledTweenRetirementForTests(); Check(f.Backend.CutNative.Commits == 1 && f.Backend.Frees == 0);
    }
    private static void FreeFailure()
    {
        using var f = Ready(); f.Backend.FailBackendFreeAt = 2;
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.Unknown && !f.Owner.CanRetireWork() && f.Backend.CutNative.Commits == 1 && f.Backend.BackendReleaseAttempted
            && !f.Backend.TweenBackendRootsReleased && f.Backend.RootManifest.Count(row => row.Holder == "Backend" && row.FreeAttempts == 1) == 2);
        var attempts = f.Backend.RootManifest.Sum(row => row.FreeAttempts);
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Backend.RootManifest.Sum(row => row.FreeAttempts) == attempts && f.Backend.CutNative.Commits == 1);
    }
    private static void CutReleaseFailure()
    {
        using var f = Ready(); f.Backend.CutNative.ReleaseFailure = true;
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.Unknown && !f.Owner.CanRetireWork() && f.Backend.CutNative.Releases == 1 && f.Backend.RootManifest.All(row => row.Freed)
            && OwnerState(f.Owner, "_retirementReceipt") is null && RegisteredOwners().Contains(f.Owner));
        var frees = f.Backend.Frees; f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Backend.Frees == frees && f.Backend.CutNative.Acquires == 1);
    }
    private static void SourceFreeReentry() => LegacyReadReentry("Source");
    private static void AdmissionFreeReentry() => LegacyReadReentry("Admission");
    private static void WithdrawalForeignRows()
    {
        using var f = Ready(); var previous = f.Owner.ReadSourceReplay().LastSequence; var beyondCutoff = false;
        f.Backend.DuringObservationWithdrawal = () => {
            f.PublishForeignPair();
            f.AfterNextJournalInformationRead(() => { f.PublishForeignPair(); beyondCutoff = true; });
        };
        f.Backend.BeforeRootFree = _ => f.RefuseJournalReads();
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && beyondCutoff && f.Roots.Withdrawal!.JournalCutoff == previous + 2
            && f.Owner.ReadSourceReplay().LastSequence == previous + 2 && f.Owner.ReadWorkCompletion().JournalCutoff == previous + 2
            && f.Backend.CutNative.LastRequest.JournalCutoff == previous + 2
            && f.Backend.CutNative.Commits == 1 && f.Backend.RootManifest.All(row => row.Freed && row.FreeAttempts == 1));
    }
    private static void WithdrawalOwnedRows()
    {
        using var f = Ready(); f.Backend.DuringObservationWithdrawal = f.PublishUnobservedOwnedPair;
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.Unknown && !f.Owner.CanRetireWork() && !f.Roots.EnrollmentOpen && !f.Roots.Retired
            && f.Backend.CutNative.Acquires == 0 && f.Backend.CutNative.Commits == 0 && f.Backend.Frees == 0
            && OwnerState(f.Owner, "_retirementReceipt") is null && !f.Owner.RetiredRootsUnavailable);
    }
    private static void WithdrawalOwnedGraph()
    {
        foreach (var mutation in new[] { "queue", "target" })
        {
            using var f = Ready();
            f.Backend.DuringObservationWithdrawal = () => {
                if (mutation == "queue") f.Backend.SetPlaying(new long[] { 101, 0, 0, 0 }, 1);
                else f.Backend.Targets[503] = f.Backend.Targets[503] with { Frame = 13, Alive = true, CachedNative = 1503 };
            };
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(f.Owner.Unknown && !f.Owner.CanRetireWork() && !f.Roots.EnrollmentOpen && !f.Roots.Retired
                && f.Backend.CutNative.Acquires == 0 && f.Backend.CutNative.Commits == 0 && f.Backend.Frees == 0
                && OwnerState(f.Owner, "_retirementReceipt") is null && !f.Owner.RetiredRootsUnavailable);
        }
    }
    private static void LegacyReadReentry(string holder)
    {
        using var f = Ready();
        // Only the actual production reader entry points are exercised here.
        // Null dependencies ensure a late API/launch/listener read cannot pass
        // as the retirement rejection. The exact owner is the sole seeded field.
        var production = new NativeLocalTweenBackend(null!, f.Main, null!, IntPtr.Zero, null!, null!, null!);
        typeof(NativeLocalTweenBackend).GetField("_owner", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(production, f.Owner);
        var binding = new NativeTweenTargetBinding(f.Backend.Tween, null, null, null, 0);
        var calls = 0;
        f.Backend.BeforeRootFree = record => {
            Check(f.Owner.RetiredRootsUnavailable && f.Backend.CutNative.Commits == 1
                && f.Backend.Native.Current.Phase == NativeTweenAdmissionPhase.Retired);
            f.Backend.RefuseReads = f.Backend.Native.RefuseReads = f.Backend.RefuseRootReads = true;
            if (record.Holder != holder) return;
            Check(!f.Backend.BackendReleaseAttempted && OwnerState(f.Owner, "_retirementReceipt") is null);
            RejectBeforeNativeRead(() => production.Refresh());
            RejectBeforeNativeRead(() => production.ReadActivity());
            RejectBeforeNativeRead(() => production.VerifyTweenDeathOwner(f.Owner));
            RejectBeforeNativeRead(() => production.ReadTweenDeathObservation(f.Backend.Tween));
            RejectBeforeNativeRead(() => production.BorrowTweenDeathSources(f.Owner, (_, _) => throw new Exception("A withdrawn source was borrowed.")));
            RejectBeforeNativeRead(() => production.BorrowTweenTargetBinding(f.Owner, f.Backend.Tween));
            RejectBeforeNativeRead(() => production.ReadTweenTargetDeath(f.Owner, f.Backend.Tween));
            RejectBeforeNativeRead(() => production.VerifyTweenTargetBinding(f.Owner, binding));
            RejectBeforeNativeRead(() => production.BorrowTweenChildEvents(f.Owner, (_, _, _) => throw new Exception("A withdrawn child was borrowed.")));
            RejectBeforeNativeRead(() => production.BorrowTypedTweenEvents(f.Owner, (_, _, _) => throw new Exception("A withdrawn typed event was borrowed.")));
            RejectBeforeNativeRead(() => production.BorrowDynamicTypedTweenEvents(f.Owner, 101, (_, _, _) => throw new Exception("A withdrawn dynamic event was borrowed.")));
            ++calls;
        };
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && calls > 0
            && calls == f.Backend.RootManifest.Count(row => row.Holder == holder)
            && f.Backend.RootManifest.All(row => row.Freed && row.FreeAttempts == 1));
        f.Owner.Tick(); _ = f.Owner.Read(); _ = f.Owner.ReadSourceReplay(); _ = f.Owner.ReadWorkCompletion();
        Check(!f.Owner.Unknown);
    }
    private static void RejectBeforeNativeRead(Action read)
    {
        try { read(); }
        catch (InvalidOperationException error) when (error.Message == "Tween backend roots have entered permanent retirement.") { return; }
        throw new Exception("The production entry point did not reject before native, launch or root access.");
    }
    private static void UnendedEffects()
    {
        using (var f = new TweenCompletionFixture(typed: true))
        {
            var play = f.Begin(NativeTweenOperation.Play); f.Backend.InvokeTyped(); f.End(play); f.ConfirmDeaths();
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && f.Roots.EnrollmentOpen && f.Backend.CutNative.Acquires == 0 && f.Backend.Frees == 0);
        }
        using (var f = new TweenCompletionFixture(target: true))
        {
            var play = f.Begin(NativeTweenOperation.Play); f.End(play); f.ConfirmDeaths(killTargets: false);
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && f.Roots.EnrollmentOpen && f.Backend.CutNative.Acquires == 0 && f.Backend.Frees == 0);
        }
    }
    private static void RepeatedRuns()
    {
        var initial = RegisteredOwners().Count;
        for (var index = 0; index < 40; ++index)
        {
            using var f = Ready(); Check(RegisteredOwners().Count == initial + 1 && RegisteredOwners().Contains(f.Owner));
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Roots.Retired && f.Backend.RootRecords == 0
                && f.Backend.RootManifest.All(row => row.Freed && row.FreeAttempts == 1)
                && RegisteredOwners().Count == initial && !RegisteredOwners().Contains(f.Owner));
        }
    }

    private static void EmptySingleEventRetires()
    {
        using var f = new TweenCompletionFixture(typed: true, target: true);
        var play = f.Begin(NativeTweenOperation.Play);
        f.Backend.InvokeTypedEmpty();
        f.End(play);
        f.ConfirmDeaths();
        Check(f.Borrow() && f.Owner.ReadWorkCompletion().Pending.Count == 0);
        Check(f.Owner.ReadTypedEffects().Single().Pending.Length == 0);
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Roots.Retired && f.Backend.RootRecords == 0
            && f.Backend.CutNative.Commits == 1 && f.Backend.CutNative.Releases == 1
            && f.Backend.RootManifest.All(row => row.Freed && row.FreeAttempts == 1)
            && f.Owner.ReadTypedEffects().Single().Pending.Length == 0);
    }

    private static void EmptySingleEventGatesRejectRetirement()
    {
        foreach (var mode in new[] {
            "no-mid", "post-graph-changed", "dispose-graph-changed", "writer-gen-mid",
            "writer-gen-post", "writer-gen-dispose", "writer-gen-late", "calls-dirty",
            "needs-update", "nonempty-persistent", "nonempty-runtime", "foreign-child",
            "activity-inflight", "activity-unknown", "abnormal", "setter-tweentype" })
        {
            using var f = new TweenCompletionFixture(typed: true, target: true, tweenType: mode == "setter-tweentype" ? 3 : 12);
            var play = f.Begin(NativeTweenOperation.Play);
            var g = f.Backend.TypedEventGraph!;
            switch (mode)
            {
                case "calls-dirty": f.Backend.TypedEventGraph = g with { CallsDirty = true }; break;
                case "needs-update": f.Backend.TypedEventGraph = g with { NeedsUpdate = true }; break;
                case "nonempty-persistent":
                    f.Backend.TypedEventGraph = g with { PersistentCalls = new(107, 3011, 3013, 1, 1, new[] { 9001L }) }; break;
                case "nonempty-runtime":
                    f.Backend.TypedEventGraph = g with { RuntimeCalls = new(107, 3012, 3014, 1, 1, new[] { 9002L }) }; break;
            }
            f.Backend.InvokeTypedEmpty(
                withMid: mode != "no-mid",
                betweenPreAndMid: () => { if (mode == "writer-gen-mid") ++f.Backend.TypedEventWriterGeneration; },
                betweenMidAndPost: () => {
                    if (mode == "post-graph-changed")
                        f.Backend.TypedEventGraph = g with { RuntimeCalls = g.RuntimeCalls with { Version = 2 } };
                    if (mode == "writer-gen-post") ++f.Backend.TypedEventWriterGeneration;
                },
                betweenPostAndDispose: () => {
                    if (mode == "dispose-graph-changed")
                        f.Backend.TypedEventGraph = g with { ExecutingCalls = g.ExecutingCalls with { Version = 2 } };
                    if (mode == "writer-gen-dispose") ++f.Backend.TypedEventWriterGeneration;
                },
                foreignChild: mode == "foreign-child",
                abnormal: mode == "abnormal");
            f.End(play, mode == "abnormal" ? NativeOperationExitFlags.Abnormal : NativeOperationExitFlags.Returned);
            if (mode == "writer-gen-late") ++f.Backend.TypedEventWriterGeneration;
            if (mode == "activity-inflight") f.Backend.TypedEventActivity = new(1, true);
            if (mode == "activity-unknown") f.Backend.TypedEventActivity = new(0, false);
            if (mode != "abnormal") f.ConfirmDeaths();
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && f.Backend.CutNative.Acquires == 0 && f.Backend.Frees == 0);
            Check(f.Owner.Unknown == (mode == "abnormal"));
        }
    }

    private static void NonEmptySingleListenersRemainPendingAcrossSites()
    {
        foreach (var (site, cached) in new[] { (0u, false), (0u, true), (2u, false), (1u, false) })
        {
            using var f = new TweenCompletionFixture(typed: true, target: true);
            var list = new NativeSingleExecutingList(107, 3001, 3002, 1, 1, new[] { 4001L });
            f.Backend.TypedEventGraph = f.Backend.TypedEventGraph! with { RuntimeCalls = list, ExecutingCalls = list };
            var play = f.Begin(NativeTweenOperation.Play);
            var (context, parent) = f.Backend.BeginTypedListenerInvocation();
            var mid = new NativeTweenSingleEventFrame {
                Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Mid, Site = 3,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11,
                ParentManagedToken = 13, ParentSequence = parent.Sequence, Event = parent.Self, List = new(3001),
                ParentMethod = parent.Method, ParentCaller = parent.Caller, ValueBits = parent.ScalarBits, SourceRsp = 0x10000
            };
            f.Owner.CaptureTypedExecutingList(context, mid, list);
            var before = new NativeTweenSingleEventFrame {
                Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Before, Site = site,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11,
                ParentManagedToken = 13, ParentSequence = parent.Sequence, Event = parent.Self, List = new(3001),
                ParentMethod = parent.Method, ParentCaller = parent.Caller, ValueBits = parent.ScalarBits,
                ChildSequence = 201, Index = 0, Invokable = new(4001), ActualTarget = new(0x181550000),
                ArgRcx = new(4001), ArgRdx = site == 2 ? new(7201) : IntPtr.Zero, ArgR8 = site == 1 ? IntPtr.Zero : new(6001),
                SourceRsp = 0x10000
            };
            var delegatePtr = site == 0 ? (cached ? 7102L : 7101L) : 0L;
            uint? effective = site == 0 ? (cached ? 0x80000000u : parent.ScalarBits) : null;
            var target = new NativeSingleDispatchTarget(site, 4001, before.ActualTarget.ToInt64(), 6001,
                before.ValueBits, effective, cached, site == 2 ? 7201L : 0L,
                site == 2 ? new[] { 7202L } : Array.Empty<long>(), "non-empty test", delegatePtr);
            f.Owner.CaptureTypedDispatch(context, before, list, target);
            var after = before; after.Stage = NativeTweenSingleEventStage.Returned;
            f.Owner.CompleteTypedDispatch(context, after);
            // Even if listener is removed before CompleteTyped/DisposeTyped, non-empty dispatch remains Pending.
            f.Backend.TypedEventGraph = f.Backend.TypedEventGraph! with {
                RuntimeCalls = f.Backend.TypedEventGraph.RuntimeCalls with { Version = 2 }
            };
            f.Owner.CompleteTyped(context, parent, NativeOperationExitFlags.Returned);
            context.Dispose();
            f.End(play);
            f.ConfirmDeaths();
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Roots.EnrollmentOpen
                && f.Backend.CutNative.Acquires == 0 && f.Backend.Frees == 0);
            var snapshot = f.Owner.ReadTypedEffects().Single();
            Check(snapshot.Pending.Length > 0);
            var dispatch = snapshot.Executing!.Dispatches.Single();
            Check(dispatch.Site == site && dispatch.CachedSingle == cached && dispatch.EffectiveSingleBits == effective
                && dispatch.Delegate == delegatePtr && dispatch.Pending.Length > 0 && dispatch.Transfer is null);
        }
    }

    private static void EmptySingleEventBusyThenRetryInvalidations()
    {
        foreach (var mode in new[] { "clean", "writer-gen", "typed-inflight", "typed-unknown", "graph-dirty" })
        {
            using var f = new TweenCompletionFixture(typed: true, target: true);
            var play = f.Begin(NativeTweenOperation.Play);
            f.Backend.InvokeTypedEmpty();
            f.End(play);
            f.ConfirmDeaths();

            f.Backend.CutNative.Busy = true;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.CutNative.Acquires == 1
                && f.Backend.CutNative.Commits == 0 && !f.Owner.RetiredRootsUnavailable);

            switch (mode)
            {
                case "writer-gen": ++f.Backend.TypedEventWriterGeneration; break;
                case "typed-inflight": f.Backend.TypedEventActivity = new(1, true); break;
                case "typed-unknown": f.Backend.TypedEventActivity = new(0, false); break;
                case "graph-dirty":
                    var g = f.Backend.TypedEventGraph!;
                    f.Backend.TypedEventGraph = g with {
                        CallsDirty = true,
                        RuntimeCalls = new(107, 3012, 3014, 1, 2, new[] { 9002L })
                    };
                    break;
            }

            f.Backend.CutNative.Busy = false;
            f.Owner.PumpControlledTweenRetirementForTests();
            if (mode == "clean")
            {
                Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.CutNative.Commits == 1
                    && f.Backend.CutNative.Releases == 1 && f.Roots.Retired);
            }
            else
            {
                Check(!f.Owner.CanRetireWork() && f.Owner.Unknown && f.Backend.CutNative.Commits == 0
                    && f.Backend.Frees == 0 && !f.Roots.Retired);
            }
        }
    }

    private static void OnTweenWriterLedgerTracksMutations()
    {
        using var main = new RizToolbox.Core.MainThreadDispatcher();
        var backend = new TypedWriterLedgerBackend();
        var authority = new TypedWriterLedgerAuthority();
        var ledger = NativeLocalUnityEventLedger.CaptureBackend(main, backend, authority, 0x20000000);
        authority.Capturing = false;

        // Register two distinct onTween<Single> graphs without adding them to Cutout _events.
        ledger.RegisterTypedEventWriter(0x1070, 0x1071, 0x1072, new[] { 0x1073L });
        ledger.RegisterTypedEventWriter(0x2070, 0x2071, 0x2072);
        Check(ledger.Read().Events == 1 && ledger.Read().EventSnapshots.Count == 1
            && ledger.Read().EventSnapshots.Single().Event == 101);
        Check(ledger.ReadTypedEventWriterGeneration(0x1070) == 0 && ledger.ReadTypedEventWriterGeneration(0x2070) == 0);
        Throws<InvalidOperationException>(() => ledger.ReadTypedEventWriterGeneration(0x9999));

        ulong seq = 10;
        void Mutate(UnityEventOperation op, long self, long arg0 = 0, long arg1 = 0)
        {
            var row = UnityEventOperationProfile.Get(op);
            var frame = new NativeOperationFrame {
                Version = 1, Size = 64, Kind = row.Kind, Self = new(self),
                Arg0 = new(arg0), Arg1 = new(arg1), Method = new(601), Caller = new(0x20001234), Sequence = ++seq
            };
            var ctx = ledger.CaptureMutation(op, frame)
                ?? throw new InvalidOperationException("Typed writer mutation was not captured for " + op);
            Check(ledger.Read().NativeInFlight == 1);
            ledger.CompleteMutation(ctx, frame, NativeOperationExitFlags.Returned);
            ctx.Dispose();
        }

        // 1. Mutate onTween (0x1070) via RemoveAllListeners
        Mutate(UnityEventOperation.RemoveAllListeners, 0x1070);
        Check(ledger.ReadTypedEventWriterGeneration(0x1070) == 1 && ledger.ReadTypedEventWriterGeneration(0x2070) == 0);
        Check(ledger.Read().ObservedMutations == 1 && ledger.Read().ReturnedMutations == 1 && ledger.Read().NativeInFlight == 0);

        // 2. Mutate onTween.m_Calls (0x1071) via ClearRuntimeCalls
        Mutate(UnityEventOperation.ClearRuntimeCalls, 0x1071);
        Check(ledger.ReadTypedEventWriterGeneration(0x1070) == 2 && ledger.ReadTypedEventWriterGeneration(0x2070) == 0);
        Check(ledger.Read().ObservedMutations == 2 && ledger.Read().ReturnedMutations == 2);

        // 3. Mutate onTween.m_PersistentCalls (0x1072) via InitializePersistentCalls
        Mutate(UnityEventOperation.InitializePersistentCalls, 0x1072, 0x1071, 0x1070);
        Check(ledger.ReadTypedEventWriterGeneration(0x1070) == 3 && ledger.ReadTypedEventWriterGeneration(0x2070) == 0);
        Check(ledger.Read().ObservedMutations == 3 && ledger.Read().ReturnedMutations == 3);

        // 4. Mutate unrelated Cutout UnityEvent (101) — must NOT advance onTween2 (0x2070) or onTween (0x1070)
        Mutate(UnityEventOperation.RemoveAllListeners, 101);
        Check(ledger.ReadTypedEventWriterGeneration(0x1070) == 3 && ledger.ReadTypedEventWriterGeneration(0x2070) == 0);
        Check(ledger.Read().ObservedMutations == 4 && ledger.Read().ReturnedMutations == 4);

        // 5. Foreign unregistered event still returns null
        var foreignFrame = new NativeOperationFrame {
            Version = 1, Size = 64, Kind = NativeOperationKind.Void0, Self = new(0x9999),
            Method = new(601), Caller = new(0x20001234), Sequence = ++seq
        };
        Check(ledger.CaptureMutation(UnityEventOperation.RemoveAllListeners, foreignFrame) is null);

        // 6. Verify production NativeLocalTweenBackend.ReadTypedEventWriterGeneration queries per-onTween generation, not ObservedMutations
        using var f = new TweenCompletionFixture(typed: true, target: true);
        var registry = new LocalRunSessionRegistry(main);
        using var bundle = new TypedWriterBundle();
        var session = registry.RegisterPrepared(new(new("typed.writer", "song"), 1, "chart"), 0x7fa000010, bundle);
        var launch = new LocalRunLaunchCommitState(main, registry, registry.BeginCommit(session), bundle);
        var prodBackend = new NativeLocalTweenBackend(null!, main, null!, IntPtr.Zero, null!, launch, ledger);
        typeof(NativeLocalTweenBackend).GetField("_owner", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(prodBackend, f.Owner);
        typeof(NativeLocalTweenBackend).GetField("_ready", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(prodBackend, true);
        var memberships = (List<NativeLocalTweenWorkOwner.TypedEventMembership>)typeof(NativeLocalTweenBackend)
            .GetField("_typedEventMemberships", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(prodBackend)!;
        var graphSource = (INativeTweenTypedEventGraphSource)prodBackend;
        using var root1070 = new NativeRuntime.RootHandle(1, _ => { }, _ => new(0x1070), main.VerifyAccess);
        using var root2070 = new NativeRuntime.RootHandle(2, _ => { }, _ => new(0x2070), main.VerifyAccess);
        using var rootUntracked = new NativeRuntime.RootHandle(3, _ => { }, _ => new(0x9999), main.VerifyAccess);
        // Seed scalar prefilter for 0x1070 and 0x2070 on owner
        typeof(NativeLocalTweenWorkOwner).GetField("_typedEventIdentities", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(f.Owner, new long[] { 0x1070, 0x2070 });
        Check(graphSource.ReadTypedEventWriterGeneration(f.Owner, root1070) == 3);
        Check(graphSource.ReadTypedEventWriterGeneration(f.Owner, root2070) == 0);
        Throws<InvalidOperationException>(() => graphSource.ReadTypedEventWriterGeneration(f.Owner, rootUntracked));
        Check(!ledger.Unknown && !f.Owner.Unknown);
        Check(memberships.Count == 0);
    }

    private static void SkippedParentWithTypedInvocationRejectsRetirement()
    {
        // Variant A: Skipped Play parent after executing child TypedEventInvocation
        using (var f = new TweenCompletionFixture(typed: true, target: true))
        {
            var play = f.Begin(NativeTweenOperation.Play);
            f.Backend.InvokeTypedEmpty(withMid: true);
            Throws<InvalidOperationException>(() => f.Owner.CompleteOperation(play, play.Frame, NativeOperationExitFlags.Skipped));
            play.Dispose();
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(f.Owner.Unknown && !f.Owner.CanRetireWork()
                && f.Backend.CutNative.Commits == 0 && f.Backend.Frees == 0 && !f.Roots.Retired
                && f.Owner.ReadSourceReplay().Occurrences.Count == 1
                && f.Owner.ReadTypedEffects().Single().Pending.Length > 0);
        }

        // Variant B: Skipped ExecuteTween parent after executing child TypedEventInvocation
        using (var f = new TweenCompletionFixture(playing: new long[] { 101, 0, 0, 0 }, working: 1, typed: true, target: true))
        {
            var update = f.Begin(NativeTweenOperation.Update);
            var call = f.Before(update, NativeTweenSourceSite.PlayingExecuteFront, 201, 303, source: 0, count: 4, version: 0);
            f.Publish(call);
            var exec = f.Begin(NativeTweenOperation.ExecuteTween, call.RelayReturnPc.ToInt64() + 5);
            f.Backend.InvokeTypedEmpty(withMid: true);
            Throws<InvalidOperationException>(() => f.Owner.CompleteOperation(exec, exec.Frame, NativeOperationExitFlags.Skipped));
            exec.Dispose();
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(f.Owner.Unknown && !f.Owner.CanRetireWork()
                && f.Backend.CutNative.Commits == 0 && f.Backend.Frees == 0 && !f.Roots.Retired
                && f.Owner.ReadSourceReplay().Occurrences.Count == 1
                && f.Owner.ReadTypedEffects().Single().Pending.Length > 0);
        }

        // Positive control: clean Skipped Play and Skipped ExecuteTween without child invocations retire normally
        using (var f = new TweenCompletionFixture(playing: new long[] { 101, 0, 0, 0 }, working: 1, typed: true, target: true))
        {
            var skippedPlay = f.Begin(NativeTweenOperation.Play);
            f.End(skippedPlay, NativeOperationExitFlags.Skipped);
            var update = f.Begin(NativeTweenOperation.Update);
            var skippedExec = f.Begin(NativeTweenOperation.ExecuteTween);
            f.End(skippedExec, NativeOperationExitFlags.Skipped);
            f.Clear(update, 0);
            f.EndUpdate(update, new long[4], 0);
            f.ConfirmDeaths();
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.Unknown && f.Owner.CanRetireWork()
                && f.Backend.CutNative.Commits == 1 && f.Backend.CutNative.Releases == 1 && f.Roots.Retired);
        }
    }

    private static void Site1PlayTransferSurvivesBusyThenRetry()
    {
        // Valid non-empty Site 1 UnityAction -> TweenInfo.Play() transfer must survive Busy -> Retry
        using (var f = new TweenCompletionFixture(playing: new long[] { 101, 0, 0, 0 }, working: 1, typed: true, target: true))
        {
            var update = f.Begin(NativeTweenOperation.Update);
            var call = f.Before(update, NativeTweenSourceSite.PlayingExecuteFront, 201, 303, source: 0, count: 4, version: 0);
            f.Publish(call);
            var exec = f.Begin(NativeTweenOperation.ExecuteTween, call.RelayReturnPc.ToInt64() + 5);
            var list = new NativeSingleExecutingList(107, 3001, 3002, 1, 1, new[] { 4001L });
            f.Backend.TypedEventGraph = f.Backend.TypedEventGraph! with { RuntimeCalls = list, ExecutingCalls = list };
            f.Backend.PlayConsumer = new(4001, 5001, 101, 700, 0x1803956f0, 0x1803956f0, 101);
            var (context, parent) = f.Backend.BeginTypedListenerInvocation();
            var mid = new NativeTweenSingleEventFrame {
                Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Mid, Site = 3,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11,
                ParentManagedToken = 13, ParentSequence = parent.Sequence, Event = parent.Self, List = new(3001),
                ParentMethod = parent.Method, ParentCaller = parent.Caller, ValueBits = parent.ScalarBits, SourceRsp = 0x10000
            };
            f.Owner.CaptureTypedExecutingList(context, mid, list);
            var before = new NativeTweenSingleEventFrame {
                Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Before, Site = 1,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11,
                ParentManagedToken = 13, ParentSequence = parent.Sequence, Event = parent.Self, List = new(3001),
                ParentMethod = parent.Method, ParentCaller = parent.Caller, ValueBits = parent.ScalarBits,
                ChildSequence = 201, Index = 0, Invokable = new(4001), ActualTarget = new(0x18154e5b0),
                ArgRcx = new(4001), ArgRdx = IntPtr.Zero, ArgR8 = IntPtr.Zero, SourceRsp = 0x10000
            };
            var target = new NativeSingleDispatchTarget(1, 4001, before.ActualTarget.ToInt64(), 6001,
                before.ValueBits, null, false, 0, Array.Empty<long>(), "site 1 play transfer");
            f.Owner.CaptureTypedDispatch(context, before, list, target);
            var innerPlay = f.Begin(NativeTweenOperation.Play);
            f.End(innerPlay);
            var after = before; after.Stage = NativeTweenSingleEventStage.Returned;
            f.Owner.CompleteTypedDispatch(context, after);
            f.Owner.CompleteTyped(context, parent, NativeOperationExitFlags.Returned);
            context.Dispose();
            f.End(exec);
            var afterCall = call; afterCall.Stage = NativeTweenSourceStage.After; afterCall.Flags |= NativeTweenSourceFlags.Paired;
            f.Publish(afterCall);
            f.Clear(update, 0);
            f.EndUpdate(update, new long[4], 0);
            f.ConfirmDeaths();

            Check(f.Owner.ReadTypedEffects().Single().Pending.Length == 0
                && f.Owner.ReadTypedExecutingLists().Single().Dispatches.Single().Transfer is not null);

            f.Backend.CutNative.Busy = true;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.CutNative.Acquires == 1
                && f.Backend.CutNative.Commits == 0 && !f.Owner.RetiredRootsUnavailable);

            f.RefuseJournalReads();
            f.Backend.CutNative.Busy = false;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.CutNative.Acquires == 2
                && f.Backend.CutNative.Commits == 1 && f.Backend.CutNative.Releases == 1 && f.Roots.Retired);
        }

        // ProbeD_08: post-Busy EventRoot, WriterGeneration, or TypedEventGraph mutations on a non-empty Site 1 Play transfer must fail closed
        foreach (var mode in new[] { "event-root-zero", "event-root-swap", "writer-gen", "graph-add-listener", "needs-update", "persistent-call-added" })
        {
            using var f = new TweenCompletionFixture(playing: new long[] { 101, 0, 0, 0 }, working: 1, typed: true, target: true);
            var update = f.Begin(NativeTweenOperation.Update);
            var call = f.Before(update, NativeTweenSourceSite.PlayingExecuteFront, 201, 303, source: 0, count: 4, version: 0);
            f.Publish(call);
            var exec = f.Begin(NativeTweenOperation.ExecuteTween, call.RelayReturnPc.ToInt64() + 5);
            var list = new NativeSingleExecutingList(107, 3001, 3002, 1, 1, new[] { 4001L });
            f.Backend.TypedEventGraph = f.Backend.TypedEventGraph! with { RuntimeCalls = list, ExecutingCalls = list };
            f.Backend.PlayConsumer = new(4001, 5001, 101, 700, 0x1803956f0, 0x1803956f0, 101);
            var (context, parent) = f.Backend.BeginTypedListenerInvocation();
            var mid = new NativeTweenSingleEventFrame {
                Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Mid, Site = 3,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11,
                ParentManagedToken = 13, ParentSequence = parent.Sequence, Event = parent.Self, List = new(3001),
                ParentMethod = parent.Method, ParentCaller = parent.Caller, ValueBits = parent.ScalarBits, SourceRsp = 0x10000
            };
            f.Owner.CaptureTypedExecutingList(context, mid, list);
            var before = new NativeTweenSingleEventFrame {
                Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Before, Site = 1,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11,
                ParentManagedToken = 13, ParentSequence = parent.Sequence, Event = parent.Self, List = new(3001),
                ParentMethod = parent.Method, ParentCaller = parent.Caller, ValueBits = parent.ScalarBits,
                ChildSequence = 201, Index = 0, Invokable = new(4001), ActualTarget = new(0x18154e5b0),
                ArgRcx = new(4001), ArgRdx = IntPtr.Zero, ArgR8 = IntPtr.Zero, SourceRsp = 0x10000
            };
            var target = new NativeSingleDispatchTarget(1, 4001, before.ActualTarget.ToInt64(), 6001,
                before.ValueBits, null, false, 0, Array.Empty<long>(), "site 1 play transfer");
            f.Owner.CaptureTypedDispatch(context, before, list, target);
            var innerPlay = f.Begin(NativeTweenOperation.Play);
            f.End(innerPlay);
            var after = before; after.Stage = NativeTweenSingleEventStage.Returned;
            f.Owner.CompleteTypedDispatch(context, after);
            f.Owner.CompleteTyped(context, parent, NativeOperationExitFlags.Returned);
            context.Dispose();
            f.End(exec);
            var afterCall = call; afterCall.Stage = NativeTweenSourceStage.After; afterCall.Flags |= NativeTweenSourceFlags.Paired;
            f.Publish(afterCall);
            f.Clear(update, 0);
            f.EndUpdate(update, new long[4], 0);
            f.ConfirmDeaths();

            f.Backend.CutNative.Busy = true;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.CutNative.Acquires == 1
                && f.Backend.CutNative.Commits == 0 && !f.Owner.RetiredRootsUnavailable);

            switch (mode)
            {
                case "event-root-zero":
                case "event-root-swap":
                    var roots = (Dictionary<uint, IntPtr>)typeof(TweenCompletionFixture.CompletionBackend)
                        .GetField("_roots", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Backend)!;
                    var eventRoot = (NativeRuntime.RootHandle)typeof(TweenCompletionFixture.CompletionBackend)
                        .GetField("_event", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Backend)!;
                    var eventRootId = (uint)typeof(NativeRuntime.RootHandle)
                        .GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(eventRoot)!;
                    roots[eventRootId] = mode == "event-root-zero" ? IntPtr.Zero : new IntPtr(9999);
                    break;
                case "writer-gen":
                    ++f.Backend.TypedEventWriterGeneration;
                    break;
                case "graph-add-listener":
                    f.Backend.TypedEventGraph = f.Backend.TypedEventGraph! with {
                        CallsDirty = true,
                        RuntimeCalls = new(107, 3012, 3014, 1, 2, new[] { 9002L })
                    };
                    break;
                case "needs-update":
                    f.Backend.TypedEventGraph = f.Backend.TypedEventGraph! with { NeedsUpdate = true };
                    break;
                case "persistent-call-added":
                    f.Backend.TypedEventGraph = f.Backend.TypedEventGraph! with {
                        PersistentCalls = new(107, 3011, 3013, 1, 2, new[] { 9001L })
                    };
                    break;
            }

            f.Backend.CutNative.Busy = false;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && f.Owner.Unknown && f.Backend.CutNative.Commits == 0
                && f.Backend.Frees == 0 && !f.Roots.Retired);
        }

        // 1.16 null-empty-receipt regression: nulling EmptyReceipt on an empty invocation during Busy->Retry must still fail closed
        using (var f = new TweenCompletionFixture(typed: true, target: true))
        {
            var play = f.Begin(NativeTweenOperation.Play);
            f.Backend.InvokeTypedEmpty();
            f.End(play);
            f.ConfirmDeaths();

            f.Backend.CutNative.Busy = true;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.CutNative.Acquires == 1 && f.Backend.CutNative.Commits == 0);

            var typedInvocations = (System.Collections.IList)OwnerState(f.Owner, "_typedInvocations")!;
            var invocation = typedInvocations[0]!;
            invocation.GetType().GetField("EmptyReceipt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(invocation, null);

            f.Backend.CutNative.Busy = false;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && f.Owner.Unknown && f.Backend.CutNative.Commits == 0
                && f.Backend.Frees == 0 && !f.Roots.Retired);
        }
    }

    private sealed class TypedWriterBundle : IDisposable { public void Dispose() { } }

    private sealed class TypedWriterLedgerAuthority : ICutoutListenerAuthority
    {
        internal bool Capturing = true;
        public bool Unknown => false;
        public void VerifyBeforeCapture() { if (!Capturing) throw new InvalidOperationException("Capture closed."); }
        public void VerifyObservation() { if (Capturing) throw new InvalidOperationException("Not active."); }
        public bool TryConfirmSourceIdle() => true;
        public void ObserveFaultFromAnyThread() { }
    }

    private sealed class TypedWriterLedgerBackend : ICutoutListenerBackend
    {
        internal NativeUnityEventGraph Graph = new(101, 201, 202, false, false,
            new(301, 0, Array.Empty<NativeUnityEventInvokable>()), new(302, 0, Array.Empty<NativeUnityEventInvokable>()),
            new(303, 0, Array.Empty<NativeUnityEventInvokable>()), 304, 0, Array.Empty<NativeUnityEventPersistent>(), 0);
        public bool CleanupUnknown => false;
        public int RootRecords => 1;
        public void Initialize(NativeLocalUnityEventLedger owner) => owner.RegisterEvent(Graph.Event);
        public NativeUnityEventGraph ReadGraph(int eventSlot) => Graph;
        public NativeUnityEventGraph CaptureMutation(int eventSlot, UnityEventOperation operation, NativeOperationFrame frame) => Graph;
        public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList) => throw new InvalidOperationException();
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
    }
    private static object? OwnerState(NativeLocalTweenWorkOwner owner, string field)
        => typeof(NativeLocalTweenWorkOwner).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner);
    private static HashSet<NativeLocalTweenWorkOwner> RegisteredOwners()
        => (HashSet<NativeLocalTweenWorkOwner>)typeof(NativeLocalTweenWorkOwner).GetField("Owners", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    private static TweenCompletionFixture Ready()
    {
        var f = new TweenCompletionFixture(playing: new long[] { 101, 101, 0, 0 }, working: 2, target: true);
        var update = f.Begin(NativeTweenOperation.Update); f.Clear(update, 1); f.Clear(update, 0); f.EndUpdate(update, new long[4], 0);
        f.ConfirmDeaths(); return f;
    }
}

internal sealed partial class TweenCompletionFixture
{
    internal sealed partial class CompletionBackend : INativeLocalTweenRetirementBackend
    {
        internal sealed class RootRecord
        {
            internal readonly uint Id; internal readonly string Holder; internal NativeRuntime.RootHandle Handle = null!;
            internal int FreeAttempts; internal bool Freed;
            internal RootRecord(uint id, string holder) => (Id, Holder) = (id, holder);
        }
        internal readonly List<RootRecord> RootManifest = new();
        internal readonly CompletionCutTransport CutNative;
        private NativeTweenAdmission.RetirementBatch? _retirementBatch;
        internal bool BackendReleaseAttempted;
        internal bool RefuseRootReads;
        internal Action<RootRecord>? BeforeRootFree;
        internal int FailBackendFreeAt;
        private int _backendFreeCount;
        internal bool ObservationBindingsClosed => _observersClosed;
        public bool TweenBackendRootsReleased { get; private set; }
        private NativeRuntime.RootHandle RetainRoot(IntPtr identity, string holder)
        {
            var id = ++_next; _roots.Add(id, identity); var record = new RootRecord(id, holder); RootManifest.Add(record);
            record.Handle = new(id, key => {
                if (++record.FreeAttempts != 1) throw new InvalidOperationException("A real handle free was repeated.");
                BeforeRootFree?.Invoke(record);
                if (record.Holder == "Backend" && ++_backendFreeCount == FailBackendFreeAt)
                    throw new InvalidOperationException("Controlled actual backend handle free failed after its attempt was recorded.");
                if (!_roots.Remove(key)) throw new InvalidOperationException("A handle was removed before its actual free.");
                record.Freed = true; ++Frees;
            }, key => RefuseRootReads ? throw new InvalidOperationException("No late retained root target reads.") : _roots.GetValueOrDefault(key), _main.VerifyAccess);
            return record.Handle;
        }
        public void VerifyTweenRetirementOwner(NativeLocalTweenWorkOwner owner)
        { _main.VerifyAccess(); if (!ReferenceEquals(owner, _owner) || owner.ObservationAuthorityUnknown) throw new InvalidOperationException("Foreign/Unknown controlled retirement owner."); }
        public NativeTweenAdmission.RetirementBatch PrepareTweenRetirement(NativeLocalTweenWorkOwner owner)
        { VerifyTweenRetirementOwner(owner); return _retirementBatch ??= _admission.PrepareRetirement(owner); }
        public NativeTweenRetirementContext BindTweenRetirementContext(NativeLocalTweenWorkOwner owner)
        { VerifyTweenRetirementOwner(owner); return NativeTweenRetirementContext.CreateControlledForTests(CutNative, owner, _main.VerifyAccess); }
        public void WithdrawTweenObservationBindings(NativeLocalTweenWorkOwner owner) { VerifyTweenRetirementOwner(owner); CloseObservationBindings(); }
        public void ReleaseTweenBackendRoots(NativeLocalTweenWorkOwner owner, NativeTweenRetirementContext.Cut cut)
        {
            VerifyTweenRetirementOwner(owner); cut.VerifyCurrent();
            if (!cut.Committed || !ReferenceEquals(cut.Owner, owner) || !_observersClosed || BackendReleaseAttempted)
                throw new InvalidOperationException("Backend roots need the same actual cut and one closed unused holder manifest.");
            BackendReleaseAttempted = true;
            foreach (var root in RootManifest.Where(row => row.Holder == "Backend"))
            { cut.VerifyCurrent(); root.Handle.Dispose(); cut.VerifyCurrent(); }
            cut.VerifyCurrent();
            TweenBackendRootsReleased = true; RefuseReads = Native.RefuseReads = true;
        }
    }
    internal sealed class CompletionCutTransport : INativeTweenRetirementContextTransport
    {
        private readonly CompletionBackend _backend;
        private NativeTweenRetirementCutReceipt _current;
        private NativeTweenRetirementClaim[] _claims = Array.Empty<NativeTweenRetirementClaim>();
        private ulong _generation;
        private bool _held;
        internal bool Busy, ReleaseFailure;
        internal string? CommitFailure;
        internal Action? BeforeRelease;
        internal NativeTweenRetirementRequest LastRequest;
        internal int Acquires, Commits, Releases;
        internal CompletionCutTransport(CompletionBackend backend) => _backend = backend;
        public uint Acquire(ref NativeTweenRetirementRequest request, NativeTweenRetirementClaim[] claims, ref NativeTweenRetirementCutReceipt cut)
        {
            ++Acquires; LastRequest = request;
            if (Busy) return 3;
            var actual = _backend.Native.Current;
            if (_held || request.ConfigurationEpoch != 7 || request.SourceEpoch != 7 || claims.Length != 1
                || claims[0].Object != actual.Object || claims[0].ActualClass != actual.ActualClass || claims[0].Slot != actual.Slot
                || claims[0].Epoch != actual.Epoch || actual.Phase != NativeTweenAdmissionPhase.Sealed || actual.CachedNative != IntPtr.Zero
                || !_backend.ObservationBindingsClosed) return 2;
            _claims = claims.ToArray();
            cut = new() { Version = 1, Size = 88, ThreadId = NativeTweenSourceReferences.CurrentThreadId(), Phase = 1,
                Token = ++_generation, Generation = _generation, ConfigurationEpoch = request.ConfigurationEpoch,
                SourceEpoch = request.SourceEpoch, JournalCutoff = request.JournalCutoff, ReferenceObserverEpoch = request.ReferenceObserverEpoch,
                ReferenceDeliveryCutoff = request.ReferenceDeliveryCutoff, ReferenceReaderGeneration = request.ReferenceReaderGeneration,
                ClaimCount = (uint)claims.Length };
            _current = cut; _held = true; return 0;
        }
        public uint Verify(ref NativeTweenRetirementCutReceipt cut) => _held && cut.Equals(_current) ? 0u : 2u;
        public uint VerifyReferenceRoots(ref NativeTweenRetirementCutReceipt cut) => Verify(ref cut);
        public uint UnregisterBatch(ref NativeTweenRetirementCutReceipt cut, NativeTweenRetirementClaim[] claims, NativeTweenAdmissionReceipt[] receipts)
        {
            if (Verify(ref cut) != 0 || claims.Length != _claims.Length || !claims.SequenceEqual(_claims) || receipts.Length != claims.Length) return 2;
            ++Commits;
            if (CommitFailure == "throw") throw new InvalidOperationException("Controlled native all-or-nothing batch threw before mutation.");
            if (CommitFailure == "status") return 2;
            _backend.Native.Current.Phase = NativeTweenAdmissionPhase.Retired;
            receipts[0] = _backend.Native.Current;
            if (CommitFailure == "tuple") receipts[0].ActualClass = new(999);
            return 0;
        }
        public uint Release(ref NativeTweenRetirementCutReceipt cut)
        { ++Releases; BeforeRelease?.Invoke(); if (ReleaseFailure) throw new InvalidOperationException("Controlled actual native Cut release failed.");
            var status = Verify(ref cut); _held = false; return status; }
    }
}
