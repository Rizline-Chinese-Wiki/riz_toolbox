# 本批范围与结论边界

2026-10-24。用户要求继续完善兼容底层，并已要求“先继续代码，实测稍后”。本批在该范围内完成代码、离线构建、隔离原生/CLR 测试及暂存归档；没有安装新包、启动游戏或执行游戏函数。[build-run.json](build-run.json) 记录唯一构建者 root 的定向和完整进程均以 0 结束。

## 执行了什么

- 新的独立原生 DLL 只发布自己的两个目标，配置 slot 19/18；真实 CALL、worker、异常捕获和 callee `_ReturnAddress()` 均由 native fixture 执行。C# 使用实际 native ABI，复制仍存活的机器帧，采用生产共用 bridge/broker dispatch core；没有伪造 Ready 或把构造的托管 frame 当作机器执行。
- [listener-managed-evidence.json](listener-managed-evidence.json) 的 12 个独立进程通过 1630 次检查。双 sink 共享同一物理父 token，但各自的身份不能授权另一个上下文；旧调用不继承晚附加 observer；逻辑 detach 后当前 child 仍完成清理，GC 后后续调用不再交付给已撤 sink。
- `normal`、`original-seh`、`original-cpp` 三个模式把真实机器回调接到实际 `NativeLocalUnityEventLedger`。Event/List/Invokable 来自 native work 的真实地址；graph、class/metadata、authority、根集合和预算来自受控后端。未调用游戏 GC handle 或 IL2CPP metadata API。
- 原生 callee SEH/C++ 在 native DLL 内捕获后由结果结构交给 CLR，未让原始异常穿过 P/Invoke。结果含 code、flags、参数、异常地址及 C++ 第四参数；当前测试**没有**与被动 first-chance 原始记录逐项比较 flags，`exceptionFlagsTransparencyVerified=false`。
- `target-read-av` 在自有虚表内布置真实不可读 target 槽，验证 helper 实际 AV、native 父 finally、没有 Before 的 abnormal After 和 token 0。该模式直接采用 bridge；`sharedProductionListenerCoreVerified=false`，没有假称它验证了 broker 的同一分支。
- `foreign-original` 只对一次受控的嵌套 original 绕过入口进行祖先遮蔽验证；Unknown 留存。没有由此证明所有 foreign parent、溢出、破坏或任意交错都已穷尽。

## 没有获得的证明

| 事项 | 本批实际边界 |
| --- | --- |
| 固定游戏冷启动与真实 listener 回调 | 九写固定映像/V2 证据继续通过，但映像隔离不执行游戏代码；本批新回调使用自有 native 目标 |
| 生产 root 两阶段采用 | 接线经静态审查；本批没有对两阶段中的每个失败点作 fault injection，不能称事务失败矩阵已验收 |
| 每个 delegate leaf 执行 | 仅观察 invokable 层实际 dispatch；没有证明内部跳过、multicast、virtual/generic/static/open 等每个 leaf 的实际执行 |
| 异常完全透明 | native code/payload、typed C++ 和清理有有限动态验证；完整 flags/ExceptionAddress 对照、所有 helper 合法 PC 的异常展开尚未由本批穷尽 |
| mutation、provider、取消和全 run 退休 | 本批不封闭全部写者或未来生产者，不签发完整消费者结束/释放凭据；ledger 保持 Pending |
| Retry、Return、Loader 全生命周期 | 没有实际游戏端到端验收；不能把数个退出计数归零当作旧资源已不可再用 |
| 可移植重建与全部 19 项完成 | 502 项只覆盖声明的仓库构建输入，不包含游戏、缓存依赖或工具链；本批不是完整 B19 发布矩阵 |

所有回调模式都明确 `firstInstallReceiptUsed=false`、`actualIl2CppMetadataResolved=false`、`gameCodeExecuted=false`、`gameRuntimeVerified=false`、`firstInstallSafetyVerified=false`、`delegateLeafExecutionVerified=false`、`allFutureListenersClosed=false`。这些 false 是证据范围，不应因总检查数上升而改写。

## 文件与操作边界

游戏固定输入为 Steam image `25977441`；[original-game-check.json](original-game-check.json) 中 `GameAssembly.dll` SHA256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，另两个游戏文件也保持匹配。已装旧批次 18 项和旧归档输入 326 项的核对见 [previous-installation-check.json](previous-installation-check.json)。新暂存包 22 项未覆盖安装目录。

本批构建使用现有 Windows x64 工具链、固定 Dobby 和已存在依赖；源/包冻结检查见 [source-archive-check.json](source-archive-check.json)、[package-archive-check.json](package-archive-check.json)。测试子进程由专用 runner 创建，有限等待并保留 stdout/stderr；未通过枚举终止或操作游戏进程来获得结果。当前文档收尾只读上述证据并新增说明，不修改构建输入，不重新运行冻结批次。
