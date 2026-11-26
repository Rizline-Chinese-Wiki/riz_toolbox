# B01–B19：下一批实际功能缺口

**11-26，r22 已通过整合回归。** 首次 GetTime 安装的完整映像校验从 21 次减到 15 次，关联播放组件时从 431 次减到 163 次。11 套标准回归通过 2123/2123 项。下一步核对安装包，实测启动耗时及官方谱和自制谱的同局行为。[r22 验收摘要](../../docs/validation/r22-steam-startup-20261126/README.md)

**11-26，r21 已安装到 Steam。** 下一局使用《Pastel Lines》EASY 核对 0.75 倍速、跳转至第 100 秒和恢复后的曲内时间与判定。自制单音符谱仍需验证首音符、结算、成绩和返回选曲；启动时两次主线程采样已定位到播放组件的安装与核对。[r21 验收记录](../../docs/validation/r21-steam-playback-20261126/README.md)

**11-23，Steam 已升级 r20 Basic。** 官方谱恢复至游戏实际退出暂停耗时 0.103 秒，随后确认判定、结算、返回选曲。下一候选要验证 0.75 倍速、第 100 秒跳转，以及恢复后媒体时间持续推进。自制谱仍需同局验证首音符、结算和官方成绩。下列 r18 结果保留作前后对照。[r20 同局摘要](D:/riz-m2/r20-steam-live-20261123/stock-run-01/stock-run-summary.json) · [当前包与验收](../CURRENT-CHECKPOINT.md)

1. **B15/B11/B10 自制谱结算与成绩。** 用含两点谱线的 1.1.1 探针在 Steam 选曲页启动单音符谱，记录首个 Tap、命中或漏判、结果窗分数与四种判定数、返回选曲、资源释放和官方成绩摘要。r18 的选曲请求获准后在 Wait 与 Tween 观察时失去跟踪。444 条成绩在另一个进程重启后摘要一致，同进程结算和返回仍待验。r19 已增加故障所在阶段的诊断。[LOCAL 证据](D:/riz-m2/r18-steam-live-20261123/local-run-01/verification.json) · [由 r19 SDK 构建的探针清单](D:/riz-m2/post-r19-stock-blockers-20261123/probes/20261123T142643651Z/validation.json)
2. **B14 官方谱控制。** r18《Pastel Lines》EASY 已观察到 `Paused` 和延迟约 200.5 秒的 `Resumed`，后续 `playing=true` 时无法读回有效曲内进度。下一轮在同局先确认进度持续推进，暂停后按当前控制器代次检查媒体时间与进度的偏移、`Hold List.Clear` 签名、0.75× 变速、跳转到 100 秒及恢复后的同步推进。两次主线程采样为整组校验开销提供了定位，封包后源码的暂停准备和回收优化已通过离线定向验证。[官方谱证据](D:/riz-m2/r18-steam-live-20261123/stock-run-01/events-000001.jsonl) · [后续源码与回归](D:/riz-m2/post-r19-clock-pause-audit-20261123/checkout-source-proof.json)
3. **组合验收。** 在同一进程切换官方谱和自制谱，保留普通通关、F06/F10 菜单与颜色恢复、Basic 画面与声音、实体手柄、跨插件焦点与重连的实机结果。[当前验收记录](../CURRENT-CHECKPOINT.md)

## 以往版本与验证记录

**11-23，r16 已封包。** B10 成绩换源和 B11/B15 自制谱对局记录已进入 r16。运行组件通过 1,985 项定向回归。B04/B17 的 r15 双包验证另在五个独立宿主进程中通过停用、依赖撤回、包目录移出及恢复、旧服务引用失效和重启。当时的实机清单也包含基础模组的设置、声音与停用后的恢复。[r16 下载和验证](../CURRENT-CHECKPOINT.md) · [跨插件验证](../../work/b17-r15-plugin-host-20261123/README.md)

**11-23，r15 已封包。** B11、B14、B15、B19 的四项修复已进入同一玩家包。正式 r14→r15 已在 Adapter 替换、Loader 发布前中断并恢复，224 个原有文件恢复完整、零额外文件。正常升级、卸载及重装均通过。[正式 ZIP 恢复回执](D:/riz-m2/b04-b19-r15-install-recovery-20261123/r15-01/validation.json)

r15 封包后的源码已修复 B10 成绩快照在读取中途换源时误报新数据的问题，并将 B15 自制谱生效与释放接入 B11 对局日志。两项合并后六套离线检查 1,507/1,507 通过，下一候选包需纳入这两项改动。[合并验证](D:/riz-m2/postr15-integration-20261123/validation.json)

**11-23，B15 的共享动画。** 已复现并修复空队列上的原调用缺少记录归属、多个管理器共用活跃状态两个问题。主树 1,050 项音频与内容检查通过。下次实机要看同一张单音符谱能否越过谱名画面、走到结算并返回；若仍失败，按新包的首次物理源点与回调阶段定位。[修复与验证](../../work/b15-shared-tween-update-20261123/validation.json)

**11-23，B15/B19。** 自制谱目录已在主树接通主菜单到选曲页的流程，缺失播放数据、曲目换版及请求的实际错误均已在隔离宿主核对。r13 玩家 ZIP 的 r12 升级、中断恢复及保留用户设置也已验证。SDK 1.3.0 已在留有旧 SDK 缓存的环境编译新接口，并以新旧宿主分别验证七个插件的加载行为。下一版需从同一批源码交付玩家包和作者包。B15 的下一次实机仍需在同一张自制谱开局、走到结算并返回，比较官方成绩与会话数。[当前记录](../CURRENT-CHECKPOINT.md)

**11-23，r13 与后续主树。** 下一次实机分别核对 r13 官方谱能否持续推进，及单音符自制谱能否结算、退出并保持官方成绩。官方谱先用的共享播放入口已在主树通过同进程受控验证。自制谱先用的方向继续处理官方播放组件接手时仍在执行的调用，以及两个自制谱初始化入口的读回。[当前构建与合入](../CURRENT-CHECKPOINT.md)

**11-23，官方谱实测。** r12 已从等待开局走到《Pastel Lines》EASY 的等待暂停状态。首次启用有分钟级停顿，曲内公开时钟读取阻塞了连续多个宿主帧。优先分别处理安装与曲内读取，在同谱上核对实际进度推进后再测暂停、变速、跳转和恢复。主树已修复官方谱优先时自制谱启动可能只改到一半的问题。同一进程内两种谱面的完整切换仍待完成。[同局证据及合入记录](../CURRENT-CHECKPOINT.md#r12-插件开发包与播放控制11-23)

**11-23，r12。** 插件开发包已独立离线验收。自制谱模块按播放请求启动的修复已编译，官方谱插件可以在控制状态保持不变时连续读取位置与倍率。下一步用 r12 分别跑官方谱和自制谱。还需在同一进程先玩自制谱再返回官方谱，核对两个模块共同使用进度回调时的表现。[交付与当前验收范围](../CURRENT-CHECKPOINT.md#r12-插件开发包与播放控制11-23)

**11-23，B11。** 普通插件的暂停和恢复已在同一张官方谱实测。将完整本地谱面运行时的准备推迟到实际播放请求，保留全程结算保护，然后重验纯官方谱会话的时钟准备、变速与跳转。一个进程先玩自制谱再玩官方谱仍需独立验证。[r11 官方谱实测](../CURRENT-CHECKPOINT.md#r11-官方谱暂停恢复和时钟冲突11-23)

**11-23，B15。** 同一张自制谱能进入谱名画面。r11 实机已将首次原生故障定位到 `PublishedTweenSource / SourceContextUnknown`，仍需区分该上下文的具体失败条件，并验证修复后的本地结算与会话释放。选曲页开局前已取得公开成绩摘要，下次需在同局返回选曲后比对。[r11 实测与证据](../CURRENT-CHECKPOINT.md#r11-单音符自制谱-steam-实测11-23)

**11-22，r10 Steam 实测。** B15 仍待验证本地结算、成绩隔离和会话释放。选曲页请求在 17.59 秒受理，受理后 6.28 秒首次记录 listener/source 未知。事件 67 的首次失败记录已定位至 `VerifyReady` 读到原生 V7 `phase=4, unknown=1`。下一项是区分哪一项原生发布校验率先置位，再用同一张自制谱验证结算与退出边界。r10 实机日志中三个已知入口的组合警告均未复现，包内的入口组合通过 3/3。[当前实测、候选和保护快照](../CURRENT-CHECKPOINT.md#r10-steam-实测11-22)

**11-22，r9 实机。** 已确认受理后显示自制谱名。下一包需验证 SDK 正确识别三个已知入口，观察宿主能否持续更新并完成本地结算。若仍失去状态，使用首次失败原因核对实际的原生检查项。N2 的当前包通过正常入口、时钟与分帧需求三组定向回归。[实机与待验项](../CURRENT-CHECKPOINT.md) · [r9 N2 回执](../../work/n2-packaged-r9-recheck-r1/acceptance.json)

**11-22，r9。** B15 已在主树接入真实 Current 的类型和谓词校验，以及 Unity 嵌套子节点的身份与回收。下一项用 r9 同谱复测，分别记录请求受理、游玩、结算、返回、成绩隔离和会话释放。普通谱面的长按字段和宽谱线捕获也已合入。[主树与证据](../CURRENT-CHECKPOINT.md)

**11-22，r8 实机。** B15 下一项是在初始化 `WaitUntil` 的物理父 Start 内，确认 Unity 对返回的迭代器另行启动子协程时的身份、归属和退出条件，再复测结算、正常释放与成绩隔离。同一张谱的选曲页请求已从 118.18 秒缩短至 19.91 秒。普通谱面的长按基类字段与超长谱线列表两个问题已另行定位。[实测与证据](../CURRENT-CHECKPOINT.md)

**11-22，B15 与 N2。** r8 源码已合入动画图读取优化、协程首次失败保留以及播放控制的分帧准备。接下来用相同谱面复测选曲页开局耗时及本地结算。若仍发生协程故障，用同局的引擎归属、枚举器和托管调用点核对实际失败条件。N2 已离线验证撤回需求后重新提交，完整实机变速和跳转仍开放。[本轮证据](../CURRENT-CHECKPOINT.md)

**11-22，r7 实机 B15。** 已通过正式选曲页的自制谱面开局受理并观察到游玩 HUD。接下来减少动画容器读取时对每个元素重复进行的完整校验，定位协程 Start 当前归属失配的具体条件。在同一张谱上重测结算、返回、会话释放和成绩隔离。[实机首个错误](../../work/b15-live-local-crossscene-20261122-r7/live/20261122T125037547Z-b02b8cce9fec466e94890a4fbc776016/r7-launch-fault.json) · [主线程采样](../../work/v5-live-fixes-20261122/r7/live-managed-stack.log)

**11-22，B15。** r6 的主菜单干净拒绝和跨场景保留同一选曲已在 Steam 验证。选曲页第二次请求取得预制体，进入 CutOut 事件来源读取后发生类型错误。r7 已针对原始资源中的 RectTransform 修改读取条件；下一项是同谱从选曲页开局、隔离结算并正常释放会话。当前完整实机链仍开放。[r6 实机回执](../../work/b15-live-local-crossscene-20261122-r6/live/20261122T122025744Z-e66e0dcddbab4995a5d8c2ef146c26cd/result.json) · [原始预制体](../../work/v5-live-fixes-20261122/r6/stock-cutout-prefab.json)

**11-22，r5 实机。** 判定设置的类初始化已通过。下一步在预留会话前核实歌曲卡片与控制器预制体是否可用。缺席时应返回可重试的 `NotReady` 并保持会话数为零；在选曲页用独立插件显式启动本地谱面，再核实结算归属和资源清理。r5 主菜单此次拒绝后的未完成会话数为 1，清理状态未知。 [本次实机回执](../../work/b15-live-local-song-20261122/live/20261122T114645455Z-50cef71877c547ffbdc0ff5610408651/r5-live-result.json)

**11-22，B15 单音符谱面。** 已在 Steam 主菜单完成注册、选择与一次公开开局请求。下一步检查 `InLevelSettings.JudgeDataList` 的初始化与原生开局顺序，解决主菜单返回 `NotReady(17)` 后，再验证同一张谱面的结算和记录隔离。[请求与撤回](../../work/b15-live-local-song-20261122/live/20261122T111203641Z-53f9de4917cd47b6ae6f39b925c789e3/result.json)

**11-22，r4。** LOCAL 已在 Steam 首帧之后就绪。下一步用单独的本地曲目插件验证开始、结算与记录隔离，另用真实长按复核 SDK 捕获。B16 的双插件背景音乐覆盖和恢复已在实机通过。N2 安装仍有较重的同步验证区间，继续追踪其对实际游玩的影响。 [当前包与实机证据](../CURRENT-CHECKPOINT.md)

**11-22，r3 实机。** LOCAL 已通过静态帧数解析并安装五个边界，接下来修复判定进度与时间线接入时的就绪检查。B16 先对齐游戏音量 getter 与滑条范围的单位，再重测两个插件分层覆盖及恢复。长按控制器数组已在源码通过 16 项定向测试，待新包装入后核实实际长按捕获。N2 带值控制仍待真实验收。[实机及源码记录](../CURRENT-CHECKPOINT.md)

**11-22，Steam 实机。** r2 普通模式已验证失焦继续走谱、关闭后恢复自动暂停，以及重新启用后从暂停页手动继续。已定位 LOCAL 查询静态方法的类型错误，下一候选需要实机复验。B04/B17 双插件完成 8 步实机验收，覆盖撤回、旧引用失效、重新获取和停用清理。N2 带值控制仍需单独验收。 [实机与开关证据](../CURRENT-CHECKPOINT.md)

**11-22，B04/B17 候选交付。** 工作线程报告服务故障时，即使普通队列已满，宿主仍会在下一帧停用对应提供者并通知订阅者。源码回归 115 项，新候选的包内 DLL 回归 67 项，独立插件经包内宿主完成 9 项验证。接下来核对同候选游戏首帧和宿主内的实际停用。 [验证入口](../CURRENT-CHECKPOINT.md)

**最新（11-21，B04/B17 独立插件）**。旧登记的异步故障和撤回观察回调保留作用域身份，避免停用同 ID 的新实例。在途调用随撤回或停用结束。真实 SDK 包经正式加载器完成依赖、消息、错误、同名配置交错等离线组合，定向与独立验证共 113 项。 [验证记录](../../docs/validation/b17-service-lifetime-20261121/README.md)

**最新（11-21，B16 配置事务及交付）**。保存校验和配额使用同一份落盘字节，回调内停用中止在途配置，迁移最终替换失败会清理本次备份。新 V5 已纳入配置与设置修复，包内 DLL 通过 108 项，独立 SDK 消费者编译成功。双独立包的配置交错及游戏设置恢复仍待验收。 [验证记录](../../docs/validation/b16-config-transaction-20261121/README.md)

**最新（11-21，B16 设置覆盖）**。范围探测拒绝嵌套覆盖，插件与模块停用保留已进入的 setter 读回及恢复责任。定期重施途中停用也可恢复最新外部基线。147 项离线检查通过，真实设置场景仍待同候选验收。 [验证记录](../../docs/validation/b16-setting-lifetime-20261121/README.md)

**最新（11-21，V5 结算与数据）**。同谱重开和数据快照修复已进入同一个新候选。隔离核对 26 个运行文件，包内 DLL 通过 913 项，新的独立 SDK 探针编译了结算读取。真实正常结算、快速重开和数据字段仍待实机。 [当前检查点](../CURRENT-CHECKPOINT.md)

**最新（11-21，B10 数据快照）**。发布前检查刷新占用、取消、失效和模块寿命，结算观察更新会标记旧修订。支持显式读取保留的旧 token，旧页与结算来自同一修订。当前源码 159 项离线检查通过。 [验证记录](../../docs/validation/b10-snapshot-publication-20261121/README.md)

**最新（11-21，B09 同谱重开）**。运行身份与判定归属共同核对控制器和 `LevelInformation`。已覆盖共享 `LevelInformation` 的新控制器、旧结算迟到及短暂采样缺失，349 项离线检查通过。实际正常结算、连续同谱及迟到结果仍需实机验收。 [验证记录](../../docs/validation/b09-controller-pair-result-20261121/README.md)

**最新（11-21，V5 播放生命周期交付）**。起播校验和停用收尾已纳入同一个新候选。隔离核对 26 个运行文件，包内 DLL 跑过 802 项，用同源 SDK 包编译了独立插件。真实 Unity 首帧、正式 Fresh、固定 `GetTime`、实际调度和带值控制仍需实机验收。 [当前检查点](../CURRENT-CHECKPOINT.md)

**最新（11-21，B05 播放命令停用）**。先关闭排队命令再清理订阅，值事务在游戏对象读回后重新核对租约，后端清理异常单独记录。当前源码通过 313 项托管检查和 193 项自有原生夹具断言。实机作用域及玩法矩阵仍需验证。 [验证记录](../../docs/validation/b05-gameplay-command-shutdown-20261121/README.md)

**最新（11-21，N2 起播读回）**。正式 Replay/Resume 来源用实际 `PlayScheduled` 参数交叉核对 DSP、起播基准和媒体锚，在恢复选中倍率前、恢复后及发布前分别检查。四个故障场景和正常起播通过，当前源码共 631 项定向检查通过。真实 Unity 调度和带值播放控制仍需同候选验收。 [验证记录](../../docs/validation/r06-stock-schedule-readback-20261121/README.md)

**最新（11-21，N4 菜单模糊续接）**。`BindDownsample` 已纳入新 V5 候选及同源 SDK 引用包，在指定场景中等待唯一目标并续接替代代次。Release solution 0 错误，五套定向检查 283/283，包内 DLL 复跑 223 项，独立项目从 SDK 包成功编译新接口。实机仍需验证画面、重建、竞争与恢复，以及已开放视觉资源的双插件覆盖和释放。 [验证与调用](../../docs/validation/n4-menu-blur-binding-20261121/README.md)

**最新（11-21，V5 交付）**。新候选已纳入菜单后处理和倍率回滚修复，隔离校验 26 个运行文件，包内 DLL 复跑 218 项，独立项目从新 SDK NuGet 包成功编译。下一步按同一候选核实游戏内首帧、正式 Fresh 与固定 `GetTime`、带值控制及后处理可见效果。 [当前检查点](../CURRENT-CHECKPOINT.md)

**最新（11-21，N3 菜单后处理）**。已接入 `riz.game.post-processing`，在固定构建的 `GameStart` 与 `MainMenu` 提供主相机模糊降采样目标。范围限定为 0–2 整数，使用共享目标和租约。Release solution 0 错误，278 项通过。实机画面变化及恢复仍待验收。 [SDK 调用与验证](../../docs/validation/n3-menu-postprocessing-20261121/README.md)

**最新（11-21，N2 倍率恢复）**。已经确认的倍率写入在回滚前核对独立读回的当前值。外部改值或持续无法读取时关闭控制并记录副作用未知。首次读回失败的同票恢复仍可执行。当前源码 986 项离线检查通过。 [验证记录](../../docs/validation/r06-pitch-recovery-ownership-20261121/README.md)

**最新（11-21，N2 Fresh 与 GetTime）**。同一自有 Fresh 票贯穿暂停、原生 Clock 激活与精确空 `MethodInfo` 调用。覆盖错误调用、worker 抑制回调、原生 backing 变化及立即退回原函数。当前九个进程 501 项通过。已发布到本机的新 V5 候选包含暂停 Clock 校验，正式 stock foundation、七根采用及游戏固定 CALL 待同候选实机验收。 [验证记录](../../docs/validation/r06-fresh-gettime-consumer-20261121/README.md)

**最新（11-21，N2 暂停 Clock）**。原生激活前后核对同一暂停回执的实际位置、倍率、音频长度和状态。改前两个真实夹具回归复现，改后当前源码 700 项检查通过。正式 Fresh 与固定 `GetTime` 的游戏内联合执行待验收。 [验证记录](../../docs/validation/r06-paused-clock-activation-20261121/README.md)

**最新（11-21，首帧绑定）**。Loader 传入版本配置的程序集，启动回调通过精确的实例 `void Update()` 签名和声明元数据准入。解决方案 Release 0 错误，托管准入 16/16、原生通道 209 项、Loader 24/24。新版 V5 已打包并完成隔离 `ValidateOnly`。下一离线切片仍核正式 `Fresh` 当前身份和固定 `GetTime` 消费者，实机首帧与 Speed/Seek 待验收。 [验证索引](../../docs/validation/e5-exact-startup-tick-20261121/README.md)

**最新（11-21，启动状态）**：启动摘要分别报告原生挂接和 Unity 首个宿主 Tick，失败时记下具体阶段。Loader 24/24、托管准入 14/14、自有原生场景 209 项，Release solution 0 错误。新 V5 启动候选完成归档回读和隔离 `ValidateOnly`，待接续同候选实机验证。[验证索引](../../docs/validation/e5-startup-readiness-20261121/README.md)

**最新（11-21，E5 交付）**：当前六组件已经编入并验证 V5 生成配置，离线检查通过此前真实启动快照。26 文件候选随包交付两个播放助手与当前示例程序集。实际 Unity Tick、整个同进程 V5 托管采用、正式 Fresh、固定 `GetTime` 消费者和真实 Speed/Seek 仍待同版实机确认。[验证索引](../../docs/validation/e5-production-candidate-20261121/README.md)

**最新（11-21，N2）**：暂停音频首次写入和后续写入分别核对准备基线与上次实际读回，防止覆盖外部变更。探针端到端传递有限谱面坐标及其单位。当前两项 Release 增量构建通过，定向验证 740 项。正式 Fresh、固定 `GetTime` 消费者和真实 Speed/Seek 仍待验收。接续范围见[当前检查点](../CURRENT-CHECKPOINT.md)，执行证据见[验证索引](../../docs/validation/n2-seek-media-ownership-20261121/README.md)。

**最新（11-20，N2 五参与者完整状态组合封存）**：在同一最终 1802 输入快照上串行完成 `current-r1`（cases 5–7，full `RizToolbox.sln` Release `0E/55W`）、`current-r2`（cases 1–4＋147 相邻，151 项）与 `canvas-adjacent-r1`（Canvas 7,8,9,10,17,18，6 项），合计 **20 个子进程、160/160 托管检查、7/7 唯一组合场景（0 重复）**，经 [seal-r1](../../work/n2-seek-game-state-composition-20261119/seal-r1/validation.json) 核验自然 `exit 0`（28 源码差异 / 26 `source-before/` / 6 DLL / `README.md` 保持）。详见[报告](../../docs/2026-11-20_seek-game-state-composition-report.md)与[证据索引](../../docs/validation/n2-seek-game-state-composition-20261119/README.md)。当前仅签离线受控五参与者对象图组合；正式 22-role `Fresh/watch/SuppressObserver`、固定 `GetTime` consumer 正向、`N2` 带单位时间与带值 Speed/Seek 控制事务、`E5` 生产首次准入、`Single mid`/非空 `listener effects`/`engine setter/effect transfer`、`N3/N4`、`B01–B19` 与实机 `R06/R07/R08` 仍保持开放。[当前进度](../CURRENT-CHECKPOINT.md)

**最新（11-19，N2 Hold恢复）**：真实epoch Prepare/Stop/target Factory/Start/Stop/原iterator inverse和backend callbacks已执行；第二Factory前失败保留已确认partial target恢复责任，pending native effect/异常Start/临时cleanup Unknown仍拒绝，目标只尝试一次，返回token先进入publication责任。当前epoch10＋target28＋mode3＋lifecycle62＋Runtime54＝**157/157**，15child/outer51745自然0，solution增量0E/0 emitted warnings（首完整编译62既有测试警告未消除），1800输入/6DLL/README保持、7修改/4新增已[封存](../../work/n2-seek-hold-epoch-recovery-20261119/validation-r1.json)，[报告](../../docs/2026-11-19_seek-hold-epoch-recovery-report.md)。原queues已有同epoch计数handoff，现实际1→0→1→0→1及18次native列表写/完整storage恢复通过，不重写。Module issuer安装字段受控装配、profile/native original为shim；不签固定body/Dobby/Fresh/完整Seek。下一沿原五参与者补完整gameState/判定/rendering与audio/四cache/Clock组合及逆序恢复；formal材料另补，不重复本批/相机/N1/退休/R08。历史缺native env的current-r1未整批PASS，不借旧31/956或旧包验新源。无在途构建/测试、无游戏/安装/账号操作，R06/SpeedSeek/B01–B19/production未完成，以CURRENT顶部为准。

**最新（11-19，N2 Hold字段）**：正式epoch误读AutoPlay/FreeHold字段导致拒绝已复现修复；capture/Verify共用ReadModes，exact backing field Boolean0x79/0x7a，模式变化/错误metadata仍拒绝。新三案例3＋相邻target28＝**31/31**，5child/outer21308自然0，solution0E/62既有测试警告，1796输入/4DLL/README保持，3修改/1新增已[封存](../../work/n2-seek-hold-mode-fields-20261119/validation-r1.json)，[报告](../../docs/2026-11-19_seek-hold-mode-fields-report.md)。只签private mode reader和相邻投影，受控ctor未执行完整epoch Prepare/Start/Stop或formal observer；下面956属于前批freeze、不验本批新源码。下一实际epoch的注册observer/profile/Stop/target Start/逆序恢复，再接gameState/判定/rendering。**队列BindHoldEpoch/ReadOwnedJudgeCount已有同epoch计数衔接，不重写或借清零count放宽baseline；target部分Start的Unknown责任先取得实际边界证据。** 无在途build/新包/游戏/UI/账号，6.1 Sol/max代理空答不计交付；R06/fullSeek/B01–B19/production仍未完成，以CURRENT顶部为准。

**最新（11-19，N2进度组合）**：最后字段metadata内撤Clock／换route仍写cache已复现并修复；独立write authority在最后guard、attempted前核同source／Clock，正式Adapter接线。既有Plan／Audio／真实四unmanaged进度缓存／Clock／公共事务六模式145＋source135＋TimeWrite161＋Gameplay238＋rate/publication277＝**956**，30child/原outer28519自然0；Release solution0E/55既有测试警告、独立provider0E/0W。1795输入/10DLL/native/README保持，4修改/2新增及四改前/六改后已在[封存](../../work/n2-seek-progress-participant-20261119/validation-r1.json)核验，[报告](../../docs/2026-11-19_seek-progress-participant-report.md)。签audio→四cache→Clock及反向恢复；实际gameState/HoldEpoch缺席，转换仍API shim、映像只核字节不执行，不签完整Seek/正式provider/Fresh/固定consumer。**下一沿原五参与者补实际gameState/HoldEpoch组合，先核Stop清零Hold count与后续prepare/inverse baseline衔接，勿无复现放宽。** 不重复本批progress/source/TimeWrite/consumer/pitch/owner-fault/相机/N1/退休/R08矩阵。无在途构建/新包/游戏/UI/账号，子代理6.1 Sol/max只读；B01–B19/production/实机仍未完成，以CURRENT顶部为准。

**最新（11-19，N2 Seek source）**：native私有SetTime由实际reservation/nonce+原frame路由给同source，不再误当外部修改；external equal仍撤权。Seek复用冻结source/run/group/binding/origin/adoption/route/Clock和实际policy，返回source-bound writer并只消费同writer successor。provider pending time沿真实continuation而非旧API receipt；原writer内部getter后/native store前核来源，busy source释放保根，准备getter释放source后返回前核对且失败owner清理。formal历史fixture不签Seek门禁保持，旧Seek publication/full-group路径已删除。最终Release solution增量0E/0W（首改后55既有警告未消除）、独立provider0E/0W，八private135+十TimeWrite161+Gameplay238+rate/publication277=**811**、24child/outer自然0，1793输入/10DLL/七native/README保持，[r2封存](../../work/n2-seek-source-operation-20261119/validation-r2.json)与[报告](../../docs/2026-11-19_seek-source-operation-report.md)。r1阶段795仅历史，不能验准备修复后最终源。owned private source反射构造不签formal/Fresh；progress/chart shim输入不签实际progress/Hold/判定/renderer或fullSeek。下一沿原完整参与者核实际四cache/转换profile→gameState/HoldEpoch→Clock提交/逆序恢复，不再重复本批source回调或旧TimeWrite/consumer/pitch/owner-fault/相机/N1/退休/R08矩阵。无在途构建/游戏/安装/新包，6.1 Sol/max代理空答不计交付；B01–B19/production/真实SpeedSeek未完成，以CURRENT顶部为准。

**最新（11-19，N2时间writer）**：actual baseline getter抢占第二time/pitch准备，以及time入口graph getter在busy之前重入写/恢复/释放，已稳定复现并修复。唯一value writer槽与原source operation先保留；registered self/thread、busy先于getter。pending SetTime旧API receipt明确NotReady、同continuation恢复责任保持；恢复前全部非媒体暂停基线独立读回，progress等已变禁止inverse。最终r3十owned161+Gameplay238+相邻rate/publication277=**676**、16子进程/outer自然0，solution/provider最终Release增量0E/0W，首改后55既有测试警告仍在；1793输入/10DLL/native/README保持。[当前汇总](../../work/n2-seek-time-writer-20261119/validation-r3.json)及[报告](../../docs/2026-11-19_seek-time-writer-report.md)。owned确有22source/23physical但缺stock/foundation/Fresh票，不提升正式Fresh/Seek。下一N2正式Seek source/publication运行身份边界与完整参与者同票通路，不重做actual TimeWrite、consumer/pitch/owner-fault/相机/N1/退休批/R08。无在途构建/游戏/安装/新包，6.1 Sol/max偏好；B01–B19/production/真实SpeedSeek未完成，以CURRENT顶部为准。

**最新（11-19，R06 同步故障撤权）**：真实watched worker constructor已Unknown且SuppressObserver、original/physical exit各一次，通知暂存时旧Router/host第一次维护仍Paused/Ready的窗口已稳定复现并修复。poll同步返回retained source/GetTime真实故障，Router当次撤Clock，host物理维护提前至进度节流前；正常policy inactive/逻辑切源不误Unknown，迟到exact fault保持原对象、不可复活。最终r3当前**1003**（Gameplay238+九owned765）、12子进程和outer自然0，两个Release增量0E/0W；首编译55既有警告未消除。1791输入、13DLL、七native身份前后一致，README保持。正常Unity backing实际Start/Pause与新SDK变速恢复零根、publication在途Unknown七根仍通过。[汇总](../../work/r06-owner-fault-poll-20261119/validation-r3.json)及[报告](../../docs/2026-11-19_owner-synchronous-fault-report.md)。旧失败和两处过时测试断言修正均保留，不放宽产品。下一正式Fresh完整执行材料/固定consumer或NEXT具体N2/N3功能，勿重复13-role worker切片，不借owned升正式22-role，不重做consumer/pitch恢复/相机/N1/退休批/R08矩阵。无在途构建/游戏/安装/新包，6.1 Sol/max代理空回复不计交付；R06/R07/R08实机、B01–B19/production未完成，以CURRENT顶部为准。

**最新（11-19，R06 GetTime consumer）**：已接正式 call/native/code profile 的同一已附加 lease、registered self/thread 运行检查；实际 source/read sample 前后核 consumer，末端 receipt native fences 与 run/source/route/profile/唯一 Clock复核。固定null入口仍 CALL0x401218/返回0x40121d，Standalone完整审计、group走既有窄通道。当前r4两个Release增量构建0E/0W、11子进程自然0，Gameplay238+mapped20+standalone24+五owned357=**639**；1790输入/DLL/native身份一致，README保持。[汇总](../../work/r06-gettime-consumer-operation-20261119/validation-r4.json)与[报告](../../docs/2026-11-19_gettime-consumer-operation-report.md)。r1/r2/r3失败及首封存exit1保持，Program副本仅after-dispatch快照。下一正式22-role Fresh/watch/SuppressObserver实际入口；现有Fresh VerifyCurrent→BeginSourceOperation和同票adoption/Clock已实现，不重写，不借11-role owned提升正式组，不重做本批consumer/前批pitch恢复/相机/N1/退休批/R08封存。无在途构建、无游戏/安装/新包；代理6.1 Sol/max仍空回复，由root继续。R06/R07/R08实机、真实Speed/Seek、B01–B19/production未完成；以CURRENT顶部为准。

**最新（11-19，R06）**：完成正常 native SetPitch 后 API 读失败的私票恢复链：source 唯一连续 writer、provider pending fence 验证、内部 recovery 转发、非 pitch 暂停基线复核和完整原倍率实际读回。临时 pitch/DSP 或独立 participant 读失败及 mismatch 可恢复，请求仍失败；永久读失败/外写同值与ABA/切源/进度变化/native异常/publication失败继续 Unknown。最终当前 solution0E/55警告、独立provider0E/0W、Gameplay238/238、19 owned原生场景1086项自然0；1788源输入和DLL前后一致，README保持。见 [当前证据](../../work/r06-pitch-readback-recovery-20261119/validation-r7.json)和[报告](../../docs/2026-11-19_pitch-readback-recovery-report.md)。历史r1/r3/r4/r6失败保持。下一仍R06正式Fresh/watch/SuppressObserver/固定consumer实际入口，不重复本批恢复或R08封存交付。未游戏/安装/新包，正式Fresh与游戏consumer不借fixture提升；R06/R07/R08/B01–B19未整体完成。代理6.1 Sol/max，任务传递空回复不计交付；以CURRENT顶部为准。

**最新（11-19）**：R08 基础预览离线交付已封存：当前 Release solution 0 errors/55 测试警告，82/82短验、采证14/14、独立作者SDK14/14完成；分发subject完整50/50，外层66046因未定义 LASTEXITCODE 实际exit1保留。默认 legacy-default Doorstop r4自然0，7325输入不变、106 MSVC警告。四份r2 ZIP全量读回（38/44/66/53成员）；包场景10/10、假游戏安装10/10和最终seal均自然0，1787输入、12DLL、SDK主体/消费者与包身份保持；只有未参与编译/SDK执行的说明生成器变化。见 [正式证据索引](../../docs/validation/r08-basic-preview-delivery-20261119/README.md)。没有在途交付检查；下一继续R06正式Fresh/watch/SuppressObserver/固定consumer的具体离线功能切片，必要短验后接线，不重跑已过20末端项、SDK14、采证14、分发50或大矩阵。R06正式路径、R07实机成本、R08三场景、Speed/Seek/B01–B19/production仍未完成；本轮未安装/启动真实游戏、未操作账号。子代理6.1 Sol/max只读审查，root串行执行；以CURRENT顶部为准。

**最新（11-18 22:47）**：R06 窄通道/独立Clock与R07后台诊断已完成当前离线整合：十套688/688、25原生模式1608、导出26，solution0 errors。结果为[validation-r8](../../work/runtime-redesign-channel-operation-20261118/validation-r8.json)，前后source/assembly hash一致。下一离线推进R08普通基础插件、schema3安装恢复和当前Runtime采证；三位GPT-6.1 Sol/max仅写独立work目录，root串行整合。正式Fresh/watch/SuppressObserver、固定consumer正向、当前包真实倍率/恢复和同机60秒成本仍待验；R06/R07/R08与B01–B19均未整体完成。本轮未安装/启动游戏，不重做相机/N1/退休批。以CURRENT顶部为准。

**最新（11-18）**：R06 已把暂停倍率的即时和排队操作接到一个会话入口，离线验证覆盖命令终态、重入、取消、未知结果和原生写入。接下来拆分正式倍率操作的安装审计与实时准入，再迁移独立 Clock 读取。真实 Speed/Seek、实机成本、B01–B19 和生产准入仍待验。见[续接点](../CURRENT-CHECKPOINT.md)及[R06 清单](../runtime-redesign/CHECKLIST.md#phase-3-单一播放事务)。

**最新（11-17 23:28，架构方向已调整）**：用户委托独立设计后，“下一步”已完成只读调用链审查与 [.docs/runtime-redesign](../runtime-redesign/README.md) 五份设计。下一实现改为 **R01运行模式→R02入口检查/计时**，随后需求/场景计划、共享观察与唯一播放事务；不自动续旧18380实测、不重做camera/N1/退休批。新Standard明确调整全量运行时代码检测保障，保留实际writer/源/Clock/fences、Unknown和原生退出责任；不能靠缓存成功或删测试伪装旧完整证明仍成立。46审查文件/1513产品输入未改，53链接/8边界/8任务文档核验通过；本批无产品实现/构建/安装/游戏操作，完整B01–B19和生产准入仍未完成。详CURRENT顶部和新审查报告，下方均是各历史时点。

**最新（11-17 22:49）**：readback-provider-r3五Adapter文件/1513freeze，新播放338+formalhost3+四读取竞争场景93+双局SDK884=1318断言、另4Host通过。只去同无consumer段重复验证；实际sample/独立DSP/fresh getters完整保留，末端receipt及route/source身份复核补齐同步重入缺口。ZIP2f9f263…37375585B，22:47:10安装25匹配，新PID18380/Start22:48:07.8563508、实际window23137438初始化；下一仅新会话Pause/Ready/statusfresh/单0.75x/Resume/Seek。旧descriptor包实际source15.02s、Pause→Ready284.26s且心跳恢复，但唯一0.75x180s超时直到22:29:35退出仍无回执，Unknown未重试，不能验当前五file。报告和完整证据见CURRENT顶部；真实SpeedSeek/读出/B01–B19/production未完成。

**最新（11-17 22:02）**：descriptor-observation-r3五file/1513freeze，338播放+3formalhost+3受控SDK场景3869断言=4210及4Host通过；ZIP72d726…已21:59:26安装25匹配，新PID38592/Start2026-11-17T22:00:13.1302671+08:00初始化。查询只观察身份/能力，实际值/Clock/Seek仍full；下一新游戏fresh heartbeat及0.75x单请求/Resume/Seek，不能借旧Ready验当前。详CURRENT顶部/report，B01–B19未完成。

**最新（11-17 20:55）**：source-verification-r1五file/1513freeze，新650+4通过，ZIP ca19dc…已20:51:43安装25匹配，正从Steam正常启动新会话。固定只读来源校验同scope与借用graph递归去重，完整entry/exit、真实sample/original边界保持。下一只新游戏source响应及SDKPause/Ready/SpeedSeek，不借旧832/旧游戏；详CURRENT顶部/新report，整体仍未完成。

**最新（11-17 20:47）**：owner-transition实机相机双绑定，source61.075秒/Paused→PreparingClock307.269秒，SDK Pause Error19不签成功，无Ready。3844已精确结束，当前无游戏。真实12s采样主耗基础身份/页验证非copy。新source-verification-r1五file/1513freeze、六Release0/0+210+3新通过，正跑实际23member typed scope/新338；不借旧832或旧log。下一完成scope正向与entry/exit/copy/thread/sticky→新Host→新包再实测，见CURRENT顶部。B01–B19未完成。

**最新（2026-11-17 20:01）**：Owner/transition/expected池六file已1512freeze，832检查+4Host新字节全0；新ZIP SHA5c62946509b5f47fe2bcfd0c1fa8625f7e720c16f00683742a525db5c256ab14，19:59:32安装25匹配，Steam已新启动3844/Start20:01:03.2832855。当前初始化，官方sky桌面段进行；下一只新会话实际camera绑定/source响应→canDoOperation后后台Pause/Resume→ClockReady/SpeedSeek。不用旧helper65秒/687验新源，不重跑已过832+4，详CURRENT顶部/新owner-transition-report；整体未完成。

**最新（2026-11-17 19:41）**：helper-r2真实source65.2372419秒，Retry same TransitionUnavailable热循环全Group仍拖倒计时；唯一Pause有Error19/未original，未Ready。相机首败camera-outside-rig-transform，旧方法不证明层级。42392已19:38:49正常退出，无游戏；官方sky运行库已恢复，无需人工。下一只合camera-owner双支graph、expected scratch buffer、精确Unsupported同transition idle hint三项候选（当前仍未合/build），新freeze新检查新包再实机，不借687或旧log验新源。详CURRENT顶部/本批reportE09，整体未完成。

**最新（2026-11-17 19:05）**：helper-r2六file/1512freeze，新Hooks210+Camera63+播放338+边界9+helper67=**687**及新链接Host4全0；r1实际helper出口缺口已补并新例复验，恢复字节仍sticky。ZIP SHA2977df032836427ea380c8eff5532d2ed973b418eb36ebdfbda9a478760bc2a5，40/25读回，19:05:13安装25匹配，未启动。CUA接口当前撤回，已请求用户手动新开局，下一仅核新包source响应/相机首败→后台SDKPause/Resume→ClockReady/SpeedSeek。旧70秒source不验新源，不重做已过687+4或退休批。详CURRENT顶部/新helper-image-report，B01–B19未完成。

**最新（2026-11-17 17:28）**：persistent-reference 新包736+4/25安装匹配已实测：外观45真实绑定恢复；相机graph仍拒绝，诊断待合。source70.221616秒、唯一SDK Pause sender至回执76.514717秒/immediate true，但无后续Paused样本；回执后337.651954秒才PreparingClock，未Ready/Speed/Seek。12344正常退出等待61秒后精确结束，CUA已completion，当前无游戏。下一只修StockSourceHub同helper image每成员3遍/整组至少66遍扫描的实质重复，保留真实消费/回调边界；候选尚未合入，不能借736验新源码。详CURRENT顶部及新persistent-reference-report，B01–B19未完成。

**最新（2026-11-17 17:11）**：常驻controller发现bug已修（两个backend+四测试），同traversal ordinary重复校验两Runtime文件也已合。新freeze1512/8diff，七构建步骤0；210+52+60+338+48+28=736项及4Host全过。新ZIP7cd9511b…/37369139B，40/25读回，17:07:18安装25匹配；当前新game12344/Start17:08:48.8851647/窗口1117310初始化，CUA段尚进行。下一仅新包Camera/Appearance发现与实际后台Pause/ClockReady/SpeedSeek；旧occurrence source78.4秒/Pause后388.84秒仍未Ready只定位问题，不验新源。非空ordinary仅单PE双片，23组members0；整体B01–B19/production仍未完成。详CURRENT顶部。

**最新（2026-11-17 16:14）**：四Runtime修复已新freeze1512/build4步骤0，Hook210+播放链338+supporting143+caller/gap/header97=**788**全0，新链接Loader actualHost4/4；ownedStock=null，不签实机。新ZIP37368320B/SHA90fe03bfac107e5b8af0618443535c236c0c4e1282a41fe9eb9775781af8a1fb，40/25读回，16:13:06仅安装25匹配。当前无游戏/CUA任务，**CUA已恢复**。下一从新包真实开局/耗时→后台Pause/Resume→真正ClockReady/SpeedSeek，旧r3实际Pause/Resume和90.88秒source仅历史，不验当前修复。不要重跑当前788+4或退休批。详CURRENT顶部/本批report，整体未完成。

**最新（2026-11-17 16:04）**：CUA已恢复并完成r3真实session；后台SDK Pause因果成立，Resume回执后144.90秒观察到Playing，再Pause成功但PreparePausedClock仍分钟级卡。source90.875657秒；无Speed/Seek请求。正常退出120秒无响应后精确Stop35088/CUAcompletion，当前无游戏。新actualstack确认producer ctor10次整组重复验证，以及pair同次gaps+完整节双读，已合四Runtime候选（其他SDK/Adapter/native未改），正47412构建，**未签新源码测试或新包**。下一完成当前新源338/定向caller与gap/header、宿主链接核验，旧r3游戏不能验新源。详CURRENT顶部。

**最新（2026-11-17 15:33）**：请求入口当前为r3，已修旧session拒绝覆盖旧实际回执导致sender被挡：只status报ScopeExpired，保留旧ack/未知状态，不执行。r3新freeze1512/8变化、三步骤build0、27/27，sender7场景同字节继承；Examples SHA49fb1e95…，其余20运行项保持。新ZIP37366478B/SHAac74e0e22fa1062aec7e079c95e6fe98c5bfc5739ef41eecea20ab25e01ebd14，40/25读回，15:30:59已安装25匹配，无启动/实际控制。下一仍等人工开局保持前台，按约定20秒后后台SDKPause，核对真实结果→Resume→ClockReady/SpeedSeek；r2包/旧游戏不能验r3。见CURRENT顶部。

**最新（2026-11-17 15:26）**：已补SDK-only opt-in调试mailbox，通过当前Storage Data→FrameUpdate→actual Acquire/显式Execute，精确session/run/generation/nonce，32请求/TTL，Acquire期间取消或过期不执行，失败回执补写不重放。r2新build三步骤0、26/26；发送脚本7假目录场景0，含旧PS时间串兼容；原691+4仅同字节继承。新ZIP37366421B/SHAa62f72147816b66ef3bd91fc0825395e86579b8981c73a438213dbea9e5ce1ac，40/25全读回，15:23:25已安装25匹配，**没有启动游戏或发送实际请求**。CUA仍无工具，已请求用户人工开局保持前台；下一新会话后台Pause→Resume→ClockReady/SpeedSeek，避免旧焦点自动暂停误作SDK因果。详见CURRENT顶部/`docs/Runtime-probe-control.md`；B01–B19未完成。

**最新（2026-11-17 14:54）**：TargetFamily去重9 Runtime file已freeze1506/新build，Hooks210+当前四case338+actual23组本地证据143=691，新链接Loader4Host场景通过；ownedStock=null，不能作Stock Inspect性能正向。新ZIP37326854B/SHA2eb7f95e45e89c3ec0af5b1f6103a786dca786acebe7c7d5bc836e54265eb1ba已14:52:39安装25匹配，未启动游戏。当前CUA工具不可用，下一优先本新包实际SDKPause/Resume与ClockReady/SpeedSeek；旧游戏结果不验新字节，不重跑691+4/退休批。B01–B19仍未完成，详见CURRENT顶部。

**最新（2026-11-17 14:30）**：RD-InFalsus-Mod参考改进已落地：普通SDK主线程shutdown、同帧停止/清理故障记录、队列满命令重试、坏JSON不阻断后台5秒包变更提示。新Loader构建、Hosting20/Lifecycle14、实际Host受控4case与命令脚本9均通过；Runtime保持df8c4276，对应210+338+actual组Dobby48=596，不重跑。新预览包37326691B/SHA63f491e46a5a74b5fc16c2c7d69558e2198a88aa6c7f636ea5bad9fa06b30207，38/25读回，未安装/实机；当前无游戏。下一继续Targets.VerifyCurrent重复Stock Inspect或新包实际Pause/ClockReady，完整SpeedSeek/B01–B19仍未完成，见CURRENT顶部。

**最新（2026-11-17 12:33）**：composition-read实际接管仍127.13秒，Resume dispatch32.73秒，无实际恢复/ClockReady，11456已精确结束/CUA已completion。dobby-traversal五file同锁共同父验证已新freeze/build，Hooks210+新字节Replay/Resume/拒绝338=548通过；正补actual23组Dobby entry/exit字节破坏及sticky定向case，未pack/install。Target VerifyCurrent嵌套Stock Inspect乘数仍在，详见CURRENT。用户新增参考RD-InFalsus-Mod，public clone HEAD617153d0/ MIT，仅参考薄加载/Core、资源刷新、局部生命周期；需要对照已有实现后落实真实gap，非照搬异游戏偏移或旧loader兼容。

**最新（2026-11-17 12:13）**：composition-read同锁两file已冻结/新构建，Hooks210+当前23组Replay/Resume/直接写拒绝338+普通/新ForComposition48=596全过；native未改仅继承原字节442。新ZIP37315227B/SHAf30bfac4c07a79b54adbc3e54497e8b9d28e320bed1d8d05eb45aa9c880442cc正安装实测。下一只核本新游戏响应及SDKPause→ClockReady→SpeedSeek，旧35548已结束；不借旧游戏恢复验本改动，不重跑已过回归。B01–B19/production仍未完成，详见CURRENT顶部。

**最新（2026-11-17 11:59）**：scheduled-engine-child新包实际恢复到Playing=true/Paused=false，旧来源拒绝未复现；按钮至恢复250.94秒，暂停无SDK回执，未ClockReady/SpeedSeek，exact child无独立成功记录。35548无响应已精确结束，CUA已completion。下一正在Runtime仅两个文件内合并同锁composition重复普通成员扫描及共享Dobby seal扫描；保留所有独立native getter/票的fresh边界。新施工未build，不借780验新源码；接着唯一串行构建+ordinary破坏/恢复sticky定向回归，再实机解卡和Pause/Clock。详见CURRENT顶部。

**最新（2026-11-17 11:34，以CURRENT顶部为准）**：scheduled-engine-child已实现并冻结，native+11托管全0、新调度native442+新字节既有机制338全过。新ZIP37315127B/SHAb99935c55dbb44642b771604e00f7f69b1be26b1f06386fba0a769376abc2d67已安装25匹配。真实35548/窗口1311806/绑定scheduledGame，已PLAY GreatnessEarth EASY4 FREE并加载；尚未本包Ready/SpeedSeek。只续本新会话接管/Pause/Resume/Clock，不重复已通过离线组，不用旧V2实测签新行为。B01–B19整体和production仍未完成；下方“施工未build”仅历史。

**最新（2026-11-17 调度内部Engine写入，施工中）**：V2新15248实际确认parent=PlayScheduled/Double1，Enginechild首败bit6 ParentLowerSlotMatches（7f/3f），不是lower参数问题；未Ready/SpeedSeek，游戏正常退出。固定Game→UnityPlay→完整BB74C0→BB6F20→Engine链已闭合，值来自native当前pitch。正在接独立schedule-original窗口/精确caller+private root+pitch/fence/原退出、stock完整profile及Producer物理子树；不造新票、不清flag。**新源码未build/测试，不借V2的11/338通过。** 下一仅合新行为/新定向检查后实机；详见CURRENT顶部。

**最新（2026-11-17 Engine child V2已安装，以CURRENT为准）**：6native+2CLR同次136B诊断，原短路求值记录parent/Begin/Child/函数内caller，不放宽身份/票。1272输入冻结，新native+11托管全0，当前4进程338=113+133+46+46全0，owned仍无formal stock/Unity/Clock/game/production。新ZIP37,305,899B/SHA9a9173ded64c0b7c7769da7c463adea65222a7501e088c054da3e806704d0e04，35/25读回，10:43:53安装25匹配；新15248 Start10:43:55.7105907+08初始化中。下一只续本新会话实际Pause/Resume取真实parent与首个失败位，再修音调child链；不借旧V1游戏/mask2或旧292验新包，不重跑11/338/退休批。完整Speed/Seek、B01–B19/production未完成。

**最新（2026-11-17 已取实机mask，V2施工中）**：新16288实际接管141.997524秒，SDK Pause精确事件/Resume dispatch已观察；之后status4明确origin source/mask0x2/当前EngineSetPitch kind8 seq11 parent10 depth3 float1.0 childFlags0，无ClockReady/SpeedSeek。游戏已正常AltF4退出无Stop。静态lower495B原样传float/唯一固定Engine CALL已核，尚无parent10真实role和短路条件，不能猜放行。新engine-child-diagnostic V2/136B仅补实际parent/tested/matched/函数内caller offset，6native+2CLR/新独立runner施工，**尚未build/测试，旧292不能验新源码**。只续这条实际音调child链，不重跑退休批/旧矩阵，详细CURRENT顶部。

**最新（2026-11-17 fresh-block-diagnostic，以CURRENT短页为准）**：新4native+2CLR把同一次Read的status4拆分为origin/闭合mask/当前scope标量，不新增查询/票、不放宽失效。1272源冻结，新native+11托管步骤全0，当前真实owned23/23+3pair三case292=113+133+46全0；r1仅runner编号错误、r2修夹具，产品不变。新ZIP37,314,033B/SHA7c8b96594520b20c2a63eccd2ac3d0eaf3d2b2eec8c341819b148257d244bb80、35/25读回，09:46:51实际安装25匹配。Steam选默认正常游戏后新16288 Start09:56:00.8748853+08正在初始化。下一只续本新会话取得实际nativeStatus+origin/mask，再修真实路径，不能拿旧receiver nativeStatus4或旧246验新包；不重跑已过11步/292。旧receiver2696已确认无进程/窗口，具体退出因果未知。正式stock/Unity/Clock及完整游戏Speed/Seek、B01–B19/production仍未通过。

**最新（2026-11-16 receiver-read已安装，以CURRENT短页为准）**：四file已新冻结构建，11步骤全0，当前owned occurrence66、supporting138、普通组204、新receiver50均0；纯receiver同traversal和fresh graph/DSP同边界去重、初暂停候选Role筛选、单次nativeRead状态分类已接。新ZIP37,311,070B/SHAbfaa4effc399c07114a44ebb7524ee97e241a939a43da0141444a0f23519bbe8、35/25读回，23:24:25实际安装25匹配。Steam启动选项后新2696于23:26:31运行，新session47d0eeca…source prepare中；只续新会话菜单/Clock/接管/SDK Pause/Ready及nativeStatus，不重跑已过离线组，不借旧noeffect游戏验新源码。owned均无foundation/Unity/正式Adoption/Clock/game/production。旧noeffect Resume Applied后native lifetime失效已正常退出，完整Speed/Seek/B01–B19仍未完成。

**最新（2026-11-16 receiver-read施工，以CURRENT为准）**：noeffect-read新50960首Clock19.1339981秒/接管209.1866040秒返回WaitingForPause；actualPaused后仍等、SDK Resume一次Applied，但随后native fresh-source receipt/lifetime changed撤销，Ready/SpeedSeek未过。实际after-resume栈为Host clockProfile→group。游戏已正常AltF4退出无Stop，23:05:59进程0。新receiver-read-r1仅施工：纯receiver元数据同traversal、fresh graph/DSP边界合并静态37→16、首Clock候选只选Pause/ProgressPlay；同一次nativeRead失败status诊断和新receiver实际组回归准备中。未freeze/build/打包，不用旧noeffect结果验新源码。下一统一新构建/相关检查→新包新会话，保留票据拒绝，整体B01–B19/production未完成。

**最新（2026-11-16 noeffect-read，以CURRENT短页为准）**：首个accepted readback前的正常原生零child返回仍无执行票，但以当前private Call/completion与精确fences识别后保留同fresh来源等待；其余拒绝仍关闭。有界root/args/child诊断已加入，两Sol审查无阻断。1243新源码冻结、11串行步骤全0；新actual23/watch/producer两case66断言通过；新五mode138/23-member+3pair及普通204/12-member也全0，只是owned机制，无foundation/Unity/正式Adoption/Clock/game。新ZIP37,309,425B/SHA0b37e7ef5c7960a663ba96cb6a34858b9d794e090109d344170dfabc8ccfb12b、35/25读回，22:46:44安装25匹配，新50960于22:46:46启动，仍初始化，尚未菜单/Pause/Ready或SpeedSeek。旧idle49744接管211.66秒后leaf mismatch撤销、实际Paused但无SDK票，已正常AltF4退出；旧日志不验新源码。下一只续本新会话及实际source拒绝诊断；整SDK/B01–B19/production仍未完成。

**最新（2026-11-16 idle-read已安装，以CURRENT短页为准）**：supporting-read实际首Clock19.0731920秒、接管210.5761087秒后WaitingForPause，后续仍21–28秒空轮询而SDK Pause/Escape请求无回执，正常退出>60秒无响应后22:04:22精确结束旧3056。root新dispatch只在same actualrun/no first candidate/no transition的WaitingForPause维护faults/drain后推迟无输入Clock准备，候选仅调度信号，真实回调发行/完整准入不变，独立Sol审查无阻塞。新3Release/clock14/host3通过；r1最后Gameplay漏传自有fixture172/201失败保留，只补子进程输入新retry201/201通过，源码未变。新ZIP37,308,123B/SHAa7a8af240bb151caea59f14afc50ed7cdf6dff9eac37573b6417ebf4c4dd9571，35/25读回，22:11:19实际安装25匹配，49744于22:11:21启动。Runtime/Core/SDK逐字节同supporting，旧138/204只继承三库范围，不验新Adapter/game。下一只续新49744初始化/实际空闲响应→SDK Pause/ReadyExperimental与SpeedSeek，不重建旧矩阵，不借旧游戏验新源码。B01–B19/production仍未完成。

**最新（2026-11-16 supporting-read已安装，以CURRENT短页为准）**：13Runtime+3Adapter去重已冻结，沿显式typed同次Traversal共享固定stock/lower/Engine/Time祖先，full入口/出口/foundation及所有member本地证据保留，独立无票仍fresh；reservation纯投影与采样外层读责任拆分也已接线。新11步构建/检查自然0；五独立进程23成员双映像真实23/23+3pair，138运行断言，四改写拒绝/恢复仍sticky；当前普通12成员raw/SEH/interference共204运行断言也新自然0。owned没有foundation/Unity/Clock/Claim/正式Adoption，不能代替游戏。新ZIP37,307,282B/SHA643b2f966f7e3b8fbf5d09d997933a2ecbe8cbf17b8fd1e141a12c4e3104fe81，35成员读回，21:46:51安装25/25匹配，新实际3056于21:46:53启动，初始化中。下一只续本包菜单→首Clock/接管→实际SDK Pause/ReadyExperimental与Speed/Seek，不重做本批离线检查或借旧game验新源码。旧group-read接管365.6946435秒后已返回WaitingForPause、Clock29.4589574秒，已正常AltF4请求退出；新包尚未实测通过。B01–B19/production仍未整体完成，详见CURRENT。

**当前（2026-11-16 supporting-read施工，以CURRENT短页为准）**：group-read-r1新13420/session1916f94d63764d0981bc0f358240e0b8、GreatnessEarth EASY4 FREE首次Clock29.4589574秒，真实Host由07:46:35 CapturingSource推进到07:52:41 WaitingForPause，约365.6946435秒，接管已返回。CUA正常Alt+F4请求后实际窗口/进程消失，无强制结束；SDK Pause/Speed/Seek未验。新第二栈指向同次member/pair/proof中反复stock/lower supporting检查。两Sol分别实施Runtime沿既有Traversal显式传票的祖先去重，以及Adapter同次reservation纯投影/Read外层读责任拆分；后者已冻，前者施工中，尚未新构建或打包。旧group-read11/9/4或游戏结果不验新源码。下一冻结→统一短构建/相关实际23成员双映像自有检查→新包实际Pause及Speed/Seek。B01–B19/production仍未整体完成。

**当前（2026-11-16 group-read，以CURRENT短页为准）**：新VerificationTraversal将一次Group.VerifyCurrent内部重复targets合并为入口/退出完整检查，中间显式传同group/thread/双锁/generation私票；foundation仍两端完整验证，owned无foundation路径也同样scoped，独立操作fresh及canonical全节/member/getter/root保留。新11步与9typed全0；新增真实无foundation组raw/SEH/interference三模式99/44/61=204运行断言，每child12实际Prepare/Commit，当前执行DLL与包一致；额外r1旧fixture缺导出失败原件保留，不改退休runner。新ZIP SHAe705d78faa6bc0cb6d106da7a6d3f0a9efeb3fd3ef314b9742f3c59afc747a4c已安装25匹配，15:36:58新13420正在初始化。上一source-read实际Clock40.806877秒，Host仍卡逾5分钟，15:19:31正常退出请求无效后精确结束42604；这不是新包验收。下一只测新13420首次Clock与Host接管返回，再推进完整七根/Seek/HoldEpoch/实际DSP；B01–B19、完整Speed/Seek与production仍未完成。详见CURRENT。
**当前（2026-11-16 source-read，以CURRENT短页为准）**：clock-read 本候选实际首次 Clock 50.222秒（前版294.328秒），随后 Host CapturingSource 仍卡约9分钟；两实际包不是新源码验收。现已合并一次 Group.VerifyCurrent 内 foundation full验证（入口/退出各一次，中间私有 ranges），并合并两组构造时纯 Binding/Origin 投影；真正 getter/BorrowRoots/控制前后 fresh 与整节 canonical 检查保留。新11步构建检查全0：Hooks210、Gameplay201、clock14、host3、旧fresh机制31；九步自有typed broker检查全0（不是正式stockgroup/Unity正向）。source-read ZIP SHA05395217e74180632a09d1b8876a7cfb90251f5f8626fe712a2458c720458805已安装25文件匹配，新42604于15:07:58启动，仍初始化。下一只验本新候选首次 Clock 与真正 Host 接管返回，再推进完整七根/Seek/HoldEpoch和实际DSP。Speed/Seek、B01–B19整体未通过；主README不改。当前实际游戏操作已获用户授权，下方历史禁用说明不覆盖该新授权。源码/新验证/强制结束旧41872边界详见CURRENT。
**当前（2026-11-16 clock-read）**：native-coroutine实际已读出语义、无新镜像首差，但同Read最多8次全来源刷新耗294.328秒；已用显式consumer scope合并当次freshprofile，nested独立/finally清理，真实native转换/reader/source/run检查保留。新九步全0，Hooks210/clock14/host3/Gameplay201；Runtime/Core/SDK/helper未改且打包逐字节核对，不重复原生矩阵。新ZIP SHA`839205a3f36fe2d93da3eda9102de8e1194d30b19fb66824823760e523d31596`已安装25文件匹配，新PID41872初始化中。下一只验该包首次Clock耗时及Host接管阶段，Speed/Seek仍未通过；此前10856已正常退出请求超时后精确结束。详情CURRENT，整体未完成。

**最新（2026-11-16 native-coroutine）**：native-judge新开局首差已推进到Hold402670。协程真实Prepare/原跳板/continuation/branch、具体bridge的kind/slot/config回读已接同私有registry，完整bodyhash只还原branch。新helper88导出、三role回读278运行CHECK、Release/Hooks210及自有九步四普通+Judge+Coroutine/正文tail正反向全0。新ZIP SHA`78a434ef64513671876ab4d80fd85e88765da87cdaec1e82010be6ef9333c608`已安装25文件匹配，新PID10856初始化中。下一先本候选真实开局取新首差/Clock，再推进完整Speed/Seek；不能用旧游戏或旧fixture验新源码。详情CURRENT短页，整体未完成。

**当前（2026-11-16 native-judge）**：已接真正NativeJudge Prepare original、完整指令/continuation/branch与私有owner，以及真实native配置只读回执；新helper87导出、两原生模式、新Release/Hooks210和五种自有broker模式全0，typed模式共10实际私有成员组合、原函数/回调各1、detach与篡改sticky通过。新ZIP SHA`69c3bf74c18635806a5bc897bc908aeb05e0b12953ff23ad0d06063dedc9ce5c`已安装25文件匹配，新PID44860初始化中。下一先本候选实际开局，取新首差或Clock成功，再推进Speed/Seek；不使用旧late-retry实测验新源码。所有代理已冻结，构建串行结束，详情CURRENT短页。整体未完成，Coroutine未在本批接线。

**最新（2026-11-16 late-retry-r1）**：完整late-prefix会话还余Controller/ResultRetry、ResultStart共三条警告/两种入口，已补并新跑七种原生前导、四模式、Release/Hooks210全0。新ZIP SHA`377e5638660f198477c613a8651fd1b70c54e575b44e5e564b105ac3e3a99944`安装25文件匹配，新PID44372初始化中，下一先本包真实开局。Sol正另行实施NativeJudge实际Prepare original及native config readback的私有组合身份；这批新源码未编译测试，不得借旧包、210或owned-r2验收。详见CURRENT顶部，整体未完成。

**当前（2026-11-16 late-prefix-r1）**：late-basic真实新开局已跨过Pause/Resume首差，下一自身普通判定3C0310。已补同会话五种完整PC-independent入口，Release/Hooks210及同字节自有原生四模式全0；normal实际原跳板搬运10/6/11/8/7B、原调用和前后回调各1、五个真实私有owner都确认。新ZIP SHA`d5e1c7d2020631ef6757dcee37dfa0210dbdfd2d31dde9d6dc3920650a596bd0`已安装25文件匹配，新PID14932初始化中。下一先本候选真实菜单/开局，取新首差或Clock profile，再推进Speed/Seek。不得用late-basic、旧owned或Compiled代替本包实际游戏通过。细节见CURRENT顶部和`docs/2026-11-16_late-hook-composition-report.md`；整体未完成。

**最新（2026-11-16 late-basic-r1，以CURRENT短页为准）**：seal-diagnostic首差已证实为自身Pause@3A2410；已接普通broker晚装私有owner、固定generation、独立canonical preimage与真实5/6B patch，group执行区每次仍整节canonical比较。新四构建步骤全0/Hooks210，自有MASM四模式全0，仅自身代码。late-basic ZIP SHA`ffc7dffdd062fd4894d628e9994ecf427634a8c168d471a340e14abf8a84225d`已安装25文件匹配，新PID25720真实启动，正在初始化，尚不签游戏/Speed/Seek。下一先实际开局取新首差；Coroutine/Judge仍无完整组合身份，不能放宽未知修改。旧seal会话SDK Resume已接受/Pause成功仅属旧包。整体SDK/B01–B19未完成，不重复已完成相机/N1/退休批与长矩阵。
**当前（2026-11-16 cold-r2，以CURRENT短页为准）**：主菜单/无controller时不再由Probe、独立DSP或身份发现隐式捕获GetTime；实际时钟/映射/控制仍当次刷新来源。Host无局只维护故障/完成记录，真实run仍先全组验证。含前批member联合检查。新owned夹具+四Release+Hooks210/clock13/host3/Gameplay200全0，cold-r1漏夹具的29失败原件保留。新`artifacts/delivery-20261116/RizToolbox-0.3.0-cold-preview-20261116.zip`已安装25文件匹配（SHAa94f1fb7fa29a0670403ad2f6f192a88a523f7b057528ca26a6300a645a1eda8），实际40500于11:34:59启动，仍安装中，尚不签响应/Speed/Seek。用户已明确恢复实测，不为旧Escape再问。下一先读本新会话，再实际菜单/开局/暂停与完整七根Seek；不重跑旧批/长矩阵，不使用queries的旧游戏证据验cold源码。整体未完成、goal active。

**当前续接（23:33 queries）**：batch-r4实际physical全组/WaitingForRun/adapter/Unity host均已初始化，但维护仍卡顿。后续20条body批量、Clock联合查询、Compose和空观察者重复检查已由root+3Sol完成；queries-r1四Release构建0、Hooks210+clock11通过，新增mapped负向仅编译未执行（旧runner缺当前Unity前提）。**新查询源码尚未打包/安装**，不能拿现有batch游戏结果验它。实体Escape停止了电脑操作，未退出/结束42952、未覆盖游戏，等明确恢复授权才继续游戏侧。先按CURRENT顶部离线打唯一queries包/更新说明，再在允许恢复后安装和实测；不重做旧功能或长矩阵。整体goal active、仍未完整交付。

**当前优先（2026-11-15 batch）**：在聚合/成员分层之后，64条原生来源记录已改为同私有token/同锁下整批读取，前后完整producer核对、CLR逐行检查保留。native新helper86导出、四mapped模式1336/1336、Loader/Examples/Hooks构建及Hooks210/210全0（batch-r4）；新batch ZIP SHA64dd22364f7cc0d5563f69130bfc9013a9236f3d68f643e14a15c442e780c4f5已实际安装25文件匹配。真实新游戏42952/session151832392Z初检1.610s、source准备约6s，正在安装，未签启动完成/游戏Speed/Seek。先读该新会话最后阶段和首错，再做实际下一缺口；不要用下面旧包状态重做功能。详情CURRENT顶部；全七根/HoldEpoch/时间精度及其余B项仍未完成，goal active。

**最新（traversal启动修复候选）**：两次actual线程栈+阶段日志已确定整组验证中成员反复重验共同targets/兄弟完整镜像，造成初始26s及单source准备约62s。已拆为每次聚合入口的共同验证+每个成员完整局部验证，独立访问与实际写入前后检查保留，无跨调用缓存。三构建0、既有Hooks210/210；traversal ZIP已实际安装25文件匹配（SHAc25c7402932bf491cb63ec113bf1d4bb66890a557f4a8e303351235ffa6cb936）。Steam旧运行状态已清除并重新启动，先读新会话耗时/下一具体错误，尚不签恢复或游戏Speed/Seek。详情CURRENT顶部，原stack与日志在work/game-seek-20261115。

**最新（2026-11-15 broker）**：proof实际首差3BEA60已对应自身ControllerOver ref0观察器；现在有真实安装前原字节/永久owner、安装后branch slot/thunk/original及同pin文件组合的闭合接线。Loader/Examples/Hooks三个构建0、既有Hooks210/210；新物理正向未借fake签发。broker-preview ZIP 37,291,114B/SHA1f64e580b0209ad9689081d544b1509cdf1c01fb23c51fe373100cf451c8b4ab已逐成员读回并实际安装25文件匹配，Steam启动已请求。先读新会话是否消除此首差或给出下一具体错误；详见CURRENT最新段。未知前导/未来新增hook仍拒绝，不把本次局部接线说成全broker/全Seek支持。SDK合同未变；不重跑旧56/SDK14，不重做已完成相机/N1/退休批。

**最新实际阻塞（2026-11-15 proof）**：controls包已真实启动，原lower-time tailJMP错误消失；同新会话下一失败为NativePlaybackExecutableSeal完整可执行段不匹配。foundation已组合64真实写区间，不凭猜测豁免。新proof包已加模块/首差RVA/16B有界诊断并接控制订阅及单环操作示例，Loader/Examples两构建0、逐成员读回0，25文件已实际安装匹配。新游戏32708已启动，先读取其新会话首差，再复用可信owner修实际来源；现阶段不重复controls56项/旧SDK14。包SHA/日志来源在CURRENT最新段；SDK合同未改，配套controls SDK仍有效。完整游戏Speed/Seek、七根/HoldEpoch及剩余B项未完成。

**最新（2026-11-15 controls）**：正式 stock lower-time 的三字节尾跳转误拒已修；圆环颜色实际 setter/缓存/渲染双读回及恢复、玩法控制 scope-owned 订阅已实现。五个串行 Release 构建0，clock11+订阅3+外观10+模块32=56项0；同新SDK引用包的独立离线消费者编译0W/0E，未复制SDK DLL。新 runtime/SDK controls-preview 两ZIP逐成员读回通过（de1db6），详细SHA和来源见 CURRENT 顶部与 `artifacts/delivery-20261115/controls-preview-receipt.json`。子代理统一Sol/max，均已本批停写。

**下一推进**：新 controls 包尚未安装；此前实际 cleanup 游戏日志里的 lower-time wrapper 拒绝是本次确定修复依据，不是新包游戏通过。先正常退出当前游戏再安装新包，确认这条阻塞消失，随后处理实际下一错误；完整七根Seek/HoldEpoch/真实时间精度、Speed/Seek正向及剩余B项仍待完成。前台后置时继续具体可离线功能缺口。圆环仅接纳cache==renderer基线；订阅是维护点观察，不自动取得控制。不要重做本轮profile/圆环/订阅/外观降级/TimeWrite恢复或旧相机/N1/退休批。以下记录为历史，冲突时以本段和CURRENT最新段为准。

**最新（2026-11-15 cleanup）**：本次已识别新child的完整子树来源记录、原pool/list/backing恢复、同步销毁及独立死亡读回已实现；同次Disappear填旧数组再扩容的旧backing私票已接正式Recognize。Canvas/Line成功逆后的独立根释放与失败撤销也已实现，不再把这些列为缺功能。最终cleanup-r4三构建+Renderer4入口自然0（27634/5b61d4）；未变Canvas18/Line38各1/1来自cleanup-r3（18603/1c5814），不混称同树全矩阵。新`RizToolbox-0.3.0-seek-cleanup-preview-20261115.zip` 37,236,147B，SHA 8d55b13458c35acc88f93aa41dade8a4946881c03429e82e78568d8fe66bf6d4，逐成员读回0/f7fb69。

**下一推进范围**：正式七根Adapter整体Seek、实际音频时间精度、完整HoldEpoch正向及剩余B01–B19的具体功能缺口。同步DestroyImmediate只在本experimental恢复范围使用，游戏回调阶段尚未验证；完整SDK/production未完成。三位Astra本批停写，构建无在途；本轮未装/启游戏。以CURRENT顶部为准，不重做本轮清理/旧17检查，不恢复旧长矩阵/归档门槛。

**当前下一步（2026-11-15 Seek 新预览已交付）**：ROOT 真实 SetTime连续writer、正式provider/SDK/示例、进度/判定/可见性/几何/Clock及HoldEpoch停止/续接/原迭代器恢复已合编。最终seek-r4四构建和17相关短检查自然0（session68420/c3d53e），新`RizToolbox-0.3.0-seek-preview-20261115.zip` 37,223,524B，SHA c32a8f9360034408a8fedfcbedf8d9ecfb8b6b853c7fa4a58128d00de134b8ba，打包逐成员读回0/85e656。3位Astra本批收尾，root单路构建；无需重做本批接线/17checks/旧相机与已退休批。详细边界见 CURRENT 顶部。

**下一功能优先**：新 Unity child 在失败恢复时的精确销毁/退休逆，以及其余实际engine/SDK覆盖。已实现的正常池接管允许成功提交；失败逆不完整仍如实报告未知并保留责任。完整七根Adapter/HoldEpoch正向、真实音频时间精度、完整游戏Seek后续集中验证；新包仍为实验预览。旧successor等ZIP未改；完整SDK/B01–B19未完成，goal active。不按下方历史“完整Seek恒关闭”或旧冻结规则重复施工。

**本轮后继已交付新预览**：Fresh→logical普通后继、Adapter同票跨run捕获/同runStart、实际completed-initializer私票post-Poll消费及formal host/provider换代均已合ROOT。最终successor-r3 Loader/Gameplay/Examples及host3+SeekClock6自然0；native-r2新增初始化同局72+跨局72自然0。新包`artifacts/delivery-20261115/RizToolbox-0.3.0-successor-preview-20261115.zip`，37,156,831B，SHA ef480c98c28b1f6761c1df07f9ef5279ea63c21271a1604a9558eade06dd2791，逐成员读回通过。正式stock七根Adapter/真实游戏未动态验证；缺实际record的blocked来源仍拒绝。详情见CURRENT顶部，不把旧native-r1的63或旧feature-preview验新源码。

**下一功能**：完整Seek仍关闭，继续把实际音频/进度定位和既有判定/Note/Hold/渲染重建参与者接成完整可恢复事务。本轮已有暂停Clock定位/恢复部件以及示例面板三个SDK变速按钮；不要重复这些已完成项，不用Retry或单纯Supported标记冒充Seek。其他B01–B19/engine覆盖仍未完成，功能优先，维持短相关检查和单编译，不恢复长矩阵/逐批ZIP证据。

**最新功能已合ROOT并出新预览**：同Fresh来源的Clock完整发布、默认正式host、SDK读数和显式experimental SetSpeed的真实writer/读回/恢复已接通；最终Loader/Gameplay编译0错误0警告、host3检查通过，新native arm/cancel/progress三路径通过。新功能包为`artifacts/delivery-20261115/RizToolbox-0.3.0-feature-preview-20261115.zip`。真实游戏未验，首来源后继handoff仍关闭，完整Seek/生产准入仍未完成。下一步优先补Fresh→已有logical handoff的真实转换和后继来源生命周期，不能重复重装/重stage新第一票；详细当前限制见CURRENT顶部。此前1458/1174/1172各快照只作历史，不再阻止主目录继续功能开发。

**2026-11-15最新工作方式**：用户要求功能实现优先、检测归档后置。以CURRENT-CHECKPOINT顶部“功能优先、统一开发版本”为准：旧main-r5已主动取消，未完成/非PASS；不再以72/96步骤或旧候选归档作为合ROOT前提。先把1458已完成功能合ROOT，再由3个Astra分别完成原生Clock、Adapter同票消费、正式provider/宿主接线。编译串行，每批只做必要关键检查，完整回归与长矩阵集中到功能阶段完成后；后续汇报以新增可用功能和具体阻塞为主。

当前优先级（2026-11-15 17:06）以[续接点](../CURRENT-CHECKPOINT.md)为准。**当前1172候选已实际启动并通过有限游戏流程，预览包已生成**；完整SDK、B01–B19、真实Speed/Seek和生产engine准入仍未完成。

**最新1174限定候选**：正式stock LowerTime成员组合与clock profile迟到恢复/旧capture刷新顺序已实现并编译。Gameplay184+Runtime383+Module31=598普通例；focused9包含在184；同新Runtime/Sdk的5组owned机制全自然0。source1174+Build1、5diff，ROOT仍1160冻结；没有完整27套、stock mapping正向或production准入结论。[报告](../../docs/2026-11-15_clock-profile-readiness-report.md)，26,114,640B ZIP SHA4d188b1a8362874cba9ec4f6fdd2867b56e43af092a3ca83e8a842f4d28dc382；root独立ZIP-only0/9fe75d。r1漏fixture原失败保留，r2原wait0/0d9b7a。新FreshSource一票一factory在独立目录实施，尚不属于此1174图或现有预览；后续正式rate provider仍须接线，不可复用fixture权限。


**FreshSource独立候选已限定归档**：1450完整输入、8diff，owned四receiver根51+51+17=119检查全过；stock七根factory只编译/review，未执行。新native五自然0，原工具失败与Adapter child成功区别保留。24,211,324B ZIP SHA441a60a5a663aa066dbf488ed7a48f5a8200a90738f7d72ca48df8e51ea260de，root独立ZIP-only0/062b20；[报告](../../docs/2026-11-15_production-fresh-source-report.md)。未与1174混验，Clock/Router/provider/production/game/未观察ABA均不签。下一新候选production-fresh-adoption只接exact capture→唯一Router/source/readback，沿用原七根与原watch并先登记真正producer排空责任，不走DTO ctor或重stage。Astra实施、Sol复核，尚无新候选执行结论；此前1174、FreshSource和预览不改。


- 可交付预览：[启动修复包](../../artifacts/delivery-20261115/RizToolbox-0.3.0-startup-preview-20261115.zip)，36,997,650B，SHA76a3ee353a73c0436547b6fbc13e90a4e27ad3170abc1cd7802ad5842fb8801e。只接受启动/有限流程，不覆盖旧ZIP。
- 当前精确1172 r2六Release/solution0E、focused8+Runtime383/Module31/Camera55已新自然0；原wait34372/43772记录0。当前独立SDK14也已新自然0（34952/15816），1266文件归档summary854f8c21176bc05d5b8f554b8e248af3e802d4af4a1e3942574f221e9818ad25，root archive-only0/f4c15f。两者不重跑。
- Steam继承内部禁用、原生证明分配布局已修入ROOT native并各自fresh构建/自身夹具/归档；Runtime invoke先筛固定内部consumer、相机interval最小正normal仍在隔离1172中，ROOT托管1160未应用。
- 此前runtime-r2实际游戏16960：nativeReady/原init1/CoreCLR/Unitytick/host新status，30模块=28active/1waiting/1unsupported；FREE EASY开局、失焦暂停恢复、返回选曲/主菜单已观察，正常退出。无RunEnded、无正常结算；ObservationLost不是精确Back取消。FREE相机目标缺失、appearance getter unsupported、settings页未测单列限制。诊断helper有游戏证据，ROOT生产helper没有借用该结论。
- 原生ZIP SHA408aa09910ce3346a149a049920820f0f4a7013737fc06a507f594367a17f4dd，root ZIP-only335 checks实际0/437582；[报告](../../docs/2026-11-15_playback-attestation-allocation-report.md)含独立工具引用笔误更正，原ZIP/JSON/log不改。完整SDK/FullSeek等false不提升。

**新增可用SDK交付**：[SDK开发包](../../artifacts/delivery-20261115/RizToolbox-SDK-1.2.0-preview-20261115.zip)，18,484,558B，SHA3dc92a00099da0c55276defe963826597d4c43acaa0ff89a97f066d44f212817；同1172候选/SDK14，5模板、29合同源码、4固定本地依赖。实际解压用新hive/cache走完4命令，Release W0/E0，原session11344 actual0/f13ea9。交付ZIP+证据独立checker0/e1b93f；[交付说明](../../artifacts/delivery-20261115/交付说明.md)。只是开发包/预览，不签完整SDK。此交付已闭合不重复smoke。

**最新准备完成**：新1172/r5整合控制13项已AST/只读来源核对并冻结，preparation SHA55a92d9710c1615f7f2c4579571d547b5311b3849e8fc83e2e2b2b12767b0bcc；原session50407实际0/4abdb7。计划96自然步骤含27普通2534/62Hold/producer3/tick8，未apply或验证main。gate实际只读拒绝（003026工具exit1，父r5原wait缺失），不是功能PASS。父16:43最新22自然0、仍有活子进程；详见CURRENT顶部。原下文“准备中/14自然0/renderer44116”只属较早观察，以此为准。

**本次接手结果已核验**：游戏25008的FREE EASY RESULT已实际看到，SDK的RunStarted/RunEnded/RunFinished同RunId，Miss479/Bad0/Score0/RawCompletion0与界面一致。原生结果Crashed=true、EndReason=Failed，只接受失败结算，Completed正常通关仍未验证。22文件新归档summary2c034063df1fc3cc29ea2892efc3237cc9931fe4aa88d31433a9125d07888635，发布0/488db4、root独立checker0/2ffbc9；[报告](../../docs/2026-11-15_game-result-flow-report.md)。设置页两模块active仅就绪/只读，不代表写入和恢复。游戏14:38曾仍活，15:14查询480d31确认进程0个，退出方式未知；用户接手已结束，后续游戏需重新观察/正常启动。全SDK/B01矩阵/真实SpeedSeek仍未完成。

下一步按顺序：

1. 续正在运行的父main-r5（supervisor35864/controller13424）；16:43查询1a0dea确认renderer-6/8128与renderer-10/21676仍运行，22自然0、26已记录，无原wait终态。不能重复启动/中断，不改ROOT1160/Build；r4中断未知/r3失败不拼接通过。
2. 仅父r5原wait0+完整72自然0后，执行其独立record→publish→archive checker→finalize。门禁registry见work/line-producer-control-next-20261114/main-r5-acceptance-controls.json，SHAa17b247777910e3dcc9c42ddaa19bd59b81f6b23140b24dfe381edd0f8337adc；保留101/6/101+inverse。
3. 使用已准备的新work/foundation-startup-main-integration-next-20261115完整控制链（顶部SHA），父r5接受后再apply；96自然步骤中的各具体检查与完整源集合不缩减。旧prepare_main_payload_r2和Hold939链仍不可运行。
4. 并行续顶部FreshSource opaque native票/七根factory、真实run之后的正式provider/publisher；其后按功能边界补正常结算/精确Back、设置页、NormalMode相机、真实判定与Speed/Seek、LOCAL engine/source的正向路径；已有N1/相机X/rate/float/Play handoff/退休批不重做。性能后置，不做旧兼容。

用户已授权必要游戏操作，认证仍手动；不使用过期B账号、不新增游戏二进制分析、不永久改env/Git/旧ZIP。更多精确SHA/命令/历史结果见CURRENT-CHECKPOINT。替换前原页在[历史快照](../history/20261115-before-startup-runtime-refresh/NEXT-FUNCTIONAL-GAPS.md)。

下方保留早期判定口径、19项范围和N1–N4定义。**E2/E3等早期源码依据与表内“尚缺”不是当前状态**；已闭合项目以上方与实际新证据为准，不按历史表重复施工。

## 判定口径与依据

| 标记 | 含义 |

|---|---|

| 功能缺口 | 当前正式调用链或公共请求合同尚不能表达/执行 SPEC 所需行为；增加相同拒绝路径测试不能完成它 |

| 证据缺口 | 已有实现或历史正向证据，但当前候选、真实目标或规定组合尚无对应验收；先做定向验证，不能据此重写已有功能 |

| 后置 | 用户明确安排稍后进行的性能优化、基准及相关长期压力；不作为本轮新功能施工任务 |

| 依据 | 本次采用的事实及边界 |

|---|---|

| E1：当前规范 | [SPEC](SPEC.md) 顶部已明确未发布阶段统一当前 SDK/API/协议，不要求历史二进制兼容；每个 B 项仍有独立验收 |

| E2：公共合同 | [ResultContracts](../../src/RizToolbox.Sdk/ResultContracts.cs) 已有结构化 `Result/OperationError`；[Contracts](../../src/RizToolbox.Sdk/Contracts.cs#L85) 仍定义只有字符串错误的 `OperationResult`，[Visual](../../src/RizToolbox.Sdk/VisualContracts.cs)、[Mutation](../../src/RizToolbox.Sdk/MutationContracts.cs)、[Target](../../src/RizToolbox.Sdk/TargetContracts.cs)、[Service](../../src/RizToolbox.Sdk/ServiceContracts.cs) 等正式服务继续使用它 |

| E3：玩法实现 | [GameplayContracts](../../src/RizToolbox.Sdk/GameplayContracts.cs) 的执行请求只有 action，没有速度/位置值；[GameplayNativeBackend:80](../../src/RizToolbox.Adapter/Gameplay/GameplayNativeBackend.cs#L80) 直接拒绝 Speed/Seek；[GameplayService](../../src/RizToolbox.Adapter/Gameplay/GameplayService.cs) 没有发布已验证秒映射，[判定发布](../../src/RizToolbox.Adapter/Judgements/JudgementCoordinator.cs#L106) 仍保留 `RawGameUnits` |

| E4：视觉与绑定实现 | [NativeVisualBackend:19](../../src/RizToolbox.Adapter/Visual/NativeVisualBackend.cs#L19) 绑定 Image/Text/Graphic，另有 TMP 共享材质槽；[视觉说明](../../docs/Visual-resources.md#目标发现及租约) 明确对象重建后需重新 Resolve、尚无持久意图自动重绑；[TargetContracts](../../src/RizToolbox.Sdk/TargetContracts.cs) 的值租约明确不跟随新代次 |

| E5：生产与 LOCAL 接线 | [NativeEngineTransportFirstInstall:14](../../src/RizToolbox.Runtime/Hooks/NativeEngineTransportFirstInstall.cs#L14) 的生产准入仍恒 false；[生产准入审计](../../docs/validation/foundation-engine-transport-production-admission-20261027/README.md) 明确生产签发分支、首次执行窗口和持续排他尚未成立。另一方面，[正式功能接线](../../docs/validation/foundation-function-wiring-20261027/README.md) 和 [NativeLocalAudioContentRuntime](../../src/RizToolbox.Adapter/AudioContent/NativeLocalAudioContentRuntime.cs#L81) 已把 RequestPlay、来源捕获、coordinator、结果/返回/重试连起来，不能再按旧文档称其完全未接线 |

| E6：已有证据 | [历史真实双包](../../docs/validation/foundation-runtime-20261019/README.md)、[真实单局/判定](../../docs/validation/runtime-judgement-identity-20261020/README.md)、[Single/dynamic 冻结批](../../docs/validation/foundation-tween-dynamic-single-20261028/README.md)、[当前退休批](../../docs/validation/foundation-tween-retirement-20261029/README.md) 各保留原候选、会话和受控/真实边界；旧证据不自动覆盖本次源码 |

## 19 项范围核对

下表的“已有”是实现或限定证据，不是整项完成。未发现新的功能缺口不等于该项已验收。

| 项目 | 已有路径与限定证据 | 仍缺的具体功能或证据 | 下一处理 |

|---|---|---|---|

| B01 单局闭环 | 实际播放标记开局、RunId、失败/Retry/Back/Skip 的分包真实证据 | **证据**：正常完成；初始化在结算页、进场未开局、快速同谱重进、迟到结果和停留结算完整矩阵。不能补造历史漏报 | 与 B09/B10/B11 合并一次当前候选真实流程矩阵，待恢复实测 |

| B02 原生运行时 | 类型/布局拒绝、根与线程、专用 ABI 及多批自有 native/CLR 证据；历史未激活对象 GC/销毁 | **功能/证据**：E5 的真实生产首次准入未成立；活体原生完整异常、激活后消费者和最终对象释放证据不足。未知 ABI 组合继续明确拒绝 | 首次准入列为独立依赖；不把 Tween root 数等同整个运行时释放 |

| B03 SDK 合同 | 当前共享程序集/范围检查、结构化 Result 类型、历史独立双包运行 | **功能**：E2 的字符串结果与结构化结果并存，旧服务错误不能按统一 Code/Capability/Target/Retryable 消费。**证据**：合同统一后须用同候选 SDK 重编独立包经正式宿主运行 | N1；历史 B03 勾选仅保留原包范围 |

| B04 包生命周期 | 当前两个独立包离线核对了必需/可选依赖、版本不匹配、环、冲突、故障传播、晚就绪与保存的停用意愿 | **证据**：将当前 Core 的独立包验证合入新候选，核对同候选真实安装归属、替换与中断恢复 | 与 B17/B19 共用验证 |

| B05 作用域/调度 | 子作用域、队列代次、取消/超时、先失效及逆序清理。播放命令覆盖在途停用、队列取消和清理异常隔离 | **证据**：同候选中回调内停用自身和依赖者、迟到结果，以及已绑定的 Unity 阶段 | 补当前组合的功能性反例，性能公平性测量后置 |

| B06 能力适配 | 模块清单、精确符号、代次、依赖、等待/退避及局部失效已有实现 | **证据**：真实可选目标缺失、晚就绪、撤回/恢复和旧服务视图失效的完整矩阵；新子能力须有独立描述 | N2/N3 新能力沿现有模块接入；不要以 RuntimeProbed 代替正向执行 |

| B07 Hook broker | 共享真实挂接、typed observer、拒绝未授权 mutation、进程保留跳板与在途责任 | **功能/证据**：生产首次准入仍依赖 E5；新目标在真实游戏上的 ABI/调用/重入/异常/原调用计数仍待验。Single mid 由并行任务处理 | 作为依赖列出；不要开放通用任意参数改写来凑覆盖 |

| B08 修改协调 | 语义键、优先级/变换/独占、基线跟随和回滚已有实现 | **功能**：统一错误合同见 N1。**证据**：当前公共协调器的双/三包交错、外部写入、重建和恢复矩阵 | N1/N2/N3 复用同一仲裁器，不再建立独立覆盖栈 |

| B09 生命周期事件 | 场景/会话/RunId/暂停/终止模型、初始基线与结果来源隔离。新控制器及原结算、判定的归属已通过定向离线回归 | **证据**：B01 全流程，加中途启用、订阅初始快照衔接、同谱重开和迟到结果 | 与 B01 共享真实会话，分别核事件与快照修订 |

| B10 一致数据 | 修订分页、取消、陈旧标记。当前 DataModule 组合真实结果与设置来源，结算变化与采集失效已有定向回归 | **证据**：当前候选的真实谱面/成绩、结算及设置字段和单位，实机分页同修订 | 与 B01/B09/B16 联合验收 |

| B11 玩法原语 | 独立时间读数、精确判定源/音符身份、实验 Pause/Resume/Retry/Skip、修改审计。受控来源覆盖带值倍率、Seek 组合和媒体时钟读回 | **证据**：正式 Fresh、固定 `GetTime` 调用、实际 DSP 调度及带值控制，另有十二判定源、手动 Hold、新尾部身份、单位及计数/结果比对 | N2；性能成本测量后置 |

| B12 语义目标 | 唯一/缺失/歧义、代次、模板/活跃区分、别名仲裁和有界发现。视觉资源与菜单模糊已有作用域绑定意图 | **证据**：实机动态场景、地址与实例 ID 复用、数组重排、容量边界及可见恢复 | N4；有界容量与严格匹配，扫描性能优化后置 |

| B13 视觉资源 | PNG→Texture/Sprite、内置 Font、白名单 Material、TMP 颜色与材质槽、赋值 pin 和延后清理。音符、轨道、相机与菜单后处理已有独立语义入口 | **证据**：各已开放类型的真实双包覆盖、失败保留、基线恢复、重建与最终释放 | N3/N4。同一候选验收可见效果，额外格式各自声明范围 |

| B14 UI/输入/导航 | [两个不同挂载点](../../docs/Ui-input-navigation.md#选择挂载点)、原生控件/模态/焦点、坐标投影、输入动作/重绑、数字手柄与导航已有实现 | **证据**：两个真实挂载点+一次实际导航、双包焦点/文本/模态/快捷键、分辨率/DPI、停用/切场景、手柄热拔矩阵。world/game 坐标、注入等未开放范围继续明确说明 | 不重造挂载点或输入系统；定向真实验证，硬件/实测继续稍后 |

| B15 音频/内容 | 自有 WAV、替换/混音、命名空间内容和正式 LOCAL play/source/result/return/retry 接线；本轮新增 Tween 退出最终票 | **功能/证据**：E5 生产准入缺失；typed/listener/engine effects 与 future source 关闭仍依赖并行工作；完整 LOCAL 正向、非空 globals 条件恢复和完整资源关闭尚未真实成立。可闻输出/游戏音频恢复/Workshop 非空正向仍缺证据 | 不再把“未接 RequestPlay”作为任务；按下节独立依赖与同候选真实流程推进 |

| B16 配置/存储/语言/设置 | schema、原件保留、隔离、配额、语言热切和设置订阅已有实现。包内 DLL 已通过配置及设置定向回归，当前两独立包已离线验证同名配置交错迁移 | **证据**：同候选真实设置中的双包交错、外写与基线恢复 | 与 B04/B10/B17 共用探针 |

| B17 跨插件合同 | 独立两包确认错误字段、硬软依赖、消息隔离、递归、故障归属与撤回恢复。队列饱和故障已用新 ZIP 内的 DLL 复验，同候选独立包 9/9 | **证据**：正式游戏宿主中的停用 | [包内回归](../../work/b17-service-fault-dispatch-20261122/packaged-service-validation.json) · [独立包](D:/riz-m2/b17-independent-20261122-r1/result.json) |

| B18 诊断/性能 | 结构化诊断、轮转/限流、所选会话导出和采样工具已有分项证据 | **证据**：新功能错误能按 owner/capability/target/run/scene 解释。**后置**：四运行基线、p50/p95/p99 校准、分配/句柄曲线、20 轮性能压力及阈值 CI | 本轮只随功能补诊断；不以性能基准为继续补功能的前提 |

| B19 分发/一致性 | 候选归档、同候选构建/合同检查、合成安装恢复和历史同机异目录重建已有独立证据 | **证据**：最新冻结树/SDK/模板/原生同一候选、当前两包、实际安装替换及真实流程、干净环境构建。生产首次准入未解时不能宣称可发布 | 当前统一 snapshot/full tests/package 由 root 收尾；不与历史包拼接，不为旧 SDK 增兼容层 |

## 四个下一实施包

### N1：把公共失败结果统一为当前结构化合同

- **范围**：B03，并覆盖 B04/B07/B08/B12/B13/B17 的公开调用结果。优先 [SDK Contracts](../../src/RizToolbox.Sdk/Contracts.cs)、Visual/Mutation/Target/Hook/Extension/Service 契约和对应 Core/Adapter 返回点。

- **直接改动**：当前公开操作统一 `Result/Result<T>/OperationError`；错误 Code 在实际失败处签出，填当前能力、目标和可重试性。去掉作者通过 `"TargetMissing:"` 等字符串前缀判断的必要性。调用与模板同步重编，不加旧 `OperationResult` 转换层。

- **可交付行为**：同一视觉目标不存在、资源格式错误、scope 结束、服务提供者撤回、依赖不满足/冲突，独立插件都能按 `Error.Code` 作不同处理；相同文本语言不改变错误语义。

- **验证**：当前 SDK 引用包编译两个独立小探针，由正式 package loader/PluginHost 调用；验证正向、错误范围/共享身份/接口/包合同拒绝和一个包故障不影响另一个包。现有功能回归按受影响项目运行，无需先启动游戏。

- **边界**：这包统一当前合同，不借机增加新服务或维护历史二进制兼容。实现集中于 SDK/Core/服务边界，可先于其余包定稿，以减少重复改签名。

### N2：交付有单位的玩法时间及有值的控制事务

- **范围**：B11，连同 B08 独占和 B01/B09 局身份、修改上下文；[Gameplay](../../src/RizToolbox.Adapter/Gameplay)、[Judgements](../../src/RizToolbox.Adapter/Judgements) 和 SDK 请求合同。

- **直接改动**：为速度/定位提供携带有限数值、单位、目标 run/代次的明确请求；分别实现与探测 SetSpeed、Seek，不把 enum 名称当正向能力。补固定构建的游戏时间↔单调时间↔DSP 映射描述及生效区间，区分暂停、速率、偏移和延迟；判定 measured delta 与哨兵分开。

- **控制路径**：一个操作先完成捕获/前置/独占/所有会改变的音频与游戏状态准备，再提交和确认；失败后要么保留原局，要么明确记录未知副作用并撤回该控制，不能留半重置局或宣称 Dispose 撤销不可逆命令。

- **验证**：受控 backend 核数值与单位、冲突、暂停/恢复、失效代次、部分失败和审计；单独的固定构建字段/方法消费证据支撑换算。真实暂停/速率/定位/结果流程未观察前，秒映射和具体控制仍保留未验证/实验状态。

- **边界**：不做完整练习 Mod，也不把游戏状态波动当性能研究。当前缺少的是实际值请求及交易路径，不是再加一条 Speed/Seek 拒绝测试。

### N3：为游戏外观提供专用语义目标

- **范围**：B12/B13，复用现有 B08 和资源租约；在独立 Adapter 模块增加经过字段/方法验证的游戏目标描述，避免改动冻结的 Tween 退出文件。

- **直接改动**：按“音符外观 → 轨道外观 → 相机参数 → 后处理参数”拆成内部小步，每步从一个具体角色、属性和固定来源接入。目标身份使用内容/组件角色/属性与对象代次，区分模板和当前活跃实例；相机/后处理标量不能伪装成 `VisualAsset` 资源覆盖。

- **可交付行为**：普通 SDK 插件可以查询和覆盖该具体游戏角色的一个已声明属性，两个别名共用同一仲裁通道；缺对象等待、多候选拒绝、对象重建结束旧租约、外部写入按该目标基线策略处理。

- **验证**：固定元数据/来源合同和受控实际服务的双包覆盖、失败保留、外部写入及代次重建；随后只在该具体真实游戏目标验收可见变化和恢复。每个角色独立登记支持范围，普通 Image/TMP 或工具面板成功不能替代。

- **边界**：未知 shader 属性、任意内存写入及新资源格式不顺带开放。现有 Sprite/Font/Material 各类型完整实机矩阵继续单列证据缺口。

### N4：让宿主维持语义覆盖意图并安全续接新对象

- **范围**：B12/B13/B05。当前一次性 `TargetHandle` 和租约失效规则保持；增加由插件 scope 拥有、以稳定 `TargetQuery` 为输入的持续绑定意图。

- **直接改动**：意图在旧对象失效时结束旧目标租约，保留仍由 scope 拥有的资源；宿主在现有发现边界重新解析。唯一匹配才申请新代次租约；零匹配发布等待，多匹配发布歧义，资源/能力/父 scope 撤回使意图永久停止。

- **可交付行为**：换场景或对象重建后，同一个作者意图能对新对象取得新租约；旧 handle 永不复活，也不会因名字相同误绑定模板、不同内容或其他场景对象。新目标继续走公共仲裁器。

- **验证**：当前 Visual 服务和 SemanticTargetRegistry 上验证缺失→唯一→歧义→重建、地址复用、scope 在重解析中撤回、资产退役 pending、两插件竞争和外部基线；资源预算与 once-only 释放沿用实际 holder 规则。

- **边界**：这是功能性续接，不以更大扫描窗口或每帧扫描替代。N3 可只产出候选/字段访问器；N4 独立实现意图状态，二者最后接现有模块，避免同时改同一发现循环。

## 独立依赖与后续验收

1. **Single mid、非空 listener effects、engine setter/effect transfer**：已有另一条任务在定位/实现。其产出必须是实际 executing-list 或具体消费者完成/转移合同；本清单只等待依赖，不复制该施工，也不把 native return、owner death 或空列表视为非空效果结束。

2. **生产首次准入**：此前实机的原生启动快照已有固定图、执行窗口与主线程证据，当前候选的内置配置验证通过。待验证范围从首次 Unity Tick、宿主初始化和 V5 托管采用开始，随后确认 LOCAL 与玩法控制各自的实际消费者。LOCAL 仍依赖实时准入结果。[E5 验证索引](../../docs/validation/e5-production-candidate-20261121/README.md)

3. **LOCAL 完整运行**：正式 RequestPlay 链已有，下一步是消费生产 authority 和各独立完成合同，验证参数/一次激活/六 globals、实际结果、非空基线条件恢复、Return/新图 Retry 与完整资源关闭。新 Tween final receipt 只满足其中一个 owner；不扩大其授权范围。

4. **同候选真实验收**：恢复实测后合并安排三组：生命周期/数据/时钟/判定；双包视觉/设置/输入/焦点；包依赖/服务/合同/安装恢复。原始失败与修复后会话分开保存；每个 B 项只消费本项对应证据。当前不得执行这些游戏步骤。

实施建议：先定 N1 当前合同，然后并行 N2、N3、N4 的独立模块；真实生产首次准入及已分派 effects 是 LOCAL 的外部依赖。除发现新故障，不再用重复 Tween 退休回归占用这一轮功能施工。

