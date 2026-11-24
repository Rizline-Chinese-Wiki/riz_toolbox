using System.Globalization;
using RizToolbox.Sdk;

namespace RizToolbox.BasicPlugins;

/// <summary>F8 toggles one owned window. User WAV loading/playback is explicit and independent of window visibility.</summary>
public sealed class QuickPanelPlugin : IRizPlugin
{
    private PluginRuntime? _runtime;
    private QuickPanelSettings _settings = new();
    private bool _configurationReady, _visible;
    private ScopedWindow? _window;
    private IUiControlHandle? _status, _pathField, _volumeField, _loopButton, _hideButton;
    private IInputActionHandle? _hotkey;
    private OwnedWavPlayback? _audio;
    private double _nextWindowAttempt, _nextHotkeyAttempt;
    private string? _lastStatus;

    public PluginManifest Manifest => PluginManifests.QuickPanel;

    public void Enable(IPluginContext context)
    {
        Disable();
        var runtime = _runtime = new PluginRuntime(context);
        _settings = new();
        _configurationReady = false;
        _visible = true;
        _nextWindowAttempt = _nextHotkeyAttempt = 0;
        _audio = new(runtime);
        runtime.Run("configuration-load", ReloadSettings);
        runtime.Start(Pulse, OnSceneChanged, Cleanup);
    }

    private void Pulse()
    {
        var runtime = _runtime!;
        if (!runtime.HasScene) return;
        EnsureHotkey();
        _audio!.Observe();
        if (_window is not null && !_window.IsAlive) ReleaseWindow();
        // Keep the controls reachable whenever F8 cannot reopen the panel.
        if (!_visible && _hotkey is not null) { ReleaseWindow(); return; }
        if (_window is null && runtime.Now >= _nextWindowAttempt)
        {
            _nextWindowAttempt = runtime.Now + 5;
            CreateWindow();
        }
        Render();
    }

    private void EnsureHotkey()
    {
        var runtime = _runtime!;
        var capability = runtime.Context.GetCapability(UiInputCapabilityIds.Actions);
        if (!capability.Available)
        {
            ReleaseHotkey();
            runtime.Fail("hotkey", new(ErrorCode.CapabilityUnavailable,
                capability.UnavailableReason ?? "Keyboard actions are unavailable.", UiInputCapabilityIds.Actions, "F8", true));
            return;
        }
        if (_hotkey is not null || runtime.Now < _nextHotkeyAttempt) return;
        _nextHotkeyAttempt = runtime.Now + 5;
        var actions = runtime.Service<IInputActionService>(runtime.Context, UiInputCapabilityIds.Actions, "input-service");
        if (actions is null) return;
        var registered = actions.Register(new("toggle-panel", new(InputDeviceKind.Keyboard, "F8")),
            _ => runtime.Run("hotkey-toggle", () => SetVisible(!_visible)));
        if (!registered.Success || registered.Value is null)
        {
            runtime.Fail("hotkey", registered.Error, UiInputCapabilityIds.Actions, "F8");
            return;
        }
        _hotkey = registered.Value;
        runtime.Clear("hotkey");
        runtime.Context.Log.Log(LogLevel.Info, "F8 registered for this plugin's panel; binding conflicts and text focus follow the host input broker.");
    }

    private void CreateWindow()
    {
        var runtime = _runtime!;
        var window = ScopedWindow.Create(runtime, "quick-panel", "Quick panel · F8", new(1000, 90, 640, 575));
        if (window is null) return;
        try
        {
            var ui = window.Handle;
            _status = Label(ui, "status", "", new(15, 50, 610, 180));
            Label(ui, "path-label", "Your WAV in plugin Data storage:", new(15, 238, 610, 25));
            _pathField = PluginRuntime.Need(ui.AddTextField(new("wav-path", _settings.WavPath, new(15, 272, 610, 42)),
                characterLimit: 256), UiInputCapabilityIds.Windows, "wav-path");
            Label(ui, "volume-label", "Volume [0,1]:", new(15, 328, 165, 35));
            _volumeField = PluginRuntime.Need(ui.AddTextField(new("volume", _settings.Volume.ToString("0.###", CultureInfo.InvariantCulture),
                new(185, 328, 140, 38)), characterLimit: 16), UiInputCapabilityIds.Windows, "volume");
            _loopButton = Button(ui, "loop", LoopText(), new(345, 328, 135, 38), ToggleLoop);
            Button(ui, "save", "Save WAV settings", new(15, 390, 270, 45), SaveSettings);
            Button(ui, "reload", "Reload settings", new(300, 390, 210, 45), ReloadSettingsAndWindow);
            Button(ui, "play", "Play WAV", new(15, 455, 185, 45), Play);
            Button(ui, "stop", "Stop WAV", new(215, 455, 185, 45), () => _audio!.Stop());
            _hideButton = Button(ui, "hide", HideText(), new(415, 455, 210, 45), () => SetVisible(false));
            _window = window;
            _lastStatus = null;
            runtime.Clear("window-controls");
        }
        catch (Exception error)
        {
            runtime.Fail("window-controls", PluginRuntime.Error(error, UiInputCapabilityIds.Windows));
            runtime.Release(window, "window-release");
            _status = _pathField = _volumeField = _loopButton = _hideButton = null;
        }
    }

    private IUiControlHandle Label(IUiWindowHandle window, string id, string text, UiRect rectangle)
        => PluginRuntime.Need(window.AddLabel(new(id, text, rectangle)), UiInputCapabilityIds.Windows, id);

    private IUiControlHandle Button(IUiWindowHandle window, string id, string text, UiRect rectangle, Action action)
        => PluginRuntime.Need(window.AddButton(new(id, text, rectangle),
            () => _runtime?.Run("button-" + id, action)), UiInputCapabilityIds.Windows, id);

    private void Play()
    {
        if (!_configurationReady) { ConfigurationUnavailable(); return; }
        _audio!.Play(_settings);
    }

    private void SaveSettings()
    {
        if (!_configurationReady) { ConfigurationUnavailable(); return; }
        if (_pathField is null || _volumeField is null) return;
        var path = _pathField.GetText();
        var volume = _volumeField.GetText();
        if (!path.Success || path.Value is null) { _runtime!.Fail("wav-settings", path.Error, UiInputCapabilityIds.Windows); return; }
        if (!volume.Success || !float.TryParse(volume.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            _runtime!.Fail("wav-settings", volume.Error ?? new(ErrorCode.InvalidArgument, "Volume must use a number from 0 to 1, with '.' as the decimal separator."));
            return;
        }
        var next = _settings with { WavPath = path.Value.Trim(), Volume = amount, Visible = _visible };
        if (!_runtime!.Save("quick-panel", next, SettingsValidation.QuickPanel)) return;
        _settings = next;
        _runtime.Clear("wav-settings");
    }

    private void ToggleLoop()
    {
        if (!_configurationReady) { ConfigurationUnavailable(); return; }
        var next = _settings with { Loop = !_settings.Loop, Visible = _visible };
        if (!_runtime!.Save("quick-panel", next, SettingsValidation.QuickPanel)) return;
        _settings = next;
        var changed = _loopButton?.SetText(LoopText());
        if (changed is { Success: false }) _runtime.Fail("loop-label", changed.Error, UiInputCapabilityIds.Windows);
        else _runtime.Clear("loop-label");
    }

    private string LoopText() => _settings.Loop ? "Loop: on" : "Loop: off";
    private string HideText() => _hotkey is null ? "Hide unavailable" : "Hide · F8";

    private void SetVisible(bool visible)
    {
        if (!visible && _hotkey is null) return;
        // A bad settings file remains untouched; the panel can still be shown to explain the failure.
        if (_configurationReady)
        {
            var next = _settings with { Visible = visible };
            if (_runtime!.Save("quick-panel", next, SettingsValidation.QuickPanel)) _settings = next;
        }
        _visible = visible;
        if (!visible) ReleaseWindow();
        else _nextWindowAttempt = _runtime!.Now + 1;
    }

    private void ReloadSettings()
    {
        _configurationReady = _runtime!.Load("quick-panel", () => new QuickPanelSettings(), SettingsValidation.QuickPanel, out var settings);
        if (!_configurationReady || settings is null) return;
        _settings = settings;
        _visible = settings.Visible;
    }

    private void ReloadSettingsAndWindow()
    {
        ReloadSettings();
        if (_configurationReady) { ReleaseWindow(); _nextWindowAttempt = _runtime!.Now + 1; }
    }

    private void ConfigurationUnavailable()
        => _runtime!.Fail("configuration", new(ErrorCode.ConfigInvalid,
            "Reload valid WAV settings before playback or saving. The existing settings file is preserved."));

    private void Render()
    {
        if (_window?.IsAlive != true || _status is null || _hideButton is null) return;
        var runtime = _runtime!;
        var text = $"Scene {PluginRuntime.Clip(runtime.Scene, 50)} · generation {runtime.SceneGeneration}\n" +
            $"F8: {(_hotkey is null ? "unavailable; see error" : "toggle panel")}\n" +
            _audio!.State + "\nWAV playback follows the saved path, volume and loop settings." + runtime.IssuesText();
        if (text == _lastStatus) return;
        var control = _status;
        var updated = control.SetText(text);
        if (updated.Success)
        {
            control = _hideButton;
            updated = control.SetText(HideText());
        }
        if (updated.Success) { _lastStatus = text; runtime.Clear("panel-text"); }
        else
        {
            runtime.Fail("panel-text", updated.Error, UiInputCapabilityIds.Windows, control.Id);
            ReleaseWindow();
            _nextWindowAttempt = runtime.Now + 5;
        }
    }

    private void OnSceneChanged()
    {
        ReleaseWindow();
        ReleaseHotkey();
        _audio?.Stop();
        _nextWindowAttempt = _nextHotkeyAttempt = _runtime!.Now + 1;
    }

    private void ReleaseWindow()
    {
        _runtime?.Release(_window, "window-release");
        _window = null;
        _status = _pathField = _volumeField = _loopButton = _hideButton = null;
        _lastStatus = null;
    }

    private void ReleaseHotkey()
    {
        _runtime?.Release(_hotkey, "hotkey-release");
        _hotkey = null;
    }

    private void Cleanup()
    {
        ReleaseWindow();
        ReleaseHotkey();
        _audio?.Stop();
    }

    public void Disable()
    {
        _runtime?.Dispose();
        _runtime = null;
        _audio = null;
    }
}
