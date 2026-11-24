# 底层剩余任务与接手顺序

核对：2026-10-21。仓库 `C:\Users\71957\Desktop\riz_toolbox`，游戏 `D:\SteamLibrary\steamapps\common\Rizline`。本文件是执行交接，验收标准仍为 [SPEC](SPEC.md)；不改变 [CHECKLIST](CHECKLIST.md) 的勾选。**19个整项仅B03已验，B18a为已验子项；766项通过不能换算为完成比例。**

**当前安装与artifacts产物已更新为767包**（25项目、0编译警告/错误、Visual69），见[新包验证](../../docs/validation/retirement-receipt-20261021/README.md)。18安装hash与256源码一致，游戏保持退出、probe仍Absent。新增退休成功诊断与日志故障回归，没有新游戏会话；下段766是已冻结的历史实机基线，其旧manifest不能拿来校验当前artifacts。
766包已构建、安装并完成局部实测，25个检查项目、0编译警告/错误，构建时18个安装文件哈希一致、256项源码输入一致。包在 `artifacts/package`，SDK在 `artifacts/sdk/RizToolbox.Sdk.1.2.0.nupkg`。[实机日志已冻结](../../docs/validation/runtime-color-content-20261021/evidence-snapshot-20261021T044942Z/README.md)：真实Pause/Resume、暂停时钟、Retry与同谱新RunId、一次hold.end、TMP发现/基线读取及Float材质应用/托管租约释放；没有正常Completed或材质物理释放证明。**游戏已退出，临时probe已恢复原Absent状态**，见[恢复记录](../../docs/validation/runtime-color-content-20261021/module-probe-restoration.json)。后续原生退休成功receipt源码属于下一包，不能改写766证据。

## 先保存现场，再继续

1. 读取 [.docs/CURRENT-CHECKPOINT.md](../CURRENT-CHECKPOINT.md)，核对实际游戏进程、最新诊断session、安装清单；不要复用历史窗口句柄。766实测已结束，下次GUI由主代理统一操作。
2. 本轮临时探针配置原本不存在，现已恢复：`D:\SteamLibrary\steamapps\common\Rizline\BepInEx\RizToolbox\config\plugin-example.panel\config-runtime-module-proof.json`。宿主改写了格式/编码，恢复前已逐字节备份并核实仅含Enabled=true，随后恢复原Absent状态；完整过程见恢复记录，不要重复删除或重建该文件。
3. 大部分源码尚未纳入Git跟踪。保留工作树、旧失败证据和现有产物；不能用HEAD覆盖、clean或把旧包日志充作766证据。没有代码变动时不必重建现有已核对的766包。

## 覆盖现状

“已实现”只表示存在执行路径；“离线”指针对对应源码的回归；“实机”严格限已有包和流程；末列是继续交付所缺内容。

| 任务 | 已实现／离线 | 已有实机范围 | 缺失或待收口 |
|---|---|---|---|
| B01、B09 局生命周期 | Game77；RunId、真实开始边沿、终止原因、结算来源隔离、同谱旧结果拒绝 | 分包已有Failed、Back/Aborted、Skip/Skipped；766补Pause/Resume、Retry/Aborted及同谱新RunId | 正常Completed、初始化位于结算、未开局、中途订阅、快速重进、真实迟到结算；每包证据边界独立 |
| B02 原生桥 | Runtime57；精确类型/布局/线程、根与失效拒绝，受支持ABI清单 | 多类真实读写/Hook调用已有分包记录 | 真实IL2CPP GC、长期持有/释放及异常矩阵；未知ABI继续拒绝 |
| B03 SDK | 固定程序集身份、增量契约和旧二进制兼容已验 | 正式宿主加载实际旧插件 | 本项已验；任何新公开契约仍须兼容回归 |
| B04 包 | 依赖/可选依赖/冲突/排序/意愿/迁移/回滚；Hosting20、PackageLifecycle14 | 明确停用跨重启保存 | 双包依赖/冲突/版本、升级归属和迁移失败完整矩阵 |
| B05 作用域/调度 | Scope30；scene/run代次、取消/迟到拒绝、逆向清理机制 | 666两轮正式启停管理资源归零，scope先取消 | 队列满/超时/迟到、回调中停用、重入和多包压力；管理计数归零不等于原生最终释放 |
| B06 适配模块 | Module26；精确签名、依赖、代次、等待/撤回/退避 | 603会话22模块active，取消能力available | 真实故障隔离、恢复后旧服务失效、可选符号缺失矩阵 |
| B07 Hook | Hooks43；broker、Prepare/Configure/Commit、独立Hold协程上下文；原生fixture31,261/61,358 | 665逐音符675观察/675原调用，8/12来源 | 新包剩余来源、在途撤回/重入/异常/优先级；参数修改未开放 |
| B08 修改协调 | Mutation21；栈/变换/独占、别名、外部基线、读回/回滚 | 旧颜色栈及双/三层持有有分包证据 | 新机制实机多包交错启停、外部游戏写入、对象重建及恢复失败 |
| B10 数据 | Data24；不可变修订、分页/取消、当前结果/设置接入 | 双包各444谱面/444成绩同修订分页 | 新当前局结果和设置字段来源/单位/流程一致性 |
| B11 玩法 | Judgements35、Gameplay30、审计17；12来源识别、NoteId、时钟、Pause/Resume/Retry/Back/Skip | 665实际8来源/675观察、失败479计数；766一次hold.end、暂停21秒时钟冻结及恢复推进 | 挑战Hold尾身份、完整来源矩阵、正常结果逐项对账、RawGameUnits单位标定、不同刷新率；一次hold.end不代表挑战尾全部通过，Speed/Seek未开放 |
| B12 目标 | Runtime57、Visual68；唯一/歧义/代次、共享4096窗口、满128轮换、历史50000边界 | 旧真实目标和场景租约有记录 | 新动态发现/重建/地址与实例ID复用、满容量及别名恢复实机；颜色缓存仍512上限 |
| B13 视觉 | Visual68、GameResources25；TMP精确color访问器、共享目标/独立基线、资源引用与延迟清理 | 666两轮Float应用/正式停用管理归零；766真实TMP发现/基线getter、Float应用及场景托管lease释放 | 真实TMP覆盖/双包释放/游戏新基线；Texture/Sprite/Material/字体逐类矩阵；766仍有cleanup pending，物理释放/独立恢复读回、音符/轨道/相机/后处理仍缺 |
| B14 UI/输入 | UI73；挂载/控件/焦点、采集/派发epoch双检、16手柄按钮、current身份切换 | 644实体F8一次完整派发；按钮/文本/菜单导航分包证据 | 双包焦点/模态/重绑、两个实际挂载点、DPI/分辨率、停用恢复、真实手柄与热拔；游戏输入截获未支持 |
| B15 音频/内容 | AudioContent64；音频/混音/替换接口；单项/批量统一事务、依赖保活/配额/清理隔离；旧Workshop图替换 | 8组PCM加载/播放/停止及Effect释放调用 | 完整独立LOCAL曲库、选择/预览、音乐/封面到局内、重试/结果；真实替换/混音/可闻输出/延迟、资源最终释放 |
| B16 存储/设置 | Storage14、Settings25；schema/迁移/独立目录/语言、受控游戏音量 | 双包迁移/隔离/热切已验 | 游戏设置真实双包覆盖、外部写入和基线恢复；任意存档事务未开放 |
| B17 跨插件 | Services19；共享身份、版本/代次、通知、请求限制 | 实际独立包跨ALC调用、可选提供者撤回 | 提供者故障、硬依赖联动及升级/契约完整矩阵 |
| B18 诊断/性能 | B18a已验；Performance11与采集/比较脚本自检10 | 结构化单会话诊断/导出 | 四基线、20轮真实切换、资源曲线、校准阈值与CI；宿主tick不等于整帧/GPU |
| B19 分发 | 766安装核对、journal/备份/恢复/互斥；恢复13与安全13；SDK/模板10 | 已执行真实正常更新；旧版另目录7发布DLL重建同hash | 最新包完整复现、干净环境、真实更新中断/磁盘故障与全部所宣称能力矩阵 |

## 按依赖推进

1. **先收实测闭环，B01/B02/B07/B09/B11。** 用已安装包取得正常Completed、失败、暂停继续、重试、提前退出及结算停留的独立序列；每次固定session/run/build。Hold各来源和结果计数并核，保留实际原调用计数，不能把超时调用次数当作计分接受次数。针对真实缺陷再写回归、修复和新包复测。
2. **证明对象和资源可存活、可恢复，B02/B05/B08/B12/B13。** TMP真实getter前后读回、两个插件释放顺序、外部新基线、目标重建；为Unity延后销毁取得独立receipt，确认根与额度最终退出。遇cleanup pending时不丢根、不清预算、不把预期等待当插件故障。
3. **补仍缺的游戏能力，优先B15独立本地曲包，再补B13玩法视觉。** CPU登记不是选曲目录；Workshop图替换不是完整本地音乐/封面。先实现下一节的独立目录与选择模型，再逐层接真实消费边界；音符/轨道/相机/后处理各自精确探测与声明。
4. **补共存矩阵，B04/B05/B06/B08/B10/B14/B16/B17。** 双包交错启停、焦点/模态与重绑、可选和硬依赖、能力撤回恢复及旧服务拒绝。手柄必须有真实硬件才计硬件验收；没有硬件仍可继续其余工作。
5. **最后B18b/B19。** 当前能力集合稳定后做四基线、20轮启停/切换、阈值文件/CI、最新包复现与升级中断。未验能力明确限制，不靠增加测试数放行。

## 首个可直接实现的代码任务：LOCAL目录与选择事务

这是新实现任务，以下类型名称为设计方向，**当前源码没有这些接口**。目标是在现有已验证内容组之上建立真实可消费的稳定曲目描述符和选择生命周期，为后续原生播放接入提供可测试的边界。首批不承担完整编辑器或在线平台。

主要入口：

- `src/RizToolbox.Sdk/AudioContentContracts.cs`：复用 `LocalContentId`、`LocalContentSnapshot` 与稳定DTO。若增加公开目录能力，用独立可选接口/能力ID，不能给现有 `ILocalContentProviderService` 增加必需成员。
- `src/RizToolbox.Adapter/AudioContent/LocalContentService.cs` / `.Batch.cs`：复用已验证内容图、`AcquireValidated` 与消费租约，不建立绕开额度的第二份资产store。
- `src/RizToolbox.Adapter/AudioContent/AudioContentModules.cs`：独立module、依赖与可用性；`WorkshopChartModule.cs` 继续保持现有实验范围。
- `tests/RizToolbox.AudioContent.Tests/`：现有事务回归，新增目录/选择测试放独立文件；GUI探针最后再接到 `examples/RizToolbox.Examples/`。
- 真实边界和资源责任：[目录静态证据](../../docs/validation/local-provider-catalog-20261021/README.md)。dump在 `C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs`；只接受对应GameAssembly指纹。

交付一：目录条目包含独立稳定LocalId、provider/revision、标题/作者、charts与已验证audio/cover引用；原子登记一组完整曲目，引用的格式/依赖/额度不符则整个条目不发布。同名不同provider共存，重复稳定ID冲突。条目撤回从新目录消失，已有选择/局内消费者继续持旧revision直到释放。

交付二：选择状态持稳定ID与目录revision，并有request generation/cancellation。选择A→B后，A的迟到封面/音频不能更新B；provider撤回后不能新建播放请求；旧已成功请求按明确规则继续持有资源。停止旧预览后再释放其音频引用。由选择事务产生完整 `LocalPlayRequest`；原生转换未接通时明确返回不可用，不能给 `AvailableForGameResolution` 提前报true。

交付三：用实际本地tab/row/card消费这个目录，并自行保存LocalId收藏/上次选中。原版 `ChangeDisc→List<WorkshopLevel>→SetInfo/PlayLevel` 及其UInt64持久ID逻辑保持原路径；本地项不伪造SteamID、不调用会读取Workshop元数据的stock SetInfo。此步需要真实UI证据才可宣称目录可用。

后续播放单独交付：先证明 `ResourceReference<Sprite/AudioClip>` 的关闭泛型/vtable与释放责任，再将解析完成的请求转为 `InsideLevelParams`；重试、结算页重试、退出、Provider撤回与LocalId/revision贯穿后才宣称完整曲包。当前候选Release会销毁Sprite连同Texture，不得让两个owner共享同一个可销毁引用。

最低回归：同名跨provider、重复ID、缺依赖/坏格式、取消/撤回无半发布、旧revision保活、A/B迟到结果拒绝、错误线程不消费租约、播放准备失败释放新引用、模块停用恢复不重置旧消费者额度。至少一项失败先写red再修；不为DTO字段逐个写镜像测试。

该任务能与主代理游戏实测、另一代理原生所有权核查并行。只允许一个代理修改公共SDK契约；共享obj/bin的构建、安装和GUI由主代理串行执行，其他代理交付独立文件和审查结果。

## 核对依据与接手动作

| 证据 | 结论 | 下个动作 |
|---|---|---|
| [SPEC](SPEC.md)、[CHECKLIST](CHECKLIST.md)、[实现状态](../../docs/IMPLEMENTATION-STATUS.md) | 验收覆盖19整项，现有大部分为实现/分包局部实证 | 对实际声明逐项补矩阵，不改写历史结论 |
| [766整合](../../docs/validation/foundation-color-content-20261021/README.md)及同目录manifest/check JSON | 当前可复验代码包与安装基线；766/25项目 | 源码不变先复用安装包；改码后统一构建并重新hash |
| [TMP证据](../../docs/validation/tmp-color-20261021/README.md) | 新访问器/25项离线通过，真实恢复和容量边界未完成 | 用独立前后getter和双插件释放补证 |
| [本地目录边界](../../docs/validation/local-provider-catalog-20261021/README.md) | stock目录/收藏/选择绑定Workshop与UInt64；独立LOCAL消费者尚缺 | 按上述三个交付实现目录、选择和真实消费 |

本次仅文档核对，没有重建、运行游戏、修改配置或复验上述历史测试。复查可从仓库根读取所链源文件与JSON；构建/安装命令以766整合证据为准。实机结论引用已冻结的766会话；后续退休成功receipt改码、构建和实测必须另立证据。
