# 现在的进度

**11 月 23 日，Steam 已升级至 r20 Basic。** 官方谱实际恢复延迟由同曲 r18 的约 200.5 秒降至 0.103 秒，本轮看到了后续判定、结算和返回选曲。0.75 倍速和跳到第 100 秒仍被拒绝，恢复后 SDK 读不到媒体时间。r20 Runtime、Basic 和 SDK 1.3.0 均已封包。下文是先前版本的记录。[同局实测](D:/riz-m2/r20-steam-live-20261123/stock-run-01/stock-run-summary.json) · [当前交付与后续](CURRENT-CHECKPOINT.md)

**11 月 23 日，r19 玩家包和 SDK 1.3.0 已封包。** Runtime 提供纯底层，Basic 自带成绩面板、主题、F8 快捷面板与 WAV 播放。r19 增加主菜单选曲准备和自制谱开场诊断。六份部署 DLL 通过 11 套共 2,112 项离线检查，独立作者包已用内置离线源编译新插件。[r19 下载、输入和验收](CURRENT-CHECKPOINT.md)

**Steam 仍装有 r18 Basic，两轮实测已归档。** 单音符自制谱获准开局后在首个 Tap 和结算之前停住。444 条官方成绩在另一次启动后摘要一致。官方谱确认暂停，变速 0.75× 和跳转到 100 秒均遭校验拒绝。恢复回执约 200.5 秒后记录到 `Resumed`，真实曲内时间仍需验证。两个实机进程的退出码均为 0。[两轮实测回执](D:/riz-m2/r18-steam-live-20261123/live-run-receipt.json)

**接下来。** r19 封包后的源码已在受控场景减少暂停时钟准备和在途回收的重复校验。官方谱要复验播放推进、变速、跳转和恢复时的同步。自制谱要走完首音符、结算和同进程返回，并重读官方成绩。Basic 三个模组还需实机核对画面、快捷键、声音和停用恢复。[下一批功能缺口](foundation-expansion/NEXT-FUNCTIONAL-GAPS.md)

**清单怎么看。** 基础框架已勾 8/10，底层扩展 2/20（含历史 SDK 合同及结构化诊断子项），运行时重构 1/8。多项未勾选任务已有实现，仍缺该项规定的硬件、真实游戏或组合验收。[基础框架](compatibility-foundation/CHECKLIST.md) · [底层扩展](foundation-expansion/CHECKLIST.md) · [运行时重构](runtime-redesign/CHECKLIST.md)

## 以往版本的记录

独立插件在 r11 的《Pastel Lines》EASY 实机通过暂停和恢复，同局还采到一次长按的启动与自然结束。变速和跳转在开局前因时钟校验拒绝。实机只读对照定位到 `ProgressControl.Update`，自制谱运行时在没有相关插件时也提前绑定了它。[现场对照与回执](CURRENT-CHECKPOINT.md#r11-官方谱暂停恢复和时钟冲突11-23)

r11 的 Steam 实测已完成一轮。选曲页在 17.88 秒内受理单音符自制谱，画面显示谱名，6.11 秒后 SDK 失去跟踪。新记录把错误缩小到动画来源上下文。开局前的 444 条官方成绩已有摘要，本地结算、赛后成绩核对和会话释放仍待验证。[r11 实测回执和当前阻塞](CURRENT-CHECKPOINT.md#r11-单音符自制谱-steam-实测11-23)

r10 已装入 Steam。同一张自制谱在主菜单 1.30 秒得到可重试的 `NotReady(17)`，进入选曲页后 17.59 秒收到受理回执。受理后 6.28 秒出现 listener/source 未知，新的诊断将首次托管故障定位到 `VerifyReady` 读到原生 V7 的 `phase=4, unknown=1`。屏幕显示谱面暂停，手动继续一次后仍未出现结算。游戏正常退出，五个临时插件文件已移出。本地结算、成绩隔离和会话释放仍待实机确认。[r10 Steam 实测](CURRENT-CHECKPOINT.md#r10-steam-实测11-22)

r10 独立安装包已通过隔离安装、校验和卸载。冻结源码通过 1043/1043 音频与内容检查，候选 ZIP 的入口组合与独立 SDK 插件宿主各通过 3/3。[r10 Windows Steam 安装包](D:/riz-m2/RizToolbox-r10-Windows-Steam-install.zip) · [构建和安装回执](CURRENT-CHECKPOINT.md#r10-构建和安装11-22)

r9 实机在 17.60 秒内受理自制谱，游戏显示了谱名，随后 SDK 停止更新。结算、成绩隔离和会话释放尚未通过。已重现三个游戏函数入口未被 SDK 校验识别，并补上首次失败原因的记录。[本次实机与修复](CURRENT-CHECKPOINT.md)

r9 已生成 [Windows Steam 安装包](D:/riz-m2/RizToolbox-r9-Windows-Steam-install.zip)。冻结源码的音频与内容检查通过 1043/1043。使用候选 ZIP 内的 DLL 复验，协程 13/13、长按与超长谱线 20/20、外部插件场景 3/3。实机的自制谱结算和成绩隔离仍是本轮的验收目标。[当前记录](CURRENT-CHECKPOINT.md)

Steam 已实测 r8。同一张单音符自制谱从选曲页请求到受理耗时 19.91 秒，r7 为 118.18 秒。结算仍待修复。日志已区分初始化父协程与同控制器上的嵌套子协程，首次故障来自子迭代器未被 SDK 正确识别。r8 保持安装，测试游戏已退出。[实机回执与后续工作](CURRENT-CHECKPOINT.md)

r8 已合入两处与自制谱面开局有关的修改。动画轨道的两次读取在 64 槽位夹具中从约 22 秒降到半秒以内；协程出错后可以保留第一个故障和调用信息。分帧准备播放控制也已验证撤回、恢复和提交。实机最新记录仍是 r7 的游玩界面，本地结算待验证。[源码、实测与离线证据](CURRENT-CHECKPOINT.md)

r7 已装入 Steam。从选曲页启动同一张自制谱面，游戏接受请求并显示游玩界面。开局耗时约 118 秒，之后 SDK 在初始化协程时失去跟踪，本地结算和成绩隔离尚未通过实测。游戏已正常退出，测试插件已移出。[安装包、两次线程采样与实机记录](CURRENT-CHECKPOINT.md)

r6 在实机验证了同一个插件能保留选曲，从主菜单遭拒后到选曲页重试。第一次请求干净退出，第二次已找到歌曲卡片，并定位到 CutOut UI 组件类型检查错误。r7 源码已修复该检查，自制谱面开局与结算仍待验收。[实机经过与证据](CURRENT-CHECKPOINT.md)

r5 独立安装包已通过隔离目录安装和卸载，候选已在 Steam 返回首帧。自制谱面实机通过判定设置初始化，开局继续走到获取歌曲控制器。当前主菜单没有活动歌曲卡片，拒绝开局后会留下一个未完成的会话；下一项是可恢复的拒绝和选曲页开局。[当前候选、实机回执和安装包](CURRENT-CHECKPOINT.md)

11-22 晚，普通插件已在 Steam 主菜单注册并选中自制单音符谱面。首次点击开局时，原生判定列表尚未初始化，SDK 返回了具体原因。开局与结算仍待验证。[实机回执](../work/b15-live-local-song-20261122/live/20261122T111203641Z-53f9de4917cd47b6ae6f39b925c789e3/result.json)

11-22 晚，r4 已安装并在 Steam 启动。SDK 的本地谱面保护已就绪，两个普通插件在实际游戏中将背景音乐从 100% 逐层调整至 98%，随后逐层恢复到 100%。本地谱面的完整开局和结算、真实长按采集及完整变速跳转仍待验收。[当前候选与验证记录](CURRENT-CHECKPOINT.md)

11-22 晚，Steam 已运行 r3。可选后台播放经玩家确认支持手动暂停。新包修复了本地谱面保护启动时读取帧数的错误，随后发现判定模块的就绪顺序仍阻止本地谱面开局。两只音量测试插件读出了界面与 SDK 的单位差异，在写入前结束。当前源码另有长按控制器数组的修复，等待新包实测。[当前交付与验收证据](CURRENT-CHECKPOINT.md)

2026-11-22 已在 Steam 版验证安装、Unity 首帧、同谱重开及后台游玩。开启后台播放后，游戏在 Steam 窗口后继续约 110 秒，结算页与 SDK 均记录 479 Miss。关闭开关后切出游戏会暂停，用户也确认开启时仍能手动暂停。两只独立插件在真实宿主完成双向调用、服务撤回、旧引用失效和重新获取。完整 Speed/Seek、成功通关与 B01–B19 总体验收仍开放。 [实机对照及当前交付](CURRENT-CHECKPOINT.md)

2026-11-18 的阶段记录。**暂停倍率已接入统一命令入口。**

同步和排队操作共用会话占位。命令可以在执行前取消，临时观察丢失后仍保留未知写入的拒绝。实验倍率面板读取队列终态。当前九套托管回归和三个自有原生播放场景通过。

下一步拆分正式倍率操作的安装审计和实时验证，再迁移独立 Clock 读取。真实游戏的成本、基础场景和 Speed/Seek 仍待验收。详见[续接点](CURRENT-CHECKPOINT.md)。下方为历史记录。

<!-- CURRENT-20261113:BEGIN -->
更新：2026-11-13T14:37:28.434200+08:00。**完整底层尚未完成；本轮继续补任意时间跳转需要的画面与状态重建。**

本轮已经写进主项目的功能：

- 保留实际线段、圆环和 Canvas 的对应关系，包括没有音符关联的线段。
- 从同一次原始采集中复制 Canvas 槽位和已初始化缓动表，供后续计算使用；外层释放后由独立持有关系保护，数据变动会拒绝继续使用。
- 计算动态连线的可见范围、采样点，以及圆环的静态/动态 X 坐标，遵循游戏逐步浮点运算。圆环 Y 明确保留。
- 构建脚本覆盖新增测试，并将较重的原生夹具用例放在独立进程运行。

这些是底层内部模块，本批主树离线回归已接受；**真实 Speed/Seek 仍关闭**。当前六组件构建已过6/6，数值13组已通过、圆环7组已通过、来源19/19、Canvas基础16/16、原回归300/300。证据已归档，完整SDK仍未完成。

还缺的主要工作是：把计算输入接到真实目标世界坐标和初始化状态，补齐线段/圆环写回与渲染后续工作，再与判定、Hold和音频时钟组成完整跳转事务。真实游戏验证按你的安排后置；本轮未安装或启动游戏。

详见[实时续接点](CURRENT-CHECKPOINT.md)。下方均为历史记录，“当前”“最新”只描述当时批次。
<!-- CURRENT-20261113:END -->

2026-11-01。PC Mod公共底层仍未全部完成，当前继续补玩法来源和初始化。

当前在补共享构造函数改写播放器时的监控：原生入口负责区分当前播放器和其他对象，托管层负责保留及交接归属，专用夹具验证实际调用。三路均在独立D目录，未验收。另已定位下一批直接AudioSource播放、停止和循环设置入口，详见[具体任务](foundation-expansion/N2-DIRECT-AUDIO-CONTROL-NEXT.md)。

最新已合入**没有旧播放器/旧Clock时的首次初始化**：真实Start完成后捕获首个音源，等待实际Pause才建立唯一Clock；原SDK能变速，并继续同局资源重建。8场景339项通过，新整树28套2598、solution零错误/零警告、双插件SDK14、六组件Release通过，未启动游戏。[本批报告](../docs/validation/playback-first-capture-20261101/README.md)。下一补共享MUG构造器改写当前播放器时的持续失效处理，完整底层和B矩阵仍未完成。下方“首次初始化未合主/忙碌重建仍执行”均为此前进展。

**最新已合入：同局初始化更换播放器后的受控恢复链。** 旧控制权在Start实际入口结束，跨协程让出独立保留对象，真实退出后条件捕获七根，后续Pause/readback重新激活唯一Clock。原Module/Service/双SDK单次恢复70项通过；Source V4新组合28套2598、solution零错误/零警告、SDK14及六Release通过。读取不可用的可选换算不再误撤有效控制票。[当前报告](../docs/validation/playback-start-initialization-20261101/README.md)。连续两次重建和忙碌交接仍在执行，不宣称全底层完成。

主树已是Source V4，以下SourceV3与“初始化未合主”的段落为此前历史。**现在并行补两处实际功能：首次初始化尚无旧Clock时捕获新资源，以及共享构造器修改当前播放器时及时撤销旧控制票。** 二者均在独立D目录开发，尚未合主；当前主树13个初始化场景547项已重新通过，连续两次重建与忙碌交接仅续缺项。还缺完整generation/未知lower writer、生产首次准入、任意Seek、effect与真实B矩阵。游戏安装/启动/UI/账号继续禁止，真实性能验证后置。

**本轮已完成：外部改播放位置时，旧时钟和旧控制事务在真正写入前失效。** 正常Resume仍按实际SetTime、pitch重置、调度和速率恢复继续。八种场景728项通过，修正后的异常夹具保留原SEH与未知资源责任，没有把异常写成正常退出。[本轮报告](../docs/validation/playback-set-time-writer-20261101/README.md)。

当前合入的SourceV3通过27套2465/2465、六DLL Release、解决方案零错误/零警告、SDK双插件14。固定映像11成员安装另30项通过，游戏方法/DllMain调用数为零。MusicPlayer、相机X、轨道/音符颜色、结构化SDK、持续绑定、确定Play回调和跨局接续保持现有实现。

**下一在补：游戏初始化更换资源时，跨协程让出期间先锁住新控制，真正退出后重新捕获资源并恢复同局服务。** 独立D源码已实际跑通Start从让出到继续、clip子调用、两次字段写入与物理退出；raw返回值/异常/栈展开共两进程91项。跨yield锁定、七根重捕获与专用同run恢复还在实现，未合主；旧91项不验后续改动。

仍缺完整source generation、未知底层writer、生产首次准入、任意Seek的音符/判定/Hold/协程/输入/结果重建，以及同候选实机矩阵。游戏实测、安装、UI和账号操作继续后置；真实Speed/Seek未开放，性能和旧API兼容不做本轮前提。[续接点](CURRENT-CHECKPOINT.md)。

以下保留历史冻结与表格；其中“当前”“最新”只描述当时批次，不能替代上面的本轮状态。

下方表格和 Boolean 批次保留历史状态；当前 operation 合同为 V7 / 2528B / 26 / 64，EngineTransport 独立保持 V5。

**当前：功能还在补齐，完整底层尚未验收。按最新设计统一接口与协议，旧框架 API/SDK/包的向后兼容不作为约束；性能优化后置，游戏实测暂缓。**

| 这一层负责什么 | 已实现并验证的内容 | 仍待完成 |
|---|---|---|
| 公共 SDK 与插件宿主 | 模块、服务、scope、清单校验、资源与回调回收；本批删除旧 SDK 转换桥和旧二进制兼容矩阵 | 全部 B01–B19 与多插件共存验收 |
| 原生启动和调用桥 | V5 engine transport；当前 V6 操作发布统一 26 操作 / 57 写入，22 个 Tween 源点与实际 CLR 全通道采用 | 真实游戏首次执行准入与冷启动实测 |
| 正式本地内容功能 | play/source/result/return/retry、重试来源更新、设置变化订阅；真实 delegate 写者的历史归属 | 真实整局、暂停、重试、返回菜单验收 |
| 局内相机标量 | `riz.game.camera` 已注册到 `RizlineAdapter`；共享 float 仲裁、只改 X、scope 恢复与旧 graph 失效；相机 33/33、整树 27 套 2369/2369 | 真实 update/render 次序、可见效果、输入投影、停止恢复与生产准入 |
| 监听器内容与资源退休 | 已补实际 `CachedInvokableCall<bool>` 的闭类/字段读取、独立 delegate root、具体目标/方法/参数快照；只改 bool 也会识别 | Tween 排队与结束的实际 owner、UI 后续工作、全部未来发行来源关闭 |

最新源码快照实际通过 Audio/content **866/866**、协调器 **28/28**、对象身份 **10/10**；六套相关专项 **468/468**（Runtime 216、Hooks 189、Hosting 20、SDK 7、Module 26、Adapter 10），同进程 CLR 采用两模式 **423** 检查。六组件 release 构建成功，0 错误、1 条既有生成文件 nullable 警告。[本批完整范围](../docs/validation/foundation-cached-boolean-20261028/README.md)。

[当前候选包](../artifacts/foundation-current-candidate-20261028/transport-candidate-package-manifest.json)已实际打包并独立核验 **24 载荷 / 20 证据**，包含本批源码；manifest SHA256 `36a76b474e07c0be1ef11f8c46a38d4c4617842e227c2e8886530b105bddfbf0`。没有安装或运行真实游戏，生产准入仍关闭。安装器的 17 项完整合成矩阵已在上一份冻结的 20 操作候选上通过；本批载荷更新没有重复该矩阵，不将历史结果写成新包实测。

接下来先实现 [CutOut 实际 Tween 的工作归属与发行关闭](../docs/validation/foundation-cutout-listeners-20261027/README.md)，再补 UI 监听器后果和未来来源；不把方法返回、场景死亡或一次空队列采样当作异步工作结束。

以下保留 2026-10-27 及更早的历史证据，其中“最新”“下一步”均是记录当时的状态，不是本批统一验收。

<!-- TRANSPORT-V5-CURRENT:BEGIN -->
**当前结论：完整底层尚未验收；加载、12 通道桥接、生命周期及候选安装链已有实现和受控验证，当前重点是把正式功能接线补齐。** SDK、插件与服务框架已有实现，不能把组件测试通过当成全功能交付。按用户“先继续代码，实测稍后”，本轮没有安装或运行真实游戏。

用户最新安排：**性能优化后置，先完成正式功能接线。** r7 固定依赖图读取计划的九种真实自有进程模式已全部通过（包括 IAT/ILT 实际变化拒绝）；p50 6586.8 μs、188112 B/次仅作为当前已知成本，入口已写注释，未接入新的 TypedPlan 优化。当前功能施工为正式本地播放/结果页/返回/重试、游戏设置变化订阅，以及 UnityEvent 跨调用的效果责任收口；尚未把施工中的源码记为通过。

用户后续明确：**框架尚未发布，不再为旧 API、SDK、协议及旧包兼容维持设计或扩大回归，统一按最新结论实现。** 游戏版本指纹、当前组件间 ABI、线程/对象/资源生命周期验证仍保留。最新功能 core-r2 已实际通过 844/844 音频/内容、28/28 协调器、10/10 对象身份；含重试重新捕获当前经理/控制/判定来源、正常空来源与真正根保留失败区分，以及未来 listener 写者不能由过去空调用代签。core-r1 的七套相关专项另外 215/215 通过（Settings 43、UI/input 79 等）。[功能接线报告](../docs/validation/foundation-function-wiring-20261027/README.md)。以上均未安装或执行真实游戏；完整未来写者正向与生产首次准入仍未完成。

core-r2 对应六组件 release（`engine-transport-production-managed-r3-20261027`）实际构建成功，0 错误、1 条生成文件 nullable 警告。已在项目桌面目录生成 [本批冻结候选](../artifacts/engine-transport-v5-function-candidate-20261027/transport-candidate-package-manifest.json)，24 项文件及 12 份证据，打包独立核验和逐项哈希均通过，manifest SHA256 `9268fb200814538cd509ec148a287f4f1c5ae8b2a0eb960fc0b54e2f2b9058b9`。它仍是生产准入关闭的候选，未安装；不会自动包含后续源码。

最新委托入口已实现并通过本批验证：`InvokableCall.add_Delegate` / `remove_Delegate` 两个实际写入口统一为 20 个操作、29 次首次写入及唯一 GetInfoV4。`delegate-writer-core-r2-20261027` 实际通过 Audio/content 857/857、协调器 28/28、对象身份 10/10；另 Runtime 207/207、Hook 189/189。实际映射发布 7 进程共 6383 条检查、Unknown 并发读取 896 条、五种 no-write 拒绝 200 条通过；同进程 C# 实际采用 20 通道及重入拒绝的两个进程共 423 条通过。映射未执行游戏函数，13 个 delegate 行为用例使用真实 ledger 与受控观察，不冒称实际 IL2CPP CAS/副作用已完成。

[最新统一候选](../artifacts/foundation-latest-20op-candidate-20261027/transport-candidate-package-manifest.json)已实际打包并核验 24 项文件/20 份证据，托管 r4 与最新 V4 helper 同包；manifest SHA256 `69839aeed47ac64f9f44060cf7fa1bfc3e4301cce17c51f2e26d1423a9605117`。它不再带旧 18 操作 helper，源码/构建/profile/映射/采用证据绑定同一版本。此候选的最新合成安装矩阵已实际 17/17 通过，覆盖当前 helper/证据拒绝、完整安装回滚、实际安装子进程中断恢复和卸载保留；[结果](D:/riz-build-cache-next/delegate-writer-install-r1-20261027/test-result.json)，SHA256 `91415396e246d6ab9be0209ad36438accd9e670b35153096db0ea52c792ca3ae`。没有安装或运行真实游戏。完整非空效果 owner、直接/序列化/泛型与外来未来写者及生产首次执行准入仍未完成。

| 层次 | 当前实际证据 | 还缺什么 |
|---|---|---|
| SDK、插件与公共服务 | 已有 SDK、加载器、模块、作用域、服务及输入/数据/视觉/音频等实现；B03 与 B18a 已分别验收 | 其余 B01–B19 完整验收，多插件共存与恢复 |
| V5 原生启动与传输 | 五模块普通依赖冷启动实际通过；12 组 Configure、36 个写入窗口、67 个 RF；自有 TLS0/TLS1/Entry 已实际记录 36 窗口补丁字节及相同线程 | 生产准入、首次游戏执行窗口、真实游戏验证 |
| 机器续接与栈展开 | 31 组真实机器对照通过，298 份实际 CONTEXT/探针栈证据；修正四个 Dispatch 序言内的链式展开身份 | 游戏中覆盖；不能据此宣称所有异常传播均已验证 |
| C# 采用与协调器 | 真实同 PID 采用全部 12 组；第二消费者共享、逻辑退订/GC 后根保留、重复全采用拒绝；109/138 条检查分别通过 | 将真实传输、游戏对象账本、取消与最终 Close 接成同一实际流程 |
| 取消与最终释放 | 已接真实取消回执、19 项消费者验证、实际 ResultBack→返回→Close、逐根释放及 15 项最终 owner 表；收紧 listener 后 Audio/content 796/796、协调器 28/28、对象身份 10/10 通过 | 生产全来源与任意 listener 副作用覆盖；真实游戏整局取消及最终释放验收 |
| 安装与实测 | 最新 20 操作 / 29 写入完整候选，24 载荷 / 20 证据；该候选 17/17 合成安装恢复场景实际通过 | 真实冷启动、整局、暂停、重试、返回菜单与多插件共存验收 |

最新原件：

- 最新活动读取 r6：`D:/riz-build-cache-next/engine-transport-owned-managed-r6-20261027/test-result.json` 实际 7/7 模式通过，构建零警告、零错误；新增真实导出 EAT 篡改立即拒绝。20 次测量 p50 8206.1 μs、p95 12299.7 μs、p99 12771.5 μs，平均分配 263328 B/次；较 r5 明显降低，但仍不适合作为每帧底层。静态文件解析可复用，实时源字节、保护、展开及依赖图仍验证。r6 修改尚未包含于 package-r2；对应 core-r6 已实际通过 Audio/content 796/796、协调器 28/28、对象身份 10/10，Runtime 零警告、Audio 夹具原有 17 警告，均零错误。[范围与数据](../docs/validation/foundation-engine-transport-owned-managed-20261027/README.md)。
- 最新安装矩阵 r4：`D:/riz-build-cache-next/engine-transport-package-install-r4-20261027/test-result.json` 实际 16/16 通过、runner 退出 0，对象是 package-r2。额外覆盖正式 V5 安装入口的只读 `ValidateOnly`、真实 GameAssembly 指纹、外来目录拒绝及旧安装器覆盖 V5 需显式降级；中断 journal 的只读拒绝也已验证。全部使用合成游戏目录，没有执行游戏或 native payload。
- 冷启动：`D:/riz-build-cache-next/engine-transport-owned-cold-r1-20261027/test-result.json`，PID 44176、退出 0、Ready、12 组配置、36 处提交、67 个 RF、91280 字节原始发布记录。仅执行自有入口/TLS/UnityMain2 短函数；未执行原游戏函数。此处证明加载完成后、wmain 中的 Ready 与不变性；自有 TLS 没有采样，因此不冒称首次 TLS 前窗口已获证明。
- 完整原生构建：`D:/riz-build-cache-next/engine-transport-owned-candidate-r3-20261027/owned-candidate-build.json`。实际链接 Bootstrap、Doorstop 与宿主；失败 r1/r2 原件保留。
- relay 机器：`D:/riz-build-cache-next/engine-transport-relay-machine-r4-20261027/test-result.json`，31 组，9742 条子检查与 4019 条父检查通过；[独立说明](../docs/validation/foundation-engine-transport-relay-machine-20261027/README.md)。先前三轮失败原件保留。
- C#：`D:/riz-build-cache-next/engine-transport-managed-layout-r3-20261027/test-result.json`，构建零警告、零错误；该组是结构/模型回归，尚非真实 V5 Native→CLR 全采用。
- 实际联合采用：`D:/riz-build-cache-next/engine-transport-owned-managed-r3-20261027/test-result.json`。正常 PID 6124 与重复采用 PID 46332 均退出 0，分别 109 与 138 条托管检查；12 个真实 Kernel Attached 从 0 变 1，全部计数及完整发布 raw 前后不变。重复全采用仅使托管 owner 永久 Unknown，原生资源/声明仍保留。实际构建零警告、零错误；自有进程未执行原游戏函数。本轮新 V5 owner 的同步组合前后各做完整验证，内部仍核实时发布及 Kernel 身份，修复了重复全文件哈希的倍增开销；r1/r2 超时原件保留，不视为通过。
- 完整原生 Verify 实测：`D:/riz-build-cache-next/engine-transport-owned-verify-benchmark-r1-20261027/test-result.json`，PID 45648 五次真实验证均通过，平均 801410 微秒。新联合采用耗时约 26/28 秒；这些是本机冷启动受控验证耗时，不代表游戏运行性能。
- 实时校验补强 r4：`D:/riz-build-cache-next/engine-transport-owned-managed-r4-20261027/test-result.json`，正常 PID 48892/109、重复 PID 50736/138、真实源字节篡改拒绝 PID 48472/108 全部通过。内部继续独立核对实时源/relay/原函数/图/RF/保护，只复用同操作前后完整文件 hash。篡改从 255→254→恢复 255，采用前被抓到，恢复后托管 Unknown 不复活；见[联合采用报告](../docs/validation/foundation-engine-transport-owned-managed-20261027/README.md)。
- 取消联合 r2：`D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/test-result.json` 三个独立自有进程通过。正常 36 条托管/238 条原生检查，缺来源 33/238，错代次 34/238。真实 L Begin/EndFree、N Dispatch、M Callback 和 Stop→Detach 在同节点运行；正常五个统一自有来源对象单次释放，两种负例保根。此组并未实际执行全部 M row，也不等于生产 Unity 五对象封口，生产 EngineCancelled 没有由测试令牌开放；见[范围与原件](../docs/validation/foundation-engine-cancellation-owned-20261027/README.md)。
- 最终源码快照回归：`D:/riz-build-cache-next/engine-active-retirement-core-r4-20261027/test-result.json` 顶层通过，500 项声明输入冻结。Audio/content 795/795、协调器 28/28、对象身份 10/10；Runtime 构建 0 警告，Audio 测试夹具保留原有 17 警告，均 0 错误。r1 缺草稿依赖、r2 已转交且无可释放根的 Unknown no-op 被误拒、r3 将 obj 误算源码清单均保留失败原件；逐项修正后 r4 才作为最终通过。
- 固定游戏 V5 原生生产候选已构建并独立静态验证 5 模块/44 条最终依赖边；六托管组件 release 构建成功，1 条生成文件 nullable 警告、0 错误。目录分别为 `D:/riz-build-cache-next/engine-transport-production-native-r2-20261027`、`engine-transport-production-final-validation-r1-20261027` 和 `engine-transport-production-managed-r1-20261027`。生产准入关闭，尚未打成新版完整包或安装游戏。
- 后续收紧 listener 判据：零派发计数不能证明真实 Invoke 没有非空 listener。`D:/riz-build-cache-next/engine-active-retirement-core-r5-20261027/test-result.json` 已实际顶层通过 796/796、28/28、10/10；来源快照和归档均保持不变。[独立报告](../docs/validation/foundation-active-retirement-20261027/README.md)明确区分受控检查与尚未执行的完整游戏退出流程。
- 新完整候选为 `D:/riz-build-cache-next/engine-transport-production-package-r2-20261027`（24 payload + 12 evidence，采用 managed-r2 的 r5 源码快照）；此前 package-r1 的合成安装矩阵在 `engine-transport-package-install-r2-20261027/test-result.json` 实际 11/11 通过。包含真实旧 V4 降级再升级、base23 嵌套 Kernel 迁移、外来/被修改文件拒绝、回滚、PID 29400 安装 worker 被真正终止且确认终态、候选丢失时先恢复 journal、卸载保留修改与无归属文件。本轮均在输出目录的 fake-game 中，没有执行任何游戏/native payload。
- 完整托管回归：`D:/riz-build-cache-next/foundation-managed-full-r2-20261027/test-result.json` 实际 24 套 / 1790 项通过，523 项输入冻结；新 SDK 分发独立 `foundation-sdk-distribution-r1-20261027/test-result.json` 10/10 通过，真实打 NuGet 引用包、生成两个插件、编译并经正式加载器验证共存/停用/能力撤回恢复。两组相加为 25 套 1800 项，范围不含游戏或新的原生冷启动证据。full-r1 缺少 UIInput 链接的两个 examples 文件导致构建失败，原件保留；r2 将真实 examples 输入一并冻结后通过。
- 自有最早窗口观测：`D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/test-result.json`，PID 20296、287 项原生检查、退出 0。TLS0/TLS1/Entry 在 wmain 前真实采样，随后自有导出记录第四条；四条记录的实际 36 窗口字节均与同 PID V5 发布补丁相等，TID 47488 与真实 producer/main 相同。只证明自有映像最早观察点已具备补丁，不能替代真实游戏所有首次执行者安全证明。r1/r2 首读因观察区未写页面实际为 SEC_IMAGE WRITECOPY(8) 被严格 RW(4) 校验拒绝；保留诊断后仅为同一已核 RW 非 X `.data` 的观察区允许 4/8，代码与 unwind 的 RX/R 校验未放宽。
- 活动读取 r5：`D:/riz-build-cache-next/engine-transport-owned-managed-r5-20261027/test-result.json` 六个实际进程通过，含首次采用、重复采用、临时篡改、20 次耗时测量、源字节和保护篡改立即拒绝。实测当前 p50 29466.7 μs、p95 37184 μs、p99 39971.7 μs，每次平均分配 6366352 B；该成本不适合逐帧调用，正在优化静态表重复解析与临时分配，尚未把此版本打入最终包。完整 native/file 校验保留在采用/共享/变更边界，活动快路径仍须逐次核实时源/relay/RF/保护/图与全部 Kernel 身份。

接下来：补全取消及所有消费者最终退休，验证真实 ResultBack 请求→退场→Close→逐根释放；更新 V5 候选并在用户恢复实测后执行游戏验收。取消终态正向生产证据尚未成立，生产准入与 `RequestPlay` 仍未开放。不能按测试条数换算全底层完成百分比。
<!-- TRANSPORT-V5-CURRENT:END -->

## 2026-10-26 及更早阶段记录

下方保留当时的证据范围与待办快照；涉及“当前”“下一步”“只有声明”等表述均以该历史时点为准，现况以上方 V5 表格为准。

公共框架、独立原生运行核、真实启动组件的源码接线与候选包已经具备；完整底层仍未验收。当前重点是把对象销毁、协程取消完成和整局资源释放接成可验证的闭环。按用户“先继续代码，实测稍后”的安排，本批没有向真实游戏安装候选，也没有运行游戏。

| 部分 | 已完成并验证 | 仍待完成 |
|---|---|---|
| 公共接口与服务 | SDK、加载器、模块、作用域、跨插件服务、存储、语言及输入/数据/视觉/音频等实现；部分已有较早游戏证据 | 各能力完整验收、多插件冲突与恢复矩阵 |
| 生产启动组件 | r5 统一构建和生成指纹；五模块身份、17 条磁盘依赖、82 项代理导出核验；独立冷启动四场景符合预期 | 真实游戏加载顺序、运行时 IAT 与首次执行窗口；生产写入许可仍关闭 |
| 候选包与安装 | 最终包 24 项 payload；合成目录中升级、重复安装、降级、卸载、中断恢复等 13 场景/98 断言通过 | 真实游戏安装与运行验收 |
| 对象与协程生命周期 | Controller、EventTrigger 各自保存身份、稳定零指针和独立死亡状态；失效阻止 Ready；自然观察根退休增加精确原生完成条件 | 原生取消完成、允许的迟到清理路径、所有消费者最终结束和整局资源释放 |

此前 K 批已经允许三类 tracker 逐条回收已证明结束的记录；其他还在运行、未 Start 或缺内联清理回执的记录继续各自保留。原 factory 新增 Close 后释放八个根、再释放两个参数根的正向路径，Stop 也已关联到实际 Detach 节点代次。完整 698/698 受控回归通过（新增 38 项），详情见[本批记录](../docs/validation/foundation-engine-retirement-20261026/README.md)。冻结来源的五个观察根、其余 owner 与整局保护的最终退休仍需后续实现；全消费者生产 verifier 尚未完成。

L 批已补齐并验证 Start 内部清理回执与同一节点代次的关联：自有原生 15 场景/7137 断言、磁盘 13032 检查通过；完整 Audio/content 已为 718/718（新增 20 组），Runtime 协调器 15/15（新增 9 组）、对象身份 10/10。真实 Native→CLR→实际账本四场景也全部通过，共 189 条托管和 293 条原生断言。对应 Start 实际 Complete/Dispose 后才确认该代清理，退订保留旧 Start 原函数内的子调用。所有新执行均在自有进程，tracker 身份来源受控；尚未接到真实 Unity、重打候选或安装游戏。详情与仍待补齐的回调 closing/异常保留边界见 [L 批记录](../docs/validation/foundation-engine-inline-20261026/README.md)。

最新实际结果：

- O 批已进入正式启动流程的传输准备：固定游戏的 13 个函数、23 个观察点和 36 处物理写入窗口已从磁盘完整核对并生成合同。新增九个原函数桩已经严格构建，在自有进程完成 108 次实际调用、202 份真实单步 CONTEXT 的栈展开，以及各 36 次 SEH/C++ 异常返回；最终原生 874 条、外层核验 959 条全部通过。V5 原生通信结构已实际编译运行，确认总长 91280 字节；对应 C# 实际布局校对 7/7、协调器 28/28、对象身份 10/10 全部通过，构建零警告、零错误，缺原生证据时也实际拒绝通过。23 处 relay 准备代码已修复 Windows `near` 宏命名冲突并通过严格编译，但尚未运行机器夹具。上述结果均不等于已接入生产或游戏，完整首次安装和托管采用仍缺。证据分别在 `D:/riz-build-cache-next/engine-transport-original-cases-r4-20261026/fixture-result.json`、`engine-transport-publication-layout-r1-20261026/layout-result.json`、`engine-transport-managed-layout-r1-20261026/test-result.json` 和 `engine-transport-relays-compile-r2-20261026/compile-result.json`。

- O 批后续：relay 准备 r2 已实际严格构建并通过 14/14 场景，子进程 554 条、父进程 41 条核验通过；包含真实 near 写入 4096 字节后遇不可访问页，所有映像字节与游戏磁盘保持不变。修正 Dispatch 的四个观察点仍在源函数序言内的展开合同：它们独立描述已执行的栈分配和 RDI 保存，排除尚未执行的 RBX 保存；其余 19 处保留 CHAININFO。该夹具不执行 relay，也不注册函数表，不证明机器运行或生产安装。36 窗口提交器新增源码已通过生产版及故障夹具版严格编译，实际逐窗口失败矩阵正在实现。证据：`D:/riz-build-cache-next/engine-transport-relay-preparation-r2-20261026/test-result.json`、`engine-transport-sourcewrites-compile-r2-20261026/compile-result.json`。

- 36 窗口提交器最终 r4 已通过 226 个实际自有场景：正常全量提交、6 阶段 × 36 窗口的 216 个故障注入、9 个预检拒绝。子进程 79756 条、父进程 27827 条核验通过；真实私有映像写入共 47606 字节，225 次函数表注册和删除均完成，所有输入与游戏磁盘 SHA 前后不变。Plan 与实际 near 同时被篡改也会被固定合同独立重发检查拒绝，真正 relay 返回 PC 必须严格在 RF 范围内。失败保留逐字节责任、恢复保护但不回滚源字节，重复提交不擦终态记录；6 阶段故障是明确模拟，不能冒称真实 OS 失败。该组件无 Ready、无生产安装，未执行映像/relay；完整报告见 [提交器记录](../docs/validation/foundation-engine-transport-source-writes-20261026/README.md)，最终证据 `D:/riz-build-cache-next/engine-transport-sourcewrites-r4-20261026/test-result.json`。12-family 统一 Configure 组件已严格编译，通过真实 Kernel 的运行验证和 23 relay 机器执行夹具正在并行实现。

- 编译收尾 r5：canonical 请求改为显式 intrinsic 清零，避免无 CRT 组件被编译器引入 `memset`。实际全矩阵再次 226/226、79756 子断言与 27827 父核验通过，PID 2472，证据 `D:/riz-build-cache-next/engine-transport-sourcewrites-r5-20261026/test-result.json`；最新 Relays、SourceWrites、Families 三组件生产选项严格编译也通过，12 输入 SHA 不变，检查的 CRT 未解析符号为零，见 `engine-transport-core-compile-r2-20261026/compile-result.json`。这仍是组件编译和私有映像验证，尚非完整生产 DLL 链接或游戏安装。

- 12-family 统一配置 r2 已实际运行通过：六个独立子进程、48 次真实 Kernel Configure、806 条子断言与 274 条父核验通过，包含正常配置、坏 site 拒绝、生产 gate 拒绝、篡改回执后永久拒绝、重复尝试保留原件，以及单个 Node Start 的真实 Attach。Inline 的 Start epoch 来自实际首组 GetInfo；23 对逻辑 site/实际 relay 返回 PC、35 项普通 Kernel 导入、12 配置原始 bytes/SHA 与实际 Info 均核对，源/near 没有改动。已留下 Kernel claim 的映射和函数表保留至子进程退出。此批未执行 original、relay 或映像，未提交源补丁，不等于完整托管采用/冷启动/生产安装；证据 `D:/riz-build-cache-next/engine-transport-families-owned-r2-20261026/test-result.json`。V5 发布端四个新导出当前仍只有声明，真实 producer、只读 CLR owner、首次全 family Attach 尚未整合。

- N 批普通 wait 的本批受控验证已完成，见 [N 批记录](../docs/validation/foundation-engine-wait-20261026/README.md)。原生运行核 r2 严格构建、46395 项磁盘检查通过；自有原生 22/22 场景、14989 断言通过。托管最终 r3 为 Audio/content 761/761（含 19 组 wait 用例）、Runtime 协调器 28/28、对象身份 10/10，471 项输入前后 SHA 不变并冻结。Runtime 构建零警告、零错误，Audio 测试夹具保留 17 条警告。

- 实际 Native→CLR wait 联合最终 r2 为 8/8、1590 条托管与 1859 条原生断言：覆盖普通派发、owner-null、完整返回值、Start 内实际发布后清理、Batch 空回调、callback 内清字段、同节点换 wait，以及旧 callback 内第二个实际节点重用同 wait。迟到 callback-null Dispatch 保留真实物理调用至 Complete/Dispose，允许独立对象引用收尾，不重新关联已退休节点；字段漂移仍拒绝。Start-clear 的独立 Node inline 最终清理未在此联合夹具安装，继续明确保留未确认责任。新核运行旧 M 原生 16/16、3718 断言及实际联合 7/7、654 托管与 548 原生断言也通过。

- N 批最终证据：`D:/riz-build-cache-next/engine-wait-evidence-r2-20261026/wait-evidence.json`，4847 项归档核验、504 份原件、28 项原生源和 471 项托管输入冻结；旧 M 联合保留它自己的 470 项历史托管冻结。初次原生 17/22 失败原件保留，修复夹具后重建复验。以上均在自有进程，未重打生产候选、安装或运行游戏；生产首次安装、全部写者/派发覆盖、取消完成与最终资源释放仍未完成。

- M 批 scheduler 已完成本批验证：六个原生入口、Drain 逐行探针、C# 调度行账本与运行协调器已实现。修复外部 callback 被错误等待、观察者重入误借外层 row，以及正常 Pending 被当成 Unknown 的问题。原生 r2 严格构建和 33110 项磁盘检查通过，自有原生 16/16 场景、3718 断言通过；管理 r4 完整 Audio/content 742/742、Runtime 23/23、对象身份 10/10 通过，465 项输入哈希前后不变并归档。真实 Native→CLR 联合最终 r3 为 7/7，共 654 条托管和 548 条原生断言。地址复用限自有 reservation 内 decommit/recommit；其他模式用实际 MEM_RELEASE 成功和逐行回执证明旧代释放，跨 CLR 后当前页状态只作诊断。完整记录见 [M 批](../docs/validation/foundation-engine-scheduler-20261026/README.md)。没有安装真实游戏或重打生产候选。

- I 批 r5：托管六组件构建零警告、零错误；Bootstrap 严格编译通过；2755 条磁盘检查通过。带最终指纹的 11 项检查、7 项发布模型和 6 项协调器模型通过。
- 最终候选包安装回归：13 场景、98 断言通过。实际终止安装 worker 后，另起进程从持久 journal 恢复；全程为合成游戏目录，没有加载游戏或原生 payload。
- 自有 cold r2：2 次正常冷启动及缺 Bootstrap、缺 Kernel 两种拒绝，共 4 个独立进程符合预期。正常宿主明确进入 NoWrite；这不证明固定游戏的运行时依赖或生产许可。
- J 批对象生命周期：新增 20 项 Audio/content 回归已注册，当时完整 660/660 通过；Runtime 实际对象身份读取夹具 10/10 通过（其中新增 3 项）。Runtime 测试构建零警告、零错误；Audio 测试夹具构建仍有 17 条警告。输入源码与三个原生夹具哈希前后不变。
- K 批 r3：root session 88038 退出 0，完整 698/698，通过逐记录 14 项、原 factory 11 项和取消关联 13 项新增回归。443 声明源和三个 native fixture 输入 SHA 前后不变；最终源码归档 447 项，未伪称完整工具链换机复现。构建 0 错误，测试夹具 17 条 CS8602/CS0649 警告保留。

J 批证据保存在 `C:/Users/71957/riz-build-cache/engine-receiver-lifetime-r1-20261026/`。它使用 r5 的固定身份生成文件，但生命周期源码是后续新版本；不能把旧 r5 候选称为已包含 J 变更。J 目前只经过受控检查，尚未安装到游戏。`RequestPlay`、生产 admission 与完整资源退休继续关闭。

K 批最终证据在 `C:/Users/71957/riz-build-cache/engine-record-retirement-r3-20261026/evidence-index.json`，也未重打生产候选或安装。失败 r1 的 696/697 与 r2 的 697/697 原件保留，r3 新增同帧 Start→Stop 场景后为 698/698；测试夹具错误已修正，生产 ABI 拒绝规则未放宽。关联候选不等于取消完成，`EngineCancelled=false` 保持。

下一步把 L/M/N 的 Start 内部清理、调度器和 wait 通道接入生产启动组件，具体任务见 [生产接线施工表](../docs/validation/foundation-engine-wait-20261026/next-production-transport.md)。当前生产组件仍只发布四个 Node 入口，新增通道的生产采用尚不可达，现有候选不包含本轮能力。九个固定原函数桩的寄存器续接和自有机器验证已经完成；内部跳入所需的三处扩展窗口已进入 23 处 relay 准备代码，仍待运行验证及正式安装接线。之后完成全部来源、取消、所有消费者及最终释放合同，再更新候选并按用户安排恢复游戏实测，验收冷启动、整局、暂停、重试、返回菜单和多插件共存。

完整目标仍以 [B01–B19 规格](foundation-expansion/SPEC.md)和[验收清单](foundation-expansion/CHECKLIST.md)为准；正式完成的是 B03 SDK 兼容及 B18a 诊断子项。其余有实现与局部证据，不能按测试数量换算完成百分比。

I 批详情：[生产启动组件与候选安装](../docs/validation/foundation-engine-production-20261025/README.md)、[阶段报告](../docs/2026-10-26_底层-生产启动组件接线-report.md)。H 批的 34 个自有进程及归档见[冻结记录](../docs/validation/foundation-engine-publication-20261025/README.md)，不重复计入本批，也不作为游戏实测。
