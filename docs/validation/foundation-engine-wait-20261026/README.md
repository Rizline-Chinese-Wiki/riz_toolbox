# N 批：Wait 发布与回调责任

**N 批本轮受控验证已完成并归档。** 已完成 Kernel 磁盘核验、独立原生 Wait、管理回归、真实 Native→CLR Wait 八模式，以及新 Kernel 的旧 scheduler 回归。这里的“完成”只指本批受控矩阵；整个兼容底层、生产安装、真实游戏、全部来源封口和引擎取消仍未完成。

| 证据层 | 最终实际结果 | 边界 |
|---|---|---|
| Kernel r2 磁盘核验 | 46,395 项 | 磁盘 PE/ABI/展开/哈希，不加载游戏 |
| 独立原生 Wait r2 | 22/22，14,989 断言，全部 exit 0 | 自有函数/窗口，不能推成游戏实测 |
| 管理侧 r3 | Audio 761/761、coordinator 28/28、receiver 10/10，源 SHA 不变 | 28 是 coordinator 专项，不是全部 Runtime 测试 |
| Native→CLR Wait r2 | 八模式全部 exit 0；1,590 CLR/1,859 native 断言 | 同一实际 Node/M/Wait 账本；仍不授予全来源封口 |
| 同 Kernel 旧 scheduler 原生 | 16/16，3,718 断言 | 单列 N 对旧 M 的回归 |
| 新 Kernel/管理 r1 的旧 M Native→CLR | 7/7，654 CLR/548 native 断言 | 独立保留它自己的 managed r1 冻结 |
| collector r2 | 实际 exit 0，4,847 核验；504 份原件，28 native 源、471 managed 输入逐条 SHA 验证 | passed/fullBatchVerified 只表示此受控证据矩阵完整 |

管理 r3 Audio 构建保留 17 个测试代码警告、0 错误；Runtime 为 0 警告、0 错误。管理 r1 的 759/470、r2 的 760/471 与 Wait 联合 r1 的 1,567 CLR/1,858 native 中间结果均保留；它们不覆盖最终 r3/r2，也不伪称已经包含后续修复。

晚到 callback-null Dispatch 已修复并实际验证：replace 中老 Dispatch→精确 callback-null 老 Dispatch→新 Dispatch，共 3 次；晚 null 的 Capture/Complete/Dispose 各 1。其余七模式该标志为 false、计数为 0。schedulerMaskRouteConfigured=true 与实际路由分开：owner-null/start-clear 无 M callback，actualSchedulerMaskRouteVerified=false；其余按实际 nativeCallbacks>0 核验。

## 阅读入口

[固定游戏静态合同](contract-notes.md)保留 9 个完整函数组、60 个精确点及未闭合边界；[正式报告](../../2026-10-26_底层-wait回调责任-report.md)给出 Evidence → Finding → Path、原件 SHA 与失败修复。

[EngineWaitContext.h](../../../native/RizToolbox.JudgeContext/EngineWaitContext.h)冻结 ABI 1：Frame 240、Completion 48、Phase 288、Config 432、Observer 40、Info 176 字节。[C++ 夹具](../../../native/RizToolbox.EngineWaitFixture/WaitFixture.cpp)及[ASM](../../../native/RizToolbox.EngineWaitFixture/WaitCaller.asm)仅操作自有进程与 owned allocations。范围继承[固定生产研究 scope](../foundation-engine-production-20261025/scope.md)中的本地源码、磁盘 PE 和受控验证；历史 scope 进度不替代本批事实。游戏目录只读，没有网络、安装或游戏动态证据。

## 这层记录什么

Wait 发布、scheduler 行、在途 callback、cleanup、字段撤回与 shared reference 各自有责任。3 个实际 Wait typed 入口（Yield、Dispatch、Batch）结合既有 4 Node 和 6 Scheduler dispatcher 私有物理 scope，共 13 种真实边界。来源由实际 dispatcher 签出，没有调用方可设置 sequence 的公共接口。foreign、observer、未 Attach 边界同样占物理栈，不能越过中间边界借祖先。

18 phase 分别记录发布 Begin/Committed、旧引用归还、Detach direct/nested owner 撤回、Start/Release 四字段与引用归还、普通派发 owner/callback/cleanup/clear、Batch 每对象 Begin/End。写前事件只是到达点；字段写入、引用归还、node 引用下降和外层 Complete→Dispose 不能互相替代。

普通 callback 原 CALL 留在源窗口后，实际 caller=Armed site+0xA；cleanup=CleanupDecision+0xB；Batch cleanup=ObjectBegin+0xD。relay CALL 返回 PC 独立标识探针，不能冒充原 caller。callback 后真实重新读取 cleanup/payload；owner-null 只跳 callback，仍可 cleanup。Detach 只清 owner，不撤已经在途的责任。Batch callback-null 不 cleanup，但仍返 shared reference。

## 实际覆盖

22 模式：publish、occupied、is-done、replace、owner-null、callback-mutate、detach-inflight、detach-nested、start-clear、release-clear、batch、high-eax、callback-seh、cleanup-seh、callback-cpp、cleanup-cpp、observer-seh、observer-reenter、nested-foreign、worker、late-attach、address-reuse。

独立原生的各场景记录真实 PID、exit、stdout/stderr、JSON 与 154,752 字节 raw。所有 18 自有窗口执行 E9→relay CALL→重放→原 continuation，含对应完整源 RF 的 CHAININFO 和实际 RtlVirtualUnwind。**仅 9 窗口**（phase 0、4、5、6、8、9、11、15、17）逐项比较 16 GPR、16 XMM、RFLAGS、MXCSR、x87 控制字；其余按真实分支、caller 与阶段顺序验证。不能称 18 窗口均做全寄存器比较。

独立原生普通释放核真实 checked MEM_RELEASE 和原分配代次，不以释放后裸地址仍为 MEM_FREE 作为证据。address-reuse 只证明自有 reservation 中真实 DECOMMIT→COMMIT，新代次、MEM_RESERVE/AllocationBase/Protect 严核；不声称系统释放后重新取得同地址。ReferenceReturned/Batch End 使用缓存，不回读已归还对象。

八个真实 Native→CLR 模式为 normal、owner-null、high-eax、start-clear、batch-null、callback-clear、replace、republish。联合夹具通过实际原生 dispatcher 进入 CLR，由同一实际 node identity ledger、scheduler row ledger 和 wait ledger 消费回执；tracker identity providers 仍受控，actualTrackerBackendExecuted=false。联合节点存储使用自有保留区内 MEM_DECOMMIT，nodeStorageReleasedToSystem=false，不应与独立原生 MEM_RELEASE 计数混称。

固定游戏最小覆盖窗存在跳入被覆盖尾部的风险。自有规范窗口通过不证明生产 publisher 安装安全。allWritersProven、allDispatchersProven、futureSourcesClosed、productionPatchInstallationVerified、productionAdmissionVerified、gameRuntimeVerified、engineCancellationCompleted 均保持 false。

## 权威原件与冻结源

| 内容 | 绝对路径 |
|---|---|
| Kernel/磁盘核验 | D:/riz-build-cache-next/engine-wait-kernel-r2-20261026 |
| Wait 构建 | D:/riz-build-cache-next/engine-wait-fixture-r2-20261026 |
| Wait 22 进程 | D:/riz-build-cache-next/engine-wait-fixture-cases-r2-20261026/fixture-result.json |
| 旧 scheduler 16 进程 | D:/riz-build-cache-next/engine-wait-scheduler-regression-cases-r1-20261026/fixture-result.json |
| 新 Kernel 旧 M 联合回归 | D:/riz-build-cache-next/engine-wait-scheduler-regression-joint-cases-r1-20261026/fixture-result.json |
| 管理最终 r3 与精确源 ZIP | D:/riz-build-cache-next/engine-wait-managed-checks-r3-20261026 |
| Wait 联合最终 native r2 | D:/riz-build-cache-next/engine-wait-joint-native-r2-20261026 |
| Wait 联合最终八进程 r2 | D:/riz-build-cache-next/engine-wait-joint-cases-r2-20261026/fixture-result.json |
| 已实际通过的最终证据包 | D:/riz-build-cache-next/engine-wait-evidence-r2-20261026/wait-evidence.json |
| 管理 r1 历史 | D:/riz-build-cache-next/engine-wait-managed-checks-r1-20261026 |
| 管理 r2 中间结果 | D:/riz-build-cache-next/engine-wait-managed-checks-r2-20261026/test-result.json |
| Wait 联合 r1 中间结果 | D:/riz-build-cache-next/engine-wait-joint-cases-r1-20261026/fixture-result.json |
| Wait 历史失败 | D:/riz-build-cache-next/engine-wait-fixture-cases-r1-20261026/fixture-result.json |

| 冻结内容 | SHA256 |
|---|---|
| Kernel | 1541feaa8fe7b37d0d3a544fbb315387ea93e3f6b96a4dc955dc76dc9c30e38a |
| 最终 managed r3，471 输入 ZIP | 13675e598424c1c678d0820147fd5cdb8ba858694d03fc821aa5b055bdcbb243 |
| collector 的 28 native 源 ZIP | dcafb6831a923a67d38d8e562e1ca75a6416edb76684e3b234eb5afce07b6d7c |
| 最终 wait-evidence.json | fd8620f886755df0e10402a81b2cd52273393879425444739990ddb605c97a20 |
| M 联合独立 managed r1，470 输入 ZIP | 56b6b3e92f12354f4fdfb2e3d5a0a165b3bbb0902e8940390081db35186ca6fb |

最终 collector 还精确保全自身脚本、四类 run.runner 原字节和联合 native build 全清单产物；collector r1（4,796 核验/490 原件）作为历史保留。两个 native ZIP 内容逐项一致，ZIP 时间导致整体 SHA 不同。

冻结源只对应各自实际构建，不承诺随后活跃源码不变。r1 失败及中间原件不覆盖。

## 复跑与归档

以下在项目根目录、PowerShell 7.2+ 执行，由唯一 builder 统一运行。r4 示例输出在本次只读核对时均不存在；真正运行前仍应检查并选全新目录。本次文档收尾没有重跑构建、测试或 collector。

~~~powershell
& ./scripts/Build-EngineWaitFixture.ps1 -KernelDirectory D:/riz-build-cache-next/engine-wait-kernel-r2-20261026 -OutputDirectory D:/riz-build-cache-next/engine-wait-fixture-r4-20261026
& ./scripts/Test-EngineWaitFixture.ps1 -BinaryDirectory D:/riz-build-cache-next/engine-wait-fixture-r4-20261026 -OutputDirectory D:/riz-build-cache-next/engine-wait-fixture-cases-r4-20261026
& ./scripts/Build-EngineWaitManagedFixture.ps1 -KernelDirectory D:/riz-build-cache-next/engine-wait-kernel-r2-20261026 -OutputDirectory D:/riz-build-cache-next/engine-wait-joint-native-r4-20261026
& ./scripts/Test-EngineWaitManagedFixture.ps1 -BinaryDirectory D:/riz-build-cache-next/engine-wait-joint-native-r4-20261026 -ManagedDirectory D:/riz-build-cache-next/engine-wait-managed-checks-r3-20261026/AudioContent/bin -OutputDirectory D:/riz-build-cache-next/engine-wait-joint-cases-r4-20261026
~~~

[Collect-EngineWaitEvidence.ps1](../../../scripts/Collect-EngineWaitEvidence.ps1)只读/复制已有证据，不构建、不启动子进程或游戏。唯一 builder 已实际运行最终 r2，exit 0、passed=true、availableEvidenceVerified=true、fullBatchVerified=true、actualNativeWaitClrIntegration=true。脚本 SHA 保持 9bd411d11e280ab3da33e3669d55510eee884e90645163df4f8e406d0515856f；文档收尾未改脚本或既有证据。

下面将当前权威结果重收集到新的 r4 目录，不覆盖已完成的 r1/r2：

~~~powershell
& ./scripts/Collect-EngineWaitEvidence.ps1 -KernelDirectory D:/riz-build-cache-next/engine-wait-kernel-r2-20261026 -NativeBinaryDirectory D:/riz-build-cache-next/engine-wait-fixture-r2-20261026 -NativeCasesDirectory D:/riz-build-cache-next/engine-wait-fixture-cases-r2-20261026 -ManagedDirectory D:/riz-build-cache-next/engine-wait-managed-checks-r3-20261026 -SchedulerBinaryDirectory D:/riz-build-cache-next/engine-wait-scheduler-regression-native-r1-20261026 -SchedulerCasesDirectory D:/riz-build-cache-next/engine-wait-scheduler-regression-cases-r1-20261026 -SchedulerJointDirectory D:/riz-build-cache-next/engine-wait-scheduler-regression-joint-cases-r1-20261026 -JointDirectory D:/riz-build-cache-next/engine-wait-joint-cases-r2-20261026 -FailedNativeDirectories D:/riz-build-cache-next/engine-wait-fixture-cases-r1-20261026 -OutputDirectory D:/riz-build-cache-next/engine-wait-evidence-r4-20261026
~~~

collector 核各引用 SHA、逐进程/raw/计数，在读锁下复制原件并核对复制前后 SHA，再逐条核精确 ZIP 源 SHA；native 源须与当次 build manifest 完全一致，漂移就拒绝重造历史冻结。SchedulerJointDirectory 使用自己的 managed r1 ZIP，不以主 ManagedDirectory 的 r3 源替换。若不提供 JointDirectory，passed/fullBatchVerified/actualNativeWaitClrIntegration 仍为 false；不存在的结果和管理回归数量不能拼成联合通过。

## 下一步

按 [生产接线施工表](next-production-transport.md)实现扩展启动发布合同、九个固定原函数桩、23 个内部观察点及只读采用，再补全来源、消费者与游戏验收。本批通过不授予 EngineCancelled、全局 future closure 或最终资源根释放权限；游戏实测仍按用户安排稍后进行。
