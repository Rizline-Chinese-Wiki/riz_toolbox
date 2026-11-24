using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.CanvasBasis;

internal static partial class NativeSeekCanvasBasisChecks
{
    private static void PartialCache()
    {
        const int nan = unchecked((int)0xffc12345), negativeZero = unchecked((int)0x80000000);
        foreach (var kind in new[] { "array-before", "array-after", "scalar-before", "scalar-after" })
        {
            using var f = new Fixture(n =>
            {
                n.CanvasCacheSetBits(0, 0, negativeZero); n.CanvasCacheSetBits(1, 1, nan);
                n.CanvasCacheSetBits(3, -1, nan); n.CanvasCacheSetBits(4, -1, negativeZero);
            });
            var n = f.Native; var basis = f.Prepare(); var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
            var before = cache.GetJournal(); var fired = false;
            if (kind == "scalar-before") n.FailCanvasCacheScalarBefore = 1;
            else if (kind == "scalar-after") n.FailCanvasCacheScalarAfter = 1;
            else if (kind == "array-before") n.AfterGeometryArrayExport = (name, pointer) =>
            {
                if (fired || name != "size" || pointer != n.CanvasCacheArray(0) || !InArrayWriter()) return;
                fired = true; throw new IOException("Synthetic official array admission failed before its raw store.");
            };
            else n.AfterCanvasCacheObservedStore = row =>
            {
                if (fired || row.Kind != SeekCanvasCacheKind.FloorPositions) return;
                fired = true; throw new IOException("Synthetic official export observed the first raw array store then failed.");
            };
            var failure = Reject(basis.Execute); f.Clear(); var cj = cache.GetJournal(); var pj = position.GetJournal();
            var entered = kind == "array-before" ? 0 : kind == "array-after" ? 1 : 10;
            Check(cj.CommitEntered && !cj.CommitFinished && !basis.GetJournal().ExecutionFinished
                && !basis.GetJournal().AppliedValuesVerified && cj.AttemptedWrites == entered
                && cj.Stores.Count == (kind == "array-before" ? 1 : entered)
                && pj.AttemptedWrites == 0 && pj.Commands.Count == 0 && n.GeometryCalls.Count == 0,
                "Partial cache phase entered positions, retried storage, or lost its original attempt boundary: " + kind);
            if (kind == "array-before")
                Check(cj.Stores[0].Attempted && !cj.Stores[0].NativeEntered && !cj.Stores[0].NativeReturned
                    && cj.Stores[0].Readback is null,
                    "Pre-admission cache rejection discarded its attempted row or invented native entry/readback.");
            if (entered != 0)
            {
                Check(ReferenceEquals(Data(failure, "SeekCanvasCacheCleanupOwner"), cache)
                    && cj.Stores[^1].NativeEntered && !cj.Stores[^1].NativeReturned
                    && cj.SideEffectsUnknown && cj.HasRetainedResponsibility,
                    "Partial native cache phase lost the exact original public cache pending owner: " + kind);
                if (kind.StartsWith("scalar", StringComparison.Ordinal))
                    Check(cj.Stores[^1].Ordinal == 9 && cj.Stores[^1].Readback == (kind == "scalar-after" ? Bits(5) : before.Baseline[9])
                        && n.CanvasCacheScalarCalls.Count == 1 && n.CanvasCacheScalarCalls[0].Stored == (kind == "scalar-after"),
                        "Partial official scalar phase lost before/after raw-bit evidence.");
            }
            NoNative(n, () => Reject(basis.Execute));
            basis.RestoreSelectedValues(); Check(basis.VerifyRestoredValues(), "Recognized partial cache basis did not restore its selected values: " + kind);
            var restored = cache.GetJournal(); var inverse = kind switch { "array-before" => 0, "array-after" => 1, "scalar-before" => 9, _ => 10 };
            Check(n.CanvasCacheSnapshot().SequenceEqual(before.Baseline) && restored.AttemptedRestores == inverse
                && restored.Stores.Where(row => row.Direction == SeekCanvasCacheDirection.Restore)
                    .All(row => row.Ordinal < entered && row.TargetBits == before.Baseline[row.Ordinal] && row.OriginalObserved)
                && !restored.HasRetainedResponsibility && position.GetJournal().AttemptedRestores == 0 && n.GeometryCalls.Count == 0
                && n.CanvasCacheBits(0, 0) == negativeZero && n.CanvasCacheBits(1, 1) == nan && n.CanvasCacheBits(3) == nan && n.CanvasCacheBits(4) == negativeZero,
                "Conditional cache inverse wrote an untouched slot, canonicalized baseline bits, or retained known pure-storage responsibility: " + kind);
            var historical = basis.GetJournal(); var calls = n.GeometryCalls.Count;
            basis.AcknowledgeVerifiedRestoredOwnership();
            var acknowledged = basis.GetJournal();
            Check(acknowledged.RestoredValuesVerified && !acknowledged.HasRetainedResponsibility
                && acknowledged.Failure == historical.Failure && historical.Failure is not null
                && acknowledged.Failures.Count == historical.Failures.Count
                && acknowledged.Failures.Zip(historical.Failures).All(row => row.First.Phase == row.Second.Phase
                    && ReferenceEquals(row.First.Failure, row.Second.Failure))
                && n.GeometryCalls.Count == calls && position.GetJournal().AttemptedWrites == 0,
                "Verified partial cache ownership erased its commit failure, dispatched a setter or retained a known inverse: " + kind);
        }
    }

    private static void BetweenCacheAndPosition()
    {
        foreach (var kind in new[] { "source", "source-profile", "position-profile", "revision", "withdrawal", "backend" })
        {
            using var f = new Fixture(); var n = f.Native; var basis = f.Prepare(); var cache = basis.OriginalCacheOwner;
            var expected = cache.GetJournal().Slots.Select(slot => slot.TargetBits).ToArray(); var fired = false;
            n.CanvasPositionProfile.OnVerify = () =>
            {
                if (fired || n.CanvasCacheObservedStores.Count != 11) return; fired = true;
                if (kind == "source") n.CanvasSourceSet(n.CanvasSourcePoint(0, true, 0), "value", 123f);
                else if (kind == "source-profile") n.CanvasSourceProfile.WrongMember = "CanvasClass";
                else if (kind == "position-profile") n.CanvasPositionProfile.WrongMember = "SetLocalPositionMethod";
                else f.Revoke(kind);
            };
            Reject(basis.Execute); f.Clear(); var journal = basis.GetJournal(); var cj = cache.GetJournal(); var pj = basis.OriginalPositionOwner.GetJournal();
            Check(fired && journal.ExecutionEntered && !journal.ExecutionFinished && !journal.AppliedValuesVerified
                && cj.CommitFinished && cj.AttemptedWrites == 11 && cj.AttemptedRestores == 0
                && pj.AttemptedWrites == 0 && pj.Commands.Count == 0 && n.GeometryCalls.Count == 0
                && n.CanvasCacheSnapshot().SequenceEqual(expected) && n.CanvasCacheObservedStores.Count == 11
                && ReferenceEquals(journal.OriginalCacheOwner, cache) && journal.Failures.Count > 0,
                "A between-phase withdrawal admitted a setter, silently inverted caches, or lost the original cache history: " + kind);
            var stores = n.CanvasCacheObservedStores.Count; Reject(basis.RestoreSelectedValues);
            Check(n.CanvasCacheObservedStores.Count == stores && n.GeometryCalls.Count == 0 && !basis.GetJournal().RestoredValuesVerified,
                "Expired original source allowed an implicit or explicit conditional inverse: " + kind);
        }
    }

    private static void PartialPosition()
    {
        var original = new NativeVector3(BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
            BitConverter.Int32BitsToSingle(unchecked((int)0xffc12345)), BitConverter.Int32BitsToSingle(0x7fc54321));
        foreach (var kind in new[] { "before", "after", "native-exception", "readback" })
        {
            using var f = new Fixture(n => n.CanvasPositionSet(0, original)); var n = f.Native; var basis = f.Prepare();
            var position = basis.OriginalPositionOwner; var cache = basis.OriginalCacheOwner;
            var cj = cache.GetJournal(); var pj = position.GetJournal(); var roots = n.CanvasBasisCurrentRoots(); var fired = false;
            if (kind == "before") n.FailGeometryBefore = 1;
            else if (kind == "after") n.FailGeometryAfter = 1;
            else if (kind == "native-exception") n.CanvasPositionSetterException = n.OtherObject;
            else n.AfterGeometryGetter = (name, _) =>
            {
                if (fired || name != "get_localPosition" || n.GeometryCalls.Count == 0) return;
                fired = true; throw new IOException("Synthetic entered position readback failed.");
            };
            var failure = Reject(basis.Execute); f.Clear(); var entered = position.GetJournal();
            Check(ReferenceEquals(Data(failure, "SeekCanvasPositionEffectsOwner"), position)
                && entered.ExecutionEntered && !entered.ExecutionFinished && entered.AttemptedWrites == 1 && entered.Commands.Count == 1
                && entered.Commands[0].NativeEntered && entered.SideEffectsUnknown && entered.ExternalEffectsUnclosed && entered.HasRetainedResponsibility
                && !basis.GetJournal().AppliedValuesVerified && cache.GetJournal().AttemptedWrites == 11
                && n.CanvasCacheSnapshot().SequenceEqual(cj.Slots.Select(slot => slot.TargetBits)) && n.GeometryCalls.Count == 1,
                "Partial setter/readback lost its original effects owner, pending command, or already applied cache phase: " + kind);
            Check(n.GeometryCalls[0].Stored == (kind != "before")
                && (kind == "readback" || !entered.Commands[0].NativeReturned),
                "Partial position evidence invented a returned native setter or wrong before/after boundary: " + kind);
            OriginalRootsRemain(n, roots, "Entered partial position released an original complete source root.");
            var order = new List<string>();
            n.AfterGeometryStore = _ => order.Add("position");
            n.AfterCanvasCacheObservedStore = _ =>
            {
                Check(pj.Baseline.Select((value, ordinal) => Same(value, n.CanvasPositionValue(ordinal))).All(value => value),
                    "Partial restoration entered cache inverse before all selected positions matched baseline.");
                order.Add("cache");
            };
            basis.RestoreSelectedValues(); f.Clear(); Check(basis.VerifyRestoredValues(), "Recognized partial position did not restore selected values: " + kind);
            var restored = position.GetJournal();
            Check(restored.AttemptedRestores == (kind == "before" ? 0 : 1)
                && restored.Commands.Where(command => command.Direction == SeekCanvasPositionDirection.Restore).All(command => command.Ordinal == 0)
                && (kind == "before" || order[0] == "position") && order.Skip(kind == "before" ? 0 : 1).All(item => item == "cache")
                && n.CanvasCacheSnapshot().SequenceEqual(cj.Baseline)
                && restored.ExternalEffectsUnclosed && restored.SideEffectsUnknown && restored.HasRetainedResponsibility
                && basis.GetJournal().HasRetainedResponsibility && basis.GetJournal().ExternalEffectsUnclosed,
                "Partial inverse touched a never-entered Transform, reordered dependencies, or closed unproved engine effects: " + kind);
            Vector(n.CanvasPositionValue(0), original, "Partial position restoration lost original NaN payload or signed-zero bits.");
            OriginalRootsRemain(n, roots, "Selected restoration discharged original position effects roots.");
            Reject(basis.AcknowledgeVerifiedRestoredOwnership);
            Check(!basis.GetJournal().RestoredValuesVerified && basis.GetJournal().HasRetainedResponsibility,
                "Selected value recovery acknowledged an unfinished or failed original Unity setter: " + kind);
            OriginalRootsRemain(n, roots, "Rejected partial-position acknowledgement dropped its original effect roots.");
        }
    }

    private static void CrossChildThirdStates()
    {
        foreach (var kind in new[] { "setter-time", "setter-array", "cache-unentered-position", "cache-entered-position" })
        {
            using var f = new Fixture(); var n = f.Native; var basis = f.Prepare(); var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
            var cj = cache.GetJournal(); var pj = position.GetJournal(); var fired = false;
            if (kind.StartsWith("setter", StringComparison.Ordinal)) n.AfterGeometryStore = _ =>
            {
                if (fired) return; fired = true;
                n.CanvasCacheSetBits(kind == "setter-time" ? 3 : 0, kind == "setter-time" ? -1 : 0, Bits(77));
            };
            else n.AfterCanvasSourceRead = (name, receiver) =>
            {
                if (fired || name != "nowTime" || receiver != n.GeometryCanvasControl || n.CanvasCacheObservedStores.Count != 11
                    || (kind == "cache-entered-position" ? n.GeometryCalls.Count != 3 : n.GeometryCalls.Count != 0)) return;
                fired = true; var target = kind == "cache-entered-position" ? pj.Targets[2].LocalPosition : pj.Baseline[2];
                n.CanvasPositionSet(2, target with { X = target.X + 17 });
            };
            Reject(basis.Execute); f.Clear(); var after = basis.GetJournal();
            Check(fired && !after.AppliedValuesVerified && !after.ExecutionFinished && after.Failures.Count > 0
                && (kind.StartsWith("setter", StringComparison.Ordinal) ? cache.GetJournal().UnrecognizedState : position.GetJournal().UnrecognizedState)
                && (kind != "cache-unentered-position" || n.GeometryCalls.Count == 0),
                "Cross-child third state survived joint readback, lost sticky evidence, or entered an unprepared setter: " + kind);
            var stores = n.CanvasCacheObservedStores.Count; var setters = n.GeometryCalls.Count; Reject(basis.RestoreSelectedValues);
            Check(n.CanvasCacheObservedStores.Count == stores && n.GeometryCalls.Count == setters,
                "Global expected preflight restored one child before observing the other child's third state: " + kind);
            if (kind == "setter-time") n.CanvasCacheSetBits(3, -1, cj.Slots[9].TargetBits);
            else if (kind == "setter-array") n.CanvasCacheSetBits(0, 0, cj.Slots[0].TargetBits);
            else n.CanvasPositionSet(2, kind == "cache-entered-position" ? pj.Targets[2].LocalPosition : pj.Baseline[2]);
            Reject(basis.RestoreSelectedValues);
            if (kind.StartsWith("setter", StringComparison.Ordinal)) Reject(cache.Restore);
            else Reject(position.RestoreSelectedValues);
            Check(n.CanvasCacheObservedStores.Count == stores && n.GeometryCalls.Count == setters
                && !basis.GetJournal().RestoredValuesVerified
                && (kind.StartsWith("setter", StringComparison.Ordinal) ? cache.GetJournal().UnrecognizedState : position.GetJournal().UnrecognizedState),
                "Returning an observed third state to expected erased sticky history or manufactured aggregate restoration.");
        }

        // Both reader-boundary regressions run even while either is red. The
        // actual third value is boxed by an official getter and then restored by
        // its callback before the manager can take a compensating observation.
        var boundaryFailures = new List<Exception>();
        foreach (var boundary in new[] { "double-sample", "final-conditional" })
        {
            try
            {
                using var f = new Fixture(); var n = f.Native; var basis = f.Prepare();
                var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
                var before = position.GetJournal(); var receiver = n.CanvasPositionTransforms[0];
                var samples = 0; var armed = false; var fired = false;
                if (boundary == "double-sample")
                {
                    basis.Execute();
                    var target = before.Targets[0].LocalPosition;
                    n.AfterGeometryGetter = (name, current) =>
                    {
                        if (name != "get_localPosition" || current != receiver || fired) return;
                        if (++samples == 1) n.CanvasPositionSet(0, target with { X = target.X + 17 });
                        else if (samples == 2) { fired = true; n.CanvasPositionSet(0, target); }
                    };
                    Reject(() => basis.VerifyAppliedValues());
                }
                else
                {
                    n.CanvasPositionProfile.OnVerify = () =>
                    {
                        if (armed || !Environment.StackTrace.Contains("PositionAccess.WriteSlot", StringComparison.Ordinal)) return;
                        armed = true; var original = before.Baseline[0];
                        n.CanvasPositionSet(0, original with { X = original.X + 17 });
                    };
                    n.AfterGeometryGetter = (name, current) =>
                    {
                        if (!armed || fired || name != "get_localPosition" || current != receiver
                            || !Environment.StackTrace.Contains("PositionAccess.WriteSlot", StringComparison.Ordinal)) return;
                        ++samples; fired = true; n.CanvasPositionSet(0, before.Baseline[0]);
                    };
                    Reject(basis.Execute);
                }
                f.Clear();
                var conflict = position.GetJournal(); var stores = n.CanvasCacheObservedStores.Count; var setters = n.GeometryCalls.Count;
                var expected = boundary == "double-sample" ? before.Targets[0].LocalPosition : before.Baseline[0];
                var refusedFreshProof = DoesNotConfirm(basis.VerifyAppliedValues);
                Exception? restorationFailure = null;
                try { basis.RestoreSelectedValues(); }
                catch (Exception error) { restorationFailure = error; }
                Check(fired && samples == (boundary == "double-sample" ? 2 : 1)
                    && Same(n.CanvasPositionValue(0), expected)
                    && conflict.UnrecognizedState && conflict.AttemptedWrites == (boundary == "double-sample" ? 3 : 0)
                    && refusedFreshProof && restorationFailure is not null
                    && n.CanvasCacheObservedStores.Count == stores && n.GeometryCalls.Count == setters
                    && !basis.GetJournal().AppliedValuesVerified && !basis.GetJournal().RestoredValuesVerified
                    && position.GetJournal().UnrecognizedState
                    && ReferenceEquals(basis.GetJournal().OriginalCacheOwner, cache)
                    && ReferenceEquals(basis.GetJournal().OriginalPositionOwner, position),
                    "An observed position third state returned to expected lost sticky evidence or admitted a new proof/inverse at " + boundary + ".");
            }
            catch (Exception error)
            {
                boundaryFailures.Add(new InvalidOperationException("Position conflict boundary: " + boundary, error));
            }
        }
        foreach (var otherThirdState in new[] { false, true })
        {
            try { PendingPositionSampleCoverage(otherThirdState); }
            catch (Exception error)
            {
                boundaryFailures.Add(new InvalidOperationException("Pending position sample coverage, other third state: " + otherThirdState, error));
            }
        }
        try { CacheConflictSampleCoverage(); }
        catch (Exception error) { boundaryFailures.Add(error); }
        if (boundaryFailures.Count != 0)
            throw new AggregateException("Observed position third states must survive their reader's throwing admission boundary.", boundaryFailures);
    }

    private static void PendingPositionSampleCoverage(bool otherThirdState)
    {
        using var f = new Fixture(); var n = f.Native; var basis = f.Prepare();
        var position = basis.OriginalPositionOwner; var cache = basis.OriginalCacheOwner; var before = position.GetJournal();
        var pendingReceiver = n.CanvasPositionTransforms[0]; var otherReceiver = n.CanvasPositionTransforms[2];
        var readbackFailed = false;
        n.AfterGeometryGetter = (name, _) =>
        {
            if (readbackFailed || name != "get_localPosition" || n.GeometryCalls.Count != 1) return;
            readbackFailed = true; throw new IOException("Synthetic first admitted setter readback leaves its original command pending.");
        };
        Reject(basis.Execute); f.Clear();
        var pending = position.GetJournal();
        Check(readbackFailed && pending.AttemptedWrites == 1 && pending.Commands.Count == 1
            && pending.Commands[0].NativeEntered && pending.Commands[0].NativeReturned && pending.Commands[0].Readback is null
            && !pending.UnrecognizedState,
            "A failed getter did not preserve the exact admitted command as a recognized pending transition.");

        n.CanvasPositionSet(0, before.Baseline[0]);
        var third = before.Baseline[2] with { X = before.Baseline[2].X + 17 };
        if (otherThirdState) n.CanvasPositionSet(2, third);
        var samples = 0; var otherSamples = 0;
        n.AfterGeometryGetter = (name, receiver) =>
        {
            if (name != "get_localPosition") return;
            if (receiver == pendingReceiver)
            {
                if (++samples == 1) n.CanvasPositionSet(0, before.Targets[0].LocalPosition);
            }
            else if (otherThirdState && receiver == otherReceiver && ++otherSamples == 2)
                n.CanvasPositionSet(2, before.Baseline[2]);
        };
        var failure = Reject(basis.RestoreSelectedValues); f.Clear();
        var conflict = Failures(failure).Select(error => error.Data[NativeSeekCanvasPositionConflict.Key])
            .OfType<NativeSeekCanvasPositionConflict>().FirstOrDefault();
        var firstSnapshot = conflict?.FirstSnapshot; var secondSnapshot = conflict?.SecondSnapshot;
        Check(samples == 2 && conflict is not null && conflict.MultipleSamples
            && conflict.Ordinal == 0 && conflict.Transform == pendingReceiver.ToInt64()
            && conflict.FirstBits == NativeSeekCanvasPositionBits.From(before.Baseline[0])
            && conflict.SecondBits == NativeSeekCanvasPositionBits.From(before.Targets[0].LocalPosition)
            && firstSnapshot is not null && secondSnapshot is not null
            && firstSnapshot.Count == before.Targets.Count && secondSnapshot.Count == before.Targets.Count
            && firstSnapshot[2].Transform == otherReceiver.ToInt64()
            && secondSnapshot[2].Transform == otherReceiver.ToInt64()
            && firstSnapshot[2].Bits == NativeSeekCanvasPositionBits.From(otherThirdState ? third : before.Baseline[2])
            && secondSnapshot[2].Bits == firstSnapshot[2].Bits
            && Same(n.CanvasPositionValue(2), before.Baseline[2]),
            "The throwing complete position sample lost raw-bit/physical evidence from another already observed occurrence.");
        var sticky = position.GetJournal().UnrecognizedState; var stores = n.CanvasCacheObservedStores.Count; var setters = n.GeometryCalls.Count;
        if (otherThirdState)
        {
            Reject(position.RestoreSelectedValues);
            Check(sticky && position.GetJournal().UnrecognizedState && n.GeometryCalls.Count == setters
                && n.CanvasCacheObservedStores.Count == stores && !basis.GetJournal().RestoredValuesVerified,
                "A pending before/target sample hid another occurrence's observed third state or admitted an inverse after it returned to baseline.");
        }
        else
        {
            Check(!sticky, "An exact pending before/target pair was incorrectly made a sticky third state.");
            position.RestoreSelectedValues(); Check(position.VerifyRestoredValues()
                && n.GeometryCalls.Count == setters + 1 && position.GetJournal().AttemptedRestores == 1,
                "A recognized pending before/target pair prevented its later conditional inverse.");
            cache.Restore(); Check(cache.ConfirmRestored().OriginalStateRestored,
                "Known cache storage could not close after the recognized pending position inverse.");
        }
    }

    private static void CompleteSourceDrift()
    {
        foreach (var kind in new[] { "transform-slot", "move-slot", "list-backing", "list-size", "list-version", "list-tail", "point", "ease", "static-vector", "range", "source-profile" })
        {
            using var f = new Fixture(n => { if (kind == "list-tail") n.CanvasBasisAddListTail(0, true); });
            var n = f.Native; var basis = f.Prepare(); var roots = n.CanvasBasisCurrentRoots(); var ranges = n.CanvasSourceRangeAttempts; var fired = false;
            n.CanvasPositionProfile.OnVerify = () =>
            {
                if (fired || n.CanvasCacheObservedStores.Count != 11) return; fired = true;
                var list = n.CanvasSourceList(0, true);
                switch (kind)
                {
                    case "transform-slot": n.CanvasSourceSlot(n.GeometryCanvasArray, 2, n.CanvasPositionTransforms[1]); break;
                    case "move-slot": n.CanvasSourceSlot(n.CanvasSourceMovesArray, 2, n.CanvasSourceMoves[1]); break;
                    case "list-backing": n.CanvasSourceSet(list, "_items", n.CanvasSourceCloneArray(n.CanvasSourceBacking(list))); break;
                    case "list-size": n.CanvasSourceSet(list, "_size", 2); break;
                    case "list-version": n.CanvasSourceSet(list, "_version", 8); break;
                    case "list-tail": n.CanvasSourceSlot(n.CanvasSourceBacking(list), 3, n.CanvasSourcePoint(0, true, 1)); break;
                    case "point": n.CanvasSourceSet(n.CanvasSourcePoint(0, true, 0), "floorPosition", 123f); break;
                    case "ease": n.GeometrySingleSlot(n.CanvasSourceSamples[0], 100, .125f); n.CanvasSourceRemember(); break;
                    case "static-vector": n.CanvasSourceSetVector("downVector", new(-1, .5f, 4)); break;
                    case "range": n.CanvasSourceProfile.Range = n.CanvasSourceCloneRange(); break;
                    default: n.CanvasSourceProfile.WrongMember = "EaseInfoClass"; break;
                }
            };
            Reject(basis.Execute); f.Clear();
            Check(fired && n.CanvasSourceRangeAttempts == ranges && basis.OriginalCacheOwner.GetJournal().AttemptedWrites == 11
                && basis.OriginalCacheOwner.GetJournal().AttemptedRestores == 0
                && basis.OriginalPositionOwner.GetJournal().AttemptedWrites == 0 && n.GeometryCalls.Count == 0
                && !basis.GetJournal().AppliedValuesVerified,
                "Complete source drift across child stages recomputed a Plan, lost prior attempts, or reached the first setter: " + kind);
            OriginalRootsRemain(n, roots, "A cross-stage expired source freed an original independent root: " + kind);
            var stores = n.CanvasCacheObservedStores.Count; Reject(basis.RestoreSelectedValues);
            Check(n.CanvasCacheObservedStores.Count == stores && n.GeometryCalls.Count == 0,
                "Expired complete source admitted a later inverse: " + kind);
        }
        using var entered = new Fixture(); var native = entered.Native; var owner = entered.Prepare(); var physical = native.CanvasBasisCurrentRoots(); var changed = false;
        native.AfterGeometryStore = _ =>
        {
            if (changed) return; changed = true;
            native.CanvasSourceSet(native.CanvasSourcePoint(0, true, 0), "value", 123f);
        };
        var failure = Reject(owner.Execute); entered.Clear();
        Check(changed && native.GeometryCalls.Count == 1 && owner.OriginalPositionOwner.GetJournal().HasRetainedResponsibility
            && ReferenceEquals(Data(failure, "SeekCanvasPositionEffectsOwner"), owner.OriginalPositionOwner)
            && native.CanvasSourceRangeAttempts == 3,
            "Source expiry after an entered setter lost the original precise effects owner.");
        OriginalRootsRemain(native, physical, "Source expiry discharged an entered position's complete retained graph.");
    }
}
