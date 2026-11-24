# 当前续接点

## 2026-11-15 23:33：查询去重代码已构建通过；实体 Escape 停止电脑操作（最新）

**先读本段。** 用户本轮实体Escape被电脑工具明确报告，当前已停止游戏侧操作；未退出/结束42952、未安装/启动下一包，不绕过停止信号用shell操纵游戏。只收取已在途构建并保存本续接。上一次实际查询42952仍未响应。恢复游戏操作需用户明确重新允许；goal本身未标暂停/完成，离线代码仍可按后续指令推进。

**batch真实进展已确定**：新会话42952/session151832392Z的所有Prepare/Commit/Arm已完整通过，15:23:19 UTC physical group complete，15:24:44 WaitingForRun，15:26:03 adapter initialized，15:26:04 Unity main-thread host initialized。此前lower-time和ControllerOver误拒未再出现。**仍不可用**：初始化和维护都反复重验整组，模块/插件出现分钟级延迟，不能把host initialized当流畅启动或Speed/Seek通过。新候选实测原件在`work/game-seek-20261115/batch-session-before-queries/`；按25已安装文件核对的三次观察为`batch-r4-startup-{a,b,c}-observation.json`，actual栈`batch-r4-final-arm-a-stack.log`定位到source profile逐body查询。

**已实现下一去重批（3位Sol/max，现均停写）**：20条固定Hook body改成一次有界查询，方法自身锁内VerifyCurrent→每条精确地址/完整hash/长度/唯一性→VerifyCurrent，拒空、超界和未知，没有跨调用缓存。Clock的精确module/target/metadata+完整原body组合为owner的一次查询；原profile前后检查边界保留。Group Compose去掉在所有贡献者各自完整验证之前的重复VerifyMembers，保留共同targets及Install/Commit/active外层完整检查（只读Sol核查含GetTime单图路径）。GetTime没有外部before/after观察者时各只合并中间无操作的连续同检查，游戏原调用两侧仍各自取当前事实。固定hash未改。

**新源码验证**：`queries-r1-result.json` 六步骤全0，Loader六组件、Gameplay.Tests、Examples、Hooks.Tests Release构建均0；既有Hooks210/210、clock-profile11/11。原session85349终态50228b自然0。Gameplay新增mapped batch正反向断言已编译但**未执行**：旧runner只映射GameAssembly，缺当前stock要求的自然Unity加载前提且保留旧20/11成员假设，不用旧结果验新查询，不为此重造大夹具。

**未打新包、未安装这批查询源码。** 当前安装/现有最新ZIP仍是下方batch-r4，不能用它的游戏记录签queries-r1。下一离线步骤可先`python -B work/game-seek-20261115/pack_proof.py queries-r1 queries`（若ZIP已存在先核对，不覆盖）；脚本已加入以batch ZIP核SHA为基底的queries分支，native沿用未改的batch-r4。随后更新receipt/交付说明并在用户恢复电脑操作后正常退出当前游戏、成套安装25文件复核、实际启动/玩法。禁止在42952仍运行时覆盖安装；不重跑未改native四模式/旧56/旧SDK14或长矩阵。

仍需确认查询批后剩余的共用helper/元数据放大是否让维护可用，不能只按理论减次数签性能。正式七根Seek/HoldEpoch、实际音频时间精度、Speed/Seek正向及完整SDK/B01–B19/生产engine仍未完成；goal active。主README和旧ZIP未改，无编译在途。

## 2026-11-15：原生64条来源整批读取已安装，正在复核实际启动（最新）

**当前候选是 batch，不是下面的 traversal。** 第二处实测放大已修：原来 CLR 每读一条来源记录，native 就重新检查全部64条/28个原跳板；现为同私有token、同锁下的一次有界整批读取，复制前后均完整检查真实producer。CLR全部逐行形状、地址、原字节/实际写区间、原跳板/entry/epoch/函数表、计数及不重叠检查保留；每次调用重新取证，没有跨帧接受缓存。新Runtime必须配套新JudgeContext，缺整批导出即拒绝，不回退旧协议。

**本批构建与检查**：`work/game-seek-20261115/batch-r4-result.json` 六步骤全部0：native helper、native attestation、Loader六组件、Examples、Hooks.Tests、Hooks210/210。native实际EAT完整86导出通过；mapped夹具normal325、patch337、original337、unwind337，共1336/1336，四子进程全0；不执行游戏代码、original init或DllMain，不代表真实游戏正向。原session63690终态e73cb7。r1/r2编译环境失败及r3旧导出总数85失败均保留，未计通过；使用已存在的SDK19041显式编译环境wrapper，只改构建子进程环境。

**包与安装**：`artifacts/delivery-20261115/RizToolbox-0.3.0-batch-preview-20261115.zip`，37,289,613B，SHA `64dd22364f7cc0d5563f69130bfc9013a9236f3d68f643e14a15c442e780c4f5`，35成员逐成员读回一致。25运行文件实际安装匹配（62545/b0d96c，`install-batch-observation.json`），新JudgeContext SHA `5917114beb3eae7784b9f17ce246770979e1b460d14618b323f587a851a45a6b`。旧traversal游戏40296已核实未响应并结束，原件在`traversal-session-before-batch/`。SDK合同未改，继续配套controls SDK。

**实际新会话**：Steam AX按钮仍报geometry unavailable，转官方Steam `-applaunch 2272590`，23:18:02实际出现42952，宿主session `20261115T151832392Z-d3f703f4739442259b4b9ac3c1fa52d4`。初始整组验证已测1.610s（old stages25.964s、traversal13.508s），SetPitch/Pause/Replay准备约6s（old约62s、traversal约25s）。仍在逐source安装，**尚未完成启动/玩法验收**；下一先读本会话最后阶段与首个错误，不拿旧包验新源码。更多单次观察用`observe_startup.py batch <unique-tag>`，同时复核当前25安装文件，不自动游戏输入。

完整七根Seek/HoldEpoch、实际音频时间精度、Speed/Seek游戏正向、完整SDK/B01–B19及生产engine仍未完成；goal active。下方为历史，不重复已完成相机/N1/退休批、controls56、旧SDK14或长矩阵。当前无编译在途；代理前轮用量受限已停写，root单路继续；不换账号、不改主README或旧ZIP。

## 2026-11-15：线程栈已确定重复整图验证，分层修复已安装（最新）

stages实际游戏40496的两次只读栈（`stages-r1-a-stack.log` / `stages-r1-b-stack.log`，官方dotnet-stack）证明正在GetTime Prepare的Image.Verify→Group.Compose→VerifyMembers→Source.VerifyMemberEvidence→共同targets→LowerPitch native pair全镜像/stock方法Inspect中持续计算，不是等待未发布original。阶段日志初始组验证 **25.964s**，SetPitch/Pause准备各约62s；已从“可能有限放大”升级为具体实测。既有代码没递归环，但成员重复触发兄弟镜像/共同元数据遍历使启动不可用。

**修复方式**：Group.VerifyMembers每次实际遍历入口验证共同targets一次；source保留登记、Dobby、代码/元数据/跳板/channel/native health全套局部验证，聚合组合时只调用同注册group的局部路径。独立member访问仍走完整targets验证；没有跨调用/跨帧缓存，也没有移除Prepare/Commit前后完整检查。仅改Group与SourceOwner两文件，保留已安装reference identity和一次性阶段标记。

`traversal-r1-result.json` Loader/Examples/Hooks三个Release构建0，既有Hooks210/210（session18456终态77e639）。候选 `RizToolbox-0.3.0-traversal-preview-20261115.zip` 37,292,160B，SHA `c25c7402932bf491cb63ec113bf1d4bb66890a557f4a8e303351235ffa6cb936`，35成员/25文件，逐成员读回0/0d88b2；已安装并逐文件匹配（23800/004ff7，install-traversal-observation.json）。旧stage游戏正常关闭超时，核实身份/未响应后结束，原件在 `stages-session-before-traversal/`。

Steam曾仍显示旧游戏运行，已通过其停止/确认正常清除残留状态；22:57:20再次通过官方Steam应用2272590请求新启动。**新启动结果待读，不能宣称性能问题已实测解决或Speed/Seek已可用。** 下一先看新会话各阶段耗时及第一个具体错误；若初始化完成再玩法验证。SDK合同未改；完整任务未完成，goal active，代理均停写、root继续，无构建在途。

## 2026-11-15：启动未返回已转阶段定位（最新）

broker游戏45472实际Result-origin安装成功，但随后约13分钟未返回宿主初始化（CPU持续计算、Responding=false，最后日志22:34:21）。已确认正常关闭超时后核实该测试进程并结束，原件在 `work/game-seek-20261115/broker-hung-session/`。两位Sol只读排除所查范围内确定的递归环/原delegate未发布：当前已知是有限但严重嵌套的验证工作，尚未从线程栈确定卡点。代理本轮随后报用量限制，均停写；root继续工作，不更换账号。

只加一次性安装阶段记录（Host PreparePhysical、Group各source预检、native family、完整image capture、Prepare/Commit/Arm），不在每帧刷日志，不删校验/缓存过期结果。`stages-r1-result.json` Loader/Examples构建0（session99613/07328e），未重复不相关检查。诊断候选ZIP `RizToolbox-0.3.0-stages-preview-20261115.zip` 37,291,655B/SHA98041514f89289595c4368d7d3b39bc143df2afcbc2f02f84f18dde9c1354aca，逐成员读回0/9ebc96；已安装25文件匹配并请求Steam启动（97931/915911，install-stages-observation.json）。**它是诊断候选，不是稳定交付。**

新游戏40496（22:47:55），starting会话 `20261115T144830800Z-95901391bbef4dbb9688111013c14c74`，22:48:31已adopt pre-init。下一步先读 `Playback installation:` 最后阶段，再只处理对应确定启动阻塞；不等待旧已停止进程、不重做旧指纹错误/ControllerOver身份。完整Speed/Seek及B01–B19仍未完成，goal active。

## 2026-11-15：ControllerOver 实际 Hook 与播放组共存修复已安装（最新）

proof新会话32708实际进入Unity，自动控制订阅输出InitialSnapshot和逐操作真实错误，Ring窗口创建；未尝试改色。首差明确为 `GameAssembly.dll+0x3BEA60`，即 `ExactResultOriginModule` 的 `ControllerOver` 引用返回观察器，原64B前缀来自固定文件，实际起点为非零disp32的6B `FF25`。原始诊断与会话记录在 `work/game-seek-20261115/proof-game-observation.json`，完整原件在 `proof-session-before-broker/`。这已确定为自身普通broker接线缺口，不能笼统归因全部Hook。

**已实现**：新增 `ExactHookBroker.ReferenceCodeIdentity`，真实Dobby安装前保存永久Entry/engine/Target/MethodInfo/delegate与原64B；安装后验证实际跳转槽指向该真实托管thunk，并核对当前闭合6B MOV/PUSH原跳板及source+6续接。托管thunk按真实可执行allocation/保留delegate验证，不误作SEC_IMAGE。`Original`在后验校验前先发布，失败也不丢已返回的游戏原调用。Group持有固定私有成员快照；ImageBodies检查重叠，组合前与已pin文件原字节核对，后续持续复核登记、slot/patch/trampoline。未按RVA白名单豁免；未知前导或未来新Hook仍触发拒绝。Sol实施+另一Sol只读复核，均未发现确定问题；不声称已通用支持所有broker类型。

**本批限定检查**：`broker-r1-result.json`，Loader/Examples/Hooks.Tests三个Release构建0，既有Hooks **210/210** 自然0，原session33413终态340b98。它验证原转发、异常、重入/GC/所有者等既有逻辑；新物理Dobby正向仍看下一实际游戏，不将fake回归替代native证明。未重跑原controls56或SDK14。

**新包及安装**：`artifacts/delivery-20261115/RizToolbox-0.3.0-broker-preview-20261115.zip`，37,291,114B，SHA `1f64e580b0209ad9689081d544b1509cdf1c01fb23c51fe373100cf451c8b4ab`，35成员/25运行文件，逐成员读回0/d68065；receipt同目录。proof游戏正常Alt+F4退出，新包安装0/f08dc2，25文件已匹配（`install-broker-observation.json`），已用Steam应用2272590请求启动。SDK合同未改，仍配套controls SDK。**新包游戏结果待读**，当前还不能说Clock/Speed/Seek已恢复。三位Sol/max本批均停写；无编译在途。

**下一步**：读取broker新会话，先确认ControllerOver identity是否通过、原3BEA60首差是否消失；如果出现新的首差/错误，按其实际owner处理，不重复旧修复。若物理组就绪，再当前局/真实Pause验证Clock与实验Speed/Seek。完整SDK/B01–B19、全七根/HoldEpoch、时间精度和生产engine仍未完成，goal active。保持功能优先、短相关检查，README及旧ZIP保留；更多历史以其各自范围看待。

## 2026-11-15：controls 实际启动与下一处代码段阻塞（最新）

**实际进展**：controls 包已安装，25运行文件逐一匹配（`work/game-seek-20261115/install-controls-observation.json`）。原 cleanup 进程41068实际无响应，普通关闭超时后核实路径/Responding=false才结束；其日志保留。controls 新游戏42688（22:07:31）实际进入Unity，宿主会话 `20261115T140803677Z-52c5d06b97cd478381d6da28254950a4`。旧 lower-time wrapper 拒绝消失；新失败是22:08:38的 `NativePlaybackExecutableSeal.VerifyExecutableFile` 完整可执行段与固定文件/重定位不一致，physical host转Draining→Withdrawn，尚未WaitingForRun。外观/相机模块active只表示服务就绪，不代表写入或完整Speed/Seek通过。

现有 foundation 已明确组合64真实写区间，含RuntimeBridge/Unity/28 originals；不能直接把新失配归咎于tick。Coroutine绑定有真实原body核对，但并不通用覆盖所有hook。暂未放宽、豁免或把当前内存当基线；已加模块文件名、sectionRVA、首差RVA及最多16字节expected/actual诊断，下一会话据此确定来源。只有错误文本变化，未造镜像测试或额外开关。

**新增可用示例**：runtime proof 创建独立子scope，自动订阅玩法控制初始快照/语义变化，各操作完整错误分行；成功后不轮询GetState，不自动Acquire。停用插件显式释放scope，host亦持有。独立 `RingColorRuntimeProbe` 双重opt-in：`runtime-module-proof.Enabled` 与 `ring-color-proof.Enabled`；显示真实目标数量、Next预览key、Select准确canonical key、明确RGBA后Apply单环、Read实际颜色/holders、Release仅本层；场景/窗口/作用域结束释放。两项都是当前SDK真实消费，不扩核心协议。三位Sol/max本批已停写。

**本批验证与包**：`proof-r1-result.json` 仅Loader（六组件）/Examples两构建0，原session71457终态471256；不重复原controls的56项或旧SDK14。新 `artifacts/delivery-20261115/RizToolbox-0.3.0-runtime-proof-preview-20261115.zip`，37,288,236B，SHA `b3755cb5ed559935de73099145cc46ce1fe5b619a83b260a06d18ea94e3c4c0d`，35成员/25运行文件，逐成员读回0/4dc53c，receipt同目录。SDK合同未变，继续配套controls SDK包。

controls游戏已由正常Alt+F4关闭；新proof包安装实际0/55013e，25文件再次匹配（`install-proof-observation.json`），首次创建ring-color-proof.Enabled=true配置，未覆盖已有配置；没有自动颜色写入。已通过官方Steam `-applaunch 2272590` 请求启动；新进程32708（22:19:50）已出现，当前新会话诊断仍待读取。根工作目录均为 `work/game-seek-20261115/`，controls实际会话原件在 `controls-session-before-proof/`。旧ZIP和主README保留。

**下一离线/实际工作**：先读取proof新会话 `firstDiffRVA`，核对已有来源与已持有exact owner，只有证明实际自有patch才组合；未知外改继续拒绝。修复后只做必要构建/相关检查，再恢复当前正式Clock/Speed/Seek。不要再重复旧lower-time修复、原56项、已完成相机/N1/退休批或长归档。完整SDK/B01–B19、正式七根Seek与HoldEpoch、音频精度及生产engine仍未完成，goal active。桌面工具窗口定位不稳定，曾出现输入后窗口已关闭而工具报定位错误，后续先重新观察再行动；未操作账号认证。

## 2026-11-15：时间接口误拒、圆环颜色及控制订阅已交付（最新）

用户最新要求已改为 **Sol/max 子代理**；三位 Sol 接续外观、控制订阅及只读复核，Astra 未恢复运行。goal active，功能优先、性能/长矩阵后置，不做旧兼容。下方较早“本轮未安装/启动游戏”只指各自历史阶段。

**本轮功能**：

- 修复正式 stock lower-time preflight 的确定误拒：固定 `set_time` wrapper 的 +0x40 是三字节 `48 FF E0`，原实现按 `FF E0` 比较。固定完整80B SHA、文件来源、owner、RF/UW和安装顺序未放宽。既有静态证据正向与单字节篡改拒绝均通过；不是 detour 被误当原始代码。
- 新增 `GameAppearanceRoles.TrackRingColor`：实际 `LinesGroupInformation.set_color@3FB990` 同时修改 +0x88 缓存及活跃池内 SpriteRenderer。独立核对两处，基线一致才开放；部分写入只在归属有效、值仍属于本次写入时用同一 setter 恢复。保留 run/scene/数组/池/active/精确类型及176B指纹；与音符共享物理颜色通道，避免重复仲裁。缺失的 LineRenderer start/end getter 未猜造。
- 新增 `IGameplayControlService.Subscribe`、`IGameplayControlSubscription.InitialSnapshot` 和观察快照。注册与初始快照在主线程原子完成，post-Poll/PumpCompleted 后共享一次实际采样，仅通知语义变化。scope、调用方取消和回调故障均停止投递；无订阅时新观察入口不增加 Capture/Probe（既有身份轮询仍存在）。不自动取得/重获控制租约，不保证捕捉维护点之间每个瞬态。
- 前批已编译的按属性外观降级与 TimeWrite 实际 getter 恢复修复均包含在新包，不重复实施。Seek composite 只读复核未发现新的确定错误；没有据此签游戏正向。

**本轮新验证**：`work/game-seek-20261115/delivery-r1-result.json`，Loader（六托管组件）/Gameplay/Appearance/Module/Examples 五个串行 Release 构建0；clock-profile **11/11**、controls observation **3/3**、ring/降级相关 **10/10**、Module **32/32**，共56项。SDK新nupkg单独离线恢复/编译 Subscribe、TrackRingColor、数值 Seek 消费者，0警告/0错误，且未复制 SDK DLL。原 session44764 最终 **a05761 自然0**。未重跑旧整树矩阵/旧SeekClock/旧SDK14，不把受控检查提升为真实游戏结果。

**新交付**（逐成员读回0/**de1db6**，receipt同目录 `controls-preview-receipt.json`）：

- `artifacts/delivery-20261115/RizToolbox-0.3.0-controls-preview-20261115.zip`，37,243,212B，SHA `ae537709f16c3aee870c50771d09417cf55815fe588dbea1eb8a943d0fcd32fb`，35成员/25运行时文件。
- `artifacts/delivery-20261115/RizToolbox-SDK-1.2.0-controls-preview-20261115.zip`，18,422,293B，SHA `4014745bc6e54dcee4e159857c2cad824834030b7f218dd6f942e2f74f37ca6f`，45成员。独立 feed 预览版本 `1.2.0-preview.20261115.1`，宿主合同仍1.2.0；当前合同源码与新引用程序集同步，旧SDK14证据不作为本版验收。

**游戏范围**：此前已安装 cleanup-preview，25文件逐一匹配，日志出现实际玩法数据及 lower-time wrapper 拒绝；来源记录在 `work/game-seek-20261115/install-observation.json`。entry-r1 中间包也保留（SHA945501…2a59），但不是最终包。**本轮新 controls 包未安装/启动、未在真实游戏验证该校验修复**；电脑操作调用在派发前中止，没有发送游戏输入或操作账号。主README SHA仍 CFBAC4691D36EDC5DABA513AA62D5856AE61E29BF553D0DC2979832750847364；旧ZIP保留。

**下一步**：先在实际游戏正常退出后安装新 controls 包，确认不再出现原 lower-time wrapper 错误，再处理新出现的精确阻塞。正式七根 Adapter 全事务、真实音频定位精度/完整 HoldEpoch、Speed/Seek 游戏正向及其余 B01–B19/LOCAL engine 仍未完成；若前台实测后置，可继续离线核对其余明确功能缺口。不要重做本轮 profile、圆环/订阅、外观降级或旧相机/N1/退休批。新功能对应文档在 `docs/Game-appearance.md` 与新SDK `API-SURFACE.md`。当前构建已结束，代理本批停写。

## 2026-11-15：Seek 对象清理与恢复释放已交付（最新）

本轮补齐**本次已识别新建 child 的同步销毁逆**：实际首次 Snapshot 捕获完整 GameObject/Transform 子树，只有正式 Appear/Disappear 或 ActiveNotes/Visibility 的 Recognize 成功后接受来源；保护原 child、prefab、场景对象及后来移入的外来子树。先恢复原 plan/pool/List/backing/counters，再 `DestroyImmediate(Object,false)`，逐个实际对象死亡读回后才从该 owner 的 snapshot 缓存移除。部分死亡、未知调用、外来改动保留原责任且不重试，不以正常返回冒充销毁完成。

同次 Disappear 先填旧数组再扩容的中间状态也已补齐：模型保留旧 backing 最后的合法槽位，正式 access 两次实际读回后持有同来源私票；逆只消费该票，后改旧数组仍拒绝覆盖。CanvasPosition/Basis/LineEndpoint 新增实际 restored acknowledgement，GameState 在逆完成并读回后释放重复根；任何恢复失败或随后读回 false 都纯撤销确认，历史 effects 记录不清。三位 Astra 分工均已收尾，主 README 固定 SHA 未变。

**最终检查**：`work/seek-cleanup-20261115/cleanup-r4-result.json`，Loader（六托管组件）/Judgements/Examples 三构建0，Renderer四个相关入口全部0：新 child 实际恢复、foreign保护（含后来移入后代）、部分死亡拒假恢复、同次扩容旧 backing 成功及foreign分支。原 session27634 最终 **5b61d4 自然0**。Canvas18、Line38在 `cleanup-r3-result.json` 各1/1，原 session18603 最终 **1c5814 自然0**；两组件及其测试在之后 Renderer 改动中未变，未重复慢检查。这是分项范围，不称最终同树全矩阵。r1/r2首case失败原件保留：原异常被Fixture.Dispose遮蔽，修夹具保原异常后确认 fake class resolver 漏 `System.Array`；补正式已有Array元数据解析后通过，没有删产品保护。

**最新包**：[RizToolbox-0.3.0-seek-cleanup-preview-20261115.zip](../artifacts/delivery-20261115/RizToolbox-0.3.0-seek-cleanup-preview-20261115.zip)，37,236,147B，SHA `8d55b13458c35acc88f93aa41dade8a4946881c03429e82e78568d8fe66bf6d4`。35成员/25运行时文件，打包逐成员读回实际0/**f7fb69**；receipt同目录 `seek-cleanup-preview-receipt.json`。六组件/Examples取最终cleanup-r4；未改native与固定依赖继承已核SHA的上一Seek包。旧ZIP均保留，当前无构建/测试进程在途。

**边界与下一步**：同步清理仍属实验路径。Unity建议运行时使用延迟Destroy；这里为维持当前同步事务，在实际暂停/原线程/原来源仍有效时使用DestroyImmediate，并要求独立死亡读回。[官方限制](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.DestroyImmediate.html)已写入代码与包内说明，真实Unity回调阶段仍未验；不把本次fixture通过提升为game/production。正式七根Adapter整体Seek、真实音频时间精度、完整HoldEpoch正向及其余B01–B19仍待完成。下一步围绕这些实际路径补功能/验证具体缺口，不重复本次清理、旧17 checks、旧批次或长归档。goal active，本轮未安装/启动游戏、未操作账号。以下为历史阶段记录，下一功能不再是“新child逆尚未实现”。

## 2026-11-15：Seek 实际事务已合编并交付实验预览（优先看本段）

本轮继续功能优先，root + 3 个 Astra 并行，编译串行。ROOT 已接正式 Seek provider、SDK 秒数/谱面坐标入口、示例 Seek 输入按钮，以及真实 Hold epoch 停止 → 音频 SetTime → 进度/cache → 判定队列、音符可见性、Canvas/Line、Hold 几何与续接 → 暂停 Clock 的事务。实际 ActiveNotes/SetNotes 替代暂停时不工作的 Update；读取失败后保留真实 SetTime continuation，恢复由实际 writer 的 RestoreOriginal 执行，不再被恢复前的 getter 阻断。完整 Seek 游戏正向仍未执行，不能把编译或部件检查当作全游戏通过。

最终 **seek-r4**：Loader（六托管组件）、Gameplay、Judgements、Examples 四构建自然0；host3、SeekClock6、Hold inverse2、visibility模型1、Hold ownership/composition3、Canvas ownership1、Line ownership1，共17项相关短检查自然0。原 session68420 最终 `c3d53e`，记录 `work/seek-integration-20261115/seek-r4-result.json`。原生 time writer 的54项独立检查属于 native-time-r1（session59540，c76dea/3184f9），覆盖连续 pitch/time 与原值恢复；后续 HoldEpoch 未修改 native helper。r1旧12项不验后续图；r2缺 using 编译失败与r3 Canvas Ack缺共享scope失败保留原日志，分别已补命名空间及在正式 Operation 内执行同样读回/确认，未放宽guard。没有新全树/长矩阵。

**本轮新增**：实际 NoteJudgeControl HoldEpoch 的 StopAll/Factory/StartCoroutine、实际观察票和独立 iterator/count/input 读回；首位 epoch participant 与同票 holdOnJudgeCount 交接。失败时只有实际音频/进度/Clock和GameState都已恢复才允许重启原迭代器；暂停状态仅接受已知state2→3恢复。手动长条续接只使用实际同音符的方向/输入 ID，AutoPlay按原生策略；无输入的手动crossing明确拒绝。成功场景对象由游戏接管后释放独立根，失败先纯撤销确认再尝试恢复。native_clock_delivery、stock_lower_time_implementation、formal_provider_delivery三位Astra均已收尾并停写，无遗留构建进程；真实HoldEpoch/七根Adapter整体正向未执行。

**新交付**：[RizToolbox-0.3.0-seek-preview-20261115.zip](../artifacts/delivery-20261115/RizToolbox-0.3.0-seek-preview-20261115.zip)，37,223,524B，SHA `c32a8f9360034408a8fedfcbedf8d9ecfb8b6b853c7fa4a58128d00de134b8ba`。35成员/25运行时文件，打包和逐成员读回实际0/85e656。六组件和Examples来自最终seek-r4，两helper为实际native-time-r1；固定依赖/未改loader等沿用successor包。包内中文说明含SDK/面板操作及边界，receipt在`artifacts/delivery-20261115/seek-preview-receipt.json`。旧ZIP保留；新包没有真实游戏验收，不用旧包游戏结果验新Seek。

**下一功能**：优先补新 Unity child 在失败恢复时的精确销毁/退休逆；继续实际engine/SDK覆盖缺口。现有成功池接管可提交，失败逆不完整则如实报告未知并保留责任，不人为封死整个Seek。正式七根Adapter整体正向、真实音频时间精度、完整Seek游戏验收待后续集中验证；无需重做本轮完整接线或重复17短检查。B01–B19 与完整 SDK 未完成，goal active。本轮不安装/启动游戏、不操作账号，主README固定SHA未变。下面均为历史阶段记录，不恢复旧长矩阵、旧冻结/归档门槛或已退休批次。

## 2026-11-15：用户最新指令——功能优先、统一开发版本

**本轮后继来源已实现、构建并打包**：[RizToolbox-0.3.0-successor-preview-20261115.zip](../artifacts/delivery-20261115/RizToolbox-0.3.0-successor-preview-20261115.zip)，37,156,831B，SHA `ef480c98c28b1f6761c1df07f9ef5279ea63c21271a1604a9558eade06dd2791`。35成员/25运行时文件，打包逐成员读回0/8bbb42；旧包保留。本包六托管组件和Examples来自最终ROOT successor-r3，两播放native DLL来自`D:/riz-build-cache-next/production-fresh-clock-handoff-20261115/native-r2`。Loader/Gameplay/Examples编译及host3/SeekClock6全自然0（session26526，ce7b95；`work/root-fresh-delivery-20261115/successor-r3-result.json`）。新native-r2编译0/baaa73，Runtime/harness编译0/d908e3；实际初始化同局72/跨局72共144自然0（session79546，2f78b7，`work/production-fresh-clock-native-next-20261115/completed-check-r2.log`），最终roots0。这里是owned原生机制，不声称正式stock七根Adapter动态正向或真实游戏已验。

Fresh首次Clock已可转入原logical handoff；原Fresh票/根退休后沿同physical group交接到新epoch、watch、source-open、Clock。Adapter已接普通跨run自捕新七根、同run实际Start、post-Poll消费实际completed initializer、同票Busy重试及稳定formal host/provider换代；旧run租约/读数按真实关联失效，不重装physical组。修复Host先Pump导致已撤销旧tracker误走same-run完成、抢先停宿主的顺序问题，旧resource保留至sourceOpen。此前普通native63是native-r1限定结果（session34449/e4a131），未拿来覆盖后来的native-r2；r2联合0是中间版本，最终以r3为准。r1的host错误码空合并编译错误已修，失败记录保留。

同时新增`NativePlaybackClock.Seek.cs`：暂停定位的条件发布、完整基线恢复、与变速共享互斥票、DSP不倒退。它只补Seek时钟部件，未开放完整Seek。opt-in示例面板新增0.75x/1.0x/1.25x按钮，实际走SDK实验变速并显示拒绝/结果；不自动暂停或恢复，启用方法在新包README-PREVIEW.md。下方feature-preview是保留的首来源版本，不能用来验收本轮后继功能。

**下一功能工作**：完整Seek仍未对SDK开放。先把实际音频/进度定位与现有判定、Note/Hold/渲染重建参与者接成完整可恢复事务，消费本轮Seek Clock部件；不能仅打开Supported或用Retry冒充Seek。后继仍有明确边界：typed初始化恢复只接受实际watch/Before资源票覆盖的Start；全新Progress的透明早Start可走普通跨run，已blocked却没有实际初始化record的来源仍拒绝，同RunId观察恢复也不伪造新run。正式stock七根后继/真实游戏Speed及完整engine写覆盖、B01–B19仍待完成。真实游戏本轮未安装/启动，goal active；后续保持功能优先及短相关检查，不恢复长矩阵/逐批归档。

**本阶段已实际交付新的功能预览包**：[RizToolbox-0.3.0-feature-preview-20261115.zip](../artifacts/delivery-20261115/RizToolbox-0.3.0-feature-preview-20261115.zip)，40,039,130B，SHA `1ef1be8ff01c3635aad1ef3b332a1718f96258567b83d7a0b72fe68d3c0d4c93`。35成员/25运行时文件，实际打包及逐成员读回0/85fe3f；旧ZIP保留。六托管组件来自新ROOT构建，两播放native DLL来自新native-r2，默认Build/Install也已接入两helper。复用未改的loader/诊断JudgeContext/EngineNodeKernel/固定BepInEx依赖；不借旧包游戏结果验新包。

当前主目录已接通：physical播放组→共享observer提交后的真实run→原七根Fresh capture/adoption→真实Pause baseline→同票Clock prepare/arm/publish/withdraw→正式host与SDK读数；实验SetSpeed已接原writer reservation/continuation、独立读回、Clock提交、失败恢复及Resume恢复所选速率。采用SDK既有`Acquire(allowExperimental:true)`；实时typed authority通过才支持，没有人工恒false的formal Provider门，所有ProductionAdmission/FullSeek报告旗标仍不提升。初次ROOT1458整合52变更，一次solution编译0；后续Clock/native/host/rate真实合ROOT，最终Loader与Gameplay各0错误/0警告、3项host检查自然0（session87873，c2b9db→a68f24，原记录work/root-fresh-delivery-20261115/integrated-r2-result.json）。新native三关键路径arm44/cancel32/progress44通过；不是正式stock七根或真实游戏变速正向。安装器仅假目标的单个正常升级检查1/1通过（a3b9db），未安装或启动游戏。

**下一功能优先级**：实际completed-initializer消费分支已收齐，继续完整Seek和剩余B01–B19实际缺口。Fresh→logical普通转换与稳定host换代已实现，能力随实际已armed前驱及typed票据就绪，不再恒false，也不以重装物理组/重stage第一票实现。真实游戏新时钟/Speed与正式七根后继路径未验、完整engine写覆盖及生产准入未完成；goal保持active。不重做本阶段已合ROOT功能，不恢复旧r5长矩阵，不恢复逐小批ZIP证据流程。当前交付二进制预览，完整任务未完成。

本段覆盖下方历史的“等父r5完整验收才能合ROOT”以及逐小批归档要求。用户认为验证/归档严重拖慢交付，明确要求先完善功能、后集中检测；goal已恢复active，并要求子代理用Astra。现在最多root+3个Astra，只有一个编译任务，完整回归/长矩阵/性能测试/详细ZIP归档后置；每批保留编译和确有必要的最短关键路径检查。

- 旧main-r5 controller13424及renderer21676/50416已由root按新方向取消；原supervisor记录controller exit4294967295、resultPassed=false。这是主动取消的未完成测试，不能接受为PASS；旧源码快照与日志保留，不再等待72步骤，也不运行其成功接受链。
- 已完成的最新开发输入为D:/riz-build-cache-next/production-fresh-adoption-20261115/source的1458文件。typed adoption8产品文件已实现；31共享读取/legacy七根机制检查、同新源码Gameplay184/184通过。root执行后者原session45492、completion e7333c实际0；不是正式stock七根/Router动态正向。最终输入work/production-fresh-adoption-next-20261115/final-inputs.json，SHA65ae466505607b274bca909a7c8bf86d7796f572764ef4794274fd00a5cd2f8e。adoption ZIP发布工作已停止，现有work记录足够续接。
- Astra stock_lower_time_implementation：把已完成1458实际合入ROOT统一开发版本（简单备份/差异清单），一次solution编译；随后负责Adapter FreshCapture/Adoption/RootedSource/ClockPublication/Router Session的同票Clock消费。
- Astra native_clock_delivery：接原production-fresh-clock-native隔离树，完成native+Runtime FreshClock同票Prepare/Arm/Publish/Withdraw，复用实际ExecutionReceipt；完成后合入ROOT，不重造完整证据归档。
- Astra formal_provider_delivery：正式provider/host生命周期及GameplayModule/RizlineAdapter接线，稳定physical hook、真实run提交后capture、实际Pause baseline与失效排空；与前两项协商接口，避免编辑同文件。

下一里程碑是同一个开发版本里可实际走通的时钟和玩法服务；Speed/Seek与完整SDK尚未交付。不能用取消检测、接口可编译或fixture结果宣称生产准入/真实游戏已验证。README/用户配置/既有ZIP保留，性能与旧版本兼容后置。下面内容为此前精确记录，冻结/等待/代理策略冲突时以本段为准。

更新时间：2026-11-15 17:06（Asia/Shanghai）。工作目录 `C:/Users/71957/Desktop/riz_toolbox`。goal active；完整SDK、B01–B19、真实Speed/Seek与生产engine准入仍未完成。

**最新功能候选1174已完成限定离线验证**：补入正式stock LowerSetTime闭合成员，完整组现在为22 source+GetTime；修复Gameplay后端首次hook未就绪后永久缺失mapping的缓存，以及旧capture转换入口先选profile后Probe的顺序。新增cache只管理可用性，真实固定profile/原owner校验不放宽。完整1174源输入+Build1，相对1172仅5diff；新FreshSource原生协议在另区，不在本归档内。

新solution 0 errors/55既有warnings；Gameplay184/184、Runtime383/383、Module31/31，共598普通用例（focused9已重叠，不另计）。同次新Runtime/Sdk再执行5个owned LowerTime安装/来源/原调用/异常退出/unwind用例全自然0。r1漏4个固定测试fixture输入导致155/184/工具exit1（d66972）保留，r2只补原定子进程fixture输入、复用未变构建，8步骤及原session51754实际0/0d9b7a。不把owned或泛型cache正向提升成stock mapping/production正向。

[新报告](../docs/2026-11-15_clock-profile-readiness-report.md)；ZIP docs/validation/clock-profile-readiness-20261115/clock-profile-readiness-evidence.zip，26,114,640B，SHA `4d188b1a8362874cba9ec4f6fdd2867b56e43af092a3ca83e8a842f4d28dc382`；summary `bf986f42d38a9e0e895d31ef074ba0f5533bc4a75b1c51c058de4947cf2b0fde`。agent发布实际0/600922；root直接ZIP-only实际0/9fe75d。独立回执 work/clock-profile-readiness-next-20261115/root-archive-check.json；ZIP内independent=false保留发布当时状态，不改旧ZIP。root另实核完整1160/45项目零变化（原session5850完成实际0/ded709）。当前预览两包不重打、不据此声称包含1174。

**FreshSource独立限定验证已闭合**：完整1450输入、8产品diff；新原生opaque票使用实际GetTime/source owner、同域epoch和原三watch。owned四receiver根协议的lifetime51、release51、old-helper17，共119检查通过；真实23 Prepare/23 Commit，Busy/worker/重复claim/同地址新来源失效及原GetTime在途保根均有原进程证据。正式stock七根factory仅编译和独立源码审阅，未执行；119不冒充七根stock正向。Adapter W0/E0、新native五步骤自然0、harness W38/E0；前序SDK/loader/fence分类/项目路径失败原件保留。原build controller exit1与Adapter child0分别记录。

[FreshSource报告](../docs/2026-11-15_production-fresh-source-report.md)；ZIP docs/validation/production-fresh-source-20261115/production-fresh-source-evidence.zip，24,211,324B，SHA `441a60a5a663aa066dbf488ed7a48f5a8200a90738f7d72ca48df8e51ea260de`；summary `3494c26d472374f4895ba879a838ea8f486e0dc5483e46062449eae4e4635810`。publisher原session11681完成实际0/037a8c；root独立ZIP-only实际0/062b20。独立回执 work/production-fresh-source-next-20261115/root-archive-check.json；ZIP内独立检查false保持创建时历史。此1450不包含1174 clock增量，两个已归档候选不拼接验收；ROOT/现有预览不改。

**下一功能施工**：在新 D:/riz-build-cache-next/production-fresh-adoption-20261115/source 合并两份精确输入后，接exact typed capture到唯一Router/source/readback，借原七根和原watch owner；先登记真正producer排空责任，再允许取消最终free。不能把Binding/Origin数字投影当权限，不能再次Claim、重新stage或重新retain。Astra实施、Sol只读复核；本新adoption尚无构建/执行结论。随后才是实际Pause基线、同票Clock publisher及正式provider接线。未观察A→B→A、完整stock/Clock/production、Runtime null-provider、完整SDK/B01–B19/真实SpeedSeek仍未闭合。

**当前两件可交付预览已就绪**：[运行时包](../artifacts/delivery-20261115/RizToolbox-0.3.0-startup-preview-20261115.zip)供游戏安装；[SDK开发包](../artifacts/delivery-20261115/RizToolbox-SDK-1.2.0-preview-20261115.zip)供作者离线创建/编译插件；[简短交付说明](../artifacts/delivery-20261115/交付说明.md)。完整SDK仍未完成，主目录整合、正常通关及真实Speed/Seek不由两件预览推定。

新SDK ZIP18,484,558B/50成员，SHA `3dc92a00099da0c55276defe963826597d4c43acaa0ff89a97f066d44f212817`；精确同1172图SDK nupkg7932f577…cfa1、5模板文件、29合同源码/API索引、4实际固定.NET6依赖及global.json（.NET8/latestFeature）。ZIP实际解压到D:/riz-build-cache-next/sdk-startup-kit-20261115/smoke-r1，用新hive和新--packages缓存执行README四命令，全自然0、Release W0/E0、插件不带宿主DLL；controller32812原exec session11344完成实际0/f13ea9（launch fcf902）。新归档docs/validation/sdk-startup-kit-20261115，summary SHA `7a53348ba5e3ba309db81c4d19078db538c74a0395b6f985de70c0cb4301d121`，发布actual0/9907ea、root独立交付ZIP+证据checker0/e1b93f。[报告](../docs/2026-11-15_sdk-startup-kit-report.md)。当前接受sdk-preview-validation.json SHA `d2c9be57251676b243542ffcd772505acab452e169bd20cd62ced5d311d0b27d`；初receipt里的smoke=false保留创建时历史，旧ZIP不改。此SDK开发交付已闭合，不重跑旧939或当前四命令。

**最新控制准备与本次实测**：新work/foundation-startup-main-integration-next-20261115的13项控制已全部补齐/AST核对，精确1172及同图SDK14只读preflight通过；原session50407完成实际0/4abdb7，controls-preparation.json SHA `55a92d9710c1615f7f2c4579571d547b5311b3849e8fc83e2e2b2b12767b0bcc`。后续96自然步骤包含27普通2534、Hold62、producer3、focused8；这是准备，不是main功能PASS。只读gate原session60823完成003026，工具exit1，明确admitted=false/Parent main-r5 original wait receipt is absent，正确拒绝提前应用；不可把拒绝当验证成功。preparation-observations.json SHA `90a2bacce077c3f9729011bf464eb5f11ea301b7fe7ab54e491e4cb330345bc8`，root观察476b25实际0。ROOT完整1160/45项目文件集合及Build未变。

父r5最新16:43实际状态（查询1a0dea）：26已记录中22自然0，renderer-6/8128与renderer-10/21676仍运行；原supervisor35864/controller13424未见原wait终态，不推断整体通过。旧renderer-7等已自然0。保持ROOT1160/Build冻结，不能重复启动或缩减完整101/6/101+inverse。README固定SHA及两ZIP存在由351232核对。

**用户接手已结束，当前候选失败结算闭环已实测通过**：用户回复“跑完了”，root实际看见Greatness Earth EASY4/FREE RESULT页：0.0000%、Hit0/479、Bad0、Miss479、Score0/1011400。runtime-r3/result-page capture实际0/14db3a，observation SHA `f182b9d4a78bc297ef59614dbe6c4330a12d6c280efbeb1b4ac0855c4935090f`。原ControllerOver return accepted=True，RunStarted/RunEnded/RunFinished关联同RunId `9674fa3a160c412b969673391aa14dcb`；原生结果Crashed=true/Skipped=false/EndReason=Failed，SDK字段与界面一致。**这是Failed失败结算，不是Completed正常通关，也不是进程崩溃**。本会话无框架Error，正常通关/精确Back/全12判定来源仍未验收。

新证据docs/validation/game-result-flow-20261115：22文件，summary SHA `2c034063df1fc3cc29ea2892efc3237cc9931fe4aa88d31433a9125d07888635`；发布实际0/488db4、root独立archive-only0/2ffbc9，[报告](../docs/2026-11-15_game-result-flow-report.md)。5份精确源码只用于核对当前合同，不冒充完整重建树；未复制游戏二进制、配置或截图payload。ROOT托管仍未应用，旧预览ZIP不变。

同会话游戏25008（13:16:06创建，diagnostics20261115T051638374Z-892aaffc8645488f9462136b3308845c）实际设置页read/override两模块active，29active/1unsupported；settings-page observation SHA `dc3f19cfa1db2b7449ceeacb1aea572c86fffb2015d7629059992c207d395c1e`，actual0/55b652。23安装文件及GameAssembly一致。流速7.3/偏移-22只读，未测试SDK写入、仲裁/恢复。14:38曾仍活（983d27），15:14查询480d31已确认Rizline进程0个；root没有发送退出输入，退出方式/退出码未知，不冒称正常退出验收；若需再操作先重新观察窗口，不能把用户近期鼠标输入当无人控制。源码判据见work/game-result-flow-next-20261115/source-review.md。

## 当前能交付的内容

**已生成可安装的启动修复预览包**：[RizToolbox-0.3.0-startup-preview-20261115.zip](../artifacts/delivery-20261115/RizToolbox-0.3.0-startup-preview-20261115.zip)，36,997,650B，SHA `76a3ee353a73c0436547b6fbc13e90a4e27ad3170abc1cd7802ad5842fb8801e`。31成员包含精确已安装的23个owned文件、固定本地BepInEx依赖、安装脚本及范围说明；重新打开ZIP逐成员核对已通过。完整解压后用PowerShell7运行`pwsh -NoProfile -File .\scripts\Install.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline'`。此包是SDK/runtime预览，不是完整SDK或B01–B19正式发布；旧ZIP未覆盖。receipt在artifacts/delivery-20261115/startup-preview-receipt.json。

**此前runtime-r2已接受的有限流程**：D:/riz-build-cache-next/runtime-tick-admission-20261115/install-r3，installer65045实际0/d3b2f4，23文件/ledger逐字节一致，用户配置及GameAssembly未变。游戏16960于2026-11-15 12:42:52.238837+08启动，nativeReady49/result1、原il2cpp init1、CoreCLR、Unitytick、host初始化、新status都成立；新会话`20261115T044326943Z-6b60851dd4ec4723ac3594c97063f689`，当前30模块为28 active/1 waiting/1 unsupported，相机模块active。

真实观察覆盖标题及工具面板、主菜单、选曲、Greatness Earth EASY的FREE开局、失焦自动暂停、鼠标恢复/倒计时、暂停页退出至选曲再回主菜单。日志确有RunStarted/Paused/Resumed/SceneChanged/ObservationChanged，**没有RunEnded**；不能把ObservationLost当精确Back取消。游戏已Alt+F4正常退出（bf8735），独立进程查询确认不存在。runtime-r2/observation.json SHA `07780b24cede6f4a6ec53dabc3f9969a76cb4cd6ea305e77ce74b600ea4551d7`，root记录实际0/bf4706。

runtime-r2当时未完成结算；本次runtime-r3的Failed结算已单独接受，Completed正常通关、手动暂停入口或时序准确性仍未验证；FREE没有精确NormalMode相机对象；game-appearance缺LineRenderer.get_startColor元数据而Unsupported；settings override等待实际设置页；示例面板旧0.2文字不能代替实际SDK1.2.0/宿主身份。实际游戏使用诊断helper（SHA e92f5a095c9ed6341fc7adb53d85ca99c77b0c2534560a0037fc79651c1d59b4），ROOT生产helper虽已新构建但未游戏实测。

## 本轮启动修复与证据

1. **Steam接力继承内部禁用标记**：ROOT已应用scripts/Build-Doorstop.ps1、native/RizToolbox.Doorstop/riz_process_environment.h及Apply-ProcessEnvironment.ps1，改为PID+创建时间私有hook/CLR标记，保留手动DOORSTOP_DISABLE。ROOT fresh native build0/bf1141、header8及实际DLL r2六项0/390b0e。独立ZIP107索引/108成员、593904B、SHA `82fb4cc29ae5a733c68474c88e0d0ac5850cfc91991f2889b7eb4a8c1f36337b`；root archive-only0/690ed8。[报告](../docs/2026-11-15_doorstop-process-state-report.md)。诊断覆盖层未合ROOT。
2. **无关runtime invoke反复执行整套proof**：隔离Runtime四文件先用固定内部consumer筛掉无关/重入回调，每个相关tick仍完整VerifyReady及二次native identity检查。manifest `c1a0bdf18d9f2d6d0bedabfa887916ae729e6df6475d75f986c8924f7c660e31`；无公共跳过/cache/grace。ROOT托管尚未应用。
3. **相机interval0令宿主注册失败**：隔离v2三文件采用最小正NORMAL `2.2250738585072014e-308`/bits0010000000000000，每推进host tick执行一次；正常>0合同不变。真实30内置模块注册与perframe检查；旧0负控29/31准确捕获异常。v1 epsilon存在subnormal风险，仅旧结果留档，不能代验v2。实际游戏FP模式未观测/改动。ROOT托管尚未应用。
4. **代码/metadata分开VirtualAlloc受地址布局影响**：ROOT FirstInstallPlaybackAttestation.inl已改为一次8192空hint申请，4096页大小守卫，code RX/metadata R，全部证明/OS unwind不放宽。production/diagnostic精确fixture各801/802 checks、28 originals、每轮155 lookup/unwind边界，实际0/2381f6。ROOT应用0/60e762；ROOT fresh85导出构建0/f2605c，helper SHA `a6b35fc6dead8977df86b25e00ea6975bf88f68fe05a401e3fd86e51d742fb97`。此前Windows.h失败及原application里尚未build的false保留历史，不改旧记录。

**最终启动组合1172的新离线验证已通过，不重复运行**：D:/riz-build-cache-next/runtime-tick-admission-20261115/run-r2，supervisor34372/controller43772原Popen.wait0，6自然步骤全0：六Release W0/E0、solution0E/55既有warnings、focused8、Runtime383/Module31/Camera55共469普通用例。result SHA `fbb63a9cd13d61b7df8a1625afc7ebaa54b849f568c24168e950ce24f2b39538`，终态观察0/0bdd7b。旧r1另外Hosting20/Adapter10/Hooks210/Gameplay175等884普通用例属于r1精确图，不代验r2未执行部分。完整冻结1172均未变，ROOT1160/Build未变。

启动离线证据：[报告](../docs/2026-11-15_game-startup-admission-report.md)；docs/validation/game-startup-admission-20261115/game-startup-admission-evidence-r1.zip，11,147,922B、1325成员，SHA `646135248cc51443bcf6d3b0c9c05f9219c2205057192b45f4ec25681494cc6f`，root archive-only0/11666b。该冻结ZIP创建在真实成功前，保留旧安全拒绝；真实成功另见下面原生证据，不能改写旧ZIP。

原生连续分配及真实有限启动证据：[报告](../docs/2026-11-15_playback-attestation-allocation-report.md)；docs/validation/playback-attestation-allocation-20261115/playback-attestation-allocation-evidence.zip，1,418,776B、80索引成员，SHA `408aa09910ce3346a149a049920820f0f4a7013737fc06a507f594367a17f4dd`；root独立ZIP-only 335 checks实际0/437582。root-native-closure-acceptance.json绑定ROOT实际新构建+两种精确fixture；provenance-correction.json仅更正两条工具chunk笔误为f2605c/6264a8，旧ZIP和JSON/log哈希不变。生产helper未借用诊断helper实测结果。

## 当前1172独立SDK14

**同源码的新独立双插件14/14已完成、归档、root直接复核**，不再重跑旧1171/939分发。D:/riz-build-cache-next/sdk-startup-candidate-20261115/run-r1，supervisor34952/controller15816原wait于05:02:28UTC自然0；正式host W0/E0，两个自然步骤0，result SHA `4813c991852dd9cf54d75777cbe693c769df980b867bca14a0e643efc8380a95`。完整1172已含Distribution两输入，不计1174。新模板/独立cache/离线固定feed，五个已定dotnet开关仅子进程设置。

docs/validation/sdk-startup-candidate-20261115/：1266文件，summary SHA `854f8c21176bc05d5b8f554b8e248af3e802d4af4a1e3942574f221e9818ad25`；新SDK包SHA `7932f5773d4813a18ed5b5c72a0774dc042cc199fa48ebf8b07f180d0a25cfa1`。发布实际0/7c1692；agent checker0/c3b626；root独立archive-only原session91321完成0/f4c15f。限定acceptance SHA `0f5cb0b91807fa76f61672ad0c9460812fa8fc8a66e0dc8de00a9952d71baef3`；[报告](../docs/2026-11-15_sdk-startup-candidate-report.md)。四net6依赖只归档实核hash/size，没有字节，不能宣称独立离线重建。根两次独立checker回执：work/runtime-tick-admission-next-20261115/root-archive-checks.json。

## 下一离线工作

0. 两件1172预览与SDK四命令交付已闭合；新1174限定离线候选另已归档/root独立检查。继续顶部typed fresh adoption与后续Clock/provider接线；不得用只读审计或source票替代Clock/production准入。整体goal active，不重复已闭合功能。
1. **续原父main-r5，不能重启或改其冻结图**。最新22自然0的有限进度见顶部；原wait终态仍未出现。原r4重启中断整体未知，原r3 OOM失败，不运行它们的成功链。
2. **只有原r5 wait0且完整72自然0后签独立接受**：依work/line-producer-control-next-20261114/main-r5-acceptance-controls.json（SHA `a17b247777910e3dcc9c42ddaa19bd59b81f6b23140b24dfe381edd0f8337adc`）顺序record_main_r5_exit→publish_evidence_r5→check_main_r5_archive→finalize_acceptance_r5，传实际工具chunk。保留原101/6/101+inverse，不缩减检查，现未执行接受链。
3. **使用新1172/r5控制，不使用旧r4/1171脚本**：work/foundation-startup-main-integration-next-20261115已准备并冻结13项控制，SHA/实际工具见顶部；28文件=27C#（15修改、12新增）+Build.ps1。父r5真实接受后顺序prepare_main_payload→apply_main_payload→supervise_main；96自然0及原wait0后record_main_exit（实际launch chunk）→publish_main_evidence→归档内check_main_archive→finalize_main_acceptance（实际publisher/check chunk）。完整1172/45项目闭包、27普通2534/62Hold/producer3/tick8；当前没有payload、apply或fresh ROOT验证。独立native四项保持原字节。
4. **不要直接运行旧prepare_main_payload_r2.py或旧Hold939链**：它们硬绑已中断r4或已失败r3、旧1171/21文件，不能冒充新1172。不能用候选95、SDK14、启动r2或实际启动代替fresh ROOT验证；原revision3仅旧控制准备，不是当前main PASS。
5. 待真实正常结算/精确Back、设置页、NormalMode相机、真实判定/Speed/Seek、LOCAL engine/source继续分别补正向证据/功能。没有功能原因时不反复测试已闭合启动部分。

## 原候选与约束

1171组合95自然0及27普通2524/2524、Hold62、producer11/12/8已接受：docs/validation/foundation-current-integration-20261114，summary `e7a1540f75185a9714f935bec18520edfd8cada522df017b9f17d5efabf87a61`、acceptance `f21745fea46a94d30210dd0b1eb9aaeec0f18a6c94a4e7ccd80916287d6baf0e`。旧1171 SDK14为docs/validation/sdk-current-integration-20261114，不能代验1172。旧main-r3有真实OOM1，r4中断未知；历史部分PASS不合成为成功。

相机X正式接入、N1、rate provider/float桥/Play自动handoff及退休冻结批已完成的范围不重做。FullSeek/HoldIdle/Startup/FullCoverage等生产授权标志保持原合同关闭；这里的“实际host启动成功”不等于这些权限成立。B01–B19和N1–N4按[NEXT](foundation-expansion/NEXT-FUNCTIONAL-GAPS.md)及各批实际证据判断，性能后置，不做旧兼容。

用户“有必要的话可以动游戏”授权必要安装/启动/正常UI实测，无须重复确认；认证用户手动，过期/撤销B账号不用。新GameAssembly/PE内容分析、永久env、Git提交/push、覆盖旧ZIP仍不做；本轮仅核GameAssembly SHA `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。README保持SHA `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`。dotnet只用子进程五项已定开关，compiler SDK env也仅子进程；后台CLI隐藏窗口，避免游戏失焦。代理可用Astra/ultra（用户最新授权）；最多主线程加3子代理，编译串行并保留系统余量。

本次替换前两页原字节已完整保存在[20261115历史快照](history/20261115-before-startup-runtime-refresh/)，校验见preservation.json。更早长历史仍在history/20261114-2250-before-current-refresh。goal保持active，不以预览包或限定接受宣称全任务完成。
