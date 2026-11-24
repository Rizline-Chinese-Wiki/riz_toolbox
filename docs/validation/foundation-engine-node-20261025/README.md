# 引擎协程节点的原生观察与代次账本

2026-10-25。**四类引擎节点调用的专用传输、CLR 分发及节点代次账本已完成本批离线验证；完整构建通过 25 套、1597/1597 项检查，561 项源码和 22 项暂存包文件已冻结核对。真实自有目标已连通 Dobby→native→CLR bridge/dispatcher→实际 Adapter 账本。** 新包未安装、未进行游戏实测；UnityPlayer 的生产安全发布和完整取消/退休链仍未接通。完整目标仍为 B01–B19，仅 B03 整项和 B18a 子项保持原有勾选。[范围](scope.md)与[下一步](next-integration.md)说明本批边界。

## 这次补上的底层能力

此前的六类 Stop 观察只能说明“请求过停止”，不能说明引擎执行窗口已经退出。根据上批[固定 UnityPlayer 静态研究](../foundation-operation-preinit-20261024/cancellation-research.md)，本批新增专用的 [EngineNodeContext](../../../native/RizToolbox.JudgeContext/EngineNodeContext.h)，分别保留 Start、Execute、Release、Detach 的实际参数和返回形式；它们不带 IL2CPP 的隐藏 MethodInfo。前三类保留原始 AL 字节，Detach 保持 void，不把返回值归一化成 bool。

这让底层可以区分一条协程的开始、实际执行、摘链和最后清理，并识别同一内存地址上的新旧协程。是否还会被引擎再次调度，仍要由后续的调度来源和消费者责任共同确认。

| 层次 | 当前实现 | 作用与边界 |
| --- | --- | --- |
| 原生传输 | 4 类、每类 32 个永久槽；112 字节 Before 和 32 字节 Completion | Execute/Release/Detach 在原函数前复制 node 的 owner、script handle/type/cache、引用计数。Start 正常返回后只读实际输出槽的指针值；全部入口均不在原函数后解引用 node |
| CLR 采用与分发 | [只读 bridge](../../../src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.cs) 与 [dispatcher](../../../src/RizToolbox.Runtime/Hooks/NativeEngineNodeObserverDispatcher.cs) | 核验实际已配置的 kind/slot/target/original/entry/epoch，只 Attach 一次；同一物理调用固定观察者和清理责任，逻辑解绑后仍配对 Complete/Dispose |
| 节点账本 | [NativeEngineCoroutineNodeLedger](../../../src/RizToolbox.Adapter/AudioContent/NativeEngineCoroutineNodeLedger.cs) | 以实际 Start 的 owner 与 type=2 的 IEnumerator cache 匹配同步 Execute，记录本地节点代次、执行深度、摘链及最后清理事实；没有 CLR 裸指针读取或根释放接口 |

原函数异常继续在 native 中展开；观察者错误、读取故障或不一致身份会保留 Unknown。节点地址只有在旧代次已观察最后清理、物理执行和调用责任都排空后，才能被新 Start 认作下一代。node 的 script handle 不是 IEnumerator 指针，引用计数也不是执行深度。

## 正式构建与分层证据

root 是唯一 builder，已实际观察完整 session `11589` terminal exit 0，包含 SDK distribution 和 Package。[构建终态](build-run.json)、[测试汇总](build-summary.json)与[完整日志](build.log)一致。检查数表示有限断言数量，不表示覆盖比例；以下两类真实调用均执行自有 fixture 的目标，没有加载或执行游戏模块。

| 验证层 | 实际结果 | 有限证明范围 |
| --- | --- | --- |
| 完整离线回归 | 25 套 `1597/1597`，Runtime `170/170`、AudioContent `627/627`、Hooks `189/189` | 新增 10 组 Runtime 与 8 组节点账本检查，覆盖同步 Start、执行责任、地址代次、错误身份、内联清理不确定性、异常、配对和容量边界 |
| [自有真实 Dobby 四入口](engine-node-native-evidence.json) | `3191` 次检查，`128` 次实际原函数，`116` 对 Enter/Exit，`12` 次无观察者原调用 | 实际 Prepare/Configure/Commit，4 次旧调用阻塞跨 Attach、16 次原函数异常、32 次观察者异常、16 次 worker 调用、8 次显式嵌套；还有 Start 内同步 Execute/Release |
| 同一原生 fixture 的节点及输出故障 | 3 个 Before 读取故障、2 个 Start 输出槽故障、5 个原函数释放节点页情形；73 次顶层调用返回的 AL 字节 ≥ `0x80` | 复制失败仍调用原函数，保留 Unknown；正常 AL 字节保持，释放后仍能用 Before 标量配对。没有证明完整 RAX 或全部异常 flags/context 透明性 |
| [真实 Dobby→CLR→实际节点账本](engine-node-managed-evidence.json) | 5 个独立进程、`855` 次检查、`44` 次实际原函数，`28` 个实际捕获 context 各自 Complete/Dispose 配对 | 四类通道共用一份真实 Adapter 账本；实际 frame 交给双 sink，覆盖正常、原 SEH、原 C++、逻辑解绑和观察者错误。正常进程在 Start 返回前完成同步节点关联，原件记录同一已释放地址的两代节点 |

CLR 五模式分别为 normal `216`、original-seh `155`、original-cpp `151`、detach `192`、observer-fault `141` 次检查。异常和观察者错误保留 Unknown，正常与逻辑解绑模式保留 Pending；所有模式最终 native/dispatcher in-flight 和 ledger outstanding 均为 0，`EngineCancelled=false`。原函数在 sticky Unknown 或无 sink 时仍正常转发，因此实际原调用和捕获 context 分别计数。原生旧调用跨 Attach 的确定性阻塞由 native EXE 验证，未把它写成 CLR 五模式的同一旧调用验证。

该联合层执行真实 Dobby 准备和发布、真实原生调用、生产 bridge/dispatcher 及实际 Adapter 账本；节点内容、metadata 身份是 fixture 控制值，没有执行生产对象 backend、IL2CPP metadata/GC、实际引擎取消或游戏函数，也没有使用 FirstInstall receipt。ABI 与故障细节可从[原生测试入口](../../../scripts/Test-EngineNodeNative.ps1)、[fixture 源码](../../../native/RizToolbox.JudgeContext/fixture/EngineNodeFixture.cpp)和 [CLR 联合测试](../../../tests/RizToolbox.Runtime.Tests/NativeEngineNodeManagedRegression.cs)复核。

## 冻结交付与保留的失败记录

[变更清单](changes-from-operation-preinit.json)相对上批新增 15、修改 4、删除 0，声明源码由 546 增至 561。[构建输入核对](build-input-integrity-check.json)、[当前源码核对](source-integrity-check.json)和[源码归档核对](source-archive-check.json)确认 561 项一致；[包归档核对](package-archive-check.json)确认 22 项逐项匹配。源码 ZIP SHA256 为 `28180b96ced1cc7e22eb37317106f4d76655c31da6e1db5cf7accfc465fa9193`，暂存包 ZIP 为 `f559489c9f5747f9f64ad6b973618e96a1822496f93c033020df1044a8973451`。这些归档不包含外部依赖、工具链或干净环境全矩阵重建证明。

[暂存检查](staging-check.json)确认新包未安装；[旧安装检查](previous-installation-check.json)确认旧 1021 批 18 项安装和其 326 项源码归档匹配；[原游戏检查](original-game-check.json)确认 3 项原游戏文件未变。本批说明文档不属于 561 项冻结构建输入。

[预检记录](preflight-run.json)保留最初 Audio `621/627` 的 6 个既有联合环境缺失失败，以及新增测试的 `CS0266`、`CS0170` 编译错误原日志。编译错误在源码冻结前修复；最终完整构建提供所需联合环境并全部通过。完整日志保留 111 条不同原始 compiler diagnostic 文本，含 upstream、既有警告和 ANSI 差异，不等于 111 个独立问题，也不称零警告。复现完整构建的入口为 [run-build.ps1](run-build.ps1)。

## 当前仍不能宣布取消完成

本批已配置通道的采用合同只证明传输身份，**没有接入游戏的 UnityPlayer 首次安全安装，也没有 V4 Ready 生产凭据或生产 coordinator 绑定**。此前 V3 的 18 个 GameAssembly 操作入口不能授权这四个新引擎入口。生产 owner/tracker 与真实 native 节点的关联、全部 scheduler 登记和撤销、Start 同步执行的内联清理及全消费者退休仍需后续实现。

账本将“摘链返回”“观察到 Release 的 AL=0”“执行深度为零”作为分开的事实。它始终输出 `EngineCancelled=false`，保留 Pending；既有 `RequestPlay` 继续关闭。完整 Retry、Return、Loader 与真实 IL2CPP/GC、冷启动和整局验收均未由本批离线结果证明。按用户“先继续代码，实测稍后”的安排，本批没有游戏/UI、安装或游戏进程终止。
