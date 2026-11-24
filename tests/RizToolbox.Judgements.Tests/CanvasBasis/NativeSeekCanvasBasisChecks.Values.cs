using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Judgements.Tests.NoteFlags;

namespace RizToolbox.Judgements.Tests.CanvasBasis;

internal static partial class NativeSeekCanvasBasisChecks
{
    private static void SingleSourceAndPlan()
    {
        using var f = new Fixture(); var n = f.Native; var serial = Serial(f.Run); var basis = f.Prepare();
        var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
        basis.VerifyPrepared(); basis.VerifyPrepared(); var journal = basis.GetJournal();
        var cj = cache.GetJournal(); var pj = position.GetJournal(); var capture = basis.SourceCapture; var plan = capture.Plan;
        Check(Serial(f.Run) == serial + 1 && journal.Prepared && !journal.ExecutionEntered
            && ReferenceEquals(journal.OriginalCacheOwner, cache) && ReferenceEquals(journal.OriginalPositionOwner, position)
            && ReferenceEquals(cj.SourceCapture, capture) && ReferenceEquals(pj.SourceCapture, capture)
            && ReferenceEquals(journal.SourceCapture, capture) && ReferenceEquals(journal.Plan, plan)
            && ReferenceEquals(cj.SourceCapture!.Plan, pj.SourceCapture!.Plan),
            "Basis created another construction, source identity, Plan, or replacement child owner.");
        Check(plan.Rows.Count == 3 && plan.Moves.Select(move => move.Index).SequenceEqual(new[] { 999, -5, 999 })
            && plan.Rows.Select(row => row.Ordinal).SequenceEqual(new[] { 0, 1, 2 })
            && plan.Rows.Select(row => row.MoveReference).SequenceEqual(n.CanvasSourceMoves.Select(pointer => pointer.ToInt64()))
            && capture.TransformReferences.SequenceEqual(n.CanvasPositionTransforms.Select(pointer => pointer.ToInt64()))
            && pj.Targets.Select(row => row.Transform).SequenceEqual(capture.TransformReferences)
            && pj.Targets.Zip(plan.Rows).All(pair => pair.First.Ordinal == pair.Second.Ordinal && Same(pair.First.LocalPosition, pair.Second.LocalPosition)),
            "Shared actual source lost duplicate move occurrences or remapped by CanvasMove.index.");
        foreach (var row in plan.Rows)
        {
            Check(cj.Slots[row.Ordinal].TargetBits == Bits(row.FloorPosition)
                && cj.Slots[3 + row.Ordinal].TargetBits == Bits(row.FloorSpeed)
                && cj.Slots[6 + row.Ordinal].TargetBits == Bits(row.XPosition),
                "Cache targets were not derived from the identical Plan row as actual positions.");
        }
        Check(n.CanvasSourceRangeAttempts == 3 && n.CanvasSourceRanges.Count == 3
            && n.CanvasSourceRanges.All(range => range.To == 5d)
            && plan.Request.ChartTime == 5 && plan.Request.Speed == 3
            && !basis.CompleteSeekPermitted && !journal.CompleteSeekPermitted && !cj.CompleteSeekPermitted
            && !pj.CompleteSeekPermitted && !capture.CompleteSeekPermitted && !plan.CompleteSeekPermitted,
            "Basis recomputed the numeric producer on verification or granted complete Seek.");
        NoWrites(n, "Shared source preparation dispatched cache stores or Unity setters.");
    }

    private static void PreparationBoundaries()
    {
        using (var f = new Fixture())
        {
            var before = f.Native.CanvasCacheSnapshot(); var positions = f.Native.CanvasPositionTransforms.Select(pointer => f.Native.GeometryTransforms[pointer].Position).ToArray();
            var basis = f.Prepare(); basis.VerifyPrepared(); var cj = basis.OriginalCacheOwner.GetJournal(); var pj = basis.OriginalPositionOwner.GetJournal();
            Check(cj.Prepared && pj.Prepared && cj.Baseline.SequenceEqual(before)
                && cj.Expected.SequenceEqual(before) && pj.Baseline.Zip(positions).All(pair => Same(pair.First, pair.Second))
                && !cj.CommitEntered && !pj.ExecutionEntered && cj.Stores.Count == 0 && pj.Commands.Count == 0,
                "Basis did not retain both independent complete baselines without dispatch.");
            NoWrites(f.Native, "Read-only dual baseline preparation performed a write.");
        }
        foreach (var kind in new[] { "source", "cache-baseline", "position-baseline" })
        {
            using var f = new Fixture(); var n = f.Native; var roots = n.CanvasBasisCurrentRoots(); var fired = false;
            n.AfterGeometryGetter = (name, receiver) =>
            {
                if (fired || name != "get_localPosition") return; fired = true;
                Check(n.CanvasSourceRangeAttempts == 3 && n.CanvasCacheObservedStores.Count == 0,
                    "Preparation callback did not run between cache capture and position sampling.");
                if (kind == "source") n.CanvasSourceSet(n.CanvasSourcePoint(0, true, 0), "value", 123f);
                else if (kind == "cache-baseline") n.CanvasCacheSetBits(3, -1, Bits(77));
                else
                {
                    var before = n.GeometryTransforms[receiver]; n.GeometrySetTransform(receiver, before with { Position = before.Position with { X = before.Position.X + 17 } });
                }
            };
            Reject(() => f.Prepare()); f.Clear();
            Check(fired && n.CanvasSourceRangeAttempts == 3 && n.ActiveRoots == roots.Count,
                "Cross-child preparation accepted a changed baseline/source, recaptured a new Plan, or leaked known roots: " + kind);
            NoWrites(n, "Changed dual baseline was accepted until after a native write: " + kind);
        }
        foreach (var kind in new[] { "cache", "getter", "setter" })
        {
            using var f = new Fixture(n =>
            {
                if (kind == "cache") n.CanvasCacheMetadata(3, "offset");
                else n.CanvasPositionMethodMetadata(kind == "getter", "return");
            });
            Reject(() => f.Prepare()); NoWrites(f.Native, "Malformed exact member survived combined preparation: " + kind);
        }
    }

    private static void EmptyAndInvalid()
    {
        using (var f = new Fixture(n => n.CanvasCacheEmpty()))
        {
            var before = f.Native.CanvasCacheSnapshot(); var basis = f.Prepare(); basis.VerifyPrepared(); basis.Execute();
            Check(basis.VerifyAppliedValues(), "Complete empty basis did not confirm its two scalar targets.");
            var cj = basis.OriginalCacheOwner.GetJournal(); var pj = basis.OriginalPositionOwner.GetJournal();
            Check(cj.Slots.Count == 2 && cj.Slots.Select(row => row.Kind).SequenceEqual(new[] { SeekCanvasCacheKind.NowTime, SeekCanvasCacheKind.Speed })
                && cj.AttemptedWrites == 2 && pj.Targets.Count == 0 && pj.Commands.Count == 0
                && f.Native.CanvasSourceRangeAttempts == 0 && f.Native.CanvasPositionGetterCalls.Count == 0 && f.Native.GeometryCalls.Count == 0
                && !basis.GetJournal().HasRetainedResponsibility && !basis.CompleteSeekPermitted,
                "Empty basis dropped required scalars, invented Transform work, or claimed external effects.");
            basis.RestoreSelectedValues(); Check(basis.VerifyRestoredValues() && f.Native.CanvasCacheSnapshot().SequenceEqual(before),
                "Empty selected scalar originals failed restoration.");
        }
        using var invalid = new Fixture(); var n = invalid.Native;
        NoNative(n, () =>
        {
            Reject(() => invalid.Run.PrepareSeekCanvasBasis(null!, n.CanvasPositionProfile, 5, 3));
            Reject(() => invalid.Run.PrepareSeekCanvasBasis(n.CanvasSourceProfile, null!, 5, 3));
            foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Reject(() => invalid.Run.PrepareSeekCanvasBasis(n.CanvasSourceProfile, n.CanvasPositionProfile, value, 3));
                Reject(() => invalid.Run.PrepareSeekCanvasBasis(n.CanvasSourceProfile, n.CanvasPositionProfile, 5, value));
            }
        });
        Check(n.CanvasSourceRangeAttempts == 0 && !typeof(IGameplayValueParticipant).IsAssignableFrom(typeof(NativeSeekCanvasBasisCommands))
            && !typeof(IGameplayValueParticipant).IsAssignableFrom(typeof(NativeSeekCanvasPositionCommands)),
            "Illegal basis requests reached acquisition or commands were promoted into a full value participant.");
    }

    private static void Aliases()
    {
        using (var f = new Fixture(n =>
        {
            n.CanvasCacheEqualTargets(); n.CanvasCacheAlias(1, 0); n.CanvasCacheAlias(2, 0); n.CanvasPositionAlias(2, 0);
        }))
        {
            var basis = f.Prepare(); var cj = basis.OriginalCacheOwner.GetJournal(); var pj = basis.OriginalPositionOwner.GetJournal();
            Check(cj.Slots.Count == 11 && cj.UniqueSlots.Count == 5 && cj.Slots.Take(9).Select(slot => slot.Owner).Distinct().Count() == 1
                && cj.UniqueSlots.Select(slot => slot.Ordinal).SequenceEqual(new[] { 0, 1, 2, 9, 10 })
                && pj.Targets.Count == 3 && pj.UniqueTargets.Count == 2 && pj.Targets[0].Transform == pj.Targets[2].Transform,
                "Combined equal aliases lost actual occurrences or failed either physical deduplication.");
            basis.Execute(); Check(basis.VerifyAppliedValues() && f.Native.CanvasCacheObservedStores.Count == 5 && f.Native.GeometryCalls.Count == 2,
                "An equal physical cache slot or Transform was dispatched more than once.");
            basis.RestoreSelectedValues(); Check(basis.VerifyRestoredValues() && f.Native.CanvasCacheObservedStores.Count == 10 && f.Native.GeometryCalls.Count == 4,
                "An equal alias was restored twice or an occurrence escaped joint verification.");
        }
        foreach (var kind in new[] { "floor-speed", "x-position", "transform" })
        {
            using var f = new Fixture(n =>
            {
                if (kind == "transform") n.CanvasPositionAlias(1, 0);
                else n.CanvasCacheAlias(kind == "floor-speed" ? 1 : 2, 0);
            });
            var roots = f.Native.ActiveRoots; Reject(() => f.Prepare());
            Check(f.Native.ActiveRoots == roots, "Conflicting combined aliases leaked known construction roots.");
            NoWrites(f.Native, "One conflicting target kind allowed the other child to write: " + kind);
        }
    }

    private static void DependencyOrder()
    {
        using var f = new Fixture(); var n = f.Native; var basis = f.Prepare(); var before = basis.GetJournal();
        var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
        var cj = cache.GetJournal(); var pj = position.GetJournal(); var expected = cj.Slots.Select(row => row.TargetBits).ToArray();
        var ranges = n.CanvasSourceRangeAttempts; var checkedFirst = false;
        n.BeforeGeometryCall = call =>
        {
            if (checkedFirst) return; checkedFirst = true;
            Check(call.Operation == "set_localPosition" && n.CanvasCacheSnapshot().SequenceEqual(expected)
                && n.CanvasCacheObservedStores.Count == cj.UniqueSlots.Count && n.CanvasCacheScalarCalls.Count == 2
                && n.CanvasCacheScalarCalls.Select(row => row.Name).SequenceEqual(new[] { "nowTime", "speed" }),
                "The first actual setter did not see complete arrays, time and speed at its official export boundary.");
        };
        basis.Execute(); f.Clear(); Check(checkedFirst && basis.VerifyAppliedValues(), "Ordered full basis application did not confirm.");
        Check(n.GeometryCalls.Select(call => call.Receiver.ToInt64()).SequenceEqual(pj.Targets.Select(row => row.Transform))
            && n.CanvasCacheSnapshot().SequenceEqual(expected)
            && cj.Slots.Where(row => row.Kind == SeekCanvasCacheKind.FloorSpeeds).Select(row => row.TargetBits).SequenceEqual(new[] { 2f, 3f, 2f }.Select(Bits))
            && n.CanvasSourceRangeAttempts == ranges && n.TargetForbiddenUpdates == 0 && n.GameplayCommandCalls == 0,
            "Basis remapped actual ordinals, multiplied FloorSpeed by request speed, or invoked a producer/consumer.");
        Vector(n.CanvasPositionValue(0), new(-51, 12.75f, 102), "Basis ignored the shared request in its first localPosition.");
        Vector(n.CanvasPositionValue(1), new(-152.5f, -55.875f, 305), "Basis ignored initialized actual ease/range inputs.");
        var order = new List<string>();
        n.AfterGeometryStore = call => order.Add("position:" + Array.IndexOf(n.CanvasPositionTransforms, call.Receiver));
        n.AfterCanvasCacheObservedStore = row =>
        {
            Check(pj.Baseline.Select((value, ordinal) => Same(value, n.CanvasPositionValue(ordinal))).All(value => value),
                "A cache inverse entered before every original position was restored.");
            order.Add("cache:" + cj.Slots.First(slot => slot.Owner == row.Owner.ToInt64() && slot.Index == row.Index && slot.Kind == row.Kind).Ordinal);
        };
        basis.RestoreSelectedValues(); f.Clear(); Check(basis.VerifyRestoredValues(), "Ordered joint selected-value restoration failed.");
        Check(order.SequenceEqual(new[] { "position:2", "position:1", "position:0" }
                .Concat(Enumerable.Range(0, 11).Reverse().Select(ordinal => "cache:" + ordinal)))
            && n.CanvasCacheSnapshot().SequenceEqual(cj.Baseline) && n.CanvasSourceRangeAttempts == ranges,
            "Basis inverse did not preserve child reverse order or recomputed its shared Plan.");
        var applied = basis.GetJournal();
        Check(!before.ExecutionEntered && before.CacheJournal!.Stores.Count == 0 && before.PositionJournal!.Commands.Count == 0
            && applied.ExecutionFinished && applied.RestorationFinished && applied.RestoredValuesVerified
            && applied.ExternalEffectsUnclosed && applied.HasRetainedResponsibility && !applied.CompleteSeekPermitted,
            "Joint restoration mutated prior evidence or discharged entered external engine effects.");
    }

    private static void FreshMetadataAndReferences()
    {
        foreach (var kind in new[] { "scalar-field", "array-field", "array-width", "array-header", "single-width", "vector-width", "vector-offset", "getter", "setter", "position-profile", "cache-reference", "transform-reference" })
        {
            using var f = new Fixture(); var n = f.Native; var basis = f.Prepare();
            var roots = n.CanvasBasisCurrentRoots(); var original = n.CanvasCacheArray(0); var originalBits = n.CanvasBasisDetachedBits(original);
            switch (kind)
            {
                case "scalar-field": n.CanvasCacheMetadata(3, "offset"); break;
                case "array-field": n.CanvasCacheMetadata(1, "byref"); break;
                case "array-width": n.CanvasSourceFault(original, "width"); break;
                case "array-header": n.CanvasSourceFault(original, "header"); break;
                case "single-width": n.CanvasSourceScalarWidth("System.Single", 8); break;
                case "vector-width": n.CanvasPositionVectorMetadata("width"); break;
                case "vector-offset": n.CanvasPositionVectorMetadata("offset"); break;
                case "getter": n.CanvasPositionMethodMetadata(true, "byref-return"); break;
                case "setter": n.CanvasPositionMethodMetadata(false, "byref-parameter"); break;
                case "position-profile": n.CanvasPositionProfile.WrongMember = "GetLocalPositionMethod"; break;
                case "cache-reference": n.CanvasCacheReplace(0, n.CanvasSourceCloneArray(original)); break;
                default: n.CanvasSourceSet(n.GeometryCanvasControl, "canvasTransforms", n.CanvasSourceCloneArray(n.GeometryCanvasArray)); break;
            }
            Reject(basis.Execute);
            Check(basis.OriginalCacheOwner.GetJournal().AttemptedWrites == 0 && basis.OriginalPositionOwner.GetJournal().AttemptedWrites == 0
                && n.CanvasBasisDetachedBits(original).SequenceEqual(originalBits),
                "Fresh exact metadata/reference drift retargeted retained storage or entered a writer: " + kind);
            OriginalRootsRemain(n, roots, "A warm rejected basis released its original independent root: " + kind);
            NoWrites(n, "Combined preflight permitted the other writer after current ABI/member/reference drift: " + kind);
        }
    }
}
