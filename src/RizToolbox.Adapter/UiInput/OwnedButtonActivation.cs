using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.UiInput;

/// <summary>One pinned read-only Button.Press observation; only framework-created live buttons are routed.</summary>
internal sealed class OwnedButtonActivation : IDisposable
{
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly bool _developerDiagnostics;
    private readonly Dictionary<IntPtr, Action> _buttons = new();
    private readonly ExactHookBroker.Binding _binding;
    private bool _disposed;
    private int _observedActivations;
    internal OwnedButtonActivation(NativeRuntime api, IMainThreadDispatcher main, ILogger log, bool developerDiagnostics = false)
    {
        _main = main; _log = log; _developerDiagnostics = developerDiagnostics; main.VerifyAccess();
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new UnsupportedSignatureException("Owned button activation requires the verified Windows x64 build.");
        var gameRoot = Path.GetDirectoryName(Environment.ProcessPath!)!;
        if (FileHash.ComputeSha256(Path.Combine(gameRoot, "GameAssembly.dll")) != Fingerprint)
            throw new UnsupportedSignatureException("Owned button activation build differs from the pinned profile.");
        var button = api.Class("UnityEngine.UI.dll", "UnityEngine.UI", "Button");
        var target = api.ResolveExactHook("framework.owned-ui.button.press", GetModuleHandle("GameAssembly.dll"), button,
            "Press", Array.Empty<string>(), 0x15D6E50, ExactHookAbi.InstanceVoid);
        var broker = new ExactHookBroker(Path.Combine(gameRoot, "BepInEx", "core", "dobby.dll"), main, log);
        // Capture just this entry's receiver until its after observation. The
        // native original runs exactly once inside the shared process broker.
        IntPtr receiver = IntPtr.Zero;
        _binding = broker.Bind(target, instance =>
        {
            if (_disposed || !_buttons.ContainsKey(instance) || !api.ExactHookReceiverMatches(instance, target.DeclaringClass)) return false;
            receiver = instance; return true;
        }, (phase, _, _, _) =>
        {
            if (phase == HookPhase.After && !_disposed && _buttons.TryGetValue(receiver, out var activated))
            {
                activated();
                if (_developerDiagnostics && _observedActivations++ < 16 && _binding is { } binding)
                {
                    var counts = binding.Describe();
                    try { _log.Log(LogLevel.Debug, $"Owned button activation observed after original: native={counts.NativeCalls}; original={counts.OriginalCalls}; reentries={counts.SuppressedReentries}; wrongThread={counts.SuppressedWrongThread}."); } catch { }
                }
            }
        });
        log.Log(LogLevel.Info, "Owned button activation bound: UnityEngine.UI.Button.Press@15D6E50; routing only framework-owned receivers.");
    }
    internal IDisposable Register(IntPtr button, Action activated)
    {
        _main.VerifyAccess();
        if (_disposed) throw new ObjectDisposedException(nameof(OwnedButtonActivation));
        if (button == IntPtr.Zero || activated is null) throw new ArgumentException("A rooted owned button and activation callback are required.");
        _buttons.Add(button, activated);
        return new Registration(this, button);
    }
    private sealed class Registration : IDisposable
    {
        private OwnedButtonActivation? _owner; private readonly IntPtr _button;
        internal Registration(OwnedButtonActivation owner, IntPtr button) => (_owner, _button) = (owner, button);
        public void Dispose() { var owner = _owner; if (owner is null) return; owner._main.VerifyAccess(); _owner = null; owner._buttons.Remove(_button); }
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return; _disposed = true; _buttons.Clear(); _binding.Dispose();
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
