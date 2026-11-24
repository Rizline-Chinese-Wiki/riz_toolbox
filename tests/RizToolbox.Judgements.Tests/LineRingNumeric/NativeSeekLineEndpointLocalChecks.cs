using System.Reflection;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.LineRingNumeric;

/// <summary>
/// Controlled reflection fixture for pure endpoint-local projection. These
/// checks do not verify native initialization provenance or parent hierarchy.
/// </summary>
internal static class NativeSeekLineEndpointLocalChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Line ring numeric endpoint local slots and Single goldens", SlotsAndGoldens),
        ("Line ring numeric endpoint same-canvas binding remains unproven", SameCanvasBinding),
        ("Line ring numeric endpoint baseline and canvas association", BaselineAndCanvasAssociation),
        ("Line ring numeric endpoint physical Transform aliases", TransformAliases),
        ("Line ring numeric endpoint occurrence and duplicate index guards", OccurrenceAndDuplicateIndex),
        ("Line ring numeric endpoint guard reentry and recovery", ReentryAndRecovery)
    };
    private static int _checks;
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static void Check(bool yes, string message)
    {
        ++_checks;
        if (!yes) throw new Exception(message);
    }
    private static void Reject<T>(Action action, string message) where T : Exception
    {
        ++_checks;
        try { action(); }
        catch (T) { return; }
        throw new Exception(message);
    }
    private static SeekCanvasTargetKeyPoint Key(long reference) => new(reference, 0f, 0f, 0, 0f);
    private static SeekCanvasTargetMove Move(long reference, int index)
        => new(reference, index, new[] { Key(reference + 100) }, new[] { Key(reference + 200) });

    private static (SeekCanvasNumericBasis Basis, SeekLineRingSourceSnapshot Source) Fixture(bool duplicateIndex = false)
    {
        var moves = new[] { Move(10, 42), Move(20, 7), Move(30, duplicateIndex ? 7 : 9) };
        var plan = NativeSeekCanvasTargetPlan.Capture(moves,
            new(0f, 2f, 9f, new(0f, -1f, 0f), new(1f, 0f, 0f)),
            (_, _) => 0f, (_, _) => 0f, () => { });
        var capture = (NativeSeekCanvasTargetCapture)Activator.CreateInstance(
            typeof(NativeSeekCanvasTargetCapture), nonPublic: true)!;
        typeof(NativeSeekCanvasTargetCapture).GetField("_plan", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(capture, plan);
        var run = new ObservedJudgementRun("s", "r", 1, 1, 100, 200);
        var notes = new SeekRuntimeNoteSourceIdentity(new SeekChartIdentity(run, 300, 400, 500),
            600, 700, 800);
        var canvasIdentity = new SeekCanvasSourceIdentity(notes, 100, 400, 900);
        var basis = (SeekCanvasNumericBasis)typeof(SeekCanvasNumericBasis)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
            .Invoke(new object[] { capture, plan, canvasIdentity, 1000L,
                new long[] { 100, 200, 300 }, 1001L,
                new[] { new SeekCanvasSourceEase(1002, 0, 1003, new float[101]) } });
        var points = new[]
        {
            new SeekLineRingPoint(701, 1000f, 2f, 0, 7, 3f),
            new SeekLineRingPoint(702, 1500f, -1f, 0, 42, 4f),
            new SeekLineRingPoint(703, 1000f, 2f, 0, 7, 3f),
            new SeekLineRingPoint(704, 2000f, 3f, 0, 7, 4f),
            new SeekLineRingPoint(705, 3000f, 4f, 0, 7, 5f),
        };
        var lines = new[]
        {
            new SeekLineRingLine(10001, false, 1, 501, 701, 702, 502, 0, 801),
            new SeekLineRingLine(10002, true, 1, 503, 703, 704, 0, 10003, 801),
            new SeekLineRingLine(10003, true, 1, 504, 704, 705, 0, 0, 801),
        };
        SeekLineRingTransformBaseline Transform(long reference)
            => new(reference, 0, new(99f, 99f, 99f), new(99f, 99f, 99f),
                new NativeRendererQuaternion(0f, 0f, 0f, 1f), new(1f, 1f, 1f));
        var source = new SeekLineRingSourceSnapshot(
            new(capture, plan, notes, 100, 900, 901),
            1100, new long[] { 10001 }, 1101, new long[] { 801 },
            lines, points, new[] { new SeekLineRingGroup(801, 505, 1102, new long[] { 10002, 10003 }) },
            new(Bits(99f), Bits(99f), Bits(99f), Bits(99f)),
            new[] { new SeekLineRingGroupBaseline(801, 0, false) },
            new[] { Transform(501), Transform(502), Transform(503), Transform(504), Transform(505) },
            Array.Empty<SeekLineRingChartPoint>(), Array.Empty<SeekLineRingNoteJoin>());
        return (basis, source);
    }

    private static void SlotsAndGoldens()
    {
        var (basis, source) = Fixture();
        var target = NativeSeekLineEndpointLocalTarget.Project(basis, source, () => { });
        Check(target.LocalTargets.Count == 4 && target.Rows.Count == 3, "Full physical line coverage failed without Notes.");
        var start = target.LocalTargets.Single(w => w.Line == 10001 && w.Role == SeekLineEndpointLocalRole.Start);
        var end = target.LocalTargets.Single(w => w.Line == 10001 && w.Role == SeekLineEndpointLocalRole.DedicatedCrossCanvasEnd);
        Check(start.CanvasSlot == 1 && start.ParentTransform == 200 && start.Transform == 501,
            "Raw canvas index was substituted for the first actual CanvasMove slot.");
        Check(end.CanvasSlot == 0 && end.ParentTransform == 100 && end.Transform == 502,
            "The dedicated cross-canvas end used the start canvas slot.");
        Check(Bits(start.LocalPosition.X) == 0x41340000 && Bits(start.LocalPosition.Y) == 0x40c00000
            && Bits(start.LocalPosition.Z) == 0x3f170a3e,
            "Start local Single golden changed or used target ScreenWidth instead of native 5.625.");
        Check(Bits(end.LocalPosition.X) == unchecked((int)0xc0b40000)
            && Bits(end.LocalPosition.Y) == 0x41000000 && Bits(end.LocalPosition.Z) == 0x3f000000,
            "Cross-canvas end used next.time or the start time scale.");
    }

    private static void SameCanvasBinding()
    {
        var (basis, source) = Fixture();
        var target = NativeSeekLineEndpointLocalTarget.Project(basis, source, () => { });
        var shared = target.Rows.Single(r => r.Line == 10002);
        var terminal = target.Rows.Single(r => r.Line == 10003);
        Check(shared.EndBinding == SeekLineEndBinding.SameCanvasEndLineReference && shared.EndLine == 10003
            && shared.InitializedEndTransform == 0 && shared.InitializedEndCanvasSlot is null,
            "An EndLine reference was promoted to an unproven shared endpoint Transform.");
        Check(terminal.EndBinding == SeekLineEndBinding.SameCanvasNoEndLine
            && terminal.InitializedEndTransform == 0 && terminal.InitializedEndCanvasSlot is null,
            "Terminal same-canvas end invented an initialized physical Transform.");
        Check(!target.CompleteSeekPermitted && source.NoteJoins.Count == 0,
            "Local targets were treated as full Seek or required a Note join.");
        var nonAdjacentLink = source with
        { Lines = source.Lines.Select(l => l.Reference == 10002 ? l with { EndPoint = 705 } : l).ToArray() };
        var unbound = NativeSeekLineEndpointLocalTarget.Project(basis, nonAdjacentLink, () => { })
            .Rows.Single(r => r.Line == 10002);
        Check(unbound.EndBinding == SeekLineEndBinding.SameCanvasEndLineReference
            && unbound.EndLine == 10003 && unbound.InitializedEndTransform == 0,
            "A nonadjacent native EndLine link was inferred to share an endpoint Transform.");
        var staleSameCanvasEnd = NativeSeekLineEndpointLocalTarget.Project(basis, source with
        { Lines = source.Lines.Select(l => l.Reference == 10002 ? l with { EndTransform = 502 } : l).ToArray() }, () => { })
            .Rows.Single(r => r.Line == 10002);
        Check(staleSameCanvasEnd.InitializedEndTransform == 0,
            "An observed same-canvas endTransform field was promoted to a Start-initialized target.");
    }

    private static void BaselineAndCanvasAssociation()
    {
        var (basis, source) = Fixture();
        var start = NativeSeekLineEndpointLocalTarget.Project(basis, source, () => { })
            .LocalTargets.Single(w => w.Line == 10001 && w.Role == SeekLineEndpointLocalRole.Start);
        var changedBaseline = source with
        {
            ControlBaseline = source.ControlBaseline with { SpeedBits = Bits(500f) },
            Transforms = source.Transforms.Select(t => t with { Position = new(123f, 234f, 345f) }).ToArray()
        };
        var again = NativeSeekLineEndpointLocalTarget.Project(basis, changedBaseline, () => { });
        Check(Bits(again.LocalTargets[0].LocalPosition.Y) == Bits(start.LocalPosition.Y),
            "An old baseline speed or world pose influenced target local position.");
        Reject<InvalidDataException>(() => NativeSeekLineEndpointLocalTarget.Project(basis, source with
        { Lines = source.Lines.Select(l => l.Reference == 10001 ? l with { CanvasIndex = 7 } : l).ToArray() }, () => { }),
            "A raw point.canvasIndex was silently accepted as LineInformation slot.");
        Reject<InvalidDataException>(() => NativeSeekLineEndpointLocalTarget.Project(basis, source with
        { Points = source.Points.Select(p => p.Reference == 701 ? p with { CanvasIndex = 99 } : p).ToArray() }, () => { }),
            "A LinePoint with no CanvasMove.index match was accepted.");
    }

    private static void TransformAliases()
    {
        var (basis, source) = Fixture();
        Reject<InvalidDataException>(() => NativeSeekLineEndpointLocalTarget.Project(basis, source with
        { Lines = source.Lines.Select(l => l.Reference == 10003 ? l with { Transform = 503 } : l).ToArray() }, () => { }),
            "Contradictory physical Transform alias was accepted.");
        var equalAlias = NativeSeekLineEndpointLocalTarget.Project(basis, source with
        { Lines = source.Lines.Select(l => l.Reference == 10002 ? l with { Transform = 501 } : l).ToArray() }, () => { });
        Check(equalAlias.LocalTargets.Count(w => w.Transform == 501) == 2,
            "An identical physical Transform alias was discarded instead of preserving both line rows.");
    }

    private static void OccurrenceAndDuplicateIndex()
    {
        var (basis, source) = Fixture();
        Reject<InvalidDataException>(() => NativeSeekLineEndpointLocalTarget.Project(basis, source with
        { DynamicLines = new long[] { 0, 10001 } }, () => { }),
            "A null dynamic occurrence was dropped as a prefix.");
        var duplicate = Fixture(duplicateIndex: true);
        Reject<NotSupportedException>(() => NativeSeekLineEndpointLocalTarget.Project(
            duplicate.Basis, duplicate.Source, () => { }),
            "A repeated CanvasMove.index was collapsed to a guessed first match.");
    }

    private static void ReentryAndRecovery()
    {
        var (basis, source) = Fixture();
        Reject<InvalidOperationException>(() => NativeSeekLineEndpointLocalTarget.Project(
            basis, source, () => NativeSeekLineEndpointLocalTarget.Project(basis, source, () => { })),
            "A guard callback could synchronously reenter endpoint projection.");
        Check(NativeSeekLineEndpointLocalTarget.Project(basis, source, () => { }).LocalTargets.Count == 4,
            "Synchronous reentry failure poisoned a later independent projection.");
    }
}
