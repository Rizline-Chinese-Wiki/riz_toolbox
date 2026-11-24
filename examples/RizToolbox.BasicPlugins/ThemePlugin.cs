using RizToolbox.Sdk;

namespace RizToolbox.BasicPlugins;

/// <summary>Priority color layers owned by one scene scope. Removing the scope delegates baseline restoration to the host.</summary>
public sealed class ThemePlugin : IRizPlugin
{
    private const int MaximumThemeTargets = 16;
    private PluginRuntime? _runtime;
    private ThemeSettings _settings = new();
    private IPluginScope? _themeScope;
    private readonly Dictionary<string, IResourceOverride> _applied = new(StringComparer.Ordinal);
    private string[] _observedKeys = Array.Empty<string>();
    private bool _configurationReady, _cleanupUnconfirmed;
    private double _nextTargetRead;
    private string? _lastWaiting;

    public PluginManifest Manifest => PluginManifests.Theme;

    public void Enable(IPluginContext context)
    {
        Disable();
        var runtime = _runtime = new PluginRuntime(context);
        _settings = new();
        _configurationReady = false;
        _cleanupUnconfirmed = false;
        _nextTargetRead = 0;
        _lastWaiting = null;
        runtime.Run("configuration-load", () =>
        {
            _configurationReady = runtime.Load("theme", () => new ThemeSettings(), SettingsValidation.Theme, out var settings);
            if (_configurationReady && settings is not null) _settings = settings with { TargetKeys = settings.TargetKeys.ToArray() };
        });
        runtime.Start(Pulse, OnSceneChanged, ReleaseTheme);
    }

    private void Pulse()
    {
        var runtime = _runtime!;
        if (!_configurationReady || !_settings.Enabled || !runtime.HasScene || _cleanupUnconfirmed) return;
        if (_themeScope is { IsActive: false }) ReleaseTheme();
        if (_cleanupUnconfirmed || runtime.Now < _nextTargetRead) return;
        _nextTargetRead = runtime.Now + 5;
        if (_themeScope is null)
        {
            var capability = runtime.Context.GetCapability(GameCapabilityIds.Resources);
            if (!capability.Available)
            {
                runtime.Fail("theme-provider", new(ErrorCode.CapabilityUnavailable,
                    capability.UnavailableReason ?? "Game UI colors are not available in the current scene.",
                    GameCapabilityIds.Resources, Retryable: true));
                return;
            }
            _themeScope = runtime.Context.CreateScope("theme-colors", ScopeLifetime.Scene);
        }
        var resources = runtime.Service<IGameResourceService>(_themeScope, GameCapabilityIds.Resources, "theme-provider");
        if (resources is null) { ReleaseTheme(); return; }
        runtime.Clear("theme-provider");
        var result = resources.GetTargets();
        if (!result.Success || result.Value is null)
        {
            runtime.Fail("theme-targets", result.Error, GameCapabilityIds.Resources);
            ReleaseTheme();
            return;
        }
        if (result.Value.Count > 2048 || result.Value.Any(target => target is null || string.IsNullOrWhiteSpace(target.Key)))
        {
            runtime.Fail("theme-targets", new(ErrorCode.ContentInvalid, "Game color target list is invalid or exceeds 2048 entries.", GameCapabilityIds.Resources));
            return;
        }
        var keys = result.Value.Select(target => target.Key).ToHashSet(StringComparer.Ordinal);
        var selected = SelectedKeys(keys).ToArray();
        var observed = keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        if (_applied.Count > 0 && !_observedKeys.SequenceEqual(observed, StringComparer.Ordinal))
        {
            ReleaseTheme();
            _nextTargetRead = runtime.Now + 1;
            return;
        }
        _observedKeys = observed;
        runtime.Clear("theme-targets");
        if (selected.Length == 0)
        {
            Waiting("No configured game UI color target is present. The theme waits for a later scene or discovery update.");
            return;
        }
        var failures = new List<OperationError>();
        foreach (var key in selected)
        {
            if (_applied.ContainsKey(key)) continue;
            var applied = resources.OverrideColor(new(key, _settings.Color, _settings.Priority));
            if (!applied.Success || applied.Value is null)
            {
                failures.Add(applied.Error ?? new(ErrorCode.ServiceUnavailable, "Color override returned no lease.", GameCapabilityIds.Resources, key));
                continue;
            }
            _applied.Add(key, applied.Value);
            _lastWaiting = null;
            runtime.Clear("theme-waiting");
            runtime.Context.Log.Log(LogLevel.Info, $"Theme layer applied: target={PluginRuntime.Clip(key, 256)}; " +
                $"priority={_settings.Priority}; sceneGeneration={runtime.SceneGeneration}; " +
                $"RGBA={_settings.Color.R},{_settings.Color.G},{_settings.Color.B},{_settings.Color.A}.");
        }
        if (failures.Count > 0) runtime.Fail("theme-apply", failures[0]);
        else runtime.Clear("theme-apply");
        var missing = _settings.TargetKeys.FirstOrDefault(key => !keys.Contains(key));
        if (missing is not null)
            runtime.Fail("theme-configured-target", new(ErrorCode.TargetMissing,
                "A configured color key is absent in this scene; other theme targets remain active.", GameCapabilityIds.Resources, missing, true));
        else runtime.Clear("theme-configured-target");
    }

    private IEnumerable<string> SelectedKeys(HashSet<string> available)
    {
        var selected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in _settings.TargetKeys) if (available.Contains(key)) selected.Add(key);
        if (_settings.IncludeMenuButtons)
            foreach (var key in available.Where(key => key.StartsWith("game.menu/button/", StringComparison.Ordinal) &&
                key.EndsWith("/color", StringComparison.Ordinal)).OrderBy(key => key, StringComparer.Ordinal))
            {
                if (selected.Count == MaximumThemeTargets) break;
                selected.Add(key);
            }
        return selected.Take(MaximumThemeTargets);
    }

    private void Waiting(string message)
    {
        if (_lastWaiting == message) return;
        _lastWaiting = message;
        _runtime!.Fail("theme-waiting", new(ErrorCode.TargetMissing, message, GameCapabilityIds.Resources, Retryable: true));
    }

    private void OnSceneChanged()
    {
        ReleaseTheme();
        _nextTargetRead = _runtime!.Now + 1;
        _lastWaiting = null;
    }

    private void ReleaseTheme()
    {
        if (_themeScope is null) return;
        var runtime = _runtime!;
        bool confirmed = true;
        foreach (var lease in _applied.Values)
            if (!runtime.Release(lease, "theme-layer-release")) confirmed = false;
        if (!runtime.Release(_themeScope, "theme-release")) confirmed = false;
        if (!confirmed)
        {
            _cleanupUnconfirmed = true;
            runtime.Fail("theme-restoration", new(ErrorCode.MutationWriteFailed,
                "Theme withdrawal encountered an unconfirmed cleanup; no new color layers will be created in this activation.",
                GameCapabilityIds.Resources));
        }
        _themeScope = null;
        _applied.Clear();
        _observedKeys = Array.Empty<string>();
        if (confirmed) runtime.Context.Log.Log(LogLevel.Info,
            "Theme scene scope withdrawn; baseline or remaining-layer restoration is reported by the host color coordinator.");
    }

    public void Disable()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}
