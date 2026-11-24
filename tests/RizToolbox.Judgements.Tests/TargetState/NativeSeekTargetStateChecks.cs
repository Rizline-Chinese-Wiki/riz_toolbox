using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekTargetStateChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Target state captures actual source and exposes an explicit incomplete practice policy", CaptureSource),
        ("Target state uses initialized native cached windows instead of information defaults", CachedWindows),
        ("Target state uninitialized Judge projects source windows and challenge initialization without stores", Initialization),
        ("Target state progress target casts to synced float without offset or chart delay replay", SyncedSeconds),
        ("Target state native epsilon chain uses literal nested-swap order rather than stable sorting", NativeSwap),
        ("Target state compares rounded float differences against the native double epsilon", EpsilonThreshold),
        ("Target state preserves distinct actual instances sharing one chart location", DuplicateInstances),
        ("Target state reports missing chart instances without fabricating references", MissingInstances),
        ("Target state prefix break retains later individually eligible heads", PrefixBreak),
        ("Target state delayed Drag continues scanning later Tap heads", DragContinue),
        ("Target state native float window rounding and strict equality boundaries are exact", FloatWindows),
        ("Target state challenge timeout reasons preserve mode and per-note conjunctions", ChallengeTimeout),
        ("Target state crossing Hold distinguishes a live head window from continuation requirements", CrossingHold),
        ("Target state Hold end equality does not certify strict-tail engine completion", HoldBoundary),
        ("Target state explicit crossing Hold skip changes queues and retains remaining work", ExplicitSkip),
        ("Target state automatically projected queues reach actual official writes and full restoration", AutoQueues),
        ("Target state source changes and two-pass observations revoke retained projections", SourceChanges),
        ("Target state rejects nonfinite values and exact declared field metadata mismatches", Rejections),
        ("Target state constructor reentry thread and callback disposal precede later exports", Lifetime),
        ("Target state unknown source free retains the exact root owner without retry", UnknownFree)
    };
    private static NativeSeekTargetStatePlan Capture(Fixture f, double target, SeekCrossingHoldPolicy policy = SeekCrossingHoldPolicy.ContinuationRequired)
        => f.Own(f.Run.CaptureSeekTargetStatePlan(f.Native.TargetProfile, new(target), new(policy)));
    private static long[] References(Fixture f) => f.Native.JudgeRuntimeReferences;
    private static void Plain(FakeNative n, int ordinal, float realTime)
    { n.TargetSetChart(ordinal, 0, realTime * 2); n.TargetSetTiming(ordinal, realTime); }
    private static void PlainFuture(FakeNative n)
    { for (var index = 0; index < n.JudgeRuntimeReferences.Length; ++index) Plain(n, index, 20 + index); }
    private static void Window(FakeNative n, float judge, float normal, float bad, bool ready = true, bool complete = false)
    {
        foreach (var (name, value) in new[] { ("_judgeTime", judge), ("_normalPerfectJudgeTimeInChallengeTime", normal), ("badTime", bad) }) n.TargetSet(n.JudgeOwner, name, value);
        n.TargetSet(n.JudgeOwner, "_instantiateReady", ready); n.TargetSet(n.GameplayInformation, "challengeCompletely", complete);
    }
    private static bool Requires(NativeSeekTargetStatePlan plan, SeekTargetRequiredWorkKind kind, long? reference = null)
        => plan.RequiredWork.Any(row => row.Kind == kind && (reference is null || row.Reference == reference));
    private static void ReadOnly(Fixture f, NativeSeekTargetStatePlan plan)
    {
        Check(!plan.CompleteSeekPermitted && f.Native.JudgeQueueWrites.Count == 0 && f.Native.FieldWrites == 0
            && f.Native.TargetForbiddenUpdates == 0 && f.Native.RendererCalls.Count == 0 && f.Native.GameplayCommandCalls == 0,
            "Target capture replayed native consumers, changed live state or authorized complete Seek.");
    }
}
