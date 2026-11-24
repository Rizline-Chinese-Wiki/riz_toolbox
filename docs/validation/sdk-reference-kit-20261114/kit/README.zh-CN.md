# RizToolbox SDK 参考包：候选939

这是给模组作者编译、检查模板的开发参考包。SDK合同版本1.2.0；来源为939输入的当前隔离候选。它不含游戏运行时安装包，新Hold实现尚未合主，完整SDK/B01–B19未验收。真实Speed/Seek、生产启动与HoldIdle仍关闭。

本包带实际验证过的SDK引用nupkg、原始插件模板和本机缓存中原样复制的四个.NET6框架引用包，可以离线还原。第三方包未改动，许可证保留在各nupkg内。verification-examples是两份分发链校验源码，含专用错误合同探针，不能当作游戏功能模组。

在本目录打开PowerShell，使用已安装的.NET8 SDK：

```powershell
dotnet new install .\template\riz-plugin --debug:custom-hive .\.template-hive
dotnet new riz-plugin --name MyMod --PluginId author.mymod --output .\MyMod --debug:custom-hive .\.template-hive
dotnet restore .\MyMod\MyMod.csproj --configfile .\NuGet.Config
dotnet build .\MyMod\MyMod.csproj -c Release --no-restore
```

模板只装入此目录的独立hive。新建项目会得到只读场景示例；游戏加载仍需要另行验收的兼容运行时。本次没有安装或启动游戏。

同一候选SDK的两份独立插件已通过正式宿主14/14检查；这是编译引用、模板和托管宿主证据，不是完整游戏或SDK验收。详细证据位于项目docs/validation/sdk-current-hold-20261114，清单摘要和所有交付文件SHA见manifest.json。旧881输入预览ZIP保持不变。
