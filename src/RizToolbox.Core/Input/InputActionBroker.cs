using RizToolbox.Sdk;

namespace RizToolbox.Core.Input;

public static class UiLocalIds
{
    public static bool IsValid(string? id) => id is { Length: >= 1 and <= 96 } && id.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');
    public static string Qualify(string owner, string localId) => owner + "/" + localId;
}

/// <summary>Framework action arbitration only. It never claims to consume the game's own native polling or InputSystem actions.</summary>
public sealed class InputActionBroker : IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly Dictionary<string, ActionLease> _actions = new(StringComparer.Ordinal);
    private readonly List<FocusLease> _focus = new();
    private bool _disposed;
    private long _frame;
    public InputActionBroker(IMainThreadDispatcher main, ILogger log) => (_main, _log) = (main, log);
    public Result<IInputActionHandle> Register(string owner, string scope, InputActionDefinition definition,
        Action<InputActionEvent> callback, Func<bool> active, Action? released = null)
    {
        _main.VerifyAccess();
        if (_disposed) return Fail<IInputActionHandle>(ErrorCode.ScopeExpired, "Input broker expired.");
        if (definition is null || callback is null || active is null || !UiLocalIds.IsValid(definition.Id) || !ValidContext(definition.Context))
            return Fail<IInputActionHandle>(ErrorCode.InvalidArgument, "Action local ID, context and callback are required.");
        Prune();
        var id = UiLocalIds.Qualify(owner, definition.Id);
        if (_actions.ContainsKey(id)) return Fail<IInputActionHandle>(ErrorCode.Conflict, "Action ID already belongs to " + owner + ": " + id);
        if (_actions.Count >= 512 || _actions.Values.Count(x => x.Owner == owner) >= 128)
            return Fail<IInputActionHandle>(ErrorCode.QuotaExceeded, "Input action quota exceeded.");
        var bindingError = ValidateBinding(definition.Binding);
        if (bindingError is not null) return Result<IInputActionHandle>.Fail(bindingError);
        var conflict = Conflicts(definition.Binding, definition.Context, null);
        if (conflict is not null) return Result<IInputActionHandle>.Fail(conflict);
        var lease = new ActionLease(this, owner, scope, id, definition.Binding, definition.Context,
            definition.WindowId is null ? null : UiLocalIds.Qualify(owner, definition.WindowId), callback, active, released);
        _actions.Add(id, lease);
        return Result<IInputActionHandle>.Ok(lease);
    }
    public IDisposable PushFocus(string owner, string window, bool modal, Func<bool> active)
    {
        _main.VerifyAccess();
        if (_disposed) throw new ObjectDisposedException(nameof(InputActionBroker));
        foreach (var existing in _focus) existing.Editing = false;
        var focus = new FocusLease(this, owner, window, modal, active);
        _focus.Add(focus); return focus;
    }
    public void SetTextEditing(string window, bool editing)
    {
        _main.VerifyAccess();
        var current = _focus.LastOrDefault();
        if (current is not null && current.Window == window) current.Editing = editing;
    }
    public IReadOnlyList<InputFocusInfo> GetFocusStack()
    {
        _main.VerifyAccess(); Prune();
        return Array.AsReadOnly(_focus.Select(x => new InputFocusInfo(x.Owner, x.Window, x.Modal, x.Editing)).ToArray());
    }
    public IReadOnlyList<InputBindingOwner> GetBindings()
    {
        _main.VerifyAccess(); Prune();
        return Array.AsReadOnly(_actions.Values.OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => new InputBindingOwner(x.Owner, x.Id, x.Binding, x.Context, x.Window)).ToArray());
    }
    public void Dispatch(IReadOnlyCollection<InputBinding> pressed, InputActionContext current, bool externalTextEditing = false)
        => Dispatch(pressed, current, externalTextEditing, null);
    public void Dispatch(IReadOnlyCollection<InputBinding> pressed, InputActionContext current, bool externalTextEditing,
        Func<InputBindingOwner, bool>? capturedRegistrationIsCurrent)
    {
        _main.VerifyAccess(); if (_disposed) return; Prune(); var frame = ++_frame;
        foreach (var action in _actions.Values.ToArray())
        {
            // Re-evaluate on every callback: a callback may open a modal, disable itself, or remove another action.
            Prune(); if (!action.Live || !_actions.ContainsKey(action.Id) || !pressed.Contains(action.Binding)) continue;
            if (!InContext(action.Context, current) || externalTextEditing || _focus.Any(x => x.Editing)) continue;
            var modal = _focus.LastOrDefault(x => x.Modal);
            var top = _focus.LastOrDefault();
            if (modal is not null && action.Window != modal.Window) continue;
            if (action.Window is not null && top?.Window != action.Window) continue;
            // Earlier callbacks may rebind an already-registered action to a pressed
            // key. Keep capture ownership until this callback, not merely this batch.
            if (capturedRegistrationIsCurrent is not null &&
                !capturedRegistrationIsCurrent(new(action.Owner, action.Id, action.Binding, action.Context, action.Window))) continue;
            try { action.Callback!(new(action.Id, action.Binding, current, frame)); }
            catch (Exception ex) { action.Dispose(); try { _log.Log(LogLevel.Error, "Input callback removed after failure: " + action.Id, ex); } catch { } }
        }
    }
    public void ClearFocus()
    { _main.VerifyAccess(); foreach (var focus in _focus.ToArray()) focus.Dispose(); }
    private void Prune()
    {
        foreach (var action in _actions.Values.ToArray()) if (!Safe(action.Active)) action.Dispose();
        foreach (var focus in _focus.ToArray()) if (!Safe(focus.Active)) focus.Dispose();
    }
    private static bool Safe(Func<bool> check) { try { return check(); } catch { return false; } }
    private static bool ValidContext(InputActionContext context) => context != InputActionContext.None && (context & ~(InputActionContext.Global | InputActionContext.Menu | InputActionContext.Gameplay)) == 0;
    private static bool InContext(InputActionContext requested, InputActionContext current) => requested.HasFlag(InputActionContext.Global) || (requested & current) != 0;
    private static bool Overlaps(InputActionContext a, InputActionContext b) => a.HasFlag(InputActionContext.Global) || b.HasFlag(InputActionContext.Global) || (a & b) != 0;
    public static OperationError? ValidateBinding(InputBinding? binding)
    {
        if (binding is null || string.IsNullOrWhiteSpace(binding.Control)) return new(ErrorCode.InvalidArgument, "A binding control is required.");
        if (binding.Device == InputDeviceKind.Mouse)
            return binding.Control is "Left" or "Right" or "Middle" ? null : new(ErrorCode.OperationUnsupported, "Only Left/Right/Middle mouse buttons are mapped.");
        if (binding.Device == InputDeviceKind.Gamepad)
            return binding.Control is "ButtonSouth" or "ButtonEast" or "ButtonWest" or "ButtonNorth"
                or "LeftShoulder" or "RightShoulder" or "LeftTrigger" or "RightTrigger"
                or "LeftStickButton" or "RightStickButton" or "Start" or "Select"
                or "DpadUp" or "DpadDown" or "DpadLeft" or "DpadRight" ? null
                : new(ErrorCode.OperationUnsupported, "Only declared Gamepad.current digital button names are mapped; axes, aliases, chords and rumble are unsupported.");
        if (binding.Device != InputDeviceKind.Keyboard) return new(ErrorCode.OperationUnsupported, "This adapter has no validated mapping for this input device.");
        var key = binding.Control;
        return (key.Length == 1 && key[0] is >= 'A' and <= 'Z') || key is "Escape" or "Tab" or "Return" or "Space" or "Backspace" or "UpArrow" or "DownArrow" or "LeftArrow" or "RightArrow" ||
            (key.StartsWith("F", StringComparison.Ordinal) && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 12 && key == "F" + f) ||
            (key.Length == 6 && key.StartsWith("Alpha", StringComparison.Ordinal) && key[5] is >= '0' and <= '9') ? null
            : new(ErrorCode.OperationUnsupported, "Keyboard mapping accepts A-Z, Alpha0-9, F1-12, arrows, Escape/Tab/Return/Space/Backspace; modifiers are not mapped.");
    }
    private OperationError? Conflicts(InputBinding binding, InputActionContext context, ActionLease? except)
    {
        Prune(); var owners = _actions.Values.Where(x => x != except && x.Binding == binding && Overlaps(x.Context, context))
            .OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Owner + ":" + x.Id).ToArray();
        return owners.Length == 0 ? null : new(ErrorCode.Conflict, "Binding already belongs to " + string.Join(", ", owners), UiInputCapabilityIds.Actions, binding.Device + "/" + binding.Control);
    }
    private Result Rebind(ActionLease lease, InputBinding binding)
    {
        _main.VerifyAccess(); if (!lease.Live || _disposed || !Safe(lease.Active)) return Result.Fail(new(ErrorCode.ScopeExpired, "Input action expired."));
        var error = ValidateBinding(binding) ?? Conflicts(binding, lease.Context, lease);
        if (error is not null) return Result.Fail(error);
        lease.Binding = binding; return Result.Ok();
    }
    private static Result<T> Fail<T>(ErrorCode code, string message) => Result<T>.Fail(new(code, message, UiInputCapabilityIds.Actions));
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return; _disposed = true;
        foreach (var action in _actions.Values.ToArray()) action.Dispose(); ClearFocus();
    }
    private sealed class ActionLease : IInputActionHandle
    {
        private readonly InputActionBroker _broker;
        internal string Owner { get; } internal string Scope { get; } public string Id { get; }
        internal InputBinding Binding; internal InputActionContext Context { get; } internal string? Window { get; }
        internal Action<InputActionEvent>? Callback; internal Func<bool> Active; private Action? _released; internal bool Live = true;
        internal ActionLease(InputActionBroker broker, string owner, string scope, string id, InputBinding binding, InputActionContext context,
            string? window, Action<InputActionEvent> callback, Func<bool> active, Action? released)
            => (_broker, Owner, Scope, Id, Binding, Context, Window, Callback, Active, _released) = (broker, owner, scope, id, binding, context, window, callback, active, released);
        public Result Rebind(InputBinding binding) => _broker.Rebind(this, binding);
        public void Dispose()
        {
            _broker._main.VerifyAccess(); if (!Live) return; Live = false; _broker._actions.Remove(Id); Callback = null; Active = () => false;
            var released = _released; _released = null; try { released?.Invoke(); } catch { }
        }
    }
    private sealed class FocusLease : IDisposable
    {
        private readonly InputActionBroker _broker;
        internal string Owner { get; } internal string Window { get; } internal bool Modal { get; } internal bool Editing;
        internal Func<bool> Active { get; }
        internal FocusLease(InputActionBroker broker, string owner, string window, bool modal, Func<bool> active)
            => (_broker, Owner, Window, Modal, Active) = (broker, owner, window, modal, active);
        public void Dispose() { _broker._main.VerifyAccess(); _broker._focus.Remove(this); }
    }
}
