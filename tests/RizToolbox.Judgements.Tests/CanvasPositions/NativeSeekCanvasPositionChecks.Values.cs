using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekCanvasPositionChecks
{
    private static void Preparation()
    {
        using var f = new Fixture(); var n = f.Native; var targets = n.CanvasPositionTransforms;
        var baseline = targets.Select(t => n.GeometryTransforms[t].Position).ToArray(); var p = f.Prepare(); p.VerifyPrepared();
        var j = p.GetJournal();
        Check(j.Prepared && !j.ExecutionEntered && !j.AppliedValuesVerified && !j.RestorationEntered
            && !j.CompleteSeekPermitted && !j.HasRetainedResponsibility && j.AttemptedWrites == 0 && j.Commands.Count == 0,
            "Read-only preparation advertised execution, ownership, confirmation, or complete Seek.");
        Check(j.Targets.Count == 3 && j.UniqueTargets.Count == 3
            && j.Targets.Select(t => t.Ordinal).SequenceEqual(new[] { 0, 1, 2 })
            && j.Targets.Select(t => t.Transform).SequenceEqual(targets.Select(t => t.ToInt64()))
            && j.Baseline.Zip(baseline).All(pair => Same(pair.First, pair.Second)),
            "Preparation dropped/reordered actual Transform occurrences or failed to read actual local baselines.");
        Vector(j.Targets[0].LocalPosition, new(-51, 12.75f, 102), "Writer ignored explicit chartTime/speed or copied current Canvas fields.");
        Vector(j.Targets[1].LocalPosition, new(-152.5f, -55.875f, 305), "Writer replaced actual initialized ease/range inputs with a theoretical projection.");
        Check(n.GeometryCalls.Count == 0 && n.CanvasPositionGetterCalls.Count >= targets.Length
            && n.CanvasSourceRanges.Select(r => r.To).All(t => t == 5d)
            && targets.All(n.HasRootFor), "Preparation wrote targets, did not invoke actual getters, or borrowed caller roots.");
        f.UnrelatedState();
    }
    private static void ActualOrder()
    {
        using var f = new Fixture(); var p = f.Prepare(); var before = p.GetJournal();
        var baseline = before.Baseline.ToArray(); var targets = before.Targets.ToArray(); f.Execute(p);
        Check(p.VerifyAppliedValues(), "Actual successful localPosition sequence could not confirm complete requested values.");
        var applied = p.GetJournal();
        Check(applied.ExecutionEntered && applied.ExecutionFinished && applied.AppliedValuesVerified
            && applied.AttemptedWrites == 3 && applied.Commands.Count == 3
            && f.Native.GeometryCalls.Select(c => c.Receiver.ToInt64()).SequenceEqual(targets.Select(t => t.Transform))
            && applied.Commands.Select(c => c.Ordinal).SequenceEqual(new[] { 0, 1, 2 })
            && applied.Commands.All(c => c.NativeEntered && c.NativeReturned && c.TargetObserved && c.Readback is not null),
            "Writer sorted by move.index, skipped an actual Transform, or invented command/readback evidence.");
        foreach (var target in targets) Vector(f.Native.GeometryTransforms[new(target.Transform)].Position, target.LocalPosition, "Actual Transform differs from requested localPosition.");
        Check(!before.ExecutionEntered && before.Commands.Count == 0 && !before.AppliedValuesVerified
            && before.Baseline.Zip(baseline).All(pair => Same(pair.First, pair.Second)), "Execution mutated an earlier immutable effects journal.");
        if (applied.Targets is IList<SeekCanvasPositionTarget> writable)
            Check(Reject(() => writable[0] = targets[1]) is NotSupportedException, "Journal exposes writable target occurrences.");
        if (applied.Commands is IList<SeekCanvasPositionCommandEffects> commands)
            Check(Reject(() => commands.Clear()) is NotSupportedException, "Journal exposes writable command evidence.");
        p.RestoreSelectedValues(); Check(p.VerifyRestoredValues(), "Complete selected values did not restore to their exact original bits.");
        var restored = p.GetJournal();
        Check(restored.RestorationEntered && restored.RestorationFinished && restored.RestoredValuesVerified
            && restored.AttemptedRestores == 3 && restored.Commands.Count == 6
            && applied.Commands.Count == 3 && !applied.RestorationEntered,
            "Restoration did not preserve independent immutable apply and restore evidence.");
        for (var i = 0; i < baseline.Length; ++i) Vector(f.Native.CanvasPositionValue(i), baseline[i], "Restored actual Canvas position differs.");
    }
    private static void Aliases()
    {
        using (var f = new Fixture(n => n.CanvasPositionAlias(2, 0)))
        {
            var p = f.Prepare(); var j = p.GetJournal();
            Check(j.Targets.Count == 3 && j.UniqueTargets.Count == 2
                && j.Targets[0].Transform == j.Targets[2].Transform
                && j.UniqueTargets.Select(t => t.Ordinal).SequenceEqual(new[] { 0, 1 }),
                "Shared actual Transform removed occurrences or lost first-occurrence order.");
            f.Execute(p); Check(p.VerifyAppliedValues() && f.Native.GeometryCalls.Count == 2,
                "Equal repeated Transform target dispatched twice or lost complete occurrence verification.");
            p.RestoreSelectedValues(); Check(p.VerifyRestoredValues() && f.Native.GeometryCalls.Count == 4,
                "Equal aliases were restored twice or left an occurrence unverified.");
        }
        using var conflict = new Fixture(n => n.CanvasPositionAlias(1, 0)); var roots = conflict.Native.ActiveRoots;
        Reject(() => conflict.Prepare());
        Check(conflict.Native.GeometryCalls.Count == 0 && conflict.Native.ActiveRoots == roots,
            "Conflicting target values for one actual Transform reached a setter or leaked known construction roots.");
    }
    private static void EmptyAndInvalid()
    {
        using (var f = new Fixture(n => n.CanvasSourceEmpty()))
        {
            var p = f.Prepare(); p.VerifyPrepared(); f.Execute(p);
            Check(p.VerifyAppliedValues(), "Equal empty actual source vectors did not permit complete empty value verification.");
            p.RestoreSelectedValues(); Check(p.VerifyRestoredValues(), "Empty restoration failed.");
            var j = p.GetJournal();
            Check(j.Targets.Count == 0 && j.UniqueTargets.Count == 0 && j.Baseline.Count == 0 && j.Commands.Count == 0
                && !j.HasRetainedResponsibility && !j.CompleteSeekPermitted
                && f.Native.CanvasPositionGetterCalls.Count == 0 && f.Native.GeometryCalls.Count == 0 && f.Native.CanvasSourceRangeAttempts == 0,
                "Empty vectors created synthetic Transform operations or claimed entered engine responsibility.");
        }
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        foreach (var time in new[] { false, true })
        {
            using var f = new Fixture(); var roots = f.Native.ActiveRoots;
            Reject(() => f.Prepare(time ? value : 5, time ? 3 : value));
            Check(f.Native.CanvasSourceRangeAttempts == 0 && f.Native.CanvasPositionGetterCalls.Count == 0
                && f.Native.GeometryCalls.Count == 0 && f.Native.ActiveRoots == roots,
                "Nonfinite explicit request reached range/Transform native code or retained known roots.");
        }
    }
    private static void GetterMetadata()
    {
        foreach (var mode in new[] { "static", "owner", "name", "return", "parameters", "arity", "byref-return" })
        {
            using var f = new Fixture(n => n.CanvasPositionMethodMetadata(true, mode)); var roots = f.Native.ActiveRoots;
            Reject(() => f.Prepare());
            Check(f.Native.CanvasPositionGetterCalls.Count == 0 && f.Native.GeometryCalls.Count == 0 && f.Native.ActiveRoots == roots,
                "Malformed exact localPosition getter entered native or leaked preparation roots: " + mode);
        }
        foreach (var member in new[] { "TransformClass", "GetLocalPositionMethod" })
        {
            using var f = new Fixture(); f.Native.CanvasPositionProfile.WrongMember = member;
            Reject(() => f.Prepare());
            Check(f.Native.CanvasPositionGetterCalls.Count == 0 && f.Native.GeometryCalls.Count == 0,
                "Canvas position getter accepted another selected profile member: " + member);
        }
    }
    private static void SetterMetadata()
    {
        foreach (var mode in new[] { "static", "owner", "name", "return", "parameters", "arity", "byref-return", "byref-parameter" })
        {
            using var f = new Fixture(n => n.CanvasPositionMethodMetadata(false, mode)); var roots = f.Native.ActiveRoots;
            Reject(() => f.Prepare());
            Check(f.Native.GeometryCalls.Count == 0 && f.Native.ActiveRoots == roots,
                "Malformed exact localPosition setter entered native or leaked preparation roots: " + mode);
        }
        using var wrong = new Fixture(); wrong.Native.CanvasPositionProfile.WrongMember = "SetLocalPositionMethod";
        Reject(() => wrong.Prepare()); Check(wrong.Native.GeometryCalls.Count == 0, "Canvas writer used another selected setter MethodInfo.");
    }
    private static void WarmMetadata()
    {
        foreach (var mode in new[] { "width", "offset", "field-type", "single-width" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var count = f.Native.CanvasPositionGetterCalls.Count;
            f.Native.CanvasPositionVectorMetadata(mode); Reject(p.Execute);
            Check(f.Native.GeometryCalls.Count == 0 && p.GetJournal().AttemptedWrites == 0
                && f.Native.CanvasPositionGetterCalls.Count == count,
                "Warm Vector3 ABI drift reached localPosition native entry: " + mode);
        }
        foreach (var getter in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare();
            f.Native.CanvasPositionMethodMetadata(getter, getter ? "byref-return" : "byref-parameter"); Reject(p.Execute);
            Check(f.Native.GeometryCalls.Count == 0 && p.GetJournal().AttemptedWrites == 0,
                "Saved localPosition declaration drift was ignored after preparation.");
        }
        using var changed = new Fixture(); var q = changed.Prepare(); changed.Native.CanvasPositionProfile.WrongMember = "GetLocalPositionMethod";
        Reject(q.VerifyPrepared); Check(changed.Native.GeometryCalls.Count == 0, "Current profile drift silently retargeted a saved writer.");
    }
    private static void RawBits()
    {
        var negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        var nan = BitConverter.Int32BitsToSingle(unchecked((int)0xffc12345));
        var original = new NativeVector3(negativeZero, nan, BitConverter.Int32BitsToSingle(0x7fc54321));
        using var f = new Fixture(n => n.CanvasPositionSet(0, original)); var p = f.Prepare();
        Vector(p.GetJournal().Baseline[0], original, "Baseline canonicalized signed zero or NaN payload bits.");
        f.Execute(p); Check(p.VerifyAppliedValues(), "Nonfinite original localPosition prevented a finite requested target.");
        p.RestoreSelectedValues(); Check(p.VerifyRestoredValues(), "Conditional original restore compared NaN numerically or lost signed zero.");
        Vector(f.Native.CanvasPositionValue(0), original, "Restored actual localPosition canonicalized baseline bits.");
        var j = p.GetJournal(); var restored = j.Commands.Single(c => c.Direction == SeekCanvasPositionDirection.Restore && c.Ordinal == 0);
        Vector(restored.Target, original, "Restoration journal lost the original vector bits.");
        Check(restored.OriginalObserved && restored.Readback is NativeVector3 readback && Same(readback, original),
            "Restore evidence did not compare original Single bits exactly.");
    }
}
