# 编译第一个 RizToolbox 插件

安装 .NET SDK 8，解压此包，在包含本文件的目录打开 PowerShell。下面的命令生成一个名为 WikiHello 的插件。启用后，它会读取当前场景并订阅场景变化。

```powershell
dotnet new install ./templates/riz-plugin
dotnet new riz-plugin --name WikiHello --PluginId wiki.hello --output ./WikiHello
dotnet restore ./WikiHello/WikiHello.csproj --configfile ./NuGet.Config --packages ./.packages
dotnet build ./WikiHello/WikiHello.csproj -c Release --no-restore
```

构建结果在 `WikiHello/bin/Release/net6.0/`。关闭游戏，将其中的 `WikiHello.dll` 和 `plugin.json` 放入游戏目录下的 `BepInEx/RizToolbox/mods/wiki.hello/`，再启动同版本的 RizToolbox 宿主。插件的 README 里有启用命令。

插件使用私有托管库时，构建命令加上 `-p:CopyLocalLockFileAssemblies=true`。将输出中的私有运行时 DLL 和所需的传递依赖一并放入插件包。例如，把 `Newtonsoft.Json.dll` 放在 `WikiHello.dll` 和 `plugin.json` 旁边。SDK、宿主实现和 .NET 框架程序集由宿主提供。

`sdk/` 存放本次构建的 NuGet 编译引用。配置将 `RizToolbox.Sdk` 指向这个本地目录，其他库和目标框架包可从 NuGet 获取。`examples/source/` 提供示例源码，`examples/packages/` 存放已编译的示例模组。

## 制作 LOCAL 自制谱曲包

`riz-song` 生成一首带谱面、音乐和封面的示例曲目。可以先原样编译，再替换项目 `assets/` 里的三个文件，并在 `Plugin.cs` 修改曲名、作者和难度。

```powershell
dotnet new install ./templates/riz-song
dotnet new riz-song --name MyTrack --PluginId indie.my-track --output ./MyTrack
dotnet restore ./MyTrack/MyTrack.csproj --configfile ./NuGet.Config --packages ./.packages
dotnet build ./MyTrack/MyTrack.csproj -c Release --no-restore
```

关闭游戏，将 `MyTrack/bin/Release/net6.0/` 里的 `MyTrack.dll`、`plugin.json` 和整个 `assets/` 放到游戏目录的 `BepInEx/RizToolbox/mods/indie.my-track/`。要在游戏中选曲，再把本包的 `tools/tools.local-catalog/` 整个复制到 `BepInEx/RizToolbox/mods/`。

启动游戏，在对应玩家包的 `delivery` 目录打开 PowerShell 7.2 或更高版本，将路径改成你的游戏目录，启用曲包和选曲窗口。

```powershell
$game = 'D:/SteamLibrary/steamapps/common/Rizline'
./scripts/Send-HostCommand.ps1 -GameDirectory $game -Action enable -PluginIds 'indie.my-track','tools.local-catalog' -WaitSeconds 30
```

使用默认的 Standard 模式，进入游戏选曲页，在 **LOCAL catalog** 窗口点击 **Tide Signal**，再点 **Request play**。生成项目的 README 列出了替换资源的格式和曲目缺席时的排查方法。
