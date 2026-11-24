# Rizline 暂停控制守卫与原生调用链

`CanDoOperation` 的真实含义是“暂停页可以操作”，公式为 `_pausing && !_over`，不是所有游戏控制的通用许可。将它应用于 Pause 会使正常播放中的暂停被永久拒绝。另一个独立问题是仅调用 `PauseUIControl.Pause()` 不会停止谱面；自然暂停先执行 `ProgressControl.Play(false)`，再打开暂停页。Resume 入口已经包含恢复准备、3 秒冷却和 UI 副作用，不应另行重复调用控制器 Resume。本报告只证明固定版本的静态行为，实际按钮事务仍须由主任务进行实机验收。

范围见 [scope.md](scope.md)。报告类型为普通逆向，`flavor = null`。

## 可复现证据

在仓库根目录运行以下 PowerShell 命令。脚本先验证游戏 SHA，读取 PE 导入/导出表，再从每个已知方法起点解码并断言关键指令；不向游戏进程写入内容。

```powershell
C:\Python314\python.exe docs\validation\gameplay-controls-20261020\static-pause-guard\inspect-pause-guard.py
```

| Evidence | 观察与来源 | 验证范围 |
|---|---|---|
| E-imports / E-exports | `pause-guard-static-proof.json` 中记录 10 个导入 DLL、386 个导出；样本与 dump 哈希、工具版本一并记录 | PE 身份与可复查导入/导出，未据此推断网络或其他行为 |
| E-CanDo | `PauseUIControl.get_CanDoOperation`，RVA `0x3C49D0`，依次读 `_pausing` 的 `0xB4` 与 `_over` 的 `0xC0`；`sete al` 在 `_over==0` 时返回 true | 公式 `_pausing && !_over` |
| E-Pause | `PauseUIControl.Update`，`0x3C42FD → 0x3C4307`，以及触摸路径 `0x3C46CA → 0x3C46D4` | 两条自然路径均 `Play(false) → PauseUI.Pause()` |
| E-Guard | `PauseUIControl.Update` 的 `0x3C3EC9`、`0x3C3EDA`、`0x3C426A`、`0x3C42A2`、`0x3C42AF..0x3C42CF` | 场景时间、terminated、TouchBlocker、活动窗口、over、pauseTime、pausing 的实际分支 |
| E-Resume | `PauseUIControl.Resume`，`0x3C3C95..0x3C3D21` 与 `ProgressControl.Play`，`0x400700..0x4007B5` | 恢复准备并不等于当帧已恢复播放 |
| E-End | `NormalModeInLevelController.Retry`、`Skip`、`LevelOver`、`ControllerOver` | 重试守卫与提前跳过的结束语义 |

上述 Evidence 的完整指令位于 [disassembly.txt](disassembly.txt)，结构化产物位于 [pause-guard-static-proof.json](pause-guard-static-proof.json)。本次产物 SHA-256 为 `214e5be7e916c84e0fb7f1fdd21e1354284d2c279584ad346235c169a6d9b05f`；重跑时 `observedAtUtc` 改变，产物哈希也随之改变。源游戏哈希固定。

## 发现与最小修复

| Finding | Evidence | 结论与处理 |
|---|---|---|
| F-CanDo（static-observed） | E-CanDo、E-End | CanDo 用于 Resume/Retry 合理；不可全局应用于 Pause/Skip。Skip 是否只允许播放状态属于 SDK 的保守策略，原生入口没有该守卫。 |
| F-Pause（static-observed） | E-Pause、E-Guard | common run guard 外，应独立读取实际 PauseUI 存在且 active、`!_over`、`!_terminated`、有限 `_pauseTime<=0`、有限场景时间 `>=1.8f`、`!TouchBlocker.Blocked`、`!WindowControl.HasActiveWindow`。以 `controller.Pause() → PauseUI.Pause()` 完整执行；控制器 Pause 精确等价于原链的 `progress.Play(false)`。 |
| F-Prepare（static-observed） | E-Resume、E-Pause | `Play(false)` 在 `prepareToPlay=true` 时会直接返回。应读取 `prepareToPlay=false` 或在停止调用后确认 `isPlaying=false`，再打开暂停页。中途异常或矛盾状态应保留“部分副作用未知”的审计，不进行自动恢复。 |
| F-Resume（static-observed） | E-Resume | `PauseUI.Resume` 自带 `_pausing&&!_over` 守卫；清 `_pausing`、设 `_pauseProgressIsLerp=true`、`_fingerId=-1`、`_pauseTime=3f`，再 `Play(true)`、动画和 HideMouse。`Play(true)` 先设 `prepareToPlay=true, prepared=false`；当帧 `!Paused` 不足以证明实际播放。 |
| F-Skip（static-observed） | E-End | Skip 设置 `_skip=true` 并虚调用 LevelOver；结束链在 skip 分支自行清 `_levelOverTime`，结果构造器接收 `_skip`。它表示提前跳过本局，不是暂停页 Back 的整套退出动画。 |

自然暂停键路径额外依赖“Pause 动作在本帧释放”，这是用户输入条件，不应成为程序化 SDK 控制必须伪造的输入。触摸路径还有手指和拖动距离门槛，同样不应由 SDK 伪造。`_pauseTime` 初始值是 3f，恢复时也重设为 3f；暂停页 Update 仅在场景时间达到约 1.8 秒后运行其主要逻辑。

Pause UI 自身还会设置 `TouchBlocker.Block(false)`，并在约 0.3 秒后经共享延迟回调 `0x3B1B60 → TouchBlocker.Release(false)` 释放。完整调用原入口会保留这些副作用。

### 精确 getter 签名

| 程序集 / namespace / class | static getter | 返回与 RVA |
|---|---|---|
| `Assembly-CSharp.dll` / 空 / `TouchBlocker` | `get_Blocked()` | `System.Boolean`, `0x3830A0` |
| `Assembly-CSharp.dll` / 空 / `WindowControl` | `get_HasActiveWindow()` | `System.Boolean`, `0x3869B0` |
| `UnityEngine.CoreModule.dll` / `UnityEngine` / `Time` | `get_timeSinceLevelLoad()` | `System.Single`, `0x155F090` |

三个 getter 都有保留的 native 方法体；Time 是 ICall wrapper。运行时仍应通过确切 metadata 校验 static、零参数和返回类型，无法读取时仅将 Pause 标为不支持，不向其他时钟或独立控制扩散失败，更不能默认守卫为 true。

## 调用路径与待验收项

P-Pause（callflow）：E-Guard 的实际前置读取 → E-Pause 的 `controller.Pause()/Play(false)` → E-Pause 的 `PauseUI.Pause()` → 实机观察 `Paused=true, Playing=false`，之后多帧确认谱面时间停止、音乐停止、无新判定。F-CanDo 与 F-Pause 支持前半段静态修复；后半段实机结果本子任务未执行。

P-Resume（callflow）：E-CanDo 的暂停页守卫 → E-Resume 的 `PauseUI.Resume()` → 观察 3 秒准备过程 → 实际 `Playing=true, Paused=false` 后时间与判定继续。不得将当帧尚未播放的 receipt 误记为最终恢复成功，也不能因为正常倒计时尚未结束就重复调用恢复。

建议离线回归覆盖：正常播放时 CanDo=false 仍可 Pause；冷却、terminated、over、UI blocker、活动窗口各自拒绝且零 dispatch；第一次停止抛错和第二次暂停页调用抛错均保留部分副作用未知；Resume 当帧 `!Paused && !Playing`，后续才恢复；Retry 只在 paused/CanDo 时派发；Skip 不依赖 CanDo、仍遵守 SDK 所选保守状态策略。实机需分别确认暂停、恢复、重试新 RunId、Skip 的提前结束结果与作用域释放。
