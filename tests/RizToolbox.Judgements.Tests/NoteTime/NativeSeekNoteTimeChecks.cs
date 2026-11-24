using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekNoteTimeChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Note time preparation is read-only and independently owns the actual complete NoteView", Preparation),
        ("Note time writer stores converted game seconds while preserving raw chart and geometry domains", ConvertedTime),
        ("Note time writer retains multiple actual instances partial coverage and empty actual sources", ActualCoverage),
        ("Note time writer leaves ordinary NaN tails untouched and requires finite type-consistent targets", TailAndFiniteTargets),
        ("Note time writer requires bounded exactly-once complete explicit target enumeration", InvalidTargets),
        ("Note time writer accepts original NaN payloads and restores exact negative-zero and raw Single bits", FloatingBits),
        ("Note time before and after store faults track and restore only the attempted prefix", NativeFaults),
        ("Note time inverse faults preserve conditional ownership and are never retried", InverseFaults),
        ("Note time third states and unattempted external changes cannot manufacture restoration", ThirdState),
        ("Note time every confirmation revokes prior success before entry or getter failure", Confirmation),
        ("Note time callback disposal and source withdrawal stop later stores and retain entered owner", CallbackWithdrawal),
        ("Note time construction and participant reentry reject before any nested native export", Reentry),
        ("Note time worker operations perform no IL2CPP exports and leave the main-thread owner usable", WrongThread),
        ("Note time writer rejects changed source type graph and exact mutable field metadata", InvalidSource),
        ("Note time final managed guard uses actual owner address without getters or scalar roots", FinalGuard),
        ("Note time unknown native free retains the exact public owner and never retries cleanup", UnknownFree)
    };
    private static readonly List<object> Retained = new();
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    private static Exception Reject(Action action)
    { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected note-time rejection."); }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend; internal readonly JudgementRunCapture Run;
        private readonly List<NativeSeekNoteTimeParticipant> _owners = new();
        private readonly HashSet<NativeSeekNoteTimeParticipant> _retained = new();
        internal Fixture(FakeNative.NoteViewShape shape = FakeNative.NoteViewShape.Complete,
            Action<FakeNative>? configure = null, bool empty = false)
        {
            Native.ConfigureNoteTime(shape); configure?.Invoke(Native); if (empty) Native.FlagEmptyRuntime(); Native.NoteTimeRemember();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-note-time", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No actual RunValue.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5, "Note-time TDD bypassed the real backend RunValue.");
        }
        internal IReadOnlyList<SeekRuntimeTimeTarget> Targets()
        {
            using var plan = Run.CaptureSeekTargetStatePlan(Native.TargetProfile, new(0), new());
            Check(!plan.CompleteSeekPermitted, "Timing targets granted a complete Seek.");
            return plan.RuntimeTimeTargets;
        }
        internal NativeSeekNoteTimeParticipant Prepare(IReadOnlyList<SeekRuntimeTimeTarget>? targets = null, ISeekChartNativeProfile? profile = null)
        { var p = Run.PrepareSeekNoteTime(profile ?? Native.TargetProfile, targets ?? Targets()); _owners.Add(p); return p; }
        internal void Clear()
        {
            Native.BeforeNoteTimeWrite = Native.AfterNoteTimeWrite = null; Native.FailNoteTimeBefore = Native.FailNoteTimeAfter = 0;
            Native.TargetProfile.OnVerify = null; Native.AfterTargetFieldRead = null; Native.AfterNoteViewFieldRead = null;
            Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null; Native.AfterFlagMetadata = null;
            Native.AfterJudgeMetadata = null; Native.AfterJudgeMethodResolution = null; Native.AfterJudgeRoot = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null; Native.DuringSeekConversion = null;
        }
        internal void Keep(NativeSeekNoteTimeParticipant p)
        {
            if (!_owners.Contains(p)) _owners.Add(p); _retained.Add(p);
            var before = Native.FreeHandleAttempts; var error = Reject(p.Dispose);
            Check(ReferenceEquals(error.Data["SeekNoteTimeCleanupOwner"], p) && p.HasRetainedResponsibility
                && Native.FreeHandleAttempts == before, "Unresolved note-time responsibility lost its exact public owner or retried cleanup.");
            Retained.Add((Native, p, error));
        }
        public void Dispose()
        {
            Clear();
            foreach (var p in _owners.AsEnumerable().Reverse())
            {
                if (_retained.Contains(p))
                {
                    var attempts = Native.FreeHandleAttempts; var error = Reject(p.Dispose);
                    Check(attempts == Native.FreeHandleAttempts && ReferenceEquals(error.Data["SeekNoteTimeCleanupOwner"], p),
                        "Repeated note-time disposal retried an unknown free or replaced the responsible owner.");
                }
                else p.Dispose();
            }
            Run.Dispose(); Backend.Dispose();
            Check(Native.NoteTimeOtherMatches() && Native.NoteTimeReceiversRooted && Native.NoteTimeViolations.Count == 0
                && Native.NoteViewDispatchViolations.Count == 0 && Native.JudgeWriteViolations.Count == 0
                && Native.JudgeQueueWrites.Count == 0 && Native.FlagWrites.Count == 0 && Native.FingerWrites.Count == 0
                && Native.RendererCalls.Count == 0 && Native.GameplayCommandCalls == 0 && Native.TargetForbiddenUpdates == 0
                && Native.ObjectFieldWrites == 0 && Native.AllGameplayReceiversRooted && Native.AllArrayReadsRooted,
                "Note-time writer changed type/source/ordinary-tail storage, replayed consumers or used unrooted receivers.");
            Check(Native.NewRoots == Native.FreedRoots + Native.ActiveRoots && (_retained.Count != 0 || Native.ActiveRoots == 0)
                && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(x => x.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "Note-time source roots were lost, leaked or freed more than once.");
            if (_retained.Count == 0) Native.Dispose();
        }
    }
    private static void Restore(Fixture f, NativeSeekNoteTimeParticipant p)
    {
        f.Clear(); p.Restore(); var result = p.ConfirmRestored();
        Check(result.OriginalStateRestored && !p.SideEffectsUnknown && f.Native.NoteTimeBaselineMatches(),
            "Exact original note-time raw storage was not restored.");
    }
    private static (IntPtr Owner, string Field, int Bits)[] Expected(Fixture f, IReadOnlyList<SeekRuntimeTimeTarget> targets)
    {
        var byReference = targets.ToDictionary(x => x.Reference);
        return f.Native.NoteTimeSlots.Select(slot => (slot.Owner, slot.Field,
            Bits(slot.Field == "realTime" ? byReference[slot.Owner.ToInt64()].RealTime : byReference[slot.Owner.ToInt64()].HoldEndRealTime!.Value))).ToArray();
    }
    private static void CheckTarget(Fixture f, IReadOnlyList<SeekRuntimeTimeTarget> targets)
    {
        Check(Expected(f, targets).All(x => f.Native.NoteTimeBits(x.Owner, x.Field) == x.Bits),
            "Actual selected Single cache bits differ from their explicit game-seconds targets.");
    }
    private static void Preparation()
    {
        using (var leases = new Fixture())
        {
            var originalRoots = leases.Native.ActiveRoots;
            var view = leases.Run.CaptureSeekRuntimeNoteView(leases.Native.TargetProfile);
            var leasedRoots = leases.Native.ActiveRoots;
            var owners = Enumerable.Range(0, 256).Select(_ => view.RetainPhysicalSource()).ToArray();
            Reject(() => view.RetainPhysicalSource()); view.VerifySource();
            view.Dispose(); var attempts = leases.Native.FreeHandleAttempts;
            Reject(view.VerifySource);
            Task.Run(() => Reject(owners[0].Dispose)).GetAwaiter().GetResult();
            Check(leases.Native.ActiveRoots == leasedRoots && leases.Native.FreeHandleAttempts == attempts,
                "Logical closure or worker lease disposal prematurely freed the complete captured source.");
            foreach (var owner in owners) owner.Dispose();
            var finalAttempts = leases.Native.FreeHandleAttempts;
            foreach (var owner in owners) owner.Dispose();
            view.Dispose();
            Check(leases.Native.ActiveRoots == originalRoots && leases.Native.FreeHandleAttempts == finalAttempts,
                "The final physical source lease did not release exactly once or repeated disposal retried frees.");
        }
        using var f = new Fixture(); var targets = f.Targets(); var p = f.Prepare(targets, f.Native.SeekProfile);
        IGameplayValueParticipant contract = p; var roots = f.Native.ActiveRoots;
        Check(roots > 0 && targets.Count == f.Native.NoteViewAllCount, "Preparation did not retain the complete actual source.");
        f.Run.Dispose(); p.VerifyPrepared();
        Check(contract == p && p.AttemptedWrites == 0 && !p.SideEffectsUnknown && f.Native.NoteTimeWrites.Count == 0
            && f.Native.ValueFieldWrites == 0 && f.Native.NoteTimeBaselineMatches() && f.Native.ActiveRoots > 0,
            "Independent preparation wrote a field or depended on disposed RunValue roots.");
        p.Dispose(); Check(f.Native.ActiveRoots == 0, "An unentered independent source did not release all its roots.");
    }
    private static void ConvertedTime()
    {
        using var f = new Fixture(configure: n => n.TargetSetChart(0, 2, 2, 8));
        var targets = f.Targets(); var first = targets.Single(x => x.Reference == f.Native.TargetNote(0).ToInt64());
        Check(first.RealTime == 1 && first.HoldEndRealTime == 4 && f.Native.TargetInt(f.Native.TargetData(0), "type") == 2,
            "The raw 2->8 chart was not explicitly converted to cache 1->4 game seconds.");
        var mutable = targets.Reverse().ToArray(); var p = f.Prepare(mutable);
        mutable[0] = mutable[0] with { RealTime = 12345 }; // Caller mutation cannot retarget an owned preparation.
        p.Commit(); Check(p.Confirm().Observed == true && !p.SideEffectsUnknown, "Converted note-time commit did not confirm.");
        CheckTarget(f, targets);
        Check(p.AttemptedWrites == f.Native.NoteTimeSlots.Count
            && f.Native.NoteTimeWrites.Select(x => (x.Owner, x.Field, x.Bits)).SequenceEqual(Expected(f, targets))
            && f.Native.TargetFloat(f.Native.TargetData(0), "time") == 2
            && f.Native.TargetFloat(f.Native.TargetNote(0), "realTime") == 1
            && f.Native.TargetFloat(f.Native.TargetNote(0), "holdEndRealTime") == 4,
            "Writer followed caller order, mixed raw chart time with game seconds or lost an actual slot.");
        Check(f.Native.NoteTimeOtherMatches(), "Timing writer changed raw chart tail/type or geometry/ready/consumer fields."); Restore(f, p);
    }
    private static void ActualCoverage()
    {
        foreach (var shape in new[] { FakeNative.NoteViewShape.MultipleSegments, FakeNative.NoteViewShape.Partial })
        {
            using var f = new Fixture(shape, n => n.TargetSetChart(0, 2, 2, 8)); var targets = f.Targets();
            Check(targets.Count == (shape == FakeNative.NoteViewShape.MultipleSegments ? 6 : 4), "Actual target cardinality was synthesized from chart ordinals.");
            if (shape == FakeNative.NoteViewShape.MultipleSegments)
                Check(f.Native.TargetData(0) == f.Native.TargetData(5) && targets[0].Reference != targets[5].Reference
                    && targets[0].RealTime == targets[5].RealTime && targets[0].HoldEndRealTime == targets[5].HoldEndRealTime,
                    "Two actual instances sharing raw chart data were merged.");
            var p = f.Prepare(targets.Reverse().ToArray()); f.Run.Dispose(); p.Commit();
            Check(p.Confirm().Observed == true && p.AttemptedWrites == Expected(f, targets).Length
                && f.Native.NoteTimeWrites.Select(x => (x.Owner, x.Field, x.Bits)).SequenceEqual(Expected(f, targets)),
                "Actual instance order or partial coverage was replaced by a full-chart synthetic target.");
            CheckTarget(f, targets); Restore(f, p);
        }
        using var empty = new Fixture(empty: true); var owner = empty.Prepare(Array.Empty<SeekRuntimeTimeTarget>());
        owner.VerifyPrepared(); owner.Commit(); Check(owner.Confirm().Observed == true && owner.AttemptedWrites == 0
            && empty.Native.NoteTimeWrites.Count == 0 && empty.Native.NoteTimeBaselineMatches(), "Empty actual source was not a zero-setter transaction."); Restore(empty, owner);
    }
    private static void TailAndFiniteTargets()
    {
        using var f = new Fixture(); var targets = f.Targets(); var ordinary = targets.First(x => x.HoldEndRealTime is null);
        var hold = targets.First(x => x.HoldEndRealTime is not null); var ordinaryNote = new IntPtr(ordinary.Reference);
        f.Native.TargetSet(ordinaryNote, "holdEndRealTime", BitConverter.Int32BitsToSingle(unchecked((int)0xffc12345)));
        f.Native.NoteTimeRemember(); var originalTail = f.Native.NoteTimeBits(ordinaryNote, "holdEndRealTime");
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(() => f.Prepare(targets.Select(x => x.Reference == ordinary.Reference ? x with { RealTime = bad } : x).ToArray()));
            Reject(() => f.Prepare(targets.Select(x => x.Reference == hold.Reference ? x with { HoldEndRealTime = bad } : x).ToArray()));
        }
        Reject(() => f.Prepare(targets.Select(x => x.Reference == hold.Reference ? x with { HoldEndRealTime = null } : x).ToArray()));
        foreach (var badTail in new[] { 0f, ordinary.RealTime, float.NaN })
            Reject(() => f.Prepare(targets.Select(x => x.Reference == ordinary.Reference ? x with { HoldEndRealTime = badTail } : x).ToArray()));
        Check(f.Native.NoteTimeWrites.Count == 0, "Nonfinite or type-inconsistent explicit timing targets reached a setter.");
        var p = f.Prepare(targets); p.Commit(); Check(p.Confirm().Observed == true, "Unused ordinary NaN tail blocked actual time writes.");
        Check(f.Native.NoteTimeBits(ordinaryNote, "holdEndRealTime") == originalTail
            && f.Native.NoteTimeWrites.All(x => x.Owner != ordinaryNote || x.Field != "holdEndRealTime"),
            "An unused ordinary tail was zeroed, normalized or written."); Restore(f, p);
    }
    private static void FloatingBits()
    {
        using var f = new Fixture(); var targets = f.Targets(); // Capture finite targets before poisoning original caches.
        var head = new IntPtr(targets[0].Reference); var hold = new IntPtr(targets.First(x => x.HoldEndRealTime is not null).Reference);
        var negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        f.Native.TargetSet(head, "realTime", BitConverter.Int32BitsToSingle(0x7fc12345));
        f.Native.TargetSet(hold, "realTime", negativeZero);
        f.Native.TargetSet(hold, "holdEndRealTime", BitConverter.Int32BitsToSingle(unchecked((int)0xffc54321)));
        f.Native.NoteTimeRemember(); var p = f.Prepare(targets); p.VerifyPrepared(); p.Commit();
        Check(p.Confirm().Observed == true, "A finite target could not replace an original NaN payload via complete NoteView access."); Restore(f, p);
        Check(f.Native.NoteTimeBits(head, "realTime") == 0x7fc12345
            && f.Native.NoteTimeBits(hold, "realTime") == unchecked((int)0x80000000)
            && f.Native.NoteTimeBits(hold, "holdEndRealTime") == unchecked((int)0xffc54321), "Original raw Single bits were numerically normalized.");
        var signedTargets = targets.Select(x => x.Reference == head.ToInt64() ? x with { RealTime = negativeZero } : x).ToArray();
        var signed = f.Prepare(signedTargets); signed.Commit(); Check(signed.Confirm().Observed == true
            && f.Native.NoteTimeBits(head, "realTime") == unchecked((int)0x80000000), "A finite negative-zero target lost its raw bits."); Restore(f, signed);
    }
}
