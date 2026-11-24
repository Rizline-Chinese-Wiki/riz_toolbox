using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekCanvasSourceChecks
{
    private static void InvalidContainers()
    {
        foreach (var kind in new[] { "transforms", "moves", "keypoints", "ease", "samples" })
        foreach (var fault in new[] { "rank", "element", "width", "bytes", "size", "header", "header-size", "length" })
        {
            using var f = new Fixture(); var roots = f.Native.ActiveRoots;
            var array = kind switch { "transforms" => f.Native.GeometryCanvasArray, "moves" => f.Native.CanvasSourceMovesArray,
                "keypoints" => f.Native.CanvasSourceBacking(f.Native.CanvasSourceList(0, true)),
                "ease" => f.Native.CanvasSourceEaseArray, _ => f.Native.CanvasSourceSamples[0] };
            f.Native.CanvasSourceFault(array, fault); Reject(() => f.Capture());
            Check(f.Native.ActiveRoots == roots && f.Native.CanvasSourceRangeAttempts == 0,
                "Malformed complete Canvas vector entered projection or leaked physical roots: " + kind + "/" + fault);
        }
        foreach (var mode in new[] { "canvas-null", "information", "transform-null", "moves-null", "count-short", "count-long", "transform-slot-null", "move-slot-null", "foreign-transform", "foreign-move" })
        {
            using var f = new Fixture(); var n = f.Native;
            switch (mode)
            {
                case "canvas-null": n.CanvasSourceSet(n.GameplayInformation, "canvasControl", IntPtr.Zero); break;
                case "information": n.CanvasSourceSet(n.GeometryCanvasControl, "levelInformation", n.OtherObject); break;
                case "transform-null": n.CanvasSourceSet(n.GeometryCanvasControl, "canvasTransforms", IntPtr.Zero); break;
                case "moves-null": n.CanvasSourceSet(n.GeometryCanvasControl, "canvasMoveData", IntPtr.Zero); break;
                case "count-short": n.CanvasSourceSet(n.GeometryCanvasControl, "canvasMoveData", n.CanvasSourceResizeArray(n.CanvasSourceMovesArray, 2)); break;
                case "count-long": n.CanvasSourceSet(n.GeometryCanvasControl, "canvasMoveData", n.CanvasSourceResizeArray(n.CanvasSourceMovesArray, 4)); break;
                case "transform-slot-null": n.CanvasSourceSlot(n.GeometryCanvasArray, 2, IntPtr.Zero); break;
                case "move-slot-null": n.CanvasSourceSlot(n.CanvasSourceMovesArray, 2, IntPtr.Zero); break;
                case "foreign-transform": n.CanvasSourceSlot(n.GeometryCanvasArray, 2, n.CanvasSourceMoves[0]); break;
                default: n.CanvasSourceSlot(n.CanvasSourceMovesArray, 2, n.GameplayInformation); break;
            }
            Reject(() => f.Capture()); Check(n.CanvasSourceRangeAttempts == 0, "Incomplete or foreign actual vectors reached a range callback: " + mode);
        }
    }
    private static void InvalidLists()
    {
        foreach (var speed in new[] { true, false })
        foreach (var mode in new[] { "null", "backing-null", "negative-size", "empty", "over-budget", "size-over-capacity", "count-mismatch", "item-mismatch", "item-null", "point-null", "foreign-point", "backing-type" })
        {
            using var f = new Fixture(); var n = f.Native; var list = n.CanvasSourceList(0, speed);
            switch (mode)
            {
                case "null": n.CanvasSourceSet(n.CanvasSourceMoves[0], speed ? "speedKeyPoints" : "xPositionKeyPoints", IntPtr.Zero); break;
                case "backing-null": n.CanvasSourceSet(list, "_items", IntPtr.Zero); break;
                case "negative-size": n.CanvasSourceSet(list, "_size", -1); break;
                case "empty": n.CanvasSourceSet(list, "_size", 0); break;
                case "over-budget": n.CanvasSourceSet(list, "_size", 65537); break;
                case "size-over-capacity": n.CanvasSourceSet(list, "_size", 4); break;
                case "count-mismatch": n.CanvasSourceCountBias = 1; break;
                case "item-mismatch": n.CanvasSourceOverrideItem(list, 1, n.CanvasSourcePoint(0, speed, 0)); break;
                case "item-null": n.CanvasSourceOverrideItem(list, 1, IntPtr.Zero); break;
                case "point-null": n.CanvasSourceSlot(n.CanvasSourceBacking(list), 1, IntPtr.Zero); break;
                case "foreign-point": n.CanvasSourceSlot(n.CanvasSourceBacking(list), 1, n.GameplayInformation); break;
                default: n.CanvasSourceSet(list, "_items", n.CanvasSourceMovesArray); break;
            }
            var roots = n.ActiveRoots; Reject(() => f.Capture());
            Check(n.ActiveRoots == roots && n.CanvasSourceRangeAttempts == 0, "Malformed actual KeyPoint list leaked roots or was truncated into a plan: " + mode);
        }
        foreach (var name in new[] { "get_Count", "get_Item" })
        foreach (var mode in new[] { "static", "owner", "return", "parameters", "byref-return" })
        {
            using var f = new Fixture(); var n = f.Native; var roots = n.ActiveRoots;
            n.CanvasSourceListMethodMetadata(name, mode); Reject(() => f.Capture());
            Check(n.ActiveRoots == roots && n.CanvasSourceRangeAttempts == 0 && n.CanvasSourceItemReads.Count == 0
                && (name == "get_Item" || n.CanvasSourceCountReads.Count == 0),
                "Malformed actual KeyPoint Count/Item metadata entered slot dispatch or projection: " + name + "/" + mode);
        }
    }
    private static void InvalidMetadata()
    {
        foreach (var field in new[] { "canvasControl", "levelInformation", "canvasTransforms", "canvasMoveData", "screenWidth", "index",
            "speedKeyPoints", "xPositionKeyPoints", "time", "value", "easeType", "floorPosition", "_items", "_size", "_version",
            "EaseInfos", "code", "progress", "downVector", "rightVector" })
        foreach (var mode in new[] { "offset", "static", "literal", "type", "byref", "parent" })
        foreach (var after in new[] { false, true })
        {
            using var f = new Fixture(); var n = f.Native; NativeSeekCanvasTargetCapture? p = after ? f.Capture() : null;
            var owner = field switch
            {
                "downVector" or "rightVector" or "EaseInfos" => IntPtr.Zero,
                "canvasControl" => n.GameplayInformation,
                "levelInformation" or "canvasTransforms" or "canvasMoveData" or "screenWidth" => n.GeometryCanvasControl,
                "index" or "speedKeyPoints" or "xPositionKeyPoints" => n.CanvasSourceMoves[0],
                "_items" or "_size" or "_version" => n.CanvasSourceList(0, true),
                "code" or "progress" => n.CanvasSourceSlots(n.CanvasSourceEaseArray)[1],
                _ => n.CanvasSourcePoint(0, true, 2)
            };
            var roots = n.ActiveRoots; var converters = n.CanvasSourceRangeAttempts;
            n.CanvasSourceMetadata(owner, field, mode);
            if (after) Reject(p!.VerifySource); else Reject(() => f.Capture());
            Check(n.ActiveRoots == roots && n.CanvasSourceRangeAttempts == converters,
                "Nonexact field metadata entered projection or lost cleanup: " + field + "/" + mode);
        }
        foreach (var field in new[] { "value", "downVector", "rightVector" })
        {
            using var f = new Fixture(); var p = f.Capture(); var n = f.Native;
            n.CanvasSourceMetadata(field == "value" ? n.CanvasSourcePoint(0, true, 0) : IntPtr.Zero, field, "width");
            Reject(p.VerifySource);
        }
        using (var f = new Fixture())
        { var p = f.Capture(); f.Native.CanvasSourceMetadata(IntPtr.Zero, "rightVector", "flags"); Reject(p.VerifySource); }
        foreach (var field in new[] { "downVector", "rightVector" })
        foreach (var after in new[] { false, true })
        {
            using var f = new Fixture(); var p = after ? f.Capture() : null;
            f.Native.CanvasSourceMetadata(IntPtr.Zero, field, "readonly");
            if (after) Reject(p!.VerifySource); else Reject(() => f.Capture());
        }
        foreach (var member in new[] { "ChartClass", "ProgressClass", "InformationClass", "CanvasClass", "CanvasMoveClass", "KeyPointClass", "TransformClass", "VectorClass", "EaseClass", "EaseInfoClass" })
        {
            using var f = new Fixture(); f.Native.CanvasSourceProfile.WrongMember = member;
            Reject(() => f.Capture()); Check(f.Native.CanvasSourceRangeAttempts == 0, "Foreign profile class entered the official range method.");
        }
    }
    private static void InvalidRange()
    {
        foreach (var mode in new[] { "static", "owner", "return", "parameters", "arity", "name", "byref-parameter", "byref-return" })
        foreach (var after in new[] { false, true })
        {
            using var f = new Fixture(); var n = f.Native; var p = after ? f.Capture() : null;
            var entries = n.CanvasSourceRangeAttempts; n.CanvasSourceRangeMetadata(mode);
            if (after) Reject(p!.VerifySource); else Reject(() => f.Capture());
            Check(n.CanvasSourceRangeAttempts == entries, "Bad saved/current Double Double Single range metadata entered native invocation: " + mode);
        }
        foreach (var mode in new[] { "null", "replacement", "generic" })
        {
            using var f = new Fixture(); var n = f.Native; var p = f.Capture(); var entries = n.CanvasSourceRangeAttempts;
            if (mode == "null") n.CanvasSourceProfile.Range = IntPtr.Zero;
            else if (mode == "replacement") n.CanvasSourceProfile.Range = n.CanvasSourceCloneRange();
            else n.GenericMethod = true;
            Reject(p.VerifySource); Check(n.CanvasSourceRangeAttempts == entries, "Range MethodInfo identity/generic state was not retained exactly.");
        }
        foreach (var scalar in new[] { "System.Double", "System.Single", "System.Int32" })
        foreach (var after in new[] { false, true })
        {
            using var f = new Fixture(); var n = f.Native;
            n.CanvasSourceWarmMethodLayouts(); var p = after ? f.Capture() : null;
            var roots = n.ActiveRoots; var entries = n.CanvasSourceRangeAttempts;
            n.CanvasSourceScalarWidth(scalar, scalar == "System.Double" ? 4 : 8);
            Reject(n.CanvasSourceWarmMethodLayouts);
            if (after) Reject(p!.VerifySource); else Reject(() => f.Capture());
            Check(n.ActiveRoots == roots && n.CanvasSourceRangeAttempts == entries,
                "Cached Canvas scalar metadata concealed current canonical class value-size drift before capture/verification: " + scalar);
        }
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            using var f = new Fixture(); f.Native.CanvasSourceRangeResult = bad; var roots = f.Native.ActiveRoots;
            Reject(() => f.Capture()); Check(f.Native.CanvasSourceRangeAttempts == 1 && f.Native.ActiveRoots == roots,
                "Nonfinite actual native Single interval was substituted, propagated or lost source cleanup.");
        }
    }
    private static void InvalidSamples()
    {
        foreach (var speed in new[] { true, false })
        foreach (var slot in new[] { 0, 2 })
        foreach (var name in new[] { "time", "value", "floorPosition" })
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            using var f = new Fixture(); var n = f.Native;
            n.CanvasSourceSet(n.CanvasSourcePoint(0, speed, slot), name, bad);
            Reject(() => f.Capture()); Check(n.CanvasSourceRangeAttempts == 0,
                "A nonfinite selected/unselected actual KeyPoint escaped complete copying: " + name);
        }
        foreach (var bad in new[] { -1, 2, 18, 19, int.MaxValue })
        {
            using var f = new Fixture(); f.Native.CanvasSourceSet(f.Native.CanvasSourcePoint(0, false, 2), "easeType", bad);
            Reject(() => f.Capture()); Check(f.Native.CanvasSourceRangeAttempts == 0,
                "Unknown or uninitialized complete X KeyPoint ease source reached projection.");
        }
        foreach (var mode in new[] { "missing", "null-row", "foreign-row", "missing-progress", "foreign-progress", "zero", "hundred", "hundred-two", "negative-code", "code-mismatch", "duplicate-code" })
        {
            using var f = new Fixture(); var n = f.Native; var info = n.CanvasSourceSlots(n.CanvasSourceEaseArray)[1];
            switch (mode)
            {
                case "missing": n.CanvasSourceSetEase(IntPtr.Zero); break;
                case "null-row": n.CanvasSourceSlot(n.CanvasSourceEaseArray, 1, IntPtr.Zero); break;
                case "foreign-row": n.CanvasSourceSlot(n.CanvasSourceEaseArray, 1, n.GameplayInformation); break;
                case "missing-progress": n.CanvasSourceSet(info, "progress", IntPtr.Zero); break;
                case "foreign-progress": n.CanvasSourceSet(info, "progress", n.CanvasSourceMovesArray); break;
                case "zero": n.CanvasSourceSet(info, "progress", n.CanvasSourceSampleArray(Array.Empty<float>())); break;
                case "hundred": n.CanvasSourceSet(info, "progress", n.CanvasSourceSampleArray(Enumerable.Repeat(.5f, 100).ToArray())); break;
                case "hundred-two": n.CanvasSourceSet(info, "progress", n.CanvasSourceSampleArray(Enumerable.Repeat(.5f, 102).ToArray())); break;
                case "negative-code": n.CanvasSourceSet(info, "code", -1); break;
                case "code-mismatch": n.CanvasSourceSet(info, "code", 18); break;
                default: n.CanvasSourceSet(info, "code", 0); break;
            }
            Reject(() => f.Capture()); Check(n.CanvasSourceRangeAttempts == 0, "Invalid initialized EaseInfos was replaced by fallback/initialization: " + mode);
        }
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        foreach (var slot in new[] { 0, 51, 100 })
        {
            using var f = new Fixture(); f.Native.GeometrySingleSlot(f.Native.CanvasSourceSamples[1], slot, bad); f.Native.CanvasSourceRemember();
            Reject(() => f.Capture()); Check(f.Native.CanvasSourceRangeAttempts == 0, "Incomplete finite checking ignored an unselected Ease sample.");
        }
        foreach (var name in new[] { "screenWidth", "downVector", "rightVector" })
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            using var f = new Fixture(); var n = f.Native;
            if (name == "screenWidth") n.CanvasSourceSet(n.GeometryCanvasControl, name, bad);
            else n.CanvasSourceSetVector(name, new(1, bad, 1));
            Reject(() => f.Capture()); Check(n.CanvasSourceRangeAttempts == 0, "Nonfinite actual width/vector entered numerical producers.");
        }
    }
    private static readonly string[] SourceMutations =
    {
        "canvas", "association", "transforms-array", "moves-array", "transform-slot", "move-slot", "index",
        "speed-list", "x-list", "speed-backing", "x-backing", "size", "version", "point-reference",
        "time-bits", "value", "ease-type", "floor", "width", "down", "right", "ease-array", "ease-row", "samples-array", "sample-bits", "note-source"
    };
    private static void MutateSource(FakeNative n, string mode)
    {
        var speed = n.CanvasSourceList(0, true); var x = n.CanvasSourceList(0, false);
        var point = n.CanvasSourcePoint(0, true, 2); var info = n.CanvasSourceSlots(n.CanvasSourceEaseArray)[1];
        switch (mode)
        {
            case "canvas": n.CanvasSourceSet(n.GameplayInformation, "canvasControl", n.CanvasSourceCloneObject(n.GeometryCanvasControl)); break;
            case "association": n.CanvasSourceSet(n.GeometryCanvasControl, "levelInformation", n.CanvasSourceCloneObject(n.GameplayInformation)); break;
            case "transforms-array": n.CanvasSourceSet(n.GeometryCanvasControl, "canvasTransforms", n.CanvasSourceCloneArray(n.GeometryCanvasArray)); break;
            case "moves-array": n.CanvasSourceSet(n.GeometryCanvasControl, "canvasMoveData", n.CanvasSourceCloneArray(n.CanvasSourceMovesArray)); break;
            case "transform-slot": n.CanvasSourceSlot(n.GeometryCanvasArray, 2, n.CanvasSourceSlots(n.GeometryCanvasArray)[0]); break;
            case "move-slot": n.CanvasSourceSlot(n.CanvasSourceMovesArray, 2, n.CanvasSourceCloneObject(n.CanvasSourceMoves[0])); break;
            case "index": n.CanvasSourceSet(n.CanvasSourceMoves[0], "index", 1000); break;
            case "speed-list": n.CanvasSourceReplaceList(0, true); break;
            case "x-list": n.CanvasSourceReplaceList(0, false); break;
            case "speed-backing": n.CanvasSourceSet(speed, "_items", n.CanvasSourceCloneArray(n.CanvasSourceBacking(speed))); break;
            case "x-backing": n.CanvasSourceSet(x, "_items", n.CanvasSourceCloneArray(n.CanvasSourceBacking(x))); break;
            case "size": n.CanvasSourceSet(speed, "_size", 2); break;
            case "version": n.CanvasSourceSet(speed, "_version", -16); break;
            case "point-reference": n.CanvasSourceSlot(n.CanvasSourceBacking(speed), 2, n.CanvasSourceCloneObject(point)); break;
            case "time-bits": n.CanvasSourceSet(n.CanvasSourcePoint(0, true, 0), "time", BitConverter.Int32BitsToSingle(unchecked((int)0x80000000))); break;
            case "value": n.CanvasSourceSet(point, "value", 7f); break;
            case "ease-type": n.CanvasSourceSet(n.CanvasSourcePoint(0, false, 2), "easeType", 1); break;
            case "floor": n.CanvasSourceSet(point, "floorPosition", 37f); break;
            case "width": n.CanvasSourceSet(n.GeometryCanvasControl, "screenWidth", 5f); break;
            case "down": n.CanvasSourceSetVector("downVector", new(-2, .50000006f, 4)); break;
            case "right": n.CanvasSourceSetVector("rightVector", new(.25f, -3, -.50000006f)); break;
            case "ease-array": n.CanvasSourceSetEase(n.CanvasSourceCloneArray(n.CanvasSourceEaseArray)); break;
            case "ease-row": n.CanvasSourceSlot(n.CanvasSourceEaseArray, 1, n.CanvasSourceCloneObject(info)); break;
            case "samples-array": n.CanvasSourceSet(info, "progress", n.CanvasSourceCloneArray(n.CanvasSourceSamples[1])); break;
            case "sample-bits": n.GeometrySingleSlot(n.CanvasSourceSamples[1], 100, BitConverter.Int32BitsToSingle(Bits(.75f) + 1)); n.CanvasSourceRemember(); break;
            case "note-source": n.CanvasSourceSet(n.TargetData(0), "time", 2.125f); break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
    private static void SourceChanges()
    {
        foreach (var mode in SourceMutations)
        {
            using var f = new Fixture(); var p = f.Capture(); var plan = p.Plan; var rows = plan.Rows.ToArray();
            var entries = f.Native.CanvasSourceRangeAttempts; MutateSource(f.Native, mode); Reject(p.VerifySource);
            Check(ReferenceEquals(plan, p.Plan) && p.Plan.Rows.SequenceEqual(rows) && f.Native.CanvasSourceRangeAttempts == entries,
                "Changed retained source retargeted the immutable plan or reinvoked a numerical producer: " + mode);
        }
        foreach (var vector in new[] { "downVector", "rightVector" })
        {
            using var f = new Fixture(); f.Native.CanvasSourceSetVector(vector, new(1, 0f, 1)); var p = f.Capture();
            f.Native.CanvasSourceSetVector(vector, new(1, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), 1));
            Reject(p.VerifySource); Check(Bits(vector == "downVector" ? p.Plan.Request.Down.Y : p.Plan.Request.Right.Y) == 0,
                "Signed-zero source verification compared Vector3 values numerically instead of their complete captured bits.");
        }
    }
    private static void DoublePass()
    {
        foreach (var mode in SourceMutations)
        {
            using var f = new Fixture(); f.Context = "DoublePass/range/" + mode; var fired = false; var roots = f.Native.ActiveRoots;
            f.Native.AfterCanvasSourceRange = () => { if (fired) return; fired = true; MutateSource(f.Native, mode); };
            Reject(() => f.Capture(), f.Context); f.Clear();
            Check(fired && f.Native.CanvasSourceRangeAttempts == 1 && f.Native.ActiveRoots == roots,
                "Official interval callback mutation escaped the next complete source guard or leaked roots: " + mode);
        }
        foreach (var pair in new[] { ("index", "index"), ("_version", "version"), ("time", "time-bits"),
            ("floorPosition", "floor"), ("downVector", "down"), ("rightVector", "right"), ("EaseInfos", "ease-array"), ("progress", "samples-array") })
        {
            using var f = new Fixture(); f.Context = "DoublePass/field/" + pair.Item1 + "/" + pair.Item2;
            var fired = false; var n = f.Native;
            // Change precisely the field whose old value has just been returned.
            // Use the first speed point's time, or the final point's FP.
            var selectedOwner = pair.Item1 switch
            {
                "index" => n.CanvasSourceMoves[0], "_version" => n.CanvasSourceList(0, true),
                "time" => n.CanvasSourcePoint(0, true, 0), "floorPosition" => n.CanvasSourcePoint(0, true, 2),
                "progress" => n.CanvasSourceSlots(n.CanvasSourceEaseArray)[1], _ => IntPtr.Zero
            };
            n.AfterCanvasSourceRead = (name, owner) =>
            {
                if (fired || name != pair.Item1 || selectedOwner != IntPtr.Zero && owner != selectedOwner) return;
                fired = true; MutateSource(n, pair.Item2);
            };
            Reject(() => f.Capture(), f.Context + "; owner=0x" + selectedOwner.ToInt64().ToString("x"));
            f.Clear(); Check(fired, "Actual field double-observation callback was not exercised: " + pair.Item1);
        }
        using var samples = new Fixture(); samples.Context = "DoublePass/samples/second-size"; var visits = 0; var changed = false;
        samples.Native.AfterGeometryArrayExport = (name, array) =>
        {
            if (name != "size" || array != samples.Native.CanvasSourceSamples[1] || ++visits != 2) return;
            changed = true; samples.Native.GeometrySingleSlot(array, 100, .125f); samples.Native.CanvasSourceRemember();
        };
        Reject(() => samples.Capture(), samples.Context); samples.Clear();
        Check(changed && samples.Native.CanvasSourceRangeAttempts == 0, "Pinned actual sample changed between complete bitwise passes without rejection.");
    }
}
