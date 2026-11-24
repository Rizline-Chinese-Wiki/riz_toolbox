# 下一批：接通 LOCAL 初始化隔离

这是下一批可实施任务，不是本批源码或验证结果。当前 `src/`、`tests/` 和本目录已冻结证据保持不变。依据 [静态报告](2026-10-23_逆向-LOCAL初始化顺序-report.md)、[27 个锚点](evidence-check.json) 和当前 `LocalRunLaunchCommitState`、`NativeLocalRunLaunchReservation`、`NativeLocalRunTerminationBoundaryState` 的真实接口编写。

目标是把实际 Level 初始化调用接入已有 pending 责任记录：原 getter 成功执行一次并核验其结果；完成初始化后，等 Start/getter/Progress.Update/Progress.Play/既有五门等真实原生入口全部退出，再允许共享 commit。初始化期间已认领 LOCAL 的父 Start 尾部，以及后来实际 LOCAL Play 的 Mode 调用不得退回 Steam。

## 已确定的原生边界

画像固定为 GameAssembly SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。画像不符时先拒绝，之后才能解析类、方法或安装钩子。

| 目标 | RVA / 原生形状 | 依据与作用 |
|---|---|---|
| `InLevel.LevelControl.Start` | `3EA580`；`void(self, MethodInfo*)` | `[self+20]` 为 info；`3EA935` 经 LevelStart delegate 调 getter；`3ECDFE` 才写 loadOver=true |
| `NormalModeInLevelController.GetInsideLevelParams` | `3BEDF0`；`ref(self, info-ref, MethodInfo*)` | `3BEF51` 写 controller.Info；随后立即使用 info.progress；必须是完整单引用参数、引用返回 ABI |
| `InLevel.ProgressControl.Play` | `400700`；`void(self, bool-play, MethodInfo*)` | 既有 [结果归属证据](../local-result-ownership-20261022/README.md) 冻结 `400795` / `4007B5` 两条 Mode 尾调；可能发生于 Start 返回后的起播/恢复路径 |
| `SteamTimeline.SetTimelineGameMode` | `48A950`；`void(int-mode, MethodInfo*)` | 现有 `LocalProgressIsolationPolicy` 已持有；Start 在 `3ECE0E` 调用，必须共享现有 entry/policy 权限判断 |
| `SteamTimeline.SetTimelineTooltip` | `48A980`；`void(string-ref, float, MethodInfo*)` | Start 在 `3ECE30` 调用；RCX 为 string，XMM1 为 float，R8 为 MethodInfo。wrapper `48A9B3` 尾调实际 ISteamTimeline API |

`Progress.Update` 在实际 info 非空、loadOver=false 时直接到 `401617..401630` 返回；这只证明条件内的有限机器路径，不证明 first Update 晚于 getter，也不授权凭默认值或 scene 同 GO 猜序。

## I01：扩展同一套 sealed 初始化/Timeline 权限

在 Runtime 增加只能由宿主使用的 Start、getter、Progress.Play、Tooltip sealed targets。校验画像、module 非零、确切 declaring class、方法名、参数类型、返回类型、实例/静态属性、非 byref、RVA、方法地址唯一及 MethodInfo 身份。getter 不能复用“无参引用返回”输送层，Play 必须验证 Windows x64 单 bool 参数形状，Tooltip 不能当作两个整数参数或只保留 string。

现有 Mode 地址已经归属 `LocalProgressIsolationPolicy`；**禁止另装同地址第二套初始化 policy**。选择把初始化上下文作为现有 binding 的能力扩展，或把现有 Timeline authority 升为一个共享所有者，Progress/初始化各保留独立作用域。两者最终只经过同一 Mode replacement 和 trampoline。保留现有 Progress 终止语义与计数，Tooltip 追加到同一明确 authority；Instantaneous/EndRange 不因新增初始化场景扩大授权。

各 API 的抑制权限必须显式分开：精确且已认领的初始化帧可抑制 Start 的 Mode/Tooltip；精确 LOCAL Play 帧可抑制其 Mode 尾调；精确终止 Progress.Update 帧按现有规则抑制终止尾部。不得把“初始化帧存在”变成全线程所有 Steam API 的通行证。普通调用保留全部原参数、浮点位型、MethodInfo、返回值与 original 恰一次。

完成标准：同 Mode entry 只安装一次；新旧能力共存不冲突；缺一个目标、别名不同或 transport/ABI 不同，均不能发布“初始化已可用”。

## I02：Start 建未定父帧，实际 getter 作纯身份认领

Start 的 around 入口先计真实在途，再建立线程/嵌套归属明确的未定父帧，记录确切 receiver 和 MethodInfo。不得先读 native 字段、取 root target、调用 metadata 或以“当前存在 LOCAL 局”分类；此时没有抑制权限。Start original 执行一次，帧在 finally 退出，覆盖原方法完整尾部、异常和观察者清理。

实际 getter 入口先从已经发布的 registry 纯身份表识别 controller 的确切 pending session/run generation。分类本身任何线程可用，不能借助当前活动场景、LevelParams.Metadata、From、结果页或任意 getter 推断归属。

一旦确切 LOCAL controller 被识别，**先为其当前直接 Start 父帧留下不可撤销的 provisional claim，再进行任何主线程检查、native root、字段读取或宿主回调**。这样后续 worker/main 检查失败、逻辑解绑、MethodInfo 异常或宿主异常，均不能消掉 claim 并把原 Start 尾部重新交给 Steam。provisional claim 只保护本次原生调用栈，不表示初始化成功，也不能用于 commit、capture 或释放。

Ordinary 的嵌套 Start 必须遮蔽外层 claim；不同 receiver 或不同 session/run 的 getter 不得继承外层令牌。相同确切 receiver 的递归也必须有明确继承规则，不能把“最近某个 LOCAL ancestor”套给任意子帧。找不到父帧、栈容量溢出或父帧关系不明时，记未知并拒绝初始化就绪；溢出遮蔽层不得让 getter 错认更外层帧。

完成标准：worker 上认领也能保留父 Start 尾保护；没有父帧不能伪造成功；混合普通/LOCAL 递归、换线程、失配 MethodInfo 和逻辑 detach 都不会让既有 claim 回退。

### I02b：Progress.Play 有独立精确作用域

复用已有独立 root 后发布的 `LocalProgressIdentity(session, runGeneration, controller, progress)` 纯映射分类实际 Play receiver。LOCAL Play 的 Mode 权限在进入 original 前成立，持续到 original/finally 完整退出；不能要求该局已终止或当前存在 Start 父帧。保留 `Play(bool)` original 恰一次及 bool/MethodInfo 原值，不能跳过其原始播放/暂停状态处理来消除 Timeline。

Play ordinary 子帧必须遮蔽外层 LOCAL Play 权限；同 receiver 递归只有明确相同 token 才继承。worker 调用可做纯分类与原生入口记账，不执行宿主字段读取或 main-thread 初始化事务；若继续其实际 game original，Mode 保护必须已经成立，未知/解绑不得撤回既有 claim。起播、暂停与恢复可能来自更晚 Update 或其它调用栈，所以“Start 尾 + terminal Update 已保护”不构成 Play 覆盖。

本项只引用已冻结 `local-result-ownership-20261022` 的两个直接尾调锚点，不重提取或修改旧证据。其它 Steam 入口继续按实际路径审查，不将 Play 的 Mode 权限扩大到任意 Steam 操作。

## I03：主线程核对父 receiver、采用真实 graph，再调用 original

provisional claim 后仅在主线程进行初始化事务。核对 launch 仍为同一 `PendingCommit`、独立 controller root 已存在、activation attempt 已记录、无 early terminal/return/cleanup/unknown，且三个既有真实责任槽均已预留。

读取并独立保留当前 Start receiver，核实类型是 `InLevel.LevelControl`、对象存活，并核实其 `[+20]` **精确等于本次 getter 的真实 info 实参**。验证 info 类型、存活、其 progress(+28) 非空及类型正确，progress.info(+20) 反向精确相同；验证已有 controller/metadata/params 约束和对象互异。不能只检查 controller.Info，因为 original getter 尚未写入它；也不能允许调用方传入一对自行拼装的 info/progress。

把这一事务接入已有 `AttachInitializingGraph`；为父 Start identity 增加明确的验证条件与所有权，不用另一套平行 graph registry。此时使用预留 entry 采用独立根并发布纯 progress mapping，全部成功后才准许 getter original。父帧由 provisional 转为 validated token，只增加成功信息，不能擦除原 claim。

已通过上述校验的 LOCAL getter original **恰调用一次**，保留 info 与 MethodInfo 原值。返回后立即采用返回引用的责任记录，核验返回对象恰为同一 owned bundle 的 params，类型、metadata、chart/resource 等原有闭包关系仍成立，且 controller.Info 恰为本次 info。不能再次调用 getter 来重取/修复，不能把 `null`、其它 bundle 的 params 或新的默认对象标为成功。

失败或 worker 入口不执行 native 验证/初始化 original，不调用 Steam，不退回普通分支。引用返回失败必须定义显式原生中止输送方式；可评估 `null` 失败返回，因为 `LevelControl.Start@3EA943` 随后检查空值并进入异常分支。该方案只有在有限 native fixture 证明 CLR 异常不跨 unmanaged 边界、无二次 original、父帧保持到实际退出后才可采用。`null` 不能作为成功 params 或让启动继续被标记 ready。输送方式未验证时继续关闭 LOCAL admission。

完成标准：任一校验失败都保留已采用根与未知责任；正常 original 一次，校验前拒绝零次，原调用已开始后失败仍一次；没有“失败后再 original 一次”的补救。

## I04：复用原配额、提前留好根槽与发布存储

扩展 `NativeLocalRunLaunchReservation.Reserve` 及其既有 graph/work/capture 记录，复用现有 32 个 process/session owner 上限；不为初始化再开独立 32 局容量，也不在 getter 已进入后才申请 work/capture 配额。`NoteNativeLaunchAttempt` 与 `NoteActivationAttempt` 前，应已确认新边界完整可用并预留每局根槽、graph 映射发布存储、初始化状态及诊断空间。

列出固定根图：现有 controller/info/progress，加上独立 Start receiver；返回 params 的临时独立根/转移记录必须在调用前有槽，校验后可以交给已有 bundle/初始化 owner，但不能把借用引用当独立所有权。root 创建、adoption、publication 或 cleanup 失败仍须强 process owner 接住；未知 handle 不重试 free，也不释放配额。纯空预留回滚继续只允许从未 native-attempted 的现有 `CanCancelPureReservation` 路径。

Start 普通帧可只持有本次调用的原始 identity 和 TLS 状态，不能给每个普通 Start 分配一份 session 资源。帧栈/递归容量应有明确边界和溢出遮蔽方案；LOCAL 不能因帧申请失败而绕过 provisional claim/中止规则。退出帧只释放帧责任，不能作为 graph 消费者已结束的证据。

完成标准：配额满、分配失败、第二/第三根失败、发布失败、factory/bridge 之间失败，均有单一明确 owner；未创建任何 native 对象的空回滚与发生 native 尝试后的未知保留不可混用。

## I05：部分挂接与解绑保持原生所有权

所有新目标先完整解析校验，再变更 detour 状态。先准备可无权限透传的共享 static entries 与 getter/Start 输送层，最后统一发布初始化可用性；安装失败不得留下“全部已装”假状态或可被新 launch 使用的半套 binding。安装任意阶段已被 native 看见的 replacement、delegate、trampoline 和 policy 都必须有强 process owner。

逻辑 detach 只阻止新 admission，不能删除正在运行 Start/getter 的 claim。已被 claim 的帧保留旧 authority，直至自身 finally 退出；普通调用仍透传。不能通过移除 sink、替换 policy 或解绑 Mode 来使在途 LOCAL 尾部回 Steam。若现有 `Entries.Remove` 安装异常逻辑不能证明 detour 未发布，本批必须同时补齐未知安装责任，不能当作纯 managed 失败丢弃。

完成标准：逐目标注入安装失败；在 getter original、Start original 和 static 原调用内发起 detach/rebind；旧帧仍受原 authority 保护，新的重复同地址 owner 被拒绝，NativeInstallFailure 不产生可用 binding。

## I06：把 Start/getter 与 static 输送层加入真实 drain

现有 `NativeLocalRunTerminationBoundaryState.IsDrained` 目前组合 Progress.Update 的实际在途与 host 五门。增加 Start/getter/Progress.Play 的完整原生入口计数：计数覆盖 ordinary/LOCAL、classifier、root/handler、original、结果核验、清理和 finally；worker、重入、拒绝及异常也必须对称退出。累计调用次数、快照数量、graph 验证完成或 logical binding active 都不能替代 InFlight。

共享 Mode、新 Tooltip 及新增输送层若允许在途解绑/复用，也应纳入真实 entry 活动/unknown 状态。读取 drain 时包括同一 owner 的全局 unknown 在途、该 run 的已归属在途及 retained 原 policy 在途。静态抑制调用也按真实入口计数，不能只给 original 分支加计数。

getter 成功仅记“真实 getter 返回并核验”；**不得在 getter 或其 Start 原调用尚未退出时完成 commit**。由之后主线程 Tick 在所有相关边界（包括 Play）已 known、InFlight=0 时调用 `VerifyInitializedGraph`，再走已有 `PrepareCommit`→`CommitPrepared`。preCommit 在最终发布前后继续检查同一实际状态及并发 early terminal；任一 unknown/stale generation/reentrancy 都保留 pending/unknown。已有结果 capture 和 graph/session 退休所用的真实 drain 也必须包含 Play，不能只在 preCommit 检它。

完成标准：getter 已返回但父 Start 仍在栈上时无法 Initialized/CommittedReady；普通 worker Start/getter 在途也挡住全局 unknown 证明；退出最后一层后才允许原有共享 token 发布。仅设置“回调已完成”的测试应失败。

## I07：离线验证清单

按行为补有意义的回归，不以固定新增数量替代覆盖。先定位现有 Hooks/AudioContent fixture 复用同一生产路径，再在独立批次串行构建，保持 frozen 证据可复现。

- Ordinary：Start、getter、Play、Mode、Tooltip 所有原参数原样透传；Play(true/false)；float 的零、负数、NaN 位型；string 可空；MethodInfo 原值；引用返回原样；original 一次。
- LOCAL 正常：同一 pre-reserved launch → exact pending controller → Start 父帧认领 → main 原始入参/反向 graph 验证 → getter original 一次 → owned params 核验 → Start Mode/Tooltip 被抑制 → 最后退出 → drain → shared commit。
- 归属拒绝：错误父 receiver/info、另一 run/generation、重复 getter、无 Start 父帧、ordinary 嵌套遮蔽、同 receiver 递归、classifier throw、非零失配 MethodInfo；不得取得 capture/commit/retirement 权限。
- Play：Start 已退出后的独立 exact LOCAL Play(true/false)、恢复路径、在普通 Update 内嵌 LOCAL Play、LOCAL Play 内嵌普通 Play，以及 worker/递归/detach；两个原版 Mode 尾调分支均受同一 entry 保护，不以 terminal claim 为条件。
- worker/失败：先 claim 再检查主线程；不读取 native/root target、不调用初始化 original 或 Steam；中止输送被验证；已经采用的责任继续保留。返回根失败、params 不同/null、graph/owner 发布失败、early terminal 与 getter/Start 内退出异常均覆盖。
- 挂接/生命周期：同 Mode address 只有一个 entry；旧 Progress 终止隔离继续有效；新 Tooltip 部分安装失败、in-flight detach、rebind、scope 溢出、清理失败不回 Steam，也不误抑制 ordinary 子帧。
- 真正 drain：getter/Start/Play/Mode/Tooltip 的 observer、original、核验及 cleanup 内分别嵌入 commit 尝试；正反断言保证计数覆盖完整入口，没有累计计数冒充完成。

至少保留一次真实生产后端参数/字段读取的受控 native fixture，而不只是手写“符合条件就成功”的 mock。任何新 ABI 必须补实际 Windows x64 native fixture，校验寄存器/栈参数、引用返回、浮点保真和异常输送，不只验证 managed delegate 互调。

## I08：交付与验收边界

下一批交付应包含：共享 Runtime sealed binding、Adapter 原生初始化 bridge 接线、独立精确 Progress.Play 作用域、已有 preCommit/结果/退休真 drain 扩展、完整离线构建结果、精确 source/staged manifest 与独立审查结论。新增的读取/抑制/拒绝计数须能区分 Start/getter/Play/Mode/Tooltip，并记录 phase、frame/thread、确切 session/run 与 retained unknown，不能把普通调用减少当作成功。

源代码与离线 fixture 完成后仍保留 LOCAL admission 关闭，直到授权实测取得同一镜像、同一包、实际普通原版对照及 LOCAL 初始化→正常结束/暂停/重试/返回的连续证据。尤其不能仅凭本批 Start 的两个直接 Steam 调用被抑制，就宣称 Save/Steam 已完整覆盖；Start 间接调用、其它初始化/异步回调、结果与退休全链仍须逐项证明。本批也不把 `CommittedReady` 当作真实 RunStarted/Awake 成功或完整消费者退休证据。
