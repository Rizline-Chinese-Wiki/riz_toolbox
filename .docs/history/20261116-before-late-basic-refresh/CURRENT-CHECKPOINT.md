# 当前续接点（2026-11-16，优先读本页）

## 最新：seal-diagnostic-r1已安装，等待首次精确变化地址

当前安装已由下方cold更新为`artifacts/delivery-20261116/RizToolbox-0.3.0-seal-diagnostic-preview-20261116.zip`，37,291,883B，SHA `ae3f2a82932d0d1c9e1f0e35c5dd0c6253267c67f66e47b5fbd6d7a609083ba2`。35成员逐成员读回，25文件已安装匹配（97068终态0476ed，`install-seal-diagnostic-observation.json`）；先用Alt+F4正常退出40500，未强杀。本包只增加RangeSeal失败的模块/区间/首差offset/最多16B expected与actual，不放宽规则。Loader/Examples/Hooks Release及Hooks210新通过（33514终态ccc885）；cold的200/13/3不当作本诊断包游戏验收。

12:03:57官方Steam应用2272590已请求启动（`launch-seal-diagnostic-request.json`），新进程/会话仍待读。下一读新阶段，初始化完成后重新选当前Rizline窗口，进入主菜单/选曲/GreatnessEarth EASY FREE，触发第一个实际profile读取，取精确offset。不重复旧prefix、ControllerOver、冷状态拆分和长矩阵。

**cold-r2真实结果**：40500在03:38:14 UTC WaitingForRun，随后host/module/data444载入，Responding=true；CUA已实际从启动页进入主菜单、选曲并开局。RunId `428d4eb45df6406483d5890941e5006c`，chart `chart.GreatnessEarth.Axya.0.EZ`。03:44:30首次玩法时钟读取报`A sealed playback image, relay or relocation byte changed`，组撤销，Speed/Seek拒绝。03:45:13失焦自动暂停；SDK Pause请求03:45:19被already-paused规则拒绝，**不能签SDK Pause成功**（口头误判已更正）。25文件核对后的会话为`cold-r2-run-a-observation.json`，完整原件`cold-session-before-seal-diagnostic/`；不要用它签诊断版。

只读Sol确认：GameAssembly `_ranges`约03:35:45–46捕获，03:38:18–21才安装自身GetJudge/Pause/Resume/Judgement/Coroutine/Retry/Back/Skip等；它们不全有composition身份，generic RangeSeal先于composer失败。**首offset未确认，不把候选RVA当已验答案。** 普通Void/Bool还没有code identity；ref0有，Coroutine有私票但无composer。下一可用原固定file+DIR64+真实owner整节重建；若group exec snapshot只保留MemoryIdentity，仍须每次整节逐字节canonical比较，RO/header和普通Dobby/helper seals不变。Late owner仅来自成功Installed且不可替换的私有登记，真实branch/delegate/original/完整搬运指令/continuation均须证明；不重capture活字节、不RVA白名单、不信外部DTO。Pause/Resume相隔0x30，64B installed seal会重叠，应只贡献已证实际patch，独立canonical核preimage再两阶段组合。以上为下步约束，**未实施**。

## 上一候选cold-r2与全局约束

goal active，完整底层SDK/B01–B19与生产engine仍未完成。用户要求功能优先、性能后置、采用最新结论，不做旧兼容；必要游戏操作已授权。上轮实体Escape之后，用户已明确选择“允许恢复实测”，11/16继续有效，不为同一旧停止信号反复询问。若再发生新的实体停止，按新信号处理。代理只用Sol/max；本批一位Sol实现Backend，root完成Host并统一串行构建。

**当前已安装 cold-r2**：`artifacts/delivery-20261116/RizToolbox-0.3.0-cold-preview-20261116.zip`，37,291,331B，SHA `a94f1fb7fa29a0670403ad2f6f192a88a523f7b057528ca26a6300a645a1eda8`。35成员逐成员读回；25运行文件已安装匹配（77074终态526e3c，`install-cold-observation.json`）。保留设置，未改账号。原生与SDK合同未变，native JudgeContext SHA `5917114beb3eae7784b9f17ce246770979e1b460d14618b323f587a851a45a6b`，继续配套11/15 controls SDK。

11/16 11:34:42已通过官方Steam `-applaunch 2272590` 请求启动，见`launch-cold-request.json`。**新实际进程/会话及响应效果仍待读，不能把“请求启动”当启动成功。** 本页随后以新观察更新；尚不签Speed/Seek或流畅启动。

## 本次已实现与验证

1. `GameplayNativeBackend`把元数据探测与玩法GetTime来源消费拆开。普通Probe、独立DSP、controller发现不再隐式Refresh；实际ReadClock/RequireMapping在本次消费Refresh并只用当次成功profile。播放读取/SetSpeed仍尝试当前可选profile，mapping失败不能废掉独立provider。原cache的旧owner/sticky/晚到保护未改。
2. `NativePlaybackStockRateHost`物理安装后，若无route/run，先读取共享观察器的实际已提交关联。无局只保留故障/完成记录维护并WaitingForRun，不声称有Clock或来源；实际run出现后，原group/sourceProfile/clockProfile完整验证仍先于capture/adopt/arm。已route/已run路径不旁路。
3. 前轮已完成、现包含的member桥联合读取：pre-proof reservation独立保留；PumpCompleted/relay之后helper前后完整核对包住实际entry/channel/owner/head读取。登记、slot/epoch、类型/角色/原跳板、Unknown及故障同步撤销保留，无新票/缓存/线程豁免。静态helper配置后4→3、配置前2→1；不把静态计数当实测。

`cold-r2-result.json`九步全自然0（原session9095终态75d502）：新建owned native夹具、Loader六组件/Gameplay/Examples/Hooks四Release构建、Hooks210/210、clock-profile13/13、host3/3、默认Gameplay200/200。套件可能包含重复入口，不把各数简单相加当独立覆盖。owned夹具只执行自己的native测试代码，无游戏生命周期/安装权限。cold-r1完整Gameplay曾29项因运行脚本漏RIZ_PLAYBACK_OWNED_FIXTURE失败，171/200；原件保留，不计通过。cold-r2按Build.ps1合同补新夹具与仅子进程环境后全部200通过。

新member native桥动态正反向、queries批量body的mapped负向尚无独立正向：旧mapped runner缺自然Unity加载前提并含过期成员数，不能拿旧runner结果验新源码。当前Game实测仍是必要下一步；不为此重造长矩阵。

## 已知游戏依据与范围

- 11/15 batch-r4实际42952：所有Prepare/Commit/Arm、WaitingForRun、adapter与Unity host初始化走通，但仍分钟级卡顿，不能签可用版。
- 后继queries-r1 ZIP SHA `2e81b2a5ce8f226f4d69980c9ab1d815660917f4826431480871c5e72c21e9ef`已安装匹配；实际5172/session `20261115T154142230Z-aacfb8303a1948f6ba3b36cda553862a`：15:44:46 UTC physical complete，15:45:01 WaitingForRun，15:45:17 Unity host initialized。profile挂接约15s（batch约85s），source准备/提交约3–4s（batch约6s，旧约62s）。仍卡顿，F8电脑操作超时；后续输入状态显示Keyboard/Mouse disabled，不能签真实输入通过。
- 5172截至11/16 11:21已无进程，退出方式未观察。完整原件在`queries-session-before-cold/`，25文件复核后的最后观察`queries-r1-final-20261116-observation.json`。不要使用它证明cold源码。
- 继续保留相机已接入、N1/退休批、rate/float/Play移交等先前完成项，不重做；ControllerOver自身Hook身份共存、stock set_time的REX.W尾跳转误拒、64条来源整批读取、20条body批量查询和Clock联合查询均已完成并包含，不再按旧表重复施工。

## 下一步

1. 只读确认cold实际新进程/会话与最后`Playback installation:`阶段；可用`python -B work/game-seek-20261115/observe_startup.py cold <unique-tag>`，它同时核25安装文件，标签必须唯一。
2. 初始化完成后用电脑工具重新列窗口、选实际Rizline，核主菜单/面板/开局/暂停。只对本候选签结果；工具超时先观察，不盲重发。保持Cua专用UI入口，不操作账号认证，不换过期账号。
3. 若仍阻塞，按新日志/两次实际栈处理第一个确定问题；不重跑未改native四模式、controls56、旧SDK14、整27套或大归档。
4. 可用后继续实际完整七根Seek/HoldEpoch、真实音频时间精度、Speed/Seek正向及其它B01–B19/engine缺口。无证据的能力仍如实不可用，不用Supported/编译或窄fixture提升为全部SDK完成。

原较长续接页逐字保留在[历史页](history/20261116-before-cold-runtime-refresh/CURRENT-CHECKPOINT.md)，包括更早包SHA、案例和未完成范围；冲突以本页及新实际证据为准。主README固定SHA仍`CFBAC4691D36EDC5DABA513AA62D5856AE61E29BF553D0DC2979832750847364`；旧ZIP未改，当前无编译在途。
