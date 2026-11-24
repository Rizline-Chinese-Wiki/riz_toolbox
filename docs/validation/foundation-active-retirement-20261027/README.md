# Active 退出、取消与逐 owner 退休证据（2026-10-27）

当前新增实现已通过冻结快照受控回归：**Audio/content 796/796、Runtime 协调器 28/28、对象身份 10/10**，最新父记录为 core r5，500 项声明输入与源码 ZIP 逐项一致。另有三个真实自有取消联合进程，以及三个 V5 冷启动/CLR 采用进程，各自范围内通过。

这些结果没有证明真实游戏已执行 `ResultBack → Return → Close → 全部 owner 释放`。三个证据组没有串成同一个实际退出流程；不得将它们相加，签成生产 `EngineCancelled`、整局退休、生产启动准入或 B01–B19 全覆盖。任意 listener 产生的未归属副作用仍保持 Pending。

## 范围与原件

沿用[授权范围与游戏只读约束](../foundation-engine-production-20261025/scope.md)。本报告复核已有日志、JSON、ZIP 和源码，没有编译、运行新测试或操作真实游戏。父代理统一执行构建与夹具；报告保留失败历史，不改写旧记录。

| 证据 | 原件 | 可签的范围 |
| --- | --- | --- |
| E1：最终受控回归 | [core r5 父记录](D:/riz-build-cache-next/engine-active-retirement-core-r5-20261027/test-result.json)、[实际子记录](D:/riz-build-cache-next/engine-active-retirement-core-r5-20261027/checks/test-result.json)、[500 输入 ZIP](D:/riz-build-cache-next/engine-active-retirement-core-r5-20261027/source-snapshot.zip)、[Audio 日志](D:/riz-build-cache-next/engine-active-retirement-core-r5-20261027/checks/audio-tests.log) | 本轮真实源码的编译与受控账本、身份、责任算法回归；包含 listener 缺口修复 |
| E2：修复前历史通过 | [core r4](D:/riz-build-cache-next/engine-active-retirement-core-r4-20261027/test-result.json) | 795/28/10；含 B13、冻结死亡源7、通用退休事务5；不包含新增 listener 负例 |
| E3：真实自有取消联合 | [joint cancel r2](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/test-result.json)、[normal](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/normal.json)、[missing-source](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/missing-source.json)、[wrong-generation](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/wrong-generation.json) | 真实 native L/M/N、Stop scope、CLR 精确终态判定与自有来源对象封口；不授游戏取消权限 |
| E4：V5 真实采用 | [owned managed r4](D:/riz-build-cache-next/engine-transport-owned-managed-r4-20261027/test-result.json)、[采用说明](../foundation-engine-transport-owned-managed-20261027/README.md) | 同 PID native 冷启动与 CLR 全 12 family 采用、重复拒绝、实时篡改拒绝；未执行本报告完整 active 退出链 |

## E1 / E2：快照回归与真实失败历史

| 轮次 | 实际终态 | 保留的原因与修复 |
| --- | --- | --- |
| [core r1](D:/riz-build-cache-next/engine-active-retirement-core-r1-20261027/test-result.json) | 父 `passed=false`，runner exit 1 | 排除了 C 的六个草稿 partial，但 `NativeLocalCutoutEventSource.cs` 已引用 `_activeObserverRetirement`；实际 Runtime-build 出现 `CS0103`。不是执行超时或测试通过。 |
| [core r2](D:/riz-build-cache-next/engine-active-retirement-core-r2-20261027/test-result.json) | 父 `passed=false`，Runtime28、receiver10 已过，Audio 未全部通过 | 实际失败用例为 `Failed native handoff leaves an adopted factory root owned until exact close without a second free`。已转交、当前无可释放根的保留 Unknown no-op 被 `VerifyUntransferredClosure` 误拒；修复保持实际根责任，不重复释放已转交根。 |
| [core r3](D:/riz-build-cache-next/engine-active-retirement-core-r3-20261027/test-result.json) | 真实 794/28/10 已通过，但父最终 `passed=false` | 无 `.git` 的隔离快照中，runner 将构建生成的 `obj/*.cs` 纳入前后源码清单，导致归档核验失败。不能把它回填成成功或解释为所有测试失败；随后显式排除 `obj/bin`。 |
| E2：core r4 | 父 `passed=true`，runner exit 0；795/28/10 | 500 声明来源固定、完整 C partial 纳入；新增最终 termination 回执生命周期用例。此轮仍带旧 listener 空派发判据。 |
| E1：core r5 | 父 `passed=true`，runner exit 0；**796/28/10** | 增加真实调用列表非空而子派发漏观测的负例，修复零 dispatch 被当作无副作用的判据。不能用 r4 或此前候选覆盖此修复。 |

r5 实际 Runtime 构建为 0 警告、0 错误；Audio 测试工程为 17 警告、0 错误，保留现有夹具 `CS8602/CS0649` 等诊断，不宣称全仓库零警告。

r5 父、子均 `passed=true`，`snapshotCaptured=true`、`sourceHashesUnchanged=true`。本报告复算了 500 个 snapshot 文件及 ZIP 中对应 500 个成员的 SHA/长度和 CRC，全部匹配；31 个声明产物 SHA/长度全部匹配。复核时 500 个原工作树输入也仍与 r5 冻结值一致。唯一明确 runner 转换是为新隔离树执行 restore 并排除生成目录，原始 runner 与转换后 runner 各有独立 SHA；不把转换后的哈希冒写为原工作树输入。

### 新增 Case 数量不是整条生产执行次数

以下数量直接从各轮冻结 ZIP 的实际 `Cases` 数组提取，并逐个与 `audio-tests.log` 的 `PASS` 行核对：

| 组 | r4 / r5 数量 | 实际覆盖 |
| --- | --- | --- |
| `NativeLocalActiveConsumersEndedTests` | **13 / 14** | 19 项完整性、缺项逐一拒绝、同局/结果代次、拒伪造 issuer、实际 registry token 隔离、实际 globals 后端重新读取与重入/错线程、Close 后只读 globals、termination 回执生命周期；r5 追加 listener 观察缺口 |
| `FrozenActiveSourceRetirementRegression` | **7 / 7** | 五个独立源根的真实受控 root-free 顺序、每根重验、异常不重试、foreign launch/factory/线程/issuer 拒绝 |
| `ObserverRetirementTransactionRegression` | **5 / 5** | Withdraw 与真实 activity drain 分离、保留至晚帧、独立逐根释放、失败 Unknown、精确 owner/context 回执 |

B 的纯完整性判定只产生诊断，不能创建生产 verifier 或 root-free 权限；globals 用例运行实际 `OwnedNativeControllerGlobals` 算法，但 delegate/native/GC 后端受控。C7/C5 的后端同样受控，不等于 Unity / IL2CPP 的真实 GC 根释放。

其他已存在数组在 r5 中分别为 cancellation association **18**、cancellation retirement **11**、Inline **21**，并非这三个数组全都是本轮新增。它们已包含在 796 中，不另加到总数。完整真实游戏退出链的执行次数在本报告仍为零。

### listener 空派发缺口及 r5 修复

旧 `ListenerEffectsAccounted` 仅见 `InvokableDispatches=0` 和空 child 记录就接受“无本 owner 的副作用”。这不足以区分实际空列表与缺失的 dispatch 观察：实际 Invoke 已返回、实际 executing list 含非空 opaque invokable，但子派发完全没有采到，仍可有未归属 async。

r5 的 `ListenerEffectsIndependentlyEmpty` 将两种证明分开：

1. 没有任何实际 Invoke，且 invocation/dispatch 计数和逐 event 记录均一致为零，才能证明没有这次 owner 的 Invoke 副作用。
2. 有实际 Invoke，必须具有完整实际 inline/path 证明、配对返回、同 token/caller/PC/graph 的确切空 executing list，且没有 mutation 或多代历史被最新快照遮住。任何字段缺失仍 Pending。

新增负例明确构造“已 Invoke + 非空真实捕获列表 + dispatch0”，要求拒绝；同时核缺 inline/path、重复 Invoke、未返回均拒绝，完整空 executing-list 事实可接受。该纯谓词不接受外部 SDK 输入作为生产凭据，生产仍只读取实际 owner 的快照。

当前 `NativeLocalUnityEventLedger.Read()` 的完整执行证明字段仍保留未证默认状态；非空任意 listener effects 没有实际终结 issuer，就不能借场景卸载、Invoke 返回、无 in-flight 或零观察计数关闭整局。这是具体剩余覆盖，r5 没有伪造完成回执。

## E3：三进程取消联合，范围单列

| 模式 | 实际 PID / exit | managed / native 检查 | 实际终态 |
| --- | --- | --- | --- |
| normal | **28484 / 0** | **36 / 238**，nativeFailures0 | actual L/M/N、实际 Inline Begin/End、Stop scope、精确账本终态均通过；五个自有 source 实际撤回 producer 引用、独立死亡并各释放一次保活根；`ownedSourceClosureVerified=true`、`ownedCancellationTerminalVerified=true` |
| missing-source | **56116 / 0** | **33 / 238**，nativeFailures0 | 引擎终态可成立，但完整来源未关闭；owned source/cancellation terminal 为 false，五个 source 根未释放；这是预期拒绝通过 |
| wrong-generation | **56568 / 0** | **34 / 238**，nativeFailures0 | source 可到自有死亡状态，但错误代次不能借用；owned source/cancellation terminal 为 false，五个根未释放；这是预期拒绝通过 |

三个父 case 均 `started=true`、`passed=true`、`timedOut=false`、`killed=false`。每个实际 Stop 的 capture/complete/dispose 均为 1；native Start/Detach/Callback 各 1；node 与 wait 各有 1 次真实自有释放。这些 node/wait 释放与五个 source 保活根是不同责任，负例中前者完成不代表后者已释放。

normal 的五个 `sourceFinal` 均 `State=3`、`RootReleaseAttempts=1`；missing-source 五个仍为 `State=0`、attempt0；wrong-generation 五个为 `State=2`、attempt0。自有来源是夹具分配的对象、代次与 producer 引用，不是原游戏的五个 Unity 对象。

本报告复核 17 个声明输入 SHA/长度，全部匹配；三个单模式 JSON 与父 `cases` 各行完全相同，stdout 解析值与对应 `child` 相同，stderr 均为空。三份 child 的 `productionEngineCancelled=false`、`productionAdmissionGranted=false`、`gameExecuted=false`、`installed=false` 保持。E3 调用共享的真实账本终态判定，不能被改写成 B 的生产 factory/source issuer 或整条 active Close 已实际执行。

## E4：V5 冷启动采用不是 active 退出证据

| 模式 | 实际 PID / exit | managed 检查 | 能证明的结果 |
| --- | --- | ---: | --- |
| adopt | **48892 / 0** | **109** | 真实 native 冷启动后同 PID CLR 采用全部 12 family；第二 consumer 共享，计数和完整 publication 不变、根保留 |
| duplicate | **50736 / 0** | **138** | 首次采用12，重复整包采用被拒；托管 Unknown 与原生既有责任保留 |
| live-tamper | **48472 / 0** | **108** | 真实源字节从255改254后，采用前即时验证拒绝；恢复255且页保护/RX/flush确认，托管 Unknown 不复活；本模式没有成功 Attach12 |

三进程均由父独立记录启动、exit0、无 timeout/kill。正常和 duplicate 的 `attachedFamilies=12`，live-tamper 为0；不能把三进程都写成“采用12”。各进程均有 native/managed 独立记录与完整 raw；native state、36 已提交窗口、67 RF、资源责任保留。采用成功没有生成本报告所有游戏对象账本，也没有调用真实游戏函数。

本报告复核 E4 的156个 inputs、207个 artifacts 和11个 managed binary 记录 SHA/长度，全部匹配。此组使用自己的冻结 Runtime/native 构建，不是 E1 Audio/Adapter 的完整 active 运行；详细普通依赖、原生状态及篡改过程见 E4 独立说明。

## 已接通的实际运行代码与信任边界

主要实现位于 [ActiveReturn](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.ActiveReturn.cs)、[ActiveRetirement](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.ActiveRetirement.cs)、[ActiveOwners](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.ActiveOwners.cs)、[verifier](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalActiveConsumersEndedVerifier.cs) 和 [逐 owner 事务](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalObserverRetirement.cs)。这些源码已编译纳入 E1，以下描述是代码行为和受控算法覆盖，不是全链已在生产执行的声明。

- 实际已分类的 `ResultBack` 只向对应的私有 coordinator 排队，匹配同一个 registry/session/result receiver。后续帧确认实际 host/engine/Stop 物理调用排空后，才执行一次 `BeginReturn`、provider 撤回、菜单 lock、真实 native return；queued 不等于 completed。历史取消 Pending 不挡必要的退场观察，但继续阻止 Close 和 free。
- Return 使用 Begin 时独立保留的 manager 根，读取真实 `AsyncOperation`、原 Level handle 卸载与目标菜单加载。菜单恢复、六 globals 的条件恢复、实际 UI callbacks 结束均有各自 owner，不以一个 return bool 代替。
- 五对象独立死亡先冻结 source seal，仍保留所有根；Node/Inline/M/N/请求版本/Detach/Start/节点代次核对完成后，实际 engine issuer 才能签取消完成。source seal 和 root-free 权限分开，caller DTO 或 bool 不能授 Close。
- `NativeLocalActiveConsumersEndedVerifier` 只能由实际 coordinator 的单例/私有 return issuer 创建；绑定同 session/controller/result scene/result generation。19 项全部成立后，Host 在真实 native guard 下调用已有 `VerifyConsumersEnded → Close`，Close 前再次重验完整 owner 与物理状态。registry token 另绑定其真实 retirement 对象。
- 成功 Close 先撤分类再释放 registry 所拥有根；任何未知 free 保留 Unknown、不重试。原 factory8根与参数2根完成后，逐个取得 Terminal、Cutout、UnityEvent、Cutout source、Cancellation 五个 exact owner 回执；再收 engine2根与其逻辑绑定、死亡源5根、return/scene/manager/reservation责任。退出物理 native 全局 callback owner、代码与已注册 unwind 不随逻辑 consumer 退出卸载。
- 最后独立泵 globals、menu、Awake window、prefab 与 bundle 的真实延迟销毁，以及实际 termination work。只有全部15类最终 owner 表、五个 exact receipt、来源与 native drain 最后复验通过，才消费 handler 对 launch 的最终强引用，并移除这一局的 `ProcessOwners`。factory 局部完成与 whole launch 终态是不同字段；晚帧等待不能重读已退休 enum 或再释放同根。

### 19 项验收表与15类最终 owner 表

19项为 ReturnRequest、ProviderWithdrawal、FactoryDeathSeal、ReceiverDeathSources、TrackerGenerations、InlineCompletion、SchedulerRows、WaitPublications、CancellationRequests、NativeActivity、HostActivity、UnityEventConsumers、CutoutDispatch、FutureNodeSource、FutureSchedulerSource、FutureWaitSource、AsyncConsumers、GlobalReferences、SceneResultConsumers。缺项、重复、Unknown、Pending、不同 session/代次均不能通过。

15类最终 owner 为 registry、factory、parameters、initialization、五观察回执组、engine、death-source、return/scene/manager、reservation、globals、menu、Awake window、prefab、bundle、termination。回执必须按 exact owner 验证，不能仅比较数量；bundle 的 `Dispose` 只是请求 Unity Destroy，须等实际 `TryCompleteDestroy` 才能移除最后 owner。

## 真正尚未完成的清单

| 实际缺口 | 当前代码/证据状态 | 需要的具体闭环 |
| --- | --- | --- |
| 任意 listener 副作用 | r5 已堵零 dispatch 漏洞；完整 executing-list/path 未证或非空 effects 无 issuer 时 Pending | 在实际 UnityEvent owner 下完成逐实际 listener effects 的来源归属与终结回执；覆盖 opaque、多播、mutation、重复调用与异步产出，不能以场景卸载/idle代签 |
| 所有未来来源 | C seal 仅覆盖这一实际 launch 的 Controller/EventTrigger 两 receiver、三个 tracker 和已登记 L/M/N 节点/row/publication | 仍需证明其它直接/序列化/泛型写者、自定义 yield 和 foreign producer 的实际可达性及封口。迟到入口被标 Unknown 是拒绝策略，不等于所有未来原生入口已物理不可达 |
| 完整 active 同进程执行 | E1 测具体算法；E3 测真实自有 L/M/N/Stop；E4 测真实 V5 采用 | 在同一个自有完整组合进程运行实际返回请求、native scene operation、真实 owner seals、取消、19项校验、Close、逐根失败与全部15类终退；不能跨进程累计代替 |
| 返回/重试/宿主入口范围 | 新 actual ResultBack cleanup 路径已接；Host result sink 的 Return/Retry 仍依赖 composition command delegate，完整新局 Retry 未验证 | 接并验证实际 UI/composition 命令与新局的独立 owner，覆盖重复请求、晚回调、取消后退场、Unknown 部分结果；排队和无异常不是完成凭据 |
| RunProtection 最终撤回 | 有 token、withdraw/drain、实际 owner table 和 `TryCompleteStop` 代码；没有本轮真实游戏整局终退证据 | 验证 classification 撤回前后重入、progress/judge/初始化边界、所有同局 handler work、真实延迟资源死亡和重复多局无根/配额增长 |
| 固定游戏启动许可 | native `ProductionFirstInstallAuthorized=false`，CLR `ProductionStartupAdmissionKnown=false`，RequestPlay 仍关闭 | 证明固定游戏第一次36窗口执行前的安全时序、完整元数据/依赖/对象身份，再按用户恢复实测的安排做冷启动、整局、暂停、重试、返回菜单、多插件共存 |
| B01–B19 完整交付 | 本批主要推进 B02/B05/B07/B09/B15 的底部；[总清单](../../../.docs/foundation-expansion/CHECKLIST.md)中历史批次各有范围 | 真实流程事件/对象GC、全部服务双包矩阵、输入/视觉/资源释放、20轮压力和干净环境/升级分发等不能用本批回归替代；本报告不修改任何整项验收勾选 |

## 关键冻结身份

所有 SHA-256 均由本报告重新读取原件复算；完整500输入与其余产物清单保留在 E1/E3/E4 JSON，不在正文重复500行。

| 原件 | SHA-256 |
| --- | --- |
| core r4 父记录 | `19c9ba34429ecad0a9f1a826c285f0bb39c5090af191c3bc721cab4b32dbf9dc` |
| core r4 source ZIP | `d3544f8725401ed8c50f093920cfcef19496184cc11064c5389e9ba13220dc8b` |
| core r5 父记录 | `043b13d40563c9ad595bb5f572c23ff96d8b6fdb38b88fa39df93f3f5362aef7` |
| core r5 子记录 | `c8516ce75ab17c12adbf473fb4ea689544e767f82f8dffdec332842ad5eed5b6` |
| core r5 source ZIP | `cba18543b4a364c0cb061c872f14aa4b6bc7f13256c9f220afe45f3a272930eb` |
| core r5 Audio 日志 | `16283c2ad4046862cedfca9e0067c47dce21cf92c80be8cc12fd7201b9d53ca1` |
| ActiveRetirement.cs（r5） | `bb43c000b046308e22caa4451079fc6fcb04be4c4f93b193da089f4b5b21057f` |
| ActiveOwners.cs（r5） | `5126aa84f8a3a8cba9f4540564cbb81f469abdc8e3bdb663ec6f46aac7809770` |
| ActiveReturn.cs（r5） | `a5cfed47e96f5cfd90f18147494f42d3cb2fa0c3a681d4b679d39f0b6c7b7c56` |
| NativeLocalActiveConsumersEndedVerifier.cs | `b467b567b4dc8b642d89a50d8d87180a5a17cc31fe68820c90cf766eddb294fc` |
| NativeLocalObserverRetirement.cs | `e4c4c6ea5ae20300d70c0eedde7050c3c0ebb9bf641ee6b14748e6b1c56a80a7` |
| B14 tests（r5） | `0a36e368ef3571f66ed0240ad2e3702a06bed0e730f318d2a31d275df97aa258` |
| frozen-source7 tests | `30ab65c26871db2c42b5fae52864bc043e0d8dc7c28a83729583db9f4dc14b50` |
| retirement-transaction5 tests | `73689c4a8f6798e93c02de5f1b49fefda6b7773445f2a1662b4fd2524cdc2c09` |
| joint-cancel r2 父记录 | `d99cbe3fb579ae19f38604fda7e5f2858820486b61e2d2bbe766f6c79f64cf9b` |
| joint normal case JSON | `99b92edfba25aabc044ea46077af5bbd8c9cdd1a79eef2dc1e04aef00794c18b` |
| joint missing-source case JSON | `18dc707a2408bdb0be5866502d28c0ebc9020c97fe92f6b43464cc6032e19af4` |
| joint wrong-generation case JSON | `8fdfd24ae7ccb3728083c70de25f5a0661330919525c4fd145ad55473547aff4` |
| V5 owned managed r4 父记录 | `60ca3b1c5f31845085022ca5d566a9d5fd48a44a3443c3048ba8af8fcf47455e` |

可复跑受控快照的命令如下；必须使用不存在的新输出目录，保存本轮输入而不是覆盖历史。本报告没有执行命令：

```powershell
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' `
  -NoProfile -File 'C:/Users/71957/Desktop/riz_toolbox/scripts/Test-ActiveRetirementSnapshot.ps1' `
  -OutputDirectory 'D:/riz-build-cache-next/engine-active-retirement-core-recheck-20261027'
```

最新原件保持 `gameExecuted=false`、`installed=false`、`productionAdmissionVerified=false`；core 的 `fullEngineCancellationVerified=false`、`fullActiveRetirementVerified=false` 与 E3 的生产取消 false 也保持。源码接线与本轮受控证明已交付，真实固定游戏与 B01–B19 的完整验收仍未完成。
