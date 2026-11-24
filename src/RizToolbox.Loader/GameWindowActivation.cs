using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RizToolbox.Sdk;

namespace RizToolbox.Loader;

// Keep the Unity player active during a running chart. Windows retains ownership
// of keyboard focus and delivers ordinary input to the actual foreground window.
internal sealed class GameWindowActivation : IDisposable
{
    private const uint Activate = 0x0006, ActivateApp = 0x001C, DestroyWindow = 0x0082;
    private const uint RawInput = 0x00FF;
    private readonly IntPtr _window;
    private readonly Callback _callback;
    private GCHandle _root;
    private bool _inLevel, _removed;
    private readonly List<(uint Message, UIntPtr W, IntPtr L)> _deferred = new(2);

    private GameWindowActivation(IntPtr window) { _window = window; _callback = OnMessage; }
    internal static GameWindowActivation Attach()
    {
        using var process = Process.GetCurrentProcess();
        return Attach(process.MainWindowHandle);
    }

    internal static GameWindowActivation Attach(IntPtr window)
    {
        var name = new StringBuilder(64);
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out uint pid) != GetCurrentThreadId()
            || pid != (uint)Environment.ProcessId || GetClassNameW(window, name, name.Capacity) == 0 || name.ToString() != "UnityWndClass")
            throw new InvalidOperationException("The Unity player window is unavailable on the host thread.");
        var owner = new GameWindowActivation(window);
        owner._root = GCHandle.Alloc(owner);
        if (!SetWindowSubclass(window, owner._callback, 1, UIntPtr.Zero))
        {
            owner._root.Free();
            throw new InvalidOperationException("Cannot observe activation of the Unity player window.");
        }
        return owner;
    }

    internal void SetScene(string? scene)
    {
        // Both chart scenes poll focus in Update. Staying armed across a manual
        // pause also covers switching away immediately after pressing Resume.
        _inLevel = scene is "Level" or "Challenge";
        if (!_inLevel) ReleaseDeferred();
    }

    private IntPtr OnMessage(IntPtr hwnd, uint message, UIntPtr w, IntPtr l, UIntPtr id, UIntPtr data)
    {
        if (message == DestroyWindow)
        {
            _deferred.Clear();
            try { return DefSubclassProc(hwnd, message, w, l); }
            finally { Detach(destroyed: true); }
        }
        bool deactivating = (message == Activate && (w.ToUInt64() & 0xffff) == 0)
            || (message == ActivateApp && w == UIntPtr.Zero);
        if (_inLevel && deactivating)
        {
            for (int i = 0; i < _deferred.Count; i++)
                if (_deferred[i].Message == message) { _deferred.RemoveAt(i); break; }
            _deferred.Add((message, w, l));
            return DefWindowProcW(hwnd, message, w, l);
        }
        if ((message == Activate && (w.ToUInt64() & 0xffff) != 0) || (message == ActivateApp && w != UIntPtr.Zero))
            _deferred.Clear();
        // WM_KILLFOCUS and key releases reach Unity so held gameplay inputs reset.
        // Background raw-input packets must not become judgements while typing elsewhere.
        if (message == RawInput && w.ToUInt64() == 1 && _deferred.Count != 0)
            return DefWindowProcW(hwnd, message, w, l);
        return DefSubclassProc(hwnd, message, w, l);
    }

    private void ReleaseDeferred()
    {
        if (_removed || _deferred.Count == 0) return;
        var messages = _deferred.ToArray();
        _deferred.Clear();
        if (GetForegroundWindow() == _window) return;
        foreach (var (message, w, l) in messages)
        {
            if (_removed || GetForegroundWindow() == _window) break;
            SendMessageW(_window, message, w, l);
        }
    }

    private void Detach(bool destroyed = false)
    {
        if (_removed) return;
        if (!RemoveWindowSubclass(_window, _callback, 1) && !destroyed) return;
        _removed = true;
        _root.Free();
    }

    public void Dispose()
    {
        _inLevel = false;
        ReleaseDeferred();
        Detach();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr Callback(IntPtr hwnd, uint msg, UIntPtr w, IntPtr l, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hwnd, Callback callback, nuint id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hwnd, Callback callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, UIntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, UIntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, UIntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, ExactSpelling=true)] private static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int capacity);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
