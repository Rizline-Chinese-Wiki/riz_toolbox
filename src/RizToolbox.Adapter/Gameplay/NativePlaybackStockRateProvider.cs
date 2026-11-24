using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Formal backend provider for one actual registered stock session.
/// Separate factories retain their actual fixture or typed formal registration.
/// A formal source and clock do not grant production engine writer admission.</summary>
internal sealed class NativePlaybackStockRateProvider : INativePlaybackRateProvider,
    INativePlaybackGetTimeHookProvider, INativePlaybackRateProviderLifetime, INativePlaybackRateSourceDescriptorProvider,
    INativePlaybackClockReadProvider
{
    private const string Coverage = "Controlled stock native fixture only: actual common Dobby group, stock Pause/Replay/Resume and leaf completion, continuous fixture writer attempts, host-issued run association, independent seven-root API readback, unique final Clock and exact native GetTime lease. Synthetic IL2CPP metadata; unknown engine/lower writers, source generation and production first-admission remain unproven.";
    private const string FormalCoverage = "Experimental playback-rate control for the fixed game build. Preparation requires explicit experimental opt-in, the current paused run, its retained source authority and armed Clock, and the actual reserved pitch writer with independent API readback and transaction restoration. Actual game rate-control behavior and coverage of unknown engine memory writers remain unverified; this does not grant production admission or Seek.";
    private readonly NativePlaybackStockClockRouter.Session _session;
    private readonly bool _formalSource;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _disposed;
    private NativePlaybackStockRateProvider(NativePlaybackStockClockRouter.Session session, bool formalSource = false)
        => (_session, _formalSource) = (session, formalSource);
    internal static NativePlaybackStockRateProvider AdoptFormalSource(NativePlaybackStockClockRouter.Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var provider = new NativePlaybackStockRateProvider(session, true);
        session.RegisterFormalProvider(provider);
        return provider;
    }
    internal static NativePlaybackStockRateProvider AdoptControlledFixture(NativePlaybackStockClockRouter.Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var provider = new NativePlaybackStockRateProvider(session);
        session.RegisterControlledProvider(provider);
        return provider;
    }
    internal bool ProductionAdmission => false;
    private void VerifyThread()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new GameplayStateException(ErrorCode.WrongThread, "The concrete stock provider is owner-thread bound.");
    }
    private void VerifyCurrent()
    {
        VerifyThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NativePlaybackStockRateProvider));
        // This facade selects its exact registered provider. Each actual
        // operation reads its source/native policy on the session below.
        _session.VerifyControlledProviderIdentity(this);
    }
    public NativePlaybackRateProviderProbe Probe()
    {
        try
        {
            VerifyCurrent();
            _session.VerifyControlledProvider(this);
            return new(true, _formalSource ? FormalCoverage : Coverage);
        }
        catch (Exception error)
        {
            return new(false, _formalSource ? "The current song's playback support is unavailable." : Coverage + " Current support is withdrawn.",
                new(error is GameplayStateException state ? state.Code : ErrorCode.CapabilityUnavailable, error.Message));
        }
    }
    public INativePlaybackRateSource Capture(NativePlaybackRateOrigin origin)
    {
        VerifyCurrent();
        // The exact RateSource constructor owns this operation's admission;
        // nested facade checks grant no second writer or root responsibility.
        return _session.CaptureRateSource(origin);
    }
    internal INativePlaybackSeekSource CaptureSeek(NativePlaybackRateOrigin origin)
    {
        VerifyCurrent();
        if (!_formalSource)
            throw new GameplayStateException(ErrorCode.OperationUnsupported, "The historical fixture provider does not issue formal Seek sources.");
        return _session.CaptureSeekSource(origin);
    }
    public NativePlaybackRateSourceDescriptor CaptureSourceDescriptor()
    {
        VerifyCurrent();
        return _session.CaptureRateSourceDescriptor(this);
    }
    public NativePlaybackGetTimeHookLease CaptureGetTimeHook()
    {
        VerifyCurrent();
        return _session.CaptureProviderHook(this);
    }
    public NativePlaybackClockReadout ReadPlaybackClock(NativePlaybackRateOrigin origin)
    {
        VerifyCurrent();
        return _session.ReadControlledClock(this, origin);
    }
    public void VerifyPlaybackClock(NativePlaybackClockReadout readout)
    {
        VerifyCurrent();
        _session.VerifyControlledClock(this, readout);
    }
    public void VerifyPlaybackClockState(NativePlaybackClockReadout readout)
        => VerifyPlaybackClockStateForHost(null, readout);
    internal void VerifyPlaybackClockStateForHost(NativePlaybackStockClockRouter.RateProviderHost? host,
        NativePlaybackClockReadout readout)
    {
        VerifyThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NativePlaybackStockRateProvider));
        _session.VerifyControlledClockState(this, readout, host);
    }
    internal void PumpCompleted() { VerifyThread(); _session.PumpProvider(this); }
    void INativePlaybackRateProviderLifetime.PumpCompleted() => PumpCompleted();
    public void Dispose()
    {
        VerifyThread();
        // Reject worker/copy cleanup before changing the disposed flag, so the
        // actual owner can still retry and retain all in-flight resources.
        _session.DisposeProvider(this);
        _disposed = true;
    }
}
