# 逐音符标记写入与恢复

`JudgementRunCapture.PrepareSeekNoteFlags(profile, targets)` 已接入实际 `NativeJudgementBackend.RunValue`。调用者为每个实际 runtime NoteInformation 明确提供 `IsActive`、`Destroyed`、`Missed` 三个 Boolean 目标；缺失、额外、重复或零引用被拒绝。输入复制后按 owned NoteView 的实际顺序应用，同一谱面位置的不同 runtime 实例仍是独立对象。

参与者独立持有图与对象引用，验证 ready-paused 来源、原始对象关系和当前字段元数据，通过官方字段 setter 写入并读回。部分写入失败保留实际已尝试前缀，恢复按逆序和当前值条件进行；未尝试字段不会被写入。回调 Dispose、同入口重入、错误线程、原生对象失效和未知 free 责任均有针对性验证。

完整图检查位于阶段边界，逐字段操作只核对所选音符及固定来源。4 与 64 音符计数用例证明完整快照次数保持常数、逐对象读取随 N 线性增长；实际 NativeRuntime 用例另外检查逐对象路径没有重扫谱面。这里没有把 65536 支持界限缩小。

## 实际验证

2026-11-07 `note-flags-r2` 从当前源码在全新 D 输出目录构建：六组件 Release、测试项目均零警告、零错误。直接 `dotnet exec` 本次新 DLL 后，判定套件 **53/53** 通过，包含 **18/18 新组**。其中主要用例经实际 RunValue 和 NativeRuntime 的合成 IL2CPP 导出进入；纯托管计数用例仅证明遍历上界。541 项源码/项目/配置输入在构建前后保持相同，测试目录的四项产品 DLL/PDB 与本次六组件产物逐项一致。

命令入口为 `D:/riz-build-cache-next/seek-next-functional-20261107/build-options/Run-Current.ps1`；每次使用新的 RunName。标准仓库的 Judgements.Tests 和 Build.ps1 已通过现有测试项目注册包含这批用例，无需额外脚本才能发现它们。

历史失败保留：r1 的 `dotnet run --no-build --artifacts-path` 实际启动旧目录，仅输出旧 35 组；该测试范围已明确撤销。随后直接执行 r1 新 DLL 为 36/53，暴露合成夹具缺少 mscorlib image。补齐专属夹具导出后，r2 才是本批首次完整通过的结果。没有改写旧失败记录或用旧 35 组验收新增功能。

## 范围

本功能是内部的显式三标记写入部件。它不猜 prefab 初始值，不推导目标时刻，不处理 renderer、pool、Judge 队列、输入集合、Hold 或 DSP。完整 Seek 仍关闭；阶段采样和条件写入不等于原子 CAS，不能排除未观察到的 ABA。所有实测均为离线合成入口，未安装或启动游戏。

下一组合顺序见 [完整 Seek 的后续工作](../../../.docs/foundation-expansion/NEXT-SEEK-COMPOSITION.md)。源文件、日志与产物身份见 [evidence-index.json](evidence-index.json)。今日早些时候的开发预览 ZIP 仍是其原冻结版本，本批源码没有借用旧 ZIP 的验收结果。
