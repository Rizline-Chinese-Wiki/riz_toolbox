# 底层 Wait 发布与回调责任报告

2026-10-26；普通固定 PE 研究及自有验证，flavor=null。**N 批本轮受控验证已完成并归档，整个兼容底层及生产完成度不由本报告宣告。** 最终管理 r3 为 761/28/10；Wait 联合 native r2/八进程 r2 为 1,590 CLR/1,859 native 断言；collector r2 实际 exit 0、4,847 核验。

本批增加 wait 自身发布代次、在途 callback/cleanup、字段撤回及 shared reference 归还的独立观察。Kernel、自有原生夹具、管理回归和真实 Native→CLR Wait 联合分别通过，最终冻结源与原件已归档，证明各自受控范围内的合同。没有安装或执行真实游戏，也没有证明全写者/派发者、未来来源封口、引擎取消或最终资源根释放。生产权限仍关闭。

## 范围

继承[固定生产研究 scope](validation/foundation-engine-production-20261025/scope.md)与[N 静态合同](validation/foundation-engine-wait-20261026/contract-notes.md)：用户授权继续完善公共兼容底层并协作，现阶段继续代码和受控验证，游戏实测稍后。历史 scope 的进度不替代 N 证据。真实游戏目录保持只读；测试仅涉及自有进程、owned allocations、本地预编 Kernel。

本报告只读取既有原件、日志与 SHA；文档编写没有运行构建、collector 或游戏。以下动态结果由唯一 builder 已实际运行。无攻击或恶意软件结论，不套 IOC/ATT&CK 结构。

## Evidence

source_type=file/log，observed_at=2026-10-26。artifact_path 以缓存绝对路径表达；E18 的 collector 已产原件与归档相对路径映射。原件不重写，复跑使用新目录。

| ID | source_ref | content_hash（SHA256） | 内容 / repro_command |
|---|---|---|---|
| E1 | D:/riz-build-cache-next/engine-wait-kernel-r2-20261026/RizToolbox.EngineNodeKernel.dll | 1541feaa8fe7b37d0d3a544fbb315387ea93e3f6b96a4dc955dc76dc9c30e38a | 133,120 字节；Get-FileHash -Algorithm SHA256 -LiteralPath 指定本行绝对路径 |
| E2 | D:/riz-build-cache-next/engine-wait-kernel-r2-20261026/wait-pe-verification-r1.json | f2a8d9181c70dfdc8df95ac0e8bc8e77032e46bfbded545807b99cbfabd76782 | passed=true，46,395 项；verify-wait-kernel.py --directory 指定目录 --output 新文件，仅磁盘核验 |
| E3 | D:/riz-build-cache-next/engine-wait-kernel-r2-20261026/kernel-manifest.json | 4e582daa17e05ff331613306d2a112c38d362222cb5fcd0e387882f4ba14c44e | 13 源、2 产物 SHA；3 typed/13 physical/18 phase，sourceHashesUnchanged=true |
| E4 | D:/riz-build-cache-next/engine-wait-fixture-cases-r2-20261026/fixture-result.json | f27774ec921a244492d9f46e048beae54f4752444584acde83adfffca7d57eb7 | 22/22，14,989 断言；PID、exit 0、日志/raw/JSON；完整复跑命令见 README |
| E5 | D:/riz-build-cache-next/engine-wait-fixture-r2-20261026/wait-fixture-build.json | 7d971265b3457aa38de105441c9be7d1b3ae533e02f736009657269ec922d0ab | buildSucceeded=true、exit 0；源/产物 SHA，与 E4 Kernel 一致 |
| E6 | D:/riz-build-cache-next/engine-wait-managed-checks-r1-20261026/test-result.json | f16b2c65de66c93e8bca016dd67970a0174f9b07d3d0361b3209a902be7bdd6e | passed/sourceHashesUnchanged=true；759 Audio、28 coordinator、10 receiver；actualNativeWaitClrIntegration=false |
| E7 | D:/riz-build-cache-next/engine-wait-managed-checks-r1-20261026/managed-project-sources.zip | 56b6b3e92f12354f4fdfb2e3d5a0a165b3bbb0902e8940390081db35186ca6fb | 当次构建前精确 470 输入；同目录 source-before.json 是清单，不承诺随后源不变 |
| E8 | D:/riz-build-cache-next/engine-wait-scheduler-regression-cases-r1-20261026/fixture-result.json | d9110a8acb598edb8510182b3be71eca6dde666a4cdc064fd9a12877f6820da9 | 同一 Kernel 旧 M 原生 16/16，3,718 断言，单列回归 |
| E9 | D:/riz-build-cache-next/engine-wait-fixture-cases-r1-20261026/fixture-result.json | 102c6608fbe2119a7610d2341b97342fc69a9f4bcd6150630a8159044da2ac9b | 历史 17/22、5 失败；未覆盖原件，E4 是修后新运行 |
| E10 | D:/riz-build-cache-next/engine-wait-scheduler-regression-joint-cases-r1-20261026/fixture-result.json | 0ed672ad338530a118ecc26bfacb4bf8462f1dedf4f16b034347cf388a3298b7 | 当前 Kernel/managed r1 的旧 M 真实 Native→CLR 7/7，654 CLR/548 native 断言；同轮回归单列，不能代 Wait 联合 |
| E11 | D:/riz-build-cache-next/engine-wait-managed-checks-r2-20261026/test-result.json | f3b883ed7853c03ca9131d81a986356f343364059c3ec0a0d5fad1f02b9de188 | 中间真实 passed/sourceHashesUnchanged=true；760 Audio/28 coordinator/10 receiver |
| E12 | D:/riz-build-cache-next/engine-wait-joint-cases-r1-20261026/fixture-result.json | 3206f7a48d9cadb7885cb30c4e7a12ac532b9dfb304d0a9c39e254a97548c734 | 中间真实 8/8、1,567 CLR/1,858 native 断言；已由 E16 的后续精确合同重验补充 |
| E13 | D:/riz-build-cache-next/engine-wait-managed-checks-r2-20261026/managed-project-sources.zip | 41794b5f9b2f2aa7a1fd4e4ae0f8c41a5480e8abb2fa6b3f02e7c5ecfefaf2e6 | managed r2 当次精确 471 输入；不覆盖 r1 的 470 输入归档 |
| E14 | D:/riz-build-cache-next/engine-wait-managed-checks-r3-20261026/test-result.json | 388107ec467b8e578e40664bb8594de5411ffb42480a80cd62ebc3914e566f1d | 最终真实 761 Audio/28 coordinator/10 receiver；passed/sourceHashesUnchanged=true |
| E15 | D:/riz-build-cache-next/engine-wait-managed-checks-r3-20261026/managed-project-sources.zip | 13675e598424c1c678d0820147fd5cdb8ba858694d03fc821aa5b055bdcbb243 | 最终精确 471 输入，逐 entry SHA 验证；不替换 M 联合 r1 的 470 输入 |
| E16 | D:/riz-build-cache-next/engine-wait-joint-cases-r2-20261026/fixture-result.json | fa92f4111a62e87c6e88bad9ad498112469ecd0bc765b5ec821b52d898523dc5 | 最终八模式全部 exit 0、1,590 CLR/1,859 native；actualNativeWaitClrIntegration=true |
| E17 | D:/riz-build-cache-next/engine-wait-joint-native-r2-20261026/wait-managed-fixture-build.json | 2debc8ff41743c58d094b9147418e51b405b0fc9f303228c067549706d447e7c | 实际 native joint 严格构建 exit 0；E16 输入 Kernel/DLL 与此 manifest 对齐 |
| E18 | D:/riz-build-cache-next/engine-wait-evidence-r2-20261026/wait-evidence.json | fd8620f886755df0e10402a81b2cd52273393879425444739990ddb605c97a20 | collector r2 实际 exit 0、4,847 核验，504 份原件；passed/available/fullBatch/actualWaitIntegration=true |
| E19 | D:/riz-build-cache-next/engine-wait-evidence-r2-20261026/native-build-sources.zip | dcafb6831a923a67d38d8e562e1ca75a6416edb76684e3b234eb5afce07b6d7c | 当次 manifest 对应 28 native 源，逐 entry SHA 核验，保精确旧/新源归属 |

固定游戏 9 个完整函数组/60 个精确点和样本 SHA 的独立证据见[contract-notes.md](validation/foundation-engine-wait-20261026/contract-notes.md)，不把自有动态函数冒充游戏原函数。

管理 r1 实际日志末行分别为 759/759 audio/content checks passed、Engine managed checks: 28/28 passed、10/10 passed。Audio 构建有 17 个测试代码警告、0 错误；Runtime 为 0 警告、0 错误。28 是 coordinator 专项，不是全部 Runtime 测试数。759/470 是 r1 的准确冻结；RetiredBatch 第 18 案例及 Wait 联合源码进入 managed r2 后，760/28/10 已实际通过，精确 471 输入单独保留（E11、E13）。晚 callback-null 修复及新增第 19 案例进入最终 r3 后，761/28/10 已实际通过；Audio 仍为 17 个测试代码警告、0 错误，Runtime 0 警告、0 错误（E14、E15）。E10 来自另一次真实进程矩阵，不能把 E6 内未执行 scheduler 联合的 false 标志改成 true。

## Findings

### F1：Wait 要独立物理 scope 与代次

severity=n/a_re；category=design；status=validated（限实现与受控验证）；confidence=high；evidence_ids=[E2,E3,E4,E14,E16]。location=EngineWaitContext、NativeEngineWaitObserverDispatcher、NativeEngineWaitLedger。

3 个 typed 入口保实际四整数寄存器输入与完整 raw RAX；既有 4 Node、6 Scheduler dispatcher 以 link-private 调用转交实际 scope，共 13 种真实物理边界。foreign、observer、未 Attach 边界都占栈，不可越过中间边界借祖先。sequence、external/source/operation sequence 由 dispatcher 生成，没有允许任意调用方设置来源的公共接口。全部写者与全部派发路线仍未证明。

### F2：发布、owner 撤回、字段撤回与引用归还各自成立

severity=n/a_re；category=reverse_algo；status=validated（固定静态合同与自有分支）；confidence=high；evidence_ids=[E2,E3,E4,E14,E16]。location=18 Wait phase、owned Yield/Start/Release/Detach。

PublishBegin 只登记尝试；Committed 需要同一次原生 scope 的实际写入分支和外层退出。occupied/isDone fallback 不产生成功发布；夹具实际调用 IsDone 虚函数并归还 node 借用引用。旧 wait 引用归还与新 wait 提交分开，历史 publication generation 不被新地址字典覆盖。

Detach direct/nested 仅清 owner；已在途 callback/cleanup 仍独立。Start/Release 清 owner、callback、cleanup、payload 与 shared reference 归还分阶段；引用归还后只用缓存，不读旧 wait。node referenceCount 与 wait sharedReferences 不是一个计数。写前阶段只发 Begin/Reached，不提前称写完。

### F3：使用原 CALL caller，callback 后重读 cleanup/payload

severity=n/a_re；category=design；status=validated（自有实际 CALL 与回执）；confidence=high；evidence_ids=[E3,E4,E14,E16]。location=DispatchCallbackArmed/CleanupDecision、BatchObjectBegin、实际子调用。

原 CALL 保留在源位置：callback caller=Armed site+0xA，cleanup=CleanupDecision+0xB，Batch cleanup=ObjectBegin+0xD。relay CALL 返回 PC 独立标识 side probe，不能替代原 caller。普通 Dispatch 在 callback 后重读 cleanup/payload；callback-mutate 实际修改两者。owner-null 跳 callback 仍 cleanup。Batch callback-null 不 cleanup，但仍返 shared reference。高位 EAX 完整保留，不把 AL 非零当作 EAX==1。

异常/Unknown 后没有 cleanup claim，就不能把最后非零 sourceExternalSequence 视为 cleanup。夹具按 CallbackClaimed/CleanupClaimed 位选择真实 child，逐项核 kind、external/source/operation sequence、caller、Exit 和 completion；未 claim 不补签。

### F4：自有重放/展开通过，不等于生产安装安全

severity=n/a_re；category=other；status=validated（限自有窗口）；confidence=high；evidence_ids=[E2,E4,E5]。location=WaitCaller.asm、WaitFixture.cpp。

18 自有窗口实际执行 E9→near relay→真实 CALL probe→重放→原 continuation，相对分支/RIP 重新定位，callback/cleanup/shared-return 原 CALL 保留。relay CHAININFO 指向各自完整源 RF，并实际 RtlVirtualUnwind。

**只有 9 窗口**（phase 0、4、5、6、8、9、11、15、17）比较全 16 GPR、16 XMM、RFLAGS、MXCSR、x87 控制字。其余按实际分支、caller、缓存与阶段顺序验证，不称 18 处全状态配对。固定游戏最小跨度存在内部跳入覆盖尾部风险，自有规范窗口通过和 CET/展开元数据核验均不能授予生产 publisher 安全。

普通释放以 checked MEM_RELEASE 和原代次证明，不要求之后裸地址仍空闲。地址复用只证明 owned reservation 内真实 DECOMMIT/COMMIT、新代次以及 MEM_RESERVE/AllocationBase/Protect，不声称系统释放后重获同址。

### F5：实际 Native→CLR Wait 已验证，晚 null 与无 callback 路由准确分流

severity=n/a_re；category=design；status=validated（本批受控矩阵）；confidence=high；evidence_ids=[E14,E15,E16,E17,E18]。location=最终管理 r3、Wait 联合 r2、实际 ledger/dispatcher。

normal、owner-null、high-eax、start-clear、batch-null、callback-clear、replace、republish 八个真实独立 dotnet 进程通过实际原生 dispatcher 进入 CLR，由同一实际 node identity、scheduler row 和 wait ledger 消费回执；没有用受控 Send 伪装原生回执。nativeInFlight=0、ledgerOutstanding=0、Pending=false、Unknown=false、真实 Typed Complete→Dispose 和逐代账本门已核验。tracker identity providers 仍受控，actualTrackerBackendExecuted=false；联合节点存储使用自有 reservation 内 MEM_DECOMMIT、nodeStorageReleasedToSystem=false，不称系统 MEM_RELEASE。

晚到 callback-null Dispatch 的误 Unknown 分支已修。replace 实际执行老 Dispatch、精确 callback-null 老 Dispatch、新 Dispatch，共 3 次；lateNullDispatchCaptures/Completes/Disposes 各 1、actualLateNullDispatchVerified=true，其他七模式 false/0。owner-null/start-clear 无 M callback 时 schedulerMaskRouteConfigured=true、actualSchedulerMaskRouteVerified=false，其余精确等于 nativeCallbacks>0，并核 nativeCallbacks==capturedCallbacks。E12 的中间结果没有被覆盖，E16 才证明修复后的合同。

### F6：受控证据完成仍不授予生产取消、封口或根释放

severity=n/a_re；category=design；status=validated（结论边界）；confidence=high；evidence_ids=[E2,E4,E14,E16,E18]。location=collector 边界标志、固定生产 scope、README。

本批受控矩阵和精确源归档已完成，不代表整个兼容底层或生产安装完成。全部 wait 写者/派发者、全来源封口、剩余消费者、生产安装窗口和真实游戏尚未闭合。allWritersProven、allDispatchersProven、futureSourcesClosed、productionPatchInstallationVerified、productionAdmissionVerified、gameRuntimeVerified、engineCancellationCompleted 继续为 false。collector 的 fullBatchVerified=true 仅指此脚本要求的受控证据齐全，不是这些全局标志的替代。

## Path

P1：已知普通 wait 派发；path_type=callflow；起点=实际 Yield/Dispatch；目标=准确记录已知责任，而非授予全局退休。

| 步骤 | 行为 | Evidence / Finding |
|---|---|---|
| 1 | 实际 Yield 与 PublishBegin 登记尝试，缓存旧 tuple/node 代次 | E3、E4 / F1、F2 |
| 2 | 旧 shared-return 原 CALL 归还，后点用缓存；Committed 对应实际写入分支 | E4、E5 / F2 |
| 3 | Dispatch 实际 owner lookup；非 null 按原 Armed caller 关联 Scheduler Callback | E3、E4 / F3 |
| 4 | callback 完成后重读 cleanup/payload，以原 cleanup caller 关联 Node Release | E4、E16 / F3 |
| 5 | 子调用完成、clear reached、外层 Complete→Dispose 分别保责任；Unknown 不补签 | E4、E16 / F1、F3 |
| 6 | 修后联合精确验证 late-null 与原代次，冻结源与原件；生产来源仍未封口 | E14–E19 / F5、F6 |

Residual risks：全写者/派发者、多字段快照的真实线程/锁一致性、剩余 managed 通知、Batch 容器与全部 yielded 对象归属、生产首次执行窗口及真实游戏生命周期。

## 失败与修复时间线

- 2026-10-26：固定磁盘合同记录 9 完整函数组/60 精确点；未执行游戏。
- Kernel r2 磁盘 46,395 项通过；同 Kernel 旧 scheduler 16/16 通过（E2、E8）。
- Wait r1 UTC 09:15:29–09:15:32 实际 17/22。四个原函数异常模式及 observer-seh 最终 MXCSR 从 0x3F80 变 0x1F80；observer-seh 另有 caller 断言失败（E9）。
- 定位为夹具错误：kind18 是普通 C++ 异常 telemetry，没有 ASM wrapper，不能故意 clobber MXCSR；非零 sourceExternalSequence 可能是 callback，不能提前当 cleanup。修复保留状态恢复断言与严格真实来源核验，没有取消 Unknown 或补签 cleanup。
- Wait r2 UTC 09:31:06–09:31:15 严格构建 exit 0；UTC 09:33:29–09:33:32 实际 22/22、14,989 断言，全部 exit 0（E4、E5）。r1 原件保留。
- 管理 r1 passed=true，源 SHA 不变、输入精确冻结；仍显式无 Native→CLR Wait 联合（E6、E7）。
- 同一新 Kernel 及 managed r1 的旧 M Native→CLR 实际 7/7、654 CLR/548 native 断言通过；这是 N 对旧 M 的真实回归，不改称 Wait 联合（E10）。
- managed r2 实际 760/28/10，通过且源 SHA 不变；Wait 联合 r1 实际 8/8、1,567 CLR/1,858 native 断言通过。r1/r2 精确归档都保留（E11–E13）。
- 随后只读发现晚到 callback-null Dispatch 的误 Unknown 分支，根修复并增第 19 回归；producer/runner 将无 callback 的 routeVerified 改为 false 并增加 configured=true，collector 同步精确规则。
- 最终 managed r3 实际 exit 0、761/28/10、sourceHashesUnchanged=true；native joint r2 严格构建 exit 0。Wait 联合 r2 UTC 10:32:09–10:32:14 八模式全部 exit 0，1,590 CLR/1,859 native；晚 null 三 Dispatch/各一次收尾实过（E14–E17）。
- collector r1 UTC 10:33:14–10:33:20 实际 exit 0、4,796 核验/490 原件，作为历史保留。最终 collector r2 再补自身脚本、四类 run.runner 原字节与联合 native build 全清单产物，实际 exit 0、4,847 核验/504 原件；28 native 源与471 managed 输入逐 entry SHA 对齐，M 联合独立 r1 的470输入冻结另存（E18、E19）。两次 native ZIP 内容逐项一致，ZIP 时间不同导致整体 SHA 不同。

raw 写出时保存断言快照，之后 fwrite/fflush/fclose 三项 I/O 检查才完成。JSON 显式 rawChecks/rawFailures/persistenceChecks=3；runner 核 raw 头/长度/SHA、rawFailures=0、JSON 总 checks=rawChecks+3，不把未来 I/O 检查假称已存在先前字节中。

## 重现与归档

完整可执行命令与新输出目录见[README](validation/foundation-engine-wait-20261026/README.md#复跑与归档)。Build/Test 只操作自有 fixture，KernelDirectory 引预编 Kernel，不反复构建 Kernel；失败保实际 PID、exit、stdout/stderr、raw、JSON 和输入 SHA。

[Collect-EngineWaitEvidence.ps1](../scripts/Collect-EngineWaitEvidence.ps1)只读取、核验和复制既有证据。唯一 builder 已实际执行，最终 r2 exit 0、4,847 核验，passed/availableEvidenceVerified/fullBatchVerified/actualNativeWaitClrIntegration 均 true（E18）。收集 504 份原件，精确保全自身 collector、四类 run.runner 与联合 native build 全清单产物；28 native 源 ZIP 和 managed r3 471 输入 ZIP 逐 entry SHA 检查通过；M 联合回归仍绑定自己的 managed r1 470 输入 ZIP。失败 r1、中间 r1/r2 原件均保留，未替换历史字节。

collector 精确脚本 SHA 为 9bd411d11e280ab3da33e3669d55510eee884e90645163df4f8e406d0515856f，本轮文档收尾未改。重收集必须用新目录；[README](validation/foundation-engine-wait-20261026/README.md#复跑与归档)使用只读确认不存在的 r4 示例，绝不将已执行 r1/r2 当作新输出。native 活跃源若漂移，collector 会拒绝重造历史冻结；应使用现有精确证据包，而非混入后来源码。

不传 JointDirectory 时，passed/fullBatchVerified/actualNativeWaitClrIntegration 仍为 false。此次 true 来自真实八进程与对应 input/build/managed freeze，不由测试数量或受控模型推定。所有生产安装、真实游戏、全来源封口和引擎取消标志仍 false。下一步进入生产 transport/安装窗口研究，再独立补生产与游戏证据。
