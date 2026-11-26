# 编译第一个 RizToolbox 插件

安装 .NET SDK 8，解压此包，在包含本文件的目录打开 PowerShell。下面的命令生成一个名为 WikiHello 的插件。启用后，它会读取当前场景并订阅场景变化。

```powershell
dotnet new install ./templates/riz-plugin
dotnet new riz-plugin --name WikiHello --PluginId wiki.hello --output ./WikiHello
dotnet restore ./WikiHello/WikiHello.csproj --configfile ./NuGet.Config --packages ./WikiHello/.packages
dotnet build ./WikiHello/WikiHello.csproj -c Release --no-restore
```

构建结果在 `WikiHello/bin/Release/net6.0/`。关闭游戏，将其中的 `WikiHello.dll` 和 `plugin.json` 放入游戏目录下的 `BepInEx/RizToolbox/mods/wiki.hello/`，再启动同版本的 RizToolbox 宿主。插件的 README 里有启用命令。

`sdk/` 存放本次构建的 NuGet 编译引用。首次还原可能从 NuGet 下载 Microsoft 的目标框架包。`examples/source/` 提供示例源码，`examples/packages/` 存放已编译的示例模组。
