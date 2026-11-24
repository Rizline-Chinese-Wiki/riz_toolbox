# Seek 音频、进度缓存与 Clock 组合

这批修复了一个确定的写入窗口：最后一次字段 metadata 查询中，当前 Clock 或 source route 已被撤销，进度参与者仍可能写入第一个缓存。现在字段实际写入前，独立复核原 source 和 Clock 的写权限；高层 graph getter 不在这个最后边界重复执行。

本批已经执行既有真实 `Plan`、`Audio`、`NativeSeekProgressParticipant`、`Clock` 和公共值事务。顺序为音频定位 → 四个进度缓存 → Clock 发布；恢复时反序执行。**完整 Seek 尚未完成**：实际 gameState、HoldEpoch、判定和渲染组合未执行，正式 Fresh／固定游戏 consumer 和正式 Seek provider 不由这些 owned 材料取得准入。

## 实现与执行范围

- `NativeSeekProgressParticipant.Capture` 必须取得独立 `verifyWriteAuthority`，最后 `WriteExactGuarded` guard 在记录 attempted 前核同一 source／Clock operation。
- 正式 `GameplayNativeBackend.Seek` 传入既有 `VerifyOrigin` 和 `captured.VerifyAuthority`，保留完整 graph 检查和实际字段读回。
- 六个新模式实际写读 unmanaged `_nowTime`、`nowTime`、`nowRealTime`、`nowSyncedTime`，进度参与者独立保留八根；音频仍执行原 private SeekSource、owned wrapper／LowerSetTime original 和 native reservation。
- 向前、向后与 ChartCoordinates 三个正向均确认目标并恢复；第三缓存后 getter 失败能够逆序恢复；最后 metadata 内 Clock 撤权／route 替换拒绝缓存写入，也不伪造无权音频 inverse。

受控例：offset 0.2，progress 5.8 → media 6 → chart 9.8；原四缓存 2.8／5.6／2.8／2.8 与原 media／Clock 3 独立恢复，原倍率和 DSP anchor 保持。四缓存写入期间，原 Clock 仍读 3，全部缓存结束后才发布目标 6。

固定 `GameAssembly.dll` 只用 `LoadLibraryExW(..., DONT_RESOLVE_DLL_REFERENCES)` 映射既有证据副本，并执行正式 `GameplayClockNativeProfile` 的字节和布局检查；没有执行映像代码、imports 或 DllMain。转换仍走 IL2CPP API shim：120 BPM，real 4／chart 8 后倍率 0.5；不是实际游戏转换函数或完整 bpmShifts 图。固定 GetTime metadata 与 owned source GetTime 明确不同，不能签固定 consumer。

## 当前结果

`build-and-checks-r1` 原 outer session **28519** 自然 exit 0，完成 chunk **ebece8**；30 个 child 全部自然 exit 0。当前检查共 **956**：

| 当前执行范围 | 项数 |
| --- | ---: |
| 新音频／真实四缓存／Clock 六模式 | 145 |
| Private SeekSource 八模式 | 135 |
| TimeWrite 十模式 | 161 |
| Gameplay | 238 |
| 相邻 rate／publication 三模式 | 277 |

Release solution **0 errors／55 条既有测试警告**；独立 stock-provider **0 errors／0 warnings**。这是本批当前源码的实际执行，未借旧 811、focused 或发行包结果验收。1795 当前输入、10 个受测 DLL、5 个基础 native 输入、隔离映像及 2 个相邻 native 输入前后相同；用户 README SHA256 仍为 `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`。

`Seal-Current.ps1` 自然 exit 0（chunk **83fc48**），核对 6 项源码差异：4 修改、2 新增；四份真实改前文件与六份改后文件已保存。[封存汇总](../work/n2-seek-progress-participant-20261119/validation-r1.json)及[实际运行结果](../work/n2-seek-progress-participant-20261119/build-and-checks-r1/results.json)。

## 保留的失败与下一步

`reproduce-r1` 的原产品稳定 child exit 1：最后 metadata callback 撤真实 Clock 后，仍发生一次 native media 写与一次 `_nowTime` store。expected-failure runner exit 0 只表示成功捕获错误，productAcceptance 为 false。

`focused-r1` 三正向各 25 通过、partial 失败，原因是测试 store observer 的真实 GetTime 提前消费了 getter 注入。只修测试 observer 的注入边界，并把旧日志中的 tuple `{}` 改成实际 JSON 属性，未据此放宽产品。`focused-r2` 三故障模式共 70 通过；这些阶段结果保留，但不单独验收最终源码。

下一步沿既有五参与者入口核 gameState／HoldEpoch 的停止、重建和逆序恢复。尤其核对实际 predecessor Stop 清零 Hold counter 后，后续准备和恢复如何接收同一 epoch 的已知状态变更；目前是待复现线索，不能先放宽 baseline。没有新包、commit／push、游戏安装／启动、UI 或账号操作；B01–B19、实机 Speed／Seek 和 production 未完成。
