# RizToolbox

Rizline PC 端工具集的协作仓库。目前公开的部分是 **托管 SDK 契约和插件示例**：用于讨论和验证跨模块的数据结构、接口和错误语义，不是可安装的游戏工具或完整运行时。

## 托管插件示例

示例展示如何通过公开 SDK 声明宿主依赖和管理资源，但这里不包含实际宿主或成品安装包。

- **歌曲成绩面板**：需要曲目目录、成绩、窗口和场景服务。
- **主题**：需要游戏资源和场景服务。
- **快捷面板**：需要窗口、输入操作、音频、插件存储和场景服务。示例 WAV 路径只是占位，不附带音频文件。

示例 JSON 仅供参考，没有游戏数据。可以在不启动游戏的情况下编译和核对三个包的入口与依赖声明：

```sh
dotnet build examples/RizToolbox.BasicPlugins/RizToolbox.BasicPlugins.csproj -c Release
dotnet run --project tests/RizToolbox.BasicPlugins.ManifestSmoke/RizToolbox.BasicPlugins.ManifestSmoke.csproj -c Release
```

## 开始开发

需要 .NET 8 SDK（`global.json` 固定到 8.0 系列）。契约库继续面向 `net6.0`，测试程序运行于 `net8.0`。如本机没有 .NET 6 引用包，首次还原时 .NET SDK 会从配置的 NuGet 源下载引用包；无需安装 .NET 6 运行时来执行这组测试。

```sh
dotnet build src/RizToolbox.Sdk/RizToolbox.Sdk.csproj -c Release
dotnet run --project tests/RizToolbox.Sdk.Smoke/RizToolbox.Sdk.Smoke.csproj -c Release
```

- `src/RizToolbox.Sdk/`：独立的 C# 契约和结果序列化。
- `tests/RizToolbox.Sdk.Smoke/`：不依赖游戏或外部测试包的契约冒烟测试。

本阶段不发布 NuGet 包，不承诺与已有私下发行包的完整 API 或二进制兼容性，也未验证游戏内行为。不含运行时、游戏文件、第三方资源或历史构建产物。

协作流程见 [CONTRIBUTING.md](CONTRIBUTING.md)，源码按 [MIT](LICENSE) 许可。
