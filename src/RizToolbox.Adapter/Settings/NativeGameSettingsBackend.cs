using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Settings;

internal sealed record FloatSettingDefinition(string Id, string Property, string SliderField, float SliderUnitsPerValue);
internal sealed record SettingMethodShape(bool Static, string ReturnType, IReadOnlyList<string> Parameters);
internal static class GameSettingSignatures
{
    internal static bool Getter(SettingMethodShape shape) => shape.Static && shape.ReturnType == "System.Single" && shape.Parameters.Count == 0;
    internal static bool Setter(SettingMethodShape shape) => shape.Static && shape.ReturnType == "System.Void" && shape.Parameters.SequenceEqual(new[] { "System.Single" });
}

/// <summary>Only official SettingsControl float getters/setters. No arbitrary field or save-file mutation.</summary>
internal sealed class NativeGameSettingsBackend : IGameSettingsBackend
{
    internal static IReadOnlyList<FloatSettingDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        // Steam build 25977441: each corresponding Awake slider callback divides by 100;
        // each Sync method multiplies by 100. The SettingsControl setters store the Single unchanged.
        new FloatSettingDefinition("rizline.audio.master-volume", "MasterVolume", "masterVolumeSlider", 100),
        new FloatSettingDefinition("rizline.audio.hitfx-volume", "HitFXVolume", "hitFXVolumeSlider", 100),
        new FloatSettingDefinition("rizline.audio.uise-volume", "UISEVolume", "uiSEVolumeSlider", 100),
        new FloatSettingDefinition("rizline.audio.music-volume", "MusicVolume", "musicVolumeSlider", 100)
    });
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly IntPtr _settings, _manager;
    private IntPtr _ui, _resources, _scene, _unityObject;
    private string? _rangeUnsupported;
    private bool _disposed;
    public IReadOnlyList<GameSettingBinding> Bindings { get; }
    public bool Ready
    {
        get { _main.VerifyAccess(); return !_disposed && _api.ReadStatic<IntPtr>(_manager, "Instance") != IntPtr.Zero; }
    }

    internal NativeGameSettingsBackend(NativeRuntime api, IMainThreadDispatcher main)
    {
        _api = api; _main = main; _main.VerifyAccess();
        _settings = api.Class("Assembly-CSharp.dll", "", "SettingsControl");
        _manager = api.Class("Assembly-CSharp.dll", "", "GameManager");
        RequireField(_manager, "Instance", "GameManager");
        Bindings = Array.AsReadOnly(Definitions.Select(definition =>
        {
            Func<float>? read = null; Action<float>? write = null; string? readError = null, writeError = null;
            try
            {
                var getter = api.Method(_settings, "get_" + definition.Property, 0, true, Array.Empty<string>());
                if (!GameSettingSignatures.Getter(new(true, api.DescribeReturnType(getter), Array.Empty<string>())))
                    throw new UnsupportedSignatureException("Settings getter is not a static Single().");
                read = () => { _main.VerifyAccess(); return api.Unbox<float>(api.CallStatic(_settings, "get_" + definition.Property)); };
            }
            catch (Exception ex) { readError = "Unsupported setting getter signature: " + ex.Message; }
            try
            {
                var setter = api.Method(_settings, "set_" + definition.Property, 1, true, new[] { "System.Single" });
                if (!GameSettingSignatures.Setter(new(true, api.DescribeReturnType(setter), new[] { "System.Single" })))
                    throw new UnsupportedSignatureException("Settings setter is not a static Void(Single).");
                write = value => { _main.VerifyAccess(); if (_disposed || !Ready) throw new InvalidOperationException("Game settings target has expired."); api.CallStatic(_settings, "set_" + definition.Property, A.Value(value)); };
            }
            catch (Exception ex) { writeError = "Unsupported setting setter signature: " + ex.Message; }
            return new GameSettingBinding(definition.Id, $"SettingsControl.get_{definition.Property} / set_{definition.Property}; game-owned scalar", read, write, readError, writeError)
            { Unit = "normalized volume scalar (0..1); continuous Single setter; UI percent = scalar * 100" };
        }).ToArray());
        try
        {
            _ui = api.Class("Assembly-CSharp.dll", "", "LevelListSettingsControl");
            _resources = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Resources");
            _scene = api.Class("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "Scene");
            _unityObject = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            api.Method(_resources, "FindObjectsOfTypeAll", 1, true, new[] { "System.Type" });
            var slider = api.Class("UnityEngine.UI.dll", "UnityEngine.UI", "Slider");
            foreach (var property in new[] { "get_minValue", "get_maxValue" })
                if (api.DescribeReturnType(api.Method(slider, property, 0, false)) != "System.Single") throw new UnsupportedSignatureException("Slider bound getter must return Single.");
            if (api.DescribeReturnType(api.Method(slider, "get_wholeNumbers", 0, false)) != "System.Boolean") throw new UnsupportedSignatureException("Slider.wholeNumbers must return Boolean.");
            foreach (var definition in Definitions) RequireField(_ui, definition.SliderField, "UnityEngine.UI.Slider");
        }
        catch (Exception ex) { _rangeUnsupported = "Game settings UI range metadata is unsupported: " + ex.Message; }
    }

    public Result<IReadOnlyDictionary<string, GameSettingRange>> GetWriteRanges()
    {
        _main.VerifyAccess();
        if (_disposed) return Failure(ErrorCode.TargetExpired, "Settings backend has been disposed.");
        if (!Ready) return Failure(ErrorCode.NotReady, "Game settings have not initialized.");
        if (_rangeUnsupported is not null) return Failure(ErrorCode.OperationUnsupported, _rangeUnsupported);
        try
        {
            var array = _api.CallStatic(_resources, "FindObjectsOfTypeAll", A.Ref(_api.Type(_ui)));
            return SettingsUiProbe.Capture(_api.ArrayItems(array), item => item == IntPtr.Zero ? null : _api.Root(item),
                IsActiveLoaded, ReadRanges);
        }
        catch (Exception ex) { return Failure(ex is UnsupportedSignatureException ? ErrorCode.UnsupportedSignature : ErrorCode.CapabilityUnavailable, "Setting UI range probe failed: " + ex.Message); }
    }

    private bool IsActiveLoaded(NativeRuntime.RootHandle root)
    {
        if (!_api.Unbox<bool>(_api.CallStatic(_unityObject, "op_Implicit", A.Ref(root.Target))) ||
            !_api.Unbox<bool>(_api.Call(root.Target, "get_isActiveAndEnabled"))) return false;
        var gameObject = _api.Call(root.Target, "get_gameObject"); using var objectRoot = _api.Root(gameObject);
        int scene = _api.Unbox<int>(_api.Call(objectRoot.Target, "get_scene"));
        return _api.Unbox<bool>(_api.CallStatic(_scene, "IsValidInternal", A.Value(scene))) &&
            _api.Unbox<bool>(_api.CallStatic(_scene, "GetIsLoadedInternal", A.Value(scene)));
    }

    private IReadOnlyDictionary<string, GameSettingRange> ReadRanges(NativeRuntime.RootHandle selected)
    {
        var ranges = new Dictionary<string, GameSettingRange>(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            var slider = _api.Read<IntPtr>(selected.Target, definition.SliderField);
            if (slider == IntPtr.Zero) continue;
            using var root = _api.Root(slider);
            if (!_api.Unbox<bool>(_api.CallStatic(_unityObject, "op_Implicit", A.Ref(root.Target)))) continue;
            float minimum = _api.Unbox<float>(_api.Call(root.Target, "get_minValue"));
            float maximum = _api.Unbox<float>(_api.Call(root.Target, "get_maxValue"));
            _ = _api.Unbox<bool>(_api.Call(root.Target, "get_wholeNumbers"));
            // Slider bounds are display percentages, while reads, requests and rollback baselines
            // stay in the original SettingsControl scalar units. Integer UI steps do not quantize
            // the direct setter: values such as .995 and the exact user baseline remain writable.
            ranges.Add(definition.Id, new(minimum / definition.SliderUnitsPerValue,
                maximum / definition.SliderUnitsPerValue));
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, GameSettingRange>(ranges);
    }

    private void RequireField(IntPtr owner, string name, string expected)
    { if (_api.DescribeFieldType(owner, name) != expected) throw new UnsupportedSignatureException($"{name} must have type {expected}."); }
    private static Result<IReadOnlyDictionary<string, GameSettingRange>> Failure(ErrorCode code, string message)
        => Result<IReadOnlyDictionary<string, GameSettingRange>>.Fail(new(code, message));
    public void Dispose() { _main.VerifyAccess(); _disposed = true; }
}
