# Listener 实际 CLR 回调与 ledger：有限复核

2026-10-24。root 已观察定向22041和完整21231的真实终态exit0；[完整汇总](build-summary.json)为25套1521/1521，新增12种独立进程模式1630检查全部通过。自有DLL的真实native→CLR listener回调已接入与生产共用的broker dispatch，normal/原SEH/原C++三种模式还进入实际ledger；metadata、对象图和根记录由受控backend提供。固定游戏listener执行、生产IL2CPP根与GC、完整delegate leaf及run退休仍无验收证据，新包未安装。

本 agent 实现本轮新原生fixture及其构建脚本，随后只读有限复核root编写的bridge、broker、backend和ledger；原生fixture部分属于作者自查，不能称独立第三方审计。本轮最后阶段仅写本文件，未运行构建、fixture、游戏或桌面操作，未改冻结输入。root是唯一builder。范围承接[上一批生产发布授权边界](../foundation-listener-publication-20261024/scope.md)，本轮增加自有native DLL、实际CLR观察和受控图上的ledger接线；不包含安装、游戏UI或扩大B01–B19勾选。文档采用普通工程有限审查结构，flavor=null。

## Evidence

source_type=file/log，observed_at=2026-10-24，本 agent 在root发布完整终态和冻结proof后只读复核。下表content_hash为实际读取文件的SHA-256；source_ref同时为artifact_path。E-build对应root运行，本 agent只读取结果和hash，不把读JSON称作亲自执行fixture。

| E-id | source_ref / artifact_path | SHA-256 | 实际观察 |
|---|---|---|---|
| E-build | [build-run.json](build-run.json) | 2aea19fb626b4588a323e7cdff31021c38727315a4582a8bcad5dec7e98772ce | full21231与targeted22041均terminalObservedByRoot=true、exit0、未安装/执行游戏 |
| E-summary | [build-summary.json](build-summary.json) | cc6f75ee1ea55388a5d9f934ef82b6ca9df3d5834ccc2ae406d626aa1126c566 | 25套1521；listenerManaged1630、旧native listener8349、旧broker CLR379均复跑 |
| E-run | [listener-managed-evidence.json](listener-managed-evidence.json) | c48f29c7d49af4989305b07865e69254a236173fa2f33c4c81d46466fdf6cc1c | 12个child exit0，12种模式checks相加1630；每个mode有实际counter和范围布尔值 |
| E-fixture | [ListenerManagedFixture.cpp](../../../native/RizToolbox.JudgeContext/fixture/ListenerManagedFixture.cpp) | b269b553ce0c6cd139ddd43010109df560879a0a9307249ee849b79d9429fa65 | 两个自有parent、真实Dobby/六区/mid、动态chain、有限work/hold及原生异常catch |
| E-fixture-abi | [ListenerManagedFixture.h](../../../native/RizToolbox.JudgeContext/fixture/ListenerManagedFixture.h) | 5aa02b2d50d4818a462e7c9cfb84ea0d0e29795a84ad68c41a734bc17fdd98c5 | Config40 / Info336 / Result240，8个fixture专用导出，无Ready |
| E-fixture-build | [Build-ListenerManagedFixture.ps1](../../../scripts/Build-ListenerManagedFixture.ps1) | 5d6a19de58ca8662198659504dffccd50c803238702f8b0c2d9b7e708fcdb70e | 编译独立DLL并复用未修改ListenerCaller.asm；导出/反汇编/unwind文件，不执行fixture |
| E-runner | [Test-ListenerManaged.ps1](../../../scripts/Test-ListenerManaged.ps1) | fbc6481c0fdc2f45b2e47f04227ca07770c9760436dbd386c871785612ea8d75 | 12个隔离CLR进程、60秒有界runner、固定Dobby与文件前后hash |
| E-bridge | [NativeListenerContextBridge.cs](../../../src/RizToolbox.Runtime/Hooks/NativeListenerContextBridge.cs) | 06c85ff63fe60dc803dcb75508e892b17685859c397d441e638b47e4699a65b2 | 176字节frame/info、32字节observer，复制live machine后清借用指针，永久Attach和异常包含 |
| E-broker | [ExactHookBroker.NativeListener.cs](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeListener.cs) | 33ef8634912310447c3b2d40b8a32ce6733a2657b9f4be14f3021933bdc2c93f | 共用dispatch、精确current-top/parent token、先留child责任、双sink与标量清理 |
| E-front | [ExactHookBroker.NativeVoid.Bootstrap.cs](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeVoid.Bootstrap.cs) | 30ea5b05946e81222d823b36caf2e2fd17872a404fd048725b20edb63b349f11 | 生产RequireReady/UnityTarget/Gate内前后VerifyReady仍保留；configured前门不授生产许可 |
| E-current | [ExactHookBroker.NativeVoid.Current.cs](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeVoid.Current.cs) | 246a465a2da4ba080e4cbdff0ce7f65f8c352372ebb04925af4b4121cf47126f | Mid/Listener返回PC来自broker私有已配置身份，仍要求当前精确scope |
| E-backend | [NativeLocalUnityEventBackend.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.cs) | c8ba28489a145f5749caff5f37c400ef15f1c8b0e32247f92dde99ec9fd9dd3e | actual invokable/list/可选RDX的两阶段根采用；其后才Target/Class/List |
| E-ledger | [NativeLocalUnityEventLedger.Listener.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.Listener.cs) | c493c1874ebbd39997b1f19de82b5bf6bc575e1ff4f016500a54fbdaf8057afc | Before先记责任，核当前generation/token/inline list；After只用scalar/copied identity |
| E-ledger-state | [NativeLocalUnityEventLedger.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.cs) | 81b00b90ab577d926adce79979ea85037949aab81513a0c715deffe861b04783 | NativeReturned与ConfirmedReturned分开；Pending及两安全标志保持 |
| E-joint | [NativeListenerManagedRegression.cs](../../../tests/RizToolbox.Runtime.Tests/NativeListenerManagedRegression.cs) | 23c573e0b371c8f31f67d1697df27b41da3021c40e2a28d429a42960d309acb0 | 真实native callback、双sink、实际ledger+受控backend、原生AV直接bridge测试 |
| E-shape | [NativeListenerContextBridgeTests.cs](../../../tests/RizToolbox.Hooks.Tests/NativeListenerContextBridgeTests.cs) | cf63917e9d1235b96063a7e2f098f53acd6e818e5c0a5e261a086e55f68f70ca | 6组受控布局/identity/frame拒绝检查；不会把构造frame称作native执行 |

原始12份mode JSON和stdout/stderr位于D:/RizToolbox-build-cache/listener-clr-final-20261024/listener-managed/，其记录汇入E-run，进程stdout/stderr哈希也保存在E-run。受测helper SHA为8382d0e8a7561b29fedddec3965d24864c84acb5030fc1546677e221aabc4db1；自有fixture DLL为e57858d0fcbf952b38064f10cc0ddbae889a1a17cdcc64e9b7bff0d4c2820553；固定Dobby为8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded。各进程记录三文件前后hash相同，不会将fixture内存补丁称作磁盘DLL修改。

复现上下文是Windows x64、当前冻结输入与已存在的VS2019 x64构建环境、.NET8和固定Dobby。仓库根运行下列命令会重新构建并只在隔离进程做验证；本 agent 未执行：

```powershell
pwsh -File docs/validation/foundation-listener-clr-20261024/run-build.ps1 -Targeted
```

只读重核本批文件固定性可以使用Get-FileHash -Algorithm SHA256 -LiteralPath后接上表实际文件路径。复现只说明离线验证范围，不是安装或游戏实测指令。

## 实际接线与有限结论

以下F-id的severity=n/a_re、category=design、confidence=high；validated严格限E-run里的所测自有fixture，source-reviewed仅表示读过冻结源码。没有由本次有限审查推导任意恶意指针、并发内存破坏或完整SDK安全性结论。

| F-id / status | evidence_ids / location | 结论与限制 |
|---|---|---|
| F-own-channel，validated | E-fixture,E-fixture-abi,E-joint,E-run；Setup/VerifyInitial | 自有DLL的两个parent分别用slot19/18，各实际DobbyPrepare→Bootstrap配置→独立lane配置→真实八写；C#读真实Info并Attach，broker的NeverInstallEngine调用0。每target的3RF、12lookup/12受控栈边界验证均在两target首写之前。此处没有runtime-invoke补丁，八写与生产九写凭据不是同一合同 |
| F-current，validated+source-reviewed | E-bridge,E-broker,E-current,E-run；ListenerParentMatches/Before | 不从ancestor搜索context。只接受同entry、当前physical top、OriginalWindow、同thread、parentManagedToken、自身event和retained record；native parent/child sequence与managed token分开。双sink共享物理parent token，context授权仍各自独立；token0不伪造managed capture |
| F-copy，validated+source-reviewed | E-bridge,E-shape,E-run；Copy/ValidFrame | 读取live176frame和1B0machine value，核真实source/transport PC、RSI/RBP/RBX、入参、对齐与OS thread，复制后清MachineFrame借用指针。委托/owner永久持有，Attach仅一次；没有卸载/热替换/物理detach能力 |
| F-production-front，validated+source-reviewed | E-front,E-broker,E-joint,E-run | 生产仍走V2 RequireReady与UnityTarget、Gate内前后VerifyReady；fixture尝试生产front被拒且两个channel尚未Attach。configured前门只在内部共用dispatch，不制造first-install receipt，不公开给SDK |
| F-ledger，validated，受控对象图 | E-ledger,E-ledger-state,E-joint,E-run | normal/SEH/Cpp真实回调进入实际ledger，保存三次实际invokable dispatch的copied身份；图、class、root计数来自测试backend，不执行生产metadata或IL2CPP GC handle。三模式都保持Pending，异常保持Unknown，不误称完整dispatch path或mutation闭包 |
| F-batch-roots，source-reviewed | E-backend；CaptureDispatch/AdoptDispatchReference | 复核确认多引用顺序问题已修：先对actual invokable/list/非null site2 RDX做managed去重与Root并存储；全批根采用之后才Known、Target/Class、ReadList。任何Root申请失败立即Unknown并保已获得根，不保证尚未成功的引用也已取得根。没有新增生产Root故障注入实证 |
| F-return-fact，source-reviewed | E-ledger,E-ledger-state；ObserveListenerAfter/Snapshot | NativeReturned只记native Stage==Returned事实；ConfirmedReturned还要求broker captured、ledger captured与无flags。此前两者混合的问题已静态修复；_dispatchReturned继续只计确认成功。capture失败而原函数返回的ledger分支没有专门动态注入证据 |
| F-cleanup，validated+source-reviewed | E-broker,E-ledger,E-run；ExitNativeListener/ObserveListenerAfter | Before调用前先保存child及每sink的captured=false责任；After不依赖Active/Known，不读native对象、不释放根，只校scalar配对并完成所留记录。父native finally先child abnormal close再V1 Exit，context最终Dispose及probe退出；所测进程native InFlight全部0 |
| F-target-av，validated，直接bridge | E-fixture,E-bridge,E-joint,E-run；PrepareAv/TargetRead | site2真实helper读[rax+178h]触发AV，原ASM预先读的[rax+180h]可读；native catch核异常RIP属于helper与实际故障地址。CLR实际收到无Before的Abnormal After(token0,flags2|8,machine空)，两direct正常完成，父Abnormal Exit。该模式不经过broker/ledger，E-run明示两项false，不扩大为完整broker AV或游戏AV实证 |
| F-foreign，validated，所测递归 | E-fixture,E-broker,E-joint,E-run；ForeignOriginal | 原生callee实际调用same-slot original trampoline，未进新的Bootstrap parent；更深source事件不能借外parent的scope，通道转Unknown且原函数继续、最终native计数排空。不能由这一例推导所有foreign mask/overflow组合 |

当前读过的上述冻结路径没有另外发现确定阻塞缺陷；这是有限结论，不是“所有底层已完成”。两阶段根采用与返回事实区分的修正属于源码证据，不因完整构建通过自动升级成生产fault-injection实证。

## 12 种模式实际结果

下表Before/After是JSON的managedListenerBefore/After，统计broker测试Observer的用户回调；ledger是额外逻辑sink，不计入这两个数。target-read-av使用单独的直接bridge计数，因此这两项为0而非“没有CLR回调”。native original次数包括本mode内的恢复调用、worker或nested调用；同一物理child向双sink交付两次也不等于原函数执行两次。

| mode | checks | original / replay | Observer Before / After | 实际范围与末状态 |
|---|---:|---:|---:|---|
| normal | 189 | 3 / 3 | 6 / 6 | 双sink各3对；实际ledger3 captured/confirmed返回，11个受控root记录，Known=true、Pending=true |
| original-seh | 176 | 3 / 2 | 6 / 6 | site2原SEH；双sink各1 abnormal，ledger2 confirmed返回+1异常，根保留、Unknown=true |
| original-cpp | 176 | 3 / 2 | 6 / 6 | site2原C++ typed payload及4参数核对，native catch内结束；ledger与SEH模式同边界 |
| target-read-av | 47 | 2 / 2 | 0 / 0 | 直接bridge本地计数2 Before、3 After，其中site2只有token0 abnormal；parentExit1；无broker/ledger验证 |
| late-attach | 139 | 6 / 6 | 6 / 6 | 旧call进入native parent并停在真实Mid之前，Attach后继续仍无新broker订阅交付；下一call正常 |
| detach | 149 | 6 / 6 | 7 / 7 | A在site0 Before内logical detach，仍收到当前child After和context Dispose；未来site/call不交付A，B继续 |
| worker | 137 | 6 / 6 | 6 / 6 | 主线程已有current parent时发真实native worker；worker不增main capture/Before，实际wrong-thread诊断1 |
| nested-same | 172 | 4 / 4 | 8 / 8 | native callee真实递归新work，新physical token且自身context单独退休，外parent恢复 |
| nested-cross | 151 | 4 / 4 | 7 / 7 | 同一native栈跨slot19/18，新channel/context配对，外parent未借错 |
| foreign-original | 91 | 4 / 4 | 2 / 2 | original-only child不借外parent，brokerKnown=false；不把Unknown后跳过的新交付计作正常采集 |
| fault-before | 100 | 6 / 6 | 1 / 2 | managed Before故障被包含；peer未成功capture也保captured=false清理；原函数及后续恢复调用各执行一次，Unknown |
| fault-after | 103 | 6 / 6 | 2 / 2 | managed After故障被包含，两个context与后续原函数均保留正确生命周期，Unknown |

12个mode的failures/nativeFailures=0、nativeInFlight=0、diskHashesUnchanged=true；normal/SEH/Cpp的actualLedgerCaptureVerified=true。其他模式的ledger字段为null或false，不能把它们写成ledger覆盖。11种broker模式均sharedProductionListenerCoreVerified=true，AV模式为false。12种firstInstallReceiptUsed、actualIl2CppMetadataResolved、gameCodeExecuted、gameRuntimeVerified、firstInstallSafetyVerified、delegateLeafExecutionVerified和allFutureListenersClosed全部false。

late-attach测量的是broker capture/mid/listener/after/fault/probe/context交付没有被旧call继承；没有单独统计raw CLR delegate入口次数，不从a.Total/b.Total为0推断raw入口必为0。不可变native descriptor snapshot的底层独立证据仍见[Bootstrap 698检查](native-bootstrap-evidence.json)，与本次broker观测分开。

异常记录实际读到SEH flags128、C++ flags129、AV flags0，分别3/4/2参数；SEH固定payload、work与site、C++ typed payload及MSVC x64四参数、AV读地址与helper模块RIP有核验。所有mode明确exceptionFlagsTransparencyVerified=false：记录的是外层native catch实际得到的record，没有逐级first-chance/handler对照证明完整Flags、Context、ExceptionRecord内容与所有异常语义全透明。异常始终在自有native DLL内catch，不穿过CLR P/Invoke；managed observer fault同样不抛回unmanaged，不要求native BeforeFaulted flag自动知道CLR已包含的异常。

## 生命周期与真实 PC 边界

新DLL只编译复用未修改的ListenerCaller.asm与ListenerFixture.h，在新CPP提供独立RtLfBefore/LoopCheck/After/DirectBody/VirtualBody。它没有游戏映像、IL2CPP导出调用或可伪造的production Ready。Setup各target有真实5/5/6 pre宽度、6/9/9 post完整重放、真实Dobby原trampoline、动态3RF chain、四边界0/5/7/9各自的真实OS lookup和受控栈展开；每target共8次fixture源写，两个target总16次。首写前展开使用自有受控栈，不是在执行生产游戏栈。

fixture的midReturn相对parent为+5Dh、三sourceReturn为+C1h/+F0h/+11Bh；生产固定映像相应为+140h和+195h/+1E7h/+245h。ledger从broker私有current identity取得真实configured PC，fixture没有把机器frame改写为游戏PC。受控LedgerBackend提供的module基值仅满足ledger的逻辑target关联检查，不是实际加载的GameAssembly基址；它无法进入生产backend或绕过RequireReady。生产返回PC仍来自真实V2发布凭据。

parent V1 observer与listener observer是独立快照，不是聚合V2 observer。本次V2若提及，指上一批416字节生产发布凭据。native listener可在parentManagedToken0下合法存在，但broker不由0制造context。target-read AV的After可在Before尚未调用时发生，未来adapter只能按flags做native责任清理，不能由token0推已捕获对象。故障RIP是真实helper读指令，不声称还原成旧source CALL地址。

fixture最多128条work，事件、动态内存、RF、模块和已配置slot永久保留，不提供Destroy/slot复用。ReadWork state0/1只复制immutable身份与atomic hold状态，state2的Interlocked发布后才复制变动counter。hold15秒、worker等待30秒、runner每进程60秒有界；worker超时不伪称state2或终止原调用。测试在部分Attach/Detach边界强制managed GC后仍成功，证明所测托管委托/观察者保活；不等于原生IL2CPP GC或移动对象/handle故障证据。

## Path 与仍缺验收

P-attach，path_type=callflow：自有DLL Setup真实配置/发布两target（E-fixture/F-own-channel）→C#核实际Info336与lane176→生产前门拒绝无receipt fixture（E-front/F-production-front）→内部configured共用core一次采用parent和listener，永久Attach（E-bridge/E-broker/F-current）→真实ASM source CALL进入CLR，校current parent与copy frame（E-run/F-copy）。该path终点是自有fixture实际回调，不是固定游戏启动。

P-ledger，path_type=callflow：native parent Enter创建retained scope→真实mid给actual list与私有current token→source Before保存child责任（E-broker）→实际ledger先追加dispatch→受控backend供actual地址对应的只读graph/root记录→callee真实执行并硬件返回到实际source post→post CLR After标量完成→parent正常After/最终Dispose/probe退出（E-ledger/E-joint/E-run，F-ledger/F-cleanup）。生产Backend两阶段真实根采用是另一条仅静态核查的分支E-backend/F-batch-roots，不能混入动态ledger backend证据。

P-abnormal，path_type=callflow：原SEH/C++或helper虚目标read AV→native unwind经parent finally先关闭child→正常已捕获child走Abnormal After，AV特殊child可Before缺失/token0→parent Abnormal Exit→自有native catch记录异常并结束→broker/ledger保Unknown责任，根不释放（E-fixture/E-run，F-cleanup/F-target-av）。AV child只证明直接bridge，不证明该path中broker/ledger AV分支已经执行。

相较原生lane批八项缺口，本轮对target-read AV和foreign-original各取得上文限定场景的新动态证据，不能再写“完全未验”。仍缺动态vtable mutation、foreign/overflow所有组合、missing-post破坏、完整exception Flags/record/context透明性、pre helper每条指令展开、invokable内部全部delegate leaf、mutation/StopCoroutine与全消费者退休。还有真实游戏首次冷启动和九区执行、生产listener→CLR/ledger、真实metadata/根/GC、provider转交与取消、完整Retry/Return/Loader。TargetRead AV的broker/ledger整合分支、生产两阶段Root故障注入、未确认capture且native正常返回的ledger专门分支也未测。

RequestPlay关闭；Pending=true、MutationClosed=false、InlineExecutionUnobserved=true、DispatchPathVerified=false保持。native invokable dispatch配对不等于delegate leaf已执行或future闭包。只保留B03整项/B18a子项既有勾选，用户“先代码、实测稍后”继续生效。

## 完整终态与冻结

[build-run](build-run.json)记录targeted22041于11:45:11 UTC、full21231于11:51:14 UTC完成且真实终态exit0；本地完整汇总25套1521/1521，Runtime160、Hooks177、Audio/Content573、Visual71、UI/input79。新增6组Hooks controlled checks与12mode1630互为不同口径，1630不并入1521，也不与targeted复跑相加。111条去重diagnostic raw lines包含ANSI显示变体、既有Audio CS0649/CS8602及upstream，不代表111个新问题，不宣称零warning。旧native listener8349、旧broker CLR379和既有映像/atomic/采用等fixture均继续复跑，范围仍按各自JSON。

本 agent 实际比较[targeted冻结输入](targeted-inputs-before.json)、[full构建前输入](build-inputs-before.json)、[归档manifest](source-manifest.json)与当前502个文件，逐项相同、当前hash零变化；[输入proof](build-input-integrity-check.json)一致。实际打开[源码ZIP](source-inputs.zip)核502个entry、[包ZIP](staged-package.zip)核22个entry，均与manifest匹配，当前暂存22项也全部匹配。[差异](changes-from-listener-publication.json)为新增9/修改10/删除0。声明输入/归档的一致性不覆盖外部工具链、缓存依赖和B19完整干净环境复现矩阵。

| 冻结产物 | 实际SHA-256 |
|---|---|
| [源码ZIP](source-inputs.zip)，[502项proof](source-archive-check.json) | 103a371b7aea4344593841508f4fc6b28d3180011b3b7f7207a96e2fa2a61590 |
| [源码manifest](source-manifest.json) | be6e8c97d7500c5f23f97bf179b7b5cb4398068c6a3acd5a4659385013b7a53e |
| [22项包ZIP](staged-package.zip)，[包proof](package-archive-check.json) | a962d2576981b6768ae4e43f025a56245c3a8ee71588fff89f57fd123d757791 |
| [完整日志](build.log) | 72ea3d3c0ee2a1e5626e64f35172b6451952f349804e4292dc14396a8e9e1c59 |
| [定向日志](targeted.log) | 149b1b1e4d8f4dac2f190dd3b34a4142fb5b79f725d264d757a50861e13a19b6 |

只读[旧安装proof](previous-installation-check.json)仍为1021批18项全部匹配、326项历史归档已核；本 agent 再读当前18项安装文件与[原游戏proof](original-game-check.json)三个文件的hash，均未变。[暂存proof](staging-check.json)与runner明确installed=false。最终已验证是离线全量、自有DLL的真实CLR回调、受控graph/root上的实际ledger和本文明确的有限异常/生命周期场景，不包含新桌面/game/UI/安装验收。
