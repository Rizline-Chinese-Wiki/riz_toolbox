namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalRunTerminationBoundaryState
{
    internal int ReadFrameForAwake()
    {
        _main.VerifyAccess();
        if (!IsReadyForHost(_host.Sessions)) throw new InvalidOperationException("Awake frame sampling lost the actual native boundary owner.");
        return _backend.Frame();
    }
}
