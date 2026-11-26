using RizToolbox.Adapter.Modules;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Core;
using RizToolbox.Sdk;

internal static class OfficialChartSelectionRegression
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Official chart read is a caller-driven scoped service and does not dispatch navigation", ScopedRead),
        ("Official chart read keeps the chart and exact stock Play state without Play focus", UnfocusedChart),
        ("Official chart read needs no EventSystem for the chart and removes stale button focus", AbsentOrChangedEventSystem),
        ("Official chart read leaves Play absent for missing, hidden, ambiguous or foreign buttons", UnverifiedButtons),
        ("Official chart read only downgrades expected optional button failures", OptionalButtonFailures),
        ("Official chart read requires the exact enabled persistent PlayLevel listener", StockListener),
        ("Official chart read rejects scene, owner or chart drift even without optional button evidence", StableIdentity)
    };
    private const string NormalPlay = "Content/Levels/PlayButton";
    private const string DiscOPlay = "Content/LevelsForDiscO/PlayButton";
    private static readonly NativeUiInputBackend.OfficialSelectionIdentity Identity = new(8, new(4), new(5), new(6), new(7), 0);
    private static readonly NativeUiInputBackend.OfficialButtonFocus Focus = new(new(1), new(2));
    private static readonly NativeUiInputBackend.OfficialPlayButtonObservation Button = new(new(3), -43, true, true);
    private static readonly OfficialChartSelectionSnapshot Chart = new("chart.PastelLines.RekuMochizuki.0.EZ", 8, -41, -42, PlayButton: null);

    private static void ScopedRead()
    {
        using var main = new MainThreadDispatcher();
        var log = new CaptureLog();
        var events = new EventHub(log);
        var backend = new Backend { SceneIdentity = "8:LevelList" };
        var registry = new CapabilityRegistry(new("test", "test", "test", "test"));
        using var modules = new ModuleManager(registry, new UiDemandSymbols(), log, "test", "test");
        var module = new UiInputModule(() => backend, main, log, events);
        modules.Add(module, RizToolbox.Adapter.RizlineAdapter.IsDemandManaged(module));
        modules.SetCapabilityDemand(new[] { UiInputCapabilityIds.OfficialChartSelection });
        modules.Tick(1);
        using var host = new PluginHost(registry, main, events, log);
        IOfficialChartSelectionService? reader = null;
        host.Register(new Plugin("test.official-chart", context =>
            reader = context.GetService<IOfficialChartSelectionService>(UiInputCapabilityIds.OfficialChartSelection).Service));
        Require(host.Enable("test.official-chart").Success && reader is not null);

        events.Publish(new FrameUpdate(1));
        Require(backend.OfficialSelectionReads == 0);
        Require(reader!.ReadCurrent().Error?.Code == ErrorCode.NavigationUnavailable);
        var stock = Chart with { PlayButton = new(-43, true, false, false) };
        backend.OfficialSelection = () => Result<OfficialChartSelectionSnapshot>.Ok(stock);
        Require(reader.ReadCurrent().Value == stock);
        Require(Task.Run(reader.ReadCurrent).GetAwaiter().GetResult().Error?.Code == ErrorCode.WrongThread);
        Require(backend.OfficialSelectionReads == 2 && backend.NavigationCalls == 0);
        host.Disable("test.official-chart");
        Require(reader.ReadCurrent().Error?.Code == ErrorCode.ScopeExpired);
        Require(backend.OfficialSelectionReads == 2);
    }

    private static void UnfocusedChart()
    {
        var paths = new List<string>();
        var unfocused = Read(() => Focus, path => { paths.Add(path); return path == NormalPlay ? Button : null; });
        Require(unfocused == (Chart with { PlayButton = new(-43, true, true, false) }));
        Require(paths.SequenceEqual(new[] { NormalPlay, DiscOPlay }));

        var noSelectedObject = Read(() => Focus with { SelectedGameObject = IntPtr.Zero },
            path => path == DiscOPlay ? Button with { Interactable = false } : null);
        Require(noSelectedObject == (Chart with { PlayButton = new(-43, true, false, false) }));

        var focused = Read(() => Focus with { SelectedGameObject = Button.GameObject },
            path => path == NormalPlay ? Button with { Active = false } : null);
        Require(focused == (Chart with { PlayButton = new(-43, false, true, true) }));
    }

    private static void AbsentOrChangedEventSystem()
    {
        var buttonReads = 0;
        Require(Read(() => null, _ => { ++buttonReads; return Button; }) == Chart);
        Require(buttonReads == 0);

        var focusReads = 0;
        Require(Read(() => ++focusReads == 1 ? Focus : Focus with { EventSystem = new(10) },
            path => path == NormalPlay ? Button : null) == Chart);
        Require(focusReads == 2);
    }

    private static void UnverifiedButtons()
    {
        Require(Read(() => Focus, _ => null) == Chart);
        Require(Read(() => Focus, path => path == NormalPlay ? Button : Button with { GameObject = new(11) }) == Chart);

        var foreign = new NativeUiInputBackend.OfficialPlayListener(new(100), "LevelCardControl, Assembly-CSharp", "PlayLevel", 1, 2);
        Require(Read(() => Focus, path => {
            if (path == NormalPlay) return null;
            NativeUiInputBackend.RequireOfficialPlayListener(Identity.Card, foreign);
            return Button;
        }) == Chart);
    }

    private static void OptionalButtonFailures()
    {
        Require(Read(() => Focus, _ => throw new MissingMethodException("Optional stock Play method is absent.")) == Chart);
        Require(Read(() => Focus, _ => throw new UiBackendException(ErrorCode.TargetExpired, "Stock Play changed.")) == Chart);
        Reject(ErrorCode.WrongThread, () => Read(() => throw new UiBackendException(ErrorCode.WrongThread, "Dispatcher refused access."), _ => Button));

        var nativeFailure = new InvalidOperationException("Native Play observation failed.");
        try { Read(() => Focus, _ => throw nativeFailure); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, nativeFailure)) { return; }
        throw new InvalidOperationException("Expected the unexpected native observation failure to propagate.");
    }

    private static void StockListener()
    {
        var card = new IntPtr(200);
        var stock = new NativeUiInputBackend.OfficialPlayListener(card, "LevelCardControl, Assembly-CSharp", "PlayLevel", 1, 2);
        NativeUiInputBackend.RequireOfficialPlayListener(card, stock);
        foreach (var different in new[] {
            stock with { Target = new(201) }, stock with { Type = "WorkshopLevelCardControl, Assembly-CSharp" },
            stock with { Method = "SetChartIndex" }, stock with { Mode = 3 }, stock with { State = 0 } })
            Reject(ErrorCode.NavigationUnavailable, () => NativeUiInputBackend.RequireOfficialPlayListener(card, different));
    }

    private static void StableIdentity()
    {
        NativeUiInputBackend.RequireOfficialSelectionIdentity(Identity, Identity);
        foreach (var different in new[] {
            Identity with { Scene = 9 }, Identity with { LevelList = new(14) }, Identity with { LevelLoop = new(15) },
            Identity with { Card = new(16) }, Identity with { Level = new(17) }, Identity with { ChartIndex = 1 } })
            Reject(ErrorCode.TargetExpired, () => NativeUiInputBackend.CompleteOfficialChartRead(Chart, Identity, () => different, () => null, _ => null));

        var latest = Identity;
        Reject(ErrorCode.TargetExpired, () => NativeUiInputBackend.CompleteOfficialChartRead(Chart, Identity, () => latest, () => Focus,
            path => { latest = latest with { ChartIndex = 1 }; return path == NormalPlay ? Button : null; }));
    }

    private static OfficialChartSelectionSnapshot Read(Func<NativeUiInputBackend.OfficialButtonFocus?> readFocus,
        Func<string, NativeUiInputBackend.OfficialPlayButtonObservation?> readButton)
        => NativeUiInputBackend.CompleteOfficialChartRead(Chart, Identity, () => Identity, readFocus, readButton);

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Official chart selection regression failed.");
    }

    private static void Reject(ErrorCode code, Action action)
    {
        try { action(); }
        catch (UiBackendException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }
}
