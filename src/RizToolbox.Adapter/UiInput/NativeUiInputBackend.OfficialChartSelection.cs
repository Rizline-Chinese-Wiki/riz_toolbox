using System.Reflection;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using UnityEngine.Events;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.UiInput;

internal sealed partial class NativeUiInputBackend
{
    private const string OfficialInputFingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private const string OfficialPlayMethod = "PlayLevel";
    private const string OfficialPlayListenerType = "LevelCardControl, Assembly-CSharp";
    private const string NormalPlayPath = "Content/Levels/PlayButton";
    private const string DiscOPlayPath = "Content/LevelsForDiscO/PlayButton";
    private readonly AdapterBuildProfile? _officialChartBuild;
    private OfficialChartMetadata? _officialChartMetadata;
    private OfficialPlayButtonMetadata? _officialPlayMetadata;
    private bool _readingOfficialChart;

    private enum OfficialChartUse { Observe, CheckSubmit, Submit }

    public Result<OfficialChartSelectionSnapshot> ReadOfficialChartSelection()
        => UseOfficialChart(OfficialChartUse.Observe);

    private Result<OfficialChartSelectionSnapshot> UseOfficialChart(OfficialChartUse use)
    {
        _main.VerifyAccess();
        var observe = use == OfficialChartUse.Observe;
        Result<OfficialChartSelectionSnapshot> Fail(ErrorCode code, string message)
            => Result<OfficialChartSelectionSnapshot>.Fail(new(code, message,
                observe ? UiInputCapabilityIds.OfficialChartSelection : UiInputCapabilityIds.Navigation,
                observe ? null : SubmitSelectedOfficialPlay,
                Retryable: code is ErrorCode.NavigationUnavailable or ErrorCode.TargetExpired));

        if (_disposed) return Fail(ErrorCode.ScopeExpired, "The native UI backend has expired.");
        if (_officialChartBuild is not { GameAssemblySha256: OfficialInputFingerprint, UnityVersion: "2022.3.62f2" }
            || !OperatingSystem.IsWindows() || IntPtr.Size != 8)
            return Fail(ErrorCode.UnsupportedBuild, "Official chart access requires the pinned Steam Windows x64 build.");
        if (_readingOfficialChart) return Fail(ErrorCode.Conflict, "An official chart operation is already in progress.");

        _readingOfficialChart = true;
        try { return Result<OfficialChartSelectionSnapshot>.Ok(CaptureOfficialChart(use)); }
        catch (UiBackendException error) { return Fail(error.Code, error.Message); }
        catch (TargetExpiredException error) { return Fail(ErrorCode.TargetExpired, error.Message); }
        catch (Exception error) when (error is MissingMemberException or UnsupportedSignatureException)
        { return Fail(ErrorCode.UnsupportedSignature, error.Message); }
        catch (Exception error) { return Fail(ErrorCode.ObservationUnavailable, error.Message); }
        finally { _readingOfficialChart = false; }
    }

    private OfficialChartSelectionSnapshot CaptureOfficialChart(OfficialChartUse use)
    {
        var scene = RequireOfficialScene();
        var metadata = _officialChartMetadata ??= new OfficialChartMetadata(this);
        using var list = HoldOfficialObject(_api.ReadStatic<IntPtr>(metadata.LevelList, "instance"), "LevelList", metadata.LevelList, unity: true);
        if (!_api.Unbox<bool>(_api.CallStatic(metadata.LevelList, "get_LoadOver")))
            throw UnavailableOfficialChart("The official LevelList is still loading or changing disc.");
        using var loop = HoldOfficialObject(_api.Read<IntPtr>(list.Target, "levelLoopList"), "level loop", metadata.Loop, unity: true);
        using var card = HoldOfficialObject(_api.Read<IntPtr>(loop.Target, "levelCardControl"), "official level card", metadata.Card, unity: true);
        RequireOfficialCardOwners(list, loop, card);
        using var listObject = HoldOfficialOwner(list, scene);
        using var loopObject = HoldOfficialOwner(loop, scene);
        using var cardObject = HoldOfficialOwner(card, scene);

        using var level = HoldOfficialObject(_api.Read<IntPtr>(card.Target, "level"), "selected official level");
        _api.RequireAssignable(metadata.Level, _api.ObjectClass(level.Target));
        var chartIndex = _api.Read<int>(card.Target, "currentChartIndex");
        using var charts = HoldOfficialObject(_api.Read<IntPtr>(level.Target, "chartInfos"), "official charts", metadata.ChartList);
        var chartsVersion = _api.Read<int>(charts.Target, "_version");
        if (chartIndex < 0 || chartIndex >= _api.Read<int>(charts.Target, "_size"))
            throw UnavailableOfficialChart("The official card has no selected chart.");
        using var difference = HoldOfficialObject(_api.Invoke(metadata.ChartItem, charts.Target, A.Value(chartIndex)),
            "selected chart difference", metadata.Difference);
        using var chart = HoldOfficialObject(_api.Read<IntPtr>(difference.Target, "chartInfo"), "selected chart information", metadata.Chart);
        var chartId = _api.ReadString(chart.Target, "id");
        if (string.IsNullOrWhiteSpace(chartId)) throw UnavailableOfficialChart("The selected official chart ID is absent.");

        var snapshot = new OfficialChartSelectionSnapshot(chartId, scene, InstanceId(list), InstanceId(card), PlayButton: null);
        var identity = new OfficialSelectionIdentity(scene, list.Target, loop.Target, card.Target, level.Target, chartIndex);
        if (use == OfficialChartUse.Observe)
            return CompleteOfficialChartRead(snapshot, identity, ReadCurrentChart, ReadOfficialButtonFocus,
                path => ReadOfficialPlayButton(path, scene, card, cardObject));

        UseOfficialPlaySubmit(loop, card, path => ReadOfficialPlayButton(path, scene, card, cardObject),
            () => RequireOfficialSelectionIdentity(identity, ReadCurrentChart()), use == OfficialChartUse.Submit);
        return snapshot;

        OfficialSelectionIdentity ReadCurrentChart()
        {
            RequireOfficialCardOwners(list, loop, card);
            if (!_api.Unbox<bool>(_api.CallStatic(metadata.LevelList, "get_LoadOver"))
                || !IsCurrentOfficialOwner(list, listObject, scene) || !IsCurrentOfficialOwner(loop, loopObject, scene)
                || !IsCurrentOfficialOwner(card, cardObject, scene)
                || _api.Read<IntPtr>(level.Target, "chartInfos") != charts.Target || _api.Read<int>(charts.Target, "_version") != chartsVersion
                || _api.Read<IntPtr>(difference.Target, "chartInfo") != chart.Target || _api.ReadString(chart.Target, "id") != chartId)
                throw new TargetExpiredException("The official chart or its active page changed during observation.");
            return new(RequireOfficialScene(), _api.ReadStatic<IntPtr>(metadata.LevelList, "instance"),
                _api.Read<IntPtr>(list.Target, "levelLoopList"), _api.Read<IntPtr>(loop.Target, "levelCardControl"),
                _api.Read<IntPtr>(card.Target, "level"), _api.Read<int>(card.Target, "currentChartIndex"));
        }
    }

    internal static OfficialChartSelectionSnapshot CompleteOfficialChartRead(OfficialChartSelectionSnapshot chart,
        OfficialSelectionIdentity identity, Func<OfficialSelectionIdentity> readCurrentChart,
        Func<OfficialButtonFocus?> readFocus, Func<string, OfficialPlayButtonObservation?> readButton)
    {
        // The chart is independent of optional button and EventSystem observations.
        var button = TryObserveOfficialPlayButton(readFocus, readButton);
        RequireOfficialSelectionIdentity(identity, readCurrentChart());
        return chart with { PlayButton = button };
    }

    private static OfficialChartPlayButtonSnapshot? TryObserveOfficialPlayButton(Func<OfficialButtonFocus?> readFocus,
        Func<string, OfficialPlayButtonObservation?> readButton)
    {
        try
        {
            if (readFocus() is not { } focus || focus.EventSystem == IntPtr.Zero) return null;
            var normal = readButton(NormalPlayPath);
            var discO = readButton(DiscOPlayPath);
            if (normal.HasValue == discO.HasValue || readFocus() != focus) return null;
            var button = (normal ?? discO)!.Value;
            return new(button.InstanceId, button.Active, button.Interactable, button.GameObject == focus.SelectedGameObject);
        }
        catch (Exception error) when (error is MissingMemberException or AmbiguousMatchException or UnsupportedSignatureException
            or UiBackendException { Code: ErrorCode.NavigationUnavailable or ErrorCode.TargetExpired })
        {
            // The optional UI or metadata could not be verified. Runtime failures reach the outer result.
            return null;
        }
    }

    private OfficialButtonFocus? ReadOfficialButtonFocus()
    {
        using var events = CurrentEvents();
        if (events is null) return null;
        RequireMethod(_eventSystem, "get_currentSelectedGameObject", false, "UnityEngine.GameObject");
        return new(events.Target, _api.Call(events.Target, "get_currentSelectedGameObject"));
    }

    private OfficialPlayButtonObservation? ReadOfficialPlayButton(string path, int scene,
        NativeRuntime.RootHandle card, NativeRuntime.RootHandle cardObject)
    {
        var metadata = _officialPlayMetadata ??= new OfficialPlayButtonMetadata(this);
        using var cardTransform = HoldOfficialObject(Transform(cardObject.Target), "official card Transform", unity: true);
        var found = _api.Invoke(metadata.Find, cardTransform.Target, _api.Str(path));
        if (found == IntPtr.Zero) return null;
        using var transform = HoldOfficialObject(found, "stock Play button Transform", unity: true);
        using var buttonObject = HoldOfficialObject(_api.Call(transform.Target, "get_gameObject"), "stock Play button object", unity: true);
        using var page = HoldOfficialObject(_api.Call(transform.Target, "get_parent"), "stock difficulty page Transform", unity: true);
        using var content = HoldOfficialObject(_api.Call(page.Target, "get_parent"), "stock card content Transform", unity: true);
        var pageName = path == NormalPlayPath ? "Levels" : "LevelsForDiscO";
        if (_api.Call(content.Target, "get_parent") != cardTransform.Target
            || !UniqueNamedOfficialChild(cardTransform.Target, content.Target, "Content")
            || !UniqueNamedOfficialChild(content.Target, page.Target, pageName)
            || !UniqueNamedOfficialChild(page.Target, transform.Target, "PlayButton")
            || ObjectScene(buttonObject) != scene)
            throw UnavailableOfficialChart("The Play button is outside the current stock page or has an ambiguous path.");
        if (!_api.Unbox<bool>(_api.Call(buttonObject.Target, "get_activeInHierarchy"))
            || !VisibleOfficialPlayPath(transform, page.Target, metadata.CanvasGroup)) return null;

        using var button = HoldOfficialObject(GetComponent(buttonObject.Target, metadata.Button), "stock Button", metadata.Button, unity: true);
        if (_api.Call(button.Target, "get_gameObject") != buttonObject.Target)
            throw UnavailableOfficialChart("The observed Button belongs to a different GameObject.");
        using var click = HoldOfficialObject(_api.Read<IntPtr>(button.Target, "m_OnClick"), "stock Button click event", metadata.ClickEvent);
        using var group = HoldOfficialObject(_api.Read<IntPtr>(click.Target, "m_PersistentCalls"), "persistent click group", metadata.PersistentGroup);
        using var calls = HoldOfficialObject(_api.Read<IntPtr>(group.Target, "m_Calls"), "persistent click list", metadata.PersistentList);
        var callsVersion = _api.Read<int>(calls.Target, "_version");
        if (_api.Read<int>(calls.Target, "_size") != 1)
            throw UnavailableOfficialChart("The stock Play button must have one exact persistent listener.");
        using var listener = HoldOfficialObject(_api.Invoke(metadata.PersistentItem, calls.Target, A.Value(0)), "persistent Play listener", metadata.PersistentCall);
        RequireOfficialPlayListener(card.Target, ReadOfficialPlayListener(listener));

        var observation = new OfficialPlayButtonObservation(buttonObject.Target, InstanceId(button),
            _api.Unbox<bool>(_api.Call(button.Target, "IsActive")), _api.Unbox<bool>(_api.Call(button.Target, "IsInteractable")),
            button.Target);
        if (_api.Call(transform.Target, "get_parent") != page.Target || _api.Call(page.Target, "get_parent") != content.Target
            || _api.Call(content.Target, "get_parent") != cardTransform.Target || GetComponent(buttonObject.Target, metadata.Button) != button.Target
            || !_api.Unbox<bool>(_api.Call(buttonObject.Target, "get_activeInHierarchy"))
            || _api.Read<IntPtr>(button.Target, "m_OnClick") != click.Target || _api.Read<IntPtr>(click.Target, "m_PersistentCalls") != group.Target
            || _api.Read<IntPtr>(group.Target, "m_Calls") != calls.Target || _api.Read<int>(calls.Target, "_version") != callsVersion)
            throw new UiBackendException(ErrorCode.TargetExpired, "The stock Play button or its listener changed during observation.");
        RequireOfficialPlayListener(card.Target, ReadOfficialPlayListener(listener));
        return observation;
    }

    private int RequireOfficialScene()
    {
        var scene = _api.Unbox<int>(_api.CallStatic(_sceneManager, "GetActiveScene"));
        if (!_api.Unbox<bool>(_api.CallStatic(_scene, "IsValidInternal", A.Value(scene)))
            || !_api.Unbox<bool>(_api.CallStatic(_scene, "GetIsLoadedInternal", A.Value(scene)))
            || _api.Text(_api.CallStatic(_scene, "GetNameInternal", A.Value(scene))) != "LevelList")
            throw UnavailableOfficialChart("The active loaded scene is not the official LevelList.");
        return scene;
    }

    private void RequireOfficialCardOwners(NativeRuntime.RootHandle list, NativeRuntime.RootHandle loop, NativeRuntime.RootHandle card)
    {
        if (_api.Read<IntPtr>(card.Target, "levelListControl") != list.Target || _api.Read<IntPtr>(card.Target, "levelLoopList") != loop.Target)
            throw new TargetExpiredException("The stock level card no longer belongs to this LevelList and level loop.");
    }

    private NativeRuntime.RootHandle HoldOfficialOwner(NativeRuntime.RootHandle component, int scene)
    {
        var root = HoldOfficialObject(_api.Call(component.Target, "get_gameObject"), "LevelList component owner", unity: true);
        try
        {
            if (!IsCurrentOfficialOwner(component, root, scene))
                throw UnavailableOfficialChart("The official list or card is inactive or belongs to a different scene.");
            return root;
        }
        catch { root.Dispose(); throw; }
    }

    private bool IsCurrentOfficialOwner(NativeRuntime.RootHandle component, NativeRuntime.RootHandle obj, int scene)
        => ObjectScene(obj) == scene && _api.Unbox<bool>(_api.Call(component.Target, "get_isActiveAndEnabled"));

    private bool UniqueNamedOfficialChild(IntPtr parent, IntPtr selected, string name)
    {
        const int maxStockChildren = 32;
        var count = _api.Unbox<int>(_api.Call(parent, "get_childCount"));
        if (count is < 1 or > maxStockChildren) return false;
        var found = false;
        for (var index = 0; index < count; index++)
        {
            using var child = HoldOfficialObject(_api.CallTyped(parent, "GetChild", new[] { "System.Int32" }, A.Value(index)), "stock page child", unity: true);
            if (ObjectName(child) != name) continue;
            if (found || child.Target != selected) return false;
            found = true;
        }
        return found;
    }

    private bool VisibleOfficialPlayPath(NativeRuntime.RootHandle buttonTransform, IntPtr page, IntPtr canvasGroup)
    {
        const int maxStockParents = 16;
        var cursor = buttonTransform.Target;
        var verifiedPage = false;
        for (var depth = 0; depth < maxStockParents && cursor != IntPtr.Zero; depth++)
        {
            using var transform = HoldOfficialObject(cursor, "stock Play ancestor Transform", unity: true);
            using var obj = HoldOfficialObject(_api.Call(transform.Target, "get_gameObject"), "stock Play ancestor", unity: true);
            var group = GetComponent(obj.Target, canvasGroup);
            if (group != IntPtr.Zero)
            {
                using var root = HoldOfficialObject(group, "stock Play CanvasGroup", canvasGroup, unity: true);
                var alpha = _api.Unbox<float>(_api.Call(root.Target, "get_alpha"));
                if (!float.IsFinite(alpha) || alpha <= 0f) return false;
                verifiedPage |= cursor == page;
            }
            cursor = _api.Call(transform.Target, "get_parent");
        }
        return verifiedPage && cursor == IntPtr.Zero;
    }

    private NativeRuntime.RootHandle HoldOfficialObject(IntPtr pointer, string description, IntPtr expectedClass = default, bool unity = false)
    {
        if (pointer == IntPtr.Zero) throw UnavailableOfficialChart($"The {description} is unavailable.");
        var root = _api.Root(pointer);
        try
        {
            if (expectedClass != IntPtr.Zero && _api.ObjectClass(root.Target) != expectedClass)
                throw UnavailableOfficialChart($"The {description} has a different native class.");
            if (unity && !Alive(root.Target)) throw new UiBackendException(ErrorCode.TargetExpired, $"The {description} is no longer alive.");
            return root;
        }
        catch { root.Dispose(); throw; }
    }

    private int ObjectScene(NativeRuntime.RootHandle obj) => _api.Unbox<int>(_api.Call(obj.Target, "get_scene"));
    private string ObjectName(NativeRuntime.RootHandle obj) => _api.Text(_api.Call(obj.Target, "get_name"));
    private int InstanceId(NativeRuntime.RootHandle obj) => _api.Unbox<int>(_api.Call(obj.Target, "GetInstanceID"));
    private OfficialPlayListener ReadOfficialPlayListener(NativeRuntime.RootHandle listener) => new(
        _api.Read<IntPtr>(listener.Target, "m_Target"), _api.ReadString(listener.Target, "m_TargetAssemblyTypeName"),
        _api.ReadString(listener.Target, "m_MethodName"), (int)_api.Read<PersistentListenerMode>(listener.Target, "m_Mode"),
        (int)_api.Read<UnityEventCallState>(listener.Target, "m_CallState"));

    internal readonly record struct OfficialSelectionIdentity(int Scene, IntPtr LevelList, IntPtr LevelLoop, IntPtr Card, IntPtr Level, int ChartIndex);
    internal readonly record struct OfficialButtonFocus(IntPtr EventSystem, IntPtr SelectedGameObject);
    internal readonly record struct OfficialPlayButtonObservation(IntPtr GameObject, int InstanceId, bool Active, bool Interactable, IntPtr Button);
    internal static void RequireOfficialSelectionIdentity(OfficialSelectionIdentity captured, OfficialSelectionIdentity current)
    {
        if (captured != current) throw new UiBackendException(ErrorCode.TargetExpired,
            "The official LevelList, card or selected chart changed during observation.");
    }

    internal readonly record struct OfficialPlayListener(IntPtr Target, string Type, string Method, int Mode, int State);
    internal static void RequireOfficialPlayListener(IntPtr card, OfficialPlayListener listener)
    {
        const int voidMode = 1, runtimeOnly = 2;
        if (card == IntPtr.Zero || listener != new OfficialPlayListener(card, OfficialPlayListenerType, OfficialPlayMethod, voidMode, runtimeOnly))
            throw UnavailableOfficialChart("The selected Button has no exact enabled stock PlayLevel listener for the current card.");
    }

    private static UiBackendException UnavailableOfficialChart(string message) => new(ErrorCode.NavigationUnavailable, message);

    private (IntPtr List, IntPtr Item) BindOfficialList(IntPtr cls, string field, string item, int offset)
    {
        var listType = "System.Collections.Generic.List<" + item + ">";
        _api.RequireInstanceFieldLayout(cls, field, listType, offset);
        var list = _api.InspectMetadataFieldClass(cls, field, listType).FieldClass.Class;
        _api.RequireInstanceFieldLayout(list, "_size", "System.Int32", 0x18);
        _api.RequireInstanceFieldLayout(list, "_version", "System.Int32", 0x1C);
        var getter = _api.Method(list, "get_Item", 1, false, new[] { "System.Int32" });
        if (_api.DescribeReturnType(getter) != item) throw new UnsupportedSignatureException("Native List item return type differs.");
        return (list, getter);
    }

    private sealed class OfficialChartMetadata
    {
        internal readonly IntPtr LevelList, Loop, Card, Level, ChartList, ChartItem, Difference, Chart;

        internal OfficialChartMetadata(NativeUiInputBackend owner)
        {
            var api = owner._api;
            IntPtr GameType(string type) => owner.C("Assembly-CSharp.dll", "", type);
            void Field(IntPtr cls, string name, string type, int offset) => api.RequireInstanceFieldLayout(cls, name, type, offset);

            LevelList = GameType("LevelListControl"); Loop = GameType("LevelLoopList"); Card = GameType("LevelCardControl");
            Level = GameType("Level"); Difference = GameType("LevelDifference"); Chart = GameType("ChartInfo");
            api.RequireStaticFieldLayout(LevelList, "instance", "LevelListControl", 0);
            Field(LevelList, "levelLoopList", "LevelLoopList", 0x30);
            Field(Loop, "levelCardControl", "LevelCardControl", 0x38);
            Field(Card, "levelListControl", "LevelListControl", 0x90);
            Field(Card, "levelLoopList", "LevelLoopList", 0xA0);
            Field(Card, "currentChartIndex", "System.Int32", 0xD0);
            Field(Card, "level", "Level", 0xD8);
            (ChartList, ChartItem) = owner.BindOfficialList(Level, "chartInfos", "LevelDifference", 0x38);
            Field(Difference, "chartInfo", "ChartInfo", 0x10);
            Field(Chart, "id", "System.String", 0x10);
            owner.RequireMethod(LevelList, "get_LoadOver", true, "System.Boolean");
        }
    }

    private sealed class OfficialPlayButtonMetadata
    {
        internal readonly IntPtr Button, ClickEvent, PersistentGroup, PersistentList, PersistentItem, PersistentCall, CanvasGroup, Find;

        internal OfficialPlayButtonMetadata(NativeUiInputBackend owner)
        {
            var api = owner._api;
            IntPtr EventType(string type) => owner.C("UnityEngine.CoreModule.dll", "UnityEngine.Events", type);
            void Field(IntPtr cls, string name, string type, int offset) => api.RequireInstanceFieldLayout(cls, name, type, offset);
            var transform = owner.C("UnityEngine.CoreModule.dll", "UnityEngine", "Transform");
            owner.RequireMethod(transform, "Find", false, "UnityEngine.Transform", "System.String");
            Find = api.Method(transform, "Find", 1, false, new[] { "System.String" });
            CanvasGroup = owner.C("UnityEngine.UIModule.dll", "UnityEngine", "CanvasGroup");
            owner.RequireMethod(CanvasGroup, "get_alpha", false, "System.Single");
            owner.RequireMethod(owner._officialChartMetadata!.Card, OfficialPlayMethod, false, "System.Void");

            Button = owner.C("UnityEngine.UI.dll", "UnityEngine.UI", "Button");
            ClickEvent = api.NestedClass(Button, "ButtonClickedEvent");
            var eventType = api.InspectMetadataClass(ClickEvent).TypeName;
            Field(Button, "m_OnClick", eventType, 0x100);
            if (api.InspectMetadataFieldClass(Button, "m_OnClick", eventType).FieldClass.Class != ClickEvent)
                throw new UnsupportedSignatureException("The stock Button click event class differs.");
            var eventBase = EventType("UnityEventBase");
            api.RequireAssignable(eventBase, ClickEvent);
            PersistentGroup = EventType("PersistentCallGroup");
            PersistentCall = EventType("PersistentCall");
            Field(eventBase, "m_PersistentCalls", "UnityEngine.Events.PersistentCallGroup", 0x18);
            (PersistentList, PersistentItem) = owner.BindOfficialList(PersistentGroup, "m_Calls", "UnityEngine.Events.PersistentCall", 0x10);
            Field(PersistentCall, "m_Target", "UnityEngine.Object", 0x10);
            Field(PersistentCall, "m_TargetAssemblyTypeName", "System.String", 0x18);
            Field(PersistentCall, "m_MethodName", "System.String", 0x20);
            Field(PersistentCall, "m_Mode", "UnityEngine.Events.PersistentListenerMode", 0x28);
            Field(PersistentCall, "m_CallState", "UnityEngine.Events.UnityEventCallState", 0x38);
            owner.RequireMethod(Button, "IsActive", false, "System.Boolean");
            owner.RequireMethod(Button, "IsInteractable", false, "System.Boolean");
        }
    }
}
