# 本地内容、设置订阅与 listener 效果接线

2026-10-27。按用户最新要求，本批先补正式功能与必要注释，性能优化后置；项目尚未发布，合同按当前实现推进，不以保留旧接口为目标。**最新核心快照 r2 实际通过 Audio/content 844、协调器 28、receiver 10 项，包含 Retry 当前来源重捕获。此前 core-r1 的七套专项实际 215/215 通过，范围保留为其原快照。** 本页按各批实际终态分别记录，不把旧完整矩阵或进行中的发布构建记为本次通过。

生产游戏 gate 仍为 false；本批没有安装或运行真实游戏。原生生产首次执行窗口的缺项以[独立准入审计](../foundation-engine-transport-production-admission-20261027/README.md)为准，现有受控回归不产生生产权限。完整候选 package-r2 没有自动包含本批后续源码；新的六托管组件 release 构建尚在进行，未记为成功或已产包。

## 功能实际调用点

| 功能 | 已落地的正式接线 | 本批声明边界 |
|---|---|---|
| 本地曲目 RequestPlay | [LocalCatalogService](../../../src/RizToolbox.Adapter/AudioContent/LocalCatalogService.cs)由当前 selection 准备精确 revision 的借用请求，经 [LocalRunProtectionModule](../../../src/RizToolbox.Adapter/AudioContent/LocalRunProtectionModule.cs)转交 [NativeLocalAudioContentRuntime](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalAudioContentRuntime.cs)；[AudioContentModules](../../../src/RizToolbox.Adapter/AudioContent/AudioContentModules.cs)传入同一个正式 protection owner | 不重新按 ID 查找同名新曲目；缺 metadata、取消、过期 selection 和不可用 composition 分别拒绝。成功表示接受异步事务，不伪造 Ready/RunStarted |
| 作者填写的播放 metadata | [LocalChartPlaybackMetadataValidation](../../../src/RizToolbox.Adapter/AudioContent/LocalChartPlaybackMetadataValidation.cs)复制并校验 difficulty 0/1/2、MaxHit/MaxScore、难度/署名/颜色；核对精确 chart/resource revision | 内容数据不授启动权限；计分上限是作者声明，不冒充推导公式 |
| 原生启动来源 | [NativeLocalPlayLaunchSource](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalPlayLaunchSource.cs)在 native 读取前保留责任；[实际 backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalPlayLaunchSourceBackend.cs)核 GameManager.Instance、inLevelControl、JudgeDataList 的选定项、列表版本和字段稳定性 | 四根逐份出版与单次释放；失败释放 Unknown 不重试。受控 backend 回归与实际游戏来源观测分开 |
| Result、Return 与 Retry | 正式 runtime 连接 coordinator、共享 host UI、result sink 和 typed commands；完整旧 launch 完成 receipt 后重新捕获当前 manager/control 与同 difficulty 的 judge 来源，才移交新事务 | 返回/重试使用同一实际缓存结果和 session，不捏造 ResultScene receiver。源暂未就绪时已知清理后待后帧；部分 root/free 失败保持 Unknown 不重试。r2 六组受控 Retry 回归已通过，不等于实际 Unity 菜单重建或生产正向运行 |
| 游戏设置变化订阅 | 新可选 [IGameSettingsChangeService](../../../src/RizToolbox.Sdk/GameSettingsContracts.cs)与 `riz.settings.changes`，[SettingsModule](../../../src/RizToolbox.Adapter/Settings/SettingsModule.cs)实际注册，[Changes 实现](../../../src/RizToolbox.Adapter/Settings/GameSettingsService.Changes.cs)在正式 Refresh 完整捕获后差分发布 | 一秒采样，初始完整快照与后续修订有序；不是每次 setter 回调。复制只读 DTO，逐回调核 activation、capability generation 和私有 subject；ScopedEvents 归属异常。旧读取接口三成员不变 |
| UnityEvent listener 效果 | [Effects ledger](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.Effects.cs)保留每次实际 Invoke generation、inline list、dispatch、native After、context Dispose、physical exit 和 membership mutation | 只有完整观察到的空执行列表可签无 listener 效果；先前非空、opaque/multicast、foreign child、缺 inline、worker 或异常保持未完成/Unknown。Remove、原函数返回和最后一次空列表不能抹去旧效果；future membership writers 仍独立未关闭 |

## 已结束的核心快照 r1

E-001：[实际父结果](D:/riz-build-cache-next/foundation-function-core-r1-20261027/test-result.json)，421,090 字节，本页只读复算 SHA256：

`1a958ab7189fca1ecd4e2b40c1a2c901278ca6c0762893d73c949ca104636c3a`

| 字段 | 实际值 |
|---|---|
| 顶层 / runner | `passed=true`，`runnerExitCode=0` |
| 输入 / 快照 / 排除草稿 | 516 / 516 / 0 |
| Audio/content | 831/831 |
| 协调器 | 28/28 |
| receiver | 10/10 |
| 子输入前后哈希 | `sourceHashesUnchanged=true` |
| 游戏 / 安装 / 生产准入 | `gameExecuted=false`、`installed=false`、`productionAdmissionVerified=false` |

E-001 记录的 [source-snapshot.zip](D:/riz-build-cache-next/foundation-function-core-r1-20261027/source-snapshot.zip) SHA 为 `5335e541a9f35711075a31764645db14f0d863adbf3c0f1441b8d273733d1a71`；其内部 runner 的 `managed-project-sources.zip` 另为 `8111ef5704582bebf682816a44cec4c154234e9faf02cca467797e3f109fa2bd`、516 项。这些是原结果中的归档指纹，本页复算的是父结果文件，未重建或重跑 runner。

复核命令仅针对已有文件：`Get-FileHash -LiteralPath 'D:/riz-build-cache-next/foundation-function-core-r1-20261027/test-result.json' -Algorithm SHA256`。

## 随后完成的七套专项 managed-r1

E-002：[七套专项实际父结果](D:/riz-build-cache-next/foundation-function-managed-r1-20261027/test-result.json)，158,964 字节，本页重新计算 SHA256 为 `5f5c9f7a3aed67da8f51f52fac196bca11c244a59eb88bd0653783e14ed382a5`。root 确认 session 12477 实际终态退出 0；结果 `passed=true`，七个项目各自的 `buildExitCode` 和 `testExitCode` 均为 0。

| 专项 | 实际检查 |
|---|---:|
| Adapter | 10/10 |
| Hosting | 20/20 |
| SDK | 13/13 |
| Module | 26/26 |
| Data | 24/24 |
| Settings | 43/43 |
| UIInput | 79/79 |
| 合计 | 215/215，7 套 |

其 `sourceProof` 明确引用 E-001 及相同 SHA `1a958ab7…636c3a`，因此范围仍是 core-r1 快照，**不包含之后的新 Retry 来源修复**。`fullManagedMatrix=false`、`distributionSuiteRun=false`；此前 1800 项完整矩阵不属于本次新结果。`gameExecuted=false`、`installed=false`、`productionAdmissionVerified=false` 保持。

Settings 的 43 项包含原 25 组和[本次新增 18 组](../../../tests/RizToolbox.Settings.Tests/SettingsChangeRegression.cs)：实际模块采样/完整修订、不可变复制、订阅中注册、初始 Own 重入/停用/直接释放、generation 替换、异常归属、失败捕获原子性、线程拒绝及实际 override/恢复通知。本次是受控 host/backend 回归，没有声称已观察真实游戏设置变化。

复核 E-002 可执行：`Get-FileHash -LiteralPath 'D:/riz-build-cache-next/foundation-function-managed-r1-20261027/test-result.json' -Algorithm SHA256`。

## 最新核心快照 r2：Retry 与来源守卫

E-003：[r2 实际父结果](D:/riz-build-cache-next/foundation-function-core-r2-20261027/test-result.json)，422,785 字节，本页实际复算 SHA256 为 `eebade7a120829c0411c44b314d4ff93265097c1f6f85236e78e18bb52699249`。顶层 `passed=true`、`runnerExitCode=0`，518 项输入/518 项快照、排除草稿 0；子记录 `sourceHashesUnchanged=true`。

| r2 验证 | 实际结果 |
|---|---|
| Audio/content | 844/844 |
| 协调器 / receiver | 28/28、10/10 |
| Runtime 构建诊断 | 0 警告、0 错误 |
| AudioContent 构建诊断 | 18 警告、0 错误；保留完整日志，不称零警告 |
| 游戏 / 安装 / 生产准入 | 均为 false |

较 r1 增加的 13 组为：6 组 [Retry 来源事务](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalPlayRetrySourceRefreshRegression.cs)、3 组实际来源 publication wrapper、4 组 listener writer-source 守卫。Retry 覆盖重建后的当前来源、已知 NotReady 后待帧、重入/worker、部分 fork、free 失败和错目标/变化来源；新增 [Retry helper](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalPlayRetrySourceRefresh.cs)在正式 runtime 中被调用，不放宽原 globals 检查。writer-source 将过去空调用效果与未来来源关闭分开，未关闭的来源保持 Pending。

E-003 记录的 source-snapshot.zip SHA 为 `1a56c344b8b56c54cd98518e939cca2c3ade7c13b0a3857d3ba8ca4974200de9`，内部 managed-project-sources.zip 为 `4ba62f2f5bf697fa61edc861ea1edc922d6594b1e63b4229cb9dae661ad4ea36`、518 项。复核父文件：`Get-FileHash -LiteralPath 'D:/riz-build-cache-next/foundation-function-core-r2-20261027/test-result.json' -Algorithm SHA256`。本页没有重跑构建或原生程序；结果证明受控功能行为，不证明实际 Unity/native production 正向链、真实整局或性能。

## 范围与追溯

本页使用 docs-generator，flavor=null；承接[只读研究与本地实现范围](../foundation-engine-binding-20261025/scope.md)。root 是唯一 builder，本页编写者只读源码和已有结果、计算文件 SHA，并新增此文档；没有执行 PE、操作 UI 或游戏，也没有更改性能门槛和生产 gate。

**F-001**：`severity=n/a_re`、`category=design`、`status=validated`、`confidence=high`；E-001 与上表正式调用点支持“本地内容/结果命令及效果账本已接入生产源码并通过指定核心快照的受控回归”。E-002 支持 core-r1 七套专项 215 项通过，包括新增设置订阅；E-003 独立覆盖后续 Retry 来源与 writer-source 修复。三者均不重记旧 1800 项完整矩阵，也不证明真实游戏行为。

**P-001**：`path_type=callflow`；精确 catalog selection → 借用 prepared request → 正式 protection composition → metadata/source 核验 → coordinator 异步事务 → 同 session 缓存结果 → typed Return/Retry → all-owner 完成 receipt。任何拒绝保留实际责任；设置变化则由正式模块 Refresh → 完整捕获 → 私有 subject/generation 校验 → 插件 ScopedEvents 回调。

时间链保留：本文初稿时 core-r1 已结束、七套专项仍在运行；随后追加 E-002，再以 E-003 记录新 Retry 所在 r2 的实际终态。性能与真实游戏验收按用户后续安排进行；release 构建与新包未在本页提前记为成功。
