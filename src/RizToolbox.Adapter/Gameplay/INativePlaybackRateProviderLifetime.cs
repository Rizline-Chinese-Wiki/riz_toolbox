namespace RizToolbox.Adapter.Gameplay;

/// <summary>Adapter-owned logical lifetime, independent of the retained physical
/// hook group. Pump and cleanup must run on the same owner thread as capture.</summary>
internal interface INativePlaybackRateProviderLifetime : IDisposable
{
    void PumpCompleted();
}
