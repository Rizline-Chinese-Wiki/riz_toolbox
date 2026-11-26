namespace RizToolbox.Sdk;

/// <summary>Additive experimental services. All operations and callbacks run on the Unity main thread.</summary>
public static class UiInputCapabilityIds
{
    public const string Windows = "riz.ui.windows";
    public const string Actions = "riz.input.actions";
    public const string Navigation = "riz.navigation.menu";
    public const string OfficialChartSelection = "riz.navigation.official-chart-selection";
}
public static class UiMountIds
{
    public const string Overlay = "framework.overlay";
    public const string MainMenu = "game.main-menu.canvas";
}
/// <summary>Coordinates use reference pixels, origin at the upper left, positive Y downwards.</summary>
public sealed record UiRect(float X, float Y, float Width, float Height);
public sealed record UiPoint(float X, float Y);
public sealed record UiLayout(UiRect Rect, float ReferenceWidth = 1920, float ReferenceHeight = 1080);
public sealed record UiTheme(string Font = "LegacyRuntime.ttf", int FontSize = 20,
    ResourceColor? Foreground = null, ResourceColor? Background = null);
public sealed record UiWindowDefinition(string Id, string MountId, string Title, UiLayout Layout,
    bool Modal = false, UiTheme? Theme = null);
public sealed record UiControlDefinition(string Id, string Text, UiRect Rect);
public sealed record UiMountInfo(string Id, bool Available, string? Scene, string? Reason,
    long Generation, string Ownership, ErrorCode? FailureCode = null);
/// <summary>Screen coordinates are physical pixels with a lower-left origin. DPI is reported only, never assumed.</summary>
public sealed record UiViewport(float ScreenWidth, float ScreenHeight, float? Dpi,
    float PixelX, float PixelY, float PixelWidth, float PixelHeight,
    float ReferenceWidth, float ReferenceHeight, float PixelsPerReferenceUnit,
    float LetterboxX, float LetterboxY);
public interface IUiControlHandle : IDisposable
{
    string Id { get; }
    Result SetText(string text);
    Result<string> GetText();
    /// <summary>Only text fields accept focus. Other controls return OperationUnsupported.</summary>
    Result Focus();
}
public interface IUiWindowHandle : IDisposable
{
    string Id { get; }
    bool IsAlive { get; }
    Result<IUiControlHandle> AddLabel(UiControlDefinition definition);
    /// <summary>The owned native UGUI button's semantic activation queues a main-thread callback.
    /// Delivery rechecks window/scope/modal ownership; device mappings are reported separately.</summary>
    Result<IUiControlHandle> AddButton(UiControlDefinition definition, Action clicked);
    /// <summary>Native UGUI InputField uses the game's existing EventSystem; changes are observed on the next host frame.</summary>
    Result<IUiControlHandle> AddTextField(UiControlDefinition definition, Action<string>? changed = null, int characterLimit = 1024);
    Result Focus();
    Result SetLayout(UiLayout layout);
    Result<UiViewport> GetViewport();
    Result<UiPoint> ScreenToUi(UiPoint screenPixels);
    Result<UiPoint> UiToScreen(UiPoint referencePixels);
}
public interface IOwnedUiService
{
    IReadOnlyList<UiMountInfo> GetMounts();
    /// <summary>Scope owns the window and every control. Every scene boundary destroys windows, including overlay windows.</summary>
    Result<IUiWindowHandle> CreateWindow(UiWindowDefinition definition);
}

public enum InputDeviceKind { Unknown, Keyboard, Mouse, Gamepad }
public enum InputSupport { Unsupported, SymbolVerified, RuntimeAvailable }
[Flags] public enum InputActionContext { None = 0, Global = 1, Menu = 2, Gameplay = 4 }
/// <summary>Case-sensitive declared control name. Keyboard uses Unity KeyCode names; mouse uses Left/Right/Middle.
/// Single-control edges only; no modifier chords or injection. Gamepad uses positional ButtonSouth/East/West/North,
/// Left/RightShoulder, Left/RightTrigger, Left/RightStickButton, Start, Select and DpadUp/Down/Left/Right.
/// Gamepad triggers are digital Input System press-threshold edges, not analog values.</summary>
public sealed record InputBinding(InputDeviceKind Device, string Control);
public sealed record InputDeviceInfo(InputDeviceKind Device, InputSupport Support, string Mapping, string? Reason);
public sealed record InputActionDefinition(string Id, InputBinding Binding, InputActionContext Context = InputActionContext.Global,
    string? WindowId = null);
public sealed record InputBindingOwner(string PluginId, string ActionId, InputBinding Binding, InputActionContext Context, string? WindowId);
public sealed record InputActionEvent(string ActionId, InputBinding Binding, InputActionContext Context, long Frame);
public sealed record InputFocusInfo(string PluginId, string WindowId, bool Modal, bool TextEditing);
public interface IInputActionHandle : IDisposable
{
    string Id { get; }
    Result Rebind(InputBinding binding);
}
public interface IInputActionService
{
    IReadOnlyList<InputDeviceInfo> GetDevices();
    IReadOnlyList<InputBindingOwner> GetBindings();
    IReadOnlyList<InputFocusInfo> GetFocusStack();
    /// <summary>Conflicting bindings in overlapping contexts are refused with all owners in the error message.</summary>
    Result<IInputActionHandle> Register(InputActionDefinition definition, Action<InputActionEvent> callback);
}
public sealed record GameNavigationTarget(string Id, bool Available, string? UnavailableReason,
    string NativeMethod, IReadOnlyList<string> Preconditions, bool ChangesGameplay);
public interface IGameNavigationService
{
    IReadOnlyList<GameNavigationTarget> GetTargets();
    /// <summary>One-way command; success means the native menu command was accepted, not that its asynchronous destination loaded.</summary>
    Result Navigate(string targetId);
    /// <summary>Submits the stock Play button only while the observed chart ID, scene, list and card
    /// still match. Button focus and availability are checked again at dispatch. Success means the
    /// original button handler returned; observe the run to confirm gameplay started.</summary>
    Result SubmitOfficialPlay(OfficialChartSelectionSnapshot expected)
        => Result.Fail(new(ErrorCode.OperationUnsupported, "This navigation provider cannot submit an observed chart.",
            UiInputCapabilityIds.Navigation));
}

/// <summary>Copied state of the selected official card in LevelList. ChartId uses the official catalog identity.
/// The Unity scene handle and instance IDs are process-local observations. PlayButton can be absent even when a chart is selected.</summary>
public sealed record OfficialChartSelectionSnapshot(string ChartId, int SceneHandle, int LevelListInstanceId,
    int LevelCardInstanceId, OfficialChartPlayButtonSnapshot? PlayButton);

/// <summary>Ordinary UGUI state for the uniquely verified stock Play button. Focused means that the current
/// EventSystem selects this button's GameObject. These facts do not grant play, unlock or pointer reachability.</summary>
public sealed record OfficialChartPlayButtonSnapshot(int InstanceId, bool Active, bool Interactable, bool Focused);

public interface IOfficialChartSelectionService
{
    /// <summary>Reads the current LevelList chart on demand on the Unity main thread, independently of UI focus.
    /// PlayButton is supplied only when the current EventSystem and uniquely attributable stock listener can be verified.</summary>
    Result<OfficialChartSelectionSnapshot> ReadCurrent();
}
