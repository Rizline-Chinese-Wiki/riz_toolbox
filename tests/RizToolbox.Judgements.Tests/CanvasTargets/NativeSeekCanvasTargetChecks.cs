using System.Reflection;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.CanvasTargets;

// Pure managed numeric projection only. These explicit callback samples are
// test inputs, not a captured native converter/ease table, retained backend,
// Transform writer or evidence that the target environment has been rebuilt.
internal static class NativeSeekCanvasTargetChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Canvas projection uses strict reverse selection at equality and duplicate times", StrictEquality),
        ("Canvas projection selects actual unsorted list slots rather than the nearest chart time", UnsortedPoints),
        ("Canvas projection handles before-first equality at first last and final self slots", BoundarySlots),
        ("Canvas projection compares rounded Single span against evidenced Double epsilon", Epsilon),
        ("Canvas projection passes raw Single progress and selected ease type then clamps the sample", EaseAndClamp),
        ("Canvas projection passes Double range endpoints and rounds Single floor multiply before add", FloorBits),
        ("Canvas projection preserves per-component native vector and Single multiply order", VectorBits),
        ("Canvas projection preserves actual canvas order repeated moves and keypoint references", MultipleCanvases),
        ("Canvas projection owns complete readonly copies before controlled callbacks can mutate inputs", ImmutableCopies),
        ("Canvas projection brackets callbacks and source reads with withdrawal guards", GuardBrackets),
        ("Canvas projection rejects synchronous callback reentry and never substitutes failed samples", ReentryAndFailures),
        ("Canvas projection rejects every nonfinite request scalar and captured basis component", NonfiniteRequest),
        ("Canvas projection validates unselected keypoints and nonfinite controlled samples", NonfinitePointsAndSamples),
        ("Canvas projection rejects Single intermediate floor x vector and addition overflow", Overflow),
        ("Canvas projection enforces complete canvas per-list and total budgets without deduplication", Bounds),
        ("Canvas projection rejects null or empty per-move lists and grants no native ownership or Seek", NullEmptyAndScope)
    };
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    private static Exception Reject(Action action)
    { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected pure Canvas projection rejection."); }
    private static float F(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));
    private static uint Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));
    private static SeekCanvasTargetKeyPoint K(long reference, float time, float value, int ease = 0, float floor = 0)
        => new(reference, time, value, ease, floor);
    private static SeekCanvasTargetMove M(long reference, IReadOnlyList<SeekCanvasTargetKeyPoint> speed,
        IReadOnlyList<SeekCanvasTargetKeyPoint> x, int index = 0) => new(reference, index, speed, x);
    private static SeekCanvasTargetRequest R(float time = 1, float speed = 2, float width = 8,
        NativeVector3? down = null, NativeVector3? right = null)
        => new(time, speed, width, down ?? new NativeVector3(0, -1, 0), right ?? new NativeVector3(1, 0, 0));
    private static SeekCanvasTargetMove[] One()
        => new[] { M(10, new[] { K(11, 0, 2, floor: 3) }, new[] { K(12, 0, .25f) }) };
    private static NativeSeekCanvasTargetPlan Project(IReadOnlyList<SeekCanvasTargetMove> moves, SeekCanvasTargetRequest request,
        Func<double, double, float> interval, Func<float, int, float> ease, Action? guard = null)
    {
        var plan = NativeSeekCanvasTargetPlan.Capture(moves, request, interval, ease, guard ?? (() => { }));
        Check(!plan.CompleteSeekPermitted && plan.Request == request,
            "Pure copied Canvas projection granted complete Seek or changed the explicit captured request.");
        return plan;
    }
    private static void Row(NativeSeekCanvasTargetPlan plan, int ordinal, long moveReference, int speedSlot, int xSlot,
        float floor, float floorSpeed, float x, NativeVector3 local)
    {
        var actual = plan.Rows[ordinal];
        Check(actual.Ordinal == ordinal && actual.MoveReference == moveReference && actual.SpeedSlot == speedSlot && actual.XSlot == xSlot
            && Bits(actual.FloorPosition) == Bits(floor) && Bits(actual.FloorSpeed) == Bits(floorSpeed) && Bits(actual.XPosition) == Bits(x)
            && Bits(actual.LocalPosition.X) == Bits(local.X) && Bits(actual.LocalPosition.Y) == Bits(local.Y)
            && Bits(actual.LocalPosition.Z) == Bits(local.Z),
            "Canvas row differs from independent expected actual-slot indices and exact Single numeric/component bits.");
    }
    private static void StableControl()
    {
        var p = Project(One(), R(), (from, to) => { Check(from == 0d && to == 1d, "Control interval endpoints changed."); return 1; },
            (_, _) => throw new InvalidOperationException("Self X must not sample ease."));
        Check(p.Rows.Count == 1 && p.Moves.Count == 1, "Control projection lost a complete explicit canvas.");
        Row(p, 0, 10, 0, 0, 5, 2, 2, new(2, -10, 0));
    }
    private static void StrictEquality()
    {
        var moves = new[] { M(100, new[] { K(1, 0, 2, floor: 10), K(2, 5, 4, floor: 20), K(3, 5, 6, floor: 30) },
            new[] { K(4, 0, .25f, 3), K(5, 5, .5f, 4), K(6, 5, .75f, 5) }) };
        var intervalCalls = 0; var easeCalls = 0;
        var equal = Project(moves, R(5), (from, to) =>
        { ++intervalCalls; Check(from == 0d && to == 5d, "Equal-time speed keypoint was treated as strictly earlier."); return 2.5f; },
            (progress, type) => { ++easeCalls; Check(Bits(progress) == Bits(1) && type == 3, "Equality used a duplicate upper ease type or progress."); return 1; });
        Row(equal, 0, 100, 0, 0, 15, 2, 4, new(4, -30, 0));
        Check(intervalCalls == 1 && easeCalls == 1, "Strict equality skipped a required range/ease callback.");
        var justAfter = F(0x40a00001); easeCalls = 0;
        var later = Project(moves, R(justAfter), (from, to) =>
        { Check(from == 5d && to == (double)justAfter, "Duplicate-time reverse scan did not select the final actual duplicate."); return 1; },
            (_, _) => { ++easeCalls; throw new InvalidOperationException("Last self keypoint must bypass ease."); });
        Row(later, 0, 100, 2, 2, 36, 6, 6, new(6, -72, 0));
        Check(easeCalls == 0 && later.Moves[0].SpeedKeyPoints.Select(x => x.Reference).SequenceEqual(new long[] { 1, 2, 3 }),
            "Duplicate-time source rows were merged or a self interval was eased.");
    }
    private static void UnsortedPoints()
    {
        var moves = new[] { M(200, new[] { K(1, 0, 1), K(2, 4, 2, floor: 10), K(3, 1, 3, floor: 20), K(4, 8, 4, floor: 30) },
            new[] { K(5, 0, 1), K(6, 4, 2), K(7, 1, 3, 7), K(8, 8, 10, 8) }, index: 99) };
        var p = Project(moves, R(5, width: 4), (from, to) =>
        { Check(from == 1d && to == 5d, "Speed scan sorted times or selected the numerically nearest earlier keypoint."); return 2; },
            (progress, type) => { Check(Bits(progress) == 0x3f124925 && type == 7, "Unsorted X slot progress or lower ease type changed."); return .25f; });
        Row(p, 0, 200, 2, 2, 26, 3, 19, new(19, -52, 0));
        Check(p.Moves[0].Index == 99 && p.Moves[0].XPositionKeyPoints.Select(x => x.Time).SequenceEqual(new[] { 0f, 4f, 1f, 8f }),
            "Captured CanvasMove.index or actual unsorted X list order was rewritten.");
    }
    private static void BoundarySlots()
    {
        var moves = new[] { M(300, new[] { K(1, 0, 2, floor: 3), K(2, 4, 3, floor: 10) }, new[] { K(3, 0, 2, 9), K(4, 4, 10, 10) }) };
        foreach (var v in new[]
        {
            (Time: -2f, From: 0d, Interval: -.75f, Slot: 0, Ease: true, Progress: -.5f, Sample: .25f, Floor: 1.5f, X: 8f),
            (Time: 0f, From: 0d, Interval: 0f, Slot: 0, Ease: true, Progress: 0f, Sample: 0f, Floor: 3f, X: 4f),
            (Time: 4f, From: 0d, Interval: 2f, Slot: 0, Ease: true, Progress: 1f, Sample: 1f, Floor: 7f, X: 20f),
            (Time: 5f, From: 4d, Interval: .5f, Slot: 1, Ease: false, Progress: 0f, Sample: 0f, Floor: 11.5f, X: 20f)
        })
        {
            var ranges = 0; var eases = 0;
            var p = Project(moves, R(v.Time, width: 2), (from, to) =>
            { ++ranges; Check(from == v.From && to == (double)v.Time, "Boundary interval endpoints were clamped or guessed."); return v.Interval; },
                (progress, type) => { ++eases; Check(v.Ease && Bits(progress) == Bits(v.Progress) && type == 9, "Boundary/self X took the wrong easing branch."); return v.Sample; });
            Row(p, 0, 300, v.Slot, v.Slot, v.Floor, v.Slot == 0 ? 2 : 3, v.X, new(v.X, -v.Floor * 2, 0));
            Check(ranges == 1 && eases == (v.Ease ? 1 : 0), "A speed self interval was skipped or final self X was interpolated.");
        }
    }
    private static void Epsilon()
    {
        // constants.log: 8dedb5a0f7c6b03e is Double 1e-6, not an inferred
        // Single threshold. Adjacent float spans straddle that exact Double.
        foreach (var spanBits in new uint[] { 0x358637bd, 0x358637be })
        {
            var span = F(spanBits); var target = span * .5f; var expectedEase = spanBits == 0x358637be; var calls = 0;
            var moves = new[] { M(400, new[] { K(1, 0, 0, floor: 3) }, new[] { K(2, 0, 2, 18), K(3, span, 6) }) };
            var p = Project(moves, R(target, width: 3), (_, _) => 1, (progress, type) =>
            { ++calls; Check(expectedEase && Bits(progress) == Bits(.5f) && type == 18, "Double epsilon branch used an incorrect rounded span."); return .75f; });
            var expectedX = expectedEase ? 15f : 6f;
            Row(p, 0, 400, 0, 0, 3, 0, expectedX, new(expectedX, -6, 0));
            Check(calls == (expectedEase ? 1 : 0), "Epsilon was guessed as zero or compared in the wrong scalar domain.");
        }
        var rounded = new[] { M(401, new[] { K(1, 0, 0, floor: 3) }, new[] { K(2, 1, 2), K(3, F(0x3f800008), 6) }) };
        var roundedPlan = Project(rounded, R(F(0x3f800004), width: 3), (_, _) => 0,
            (_, _) => throw new InvalidOperationException("Rounded eight-ULP Single span is below Double epsilon."));
        Row(roundedPlan, 0, 401, 0, 0, 3, 0, 6, new(6, -6, 0));
        // Double(high)-Double(low)=1.0000000010279564e-6 is ABOVE eps,
        // but the required Single subtraction rounds to 0x358637bd BELOW eps.
        var subtractFirst = new[] { M(403, new[] { K(1, 0, 0, floor: 3) },
            new[] { K(2, -F(0x27800000), 2), K(3, F(0x358637bd), 6) }) };
        var singleSpan = Project(subtractFirst, R(F(0x350637bd), width: 3), (_, _) => 0,
            (_, _) => throw new InvalidOperationException("X span must round as Single before Double epsilon comparison."));
        Row(singleSpan, 0, 403, 0, 0, 3, 0, 6, new(6, -6, 0));
        var negativeSpan = new[] { M(402, new[] { K(1, 0, 0, floor: 3) }, new[] { K(2, 1, 2, 17), K(3, 0, 6) }) };
        var negative = Project(negativeSpan, R(0, width: 3), (_, _) => 0,
            (progress, type) => { Check(Bits(progress) == Bits(1) && type == 17, "Negative Single span used a signed epsilon comparison or reordered points."); return .5f; });
        Row(negative, 0, 402, 0, 0, 3, 0, 12, new(12, -6, 0));
    }
    private static void EaseAndClamp()
    {
        var moves = new[] { M(500, new[] { K(1, 0, 0, floor: 3) }, new[] { K(2, 0, 2, 9), K(3, 2, 6, 4) }) };
        foreach (var v in new[] { (Sample: -2f, X: 6f), (Sample: 2f, X: 18f), (Sample: .25f, X: 9f) })
        {
            var p = Project(moves, R(1, width: 3), (_, _) => 0, (progress, type) =>
            { Check(Bits(progress) == Bits(.5f) && type == 9, "Easing did not consume the selected lower keypoint's Single progress/type."); return v.Sample; });
            Row(p, 0, 500, 0, 0, 3, 0, v.X, new(v.X, -6, 0));
        }
        var extrapolated = Project(moves, R(-2, width: 3), (_, _) => 0, (progress, type) =>
        { Check(Bits(progress) == Bits(-1) && type == 9, "Progress was clamped before the controlled ease callback."); return .25f; });
        Row(extrapolated, 0, 500, 0, 0, 3, 0, 9, new(9, -6, 0));
    }
    private static void FloorBits()
    {
        var onePlus = F(0x3f800001); var roundedProduct = F(0x3f800002); var calls = 0;
        var moves = new[] { M(600, new[] { K(1, 1.25f, onePlus, floor: -roundedProduct) }, new[] { K(2, 0, 0) }) };
        var p = Project(moves, R(2.5f, speed: 4, width: 1), (from, to) =>
        { ++calls; Check(from == 1.25d && to == 2.5d, "Range callback did not receive Double(fromTime,targetTime) in order."); return onePlus; },
            (_, _) => throw new InvalidOperationException("Single self X does not require ease."));
        // (1+2^-23)^2 rounds to 1+2^-22 BEFORE adding -(1+2^-22).
        // Double/FMA multiply-add would instead retain the nonzero 2^-46 term.
        Row(p, 0, 600, 0, 0, 0, onePlus, 0, new(0, 0, 0));
        Check(calls == 1 && Bits(p.Rows[0].FloorPosition) == 0, "Floor multiply/add was fused or promoted beyond native Single order.");
    }
    private static void VectorBits()
    {
        var moves = new[] { M(700, new[] { K(1, 0, 2, floor: 3) }, new[] { K(2, 0, 2, 5), K(3, 4, 10) }) };
        var p = Project(moves, R(2, speed: 3, width: 3, down: new(-2, .5f, 4), right: new(.25f, -3, -.5f)),
            (from, to) => { Check(from == 0d && to == 2d, "Nonidentity vector range source changed."); return 1.25f; },
            (progress, type) => { Check(Bits(progress) == Bits(.5f) && type == 5, "Nonidentity vector X sample changed."); return .25f; });
        Row(p, 0, 700, 0, 0, 5.5f, 2, 12, new(-30, -27.75f, 60));
        var onePlus = F(0x3f800001); var third = F(0x3eaaaaab);
        var ordered = new[] { M(701, new[] { K(1, 0, 9, floor: onePlus) }, new[] { K(2, 0, 1) }) };
        var bitPlan = Project(ordered, R(speed: onePlus, width: 1, down: new(third, -third, 2), right: new(0, 0, 1)), (_, _) => 0,
            (_, _) => throw new InvalidOperationException("Ordered vector self X must bypass ease."));
        // S(S(floor*speed)*down.X)=0x3eaaaaae; regrouping as
        // S(floor*S(speed*down.X)) incorrectly produces 0x3eaaaaad.
        Row(bitPlan, 0, 701, 0, 0, onePlus, 9, 1, new(F(0x3eaaaaae), F(0xbeaaaaae), F(0x40400002)));
    }
    private static void MultipleCanvases()
    {
        var duplicateSpeed = K(21, 0, 2, floor: 3); var duplicateX = K(22, 0, .25f);
        var a = M(200, new[] { duplicateSpeed, duplicateSpeed }, new[] { duplicateX, duplicateX }, index: 999);
        var b = M(100, new[] { K(23, 1, 3, floor: 4) }, new[] { K(24, 0, -.5f) }, index: -5);
        var calls = new List<(double From, double To)>();
        var p = Project(new[] { a, b, a }, R(3), (from, to) =>
        { calls.Add((from, to)); Check(to == 3d, "Multiple-canvas target time changed."); return from == 0d ? 1.5f : .75f; },
            (_, _) => throw new InvalidOperationException("Repeated/final self X must bypass ease."));
        Check(p.Rows.Count == 3 && p.Moves.Select(x => (x.Reference, x.Index)).SequenceEqual(new[] { (200L, 999), (100L, -5), (200L, 999) })
            && calls.SequenceEqual(new[] { (0d, 3d), (1d, 3d), (0d, 3d) })
            && p.Moves[0].SpeedKeyPoints.Select(x => x.Reference).SequenceEqual(new long[] { 21, 21 })
            && p.Moves[2].XPositionKeyPoints.Select(x => x.Reference).SequenceEqual(new long[] { 22, 22 }),
            "Actual canvas array order, repeated Move/keypoint references or range call order was sorted/deduplicated.");
        Row(p, 0, 200, 1, 1, 6, 2, 2, new(2, -12, 0));
        Row(p, 1, 100, 0, 0, 6.25f, 3, -4, new(-4, -12.5f, 0));
        Row(p, 2, 200, 1, 1, 6, 2, 2, new(2, -12, 0));
    }
    private static void Readonly<T>(IReadOnlyList<T> rows, T replacement)
    {
        if (rows is not IList<T> list) return; var failure = Reject(() => list[0] = replacement);
        Check(failure is NotSupportedException, "Copied projection collection exposed a mutable IList setter.");
    }
    private static void ImmutableCopies()
    {
        var speedA = new[] { K(1, 0, 2, floor: 3) }; var speedB = new[] { K(2, 0, 4, floor: 1) };
        var xA = new[] { K(3, 0, .25f) }; var xB = new[] { K(4, 0, .5f) };
        var moves = new[] { M(10, speedA, xA, 8), M(20, speedB, xB, -2) }; var request = R(); var originalRequest = request; var calls = 0;
        var p = Project(moves, request, (from, to) =>
        {
            Check(from == 0d && to == 1d, "Copied source calculation reread a changed request or keypoint.");
            if (++calls == 1)
            {
                speedA[0] = speedA[0] with { Value = float.NaN }; speedB[0] = speedB[0] with { FloorPosition = float.PositiveInfinity };
                xA[0] = xA[0] with { Value = 99 }; xB[0] = xB[0] with { Value = float.NaN };
                moves[1] = null!; request = request with { ChartTime = 99 };
            }
            return 1;
        }, (_, _) => throw new InvalidOperationException("Copied self X must bypass ease."));
        Check(calls == 2 && p.Request == originalRequest && p.Moves.Count == 2 && p.Moves[1].Reference == 20
            && p.Moves[0].SpeedKeyPoints[0].Value == 2 && p.Moves[1].SpeedKeyPoints[0].FloorPosition == 1
            && p.Moves[0].XPositionKeyPoints[0].Value == .25f && p.Moves[1].XPositionKeyPoints[0].Value == .5f,
            "Projection copied only the current canvas or retained mutable input-list aliases across callbacks.");
        Row(p, 0, 10, 0, 0, 5, 2, 2, new(2, -10, 0)); Row(p, 1, 20, 0, 0, 5, 4, 4, new(4, -10, 0));
        Readonly(p.Moves, p.Moves[0] with { Index = 123 }); Readonly(p.Rows, p.Rows[0] with { FloorPosition = 99 });
        Readonly(p.Moves[0].SpeedKeyPoints, K(1000, 0, 99)); Readonly(p.Moves[1].XPositionKeyPoints, K(1001, 0, 99));
        Row(p, 1, 20, 0, 0, 5, 4, 4, new(4, -10, 0));
    }
    private static void GuardBrackets()
    {
        var moves = new[] { M(10, new[] { K(1, 0, 2, floor: 3) }, new[] { K(2, 0, .25f), K(3, 2, .75f) }) };
        var events = new List<string>(); var guardCalls = 0;
        var p = Project(moves, R(), (_, _) => { events.Add("I.enter"); events.Add("I.exit"); return 1; },
            (progress, _) => { events.Add("E.enter"); Check(Bits(progress) == Bits(.5f), "Guarded X progress changed."); events.Add("E.exit"); return .5f; },
            () => { ++guardCalls; events.Add("G"); });
        Row(p, 0, 10, 0, 0, 5, 2, 4, new(4, -10, 0));
        foreach (var name in new[] { "I", "E" })
        {
            var enter = events.IndexOf(name + ".enter"); var exit = events.IndexOf(name + ".exit");
            Check(enter > 0 && events[enter - 1] == "G" && exit + 1 < events.Count && events[exit + 1] == "G",
                "A controlled callback lacked its immediately preceding/following managed guard.");
        }
        Check(guardCalls >= 4, "Both callbacks were not bracketed by independent managed checks.");
        foreach (var withdrawAtEase in new[] { false, true })
        {
            var live = true; var ranges = 0; var eases = 0; var withdrawnChecks = 0;
            Reject(() => Project(moves, R(), (_, _) => { ++ranges; if (!withdrawAtEase) live = false; return 1; },
                (_, _) => { ++eases; live = false; return .5f; }, () => { if (!live) { ++withdrawnChecks; throw new InvalidOperationException("Controlled source withdrawn."); } }));
            Check(ranges == 1 && eases == (withdrawAtEase ? 1 : 0) && withdrawnChecks >= 1,
                "Callback source withdrawal escaped its post-call guard or permitted a later easing callback.");
        }
        var sourceLive = true; var readFired = false; var calculations = 0;
        var observed = new ObservedList<SeekCanvasTargetKeyPoint>(new[] { K(1, 0, 2, floor: 3) }, () => { readFired = true; sourceLive = false; });
        Reject(() => Project(new[] { M(10, observed, new[] { K(2, 0, .25f) }) }, R(), (_, _) => { ++calculations; return 1; },
            (_, _) => { ++calculations; return .5f; }, () => { if (!sourceLive) throw new InvalidOperationException("Source-list read withdrew capture."); }));
        Check(readFired && calculations == 0, "Source withdrawal during bounded copying reached a calculation callback."); StableControl();
    }
    private static void ReentryAndFailures()
    {
        var moves = new[] { M(10, new[] { K(1, 0, 2, floor: 3) }, new[] { K(2, 0, .25f), K(3, 2, .75f) }) };
        foreach (var boundary in new[] { "guard", "interval", "ease" })
        {
            var fired = false; var nestedGuards = 0; var nestedCalculations = 0;
            void Nested()
            {
                if (fired) return; fired = true;
                Reject(() => NativeSeekCanvasTargetPlan.Capture(moves, R(), (_, _) => { ++nestedCalculations; return 1; },
                    (_, _) => { ++nestedCalculations; return .5f; }, () => ++nestedGuards));
                Check(nestedGuards == 0 && nestedCalculations == 0, "Reentrant capture entered nested guards or sample callbacks before rejection.");
            }
            var p = Project(moves, R(), (_, _) => { if (boundary == "interval") Nested(); return 1; },
                (_, _) => { if (boundary == "ease") Nested(); return .5f; }, () => { if (boundary == "guard") Nested(); });
            Check(fired, "Selected synchronous capture reentry boundary was not exercised."); Row(p, 0, 10, 0, 0, 5, 2, 4, new(4, -10, 0));
        }
        foreach (var failEase in new[] { false, true })
        {
            var ranges = 0; var eases = 0;
            Reject(() => Project(moves, R(), (_, _) => { ++ranges; if (!failEase) throw new IOException("Controlled interval failure."); return 1; },
                (_, _) => { ++eases; throw new IOException("Controlled ease failure."); }));
            Check(ranges == 1 && eases == (failEase ? 1 : 0), "A failed controlled sample was replaced by identity/theory or followed by another callback.");
            var recovered = Project(moves, R(), (_, _) => 1, (_, _) => .5f); Row(recovered, 0, 10, 0, 0, 5, 2, 4, new(4, -10, 0));
        }
    }
    private static void NonfiniteRequest()
    {
        var original = R();
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        foreach (var request in new[]
        {
            original with { ChartTime = bad }, original with { Speed = bad }, original with { ScreenWidth = bad },
            original with { Down = original.Down with { X = bad } }, original with { Down = original.Down with { Y = bad } }, original with { Down = original.Down with { Z = bad } },
            original with { Right = original.Right with { X = bad } }, original with { Right = original.Right with { Y = bad } }, original with { Right = original.Right with { Z = bad } }
        })
        {
            var callbacks = 0; Reject(() => Project(One(), request, (_, _) => { ++callbacks; return 1; }, (_, _) => { ++callbacks; return .5f; }));
            Check(callbacks == 0, "A nonfinite explicit request/basis reached controlled numerical calculation.");
        }
        StableControl();
    }
    private static void NonfinitePointsAndSamples()
    {
        foreach (var speedList in new[] { false, true })
        foreach (var property in new[] { "Time", "Value", "FloorPosition" })
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var speed = new[] { K(1, 0, 2, floor: 3), K(2, 2, 4, floor: 5) };
            var x = new[] { K(3, 0, .25f), K(4, 2, .75f) }; var list = speedList ? speed : x; var row = list[1];
            list[1] = property switch { "Time" => row with { Time = bad }, "Value" => row with { Value = bad }, _ => row with { FloorPosition = bad } };
            var callbacks = 0; Reject(() => Project(new[] { M(10, speed, x) }, R(), (_, _) => { ++callbacks; return 1; }, (_, _) => { ++callbacks; return .5f; }));
            Check(callbacks == 0, "An unselected or currently unused keypoint's nonfinite field escaped full source validation.");
        }
        var valid = new[] { M(10, new[] { K(1, 0, 2, floor: 3) }, new[] { K(2, 0, .25f), K(3, 2, .75f) }) };
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        foreach (var badEase in new[] { false, true })
        {
            var ranges = 0; var eases = 0;
            Reject(() => Project(valid, R(), (_, _) => { ++ranges; return badEase ? 1 : bad; }, (_, _) => { ++eases; return bad; }));
            Check(ranges == 1 && eases == (badEase ? 1 : 0), "A nonfinite interval/ease result was clamped, hidden or replaced by a fallback.");
        }
        StableControl();
    }
    private static void Overflow()
    {
        var max = float.MaxValue; var half = max * .5f;
        Reject(() => Project(new[] { M(10, new[] { K(1, 0, 2) }, new[] { K(2, 0, .25f) }) }, R(), (_, _) => max, (_, _) => .5f)); // interval*value
        Reject(() => Project(new[] { M(10, new[] { K(1, 0, 1, floor: max) }, new[] { K(2, 0, .25f) }) }, R(), (_, _) => max, (_, _) => .5f)); // floor addition
        Reject(() => Project(new[] { M(10, new[] { K(1, 0, 0, floor: 3) }, new[] { K(2, -max, 0), K(3, max, 1) }) }, R(0), (_, _) => 0, (_, _) => .5f)); // Single time span
        Reject(() => Project(new[] { M(10, new[] { K(1, 0, 0, floor: 3) }, new[] { K(2, 0, -max), K(3, 2, max) }) }, R(), (_, _) => 0, (_, _) => .5f)); // X value difference
        Reject(() => Project(new[] { M(10, new[] { K(1, 0, 0, floor: 3) }, new[] { K(2, 0, max) }) }, R(width: 2), (_, _) => 0, (_, _) => .5f)); // X*width
        Reject(() => Project(new[] { M(10, new[] { K(1, 0, 0, floor: max) }, new[] { K(2, 0, 0) }) }, R(speed: 2), (_, _) => 0, (_, _) => .5f)); // floor*speed
        Reject(() => Project(new[] { M(10, new[] { K(1, 0, 0, floor: max) }, new[] { K(2, 0, 0) }) }, R(speed: 1, down: new(2, 0, 0)), (_, _) => 0, (_, _) => .5f)); // down component
        Reject(() => Project(new[] { M(10, new[] { K(1, 0, 0, floor: 3) }, new[] { K(2, 0, max) }) }, R(width: 1, right: new(2, 0, 0)), (_, _) => 0, (_, _) => .5f)); // right component
        Reject(() => Project(new[] { M(10, new[] { K(1, 0, 0, floor: half) }, new[] { K(2, 0, half) }) },
            R(speed: 1, width: 1, down: new(1, 0, 0), right: new(2, 0, 0)), (_, _) => 0, (_, _) => .5f)); // component addition
        StableControl();
    }
    private sealed class RepeatedList<T> : IReadOnlyList<T>
    {
        private readonly T _value; internal RepeatedList(T value, int count) { _value = value; Count = count; }
        public int Count { get; }
        public T this[int index] => (uint)index < (uint)Count ? _value : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<T> GetEnumerator() { for (var i = 0; i < Count; ++i) yield return _value; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class ReportedList<T> : IReadOnlyList<T>
    {
        private readonly T[] _rows; internal int Visits;
        internal ReportedList(T[] rows, int count) { _rows = rows; Count = count; }
        public int Count { get; }
        public T this[int index] => _rows[index];
        public IEnumerator<T> GetEnumerator() { foreach (var value in _rows) { ++Visits; yield return value; } }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class ObservedList<T> : IReadOnlyList<T>
    {
        private readonly T[] _rows; private readonly Action _read;
        internal ObservedList(T[] rows, Action read) { _rows = rows; _read = read; }
        public int Count { get { _read(); return _rows.Length; } }
        public T this[int index] { get { _read(); return _rows[index]; } }
        public IEnumerator<T> GetEnumerator() { foreach (var value in _rows) { _read(); yield return value; } }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static void Bounds()
    {
        Check(NativeSeekCanvasTargetPlan.MaxCanvases == 65536 && NativeSeekCanvasTargetPlan.MaxKeypoints == 65536
            && NativeSeekCanvasTargetPlan.MaxTotalKeypoints == 1048576, "Complete Canvas/keypoint budgets differ from the stated capture contract.");
        var single = One()[0]; var calls = 0;
        var maxCanvases = Project(new RepeatedList<SeekCanvasTargetMove>(single, 65536), R(), (_, _) => { ++calls; return 1; },
            (_, _) => throw new InvalidOperationException("Budget self X must bypass ease."));
        Check(maxCanvases.Rows.Count == 65536 && maxCanvases.Moves.Count == 65536 && calls == 65536,
            "Exact maximum canvas count was rejected, truncated or deduplicated by repeated Move reference.");
        Row(maxCanvases, 0, 10, 0, 0, 5, 2, 2, new(2, -10, 0)); Row(maxCanvases, 65535, 10, 0, 0, 5, 2, 2, new(2, -10, 0));
        var speed = new RepeatedList<SeekCanvasTargetKeyPoint>(K(1, 0, 2, floor: 3), 65536);
        var x = new RepeatedList<SeekCanvasTargetKeyPoint>(K(2, 0, .25f), 65536); var wide = M(20, speed, x); calls = 0;
        var maxTotal = Project(new RepeatedList<SeekCanvasTargetMove>(wide, 8), R(), (_, _) => { ++calls; return 1; },
            (_, _) => throw new InvalidOperationException("Repeated last X must bypass ease."));
        Check(maxTotal.Rows.Count == 8 && maxTotal.Moves.All(m => m.SpeedKeyPoints.Count == 65536 && m.XPositionKeyPoints.Count == 65536)
            && calls == 8, "Exact per-list/total budget was rejected or references were collapsed instead of completely copied.");
        Row(maxTotal, 0, 20, 65535, 65535, 5, 2, 2, new(2, -10, 0)); Row(maxTotal, 7, 20, 65535, 65535, 5, 2, 2, new(2, -10, 0));
        foreach (var invalid in new IReadOnlyList<SeekCanvasTargetMove>[]
        {
            new RepeatedList<SeekCanvasTargetMove>(single, 65537),
            new[] { M(30, new RepeatedList<SeekCanvasTargetKeyPoint>(K(1, 0, 2), 65537), new[] { K(2, 0, .25f) }) },
            new[] { M(31, new[] { K(1, 0, 2) }, new RepeatedList<SeekCanvasTargetKeyPoint>(K(2, 0, .25f), 65537)) },
            new RepeatedList<SeekCanvasTargetMove>(wide, 9)
        })
        {
            var callbacks = 0; Reject(() => Project(invalid, R(), (_, _) => { ++callbacks; return 1; }, (_, _) => { ++callbacks; return .5f; }));
            Check(callbacks == 0, "Over-budget complete source was truncated or computed before its total coverage was validated.");
        }
        var extraMoves = new ReportedList<SeekCanvasTargetMove>(new[] { single, single }, 1);
        var shortMoves = new ReportedList<SeekCanvasTargetMove>(new[] { single }, 2);
        var extraPoints = new ReportedList<SeekCanvasTargetKeyPoint>(new[] { K(1, 0, 2), K(2, 0, 3) }, 1);
        var shortPoints = new ReportedList<SeekCanvasTargetKeyPoint>(new[] { K(1, 0, 2) }, 2);
        foreach (var invalid in new IReadOnlyList<SeekCanvasTargetMove>[]
        { extraMoves, shortMoves, new[] { M(40, extraPoints, new[] { K(3, 0, .25f) }) }, new[] { M(41, new[] { K(3, 0, 2) }, shortPoints) } })
        {
            var callbacks = 0; Reject(() => Project(invalid, R(), (_, _) => { ++callbacks; return 1; }, (_, _) => { ++callbacks; return .5f; }));
            Check(callbacks == 0, "Advertised Count concealed extra/missing source enumeration and yielded partial projection.");
        }
        Check(extraMoves.Visits <= 2 && extraPoints.Visits <= 2, "Complete coverage detection scanned beyond declared count plus one row.");
    }
    private static void NullEmptyAndScope()
    {
        var request = R(); var calls = 0;
        Reject(() => NativeSeekCanvasTargetPlan.Capture(null!, request, (_, _) => 1, (_, _) => .5f, () => { }));
        Reject(() => NativeSeekCanvasTargetPlan.Capture(One(), null!, (_, _) => 1, (_, _) => .5f, () => { }));
        Reject(() => NativeSeekCanvasTargetPlan.Capture(One(), request, null!, (_, _) => .5f, () => { }));
        Reject(() => NativeSeekCanvasTargetPlan.Capture(One(), request, (_, _) => 1, null!, () => { }));
        Reject(() => NativeSeekCanvasTargetPlan.Capture(One(), request, (_, _) => 1, (_, _) => .5f, null!));
        foreach (var moves in new IReadOnlyList<SeekCanvasTargetMove>[]
        {
            new SeekCanvasTargetMove[] { null! },
            new[] { M(10, null!, new[] { K(2, 0, .25f) }) }, new[] { M(10, new[] { K(1, 0, 2) }, null!) },
            new[] { M(10, Array.Empty<SeekCanvasTargetKeyPoint>(), new[] { K(2, 0, .25f) }) },
            new[] { M(10, new[] { K(1, 0, 2) }, Array.Empty<SeekCanvasTargetKeyPoint>()) },
            new[] { M(10, new SeekCanvasTargetKeyPoint[] { null! }, new[] { K(2, 0, .25f) }) },
            new[] { M(10, new[] { K(1, 0, 2) }, new SeekCanvasTargetKeyPoint[] { null! }) }
        })
        {
            Reject(() => Project(moves, request, (_, _) => { ++calls; return 1; }, (_, _) => { ++calls; return .5f; }));
            Check(calls == 0, "Null/empty per-canvas source was invented, skipped or passed to a calculation callback.");
        }
        var guards = 0; var empty = Project(Array.Empty<SeekCanvasTargetMove>(), request,
            (_, _) => { ++calls; return 1; }, (_, _) => { ++calls; return .5f; }, () => ++guards);
        Check(empty.Moves.Count == 0 && empty.Rows.Count == 0 && calls == 0 && guards >= 2 && !empty.CompleteSeekPermitted,
            "An empty explicit actual canvas array was filled with synthetic rows or skipped capture guards.");
        var type = typeof(NativeSeekCanvasTargetPlan);
        Check(!typeof(IDisposable).IsAssignableFrom(type)
            && type.GetProperty("RootOwner", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is null,
            "Pure numeric Canvas projection advertises native-root ownership or native cleanup responsibility.");
        StableControl();
    }
}
