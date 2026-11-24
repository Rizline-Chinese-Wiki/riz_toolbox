using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekRendererCommandsChecks
{
    private static void Reentry()
    {
        using var f = new Fixture(FakeNative.RendererShape.Inactive, 5); var fired = false;
        f.Native.RendererProfile.OnVerify = () =>
        {
            if (fired) return; fired = true; f.Native.BeginLowerIl2CppAudit();
            try { Expect<InvalidOperationException>(() => f.Run.PrepareSeekRendererCommands(f.Native.RendererProfile, f.All(SeekRendererCommandKind.Preserve))); }
            finally { f.Native.EndLowerIl2CppAudit(); }
            Check(f.Native.LowerIl2CppCalls == 0, "Same-RunValue renderer preparation reentry reached native work.");
        };
        var p = f.Prepare(f.All(SeekRendererCommandKind.Appear)); f.ClearCallbacks(); Check(fired, "Renderer preparation reentry injection did not fire.");
        var callbacks = 0;
        f.Native.BeforeRendererCommand = _ =>
        {
            ++callbacks; f.Native.BeginLowerIl2CppAudit();
            try
            {
                Expect<InvalidOperationException>(() => p.Execute()); var journal = p.GetJournal();
                Check(journal.ExecutionEntered && journal.AttemptedCommands == callbacks, "Managed in-flight journal lost its admitted native prefix.");
            }
            finally { f.Native.EndLowerIl2CppAudit(); }
            Check(f.Native.LowerIl2CppCalls == 0, "Renderer Execute reentry or managed GetJournal dispatched an export.");
        };
        Known(p.Execute(), 5); f.ClearCallbacks(); Check(callbacks == 5, "Rejected reentry disrupted the admitted outer command sequence.");
    }
    private static void CallbackDispose()
    {
        using (var f = new Fixture(FakeNative.RendererShape.Inactive, 5))
        {
            var p = f.Prepare(f.All(SeekRendererCommandKind.Appear)); f.Run.Dispose(); var fired = false; var invocations = -1; var fields = -1;
            var owned = (NativeSeekRuntimeNoteView)Field(Field(p, "_access"), "_view");
            f.Native.AfterRendererGetter = (name, receiver) =>
            {
                if (fired || name != "get_localPosition") return; fired = true; p.Dispose();
                Check(f.Native.HasRootFor(receiver), "Dispose released the still-running getter's native receiver.");
                Expect<ObjectDisposedException>(owned.VerifySource); invocations = f.Native.Invocations; fields = f.Native.FieldReads;
            };
            Expect<ObjectDisposedException>(() => p.Execute()); f.ClearCallbacks();
            Check(fired && p.AttemptedCommands == 0 && f.Native.RendererCalls.Count == 0 && f.Native.ActiveRoots == 0
                && f.Native.Invocations == invocations && f.Native.FieldReads == fields && !p.SideEffectsUnknown && p.GetJournal().Disposed,
                "A read callback disposal crossed the next high-level native boundary, lost deferred root cleanup or invented command effects.");
        }
        using (var f = new Fixture(FakeNative.RendererShape.Inactive, 5))
        {
            var p = f.Prepare(f.All(SeekRendererCommandKind.Appear)); var closed = false; var frees = -1;
            f.Native.AfterRendererCommand = _ =>
            {
                closed = true; frees = f.Native.FreeHandleAttempts; p.Dispose();
                Check(f.Native.FreeHandleAttempts == frees, "Logical Dispose inside an entered command physically released its effects source.");
            };
            var failure = Expect<ObjectDisposedException>(() => p.Execute()); f.ClearCallbacks();
            Check(closed && p.AttemptedCommands == 1 && f.Native.RendererCalls.Count == 1 && f.Native.RendererGets.Count == 1
                && p.GetJournal().Commands.Single().NativeEntered && p.GetJournal().Commands.Single().SideEffectsUnknown,
                "Entered-command disposal lost its native effects prefix or proceeded to another command.");
            RetainEffects(f, p, failure);
        }
    }
    private static void WrongThread()
    {
        using var f = new Fixture(); var p = f.Prepare(); f.Native.BeginLowerIl2CppAudit();
        try
        {
            Task.Run(() =>
            {
                Expect<InvalidOperationException>(() => p.Execute()); Expect<InvalidOperationException>(p.Dispose);
                Expect<InvalidOperationException>(() => f.Run.PrepareSeekRendererCommands(f.Native.RendererProfile, f.All(SeekRendererCommandKind.Preserve)));
                Check(p.GetJournal().Prepared && !p.GetJournal().ExecutionEntered, "Managed worker journal access changed lifecycle state.");
            }).GetAwaiter().GetResult();
        }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0, "Worker renderer admission dispatched an IL2CPP export.");
        Known(p.Execute(), 0);
    }
    private static void UnknownFree()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); p.Execute(); f.Run.Dispose(); var target = f.Native.RendererPlans[0];
            var handle = f.Native.NoteViewHandlesFor(target).Last(); f.Native.FailFreeHandle = handle;
            var failure = Expect<AggregateException>(p.Dispose); f.Retain(p, failure); var access = Field(p, "_access");
            var roots = Items(Field(access, "_roots")); var unknown = Items(Field(access, "_unknownFrees"));
            Check(roots.Length == 1 && unknown.Length == 1 && ReferenceEquals(roots[0], unknown[0]) && p.CleanupFailure is not null
                && p.HasRetainedResponsibility && failure.Data["SeekRendererEffectsOwner"] is not null
                && f.Native.NoteViewFreeLog.Count(row => row.Handle == handle && row.Target == target && row.InjectedFailure) == 1,
                "Unknown accessor native free lost its exact retained wrapper, handle/target, owner or cleanup failure.");
            var frees = f.Native.FreeHandleAttempts; Expect<AggregateException>(p.Dispose);
            Check(f.Native.FreeHandleAttempts == frees && !p.SideEffectsUnknown, "A cleanup-only unknown native free was retried or rewritten as an unknown command effect.");
        }
        using (var f = new Fixture(FakeNative.RendererShape.Inactive, 5))
        {
            var p = f.Prepare(f.All(SeekRendererCommandKind.Appear)); var ownersBefore = PublicationOwners(); uint handle = 0;
            f.Native.BeforeRendererCommand = _ => { handle = f.Native.NoteViewHandlesFor(f.Native.RendererNotes[0]).Last(); f.Native.FailFreeHandle = handle; };
            var failure = Expect<AggregateException>(() => p.Execute()); f.ClearCallbacks(); var owner = PublicationOwners().Except(ownersBefore).Single();
            Check(p.AttemptedCommands == 1 && f.Native.RendererCalls.Count == 1 && f.Native.RendererGets.Count == 1
                && Items(Field(owner, "_roots")).Length == 1 && Items(Field(owner, "_arguments")).Length == 0
                && (int)Field(owner, "_failureCount") == 1 && (bool)Field(owner, "_cleanupAttempted")
                && !(bool)Field(owner, "_publicationFailed") && f.Native.NoteViewFreeLog.Count(row => row.Handle == handle && row.InjectedFailure) == 1,
                "An entered command's temporary receiver free lost its exact publication owner or was mistaken for known engine cleanup.");
            f.RetainPublication(owner); RetainEffects(f, p, failure);
        }
    }
}
