# RizToolbox 插件作者包使用说明

这个压缩包里已经放好了当前版本的 SDK（1.6.0）、项目模板、示例源码和离线 NuGet 配置，不用编译宿主仓库就能直接写插件或做自制谱曲包。

**前置要求**：安装 [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0)。

---

## 一、写出你的第一个插件

1. 解压本包，在**包含本 README 的目录**打开 PowerShell。
2. 依次执行下面 4 条命令，创建一个叫 `WikiHello` 的示例插件（启用后会读取当前场景并监听场景切换）：

```powershell
dotnet new install ./templates/riz-plugin
dotnet new riz-plugin --name WikiHello --PluginId wiki.hello --output ./WikiHello
dotnet restore ./WikiHello/WikiHello.csproj --configfile ./NuGet.Config --packages ./.packages
dotnet build ./WikiHello/WikiHello.csproj -c Release --no-restore
```

3. **关闭游戏**，把 `WikiHello/bin/Release/net6.0/` 里的 `WikiHello.dll` 和 `plugin.json` 复制到游戏目录的 `BepInEx/RizToolbox/mods/wiki.hello/`。
4. 启动对应版本的 RizToolbox 宿主，按生成项目里的 `README.md` 发送启用命令即可。

### 插件用到了第三方库怎么办？

如果插件依赖了自己的第三方 NuGet 包（比如 `Newtonsoft.Json`），构建时加上 `-p:CopyLocalLockFileAssemblies=true`，然后把生成的第三方 DLL 一起放到 `WikiHello.dll` 和 `plugin.json` 旁边即可。

> 注意：**不要**把 `RizToolbox.Sdk.dll`、宿主 DLL 或 .NET 系统库拷进插件目录，这些由宿主统一提供。

### 包内其他目录

- `sdk/`：本次构建的 `RizToolbox.Sdk` 编译引用包（`NuGet.Config` 已自动指向这里，其他第三方包仍从 NuGet 下载）。
- `examples/source/` 与 `examples/packages/`：示例插件的源码与已编译成品，可直接参考。

---

## 二、制作或试玩 LOCAL 自制谱曲包

### 直接试玩成品示例曲

如果想先看效果，可以在 GitHub Actions 的 Windows 构建页面下载 **`TideSignal-LOCAL-Mods.zip`**（里面自带示例曲 `demo.tide-signal` 和选曲窗口 `tools.local-catalog`）：

1. 关闭游戏，把压缩包里的两个文件夹解压到 `<游戏目录>/BepInEx/RizToolbox/mods/`。
2. 启动游戏，按下面的“启用并开打”步骤分别启用 `demo.tide-signal` 和 `tools.local-catalog`。

> **区分宿主版本**：需要支持当前游戏构建的 SDK 1.6 宿主。查看游戏目录下的 `.doorstop_version`：如果是 `4.5.0-riz-engine-transport-candidate.5`，说明装的是 Steam 专用候选版，升级请继续用同系列 Steam 候选包，不要和 Actions 的通用 Basic 预览包混装。

### 用模板做自己的自制谱曲包

`riz-song` 模板自带一首能直接玩的示例曲（含谱面、音频、封面）。建议先原样编通，再替换 `assets/` 里的文件并改 `Plugin.cs` 中的曲目信息：

```powershell
dotnet new install ./templates/riz-song
dotnet new riz-song --name MyTrack --PluginId indie.my-track --output ./MyTrack
dotnet restore ./MyTrack/MyTrack.csproj --configfile ./NuGet.Config --packages ./.packages
dotnet build ./MyTrack/MyTrack.csproj -c Release --no-restore
```

安装曲包和选曲窗口（先关闭游戏）：
1. 把 `MyTrack/bin/Release/net6.0/` 里的 `MyTrack.dll`、`plugin.json` 和整个 `assets/` 放到 `<游戏目录>/BepInEx/RizToolbox/mods/indie.my-track/`。
2. 把本包的 `tools/tools.local-catalog/` 整个复制到 `<游戏目录>/BepInEx/RizToolbox/mods/`。

### 启用并开打

启动游戏后，在**玩家包**里包含 `scripts/` 的目录打开 PowerShell 7.2+（Steam 专用包就在解压根目录，通用预览包在 `delivery/`），把路径改成你的游戏目录并启用两个插件：

```powershell
$game = 'D:/SteamLibrary/steamapps/common/Rizline'
./scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginId indie.my-track -WaitSeconds 30
./scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginId tools.local-catalog -WaitSeconds 30
```

- 第一条命令如果返回 `waiting`，说明宿主已经记住了启用请求、正在等依赖能力就绪，接着把第二条也跑完即可。
- 查看 `<游戏目录>/BepInEx/RizToolbox/status.json`，等两个插件在 `plugins` 列表里都变成 `State: 2`（已启用）。
- 进入游戏选曲页（默认 Standard 模式），在 **LOCAL catalog** 窗口点 **Tide Signal** → **Request play** 即可开始游玩。

替换成自己的谱面/音频/封面的具体格式要求，以及没看到曲目时的排查步骤，见生成项目里的 `README.md`。
