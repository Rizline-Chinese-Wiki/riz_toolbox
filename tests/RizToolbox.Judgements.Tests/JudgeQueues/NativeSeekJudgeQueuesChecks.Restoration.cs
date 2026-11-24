using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekJudgeQueuesChecks
{
    private static void RestorationFaults()
    {
        foreach (var operation in new[] { "SetValue", "_size", "_items", "_version" })
        foreach (var after in new[] { false, true })
        {
            using var f = new Fixture(); var r = f.Native.JudgeRuntimeReferences;
            var targets = f.Targets with { Notes = Enumerable.Range(0, 9).Select(index => r[index % r.Length]).ToArray() };
            var p = f.Prepare(targets); p.Commit(); var forward = p.AttemptedWrites;
            FakeNative.JudgeQueueWrite? failed = null;
            void Fault(FakeNative.JudgeQueueWrite row)
            {
                if (failed is not null || row.Operation != operation) return; failed = row;
                if (after) f.Native.FailJudgeQueueAfterStore = row.Ordinal;
                else f.Native.FailJudgeQueueBeforeStore = row.Ordinal;
            }
            if (after) f.Native.AfterJudgeQueueStore = Fault; else f.Native.BeforeJudgeQueueStore = Fault;
            p.Restore(); f.ClearCallbacks(); var restored = p.ConfirmRestored().OriginalStateRestored;
            Check(failed is not null && failed.Stored == after && p.AttemptedWrites == forward
                && !f.Native.JudgeQueueWrites.Any(row => row.Ordinal > failed.Ordinal && row.Operation == failed.Operation
                    && row.Receiver == failed.Receiver && row.Slot == failed.Slot)
                && restored == after && f.Native.JudgeQueueStorageEquals(f.Baseline) == after
                && p.SideEffectsUnknown == !after,
                "A restoration native before/after failure was blindly retried, attributed to forward writes or falsely confirmed: " + operation);
        }
    }
    private static void UnknownPartialStates()
    {
        foreach (var operation in new[] { "Clear", "Add", "SetValue", "_size", "_items", "_version" })
        {
            using var f = new Fixture(); var r = f.Native.JudgeRuntimeReferences;
            var targets = f.Targets with { Notes = Enumerable.Range(0, 9).Select(index => r[index % r.Length]).ToArray() };
            var p = f.Prepare(targets); var forward = operation is "Clear" or "Add";
            if (!forward) p.Commit();
            FakeNative.JudgeQueueWrite? failed = null;
            f.Native.BeforeJudgeQueueStore = row =>
            {
                if (failed is not null || row.Operation != operation) return; failed = row;
                switch (operation)
                {
                    case "Clear": f.Native.JudgeSetSlot(row.List, 0, IntPtr.Zero); break;
                    case "Add": f.Native.JudgeSetSlot(row.List, 0, new IntPtr(row.Requested)); break;
                    case "SetValue":
                        var previous = f.Native.JudgeQueueArrayCapacity(row.Receiver)[row.Slot!.Value].ToInt64();
                        var third = r.First(value => value != row.Requested && value != previous);
                        f.Native.JudgeQueueSetArraySlot(row.Receiver, row.Slot.Value, new IntPtr(third)); break;
                    case "_size": f.Native.JudgeSetList(row.List, "_size", 1); break;
                    case "_items": f.Native.JudgeReplaceBacking(row.List); break;
                    case "_version": f.Native.JudgeSetList(row.List, "_version", 1234567); break;
                }
                throw new IOException("Synthetic unrecognized partial native state.");
            };
            if (forward) Expect<IOException>(p.Commit); else p.Restore();
            f.ClearCallbacks(); var writes = f.Native.JudgeQueueWrites.Count;
            if (forward) TryRestore(p);
            Check(failed is not null && !failed.Stored && p.SideEffectsUnknown
                && !p.ConfirmRestored().OriginalStateRestored && f.Native.JudgeQueueWrites.Count == writes,
                "An unrecognized partial native/restore state was overwritten or falsely confirmed: " + operation);
        }
    }
    private static void ForeignGrowth()
    {
        foreach (var oldArray in new[] { false, true })
        {
            using var f = new Fixture(configure: n => n.JudgeQueueCapacity(0, 0));
            var r = f.Native.JudgeRuntimeReferences;
            var targets = f.Targets with { Notes = Enumerable.Repeat(r[0], oldArray ? 6 : 2).ToArray() };
            var p = f.Prepare(targets); var fired = false; IntPtr foreignArray = IntPtr.Zero;
            f.Native.AfterJudgeQueueStore = row =>
            {
                if (fired || row.Operation != "Add" || f.Native.JudgeQueueGrowths.Count != (oldArray ? 2 : 1)) return;
                fired = true;
                if (oldArray)
                {
                    foreignArray = f.Native.JudgeQueueGrowths[^1].OldBacking;
                    f.Native.JudgeQueueSetArraySlot(foreignArray, 0, new IntPtr(r[1]));
                }
                else { f.Native.JudgeReplaceBacking(0, 7); foreignArray = f.Native.JudgeBacking(0); }
            };
            Expect<InvalidOperationException>(p.Commit); f.ClearCallbacks(); var writes = f.Native.JudgeQueueWrites.Count;
            TryRestore(p);
            Check(fired && p.SideEffectsUnknown && !p.ConfirmRestored().OriginalStateRestored
                && f.Native.JudgeQueueWrites.Count == writes
                && (oldArray ? f.Native.JudgeQueueArrayCapacity(foreignArray)[0] == new IntPtr(r[1])
                    : f.Native.JudgeBacking(0) == foreignArray && f.Native.JudgeCapacity(0).Length == 7),
                "An unrecognized grown capacity or changed previous growth array was signed as owned and overwritten.");
        }
    }
}
