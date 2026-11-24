using System.Runtime.CompilerServices;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal enum SeekLineRingDynamicProjectionKind
{ Buffer, OutsideFutureFloor, OutsidePastFloor, AboveWorldWindow, BelowWorldWindow }

// These explicit numeric inputs are NOT a proof that a retained native source,
// endpoint target hierarchy, camera or initialization seed has been established.
internal sealed record SeekLineRingDynamicNumericInput(float StartFloor, float EndFloor,
    float StartCanvasFloor, float EndCanvasFloor, float Speed,
    NativeVector3 StartWorld, NativeVector3 EndWorld, float CameraScale, float SceneScale);

internal sealed class SeekLineRingDynamicNumericResult
{
    internal SeekLineRingDynamicProjectionKind Kind { get; }
    internal SeekLineRingDynamicNumericInput Input { get; }
    internal int? Step { get; }
    internal int? PositionCount { get; }
    internal IReadOnlyList<NativeVector3> Points { get; }
    internal bool CompleteSeekPermitted => false;
    internal SeekLineRingDynamicNumericResult(SeekLineRingDynamicProjectionKind kind,
        SeekLineRingDynamicNumericInput input, int? step, NativeVector3[] points)
    {
        Kind = kind; Input = input with { }; Step = step;
        // A cull means no SetPositions operation, never a count=0 target.
        PositionCount = kind == SeekLineRingDynamicProjectionKind.Buffer ? points.Length : null;
        Points = Array.AsReadOnly(points);
    }
}

/// <summary>Pure producer-input arithmetic from the pinned static slice.
/// It never captures a source, invokes native code, grants a writer, or supplies
/// missing world/camera/seed provenance. The future target plan owns that gate.</summary>
internal static class NativeSeekLineRingNumericMath
{
    internal const int EaseSamples = 101, MaximumGroupSegments = 65536;
    [ThreadStatic] private static bool _running;

    internal static SeekLineRingDynamicNumericResult ProjectDynamic(SeekLineRingDynamicNumericInput input,
        IReadOnlyList<float> initializedSamples, Action guard) => Run(guard, () =>
    {
        ArgumentNullException.ThrowIfNull(input); var owned = input with { };
        Check(owned.StartFloor); Check(owned.EndFloor); Check(owned.StartCanvasFloor); Check(owned.EndCanvasFloor);
        if (Check(owned.Speed) <= 0) throw new NotSupportedException("The numeric floor window requires positive finite speed.");
        var future = Div(8f, owned.Speed);
        if (owned.StartFloor > Add(future, owned.StartCanvasFloor)
            && owned.EndFloor > Add(future, owned.EndCanvasFloor)) return Cull(SeekLineRingDynamicProjectionKind.OutsideFutureFloor, owned);
        var past = Div(3f, owned.Speed);
        if (Sub(owned.StartCanvasFloor, past) > owned.StartFloor
            && Sub(owned.EndCanvasFloor, past) > owned.EndFloor) return Cull(SeekLineRingDynamicProjectionKind.OutsidePastFloor, owned);
        Vector(owned.StartWorld); Vector(owned.EndWorld);
        if (owned.StartWorld.Y > 5f && owned.EndWorld.Y > 5f) return Cull(SeekLineRingDynamicProjectionKind.AboveWorldWindow, owned);
        if (-5f > owned.StartWorld.Y && -5f > owned.EndWorld.Y) return Cull(SeekLineRingDynamicProjectionKind.BelowWorldWindow, owned);
        if (Check(owned.CameraScale) <= 0 || Check(owned.SceneScale) <= 0)
            throw new NotSupportedException("The first numeric support domain requires positive camera and scene scales.");
        var distance = Distance(owned.StartWorld, owned.EndWorld);
        var step = distance < .5f ? 20 : distance < 1.5f ? 10 : distance < 2.5f ? 5 : distance < 3.5f ? 2 : 1;
        var samples = Copy(initializedSamples, EaseSamples, EaseSamples, guard);
        var points = new NativeVector3[100 / step + 1];
        for (var j = 0; j <= 100; j += step)
        {
            guard();
            // Keep the native subtract-zero / add-zero boundaries, sample X
            // from integer ease without clamping, and sample Y linearly.
            var dx = Sub(Sub(owned.EndWorld.X, owned.StartWorld.X), 0f);
            var x = Div(Div(Add(Mul(samples[j], dx), 0f), owned.CameraScale), owned.SceneScale);
            var dy = Sub(Sub(owned.EndWorld.Y, owned.StartWorld.Y), 0f);
            var y = Div(Div(Add(Mul(dy, Div(j, 100f)), 0f), owned.CameraScale), owned.SceneScale);
            points[j / step] = new(x, y, 0f); guard();
        }
        return new(SeekLineRingDynamicProjectionKind.Buffer, owned, step, points);
    });

    internal static float ReadInitializedIndex(IReadOnlyList<float> initializedSamples, int index, Action guard)
        => Run(guard, () => Copy(initializedSamples, EaseSamples, EaseSamples, guard)[index <= 0 ? 0 : index >= 100 ? 100 : index]);

    // Forward-only native selection from an explicitly supplied seed. This
    // neither derives a canonical seed nor reconstructs a backwards seek.
    internal static int SelectForwardFromSeed(IReadOnlyList<float> startTimes, int provenSeed, float time, Action guard)
        => Run(guard, () =>
        {
            Check(time); var rows = Copy(startTimes, 1, MaximumGroupSegments, guard);
            if (provenSeed < 0 || provenSeed >= rows.Length) throw new ArgumentOutOfRangeException(nameof(provenSeed));
            var selected = provenSeed;
            while (selected < rows.Length - 1 && time > rows[selected + 1]) { guard(); ++selected; guard(); }
            return selected;
        });

    private static SeekLineRingDynamicNumericResult Cull(SeekLineRingDynamicProjectionKind kind, SeekLineRingDynamicNumericInput input)
        => new(kind, input, null, Array.Empty<NativeVector3>());

    private static float Distance(NativeVector3 start, NativeVector3 end)
    {
        var dx = Sub(start.X, end.X); var dy = Sub(start.Y, end.Y); var dz = Sub(start.Z, end.Z);
        var yy = Mul(dy, dy); var xx = Mul(dx, dx); var zz = Mul(dz, dz);
        // Native helper: rounded Y²+X², then Z²; Double sqrt; Single result.
        var sum = Add(Add(yy, xx), zz); return Check((float)Math.Sqrt((double)sum));
    }
    private static T Run<T>(Action guard, Func<T> action)
    {
        if (_running) throw new InvalidOperationException("Line/ring numeric projection cannot reenter before its nested guard.");
        _running = true;
        try { ArgumentNullException.ThrowIfNull(guard); guard(); var result = action(); guard(); return result; }
        finally { _running = false; }
    }
    private static float[] Copy(IReadOnlyList<float> values, int minimum, int maximum, Action guard)
    {
        ArgumentNullException.ThrowIfNull(values); guard(); var count = values.Count; guard();
        if (count < minimum || count > maximum) throw new NotSupportedException("The complete numeric input has an unsupported count.");
        var rows = new List<float>(count); guard();
        using (var iterator = values.GetEnumerator())
        {
            guard();
            while (true)
            {
                guard(); var more = iterator.MoveNext(); guard(); if (!more) break;
                guard(); var value = iterator.Current; guard();
                if (rows.Count >= count) throw new InvalidDataException("The numeric enumeration exceeds its declared complete count.");
                rows.Add(Check(value)); guard();
            }
        }
        guard(); var finalCount = values.Count; guard();
        if (finalCount != count || rows.Count != count) throw new InvalidDataException("The complete numeric input changed or omitted a row.");
        return rows.ToArray();
    }
    private static void Vector(NativeVector3 value) { Check(value.X); Check(value.Y); Check(value.Z); }
    private static float Check(float value)
    { if (!float.IsFinite(value)) throw new NotSupportedException("The first numeric support domain rejects nonfinite/overflow intermediates."); return value; }
    // Non-inlined binary operations preserve signed zero and prohibit fusing
    // consecutive Single multiply/add steps across native operation boundaries.
    [MethodImpl(MethodImplOptions.NoInlining)] private static float Add(float a, float b) => Check(a + b);
    [MethodImpl(MethodImplOptions.NoInlining)] private static float Sub(float a, float b) => Check(a - b);
    [MethodImpl(MethodImplOptions.NoInlining)] private static float Mul(float a, float b) => Check(a * b);
    [MethodImpl(MethodImplOptions.NoInlining)] private static float Div(float a, float b) => Check(a / b);
}
