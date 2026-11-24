# 操作入口的预初始化发布与只读采用

2026-10-24。**本批把 12 个监听变更入口和 6 个协程 Stop 入口纳入原生预初始化发布；CLR 启动后只核对真实发布凭据并附加观察者。完整离线构建通过 25 套、1579/1579 项检查，546 项源码输入和 22 项暂存包文件已冻结核对。** 新包尚未安装或进行游戏实测；整体 B01–B19 仍未完成。范围见 [scope.md](scope.md)，后续实施见 [next-integration.md](next-integration.md)，详证见[正式报告](../../2026-10-24_底层-操作入口预初始化-report.md)。

## 这次解决的具体问题

此前这 18 个入口是在 CLR 解析到方法后才首次调用 Dobby 安装，游戏可能已经有线程执行这些函数。本批将其移动到受支持的 Doorstop 首次 `il2cpp_init` 之前：先验证固定游戏映像，准备全部原函数通道，再完成原有 9 处及新增 18 处，共 **27 次真实写尝试**。发布后的 V3 凭据保存 18 个入口各自的 target、original、entry、epoch 和完整身份；旧 V1/V2 布局保留，但不能授权新入口。

CLR 的默认生产绑定必须取得实际 V3 Ready，再匹配真实 metadata 与对应行，只执行一次 Attach。缺少凭据就拒绝绑定，不退回运行中首次安装。原生入口在每次调用开始时固定一份观察者快照：已经开始、当时没有观察者的旧调用，即使中途 Attach，也不会把自己的后半段交给新观察者。逻辑解绑保留原生通道和在途清理责任。

这解决了生产代码中晚安装的路径；实际游戏启动是否满足首次初始化时序、18 个声明能否通过活体 metadata 核验，仍须单独验收。原生写入后失败保留 Unknown 和已产生责任，不尝试假回滚或重新安装；任意第 N 写失败的动态注入尚未完成。精确合同见 [原生方案](native-plan.md)、[CLR 采用方案](runtime-plan.md)及[有限审查](review.md)。

## 已通过的分层证据

以下引用本批完整构建留下的原件。它们分别验证不同层，不能相加当作全覆盖率，也不能合并成尚未执行的游戏端到端运行。

| 验证层 | 实际结果 | 证明范围 |
| --- | --- | --- |
| [构建终态](build-run.json)、[测试汇总](build-summary.json) | root 实际观察 session `94416` terminal exit 0；25 套 `1579/1579`，Hooks `189/189`、AudioContent `619/619`、Runtime `160/160` | 包括 V3 形状/身份拒绝、实际槽责任及无 producer 时默认生产绑定拒绝晚安装；检查数不是完工率 |
| [原生 bootstrap 通道](operation-bootstrap-native-evidence.json) | 1477 次检查，84 次实际 original，72 对观察者 Enter/Exit；最终在途为 0 | 三种自有目标实际 Dobby Prepare/Configure/Commit、3 次旧调用跨 Attach 无新回调、15 次 worker 调用，以及有限嵌套、原 SEH/C++、观察者故障和永久槽拒绝 |
| [原生通道 → CLR](operation-bootstrap-managed-evidence.json) | 5 个独立模式，共 837 次检查、75 次实际 original，其中 45 次发生在 Attach 前 | 自有真实回调进入生产 bridge 和共用 broker dispatch core；双 sink、解绑/GC、原异常及观察者故障。metadata 受控，未使用 FirstInstall receipt，未串实际 ledger；此层不验证旧调用跨 Attach |
| [固定游戏映像发布](mapped-publication-evidence.json) | 6 个独立进程，共 5074 次检查：normal 839，5 种篡改各 847；核对 18 行和 27 写 | `DONT_RESOLVE_DLL_REFERENCES` 隔离映射固定 GameAssembly，验证真实 producer 的字节、通道身份和篡改拒绝；Ready 为重入前快照，重复进入后转 Unknown，写计数保持 27，无第 28 写 |
| [CLR 只读采用](managed-adoption-evidence.json) | 2 个独立进程，共 384 次检查；happy 模式实际采用并 Attach 全部 18 个通道 | 生产 RequireReady 读取实际 V3，双 sink 共用入口；前后指令不变、engine install 调用为 0。metadata 受控，映像函数和观察者回调均未执行 |
| [Unknown 竞争](atomic-unknown-evidence.json) | 888 次检查；V1/V2/V3 各 100 次确定性读取均见 Unknown | 暂停 detail writer 后读取原子 Unknown，保留真实 27 写与已有责任；不是任意部分写入失败注入 |

CLR 五模式为 `normal`、`original-seh`、`original-cpp`、`detach`、`observer-fault`。原生 fixture 的跨 Attach 阻塞旧调用和 CLR fixture 的前置无观察者调用是不同测试；没有把两者拼成 CLR 已验证同一旧调用跨 Attach。完整异常 flags/context 透明性仍未证。

映像层没有执行 GameAssembly 的 DllMain、IL2CPP 初始化、任何游戏函数或 patched target；其回调计数为 0。自有目标层确实执行原生函数和回调，但不是实际引擎 Stop。这些实证尚不证明游戏并发首写、真实冷启动或 IL2CPP 根/GC。

## 交付状态

[变化清单](changes-from-cancellation-request.json)相对上批为新增 12、修改 21、删除 0，声明源码从 534 增至 546。[构建前后检查](build-input-integrity-check.json)、[当前源码检查](source-integrity-check.json)和[源码归档检查](source-archive-check.json)确认 546 项一致；[包归档检查](package-archive-check.json)确认 22 项逐项匹配。源码 ZIP SHA256 为 `6a3844055b5a2ce2d35f74cbc8491540480d946fdd9da38209c1ded1000cc9cb`，暂存包 ZIP 为 `624cd1825b5a1bfb282d7887a1c261ff10a00476f53f9748c9fbd8d5d42dbddb`。

[暂存检查](staging-check.json)明确新包未安装；[旧安装检查](previous-installation-check.json)确认原 1021 批的 18 项安装和其归档 326 项输入仍匹配；[原游戏检查](original-game-check.json)确认 3 个原游戏文件未变。[完整日志](build.log)保留 111 条不同原始 compiler diagnostic 文本，含 upstream、既有警告和 ANSI 差异，不等于 111 个独立问题，也不能称零警告。归档未包含工具链、外部依赖或干净环境全矩阵重建证明；复现入口为 [run-build.ps1](run-build.ps1)。本批说明文档不属于 546 项冻结构建输入。

## 取消完成仍是下一条链

[固定 UnityPlayer 静态研究](cancellation-research.md)已找到真实 Start 的节点身份、Stop 汇合的撤销路径、执行窗口和最后引用清理。它明确说明：节点可先摘链，正在执行的协程随后才退出；单凭 Stop 返回、handle 释放或 Dispose，仍不能确认引擎取消完成。

下一步将把实际节点登记、执行和最后清理组合成逐代次证据，再联合 listener、provider 和消费者资源退休验证完整 Retry/Return/Loader。当前 `EngineCancelled=false`，共享 Pending 保留，`RequestPlay` 关闭；仅 B03 整项与 B18a 子项保持勾选，本批没有新增验收勾选，用户“先继续代码，实测稍后”的安排保持。
