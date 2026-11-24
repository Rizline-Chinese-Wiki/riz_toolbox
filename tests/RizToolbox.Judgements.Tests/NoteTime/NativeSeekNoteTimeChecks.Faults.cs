using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekNoteTimeChecks
{
    private static void InvalidTargets()
    {
        using var f = new Fixture(); var targets = f.Targets(); var source = targets.ToArray(); var roots = f.Native.ActiveRoots;
        foreach (var rows in new[]
        {
            source.Take(1).ToArray(), source.Skip(1).ToArray(), source.Concat(new[] { source[0] }).ToArray(),
            source.Select((x, i) => i == 0 ? source[1] : x).ToArray(),
            source.Select((x, i) => i == 0 ? x with { Reference = 0 } : x).ToArray(),
            source.Select((x, i) => i == 0 ? x with { Reference = long.MaxValue } : x).ToArray(),
            source.Select((x, i) => i == 0 ? null! : x).ToArray()
        })
        {
            Reject(() => f.Prepare(rows));
            Check(f.Native.ActiveRoots == roots, "Rejected timing coverage leaked its complete independent source.");
        }
        var extra = new TrackedTargets(source.Concat(new[] { new SeekRuntimeTimeTarget(long.MaxValue, 1, null) }).ToArray(), source.Length);
        var shortList = new TrackedTargets(source.Take(1).ToArray(), source.Length);
        var endless = new TrackedTargets(source.Concat(new[] { source[0] }).ToArray(), source.Length, repeatLast: true);
        var throwing = new TrackedTargets(source, source.Length, throwAt: 2);
        foreach (var list in new[] { extra, shortList, endless, throwing })
        {
            Reject(() => f.Prepare(list));
            Check(list.Visits <= source.Length + 1 && f.Native.ActiveRoots == roots,
                "Dishonest target enumeration exceeded actual coverage plus one detection row or leaked roots.");
        }
        Check(extra.Enumerations == 1 && extra.Visits == source.Length + 1,
            "An extra enumerated timing target was hidden by IReadOnlyList.Count or its indexer.");
        Check(f.Native.NoteTimeWrites.Count == 0 && f.Native.NoteTimeBaselineMatches(), "Rejected complete timing targets reached a field store.");
        var complete = new TrackedTargets(source.Reverse().ToArray(), source.Length); var p = f.Prepare(complete);
        p.VerifyPrepared(); p.Commit(); Check(p.Confirm().Observed == true && complete.Enumerations == 1 && complete.Visits == source.Length,
            "Complete explicit timing targets were enumerated again after their owned snapshot was prepared."); Restore(f, p);
    }
    private sealed class TrackedTargets : IReadOnlyList<SeekRuntimeTimeTarget>
    {
        private readonly SeekRuntimeTimeTarget[] _rows; private readonly bool _repeatLast; private readonly int _throwAt;
        internal int Enumerations, Visits;
        internal TrackedTargets(SeekRuntimeTimeTarget[] rows, int count, bool repeatLast = false, int throwAt = -1)
        { _rows = rows; Count = count; _repeatLast = repeatLast; _throwAt = throwAt; }
        public int Count { get; }
        public SeekRuntimeTimeTarget this[int index] => _rows[index];
        public IEnumerator<SeekRuntimeTimeTarget> GetEnumerator()
        {
            ++Enumerations;
            for (var index = 0; index < _rows.Length || _repeatLast; ++index)
            {
                ++Visits;
                // This fuse makes an incorrectly unbounded implementation fail
                // an assertion rather than hanging the test process forever.
                if (Visits > Count + 1) throw new IOException("Test fuse: unbounded timing target enumeration.");
                if (index == _throwAt) throw new IOException("Synthetic target enumeration failure.");
                yield return _rows[Math.Min(index, _rows.Length - 1)];
            }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static void NativeFaults()
    {
        foreach (var after in new[] { false, true }) for (var slot = 1; slot <= 6; ++slot)
        {
            using var f = new Fixture(); var targets = f.Targets(); var expected = Expected(f, targets); var p = f.Prepare(targets);
            Check(expected.Length == 6, "The default actual source no longer has five heads and one Hold tail.");
            if (after) f.Native.FailNoteTimeAfter = slot; else f.Native.FailNoteTimeBefore = slot;
            var error = Reject(p.Commit);
            Check(p.AttemptedWrites == slot && f.Native.NoteTimeWrites.Count == slot && p.SideEffectsUnknown
                && ReferenceEquals(error.Data["SeekNoteTimeCleanupOwner"], p)
                && f.Native.NoteTimeWrites.Count(x => x.Stored) == (after ? slot : slot - 1),
                "A native timing fault lost its attempted prefix, actual store outcome or exact responsible owner.");
            for (var index = 0; index < expected.Length; ++index)
            {
                var row = expected[index]; var stored = index < slot - 1 || after && index == slot - 1;
                Check(f.Native.NoteTimeBits(row.Owner, row.Field) == (stored ? row.Bits : f.Native.NoteTimeOriginalBits(row.Owner, row.Field)),
                    "A native timing fault wrote an unattempted slot or fabricated a before-storage outcome.");
            }
            Reject(() => p.Confirm()); Restore(f, p);
            Check(p.AttemptedWrites == slot && f.Native.NoteTimeWrites.Skip(slot).All(x =>
                expected.Take(slot).Any(y => y.Owner == x.Owner && y.Field == x.Field)),
                "Timing rollback overwrote an unattempted slot or counted inverse stores as forward attempts.");
        }
    }
    private static void InverseFaults()
    {
        foreach (var after in new[] { false, true }) for (var inverseSlot = 1; inverseSlot <= 6; ++inverseSlot)
        {
            using var f = new Fixture(); var targets = f.Targets(); var expected = Expected(f, targets); var p = f.Prepare(targets);
            p.Commit(); Check(p.Confirm().Observed == true, "Forward timing confirmation failed before inverse injection.");
            var forward = f.Native.NoteTimeWrites.Count; var failAt = forward + inverseSlot;
            if (after) f.Native.FailNoteTimeAfter = failAt; else f.Native.FailNoteTimeBefore = failAt;
            p.Restore(); var writes = f.Native.NoteTimeWrites.Count; Reject(p.Restore);
            Check(writes == f.Native.NoteTimeWrites.Count && p.AttemptedWrites == forward
                && f.Native.NoteTimeWrites.Skip(forward).GroupBy(x => (x.Owner, x.Field)).All(x => x.Count() == 1),
                "A failed inverse store or once-only restoration was retried.");
            var result = p.ConfirmRestored(); var fault = expected[expected.Length - inverseSlot];
            Check(result.OriginalStateRestored == after
                && f.Native.NoteTimeBits(fault.Owner, fault.Field) == (after ? f.Native.NoteTimeOriginalBits(fault.Owner, fault.Field) : fault.Bits),
                "Inverse confirmation ignored actual after-storage restoration or overwrote a failed before-storage cache.");
            if (after) Check(f.Native.NoteTimeBaselineMatches() && !p.SideEffectsUnknown, "An after-storage inverse fault lost recoverable raw baseline bits.");
            else { Check(p.SideEffectsUnknown, "An unrestored inverse scalar was reported known."); f.Keep(p); }
        }
    }
    private static void ThirdState()
    {
        using (var f = new Fixture())
        {
            var targets = f.Targets(); var p = f.Prepare(targets); p.Commit(); Check(p.Confirm().Observed == true, "Initial timing confirmation failed.");
            var note = f.Native.TargetNote(0); f.Native.TargetSet(note, "realTime", 123456f);
            Check(p.Confirm().Observed == false && p.SideEffectsUnknown, "A third-state timing scalar retained stale forward success.");
            f.Native.TargetSet(note, "realTime", targets[0].RealTime); var count = f.Native.NoteTimeWrites.Count;
            p.Restore(); Check(!p.ConfirmRestored().OriginalStateRestored && f.Native.NoteTimeWrites.Count == count,
                "An observed third state stopped being sticky when an external writer put target bits back."); f.Keep(p);
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); p.Commit(); Check(p.Confirm().Observed == true, "Forward timing confirmation failed.");
            var tail = f.Native.NoteTimeSlots.First(x => x.Field == "holdEndRealTime"); f.Native.TargetSet(tail.Owner, tail.Field, 123456f);
            var count = f.Native.NoteTimeWrites.Count; p.Restore();
            Check(!p.ConfirmRestored().OriginalStateRestored && f.Native.NoteTimeBits(tail.Owner, tail.Field) == Bits(123456f)
                && f.Native.NoteTimeWrites.Count == count, "Rollback overwrote an external Hold-tail third state."); f.Keep(p);
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); var unattempted = f.Native.TargetNote(1);
            f.Native.AfterNoteTimeWrite = _ =>
            { f.Native.TargetSet(unattempted, "realTime", 123456f); throw new IOException("Foreign unattempted cache changed after first store."); };
            Reject(p.Commit); f.Clear(); var count = f.Native.NoteTimeWrites.Count; p.Restore();
            Check(p.AttemptedWrites == 1 && !p.ConfirmRestored().OriginalStateRestored
                && f.Native.NoteTimeBits(unattempted, "realTime") == Bits(123456f) && f.Native.NoteTimeWrites.Count == count,
                "Rollback fabricated responsibility for an unattempted externally changed cache."); f.Keep(p);
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Native.BeforeNoteTimeWrite = row =>
            { f.Native.TargetSet(row.Owner, row.Field, 123456f); throw new IOException("Third state before native store."); };
            Reject(p.Commit); f.Clear(); var count = f.Native.NoteTimeWrites.Count; p.Restore();
            Check(p.AttemptedWrites == 1 && !p.ConfirmRestored().OriginalStateRestored && f.Native.NoteTimeWrites.Count == count
                && f.Native.NoteTimeBits(f.Native.TargetNote(0), "realTime") == Bits(123456f),
                "A pending before-storage failure claimed an external third state as its own target."); f.Keep(p);
        }
    }
    private static void Confirmation()
    {
        foreach (var restoration in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); p.Commit(); Check(p.Confirm().Observed == true, "Initial timing confirmation failed.");
            if (restoration) Restore(f, p);
            f.State.ObservePause(true); f.Native.BeginLowerIl2CppAudit();
            try { if (restoration) Reject(() => p.ConfirmRestored()); else Reject(() => p.Confirm()); }
            finally { f.Native.EndLowerIl2CppAudit(); }
            Check(p.SideEffectsUnknown && f.Native.LowerIl2CppCalls == 0,
                "Managed confirmation entry failure kept prior success or dispatched a native export before rejection."); f.Keep(p);
        }
        foreach (var restoration in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); p.Commit(); Check(p.Confirm().Observed == true, "Forward timing confirmation failed.");
            if (restoration) Restore(f, p);
            var fired = false; f.Native.AfterTargetFieldRead = (name, owner) =>
            {
                if (owner != f.Native.TargetNote(0) || name != "realTime") return;
                fired = true; throw new IOException("Synthetic timing reconfirmation getter failure.");
            };
            if (restoration) Reject(() => p.ConfirmRestored()); else Reject(() => p.Confirm()); f.Clear();
            Check(fired && p.SideEffectsUnknown, "Reconfirmation getter failure did not revoke the old exact successful result."); f.Keep(p);
        }
        using var stable = new Fixture(); var writer = stable.Prepare(); writer.Commit();
        Check(writer.Confirm().Observed == true && writer.Confirm().Observed == true && !writer.SideEffectsUnknown,
            "Repeated independent forward readback did not confirm unchanged exact timing bits."); Restore(stable, writer);
        Check(writer.ConfirmRestored().OriginalStateRestored && !writer.SideEffectsUnknown,
            "Repeated baseline readback did not independently confirm unchanged original timing bits.");
    }
    private static void CallbackWithdrawal()
    {
        foreach (var before in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); var roots = f.Native.ActiveRoots;
            void Withdraw(FakeNative.NoteTimeWrite row)
            {
                p.Dispose(); Check(f.Native.ActiveRoots == roots && row.Ordinal == 1,
                    "Entered callback disposal freed active actual-note source roots or allowed a later setter.");
            }
            if (before) f.Native.BeforeNoteTimeWrite = Withdraw; else f.Native.AfterNoteTimeWrite = Withdraw;
            var error = Reject(p.Commit); f.Clear();
            Check(p.AttemptedWrites == 1 && f.Native.NoteTimeWrites.Count == 1 && f.Native.NoteTimeWrites[0].Stored
                && ReferenceEquals(error.Data["SeekNoteTimeCleanupOwner"], p) && p.HasRetainedResponsibility && f.Native.ActiveRoots > 0,
                "Callback withdrawal lost the entered store outcome, exact owner or source roots."); f.Keep(p);
        }
        foreach (var disposeBackend in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); f.Native.AfterNoteTimeWrite = _ =>
            { if (disposeBackend) f.Backend.Dispose(); else f.State.ObservePause(true); };
            Reject(p.Commit); f.Clear(); Check(p.AttemptedWrites == 1 && f.Native.NoteTimeWrites.Count == 1 && p.SideEffectsUnknown,
                "Source withdrawal after an entered timing store allowed later writes or reported known effects."); f.Keep(p);
        }
    }
}
