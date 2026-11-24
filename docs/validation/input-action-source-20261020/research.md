# F8 输入来源与更新阶段研究

2026-10-20，flavor = null。范围为已授权的本地 Rizline PC 输入兼容底层；本轮只读本体、源码、日志，写入本验证目录。没有修改设备启用状态、调用输入更新、操作游戏或运行 dotnet。后续实现和实机验收另行记录。

结论：宿主在原版 `GameManager.Update` 返回后采样；游戏存在主动推进 Input System 的业务代码，因此 input update 与 Host FrameUpdate 不能视为同一边界。当前证据支持把原始边沿采集移到实际 input update 完成后，再由 Host 帧分发的修复方向；尚不能证明本次 F8 漏报唯一由更新顺序造成。UI 指针实际来源与 `Mouse.current` 已明确不同。原生 UGUI 按钮应保持独立。

## Evidence

| ID | 来源与复现 | 观察 |
| --- | --- | --- |
| E-identity | [binary-identity.json](binary-identity.json)，`C:/Python314/python.exe docs/validation/input-action-source-20261020/inspect-host-source.py` | GameAssembly SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，Windows x64；保留导入分类和导出表。导入存在不能证明具体输入路由。 |
| E-host | [host-source-evidence.json](host-source-evidence.json)，同上脚本，逐文件 SHA-256 与真实行号 | profile `tickType=gameManager`、`tickMethod=Update`；RuntimeBridge.cs:51 先调用 original，:64 后执行 host Tick；ToolboxHost 在 `adapter.Tick` 后发布 FrameUpdate；UI 模块 FrameUpdate 回调调用 Capture。 |
| E-metadata | [input-metadata.json](input-metadata.json)，同上脚本 | 保留 `InputSystem.settings`、`InputSettings.updateMode`、`InputState.currentUpdateType/updateCount`、`InvokeAfterUpdateCallback` 等实际元数据。dump 的 enum 字段偏移不能当作常量值。 |
| E-game-path | [game-path-static.json](game-path-static.json)，`C:/Python314/python.exe docs/validation/input-action-source-20261020/game-path-inspect.py` | 固定 SHA 下 57 个有界代码区、40 个有效直接引用；详细说明见 [game-path-findings.md](game-path-findings.md)。静态证据明确标记 `runtimeInputVerified=false`。 |
| E-runtime | [runtime-input-evidence.json](runtime-input-evidence.json)，同上 host-source 脚本，原日志冻结为 [runtime-log-frozen.log](runtime-log-frozen.log) | 日志副本 SHA-256 `5a8c787e28ec169c8bac72b72f92857063dc84d242f28097eafcd5c54331c744`。10:39:08 UTC，UI point/leftClick activeControl deviceId=31、native=false、added/enabled=true、lastUpdateTime=106.1708873；Keyboard.current 为 native device1、enabled=true、lastUpdateTime=55.2109066；Mouse.current 为 native device2、enabled=false。没有 ModuleProbe INPUT F8 日志。 |

脚本只写本目录。日志冻结后不自动覆盖；需要另一次会话证据时另建目录。宿主源码哈希仅描述冻结时版本，后续改动不改变这份静态观察。

## Findings

### F-host：采样属于 GameManager.Update 后置回调

- status: validated；confidence: high；evidence_ids: E-host。
- 调用顺序为原版 `GameManager.Update` 返回 → RuntimeBridge → ToolboxHost.Tick → adapter.Tick → FrameUpdate → UiInputService.Tick → NativeUiInputBackend.Capture。
- 这不是 `Awake`，也没有专门的 after-input 采样回调。单纯读取原始 `wasPressedThisFrame` 并不代表 SDK 已建立输入更新与分发的对应关系。

### F-manual：原游戏存在手动 Input System 更新路径

- status: validated（静态存在）；confidence: high；evidence_ids: E-game-path。
- `LowLevelTouchControl.UpdateTouch` 在 `0x3A7764` 调用 `InputSystem.Update()` `0x129CA60`。
- `LowLevelTouchControl.OnEnable` 设 `updateMode=3`，OnDisable 设 `1`；本构建设置路径分别映射更新 mask `0x10` 和 `1`。
- 菜单热键使用 `InputActionControl → InputAction.WasPressedThisFrame`；谱面键盘使用新输入系统 Keyboard/ButtonControl.isPressed。不是“游戏仅用 Legacy Input”的证据。
- GameManager.Update 仅证实调用其 OnUpdate 委托，未建立 UpdateTouch 订阅关系，也未采集本次实机 updateMode/currentUpdateType。因此“这条路径正好覆盖 F8 边沿”仍是 candidate。

### F-source：UI 与原始鼠标不是同一个来源

- status: validated；confidence: high；evidence_ids: E-runtime。
- 同一采样中 UI 实际动作来自 device31，而 Mouse.current 是被禁用的 device2。设备31只证明这条动作的实际 device 身份，不能据此认定其就是某个电脑操作工具生成的设备，也不能把它冒充键盘。
- Keyboard.current 的 enabled/added 只证明可读取；没有本次 F8 控件状态或原始事件证据。日志不连续覆盖所有按键，不能从没有日志反推出没有操作。

### F-after：最小采集位置已有确定的本构建方法

- status: validated（静态调用点）；confidence: high；evidence_ids: E-game-path、E-metadata。
- `InputManager.OnUpdate` 在 `0x13010AC` 和 `0x13010FA` 调 `InvokeAfterUpdateCallback(InputUpdateType)` `0x12FED70`。
- 该目标需要独立的 `instance void(enum32)` ABI。现有零参数 ABI 或 `(enum32,float)` ABI 均不能借用。
- 目标是 after-input 采集的候选；仍须实机对照该 invocation 与 SDK frame，证明 F8 在目标处可见并经正常焦点判定分发。

## Path

P-input（callflow）：本构建游戏输入更新（E-game-path/F-manual） → InputManager.OnUpdate 实际处理 → InvokeAfterUpdateCallback（E-game-path/F-after） → 复制当前已注册动作的原始边沿到有界队列 → GameManager.Update 后 Host FrameUpdate（E-host/F-host）排空队列 → 按当时场景、文本编辑及模态焦点决定 SDK 回调。

建议实现边界：只绑定固定本体 SHA、Unity 2022.3.62f2、Windows x64 和已验证 RVA；after-original 回调不运行插件代码，只存普通数据。以原生 invocation 去重，保留连续多个 input update 的独立边沿；队列有上限，溢出明确诊断并撤回采集，不能静默复用旧输入。设备临时缺失/禁用/Root 失败可恢复，模块撤回清空所有队列和订阅；wrong-thread/reentry 不读取元数据，原调用每次严格一次。保持原生按钮路径独立。

验收必须同时看到确定的 after-input F8 edge 与 Frame callback，记录 invocation、update type、Host 分发顺序；覆盖一 Host 帧间多个 update、短按/重复按、未注册键不采集、禁用恢复、文本编辑/模态抑制、scope 撤回及队列溢出。不能仅凭设备 RuntimeAvailable 或离线回归宣称实机完成。

Unity 官方文档说明默认 Dynamic 在脚本 Update 前处理，Manual 则由调用者决定阶段；额外调用 Update 会推进一次输入状态。这里只用于说明术语，运行时行为以本构建静态路径和实机记录为准：[Input System 更新 API](https://docs.unity.cn/Packages/com.unity.inputsystem@1.7/api/UnityEngine.InputSystem.InputSystem.html)、[UpdateMode](https://docs.unity.cn/Packages/com.unity.inputsystem@1.7/api/UnityEngine.InputSystem.InputSettings.UpdateMode.html)。本构建 ButtonControl 含较新的 frame-press 计数结构，不能直接套用 1.7 的 ButtonControl 实现。

未完成：当前 mode/type 的实机读数、UpdateTouch 实际订阅连接、原始 F8 控件/事件是否进入 device1、after-input 新采集器的实现与实测。没有使用 OS 键盘轮询兜底、开启原游戏禁用的鼠标或主动 InputSystem.Update。
