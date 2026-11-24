# 下一补丁：结果对象的局次来源

日期：2026-10-20。此文件是对 666 包源码的只读审计结果与下一补丁计划；没有修改、构建或安装运行代码，不是已完成实现或实机验收证据。

后续状态：该计划之后已获授权实施，源码和专项红绿结果见同目录 `result-origin-implementation-checkpoint.md`。本文件保留初始审计语境；666 包与新工作树证据仍分开。

## 确认存在的缺口

`src/RizToolbox.Adapter/GameEventState.cs` 的 `Finish`（143–155 行）在当前局仍开放、谱面相同或未知，且当前控制器已 `over` 或已消失时，把新看到的结果关联到**当前** `RunId`。结果对象本身没有来源凭证参与判断。

因此存在具体错误窗口：

1. 局 A 与局 B 是同一谱面，B 已产生新 `RunId`。
2. B 进入 `AwaitingResult`，或控制器暂时不可见而进入 `ObservationLost`。
3. A 的结果此时才出现在结算界面。
4. 当前 `Finish` 会把 A 的成绩标为 B，并关闭 B。

现有 `tests/RizToolbox.Game.Tests/Program.cs` 的 “Same-chart late result during active playback is explicitly unassociated” 只覆盖 B 仍 Playing 的情况；“Late result after direct Skip...” 对新 B 的检查也仍在 Playing。取消 token、判定调用入口来源及 run scope 边界已经有防旧局逻辑，不能据此推导结算对象也已解决。

`GameEventObserver.Poll`（169–191 行）根持有实际 `InsideLevelResult` 并防止连续重复采样，但传给 `Finish` 的只有复制后的 `RunFinished` 字段。这是丢失对象来源的接线位置。

## 采用的最小接入点

固定游戏构建的 `NormalModeInLevelController.ControllerOver()`，RVA `0x3BEA60`，返回 `InsideLevelResult`，没有显式参数。使用已有 `ExactHookAbi.InstanceReferenceReturn0`，不扩展构造函数参数 ABI。

现有可复查静态证据：

- `docs/validation/gameplay-controls-20261020/static-pause-guard/disassembly.txt:1281` 起覆盖该函数。
- 函数读取接收者的 `LevelParams`、判定计数等字段，在 `0x3BECAA` 调用 `InsideLevelResult` 构造函数，在 `0x3BECAF` 装回同一对象，并于 `0x3BED07` 返回。
- 当前 dump 中 `NormalModeInLevelController.ControllerOver` 声明与上述地址吻合。

这支持“原始返回对象来自本次接收者”的接入设计。实施时仍需核对固定 SHA、导出的类型/方法签名、返回类及实际结果传到 `LevelOverControl.levelOverInfo` 的路径；不能把上述静态证据当成新 hook 的实机证明。

## 修改边界与方法

1. **Runtime：新增纯观察的引用返回绑定。**
   在 `ExactHookBroker` 增加类似 `BindRetainedReferenceObservation<T>` 的内部方法：入口 `capture(receiver)` 返回拥有根的上下文；原方法成功返回后，校验并根持有原始返回对象，再调用 `after(context, result)`。`InvokeReference` 仍原样返回原始返回值；观察者不能提交替换值、吞异常、补调用或改参数。新观察绑定允许多个订阅，不占用既有替换协调器的独占名额。
   
   与新判定观察路径保持一致：捕获上下文先于普通 Before 回调；结果观察先于普通 After 回调。错误线程、重入、receiver 不匹配只透传原方法，不能借外层上下文。原方法异常时不提交来源，所有已获得根都在 finally 中分别释放。停用发生在途时停止提交，仍完成清理。原始返回对象与替换协调器最终对象不同，采样后者时不得继承原始对象来源。

2. **Adapter：单一、内部、有界的结果来源存储。**
   新建内部 `ResultOriginRegistry`，及独立可撤回的 `ResultOriginModule`（名称可按现有目录惯例调整）。由模块使用固定 `ControllerOver` 接入。入口从接收者根读取并根持有 `LevelInformation`，通过 `GameEventState.CaptureResultOrigin` 捕获当时已观察到的 `SessionId / RunId / information identity`，附加 controller identity、模块代次、观察证据。返回时使用入口凭证，不能重新读取当前 `RunId` 来替换它。
   
   `CaptureResultOrigin` 仅接受已观察开局且与该 information 匹配的当前开放局；它允许该局已 over，不能套用取消 token 的 `!over` 限制。当前 state 只存 information，故 `GameEventObserver.Poll` 的真实 controller 采样入口还需登记所选 controller/information 对，捕获时核对这两个身份，不能把捕获参数中的 controller 无条件盖章。控制器 A 已被 B 替换后才进入的旧回调若没有有效来源，只记录 unavailable/拒绝计数，不把它关联到 B。保持当前 Skip/Abort 之后无来源的结果仍未关联的语义；本补丁不承诺补齐所有已关闭旧局的历史结果。
   
   返回对象经精确返回类验证后，注册“独立根持有的实际 result → 入口凭证”。正常重复捕获同一对象、同一来源幂等；同一对象出现互相冲突的来源时撤销该对象的关联并记录歧义，不能最后写入胜出。

3. **State/Observer：来源成为关联的必要条件。**
   `GameEventObserver.Poll` 在已有结果根保护下查询上述存储，把查到的凭证与复制 DTO 一起传给 `GameEventState.Finish`。`Finish` 只有在来源与当前开放局的 session/run/information 完全匹配、谱面没有已知冲突、且结束状态条件成立时才能发 `RunEnded`。未知、冲突、旧局来源只发未关联的 `ResultObserved`，不改当前局的 phase、RunId、开放状态或 run scope。
   
   不保留 chart-only fallback，否则缺陷仍在。凭证匹配不要要求当前 revision 等于入口 revision，也不要要求结算场景等于开局场景：合法暂停、结束和场景切换会推进它们。真正约束是固定局次和对应根身份。对外 SDK 1.x 构造签名保持不变。

4. **模块与诊断接线。**
   在 `RizlineAdapter` 组合单一 registry，注入 runs observer 与结果来源模块；模块依赖现有 lifecycle，不把它做成业务插件功能。声明单独实验能力/operation（例如结果来源观察），把当前“late-result origin remains unverified”的无条件文字替换为实际模块状态和本次结果的拒绝原因。
   
   模块不可用或撤回时：禁止新关联、清空 registry 并释放它持有的根、提高代次；已有已发 DTO 保持不变。基础结果字段读取和 `ResultObserved` 可继续，不能用旧代次来源或谱面猜测补回 `RunId`。重新启用后只接受新代次真实捕获，初始化结算页仍仅建立基线。

## 根的寿命与容量

- 入口 controller/information 根只需活到原调用退出；若 registry 为后续精确身份校验需保存 information，必须显式转移或获取独立根，不保存已释放根的裸指针凭证。
- 每个待采样 result 必须由 registry 持有独立根，不能借用 broker 的短暂返回根。匹配时读取 `RootHandle.Target`，不假定对象地址永不变化；跨存储边界使用内部对象身份而非公开原始地址。
- 建议初始上限 32 个待处理结果。满额时拒绝新的关联并记录容量原因，不能静默覆盖仍等待消费的来源。仅凭时钟到期不能替旧结果猜测来源。
- 消费后释放 registry 所持的独立根；observer 仍按既有规则保留当前结果根，完成连续采样去重。若需要“旧结果 A → 新结果 B → A 重现”去重，应明确增加有界已消费根缓存（同样 32 项），保留对象身份与已处理标记；被淘汰对象再次出现按未知处理，不能重建旧局关联。
- 模块撤回、适配器释放、容量拒绝、重复/冲突注册、返回类型不符、异常和在途停用，每条路径都必须有明确根所有者及一次释放。不可在 native 返回观察回调中同步派发生命周期事件；存储提交后仍由正常采样阶段发事件，避免原生调用内部触发插件资源拆卸。

## 有意义的回归矩阵

| 用例 | 必须证明 |
|---|---|
| A/B 同谱，B 已 over，迟到 A 结果 | A 不关闭 B；B 的真实结果随后恰好关闭一次；这是当前代码会失败的首个回归 |
| A/B 同谱，B 暂不可见 | A 不改变 B 的 `ObservationLost` 和 `RunId`，B 恢复后仍能完成 |
| 当前正确来源，controller 销毁/场景已改变 | 正常结果仍关联同一局，避免以更窄条件“修好”错误而丢正常功能 |
| 原调用中开始 B，返回 A 对象 | 来源保留入口 A；不能在 after 重读状态误绑 B |
| 未开局、初始化既有结算、未知来源、未知 metadata | 不补造开局或关联；初始基线不重放；metadata 未知不能绕过 origin 检查 |
| Skip/Abort 已结束后迟到结果 | 无第二个 `RunEnded`；不取消后来 B 的 scope |
| 同对象重复捕获/重复采样/页面消失再出现 | 同来源幂等，最多一次结束；冲突来源显式拒绝 |
| 同地址复用/可变 RootHandle.Target | 根身份和代次阻止误认旧对象；释放前不复用仅指针缓存 |
| 返回 null、错误返回类、capture/after/cleanup 异常 | 原始返回和异常保持，原调用恰好一次，其他观察者继续，根无漏释放 |
| 重入、错误线程、不匹配 receiver | 不访问不允许的元数据、不借外层来源，原方法正常透传 |
| 在途停用、模块撤回/重新启用、满32项 | 无旧来源提交、无旧代次关联、所有根明确释放；基础未关联结果读取继续 |
| 真正双插件订阅与普通 Before/After 回调 | 捕获来源不受前置插件改状态影响；旧结果不终止另一局 scope；错误插件不影响其余订阅 |

落地测试位置：`tests/RizToolbox.Game.Tests` 覆盖状态与来源 registry；`tests/RizToolbox.Hooks.Tests` 覆盖纯观察 reference ABI 与资源寿命；现有正式宿主 scope 测试补旧结果不会取消新 run scope 的集成用例。先加入 A/B AwaitingResult 回归并记录修复前失败，然后实现，不用自造 token 直接传给 `Finish` 来替代真实捕获/注册/消费链。

实机新包必须冻结新会话：至少正常结束与失败、同谱 Retry、Skip/Back 后再次开局、停留/离开结算页。日志同时记录来源捕获、返回对象注册、采样消费及最终 RunId/原因；注入的离线迟到场景与自然游戏流程分别标明，不声称通过自然游玩已经发生所有迟到次序。

## 完成条件

只有新源码、新包和上述边界证据一致后，才能移除“同谱迟到结果 origin 缺口”。该补丁不自动完成整个 B09，也不把 666 包既有完成结果追溯升级为带原生来源凭证的结果。
