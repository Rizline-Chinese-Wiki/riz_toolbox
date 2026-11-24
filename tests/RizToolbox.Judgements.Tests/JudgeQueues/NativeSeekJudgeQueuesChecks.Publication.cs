using System.Reflection;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekJudgeQueuesChecks
{
    private static object[] PublicationOwners() => Items(typeof(NativePublicationCall)
        .GetField("ProcessOwners", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!);
    private static void UnknownBackingTemporaryFree()
    {
        using var f = new Fixture(); var r = f.Native.JudgeRuntimeReferences;
        var targets = f.Targets with { Notes = Enumerable.Range(0, 9).Select(index => r[index % r.Length]).ToArray() };
        var p = f.Prepare(targets); p.Commit(); var originalBacking = f.Baseline[0].Backing;
        var beforeOwners = PublicationOwners(); uint temporaryHandle = 0;
        f.Native.AfterJudgeRoot = (handle, target) =>
        {
            if (target != originalBacking || !InMethod("WriteExactGuarded", "NativeRuntime")) return;
            Check(temporaryHandle == 0, "The original backing was temporarily rooted more than once for one guarded setter.");
            temporaryHandle = handle; f.Native.FailFreeHandle = handle;
        };
        p.Restore(); f.ClearCallbacks();
        var owner = PublicationOwners().Except(beforeOwners).Single();
        var roots = Items(Field(owner, "_roots")).Where(value => value is not null).ToArray();
        var failures = Items(Field(owner, "_failures")).Where(value => value is not null).ToArray();
        Check(temporaryHandle != 0 && roots.Length == 1 && failures.Length == 1 && failures[0] is IOException
            && f.Native.NoteViewFreeLog.Count(row => row.Handle == temporaryHandle && row.Target == originalBacking && row.InjectedFailure) == 1
            && f.Native.JudgeQueueWrites.Count(row => row.Operation == "_items" && row.List == 0) == 1,
            "An original-backing setter temporary free lost its exact bounded root/error owner or retried the dispatched setter.");
        f.RetainPublicationUnknown(p, (Exception)failures[0]);
        Check(p.ConfirmRestored().OriginalStateRestored && f.Native.JudgeQueueStorageEquals(f.Baseline),
            "Known complete baseline readback was lost after the setter had stored before temporary cleanup became unknown.");
        var attempts = f.Native.FreeHandleAttempts; p.Dispose();
        Check(f.Native.FreeHandleAttempts >= attempts
            && f.Native.NoteViewFreeLog.Count(row => row.Handle == temporaryHandle) == 1
            && PublicationOwners().Contains(owner),
            "Known accessor cleanup retried or discarded the process-owned unknown temporary root.");
    }
    private static void GrowthAlias()
    {
        using var f = new Fixture(configure: n => { n.JudgeQueueCapacity(0, 4); n.JudgeQueueCapacity(2, 0); });
        var r = f.Native.JudgeRuntimeReferences;
        var targets = f.Targets with { Notes = new[] { r[0] }, TapWaiting = Array.Empty<long>(), DragWaiting = new[] { r[0] } };
        var p = f.Prepare(targets); var fired = false;
        f.Native.AfterJudgeQueueStore = row =>
        {
            if (fired || row.Operation != "Add" || row.List != 2) return;
            fired = true; f.Native.JudgeSetList(2, "_items", f.Native.JudgeBacking(0));
        };
        Expect<InvalidOperationException>(p.Commit); f.ClearCallbacks(); var writes = f.Native.JudgeQueueWrites.Count;
        TryRestore(p);
        Check(fired && p.SideEffectsUnknown && !p.ConfirmRestored().OriginalStateRestored
            && f.Native.JudgeBacking(0) == f.Native.JudgeBacking(2) && f.Native.JudgeQueueWrites.Count == writes,
            "The final Add accepted an externally introduced positive-capacity backing alias or overwrote it during restoration.");
    }
}
