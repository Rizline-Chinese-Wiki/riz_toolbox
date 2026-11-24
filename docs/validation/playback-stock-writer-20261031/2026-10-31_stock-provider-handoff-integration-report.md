# 正式单局 provider 与原生跨局移交的源码整合

2026-10-31，`flavor=null`。S5 正式单 Session provider 和 S6 私有 joint handoff 已合入主目录：分别8个与23个文件，最终778个源文件逐字节核对。授权和剩余功能见[当前续接点](../../../.docs/CURRENT-CHECKPOINT.md)与[N2合同](../../../.docs/foundation-expansion/N2-STOCK-SOURCE-NEXT.md)。未安装、启动游戏或操作 UI/账号，生产准入及真实 Speed/Seek 保持关闭。

当前组合实现了真实 stock 来源到正式 Backend/Service/Module/PluginHost 的单局值控制，也实现了同一物理九成员组的跨局私有移交。两者组合已重新编译和测试；**同一正式 Module/Service/SDK 视图持续存活两局的采用机制属于下一增量，尚未在本项验收**。完整 source generation、未知 lower writer、生产首次准入、任意 Seek 和 B01–B19 总验收也未完成。

联合原件：`D:/riz-build-cache-next/playback-stock-handoff-main-integration-r2-20261031/validation-r1/test-result.json`，SHA `fe2707277fcfc15a9ae36c368199441d33fd224e1dd6a9ed12e8feaa4f3551d9`；证据索引 SHA `c07f17a8ece25c7291202dc8505d84c27897b45f7ad0428ad1a0657491faaba7`，3493个唯一文件回核。入口为[当前整合索引](stock-provider-handoff-validation-index.json)。

## 改动和实际验证

| Evidence | 证明范围 |
|---|---|
| E-S5 | [单局 provider 报告](2026-10-31_stock-provider-report.md)。concrete provider 只消费确切 Session/最终 Clock/同组 GetTime lease。物理故障只入队，由 owner-thread Module.Tick 撤回；Release 先验线程，worker 拒绝后仍可由 owner 重试。两个插件交接 exclusive 控制与修改审计实际执行 |
| E-S6 | [私有跨局报告](2026-10-31_stock-logical-handoff-report.md)。GetTime gate→Source Owner.Lock 联合核真实在途；sourceClosed 贯穿新七根/route 构造与旧根退出，显式 sourceOpen 后才可产生新 Pause baseline/dormant/唯一 Clock arm。新 native epoch/revision 递增，旧票、旧 callback、旧清理不撤合法 successor；physical fault 保持 sticky |
| E-MERGE | main integration r2 的 `before-source.zip`、`combined-source.zip`、`combined-s6.patch` 与实际 git apply 记录。778源；唯一三方重叠 Router 保留 S5 的 Open/Pump 故障队列处理及 Withdraw 线程 guard，其他 S6 增量按冻结字节；S5 Module/Bridge79e保留 |
| E-FULL | `foundation-stock-joint-778-{core,full,release,solution,sdk}-r1-20261031`，全部实际 exit0。core 756输入；27套2465/2465；六 DLL Release；solution 0 error/0 warning；两个独立 SDK 插件14/14。Release保留一个既有 generated nullable warning |
| E-JOINT | `playback-stock-joint-778-checks-r1-20261031/completion-r1.json`，SHA `4a8a0d334a9862cbf9817f5deb10287554b440648f61cb1b27165b3a6d82d5da`。新778源码实际编译/执行：provider7进程1021项、Clock10进程179项、handoff5进程197项，共22进程1397项。15组新PE/PDB、1429文档绑定；三组native PE/PDB、6COFF及编译输入重验 |

F-COMPOSE（`n/a_re/high/validated`）：E-MERGE/E-FULL/E-JOINT 证明当前778源码的单局正式消费和私有 joint handoff 共存。S6 原冻结的66局65次交接、11case388及独审2进程47项仍只签各自 stage；本次不重算为新源码检查数，也不将 stage 单独通过代替本组合的实际执行。

P-SINGLE（`path_type=callflow`）：actual stock Pause→七根/API/宿主 run→唯一 final Clock/native policy→concrete provider→GameplayNativeBackend→Service→正式 Module/独立 PluginHost scope→exclusive SDK SetSpeed→实际 pitch+Clock 确认→RunModificationJournal→stock Replay/Resume→native GetTime。

P-HANDOFF（同类型）：旧 exact publication conditional withdraw→实际 GetTime/source 两侧 drain→不可复用 native receipt/新 epoch/revision→sourceClosed 下独立新七根与 route→条件 sourceOpen→实际 Pause→新 dormant publication/唯一 Clock→joint arm→同物理 entry/original/group，Dobby Commit 不增加。这个私有路径本身不代表 Module 内 readonly 单局 provider 已自动换代。

## 复现与保留边界

collector 为 [Finalize-StockJointIntegration.py](Finalize-StockJointIntegration.py)，逐项核当前源码、各 runner 实际 exit、PE/PDB、native 输入与结果哈希；结果目录已存在时拒绝覆盖。S6 原补丁基于 S4+79e，与 S5 在 Router 重叠；首次三方输入的 LF/CRLF 差异导致冲突，r1原件保留。r2只规范临时 merge 输入换行，实际 merge-file、独立复制 apply、主树 apply 均为0；新组合全部重编，没有借旧包验新源码。

默认 RizlineAdapter 仍不发行真实生产 provider；fixture ClockMapping 明确 unavailable。source epoch 是受控私有逻辑移交凭据，不等于任意生产 source generation/未知下层 writer 已封口。下一采用稳定私有 host provider 的正式跨局组合，需要在同 Module/Service/SDK 视图上独立正向验收。
