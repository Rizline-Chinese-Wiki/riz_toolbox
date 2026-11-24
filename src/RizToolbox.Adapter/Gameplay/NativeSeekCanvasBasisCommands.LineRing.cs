namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasBasisCommands
{
    internal NativeSeekLineRingSourceCapture CaptureLineRingSource(ISeekLineRingSourceProfile profile)
    {
        CheckThread(); ArgumentNullException.ThrowIfNull(profile);
        return Operation("CaptureLineRingSource", () =>
        {
            return NativeSeekLineRingSourceCapture.Capture(lifetime => _basis!.CreateLineRingReader(profile,
                () => { Local(); lifetime(); }));
        });
    }
}
