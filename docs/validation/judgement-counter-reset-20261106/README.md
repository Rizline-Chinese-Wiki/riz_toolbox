# 判定七计数重置离线验证

2026-11-07。独立重置与条件恢复参与者已通过正式 `RunValue` 合成入口验证，三份产品源已合入桌面仓库。完整证据路径、大小与 SHA256 集中在 [evidence-index.json](evidence-index.json)。

## 使用内部参与者

`JudgementRunCapture.PrepareJudgementCounterReset(profile)` 绑定实际 `RunValue.Run`、独立持有的 Note 视图与 ready-paused 来源。它选定 `_bad`、`_miss`、`_perfect`、`_early`、`_late`、`_maxPerfect`、`_challengeTimeMiss` 七个声明正确、可变的实例 `Int32` 字段，保留完整有符号初值。

调用顺序为 `PrepareJudgementCounterReset(profile)` → `VerifyPrepared()` → `Commit()` → `Confirm()`。恢复时调用 `Restore()` 与 `ConfirmRestored()`，最后调用 `Dispose()`。提交逐字段清零，恢复按逆序处理已尝试字段，并按观察到的当前值保护外部写入。每次 setter 的责任记录位于 `WriteExactGuarded` 最终托管边界。

入口和访问器见 `Judgements/NativeJudgementBackend.cs`、`Judgements/NativeJudgementBackend.JudgeCounters.cs`，状态与恢复逻辑见 `Gameplay/NativeJudgementCounterResetParticipant.cs`，均位于 `src/RizToolbox.Adapter`。

## 已验结果

| 证据 | 实际观察 | 对应路径 |
| --- | --- | --- |
| E1 源码根审 | 三份源、内部入口与七字段合同通过审查 | 准备独立参与者 |
| E2 Adapter 编译根审 | 268 作者源加 3 生成源对应 271 个 PDB 文档，162 引用，0 警告、0 错误 | 新参与者进入产品程序集 |
| E3 正式入口根审 | 21 组、37 个 fixture、533 检查通过，79 次官方 setter，禁止分派 0 | 实际 Runtime/RunValue 在合成存储上提交、确认、恢复和清理 |
| E4 整合 journal | 2 新增、1 更新，原文件有 D 盘备份 | 已验三源应用到仓库 |
| E5 合并后根审 | 当前 C 盘三源哈希与候选一致 | 确认整合后源身份 |

正式场景覆盖元数据与 ready-paused 拒绝、setter 存储前后故障、外部值变化、最终守卫失效、递归、跨线程调用、活动 Note 视图回调释放和局修订。故障测试注入一次结果未知的根释放，保留一份原 owner/participant 清理责任和对应包装对象。重复 `Dispose()` 时责任继续保留，free 尝试计数保持。

net8 测试宿主引用 net6 产品时保留 4 条 CS1701 框架引用统一警告，错误为 0。正式 compiler、metadata、cases 与独立 held outer 均自然退出 0。

## 核对证据

以下命令在项目目录只读核对索引中的证据与当前三源。

```powershell
$index = Get-Content -LiteralPath 'docs/validation/judgement-counter-reset-20261106/evidence-index.json' -Raw | ConvertFrom-Json
$files = @($index.evidence.PSObject.Properties.Value) + @($index.sources | ForEach-Object { $_.file })
foreach ($entry in $files) {
    $item = Get-Item -LiteralPath $entry.path
    $actual = (Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($item.Length -ne $entry.bytes -or $actual -ne $entry.sha256) { throw ('Evidence changed ' + $entry.path) }
}
```

## 正常六组件构建

正常 Loader ProjectReference Release 链已重新构建全部六组件，0 警告、0 错误。514 份作者源加 18 份生成源对应 532 个 PDB 文档，11 组 fresh 引用和 22 份 DLL/PDB 副本逐一核对。构建、metadata 与独立 held outer 均自然退出 0，根审重新核对 2666 份当前文件事实。

首轮构建在 CoreCompile 前因采集 targets 内遗留的旧捕获目录而失败。r2 修正该常量，并在 Prepare 与后续文件核验中检查 XML 常量和实际 argv 精确相等。原失败记录列入证据索引。

## 后续范围

当前每字段预期值检查提供采样式条件写入，原子 CAS 与任意 ABA 保证继续待覆盖。完整 Seek 还需 score/combo/life/history、Judge 三列表、holdOnJudgeCount、活跃协程、input/result 状态及实际音频调度的协同重建。真实游戏、完整来源代次与生产调度验收继续后置。
