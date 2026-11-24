更新：2026-10-21 18:03（UTC+8）。最新状态优先于下面历史停止现场。

## 最新状态

- 18:08覆盖：788游戏已正常退出。session `20261021T100328262Z-0637fccb72724092a5d1f3bfe13a0319` 的325条完整失败证据在runtime-return-menu-20261021/initial-scene-window-conflict-red-20261021T100535Z。MainMenu窗口创建先于公共场景通知，probe丢句柄未Dispose导致同IDConflict。已修LOCAL/TMP探针显式释放，真实LOCAL源码链接回归red78/79→green79/79、examples构建0警告。
- **当前新完整789构建/安装串行运行，exec session36415，证据foundation-return-menu-final-20261021，native D:/RizToolbox-build-cache/return-menu-final-20261021。** 源码稳定，不要另起dotnet。游戏当前退出；runtime-return-menu-20261021/probe-files.json六份临时文件仍stage中，尚未restore，供修正版再测。
- 用户已明确回复“允许恢复实测”，GUI恢复授权生效；旧停止记录保留作历史。
- 返回场景修复已完成，UI专项red77/78→green78/78；完整788/788、0编译警告/错误，18安装hash匹配、268源码未变。构建1389和安装9627均结束exit0。当前已安装 `foundation-return-menu-20261021`，支持真实MainMenu/FakeMainMenu图验证和菜单输入分类。
- 旧787游戏已正常Alt+F4退出；完整session1060条/0Error冻结 `runtime-canvas-catalog-20261021/normal-exit-menu-return-red-20261021T100230Z/`，6份临时文件已按该目录probe-files.json恢复。旧935快照仍只是部分证据。
- **新阶段已stage临时probe并启动788，待实测返回页和跨重启偏好。** 恢复记录现在是 `runtime-return-menu-20261021/probe-files.json`；不能用前两轮恢复记录代替。游戏退出后才restore。启动器报告targetless不代表游戏没启动，先list_windows/Get-Process再考虑重启。
- 无构建在跑，源码稳定。后续完整目标仍B01–B19，不complete。FakeMainMenu原生重挂载尚待本阶段验证。

## 电脑操作停止现场

- **用户实体Escape触发Computer Use停止；本轮不再调用GUI工具，不用shell绕过。** 完整B01–B19目标仍未完成，不complete或自行pause。
- final串行构建/安装27909已结束exit0：`foundation-canvas-catalog-final-20261021` 787/787、25 suites、0编译警告/错误、18安装hash匹配、268源码未变。无构建在跑。
- **新实机临时文件已stage且待恢复**：`runtime-canvas-catalog-20261021/probe-files.json` 记录panel LOCAL config、只读metadata marker、偏好与3原创资产原件。不要用上一轮final-probes-restoration当本轮恢复。本轮游戏若仍运行，勿安装/恢复/删除其资源；进程与停止记录见computer-use-stopped.json。退出后用probe-files.py restore，并核文件状态。
- 新session **20261021T094900103Z-5d3bca7a402a4aec8cefcda43ebf42a7**；PID37988（以新Get-Process核验为准）。停止时的快照命名escape-stopped-partial，只表示读到的连续日志，不是正常退出完整会话。
- 实测MainMenu挂载成功，窗口位置和点击正确：同名song0/generation1→收藏song0→song1/generation2→Play明确OperationUnsupported→收藏过滤只剩song0。进入LevelList后selection generation2以scene-change释放，LOCAL窗口消失，已验证。
- **新发现必须修复：返回页的实际Scene是FakeMainMenu，NativeUiInputBackend只允许MainMenu，所以未重新挂载。** GetMounts.Scene硬编码MainMenu，InputContext/NavigationReason也未覆盖FakeMainMenu。需凭同一真实GameManager.mainMenuControl/mainButtonCanvasGroup加载/活跃/Canvas验证扩展已知返回场景；不只伪造场景字符串或overlay冒充。源码尚未改此项，不能宣称返回恢复通过。
- 资源metadata诊断完整13行已实际采集：InsideLevelParams的两闭interface及ctor；Sprite closed ResourceReference ctor RVA AEF2C0、Release AEF1F0真实metadata/entry/slot已核。AudioClip concrete与既有对象virtual dispatch明确unknown；尚无ExistingOwner采集或原生本地播放。
- 下一步先核游戏是否已退出；恢复本轮探针并冻结最终完整分卷（如仍运行则等待用户恢复GUI授权/正常退出，继续可独立CPU工作）。随后修FakeMainMenu真实挂载/菜单输入上下文并构建，授权恢复后实测返回重建与偏好恢复。用户此次停止只记录为GUI停止，不能据此伪称整体完成。

## 最新状态（断电续接先读）

- **最新串行完整构建/安装仍在运行，exec session27909**，目录 `foundation-canvas-catalog-final-20261021`，native目录 `D:/RizToolbox-build-cache/canvas-catalog-final-20261021`。待核终态再操作游戏，不能另起共享obj/bin构建。此前40562已结束exit0，787/787、18安装hash和268源码未变；final仅补资源诊断按行输出，避免2048字符日志截断。
- Canvas77/77、AudioContent78/78已通过。catalog代理测试首次编译失败已改为真正PluginHost/scoped factory访问，`boundaries-compile-failed.log`与green-boundaries.log保留。
- 新实机辅助已落盘 `runtime-canvas-catalog-20261021/probe-files.py`（stage/restore）和freeze-session.py。**尚未stage，没有本轮新临时配置；尚未启动787游戏。** stage会记录panel opt-in、metadata marker及偏好/3资产原件。只在确认无Rizline进程时stage/restore。
- 用户授权自行 computer-use 和多代理并行。完整目标仍为 SPEC B01–B19；未完成，CHECKLIST 仍仅 B03 整项、B18a 子项，不能凭测试数量 complete。
- 游戏已正常 Alt+F4 退出。`runtime-local-catalog-20261021/final-probes-restoration.json` 已于 09:23:30Z 写出；主代理复核 3 份临时配置与 3 个原创验证资产均恢复原 Absent。下面较早的“仍 Enabled=true”已过时。
- 最新已完成并安装完整包为 `foundation-local-menu-parent-20261021`：778/778、25 suites、0 编译警告/错误、18 安装 hash 与264源码输入当时未变。旧 build sessions 57952/10232 均已结束。原游戏三文件 before/after SHA256 全相同，已写 original-game-after.json。
- **当前源码正在新增 Canvas 投影与只读资源引用诊断，已装778不包含这些新变更。所有 dotnet 构建仍由 root 串行执行。**
- 真实主菜单 `GetComponentInParent(Type)` 被裁剪，最新已装包已修为精确 `Component.GetComponentInParent(Type,bool)`。Canvas 现可解析，但真实窗口拒绝原因变为 `Canvas rectangle and pixel viewport aspect ratios do not match.`；不能宣称 LOCAL UI 已验。
- 最新 session `20261021T091529261Z-fd4e98c80f764ecba27751e8756db6b8` 已完整冻结 `runtime-local-catalog-20261021/menu-parent-fixed-canvas-rejected-20261021T092329Z/`。更早签名失败保留于 menu-parent-signature-red-20261021T091214Z。
- 三代理随后全部命中账号usage limit，已落盘由root接管。canvas_projection已实现 helper/Backend与4项行为回归；resource_reference_binding新增两个只读诊断文件和README；catalog独立审查无确定阻断缺陷，并已落盘5项边界回归。
- Canvas：首次 red.log 是 internal exception 编译失败；有效 red-behavior.log为73/77，新4项stub失败；实现后green.log为77/77，0编译警告。新metadata两文件随此已成功编译。AudioContent新边界首次green-boundaries.log编译失败（错误GetService重载），root修后再跑，保留失败记录。
- root已接线 WorkshopChartModule 的显式marker：`BepInEx/RizToolbox/probes/local-resource-reference.enabled`，核build/hash后一次CaptureMetadata并输出JSON。默认不执行，不自动扫描/接ExistingOwner。
- 下一步：完成 Canvas 行为 red/green与源码审查；补 catalog 独立报告指出的模块/配额/撤回回归；集成只读 metadata diagnostics；完整新包构建/安装后实测真实主菜单 LOCAL UI。临时探针和偏好/资产必须先记备份，退出后冻结全日志分卷并恢复。
- B15 真实原生 parser 与两首原创验证数据发布已成功；CPU catalog/选择/Prepare不是可玩曲包，原生音乐/封面 ResourceReference 关闭泛型、InsideLevelParams、重试/结果链仍待实证。其余 B01–B19 缺口按原SPEC继续，不缩范围。

# 续接检查点

完整目标仍为B01–B19，用户授权自行computer-use和多代理。未全部验收，goal不能complete；CHECKLIST仍仅B03整项、B18a子项。用户要求并行加速防断电，本轮3代理均usage limit，已落盘代码由主代理接管完成。

## 最新现场覆盖说明

- final-coexistence包778/778、18安装hash与264源码已核；该包实测session `20261021T085918091Z-4ff5aa1dbc1443efa33add18650d10d4` 0错误，三层EdgeCover与TMP探针共存恢复。原创WAV/PNG/chart通过真实parser，songs=2已发布；但MainMenu挂载不可用，LOCAL窗口未出现，不能宣称UI验收。已正常退出，冻结 `runtime-local-catalog-20261021/final-coexistence-mount-waiting-20261021T090518Z`。
- **当前3份临时config仍Enabled=true，需要恢复**；记录 `runtime-local-catalog-20261021/final-probes.json`（两个TMP + panel LOCAL）。该文件还记录原偏好与3个validation资产是否存在及备份。游戏当前已退出，等待诊断包构建。旧tmp-restoration只属于上一阶段，不表示这3份最终配置已恢复。
- 新源码仅增加LOCAL probe对MainMenu mount不可用原因的变更限流日志，用于查真实B14缺口；无fallback冒充真实menu mount。构建57952完成后先读log/退出码再启动。
## 当前状态

- repo `C:\Users\71957\Desktop\riz_toolbox`，大量未跟踪源码，禁止Git reset/clean/HEAD覆盖。无提交/推送。
- 游戏已正常Alt+F4退出；两份TMP临时配置已恢复原Absent，见 `docs/validation/runtime-local-catalog-20261021/tmp-restoration.json`。本轮新建空config目录可保留，不递归删除。电池最近100%、接电。
- **当前补主菜单挂载诊断的778整包构建/安装正在串行运行，exec session57952**，日志 `docs/validation/foundation-local-catalog-mount-20261021/build.log`，native目录 `D:/RizToolbox-build-cache/local-catalog-mount-20261021`。源码稳定，不能另起共享obj/bin构建。完成必须核退出码、18安装hash、264源码hash与原游戏三文件。
- 前一个778已全量通过/安装，但TMP探针曾跳过原EdgeCover层，导致旧panel三层断言故障；失败session原样冻结，不能宣称零错误。最终源码已改为TMP探针与原EdgeCover并存，并在SceneChanged先清过期label/window，避免释放时操作旧scene控件。此为重建的真实原因。

## 本轮实际实现

- 新可选SDK `ILocalCatalogProviderService`/`ILocalCatalogSelectionService`，独立cap `riz.content.local.catalog`/`riz.content.local.selection`；新增LocalCatalogService/Module/ContentBridge。完整稳定LocalId歌曲元数据、Audio/Cover/1–16 Chart精确资源代次；128全局/32每owner含撤回持有条目，复用内容store额度。Own/取消/内容/目录修订复核后发布。选择代次失败保旧、旧dispose不撤新，host LocalPlayRequest保活旧revision；native播放/预览仍OperationUnsupported。没有伪Workshop UInt64。
- AudioContent从64→73。red64/69、绿69/69；追加发现Own期间目录撤回导致修订重复，行为red72/73，修为Conflict，最终绿73/73。正式PluginHost/PluginScope查询、旧视图失效、payload保活、错误线程、迟到选择已测。文档 `docs/validation/local-catalog-20261021/README.md`。
- LocalCatalogRuntimeProbe真实MainMenu挂载，分页/难度卡/收藏/稳定LocalId偏好与Play拒绝显示；默认不开。可显式PublishValidationFixture生成实际验证的原创短WAV/PNG/单音符chart，两同标题不同ID条目，绝不宣称可原生游玩。后续需实机UI证据。
- Visual root/budget未知free保留有界责任账本、依赖/预算与ProcessRoots，不重试任意Dispose；模块Unsupported要求重启。借用/目标/候选根也隔离。服务责任票正常shutdown归还，故障不空预算恢复。主代理修Quarantined票无Cleanup时不能被其他正常票移除ProcessRoots。红69/70→绿71/71，见 `docs/validation/visual-root-quarantine-20261021/README.md`。

## 实机证据

- 767 session `20261021T050719415Z-bea8877e6d904db19169658e8e93ac51`，已退出并恢复probe。`runtime-767-retirement-20261021/complete-20261021T083926Z`完整3分卷2732条、序号连续。材质2b5b36…创建/释放后pending，第二次attempt完成receipt已观测。后台停留3小时不等于压力基线。旧错误单分卷快照保留，完整证据看README。
- 778 TMP旧共存失败session `20261021T084959014Z-f884c6c928644aa7953af0d1cb6ca9cc`，冻结 `runtime-local-catalog-20261021/tmp-before-coexistence-fix-20261021T085501Z`，320条/4错误（均旧panel三层探针假设冲突）。同时两个独立真实plugin已对同一个TMP字段generation135取得原生getter序列：白色0holder→低层1→高层2→释放高层回低1→释放低回白0，LastError=null。不是像素/GPU或整个B13验收。该错误已驱动最终重建，后续不能覆盖失败证据。
- 766暂停/恢复/Retry/同谱新RunId/一次hold.end/TMPbaseline/Float切场景证据保持在 `runtime-color-content-20261021/evidence-snapshot-20261021T044942Z`，不混成新包实测。

## 接下来

1. 等63423终态、核构建安装与hash。用最终包重新验证TMP与旧三层探针共存；临时TMP config必须按记录恢复。UI只走@oai/sky的node_repl；凭fresh Window/state操作，不能终端WinAPI模拟。
2. 实测LOCAL UI：临时panel `config-local-catalog-proof.json` Enabled=true/PublishValidationFixture=true；先备份任何已存在文件和偏好。确认两真实验证条目、选择A/B新代次、收藏/存储、Play明确unsupported、切场景释放。结束后正常退出、恢复probe，冻结全session分卷。
3. 完整B15仍须真实独立音乐/封面消费→ResourceReference关闭泛型所有权→InsideLevelParams→重试/结果/撤回贯通。CPU目录和UI不是完整可玩曲包。
4. 继续正常Completed、挑战Hold尾完整来源/时间标定、GC长期/物理释放、音符/轨道/相机/后处理、双包焦点/依赖/设置、真实手柄、四基线/20轮与发布复现矩阵。原SPEC范围不缩。
