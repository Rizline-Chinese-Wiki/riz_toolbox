# 写入后世界坐标读取：本批离线验收

世界坐标读取已接入主树，并在新构建上通过本批 130 项专项检查。`NativeSeekLineEndpointCommands.ReadAppliedWorldObservation()` 沿原来保留的 LineRingReader，在端点父对象和局部位置写入完成后调用实际 `Transform.get_position`；返回结果明确携带原 source 和命令 owner。

完整 SDK、B01–B19、完整 Speed/Seek 和游戏实测仍未完成。本批没有安装或启动游戏，也没有覆盖既有开发预览 ZIP。

## 行为和边界

读取要求本命令已完成应用且尚未开始恢复。它先核对完整端点与父级关系图，再读取两轮 parent/local/world tuple，逐分量比较 Single 位值、拒绝非有限 world，并在发布前重新核对端点和关系图。观察到漂移会阻止继续写入或逆向恢复；getter 回调取消则由原 lifetime guard 停止后续原生访问，并保留已经进入 Unity 的效果责任。

返回的是自有只读历史观测，`AtomicSnapshot=false`、`CompleteSeekPermitted=false`。它提供写入后的实际读回值；不使用旧 `Snapshot.Transforms.Position` 代替目标世界坐标，也不证明确定性世界坐标重建或 Unity 原子性。原先的 `SideEffectsUnknown`、`ExternalEffectsUnclosed` 和原 source/owner 继续保留。

该内部 record 是数据，不是授权后续原生写入的凭证。后继相机/场景组合接口正在单独补 owner 发行身份检查；该候选尚未构建、合主或验收，不能据本报告声称它已完成。

## 本批验证

| 项目 | 结果 |
| --- | --- |
| 主源码改动 | 4 个替换文件，输入集合保持 892 项；Program/Build 路由不变 |
| 六组件 Release | 0 警告、0 错误 |
| 解决方案 | 0 错误，测试 fixture 的既有 56 个警告另列 |
| writer | 36/36，含本次 5 项世界坐标检查 |
| 新增 5 项 | 实际读回与旧快照不同及 owner 关联；世界坐标漂移；非有限值；恢复后拒绝读取；getter 回调取消 |
| 相邻 Judgements | provider6、chart5、endpoint6、numeric13、RingX7、source13/14/17/18/19、basis1 |
| Runtime ABI | 51/51 |
| 参数错误 | 两项实际 exit2，stdout 空、stderr 为精确 usage |
| 合计 | 53 个自然退出子步骤，130 项正向检查（79 Judgements、51 Runtime ABI） |
| 稳定性 | 源码、控制、六生产/两测试 DLL/PDB pair、12 依赖、3 native fixture 与实际数值 golden 均核对 |

本次是完整的新主图执行，控制进程 31476 的原 Windows 进程句柄由 observer 14856 保持到实际 exit0。没有复用上批中断执行步骤。隔离候选的 5/5 只保留为上游证据，没有用于代替主图执行。

旧 300 项仅核对清单与顺序；本批没有重新执行旧 300、完整 source19、完整 basis16 或默认 408 项单进程。130 是本批专项数量，不代表底层整体完成比例。

## Evidence → Finding → Path

范围是用户授权的本地离线底层开发，flavor 为 null。所有验证使用本批新产物和离线 fake-native 路径，不涉及真实游戏 UI、账号或永久环境。

| Evidence | 文件 | 观察 |
| --- | --- | --- |
| E-001 | [验收摘要](validation/line-ring-world-read-20261113/validation-summary.json) | 892 输入、53 步骤、130 检查和范围边界 |
| E-002 | [原句柄退出记录](validation/line-ring-world-read-20261113/run/controller-handle-exit.json) | 实际 exit0，控制脚本与结果 SHA 一致 |
| E-003 | [本批执行记录](validation/line-ring-world-read-20261113/run/result.json) | 每步 argv、退出、RUN/PASS、日志及产物校验值 |
| E-004 | [源码审查](validation/line-ring-world-read-20261113/reviews/00-static-review.md) | 原 reader、guard、tuple 对比与历史观测边界 |
| E-005 | [归档清单](validation/line-ring-world-read-20261113/artifact-manifest.json) | 1095 个文件含清单本身，33,902,549 bytes |

F-001（validated，E-001/E-002/E-003）：实际 post-write `get_position` 读取已进入当前主源码，五条新增真实后端 fake-native 回归及相邻专项通过。

F-002（validated，E-003/E-004）：重复完整观测可以拒绝已观察到的坐标变化、非有限值及取消后的继续读取，结果保留准确的原来源与 owner。

F-003（open，E-001/E-004）：有限观测不是原子快照。camera/UI/scene 的正式组合来源、canonical Ring seed、scratch/renderer、Hold engine、DSP schedule 与完整时钟事务仍需完成，完整 Speed/Seek 保持关闭。

P-001（callflow）：原 Canvas basis → 独立 LineRing source → parent/local writer → 完整当前状态检查 → 原始 reader 的 rooted `get_position` → 两轮 tuple 与后检 → 自有历史观测。此路径支持 F-001/F-002，保留 F-003。

## 独立复核与续接

本批结果 SHA256：`5563eb5458409ccc41938a4785ef4f9b5a8fd17f280c1d7d06ab227a5c8c1ae9`。归档摘要 SHA256：`ce8b17aa85be2ad8923c9792abfb375c38389ee34ee2b8c17fe2c5f6126cdc9b`。

归档带有源码、控制、原始日志、生产/测试/依赖、native fixture、数值 golden 和审查记录，可独立检查：

```powershell
python -B docs/validation/line-ring-world-read-20261113/publisher/publish.py --check-only --archive docs/validation/line-ring-world-read-20261113
```

下一候选在 `work/line-camera-scene-next-20261113/`。从 [.docs/CURRENT-CHECKPOINT.md](../.docs/CURRENT-CHECKPOINT.md) 读取最新状态，以实际构建和执行结果决定接入；不要用本批或旧 ZIP 验收它。
