using System.Runtime.CompilerServices;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

// Values here are explicit target inputs. The caller must establish their
// provenance; neither record accepts a retained source baseline as proof.
internal sealed record SeekLineRingStaticXNumericInput(
    float StartFloor, float EndFloor, float StartX, float EndX,
    float CanvasFloor, float CanvasX, float ScreenWidth);

internal sealed record SeekLineRingDynamicXNumericInput(
    float TargetRingWorldY, NativeVector3 StartWorld, NativeVector3 EndWorld,
    float CameraScale, float SceneScale, float CameraX);

internal enum SeekLineRingXBranch { Static, Dynamic }
internal enum SeekLineRingLocalYAction { PreserveLocalY }

internal sealed class SeekLineRingXNumericResult<TInput> where TInput : class
{
    internal SeekLineRingXBranch Branch { get; }
    internal TInput Input { get; }
    internal IReadOnlyList<float> InitializedSamples { get; }
    internal float RawProgress { get; }
    internal float EaseBeforeClamp { get; }
    internal float ClampedEase { get; }
    internal float LocalX { get; }
    internal SeekLineRingLocalYAction LocalYAction => SeekLineRingLocalYAction.PreserveLocalY;
    internal float LocalZ => -0.5f;
    internal bool CompleteSeekPermitted => false;

    internal SeekLineRingXNumericResult(SeekLineRingXBranch branch, TInput input,
        float[] ownedSamples, float rawProgress, float easeBeforeClamp, float clampedEase, float localX)
    {
        Branch = branch; Input = input; InitializedSamples = Array.AsReadOnly(ownedSamples);
        RawProgress = rawProgress; EaseBeforeClamp = easeBeforeClamp;
        ClampedEase = clampedEase; LocalX = localX;
    }
}

/// <summary>Only the two SetRing producer-input X paths. This has no native,
/// source, root, worker, registry, renderer or complete-Seek behavior.</summary>
internal static class NativeSeekLineRingXNumericMath
{
    private const int EaseSampleCount = 101;
    [ThreadStatic] private static bool _running;

    internal static SeekLineRingXNumericResult<SeekLineRingStaticXNumericInput> ProjectStatic(
        SeekLineRingStaticXNumericInput input, IReadOnlyList<float> initializedSamples, Action guard)
        => Run(guard, () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var owned = input with { };
            Check(owned.StartFloor); Check(owned.EndFloor);
            Check(owned.StartX); Check(owned.EndX);
            Check(owned.CanvasFloor); Check(owned.CanvasX);
            Positive(owned.ScreenWidth, "screen width");
            var denominator = Sub(owned.EndFloor, owned.StartFloor);
            var q = Div(Sub(owned.CanvasFloor, owned.StartFloor), denominator);
            var samples = CopySamples(initializedSamples, guard);
            var ease = EaseProgress(q, samples);
            var clamped = Clamp01(ease);
            var difference = Sub(owned.EndX, owned.StartX);
            var mixed = Add(Mul(difference, clamped), owned.StartX);
            var x = Add(Mul(mixed, owned.ScreenWidth), owned.CanvasX);
            return new SeekLineRingXNumericResult<SeekLineRingStaticXNumericInput>(
                SeekLineRingXBranch.Static, owned, samples, q, ease, clamped, x);
        });

    internal static SeekLineRingXNumericResult<SeekLineRingDynamicXNumericInput> ProjectDynamic(
        SeekLineRingDynamicXNumericInput input, IReadOnlyList<float> initializedSamples, Action guard)
        => Run(guard, () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var owned = input with { };
            Check(owned.TargetRingWorldY); Vector(owned.StartWorld); Vector(owned.EndWorld);
            Positive(owned.CameraScale, "camera scale");
            Positive(owned.SceneScale, "scene scale"); Check(owned.CameraX);
            var denominator = Sub(owned.EndWorld.Y, owned.StartWorld.Y);
            var q = Div(Sub(owned.TargetRingWorldY, owned.StartWorld.Y), denominator);
            var samples = CopySamples(initializedSamples, guard);
            var ease = EaseProgress(q, samples);
            var clamped = Clamp01(ease);
            var difference = Sub(owned.EndWorld.X, owned.StartWorld.X);
            var mixed = Add(Mul(difference, clamped), owned.StartWorld.X);
            var x = Sub(Div(Div(mixed, owned.CameraScale), owned.SceneScale), owned.CameraX);
            return new SeekLineRingXNumericResult<SeekLineRingDynamicXNumericInput>(
                SeekLineRingXBranch.Dynamic, owned, samples, q, ease, clamped, x);
        });

    private static float EaseProgress(float q, float[] samples)
    {
        // Pinned GetEaseWithProgress: Single scale, cvttss2si, then table
        // boundary or four separate Single interpolation operations.
        var scaled = Mul(q, 100f);
        if ((double)scaled < int.MinValue || (double)scaled > int.MaxValue)
            throw new NotSupportedException("The Ease index is outside native Int32 conversion support.");
        var index = (int)scaled;
        if (index < 0) return samples[0];
        if (index >= 100) return samples[100];
        var fraction = Sub(scaled, (float)index);
        var difference = Sub(samples[index + 1], samples[index]);
        return Add(Mul(fraction, difference), samples[index]);
    }

    private static float Clamp01(float value)
    {
        // SetRing clamps the ease result, after the progress table lookup.
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }

    private static float[] CopySamples(IReadOnlyList<float> values, Action guard)
    {
        ArgumentNullException.ThrowIfNull(values);
        guard(); var count = values.Count; guard();
        if (count != EaseSampleCount)
            throw new NotSupportedException("The selected initialized Ease row must contain all 101 samples.");
        var rows = new List<float>(count); guard();
        using (var iterator = values.GetEnumerator())
        {
            guard();
            while (true)
            {
                guard(); var more = iterator.MoveNext(); guard();
                if (!more) break;
                guard(); var value = iterator.Current; guard();
                if (rows.Count >= count)
                    throw new InvalidDataException("Ease enumeration exceeds its declared complete count.");
                rows.Add(Check(value)); guard();
            }
        }
        guard(); var finalCount = values.Count; guard();
        if (finalCount != count || rows.Count != count)
            throw new InvalidDataException("The initialized Ease row changed or omitted a sample.");
        return rows.ToArray();
    }

    private static T Run<T>(Action guard, Func<T> action)
    {
        if (_running) throw new InvalidOperationException("Ring X projection cannot synchronously reenter its guard or input.");
        _running = true;
        try { ArgumentNullException.ThrowIfNull(guard); guard(); var result = action(); guard(); return result; }
        finally { _running = false; }
    }

    private static void Vector(NativeVector3 value) { Check(value.X); Check(value.Y); Check(value.Z); }
    private static void Positive(float value, string name)
    { if (Check(value) <= 0f) throw new NotSupportedException($"Ring X projection requires positive finite {name}."); }
    private static float Check(float value)
    { if (!float.IsFinite(value)) throw new NotSupportedException("Ring X projection rejects nonfinite or overflowed Single values."); return value; }
    [MethodImpl(MethodImplOptions.NoInlining)] private static float Add(float a, float b) => Check(a + b);
    [MethodImpl(MethodImplOptions.NoInlining)] private static float Sub(float a, float b) => Check(a - b);
    [MethodImpl(MethodImplOptions.NoInlining)] private static float Mul(float a, float b) => Check(a * b);
    [MethodImpl(MethodImplOptions.NoInlining)] private static float Div(float a, float b)
    { if (b == 0f) throw new NotSupportedException("Ring X projection rejects a zero denominator."); return Check(a / b); }
}
