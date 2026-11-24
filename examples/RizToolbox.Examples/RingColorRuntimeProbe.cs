using System.Globalization;
using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Examples;

/// <summary>Optional SDK-only ring controls. A displayed candidate is never an implicit selection or write.</summary>
internal sealed class RingColorRuntimeProbe : IDisposable
{
    private const string Role = GameAppearanceRoles.TrackRingColor;
    private readonly IPluginContext _context;
    private IDisposable? _frames, _scenes;
    private IUiWindowHandle? _window;
    private IUiControlHandle? _count, _candidate, _keyInput, _selection, _rgbaInput, _status;
    private GameAppearanceTarget[] _targets = Array.Empty<GameAppearanceTarget>();
    private GameAppearanceTarget? _selected;
    private IMutationLease<ResourceColor>? _override;
    private string? _overrideId;
    private MutationKey<ResourceColor>? _overrideKey;
    private long _scene;
    private double _next;
    private int _cursor = -1;
    private bool _disposed, _hidden;
    private string? _lastError;

    private RingColorRuntimeProbe(IPluginContext context) => _context = context;

    internal static void Start(IPluginContext context)
    {
        var config = context.Configuration.Load("ring-color-proof", () => new Settings());
        if (!config.Success) { context.Log.Log(LogLevel.Warning, "RingProbe CONFIG REFUSED " + JsonSerializer.Serialize(config.Error)); return; }
        if (config.Value?.Enabled != true) return;
        var probe = context.Own(new RingColorRuntimeProbe(context));
        try
        {
            probe._scenes = context.Events.Subscribe<GameSceneChanged>(change => probe.SceneChanged(change.SceneGeneration));
            probe._frames = context.Events.Subscribe<FrameUpdate>(probe.Tick);
            probe.Log("ENABLED", new { writes = "one explicitly selected canonical key; button clicks only" });
        }
        catch { probe.Dispose(); throw; }
    }

    private void Tick(FrameUpdate frame)
    {
        if (_disposed || _hidden || frame.ElapsedSeconds < _next) return;
        _next = frame.ElapsedSeconds + 1;
        try
        {
            if (_window?.IsAlive != true)
            {
                if (!Release("window-ended")) return;
                CloseWindow(); _selected = null;
                var ui = _context.GetService<IOwnedUiService>(UiInputCapabilityIds.Windows);
                if (!ui.Available || ui.Service is null) return;
                var created = ui.Service.CreateWindow(new("ring-color-proof", UiMountIds.Overlay, "Selected judge ring color",
                    new(new(15, 80, 790, 860)), Theme: new(FontSize: 17)));
                if (!created.Success) { Refused("window", created.Error!); return; }
                _window = created.Value!;
                _count = Need(_window.AddLabel(new("count", "Querying current ring targets", new(15, 40, 760, 65))));
                Button("refresh", "Refresh ring targets / log keys", 115, 15, () => RefreshTargets(true));
                Button("next", "Next candidate key", 115, 405, NextCandidate);
                _candidate = Need(_window.AddLabel(new("candidate", "No candidate chosen. Next only displays a key.", new(15, 175, 760, 90))));
                _keyInput = Need(_window.AddTextField(new("canonical-key", "", new(15, 280, 760, 45)), characterLimit: 512));
                Button("select", "Select exact canonical key above", 340, 15, Select, 760);
                _selection = Need(_window.AddLabel(new("selection", "No target selected. Select a key before applying.\nRGBA below: enter r,g,b,a in [0,1], using decimal points.", new(15, 400, 760, 95))));
                _rgbaInput = Need(_window.AddTextField(new("rgba", "", new(15, 510, 760, 45)), characterLimit: 128));
                Button("apply", "Apply RGBA to selected ring", 575, 15, Apply);
                Button("release", "Release layer / request restore", 575, 405, () => {
                    if (Release("manual")) SetStatus("Probe layer released. Read selected color / holders to inspect the current game value.");
                });
                Button("read", "Read selected color / holders", 635, 15, Audit);
                Button("hide", "Release and hide for this scene", 635, 405, () => {
                    if (!Release("hide")) return;
                    _hidden = true; _selected = null; CloseWindow();
                });
                _status = Need(_window.AddLabel(new("status", "Refresh -> Next key -> Select -> enter RGBA -> Apply.\nOnly this probe's layer is released. Rendering remains experimental.", new(15, 695, 760, 150))));
                Log("WINDOW CREATED", new { _scene });
            }
            RefreshTargets();
        }
        catch (Exception error) { Failed("tick", error); }
    }

    private IGameAppearanceService? RefreshTargets(bool logKeys = false)
    {
        var lookup = _context.GetService<IGameAppearanceService>(GameAppearanceCapabilityIds.Appearance);
        if (!lookup.Available || lookup.Service is null)
        {
            Unavailable(lookup.Error ?? Error(ErrorCode.CapabilityUnavailable, "Game appearance is unavailable.")); return null;
        }
        var result = lookup.Service.GetTargets(new(Role: Role));
        if (!result.Success) { Unavailable(result.Error!); return null; }
        _targets = result.Value!.OrderBy(value => value.Target.Key, StringComparer.Ordinal).ToArray();
        Text(_count, $"Current eligible ring targets: {_targets.Length}. One canonical key is required.\nNext displays a candidate; Select confirms it. Neither changes color.");
        if (logKeys) foreach (var target in _targets) Log("TARGET", TargetInfo(target));
        if (_selected is { } selected && !_targets.Any(value => Same(value, selected)))
        {
            Release("target-expired"); _selected = null; Text(_selection, "Selected target expired. Choose and select a current canonical key.");
            Refused("selected-target", Error(ErrorCode.TargetExpired, "The selected run, scene or target generation is no longer current.", selected.Target.Key));
        }
        else if (_override is not null && !_override.IsActive)
        {
            Release("lease-inactive");
            Refused("lease", Error(ErrorCode.ScopeExpired, "The ring override lease is inactive. Select or apply again only after inspecting the current target.", _selected?.Target.Key));
        }
        return lookup.Service;
    }

    private void Unavailable(OperationError error)
    {
        _targets = Array.Empty<GameAppearanceTarget>(); Text(_count, $"Ring target count unavailable [{error.Code}].");
        Release("appearance-unavailable"); _selected = null; Text(_selection, "No current target selected.");
        Refused("GetTargets", error);
    }

    private void NextCandidate()
    {
        if (RefreshTargets() is null) return;
        if (_targets.Length == 0) { Refused("candidate", Error(ErrorCode.TargetMissing, "No current eligible judge ring target is present.")); return; }
        var target = _targets[_cursor = (_cursor + 1) % _targets.Length];
        Text(_keyInput, target.Target.Key);
        Text(_candidate, $"Candidate {_cursor + 1}/{_targets.Length}; generation {target.Target.Generation}.\n{target.Target.Key}\nClick Select to confirm this key.");
        Log("CANDIDATE", TargetInfo(target));
    }

    private void Select()
    {
        var entered = _keyInput!.GetText();
        if (!entered.Success) { Refused("key-input", entered.Error!); return; }
        var key = entered.Value!.Trim();
        if (key.Length == 0) { Refused("select", Error(ErrorCode.InvalidArgument, "Enter a full canonical key or display one with Next.")); return; }
        if (RefreshTargets() is null) return;
        // Exact canonical equality intentionally excludes aliases and a replacement's generation.
        var matches = _targets.Where(value => value.Target.Key == key && value.Property.Role == Role).ToArray();
        if (matches.Length != 1)
        {
            Refused("select", Error(matches.Length == 0 ? ErrorCode.TargetMissing : ErrorCode.AmbiguousTarget,
                "The entered key must identify exactly one current canonical ring target.", key)); return;
        }
        if (!Release("selection-changed")) return;
        _selected = matches[0];
        Text(_selection, $"Selected generation {_selected.Target.Generation}; run {_selected.RunId}.\n{_selected.Target.Key}\nRGBA below: r,g,b,a, each finite and in [0,1].");
        SetStatus("Selection confirmed. Enter an explicit RGBA value, then Apply to this ring only.");
        Log("SELECTED", TargetInfo(_selected));
    }

    private GameAppearanceTarget? CurrentSelection(out IGameAppearanceService? service)
    {
        service = null;
        if (_selected is null) { Refused("selection", Error(ErrorCode.TargetMissing, "Select an exact canonical ring key first.")); return null; }
        service = RefreshTargets();
        return service is null ? null : _selected;
    }

    private void Apply()
    {
        var selected = CurrentSelection(out var service); if (selected is null) return;
        var key = _keyInput!.GetText();
        if (!key.Success) { Refused("key-input", key.Error!); return; }
        if (key.Value!.Trim() != selected.Target.Key)
        { Refused("apply", Error(ErrorCode.InvalidArgument, "The displayed key differs from the confirmed selection. Click Select before applying.", selected.Target.Key)); return; }
        var entered = _rgbaInput!.GetText();
        if (!entered.Success) { Refused("rgba-input", entered.Error!); return; }
        var pieces = entered.Value!.Split(',', StringSplitOptions.TrimEntries);
        var channels = new float[4];
        var valid = pieces.Length == 4;
        for (var index = 0; valid && index < 4; index++)
            valid = float.TryParse(pieces[index], NumberStyles.Float, CultureInfo.InvariantCulture, out channels[index])
                && float.IsFinite(channels[index]) && channels[index] >= 0 && channels[index] <= 1;
        if (!valid)
        { Refused("apply", Error(ErrorCode.InvalidArgument, "Enter exactly four finite channels r,g,b,a in [0,1], using decimal points.", selected.Target.Key)); return; }
        if (!Release("replace-layer")) return;
        var color = new ResourceColor(channels[0], channels[1], channels[2], channels[3]);
        var result = service!.OverrideColor(new(new(Key: selected.Target.Key, Role: Role), selected.RunId, color,
            Priority: 100, SceneGeneration: selected.SceneGeneration));
        if (!result.Success) { Refused("OverrideColor", result.Error!); return; }
        _override = result.Value!;
        _overrideId = _override.LeaseId; _overrideKey = _override.Key;
        Log("APPLIED", new { target = TargetInfo(selected), color, priority = 100, leaseId = _overrideId });
        SetStatus($"Override accepted for this ring only; priority 100.\n{selected.Target.Key}\nRelease removes this probe's layer; inspect rendering and the actual read-back.");
    }

    private void Audit()
    {
        var selected = CurrentSelection(out _); if (selected is null) return;
        var targets = _context.GetService<ITargetService>(TargetCapabilityIds.Targets);
        if (!targets.Available || targets.Service is null)
        { Refused("read", targets.Error ?? Error(ErrorCode.CapabilityUnavailable, "Shared targets are unavailable.", selected.Target.Key)); return; }
        var handle = new TargetHandle<ResourceColor>(selected.Target.Key, selected.Target.Generation);
        var acquired = targets.Service.Acquire(handle);
        if (!acquired.Success) { Refused("Acquire", acquired.Error!); return; }
        using var read = acquired.Value!;
        var actual = read.Read();
        if (!actual.Success) { Refused("Read", actual.Error!); return; }
        var mutations = _context.GetService<IMutationService>(TargetCapabilityIds.Mutations);
        if (!mutations.Available || mutations.Service is null)
        { Refused("holders", mutations.Error ?? Error(ErrorCode.CapabilityUnavailable, "Shared mutations are unavailable.", selected.Target.Key)); return; }
        var state = mutations.Service.Describe(new MutationKey<ResourceColor>(handle, "color"));
        if (!state.Success) { Refused("Describe", state.Error!); return; }
        Log("READ", new { target = TargetInfo(selected), actual.Value, coordination = state.Value });
        SetStatus($"Actual selected RGBA: {actual.Value}\nHolders: {state.Value!.Holders.Count}; probe layer active: {_override?.IsActive == true}.\nRendering and restoration still require game inspection.");
    }

    private bool Release(string reason)
    {
        if (_override is null) return true;
        var lease = _override;
        try
        {
            var leaseId = _overrideId; var key = _overrideKey;
            // Lease properties can become unavailable as their owner scope ends.
            // Capture identity at Apply and do not query a disposed lease.
            lease.Dispose(); _override = null; _overrideId = null; _overrideKey = null;
            Log("RELEASE REQUESTED", new { reason, leaseId, key,
                restoration = "adapter conditionally restores the coordinated baseline; not a visual acceptance" });
            return true;
        }
        catch (Exception error) { Failed("release", error); return false; }
    }

    private void SceneChanged(long generation)
    {
        if (_disposed) return;
        Release("scene-change"); CloseWindow(); _targets = Array.Empty<GameAppearanceTarget>(); _selected = null;
        _scene = generation; _cursor = -1; _hidden = false; _next = 0;
    }

    private void Button(string id, string text, float y, float x, Action action, float width = 370)
        => Need(_window!.AddButton(new(id, text, new(x, y, width, 45)), () => {
            if (_disposed) return;
            try { action(); } catch (Exception error) { Failed(id, error); }
        }));
    private void Text(IUiControlHandle? control, string text)
    {
        if (control is null) return;
        var changed = control.SetText(text); if (!changed.Success) Log("UI REFUSED", changed.Error);
    }
    private void SetStatus(string text) { _lastError = null; Text(_status, text); }
    private void Refused(string action, OperationError error)
    {
        var stamp = action + " " + JsonSerializer.Serialize(error);
        if (_lastError != stamp) Log("REFUSED", new { action, error });
        _lastError = stamp;
        Text(_status, $"{action} [{error.Code}]\nCapability: {error.CapabilityId ?? "(unspecified)"}\nTarget: {error.TargetId ?? "(none)"}\n{error.Message}");
    }
    private void Failed(string action, Exception error) => Refused(action,
        Error(error is ObjectDisposedException ? ErrorCode.ScopeExpired : ErrorCode.InternalError, error.Message, _selected?.Target.Key));
    private static OperationError Error(ErrorCode code, string message, string? key = null)
        => new(code, message, GameAppearanceCapabilityIds.Appearance, key);
    private static bool Same(GameAppearanceTarget left, GameAppearanceTarget right) => left.Target.Key == right.Target.Key
        && left.Target.Generation == right.Target.Generation && left.RunId == right.RunId && left.SceneGeneration == right.SceneGeneration
        && left.Property.Role == Role;
    private static object TargetInfo(GameAppearanceTarget value) => new { key = value.Target.Key, value.Target.Generation,
        value.RunId, value.SceneGeneration, value.Target.Metadata.Scene, value.Property.Role };
    private void Log(string label, object? value)
    {
        var text = "RingProbe " + label + " " + JsonSerializer.Serialize(value);
        _context.Log.Log(LogLevel.Info, text.Length < 2048 ? text : text[..2000] + " [truncated]");
    }
    private void CloseWindow()
    {
        var previous = _window; _window = null;
        _count = _candidate = _keyInput = _selection = _rgbaInput = _status = null;
        try { previous?.Dispose(); } catch (Exception error) { Log("WINDOW RELEASE FAILED", error.Message); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { try { _frames?.Dispose(); } finally { _scenes?.Dispose(); } }
        finally { try { Release("proof-scope-ended"); } finally { CloseWindow(); } }
    }
    private static T Need<T>(Result<T> result) => result.Success ? result.Value! : throw new InvalidOperationException(result.Error!.Message);
    private sealed record Settings(bool Enabled = false);
}
