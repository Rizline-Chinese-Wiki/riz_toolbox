using RizToolbox.Sdk;

namespace RizToolbox.Core.Input;

/// <summary>Uniform fit with centered letterboxing. All methods are pure and accept only finite, positive extents.</summary>
public static class UiCoordinates
{
    public static bool Valid(UiRect? rect) => rect is not null && new[] { rect.X, rect.Y, rect.Width, rect.Height }.All(float.IsFinite) && rect.Width > 0 && rect.Height > 0;
    public static bool Valid(UiLayout? layout) => layout is not null && Valid(layout.Rect) && float.IsFinite(layout.ReferenceWidth) && float.IsFinite(layout.ReferenceHeight)
        && layout.ReferenceWidth is >= 64 and <= 16384 && layout.ReferenceHeight is >= 64 and <= 16384 && layout.Rect.X >= 0 && layout.Rect.Y >= 0
        && layout.Rect.X + layout.Rect.Width <= layout.ReferenceWidth && layout.Rect.Y + layout.Rect.Height <= layout.ReferenceHeight;
    public static UiViewport Fit(float screenWidth, float screenHeight, float? dpi, UiRect pixelRect, UiLayout layout)
    {
        if (!Valid(layout) || !Valid(pixelRect) || !float.IsFinite(screenWidth) || !float.IsFinite(screenHeight) || screenWidth <= 0 || screenHeight <= 0)
            throw new ArgumentException("Viewport and reference extents must be finite and positive.");
        if (pixelRect.X < 0 || pixelRect.Y < 0 || pixelRect.X + pixelRect.Width > screenWidth + 1 || pixelRect.Y + pixelRect.Height > screenHeight + 1)
            throw new ArgumentException("Pixel viewport must lie inside the current screen.");
        var scale = Math.Min(pixelRect.Width / layout.ReferenceWidth, pixelRect.Height / layout.ReferenceHeight);
        return new(screenWidth, screenHeight, dpi is > 0 && float.IsFinite(dpi.Value) ? dpi : null,
            pixelRect.X, pixelRect.Y, pixelRect.Width, pixelRect.Height, layout.ReferenceWidth, layout.ReferenceHeight, scale,
            (pixelRect.Width - layout.ReferenceWidth * scale) / 2, (pixelRect.Height - layout.ReferenceHeight * scale) / 2);
    }
    public static Result<UiPoint> ScreenToUi(UiViewport view, UiPoint point)
    {
        if (!ValidView(view) || point is null || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return Invalid();
        var x = (point.X - view.PixelX - view.LetterboxX) / view.PixelsPerReferenceUnit;
        var y = (view.PixelY + view.PixelHeight - view.LetterboxY - point.Y) / view.PixelsPerReferenceUnit;
        return x < 0 || y < 0 || x > view.ReferenceWidth || y > view.ReferenceHeight ? Outside() : Result<UiPoint>.Ok(new(x, y));
    }
    public static Result<UiPoint> UiToScreen(UiViewport view, UiPoint point)
    {
        if (!ValidView(view) || point is null || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return Invalid();
        return point.X < 0 || point.Y < 0 || point.X > view.ReferenceWidth || point.Y > view.ReferenceHeight ? Outside()
            : Result<UiPoint>.Ok(new(view.PixelX + view.LetterboxX + point.X * view.PixelsPerReferenceUnit,
                view.PixelY + view.PixelHeight - view.LetterboxY - point.Y * view.PixelsPerReferenceUnit));
    }
    private static Result<UiPoint> Invalid() => Result<UiPoint>.Fail(new(ErrorCode.InvalidArgument, "Point must be finite."));
    private static bool ValidView(UiViewport? view) => view is not null && new[] { view.PixelX, view.PixelY, view.PixelWidth, view.PixelHeight, view.ReferenceWidth, view.ReferenceHeight,
        view.PixelsPerReferenceUnit, view.LetterboxX, view.LetterboxY }.All(float.IsFinite) && view.PixelWidth > 0 && view.PixelHeight > 0 && view.ReferenceWidth > 0 && view.ReferenceHeight > 0 && view.PixelsPerReferenceUnit > 0;
    private static Result<UiPoint> Outside() => Result<UiPoint>.Fail(new(ErrorCode.InvalidArgument, "Point lies outside the fitted reference area (letterbox excluded)."));
}
