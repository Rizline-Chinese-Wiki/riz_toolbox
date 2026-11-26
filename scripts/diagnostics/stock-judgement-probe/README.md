# r24 官方判定探针插件

用于 r24 同会话诊断的小型探针插件。每局游玩 `chart.PastelLines.RekuMochizuki.0.EZ` 时，它只会在「暂停后恢复」之后，记录第一条满足同宿主、同运行、同场景条件的原生判定返回。

## 安装与启用

1. 先关掉游戏，把 `plugin.json` 和 `StockJudgementProbe.dll` 放到 `<游戏目录>/BepInEx/RizToolbox/mods/probe.r24.stock-judgement/`。
2. 启动游戏，确认 `probe.r24.stock-judgement` 已启用。如果宿主保留了旧的插件开关状态，可以在官方曲目开打前，在仓库根目录用 [Send-HostCommand.ps1](../../Send-HostCommand.ps1) 手动启用：

```powershell
$game = Read-Host 'Rizline 游戏目录'
pwsh -File scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginId probe.r24.stock-judgement -WaitSeconds 10
```

触发后，宿主的 `diagnostics/<宿主会话>/events-*.jsonl` 里会多出一条以 `[probe.r24.stock-judgement] StockJudgementProbe POST_RESUME ` 开头的日志，里面的简短 JSON 记录了会话、运行、场景代次、恢复修订号和 SDK 返回的判定标识。

## 从源码构建

需要 .NET 8 SDK 和已解压的 r24 SDK 作者包。在仓库根目录执行后，取出输出目录里的 `StockJudgementProbe.dll`，搭配本目录的 `plugin.json` 一起安装即可：

```powershell
$author = Read-Host '已解包的 r24 SDK 作者包目录'
$feed = Join-Path $author 'feed'
$output = Join-Path $env:TEMP ('riz-stock-judgement-' + [Guid]::NewGuid().ToString('N'))
dotnet build scripts/diagnostics/stock-judgement-probe/StockJudgementProbe.csproj -c Release --artifacts-path $output "-p:RestoreSources=$feed"
Get-ChildItem -LiteralPath (Join-Path $output 'bin/StockJudgementProbe/release') -File
```
