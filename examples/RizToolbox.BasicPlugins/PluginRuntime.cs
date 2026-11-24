using System.Text;
using RizToolbox.Sdk;

namespace RizToolbox.BasicPlugins;

/// <summary>One activation, one one-second pulse, and a copied scene fence. No game discovery is performed here.</summary>
internal sealed class PluginRuntime : IDisposable
{
    private readonly Dictionary<string, OperationError> _issues = new(StringComparer.Ordinal);
    private readonly int _thread;
    private IDisposable? _frames, _scenes;
    private Action? _pulse, _sceneChanged, _cleanup;
    private double _nextPulse;
    private bool _disposed;
    private string? _sceneSession;

    internal PluginRuntime(IPluginContext context)
    {
        context.MainThread.VerifyAccess();
        Context = context as IScopedPluginContext
            ?? throw new NotSupportedException("Basic plugins require the SDK 1.2 scoped plugin context.");
        _thread = Environment.CurrentManagedThreadId;
        context.Own(this);
    }

    internal IScopedPluginContext Context { get; }
    internal double Now { get; private set; }
    internal string Scene { get; private set; } = "Unavailable";
    internal long SceneGeneration { get; private set; }
    internal bool HasScene { get; private set; }
    internal bool IsActive => !_disposed && !Context.Lifetime.IsCancellationRequested;

    internal void Start(Action pulse, Action sceneChanged, Action cleanup)
    {
        _pulse = pulse;
        _sceneChanged = sceneChanged;
        _cleanup = cleanup;
        _scenes = Context.Events.Subscribe<GameSceneChanged>(change =>
        {
            if (IsActive) ObserveScene(change.SessionId, change.SceneGeneration, change.Scene);
        });
        _frames = Context.Events.Subscribe<FrameUpdate>(frame =>
        {
            if (!IsActive || !double.IsFinite(frame.ElapsedSeconds) || frame.ElapsedSeconds < _nextPulse) return;
            Now = frame.ElapsedSeconds;
            _nextPulse = Now + 1;
            Run("scene-read-dispatch", ReadScene);
            Run("plugin-update", pulse);
        });
    }

    private void ReadScene()
    {
        var service = Service<IGameStateService>(Context, GameCapabilityIds.Scenes, "scene-observation");
        if (service is null) { LoseScene(); return; }
        var result = service.GetSnapshot();
        if (!result.Success || result.Value is null)
        {
            Fail("scene-observation", result.Error, GameCapabilityIds.Scenes);
            LoseScene();
            return;
        }
        Clear("scene-observation");
        ObserveScene(result.Value.SessionId, result.Value.SceneGeneration, result.Value.Scene);
    }

    private void ObserveScene(string? session, long generation, string scene)
    {
        if (session == _sceneSession && generation < SceneGeneration) return;
        if (string.IsNullOrWhiteSpace(scene)) { LoseScene(); return; }
        bool changed = !HasScene || session != _sceneSession || generation != SceneGeneration || scene != Scene;
        HasScene = true;
        _sceneSession = session;
        SceneGeneration = generation;
        Scene = scene;
        if (changed) Run("scene-boundary", () => _sceneChanged?.Invoke());
    }

    private void LoseScene()
    {
        if (!HasScene) return;
        HasScene = false;
        Scene = "Unavailable";
        Run("scene-boundary", () => _sceneChanged?.Invoke());
    }

    internal T? Service<T>(IPluginContext context, string capability, string operation) where T : class
    {
        var lookupOperation = operation + "-lookup";
        try
        {
            var lookup = context.GetService<T>(capability);
            if (!lookup.Available || lookup.Service is null)
            {
                Fail(lookupOperation, lookup.Error, capability);
                return null;
            }
            Clear(lookupOperation);
            return lookup.Service;
        }
        catch (Exception error)
        {
            Fail(lookupOperation, Error(error, capability));
            return null;
        }
    }

    internal bool Load<T>(string name, Func<T> defaults, Func<T, string?> validate, out T? value) where T : class
    {
        value = null;
        try
        {
            var loaded = Context.Configuration.Load(name, defaults);
            if (!loaded.Success || loaded.Value is null) { Fail("configuration", loaded.Error, target: name); return false; }
            var invalid = validate(loaded.Value);
            if (invalid is not null)
            {
                Fail("configuration", new(ErrorCode.ConfigInvalid, invalid, TargetId: name));
                return false;
            }
            value = loaded.Value;
            Clear("configuration");
            return true;
        }
        catch (Exception error) { Fail("configuration", Error(error, target: name)); return false; }
    }

    internal bool Save<T>(string name, T value, Func<T, string?> validate) where T : class
    {
        var invalid = validate(value);
        if (invalid is not null) { Fail("configuration", new(ErrorCode.ConfigInvalid, invalid, TargetId: name)); return false; }
        try
        {
            var saved = Context.Configuration.Save(name, value);
            if (!saved.Success) { Fail("configuration", saved.Error, target: name); return false; }
            Clear("configuration");
            return true;
        }
        catch (Exception error) { Fail("configuration", Error(error, target: name)); return false; }
    }

    internal void Fail(string operation, OperationError? error, string? capability = null, string? target = null)
    {
        error ??= new(ErrorCode.ServiceUnavailable, "The service did not return a usable value.", capability, target, true);
        error = error with { CapabilityId = error.CapabilityId ?? capability, TargetId = error.TargetId ?? target };
        if (_issues.TryGetValue(operation, out var previous) && previous == error) return;
        _issues[operation] = error;
        Context.Log.Log(LogLevel.Warning, $"{operation} [{error.Code}]: {Clip(error.Message, 500)}; " +
            $"capability={error.CapabilityId ?? "N/A"}; target={Clip(error.TargetId, 256)}; retryable={error.Retryable}.");
    }

    internal void Clear(string operation) => _issues.Remove(operation);

    internal string IssuesText()
    {
        var text = new StringBuilder();
        foreach (var issue in _issues.Take(3))
            text.Append('\n').Append(issue.Key).Append(" [").Append(issue.Value.Code).Append("]: ")
                .Append(Clip(issue.Value.Message, 160));
        if (_issues.Count > 3) text.Append("\nFurther operation details are in the plugin log.");
        return text.ToString();
    }

    internal void Run(string operation, Action action)
    {
        try { action(); Clear(operation); }
        catch (BasicOperationException error) { Fail(operation, error.Failure); }
        catch (Exception error) { Fail(operation, Error(error)); }
    }

    internal bool Release(IDisposable? resource, string operation)
    {
        if (resource is null) return true;
        try { resource.Dispose(); Clear(operation); return true; }
        catch (Exception error) { Fail(operation, Error(error)); return false; }
    }

    internal static OperationError Error(Exception error, string? capability = null, string? target = null)
        => error is BasicOperationException structured ? structured.Failure : new(
            error is ObjectDisposedException ? ErrorCode.ScopeExpired :
            error is OperationCanceledException ? ErrorCode.Cancelled :
            error is ArgumentException ? ErrorCode.InvalidArgument : ErrorCode.InternalError,
            string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message, capability, target);

    internal static string Clip(string? text, int maximum)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var clipped = text.Length > maximum ? text[..(maximum - 1)] + "…" : text;
        return clipped.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }

    internal static T Need<T>(Result<T> result, string capability, string? target = null) where T : class
        => result.Success && result.Value is not null ? result.Value :
            throw new BasicOperationException(result.Error ??
                new(ErrorCode.ServiceUnavailable, "The operation returned no usable resource.", capability, target));

    public void Dispose()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Plugin cleanup requires its main thread.");
        _disposed = true;
        Release(_frames, "frame-subscription-release"); _frames = null;
        Release(_scenes, "scene-subscription-release"); _scenes = null;
        Run("plugin-cleanup", () => _cleanup?.Invoke());
        _pulse = _sceneChanged = _cleanup = null;
    }
}

internal sealed class BasicOperationException : Exception
{
    internal BasicOperationException(OperationError failure) : base(failure.Message) => Failure = failure;
    internal OperationError Failure { get; }
}

/// <summary>The service owns every control; the child scope owns its service view and window.</summary>
internal sealed class ScopedWindow : IDisposable
{
    private ScopedWindow(IPluginScope scope, IUiWindowHandle handle) => (Scope, Handle) = (scope, handle);
    internal IPluginScope Scope { get; }
    internal IUiWindowHandle Handle { get; }
    internal bool IsAlive => Scope.IsActive && Handle.IsAlive;

    internal static ScopedWindow? Create(PluginRuntime runtime, string id, string title, UiRect rectangle)
    {
        IPluginScope? scope = null;
        try
        {
            var available = runtime.Context.GetCapability(UiInputCapabilityIds.Windows);
            if (!available.Available)
            {
                runtime.Fail("window", new(ErrorCode.CapabilityUnavailable,
                    available.UnavailableReason ?? "Owned UI is currently unavailable.", UiInputCapabilityIds.Windows, id, true));
                return null;
            }
            scope = runtime.Context.CreateScope(id, ScopeLifetime.Scene);
            var ui = runtime.Service<IOwnedUiService>(scope, UiInputCapabilityIds.Windows, "window");
            if (ui is null) { runtime.Release(scope, "window-release"); return null; }
            var handle = PluginRuntime.Need(ui.CreateWindow(new(id, UiMountIds.Overlay, title,
                new(rectangle), Theme: new(FontSize: 18))), UiInputCapabilityIds.Windows, id);
            runtime.Clear("window");
            return new(scope, handle);
        }
        catch (Exception error)
        {
            runtime.Fail("window", PluginRuntime.Error(error, UiInputCapabilityIds.Windows, id));
            runtime.Release(scope, "window-release");
            return null;
        }
    }

    public void Dispose()
    {
        try { Handle.Dispose(); }
        finally { Scope.Dispose(); }
    }
}
