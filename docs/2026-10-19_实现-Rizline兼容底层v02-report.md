# Rizline 兼容底层 0.2 实现与验收

2026-10-19，Asia/Shanghai。框架 0.2.0，SDK 契约 1.1.0。授权与范围沿用 [scope.md](scope.md) 和用户“写详细任务然后开始实现”的请求；任务规格见 [.docs/compatibility-foundation](../.docs/compatibility-foundation/README.md)。普通技术实现报告，flavor=null。

## 结果

本轮完成插件宿主与游戏适配的分层、插件包依赖隔离、能力变化协调、服务作用域与自动归属、插件配置和强类型观察扩展点。宿主不再引用示例项目，也没有 `example.scores` 启动分支；五个样例都以普通独立包加载。已在本机游戏启动流程验证两个独立插件共同覆盖同一真实游戏 Image，停用上层恢复下层，全部停用恢复原值，重新启用及停用下层也符合优先级规则。最终启动自测无 Error，原始 Rizline.exe 和 GameAssembly.dll 哈希保持不变。真实游玩的场景切换、开局、结算尚未验收；观察器代码和状态去重检查不代替实际游玩。

## 详细任务与实现

| 模块 | 实现位置 | 实际行为 |
|---|---|---|
| SDK | src/RizToolbox.Sdk | 契约、普通 DTO、每插件配置、有版本观察点、真实游戏资源接口 |
| Core | src/RizToolbox.Core | 依赖、启用意愿、Waiting/Faulted、能力撤回、作用域、代际校验、自动清理 |
| Hosting | src/RizToolbox.Hosting | 独立目录包、预检清单与引用、每包 ALC、私有依赖隔离、共享 SDK |
| Runtime | src/RizToolbox.Runtime | 单一进程原生入口、IL2CPP API、缓存 tick 方法指针，插件不接触 native 指针 |
| Adapter | src/RizToolbox.Adapter | 构建配置、游戏服务、场景/普通模式观察、真实 UI 资源目标 |
| Loader | src/RizToolbox.Loader | Doorstop 入口、组合、包发现、命令、状态和外部开发验证计划 |

配置路径为 `BepInEx/RizToolbox/config/plugin-<id>/config-<name>.json`。服务自动包装为作用域视图，停用与能力代际变更立即使旧视图失效。面板、纹理/颜色覆盖及扩展订阅自动登记，插件自建资源仍通过 Own 管理。用户明确停用的依赖不会被其他插件或晚就绪能力悄悄重启；故障插件需要显式启用重试。

每个包包含 plugin.json、入口 DLL 和私有依赖。SDK 由宿主共享，包禁止携带宿主及框架运行时替代程序集。程序集上下文仅作为依赖解析边界，不构成同进程安全沙箱；释放上下文不保证完整热卸载。旧平铺 DLL 被明确诊断并要求迁移。

SDK 1.x 的程序集绑定身份固定为 1.0.0.0，接口功能版本通过 SdkInfo.Version=1.1.0 和清单范围控制。框架文件版本仍为 0.2.0。兼容的小版本更新保留绑定身份，破坏性修改必须升级 SDK 主版本。

游戏适配配置位于 `src/RizToolbox.Adapter/profiles/steam-25977441.json`。当前仅验证一个构建，未知指纹拒绝启用。新增游戏支持应集中维护配置和 Adapter，不向普通插件暴露固定 RVA。Runtime 首次绑定 GameManager.Update 后以指针比较分发，避免对全局 runtime_invoke 每次重复解析字符串。

## 真实资源和事件边界

资源服务枚举游戏中已有的活跃 Image，排除 RizToolbox 根节点。会话键为 `game.ui/image/<instanceId>/color`；主菜单控制器就绪时可增加稳定菜单别名，同一对象共享同一个覆盖栈。优先级高者生效，同优先级后注册者生效；释放层后恢复下一层或捕获的原始颜色。目标销毁时解除所有别名并释放根句柄。只对实际被覆盖的目标恢复颜色，避免回写未被修改对象的旧动画/主题值。

最终真实目标来自游戏 `EdgeCoverCanvas`，会话键记录在 runtime.log，不依赖固定实例编号。两个插件明确选择边缘遮罩，避免覆盖输入阻挡层或整屏遮罩，且未手动 Own 句柄，自动作用域清理仍完成恢复。启动页不包含已就绪的主菜单，菜单别名的真实出现不在本轮启动验收范围。

观察点 `game.scene.changed`、`game.run.started`、`game.run.finished` 由 Adapter 每 250 ms 在 Unity 主线程采样。来源分别是 Unity 活跃场景、活跃 NormalModeInLevelController 的 LevelInformation、活跃 LevelOverControl 新的 InsideLevelResult；历史最佳分数变化不作为结算来源。首次观察已有结果建立基线；新结果去重，保留控制器销毁后的 RunId 关联。已结束控制器不会误报开局。

观察点支持确定性顺序、类型与版本匹配、异常归属及停用撤销。它们是观察型扩展点，不提供任意函数的 before/after 原生补丁、跳过原方法或修改参数/返回值。250 ms 内的短暂状态可能漏采。RunFinished 保留 Perfect/MaxPerfect 等独立游戏计数，旧 RunCompleted 仍不发布。

## 验证结果

| 验证 | 结果 | 证据 |
|---|---|---|
| Release 构建 | 0 错误、0 警告 | [build.log](validation/v02/build.log) |
| Core | 24/24，包含晚就绪、撤回、故障、作用域、配置、扩展点及重入 | build.log |
| PNG | 10/10，输入格式、CRC、滤波及边界 | build.log |
| Hosting | 16/16，含同名不同版本、无 TPA、SDK 身份、坏包、链接路径 | build.log |
| Game 状态/配置 | 26/26，边沿、基线、去重、RunId、未知和非法构建 | build.log |
| 安装与卸载 | 12/12，临时目录，不启动游戏 | [install-safety.json](validation/v02/install-safety.json) |
| 真实插件包 | 5 个包独立加载，SDK 身份共享 | [runtime.log](validation/v02/runtime.log) |
| 晚就绪数据能力 | 444 谱面/444 槽位，普通分数插件自动启动导出 | runtime.log |
| 真实颜色共存 | 2→1（低层）→0（原值）→1（高层）→2→1（高层）→0 | runtime.log |
| 主线程与清理 | 初始化、五插件停用、退出码 0、Error 0 | [runtime-summary.json](validation/v02/runtime-summary.json) |
| 游戏原始文件 | 两个原始二进制 SHA-256 不变 | runtime-summary.json |
| 实际场景切换/游玩 | 未触发，不计通过 | summary 的三个 observed 字段为 false |

成绩原始 JSON 留在本机游戏 exports，证据只保留汇总和必要日志。源仓库没有加入个人成绩列表。

复现自动检查：

```powershell
pwsh -File .\scripts\Build.ps1
pwsh -File .\scripts\Test-InstallSafety.ps1
```

关闭现有游戏后复现受控启动、资源覆盖和清理：

```powershell
pwsh -File .\scripts\Validate-Runtime.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline'
```

脚本安装当前产物，保存原设置，临时设置验证计划，仅等待并控制自己启动的进程；计划结束恢复设置。日常 runSelfValidation=false，游戏没有自动关闭计时器。测试计划不自动游玩或制造正式成绩。

## 发现与修复

首次运行揭示 Doorstop 不提供普通 dotnet 主机的 TPA 清单，导致 System.Runtime 误判为私有缺失依赖。Hosting 已改为从运行中的 CoreLib 目录建立具体框架程序集目录，校验版本、公钥身份与实际路径；新增清空 TPA 的回归检查。没有采用按 System 前缀任意放行的方案。

复核另外修复了能力在激活中撤回、订阅故障归属、工厂重入清理、资源别名共用栈、未覆盖对象错误回写、初始化/失败纹理覆盖回滚，以及已结束控制器误报开始。安装检查与游戏运行串行执行，避免运行状态锁影响临时安装检查。

## Evidence → Finding → Path

| Evidence | 来源 | 固定性与范围 |
|---|---|---|
| E-original | 本机 Rizline.exe / GameAssembly.dll | GameAssembly SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，运行后核验 |
| E-imports / E-exports | 前期 PE 清单 `绿豆诡计v2/output/mod-design/2026-10-19/static-evidence.json` | 复用已知 x64 与 IL2CPP API 导入/导出证据，本轮没有新增 IDA 反编译结论 |
| E-symbols | 既有 il2cpp_output/dump.cs：GameManager、MainPageButtonControl、InLevelController、InsideLevelResult、Unity Scene/Resources | 仅作为候选符号，Adapter 运行时查询类型/方法，未知构建拒绝 |
| E-tests | build.log 与 install-safety.json | 76 项自动检查、12 项安装场景 |
| E-runtime | runtime.log / runtime-summary.json / status.json | 五独立包、真实 Image 读回、晚就绪与停用恢复 |

| Finding | category / severity | evidence_ids | status / confidence |
|---|---|---|---|
| F-core-package | design / n/a | E-tests, E-runtime | validated / high |
| F-real-resource | design / n/a | E-symbols, E-runtime | validated / high（启动页实际目标） |
| F-run-observer | design / n/a | E-symbols, E-tests | implemented / medium（尚未实际游玩） |
| F-version-adapter | design / n/a | E-original, E-tests, E-runtime | validated / high（单构建） |

P-02（path_type=callflow）：Doorstop → 指纹选择配置 → Runtime 绑定 Unity tick → Loader 组合 Core/Hosting/Adapter → 五包隔离加载 → 能力探测后调和等待插件 → 两包覆盖同一真实 Image → 外部计划逐层停用、恢复、再启用 → 自动释放资源与插件 → 自测退出。对应 E-runtime 与 F-core-package/F-real-resource。

## 剩余验收

实际进行普通模式开局、结束、重试与离开场景，核对日志 RunId、ChartId、计数和去重；此项完成后才能勾选 F06/F10。验证主菜单控制器就绪后的别名枚举。当前没有通用纹理/音频/AssetBundle 替换、任意原生方法修改、练习、工坊或 IPC；未来新增业务由 Adapter 实现并通过 SDK 能力暴露。
