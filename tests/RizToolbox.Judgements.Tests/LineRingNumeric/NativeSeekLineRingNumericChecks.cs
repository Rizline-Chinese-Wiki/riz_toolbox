using System.Collections;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.LineRingNumeric;

internal static class NativeSeekLineRingNumericChecks
{
    internal static readonly (string Name, Action Run)[] Cases = new (string Name, Action Run)[]
    {
        ("Line ring numeric strict distance thresholds and all five buffer counts", Thresholds),
        ("Line ring numeric manual overshoot and relative X linear Y golden values", GoldenBuffer),
        ("Line ring numeric strict world windows and no buffer operation for culls", WorldWindows),
        ("Line ring numeric per endpoint floor boundaries and ordered culls", FloorWindows),
        ("Line ring numeric integer ease clamps index and preserves raw values", IntegerEase),
        ("Line ring numeric finite positive support domain and overflow refusals", UnsupportedInputs),
        ("Line ring numeric forward selection strict equality order and explicit seed", ForwardSelection),
        ("Line ring numeric complete enumeration refuses missing extra and changing count", CompleteInputs),
        ("Line ring numeric caller withdrawal stops sampling and disposes enumerator once", Withdrawal),
        ("Line ring numeric cross operation synchronous reentry refuses before nested guard", Reentry),
        ("Line ring numeric owned samples immutable results and historical success", ImmutableHistory),
        ("Line ring numeric Single add zero and output Z preserve positive zero", SignedZero),
        ("Line ring numeric exact rational Single intermediate rounding and ordered scale goldens", SingleRoundingGoldens),
    };

    private static float[] Samples() => Enumerable.Range(0, 101).Select(j => j / 100f).ToArray();
    private static SeekLineRingDynamicNumericInput Input(float distance = 1f) => new(0, 0, 0, 0, 2,
        new(0, 0, 0), new(distance, 0, 0), 1, 1);
    private static SeekLineRingDynamicNumericResult Project(SeekLineRingDynamicNumericInput input,
        IReadOnlyList<float>? samples = null, Action? guard = null)
        => NativeSeekLineRingNumericMath.ProjectDynamic(input, samples ?? Samples(), guard ?? (() => { }));
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    private static Exception Reject(Action action)
    { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected refusal."); }
    private static void Bits(float actual, int expected, string why)
        => Check(BitConverter.SingleToInt32Bits(actual) == expected, why + ": " + BitConverter.SingleToInt32Bits(actual).ToString("x8"));
    private static void Thresholds()
    {
        foreach (var (distance, step, count) in new[] {
            (0f, 20, 6), (MathF.BitDecrement(.5f), 20, 6), (.5f, 10, 11), (MathF.BitIncrement(.5f), 10, 11),
            (MathF.BitDecrement(1.5f), 10, 11), (1.5f, 5, 21), (MathF.BitIncrement(1.5f), 5, 21),
            (MathF.BitDecrement(2.5f), 5, 21), (2.5f, 2, 51), (MathF.BitIncrement(2.5f), 2, 51),
            (MathF.BitDecrement(3.5f), 2, 51), (3.5f, 1, 101), (MathF.BitIncrement(3.5f), 1, 101), (5f, 1, 101) })
        {
            var result = Project(Input(distance));
            Check(result.Kind == SeekLineRingDynamicProjectionKind.Buffer && result.Step == step
                && result.PositionCount == count && result.Points.Count == count, "Wrong threshold equality/count: " + distance);
            Bits(result.Points[^1].X, BitConverter.SingleToInt32Bits(distance), "Endpoint index100 missing");
        }
        Check(Project(Input() with { EndWorld = new(0, 0, 1.5f) }).Step == 5,
            "Distance omitted Z or used X only.");
    }
    private static void GoldenBuffer()
    {
        var samples = Samples(); samples[50] = 1.25f;
        var result = Project(Input() with { StartWorld = new(2, 2, 0), EndWorld = new(4, 4, 0), CameraScale = 2, SceneScale = 4 }, samples);
        Check(result.Step == 2 && result.PositionCount == 51, "sqrt(2²+2²) should select step2.");
        // j=50: X=(1.25*2)/2/4=.3125, Y=(2*.5)/2/4=.125.
        // These exact fractions detect value clamping, adding back world origin,
        // using ease for Y, and the wrong sample slot/count independently.
        Bits(result.Points[25].X, 0x3ea00000, "Unclamped relative X");
        Bits(result.Points[25].Y, 0x3e000000, "Linear Y");
        Bits(result.Points[25].Z, 0, "Z is positive zero");
        Bits(result.Points[0].X, 0, "Start X must not add world origin");
        Bits(result.Points[^1].X, 0x3e800000, "Final relative X");
        Bits(result.Points[^1].Y, 0x3e800000, "Final relative Y");
    }
    private static void WorldWindows()
    {
        foreach (var (y, kind) in new[] { (MathF.BitIncrement(5), SeekLineRingDynamicProjectionKind.AboveWorldWindow),
            (MathF.BitDecrement(-5), SeekLineRingDynamicProjectionKind.BelowWorldWindow) })
        {
            var result = NativeSeekLineRingNumericMath.ProjectDynamic(Input() with {
                StartWorld = new(0, y, 0), EndWorld = new(1, y, 0), CameraScale = 0, SceneScale = 0 }, null!, () => { });
            Check(result.Kind == kind && result.PositionCount is null && result.Step is null && result.Points.Count == 0,
                "World cull fabricated count0 or read unused ease/scale inputs.");
        }
        foreach (var y in new[] { -5f, 5f })
            Check(Project(Input() with { StartWorld = new(0, y, 0), EndWorld = new(1, y, 0) }).PositionCount is > 0,
                "An exact world boundary was culled.");
        Check(Project(Input() with { StartWorld = new(0, 5, 0), EndWorld = new(1, 6, 0) }).Kind == SeekLineRingDynamicProjectionKind.Buffer,
            "A straddling segment was culled.");
    }
    private static void FloorWindows()
    {
        Check(Project(Input() with { StartFloor = 4, EndFloor = 4 }).Kind == SeekLineRingDynamicProjectionKind.Buffer, "Future equality was culled.");
        Check(Project(Input() with { StartFloor = -1.5f, EndFloor = -1.5f }).Kind == SeekLineRingDynamicProjectionKind.Buffer, "Past equality was culled.");
        foreach (var (floor, kind) in new[] { (MathF.BitIncrement(4), SeekLineRingDynamicProjectionKind.OutsideFutureFloor),
            (MathF.BitDecrement(-1.5f), SeekLineRingDynamicProjectionKind.OutsidePastFloor) })
        {
            var result = NativeSeekLineRingNumericMath.ProjectDynamic(Input() with { StartFloor = floor, EndFloor = floor,
                StartWorld = new(float.NaN, float.NaN, float.NaN), CameraScale = 0 }, null!, () => { });
            Check(result.Kind == kind && result.PositionCount is null, "Floor cull read unsupported unused world/ease inputs.");
        }
        Check(Project(Input() with { StartCanvasFloor = 100, EndCanvasFloor = -20, StartFloor = 104, EndFloor = -16 }).Kind
            == SeekLineRingDynamicProjectionKind.Buffer, "Cross-Canvas equality was culled.");
        foreach (var reverse in new[] { false, true })
        {
            var startCanvas = reverse ? -20f : 100f; var endCanvas = reverse ? 100f : -20f;
            Check(Project(Input() with { StartCanvasFloor = startCanvas, EndCanvasFloor = endCanvas,
                StartFloor = startCanvas + 5, EndFloor = endCanvas + 5 }).Kind == SeekLineRingDynamicProjectionKind.OutsideFutureFloor,
                "An asymmetric future cull reused one endpoint's Canvas floor for both.");
            Check(Project(Input() with { StartCanvasFloor = startCanvas, EndCanvasFloor = endCanvas,
                StartFloor = startCanvas - 2, EndFloor = endCanvas - 2 }).Kind == SeekLineRingDynamicProjectionKind.OutsidePastFloor,
                "An asymmetric past cull reused one endpoint's Canvas floor for both.");
        }
        Check(Project(Input() with { StartFloor = 100, EndFloor = 4 }).Kind == SeekLineRingDynamicProjectionKind.Buffer,
            "Floor culling must require both endpoints.");
    }
    private static void IntegerEase()
    {
        var samples = Samples(); samples[0] = -.25f; samples[50] = 1.25f; samples[100] = 1.5f;
        foreach (var index in new[] { int.MinValue, -1, 0 })
            Bits(NativeSeekLineRingNumericMath.ReadInitializedIndex(samples, index, () => { }), unchecked((int)0xbe800000), "Index lower endpoint");
        Bits(NativeSeekLineRingNumericMath.ReadInitializedIndex(samples, 50, () => { }), 0x3fa00000, "Index value overshoot");
        foreach (var index in new[] { 100, 101, int.MaxValue })
            Bits(NativeSeekLineRingNumericMath.ReadInitializedIndex(samples, index, () => { }), 0x3fc00000, "Index upper endpoint");
    }
    private static void UnsupportedInputs()
    {
        foreach (var speed in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.Epsilon }) Reject(() => Project(Input() with { Speed = speed }));
        foreach (var scale in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        { Reject(() => Project(Input() with { CameraScale = scale })); Reject(() => Project(Input() with { SceneScale = scale })); }
        Reject(() => Project(Input() with { EndWorld = new(1e20f, 0, 0) }));
        Reject(() => Project(Input() with { StartWorld = new(float.MaxValue, 0, 0), EndWorld = new(-float.MaxValue, 0, 0) }));
        foreach (var count in new[] { 0, 100, 102 }) Reject(() => Project(Input(), new float[count]));
        var samples = Samples(); samples[99] = float.NaN; Reject(() => Project(Input(), samples));
    }
    private static void ForwardSelection()
    {
        int Select(float[] rows, int seed, float time) => NativeSeekLineRingNumericMath.SelectForwardFromSeed(rows, seed, time, () => { });
        Check(Select(new[] { 0f, 2f, 4f }, 0, 2) == 0 && Select(new[] { 0f, 2f, 4f }, 0, MathF.BitIncrement(2)) == 1,
            "Selection lost strict equality.");
        Check(Select(new[] { 0f, 2f, 4f }, 0, 4) == 1 && Select(new[] { 0f, 2f, 4f }, 2, -100) == 2,
            "Selection reset or rewound the explicit seed.");
        Check(Select(new[] { 0f, 5f, 3f }, 0, 4) == 0, "Selection sorted or globally searched the source.");
        Check(Select(new[] { 0f, 2f, 2f }, 0, MathF.BitIncrement(2)) == 2, "Duplicate time traversal omitted a segment.");
        Reject(() => Select(Array.Empty<float>(), 0, 0)); Reject(() => Select(new[] { 0f }, -1, 0));
        Reject(() => Select(new[] { 0f }, 1, 0)); Reject(() => Select(new float[65537], 0, 0));
    }
    private static void CompleteInputs()
    {
        foreach (var actualCount in new[] { 100, 102 })
            Reject(() => Project(Input(), new ProbeList(new float[actualCount], 101)));
        var changing = new ProbeList(Samples(), 101); changing.AfterYield = index => { if (index == 5) changing.AdvertisedCount = 102; };
        Reject(() => Project(Input(), changing)); Check(changing.DisposeCount == 1, "Changing enumeration cleanup lost.");
    }
    private static void Withdrawal()
    {
        var samples = new ProbeList(Samples(), 101); var withdrawn = false; var injected = new IOException("withdraw numeric preview");
        samples.AfterYield = index => { if (index == 10) withdrawn = true; };
        var error = Reject(() => Project(Input(), samples, () => { if (withdrawn) throw injected; }));
        Check(ReferenceEquals(error, injected) && samples.YieldCount == 11 && samples.DisposeCount == 1,
            "Withdrawal consumed a later sample or lost the original failure.");
        Check(Project(Input()).PositionCount == 11, "Failed operation did not release managed reentry gate.");
    }
    private static void Reentry()
    {
        var nestedGuards = 0; var fired = false;
        var result = Project(Input(), guard: () =>
        {
            if (fired) return; fired = true;
            Check(Reject(() => NativeSeekLineRingNumericMath.ReadInitializedIndex(Samples(), 0, () => ++nestedGuards)) is InvalidOperationException,
                "Nested math operation was accepted.");
        });
        Check(result.PositionCount == 11 && nestedGuards == 0, "Nested guard entered before reentry refusal.");
    }
    private static void ImmutableHistory()
    {
        var raw = Samples(); var samples = new ProbeList(raw, 101); samples.OnDispose = () => Array.Fill(raw, 9f);
        var original = Input(); var result = Project(original, samples);
        Check(!ReferenceEquals(original, result.Input) && samples.DisposeCount == 1 && raw[100] == 9,
            "The mutation fixture or owned request was not established.");
        Bits(result.Points[^1].X, 0x3f800000, "Original initialized sample copy changed after enumeration disposal");
        Check(Reject(() => ((IList<NativeVector3>)result.Points).Clear()) is NotSupportedException && result.Points.Count == 11,
            "Published points were mutable.");
        Reject(() => Project(Input() with { Speed = 0 })); Bits(result.Points[^1].X, 0x3f800000, "A later failure changed a historical result");
        Check(!result.CompleteSeekPermitted, "Numeric success granted complete Seek.");
    }
    private static void SignedZero()
    {
        var negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        var input = Input() with { EndWorld = new(negativeZero, 0, 0) };
        Bits(input.EndWorld.X, unchecked((int)0x80000000), "The signed-zero input fixture must be negative zero");
        var result = Project(input, Enumerable.Repeat(1f, 101).ToArray());
        foreach (var point in result.Points) { Bits(point.X, 0, "Native add-positive-zero X"); Bits(point.Y, 0, "Native add-positive-zero Y"); Bits(point.Z, 0, "Native positive-zero Z"); }
    }

    private static void SingleRoundingGoldens()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "line-ring-numeric-goldens.json")));
        var root = document.RootElement;
        int Raw(JsonElement element) => unchecked((int)Convert.ToUInt32(element.GetString()![2..], 16));
        float Number(JsonElement element) => BitConverter.Int32BitsToSingle(Raw(element));
        NativeVector3 Vector(JsonElement element) => new(Number(element[0]), Number(element[1]), Number(element[2]));
        foreach (var row in root.GetProperty("distanceCases").EnumerateArray())
        {
            var expected = row.GetProperty("expectedStep").GetInt32();
            Check(expected != row.GetProperty("alternateStep").GetInt32(), "The rounding fixture does not discriminate its named wrong algorithm.");
            var result = Project(Input() with { EndWorld = Vector(row.GetProperty("endWorld")) });
            Check(result.Step == expected && result.PositionCount == 100 / expected + 1,
                "Near-threshold native Single rounding differs: " + row.GetProperty("name").GetString());
        }
        var buffer = root.GetProperty("bufferCase"); var index = buffer.GetProperty("sampleIndex").GetInt32();
        var samples = Samples(); samples[index] = Number(buffer.GetProperty("sample"));
        var projected = Project(Input() with { StartWorld = Vector(buffer.GetProperty("startWorld")),
            EndWorld = Vector(buffer.GetProperty("endWorld")), CameraScale = Number(buffer.GetProperty("cameraScale")),
            SceneScale = Number(buffer.GetProperty("sceneScale")) }, samples);
        Check(projected.Step == 1, "The non-exact division golden did not expose its exact sample slot.");
        foreach (var alternate in buffer.GetProperty("alternativeX").EnumerateObject())
            Check(Raw(alternate.Value) != Raw(buffer.GetProperty("expectedX")), "A scale-order golden does not discriminate " + alternate.Name);
        Bits(projected.Points[index].X, Raw(buffer.GetProperty("expectedX")), "Non-exact X Single/division order");
        Bits(projected.Points[index].Y, Raw(buffer.GetProperty("expectedY")), "Non-exact Y Single/division order");
    }

    private sealed class ProbeList : IReadOnlyList<float>
    {
        private readonly float[] _rows;
        internal int AdvertisedCount, YieldCount, DisposeCount;
        internal Action<int>? AfterYield;
        internal Action? OnDispose;
        internal ProbeList(float[] rows, int advertisedCount) { _rows = rows; AdvertisedCount = advertisedCount; }
        public int Count => AdvertisedCount;
        public float this[int index] => _rows[index];
        public IEnumerator<float> GetEnumerator()
        {
            try { for (var i = 0; i < _rows.Length; ++i) { ++YieldCount; AfterYield?.Invoke(i); yield return _rows[i]; } }
            finally { ++DisposeCount; OnDispose?.Invoke(); }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
