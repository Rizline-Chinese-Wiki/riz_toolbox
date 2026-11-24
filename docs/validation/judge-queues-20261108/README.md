# Judge 三队列写入与完整回退：2026-11-08

`RunValue.PrepareSeekJudgeQueues(profile, targets)` 已接入真实 backend。按明确的 Notes、TapWaiting、DragWaiting 引用顺序调用官方 `List.Clear/Add`，保留重复成员，失败后条件恢复原数组、完整容量槽（包括尾部）、数量和有符号版本。不会根据时间或 NoteControl 顺序猜目标队列。

最终新源码验证：**93/93 判定测试（新增 23 组），36/36 底层写入回归**。六组件及判定测试项目零警告、零错误；Runtime 测试项目有 1 条既有夹具字段 `DuringHandleTarget` 未赋值的 CS0649 警告、零错误。见[构建结果](build-test-result.json)、[证据索引](evidence-index.json)及[独立源码复核](source-review.json)。

## 已验证行为

- 全部目标来自独立保留的实际 NoteView；重复 occurrence 保持原意，同一个 List 的别名目标必须一致且只写一次；不同 List 共用空数组可用。
- 扩容须精确符合 0→4 或翻倍，不能冒用其他队列/原数组；连续扩容会独立验证本次旧数组未被改动。原数组及其尾部引用始终可追踪。
- 原生调用前的最终托管检查位于元数据、临时保根、参数准备之后。撤销可阻止尚未进入的写入；已执行后抛错保留责任，不盲目重试。
- 回退按原数组完整槽、size、items、version处理，并独立读回完整原状态；未知内部部分写入或外部改动不会被假装成自己的写入覆盖。
- 补齐 guarded 字段写入的临时引用和自带参数释放责任。未知 native free 保留原 owner，参数容量拒绝由调用方清理，避免丢失或重试。
- 覆盖真实 RunValue、扩容/别名、23 组队列故障及生命周期用例；旧 70 组判定保持。36 组底层回归包含原 void/field API 和新增 guard、容量拒绝、未知释放测试。

输入 613 项构建前后哈希相同；两个测试程序集的 SDK/Core/Runtime/Adapter DLL/PDB 共 16 份引用与本次六组件产物一致。全部直接执行新 D 目录 DLL，没有用旧 C/bin 或交付 ZIP 验收。r1 的 92/93 失败记录完整保留；最终 guard 注入改用实际 InvokeCore 的 List receiver 保根窗口，保留“注入触发、零尝试、零写入、原状态不变”断言，r2 才全通过。

## 边界与下一步

本轮是受控导出下的正式源码组合验证，未运行 Unity 或真实游戏。不同 List 共享非空 backing 暂不支持；预测翻倍后超过 65536 容量的目标在写入前拒绝。阶段做完整图验证，逐写仍有完整队列/容量条件读回成本，尚未作性能验收。条件采样到 native dispatch 不是原子 CAS，不能排除未观察的外部 ABA。

`fingerIds` 输入集合、目标生成/渲染同步、Hold 引擎退出及完整事务仍待补齐；不写 Hold count，也不声明 HoldIdle。完整 Speed/Seek 与 B01–B19 尚未完成。下一步见[Seek 组合计划](../../../.docs/foundation-expansion/NEXT-SEEK-COMPOSITION.md)。2026-11-07 的开发预览 ZIP 保持原哈希，本轮新源码未放入该旧包；未安装/启动游戏、操作 UI 或账号。
