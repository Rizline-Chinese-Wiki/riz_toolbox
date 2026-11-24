# 从模板编写独立插件

作者只引用 SDK。`artifacts/sdk/RizToolbox.Sdk.1.3.0.nupkg` 是编译引用包，游戏运行时由宿主提供实现；不要把 SDK、Core、Runtime、Adapter、Hosting 或 Loader DLL 放入插件目录。已有 [14 项分发检查](validation/foundation-structured-errors-20261029/README.md)使用独立模板引擎和全新依赖缓存生成两个插件，执行正式包加载、宿主启停、能力撤回与恢复，以及结构化错误传播、失效作用域、私有合同拒绝和插件故障隔离。它不等于新增游戏能力已实机验收。

## 创建和构建

需要 .NET SDK 8、PowerShell 7.2+。先在此仓库根目录构建引用包并安装本地模板：

```powershell
pwsh -File ./scripts/Pack-Sdk.ps1
dotnet new install ./templates/riz-plugin
dotnet new riz-plugin --name WikiHello --PluginId wiki.hello --output ./artifacts/WikiHello
dotnet restore ./artifacts/WikiHello/WikiHello.csproj --source ./artifacts/sdk --source https://api.nuget.org/v3/index.json
dotnet build ./artifacts/WikiHello/WikiHello.csproj -c Release --no-restore
```

模板面向 `net6.0`，通过 `IncludeAssets="compile"`、`PrivateAssets="all"` 引用 SDK。NuGet 从 `ref/net6.0` 选编译资产，该目录不提供运行时资产；这是 [NuGet 的资产选择规则](https://learn.microsoft.com/en-us/nuget/create-packages/select-assemblies-referenced-by-projects)。本项目的检查还直接验证 `project.assets.json`、生成 DLL 的引用身份和输出目录，防止把引用包误作运行时组件。

普通开发还原可使用本地 feed 与 NuGet；仓库的 `Test-SdkDistribution.ps1` 额外使用来源映射，确保 `RizToolbox.Sdk` 来自本轮本地包，并为每轮创建全新的包缓存。它不修改全局已安装模板。

## 放入游戏与启用

游戏关闭时，把以下两个文件复制到新目录 `BepInEx/RizToolbox/mods/wiki.hello/`：

```text
artifacts/WikiHello/bin/Release/net6.0/WikiHello.dll
artifacts/WikiHello/plugin.json
```

模板只读取当前场景并观察后续场景变化。运行中面板和游玩控制不是它的职责。入口类的 `Manifest` 和 `plugin.json` 必须同时更新 ID、版本、SDK 范围及依赖，入口构造函数不进行游戏操作。

重启游戏后，已有安装的启用选择不会被新包覆盖。向 `BepInEx/RizToolbox/command.json` 写一次带新 ID 的命令：

```json
{ "id": "enable-wiki-hello-1", "action": "enable", "pluginId": "wiki.hello" }
```

在 `status.json` 查看插件状态及不可用原因。缺少所需能力会等待；异常会归属插件。作用域中的回调在停用时清除，再次启用须重新取得服务，不能复用旧句柄。

插件启动所需的服务应列进 `requiredCapabilities`。普通运行模式下，部分模块只在插件请求后建立，单独调用 `GetService` 不会唤醒模块。等待能力就绪的插件仍会提出需求。参见[运行模式与发行包](Runtime-modes.md)。

## 选择接口和版本

程序集身份固定为 `RizToolbox.Sdk, Version=1.0.0.0`，契约版本为 `SdkInfo.Version == 1.3.0`。插件清单包含最小兼容 SDK 与排他的上限；运行时还须按能力 ID 查询服务及细分操作。引用 SDK 里的接口不代表当前游戏构建提供相应服务。

`artifacts/sdk/sdk-contracts.json` 由分发检查从当前 SDK 反射生成，列出能力声明和公开接口；它是 API 清单，不能充当能力运行证明。实际支持与验收范围见 [实现状态](IMPLEMENTATION-STATUS.md)。接口用法见 [SDK](SDK.md)，原生时钟、控制、UI、资源与内容均有各自限制。

`OperationResult` / `OperationResult<T>` 和已有的 `Result` / `Result<T>` 都以 `OperationError` 表示失败。服务视图在作用域失效或错误线程访问时返回 `ScopeExpired` / `WrongThread`，携带所请求能力，并在已知目标时保留目标 ID；拒绝分支不会调用后端。`GetService<T>` 的失败字段是 `Error`，无需解析文本原因。返回普通列表或原始值的方法仍可能抛失效异常。完整工厂与代码目录见 [操作错误](Operation-errors.md)。

## 验证分发与可重复打包

```powershell
pwsh -File ./scripts/Test-SdkDistribution.ps1
```

该脚本保留生成源码、还原锁文件和结果于新的 `artifacts/sdk-distribution-<ID>/`，不会启动游戏。完整构建也执行此检查。`Pack-Sdk.ps1` 对 SDK 8 生成的未签名 NuGet 包统一 ZIP 时间、OPC 属性路径和关系 ID；DLL 内容不变，拒绝处理已有签名的包。相同引用输入的两次全新 pack 已验证哈希一致，见 [复打包证据](validation/distribution-sdk-20261020/repack-reproducibility.json)。

统一编译启用确定性输出及源码路径映射；不同 SDK、依赖或源码仍可能产生不同结果。相同输入复打包不代表已证明任意机器上的完整游戏包可复现，干净宿主构建和升级矩阵仍需独立验收。
