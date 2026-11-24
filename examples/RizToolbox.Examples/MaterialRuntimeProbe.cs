using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Examples;

/// <summary>SDK-only, explicitly clicked probe. The adapter decides shader provenance and checks native parameter read-back.</summary>
internal sealed class MaterialRuntimeProbe : IDisposable
{
    private const int MaximumTargets = 128;
    private readonly IPluginContext _context;
    private IUiWindowHandle? _window;
    private IUiControlHandle? _status;
    private IVisualAssetLease? _asset;
    private IMutationLease<VisualAsset>? _override;
    private VisualTarget? _selected;
    private long? _attemptedScene;
    private int _cursor = -1, _created, _released, _applied, _restoresRequested;
    private long _sceneGeneration;
    private bool _disposed;
    private string? _profile;

    internal MaterialRuntimeProbe(IPluginContext context) => _context = context;

    internal void Tick(long sceneGeneration)
    {
        if (_disposed || _window?.IsAlive == true || _attemptedScene == sceneGeneration) return;
        var ui = _context.GetService<IOwnedUiService>(UiInputCapabilityIds.Windows);
        if (!ui.Available || ui.Service is null) return;
        _attemptedScene = sceneGeneration;
        _sceneGeneration = sceneGeneration;
        var created = ui.Service.CreateWindow(new("material-proof", UiMountIds.Overlay, "Material Color / Float proof",
            new(new(830, 80, 520, 610)), Theme: new(FontSize: 18)));
        if (!created.Success) { Log("WINDOW REFUSED", new { created.Error }); return; }
        _window = created.Value!;
        _status = Need(_window.AddLabel(new("target", "Select a live game material target", new(15, 40, 490, 90))));
        Button("next-target", "Next game material target", 140, NextTarget);
        Button("tmp-parameters", "Apply TMP Color + Float profile", 195, () => Apply(true));
        Button("translucent-parameters", "Apply Translucent Float profile", 250, () => Apply(false));
        Button("restore", "Dispose override / restore / release clone", 305, () => Release("manual"));
        Button("stats", "Audit selected target / probe resource counts", 360, Audit);
        Need(_window.AddLabel(new("limits", "Each Apply checks only the selected live target.\nA refused shader stays unchanged; select Next.\nParameter read-back is checked inside the adapter.\nRestore is requested; inspect the game and logs.", new(15, 420, 490, 135))));
        Log("WINDOW CREATED", new { sceneGeneration, layout = new { x = 830, y = 80, width = 520, height = 610 }, parameterReadback = "adapter CreateMaterial success only" });
        NextTarget();
    }

    private void Button(string id, string text, float y, Action action)
        => Need(_window!.AddButton(new(id, text, new(15, y, 490, 45)), () =>
        {
            Log("BUTTON", new { id, _sceneGeneration });
            try { action(); }
            catch (Exception error) { Log("ACTION FAILED", new { id, message = Limit(error.Message, 500) }); }
        }));

    private IVisualResourceService? Service()
    {
        var lookup = _context.GetService<IVisualResourceService>(VisualCapabilityIds.Materials);
        if (lookup.Available && lookup.Service is not null) return lookup.Service;
        Log("CAPABILITY UNAVAILABLE", new { code = lookup.Error?.KnownCode, lookup.Error });
        return null;
    }

    private void NextTarget()
    {
        if (_override is not null || _asset is not null) Release("select-next");
        var service = Service(); if (service is null) return;
        var all = service.GetTargets().Where(x => x.Kind == VisualTargetKind.Material && x.Metadata.Active && !x.Metadata.Template)
            .OrderByDescending(x => x.Metadata.ResourceId == "unity.ui.translucent-image.material" ? 2 : x.Metadata.ResourceId == "unity.tmp.font-shared-material" ? 1 : 0)
            .ThenBy(x => x.Target.Key, StringComparer.Ordinal).ThenBy(x => x.Target.Generation).Take(MaximumTargets).ToArray();
        if (all.Length == 0)
        {
            _selected = null; SetStatus("TargetMissing: no eligible live game material target.");
            Log("TARGET MISSING", new { code = ErrorCode.TargetMissing, count = 0 }); return;
        }
        _cursor = (_cursor + 1) % all.Length; _selected = all[_cursor]; _profile = null;
        SetStatus($"Target {_cursor + 1}/{all.Length}: {Limit(_selected.Metadata.Description ?? _selected.Target.Key, 110)}\nChoose a profile; Next tries another game target.");
        Log("TARGET SELECTED", TargetInfo(_selected, new { index = _cursor + 1, count = all.Length, cap = MaximumTargets }));
    }

    private void Apply(bool textProfile)
    {
        Release("before-apply");
        var service = Service(); if (service is null) return;
        if (_selected is null) { Log("TARGET MISSING", new { code = ErrorCode.TargetMissing, reason = "Select a live target first." }); return; }
        // The copied target generation must still exist. Names and shader names never grant parameter access.
        var live = service.GetTargets().SingleOrDefault(x => x.Target == _selected.Target && x.Kind == VisualTargetKind.Material && x.Metadata.Active && !x.Metadata.Template);
        if (live is null) { SetStatus("TargetMissing: selected object generation ended. Select Next."); Log("TARGET MISSING", new { code = ErrorCode.TargetMissing, _selected.Target }); return; }
        _selected = live;
        _profile = textProfile ? "rizline.pc.tmp-mobile-outline.v1" : "rizline.pc.translucent-ui-float.v1";
        var colors = textProfile ? new[] {
            new MaterialColorProperty("_FaceColor", new(1f, .75f, .35f, 1f)),
            new MaterialColorProperty("_OutlineColor", new(.1f, .25f, .8f, 1f)) } : Array.Empty<MaterialColorProperty>();
        var floats = textProfile ? new[] { new MaterialFloatProperty("_OutlineWidth", .2f), new MaterialFloatProperty("_FaceDilate", .05f) }
            : new[] { new MaterialFloatProperty("_Brightness", .7f), new MaterialFloatProperty("_Vibrancy", 1.1f), new MaterialFloatProperty("_Flatten", .1f) };
        Log("CREATE REQUEST", new { profile = _profile, live.Target, colors, floats });
        var created = service.CreateMaterial(new(live.Target, colors, floats));
        if (!created.Success || created.Value is null)
        {
            var mismatch = created.Error?.KnownCode is ErrorCode.UnsupportedSignature or ErrorCode.OperationUnsupported or ErrorCode.TargetMissing;
            SetStatus(mismatch ? "TargetMissing: selected material does not match this verified profile. Select Next." : "Clone creation refused; see failure diagnostic.");
            Log("CREATE REFUSED", new { profile = _profile, code = mismatch ? ErrorCode.TargetMissing : ErrorCode.ResourceLoadFailed, error = Limit(created.Error?.Message, 650), live.Target });
            Audit(); return;
        }
        _asset = created.Value; _created++;
        Log("CREATED", new { profile = _profile, _asset.Asset, _asset.EstimatedBytes, _asset.IsValid,
            parameterReadback = "adapter accepted every requested native GetColor/GetFloat value", sourceUnchanged = "clone-only adapter contract" });
        var applied = service.Override(live.Target, _asset.Asset, priority: 100);
        if (!applied.Success || applied.Value is null)
        {
            Log("OVERRIDE REFUSED", new { profile = _profile, error = Limit(applied.Error?.Message, 650), live.Target });
            Release("override-refused"); return;
        }
        _override = applied.Value; _applied++;
        SetStatus($"Applied {_profile}\nInspect actual game rendering, then Dispose / restore.");
        Log("APPLIED", new { profile = _profile, live.Target, _override.LeaseId, _override.IsActive,
            assignmentReadback = "adapter Override success", rendering = "requires visual inspection" });
        Audit();
    }

    private void Audit()
    {
        Log("RESOURCE COUNTS", new { _sceneGeneration, profile = _profile, created = _created, released = _released,
            applied = _applied, restoreRequests = _restoresRequested, retainedProbeClones = _asset is null ? 0 : 1,
            estimatedRetainedProbeBytes = _asset?.EstimatedBytes ?? 0, activeProbeOverrides = _override?.IsActive == true ? 1 : 0,
            assetValid = _asset?.IsValid, accounting = "probe lease counts; SDK exposes no native visual resource statistics" });
        if (_selected is not null) Log("TARGET AUDIT", TargetInfo(_selected, new { profile = _profile }));
    }

    private static object TargetInfo(VisualTarget target, object detail) => new { target.Target, target.Metadata.Scene, target.Metadata.Role,
        description = Limit(target.Metadata.Description, 220), aliases = target.Aliases.Take(2).Select(x => Limit(x, 160)), detail };

    private void Release(string reason)
    {
        if (_override is not null)
        {
            var lease = _override;
            // Keep the clone leased if override disposal throws, so the target cannot reference a prematurely released asset.
            lease.Dispose(); _override = null; _restoresRequested++;
            Log("RESTORE REQUESTED", new { reason, profile = _profile, lease.LeaseId, activeAfterDispose = lease.IsActive,
                baselineReadback = "not exposed by SDK; inspect adapter diagnostics and game rendering" });
        }
        if (_asset is not null)
        {
            var asset = _asset; asset.Dispose(); _asset = null; _released++;
            Log("CLONE RELEASED", new { reason, profile = _profile, asset.Asset, managedLeaseValidAfterDispose = asset.IsValid });
        }
        if (!_disposed) Audit();
    }

    internal void SceneChanged(long generation)
    {
        if (_disposed) return;
        try { Release("scene-change"); }
        catch (Exception error) { Log("SCENE CLEANUP FAILED", new { message = Limit(error.Message, 500) }); }
        _sceneGeneration = generation; _selected = null; _cursor = -1; _status = null; _window = null;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { Release("plugin-scope-ended"); }
        finally { _window?.Dispose(); _window = null; _status = null; }
    }
    private void SetStatus(string text) { if (_status is not null) { var result = _status.SetText(text); if (!result.Success) Log("STATUS REFUSED", new { result.Error }); } }
    private void Log(string label, object? value)
    {
        var text = "MaterialProbe " + label + " " + JsonSerializer.Serialize(value);
        _context.Log.Log(LogLevel.Info, text.Length < 2048 ? text : text[..2000] + " [truncated]");
    }
    private static string? Limit(string? text, int maximum) => text is null || text.Length <= maximum ? text : text[..maximum] + "…";
    private static T Need<T>(Result<T> result) => result.Success ? result.Value! : throw new InvalidOperationException(result.Error!.Message);
}
