# Rizline LOCAL 首次转场分析报告

2026-10-23，flavor=null，普通 native PE64 逆向。**静态路径已确认；本批没有游戏动态验收。** controller Awake 只负责持久化和全局回调，不能代替加载新的关卡场景。原版菜单在参数 setter 后请求异步转场，因此 LOCAL 完整资源图可以复用转场机制而不重新进入 stock 资源加载协程。转场正常返回还不足以证明新场景就绪；真实 AsyncOperation、目标场景与 manager 清理状态必须共同成立。

## 范围、样本与方法

授权及阶段边界见 [scope.md](scope.md)。样本是 `D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll`，SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。继承 [前序分诊](../local-launch-20261022/triage.json)的 PE machine=8664、CLR size=0、导入/导出分类；静态导入不排除动态加载行为。本次只新增七个明确边界，不执行样本。

工具索引显示 IDA/r2 不可用，沿用本机 `C:/Python314/python.exe` 与已安装 pefile/capstone/UnityPy。版本记录在 evidence.json，不安装新依赖。

## Evidence

表中复现命令 `R` 指仓库根目录执行 `C:/Python314/python.exe docs/validation/local-launch-scene-transition-20261023/inspect-scene-transition.py`。

| E-id | source_ref / artifact_path | repro_command | content_hash |
|---|---|---|---|
| E-scene | [evidence.json](evidence.json) | R | `1335b8e4b93a34c7cb7d26d17f7316028b4776c5abbd72cc09d8c79cfb57d585` |
| E-native | [disassembly.txt](disassembly.txt) | R | `0cd173de233312c9e4774cfa3ac34eae834420da8b595f94b0f81e2970238d44` |
| E-types | [metadata-excerpts.txt](metadata-excerpts.txt) | R | `6df19634c5e0ea56ed0ec8df3b702eae21a5d9fc3a11795f1c3770299159bbb6` |
| E-triage | [前序 triage.json](../local-launch-20261022/triage.json) | 复用固定样本前序分诊，原入口见前序 README | n/a：继承文件引用，样本哈希与本次相同 |

## Findings

| F-id | severity | evidence_ids | confidence | location | status / 结论 |
|---|---|---|---|---|---|
| F-request | n/a_re | E-native,E-types | high | `3C73B3→3C749C`, `402170` | confirmed-static：setter 后调用精确实例 StartNextScene；时间3.0、predicate=null |
| F-complete | n/a_re | E-native | high | `4021AC`, `404228`, `40448A`, `40412E..40413D` | confirmed-static：busy async 可使方法无操作返回；加载/激活/清理是后续异步过程 |
| F-balance | n/a_re | E-native,E-types | high | `3C7411/3C7455/3C755D/3C7659` | confirmed-static：原版另有相机/粒子停用、TouchBlockRelease 延迟、cut-in 等待回调；不能省略其所有权分析 |
| F-literal | n/a_re | E-scene | medium | slot `1E79DF0`、BuildSettings index6 | candidate：构建场景为 Level，但恢复 metadata 的目标 literal 条目越界，未独立解码目标字面量 |

精确指令及实现约束另见 [README](README.md)；没有把生成 C 签名中遗漏的 this 当作静态调用依据。

## Path

P-launch，path_type=callflow：已持有完整 LOCAL bundle/精确 setter receipt → 激活前捕获 current manager 与 origin scene（E-types）→ 受控 Awake 后请求一次 `StartNextScene`（E-native/F-request）→ 保留实际新 async operation → 观察 operation.isDone、新 loaded Level handle、manager async清零且lock恢复（E-native/F-complete）→ 核验持久 clone、Awake/globals 与实际初始化图 → 才允许共享 commit。最后两步中的完整初始化证据由已有初始化保护桥负责，转场模块不伪造。

## 动态状态与遗留问题

没有本次 native 调用、Hook 日志或 UI 操作。控制 seam 回归由父任务统一构建，具体计数以其对应冻结构建包为准。`NativeLocalRunSceneTransition` 与 post-scene activation reader 已交给父任务接线；它们不构成可玩启动入口、输入锁平衡或消费者结束证明。

下一工作是条件交接/恢复相机和粒子、明确 TouchBlock/BackLock 与 cut-in 回调、活动局退休、返回/重试，然后依用户恢复实测安排验证 native 时序。literal 文本缺证也应在后续独立补齐；不能从本报告消除该限制。

关键时间线见 [scope.md](scope.md)。整个 B01–B19 任务仍未因此完成。
