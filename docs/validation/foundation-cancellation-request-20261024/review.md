# Cancellation request 与 Operation Dobby：有限复核

2026-10-24。root已观察定向69797与完整45961真实terminal exit0，本agent实际读取两runner、完整summary/原始JSON及归档proof：25套1575/1575，Audio619/Hooks185/Runtime160；真实Dobby1165检查/69观察调用+1未发布调用，以及实际native→CLR mutation/cancellation ledger联合5进程544检查均重跑通过。534声明源码和22暂存包已逐项现场核hash并打开两个ZIP核全部条目，旧安装18项和原游戏3项未变。root是唯一builder；本agent没有构建、运行fixture、安装、启动游戏或操作UI，只编辑本review。

本agent实现了独立OperationDobby fixture和Stop离线研究，对这部分是作者自查，不能称第三方独立审计。对root/其他代理的Stop resolver、observer、ledger、coordinator及三tracker/backend退休接线进行了有限交叉审查；已报gate位置缺口的Ready发布和退休边界已完成源码复核，并有受控launch6/retirement8真实通过日志。其余已读路径未发现新的确定阻塞缺陷。以下hash对应本批冻结输入；通过计数不表示游戏全覆盖。

## Evidence

source_type=file/command；observed_at=2026-10-24；source_ref同时为artifact_path。flavor=null，普通工程复核；不包含新增发布或取消完成授权。

| E-id | source_ref / artifact_path | SHA-256 / 状态 | 实际观察 |
|---|---|---|---|
| E-stop | [stop-abi.json](stop-abi.json)、[research.md](research.md) | JSON 1bfe8aecc8149f3cedadab6c8ba159e0fd1fc4485d8abea36dc07b322bd05fbe | 实际固定DLL六完整RF解码、签名/RVA、icall缓存与尾跳；未执行gamecode |
| E-observer | [NativeCoroutineCancellationObserver.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeCoroutineCancellationObserver.cs) | b51bb96598ac9a64be1cd2745b15cc132e98dcbe83b582793ea2fe69b5ad91ed | 六绑定、静态永久owner、部分安装CaptureException；owned receiver在provider前发布atomic pending |
| E-resolver | [NativeRuntime.CoroutineStopHooks.cs](../../../src/RizToolbox.Runtime/NativeRuntime.CoroutineStopHooks.cs) | fbc87fafac2e05351b006f25db2901d515a14450721c6914bad2dc0bb540c423 | 实际declaringClass/void/reference/params/RVA/MethodInfo布局/executable核验；本轮未在真实metadata执行 |
| E-profile | [CoroutineStopProfile.cs](../../../src/RizToolbox.Runtime/Hooks/CoroutineStopProfile.cs) | 327b023a0b7e59228882b5848a379db849d3633237a7549d019468c24f154502 | 六精确声明，5×kind1及1×kind0，与磁盘机器码一致 |
| E-ledger | [NativeCoroutineCancellationLedger.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeCoroutineCancellationLedger.cs) | 44bab541227651e4d1aac299d22e5684eaebeab6053943701bdc67e575c2f3a2 | 先记录owned scalar责任，再只读provider；真实返回/异常与取消完成分开 |
| E-coordinator | [NativeLocalRunLaunchCoordinator.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs) | 12495e90f1ca37069f766ebaa0a6c23c9ab6c6dad1b94d0a28158ff5bdb48840 | 实际shared pump在activation/getter/actualgraph后反复gate，再Prepare→Commit |
| E-state | [LocalRunLaunchCommitState.cs](../../../src/RizToolbox.Adapter/AudioContent/LocalRunLaunchCommitState.cs) | 8596cc94c9580f6d6c8f5b184a82e0ad4aca911f10b0a75202a40fbf3ca0409d | 独立atomic pending；usableReady/activation/Prepare/Commit及发布后复核 |
| E-launch-tests | [NativeCoroutineCancellationLaunchTests.cs](../../../tests/RizToolbox.AudioContent.Tests/NativeCoroutineCancellationLaunchTests.cs) | 361789eff6cc033251655972e4c8d3d8d6de1facf4d6b744ec2ef556d6b247eb | 六个actual shared pump/state受控注入case，完整日志实际PASS |
| E-dobby-fixture | [OperationDobbyFixture.cpp](../../../native/RizToolbox.JudgeContext/fixture/OperationDobbyFixture.cpp) | 484d7c4236239db6f0996fe88126056d1fc50fcccc6aa514e5d1df8e2862f7c7 | 自有typed目标真实Prepare/Configure/Commit及独立caller；定向与完整均执行通过 |
| E-dobby-runner | [Build-OperationDobbyFixture.ps1](../../../scripts/Build-OperationDobbyFixture.ps1)、[Test-OperationDobby.ps1](../../../scripts/Test-OperationDobby.ps1) | Parser.ParseFile零错误；未由本agent执行build/test | pin Dobby hash，有限子进程timeout，验证有限schema及磁盘输入不变 |
| E-full-run | [build-run.json](build-run.json) | 3e2938394458a5b6127528171ca18a64e6dd218d8a6f6988397344a9e19d04a7 | 45961 exit0，2026-10-24 14:39:46 UTC；target69797 exit0、未安装/游戏 |
| E-summary | [build-summary.json](build-summary.json) | 8298f4f99e3fdd1263fb36e280cb5bac5c62a3024eb9bfa14ed29b1bf711a8e0 | 25套1575/1575；core8+launch6+retirement8包含在Audio619 |
| E-dobby-result | [operation-dobby-evidence.json](operation-dobby-evidence.json) | f510f2b42e14e3daea0339fe81a3f50995fc631b385d33e6324b7af6a23163a3 | 完整构建1165检查，69观察+1未发布实际原调用 |
| E-ledger-joint | [operation-ledger-evidence.json](operation-ledger-evidence.json) | 29974516f274bd01cb9dcc97d412a9bac1d9aee00421fa881546356d93e38ef7 | 五独立CLR进程544检查；真实native帧/生产broker/实际ledger，图/根/provider/初始idle受控 |

E-stop实际GameAssembly SHA-256为853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79；dump/script仅命名/签名辅助，不是活体metadata。PE survey含228 imports/10 DLL及386 exports。完整命令、RF范围和ABI边界见research。

本agent重读的退休六源及冻结hash如下；[八组退休测试](../../../tests/RizToolbox.AudioContent.Tests/NativeCoroutineCancellationRetirementTests.cs)为生产tracker配受控backend/atomic authority，不能冒称真实GC释放故障注入：

| source_ref / artifact_path | SHA-256 |
|---|---|
| [Initialization tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalInitializationCoroutineTracker.cs) | e7ebb1e0be49ed15444d1bba6825a42d25cc48676e64cedb96f2e152ed944211 |
| [Initialization backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalInitializationCoroutineBackend.cs) | dd00feffef02deda7b86a21930594c3e8df768d8024436b2da558768c7f729b9 |
| [Terminal tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineTracker.cs) | cdef1c626546efb4da5c7881f486a05185291695d2fa6a7cc1f42148cd6702cf |
| [Terminal backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineBackend.cs) | f8afabcf79e439ae62f33b9832f349692f72c18a08c12d21e47502c5eeac978c |
| [Cutout tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineTracker.cs) | 93dfbe895f47655eff4e2e1559dcb688a5beca96c5adbf22b165f7b130018fb0 |
| [Cutout backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineBackend.cs) | c6aec52c510b2c7a30ea903b91be752cca4ea7b5d9efb6becc40bbf5e686bebf |

production三个authority实际读launch.CoroutineCancellationPending；接口默认false只维持旧受控实现兼容。Pending不直接改写自然Terminal事实，也不等同Unknown：入口确认/释放先返回false或0；活动/enum/source读取期间新Pending阻止后续操作，记录撤回或已开始detach/free后新Pending会保Unknown、不完成slot回收/Closed，未尝试的其余根继续保留，已尝试free不重试。backend在逐根free前和free后再次核scalar/idle；同步首根free期间产生Pending，不能撤销已发生的一次free，但停止后续根并保留不确定责任。worker更新只涉及atomic scalar，正常Start/MoveNext/原Exit仍继续配对。没有据这些检查新增native admission gate或保证任意线程在任何指令间的全局封口。

## Findings

| F-id / status | evidence_ids / location | 结论及边界 |
|---|---|---|
| F-gate，已修 / validated受控 | E-coordinator/E-state/E-launch-tests/E-summary；下节退休六源 | 初版首gate后的同步Stop窗口已收口：owned先atomic pending，shared pump逐核，state Prepare/Commit/usableReady与发布后再核；三tracker/backend在确认、撤回、逐根释放边界拒pending。launch6/retirement8完整PASS；不声称游戏并发全证 |
| F-partial，source-reviewed | E-observer / Capture；broker BindNativeOperationObservation | owner先进入静态永久集合，成功bindings retained；异常以CaptureException交回coordinator，失败transport由broker永久Entry持有。_ready=false/Unknown不授予回收，也未重试或物理detach。尚无本批observer部分安装动态注入结果 |
| F-thread，source-reviewed | E-observer / ObserveFault；E-ledger / ObserveFault | worker fault只核immutable receiver表和scalar，写ledger Unknown/launch atomic；不调用provider、对象Class/Target、Root或native读取 |
| F-identity，source-reviewed | E-ledger / Capture、ReadCurrent | foreign receiver返回null且不读provider；owned nullarg仍先建请求责任，保原异常；handle只有StartReturned+HandleConfirmed可匹配；StopNamed只保存pointer并MatchUnproven。闭合初始化诊断行先按Published/Retiring过滤，再验证Known/current代次 |
| F-pending，source-reviewed | E-observer / RequiresRetention；E-ledger / Read | 请求完成、Outstanding0、CanConfirmRequestIdle=true均不能消除HasObservedRequests/Pending；EngineCancelled恒false。只能证明请求边界，不证明引擎停止、根退休或自然结束 |
| F-abi，source-reviewed | E-stop/E-profile/E-resolver | 六声明及kind与固定RF一致；public两种内联internal icall故须分别观察。actual hidden MethodInfo值、真实metadata解析和六新入口生产首发仍未动态验证 |
| F-dobby，validated自有native | E-dobby-fixture/E-dobby-runner/E-dobby-result | 自有目标、实际trampoline和精确source CALL返回PC；完整1165检查，三成功发布及有限拒绝已执行。与ledger fixture是两条独立证据，不能拼成固定游戏首发或真实Dobby→CLR ledger整链 |
| F-ledger-joint，validated受控依赖 | E-ledger-joint | 真native Operation帧进入实际mutation ledger，kind0/1兼入实际cancellation ledger，真实native Exit驱动完成/Dispose；5模式544检查。metadata/图/根/provider/initial idle受控、prepared engine不patch；未执行生产object backend或真实Stop安装 |

已报F-gate的Ready与退休部分已修，有限复核没有新增确定阻塞缺陷。此结论不是全仓库、全部异常/竞态或完整游戏验收审计。ledger只保managed scalar identity：generation/population变化会Unknown，abnormal时先记原生完成事实再不读After provider，精确context释放仍继续；这一合同不等同IL2CPP GC/root实证。

## Path

P-observe，path_type=callflow：同launch/receiver的三个tracker provider→六exact metadata声明全部先解析→逐项绑定永久Operation Entry→native Enter64→owned receiver先record→scalar Before/匹配→原函数真实一次→Exit返回/异常事实→scalar After一致性→exact context Dispose。失败仍留静态owner/已得bindings/transport责任，worker不进入native对象读取。对应E-observer/E-resolver/E-ledger，F-partial/F-thread/F-identity。

P-gate，path_type=callflow：owned receiver在provider前置atomic pending→shared pump的CancellationClear在每个activation/getter/actualgraph核验后双读pending并可读observer activity→PrepareCommit/CommitPrepared自身拒pending→Registry.Commit与共享ready发布后再次核pending/terminal/fault→IsCommittedReady持续核pending。Prepare与Commit之间不增加native gate，避免已分配preparedReady因临时inFlight被搁置后错误重试。自然回收以同一state atomic pending经production authority传入三tracker/backend，逐边界阻止后续释放；完整受控case已通过。

P-dobby，path_type=callflow：只读文件pin/BCrypt核固定Dobby→自有三typed targets Prepare真实trampoline且原bytes不变→native immutable Configure→Commit真实源bytes改变→独立RX caller CALL→frame.caller精确returnPC→typed original/异常finally→Exit计数。独立prepared但未发布target保留slot/trampoline不Commit；重复Commit拒绝后核字节不变。后两种拒绝不模拟任意写一半后的故障恢复。该路径已取得下节preliminary动态结果。

## 动态结果与未完成部分

本批定向69797与完整45961均真实exit0，完整runner记录2026-10-24 14:39:46 UTC。[targeted.log](targeted.log) SHA-256 552b8c8b89938149fa4e70810758fee37a7499f9201b343382dae3e069868d9d；[完整日志](build.log)为95498a9204d2e45835b4372986ce6c678b4fa0b2ec77b81758290996dc6d0feb。完整25套1575/1575，Runtime160/Hooks185/Audio619/Visual71/UIInput79；本批core8+launch6+retirement8实际包含在619并全部PASS。summary含94条去重原始diagnostic文本，含上游与ANSI变体，不表示94新问题或零warning。没有本批失败日志需要被后续pass覆盖。

root已观察独立native编译54091 exit0，并执行[preliminary fixture-result.json](D:/RizToolbox-build-cache/cancellation-request-targeted-20261024/operation-dobby-preliminary/fixture-result.json)与[runner manifest](D:/RizToolbox-build-cache/cancellation-request-targeted-20261024/operation-dobby-preliminary/fixture-manifest.json)，本agent实际读回：passed=true、1165checks/failures0；69 detoured original、Entered/Exited/uniqueSequence均69，另未发布原target一次，总实际70；原异常12、observer异常24、nativeworker12、nested6、logicaldetach3、legitimatezero3。Prepare4、Commit3、重复Commit拒绝3、Configure拒绝target1/retainedUnpublished1；prepare(null)拒绝。helper/fixture/Dobby三输入现场重算均与runner before/after hash一致。

该result SHA-256为5e365ed0037eb7e770fabef8705a46f79fdd11b26bd6bc6a7b30c11f85a2a98f；manifest为16885bb03d457eeb4fa18079127279602cdbe6dedf6ebae10a620f0bbb78efcd。编译manifest的sourcehash484d7c...与E-dobby-fixture一致；预验证实际使用上一冻结helper5a514928e27cd5db2edf53a1ae644db23fb3bd174b74284829ed2dfa66f87c03，不等于本批full采用了最终所有输入。JSON明确game/firstInstallSafety/concurrentFirstPublication/partialPublicationFailure/exceptionFlagsTransparency/nativeAdmissionGate均未证，installed=false。

以上preliminary保留为早期原件；本批完整[operation-dobby-evidence.json](operation-dobby-evidence.json)以新编helper2f776d6da4798fe9b824e4c1aabdc2ff979dfd56b99abbc0b731c6aa3f9ecb9e再次实际通过1165检查/69+1调用，不能只引用旧helper预验证冒充完整运行。

实际native→生产bridge/broker→实际mutation ledger联合为5独立CLR进程、544检查、15原函数/15 mutation records、15complete/15Dispose；normal107、original-SEH110、original-C++110、before-fault107、after-fault110。normal有3个ConfirmedReturned，其余模式保真实返回/异常与图确认区别。kind0/1另对实际cancellation ledger累计10请求/10complete/10Dispose；Pending始终保留且EngineCancelled=false，before/after受控fault时仍维护请求责任。每个case读到actual native source frame、真实回调与Exit，原异常在native catch内结束不跨P/Invoke。但初始idle、identity provider、metadata/graph/roots由受控backend提供，actualTrackerProviderUsed=false、productionObjectBackendExecuted=false、productionStopBindingVerified=false，prepared engine不做Dobby发布。**Dobby fixture与ledger fixture是两条独立实证，不能拼成真实Dobby安装后生产Stop/GC整链。**

本agent独立核534当前输入与构建前/source-manifest逐项一致，实际打开[source-inputs.zip](source-inputs.zip)534条目与[staged-package.zip](staged-package.zip)22条目重算hash全部匹配，再核当前暂存22文件。[差分](changes-from-listener-mutation.json)为+12/~11/-0；[source proof](source-archive-check.json)、[package proof](package-archive-check.json)与实际读回一致：

| artifact | SHA-256 |
|---|---|
| [source-manifest.json](source-manifest.json) | f6ffbdcc6289648d55422496852b872c23c03078c3b5b264c2b076735b68c8ad |
| [source-inputs.zip](source-inputs.zip) | f7cf1791598aee855953ad5e33c662312fe96117d935c108430a9a2a9201a3d3 |
| [staged-package.zip](staged-package.zip) | fce8cbae72b98d9a744e284ec8863a43f275c7465a7a004864d596d5abc6ce12 |

另现场重算[旧安装18项](previous-installation-check.json)与[原游戏3项](original-game-check.json)均匹配。旧安装仍1021批；326历史源码归档数量来自本轮旧安装proof，未在本次额外打开历史ZIP重核。新22项包已冻结暂存、installed=false；不改原游戏磁盘、不启动gamecode、无桌面/UI/新包游戏验收。

仍缺固定游戏12 mutation+6 Stop共18新增入口的生产安全首次发布、实际IL2CPP metadata/GC、引擎scheduler登记撤销与future producer封口、完整取消完成/根退休、任意部分写失败、native admission gate、游戏冷启动验收、全部inlined/direct/icall调用方和完整异常Record/Context逐级透明性。既有九写V2 Ready不能授权新增18处detour；未安装/实测稍后、RequestPlay关闭、B03/B18a以外不扩勾，Pending不得据本稿消除。
