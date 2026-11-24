# 端点位置写入：本批实现与离线验收

端点位置写入已进入当前主树，并在新 891 输入图上通过本批离线验收。它沿原 Canvas Plan 和独立保留的 LineRingReader 操作真实 Transform receiver，提供准备、写入、读回确认和所选值的逆序恢复。当前支持父对象已经匹配目标 Canvas 槽位的端点；父对象不同仍会拒绝，真实 `SetParent(false)` 正在后继候选中实现。

完整 SDK、B01–B19 和完整 Speed/Seek 仍未完成。本次没有安装或启动游戏。现有 2026-11-13 ZIP 是此前 881 输入的开发预览；本批 891 源码尚未重新打包，旧包不认证新源码。

## 开发者获得的底层能力

正式入口是内部 `NativeSeekLineEndpointCommands.Prepare`，其 source factory 返回从原 Canvas basis 创建的 `NativeSeekLineRingSourceCapture`。命令对象取得该 source 的唯一归属，持有同一个原始 reader，不接管调用者的 Run、Canvas aggregate 或 CanvasCapture。它使用已接入的无 Note chart occurrence 和局部端点 provider；没有依赖 Hold、运行中 Note 或按时间猜测段落。

调用顺序为 `Prepare → VerifyPrepared → Execute → VerifyAppliedValues`；需要恢复时执行 `RestoreSelectedValues → VerifyRestoredValues`。`GetJournal` 保留每次 setter 的 native entry、return、读回、目标或基线观测和失败信息。相同 Transform 的相同目标保留所有来源行，实际 setter 按唯一 Transform 去重。父对象、别名目标、当前来源或类型不满足时拒绝；已观测的第三种状态会阻止继续写入和逆向操作。

`Dispose` 不能消除已进入 Unity 的效果。即使原始位置数值已恢复，命令对象与原始 source 仍保留效果责任；调用者须保留日志给出的原 owner。未知临时 native 清理会保留准确异常和 source cleanup owner，不重试原 free。

## 验证范围

| 项目 | 当前证据 |
| --- | --- |
| 主源码 | 891 项编译输入，另有 Build.ps1 控制 |
| 六组件 | Release 构建成功，0 警告、0 错误 |
| 解决方案 | 构建成功，0 错误 |
| 新写入测试 | 17/17：真实后端 fake-native 路径，包含正常/失败写入与逆序恢复、父对象不匹配、全量预检、第三态、回调取消/重入、迟到取样变化、临时 rooting 后重查、类型变化、未知清理、外层释放、真实 Transform 别名 |
| 相邻 Judgements | provider6、chart occurrence5、endpoint6、numeric13、Ring X7、source13/14/17/18/19、basis1，合计与新写入测试为 60 项 |
| Runtime ABI | 51/51，使用本批新 Runtime 字节 |
| 参数错误 | 2 项均退出 2，stdout 空且 stderr 为精确 usage |
| 本批总计 | 18 个自然终止步骤、111 项正向检查；原控制进程实际退出 0 |
| 字节保持 | 六生产 DLL/PDB pair、两测试 pair、12 个测试依赖，以及输入、控制和 3 个 native fixture 均核对 |

本批只核对旧 300 项的名称和顺序，没有重新执行旧 300、完整 source19、完整 basis16 或默认389单进程；上一 885 输入批次的 47/72 证据单独保留。

## 原子性与完整 Seek 的边界

坐标和完整向量会重复观测，setter metadata、临时 rooting 与参数准备有逐出口 lifetime guard，记录 native entry 前会再次核对已准备的目标状态。但 native getter、profile 或引擎回调仍可能在最后一次读取之后改动状态，有限次读取不能成为 Unity 原子事务。

因此 journal 明确 `AtomicConditionalWrites=false`、`BeforeIsAtomicPreState=false`；`Before` 表示最后准入的观测值。所有实际进入 Unity 的操作继续保留 `SideEffectsUnknown`、`ExternalEffectsUnclosed` 和原责任对象。世界目标、相机/场景、canonical Ring seed、scratch/renderer、Hold 引擎效果及完整 DSP/时钟事务仍需闭合；`CompleteSeekPermitted` 保持 false。

## Evidence → Finding → Path

范围为用户授权的本地离线底层开发和验证；不操作游戏、账号、代理或永久环境。报告使用通用工程结构，flavor 为 null。

| Evidence | 文件 | 观察 |
| --- | --- | --- |
| E-001 | [本批摘要](validation/line-endpoint-writer-20261113/validation-summary.json) | 891 输入、18 步骤、111 检查及范围限制 |
| E-002 | [原句柄退出记录](validation/line-endpoint-writer-20261113/run/controller-handle-exit.json) | controller29728 的原始句柄保持到退出，实际 exit0，结果 SHA 一致 |
| E-003 | [原始执行记录](validation/line-endpoint-writer-20261113/run/result.json) | 每步实际 argv、自然退出、stdout/stderr SHA、产物前后字节 |
| E-004 | [独立源码复核](validation/line-endpoint-writer-20261113/reviews/review-root-r2.json) | 生命周期/观测缺陷修复，保留原子准入开放项 |
| E-005 | [归档清单](validation/line-endpoint-writer-20261113/artifact-manifest.json) | 988 个归档文件的逐项校验 |

F-001（validated，E-001/E-002/E-003）：parent-equal 的 native localPosition 命令已接入当前源码和新测试字节。它提供实际 setter 与状态/责任日志，不是只有纯模型计算。

F-002（validated，E-003/E-004）：回调取消已接到原 reader 的最深 lifetime guard，取样和元数据/rooting 时机的测试覆盖本次修复；未知清理的准确 owner 保留且无重复 free。

F-003（open，E-001/E-004）：完整 parent 切换、原子准入和引擎效果闭合仍未实现/验证。本批不能用于开启完整 Speed/Seek 或签署 SDK 整体完成。

P-001（callflow）：原 Canvas basis → 独立 LineRing source → chart/局部端点投影 → 唯一命令 owner → 当前来源及完整向量观测 → typed native setter/entry 日志 → 读回 → 按实际已进入顺序逆向恢复所选值。E-001/E-003 支持 F-001/F-002；F-003 的边界始终保留。

## 复核与续接

归档文件可独立复核，不需要当前主源码或游戏文件：

```powershell
python -B docs/validation/line-endpoint-writer-20261113/publisher/publish.py --check-only --archive docs/validation/line-endpoint-writer-20261113
```

复现编译/测试需要固定 .NET SDK 8.0.418、引用包和原离线 native fixtures；原始 argv、输入快照和执行脚本已随归档保留。重做执行应使用新输出目录，保留本批原始结果。续接入口是 [.docs/CURRENT-CHECKPOINT.md](../.docs/CURRENT-CHECKPOINT.md)，下一候选位于 `work/line-endpoint-parent-writer-20261113/`。
