# 视觉资源退休完成回执

2026-10-21。**767/767、25个检查项目通过，0编译警告/错误，已安装。** Visual由68项增至69项；18个安装文件hash匹配，256项源码输入一致，原始Rizline.exe、GameAssembly.dll、globalgamemanagers前后不变。游戏已退出、临时probe配置不存在、安装journal无pending或transaction，见`final-state.json`。

## 改变与验证

766实机中，Unity延迟销毁先记录`Visual cleanup pending`，原有重试成功路径没有回执，无法从日志确认退休预约何时释放。本包在cleanup谓词返回true、ticket释放之后，记录一次Info级`Visual cleanup completed`，包含资源描述、目标种类、独立总尝试数及owner/global退休预约数量。已有失败退避策略不变；日志sink抛异常不会打断清理。

回执仅证明cleanup谓词完成与retirement reservation释放；不等于资产预算、GC根或GPU释放证明。总尝试数独立记录，不能以封顶的Failures+1替代。源文件为`src/RizToolbox.Adapter/Visual/VisualResourceRetirement.cs`。新增一项有意义的异常路径回归：pending和success日志sink均抛异常时，重试仍完成、容量先归还，后续tick不重复清理。测试在`tests/RizToolbox.Visual.Tests/Program.cs`。

完整构建包含69/69 Visual、64/64 AudioContent、73/73 UI/input、57/57 Runtime、25/25 GameResources，以及两组真实原生fixture31,261/61,358（inFlight均0）、SDK分发10/10。详见`build.log`与`build-summary.json`，未因单纯增加日志重跑游戏流程。

## 证据与复验

`source-manifest.json`、`package-manifest.json`、`native-build/`固定本包输入与产物；`installation-check.json`、`source-integrity-check.json`、`original-game-before.json`和`original-game-after.json`记录安装与原始文件校验。

在仓库根目录执行：

```powershell
./scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/cleanup-receipt-20261021'
./scripts/Install.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Rizline'
```

本包尚无新游戏会话，不能宣称实际原生成功回执已经观察。先前[766实机快照](../runtime-color-content-20261021/evidence-snapshot-20261021T044942Z/README.md)只证明对应旧代码的Pause/Resume、Retry与新RunId、一次hold.end、TMP基线及Float应用；其中cleanup pending边界原样保留。后续应单独取得本包的cleanup完成回执，并检查根/预算真实异常与长期释放；[root/budget审查](../runtime-color-content-20261021/root-budget-review.md)记录了尚未由生产输入复现的异常清理责任缺口。

本批还完成[内容事务独立审查](../content-transaction-audit-20261021/independent-review.md)、[材料清理审查](../runtime-color-content-20261021/material-cleanup-review.md)及[19项后续任务交接](../../../.docs/foundation-expansion/NEXT-HANDOFF-20261021.md)。完整B01–B19仍未全部验收。
