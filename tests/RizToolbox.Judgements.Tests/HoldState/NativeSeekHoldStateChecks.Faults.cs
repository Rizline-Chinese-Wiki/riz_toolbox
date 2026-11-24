using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekHoldStateChecks
{
    private static void ForwardFaults()
    {
        // Both sides of each actual official list/scalar dispatch, not a mocked
        // access implementation. Every recoverable prefix restores all storage.
        for (var ordinal = 1; ordinal <= 16; ++ordinal)
        foreach (var after in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare();
            if (after) f.Native.FailHoldStateAfter = ordinal; else f.Native.FailHoldStateBefore = ordinal;
            Expect<IOException>(p.Commit);
            Check(p.AttemptedWrites == ordinal && p.SideEffectsUnknown && p.Failure is not null
                && f.Native.HoldStateWrites.Count == ordinal && f.Native.HoldStateWrites[^1].Stored == after,
                "Hold forward fault lost its exact conservative dispatch prefix: " + ordinal);
            f.ClearCallbacks(); Restored(f, p);
        }
    }

    private static void RestoreFaults()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare();
            f.Native.BeforeHoldStateStore = _ => throw new InvalidOperationException("Untouched Hold restoration must not dispatch a native mutation.");
            p.Restore();
            Check(p.ConfirmRestored().OriginalStateRestored && p.AttemptedWrites == 0 && f.Native.HoldStateWrites.Count == 0
                && f.Native.HoldStateEquals(f.Baseline) && !p.SideEffectsUnknown,
                "Restore before Commit changed untouched actual Hold storage or failed to confirm the complete baseline.");
            f.ClearCallbacks(); p.Dispose();
        }
        foreach (var operation in new[] { "SetValue", "_size", "_size.publish", "_items", "_version", "isStaticNote", "lastFrameHoldLineLength", "_nowTime", "_speed", "_holdLineLength" })
        foreach (var after in new[] { false, true })
        {
            using var f = new Fixture(); var target = f.Targets with { HoldNotes = Enumerable.Repeat(f.Targets.HoldNotes[0], 9).ToArray() };
            var p = f.Prepare(target); p.Commit(); var committed = f.Native.HoldStateWrites.Count; var attempts = p.AttemptedWrites;
            FakeNative.HoldStateWrite? failure = null;
            void Inject(FakeNative.HoldStateWrite row)
            {
                var name = operation == "_size.publish" ? "_size" : operation;
                if (failure is not null || row.Operation != name || operation == "_size.publish" && row.Requested == 0) return; failure = row;
                if (after) f.Native.FailHoldStateAfter = row.Ordinal; else f.Native.FailHoldStateBefore = row.Ordinal;
            }
            if (after) f.Native.AfterHoldStateStore = Inject; else f.Native.BeforeHoldStateStore = Inject;
            TryRestore(p); f.ClearCallbacks();
            var restored = p.ConfirmRestored().OriginalStateRestored;
            Check(failure is not null && failure.Stored == after && p.AttemptedWrites == attempts
                && f.Native.HoldStateWrites.Skip(committed).Count(row => row.Operation == failure.Operation
                    && row.Receiver == failure.Receiver && row.Slot == failure.Slot && row.Requested == failure.Requested) == 1
                && restored == after && f.Native.HoldStateEquals(f.Baseline) == after && p.SideEffectsUnknown == !after,
                "Hold inverse fault was retried, counted as forward work or falsely confirmed: " + operation);
            if (!after)
            {
                var writes = f.Native.HoldStateWrites.Count; TryRestore(p);
                Check(f.Native.HoldStateWrites.Count == writes && !p.ConfirmRestored().OriginalStateRestored,
                    "A refused once-only Hold inverse was blindly retried."); f.RetainState(p);
            }
        }
    }

    private static void InternalPartial()
    {
        foreach (var operation in new[] { "Clear", "Add" })
        {
            using var f = new Fixture(); var p = f.Prepare(); FakeNative.HoldStateWrite? partial = null;
            f.Native.DuringHoldStateStore = (row, stage) =>
            {
                if (partial is not null || row.Operation != operation || stage != (operation == "Clear" ? "slots" : "slot")) return;
                partial = row; throw new IOException("Synthetic internal partial official Hold List operation.");
            };
            var failure = Expect<IOException>(p.Commit); f.ClearCallbacks(); var changed = f.Native.HoldStateSnapshot(); var writes = f.Native.HoldStateWrites.Count;
            TryRestore(p);
            Check(partial is not null && !partial.Stored && p.SideEffectsUnknown && RestorationDenied(p)
                && f.Native.HoldStateWrites.Count == writes && f.Native.HoldStateEquals(changed),
                "Internal partial Hold List storage was accepted as a complete before/after effect or overwritten.");
            f.RetainState(p, failure);
        }
    }

    private static void ExternalChanges()
    {
        foreach (var mutation in new[] { "tail", "size", "version", "backing", "note.bool", "note.float", "control.now", "control.speed", "control.length" })
        {
            using var f = new Fixture(); var p = f.Prepare(); p.Commit();
            var n = f.Native;
            switch (mutation)
            {
                case "tail": n.HoldStateSetSlot(n.HoldStateList(1).Backing, 4, n.HoldStateNotes[0]); break;
                case "size": n.TargetSet(n.TargetHoldLists[1], "_size", 0); break;
                case "version": n.TargetSet(n.TargetHoldLists[1], "_version", 123456789); break;
                case "backing": n.HoldStateReplaceBacking(1); break;
                case "note.bool": n.TargetSet(n.HoldStateNotes[0], "isStaticNote", true); break;
                case "note.float": n.TargetSet(n.HoldStateNotes[0], "lastFrameHoldLineLength", 912.5f); break;
                case "control.now": n.TargetSet(n.TargetHold, "_nowTime", 912.5f); break;
                case "control.speed": n.TargetSet(n.TargetHold, "_speed", 912.5f); break;
                case "control.length": n.TargetSet(n.TargetHold, "_holdLineLength", 912.5f); break;
            }
            var changed = n.HoldStateSnapshot(); var writes = n.HoldStateWrites.Count; TryRestore(p);
            Check(p.SideEffectsUnknown && !p.ConfirmRestored().OriginalStateRestored && n.HoldStateWrites.Count == writes && n.HoldStateEquals(changed),
                "Hold restore overwrote an external source value or complete storage change: " + mutation); f.RetainState(p);
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); p.Commit(); var fired = false;
            f.Native.AfterHoldStateStore = row =>
            {
                if (fired || row.Operation != "_version" || row.List != 0) return;
                fired = true; f.Native.TargetSet(f.Native.TargetHold, "_nowTime", 912.5f);
            };
            Expect<InvalidOperationException>(p.Restore); f.ClearCallbacks();
            Check(fired && !p.ConfirmRestored().OriginalStateRestored && p.SideEffectsUnknown,
                "A third state observed after the final inverse setter was incorrectly confirmed.");
            f.Native.TargetSet(f.Native.TargetHold, "_nowTime", BitConverter.Int32BitsToSingle(f.Baseline.NowBits));
            Check(f.Native.HoldStateEquals(f.Baseline) && !p.ConfirmRestored().OriginalStateRestored && p.SideEffectsUnknown,
                "An externally reconstructed baseline washed away previously observed unknown Hold ownership."); f.RetainState(p);
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); p.Commit(); p.Restore();
            Check(f.Native.HoldStateEquals(f.Baseline), "The confirmation-only third-state fixture did not complete physical restoration first.");
            f.Native.TargetSet(f.Native.TargetHold, "_nowTime", 913.5f);
            Check(!p.ConfirmRestored().OriginalStateRestored && p.SideEffectsUnknown,
                "Independent ConfirmRestored failed to notice a third state after a completed inverse sequence.");
            f.Native.TargetSet(f.Native.TargetHold, "_nowTime", BitConverter.Int32BitsToSingle(f.Baseline.NowBits));
            Check(f.Native.HoldStateEquals(f.Baseline) && !p.ConfirmRestored().OriginalStateRestored && p.SideEffectsUnknown,
                "Confirmation-only observation of a third state was washed away by an externally reconstructed baseline."); f.RetainState(p);
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); p.Commit(); p.Restore(); var writes = f.Native.HoldStateWrites.Count;
            f.Native.HoldStateSetSlot(f.Baseline.Lists[0].Backing, 0, f.Native.TargetNote(1));
            ExpectFailure(() => p.ConfirmRestored());
            Check(p.SideEffectsUnknown, "Malformed actual Hold active storage did not retain unknown responsibility on confirmation failure.");
            f.Native.HoldStateSetSlot(f.Baseline.Lists[0].Backing, 0, f.Baseline.Lists[0].Capacity[0]);
            Check(f.Native.HoldStateEquals(f.Baseline), "The external active-prefix repair did not reconstruct the physical baseline.");
            ExpectFailure(() => p.ConfirmRestored());
            Check(p.SideEffectsUnknown && f.Native.HoldStateWrites.Count == writes,
                "An exception-observed malformed actual Hold source was re-admitted or triggered mutation after external repair."); f.RetainState(p);
        }
        foreach (var priorRestorationReceipt in new[] { false, true })
        foreach (var nativeShape in new[] { true, false })
        {
            using var f = new Fixture(); var p = f.Prepare(); p.Commit();
            if (priorRestorationReceipt)
            {
                p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored,
                    "The prior-restoration-receipt fixture failed to confirm its complete original state.");
            }
            else Check(p.Confirm().Observed == true, "The prior-commit-receipt fixture failed to confirm its explicit target state.");
            var confirmed = f.Native.HoldStateSnapshot(); var list = confirmed.Lists[0]; var writes = f.Native.HoldStateWrites.Count;
            if (nativeShape) f.Native.HoldStateSetSlot(list.Backing, 0, f.Native.TargetNote(1));
            else f.State.ObservePause(true);
            var firstFailure = ExpectFailure(() =>
            {
                if (priorRestorationReceipt) p.ConfirmRestored(); else p.Confirm();
            });
            var firstUnknown = p.SideEffectsUnknown;
            if (nativeShape) f.Native.HoldStateSetSlot(list.Backing, 0, list.Capacity[0]);
            var physicallyRepaired = f.Native.HoldStateEquals(confirmed);
            var secondFailure = ExpectFailure(() =>
            {
                if (priorRestorationReceipt) p.ConfirmRestored(); else p.Confirm();
            });
            var stillUnknown = p.SideEffectsUnknown; var unchangedWrites = f.Native.HoldStateWrites.Count == writes;
            f.RetainState(p, firstFailure);
            Check(firstUnknown && stillUnknown && physicallyRepaired && unchangedWrites
                && ReferenceEquals(firstFailure.Data["SeekHoldStateCleanupOwner"], p)
                && ReferenceEquals(secondFailure.Data["SeekHoldStateCleanupOwner"], p),
                "A previously successful Hold confirmation receipt authorized root release after a later source rejection: "
                + (priorRestorationReceipt ? "ConfirmRestored" : "Confirm") + (nativeShape ? " native shape" : " initial managed revision"));
        }
    }

    private static void ForeignGrowth()
    {
        foreach (var intermediate in new[] { false, true })
        {
            using var f = new Fixture(configure: n => n.HoldStateCapacity(0, 0)); var good = f.Targets;
            var target = good with { HoldNotes = Enumerable.Repeat(good.HoldNotes[0], intermediate ? 6 : 2).ToArray() };
            var p = f.Prepare(target); var fired = false; var injectedDifferentStorage = false; IntPtr changedArray = IntPtr.Zero;
            f.Native.AfterHoldStateStore = row =>
            {
                if (fired || row.Operation != "Add" || f.Native.HoldStateGrowths.Count != (intermediate ? 2 : 1)) return;
                fired = true;
                if (intermediate)
                {
                    changedArray = f.Native.HoldStateGrowths[^1].Before;
                    var old = f.Native.HoldStateCapacity(changedArray)[0];
                    var foreign = f.Native.HoldStateNotes.First(reference => reference != old);
                    f.Native.HoldStateSetSlot(changedArray, 0, foreign);
                    injectedDifferentStorage = foreign != old && f.Native.HoldStateCapacity(changedArray)[0] == foreign;
                }
                else
                {
                    var old = f.Native.HoldStateList(0); f.Native.HoldStateReplaceBacking(0, 7);
                    var changed = f.Native.HoldStateList(0); changedArray = changed.Backing;
                    injectedDifferentStorage = changed.Backing != old.Backing && old.Capacity.Count == 4 && changed.Capacity.Count == 7
                        && changed.Size == old.Size && changed.Version == old.Version
                        && changed.Capacity.Take(changed.Size).SequenceEqual(old.Capacity.Take(old.Size));
                }
            };
            try
            {
                var commitFailure = ExpectFailure(p.Commit); f.ClearCallbacks(); var writes = f.Native.HoldStateWrites.Count;
                var capacity = f.Native.HoldStateCapacity(changedArray); TryRestore(p);
                var unknown = p.SideEffectsUnknown; var denied = !p.ConfirmRestored().OriginalStateRestored;
                var untouched = f.Native.HoldStateWrites.Count == writes && f.Native.HoldStateCapacity(changedArray).SequenceEqual(capacity);
                f.RetainState(p, commitFailure);
                Check(commitFailure is InvalidOperationException && fired && injectedDifferentStorage && unknown && denied && untouched,
                    "An unrecognized Hold growth or changed historical backing was not injected, admitted or overwritten: "
                    + (intermediate ? "historical backing slot" : "grown capacity seven"));
            }
            catch (Exception primary)
            {
                f.ClearCallbacks(); f.RetainState(p, primary); throw;
            }
        }
    }
}
