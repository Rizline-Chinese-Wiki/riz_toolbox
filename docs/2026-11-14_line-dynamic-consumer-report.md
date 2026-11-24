# 动态线条缓冲消费链：离线验收

动态线条的实际数据消费链已合入主树，并在新构建上通过 155 项专项检查。它将原 Canvas 的已应用目标、端点写入后的实际世界坐标和同来源当前场景参数接到已有数值内核，生成非空的有序动态线条缓冲区。

本批没有安装或启动游戏。完整 SDK、B01–B19、实际 renderer 写入、Ring 初始化、Hold 引擎、DSP schedule 和完整 Speed/Seek 仍未完成；完整 Speed/Seek 保持关闭。

## 已实现的行为

消费前后都调用原 Canvas aggregate 的完整 fresh `VerifyAppliedValues()`，并保留原 capture、Plan 和 endpoint writer 身份。世界坐标来自原 writer 最新发行的记录；当前 LineControl speed、缓存 gameSceneScale 和 Camera scale 分别读取，不能换成 Plan 请求速度或 UI Transform scale。

floorPositions 使用 raw LinePoint.canvasIndex；端点的物理父对象则通过 CanvasMove.index 匹配得到，两种索引分开保存。全部 dynamicLines occurrence 按原顺序输出，包括重复物理 Line 和没有 Note 的线条。被 floor/world 窗口裁掉的 occurrence 是 NoBufferUpdate，不能写成零长度 SetPositions。

结果属于自有历史数据，`AtomicSnapshot=false`、`DeterministicTarget=false`、`CompleteSeekPermitted=false`。这一步只生成 producer-input buffer，尚未调用 renderer setter。相同的两轮观测不证明原子快照，也不消除已经存在的未知 Unity 外部效果。

## 本批验证

| 项目 | 结果 |
| --- | --- |
| 接入 | 8 个 C# 文件，3 个新增；895→898 输入；Build source 范围 24→30 |
| 六生产组件 Release | 0 警告、0 错误 |
| 解决方案 | 0 错误；原测试 fixture 警告另列在日志 |
| endpoint writer | 36/36 |
| 完整来源读取 | 30/30，含六项新增 dynamic consumer 检查 |
| 相邻专项 | provider6、chart5、endpoint6、numeric13、RingX7、basis1 |
| Runtime ABI | 51/51 |
| 非法参数 | 两项实际 exit2，stdout 空，stderr 为精确 usage |
| 总计 | 78 个自然退出子步骤、155 项正向检查（104 Judgements、51 Runtime ABI） |
| 归档 | 1234 个文件，54,620,689 bytes |

新增六项覆盖非空 101 点缓冲与重复 occurrence、原 Canvas/owner 准入、cull 与空动态数组、当前 speed/world 漂移、恢复后拒绝及历史结果不变、raw floor index 与物理 Canvas 槽位错位。案例中 Line 缓存 scale=4、UI.X scale=2，验证实际除数来源；错位 CanvasMove.index 案例使用不同 floor，避免值相等掩盖索引错误。

所有测试使用本批新构建。源码、控制、六生产/两测试 DLL/PDB pair、12 个依赖、3 native fixture 和实际数值 golden 均核对。controller14156 的原 Windows handle 由 observer23532 保持至实际 exit0。旧 300 项只列清单；本批没有声称执行默认 419 项单进程、完整 basis16 或整个 SDK 回归。

## Evidence → Finding → Path

范围为用户授权的本地离线底层开发，沿用 [场景来源范围](../work/line-camera-scene-source-case-20261113/scope.md)。本批 flavor 为 null；附属文档、dump 和冻结反汇编作为分析数据处理，不扩大用户授权。

| Evidence | source_ref | SHA256 | 复核方式 |
| --- | --- | --- | --- |
| E-001 | [验收摘要](validation/line-dynamic-consumer-20261113/validation-summary.json) | `4e56d42e620345ca89b0fad3bcf8df4eca99fd3854388ff352b2f8a8853fe367` | 下方 archive-only 命令 |
| E-002 | [实际执行记录](validation/line-dynamic-consumer-20261113/run/result.json) | `2546860ab24b607e24eaf9e3fc7d40ad89007d6d26291f12db08462aa9a9d494` | 同命令逐项检查 argv、RUN/PASS、自然退出与原日志 |
| E-003 | [原句柄退出记录](validation/line-dynamic-consumer-20261113/run/controller-handle-exit.json) | `f34de4b88ab8438c10368f25b0159a5442e2ac73e9ac579ce228c1629666464b` | 同命令核对原 PID、脚本和结果 |
| E-004 | [实际发行与索引源码审查](validation/line-dynamic-consumer-20261113/reviews/prior-product-review.md) | `5a3aeb9f746c8d48741ea4035f82994d9fa02ea5c9b51a691afc4d8de5a0e156` | 同命令核对原审查；结合 v4 修复及完整源码 |
| E-005 | [v4 用例时机审查](validation/line-dynamic-consumer-20261113/reviews/03-review.md) | `5ece16e56c69f43aa3ba9233af097564795be786672fb0c59329caa3b2b92401` | 同命令核对最终 v4 和实际 shadow |

F-001，status=validated，severity=n/a_re，confidence=high，evidence_ids=E-001/E-002/E-004：`NativeSeekLineRingDynamicConsumer.Project` 实际消费原已应用对象，输出有序非空缓冲并保留 cull/重复/no-Note 语义。

F-002，status=validated，severity=n/a_re，confidence=high，evidence_ids=E-002/E-004/E-005：raw floor index 和物理父槽位、Line 缓存 scale 和 UI scale 的两类来源混用已修正，差异值及错位用例在新主图通过。

F-003，status=candidate，severity=n/a_re，confidence=high，evidence_ids=E-001/E-004：renderer writer 和原共享 scratch 的实际消费、Unity 效果与完整 Seek 尚未闭合；本批的数据结果不授予后续写入许可。

P-001，path_type=callflow：原 Canvas fresh applied 校验 → 原 endpoint writer 最新 world 记录 → 原 reader 当前 scene/speed/scale → 完整 occurrence 和 raw floor 索引 → 既有精确数值内核 → 再次完整 world/scene/Canvas 校验 → 自有历史缓冲。该路径支撑 F-001/F-002，保留 F-003。

## 失败记录和时间线

- 2026-11-13 23:10（本地）：v3 首次 shadow 的 source28 失败，root-r1 原结果和原句柄 exit1 保留；过早的 world 漂移钩子及清理异常遮盖问题随后修复。没有以已有三个通过项认证 v4。
- 23:25：v4 新 shadow W0/E0、12/12，自然 exit0；完整候选、控制、日志和产物冻结。
- 23:30：8 个 C# 文件和 Build 单行改动合主；原 application-record 保留当时 validation=false。
- 2026-11-14 00:13：新主图 78 步骤/155 专项全部自然通过，原 handle exit0。
- 00:14：原子归档、archive-only 复核均实际 exit0，checkpoint 更新为本批接受。

原归档工具还有一次 live shadow 日志目录修正；原工具与独立审查保留，最终改动及只读复核见归档中的 root-log-path-review，未改测试结果或日志。

## 独立复核与后续

```powershell
python -B docs/validation/line-dynamic-consumer-20261113/publisher/publish.py --check-only --archive docs/validation/line-dynamic-consumer-20261113
```

该命令只读取归档。下一项原生 Vector3[] 创建/publication 接入位于 `work/line-vector3-array-integration-20261113/`；其 61 项隔离测试已通过，正式主图须另验。后继共享 scratch 与显式 SetPositions 字段交接位于 `work/line-renderer-next-20261114/`，不能据本报告称 renderer 已完成。最新状态见 [.docs/CURRENT-CHECKPOINT.md](../.docs/CURRENT-CHECKPOINT.md)。
