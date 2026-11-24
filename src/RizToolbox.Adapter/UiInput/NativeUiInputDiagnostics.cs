using System.Globalization;
using System.Text.Json;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.UiInput;

/// <summary>One bounded, read-only comparison of the UI module's actual action devices and current raw devices.
/// No reflection/native layout guesses, input updates, enable/disable operations, or native addresses are logged.</summary>
internal static class NativeUiInputDiagnostics
{
    private const string InputModuleName = "UnityEngine.InputSystem.UI.InputSystemUIInputModule";
    private static readonly string[] NoParameters = Array.Empty<string>();

    /// <summary>Call only from the already verified Unity thread. Unsupported getters remain independent observations.
    /// Even a diagnostic/runtime/root cleanup failure is contained; this helper cannot change an input operation's result.</summary>
    internal static string Capture(NativeRuntime api)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var roots = new List<NativeRuntime.RootHandle>();
        try
        {
            if (api is null) return "{\"diagnostic\":\"unsupported[null-runtime]\"}";
            CaptureUiModule(api, values, roots);
            CaptureCurrentDevice(api, "Keyboard", values, roots);
            CaptureCurrentDevice(api, "Mouse", values, roots);
        }
        catch (Exception ex) { values["diagnostic"] = Unsupported(ex); }
        finally
        {
            for (var index = roots.Count - 1; index >= 0; index--)
            {
                try { roots[index].Dispose(); }
                catch (Exception ex) { values["root-cleanup"] = Unsupported(ex); }
            }
        }
        try { return JsonSerializer.Serialize(values); }
        catch { return "{\"diagnostic\":\"unsupported[serialization]\"}"; }
    }

    private static void CaptureUiModule(NativeRuntime api, IDictionary<string, string> values, List<NativeRuntime.RootHandle> roots)
    {
        var events = Reference(api, values, roots, "eventSystem.current", () => Static(api,
            "UnityEngine.UI.dll", "UnityEngine.EventSystems", "EventSystem", "get_current", "UnityEngine.EventSystems.EventSystem"));
        if (events is null)
        {
            values["eventSystem.currentInputModule.type"] = "unavailable[event-system]";
            return;
        }
        Scalar(api, values, "eventSystem.isFocused", () => Instance(api, events, "UnityEngine.UI.dll", "UnityEngine.EventSystems", "EventSystem", "get_isFocused", "System.Boolean"), ReadBool);
        var module = Reference(api, values, roots, "eventSystem.currentInputModule", () => Instance(api, events,
            "UnityEngine.UI.dll", "UnityEngine.EventSystems", "EventSystem", "get_currentInputModule", "UnityEngine.EventSystems.BaseInputModule"));
        if (module is null)
        {
            values["eventSystem.currentInputModule.type"] = "unavailable[input-module]";
            return;
        }
        string? moduleName = null;
        try { moduleName = FullName(api, module, roots); values["eventSystem.currentInputModule.type"] = moduleName; }
        catch (Exception ex) { values["eventSystem.currentInputModule.type"] = Unsupported(ex); }
        if (moduleName != InputModuleName)
        {
            values["ui.point"] = values["ui.leftClick"] = moduleName is null
                ? "unsupported[module-type-unavailable]" : "not-applicable[actual-module-is-not-InputSystemUIInputModule]";
            return;
        }
        CaptureAction(api, module, "point", values, roots);
        CaptureAction(api, module, "leftClick", values, roots);
    }

    private static void CaptureAction(NativeRuntime api, NativeRuntime.RootHandle module, string name,
        IDictionary<string, string> values, List<NativeRuntime.RootHandle> roots)
    {
        var prefix = "ui." + name;
        var reference = Reference(api, values, roots, prefix + ".reference", () => Instance(api, module,
            "Unity.InputSystem.dll", "UnityEngine.InputSystem.UI", "InputSystemUIInputModule", "get_" + name,
            "UnityEngine.InputSystem.InputActionReference"));
        if (reference is null) { values[prefix + ".action"] = "unavailable[action-reference]"; return; }
        var action = Reference(api, values, roots, prefix + ".action", () => Instance(api, reference,
            "Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputActionReference", "get_action", "UnityEngine.InputSystem.InputAction"));
        if (action is null) return;
        Scalar(api, values, prefix + ".action.enabled", () => Instance(api, action,
            "Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputAction", "get_enabled", "System.Boolean"), ReadBool);
        var control = Reference(api, values, roots, prefix + ".action.activeControl", () => Instance(api, action,
            "Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputAction", "get_activeControl", "UnityEngine.InputSystem.InputControl"));
        if (control is null) { values[prefix + ".action.activeControl.deviceId"] = "unavailable[no-active-control]"; return; }
        var device = Reference(api, values, roots, prefix + ".action.activeControl.device", () => Instance(api, control,
            "Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputControl", "get_device", "UnityEngine.InputSystem.InputDevice"));
        if (device is null) { values[prefix + ".action.activeControl.deviceId"] = "unavailable[no-device]"; return; }
        Scalar(api, values, prefix + ".action.activeControl.deviceId", () => DeviceGetter(api, device, "get_deviceId", "System.Int32"), ReadInt);
        CaptureDeviceProperties(api, device, prefix + ".device", values);
    }

    private static void CaptureCurrentDevice(NativeRuntime api, string name,
        IDictionary<string, string> values, List<NativeRuntime.RootHandle> roots)
    {
        var prefix = name + ".current";
        var device = Reference(api, values, roots, prefix, () => Static(api,
            "Unity.InputSystem.dll", "UnityEngine.InputSystem", name, "get_current", "UnityEngine.InputSystem." + name));
        if (device is not null) CaptureDeviceProperties(api, device, prefix, values);
    }

    private static void CaptureDeviceProperties(NativeRuntime api, NativeRuntime.RootHandle device, string prefix, IDictionary<string, string> values)
    {
        // dump.cs 47601..47609; scalar/reference getters are checked independently against live metadata.
        Scalar(api, values, prefix + ".deviceId", () => DeviceGetter(api, device, "get_deviceId", "System.Int32"), ReadInt);
        Scalar(api, values, prefix + ".added", () => DeviceGetter(api, device, "get_added", "System.Boolean"), ReadBool);
        Scalar(api, values, prefix + ".enabled", () => DeviceGetter(api, device, "get_enabled", "System.Boolean"), ReadBool);
        Scalar(api, values, prefix + ".native", () => DeviceGetter(api, device, "get_native", "System.Boolean"), ReadBool);
        Scalar(api, values, prefix + ".lastUpdateTime", () => DeviceGetter(api, device, "get_lastUpdateTime", "System.Double"), ReadDouble);
    }

    private static IntPtr DeviceGetter(NativeRuntime api, NativeRuntime.RootHandle device, string getter, string expected)
        => Instance(api, device, "Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputDevice", getter, expected);

    private static IntPtr Static(NativeRuntime api, string assembly, string ns, string type, string method, string expected)
    {
        var pointer = api.Method(api.Class(assembly, ns, type), method, 0, true, NoParameters);
        RequireReturn(api, pointer, method, expected);
        return api.Invoke(pointer, IntPtr.Zero);
    }

    private static IntPtr Instance(NativeRuntime api, NativeRuntime.RootHandle receiver,
        string assembly, string ns, string type, string method, string expected)
    {
        var pointer = api.Method(api.Class(assembly, ns, type), method, 0, false, NoParameters);
        RequireReturn(api, pointer, method, expected);
        return api.Invoke(pointer, receiver.Target);
    }

    private static void RequireReturn(NativeRuntime api, IntPtr method, string name, string expected)
    {
        if (api.DescribeReturnType(method) != expected)
            throw new UnsupportedSignatureException("Diagnostic getter return differs: " + name + " expected " + expected);
    }

    private static NativeRuntime.RootHandle? Reference(NativeRuntime api, IDictionary<string, string> values,
        List<NativeRuntime.RootHandle> roots, string label, Func<IntPtr> read)
    {
        try
        {
            var pointer = read();
            if (pointer == IntPtr.Zero) { values[label] = "null"; return null; }
            var root = api.Root(pointer); roots.Add(root); values[label] = "present"; return root;
        }
        catch (Exception ex) { values[label] = Unsupported(ex); return null; }
    }

    private static string FullName(NativeRuntime api, NativeRuntime.RootHandle receiver, List<NativeRuntime.RootHandle> roots)
    {
        // NativeRuntime intentionally exposes no guessed ClassName/object header API. Object.GetType is retained
        // (dump.cs 6915), and runtime dispatch of the actual Type object's get_FullName selects its concrete override.
        var pointer = Instance(api, receiver, "mscorlib.dll", "System", "Object", "GetType", "System.Type");
        if (pointer == IntPtr.Zero) throw new TargetExpiredException("Diagnostic GetType returned null.");
        var type = api.Root(pointer); roots.Add(type);
        var fullName = api.Text(api.CallTyped(type.Target, "get_FullName", NoParameters));
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 256 || fullName.Any(char.IsControl))
            throw new UnsupportedSignatureException("Diagnostic runtime type name is invalid.");
        return fullName;
    }

    private static void Scalar(NativeRuntime api, IDictionary<string, string> values, string label, Func<IntPtr> read,
        Func<NativeRuntime, IntPtr, string> format)
    {
        try { values[label] = format(api, read()); }
        catch (Exception ex) { values[label] = Unsupported(ex); }
    }
    private static string ReadBool(NativeRuntime api, IntPtr value) => api.Unbox<bool>(value) ? "true" : "false";
    private static string ReadInt(NativeRuntime api, IntPtr value) => api.Unbox<int>(value).ToString(CultureInfo.InvariantCulture);
    private static string ReadDouble(NativeRuntime api, IntPtr value)
    {
        var number = api.Unbox<double>(value);
        return double.IsFinite(number) ? number.ToString("R", CultureInfo.InvariantCulture) : "unsupported[non-finite-value]";
    }
    // Do not concatenate native addresses/object ToString or exception messages into diagnostic output.
    private static string Unsupported(Exception exception) => "unsupported[" + exception.GetType().Name + "]";
}
