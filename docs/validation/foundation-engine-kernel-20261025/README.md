# 独立协程运行核

2026-10-25。**轻量原生运行核已实现、纳入统一构建并通过实际验证。** `RizToolbox.EngineNodeKernel.dll` 复用 canonical EngineNodeContext 实现，39424字节，只有8项 ABI2 导出与2项系统导入，无 PE 入口、TLS、CRT 或 Dobby 直接依赖。它与旧 JudgeContext 各自保留模块身份和状态；生产启动链尚未采用它。

root 实际观察完整构建 session23528 退出0；25套1617/1617，599项声明源码与23项包已冻结归档。新增核的7个独立子进程与原39个早期加载进程分别计数；没有执行或安装游戏。完整 B01–B19 尚未验收完成。

| 原件 | 实际结果及边界 |
| --- | --- |
| [实际终态](build-run.json)、[wrapper](build.log.run.json)、[日志](build.log)、[摘要](milestone-summary.json) | 23528 exit0；25套1617项，SDK分发检查成功；保留94条去重编译诊断，不能说零warning |
| [128入口原件](native-build/engine-node-kernel-cases/entries/fixture-result.json)、[独立PE/表核对](native-build/engine-node-kernel-cases/kernel-entry-proof.json) | 1606个宿主检查、1486条父断言；128个独立RX入口、四张只读常量表和128项DIR64逐项核；256配置拒绝、12非法坐标；描述符宿主不调用typed入口 |
| [新核原生](native-build/engine-node-kernel-cases/native/fixture-result.json) | 3402检查、136原调用、124观察配对；完整EAX、嵌套、worker、原生/观察器异常及失效节点处理 |
| [新核托管](native-build/engine-node-kernel-cases/managed/fixture-result.json) | 五进程889检查，44原调用/28上下文；真实 bridge→dispatcher→ledger，标量provider和对象backend仍受控 |
| [新核汇总](engine-kernel-evidence.json)、[有限依赖自查](kernel-dependency-review.md)、[入口复核](review-entry.md)、[runner复核](review-runtime.md) | 新核7进程合计180原调用/152配对；Dobby只用于自有测试目标的发布，不是Kernel依赖 |
| [旧路径回归](build-summary.json)、[39早期宿主](early-loader-evidence.json) | 旧helper及现有原生/托管路径重新通过；39/7316属于原Dobby早期链，新核尚未接入73用例的固定发布链 |
| [源码ZIP核对](source-archive-check.json)、[包ZIP核对](package-archive-check.json)、[前后输入](build-input-integrity-check.json) | 599源/23包逐项hash匹配；旧576中4项有意修改在[变更清单](intentional-source-changes.json)，旧ZIP不改 |
| [现场与历史](preservation-check.json)、[旧安装](previous-installation-check.json)、[原游戏](original-game-check.json) | 18已装文件、326旧源ZIP仍匹配；EXE/GameAssembly/metadata和另核UnityPlayer均未变化 |

核心变化仅将四个入口表改为 typed constexpr，`GetEntry/IsEntry` 边界才转 `void*`。原实现已被本机优化器折叠为静态表，这次让编译期保证不再依赖该优化行为，没有编造旧版动态初始化崩溃。新 DLL 用 `/NOENTRY /NODEFAULTLIB` 独立链接；COFF 仍含 STL 的 `/DEFAULTLIB:libcpmt` 指令，最终PE实际没有CRT导入，两者必须区分。四类ABI、32槽、record V1和永久责任逻辑保持不变。

入口元数据首版把映射后头字段叫 `preferredBase`；真实ASLR样本证明它等于实际load base。正式版已改为 `mappedHeaderImageBase`，磁盘preferred值由父独立读取，128表地址按actual base和磁盘RVA分别核验。首版真实样本保留在[历史](history/observed-runs.json)，没有把命名修正当成入口功能失败。

测试期间31项输入全部由同一只读句柄核前后hash。随后 `Test-FirstInstallManaged.ps1` 为另一组旧helper测试更新了 Runtime.Tests 旁边的一份旧DLL副本，导致第一次离线汇总的“整个构建结束仍等于先前全部输入”假设失败；[原诊断](history/first-summary-failure.log)已保留。这发生在新核runner释放锁之后，[后续变化](kernel-post-run-files.json)明确记录当时01e517…与后来398d1c…。当时的精确DLL从既有冻结原件按hash恢复到[测试输入快照](kernel-supplied-files.json)，没有覆盖历史before/after或用后来文件替代。新Kernel及其余30项均保持匹配。

源码 ZIP SHA256：`a48d274857eb0b86a2c8530611282b374301e6ed73df17bdea537ccf8ef77532`。包 ZIP：`cbcb7f4b07f451717106435196f1f871040ae5ef45a9a5b97af1cc5a75067d6d`。这些归档不含完整工具链、Windows和.NET安装；不能据此宣称换机复现已完成。

下一步是让固定早期发布链独立绑定新核，验证整个可控加载组件不再导入庞大旧helper，然后实现实际游戏 bootstrap/系统WinHTTP转发和独立V4采用。具体顺序见[下一步](next-integration.md)；本批[正式报告](../../2026-10-25_底层-独立协程运行核验证-report.md)详列证据。生产coordinator/IL2CPP对象GC、scheduler/async/Start内联清理、全消费者退休和完整Retry/Return/Loader仍待补齐。EngineCancelled=false、Pending保留、RequestPlay关闭。

## 安装补充已另批完成

核心23528之后发现安装白名单仍是22项，真实临时安装会拒绝新23包。该问题已在[独立补充批](../foundation-kernel-install-20261025/README.md)修复并通过16项安全/13项恢复检查。后续只改3个安装/测试脚本，核心599归档保留构建时身份；当前源码以补充批的[组合599清单](../foundation-kernel-install-20261025/source-final-manifest.json)和[组合ZIP证明](../foundation-kernel-install-20261025/source-archive-check.json)为准，其余596项相同。不能把核心ZIP误称为包含后来的安装修复，也不需要将不受影响的1617项再算一次。
