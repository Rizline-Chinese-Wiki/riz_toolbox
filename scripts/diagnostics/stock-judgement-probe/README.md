# r24 官方判定探针

供 r24 同会话诊断使用。每局只在 `chart.PastelLines.RekuMochizuki.0.EZ` 从暂停恢复后，记录第一条满足同宿主、同运行和同场景条件的原生判定返回。

## 安装与启用

1. 关闭游戏，把 `plugin.json` 和 `StockJudgementProbe.dll` 放到 `<游戏目录>/BepInEx/RizToolbox/mods/probe.r24.stock-judgement/`。这两个文件构成独立插件包。
2. 启动游戏并确认 `probe.r24.stock-judgement` 已启用。如果宿主已有保存的插件启用设置，可在官方曲目开打前从仓库根目录执行下面的命令。[激活命令脚本](../../Send-HostCommand.ps1)

```powershell
$game = Read-Host 'Rizline 游戏目录'
pwsh -File scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginId probe.r24.stock-judgement -WaitSeconds 10
```

宿主的 `diagnostics/<宿主会话>/events-*.jsonl` 中会出现 `[probe.r24.stock-judgement] StockJudgementProbe POST_RESUME ` 开头的 `message`。短 JSON 提供生命周期会话、运行、场景代次、恢复修订号及 SDK 返回的判定标识。

## 从源码构建

在仓库根目录使用 .NET 8 SDK 和已解包的 r24 SDK 作者包。将命令输出中的 `StockJudgementProbe.dll` 和同目录的 `plugin.json` 用作安装文件。

```powershell
$author = Read-Host '已解包的 r24 SDK 作者包目录'
$feed = Join-Path $author 'feed'
$output = Join-Path $env:TEMP ('riz-stock-judgement-' + [Guid]::NewGuid().ToString('N'))
dotnet build scripts/diagnostics/stock-judgement-probe/StockJudgementProbe.csproj -c Release --artifacts-path $output "-p:RestoreSources=$feed"
Get-ChildItem -LiteralPath (Join-Path $output 'bin/StockJudgementProbe/release') -File
```
