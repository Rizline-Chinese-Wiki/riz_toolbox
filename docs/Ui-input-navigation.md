# UI、输入与菜单导航底层

2026-10-21 源码更新：采集按键时和 Host 派发时都核对焦点、模态与文本编辑，关闭弹窗或失焦不重放先前被挡住的快捷键；每个回调前还复核动作代次，阻止回调内重绑接收别人的旧按键。见[回归与限制](validation/input-focus-capture-20261021/README.md)。这批变更尚无新包实机证据；以下 644 实体 F8 仍只属于当时的包。

B14 提供三个实验契约和实际原生 backend，旧 `IUiService` 不变。最新定向检查 **70/70**，包含新增手柄数字输入路径的11项回归，见[范围和证据](validation/gamepad-input-20261021/README.md)。[644包实体F8证据](validation/runtime-input-capture-fix-20261020/physical-key-proof.json)只验证当时真实输入更新后采集、Host帧排空、插件回调各一次。队列按invocation/updateCount去重，只复制已注册动作，采集和派发均核对焦点/上下文；溢出明确撤回采集。此前overlay、输入框、自有按钮和原菜单导航证据保留。原始Mouse.current被游戏禁用时报告SymbolVerified并跳过控件读取，自有UGUI按钮独立工作。新手柄硬件、双插件实际焦点/挂载、手动更新和完整场景矩阵仍缺，B14未完成。

## 创建自己的窗口和控件

current 手柄身份切换的源码补充：设备 A 直接切换 B 时也撤销 A 的旧 queued/drained epoch；按运行时 deviceId/InvalidDeviceId 精确验证，不依赖中间出现 null。73/73专项与实机限制见[设备身份回归](validation/gamepad-device-identity-20261021/README.md)。

插件只引用 [SDK 契约](../src/RizToolbox.Sdk/UiInputContracts.cs)。在已启用插件的 Unity 主线程取得服务；窗口、控件和输入动作随服务所在启用/子作用域释放，无须引用 Core、Adapter 或 Unity 程序集。

```csharp
var lookup = context.GetService<IOwnedUiService>(UiInputCapabilityIds.Windows);
if (!lookup.Available) return;
var created = lookup.Service!.CreateWindow(new(
    "tools", UiMountIds.Overlay, "My tools",
    new UiLayout(new UiRect(40, 40, 520, 280))));
if (!created.Success) {
    context.Log.Log(LogLevel.Warning, created.Error!.Message);
    return;
}
var window = created.Value!;
var label = window.AddLabel(new("status", "Ready", new(12, 54, 480, 36)));
window.AddButton(new("refresh", "Refresh", new(12, 104, 200, 44)), () => {
    label.Value?.SetText("Clicked");
});
window.AddTextField(new("search", "", new(12, 168, 480, 44)), text => {
    context.Log.Log(LogLevel.Debug, "Search text length: " + text.Length);
}, characterLimit: 256);
```

窗口 ID 在插件内唯一；控件 ID 在窗口内唯一。ID 允许 1–96 个 ASCII 字母、数字、点、短横线或下划线。宿主把窗口/动作 ID 限定为 `pluginId/localId`；两个插件可以同时使用 `tools`、`search` 等同名局部 ID。相同插件不同子作用域不能同时占用同一个窗口/动作 ID。窗口动作只能引用同一个插件上下文中的窗口，不能借相同 PluginId 跨子作用域引用别人的窗口。

控件位置相对于窗口左上角；内建标题占据窗口内 `x=12, y=8, height=32` 的区域，作者应从 `y>=48` 安排内容。`SetText` 是程序赋值，不触发 `changed`；原生输入框文本改变在下一个宿主帧采样，同一帧的多个字符变化可能合并为一次回调。文本上限 4096 个 UTF-16 code unit，输入框还受其 `characterLimit` 限制。

按钮创建原生 UGUI `Button`。已知构建中，框架只观察自有、仍存活的 `Button.Press` 在原调用成功返回后的激活，再由宿主主线程消费有界队列；每个控件每帧最多 8 次，超额丢弃。每次回调前重新检查窗口、作用域和模态焦点；失效或被遮挡的队列被丢弃，不在恢复后重放。该按钮不再走原始鼠标几何回调，避免双发。静态证据表明 PointerClick 和 Submit 共用该入口，但实际设备支持仍独立报告，不据此宣称手柄或全部键盘映射已验证。插件不取得原生指针或 UnityEvent 委托。

## 选择挂载点

`GetMounts()` 报告当前可用性、失效代次、所有权和失败原因。创建时重新解析，不能把旧查询当作持续有效的目标。

| ID | 实际路径 | 可用条件与所有权 |
|---|---|---|
| `framework.overlay` | 框架创建 `RizToolbox.OwnedOverlay` Canvas、GraphicRaycaster，ScreenSpaceOverlay、sortingOrder 31000 | 只拥有自己创建的 GameObject；窗口始终随场景边界销毁，Canvas 本身存活到模块释放 |
| `game.main-menu.canvas` | `GameManager.Instance.mainMenuControl.mainButtonCanvasGroup.GetComponentInParent(typeof(Canvas))` | 当前场景必须是 MainMenu，原始组件和 Canvas 有效、活跃且场景已加载；Canvas 须为屏幕空间并已有 GraphicRaycaster；只创建/销毁后代，不销毁原始 Canvas |

菜单 Canvas 通过游戏字段角色取得，既不全场景选第一个同名对象，也不是把第二个框架面板冒充另一个游戏挂载点。零候选返回 `TargetMissing`；类型/Canvas 模式不符返回 `UnsupportedSignature`。当前单一字段链本身不产生多个候选；未知挂载 ID 返回 `OperationUnsupported`。旧的 `UnityUiService` 自有 Canvas 不作为第二个游戏挂载点。

模态窗口仅支持 `framework.overlay`，确保它的原生 blocker 位于两个支持挂载点之上；在菜单挂载点请求 Modal 返回 `OperationUnsupported`。菜单普通窗口可以与 overlay 窗口并存。原生按钮/输入框要求游戏已有活动 EventSystem 和 currentInputModule；未准备时返回 `NotReady`，模块不创建或替换游戏输入系统。

`UiTheme` 目前允许 `LegacyRuntime.ttf` 或 `Arial.ttf` 内建字体名、8–96 的参考字号，以及可空 RGBA 前景/背景引用值。颜色须为有限的 `[0,1]`。字体实际加载失败会拒绝创建；自定义字体文件、外部视觉租约和 AssetBundle 尚未接入本服务。

## 坐标与缩放

| 空间 | 单位、原点与范围 |
|---|---|
| 屏幕 | 物理像素；左下角原点；`Screen.width/height` 和 Canvas.pixelRect 定义当前显示区域 |
| UI reference | 默认 1920×1080 参考像素；左上角原点、Y 向下；可显式设置 64–16384 的参考宽高 |
| 游戏/世界 | 未接入转换，不把 UI reference 坐标当作玩法或世界坐标 |

每个窗口使用 uniform fit：`scale=min(pixelWidth/referenceWidth,pixelHeight/referenceHeight)`，剩余区域居中留黑。`UiToScreen` / `ScreenToUi` 都以整个 reference 区域为坐标域，不是相对窗口；控件 Rect 才相对窗口。落在 letterbox 外缘或参考范围外的点返回 `InvalidArgument`。`GetViewport()` 返回实际比例、pixelRect、留白、分辨率，以及可空的 DPI；DPI 为 0/无效时保留 null，不假装获得显示器缩放系数，也不做未经验证的自动 DPI 补偿。

适配器把参考单位换成 Canvas RectTransform 的实际单位，并在布局或分辨率改变时更新位置、尺寸与字号；未改变的几何不逐帧重复设置全部控件。Canvas rect 与 pixelRect 宽高比不匹配、WorldSpace Canvas 均拒绝。点击命中使用 `RectTransformUtility.RectangleContainsScreenPoint(RectTransform,Vector2,Camera)`，相机来自当前 Canvas。非默认显示器、多显示器及特殊旋转/相机配置没有实机证据，仍属未验收范围。

## 登记动作、重绑和查看冲突归属

```csharp
var input = context.GetService<IInputActionService>(UiInputCapabilityIds.Actions);
if (input.Available) {
    var action = input.Service!.Register(new(
        "refresh", new InputBinding(InputDeviceKind.Keyboard, "F7"),
        InputActionContext.Menu, WindowId: "tools"), _ => {
            label.Value?.SetText("Shortcut");
        });
    if (action.Success)
        action.Value!.Rebind(new(InputDeviceKind.Keyboard, "F8"));
}
```

| 设备 | 已实现的独立探测/映射 | 未支持 |
|---|---|---|
| Keyboard | `Keyboard.current` 的具名 KeyControl getter 与 `wasPressedThisFrame`；A–Z、Alpha0–9、F1–F12、四方向、Escape、Tab、Return、Space、Backspace | 修饰键组合、连续 held/repeat、原始注入 |
| Mouse | `Mouse.current`、position control 的静态 `InputControlExtensions.ReadValueAsObject`、Left/Right/Middle down；按钮另采样 left-up | 滚轮、额外按键、鼠标注入 |
| Gamepad | 精确符号通过后 `SymbolVerified`；当前设备存在、added/enabled且完整数字采集成功后 `RuntimeAvailable` | 16个数字按钮，缺失/禁用/故障独立失效并重试；尚无真实手柄验收 |

手柄 `InputBinding` 使用位置名 `ButtonSouth/East/West/North`、`Left/RightShoulder`、`Left/RightTrigger`、`Left/RightStickButton`、`Start`、`Select` 和 `DpadUp/Down/Left/Right`，区分大小写。Trigger 是 Input System 按压阈值的数字边沿；不提供轴值、震动、多玩家设备选择或模拟输入。采集期间持有 current 设备与控件根，读前读后核对 added/enabled。设备丢失时旧手柄队列及代次失效，键鼠队列保留；重新出现后的新边沿可继续已登记动作。

`RuntimeAvailable` 只表示该进程中的 API 实际调用通过且有当前设备，不等于完整设备验收或任意游戏控制已接入。本轮实测发现旧 `Input.GetKeyDown` 调用抛出原生异常；后端已改用游戏实际采用的 Input System。键盘以精确具名 getter 取得 KeyControl，不使用枚举整数；鼠标通过 `Mouse.current` 的按钮边沿和 position control 的 `ReadValueAsObject` 取得经 ABI 验证的 Vector2，不猜状态缓冲区偏移。设备缺失保留探测状态，单个设备签名/调用失败独立标为不可用。

Global 在任何场景匹配；Menu 目前仅 MainMenu/LevelList；Gameplay 目前仅 Level。未识别场景只匹配 Global。同一绑定在上下文重叠时拒绝；Menu 与 Gameplay 可共存，Global 与两者均冲突。不根据注册顺序让一个插件静默夺走另一个插件的键。错误为 `Conflict`，消息列出所有已有 `PluginId:ActionId`；`GetBindings()` 也提供完整归属快照。重绑先校验再替换，失败保留旧绑定。绑定只在本次作用域有效，不自动写配置文件。

焦点按窗口栈保存：新模态入栈，关闭后回到上一有效窗口，移除低层窗口不抢高层焦点。绑定 WindowId 的动作仅在该窗口位于焦点顶部时分发；有模态时只允许该模态自己的窗口动作，Global 也被阻止。任意本框架文本输入焦点，或当前原生 EventSystem 选中且处于编辑状态的 UGUI/TMP 输入框，都会阻止本 broker 的快捷键。一次回调开启模态或停用自身后，后续动作会重新检查焦点与作用域。鼠标位于 owned UI 时不再分发 broker 鼠标快捷键。

**拦截范围是框架 broker。** 它不 Hook/消费游戏自己的 Legacy Input、InputSystem、原生键盘/手柄逻辑；游戏自己的轮询快捷键和原生弹层覆盖没有经过完整验证。原生 blocker 可参加 UGUI raycast，但不能据此宣称游戏输入已被完整拦截。按钮的语义激活遵循原生 UGUI 流程，框架自己的模态限制在派发前再次检查；游戏其他弹层之间的覆盖关系仍须实机检查。

## 调用游戏原始菜单导航

主菜单命令 `game.main-menu.activate-selected` 使用下列游戏调用链。

```text
GameManager.Instance : GameManager [static field]
  -> mainMenuControl : MainMenuControl [instance field]
  -> EnterCurrentButton() : System.Void [instance method]
```

它激活游戏当前选中的原始主菜单项，不自行指定页面字符串、改选中索引或略过原游戏流程。`GameManager` 本身没有从现有 dump 得到直接页面方法；`Rizline.SceneManager.StartNextScene` 虽在元数据中出现，但其字符串格式/前置流程未验证，因此没有发布任意页面切换 API。

`GetTargets()` 返回精确方法与前置：当前 MainMenu 场景；MainMenuControl 有效、活跃且场景已加载；`Working=true`、`allowMove=true`、`opLock=false`；`GameManager.Instance.sceneManager._lock=false`；没有框架模态/文本焦点。每次 Navigate 都重查。条件不满足返回 `NavigationUnavailable`；未知目标返回 `OperationUnsupported`。原始游戏方法正常返回只代表命令调用被接受，异步目标页是否打开必须另观察，不会在 Result 中伪造页面完成事件。该命令是单向操作，没有返回假的 Dispose 回滚租约。

### 提交当前官方 Play 按钮

`riz.navigation.menu` 0.2 的 `IGameNavigationService.GetTargets()` 枚举 `game.level-list.submit-selected-play`。插件在主线程收到玩家的明确操作后，可用同一 ID 调用 `Navigate`。此命令通过宿主的目标列表区分，使用已有 SDK 1.4.0 导航接口。r23 的只读选曲版本没有这个目标。

当前正式 `LevelList` 中，EventSystem 必须已经选中当前卡片上唯一、可交互且活跃的原版 Play 按钮。宿主核对原有 Submit 动作、持久监听及游戏窗口、触摸和转场锁，随后在同一次调用中复核目标和谱面，再提交给该按钮的原版 `Button.OnSubmit`。目标不就绪返回 `NavigationUnavailable`，目标在核对期间变化可能返回 `TargetExpired`。 [Unity Button](https://github.com/Unity-Technologies/uGUI/blob/main/com.unity.ugui/Runtime/UGUI/UI/Core/Button.cs) · [Unity InputSystem Submit](https://github.com/Unity-Technologies/InputSystem/blob/1.7.0/Packages/com.unity.inputsystem/InputSystem/Plugins/UI/InputSystemUIInputModule.cs)

成功返回表示原版 Submit 正常返回。通过后续 `RunStarted.ChartId` 确认实际开局和谱面。Steam 上的原版焦点、回调与开局仍待同一构建实测。

## 读取选曲页的当前官方谱面

SDK 1.4.0 的实验服务 `riz.navigation.official-chart-selection` 在 Steam Windows x64 `25977441` 提供按需读取。

```csharp
var selection = context.GetService<IOfficialChartSelectionService>(UiInputCapabilityIds.OfficialChartSelection);
var current = selection.Service?.ReadCurrent();
```

`ReadCurrent()` 需要在 Unity 主线程调用。正式 `LevelList` 页的当前卡片和谱面选项稳定时，读取返回官方 `ChartId`、场景句柄和 List/Card 实例标识。鼠标浏览和 EventSystem 焦点不影响谱面读取。场景不符、没有有效选谱或列表仍在换碟时返回 `NavigationUnavailable`，采样期间卡片或谱面身份变化可返回 `TargetExpired`。

`PlayButton` 单独返回。当前 EventSystem 可用，且能从卡片的两个原版路径唯一核实活动分区上的 Play 按钮及其 `PlayLevel` 持久监听器时，快照包含按钮实例标识和 `Active`、`Interactable`、`Focused`。`Focused` 表示 EventSystem 当前选中该按钮的 GameObject，未聚焦的已核实按钮仍可读。按钮缺失、分区未激活或 CanvasGroup 隐藏、路径冲突、监听器不符或 EventSystem 不可用时，`PlayButton` 为空，已核实的 `ChartId` 仍可读。

按钮读数用于观察 UGUI 状态。它们不证明射线可达、窗口无遮挡、难度解锁或关卡可启动。此接口不选择谱面或触发回调，宿主也不会在每帧扫描选曲页。 [已支持的原版导航命令](#调用游戏原始菜单导航)

## 失效、清理与预算

活动场景 handle/name 身份改变（即使名字相同但 handle 不同）会销毁两类挂载点上的全部窗口和焦点，绑定旧窗口的动作随之失效。原始菜单对象销毁后不复用旧窗口句柄。插件/子作用域失效先阻止回调，随后逆序释放动作与窗口；控件回调、焦点、原生根和 GameObject 同时撤销。销毁前对自有 GameObject 执行 `SetActive(false)`，避免等待 Unity 延迟 Destroy 时继续遮挡。只在当前 EventSystem 选中对象仍属于本窗口时还原仍存活的前一个选择；嵌套窗口中移除低层窗口会转交其恢复链。原生恢复链尚待实机验证。

失败的动作回调被移除，失败的按钮/文本回调被隔离，并记录带插件/窗口 ID 的日志。一个资源或日志 sink 抛错不阻止其他资源清理。窗口上限为全局 64、每插件 16；每窗口 128 个控件；动作上限全局 512、每插件 128；超限返回 `QuotaExceeded`。离线 100 轮创建/释放检查验证 View 不保留已释放窗口/动作；它不是原生 GC、显存或帧时间压力验收。

## 宿主接入与验证

[UiInputModule](../src/RizToolbox.Adapter/UiInput/UiInputModule.cs) 是独立 `IAdapterModule`：

```csharp
RegisterModule(new UiInputModule(api, main, log, events));
```

模块登记自有 UI、输入动作、菜单导航和官方谱面读取四个 experimental 0.1 scoped 服务。module 自己订阅一次宿主 FrameUpdate；ModuleManager 的一秒探测周期不能充当输入帧周期。Release 移除订阅并清理服务。新增 UI 只修改自有对象，借用游戏 Canvas 不通过 B08 修改原始属性；菜单命令则明确标为单向操作。旧 IUiService 和旧 panel 纹理能力继续走原路径。

```powershell
dotnet run --project tests/RizToolbox.UiInput.Tests/RizToolbox.UiInput.Tests.csproj -c Release
```

2026-10-20：**39/39 通过**。检查使用真正的 UiInputService、InputActionBroker、PluginHost 和 scoped DispatchProxy；仅 native backend 为 fake。覆盖双插件局部 ID、双挂载点的可用/缺失拒绝、独立设备拒绝、冲突所有者、重绑原子性、嵌套焦点和文本抑制、回调重入/故障、鼠标窗口层级、场景和对象重建、清理异常、重复释放、坐标/布局、模块帧订阅，以及精确 Vector3 ABI 和任意同形结构拒绝。新增 6 项覆盖原生激活队列的一次性消费、几何去重、旧窗口/场景隔离、模态丢弃、回调中打开模态及队列上限。

元数据候选、工具限制及 Evidence→Finding→Path 见 [初次实现证据](validation/ui-input-20261020/metadata-evidence.md)及[原生按钮静态证据](validation/runtime-unified-20261020/owned-button-static-proof.json)。最新实机证据单独绑定包清单。剩余真实验收包括两个插件在 actual menu Canvas 与 overlay 上共存、焦点归还、游戏原生弹层与所有独立设备范围、分辨率/DPI、20 轮场景与启停资源趋势。没有完整证据，不勾选 B14。
