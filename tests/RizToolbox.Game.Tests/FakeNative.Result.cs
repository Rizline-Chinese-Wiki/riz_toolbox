using System.Runtime.InteropServices;
using RizToolbox.Runtime;

sealed partial class FakeNative
{
    internal IntPtr ResultObject, ResultParameters, ResultMetadata;
    internal int ResultScore = 580000;
    internal float ResultRate = .75f;
    internal Action? AfterResultScore;

    internal void ConfigureTerminalResult()
    {
        ConfigureGameplay();
        var controllerType = _boxedTypes[GameplayController];
        var resultType = AddType("InsideLevelResult", 18, false, 8);
        var parametersType = AddType("InsideLevelParams", 18, false, 8);
        var metadataType = AddType("LevelMetadata", 18, false, 8);
        var difficultyType = AddType("FixtureDifficulty", 18, false, 8);
        var chartType = AddType("FixtureChart", 18, false, 8);
        ResultObject = Box(resultType, new byte[16]);
        ResultParameters = Box(parametersType, new byte[16]);
        ResultMetadata = Box(metadataType, new byte[16]);
        var difficulty = Box(difficultyType, new byte[16]);
        var chart = Box(chartType, new byte[16]);
        GameplayField("controller.parameters", controllerType, "LevelParams", parametersType, GameplayController, ResultParameters);
        GameplayField("parameters.metadata", parametersType, "metadata", metadataType, ResultParameters, ResultMetadata);
        GameplayField("result.metadata", resultType, "metadata", metadataType, ResultObject, ResultMetadata);
        GameplayField("metadata.chartInfo", metadataType, "chartInfo", difficultyType, ResultMetadata, difficulty);
        GameplayField("difficulty.chartInfo", difficultyType, "chartInfo", chartType, difficulty, chart);
        GameplayField("chart.id", chartType, "id", String, chart, MakeString("fixture.chart"));
        foreach (var (name, value) in new[] { ("perfect", 53), ("maxPerfect", 8), ("bad", 2), ("miss", 16) })
            GameplayField("result." + name, resultType, name, Int32, ResultObject, value);
        GameplayField("result.crashed", resultType, "crashed", Boolean, ResultObject, false);
        GameplayField("result.skip", resultType, "skip", Boolean, ResultObject, false);
        GameplayMethodAdd(resultType, "get_Score", false, Int32);
        GameplayMethodAdd(resultType, "get_RawCompleteRate", false, Single);
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(InvokeResultGetter));
        GameplayApi = new NativeRuntime(ExportPointer);
        GameplayApi.BindCurrentThread();
    }

    private IntPtr InvokeResultGetter(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        if (instance != ResultObject) return GameplayInvoke(method, instance, arguments, exception);
        AllGameplayReceiversRooted &= HasRootFor(instance);
        Marshal.WriteIntPtr(exception, IntPtr.Zero);
        switch (_gameplayMethods[method].Name)
        {
            case "get_Score":
                var current = ResultScore;
                AfterResultScore?.Invoke();
                return Box(Int32, BitConverter.GetBytes(current));
            case "get_RawCompleteRate":
                return Box(Single, BitConverter.GetBytes(ResultRate));
            default:
                throw new InvalidOperationException("Unexpected fixture result getter.");
        }
    }
}
