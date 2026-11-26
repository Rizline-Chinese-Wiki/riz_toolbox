# RizSongProject 本地自制谱曲包

这个模板会把一份谱面、音乐和封面注册进 RizToolbox 的 **LOCAL** 本地曲库，自带一首可直接试玩的原创示例曲《Tide Signal》（第 4 拍有一个 Tap）。

- **编译需要**：.NET SDK 8
- **运行需要**：已安装对应 Steam Windows x64 构建的 RizToolbox 宿主（SDK 1.6）

---

## 1. 编译与安装

在生成的项目目录打开 PowerShell（假设本项目建在作者包根目录下，直接复用上级的 `NuGet.Config`）：

```powershell
dotnet restore ./RizSongProject.csproj --configfile ../NuGet.Config --packages ../.packages
dotnet build ./RizSongProject.csproj -c Release --no-restore
```

**关闭游戏**，把 `bin/Release/net6.0/` 里的以下文件按原有相对路径放进 `<游戏目录>/BepInEx/RizToolbox/mods/wiki.song/`（如果用 `dotnet publish -c Release --no-restore`，则取 `bin/Release/net6.0/publish/` 下的内容；不要拷入任何 `RizToolbox.*` 宿主 DLL）：

```text
wiki.song/
  RizSongProject.dll
  plugin.json
  assets/
    chart.json
    song.wav
    cover.png
```

---

## 2. 在游戏里选曲开打

作者包里自带了可选的选曲窗口插件 `tools.local-catalog`（默认 Standard 模式就能用）：

1. **关闭游戏**，把作者包里的 `tools/tools.local-catalog/` 整个复制到 `<游戏目录>/BepInEx/RizToolbox/mods/`。
2. 启动游戏，在**玩家包**包含 `scripts/` 的目录打开 PowerShell 7.2+（Steam 专用包就在解压根目录，通用预览包在 `delivery/`），改好游戏路径并启用两个插件：

```powershell
$game = 'D:/SteamLibrary/steamapps/common/Rizline'
./scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginId wiki.song -WaitSeconds 30
./scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginId tools.local-catalog -WaitSeconds 30
```

3. 如果第一次返回 `waiting`，说明宿主正在等能力就绪，两条命令都执行完即可。等 `status.json` 里两个插件的状态都变成 `State: 2`（已启用）：
   - 进入选曲页，在 **LOCAL catalog** 窗口选 **Tide Signal** → 点 **Request play** 就能开打（也可以先在主菜单选好曲，再到选曲页点播放）。
   - 打完返回选曲页或主菜单后，窗口底部会显示这次游玩的结算（曲名、难度、分数、完成度、四项判定和结束状态）。
   - **没看到曲目？** 先看 `<游戏目录>/BepInEx/RizToolbox/status.json` 里的插件状态和能力是否就绪，再看 `toolbox.log` 里的资源校验报错。

---

## 3. 换成你自己的曲目

只需修改下面 4 个文件：

| 文件 | 怎么改 / 格式要求 |
| --- | --- |
| `assets/chart.json` | 换成 `rizline.chart.json` v0 谱面：每条带音符的线至少要有 2 个可承载音符的 `linePoints`，音符时间不要超过音频长度（示例曲 120 BPM、恒定卷速 1，第 4 拍 / 2 秒处有 1 个 Tap，音频长 6.25 秒） |
| `assets/song.wav` | PCM16 格式 WAV，单声道或双声道，采样率 8–192 kHz，文件不超过 16 MiB |
| `assets/cover.png` | 8 位 RGBA、非隔行 PNG，宽高都不超过 2048 像素 |
| `Plugin.cs` | 改 `SongId`、`SongTitle`（需和谱面里的 `songsName` 一致）、`Artist`、难度名、`Playback`、`MaxHit`、`MaxScore`，`DifficultyIndex` 选 `0` / `1` / `2` |

如果要换新的插件 ID 或升级版本号，请**同时修改** `Plugin.cs` 里的 `Manifest` 和 `plugin.json`，重新编译后替换整个插件目录。

---

## 4. 进阶：自建选曲界面

想自己做选曲 UI 的插件，在 `plugin.json` 声明依赖 `riz.content.local.catalog`、`riz.content.local.selection` 和 `riz.ui.windows`：
- 在主线程调 `ILocalCatalogProviderService.Read()` 拉取当前曲目列表；
- 调 `ILocalCatalogSelectionService.SelectRevision(song, chartId)` 选中一首歌和对应谱面；
- 玩家在选曲页确认开打时，对选中句柄调用 `RequestPlay()`（若要读取该局结算，则改用 `RequestPlayWithResult()` 并保存返回的结果句柄）。

详细接口与回执说明见 [LOCAL 接口与回执](https://github.com/Rizline-Chinese-Wiki/riz_toolbox/blob/7f45a54189f4865ec548b1ae19c28de468c9d3e3/docs/Audio-content.md#从当前选择请求播放)。

> 宿主会在相关内容与目录能力就绪后自动调用 `Enable`；模板通过 `RegisterBatch` 一次性校验三个资源文件。停用或启用失败时会自动撤销注册，已经开打的局会独立保留该局资源直到结算清理完毕。
