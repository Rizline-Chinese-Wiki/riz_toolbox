using System.Runtime.InteropServices;
using RizToolbox.Runtime;

sealed partial class FakeNative
{
    internal sealed record ScoreSource(IntPtr Manager, IntPtr Assets, IntPtr Control, IntPtr BestScore);

    private readonly Dictionary<IntPtr, IntPtr> _scoreLevels = new();
    private readonly Dictionary<IntPtr, IntPtr[]> _scoreLists = new();
    private IntPtr _scoreManagerField;
    internal NativeRuntime ScoreCaptureApi = null!;
    internal IntPtr ScoreManagerClass, ScoreManager;
    internal ScoreSource InitialScoreSource = null!;
    internal ScoreSource ReplacementScoreSource = null!;
    internal int GetAllLevelCalls;
    internal bool ReturnNullLevelList;
    internal Action? AfterGetAllLevel, AfterLevelItem;

    internal void ConfigureScoreCapture()
    {
        ConfigureGameplay();
        var managerType = AddType("GameManager", 18, false, IntPtr.Size);
        var assetsType = AddType("AssetManager", 18, false, IntPtr.Size);
        var controlType = AddType("LevelControl", 18, false, IntPtr.Size);
        var levelType = AddType("Level", 18, false, IntPtr.Size);
        var differenceType = AddType("LevelDifference", 18, false, IntPtr.Size);
        var chartType = AddType("ChartInfo", 18, false, IntPtr.Size);
        var scoreType = AddType("BestScore", 18, false, IntPtr.Size);
        var levelListType = AddListType("Level");
        var chartListType = AddListType("LevelDifference");
        GameplayMethodAdd(controlType, "GetAllLevel", false, levelListType);

        ScoreManagerClass = ClassOf(managerType);
        GameplayField("score.capture.instance", managerType, "Instance", managerType, IntPtr.Zero, IntPtr.Zero);
        _scoreManagerField = _gameplayFields["score.capture.instance"].Field;
        InitialScoreSource = AddSource("initial", 100);
        ReplacementScoreSource = AddSource("replacement", 200);
        ScoreManager = InitialScoreSource.Manager;

        Add("il2cpp_field_get_flags", "Flags", nameof(ScoreFieldFlags));
        Add("il2cpp_field_static_get_value", "StaticGet", nameof(ReadScoreStaticField));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(InvokeScoreCapture));
        ScoreCaptureApi = new NativeRuntime(ExportPointer);
        ScoreCaptureApi.BindCurrentThread();

        IntPtr AddListType(string itemName)
        {
            var type = AddType($"System.Collections.Generic.List<{itemName}>", 21, false, IntPtr.Size);
            GameplayMethodAdd(type, "get_Count", false, Int32);
            GameplayMethodAdd(type, "get_Item", false, Object, Int32);
            return type;
        }

        ScoreSource AddSource(string id, int points)
        {
            IntPtr Create(IntPtr type) => Box(type, new byte[16]);
            var manager = Create(managerType);
            var assets = Create(assetsType);
            var control = Create(controlType);
            var level = Create(levelType);
            var difference = Create(differenceType);
            var chart = Create(chartType);
            var score = Create(scoreType);
            var levels = Create(levelListType);
            var charts = Create(chartListType);

            _scoreLevels[control] = levels;
            _scoreLists[levels] = new[] { level };
            _scoreLists[charts] = new[] { difference };
            AddScoreField(manager, "assetManager", assetsType, assets);
            AddScoreField(manager, "levelControl", controlType, control);
            AddScoreField(assets, "loadAssetListOver", Boolean, true);
            AddScoreField(level, "id", String, MakeString(id));
            AddScoreField(level, "musicInfo", Object, IntPtr.Zero);
            AddScoreField(level, "chartInfos", chartListType, charts);
            AddScoreField(difference, "chartInfo", chartType, chart);
            AddScoreField(difference, "bestScore", scoreType, score);
            AddScoreField(chart, "id", String, MakeString(id + ".chart"));
            AddScoreField(chart, "level", String, MakeString("IN"));
            AddScoreField(chart, "difficulty", Single, 10f);
            AddScoreField(score, "score", Int32, points);
            AddScoreField(score, "completeRate", Single, .5f);
            foreach (var field in new[] { "hit", "bad", "miss" }) AddScoreField(score, field, Int32, 1);
            foreach (var field in new[] { "isFullCombo", "isClear" }) AddScoreField(score, field, Boolean, false);
            return new(manager, assets, control, score);
        }
    }

    private void AddScoreField(IntPtr owner, string name, IntPtr type, object value)
    {
        var ownerType = _boxedTypes[owner];
        var field = GameplayFindField(ClassOf(ownerType), name);
        if (field == IntPtr.Zero) GameplayField($"{owner}.{name}", ownerType, name, type, owner, value);
        else
        {
            _gameplayFields[$"{owner}.{name}"] = (owner, field);
            SetScoreField(owner, name, value);
        }
    }

    internal void SetScoreField(IntPtr owner, string name, object value) => SetGameplayField($"{owner}.{name}", value);

    private uint ScoreFieldFlags(IntPtr field) => field == _scoreManagerField ? 0x10u : FieldFlags(field);
    private void ReadScoreStaticField(IntPtr field, IntPtr output) => Marshal.WriteIntPtr(output, ScoreManager);

    private IntPtr InvokeScoreCapture(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        Invocations++;
        AllGameplayReceiversRooted &= HasRootFor(instance);
        Marshal.WriteIntPtr(exception, IntPtr.Zero);
        switch (_gameplayMethods[method].Name)
        {
            case "GetAllLevel":
                GetAllLevelCalls++;
                var levels = _scoreLevels[instance];
                AfterGetAllLevel?.Invoke();
                return ReturnNullLevelList ? IntPtr.Zero : levels;
            case "get_Count":
                return Box(Int32, BitConverter.GetBytes(_scoreLists[instance].Length));
            case "get_Item":
                var item = _scoreLists[instance][Marshal.ReadInt32(Marshal.ReadIntPtr(arguments))];
                if (_scoreLevels.ContainsValue(instance)) AfterLevelItem?.Invoke();
                return item;
            default:
                throw new InvalidOperationException("Unexpected score capture fixture method.");
        }
    }
}
