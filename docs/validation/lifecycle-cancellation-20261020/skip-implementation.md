# 原生 Skip 终止候选

`ExactSkipTerminationModule` 已写入；主任务已在源码中集成 SDK 常量、Adapter metadata/注册和 Flush。模块常量引用 `GameLifecycleCapabilityIds.SkipTermination`，值为 `riz.lifecycle.skip-termination.observe`，构造参数与 Back/Retry 相同，提供 `FlushAccepted()`。本子任务未构建或执行该候选的实机验收。终止原因明确为 `Skipped`；不借用 `Aborted`，也不伪造 `RunFinished`。

## 静态后置判据

E-Skip：`NormalModeInLevelController.Skip@0x3C17D0` 写 controller._skip=true，然后虚调用 LevelOver。E-LevelOver：固定构建的 `NormalModeInLevelController.LevelOver@0x3C0C00` 在入口取 controller.pauseUIControl，`0x3C0C98` 调 `Behaviour.set_enabled(false)`；skip 分支随后清 `_levelOverTime` 并安排原有返回链。

因此不猜 `information.levelOver` 必然变 true。候选使用两条实际变化：同一个被根住 controller 的 `_skip` false→true，以及其同一个 pause 的 `enabled` true→false；原 Skip 必须正常返回。完整函数正常返回与该已知调用链共同支持“原生 Skip 已提交结束”的结论，禁用 pause 单独并不构成终局证据。

Probe 精确校验 Skip 与 LevelOver 的 metadata/RVA、固定游戏 SHA、Windows x64 ABI，只 patch Skip。`get_enabled()` 精确签名为 `UnityEngine.CoreModule.dll / UnityEngine / Behaviour` instance 零参数 `System.Boolean`，RVA `0x1534F90`。该副作用来自 LevelOver，不能拿 `get_isActiveAndEnabled=false` 代替 enabled=false：前者可能只因父对象 inactive 而改变。

F-Skip → P-Skip：E-Skip/E-LevelOver → 保留原调用上下文 → 确认双边沿 → 取得同 run 的最新 token → `VerifiedRunCancellationQueue.Enqueue(..., RunEndReason.Skipped)` → 下一 host update 的 Flush → 一次 `RunEnded(Skipped, VerifiedCallback)`。

## 身份、拒绝与资源

Before 必须 active controller、active information，且原生 information.levelOver/crashed、controller._crashed/_skip 均为 false；存在已观察真实开始的 current run token；controller/pause 双向关联一致，pause 未 terminated、active 且 enabled。该入口观察真实调用，不改 SDK Skip 只允许 playing 的控制策略。

After 重新核对同一组 root 的关联、失败标志和双后置；token 的 session/run/scene/info 必须仍相同，允许同局正常 revision 推进但使用最新 token 入队。缺失符号、原调用异常、after 读取失败、逻辑 binding 关闭、过期 run 或只有一条变化均不发事件。已结束/已 Skip 的再次调用照样转发原始方法一次，但不重复发终止。

一次上下文最多保留 controller、information、pause 三个 root，成功和异常均由 Broker finally 清理，不跨帧保留；队列只存普通 token 与证据。Release 先标 detached 和解绑，再清队列，已在途上下文仍独立释放。没有保存原生协程句柄或迟到结果身份。

## 回归与集成

Game.Tests 新增 8 项受控状态/队列/宿主回归：Skip 原因与审计顺序、只允许 Aborted/Skipped、同 token 冲突原因拒绝、正常结果/over 抢先终止时不覆盖、旧 scene/run/revision/撤回不重放、迟到结果不重复结束或终止新同谱面局、未观察开始/错误身份/重复边沿拒绝，以及真实 PluginHost 的 Run scope 在普通生命周期监听前取消一次。它们未调用原生 Skip，不冒充游戏通过。

主任务已注册模块/metadata/SDK 常量，并在下一 host update 的采样之前调用 Flush；新的 State/Queue 已由主任务提供支持 Skipped 的接口。本子任务没有修改 State/Queue、SDK、Adapter 注册或 Runtime/Core，也没有运行 dotnet/UI。源码与 Game.Tests 已冻结交给主任务串行构建，本文不提前宣称其构建结果。

静态复现：

```powershell
C:\Python314\python.exe docs\validation\lifecycle-cancellation-20261020\skip-inspect.py
```

完整指令、样本/源码哈希位于 `skip-static-proof.json`。该静态检查和离线回归不证明原生双边沿已在游戏发生；还须冻结真实 SDK Skip receipt → 同 RunId 的 Skipped → scope 一次失效 → LevelList 返回证据。结果页是否出现、迟到结果 origin 及新同谱面局已 over 时的关联仍属独立限制。
