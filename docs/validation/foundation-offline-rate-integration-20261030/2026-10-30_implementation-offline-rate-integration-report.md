# 正式速率接线与离线全树整合

2026-10-30，`flavor=null`。本批完成了正式 backend 对速率 provider 的采用，补齐现有 pitch/时钟事务到正式控制服务之间的接线。准备失败时，三个独立资源的清理现在都能尝试，并保留原准备异常。独立 Single-return 原型修复了 clip 掩盖转换溢出与 Candidate 算术缺少 SEH 隔离的问题。Play 新增了真实 operation/source 生命周期到私有 InstantWork transfer 的最小自动联合。

当前源码已统一冻结、构建并通过 27 套 2436 项托管检查、六组件 Release、解决方案和两个独立 SDK 插件验证。相机接入、既有退休实现及正式 V7/83 exports 未重做。**没有真实生产 provider，默认原生 Speed/Seek 仍关闭；完整 B01–B19 未验收。** 未安装、启动或操作真实游戏，未操作账号。授权和边界见 [scope.md](scope.md)。

## 实现及实际行为

- `GameplayModule` 新增内部可选 provider 装配口并透传到真正的 `GameplayNativeBackend`；当前默认 `RizlineAdapter` 不传 provider。Module 透传已由本批全树与六组件构建验证，不使用隔离旧 baseline 的编译结论代签。
- SetSpeed 同时要求 backend 元数据完整和 provider 可用；只开放 `PlaybackRateMultiplier`。正式服务每次读取当前能力，能反映 provider 撤回。准备时交付实际 rooted controller/information/progress/pause 图；每次读写、确认及恢复前后重新验证图、绑定、同一 clock 和 source authority。
- 实际 Backend → NativeRuntime 受控 exports/字段/`runtime_invoke` setter → 正式 GameplayService/PluginHost → 原有 pitch/clock participants 的 15 个新增场景通过。这里执行的是受控 native API 实现，没有调用真实 Unity 音频或完整原生 provider。
- 独立返回原型按 `finite double → 实际 float cast 有限 → clip` 判定；略高于精确 float 最大值但可舍入为有限 float 的正向边界也通过。Candidate 真正的 SSE 算术溢出由独立 SEH 捕获，原始返回 bits、一次 original 调用及 Unknown 责任保持。具体源码/机器证明见 [专项报告](../single-return-native-repair-20261030/README.md)。本 helper 仍为 D 盘原型，没有加入正式发行。
- Play 最小自动联合去掉 inner Play 的人工 Begin/End，真实 Void0/25 的 Before/Returned 自动进入原账本，source active 为 `1/1/1 → 0`。source site rows 为 0，外层 parent、metadata、admission/death 仍受控；未证明非空 Add/queue、全部 source 站点或最终释放。见 [实际边界](../foundation-single-play-joined-20261030/AUTOMATIC-INSTANT.md)。

## 当前冻结验证

所有本批主构建使用 `D:/riz-build-cache-next/foundation-offline-rate-*-r1-20261030/` 的新目录，完整路径与 hash 在 [evidence-index.json](evidence-index.json)。

| 范围 | 实际结果 | 说明 |
|---|---|---|
| core 当前源码捕获 | Audio 1001/1001、coordinator 28/28、receiver 10/10，exit 0 | 680 条 live 输入；后续编译只读独立快照 |
| full 当前托管矩阵 | 27 套 2436/2436，各 build/test exit 0 | 700 个冻结文件；Gameplay 146、相机 33、SingleEvent 65 |
| 六组件 Release | 6 DLL，build exit 0 | 1 条既有生成 profile 的 CS8669，0 error；沿用已冻结 transport profile |
| 冻结解决方案 | build exit 0，0 error/0 warning | 在已编译的 full 树上完成增量 solution build；不抹掉各 suite 之前的 fixture warning |
| SDK 分发 | 两个实际生成/独立编译插件，正式 loader/host 14/14，exit 0 | 绑定同一 full proof；模板安装在隔离 hive |
| N2 采用专项 | 146/146，build/test 0/0 | 隔离 baseline + 13 overlay；不包含本批 Module 透传编译 |
| Single-return 专项 | 同一 r4 helper：3/25 + 12/119 + 4/15 = 19 场景/159 检查 | 95 个归档文件；实际 PE/PDB/COFF 与编译源码证明 |
| 自动 Play 专项 | 1 个独立进程，native/managed/test exit 0 | 660 个冻结源码；site rows 0，不与托管检查数相加 |

SDK 发行前置和 `Test-FoundationSnapshot.ps1` 已同步为包含 GameCamera 的 27 套清单；full proof 同时冻结 `.sln`。新增 `Test-FoundationSolutionSnapshot.ps1` 从该 full proof 构建解决方案，并检查输入 hash、实际退出码和编译摘要。主验证结束后，2095 条 hash 再核一致；摘要见 [test-result.json](test-result.json)。原有 Play 7 进程结果只按源码一致性沿用，未重跑，未算作本轮新增运行数。

## Evidence → Finding → Path

以下 Evidence 的精确 `source_ref`、文件大小和 `content_hash` 均在索引；读取原件可用 `Get-Content -LiteralPath <source_ref>`，完整性复核用 `Get-FileHash -LiteralPath <source_ref> -Algorithm SHA256`。本报告只声明离线受控行为。

| Evidence | 原件 |
|---|---|
| E01 | `gameplay-rate-provider-r2-20261030/test-result.json`、该专项 index/source archive |
| E02 | `single-return-freeze-r2-20261030/freeze-manifest.json`、专项 index 和 PE/PDB/source proofs |
| E03 | 当前 core `test-result.json` 与 `source-snapshot.zip` |
| E04 | 当前 full `test-result.json`、`source-before.json`、各 suite 原始 build/test log |
| E05 | 当前 release `managed-build-manifest.json`、六个实际 DLL |
| E06 | 当前 solution `test-result.json`、`solution-build.log` |
| E07 | 当前 SDK `test-result.json`、`sdk-distribution.log` 与实际双包 result |
| E08 | `single-play-automatic-frozen-r4-20261030/test-result.json` 和 AUTOMATIC-INSTANT 报告 |

| Finding | severity / confidence / status | evidence_ids / location |
|---|---|---|
| F01：正式 backend 已能消费有来源凭据的速率事务，并区分恢复与未知副作用 | n/a_re / high / validated-controlled | E01/E03/E04/E05；`GameplayNativeBackend`、`NativePlaybackRateProviderAdoption`、`GameplayService` |
| F02：native 返回计算的溢出/SEH 与托管边界一致，失败不冒签同步成功 | n/a_re / high / validated-controlled | E02；D-only `SingleReturnContext.cpp::Candidate/SafeCandidate` |
| F03：本批源码、Module 装配和 SDK 可在同一冻结输入下构建/回归 | n/a_re / high / validated-offline | E03/E04/E05/E06/E07；三个当前 snapshot scripts |
| F04：真实 Play/source 生命周期已自动到达私有 InstantWork transfer，非空工作仍缺 | n/a_re / high / validated-limited | E08；D-only automatic fixture |
| F05：真实速度、Seek 与整体底层仍未完成 | n/a_re / high / validated-scope-limit | E01/E02/E04/E08；默认 provider=null、生产准入关闭及 scope |

P01（`path_type=callflow`）：当前 SDK request/lease/run → 真正 backend 的 rooted capture → provider 完整绑定核验 → actual 受控 pitch setter/readback → clock participant → 正式 GameplayService 确认/audit；失败逆序恢复并独立读回，失去来源时保留 Unknown。关联 E01/E03/E04、F01。

P02（`path_type=callflow`）：同一 core source archive → full 27 套 → 同 core 的六组件与同 full 的 solution/SDK → hash 再核与证据索引。关联 E03–E07、F03。独立原型 E02/E08 有自己的源码包，不冒充当前正式 native 生产候选。

## 复现和下一步

依次运行下列现有脚本，目录参数使用未存在的新绝对目录。它们只运行自有 fixtures 或托管测试，不安装/启动游戏。

```powershell
$env:PATH = 'C:/Users/71957/.cache/opencode/bin;C:/Program Files/dotnet;' + $env:PATH
scripts/Test-ActiveRetirementSnapshot.ps1 -OutputDirectory D:/riz-build-cache-next/offline-rate-replay-core
scripts/Test-FoundationSnapshot.ps1 -SourceSnapshotManifest D:/riz-build-cache-next/offline-rate-replay-core/test-result.json -OutputDirectory D:/riz-build-cache-next/offline-rate-replay-full
scripts/Build-EngineTransportManagedCandidate.ps1 -SourceSnapshotManifest D:/riz-build-cache-next/offline-rate-replay-core/test-result.json -NativeBuildDirectory D:/riz-build-cache-next/engine-transport-production-native-r2-20261027 -OutputDirectory D:/riz-build-cache-next/offline-rate-replay-release
scripts/Test-FoundationSolutionSnapshot.ps1 -FoundationDirectory D:/riz-build-cache-next/offline-rate-replay-full -OutputDirectory D:/riz-build-cache-next/offline-rate-replay-solution
scripts/Test-SdkDistributionSnapshot.ps1 -FoundationDirectory D:/riz-build-cache-next/offline-rate-replay-full -OutputDirectory D:/riz-build-cache-next/offline-rate-replay-sdk
```

需要 PowerShell 7.2+、本机 .NET SDK 和脚本声明的已冻结 native/profile 依赖。若当前工作树之后变化，这些命令验证的是后续新源码；复核本报告请使用索引绑定的 source archive 和原始 logs。

下一 N2 离线功能是 broker 可证明的 GetTime detour/原跳板 profile、stock Pause/Replay/Resume occurrence 与 DSP 调度事实、连续 pitch writer/source 凭据及正式 provider。不能跳过原正文校验，也不能把 provider Probe 当单局准入。Play 下一步为 actual outer Update/Execute、非空 source site/reference、actual Add/queue 联合。任意 Seek 还缺音符、判定、Hold/协程、输入与结果状态的完整重建/恢复。完整实机验收继续后置。

Timeline：核对 checkpoint/冻结原件 → 三路 Sol/max 实现与审查 → N2 146/146 与返回桥 r4 19/159 → 自动 Play r4 → 当前 core/full/release/solution/SDK → hash 再核 → 报告与 checkpoint。失败和中间版目录原样保留，未 reset/commit/push。
