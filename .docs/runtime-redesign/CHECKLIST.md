---
plan: runtime-redesign
version: 1.0
created: 2026-11-17
status: active
---

# Checklist: runtime-redesign

当前已完成 R01/R02 的首批源码和受控回归。R03 的组合输入、共享音频和 Gameplay 服务也已接入按需。真实游戏的完整启动链和运行成本仍待验收。任务只有在所链接 SPEC Tests 全部通过后才标为完成。

## Phase 1: 运行模式与入口

R01→R02 表示实现顺序，**两项作为同一切片联合签收**。R02只需要R01产出的模式/配置接线，不等待R01全部运行验收结束；普通Tick不做完整审计的要求由R02实现。在两项相关SPEC Tests全部通过前，不单独发布声称新Standard合同已生效的包。

- [ ] **R01 普通运行与开发诊断分离** → [SPEC.md#runtime-modes](SPEC.md#runtime-modes)
  - 当前：启动模式、日志、验证插件门禁及三种发行包已接入。完整游戏安装审计待验。
  - 位置：`Bootstrap.cs`、`ToolboxHost.cs`、`Build.ps1`、`Install.ps1`、examples/proof 配置。
  - 改动：Standard/Developer 显式选择；普通日志去掉硬编码 Debug；不自动开启 proof；迁移保留用户文件与启用意愿。
  - Complete when：与R02联合使runtime-modes 的全部 Tests 通过；非法配置和安装失败不得默默启用写入。

- [ ] **R02 拆分安装证明与 Tick 通道检查，并覆盖入口计时** → [SPEC.md#tick-admission](SPEC.md#tick-admission)
  - 当前：受控的真实原生通道验证、计时和命令前后边界处理已通过。实机接线和成本待验。
  - 依赖：R01的模式/配置产出；两项联合验收，不形成完成条件循环。
  - 位置：`NativeFirstInstall.cs`、`NativeRuntimeInvokeBridge.cs`、`RuntimeBridge.cs`、`ToolboxHost.cs` 及对应真实native/managed入口测试。
  - 改动：新增窄的运行通道检查，保留原函数/ABI、登记身份、epoch、线程、in-flight/Unknown和退出责任；Standard accepted tick 不再调用完整发布图验证。Developer 保留显式完整审计。
  - Complete when：tick-admission 的全部 Tests 通过；报告完整安装/普通通道不同保障，不能用成功缓存假装原证明未改变。

## Phase 2: 需求与共享观察

- [ ] **R03 先发现清单和启用意愿，再制定模块需求/场景计划** → [SPEC.md#demand-and-scene](SPEC.md#demand-and-scene)
  - 当前：清单发现、Waiting 的硬依赖和 15 个按需模块已接入。组合 UI/Input 按需启停，Canvas 和按钮观察延至实际创建。共享音频依需求维护，Gameplay 的首次执行覆盖与 SDK 服务相分离。LOCAL 保留现有运行的结果 UI。实机空 mods 和首次执行覆盖待验。
  - 依赖：R01；与 R02 可分文件推进，但合并后统一验证。
  - 位置：`PluginPackageLoader.cs`、`ToolboxHost.cs`、`ModuleManager.cs`、`RizlineAdapter.cs`，必要时一次调整同候选 SDK 清单。
  - 改动：纯 metadata discovery；Waiting 意愿参与需求；required 与 optional 分开；最小 scene observer 持续工作；无需求不创建可选 Canvas/音源/功能 gates。
  - Complete when：demand-and-scene 全部 Tests 通过；有明确早装清单及 pending retirement 路径。

- [ ] **R04 固定 Tick 阶段与不可变共享观察** → [SPEC.md#shared-observation](SPEC.md#shared-observation)
  - 11-23，r16 玩家包在六种情况下能重新读出已失效的控制器状态。r17 冻结源码修复了时钟采样、发布、两种时间转换、命令准备及同一来源暂时消失后的失效处理。定向 16/16、Gameplay 259/259。受控环境还不能在正式播放宿主已就绪时核对查询的全部成本。[六例对照](D:/riz-m2/postr16-observation-graph-20261123/validation.json) · [就绪场景覆盖范围](D:/riz-m2/r04-ready-stock-offline-20261123/coverage.json)
  - 当前：Gameplay 查询与订阅共用按宿主 Tick 序号发布的快照。控制器根来自生命周期采集，场景、局和音源变化会撤回旧观察。Gameplay、入口序号、命令后边界和快照递归读取已通过离线回归。其他资源发现与维护继续按 R05 处理。
  - 依赖：R02、R03。
  - 位置：`ToolboxHost.cs`、`RizlineAdapter.cs`、`GameplayService.ControlObservation.cs`、`GameplayNativeBackend.cs`、各资源 backend。
  - 改动：前/后 boundary drain 按 sequence 消费；使用 host tick 序列而非假定Unity frame；共享 controller inventory/观察；订阅分发避免 N² 空投递。
  - Complete when：shared-observation 全部 Tests 通过，递归读取/多插件不会重复采集，旧观察无法写入。

- [x] **R05 共享后端唯一维护和真实退休阶段** → [SPEC.md#retirement-and-faults](SPEC.md#retirement-and-faults)
  - 当前：音频集中维护及确认退出后的释放、相机有限频发现、批量撤回与独立阶段故障隔离均通过离线回归。运行成本按 R07 实测。见[十二套结果](../../work/runtime-redesign-retirement-20261118/validation-r2.json)。
  - 依赖：R03、R04。
  - 位置：`AudioContentModules.cs`、`AudioService.cs`、camera/appearance/visual、`ModuleManager.cs`、process-owner/retirement pumps。
  - 改动：Probe/Maintenance/Discovery 分开；audio 三能力共用一次 Refresh；camera 空目标尊重重试间隔；最后需求消失先撤新访问，实际 pending 继续维护。
  - Complete when：retirement-and-faults 全部 Tests 通过；补无需求零发现/三audio能力一次刷新/空camera有界发现/单managed故障后独立阶段继续的定向执行证据。

## Phase 3: 单一播放事务

- [ ] **R06 先迁移暂停倍率操作，再迁移独立 Clock 读取** → [SPEC.md#playback-operation](SPEC.md#playback-operation)
  - 11-23，自制谱先用 Play、官方谱随后安装的方向已合入主树。隔离宿主中，Play 原调用仍在执行时，两次检查均保持等待；原调用返回后官方模块补齐 23 个入口，继续复用同一处 Play。主树重建后，混合方向、撤回和写入故障的 18 个进程、300 项 Play 断言、四项本地谱生命周期检查均通过。新原生组件通过两项核心验证、1,747 项展开检查和六个初始化用例。Steam 内连续切谱仍待验证。[主树组合验收](D:/riz-m2/b11-local-first-main-integration-20261123/main-verification.json)
  - 11-23 主树受控验证贯通了官方先安装、LOCAL 借用 Play 并撤回、官方继续调用的顺序。主树构建通过 14 个子进程和 19 项自有映像 Update 断言。反向顺序和游戏内两种谱面连续游玩仍开放。[同进程验证](D:/riz-m2/b11-official-first-main-integration-20261123/final-verification.json)
  - 11-23 r13 的时钟读取、分帧安装和独立插件已完成封包验收。后续主树增加逐动作就绪查询，正常撤回及在飞原调用退出后能读到真实活动。实机还需在《Pastel Lines》同局核对率值、位置、恢复和后续采样。[r13 交付](../../work/b15-live-local-crossscene-20261123-r13/delivery/r13-delivery-validation.json) · [后续主树构建与验证](../../work/b11-control-readiness-20261123/main-merge/validation.json)
  - 11-23 r12 已在官方谱中读到同局、同控制器的时钟样本。首次适配器准备 112.25 秒，第一次公开时钟帧回调 39.32 秒。宿主 tick 回调与谱面时间为两个不同指标；停用测试插件后前者恢复推进，曲内没有读到可用的变速、跳转回执。新的测试插件会让显式命令先执行，并在慢读取后留出恢复时间，独立宿主 15/15、共用命令收发 28/28 通过。仍需用修正后的生产时钟在同谱复验。[实机与验证入口](../CURRENT-CHECKPOINT.md#r12-插件开发包与播放控制11-23)
  - 11-23 r11 官方谱暂停和恢复在同一局实测通过。r12 已修正本地谱运行时过早占用官方进度函数的问题，离线验证了独立插件在控制状态不变时能读回连续时钟样本。真实变速、跳转与同进程切换自制谱仍待验。[实测与候选进展](../CURRENT-CHECKPOINT.md#r12-插件开发包与播放控制11-23)
  - 11-22 r9 ZIP 复验正常入口 21/21、时钟 14/14、分帧需求门控 7/7。实机本地谱开局的宿主故障和完整 Speed/Seek 仍待验收。[r9 包内回执](../../work/n2-packaged-r9-recheck-r1/acceptance.json)
  - 11-22：首次按需接入可以跨帧准备。撤回需求保留同一对象和文件，恢复后在原主线程完成一次物理提交。受控场景 535 项、Gameplay 247 项、正式宿主 7 项、时钟就绪检查 14 项已通过。真实游戏下的变速与跳转仍开放。[主树验证](../../work/n2-staged-main-20261122/main-20261122T133148907Z/validation.json)
  - 11-21 Gameplay 停用先取消排队命令，读回重入结束的租约直接转入恢复，后端清理失败独立记日志。当前 313 项托管检查和 193 项自有原生夹具断言通过。 [验证记录](../../docs/validation/b05-gameplay-command-shutdown-20261121/README.md)
  - 11-21 Replay/Resume 会用实际 `PlayScheduled` 实参核对起播基准、媒体位置和 DSP 采样窗口。四个改前反例在修复后通过，定向检查共 631 项。夹具通过真实挂接调用，正式游戏来源和真实 DSP 时序仍待验。 [验证记录](../../docs/validation/r06-stock-schedule-readback-20261121/README.md)
  - 11-21 倍率回滚对已确认写入增加当前值读回。实际值被外部改变时拒绝逆向写入，首次读回失败仍沿私有续票恢复。当前源码 986 项离线检查通过。 [验证记录](../../docs/validation/r06-pitch-recovery-ownership-20261121/README.md)
  - 11-21 自有 22-role 播放来源夹具执行同票 Fresh → 暂停 → 原生 Clock → 登记 `GetTime` 调用点，共 501 项。worker 修改受监视播放器后原函数正常退出，托管来源回调为零，下一次 Clock 消费即时拒绝。正式 stock 与游戏固定调用仍待验收。 [验证记录](../../docs/validation/r06-fresh-gettime-consumer-20261121/README.md)
  - 11-21 暂停 Clock 激活前后复核当前暂停位置、倍率、音频长度与状态。激活前变化时保留待激活策略，激活后读回不符时撤销原生策略。当前源码的两个新增回归、相邻读回与重新开局、跨局路径共 700 项检查通过。 [验证记录](../../docs/validation/r06-paused-clock-activation-20261121/README.md)
  - 11-21 V5 候选已随包交付 Clock 和播放来源的两个原生助手。当前编译配置通过此前实机原生启动快照复核。正式 Fresh、固定 `GetTime` 消费者及真实 Speed/Seek 仍需同候选验收。[验证索引](../../docs/validation/e5-production-candidate-20261121/README.md)
  - 11-21 N2：首次及后续 Seek 在原生写入前核对独立读回的位置，避免覆盖外部暂停位置。探针可传递谱面坐标。当前 Release 构建及 740 项定向验证通过，正式 Fresh、固定 `GetTime` 消费者和实机 Speed/Seek 保持待验收。[验证索引](../../docs/validation/n2-seek-media-ownership-20261121/README.md)
  - 11-20 最新 N2 五参与者组合封存（[seal-r1](../../work/n2-seek-game-state-composition-20261119/seal-r1/validation.json)）：在同一 1802 输入快照上完成 `current-r1`（cases 5–7，solution `0E/55W`）、`current-r2`（cases 1–4＋147 相邻，151 项）与 `canvas-adjacent-r1`（Canvas 7,8,9,10,17,18，6 项），合计 20 child / 160/160 托管检查 / 7 唯一组合场景，28 源码差异与 6 DLL 身份一致（[报告](../../docs/2026-11-20_seek-game-state-composition-report.md)、[索引](../../docs/validation/n2-seek-game-state-composition-20261119/README.md)）。仅签离线受控五参与者组合；正式 22-role `Fresh/watch/SuppressObserver`、固定 `GetTime` consumer 正向、完整 Seek/Speed 及实机倍率/恢复仍保持开放，R06 保持未完成。[当前进度](../CURRENT-CHECKPOINT.md)
  - 最新N2 Hold恢复：实际epoch Prepare/Stop/Factory/Start/原iterator inverse已执行，第二Factory前失败可恢复已确认partial target，pending effect/异常Start/临时cleanup Unknown保持；返回token先publication、target一次性。epoch10＋target28＋mode3＋lifecycle62＋Runtime54＝157、15child/outer自然0，solution增量0E/0 emitted warnings（首完整编译62既有测试警告未消除），1800输入/6DLL/README保持、11diff已[封存](../../work/n2-seek-hold-epoch-recovery-20261119/validation-r1.json)。queues原同epoch count接线实测1→0→1→0→1与18次列表写/完整storage恢复，不重写。Module issuer受控装配、profile/native original为shim，不签fixed body/Dobby/Fresh/完整Seek；下一原五参与者gameState/rendering/audio/四cache/Clock整合，R06保持未完成。
  - 最新N2 Hold：capture/Verify共用正确AutoPlay/FreeHold backing fields；真实private VerifyModes原缺字段复现，模式/metadata拒绝保持。当前3新案例＋target28＝31、5child/outer自然0，solution0E/62既有测试警告、1796输入/4DLL/README保持，[封存](../../work/n2-seek-hold-mode-fields-20261119/validation-r1.json)。未执行完整epoch Prepare/Start/Stop或formal observer，旧956不验本批。队列同epoch count接线已存在、不重写；下一实际target部分Start/停止及predecessor inverse，再补gameState全组合，R06仍未完成。
  - 最新N2进度：独立source/Clock写权限接最后field guard，metadata撤权后不再store；既有Plan/Audio/真实四cache/Clock及反序恢复六模式145，加source135/TimeWrite161/Gameplay238/rate277＝956、30child/outer自然0，1795输入/10DLL/native/README保持。当前Release solution0E/55既有测试警告、独立provider0E/0W，六diff/四改前/六改后已[封存](../../work/n2-seek-progress-participant-20261119/validation-r1.json)。转换为API shim、隔离映像只核字节；实际gameState/HoldEpoch未执行，不签fullSeek/Fresh/固定consumer。下一核实际Stop清零Hold count与gameState准备/恢复的同epoch衔接并补原五参与者组合，R06仍未完成。
  - 最新N2 source：private SetTime按实际reservation/nonce/frame路由，不误撤；Seek共用冻结来源与实际Clock policy、source-bound writer、同writer successor及pending continuation验证。getter后/native store前核来源、busy source拒绝释放保根、准备getter释放后返回前核来源/清理owner、external equal仍撤权。最终新Release solution增量0E/0W（首改后55既有测试警告未消除）、独立provider0E/0W，八private135+十TimeWrite161+Gameplay238+三rate/publication277=811、24child/outer自然0，1793输入/10DLL/native/README保持。[r2证据](../../work/n2-seek-source-operation-20261119/validation-r2.json)，r1阶段795仅历史。owned反射private source不签formal/Fresh，progress/chart注入不签真实完整参与者。下一实际四cache/转换profile与gameState/HoldEpoch/Clock原事务组合，R06/fullSeek保持未完成。
  - 最新N2：actual TimeWrite准备/执行独占前置、pending旧receipt隔离与非媒体baseline恢复核对已实现。十owned161+Gameplay238+三rate/publication277=676，16child/outer自然0、1793输入/10DLL/native/README保持；最终Release增量0E/0W，首改后55既有测试警告未消除。[当前证据](../../work/n2-seek-time-writer-20261119/validation-r3.json)。owned22source/23physical不具正式stock/foundation/Fresh票，未签正式Seek provider或fullSeek；下一收拢正式Seek source/publication运行边界和完整参与者通路，R06仍未完成。
  - 最新 owner-fault：真实watched worker SuppressObserver后的同步poll故障结果现直接撤当前Clock/SDK，host物理维护在进度节流前；通知暂存首次维护撤权、迟到exact对象保持、不复活、七根保留。当前r3 Gameplay238+九owned765=1003、12子进程/outer自然0，1791输入/13DLL/七native保持；两个Release增量0E/0W，首编译55既有警告未消除。[证据](../../work/r06-owner-fault-poll-20261119/validation-r3.json)。正常Unity backing实际Start/Pause及SDK变速恢复零根通过；测试仅改同一独占writer和独立观察代次口径。13-role owned/反射host仅签实际worker与维护，不升正式22-role Fresh；固定consumer/R06整体仍未完成，下一实际正式执行材料或具体N2/N3功能，不重复本切片。
  - 最新 consumer：正式 call/native/code profile 已检查同一已附加实际GetTime lease、self/thread，source和native reader实际sample前后及末端receipt/run/source/route/profile/Clock完整身份复核。r4当前639项、11子进程自然0、两个Release增量0E/0W，1790输入/DLL/native身份保持；[当前证据](../../work/r06-gettime-consumer-operation-20261119/validation-r4.json)。正常切源实际original一次、原始3返回、Known撤权后零根清理；publication在途Unknown仍留根。Mapped仅standalone profile，自有reader不签正式22-role Fresh或游戏consumer。下一Fresh/watch/SuppressObserver实际入口，不重写已接BeginSourceOperation，R06仍未完成。
  - 最新：native SetPitch 正常完成后 API 读失败的私票恢复已接入 source/provider/participant；pending 旧receipt不借权，恢复前完整非pitch基线核对、恢复后独立原倍率读回。永久读失败/外写/切源/进度/native异常继续Unknown，copy/worker/getter重入拒绝。当前Gameplay238、19 owned模式1086、solution0E/55警告和独立provider0E/0W通过，1788输入/DLL保持；[当前恢复证据](../../work/r06-pitch-readback-recovery-20261119/validation-r7.json)。该切片未运行正式Fresh/watch/固定game consumer或真实游戏，R06保持未完成。
  - 当前：同步与排队共用会话占位；Runtime 窄 channel/rate 操作和独立 Clock 读取已通过当前离线整合。真实 GetTime 占用先拒准入，写后发布 in-flight 失败保持 Unknown、撤权及原七根责任。17 operation modes、三个主路径和五个交接/清理模式全部通过。正式 Fresh/watch/SuppressObserver、固定 consumer 正向和当前包实机倍率恢复仍待验。见[当前整合](../../work/runtime-redesign-channel-operation-20261118/validation-r8.json)。
  - 依赖：R02、R04、R05。
  - 位置：`GameplayService`、`NativePlaybackRateProviderAdoption`、`StockRateHost/ClockRouter`、`StockRootedSource`、`GameplayPreparedValueControl` 与 Runtime 窄操作接口。
  - 改动：一次请求一个权威 operation；嵌套身份复核不触发完整 Probe；安装图证明与实际可写性分开；Queued/Executing/Unknown 明确；保留真实写前保护、写后读回与恢复归属。
  - Complete when：playback-operation 全部 Tests 通过；新包真实倍率/恢复有明确终态、记录每阶段成本与审计/graph/native调用数。Seek 不随倍率通过自动开放。

## Phase 4: 可交付预览

- [ ] **R07 诊断副本外置和成本验收** → [SPEC.md#diagnostics-and-budget](SPEC.md#diagnostics-and-budget)
  - 当前：有界单消费者日志、固定 cutoff/逐次 Flush 结果、有限 Dispose/实际 Completion、最多两份宿主状态、后台分位数/JSON、Standard 输入门禁及实际控制阶段观测已实现。当前十套688、原生25模式1608、导出26通过；诊断22、性能16、Loader23。同机60秒成本及全部实机检查待验，不能整体勾完。见[报告](../../docs/2026-11-18_playback-channel-diagnostics-report.md)。
  - 依赖：R02；最终验收包含 R03–R06 的当前候选。
  - 位置：`WriteStatus`、`CallbackPerformanceMonitor`、命令/mailbox、包更新监测、日志层。
  - 改动：主线程有界复制，后台合并/序列化；普通运行不定时全包SHA；分别记录入口/host/模块/command；预算不能中断执行中native调用。
  - Complete when：diagnostics-and-budget 全部 Tests 通过；同机基线及模式/插件集合/场景/时间段可复核，未达标明确保留。

- [ ] **R08 分离 Runtime/Author/Validation 包并完成三个真实场景** → [SPEC.md#deliverable-scenarios](SPEC.md#deliverable-scenarios)
  - r16 已独立归档 Validation，解压包内的采证工具和两个原生夹具实际运行通过。五个验证示例留在该包，与 Runtime/Basic 的 16 个核心文件逐字节对齐。[独立验证包](D:/riz-m2/r16-validation-kit-20261123/validation-delivery.json)
  - 11-23，r16 现可安装标准 V5 纯底层版或三个基础模组版。两种包新装后普通 SDK 宿主分别加载 0 个和 3 个模组，安装、两版互换和卸载已在隔离目录验证。三个基础模组在真实游戏中的面板、可见颜色与声音、停用恢复和实机成本仍待验。[交付和回执](../CURRENT-CHECKPOINT.md)
  - 11-23，r14 已完成候选包、玩家包和 SDK 1.3 作者包归档、玩家包隔离装卸和模板离线实编。r15 冻结树的六份部署 DLL 通过五套 1,870 项独立检查，正式候选 ZIP 的四项播放切换复验通过。三个真实游戏场景仍待当前包完成。[r14 交付](D:/riz-m2/v5-live-fixes-preparation-20261122-r14/publication/r14-delivery-validation.json) · [r15 独立套件](D:/riz-m2/r15-primary-qa-20261123/prepared-suites.json) · [r15 包内播放切换](D:/riz-m2/r15-play-deployment-probe-20261123/candidate-final/playback-deployment-verification.json)
  - 当前：三个普通 SDK 基础模组、显式 basicPlugins 和安装归属、采证及交付读回工具已合入。当前 solution 0 errors，82项短验、采证14、两个独立作者SDK14完成。分发subject完整50/50，外层exit1保留；默认legacy-default Doorstop r4自然0、106 MSVC警告。四份r2 ZIP及38/44/66/53成员已全量读回；包有限10场景和假游戏安装10项均自然0。首装212归属、二装10用户文件保留、卸载210未改文件/保留2修改配置，真实游戏进程读取0，原件身份相同。最终seal自然0，1787输入、12DLL、SDK消费者和四包身份保持，离线交付已闭合；见 [证据索引](../../docs/validation/r08-basic-preview-delivery-20261119/README.md)。旧r1中断、短验r2汇总失败、说明汇总exit1及包场景r2中断原件保留。三个实机场景、停用恢复、重启与成本仍待验，R08保持未完成。
  - 依赖：R01–R07；普通基础包可以不含实验倍率/Seek，但必须如实显示其状态。
  - 位置：`Build.ps1`、`Install.ps1`、templates/examples、安装归属与包清单。
  - 改动：普通包不携带或自动运行研发示例；Author 包提供 SDK/模板；Validation 包附 proof/fixture；同候选验包、启停恢复和重启持久化。
  - Complete when：deliverable-scenarios 全部 Tests 通过。通过仅表示该基础预览，不勾选完整 B01–B19 或 production admission。

## 后续范围

任意 Seek、完整 LOCAL/Workshop 玩法接管、相机可见效果与全部 B 项继续保留各自的验证状态。R08 当前离线包及文件事务已经闭合；下一离线切片继续 R06 正式 Fresh/watch/SuppressObserver 与固定 consumer 的实际功能缺口。R06、R07 实机成本和 R08 实机场景仍保持未完成。
