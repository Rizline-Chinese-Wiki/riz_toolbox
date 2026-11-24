# N2 五参与者完整状态组合离线证据

当前 **160/160 托管检查 / 20 个子进程（`current-r1` 4 + `current-r2` 9 + `canvas-adjacent-r1` 7）全部自然 `exit 0`**，覆全部 7 项五参与者组合场景（0 重复）、28 项 TargetState、3 项 Hold 模式、62 项 Hold 生命周期、54 项 Reference Publication 与 6 项 Canvas 相邻场景。`current-r1` 完整 `RizToolbox.sln` Release 构建 `0 errors / 55 warnings`（既有测试警告）；`current-r2` 与 `canvas-adjacent-r1` 增量构建 `0 errors / 0 warnings`。1802 当前输入、6 个受测 DLL 与受保护 `README.md`（SHA256 `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`）前后一致；28 项源码差异（26 修改 + 2 新增）及全部 26 份真实改前文件均已由 `seal-r1` 核验封存。

- [修复与范围报告](../../2026-11-20_seek-game-state-composition-report.md)
- [当前封存汇总 (`seal-r1`)](../../../work/n2-seek-game-state-composition-20261119/seal-r1/validation.json)（SHA256 `ab1b59420f736b7a59f011e925184f02c8c4b66ab2bbd027564d8feff3da2ae4`）
- [源码变更清单 (`changes.json`)](../../../work/n2-seek-game-state-composition-20261119/seal-r1/changes.json)与[原始基线 (`original-inputs.json`)](../../../work/n2-seek-game-state-composition-20261119/original-inputs.json)
- [运行批次 `current-r1`（cases 5–7 + 全量编译）](../../../work/n2-seek-game-state-composition-20261119/current-r1/results.json)
- [运行批次 `current-r2`（cases 1–4 + 147 项相邻检查）](../../../work/n2-seek-game-state-composition-20261119/current-r2/results.json)
- [运行批次 `canvas-adjacent-r1`（Canvas 7,8,9,10,17,18）](../../../work/n2-seek-game-state-composition-20261119/canvas-adjacent-r1/results.json)
- [历史原故障日志 (`failure-r2`)](../../../work/n2-seek-game-state-composition-20261119/failure-r2/game-state-5.stderr.log)与[历史部分运行 (`restored-ownership-r1`)](../../../work/n2-seek-game-state-composition-20261119/restored-ownership-r1/)
- [检查运行器 (`Run-CompositionChecks.ps1`)](../../../work/n2-seek-game-state-composition-20261119/Run-CompositionChecks.ps1)与[封存脚本 (`Seal-Composition.ps1`)](../../../work/n2-seek-game-state-composition-20261119/Seal-Composition.ps1)

仅签收受控离线五参与者对象图组合与逆序恢复（`visibilityBodiesSynthetic=true`、`nativeOriginalSynthetic=true`）；`fixedOriginalBodyVerified=false`、`formalFreshIssued=false`、`fullSeekCompleted=false`、`productionAdmission=false`、`gameExecuted=false`、`installed=false`、`packageChanged=false`。正式 22-role `Fresh/watch/SuppressObserver`、固定 `GetTime` consumer 正向、`E5` 生产首次准入、`N2` 带值 Speed/Seek 与实机 `R06/R07/R08/B01–B19` 均未签收。
