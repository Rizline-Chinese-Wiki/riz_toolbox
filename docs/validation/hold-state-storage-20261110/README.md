# Hold 两 List 与显式状态恢复：2026-11-10

`RunValue.PrepareSeekHoldState` 现在接受两条实际 Hold 列表的完整顺序和每个实际长条的显式状态，提交、读回确认并条件恢复原存储。它支持同一谱面位置的多个实际实例、重复 occurrence、空目标、相同 List 别名，以及原生扩容。逐长条字段为 `isStaticNote` / `lastFrameHoldLineLength`，控制器字段为 `_nowTime`（谱面坐标）/ `_speed` / `_holdLineLength`。

新 DLL **178/178 判定测试（新增 22 组），51/51 Runtime 回归**通过。六组件/判定/Runtime 构建警告依次 0/0/1，错误均 0；676 项输入前后相同，两个测试目录 16 份依赖 DLL/PDB 与当前六组件相同。见[构建结果](build-test-result.json)、[源码复核](source-review.json)和[证据索引](evidence-index.json)。

此前 r1 进程消失且无最终 DTO，按中断保留、不签通过；r2 为 177/178，历史数组测试的注入实际写回同一引用，修正为明确不同的合法 Hold 后保留原强断言。本批还补充已确认后再次读取失败的回归：每次合法重新确认先撤销旧成功结果，失败后持续保留责任。

恢复保留原 List/backing 身份、每个有效槽和容量尾槽、size、完整 signed version，以及浮点原始位（含负零、NaN payload）。先把有效范围降为 0，再还原槽和原 backing，最后恢复 size/version；不会让空值或历史非 Hold 尾引用临时进入有效范围。仅精确原状态/完整合法后状态可归属本次调用；已观察的第三状态保持未知，即使外部随后复原数值。未提交时的恢复不产生多余写入；所有实际 dispatch（含 inverse）均跟踪责任。

每个数组上限 65536，两个原 backing 总容量上限 131072，全部历史扩容数组合计上限 262144。不同 List 共享非空 backing 明确 Unsupported；共享空 backing 可用。最多保留 256 个未闭合 owner，未知 inverse/free 不自动重试。准备只读；完整图检查位于阶段边界，逐字段按实际对象校验。

固定文件核验新增 5 个候选完整 body、1073 字节，组合已冻结的目标 profile。离线没有观察到 closed List 的真实方法入口关联；生产 profile 必须从两个实际字段取得同一 closed class，并在每次校验中匹配布局、MethodInfo、入口和代码体，条件不符拒绝。合成导出测试使用正式 NativeRuntime/backend，未运行 Unity 或游戏代码。

目标坐标/几何、Transform、renderer 事件及延后回池、输入与 Hold 协程/引擎退出、Judge 初始化写入和完整 Seek 事务仍待完成。真实 Speed/Seek 关闭，B01–B19 未完成；阶段检查并非原子 CAS，不能排除外部 ABA。本批未安装/启动游戏或操作 UI/账号；旧预览 ZIP 未更新，本批源不能用旧包结果验收。
