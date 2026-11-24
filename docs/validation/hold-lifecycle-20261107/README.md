# Hold 跨帧生命周期：实际调度与自然结束

2026-11-07：本批源码已整合并完成离线验收。六组件 Release 和判定测试项目均为零警告、零错误；直接执行本轮新 DLL，**61/61 组通过，其中新增 Hold 8 组**。545 项构建输入前后不变。实际结果与文件哈希见 [evidence-index.json](evidence-index.json)。

## 实现范围

- `JudgementModule` 复用现有 native coroutine transport，先绑定 `MoveNext@0x402670`，再绑定 `StartCoroutine@0x153D380` 及独立校验的 Auto alias。没有新增错误的 CheckHold factory ABI，也没有钩住共享空 Dispose。
- Start 在原函数前登记实际 iterator、owner、note 和 run；原函数可以同步调用第一次 Move。Start 返回的实际 Coroutine handle 加入长期保留的引用集合。
- Move 观察返回寄存器的低字节：非零表示继续，零仅确认这个 iterator 自然结束。完成记录及引用留到 run 退休，防止地址复用被误认成同一个身份。
- 暂停可增加 revision；同代采样及已进入的回调都能继续。换局不把旧回调归到新局，退出模块或解绑时仍由原调用的 finally 释放在途引用。
- 同步 Hold 判定上下文独立保留。生命周期采集失败不会破坏已有的长按判定归属。释放结果未知时保留原 owner 和异常，不重试释放。

独立交叉审已修正同代 revision 刷新、同步归属与新采样解耦、参数求值前过早转移所有权，以及旧引用清理回调后再次核验新关联等问题。正常 Move 清理不扫描全部累计记录；每次 source 采集仍有最多 1024 项的 controller discovery，性能优化后置。

## 实际验证

本轮使用实际 `ExactHookBroker` 的 Prepare / Configure / Commit、Enter / Exit 和 `NativeJudgementBackend`，以受控 bridge 及合成 IL2CPP 导出提供对象、数组和原调用。覆盖同步 Start→Move、非标准 true 字节 `0xff` 与 false、暂停及采样中的 revision 增长、缺失/重复 Start、异常、在途解绑、换局退休、worker/foreign MethodInfo、音符关联变化及未知释放。

原生 Unity 引擎和游戏 UI 未执行；这些测试验证离线组合行为，不代替实机调度。取消测试直接调用 ledger 的 request-only 入口，**没有测试或实现实际 StopCoroutine 接线**。

构建与原始日志位于 `D:/riz-build-cache-next/seek-next-functional-20261107/runs/hold-lifecycle-r1/`；实际进程回执位于相邻 `validation/hold-lifecycle-r1/`。root session 94166 与实际构建 child 53224 均自然退出 0。执行命令为该 run 下的 `dotnet exec .../RizToolbox.Judgements.Tests.dll`，没有使用旧 C/bin 或旧 ZIP。

## 剩余边界与下一步

`FullCoverageKnown` 始终为 false。本批没有覆盖调度前的工厂创建、真实 Unity 停止/最终清理、所有未观察到的 iterator，也不提供全局 HoldIdle 证明。取消请求、空记录和计数器归零都不等于协程已退出。关联复核为条件观察，不是原子快照。

下一步补实际停止请求与引擎退出确认，再接 Judge 队列、输入集合和渲染/目标状态重建。完整 Speed/Seek、B01–B19 仍未完成；今日开发预览 ZIP 保持原冻结版本，本批新源码不在该 ZIP 内。未安装或启动游戏，未操作账号。
