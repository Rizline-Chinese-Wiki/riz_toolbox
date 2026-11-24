using RizToolbox.Sdk;

namespace RizToolbox.BasicPlugins;

/// <summary>One explicitly started WAV in one child scope. A failed stop retains the scope and blocks replacement playback.</summary>
internal sealed class OwnedWavPlayback
{
    private readonly PluginRuntime _runtime;
    private IPluginScope? _scope;
    private IAudioAssetLease? _asset;
    private IAudioPlaybackLease? _playback;
    private bool _stopUnconfirmed, _startUnconfirmed;
    internal OwnedWavPlayback(PluginRuntime runtime) => _runtime = runtime;
    internal string State { get; private set; } = "Stopped. Playback starts only when you press Play WAV.";

    internal void Play(QuickPanelSettings settings)
    {
        if (!Stop()) return;
        var storage = _runtime.Service<IPluginStorageService>(_runtime.Context, StorageCapabilityIds.Storage, "wav-storage");
        if (storage is null) { State = "WAV storage is unavailable."; return; }
        // This preflight reports storage errors. The audio service enforces its own limit on the file it opens.
        Result<byte[]> bytes;
        try { bytes = storage.Read(PluginStorageArea.Data, settings.WavPath); }
        catch (Exception error)
        {
            _runtime.Fail("wav-file", PluginRuntime.Error(error, StorageCapabilityIds.Storage, settings.WavPath));
            State = "WAV file could not be read. See the reported storage error.";
            return;
        }
        if (!bytes.Success || bytes.Value is null)
        {
            _runtime.Fail("wav-file", bytes.Error, StorageCapabilityIds.Storage, settings.WavPath);
            State = "WAV file could not be read. Place your own PCM16 WAV in this plugin's Data storage.";
            return;
        }
        if (bytes.Value.Length < 44 || bytes.Value.Length > 1024 * 1024)
        {
            _runtime.Fail("wav-file", new(ErrorCode.ContentInvalid,
                "The selected WAV must be 44 bytes..1 MiB; decoding and PCM16 validation are performed by the audio service.",
                AudioContentCapabilityIds.Playback, settings.WavPath));
            State = "WAV file is empty, truncated or exceeds the 1 MiB basic-plugin limit.";
            return;
        }
        _runtime.Clear("wav-file");
        try { _scope = _runtime.Context.CreateScope("wav-playback"); }
        catch (Exception error)
        {
            _runtime.Fail("wav-scope", PluginRuntime.Error(error, AudioContentCapabilityIds.Playback, settings.WavPath));
            State = "A playback scope could not be created.";
            return;
        }
        _runtime.Clear("wav-scope");
        var audio = _runtime.Service<IAudioPlaybackService>(_scope, AudioContentCapabilityIds.Playback, "wav-service");
        if (audio is null) { CleanupAfterFailure("WAV playback service is unavailable."); return; }
        Result<IAudioAssetLease> loaded;
        try { loaded = audio.LoadWav(new(ContentFileArea.PluginData, settings.WavPath) { MaximumBytes = 1024 * 1024 }, _scope.Lifetime); }
        catch (Exception error)
        {
            _runtime.Fail("wav-load", PluginRuntime.Error(error, AudioContentCapabilityIds.Playback, settings.WavPath));
            CleanupAfterFailure("The audio service threw while loading this WAV.");
            return;
        }
        if (!loaded.Success || loaded.Value is null)
        {
            _runtime.Fail("wav-load", loaded.Error, AudioContentCapabilityIds.Playback, settings.WavPath);
            CleanupAfterFailure("The audio service could not load this WAV.");
            return;
        }
        _asset = loaded.Value;
        _runtime.Clear("wav-load");
        Result<IAudioPlaybackLease> played;
        try { played = audio.Play(_asset.Asset, settings.Volume, settings.Loop); }
        catch (Exception error)
        {
            // A throwing provider may have started a scope-owned source without returning its stop handle.
            // Retain the asset and scope for host cleanup rather than releasing the clip before an unknown source.
            _runtime.Fail("wav-play", PluginRuntime.Error(error, AudioContentCapabilityIds.Playback, settings.WavPath));
            _stopUnconfirmed = _startUnconfirmed = true;
            State = "Playback creation threw without a stop lease. Ownership is retained; replacement is blocked until host cleanup.";
            return;
        }
        if (played is null || played.Success && played.Value is null)
        {
            // A malformed success can hide a source already owned by this scope, just like a throwing start.
            _runtime.Fail("wav-play", new(ErrorCode.ServiceProviderFailed,
                "The audio provider did not return a stop lease. Playback creation cannot be confirmed.",
                AudioContentCapabilityIds.Playback, settings.WavPath));
            _stopUnconfirmed = _startUnconfirmed = true;
            State = "Playback creation returned no stop lease. Ownership is retained; replacement is blocked until host cleanup.";
            return;
        }
        if (!played.Success)
        {
            _runtime.Fail("wav-play", played.Error, AudioContentCapabilityIds.Playback, settings.WavPath);
            CleanupAfterFailure("The WAV loaded, but playback was refused.");
            return;
        }
        _playback = played.Value;
        _runtime.Clear("wav-play");
        State = $"Playing {PluginRuntime.Clip(settings.WavPath, 100)} · {_asset.Channels} channel(s), {_asset.Frequency} Hz.";
        _runtime.Context.Log.Log(LogLevel.Info, $"User WAV playback accepted: file={PluginRuntime.Clip(settings.WavPath, 256)}, " +
            $"frames={_asset.SampleFrames}, channels={_asset.Channels}, frequency={_asset.Frequency}, volume={settings.Volume}, loop={settings.Loop}.");
    }

    internal void Observe()
    {
        if (_scope is null) return;
        if (_startUnconfirmed) return;
        if (_stopUnconfirmed) { State = "Stop or cleanup is unconfirmed. Playback and asset ownership remain retained; use Stop WAV again."; return; }
        try
        {
            if (_scope.IsActive && _asset?.IsValid == true && _playback?.IsActive == true)
            { _runtime.Clear("wav-observation"); return; }
        }
        catch (Exception error)
        {
            _runtime.Fail("wav-observation", PluginRuntime.Error(error, AudioContentCapabilityIds.Playback));
            _stopUnconfirmed = true;
            State = "Playback status could not be read. Ownership is retained; use Stop WAV.";
            return;
        }
        if (Stop()) State = "Stopped after playback completion or provider withdrawal. Press Play WAV to start again.";
    }

    private void CleanupAfterFailure(string failedState)
    {
        if (Stop()) State = failedState;
    }

    internal bool Stop()
    {
        if (_scope is null) { _stopUnconfirmed = _startUnconfirmed = false; return true; }
        if (_startUnconfirmed)
        {
            _runtime.Fail("wav-stop", new(ErrorCode.ServiceProviderFailed,
                "Playback creation is unconfirmed without returning a stop lease. The host must withdraw its owned scope; this activation cannot confirm cleanup.",
                AudioContentCapabilityIds.Playback));
            State = "Playback creation is unconfirmed. WAV ownership remains retained and replacement is blocked; disable the plugin to request host cleanup.";
            return false;
        }
        if (_playback is not null)
        {
            Result stopped;
            try { stopped = _playback.Stop(); }
            catch (Exception error)
            {
                _runtime.Fail("wav-stop", PluginRuntime.Error(error, AudioContentCapabilityIds.Playback));
                _stopUnconfirmed = true;
                State = "Stop threw; playback and its WAV remain owned. No replacement playback will start.";
                return false;
            }
            if (!stopped.Success)
            {
                _runtime.Fail("wav-stop", stopped.Error, AudioContentCapabilityIds.Playback);
                _stopUnconfirmed = true;
                State = "Stop failed; playback and its WAV remain owned. No replacement playback will start.";
                return false;
            }
            if (!_runtime.Release(_playback, "wav-playback-release"))
            {
                _stopUnconfirmed = true;
                State = "Playback cleanup is unconfirmed; WAV ownership remains retained.";
                return false;
            }
            _playback = null;
            _runtime.Clear("wav-stop");
        }
        if (_asset is not null)
        {
            if (!_runtime.Release(_asset, "wav-asset-release"))
            {
                _stopUnconfirmed = true;
                State = "WAV asset release is unconfirmed. The playback scope is retained.";
                return false;
            }
            _asset = null;
        }
        if (!_runtime.Release(_scope, "wav-scope-release"))
        {
            _stopUnconfirmed = true;
            State = "WAV scope cleanup is unconfirmed.";
            return false;
        }
        _scope = null;
        _stopUnconfirmed = _startUnconfirmed = false;
        _runtime.Clear("wav-stop");
        _runtime.Clear("wav-observation");
        State = "Stopped. Playback starts only when you press Play WAV.";
        _runtime.Context.Log.Log(LogLevel.Info, "User WAV playback stopped and its asset/scope leases released.");
        return true;
    }
}
