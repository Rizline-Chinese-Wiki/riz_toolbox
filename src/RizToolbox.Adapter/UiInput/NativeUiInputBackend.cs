using RizToolbox.Adapter.Visual;
using RizToolbox.Core.Input;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.UiInput;

/// <summary>Owned UGUI, read-only Input System devices and one original game menu command. No injected input or raw callbacks.</summary>
internal sealed class NativeUiInputBackend : IUiInputBackend
{
    internal const string ActivateSelected = "game.main-menu.activate-selected";
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger? _log;
    private readonly bool _developerDiagnostics;
    private int _diagnosticSamples, _diagnosticEdges, _sourceDiagnostics;
    private double _nextDiagnostic;
    private readonly IntPtr _object, _resources, _scene, _sceneManager, _canvasType, _eventSystem, _rectUtility, _screen;
    private IntPtr _keyboard, _mouse, _gamepad, _inputControlExtensions, _deviceAdded, _deviceEnabled;
    private IntPtr _gamepadDeviceIdGetter;
    private int _invalidGamepadDeviceId;
    private int? _capturedGamepadDeviceId;
    private readonly List<NativeWindow> _focusWindows = new();
    private NativeRuntime.RootHandle? _overlayGo, _overlayCanvas;
    private bool _overlayCleanupUnconfirmed;
    private OwnedButtonActivation? _buttonActivation;
    private IntPtr _managerType;
    private string? _menuUnsupported, _navigationUnsupported, _keyboardUnsupported, _mouseUnsupported, _gamepadUnsupported;
    private string? _canvasProjectionUnsupported;
    private string? _keyboardCaptureFailure, _mouseCaptureFailure, _gamepadCaptureFailure;
    private string? _inputCaptureUnsupported;
    private readonly InputUpdateEdgeCollector _inputEdges = new();
    private NativeInputUpdateCapture? _inputUpdate;
    private Func<IReadOnlyList<InputBindingOwner>> _bindingSource = () => Array.Empty<InputBindingOwner>();
    private Func<IReadOnlyList<InputFocusInfo>> _focusSource = () => Array.Empty<InputFocusInfo>();
    private string? _inputScene;
    private UiPoint? _latestPointer;
    private int _capturedEdgeDiagnostics, _drainDiagnostics;
    private DeviceSample _keyboardSample, _mouseSample, _gamepadSample;
    private bool _disposed;
    private bool _keyboardReady => _keyboardSample.CaptureSucceeded;
    private bool _mouseReady => _mouseSample.CaptureSucceeded;
    private long _mountGeneration;
    private string? _lastMenuIdentity;
    // Keys never outlive a host capture epoch; each cached Canvas is held by an owned window's root handle.
    private readonly Dictionary<IntPtr, CanvasProjectionFrame> _canvasProjections = new();
    private IntPtr _transformPoint, _worldToScreenPoint, _cameraWorldToScreenPoint;
    private readonly record struct CanvasProjectionFrame(CanvasProjection Projection, float ScreenWidth, float ScreenHeight, float? Dpi)
    {
        internal UiViewport Fit(UiLayout layout) => UiCoordinates.Fit(ScreenWidth, ScreenHeight, Dpi, Projection.Pixels, layout);
    }
    internal NativeUiInputBackend(NativeRuntime api, IMainThreadDispatcher main, ILogger? log = null,
        bool developerDiagnostics = false)
    {
        _api = api; _main = main; _log = log; _developerDiagnostics = developerDiagnostics; main.VerifyAccess();
        _object = C("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        _resources = C("UnityEngine.CoreModule.dll", "UnityEngine", "Resources");
        _scene = C("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "Scene");
        _sceneManager = C("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "SceneManager");
        _canvasType = C("UnityEngine.UIModule.dll", "UnityEngine", "Canvas");
        _rectUtility = C("UnityEngine.UIModule.dll", "UnityEngine", "RectTransformUtility");
        _eventSystem = C("UnityEngine.UI.dll", "UnityEngine.EventSystems", "EventSystem");
        _screen = C("UnityEngine.CoreModule.dll", "UnityEngine", "Screen");
        foreach (var symbol in EssentialSymbols)
        {
            var probe = new Modules.NativeModuleSymbolResolver(api).Probe(symbol);
            if (!probe.Available) throw new UnsupportedSignatureException(probe.Signature + ": " + probe.Failure);
        }
        try
        {
            _managerType = C("Assembly-CSharp.dll", "", "GameManager");
            RequireField(_managerType, "Instance", "GameManager"); RequireField(_managerType, "mainMenuControl", "MainMenuControl");
            var menu = C("Assembly-CSharp.dll", "", "MainMenuControl");
            RequireField(menu, "mainButtonCanvasGroup", "UnityEngine.CanvasGroup");
            // The one-argument convenience overload is stripped from this player.
            RequireMethod(C("UnityEngine.CoreModule.dll", "UnityEngine", "Component"), "GetComponentInParent", false,
                "UnityEngine.Component", "System.Type", "System.Boolean");
        }
        catch (Exception ex) { _menuUnsupported = ex.Message; }
        try
        {
            var transform = C("UnityEngine.CoreModule.dll", "UnityEngine", "Transform");
            RequireMethod(transform, "TransformPoint", false, "UnityEngine.Vector3", "UnityEngine.Vector3");
            RequireMethod(transform, "set_localScale", false, "System.Void", "UnityEngine.Vector3");
            RequireMethod(_rectUtility, "WorldToScreenPoint", true, "UnityEngine.Vector2", "UnityEngine.Camera", "UnityEngine.Vector3");
            var camera = C("UnityEngine.CoreModule.dll", "UnityEngine", "Camera");
            RequireMethod(camera, "WorldToScreenPoint", false, "UnityEngine.Vector3", "UnityEngine.Vector3");
            RequireMethod(camera, "get_pixelRect", false, "UnityEngine.Rect");
            RequireMethod(camera, "get_targetTexture", false, "UnityEngine.RenderTexture");
            RequireMethod(camera, "get_targetDisplay", false, "System.Int32");
            _transformPoint = _api.Method(transform, "TransformPoint", 1, false, new[] { "UnityEngine.Vector3" });
            _worldToScreenPoint = _api.Method(_rectUtility, "WorldToScreenPoint", 2, true, new[] { "UnityEngine.Camera", "UnityEngine.Vector3" });
            _cameraWorldToScreenPoint = _api.Method(camera, "WorldToScreenPoint", 1, false, new[] { "UnityEngine.Vector3" });
        }
        catch (Exception ex) { _canvasProjectionUnsupported = ex.Message; }
        try
        {
            if (_menuUnsupported is not null) throw new UnsupportedSignatureException(_menuUnsupported);
            var menu = C("Assembly-CSharp.dll", "", "MainMenuControl");
            RequireMethod(menu, "EnterCurrentButton", false, "System.Void"); RequireMethod(menu, "get_Working", false, "System.Boolean");
            RequireField(menu, "allowMove", "System.Boolean"); RequireField(menu, "opLock", "System.Boolean");
            RequireField(_managerType, "sceneManager", "Rizline.SceneManager");
            RequireField(C("Assembly-CSharp.dll", "Rizline", "SceneManager"), "_lock", "System.Boolean");
        }
        catch (Exception ex) { _navigationUnsupported = ex.Message; }
        try
        {
            RequireDeviceState();
            _keyboard = C("Unity.InputSystem.dll", "UnityEngine.InputSystem", "Keyboard");
            RequireMethod(_keyboard, "get_current", true, "UnityEngine.InputSystem.Keyboard");
            RequireMethod(_keyboard, "get_f8Key", false, "UnityEngine.InputSystem.Controls.KeyControl");
            var button = C("Unity.InputSystem.dll", "UnityEngine.InputSystem.Controls", "ButtonControl");
            RequireMethod(button, "get_wasPressedThisFrame", false, "System.Boolean");
        }
        catch (Exception ex) { _keyboardUnsupported = ex.Message; }
        try
        {
            RequireDeviceState();
            _mouse = C("Unity.InputSystem.dll", "UnityEngine.InputSystem", "Mouse");
            RequireMethod(_mouse, "get_current", true, "UnityEngine.InputSystem.Mouse");
            foreach (var method in new[] { "get_leftButton", "get_rightButton", "get_middleButton" })
                RequireMethod(_mouse, method, false, "UnityEngine.InputSystem.Controls.ButtonControl");
            RequireMethod(_mouse, "get_position", false, "UnityEngine.InputSystem.Controls.Vector2Control");
            _inputControlExtensions = C("Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputControlExtensions");
            RequireMethod(_inputControlExtensions, "ReadValueAsObject", true, "System.Object", "UnityEngine.InputSystem.InputControl");
            RequireMethod(C("Unity.InputSystem.dll", "UnityEngine.InputSystem.Controls", "ButtonControl"), "get_wasReleasedThisFrame", false, "System.Boolean");
        }
        catch (Exception ex) { _mouseUnsupported = ex.Message; }
        try
        {
            RequireDeviceState();
            _gamepad = C("Unity.InputSystem.dll", "UnityEngine.InputSystem", "Gamepad");
            var inputDevice = C("Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputDevice");
            RequireMethod(inputDevice, "get_deviceId", false, "System.Int32");
            _gamepadDeviceIdGetter = _api.Method(inputDevice, "get_deviceId", 0, false, Array.Empty<string>());
            _invalidGamepadDeviceId = _api.ReadStatic<int>(inputDevice, "InvalidDeviceId");
            RequireMethod(_gamepad, "get_current", true, "UnityEngine.InputSystem.Gamepad");
            RequireMethod(_gamepad, "get_dpad", false, "UnityEngine.InputSystem.Controls.DpadControl");
            var dpad = C("Unity.InputSystem.dll", "UnityEngine.InputSystem.Controls", "DpadControl");
            foreach (var control in GamepadControls)
            {
                var path = GamepadPath(control);
                RequireMethod(path.Dpad ? dpad : _gamepad, path.Getter, false, "UnityEngine.InputSystem.Controls.ButtonControl");
            }
            RequireMethod(C("Unity.InputSystem.dll", "UnityEngine.InputSystem.Controls", "ButtonControl"),
                "get_wasPressedThisFrame", false, "System.Boolean");
        }
        catch (Exception ex) { _gamepadUnsupported = ex.Message; }
        try { _inputUpdate = new(api, main, log!, CaptureInputUpdate, CaptureInputFailure); }
        catch (Exception ex)
        {
            _inputCaptureUnsupported = "Verified after-input capture is unavailable: " + ex.GetType().Name;
            try { _log?.Log(LogLevel.Warning, _inputCaptureUnsupported, ex); } catch { }
        }
    }
    internal static IReadOnlyList<AdapterSymbolRequirement> EssentialSymbols { get; } = Array.AsReadOnly(new[]
    {
        Symbol("scene.current", "UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "SceneManager", "GetActiveScene", true, "UnityEngine.SceneManagement.Scene"),
        Symbol("scene.name", "UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "Scene", "GetNameInternal", true, "System.String", "System.Int32"),
        Symbol("scene.valid", "UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "Scene", "IsValidInternal", true, "System.Boolean", "System.Int32"),
        Symbol("scene.loaded", "UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "Scene", "GetIsLoadedInternal", true, "System.Boolean", "System.Int32"),
        Symbol("canvas.mode", "UnityEngine.UIModule.dll", "UnityEngine", "Canvas", "get_renderMode", false, "UnityEngine.RenderMode"),
        Symbol("canvas.pixels", "UnityEngine.UIModule.dll", "UnityEngine", "Canvas", "get_pixelRect", false, "UnityEngine.Rect"),
        Symbol("canvas.camera", "UnityEngine.UIModule.dll", "UnityEngine", "Canvas", "get_worldCamera", false, "UnityEngine.Camera"),
        Symbol("canvas.flush", "UnityEngine.UIModule.dll", "UnityEngine", "Canvas", "ForceUpdateCanvases", true, "System.Void"),
        Symbol("rect.read", "UnityEngine.CoreModule.dll", "UnityEngine", "RectTransform", "get_rect", false, "UnityEngine.Rect"),
        Symbol("rect.hit", "UnityEngine.UIModule.dll", "UnityEngine", "RectTransformUtility", "RectangleContainsScreenPoint", true, "System.Boolean", "UnityEngine.RectTransform", "UnityEngine.Vector2", "UnityEngine.Camera"),
        Symbol("events.current", "UnityEngine.UI.dll", "UnityEngine.EventSystems", "EventSystem", "get_current", true, "UnityEngine.EventSystems.EventSystem"),
        Symbol("events.select", "UnityEngine.UI.dll", "UnityEngine.EventSystems", "EventSystem", "SetSelectedGameObject", false, "System.Void", "UnityEngine.GameObject"),
        Symbol("field.focus", "UnityEngine.UI.dll", "UnityEngine.UI", "InputField", "ActivateInputField", false, "System.Void"),
        Symbol("field.text.read", "UnityEngine.UI.dll", "UnityEngine.UI", "InputField", "get_text", false, "System.String"),
        Symbol("field.text.write", "UnityEngine.UI.dll", "UnityEngine.UI", "InputField", "set_text", false, "System.Void", "System.String"),
        Symbol("screen.width", "UnityEngine.CoreModule.dll", "UnityEngine", "Screen", "get_width", true, "System.Int32"),
        Symbol("screen.height", "UnityEngine.CoreModule.dll", "UnityEngine", "Screen", "get_height", true, "System.Int32")
    });
    private static AdapterSymbolRequirement Symbol(string id, string assembly, string ns, string type, string method, bool isStatic, string returns, params string[] parameters)
        => new(id, assembly, ns, type, AdapterSymbolKind.Method, method, isStatic, Array.AsReadOnly(parameters)) { ExpectedType = returns };
    private IntPtr C(string assembly, string ns, string type) => _api.Class(assembly, ns, type);
    private void RequireField(IntPtr type, string name, string expected)
    { if (_api.DescribeFieldType(type, name) != expected) throw new UnsupportedSignatureException("Field type changed: " + name + " expected " + expected); }
    private void RequireMethod(IntPtr type, string method, bool isStatic, string returns, params string[] parameters)
    { if (_api.DescribeReturnType(_api.Method(type, method, parameters.Length, isStatic, parameters)) != returns) throw new UnsupportedSignatureException("Method return changed: " + method); }
    private void RequireDeviceState()
    {
        var type = C("Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputDevice");
        RequireMethod(type, "get_added", false, "System.Boolean"); RequireMethod(type, "get_enabled", false, "System.Boolean");
        _deviceAdded = _api.Method(type, "get_added", 0, false, Array.Empty<string>());
        _deviceEnabled = _api.Method(type, "get_enabled", 0, false, Array.Empty<string>());
    }
    private bool Alive(IntPtr obj) => obj != IntPtr.Zero && _api.Unbox<bool>(_api.CallStatic(_object, "op_Implicit", A.Ref(obj)));
    private bool Loaded(IntPtr component)
    {
        if (!Alive(component)) return false;
        using var go = _api.Root(_api.Call(component, "get_gameObject")); var scene = _api.Unbox<int>(_api.Call(go.Target, "get_scene"));
        return _api.Unbox<bool>(_api.CallStatic(_scene, "IsValidInternal", A.Value(scene))) && _api.Unbox<bool>(_api.CallStatic(_scene, "GetIsLoadedInternal", A.Value(scene)));
    }
    private bool Active(IntPtr component) => Loaded(component) && _api.Unbox<bool>(_api.Call(component, "get_isActiveAndEnabled"));
    public string SceneIdentity
    {
        get { _main.VerifyAccess(); var scene = _api.Unbox<int>(_api.CallStatic(_sceneManager, "GetActiveScene")); return scene + ":" + _api.Text(_api.CallStatic(_scene, "GetNameInternal", A.Value(scene))); }
    }
    private string SceneName => SceneIdentity.Split(':', 2)[1];
    // The original game returns from LevelList through FakeMainMenu while retaining
    // the same actual MainMenuControl graph. Scene identity still expires all old leases.
    private static bool IsMainMenuScene(string scene) => scene is "MainMenu" or "FakeMainMenu";
    private NativeRuntime.RootHandle Menu()
    {
        if (_menuUnsupported is not null) throw new UnsupportedSignatureException(_menuUnsupported);
        if (!IsMainMenuScene(SceneName)) throw new TargetExpiredException("Actual MainMenu or FakeMainMenu scene is not current.");
        using var manager = _api.Root(_api.ReadStatic<IntPtr>(_managerType, "Instance"));
        var menu = _api.Root(_api.Read<IntPtr>(manager.Target, "mainMenuControl"));
        try { if (!Active(menu.Target)) throw new TargetExpiredException("Actual main menu is not active in a loaded scene."); return menu; }
        catch { menu.Dispose(); throw; }
    }
    private NativeRuntime.RootHandle MenuCanvas()
    {
        if (_canvasProjectionUnsupported is not null) throw new UnsupportedSignatureException(_canvasProjectionUnsupported);
        using var menu = Menu(); using var group = _api.Root(_api.Read<IntPtr>(menu.Target, "mainButtonCanvasGroup"));
        var pointer = _api.CallTyped(group.Target, "GetComponentInParent", new[] { "System.Type", "System.Boolean" }, A.Ref(_api.Type(_canvasType)), A.Value(false));
        var canvas = _api.Root(pointer);
        try
        {
            if (!Active(canvas.Target)) throw new TargetExpiredException("Main menu parent Canvas is inactive or unloaded.");
            var mode = _api.Unbox<int>(_api.Call(canvas.Target, "get_renderMode"));
            if (mode is not (0 or 1)) throw new UnsupportedSignatureException("World-space menu Canvas layout is not supported.");
            if (mode == 1 && !Alive(_api.Call(canvas.Target, "get_worldCamera"))) throw new TargetExpiredException("Screen-space camera Canvas has no live camera.");
            using var canvasGo = _api.Root(_api.Call(canvas.Target, "get_gameObject"));
            if (!Alive(GetComponent(canvasGo.Target, C("UnityEngine.UI.dll", "UnityEngine.UI", "GraphicRaycaster")))) throw new UnsupportedSignatureException("Actual menu Canvas has no GraphicRaycaster; pointer blocking is not available.");
            return canvas;
        }
        catch { canvas.Dispose(); throw; }
    }
    public IReadOnlyList<UiMountInfo> GetMounts()
    {
        _main.VerifyAccess(); var mounts = new List<UiMountInfo> {
            new(UiMountIds.Overlay, !_disposed && !_overlayCleanupUnconfirmed && (_overlayCanvas is null || Alive(_overlayCanvas.Target)),
                SceneName, _overlayCleanupUnconfirmed ? "Previous overlay cleanup is unconfirmed." : null, 1,
                "Screen-space overlay created with the first owned window; windows expire on scene change.") };
        try
        {
            using var canvas = MenuCanvas(); var identity = _api.Unbox<int>(_api.Call(canvas.Target, "GetInstanceID")) + ":" + SceneIdentity;
            if (_lastMenuIdentity != identity) { _lastMenuIdentity = identity; ++_mountGeneration; }
            mounts.Add(new(UiMountIds.MainMenu, true, SceneName, null, _mountGeneration, "Borrowed actual Canvas reached through GameManager.mainMenuControl.mainButtonCanvasGroup; only created descendants are owned."));
        }
        catch (Exception ex)
        {
            _lastMenuIdentity = null;
            mounts.Add(new(UiMountIds.MainMenu, false, null, ex.Message, _mountGeneration, "Borrowed actual menu Canvas.", ex is NotSupportedException ? ErrorCode.UnsupportedSignature : ErrorCode.TargetMissing));
        }
        return Array.AsReadOnly(mounts.ToArray());
    }
    public IReadOnlyList<InputDeviceInfo> GetDevices()
    {
        _main.VerifyAccess(); return Array.AsReadOnly(new[] {
            DeviceStatus(InputDeviceKind.Keyboard, _keyboardSample, _keyboardUnsupported ?? _inputCaptureUnsupported ?? _inputEdges.Failure, _keyboardCaptureFailure),
            DeviceStatus(InputDeviceKind.Mouse, _mouseSample, _mouseUnsupported ?? _inputCaptureUnsupported ?? _inputEdges.Failure, _mouseCaptureFailure),
            DeviceStatus(InputDeviceKind.Gamepad, _gamepadSample, _gamepadUnsupported ?? _inputCaptureUnsupported ?? _inputEdges.Failure, _gamepadCaptureFailure) });
    }
    internal readonly record struct DeviceSample(bool Sampled, bool Present, bool Added, bool Enabled, bool CaptureSucceeded);
    /// <summary>Temporary removal/disable skips all control reads and can recover on the next frame.</summary>
    internal static DeviceSample CaptureDevice(bool present, Func<bool> readAdded, Func<bool> readEnabled, Action capture)
    {
        if (!present) return new(true, false, false, false, false);
        if (!readAdded()) return new(true, true, false, false, false);
        if (!readEnabled()) return new(true, true, true, false, false);
        capture();
        return new(true, true, true, true, true);
    }
    /// <summary>Contains runtime/root failures for this frame only. The next call always retries the provider.</summary>
    internal static DeviceSample TryCaptureDevice(Func<DeviceSample> capture, out string? failure)
    {
        failure = null;
        try { return capture(); }
        catch (Exception ex)
        {
            // Exception text can include native details; expose the failure category and retry semantics only.
            failure = "capture failed (" + ex.GetType().Name + "); input capture will retry next frame.";
            return default;
        }
    }
    internal static InputDeviceInfo DeviceStatus(InputDeviceKind kind, DeviceSample state, string? unsupportedReason = null, string? captureFailure = null)
    {
        var mapping = kind switch {
            InputDeviceKind.Keyboard => "Input System Keyboard.current named KeyControl getters and wasPressedThisFrame; no modifier chords or injection.",
            InputDeviceKind.Mouse => "Input System Mouse.current boxed Vector2 position and Left/Right/Middle edges; owned native UGUI buttons remain independent.",
            InputDeviceKind.Gamepad => "Input System Gamepad.current digital ButtonControl.wasPressedThisFrame: " + string.Join(", ", GamepadControls) + ". Triggers use Input System press thresholds; no axes, rumble, injection or per-player device selection.",
            _ => "No declared mapping." };
        var name = kind switch { InputDeviceKind.Keyboard => "Keyboard.current", InputDeviceKind.Mouse => "Mouse.current", InputDeviceKind.Gamepad => "Gamepad.current", _ => "Unknown device" };
        var ready = captureFailure is null && state.Sampled && state.Present && state.Added && state.Enabled && state.CaptureSucceeded;
        var reason = unsupportedReason ?? (captureFailure is not null ? name + " " + captureFailure : null) ?? (!state.Sampled ? name + " has not been sampled yet."
            : !state.Present ? name + " is null; input capture will retry next frame."
            : !state.Added ? name + " is not added to Input System; control reads are skipped and will retry next frame."
            : !state.Enabled ? name + " is disabled; control reads are skipped and will retry next frame."
            : !state.CaptureSucceeded ? name + " has not completed a successful input capture." : null);
        return new(kind, unsupportedReason is not null ? InputSupport.Unsupported : ready ? InputSupport.RuntimeAvailable : InputSupport.SymbolVerified, mapping, reason);
    }
    internal static string KeyboardGetter(string name) => name switch
    {
        "Return" => "get_enterKey", "Escape" => "get_escapeKey", "Tab" => "get_tabKey",
        "Space" => "get_spaceKey", "Backspace" => "get_backspaceKey",
        "LeftArrow" => "get_leftArrowKey", "RightArrow" => "get_rightArrowKey",
        "UpArrow" => "get_upArrowKey", "DownArrow" => "get_downArrowKey",
        { Length: 1 } when name[0] is >= 'A' and <= 'Z' => "get_" + name.ToLowerInvariant() + "Key",
        { Length: 6 } when name.StartsWith("Alpha", StringComparison.Ordinal) && name[5] is >= '0' and <= '9' => "get_digit" + name[5] + "Key",
        _ when name.Length is >= 2 and <= 3 && name[0] == 'F' && int.TryParse(name.AsSpan(1), out var number) && number is >= 1 and <= 12 => "get_f" + number + "Key",
        _ => throw new UnsupportedSignatureException("Keyboard mapping is not declared: " + name)
    };
    internal static IReadOnlyList<string> GamepadControls { get; } = Array.AsReadOnly(new[] {
        "ButtonSouth", "ButtonEast", "ButtonWest", "ButtonNorth", "LeftShoulder", "RightShoulder",
        "LeftTrigger", "RightTrigger", "LeftStickButton", "RightStickButton", "Start", "Select",
        "DpadUp", "DpadDown", "DpadLeft", "DpadRight" });
    internal static (bool Dpad, string Getter) GamepadPath(string control) => control switch {
        "ButtonSouth" => (false, "get_buttonSouth"), "ButtonEast" => (false, "get_buttonEast"),
        "ButtonWest" => (false, "get_buttonWest"), "ButtonNorth" => (false, "get_buttonNorth"),
        "LeftShoulder" => (false, "get_leftShoulder"), "RightShoulder" => (false, "get_rightShoulder"),
        "LeftTrigger" => (false, "get_leftTrigger"), "RightTrigger" => (false, "get_rightTrigger"),
        "LeftStickButton" => (false, "get_leftStickButton"), "RightStickButton" => (false, "get_rightStickButton"),
        "Start" => (false, "get_startButton"), "Select" => (false, "get_selectButton"),
        "DpadUp" => (true, "get_up"), "DpadDown" => (true, "get_down"),
        "DpadLeft" => (true, "get_left"), "DpadRight" => (true, "get_right"),
        _ => throw new UnsupportedSignatureException("Gamepad digital mapping is not declared: " + control)
    };
    // The production sample owns a rooted current device throughout all state and
    // control reads. This narrow seam permits fault/root-lifetime tests without hardware.
    internal interface IGamepadSampleDevice : IDisposable
    {
        bool Added { get; }
        bool Enabled { get; }
        int DeviceId { get; }
        bool WasPressed(string control);
    }
    internal sealed record GamepadCapture(DeviceSample State, IReadOnlyList<InputBinding> Pressed, string? Failure, int? DeviceId = null);
    internal static GamepadCapture CaptureGamepad(Func<IGamepadSampleDevice?> openCurrent, int invalidDeviceId = 0)
    {
        var pressed = new List<InputBinding>();
        int? deviceId = null;
        var state = TryCaptureDevice(() =>
        {
            using var device = openCurrent();
            return CaptureDevice(device is not null, () => device!.Added, () => device!.Enabled, () =>
            {
                deviceId = device!.DeviceId;
                if (deviceId == invalidDeviceId) throw new InvalidOperationException("Current gamepad has the runtime's invalid device identity.");
                // Reading the declared 16 controls is a fixed budget even before
                // the first action registration; metadata alone never sets ready.
                foreach (var control in GamepadControls)
                    if (device!.WasPressed(control)) pressed.Add(new(InputDeviceKind.Gamepad, control));
                if (!device!.Added || !device.Enabled || device.DeviceId != deviceId)
                    throw new InvalidOperationException("Gamepad was removed or disabled during digital capture.");
            });
        }, out var failure);
        return new(state, state.CaptureSucceeded ? Array.AsReadOnly(pressed.ToArray()) : Array.Empty<InputBinding>(), failure,
            state.CaptureSucceeded ? deviceId : null);
    }
    internal static int? ApplyGamepadIdentity(InputUpdateEdgeCollector collector, IReadOnlyList<InputBindingOwner> bindings,
        int? priorDeviceId, GamepadCapture capture)
    {
        var current = capture.State.CaptureSucceeded ? capture.DeviceId : null;
        // Gamepad.current may change A -> B without an intervening null/disabled sample.
        // Revoke both queued and already drained epochs from A before admitting B's edges.
        if (current is null || priorDeviceId is not null && current != priorDeviceId)
            InvalidateGamepadEdges(collector, bindings);
        return current;
    }
    internal static void InvalidateGamepadEdges(InputUpdateEdgeCollector collector, IReadOnlyList<InputBindingOwner> bindings)
    {
        foreach (var binding in bindings.Where(x => x.Binding.Device == InputDeviceKind.Gamepad))
            collector.InvalidateAction(binding.ActionId);
    }
    private IGamepadSampleDevice? OpenGamepad()
    {
        var pointer = _api.CallStatic(_gamepad, "get_current");
        return pointer == IntPtr.Zero ? null : new NativeGamepadSample(this, _api.Root(pointer));
    }
    private sealed class NativeGamepadSample : IGamepadSampleDevice
    {
        private readonly NativeUiInputBackend _owner;
        private readonly NativeRuntime.RootHandle _device;
        private NativeRuntime.RootHandle? _dpad;
        internal NativeGamepadSample(NativeUiInputBackend owner, NativeRuntime.RootHandle device) => (_owner, _device) = (owner, device);
        public bool Added => _owner._api.Unbox<bool>(_owner._api.Invoke(_owner._deviceAdded, _device.Target));
        public bool Enabled => _owner._api.Unbox<bool>(_owner._api.Invoke(_owner._deviceEnabled, _device.Target));
        public int DeviceId => _owner._api.Unbox<int>(_owner._api.Invoke(_owner._gamepadDeviceIdGetter, _device.Target));
        public bool WasPressed(string control)
        {
            var api = _owner._api; var path = GamepadPath(control); var receiver = _device.Target;
            if (path.Dpad) { _dpad ??= api.Root(api.Call(_device.Target, "get_dpad")); receiver = _dpad.Target; }
            using var button = api.Root(api.Call(receiver, path.Getter));
            return api.Unbox<bool>(api.Call(button.Target, "get_wasPressedThisFrame"));
        }
        public void Dispose() { try { _dpad?.Dispose(); } finally { _device.Dispose(); } }
    }
    public void SetInputBindingSource(Func<IReadOnlyList<InputBindingOwner>> source)
    { _main.VerifyAccess(); _bindingSource = source ?? throw new ArgumentNullException(nameof(source)); }
    public void SetInputFocusSource(Func<IReadOnlyList<InputFocusInfo>> source)
    { _main.VerifyAccess(); _focusSource = source ?? throw new ArgumentNullException(nameof(source)); }
    public void InvalidateInputAction(string actionId) { _main.VerifyAccess(); _inputEdges.InvalidateAction(actionId); }
    public bool IsCurrentInputAction(InputActionEdge action) { _main.VerifyAccess(); return _inputEdges.IsCurrent(action); }
    public void ClearInput() { _main.VerifyAccess(); _inputEdges.Reset(); _latestPointer = null; _capturedGamepadDeviceId = null; _canvasProjections.Clear(); }
    private void CaptureInputFailure(Exception error)
    {
        _keyboardSample = _mouseSample = _gamepadSample = default;
        _keyboardCaptureFailure = _mouseCaptureFailure = _gamepadCaptureFailure = "after-input capture failed (" + error.GetType().Name + "); input capture will retry next update.";
        ClearInput();
    }
    private void CaptureInputUpdate(long invocation, int updateType, uint updateCount)
    {
        if (_disposed || _inputEdges.Retired) return;
        var bindings = _bindingSource();
        _main.VerifyAccess(); var pressed = new List<InputBinding>(); UiPoint? pointer = null; bool down = false, up = false;
        // No previous frame's ready bit can survive a missing, removed, disabled or failed current device.
        _keyboardSample = _mouseSample = _gamepadSample = default;
        _keyboardCaptureFailure = _mouseCaptureFailure = _gamepadCaptureFailure = null;
        if (_keyboardUnsupported is null)
        {
            var keys = new List<InputBinding>();
            _keyboardSample = TryCaptureDevice(() =>
            {
                var keyboardPointer = _api.CallStatic(_keyboard, "get_current");
                using var keyboard = keyboardPointer == IntPtr.Zero ? null : _api.Root(keyboardPointer);
                return CaptureDevice(keyboard is not null,
                    () => _api.Unbox<bool>(_api.Invoke(_deviceAdded, keyboard!.Target)),
                    () => _api.Unbox<bool>(_api.Invoke(_deviceEnabled, keyboard!.Target)), () =>
                {
                    foreach (var binding in bindings.Select(x => x.Binding).Where(x => x.Device == InputDeviceKind.Keyboard).Distinct())
                    {
                        var method = _api.Method(_keyboard, KeyboardGetter(binding.Control), 0, false, Array.Empty<string>());
                        if (_api.DescribeReturnType(method) != "UnityEngine.InputSystem.Controls.KeyControl")
                            throw new UnsupportedSignatureException("Keyboard control getter signature differs.");
                        using var key = _api.Root(_api.Invoke(method, keyboard!.Target));
                        if (key.Target != IntPtr.Zero && _api.Unbox<bool>(_api.Call(key.Target, "get_wasPressedThisFrame"))) keys.Add(binding);
                    }
                });
            }, out _keyboardCaptureFailure);
            if (_keyboardSample.CaptureSucceeded) pressed.AddRange(keys);
        }
        if (_mouseUnsupported is null)
        {
            var buttons = new List<InputBinding>(); UiPoint? mousePosition = null; bool mouseDown = false, mouseUp = false;
            _mouseSample = TryCaptureDevice(() =>
            {
                var mousePointer = _api.CallStatic(_mouse, "get_current");
                using var mouse = mousePointer == IntPtr.Zero ? null : _api.Root(mousePointer);
                return CaptureDevice(mouse is not null,
                    () => _api.Unbox<bool>(_api.Invoke(_deviceAdded, mouse!.Target)),
                    () => _api.Unbox<bool>(_api.Invoke(_deviceEnabled, mouse!.Target)), () =>
                {
                    using var positionControl = _api.Root(_api.Call(mouse!.Target, "get_position"));
                    // This build retains the static non-generic extension, not
                    // an instance method. Its boxed value avoids state offsets.
                    using var positionValue = _api.Root(_api.CallStatic(_inputControlExtensions, "ReadValueAsObject", A.Ref(positionControl.Target)));
                    var position = _api.Unbox<Vec2>(positionValue.Target); mousePosition = new(position.X, position.Y);
                    for (var i = 0; i < 3; i++)
                    {
                        using var button = _api.Root(_api.Call(mouse.Target, i == 0 ? "get_leftButton" : i == 1 ? "get_rightButton" : "get_middleButton"));
                        var isDown = _api.Unbox<bool>(_api.Call(button.Target, "get_wasPressedThisFrame"));
                        if (isDown) buttons.Add(new(InputDeviceKind.Mouse, i == 0 ? "Left" : i == 1 ? "Right" : "Middle"));
                        if (i == 0) { mouseDown = isDown; mouseUp = _api.Unbox<bool>(_api.Call(button.Target, "get_wasReleasedThisFrame")); }
                    }
                });
            }, out _mouseCaptureFailure);
            if (_mouseSample.CaptureSucceeded) { pressed.AddRange(buttons); pointer = mousePosition; down = mouseDown; up = mouseUp; }
        }
        // Reset scene epochs before observing the current device. Otherwise this
        // reset would erase A's identity after its edges were admitted, letting a
        // direct switch to B on the next update inherit A's first queued sample.
        var scene = SceneIdentity;
        if (_inputScene != scene) { _inputScene = scene; ClearInput(); }
        if (_gamepadUnsupported is null)
        {
            var capture = CaptureGamepad(OpenGamepad, _invalidGamepadDeviceId);
            _gamepadSample = capture.State; _gamepadCaptureFailure = capture.Failure;
            _capturedGamepadDeviceId = ApplyGamepadIdentity(_inputEdges, bindings, _capturedGamepadDeviceId, capture);
            if (_gamepadSample.CaptureSucceeded) pressed.AddRange(capture.Pressed);
        }
        _latestPointer = pointer;
        InputCapturePolicy? policy = null;
        if (pressed.Count != 0 || down || up)
        {
            using var events = CurrentEvents();
            var focused = events is null || _api.Unbox<bool>(_api.Call(events.Target, "get_isFocused"));
            policy = new(InputContext(SceneName), ReadTextEditing(events), focused, _focusSource());
        }
        _inputEdges.Add(invocation, updateType, bindings, pressed, pointer, down, up, updateCount, policy);
        if (_inputEdges.Retired)
        {
            _inputUpdate?.Dispose(); _inputUpdate = null;
            _keyboardSample = _mouseSample = _gamepadSample = default;
            try { _log?.Log(LogLevel.Error, _inputEdges.Failure!); } catch { }
            return;
        }
        if (_developerDiagnostics && pressed.Count != 0 && _capturedEdgeDiagnostics++ < 64)
            try { _log?.Log(LogLevel.Debug, $"Input edge captured after original input update: invocation={invocation}; updateType={updateType}; updateCount={updateCount}; pressed={string.Join(',', pressed.Take(16).Select(x => x.Device + "/" + x.Control))}; queued={_inputEdges.Pending}."); } catch { }
    }
    public UiInputFrame Capture(IReadOnlyList<InputBindingOwner> bindings)
    {
        _main.VerifyAccess();
        _canvasProjections.Clear();
        // Raw device state is sampled exclusively after input updates, including the game's manual updates.
        // Host frames only drain copied values and inspect current UI focus/context for dispatch.
        var updates = _inputEdges.Drain();
        var pressed = updates.SelectMany(x => x.Actions.Select(a => a.Owner.Binding)).Distinct().ToList();
        var pointer = _latestPointer; var down = updates.Any(x => x.LeftDown); var up = updates.Any(x => x.LeftUp);
        if (_developerDiagnostics && updates.Count != 0 && _drainDiagnostics++ < 64)
            try { _log?.Log(LogLevel.Debug, $"Input update edges drained on Host FrameUpdate: updates={updates.Count}; firstInvocation={updates[0].Invocation}; lastInvocation={updates[^1].Invocation}; actions={updates.Sum(x => x.Actions.Count)}."); } catch { }
        using var events = CurrentEvents();
        var textEditing = ReadTextEditing(events);
        bool? focused = events is null ? null : _api.Unbox<bool>(_api.Call(events.Target, "get_isFocused"));
        if (_developerDiagnostics && _log is not null)
        {
            var inputSeen = pressed.Count != 0 || down || up;
            var now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            if (inputSeen ? _diagnosticEdges < 64 : _diagnosticSamples < 24 && now >= _nextDiagnostic)
            {
                if (inputSeen) ++_diagnosticEdges;
                else { ++_diagnosticSamples; _nextDiagnostic = now + 10; }
                try { _log.Log(LogLevel.Debug, $"Input System sample: keyboard={_keyboardReady}; mouse={_mouseReady}; gamepad={_gamepadSample.CaptureSucceeded}; eventSystemFocused={focused}; pointer={pointer}; leftDown={down}; leftUp={up}; pressed={string.Join(',', pressed.Take(16).Select(x => x.Control))}."); } catch { }
                if (_sourceDiagnostics < 4 && (inputSeen || _diagnosticSamples is 1 or 4 or 12))
                {
                    ++_sourceDiagnostics;
                    try { _log.Log(LogLevel.Debug, "Input source comparison: " + NativeUiInputDiagnostics.Capture(_api)); } catch { }
                }
            }
        }
        if (focused == false) { pressed.Clear(); down = up = false; }
        return new(Array.AsReadOnly(pressed.ToArray()), pointer, down, up, textEditing, InputContext(SceneName))
            { Updates = focused == false ? Array.Empty<InputUpdateSample>() : updates };
    }
    internal static InputActionContext InputContext(string scene)
        => IsMainMenuScene(scene) || scene == "LevelList" ? InputActionContext.Menu : scene == "Level" ? InputActionContext.Gameplay : InputActionContext.None;
    private bool ReadTextEditing(NativeRuntime.RootHandle? events)
    {
        var textEditing = false;
        if (events is not null)
        {
            var selected = _api.Call(events.Target, "get_currentSelectedGameObject");
            if (Alive(selected))
            {
                using var selectedRoot = _api.Root(selected); var field = GetComponent(selectedRoot.Target, C("UnityEngine.UI.dll", "UnityEngine.UI", "InputField"));
                textEditing = Alive(field) && _api.Unbox<bool>(_api.Call(field, "get_isFocused"));
                try
                {
                    var tmp = GetComponent(selectedRoot.Target, C("Unity.TextMeshPro.dll", "TMPro", "TMP_InputField"));
                    textEditing |= Alive(tmp) && _api.Unbox<bool>(_api.Call(tmp, "get_isFocused"));
                }
                catch (MissingMemberException) { }
            }
        }
        return textEditing;
    }
    private NativeRuntime.RootHandle? CurrentEvents()
    { var events = _api.CallStatic(_eventSystem, "get_current"); return Alive(events) ? _api.Root(events) : null; }
    public IUiNativeWindow Create(string qualifiedId, UiWindowDefinition definition)
    {
        _main.VerifyAccess(); if (_disposed) throw new ObjectDisposedException(nameof(NativeUiInputBackend));
        if (CurrentEvents() is not { } events) throw new UiBackendException(ErrorCode.NotReady, "Existing game EventSystem is required; this module does not replace it.");
        using (events) if (!Alive(_api.Call(events.Target, "get_currentInputModule"))) throw new UiBackendException(ErrorCode.NotReady, "Game EventSystem has no current input module.");
        using var canvas = definition.MountId == UiMountIds.Overlay ? OverlayCanvas() : definition.MountId == UiMountIds.MainMenu ? MenuCanvas() : throw new NotSupportedException("Unknown mount.");
        _api.CallStatic(_canvasType, "ForceUpdateCanvases");
        return new NativeWindow(this, qualifiedId, definition, _api.Root(canvas.Target));
    }
    private NativeRuntime.RootHandle OverlayCanvas()
    {
        if (_overlayCleanupUnconfirmed) throw new UiBackendException(ErrorCode.NotReady, "Previous overlay cleanup is unconfirmed.");
        if (_overlayCanvas is { } current) return _api.Root(current.Target);
        try
        {
            _overlayGo = NewGo("RizToolbox.OwnedOverlay");
            _overlayCanvas = _api.Root(Component(_overlayGo.Target, _canvasType));
            _api.Call(_overlayCanvas.Target, "set_renderMode", A.Value(0));
            _api.Call(_overlayCanvas.Target, "set_sortingOrder", A.Value(31000));
            Component(_overlayGo.Target, C("UnityEngine.UI.dll", "UnityEngine.UI", "GraphicRaycaster"));
            _api.CallStatic(_object, "DontDestroyOnLoad", A.Ref(_overlayGo.Target));
            return _api.Root(_overlayCanvas.Target);
        }
        catch
        {
            ReleaseOverlay();
            throw;
        }
    }
    private void ReleaseOverlay()
    {
        if (_overlayCleanupUnconfirmed) throw new InvalidOperationException("Previous overlay cleanup is unconfirmed; retained roots require process exit.");
        if (_overlayGo is not { } go) return;
        try
        {
            if (Alive(go.Target)) _api.CallStatic(_object, "Destroy", A.Ref(go.Target));
            _overlayCanvas?.Dispose(); _overlayCanvas = null;
            go.Dispose(); _overlayGo = null;
        }
        catch { _overlayCleanupUnconfirmed = true; throw; }
    }
    private IDisposable RegisterButtonActivation(IntPtr button, Action handler)
        => (_buttonActivation ??= new(_api, _main, _log ?? throw new UnsupportedSignatureException("Owned button activation requires a diagnostic logger."), _developerDiagnostics))
            .Register(button, handler);
    private string? NavigationReason()
    {
        if (_navigationUnsupported is not null) return "Unsupported navigation signature: " + _navigationUnsupported;
        if (!IsMainMenuScene(SceneName)) return "Current scene must be MainMenu or FakeMainMenu.";
        using var menu = Menu();
        if (!_api.Unbox<bool>(_api.Call(menu.Target, "get_Working")) || !_api.Read<bool>(menu.Target, "allowMove") || _api.Read<bool>(menu.Target, "opLock")) return "Main menu is not working/movable, or its operation lock is held.";
        using var manager = _api.Root(_api.ReadStatic<IntPtr>(_managerType, "Instance")); using var sceneManager = _api.Root(_api.Read<IntPtr>(manager.Target, "sceneManager"));
        return _api.Read<bool>(sceneManager.Target, "_lock") ? "Game scene transition lock is held." : null;
    }
    public IReadOnlyList<GameNavigationTarget> GetNavigationTargets()
    {
        _main.VerifyAccess(); string? reason;
        try { reason = NavigationReason(); } catch (Exception ex) { reason = ex.Message; }
        return Array.AsReadOnly(new[] { new GameNavigationTarget(ActivateSelected, reason is null, reason,
            "GameManager.Instance.mainMenuControl -> MainMenuControl.EnterCurrentButton():System.Void [instance]",
            Array.AsReadOnly(new[] { "MainMenu or FakeMainMenu scene; live loaded active main menu", "Working=true; allowMove=true; opLock=false", "GameManager.sceneManager._lock=false", "No framework modal/text focus; original game predicates/unlocks remain authoritative" }), false) });
    }
    public Result Navigate(string id)
    {
        _main.VerifyAccess();
        if (_disposed) return Result.Fail(new(ErrorCode.ScopeExpired, "Navigation backend expired."));
        if (id != ActivateSelected) return Result.Fail(new(ErrorCode.OperationUnsupported, "Only activation of the currently selected original main-menu entry is adapted.", UiInputCapabilityIds.Navigation, id));
        try
        {
            var reason = NavigationReason(); if (reason is not null) return Result.Fail(new(ErrorCode.NavigationUnavailable, reason, UiInputCapabilityIds.Navigation, id, true));
            using var menu = Menu(); _api.CallTyped(menu.Target, "EnterCurrentButton", Array.Empty<string>()); return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(new(ErrorCode.NavigationUnavailable, ex.Message, UiInputCapabilityIds.Navigation, id, true)); }
    }
    private NativeRuntime.RootHandle NewGo(string name) => _api.Root(_api.New(C("UnityEngine.CoreModule.dll", "UnityEngine", "GameObject"), _api.Str(name)));
    private IntPtr Component(IntPtr go, IntPtr type) => _api.CallTyped(go, "AddComponent", new[] { "System.Type" }, A.Ref(_api.Type(type)));
    private IntPtr GetComponent(IntPtr go, IntPtr type) => _api.CallTyped(go, "GetComponent", new[] { "System.Type" }, A.Ref(_api.Type(type)));
    private IntPtr Transform(IntPtr go) => _api.Call(go, "get_transform");
    private CanvasProjectionFrame ProjectCanvas(NativeRuntime.RootHandle canvas, bool ownedOverlay)
    {
        _main.VerifyAccess(); var key = canvas.Target;
        if (_canvasProjections.TryGetValue(key, out var cached)) return cached;
        var api = _api;
        // Unity may not have performed the first layout pass when a plug-in creates its first window.
        api.CallStatic(_canvasType, "ForceUpdateCanvases");
        using var transform = api.Root(api.Call(canvas.Target, "get_transform"));
        var rect = api.Unbox<NativeRect>(api.Call(transform.Target, "get_rect"));
        var local = new UiRect(rect.X, rect.Y, rect.Width, rect.Height);
        if (!UiCoordinates.Valid(local) || !float.IsFinite(rect.X + rect.Width) || !float.IsFinite(rect.Y + rect.Height))
            throw new UnsupportedSignatureException("Canvas local rectangle is not finite and positive.");
        var nativePixels = api.Unbox<NativeRect>(api.Call(canvas.Target, "get_pixelRect"));
        var pixels = new UiRect(nativePixels.X, nativePixels.Y, nativePixels.Width, nativePixels.Height);
        var width = api.Unbox<int>(api.CallStatic(_screen, "get_width"));
        var height = api.Unbox<int>(api.CallStatic(_screen, "get_height"));
        float? dpi = null; try { dpi = api.Unbox<float>(api.CallStatic(_screen, "get_dpi")); } catch (MissingMemberException) { }
        CanvasProjection projection;
        if (ownedOverlay)
        {
            // This Canvas is created by this backend as a screen-space overlay with no inherited game transforms.
            if (!UiCoordinates.Valid(local) || !UiCoordinates.Valid(pixels) ||
                Math.Abs(local.Width / local.Height - pixels.Width / pixels.Height) > .02f)
                throw new UnsupportedSignatureException("Owned overlay no longer has its verified uniform screen-space projection.");
            projection = CanvasProjection.Create(local, pixels, new(0, 0, width, height), width, height,
                new(pixels.X, pixels.Y + pixels.Height, 1), new(pixels.X + pixels.Width, pixels.Y + pixels.Height, 1),
                new(pixels.X, pixels.Y, 1), new(pixels.X + pixels.Width, pixels.Y, 1));
            if (Math.Abs(projection.RootScaleX - 1) > .00001f)
                throw new UnsupportedSignatureException("Owned overlay acquired an unexpected nonuniform unit scale.");
        }
        else
        {
            if (_menuUnsupported is not null || _canvasProjectionUnsupported is not null)
                throw new UnsupportedSignatureException(_menuUnsupported ?? _canvasProjectionUnsupported!);
            var mode = api.Unbox<int>(api.Call(canvas.Target, "get_renderMode"));
            if (mode is not (0 or 1)) throw new UnsupportedSignatureException("World-space menu Canvas projection is not supported.");
            using var camera = mode == 1 ? api.Root(api.Call(canvas.Target, "get_worldCamera")) : null;
            var cameraPixels = new UiRect(0, 0, width, height);
            if (camera is not null)
            {
                if (!Alive(camera.Target)) throw new TargetExpiredException("Menu Canvas camera expired.");
                if (api.Call(camera.Target, "get_targetTexture") != IntPtr.Zero || api.Unbox<int>(api.Call(camera.Target, "get_targetDisplay")) != 0)
                    throw new UnsupportedSignatureException("Menu Canvas camera must render directly to the primary screen.");
                var cameraRect = api.Unbox<NativeRect>(api.Call(camera.Target, "get_pixelRect"));
                cameraPixels = new(cameraRect.X, cameraRect.Y, cameraRect.Width, cameraRect.Height);
            }
            NativeVector3 Project(float x, float y)
            {
                var world = api.Unbox<NativeVector3>(api.Invoke(_transformPoint, transform.Target, A.Value(new NativeVector3(x, y, 0))));
                if (!float.IsFinite(world.X) || !float.IsFinite(world.Y) || !float.IsFinite(world.Z))
                    throw new UnsupportedSignatureException("Canvas transform produced a non-finite world corner.");
                var screen = api.Unbox<Vec2>(api.Invoke(_worldToScreenPoint, IntPtr.Zero, A.Ref(camera?.Target ?? IntPtr.Zero), A.Value(world)));
                var depth = camera is null ? 1 : api.Unbox<NativeVector3>(api.Invoke(_cameraWorldToScreenPoint, camera.Target, A.Value(world))).Z;
                return new(screen.X, screen.Y, depth);
            }
            projection = CanvasProjection.Create(local, pixels, cameraPixels, width, height,
                Project(rect.X, rect.Y + rect.Height), Project(rect.X + rect.Width, rect.Y + rect.Height),
                Project(rect.X, rect.Y), Project(rect.X + rect.Width, rect.Y));
        }
        var result = new CanvasProjectionFrame(projection, width, height, dpi);
        _canvasProjections[key] = result; return result;
    }
    private NativeRuntime.RootHandle NewRect(string name, IntPtr parent)
    {
        var go = NewGo(name);
        try { var rect = Component(go.Target, C("UnityEngine.CoreModule.dll", "UnityEngine", "RectTransform")); _api.CallTyped(rect, "SetParent", new[] { "UnityEngine.Transform", "System.Boolean" }, A.Ref(parent), A.Value(false)); return go; }
        catch { Destroy(go); throw; }
    }
    private void Place(IntPtr go, float x, float y, float width, float height)
    {
        var rect = Transform(go); _api.Call(rect, "set_anchorMin", A.Value(new Vec2(0, 1))); _api.Call(rect, "set_anchorMax", A.Value(new Vec2(0, 1)));
        _api.Call(rect, "set_pivot", A.Value(new Vec2(0, 1))); _api.Call(rect, "set_sizeDelta", A.Value(new Vec2(width, height))); _api.Call(rect, "set_anchoredPosition", A.Value(new Vec2(x, -y)));
    }
    private void Destroy(NativeRuntime.RootHandle? root)
    {
        if (root is null) return;
        try { if (Alive(root.Target)) { _api.Call(root.Target, "SetActive", A.Value(false)); _api.CallStatic(_object, "Destroy", A.Ref(root.Target)); } }
        finally { root.Dispose(); }
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return; _disposed = true;
        _inputUpdate?.Dispose(); _inputUpdate = null; _inputEdges.Dispose(); _bindingSource = () => Array.Empty<InputBindingOwner>();
        _focusSource = () => Array.Empty<InputFocusInfo>(); _latestPointer = null;
        _canvasProjections.Clear();
        _buttonActivation?.Dispose(); _buttonActivation = null;
        ReleaseOverlay();
    }
    private sealed class NativeWindow : IUiNativeWindow
    {
        private readonly NativeUiInputBackend _owner;
        private readonly NativeRuntime.RootHandle _canvas;
        private NativeRuntime.RootHandle? _root, _panel, _blocker, _font, _previousSelection, _titleGo, _titleText;
        private readonly List<NativeControl> _controls = new();
        private readonly UiWindowDefinition _definition;
        private UiLayout _layout;
        private bool _disposed;
        private float _unitsPerReference;
        private UiViewport? _appliedViewport;
        private UiLayout? _appliedLayout;
        private CanvasProjection? _appliedProjection;
        public bool Alive => !_disposed && _root is not null && _owner.Alive(_root.Target) && (_definition.MountId == UiMountIds.Overlay || _owner.Active(_canvas.Target));
        internal NativeWindow(NativeUiInputBackend owner, string id, UiWindowDefinition definition, NativeRuntime.RootHandle canvas)
        {
            _owner = owner; _canvas = canvas; _definition = definition; _layout = definition.Layout;
            try
            {
                _root = owner.NewRect("RizToolbox.Window." + id, owner._api.Call(canvas.Target, "get_transform"));
                if (definition.Modal) { _blocker = owner.NewRect("ModalBlocker", owner.Transform(_root.Target)); Image(_blocker.Target, new(0, 0, 0, .55f)); }
                _panel = owner.NewRect("Panel", owner.Transform(_root.Target)); Image(_panel.Target, definition.Theme?.Background ?? new(.06f, .08f, .1f, .97f));
                var fontType = owner.C("UnityEngine.TextRenderingModule.dll", "UnityEngine", "Font");
                _font = owner._api.Root(owner._api.CallStatic(owner._resources, "GetBuiltinResource", A.Ref(owner._api.Type(fontType)), owner._api.Str(definition.Theme?.Font ?? "LegacyRuntime.ttf")));
                _titleGo = owner.NewRect("WindowTitle", owner.Transform(_panel.Target)); _titleText = owner._api.Root(Text(_titleGo.Target, definition.Title));
                Layout(_layout);
            }
            catch { Dispose(); throw; }
        }
        public UiViewport Viewport
        {
            get => _owner.ProjectCanvas(_canvas, _definition.MountId == UiMountIds.Overlay).Fit(_layout);
        }
        public void Layout(UiLayout layout)
        {
            if (!Alive) throw new TargetExpiredException("Owned window or mount expired."); _layout = layout;
            var api = _owner._api; var frame = _owner.ProjectCanvas(_canvas, _definition.MountId == UiMountIds.Overlay);
            var projection = frame.Projection; var view = frame.Fit(layout);
            if (_appliedLayout == layout && _appliedViewport == view && _appliedProjection == projection) return;
            var unitsPerPixel = projection.UnitsPerPixel; _unitsPerReference = view.PixelsPerReferenceUnit * unitsPerPixel;
            _owner.Place(_root!.Target, projection.RootX, projection.RootY, projection.RootWidth, projection.RootHeight);
            if (_definition.MountId != UiMountIds.Overlay)
                api.CallTyped(_owner.Transform(_root.Target), "set_localScale", new[] { "UnityEngine.Vector3" }, A.Value(new NativeVector3(projection.RootScaleX, 1, 1)));
            if (_blocker is not null) _owner.Place(_blocker.Target, 0, 0, projection.RootWidth, projection.RootHeight);
            var area = layout.Rect; _owner.Place(_panel!.Target, view.LetterboxX * unitsPerPixel + area.X * _unitsPerReference,
                view.LetterboxY * unitsPerPixel + area.Y * _unitsPerReference, area.Width * _unitsPerReference, area.Height * _unitsPerReference);
            _owner.Place(_titleGo!.Target, 12 * _unitsPerReference, 8 * _unitsPerReference, Math.Max(1, (area.Width - 24) * _unitsPerReference), 32 * _unitsPerReference);
            api.Call(_titleText!.Target, "set_fontSize", A.Value(Math.Max(1, (int)Math.Round((_definition.Theme?.FontSize ?? 20) * _unitsPerReference))));
            foreach (var control in _controls) control.Place(_unitsPerReference);
            (_appliedLayout, _appliedViewport, _appliedProjection) = (layout, view, projection);
        }
        private IntPtr Image(IntPtr go, ResourceColor color)
        {
            var image = _owner.Component(go, _owner.C("UnityEngine.UI.dll", "UnityEngine.UI", "Image"));
            _owner._api.Call(image, "set_color", A.Value(new Rgba(color.R, color.G, color.B, color.A))); _owner._api.Call(image, "set_raycastTarget", A.Value(true)); return image;
        }
        private IntPtr Text(IntPtr go, string value)
        {
            var api = _owner._api; var text = _owner.Component(go, _owner.C("UnityEngine.UI.dll", "UnityEngine.UI", "Text"));
            api.Call(text, "set_font", A.Ref(_font!.Target)); api.Call(text, "set_fontSize", A.Value(_definition.Theme?.FontSize ?? 20));
            var color = _definition.Theme?.Foreground ?? new(1, 1, 1, 1); api.Call(text, "set_color", A.Value(new Rgba(color.R, color.G, color.B, color.A)));
            api.Call(text, "set_raycastTarget", A.Value(false)); api.Call(text, "set_text", api.Str(value)); return text;
        }
        public IUiNativeControl Add(UiControlKind kind, UiControlDefinition definition, int characterLimit)
        {
            var owner = _owner; var api = owner._api; NativeRuntime.RootHandle? go = null, textGo = null, component = null;
            try
            {
                go = owner.NewRect(definition.Id, owner.Transform(_panel!.Target)); IntPtr value;
                if (kind == UiControlKind.Label) value = Text(go.Target, definition.Text);
                else
                {
                    var image = Image(go.Target, kind == UiControlKind.Button ? new(.16f, .28f, .38f, 1) : new(.12f, .14f, .17f, 1));
                    var control = owner.Component(go.Target, owner.C("UnityEngine.UI.dll", "UnityEngine.UI", kind == UiControlKind.Button ? "Button" : "InputField"));
                    component = api.Root(control); api.Call(control, "set_targetGraphic", A.Ref(image));
                    textGo = owner.NewRect("Text", owner.Transform(go.Target)); value = Text(textGo.Target, definition.Text);
                    if (kind == UiControlKind.TextField)
                    { api.Call(control, "set_textComponent", A.Ref(value)); api.Call(control, "set_characterLimit", A.Value(characterLimit)); api.Call(control, "set_text", api.Str(definition.Text)); value = control; }
                }
                var result = new NativeControl(this, go, textGo, component, api.Root(value), kind, definition.Rect); go = textGo = component = null;
                _controls.Add(result); try { result.InitializeActivation(); result.Place(_unitsPerReference); return result; } catch { result.Dispose(); throw; }
            }
            catch { component?.Dispose(); if (textGo is not null) owner.Destroy(textGo); if (go is not null) owner.Destroy(go); throw; }
        }
        private bool Contains(IntPtr go, UiPoint screen)
        {
            var api = _owner._api; var camera = api.Unbox<int>(api.Call(_canvas.Target, "get_renderMode")) == 0 ? IntPtr.Zero : api.Call(_canvas.Target, "get_worldCamera");
            return api.Unbox<bool>(api.CallStatic(_owner._rectUtility, "RectangleContainsScreenPoint", A.Ref(_owner.Transform(go)), A.Value(new Vec2(screen.X, screen.Y)), A.Ref(camera)));
        }
        public bool HitTest(UiPoint screen) => Alive && Contains((_blocker ?? _panel)!.Target, screen);
        public void Focus()
        {
            using var events = _owner.CurrentEvents(); if (events is null) throw new TargetExpiredException("Game EventSystem is unavailable.");
            var api = _owner._api;
            var selected = api.Call(events.Target, "get_currentSelectedGameObject");
            if (!OwnsSelection(selected))
            {
                DetachFocus(false); _previousSelection?.Dispose(); _previousSelection = _owner.Alive(selected) ? api.Root(selected) : null;
            }
            _owner._focusWindows.Remove(this); _owner._focusWindows.Add(this);
            api.Call(_owner.Transform(_root!.Target), "SetAsLastSibling"); api.CallTyped(events.Target, "SetSelectedGameObject", new[] { "UnityEngine.GameObject" }, A.Ref(_panel!.Target));
        }
        private bool OwnsSelection(IntPtr selected)
            => _root is not null && _owner.Alive(_root.Target) && _owner.Alive(selected) && _owner._api.Unbox<bool>(_owner._api.CallTyped(_owner.Transform(selected), "IsChildOf", new[] { "UnityEngine.Transform" }, A.Ref(_owner.Transform(_root.Target))));
        private void DetachFocus(bool restore)
        {
            var index = _owner._focusWindows.IndexOf(this);
            if (index >= 0)
            {
                if (index + 1 < _owner._focusWindows.Count)
                {
                    var next = _owner._focusWindows[index + 1];
                    if (next._previousSelection is not null && OwnsSelection(next._previousSelection.Target))
                    { next._previousSelection.Dispose(); next._previousSelection = _previousSelection; _previousSelection = null; }
                }
                _owner._focusWindows.RemoveAt(index);
            }
            if (!restore) return;
            using var events = _owner.CurrentEvents();
            if (events is not null && OwnsSelection(_owner._api.Call(events.Target, "get_currentSelectedGameObject")))
            {
                var previous = _previousSelection is not null && _owner.Alive(_previousSelection.Target) ? _previousSelection.Target : IntPtr.Zero;
                _owner._api.CallTyped(events.Target, "SetSelectedGameObject", new[] { "UnityEngine.GameObject" }, A.Ref(previous));
            }
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try
            {
                DetachFocus(true);
            }
            finally
            {
                foreach (var control in _controls.ToArray()) try { control.Dispose(); } catch { }
                try { _owner.Destroy(_root); } finally { _owner._canvasProjections.Remove(_canvas.Target); _root = null; _panel?.Dispose(); _blocker?.Dispose(); _font?.Dispose(); _titleGo?.Dispose(); _titleText?.Dispose(); _previousSelection?.Dispose(); _canvas.Dispose(); }
            }
        }
        private sealed class NativeControl : IUiNativeControl
        {
            private readonly NativeWindow _window;
            private readonly NativeRuntime.RootHandle _go, _value;
            private readonly NativeRuntime.RootHandle? _textGo, _component;
            private readonly UiControlKind _kind;
            private readonly UiRect _rect;
            private bool _disposed;
            private IDisposable? _activation;
            private int _pendingActivations;
            internal NativeControl(NativeWindow window, NativeRuntime.RootHandle go, NativeRuntime.RootHandle? textGo, NativeRuntime.RootHandle? component,
                NativeRuntime.RootHandle value, UiControlKind kind, UiRect rect)
                => (_window, _go, _textGo, _component, _value, _kind, _rect) = (window, go, textGo, component, value, kind, rect);
            internal void InitializeActivation()
            {
                if (_kind == UiControlKind.Button) _activation = Owner.RegisterButtonActivation(_component!.Target, () =>
                {
                    if (!_disposed && Alive && Owner.Active(_component.Target) && Owner._api.Unbox<bool>(Owner._api.Call(_component.Target, "IsInteractable")))
                        _pendingActivations = Math.Min(8, _pendingActivations + 1);
                });
            }
            public bool UsesNativeActivation => _kind == UiControlKind.Button;
            public int DrainActivations() { var count = _pendingActivations; _pendingActivations = 0; return count; }
            private NativeUiInputBackend Owner => _window._owner;
            public bool Alive => !_disposed && Owner.Alive(_go.Target) && Owner.Alive(_value.Target);
            public bool IsFocused => _kind == UiControlKind.TextField && Alive && Owner._api.Unbox<bool>(Owner._api.Call(_value.Target, "get_isFocused"));
            public string Text { get => Owner._api.Text(Owner._api.Call(_value.Target, "get_text")); set => Owner._api.Call(_value.Target, "set_text", Owner._api.Str(value)); }
            public bool HitTest(UiPoint screen) => Alive && _window.Contains(_go.Target, screen);
            internal void Place(float units)
            {
                if (!Alive) return; Owner.Place(_go.Target, _rect.X * units, _rect.Y * units, _rect.Width * units, _rect.Height * units);
                if (_textGo is not null) Owner.Place(_textGo.Target, 8 * units, 4 * units, Math.Max(1, (_rect.Width - 16) * units), Math.Max(1, (_rect.Height - 8) * units));
                if (_kind == UiControlKind.Label) Owner._api.Call(_value.Target, "set_fontSize", A.Value(Math.Max(1, (int)Math.Round((_window._definition.Theme?.FontSize ?? 20) * units))));
                else if (_textGo is not null)
                {
                    var text = _kind == UiControlKind.TextField ? Owner._api.Call(_value.Target, "get_textComponent") : _value.Target;
                    Owner._api.Call(text, "set_fontSize", A.Value(Math.Max(1, (int)Math.Round((_window._definition.Theme?.FontSize ?? 20) * units))));
                }
            }
            public void Focus()
            {
                if (_kind != UiControlKind.TextField) throw new NotSupportedException("Only InputField accepts focus.");
                using var events = Owner.CurrentEvents(); if (events is null) throw new TargetExpiredException("Game EventSystem disappeared.");
                Owner._api.CallTyped(events.Target, "SetSelectedGameObject", new[] { "UnityEngine.GameObject" }, A.Ref(_go.Target)); Owner._api.Call(_value.Target, "ActivateInputField");
            }
            public void Dispose()
            {
                if (_disposed) return; _disposed = true; _activation?.Dispose(); _activation = null; _pendingActivations = 0; _window._controls.Remove(this);
                try { Owner.Destroy(_go); } finally { _textGo?.Dispose(); _component?.Dispose(); _value.Dispose(); }
            }
        }
    }
}
