# Back 直接取消候选实现

实现已写入；主任务已在源码中接入 SDK 常量、Adapter metadata/注册和 Flush。模块常量引用 `GameLifecycleCapabilityIds.BackCancellation`，值为 `riz.lifecycle.back-cancellation.observe`。本 Back 候选尚未构建或实机验收，不能作为 B01 已通过的依据。

`ExactBackCancellationModule` 使用独立原生绑定：`PauseUIControl.Back@0x3C3A50` 的 before 确认实际 `_pausing&&!_over&&!_terminated`，after 正常返回并观察 `_over` false→true，仅记录退出请求。该请求独立根住 controller、pause、information，最多保留一个；原调用上下文拥有另一组根，由 Broker finally 释放。

`PauseUIControl.Update@0x3C3E20` 只为已有请求的那个 PauseUI 捕获上下文。必须同 controller/pause/information、同 observed RunId/session/scene，before `_terminated=false`、controller._skip=false 且 `_over=true`，原函数正常返回后 `_terminated=true` 且 controller._skip=true，才接受终止。这两个变化位于原 LevelOver 与终止音效调用路径。单次 `_terminated=true` 样本、对象消失、其他对象或守卫 no-op 都不会生成取消。

`BackCancellationTracker` 将稳定身份与 revision 分开：pending 请求允许同局的正常暂停 baseline、availability 等修订推进；终止边沿重新捕获最新 `ObservedRunCancellationToken`，送入既有严格 revision 的 `VerifiedRunCancellationQueue`。排队不立即发生命周期事件，下一次 host update 的 `FlushAccepted()` 才发一次 `Aborted`。scene/run/结果边界、模块 Release 会释放 pending 根并清除排队工作。

8 项新增 Game.Tests 仅验证纯状态 helper 与真实生命周期状态机/队列的协作，包括：无终止边沿不结束、正常 revision 推进仍取得新 token、重复和陌生对象有界拒绝、场景/局/结算/Release 清理、ObservationLost 和 terminated baseline 不造取消、旧 Update 不消费新同谱面请求、无真实开始不接受、清理异常不阻塞后续所有权。它们没有执行原生函数，也不代表 Back 在游戏中通过。

## 集成步骤

1. 主任务已在 adapter 注册独立模块，构造参数与 Retry cancellation 模块相同；模块内部 const 引用 SDK 可选能力常量。
2. 主任务已在 host update 的 sampled observer 之前调用 `FlushAccepted()`；实测还须确认 SDK 命令审计先于取消通知。
3. 串行构建并运行 Game/Hooks/Scope 相关回归；本子任务没有运行 dotnet。
4. 真实暂停后按游戏退出按钮，保留同 session 的 Back 请求、terminal edge、同 RunId VerifiedCallback Aborted、scope 取消及返回场景日志；再验证 duplicate/no-op、快速同谱面重进、退出后的迟到结果。

## 证据与限制

复现静态指令检查：

```powershell
C:\Python314\python.exe docs\validation\lifecycle-cancellation-20261020\back-inspect.py
```

证据链：E-BackRequest / E-BackCommitted（`back-static-proof.json` 中实际指令）→ F-Back（两阶段直接观察）→ Back before/after → pending roots → Update before/after → 新 token 入队 → 下一 host update 发 Aborted。既有范围与导入/导出锚点见同目录 `scope.md`、`cancellation-static-proof.json`。

此能力不证明是否生成结果页，也未建立迟到结果的原生 origin。现有结果采样的同谱面串局风险仍须独立修复，不能因接入取消就移除该限制。本子任务未编辑 GameEventState、Adapter 注册、SDK、Runtime 或 Core；SDK 和 Adapter 的相应接入由主任务完成。

## 独立逻辑复查与修复

复查发现两项应在集成前修复的问题，已在独占的 `ExactBackCancellationModule.cs` 修复；当前没有发现该模块内仍待修的同类必修问题。这是代码复查结论，尚未编译或实机验证。

| 检查点 | 风险与处理 | 当前证据 |
|---|---|---|
| Pending 来源 | 原生 Back before 必须 active、双向 controller/pause 关系一致、信息对应真实 observed start；`_pausing&&!_over&&!_terminated` 成立；正常 after 的 `_over` false→true 仅创建请求，不结束局 | Capture、AfterBack；Back 原生守卫指令 |
| 正常结束误归因（已修） | 250 ms sampler 可能落后于真实失败/结束/另一次 Skip。现在 before 直接拒绝 native information.levelOver/crashed、controller._crashed/_skip，不依赖旧的 open token；Back after 再拒绝正在结束的状态，Update after 再拒绝失败状态。Update 还要求 `_skip` 本身 false→true，排除“另一个 Skip 已发生，只读到 true”的误归因 | 新增精确 Boolean 字段要求；Capture、AlreadyEnding/Crashed、AfterBack、AfterUpdate；静态脚本校验 dump 中字段类型及偏移 |
| 真正完成 | 仅 pending 来源已确认的 Update 原调用成功返回，并跨 `_terminated` 与 `_skip` 两条边沿才完成。正常 LevelOver 会禁用暂停组件，before active 检查也会拒绝迟到 Update；没有 pending 的自然完成不进入 terminal observer | Back/Update 独立绑定；BindRetainedObservation 仅正常返回后调用 after |
| Logical disable（已修） | 原先 Release 在解绑前释放 pending 根；清理/日志重入可能在清空后重新创建请求。现在先标 detached、立即解绑两个 sink，再退订 lifecycle、清 queue/pending；Capture 与两个 after 都检查完整 Installed 状态。已在途上下文仍由 Broker finally 独立释放，不依赖 pending 根 | Release、Installed；不改 Runtime Broker |
| 过期 scene/run | Pending 稳定身份包含 session/run/scene/info/controller/pause；生命周期订阅与 Tick 调 Prune，scene/run/真实结束换代会释放。普通同局 revision 推进不释放，终止时才重新取最新 token。旧 Update serial 不能消费新的同谱面请求 | BackCancellationTracker；既有 8 项 helper 回归覆盖该分支，尚未运行 |
| 资源与异常 | Pending 独立持有最多 3 个 root；一次在途上下文另外持有最多 3 个，finally 自行释放。拒绝/重复请求也接管并释放传入 retention。清理现在不分配快照数组，逐项移除后释放，某项失败继续其余；部分 pending 保留失败会清理已创建根并保留原始异常 | RootSet、RetainPending、tracker Clear/Release |

若退出请求已被原生接受，但后续原 Update 抛错、读取失败、绑定撤回或仅观察到 `_terminated=true` baseline，本模块会保持失败关闭：不补造取消。最多一个 pending 将在真实 scene/run 边界或 Release 清理；这可能漏记异常退出，但不会把观察失效冒充已确认取消。不能靠移除这些拒绝分支来提高“事件覆盖率”。

仍需主任务串行构建和实机验证原生字段、正常完成不误发、失败不误发、Back 真正退出、logical disable、快速同谱面进入以及迟到结果。资源数上限和 helper 回归均不等于原生流程验收。
