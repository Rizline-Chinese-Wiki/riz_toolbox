# Rizline 重试与主动退出的直接生命周期接入

优先接入 `NormalModeInLevelController.Retry` 的真实接受边沿，可以将正在观察的旧局可靠结束为 `Aborted`。正常返回本身不够：原函数包含无副作用的拒绝分支，必须同时匹配旧局身份、原始暂停守卫和同一 `LevelInformation.gameObject.activeSelf` 的 true→false 变化。`PauseUI.Back` 只提交退出请求，真正退出发生在随后 Update；不能把它等同于当场取消。Back 后是否出现可供现有 observer 读取的结果页尚未验证，不宣称结果必然出现或必然不存在。

范围见 [scope.md](scope.md)，`flavor = null`。本报告是静态研究和可实施设计，未接入代码或执行实机验收。

## Evidence 与复现

```powershell
C:\Python314\python.exe docs\validation\lifecycle-cancellation-20261020\inspect-cancellation.py
```

脚本检查固定游戏 SHA，引用同一样本既有导入/导出证据，重新解码 7 个方法并断言关键指令，保存源代码观察点和哈希。已实际执行通过。完整证据：[cancellation-static-proof.json](cancellation-static-proof.json)、[disassembly.txt](disassembly.txt)。本次 JSON SHA-256 为 `c7135396c5789f5aaf3f62fd59b1f173dc55eb8dcd7097eca70402c35ec9b587`，重跑会更新观察时间与源代码快照，哈希相应改变。

| Evidence | 原始观察 | 用途 |
|---|---|---|
| E-imports / E-exports | 同固定 SHA 的前序 PE 证据含 10 个导入 DLL、386 个导出；本 JSON 记录其位置与哈希 | 固定样本身份，避免迁移 RVA |
| E-RetryGuard | `0x3C1359` 读取 controller.pauseUIControl；`0x3C1373..0x3C1387` 检查 `_pausing` 与 `_over`，不满足直接到 `0x3C151B` 返回 | 不能把所有 native Retry 调用算取消 |
| E-RetryAccepted | `0x3C1446..0x3C1470` 取 controller.LevelInformation.gameObject 并 `SetActive(false)`；随后 UnloadSceneAsync、StartCoroutine | 同一对象 activeSelf true→false 证明接受路径执行 |
| E-BackRequest | `0x3C3A54..0x3C3A66` 以 `_pausing&&!_over` 为守卫并将 `_over=true`，再启动动画 | Back after 仅能证明请求已接受 |
| E-BackCommitted | `PauseUI.Update` 在 exitProgress>0.99 且 `_over` 后，清 `_levelOverTime`、设 controller._skip、虚调用 LevelOver、播放终止音效，最后 `0x3C44BA` 写 `_terminated=true` | 具备 pending Back 的 false→true 边沿才是直接退出证据 |
| E-Destination | LevelOver 的 `_skip` 分支经 metadata 接口 slot 0 选择下一场景，区别于普通结算固定分支 | 不能仅由 `_skip=true` 断言现有结果页 observer 一定拿到结果 |
| E-State | `GameEventState.AbortFromVerifiedCallback` 仅受 open/over/字符串守卫；`Finish` 采用 chart 与 over/消失关联 | 新入口必须增加身份 token，防误取消与迟到结果串局 |
| E-Broker | 普通 Bind 的 Before/After；After 仅在 original 正常返回时执行 | 捕获原生对象根时须增加 unconditional finally 清理 |

## Retry 的精确接入方案

目标为 `Assembly-CSharp.dll`、空 namespace、`NormalModeInLevelController.Retry()`、instance `System.Void`、RVA `0x3C1310`，ABI 为 Windows x64 `void(receiver, MethodInfo*)`。使用已有 `ResolveExactHook` 的 SHA、精确 metadata、参数数、返回类型、机器入口地址与可执行段检查，不根据 `script.json` 的 C 风格 Signature 猜参数；该 dump 的 Signature 会漏列实例参数。

P-Retry（callflow，E-RetryGuard → E-RetryAccepted → E-State）：

1. Native before 在已验证主线程确认实际 receiver 是当前唯一 active controller。读取并根住该 receiver、其 `LevelInformation`、`pauseUIControl`、information 的 GameObject。后者由 `Component.get_gameObject()` 取得；`GameObject.get_activeSelf()` 返回 `System.Boolean`，RVA `0x1538EF0`，为 instance 零参数 getter。
2. 在 shared state 捕获内部不可伪造 token：observer/module 激活代次、SessionId、当前 RunId、当前 information/controller 身份和当前 scene/run 代次。当前局必须确有 observed start、仍 open，info 必须与 observer 保留的根及 state._information 相同。无开始、观察失效、多个 active controller、错误对象均只转发原始调用，不产生终止事件。
3. 读取同一 pause 的 `_pausing=true && _over=false`，并记录 information GameObject 的 `activeSelf=true`。只有该状态可以形成候选。不要把未观测到的 pause edge 伪造成生命周期事件；实际字段足以决定 native 守卫，生命周期 RunId 可以仍停留在刚才的 Playing 样本。
4. 原函数只执行一次。Native after 只在原函数正常返回时检查同一根对象 `activeSelf=false`。这条边沿排除拒绝分支；不是靠轮询丢对象推断。`GetActiveScene` 的 handle 前后变化可以补充诊断，但不必代替该直接边沿。
5. 再验证 token 指向的旧局仍是 state 当前 open run，observer/module 未撤回、identity 未替换、run 未由真实结束关闭。以带 expected token 的内部状态方法发布一次 `RunEnded(Aborted)`，`Source=VerifiedCallback`，采样间隔 0，证据包含目标 RVA、hook invocation ID 与接受边沿。不要在外层只验证一次，然后调用当前仅收任意字符串的 Abort 方法。
6. 每次 native invocation 的根在 unconditional finally 清理，即使 original 抛错、逻辑 binding 被撤回或 After 观察异常也必须释放。异常路径不发布成功取消，记录部分原生副作用未知；不吞原始异常、不重试 original、不自动恢复或干涉游戏重试。

Retry 不修改 `pause._over` 或 `_terminated`，不能要求它们产生 false→true。函数返回 void，`StartCoroutine` 的句柄不保存在 controller 字段，不能假设存在可轮询的“重试 token”。Unity `Destroy(controller.gameObject)` 是延迟行为，不应把 `op_Implicit` 当帧变 false 或立即丢失 controller 当作唯一接受判据。

最小实现位置建议为独立的 optional exact-cancellation 模块，依赖运行观察 state，但不依赖公开 SDK hook 订阅者。普通采样能力不能因为该可选精确入口缺失而整体撤回；未知构建只拒绝这项直接取消。现有 Broker 可新增内部带 capture/after/finally 的作用域绑定；不要把原生 receiver 或对象根暴露给插件，也不要用长寿命全局闭包保存未清理的 before 状态。

## Back 的下一步与迟到结果

F-Back（static-observed，E-BackRequest、E-BackCommitted）：先在 Back after 确认 `_over` false→true，并保留同局 pending token；不能立即称 run 已结束。随后通过同一 `PauseUI.Update` 的 exact after，验证原函数正常返回、`_terminated` false→true、同一个 controller._skip=true、当前 run token 未换代，才能证明该退出已经过 LevelOver 正常返回。`_terminated=true` 的单次采样不足以证明主动退出，必须有前序真实 Back 接受及边沿。

结果处理仍需独立完善。当前 `Finish` 的“同 chart 且 over/对象消失”可能把旧局的迟到结果误关联给同谱面新局，尤其新局也已 over 时。直接取消后不应伪造 RunFinished，不应让后来一个未知来源结果再次结束该局；无可靠来源的结果保留 `ResultObserved`、RunId=null，并明确原因。要保留迟到结果与已结束旧局的强关联，应在实际结果创建路径捕获 origin token 与结果对象身份，例如增加只读的 `ControllerOver` 返回对象观察；不能仅用相同 chart 或时间接近来补造归属。

Back 的这套 pending/terminal/late-result 处理在本次研究中没有实现，也未实测，应保持明确缺口。此前仅凭 `_skip` 说明 Back 必然出现结果页的推断已撤回，本报告与结构化证据均不依赖该假设。

## 作用域与验证建议

F-Scope（source-observed，E-State）：当前 `PluginScope.CreateScope(ScopeLifetime.Run)` 只订阅 SceneChanged、RunStarted、RunFinished，没有订阅 lifecycle RunEnded。接入真实 Aborted 时，应补充一次性的 RunEnded 边界，并按照该 scope 绑定的真实局代次过滤；不能伪造 RunFinished 来驱动清理。现有 legacy 边界仍需保留兼容性，但多条边界到来时必须幂等。

离线应验证：Retry guard no-op、before info 已 inactive、陌生 receiver、旧 observer、无 observed start、同 chart 新 RunId、原函数异常、after 读取异常、两次 Retry、before/after 之间 state 已结束、作用域回调重入和 module 撤回，均不伪造或重复终止；after 成功分支只关闭正确旧局。另验证原始方法每次 native entry 恰好调用一次，所有捕获根在成功和异常上均释放。

实机应保存：旧局真实开始 → 暂停 → Retry 接受边沿 → 同 RunId 的 VerifiedCallback Aborted → 新 information → 新 RunId 真实开始，且旧 run scope 取消一次。原始日志、安装包哈希与 session 应对应。仅有 hook 安装成功、离线测试或 ObservationLost 都不构成实机取消验收。
