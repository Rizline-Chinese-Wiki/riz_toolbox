using System.Diagnostics;
using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekNoteTimeChecks
{
    private static bool InWriteExactCore() => new StackTrace(false).GetFrames().Any(x => x.GetMethod()?.Name == "WriteExactCore");
    private static void Reentry()
    {
        using var f = new Fixture(); var targets = f.Targets(); var fired = false;
        f.Native.TargetProfile.OnVerify = () =>
        {
            if (fired) return; fired = true; f.Native.BeginLowerIl2CppAudit();
            try { Reject(() => f.Prepare(targets)); } finally { f.Native.EndLowerIl2CppAudit(); }
            Check(f.Native.LowerIl2CppCalls == 0, "Same-RunValue timing construction reentry dispatched a native export.");
        };
        var p = f.Prepare(targets); f.Clear(); Check(fired, "Timing construction reentry callback was not exercised.");
        var callbacks = 0; f.Native.BeforeNoteTimeWrite = _ =>
        {
            ++callbacks; var stores = f.Native.NoteTimeWrites.Count; f.Native.BeginLowerIl2CppAudit();
            try
            {
                foreach (Action action in new Action[] { p.VerifyPrepared, p.Commit, () => p.Confirm(), p.Restore, () => p.ConfirmRestored() }) Reject(action);
            }
            finally { f.Native.EndLowerIl2CppAudit(); }
            Check(stores == f.Native.NoteTimeWrites.Count && f.Native.LowerIl2CppCalls == 0,
                "Busy timing participant admitted nested native reads, setters or restoration.");
        };
        p.Commit(); Check(callbacks == f.Native.NoteTimeSlots.Count && p.Confirm().Observed == true,
            "Rejected participant reentry consumed the outer timing commit or revoked its eventual success."); Restore(f, p);
    }
    private static void WrongThread()
    {
        using var f = new Fixture(); var targets = f.Targets(); var p = f.Prepare(targets); var stores = f.Native.NoteTimeWrites.Count;
        f.Native.BeginLowerIl2CppAudit();
        try
        {
            Task.Run(() =>
            {
                foreach (Action action in new Action[]
                { () => f.Prepare(targets), p.VerifyPrepared, p.Commit, () => p.Confirm(), p.Restore, () => p.ConfirmRestored(), p.Dispose }) Reject(action);
            }).GetAwaiter().GetResult();
        }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0 && p.AttemptedWrites == 0
            && f.Native.NoteTimeWrites.Count == stores && f.Native.NoteTimeBaselineMatches(),
            "Worker rejection performed native exports, consumed owner responsibility or wrote actual note storage.");
        p.VerifyPrepared(); p.Commit(); Check(p.Confirm().Observed == true, "Worker rejection invalidated a still-valid main-thread preparation.");
        f.Native.BeginLowerIl2CppAudit();
        try
        {
            Task.Run(() => { Reject(() => p.Confirm()); Reject(() => p.ConfirmRestored()); Reject(p.Dispose); }).GetAwaiter().GetResult();
        }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Native.LowerIl2CppCalls == 0 && !p.SideEffectsUnknown,
            "Rejected worker confirmation revoked the owner-thread's already successful exact confirmation."); Restore(f, p);
    }
    private static void InvalidSource()
    {
        foreach (var name in new[] { "realTime", "holdEndRealTime" })
        foreach (var fault in Enum.GetValues<FakeNative.NoteTimeMetadataFault>())
        foreach (var afterPreparation in new[] { false, true })
        {
            using var f = new Fixture(); var targets = f.Targets(); NativeSeekNoteTimeParticipant? p = null;
            if (afterPreparation) p = f.Prepare(targets); var roots = f.Native.ActiveRoots;
            f.Native.NoteTimeChangeMetadata(name, fault);
            if (afterPreparation) Reject(p!.Commit); else Reject(() => f.Prepare(targets));
            Check(f.Native.NoteTimeWrites.Count == 0 && f.Native.NoteTimeViolations.Count == 0
                && f.Native.ActiveRoots == roots && (p is null || p.AttemptedWrites == 0),
                "Non-exact Single/width/offset/parent/static/readonly timing metadata reached a setter or leaked new roots.");
        }
        foreach (var kind in new[] { "type", "noteData", "array", "ready" })
        {
            using var f = new Fixture(); var targets = f.Targets(); var p = f.Prepare(targets); var data = f.Native.TargetData(0);
            switch (kind)
            {
                case "type": f.Native.TargetSet(data, "type", 2); break;
                case "noteData": f.Native.TargetSet(f.Native.TargetNote(0), "noteData", f.Native.TargetData(1)); break;
                case "array": f.Native.NoteViewSwapAllSlots(); break;
                default: f.Native.SetGameplayField("progress.ready", false); break;
            }
            Reject(p.Commit); Check(p.AttemptedWrites == 0 && f.Native.NoteTimeWrites.Count == 0,
                "Changed complete actual source/type/array or non-ready progress reached a timing setter.");
            // Undo only these deliberate external fixture source mutations. The
            // expired participant is never used to repair or rearm its source.
            switch (kind)
            {
                case "type": f.Native.TargetSet(data, "type", 0); break;
                case "noteData": f.Native.TargetSet(f.Native.TargetNote(0), "noteData", data); break;
                case "array": f.Native.NoteViewSwapAllSlots(); break;
                default: f.Native.SetGameplayField("progress.ready", true); break;
            }
        }
        using (var invalidType = new Fixture())
        {
            var targets = invalidType.Targets(); var data = invalidType.Native.TargetData(0); var roots = invalidType.Native.ActiveRoots;
            invalidType.Native.TargetSet(data, "type", 3); Reject(() => invalidType.Prepare(targets));
            invalidType.Native.TargetSet(data, "type", 0);
            Check(invalidType.Native.NoteTimeWrites.Count == 0 && invalidType.Native.ActiveRoots == roots,
                "Unsupported raw chart type was admitted by the actual timing source.");
        }
        using var getter = new Fixture(); var writer = getter.Prepare(); var fired = false;
        getter.Native.AfterTargetFieldRead = (name, owner) =>
        { if (!fired && owner == getter.Native.TargetNote(0) && name == "realTime") { fired = true; getter.State.ObservePause(true); } };
        Reject(writer.Commit); getter.Clear(); Check(fired && writer.AttemptedWrites == 0 && getter.Native.NoteTimeWrites.Count == 0,
            "An actual getter callback source revision escaped the subsequent local lifetime guard.");
    }
    private static void FinalGuard()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); var visits = 0; var note = f.Native.TargetNote(0);
            f.Native.DuringHandleTarget = actualOwner =>
            {
                if (actualOwner != note || !InWriteExactCore()) return;
                // Scalar WriteExactCore consults the owner once for its exact
                // class and once for the final setter receiver. The callback
                // supplies an OBJECT ADDRESS, never its GC handle id.
                if (++visits == 2) f.State.ObservePause(true);
            };
            Reject(p.Commit); f.Clear(); Check(visits == 2 && p.AttemptedWrites == 0 && f.Native.NoteTimeWrites.Count == 0,
                "The final managed actual-owner guard accepted a source change at the last scalar receiver lookup.");
        }
        using (var f = new Fixture())
        {
            var targets = f.Targets(); var p = f.Prepare(targets); long exportsAtOwner = -1;
            var readsAtOwner = -1; var invokesAtOwner = -1; var scalarRoots = 0; var fullGraphInsideSelected = 0; var guardChecks = 0;
            f.Native.DuringHandleTarget = actualOwner =>
            {
                if (!InWriteExactCore()) return;
                Check(f.Native.NoteTimeSlots.Any(x => x.Owner == actualOwner), "Scalar writer resolved a foreign or temporary-root receiver.");
                exportsAtOwner = f.Native.LowerIl2CppCalls; readsAtOwner = f.Native.FieldReads; invokesAtOwner = f.Native.Invocations;
            };
            f.Native.AfterJudgeRoot = (_, _) => { if (InWriteExactCore()) ++scalarRoots; };
            f.Native.AfterNoteViewFieldRead = (name, owner) =>
            {
                if (name == "type" && f.Native.SeekNotes.Contains(owner)
                    && new StackTrace(false).GetFrames().Any(x => x.GetMethod()?.Name is "ReadSlot" or "WriteSlot")) ++fullGraphInsideSelected;
            };
            f.Native.BeforeNoteTimeWrite = _ =>
            {
                ++guardChecks;
                Check(exportsAtOwner >= 0 && f.Native.LowerIl2CppCalls == exportsAtOwner + 1
                    && f.Native.FieldReads == readsAtOwner && f.Native.Invocations == invokesAtOwner,
                    "A getter/metadata/native export was inserted between final owner lookup, managed guard and actual setter.");
            };
            f.Native.BeginLowerIl2CppAudit(); try { p.Commit(); } finally { f.Native.EndLowerIl2CppAudit(); }
            Check(p.Confirm().Observed == true && guardChecks == f.Native.NoteTimeSlots.Count && scalarRoots == 0 && fullGraphInsideSelected == 0,
                "Actual guarded scalar writes created temporary argument roots or performed full graph scans inside selected-slot access.");
            f.Native.BeginLowerIl2CppAudit(); try { p.Restore(); } finally { f.Native.EndLowerIl2CppAudit(); }
            Check(p.ConfirmRestored().OriginalStateRestored && f.Native.NoteTimeBaselineMatches()
                && guardChecks == f.Native.NoteTimeSlots.Count * 2 && scalarRoots == 0 && fullGraphInsideSelected == 0,
                "Conditional inverse scalar writes inserted getters, temporary roots or selected-access full graph scans."); f.Clear();
        }
        using var inverse = new Fixture(); var writer = inverse.Prepare(); writer.Commit();
        Check(writer.Confirm().Observed == true, "Forward timing commit failed before inverse final-guard injection.");
        var count = inverse.Native.NoteTimeWrites.Count; var visitsOnInverse = 0; var lastOwner = inverse.Native.NoteTimeSlots.Last().Owner;
        inverse.Native.DuringHandleTarget = actualOwner =>
        { if (actualOwner == lastOwner && InWriteExactCore() && ++visitsOnInverse == 2) inverse.State.ObservePause(true); };
        Reject(writer.Restore); inverse.Clear();
        Check(visitsOnInverse == 2 && inverse.Native.NoteTimeWrites.Count == count && writer.SideEffectsUnknown,
            "Inverse final guard allowed a store after the retained actual source changed."); inverse.Keep(writer);
    }
    private static void UnknownFree()
    {
        foreach (var committed in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare();
            if (committed) { p.Commit(); Check(p.Confirm().Observed == true, "Entered timing source did not confirm before unknown-free injection."); }
            f.Run.Dispose(); var note = f.Native.TargetNote(0);
            var handle = f.Native.NoteViewHandlesFor(note).Last(); f.Native.FailFreeHandle = handle; var error = Reject(p.Dispose);
            var failures = f.Native.NoteViewFreeLog.Where(x => x.InjectedFailure).ToArray();
            Check(p.HasRetainedResponsibility && ReferenceEquals(error.Data["SeekNoteTimeCleanupOwner"], p)
                && error.Data["SeekNoteTimeCleanupFailure"] is Exception
                && failures.Length == 1 && failures[0].Handle == handle && failures[0].Target == note && f.Native.ActiveRoots == 0,
                "An unknown actual-note native free lost its exact wrapper/target/public owner or blocked known root cleanup."); f.Keep(p);
        }
        using var construction = new Fixture(); var targets = construction.Targets(); var fired = false; uint failedHandle = 0;
        construction.Native.AfterTargetFieldRead = (name, owner) =>
        {
            if (fired || name != "realTime" || owner != construction.Native.TargetNote(0)) return;
            fired = true; failedHandle = construction.Native.NoteViewHandlesFor(owner).Last();
            construction.Native.FailFreeHandle = failedHandle; throw new IOException("Synthetic preparation failure with unknown source cleanup.");
        };
        var failure = Reject(() => construction.Prepare(targets)); construction.Clear();
        var retained = failure.Data["SeekNoteTimeCleanupOwner"] as NativeSeekNoteTimeParticipant;
        Check(fired && failedHandle != 0 && retained is not null && retained.HasRetainedResponsibility
            && failure.Data["SeekNoteTimeCleanupFailure"] is Exception
            && construction.Native.NoteTimeWrites.Count == 0
            && construction.Native.NoteViewFreeLog.Count(x => x.InjectedFailure && x.Handle == failedHandle
                && x.Target == construction.Native.TargetNote(0)) == 1,
            "Preparation with unknown source cleanup discarded the public timing participant or its exact native-free outcome."); construction.Keep(retained!);
    }
}
