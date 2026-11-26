using RizToolbox.Core.Input;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.UiInput;

internal enum UiControlKind { Label, Button, TextField }
internal sealed class UiBackendException : InvalidOperationException
{
    internal ErrorCode Code { get; }
    internal UiBackendException(ErrorCode code, string message) : base(message) => Code = code;
}
internal sealed record UiInputFrame(IReadOnlyCollection<InputBinding> Pressed, UiPoint? Pointer, bool LeftDown, bool LeftUp,
    bool ExternalTextEditing, InputActionContext Context)
{
    // Null preserves the frame-based contract of portable/fake backends. Native input uses explicit copied updates.
    internal IReadOnlyList<InputUpdateSample>? Updates { get; init; }
}
internal interface IUiNativeControl : IDisposable
{
    bool UsesNativeActivation => false;
    int DrainActivations() => 0;
    bool Alive { get; }
    bool IsFocused { get; }
    string Text { get; set; }
    bool HitTest(UiPoint screen);
    void Focus();
}
internal interface IUiNativeWindow : IDisposable
{
    bool Alive { get; }
    bool HitTest(UiPoint screen);
    UiViewport Viewport { get; }
    void Layout(UiLayout layout);
    IUiNativeControl Add(UiControlKind kind, UiControlDefinition definition, int characterLimit);
    void Focus();
}
internal interface IUiInputBackend : IDisposable
{
    string SceneIdentity { get; }
    IReadOnlyList<UiMountInfo> GetMounts();
    IReadOnlyList<InputDeviceInfo> GetDevices();
    IUiNativeWindow Create(string qualifiedId, UiWindowDefinition definition);
    UiInputFrame Capture(IReadOnlyList<InputBindingOwner> bindings);
    void SetInputBindingSource(Func<IReadOnlyList<InputBindingOwner>> source) { }
    void SetInputFocusSource(Func<IReadOnlyList<InputFocusInfo>> source) { }
    void InvalidateInputAction(string actionId) { }
    bool IsCurrentInputAction(InputActionEdge action) => true;
    void ClearInput() { }
    IReadOnlyList<GameNavigationTarget> GetNavigationTargets();
    Result Navigate(string id);
    Result SubmitOfficialPlay(OfficialChartSelectionSnapshot expected)
        => Result.Fail(new(ErrorCode.OperationUnsupported, "This adapter cannot submit an observed chart.",
            UiInputCapabilityIds.Navigation));
    Result<OfficialChartSelectionSnapshot> ReadOfficialChartSelection()
        => Result<OfficialChartSelectionSnapshot>.Fail(new(ErrorCode.OperationUnsupported,
            "This adapter has no official chart selection reader.", UiInputCapabilityIds.OfficialChartSelection));
}

/// <summary>Shared production ownership/dispatch path. The native backend is the only part that touches Unity objects.</summary>
internal sealed class UiInputService : IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly IUiInputBackend _backend;
    private readonly InputActionBroker _input;
    private readonly Dictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private string _scene;
    private bool _disposed;
    internal bool IsDisposed => _disposed;
    private long _order;
    internal UiInputService(IMainThreadDispatcher main, ILogger log, IUiInputBackend backend)
    {
        _main = main; _log = log; _backend = backend; _input = new(main, log); _main.VerifyAccess(); _scene = backend.SceneIdentity;
        backend.SetInputBindingSource(_input.GetBindings);
        backend.SetInputFocusSource(_input.GetFocusStack);
    }
    internal View ForPlugin(IPluginContext context) => context.Own(new View(this, context));
    internal void Tick()
    {
        _main.VerifyAccess(); if (_disposed) return;
        var scene = _backend.SceneIdentity;
        if (scene != _scene) { _scene = scene; foreach (var window in _windows.Values.ToArray()) Cleanup(window); _input.ClearFocus(); _backend.ClearInput(); }
        foreach (var window in _windows.Values.ToArray()) if (!window.IsAlive) Cleanup(window);
        var frame = _backend.Capture(_input.GetBindings());
        if (frame.Updates is not null)
        {
            if (frame.Updates.Count == 0) DispatchFrame(frame with { Pressed = Array.Empty<InputBinding>(), LeftDown = false, LeftUp = false });
            foreach (var update in frame.Updates)
            {
                if (_disposed) return;
                if (_backend.SceneIdentity != _scene) { _backend.ClearInput(); return; }
                DispatchFrame(frame with { Pressed = Array.Empty<InputBinding>(), Pointer = update.Pointer,
                    LeftDown = update.LeftDown, LeftUp = update.LeftUp, Updates = null }, update);
            }
            return;
        }
        DispatchFrame(frame);
    }
    private void DispatchFrame(UiInputFrame frame, InputUpdateSample? update = null)
    {
        var scene = _scene;
        var pointerWindow = frame.Pointer is null ? null : _windows.Values.Where(MayInteract).OrderByDescending(x => x.OverlayLayer).ThenByDescending(x => x.Order).FirstOrDefault(x => x.HitTest(frame.Pointer));
        foreach (var window in _windows.Values.ToArray())
        {
            try { window.Tick(frame, ReferenceEquals(window, pointerWindow)); }
            catch (Exception ex) { Report(LogLevel.Warning, "Owned window retired after backend failure: " + window.Id, ex); Cleanup(window); }
        }
        if (_disposed) return;
        if (_backend.SceneIdentity != scene) { _backend.ClearInput(); return; }
        IReadOnlyCollection<InputBinding> keys = frame.Pressed;
        if (update is not null)
        {
            // Window/text callbacks above can replace an action or rebind it. Validate the
            // captured registration epoch immediately before broker dispatch, not before UI callbacks.
            var current = _input.GetBindings();
            keys = update.Actions.Where(x => _backend.IsCurrentInputAction(x) && current.Contains(x.Owner) &&
                (x.Owner.Context.HasFlag(InputActionContext.Global) || (x.Owner.Context & frame.Context) != 0))
                .Select(x => x.Owner.Binding).Distinct().ToArray();
        }
        // A shortcut can change the active scene before the next callback in this update.
        _input.Dispatch(pointerWindow is null ? keys : keys.Where(x => x.Device != InputDeviceKind.Mouse).ToArray(),
            frame.Context, frame.ExternalTextEditing, owner =>
                (update is null || update.Actions.Any(edge => edge.Owner == owner && _backend.IsCurrentInputAction(edge))) &&
                _backend.SceneIdentity == scene);
    }
    private void Cleanup(Window window) { try { window.Dispose(); } catch (Exception ex) { Report(LogLevel.Warning, "Owned UI cleanup failed: " + window.Id, ex); } }
    private void Report(LogLevel level, string message, Exception? ex = null) { try { _log.Log(level, message, ex); } catch { } }
    internal void VerifyThread() => _main.VerifyAccess();
    private bool MayInteract(Window window)
    {
        var stack = _input.GetFocusStack(); var modal = stack.LastOrDefault(x => x.Modal);
        return modal is null || modal.WindowId == window.Id;
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return; _disposed = true;
        foreach (var window in _windows.Values.ToArray()) Cleanup(window);
        try { _input.Dispose(); } finally { _backend.Dispose(); }
    }
    internal sealed class View : IOwnedUiService, IInputActionService, IGameNavigationService, IOfficialChartSelectionService, IDisposable
    {
        private readonly UiInputService _owner;
        private readonly IPluginContext _context;
        private readonly List<IDisposable> _owned = new();
        private readonly string _scope = Guid.NewGuid().ToString("N");
        private bool _disposed;
        internal string PluginId => _context.PluginId;
        internal View(UiInputService owner, IPluginContext context) => (_owner, _context) = (owner, context);
        internal bool Active
        {
            get { try { _context.MainThread.VerifyAccess(); return !_disposed && !_owner._disposed && (_context is not IScopedPluginContext scoped || !scoped.Lifetime.IsCancellationRequested); } catch { return false; } }
        }
        internal OperationError? Check()
        {
            if (!_owner._main.IsMainThread) return new(ErrorCode.WrongThread, "Owned UI/input requires the Unity main thread.");
            return Active ? null : new(ErrorCode.ScopeExpired, "Owned UI/input scope expired.");
        }
        private T Own<T>(T resource) where T : IDisposable
        { _owned.Add(resource); return resource; }
        internal int RetainedOwnedCount => _owned.Count;
        internal void Forget(IDisposable resource) => _owned.Remove(resource);
        public IReadOnlyList<UiMountInfo> GetMounts() { var error = Check(); if (error is not null) return Array.Empty<UiMountInfo>(); return _owner._backend.GetMounts(); }
        public IReadOnlyList<InputDeviceInfo> GetDevices() { if (Check() is not null) return Array.Empty<InputDeviceInfo>(); return _owner._backend.GetDevices(); }
        public IReadOnlyList<InputBindingOwner> GetBindings() { if (Check() is not null) return Array.Empty<InputBindingOwner>(); return _owner._input.GetBindings(); }
        public IReadOnlyList<InputFocusInfo> GetFocusStack() { if (Check() is not null) return Array.Empty<InputFocusInfo>(); return _owner._input.GetFocusStack(); }
        public Result<IUiWindowHandle> CreateWindow(UiWindowDefinition definition)
        {
            if (Check() is { } expired) return Result<IUiWindowHandle>.Fail(expired);
            if (definition is null || !UiLocalIds.IsValid(definition.Id) || !UiCoordinates.Valid(definition.Layout) || string.IsNullOrWhiteSpace(definition.Title) || definition.Title.Length > 512)
                return Fail<IUiWindowHandle>(ErrorCode.InvalidArgument, "Window requires a local ID, title and a finite layout inside its reference area.");
            var theme = definition.Theme ?? new();
            if (definition.Modal && definition.MountId != UiMountIds.Overlay)
                return Fail<IUiWindowHandle>(ErrorCode.OperationUnsupported, "Modal windows require framework.overlay so their blocker is above both supported mounts.");
            if (theme.Font is not ("LegacyRuntime.ttf" or "Arial.ttf") || theme.FontSize is < 8 or > 96 || !ValidColor(theme.Foreground) || !ValidColor(theme.Background))
                return Fail<IUiWindowHandle>(ErrorCode.OperationUnsupported, "Only declared built-in fonts, 8..96 font size and finite RGBA [0,1] themes are supported.");
            var id = UiLocalIds.Qualify(PluginId, definition.Id);
            if (_owner._windows.ContainsKey(id)) return Fail<IUiWindowHandle>(ErrorCode.Conflict, "Window ID already belongs to " + PluginId + ": " + id);
            if (_owner._windows.Count >= 64 || _owner._windows.Values.Count(x => x.View.PluginId == PluginId) >= 16)
                return Fail<IUiWindowHandle>(ErrorCode.QuotaExceeded, "Owned window quota exceeded.");
            var mount = _owner._backend.GetMounts().SingleOrDefault(x => x.Id == definition.MountId);
            if (mount is null) return Fail<IUiWindowHandle>(ErrorCode.OperationUnsupported, "Unknown UI mount: " + definition.MountId);
            if (!mount.Available) return Fail<IUiWindowHandle>(mount.FailureCode ?? ErrorCode.TargetMissing, mount.Reason ?? "UI mount has not appeared.");
            IUiNativeWindow? native = null; Window? window = null;
            try
            {
                native = _owner._backend.Create(id, definition); window = new(_owner, this, id, definition, native); native = null;
                _owner._windows.Add(id, window); Own(window);
                if (definition.Modal) { var focused = window.Focus(); if (!focused.Success) { window.Dispose(); return Result<IUiWindowHandle>.Fail(focused.Error!); } }
                return Result<IUiWindowHandle>.Ok(window);
            }
            catch (Exception ex) { window?.Dispose(); native?.Dispose(); return Fail<IUiWindowHandle>(FailureCode(ex), ex.Message); }
        }
        public Result<IInputActionHandle> Register(InputActionDefinition definition, Action<InputActionEvent> callback)
        {
            if (Check() is { } expired) return Result<IInputActionHandle>.Fail(expired);
            Window? actionWindow = null;
            if (definition?.WindowId is not null && (!UiLocalIds.IsValid(definition.WindowId) || !_owner._windows.TryGetValue(UiLocalIds.Qualify(PluginId, definition.WindowId), out actionWindow) || !ReferenceEquals(actionWindow.View._context, _context)))
                return Fail<IInputActionHandle>(ErrorCode.TargetMissing, "Window-scoped action requires a live window owned by this plugin context.");
            if (definition is not null)
            {
                var device = _owner._backend.GetDevices().SingleOrDefault(x => x.Device == definition.Binding?.Device);
                if (device?.Support != InputSupport.RuntimeAvailable) return Fail<IInputActionHandle>(ErrorCode.OperationUnsupported, device?.Reason ?? "Input device is unavailable.");
            }
            IInputActionHandle? retained = null;
            var result = _owner._input.Register(PluginId, _scope, definition!, callback, () => Active && (actionWindow is null || actionWindow.IsAlive), () =>
            {
                if (retained is null) return;
                _owned.Remove(retained); _owner._backend.InvalidateInputAction(retained.Id);
            });
            if (!result.Success) return result;
            _owner._backend.InvalidateInputAction(result.Value!.Id);
            retained = new ActionHandle(_owner, result.Value);
            return Result<IInputActionHandle>.Ok(Own(retained));
        }
        public IReadOnlyList<GameNavigationTarget> GetTargets() => Check() is null ? _owner._backend.GetNavigationTargets() : Array.Empty<GameNavigationTarget>();
        public Result Navigate(string targetId) => RunNavigation(targetId, () => _owner._backend.Navigate(targetId));
        public Result SubmitOfficialPlay(OfficialChartSelectionSnapshot expected)
        {
            const string target = NativeUiInputBackend.SubmitSelectedOfficialPlay;
            return RunNavigation(target, () => expected is null || string.IsNullOrWhiteSpace(expected.ChartId)
                ? Result.Fail(new(ErrorCode.InvalidArgument, "An observed official chart ID is required.", UiInputCapabilityIds.Navigation, target))
                : _owner._backend.SubmitOfficialPlay(expected));
        }
        private Result RunNavigation(string targetId, Func<Result> dispatch)
        {
            if (Check() is { } error) return Result.Fail(error);
            if (_owner._input.GetFocusStack().Any(x => x.Modal || x.TextEditing)) return Result.Fail(new(ErrorCode.NavigationUnavailable, "Close framework modal/text focus before navigating.", UiInputCapabilityIds.Navigation, targetId));
            try { return dispatch(); }
            catch (Exception ex) { return Result.Fail(new(ErrorCode.NavigationUnavailable, ex.Message, UiInputCapabilityIds.Navigation, targetId)); }
        }
        public Result<OfficialChartSelectionSnapshot> ReadCurrent()
        {
            if (Check() is { } guard) return Result<OfficialChartSelectionSnapshot>.Fail(guard with { CapabilityId = UiInputCapabilityIds.OfficialChartSelection });
            var result = _owner._backend.ReadOfficialChartSelection();
            return Check() is { } expired
                ? Result<OfficialChartSelectionSnapshot>.Fail(expired with { CapabilityId = UiInputCapabilityIds.OfficialChartSelection })
                : result;
        }
        public void Dispose()
        {
            _owner._main.VerifyAccess(); if (_disposed) return; _disposed = true;
            foreach (var owned in _owned.AsEnumerable().Reverse().ToArray())
                try { owned.Dispose(); } catch (Exception ex) { _owner.Report(LogLevel.Warning, "UI scope cleanup continued after failure.", ex); }
            _owned.Clear();
        }
    }
    private sealed class ActionHandle : IInputActionHandle
    {
        private readonly UiInputService _owner; private readonly IInputActionHandle _inner;
        internal ActionHandle(UiInputService owner, IInputActionHandle inner) => (_owner, _inner) = (owner, inner);
        public string Id => _inner.Id;
        public Result Rebind(InputBinding binding)
        {
            var result = _inner.Rebind(binding);
            if (result.Success) _owner._backend.InvalidateInputAction(Id);
            return result;
        }
        public void Dispose() => _inner.Dispose();
    }
    private sealed class Window : IUiWindowHandle
    {
        private readonly UiInputService _owner;
        internal View View { get; }
        private readonly UiWindowDefinition _definition;
        private UiLayout _layout;
        private readonly IUiNativeWindow _native;
        private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
        private IDisposable? _focus;
        private Control? _pressed;
        private int _textFocusPending;
        private bool _disposed;
        public string Id { get; }
        internal long Order { get; private set; }
        internal bool OverlayLayer => _definition.MountId == UiMountIds.Overlay;
        public bool IsAlive { get { _owner._main.VerifyAccess(); return !_disposed && View.Active && _native.Alive; } }
        internal Window(UiInputService owner, View view, string id, UiWindowDefinition definition, IUiNativeWindow native)
        { (_owner, View, Id, _definition, _native, _layout) = (owner, view, id, definition, native, definition.Layout); Order = ++owner._order; }
        internal bool HitTest(UiPoint point) => IsAlive && _native.HitTest(point);
        internal OperationError? Check() => View.Check() ?? (!IsAlive ? new(ErrorCode.TargetExpired, "UI window expired.", UiInputCapabilityIds.Windows, Id) : null);
        public Result<IUiControlHandle> AddLabel(UiControlDefinition definition) => Add(UiControlKind.Label, definition, null, null, 0);
        public Result<IUiControlHandle> AddButton(UiControlDefinition definition, Action clicked) => clicked is null ? Fail<IUiControlHandle>(ErrorCode.InvalidArgument, "Button callback is required.") : Add(UiControlKind.Button, definition, clicked, null, 0);
        public Result<IUiControlHandle> AddTextField(UiControlDefinition definition, Action<string>? changed = null, int characterLimit = 1024) => Add(UiControlKind.TextField, definition, null, changed, characterLimit);
        private Result<IUiControlHandle> Add(UiControlKind kind, UiControlDefinition definition, Action? clicked, Action<string>? changed, int limit)
        {
            if (Check() is { } error) return Result<IUiControlHandle>.Fail(error);
            if (definition is null || !UiLocalIds.IsValid(definition.Id) || !UiCoordinates.Valid(definition.Rect) || definition.Text is null || definition.Text.Length > 4096 ||
                definition.Rect.X < 0 || definition.Rect.Y < 0 || definition.Rect.X + definition.Rect.Width > _layout.Rect.Width || definition.Rect.Y + definition.Rect.Height > _layout.Rect.Height ||
                (kind == UiControlKind.TextField && (limit is < 1 or > 4096 || definition.Text.Length > limit))) return Fail<IUiControlHandle>(ErrorCode.InvalidArgument, "Control ID, text, limit or local rectangle is invalid.");
            if (_controls.ContainsKey(definition.Id)) return Fail<IUiControlHandle>(ErrorCode.Conflict, "Control ID already exists in " + Id);
            if (_controls.Count >= 128) return Fail<IUiControlHandle>(ErrorCode.QuotaExceeded, "Window control quota exceeded.");
            IUiNativeControl? native = null;
            try
            {
                native = _native.Add(kind, definition, limit); var control = new Control(this, native, definition.Id, kind, clicked, changed, limit, definition.Rect);
                _controls.Add(definition.Id, control); return Result<IUiControlHandle>.Ok(control);
            }
            catch (Exception ex) { native?.Dispose(); return Fail<IUiControlHandle>(FailureCode(ex), ex.Message); }
        }
        public Result Focus()
        {
            if (Check() is { } error) return Result.Fail(error);
            if (!_owner.MayInteract(this) && !(_definition.Modal && _focus is null)) return Result.Fail(new(ErrorCode.Conflict, "Another modal window owns framework focus."));
            try { _native.Focus(); _focus?.Dispose(); _focus = _owner._input.PushFocus(View.PluginId, Id, _definition.Modal, () => IsAlive); Order = ++_owner._order; return Result.Ok(); }
            catch (Exception ex) { return Result.Fail(new(FailureCode(ex), ex.Message)); }
        }
        public Result SetLayout(UiLayout layout)
        {
            if (Check() is { } error) return Result.Fail(error);
            if (!UiCoordinates.Valid(layout)) return Result.Fail(new(ErrorCode.InvalidArgument, "Invalid layout."));
            if (_controls.Values.Any(x => x.Rect.X + x.Rect.Width > layout.Rect.Width || x.Rect.Y + x.Rect.Height > layout.Rect.Height))
                return Result.Fail(new(ErrorCode.InvalidArgument, "Existing controls must fit inside the new window rectangle."));
            try { _native.Layout(layout); _layout = layout; return Result.Ok(); } catch (Exception ex) { return Result.Fail(new(FailureCode(ex), ex.Message)); }
        }
        public Result<UiViewport> GetViewport()
        { if (Check() is { } error) return Result<UiViewport>.Fail(error); try { return Result<UiViewport>.Ok(_native.Viewport); } catch (Exception ex) { return Fail<UiViewport>(FailureCode(ex), ex.Message); } }
        public Result<UiPoint> ScreenToUi(UiPoint point) { var view = GetViewport(); return view.Success ? UiCoordinates.ScreenToUi(view.Value!, point) : Result<UiPoint>.Fail(view.Error!); }
        public Result<UiPoint> UiToScreen(UiPoint point) { var view = GetViewport(); return view.Success ? UiCoordinates.UiToScreen(view.Value!, point) : Result<UiPoint>.Fail(view.Error!); }
        internal void Tick(UiInputFrame frame, bool pointerEligible)
        {
            if (!IsAlive) return; _native.Layout(_layout);
            var canInteract = _owner.MayInteract(this);
            if (canInteract && pointerEligible && frame.Pointer is not null && frame.LeftDown)
            {
                var control = _controls.Values.Reverse().FirstOrDefault(x => x.Live && x.Kind != UiControlKind.Label && x.Native.HitTest(frame.Pointer));
                _pressed = control?.Kind == UiControlKind.Button && !control.Native.UsesNativeActivation ? control : null;
                if (control is not null) { Focus(); if (control.Kind == UiControlKind.TextField) control.Focus(); }
            }
            if (frame.LeftUp)
            {
                var pressed = _pressed; _pressed = null;
                if (canInteract && pointerEligible && pressed?.Live == true && frame.Pointer is not null && pressed.Native.HitTest(frame.Pointer)) pressed.Click();
            }
            bool editing = _textFocusPending > 0; if (_textFocusPending > 0) --_textFocusPending;
            foreach (var control in _controls.Values.ToArray())
            {
                if (!control.Live) continue;
                if (!control.Native.Alive) { control.Dispose(); continue; }
                var activations = control.Kind == UiControlKind.Button && control.Native.UsesNativeActivation ? control.Native.DrainActivations() : 0;
                for (var i = 0; i < Math.Min(activations, 8) && control.Live && IsAlive && _owner.MayInteract(this); i++) control.Click();
                if (!IsAlive) return;
                editing |= control.Kind == UiControlKind.TextField && control.Native.IsFocused;
                control.Poll(); if (_disposed) return;
            }
            _owner._input.SetTextEditing(Id, editing);
        }
        public void Dispose()
        {
            _owner._main.VerifyAccess(); if (_disposed) return; _disposed = true;
            _owner._windows.Remove(Id); _focus?.Dispose(); _focus = null; _pressed = null;
            View.Forget(this);
            foreach (var control in _controls.Values.ToArray()) try { control.Dispose(); } catch (Exception ex) { _owner.Report(LogLevel.Warning, "Control cleanup continued.", ex); }
            _native.Dispose();
        }
        private sealed class Control : IUiControlHandle
        {
            private readonly Window _window;
            internal IUiNativeControl Native { get; }
            internal UiControlKind Kind { get; }
            private Action? _clicked; private Action<string>? _changed;
            private string _last; private readonly int _limit;
            internal UiRect Rect { get; }
            internal bool Live = true;
            public string Id { get; }
            internal Control(Window window, IUiNativeControl native, string id, UiControlKind kind, Action? clicked, Action<string>? changed, int limit, UiRect rect)
            { _window = window; Native = native; Id = id; Kind = kind; _clicked = clicked; _changed = changed; _last = native.Text; _limit = limit; Rect = rect; }
            private OperationError? Check() => _window.Check() ?? (!Live || !Native.Alive ? new(ErrorCode.TargetExpired, "UI control expired.") : null);
            public Result SetText(string text)
            {
                if (Check() is { } error) return Result.Fail(error);
                if (text is null || text.Length > 4096 || (Kind == UiControlKind.TextField && text.Length > _limit)) return Result.Fail(new(ErrorCode.InvalidArgument, "Text exceeds its declared limit."));
                try { Native.Text = text; _last = text; return Result.Ok(); } catch (Exception ex) { return Result.Fail(new(FailureCode(ex), ex.Message)); }
            }
            public Result<string> GetText() { if (Check() is { } error) return Result<string>.Fail(error); try { return Result<string>.Ok(Native.Text); } catch (Exception ex) { return Fail<string>(FailureCode(ex), ex.Message); } }
            public Result Focus()
            {
                if (Check() is { } error) return Result.Fail(error);
                if (Kind != UiControlKind.TextField) return Result.Fail(new(ErrorCode.OperationUnsupported, "Only text fields accept control focus."));
                var focused = _window.Focus(); if (!focused.Success) return focused;
                try { Native.Focus(); _window._textFocusPending = 2; _window._owner._input.SetTextEditing(_window.Id, true); return Result.Ok(); } catch (Exception ex) { return Result.Fail(new(FailureCode(ex), ex.Message)); }
            }
            internal void Click() { if (_clicked is null) return; try { _clicked(); } catch (Exception ex) { _clicked = null; _window._owner.Report(LogLevel.Error, "Button callback disabled: " + _window.Id + "/" + Id, ex); } }
            internal void Poll()
            {
                if (Kind != UiControlKind.TextField) return;
                var text = Native.Text; if (text == _last) return; _last = text;
                try { _changed?.Invoke(text); } catch (Exception ex) { _changed = null; _window._owner.Report(LogLevel.Error, "Text callback disabled: " + _window.Id + "/" + Id, ex); }
            }
            public void Dispose() { _window._owner._main.VerifyAccess(); if (!Live) return; Live = false; _clicked = null; _changed = null; _window._controls.Remove(Id); Native.Dispose(); }
        }
    }
    private static bool ValidColor(ResourceColor? color) => color is null || new[] { color.R, color.G, color.B, color.A }.All(x => float.IsFinite(x) && x is >= 0 and <= 1);
    private static ErrorCode FailureCode(Exception ex) => ex is UiBackendException backend ? backend.Code : ex is NotSupportedException ? ErrorCode.UnsupportedSignature : ex is ObjectDisposedException or TargetExpiredException ? ErrorCode.TargetExpired : ErrorCode.InternalError;
    private static Result<T> Fail<T>(ErrorCode code, string message) => Result<T>.Fail(new(code, message, UiInputCapabilityIds.Windows));
}
