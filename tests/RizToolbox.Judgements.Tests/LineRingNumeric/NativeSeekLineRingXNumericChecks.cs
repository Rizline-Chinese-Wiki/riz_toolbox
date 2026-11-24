using System.Collections;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.LineRingNumeric;

internal static class NativeSeekLineRingXNumericChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Line ring numeric ring static exact Fraction bit goldens", StaticGoldens),
        ("Line ring numeric ring dynamic exact Fraction bit goldens", DynamicGoldens),
        ("Line ring numeric ring finite positive and zero denominator boundaries", Boundaries),
        ("Line ring numeric ring complete initialized table enumeration", CompleteSamples),
        ("Line ring numeric ring immutable owned input samples and Y Z action", Ownership),
        ("Line ring numeric ring guard withdrawal and synchronous reentry", GuardAndReentry),
        ("Line ring numeric ring negative zero ease boundary", SignedZero),
    };

    private static float[] Samples()
    {
        var samples = Enumerable.Repeat(0.25f, 101).ToArray();
        samples[1] = 0.75f; samples[25] = 1.5f;
        samples[50] = 0.2f; samples[51] = 0.8f;
        samples[75] = -0.25f; samples[100] = 1.25f;
        return samples;
    }
    private static SeekLineRingStaticXNumericInput StaticInput() => new(1, 3, -1.25f, 3.5f, 2.01f, -0.125f, 1.3f);
    private static SeekLineRingDynamicXNumericInput DynamicInput() =>
        new(2.01f, new(-1.25f, 1, 0), new(3.5f, 3, 0), 1.25f, 0.8f, 0.3f);
    private static SeekLineRingXNumericResult<SeekLineRingStaticXNumericInput> Static(
        SeekLineRingStaticXNumericInput? input = null, IReadOnlyList<float>? samples = null, Action? guard = null)
        => NativeSeekLineRingXNumericMath.ProjectStatic(input ?? StaticInput(), samples ?? Samples(), guard ?? (() => { }));
    private static SeekLineRingXNumericResult<SeekLineRingDynamicXNumericInput> Dynamic(
        SeekLineRingDynamicXNumericInput? input = null, IReadOnlyList<float>? samples = null, Action? guard = null)
        => NativeSeekLineRingXNumericMath.ProjectDynamic(input ?? DynamicInput(), samples ?? Samples(), guard ?? (() => { }));
    private static void Check(bool condition, string why)
    { if (!condition) throw new InvalidOperationException(why); }
    private static void Bits(float actual, uint expected, string why)
    {
        var observed = (uint)BitConverter.SingleToInt32Bits(actual);
        Check(observed == expected, $"{why}: observed {observed:x8}, expected {expected:x8}");
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name} refusal.");
    }
    private static void Components<T>(SeekLineRingXNumericResult<T> result, SeekLineRingXBranch branch) where T : class
    {
        Check(result.Branch == branch && result.LocalYAction == SeekLineRingLocalYAction.PreserveLocalY
            && !result.CompleteSeekPermitted, "Branch, Y preservation, or complete Seek claim is wrong.");
        Bits(result.LocalZ, 0xbf000000u, "Native ring local Z");
        Check(result.InitializedSamples.Count == 101, "The owned table is incomplete.");
    }
    private static void StaticGoldens()
    {
        // All bit values come from oracle_fraction.py's exact Fraction rounding,
        // not a copy of the C# arithmetic implementation.
        var interpolation = Static(); Components(interpolation, SeekLineRingXBranch.Static);
        Bits(interpolation.RawProgress, 0x3f0147aeu, "Static raw progress");
        Bits(interpolation.EaseBeforeClamp, 0x3f000000u, "Static interpolated ease");
        Bits(interpolation.ClampedEase, 0x3f000000u, "Static clamped ease");
        Bits(interpolation.LocalX, 0x3fab3333u, "Static Canvas floor/X/width path");

        var negative = Static(new(0, 1, 1, 9, -0.005f, 3, 2));
        Bits(negative.RawProgress, 0xbba3d70au, "Negative static progress remains raw");
        Bits(negative.EaseBeforeClamp, 0u, "Negative fraction extrapolates before clamp");
        Bits(negative.LocalX, 0x40a00000u, "Early progress clamp would change X");

        var overshoot = Static(new(0, 1, -1, 2, 0.25f, 3, 2));
        Bits(overshoot.EaseBeforeClamp, 0x3fc00000u, "Static ease overshoot retained");
        Bits(overshoot.ClampedEase, 0x3f800000u, "Static caller clamps ease");
        Bits(overshoot.LocalX, 0x40e00000u, "Static overshoot X");
    }
    private static void DynamicGoldens()
    {
        var interpolation = Dynamic(); Components(interpolation, SeekLineRingXBranch.Dynamic);
        Bits(interpolation.RawProgress, 0x3f0147aeu, "Dynamic world Y progress");
        Bits(interpolation.EaseBeforeClamp, 0x3f000000u, "Dynamic interpolated ease");
        Bits(interpolation.LocalX, 0x3f533333u, "Dynamic world/camera/scene X");

        var negative = Dynamic(new(-0.005f, new(1, 0, 0), new(9, 1, 0), 2, 4, 0.5f));
        Bits(negative.RawProgress, 0xbba3d70au, "Negative dynamic progress remains raw");
        Bits(negative.EaseBeforeClamp, 0u, "Dynamic fraction extrapolates before clamp");
        Bits(negative.LocalX, 0xbec00000u, "Dynamic camera X subtraction order");

        var undershoot = Dynamic(new(0.75f, new(-1, 0, 0), new(2, 1, 0), 1, 1, 0));
        Bits(undershoot.EaseBeforeClamp, 0xbe800000u, "Negative ease sample retained");
        Bits(undershoot.ClampedEase, 0u, "Dynamic caller clamps negative ease");
        Bits(undershoot.LocalX, 0xbf800000u, "Dynamic undershoot X");

        var high = Dynamic(new(1.25f, new(-1, 0, 0), new(2, 1, 0), 1, 1, 0));
        Bits(high.RawProgress, 0x3fa00000u, "High progress remains raw");
        Bits(high.EaseBeforeClamp, 0x3fa00000u, "High index uses sample100");
        Bits(high.ClampedEase, 0x3f800000u, "High ease caller clamp");
        Bits(high.LocalX, 0x40000000u, "High endpoint X");
    }
    private static void Boundaries()
    {
        Reject<NotSupportedException>(() => Static(StaticInput() with { EndFloor = 1 }));
        Reject<NotSupportedException>(() => Dynamic(DynamicInput() with { EndWorld = new(3.5f, 1, 0) }));
        Reject<NotSupportedException>(() => Static(StaticInput() with { ScreenWidth = 0 }));
        Reject<NotSupportedException>(() => Static(StaticInput() with { ScreenWidth = -1 }));
        Reject<NotSupportedException>(() => Dynamic(DynamicInput() with { CameraScale = 0 }));
        Reject<NotSupportedException>(() => Dynamic(DynamicInput() with { SceneScale = -1 }));
        Reject<NotSupportedException>(() => Dynamic(DynamicInput() with { CameraX = float.NaN }));
        Reject<NotSupportedException>(() => Dynamic(DynamicInput() with { StartWorld = new(0, 0, float.PositiveInfinity) }));
        Reject<NotSupportedException>(() => Static(StaticInput() with { CanvasFloor = 1e8f, StartFloor = 0, EndFloor = 1 }));
        Reject<NotSupportedException>(() => Static(StaticInput() with { StartX = float.MaxValue, EndX = -float.MaxValue }));
        // Any positive Single denominator is supported if the result remains finite.
        Bits(Static(new(0, float.Epsilon, 0, 1, 0, 0, 1)).RawProgress, 0u, "No floor epsilon substitution");
        Bits(Dynamic(new(0, new(0, 0, 0), new(1, float.Epsilon, 0), 1, 1, 0)).RawProgress,
            0u, "No world Y epsilon substitution");
    }
    private static void CompleteSamples()
    {
        Reject<NotSupportedException>(() => Static(samples: new float[100]));
        Reject<NotSupportedException>(() => Dynamic(samples: new float[102]));
        var invalid = Samples(); invalid[99] = float.NaN;
        Reject<NotSupportedException>(() => Static(samples: invalid));
        Reject<InvalidDataException>(() => Static(samples: new CountedSamples(Samples(), 101, 100)));
        Reject<InvalidDataException>(() => Dynamic(samples: new CountedSamples(Samples(), 101, 102)));
        Reject<InvalidDataException>(() => Static(samples: new CountedSamples(Samples(), 101, 101, changeCount: true)));
    }
    private static void Ownership()
    {
        var source = Samples(); var input = StaticInput();
        var first = Static(input, source);
        Check(!ReferenceEquals(first.Input, input), "Input record was retained rather than copied.");
        source[50] = float.NaN;
        Bits(first.InitializedSamples[50], 0x3e4ccccdu, "Retained samples changed with the caller array");
        var readOnly = (IList<float>)first.InitializedSamples;
        Reject<NotSupportedException>(() => readOnly[50] = 0);
        Bits(first.LocalX, 0x3fab3333u, "Historical result changed after source mutation");
        Reject<NotSupportedException>(() => Static(input, source));
    }
    private static void GuardAndReentry()
    {
        var list = new CountedSamples(Samples(), 101, 101);
        var calls = 0;
        Reject<OperationCanceledException>(() => Static(samples: list, guard: () =>
        { if (++calls == 10) throw new OperationCanceledException(); }));
        Check(list.DisposeCount == 1, "Withdrawal did not dispose the owned enumeration once.");
        var nestedGuardCalls = 0;
        Reject<InvalidOperationException>(() => Static(guard: () =>
            Static(guard: () => ++nestedGuardCalls)));
        Check(nestedGuardCalls == 0, "Nested guard ran before reentry refusal.");
        Bits(Static().LocalX, 0x3fab3333u, "Projection remained locked after a rejected callback");
    }
    private static void SignedZero()
    {
        var samples = Samples(); samples[0] = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        var result = Static(new(0, 1, 0, 1, -0.02f, 0, 1), samples);
        Bits(result.EaseBeforeClamp, 0x80000000u, "The low table endpoint lost negative zero");
        Bits(result.ClampedEase, 0x80000000u, "Caller clamp changed negative zero");
    }

    private sealed class CountedSamples : IReadOnlyList<float>
    {
        private readonly float[] _values;
        private readonly int _declared, _emitted;
        private readonly bool _changeCount;
        private int _reads;
        internal int DisposeCount { get; private set; }
        internal CountedSamples(float[] values, int declared, int emitted, bool changeCount = false)
        { _values = values; _declared = declared; _emitted = emitted; _changeCount = changeCount; }
        public int Count => _changeCount && ++_reads > 1 ? _declared - 1 : _declared;
        public float this[int index] => throw new InvalidOperationException("Projection must enumerate the complete table.");
        public IEnumerator<float> GetEnumerator() => new Iterator(this);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Iterator : IEnumerator<float>
        {
            private readonly CountedSamples _owner;
            private int _index = -1;
            internal Iterator(CountedSamples owner) { _owner = owner; }
            public float Current => _owner._values[_index % _owner._values.Length];
            object IEnumerator.Current => Current;
            public bool MoveNext() => ++_index < _owner._emitted;
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => ++_owner.DisposeCount;
        }
    }
}
