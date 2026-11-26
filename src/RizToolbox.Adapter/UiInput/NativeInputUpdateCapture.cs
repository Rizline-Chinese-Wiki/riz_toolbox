using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.UiInput;

/// <summary>Framework-only observation after the game's own input processing; never triggers an input update.</summary>
internal sealed class NativeInputUpdateCapture : IDisposable
{
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private readonly IMainThreadDispatcher _main;
    private readonly ExactHookBroker.Binding _binding;
    private bool _disposed;
    internal NativeInputUpdateCapture(NativeRuntime api, IMainThreadDispatcher main, ILogger log, Action<long, int, uint> capture, Action<Exception> failed)
    {
        _main = main; main.VerifyAccess();
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new UnsupportedSignatureException("Input update capture requires verified Windows x64.");
        var root = Path.GetDirectoryName(Environment.ProcessPath!)!;
        if (FileHash.ComputeSha256(Path.Combine(root, "GameAssembly.dll")) != Fingerprint)
            throw new UnsupportedSignatureException("Input update capture build differs from the pinned profile.");
        // This player strips Application.get_unityVersion. The exact serialized engine
        // resource is already pinned to Unity 2022.3.62f2; do not require a missing icall.
        if (FileHash.ComputeSha256(Path.Combine(root, "Rizline_Data", "globalgamemanagers")) != "4b43cc1090bd3655287390cfb13d244038f53ced4479cedf92bbcc1f8fbdbeac")
            throw new UnsupportedSignatureException("Input update capture engine resources differ from pinned Unity 2022.3.62f2.");
        var manager = api.Class("Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputManager");
        var target = api.ResolveExactHook("framework.input.after-update", GetModuleHandle("GameAssembly.dll"), manager,
            "InvokeAfterUpdateCallback", new[] { "UnityEngine.InputSystem.LowLevel.InputUpdateType" }, 0x12FED70, ExactHookAbi.InstanceEnum32Void);
        var input = api.Class("Unity.InputSystem.dll", "UnityEngine.InputSystem", "InputSystem");
        if (api.DescribeFieldType(input, "s_Manager") != "UnityEngine.InputSystem.InputManager")
            throw new UnsupportedSignatureException("InputSystem manager identity field differs.");
        // Use the retained exact predicate instead of guessing enum numeric constants from dump field offsets.
        var update = api.Class("Unity.InputSystem.dll", "UnityEngine.InputSystem.LowLevel", "InputUpdate");
        var playerUpdate = api.Method(update, "IsPlayerUpdate", 1, true, new[] { "UnityEngine.InputSystem.LowLevel.InputUpdateType" });
        if (api.DescribeReturnType(playerUpdate) != "System.Boolean") throw new UnsupportedSignatureException("Input player-update predicate differs.");
        var state = api.Class("Unity.InputSystem.dll", "UnityEngine.InputSystem.LowLevel", "InputState");
        var updateCount = api.Method(state, "get_updateCount", 0, true, Array.Empty<string>());
        if (api.DescribeReturnType(updateCount) != "System.UInt32") throw new UnsupportedSignatureException("Input update ordinal differs.");
        var broker = new ExactHookBroker(Path.Combine(root, "BepInEx", "core", "dobby.dll"), main, log);
        _binding = broker.Bind(target, receiver =>
            {
                if (_disposed || receiver == IntPtr.Zero) return false;
                try { return api.ExactHookReceiverMatches(receiver, manager) && api.ReadStatic<IntPtr>(input, "s_Manager") == receiver; }
                catch (Exception ex) { failed(ex); return false; }
            },
            (phase, invocation, type, _) =>
            {
                if (phase != HookPhase.After || _disposed) return;
                try
                {
                    if (api.Unbox<bool>(api.Invoke(playerUpdate, IntPtr.Zero, NativeRuntime.Arg.Value((UnityEngine.InputSystem.LowLevel.InputUpdateType)type))))
                        capture(invocation, type, api.Unbox<uint>(api.Invoke(updateCount, IntPtr.Zero)));
                }
                catch (Exception ex) { failed(ex); }
            });
        log.Log(LogLevel.Info, "Raw input capture bound after original InputManager.InvokeAfterUpdateCallback@12FED70; player updates only, bounded copies, SDK dispatch remains on Host FrameUpdate.");
    }
    public void Dispose() { _main.VerifyAccess(); if (_disposed) return; _disposed = true; _binding.Dispose(); }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
