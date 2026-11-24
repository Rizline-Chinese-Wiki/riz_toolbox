using System.Diagnostics;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static class NativeSeekJudgeInitializationChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Judge initialization preparation is read-only and owns the complete actual source", Preparation),
        ("Judge initialization writes caches flags count then ready and restores original bits", CommitAndRestore),
        ("Judge initialized sources are checked without any redundant initialization stores", InitializedNoOp),
        ("Judge initialization rejects missing duplicate foreign and inconsistent target coverage", InvalidTargets),
        ("Judge initialization requires finite target windows while preserving raw baseline bits", FloatingBits),
        ("Judge initialization partial native faults leave ready unpublished and restore the prefix", NativeFaults),
        ("Judge initialization does not overwrite an observed external third state", ThirdState),
        ("Judge initialization stops restoration when unattempted storage changes", UnattemptedChange),
        ("Judge initialization ready publication never precedes completed cache flag and count writes", ReadyLast),
        ("Judge initialization callback withdrawal stops later writes and retains entered owner", CallbackDispose),
        ("Judge initialization rejects same-run construction and participant reentry", Reentry),
        ("Judge initialization worker rejection performs no native exports", WrongThread),
        ("Judge initialization final managed field guard rejects source revision changes", FinalGuard),
        ("Judge initialization unknown native-free retains the exact owner without retry", UnknownFree),
        ("Judge initialization changed source and exact metadata reject before stores", InvalidSource),
        ("Judge initialization snapshots and repeated confirmation are independent", Confirmation)
    };
    private static readonly List<object> Retained = new();
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    private static Exception Reject(Action action) { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected initialization rejection."); }
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend; internal readonly JudgementRunCapture Run;
        private readonly List<NativeSeekJudgeInitializationParticipant> _owners = new();
        private readonly HashSet<NativeSeekJudgeInitializationParticipant> _retained = new();
        internal Fixture(Action<FakeNative>? configure = null)
        {
            Native.ConfigureJudgeInitialization(); configure?.Invoke(Native); Native.InitializationRemember();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-judge-initialization", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No actual RunValue.");
            Check(Run.GetType().Name == "RunValue", "The initialization tests bypassed the real backend.");
        }
        internal SeekJudgeInitializationTargets Targets()
        { using var p = Run.CaptureSeekTargetStatePlan(Native.TargetProfile, new(0), new()); return p.InitializationTargets; }
        internal NativeSeekJudgeInitializationParticipant Prepare(SeekJudgeInitializationTargets? targets = null)
        { var p = Run.PrepareSeekJudgeInitialization(Native.TargetProfile, targets ?? Targets()); _owners.Add(p); return p; }
        internal void Clear()
        {
            Native.BeforeInitializationWrite = Native.AfterInitializationWrite = null; Native.FailInitializationBefore = Native.FailInitializationAfter = 0;
            Native.TargetProfile.OnVerify = null; Native.AfterTargetFieldRead = null; Native.AfterNoteViewFieldRead = null;
            Native.AfterNoteViewObjectClass = null; Native.AfterJudgeRoot = null; Native.BeforeExport = null; Native.DuringHandleTarget = null;
        }
        internal void Keep(NativeSeekJudgeInitializationParticipant p)
        {
            _retained.Add(p); var error = Reject(p.Dispose);
            Check(ReferenceEquals(error.Data["SeekJudgeInitializationCleanupOwner"], p) && p.HasRetainedResponsibility,
                "Unknown initialization responsibility lost its public owner."); Retained.Add((Native, p));
        }
        public void Dispose()
        {
            Clear();
            foreach (var p in _owners.AsEnumerable().Reverse())
            {
                if (_retained.Contains(p)) { var attempts = Native.FreeHandleAttempts; Reject(p.Dispose); Check(attempts == Native.FreeHandleAttempts, "Repeated unknown free was retried."); }
                else p.Dispose();
            }
            Run.Dispose(); Backend.Dispose();
            Check(Native.InitializationOtherMatches() && Native.InitializationReceiversRooted && Native.NoteViewDispatchViolations.Count == 0
                && Native.JudgeQueueWrites.Count == 0 && Native.FlagWrites.Count == 0 && Native.RendererCalls.Count == 0 && Native.TargetForbiddenUpdates == 0,
                "Initialization changed unrelated storage, replayed consumers or used an unrooted setter receiver.");
            Check(Native.NewRoots == Native.FreedRoots + Native.ActiveRoots && (_retained.Count != 0 || Native.ActiveRoots == 0), "Initialization root bookkeeping changed.");
            if (_retained.Count == 0) Native.Dispose();
        }
    }
    private static void Restore(Fixture f, NativeSeekJudgeInitializationParticipant p)
    { f.Clear(); p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored && f.Native.InitializationBaselineMatches(), "Exact original initialization storage did not restore."); }
    private static void Preparation()
    { using var f = new Fixture(); var p = f.Prepare(); p.VerifyPrepared(); Check(p.AttemptedWrites == 0 && f.Native.InitializationWrites.Count == 0 && f.Native.InitializationBaselineMatches(), "Preparation wrote native storage."); }
    private static void CommitAndRestore()
    {
        using var f = new Fixture(); var target = f.Targets(); var p = f.Prepare(target); p.Commit(); Check(p.Confirm().Observed == true, "Initialization did not confirm.");
        Check(f.Native.TargetBoolean(f.Native.JudgeOwner, "_instantiateReady") && f.Native.TargetInt(f.Native.GameplayInformation, "challengeTimeComboNum") == 3
            && target.Notes.All(x => f.Native.TargetBoolean(new(x.Reference), "inChallengeTime") == x.InChallengeTime), "Explicit initialization target was not stored."); Restore(f, p);
    }
    private static void InitializedNoOp()
    {
        using var f = new Fixture(n => n.TargetSet(n.JudgeOwner, "_instantiateReady", true)); var p = f.Prepare(); p.Commit();
        Check(p.Confirm().Observed == true && p.AttemptedWrites == 0 && f.Native.InitializationWrites.Count == 0, "Initialized source was redundantly overwritten."); Restore(f, p);
    }
    private static void InvalidTargets()
    {
        using var f = new Fixture(); var t = f.Targets();
        foreach (var notes in new[] { t.Notes.Take(1).ToArray(), t.Notes.Concat(new[] { t.Notes[0] }).ToArray(), t.Notes.Select((x, i) => i == 0 ? x with { Reference = long.MaxValue } : x).ToArray() }) Reject(() => f.Prepare(t with { Notes = notes }));
        Reject(() => f.Prepare(t with { Notes = new ExtraFlagList(t.Notes.Concat(new[] { new SeekChallengeFlagTarget(long.MaxValue, true) }).ToArray()) }));
        Reject(() => f.Prepare(t with { RequiresInitialization = false }));
        Check(f.Native.InitializationWrites.Count == 0, "Invalid complete initialization coverage wrote a field.");
    }
    private static void FloatingBits()
    {
        using var f = new Fixture(n =>
        {
            n.TargetSet(n.JudgeOwner, "_judgeTime", BitConverter.Int32BitsToSingle(0x7fc12345));
            n.TargetSet(n.JudgeOwner, "badTime", BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)));
            n.TargetSet(n.GameplayInformation, "challengeTimeComboNum", int.MinValue);
        });
        var t = f.Targets(); Reject(() => f.Prepare(t with { Windows = t.Windows with { JudgeTime = float.NaN } }));
        var p = f.Prepare(t); p.Commit(); Check(p.Confirm().Observed == true, "Finite initialization targets did not commit."); Restore(f, p);
    }
    private static void NativeFaults()
    {
        foreach (var after in new[] { false, true }) foreach (var slot in new[] { 1, 5, 10, 11 })
        {
            using var f = new Fixture(); var p = f.Prepare();
            if (after) f.Native.FailInitializationAfter = slot; else f.Native.FailInitializationBefore = slot;
            Reject(p.Commit);
            Check(f.Native.TargetBoolean(f.Native.JudgeOwner, "_instantiateReady") == (after && slot == 11)
                && p.AttemptedWrites == slot, "Partial initialization hid an actual ready store or lost the attempted prefix.");
            Reject(() => p.Confirm()); Restore(f, p);
        }
    }
    private static void ThirdState()
    {
        foreach (var after in new[] { false, true })
        {
            using var inverse = new Fixture(); var writer = inverse.Prepare(); writer.Commit();
            Check(writer.Confirm().Observed == true, "Forward initialization failed before inverse fault.");
            var next = inverse.Native.InitializationWrites.Count + 1;
            if (after) inverse.Native.FailInitializationAfter = next; else inverse.Native.FailInitializationBefore = next;
            writer.Restore();
            Check(writer.ConfirmRestored().OriginalStateRestored == after,
                "A failed ready clear either fabricated recovery or ignored actual after-storage restoration.");
            if (after) Check(inverse.Native.InitializationBaselineMatches(), "After-storage ready failure lost the complete original state.");
            else
            {
                Check(inverse.Native.InitializationWrites.Count == next && inverse.Native.TargetBoolean(inverse.Native.JudgeOwner, "_instantiateReady"),
                    "Other inverse fields changed while the ready flag was still published.");
                inverse.Keep(writer);
            }
        }
        using var f = new Fixture(); var p = f.Prepare(); p.Commit(); Check(p.Confirm().Observed == true, "Initial confirmation failed.");
        f.Native.TargetSet(f.Native.GameplayInformation, "challengeTimeComboNum", 123456); p.Restore();
        Check(!p.ConfirmRestored().OriginalStateRestored && f.Native.TargetInt(f.Native.GameplayInformation, "challengeTimeComboNum") == 123456,
            "An external third state was overwritten."); f.Keep(p);
    }
    private static void UnattemptedChange()
    {
        using var f = new Fixture(); var p = f.Prepare();
        f.Native.AfterInitializationWrite = _ => { f.Native.TargetSet(f.Native.GameplayInformation, "challengeTimeComboNum", 123456); throw new IOException("stop after foreign change"); };
        Reject(p.Commit); f.Clear(); p.Restore();
        Check(!p.ConfirmRestored().OriginalStateRestored && f.Native.TargetInt(f.Native.GameplayInformation, "challengeTimeComboNum") == 123456,
            "Unattempted external storage was overwritten during rollback."); f.Keep(p);
    }
    private static void ReadyLast()
    {
        using var f = new Fixture(); var t = f.Targets(); var p = f.Prepare(t);
        f.Native.BeforeInitializationWrite = row =>
        {
            if (row.Field == "_instantiateReady" && row.Bits == 1)
                Check(f.Native.TargetInt(f.Native.GameplayInformation, "challengeTimeComboNum") == t.ChallengeComboCount
                    && t.Notes.All(x => f.Native.TargetBoolean(new(x.Reference), "inChallengeTime") == x.InChallengeTime),
                    "Ready preceded complete flag/count publication.");
        };
        p.Commit(); Check(f.Native.InitializationWrites.Last().Field == "_instantiateReady", "Ready was not the last forward write."); Restore(f, p);
    }
    private static void CallbackDispose()
    {
        using var f = new Fixture(); var p = f.Prepare(); f.Native.AfterInitializationWrite = _ => p.Dispose(); Reject(p.Commit);
        Check(p.AttemptedWrites == 1 && f.Native.InitializationWrites.Count == 1 && !f.Native.TargetBoolean(f.Native.JudgeOwner, "_instantiateReady"), "Withdrawal allowed later native writes."); f.Keep(p);
    }
    private static void Reentry()
    {
        using var f = new Fixture(); var t = f.Targets(); var fired = false;
        f.Native.TargetProfile.OnVerify = () =>
        {
            if (fired) return; fired = true; var reads = f.Native.FieldReads;
            Reject(() => f.Prepare(t)); Check(reads == f.Native.FieldReads, "Same-run preparation reentry read fields.");
        };
        var p = f.Prepare(t); f.Native.TargetProfile.OnVerify = null;
        f.Native.BeforeInitializationWrite = _ =>
        { var count = f.Native.InitializationWrites.Count; Reject(p.Commit); Check(count == f.Native.InitializationWrites.Count, "Commit reentry entered a setter."); };
        p.Commit(); Check(fired, "Construction reentry was not exercised."); Restore(f, p);
    }
    private static void WrongThread()
    {
        using var f = new Fixture(); var p = f.Prepare(); var reads = f.Native.FieldReads; var invokes = f.Native.Invocations;
        Task.Run(() => Reject(p.Commit)).GetAwaiter().GetResult();
        Check(reads == f.Native.FieldReads && invokes == f.Native.Invocations && p.AttemptedWrites == 0, "Worker rejection came after exports.");
    }
    private static void FinalGuard()
    {
        using var f = new Fixture(); var p = f.Prepare(); var fired = false;
        f.Native.DuringHandleTarget = target =>
        {
            if (!fired && target == f.Native.JudgeOwner
                && new StackTrace(false).GetFrames().Any(x => x.GetMethod()?.Name == "WriteExactCore"))
            { fired = true; f.State.ObservePause(true); }
        };
        Reject(p.Commit); f.Clear();
        Check(fired && p.AttemptedWrites == 0 && f.Native.InitializationWrites.Count == 0, "Final managed guard accepted a changed source.");
    }
    private static void UnknownFree()
    {
        using var f = new Fixture(); var p = f.Prepare();
        f.Native.FailFreeHandle = f.Native.NoteViewHandlesFor(f.Native.JudgeOwner).Last(); Reject(p.Dispose); f.Keep(p);
    }
    private sealed class ExtraFlagList : IReadOnlyList<SeekChallengeFlagTarget>
    {
        private readonly SeekChallengeFlagTarget[] _rows;
        internal ExtraFlagList(SeekChallengeFlagTarget[] rows) => _rows = rows;
        public int Count => _rows.Length - 1;
        public SeekChallengeFlagTarget this[int index] => _rows[index];
        public IEnumerator<SeekChallengeFlagTarget> GetEnumerator() => ((IEnumerable<SeekChallengeFlagTarget>)_rows).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static void InvalidSource()
    {
        using (var metadata = new Fixture())
        {
            var targets = metadata.Targets(); metadata.Native.InitializationFieldOffset("_judgeTime", 0x58);
            Reject(() => metadata.Prepare(targets));
            Check(metadata.Native.InitializationWrites.Count == 0, "Wrong exact initialization field offset reached a store.");
        }
        using var f = new Fixture(); var p = f.Prepare(); f.Native.TargetSet(f.Native.JudgeOwner, "levelInformation", f.Native.OtherObject);
        Reject(p.Commit); Check(f.Native.InitializationWrites.Count == 0, "Foreign Judge source reached a setter.");
        // Undo this deliberate external fixture mutation; the expired source itself is not rearmed.
        f.Native.TargetSet(f.Native.JudgeOwner, "levelInformation", f.Native.GameplayInformation);
    }
    private static void Confirmation()
    {
        using (var expired = new Fixture())
        {
            var writer = expired.Prepare(); writer.Commit();
            Check(writer.Confirm().Observed == true, "Forward confirmation failed before entry rejection.");
            Restore(expired, writer);
            var reads = expired.Native.FieldReads; var invokes = expired.Native.Invocations;
            expired.State.ObservePause(true);
            Reject(() => writer.ConfirmRestored());
            Check(writer.SideEffectsUnknown && expired.Native.FieldReads == reads && expired.Native.Invocations == invokes,
                "Restoration reconfirmation did not fail at the managed source entry.");
            expired.Keep(writer);
        }
        using (var restored = new Fixture())
        {
            var writer = restored.Prepare(); writer.Commit(); Check(writer.Confirm().Observed == true, "Forward confirmation failed.");
            Restore(restored, writer);
            restored.Native.AfterTargetFieldRead = (name, owner) =>
            { if (owner == restored.Native.JudgeOwner && name == "_judgeTime") throw new IOException("Reconfirmation getter fault."); };
            Reject(() => writer.ConfirmRestored()); restored.Clear();
            Check(writer.SideEffectsUnknown, "Failed restoration reconfirmation kept its stale successful result.");
            restored.Keep(writer);
        }
        using var f = new Fixture(); var p = f.Prepare(); p.Commit(); Check(p.Confirm().Observed == true, "Initial confirmation failed.");
        f.Native.TargetSet(f.Native.GameplayInformation, "challengeTimeComboNum", 123456);
        Check(p.Confirm().Observed == false, "Repeated confirmation used stale success."); p.Restore();
        Check(!p.ConfirmRestored().OriginalStateRestored, "A third state regained fake confirmation."); f.Keep(p);
    }
}
