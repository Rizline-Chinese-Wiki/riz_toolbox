# B14 元数据与离线验证记录

范围见 [scope](scope.md)。这是基于既有 dump 的候选定位与可执行运行时拒绝设计，不是完成了 IDA 反编译/动态游戏验证的报告。本机工具索引标记 ida、idalib-mcp、ida-pro-mcp 缺失；本轮 `Get-Command ida,idalib-mcp,ida-pro-mcp -ErrorAction SilentlyContinue` 无结果，未自举或启动 GUI。

## Evidence

### E-imports-metadata

- observed_at: 2026-10-20
- source_type: file
- source_ref: `C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs`
- content_hash: `092CDB96914CCB7AF3F31D9D2E703E00DD6DDFF63FD559F11AC6B42E6635C1CB`
- artifact_path: external existing dump; project-local extracts below
- repro_command: `Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs'`
- raw_excerpt: 游戏类型引用 UnityEngine.UI、UnityEngine.EventSystems、UnityEngine.Canvas；GameManager 引用 MainMenuControl 和 Rizline.SceneManager。
- linked_workitem: B14
- supersedes: none

这是本任务使用的托管/IL2CPP 元数据引用摘要等价锚点。没有读取 PE IAT/exports，未完成二进制 survey，不能宣称从机器码证实了方法体、调用先后、解锁或场景转移逻辑。

### E-game-symbols

- observed_at: 2026-10-20
- source_type: file
- source_ref: `dump.cs` 91489（GameManager）、97003（MainMenuControl）、97136（MainPageButton）、98220（Rizline.SceneManager）附近
- artifact_path: [game-metadata.txt](game-metadata.txt)
- content_hash: `93CFA83D27CC361A7F8C820D8A265C03B201FEFA76C0F1417B35D732C9189C47`
- repro_command: `rg -n -A 34 '^public class GameManager :|^public class SceneManager :|^public class MainMenuControl :|^public class MainPageButton$' 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs'`
- raw_excerpt: `GameManager.mainMenuControl : MainMenuControl`；`MainMenuControl.mainButtonCanvasGroup : UnityEngine.CanvasGroup`；`EnterCurrentButton():System.Void`；`Working` getter、`allowMove`、`opLock`；`Rizline.SceneManager._lock : System.Boolean`。
- linked_workitem: B14
- supersedes: none

### E-ui-symbols

- observed_at: 2026-10-20
- source_type: file
- source_ref: `dump.cs` 132143（InputField）、134448（EventSystem）、156926（RectTransformUtility）、156976（Canvas）、157739（Input）附近
- artifact_path: [ui-input-metadata.txt](ui-input-metadata.txt)
- content_hash: `9046A1196542325A4FE24CA8E4F916D2F4115E3B1E3DE635DCA73C57974B0040`
- repro_command: `rg -n '^public class Canvas :|^public class Input$|^public class InputField :|^public class EventSystem :|RectangleContainsScreenPoint\(|GetKeyDown\(|GetMouseButtonDown\(|get_mousePosition\(|EnterCurrentButton\(|get_Working\(|get_isFocused\(' 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs'`
- raw_excerpt: `RectangleContainsScreenPoint(RectTransform,Vector2,Camera):Boolean`；`Input.GetKeyDown(KeyCode):Boolean`；`Input.get_mousePosition():Vector3`；`InputField.ActivateInputField():Void`。
- linked_workitem: B14
- supersedes: none

静态性在该 dump 文本中不完整，不能从没有 `static` 字样推导为实例方法。实现由 `NativeRuntime.Method` 按参数类型、静态性、返回元数据逐次校验，ABI 校验后才 runtime_invoke；根句柄跨调用保留。Vector3 仅接受 Adapter 的 `NativeVector3` 和精确 x/y/z Single 布局，不能用任意同形 Marshal 结构替换。

### E-offline

- observed_at: 2026-10-20
- source_type: command
- source_ref: `tests/RizToolbox.UiInput.Tests/Program.cs`
- repro_command: `dotnet run --project tests/RizToolbox.UiInput.Tests/RizToolbox.UiInput.Tests.csproj -c Release`
- raw_excerpt: `UI/input checks: 33/33 passed.`；编译无 warning/error。
- linked_workitem: B14
- supersedes: 初次 30/31 检查中，fake 输入帧没有清空旧 down edge，导致重复派发；fake Capture 已改成逐帧消费，后续检查全部通过。没有用该 fake 修复宣称原生 Unity 行为已验证。

## Findings

### F-mount-candidates

- severity: n/a_re
- category: other
- status: candidate
- evidence_ids: [E-imports-metadata, E-game-symbols, E-ui-symbols]
- location: `NativeUiInputBackend.MenuCanvas`
- confidence: medium
- impact: 提供框架 overlay 与通过真实字段链解析的主菜单 Canvas 两条不同挂载路径；不会按名字选首个对象。实际 Canvas 模式、激活时机及渲染交互尚需游戏验证。
- remediation: 由宿主运行 manual matrix，保存两个挂载点成功、切场景撤销和失败原因；保持 B14 未验收。

### F-navigation-candidate

- severity: n/a_re
- category: other
- status: candidate
- evidence_ids: [E-game-symbols]
- location: `GameManager.Instance.mainMenuControl.EnterCurrentButton`
- confidence: medium
- impact: 已实现精确原方法调用与前置拒绝；未推断或实现任意页面字符串/索引跳转。方法内部流程、异步目的页是否完成没有证据。
- remediation: 在已选中安全菜单项的 MainMenu 状态执行一次，记录命令结果和后续真实场景；完成前只列 experimental。

### F-service-path

- severity: n/a_re
- category: other
- status: validated
- evidence_ids: [E-offline]
- location: `UiInputService`、`InputActionBroker`、`UiCoordinates`、Vector3 ABI
- confidence: high
- impact: 共享生产服务层的 scoped 所有权、冲突、焦点和失效/清理路径可离线执行；验证范围不包含 native backend 的游戏行为。
- remediation: 保留离线回归，真实设备/UI 和导航证据分开收集。

## Paths

### P-owned-ui

- path_type: callflow
- start: PluginScope.GetService(IOwnedUiService)
- goal: 创建/释放归属明确的 UGUI 后代
- steps: scoped UiInputService.View → local ID/布局/主题/配额校验 → mount 运行时解析 → native window/control roots → framework focus/action dispatch → scope/scene/target retirement → 先失效回调，再 SetActive(false)/Destroy 和 roots release
- evidence_ids: [E-game-symbols, E-ui-symbols, E-offline]

### P-menu-command

- path_type: callflow
- start: IGameNavigationService.Navigate(game.main-menu.activate-selected)
- goal: 调用一次原始主菜单当前项激活方法
- steps: 检查框架 focus → 实查 MainMenu 和 GameManager/MainMenuControl/SceneManager 前置 → ABI 校验 runtime_invoke EnterCurrentButton → 返回调用接受结果 → 真实异步目的页仍需观察验证
- evidence_ids: [E-game-symbols, E-offline]
