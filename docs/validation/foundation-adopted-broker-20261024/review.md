# 已配置父通道共用 broker core：独立源码复核

2026-10-24 18:01 +08:00 更新。范围为生产 front、共用采用 core、bridge 身份读取，以及新增自有 native→CLR 联合用例和 pre-Mid 门。只读源码审查未发现阻塞的确定缺陷。root 已确认完整 session 11559 真实终态 exit0，25套1515/1515；本 agent 读取完整构建的 JSON、日志哈希和冻结记录，另列真实回调三进程379项复跑通过。484源/22包已冻结未安装；这不是实际游戏验收。

本次没有构建、安装、游戏/UI 操作或源码修改。六区 listener 另见 [native-listener-design.md](native-listener-design.md)，它是下一批设计，不是本批能力。采用 docs-generator 通用最小结构，flavor=null；下文只陈述源码属性及证据边界。

## 证据

下表均为 source_type=file，observed_at 为上述时间，source_ref 与 artifact_path 为对应链接，supersedes 为本文件 17:37 首审记录。在仓库根以 Get-FileHash -Algorithm SHA256 -LiteralPath 后接对应文件路径即可复核哈希，再读取实现。没有通过运行来推定结论。

| E-id | source_ref / artifact_path | SHA-256 | 实际读取内容 |
|---|---|---|---|
| E-core | [ExactHookBroker.NativeVoid.Bootstrap.cs](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeVoid.Bootstrap.cs) | 05c3a0c46bf3e75771167032f70f1061c75a213b7128d9149bff5f8a1075d3e2 | RequireReady/UnityTarget；Gate 内 VerifyReady→共用 core→VerifyReady；private Entry→完整 tuple 字典 |
| E-bridge | [NativeBootstrappedVoidBridge.cs](../../../src/RizToolbox.Runtime/Hooks/NativeBootstrappedVoidBridge.cs) | bb87b3a4aee2f9d854e2139bee36aa59fccbe75073210b6c71a5828307972df1 | 只读 slot/target/original/epoch；immutable callbacks、一次 Attach 与真实 Info/统计读取 |
| E-joint | [NativeAdoptedBrokerRegression.cs](../../../tests/RizToolbox.Runtime.Tests/NativeAdoptedBrokerRegression.cs) | bab8f02f9b5439f678e2e4293b12e6e14180059005ea9aa7676c04c100e421b3 | 最终源码含 using RizToolbox.Runtime 与实际 SuppressedWrongThread 字段；同 core、双 sink、真实 native caller/Mid、GC、异常后正常调用 |
| E-native | [Fixture.cpp](../../../native/RizToolbox.InitializationAbiFixture/Fixture.cpp) | b36275e16e09210a8f1e132db9f78b6d94573632f2c2c9ba5033ee704f21a3a6 | 独立 pre-Mid 门、active 责任、10s 等待、超时 finally、原函数 /EHa RAII |
| E-asm | [Entries.asm](../../../native/RizToolbox.InitializationAbiFixture/Entries.asm) | 9353115a9dc977c32c93733205aa1d0d031b1f856f43dce711a0f1692c4cd21d | kind4 固定 +13Bh CALL/+140h 返回标签；门后与原 body 前分别恢复 RCX/RDX |
| E-runner | [Test-AdoptedBrokerManaged.ps1](../../../scripts/Test-AdoptedBrokerManaged.ps1) | abe6329adf4c942aa80b8c1bf74d9c3aab8d059e8f0eaee999970d337878a8c3 | 每个自有 child 上限 60s；仅超时终止本脚本创建的进程；输入前后 hash 与非游戏证据合同 |
| E-info | [BootstrapVoidContext.h](../../../native/RizToolbox.JudgeContext/BootstrapVoidContext.h) | 7999bbd1be79b272c55c6681155b7ec111452ddec7a97ee32b5bc8ece7ed1b1e | Info56 不含 native expectedMidReturnPc |
| E-caller | [NativeLocalUnityEventBackend.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.cs) | 2043d8fc4201557293691d1409c3d570cdad8ee47b89fa1aeb03731a808080cc | 生产 Adapter 仍调用 receipt-checked BindNativeVoidObservationWithMid |

配套文件也已读取：[Fixture.h](../../../native/RizToolbox.InitializationAbiFixture/Fixture.h)，SHA-256 3e96cd498ba0b4906ff9ad495d97efafe4ce651e8aa9f4c2e658344f61807ada；[Build-InitializationAbiFixture.ps1](../../../scripts/Build-InitializationAbiFixture.ps1)，SHA-256 9981c2c2b62660f0e434bb2e90ecdd496d51098b9476c10a98508230e53374cd。后者要求新门 exports 且以 /EHa 编译；这里只读该要求，没有执行。

## Findings

各项 category=design、severity=info、confidence=high。F-production/F-mid-boundary/F-failure 的生产保证仍为 source-reviewed；涉及自有 fixture 的结论另有下节 E-targeted 动态支持，范围不扩大到游戏。linked_workitem 限于本批共用采用核心，不扩大 B01–B19。

| F-id | evidence_ids | 结论与影响 |
|---|---|---|
| F-production | E-core,E-caller,E-joint | RequireReady、精确 UnityTarget 和同一全局 Gate 内前后 VerifyReady 保留。共用 core、tuple、broker class 都 internal。fixture 先调用生产 front 并要求因无真实 receipt 拒绝，随后才用内部已配置通道入口。任意 tuple 没有变成生产 Ready。 |
| F-identity | E-core,E-bridge,E-joint | 首次 factory 只在 Entry 不存在时调用，核实际 bridge 的 slot/target/original/entry/epoch。共享 Entry 先比私有字典完整 tuple（含 mid）、ABI/MethodInfo/Class，再比实际 bridge 与 Known，才添 sink/probe。两 broker 实例共用 static Entries/Gate，identity 字典也只在该 Gate 访问。测试要求 factory=1、broker 安装/Prepare/Commit=0，并拒绝逐字段 identity 与 metadata/ABI 变化。 |
| F-mid-boundary | E-core,E-info,E-joint | **采用时没有从 native Info 读回 mid 地址。** 首次 mid 来自发布方 tuple，私有字典固定该值；共享时严格一致，真实回调后再核 frame.ReturnPc。生产值来自真实 producer receipt，fixture 来自实际 native 标签且 Configure 用同值。不能描述成 adoption 已通过 Info56 读回并验证 native 期待 mid；本批不扩展 ABI。 |
| F-failure | E-core,E-joint | 生产 post-Verify 失败只撤本次 logical binding/probe，永久 Entry 与 native owner 保留并置 NativeVoidUnknown。不会拆原现场、重试安装或把 peer 置 Known。此分支目前是静态结论；本 fixture 的生产 front 是拒绝路径，不执行实际 producer post-Verify 失败注入。 |
| F-publication | E-core,E-bridge,E-joint | 首次 sink/probe 在 Attach 前准备，三个 handler 在 Configure 前冻结为同一 callbacks；native 源码使用 Enter 时 descriptor 快照。late-attach 先核真实 Parent=1/InFlight=1、Mid=0、body=0，再采用；释放后 Mid=1、body=1 且两个新 broker sink 的总数仍零，只证明旧调用实际经过 native Mid/Exit 后没有新 broker 订阅/context/probe 交付。本用例不单独计 raw CLR delegate 入口次数；若原始 Mid 误入，core 也可能因无 physicalTop 而直接返回。 |
| F-native-gate | E-native,E-asm,E-joint,E-runner | RCX/RDX 保存在 rsp+20h/+28h，与 20h outgoing shadow space 分开；门后和原 body 前均恢复。pre 门正常返回后由 VoidBody 原生 RAII 承接 active，body 正常/SEH/C++与等待异常会清理；pre 门自身超时由 finally 清 active。managed finally 总会 Resume，等待有界，外层 child 另有 60s cap。late-attach 正向会在超时前释放，超时分支没有本批执行证据。 |
| F-context | E-core,E-joint | main 实际 capture→Mid→After→Dispose 使用两份独立 context、同一 physical token；各 identity 拒绝 peer context，Exit 后失效。worker 与 foreign MethodInfo 路径只核标量 probe/fault，受控高位指针不解引用成 IL2CPP metadata。callback 断言异常被保留并在 child 主流程重抛，避免被生产 containment 吞掉后假通过。 |
| F-unwind-recovery | E-native,E-joint | fault 包含 managed After 故障不改原返回、原 native SEH/C++ code/kind/payload 透明、异常不发 After、context/probe 配平。每种原异常后再同线程正常调用，核 capture/After/Dispose/probeExit 恢复，同时 broker Unknown 保持、native bridge Known idle。最终核 body/parent/Mid 计数及 admission active/parked/unknown，不能只读托管 idle。 |
| F-retention | E-core,E-bridge,E-joint | 逻辑解绑保留 permanent Entry/native bridge，rebind 不再采用/Attach。测试仅 weak reference 查询 bridge，没有由测试 Owners 强根 bridge；GC 后再实际回调。此用例可形成持根证据，但不证明游戏全 run 退休或全部 SDK owner 的 GC 闭合。 |

F-native-gate 的 active 责任按用例合同覆盖等待、原 body、contained observer 故障。没有把 arbitrary hardware fault、线程强杀或 pre 返回与 body 构造 RAII 之间的任意异常注入列为已覆盖。实际六区 child 的异常责任仍须按 listener 设计另行实现。

## Path 与后续证据

P-production，path_type=callflow：生产 RequireReady → UnityTarget 核对 → Gate 内 VerifyReady → 共用 core 首次实际 Adopt/immutable Attach 或同 Entry 完整 identity 共享 → 再次 VerifyReady → 返回 logical Binding。证据 E-core/E-bridge/E-caller，对应 F-production/F-identity/F-failure。末端是静态调用链，不是本批真实游戏冷启动证明。

P-joint，path_type=callflow：自有 native 目标 Dobby Prepare → Bootstrap Configure 与真实 Mid CALL 发布 → 一次 Commit → 两 broker 经同 core Adopt/共享 → 自有原函数真实 native→CLR callbacks → matching context/probe Exit → 原生统计与 bridge/admission 状态核对。证据 E-joint/E-native/E-asm/E-runner/E-targeted，对应 F-publication/F-context/F-unwind-recovery/F-retention。E-targeted 已支持此自有目标执行链通过，不支持真实游戏启动结论。

## 实际定向结果

E-targeted，source_type=log/file，observed_at=2026-10-24 17:50 +08:00。root 报告真实终态 session 35938 exit0；本 agent 只读对应 JSON 与日志，没有启动进程。artifact/source_ref 为 D:/RizToolbox-build-cache/adopted-broker-targeted-20261024/adopted-broker-managed/fixture-result.json，SHA-256 50880d31f989537dde0ab4bd18de96c237efa02d28245c83ee70095a316a0e86；source-file-check.json 同目录，SHA-256 15356e9dcba667f7c2dc55d06311affe919b0f0f92b31ab33abe727962836e64。源码调用链与独立 child 的原生执行结果共同支持 F-identity/F-publication/F-context/F-unwind-recovery/F-retention 的 fixture 范围，不能推导 F-failure 已被生产故障注入验证。

| child | exit / checks | 实际观测 |
|---|---|---|
| happy | 0 / 123 | 原 native 6 次、broker 6 次、Mid 6 次；capture/Mid/After/Dispose 各4，含 rebind；probe8/8；factory1、broker安装0，broker/native bridge Known |
| fault | 0 / 181 | 原 native 6 次、Mid 6 次；capture/Dispose12、After8、probe12/12；managed故障与原SEH/C++保留均true；broker Unknown、native bridge Known，异常后正常调用已恢复配对 |
| late-attach | 0 / 75 | 原 native/Mid2次，broker仅1次；旧call没有新broker订阅/context/probe交付（JSON原字段oldInvocationDidNotInheritObserver=true）；不声称raw CLR delegate入口次数为0。随后新调用双sink capture/Mid/After/Dispose各2、probe2/2；factory1、broker安装0 |

三个 child 的 gateActive/gateParked/gateUnknown 全为0，文件 hash 前后相同，均明确 firstInstallReceiptUsed=false、metadataIdentityControlled=true、actualIl2CppMetadataResolved=false、gameCodeExecuted=false、actualLedgerCaptureVerified=false。三个独立进程总计379项，失败0；这是实际 native→CLR 回调证据，区别于上一批仅 idle adoption。

既有定向 Runtime160/Hooks171 的日志为 [targeted.log](targeted.log)，SHA-256 274e527c822d75c2924f1d0e196babfb9a73fbfeaad6a4f7360e995edb0b1d0f。首次 session32771 exit1 的 [编译失败日志](targeted-first-compile-failure.log) 保留；根因是测试缺 namespace using 和使用了不存在的 WrongThread 字段。root 仅修这两行后重跑，production core/bridge hash 未变；不能把首次失败抹去或描述为运行时故障。

## 完整构建与冻结终态

E-full，source_type=log/file，observed_at=2026-10-24 18:01 +08:00。root 以实际 runner 终态确认 session11559 exit0；本 agent 只读重核如下文件与 SHA-256，不另运行构建。完整构建为 Runtime160/Hooks171/Visual71/UIinput79/AudioContent573，总25套1515项，另有三个自有native→CLR child379项（123/181/75）。本次完整日志保留109条去重编译诊断，不宣称零warning或全新环境复现。

| source_ref | content_hash SHA-256 | 实际记录 |
|---|---|---|
| [build.log](build.log) | 41a44d98a044de16680110701fec48daf8e9ab2630bde6ab6f55a3813ecab914 | 与summary/runner的logSha256相同 |
| [build-summary.json](build-summary.json) | 635a7b9655ee97defa77db57a813b3dab466345a6bb7e895cf7068ee73bb8195 | 25套1515/1515，含实际adoptedBrokerCallbacks379/3 |
| [build-run.json](build-run.json) | 4f3be9d526dc8e851f9a2a55e990dc6c19673d321f8caab92f42c6d130e22fa1 | session11559 exit0，rootSoleBuilder=true、installed/gameExecuted=false；首次定向失败保留 |
| [adopted-broker-evidence.json](adopted-broker-evidence.json) | 892001f022807acafb638d963d56a20f796ef7736c35a171c83fe349c03c1edc | full新native缓存的三child均exit0；各counts与上节定向相同，不挪用定向旧helper充作full |
| [build-input-integrity-check.json](build-input-integrity-check.json) | 5a22287f64e968ff4ef0a6d95963c812c07b874cced7d1854767bf558fa16333 | 484源，构建前清单等于归档manifest、currentChanged=[] |
| [source-archive-check.json](source-archive-check.json) | 6031cef9cd6d3478925d6dcf29a16e37ae1cfd6b9feffc40b2fec14026322583 | 484源ZIP逐项匹配；source ZIP另为04e410017be6e81fd28b0485ff6593c60fa820f17ce434fc3ecd44e916655396 |
| [package-archive-check.json](package-archive-check.json) | 7d301badd1db3f9b885bf414ac3e17afef0e9275bf606a77be36ed14ff665d0b | 22暂存文件逐项匹配、installed=false；package ZIP另为7fcb2d2e26c0242f42567a4fbcd5b95ca14dd9486ce023fe493c0f734451b03c |

source-manifest SHA-256为56cad82966aecd31110091efae491ac6fddfc604a27d75ffd22aca8c5814de73；[与CLR采用批差异](changes-from-clr-preinit.json)为482→484源、新增2/修改8/删除0。完整child的[source-file-check](native-build/adopted-broker-managed/source-file-check.json) SHA-256为060fa2471e0f94446f6e53cd85ddc07aa91839a4d309bc0a978b8af995f9414c，五个输入前后相同。旧1021安装18项/其326源归档与原游戏三hash的独立核对保持，不代表新22项包已安装。

F-identity/F-publication/F-context/F-unwind-recovery/F-retention现有源码与定向/full动态证据交叉支持，范围限于共享生产core加自有fixture真实callbacks。F-production的生产门与F-failure的postVerify撤回仍按源码结论，Info56不读回mid的事实不因full通过改变。真实固定游戏receipt callbacks、实际metadata、游戏coldboot/acceptance、三listener六区、listener/GC/full retirement仍分开验收。RequestPlay关闭，仅B03/B18a勾选，实测稍后；本轮没有游戏/UI、安装、原游戏修改或commit/push。
