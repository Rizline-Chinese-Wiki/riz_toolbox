using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Judgements.Tests.NoteFlags;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    private static void ExactMetadata()
    {
        CanonicalGetterReferences(); CanonicalFieldReferences();
        foreach (var member in new[] { "ChartClass", "ProgressClass", "InformationClass", "LineControlClass",
            "LineClass", "LinePointClass", "GroupClass", "TransformClass", "GetTransformMethod", "GetParentMethod",
            "GetPositionMethod", "GetLocalPositionMethod", "GetLocalRotationMethod", "GetLocalScaleMethod" })
        {
            using var f = new Fixture(); var basis = f.Prepare(); f.Native.LineRingSourceProfile.WrongMember = member;
            Reject(() => f.Capture(basis)); f.Clear(); NoWrites(f.Native, "A mismatched exact source profile reached a writer: " + member);
        }
        foreach (var kind in new[] { "control", "line", "point", "group", "transform" })
        {
            using var f = new Fixture(); var n = f.Native; var basis = f.Prepare();
            var target = kind switch { "control" => n.LineRingControl, "line" => n.LineRingOrphanDynamic,
                "point" => n.LineRingPoints[^1], "group" => n.LineRingOrphanGroup,
                _ => n.LineRingLineTransform(n.LineRingOrphanDynamic) };
            n.LineRingSetObjectClass(target, n.ClassOf(n.Object)); Reject(() => f.Capture(basis));
        }
        foreach (var field in new[] { "lineControl", "dynamicLines", "lineGroups", "_canvasControl", "_nowTime",
            "lineIsStatic", "canvasIndex", "startLinePoint", "endLinePoint", "endTransform", "endLineInformation",
            "linesGroup", "xPosition", "easeType", "floorPosition", "judgeRingTransform", "ringActive", "lines", "nowLineIndex" })
        foreach (var mode in new[] { "offset", "type", "static" })
        {
            using var f = new Fixture(); var n = f.Native; var basis = f.Prepare();
            var owner = field switch
            {
                "lineControl" => n.GameplayInformation,
                "dynamicLines" or "lineGroups" or "_canvasControl" or "_nowTime" => n.LineRingControl,
                "xPosition" or "easeType" or "floorPosition" => n.LineRingPoints[^1],
                "judgeRingTransform" or "ringActive" or "lines" or "nowLineIndex" => n.LineRingOrphanGroup,
                _ => n.LineRingOrphanDynamic
            };
            n.CanvasSourceMetadata(owner, field, mode); Reject(() => f.Capture(basis));
        }
        foreach (var getter in new[] { "get_transform", "get_parent", "get_position", "get_localPosition", "get_localRotation", "get_localScale" })
        foreach (var mode in new[] { "static", "owner", "name", "return", "parameters", "byref-return" })
        {
            using var f = new Fixture(); var n = f.Native; var source = f.Capture();
            source.VerifySource(); source.VerifyBaseline(); n.LineRingGetterMetadata(getter, mode);
            Reject(source.VerifySource);
            Check(source.GetJournal().SourceExpired && !source.GetJournal().SourceVerified && !source.GetJournal().BaselineVerified,
                "Warm exact getter metadata drift preserved old success: " + getter + "/" + mode);
            NoNative(n, () => Reject(source.VerifySource));
        }
    }

    private static void ArrayIntegrityAndBudgets()
    {
        foreach (var kind in new[] { "dynamic", "groups", "segments" })
        foreach (var mode in new[] { "rank", "element", "width", "length", "bytes", "size", "header", "header-size" })
        {
            using var f = new Fixture(); var n = f.Native; var basis = f.Prepare();
            var array = kind == "dynamic" ? n.LineRingDynamicArray : kind == "groups" ? n.LineRingGroupsArray
                : n.LineRingGroupArray(n.LineRingOrphanGroup);
            n.CanvasSourceFault(array, mode); Reject(() => f.Capture(basis));
            NoWrites(n, "Malformed actual line/ring array reached a writer: " + kind + "/" + mode);
        }
        foreach (var kind in new[] { "dynamic", "groups", "segments" })
        {
            using var f = new Fixture(); var n = f.Native; var basis = f.Prepare();
            n.LineRingSet(kind == "segments" ? n.LineRingOrphanGroup : n.LineRingControl,
                kind == "dynamic" ? "dynamicLines" : kind == "groups" ? "lineGroups" : "lines", IntPtr.Zero);
            Reject(() => f.Capture(basis));
        }
        foreach (var kind in new[] { "dynamic", "groups", "segments" })
        {
            using var f = new Fixture(); var n = f.Native; var source = f.Capture();
            var array = kind == "dynamic" ? n.LineRingDynamicArray : kind == "groups" ? n.LineRingGroupsArray
                : n.LineRingGroupArray(n.LineRingOrphanGroup);
            n.LineRingSet(kind == "segments" ? n.LineRingOrphanGroup : n.LineRingControl,
                kind == "dynamic" ? "dynamicLines" : kind == "groups" ? "lineGroups" : "lines", n.CanvasSourceCloneArray(array));
            Reject(source.VerifySource); Check(source.GetJournal().SourceExpired,
                "An equal-content replacement array retargeted the retained source: " + kind);
        }
        foreach (var at in new[] { "length", "slot" })
        {
            using var f = new Fixture(); var n = f.Native; var basis = f.Prepare(); var fired = false;
            if (at == "length") n.AfterGeometryArrayExport = (name, array) =>
            {
                if (fired || name != "length" || array != n.LineRingDynamicArray) return;
                fired = true; n.CanvasSourceFaultArray = array; n.CanvasSourceArrayFault = "length";
            };
            else n.AfterCanvasSourceRoot = (_, target, _) =>
            {
                if (fired || target != n.LineRingOrphanDynamic) return;
                fired = true; n.LineRingSetSlot(n.LineRingDynamicArray, 2, n.LineRingLines[0]);
            };
            Reject(() => f.Capture(basis)); f.Clear(); Check(fired,
                "The changing complete array did not reach its official publication boundary: " + at);
        }
        using (var f = new Fixture(n => n.LineRingSetDynamic(Enumerable.Repeat(n.LineRingOrphanDynamic,
            NativeSeekLineRingSourceCapture.MaximumDynamicOccurrences + 1).ToArray())))
        {
            Reject(() => f.Capture());
            Check(f.Native.LineRingGetterCalls.All(call => call.Name == "get_localPosition"),
                "An oversized dynamic occurrence array was consumed as a supported prefix.");
        }
        using (var f = new Fixture(n => n.LineRingSetGroups(Enumerable.Repeat(n.LineRingOrphanGroup,
            NativeSeekLineRingSourceCapture.MaximumGroupOccurrences + 1).ToArray()))) Reject(() => f.Capture());
        using (var f = new Fixture(n =>
        {
            var half = NativeSeekLineRingSourceCapture.MaximumGroupLineOccurrences / 2 + 1;
            n.LineRingSetGroupLines(n.LineRingGroups[0], Enumerable.Repeat(n.LineRingLines[0], half).ToArray());
            n.LineRingSetGroupLines(n.LineRingOrphanGroup, Enumerable.Repeat(n.LineRingOrphanDynamic, half).ToArray());
        })) Reject(() => f.Capture());
        using (var f = new Fixture(n => n.LineRingAddParentChain(257)))
        {
            var n = f.Native; var basis = f.Prepare(); var transform = n.LineRingLineTransform(n.LineRingOrphanDynamic);
            Reject(() => f.Capture(basis));
            Check(n.LineRingGetterCalls.All(call => call.Receiver != transform || call.Name is "get_parent"),
                "Unsupported parent depth entered a pose getter before closure admission.");
        }
    }

    private static void SourceDrift()
    {
        foreach (var field in new[] { "time", "xPosition", "floorPosition", "easeType", "canvasIndex" })
        {
            using var f = new Fixture(); var n = f.Native; var source = f.Capture();
            var point = n.LineRingPoints[^1]; var before = source.Snapshot.Points.Single(row => row.Reference == point.ToInt64());
            object original = field switch { "time" => before.Time, "xPosition" => before.XPosition,
                "floorPosition" => before.FloorPosition, "easeType" => before.EaseType, _ => before.CanvasIndex };
            object changed = field is "easeType" or "canvasIndex" ? 99 : 99.125f;
            n.LineRingSet(point, field, changed); Reject(source.VerifySource);
            Check(source.GetJournal().SourceExpired && !source.GetJournal().SourceVerified,
                "Endpoint input drift preserved source success: " + field);
            n.LineRingSet(point, field, original); NoNative(n, () => { Reject(source.VerifySource); Reject(source.VerifyBaseline); });
            Check(source.Snapshot.Points.Single(row => row.Reference == point.ToInt64()) == before,
                "A failed fresh point observation mutated the original immutable input history.");
        }
        foreach (var field in new[] { "startLinePoint", "endLinePoint", "linesGroup", "endLineInformation", "endTransform", "lineIsStatic", "canvasIndex" })
        {
            using var f = new Fixture(); var n = f.Native; var source = f.Capture(); var line = n.LineRingOrphanDynamic;
            var before = source.Snapshot.Lines.Single(row => row.Reference == line.ToInt64());
            object changed = field switch
            {
                "startLinePoint" or "endLinePoint" => n.LineRingPoints[0], "linesGroup" => n.LineRingGroups[0],
                "endLineInformation" => n.LineRingLines[0], "endTransform" => n.GeometryUiRoot,
                "lineIsStatic" => true, _ => 2
            };
            n.LineRingSet(line, field, changed); Reject(source.VerifySource);
            Check(source.GetJournal().SourceExpired && source.Snapshot.Lines.Single(row => row.Reference == line.ToInt64()) == before,
                "Line identity/backreference drift was accepted or rewrote history: " + field);
        }
        using (var f = new Fixture())
        {
            var source = f.Capture(); f.Native.LineRingSet(f.Native.LineRingOrphanGroup, "judgeRingTransform", f.Native.GeometryUiRoot);
            Reject(source.VerifySource); Check(source.GetJournal().SourceExpired, "Ring target identity was treated as a mutable baseline pose.");
        }
        using (var f = new Fixture())
        {
            var source = f.Capture(); var n = f.Native;
            n.LineRingSet(n.LineRingControl, "levelInformation", n.CanvasSourceCloneObject(n.GameplayInformation));
            Reject(source.VerifySource); Check(source.GetJournal().SourceExpired, "LineControl accepted another information backpointer.");
        }
    }

    private static void BaselineSeparation()
    {
        BaselineObservationInstability();
        foreach (var field in new[] { "_nowTime", "_sceneWidth", "_speed", "_gameSceneScale", "nowLineIndex", "ringActive", "position", "rotation", "scale", "parent" })
        {
            using var f = new Fixture(); var n = f.Native; var source = f.Capture(); var before = source.Snapshot;
            source.VerifySource(); source.VerifyBaseline();
            if (field.StartsWith('_')) n.LineRingSet(n.LineRingControl, field,
                field == "_nowTime" ? 0f : field == "_sceneWidth" ? BitConverter.Int32BitsToSingle(unchecked((int)0x7fc04321)) : 19.5f);
            else if (field == "nowLineIndex") n.LineRingSet(n.LineRingOrphanGroup, field, 29);
            else if (field == "ringActive") n.LineRingSet(n.LineRingOrphanGroup, field, false);
            else
            {
                var transform = n.LineRingLineTransform(n.LineRingOrphanDynamic); var old = n.GeometryTransforms[transform];
                n.GeometrySetTransform(transform, field switch
                {
                    "position" => old with { Position = new(-.0f, 55f, -4f) },
                    "rotation" => old with { Rotation = new(0, 0, 1, 0) },
                    "scale" => old with { Scale = new(3, 4, 5) },
                    _ => old with { Parent = n.GeometryNewTransform(IntPtr.Zero, new(7, 8, 9), new(0, 0, 0, 1), new(1, 1, 1)) }
                });
            }
            source.VerifySource();
            Check(source.GetJournal().SourceVerified && !source.GetJournal().SourceExpired,
                "Mutable scalar/selection/pose was folded into SourceGraph identity: " + field);
            Reject(source.VerifyBaseline);
            Check(!source.GetJournal().BaselineVerified && !source.GetJournal().SourceExpired,
                "A changed baseline retained old success or poisoned stable source identity: " + field);
            source.VerifySource();
            Check(ReferenceEquals(source.Snapshot, before) && before.ControlBaseline.NowTimeBits == unchecked((int)0x80000000)
                && before.ControlBaseline.SceneWidthBits == unchecked((int)0x7fc01234),
                "A fresh baseline observation rewrote original NaN/-0 history.");
        }
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); var source = f.Capture(basis); var n = f.Native;
            n.CanvasCacheSetBits(3, -1, Bits(888f)); n.CanvasCacheSetBits(4, -1, Bits(-19f));
            n.CanvasPositionSet(0, new(77, 88, 99)); source.VerifySource(); source.VerifyBaseline();
            Check(source.GetJournal().BaselineVerified,
                "Independent Canvas cache/position values were mistaken for line/ring source or baseline identity.");
        }
    }

    private static void CanonicalGetterReferences()
    {
        foreach (var getter in new[] { "get_transform", "get_parent" })
        {
            using var f = new Fixture(); var n = f.Native; var source = f.Capture();
            source.VerifySource(); source.VerifyBaseline(); var calls = n.LineRingGetterCalls.Count;
            n.LineRingGetterMetadata(getter, "same-name-reference"); Reject(source.VerifySource);
            Check(source.GetJournal().SourceExpired && !source.GetJournal().SourceVerified
                && !source.GetJournal().BaselineVerified && n.LineRingGetterCalls.Count == calls,
                "A same-named foreign getter return class passed fresh exact metadata: " + getter);
            NoNative(n, () => Reject(source.VerifySource));
        }
    }

    private static void CanonicalFieldReferences()
    {
        using var f = new Fixture(); var n = f.Native; var basis = f.Prepare();
        // Optional null values cannot expose a forged declaration by inspecting
        // the returned object's class. Admission must reject the metadata itself.
        foreach (var line in n.LineRingLines) n.LineRingSet(line, "endTransform", IntPtr.Zero);
        n.LineRingSpoofReferenceField(n.LineRingOrphanDynamic, "endTransform");
        var calls = n.LineRingGetterCalls.Count; Reject(() => f.Capture(basis));
        Check(n.LineRingGetterCalls.Count == calls,
            "A same-named foreign nullable field class reached a getter before metadata refusal.");
    }

    private static void BaselineObservationInstability()
    {
        using var f = new Fixture(); var n = f.Native; var basis = f.Prepare(); var source = f.Capture(basis);
        var transform = n.LineRingLineTransform(n.LineRingOrphanDynamic); var original = n.GeometryTransforms[transform];
        var replacement = n.GeometryNewTransform(IntPtr.Zero, new(7, 8, 9), new(0, 0, 0, 1), new(1, 1, 1));
        source.VerifyBaseline(); var history = source.GetJournal(); var fired = false;
        n.AfterGeometryGetter = (name, receiver) =>
        {
            if (fired || name != "get_position" || receiver != transform) return;
            fired = true; n.GeometrySetTransform(transform, original with { Parent = replacement });
        };
        Reject(source.VerifySource); f.Clear(); var refused = source.GetJournal();
        Check(fired && !refused.SourceExpired && !refused.SourceVerified && !refused.BaselineVerified
            && history.BaselineVerified && history.SourceVerified,
            "A parent change during baseline sampling permanently expired stable source identity or retained stale proof.");
        source.VerifySource(); Reject(source.VerifyBaseline);
        Check(!source.GetJournal().SourceExpired,
            "A stable replacement parent was mistaken for a new source graph.");
        n.GeometrySetTransform(transform, original);
        source.VerifyBaseline(); basis.OriginalCacheOwner.VerifyPrepared(); basis.OriginalPositionOwner.VerifyPrepared();
        Check(source.GetJournal().BaselineVerified && source.GetJournal().SourceVerified,
            "Restoring a baseline after observation instability could not establish a fresh proof.");
        foreach (var cycle in new[] { true, false })
        {
            n.LineRingAddParentChain(cycle ? 2 : NativeSeekLineRingSourceCapture.MaximumParentDepth + 1, cycle);
            Reject(source.VerifySource);
            Check(!source.GetJournal().SourceExpired && !source.GetJournal().SourceVerified
                && !source.GetJournal().BaselineVerified,
                "An unsupported current baseline parent topology permanently expired immutable source identity.");
            n.GeometrySetTransform(transform, original); source.VerifyBaseline();
            Check(source.GetJournal().BaselineVerified,
                "A restored supported baseline could not retry after a parent topology refusal.");
        }
    }

    private static void OriginalCanvasDrift()
    {
        foreach (var kind in new[] { "move", "list", "ease", "vector", "notearray", "chartpoint" })
        {
            using var f = new Fixture(); var n = f.Native; var source = f.Capture(); var serial = Serial(f.Run); var ranges = n.CanvasSourceRangeAttempts;
            switch (kind)
            {
                case "move": n.CanvasSourceSet(n.CanvasSourceMoves[0], "index", 123); break;
                case "list": n.CanvasSourceReplaceList(0, true); break;
                case "ease": n.GeometrySingleSlot(n.CanvasSourceSamples[0], 99, .875f); break;
                case "vector": n.CanvasSourceSetVector("downVector", new(12, 13, 14)); break;
                case "notearray": n.LineRingNoNotes(); break;
                default: n.LineRingSet(n.NoteViewPoints[0][0], "time", .0625f); break;
            }
            Reject(source.VerifySource);
            Check(source.GetJournal().SourceExpired && !source.GetJournal().SourceVerified && !source.GetJournal().BaselineVerified
                && n.CanvasSourceRangeAttempts == ranges && Serial(f.Run) == serial,
                "Original Canvas/NoteView drift was accepted by retargeting or recapturing a numeric Plan: " + kind);
            NoNative(n, () => { Reject(source.VerifySource); Reject(source.VerifyBaseline); });
        }
    }
}
