using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

internal abstract class BuiltinModule : IAdapterModule
{
    protected readonly NativeRuntime Api;
    protected readonly IMainThreadDispatcher Main;
    protected readonly ILogger Log;
    public AdapterModuleManifest Manifest { get; }
    public virtual double IntervalSeconds => 1;
    protected BuiltinModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log, AdapterModuleManifest manifest)
    { Api = api; Main = main; Log = log; Manifest = manifest; }
    public abstract ModuleProbe Probe();
    public abstract void Register(ModuleRegistration registration);
    public abstract ModuleProbe Tick();
    public abstract void Release();
    protected static AdapterSymbolRequirement Type(string id, string image, string ns, string type)
        => new(id, image, ns, type);
    protected static AdapterSymbolRequirement Field(string id, string type, string field, string ns = "")
        => new(id, "Assembly-CSharp.dll", ns, type, AdapterSymbolKind.Field, field)
        { ExpectedType = (type, field) switch {
            ("GameManager", "assetManager") => "AssetManager", ("GameManager", "levelControl") => "LevelControl",
            ("AssetManager", "loadAssetListOver") => "System.Boolean", ("Level", "chartInfos") => "System.Collections.Generic.List<LevelDifference>",
            ("LevelDifference", "chartInfo") => "ChartInfo", ("LevelDifference", "bestScore") => "BestScore", ("ChartInfo", "id") => "System.String",
            ("GameManager", "mainMenuControl") => "MainMenuControl", ("MainMenuControl", "mainPageButtonControls") => "System.Collections.Generic.List<MainPageButtonControl>",
            ("MainPageButtonControl", "mainColor") => "UnityEngine.UI.Image", ("MainPageButtonControl", "pageIndex") => "System.Int32",
            ("NormalModeInLevelController", "LevelInformation") => "InLevel.LevelInformation", ("NormalModeInLevelController", "LevelParams") => "InsideLevelParams",
            ("LevelInformation", "levelBegan" or "levelOver") => "System.Boolean", ("LevelOverControl", "levelOverInfo") => "InsideLevelResult",
            ("LevelInformation", "progressControl") => "InLevel.ProgressControl", ("ProgressControl", "isPlaying" or "timeReady") => "System.Boolean",
            _ => null } };
    protected static AdapterSymbolRequirement Method(string id, string image, string ns, string type, string method,
        bool isStatic = false, params string[] parameters)
        => new(id, image, ns, type, AdapterSymbolKind.Method, method, isStatic, Array.AsReadOnly(parameters))
        { ExpectedType = method switch { ".ctor" or "set_color" => "System.Void", "AddComponent" => "UnityEngine.Component",
            "GetAllLevel" => "System.Collections.Generic.List<Level>", "get_color" => "UnityEngine.Color",
            "GetActiveScene" => "UnityEngine.SceneManagement.Scene", "GetNameInternal" => "System.String",
            "FindObjectsOfTypeAll" => "UnityEngine.Object[]", "get_Score" => "System.Int32", "get_RawCompleteRate" => "System.Single", _ => null } };
    protected static AdapterModuleManifest Definition(string id, string[] capabilities, AdapterSymbolRequirement[] symbols,
        string evidence, string[]? dependencies = null, string[]? scenes = null)
        => new(id, new(1, 0), Array.AsReadOnly(capabilities), Array.AsReadOnly(dependencies ?? Array.Empty<string>()),
            Array.AsReadOnly(scenes ?? new[] { "*" }), Array.AsReadOnly(symbols), evidence);
}

internal sealed class DataModule : BuiltinModule
{
    private readonly IntPtr _manager;
    private GameDataService? _service;
    private DataSnapshotService? _snapshots;
    private readonly Func<(bool Available, RunFinished? Result)>? _currentResult;
    private readonly Func<Result<IReadOnlyList<GameSettingSnapshot>>>? _settings;
    public override double IntervalSeconds => 2;
    internal DataModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log, IntPtr manager,
        Func<(bool Available, RunFinished? Result)>? currentResult = null,
        Func<Result<IReadOnlyList<GameSettingSnapshot>>>? settings = null)
        : base(api, main, log, Definition("rizline.data", new[] { CapabilityIds.Catalog, CapabilityIds.Scores,
            AdapterOperationCapabilityIds.CatalogRead, AdapterOperationCapabilityIds.ScoresRead, DataCapabilityIds.Read }, new[] {
                Field("manager.asset", "GameManager", "assetManager"), Field("manager.levels", "GameManager", "levelControl"),
                Field("assets.ready", "AssetManager", "loadAssetListOver"),
                Method("levels.all", "Assembly-CSharp.dll", "", "LevelControl", "GetAllLevel"),
                Field("level.charts", "Level", "chartInfos"), Field("difference.chart", "LevelDifference", "chartInfo"),
                Field("difference.score", "LevelDifference", "bestScore"), Field("chart.id", "ChartInfo", "id") },
            "Runtime catalog capture validates reads; historical 444-chart startup evidence is build-specific."))
    { _manager = manager; _currentResult = currentResult; _settings = settings; }
    public override ModuleProbe Probe()
    {
        _service ??= new(Api, Main, _manager);
        _snapshots ??= new(Main, Capture, observeResult: _currentResult);
        var capture = _snapshots.Refresh();
        if (!capture.Success) return Failure(capture.Error!);
        Log.Log(LogLevel.Info, $"Game data validated: {capture.Value!.ChartCount} charts / {capture.Value.BestScoreCount} records; revision={capture.Value.Token.Revision}.");
        return ModuleProbe.Available;
    }
    public override void Register(ModuleRegistration registration)
    {
        registration.Register<ICatalogService>(CapabilityIds.Catalog, _service!);
        registration.Register<IScoreService>(CapabilityIds.Scores, _service!);
        registration.Register<ICatalogService>(AdapterOperationCapabilityIds.CatalogRead, _service!);
        registration.Register<IScoreService>(AdapterOperationCapabilityIds.ScoresRead, _service!);
        registration.Register<IDataSnapshotService>(DataCapabilityIds.Read, _snapshots!);
    }
    public override ModuleProbe Tick()
    {
        var capture = _snapshots!.Refresh();
        return capture.Success ? ModuleProbe.Available : Failure(capture.Error!);
    }
    private static ModuleProbe Failure(OperationError error) => error.KnownCode is ErrorCode.UnsupportedSignature or ErrorCode.UnsupportedBuild
        ? ModuleProbe.Unsupported(error.Message) : ModuleProbe.Waiting(error.Message);
    public override void Release() { _snapshots?.Dispose(); _snapshots=null; _service = null; }
    private Result<DataSnapshotInput> Capture(CancellationToken token)
    {
        var captured = _service!.CaptureSnapshot(token);
        if (!captured.Success) return captured;
        var settings = _settings?.Invoke();
        return Result<DataSnapshotInput>.Ok(captured.Value! with { Settings = settings?.Success == true ? settings.Value : null });
    }
}

internal sealed class UiModule : BuiltinModule
{
    private UnityUiService? _service;
    internal UiModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log)
        : base(api, main, log, Definition("rizline.tool-ui", new[] { CapabilityIds.Ui, CapabilityIds.Resources,
            AdapterOperationCapabilityIds.UiCreate, AdapterOperationCapabilityIds.ToolTextureOverride }, new[] {
                Method("gameobject.new", "UnityEngine.CoreModule.dll", "UnityEngine", "GameObject", ".ctor", false, "System.String"),
                Method("gameobject.component", "UnityEngine.CoreModule.dll", "UnityEngine", "GameObject", "AddComponent", false, "System.Type"),
                Type("canvas", "UnityEngine.UIModule.dll", "UnityEngine", "Canvas"),
                Type("text", "UnityEngine.UI.dll", "UnityEngine.UI", "Text"), Type("image", "UnityEngine.UI.dll", "UnityEngine.UI", "Image") },
            "Creates an actual persistent UGUI canvas; service/handle ownership checked by Core tests.")) { }
    public override ModuleProbe Probe() { _service ??= new(Api, Main, Log); return ModuleProbe.Available; }
    public override void Register(ModuleRegistration registration)
    {
        registration.Register<IUiService>(CapabilityIds.Ui, _service!);
        registration.Register<IResourceService>(CapabilityIds.Resources, _service!);
        registration.Register<IUiService>(AdapterOperationCapabilityIds.UiCreate, _service!);
        registration.Register<IResourceService>(AdapterOperationCapabilityIds.ToolTextureOverride, _service!);
    }
    public override ModuleProbe Tick() => ModuleProbe.Available;
    public override void Release() { _service?.Dispose(); _service = null; }
}

internal sealed class MenuResourceModule : BuiltinModule
{
    private readonly IntPtr _manager;
    private readonly SemanticTargetsModule _shared;
    private GameResourceService? _service;
    internal MenuResourceModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log, IntPtr manager,SemanticTargetsModule shared)
        : base(api, main, log, Definition("rizline.menu-resources", new[] { GameCapabilityIds.Resources,
            AdapterOperationCapabilityIds.GameResourceRead, AdapterOperationCapabilityIds.GameResourceOverride }, new[] {
                Field("manager.menu", "GameManager", "mainMenuControl"), Field("menu.buttons", "MainMenuControl", "mainPageButtonControls"),
                Field("button.image", "MainPageButtonControl", "mainColor"), Field("button.index", "MainPageButtonControl", "pageIndex"),
                Method("image.color.read", "UnityEngine.UI.dll", "UnityEngine.UI", "Graphic", "get_color"),
                Method("image.color.write", "UnityEngine.UI.dll", "UnityEngine.UI", "Graphic", "set_color", false, "UnityEngine.Color"),
                Method("images.scan","UnityEngine.CoreModule.dll","UnityEngine","Resources","FindObjectsOfTypeAll",true,"System.Type"),
                Method("image.active","UnityEngine.CoreModule.dll","UnityEngine","Behaviour","get_isActiveAndEnabled") with {ExpectedType="System.Boolean"},
                Method("gameobject.scene","UnityEngine.CoreModule.dll","UnityEngine","GameObject","get_scene") with {ExpectedType="UnityEngine.SceneManagement.Scene"},
                Method("scene.valid","UnityEngine.CoreModule.dll","UnityEngine.SceneManagement","Scene","IsValidInternal",true,"System.Int32") with {ExpectedType="System.Boolean"},
                Method("scene.loaded","UnityEngine.CoreModule.dll","UnityEngine.SceneManagement","Scene","GetIsLoadedInternal",true,"System.Int32") with {ExpectedType="System.Boolean"} },
            "Game-owned Image colors use exact Graphic accessors and setter/getter read-back; optional TMP_Text.color accessors are independently checked for exact declaring class, parameters and return types. A changed TMP signature excludes TMP only. Targets determine runtime readiness; exact scene-name coverage and native TMP acceptance are not declared.",
            dependencies:new[]{TargetCapabilityIds.Targets,TargetCapabilityIds.Mutations},scenes: Array.Empty<string>())) => (_manager,_shared) = (manager,shared);
    public override ModuleProbe Probe()
    {
        _service ??= new(Api, Main, Log, _manager,_shared.Targets,_shared.Mutations);
        return _service.Refresh() > 0 ? ModuleProbe.Available : ModuleProbe.Waiting("No eligible live game UI targets are present.");
    }
    public override void Register(ModuleRegistration registration)
    {
        registration.RegisterScoped<IGameResourceService>(GameCapabilityIds.Resources, context=>_service!.ForPlugin(context));
        registration.RegisterScoped<IGameResourceService>(AdapterOperationCapabilityIds.GameResourceRead, context=>_service!.ForPlugin(context));
        registration.RegisterScoped<IGameResourceService>(AdapterOperationCapabilityIds.GameResourceOverride, context=>_service!.ForPlugin(context));
    }
    public override ModuleProbe Tick() => Probe();
    public override void Release() { _service?.Dispose(); _service = null; }
}

/// <summary>Separate observer instances isolate optional failures, while shared state retains scene/run correlation.</summary>
internal sealed class ObservationModule : BuiltinModule
{
    private readonly AdapterBuildProfile _profile;
    private readonly EventHub _events;
    private readonly ExtensionPointRegistry _extensions;
    private readonly GameEventState _state;
    private readonly bool _scenes;
    private GameEventObserver? _observer;
    internal (bool Available, RunFinished? Result) ReadCurrentResult()
        => (_observer?.RunsAvailable == true, _observer?.CurrentResult);
    internal NativeRuntime.RootHandle? AcquireObservedGameplayController()
        => _observer?.AcquireObservedGameplayController();
    public override double IntervalSeconds => .25;
    internal ObservationModule(NativeRuntime api, AdapterBuildProfile profile, IMainThreadDispatcher main,
        EventHub events, ExtensionPointRegistry extensions, ILogger log, GameEventState state, bool scenes)
        : base(api, main, log, Definition(scenes ? "rizline.scenes" : "rizline.runs",
            scenes ? new[] { GameCapabilityIds.Scenes, AdapterOperationCapabilityIds.SceneRead, AdapterOperationCapabilityIds.SceneObserve }
                : new[] { GameCapabilityIds.Runs, AdapterOperationCapabilityIds.RunRead, AdapterOperationCapabilityIds.RunObserve, GameLifecycleCapabilityIds.Lifecycle },
            Requirements(profile, scenes), scenes ? "Runtime active scene sample; transition delivery needs actual transition evidence."
                : "Normal controller flags and real result objects; a successful empty-scene poll is not gameplay acceptance.",
            new[] { CapabilityIds.Extensions }, scenes ? new[] { "*" } : Array.Empty<string>()))
    { _profile = profile; _events = events; _extensions = extensions; _state = state; _scenes = scenes; }
    private static AdapterSymbolRequirement[] Requirements(AdapterBuildProfile profile, bool scenes)
    {
        AdapterSymbolRequirement Bound(string key, string member, bool isStatic, params string[] parameters)
        {
            if (!profile.Types.TryGetValue(key, out var type)) return new("binding." + key, "<profile-missing>", "", key);
            return Method(key + "." + member, type.Assembly, type.Namespace, type.Name, member, isStatic, parameters);
        }
        if (scenes) return new[] { Bound("sceneManager", "GetActiveScene", true), Bound("scene", "GetNameInternal", true, "System.Int32") };
        return new[] { Bound("resources", "FindObjectsOfTypeAll", true, "System.Type"),
            Field("controller.info", "NormalModeInLevelController", "LevelInformation"),
            Field("controller.params", "NormalModeInLevelController", "LevelParams"),
            Field("info.began", "LevelInformation", "levelBegan", "InLevel"), Field("info.over", "LevelInformation", "levelOver", "InLevel"),
            Field("info.progress", "LevelInformation", "progressControl", "InLevel"),
            Field("progress.playing", "ProgressControl", "isPlaying", "InLevel"), Field("progress.ready", "ProgressControl", "timeReady", "InLevel"),
            Field("result.info", "LevelOverControl", "levelOverInfo"),
            Method("result.score", "Assembly-CSharp.dll", "", "InsideLevelResult", "get_Score"),
            Method("result.rate", "Assembly-CSharp.dll", "", "InsideLevelResult", "get_RawCompleteRate") };
    }
    public override ModuleProbe Probe()
    {
        _observer ??= new(Api, _profile, Main, _events, _extensions, Log, _scenes, !_scenes, _state);
        if (!Healthy) return ModuleProbe.Unsupported("Observation symbols or exact extension-point declaration are unavailable.");
        _observer.Poll();
        return Healthy ? ModuleProbe.Available : ModuleProbe.Waiting("Runtime observation failed; waiting for a fresh probe.");
    }
    private bool Healthy => _observer is not null && (_scenes ? _observer.ScenesAvailable : _observer.RunsAvailable);
    public override void Register(ModuleRegistration registration)
    {
        foreach (var id in Manifest.Capabilities)
            if (id == GameLifecycleCapabilityIds.Lifecycle)
                registration.RegisterScoped<IGameLifecycleService>(id, context => _observer!.ForPlugin(context));
            else registration.Register<IGameStateService>(id, _observer!);
    }
    public override ModuleProbe Tick()
    {
        _observer!.Poll();
        return Healthy ? ModuleProbe.Available : ModuleProbe.Waiting("Runtime observation became unavailable.");
    }
    public override void Release() { var observer = _observer; _observer = null; observer?.Dispose(); }
}
