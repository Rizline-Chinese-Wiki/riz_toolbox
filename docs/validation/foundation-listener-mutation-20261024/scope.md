# 本批范围与验证边界

2026-10-24。用户要求继续完善底层，游戏实测稍后。本批范围是生产代码接线、离线测试、隔离 native/CLR 联测和文档归档；未安装新包、未启动或操作游戏，也未调用游戏或 IL2CPP 函数。[构建终态](build-run.json)记录定向进程 `32299` 和完整进程 `60161` 均为 `exitCode=0`；[测试汇总](build-summary.json)为 25 套、1553/1553。易读进展见 [README.md](README.md)。

## 实际覆盖

生产 mutation 接线使用固定构建的 12 项允许表，分别核对类、方法、参数、返回值和代码地址后采用精确零/一/二引用参数的实例 void 通道。原始函数仍由 native 执行；broker 只保留观察上下文，不替换参数或返回值。来源、native sequence、receiver、参数、嵌套父上下文和前后图属于同一次物理观察；异常退出和正常返回分别记录。

mutation ledger 的 8 个测试直接调用生产账本，图、根集合、操作帧和 authority 来自受控后端。它们证明最终图相等也保留中间代次、上下层嵌套配对、foreign receiver 不被采用、部分捕获/读图失败后保责任、worker 只读已发布标量身份。它们不执行引擎增删操作，也不证明游戏中所有写者已被看到。

对象采用的 10 个测试调用实际生产 `CaptureDispatch` / `AdoptDispatchReference`，用已有 NativeRuntime 导出替身控制 Root、Target 和 Class 故障。初始化 metadata、已知绑定和对象关系受控；不伪造生产 Ready，不调用真实 IL2CPP GC。三引用先 Root 保存、后读取的顺序获得有限故障注入验证；这与 V2 receipt / bridge / Attach 的阶段划分是两件事。

协程身份的 6 个测试覆盖 Initialization、Cutout、Terminal tracker。Start 返回值在随后读取或保活失败前保存；ObservedHandle 与 HandleConfirmed 分开；可复用槽以原代次拒绝旧快照，Initialization 沿用一次性 owner/role，代次为 0。只读匹配拒绝 Unknown、退休、错误 owner/receiver 和重复句柄歧义，不调用 Stop 或授予释放凭据。

[原生 Operation fixture](operation-native-evidence.json) 使用自有函数和 helper 的实际三类入口，1156 次检查、72 次 original；[CLR 联测](operation-managed-evidence.json)使用同类自有目标，在 5 个独立进程中通过 259 次检查、18 次 original。CLR 联测确实采用生产 bridge/broker；原始 SEH/C++ 在原生 fixture 中捕获，不穿过 P/Invoke。异常 code/payload 和清理有有限验证，完整异常 flags 透明性未验证，JSON 保留 `exceptionFlagsTransparencyVerified=false`。这些检查单列，不计入 1553 项托管项目汇总。

## 尚未获得的证明

| 事项 | 本批边界 |
| --- | --- |
| 新 mutation 入口的 Dobby 首次发布与游戏冷启动 | Operation fixture 直接进入已配置 native 入口；`realDobbyPreparedCommit=false` / `dobbyPublicationVerified=false`，不能证明生产首次改写安全 |
| 真实 IL2CPP 对象与账本端到端 | CLR fixture 的 `actualIl2CppMetadataResolved=false`、`actualLedgerCaptureVerified=false`；账本和对象采用测试使用受控后端，尚未串到实际游戏回调 |
| 所有变化来源 | 12 项允许表只覆盖已确认非泛型入口；序列化同址 alias、generic/shared body、其他直接字段写和动态创建路径尚未全部纳入 |
| 每个 delegate leaf 执行 | 既有 listener 观察停在 invokable 调用点；快照及本批增删记录不能证明 multicast、virtual/generic 等每个 leaf 实际执行 |
| 取消机制 | 保存和匹配 Coroutine/Enumerator 身份已经实现；StopCoroutine/StopAllCoroutines 观察、取消请求配对、引擎确认停止与 Dispose 归属尚未完整接线 |
| 完整退休与整体 19 项 | 仍需 provider/loader、Retry、Return、取消和未来生产者封口；测试数不表示全部 19 项完成，不改 Pending/MutationClosed 的保守状态 |

所有新 fixture 均明确 `gameCodeExecuted=false`、`gameRuntimeVerified=false`、`firstInstallSafetyVerified=false`；CLR 结果另有 `installed=false`。旧固定映像九写/V2 验证与本批 Operation 入口是不同发布范围，前者通过不能替后者提供首写证明。

## 操作和归档边界

root 是唯一构建者。runner 仅管理其创建的自有测试进程，有限等待并保留 stdout/stderr；没有通过枚举或终止游戏进程获得结果。本文写作只读现有证据，不重建或修改源码。

[源码归档](source-archive-check.json)包含 522 项声明输入，与[构建前后清单](build-input-integrity-check.json)及[当前源码](source-integrity-check.json)一致；[暂存包归档](package-archive-check.json)包含 22 项文件，哈希逐项匹配且未安装。相比上一批新增 20、修改 14、删除 0，见[差异原件](changes-from-listener-clr.json)。这不包括外部依赖、工具链、干净环境重建和完整 B19 发布矩阵。

[旧安装检查](previous-installation-check.json)核对原有 18 项安装文件及旧归档 326 项源码输入均匹配；[原游戏检查](original-game-check.json)确认 `Rizline.exe`、`GameAssembly.dll`、`global-metadata.dat` 三文件哈希未变。固定 GameAssembly SHA256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。原始日志提取的 94 条不同 compiler diagnostic 文本包含上游、已有警告和 ANSI 差异，不能视为 94 个独立缺陷；通过构建不表示零警告。
