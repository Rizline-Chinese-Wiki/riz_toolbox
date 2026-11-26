# riz_toolbox

Rizline（Steam Windows x64）的 Mod 加载器与公共开发平台：帮玩家组合使用各种插件和自制谱，让 Mod 作者基于统一 SDK 开发，不用自己折腾游戏底层和插件冲突。

> **当前状态**：仍在开发预览中。仅适配 **Steam 构建 `25977441`**（Unity `2022.3.62f2`，Windows x64），启动时会校验 `GameAssembly.dll` 哈希，遇到未知游戏版本会自动拒绝启动。
> 当前宿主版本 `0.3.0`，SDK 版本 `1.6.0`。仓库只包含自有源码、文档和合成测试数据，**不含任何游戏文件**。

---

## 一、玩家快速上手

### 1. 下载安装

从 GitHub Actions 的 **Build Windows x64**（或 Steam 专用发行包，最近记录见 [r25 发行说明](docs/validation/r25-delivery-20261126/README.md)）下载玩家包：

| 版本 | 适合谁 | 说明 |
| --- | --- | --- |
| **Basic 版**（推荐） | 大多数玩家 | 自带 3 个开箱即用的基础插件：曲库与成绩面板、菜单主题改色、F8 快捷面板与本地 WAV 播放（详见 [基础插件说明](docs/Basic-plugins.md)） |
| **Runtime 版** | 想从零自选插件的玩家 | 只装底层宿主，不预装内置插件 |

安装步骤：
1. **先关闭游戏**，把下载的 ZIP 解压到**游戏目录之外**的文件夹。
2. 按解压目录里的 `安装说明.md`（Steam 专用包）或 `delivery/DELIVERY.md`（通用预览包）执行安装脚本。
3. 正常从 Steam 启动游戏即可。

如果从本仓库源码目录直接安装或卸载（同样先关游戏）：

```powershell
$game = 'D:\SteamLibrary\steamapps\common\Rizline'
pwsh -File .\scripts\Install.ps1   -GameDirectory $game
pwsh -File .\scripts\Uninstall.ps1 -GameDirectory $game
```

安装和升级时会保留你的配置与自装插件；卸载只清理未改动的受管文件，不会删你的配置、日志和存档数据。如果遇到 Steam 环境变量导致宿主没拉起来，可以看看本地开发启动器 [tools/RizToolbox.GameLauncher/README.md](tools/RizToolbox.GameLauncher/README.md)。

### 2. 开关插件与查看状态

游戏运行时，可以用命令脚本启停单个插件（把路径换成你的游戏目录）：

```powershell
$game = 'D:\SteamLibrary\steamapps\common\Rizline'
pwsh -File .\scripts\Send-HostCommand.ps1 -GameDirectory $game -Action enable  -PluginId 'basic.theme'
pwsh -File .\scripts\Send-HostCommand.ps1 -GameDirectory $game -Action disable -PluginId 'basic.theme'
```

也可以直接往 `<游戏目录>/BepInEx/RizToolbox/command.json` 写一条命令（每次换个新的 `id`）：

```json
{ "id": "disable-theme-1", "action": "disable", "pluginId": "basic.theme" }
```

支持的动作有 `enable`、`disable`、`status`，以及带 `language` 字段的 `set-language`。常用排查文件都在 `<游戏目录>/BepInEx/RizToolbox/` 下：
- `status.json`：当前加载了哪些插件、启用状态、可用能力和报错原因。
- `toolbox.log` 与 `diagnostics/`：运行日志与结构化会话诊断。
- `settings.json` 与 `activation.json`：宿主设置和已保存的插件启停选择（日常游玩保持 `"runSelfValidation": false`）。

### 3. 试玩自制谱（LOCAL）

从 Actions 构建下载 `TideSignal-LOCAL-Mods.zip`，关游戏后把里面的 `demo.tide-signal` 和 `tools.local-catalog` 解压到 `<游戏目录>/BepInEx/RizToolbox/mods/`，开游戏并用上面的命令启用这两个插件，就能在选曲页的 **LOCAL catalog** 窗口选择并开打。

---

## 二、Mod 与自制谱作者

不想编译整个宿主源码，直接去 Actions 下载 **`RizToolbox-Plugin-Author-Kit.zip`**（打包了对应版本的 SDK 1.6.0、项目模板、示例插件和离线 NuGet 配置），按里面的 [作者包说明](templates/author-kit/README.md) 几条命令就能出包：

- **写功能插件**：用 `dotnet new riz-plugin` 生成工程（详见 [插件模板说明](templates/riz-plugin/README.md)）。
- **做自制谱曲包**：用 `dotnet new riz-song` 生成曲包，替换谱面/音乐/封面即可（详见 [曲包模板说明](templates/riz-song/README.md)）。

写插件时的几条核心约定：
- 目标框架为 **`net6.0`**，入口类实现 `IRizPlugin`（不要继承 BepInEx 的 `BasePlugin`，构造函数里别调游戏接口）。
- 只引用编译期包 `RizToolbox.Sdk`（详见 [SDK 包说明](packaging/sdk/README.md)），**不要**把任何 `RizToolbox.*` 宿主 DLL 放进插件目录。
- 每个插件独立放在 `BepInEx/RizToolbox/mods/<插件ID>/` 目录（放入口 DLL、`plugin.json` 和你自己的第三方依赖）；更新插件 DLL 后需重启游戏。
- 宿主服务和游戏回调统一在 Unity 主线程调用；插件创建的面板、改色层和事件订阅会在停用时自动清理。

### 当前提供的主要能力

| 能力 ID | 能做什么 |
| --- | --- |
| `riz.catalog` / `riz.scores` | 读取官方曲库、谱面列表和历史最好成绩（保留游戏原始完成率） |
| `riz.scenes` / `riz.runs` | 读取当前场景、监听场景切换、开局和单局结算（含分数、判定、Crashed/Skipped） |
| `riz.ui` | 创建简单的游戏内 UGUI 面板 |
| `riz.resources` / `riz.game-resources` | 替换工具图标 PNG，或按优先级叠加/恢复游戏场景里的 `Image.color`（含主菜单按钮别名） |
| `riz.content.local.*` | 注册自制谱（谱面/WAV/封面）、提供 LOCAL 目录查询、选曲与开局结算 |
| `riz.extensions` / `riz.capability-metadata` | 订阅强类型观察点，查询各能力当前的版本与可用状态 |
| `riz.practice` | 暂未开放，查询时会返回不可用原因 |

> 公开接口存在不代表每个能力都在所有场景下完成实机验收，调用前请检查服务返回的可用状态和错误码。

更多开发文档：
[SDK 指南](docs/SDK.md) · [插件编写](docs/Authoring.md) · [音频与自制谱](docs/Audio-content.md) · [宿主命令与控制](docs/Host-control.md) · [运行模式](docs/Runtime-modes.md) · [生命周期](docs/Lifecycle.md) · [玩法原语](docs/Gameplay-primitives.md) · [视觉资源](docs/Visual-resources.md) · [UI 与输入](docs/Ui-input-navigation.md) · [游戏设置](docs/Game-settings.md) · [精确 Hook](docs/Exact-hooks.md) · [性能说明](docs/Performance.md)

---

## 三、从源码构建（开发者）

### 环境要求

| 场景 | 需要安装 |
| --- | --- |
| 只编译托管代码（C#） | Windows 10/11 x64、[.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0)、PowerShell 7.2+（`pwsh`） |
| 编译原生组件（C++/MASM） | Visual Studio 2019 或 2022（勾选「使用 C++ 的桌面开发」，含 MSVC x64 与 Windows 10 SDK） |
| 跑全量离线回归 | Python 3.8+，并执行 `python -m pip install capstone pefile` |

### 常用命令

```powershell
git clone https://github.com/Rizline-Chinese-Wiki/riz_toolbox.git
cd riz_toolbox

# 1. 只编译 C# 托管代码（不需要游戏，也不需要 Visual Studio）
dotnet build .\RizToolbox.sln -c Release

# 2. 完整构建（含原生组件，不跑长回归）
pwsh -File .\scripts\Build.ps1 -SkipTests

# 3. 完整构建 + 全量离线回归（耗时较长）
pwsh -File .\scripts\Build.ps1
```

`Build.ps1` 首次运行会自动下载并校验固定哈希的 Doorstop/BepInEx 依赖到 `.tools/`，再依次编译 `native/`、`src/`、`examples/`、`plugins/` 和测试项目。构建产物默认输出在 `artifacts/`（含 `loader/`、`native-judge-context/`、`sdk/RizToolbox.Sdk.1.6.0.nupkg` 等）。

需要用 `Validate-Runtime.ps1` 做受控自测时，在 `settings.json` 临时开启 `runSelfValidation: true` 并提供 `validation-plan.json`（测完改回 `false`），再运行：

```powershell
pwsh -File .\scripts\Validate-Runtime.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline'
```

<details>
<summary>工具链路径覆盖（自动探测失败时使用）</summary>

脚本默认通过 `scripts/Toolchain.Common.ps1` 自动查找本机工具链，也支持传参（`-VcVars`、`-Python`、`-MsvcDirectory`、`-WindowsSdkVersion`、`-Dotnet`）或设置以下环境变量：

| 变量 | 用途 |
| --- | --- |
| `RIZ_VCVARS` | `vcvars64.bat` 路径 |
| `RIZ_MSVC_DIRECTORY` | `<VS>/VC/Tools/MSVC/<version>` |
| `RIZ_WINDOWS_SDK_DIRECTORY` / `RIZ_WINDOWS_SDK_VERSION` | Windows SDK 路径 / 版本 |
| `RIZ_PYTHON` / `RIZ_DOTNET` | `python.exe` / `dotnet.exe` 路径 |
| `RIZ_HOSTFXR` / `RIZ_HOST_HEADERS` | `hostfxr.dll` 及 `Microsoft.NETCore.App.Host.win-x64` 原生头文件目录 |

</details>

### 仓库目录

```text
src/
  RizToolbox.Sdk/        插件编译期公共接口、DTO 与错误类型
  RizToolbox.Core/       插件依赖管理、启停、作用域、冲突协调与日志
  RizToolbox.Hosting/    plugin.json 校验与独立插件包隔离加载
  RizToolbox.Runtime/    IL2CPP 导出封装、原生入口与 Unity 调用桥
  RizToolbox.Adapter/    Rizline 具体版本适配与各项游戏服务
  RizToolbox.Loader/     Doorstop 启动入口、宿主主循环与命令处理
native/                  Doorstop 补丁、原生运行核与底层测试夹具
plugins/                 随包分发的内置功能插件（如 BackgroundPlay）
examples/                示例插件与基础插件包源码
templates/               插件模板（riz-plugin）、曲包模板（riz-song）与作者包入口
packaging/               SDK NuGet 打包说明
tools/                   本地辅助工具（如 RizToolbox.GameLauncher）
scripts/                 构建、打包、安装、卸载与验证脚本
tests/                   托管回归、包隔离与游戏状态测试
docs/                    使用与开发文档（docs/validation/ 为历史验收证据）
.docs/                   架构设计、任务清单与实时进度
```

---

## 四、已知边界与项目进度

- **游戏版本绑定**：当前只支持 Steam `25977441`，游戏更新后需要重新适配配置。
- **尚未全部收口**：完整底层验收、官方谱变速与跳进度（Speed/Seek）、自制谱全流程实机复验以及部分启动停顿问题仍在持续修复中；各模块最新进展见 [一页进度](.docs/PROGRESS.md) 与 [实现状态](docs/IMPLEMENTATION-STATUS.md)。
- **隔离不是安全沙箱**：每个插件有独立的程序集加载上下文，可避免依赖冲突，但同进程下不防恶意代码，请只安装来源可信的插件。
- **不开放的功能**：暂不支持通用纹理/AssetBundle 替换、任意 Harmony/IL2CPP 补丁、练习模式与跨语言 IPC。

想提 PR 或了解 CI 产出说明，请看 [CONTRIBUTING.md](CONTRIBUTING.md)。历史各批次的离线/实机验证记录保存在 `docs/validation/` 下。
