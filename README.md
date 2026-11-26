# riz_toolbox

Rizline（Steam Windows x64）的 Mod 公共底层与工具集：为 Mod 作者提供稳定的插件接入、能力服务、共存协调与游戏版本适配。

- 目标游戏构建：Steam `25977441` / Unity `2022.3.62f2` / Windows x64（用 `GameAssembly.dll` 的 SHA-256 选择适配配置，未知构建拒绝启动）。
- 当前版本：宿主 `0.3.0`，SDK `1.3.0`（SDK 源码在 `src/RizToolbox.Sdk/`，随构建产出编译引用包）。
- 项目状态：**仍在开发中**，19 项底层任务尚未全部验收；进度与未完成项见 [一页进度](.docs/PROGRESS.md) 与 [实现状态](docs/IMPLEMENTATION-STATUS.md)。
- 本仓库只包含自有源码、文档和合成测试数据；不包含游戏文件。

```
src/RizToolbox.Sdk/       IRizPlugin、服务接口、DTO、有版本的扩展点
src/RizToolbox.Core/      依赖、启用意愿、能力协调、作用域、配置、主线程
src/RizToolbox.Hosting/   plugin.json 校验、独立包、私有依赖解析
src/RizToolbox.Runtime/   IL2CPP 导出 API、单一原生入口、Unity 调用桥
src/RizToolbox.Adapter/   Rizline 构建配置、业务服务、真实状态与资源适配
src/RizToolbox.Loader/    Doorstop 入口、组件组合、命令和状态诊断
native/                   Doorstop 补丁、原生上下文、协程运行核与各 fixture
examples/                 独立的 SDK 插件包（`examples/packages`、`examples/basic-packages`）
plugins/                  随包分发的插件（如 BackgroundPlay）
tests/                    托管回归、包隔离、PNG、游戏状态及安装检查
scripts/                  构建、安装、打包与证据脚本
docs/                     子系统接口与限制说明；docs/validation 为验收证据摘要
.docs/                    设计与任务计划、实现状态与历史检查点
```

## 快速开始

### 1. 环境要求

| 用途 | 要求 |
|---|---|
| 托管构建 / 测试 | Windows 10/11 x64、[.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0)、PowerShell 7.2+（`pwsh`） |
| 原生组件（Doorstop、运行核、fixture） | Visual Studio 2019 或 2022 的“使用 C++ 的桌面开发”工作负载（MSVC x64 + Windows 10 SDK） |
| 原生 fixture 证据脚本 | Python 3.8+ 及 `capstone`、`pefile`（`python -m pip install capstone pefile`）；仅完整回归需要，`-SkipTests` 不需要 |

```powershell
git clone https://github.com/Rizline-Chinese-Wiki/riz_toolbox.git
cd riz_toolbox
dotnet build .\RizToolbox.sln -c Release    # 托管部分：0 error 即可
```

首次 `dotnet build` 会从 NuGet 还原 .NET 6/8 引用包。托管构建不需要游戏、不需要 Visual Studio，也不需要管理员权限。

### 2. 完整构建（含原生组件）

```powershell
pwsh -File .\scripts\Build.ps1                 # 构建 + 全量离线回归（耗时较长）
pwsh -File .\scripts\Build.ps1 -SkipTests      # 只构建，不跑测试
```

完整回归会用 Python 复核原生产物（`docs/validation/*/verify-*.py`），缺少 `capstone`/`pefile` 时 `Build.ps1` 会在开头直接报错并给出安装命令。`-SkipTests` 只构建托管与原生组件，不执行这些复核。

`Build.ps1` 会：

1. 下载并校验固定哈希的 BepInEx/Doorstop 依赖包到 `.tools/`（首次联网，之后校验缓存）；
2. 用本机 MSVC 编译 `native/` 下的原生组件；
3. 编译 `src/`、`examples/`、`plugins/` 与托管测试，并按需执行离线回归。

产物在 `artifacts/`（默认）：`native-judge-context/`、`loader/`、`sdk/RizToolbox.Sdk.1.3.0.nupkg` 等。

### 3. 安装 / 卸载 / 受控复验

```powershell
pwsh -File .\scripts\Install.ps1   -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline'
pwsh -File .\scripts\Uninstall.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline'
pwsh -File .\scripts\Validate-Runtime.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline'
```

安装和更新前请关闭游戏。安装器校验文件归属、哈希与待完成写入记录，保留现有用户配置；卸载只移除未修改的受管文件，保留用户修改项、日志和运行数据。正常启动游戏即可加载，日常 `runSelfValidation` 应为 `false`。

### 4. 工具链路径覆盖

所有脚本都接受显式参数（如 `-VcVars`、`-Python`、`-MsvcDirectory`、`-WindowsSdkVersion`、`-Dotnet`），未指定时由 `scripts/Toolchain.Common.ps1` 自动探测（`vswhere`、`PATH`、标准安装目录）。也可以用环境变量固定：

| 变量 | 含义 |
|---|---|
| `RIZ_VCVARS` | `vcvars64.bat` 路径 |
| `RIZ_MSVC_DIRECTORY` | `<VS>/VC/Tools/MSVC/<version>` |
| `RIZ_WINDOWS_SDK_DIRECTORY` / `RIZ_WINDOWS_SDK_VERSION` | Windows SDK 根目录 / 版本 |
| `RIZ_PYTHON` | `python.exe` |
| `RIZ_DOTNET` | `dotnet.exe` |
| `RIZ_HOSTFXR` | `hostfxr.dll` |
| `RIZ_HOST_HEADERS` | `Microsoft.NETCore.App.Host.win-x64` 的 `runtimes/win-x64/native` 目录 |

## 构建与运行说明

构建使用固定哈希的 Doorstop、CoreCLR 和 Dobby 依赖包，游戏内使用随包部署的 .NET 6 运行时。插件只引用共享 SDK，宿主管理依赖、主线程、资源归属和停用清理；普通插件不携带宿主 SDK 实现 DLL。

运行使用 Doorstop 直接启动本框架，再由 Runtime 调用 IL2CPP 导出 API；未启用完整 BepInEx 插件链，插件实现 `IRizPlugin`，不继承 `BasePlugin`。安装后的布局：

```text
Rizline/
  BepInEx/plugins/RizToolbox/
    RizToolbox.Sdk.dll
    RizToolbox.Core.dll
    RizToolbox.Hosting.dll
    RizToolbox.Runtime.dll
    RizToolbox.Adapter.dll
    RizToolbox.Loader.dll
    profiles/steam-25977441.json
  BepInEx/RizToolbox/
    settings.json
    mods/
      example.panel/plugin.json + 入口 DLL
      example.events/plugin.json + 入口 DLL
      example.color-low/plugin.json + 入口 DLL
      example.color-high/plugin.json + 入口 DLL
      example.scores/plugin.json + 入口 DLL
    config/plugin-<插件ID>/config-<配置名>.json
    toolbox.log
    status.json
```

每个插件包有独立 `AssemblyLoadContext`，私有依赖只从该包解析。宿主共享 SDK，其程序集身份必须匹配；包内禁止复制 SDK、宿主或框架运行时组件。平铺在 `mods` 根目录的旧 DLL 给出迁移诊断，不会加载。停用释放插件作用域，替换 DLL 后仍需重启游戏。

## 服务与生命周期

| 能力 ID | 当前范围 |
|---|---|
| `riz.catalog` | 官方曲目、谱面及难度快照 |
| `riz.scores` | 历史最佳成绩快照，完成率保留游戏原值 |
| `riz.ui` | 简单 UGUI 面板，ID 自动加入插件命名空间 |
| `riz.resources` | 工具面板图标 PNG 覆盖，RGB/RGBA8、非交错、每边不超过 64 像素 |
| `riz.game-resources` | 枚举和覆盖游戏场景内真实 `Image.color`，排除工具自身 UI |
| `riz.scenes` | 当前活跃 Unity 场景快照及切换观察 |
| `riz.runs` | 普通模式游玩开始标记和实际单局结果观察 |
| `riz.extensions` | 查询、订阅已声明的强类型版本化观察点 |
| `riz.capability-metadata` | 查询当前能力的版本、可用性代次、线程、清理责任及采样语义；不激活尚未实现的服务 |
| `riz.practice` | 尚无已验证实现，查询返回不可用原因 |

能力随加载和场景变化出现或撤回，宿主协调所有插件。希望启用但条件未满足的插件等待，条件满足后启动；用户明确停用后不会因能力恢复重新启动。同步回调故障停用故障插件及依赖者，故障插件等待显式启用重试。

生命周期、服务访问及游戏回调在 Unity 主线程执行。服务创建的面板、覆盖句柄和扩展点订阅自动归属当前启用作用域，停用时逆序释放；插件自建资源仍可用 `context.Own(...)` 登记。旧作用域的服务与回调不能继续修改游戏。

真实资源目标包括当前会话的 `game.ui/image/<instanceId>/color`；游戏主菜单对象就绪后增加 `game.menu/button/<pageIndex>/color` 别名，同一对象的别名共享一个覆盖栈。请通过 `GetTargets()` 查询，实例 ID 在重启或对象重建后可能改变。高优先级覆盖生效，相同优先级后注册者生效；撤回上层恢复下一层，全部撤回恢复捕获的颜色基线。写入后读取实际颜色确认，目标销毁后清理句柄；未被覆盖的对象不会回写旧颜色。

新宿主的 `status.json` 另含 `capabilityDetails`、`dispatcher` 和 `diagnostics`。结构化日志按会话保存在 `diagnostics/`，轮转与限流；兼容文本 `toolbox.log` 也限制大小。`Validate-Runtime` 按会话采集，不再依赖旧文本文件的字节偏移。

## 游戏事件边界

当前观察点是 `game.scene.changed`、`game.run.started`、`game.run.finished`，对应普通 DTO `GameSceneChanged`、`RunStarted`、`RunFinished`。Adapter 每 250 ms 在主线程读取真实场景、游玩标记和新 `InsideLevelResult`。首次观察已有结果只建立基线，后续新结果发布一次；开始与结算尽可能共享 RunId，无法取得的 ChartId 为 null。

`RunFinished` 复制单局 Score、RawCompletion、Perfect、MaxPerfect、Bad、Miss、Crashed 和 Skipped，不以历史最佳成绩推断游玩。采样可能漏掉两个观察点之间的短暂状态。精确 GetJudge/Pause/Resume 观察属于独立实验 Hook 模块，不能把该采样器当逐音符回调；普通 SDK 不开放任意参数或返回值修改。已知构建的 Workshop 谱面解析替换有单独受控入口与显式实验选项。旧 `RunCompleted` 保留，当前适配器不发布。

## 配置、命令和自测

`BepInEx/RizToolbox/settings.json` 日常示例：

```json
{
  "enableExamples": false,
  "runSelfValidation": false,
  "disabledPlugins": []
}
```

`enableExamples` 和旧 `disabledPlugins` 仅用于没有启用意愿文件时的初始选择。正式启停会写入 `activation.json`，显式停用跨成功重启保留；已有配置不会因升级而强制开启示例。单插件旧配置通过 `context.Configuration.Load/Save` 按插件隔离；新的 schema 存储和语言服务见 SDK 文档。

`toolbox.log` 记录加载、生命周期和适配错误；`status.json` 包含状态与启用意愿、构建信息、能力、扩展点和坏包诊断。向同目录 `command.json` 写入命令，每次使用新 ID：

```json
{ "id": "disable-high-1", "action": "disable", "pluginId": "example.color-high" }
```

支持 `enable`、`disable`、`status`，后台读取后交主线程执行并持久化启用意愿；`set-language` 加 `language` 字段可切换本次进程的插件语言，长期默认语言在 settings 的 `language` 中设置。

开发自测需显式启用 `runSelfValidation`，并在同目录提供 `validation-plan.json`：

```json
[
  { "atSeconds": 5, "action": "disable", "pluginId": "example.color-high" },
  { "atSeconds": 10, "action": "enable", "pluginId": "example.color-high" },
  { "atSeconds": 15, "action": "shutdown" }
]
```

可在 settings 中设置 `validationRequiredCapabilities: ["riz.game-resources"]`，能力就绪后开始计划计时。`shutdown` 会清理插件、恢复资源并关闭测试游戏。测试结束恢复 `runSelfValidation: false`。

新增模块的接口和限制见 [精确 Hook](docs/Exact-hooks.md)、[生命周期](docs/Lifecycle.md)、[玩法](docs/Gameplay-primitives.md)、[视觉](docs/Visual-resources.md)、[UI/输入](docs/Ui-input-navigation.md)、[音频/内容](docs/Audio-content.md)、[游戏设置](docs/Game-settings.md) 与 [性能](docs/Performance.md)。公开接口与离线通过不表示对应游戏能力已经实测验收；运行时仍须查询操作和不可用原因。编写插件见 [SDK 文档](docs/SDK.md) 与 [模板说明](docs/Authoring.md)，详细要求见 [规格](.docs/compatibility-foundation/SPEC.md)。

## 验证与限制

验收分别记录离线测试、游戏运行和未验证行为，见 [任务清单](.docs/compatibility-foundation/CHECKLIST.md) 与 [底层补全计划](.docs/foundation-expansion/README.md)。检查覆盖能力晚就绪、用户停用、故障恢复、自动清理、配置、观察点顺序、依赖隔离、PNG 输入、游戏状态边沿和构建配置。

两个独立颜色插件覆盖同一真实目标的恢复流程已验证。2026-10-19 的实际游玩捕获了场景切换及失败结算，但开局漏报；Boolean 读取修复已安装，开局/结算关联、正常结束和重试仍需复测。离线测试与菜单启动不能替代真实单局验证。

完整底层仍未验收：真实游戏首次执行准入、任意非空 listener 的效果归属、全部未来来源封口及整局实测仍待补齐，候选的生产准入保持关闭。通用纹理、音频、AssetBundle 替换、任意 IL2CPP/Harmony 补丁、参数/返回值修改、练习、完整工坊适配和跨语言 IPC 仍待扩展。依赖隔离不构成同进程插件的安全沙箱。

## 参与贡献

见 [CONTRIBUTING.md](CONTRIBUTING.md)。请勿提交游戏文件、游戏内存或反汇编导出、闭源依赖、构建输出、密钥或个人日志；`docs/validation` 只保留每个验收批次的 `README.md` 摘要。

## 开发进度记录（历史，按时间倒序）

以下批次保留历史证据，不构成对最新源码的统一验收；最新状态以 [一页进度](.docs/PROGRESS.md) 与 [实现状态](docs/IMPLEMENTATION-STATUS.md) 为准。条目中的 `work/`、`artifacts/`、`.tools/` 以及 `docs/validation` 下原始捕获的链接指向开发机本地证据，未随仓库发布；仓库只保留各批次的 `README.md` 与文字摘要。

2026-11-01 当前主树：MusicPlayer与播放位置写入会在实际store前撤旧控制票；SourceV3整合的27套2465/2465、六DLL Release、solution零错误/零警告、SDK双插件14/14及专项8模式728通过。[本轮证据](docs/validation/playback-set-time-writer-20261101/README.md)。下一在独立目录补初始化跨协程的来源重捕获，完整Seek与生产准入仍未完成；没有安装或启动游戏。

2026-10-30 历史整包冻结已核对：float 事件正式发布与实际列表／逐监听器采集已接通，有值控制请求、固定时间单位转换和资源持续绑定已合入。25 套托管回归 **2233/2233**、两个独立插件 **14/14**、验包 **32/32**、合成安装恢复 **26/26** 通过；候选含 24 载荷 / 31 证据。[本批范围与候选](docs/validation/foundation-single-v7-20261029/README.md)。后续订阅退出、[轨道/音符外观](docs/Game-appearance.md)及采样时钟映射已各自通过独立检查，正在合入下一全树冻结。

2026-10-30 最新 live 树：局内相机 X 标量已正式接入 `RizlineAdapter` 与 `Build.ps1` 托管矩阵；相机专项 **33/33**，整树 **27 套 2369/2369**，`dotnet build RizToolbox.sln -c Release` 0 error。[接入证据](docs/validation/game-camera-integration-20261030/README.md)。相机仍为 experimental/modifiesGameplay；`CameraControl.Update` 每帧覆写 X，实机 update/render 次序、可见效果与输入投影尚未验证。

<!-- ENGINE-PUBLICATION-LATEST:BEGIN -->
最新：[V4 原生发布与同进程 CLR 联合验证](docs/validation/foundation-engine-publication-20261025/README.md)。34 个主要实际进程场景符合预期：6 正常/托管、26 声明启动故障、2 动态加载拒绝；故障原件另通过 2035 条离线断言。18 个写入后失败场景有父观察器完整函数体与页保护读回。真实 Doorstop→Bootstrap 与生产协调器尚未接入，对象/GC/调度取消和资源释放仍未全部验收，新候选未安装。下一步直接推进[生产接线施工表](docs/validation/foundation-engine-publication-20261025/next-production-admission-review.md)，游戏实测仍按用户安排稍后进行。
<!-- ENGINE-PUBLICATION-LATEST:END -->

以下为上一批 Doorstop 候选的历史时点，不能将候选 WinHTTP 接线和本批自有 V4 联合链混写成游戏接入已完成。

<!-- DOORSTOP-COLD-LATEST:BEGIN -->
最新补充：[实际Doorstop冷启动接线已验证](docs/validation/foundation-doorstop-cold-20261025/README.md)。session80976实际exit0：3正常冷启动+2错误部署拒绝、原21转发回归、旧默认Doorstop构建均通过。82名称/ordinal保持，候选真实DllEntry初始化先于自有Importer的TLS/C/C++/DllMain；626源/8二进制/585原件归档。固定游戏四引擎入口profile事实已补全。尚未安装或运行游戏，生产V4/新核只读采用、对象GC、调度、取消与全部资源退休仍待完成；B01–B19状态不变。
<!-- DOORSTOP-COLD-LATEST:END -->

以下为前一批独立转发组件的历史时点；其“尚未接入Doorstop”已由上述候选接续，仍未接入真实游戏。

<!-- WINHTTP-FORWARD-LATEST:BEGIN -->
最新补充：[固定系统 WinHTTP 转发已通过独立宿主验证](docs/validation/foundation-winhttp-forward-20261025/README.md)。root实际session40901/exit0，21进程、5653父核验；613声明源/4二进制/260原件归档。TLS与C/C++入口保留问题已修复，CFG保持。该组件尚未接入生产Doorstop或游戏；下一步是生产首次发布与新核采用，然后对象GC、调度、取消和全部消费者退休。此前1617属于旧完整构建，本批未重跑；游戏实测仍稍后，B01–B19验收状态不变。
<!-- WINHTTP-FORWARD-LATEST:END -->

以下新核启动链为本批之前的历史时点，其中关于WinHTTP尚未实现的描述已由上方接续。

<!-- EARLY-KERNEL-LATEST:BEGIN -->
- **最新原生集成：新 Kernel 自有启动链已接通。** [本批原件](docs/validation/foundation-early-kernel-20261025/README.md)。root 实际观察 session17459 终态0；新链73/13248断言、旧发布器57、旧链73/13031断言均通过，共203个自有宿主；604项声明输入和18条二进制按7+4+7隔离归档。本批没有重跑上一批25套1617，23项包字节保持。
- **实际修复与证据**：独立 publisher 记录2、wire3与helper ABI2/scalar1分层；复用同一核心代码，固定模块/入口/epoch不混用。调试观察改为有界映射身份与线程硬件返回断点；3个错栈经过保留责任、后续真实RSP返回清槽。正式debug为5Load/0Unload，另3次强化预飞实际各6Load/1Unload；1007未命中，不计覆盖。首批31803与失败诊断原件保留。
- **直接下一步**：实现固定系统WinHTTP转发及生产启动凭据，再接新核的CLR只读采用、coordinator/真实对象GC、scheduler/async/Start内联清理和全部消费者资源退休；补完整Retry/Return/Loader。新链仅用于自有宿主，真实游戏仍是旧安装，18安装项/4原始文件hash一致；未安装或运行游戏。B01–B19仍active，仅B03整项/B18a子项验收，EngineCancelled=false、Pending保留、RequestPlay关闭。
<!-- EARLY-KERNEL-LATEST:END -->

下方为上一批独立核完整构建的历史时点；其“下一步”已由上方接续，不代表当前固定链仍未实现。

<!-- ENGINE-KERNEL-LATEST:BEGIN -->
- **最新完整构建：独立协程运行核已实现并验证。** [本批证据](docs/validation/foundation-engine-kernel-20261025/README.md)。session23528实际exit0，25套1617/1617，599项声明源/23项包冻结。新Kernel仅39424字节、8个ABI2导出和2项系统导入，无入口/TLS/CRT/Dobby直接依赖；canonical入口表改为typed constexpr，旧helper继续独立回归。
- **实际范围**：新核7进程：128入口与256拒绝、1606宿主检查/1486父断言；原生3402+五托管889检查，180原调用/152配对。旧39早期宿主/7316另计。31项输入在新核运行期间持只读句柄且hash不变；后续另一测试正常复制旧helper的一项变化及准确旧文件恢复已单列。94条去重编译诊断保留，未声称零warning。
- **接下来**：新Kernel尚未由生产或固定早期链采用；下一批独立绑定它，再实现真实WinHTTP/首次发布与V4、coordinator/对象GC、scheduler/async/Start内联清理和全消费者退休。新包未安装，游戏实测稍后。完整B01–B19仍active，仅B03整项/B18a子项验收；EngineCancelled=false、Pending保留、RequestPlay关闭。
- **安装补充已修复并实测**：[独立证据](docs/validation/foundation-kernel-install-20261025/README.md)。新23包在旧22项白名单下的真实拒绝已复现；精确加入Kernel后，自有临时目录16项安全检查和13项恢复检查通过。三脚本覆盖修复与原核心构建分列，现行599源码另有组合归档，其余596项不变；没有再次宣称重跑1617，也未操作真实游戏目录。<!-- KERNEL-INSTALL-SUPPLEMENT -->
<!-- ENGINE-KERNEL-LATEST:END -->

### 上一批固定发布早期链（历史）

<!-- EARLY-FIXED-LATEST:BEGIN -->
- **历史：无 Dobby 的固定发布早期链已接通，73 个独立进程通过。** [本批证据](docs/validation/foundation-early-fixed-20261025/README.md)。root 观察 session34351 实际 exit0，14 项输入/7 个二进制已冻结；全 Prepare→4 Configure→Bind→4 Commit/Ready 早于真实 TLS/C/C++/DllMain 首调用。正常86/worker92次原调用，各56观察配对，包含真实前缀副作用和原异常。
- **本轮修复与验证**：Capture Busy/错误现永久关闭原生准入，真实受控递归及后续捕获均不能误放行；独立未发布进程建立同 binary/RVA 的 SEH/C++ raw flags 基线。73 个进程含56内部故障、实际1字节部分写和17条调试补充观测；API结果注入、跨 child 基线和仪器化边界均单列。上次完整25套1617/1617仍属于99557，本批未重跑托管套件。
- **接下来**：实现实际游戏的系统 WinHTTP 转发与可信首次发布/独立 V4，再接 coordinator/对象GC、scheduler/async/Start内联清理和全消费者退休，补完整 Retry/Return/Loader。游戏实测稍后，本批未安装；B01–B19 继续，仅 B03 整项/B18a 子项验收，EngineCancelled=false、Pending保留、RequestPlay关闭。旧576/12声明源、已装18文件/326源ZIP和4项原游戏hash已重新核对。
<!-- EARLY-FIXED-LATEST:END -->

### 上一批独立固定组件（历史）

<!-- FIXED-PUBLISHER-LATEST:BEGIN -->
- **历史：固定四入口最小发布组件已实现，57个独立进程通过。** [本批证据](docs/validation/foundation-fixed-publisher-20261025/README.md)。12项组件输入/4二进制独立冻结，session5523实际exit0；已去掉该发布路径对Dobby、动态路由、堆和CRT日志的依赖，原函数桩重放真实非NOP前缀。34原调用、20观察配对、16异常捕获；57份实际页快照、2850个控制展开上下文均有独立核对，控制上下文不等于逐PC真实异常。
- **状态分列**：上批完整25套1617/1617与39个早期宿主属于session99557的576源/22包归档；本批未重跑这些托管套件，也未把最小组件接进早期loader。FF25尾部实际展开失败后改R11续接，SEH flags改用未修改真实基线精确对比，失败原件保留。56故障中3类API失败是受控注入，实际partial3字节另有原件。
- **仍缺**：无Dobby早期宿主的同链验证、并发首写/实际游戏启动点、系统WinHTTP转发/独立V4、生产coordinator和对象GC、scheduler/async/Start内联清理、全部消费者资源退休及完整Retry/Return/Loader。新组件未安装；完整B01–B19 active，仅B03整项/B18a子项验收，EngineCancelled=false、Pending保留、RequestPlay关闭。游戏实测稍后。
<!-- FIXED-PUBLISHER-LATEST:END -->

### 历史完整构建：旧早期宿主

<!-- EARLY-LOADER-LATEST:BEGIN -->
- **最新：自有早期加载宿主已实现并纳入完整构建。** [本批结果](docs/validation/foundation-early-loader-20261025/README.md)。25套1617/1617离线回归；另39个独立子进程、7316条父断言通过，两组计数分列。576项声明源码/22项包冻结，新包未安装。
- **本批证据**：真实静态依赖链，四目标发布早于Importer的TLS/C/C++/DllMain；normal86与worker92次原调用，实际嵌套/worker/EAX/原生异常及加载后Attach；迟到已有worker拒绝和24处API边界故障已验。额外debugger子进程观察helper/Dobby真实初始化返回，明确属于内存断点仪器化证据。原件合计324次原调用/168观察配对/24异常捕获，完整构建session99557实际exit0，保留94条去重原始编译诊断。
- **仍缺**：游戏的生产早期安装、系统WinHTTP转发及独立V4凭据、coordinator实际对象/GC接线、scheduler/async/Start内联清理和全消费者资源退休。Dobby的CRT堆、日志及未解间接路径仍不具备生产loader安全证明，下一段实现固定四入口最小发布器。若干拒绝项是受控expected/selector条件，24故障不等于Dobby内部OOM/写入中断。EngineCancelled=false、Pending保留、RequestPlay关闭；完整B01–B19继续，仅B03整项/B18a子项验收。游戏实测稍后；旧1021安装18项/326旧源ZIP与原游戏3hash匹配。
<!-- EARLY-LOADER-LATEST:END -->
