using System.Text;
using System.Text.Json;
using RizToolbox.Core;
using RizToolbox.Core.Input;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Storage;
using RizToolbox.Core.Targets;
using RizToolbox.Sdk;

namespace RizToolbox.BasicPlugins.Tests;

internal static class BasicIds
{
    internal const string Songs = "basic.song-scores";
    internal const string Theme = "basic.theme";
    internal const string Quick = "basic.quick-panel";
}

internal static class Check
{
    internal static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Equal<T>(T expected, T actual, string message)
        => True(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, actual {actual}.");

    internal static void Contains(string text, string expected, string message)
        => True(text.Contains(expected, StringComparison.Ordinal), $"{message}: missing '{expected}' in '{text}'.");

    internal static void Excludes(string text, string unexpected, string message)
        => True(!text.Contains(unexpected, StringComparison.Ordinal), $"{message}: unexpected '{unexpected}' in '{text}'.");

    internal static void Ok(OperationResult result, string message)
        => True(result.Success, $"{message}: {result.Error}");

    internal static void Ok(Result result, string message)
        => True(result.Success, $"{message}: {result.Error}");

    internal static T Value<T>(OperationResult<T> result, string message) where T : class
    {
        True(result.Success && result.Value is not null, $"{message}: {result.Error}");
        return result.Value!;
    }

    internal static T Value<T>(Result<T> result, string message) where T : class
    {
        True(result.Success && result.Value is not null, $"{message}: {result.Error}");
        return result.Value!;
    }

    internal static void Before(IReadOnlyList<string> events, string earlier, string later, string message)
    {
        var first = events.ToList().IndexOf(earlier);
        var second = events.ToList().IndexOf(later);
        True(first >= 0 && second > first, $"{message}: {string.Join(" -> ", events)}");
    }
}

internal sealed class CaptureLog : ILogger
{
    internal List<(LogLevel Level, string Message)> Entries { get; } = new();
    public void Log(LogLevel level, string message, Exception? exception = null)
        => Entries.Add((level, exception is null ? message : message + " " + exception.Message));
    internal bool Has(string text) => Entries.Any(entry => entry.Message.Contains(text, StringComparison.Ordinal));
}

/// <summary>Only SDK/Core public entry points are used. Each case gets a private temporary data root.</summary>
internal sealed class HostFixture : IDisposable
{
    private const string TemporaryPrefix = "RizToolbox.BasicPlugins.Tests-";
    private bool _disposed;
    internal HostFixture(bool registerProviders = true)
    {
        DataRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), TemporaryPrefix + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(DataRoot);
        Main = new MainThreadDispatcher();
        Log = new CaptureLog();
        Events = new EventHub(Log);
        Capabilities = new CapabilityRegistry(new("offline", "offline", "test", "basic-plugin-fixture"), Log);
        Host = new PluginHost(Capabilities, Main, Events, Log, DataRoot);
        Input = new InputActionBroker(Main, Log);
        Targets = new SemanticTargetRegistry(Main, Log);
        Mutations = new MutationCoordinator(Targets, Main, Log);
        Storage = new PluginStorageManager(DataRoot);
        Scenes = new ControlledScenes();
        Catalog = new ControlledCatalog();
        Scores = new ControlledScores();
        Ui = new ControlledUi(Input);
        Resources = new ControlledResources(Scenes);
        Audio = new ControlledAudio(Storage);
        if (registerProviders) RegisterAllProviders();
    }

    internal string DataRoot { get; }
    internal MainThreadDispatcher Main { get; }
    internal CaptureLog Log { get; }
    internal EventHub Events { get; }
    internal CapabilityRegistry Capabilities { get; }
    internal PluginHost Host { get; }
    internal InputActionBroker Input { get; }
    internal SemanticTargetRegistry Targets { get; }
    internal MutationCoordinator Mutations { get; }
    internal PluginStorageManager Storage { get; }
    internal ControlledScenes Scenes { get; }
    internal ControlledCatalog Catalog { get; }
    internal ControlledScores Scores { get; }
    internal ControlledUi Ui { get; }
    internal ControlledResources Resources { get; }
    internal ControlledAudio Audio { get; }
    internal double Elapsed { get; private set; } = -1;

    internal void RegisterScenes() => Capabilities.Register<IGameStateService>(GameCapabilityIds.Scenes, Scenes);
    internal void RegisterCatalog() => Capabilities.Register<ICatalogService>(CapabilityIds.Catalog, Catalog);
    internal void RegisterScores() => Capabilities.Register<IScoreService>(CapabilityIds.Scores, Scores);
    internal void RegisterWindows() => Capabilities.RegisterScoped<IOwnedUiService>(UiInputCapabilityIds.Windows, Ui.ForScope);
    internal void RegisterInput() => Capabilities.RegisterScoped<IInputActionService>(UiInputCapabilityIds.Actions,
        context => new ControlledInputView(Input, (IPluginScope)context));
    internal void RegisterResources() => Capabilities.RegisterScoped<IGameResourceService>(GameCapabilityIds.Resources, Resources.ForScope);
    internal void RegisterStorage() => Capabilities.RegisterScoped<IPluginStorageService>(StorageCapabilityIds.Storage, Storage.ForPlugin);
    internal void RegisterAudio() => Capabilities.RegisterScoped<IAudioPlaybackService>(AudioContentCapabilityIds.Playback, Audio.ForScope);

    private void RegisterAllProviders()
    {
        RegisterScenes();
        RegisterCatalog();
        RegisterScores();
        RegisterWindows();
        RegisterInput();
        RegisterResources();
        RegisterStorage();
        RegisterAudio();
    }

    internal void Register(IRizPlugin plugin) => Check.Ok(Host.Register(plugin), "Register actual plugin " + plugin.Manifest.Id);
    internal void Enable(string id) => Check.Ok(Host.Enable(id), "Enable " + id);
    internal void Disable(string id) => Check.Ok(Host.Disable(id), "Disable " + id);
    internal void Start(IRizPlugin plugin)
    {
        Register(plugin);
        Enable(plugin.Manifest.Id);
        Tick(Elapsed + 1);
        Tick(Elapsed + 1);
    }

    internal void Tick(double elapsed)
    {
        Check.True(double.IsFinite(elapsed) && elapsed >= Elapsed, "Fixture frames must be monotonic.");
        Elapsed = elapsed;
        Events.Publish(new FrameUpdate(elapsed));
    }

    internal void ChangeScene(string scene, long generation)
    {
        var previous = Scenes.Snapshot.Scene;
        Scenes.Snapshot = new(scene, null, null, false)
        { SessionId = "offline-session", SceneGeneration = generation, Revision = generation };
        Events.Publish(new GameSceneChanged(previous, scene)
        { SessionId = "offline-session", SceneGeneration = generation, Revision = generation });
    }

    internal void F8() => Input.Dispatch(new[] { new InputBinding(InputDeviceKind.Keyboard, "F8") }, InputActionContext.Menu);
    internal PluginStatus Status(string id) => Host.Statuses.Single(row => row.Id == id);
    internal PluginResourceCounts Counts(string id) => Host.GetResourceCounts().Single(row => row.PluginId == id);
    internal ControlledWindow Window(string owner, string localId) => Ui.Windows.Single(window =>
        window.Owner == owner && window.LocalId == localId && window.IsAlive);
    internal ControlledWindow? OpenWindow(string owner, string localId) => Ui.Windows.SingleOrDefault(window =>
        window.Owner == owner && window.LocalId == localId && window.IsAlive);
    internal ControlledWindow QuickWindow => Window(BasicIds.Quick, "quick-panel");
    internal ControlledWindow SongWindow => Window(BasicIds.Songs, "song-scores");

    internal string ConfigurationPath(string pluginId, string name)
        => Path.Combine(DataRoot, "config", "plugin-" + pluginId, "config-" + name + ".json");
    internal void SeedConfiguration<T>(string pluginId, string name, T settings)
        => SeedRawConfiguration(pluginId, name, JsonSerializer.Serialize(settings));
    internal void SeedRawConfiguration(string pluginId, string name, string json)
    {
        var path = ConfigurationPath(pluginId, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }
    internal T ReadConfiguration<T>(string pluginId, string name) where T : class
        => JsonSerializer.Deserialize<T>(File.ReadAllText(ConfigurationPath(pluginId, name)))
            ?? throw new InvalidOperationException("Saved configuration unexpectedly contains null.");

    internal void SeedData(string pluginId, string relativePath, byte[] bytes)
    {
        var path = Path.Combine(Storage.GetDataDirectory(pluginId), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    internal ColorCell AddColor(string key, ResourceColor baseline, string? scene = null)
    {
        var cell = new ColorCell(key, scene ?? Scenes.Snapshot.Scene, baseline);
        cell.Target = Check.Value(Targets.Register<ResourceColor>(key, () => cell.Color, () => cell.Alive,
            metadata: new(Scene: cell.Scene, Role: "color")), "Publish controlled game color target");
        cell.Channel = Check.Value(Mutations.Register(cell.Target, "color", value => cell.Color = value,
            MutationStrategy.PriorityOverride, ExternalWritePolicy.AdoptAndReapply), "Register real Core color channel");
        Resources.Cells.Add(key, cell);
        return cell;
    }

    internal static byte[] SilentPcm16Wav()
    {
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(38);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(8000);
            writer.Write(16000);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(2);
            writer.Write((short)0);
        }
        return memory.ToArray();
    }

    private void DeleteTemporaryRoot()
    {
        var target = Path.GetFullPath(DataRoot);
        var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Check.True(target.StartsWith(parent, comparison) && Path.GetFileName(target).StartsWith(TemporaryPrefix, StringComparison.Ordinal),
            "Fixture cleanup target must stay inside its private temporary directory.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var failures = new List<Exception>();
        foreach (var cleanup in new Action[] { Host.Dispose, Input.Dispose, Mutations.Dispose, Targets.Dispose, Main.Dispose, DeleteTemporaryRoot })
        {
            try { cleanup(); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0) throw new AggregateException("Fixture teardown failed.", failures);
    }
}

internal sealed class ControlledScenes : IGameStateService
{
    internal GameStateSnapshot Snapshot { get; set; } = new("MainMenu", null, null, false)
    { SessionId = "offline-session", SceneGeneration = 1, Revision = 1 };
    public OperationResult<GameStateSnapshot> GetSnapshot() => OperationResult<GameStateSnapshot>.Ok(Snapshot);
}

internal sealed class ControlledCatalog : ICatalogService
{
    internal List<ChartInfo> Charts { get; } = new()
    {
        new("chart-e", "song-e", "Echo", "IN", 10.5),
        new("chart-c", "song-c", "Charlie", "EZ", 2),
        new("chart-a", "song-a", "Alpha", "HD", 6),
        new("chart-d", "song-d", "Delta", "IN", 9),
        new("chart-b", "song-b", "Bravo", "HD", 5)
    };
    internal int Reads { get; private set; }
    public OperationResult<IReadOnlyList<ChartInfo>> GetCharts()
    {
        Reads++;
        return OperationResult<IReadOnlyList<ChartInfo>>.Ok(Charts);
    }
}

internal sealed class ControlledScores : IScoreService
{
    internal List<ScoreRecord> Records { get; } = new()
    {
        new("chart-a", 12345, .9876, 10, 0, 0, IsFullCombo: true),
        new("chart-e", 56789, .6543, 8, 1, 1, IsClear: true)
    };
    internal OperationError? Error { get; set; }
    internal int Reads { get; private set; }
    public OperationResult<IReadOnlyList<ScoreRecord>> GetBestScoresSnapshot()
    {
        Reads++;
        return Error is null ? OperationResult<IReadOnlyList<ScoreRecord>>.Ok(Records)
            : OperationResult<IReadOnlyList<ScoreRecord>>.Fail(Error);
    }
}

/// <summary>The real broker performs conflict/focus arbitration; this provider supplies scope ownership.</summary>
internal sealed class ControlledInputView : IInputActionService
{
    private readonly InputActionBroker _broker;
    private readonly IPluginScope _scope;
    internal ControlledInputView(InputActionBroker broker, IPluginScope scope) => (_broker, _scope) = (broker, scope);
    public IReadOnlyList<InputDeviceInfo> GetDevices() => new[]
    { new InputDeviceInfo(InputDeviceKind.Keyboard, InputSupport.RuntimeAvailable, "Controlled F8 edge", null) };
    public IReadOnlyList<InputBindingOwner> GetBindings() => _broker.GetBindings();
    public IReadOnlyList<InputFocusInfo> GetFocusStack() => _broker.GetFocusStack();
    public Result<IInputActionHandle> Register(InputActionDefinition definition, Action<InputActionEvent> callback)
    {
        var result = _broker.Register(_scope.PluginId, _scope.ScopeId, definition, callback, () => _scope.IsActive);
        return result.Success && result.Value is not null
            ? Result<IInputActionHandle>.Ok(_scope.Own(result.Value)) : result;
    }
}

internal sealed class F8CompetingPlugin : IRizPlugin
{
    private IInputActionHandle? _action;
    internal int Hits { get; private set; }
    public PluginManifest Manifest { get; } = new("test.f8-owner", new(1, 0, 0), new(1, 2, 0),
        RequiredCapabilities: new[] { UiInputCapabilityIds.Actions });
    public void Enable(IPluginContext context)
    {
        var actions = context.GetService<IInputActionService>(UiInputCapabilityIds.Actions);
        Check.True(actions.Available && actions.Service is not null, "Competing plugin needs the public input service.");
        _action = Check.Value(actions.Service!.Register(new("competing-f8", new(InputDeviceKind.Keyboard, "F8")),
            _ => Hits++), "Register competing plugin F8");
    }
    public void Disable() { _action?.Dispose(); _action = null; }
}

internal sealed class ControlledUi
{
    private readonly InputActionBroker _input;
    internal ControlledUi(InputActionBroker input) => _input = input;
    internal List<ControlledWindow> Windows { get; } = new();
    internal IOwnedUiService ForScope(IPluginContext context) => new View(this, (IPluginScope)context);

    private sealed class View : IOwnedUiService
    {
        private readonly ControlledUi _owner;
        private readonly IPluginScope _scope;
        internal View(ControlledUi owner, IPluginScope scope) => (_owner, _scope) = (owner, scope);
        public IReadOnlyList<UiMountInfo> GetMounts() => new[]
        { new UiMountInfo(UiMountIds.Overlay, _scope.IsActive, "controlled", null, 1, "scope") };
        public Result<IUiWindowHandle> CreateWindow(UiWindowDefinition definition)
        {
            if (!_scope.IsActive) return Result<IUiWindowHandle>.Fail(new(ErrorCode.ScopeExpired, "UI scope expired."));
            if (_owner.Windows.Any(window => window.Owner == _scope.PluginId && window.LocalId == definition.Id && window.IsAlive))
                return Result<IUiWindowHandle>.Fail(new(ErrorCode.Conflict, "An owned window already exists."));
            var window = new ControlledWindow(_scope, definition, _owner._input);
            _owner.Windows.Add(window);
            return Result<IUiWindowHandle>.Ok(_scope.Own<IUiWindowHandle>(window));
        }
    }
}

internal sealed class ControlledWindow : IUiWindowHandle
{
    private readonly InputActionBroker _input;
    private readonly UiWindowDefinition _definition;
    private readonly Dictionary<string, ControlledControl> _controls = new(StringComparer.Ordinal);
    private IDisposable? _focus;
    private bool _expired, _disposed;
    internal ControlledWindow(IPluginScope scope, UiWindowDefinition definition, InputActionBroker input)
        => (Scope, _definition, _input, Owner, LocalId, Id) =
            (scope, definition, input, scope.PluginId, definition.Id, UiLocalIds.Qualify(scope.PluginId, definition.Id));
    internal IPluginScope Scope { get; }
    internal string Owner { get; }
    internal string LocalId { get; }
    internal int DisposeCalls { get; private set; }
    public string Id { get; }
    public bool IsAlive => !_disposed && !_expired && Scope.IsActive;
    internal ControlledControl Control(string id) => _controls[id];
    internal string Text(string id) => Control(id).Text;
    internal void Click(string id) => Control(id).Click();
    internal void Enter(string id, string text) => Control(id).Enter(text);
    public Result<IUiControlHandle> AddLabel(UiControlDefinition definition) => Add(definition, ControlKind.Label, null, null, int.MaxValue);
    public Result<IUiControlHandle> AddButton(UiControlDefinition definition, Action clicked) => Add(definition, ControlKind.Button, clicked, null, int.MaxValue);
    public Result<IUiControlHandle> AddTextField(UiControlDefinition definition, Action<string>? changed = null, int characterLimit = 1024)
        => Add(definition, ControlKind.TextField, null, changed, characterLimit);
    private Result<IUiControlHandle> Add(UiControlDefinition definition, ControlKind kind, Action? clicked, Action<string>? changed, int limit)
    {
        if (!IsAlive) return Result<IUiControlHandle>.Fail(new(ErrorCode.ScopeExpired, "Window expired."));
        if (_controls.ContainsKey(definition.Id)) return Result<IUiControlHandle>.Fail(new(ErrorCode.Conflict, "Control already exists."));
        var control = new ControlledControl(this, definition, kind, clicked, changed, limit);
        _controls.Add(definition.Id, control);
        return Result<IUiControlHandle>.Ok(control);
    }
    public Result Focus()
    {
        if (!IsAlive) return Result.Fail(new(ErrorCode.ScopeExpired, "Window expired."));
        _focus?.Dispose();
        _focus = _input.PushFocus(Owner, Id, _definition.Modal, () => IsAlive);
        return Result.Ok();
    }
    internal Result FocusText()
    {
        var result = Focus();
        if (result.Success) _input.SetTextEditing(Id, true);
        return result;
    }
    public Result SetLayout(UiLayout layout) => IsAlive ? Result.Ok() : Result.Fail(new(ErrorCode.ScopeExpired, "Window expired."));
    public Result<UiViewport> GetViewport() => Result<UiViewport>.Ok(new(1920, 1080, 96, 0, 0, 1920, 1080, 1920, 1080, 1, 0, 0));
    public Result<UiPoint> ScreenToUi(UiPoint screenPixels) => Result<UiPoint>.Ok(new(screenPixels.X, 1080 - screenPixels.Y));
    public Result<UiPoint> UiToScreen(UiPoint referencePixels) => Result<UiPoint>.Ok(new(referencePixels.X, 1080 - referencePixels.Y));
    internal void Expire()
    {
        _expired = true;
        _focus?.Dispose();
        _focus = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeCalls++;
        _focus?.Dispose();
        _focus = null;
        foreach (var control in _controls.Values) control.Dispose();
    }
}

internal enum ControlKind { Label, Button, TextField }
internal sealed class ControlledControl : IUiControlHandle
{
    private readonly ControlledWindow _window;
    private readonly ControlKind _kind;
    private readonly Action? _clicked;
    private readonly Action<string>? _changed;
    private readonly int _limit;
    private bool _disposed;
    internal ControlledControl(ControlledWindow window, UiControlDefinition definition, ControlKind kind,
        Action? clicked, Action<string>? changed, int limit)
        => (_window, Id, Text, _kind, _clicked, _changed, _limit) =
            (window, window.Id + "/" + definition.Id, definition.Text, kind, clicked, changed, limit);
    public string Id { get; }
    internal string Text { get; private set; }
    private bool Alive => !_disposed && _window.IsAlive;
    public Result SetText(string text)
    {
        if (!Alive) return Result.Fail(new(ErrorCode.ScopeExpired, "Control expired."));
        Text = text.Length > _limit ? text[.._limit] : text;
        return Result.Ok();
    }
    public Result<string> GetText() => Alive ? Result<string>.Ok(Text) : Result<string>.Fail(new(ErrorCode.ScopeExpired, "Control expired."));
    public Result Focus() => !Alive ? Result.Fail(new(ErrorCode.ScopeExpired, "Control expired."))
        : _kind == ControlKind.TextField ? _window.FocusText()
        : Result.Fail(new(ErrorCode.OperationUnsupported, "Only text fields accept focus."));
    internal void Enter(string text)
    {
        Check.True(_kind == ControlKind.TextField, "Only a text field can receive entered text.");
        Check.Ok(SetText(text), "Enter controlled text");
        if (Alive) _changed?.Invoke(Text);
    }
    internal void Click()
    {
        Check.True(Alive && _kind == ControlKind.Button, "Only a live button can be clicked.");
        _clicked?.Invoke();
    }
    internal Action QueuedActivation()
    {
        Check.True(_kind == ControlKind.Button, "Only buttons queue activation callbacks.");
        var callback = _clicked;
        // Models the SDK's delivery-time scope/window guard after a scene or disposal.
        return () => { if (Alive) callback?.Invoke(); };
    }
    public void Dispose() => _disposed = true;
}

internal sealed class ColorCell
{
    internal ColorCell(string key, string scene, ResourceColor baseline) => (Key, Scene, Color) = (key, scene, baseline);
    internal string Key { get; }
    internal string Scene { get; }
    internal ResourceColor Color { get; set; }
    internal bool Alive { get; set; } = true;
    internal TargetHandle<ResourceColor> Target { get; set; } = null!;
    internal MutationChannel<ResourceColor> Channel { get; set; } = null!;
}

/// <summary>Target values are controlled; priority selection and restoration belong to Core.</summary>
internal sealed class ControlledResources
{
    private readonly ControlledScenes _scenes;
    internal ControlledResources(ControlledScenes scenes) => _scenes = scenes;
    internal Dictionary<string, ColorCell> Cells { get; } = new(StringComparer.Ordinal);
    internal List<ControlledColorOverride> Leases { get; } = new();
    internal int TargetReads { get; private set; }
    internal IGameResourceService ForScope(IPluginContext context) => new View(this, (IPluginScope)context);
    private sealed class View : IGameResourceService
    {
        private readonly ControlledResources _owner;
        private readonly IPluginScope _scope;
        internal View(ControlledResources owner, IPluginScope scope) => (_owner, _scope) = (owner, scope);
        public OperationResult<IReadOnlyList<GameResourceTarget>> GetTargets()
        {
            _owner.TargetReads++;
            return OperationResult<IReadOnlyList<GameResourceTarget>>.Ok(_owner.Cells.Values
                .Where(cell => cell.Alive && cell.Scene == _owner._scenes.Snapshot.Scene)
                .Select(cell => new GameResourceTarget(cell.Key, "color", "Controlled game color")).ToArray());
        }
        public OperationResult<IResourceOverride> OverrideColor(GameColorOverrideRequest request)
        {
            if (!_owner.Cells.TryGetValue(request.ResourceKey, out var cell) || !cell.Alive || cell.Scene != _owner._scenes.Snapshot.Scene)
                return OperationResult<IResourceOverride>.Fail(ErrorCode.TargetMissing, "Controlled target is absent.", GameCapabilityIds.Resources, request.ResourceKey);
            var applied = cell.Channel.Override(_scope.PluginId, request.Color, request.Priority);
            if (!applied.Success || applied.Value is null) return OperationResult<IResourceOverride>.Fail(applied.Error!);
            var lease = new ControlledColorOverride(request.ResourceKey, _scope, applied.Value);
            _owner.Leases.Add(lease);
            // Core's public GameResources scoped view owns this returned override.
            return OperationResult<IResourceOverride>.Ok(lease);
        }
    }
}

internal sealed class ControlledColorOverride : IResourceOverride
{
    private readonly IMutationLease<ResourceColor> _inner;
    internal ControlledColorOverride(string resourceKey, IPluginScope scope, IMutationLease<ResourceColor> inner)
        => (ResourceKey, Scope, _inner) = (resourceKey, scope, inner);
    public string ResourceKey { get; }
    internal IPluginScope Scope { get; }
    internal bool IsActive => _inner.IsActive;
    public void Dispose() => _inner.Dispose();
}

internal enum StopBehavior { Succeed, Fail, Throw }
internal enum StartBehavior { Confirmed, ThrowAfterOwning, SuccessWithoutLease, MissingResult }
internal sealed record AudioLoad(ContentFile File, byte[] Bytes, IPluginScope Scope, CancellationToken Token, ControlledAsset Asset);
internal sealed record AudioPlay(ControlledAsset Asset, float Volume, bool Loop, ControlledPlayback Playback, bool Returned);

/// <summary>No native decoding or playback. Returned and unknown sources are explicitly owned by the public child scope.</summary>
internal sealed class ControlledAudio
{
    private readonly PluginStorageManager _storage;
    internal ControlledAudio(PluginStorageManager storage) => _storage = storage;
    internal List<AudioLoad> Loads { get; } = new();
    internal List<AudioPlay> Plays { get; } = new();
    internal List<string> Events { get; } = new();
    internal StopBehavior StopBehavior { get; set; }
    internal StartBehavior StartBehavior { get; set; }
    internal IAudioPlaybackService ForScope(IPluginContext context) => new View(this, (IPluginScope)context);
    private sealed class View : IAudioPlaybackService
    {
        private readonly ControlledAudio _owner;
        private readonly IPluginScope _scope;
        internal View(ControlledAudio owner, IPluginScope scope) => (_owner, _scope) = (owner, scope);
        public Result<IAudioAssetLease> LoadWav(ContentFile file, CancellationToken cancellationToken = default)
        {
            if (!_scope.IsActive || cancellationToken.IsCancellationRequested)
                return Result<IAudioAssetLease>.Fail(new(ErrorCode.ScopeExpired, "Controlled audio scope expired."));
            if (file.Area != ContentFileArea.PluginData)
                return Result<IAudioAssetLease>.Fail(new(ErrorCode.InvalidArgument, "Fixture only reads plugin Data WAVs."));
            var path = Path.Combine(_owner._storage.GetDataDirectory(_scope.PluginId), file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 1 || bytes.Length > file.MaximumBytes)
                return Result<IAudioAssetLease>.Fail(new(ErrorCode.ContentInvalid, "Controlled WAV exceeds the caller's byte allowance."));
            _scope.Own<IDisposable>(_scope.Lifetime.Register(() => _owner.Events.Add("scope-cancelled:" + _scope.ScopeId)));
            var asset = new ControlledAsset(_owner, _scope, "asset-" + (_owner.Loads.Count + 1));
            _scope.Own<IAudioAssetLease>(asset);
            _owner.Loads.Add(new(file, bytes, _scope, cancellationToken, asset));
            _owner.Events.Add("load:" + asset.Asset.Id);
            return Result<IAudioAssetLease>.Ok(asset);
        }
        public Result<IAudioPlaybackLease> Play(AudioAsset clip, float volume = 1, bool loop = false)
        {
            var asset = _owner.Loads.Select(load => load.Asset).Single(value => value.Asset == clip);
            if (!asset.IsValid || !ReferenceEquals(asset.Scope, _scope))
                return Result<IAudioPlaybackLease>.Fail(new(ErrorCode.ScopeExpired, "Controlled clip scope expired."));
            var source = new ControlledPlayback(_owner, asset, "source-" + (_owner.Plays.Count + 1));
            _scope.Own<IAudioPlaybackLease>(source);
            _owner.Plays.Add(new(asset, volume, loop, source, _owner.StartBehavior == StartBehavior.Confirmed));
            _owner.Events.Add("play:" + source.Id);
            if (_owner.StartBehavior == StartBehavior.ThrowAfterOwning) throw new InvalidOperationException("Controlled Play threw after owning an unknown source.");
            if (_owner.StartBehavior == StartBehavior.SuccessWithoutLease) return Result<IAudioPlaybackLease>.Ok(null!);
            if (_owner.StartBehavior == StartBehavior.MissingResult) return null!;
            return Result<IAudioPlaybackLease>.Ok(source);
        }
    }
}

internal sealed class ControlledAsset : IAudioAssetLease
{
    private readonly ControlledAudio _owner;
    private bool _disposed;
    internal ControlledAsset(ControlledAudio owner, IPluginScope scope, string id)
        => (_owner, Scope, Asset) = (owner, scope, new AudioAsset(id));
    internal IPluginScope Scope { get; }
    internal int DisposeCalls { get; private set; }
    public AudioAsset Asset { get; }
    public int SampleFrames => 1;
    public int Channels => 1;
    public int Frequency => 8000;
    public bool IsValid => !_disposed && Scope.IsActive;
    public void Dispose()
    {
        if (_disposed) return;
        Check.True(!_owner.Plays.Any(play => ReferenceEquals(play.Asset, this) && play.Playback.IsActive),
            "A WAV asset must not be released before its owned source stops.");
        _disposed = true;
        DisposeCalls++;
        _owner.Events.Add("dispose-asset:" + Asset.Id);
    }
}

internal sealed class ControlledPlayback : IAudioPlaybackLease
{
    private readonly ControlledAudio _owner;
    private bool _playing = true, _disposed;
    internal ControlledPlayback(ControlledAudio owner, ControlledAsset asset, string id) => (_owner, Asset, Id) = (owner, asset, id);
    internal ControlledAsset Asset { get; }
    internal string Id { get; }
    internal int StopCalls { get; private set; }
    internal int DisposeCalls { get; private set; }
    public bool IsActive => !_disposed && _playing;
    public Result Stop()
    {
        StopCalls++;
        if (_owner.StopBehavior == StopBehavior.Throw)
        {
            _owner.Events.Add("stop-threw:" + Id);
            throw new InvalidOperationException("Controlled Stop threw without confirming source shutdown.");
        }
        if (_owner.StopBehavior == StopBehavior.Fail)
        {
            _owner.Events.Add("stop-failed:" + Id);
            return Result.Fail(new(ErrorCode.ServiceProviderFailed, "Controlled Stop refused without confirming source shutdown.", AudioContentCapabilityIds.Playback));
        }
        _playing = false;
        _owner.Events.Add("stop-ok:" + Id);
        return Result.Ok();
    }
    public void Dispose()
    {
        if (_disposed) return;
        // Provider scope withdrawal is the fixture's successful emergency cleanup boundary.
        _playing = false;
        _disposed = true;
        DisposeCalls++;
        _owner.Events.Add("dispose-playback:" + Id);
    }
}
