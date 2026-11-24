using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using RizToolbox.Core;
using RizToolbox.Loader;
using RizToolbox.Sdk;

// Offline protocol checks only. HWND_MESSAGE windows cannot be displayed or
// activated. No game, keyboard injection, SetFocus or foreground changes occur.
internal static class BackgroundPlayWindowChecks
{
    private const uint Activate = 0x0006, ActivateApp = 0x001c, SetFocus = 0x0007, KillFocus = 0x0008, KeyUp = 0x0101;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Type Activation = typeof(Bootstrap).Assembly.GetType("RizToolbox.Loader.GameWindowActivation", true)!;
    internal static readonly (string Name, Action Run)[] All = {
        ("deferred activation release preserves the complete native notification tuples", TupleReplay),
        ("Level and Challenge remain armed through manual pause and immediate resume", SceneLifecycle),
        ("keyboard focus transitions and key releases reach the original window procedure", FocusAndKeyUp),
        ("real activation discards stale deferred loss without replaying it later", Reactivation),
        ("plugin scope retirement releases the request subscription and retained window callback", ScopeCleanup),
        ("native window destruction removes and unroots the actual callback", DestroyCleanup),
        ("window attachment rejects an absent window, another class and another thread", AttachGuards)
    };

    internal static int Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Hidden-window protocol checks require Windows x64.");
        var failures = 0;
        foreach (var check in All)
        {
            try { check.Run(); Console.WriteLine("PASS " + check.Name); }
            catch (Exception error) { ++failures; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
        }
        Console.WriteLine($"{All.Length - failures}/{All.Length} background play window checks passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static object? Invoke(object? owner, Type type, string name, Type[] signature, params object?[] args)
    {
        var method = type.GetMethod(name, Private, null, signature, null) ?? throw new MissingMethodException(type.FullName, name);
        try { return method.Invoke(owner, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static object Attach(IntPtr window) => Invoke(null, Activation, "Attach", new[] { typeof(IntPtr) }, window)!;
    private static void Scene(object owner, string? scene) => Invoke(owner, Activation, "SetScene", new[] { typeof(string) }, scene);
    private static T Field<T>(object owner, string name) => (T)(owner.GetType().GetField(name, Private)?.GetValue(owner)
        ?? throw new MissingFieldException(owner.GetType().FullName, name));
    private static void SetField(object owner, string name, object? value) => (owner.GetType().GetField(name, Private)
        ?? throw new MissingFieldException(owner.GetType().FullName, name)).SetValue(owner, value);
    private static void Released(object owner) => Check(Field<bool>(owner, "_removed") && !Field<GCHandle>(owner, "_root").IsAllocated,
        "The actual native subclass and its managed root were not retired.");
    private static void Reject(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("Window ownership check unexpectedly accepted the request."); }

    private static void TupleReplay()
    {
        using var window = new HiddenWindow();
        var owner = Attach(window.Handle);
        try
        {
            Scene(owner, "Level");
            var inactive = new Message(Activate, new UIntPtr(0x10000), new IntPtr(0x11223344));
            var inactiveApp = new Message(ActivateApp, UIntPtr.Zero, new IntPtr(0x5566));
            window.Send(inactive); window.Send(inactiveApp);
            Check(window.Messages.Count == 0, "Chart-scene activation loss escaped before release.");
            Scene(owner, null);
            Check(window.Messages.SequenceEqual(new[] { inactive, inactiveApp }),
                "Release changed a minimized flag, next-window handle, thread identifier or notification order.");
            Scene(owner, "MainMenu");
            Check(window.Messages.Count == 2, "Repeated release replayed a native notification twice.");
        }
        finally { ((IDisposable)owner).Dispose(); }
        Released(owner);
    }

    private static void SceneLifecycle()
    {
        using var window = new HiddenWindow();
        var owner = Attach(window.Handle);
        try
        {
            foreach (var scene in new[] { "Level", "Challenge" })
            {
                window.Messages.Clear();
                foreach (var phase in new[] { RunLifecyclePhase.Loading, RunLifecyclePhase.Playing, RunLifecyclePhase.Paused, RunLifecyclePhase.Playing })
                {
                    var snapshot = new GameLifecycleSnapshot("offline", 1, 1, scene, "run", "chart", phase,
                        phase == RunLifecyclePhase.Paused, true, true, true, Array.Empty<OperationError>());
                    // Same snapshot field consumed by the real lifecycle subscriber.
                    Scene(owner, snapshot.Scene);
                    window.Send(new(ActivateApp, UIntPtr.Zero, new IntPtr((int)phase + 100)));
                    Check(window.Messages.Count == 0, "Manual pause/resume temporarily disarmed background activation in a chart scene.");
                }
                Scene(owner, "MainMenu");
                Check(window.Messages.SequenceEqual(new[] { new Message(ActivateApp, UIntPtr.Zero, new IntPtr((int)RunLifecyclePhase.Playing + 100)) }),
                    "Scene exit did not release exactly the latest retained app-deactivation tuple.");
            }
        }
        finally { ((IDisposable)owner).Dispose(); }
        Released(owner);
    }

    private static void FocusAndKeyUp()
    {
        using var window = new HiddenWindow();
        var owner = Attach(window.Handle);
        try
        {
            Scene(owner, "Level");
            var focusLost = new Message(KillFocus, new UIntPtr(0x1234), IntPtr.Zero);
            var keyReleased = new Message(KeyUp, new UIntPtr(0x41), new IntPtr(unchecked((long)0xc01e0001)));
            var focusGained = new Message(SetFocus, new UIntPtr(0x5678), IntPtr.Zero);
            window.Send(focusLost); window.Send(keyReleased); window.Send(focusGained);
            Check(window.Messages.SequenceEqual(new[] { focusLost, keyReleased, focusGained }),
                "Input focus or key-up was swallowed, changed or delayed by the activation policy.");
        }
        finally { ((IDisposable)owner).Dispose(); }
        Released(owner);
    }

    private static void Reactivation()
    {
        using var window = new HiddenWindow();
        var owner = Attach(window.Handle);
        try
        {
            Scene(owner, "Challenge");
            window.Send(new(ActivateApp, UIntPtr.Zero, new IntPtr(0x1234)));
            var active = new Message(ActivateApp, new UIntPtr(1), new IntPtr(0x4321));
            window.Send(active);
            Scene(owner, null);
            Check(window.Messages.SequenceEqual(new[] { active }), "An already-reactivated window received a stale loss notification on scene exit.");
        }
        finally { ((IDisposable)owner).Dispose(); }
        Released(owner);
    }

    private static void ScopeCleanup()
    {
        using var window = new HiddenWindow();
        var activation = Attach(window.Handle);
        Scene(activation, "Level");
        var inactive = new Message(ActivateApp, UIntPtr.Zero, new IntPtr(0x7777)); window.Send(inactive);
        var serviceType = Activation.Assembly.GetType("RizToolbox.Loader.BackgroundPlayService", true)!;
        var service = Activator.CreateInstance(serviceType, true)!;
        var subscription = new Subscription();
        var logger = new Logger();
        using var main = new MainThreadDispatcher();
        var capabilities = new CapabilityRegistry(new("offline", "offline", "win-x64", new string('0', 64), false), logger);
        var events = new EventHub(logger);
        IScopedPluginContext? captured = null;
        // Inject only an already Attach-verified invisible HWND into the real
        // request, avoiding discovery of a real main game window. Cleanup and
        // PluginScope/FactoryOwner paths are the production implementations.
        capabilities.RegisterScoped<IBackgroundPlayService>(BackgroundPlayCapability.Id, context =>
        {
            captured = (IScopedPluginContext)context;
            var request = Invoke(service, serviceType, "ForPlugin", new[] { typeof(IPluginContext) }, context)!;
            SetField(request, "_window", activation); SetField(request, "_subscription", subscription); SetField(service, "_active", request);
            context.Own((IDisposable)request);
            return (IBackgroundPlayService)request;
        });
        using var plugins = new PluginHost(capabilities, main, events, logger, dataRoot: Path.Combine(AppContext.BaseDirectory, "background-review-data"));
        var plugin = new ProbePlugin();
        try
        {
            Check(plugins.Register(plugin).Success && plugins.Enable(plugin.Manifest.Id).Success, "The offline plugin scope did not enable.");
            Check(plugins.Disable(plugin.Manifest.Id).Success && captured?.Lifetime.IsCancellationRequested == true,
                "Plugin disable did not expire its actual scope.");
            Check(subscription.Disposals == 1 && serviceType.GetField("_active", Private)!.GetValue(service) is null,
                "Scope cleanup retained the lifecycle subscription or exclusive background-play owner.");
            Released(activation);
            Check(window.Messages.SequenceEqual(new[] { inactive }), "Scope cleanup did not release the deferred native loss tuple.");
            window.Messages.Clear(); window.Send(inactive);
            Check(window.Messages.SequenceEqual(new[] { inactive }), "A retired callback still intercepted the window.");
        }
        finally { ((IDisposable)activation).Dispose(); }
    }

    private static void DestroyCleanup()
    {
        using var window = new HiddenWindow();
        var owner = Attach(window.Handle);
        Scene(owner, "Level");
        window.Send(new(ActivateApp, UIntPtr.Zero, new IntPtr(0x9876)));
        window.Destroy();
        Released(owner);
        ((IDisposable)owner).Dispose();
        Released(owner);
        Check(window.Messages.Count == 0, "A destroyed window replayed deferred activation state.");
    }

    private static void AttachGuards()
    {
        Reject(() => Attach(IntPtr.Zero));
        using (var otherClass = new HiddenWindow("RizToolbox.NotUnity." + Guid.NewGuid().ToString("N")))
            Reject(() => Attach(otherClass.Handle));
        using var window = new HiddenWindow();
        Task.Run(() => Reject(() => Attach(window.Handle))).GetAwaiter().GetResult();
        var owner = Attach(window.Handle);
        ((IDisposable)owner).Dispose(); Released(owner);
    }

    private sealed class ProbePlugin : IRizPlugin
    {
        public PluginManifest Manifest { get; } = new("offline.background-review", new(1, 0), SdkInfo.Version);
        public void Enable(IPluginContext context) => Check(context.GetService<IBackgroundPlayService>(BackgroundPlayCapability.Id).Service is not null,
            "The scoped background-play view was unavailable.");
        public void Disable() { }
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
    private sealed class Subscription : IGameLifecycleSubscription
    {
        public int Disposals;
        public GameLifecycleSnapshot InitialSnapshot => throw new InvalidOperationException("This cleanup-only fixture never samples a game.");
        public void Dispose() => ++Disposals;
    }

    private readonly record struct Message(uint Id, UIntPtr W, IntPtr L);
    private sealed class HiddenWindow : IDisposable
    {
        private readonly string _class;
        private readonly IntPtr _instance = GetModuleHandleW(null);
        private readonly WindowProcedure _procedure;
        private readonly ushort _atom;
        internal IntPtr Handle { get; private set; }
        internal List<Message> Messages { get; } = new();
        internal HiddenWindow(string name = "UnityWndClass")
        {
            _class = name; _procedure = Procedure;
            var definition = new WindowClass { Procedure = Marshal.GetFunctionPointerForDelegate(_procedure), Instance = _instance, ClassName = _class };
            _atom = RegisterClassW(ref definition);
            if (_atom == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Register invisible test window class");
            Handle = CreateWindowExW(0, _class, "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, _instance, IntPtr.Zero);
            if (Handle == IntPtr.Zero) { UnregisterClassW(_class, _instance); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Create HWND_MESSAGE"); }
        }
        private IntPtr Procedure(IntPtr window, uint message, UIntPtr w, IntPtr l)
        {
            if (message is Activate or ActivateApp or SetFocus or KillFocus or KeyUp)
            {
                Messages.Add(new(message, w, l));
                // Do not invoke activation defaults or create real input focus.
                return IntPtr.Zero;
            }
            return DefWindowProcW(window, message, w, l);
        }
        internal void Send(Message message) => SendMessageW(Handle, message.Id, message.W, message.L);
        internal void Destroy()
        {
            if (Handle == IntPtr.Zero) return;
            Check(DestroyWindow(Handle), "The invisible test window could not be destroyed."); Handle = IntPtr.Zero;
        }
        public void Dispose() { Destroy(); if (_atom != 0) UnregisterClassW(_class, _instance); GC.KeepAlive(_procedure); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    {
        public uint Style; public IntPtr Procedure; public int ClassBytes, WindowBytes; public IntPtr Instance, Icon, Cursor, Background, MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassW(ref WindowClass definition);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClassW(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint extended, string name, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr window, uint message, UIntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr window, uint message, UIntPtr w, IntPtr l);
}
