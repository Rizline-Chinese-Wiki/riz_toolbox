# Rizline PC 底层首版实现与验证

日期：2026-10-19，Asia/Shanghai。版本：原型 0.1.0，SDK 契约 1.0.0。范围与授权见 [scope.md](scope.md)。普通技术实现报告，flavor=null。

首版已经在本机游戏中完成真实接入：Doorstop 启动 CoreCLR、IL2CPP API 桥、Unity 主线程宿主、C# 插件生命周期、UGUI 面板和具名图标覆盖恢复。读取了 444 个唯一谱面条目及 444 个成绩槽位，其中 39 个槽位分数非零；槽位数量不等于实际游玩次数。核心/PNG 检查 20 项通过，安装脚本安全检查 12 项通过。自测进程已退出，日常配置的 `runSelfValidation` 已设为 false。

## 已实现的边界

- SDK 不引用游戏程序集或 native 类型，普通示例仅引用 SDK。
- Core 管理依赖与版本范围、能力原因、启用失败回滚、逆序资源释放、事件顺序、异常处理和有界主线程调度。
- Loader 集中管理导出 API、程序集/命名空间/类型解析、字段读取、static/instance 校验和歧义重载拒绝。特殊重载明确参数类型，不照抄旧 dump 的 native 签名。
- 游戏内 `GameManager.Update` 的 Unity 调用触发宿主 tick；`FrameUpdate` 是当前实际发布的事件。
- 资源覆盖采用优先级栈，释放后恢复下一层或原图；真实验证对 Unity `RawImage.texture` 读回检查。
- 安装清单记录路径和 SHA-256，先登记 pending 再原子写入，拒绝无归属目标、修改文件、越界及符号链接路径。卸载保留修改项与生成数据。

## 接入选择

固定包：BepInEx `6.0.0-be.789+6cea286` Windows IL2CPP x64，SHA-256 见 `dependencies.lock.json`。

先按原设计尝试 BepInEx 全链。现有 metadata 虽有标准头，但 Cpp2IL 在方法定义字符串解析中产生 `EndOfStreamException`，随后缺少 Unity interop 程序集，插件链无法运行。诊断日志保存在本地 `artifacts/validation/bepinex-metadata-failure.log`。

因此复用同一固定运行包的 Doorstop、CoreCLR 和 Dobby，通过运行时导出 API 获取实际类型和对象。最终不运行 BepInEx chainloader，也不依赖离线生成的游戏代理 DLL。无需自写 C++ 宿主，特殊桥接代码集中在 `NativeRuntime` 与 `Bootstrap`；C# SDK/Core 不随这一选择改变。

该选择已在本构建验证，不代表未知 Unity/游戏版本自动兼容。目前用 GameAssembly SHA-256 严格限定适配范围。

## 实际验证

| 项目 | 结果 | 证据 |
|---|---|---|
| Release 构建 | 0 错误、0 警告 | [build-final.log](validation/build-final.log) |
| Core | 10/10，依赖环、版本、失败回滚、事件异常、队列取消等 | 同上 |
| PNG 适配输入 | 10/10，五种滤波、CRC、长度、边界与示例图 | 同上 |
| 安装/卸载 | 12/12，临时目录实际安装更新及恢复 | 本地 `artifacts/script-safety-9442c26189784d6f95d516b4fbb5b61f/results.json` |
| 主线程宿主 | 真正进入 GameManager 生命周期 | [runtime-release.log](validation/runtime-release.log) |
| 历史数据 | 444 个唯一谱面及成绩槽位，39 个非零成绩 | [runtime-validation.json](validation/runtime-validation.json) |
| UGUI | 面板创建成功；未记录截图，不宣称完成视觉布局验收 | runtime-release.log |
| 资源 | 激活 → 停用恢复 → 再激活 → 最终恢复，读回指针一致 | runtime-release.log |
| 原始游戏文件 | Rizline.exe 与 GameAssembly.dll 哈希和分析前一致 | runtime-validation.json |

验证中发现游戏裁剪了 `Application.set_runInBackground` 和无参数 `Application.Quit`。最终代码不依赖前者，并调用已存在的 `Quit(int)`；初始化最多尝试一次，避免失败时反复创建宿主。最后一轮运行日志没有 Error，插件按序停用，自测进程正常退出。

导出包含实际用户成绩，原始成绩 JSON 留在本机游戏 `BepInEx/RizToolbox/exports`，未加入仓库。入库证据只保留汇总和加载日志。

## 尚未实现或尚未覆盖的部分

- 仅有一个游戏构建适配，尚无跨版本自动迁移。
- 任意原生/第三方插件共存未验证；安装器会拒绝其他加载器占用。
- 资源能力当前只支持具名面板图标槽，不支持全游戏纹理、音频或 AssetBundle 通用替换。
- 单局结算采集、练习操作、工坊内容、自定义谱面和 IPC 尚未实现。`RunCompleted` 仅为预留契约。
- 普通插件不具备程序集热卸载；停用释放作用域，替换程序集后重启。
- 未自动操作游戏进行两局游玩，未逐首对照 UI 分数；数据字段依据已存在的游戏对象读取，完成率保持原值。
- 设置目前包括加载器设置文件与插件自身逻辑；统一的每插件配置编辑界面尚未提供。

## Evidence → Finding → Path

| Evidence | 来源与复现 | 固定性 |
|---|---|---|
| E-imports / E-exports | 前期用户指定本体的 PE 清单位于 `C:\Users\71957\Desktop\绿豆诡计v2\output\mod-design\2026-10-19\static-evidence.json`：x64，包含 IL2CPP 查询/调用 API；导入含文件、网络、随机数及动态库加载。此次复用既有清单，没有新增反编译结论 | GameAssembly SHA-256 在 lock 中；运行后再次核验 |
| E-build | `pwsh -File scripts/Build.ps1`；构建和 20 项检查 | validation/build-final.log；产物哈希见 validation/package-manifest.json |
| E-runtime | 安装后开启开发自测运行游戏；读取主线程、能力与资源日志 | validation/runtime-release.log、runtime-validation.json |
| E-install | `pwsh -File scripts/Test-InstallSafety.ps1`，在临时目录验证安装脚本 | 本机 artifacts 中的 12 项结果 |
| E-originals | 验证前后比对游戏原始二进制 SHA-256 | validation/runtime-validation.json |

| Finding | severity / category | evidence_ids | status / confidence | location |
|---|---|---|---|---|
| F-host | n/a_re / design | E-exports, E-build, E-runtime | validated / high | Bootstrap、ToolboxHost |
| F-ui-resource | n/a_re / design | E-build, E-runtime | validated / high（仅对象创建和读回） | UnityUiService |
| F-data | n/a_re / design | E-exports, E-runtime | validated / high（数据读取，未逐首 UI 对照） | GameDataService |
| F-install | n/a_re / design | E-install, E-originals | validated / high | Install/Uninstall 脚本 |

P-01（path_type=callflow）：Doorstop 调用 Entrypoint → 在固定构建安装 runtime_invoke 桥 → Unity 调用 GameManager.Update → 主线程构造 Core/能力服务 → 插件通过 SDK 创建面板与覆盖图标 → 成绩加载后注册数据能力并导出 → 停用依赖与资源，读回原图 → 自测退出。各步见 E-runtime、E-build，对应 F-host/F-ui-resource/F-data。

残余限制为上述未实现项目和 native 桥的构建约束；同进程插件异常管理不构成进程隔离。

时间线：克隆空仓库 → SDK/Core 实现与测试 → BepInEx metadata 生成失败定位 → Doorstop API 桥 → 实际 UI/数据/资源验证 → 安装恢复和路径保护 → Release 回归与日常设置恢复。
