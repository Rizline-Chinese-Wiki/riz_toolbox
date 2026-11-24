# Tween 最终退出路径实施审计

日期：2026-10-28。工作区：`C:/Users/71957/Desktop/riz_toolbox`。依据：[P4 设计](TWEEN-WORK-COMPLETION.md#p4具体死亡凭证unregister-与释放顺序)与当前实际源码。范围为 Tween 工作、admission、source journal、同步引用、子事件和 coordinator 的最终退出。本轮只读生产代码，新增本文；没有构建、执行 fixture、安装、操作游戏或修改其它代理文件。源码仍在并行完善，以下以文件及方法名定位为准，行号是本次读取时的辅助定位。

**结论：P4 仍缺一条实际可以成功的完整退休事务。不能把 `CanRetireWork()` 改为 seal/idle/death 的布尔组合。已有的具体 TweenInfo 死亡票、已提交 source 步骤、零参数空子事件效果票可以复用；完整原生切面、checked Unregister、各类根的所有权释放、source hub 退订、final receipt 均须新增。** 当前 typed onTween 已具备实际入口/参数/父归属观察，尚无完整 listener effects 结束证明。完成本项也不代表 B01–B19 或整局消费者结束。

本文使用 docs-generator 的通用技术审计结构，`flavor = null`。范围来自当前用户授权和 [SPEC.md](SPEC.md) 的 active 项目约束；没有独立 security case，`scope.md`/IOC/ATT&CK 不适用。本文的实施路径是待完成工作，不是已发行的生产能力。

## 1. 证据与可复查结论

这些是读取中的工作区源码，不是冻结候选；`content_hash = n/a`，不把旧构建或旧测试结果继承成 retirement 的通过证据。

| 证据 | source_ref 与位置 | 实际内容 |
|---|---|---|
| E1 | `.docs/foundation-expansion/TWEEN-WORK-COMPLETION.md`，P4、可验收功能 8/9 | 退出必须包含 source 封口、队列退出、具体死亡/效果、native Retired、逐 holder free、私有最终票。 |
| E2 | `src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.cs`，`CaptureOperation`、`CompleteOperation`、`DisposeContext`、`TrySeal`、`CanRetireWork`（397） | 实际上下文、source 接线及 seal 已有；`CanRetireWork` 无条件 false；Owners 没有 Tween 退休移除路径。 |
| E3 | `src/RizToolbox.Runtime/Hooks/NativeTweenAdmission.cs`，`ClaimCore`、`Observe`（181）、`ReceiptMatches`（255）、`LoadedTransport` | native Unregister export 已加载，但 authority/lease 没有可达的受核注销或 free；receipt 验证只接受 Open/Closing/Sealed。 |
| E4 | `native/RizToolbox.JudgeContext/TweenAdmission.cpp`，`FiveLanesState`（165）、`RizTweenAdmission_Unregister`（234）、`RizTweenAdmissionEnter/Exit` | Unregister 在 admission SRW 锁下核 exact class、cached0、sealed、issuers0、entries0 与五入口采样，回 Retired 后清原生 object/cached。 |
| E5 | `src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.Sources.cs`，`CompleteSourceOperation`（107）、`RefreshSourceOccurrences`（519） | 真实 pair、occurrence、transient、move 与 final graph 校验已有；QueueExited 为内部模型推导，尚不是完成票。 |
| E6 | `NativeLocalTweenDeathTracker.cs`、`NativeLocalTweenWorkOwner.Death.cs`、`src/RizToolbox.Runtime/NativeRuntime.TweenDeath.cs` | 同 root 的 TweenInfo 活基线、Return、sealed lease epoch、两个后续死亡 frame 及私有 DeathReceipt 已有；只覆盖 TweenInfo。 |
| E7 | `NativeLocalUnityEventLedger.Effects.cs`、`.TweenChildren.cs`、`NativeLocalTweenWorkOwner.ChildEffects.cs`、`.TypedEvents.cs` | zero-arg 实际 empty-list receipt 与 child effects 关系已有；非空 listener effects 未闭合；Single1 目前只记录 native Return/Abnormal/Dispose。 |
| E8 | `src/RizToolbox.Runtime/Hooks/NativeTweenSource.cs`、`.DynamicReferences.cs`、`NativeTweenSourceReferences.cs` | 初始 roots 独立分配；operation anchor 借 backend 同 handle；同步引用借全局 hub 同 handle；reader/hub/root-set 没有退出或释放 API。 |
| E9 | `native/.../OperationContext.cpp`、`TweenSourceContext.cpp`、`TweenSourceReferences.cpp` | whole-entry、source context/pending、reference scope/callback 分别记活动；不是同一原子排空协议。 |
| E10 | `NativeLocalRunLaunchCoordinator.TweenWork.cs`、`.ActiveRetirement.cs`、`.ActiveOwners.cs`、`NativeLocalObserverRetirement.cs` | 普通/frozen pump 已泵 Tween seal/death；active Close 前检查 TweenCanRetire，而旧 observer context 在 Close 后才能用且最多 2048 slots。 |

| Finding | Evidence → Finding → Path |
|---|---|
| F1，validated | E2/E3/E4 → native 有 Unregister，但 managed 生命周期不能安全调用或接收 Retired → P4-A 增加私有完成 interval、checked native commit 与 lease 的独立 retired/free 状态。 |
| F2，validated | E4/E9 → 五入口 SRW 采样不覆盖 Single1/source/reference，也没有跨域阻止新 borrower 的 token → P4-B 增加共同 rundown/epoch 协议，不用重复 GetInfo 代替原子性。 |
| F3，validated | E8 → root-set 的非 null Root 数量混合实际持有和借用，hub 跨 reader 共享 → P4-C 按 RootHandle 实例和实际 holder 建释放清单及共享租约。 |
| F4，validated | E5/E6/E7 → 队列、死亡、effects 三类事实互不替代，InstantWork/typed/nonempty/setter 仍缺结束票 → P4-D 补完整 WorkEnded 私有收敛。 |
| F5，validated | E2/E10 → 复用 Close 后 observer context 会产生 Close 前置条件循环；退休后旧 Tick/Read 还会读 root/native lease → P4-E 新专用事务及冻结诊断分支，先独立退休 Tween，再供 active verifier 消费。 |

只读复查命令（在项目根目录运行）：

```powershell
rg -n 'CanRetireWork|TrySeal|Owners|ReadActivity|RootRecords' src/RizToolbox.Adapter/AudioContent -g 'NativeLocalTween*.cs'
rg -n 'Unregister|ReceiptMatches|CanAdvance|RootRetention|ProcessOwners' src/RizToolbox.Runtime/Hooks/NativeTweenAdmission.cs
rg -n 'FiveLanesState|Unregister|RizTweenAdmissionEnter|RizTweenAdmissionExit' native/RizToolbox.JudgeContext/TweenAdmission.cpp
rg -n 'ProcessOwners|RootRecords|ReferenceHubs|_readers|ReserveOperationAnchor|ReserveSynchronousRoot' src/RizToolbox.Runtime/Hooks -g 'NativeTweenSource*.cs'
rg -n 'PumpTweenWork|TweenWorkCanRetire|ListenerEffectsAccounted|TryRetireActiveObserverOwners' src/RizToolbox.Adapter/AudioContent -g 'NativeLocalRunLaunchCoordinator*.cs'
```

## 2. 可以直接复用的真实凭据

| 已有凭据 | 复用方式 | 不能提供的额外结论 |
|---|---|---|
| exact NativeFirstInstall/operation/source publication、官方 metadata 与已持 RootHandle | 作为同候选、同 module、同 class/method/source epoch 的基线，最终事务前再验证。 | publication Ready 不等于本 run 工作退出。 |
| `NativeTweenAdmissionLease` 私有 authority/Owner/slot/epoch、独立或共享 RootRetention | 新 interval 必须引用实际 lease 实例，涵盖完整 `_sourceClaims`，拒绝 caller DTO。保留 status 6 Claim busy 的同根重试语义。 | Seal 只封新 Play/Add，不能清队列或结束已接纳 Execute。尚未成功 Claim 的 lease 没有可注销 slot。 |
| `OperationContext` 真实 Before/After/Complete/Dispose 与 exact frame | 冻结完整操作截止序列；每个记录正常 Returned 或合法空 Skipped，且已 Dispose、没有 open `_top`。 | 普通返回不能结束其子任务或整个 occurrence。Skipped 的 source operation 可 `Skipped=true, Committed=false`，必须单独验空。 |
| `SourceOperation` reader-issued pair、源点索引、normal final graph commit | 沿实际标签状态验证每个 owned occurrence；pair/transient/move 为空且 final graph 一致。保留 foreign 项用于位置重放。 | 不可要求所有 foreign Tween 死亡；也不可只比较 DTO 的 QueueExited。Instant occurrence 从定义上不会 QueueExited。 |
| `NativeLocalTweenDeathTracker.DeathReceipt` | `ReadTweenDeathReceipt(exactRoot)` 当前会复核同 owner/root/launch/epoch；`VerifyReceiptCurrent → ReadSealedEpoch(requireNativeDead: true)` 在消费前重新读 sealed lease且要求 CachedNative=0，即使没再Pump也拒绝同slot/epoch复活。可以作为 TweenInfo 具体 native 死亡事实。 | 没有 target Component、其 GameObject/Transform 的对应死亡票；没有 source/ref 原子排空。Unregister 后不能再调用此票的现有 VerifyCurrent（它会读已 retired 的 lease）。 |
| zero-arg `EmptyListenerEffectsReceipt` + `TweenChildEffectsReceipt` | 复用对应 native Invoke generation、实际 empty executing list、稳定 mutation、After/Dispose/physical exit 与源 CALL After。 | 只证明该具体调用没有 listener effects；不封未来序列化/direct/external writer。非空 listener、typed onTween、setter effect 未获得该票。 |
| Single1 typed invocation 的 float bits、真实硬件 return PC、真实 Play/Execute parent、Return/Abnormal/Dispose | 作为 typed effect 的 entry/exit 证据输入，实际共享 event 按 Tween parent 选择。 | 不含完整 executing-list/listener generation，也没有 occurrence ID；不能按 Returned=true 发 EffectsEnded。 |
| `ExactHookBroker.Binding.Dispose()` | 复用逻辑退出本 sink；历史已捕获 context 仍由真实 Exit/Dispose 完成。 | 不拆永久 native detour，不保证 physical drain，也不是 source-reference hub 的退订。 |
| coordinator 的 classified Return / same session/launch 私有验证 | 新事务必须由 `RequireTweenAdmissionSealRequest` 所保护的真实 Return 开始，普通和 frozen 两条 pump 都可调用。 | 不能借整局 closed session 或 factory 五对象死亡代替具体 Tween/target 死亡。 |

死亡票的现有强处应保留：同 handle instance、class/field/native identity、单调 frame、live→dead 不可逆；Return frame 本身不算死亡；两个真正后续 frame；lease epoch 变动会撤销旧票。最终原子切面只是补充，不应删除这些检查。

## 3. 实际根与借用关系：必须逐 holder 释放

| 记录/holder | 真实根的来源 | 必须新增的动作 |
|---|---|---|
| Tween backend `_roots[4096]`、`_slots` | `Hold`/`AdoptPublishedInput` 各分配一次；包括源 Tween、foreign queue 数据、manager、容器/历史数组、target、curve、event、string/delegate。 | 后端专用 root manifest，按 slot + handle 实例记录 once-free；清 `_slots` 并使 retired 分支禁止再 Hold/Root/Pointer。不能只释放 `_tweens`。 |
| 初始 admission 的 IndependentClaimRoot | `Claim` 在 Runtime 重新 Root，和 backend 根不同。 | native 真实 Retired 后由 admission authority 释放一次；有独立 free-attempt 状态。 |
| 动态 admission 的 SharedRetainedProcessRoot | `ClaimRetainedRoot` 保存同一个 backend handle；不会另 Root。 | admission 退出只放弃借用，backend 才是 free 的唯一 holder；两处各调用 Dispose 会误报或掩盖重复释放。 |
| 初始 source root-set entries | `RetainRootedObjects` 调 `_adopt` 重新 Root，和 backend 不同。 | reader 专用 own-root release，先停止其 journal/references 读取；移除 set/ProcessOwners 注册。 |
| operation root-set anchors（动态 Tween/Manager） | `ReserveOperationAnchor` 直接 `RecordRoot(entry, root)`，借 backend 同一 handle。 | 释放 root-set/OperationRootReceipt/owner anchor 的借用，不负责 free；确认物理框架已结束且无历史 reader 会再校验它。 |
| source synchronous reference entries / 全局hub永久根 | `NativeTweenSourceReferences.Notify` 全局 `_roots` 独立 Root，当前明确只增不减、永久保留；多个 reader 的 root-set 共用该 handle。 | 本run先实际退订/放弃borrow，绝不能free全局根。若要兑现P4的最终可回收根目标，另外增加hub root lease：当前delivery、未闭合native scope、每个reader/set、其它owner分别计数；只有hub实际holder在last lease退出后free。 |
| DeathTracker / typed / child membership | 通常借 backend 或 admission 根；它们自己不 Root。 | 失效其可解引用 membership/receipt 路径；消费完成后保存 scalar history。不能为“death roots”虚构另一次 free。若新增 target tracker 真正另 Root，须独立登记。 |
| UnityEvent backend `_roots[2048]` 和 child root responsibilities | `AdoptBorrowedTweenChildEventRoot` 的 `Hold` 建立或复用该 backend 自己的 handle，与 Tween lender 独立。 | 现有 active-close 后释放路径可继续负责这些根。Tween 结束前须收到它对独立根/效果责任的真实接受或 frozen receipt，不能替它 free，也不能把它的完整退休设为 Tween 退休前置条件。 |

当前 `NativeLocalTweenBackend.RootRecords` 已避免把 shared admission root 再算一次，但仍加上 `_sourceJournal.RootRecords`；后者数的是 entry 的非 null Root，包含共享/借用 references，且不包含所有 hub 全局根。**这个合计不是 unique GC handle 数，也不是可依序 free 的清单。** 不同 handle 指向同一对象应各 free 一次；同一 handle 出现在多个表只允许实际 holder free 一次。清单必须记录 retention kind、holder、handle 实例、borrower、native slot/epoch、free attempt/result，不能按 managed address 去重。

SourceReferenceHub 另有两个必须先解决的问题：

1. `SourceReferenceHub` 只有 Add/Observe，没有 Remove；`NativeTweenSourceReferences`、reader 与 admission 的 process registries 都只增加。成功退休若只移除 WorkOwner，旧 hub 仍会调用旧 reader，重新验证/扩充本 run 根。
2. Notify 在判断 reader relevance 之前为 notice 的全部非 null identity Root。这是现有借用安全策略；不能为了退休先解引用未 Root 的裸 journal 地址。新增兴趣过滤只能在真实 borrowed callback 内，以 scalar identity/role 和已登记 source token 决定是否需要 run leases。全局永久 observer 可以保持 rootless，但无活 reader 的成功 scope 必须释放它的临时根；其它 reader 仍需要的根必须保留。

这里要区分两个实际交付：**RunBorrowersWithdrawn** 可证明该run已从永久hub退出且其自有根可回收；**HubOwnedRootsReclaimed** 才证明hub持有的对应实际GC handles已free。若本批选择保留全局hub永久根，最终receipt必须明确记录“借用已移交全局process holder，process永久根仍存”，不能声称P4要求的全部观察根归零。这个process holder和transfer receipt必须真实存在且可验证，不能用root数未纳入统计隐藏留根。保持permanent callback/dead policy不要求保持每个游戏对象的GC root永久。

现在 `RootedReference.Root` 根据 `_owner._roots[raw.Value]` 重新查找。允许释放/地址复用后，必须改成捕获 immutable RootRecord generation；旧 receipt 不能通过同地址新记录“复活”。root-set 的 Ready/HasRetainedObject、OperationRootAnchor、旧 Receipt.VerifyIssuedBy 也须理解 Retired/Disposed，不得让冻结前后身份表混用。

## 4. native 完整原子排空缺口

现有 `Unregister` 的五入口 gate 有价值：Play/Add/Execute 的 owner entry reservation 与 Seal 用同一 SRW；`OperationContext::Dispatch` 先增加 `inFlight`，再进 admission。它能拒绝当时可见的 busy 五入口，成功后清掉 owner 裸地址，永久 exact-class dead policy 继续存在。这不是完整 P4 切面，原因如下：

- `FiveLanesState` 仅 Void0 25/24/23、Reference1 20/19。Single1 slot 31 未包括。
- 五入口 GetInfo 逐个采样；Update/Awake 不获取 admission owner reservation，计数递增也不持 admission SRW。采过为零的某一 lane 可以在其它 lane 采样后进入。
- native entry 在 `Snapshot(state)` 后才进入 Dispatch 计数；CLR `EnterOperation` 也在取 observer snapshot 后才增加 managed context 计数。退休 gate 必须覆盖这类尚未发布借用的入口窗口，不能只看当前 `_top`。
- source context 的 `active`（wire 名 ActiveUpdates，实际包含 Play/Execute）、`pendingCount`、journal epoch/events/transactions，受 `TweenSourceContext.cpp` 的独立锁保护。
- reference active/retained scopes、callbackInFlight、observer epoch、delivery watermark，受 `TweenSourceReferences.cpp` 的另一把锁保护。Source 在发布 references 时显式 Unlock，再进 CLR，再 Relock；不能持这些锁回调 CLR/调用游戏/Root/free。
- backend ReadActivity 合并 managed + native 的五入口与 typed activity；不包含 source pending/ref callback 的完整原生区间。Death source 的两次 Poll/activity/sequence 夹读是具体观测的防变检查，不能变成最后 free 的原子承诺。
- 一个 manager 是共享源，hub 也跨 run。禁止为退休一局全局关掉其它 run/foreign UI 的观察或原游戏执行。

### P4-B 必须提供的 native rundown 合同

新增共同的 retirement gate/epoch，或等价且经竞态 fixture 证明的协议；仅新增一个聚合 GetInfo 不够。最低合同如下：

1. 在实际入口选择 observer/创建 owner borrower **之前**，在共同 gate 注册 rundown reservation；覆盖五入口、Single1、source contexts/pending/relay 发布和 synchronous-reference scope/delivery。出口按原 ticket 解除。计数、overflow/worker/异常不可伪归零。
2. 对本 run 关闭新的观察 borrower enrollment，保留之前已捕获 context 的 Complete/Dispose；已经封口且确死的 Play/Add/Execute 仍由永久 policy 处理，foreign/其它 run 的原始执行和独立订阅继续。不能持 gate 等 CLR 完成或暂停主线程等自己。
3. 最后一个旧 borrower 退出后，由同 gate 原子取得不可复用的 drain token。绑定 helper/module/publication identity、admission configuration epoch、全部真实 lease slot/epoch、run observer registration epoch、source epoch/journal cutoff、reference observer epoch/delivery cutoff及完整已关闭集合。Pending 与 sticky Unknown 有明确不同结果。
4. 用同一 token 验证并 commit 全部 source lease 的 unregister。建议增加“先验证全组、再全组清 address”的 native batch，复用现有 Unregister 内部 exact-dead/Retired 动作；这样第 k 个失败不会留下半组已退休，而后续 death verifier又要求所有 lease Sealed 的陷阱。若仍逐项注销，必须有真实 partial-commit账本，后续校验只读已返回 receipt，不再 Read/Unregister 已 Retired slot，任一未知结果禁止 free 全组。
5. commit 和最终释放之间 owner enrollment 保持关闭；token 只消费一次，旧 token/新 epoch/新 slot 复用皆拒绝。新增 callback 若不能借本 owner，应明确走保留全局永久 infrastructure 的无 owner 路径；不能访问已放弃的 root-set 或重新为它登记根。

实现锁序前应先列出现有 admission→operation info、source Unlock→references/CLR、CLR→source/admission 的调用图。共同 gate 只保护 reservation、epoch 与无回调的数据提交；不能用嵌套三把现有锁并在里面调用 CLR 的方式“实现原子”。

## 5. 必须补齐的 WorkEnded 事实

P4-A/P4-D 新增 `NativeLocalTweenWorkOwner.Completion.cs`。只由这个真实 owner 在 coordinator 的当前 Return 事务中，同步借出一次私有 CompletionInterval；不能有公共 `Complete(bool)`、snapshot constructor 或外部可保存的“票据后补”。

interval 必须同时覆盖：

- exact owner/backend/launch/coordinator、source set version、真实 admission lease 集合、所有动态 Tween/Manager ownership anchors；没有漏掉后来加入的来源。
- 每个 owned 非 instant occurrence 已按实际索引退出；没有当前 scheduling alias、transient、move、source pair。foreign occurrence 只约束重放完整性。manager/Awake/resize 历史世代须有实际 field alias 已断开且借用 frame 排空的凭据，不能要求无调度权的旧数组残影全部清零。
- 每个 InstantWork 由实际 Play outcome 和它真实发行的 child effects 收敛；不能因为 `QueueExited` 恒 false 永远卡住，也不能直接把 Play Returned 当结束。
- 每个操作已 Complete/Dispose；正常 source commit 或经核实没有任何 source steps 的 Skipped；foreign child CALL 都按原 source parent配对闭合。所有 typed context 正常返回并 Dispose，异常/Failure 保 Unknown。
- zero-arg child effects 消费真实现有 empty receipt；非空 listener、child coroutine、动态 Tween、scene/engine task 必须由相应实际 owner 接受独立责任并给 private transfer/completion receipt。父 obligation 结束不等于被移交的任务已全局结束。
- onTween 的实际 complete executing-list 和 listener generation 需要接入 typed listener effect owner；同时将 actual Execute/Play typed invocation关联到当前 occurrence 或独立 InstantWork。当前 typed Snapshot 只有 Tween 与 operation sequence，遇相同指针多个 occurrence 不能猜。
- 具体 native setter 分支用确切 target/method/call contract 记录责任；同步 setter 的真实返回只能完成明确验证的同步动作，不能把未知派生效果收掉。目标变更、层级重挂与 unknown setter 必须保持责任或真移交。
- 除已有 TweenInfo death receipt 外，P4 所要求的实际 target Component、GameObject/Transform 需要各自 held root 和 live→later-dead证明，或者由真正接收其存活责任的 owner完成转移。现有 backend 只读/Root targetComponent 与 curve，没有这套具体对象死亡覆盖；factory 五对象 receipt 不能按名字或 hierarchy 换用。对同一个实际对象的多个角色/多个 handle，需要明确 alias/holder关系。
- journal 已读至 native token 的最终 cutoff，没有漏行、sequence gap/epoch drift、native pending或reference scope；仍 rooted 的全部最终 graph与截止记录一致。

现有 `ReadListenerWriterSources()` 的 FutureEventSourceMembership 永远 Pending；其它 adopted event 的 serialized/direct/generic/external writer也明确 Pending。Tween 事务不应等整个 listener ledger 的全局 closure/retirement来生成自己的票，否则仍形成环。它必须只消费本次 Tween child实际 effects receipt，处理本 Tween lender根/观察者退出，把 listener backend自身的独立 roots/未来 writer职责保留给 ledger。整局 verifier继续单独检查完整 UnityEventConsumers，不能因 Tween receipt 提前放行。

## 6. 最小但完整的事务与 API 改动

以下是实施合同，不是现成 API。建议新增 `NativeLocalTweenRetirementTransaction.cs`，所有动作由主线程真实 pump 驱动。

| 阶段 | 具体生产动作 | 暂缓/失败时必须留下什么 |
|---|---|---|
| Requested / Sealing | 核真实 Return并封完整 lease 集合；busy Claim在同根上推进；所有动态source join禁止新增。 | 未 claimed/sealed、活工作正常继续；pending有原因，不能当 Unknown。 |
| Draining / AwaitingProof | 继续五入口/source replay与具体死亡/effect观察，真正建立第5节的完整集合。 | 每个未退出occurrence/target/effect仍由原owner持有。 |
| Prepared | 冻结完成 interval、root manifest、source cutoff、death/effect/transfer receipt身份。所有需要 root.Target 的验证到此完成。 | source set或任一generation变化使本interval作废；尚未发free权限。 |
| EnrollmentClosed / Withdrawing | 经共同rundown关闭本run新borrower；逻辑Dispose五入口+typed binding、source reader subscription；已捕获context继续真实退出。 | 必须保留全部根；本run不重新开户；其它run继续。 |
| NativeCommit | 取得第4节token并同步checked Unregister全组；验证Retired完整tuple与零活动；保存原始native receipts。 | 已定义no-mutation busy可重试取新cut；未知结果sticky Unknown；不得free。 |
| ReferencesReleased | 在受核observer cutoff内放弃rootset/operation/ref-hub borrowed leases；hub last-holder才free其实际owned roots。 | 当前callback/scope/其它reader还持有则pending；不要全局detach共享hub。 |
| FreeAttempted | 逐holder实际free一次：independent admission、source own roots、Tween backend own roots、新增death/effect own roots；共享handle先放弃借用再由唯一holderfree。 | 每slot先写attempt再调free；throw时保留record+错误且不再重试，不发最终票。 |
| Retired | 只读冻结facts与native rundown状态核本holder自有root已free、全部borrow已退出或由真实外部holder接受，移除本run相关registry与hub reader，发私有最终receipt。 | 永久class policy、detour、native/managed callback delegate仍由process owner保活，不卸载；hub永久游戏对象根若未回收则另列实际process责任，不能宣称它们已free。 |

Runtime 必须新增的最小动作：

- Admission：由同一个真实 registered Owner 同步借出 interval；校验authority/lease引用/slot/epoch/source集合，调用native checked commit，专门验证 `Retired` tuple（现有 ReceiptMatches不能复用），保留独立 `NativeRetired`、`FreeAttempted`、`FreeSucceeded`、`SharedBorrowReleased` 状态。不能把普通 Read 的允许状态直接扩成接受Retired后继续读root。
- Source reader：冻结最终watermark、关闭reader enrollment、从hub退订、撤销rootset/anchor可解引用权限；释放初始own roots并放弃borrowed handles；从 ProcessOwners/sets移除。冻结后ReadAvailable/VerifyRoots必须拒绝，或返回标量closed诊断，不能继续读root。
- SourceReferences：真实scope/reader leases和RootRecord generation；允许last-holder一次free与scope history标量化，防地址复用；保留permanent attach delegate，不增加可任意替换native observer的接口。
- Backend：4096个slots的holder清单和once-free，6个logical bindings清单（五入口+typed）、retired diagnostics；完成后RootRecords是本holder的0，另列shared borrow/其它owner transfer，不能继续拿历史非null entry合计。
- Effects/death：仅同owner当前interval可消费现有private票；commit后使用冻结receipt identity，不再调用会读sealed lease/root的VerifyCurrent。listener lender关系需private transfer/frozen result，避免ledger之后再解引用已free的Tween membership。

`NativeRuntime.RootHandle.Dispose()` 在调用 native free 前已把 `_id` Exchange为0（`NativeRuntime.cs:393`）。因此free throw后无法用同handle重查Target或重试；事务必须留下“已尝试、结果未知”的明确事实，保留出错记录，不以再次Dispose无操作充当成功。

### P4-E coordinator 接线与已退休后的行为

`NativeLocalRunLaunchCoordinator.Tick()` 的普通路径（235）与 frozen factory路径（224）都已调用 `PumpTweenWork()`，可复用调度位置。新增专用退休pump应在native source安全时推进上述事务，且在active消费者Close之前得到Tween私有票。

`TweenWorkCanRetire()` 最终只能验证同owner/同launch的实际 `TweenOwnerRetirementReceipt`。不返回 `TrySeal()` 或summary bool。`NativeLocalObserverRetirementTransaction` 当前要求已Closed的factory context且root slot上限2048；不能直接套到4096+128+source/hub持有者，也不能调用它来生成这个Close的前置票。

成功退休后，`PumpTweenWork()` 不能再次 `Tick → Refresh/Read lease` 或 `PumpActualTweenDeaths`；`NativeLocalTweenWorkOwner.Read()` 不能继续调用现backend.ReadActivity/RootRecords，也不能让 `ReadSourceReplay → child.Snapshot` 沿旧membership回读root。建立只含冻结scalar history、retirement result和安全process活动的终态分支。旧Membership/DeathReceipt/OperationRootReceipt的使用拒绝；private final receipt按owner/launch/transaction身份可重复验证，但不能重复free或再注销。

## 7. 正向与反向验收矩阵

本轮没有执行下列测试；这些是实施时必须实际新增的验收，不能复用此前typed/source/death通过数宣称完成P4。受控语义fixture与真实native helper fixture必须分开标明，二者都不运行游戏。

| 用例 | 输入与真实动作 | 必须核到的结果 |
|---|---|---|
| T01 完整正向 | 实际受控backend/source reader/private contexts产生两条重复Tween occurrence、正常搬移/清除；真实Return/seal；具体Tween+target晚frame死亡；空子事件effects闭合；checked native Retired；所有holder实际free。 | 可消费一次final receipt；每个真实handle恰好一次free，borrowed无free；native owner object清空、per-run registries与hub reader退出。必须存在成功路径，不能只测始终false。 |
| T02 admission状态 | pending Claim status6、seal closing、issuer/entry在途、活且sealed的旧任务继续。 | pending不发票/不free；同根重试；live工作照常跑；Unknown/duplicate Claim不重试。 |
| T03 occurrence精度 | 同Tween指针两个queue admission，只退出一个；ready transient；pendingcopy；未配对source；wrong index/final capacity不同。 | 只有真实完整退出集合可通过；单个Return/isOver/数量归零不能蒙混。 |
| T04 Instant/Skipped | 独立instant Play，含已发行子效果；合法无source Skipped；Skipped却有source steps。 | instant独立结束契约；合法Skipped不要求Committed；矛盾Skipped保Unknown。 |
| T05 source/manager世代 | Awake/queue resize造成旧field alias断开，但旧数组残留managed pointer；同时另一个旧frame仍在借用。 | 有真实alias断开+frame退出才可退休历史观察根；不要求无权旧数组清零，也不提前放根。 |
| T06 death | wrong root实例、同地址新native/class/epoch、Return同frame、只有一次dead sample、target仍活/重挂、dead→alive；不再Pump但native lease CachedNative复活。 | 拒绝或pending；Tween票不能替target/factory对象；旧death票不能跨epoch或借上次cached0使用。 |
| T07 effects | zero-arg真实empty-list完成；Single1空/非空、unknown/multicast listener、child coroutine/newTween/scene task、setter目标改变。 | 真正empty或真实transfer/completion才能结束；onTween Returned/Dispose、listener移除、scene卸载均不足。 |
| T08 原子切面 | native fixture用barrier精确停在Snapshot之前/之后、inFlight增加前、各五入口采样之间；Single1独立在途；source Pending/发布Unlock/refs callback在途。 | 新共同协议不得发行drain token/Retired/free；活动恢复后才成功。只测两次idle相同不合格。 |
| T09 token身份 | wrongrun、foreignbackend、copy DTO、重复consume、stale source/ref epoch、journal cutoff落后、slot退休后新epoch复用。 | 首次原生变更前拒绝；native slot和free计数不变；不能恢复旧receipt权限。 |
| T10 关闭后的调用 | withdrawal之前已捕获context晚Complete/Dispose；关闭后又Play/Add/Execute/Single1/refs；其它reader仍活。 | 旧context真实排空，新callback不借retired根；永久dead policy仍跳过确死源；foreign/其它run原调用和订阅不受破坏。 |
| T11 native注销异常 | batch中第k项busy、wrongtuple、statusunknown、call throw；或逐项模式前几项已成功。 | 定义pending/partial记录；所有root留存；已Retired不得重读sealed death或重注销；未知结果不发票不free。 |
| T12 shared/own roots | 一个backend handle同时admission shared + operationanchor；同object不同独立handle；hub两reader共享同root；scope在途。 | 真holder一次free；其它reader仍安全；最后scope/reader退出后hub实际free；RootRecords不冒充free清单。 |
| T13 地址复用 | hub旧RootRecord退休后同地址在新native scope重新出现。 | 新RootRecord generation；旧RootedReference/receipt/rootset拒绝借新handle；不复活旧owner。 |
| T14 free异常 | 第k个真实nativefree throw、free后验证又尝试root.Target、重新进入退休。 | attempt记录/Unknown持久；不重试该free；不读已free根；不发最终票，保留其余具体责任。 |
| T15 coordinator与配额 | 普通及frozen路径各完成一次；成功后多frameTick/Read；连续超过32个真实退出run；observer ledger独立未闭合。 | Tween先独立完成且无Close循环；终态不native读根；owner/lease/source配额回收；UnityEvent/global/scene未闭合时整个run仍pending。 |

可复用回归位置：`NativeTweenSourceReplayTests.cs`、`NativeTweenDynamicSourceTests.cs`、`NativeTweenDeathTests.cs`、`NativeTweenTypedEventTests.cs`、`NativeTweenChildEventTests.cs`、Runtime `NativeTweenSource*Regression.cs` / `NativeTweenDeathRuntimeTests.cs`；native `fixture/TweenAdmissionFixture.cpp`、`TweenSourceFixture.cpp`、`TweenSourceReferencesFixture.cpp`。现admission fixture已证明live/in-flight阻止单项Unregister与Retired slot新epoch，但没有source/Single1/ref原子切面或managed root释放成功路径。

建议新增专用 `NativeTweenRetirementTests.cs`、Runtime checked-retirement regression及native aggregate-rundown fixture，统一写到 `D:/riz-build-cache-next/tween-retirement-...`。按fixture给的实际export及当前测试注册运行，不改包/安装脚本，不把测试计数代替具体nativefree/Retired/registry证据。

## 8. 实施顺序与完成边界

先完成root provenance/borrow leases与可冻结的WorkEnded/death/effect事实，再实现共同native rundown和checked注销，最后接逐holderfree、coordinator和final receipt。可以先写正反fixture，但任何中间阶段都保持 `CanRetireWork()` 不授予成功。

可以直接分工的修改包如下；这是依赖划分，本审计没有创建新代理或修改它们的生产文件。

| 修改包 | owned文件建议 | 首次必须实际产出 | 依赖/完成前不得声称 |
|---|---|---|---|
| A：native共同rundown与注销 | 新 `native/RizToolbox.JudgeContext/TweenRetirementContext.*`；root协调接 `OperationContext.cpp`、`TweenAdmission.cpp`、`TweenSourceContext.cpp`、`TweenSourceReferences.cpp` 及当前publication | 六lane+source/ref真实reservation、不可复用cut token、checked全lease集Retired、barrier竞态fixture。 | 独立完成T08/T09/T10/T11；不得只扩FiveLanesState采样或破坏当前完整native body/unwind/publication封印。 |
| B：Runtime admission释放 | 新 `Hooks/NativeTweenAdmission.Retirement.cs`（先将当前非partial类改partial，或同文件实现）；专用Runtime回归 | 绑定真实Owner的同步interval、Retired validator、own/shared state与native receipts、独立claim root once-free。 | 与A合同一致；不能从AdmissionSnapshot/外部bool发行权限，不能free SharedRetainedProcessRoot。 |
| C：source reader/hub借用退休 | 新 `Hooks/NativeTweenSource.Retirement.cs`、`NativeTweenSourceReferences.Retirement.cs`，协同 `.DynamicReferences.cs` | 精确run退订、frozen watermark、initial own roots释放、operation/ref借用退还、global RootRecord generation/leases或真实永久process移交票。 | 先明确每handle holder；T12/T13通过；不得将一次run退出变成全局hub detach，或将永久根漏出receipt。 |
| D：实际effects与具体死亡 | 新 `AudioContent/NativeLocalTweenWorkOwner.Completion.cs`、setter/typed effect partial、target death partial；协调现child ledger | owned occurrence/InstantWork私有结束票、typed完整listener效果来源、真实target死亡/移交；复用zero-argempty票。 | nonempty/multicast/unknown listener没有真实接受方时继续Pending；这包未完成，后端root事务即使能跑fixture也不能生产成功退休。 |
| E：Adapter holder事务 | 新 `NativeLocalTweenRetirementTransaction.cs`、`NativeLocalTweenBackend.Retirement.cs`、owner completion/retired diagnostics；新 `NativeTweenRetirementTests.cs` | 本run完整root manifest、关闭enrollment、实际nativecommit、逐holderfree、Unknown无重试、finalprivate票。 | 消费A/B/C/D实际结果；必须T01正向真free，不能拿controlled bool模拟整条生产授权。 |
| F：root最终接线 | `NativeLocalRunLaunchCoordinator.TweenWork.cs`、`.ActiveRetirement.cs`、`.ActiveOwners.cs`、main Read/Tick | 普通/frozenpump独立完成Tween；Close前只消费最终私有票；成功后停止root/nativelease读取。 | 保留UnityEvent writer/engine/scene/global独立 obligations；T15证明无Close循环、无late read。 |

最终交付至少应同时包含：真实native Retired tuple；一次性的private completion/drain token；own/shared完整root manifest及每次free结果；source/hub退订与late-callback拒绝；终态只读冻结诊断；普通/frozen两条正向pump；T01/T08/T12/T14/T15的实际通过证据。P4从“不可退休”变成真实成功后，其它UnityEvent writer、engine/scene/global消费者仍按各自私有票验收，B01–B19继续保持独立未完项。
