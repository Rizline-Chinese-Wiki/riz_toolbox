# 激活前预留终止责任，再提交已绑定的运行图

2026-10-23。范围：只读审查既有启动计划、会话 registry、终止 handler 和原生 bridge；本次仅新增本文，没有修改源码、构建、安装或操作游戏。`runtimeAcceptance=false`，`flavor=null`。固定游戏 SHA-256 仍为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。

下一步应给现有 handler/bridge 增加 **PendingCommit 的责任预留与原位完成登记**。所有槽位和 controller 角色在激活前已有主人；实际 information/progress 在受控初始化入口出现时才绑定；最后一次性发布可运行状态。不能把 `Sessions.Commit` 提前当作激活证据，也不能把现有两个 `RegisterCommitted` 移到激活之后直接调用便认为空窗已消失。

## 已确认的空窗

| Evidence | Finding | 下一步 Path |
|---|---|---|
| E1：[原计划](../local-launch-20261022/next-activation-transaction.md) 第4步，激活/Awake/globals 正向核验后才 Commit | F1，confirmed-source/high：Awake 是有全局副作用的提交边界，发生在 registry 的最终逻辑提交之前 | 保留此证据顺序，在此前预留责任，不能伪造已成功 Commit |
| E2：[registry](../../../src/RizToolbox.Adapter/AudioContent/LocalRunSessionRegistry.cs) 的 `RegisterPrepared`、`BeginCommit`、`BindPendingController` | F2，confirmed-source/high：SessionId/string 与 RunGeneration/long 在 Prepared 时已确定；PendingController 已能发布 exact controller 的保护分类 | 使用同一既有身份和 CommitClaim 预留后续槽位；不是等激活后再分配 generation |
| E3：[bridge](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunTerminationBoundaryState.cs) 与 [handler](../../../src/RizToolbox.Adapter/AudioContent/LocalRunTerminationHandler.cs) 的 `RegisterCommitted` | F3，confirmed-source/high：两者都要求 Committed；bridge 还要求非空完整正反图；handler 无预留记录时 `Find` 会失败。门仍 skip stock，但不是已登记的可完成终止事务 | 增加 pending 入口，原位完成已有记录；不在激活返回后重新竞争32槽 |
| E4：[既存反汇编](../local-metadata-bridge-20261022/disassembly.txt)，`GetInsideLevelParams@3BEDF0` 的 `3BEF51`、`3BEF6F`、`3BEFD5` | F4，confirmed-static/high：实际传入 info 被写到 controller `+28`，随后已有 SetActive、delay 等副作用。仅参数 setter 不会建立此图 | 在该 exact 初始化入口 original 之前取得实际 info 参数；验证并保留 info/progress，再允许原初始化继续。具体新 ABI/调用时序仍须后续验证 |

这不是当前已启用的播放漏洞：本批尚未安装实际结果 sink/commands 的完整启动 handler，也未开放 RequestPlay。它是下一批接线必须消除的顺序缺口。

## 先预留什么

推荐增加一个很小的 host-only `LaunchReservation`，由已有 SessionHandle、当前 CommitClaim、完整 bundle 责任共同授权。它只协调现有 owner，不替代 registry、handler、bridge 或 native capture。以下名称是待实现接口设计，不是当前可调用 API。

| 预留项 | 何时占槽/取得根 | 责任与 receiver 角色 |
|---|---|---|
| 现有 session / bundle / 返回快照 | clone 前 `RegisterPrepared` → `BeginCommit`；保存原 scene、选择、revision/chart、globals baseline | 已有 SessionId 与 RunGeneration 原样贯穿；bundle/params 只按当前 claim 导出 |
| handler 的 `ReservePending(claim)` | clone 前先占既有32个 process work 槽之一 | Pending 状态也接收本 controller 的终止/取消并保责任；禁止 capture 和默认成功 Retry/Back |
| bridge 的 `ReservePending(claim)` | clone 前先占既有32个 process graph 槽之一；controller/info/progress 固定根槽预先存在 | 此时没有 progress 地址；仅记录同一纯会话身份，不能发布零地址或猜测的 progress token |
| actual-result capture 配额 | clone 前在现有32-owner capture 责任中预留一次额度，随 factory/reservation 转移 | 只预留配额；不调用 ControllerOver，不建立 ResultCaptureClaim，不伪造结果对象 |
| exact controller / capture factory | inactive clone publication 后、任何激活前；registry 独立 controller 根、bridge 独立 controller 根、factory 独立 controller 根分别拥有 | `ControllerLevelOver`、`ControllerRetry` 使用 controller 角色。factory 登记后已有主人，不能等终止发生才分配其责任槽 |
| actual information / progress | 新初始化入口实际提供 info 时，在已预留槽中独立 root；验证 info→progress→info | `Progress.Update` 只使用 exact progress 角色；初始无 info 是“尚未绑定”，不是可用的空图 |
| result UI / commands | 准入预检时已有真实实现与可返回错误状态；展示时先发布 presentation 责任再执行可失败工作 | 宿主 UI 回调走 host commands。不能拿 controller、info、InsideLevelResult 或任意游戏结果页冒充 `ResultStart/ResultRetry/ResultBack` receiver |

槽位应作为一组预留：任何容量拒绝都发生在 clone/激活之前；已预留但未发生 native 副作用的额度可以明确退还。原生根/参数导出/初始化一旦有未知结果，保留既有 owner 和额度，不做“补登记重试”。完整 settings 与活性检查继续沿用现有责任边界；不要为构造态 speed=0 猜写常数，见[标量来源](../local-launch-20261022/parameter-scalar-consumers.md)。

## 直接实施的顺序

1. 验证同一 host/registry、五门真实安装与在途读口、四项 Progress/Timeline authority 的完整 active binding、真实 sink/commands、返回快照和 globals owner。`IsReadyForHost` 在安装 handler 前可用；不要求此时 `AcceptingNewRuns=true`。保留现有 unknown/撤回拒绝。
2. 完整 bundle 交给 `RegisterPrepared`，取得当前 `BeginCommit`。在任何 clone 前，顺序取得 handler、bridge、capture 三份 reservation；一个失败便释放尚未采用 native 工作的预留，已有 bundle 走现有安全退出。先完成固定槽分配和必要的 publication 存储容量准备。
3. 在既有 Awake 窗口内创建 inactive clone。原生 publication 输出立即有主。`BindPendingController` 前后不得穿插激活；把 exact controller 和独立根原位附着到两份 reservation，factory 根也在此采用。随后通过既有 exact setter 注入本 bundle 的参数并读回。至此 Pending controller 的五门分类已存在，handler 也已有能够保留早期终止的记录。
4. 打开本 reservation 专属的受控激活窗口；记录“激活调用已尝试”后才执行激活。Awake 重入或同步 LevelOver 只能进入预留记录：仍先封住当前 native terminal tail、记早期终止/失败，禁止 stock fallback、禁止同步 capture。发生早期终止时不发布运行成功。
5. 用 **一个新增的 sealed exact `GetInsideLevelParams(InLevel.LevelInformation)` 初始化边界** 填入尚空的 info/progress 根槽。它只对已预留 exact controller 生效，ordinary receiver 原参数 original 一次；需要专属含引用参数/引用返回的已验证 ABI，不能复用现有 InstanceVoid，也不能 Hook 共享 `SetInsideLevelParams@36B0E0` 地址。具体收养顺序见下节。
6. 原初始化与激活最外 native 调用返回后，再核 Awake 恰好一次、controller/globals/持久场景责任、原返回 params 恰为本 bundle、controller→info→progress→info 与 metadata 完全一致，且所有相关真实 native 在途为零。两份 reservation 已拥有全部必要根和 token；此时不再分配槽、创建 factory 或发现 receiver。
7. 最后由一个协调方法完成提交：先 `Sessions.Commit`，再对共享 reservation 发布一次 immutable `CommittedReady` 状态。bridge/handler 都引用同一 reservation，不能各自公布独立的 ready bool。Commit 与 Ready 之间发生的 exact 入口仍按 Pending 保留并 skip；只有共同 Ready 才能消费后续终止队列。此阶段不得夹入新的可失败 native 调用或分配工作。
8. 已 Ready 的正常终止继续复用本批现有路径：当前 tail 标记 → 去重排队 → 更晚真实 Unity frame、相关 Progress 与五门真在途归零 → 真实 ControllerOver 一次 → actual root/cache → 自有 UI。逻辑 Commit/Ready 不发布 `RunStarted`，生命周期仍需真实播放观测。

这条 P1（`path_type=callflow`）直接承接 E1/F1 → E2/F2 → E3/F3 → E4/F4。旧 `RegisterCommitted` 可保留为严格的已初始化登记入口；新启动事务应走“预留→附着→原位完成”，而不是先激活再调用这两个旧入口。

## actual Progress 出现前如何不猜身份

预留的是 **会话身份和槽位**，不是未来 native 地址。`SessionId` 与 `RunGeneration` 已从 Prepared 得到；controller 地址在 inactive clone publication 时绑定；info 与 progress 地址必须来自初始化边界的真实输入和字段。不存在 info 时 classifier 对任何未知 Progress 都返回 ordinary，不按“当前有 LOCAL pending”扩张分类。

初始化边界 original 之前，在同一 exact controller/pending claim 下立即保留其实际 `info` 参数，再取 `info.progressControl+28` 并保留 progress，核 `progress.levelInformation+20 == info`、正确 class/Unity 活性、controller 参数 metadata 与独占 bundle 一致。此时 controller `LevelInformation+28` 可以仍为0，因为已证实原函数在 `3BEF51` 才赋值；这是独立的 `Initializing` 合同，不能冒充当前 bridge 的完整正反图验收。先发布该受控来源的 immutable progress token，再执行 original，才能覆盖 original 中同步/嵌套的 Update 和 terminal tail。返回后必须核 controller 的实际字段已经等于同一 info，然后才允许第6、7步。

若入口时 `info.progressControl` 或反向链接还未初始化，**拒绝本次 LOCAL 初始化并进入已拥有的失败事务**，不能先放 original 再在下一 Tick 补图。现有静态范围没有证明所有 scene/Start 的先后，因此下一批最小补证应确认这个入口之前正反链接是否成立，以及 first Progress.Update 是否可能更早发生。若确实更早，改在已证明的同一 scene 初始化更早边界收养 exact info/progress；在证明之前保持启动不开放，不构造假 info、不使用 SceneLoaded 枚举结果猜归属。

## 失败与退出合同

| 失败阶段 | 必须保持的结果 |
|---|---|
| 纯预留/仍封闭 inactive 且 Awake 持续0 | 不做假 Commit；符合现有闭包证据时可走 never-activated retirement |
| 已尝试激活、出现 Awake/globals/info/scene/coroutine 之一 | 交给 active launch ledger 保责任；不得回用 never-activated verifier，不能仅“未 Ready”便 Close |
| 初始化入口字段不符、worker、重入、publication/缓存/UI 不明 | exact claim 继续 skip，reservation sticky unknown/held；不自动重试 native 初始化或 capture，不回 stock |
| 两份记录已附着而最终 Commit 失败 | 根、factory、pending terminal 与 slots 仍有主；不倒退成 Prepared，也不二次执行激活 |
| 返回或 provider/module 撤回 | host pump 持续工作。只有实际消费者结束证据、registry Close、撤 map 后二次真 drain、UI 回调结束及各独立清理完成后才退还槽位 |

下一批最小受控回归应覆盖：容量拒绝发生在激活计数0时；Awake 内同步 LevelOver 找到 pending handler；初始化 original 内嵌套 Update 已有 exact progress token；Ready 前无 capture；Commit/Ready 之间 reentry 仍保留；info/反向链接缺失不调用 LOCAL original；已激活失败不能取得 never-active token；撤回后 pending/unknown 阻止门 detach。随后另做用户授权的实机时序与保存/Steam 零调用验收，本文不替代它们。

## 只读复核与源版本

在仓库根目录可运行以下只读检索，分别定位 E1–E4；无需重提取二进制或启动游戏。

```powershell
rg -n '成功证据|受控激活|registry.Commit' docs/validation/local-launch-20261022/next-activation-transaction.md
rg -n 'RegisterPrepared|BeginCommit|BindPendingController|Commit\(' src/RizToolbox.Adapter/AudioContent/LocalRunSessionRegistry.cs
rg -n 'RegisterCommitted|Phase != LocalRunSessionPhase.Committed|private Work Find' src/RizToolbox.Adapter/AudioContent/LocalRunTerminationHandler.cs src/RizToolbox.Adapter/AudioContent/NativeLocalRunTerminationBoundaryState.cs
rg -n '003bef51|003bef6f|003befd5' docs/validation/local-metadata-bridge-20261022/disassembly.txt
```

审查时源 SHA-256（后续实现变更时这些值自然会变化）：

| 源 | SHA-256 |
|---|---|
| E1 原计划 | `41164d168771af2aa399e281f1ae68779b038830a4f42ff12a35e7106f11b1f7` |
| E2 registry | `225e7ff29ae5586f76c5667f27f488f90e0d2d09ebedbf4b7e3417588da0cc92` |
| E3 bridge | `c9c5e871a3d6b8cd35396990bb72da397bd29373fd0de3e047db24a11fdfd1d7` |
| E3 handler | `cc966279e9add1a5c5c7c1c5eec5f15608549144c0ce86e5f2792aa862e7d21b` |
| E4 反汇编 | `baeedffbee2a550ef1b819c6dc1a3d54887c52ef101034ef4fc293118b40f867` |
