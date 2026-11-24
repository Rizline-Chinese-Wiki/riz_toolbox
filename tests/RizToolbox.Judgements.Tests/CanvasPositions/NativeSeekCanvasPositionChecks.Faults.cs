using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekCanvasPositionChecks
{
    private static bool DoesNotConfirm(Func<bool> verify)
    { try { return !verify(); } catch { return true; } }
    private static void NativeFaults()
    {
        foreach (var mode in new[] { "before", "after", "native-exception" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var original = p.GetJournal().Baseline.ToArray();
            if (mode == "before") f.Native.FailGeometryBefore = 1;
            else if (mode == "after") f.Native.FailGeometryAfter = 1;
            else f.Native.CanvasPositionSetterException = f.Native.OtherObject;
            var failure = Reject(() => f.Execute(p)); var j = p.GetJournal();
            Check(ReferenceEquals(failure.Data["SeekCanvasPositionEffectsOwner"], p)
                && j.ExecutionEntered && !j.ExecutionFinished && !j.AppliedValuesVerified
                && j.AttemptedWrites == 1 && j.Commands.Count == 1 && j.Commands[0].NativeEntered
                && !j.Commands[0].NativeReturned && j.SideEffectsUnknown && j.ExternalEffectsUnclosed && j.HasRetainedResponsibility,
                "Partially entered Canvas native command lost its exact owner or immutable failure evidence: " + mode);
            Check(f.Native.GeometryCalls.Count == 1 && f.Native.GeometryCalls[0].Stored == (mode != "before"),
                "Canvas command retried a partially entered setter or invented native storage: " + mode);
            f.Clear(); NoNative(f.Native, () => Reject(p.Execute));
            p.RestoreSelectedValues(); Check(p.VerifyRestoredValues(), "Entered partial localPosition could not conditionally restore its recognized selected value: " + mode);
            for (var i = 0; i < original.Length; ++i) Vector(f.Native.CanvasPositionValue(i), original[i], "Partial restoration changed an untouched actual Transform.");
            Check(p.GetJournal().AttemptedRestores == (mode == "before" ? 0 : 1)
                && p.GetJournal().Commands.Where(c => c.Direction == SeekCanvasPositionDirection.Restore).All(c => c.Ordinal == 0),
                "Partial inverse wrote an already-original or never-entered Canvas slot: " + mode);
        }
    }
    private static void ThirdStates()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); var before = f.Native.CanvasPositionValue(0); f.Native.CanvasPositionSet(0, before with { X = before.X + 1 });
            Reject(() => f.Execute(p)); Check(f.Native.GeometryCalls.Count == 0 && p.GetJournal().AttemptedWrites == 0,
                "Changed baseline was overwritten before first actual setter.");
        }
        using var changed = new Fixture(); var q = changed.Prepare(); changed.Execute(q); Check(q.VerifyAppliedValues(), "Initial target confirmation failed.");
        var target = q.GetJournal().Targets[0].LocalPosition; var external = target with { X = target.X + 17 };
        changed.Native.CanvasPositionSet(0, external); var writes = changed.Native.GeometryCalls.Count;
        Check(DoesNotConfirm(q.VerifyAppliedValues) && q.GetJournal().UnrecognizedState && !q.GetJournal().AppliedValuesVerified,
            "Third actual value did not withdraw a prior target confirmation or mark sticky unrecognized state.");
        Reject(q.RestoreSelectedValues); Vector(changed.Native.CanvasPositionValue(0), external, "Conditional restoration overwrote an external third state.");
        changed.Native.CanvasPositionSet(0, target); Reject(q.RestoreSelectedValues);
        Check(changed.Native.GeometryCalls.Count == writes && q.GetJournal().UnrecognizedState && q.HasRetainedResponsibility,
            "Returning a third state to target erased sticky evidence or was treated as proof of no intervening external change.");
    }
    private static void SourceAndWithdrawal()
    {
        foreach (var mode in new[] { "point", "array", "profile", "revision", "withdrawal", "backend" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native;
            if (mode == "point") n.CanvasSourceSet(n.CanvasSourcePoint(0, true, 0), "value", 123f);
            else if (mode == "array") n.CanvasSourceSet(n.GeometryCanvasControl, "canvasMoveData", n.CanvasSourceCloneArray(n.CanvasSourceMovesArray));
            else if (mode == "profile") n.CanvasPositionProfile.WrongMember = "SetLocalPositionMethod";
            else Revoke(f, mode);
            if (mode is "revision" or "withdrawal" or "backend") NoNative(n, () => Reject(p.Execute));
            else Reject(p.Execute);
            Check(n.GeometryCalls.Count == 0 && p.GetJournal().AttemptedWrites == 0,
                "Stale actual source/profile or revoked owner reached a Canvas setter: " + mode);
        }
        using var callback = new Fixture(); var q = callback.Prepare(); var fired = false;
        callback.Native.AfterGeometryStore = _ =>
        {
            if (fired) return; fired = true;
            callback.Native.CanvasSourceSet(callback.Native.CanvasSourcePoint(0, true, 0), "value", 123f);
        };
        Reject(() => callback.Execute(q)); callback.Clear();
        Check(fired && callback.Native.GeometryCalls.Count == 1 && q.GetJournal().SideEffectsUnknown && q.HasRetainedResponsibility,
            "Actual source callback change allowed another target setter or lost entered effects.");
        Reject(q.RestoreSelectedValues); Check(callback.Native.GeometryCalls.Count == 1, "Conditional inverse ignored the changed actual source.");
    }
    private static void ConfirmationAndEffects()
    {
        using var f = new Fixture(); var p = f.Prepare(); f.Execute(p); Check(p.VerifyAppliedValues(), "Initial applied values did not confirm.");
        var applied = p.GetJournal();
        Check(applied.AppliedValuesVerified && applied.SideEffectsUnknown && applied.ExternalEffectsUnclosed
            && applied.HasRetainedResponsibility && !applied.CompleteSeekPermitted,
            "A sampled localPosition success was presented as closed engine effects or a complete Seek permit.");
        f.Native.AfterGeometryGetter = (_, _) => throw new IOException("Synthetic later actual getter failure.");
        Check(DoesNotConfirm(p.VerifyAppliedValues) && !p.GetJournal().AppliedValuesVerified,
            "A failed confirmation left an earlier successful applied-value proof active.");
        f.Clear(); p.RestoreSelectedValues(); Check(p.VerifyRestoredValues(), "Recognized values failed to restore after a transient confirmation getter error.");
        var restored = p.GetJournal();
        Check(restored.RestoredValuesVerified && restored.ExternalEffectsUnclosed && restored.SideEffectsUnknown
            && restored.HasRetainedResponsibility && !restored.CompleteSeekPermitted,
            "Restored visible position values discharged unproved native engine responsibility.");
        f.Native.AfterGeometryGetter = (_, _) => throw new IOException("Synthetic restored-value confirmation failure.");
        Check(DoesNotConfirm(p.VerifyRestoredValues) && !p.GetJournal().RestoredValuesVerified,
            "A later failed restoration confirmation retained stale successful proof.");
        f.Clear(); var roots = f.Native.ActiveRoots; var frees = f.Native.FreeHandleAttempts;
        var failure = Reject(p.Dispose); var j = p.GetJournal();
        Check(ReferenceEquals(failure.Data["SeekCanvasPositionEffectsOwner"], p) && j.Disposed
            && j.HasRetainedResponsibility && !j.CompleteSeekPermitted && f.Native.ActiveRoots == roots
            && f.Native.FreeHandleAttempts == frees && roots > 0,
            "Dispose freed independent roots or discarded effects after selected positions were restored.");
    }
    private static void UnknownCleanup()
    {
        foreach (var kind in new[] { "receiver", "box" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var fired = false; uint failed = 0;
            var roots = n.ActiveRoots;
            if (kind == "receiver") n.AfterGeometryGetter = (_, receiver) =>
            {
                if (fired) return; fired = true; failed = n.NoteViewHandlesFor(receiver).Last(); n.FailFreeHandle = failed;
            };
            else n.AfterCanvasSourceRoot = (handle, pointer, _) =>
            {
                if (fired || pointer != n.CanvasPositionLastBox) return;
                fired = true; failed = handle; n.FailFreeHandle = handle;
            };
            var failure = Reject(p.VerifyPrepared); f.Clear(); f.RetainPublication(p, failure);
            Check(fired && failed != 0 && n.GeometryCalls.Count == 0 && p.GetJournal().AttemptedWrites == 0
                && ReferenceEquals(failure.Data["SeekCanvasPositionCleanupOwner"], p)
                && failure.Data["SeekCanvasPositionCleanupFailure"] is Exception
                && failure.Data["SeekCanvasPositionSourceCleanupOwner"] is object
                && p.HasRetainedResponsibility && n.ActiveRoots == roots
                && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1,
                "Prepared getter temporary unknown free lost public writer/source responsibility or freed retained source roots: " + kind);
            var attempts = n.FreeHandleAttempts;
            NoNative(n, () => { Reject(p.VerifyPrepared); Reject(p.Execute); Reject(p.Dispose); });
            Check(n.FreeHandleAttempts == attempts && n.ActiveRoots == roots && p.HasRetainedResponsibility,
                "Getter temporary unknown cleanup was retried or signed as safe no-setter disposal: " + kind);
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Run.Dispose(); var n = f.Native; var target = n.CanvasPositionTransforms[1];
            var handle = n.NoteViewHandlesFor(target).Last(); n.FailFreeHandle = handle;
            var error = Reject(p.Dispose); f.Retain(p, error);
            Check(ReferenceEquals(error.Data["SeekCanvasPositionCleanupOwner"], p)
                && error.Data["SeekCanvasPositionCleanupFailure"] is Exception && p.HasRetainedResponsibility
                && n.NoteViewFreeLog.Count(row => row.Handle == handle && row.Target == target && row.InjectedFailure) == 1
                && n.ActiveRoots == 0, "Unknown root free discarded exact writer ownership or prevented other known cleanup.");
            var frees = n.FreeHandleAttempts; var again = Reject(p.Dispose);
            Check(ReferenceEquals(again.Data["SeekCanvasPositionCleanupOwner"], p) && n.FreeHandleAttempts == frees,
                "Unknown Canvas root free was retried or its original public owner was replaced.");
        }
        using var construction = new Fixture(); var native = construction.Native; var constructionFired = false; uint constructionFailedHandle = 0;
        var point = native.CanvasSourcePoint(0, true, 2);
        native.AfterCanvasSourceRange = () =>
        {
            if (constructionFired) return; constructionFired = true;
            constructionFailedHandle = native.NoteViewHandlesFor(point).Last(); native.FailFreeHandle = constructionFailedHandle;
            throw new IOException("Synthetic writer source construction failure with unknown root cleanup.");
        };
        var constructionFailure = Reject(() => construction.Prepare()); construction.Clear();
        var owner = constructionFailure.Data["SeekCanvasPositionEffectsOwner"] as NativeSeekCanvasPositionCommands
            ?? constructionFailure.Data["SeekCanvasPositionCleanupOwner"] as NativeSeekCanvasPositionCommands;
        Check(constructionFired && constructionFailedHandle != 0 && owner is not null && owner.HasRetainedResponsibility
            && constructionFailure.Data["SeekCanvasPositionSourceCleanupOwner"] is object
            && native.NoteViewFreeLog.Count(row => row.Handle == constructionFailedHandle && row.Target == point && row.InjectedFailure) == 1,
            "Real source construction failure lost original cleanup handoff or public writer responsibility.");
        construction.Retain(owner!, constructionFailure); var constructionAttempts = native.FreeHandleAttempts; Reject(owner!.Dispose); Reject(owner.Dispose);
        Check(native.FreeHandleAttempts == constructionAttempts, "Retained source construction cleanup retried an unknown native free.");
    }
}
