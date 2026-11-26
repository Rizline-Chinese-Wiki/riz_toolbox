using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.UiInput;

internal sealed partial class NativeUiInputBackend
{
    internal const string SubmitSelectedOfficialPlay = "game.level-list.submit-selected-play";
    private const string EventSystemType = "UnityEngine.EventSystems.EventSystem";
    private const string EventDataType = "UnityEngine.EventSystems.BaseEventData";
    private const string ActionReferenceType = "UnityEngine.InputSystem.InputActionReference";
    private const string ActionType = "UnityEngine.InputSystem.InputAction";
    private OfficialPlaySubmitMetadata? _officialSubmitMetadata;

    internal readonly record struct OfficialPlaySubmitRoute(OfficialButtonFocus Focus, OfficialPlayButtonObservation Button,
        IntPtr InputModule, IntPtr ActionReference, IntPtr SubmitAction, IntPtr ClickEvent);

    private void UseOfficialPlaySubmit(NativeRuntime.RootHandle loop, NativeRuntime.RootHandle card,
        Func<string, OfficialPlayButtonObservation?> readButton, Action verifyChart, bool submit)
    {
        var metadata = _officialSubmitMetadata ??= new OfficialPlaySubmitMetadata(this);
        var captured = ReadRoute();
        if (!submit)
        {
            verifyChart();
            return;
        }

        using var events = HoldOfficialObject(captured.Focus.EventSystem, "current EventSystem", _eventSystem, unity: true);
        using var button = HoldOfficialObject(captured.Button.Button, "selected stock Button", metadata.Button, unity: true);
        using var data = _api.NewExact(metadata.EventData, new[] { EventSystemType }, A.Ref(events.Target));
        CompleteOfficialPlaySubmit(captured, ReadRoute, verifyChart, () =>
        {
            try { _api.Invoke(metadata.OnSubmit, button.Target, A.Ref(data.Target)); }
            catch (Exception error)
            {
                throw new UiBackendException(ErrorCode.InternalError,
                    "The original Play submit did not return normally. Verify the game state before trying again. " + error.Message);
            }
        });

        OfficialPlaySubmitRoute ReadRoute()
        {
            if (_disposed) throw new UiBackendException(ErrorCode.ScopeExpired, "The native UI backend has expired.");
            if (_focusSource().Any(x => x.Modal || x.TextEditing)
                || _api.Unbox<bool>(_api.CallStatic(metadata.TouchBlocker, "get_Blocked"))
                || _api.Unbox<bool>(_api.CallStatic(metadata.Window, "get_HasActiveWindow"))
                || _api.Read<bool>(card.Target, "onPlay")
                || _api.Unbox<bool>(_api.Call(loop.Target, "get_Dragging")))
                throw UnavailableOfficialChart("Game or framework input is blocked, or the current card is busy.");

            using var manager = HoldOfficialObject(_api.ReadStatic<IntPtr>(metadata.GameManager, "Instance"), "game manager",
                metadata.GameManager, unity: true);
            using var transition = HoldOfficialObject(_api.Read<IntPtr>(manager.Target, "sceneManager"), "game scene manager",
                metadata.SceneManager, unity: true);
            if (_api.Read<bool>(transition.Target, "_lock"))
                throw UnavailableOfficialChart("The game scene transition lock is held.");

            using var currentEvents = HoldOfficialObject(_api.CallStatic(_eventSystem, "get_current"), "current EventSystem",
                _eventSystem, unity: true);
            if (!_api.Unbox<bool>(_api.Call(currentEvents.Target, "get_isActiveAndEnabled"))
                || !_api.Unbox<bool>(_api.Call(currentEvents.Target, "get_isFocused"))
                || !_api.Unbox<bool>(_api.Call(currentEvents.Target, "get_sendNavigationEvents"))
                || _api.Unbox<bool>(_api.Call(currentEvents.Target, "get_alreadySelecting")))
                throw UnavailableOfficialChart("The current EventSystem cannot submit to its selection.");

            using var module = HoldOfficialObject(_api.Call(currentEvents.Target, "get_currentInputModule"), "current InputSystem UI module",
                metadata.InputModule, unity: true);
            if (!_api.Unbox<bool>(_api.Call(module.Target, "get_isActiveAndEnabled"))
                || _api.Call(module.Target, "get_eventSystem") != currentEvents.Target)
                throw UnavailableOfficialChart("The current InputSystem UI module cannot submit for this EventSystem.");

            using var submitReference = HoldOfficialObject(_api.Call(module.Target, "get_submit"), "stock Submit action reference",
                metadata.ActionReference, unity: true);
            using var action = HoldOfficialObject(_api.Call(submitReference.Target, "get_action"), "stock Submit action", metadata.SubmitAction);
            if (!_api.Unbox<bool>(_api.Call(action.Target, "get_enabled")))
                throw UnavailableOfficialChart("The game's Submit action is disabled.");

            var focus = new OfficialButtonFocus(currentEvents.Target, _api.Call(currentEvents.Target, "get_currentSelectedGameObject"));
            var selected = RequireOfficialPlaySubmitButton(focus, readButton(NormalPlayPath), readButton(DiscOPlayPath));
            using var selectedButton = HoldOfficialObject(selected.Button, "selected stock Button", metadata.Button, unity: true);
            using var buttonObject = HoldOfficialObject(selected.GameObject, "selected stock Play object", metadata.GameObject, unity: true);
            // The public GetComponents(Type) overload is stripped from this player.
            // Reject other EventSystem handlers, including handlers that can consume update-selected before Submit.
            using var handlers = HoldOfficialObject(_api.Invoke(metadata.GetComponents, buttonObject.Target,
                A.Ref(_api.Type(metadata.EventHandler)), A.Value(false), A.Value(false), A.Value(true), A.Value(false), A.Ref(IntPtr.Zero)),
                "current Play event handler array");
            var recipients = _api.ArrayItems(handlers.Target).Take(2).ToArray();
            if (recipients.Length != 1 || recipients[0] != selectedButton.Target)
                throw UnavailableOfficialChart("The selected Play object must have only the stock Button EventSystem handler.");

            using var click = HoldOfficialObject(_api.Read<IntPtr>(selectedButton.Target, "m_OnClick"), "stock Play event",
                _officialPlayMetadata!.ClickEvent);
            using var calls = HoldOfficialObject(_api.Read<IntPtr>(click.Target, "m_Calls"), "stock Play invokable calls", metadata.InvokableCalls);
            using var runtimeCalls = HoldOfficialObject(_api.Read<IntPtr>(calls.Target, "m_RuntimeCalls"), "stock Play runtime listeners",
                metadata.RuntimeCalls);
            if (_api.Read<int>(runtimeCalls.Target, "_size") != 0)
                throw UnavailableOfficialChart("The stock Play event has additional runtime listeners.");

            if (_api.CallStatic(_eventSystem, "get_current") != currentEvents.Target
                || _api.Call(currentEvents.Target, "get_currentInputModule") != module.Target
                || _api.Call(currentEvents.Target, "get_currentSelectedGameObject") != selected.GameObject
                || _api.Call(module.Target, "get_submit") != submitReference.Target
                || _api.Call(submitReference.Target, "get_action") != action.Target)
                throw new UiBackendException(ErrorCode.TargetExpired, "The current stock Submit route changed during verification.");
            return new(focus, selected, module.Target, submitReference.Target, action.Target, click.Target);
        }
    }

    internal static OfficialPlayButtonObservation RequireOfficialPlaySubmitButton(OfficialButtonFocus focus,
        OfficialPlayButtonObservation? normal, OfficialPlayButtonObservation? discO)
    {
        if (normal.HasValue == discO.HasValue)
            throw UnavailableOfficialChart("The current card must have one uniquely attributable stock Play button.");
        var button = (normal ?? discO)!.Value;
        if (focus.EventSystem == IntPtr.Zero || focus.SelectedGameObject == IntPtr.Zero || button.Button == IntPtr.Zero
            || button.GameObject != focus.SelectedGameObject || !button.Active || !button.Interactable)
            throw UnavailableOfficialChart("The current EventSystem must already select the active, interactable stock Play button.");
        return button;
    }

    internal static void CompleteOfficialPlaySubmit(OfficialPlaySubmitRoute captured, Func<OfficialPlaySubmitRoute> readRoute,
        Action verifyChart, Action submit)
    {
        if (readRoute() != captured)
            throw new UiBackendException(ErrorCode.TargetExpired, "The current stock Submit route changed before dispatch.");
        verifyChart();
        submit();
    }

    private sealed class OfficialPlaySubmitMetadata
    {
        internal readonly IntPtr Button, EventData, OnSubmit, InputModule, ActionReference, SubmitAction, TouchBlocker, Window,
            GameManager, SceneManager, GameObject, EventHandler, GetComponents, InvokableCalls, RuntimeCalls;

        internal OfficialPlaySubmitMetadata(NativeUiInputBackend owner)
        {
            var api = owner._api;
            IntPtr UiType(string name) => owner.C("UnityEngine.UI.dll", "UnityEngine.EventSystems", name);
            IntPtr GameType(string name) => owner.C("Assembly-CSharp.dll", "", name);
            IntPtr InputType(string name) => owner.C("Unity.InputSystem.dll", "UnityEngine.InputSystem", name);
            void BoolGetter(IntPtr cls, string getter, bool isStatic = false)
                => owner.RequireMethod(cls, getter, isStatic, "System.Boolean");

            Button = owner.C("UnityEngine.UI.dll", "UnityEngine.UI", "Button");
            EventData = UiType("BaseEventData");
            owner.RequireMethod(EventData, ".ctor", false, "System.Void", EventSystemType);
            owner.RequireMethod(Button, "OnSubmit", false, "System.Void", EventDataType);
            OnSubmit = api.Method(Button, "OnSubmit", 1, false, new[] { EventDataType });
            foreach (var getter in new[] { "get_isActiveAndEnabled", "get_isFocused", "get_sendNavigationEvents", "get_alreadySelecting" })
                BoolGetter(owner._eventSystem, getter);
            owner.RequireMethod(owner._eventSystem, "get_currentInputModule", false, "UnityEngine.EventSystems.BaseInputModule");
            owner.RequireMethod(owner._eventSystem, "get_currentSelectedGameObject", false, "UnityEngine.GameObject");

            InputModule = owner.C("Unity.InputSystem.dll", "UnityEngine.InputSystem.UI", "InputSystemUIInputModule");
            ActionReference = InputType("InputActionReference"); SubmitAction = InputType("InputAction");
            BoolGetter(InputModule, "get_isActiveAndEnabled");
            owner.RequireMethod(InputModule, "get_eventSystem", false, EventSystemType);
            owner.RequireMethod(InputModule, "get_submit", false, ActionReferenceType);
            owner.RequireMethod(ActionReference, "get_action", false, ActionType);
            BoolGetter(SubmitAction, "get_enabled");

            TouchBlocker = GameType("TouchBlocker"); Window = GameType("WindowControl");
            BoolGetter(TouchBlocker, "get_Blocked", isStatic: true); BoolGetter(Window, "get_HasActiveWindow", isStatic: true);
            GameManager = GameType("GameManager"); SceneManager = owner.C("Assembly-CSharp.dll", "Rizline", "SceneManager");
            owner.RequireField(GameManager, "Instance", "GameManager");
            owner.RequireField(GameManager, "sceneManager", "Rizline.SceneManager");
            owner.RequireField(SceneManager, "_lock", "System.Boolean");
            owner.RequireField(owner._officialChartMetadata!.Card, "onPlay", "System.Boolean");
            BoolGetter(owner._officialChartMetadata.Loop, "get_Dragging");

            GameObject = owner.C("UnityEngine.CoreModule.dll", "UnityEngine", "GameObject");
            EventHandler = UiType("IEventSystemHandler");
            var getComponentsParameters = new[] { "System.Type", "System.Boolean", "System.Boolean",
                "System.Boolean", "System.Boolean", "System.Object" };
            owner.RequireMethod(GameObject, "GetComponentsInternal", false, "System.Array", getComponentsParameters);
            GetComponents = api.Method(GameObject, "GetComponentsInternal", getComponentsParameters.Length, false, getComponentsParameters);

            var eventBase = owner.C("UnityEngine.CoreModule.dll", "UnityEngine.Events", "UnityEventBase");
            InvokableCalls = owner.C("UnityEngine.CoreModule.dll", "UnityEngine.Events", "InvokableCallList");
            api.RequireInstanceFieldLayout(eventBase, "m_Calls", "UnityEngine.Events.InvokableCallList", 0x10);
            const string runtimeListType = "System.Collections.Generic.List<UnityEngine.Events.BaseInvokableCall>";
            api.RequireInstanceFieldLayout(InvokableCalls, "m_RuntimeCalls", runtimeListType, 0x18);
            RuntimeCalls = api.InspectMetadataFieldClass(InvokableCalls, "m_RuntimeCalls", runtimeListType).FieldClass.Class;
            api.RequireInstanceFieldLayout(RuntimeCalls, "_size", "System.Int32", 0x18);
        }
    }
}
