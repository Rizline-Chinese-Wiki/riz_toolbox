# 当前续接点

更新时间：2026-11-15 13:15（Asia/Shanghai）。工作目录 `C:/Users/71957/Desktop/riz_toolbox`。goal active；完整SDK、B01–B19、真实Speed/Seek与生产engine准入仍未完成。

## 当前能交付的内容

**已生成可安装的启动修复预览包**：[RizToolbox-0.3.0-startup-preview-20261115.zip](../artifacts/delivery-20261115/RizToolbox-0.3.0-startup-preview-20261115.zip)，36,997,650B，SHA `76a3ee353a73c0436547b6fbc13e90a4e27ad3170abc1cd7802ad5842fb8801e`。31成员包含精确已安装的23个owned文件、固定本地BepInEx依赖、安装脚本及范围说明；重新打开ZIP逐成员核对已通过。完整解压后用PowerShell7运行`pwsh -NoProfile -File .\scripts\Install.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline'`。此包是SDK/runtime预览，不是完整SDK或B01–B19正式发布；旧ZIP未覆盖。receipt在artifacts/delivery-20261115/startup-preview-receipt.json。

**当前精确候选已经实际进入游戏**：D:/riz-build-cache-next/runtime-tick-admission-20261115/install-r3，installer65045实际0/d3b2f4，23文件/ledger逐字节一致，用户配置及GameAssembly未变。游戏16960于2026-11-15 12:42:52.238837+08启动，nativeReady49/result1、原il2cpp init1、CoreCLR、Unitytick、host初始化、新status都成立；新会话`20261115T044326943Z-6b60851dd4ec4723ac3594c97063f689`，当前30模块为28 active/1 waiting/1 unsupported，相机模块active。

真实观察覆盖标题及工具面板、主菜单、选曲、Greatness Earth EASY的FREE开局、失焦自动暂停、鼠标恢复/倒计时、暂停页退出至选曲再回主菜单。日志确有RunStarted/Paused/Resumed/SceneChanged/ObservationChanged，**没有RunEnded**；不能把ObservationLost当精确Back取消。游戏已Alt+F4正常退出（bf8735），独立进程查询确认不存在。runtime-r2/observation.json SHA `07780b24cede6f4a6ec53dabc3f9969a76cb4cd6ea305e77ce74b600ea4551d7`，root记录实际0/bf4706。

限制明确保留：未完成正常一局/结算、手动暂停入口或时序准确性；FREE没有精确NormalMode相机对象；game-appearance缺LineRenderer.get_startColor元数据而Unsupported；settings override等待实际设置页；示例面板旧0.2文字不能代替实际SDK1.2.0/宿主身份。实际游戏使用诊断helper（SHA e92f5a095c9ed6341fc7adb53d85ca99c77b0c2534560a0037fc79651c1d59b4），ROOT生产helper虽已新构建但未游戏实测。

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

1. **续原父main-r5，不能重启或改其冻结图**。D:/riz-build-cache-next/line-producer-control-20261114/main-r5，supervisor35864/controller13424/renderer44116，最新实际进程观察仍存活，14自然0、renderer1在途。整体72未结束，无controller-exit.json，不算通过。原r4因系统重启中断、整体退出未知，不能运行r4成功链；旧2631文件保留，interruption receipt SHA `716a3ef05b2596a845dfe215439d71b7b6c771fb6ef40c1f967eddc3b997ea51`。
2. **只有原r5 wait0且完整72自然0后签独立接受**：依work/line-producer-control-next-20261114/main-r5-acceptance-controls.json（SHA `a17b247777910e3dcc9c42ddaa19bd59b81f6b23140b24dfe381edd0f8337adc`）顺序record_main_r5_exit→publish_evidence_r5→check_main_r5_archive→finalize_acceptance_r5，传实际工具chunk。保留原101/6/101+inverse，不缩减检查，现未执行接受链。
3. **托管整合要用新1172/r5门禁控制**：Sol/max正在work/foundation-startup-main-integration-next-20261115准备reviewed prepare/apply/main fresh/归档链；当前实际差异为27个C#（15修改、12新增）+Build.ps1共28文件，完整1160→1172。原95自然步骤保留，再加tick focused一个步骤含8断言，计划96步骤，须以完成的控制审查为准。现在未prepare payload、未apply、未启动ROOT fresh验证。已应用四个native文件不在父托管冻结图内。
4. **不要直接运行旧prepare_main_payload_r2.py或旧Hold939链**：它们硬绑已中断r4或已失败r3、旧1171/21文件，不能冒充新1172。不能用候选95、SDK14、启动r2或实际启动代替fresh ROOT验证；原revision3仅旧控制准备，不是当前main PASS。
5. 待真实正常结算/精确Back、设置页、NormalMode相机、真实判定/Speed/Seek、LOCAL engine/source继续分别补正向证据/功能。没有功能原因时不反复测试已闭合启动部分。

## 原候选与约束

1171组合95自然0及27普通2524/2524、Hold62、producer11/12/8已接受：docs/validation/foundation-current-integration-20261114，summary `e7a1540f75185a9714f935bec18520edfd8cada522df017b9f17d5efabf87a61`、acceptance `f21745fea46a94d30210dd0b1eb9aaeec0f18a6c94a4e7ccd80916287d6baf0e`。旧1171 SDK14为docs/validation/sdk-current-integration-20261114，不能代验1172。旧main-r3有真实OOM1，r4中断未知；历史部分PASS不合成为成功。

相机X正式接入、N1、rate provider/float桥/Play自动handoff及退休冻结批已完成的范围不重做。FullSeek/HoldIdle/Startup/FullCoverage等生产授权标志保持原合同关闭；这里的“实际host启动成功”不等于这些权限成立。B01–B19和N1–N4按[NEXT](foundation-expansion/NEXT-FUNCTIONAL-GAPS.md)及各批实际证据判断，性能后置，不做旧兼容。

用户“有必要的话可以动游戏”授权必要安装/启动/正常UI实测，无须重复确认；认证用户手动，过期/撤销B账号不用。新GameAssembly/PE内容分析、永久env、Git提交/push、覆盖旧ZIP仍不做；本轮仅核GameAssembly SHA `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。README保持SHA `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`。dotnet只用子进程五项已定开关，compiler SDK env也仅子进程；后台CLI隐藏窗口，避免游戏失焦。代理统一Sol/max。

本次替换前两页原字节已完整保存在[20261115历史快照](history/20261115-before-startup-runtime-refresh/)，校验见preservation.json。更早长历史仍在history/20261114-2250-before-current-refresh。goal保持active，不以预览包或限定接受宣称全任务完成。
