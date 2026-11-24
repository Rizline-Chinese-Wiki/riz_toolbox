# InFalsusTools 架构参考

2026-10-22。参考仓库：[Anling20/InFalsusTools](https://github.com/Anling20/InFalsusTools)，固定审阅提交 `98e910339fd7cd7e55a364221ead115bd031151f`。主代理与一个子代理完成源码静态对照，未构建或运行第三方项目。普通工程架构报告，flavor=null。

**结论：借鉴数据与算法复用、配置独立交付和集中操作入口。继续沿用现有C#兼容宿主、SDK和插件体系，把业务策略放到插件层；当前优先补完整LOCAL启动/结果事务。**

## 2026-10-23 复查与当前决定

最新工程状态以[1515原生启动安装批](validation/foundation-preinit-producer-20261024/README.md)为准：已接实际Doorstop pre-init publisher与托管采用路径，固定映像三处发布在不执行游戏代码的进程中通过；真实CLR同进程采用、冷启动、listener/GC、动态转交/取消、全部消费者退休和完整Retry/Return/Loader仍缺。新包未安装，RequestPlay关闭。上层架构参考不计为我方底层验收。

用户再次提供该仓库后，重新查看其 README，并复核固定提交的算法契约、CLI/GUI Dispatcher 与悬浮窗源码。它提供外部内存读取、存档记录、潜力算法及展示工具；没有从这些文件确认通用进程内 Mod 宿主、插件资源作用域或原生消费者退休协议。当前参考的是明确可复用的设计，未运行或复制第三方实现。

| 我们采用的设计 | 对应现有层 | 应保持的边界 |
|---|---|---|
| 一份曲目/成绩快照供窗口、命令、导出复用 | SDK 契约、Adapter 采集、Loader 命令/状态 | 展示入口消费服务，避免每个界面另读游戏内存 |
| 算法、定数和显示方案可独立替换 | Hosting 独立插件包、Core 服务与存储 | 评分/规则是插件策略，不写入 Runtime 的 Hook 与生命周期内核 |
| 游戏地址和版本资料独立维护 | Adapter profile 与 Runtime 元数据/ABI 核验 | 配置文件提供候选绑定；实际构建指纹和签名通过后才可调用 |
| 集中操作和设置入口 | 既有 ToolboxHost 快照、命令、设置服务 | 窗口展示能力可用性和失败原因，复用同一个宿主 |

继续使用 C#，维持 `SDK → Core/Hosting → Runtime/Adapter → Loader` 的现有职责划分；暂不增加第二套脚本运行时或独立配置内核。优先完成 `启动 → 游玩 → 结果 → 重试/返回 → 资源结束` 的完整链，再在这些公共接口上扩展上层工具。

首次审阅时的 1038 状态保留在下文，属于历史记录。此后 [1266 冻结批](validation/foundation-actual-launch-20261023/README.md)已增加可调用的生产启动协调器、激活/首次转场、设置核验和结果 sink；[当前返回原语批](validation/foundation-return-primitives-20261023/README.md)继续补返回场景与精确 Boolean 参数观察。Loader 仍未装配整链，活动局消费者退休、菜单锁/相机交接和完整 Retry/Return 命令仍缺。以[当前检查点](../.docs/CURRENT-CHECKPOINT.md)为准，不把上层工具架构参考计为底层验收。

## Evidence → Finding → Path

| Evidence | Finding | 我方落点 |
|---|---|---|
| [算法契约](https://github.com/Anling20/InFalsusTools/blob/98e910339fd7cd7e55a364221ead115bd031151f/src/main/kotlin/potential_algorithm/PotentialAlgorithm.kt)、[记录模型](https://github.com/Anling20/InFalsusTools/blob/98e910339fd7cd7e55a364221ead115bd031151f/src/main/kotlin/model/Models.kt)、[CLI/GUI入口](https://github.com/Anling20/InFalsusTools/blob/98e910339fd7cd7e55a364221ead115bd031151f/src/main/kotlin/command/Dispatcher.kt) | 记录、算法结果供界面、命令行、导图复用；算法有统一类型 | 已有SDK目录/成绩/事件/服务契约。后续工具消费同一份快照和服务，不在各界面重复采集游戏数据 |
| [模型中的可替换文件](https://github.com/Anling20/InFalsusTools/blob/98e910339fd7cd7e55a364221ead115bd031151f/src/main/kotlin/model/Models.kt)、[算法加载器](https://github.com/Anling20/InFalsusTools/blob/98e910339fd7cd7e55a364221ead115bd031151f/src/main/kotlin/potential_algorithm/script/Loader.kt) | 曲目定数、地址、算法等可从文件替换，算法加载要求返回指定类型 | 数据/内容用现有Storage和provider；逻辑策略用现有DLL包和版本化服务。用户配置与游戏绑定分开 |
| [设置状态绑定](https://github.com/Anling20/InFalsusTools/blob/98e910339fd7cd7e55a364221ead115bd031151f/src/main/kotlin/setting/ComposeStatedReadWriteProperty.kt) | 设置和工具窗口有清晰的使用入口 | 我方ToolboxHost已有settings.json、command.json、status.json及模块/性能/资源快照。需要界面时在其上做薄展示，不新增监测与配置内核 |
| [进程内存读取](https://github.com/Anling20/InFalsusTools/blob/98e910339fd7cd7e55a364221ead115bd031151f/src/main/kotlin/ProcessMemory.kt)、[悬浮窗场景检测](https://github.com/Anling20/InFalsusTools/blob/98e910339fd7cd7e55a364221ead115bd031151f/src/main/kotlin/window/potentialWindow.kt) | 外部只读进程，默认500ms轮询，结果页进入后再等1秒读取存档 | 可用于辅助展示的思路参考；我方LOCAL必须在原Start保存前建立精确receiver归属并接管，轮询不能提供这个时序保证 |

## 直接采用的架构原则

1. **一套契约，多种入口。** SDK输出普通数据、事件与服务；界面、命令行、报告工具复用它们。已有ICatalogService、IScoreService、IPluginServices及scope/代次机制，不重造一套工具专用数据层。
2. **基础能力与策略分别交付。** Core处理作用域、依赖、存储、仲裁和调度；Runtime/Adapter处理游戏绑定。评分算法、规则转换、显示方案等由插件实现；它们不进入原生生命周期内核。已有包加载器与服务注册足以承载第一版，暂不引入新的脚本运行时。
3. **数据替换不等于任意地址替换。** 用户内容/设置按命名空间和schema处理；游戏适配仍经过构建指纹、metadata、ABI和模块探测。参考的address.json不直接成为我方公共原生地址接口。
4. **已有后端优先组合。** 后续如需要管理界面，集中展示能力是否可用、失败原因、依赖、设置和性能；复用ToolboxHost现有快照/命令。它是展示需求，当前不扩成新的独立产品任务。

## 已有实现与尚缺实现

| 部分 | 我方已有源码 | 当前决定/缺口 |
|---|---|---|
| SDK数据与跨插件服务 | [Contracts.cs](../src/RizToolbox.Sdk/Contracts.cs)、[ServiceContracts.cs](../src/RizToolbox.Sdk/ServiceContracts.cs) | 继续通过共享契约复用，现有服务不是新待办 |
| 包与生命周期 | [PluginPackageLoader.cs](../src/RizToolbox.Hosting/PluginPackageLoader.cs)、[PluginScope.cs](../src/RizToolbox.Core/PluginScope.cs)、[ModuleManager.cs](../src/RizToolbox.Adapter/Modules/ModuleManager.cs) | 已有包隔离、scope、依赖撤回及有界探测，不为模仿参考重做 |
| 配置/状态后端 | [ToolboxHost.cs](../src/RizToolbox.Loader/ToolboxHost.cs) | 后端已存在，集中前端是可选后续使用方式 |
| 版本适配 | [BuildProfiles.cs](../src/RizToolbox.Adapter/BuildProfiles.cs) | 保持按SHA选择已验证profile；偏移文件可配置不能单独证明版本兼容 |
| LOCAL实际事务 | [接线计划](validation/local-launch-20261022/next-activation-transaction.md) | 仍缺首次结果receiver归属、实际参数初始化/激活、globals恢复、完整结果/重试/返回及消费者结束验证 |

参考脚本的Define使用wholeClasspath，Loader同步host.eval后检查返回类型。没有从这些源码确认与我方scope、能力撤销、故障归属等价的接入；因此不能把直接移植脚本执行器当作省工方案。保留“算法有统一接口且可替换”的设计，通过现有插件/服务实现。

本轮仅增加架构参考文档与决策说明。1038暂存包、329输入源码归档及当前1021安装均保持各自边界；未做新游戏输入或安装，不新增B01–B19勾选。现有源码回归未因纯文档修改重跑。后续仍按[当前检查点](../.docs/CURRENT-CHECKPOINT.md)与用户“实测稍后”的安排继续。
