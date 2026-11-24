# 完整原生 Note 视图离线验证

2026-11-06。三份产品源已合入桌面仓库，正常六组件构建、18 个契约场景与 16 个正式入口合成场景均已验收。详细文件哈希和证据路径见 [evidence-index.json](evidence-index.json)。

## 已交付能力

`JudgementRunCapture.CaptureSeekRuntimeNoteView(profile)` 从当前正式 `RunValue` 捕获独立保留的只读视图。视图按实际 `NoteInformation.noteData` 和 `LineInformation` 的相邻 `LinePoint` 引用关联完整谱面，保留两个原生数组的顺序、同一谱面位置的多个运行实例，以及缺少运行实例的位置。

`Coverage` 区分空谱面、无运行实例、部分覆盖和完整谱面位置覆盖。调用者释放原 `RunValue` 后，视图继续持有自己的来源。每次 `VerifySource()` 核对局修订、对象引用、数组和谱面数据，失效后拒绝后续读取。

源码位于 `src/RizToolbox.Adapter/Gameplay/NativeSeekRuntimeNoteView.cs`、`Judgements/NativeJudgementBackend.SeekNotes.cs` 和 `Judgements/NativeJudgementBackend.cs`。这是后续 Note/Judge 状态重建的只读输入。

## 验证结果

| 证据 | 观察 | 对应能力与后续工作 |
| --- | --- | --- |
| E1 正常六组件构建 | 512 作者源和 18 生成源对应 530 个 PDB 文档，11 个新组件引用、22 份 DLL/PDB 副本，0 警告、0 错误 | 新源码通过正常 Loader 传递构建 |
| E2 契约检查 | 18 个场景通过，保留原检查源，断言总数未插桩统计 | 覆盖空/部分/多实例映射、不可变副本、递归与线程/释放合同 |
| E3 正式入口检查 | 16 场景、332 检查通过，3738 次字段读取、2307 次方法调用，禁止分派 0 | 实际 Runtime/RunValue 在合成存储上完成引用关联和失效处理 |
| E4 整合日志 | 1 原件备份、2 新文件和 1 文件更新，逐文件写前日志与合并后哈希核对 | 桌面产品源与已验编译源一致 |

正式入口还核对了方法缓存移除后的 `ObjectClass` 边界、静态 `op_Implicit` 回调、字段 getter 中的释放/局修订、数组替换，以及 4939 个合成根记录与释放尝试的一一对应。

模拟故障分别保留一个释放结果未知的包装对象和一个未确认引用发布记录。测试核对了原对象身份、持有者和禁止盲目重试的行为。两个 net8 测试宿主引用 net6 产品时各有 4 条 CS1701 框架引用统一警告，执行均自然退出 0。

## 证据核对

完整原始证据保存在 `D:/riz-build-cache-next/playback-lower-time-r1-20261104`。`evidence-index.json` 记录本次四个根验收文件的完整路径、大小和 SHA256，可在项目目录只读核对。

```powershell
$index = Get-Content -LiteralPath 'docs/validation/native-note-view-20261106/evidence-index.json' -Raw | ConvertFrom-Json
foreach ($entry in $index.evidence.PSObject.Properties.Value) {
    $actual = (Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.sha256) { throw ('Evidence changed: ' + $entry.path) }
}
```

## 下一步

完整 Seek 还需 Note/Judge/hold/input/result 状态重建、实际音频调度，以及独立来源代次和写入责任的验证。真实游戏测试继续后置。
