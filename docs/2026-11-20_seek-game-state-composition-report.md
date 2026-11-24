# Seek 五参与者完整状态组合与恢复报告

本批修复了 `N2` 离线五参与者（`HoldEpoch → Audio → 四个 unmanaged 进度缓存 → GameState/判定/可见性/渲染重建 → Clock`）完整组合与逆序恢复中的三类阻断，并在同一最终源码快照上完成全部 7 项组合场景、相邻 Hold/Canvas/Publication 检查及离线封存（`seal-r1`）。

- **可见性数组类型与元素采用**：`NativeJudgementBackend.NoteVisibility` 新增 `ReadVisibilityBasisArray`，按实际 `TransformClass` / `EaseInfoClass` 调用 `ReadRootedCanvasSourceArray` 并逐项采用并核验元素精确类型，修复原先误用 `ReadRendererArray` 导致的基线读取拒绝。
- **Canvas 缓存提交失败后的可确认恢复**：`NativeSeekCanvasBasisCommands.Ownership` 改为检查 `_restorationFailure`（而非历史 `_failure`），使已完整逆序恢复并通过独立读回的 Canvas 缓存失败可以确认恢复所有权；最初异常及原始位置检查仍保持记录与强制执行。
- **异常目标长条退出后拦截外层时间逆写**：`NativePlaybackSeekParticipants`、`NativeSeekGameStatePreparation`、`NativeSeekProgressParticipant` 与 `NativeJudgementBackend.SeekHoldEpoch` 新增 `VerifyTimeRestoration` / `VerifyTargetStoppedForTimeRecovery`，在目标长条启动异常且未完成已知停止（或存在未知生命周期状态）时，阻止 Audio 与 Progress 逆序回写时间，保留原 Clock 不变、不重启原长条，并完整保留未知原生根责任（case 7 保留 362 根）。

## 执行命令、时间与检查统计

三组串行执行（均使用 `pwsh` 7.6.9、`rg` 13.0.0 与同一最终 1802 项源码快照）及离线封存均自然 `exit 0`：

| 批次 | 命令 | UTC 起止时间 | 耗时 (s) | 结果 |
| --- | --- | --- | ---: | --- |
| `current-r1` | `Run-CompositionChecks.ps1 -RunName 'current-r1' -Cases @(5,6,7) -FullBuild -ChildTimeoutSeconds 1800` | `2026-11-20T04:28:35.165Z` – `04:46:08.523Z` | 1053.36 | `passed=true`, `inputsMatched=true`, 4 child / 3 案例 |
| `current-r2` | `Run-CompositionChecks.ps1 -RunName 'current-r2' -Cases @(1,2,3,4) -AdjacentChecks -ChildTimeoutSeconds 1800` | `2026-11-20T05:00:45.202Z` – `05:12:19.228Z` | 694.03 | `passed=true`, `inputsMatched=true`, 9 child / 151 案例 |
| `canvas-adjacent-r1` | `Run-CompositionChecks.ps1 -RunName 'canvas-adjacent-r1' -Cases @() -CanvasCases @(7,8,9,10,17,18) -ChildTimeoutSeconds 1800` | `2026-11-20T05:12:44.420Z` – `05:46:06.767Z` | 2002.35 | `passed=true`, `inputsMatched=true`, 7 child / 6 案例 |
| `seal-r1` | `Seal-Composition.ps1 -RunDirectories @('current-r1','current-r2','canvas-adjacent-r1') -SealName 'seal-r1'` | `sealedUtc=2026-11-20T05:46:35.403Z` | — | `passed=true`, 7 唯一组合案例 / 160 托管检查 |

### 分项子进程明细（20 个子进程，合计 160/160 托管检查）

| 批次 / 子进程 | 范围 / 参数 | 退出码 | 耗时 (s) | 通过 / 汇总 |
| --- | --- | ---: | ---: | --- |
| `current-r1 / build-current` | `RizToolbox.sln` (`Release`, `--no-restore -m:1`) | 0 | 134.77 | 0 errors, 55 warnings（既有测试警告） |
| `current-r1 / game-state-5` | `--seek-game-state-case 5`（Canvas 缓存前失败恢复） | 0 | 129.67 | 1/1 |
| `current-r1 / game-state-6` | `--seek-game-state-case 6`（部分目标 Start 失败逆向恢复） | 0 | 522.59 | 1/1 |
| `current-r1 / game-state-7` | `--seek-game-state-case 7`（异常目标 Start 保留未知根，禁逆写时间） | 0 | 262.98 | 1/1 |
| `current-r2 / build-current` | `RizToolbox.Judgements.Tests.csproj` (`Release`, `--no-restore -m:1`) | 0 | 2.86 | 0 errors, 0 warnings（增量跳过） |
| `current-r2 / game-state-1` | `--seek-game-state-case 1`（全五参与者准备与基线读取） | 0 | 37.88 | 1/1 |
| `current-r2 / game-state-2` | `--seek-game-state-case 2`（完整五参与者执行与 36 步逆序恢复） | 0 | 557.23 | 1/1 |
| `current-r2 / game-state-3` | `--seek-game-state-case 3`（Canvas 与 EaseInfor 完整可见性基线读取） | 0 | 6.19 | 1/1 |
| `current-r2 / game-state-4` | `--seek-game-state-case 4`（标量写入前失败前缀回滚） | 0 | 49.92 | 1/1 |
| `current-r2 / target-state-adjacent` | `--seek-target-state-checks` | 0 | 13.19 | 28/28 |
| `current-r2 / hold-modes-adjacent` | `--seek-hold-mode-checks` | 0 | 1.64 | 3/3 |
| `current-r2 / hold-lifecycle-adjacent` | `--seek-hold-lifecycle-checks` | 0 | 19.27 | 62/62 |
| `current-r2 / runtime-publication-adjacent` | `--seek-reference-publication-checks` | 0 | 2.30 | 54/54 |
| `canvas-adjacent-r1 / build-current` | `RizToolbox.Judgements.Tests.csproj` (`Release`, `--no-restore -m:1`) | 0 | 2.88 | 0 errors, 0 warnings（增量跳过） |
| `canvas-adjacent-r1 / canvas-basis-7..10,17,18` | `--canvas-basis-case 7/8/9/10/17/18` | 0 | 1995.89 | 6/6（230.38s / 236.44s / 396.05s / 690.54s / 110.98s / 331.50s） |

## 封存与身份校验（`seal-r1`）

[seal-r1 汇总](../work/n2-seek-game-state-composition-20261119/seal-r1/validation.json)（SHA256：`ab1b59420f736b7a59f011e925184f02c8c4b66ab2bbd027564d8feff3da2ae4`）已对上述三组运行及源码快照完成离线核验：

- **输入与源码变更**：当前输入共 **1802** 项，三批运行的 `inputs-before`、`inputs-after` 与当前工作区完全一致（`inputsMatched=true`、`sourceInputsUnchanged=true`、`payloadIdentityUnchanged=true`）。相对 [original-inputs.json](../work/n2-seek-game-state-composition-20261119/original-inputs.json)（22,744 B / 346,438 B 清单，SHA256 `34a8bbcf44789f8b0a45519d10c02afab2afbf6cf97c5b0c23af168f75b5e76e`）共有 **28** 项变更（**26 修改** + **2 新增**），全部 26 个修改文件的真实改前原件均已核对保存在 `work/n2-seek-game-state-composition-20261119/source-before/`。
- **受保护 `README.md`**：22,744 B，SHA256 保持 `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`。
- **受测程序集（`assemblies-after.json` 与磁盘 DLL 逐字节一致）**：
  - `tests/RizToolbox.Judgements.Tests/bin/Release/net8.0/RizToolbox.Judgements.Tests.dll`（1,622,016 B，SHA256 `dc4d1498b73281e3a47b08b6cf1fc127d2c70246024e6142654bd4ded238c82e`）
  - `tests/RizToolbox.Judgements.Tests/bin/Release/net8.0/RizToolbox.Adapter.dll`（4,599,296 B，SHA256 `417da9d483e143d1ada369e8f165a4188eb8a4f82a8b5627c1e43d11e189b64e`）
  - `tests/RizToolbox.Judgements.Tests/bin/Release/net8.0/RizToolbox.Runtime.dll`（1,798,144 B，SHA256 `dfa5249a7a0a2868becff19ca52f69662979f6228db56da16e7c1c65f6d3d3ec`）
  - `tests/RizToolbox.Judgements.Tests/bin/Release/net8.0/RizToolbox.Sdk.dll`（390,656 B，SHA256 `d29c9f94dea614b799565e32da6d4b2063218417f67ca12c3c20e261441d03ba`）
  - `tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll`（1,281,536 B，SHA256 `9b83e9b58b7c478933f879a9ecb6450d70a58826c0823f6779cd1a8335af6400`）
  - `tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.dll`（1,798,144 B，SHA256 `dfa5249a7a0a2868becff19ca52f69662979f6228db56da16e7c1c65f6d3d3ec`）
- **历史失败保留**：`failure-r1`、`failure-r2`、`visibility-r1/r2`、`restored-ownership-r1`、`forced-adapter-r1` 及阶段夹具日志均完整保留，未覆盖或改写。

## 诚实范围与剩余缺口

`seal-r1` 属于 **离线受控五参与者组合封存**（`visibilityBodiesSynthetic=true`、`nativeOriginalSynthetic=true`、`fixedOriginalBodyVerified=false`、`formalFreshIssued=false`、`fullSeekCompleted=false`、`productionAdmission=false`、`gameExecuted=false`、`installed=false`、`packageChanged=false`）。以下关键缺口仍然保持开放：

1. **R06 正式执行链未闭合**：正式 22-role `Fresh / watch / SuppressObserver` 实入口、固定游戏 `GetTime` consumer 正向执行仍未签收。
2. **N2 公共控制与数值化事务未闭合**：带单位时间/BPM 换算、带数值 `SetSpeed` / `Seek` 公共控制事务、`HoldIdle` 整合及真实游戏校准与 Speed/Seek 实机验收仍未完成。
3. **独立依赖与实机验收未闭合**：`E5` 生产首次准入、`Single mid`、非空 `listener effects`（Tween P4）、`engine setter/effect transfer`、`B15 LOCAL`、`N3/N4`、`R07` 同机 60 秒成本基线、`R08` 三个实机场景、完整 `B01–B19` 与最终交付包验收仍待后续批次完成。

详细索引见 [N2 五参与者组合证据索引](validation/n2-seek-game-state-composition-20261119/README.md)。
