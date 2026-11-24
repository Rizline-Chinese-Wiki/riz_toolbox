# 同源 Canvas basis 独立源码复核 r3

在本记录声明的源码范围内，未发现必须阻断的剩余问题，`sourceReviewCompleteForScope=true`、`blockingFindings=[]`。这只签源码审查范围，不签执行结果、默认 316 单进程运行或完整 Seek/Speed 准入。本代理未实现或修改源码、测试、控制器，未构建、跑测试或启动游戏/UI，也未操作账号、代理、旧 ZIP 或 Git；只新增本 README 与 [r3 JSON](independent-source-review-r3.json)。既有 r1/r2 文件均按原字节保留。

当前 856 个输入逐项重新计算后，全部匹配 `D:/riz-build-cache-next/canvas-basis-20261112/root-r3/final-r1/inputs-before.json`；recovery-r1 的输入清单也与它逐项相同。清单 SHA-256 为 `3c8108b09bb41b210e12581cdec22a594f55b0c374405132d077468827813ad8`。相对 r2/final-r2，路径集不变、855 项哈希不变，仅 `NativeSeekCanvasBasisChecks.Guards.cs` 从 `1e98d639e62d5c8a0db75895b432c1790b5e0075a0320430bccb13e4b2881910` 改为 `857e52540ab8b697f60af14e35f8ff3123c8af75c2f990a2c99fa6cbdabf1f9c`；生产源码没有变化。r2 的 before/after 清单也已核为相同。

r2 源码审查漏过了 Case12 的 oracle 错误，本轮明确承认此漏检。原断言在 basis 准备后采集所有现存根，混入申请 Run 原有、允许随 Run 释放的 handle；按目标地址排除单个 controller 不能区分所有权。新断言在 Prepare 前取申请者 handle 集，之后只取新增 handle，要求集合非空并用 `OriginalRootsRemain` 核对相同 handle 仍映射原目标且从未进入 free log；原 Transform 覆盖检查、Run.Dispose 后的三个 VerifyPrepared、执行及恢复断言均保留。已把当前文件与实际旧快照 `root-r2/final-r2/source-before/NativeSeekCanvasBasisChecks.Guards.cs` 做全文差分，只有 `IndependentLifetimeAndSerial` 首段发生上述变化，其他方法和断言不变。这是 oracle 的来源归属修正，不能抹去原 Case12 的 0/1、自然退出码 1。

语义范围仍为 58 个条目，其中 57 个属于构建输入，另一个为 [实现合同](../canvas-basis-handoff-20261112/implementation-contract.md)，角色单独标为 contract/document，其 SHA-256 为 `00bfad446de64ea819141fbd2551894135421b02eb846cf9b52baab15cd78be4`。本代理重读 18 个文件/文档：12 个全文/全文档，6 个只重读 JSON 指明的 section；其余语义结论仅在哈希与 r2 一致的范围内继承。r3 JSON 每条都区分本轮重读与继承，未把 856 个依赖冒称为逐行独立重审。`dependencyHashManifest` 列完整 856 项，与 `reviewedFiles` 中的 57 个输入有意重叠；其中 799 项仅做字节核验。

本轮亲自复读的重点包括 aggregate 三个分片、SharedBasisSource、CanvasSource、两个 access、capture lifecycle、两个 child 的预留/scope/revision/Dispose section、两个 VerifyExpectedValues、Guard 全文及其根证据 helper，并重核 DependencyOrder 的 Owner+Index+Kind 记录器。未变的数值计算、writer 原算法、typed conflict 的完整/部分样本保留和其它回归源码，依其 r2 哈希及明确的原审查范围继承，不声称在本轮全部重新逐行审阅。

| 证据 | 本轮结论 | 调用或证据路径 |
|---|---|---|
| E-inputs、E-delta：两代冻结图、当前 856 个实际哈希及旧 Guard 快照 | 唯一变化是 Case12 oracle 首段；旧图不能直接代签新测试图 | 旧快照 → 单块差分 → 当前 Guard → r3 完整输入表 |
| E-source、E-contract：JSON 内实际源码/合同哈希及重读范围 | 一次 gate/serial、先预留三个原 owner、同一 capture/Plan、managed scope 和各 child lease 仍符合合同；未发现必要源码 blocker | RunValue → reserve aggregate/cache/position → SharedBasisSource → 原两个 child |
| E-source：aggregate Execute/Lifecycle、child scope/Dispose/revision | cache 完整确认后才进入 position；恢复先位置后缓存；两轮联合样本及 revision 撤旧证明；已进入 position effects 继续保责 | Commit/Confirm → ExecutePositions → 两轮读回；显式恢复逆序执行 |
| E-products、E-fixtures：12 个生产文件、测试副本及 3 个 native fixture 的亲算 SHA-256 | 保留的旧生产产物、记录的 previous/current、当前产物、recovery 副本全部同字节；fixture 身份相同 | original-compiled/bin → 当前 artifacts/bin → resumed-compiled/bin |
| E-original、E-current-prefix：保留日志、原 result 及其哈希 | 01–11 与 12–13 来自不同测试程序集/输入图；原失败保留 | r2/final-r2 → r3/final-r1 的 oracle continuation |
| E-interruption：原 unfinished report、仅含 RUN 的 Case14 日志、interrupted-observation/recovery | 原 Case14 没有可证明的 PASS 或退出码；恢复必须作为新子进程证据，不能补造旧 child exit | 保留原记录 → 中断后 513 文件指纹 → 无重建恢复计划 |

下列是对已有协调器记录的只读核验，均非本代理执行。每个已完成条目的日志字节与其 result 中的 `logSha256` 相同，完成条目记录无 timeout/resourceStop；r3 JSON 保留逐 case 的源图、程序集和日志归属。

| 记录 | 冻结图与测试程序集 | 原记录的结果 |
|---|---|---|
| r2/final-r2 Cases01–11 | r2 manifest `da773379…`；旧测试 DLL `0f1dd2510a32733c126c059f2dad1758793fc00a107ab947e1576727b9ad154f`，PDB `245568f084d28d03fd29e52be4d9fdb035e5e09b398876391122ebd19c25316f`，副本位于 r3/final-r1/original-compiled/bin | 各 1/1、自然退出码 0；后续只保留原结果，不声称由新 DLL 重跑 |
| r2/final-r2 Case12 | 同上；旧 Guard 快照及原失败日志保留 | 0/1、自然退出码 1，失败在原根断言 |
| r3/final-r1 Cases12–13 | 当前 manifest `3c8108b0…`；当前测试 DLL `e5106eaa27c5c3ac45524531dac641844d8b42399cec9a844cbe1c438a9aef43`，PDB `d4f6f4037f73d6d907d8e3102d72f94577c3d9c240d24081af987c99442519f8` | 各 1/1、自然退出码 0 |
| r3/final-r1 原 Case14 | 当前图；原 report 仍无 finishedUtc/终结 step，日志 SHA-256 `02cde571253b57fe72d6fc2fbcdd6ac0998f50c9b27eae0d9ed28a53e20065bd` | 只有 RUN；退出未知，不计通过 |
| r3/recovery-r1 | 同一 856 图和当前编译产物；新恢复控制器 | 继续 14–16 及原 300/51/1001/6 的计划；运行结果由主控另行记录，本源码审不代签 |

12 个生产 DLL/PDB 的每个实际路径及完整 SHA-256 列于 JSON 的 `artifactByteAudit.productionProducts`。三个测试目录的 Sdk/Core/Runtime/Adapter DLL/PDB，在旧保留目录和当前目录共 48 个实际依赖副本均核为其对应生产文件的同字节副本；Runtime.Tests 与 AudioContent.Tests 的旧/新 DLL/PDB 相同，Judgements.Tests 因 oracle 修正而不同。不能把上述组合覆盖写成同一个当前测试程序集已完整跑过 16/316，也不能把旧缓存批次已接受的 300/51/6/1001 认证为本图的新通过。

本代理还核对了 recovery 的输入、复制的 bytegate 表、原测试程序集快照，以及 `interrupted-observation.json` 中列出的 513 个编译文件：当前与 resumed-compiled 副本的相对路径集及哈希全相同。该 observation 的 SHA-256 为 `56101e7d75c2c4feac2ec64c06dfb24aa662e2977ac31a03b0f2175e8ec4a149`，标明指纹取于中断后；它不能倒推原 Case14 的终结状态或原进程启动时的全部二进制身份。旧测试程序集指纹记录于原运行后、重建前，原报告仅记录 dotnet 路径及 fixture 环境，没有历史 dotnet host/runtime 二进制哈希；本报告不补造该证据。恢复控制器源码的 resume 分支会跳过构建，只复用通过的前缀并重启未完成 Case14；本代理没有运行该控制器。

成功范围仍仅限所选 cache/localPosition 的有序完整可观察值；有序样本不是原子 CAS，不能排除未观察 writer/ABA。typed 冲突审查只涵盖已有完整/部分真实样本和明确条件拒绝边界，普通 getter IO/metadata 失败本身不是第三态，也不承诺任意异常的通用部分采样日志。position setter 一旦进入，原 EffectsOwner 及完整来源根保留，selected-value 恢复不关闭 Unity 引擎 effects。所有 CompleteSeekPermitted 保持 false；Unity internal-call、consumer/callback closure、line/ring、缺失 runtime、Hold 引擎退出及完整时钟事务未由本审查补齐。
