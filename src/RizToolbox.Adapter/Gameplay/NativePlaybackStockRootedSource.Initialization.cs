using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockRootedSource
{
    internal object InitializationWatchOwner => _freshAdoption?.InitializationWatchOwner(this) ?? this;

    internal void VerifyFirstHostRootSuccession(NativePlaybackInitializationHost host, PlaybackSourceGroupLease group)
    {
        Open();
        if (_initializationCapture is not null || !ReferenceEquals(_group, group) || _rootsReleased
            || _binding.Controller != host.Origin.Controller || _binding.Information != host.Origin.Information
            || _binding.Progress != host.Origin.Progress || _origin.Pause != host.Origin.Pause)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The exact seven-root first source has not succeeded its retained host roots.");
        VerifyGraph();
    }
    internal static NativePlaybackStockRootedSource CaptureInitialized(NativeRuntime api, NativePlaybackBinding binding,
        NativePlaybackRateOrigin origin, GameEventState.PlaybackRunAssociation run,
        PlaybackSourceGroupLease group, NativePlaybackInitializationTracker.CapturePermit permit,
        Action<ReadbackReceipt>? returned, Action<string>? withdrawn)
        => new(api, binding, origin, run, group, permit ?? throw new ArgumentNullException(nameof(permit)), returned, withdrawn);

    private void VerifyCaptureAdmission()
    {
        if (_initializationCapture is { } permit) permit.Verify(this, _group, _binding);
        else _group.RequireSourceOperationAdmission();
    }

    internal void CompleteInitializationCapture(NativePlaybackInitializationTracker.CapturePermit permit)
    {
        Open();
        if (!ReferenceEquals(_initializationCapture, permit))
            throw new GameplayStateException(ErrorCode.TargetExpired, "Only this source's exact conditional initializer capture can open.");
        permit.CompleteSourceOpen(this, _group, _binding);
        _initializationCapture = null;
        VerifyGraph();
    }

    internal void WithdrawForInitialization()
    {
        VerifyThread();
        if (_disposed) return;
        Fail(new GameplayStateException(ErrorCode.TargetExpired, "The actual Start initializer ended the predecessor source before its first store."));
    }

    internal void WithdrawForPlayerConstructor()
    {
        VerifyThread();
        if (_disposed) return;
        Fail(new GameplayStateException(ErrorCode.TargetExpired,
            "The registered player's shared constructor ended its old source before the reference store."));
    }

    internal void WithdrawForDirectAudio()
    {
        VerifyThread();
        if (_disposed) return;
        Fail(new GameplayStateException(ErrorCode.TargetExpired,
            "The exact AudioSource entered a direct playback or loop attempt before its original."));
    }

    internal void VerifyPlayerWatchActivated()
    {
        Open();
        if (!_group.HasPlayerConstructor) return; // the historical eleven-member owned fixtures
        if (!_watchStaged) throw new GameplayStateException(ErrorCode.TargetExpired, "This source has no exact rooted player watch.");
        _group.VerifyOperationActivePlayerWatch(this);
    }
}
