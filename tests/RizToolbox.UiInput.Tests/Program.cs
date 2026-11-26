using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Adapter.Modules;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Core;
using RizToolbox.Core.Input;
using RizToolbox.Sdk;
using RizToolbox.Examples;

var checks = new (string Name, Action Run)[]
{
    ("Two plugin contexts own identical local window/control IDs independently", () => {
        using var f = new Fixture(); var a = f.Ui("a").CreateWindow(Window()).Value!; var b = f.Ui("b").CreateWindow(Window()).Value!;
        Equal("a/panel", a.Id); Equal("b/panel", b.Id);
        Check(a.AddLabel(Control()).Success && b.AddLabel(Control()).Success); Check(!a.AddLabel(Control()).Success);
        f.Host.Disable("a"); Check(!a.IsAlive && b.IsAlive); Equal(1, f.Backend.LiveWindows);
    }),
    ("Window duplicates and unknown or absent mounts refuse before native creation", () => {
        using var f = new Fixture(); var ui = f.Ui("a"); Check(ui.CreateWindow(Window()).Success);
        Code(ErrorCode.Conflict, ui.CreateWindow(Window()).Error);
        Code(ErrorCode.OperationUnsupported, ui.CreateWindow(Window("unknown") with { MountId = "not-a-mount" }).Error);
        f.Backend.MenuReady = false; Code(ErrorCode.TargetMissing, ui.CreateWindow(Window("menu") with { MountId = UiMountIds.MainMenu }).Error); Equal(1, f.Backend.Created);
    }),
    ("Invalid rectangles fonts themes and oversized text cannot reach native writes", () => {
        using var f = new Fixture(); var ui = f.Ui("a");
        Code(ErrorCode.InvalidArgument, ui.CreateWindow(Window() with { Layout = new(new(float.NaN, 0, 100, 100)) }).Error);
        Code(ErrorCode.OperationUnsupported, ui.CreateWindow(Window() with { Theme = new("arbitrary.ttf") }).Error);
        Code(ErrorCode.OperationUnsupported, ui.CreateWindow(Window() with { Theme = new(Foreground: new(2, 0, 0, 1)) }).Error);
        Equal(0, f.Backend.Created); var window = ui.CreateWindow(Window()).Value!;
        Code(ErrorCode.InvalidArgument, window.AddTextField(Control() with { Text = "too long" }, characterLimit: 2).Error);
        Code(ErrorCode.InvalidArgument, window.AddLabel(Control() with { Rect = new(490, 0, 100, 20) }).Error); Equal(0, f.Backend.Last.Controls.Count);
    }),
    ("Control focus and native text polling use the production service path", () => {
        using var f = new Fixture(); var window = f.Ui("a").CreateWindow(Window()).Value!; var values = new List<string>();
        var text = window.AddTextField(Control(), values.Add).Value!; Check(text.Focus().Success); Check(f.Input("a").GetFocusStack().Single().TextEditing);
        f.Backend.Last.Controls.Single().Text = "typed"; f.Service.Tick(); f.Service.Tick(); Equal(1, values.Count); Equal("typed", values[0]);
        Check(text.SetText("programmatic").Success); f.Service.Tick(); Equal(1, values.Count);
    }),
    ("Window actions can bind through a different capability view of the same real plugin scope", () => {
        using var f = new Fixture(); f.Ui("a").CreateWindow(Window());
        Check(f.Input("a").Register(Action("key", "F1") with { WindowId = "panel" }, _ => { }).Success);
        var child = f.Contexts["a"].CreateScope("sibling"); var childInput = child.GetService<IInputActionService>(UiInputCapabilityIds.Actions).Service!;
        Code(ErrorCode.TargetMissing, childInput.Register(Action("other", "F2") with { WindowId = "panel" }, _ => { }).Error);
    }),
    ("Duplicate actions and overlapping bindings identify every existing owner", () => {
        using var f = new Fixture(); Check(f.Input("a").Register(Action("same", "F1", InputActionContext.Menu), _ => { }).Success);
        Check(f.Input("b").Register(Action("same", "F1", InputActionContext.Gameplay), _ => { }).Success);
        var conflict = f.Input("a").Register(Action("global", "F1"), _ => { }); Code(ErrorCode.Conflict, conflict.Error);
        Check(conflict.Error!.Message.Contains("a:a/same") && conflict.Error.Message.Contains("b:b/same"));
        Code(ErrorCode.Conflict, f.Input("a").Register(Action("same", "F2"), _ => { }).Error); Equal(2, f.Input("a").GetBindings().Count);
    }),
    ("Failed rebind is atomic and successful rebind changes the polled production binding", () => {
        using var f = new Fixture(); int a = 0, b = 0;
        var first = f.Input("a").Register(Action("first", "F1"), _ => a++).Value!;
        f.Input("b").Register(Action("second", "F2"), _ => b++);
        Code(ErrorCode.Conflict, first.Rebind(new(InputDeviceKind.Keyboard, "F2")).Error);
        f.Press("F1"); Equal(1, a); Equal(0, b); Check(first.Rebind(new(InputDeviceKind.Keyboard, "F3")).Success);
        f.Press("F1"); f.Press("F3"); Equal(2, a); Equal("F3", f.Input("a").GetBindings().Single(x => x.PluginId == "a").Binding.Control);
    }),
    ("Keyboard mouse and gamepad are independently declared; unmapped devices are refused", () => {
        using var f = new Fixture(); var input = f.Input("a");
        Equal(InputSupport.Unsupported, input.GetDevices().Single(x => x.Device == InputDeviceKind.Gamepad).Support);
        Code(ErrorCode.OperationUnsupported, input.Register(new("pad", new(InputDeviceKind.Gamepad, "A")), _ => { }).Error);
        Code(ErrorCode.OperationUnsupported, input.Register(Action("chord", "Ctrl+F1"), _ => { }).Error);
        Check(input.Register(new("mouse", new(InputDeviceKind.Mouse, "Left")), _ => { }).Success);
        f.Backend.KeyboardReady = false; Code(ErrorCode.OperationUnsupported, input.Register(Action("key", "F1"), _ => { }).Error);
        Check(input.Register(new("right", new(InputDeviceKind.Mouse, "Right")), _ => { }).Success);
    }),
    ("Current device removal and disable skip raw reads and recover without changing device state", () => {
        var addedReads = 0; var enabledReads = 0; var controlReads = 0;
        bool added = true, enabled = true;
        NativeUiInputBackend.DeviceSample Capture(bool present = true) => NativeUiInputBackend.CaptureDevice(present,
            () => { addedReads++; return added; }, () => { enabledReads++; return enabled; }, () => controlReads++);
        var initial = Capture(); Equal(InputSupport.RuntimeAvailable, NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, initial).Support);
        Equal(1, controlReads);
        enabled = false; var disabled = Capture();
        Equal(InputSupport.SymbolVerified, NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, disabled).Support);
        Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, disabled).Reason!.Contains("disabled"));
        Equal(1, controlReads); Equal(2, addedReads); Equal(2, enabledReads);
        added = false; var removed = Capture();
        Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, removed).Reason!.Contains("not added"));
        Equal(1, controlReads); Equal(3, addedReads); Equal(2, enabledReads);
        var missing = Capture(false); Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, missing).Reason!.Contains("is null"));
        Equal(3, addedReads); Equal(2, enabledReads); Equal(1, controlReads);
        added = enabled = true; var recovered = Capture();
        Equal(InputSupport.RuntimeAvailable, NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, recovered).Support);
        Equal(2, controlReads); Equal(4, addedReads); Equal(3, enabledReads);
    }),
    ("Device availability requires a successful fresh capture and remains independent by device", () => {
        var unsampled = NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, default);
        Equal(InputSupport.SymbolVerified, unsampled.Support); Check(unsampled.Reason!.Contains("not been sampled"));
        var sample = NativeUiInputBackend.CaptureDevice(true, () => true, () => true, () => { });
        Equal(InputSupport.RuntimeAvailable, NativeUiInputBackend.DeviceStatus(InputDeviceKind.Keyboard, sample).Support);
        // Capture starts from default each frame. A failing capture cannot reuse the prior successful value.
        sample = NativeUiInputBackend.TryCaptureDevice(() => NativeUiInputBackend.CaptureDevice(true, () => true, () => true,
            () => throw new IOException("fixture control read failure")), out var failure);
        Equal(InputSupport.SymbolVerified, NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, sample, captureFailure: failure).Support);
        Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, sample, captureFailure: failure).Reason!.Contains("IOException"));
        var keyboard = NativeUiInputBackend.CaptureDevice(true, () => true, () => true, () => { });
        var mouse = NativeUiInputBackend.CaptureDevice(true, () => true, () => false, () => throw new InvalidOperationException("Disabled mouse must not read controls."));
        Equal(InputSupport.RuntimeAvailable, NativeUiInputBackend.DeviceStatus(InputDeviceKind.Keyboard, keyboard).Support);
        Equal(InputSupport.SymbolVerified, NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, mouse).Support);
        Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, mouse).Reason!.Contains("disabled"));
        foreach (var partial in new[] {
            new NativeUiInputBackend.DeviceSample(false, true, true, true, true),
            new NativeUiInputBackend.DeviceSample(true, false, true, true, true),
            new NativeUiInputBackend.DeviceSample(true, true, false, true, true),
            new NativeUiInputBackend.DeviceSample(true, true, true, false, true),
            new NativeUiInputBackend.DeviceSample(true, true, true, true, false) })
            Check(NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, partial).Support != InputSupport.RuntimeAvailable);
    }),
    ("Temporary native root state and control read failures retry and recover on the next capture", () => {
        foreach (var stage in new[] { "root", "added", "enabled", "control", "root-cleanup" })
        {
            var calls = 0; var controls = 0;
            NativeUiInputBackend.DeviceSample Read()
            {
                calls++; var failing = calls == 1;
                if (failing && stage == "root") throw new InvalidOperationException("fixture native root failure");
                var result = NativeUiInputBackend.CaptureDevice(true,
                    () => failing && stage == "added" ? throw new IOException("fixture device state failure") : true,
                    () => failing && stage == "enabled" ? throw new IOException("fixture device state failure") : true,
                    () => { controls++; if (failing && stage == "control") throw new InvalidOperationException("fixture native control failure"); });
                if (failing && stage == "root-cleanup") throw new InvalidOperationException("fixture native root cleanup failure");
                return result;
            }
            var first = NativeUiInputBackend.TryCaptureDevice(Read, out var failure);
            Check(!first.CaptureSucceeded && failure is not null);
            var refused = NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, first, captureFailure: failure);
            Equal(InputSupport.SymbolVerified, refused.Support); Check(refused.Reason!.Contains("retry next frame"));
            var second = NativeUiInputBackend.TryCaptureDevice(Read, out failure);
            Check(second.CaptureSucceeded && failure is null); Equal(2, calls);
            Equal(InputSupport.RuntimeAvailable, NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, second, captureFailure: failure).Support);
            Equal(stage is "control" or "root-cleanup" ? 2 : 1, controls);
        }
    }),
    ("Permanent symbol failures remain unsupported while temporary device failures do not affect the other device", () => {
        var keyboard = NativeUiInputBackend.TryCaptureDevice(() => NativeUiInputBackend.CaptureDevice(true, () => true, () => true, () => { }), out var keyboardFailure);
        var mouse = NativeUiInputBackend.TryCaptureDevice(() => throw new IOException("native-address-private-fixture"), out var mouseFailure);
        var healthy = NativeUiInputBackend.DeviceStatus(InputDeviceKind.Keyboard, keyboard, captureFailure: keyboardFailure);
        var temporary = NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, mouse, captureFailure: mouseFailure);
        Equal(InputSupport.RuntimeAvailable, healthy.Support); Equal(InputSupport.SymbolVerified, temporary.Support);
        Check(!temporary.Reason!.Contains("native-address-private-fixture")); Check(temporary.Reason.Contains("IOException"));
        var unsupported = NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, keyboard, "InputDevice.get_enabled signature differs.", mouseFailure);
        Equal(InputSupport.Unsupported, unsupported.Support); Equal("InputDevice.get_enabled signature differs.", unsupported.Reason);
        // A stale successful sample cannot override this frame's failure either.
        Equal(InputSupport.SymbolVerified, NativeUiInputBackend.DeviceStatus(InputDeviceKind.Mouse, keyboard, captureFailure: mouseFailure).Support);
    }),
    ("After-input collector copies only declared actions and drains ordered updates once", () => {
        using var collector = new InputUpdateEdgeCollector();
        var key = new InputBinding(InputDeviceKind.Keyboard, "F8");
        var owners = new List<InputBindingOwner> { new("a", "a/f8", key, InputActionContext.Global, null) };
        Check(collector.Add(1, 16, owners, new[] { new InputBinding(InputDeviceKind.Keyboard, "F9") }, null, false, false, 10));
        Equal(0, collector.Pending);
        Check(collector.Add(2, 16, owners, new[] { key }, null, false, false, 11));
        owners.Clear();
        var first = collector.Drain(); Equal(1, first.Count); Equal("a/f8", first[0].Actions.Single().Owner.ActionId);
        Equal(2L, first[0].Invocation); Equal((uint?)11, first[0].UpdateCount); Equal(0, collector.Drain().Count);
    }),
    ("Collector deduplicates native invocations and input ordinals without merging separate edges", () => {
        using var collector = new InputUpdateEdgeCollector(); var key = new InputBinding(InputDeviceKind.Keyboard, "F8");
        var owners = new[] { new InputBindingOwner("a", "a/f8", key, InputActionContext.Global, null) };
        Check(collector.Add(1, 16, owners, new[] { key }, null, false, false, uint.MaxValue));
        Check(!collector.Add(1, 16, owners, new[] { key }, null, false, false, uint.MaxValue));
        Check(!collector.Add(2, 16, owners, new[] { key }, null, false, false, uint.MaxValue));
        Check(collector.Add(3, 16, owners, new[] { key }, null, false, false, 0));
        Check(collector.Add(4, 1, owners, new[] { key }, null, false, false, 0));
        Equal(2L, collector.Duplicates); Check(collector.Drain().Select(x => x.Invocation).SequenceEqual(new long[] { 1, 3, 4 }));
    }),
    ("Multiple input updates between Host frames each dispatch once through the real service", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); int count = 0;
        Check(f.Input("a").Register(Action("f8", "F8"), _ => count++).Success);
        f.Backend.QueueInput(1, "F8"); f.Backend.QueueInput(2, "F8"); f.Service.Tick(); Equal(2, count);
        f.Service.Tick(); Equal(2, count); f.Backend.QueueInput(3, "F8"); f.Service.Tick(); Equal(3, count);
    }),
    ("Already-drained queued edges cannot reach an identically re-created action", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var old = 0; var replacement = 0; IInputActionHandle? action = null;
        action = f.Input("a").Register(Action("f8", "F8"), _ => {
            old++; action!.Dispose(); Check(f.Input("a").Register(Action("f8", "F8"), _ => replacement++).Success);
        }).Value!;
        f.Backend.QueueInput(1, "F8"); f.Backend.QueueInput(2, "F8"); f.Service.Tick();
        Equal(1, old); Equal(0, replacement);
        f.Backend.QueueInput(3, "F8"); f.Service.Tick(); Equal(1, replacement);
    }),
    ("Scope withdrawal and rebind discard their queued action epochs", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var first = 0; var second = 0;
        var action = f.Input("a").Register(Action("key", "F8"), _ => first++).Value!;
        f.Backend.QueueInput(1, "F8"); Check(action.Rebind(new(InputDeviceKind.Keyboard, "F9")).Success);
        f.Service.Tick(); Equal(0, first); f.Backend.QueueInput(2, "F9"); f.Service.Tick(); Equal(1, first);
        f.Backend.QueueInput(3, "F9"); f.Host.Disable("a"); Check(f.Input("b").Register(Action("fresh", "F9"), _ => second++).Success);
        f.Service.Tick(); Equal(1, first); Equal(0, second);
        f.Backend.QueueInput(4, "F9"); f.Service.Tick(); Equal(1, second);
    }),
    ("Queued menu edges do not transfer to a later gameplay registration sharing the key", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var menu = 0; var game = 0;
        f.Input("a").Register(Action("menu", "F8", InputActionContext.Menu), _ => menu++);
        f.Backend.QueueInput(1, "F8");
        f.Input("b").Register(Action("game", "F8", InputActionContext.Gameplay), _ => game++);
        f.Backend.Frame = Empty() with { Context = InputActionContext.Gameplay }; f.Service.Tick(); Equal(0, menu); Equal(0, game);
        f.Backend.Frame = Empty() with { Context = InputActionContext.Gameplay };
        f.Backend.QueueInput(2, "F8"); f.Service.Tick(); Equal(1, game);
    }),
    ("Queued updates re-evaluate modal focus after each callback and honor current text editing", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var next = 0;
        f.Input("a").Register(Action("open", "F1"), _ => f.Ui("a").CreateWindow(Window() with { Modal = true }));
        f.Input("b").Register(Action("next", "F2"), _ => next++);
        f.Backend.QueueInput(1, "F1"); f.Backend.QueueInput(2, "F2"); f.Service.Tick(); Equal(0, next);
        f.Host.Disable("a"); f.Backend.QueueInput(3, "F2"); f.Backend.Frame = Empty() with { ExternalTextEditing = true }; f.Service.Tick(); Equal(0, next);
        f.Backend.QueueInput(4, "F2"); f.Service.Tick(); Equal(1, next);
    }),
    ("Closing a modal before Host dispatch cannot replay its blocked shortcut", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var count = 0;
        f.Input("b").Register(Action("key", "F8"), _ => count++);
        var modal = f.Ui("a").CreateWindow(Window() with { Modal = true }).Value!;
        f.Backend.QueueInput(1, "F8"); modal.Dispose(); f.Service.Tick(); Equal(0, count);
        f.Backend.QueueInput(2, "F8"); f.Service.Tick(); Equal(1, count);
    }),
    ("Native text editing at capture prevents shortcut delivery after editing ends", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var count = 0;
        f.Input("a").Register(Action("key", "F8"), _ => count++);
        f.Backend.Frame = Empty() with { ExternalTextEditing = true };
        f.Backend.QueueInput(1, "F8"); f.Backend.Frame = Empty(); f.Service.Tick(); Equal(0, count);
        f.Backend.QueueInput(2, "F8"); f.Service.Tick(); Equal(1, count);
    }),
    ("Owned text focus at capture is not lost when the field blurs before dispatch", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var count = 0;
        var window = f.Ui("a").CreateWindow(Window()).Value!;
        var field = window.AddTextField(Control()).Value!; Check(field.Focus().Success);
        f.Input("a").Register(Action("key", "F8"), _ => count++);
        f.Service.Tick(); f.Service.Tick(); // Let the two-frame native focus request settle.
        f.Backend.QueueInput(1, "F8"); f.Backend.Last.Controls.Single().Focused = false;
        f.Service.Tick(); Equal(0, count);
        f.Backend.QueueInput(2, "F8"); f.Service.Tick(); Equal(1, count);
    }),
    ("Window focus restored after capture does not inherit another window's key", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var count = 0;
        var first = f.Ui("a").CreateWindow(Window()).Value!;
        var second = f.Ui("b").CreateWindow(Window()).Value!;
        f.Input("a").Register(Action("key", "F8") with { WindowId = "panel" }, _ => count++);
        Check(second.Focus().Success); f.Backend.QueueInput(1, "F8");
        Check(first.Focus().Success); f.Service.Tick(); Equal(0, count);
        f.Backend.QueueInput(2, "F8"); f.Service.Tick(); Equal(1, count);
    }),
    ("Application focus regained before dispatch cannot replay a background key", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var count = 0;
        f.Input("a").Register(Action("key", "F8"), _ => count++);
        f.Backend.ApplicationFocused = false; f.Backend.QueueInput(1, "F8");
        f.Backend.ApplicationFocused = true; f.Service.Tick(); Equal(0, count);
        f.Backend.QueueInput(2, "F8"); f.Service.Tick(); Equal(1, count);
    }),
    ("A native button callback cannot transfer a queued key to a replacement action", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var old = 0; var replacement = 0;
        var action = f.Input("a").Register(Action("key", "F8"), _ => old++).Value!;
        var window = f.Ui("a").CreateWindow(Window()).Value!;
        window.AddButton(Control(), () => {
            action.Dispose();
            Check(f.Input("a").Register(Action("key", "F8"), _ => replacement++).Success);
        });
        var button = f.Backend.Last.Controls.Single(); button.UsesNativeActivation = true;
        f.Backend.QueueInput(1, "F8"); button.QueueActivations(); f.Service.Tick();
        Equal(0, old); Equal(0, replacement);
        f.Backend.QueueInput(2, "F8"); f.Service.Tick(); Equal(1, replacement);
    }),
    ("An earlier action callback cannot rebind another action onto somebody else's queued key", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var old = 0; var recipient = 0;
        IInputActionHandle? previous = null, existing = null;
        f.Input("a").Register(Action("trigger", "F1"), _ => {
            previous!.Dispose(); Check(existing!.Rebind(new(InputDeviceKind.Keyboard, "F8")).Success);
        });
        previous = f.Input("a").Register(Action("old", "F8"), _ => old++).Value!;
        existing = f.Input("b").Register(Action("existing", "F9"), _ => recipient++).Value!;
        f.Backend.QueueInput(1, "F1", "F8"); f.Service.Tick(); Equal(0, old); Equal(0, recipient);
        f.Backend.QueueInput(2, "F8"); f.Service.Tick(); Equal(1, recipient);
    }),
    ("A shortcut that changes scenes retires the rest of the same input update", () => {
        using var f = new Fixture();
        f.Backend.Edges = new();
        f.Backend.UseNativeSceneInput = true;
        f.Backend.GamepadReady = true;
        var transitionKey = new InputBinding(InputDeviceKind.Keyboard, "F1");
        var menuKey = new InputBinding(InputDeviceKind.Keyboard, "F8");
        var gamepadButton = new InputBinding(InputDeviceKind.Gamepad, "ButtonSouth");
        var transitions = 0;
        var menuCalls = 0;
        var gamepadCalls = 0;
        Check(f.Input("a").Register(new("transition", transitionKey, InputActionContext.Menu), _ => {
            transitions++;
            f.Backend.SceneIdentity = "2:MainMenu";
        }).Success);
        Check(f.Input("b").Register(new("menu", menuKey, InputActionContext.Menu), _ => menuCalls++).Success);
        Check(f.Input("b").Register(new("pad", gamepadButton), _ => gamepadCalls++).Success);

        f.Backend.QueueBindings(1, transitionKey, menuKey, gamepadButton);
        f.Service.Tick();
        Equal(1, transitions);
        Equal((0, 0), (menuCalls, gamepadCalls));

        f.Service.Tick();
        f.Backend.QueueBindings(2, menuKey, gamepadButton);
        f.Service.Tick();
        Equal((1, 1), (menuCalls, gamepadCalls));
    }),
    ("Scene reset and collector disposal reject queued history while native buttons stay independent", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); var keys = 0; var clicks = 0;
        f.Input("a").Register(Action("key", "F8"), _ => keys++); f.Backend.QueueInput(1, "F8");
        f.Backend.SceneIdentity = "2:MainMenu"; f.Service.Tick(); Equal(0, keys);
        var window = f.Ui("a").CreateWindow(Window()).Value!; window.AddButton(Control(), () => clicks++);
        var button = f.Backend.Last.Controls.Single(); button.UsesNativeActivation = true;
        f.Backend.Edges.Dispose(); button.QueueActivations(); f.Service.Tick(); Equal(1, clicks); Equal(0, keys);
        Check(!f.Backend.Edges.Add(2, 16, Array.Empty<InputBindingOwner>(), Array.Empty<InputBinding>(), null, true, false));
    }),
    ("Batch and edge quota overflow clears and retires the collector explicitly", () => {
        var key = new InputBinding(InputDeviceKind.Keyboard, "F8");
        var owners = new[] { new InputBindingOwner("a", "a/key", key, InputActionContext.Menu, null) };
        using var batches = new InputUpdateEdgeCollector(capacity: 1);
        Check(batches.Add(1, 16, owners, new[] { key }, null, false, false));
        Check(!batches.Add(2, 16, owners, new[] { key }, null, false, false));
        Check(batches.Retired && batches.Failure!.Contains("overflowed")); Equal(1L, batches.OverflowCount); Equal(0, batches.Drain().Count);
        using var edges = new InputUpdateEdgeCollector(edgeCapacity: 1);
        Check(edges.Add(1, 16, owners, new[] { key }, null, false, false));
        var captured = edges.Drain().Single().Actions.Single(); Check(edges.IsCurrent(captured));
        Check(edges.Add(2, 16, owners, new[] { key }, null, false, false));
        Check(!edges.Add(3, 16, owners, new[] { key }, null, false, false));
        Check(edges.Retired && !edges.IsCurrent(captured) && edges.Drain().Count == 0);
    }),
    ("Context filtering allows disjoint menu and gameplay registrations", () => {
        using var f = new Fixture(); int menu = 0, game = 0;
        f.Input("a").Register(Action("menu", "F1", InputActionContext.Menu), _ => menu++);
        f.Input("b").Register(Action("game", "F1", InputActionContext.Gameplay), _ => game++);
        f.Press("F1", InputActionContext.Menu); f.Press("F1", InputActionContext.Gameplay); f.Press("F1", InputActionContext.None);
        Equal(1, menu); Equal(1, game);
    }),
    ("Modal focus blocks global shortcuts while the top modal's own action remains usable", () => {
        using var f = new Fixture(); int global = 0, modal = 0;
        f.Input("b").Register(Action("global", "F1"), _ => global++);
        var window = f.Ui("a").CreateWindow(Window() with { Modal = true }).Value!;
        f.Input("a").Register(Action("close", "F2") with { WindowId = "panel" }, _ => modal++);
        f.Press("F1"); f.Press("F2"); Equal(0, global); Equal(1, modal);
        window.Dispose(); f.Press("F1"); Equal(1, global); Check(f.Input("a").GetBindings().All(x => x.ActionId != "a/close"));
    }),
    ("Nested modals preserve the stack and removing a lower modal cannot steal focus", () => {
        using var f = new Fixture(); var lower = f.Ui("a").CreateWindow(Window() with { Modal = true }).Value!;
        var upper = f.Ui("b").CreateWindow(Window() with { Modal = true }).Value!;
        Equal(2, f.Input("a").GetFocusStack().Count); Code(ErrorCode.Conflict, lower.Focus().Error);
        lower.Dispose(); Equal("b/panel", f.Input("a").GetFocusStack().Single().WindowId); upper.Dispose(); Equal(0, f.Input("a").GetFocusStack().Count);
    }),
    ("Closing the top modal returns framework focus to the previous owned window", () => {
        using var f = new Fixture(); var lower = f.Ui("a").CreateWindow(Window() with { Modal = true }).Value!;
        var upper = f.Ui("b").CreateWindow(Window() with { Modal = true }).Value!; upper.Dispose();
        Equal(lower.Id, f.Input("a").GetFocusStack().Single().WindowId); Check(lower.Focus().Success);
    }),
    ("Opening a modal over a pending text focus still delivers its first captured shortcut", () => {
        using var f = new Fixture();
        f.Backend.Edges = new();
        var lower = f.Ui("a").CreateWindow(Window() with { Modal = true }).Value!;
        var field = lower.AddTextField(Control()).Value!;
        var nativeField = f.Backend.Last.Controls.Single();
        int lowerCalls = 0, upperCalls = 0;
        Check(f.Input("a").Register(Action("shortcut", "F1") with { WindowId = "panel" }, _ => lowerCalls++).Success);
        Check(field.Focus().Success);

        var upper = f.Ui("b").CreateWindow(Window() with { Modal = true }).Value!;
        nativeField.Focused = false; // Unity deselects the lower field when the upper panel is selected.
        Check(f.Input("b").Register(Action("shortcut", "F2") with { WindowId = "panel" }, _ => upperCalls++).Success);
        f.Backend.QueueInput(1, "F1", "F2");
        Equal(1, f.Backend.Edges.Pending);
        f.Service.Tick();
        Equal(0, lowerCalls);
        Equal(1, upperCalls);

        upper.Dispose();
        Equal(lower.Id, f.Input("a").GetFocusStack().Single().WindowId);
        f.Backend.QueueInput(2, "F1");
        f.Service.Tick();
        Equal(1, lowerCalls);

        Check(field.Focus().Success);
        f.Service.Tick();
        f.Service.Tick(); // Wait for native focus to settle before testing active editing.
        f.Backend.QueueInput(3, "F1");
        f.Service.Tick();
        Equal(1, lowerCalls);
    }),
    ("A covered field's late native focus sample cannot block the current modal's shortcut", () => {
        using var f = new Fixture();
        f.Backend.Edges = new();
        var lower = f.Ui("a").CreateWindow(Window() with { Modal = true }).Value!;
        var field = lower.AddTextField(Control()).Value!;
        var nativeField = f.Backend.Last.Controls.Single();
        Check(field.Focus().Success);

        f.Ui("b").CreateWindow(Window() with { Modal = true });
        Check(nativeField.Focused); // Its last native sample still reports focus; the top window owns input now.
        var calls = 0;
        Check(f.Input("b").Register(Action("shortcut", "F2") with { WindowId = "panel" }, _ => calls++).Success);
        f.Service.Tick();
        f.Backend.QueueInput(1, "F2");
        Equal(1, f.Backend.Edges.Pending);
        f.Service.Tick();
        Equal(1, calls);
    }),
    ("Text input suppresses corresponding framework shortcuts including its owner's", () => {
        using var f = new Fixture(); int count = 0; var window = f.Ui("a").CreateWindow(Window()).Value!;
        var text = window.AddTextField(Control()).Value!; f.Input("a").Register(Action("edit", "F1"), _ => count++); text.Focus();
        f.Press("F1"); Equal(0, count); f.Backend.Last.Controls.Single().Focused = false; f.Service.Tick(); f.Service.Tick(); f.Press("F1"); Equal(1, count);
        f.Backend.Frame = Empty() with { ExternalTextEditing = true, Pressed = new[] { new InputBinding(InputDeviceKind.Keyboard, "F1") } }; f.Service.Tick(); Equal(1, count);
    }),
    ("One callback opening a modal immediately prevents the next shortcut callback", () => {
        using var f = new Fixture(); int next = 0;
        f.Input("a").Register(Action("open", "F1"), _ => f.Ui("a").CreateWindow(Window() with { Modal = true }));
        f.Input("b").Register(Action("next", "F2"), _ => next++);
        f.Backend.Frame = Empty() with { Pressed = new[] { new InputBinding(InputDeviceKind.Keyboard, "F1"), new InputBinding(InputDeviceKind.Keyboard, "F2") } }; f.Service.Tick(); Equal(0, next);
    }),
    ("Self-disable during dispatch removes all following callbacks from that scope", () => {
        using var f = new Fixture(); int later = 0;
        f.Input("a").Register(Action("stop", "F1"), _ => f.Host.Disable("a"));
        f.Input("a").Register(Action("later", "F2"), _ => later++);
        f.Backend.Frame = Empty() with { Pressed = new[] { new InputBinding(InputDeviceKind.Keyboard, "F1"), new InputBinding(InputDeviceKind.Keyboard, "F2") } }; f.Service.Tick(); Equal(0, later);
    }),
    ("Faulting shortcut and text callbacks are isolated with owner diagnostics", () => {
        using var f = new Fixture(); int healthy = 0;
        f.Input("a").Register(Action("bad", "F1"), _ => throw new InvalidOperationException("callback failure"));
        f.Input("b").Register(Action("good", "F2"), _ => healthy++); f.Press("F1"); f.Press("F2"); Equal(1, healthy); Check(f.Input("b").GetBindings().All(x => x.PluginId != "a"));
        var window = f.Ui("a").CreateWindow(Window()).Value!; int faults = 0;
        window.AddTextField(Control(), _ => { faults++; throw new InvalidOperationException("text failure"); });
        f.Backend.Last.Controls[0].Text = "first"; f.Service.Tick(); f.Backend.Last.Controls[0].Text = "second"; f.Service.Tick(); Equal(1, faults); Check(f.Log.Messages.Any(x => x.Contains("a/panel")));
    }),
    ("Overlapping panels route pointer clicks to the top owned panel only", () => {
        using var f = new Fixture(); int lower = 0, upper = 0, shortcut = 0;
        f.Ui("a").CreateWindow(Window()).Value!.AddButton(Control(), () => lower++);
        f.Ui("b").CreateWindow(Window()).Value!.AddButton(Control(), () => upper++);
        f.Input("a").Register(new("mouse", new(InputDeviceKind.Mouse, "Left")), _ => shortcut++);
        f.Mouse(true, false, new(10, 10)); f.Mouse(false, true, new(10, 10)); Equal(0, lower); Equal(1, upper); Equal(0, shortcut);
    }),
    ("Button callback requires matching left release inside and is removed on disposal", () => {
        using var f = new Fixture(); int count = 0; var window = f.Ui("a").CreateWindow(Window()).Value!; var button = window.AddButton(Control(), () => count++).Value!;
        f.Mouse(true, false, new(10, 10)); f.Mouse(false, true, new(900, 900)); Equal(0, count);
        button.Dispose(); f.Mouse(true, false, new(10, 10)); f.Mouse(false, true, new(10, 10)); Equal(0, count);
    }),
    ("Native button activation is delivered without mouse input and drained exactly once", () => {
        using var f = new Fixture(); int count = 0;
        f.Ui("a").CreateWindow(Window()).Value!.AddButton(Control(), () => count++);
        var native = f.Backend.Last.Controls.Single(); native.UsesNativeActivation = true; native.QueueActivations();
        f.Backend.Frame = Empty(); f.Service.Tick(); Equal(1, count); Equal(1, native.DrainCount); Equal(0, native.PendingActivations);
        f.Service.Tick(); f.Service.Tick(); Equal(1, count); Equal(3, native.DrainCount);
    }),
    ("Native activation replaces geometric mouse activation in the same frame", () => {
        using var f = new Fixture(); int count = 0;
        f.Ui("a").CreateWindow(Window()).Value!.AddButton(Control(), () => count++);
        var native = f.Backend.Last.Controls.Single(); native.UsesNativeActivation = true;
        f.Mouse(true, false, new(10, 10)); Equal(0, count); native.QueueActivations();
        f.Mouse(false, true, new(10, 10)); Equal(1, count);
        // A second plain geometric click cannot create a second source of native-button callbacks.
        f.Mouse(true, false, new(10, 10)); f.Mouse(false, true, new(10, 10)); Equal(1, count);
    }),
    ("Queued native activations cannot survive control window scope or scene expiration", () => {
        foreach (var expiration in new[] { "control", "window", "scope", "scene" })
        {
            using var f = new Fixture(); int count = 0;
            var window = f.Ui("a").CreateWindow(Window()).Value!; var button = window.AddButton(Control(), () => count++).Value!;
            var native = f.Backend.Last.Controls.Single(); native.UsesNativeActivation = true; native.QueueActivations(2);
            switch (expiration)
            {
                case "control": button.Dispose(); break;
                case "window": window.Dispose(); break;
                case "scope": f.Host.Disable("a"); break;
                case "scene": f.Backend.SceneIdentity = "2:MainMenu"; break;
            }
            f.Service.Tick(); f.Service.Tick(); Equal(0, count); Check(!native.Alive); Equal(0, native.PendingActivations);
            // A fresh control with the same local IDs has a new native queue, so an old activation cannot target it.
            if (expiration == "scope") continue;
            if (expiration == "control") window.Dispose();
            var replacement = f.Ui("a").CreateWindow(Window()).Value!; replacement.AddButton(Control(), () => count++);
            f.Backend.Last.Controls.Single().UsesNativeActivation = true; f.Service.Tick(); Equal(0, count);
        }
    }),
    ("A modal discards another window's queued native activations rather than replaying them later", () => {
        using var f = new Fixture(); int count = 0;
        f.Ui("a").CreateWindow(Window()).Value!.AddButton(Control(), () => count++);
        var native = f.Backend.Last.Controls.Single(); native.UsesNativeActivation = true; native.QueueActivations(3);
        var modal = f.Ui("b").CreateWindow(Window() with { Modal = true }).Value!;
        f.Service.Tick(); Equal(0, count); Equal(1, native.DrainCount); Equal(0, native.PendingActivations);
        modal.Dispose(); f.Service.Tick(); Equal(0, count);
        native.QueueActivations(); f.Service.Tick(); Equal(1, count);
    }),
    ("First native callback opening a modal stops the same batch and later buttons immediately", () => {
        using var f = new Fixture(); int first = 0, later = 0; IUiWindowHandle? modal = null;
        var window = f.Ui("a").CreateWindow(Window()).Value!;
        window.AddButton(Control("open"), () => {
            first++; if (modal is null) modal = f.Ui("b").CreateWindow(Window() with { Modal = true }).Value!;
        });
        window.AddButton(Control("later"), () => later++);
        var nativeFirst = f.Backend.Last.Controls[0]; var nativeLater = f.Backend.Last.Controls[1];
        nativeFirst.UsesNativeActivation = nativeLater.UsesNativeActivation = true;
        nativeFirst.QueueActivations(3); nativeLater.QueueActivations(2);
        f.Service.Tick(); Equal(1, first); Equal(0, later); Check(modal is not null);
        Equal(0, nativeFirst.PendingActivations); Equal(0, nativeLater.PendingActivations);
        modal!.Dispose(); f.Service.Tick(); Equal(1, first); Equal(0, later);
    }),
    ("Native activation floods have a bounded per-frame dispatch and excess is not replayed", () => {
        using var f = new Fixture(); int count = 0;
        f.Ui("a").CreateWindow(Window()).Value!.AddButton(Control(), () => count++);
        var native = f.Backend.Last.Controls.Single(); native.UsesNativeActivation = true; native.QueueActivations(20);
        f.Service.Tick(); Equal(8, count); Equal(0, native.PendingActivations);
        f.Service.Tick(); Equal(8, count); native.QueueActivations(); f.Service.Tick(); Equal(9, count);
    }),
    ("Scene identity changes destroy both actual mount windows and overlay windows", () => {
        using var f = new Fixture(); var overlay = f.Ui("a").CreateWindow(Window()).Value!; var menu = f.Ui("b").CreateWindow(Window() with { MountId = UiMountIds.MainMenu }).Value!;
        var control = overlay.AddLabel(Control()).Value!; f.Backend.SceneIdentity = "2:MainMenu"; f.Service.Tick();
        Check(!overlay.IsAlive && !menu.IsAlive); Code(ErrorCode.TargetExpired, control.SetText("late").Error); Equal(0, f.Backend.LiveWindows);
        Check(f.Ui("a").CreateWindow(Window()).Success); Code(ErrorCode.TargetExpired, control.GetText().Error);
    }),
    ("Returning through FakeMainMenu retires old focus and admits only fresh menu input", () => {
        using var f = new Fixture(); f.Backend.Edges = new(); f.Backend.UseNativeSceneInput = true;
        var old = f.Ui("a").CreateWindow(Window() with { Modal = true }).Value!;
        var control = old.AddLabel(Control()).Value!; Check(old.Focus().Success);
        var menuKeys = 0; var globalKeys = 0;
        Check(f.Input("b").Register(Action("menu-return", "F8") with { Context = InputActionContext.Menu }, _ => menuKeys++).Success);
        Check(f.Input("b").Register(Action("global-return", "F9") with { Context = InputActionContext.Global }, _ => globalKeys++).Success);
        f.Backend.QueueInput(1, "F8"); f.Backend.SceneIdentity = "2:FakeMainMenu"; f.Service.Tick();
        Equal(0, menuKeys); Check(!old.IsAlive); Code(ErrorCode.TargetExpired, control.SetText("stale").Error);
        Equal(0, f.Input("b").GetFocusStack().Count);
        Check(f.Ui("b").CreateWindow(Window() with { MountId = UiMountIds.MainMenu }).Success);
        f.Backend.QueueInput(2, "F8"); f.Service.Tick(); Equal(1, menuKeys);
        f.Backend.SceneIdentity = "3:UnknownScene"; f.Service.Tick();
        f.Backend.QueueInput(3, "F8", "F9"); f.Service.Tick(); Equal(1, menuKeys); Equal(1, globalKeys);
    }),
    ("Mount expiry retires controls and stale handles cannot affect replacement instances", () => {
        using var f = new Fixture(); var window = f.Ui("a").CreateWindow(Window()).Value!; var control = window.AddLabel(Control()).Value!;
        f.Backend.Last.Expired = true; f.Service.Tick(); Check(!window.IsAlive); Code(ErrorCode.TargetExpired, control.SetText("stale").Error);
        var replacement = f.Ui("a").CreateWindow(Window()).Value!; control.Dispose(); Check(replacement.IsAlive);
    }),
    ("Late public scene notification retires a probe window created after native scene detection", () => {
        var root = Path.Combine(Path.GetTempPath(), "RizToolbox.ProbeScene." + Guid.NewGuid().ToString("N"));
        try
        {
            using var f = new Fixture(root);
            Check(f.Contexts["a"].Configuration.Save("local-catalog-proof", new { Enabled = true }).Success);
            LocalCatalogRuntimeProbe.Start(f.Contexts["a"]);
            f.Service.Tick(); f.Events.Publish(new FrameUpdate(2)); Equal(1, f.Backend.LiveWindows);
            f.Events.Publish(new GameSceneChanged("GameStart", "MainMenu") { SceneGeneration = 2 });
            Equal(0, f.Backend.LiveWindows);
            f.Events.Publish(new FrameUpdate(4)); Equal(1, f.Backend.LiveWindows); Equal(2, f.Backend.Created);
            Check(!f.Log.Messages.Any(x => x.Contains("WINDOW REFUSED", StringComparison.Ordinal)));
        }
        finally
        {
            var config = Path.Combine(root, "config", "plugin-a", "config-local-catalog-proof.json");
            if (File.Exists(config)) File.Delete(config);
            if (Directory.Exists(Path.GetDirectoryName(config)!)) Directory.Delete(Path.GetDirectoryName(config)!);
            if (Directory.Exists(Path.Combine(root, "config"))) Directory.Delete(Path.Combine(root, "config"));
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }),
    ("Context disable cleans windows actions focus callbacks and rejects old service views", () => {
        using var f = new Fixture(); var ui = f.Ui("a"); var input = f.Input("a");
        var window = ui.CreateWindow(Window() with { Modal = true }).Value!; window.AddTextField(Control()).Value!.Focus();
        var action = input.Register(Action("key", "F1"), _ => { }).Value!; f.Host.Disable("a");
        Equal(0, f.Backend.LiveWindows); Equal(0, f.Input("b").GetFocusStack().Count); Equal(0, f.Input("b").GetBindings().Count);
        Code(ErrorCode.ScopeExpired, action.Rebind(new(InputDeviceKind.Keyboard, "F2")).Error);
        Code(ErrorCode.ScopeExpired, ui.CreateWindow(Window()).Error);
    }),
    ("Wrong-thread mutations return WrongThread without touching backend", () => {
        using var f = new Fixture(); var direct = f.Service.ForPlugin(f.Contexts["a"]); Result<IUiWindowHandle>? result = null;
        var thread = new Thread(() => result = direct.CreateWindow(Window())); thread.Start(); thread.Join(); Code(ErrorCode.WrongThread, result!.Error); Equal(0, f.Backend.Created);
    }),
    ("Native creation and control construction failures roll back all transferred objects", () => {
        using var f = new Fixture(); f.Backend.FailCreate = true; Code(ErrorCode.InternalError, f.Ui("a").CreateWindow(Window()).Error); Equal(0, f.Backend.LiveWindows);
        f.Backend.FailCreate = false; var window = f.Ui("a").CreateWindow(Window()).Value!; f.Backend.Last.FailControlRead = true;
        Code(ErrorCode.InternalError, window.AddLabel(Control()).Error); Equal(0, f.Backend.Last.Controls.Count); window.Dispose(); Equal(0, f.Backend.LiveWindows);
    }),
    ("One cleanup failure cannot leave another plugin window or callback behind", () => {
        using var f = new Fixture(); f.Ui("a").CreateWindow(Window()); f.Backend.Last.ThrowDispose = true;
        f.Ui("b").CreateWindow(Window()); f.Service.Dispose(); Equal(0, f.Backend.LiveWindows); Check(f.Backend.Disposed);
    }),
    ("Navigation is a declared one-way original menu command and rejects invalid state and modals", () => {
        using var f = new Fixture(); var nav = f.Nav("a"); Check(nav.GetTargets().Single().NativeMethod.Contains("EnterCurrentButton"));
        Code(ErrorCode.OperationUnsupported, nav.Navigate("invented.page").Error); f.Backend.NavigationReady = false;
        Code(ErrorCode.NavigationUnavailable, nav.Navigate(NativeUiInputBackend.ActivateSelected).Error); f.Backend.NavigationReady = true;
        var modal = f.Ui("a").CreateWindow(Window() with { Modal = true }).Value!; Code(ErrorCode.NavigationUnavailable, nav.Navigate(NativeUiInputBackend.ActivateSelected).Error);
        modal.Dispose(); Check(nav.Navigate(NativeUiInputBackend.ActivateSelected).Success); Equal(1, f.Backend.NavigationCalls);
    }),
    ("Layouts update scaling and reject shrinking across existing controls", () => {
        using var f = new Fixture(); var window = f.Ui("a").CreateWindow(Window()).Value!; window.AddLabel(Control() with { Rect = new(300, 0, 180, 30) });
        Code(ErrorCode.InvalidArgument, window.SetLayout(new(new(0, 0, 400, 200))).Error);
        Check(window.SetLayout(new(new(0, 0, 800, 500), 1280, 720)).Success); f.Service.Tick(); Equal(1280f, window.GetViewport().Value!.ReferenceWidth);
    }),
    ("Reference coordinates round-trip at 4K and ultrawide while letterbox clicks are rejected", () => {
        var layout = new UiLayout(new(0, 0, 500, 400));
        var fourK = UiCoordinates.Fit(3840, 2160, 144, new(0, 0, 3840, 2160), layout); Equal(2f, fourK.PixelsPerReferenceUnit);
        var wide = UiCoordinates.Fit(3440, 1440, 0, new(0, 0, 3440, 1440), layout); Check(wide.LetterboxX > 0 && wide.Dpi is null);
        var point = new UiPoint(123, 456); var back = UiCoordinates.ScreenToUi(wide, UiCoordinates.UiToScreen(wide, point).Value!).Value!;
        Check(Math.Abs(point.X - back.X) < .001 && Math.Abs(point.Y - back.Y) < .001); Code(ErrorCode.InvalidArgument, UiCoordinates.ScreenToUi(wide, new(0, 720)).Error);
        Code(ErrorCode.InvalidArgument, UiCoordinates.UiToScreen(wide, new(float.NaN, 2)).Error);
    }),
    ("Portrait and offset pixel viewports preserve lower-left screen versus top-left UI origins", () => {
        var view = UiCoordinates.Fit(1080, 1920, null, new(20, 40, 1000, 1800), new(new(0, 0, 500, 400)));
        var top = UiCoordinates.UiToScreen(view, new(0, 0)).Value!; var bottom = UiCoordinates.UiToScreen(view, new(0, 1080)).Value!;
        Check(top.Y > bottom.Y); Check(Math.Abs(UiCoordinates.ScreenToUi(view, top).Value!.Y) < .001);
        Throws<ArgumentException>(() => UiCoordinates.Fit(0, 100, null, new(0, 0, 100, 100), new(new(0, 0, 50, 50))));
    }),
    ("UI input stays dormant without demand and releases only after its retained consumer ends", () => {
        using var main = new MainThreadDispatcher(); var log = new CaptureLog(); var events = new EventHub(log); var backends = new List<Backend>();
        var module = new UiInputModule(() => { var backend = new Backend(); backends.Add(backend); return backend; }, main, log, events);
        var registry = new CapabilityRegistry(new("test", "test", "test", "test"));
        using var modules = new ModuleManager(registry, new UiDemandSymbols(), log, "test", "test");
        modules.Add(module, RizToolbox.Adapter.RizlineAdapter.IsDemandManaged(module));
        modules.Tick(0); events.Publish(new FrameUpdate(0)); Equal(0, backends.Count);
        modules.SetCapabilityDemand(new[] { UiInputCapabilityIds.Actions }); modules.Tick(1);
        var first = backends.Single(); var before = first.Captures;
        Equal(4, module.Manifest.Capabilities.Count); events.Publish(new FrameUpdate(1)); Equal(before + 1, first.Captures);
        var hostView = module.GetHostUi();
        modules.SetRetainedCapability(UiInputCapabilityIds.Windows, true);
        modules.SetCapabilityDemand(Array.Empty<string>()); modules.Tick(2); Check(!first.Disposed);
        modules.SetRetainedCapability(UiInputCapabilityIds.Windows, false); modules.Tick(3);
        events.Publish(new FrameUpdate(3)); Equal(before + 1, first.Captures); Check(first.Disposed);
        Equal(0, hostView.GetMounts().Count);
        modules.SetCapabilityDemand(new[] { UiInputCapabilityIds.Windows }); modules.Tick(4);
        Equal(2, backends.Count); Check(!ReferenceEquals(hostView, module.GetHostUi()));
    }),
    ("Window action generation expires when a same-ID window is reconstructed", () => {
        using var f = new Fixture(); var first = f.Ui("a").CreateWindow(Window()).Value!; int old = 0;
        f.Input("a").Register(Action("old", "F1") with { WindowId = "panel" }, _ => old++); first.Dispose();
        var next = f.Ui("a").CreateWindow(Window()).Value!; next.Focus(); f.Press("F1"); Equal(0, old); Equal(0, f.Input("a").GetBindings().Count);
    }),
    ("Repeated window/action release does not retain managed callbacks in the scope view", () => {
        using var f = new Fixture(); var view = f.Service.ForPlugin(f.Contexts["a"]);
        for (var i = 0; i < 100; i++)
        {
            var window = view.CreateWindow(Window()).Value!; window.AddButton(Control(), () => { });
            var action = view.Register(Action("key", "F1") with { WindowId = "panel" }, _ => { }).Value!; window.Dispose(); action.Dispose();
        }
        Equal(0, view.RetainedOwnedCount); Equal(0, f.Backend.LiveWindows); Equal(0, view.GetBindings().Count);
    }),
    ("Throwing diagnostic sinks cannot stop later callbacks or owned cleanup", () => {
        using var f = new Fixture(); int good = 0; f.Log.Throw = true;
        f.Input("a").Register(Action("bad", "F1"), _ => throw new InvalidOperationException("callback"));
        f.Input("b").Register(Action("good", "F2"), _ => good++);
        f.Backend.Frame = Empty() with { Pressed = new[] { new InputBinding(InputDeviceKind.Keyboard, "F1"), new InputBinding(InputDeviceKind.Keyboard, "F2") } }; f.Service.Tick(); Equal(1, good);
        f.Ui("a").CreateWindow(Window()); f.Backend.Last.ThrowDispose = true; f.Ui("b").CreateWindow(Window()); f.Service.Dispose(); Equal(0, f.Backend.LiveWindows);
    }),
    ("Exact Vector3 ABI allows only the adapter struct and rejects matching arbitrary structs", () => {
        var type = Type.GetType("RizToolbox.Runtime.NativeAbi, RizToolbox.Runtime", true)!; var size = type.GetMethod("SizeOf", BindingFlags.Static | BindingFlags.NonPublic)!;
        Equal(12, (int)size.Invoke(null, new object[] { typeof(NativeVector3) })!); Throws<TargetInvocationException>(() => size.Invoke(null, new object[] { typeof(ImpostorVector3) }));
        var assembly = type.Assembly; var fieldType = assembly.GetType("RizToolbox.Runtime.NativeFieldLayout")!; var layoutType = assembly.GetType("RizToolbox.Runtime.NativeTypeLayout")!;
        var kindType = assembly.GetType("RizToolbox.Runtime.NativeValueKind")!; var fields = Array.CreateInstance(fieldType, 3);
        for (var i = 0; i < 3; i++) fields.SetValue(Activator.CreateInstance(fieldType, new object[] { new[] { "x", "y", "z" }[i], "System.Single", i * 4 }), i);
        object Layout(int bytes) => Activator.CreateInstance(layoutType, new object?[] { "UnityEngine.Vector3", Enum.Parse(kindType, "Struct"), bytes, false, IntPtr.Zero, null, fields })!;
        var validate = type.GetMethod("ValidateNativeStruct", BindingFlags.Static | BindingFlags.NonPublic)!;
        validate.Invoke(null, new[] { Layout(12) }); Throws<TargetInvocationException>(() => validate.Invoke(null, new[] { Layout(16) }));
        fields.SetValue(Activator.CreateInstance(fieldType, new object[] { "z", "System.Int32", 8 }), 2); Throws<TargetInvocationException>(() => validate.Invoke(null, new[] { Layout(12) }));
    })
};
checks = checks.Concat(GamepadInputRegression.Cases).ToArray();
checks = checks.Concat(CanvasProjectionRegression.Cases).ToArray();
checks = checks.Concat(OfficialChartSelectionRegression.Cases).ToArray();
checks = checks.Append(("LOCAL catalog follows the selected chart from menu to song list and dispatches only current requests", LocalCatalogScreenRegression.Run)).ToArray();
var failures = 0;
foreach (var check in checks) try { check.Run(); Console.WriteLine("PASS " + check.Name); } catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + check.Name + ": " + ex); }
Console.WriteLine($"UI/input checks: {checks.Length - failures}/{checks.Length} passed."); return failures == 0 ? 0 : 1;

static UiWindowDefinition Window(string id = "panel") => new(id, UiMountIds.Overlay, "Owned window", new(new(0, 0, 500, 400)));
static UiControlDefinition Control(string id = "control") => new(id, "text", new(0, 0, 180, 40));
static InputActionDefinition Action(string id, string key, InputActionContext context = InputActionContext.Global) => new(id, new(InputDeviceKind.Keyboard, key), context);
static UiInputFrame Empty() => new(Array.Empty<InputBinding>(), null, false, false, false, InputActionContext.Menu);
static void Check(bool value) { if (!value) throw new InvalidOperationException("Check failed."); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, observed {actual}."); }
static void Code(ErrorCode code, OperationError? error) => Equal(code, error?.Code);
static void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

[StructLayout(LayoutKind.Sequential)] readonly record struct ImpostorVector3(float X, float Y, float Z);
sealed class UiDemandSymbols : IModuleSymbolResolver
{ public AdapterSymbolProbe Probe(AdapterSymbolRequirement symbol) => new(symbol.Id, true, symbol.Type, null); }
sealed class CaptureLog : ILogger
{
    public List<string> Messages { get; } = new();
    public bool Throw;
    public void Log(LogLevel level, string message, Exception? exception = null) { Messages.Add(message); if (Throw) throw new IOException("sink failure"); }
}
sealed class Plugin : IRizPlugin
{
    private readonly Action<IPluginContext> _capture;
    public PluginManifest Manifest { get; }
    public Plugin(string id, Action<IPluginContext> capture) { Manifest = new(id, new(1, 0), new(1, 0)); _capture = capture; }
    public void Enable(IPluginContext context) => _capture(context);
    public void Disable() { }
}
sealed class Fixture : IDisposable
{
    public MainThreadDispatcher Main { get; } = new();
    public CaptureLog Log { get; } = new();
    public Backend Backend { get; } = new();
    public CapabilityRegistry Registry { get; } = new(new("test", "test", "test", "test"));
    public Dictionary<string, IScopedPluginContext> Contexts { get; } = new();
    public UiInputService Service { get; }
    public PluginHost Host { get; }
    public EventHub Events { get; }
    public Fixture(string? dataRoot = null)
    {
        var events = Events = new EventHub(Log); Service = new(Main, Log, Backend);
        Registry.RegisterScoped<IOwnedUiService>(UiInputCapabilityIds.Windows, c => Service.ForPlugin(c));
        Registry.RegisterScoped<IInputActionService>(UiInputCapabilityIds.Actions, c => Service.ForPlugin(c));
        Registry.RegisterScoped<IGameNavigationService>(UiInputCapabilityIds.Navigation, c => Service.ForPlugin(c));
        Host = new(Registry, Main, events, Log, dataRoot);
        foreach (var id in new[] { "a", "b" }) { Host.Register(new Plugin(id, c => Contexts[id] = (IScopedPluginContext)c)); if (!Host.Enable(id).Success) throw new InvalidOperationException("Enable failed."); }
    }
    public IOwnedUiService Ui(string owner) => Contexts[owner].GetService<IOwnedUiService>(UiInputCapabilityIds.Windows).Service!;
    public IInputActionService Input(string owner) => Contexts[owner].GetService<IInputActionService>(UiInputCapabilityIds.Actions).Service!;
    public IGameNavigationService Nav(string owner) => Contexts[owner].GetService<IGameNavigationService>(UiInputCapabilityIds.Navigation).Service!;
    public void Press(string key, InputActionContext context = InputActionContext.Menu) { Backend.Frame = new(new[] { new InputBinding(InputDeviceKind.Keyboard, key) }, null, false, false, false, context); Service.Tick(); }
    public void Mouse(bool down, bool up, UiPoint point) { Backend.Frame = new(down ? new[] { new InputBinding(InputDeviceKind.Mouse, "Left") } : Array.Empty<InputBinding>(), point, down, up, false, InputActionContext.Menu); Service.Tick(); }
    public void Dispose() { Host.Dispose(); Service.Dispose(); Main.Dispose(); }
}
sealed class Backend : IUiInputBackend
{
    public string SceneIdentity { get; set; } = "1:MainMenu";
    public bool MenuReady = true, KeyboardReady = true, NavigationReady = true, ApplicationFocused = true, FailCreate, Disposed, GamepadReady;
    public int Created, Captures, NavigationCalls;
    public Func<Result<OfficialChartSelectionSnapshot>>? OfficialSelection;
    public Func<OfficialChartSelectionSnapshot, Result>? OfficialPlaySubmission;
    public int OfficialSelectionReads;
    public List<NativeWindow> Windows { get; } = new();
    public NativeWindow Last => Windows.Last();
    public int LiveWindows => Windows.Count(x => x.Alive);
    public UiInputFrame Frame = new(Array.Empty<InputBinding>(), null, false, false, false, InputActionContext.Menu);
    public bool UseNativeSceneInput;
    private UiInputFrame CurrentFrame => UseNativeSceneInput ? Frame with { Context = NativeUiInputBackend.InputContext(SceneIdentity.Split(':', 2)[1]) } : Frame;
    public InputUpdateEdgeCollector? Edges;
    private Func<IReadOnlyList<InputBindingOwner>> _bindingSource = () => Array.Empty<InputBindingOwner>();
    private Func<IReadOnlyList<InputFocusInfo>> _focusSource = () => Array.Empty<InputFocusInfo>();
    public void SetInputBindingSource(Func<IReadOnlyList<InputBindingOwner>> source) => _bindingSource = source;
    public void SetInputFocusSource(Func<IReadOnlyList<InputFocusInfo>> source) => _focusSource = source;
    public void InvalidateInputAction(string id) => Edges?.InvalidateAction(id);
    public bool IsCurrentInputAction(InputActionEdge action) => Edges?.IsCurrent(action) ?? true;
    public void ClearInput() => Edges?.Reset();
    public void QueueInput(long invocation, params string[] keys)
        => QueueBindings(invocation, keys.Select(key => new InputBinding(InputDeviceKind.Keyboard, key)).ToArray());
    public void QueueBindings(long invocation, params InputBinding[] bindings)
        => Edges!.Add(invocation, 16, _bindingSource(), bindings, null, false, false, (uint)invocation,
            new(CurrentFrame.Context, Frame.ExternalTextEditing, ApplicationFocused, _focusSource()));
    public void LoseGamepad() { GamepadReady = false; NativeUiInputBackend.InvalidateGamepadEdges(Edges!, _bindingSource()); }
    public IReadOnlyList<UiMountInfo> GetMounts()
    {
        var scene = SceneIdentity.Split(':', 2)[1];
        var menu = MenuReady && scene is ("MainMenu" or "FakeMainMenu");
        return new[] { new UiMountInfo(UiMountIds.Overlay, true, scene, null, 1, "owned"),
            new UiMountInfo(UiMountIds.MainMenu, menu, menu ? scene : null, menu ? null : "menu absent", 2, "borrowed") };
    }
    public IReadOnlyList<InputDeviceInfo> GetDevices() => new[] { new InputDeviceInfo(InputDeviceKind.Keyboard, KeyboardReady ? InputSupport.RuntimeAvailable : InputSupport.Unsupported, "test", "keyboard unavailable"), new InputDeviceInfo(InputDeviceKind.Mouse, InputSupport.RuntimeAvailable, "test", null), new InputDeviceInfo(InputDeviceKind.Gamepad, GamepadReady ? InputSupport.RuntimeAvailable : InputSupport.Unsupported, "synthetic digital gamepad", "fixture unavailable") };
    public IUiNativeWindow Create(string qualifiedId, UiWindowDefinition definition)
    { if (FailCreate) throw new InvalidOperationException("creation fault"); Created++; var window = new NativeWindow(definition); Windows.Add(window); return window; }
    public UiInputFrame Capture(IReadOnlyList<InputBindingOwner> bindings)
    {
        Captures++; var frame = CurrentFrame; Frame = new(Array.Empty<InputBinding>(), null, false, false, false, InputActionContext.Menu);
        return Edges is null ? frame : frame with { Updates = Edges.Drain(), Pressed = Array.Empty<InputBinding>(), LeftDown = false, LeftUp = false };
    }
    public IReadOnlyList<GameNavigationTarget> GetNavigationTargets() => new[] { new GameNavigationTarget(NativeUiInputBackend.ActivateSelected, NavigationReady, NavigationReady ? null : "not ready", "GameManager.mainMenuControl.EnterCurrentButton", new[] { "menu ready" }, false) };
    public Result Navigate(string id) => id != NativeUiInputBackend.ActivateSelected ? Result.Fail(new(ErrorCode.OperationUnsupported, "unknown")) : !NavigationReady ? Result.Fail(new(ErrorCode.NavigationUnavailable, "not ready")) : Navigate();
    private Result Navigate() { NavigationCalls++; return Result.Ok(); }
    public Result SubmitOfficialPlay(OfficialChartSelectionSnapshot expected)
        => OfficialPlaySubmission?.Invoke(expected) ?? Result.Fail(new(ErrorCode.OperationUnsupported, "No observed chart submit is attached."));
    public Result<OfficialChartSelectionSnapshot> ReadOfficialChartSelection()
    {
        OfficialSelectionReads++;
        return OfficialSelection?.Invoke() ?? Result<OfficialChartSelectionSnapshot>.Fail(new(ErrorCode.NavigationUnavailable,
            "The official card has no selected chart.", UiInputCapabilityIds.OfficialChartSelection, Retryable: true));
    }
    public void Dispose() { Disposed = true; Edges?.Dispose(); }
}
sealed class NativeWindow : IUiNativeWindow
{
    public bool Expired, ThrowDispose, FailControlRead;
    private bool _disposed;
    private UiLayout _layout;
    public bool Alive => !_disposed && !Expired;
    public List<NativeControl> Controls { get; } = new();
    public string MountId { get; }
    public NativeWindow(UiWindowDefinition definition) => (_layout, MountId) = (definition.Layout, definition.MountId);
    public UiViewport Viewport => UiCoordinates.Fit(1920, 1080, null, new(0, 0, 1920, 1080), _layout);
    public void Layout(UiLayout layout) => _layout = layout;
    public bool HitTest(UiPoint point) => point.X is >= 0 and < 500 && point.Y is >= 0 and < 400;
    public IUiNativeControl Add(UiControlKind kind, UiControlDefinition definition, int characterLimit)
    { var control = new NativeControl(this, definition, kind == UiControlKind.TextField, FailControlRead); Controls.Add(control); return control; }
    public void Focus() { foreach (var control in Controls) control.Focused = false; }
    public void Dispose() { if (_disposed) return; _disposed = true; foreach (var control in Controls.ToArray()) control.Dispose(); if (ThrowDispose) throw new InvalidOperationException("cleanup fault"); }
}
sealed class NativeControl : IUiNativeControl
{
    private readonly NativeWindow _window; private string _text; private readonly bool _field, _failRead; private bool _disposed;
    public bool Focused;
    public bool UsesNativeActivation { get; set; }
    public int DrainCount { get; private set; }
    public int PendingActivations { get; private set; }
    public string Id { get; }
    public NativeControl(NativeWindow window, UiControlDefinition definition, bool field, bool failRead)
        => (_window, Id, _text, _field, _failRead) = (window, definition.Id, definition.Text, field, failRead);
    public bool Alive => !_disposed && _window.Alive;
    public bool IsFocused => Focused;
    public string Text { get => _failRead ? throw new InvalidOperationException("read fault") : _text; set => _text = value; }
    public bool HitTest(UiPoint point) => point.X is >= 0 and < 180 && point.Y is >= 0 and < 40;
    public void Focus() { if (!_field) throw new NotSupportedException(); Focused = true; }
    public void QueueActivations(int count = 1)
    { if (count < 1) throw new ArgumentOutOfRangeException(nameof(count)); if (!_disposed) PendingActivations = checked(PendingActivations + count); }
    public int DrainActivations() { DrainCount++; var count = PendingActivations; PendingActivations = 0; return count; }
    public void Dispose() { if (_disposed) return; _disposed = true; PendingActivations = 0; _window.Controls.Remove(this); }
}
