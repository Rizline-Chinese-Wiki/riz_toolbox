using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class GameplayNativeBackend
{
    private GameplayControlAvailability CurrentSeekAvailability()
    {
        const string target = "gameplay.controller/Seek";
        try
        {
            if (_rateMetadataError is { } metadata)
                return new(GameplayControlAction.Seek, false, "The current gameplay source metadata is unavailable.",
                    new(ErrorCode.UnsupportedSignature, metadata, GameplayCapabilityIds.Controls, target));
            if (_rateProvider is not INativePlaybackSeekProvider provider)
                return new(GameplayControlAction.Seek, false, "The installed playback provider has no complete Seek transaction.",
                    new(ErrorCode.OperationUnsupported, "The installed playback provider has no complete Seek transaction.", GameplayCapabilityIds.Controls, target));
            var probe = provider is INativePlaybackAvailabilityObservation observation
                ? observation.ObserveSeekAvailability() : provider.ProbeSeek();
            return new(GameplayControlAction.Seek, probe.Supported, probe.Evidence,
                probe.Error is { } error ? error with { CapabilityId = GameplayCapabilityIds.Controls, TargetId = target } : null)
            {
                ValueUnits = probe.Supported
                    ? new[] { GameplayControlValueUnit.GameSeconds, GameplayControlValueUnit.ChartCoordinates }
                    : Array.Empty<GameplayControlValueUnit>()
            };
        }
        catch (Exception error)
        {
            return new(GameplayControlAction.Seek, false, "The current song cannot prepare a complete Seek transaction.",
                new(error is GameplayStateException state ? state.Code : ErrorCode.CapabilityUnavailable,
                    error.Message, GameplayCapabilityIds.Controls, target));
        }
    }

    private sealed partial class CaptureValue
    {
        private GameplayPreparedControl PrepareSeek(GameplayControlRequest request)
        {
            if (_owner._rateProvider is not INativePlaybackSeekProvider provider)
                throw new GameplayStateException(ErrorCode.OperationUnsupported, "This playback provider cannot prepare Seek.");
            var profile = RequireMapping(independent: true);
            var descriptor = _sourceIdentity?.NativeDescriptor ?? throw new GameplayStateException(ErrorCode.NotReady,
                "The current song has no prepared playback source.");
            var binding = descriptor.Binding;
            void VerifyCapture()
            {
                VerifyPlaybackCapture();
                if (!MatchesServiceContext(request) || request.ObservedRunId != binding.RunId
                    || binding.Controller != ControllerIdentity || binding.Information != Identity
                    || _sourceIdentity is not { } identity || binding.Progress != identity.Progress
                    || descriptor.Pause != identity.Pause || binding.Player != identity.Player
                    || binding.AudioSource != identity.AudioSource || binding.Clip != identity.Clip)
                    throw new GameplayStateException(ErrorCode.TargetExpired, "Seek targets a different run, generation or retained playback graph.");
                if (!LoadOver || !TimeReady || !Paused || Playing || LevelOver || Crashed || !CanDoOperation)
                    throw new GameplayStateException(ErrorCode.InvalidGameState, "Seek requires the current ready, paused, non-ending song.");
                profile.VerifyCurrent();
            }
            VerifyCapture();
            if (_owner.CaptureSourceDescriptor() != descriptor)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The playback source changed before Seek preparation.");
            var origin = new NativePlaybackRateOrigin(binding.RunId, binding.ControllerGeneration,
                binding.Controller, binding.Information, binding.Progress, descriptor.Pause);
            INativePlaybackSeekSource? source = null;
            NativeSeekProgressParticipant? progress = null;
            NativeSeekGameStatePreparation? gameState = null;
            try
            {
                source = provider.CaptureSeek(origin);
                var captured = source;
                var judgement = provider.CaptureSeekJudgementBackend(captured.Run);
                void VerifyOrigin()
                {
                    // A committed setter may temporarily have only its private
                    // continuation. Ordinary provider readback must not retire
                    // that writer before it can restore the original audio time.
                    VerifyCapture(); captured.VerifyAuthority(); judgement.VerifyCurrent();
                    if (captured.Binding != binding || !ReferenceEquals(provider.GameState.CaptureCurrentPlaybackRun(), captured.Run))
                        throw new GameplayStateException(ErrorCode.TargetExpired, "The private Seek source no longer belongs to this observer's current run.");
                    captured.Run.VerifyHostCurrent(); VerifyCapture();
                }
                VerifyOrigin();
                progress = NativeSeekProgressParticipant.Capture(_owner._api, profile, binding, descriptor.Pause, request,
                    VerifyOrigin, captured.VerifyAuthority);
                gameState = NativeSeekGameStatePreparation.Prepare(_owner._api, provider.GameState, source.Run, profile, progress.Coordinates, judgement);
                VerifyOrigin();
                // Composition takes every prepared owner on both success and
                // failure, preserving partial native cleanup responsibility.
                var retainedProgress = progress; var retainedGameState = gameState;
                source = null; progress = null; gameState = null;
                return NativePlaybackSeekParticipants.Prepare(request, captured, retainedProgress, retainedGameState, VerifyOrigin);
            }
            catch (Exception error)
            {
                foreach (var owner in new IDisposable?[] { gameState, progress, source })
                    try { owner?.Dispose(); }
                    catch (Exception cleanup)
                    {
                        error.Data["SeekPreparationCleanupOwner/" + owner!.GetType().Name] = owner;
                        error.Data["SeekPreparationCleanupFailure/" + owner.GetType().Name] = cleanup;
                    }
                throw;
            }
        }
    }
}
