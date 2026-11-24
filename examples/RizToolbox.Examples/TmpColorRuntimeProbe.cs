using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Examples;

/// <summary>Opt-in controls owned by each real color plugin; every write requires a button click.</summary>
internal sealed class TmpColorRuntimeProbe : IDisposable
{
    private readonly IPluginContext _context;
    private readonly ResourceColor _color;
    private readonly int _priority;
    private IUiWindowHandle? _window;
    private IUiControlHandle? _status;
    private TargetHandle<ResourceColor>? _target;
    private ITargetValueLease<ResourceColor>? _read;
    private IMutationLease<ResourceColor>? _override;
    private double _next;
    private int _cursor = -1;
    private bool _disposed, _hidden;
    private long _scene;

    private TmpColorRuntimeProbe(IPluginContext context, ResourceColor color, int priority)
    { _context = context; _color = color; _priority = priority; }

    internal static bool Start(IPluginContext context, ResourceColor color, int priority)
    {
        var settings = context.Configuration.Load("tmp-color-proof", () => new Settings());
        if (!settings.Success) throw new InvalidOperationException(settings.Error?.Message);
        if (settings.Value?.Enabled != true) return false;
        var probe = context.Own(new TmpColorRuntimeProbe(context, color, priority));
        context.Events.Subscribe<GameSceneChanged>(change =>
        {
            // A sampled public event can arrive after a new native-scene window
            // was created. Release it explicitly before forgetting its stable ID.
            var previousWindow = probe._window; probe._status = null; probe._window = null;
            try { previousWindow?.Dispose(); } catch (Exception error) { probe.Log("WINDOW RELEASE FAILED", error.Message); }
            probe.Release(); probe.ClearRead(); probe._target = null;
            probe._scene = change.SceneGeneration; probe._cursor = -1; probe._hidden = false;
        });
        context.Events.Subscribe<FrameUpdate>(probe.Tick);
        probe.Log("ENABLED", new { priority, color, writes = "explicit buttons only" });
        return true;
    }

    private void Tick(FrameUpdate frame)
    {
        if (_disposed || _hidden || frame.ElapsedSeconds < _next) return;
        _next = frame.ElapsedSeconds + 1;
        if (_window?.IsAlive == true) return;
        var ui = _context.GetService<IOwnedUiService>(UiInputCapabilityIds.Windows);
        if (!ui.Available || ui.Service is null) return;
        var created = ui.Service.CreateWindow(new("tmp-color-proof", UiMountIds.Overlay,
            $"TMP field color / {_context.PluginId} / {_priority}",
            new(new(15, _priority == 10 ? 170 : 495, 650, 300)), Theme: new(FontSize: 16)));
        if (!created.Success) { Log("WINDOW REFUSED", created.Error); return; }
        _window = created.Value!;
        _status = Need(_window.AddLabel(new("target", "No TMP target selected", new(15, 40, 620, 95))));
        Button("select", "Next real TMP_Text.color", 145, 15, Select);
        Button("apply", $"Apply priority {_priority}", 145, 330, Apply);
        Button("release", "Release this plugin's layer", 195, 15, Release);
        Button("audit", "Read native color / holders", 195, 330, Audit);
        Button("hide", "Release and hide for this scene", 245, 15, () =>
        { Release(); ClearRead(); _hidden = true; _window.Dispose(); });
        if (_target is null) Select();
    }

    private void Button(string id, string text, float y, float x, Action action)
        => Need(_window!.AddButton(new(id, text, new(x, y, 300, 40)), () =>
        {
            try { action(); }
            catch (Exception error) { Log("ACTION FAILED", new { id, error = error.Message }); }
        }));

    private void Select()
    {
        Release(); ClearRead();
        var service = _context.GetService<ITargetService>(TargetCapabilityIds.Targets).Service;
        if (service is null) { Status("Target service is unavailable"); return; }
        var candidates = service.GetTargets().Where(x => x.ValueType == typeof(ResourceColor)
            && x.Metadata.ResourceId == "unity.tmp-text.color" && x.Metadata.Active && !x.Metadata.Template)
            .OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Generation).Take(512).ToArray();
        if (candidates.Length == 0) { _target = null; Status("TargetMissing: no live TMP field color"); return; }
        var selected = candidates[_cursor = (_cursor + 1) % candidates.Length];
        var match = service.Resolve<ResourceColor>(new(Key: selected.Key));
        if (match.Kind != TargetMatchKind.Unique || match.Target is null)
            throw new InvalidOperationException("Selected TMP binding is no longer unique/live.");
        var read = service.Acquire(match.Target);
        if (!read.Success) throw new InvalidOperationException(read.Error?.Message);
        _target = match.Target; _read = read.Value!;
        Log("SELECTED", new { _scene, target = _target, selected.Metadata, index = _cursor, count = candidates.Length });
        Audit();
    }

    private void Apply()
    {
        if (_override?.IsActive == true) { Audit(); return; }
        if (_target is null) throw new InvalidOperationException("Select a live TMP field target first.");
        var service = _context.GetService<IMutationService>(TargetCapabilityIds.Mutations).Service
            ?? throw new InvalidOperationException("Mutation service is unavailable.");
        var applied = service.Override(new MutationKey<ResourceColor>(_target, "color"), _color, _priority);
        if (!applied.Success) throw new InvalidOperationException(applied.Error?.Message);
        _override = applied.Value!;
        Log("APPLIED", new { target = _target, _priority, _color, _override.LeaseId, _override.IsActive });
        Audit();
    }

    private void Release()
    {
        if (_override is not null)
        {
            var id = _override.LeaseId;
            _override.Dispose(); _override = null;
            Log("RELEASE REQUESTED", new { target = _target, leaseId = id });
        }
        if (!_disposed && _target is not null) Audit();
    }

    private void Audit()
    {
        if (_target is null) { Status("No TMP target selected"); return; }
        var native = _read?.Read();
        var service = _context.GetService<IMutationService>(TargetCapabilityIds.Mutations).Service;
        var state = service?.Describe(new MutationKey<ResourceColor>(_target, "color"));
        Log("AUDIT", new { target = _target, native, state });
        Status($"{_target.Key}\nNative read={native?.Success}; RGBA={native?.Value}; holders={state?.Value?.Holders.Count}");
    }

    private void Status(string text) { if (_status is not null) Need(_status.SetText(text)); }
    private void ClearRead() { _read?.Dispose(); _read = null; }
    private void Log(string name, object? data) => _context.Log.Log(LogLevel.Info,
        "TmpFieldProbe " + name + " " + JsonSerializer.Serialize(data));
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { Release(); } finally { try { ClearRead(); } finally { _window?.Dispose(); } }
    }
    private static T Need<T>(Result<T> result)
        => result.Success ? result.Value! : throw new InvalidOperationException(result.Error!.Message);
    private static void Need(Result result) { if (!result.Success) throw new InvalidOperationException(result.Error!.Message); }
    private sealed record Settings(bool Enabled = false);
}
