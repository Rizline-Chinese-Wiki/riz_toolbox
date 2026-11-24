using RizToolbox.Sdk;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.UiInput;

/// <summary>A copied, axis-aligned Canvas projection. Positions are parent units; owned contents use one uniform unit scale.</summary>
internal readonly record struct CanvasProjection(UiRect Pixels, float RootX, float RootY, float RootWidth,
    float RootHeight, float RootScaleX, float UnitsPerPixel)
{
    internal static CanvasProjection Create(UiRect localRect, UiRect canvasViewport, UiRect cameraViewport,
        float screenWidth, float screenHeight, NativeVector3 topLeft, NativeVector3 topRight,
        NativeVector3 bottomLeft, NativeVector3 bottomRight)
    {
        RequireRect(localRect); RequireRect(canvasViewport); RequireRect(cameraViewport);
        if (!float.IsFinite(screenWidth) || !float.IsFinite(screenHeight) || screenWidth <= 0 || screenHeight <= 0)
            throw Unsupported("Physical screen extents must be finite and positive.");
        var corners = new[] { topLeft, topRight, bottomLeft, bottomRight };
        if (corners.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z) || p.Z <= 0))
            throw Unsupported("Canvas corner projection must be finite and in front of its camera.");
        // A RectTransform is planar. Equal camera depths plus axis-aligned matching edges verify the
        // affine screen mapping used below; reject perspective, rotation, shear and reversed axes.
        var depthTolerance = Math.Max(.0001f, Math.Abs(topLeft.Z) * .0001f);
        if (corners.Any(p => Math.Abs(p.Z - topLeft.Z) > depthTolerance))
            throw Unsupported("Canvas plane has an unsupported depth-dependent projection.");
        var projectedWidth = topRight.X - topLeft.X;
        var projectedHeight = topLeft.Y - bottomLeft.Y;
        if (!float.IsFinite(projectedWidth) || !float.IsFinite(projectedHeight) || projectedWidth <= 0 || projectedHeight <= 0)
            throw Unsupported("Canvas screen axes must be positive and non-degenerate.");
        var tolerance = Math.Max(.01f, Math.Max(projectedWidth, projectedHeight) * .00001f);
        if (!Near(topLeft.Y, topRight.Y, tolerance) || !Near(bottomLeft.Y, bottomRight.Y, tolerance) ||
            !Near(topLeft.X, bottomLeft.X, tolerance) || !Near(topRight.X, bottomRight.X, tolerance))
            throw Unsupported("Canvas projection must be axis-aligned and affine.");

        var left = Math.Max(0, Math.Max(topLeft.X, Math.Max(canvasViewport.X, cameraViewport.X)));
        var bottom = Math.Max(0, Math.Max(bottomLeft.Y, Math.Max(canvasViewport.Y, cameraViewport.Y)));
        var right = Math.Min(screenWidth, Math.Min(topRight.X, Math.Min(canvasViewport.X + canvasViewport.Width, cameraViewport.X + cameraViewport.Width)));
        var top = Math.Min(screenHeight, Math.Min(topLeft.Y, Math.Min(canvasViewport.Y + canvasViewport.Height, cameraViewport.Y + cameraViewport.Height)));
        if (right <= left || top <= bottom) throw Unsupported("Canvas has no visible intersection with its camera and physical screen.");
        var pixels = new UiRect(left, bottom, right - left, top - bottom);
        var unitsX = localRect.Width / projectedWidth;
        var unitsY = localRect.Height / projectedHeight;
        var rootX = (left - topLeft.X) * unitsX;
        var rootY = (topLeft.Y - top) * unitsY;
        var rootWidth = pixels.Width * unitsY;
        var rootHeight = pixels.Height * unitsY;
        var scaleX = unitsX / unitsY;
        if (new[] { unitsX, unitsY, rootWidth, rootHeight, scaleX }.Any(v => !float.IsFinite(v) || v <= 0) ||
            !float.IsFinite(rootX) || !float.IsFinite(rootY))
            throw Unsupported("Canvas unit compensation is not finite and positive.");
        return new(pixels, rootX, rootY, rootWidth, rootHeight, scaleX, unitsY);
    }
    private static bool Near(float a, float b, float tolerance) => Math.Abs(a - b) <= tolerance;
    private static void RequireRect(UiRect rect)
    {
        if (rect is null || new[] { rect.X, rect.Y, rect.Width, rect.Height, rect.X + rect.Width, rect.Y + rect.Height }.Any(v => !float.IsFinite(v)) || rect.Width <= 0 || rect.Height <= 0)
            throw Unsupported("Canvas and camera rectangles must be finite with positive extents.");
    }
    private static UnsupportedSignatureException Unsupported(string message) => new(message);
}
