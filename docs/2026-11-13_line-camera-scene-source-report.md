# 相机、UI 与场景来源：本批离线验收

相机和场景真实字段读取已合入主树，并在新构建上通过 149 项专项检查。端点写入后的世界坐标，可以与同一原来源的相机、UI 和场景观测组合；组合接口只接受原 writer 最近一次实际发行的 world record，复制或伪造的 record 会被拒绝。

完整 SDK、B01–B19 和完整 Speed/Seek 仍未完成。本轮没有安装或启动游戏。既有 881 输入预览 ZIP 保持原字节，不包含本批新代码。

## 实际行为

读取沿用原 retained LineRingReader 和原 LevelInformation，没有另建捕获、Hold 实例或全局扫描。LevelInformation.sceneWidth、LineControl 缓存 width/scale、CameraControl.xPosition/scale、UI gameSceneCanvas.localScale 分别保存，不能因值偶尔相等就互相替代。

读取核对两轮完整 Single 位值，并在前后验证原来源。首次成功的场景读取固定实际 UI、Camera 和 Canvas 引用，后续换对象会使原来源失效。瞬时非有限值或可变标量漂移会拒绝本次观测；组合入口另外校验原 writer、source 和最新发行对象的引用身份。

结果属于自有只读历史数据，`AtomicSnapshot=false`、`DeterministicTarget=false`、`CompleteSeekPermitted=false`。顺序读取一致，不等于原子快照、目标时刻相机重建或后续写入许可。

## 验证结果

| 项目 | 本批结果 |
| --- | --- |
| 主树 | 10 个 C# 文件、3 个新增输入；892→895；Build.ps1 source 范围 19→24 |
| 六组件 Release | 0 警告、0 错误 |
| 解决方案 | 0 错误；测试 fixture 的既有警告单独保留在原日志 |
| endpoint writer | 36/36 |
| 来源读取 | 完整 24/24，含本次 5 项场景检查 |
| 相邻模块 | provider6、chart5、endpoint6、numeric13、RingX7、basis1 |
| Runtime ABI | 51/51 |
| 非法 selector | 两项实际 exit2，stdout 空、stderr 为精确 usage |
| 合计 | 72 个自然退出子步骤、149 项正向检查（98 Judgements、51 Runtime ABI） |
| 归档 | 1181 个文件、36,899,255 bytes，含源码、控制、原日志、产物、依赖、fixture 和审查 |

controller44940 的原 Windows 进程句柄由 observer19556 保持到实际 exit0。所有 895 输入、六生产与两测试 DLL/PDB pair、12 依赖、3 native fixture 和实际数值 golden 均按本批字节复核。隔离 shadow 的 5/5 只作上游候选证据，没有替代主图执行。

旧 300 项只核清单与顺序；本批没有执行旧 300、完整 basis16 或默认 413 项的单进程测试。149 是本批专项数量，不表示整体完成比例。

## Evidence → Finding → Path

范围见 [scope.md](../work/line-camera-scene-source-case-20261113/scope.md)，时间线见 [timeline.md](../work/line-camera-scene-source-case-20261113/timeline.md)。这是用户授权的本地离线 SDK 开发，flavor 为 null；只复用既有冻结文本，没有重新打开或执行游戏二进制。

| Evidence | source_ref | content_hash SHA256 | 复核方式 |
| --- | --- | --- | --- |
| E-001 | [本批摘要](validation/line-camera-scene-source-20261113/validation-summary.json) | `98e554f44d1ae84f30368e1c13162e2c45a1a9a758cb666e621612bd9e7c4bef` | 运行下方 archive-only 命令 |
| E-002 | [执行记录](validation/line-camera-scene-source-20261113/run/result.json) | `3dfaaf45458db2a76bea37d541d9db0d6ce2cf6fe3c55728eca38ec654857ff0` | 同命令逐项核对 argv、原日志、自然退出和产物 |
| E-003 | [原句柄记录](validation/line-camera-scene-source-20261113/run/controller-handle-exit.json) | `53d673d7e19a91b2503161bb676e18dbdf1a73bb70b3a99742e1657629adf542` | 同命令核对原 PID、脚本、结果与实际 exit0 |
| E-004 | [源码审查](validation/line-camera-scene-source-20261113/reviews/01-review.md) | `3c4362bdef59b5e3c5b7ef51d4328df8d2e25e307ac1b02176334790bcc082ca` | 同命令核对候选与主输入；阅读来源和发行身份检查 |
| E-005 | [归档控制审查](validation/line-camera-scene-source-20261113/reviews/03-review.md) | `720790cbdca8505ff625b3f61cdb28dd60f021e45d68ffc7c487356501f19b08` | 同命令核对 publisher、显式 root pin 和清单 |

F-001：status=validated，severity=n/a_re，confidence=high，evidence_ids=E-001/E-002/E-004。位置为 `NativeSeekLineRingSourceCapture.Scene.cs` 与正式后端场景 reader：原相机、UI、场景来源已正式接入，新增五项真实后端 fake-native 检查通过。

F-002：status=validated，severity=n/a_re，confidence=high，evidence_ids=E-002/E-003/E-004。位置为 `NativeSeekLineEndpointCommands.RequireIssuedWorldObservation`：组合读取拒绝值相同但并非原 writer 最新发行对象的记录，保留原 source/owner。

F-003：status=candidate，severity=n/a_re，confidence=high，evidence_ids=E-001/E-004。位置为后继动态 consumer、renderer 和完整 Seek：本批提供顺序历史观测，仍缺动态输入实际消费、renderer、Ring 初始化、Hold 引擎、实际 DSP schedule 和完整时钟事务的整体闭合。

P-001，path_type=callflow：原 Canvas basis → 独立 retained LineRing source → parent/local writer → 实际 world getter 与最新 record 发行 → 同一 reader 的场景观测 → 原发行对象与来源复核 → 自有历史结果。此路径支撑 F-001/F-002，未关闭 F-003。

## 独立复核与下一步

归档内检查器只读取归档，不依赖当前主树、候选目录或 D 盘原运行目录：

```powershell
python -B docs/validation/line-camera-scene-source-20261113/publisher/publish.py --check-only --archive docs/validation/line-camera-scene-source-20261113
```

下一候选位于 `work/line-dynamic-consumer-next-20261113/`，v3 修正了 raw floor index 与物理 Canvas 槽位混用，以及 LineControl 缓存 scale 与 UI scale 混用。候选尚未合主；必须用其新构建和实际用例验收。最新运行状态以 [.docs/CURRENT-CHECKPOINT.md](../.docs/CURRENT-CHECKPOINT.md) 顶部为准。
