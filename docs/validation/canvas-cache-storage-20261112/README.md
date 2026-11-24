# Canvas 计算缓存同步：本批离线验收

正式 RunValue 已接入 floorPositions / floorSpeeds / xPositions 三个完整实际 Single[] 和 nowTime / speed 两个 Single 的显式写入、确认及原始位条件恢复。**只完成计算缓存，不调用 Canvas producer、Transform setter 或 Update，不开放完整 Seek。** 未安装或启动真实游戏；运行证据来自受控原生导出与自有夹具。

| 检查 | 结果 |
|---|---|
| 六组件 Release | 成功，0 警告 / 0 错误 |
| 判定默认完整执行 | 300/300，含新增 16 组缓存检查 |
| Runtime ABI / 精确静态读取 | 51/51、6/6 |
| AudioContent 默认完整执行 | 1001/1001 |
| 冻结输入 / 测试依赖 | 843 项未变、24 份 DLL/PDB 与当前六组件匹配 |
| 独立源审 | 73 个实现/支持/测试文件哈希匹配，无必要阻断 |

实际命令、退出及产物哈希见 [完整结果](build-test-result.json)，警告数见 [索引](evidence-index.json)。Runtime/AudioContent 测试构建分别有 1/27 条既有测试警告、无错误；不是全解决方案或全部 B01–B19 的验收。

目标取同一个实际来源 Plan 的 FloorPosition / FloorSpeed / XPosition 和 Request.ChartTime / Speed。声明顺序是三个完整数组后两标量；不模拟 native Update 或 engine 发布点。实际数组引用不替换、不扩容。跨语义字段的物理 (owner,index) 目标冲突零写拒绝，一致位只写一次；空图仍覆盖两个标量。

数组写入先核 fresh Single ABI、官方 rank/element/length/header/byteLength/objectSize、已登记 pin、完整内存边界与四字节可写页。pin/metadata回调后再次核当前来源与引用，最后核 expected 原始位再进入。标量专用接口收 saved descriptor，所有准备和当前来源检查后两次官方读取 expected 位，之后只做 managed guard 与正式 setter。双次采样不提供原子性或 ABA 排除。

完整来源独立保根，RunValue 退出后已有事务仍有效。不可变 journal 分开记录进入、返回、pending、读回及恢复；原值允许 NaN payload / -0，目标必须有限。第三态与未尝试槽外改 sticky，不覆盖外部状态。Confirm / ConfirmRestored 只关闭本缓存已知存储责任，Dispose 对已确认且待释放的事务再做完整读回；已有位置命令的 Unity 外部效果及 owner 继续独立保留。临时 pin / 最终 root 释放或构造 cleanup 失败保持精确公共 owner、原 native cleanup owner 和 failure，未知 free 不重试，未写入也不丢失临时责任。

| Evidence | Finding | Path |
|---|---|---|
| E-001：[结果](build-test-result.json)、[输入](inputs-before.json) | F-001：本批新图离线矩阵通过 | 实际源 Plan → 五类缓存 → typed 条件写 → 完整读回 → 条件恢复 |
| E-002：[合同核验](cache-contract-verification.json)、[静态依据](cache-static-evidence.json) | F-002：5字段、4完整范围 / 2224B、20指令断言；复用原profile | 冻结机器码 → 实际槽和字段 → 当前运行时准入 |
| E-003：[当前复核](independent-source-review-r3.json)、[状态](source-review-status.json) | F-003：复核覆盖当前验收哈希，不代表真实游戏观察；原版复核保留 | metadata/source/生命周期/cleanup → 原始位责任 |

P-001（callflow）：E-002 固定合同 → E-001 完整来源与数组保根 → 最后来源/布局/expected位检查 → 正式存储与 entered journal → 当前值确认 / 已进入槽条件恢复 → E-003 精确 owner。迭代失败和日志（如有）保留；未运行257个同时未闭合缓存owner的容量试验。

在项目根复现，输出使用不存在的新目录：

```powershell
C:/Python314/python.exe -B work/canvas-cache-validation-20261112/validate_current.py --output D:/riz-build-cache-next/canvas-cache-20261112/manual-recheck
```

`--cache-only` 只运行16组，不替代完整矩阵。自有 native 夹具先核哈希，只对子进程设路径，不改永久环境。实际目录：`D:\riz-build-cache-next\canvas-cache-20261112\root-r3\final-r1`。下一步见 [Canvas basis 组合合同](next-canvas-basis-contract.md)。真实 Speed/Seek 关闭，B01–B19 未完成；未操作 UI/账号/代理，未改旧ZIP，未提交或推送Git。
