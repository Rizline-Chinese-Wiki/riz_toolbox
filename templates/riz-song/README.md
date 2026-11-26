# RizSongProject 本地曲包

这个插件把谱面、音乐和封面登记到 RizToolbox 的 LOCAL 目录。随附原创示例曲《Tide Signal》在第 4 拍有一个 Tap。构建需要 .NET SDK 8，运行需要已安装的、支持该 Steam Windows x64 游戏构建的 RizToolbox SDK 1.6 宿主。

## 构建并安装

在生成的项目目录打开 PowerShell。下面的命令假定项目位于作者包根目录下，使用作者包的 `NuGet.Config` 和 SDK 1.6.0 编译引用。

```powershell
dotnet restore ./RizSongProject.csproj --configfile ../NuGet.Config --packages ../.packages
dotnet build ./RizSongProject.csproj -c Release --no-restore
```

关闭游戏后，将 `bin/Release/net6.0/` 的内容作为完整插件包放进游戏目录的 `BepInEx/RizToolbox/mods/wiki.song/`。使用 `dotnet publish -c Release --no-restore` 时，取 `bin/Release/net6.0/publish/` 的内容。保留以下相对路径。运行时 SDK 由宿主提供。

```text
wiki.song/
  RizSongProject.dll
  plugin.json
  assets/
    chart.json
    song.wav
    cover.png
```

## 在游戏中选曲

随作者包提供的 `tools.local-catalog` 是可选的 LOCAL 选曲工具，支持默认的 Standard 模式。

1. 关闭游戏，将作者包的 `tools/tools.local-catalog/` 整个复制到游戏目录的 `BepInEx/RizToolbox/mods/`。
2. 启动游戏后，在解压的玩家包 `delivery` 目录打开 PowerShell 7.2+。将 `$game` 改成实际游戏目录并执行下面的命令。

```powershell
$game = 'D:/SteamLibrary/steamapps/common/Rizline'
./scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginId wiki.song -WaitSeconds 30
./scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginId tools.local-catalog -WaitSeconds 30
```

首次启用若返回 `waiting`，宿主会保留该插件的启用请求。请把两条命令都执行一次，等两个插件在 `status.json` 的 `plugins` 列表中均为 `State: 2`（已启用）。

进入游戏选曲页，在 **LOCAL catalog** 窗口选择 **Tide Signal**，再按 **Request play**。也可以先在主菜单的 LOCAL 窗口选择曲目，然后到选曲页请求播放。面板会显示宿主回执。结束后返回选曲页或主菜单，窗口底部会显示最近一次受理请求对应的首局结算，包括曲名、难度、分数、原始完成度、四项判定和结束状态。若曲目缺席，先检查 `BepInEx/RizToolbox/status.json` 的插件状态与能力，并在 `BepInEx/RizToolbox/toolbox.log` 查看资源校验结果。

自建选曲界面的插件可声明 `riz.content.local.catalog`、`riz.content.local.selection` 和 `riz.ui.windows`。在主线程调用 `ILocalCatalogProviderService.Read()` 列出曲目，按当前显示的歌曲快照和谱面 ID 调用 `ILocalCatalogSelectionService.SelectRevision(song, chartId)`，在选曲页响应玩家确认时对返回的选中句柄调用 `RequestPlay()`。根据失败结果刷新目录或等待开局条件。需要单局结算时使用 `RequestPlayWithResult()` 保存返回的结果句柄。 [LOCAL 接口与回执](https://github.com/Rizline-Chinese-Wiki/riz_toolbox/blob/7f45a54189f4865ec548b1ae19c28de468c9d3e3/docs/Audio-content.md#从当前选择请求播放)

## 换成自己的曲目

| 文件 | 修改内容 |
| --- | --- |
| `assets/chart.json` | 换成 `rizline.chart.json` v0 谱面。每条带音符的线至少有两个可承载音符的 `linePoints`。让音符在音频结束前出现。示例为 120 BPM、恒定卷速 1，Tap 在第 4 拍（2 秒），音频长 6.25 秒。 |
| `assets/song.wav` | 导出 PCM16 WAV，单声道或双声道，8–192 kHz，文件最多 16 MiB。 |
| `assets/cover.png` | 导出 8 位 RGBA、非隔行 PNG。宽和高各不超过 2048 像素。 |
| `Plugin.cs` | 修改 `SongId`、`SongTitle`、`Artist`、难度显示名和 `Playback`。使 `SongTitle` 与谱面的 `songsName` 一致。按你的谱面设定 `MaxHit`、`MaxScore`，并选择 `DifficultyIndex` 0、1 或 2。 |

用另一个插件 ID 发布时，同时修改 `Plugin.cs` 的 `Manifest` 和 `plugin.json`。更新插件版本时也同步这两个文件。改完后重新构建并替换整个插件目录。

宿主在曲包声明的内容登记、目录能力和 v0 解析器就绪后调用 `Enable`。模板通过 `RegisterBatch` 一起校验三个文件，再把成功登记的内容组合成一首歌。停用或启用失败时，宿主按作用域释放登记。已受理的一局由宿主持有自己的资源修订，直到该局完成清理。
