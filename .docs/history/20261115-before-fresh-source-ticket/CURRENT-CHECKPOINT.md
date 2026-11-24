# 当前续接点

更新时间：2026-11-15 16:45（Asia/Shanghai）。工作目录 `C:/Users/71957/Desktop/riz_toolbox`。goal active；完整SDK、B01–B19、真实Speed/Seek与生产engine准入仍未完成。

**最新功能候选1174已完成限定离线验证**：补入正式stock LowerSetTime闭合成员，完整组现在为22 source+GetTime；修复Gameplay后端首次hook未就绪后永久缺失mapping的缓存，以及旧capture转换入口先选profile后Probe的顺序。新增cache只管理可用性，真实固定profile/原owner校验不放宽。完整1174源输入+Build1，相对1172仅5diff；新FreshSource原生协议在另区，不在本归档内。

新solution 0 errors/55既有warnings；Gameplay184/184、Runtime383/383、Module31/31，共598普通用例（focused9已重叠，不另计）。同次新Runtime/Sdk再执行5个owned LowerTime安装/来源/原调用/异常退出/unwind用例全自然0。r1漏4个固定测试fixture输入导致155/184/工具exit1（d66972）保留，r2只补原定子进程fixture输入、复用未变构建，8步骤及原session51754实际0/0d9b7a。不把owned或泛型cache正向提升成stock mapping/production正向。

[新报告](../docs/2026-11-15_clock-profile-readiness-report.md)；ZIP docs/validation/clock-profile-readiness-20261115/clock-profile-readiness-evidence.zip，26,114,640B，SHA `4d188b1a8362874cba9ec4f6fdd2867b56e43af092a3ca83e8a842f4d28dc382`；summary `bf986f42d38a9e0e895d31ef074ba0f5533bc4a75b1c51c058de4947cf2b0fde`。agent发布实际0/600922；root直接ZIP-only实际0/9fe75d。独立回执 work/clock-profile-readiness-next-20261115/root-archive-check.json；ZIP内independent=false保留发布当时状态，不改旧ZIP。root另实核完整1160/45项目零变化（原session5850完成实际0/ded709）。当前预览两包不重打、不据此声称包含1174。

**下一功能施工**：正式接线不能先伪造首RunId。共享observer提交真实run之后，由新原生opaque FreshSource票签同一epoch域，再由独立七根factory使用；先安装dormant physical group、后等待真实Pause，才到正式provider/publisher接线。原PlayerWatch.Ordinal/裸诊断不能替代source generation。Astra与Sol在 D:/riz-build-cache-next/production-fresh-source-20261115/ 处理此独立增量；当前只有编译/只读review，不签完整stock/Clock/production准入。Runtime null-provider正式接线仍未闭合；完整SDK/B01–B19/真实SpeedSeek仍未完成。


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

0. 两件1172预览与SDK四命令交付已闭合；新1174限定离线候选另已归档/root独立检查。继续顶部FreshSource正式source epoch/factory与后续provider接线；不得用只读审计或source票替代Clock/production准入。整体goal active，不重复已闭合功能。
1. **续原父main-r5，不能重启或改其冻结图**。最新22自然0的有限进度见顶部；原wait终态仍未出现。原r4重启中断整体未知，原r3 OOM失败，不运行它们的成功链。
2. **只有原r5 wait0且完整72自然0后签独立接受**：依work/line-producer-control-next-20261114/main-r5-acceptance-controls.json（SHA `a17b247777910e3dcc9c42ddaa19bd59b81f6b23140b24dfe381edd0f8337adc`）顺序record_main_r5_exit→publish_evidence_r5→check_main_r5_archive→finalize_acceptance_r5，传实际工具chunk。保留原101/6/101+inverse，不缩减检查，现未执行接受链。
3. **使用新1172/r5控制，不使用旧r4/1171脚本**：work/foundation-startup-main-integration-next-20261115已准备并冻结13项控制，SHA/实际工具见顶部；28文件=27C#（15修改、12新增）+Build.ps1。父r5真实接受后顺序prepare_main_payload→apply_main_payload→supervise_main；96自然0及原wait0后record_main_exit（实际launch chunk）→publish_main_evidence→归档内check_main_archive→finalize_main_acceptance（实际publisher/check chunk）。完整1172/45项目闭包、27普通2534/62Hold/producer3/tick8；当前没有payload、apply或fresh ROOT验证。独立native四项保持原字节。
4. **不要直接运行旧prepare_main_payload_r2.py或旧Hold939链**：它们硬绑已中断r4或已失败r3、旧1171/21文件，不能冒充新1172。不能用候选95、SDK14、启动r2或实际启动代替fresh ROOT验证；原revision3仅旧控制准备，不是当前main PASS。
5. 待真实正常结算/精确Back、设置页、NormalMode相机、真实判定/Speed/Seek、LOCAL engine/source继续分别补正向证据/功能。没有功能原因时不反复测试已闭合启动部分。

## 原候选与约束

1171组合95自然0及27普通2524/2524、Hold62、producer11/12/8已接受：docs/validation/foundation-current-integration-20261114，summary `e7a1540f75185a9714f935bec18520edfd8cada522df017b9f17d5efabf87a61`、acceptance `f21745fea46a94d30210dd0b1eb9aaeec0f18a6c94a4e7ccd80916287d6baf0e`。旧1171 SDK14为docs/validation/sdk-current-integration-20261114，不能代验1172。旧main-r3有真实OOM1，r4中断未知；历史部分PASS不合成为成功。

相机X正式接入、N1、rate provider/float桥/Play自动handoff及退休冻结批已完成的范围不重做。FullSeek/HoldIdle/Startup/FullCoverage等生产授权标志保持原合同关闭；这里的“实际host启动成功”不等于这些权限成立。B01–B19和N1–N4按[NEXT](foundation-expansion/NEXT-FUNCTIONAL-GAPS.md)及各批实际证据判断，性能后置，不做旧兼容。

用户“有必要的话可以动游戏”授权必要安装/启动/正常UI实测，无须重复确认；认证用户手动，过期/撤销B账号不用。新GameAssembly/PE内容分析、永久env、Git提交/push、覆盖旧ZIP仍不做；本轮仅核GameAssembly SHA `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。README保持SHA `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`。dotnet只用子进程五项已定开关，compiler SDK env也仅子进程；后台CLI隐藏窗口，避免游戏失焦。代理统一Sol/max。

本次替换前两页原字节已完整保存在[20261115历史快照](history/20261115-before-startup-runtime-refresh/)，校验见preservation.json。更早长历史仍在history/20261114-2250-before-current-refresh。goal保持active，不以预览包或限定接受宣称全任务完成。
