# Tween 退出与引用回收：实现及验证记录

本批把已观察到的工作结束、原生记录注销和实际引用释放接成同一个私有事务。完整底层 B01–B19 尚未完成；本文件不将受控测试解释为真实游戏验证。

## 当前实现

1. 完成借用检查完整 owned occurrence、正常或跳过的实际调用结果、队列和 transient alias、Tween 自身与具体 target 的死亡凭据、child/typed effects。
2. 停止本 run 的五个 Tween 和一个 Single 观察订阅，关闭 source/root set 及 hub 的新借用，保存同一代 journal 水位。native Busy 保留同一私有事务，下一帧重新借用完成区间。
3. common native rundown 覆盖六种实际入口的 observer snapshot 前、source operation/pending/relay、reference scope/notify。Cut 是同步同线程凭据，保留 foreign original 的执行。
4. 在 Cut 内先核并注销完整 admission 批次，然后分别结束 source/reference 借用、释放 admission 自有根、释放 backend 自有根。共享根只撤借用，由实际 holder 唯一释放。
5. 每个实际 Free 前登记尝试；错误保持 Unknown 并保留未确认责任，不能再次调用同一个 Free。native commit 后所有旧根读取入口先依据标量拒绝，避免释放回调重入读取 retired lease 或旧 root。
6. 最终完成票在 Cut 最后校验和原生 Release 正常返回后才发行。`CanRetireWork` 只能消费该私有票，seal、快照或外部 Boolean 均不能替代。

## 已有证据与当前限制

当前生产 Adapter 入口实际 Release 编译：`D:/riz-build-cache-next/p4-e-adapter-r1-20261029`，0 警告、0 错误。该编译早于最后的跨 holder 修复，后续须以 fresh source snapshot 的完整回归为准。

Source 生命周期分项：`D:/riz-build-cache-next/p4-c-source-lifecycle-20261029/tests-focused.log`，51/51；包括真实 RootHandle Free、共享双 reader、晚 callback、地址复用、Free 重入和异常。Admission 分项：`D:/riz-build-cache-next/p4-admission-managed-r3-20261029/hooks-tests.log`，Hooks 209/209。完成事实分项：`D:/riz-build-cache-next/tween-retirement-d-audio-r8-cases.log`，106/106。这些结果各自有范围，不能代替整树或真实游戏验收。

typed Single 的实际 mid/executing-list producer、非空 listener effects 的完成或转移、具体 engine setter effects 仍未齐备，相关工作维持 Pending。真实游戏未执行或安装本批。

## 当前冻结与实际执行

当前源码快照为 `D:/riz-build-cache-next/foundation-tween-retirement-core-r1-20261029/test-result.json`，实际通过 Audio 989/989、协调器 28/28、对象身份 10/10。它包含 A/B/C/D/E 与 child 独立引用的最终接线；后续 SDK 结构化错误施工不属于该快照。

| 证据 | 实际范围 | 原件目录，位于 `D:/riz-build-cache-next/` |
|---|---|---|
| 原生 helper | 完整 `/W4 /WX` 构建、77 个实际 PE 导出 | `tween-retirement-helper-a-r3-20261029` |
| 原生共同回收 | 18 个独立进程、667 检查；六入口 snapshot 前竞态、故障、完整批量注销、foreign original 在途时继续执行 | `tween-retirement-native-a-r5-20261029` |
| 来源与引用联动 | 13 个独立进程、1,625,434 检查；保持寄存器的真实 source relay 与 5 类联合引用场景 | `tween-retirement-source-a-r4-20261029` |
| 独立引用协议 | 9 个独立进程、21,991 检查 | `tween-retirement-references-a-r2-20261029` |
| 实际准入/原函数分发 | 同 helper 4 个进程、359 检查 | `tween-retirement-admission-a-r1-20261029` |
| 固定 PE 与完整 Single 函数 | 26 个主函数与 3 个 CHAININFO continuation，共 647 字节的 Single 函数 | `foundation-tween-retirement-profile-r1-20261029` |
| 映像发布及篡改拒绝 | 41 个独立映像进程；游戏映像仅以不解析入口方式映射 | `foundation-tween-retirement-mapped-r1-20261029` |
| Bootstrap ABI | 2088 检查、120 次原调用；四 ABI、float 原始位 | `foundation-tween-retirement-bootstrap-r1-20261029` |
| 实际 Dobby ABI | 1546 检查、92 次原调用 | `foundation-tween-retirement-opdobby-r1-20261029` |
| CLR 采用 | 同 helper 两个独立映像进程、525 检查 | `foundation-tween-retirement-adoption-r1-20261029` |
| 托管发布构建 | 六 DLL、0 错误、1 条既有 generated nullable 警告 | `foundation-tween-retirement-release-r1-20261029` |

原生 helper SHA256 为 `cd34da8cf215ed2e6a22d2330291c85449c2a34456697ee05c0f129dc28abb8e`。共同回收 aggregate 的 source counters 明确为受控 stub，真实 source relay 和 reference callback 分别由另外两套进程验证；不将这些证据解释为真实游戏执行或 IL2CPP GC 验收。

候选 `artifacts/foundation-tween-retirement-candidate-r2-20261029` 已通过实际打包及独立 PE/源码归档校验，24 载荷 / 31 证据，manifest SHA256 `3b2a3b87e537b534d51e20a6f902e70689f0ac828f4f083bb3f6149c82ee0df2`。新增必需的共同回收与独立引用证明逐一与该 helper 的冻结源码比对。r1 首次打包因 verifier 仍使用旧 admission 输入清单而拒绝；已补入新 rundown 两个依赖并在 r2 完整重跑，保留 r1 历史目录。

完整 24 套托管矩阵已实际通过 **2128/2128**（Runtime 317、Hooks 209、Audio 989），原件 `D:/riz-build-cache-next/foundation-tween-retirement-full-r1-20261029/test-result.json`。六组件发布构建和同快照 CLR 采用也实际通过。

合成安装/恢复矩阵已实际通过 **21/21**，原件 `D:/riz-build-cache-next/foundation-tween-retirement-install-r1-20261029/test-result.json`。新增两类安装负例确认缺少回收证明，以及重新绑定外层哈希后篡改共同回收源码均明确拒绝。失败场景中出现的 Python 拒绝 traceback 是预期负例，不是测试失败。操作限定在临时合成目录，真实游戏仅作为只读文件来源；没有真实安装或游戏执行。

上面通过的是该冻结批次。后续 live 工作树开始 SDK 结构化错误迁移，2128 与当前候选不验证后续新改动。

## 上一批已冻结候选

`artifacts/foundation-tween-dynamic-single-candidate-r1-20261028/transport-candidate-package-manifest.json` SHA256 为 `80621f1690547b4fe4918c5750189b0be4885871ae9f1009158debc916f27b31`。该批的 2063/2063 托管检查、19/19 合成安装和完整 native/CLR 证据见相邻 `foundation-tween-dynamic-single-20261028/README.md`；该包尚未含本批退休实现。
