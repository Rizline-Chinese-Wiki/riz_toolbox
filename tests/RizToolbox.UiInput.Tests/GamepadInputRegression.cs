using RizToolbox.Adapter.UiInput;
using RizToolbox.Core.Input;
using RizToolbox.Sdk;

internal static class GamepadInputRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Gamepad digital mappings match the exact Core allowlist and reject axes aliases and chords", Mappings),
        ("Gamepad symbols alone never report runtime availability and unsupported overrides old success", Availability),
        ("Gamepad capture owns one device through all 16 reads and returns only copied digital edges", Capture),
        ("Missing removed disabled and recovered gamepad samples skip unsafe controls and retry", Removal),
        ("Gamepad acquisition state control and cleanup faults discard partial edges without changing keyboard mouse", Faults),
        ("Gamepad hot removal during a read discards the entire partial capture", HotRemoval),
        ("Gamepad invalidation removes only its queued and already-drained epochs", IsolatedInvalidation),
        ("Gamepad context conflicts identify owners and failed rebinding remains atomic", Conflicts),
        ("Gamepad capture-time modal text and background focus never replay blocked edges", Focus),
        ("Gamepad callback rebinding cannot inherit another owner's same-batch digital edge", CallbackRebind),
        ("Gamepad disconnect preserves mixed keyboard edges and fresh reconnection can resume existing actions", Reconnect),
        ("Direct current gamepad replacement revokes old queued and drained edges while preserving keyboard", DeviceReplacement),
        ("Same current device identity preserves pending edges and replacement accepts only fresh epochs", StableIdentity),
        ("Invalid or changing device identity discards the full digital capture and releases device", InvalidIdentity)
    };
    private static InputBinding Pad(string name) => new(InputDeviceKind.Gamepad, name);
    private static InputActionDefinition Action(string id, string control, InputActionContext context = InputActionContext.Global)
        => new(id, Pad(control), context);
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Gamepad invariant failed."); }
    private static Fixture Ready()
    {
        var f = new Fixture(); f.Backend.GamepadReady = true; f.Backend.Edges = new(); return f;
    }
    private static void Mappings()
    {
        var controls = NativeUiInputBackend.GamepadControls;
        Check(controls.Count == 16 && controls.Distinct().Count() == 16);
        foreach (var control in controls)
        {
            Check(InputActionBroker.ValidateBinding(Pad(control)) is null);
            var path = NativeUiInputBackend.GamepadPath(control);
            Check(path.Getter.StartsWith("get_", StringComparison.Ordinal) && path.Dpad == control.StartsWith("Dpad", StringComparison.Ordinal));
        }
        Check(NativeUiInputBackend.GamepadPath("ButtonSouth") == (false, "get_buttonSouth"));
        Check(NativeUiInputBackend.GamepadPath("LeftTrigger") == (false, "get_leftTrigger"));
        Check(NativeUiInputBackend.GamepadPath("DpadRight") == (true, "get_right"));
        foreach (var invalid in new[] { "A", "Cross", "buttonSouth", "LeftStick", "LeftStickX", "TriggerAxis", "Start+Select", "Rumble", "Dpad" })
        {
            Check(InputActionBroker.ValidateBinding(Pad(invalid))?.Code == ErrorCode.OperationUnsupported);
            var rejected = false; try { NativeUiInputBackend.GamepadPath(invalid); } catch (NotSupportedException) { rejected = true; }
            Check(rejected);
        }
    }
    private static void Availability()
    {
        Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Gamepad, default).Support == InputSupport.SymbolVerified);
        var pad = new Device(); var capture = NativeUiInputBackend.CaptureGamepad(() => pad);
        Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Gamepad, capture.State).Support == InputSupport.RuntimeAvailable);
        var unsupported = NativeUiInputBackend.DeviceStatus(InputDeviceKind.Gamepad, capture.State, "get_dpad signature changed");
        Check(unsupported.Support == InputSupport.Unsupported && unsupported.Reason == "get_dpad signature changed");
        Check(unsupported.Mapping.Contains("press thresholds") && unsupported.Mapping.Contains("no axes"));
        Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Gamepad, capture.State, captureFailure: "temporary").Support == InputSupport.SymbolVerified);
    }
    private static void Capture()
    {
        var pad = new Device(); pad.Pressed.UnionWith(new[] { "ButtonSouth", "Start", "DpadUp", "LeftTrigger" });
        var capture = NativeUiInputBackend.CaptureGamepad(() => pad);
        Check(capture.State.CaptureSucceeded && capture.Failure is null && pad.Disposals == 1 && pad.Reads.Count == 16);
        Check(capture.Pressed.Select(x => x.Control).ToHashSet().SetEquals(pad.Pressed));
        Check(capture.Pressed.All(x => x.Device == InputDeviceKind.Gamepad) && pad.Reads.SequenceEqual(NativeUiInputBackend.GamepadControls));
        pad.Pressed.Clear(); Check(capture.Pressed.Count == 4);
    }
    private static void Removal()
    {
        var missing = NativeUiInputBackend.CaptureGamepad(() => null);
        Check(missing.State.Sampled && !missing.State.Present && missing.Pressed.Count == 0);
        Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Gamepad, missing.State).Reason!.StartsWith("Gamepad.current"));
        foreach (var stage in new[] { "removed", "disabled" })
        {
            var pad = new Device { PresentInSystem = stage != "removed", IsEnabled = stage != "disabled" };
            var stopped = NativeUiInputBackend.CaptureGamepad(() => pad);
            Check(!stopped.State.CaptureSucceeded && stopped.Pressed.Count == 0 && pad.Reads.Count == 0 && pad.Disposals == 1);
            Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Gamepad, stopped.State).Support == InputSupport.SymbolVerified);
        }
        var recovered = new Device(); recovered.Pressed.Add("DpadDown");
        Check(NativeUiInputBackend.CaptureGamepad(() => recovered).Pressed.Single() == Pad("DpadDown"));
    }
    private static void Faults()
    {
        var keyboard = NativeUiInputBackend.CaptureDevice(true, () => true, () => true, () => { });
        var mouse = keyboard;
        foreach (var stage in new[] { "open", "added", "enabled", "identity", "control", "dispose" })
        {
            var pad = new Device { FailureStage = stage }; pad.Pressed.Add("ButtonSouth");
            var capture = NativeUiInputBackend.CaptureGamepad(() => stage == "open" ? throw new IOException("private native details") : pad);
            Check(!capture.State.CaptureSucceeded && capture.Pressed.Count == 0 && capture.Failure is not null);
            Check(!capture.Failure!.Contains("private native details") && pad.Disposals == (stage == "open" ? 0 : 1));
            Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Keyboard, keyboard).Support == InputSupport.RuntimeAvailable);
            Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, mouse).Support == InputSupport.RuntimeAvailable);
            Check(NativeUiInputBackend.CaptureGamepad(() => new Device()).State.CaptureSucceeded);
        }
    }
    private static void HotRemoval()
    {
        var pad = new Device { RemoveAfterRead = 3 }; pad.Pressed.Add("ButtonSouth");
        var capture = NativeUiInputBackend.CaptureGamepad(() => pad);
        Check(!capture.State.CaptureSucceeded && capture.Pressed.Count == 0 && capture.Failure is not null && pad.Disposals == 1);
        Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Gamepad, capture.State, captureFailure: capture.Failure).Support == InputSupport.SymbolVerified);
    }
    private static void IsolatedInvalidation()
    {
        using var edges = new InputUpdateEdgeCollector();
        var owners = new[] {
            new InputBindingOwner("a", "a/pad", Pad("ButtonSouth"), InputActionContext.Global, null),
            new InputBindingOwner("b", "b/key", new(InputDeviceKind.Keyboard, "F8"), InputActionContext.Global, null),
            new InputBindingOwner("b", "b/mouse", new(InputDeviceKind.Mouse, "Left"), InputActionContext.Global, null) };
        edges.Add(1, 16, owners, owners.Select(x => x.Binding).ToArray(), null, false, false);
        var drained = edges.Drain().Single();
        edges.Add(2, 16, owners, owners.Select(x => x.Binding).ToArray(), null, false, false);
        NativeUiInputBackend.InvalidateGamepadEdges(edges, owners);
        Check(!edges.IsCurrent(drained.Actions[0]) && drained.Actions.Skip(1).All(edges.IsCurrent));
        Check(edges.Drain().Single().Actions.Select(x => x.Owner.Binding.Device).SequenceEqual(new[] { InputDeviceKind.Keyboard, InputDeviceKind.Mouse }));
    }
    private static void Conflicts()
    {
        using var f = Ready(); var menu = 0; var game = 0;
        var first = f.Input("a").Register(Action("menu", "ButtonSouth", InputActionContext.Menu), _ => menu++).Value!;
        Check(f.Input("b").Register(Action("game", "ButtonSouth", InputActionContext.Gameplay), _ => game++).Success);
        var conflict = f.Input("a").Register(Action("global", "ButtonSouth"), _ => { });
        Check(conflict.Error?.Code == ErrorCode.Conflict && conflict.Error.Message.Contains("a:a/menu") && conflict.Error.Message.Contains("b:b/game"));
        Check(f.Input("b").Register(Action("east", "ButtonEast"), _ => { }).Success);
        Check(first.Rebind(Pad("ButtonEast")).Error?.Code == ErrorCode.Conflict);
        f.Backend.QueueBindings(1, Pad("ButtonSouth")); f.Service.Tick(); Check(menu == 1 && game == 0);
        Check(first.Rebind(Pad("DpadLeft")).Success);
        f.Backend.QueueBindings(2, Pad("ButtonSouth")); f.Service.Tick(); Check(menu == 1);
        f.Backend.QueueBindings(3, Pad("DpadLeft")); f.Service.Tick(); Check(menu == 2);
    }
    private static void Focus()
    {
        using var f = Ready(); var count = 0;
        Check(f.Input("b").Register(Action("south", "ButtonSouth"), _ => count++).Success);
        var modal = f.Ui("a").CreateWindow(new("modal", UiMountIds.Overlay, "modal", new(new(0, 0, 500, 400)), Modal: true)).Value!;
        f.Backend.QueueBindings(1, Pad("ButtonSouth")); modal.Dispose(); f.Service.Tick(); Check(count == 0);
        f.Backend.Frame = f.Backend.Frame with { ExternalTextEditing = true };
        f.Backend.QueueBindings(2, Pad("ButtonSouth")); f.Backend.Frame = f.Backend.Frame with { ExternalTextEditing = false };
        f.Service.Tick(); Check(count == 0);
        f.Backend.ApplicationFocused = false; f.Backend.QueueBindings(3, Pad("ButtonSouth")); f.Backend.ApplicationFocused = true;
        f.Service.Tick(); Check(count == 0);
        f.Backend.QueueBindings(4, Pad("ButtonSouth")); f.Service.Tick(); Check(count == 1);
    }
    private static void CallbackRebind()
    {
        using var f = Ready(); var oldCalls = 0; var inherited = 0; IInputActionHandle? old = null, existing = null;
        Check(f.Input("a").Register(Action("trigger", "Start"), _ => {
            old!.Dispose(); Check(existing!.Rebind(Pad("ButtonSouth")).Success);
        }).Success);
        old = f.Input("a").Register(Action("old", "ButtonSouth"), _ => oldCalls++).Value!;
        existing = f.Input("b").Register(Action("existing", "ButtonEast"), _ => inherited++).Value!;
        f.Backend.QueueBindings(1, Pad("Start"), Pad("ButtonSouth")); f.Service.Tick(); Check(oldCalls == 0 && inherited == 0);
        f.Backend.QueueBindings(2, Pad("ButtonSouth")); f.Service.Tick(); Check(inherited == 1);
    }
    private static void Reconnect()
    {
        using var f = Ready(); var pad = 0; var key = 0;
        Check(f.Input("a").Register(Action("pad", "RightTrigger"), _ => pad++).Success);
        Check(f.Input("b").Register(new("key", new(InputDeviceKind.Keyboard, "F8")), _ => key++).Success);
        f.Backend.QueueBindings(1, Pad("RightTrigger"), new(InputDeviceKind.Keyboard, "F8"));
        f.Backend.LoseGamepad(); f.Service.Tick(); Check(pad == 0 && key == 1);
        Check(f.Input("a").Register(Action("new", "Select"), _ => { }).Error?.Code == ErrorCode.OperationUnsupported);
        f.Backend.GamepadReady = true;
        f.Backend.QueueBindings(2, Pad("RightTrigger")); f.Service.Tick(); Check(pad == 1 && key == 1);
    }
    private static InputBindingOwner[] IdentityOwners() => new[] {
        new InputBindingOwner("a", "a/pad", Pad("ButtonSouth"), InputActionContext.Global, null),
        new InputBindingOwner("b", "b/key", new(InputDeviceKind.Keyboard,"F8"), InputActionContext.Global,null) };
    private static void DeviceReplacement()
    {
        using var edges = new InputUpdateEdgeCollector(); var owners = IdentityOwners();
        var a = NativeUiInputBackend.CaptureGamepad(() => new Device { Identity = 12 });
        var id = NativeUiInputBackend.ApplyGamepadIdentity(edges,owners,null,a);
        edges.Add(1,16,owners,owners.Select(x=>x.Binding).ToArray(),null,false,false);
        var drained = edges.Drain().Single();
        edges.Add(2,16,owners,owners.Select(x=>x.Binding).ToArray(),null,false,false);
        var b = NativeUiInputBackend.CaptureGamepad(() => new Device { Identity = 37 });
        id = NativeUiInputBackend.ApplyGamepadIdentity(edges,owners,id,b);
        Check(id == 37 && !edges.IsCurrent(drained.Actions[0]) && edges.IsCurrent(drained.Actions[1]));
        Check(edges.Drain().Single().Actions.Single().Owner.Binding.Device == InputDeviceKind.Keyboard);
    }
    private static void StableIdentity()
    {
        using var edges = new InputUpdateEdgeCollector(); var owners = IdentityOwners();
        var a = NativeUiInputBackend.CaptureGamepad(() => new Device { Identity = 12 });
        var id = NativeUiInputBackend.ApplyGamepadIdentity(edges,owners,null,a);
        edges.Add(1,16,owners,new[]{Pad("ButtonSouth")},null,false,false);
        id = NativeUiInputBackend.ApplyGamepadIdentity(edges,owners,id,a);
        var old = edges.Drain().Single().Actions.Single(); Check(edges.IsCurrent(old));
        id = NativeUiInputBackend.ApplyGamepadIdentity(edges,owners,id,NativeUiInputBackend.CaptureGamepad(()=>new Device {Identity=13}));
        edges.Add(2,16,owners,new[]{Pad("ButtonSouth")},null,false,false);
        var fresh = edges.Drain().Single().Actions.Single();
        Check(!edges.IsCurrent(old) && edges.IsCurrent(fresh) && fresh.Epoch != old.Epoch);
    }
    private static void InvalidIdentity()
    {
        foreach(var device in new[]{new Device { Identity=-7 },new Device { ChangeIdentityAfterRead=3 }})
        {
            device.Pressed.Add("ButtonSouth");
            var capture=NativeUiInputBackend.CaptureGamepad(()=>device,invalidDeviceId:-7);
            Check(!capture.State.CaptureSucceeded && capture.DeviceId is null && capture.Pressed.Count==0 && device.Disposals==1);
        }
        using var edges=new InputUpdateEdgeCollector();var owners=IdentityOwners();
        edges.Add(1,16,owners,new[]{Pad("ButtonSouth")},null,false,false);
        Check(NativeUiInputBackend.ApplyGamepadIdentity(edges,owners,12,NativeUiInputBackend.CaptureGamepad(()=>null)) is null);
        Check(edges.Pending==0);
    }
    private sealed class Device : NativeUiInputBackend.IGamepadSampleDevice
    {
        internal bool PresentInSystem = true, IsEnabled = true;
        internal string? FailureStage; internal int RemoveAfterRead, Disposals, Identity=1, ChangeIdentityAfterRead;
        internal readonly List<string> Reads = new(); internal readonly HashSet<string> Pressed = new();
        private void Live() { Check(Disposals == 0); }
        public bool Added { get { Live(); if (FailureStage == "added") throw new IOException("private native details"); return PresentInSystem; } }
        public bool Enabled { get { Live(); if (FailureStage == "enabled") throw new IOException("private native details"); return IsEnabled; } }
        public int DeviceId { get { Live(); if(FailureStage=="identity")throw new IOException("private native details");return Identity; } }
        public bool WasPressed(string control)
        {
            Live(); Reads.Add(control);
            if (RemoveAfterRead > 0 && Reads.Count == RemoveAfterRead) PresentInSystem = false;
            if (ChangeIdentityAfterRead > 0 && Reads.Count == ChangeIdentityAfterRead) Identity++;
            if (FailureStage == "control" && Reads.Count == 2) throw new IOException("private native details");
            return Pressed.Contains(control);
        }
        public void Dispose() { Disposals++; if (FailureStage == "dispose") throw new IOException("private native details"); }
    }
}
