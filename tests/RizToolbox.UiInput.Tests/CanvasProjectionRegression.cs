using RizToolbox.Adapter.UiInput;
using RizToolbox.Core.Input;
using RizToolbox.Sdk;

internal static class CanvasProjectionRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Canvas nonuniform units preserve uniform UI pixels through owned-root compensation", Nonuniform),
        ("Canvas overscan camera offset and screen clipping round-trip actual owned coordinates", Clipped),
        ("Canvas rotation skew perspective behind-camera and reversed extents refuse projection", Unsupported),
        ("Canvas nonfinite degenerate and empty visible geometry refuse projection", Invalid)
    };
    private static CanvasProjection Make(UiRect local, UiRect canvas, UiRect camera, float width, float height,
        NativeVector3 tl, NativeVector3 tr, NativeVector3 bl, NativeVector3 br)
        => CanvasProjection.Create(local, canvas, camera, width, height, tl, tr, bl, br);
    private static CanvasProjection Normal() => Make(new(-300, -200, 600, 400), new(0, 0, 1200, 800), new(0, 0, 1200, 800),
        1200, 800, new(0, 800, 10), new(1200, 800, 10), new(0, 0, 10), new(1200, 0, 10));
    private static void Nonuniform()
    {
        var p = Make(new(-500, -500, 1000, 1000), new(0, 0, 2000, 1000), new(0, 0, 2000, 1000),
            2000, 1000, new(0, 1000, 20), new(2000, 1000, 20), new(0, 0, 20), new(2000, 0, 20));
        Near(.5f, p.RootScaleX); Near(1, p.UnitsPerPixel); Near(2000, p.RootWidth); Near(1000, p.RootHeight);
        // Apply the real root compensation and parent scales, then compare to the public uniform viewport contract.
        RoundTrip(p, new(0, 1000), 2, 1, 2000, 1000, new(230, 470));
        RoundTrip(p, new(0, 1000), 2, 1, 2000, 1000, new(1500, 700));
    }
    private static void Clipped()
    {
        var p = Make(new(-300, -200, 600, 400), new(-100, -50, 1300, 900), new(100, 70, 1050, 700),
            1000, 800, new(-100, 850, 15), new(1100, 850, 15), new(-100, -50, 15), new(1100, -50, 15));
        if (p.Pixels != new UiRect(100, 70, 900, 700)) throw new InvalidOperationException("Viewport intersection was not applied.");
        Near(100, p.RootX); Near(80 / 2.25f, p.RootY); Near(900 / 2.25f, p.RootWidth);
        RoundTrip(p, new(-100, 850), 2, 2.25f, 1000, 800, new(100, 130));
        RoundTrip(p, new(-100, 850), 2, 2.25f, 1000, 800, new(1500, 850));
    }
    private static void RoundTrip(CanvasProjection p, UiPoint projectedTopLeft, float parentPixelsX, float parentPixelsY,
        float width, float height, UiPoint ui)
    {
        var layout = new UiLayout(new(0, 0, 1920, 1080));
        var view = UiCoordinates.Fit(width, height, null, p.Pixels, layout);
        var expected = UiCoordinates.UiToScreen(view, ui).Value!;
        var ux = (view.LetterboxX + ui.X * view.PixelsPerReferenceUnit) * p.UnitsPerPixel;
        var uy = (view.LetterboxY + ui.Y * view.PixelsPerReferenceUnit) * p.UnitsPerPixel;
        var actual = new UiPoint(projectedTopLeft.X + (p.RootX + ux * p.RootScaleX) * parentPixelsX,
            projectedTopLeft.Y - (p.RootY + uy) * parentPixelsY);
        Near(expected.X, actual.X); Near(expected.Y, actual.Y);
        var recovered = UiCoordinates.ScreenToUi(view, actual).Value!; Near(ui.X, recovered.X); Near(ui.Y, recovered.Y);
    }
    private static void Unsupported()
    {
        Reject(new(0, 800, 10), new(1200, 790, 10), new(10, 0, 10), new(1210, -10, 10)); // rotation
        Reject(new(20, 800, 10), new(1200, 800, 10), new(0, 0, 10), new(1180, 0, 10)); // skew
        Reject(new(0, 800, 10), new(1200, 800, 11), new(0, 0, 10), new(1200, 0, 11)); // depth-dependent mapping
        Reject(new(0, 800, -10), new(1200, 800, -10), new(0, 0, -10), new(1200, 0, -10));
        Reject(new(1200, 800, 10), new(0, 800, 10), new(1200, 0, 10), new(0, 0, 10));
    }
    private static void Invalid()
    {
        Reject(new(float.NaN, 800, 10), new(1200, 800, 10), new(0, 0, 10), new(1200, 0, 10));
        Reject(new(0, 800, 10), new(0, 800, 10), new(0, 0, 10), new(0, 0, 10));
        Throws(() => Make(new(0, 0, 0, 400), new(0, 0, 1200, 800), new(0, 0, 1200, 800), 1200, 800,
            new(0, 800, 10), new(1200, 800, 10), new(0, 0, 10), new(1200, 0, 10)));
        Throws(() => Make(new(0, 0, 600, 400), new(1300, 0, 100, 800), new(0, 0, 1200, 800), 1200, 800,
            new(0, 800, 10), new(1200, 800, 10), new(0, 0, 10), new(1200, 0, 10)));
        _ = Normal();
    }
    private static void Reject(NativeVector3 tl, NativeVector3 tr, NativeVector3 bl, NativeVector3 br)
        => Throws(() => Make(new(-300, -200, 600, 400), new(0, 0, 1200, 800), new(0, 0, 1200, 800), 1200, 800, tl, tr, bl, br));
    private static void Throws(Action run) { try { run(); } catch (Exception ex) when (ex.GetType().FullName == "RizToolbox.Runtime.UnsupportedSignatureException") { return; } throw new InvalidOperationException("Expected unsupported Canvas projection."); }
    private static void Near(float expected, float actual) { if (Math.Abs(expected - actual) > .002f) throw new InvalidOperationException($"Expected {expected}, observed {actual}."); }
}
