# 当前续接点（2026-11-17，优先读本页）

## 最新实际阻塞及施工（11-17 10:31）

fresh-block-diagnostic 已完成本包真实诊断，新16288正常AltF4退出，CUA确认窗口无、shell确认进程无；无Stop/强杀，OSexit未知。原件`work/game-seek-20261117/fresh-block-diagnostic/fresh-diagnostic-r1-game-result.json`：接管141.997524秒、SDK Pause精确事件33.49061秒，SDK Resume dispatch32.819183秒（不等于立即恢复）。02:14:04Z Draining→02:14:09Z Withdrawn，实际 **status4/origin source/mask0x2/当前EngineSetPitch kind8/seq11 parent10 depth3/argBits0x3f800000/childFlags0**。无PreparingClock/Ready/SpeedSeek。该mask是LowerPitchBlocked，当前scope不证明历史置位者，也尚不知道parent10角色，不猜放行。

固定UnityPlayer原件已独立核对：lower495B原样传float、唯一CALL1d78f9→engineBBAD20/return1d78fe，native self无offset变换；证据`engine-child-analysis/static-verification.json`。下一 **engine-child-diagnostic** 私有V2/136B：保持同次Read和原判定，只记录实际短路求值的Begin/Child tested/matched、当前真实父scope和lower函数内caller offset；不重跑getter/query、不变控制权限/失效/根。Sol实施6native，root实施2CLR，新runner另由Sol独立准备。**当前新源码正在变动，尚未freeze/build/测试；不能拿旧292或旧ZIP验它。** root唯一串行构建，未装/启新V2包，现无游戏进程。

下方“16288初始化”仅为较早观察。下一以本节为准：完成V2→新字节定向检查→新包实际parent/首个失败条件→修实证路径；B01–B19/production仍未完成。

## 当前：fresh-block-diagnostic 新包已安装，真实 16288 初始化中

最新候选 [RizToolbox-0.3.0-fresh-block-diagnostic-preview-20261117.zip](../artifacts/delivery-20261117/RizToolbox-0.3.0-fresh-block-diagnostic-preview-20261117.zip)，37,314,033B，SHA `7c8b96594520b20c2a63eccd2ac3d0eaf3d2b2eec8c341819b148257d244bb80`，35成员/25运行文件逐字节读回。`work/game-seek-20261117/fresh-block-diagnostic/install-observation.json`记录09:46:51.0949107本地安装25/25，实际目录`package-r2`；首提取因.tools已存在在安装前失败，原件保留，r2修安装脚本后成功，产品字节不变。

初始10788只到CLR pre-init后消失，无Stop。Steam启动选项实际仍待确认；CUA选择默认“玩Rizline”并点击开始，未改始终使用选项。真实 **16288 Start09:56:00.8748853+08**，绑定返回的Rizline窗口1642024，观察白色初始化画面。`fresh-diagnostic-r1-start-b-observation.json`已核25安装文件，最新01:57:46.1698664Z准备AudioStopBoolean，尚无真实nativeStatus、ReadyExperimental或Speed/Seek通过。10788不与16288混验。

## 已完成的新代码与当前字节验证

4 native + 2 CLR文件已冻结、构建、打包。原Read与新ReadWithDiagnostic共用core；同一次原生Read输出独立80B FailureV1，区分SingleReturn authority/logical及Source fresh/lowerPitch/audio/backing/logical闭合，不新增查询、票、根，不清失效flag、不放宽准入。Action4→phase0 Busy8或Verify(action2)、terminalization和原锁序保留。worker10先于Source flags/TLS采样。当前TLS不是历史writer；Reference1/未知kind argBits为0，双端只允许标量kind，不输出根地址。最新CLR强制DiagnosticVersion1，无旧导出fallback。成功208B InfoV1仍仅status0消费。

当前源码依据：`work/game-seek-20261117/fresh-block-diagnostic/source-freeze.json`1272编译输入；`native-source-manifest-r2.json`为当前标量脱敏后4 native，r1只是历史。`managed-applied.json`记录2 CLR应用；独立Sol对live native r2 + CLR当前hash静审无blocker，非运行证据。

| 当前新证据 | 实际范围 |
| --- | --- |
| `fresh-block-diagnostic/native-build-result.json`，原74160/c355a8自然0 | 新2 native（W4/WX等）+新11步托管Release/check全0；Hooks210、Gameplay201、clock14、host3、旧fresh机制31。31不执行正式stock Adoption |
| `fresh-block-diagnostic/checks-runner/checks-r2/result.json`，原78862/406982自然0 | 292=Replay113+Resume133+direct-time46；每child实际23/23+3pair、4真实root、各factory claim1。Replay/Resume完整原调用树、同票及实际DSP参数保持 |
| 同上direct-time | 同真实票worker先10且不闭合；owner随后能读；真实direct LowerSetTime IcallSingle1在当前回调拒绝4，origin=source/action2/sourceMask=0x4/role=LowerSetTime/kind8/argBits=0x3f400001；无执行票。原函数/退出/排空/根退休通过 |
| `fresh-block-diagnostic-preview-receipt.json`，原99efb0自然0 | 当前1272源hash、新helper和当前执行DLL绑定；35/25逐成员读回，不借旧包验新源码 |

两个新native helper：SingleReturn 80384B SHA `208b9c04a4b68a1548037aca05c48ec5ae6929865ba042419688f7a0cfea5e69`；Source 209920B SHA `50a1c440731a7b11bc51fe61184f1a0cd9208bc66154ff456db632a877c25214`。JudgeContext未改。SDK公共合同未改，原controls SDK包仍适用。

292的r1夹具错误把stock fixture编号8当Source IcallSingle1（实际为UInt64），scalar断言失败，原件保留；r2只改runner用真实Cdecl float入口，产品和helper未改，不能称r1通过。旧native字节下的baseline Replay/Resume246独立在`resume-current23-checks/checks-baseline-r1`，仅历史对照，不能验本新包。

所有owned证据均未执行stock foundation、Unity、正式stock Adoption、Clock或game/production；不等于实际游戏Speed/Seek通过。不重跑本已过292/11步及旧长矩阵。

## 上一receiver-read实际结论（历史，不能验新包）

旧真实2696、session89d517125f114634a2e6aa12ef9be147、run1f74b197efdb45b58ee7c21cc97aad92：GreatnessEarth/Axya/EASY4/FREE，首Clock17.7933961秒、接管150.7197304秒返回WaitingForPause；实际Paused后SDK Resume一次Applied（按钮至Applied31.4702073秒），随后Draining→Withdrawn，`nativeStatus=4; detail=native-status`。无PreparingClock/Ready/SpeedSeek正向。after-resume栈为FreshBeforeClosed→Capture.VerifyGraph→Group.VerifyReceivers，未直接记录当前role。

旧2696已在11-17 09:11:58观察进程和窗口均无，无Stop；前晚AltF4请求后用户暂停，不能证明具体退出因果或OSexit。终态见`work/game-seek-20261115/receiver-read-r1-game-result.json`及after-exit/terminal原件。旧ZIP及原日志不改。

已排除猜测：正常Resume/Replay在真实owned完整调用树中不必失票；SetTime直达确会先AudioBlocked，但尚不能认定游戏就是此role；Stock Stop(void)不调用hooked Stop(bool)；旧实际日志没有PreparingClock，不把Prepare cancel猜作根因。不新增猜测child或重发fresh票。

## 下一步

1. **只续新16288**初始化→菜单→开局首Clock/接管→SDK Pause/实际候选与Ready。若再拒绝，读这次同Read的origin/mask/currentRole，直接修实证对应路径；当前诊断源码冻结，不重复已过矩阵。
2. 真正Ready后再验Speed/Seek/恢复、七根/HoldEpoch与实际DSP精度。分钟级主线程阻塞属于可用性故障，不把性能后置当作可以忽略。
3. 全SDK/B01–B19/production仍未整体完成，goal active；功能优先，不做旧兼容，不重做相机/N1/rate/float/Play移交/退休批。

必要游戏/恢复实测已获授权，无本轮新停止信号；UI仅cua_repl，不操作认证/账号，不commit/push。子代理Sol/max，root唯一串行BelowNormal构建。主README SHA `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`保持。CUA当前新段未闭合，最终观察退出/结果后下一CUA调用须仅completion_summary。报告见`docs/2026-11-17_fresh-block-diagnostic-report.md`（离线部分由Sol整理）。上一续接页在`history/20261117-before-fresh-block-diagnostic/`。
