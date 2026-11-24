using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Fixed provider capability, not admission of a particular run. A provider must withdraw
/// support when its return/schedule/pitch-writer transport is withdrawn or no longer complete.</summary>
internal sealed record NativePlaybackRateProviderProbe(bool Supported, string Evidence, OperationError? Error = null);

/// <summary>The current rooted backend capture. Run and generation come from the service request;
/// the provider independently verifies their current scene/source association before issuing access.</summary>
internal sealed record NativePlaybackRateOrigin(string RunId, long ControllerGeneration,
    long Controller, long Information, long Progress, long Pause);

/// <summary>No native implementation is implicitly installed. A provider needs the exact rooted
/// return authority, all stock Pause/Replay/Resume occurrences and continuous pitch-writer coverage,
/// including commands outside this service. It must preserve/verify the selected pitch at schedules
/// or withdraw and retain unknown-side-effect responsibility. Probe never substitutes for these facts.</summary>
internal interface INativePlaybackRateProvider
{
    NativePlaybackRateProviderProbe Probe();
    INativePlaybackRateSource Capture(NativePlaybackRateOrigin origin);
}

/// <summary>Read-only identity of a currently verified native source. Its native
/// controller generation and source epoch are not service generations. This
/// descriptor is not a source, writer receipt, or admission authority.</summary>
internal sealed record NativePlaybackRateSourceDescriptor(NativePlaybackBinding Binding, long Pause);

internal interface INativePlaybackRateSourceDescriptorProvider
{
    NativePlaybackRateSourceDescriptor? CaptureSourceDescriptor();
}

/// <summary>Observation of a registered source's immutable identity only.
/// Availability is checked against the current host/run/session, not by
/// issuing audio access. A descriptor never authorizes a Clock read or write;
/// Capture/Execute and their participants retain fresh physical validation.</summary>
internal interface INativePlaybackRateSourceDescriptorObservation
{
    NativePlaybackRateSourceDescriptor? ObserveSourceDescriptor();
}

/// <summary>Request-entry availability only. Observation creates no source,
/// Clock readout, write participant or judgement access. Value preparation
/// continues to use the provider's full Probe/Capture protocol independently.</summary>
internal interface INativePlaybackAvailabilityObservation
{
    NativePlaybackRateProviderProbe ObserveRateAvailability();
    NativePlaybackRateProviderProbe ObserveSeekAvailability();
}

/// <summary>One independently owned rooted source. Ownership transfers to the backend even when
/// preparation fails; Dispose releases preparation resources only. VerifyAuthority proves the exact
/// scene/run/source and complete active transports, rather than comparing current scalar values.</summary>
internal interface INativePlaybackRateSource : INativePlaybackRateAccess
{
    NativePlaybackBinding Binding { get; }
    NativePlaybackClock Clock { get; }
}

// Pure owner/busy checks only. Wrappers must forward a known release refusal
// before consuming their once-only cleanup state. This grants no read, writer
// or source authority and does not retry cleanup after an actual attempt.
internal interface INativePlaybackReleaseGuard
{
    void VerifyReleaseAllowed();
}

/// <summary>Backend adoption of a provider-owned exact source. This adds capture/source checks to
/// the existing actual audio+clock participants and supplies no production-admission shortcut.</summary>
internal static class NativePlaybackRateProviderAdoption
{
    internal static GameplayControlAvailability Availability(INativePlaybackRateProvider? provider, bool observationOnly = false)
    {
        const string target = "gameplay.controller/SetSpeed";
        const string unavailable = "No complete native playback return, schedule and continuous pitch-writer provider is installed.";
        if (provider is null) return new(GameplayControlAction.SetSpeed, false, unavailable,
            new(ErrorCode.OperationUnsupported, unavailable, GameplayCapabilityIds.Controls, target));
        try
        {
            var probe = (observationOnly && provider is INativePlaybackAvailabilityObservation observation
                ? observation.ObserveRateAvailability() : provider.Probe())
                ?? throw new InvalidOperationException("The playback-rate provider returned no capability description.");
            if (string.IsNullOrWhiteSpace(probe.Evidence)) throw new InvalidOperationException("The playback-rate provider has no capability evidence.");
            if (!probe.Supported || probe.Error is not null)
                return new(GameplayControlAction.SetSpeed, false, probe.Evidence,
                    (probe.Error ?? new(ErrorCode.CapabilityUnavailable, probe.Evidence)) with
                    { CapabilityId = GameplayCapabilityIds.Controls, TargetId = target });
            return new(GameplayControlAction.SetSpeed, true, probe.Evidence + " Current paused run/source authority is verified separately during preparation.")
                { ValueUnits = Array.AsReadOnly(new[] { GameplayControlValueUnit.PlaybackRateMultiplier }) };
        }
        catch (Exception error)
        {
            return new(GameplayControlAction.SetSpeed, false, "The playback-rate provider capability is unavailable.",
                new(ErrorCode.CapabilityUnavailable, error.Message, GameplayCapabilityIds.Controls, target));
        }
    }

    internal static GameplayPreparedControl Prepare(INativePlaybackRateProvider provider,
        NativePlaybackRateOrigin origin, GameplayControlRequest request, Action verifyCapture)
    {
        ArgumentNullException.ThrowIfNull(provider); ArgumentNullException.ThrowIfNull(verifyCapture);
        if (request.Action != GameplayControlAction.SetSpeed || request.Unit != GameplayControlValueUnit.PlaybackRateMultiplier
            || request.Value is not double value || !NativePlaybackClock.ValidRate(value)
            || string.IsNullOrWhiteSpace(request.ObservedRunId) || request.ControllerGeneration <= 0
            || origin.RunId != request.ObservedRunId || origin.ControllerGeneration != request.ControllerGeneration)
            throw new GameplayStateException(ErrorCode.InvalidArgument, "Provider adoption requires an explicit positive finite Single playback rate for the exact observed run/generation.");
        verifyCapture();
        RequireSupported(provider);
        var source = provider.Capture(origin) ?? throw new GameplayStateException(ErrorCode.NotReady, "The playback-rate provider supplied no current exact source.");
        var bound = new BoundSource(source, origin, verifyCapture);
        try
        {
            var clock = bound.Bind();
            return NativePlaybackRateParticipants.Prepare(request, clock, () => bound, bound.VerifyAuthority);
        }
        catch
        {
            // The participants also own this wrapper once captured. Its Dispose is once-only,
            // including failure before their capture factory is entered. Preserve preparation failure.
            try { bound.Dispose(); } catch { }
            throw;
        }
    }

    private static void RequireSupported(INativePlaybackRateProvider provider)
    {
        var available = Availability(provider);
        if (!available.Supported) throw new GameplayStateException(available.Error?.KnownCode ?? ErrorCode.CapabilityUnavailable,
            available.Error?.Message ?? available.Evidence);
    }

    private sealed class BoundSource : INativePlaybackRateAccess, INativePlaybackReleaseGuard
    {
        private readonly INativePlaybackRateSource _source;
        private readonly NativePlaybackRateOrigin _origin;
        private readonly Action _verifyCapture;
        private readonly BoundSource _registered;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private NativePlaybackBinding? _binding;
        private NativePlaybackClock? _clock;
        private bool _disposed;
        internal BoundSource(INativePlaybackRateSource source, NativePlaybackRateOrigin origin, Action verifyCapture)
            => (_source, _origin, _verifyCapture, _registered) = (source, origin, verifyCapture, this);

        internal NativePlaybackClock Bind()
        {
            Open();
            _binding = _source.Binding; _clock = _source.Clock;
            VerifyAuthority(); return _clock;
        }

        public void VerifyAuthority()
        {
            Open();
            // The captured source proves the live operation authority. Global
            // capability probing at every wrapper boundary repeats admission.
            _verifyCapture(); _source.VerifyAuthority();
            if (_binding is null || !_binding.Valid || _binding.RunId != _origin.RunId
                || _binding.ControllerGeneration != _origin.ControllerGeneration
                || _binding.Controller != _origin.Controller || _binding.Information != _origin.Information
                || _binding.Progress != _origin.Progress || _source.Binding != _binding
                || !ReferenceEquals(_source.Clock, _clock) || _clock?.Capture().Binding != _binding)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The provider's exact clock/source/run graph differs from this rooted backend capture.");
            _verifyCapture();
            Open();
        }

        public NativePlaybackRateSample Read()
        {
            VerifyAuthority(); var sample = _source.Read(); VerifyAuthority();
            if (sample.Binding != _binding)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The provider sample belongs to another exact source generation or run.");
            return sample;
        }

        public INativePlaybackPitchWrite PreparePitchWrite()
        {
            VerifyAuthority();
            var write = _source.PreparePitchWrite() ?? throw new GameplayStateException(ErrorCode.NotReady, "The source supplied no continuous pitch-writer receipt.");
            try
            {
                VerifyAuthority();
                return write is INativePlaybackPitchRecovery recovery
                    ? new BoundRecoverableWrite(this, write, recovery) : new BoundWrite(this, write);
            }
            catch { try { write.Dispose(); } catch { } throw; }
        }

        public void VerifyReleaseAllowed()
        {
            VerifyOwner();
            if (_disposed) return;
            if (_source is INativePlaybackReleaseGuard guard) guard.VerifyReleaseAllowed();
        }

        public void Dispose()
        {
            // Cleanup uses this wrapper's retained ownership, independent of
            // current source admission. Reject worker/copy calls before taking
            // the once-only attempt from the actual owner.
            VerifyReleaseAllowed();
            if (_disposed) return;
            _disposed = true;
            _source.Dispose();
        }

        private void Open()
        {
            VerifyOwner();
            if (_disposed) throw new ObjectDisposedException(nameof(BoundSource));
        }

        private void VerifyOwner()
        {
            if (_thread != Environment.CurrentManagedThreadId)
                throw new GameplayStateException(ErrorCode.WrongThread, "The adopted playback source belongs to its owner thread.");
            if (!ReferenceEquals(_registered, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied source wrapper owns no preparation or cleanup responsibility.");
        }

        private class BoundWrite : INativePlaybackPitchWrite, INativePlaybackReleaseGuard
        {
            private readonly BoundSource _source; private readonly INativePlaybackPitchWrite _write;
            private readonly BoundWrite _registered;
            private bool _disposed;
            internal BoundWrite(BoundSource source, INativePlaybackPitchWrite write)
                => (_source, _write, _registered) = (source, write, this);
            public void VerifyCurrent()
            {
                VerifyOwner();
                if (_disposed) throw new ObjectDisposedException(nameof(BoundWrite));
                _source.VerifyAuthority(); _write.VerifyCurrent(); _source.VerifyAuthority();
                if (_disposed) throw new ObjectDisposedException(nameof(BoundWrite));
            }
            public void Write(float pitch) { VerifyCurrent(); _write.Write(pitch); _source.VerifyAuthority(); if (_disposed) throw new ObjectDisposedException(nameof(BoundWrite)); }
            public void VerifyReleaseAllowed()
            {
                VerifyOwner();
                if (_disposed) return;
                if (_write is INativePlaybackReleaseGuard guard) guard.VerifyReleaseAllowed();
            }
            public void Dispose() { VerifyReleaseAllowed(); if (_disposed) return; _disposed = true; _write.Dispose(); }
            private void VerifyOwner()
            {
                _source.VerifyOwner();
                if (!ReferenceEquals(_registered, this))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "A copied writer wrapper owns no preparation or cleanup responsibility.");
            }
        }

        // Only the actual writer's recovery capability is forwarded. Providers
        // that retain attempt fences without a completed continuation continue
        // to use the ordinary participant read-before-restore path.
        private sealed class BoundRecoverableWrite : BoundWrite, INativePlaybackPitchRecovery
        {
            private readonly INativePlaybackPitchRecovery _recovery;
            internal BoundRecoverableWrite(BoundSource source, INativePlaybackPitchWrite write,
                INativePlaybackPitchRecovery recovery) : base(source, write) => _recovery = recovery;
            public void RestoreOriginal()
            {
                VerifyCurrent(); _recovery.RestoreOriginal(); VerifyCurrent();
            }
        }
    }
}
