using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekJudgeQueuesChecks
{
    private static void PartialWrites()
    {
        foreach (var after in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare();
            if (after) f.Native.FailJudgeQueueAfterStore = 7; else f.Native.FailJudgeQueueBeforeStore = 7;
            Expect<IOException>(p.Commit);
            Check(p.AttemptedWrites == 7 && p.SideEffectsUnknown && p.Failure is not null
                && f.Native.JudgeQueueWrites.Count == 7 && f.Native.JudgeQueueWrites[^1].Stored == after,
                "A multi-list native fault lost its conservative exact attempted prefix.");
            f.ClearCallbacks(); Restored(f, p);
        }
    }
    private static void TryRestore(NativeSeekJudgeQueuesParticipant participant)
    {
        try { participant.Restore(); } catch (InvalidOperationException) { }
    }
    private static void UnattemptedForeignStorage()
    {
        using var f = new Fixture(); var p = f.Prepare(); f.Native.FailJudgeQueueAfterStore = 2;
        Expect<IOException>(p.Commit); f.ClearCallbacks();
        var foreign = new IntPtr(f.Native.JudgeRuntimeReferences[2]); f.Native.JudgeSetSlot(2, 2, foreign);
        var writes = f.Native.JudgeQueueWrites.Count; TryRestore(p);
        Check(f.Native.JudgeCapacity(2)[2] == foreign && f.Native.JudgeQueueWrites.Count == writes && p.SideEffectsUnknown,
            "Restoration overwrote foreign unattempted capacity storage or proceeded past its conditional snapshot.");
    }
    private static void RestoredForeignStorage()
    {
        using var f = new Fixture(); var p = f.Prepare(); p.Commit(); var committed = f.Native.JudgeQueueWrites.Count;
        FakeNative.JudgeQueueWrite? changed = null; var foreign = new IntPtr(f.Native.JudgeRuntimeReferences[2]);
        f.Native.AfterJudgeQueueStore = row =>
        {
            if (changed is not null || row.Operation != "SetValue" || row.Slot is not { } slot) return;
            changed = row;
            if (new IntPtr(row.Requested) == foreign) foreign = new IntPtr(f.Native.JudgeRuntimeReferences[3]);
            f.Native.JudgeSetSlot(row.List, slot, foreign);
        };
        TryRestore(p); f.ClearCallbacks();
        Check(changed is not null && f.Native.JudgeCapacity(changed.List)[changed.Slot!.Value] == foreign
            && f.Native.JudgeQueueWrites.Skip(committed).Count(row => row.Operation == "SetValue"
                && row.Receiver == changed.Receiver && row.Slot == changed.Slot) == 1 && p.SideEffectsUnknown,
            "Capacity changed by another writer after restoration was overwritten again or falsely trusted.");
    }
    private static void FinalDispatchGuard()
    {
        using var f = new Fixture(); var p = f.Prepare(); var fired = false;
        f.Native.AfterJudgeRoot = (_, target) =>
        {
            // The small guard wrapper may be JIT-inlined. InvokeCore roots this
            // exact List receiver only for Clear/Add; reads invoke arrays.
            // Zero attempts and zero writes still require final-guard rejection.
            if (fired || target != f.Native.JudgeLists[0] || !InMethod("InvokeCore", "NativeRuntime")) return;
            fired = true; f.State.ObservePause(true);
        };
        Expect<InvalidOperationException>(p.Commit); f.ClearCallbacks();
        Check(fired && p.AttemptedWrites == 0 && f.Native.JudgeQueueWrites.Count == 0
            && f.Native.JudgeQueueStorageEquals(f.Baseline),
            "A source revision changed during native receiver rooting but crossed the final managed dispatch guard.");
    }
    private static void UnknownFree()
    {
        using var f = new Fixture(); var p = f.Prepare(); f.Run.Dispose(); var target = f.Native.JudgeLists[0];
        var handle = f.Native.NoteViewHandlesFor(target).Last(); f.Native.FailFreeHandle = handle;
        var error = Expect<AggregateException>(p.Dispose); f.RetainUnknown(p, error);
        var access = Field(p, "_access"); var roots = Items(Field(access, "_roots")); var unknown = Items(Field(access, "_unknownFrees"));
        var addresses = (System.Collections.IDictionary)Field(access, "_addresses");
        Check(roots.Length == 1 && unknown.Length == 1 && ReferenceEquals(roots[0], unknown[0])
            && (long)addresses[roots[0]]! == target.ToInt64() && p.CleanupFailure is not null,
            "Unknown Judge native free lost its exact wrapper, address or cleanup failure.");
        var failures = f.Native.NoteViewFreeLog.Where(row => row.InjectedFailure).ToArray();
        Check(failures.Length == 1 && failures[0].Handle == handle && failures[0].Target == target && f.Native.ActiveRoots == 0,
            "The unknown-free injection was not the exact retained List handle or blocked later known cleanup.");
        var attempts = f.Native.FreeHandleAttempts; Expect<AggregateException>(p.Dispose);
        Check(attempts == f.Native.FreeHandleAttempts, "Later Judge queue Dispose retried an unknown native free.");
    }
}
