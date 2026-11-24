# 原生 void0 transport 复核

本页记录对 native helper、C# bridge、broker 和 boundary probe 接线的只读复核。此前发现的故障报告回调抛出后未设置 Unknown 的问题已修复，并有独立干净 entry 的回归覆盖。这里只确认 transport 边界；游戏内监听者执行集合、取消和资源退休仍按 [本批范围](scope.md) 保持未完成。

| 边界 | 当前实现与结论 | 直接证据 |
| --- | --- | --- |
| ABI | `FrameV1` 为 `0x28`：四个 `uint32` 后为 `self/method/caller`；`ConfigV1` 为 `0x28`；`StatsV1` 为 `0x30`。C# 顺序布局与原生静态断言一致。Enter 返回 token，Exit 接收 frame/token/flags/user，没有结果替换参数。flags 为 Returned=1、Abnormal=2、EnterFaulted=4。 | [VoidContext.h](../../../native/RizToolbox.JudgeContext/VoidContext.h)、[NativeVoidContextBridge.cs](../../../src/RizToolbox.Runtime/Hooks/NativeVoidContextBridge.cs)、Hooks `Layout` |
| 32 个永久槽 | 独立于 coroutine kind。配置经空槽→发布中→就绪；已认领不可重配，未知发布不可重试。合法但尚未配置的 entry 执行时 fail-fast。broker 用 Prepare→Configure→Commit，逻辑解绑保留 patch、bridge 和 callbacks。 | [VoidContext.cpp](../../../native/RizToolbox.JudgeContext/VoidContext.cpp)、[ExactHookBroker.NativeVoid.cs](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeVoid.cs)、Hooks `Publication/Unknown/ProcessRoots` |
| 原函数与异常 | 原生 `__try` 中唯一调用 original；`__finally` 执行 Exit、退出计数和 inFlight 递减。观察者 SEH/C++ 故障局部包含，原函数异常继续向原生调用者展开，不能由观察者跳过原函数或改写返回。真实 `_ReturnAddress()` 在 entry 捕获。 | [NativeVoidAbiRegression.cs](../../../tests/RizToolbox.Runtime.Tests/NativeVoidAbiRegression.cs) 四个真实 Dobby/MASM/C++ 案例 |
| 4096 条同时调用 | broker 先保留调用记录，超过 4096 时在 probe Enter 前拒绝新观察、返回零 token、设置 sticky Unknown，仍由 native 执行 original 一次。此前 4096 个 token 的 Context/Probe 继续各自退出；不会给溢出帧伪造 probe Exit。 | [NativeVoidObservationTests.cs](../../../tests/RizToolbox.Hooks.Tests/NativeVoidObservationTests.cs) `Capacity`：第 4097 帧在途数为 4097，最终为 Unknown/0 |
| worker 与跨线程 Exit | worker 不进入 broker `Gate`，仅保留标量 frame/probe 并报告故障，跳过 Unity 元数据捕获；Commit 持 Gate 时 worker 可返回。代码对不可能的跨线程 Exit 保留原 record 并 sticky Unknown，不授予回收权限；该分支结论来自代码复核，未冒充真实 native 跨线程展开测试。 | broker `EnterNativeVoid/ExitNativeVoid`；Hooks `WorkerDuringCommit/ConcurrentWorkers` |
| 每次 probe 配对 | 调用记录保存入口时不可变 probe 数组，早于 frame/thread/method 验证。正常、异常、重入和入口后解绑均使用原数组逆序退出。Context 逆序 Dispose 后才退出 probe；probe 失败设置 Unknown 并向该调用的 owner 报告。 | [ExactHookBroker.BoundaryProbe.cs](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.BoundaryProbe.cs)、Hooks `Reentry/Detach/ProbeFaults/OriginalFault`、真实 Dobby probe 快照案例 |
| 实际 native drain | binding、probe 和 exact activity 汇合 `max(保留记录数, native inFlight)`。managed After/Dispose/Probe Exit 返回后、native finally 完成前，仍能读到物理在途；统计读取失败、负值、ABI 不符、原生 callback fault 或 broker Unknown 都不能恢复为 Known。计数归零只表示 transport 当前静止，不代表监听者来源封口或资源可释放。 | Hooks `NativeExitActivity/Unknown`、真实 worker/detach 与 callback fault 案例；bridge `ReadActivity` |

## 已关闭的审查问题

此前 `NativeVoidFaults` 直接吞掉 reporter 异常，而 worker/foreign method 分支允许 owner 按自己的标量身份判断责任；如果 reporter 自身抛出，entry 可能仍被报告为 Known。现在该方法接收 `Entry`，在每个 reporter 的 catch 中原子设置 `NativeVoidUnknown=1`，同时继续调用健康 reporter。

独立 `FaultReporter` 测试创建全新的 target/bridge，首个故障即来自 worker 的 reporter 抛出；验证 original 一次、健康 reporter 一次、probe Exit 一次，两个 binding 和 probe 均为 Unknown/0。它不依赖前序 capture/after/dispose 故障，避免原先已 Unknown 的状态遮蔽缺口。

[首轮全量日志](build-first.log) 已记录 Runtime `159/159`、Hooks `144/144`，包含该独立 reporter 案例及四个真实 native 案例通过。首轮随后在 Audio fixture 编译处停止，因此这些局部结果不能称为本批全量完成；最终全量结论由本批最终日志与汇总提供。本复核未构建、未操作游戏，也未修改源码、测试或 native 文件。
