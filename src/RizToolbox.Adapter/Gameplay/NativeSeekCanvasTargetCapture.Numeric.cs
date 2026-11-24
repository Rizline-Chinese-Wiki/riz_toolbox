namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasTargetCapture
{
    /// <summary>Copies the already initialized table and exact Canvas slots
    /// during the original capture's active shared source operation. The copy
    /// itself owns no native handle and can outlive this capture.</summary>
    internal SeekCanvasNumericBasis CopyNumericBasis() => Operation(() =>
    {
        CompareSource(); Local();
        var result = SeekCanvasNumericBasis.Copy(this, Plan, _baseline, Local);
        CompareSource(); Local(); return result;
    });
}
