# M 批：scheduler 行责任与真实回调

2026-10-26，本批验证已完成，完整底层仍在建设。完整目标仍为 [B01–B19](../../../.docs/foundation-expansion/SPEC.md)，本批没有授予生产启动、取消完成或未来任务来源关闭权限。按用户安排不操作真实游戏。

已实现六个 scheduler 原生入口，以及 Drain 内部逐行 Begin/End；保留四个原节点入口和 Start inline 的独立 ABI。C# 行账本分别记录登记、消费后等待 callback、callback、cleanup、Drain 与退休，节点身份来自同一套实际 identity/node 账本。消费移出队列不清除回调责任，重复登记保留两个登记代次，引用数量不因复制行而增加。End 和退休使用缓存身份，不重新读取已释放 entry/node。

运行协调器一次接入六通道，将原生及托管在途计入活动状态。节点 cleanup 必须依次经过实际 Identity/Node Complete、cleanup Complete、Identity/Node Dispose、cleanup Dispose，才允许该行准备退休。对应节点代次的待完成行阻止观察根退休；正常 Pending 仍可重试，不当作永久 Unknown，也不阻塞其他已完成节点代次。生产接线仍缺新的原生发布合同，旧生产候选不能被视为含本批代码。

## 已执行的验证

| 证据 | 实际结果 | 范围 |
|---|---|---|
| `engine-scheduler-contract-r1-20261026/fixed-sites.json` | 固定 UnityPlayer SHA、六个完整函数和两个 Drain 重放点匹配 | 只读磁盘分析，不执行游戏 |
| `engine-scheduler-kernel-r2-20261026/scheduler-pe-verification-r1.json` | 33110 项磁盘检查通过；原生严格构建退出 0 | 25 个导出、7 个系统导入、无 CRT/入口/TLS、展开信息及探针保存恢复 |
| `engine-scheduler-cases-r1-20261026/fixture-result.json` | 16/16 自有原生进程通过 | 完整返回寄存器、逐行清理、实际 E9/relay/CALL 重放与运行时展开、异常及重入 |
| `engine-scheduler-managed-checks-r4-20261026/test-result.json` | Audio/content 742/742、Runtime 23/23、对象身份 10/10 | 受控传输运行实际协调器和账本；465 项输入前后不变 |
| `engine-scheduler-managed-cases-r3-20261026/fixture-result.json` | 真实 Native→CLR 最终 7/7；654 条托管、548 条原生断言 | normal、high-eax、repeat、multirow、remove、drain、address-reuse 七个实际进程；节点 identity provider 受控，未执行真实 tracker backend |

所有上述目录位于 `C:/Users/71957/riz-build-cache/`。固定游戏 SHA 为 `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`；r2 Kernel SHA 为 `5c59a29430a4ca1a183b155ed1b6e17358d0831794ac3c7578d15c2f95fe126f`。管理 r4 的 465 项输入在编译前即归档为 `managed-project-sources.zip`，SHA `648c188c6f7ae3f6f8ed96e0b9feece7d89f02f605759a5666a5d8a566a3b9b9`。构建保留 17 条已有测试夹具警告；Runtime 构建零警告、零错误。

冻结索引：[evidence-index.json](C:/Users/71957/riz-build-cache/engine-scheduler-evidence-r1-20261026/evidence-index.json)，SHA `507c2c5758953dcff1f7bd0a8323daf168ff2e686722b6ba45637a8c8e3d30e4`。它复核 8 份完成回执及其中实际路径/哈希，归档 22 项原生与检查来源、210 项实际二进制/对象文件/原件/日志，以及上述 465 项管理输入。[阶段报告](../../2026-10-26_底层-scheduler逐行责任与真实回调-report.md)给出 Evidence → Finding → Path 与重跑入口。

## 已修问题与失败记录

原生复核修复了两个实际问题：foreign callback 的行不会进入已知协程 callback，不能给它留下永远等待该入口的 pending；观察者重入不得越过中间非 original scope 借外层 Drain row。额外两个原生实际场景已覆盖这两处。

Start 退订责任补上 Complete/Dispose 回调内重入：本消费者的实际 closing context 尚在时继续接子调用；最近内层 context 已处置时，不能借另一个仍打开的外层 context。两个新增 Runtime 场景实际通过。

首次 PE verifier 自身遗漏继承结果字段及 linker 状态赋值，已修校验器，未以此修改或放宽 native ABI。原生独立夹具 r1 因 ASM 类型声明错误构建失败，r2 修正后通过。管理 r1 的新增测试将 Identity.Call 误传为 Node.Call，导致 5 个编译错误；修正为该实际 call.NodeCall 后，r2 完整回归通过。失败输出和后续结果分别保留。

联合 address-reuse 首轮子进程退出 `0xC0000602`。按控制流定位为夹具在 MEM_RELEASE 后跨越 CLR 调用，再强制分配回同一系统地址；当次没有保存 LastError，不能伪称捕获了系统失败原因。最终使用明确的自有 reservation 内 decommit/recommit 复用，单独记录页面状态和操作次数，只证明同一保留地址内实际新登记代次，不冒充系统地址释放后重新取得。

联合第二轮 normal 的 CLR gap 查询又暴露了同类假设：旧分配成功 MEM_RELEASE 后，裸地址可被 CLR 立即重新使用。最终其他六模式保留实际 MEM_RELEASE 返回成功、逐行 StorageRetired 和 release 次数检查；gap/Drain End 当前页状态只作查询诊断，不作为旧分配释放与否的证明。独立 native 16 场景的 MEM_FREE/不可读和无回读检查未修改。私有联合 fixture ABI2 的 Descriptor216/Receipt600 与公共 Scheduler ABI1 分开；原生 r2 因新增比较的 signedness 在 `/WX` 下失败，修正类型后 r4 严格构建退出 0。失败 r1/r2 与最终 r3 执行目录分别保留，没有靠重试跳过未修缺陷。

## 接下来

按 [wait 固定合同与分工](../foundation-engine-wait-20261026/contract-notes.md)补普通 wait 派发、实际发布/清空，再完成取消、所有消费者和未来来源关闭，最后更新候选并按用户安排恢复游戏整局与返回菜单验收。新的 wait 静态检查已核 9 个完整函数、1580 条指令、60 个位置；尚无该族 native/CLR 动态产物。
