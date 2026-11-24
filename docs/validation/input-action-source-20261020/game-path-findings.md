# Rizline 键盘与输入更新路径：固定构建静态证据

本轮只读分析，不改生产代码、不运行 dotnet、不操作游戏、不读取进程内存。目标为 `D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll`，SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。分析入口遵循已读取的 `ida-reverse/SKILL.md`；当前工具索引中 IDA 本体与 MCP 缺失，沿用上级已采用的本机 pefile / Capstone 与既有 IL2CPP dump。没有宣称完成 IDA survey 或实机输入验收。

复现：

```powershell
& 'C:\Python314\python.exe' 'C:\Users\71957\Desktop\riz_toolbox\docs\validation\input-action-source-20261020\game-path-inspect.py'
```

脚本先锁定 PE 哈希，读取本构建 imports / exports，再解析 dump 元数据与 unwind 范围，保存选定机器码和已验证的直接 call / jmp。产物是 [game-path-static.json](game-path-static.json)。E8 / E9 原始字节候选必须落在 unwind 起点解码得到的指令边界才能计入已验证引用。该集合不覆盖间接调用、内联或所有跨 unwind 分块。

## 已证实事实

| 路径 | 本构建机器码证据 | 能说明的范围 |
|---|---|---|
| 游戏菜单按键 | `InputActionControl.WasPressedThisFrame` `0x372F80` 调 `GetAction`，在 `0x372F99` 跳到 `UnityEngine.InputSystem.InputAction.WasPressedThisFrame` `0x124B8D0` | 菜单快捷键基于生成的 `InputActions`，不是凭 `Legacy Input` 类型存在推断而来。 |
| 实际菜单调用点 | `MainMenuControl.Update` `0x3F189D` / `0x3F18AD`；`GameStart.Update` `0x3E74B0` / `0x3E74C0` 等直接调用上述包装器 | 游戏实际业务代码保留了这条调用链。具体当前 action 绑定、启用和设备仍须运行时读取。 |
| 谱面键盘 | `InLevel.InputControl.UpdateKeyboardInputs` 在 `0x3E998E` / `0x3E9B39` 调 `Keyboard.get_Item`，随后在 `0x3E99A1` / `0x3E9B4C` 调 `ButtonControl.get_isPressed` | 谱面键盘使用新输入系统 Key 控件的按住状态；不等同于框架 F8 的逐帧边沿。 |
| 游戏主动更新输入 | `LowLevelTouchControl.UpdateTouch` `0x3A7690` 在 `0x3A7764` 调 `InputSystem.Update()` `0x129CA60` | 不能假设输入只由 Unity 自动更新一次。是否调用、每帧次数及它相对 Host 的先后尚未动态证实。 |
| 游戏改变更新模式 | `LowLevelTouchControl.OnEnable` `0x3A7674` 写入参数 `3`，`0x3A7680` 跳到 `InputSettings.set_updateMode`；`OnDisable` 对应传入 `1` | 该组件启用 / 停用确实会改变设置。dump enum 成员后的 `0x0` 是偏移，不能当枚举值。 |
| 设置到更新 mask | `InputManager.ApplySettings` 的 `0x12FAF65..0x12FAF87` 将设置整数 `1` / `2` / `3` 对应到 mask 位 `1` / `2` / `0x10`，再并入已有位 | 本构建没有把这三种设置映射成同一个阶段；当前设置未知。 |
| 全局更新序号 | `InputUpdate.OnBeforeUpdate` / `OnUpdate` 对类型 `1`、`2`、`0x10` 更新 `s_PlayerUpdateStepCount` 并写 `s_UpdateStepCount`；`OnBeforeUpdate` 的标记避免同一更新在 `OnUpdate` 再增一次 | 边沿语义跟随 input update 计数，不是简单的 Host 帧号或渲染帧号。 |
| 按钮边沿缓存 | `ButtonControl.wasPressedThisFrame` `0x126B34D` 检查 `needsToCheckFramePress`；已启用缓存的路径在 `0x126B36A` 将 `m_UpdateCountLastPressed` 与 `InputUpdate.s_UpdateStepCount` 比较；第一次读取路径另有当前 / 前次值与 `wasUpdatedThisFrame` 检查 | 不能套用旧版包“单纯当前值减前次值”源码，也不能把设备 non-null 当作这帧成功捕获边沿。完整 getter 跨多个 unwind 范围，另存 metadata bounded span。 |
| action 边沿缓存 | `InputAction.WasPressedThisFrame` 在 `0x124B930` 比较 action trigger 内的计数与同一全局序号，且序号必须非零 | 游戏 action 的边沿也会受另一次 input update 影响。 |
| after-input 候选 | `InputManager.OnUpdate` 在 `0x1300421` 调 `InputUpdate.OnUpdate`，处理事件后在 `0x13010AC` / `0x13010FA` 调 `InvokeAfterUpdateCallback` `0x12FED70`；后者跳过参数 `8`，其它经 `DelegateHelpers.InvokeCallbacksSafe` | 可作为锁定构建的观测 / 采样候选，签名是 instance void + 一个 enum32。不能复用参数数量不同的 hook ABI。 |
| Legacy Input | 当前直接引用扫描只在 `ExampleScene1Controller.Update` / `ExampleScene2Controller.Update` 找到 `Input.GetKeyDown` `0x157DF40` 调用；`UnityEngine.UI`、`InputSystemUIInputModule` 均有代码 | 不能用示例场景的调用证明实际菜单在用 Legacy。静态类型和代码也不能决定当前 EventSystem 采用哪一个模块。 |

`GameManager.Update` 本体调用 `OnUpdate` 委托，然后处理分辨率 / scale；没有直接调用 `InputSystem.Update` 或 `LowLevelTouchControl.UpdateTouch`。本轮没有证明谁把 `UpdateTouch` 接进哪个调度链，因此 **没有把这几个方法强行连成已证实的帧顺序**。dump 中没有名为 `KeyboardChange` 的类型或方法命中；与此接近的已定位业务类是 `InputActionControl`、`InputDeviceDetector` 和 `ShortcutKeyBinding`。

`InputSystem.get_version` `0x129DA80` 会从一项运行时 string literal 构造 `System.Version`。既有 `script.json` 仅含 `ScriptMethod`，本轮未取得该 literal 的文本，故没有猜包版本。方法体结构已足以证明它与旧版简化 getter 存在差异。

## 最小修复方向与验收边界

框架可在严格核对 SHA、元数据、接收者与 ABI 后，在上述 after-input 路径 **只读捕获** 已注册控件的边沿，带 input update 类型、计数与设备身份入有界队列，再在既有 Host 主线程派发一次。保持每次 native original 正常调用一次，不主动调用 `InputSystem.Update`，不改变游戏 updateMode 或启用 / 停用设备。按计数与控件身份去重，禁止把多个固定更新或手动更新当成同一渲染帧的重复噪声丢弃。

采用该方向前，根代理仍需用本包日志实证以下信息：当前 `InputSettings.updateMode`、当前 EventSystem 模块、真实 UI action 的 activeControl/device、每帧 input update 类型 / 计数，以及 Host 采样时的相同计数。静态结果只支持“存在主动更新与更新计数风险”，**不能认定它就是当前 F8 无回调的唯一根因，也不能认定 `Keyboard.current` 就是 UI 当前使用的设备**。

所有结论均为静态研究证据，`runtimeInputVerified=false`。没有修改游戏文件或设置，没有完成新 hook 的实现与实机验证。
