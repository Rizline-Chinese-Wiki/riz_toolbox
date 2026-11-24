using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekCanvasCacheChecks
{
    private static void IndependentLifetime()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); var n = f.Native;
            var physical = new[] { n.GameplayInformation, n.GameplayProgress, n.SeekChart, n.GeometryCanvasControl,
                    n.GeometryCanvasArray, n.CanvasSourceMovesArray, n.CanvasSourceEaseArray }
                .Concat(Enumerable.Range(0, 3).Select(n.CanvasCacheArray)).Concat(n.CanvasPositionTransforms)
                .Concat(n.CanvasSourceMoves).Concat(n.CanvasSourceLists).Concat(n.CanvasSourceLists.Select(n.CanvasSourceBacking))
                .Concat(n.CanvasSourceLists.SelectMany(list => n.CanvasSourceSlots(n.CanvasSourceBacking(list))))
                .Concat(n.CanvasSourceSamples).Distinct().ToArray();
            f.Run.Dispose(); p.VerifyPrepared();
            Check(physical.All(n.HasRootFor), "Cache owner borrowed roots from the applicant RunValue instead of independently retaining the complete source.");
            p.Commit(); Check(p.Confirm().Observed == true, "Independently owned cache storage expired when the applicant RunValue exited.");
            p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored, "Independent cache owner could not restore its original bits after applicant exit.");
        }
        foreach (var at in new[] { "profile", "cache-read", "scalar-read" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var fired = false; var verified = false;
            void CancelLater()
            {
                if (fired) return; fired = true; f.Run.Dispose();
                n.CanvasSourceAuditExports = false;
                try { p.VerifyPrepared(); verified = true; }
                finally { n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true; }
            }
            if (at == "profile") n.CanvasSourceProfile.OnVerify = CancelLater;
            else if (at == "scalar-read") n.AfterCanvasSourceRead = (name, owner) =>
            { if (name == "nowTime" && owner == n.GeometryCanvasControl) CancelLater(); };
            else n.AfterCanvasSourceRoot = (_, pointer, pinned) =>
            { if (pinned && pointer == n.CanvasCacheArray(0)) CancelLater(); };
            Reject(() => f.Prepare()); f.Clear();
            Check(fired && verified && n.CanvasCacheObservedStores.Count == 0,
                "Later cancelled cache construction consumed an earlier independent public owner: " + at);
            OnlyCleanup(n, "Cancelled cache construction continued exports after callback return.");
            p.VerifyPrepared(); p.Dispose(); Check(n.ActiveRoots == 0, "Cancelled construction or known earlier cache disposal leaked physical roots.");
        }
        foreach (var kind in new[] { "revision", "withdrawal", "backend" })
        {
            using var f = new Fixture(); var p = f.Prepare(); Revoke(f, kind);
            NoNative(f.Native, () => Reject(p.Commit));
            Check(p.GetJournal().AttemptedWrites == 0, "Locally revoked cache owner entered a storage attempt: " + kind);
        }
    }
    private static void ThreadAndReentry()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); var errors = new List<Exception>();
            NoNative(f.Native, () => Task.Run(() =>
            {
                foreach (Action action in new Action[] { () => f.Prepare(),
                    () => f.Run.PrepareSeekCanvasCaches(null!, float.NaN, float.PositiveInfinity),
                    p.VerifyPrepared, p.Commit, () => p.Confirm(), p.Restore, () => p.ConfirmRestored(), p.Dispose,
                    () => p.GetJournal(), () => _ = p.SideEffectsUnknown, () => _ = p.HasRetainedResponsibility }) errors.Add(Reject(action));
            }).GetAwaiter().GetResult());
            Check(errors.Count == 11 && errors.All(error => error.GetType() == typeof(InvalidOperationException))
                && !p.GetJournal().CommitEntered && !p.GetJournal().Disposed,
                "Worker cache admission/diagnostics entered native validation or consumed the main-thread public owner.");
            p.VerifyPrepared();
        }
        using var outer = new Fixture(); var n = outer.Native; var preparing = false;
        n.CanvasSourceProfile.OnVerify = () =>
        {
            if (preparing) return; preparing = true;
            NoNative(n, () =>
            {
                Reject(() => outer.Prepare()); Reject(() => outer.Run.PrepareSeekCanvasCaches(null!, float.NaN, float.PositiveInfinity));
            });
        };
        var q = outer.Prepare(); outer.Clear(); Check(preparing, "Cache construction reentry fixture never entered the actual source callback.");
        var writing = false;
        n.BeforeCanvasCacheScalar = ignoredName =>
        {
            if (writing) return; writing = true;
            NoNative(n, () =>
            {
                Reject(q.Commit); Reject(q.VerifyPrepared); Reject(() => q.Confirm()); Reject(q.Restore);
                Reject(() => q.ConfirmRestored()); Reject(() => q.GetJournal());
                Reject(() => _ = q.SideEffectsUnknown); Reject(() => _ = q.HasRetainedResponsibility);
            });
        };
        q.Commit(); outer.Clear();
        Check(writing && q.Confirm().Observed == true && n.CanvasCacheObservedStores.Count == 11,
            "Rejected synchronous reentry damaged the actual outer complete cache sequence.");
        q.Restore(); Check(q.ConfirmRestored().OriginalStateRestored, "Outer cache original state failed after rejected synchronous reentry.");
    }
    private static void ConfirmationAndJournals()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); var initial = p.GetJournal(); var copied = initial.Slots.ToArray();
            p.Commit(); Check(p.Confirm().Observed == true, "Initial actual target cache confirmation failed.");
            var applied = p.GetJournal();
            Check(applied.AppliedValuesVerified && !applied.SideEffectsUnknown && !applied.HasRetainedResponsibility
                && !applied.CompleteSeekPermitted, "Exact pure-cache readback did not close only its own known storage responsibility.");
            f.Native.AfterGeometryArrayExport = (_, pointer) =>
            { if (pointer == f.Native.CanvasSourceSamples[0]) throw new IOException("Synthetic later complete source sample failure."); };
            Check(DoesNotConfirm(() => p.Confirm().Observed == true) && !p.GetJournal().AppliedValuesVerified
                && p.SideEffectsUnknown && p.HasRetainedResponsibility,
                "Failed later target confirmation left an older successful cache proof or discharged pending responsibility.");
            f.Clear();
            // A sample export failure expires the complete source reader. A
            // separate owner below exercises a transient target-field read fault.
            f.Retain(p);
            Check(!initial.CommitEntered && initial.Stores.Count == 0 && initial.Slots.SequenceEqual(copied)
                && applied.AppliedValuesVerified && applied.Stores.Count == 11,
                "Failed confirmation changed earlier immutable baseline/target/store evidence.");
            if (initial.Slots is IList<SeekCanvasCacheSlot> slots)
                Check(Reject(() => slots[0] = copied[1]) is NotSupportedException, "Journal exposes mutable cache target occurrences.");
            if (applied.Stores is IList<SeekCanvasCacheStoreEffects> stores)
                Check(Reject(stores.Clear) is NotSupportedException, "Journal exposes mutable native store evidence.");
            if (initial.Baseline is IList<int> baseline)
                Check(Reject(() => baseline[0] = 1) is NotSupportedException, "Journal exposes writable original raw bits.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); p.Commit(); Check(p.Confirm().Observed == true, "Target confirmation failed before transient-read test.");
            f.Native.AfterCanvasSourceRead = (name, owner) =>
            { if (name == "nowTime" && owner == f.Native.GeometryCanvasControl) throw new IOException("Synthetic target-cache field read failure."); };
            Check(DoesNotConfirm(() => p.Confirm().Observed == true) && !p.GetJournal().AppliedValuesVerified,
                "Transient target field read failure preserved stale target success.");
            f.Clear(); Check(p.Confirm().Observed == true, "Target-cache-only read failure incorrectly expired the fixed source identity.");
            p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored, "Conditional restoration failed after a transient cache read error.");
            var restored = p.GetJournal();
            f.Native.AfterCanvasSourceRead = (name, owner) =>
            { if (name == "speed" && owner == f.Native.GeometryCanvasControl) throw new IOException("Synthetic restored-cache field read failure."); };
            Check(DoesNotConfirm(() => p.ConfirmRestored().OriginalStateRestored) && !p.GetJournal().RestoredValuesVerified
                && p.HasRetainedResponsibility, "Failed later original confirmation left stale restored proof.");
            f.Clear(); Check(p.ConfirmRestored().OriginalStateRestored && restored.RestoredValuesVerified
                && !p.HasRetainedResponsibility, "A fresh original observation could not close pure-cache responsibility.");
        }
        foreach (var restored in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); p.Commit();
            Check(p.Confirm().Observed == true, "Target state did not confirm before direct-disposal drift test.");
            if (restored)
            {
                p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored, "Original state did not confirm before direct-disposal drift test.");
            }
            var prior = p.GetJournal(); var n = f.Native; var roots = n.ActiveRoots;
            var priorFrees = n.NoteViewFreeLog.Select(row => row.Handle).ToHashSet();
            var originalHandles = n.NoteViewRootLog.Where(row => !priorFrees.Contains(row.Handle)).Select(row => row.Handle).ToHashSet();
            var freeRecords = n.NoteViewFreeLog.Count;
            n.CanvasCacheSetBits(0, 0, Bits(77)); var error = Reject(p.Dispose); f.Retain(p, error);
            var current = p.GetJournal();
            Check(ReferenceEquals(error.Data["SeekCanvasCacheCleanupOwner"], p) && current.Disposed
                && current.UnrecognizedState && current.SideEffectsUnknown && current.HasRetainedResponsibility
                && !current.AppliedValuesVerified && !current.RestoredValuesVerified
                && originalHandles.Count == roots && n.NoteViewFreeLog.Skip(freeRecords).All(row => !originalHandles.Contains(row.Handle))
                && n.ActiveRoots == roots && n.CanvasCacheBits(0, 0) == Bits(77)
                && (restored ? prior.RestoredValuesVerified : prior.AppliedValuesVerified),
                "Dispose used a stale successful cache proof after direct external drift or freed independent roots: " + restored);
            NoNative(n, () => Reject(p.Dispose));
        }
        using var combined = new Fixture(positions: true); var caches = combined.Prepare();
        var position = combined.Run.PrepareSeekCanvasPositions(combined.Native.CanvasSourceProfile, combined.Native.CanvasPositionProfile, 5, 3);
        combined.RetainPosition(position); caches.Commit(); Check(caches.Confirm().Observed == true, "Coexisting cache target did not confirm.");
        position.Execute(); Check(position.VerifyAppliedValues(), "Coexisting actual position target did not confirm.");
        caches.Restore(); Check(caches.ConfirmRestored().OriginalStateRestored && !caches.HasRetainedResponsibility,
            "Cache restoration failed to close known cache-only storage.");
        Check(position.HasRetainedResponsibility && position.SideEffectsUnknown && position.GetJournal().ExternalEffectsUnclosed
            && !position.GetJournal().CompleteSeekPermitted && !caches.GetJournal().CompleteSeekPermitted,
            "Cache restoration erased another position command's entered Unity engine effects or permitted complete Seek.");
        position.RestoreSelectedValues(); Check(position.VerifyRestoredValues() && position.HasRetainedResponsibility
            && position.GetJournal().ExternalEffectsUnclosed, "Visible position restoration incorrectly closed its independent external engine responsibility.");
        caches.Dispose();
        Check(position.HasRetainedResponsibility && combined.Native.ActiveRoots > 0,
            "Known cache disposal released an independently retained position engine owner's source roots.");
    }
    private static void UnknownCleanup()
    {
        foreach (var store in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var roots = n.ActiveRoots;
            var fired = false; uint failed = 0;
            n.AfterCanvasSourceRoot = (handle, pointer, pinned) =>
            {
                if (fired || !pinned || pointer != n.CanvasCacheArray(0) || store && !InArrayWriter()) return;
                fired = true; failed = handle; n.FailFreeHandle = handle;
            };
            var error = Reject(() => { if (store) p.Commit(); else p.VerifyPrepared(); }); f.Clear(); f.RetainPublication(p, error);
            var journal = p.GetJournal(); var originalOwner = error.Data["SeekCanvasCacheSourceCleanupOwner"];
            Check(fired && failed != 0 && journal.AttemptedWrites == (store ? 1 : 0)
                && n.CanvasCacheObservedStores.Count == (store ? 1 : 0) && n.CanvasCacheScalarCalls.Count == 0
                && ReferenceEquals(error.Data["SeekCanvasCacheCleanupOwner"], p)
                && error.Data["SeekCanvasCacheCleanupFailure"] is Exception && originalOwner is not null
                && ReferenceEquals(journal.CleanupOwner, p) && ReferenceEquals(journal.SourceCleanupOwner, originalOwner)
                && p.HasRetainedResponsibility && n.ActiveRoots == roots
                && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1,
                "Unknown read/write temporary pin cleanup lost the original publication, public owner, or no-store evidence.");
            var attempts = n.FreeHandleAttempts;
            NoNative(n, () =>
            {
                Reject(p.VerifyPrepared); Reject(p.Commit); Reject(() => p.Confirm()); Reject(p.Restore);
                Reject(() => p.ConfirmRestored()); var again = Reject(p.Dispose);
                Check(ReferenceEquals(again.Data["SeekCanvasCacheCleanupOwner"], p)
                    && ReferenceEquals(again.Data["SeekCanvasCacheSourceCleanupOwner"], originalOwner),
                    "Terminal unknown cleanup replaced its exact original source/public owner.");
            });
            Check(n.FreeHandleAttempts == attempts && journal.Stores.Count == (store ? 1 : 0),
                "Unknown pin cleanup was retried or an immutable prior journal changed.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Run.Dispose(); var n = f.Native; var target = n.CanvasCacheArray(1);
            var handle = n.NoteViewHandlesFor(target).Last(); n.FailFreeHandle = handle;
            var error = Reject(p.Dispose); f.Retain(p, error);
            Check(ReferenceEquals(error.Data["SeekCanvasCacheCleanupOwner"], p)
                && error.Data["SeekCanvasCacheCleanupFailure"] is Exception && p.HasRetainedResponsibility
                && n.NoteViewFreeLog.Count(row => row.Handle == handle && row.Target == target && row.InjectedFailure) == 1
                && n.ActiveRoots == 0, "Terminal cache root free lost exact ownership or skipped remaining known cleanup.");
            var attempts = n.FreeHandleAttempts; NoNative(n, () => Reject(p.Dispose));
            Check(n.FreeHandleAttempts == attempts, "Unknown terminal cache root free was retried.");
        }
        using (var f = new Fixture())
        {
            var n = f.Native; var fired = false; uint failed = 0;
            n.AfterCanvasSourceRoot = (handle, pointer, pinned) =>
            {
                if (fired || !pinned || pointer != n.CanvasCacheArray(0)) return;
                fired = true; failed = handle; n.FailFreeHandle = handle;
            };
            var error = Reject(() => f.Prepare()); f.Clear();
            var owner = error.Data["SeekCanvasCacheCleanupOwner"] as NativeSeekCanvasCacheParticipant;
            Check(fired && failed != 0 && owner is not null && owner.HasRetainedResponsibility
                && error.Data["SeekCanvasCacheSourceCleanupOwner"] is object && n.CanvasCacheObservedStores.Count == 0,
                "Constructor cache-pin cleanup failure escaped without its original public owner and no-store evidence.");
            f.RetainPublication(owner!, error); var attempts = n.FreeHandleAttempts;
            NoNative(n, () => { Reject(owner!.Commit); Reject(owner!.Dispose); });
            Check(n.FreeHandleAttempts == attempts, "Unknown constructor cache-pin cleanup was retried.");
        }
    }
}
