using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Implemented by the actual registered provider and its stable host. Each
/// read independently proves source authority. Descriptors carry matching data only.</summary>
internal interface INativePlaybackClockReadProvider
{
    NativePlaybackClockReadout ReadPlaybackClock(NativePlaybackRateOrigin origin);
    void VerifyPlaybackClock(NativePlaybackClockReadout readout);
    void VerifyPlaybackClockState(NativePlaybackClockReadout readout);
}

internal sealed partial class NativePlaybackStockClockRouter
{
    internal sealed partial class Session
    {
        // The service can run capture getters after sampling. Finish that
        // window on the same native receipt and Clock policy without sampling
        // DSP/media again or recursively probing the provider.
        internal void VerifyControlledClockState(NativePlaybackStockRateProvider provider,
            NativePlaybackClockReadout readout, RateProviderHost? host = null)
        {
            VerifyControlledClockLocalState(provider, readout, host);
            var receipt = readout.SourceReceipt!;
            receipt.VerifyOperationIdentity();
            _ = readout.Clock.ReadPublished(readout.State, readout.DspSeconds);
            receipt.VerifyOperationIdentity();
            VerifyControlledClockLocalState(provider, readout, host);
        }

        private void VerifyControlledClockLocalState(NativePlaybackStockRateProvider provider,
            NativePlaybackClockReadout readout, RateProviderHost? host = null)
        {
            if (_owner._thread != Environment.CurrentManagedThreadId || _owner._disposed || !_armed || _withdrawn
                || !ReferenceEquals(_owner._registeredOwner, _owner) || !ReferenceEquals(_owner._session, this)
                || !ReferenceEquals(_rateProvider, provider) || !ReferenceEquals(readout.Issuer, provider)
                || !ReferenceEquals(_finalClock, readout.Clock) || readout.State.Binding != _initial.Binding
                || readout.SourceReceipt is null
                || host is not null && (!ReferenceEquals(_providerHost, host) || !ReferenceEquals(_owner._rateHost, host))
                || !readout.Clock.HasExactReadState(readout.State))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The local provider, session or immutable playback Clock ownership ended.");
            _owner._run.VerifyCurrent(_initial.Binding);
            if (!ReferenceEquals(_owner.ObserveCurrent(), readout.SourceReceipt))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The playback readout no longer belongs to the exact current source receipt.");
        }

        internal NativePlaybackClockReadout ReadControlledClock(NativePlaybackStockRateProvider provider,
            NativePlaybackRateOrigin origin)
        {
            VerifyControlledProviderIdentity(provider);
            if (origin != _owner._origin)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The playback read belongs to another observed run or graph.");
            var clock = Clock;
            var expected = clock.Capture();
            var receipt = _owner.ObserveCurrent();
            // Read the current registered source's seven retained roots. A typed
            // successor owns its own graph; the read never borrows its predecessor.
            var actual = receipt.Read().Sample;
            VerifyControlledProviderIdentity(provider);
            if (!ReferenceEquals(_owner.ObserveCurrent(), receipt) || !ReferenceEquals(clock, Clock)
                || actual.Binding != expected.Binding || expected.Binding != _initial.Binding)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The registered source or unique playback Clock changed during the read.");
            var result = clock.ReadPublished(expected, actual.DspSeconds) with { Issuer = provider, SourceReceipt = receipt };
            receipt.VerifyOperationIdentity();
            VerifyControlledClockLocalState(provider, result);
            return result;
        }

        internal void VerifyControlledClock(NativePlaybackStockRateProvider provider, NativePlaybackClockReadout readout)
        {
            VerifyControlledProviderIdentity(provider);
            VerifyControlledClockLocalState(provider, readout);
            var receipt = readout.SourceReceipt!;
            // The service may have invoked real capture getters after the
            // sample. Recheck the current graph/fences on this exact receipt;
            // this verifies authority without reading another DSP sample.
            receipt.VerifyCurrent();
            _ = readout.Clock.ReadPublished(readout.State, readout.DspSeconds);
            receipt.VerifyOperationIdentity();
            VerifyControlledClockLocalState(provider, readout);
        }
    }
}
