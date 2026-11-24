# 当前续接点

更新时间：2026-11-15 12:34（Asia/Shanghai）。工作目录 `C:/Users/71957/Desktop/riz_toolbox`。goal active；完整SDK、B01–B19、真实Speed/Seek仍未完成。

**新用户授权优先于历史禁令**：用户明确说“有必要的话可以动游戏”。必要的候选安装、启动与正常UI/游戏实测现已允许，不再重复询问；身份认证仍用户手动完成。现用computer-use 26.1111.21308的sky，基础/guidance/confirmations/api均已读取。原26.1106版本路径已失效。游戏本体SHA与supportedBuild一致（853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79），不能用旧日志验新源码。

**真实安装已完成，正在修启动链**：`work/game-current-candidate-20261114/`的r1实际1（SDK环境缺Windows.h），保留失败；r2只在子进程修正现有SDK19041环境，原监督Popen.wait实际0，四步全0，精确已接受1171六DLL+fresh JudgeContext/EngineKernel/Doorstop组成23文件包，已安装且逐字节核验。旧实际250个owned文件及用户配置保存在D:/riz-build-cache-next/game-current-candidate-20261114/install-r1/before；安装核验SHA db400021764b7148fc5840ca494afb65c9b1c91aeacdb17487f3b64c1d07872a。

首次启动出现Steam接力后新进程只有WinHTTP、没有CoreCLR/helper，status仍10月22日；源码与Doorstop日志确认内部DOORSTOP_DISABLE=TRUE被子进程继承。新`work/doorstop-process-state-next-20261115/`隔离修复改为PID+创建时间私有hook/CLR标记，保留手动禁用及非Windows行为，不在detach清手动开关。C11与双进程header夹具8/8（fixture-r1的SDK宏warning失败保留；r2同生产预处理开关实际0），fresh Doorstop build-r1原wait实际0；实际DLL父子进程/WinHTTP转发/ignore开关/alias detach六项6/6，独立Sol/max只读审查无阻断。原生只验证该改动，不冒称全native矩阵。

**进程标记修复已合ROOT native三文件并闭合独立证据**：scripts/Build-Doorstop.ps1、native/RizToolbox.Doorstop/riz_process_environment.h、Apply-ProcessEnvironment.ps1；原字节备份与新增原先不存在记录在work/doorstop-process-state-next-20261115/root-native-application，application SHA 00afc1d808350feb23400a475896e678b8230d99dd72662952556715aa40890f。实际ROOT fresh native build root-build-r1实际0/bf1141（session70747）；头文件8、actual Root DLL r2六项0/390b0e，精确实际controller及INI初末另冻。旧r1错误controller冻结/有限验收原样保留。acceptance-r2 SHA 39f563651a1e5117794b223b96351b64d88b6b91650275d012e4ca1be8a0ef58；docs/validation/doorstop-process-state-20261115/doorstop-process-state-evidence.zip 593904B、SHA82fb4cc29ae5a733c68474c88e0d0ac5850cfc91991f2889b7eb4a8c1f36337b，107输入/18 Root native。agent checker0/173373及root独立archive-only0/690ed8，root-check-receipt.json保留；[报告](../docs/2026-11-15_doorstop-process-state-report.md)。不是整套SDK或游戏通过。ROOT托管1160、Build.ps1/README未变；诊断覆盖层未合ROOT。

**真实启动的最新结论**：D:/riz-build-cache-next/doorstop-process-state-20261115/install-r4安装23文件实际0/fe65b3（session99180），精确已接受1171六DLL+fresh诊断native(v2)，保留配置。此前install-r1冷Steam接力已证私有hook标记和helper加载；runtime-r2进程664日志精确operation.prepare38拒绝0、原init返回1，未加载CLR。v2后进程40780于11:37启动，nativeReady49/result1、原init1、CoreCLR及新SDK会话成功；**03:40:46UTC实际Unity tick已绑定，随后ModuleManager.Add抛“Module manifest or interval is invalid”**。旧“未绑定tick”早段观察已被后续日志修正，runtime-r3/observation.json中unityTickBoundObserved=true，保留原始日志。窗口无响应，sky激活连续超时；root核对PID/启动时刻后只结束自有未开局测试进程（10e78c），随后CIM确认无游戏。runtime-r3 SHA b2f9d93afab0caea5a8377469e737d36c62da5a337883eea429e109d971c74f6（记录实际0/6f4911），不算游戏/SDK通过。

诊断v1/v2的fresh JudgeContext85导出、Doorstop、16进程原init合同及actualDLL6分别实际通过；初diagnostic-build-r1 PowerShell CRLF匹配失败保留，r2/r3修正LF后各自然0。诊断只加私有观察，源码剥离还原原检查顺序，未改准入/ABI/profile。

**当前启动修正版已离线通过并安装，真实验收被原生布局申请阻断**：work/runtime-tick-admission-next-20261115/Runtime四文件manifest c1a0bdf18d9f2d6d0bedabfa887916ae729e6df6475d75f986c8924f7c660e31，固定内部tick consumer先筛选，相关tick仍每次full proof+二次native identity。相机interval0修正v1用epsilon；原zero负控29/31准确复现异常。随后独立review查到原生x64可flush subnormal风险（实际游戏FP模式未观测，不改模式），module-manifest-startup-next-20261115/v2采用最小正NORMAL常量2.2250738585072014e-308/确切bits0010000000000000，保持每推进host tick一次，三文件含新真实30内置模块注册+perframe测试。

新组合1172（原1171+1测试输入）r1原supervisor40656/controller15488实际wait0，10自然步骤全0：六Release W0/E0、solution0E、focused8、Runtime383/Hosting20/Adapter10/Hooks210/Gameplay175/Module31/Camera55共884普通用例。r2针对NORMAL最终源码fresh六/solution+focused8+Runtime383/Module31/Camera55共469普通用例，6自然0，supervisor34372/controller43772原wait0（终态观察0/0bdd7b，result SHA fbb63a9cd13d61b7df8a1625afc7ebaa54b849f568c24168e950ce24f2b39538）。两次完整冻结/父1160未变；r1旧结果不代验r2未执行组件。ROOT托管未应用。Sol/max正独立整理离线证据/报告，不重复已过回归。

精确r2六DLL/新Examples+保持同一native诊断basis23文件已安装：D:/riz-build-cache-next/runtime-tick-admission-20261115/install-r2，原installer.wait实际0/b62b8e（session80779），逐文件/ledger核验，配置与GameAssembly保留。12:31:11游戏15924正常到标题但原生拒绝：operation.prove.block-present145/result0，operation_row14、dobby_result0，原始init1；无新Loader/status旧。已Alt+F4正常退出，runtime-r1/observation.json SHA b0295c13d99432c9e7e220c51f5986c765333c493617f5cf724bd37301a26c09（记录实际0/85393a）。当前新托管修正尚未进入真实执行，不能说游戏修复已验收。新Sol work/playback-attestation-allocation-next-20261115隔离修复Prove代码与metadata分开寻找正地址失败：拟单次8192分配，code页RX+metadata页R，保留全部证明；待自身实际Prove/OSunwind夹具与fresh85导出构建，再安装同r2托管重测。不覆盖旧ZIP，不开放FullSeek/HoldIdle/Startup/FullCoverage。

**原父main-r4已中断，新r5已完整重跑启动**：OS LastBootUpTime=2026-11-15 10:35:40.500+08:00，原supervisor42312/controller44740/renderer44196均不存在且无controller-exit.json；仅14自然0、renderer1只有RUN，整体退出码未知。旧2631文件核验保留，main-r5-r4-interruption.json SHA 716a3ef05b2596a845dfe215439d71b7b6c771fb6ef40c1f967eddc3b997ea51。新r5实际launch0/4d995f，supervisor35864原wait持有controller13424，自03:06:27UTC开始同1160完整72步；11:13两者存活、10步自然0，11:18已有13步自然0（producer12在途），没有终态，不算通过。只改新OUT/r5控制名、child-only Python缓存禁写，原完整检查/101/6/101+inverse保留。

## 下一离线工作

0. **先续原生布局申请修复**：work/playback-attestation-allocation-next-20261115，读取新manifest/自身Prove+OSunwind夹具，合入当前诊断v2的精确inl基线、fresh JudgeContext85导出构建；不改ROOT1160/Build或活父回归。保留原生准入及全部不可变证明，代码/元数据两页同一次allocation分别RX/R。完成后以已通过当前r2精确托管产物重包/备份安装/实测，勿重复相机或Runtime已通过部分，不能借本轮旧元数据申请版本结果验新helper。先看实际进程/终态后再动作。新托管离线证据由Sol另整理，完成后root独立archive-only核对。
1. **先续新r5原进程，保留r4中断**：D:/riz-build-cache-next/line-producer-control-20261114/main-r5*，supervisor35864/controller13424，终态由原wait写main-r5-supervision/controller-exit.json。当前完整72步未完成；活着就不重复启动或中断，不改ROOT源码/冻结控制，不缩减101/6/101+inverse。
2. **不要运行旧r4成功链**。新r5只有原监督actualControllerExitCode=0且全72自然0，才按work/line-producer-control-next-20261114/main-r5-acceptance-controls.json执行record_main_r5_exit→publish_evidence_r5→check_main_r5_archive→finalize_acceptance_r5（传实际publisher/check chunk）。控制registry SHA a17b247777910e3dcc9c42ddaa19bd59b81f6b23140b24dfe381edd0f8337adc，仅准备/语法检查，未签接受；保留51个原archive检查，归档目的地D:/riz-build-cache-next/line-producer-control-20261114/main-r5-evidence，收据main-r5-acceptance-receipt.json。不能伪造r4接受记录。
3. **新组合95步和当前SDK14均已接受，不再重跑候选或旧939**，见下方证据。父r5接受后须先新增reviewed main prepare修订，明确将原prepare_main_payload_r2.py的父r4接受依赖转绑真实r5接受及其归档，而非改旧收据/跳过门禁；再apply_main_payload→supervise_main。仍要求当前组合95及同1171源SDK14接受；21文件应用保留原字节备份及11新文件原先不存在记录。旧r2仍硬绑r4，不能直接当新r5链运行；main payload/实际应用/main-r1均尚未执行。
4. fresh main验证/接受链已全部准备，尚未执行：`validate_main.py`/`supervise_main.py`保持95自然步骤（六Release、solution45、Build parser、Hold62、producer11/12/8、27普通组件），直接构建实际ROOT并冻结1171输入，原监督持有真实退出。main自然95/原Popen.wait实际0后，运行`record_main_exit.py`（传本次main启动工具的真实launch-tool-chunk/session）→`publish_main_evidence.py`→归档内同script `--check-only docs/validation/foundation-current-main-20261114`→`finalize_main_acceptance.py`（传实际publisher/check的chunk/session）。独立main接受归档名为foundation-current-main-20261114；不能用候选95/父72或旧包代验。
5. **main控制以最新revision3为准**：`work/foundation-current-integration-next-20261114/main-validation-controls-revision-3.json` SHA `c65c9687f42d2446b4dfc324e40005cc4e716b5b1bad2e0b736be47fd73f56c5`，记录实际0/67e1ca；含main-source-review.md与三个终态/归档控制，均仅语法审查，未签main通过。早期preparation/revision2按历史保留。新增source_closure.py在prepare/apply/实际main执行前中后检查完整文件集合，覆盖新增文件而不只旧hash：现父1160/45项目与已冻候选1171/45项目都匹配；逻辑遗漏一个真实C#声明会被实际发现/拒绝，没改物理源。revision2记录原root35501实际0/57b224，SHA `9a337b43b130568fedb982adeb4ac172e43338983540280da167621ae328c1e0`；不是重跑功能测试。**不要运行旧Hold939的prepare/apply链**：依赖已失败main-r3并会带回旧fixture。

历史上原父renderer1曾继续增长CPU，之后系统已重启；当前三进程缺失且无实际终态，不能由旧活体观察推断通过或恢复其原wait句柄。旧冻结控制保持不变，无手动中断活进程。

合并候选旧run-r1/controller17632/root79606停在suite-Runtime（73步骤记录、前72自然0）；write_stdin返回Unknown process id 79606，随后原controller及unfinished child41456两次确认为不存在，result无finishedUtc。**原工具/控制器退出码未知，未通过**，不能拿部分结果认证新图。完整源码/控制/日志保留，interruption receipt SHA `2e18ed6349f5d42bcb71a52a8d3a4b98a97bb344958258d6208b85fda231c027`；prepare_supervised_r2实际0/041f5a核1171输入及父图不变后才允许启动r2。没有主动中断或重启活进程；不能声称已知是某个账户/工具更新杀进程。

## 已验证的当前候选

**最新组合95步已接受/归档/直接复核，不重复实施或验证**：`docs/validation/foundation-current-integration-20261114/`，summary SHA `e7a1540f75185a9714f935bec18520edfd8cada522df017b9f17d5efabf87a61`；1897文件/222149802字节。原run-r2 supervisor34024/controller28536 Popen.wait实际0，95自然步骤全0，完整1171源与主目录旧1160源均未变；六Release W0/E0、solution45/0E/55既有warnings、Build parser0，27普通组件2524/2524、Hold62、producer11/12/8三项分别记。原root receipt SHA `3a641f8fb080ae7d7590e78818b92d6569ffbfaf358083157cce592e6e26aeaf`（记录实际0/781b72）。发布root59847实际0/4e2443；直接归档checker root21633实际0/aafcd3；候选acceptance SHA `f21745fea46a94d30210dd0b1eb9aaeec0f18a6c94a4e7ccd80916287d6baf0e`，finalize实际0/43dd70；[报告](../docs/2026-11-14_foundation-current-integration-report.md)。mainApplied/fullManagedMatrix/fullJudgementsRerun/parentRendererSuiteRerun/sdkGoalComplete均false。它显式保留父新metadata夹具、Hosting GetTargetPath、Hold939及描述符修正；不拿旧r1部分通过代验。

**当前组合独立SDK14已完成，不用再跑旧939 SDK分发**：`docs/validation/sdk-current-integration-20261114/`，summary SHA `bb5cbd807f090b9dde1255c9386368279dbea9582cb4fccc6fb2109db28bb6f0`，1263文件。完整1171源已含Distribution.Tests2输入；另列核对的两者与完整图重叠，不计1173，见work/sdk-current-integration-20261114/input-scope-reconciliation.json。原模板/pack/test脚本另行冻结；controller35948/supervisor15176的原Popen.wait实际0，正式host W0/E0，两套独立插件14/14，launch0/a2fddf非验证0，root receipt `fdd4dcbeee63b5e6d2682e128433baf95d4e44cd882e2949e7601971ae94c936`。发布root72145实际0/ad358f；直接归档checker root43574实际0/9ed1bd；acceptance SHA `232ae4ebf6e25af8766d15ad777e1214b770da7cd0be5a5a1bbe8da115645e5b`；[报告](../docs/2026-11-14_sdk-current-integration-report.md)。新runner只从当前run-r2消费冻结源码，不借其未结束的验收；14与95/父72分别记。首次launch因父输出目录缺失在创建任何controller前失败，原chunk4a4041 receipt保留；补目录后才真正launch。无用户模板/全局环境/游戏变化，不声称全SDK通过。

**描述符就绪修正**：`docs/validation/gameplay-descriptor-readiness-20261114/`，summary SHA `7c1d950ddbfa491ea3316de872a9fa68e2a40c03a4b1379cd55c3cb011717492`；1987文件/221618253字节。完整1171源/45项目solution0E/55既有warnings；新run-r3 controller39064/root78272实际0/85002b，90步全自然0，27普通组件2524/2524，另Hold62。其中Gameplay175、SourceGeneration14、Runtime375均在本轮实跑。fullManagedMatrix/fullJudgementsRerun/parentRendererSuiteRerun/mainApplied/sdkGoalComplete均false。

归档root80909实际0/3cfc66，直接归档checker root8614实际0/74fd71。candidate acceptance SHA `8a033d0149fba3d86047123103d6d659dc68269c4a007ef89dbf7f4ade8c4611`；[报告](../docs/2026-11-14_gameplay-descriptor-readiness-report.md)。修正：fresh null/null返回NotReady；旧有/已变descriptor仍TargetExpired；readiness变化仅结束控制owner，保留独立graph/clock。测试保留零provider/写入/audit、根稳定、恢复后确认和Dispose归零。历史r1/r2/focused-r3/r4失败及focused-r5的14/14均原样归档，不代验全run-r3。

**组合来源与待合文件**：以描述符1171冻结图重叠父批metadata历史修正，唯一rebase冲突是`FakeNative.LineProducerControl.cs`，保留父批新字节SHA `6692017570ab4c289d3a4d082320031658c787f3af7391055fa2e0374595128c`。Hosting GetTargetPath保持。payload20个C#（含11新文件）+Build.ps1共21文件，完整1171（scoped939+extra232）；manifest SHA `7b8cea0b1a3a5d7cf06c5821374a50e2433402d9e80c2f6383c1a48bc6dc18d4`，prepare实际0/e91990。Build新增45个已实现Hold selector调用，parser实际0/93c818；父main未改。已接受的新95步不是完整Judgements或父renderer重跑。

## 主目录及失败边界

主目录当前生产仍原producer928 scoped图；额外232个solution输入共1160。只新增两项已带原字节备份的测试修正：renderer-only JudgeMetadataLog/引用历史有界压缩，和Hosting测试实际GetTargetPath。payload `fb359759bd4a0e8d715f2777c8fe7ba9a45ab7f29a6fb7149122d9a29dfcd7e8`，apply实际0/78e15c；freshMainPassed=false。保留distinct集合/累计long计数、所有HGlobal/原生操作/回调/proof根/失败释放/完整101/6/101与inverse，生产src不改。相关focused11/12/8实际0和早段diagnostic0各自scope有限，不代验完整main-r4。

**旧main-r3已失败**：原controller19256/supervisor39624在2026-11-14T14:00:52UTC通过原Popen.wait记录actual1。renderer1逆序在测试JudgeMetadataLog.Add/List.Grow发生OOM；不是算法错误已证实，也不是PASS。928原源码已存main-r3/source，failure receipt SHA `b98c2393eb016e3bb15737f10afc43dc6a18083ee0e414de20c12b325ee5bc8d`。旧r3成功record/publish/finalize均作废；旧main-r1资源中断、r2缺失进程和各自旧图保留。

已有完整solution/Hosting证据：`docs/validation/complete-solution-hold-20261114/`，summary `94b40bdc6e452b24b8fdaeeb9de10fe8917c1fb9ab1736a1d35b356708c26218`，45项目/完整1171、solution0E，Hosting artifacts/default各20/20。原Hold939 candidate summary `0c9a8d8c302cea6cf2799f7967bbe0d500a85b0e6f575f4a30c39d937e719401`已被新描述符及新合并候选承接，不重做owner/guard/alive/discovery。

## 交付件与未完成范围

现有SDK编译参考包：`artifacts/sdk-reference-candidate-20261114-hold939.zip`，18385889字节，SHA `d83ecee11dab54710c0bd632a32f1080dca9e3b929f9ff89820d637e7d700294`。939图双独立插件14/14及实际解压四命令均通过；[报告](../docs/2026-11-14_sdk-current-hold-candidate-report.md)。它不含RizToolbox运行时，不认证更新图。旧881运行时预览ZIP原样保留，不能用于验收本轮。

相机X正式接入、N1、rate provider/float桥/Play自动handoff与退休冻结批已完成的部分不重做。N2的独立owner死亡、全局factory/origin、真实engine detach/release/scheduler及实际module/body/source准入仍未全闭合；FullSeek/HoldIdle/Startup/FullCoverage保持false。N3/N4按[功能范围](foundation-expansion/NEXT-FUNCTIONAL-GAPS.md)及最新各批证据判断，不能把历史基线表当当前全部缺失。

## 工作约束与历史

必要游戏操作按顶部新用户授权执行；账号认证不自动操作，过期/撤销B账号不使用。新GameAssembly/PE内容分析、永久环境修改、Git提交/push、覆盖旧ZIP仍保持原限制；本轮只计算已安装GameAssembly文件哈希，未新开二进制分析。README必须保持SHA `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`。所有dotnet仅子进程设置cert/global-tools/telemetry/first-time/no-logo五个已定变量，不改USERPROFILE/CLIHOME。用户允许代理时只用Sol/max；最近三个额度失败不重复重启。

原785869字节checkpoint及465418字节NEXT原文完整保存在[历史快照](history/20261114-2250-before-current-refresh/)；字节SHA见其中preservation.json。旧记录为当时历史，冲突时本页及实际原进程终态优先。根goal继续active。
