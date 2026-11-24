using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.CanvasBasis;

internal static partial class NativeSeekCanvasBasisChecks
{
    private static void IndependentLifetimeAndSerial()
    {
        using (var f = new Fixture())
        {
            var n = f.Native; var applicantRoots = n.CanvasBasisCurrentRoots(); var basis = f.Prepare();
            var roots = n.CanvasBasisCurrentRoots().Where(row => !applicantRoots.ContainsKey(row.Key)).ToDictionary(row => row.Key, row => row.Value);
            f.Run.Dispose();
            basis.VerifyPrepared(); basis.OriginalCacheOwner.VerifyPrepared(); basis.OriginalPositionOwner.VerifyPrepared();
            Check(basis.SourceCapture.TransformReferences.All(pointer => n.HasRootFor(new(pointer)))
                && roots.Count != 0,
                "Returned shared basis borrowed physical roots or lifetime from its applicant RunValue.");
            OriginalRootsRemain(n, roots, "Returned shared basis lost a newly acquired independent handle after its applicant RunValue exited.");
            basis.Execute(); Check(basis.VerifyAppliedValues(), "RunValue disposal damaged an independently returned basis.");
            basis.RestoreSelectedValues(); Check(basis.VerifyRestoredValues(), "Independent basis could not restore after applicant RunValue exit.");
        }
        using (var f = new Fixture())
        {
            var old = f.Prepare(); var n = f.Native; var serial = Serial(f.Run); var fired = false; var oldVerified = false;
            n.CanvasSourceProfile.OnVerify = () =>
            {
                if (fired) return; fired = true; f.Run.Dispose(); old.VerifyPrepared(); oldVerified = true;
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            };
            Reject(() => f.Prepare()); f.Clear();
            Check(fired && oldVerified && Serial(f.Run) == serial + 1
                && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                "A later cancelled construction changed an older holder's serial admission or continued native work.");
            old.VerifyPrepared(); old.Dispose(); Check(n.ActiveRoots == 0, "Cancelled later basis construction leaked known physical roots.");
        }
        using var gate = new Fixture(); var native = gate.Native; var entered = false; var initialSerial = Serial(gate.Run);
        native.CanvasSourceProfile.OnVerify = () =>
        {
            if (entered) return; entered = true; var serial = Serial(gate.Run);
            NoNative(native, () =>
            {
                Reject(() => gate.Run.CaptureSeekCanvasTargetSource(null!, float.NaN, float.PositiveInfinity));
                Reject(() => gate.Run.PrepareSeekCanvasCaches(null!, float.NaN, float.PositiveInfinity));
                Reject(() => gate.Run.PrepareSeekCanvasPositions(null!, null!, float.NaN, float.PositiveInfinity));
                Reject(() => gate.Run.PrepareSeekCanvasBasis(null!, null!, float.NaN, float.PositiveInfinity));
            });
            Check(Serial(gate.Run) == serial, "Rejected public Canvas construction reentry consumed another serial.");
        };
        var value = gate.Prepare(); gate.Clear();
        Check(entered && Serial(gate.Run) == initialSerial + 1 && native.CanvasSourceRangeAttempts == 3,
            "Basis internally called another public Canvas factory or failed to share the four-entry construction gate.");
        value.VerifyPrepared(); NoWrites(native, "Synchronous construction gate checks dispatched a value write.");
    }

    private static void ChildDisposalAndHolds()
    {
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
            var source = basis.SourceCapture; cache.Dispose(); position.VerifyPrepared();
            Reject(basis.VerifyPrepared); Check(ReferenceEquals(position.GetJournal().SourceCapture, source),
                "Cache logical disposal replaced or withdrew its original sibling's shared source.");
            position.Execute(); Check(position.VerifyAppliedValues(), "Position sibling could not execute after cache-only logical disposal.");
            position.RestoreSelectedValues(); Check(position.VerifyRestoredValues() && position.HasRetainedResponsibility
                && position.GetJournal().ExternalEffectsUnclosed && f.Native.ActiveRoots > 0,
                "Cache-only disposal discharged or corrupted the independently entered position responsibility.");
        }
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); var cache = basis.OriginalCacheOwner; basis.OriginalPositionOwner.Dispose();
            cache.VerifyPrepared(); cache.Commit(); Check(cache.Confirm().Observed == true,
                "Position logical disposal withdrew its unentered cache sibling.");
            cache.Restore(); Check(cache.ConfirmRestored().OriginalStateRestored,
                "Cache sibling failed conditional inverse after position-only disposal.");
            Reject(basis.Execute); Check(f.Native.GeometryCalls.Count == 0 && !basis.GetJournal().AppliedValuesVerified,
                "Aggregate accepted a missing logical child or invented combined success from independent cache confirmation.");
        }
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); var n = f.Native; f.Run.Dispose(); var roots = n.CanvasBasisCurrentRoots();
            basis.OriginalCacheOwner.Dispose(); OriginalRootsRemain(n, roots, "Nonfinal cache child drop freed the shared source.");
            basis.OriginalCacheOwner.Dispose(); OriginalRootsRemain(n, roots, "Repeated cache child drop decremented another hold.");
            basis.OriginalPositionOwner.Dispose(); OriginalRootsRemain(n, roots, "Both child drops consumed the aggregate bootstrap hold.");
            basis.Dispose(); Check(n.ActiveRoots == 0 && roots.Keys.All(handle => n.NoteViewFreeLog.Count(row => row.Handle == handle) == 1),
                "Final known bootstrap drop did not free each original independent root exactly once.");
            var attempts = n.FreeHandleAttempts;
            NoNative(n, () => { basis.Dispose(); basis.OriginalCacheOwner.Dispose(); basis.OriginalPositionOwner.Dispose(); });
            Check(n.FreeHandleAttempts == attempts, "Repeated final known disposal retried root free.");
        }
        foreach (var cacheCaller in new[] { false, true })
        {
            using var f = new Fixture(); var n = f.Native; var basis = f.Prepare(); var fired = false;
            n.AfterCanvasSourceRead = (name, receiver) =>
            {
                if (fired || name != "screenWidth" || receiver != n.GeometryCanvasControl) return; fired = true;
                if (cacheCaller) basis.OriginalCacheOwner.Dispose(); else basis.OriginalPositionOwner.Dispose();
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            };
            Reject(cacheCaller ? basis.OriginalCacheOwner.VerifyPrepared : basis.OriginalPositionOwner.VerifyPrepared); f.Clear();
            Check(fired && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
                "Child-local deep source withdrawal continued exports after its callback.");
            if (cacheCaller) basis.OriginalPositionOwner.VerifyPrepared(); else basis.OriginalCacheOwner.VerifyPrepared();
            Check(!basis.SourceCapture.HasRetainedResponsibility && !basis.GetJournal().HasRetainedResponsibility,
                "A caller-only deep source cancellation was made a physical-source fault or native cleanup unknown.");
            Reject(basis.VerifyPrepared); NoWrites(n, "Child-only callback disposal reached either writer.");
        }
    }

    private static void ScopeThreadAndCallbackDisposal()
    {
        foreach (var cacheCaller in new[] { false, true })
        {
            // The aggregate is idle while an original child is operating. Its
            // terminal callback must return the aggregate's registry slot after
            // that child unwinds, without a later GetJournal/Dispose to help it.
            var owners = typeof(NativeSeekCanvasBasisCommands).GetField("Owners",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            int OwnerCount() => (int)owners.GetType().GetProperty("Count")!.GetValue(owners)!;
            var before = OwnerCount();
            using var f = new Fixture(); var basis = f.Prepare(); var n = f.Native; f.Run.Dispose();
            var roots = n.CanvasBasisCurrentRoots(); var fired = false;
            var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
            n.AfterCanvasSourceRead = (name, receiver) =>
            {
                if (fired || name != "screenWidth" || receiver != n.GeometryCanvasControl) return;
                fired = true; basis.Dispose();
                OriginalRootsRemain(n, roots, "Idle aggregate disposal released roots while an independent child scope was active.");
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            };
            Reject(cacheCaller ? cache.VerifyPrepared : position.VerifyPrepared); f.Clear();
            Check(fired && OwnerCount() == before && n.ActiveRoots == 0
                && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free")
                && basis.GetJournal().Disposed && !basis.HasRetainedResponsibility
                && basis.GetJournal().CleanupFailure is null,
                "Independent child callback disposal leaked the aggregate registry or misclassified managed withdrawal as unknown native cleanup.");
            NoNative(n, basis.Dispose);
        }
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
            var cacheRevision = cache.ObservationRevision; var positionRevision = position.ObservationRevision; var errors = new List<Exception>();
            NoNative(f.Native, () => Task.Run(() =>
            {
                foreach (Action action in new Action[]
                {
                    () => f.Prepare(), () => f.Run.PrepareSeekCanvasBasis(null!, null!, float.NaN, float.PositiveInfinity),
                    basis.VerifyPrepared, basis.Execute, () => basis.VerifyAppliedValues(), basis.RestoreSelectedValues,
                    () => basis.VerifyRestoredValues(), basis.Dispose, () => basis.GetJournal(),
                    () => _ = basis.OriginalCacheOwner, () => _ = basis.OriginalPositionOwner, () => _ = basis.SourceCapture,
                    cache.VerifyPrepared, cache.Commit, () => cache.Confirm(), cache.Restore, () => cache.ConfirmRestored(), cache.Dispose, () => cache.GetJournal(),
                    position.VerifyPrepared, position.Execute, () => position.VerifyAppliedValues(), position.RestoreSelectedValues,
                    () => position.VerifyRestoredValues(), position.Dispose, () => position.GetJournal()
                }) errors.Add(Reject(action));
            }).GetAwaiter().GetResult());
            Check(errors.Count == 26 && errors.All(error => error.GetType() == typeof(InvalidOperationException))
                && cache.ObservationRevision == cacheRevision && position.ObservationRevision == positionRevision
                && !basis.GetJournal().ExecutionEntered && !basis.GetJournal().Disposed,
                "Worker basis/child operations dispatched native, consumed revisions, or disposed the owner-thread scope.");
            basis.VerifyPrepared();
        }
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); var n = f.Native; var fired = false; var position = basis.OriginalPositionOwner;
            n.BeforeCanvasCacheScalar = _ =>
            {
                if (fired) return; fired = true;
                NoNative(n, () =>
                {
                    Reject(position.VerifyPrepared); Reject(position.Execute);
                    Reject(() => position.VerifyAppliedValues());
                    Reject(basis.VerifyPrepared); Reject(basis.Execute); Reject(() => basis.VerifyAppliedValues());
                    Reject(basis.RestoreSelectedValues); Reject(() => basis.VerifyRestoredValues()); Reject(() => basis.GetJournal());
                });
            };
            basis.Execute(); f.Clear(); Check(fired && basis.VerifyAppliedValues() && n.GeometryCalls.Count == 3,
                "Managed sibling scope reentry damaged the valid outer basis or became NoteView in-flight self-reentry.");
            basis.RestoreSelectedValues(); Check(basis.VerifyRestoredValues(), "Outer restoration failed after rejected sibling scope reentry.");
        }
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); var n = f.Native; var fired = false;
            var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
            n.AfterCanvasSourceRead = (name, receiver) =>
            {
                if (fired || name != "screenWidth" || receiver != n.GeometryCanvasControl) return;
                var stack = Environment.StackTrace;
                if (stack.Contains("NativeSeekCanvasCacheParticipant", StringComparison.Ordinal)
                    || stack.Contains("NativeSeekCanvasPositionCommands", StringComparison.Ordinal)) return;
                fired = true;
                NoNative(n, () =>
                {
                    Reject(cache.VerifyPrepared); Reject(position.VerifyPrepared);
                    Reject(basis.VerifyPrepared); Reject(() => basis.GetJournal());
                });
            };
            basis.VerifyPrepared(); f.Clear(); Check(fired, "Common shared-source sweep reentry fixture did not reach the aggregate-only managed scope.");
            basis.VerifyPrepared(); NoWrites(n, "Common source sweep reentry reached a native writer.");
        }
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); var n = f.Native; f.Run.Dispose(); var roots = n.CanvasBasisCurrentRoots(); var fired = false;
            n.AfterCanvasSourceRead = (name, receiver) =>
            {
                if (fired || name != "screenWidth" || receiver != n.GeometryCanvasControl) return; fired = true;
                try { basis.Dispose(); } catch (Exception) { /* The in-flight original child retains its own deferred cleanup error. */ }
                OriginalRootsRemain(n, roots, "Callback aggregate disposal freed the capture while its managed scope was active.");
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            };
            Reject(basis.VerifyPrepared); f.Clear();
            Check(fired && basis.GetJournal().Disposed && !basis.GetJournal().HasRetainedResponsibility
                && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free") && n.ActiveRoots == 0
                && roots.Keys.All(handle => n.NoteViewFreeLog.Count(row => row.Handle == handle) == 1),
                "Aggregate callback disposal continued deep exports, faulted the source, or failed deferred final bootstrap release.");
            NoNative(n, basis.Dispose); NoWrites(n, "Callback-only aggregate disposal entered a writer.");
        }
    }

    private static void ProofsJournalsAndDisposal()
    {
        using var f = new Fixture(); var n = f.Native; var basis = f.Prepare(); var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
        var initial = basis.GetJournal(); var slots = initial.CacheJournal!.Slots.ToArray(); var positions = initial.PositionJournal!.Targets.ToArray();
        basis.Execute(); Check(basis.VerifyAppliedValues(), "Initial full basis application failed."); var applied = basis.GetJournal();
        Check(cache.Confirm().Observed == true && !basis.GetJournal().AppliedValuesVerified,
            "Independent original cache observation failed to revoke a prior aggregate proof.");
        Check(cache.Confirm().Observed == true && !basis.GetJournal().AppliedValuesVerified,
            "A later independent cache confirmation resurrected an older aggregate proof.");
        Check(basis.VerifyAppliedValues(), "Fresh two-round basis readback could not establish a new combined proof.");
        Check(position.VerifyAppliedValues() && !basis.GetJournal().AppliedValuesVerified,
            "Independent original position revision left the old combined applied proof active.");
        Check(basis.VerifyAppliedValues(), "Fresh combined observation failed after position-only confirmation.");
        var injected = new IOException("Synthetic later selected getter confirmation failure."); var fired = false;
        n.AfterGeometryGetter = (name, _) =>
        {
            if (fired || name != "get_localPosition") return; fired = true; throw injected;
        };
        Check(DoesNotConfirm(basis.VerifyAppliedValues) && fired && !basis.GetJournal().AppliedValuesVerified,
            "A failed fresh confirmation preserved a successful older aggregate proof.");
        f.Clear(); Check(basis.VerifyAppliedValues(), "A transient target getter failure incorrectly expired the shared physical source.");
        basis.RestoreSelectedValues(); Check(basis.VerifyRestoredValues(), "Joint selected values failed to restore after a transient getter failure.");
        var restored = basis.GetJournal();
        Check(cache.ConfirmRestored().OriginalStateRestored && !basis.GetJournal().RestoredValuesVerified,
            "Independent original cache restored-value revision left stale combined restoration active.");
        Check(cache.ConfirmRestored().OriginalStateRestored && !basis.GetJournal().RestoredValuesVerified && basis.VerifyRestoredValues(),
            "Independent restored cache confirmation revived old proof or prevented a new joint proof.");
        Check(!initial.ExecutionEntered && initial.CacheJournal.Stores.Count == 0 && initial.PositionJournal.Commands.Count == 0
            && initial.CacheJournal.Slots.SequenceEqual(slots) && initial.PositionJournal.Targets.SequenceEqual(positions)
            && applied.AppliedValuesVerified && applied.CacheJournal!.Stores.Count == 11 && applied.PositionJournal!.Commands.Count == 3
            && !applied.RestorationEntered && restored.RestoredValuesVerified && restored.Failures.Count > 0,
            "Later confirmation, failure, or restoration mutated a prior immutable aggregate/child journal.");
        if (initial.CacheJournal.Slots is IList<SeekCanvasCacheSlot> mutableSlots)
            Check(Reject(() => mutableSlots.Clear()) is NotSupportedException, "Aggregate journal exposed writable original cache evidence.");
        if (applied.PositionJournal!.Commands is IList<SeekCanvasPositionCommandEffects> mutableCommands)
            Check(Reject(() => mutableCommands.Clear()) is NotSupportedException, "Aggregate journal exposed writable native position evidence.");
        if (restored.Failures is IList<SeekCanvasBasisFailure> mutableFailures)
            Check(Reject(() => mutableFailures.Clear()) is NotSupportedException, "Aggregate journal exposed a mutable failure history.");
        f.Run.Dispose(); var roots = n.CanvasBasisCurrentRoots(); cache.Dispose();
        OriginalRootsRemain(n, roots, "Known cache restoration/disposal released a position owner's entered external effects roots.");
        var failure = Reject(basis.Dispose); var disposed = basis.GetJournal();
        Check(ReferenceEquals(disposed.OriginalCacheOwner, cache) && ReferenceEquals(disposed.OriginalPositionOwner, position)
            && ReferenceEquals(basis.OriginalCacheOwner, cache) && ReferenceEquals(basis.OriginalPositionOwner, position)
            && ReferenceEquals(Data(failure, "SeekCanvasPositionEffectsOwner"), position)
            && ReferenceEquals(position.GetJournal().EffectsOwner, position) && position.GetJournal().Disposed
            && disposed.Disposed && disposed.ExternalEffectsUnclosed && disposed.SideEffectsUnknown && disposed.HasRetainedResponsibility
            && !disposed.AppliedValuesVerified && !disposed.RestoredValuesVerified && !disposed.CompleteSeekPermitted
            && disposed.Failures.Any(item => ReferenceEquals(item.ChildOwner, position)),
            "Aggregate disposal lost original child identities, cleared entered effects, or kept stale combined proof.");
        OriginalRootsRemain(n, roots, "Disposed aggregate freed complete source roots still owned by its original effects child.");
        NoNative(n, () => Reject(basis.Dispose));
    }
}
