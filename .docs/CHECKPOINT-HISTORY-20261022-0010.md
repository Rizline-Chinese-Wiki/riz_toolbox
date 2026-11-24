# 当前续接点

更新：2026-10-21，UTC+8。目标仍为 `.docs/foundation-expansion/SPEC.md` 的完整B01–B19，未完成；CHECKLIST仅B03整项与B18a子项。测试数量不代表覆盖率，不调用goal complete或自行pause。

## 当前状态

- 18:39最新：793完整构建/安装71925已结束exit0：793/793、25套、0编译警告/错误，18安装hash和270源码一致，原游戏三个文件before/after相同。当前已装793。AudioContent有效red78/82→green82/82；其余沿用本次完整构建。
- **游戏已正常退出，metadata marker已恢复原Absent并核验。** session20261021T103006149Z-fb954c63ee9b4865b90dac3cbc1a23c9完整1335条/0Error，冻结runtime-resource-owner-20261021/assetref-owner-dispatch-20261021T103813Z。runtime-proof.json已严格解析一次header/完整10行、两个官方virtual resolver映射与同一RunId开局/结束。
- **最新闭合包装实测已完成。** 安装后的793包在真实Rizline进程中进入主菜单→选歌→Greatness Earth关卡暂停页→退出回选歌→返回主菜单；转场中工具曾短暂绑定Codex桌面，已通过重新枚举Rizline窗口恢复，未对错误截图继续输入。探针会话`20261021T151120008Z-99451947aada40e89bb234ce07a2d411`在真实关卡路径输出`closed-local-wrapper-metadata`，8/8行成功；marker随后已删除并核验Absent。
- 原版真实对象分别是AssetRefReference<Sprite>和AssetRefReference<AudioClip>，Release实现RVA均B115D0、MethodInfo slot5；interface Release slot1。不是ResourceReference<T>，其Sprite的AEF1F0不能套用；ResourceReference<AudioClip>仍unknown。该局FREE、0分、479Miss、Crashed=true、EndReason=Failed，不能当正常Completed。
- 无构建在跑；最新GUI授权继续有效。下一步直接取得专用ResourceReference<AudioClip>的可验证闭类型/方法（可从官方System.Type.MakeGenericType/IL2CPP class_from_system_type路径探测元数据，必须严格验证类型/签名和AOT入口；不先构造资源wrapper）。随后才做本地资源保活/metadata桥，不再重复原版AssetRef采样或25静态范围。
- repo：`C:\Users\71957\Desktop\riz_toolbox`，大量源码未跟踪，禁止reset/clean/HEAD覆盖。无本轮提交或推送。
- 前一789完整包：`docs/validation/foundation-return-menu-final-20261021/`。789/789、25套回归、0编译警告/错误，18项安装hash匹配、268源码输入未变；原游戏三个before/after相同。返回菜单与跨重启局部验收不因793采样而改写。
- 构建36415和安装均已结束exit0，没有dotnet构建在跑。包在artifacts/package，SDK1.2.0。
- 游戏已正常退出。`runtime-return-menu-20261021/probe-files-restoration.json`六份临时文件已恢复原Absent并复核不存在。不要把历史stage/stop记录当当前状态。
- 用户实体Escape曾停止GUI，随后明确回复“允许恢复实测”，授权已恢复且完成本批实测。仅用@oai/sky的node_repl操作原生应用，fresh返回Window/state、一动作后刷新；不得shell WinAPI模拟、认证或Codex UI自动化。
- 3个代理均命中账号usage limit，已落盘代码由root接管；不要等旧代理终态或并行启动共享obj/bin构建。

## 本批交付

- 真实Canvas四角投影：TransformPoint→RectTransformUtility.WorldToScreenPoint，核前向同深度仿射，拒绝旋转/斜切/反转/透视深度变化/RenderTexture/非主屏。求Canvas、camera、Screen交集；只给owned-root做X/Y补偿，保持SDK均匀坐标。Capture/ClearInput清值缓存。
- 真实菜单的GetComponentInParent精确(Type,bool)重载；支持已观察MainMenu/FakeMainMenu，通过原GameManager.mainMenuControl/mainButtonCanvasGroup、活跃加载、相机、Raycaster核验；Scene元数据返回真实名。未知场景保持拒绝。
- LOCAL/TMP探针显式Dispose迟到公共场景通知时仍活窗口，再清句柄，避免稳定ID Conflict。UI测试链接真实LOCAL源码：迟到通知red78/79→green79/79；返回输入red77/78→green78/78；Canvas red73/77→green77/77。
- 独立LOCAL catalog：稳定命名空间歌曲/难度/精确资源代次、原子Own后复核、取消/修订冲突、选择失败保旧/旧Dispose不撤新、请求保活图、32owner/128global含退休持有额度。AudioContent78。正式scope、cap撤回、模块Waiting/unknown cleanup不恢复已测。独立审查无确定阻断缺陷。
- Visual未知free有界责任隔离、预算/依赖/ProcessRoots保留不重试，Visual71；正常shutdown归还责任，故障需进程重启。无原生free异常/GPU完整证明。
- 原生LOCAL RequestPlay/preview仍OperationUnsupported、AvailableForGamePlayback=false；无伪Workshop UInt64、无stock替换冒充完整曲包。

## 最新完整实机证据

`docs/validation/runtime-return-menu-20261021/runtime-proof.json`由analyze-success.py严格解析完整冻结日志，README有边界。

- 789 session `20261021T101115225Z-0727ccd94dfa47aca9fceaa1a0ab23d8`：930条连续、0Error。song0/gen1→收藏song0→song1/gen2；MainMenu→LevelList释放gen2，窗口消失；返回FakeMainMenu创建sceneGeneration4新窗口，恢复song1/gen3；新按钮Play明确OperationUnsupported。
- 789重启session `20261021T101619095Z-25269e51a2f74676a38b78e62a8dcf43`：244条连续、0Error。读取lastSong=song1、favorites=1，自动选择song1、song0收藏可见。此场景实际又出现window先于公共通知创建，正确释放/重建并恢复，无ID冲突。重启前后偏好文件字节一致。
- 中间 `20261021T101427413Z-5cbc1c72a52d4622ba91c9dd752b9868`仅2条日志后进入Steam启动选项，未构成可用游戏会话；选择已选的“玩Rizline”后得到成功重启会话。不能称作已证实崩溃。
- 788 session `20261021T100328262Z-0637fccb72724092a5d1f3bfe13a0319`：完整325条/0Error，但探针丢活窗口句柄导致Conflict；失败保留，789不能改写它。
- 787 session `20261021T094900103Z-5d3bca7a402a4aec8cefcda43ebf42a7`：完整1060条/0Error。首次MainMenu窗口/同名选择/收藏/退出场景通过，FakeMainMenu未挂载。旧935部分快照保留；原六份临时文件已恢复。

## B15已取得的元数据与下一步

- `LocalResourceReferenceDiagnostics`和Runtime partial已编译，opt-in marker `BepInEx/RizToolbox/probes/local-resource-reference.enabled`，默认关闭。WorkshopChartModule核build/hash后一次CaptureMetadata，header+逐行JSON避免2048日志截断。
- 最新实机证据见`docs/validation/closed-wrapper-metadata-20261021/actual-game-proof.json`及原始`actual-game-events.jsonl`：首次在运行时闭合验证`ResourceReference<UnityEngine.AudioClip>`，并用`InsideLevelParams.musicReference`的精确`IResourceReference<UnityEngine.AudioClip>`作assignability约束；构造、`get_Resource`、`Release`的声明类、MethodInfo、RVA和slot均已读到。证据边界仍是BCL Type/Array元数据分配，未创建ResourceReference/Unity资源实例，也未调用任何资源方法。
- 787真实13行：InsideLevelParams两闭interface及5参数ctor；Sprite closed ResourceReference ctor RVA AEF2C0、Release AEF1F0真实declaring class、六项MethodInfo交叉核验与GameAssembly可执行entry成立。interface的方法占位entry不代表实现。AudioClip concrete和既有对象virtual dispatch明确unknown。
- `CaptureExistingOwner`已接到ExactResultOriginModule已有Parameters root并完成实机。只读exact字段→class_from_type→官方object_get_virtual_method，得到本局两种AssetRefReference。未Invoke资源方法/构造wrapper/猜vtable；LevelOverControl入口尚未运行。
- 下一项：专用ResourceReference<AudioClip>闭类型/AOT方法可用性；之后设计可信LOCAL原生音乐/封面工厂和metadata桥。已有Sprite闭类与原版AssetRef映射不能代替AudioClip闭类证明。
- 完整LOCAL需要独立音乐/封面消费、ILevelMetadata兼容桥、InsideLevelParams、取消/撤回/重试/结果及最终所有权释放。不得用CPU目录/Prepare或现有Workshop图替换代替原生可玩路径；不得在未证明的泛型/vtable上直接写入。

## 其他仍缺

正常Completed、挑战Hold尾完整来源/时间标定、长期GC/物理释放、音符/轨道/相机/后处理、双包焦点/依赖/设置、真实手柄、四基线/20轮与发布复现矩阵。按原SPEC推进，不缩范围，不以本批局部验收勾整项。

767退休receipt、778真实TMP双插件白0→低1→高2→低1→白0、766暂停/恢复/Retry等历史索引见docs/IMPLEMENTATION-STATUS.md和CHECKPOINT-HISTORY-20261021-1819.md；不能混作789新包全部实测。
