# Single run 订阅退出后保留实际 parent 的同步清理

2026-10-30，本包已完成 run-owned Single 订阅的退出与清理接线。订阅 Dispose 阻止新 parent 借用，已经从实际 Operation Capture 借出的 parent 保留其回调、fault 和 Exit/Dispose 责任。选入同次 callback snapshot 的 peer 不会因逻辑退出被跳过；已闭 parent 的重入不能新增 callback。正常清理完成后，永久 observer 的 hub 清除 run 委托，真实 operation 桥与 broker 的 GC 检查也确认结束的回调捕获状态可以回收。回调、Capture、Complete、Dispose 任一不确定都会保留 Unknown/Pending，没有新增效果完成票。

本次实际通过 65/65 Single 检查、8/8 独立 native operation 回调子进程和 17/17 旧 typed/completion 定向回归。范围与授权见 [scope](scope.md)。这是受控同步清理验收，未执行游戏，也未正向执行 Single producer ReadCurrent 与生产对象后端初始化；private parent scalar core 的正向观察使用 reflection。全 B01–B19 集成验收由 root 对统一冻结另行执行。

## 可重现的冻结结果

正式输出保存在 `D:/riz-build-cache-next/single-subscriptions-frozen-r1-20261030/`。其 `passed=true` 为最终交接冻结；此前成功诊断目录单独保留。

| 项目 | 实际结果 |
|---|---|
| 依赖基底 | foundation-single-v7-core-r1-20261029/source-snapshot，622 编译输入，manifest SHA `fb90ef1765ca76ea71192899a19a7c23f45bdf40859c43608050551617e95890` |
| 本包覆盖 | 17 个完整 owned 文件及 Retirement 两方法，overlay manifest SHA `9d36c65efb5c16425f0e3a18badb2188d50a72c1db9d5496236014385ac0648b` |
| 最终源码 | 626 输入，before/after SHA 均 `b8046d6d8e23e924f7b0f2d5af98ba6f7e53fa1af81f52426e64efe43621cc9f` |
| 源码 ZIP | SHA `388649d9f4aca29f11ec6da27b80399aca38d744a01b3a90542f973f4588a8c9` |
| SingleEvent build | exit 0；生产 Runtime/Adapter 0 警告，既有共享 fixture 26 条 CS0649 |
| SingleEvent 普通进程 | 65/65：42 现有检查 + 23 新实际 hub 委托/Scope 检查 |
| SingleEvent native 子进程 | 8/8；14 次真实 native original，36 次被计数的 managed Capture/Observe/Complete/fault 回调；原生 Enter/Exit/in-flight/故障计数逐模式独立核对 |
| AudioContent build | exit 0；19 条既有 fixture/nullable 警告 |
| AudioContent 精确定向 | 16 条 typed + 1 条 completion 反例，各自独立子进程，17/17 exit 0 |
| 固定 helper | SHA `30ec8ed7245432ff81490cc346fb13d03dca060bb0ef9575f90e2a3078779bde`；只读加载，前后不变 |
| test-result | SHA `a966aec6cd9a809f9d8941bb91b68207ee441c61f0f25b3e6c04658dfa8462b7` |

复现命令：

```powershell
C:/Python314/python.exe docs/validation/foundation-single-subscriptions-20261030/run-checks.py --output D:/riz-build-cache-next/single-subscriptions-replay-20261030
```

需要当前 Windows x64 .NET SDK，本次实际为 8.0.418；还需上述已保存基底和 helper。输出目录必须不存在。脚本从固定基底复制源码，再仅覆盖 [overlay-inputs](overlay-inputs.json) 所列源码；Retirement 只替换两个 owned 函数，其余字节来源于基底。所有构建输出到 D 盘 fresh artifacts；不消费工作区 bin/obj、旧测试 DLL 或并发模块的中间状态。完整命令、stdout/stderr 哈希及每个结果见 [test-result](test-result.json)；源码清单见 [source-before](source-before.json)、[source-after](source-after.json)。

## 生产接线和责任

1. `BindSingleExecutingSource` 保存 `_singleEventSubscription`，其闭包属于该 backend/run。永久 `NativeTweenSingleEventBridge` 只持有订阅 hub，不再持有永不退出的 backend observer 列表。
2. `ValidatedCapture` 先做 scalar event identity 预筛，再从实际 `Binding.CaptureSingleSubscriberParent` 取得私有 issuer。它核 Capture 窗口、当前物理 Operation、选中的 sink、native Single1/slot31 身份和真实 managed token；`BorrowParent` 在第一条 rooted membership/metadata 读取之前登记责任。复制 DTO 无法发出该 authority。
3. 订阅 Dispose 原子移出 active 集合并清除订阅闭包；`Begin` 持同一 gate 拒绝退出后或已经 Unknown 的订阅新借用。已借 `ParentLoan` 保留自己选定的 Observe/Fault。Capture authority 的后续 current 核验不再要求 sink 继续 Active：已选 Capture 在退出中仍拥有该 parent 的清理责任。native parent/token/Capture 窗口仍必须保持当前。
4. `Dispatch` 在调用任何 observer 前为完整 parent snapshot 预记 callback 在途数。一个 callback 关闭 parent 或退出 peer，不能删除这次已经选中的 peer；之后的重入不再选入闭 parent。每个已选 callback 的异常都会置 subscription Unknown，并调用该 loan 保存的 fault 委托。原有 Complete/Dispose 仍执行一次。
5. `NativeTweenSingleEventScope` 将真实 entry frame、inner context 与 loan 绑定。Complete 只接受同一实际 frame 的一次 Exit；Dispose 只尝试 inner 一次。没有确认 Exit 的 Dispose 保留 capture responsibility，失败不变成已排空；重复 Dispose 现在同样将 subscription 标 Unknown。
6. `WithCurrentSingleContext` 的物理 parent 核验位于 live `Delivery.VerifyCurrent` 之后，foreign/nested 直接父遮蔽祖先。为测试抽出的 `CurrentSingleContext` 是 private scalar core，不能提供 native object reader 权限。生产中，一个已被选中的订阅若丢失实际 context，现在会抛出 source fault；不再忽略返回 false。
7. `WithdrawTweenObservationBindings` 先退出 Single subscription，再撤 Single operation 与其它 Tween sinks；`VerifyTweenCompletionBindingsWithdrawn` 要求该 subscription 确实 withdrawn、callback/loan 为零且 Known。退出重入不能在这段期间新增 Single loan。实际已借 parent 的原生快照仍通过 broker 保存的 contexts 完成。
8. Returned/Abnormal child 完成分支在新根和 metadata 读取之前，只处理既有 Before 责任。WorkOwner 的 Unknown 后不启动 journal 新读取；typed effect 诊断仍保持 Pending。没有变更 native、pack、FirstInstall、根释放发行者或 RequestPlay 准入。

## 实际 callback 覆盖

23 项新 managed 检查调用生产 hub 的真实委托和实际 Scope.Complete/Dispose，覆盖 idle/borrowed 退出、独立订阅、重复借用、foreign key、self/peer 退出、已选 peer 抛错、closed 重入、nested scopes、两次 run、Active/Unknown 禁止新借用、capture/consumer fault、错 frame、重复 Complete/Dispose、无 Exit、GC 断锚，以及在 callback 内挂起后从另一线程退出/完成 parent 再恢复。挂起状态确认 in-flight=1，恢复成功后才排空；恢复后抛错则保持 Unknown。未以 DTO 值校验代替 callback 执行。

8 项原生子进程在各自新进程配置 helper 的闲置 Single1/slot31，保留两个互异 CLR 函数指针作 target identity 和 original，随后直接调用真实 native entry。native `Enter` 调用生产 `NativeBootstrappedOperationBridge` 和 `ExactHookBroker`，实际 Capture 取得 Single issuer、创建 hub loan 与 Scope；native original 内实际调用已选 managed observer；native `Exit` 执行同一 broker 的 Complete 和 context Dispose。没有 Dobby publication 或游戏 detour。这是实际 native operation 到 CLR 的同步调用，**不是 Single producer → Bridge.Notify/ReadCurrent 的正向调用**。

| 模式 | 实际验证 |
|---|---|
| withdraw-original | original 内退出订阅和 binding；原 snapshot 仍观察两次，Exit/Dispose 各一次；下次 native original 继续执行且不再读旧回调 |
| withdraw-capture | Begin 后在 Capture 内退出；已有 authority 继续 current，禁止新 Begin；嵌套无 observer original 返回后恢复并完成旧 parent |
| nested-foreign | foreign 与 owned nested parent 都遮蔽 outer context；nested Exit 后 outer 精确恢复；两个 owned context 各完成/释放一次 |
| two-runs | 同一 receiver 下不同 native sequence/managed token；两次 run 共享一个永久 observer，旧 key 不触发旧 run |
| observer-fault | 退出后已选 observer 抛错；subscription 与 broker Unknown，真实 Complete/Dispose 仍各一次 |
| completion-fault | consumer Complete 抛错后 context cleanup 仍尝试一次，状态不报 known/drained |
| context-fault | consumer Dispose 抛错只尝试一次，fault 责任保留 |
| collected-run | withdrawn+completed 的回调捕获 run 状态经真实 GC 回收；native bridge/broker/hub 继续存在，之后 original 仍运行 |

GC 检查的动态对象为受控 run state，并未初始化生产 NativeLocalTweenBackend 或游戏 roots。生产 backend 的闭包清除路径由源码接线、同一 hub/Scope 的真实委托检查和这条实际 broker 生命周期共同支撑。23 项与 8 个子进程有不同证据边界，未将它们合计称为完整产品矩阵。

## Evidence → Finding → Path

| Evidence | source_ref / artifact_path | hash / repro / raw_excerpt |
|---|---|---|
| E-base | [base-inputs.json](base-inputs.json) | SHA `fb90ef1765ca76ea71192899a19a7c23f45bdf40859c43608050551617e95890`；repro 为上面的 run-checks 命令；622 frozen inputs |
| E-source | [overlay-inputs.json](overlay-inputs.json)、[test-result.json](test-result.json) sourceWiringChecks | SHA 与上表一致；17 files + 2 methods；7/7 静态生产接线检查，非动态 native Delivery 权限测试 |
| E-managed | [logs/SingleEvent.00.stdout.log](logs/SingleEvent.00.stdout.log) | 文件 SHA 见 index；同一 repro；`65/65 Single managed source checks passed.`，其中23项为新实际委托/Scope |
| E-native | [logs/SingleEvent.01.stdout.log](logs/SingleEvent.01.stdout.log) 至 `.08` 的8个独立日志 | 逐文件 SHA 见 index；同一 repro；每条 `passed=true`、`nativeObserverAttached=1`、`nativeInFlight=0`，且 `actualSingleDeliveryReadCurrentVerified=false` |
| E-regression | [test-result.json](test-result.json)、[logs/AudioContent.16.stdout.log](logs/AudioContent.16.stdout.log) 及0–15日志 | 逐文件 SHA 见 index；同一 repro；17 个实际 `1/1`，包括没有实际 mid/list 的 completion Pending 反例 |

所有 Evidence observed_at=2026-10-30T02:22:33.612587+00:00，source_type=file/log；linked_workitem=Single subscription exit cleanup；supersedes=none。详细固定路径与各 content_hash 见 [evidence-index](evidence-index.json)。

F-subscription：severity=n/a_re，category=design，status=validated，confidence=high，evidence_ids=[E-source,E-managed,E-native]。location=NativeTweenSingleEventSubscriptions/Scope/PhysicalParent。impact=退出不再剥夺已选 callback 或实际 parent 的完成责任，同时禁止新借用。repro_steps=运行本包脚本，核 withdraw-capture、withdraw-original、selected-peer、suspended 两类结果。remediation=已落实本报告责任规则。residual_risk=生产 native Single producer ReadCurrent 正向仍由独立链路验证。

F-lifetime：severity=n/a_re，category=design，status=validated，confidence=high，evidence_ids=[E-source,E-managed,E-native]。location=subscription.Dispose、ParentLoan.TryRelease、retirement withdrawal。impact=正常结束后永久 hub 不保留 run 回调；Unknown 或未确认 Exit 不会被清零为成功。repro_steps=核 collected-run、permanent-hub-GC、fault 和 once-only 检查。remediation=已实施精确快照清除与 drained 门槛。residual_risk=动态 GC 对象为受控回调 run state，非完整生产 backend/游戏对象初始化。

F-pending：severity=n/a_re，category=design，status=validated，confidence=high，evidence_ids=[E-source,E-regression,E-managed]。location=WorkOwner.TypedDispatch/TypedEffects 与实际 completion 反例。impact=Return、Dispose、空 list、异常后的同步清理均不能发出效果完成票；真实 effect end/transfer/writer closure 未知继续 Pending。repro_steps=核 AudioContent.16 与现有 Single empty/missing-post 检查。remediation=n/a，本包保持该闭合边界。

P-single，path_type=callflow：start=run backend subscription；goal=阻止新 parent 借用并保留实际旧 parent 的同步责任。steps=(1) 实际 Operation Capture 私有 issuer → Begin 预记 loan，E-source/E-native，F-subscription；(2) subscription Dispose 移出 active，已借 callback/fault snapshot 保留，E-managed/E-native，F-subscription；(3) 当前物理 parent scalar 关联后执行既有 callback，foreign/nested 遮蔽祖先，E-source/E-native，F-subscription；(4) actual Exit → Complete → 一次 Dispose，已选在途结束后清除闭包，E-managed/E-native，F-lifetime；(5) independent effects 尚无 end/transfer 继续 Pending，E-regression，F-pending。residual_risks=实际 Single producer Delivery 正向、游戏 metadata 与效果最终闭合均不由本包冒称完成。

## 交接与诊断

交接的生产文件严格限于 [scope](scope.md) 和 overlay 清单。root 无需重做此包测试，可将它们纳入统一 fresh 全树冻结再跑整套；旧 full25/2233、release-r1 和 adoption-r2 不代表这批新源码已经通过全矩阵。

早期 r1/r2 live 定向构建和测试成功；r3 live 遇到并发 GameAppearanceService 的 5 个编译错误，随后按 root 指令换到精确固定基底，并取得上述完整成功结果。没有修改或掩盖那些非 owned 错误，也没有把失败的 live build 列作成功冻结。时间线见 [timeline](timeline.md)。

文档使用 docs-generator 的普通技术报告最小结构，flavor=null。Scope、可复现命令、E/F/P、哈希和边界均已核对；本任务没有恶意样本/IOC/ATT&CK，不套用恶意软件报告。没有游戏、GUI、真实安装或推送。
