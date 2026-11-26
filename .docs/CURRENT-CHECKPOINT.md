# 当前续接点（2026-11-26，优先读本页）

## SDK 1.4 和 Windows 预览包集成候选

维护者可在 [PR #15](https://github.com/Rizline-Chinese-Wiki/riz_toolbox/pull/15) 一次审阅 SDK 1.4、官方谱的选曲读取与 Play 导航、宿主控制和自动构建。r23 的封包验收有[独立记录](../docs/validation/r23-delivery-20261126/README.md)。后续集成源码在同一组 DLL 上通过 Loader 33/33、UI/Input 90/90、Game 92/92。Runtime 预览 ZIP 的 38 个文件经过回读，从 ZIP 提取的命令脚本通过 21 个场景。

启用插件时，`Send-HostCommand.ps1 -WaitSeconds 10` 会返回实际结果。缺少依赖时返回 `waiting`，插件清理异常时返回 `failed`。停机回执附带已持久化的最终状态修订号。离线宿主用 7 条命令验证了五种动作和四种结果。[宿主控制说明](../docs/Host-control.md)

尚待 Steam 同一局验证两项功能。官方谱须在调速、跳转和恢复后观察到媒体时间继续推进、产生新的判定并结算。自制谱须在真实结算窗返回选曲后再次读取官方成绩，与开局前的快照比较。

## r23 两项功能已联合构建（11-26）

SDK 1.4 的插件可以按需读取当前所选官方谱面，播放组件的准备过程也减少了重复校验。从两项改动的 1888 个输入重建 Release，11 套回归通过 2130/2130 项，另有 5 个播放专项在同一份 Runtime 上通过。当前 Steam 装有 r22。下一次实机分别核对选曲页谱面 ID，以及官方谱变速、跳转、恢复和自制谱结算。[选曲接口 PR #6](https://github.com/Rizline-Chinese-Wiki/riz_toolbox/pull/6) · [播放优化 PR #7](https://github.com/Rizline-Chinese-Wiki/riz_toolbox/pull/7)

## r22 源码与离线验收（11-26）

r22 Basic、Runtime 和 SDK 已完成发行验收。封包组件通过 2123/2123 项标准回归和 360 项播放专项检查。Steam 实际升级成功，播放组件在进程启动后 322.120 秒报告等待开曲。官方谱变速、跳转与自制谱结算待同局验证。r21 与 r22 分别在 [PR #4](https://github.com/Rizline-Chinese-Wiki/riz_toolbox/pull/4) 和 [PR #5](https://github.com/Rizline-Chinese-Wiki/riz_toolbox/pull/5)。[r22 验收摘要](../docs/validation/r22-steam-startup-20261126/README.md)

## r21 播放控制与自制谱

发行候选 11 套回归通过 2121/2121 项，包内播放专项 18 种场景通过 336 项检查。Steam 已完成实际升级，游戏及官方谱测试插件已启动。0.75 倍速、跳转、恢复后的同步和自制谱结算等待同局实测。[r21 验收记录](../docs/validation/r21-steam-playback-20261126/README.md)

## r20 官方谱实测与交付（11-23）

Steam 版《Pastel Lines》EASY 暂停后，恢复指令至实际恢复为 0.103 秒（r18 同曲为 200.515 秒）。随后记录到两次判定、结算与返回选曲。11 份恢复后播放样本均缺媒体时间，SDK 还不能持续报告恢复后的曲内时间。Steam 记录本次进程以退出码 0 结束。[同局摘要](D:/riz-m2/r20-steam-live-20261123/stock-run-01/stock-run-summary.json) · [完整事件](D:/riz-m2/r20-steam-live-20261123/stock-run-01/events-final-000001.jsonl) · [退出记录](D:/riz-m2/r20-steam-live-20261123/stock-run-01/steam-exit.txt)

同局 0.75 倍速在写入前被拒绝，暂停时音源时间与末帧进度加偏移相差 117.25 毫秒。跳转到第 100 秒也在写入前被拒绝，实际 Hold 列表 Clear 入口与旧配置不同。两项仍需用含修复的新包实测。[调速回执](D:/riz-m2/r20-steam-live-20261123/stock-run-01/set-speed-gen2-result.json) · [跳转回执](D:/riz-m2/r20-steam-live-20261123/stock-run-01/seek-gen2-result.json)

两种 r20 玩家包均通过隔离安装与升级，Basic 随包回归 15/15。SDK 1.3.0 作者包通过随包离线源编译。[玩家包回执](D:/riz-m2/r20-standard-players-20261123/delivery-validation.json) · [正式交付回执](D:/riz-m2/v5-live-fixes-r20-20261123/publication/r20-delivery-validation.json) · [SDK 下载](D:/riz-m2/v5-live-fixes-r20-20261123/publication/author/RizToolbox-r20-Plugin-SDK.zip)

Steam 已从 r18 升级到 r20 Basic。安装后的 22 个文件均与候选清单相符，8 个用户设置或模组文件保持原散列。官方谱控制探针 1.1.1 现在分别记录谱面延迟和已应用偏移。候选回归通过 2,113 项，其中 Gameplay 260 项。暂停、调速、跳转与恢复的结论以 r20 自己的对局日志为准。[安装回执](D:/riz-m2/r20-steam-live-20261123/installation-verified.json) · [Basic 玩家包](D:/riz-m2/r20-standard-players-20261123/downloads/RizToolbox-r20-Steam-Basic.zip) · [冻结候选回归](D:/riz-m2/v5-live-fixes-r20-20261123/qa/prepared-suites.json)

r20 修复了暂停时重复核验镜像、恢复回调尚在执行时尝试回收播放对象，以及同一局的播放源已经撤回后每帧重新核验的问题。0.75 倍速和跳到第 100 秒的拒绝回执增加了具体的时间读数、闭合方法入口或元数据原因。[冻结源](D:/riz-m2/v5-live-fixes-r20-20261123/source-freeze.json) · [同局撤回与下一局检查](D:/riz-m2/post-r19-stock-blockers-20261123/ended-cancelled-dormant.log)

冻结之后另修复了自制谱调度累计 2,049 次时误报耗尽的问题。回归覆盖待处理音符旁连续完成的空调度，以及真实帧协议的嵌套上限。工作区的音频与内容回归通过 1,073 项，供下一候选打包。[单独验证](D:/riz-m2/post-r20-local-scheduler-20261123/validation.json)

## r19 标准玩家包与 SDK 1.3.0（11-23）

**r19 已封包，Steam 实机仍为 r18 Basic。** 两轮 r18 实测的进程均已退出。[r19 Basic 玩家包](D:/riz-m2/r19-standard-players-20261123/downloads/RizToolbox-r19-Steam-Basic.zip) · [r19 Runtime 纯底层包](D:/riz-m2/r19-standard-players-20261123/downloads/RizToolbox-r19-Steam-Runtime.zip) · [r19 SDK 1.3.0 作者包](D:/riz-m2/v5-live-fixes-r19-20261123/publication/author/RizToolbox-r19-Plugin-SDK.zip)

r19 加入自制谱开场诊断。主菜单只有在两组已核实的选曲卡片来源均为空时才会提前返回可重试的状态。协程保留首个 Wait 阶段或节点故障，Tween 借用引用失败时记录具体处理阶段。冻结源码 Release 构建为 0 错误、86 个警告，六份部署 DLL 通过 11 套共 2,112 项检查，候选 ZIP 另通过 4 个受控播放场景。原生映射沿用 r14 的 182 个场景，已复核 292 份源码和 222 份证据文件。[源码冻结与输入](D:/riz-m2/v5-live-fixes-r19-20261123/source-freeze.json) · [正式交付回执](D:/riz-m2/v5-live-fixes-r19-20261123/publication/r19-delivery-validation.json) · [随包专项回归](D:/riz-m2/v5-live-fixes-r19-20261123/qa/packaged-focus.json)

Runtime 和 Basic 共用的 16 个文件逐项一致。隔离宿主分别发现 0 和 3 个插件，Basic 随包回归 15/15。隔离安装的 r18 Basic → r19 Basic 替换 6 个文件并沿用 16 个，卸载后保留用户设置。SDK 作者包仅用包内离线源编译新插件。下一轮的单音符、成绩和官方谱控制探针也已由该 SDK 离线构建。[玩家包和隔离安装](D:/riz-m2/r19-standard-players-20261123/delivery-validation.json) · [独立作者验证](D:/riz-m2/v5-live-fixes-r19-20261123/publication/isolated-author-cycle/author-kit-cycle.json) · [下一轮探针和输入清单](D:/riz-m2/post-r19-stock-blockers-20261123/probes/20261123T142643651Z/validation.json)

## r18 Steam 实测归档（11-23）

r18 Basic 已从 r12 实际升级，22 个包内文件与安装记录相符，11 份用户文件保留。两个实测进程的事件、末次状态、Unity 日志与 Steam 退出记录已封存，回执包含 38 份运行证据的 SHA-256。以下时间为北京时间。[实机安装回执](D:/riz-m2/r18-steam-live-20261123/installation-verified.json) · [两轮实测总回执](D:/riz-m2/r18-steam-live-20261123/live-run-receipt.json)

- **B15 单音符自制谱。** 1.1.1 探针在主菜单得到可重试的 `NotReady`，进入选曲后用 18.817 秒受理开局。随后日志记录 Wait 节点或清理绑定未确认，以及 `PublishedTweenSource` 的 `ReadyPopAppend` 借用引用交付失败。首个 Tap 和结算窗未观察到。PID 46916 在 21:22:30 以退出码 0 结束，单音符探针已归档。[事件](D:/riz-m2/r18-steam-live-20261123/local-run-01/events-000001.jsonl) · [结算核对](D:/riz-m2/r18-steam-live-20261123/local-run-01/verification.json) · [退出记录](D:/riz-m2/r18-steam-live-20261123/local-run-01/steam-exit.txt)
- **官方成绩。** LOCAL 开局前与另一个进程重启后均在选曲页读取 444 条，SHA-256 完全一致。还需在同一进程完成自制谱结算、返回选曲并重读，再核对对局资源是否释放。[首次成绩与对局日志](D:/riz-m2/r18-steam-live-20261123/local-run-01/toolbox.log) · [重启后成绩](D:/riz-m2/r18-steam-live-20261123/stock-run-01/official-scores-after-restart.txt)
- **B14 官方谱。** 《Pastel Lines》EASY 的曲内进度先推进到 104.65 秒，21:46:28 观察到 `Paused`。约 130 秒后首次读到暂停时钟，进度为 106.660 秒，媒体时间为 106.732 秒。0.75× 变速因媒体时间与进度加偏移不符被拒绝。跳转到曲内 100 秒因 `Hold List.Clear` 签名不匹配被拒绝，准备阶段耗时 113.64 秒。[变速回执](D:/riz-m2/r18-steam-live-20261123/stock-run-01/speed-075-result.json) · [跳转回执](D:/riz-m2/r18-steam-live-20261123/stock-run-01/seek-100-result.json) · [最终性能数据](D:/riz-m2/r18-steam-live-20261123/stock-run-01/status.json)
- **恢复、卡顿和退出。** 同局在恢复回执 200.515 秒后观察到 `Resumed`。后续采到 `playing=true`，直到退出前仍缺有效的曲内进度和媒体时间，关闭请求发出时窗口无响应。主线程两次采样分别停在播放源根引用回收触发的整组校验，以及宿主帧内的播放代码体校验。22:23:16 请求 `WM_CLOSE`，Steam 在 22:23:30 记录 PID 12396 以退出码 0 结束。自然结算、返回和持续同步推进仍待实机验证。[恢复回执](D:/riz-m2/r18-steam-live-20261123/stock-run-01/resume-result.json) · [恢复后线程栈](D:/riz-m2/r18-steam-live-20261123/stock-run-01/after-resume.stack.txt) · [游玩卡顿线程栈](D:/riz-m2/r18-steam-live-20261123/stock-run-01/playing-stall.stack.txt) · [退出记录](D:/riz-m2/r18-steam-live-20261123/stock-run-01/steam-exit.txt)

## r19 封包后的官方谱源码与离线验证（11-23）

后续源码的暂停时钟准备沿用已验收的播放组，仍逐次验证 GetTime。定向夹具中的 4 次整组、344 次整映像重复校验降为 0，保留 4 次 GetTime 检查。播放源和 GetTime 回调排空后才执行回收所需的完整校验。暂停时钟准备和提交遇到身份漂移仍会拒绝。当前源码的 260 项 Gameplay、正常启用及取消、丢局、切换的受控场景已通过。[源码散列与合入](D:/riz-m2/post-r19-clock-pause-audit-20261123/checkout-source-proof.json) · [暂停准备](D:/riz-m2/post-r19-clock-pause-audit-20261123/checkout-clock-preparation.log) · [在途回收](D:/riz-m2/post-r19-stock-blockers-20261123/ended-root-retirement-in-flight.log) · [Gameplay 260/260](D:/riz-m2/post-r19-stock-blockers-20261123/gameplay-with-fixture.log)

B14 继续核对实机的暂停媒体时间偏移和 `Hold List.Clear`，再用同局读回 0.75×、100 秒跳转和恢复后的连续推进。B15 用 r19 的新增诊断继续确认首音符、四类判定、结算、返回和同进程成绩隔离。两条流程还需同进程切换。F06/F10 普通通关结算和真实菜单恢复、Basic 三个模组的画面、快捷键与声音仍待实机验收。[最新功能缺口](foundation-expansion/NEXT-FUNCTIONAL-GAPS.md)

## r18 玩家包（历史）

[r18 Basic 玩家包](D:/riz-m2/r18-standard-players-20261123/downloads/RizToolbox-r18-Steam-Basic.zip) · [r18 Runtime 纯底层包](D:/riz-m2/r18-standard-players-20261123/downloads/RizToolbox-r18-Steam-Runtime.zip) · [r18 SDK 1.3.0 作者包](D:/riz-m2/v5-live-fixes-r18-20261123/publication/author/RizToolbox-r18-Plugin-SDK.zip)

r18 在开局前指出哪些自制谱音符缺少游戏可用的谱线段。两种玩家包含有相同的 16 个运行文件，独立宿主分别发现 3 个和 0 个插件。冻结的六份部署 DLL 通过 11 套共 2,105 项离线回归。r18 SDK 的独立插件已用包内离线依赖编译通过。[谱线和官方示例检查](D:/riz-m2/b15-r17-chart-import-20261123/validation.json) · [r18 交付回执](D:/riz-m2/v5-live-fixes-r18-20261123/publication/r18-delivery-validation.json)

## r17 玩家包与实机准备（11-23）

**自制谱实测请使用 1.1.1 的单音符插件。** 旧测试谱的谱线只有一个点。Steam 版本按相邻两个点创建线段，随后在段上创建音符，所以旧包虽然声明了一个 Tap，游戏实际不会生成它。r11 日志还记录了一次独立的动画跟踪故障。新版在同一画布上加了终点，第 2 拍的 Tap 落在段内，对应 4 秒音频的第 1 秒。新 ZIP 已用 r17 Basic 的组件实际加载，通过 5/5 项离线检查。实机仍需分别观察首音符画面、打击或漏判、结算和返回后的官方成绩。[单音符插件 1.1.1](D:/riz-m2/b15-postr17-one-note-20261123/20261123T120357164Z/archives/probe.r14.one-note-local.zip) · [建段分支](../work/seek-target-semantics-astra-20261108/LevelControl.Start.asm.txt#L655) · [新旧包与九张官方示例核对](D:/riz-m2/b15-postr17-one-note-20261123/20261123T120357164Z/one-note-correction.json)

部署这组三个实测插件时，用 `Move-R14LiveProbe.ps1` 的 `-ProbeReceipt` 指向这次的[新清单](D:/riz-m2/b15-postr17-one-note-20261123/20261123T120357164Z/validation.json)。游戏停在结算页时，核对工具就能读出该局数值，`oneTapResultCounters` 单列打击、漏判、全零或计数歧义。画面里是否出现音符仍需截图或录屏确认；返回选曲后再比较官方成绩。[31 种日志场景](../work/r14-live-probes-20261123/controlled/evidence-7021ac36b39746c6b2b906999f36bbe9/validation.json)

[Basic 玩家包](D:/riz-m2/r17-standard-players-20261123/downloads/RizToolbox-r17-Steam-Basic.zip) · [Runtime 纯底层包](D:/riz-m2/r17-standard-players-20261123/downloads/RizToolbox-r17-Steam-Runtime.zip) · [插件 SDK 1.3.0](D:/riz-m2/v5-live-fixes-r17-20261123/publication/author/RizToolbox-r17-Plugin-SDK.zip)

r17 包含四项修复。选曲列表还在加载时，成绩查询会返回可重试的状态。游戏时钟或命令发现控制器已更换时，其他插件会立即读到旧状态失效。四种音量设置能区分相邻的浮点值，停用插件会保留玩家最近的微调。其他模组已占用 F8 时，快捷面板会保持可见，之前保存的隐藏偏好在重新取得快捷键后继续生效。[成绩](D:/riz-m2/postr16-loading-catalog-20261123/validation.json) · [控制状态](D:/riz-m2/postr16-observation-graph-20261123/validation.json) · [音量](D:/riz-m2/b16-settings-adjacent-values-20261123/green-tests.log) · [快捷面板](D:/riz-m2/r17-standard-players-20261123/proofs/quick-panel-r16/validation.json)

冻结源码的 Release 解决方案编译为 0 错误、86 个警告。装入六份实际部署 DLL 的九套回归通过 2,048/2,048 项。原生映射沿用经比对的既有证据。用同一测试程序读取两个正式 Basic ZIP，控制状态定向回归在 r16 通过 10/16，r17 通过 16/16。Runtime 和 Basic 的 16 个核心文件逐字节相同。[九套回归](D:/riz-m2/v5-live-fixes-r17-20261123/qa/prepared-suites.json) · [r17 包内控制检查](D:/riz-m2/postr16-observation-graph-20261123/r17-actual.log) · [原生证据复核](D:/riz-m2/v5-live-fixes-r17-20261123/native-reuse.json)

实机升级目录为 [live-ready-basic](D:/riz-m2/r17-standard-players-20261123/live-ready-basic)。本次只读核对时 Steam 仍运行 r12，PID 20736。r17 Basic 的 71 个归档成员已从 ZIP 解压并逐一核对。对比游戏目录的 22 个目标，7 个相同、9 个待替换、6 个新增，归属冲突为零。待旧会话正常退出后，用该目录的安装器先执行 `-ValidateOnly`，再安装并验证自制谱首音符、结算数值、返回后官方成绩隔离及官方谱变速、跳转和恢复。[升级前核对](D:/riz-m2/r17-standard-players-20261123/live-ready.json)

F10 实测可用[独立探针包](D:/riz-m2/f10-basic-stack-20261123/RizToolbox-r17-F06-F10-Steam-Probes.zip)。旧探针遇到 Basic 默认主题会跳过按钮测试。新版识别该主题并保留其他插件的颜色层。用实际 r17 Basic 插件和受控菜单按钮验证了六次独立读回，持有层数为 1、2、3、2、1、0，对应主题色、绿、粉、绿、主题色、原色。F06/F10 正常结算和全部真实菜单验收仍待完成。[同一按钮读回](D:/riz-m2/f10-basic-stack-20261123/validation.json)

r17 SDK 作者包已使用自带依赖独立编译新插件，正式候选另通过四种离线播放切换。准备旧布局时遇到一次打包错误，r17 的构建驱动已在该步骤显式传递清单。主树也修复了省略此参数时的报错，13 个旧布局输入及现代布局的选择检查通过。此脚本修正发生在冻结后。[交付总回执](D:/riz-m2/v5-live-fixes-r17-20261123/publication/r17-delivery-validation.json) · [脚本验证](D:/riz-m2/r17-package-driver-fixture-20261123/validation.json)

标准 ZIP 已通过隔离安装、两版互换、从 r12 和 r16 升级及卸载。两个升级场景都保留了五份用户文件。包内 F8 等基础模组回归 15/15，宿主发现、启停与清理 7/7。B17 的 26 项服务回归使用了 r17 Basic 中的 Core 和 SDK。三个独立模组从作者包编译，在六个进程中通过 17 项跨插件调用、故障、撤回与恢复检查。实际 Unity 宿主中的提供者故障仍待实测。[玩家交付回执](D:/riz-m2/r17-standard-players-20261123/delivery-validation.json) · [跨插件回执](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/validation.json)

r17 封包后修复了一处插件存储通知顺序问题。用 r17 Basic 复现时，一个订阅者在回调中再次保存配置，另一个订阅者依次收到了第 5、4 次变更。主树现按插件分别排队，接收者会先看到较早的变更。存储与语言回归从 19/21 提升到 21/21，包含嵌套保存与插件停用场景。这项修复待下次打包。[r17 复现](D:/riz-m2/b16-r17-basic-qualification-20261123/public-config.json) · [修后回归](D:/riz-m2/b16-postr17-storage-notification-order-20261123/green-tests.log)

下一版还修复了 `context.Configuration` 的线程等待。插件在主线程序列化设置时，后台线程调用 `Load` 或 `Save` 现在立即返回 `WrongThread`。先前后台线程会等待主线程释放锁，若主线程又在等它，两边就会互相等待。用同一程序对照，r17 Core 为 26/27，修后为 27/27。[复现和回归](D:/riz-m2/b16-configuration-worker-20261123/validation.json)

LOCAL 目录现可提前识别会丢失声明音符的谱面，保留浏览和选择，阻止无效开局，并将具体行或音符下标返回给作者。规则按原序的相邻线点建段，末点仅作终点；末段允许音符晚于最后一点。Workshop 绑定和同 ID 重新发布时也会检查该谱，纯装饰行仍可使用。格式检查同时要求长按附加参数为数值、显式 `chartDelayMs` 为 32 位整数。九张 Steam 编辑器示例均可解析。[原生段起点与顺序](D:/riz-m2/b15-line-segment-readiness-20261123/point-order-native.txt) · [最终九项定向验证](D:/riz-m2/b15-line-segment-readiness-20261123/green-endpoint-test.log) · [Workshop 正式包对照](D:/riz-m2/b15-postr17-workshop-20261123/workshop-result.json) · [九张官方样谱](D:/riz-m2/b15-r17-chart-import-20261123/validation.json)

## r16 标准玩家包与 SDK 1.3.0（11-23）

[Basic 玩家包（成绩面板、主题、F8 快捷面板）](D:/riz-m2/r16-standard-players-20261123/downloads/RizToolbox-r16-Steam-Basic.zip) · [Runtime 纯底层包](D:/riz-m2/r16-standard-players-20261123/downloads/RizToolbox-r16-Steam-Runtime.zip) · [SDK 作者包](D:/riz-m2/v5-live-fixes-r16-20261123/publication/author/RizToolbox-r16-Plugin-SDK.zip)

开发验证另有 [Validation 包](D:/riz-m2/r16-validation-kit-20261123/downloads/RizToolbox-r16-Validation.zip)。它沿用同一套 16 个运行文件，自带五个验证示例、采证脚本和两个原生测试程序。从新目录解压后，JudgeContext 31,308 项、Kernel Entry 1,606 项通过，修改归档成员会被验证器拒绝。[Validation 交付回执](D:/riz-m2/r16-validation-kit-20261123/validation-delivery.json)

r16 已包含此前两项源码改动。官方成绩读取中途切换数据源时，SDK 要求重试；自制谱的生效和清理会按对局记录。冻结解决方案编译 0 错误、86 个警告，八套部署组件回归 1,985/1,985，候选包的四种播放切换通过，作者包可离线编译插件。原生映射矩阵沿用 r14 的 182 个场景，r16 核对了 292 份源码和 222 份证明文件。[r16 总回执](D:/riz-m2/v5-live-fixes-r16-20261123/publication/r16-delivery-validation.json) · [八套回归](D:/riz-m2/v5-live-fixes-r16-20261123/qa/prepared-suites.json)

原 V5 打包器会对标准分发清单索要 `example.panel` 等开发示例。新版按清单选择普通模组，两种玩家包的 16 个核心文件都与 r16 已验证候选相同。Runtime 新装发现 0 个模组，Basic 新装发现 3 个。隔离安装实际核对 206 项与 212 项归属。两版互换及卸载后，真实 SDK 写入的主题停用意愿仍可原样读回，用户自装文件保留，最终归属 0 项。正式宿主受控加载 7/7、包内组件回归 84/84，两套打包校验分别通过 21 种选择及拒绝案例。安装说明标注的 Python 3.10 已用最终 ZIP 验过两种布局。[最终三包与散列](D:/riz-m2/r16-standard-players-20261123/delivery-validation.json) · [安装与升级](D:/riz-m2/r16-standard-players-20261123/install-sdk-preferences/validation.json) · [宿主与包内回归](../work/r16-standard-host-check-20261123/README.md)

Steam 当前会话仍是 r12。下一次使用上面的 r16 标准包验收自制谱首音符、结算、返回及官方成绩隔离，再在实际持续推进的官方谱验证变速、跳转和恢复。F06/F10 的历史审计找到失败结算和独立的双层颜色恢复记录，普通通关与同进程停用读回仍待实机；r11/r12 原返回已登记但 SDK 未发结算的场景，r16 已在离线回放中通过。[历史实机审计](../work/f06-f10-live-coverage-20261123/HISTORICAL-AUDIT.md) · [三个版本的结果回放](../work/f06-f10-live-coverage-20261123/result-return-replay/RESULT-RETURN-REPLAY.md)

实机准备目录在 [live-ready-basic](D:/riz-m2/r16-standard-players-20261123/live-ready-basic)。71 个文件与下载 ZIP 一致。只读检查当前 Steam 安装时，22 个目标中 7 个相同、9 个待替换、6 个是新模组文件，归属冲突为 0。用原版 r12 和 r16 安装器在隔离目录实际升级，同样替换 9 个、新增 6 个。旧示例的 10 份文件、6 份用户文件及 3 条停用意愿完整保留。旧 Steam 会话正常结束后先用准备目录的安装器做 `-ValidateOnly`。[实际迁移演练](D:/riz-m2/r12-r16-basic-upgrade-20261123/validation.json) · [Steam 升级前核对](D:/riz-m2/r16-standard-players-20261123/live-ready.json)

F06/F10 的[测试模组](D:/riz-m2/f06-r16-live-probes-20261123/RizToolbox-r16-F06-F10-Steam-Probes.zip)单独分发。官方谱控制工具可用 `-WaitForReady` 等待选定对局和控制器上的操作就绪，倒计时期间不会提前用掉命令。曲内时间是否持续推进仍要看实际时钟采样。[事件与颜色实测步骤](../work/f06-r16-live-probes-20261123/README.md) · [控制命令用法](../work/r14-live-probes-20261123/checks/stock-sender/README.md)

### r16 封包后的成绩读取修复

r16 的成绩服务在两种加载状态下返回了 `TargetExpired`，一是资源已就绪而 `levelControl` 尚未出现，二是它的 `GetAllLevel` 暂未返回列表。两个问题均已用正式 Basic ZIP 的组件复现。当前源码在这两种状态返回可重试的 `NotReady`，列表出现后能读取实际成绩。Game 92/92、Data 30/30、基础模组 14/14 通过；完整 Release 解决方案增量构建 0 错误。修复待下一候选纳入。[源码验证](D:/riz-m2/postr16-loading-catalog-20261123/validation.json) · [构建和模组回归](D:/riz-m2/postr16-loading-catalog-20261123/solution-validation.json)

## r15 玩家包和 SDK 1.3.0 作者包已封存（11-23）

[Windows Steam 玩家包](D:/riz-m2/v5-live-fixes-r15-20261123/publication/player/RizToolbox-r15-Windows-Steam-install.zip) 与 [SDK 作者包](D:/riz-m2/v5-live-fixes-r15-20261123/publication/author/RizToolbox-r15-Plugin-SDK.zip) 已通过正式封包验收。玩家包完成隔离安装、卸载及 71 个成员的回读；作者包用自己的离线源成功编译模板插件。冻结树构建 0 错误、86 个警告。六份待部署 DLL 在独立进程通过 Runtime 397、AudioContent 1,053、Gameplay 253、UI/Input 81、Game 86 项，合计 1,870/1,870。正式候选 ZIP 另通过四项播放切换复验。182 个原生映射场景沿用 r14 的原始执行，已核对 292 个相同原生源文件和 222 项证明文件；本次重新执行的原生矩阵场景数为 0。[交付回执](D:/riz-m2/v5-live-fixes-r15-20261123/publication/r15-delivery-validation.json) · [五套回归](D:/riz-m2/r15-primary-qa-20261123/prepared-suites.json) · [ZIP 内播放验证](D:/riz-m2/r15-play-deployment-probe-20261123/candidate-final/playback-deployment-verification.json)

本次修复覆盖以下四个可独立复现的问题。Steam 仍运行 r12，自制谱结算、返回及官方成绩隔离，官方谱变速、跳转和恢复仍待当前包实机验证。

仓库的[插件指南](../docs/SDK.md)已与包内 SDK 1.3.0 对齐。从 Markdown 中逐字抽出面板插件的 C# 和 JSON，用正式作者包的离线源编译，再用正式玩家包的加载器成功发现并加载 `wiki.hello`。[编译记录](D:/riz-m2/r15-primary-qa-20261123/documented-plugin/checked.json) · [加载记录](D:/riz-m2/r15-primary-qa-20261123/documented-plugin-loader/loading.log)

已用正式 r14 和 r15 玩家 ZIP 在隔离目录验证中断升级。实际有两处文件内容变化：Adapter 已替换、Loader 已写入临时文件但尚未发布时终止安装器。恢复还原全部 224 个 r14 基线文件，额外文件为 0。正常升级和重装各核对 216 项归属；卸载清除 215 项并保留用户编辑的设置。5 份用户文件与插件停用意愿均保留。对 Steam 目录做的只读核对显示，r12 安装器登记的 26 个运行载荷均匹配，升级至 r15 会更换其中 19 个，旧事务待恢复数为 0。[正式包中断恢复](D:/riz-m2/b04-b19-r15-install-recovery-20261123/r15-01/validation.json) · [Steam 升级前只读核对](D:/riz-m2/r15-primary-qa-20261123/steam-r12-readonly-baseline.json)

- **自制谱结算。** 用正式 r14 ZIP 内的组件复现了正常结束却显示 `Unknown`。现在与官方谱共用结束原因规则，保留原始分数、完成度和四种判定计数。结果窗取得焦点后，宿主只读回一次该窗的数值标签；读回失败时玩家仍可返回或重试。当前主树音频与内容 1,053/1,053、游戏事件 86/86、日志关联 23/23 通过。真实 Steam 的自制谱结算页仍待观察。[修复和验证](D:/riz-m2/local-result-readback-20261123/validation.json)
- **官方谱变速与跳转。** r14 会把运行时不支持或来源已失效也报成“尚未就绪”。主树已能区分等待、不支持与失效。r12 存档中实际只执行过一次暂停请求，倒计时期间被游戏拒绝，还没有同局变速或跳转的执行结果。[定位与定向复验](D:/riz-m2/r14-stock-gap-20261123/validation.json)
- **键盘与手柄。** 首个快捷键回调切换到同名的新场景后，同批其余按键曾误触。现已核对场景身份，旧的 F8、ButtonSouth 被丢弃，新场景的新按键仍各送达一次。[复现与修复](D:/riz-m2/r14-ui-input-gap-20261123/validation.json)
- **升级中断恢复。** 用 r14 玩家包中断 r13 升级，恢复后插件目录会遗留一份完整的 Runtime 临时 DLL。修复版已在相同中断点还原 224 个原有文件，清除自己的临时文件并保留用户的同形文件、设置和停用意愿。正常升级、卸载和重装也已复验。[安装恢复回执](../work/r14-install-recovery-20261123/validation.json)

### r15 封包后的源码进展（11-23）

读取官方成绩途中，如果成绩管理器或关卡对象发生切换，SDK 会要求重试，保留上次快照的旧版标记。自制谱也已接入对局日志，插件可以按对局查询哪张谱生效、何时完成清理。重试会关联到新的一局。两项改动合并后使用同一份 Adapter 运行六套离线检查，1,507/1,507 通过。这两项尚未封包或实机验收。[合并验证](D:/riz-m2/postr15-integration-20261123/validation.json) · [成绩快照](D:/riz-m2/postr15-score-capture-20261123/evidence.json) · [对局关联](D:/riz-m2/postr15-local-run-context-20261123/validation.json)

## r14 玩家包与 SDK 1.3.0 作者包已完成离线交付验收（11-23）

[玩家安装包](D:/riz-m2/v5-live-fixes-preparation-20261122-r14/publication/player/RizToolbox-r14-Windows-Steam-install.zip) 在隔离目录完成安装和卸载，216 项逐项核验，卸载后归属文件零残留，用户文件保留。[作者包](D:/riz-m2/v5-live-fixes-preparation-20261122-r14/publication/author/RizToolbox-r14-Plugin-SDK.zip) 通过独立缓存离线模板编译，实际调用了三项 SDK 1.3 新接口。当前 Steam 会话仍在运行 r12。r14 实机自制谱首音符、结算及返回选曲、官方成绩隔离和官方谱播放控制尚待验证。[交付总回执](D:/riz-m2/v5-live-fixes-preparation-20261122-r14/publication/r14-delivery-validation.json)

本次冻结包含 1,853 个文件，SDK 1.3.0、两种谱面的共享播放入口和动画来源读取修复均已纳入。动画读取在每批记录的入口和出口核验完整发布信息，仍逐条查实际来源、返回地址和代次。128 行夹具的完整托管校验从 132 次降到 2 次。若末条记录读完后物理入口发生变化，修复版保留原游标。已结束的外部动画子调用只保留序号上界，旧序号再次出现会被拒绝。

冻结源码已重新构建 JudgeContext 和 Playback，并使用 r14 自己的 JudgeContext 完成 1,050/1,050 项音频与内容、28/28 项运行时协调、10/10 项协程接收者检查。主树、冻结树和实际参与这次测试的 13 个相关源码文件逐字节一致。[冻结版动画修复与源码核对](../work/b15-shared-tween-update-20261123/r14-validation.json) · [独立快照回执](D:/riz-m2/v5-live-fixes-preparation-20261122-r14/managed-snapshot/test-result.json)

冻结树位于 `D:/riz-m2/v5-live-fixes-preparation-20261122-r14/tree`。Judge、Playback、Snapshot、Managed、Adopt、Base、Prepared 七个步骤均已成功退出，六个 Release 组件及打包输入已核对。原生验收已自然退出，182/182 个映射场景共 256,673 项检查通过。[正式准备回执](D:/riz-m2/v5-live-fixes-preparation-20261122-r14/prepared-release.json) · [原生验收回执](D:/riz-m2/v5-live-fixes-preparation-20261122-r14/native-proofs/native-proof-receipt.json) · [批读取验证](D:/riz-m2/b15-tween-source-batch-20261123/verification.json)

三份用于实机的独立插件已按 SDK 1.3.0 构建。单音符谱必须显示该局的 LOCAL 结果窗、返回新的选曲页并结束会话，才可将前后两份官方成绩摘要用于隔离验收。单看开局受理或谱名仍属未完成。官方谱的时钟和控制在单独会话观察。[插件与受控验证](../work/r14-live-probes-20261123/final-verification.json) · [实机核对脚本](../work/r14-live-probes-20261123/Verify-LiveEvidence.ps1)

从正式候选 ZIP 抽取组件后，在独立进程中复验官方谱优先、自制谱优先、原调用在途时撤回、最后一步写入失败，四项均通过。8 个部署成员与打包输入逐字节一致，46 条实际加载记录逐项核对。这四项使用测试映像。[包内四项验收](D:/riz-m2/r14-play-deployment-probe-20261123/candidate-final/playback-deployment-verification.json) · [加载记录对照](D:/riz-m2/r14-play-deployment-probe-20261123/candidate-final/prepared-identity-comparison.json)

冻结整树用正式 transport 配置构建 Release 解决方案，0 错误、86 个编译警告。另建独立测试目录，从正式打包输入直接复制六份 DLL 并核对 SHA-256，运行时 397/397、音频与内容 1,050/1,050 通过。相同六组件还通过 SDK 10/10、UI/输入 80/80、游戏事件 86/86、玩法 252/252、加载器 28/28、插件生命周期 15/15 和共享回调 212/212。玩法和加载器首轮分别缺少独立原生夹具、测试插件目录，补齐后按套复跑。[最终六组件回放](../work/b15-shared-tween-update-20261123/r14-prepared-suite.json) · [七套接口验证](../work/b15-shared-tween-update-20261123/r14-consumer-suites.json)

## 自制谱启动时的共享动画队列（11-23）

已在主树修复一处可复现的开局故障。游戏的共享动画管理器在空队列上尝试取出一个动画时，会留下正常的一对记录。旧适配器跳过了这次调用，随后读到记录便将整局的动画跟踪标成失效。新版按具体管理器保留这次调用，核对进出队列、原函数返回和每条记录；已核实的空闲帧可以释放。遇到本地动画、队列实际转移、嵌套工作或未成对的退出时，仍会读取或保留相应证据。

修复前已复现孤立记录和两个管理器互相影响。修复后，连续 1,050 个空闲帧处理 2,100 条记录，完整动画图读取 0 次，随后第一个本地动画仍能正常登记。五个定向场景和全部音频/内容回归 1,050/1,050 通过，Release 解决方案编译 0 错误。实机 r11 的日志只指出动画来源失效，还无法据此认定当时卡住的首因。下一次在 Steam 用同一张自制谱核对谱名之后的首个音符、结算、选曲返回和官方成绩。[主树回执和原始日志](../work/b15-shared-tween-update-20261123/validation.json)

## r13 之后的作者 SDK 1.3.0（11-23）

主树已启用 SDK 1.3.0。作者可在插件中读取逐动作的就绪状态、选中指定版本的曲目，并检查谱面是否具备播放材料。用已缓存 r13 SDK 1.2.0 的作者环境编译通过。同一批七个插件在新宿主全部加载，独立探针实际调用了新接口；r13 宿主在加载程序集前逐包报告版本不符。SDK 检查 10/10、分发检查 14/14、预览检查 50/50。可下载的 r13 作者包仍对应 SDK 1.2.0，1.3.0 已有独立 NuGet 包，尚待与下一版玩家包一并验收。[1.3.0 验收回执](D:/riz-m2/sdk-contract-1.3-20261123/sdk-1.3.0-validation.json) · [SDK 包](D:/riz-m2/sdk-contract-1.3-20261123/final-feed/RizToolbox.Sdk.1.3.0.nupkg)

r14 作者包、玩家包的打包入口已经可以从候选 DLL、模板及支持包清单核对版本。17 项临时夹具与两项作者分发回归通过，从生成的作者包解出模板后，三个新增 SDK API 均已编译。实物 r13 作者包与玩家包通过只读验证。尚待 r14 正式构建与实机安装。[打包工具](../work/v5-live-fixes-20261122/r14/Invoke-R14Delivery.ps1) · [验收回执](D:/riz-m2/r14-delivery-entry-20261123/delivery-entry-validation.json)

两个谱种的 Play 共用入口已经合入主树，补上了自制谱先用、官方谱后用的顺序。隔离宿主等进行中的 Play 原调用返回，才提交剩余三个官方入口。中途撤回或写入失败都不会向插件公开半套播放控制。主树重建后通过 18 个进程、300 项 Play 断言和四个生命周期检查，以新 JudgeContext 复跑六个初始化夹具也全部通过。两种谱面在同一游戏进程连续游玩仍需验证。[主树回执](D:/riz-m2/b11-local-first-main-integration-20261123/main-verification.json) · [合入清单](D:/riz-m2/b11-local-first-main-integration-20261123/main-merge-receipt.json)

## 自制谱选曲、共享播放入口与 r13 安装恢复（11-23）

r13 之后的主树已修复自制谱目录。启用目录示例后，主菜单选中的谱面能原样带到选曲页；玩家点击后会看到游戏受理或拒绝请求的具体结果。SDK 的 `SelectRevision` 限定玩家看到的曲目版本，`HasPlaybackContent` 逐张谱面标明播放材料是否齐全。同一 ID 重发后，旧按钮会报告曲目已换版，已有的有效选择得以保留。已分别复现旧界面误发开局和旧列表误选新版，修复后 UI/输入 80/80、音频与内容 1045/1045 通过。该界面的场景切换和最终请求在隔离宿主验证，Steam 的自制谱结算与官方成绩比对仍待实测。[目录与 SDK 验证](../work/b15-catalog-screen-20261123/validation.json)

r13 正式玩家 ZIP 完成 6/6 项升级与恢复检查。实际 r12 包升级过程中锁住第四份 DLL，安装器自动还原前三份；主动中断隔离安装后，恢复命令还原了 216 项文件归属。修改过的设置和插件停用意愿在卸载重装后仍保留。[r12 到 r13 安装回执](D:/riz-m2/b04-b19-r13-install-recovery-20261123/attempt-3/validation.json)

官方谱先取得播放入口的方向已合入主树。独立原生夹具通过普通 SDK 目录请求进入同一个 Play，LOCAL 回调识别一次，原函数执行一次；撤回 LOCAL 后官方原函数仍能调用。主树重建通过 14 个子进程、300 项 Play 断言、19 项 Update 断言。反向顺序仍缺完整安装组，其中包括处理正在执行的 Play、核对两个初始化入口。实机的曲内变速、跳转以及两种谱面连续游玩均待验证。[主树共享入口验证](D:/riz-m2/b11-official-first-main-integration-20261123/final-verification.json)

## r13 作者开发包与下一版播放控制

[r13 玩家安装包](D:/riz-m2/RizToolbox-r13-Windows-Steam-install.zip) 已通过隔离安装与卸载。已核对 216 个安装文件，卸载保留用户文件。正式候选通过 182 个原生场景；ZIP 内组件通过游戏事件 86/86、加载器 28/28、分帧提交 23/23 场景以及新版官方谱测试插件 15/15。下一次实机优先在同一张官方谱确认时间推进，然后验证暂停、变速、跳转与恢复；自制谱另开会话查结算和成绩隔离。[发行验收](../work/b15-live-local-crossscene-20261123-r13/delivery/r13-delivery-validation.json) · [官方谱插件验收](D:/riz-m2/b11-stock-observation-r13-20261123/zip-host/validation.json)

[r13 作者开发包](D:/riz-m2/RizToolbox-r13-Plugin-SDK.zip) 已生成。在中文路径解压后，模板能从空缓存离线还原并编译插件。六个独立插件通过九项宿主检查。r13 已从冻结源码构建六个发布组件，音频与内容回归 1045/1045；当前 Steam 安装仍是 r12。[开发包验收](../work/b15-live-local-crossscene-20261123-r13/delivery/author/validation.json) · [冻结构建](../work/v5-live-fixes-20261122/r13/prepared-release.json)

r12 开场时暂停已有方法支持，但实际倒计时阻止执行。r13 封包后合入的 `StateReadiness` 给插件提供共享的逐动作状态和拒绝原因，执行时仍两次读取实时条件。主树还修正了官方播放控制正常撤回后误报未知的问题，并能区分自制谱动画的具体物理源点和回调阶段。在同一隔离组合中，玩法 252/252、入口与回调 212/212、示例控制命令 29/29 通过；六个独立进程验证了撤回、在飞原调用与异常保留。这些修改用于 r13 之后的版本。两类谱面切换所需的共享入口仍在开发。[合并回执](../work/b11-control-readiness-20261123/main-merge/receipt.json) · [组合验证](../work/b11-control-readiness-20261123/integration.json)

合入后的主工作目录已重新构建。主树 DLL 通过玩法 252/252、入口与回调 212/212、示例命令 29/29，六个撤回场景也已用实际主树 DLL 各跑一次。[主树复验](../work/b11-control-readiness-20261123/main-merge/validation.json)

## r12 插件开发包与播放控制（11-23）

**下一候选 r13。** 物理播放组件的提交现分散到 20 个宿主帧，最后一帧才一起启用公开的音高、位置写入与播放控制。23 个隔离进程通过 3463 项检查，包含准备中撤回、提交中原调用和部分写入后失败。另修正了只有浏览数据的自制谱请求会提前触发原生准备的问题：公开选曲接口会先说明缺失的播放元数据，后续宿主帧保持未安装，提供了完整数据的选曲仍走原准入。音频与内容回归首轮 1039/1045，通过补齐本地测试用原生 DLL，余下六例在同一托管程序集分别通过。[分帧构建与回归](../work/b11-r12-clock-commit-frames-20261123/rebased-results/summary.json) · [自制谱请求的红绿及逐项复验](../work/b15-local-invalid-request-20261123/validation.json)

**r12 之后的曲内时钟修复已合入。** 公开读取复用宿主已准备的时钟配置。在相同的隔离映射组中，两次读取的完整组核对从 23 次降至 0 次，运行时通道仍实际核对 4 次；耗时从 16.19 秒降至 25.8 毫秒。两次读取之间损坏入口时，下一次读取会拒绝。另在固定夹具中，空闲原调用的分配从每次约 1.19 MB 降至约 6.7 KB，真实 DLL 路径仍逐次读取，原值逐位一致。Steam 当前 r12 尚未装入这两项修复。[时钟修复与前后验证](../work/b11-r12-stock-check-20261123/clock-read-repair/clock-read-validation.json) · [空闲原调用及合入](../work/b11-r12-post-withdraw-20261123/verification.json)

已从 [r12 玩家安装包](D:/riz-m2/RizToolbox-r12-Windows-Steam-install.zip) 安装到 Steam。26 个载荷均匹配，安装事务已结束。独立目录验证了 216 个文件的安装与卸载，保留用户文件。候选 ZIP 的 58 个条目已回读，182 个原生场景通过。[发行验收](../work/b15-live-local-crossscene-20261123-r12/delivery/r12-delivery-validation.json) · [Steam 安装记录](../work/v5-live-fixes-20261122/r12/live-install.json)

插件作者可以下载 [r12 开发包](D:/riz-m2/RizToolbox-r12-Plugin-SDK.zip)。已在新模板目录和新 NuGet 缓存中，仅使用 ZIP 内的模板与依赖生成、还原并编译 `HelloScene`。输出插件清单与程序集匹配，编译 0 警告、0 错误。独立插件宿主通过 9 项检查。[开发包与离线验证](../work/b19-r12-plugin-sdk-20261123/validation.json)

r12 已把自制谱运行时的准备推迟到第一次播放请求。官方成绩的五道保护仍从启动起生效。动画初始化和来源检查现在保留具体的首次错误。冻结源码通过 1045 项音频与内容检查，六个托管发布组件已构建。[冻结构建](../work/v5-live-fixes-20261122/r12/prepared-release.json)

官方谱实测插件通过公开 SDK 每隔五秒读取两次相邻的时钟样本，在控制状态变化时立即再采样。这样可以核对倍率和位置实际读回，即使控制项一直显示可用。旧插件在控制状态不变时丢失后续样本，新插件通过全部 8 项隔离检查。[新旧插件对照及测试用宿主](../work/b11-r12-stock-check-20261123/validation.json)

r12 在 Steam 中进入了《Pastel Lines》EASY。菜单准备完成后，官方谱插件先报告等待开局，开始同一局后报告等待暂停。开场请求暂停时游戏倒计时尚未结束，SDK 返回了这一具体原因。实机没有执行变速或跳转。首次准备期间适配器单次耗时 112.25 秒。入曲后首次插件帧回调另耗 39.32 秒，相邻七个宿主 tick 的歌曲进度均为 0.042 秒。时钟读取栈经过公开 Clock.Read、时钟配置刷新及运行时代码核验。停用探针获得宿主确认，后续宿主 tick 已恢复推进；仍需核实修正后的歌曲实际推进、暂停、变速和跳转。[开局、时钟采样与耗时](../work/b11-r12-stock-check-20261123/live/20261123T041248273Z-59ab7e59fbcc47ea9d6bf5ba17487d93/song-first-read.json) · [公开时钟读取栈](../work/b11-r12-stock-check-20261123/live/20261123T041248273Z-59ab7e59fbcc47ea9d6bf5ba17487d93/song-first-stack.txt) · [停用回执](../work/b11-r12-stock-check-20261123/live/20261123T041248273Z-59ab7e59fbcc47ea9d6bf5ba17487d93/disable-command.json)

官方谱优先时，自制谱请求现在先一起检查并预留两个共享入口，冲突会在写入任一入口前返回。此修复已合入 r12 之后的主树。含 B01/B04 的相同源码重新构建成功，公开选谱接口返回原冲突并释放借用的检查通过。官方谱和自制谱在同一进程内顺序游玩仍待实现及实测。[合入记录](../work/b11-local-stock-coexistence-20261123/main-merge-20261123T042018885Z/receipt.json) · [组合验证](D:/riz-m2/r13-managed-integration-b11-20261123/checks-20261123T042735390Z/verification.json)

首次安装播放控制时已合并同一次提交前的重复核对。受控完整安装的镜像核对从 2492 次降至 2338 次，安装后的独立全量检查维持 85 次；实机耗时要用新包重测。下一次实测的独立插件也已修正：当一次读时钟超过半秒，后续帧给游戏留出恢复时间；显式控制命令优先执行，命令后的采样取下一次观察。旧实测包已复现反复读取，新插件在独立宿主通过 15 项检查，共用命令收发通过 28 项。[安装修复合入](../work/b11-r12-clock-install-stall-20261123/main-merge/merge-receipt.json) · [安装前后对照](../work/b11-r12-clock-install-stall-20261123/proof-summary.json) · [新插件包验证](../work/b11-stock-observation-recovery-20261123/validation.json) · [命令收发回归](../work/b11-stock-observation-recovery-20261123/mailbox-regression.log)

**下一候选的两处修复已合入主树。** 若结算界面没有被轮询采到，插件现可从同一局游戏实际返回的结果取得分数和判定。覆盖了控制器在两次轮询之间退出、两次分数读取不一致、元数据中途替换，以及仅切换场景而没有结算数据。启停命令也已补上请求 ID、意愿是否已记录和实际状态，可区分等待能力与真正拒绝。r12 封包后合入的 10 个源码与测试文件已逐项回读。相同源码的 Release 解决方案编译 0 错误，单局事件 86/86、宿主 28/28、包生命周期 15/15、玩法接口 247/247 通过。[单局事件补丁验证](../work/b01-r12-result-sampling-20261123/validation.json) · [联合构建与主树对照](../work/b01-b04-main-20261123/integration.json)

## r11 官方谱暂停、恢复和时钟冲突（11-23）

独立 SDK 插件在《Pastel Lines》EASY 的同一局发出暂停和恢复。游戏显示了暂停菜单，同局分别读到两次暂停状态、两次恢复播放状态。一次长按记录到启动和自然结束。游戏返回选曲页，插件正常停用，Steam 退出码 0，两个测试文件已归档。[实机回执](../work/b11-r11-stock-check-20261123/live/20261123T025755853Z-f350e869e0ce413880c0e006e8f3446d/result.json) · [原始事件](../work/b11-r11-stock-check-20261123/live/20261123T025755853Z-f350e869e0ce413880c0e006e8f3446d/events-final.jsonl)

事件 49 显示变速和跳转在开局前就已停用。当时只有后台播放与官方谱测试两个插件注册，事件 31–33 却已安装完整本地谱面运行时。现场只读核对五项时钟函数摘要，唯一与固定映像不同的是 `ProgressControl.Update`，该函数也是本地谱面初始化时绑定的对象。两个磁盘副本的五项摘要全部匹配。现有 r11 不能用来验证真实变速或跳转。[五项实机对照](../work/b11-r11-stock-check-20261123/live/20261123T025755853Z-f350e869e0ce413880c0e006e8f3446d/live-clock-digests.json) · [磁盘对照和拒绝原因回归](../work/b11-r11-clock-body-20261123/verification.json)

## r11 单音符自制谱 Steam 实测（11-23）

r11 已装入 Steam，26 个载荷均匹配。游戏返回首帧；主菜单请求在 1.35 秒后返回可重试的未就绪，选曲页沿用同一张谱，在 17.88 秒后受理。游戏显示了谱名 `B15 One-note LOCAL`。受理后 6.11 秒出现第一条故障，SDK 的最后一条事件为 74。屏幕停在谱名画面，直到正常退出。[Steam 安装回执](../work/v5-live-fixes-20261122/r11/live-install.json) · [实机回执](../work/b15-live-local-crossscene-20261122-r11/live/20261123T024707842Z-eaeacebf6bc24a0c999e5b1f502cc82e/result.json)

事件 68 指向动画来源上下文 `PublishedTweenSource / SourceContextUnknown`（4/5）。此码涵盖多个内部检查，事件 67 的托管初始化消息也有多个写入来源；两者尚不能确定最先触发的分支。开局前独立插件通过公开接口读取了 444 条官方成绩，已保存摘要并撤回插件。同局未返回选曲页，暂缺赛后对照、本地结算及会话释放证据。Steam 记录退出码 0；七个临时插件文件已移出并核对。[赛前摘要](../work/b15-r11-score-isolation-20261123/live/20261123T024707842Z-eaeacebf6bc24a0c999e5b1f502cc82e/before.json) · [原始事件](../work/b15-live-local-crossscene-20261122-r11/live/20261123T024707842Z-eaeacebf6bc24a0c999e5b1f502cc82e/events-final.jsonl) · [正常退出](../work/b15-live-local-crossscene-20261122-r11/live/20261123T024707842Z-eaeacebf6bc24a0c999e5b1f502cc82e/steam-exit.log)

## r11 发行包验收（11-23）

插件开发者可使用 [r11 开发包](D:/riz-m2/RizToolbox-r11-Plugin-SDK.zip)。包内附有 SDK 1.2.0、本地还原源、插件模板和中文入门说明。已从该 ZIP 在独立模板目录生成 `wiki.hello`，离线还原并以 Release 编译出 `HelloScene.dll` 和插件清单，0 错误、0 警告。[开发包构建与验证](../work/b19-r11-plugin-sdk-20261123/validation.json)

六个 Release 组件已构建。冻结源码的音频与内容检查通过 1044/1044。原生验证已重新跑完 182 个场景，合计 256,673 项检查通过。首次打包发现一份静态校验记录仍引用 r10 源码；重新生成该记录后，候选 ZIP 的 58 项内容全部通过回读。独立插件宿主的 9 项验收已核对到 ZIP 内六个 DLL 的实际字节，单音符插件的 3 个受控场景也已直接用包内 DLL 复验。[冻结构建](../work/v5-live-fixes-20261122/r11/prepared-release.json) · [原生验证](D:/riz-m2/v5-live-fixes-native-20261122-r11/native-proof-receipt.json) · [候选 ZIP](../work/v5-live-fixes-20261122/r11/candidate-validation.json) · [双插件](../work/v5-live-fixes-20261122/r11/b17-zip-validation.json) · [单音符插件](../work/b15-live-local-crossscene-20261122-r11/packaged-host.json)

## r11 首次故障定位（11-22）

自制谱开局后，r10 的两条日志只能指出原生组件、事件监听器已进入未知状态。r11 在原有 V7 回执中保留原生组件最先拒绝的检查及其细项，监听器也保留已记录的第一条错误。原生组件、托管组件的拒绝条件保持。真实 r10 会话未记录该细项，仍需用 r11 复测同一张谱。

新编译的原生 DLL 与托管 Runtime 配对实测，正常流程 425 项、重复初始化 132 项通过。用相同的测试程序换回 r10 原生 DLL，可以复现首因丢失。独立原生夹具 1178 项通过，监听器定向回归 5 项通过。当前 Steam 安装版本及上一次实机结果见下方 r10 记录。[新 DLL 对照](../work/r11-managed-first-failure-20261122/validation-20261122T155316640Z.json) · [原生检查](../work/r11-native-first-cause-20261122/mapped-20261122T154725764Z/validation.json) · [监听器检查](D:/riz-m2/r11-listener-guard-20261122/validation.json)

## r10 Steam 实测（11-22）

同一个普通 SDK 插件在主菜单保留了 `B15 One-note LOCAL` 的选曲与谱面版本。从请求到回执用 **1.30 秒**，得到可重试的 `NotReady(17)`（事件 55→56）。进入 `LevelList` 后沿用同一选曲和谱面，从请求到受理用 **17.59 秒**（事件 63→64）。受理后 2.93 秒的保护快照显示五道保护已安装、一局待释放，五个边界的调用数均为零（事件 65）。[两次请求和保护快照](../work/b15-live-local-crossscene-20261122-r10/live/20261122T151253810Z-5caf37d90d8949e1809da2a1ba87704c/result.json)

受理后 **6.28 秒**，事件 66 首次记录 listener/source 已未知。事件 67 保留了 FirstInstall 首次托管失败，`VerifyReady` 读到原生 V7 `status=0, phase=4, unknown=1`。接着记录了初始化协程归属未知。会话日志截至事件 73，未复现 r9 的三个 `Unknown complete relocation prefix` 警告。原生 V7 最先触发未知的检查仍待定位。[完整会话日志](../work/b15-live-local-crossscene-20261122-r10/live/20261122T151253810Z-5caf37d90d8949e1809da2a1ba87704c/events-final.jsonl) · [最后一份 SDK 状态](../work/b15-live-local-crossscene-20261122-r10/live/20261122T151253810Z-5caf37d90d8949e1809da2a1ba87704c/r10-state.json) · [r9 原始入口对照](../work/r9-movenext-composition-20261122/evidence.json)

屏幕显示 `B15 One-note LOCAL` 的暂停界面，手动选择一次继续后仍未出现本地结算。本地结算、成绩隔离及会话释放仍待实机确认。Steam 记录 PID 55444 于本地时间 **23:27:48** 以退出码 0 退出。五个临时插件文件已移出并归档。[Steam 退出](../work/b15-live-local-crossscene-20261122-r10/live/20261122T151253810Z-5caf37d90d8949e1809da2a1ba87704c/steam-exit.log) · [探针移出记录](../work/v5-live-fixes-20261122/r10/live-probe-retirement.json)

## r10 构建和安装（11-22）

r10 Steam 独立安装包为 **43,131,287 字节、71 个成员**，SHA-256 `3c899403eb5b00f7ed68bf798c4d8f1b69f6b2e5aa0381f54658185fc6436243`。隔离安装核对 216 个安装文件和 26 个候选载荷，卸载后受管残留为零，用户文件完整。Steam 实际安装的 26 个候选载荷均匹配，安装事务待处理数为零。[独立安装包](D:/riz-m2/RizToolbox-r10-Windows-Steam-install.zip) · [独立包校验](../work/v5-live-fixes-20261122/r10/portable-package.json) · [隔离安装和卸载](../work/v5-live-fixes-20261122/r10/portable-install/install-cycle.json) · [Steam 安装回执](../work/v5-live-fixes-20261122/r10/live-install.json)

冻结源码通过运行时协调 28/28、协程接收者 10/10、音频与内容 1043/1043。使用 r10 候选 ZIP 中的实际 DLL，入口组合定向 3/3、独立 SDK 插件宿主 3/3。候选 ZIP SHA-256 `9a7fca298050f36785f258ea63f427727176eceb1685ea28346c0603a9315a9d`。[冻结回执](D:/riz-m2/v5-live-fixes-source-20261122-r10/test-result.json) · [候选 ZIP 校验](../work/v5-live-fixes-20261122/r10/candidate-validation.json) · [组合复验](../work/r9-movenext-composition-20261122/r10-packaged-validation.json) · [插件宿主](../work/b15-live-local-crossscene-20261122-r10/packaged-host.json)

## r9 Steam 实测（11-22）

同一张自制谱在主菜单用 1.25 秒返回可重试的 `NotReady(17)`，进入选曲后用 17.60 秒收到受理回执，屏幕显示 `B15 One-note LOCAL`。随后宿主停止更新，未到结算页。首次记录的错误是事件 67 的 listener/source 状态未知；事件 68 的运行时观察器发现 FirstInstall 已处于永久未知状态。最后一条健康快照为事件 66，当时仍有一局待释放。测试游戏已正常退出，五个临时插件文件已移出。r9 保持安装。[同局回执](../work/b15-live-local-crossscene-20261122-r9/live/20261122T143513068Z-8a02e869a3f049d5ab0200ddd03a2902/result.json) · [移出记录](../work/v5-live-fixes-20261122/r9/live-probe-retirement.json)

日志还记录了三个游戏函数的入口未计入 SDK 的代码校验。已用 r9 ZIP 的 Runtime 复现三个遗漏，并在修正后验证同一夹具的三个入口全部计入。此前的日志没有保留 FirstInstall 最初的失败原因，缺少这三个入口与事件 68 之间的因果待实测。已为后续版本保留首次失败原因。[入口对照](../work/r9-movenext-composition-20261122/test-results.json) · [首次错误读回测试](../work/r10-native-first-failure-20261122/validation.json)

普通谱面的资源也已对齐。实机记录里的《Never Say Goodbye》AT 在本地资源目录唯一对应 6,567 条谱线、1,943 个谱面音符对象的文件。[曲目标识和谱面校验](../work/stock-at-catalog-map-20261122/validation.json)

## r9 主树集成（11-22）

[r9 Windows Steam 独立安装包](D:/riz-m2/RizToolbox-r9-Windows-Steam-install.zip) 为 43,129,620 字节，SHA-256 `0a9742a3857de8fcd73ab17059f0eda971e12ed3fdc365b35e9347fac8d4e52c`。冻结源码音频与内容 1043/1043。候选 ZIP 的协程 13/13、判定 20/20、独立插件 3/3 均已通过。[候选源码与六个 DLL](../work/v5-live-fixes-20261122/r9/candidate-validation.json) · [协程](../work/v5-live-fixes-20261122/r9/packaged-live-features/results.json) · [判定](../work/judgements-r9-package-checks-20261122/r9-20261122T143055718Z/validation.json) · [插件](../work/b15-live-local-crossscene-20261122-r9/packaged-host.json)

已修正普通谱面长按字段和宽谱面漏读。现在用实际派生控制器读取其直接基类声明的 `LevelInformation`。宽谱面先校验 `Chart.lines` 的后备数组，再保留原始谱线编号读取音符。主树 Release 定向回归通过 112/112，覆盖一张 6,567 条谱线的夹具及索引 4,096 之后的音符。[主树验证](D:/riz-m2/hold-wide-main-20261122T141038416Z/validation.json) · [固定谱面](../work/judgement-chart-lines-20261122-r8/fixed-input-evidence.json)

本地谱面开局已接入嵌套子协程识别。初始化迭代器首次返回 true 时，从其实际 Current 保留 `UnityEngine.WaitUntil`，核对父子谓词，并在下次 MoveNext 进入前撤回身份。Unity 实际从该父节点的 Execute、Yield 进入子 Start 时，引擎登记子节点自己的生命周期，待子节点结束后再释放父节点。固定客户端的原始调用点与 r8 实机日志已对照。完整自制谱结算、退出和成绩隔离仍待 r9 实机验收。[原始调用点](../work/b15-yield-child-proof-20261122-r8/static-evidence.json) · [合入清单](../work/b15-yield-child-proof-20261122-r8/main-integration.json)

## r8 Steam 实测（11-22）

主菜单请求在 1.53 秒后返回可重试的 `NotReady(17)`。同一个插件带同一张单音符谱从选曲页再次发起请求，SDK 在 **19.91 秒**后受理。r7 同一段耗时 118.18 秒；两者均量自请求到回执。r8 没有出现可验证的本地结算。一次较早的 Steam 启动在首帧前退出，第二次启动才获得下述会话。[r8 回执、计时与异常](../work/b15-live-local-crossscene-20261122-r8/live/20261122T135202966Z-e1352888584043bdb6d0320bd144f5cc/result.json) · [r7 对照](../work/b15-live-local-crossscene-20261122-r7/live/20261122T125037547Z-b02b8cce9fec466e94890a4fbc776016/r7-second-attempt.json) · [首次启动](../work/v5-live-fixes-20261122/r8/live-steam-attempt1.log)

选曲页回执为事件 64。事件 66 在克隆控制器上观察到嵌套 Start（深度 2），子调用 58331、父调用 58328；对应的节点作用域存在且有效，子迭代器在已登记身份中匹配数为零。事件 68 记录初始化 `WaitUntil` 的托管 Start 返回，其迭代器与出错的子迭代器不同。最后有效的保护快照为故障前的事件 65，有一条尚未结束的会话。结算、成绩隔离及正常释放均待验收。下一步沿实际调用点核实子迭代器的来源与生命周期。[会话日志](../work/b15-live-local-crossscene-20261122-r8/live/20261122T135202966Z-e1352888584043bdb6d0320bd144f5cc/events-000001.jsonl)

r8 已安装。测试游戏正常退出，五个临时插件文件已移出 Steam 目录。另外一次 r7 普通谱面实测暴露了长按字段声明及判定谱线遍历上限问题，已单独保留记录。[正常退出](../work/v5-live-fixes-20261122/r8/live-normal-exit.json) · [探针移出](../work/v5-live-fixes-20261122/r8/live-probe-retirement.json) · [普通谱面记录](../work/v5-live-fixes-20261122/r8/previous-stock-session.json)

## r8 源码与离线验证（11-22）

r8 已从独立 ZIP 装入 Steam，26 个候选载荷全部匹配，安装事务待处理数为零。隔离目录也从同一 ZIP 完成安装、校验及卸载，216 个安装文件全数匹配，卸载后受管残留为零，用户文件保留。[实机安装](../work/v5-live-fixes-20261122/r8/live-install.json) · [独立包验收](../work/v5-live-fixes-20261122/r8/portable-install/install-cycle.json)

[r8 Windows Steam 独立安装包](D:/riz-m2/RizToolbox-r8-Windows-Steam-install.zip)，43,112,288 字节，SHA-256 `420bac1e6f48a0bab03bb83db9b5106637839c97dd8aa73ed1ae862cfd1382ab`。六个 Release 组件构建成功，冻结源码通过 1038/1038 项音频与内容回归。使用本次候选 ZIP 的 DLL 又分别通过协程 56 项、读图 5 种情况、外部插件开局 3 项和 N2 播放控制 12 次运行。包内的 64 槽位读图测得 298 毫秒，仅为夹具单次用时。[候选清单](../work/v5-live-fixes-20261122/r8/candidate-validation.json) · [协程](../work/v5-live-fixes-20261122/r8/packaged-live-features/results.json) · [动画](../work/tween-read-20261122-r7/packaged-graph-proof/37e5c4e3c6981a98-6d343adb/result.json) · [插件](../work/b15-live-local-crossscene-20261122-r8/packaged-host.json) · [N2](../work/n2-staged-main-20261122/candidate-37e5c4e3c698-20261122T134140585Z/validation.json)

自制谱面开局时，动画管理器读取每个数组元素都会触发完整的原生校验。现在每次只读遍历在进入和退出时各做一次完整校验，遍历期间持续检查线程、回调活动和故障。两次遍历分别校验，并比较两张图。在同一组 64 槽位的离线夹具中，两次遍历的完整校验从 186 次降到 4 次。r7 用时 21.96 秒，最终源码两次均低于半秒。实机整局耗时另计。[r7 基线](../work/tween-read-20261122-r7/before-graph-normal.json) · [当前读图](../work/tween-read-20261122-r7/after-graph-normal.json) · [源码清单](../work/tween-read-20261122-r7/graph-main.json)

协程启动失败时，SDK 现在记录引擎侧的调用和归属，以及托管侧返回的迭代器、调用点和句柄。回调清理曾覆盖首次故障，已复现并修复。合入动画优化前，本轮 1038 项音频与内容回归全部通过。r7 未记录足以确认该局首因的信息，仍需在新版本重测本地结算、成绩隔离及正常退出。[失败复现](../work/b15-inline-start-diagnosis-20261122-r7/managed-return/first-cause-before.log) · [修复后](../work/b15-inline-start-diagnosis-20261122-r7/managed-return/final-result.json) · [当前回归](../work/b15-inline-start-diagnosis-20261122-r7/managed-return/full-final.json)

播放控制首次按需接入已经分帧。撤回需求的三个帧内，已准备的对象和文件保持原样；恢复后在原主线程完成一次提交。主树通过 9 个原生夹具场景共 535 项、Gameplay 247 项、正式播放控制宿主 7 项、时钟就绪检查 14 项。完整变速与跳转仍待验收。[N2 主树回执](../work/n2-staged-main-20261122/main-20261122T133148907Z/validation.json)

## r7 独立安装包与 CutOut 修正（11-22）

本轮两次开局请求、首次故障和退出状态已归档。[r7 实机完整回执与原始日志索引](../work/b15-live-local-crossscene-20261122-r7/live/20261122T125037547Z-b02b8cce9fec466e94890a4fbc776016/result.json)

Steam 实机已接受选曲页发出的单音符自制谱面请求，屏幕出现游玩血条和判定环。主菜单先返回可重试的 `NotReady`，两次请求使用同一选曲。选曲页从发出请求到接受耗时约 118 秒。[选曲页原始回执](../work/b15-live-local-crossscene-20261122-r7/live/20261122T125037547Z-b02b8cce9fec466e94890a4fbc776016/r7-second-attempt.json) · [游玩界面观察](../work/v5-live-fixes-20261122/r7/live-hud-observation.json)

本次未产生可验证的本地结算。两次主线程采样均显示，读动画数组时在逐项重复验证来源，同期约 106 秒才有下一次宿主进度上报。随后 SDK 在记录初始化协程 Start 时无法确认对象归属，事件 77 起宿主失去有效观察，最后上报的 tick 为 11858。此前事件 66 报告一条待结束会话，五个结算边界计数均为零。已正常关闭本次游戏并归档测试插件，未确认会话正常释放或成绩隔离。下一步分别修复动画读取拖慢主线程的问题，以及协程归属错误，再重测本地结算与返回。[主线程采样一](../work/v5-live-fixes-20261122/r7/live-managed-stack.log) · [主线程采样二](../work/v5-live-fixes-20261122/r7/live-managed-stack-2.log) · [首次协程异常](../work/b15-live-local-crossscene-20261122-r7/live/20261122T125037547Z-b02b8cce9fec466e94890a4fbc776016/r7-launch-fault.json) · [测试退出](../work/v5-live-fixes-20261122/r7/live-normal-exit.json) · [探针移出](../work/v5-live-fixes-20261122/r7/live-probe-retirement.json)

[Windows Steam 独立安装包](D:/riz-m2/RizToolbox-r7-Windows-Steam-install.zip) 已装入 Steam 游戏目录，26 个载荷均与清单一致。六个 Release 组件已构建，冻结源码通过 1035/1035 项音频与内容检查。直接使用候选 ZIP 中的 DLL 另验了 18/18 项 CutOut 检查，其中包含验证 UI RectTransform 的回归用例。该包只改变本地谱面开局时的 CutOut 类型检查和诊断。[安装包信息](../work/v5-live-fixes-20261122/r7/portable-package.json) · [包内验证](../work/v5-live-fixes-20261122/r7/packaged-live-features/results.json) · [Steam 安装](../work/v5-live-fixes-20261122/r7/live-install.json)

## Steam 实机，r6 主菜单拒绝后在选曲页重试（11-22）

r6 独立 ZIP 也已用包内原始安装器在新目录完成安装、校验及卸载。216 个文件哈希全部匹配，其中含 26 个候选载荷。卸载后归属文件和待处理事务均为 0，三个原始文件和两个模拟用户文件的内容一致。[完整安装卸载演练](../work/v5-live-fixes-20261122/r6/portable-install/direct/install-cycle.json)

同一普通插件在两个页面请求了同一张自制单音符谱面。主菜单返回可重试的 `NotReady(17)`，保留当前选曲，未结束会话为 0。在选曲页再次请求时，SDK 找到了歌曲卡片和控制器，进入关卡准备。此后在读取 CutOut 组件时报错，返回 `InternalError(28)`，有 1 个会话的清理状态未知。五个结算边界调用数均为 0。本次没有进入打歌与结算。已正常关闭测试游戏并移出五个探针文件。[两次实机回执及完整栈](../work/b15-live-local-crossscene-20261122-r6/live/20261122T122025744Z-e66e0dcddbab4995a5d8c2ef146c26cd/result.json) · [正常退出](../work/v5-live-fixes-20261122/r6/live-normal-exit.json) · [探针归档](../work/v5-live-fixes-20261122/r6/live-probe-retirement.json)

异常栈中 r6 包内的回调明确对应根引用 3，即 `GameObject.get_transform`。Steam 选曲页的卡片引用 `NormalModeController`，其 `cutoutMaskEventTrigger` 位于 `CutOut`，实际组件是 `RectTransform`。修正已纳入 r7 源码，仅该处同时接受 Unity 的普通 Transform 和 UI RectTransform。下一次实测需验证开局、单音符结果、返回及会话释放。[包内回调](../work/v5-live-fixes-20261122/r6/cutout-fault-callback.json) · [Steam 原始资源引用链](../work/v5-live-fixes-20261122/r6/stock-cutout-prefab.json)

## r6 候选，主菜单缺少歌曲卡片时可重试（11-22）

[r6 独立安装包](D:/riz-m2/RizToolbox-r6-Windows-Steam-install.zip) 已构建。SDK 先查找活动歌曲卡片提供的控制器，再预留本地游戏会话。若扫描完成、没有合适卡片且所有临时引用已释放，返回可重试的 `NotReady`。找到预制体时沿用刚刚验证过的同一引用。包内还包含长按生命周期诊断，以及播放控制初始化前的可取消后台文件读取。

此次冻结源码的音频与内容回归 1034/1034。使用 r6 ZIP 中的正式 DLL 通过 56 次独立进程检查，包括歌曲卡片与开局 31 项、长按 24 项、Gameplay 完整套件 247/247。[本次包内检查](../work/v5-live-fixes-20261122/r6/packaged-live-features/results.json) · [独立安装包信息](../work/v5-live-fixes-20261122/r6/portable-package.json)

## V5 独立安装包与本地谱面开局修正（11-22）

下一候选的主树已接入长按生命周期诊断。每局分别记录首次已取得有效句柄并退出的 Start，以及首次自然退出或故障，可用运行和记录序号对应。观察到两个成功事件后，后续采样跳过该局的记录扫描。主树的 24 个定向检查已通过。该项在 r5 封包后合入，尚待后续安装包实机验证。[主树测试](../work/hold-lifecycle-diagnostic-20261122/integrated/results.json)

r5 在 Steam 主菜单再次用同一张单音符谱面点击开局，已取得实际判定列表并进入 `Begin`。下一步需要从活动歌曲卡片取得控制器预制体。主菜单此时没有歌曲卡片，请求被拒，保护宿主已有 1 个未完成会话并将清理状态标为未知。五个结算边界的调用次数均为 0。已正常退出测试游戏，随后移出五个探针文件。接下来在预留游戏会话前检查歌曲卡片，让玩家能够进入选曲页后重试，并从选曲页实测开局、结算和记录隔离。[单次实机请求、拒绝和清理状态](../work/b15-live-local-song-20261122/live/20261122T114645455Z-50cef71877c547ffbdc0ff5610408651/r5-live-result.json) · [进程退出](../work/v5-live-fixes-20261122/r5/live-normal-exit.json) · [探针移出](../work/v5-live-fixes-20261122/r5/live-probe-retirement.json)

当前 Steam 版已经安装 [r5 独立安装包对应的候选](D:/riz-m2/RizToolbox-r5-Windows-Steam-install.zip)。r5 的音频与内容回归 1032/1032，六组件 Release 已构建，使用本次 ZIP 的 DLL 再跑了 19 项开局来源与隔离检查。独立安装包也单独通过从压缩包解压、校验、安装到中文目录和卸载。Steam 安装的 26 个载荷匹配，进程 16256 的首个 Unity 宿主更新已返回，主菜单的 LOCAL 保护已安装。前两次启动均在首帧前退出，Steam 记录退出码 0。[包内检查](../work/v5-live-fixes-20261122/r5/packaged-live-features/results.json) · [独立包安装与卸载](../work/v5-live-fixes-20261122/r5/portable-install-cycle.json) · [Steam 安装](../work/v5-live-fixes-20261122/r5/live-install.json) · [首帧](../work/v5-live-fixes-20261122/r5/live-first-tick.json)

[r4 独立安装包](D:/riz-m2/RizToolbox-r4-Windows-Steam-install.zip) 已包含候选文件、安装与卸载脚本、固定版本的运行库和中文说明。把 ZIP 解压到新的中文路径后，从包内运行安装检查、实际安装和卸载，26 个候选文件及 216 个安装归属文件均通过哈希核对。卸载后归属文件剩余 0 个，用户文件和三个原游戏文件完整。此轮使用隔离目录。[隔离安装与卸载回执](../work/v5-live-fixes-20261122/r4/portable-install/install-cycle.json)

已在主树补上官方开局前的 IL2CPP 判定设置类初始化。原生运行时负责构建列表，SDK 继续查验列表及所选难度。隔离源码完成 19 项定向检查。r4 的下方实机回执保留为修前复现。[修复与原生对照](../work/local-judge-initialization-20261122-r5/merge-manifest.json) · [定向验证](../work/local-judge-initialization-20261122-r5/targeted-checks.json)

## r4 已安装，Steam 实测通过 LOCAL 启动与双插件音量恢复（11-22）

在新的 Steam 进程 30936 中，普通插件成功注册并选中一张自制单音符谱面。在主菜单点击一次开局后，SDK 返回 `NotReady(17)`，原因是 `InLevelSettings.JudgeDataList is not ready.`。请求在创建本地游戏控制器前结束。下一步核对游戏初始化判定列表的时机，再验开局、结算和记录隔离。测试插件已停用，未结束的本地会话为零，五个测试文件已校验并移出游戏目录。[实机请求与回执](../work/b15-live-local-song-20261122/live/20261122T111203641Z-53f9de4917cd47b6ae6f39b925c789e3/result.json) · [测试插件归档](../work/b15-live-local-song-20261122/live-retirement.json)

当前 Steam 安装为 [r4 候选](D:/riz-m2/RizToolbox-0.3.0-V5-live-fixes-candidate-20261122-r4.zip)，26 个载荷的哈希与 ZIP 一致。进程 3568 的首个宿主更新已返回。判定服务先就绪，随后本地谱面保护的五个边界全部安装，开始接受新局。完整的本地谱面开局、结算与记录隔离仍待实测。[安装回执](../work/v5-live-fixes-20261122/r4/live-install.json) · [LOCAL 启动顺序与前版对照](../work/v5-live-fixes-20261122/r4/live-local-startup.json)

声音设置页的背景音乐原值为 100%。两个普通插件按优先级依次设置为 99%、98%，撤回高优先级插件后读回 99%，再撤回低优先级插件后读回 100%。此时覆盖持有者为零，界面也显示 100%。第一次尝试早于设置页范围发布，在插件启用前被拒绝。范围就绪后，同一进程完成全程。两个测试包已移出游戏目录。其余三个音量字段已通过原生代码核对及离线测试。[真实读写与恢复](../work/b16-live-volume-pair-20261122-r1/live/20261122T110010311Z-2ec0bbe0e7044019bbac18499000bbca/ready-r2/execution.json) · [界面复核与清理](../work/v5-live-fixes-20261122/r4/live-settings.json)

用 ZIP 中的程序集执行了 40 次独立进程检查，包含长按发现 16 项，以及播放安装的 20 个场景、516 项断言。长按仍需用真机谱面触发核对；变速和跳转的初始化仍有耗时风险，实机流畅度与完整带值操作均待验。B01–B19 仍开放。[包内验证](../work/v5-live-fixes-20261122/r4/packaged-features-verified.json)

## Steam 候选 r3 与本地谱面、音量和长按的验收进度（11-22）

Steam 已安装 [r3 候选](D:/riz-m2/RizToolbox-0.3.0-V5-live-fixes-candidate-20261122-r3.zip)，26 个运行文件通过哈希核对。进程 25208 的首个 Unity 更新已返回。LOCAL 成功解析静态帧数方法，五个原生边界均已安装。接入判定进度与时间线时仍未取得就绪确认，宿主关闭了本地谱面入口。完整本地谱面开局、结算隔离和清理待验。[安装](../work/v5-live-fixes-20261122/r3/live-install.json) · [首帧与 LOCAL 日志](../work/v5-live-fixes-20261122/r3/live-local-startup.json)

声音设置页的背景音乐为 100%，SDK 读取为 1，写入描述却给出滑条范围 0–100。两只测试插件在只读阶段停下，均已停用并移出游戏目录。音量恢复测试须先统一 getter、setter 与范围的单位。[命令及终态](../work/b16-live-volume-pair-20261122-r1/live/20261122T101102554Z-811a022c48c0496b92dd313f8a8f775c/execution.json) · [临时插件移出记录](../work/b16-live-volume-pair-20261122-r1/live-r3-retirement.json)

正式源码现已能读取实际类型为普通模式控制器数组的长按候选，并保留原有数组和元素校验。16 项定向测试通过。r2 实机记录的是数组类型检查失败，尚未取得其实际元素类型。本项改动在 r3 打包后合入，等待下一包实测。[实机异常](../work/hold-discovery-live-20261122/r2-hold-capture.json) · [源码回归及异常类型诊断](../work/hold-discovery-live-20261122/integrated-diagnostic-results.json)

正式源码已接入两项后续修复。LOCAL 会等待判定服务就绪，模块定向 9/9、诊断 5/5。四个音量字段以 0–1 连续值读写，双插件停用后恢复原值，设置回归 53/53。两项均等待新包实机验收。[LOCAL 合入记录](../work/local-admission-live-20261122-r4/active-integration.json) · [音量合入记录](../work/b16-volume-units-20261122-r4/active-integration.json) · [音量回归](../work/b16-volume-units-20261122-r4/green-tests.log)

## Steam 实测通过「切出窗口继续播放」（11-22）

当前安装为 [r2 候选](D:/riz-m2/RizToolbox-0.3.0-V5-live-fixes-candidate-20261122-r2.zip)。变速和跳转按插件需求初始化，Steam 进程 23396 的首个 Unity 更新正常返回。`riz.background-play` 已开启，选择已保存。 [安装核验](../work/background-play-20261122/live-install-r2.json) · [首帧](../work/background-play-20261122/live-r2-first-tick.json) · [开关说明](../plugins/BackgroundPlay/README.md)

《Greatness Earth》EASY、FREE 模式下，切到 Steam 后约 110 秒到达结算，期间未观察到暂停。无输入，结算页与 SDK 均为 479 Miss。重开同谱，关闭插件后切到 Steam，游戏进入暂停菜单。在暂停页重新启用插件，游戏保持暂停；点击「继续」后出现倒数并完成同一局。用户使用实体键确认手动暂停正常。挑战模式和后台音量未做实机验收。 [开关对照](../work/background-play-20261122/live-r2-ab.json) · [原始运行事件](../work/background-play-20261122/live-r2-ab.log)

同包的 LOCAL 首个失败已定位为解析静态 `Time.get_frameCount` 时使用了实例方法查询。两个独立插件在同一 Steam 进程通过全部 8 步。双方调用成功，提供者撤回后旧引用失效，重新启用后消费者取得新引用，两个插件均正常停用。两份临时包已移出游戏目录。完整 Speed/Seek、成功通关及 B01–B19 总体验收仍开放。 [包内回归](../work/v5-live-fixes-20261122/r2/packaged-fixes-validation.json) · [双插件实测](../work/live-service-pair-20261122-r1/live/20261122T093114040Z-fac3b7e5b8694347b138320356f180a9/rw-jsonl-r2/validation.json)

## 后台播放插件已加载，首帧实测暴露播放控制初始化卡顿（11-22）

Steam 版已安装包含后台播放服务的 r1 候选，26 个运行文件与包内哈希一致。普通 Mod `riz.background-play` 在实际 Unity 窗口上启用成功。窗口消息回归 7/7，覆盖失焦、恢复前台、按键释放和停用清理。实机在标题画面初始化时，普通模式即使没有插件请求播放控制也同步准备了变速和跳转。两次主线程采样均落在这条调用链。该候选尚未验证打歌过程中切出窗口；后续使用按需初始化的候选重测。 [安装回执](../work/background-play-20261122/live-install.json) · [实机首帧](../work/background-play-20261122/r1-startup-stall.json) · [调用栈](../work/background-play-20261122/live-startup-managed.stack.log) · [窗口测试](../work/background-play-review-20261122-r1/checks.log)

## Steam 实机已完成 Unity 首帧和一次失败结算（11-22）

B17 候选先在隔离目录安装，再安装到 Steam 客户端；两个目录都逐文件匹配 26 项载荷。原安装已备份 259 个文件。Steam 启动后，进程 22404 的宿主首个 Unity 更新返回，游戏数据模块读到 444 张谱面与 444 条记录。[安装](../work/b17-service-fault-dispatch-20261122/live-install.json) · [首帧](../work/b17-service-fault-dispatch-20261122/live-first-tick.json)

《Greatness Earth》EASY 在 FREE 模式全程无输入到达结算。游戏显示 0 分、479 Miss，SDK 发布相同计数并将结算绑定到实际开局的 RunId，结束原因是 Failed。结算页重开后观察到新的 RunId，随后切出游戏触发暂停。成功通关仍待验。此包的可选播放控制遇到框架本身的 Unity 窗口校验冲突，LOCAL 终止保护也未就绪。[首局对照](../work/b17-service-fault-dispatch-20261122/live-first-run.json) · [会话日志](../work/b17-service-fault-dispatch-20261122/live-first-run.log)

## 最新：异步服务故障在普通队列饱和时仍由宿主交付（11-22）

工作线程按原服务登记记录一次故障，宿主每帧在普通主线程任务之后交付。停用或撤回会移除失效登记，迟到故障不会报告给下一代插件。测试先在原实现复现普通队列饱和时漏掉停用和撤回通知，改后 Services 26/26、Core 26/26、Hosting 24/24、PackageLifecycle 15/15、Loader 24/24。Release 解决方案 0 错误、66 条警告。 [验证结果](../work/b17-service-fault-dispatch-20261122/validation.json) · [解决方案日志](../work/b17-service-fault-dispatch-20261122/solution.log)

当前 [V5 候选](D:/riz-m2/RizToolbox-0.3.0-V5-b17-candidate-20261122-r1.zip) 已验证 58 个归档成员。直接使用 ZIP 内的 DLL 通过 Services 26/26、Core 26/26、PackageLifecycle 15/15。两个独立插件引用同源 [SDK 包](D:/riz-m2/b17-independent-20261122-r1/feed/RizToolbox.Sdk.1.2.0.nupkg)，经包内 Core、Hosting 和 SDK 通过 9/9，覆盖依赖、故障传播、服务撤回和同名配置迁移。游戏内首帧、起播、设置恢复、Speed/Seek 和 B01–B19 整体验收仍待核对。 [归档核验](../work/b17-service-fault-dispatch-20261122/candidate-validation-r1.json) · [包内回归](../work/b17-service-fault-dispatch-20261122/packaged-service-validation.json) · [独立包结果](D:/riz-m2/b17-independent-20261122-r1/result.json)

## 最新：独立插件依赖、服务撤回与故障归属（11-21）

跨插件调用保留原服务代次及消费者作用域。消费者停用或服务撤回会结束在途异步等待，旧登记的迟到故障不会停掉重启的新实例。六个改前问题已复现，当前 Services 25/25、Core 26/26、Hosting 24/24、PackageLifecycle 15/15。两个独立插件通过正式包加载器完成依赖与配置组合 9/9，标准模板通过 14/14。 [验证记录](../docs/validation/b17-service-lifetime-20261121/README.md) · [汇总](../work/b17-service-lifetime-20261121/validation.json)

本批进入源码，当前 V5 候选仍为下方 B16 包。继续核对主线程队列饱和时的异步故障交付，再用当前 Core 复验包分发。真实游戏设置、Speed/Seek 和 B01–B19 整体验收保持开放。

## 最新：配置保存和迁移保留原文件及作用域（11-21）

插件停用后，在途配置序列化和读取回调返回 `ScopeExpired`。配额按实际落盘的同一份字节核对，同一文档嵌套操作返回 `Conflict`。迁移的最终替换失败时清理本次备份和临时文件。改前复现七处问题，改后四个定向 Release 构建成功，Core 26/26、Storage 19/19、PackageLifecycle 15/15、Settings 48/48。 [验证记录](../docs/validation/b16-config-transaction-20261121/README.md) · [封存结果](../work/b16-config-transaction-20261121/current-r1/validation.json)

两批 B16 修复已合入新 V5 候选。冻结源码 1053 项、V7 隔离映像 554 项通过。ZIP 的 58 个成员逐项核对，隔离 `ValidateOnly` 验证 26 个运行文件。从新 ZIP 解出的 DLL 复跑 B16 共 108 项，独立配置及设置消费者使用同源 SDK 引用包编译成功。解决方案 Release 为 0 错误、65 项警告。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-b16-candidate-20261121-r1.zip) · [SDK 引用包](D:/riz-m2/v5-b16-contracts-20261121-r1/sdk-feed/RizToolbox.Sdk.1.2.0.nupkg) · [归档核验](../work/b16-config-transaction-20261121/candidate-validation-r1.json) · [包内回归](../work/b16-config-transaction-20261121/packaged-b16-validation-r1.json)

下一离线工作可继续 B04/B17 的独立插件组合及依赖撤回。双独立包的配置交错、游戏内设置恢复、正式 Fresh、固定 `GetTime`、实际 DSP、带值 Speed/Seek 和 B01–B19 整体验收仍待完成。本批未安装或启动游戏。

## 最新：设置停用保留已进入的写入及恢复（11-21）

滑块范围探测一次完成后发布，拒绝重入写入。范围读取时停用插件会挡住随后写入。setter 已进入时公开租约立即停止，物理释放等待实际读回。管理器定期重施期间停用也会恢复最新的外部基线。四个改前问题已复现，当前 Settings 48/48、Mutation 21/21、Data 30/30、Module 48/48，四个定向 Release 构建成功。 [验证记录](../docs/validation/b16-setting-lifetime-20261121/README.md) · [封存结果](../work/b16-setting-lifetime-20261121/current-r1/validation.json)

修复已进入源码。下方 V5 候选包含此前的结算与数据修订。真实设置恢复、Speed/Seek 和 B01–B19 整体验收仍待完成。

## 最新：V5 合并结算归属和数据修订修复（11-21）

从当前 1814 项输入编译六个托管组件。两个 V7 映像夹具 554 项、ZIP 58 个成员回读、隔离 `ValidateOnly` 的 26 个运行文件均通过。包内 DLL 复跑 Game 81/81、Data 30/30、Module 48/48、Gameplay 241/241，以及起播、播放命令与倍率恢复 513 项，合计 913 项。同源 SDK 引用包编译了独立插件的结算读取和模糊续接。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-result-data-candidate-20261121-r1.zip) · [SDK 引用包](D:/riz-m2/v5-result-data-contracts-20261121-r1/sdk-feed/RizToolbox.Sdk.1.2.0.nupkg) · [归档核验](../work/v5-result-data-delivery-20261121/candidate-validation-r1.json) · [包内回归](../work/v5-result-data-delivery-20261121/packaged-contract-validation-r1.json)

同候选实机仍需核对 Unity 首帧、正常结算和同谱快重开、数据结果字段、正式 Fresh、固定 `GetTime`、实际 DSP 和带值 Speed/Seek。B01–B19 整体验收保持开放。

## 最新：数据快照拒绝已失效的采集（11-21）

SDK 只允许一次刷新发布。同次采集中的失效或模块释放会中止发布，单独取消会保留上一份已验证的数据。已有结算观察变化会标记旧修订，显式旧页仍可按同一 token 读取。四个改前问题已复现，三个定向 Release 构建成功，159 项离线检查通过。 [验证记录](../docs/validation/b10-snapshot-publication-20261121/README.md) · [封存结果](../work/b10-snapshot-publication-20261121/current-r1/validation.json)

数据快照与同谱重开修复已随本页顶部的 V5 候选交付。

## 最新：同谱面更换控制器后重新核对局身份（11-21）

即使沿用 `LevelInformation`，更换控制器也会结束旧局的关联。新图须有当前 `isPlaying` 与 `timeReady` 才签出新 `RunId`。旧结算在新局等待结果时保持未关联，最后一笔判定仍可由原控制器提交。已复现旧 RunId 被继承、另一控制器的判定借用旧局两处问题，修复后四个定向 Release 构建成功，共 349 项离线检查。 [验证记录](../docs/validation/b09-controller-pair-result-20261121/README.md) · [封存结果](../work/b09-controller-pair-result-20261121/current-r1/validation.json)

修复已随本页顶部的 V5 候选交付，实机同谱快重开与正常结算仍待验收。

## 最新：V5 合并起播校验和播放命令停用修复（11-21）

当前 1814 项输入重新冻结并构建六个托管组件。两个 V7 映像夹具 554 项通过。ZIP 58 个成员逐项核对，隔离 `ValidateOnly` 通过 26 个运行文件。包内 DLL 复跑 Gameplay 241/241、Module 48/48、起播 145 项、命令生命周期 193 项和倍率恢复 175 项，共 802 项。独立插件使用同源 SDK 包编译通过。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-playback-lifecycle-candidate-20261121-r1.zip) · [SDK 引用包](D:/riz-m2/v5-playback-lifecycle-contracts-20261121-r1/sdk-feed/RizToolbox.Sdk.1.2.0.nupkg) · [归档核验](../work/v5-playback-lifecycle-delivery-20261121/candidate-validation-r1.json) · [包内回归](../work/v5-playback-lifecycle-delivery-20261121/packaged-contract-validation-r1.json)

实机验收使用此候选核 Unity 首帧、正式 Fresh、固定 `GetTime`、实际 DSP 调度和带值 Speed/Seek。菜单模糊、作用域停用和 B01–B19 整体流程仍待实机验证。

## 最新：播放命令保留停用时的终态（11-21）

游戏对象读回前后都复核控制租约。插件在读回途中停用时，已写入的倍率或定位事务走原恢复流程。停用先取消排队命令，观察订阅逐个清理。延迟释放后端失败时单独记日志，即时和排队命令保留已确认的结果。三个改前故障已复现，四个定向 Release 构建成功，三套托管检查 313/313 和三个原生夹具场景 193 项通过。 [验证记录](../docs/validation/b05-gameplay-command-shutdown-20261121/README.md) · [封存结果](../work/b05-gameplay-command-shutdown-20261121/current-r1/validation.json)

这项修复已进入本页顶部的 V5 候选。

## 最新：Replay/Resume 发布时钟前核对起播读回（11-21）

正式来源现在用实际 `PlayScheduled` 参数交叉核对 DSP 采样、游戏的起播基准和媒体位置。恢复选中倍率之前发现冲突或起播点已过，只保留游戏自己的倍率重置。倍率写入后或发布前发生漂移时，撤回当前时钟与 SDK 播放读数，原生 `GetTime` 返回原值。四个改前反例已经复现，修复后的七个独立场景共 393 项，Gameplay 238/238，三个定向 Release 项目均成功。 [验证记录](../docs/validation/r06-stock-schedule-readback-20261121/README.md) · [封存结果](../work/r06-stock-schedule-readback-20261121/current-r3/validation.json)

这项修复已进入本页顶部的 V5 候选。

## 最新：V5 已纳入菜单模糊续接（11-21）

从当前源码重新封存并编译六个托管组件。两个 V7 映像夹具 554 项通过，ZIP 58 个成员逐项核对，隔离 `ValidateOnly` 核对 26 个运行文件。使用新包 DLL 复跑 48 项模块检查及 175 项倍率恢复检查。独立项目从同源 SDK 引用包编译了 `BindDownsample` 和 `LastSnapshot`。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-blur-binding-candidate-20261121-r1.zip) · [SDK 引用包](D:/riz-m2/v5-blur-binding-contracts-20261121-r1/sdk-feed/RizToolbox.Sdk.1.2.0.nupkg) · [包验证](../work/n4-menu-blur-binding-20261121/candidate-validation-r1.json) · [合同验证](../work/n4-menu-blur-binding-20261121/packaged-contract-validation-r1.json)

实机使用此候选验证 Unity 首帧、正式 Fresh、固定 `GetTime` 与带值 Speed/Seek，并检查菜单模糊效果、相机重建后的续接与恢复。B01–B19 整体验收仍开放。

## 最新：菜单模糊覆盖可跟随对象重建（11-21）

N4 已为 `IGamePostProcessingService` 接入 `BindDownsample`。插件按明确的 `MainMenu` 或 `GameStart` 注册覆盖，宿主复用现有发现节奏，唯一目标被重建时获取新代次租约。两个插件继续共用优先级仲裁。外部改值、通道失效及提供程序撤回均可结束意图。已用受控后端走正式插件宿主验证歧义、身份复用、重建和写入期间停用插件。1814 项冻结输入，Release 解决方案 0 错误、66 条既有警告，五套定向检查 283/283。 [SDK 调用与验证](../docs/validation/n4-menu-blur-binding-20261121/README.md) · [结果汇总](../work/n4-menu-blur-binding-20261121/validation.json)

此功能已随本页顶部的新 V5 候选交付。视效、实际重建及恢复仍待实机验证。

## 最新：V5 合入菜单后处理与倍率回滚修复（11-21）

1813 项冻结输入生成六组件 Release，0 错误。两个 V7 映像夹具共 554 项，ZIP 58 项全量回读，隔离 `ValidateOnly` 核对 26 个运行文件。使用包内 DLL 复跑 43 项模块与 175 项倍率回滚检查。单独的 SDK NuGet 引用程序集与冻结构建字节相同，独立项目从本地 feed 编译菜单模糊接口成功。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-postprocessing-candidate-20261121-r1.zip) · [SDK 引用包](D:/riz-m2/v5-postprocessing-contracts-20261121-r1/sdk-feed/RizToolbox.Sdk.1.2.0.nupkg) · [包验证](../work/v5-postprocessing-delivery-20261121/candidate-validation-r1.json) · [合同验证](../work/v5-postprocessing-delivery-20261121/packaged-contract-validation-r1.json)

当前真实游戏验收使用同一候选依序看 Unity 首帧、正式 stock Fresh、固定 `GetTime` 调用和带值 Speed/Seek。新增后处理单独看启动页与主菜单可见效果、竞争与恢复。B01–B19 整体验收保持开放。

## 最新：菜单相机后处理目标已接入 SDK（11-21）

N3 新增实验能力 `riz.game.post-processing`。`GameStart` 与 `MainMenu` 的主相机会按需绑定唯一活跃 `TranslucentImageSource.Downsample`，SDK 接受 0–2 整数，复用共享目标、优先级及插件作用域租约。当前 Release solution 0 错误，五套定向验证 278 项。实际渲染与恢复待验收。 [SDK 调用与验证](../docs/validation/n3-menu-postprocessing-20261121/README.md)

此功能已经随本页顶部的 V5 候选交付。已接入的音符、轨道、相机及菜单后处理无需重做。

## 最新：倍率回滚保留已经外部更改的值（11-21）

暂停倍率写入已经确认时，逆向写入前重读实际倍率和基线。改前覆盖了夹具中的外部 `1.25×`，改后保留该值，审计记录副作用未知，旧 SDK Clock 样本撤回。首次读回失败时同一私有续票仍可完成逆向写入和确认。当前 Release solution 0 错误、定向 986 项。 [验证记录](../docs/validation/r06-pitch-recovery-ownership-20261121/README.md)

此修复已经随本页顶部的 V5 候选交付。正式 Fresh、游戏固定 `GetTime` 调用及实机 Speed/Seek 仍待验收。

## 最新：Fresh 票接到原生 GetTime 调用点（11-21）

使用自有的 22-role 播放来源夹具，以同一 Fresh 票完成暂停、Clock 激活与 `GetTime` 返回。登记调用点在宿主线程传入空 `MethodInfo` 时应用 Clock。错误调用点、错误方法和 worker 原样执行原函数。受监视构造器在 worker 执行后，下一次登记调用点立刻回到原生返回，物理 Unknown 保留原持有的根。九个独立进程共 501 项检查。 [验证记录](../docs/validation/r06-fresh-gettime-consumer-20261121/README.md)

新 V5 候选已包含精确首帧识别和暂停 Clock 激活校验。六组件 Release、V7 映像夹具 554 项、ZIP 58 项回读和隔离安装校验通过。下一验收依序确认同一候选的 Unity 首帧、正式 stock foundation 与 Fresh 七根采用、游戏 `ProgressControl.Update` 的固定调用点，再验证带值 Speed/Seek。R06/R07/R08 及 B01–B19 保持待验收。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-paused-clock-candidate-20261121-r1.zip) · [包验证](../work/r06-fresh-gettime-consumer-20261121/candidate-validation-r1.json)

## 最新：暂停 Clock 激活使用当前读回（11-21）

Clock 激活前后以同一完成的暂停回执复核位置、倍率、音频长度和播放状态，正式路径同时复核固定 `GetTime` 消费者与原挂接凭证。两个改前故障已复现。激活前基线变化时可等原值恢复并重试，激活后读回变化会撤销这次策略。当前 Release solution 0 错误，定向检查 700 项，包含重新开局和跨局进程。 [验证记录](../docs/validation/r06-paused-clock-activation-20261121/README.md)

该批源码已随本页顶部的新 V5 候选交付。

## 最新：Unity 首帧按版本配置绑定（11-21）

首帧识别要求配置程序集中的声明类型、实例 `void Update()` 签名和精确方法元数据。夹具依次投递其他程序集的同名方法、带参数重载和另一方法元数据，随后配置回调正常取得首帧和下一帧序号。改前托管 14/16，改后托管 16/16、四个自有原生通道 209 项、Loader 24/24，Release solution 0 错误、67 条既有警告。 [验证索引](../docs/validation/e5-exact-startup-tick-20261121/README.md)

新版 V5 启动候选含首帧匹配器，六组件 Release、两个 V7 映像夹具 554 项、58 个 ZIP 成员核对及隔离 `ValidateOnly` 已通过。下一离线切片仍为正式 `Fresh` 当前身份与固定 `GetTime` 消费者的联合读回。真实首帧、正式 Speed/Seek、R06/R07/R08 与 B01–B19 待验收。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-exact-tick-candidate-20261121-r1.zip) · [包验证](../work/e5-exact-startup-tick-20261121/candidate-validation-r1.json)

## 最新：原生挂接与 Unity 首帧分别报告（11-21）

`runtime-startup.json` 新增宿主状态和首个 Tick 序号，跟踪等待 Unity、宿主构造、首帧执行及三类启动失败。启动摘要只在原生挂接完成、首个宿主 Tick 正常返回且配置允许时显示可选写入就绪。改前 Loader 22/23，当前 Loader 24/24、托管 Tick 14/14、四个自有原生场景 209 项，Release solution 0 错误、67 条警告，1808 源输入已封存。[验证索引](../docs/validation/e5-startup-readiness-20261121/README.md)

新 V5 启动候选包含六个当前 Release 组件，通过同源码冻结回归、两个 V7 映像采用场景、58 个 ZIP 成员逐项回读及隔离 `ValidateOnly`。下一次同候选实机应以进程 ID 对应启动文件，先确认首个 Tick 是否到达，再检查 V5 托管采用、正式 Fresh 和固定 `GetTime` 消费者。正式 Speed/Seek、R06/R07/R08 和 B01–B19 保持待验收。[新候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-startup-candidate-20261121-r1.zip) · [包验证](../work/e5-startup-readiness-20261121/candidate-validation-r1.json)

## 最新：V5 候选包补齐播放依赖（11-21）

V5 构建入口现在核对原生构建生成的配置哈希，去除了对本机旧 V4 配置缓存的依赖。六个 Release 组件构建成功，0 错误、2 条可空性警告。当前 Runtime 用内置配置核验 11-20 保存的 36/36 原生写入与主线程见证，原生文件图核验 5 个模块、44 条依赖。协调器 28/28、接收者 10/10、音频与内容 1015/1015。当前托管源码完成两个 V7 映像夹具，共 554 项检查。[验证索引](../docs/validation/e5-production-candidate-20261121/README.md)

打包器随 V5 候选交付 `RizToolbox.SingleReturn.Prototype.dll` 和 `RizToolbox.PlaybackSourcePrototype.dll`，并把冻结的 Schema 3 示例模组放到宿主读取的目录。新包通过 PowerShell 和独立 Python 校验，26 个运行文件、31 份证据，ZIP 58 个成员逐项回读。隔离安装 26/26，独立包缺失文件及替换文件反例 3/3。[候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-candidate-20261121-r4.zip) · [封存](../work/e5-production-admission-20261120/managed-current-20261121/validation-r4.json)

真实游戏保持后置。此前会话已经确认原生启动快照，托管日志停在等待 Unity 首次回调。下一次实机从同版候选的首次 Tick、宿主初始化和 V5 托管采用依次确认，再推进同局正式 Fresh、固定 `GetTime` 消费者及带值控制。完整 B01–B19 与 R06/R07/R08 仍开放。

## 最新：Seek 保留外部暂停位置，探针支持谱面坐标（11-21）

改前两例复现了首次写入及再次写入覆盖外部变更的位置。`TimeWrite` 现在分别对照准备基线和上次实际读回，在调用原 `SetTime` 前检查当前位置。两例均验证拒绝时无原生调用或写入，外部恢复原值后可继续定位、独立确认恢复并释放根。探针脚本和接收端现通过同一个 SDK 请求传递 `chart-coordinates`，默认仍为 `game-seconds`。

当前 Release solution 与独立 provider 增量构建 0 错误。12 个时间写入场景 193 项、Gameplay 238 项、相邻 provider 277 项、探针接收端 28 项、隔离发送端 4 项，共 **740 项**。1807 输入与受保护 README 按当前结果核对。[验证索引](../docs/validation/n2-seek-media-ownership-20261121/README.md) · [封存](../work/n2-seek-media-ownership-20261121/validation-r1.json)

**下一离线工作**：核对正式 `Fresh` 入口从 `VerifyCurrent` 到 `BeginSourceOperation` 的同代采用，以及固定 `GetTime` 消费者能否用已存在的映像与观察材料完整验证。11-20 的 E5 留档已有 36/36 原生初始化写入和 Ready 快照，托管会话记录止于等待 Unity 生命周期。后续需把它与运行接线证据对应，正式 Speed/Seek、R06/R07/R08 实机及 B01–B19 仍未验收。本轮未安装或启动游戏。[E5 原生记录](../work/e5-production-admission-20261120/live-admission/live-native-admission.json) · [E5 会话日志](../work/e5-production-admission-20261120/live-admission/toolbox-live-session.log)

## 最新：N2 五参与者完整状态组合与恢复已封存，160 项当前检查通过（11-20）

离线对象图已完成原长条停止、音频、四个 unmanaged 进度缓存、判定与可见性重建、Clock 发布的五参与者组合与 36 步逆序恢复（[seal-r1 汇总](../work/n2-seek-game-state-composition-20261119/seal-r1/validation.json)）。当前源码修复了三处阻断：`NativeJudgementBackend.NoteVisibility` 按 `TransformClass`/`EaseInfoClass` 读取并采用元素以修复可见性数组基线；`NativeSeekCanvasBasisCommands.Ownership` 在逆序恢复与独立读回完成后检查 `_restorationFailure` 确认所有权（最初异常仍保存在日志中）；`NativePlaybackSeekParticipants`、`NativeSeekGameStatePreparation`、`NativeSeekProgressParticipant` 与 `SeekHoldEpoch` 在异常目标长条未确认停止时拦截 Audio 与 Progress 时间逆写，保留原 Clock 与 362 个未知原生根责任（[原始故障日志](../work/n2-seek-game-state-composition-20261119/failure-r2/game-state-5.stderr.log)、[历史部分运行](../work/n2-seek-game-state-composition-20261119/restored-ownership-r1/)）。

同一最终 1802 输入快照上三组串行执行及封存全部自然 `exit 0`：`current-r1`（cases 5–7，[结果](../work/n2-seek-game-state-composition-20261119/current-r1/results.json)，完整 `RizToolbox.sln` Release 构建 `0 errors / 55 warnings`）、`current-r2`（cases 1–4＋147 项相邻 TargetState/HoldModes/HoldLifecycle/RuntimePublication，151 项，[结果](../work/n2-seek-game-state-composition-20261119/current-r2/results.json)）与 `canvas-adjacent-r1`（Canvas 7,8,9,10,17,18，6 项，[结果](../work/n2-seek-game-state-composition-20261119/canvas-adjacent-r1/results.json)），合计 **20 个子进程、160/160 托管检查、7/7 唯一组合场景（0 重复）**；28 项源码差异（26 修改＋2 新增）、26 份真实改前文件、6 个受测 DLL 及受保护 `README.md`（SHA256 `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`）经 `seal-r1` 核验一致。详见[报告](../docs/2026-11-20_seek-game-state-composition-report.md)与[证据索引](../docs/validation/n2-seek-game-state-composition-20261119/README.md)。

**诚实边界与下一工作**：`seal-r1` 仅签离线受控五参与者组合（`visibilityBodiesSynthetic=true`、`nativeOriginalSynthetic=true`、`fixedOriginalBodyVerified=false`、`formalFreshIssued=false`、`fullSeekCompleted=false`、`productionAdmission=false`、`gameExecuted=false`、`installed=false`、`packageChanged=false`）。正式 22-role `Fresh/watch/SuppressObserver` 实入口、固定 `GetTime` consumer 正向、`N2` 带单位时间与带值 Speed/Seek 控制事务、`E5` 生产首次准入、`Single mid`/非空 `listener effects`/`engine setter/effect transfer`、`B01–B19` 及 `R06/R07/R08` 实机验收均保持开放；无 commit/push、新包或游戏操作。

## 最新：N2 长条部分目标启动恢复，157 项当前定向检查通过（11-19）

已执行真实epoch Prepare→原Stop→target Factory/Start/Stop→原iterator inverse，以及实际backend observer callbacks。修复第二Factory前失败将第一条已确认target一并Unknown、无法恢复的缺口：每项native effect在dispatch前登记，独立读回后确认；仅target构造的已知检查/前dispatch失败保持恢复责任。pending effect、异常Start、完整target未确认、原observer Unknown或临时cleanup Unknown仍拒绝。target仅尝试一次；返回token在post-call撤权前进入原publication owner，不静默丢输出。

最终 `work/n2-seek-hold-epoch-recovery-20261119/current-r2/` **15child及outer51745自然exit0**（chunk `4dd7eb`）：epoch10＋target28＋模式3＋Hold lifecycle62＋Runtime publication/field54＝**157/157当前案例**。Release solution最终增量 **0E/0 emitted warnings**；本轮首次完整编译current-r1仍62条既有测试警告，未消除。1800输入/6DLL/README前后保持；7修改/4新增、7真实改前/11改后已[封存](../work/n2-seek-hold-epoch-recovery-20261119/validation-r1.json)，seal55046自然0/chunk `96da07`。[报告](../docs/2026-11-19_seek-hold-epoch-recovery-report.md)及[索引](../docs/validation/n2-seek-hold-epoch-recovery-20261119/README.md)。不借旧31/956/旧包验新源，157不是整树完整回归。

原partial case3 actual child exit1，Factory1/Start1/Stop1/count1且三in-flight0仍Unknown，原Execution/Runtime字节匹配改前；expected-failure runner0仅捕获。原case2错误注入TargetProfile会撤finger来源、不签可恢复，最终仅注入受控epoch profile。focused-r1/r2、Runtime新断言/编译失败均保留。current-r1默认Runtime380/389因9项缺原生fixture env实际outer exit1，整批未验收；最终只用current-r2当前定向执行，不删除原测试/不借历史native结论。

真实Module.CaptureSeekBackend issuer已执行，但安装字段受控装配、epoch profile/native original为shim，不签固定original body/真实Dobby/engine Detach/正式Fresh。原Judge queues同epoch count接线已存在，本批实际验证1→0→1→0→1、18次Clear/Add/inverse、完整backing/tail/size/signed version恢复；**没有重写计数handoff**。完整gameState/rendering/五参与者仍缺席，完整Seek未签。

**下一离线工作**：沿原NativePlaybackSeekParticipants和NativeSeekGameStatePreparation补实际gameState/判定/rendering与HoldEpoch→audio→四progress cache→gameState→Clock整合及逆序恢复。正式固定profile/Fresh/consumer实际执行材料单独补，不以shim代签。相机/N1/退休/consumer/pitch/source/progress单切片及R08矩阵不重做。

当前无在途构建或测试。模型按 **6.1 Sol/max**；两个旧审查代理及新具体只读复核仍仅空询问、零有效交付，root完成实施、源码复核和串行封存；勿把这些空答复计作独立通过。封存后27项证据身份及1800源码仍一致，五文档130本地链接检查通过。没有commit/push、新包、真实游戏安装/启动、UI或账号操作。真实Speed/Seek关闭，R06/R07/R08实机、B01–B19及production未完成，goal active。

## 最新：N2 长条模式字段阻塞修复，31 项当前定向检查通过（11-19）

实际Hold epoch初始化/后续检查误用不存在的`AutoPlay`／`FreeHold`字段，已用原产品真实private VerifyModes稳定复现。正式元数据为`<AutoPlay>k__BackingField` Boolean0x79、`<FreeHold>k__BackingField` Boolean0x7a；现在同一私有ReadModes负责capture和后续检查，实际模式变化/错误类型/偏移仍拒绝，不加别名/写权限。

最终 `work/n2-seek-hold-mode-fields-20261119/current-r1/` **5child及outer21308自然exit0**（chunk `75baf9`）：三新模式案例3＋相邻TargetState/初始化/时间目标28＝**31/31**；当前Release solution **0 errors／62既有测试警告**（PlaybackStartInitialization55＋Judgements7）。1796输入/4DLL/README前后保持。三改前/四改后、3修改/1新增及原错误在[封存](../work/n2-seek-hold-mode-fields-20261119/validation-r1.json)核验，seal自然0/chunk `9445af`，[报告](../docs/2026-11-19_seek-hold-mode-fields-report.md)和[索引](../docs/validation/n2-seek-hold-mode-fields-20261119/README.md)。本批不借下面956/旧811/旧包验新源，31是案例数不是整树完整回归。

原 `reproduce-r4` 真实private VerifyModes字段缺失actual child exit1，expected-failure runner0只捕获不验收；r1/r2测试缺namespace编译失败、r3fixture缺Coroutine metadata为工具/测试失败、不算产品复现，均保留。受控ctor仅执行实际private ReadModes/VerifyModes、NativeRuntime/RunValue/target reader；未运行完整epoch Prepare/Start/Stop、未发行formal observer。完整Seek仍未签。

**下一离线工作**：推进实际注册observer/profile下的Hold epoch Prepare→Stop→target Start/Stop→predecessor inverse，再接gameState/判定/rendering原五参与者。Judge queues已有BindHoldEpoch/ReadOwnedJudgeCount和两份完整样本接收同epoch计数，只改count、不改列表baseline，**不再把Stop清零count当确定缺口，不重写此接线**。重点核target Start部分完成：_targetStarted仅全部完成后置true、当前异常仍Unknown；先取得实际退出/drain材料，再决定是否细分可恢复前dispatch失败，勿放宽未知失败。正式Fresh/固定consumer正向仍缺材料。相机/N1/退休/consumer/pitch/owner-fault/source/progress及R08矩阵不重做。

当前无在途构建或测试；6.1 Sol/max新具体审查代理仍返回空询问、零有效交付，root串行完成，勿重复调度空答复。没有commit/push、新包、安装/启动游戏、UI/账号操作。R06/R07/R08实机、真实Speed/Seek、B01–B19、production未完成，goal active。

## 最新：N2 音频／真实四进度缓存／Clock 组合闭合，956 项当前检查通过（11-19）

修复最后字段 metadata 查询中 Clock／route 撤权后仍写 progress 的实际窗口：`Capture` 必须接独立写权限检查；`WriteExactGuarded` 最后边界在 attempted 登记前核原 source／Clock operation，正式 Adapter 分别传入 `VerifyOrigin` 与 `captured.VerifyAuthority`。真实四 unmanaged cache、独立八根及既有 Plan／Audio／Clock／公共事务已执行。向前／向后／ChartCoordinates 三正向、第三cache后失败恢复和最后metadata撤Clock／换route均完成；正向audio→四cache→Clock，反向Clock→四cache逆序→audio，原倍率及DSP anchor保持。

最终 `work/n2-seek-progress-participant-20261119/build-and-checks-r1/` **30 child 及原outer28519自然exit0**，完成chunk `ebece8`：新六progress模式145＋private source135＋TimeWrite161＋Gameplay238＋相邻rate/publication277＝**956**。当前Release solution **0 errors／55既有测试警告**、独立stock-provider0E/0W；1795当前输入/10DLL/5native+隔离映像+2相邻native前后相同，用户README不变。seal自然0/chunk `83fc48`，四份真实改前与六份改后、4修改/2新增已核。[封存汇总](../work/n2-seek-progress-participant-20261119/validation-r1.json)、[报告](../docs/2026-11-19_seek-progress-participant-report.md)、[索引](../docs/validation/n2-seek-progress-participant-20261119/README.md)。

原产品 `reproduce-r1` 撤Clock后仍有一media write和一cache store，actual child exit1保留；expected-failure runner0仅捕获成功。focused-r1 partial因测试observer真实GetTime抢先消费注入而失败，仅修测试边界/JSON tuple展示，focused-r2故障70通过；不借这些阶段或旧811验最终源。映像只DONT_RESOLVE映射核正式profile，不执行代码/DllMain；120BPM及shift转换为API shim，owned GetTime与固定metadata不同。actual gameState/HoldEpoch缺席，**不签完整Seek／正式provider／Fresh／固定consumer**。

**下一离线工作**：沿原五参与者补实际gameState/HoldEpoch停止→重建→Clock与逆序恢复。重点核predecessor Stop把Hold count清零之后，gameState各准备baseline与恢复接收同epoch已知写入的衔接；现为待复现线索，勿直接放宽baseline造PASS。相机/N1/退休、consumer/pitch/owner-fault、source回调和本批progress组合不重做；不重跑R08包矩阵。

当前无在途构建/检查。子代理按 **gpt-6.1-sol/max** 做具体只读审查，root串行实施和构建。没有commit/push、新包、安装/启动游戏、UI或账号操作。R06/R07/R08实机、真实Speed/Seek、B01–B19及production未完成，goal active。

## 最新：N2 Seek source/writer/Clock 正式接线已合，811 项当前检查通过（11-19）

修复实际 Router 把本 source 的 native 私有 SetTime 请求当作外部音频修改而撤权：当前 reservation/nonce+原 callback frame 选中同一个真实 writer，producer 仍独立登记 wrapper/lower child 并核实际退出；外部同值 SetTime 仍撤 source/Clock。Seek 复用 RateSource 已冻结的 source/run/group/binding/origin/adoption/route/Clock 与实际 policy，返回 source-bound time wrapper；原生读取后 receipt 只由同 writer successor更新。provider 验实际 time continuation，pending旧API receipt仍不可消费、不误撤唯一inverse owner。原 TimeWrite内部在graph getter后/native store前复核绑定来源，getter内切route提前拒绝；busy source释放拒绝后同源及准备根保留。准备getter中source释放后返回前再次核来源，不遗留未返回writer；登记后最终验证失败清理实际准备owner。旧单独Seek publication/full-group重复路径删除，准入一次核pitch+SetTime/lower pair/GetTime in-flight/native gate；formal provider历史fixture不签Seek门禁保持。

最终 `work/n2-seek-source-operation-20261119/build-and-checks-r2/` **24 个 child 和 outer 自然退出0**：八个private source/actual Clock模式 **135**、十个TimeWrite模式 **161**、Gameplay **238**、三rate/publication模式 **277**，合 **811**。最终Release solution增量 **0E/0W**（本批首改后55既有测试警告未消除），独立stock-provider **0E/0W**；1793当前输入/10DLL/七native前后保持、用户README未变。12项源差异（10修改/1新增/1删除）、11份真实改前副本和各原失败已在[r2封存汇总](../work/n2-seek-source-operation-20261119/validation-r2.json)核验，seal自然0。outer39503/chunk7853f3，seal/chunk20048a。[报告](../docs/2026-11-19_seek-source-operation-report.md)及[证据索引](../docs/validation/n2-seek-source-operation-20261119/README.md)。正常native媒体→后续progress观察→Clock提交与Clock→progress→媒体恢复已实际执行，但progress/chart是API shim输入，不是完整参与者；owned private SeekSource反射构造不发行formal/Fresh，**不签正式Seek provider/完整Seek**。

原失败：withdrawn Clock/route replacement仍store、私有SetTime误撤、修路由后真实post-store pending provider误撤、getter后换route仍store、prepare getter释放source遗留未返回writer均child exit1保留。最初readback被路由阻塞不能冒称provider复现；expected-failure runner自然0仅说明捕获成功。focused-r1/r2依次71/87通过，focused-preparation-r1为55；r1阶段795保留历史及改前副本，不能验最终源。最终只签当前r2的811，不借旧676/1003/R08包验新源。

**下一离线工作**：沿既有 `NativePlaybackSeekParticipants` 补 `NativeSeekProgressParticipant`→gameState/HoldEpoch→Clock 的实际组合执行材料。优先核真实进度四cache与转换profile、完整事务提交和逆序恢复，别把本批shim进度或private-source反射签成全Seek。正式Fresh/固定consumer正向仍缺stock+foundation+claimed ticket实际执行材料；不借owned22roles提升。相机/N1/退休批、GetTime consumer/pitch恢复/owner同步撤权/本批source回调接线和R08矩阵不重做。

当前无在途构建/检查。两名新子代理已按**gpt-6.1-sol/max**调度但只返回待命询问，零有效交付，root串行实施/核验。没有commit/push、新包、安装/启动游戏/UI/账号操作。R06/R07/R08实机、真实Speed/Seek、B01–B19和production未完成，goal active。

## 最新：N2 Seek 时间写入器独占与恢复闭合，676 项当前检查通过（11-19）

本批稳定复现并修复了两个实际入口缺口：暂停 baseline getter 可在外层尚未登记时抢占第二个 time/pitch writer；time writer 在进入 busy 前做 graph getter，回调可写入、恢复或释放同 writer。现在先占唯一 value writer 槽与原 operation，在任何 getter 前核 self/thread 并进入 busy。已完成原生 SetTime、API尚未读回时，旧receipt返回NotReady，不消费旧native fence误撤唯一恢复来源。恢复前独立读取全部非媒体暂停基线，progress/offset/pitch等变化时不做inverse；临时media读失败仍可用同native continuation一次恢复，必须新完整读回。source close期间原七个handle保持，真实getter退出后writer释放清理零根。

最终 `work/n2-seek-time-writer-20261119/build-and-checks-r3/` **16 子进程及外层自然退出0**：十个actual time writer模式 **161**、Gameplay **238**、三个相邻rate/publication模式 **277**，合 **676**。solution与独立stock provider最终Release增量0E/0W；首轮改后solution仍55条既有测试警告，未消除。1793当前输入、10DLL、native字节前后保持，六份真实改前副本/八项源差异与用户README在[封存汇总](../work/n2-seek-time-writer-20261119/validation-r3.json)核验。[报告](../docs/2026-11-19_seek-time-writer-report.md)及[证据索引](../docs/validation/n2-seek-time-writer-20261119/README.md)。owned两映像确有22source/23physical，但没有正式stock/foundation/Fresh票，**不签正式Fresh、正式Seek provider或完整Seek**。

原三重入 `reproduce-r3` 和pending receipt `reproduce-r4` actual child exit1保留；r1测试引用字段编译错误、r2逗号模式当未知项不算产品复现。focused四核心通过；完整r1/r2与release probes失败只修注入实际post-store边界和允许NativeRuntime两个临时调用根，严格同七源handle保留，产品未放宽。seal两次PowerShell换行语法失败保留工具记录，修work运行器后自然0，没有重跑已经通过的主体。

**下一离线工作**：继续N2正式Seek source/publication运行身份边界及完整参与者通路。`Session.CaptureSeekSource`仍用`VerifyFormalRateAuthority`、`VerifySeekPublicationOwner`仍full group；本批实际TimeWrite已迁移原receipt/continuation身份，勿重做。保持formal provider历史fixture不签Seek限制；不要为测试删限制。完整参与者已有HoldEpoch→audio→progress→gameState→Clock与逆序恢复，优先沿现有同票source/当前Clock补具体缺口。正式Fresh/固定consumer正向仍缺完整实际执行材料；不借22-role owned代签正式stock/foundation。相机/N1/退休批、GetTime consumer、pitch恢复、owner同步撤权及R08矩阵不重做。

当前无在途构建/检查。子代理偏好**gpt-6.1-sol/max**，现有代理未提供有效交付，root串行实施与核验。没有commit/push、新包、安装/启动游戏/UI/账号操作；R08旧包不能验本批新源码。R06/R07/R08实机、真实Speed/Seek、B01–B19和production仍未完成，goal active。

## 最新：R06 worker 原生故障在当次 owner 维护撤权，1003 项通过（11-19）

稳定复现并修复了 async fault notification 时序窗口：真实 watched worker constructor 已在原 store 前 native Unknown、SuppressObserver，原 original/physical exit 各一次，但通知暂存时第一次 Router pump 仍 Paused、宿主仍 Ready。`PollOperationNativeFaults` 现在同步返回 retained source/GetTime 的真实故障；Router 当次撤 Clock，host 在进度节流前做物理维护。正常 Armed/AuthorityActive=false 不误判物理故障。迟到 exact fault 对象继续消费，不复活、不新增 pitch 写，Unknown 保留原七根。

最终 `work/r06-owner-fault-poll-20261119/build-and-checks-r3/` **12 子进程及外层运行器自然退出 0**：Gameplay **238/238**、九个 actual owned 模式 **765**，合 **1003**；两个 Release **增量**构建本次0E/0W（首次变更后solution仍55条既有测试警告，未消除）。1791当前输入、13 DLL、七份native材料前后身份相同，README用户字节保持。正常Unity backing切源Known、实际Start/Pause后SDK新变速确认、最后零根；publication in-flight仍Unknown留七根。见[当前汇总](../work/r06-owner-fault-poll-20261119/validation-r3.json)、[报告](../docs/2026-11-19_owner-synchronous-fault-report.md)和[证据索引](../docs/validation/r06-owner-fault-poll-20261119/README.md)。

原改前owner/host exit1、focused错误要求generic game time消失、完整r1同时准备两个独占writer、r2错误强制optional native availability消失必须增加controller generation，均保留失败日志/exit。后两处只改测试为同一实际writer及旧lease/descriptor失权，不放宽产品。seal-r1语法失败保留，仅修work运行器后seal-r2自然0，没有重跑已过检查。七项源差异/六份真实改前副本在汇总核对；不借旧639/1086或R08包验新源码。

**下一离线工作**：本批十三成员owned worker/watch/SuppressObserver→同步poll撤权已闭合，勿再重复。继续沿正式`NativePlaybackFreshSourceCapture.VerifyCurrent → BeginSourceOperation`与同票adoption/Clock的完整执行材料，正式scope仍要求foundation、Unity backing、LowerTime、EnginePitch、ProgressResource、22 source roles及唯一claimed owner。先确定实际可执行材料，不能借本批13-role或旧11-role提升正式Fresh；固定真实consumer正向仍未执行。若正式材料不足，转NEXT未闭合N2/N3的具体可离线功能，避免再扩展等价故障拒绝测试。相机/N1/退休批、consumer接线、pitch恢复、R08封存矩阵不重做。

当前无在途构建或检查。子代理保持**gpt-6.1-sol / max**；两名既有代理followup仍只询问任务，零有效交付，root自主实施/复核，不重复调度空答复。无commit/push、新包、游戏安装/启动/UI/账号操作。正式Fresh/fixed游戏consumer、R06/R07/R08实机、真实Speed/Seek、B01–B19及production未完成，goal active。

## 最新：R06 GetTime consumer 操作接线已闭合，639 项当前检查通过（11-19）

正式 `NativePlaybackGetTimeCallProfile` 新增 owner thread/registered self 和 `VerifyOperation(expectedHook)`，沿 native/code profile 检查最初附加的 **同一个实际 lease**。固定 null MethodInfo 仍只有 CALL `0x401218`、返回 `0x40121d`。`PreparePausedClock` 保留完整安装/consumer 核验后再核同 lease；运行 source 和真实 GetTime reader 在实际 sample 前后核 consumer，最终核当前 receipt 的 native fences、run/source/route/profile 和唯一 Clock 快照。Standalone 仍完整审计；grouped 走已有运行窄通道，不缓存验证、不跨操作借 scope。

最终 `work/r06-gettime-consumer-operation-20261119/build-and-checks-r4/` **11 个子进程自然退出 0**：两个 Release 增量构建本次日志 **0 errors、0 warnings**（不是此前 55 条完整编译测试警告已消除）；Gameplay **238/238**，mapped profile **20**、standalone **24**、五个 actual owned 模式 **357**，合 **639**。1790 当前输入、10 个受测 DLL、六份 native 材料身份前后一致；用户 README 保持。实际 GetTime 最后 DSP getter 中真实 `set_MusicPlayer` 切源：原 original 一次、返回原始 `3`、旧 Clock/SDK 撤权、无 pitch/time 写，Known/faults保持并在最终 release 零根；真正 publication in-flight Unknown 仍留七根。见[当前汇总](../work/r06-gettime-consumer-operation-20261119/validation-r4.json)、[报告](../docs/2026-11-19_gettime-consumer-operation-report.md)和[证据索引](../docs/validation/r06-gettime-consumer-operation-20261119/README.md)。

r1 缺正式 UnityPlayer/foundation、r2 正常切源误要求物理 Unknown、r3 新 snapshot 字段编译错误，均保留原日志/exit。封存首轮因 borrowed native 描述含额外字段而错误按整对象比较，exit1保留；只修身份比较为 path/bytes/SHA256 后封存自然0，无产品变化、未重跑已过检查。`source-before/.../Program.cs` 是 after-dispatch 副本，validation 已明确不算改前原件，其原哈希仍在最初输入记录。

**下一离线工作**：继续 R06 正式 22-role Fresh/watch/SuppressObserver 的实际入口。现有 `NativePlaybackFreshSourceCapture.VerifyCurrent → BeginSourceOperation`、同票 adoption/Clock、writer 和 watcher 接线已经存在，先沿实际链定位仍需补的功能/执行材料，勿重写已有 scope 或借 11-role owned 夹具提升正式准入。优先闭合一个真实 worker lifetime writer 撤权→native SuppressObserver→owner Poll/排空切片；保持源、writer/scheduling fences 与根责任。固定真实 consumer 正向仍未执行。不要重做本批 consumer 接线、上一批 pitch 恢复、相机/N1/退休批，也不重跑 R08 封存矩阵。

当前无在途构建或检查。三名新代理已用 **gpt-6.1-sol / max / fork_turns=none**，仍只返回询问任务，未提供有效审查；root 自主实施和串行验证，不再重复调度空回复。无 commit/push，本轮未安装/启动/操作游戏、UI 或账号，未创建新发行包。R08 四包保持旧身份、不能验本批源码；正式 Fresh/固定游戏 consumer、R06/R07/R08 实机项、真实 Speed/Seek、完整 B01–B19 和 production 未完成，goal active。

## 最新：R06 倍率读回失败恢复已闭合，正式 Fresh/consumer 仍待补（11-19）

原生 SetPitch 正常返回且实际退出后，API getter 异常或返回 pitch 不符，现在保留真实 continuation 和唯一 registered PitchWrite，而不是立刻丢掉恢复凭据。provider 的当前操作验证沿用原生 writer/source/scheduling fences 和 Clock policy，pending 旧 API receipt 不可被消费；内部恢复能力只从实际 writer 经 Bound wrapper 传递。恢复重新核对所有非 pitch 暂停基线，允许唯一原倍率 native write；完整独立读回才记录 Restored，请求终态仍 Rejected。永久读失败、外部同值/ABA、切源、进度变化、native SEH、publication 故障/in-flight 继续 Unknown，不自动重发。getter 内重入/copy/worker 拒绝，七根责任保留到真实操作退出。

最终 `work/r06-pitch-readback-recovery-20261119/build-and-checks-r7/` 两个 Release 构建自然退出 **0**：solution **0 errors、55 条测试警告**，独立 stock provider **0 errors、0 warnings**；Gameplay **238/238**，19 个 owned 原生场景 **1086 项**全部通过。1788 当前输入、受测 DLL 和原生夹具前后身份一致；用户 README 不变。[验收汇总](../work/r06-pitch-readback-recovery-20261119/validation-r7.json)及[报告](../docs/2026-11-19_pitch-readback-recovery-report.md)。r1 漏夹具环境、r3 物理 Unknown 断言错误、r4 错 progress key、r6 第二 writer 旧测试失败均保留，未改成通过；仅最终 r7 签当前源码。两名新 6.1 Sol/max 代理没有有效交付，本批由 root 独立实施和串行验证。

**下一离线工作**：继续 R06 正式 Fresh/watch/SuppressObserver 与固定 consumer 的实际入口，优先从已有 `NativePlaybackFreshSourceCapture`、同票 Clock/publication 和实际 native watcher 链选择一个完整切片。不要重做本批 pitch 恢复、相机/N1/退休批，也不重跑 R08 已封存包/SDK/安装/采证矩阵。当前没有在途构建或检查。子代理统一 **6.1 Sol / max**；任务传递未恢复前 root 继续自主施工，不反复调度空答复。

本轮没有安装/启动/操作真实游戏、UI 或账号，也没有创建新包。R08 四份预览仍是原封存身份，**不能验本轮新源码**。正式 Fresh/watch/SuppressObserver、固定游戏 consumer 正向、R07 同机成本、R08 实机场景/恢复/重启、真实 Speed/Seek、完整 B01–B19 和 production 仍未完成；goal active。

## 最新：R08 基础预览离线交付已封存，下一推进 R06（11-19）

三个普通 SDK 模组 `basic.song-scores`、`basic.theme`、`basic.quick-panel` 已接入 `examples/`、solution 和 `Build.ps1`。schema3 精确 `basicPlugins` 选择、所选模组安装归属事务和当前包只读采证也已合入。默认 Runtime 仍为无模组；选择三个基础模组才是 21 项运行载荷。采证读取安装归属并区分实际字节、无归属、归属不匹配、pending、invalid；用户配置的修改如实记录，不作必需载荷失败。记录始终为 `preview/unverified`，不把人工声明提升为实机验收。

两处 WAV 边界已修复并通过当前源码检查：`ContentFile.MaximumBytes` 由实际打开的文件在分配前执行（默认 16 MiB，基础模组显式 1 MiB）；Play 成功却缺停止租约或返回 null 同异常启动保留 WAV/作用域，阻止替代播放并交宿主清理。当前 Release solution 构建 **0 errors、55 条测试警告**，1787 项输入前后相同；基础生命周期 **14**、SDK **10**、Core **24**、Hosting **24** 和十个音频/内容定向 case 共 **82/82**，受测 assembly 前后相同。证据在 `work/r08-delivery-integration-20261118/current-release-build-r2-exit.json` 与 `current-checks-r3.json`。r2 运行器在末端汇总 OrderedDictionary 字段时报错，未产完成报告，保留原失败及日志；修正后 r3 自然退出 0。首版 12/12 和历史 r8 不用于签当前源码。

采证原 `session 42102` 已自然退出 0，**14/14** 完整结果在 `acceptance-r1/results.json` 与 `acceptance-r1-exit.json`，没有转移实机验收。分发 `session 66046` 已自然终止：subject 完整 **50/50**，`distribution-r2/results.json` 已生成，源码/测试/1787 构建输入仍一致；外层工作运行器随后错误读取未定义的 `$LASTEXITCODE`，实际进程退出 **1**。`distribution-r2-process-observation.json` 如实区分完整 subject 结果与外层失败，未把退出 1 改为成功，也不重复已完成的 50 项。原分发 `session 69053` 已不存在，`distribution-r1/` 没有最终 results/exit；50 条中间 PASS 保留为 interrupted-before-final-report，不能算完整通过。

当前 SDK 已 Pack 并完成两个独立模板 hive/CLI home/NuGet cache、仅本地 feed 的正式 loader **14/14**；十个子进程自然退出 0，`sdk-offline-r1/result.json` 与过程观察完整。默认 `legacy-default` Doorstop r4 已自然退出 0，7325 项输入前后一致；输出在 `work/r08-doorstop-r4/`，编译有 **106 条 MSVC 警告**。r1 缺 rc、r2 缺 winres.h、r3 长路径失败保留；仅 work 运行器补 SDK 环境并缩短输出目录，主构建脚本与 profile 未改。不要重复 SDK14、Doorstop 或分发50。

四份当前分发与 r1 ZIP 全部已生成，`current-distributions-r1.json`、`current-deliveries-r1.json` 完整；原最后 session57736 自然0已经记录。代理发现操作说明把未安装的插件 ID 用作通用命令，现仅修 `New-PreviewDelivery.ps1` 的说明模板及三份用户文档；1787 输入相对当前编译记录只有该说明生成器变动，12 个当前 DLL 与四份 package-manifest 均未改。r2 首次汇总误取 Test 返回值不存在的 archiveBytes，自然exit1，原失败及已完成 Runtime-empty 保留；finish runner 从已核 receipt 取大小，并只生成其余三包。

`Generate-CurrentDeliveries-r2-finish.ps1` 已自然退出 **0**（session3551），四份 r2 ZIP 成员 **38/44/66/53** 全量读回，`current-deliveries-r2.json` 完整；载荷清单与 r1 相同、说明按实际插件选择生成。原有限场景 session74269 随工具连接中断：handle缺失、实际进程0、缺results，217个原来源文件hash/大小/创建和写入时间再核一致；`delivery-scenarios-r2-interrupted.json`保留，不计通过。新有限包场景 `delivery-scenarios-r3/results.json` 已 **10/10**、session84994自然0，含四包正向/六负向，原件及工具前后身份相同。假游戏安装 `current-install-r2/results.json` 已 **10/10**，session81194 与实际 child 均自然0；只在 clone lock 替换64字节哈希，原包/脚本/lock身份保持。首装212归属、二装10用户文件保留；卸载210未改文件、保留2修改配置及10用户文件。两次安装、一次卸载，网络和真实游戏进程读取均0，未加载载荷DLL。

最终 `Seal-CurrentDelivery-r2.ps1`（session9484）已自然退出 **0**，`delivery-final-seal-r2.json` 完整：1787项最终输入一致、12个当前DLL保持、四ZIP/收据/清单与当前support一致，独立SDK原主体和1236消费者记录保持；相对编译/SDK输入仅允许未参与其执行的说明生成器变动。状态保持 `preview/unverified`。过程观察和实际运行器复核在 `delivery-final-seal-r2-process-observation.json`、`root-runner-review-r2.json`，历史r1未执行记录保留。正式索引见 [R08 离线交付证据](../docs/validation/r08-basic-preview-delivery-20261119/README.md)，包和用法见 [交付报告](../docs/2026-11-19_basic-preview-delivery-report.md)。

**下一离线工作**：继续 R06 正式 Fresh/watch/SuppressObserver 与固定 consumer 的实际功能缺口；先核对当前源码与已有受控入口，选最小完整功能切片再改代码、串行构建和必要短验。R08 离线交付没有在途检查，不再重复包场景10、安装10、SDK14、采证14、分发50或大矩阵。Judge/Kernel 与两播放 helper 172 项当前来源/字节复用不倒签历史构建脚本。子代理统一 **6.1 Sol / max**，当前仅只读审查，root 独占串行执行。

本轮发行 ZIP 已生成，状态继续 `preview/unverified`。R06 正式 Fresh/watch/SuppressObserver/固定 consumer、R07 同机 60 秒成本、R08 三个实机场景及停用恢复/重启继续未验；真实 Speed/Seek、B01–B19、production 未完成。本轮没有安装、启动、操作真实游戏或账号；README 用户修改保持。不要重做相机/N1/退休批，不拿旧包验新源。

## 最新：R06 通道操作与 R07 后台诊断离线整合通过（11-18 22:47）

暂停倍率准入检查真实 GetTime in-flight，占用时写前拒绝而不撤销健康 policy。写 pitch 后才出现的 GetTime 原调用仍由最终 native publication 独立拒绝：SDK 命令进入不可重试 Unknown，撤回旧权限，实际原调用退出后仍保留物理 Unknown 与原七根责任。Standard 窄操作不重复完整安装/映像审计；安装和显式诊断继续全量验证。source fresh gate 只是 preflight，不是贯穿事务的锁。

R07 已接入有界后台日志、固定 cutoff 与逐次 Flush 结果、有限 Dispose/实际 Completion、一个正在写和一个最新待写状态副本、主线程原始性能窗口复制、后台序列化与分位数。诊断失败不阻断正常帧；停机不读已释放 provider；Standard 输入不做详细 Debug 比较；实际控制分阶段计时不改变写入/恢复/退出。回归清理改为等待实际 Completion；并发仍核对 1000 条及严格顺序。terminal 能力按 ID 核对，不能假定有界诊断选择是完整 SDK 排序。

当前唯一通过执行为 `work/runtime-redesign-channel-operation-20261118/run-r8-integrated-20261118/`：Release solution Rebuild **0 errors、55 条测试编译警告**；两个外部播放项目通过；十套托管 **688/688**（Gameplay 238），诊断导出 **26/26**，25 个原生播放模式 **1608 项**通过，含全部 17 operation modes 及 29 个真实 owned-call 注册逐项唯一 PASS。源与 assembly 前后 hash 相同，README 用户修改保持。见[结果索引](../work/runtime-redesign-channel-operation-20261118/validation-r8.json)及[报告](../docs/2026-11-18_playback-channel-diagnostics-report.md)。r1–r6 失败与两个定向 probe 保留，不能覆写成成功。

**下一离线工作**：R08 三位 **GPT-6.1 Sol / max** 正分别只写 `work/r08-basic-plugins-20261118/`、`work/r08-distribution-20261118/`、`work/r08-acceptance-record-20261118/`。基础插件独立 `basic.song-scores`、`basic.theme`、`basic.quick-panel`，不带 proof/Speed/Seek，全部实际需要能力写 RequiredCapabilities，使用既有 Waiting/需求语义，不给宿主加插件 ID 特判。另补 schema3 三类包生成/安装/恢复及同候选 Runtime/安装/插件/WAV/会话采证。root 独占串行构建，三代理交付停写后整合，先必要短验再出当前包；**当前无运行中的构建/测试**。

R06/R07 **没有整体完成**：正式 Fresh/watch/SuppressObserver、固定真实 consumer 正向、当前包实际倍率恢复与跨局、同机 60 秒成本仍待验。R08、真实 Speed/Seek、B01–B19、production 未完成。本轮未安装、打包、启动或操作游戏，未操作账号。不要重做相机/N1/退休批，不拿旧包验新源。

## 最新：暂停倍率接入统一命令入口（11-18）

R06 首批把会话占位提前到 `Probe`、控制器采集和准备之前。同步 `Execute` 与排队 `Submit` 共用一条执行路径。宿主在第二次边界处理前执行已排队命令，实验面板读取命令快照。SDK 记录区分排队、执行、成功、拒绝、取消和未知，终态可在作用域结束后读取。

排队取消不会进入原生执行，实际写后仍需读回或验证恢复。未知结果保留错误并阻止同一音源上的相关操作。控制器采集短暂失败或同一局的观察暂时丢失，都不会解除拒绝。确认控制器、局或音源替换后可重新准入。倍率包装层以当前捕获源的实时权限和精确绑定为准，嵌套检查不再重复调用完整 provider `Probe`。

Release 解决方案构建 0 错误、55 条测试编译警告。九套托管检查共 **596 项**，其中 Gameplay 207 项。三个自有原生播放场景共 **2094 项**，涵盖确认的倍率写入、跨局交接和同局音源重建。当前源码及夹具哈希见[验证结果](../work/runtime-redesign-operation-20261118/validation-r3.json)。真实帧成本、Speed/Seek、完整 B01–B19 和新版包仍待验。

**下一离线工作**：继续 [R06](runtime-redesign/CHECKLIST.md#phase-3-单一播放事务)。从 `NativePlaybackStockClockRouter.RateSource` 的 `VerifyFormalRateAuthority` 与 `RequireFreshAdmission` 拆分安装图审计和当前操作检查。保留写入凭据、Clock 发布、真实读回及原生退出责任，再迁移独立 Clock 的读取路径。

## 最新：相机发现限频与维护故障隔离（11-18）

R05 的相机探测只建立服务，首次维护再发现目标。已启用的覆盖每个宿主 Tick 维持。空结果和失败的扫描同样等待 250 ms，换局或换场景后可以立即发现新目标。离线夹具验证了空目标连续 100 个 Tick 只有一次扫描、发现失败后按时恢复，以及旧视图在回滚写入前失效。

故障模块和它的硬依赖者在任何清理回调前统一撤回能力。清理顺序是依赖者先于提供者。无从确认的清理保留实际模块及仍被其依赖的提供者，并报告需要重启。边界、共享音频、视觉和播放退休分别推进。日志与通知故障也经过隔离，独立模块和 FrameUpdate 继续执行。

Release 解决方案构建 0 错误、55 条测试编译警告。当前源码 12 套离线回归 **1704/1704**，其中相机 65、模块 40、宿主 10、音频/内容 1004。真实游戏帧耗时、Speed/Seek、完整 B01–B19 和新版发行包仍待验。见[本轮验证](../work/runtime-redesign-retirement-20261118/validation-r2.json)。

**下一离线工作**：推进 [R06](runtime-redesign/CHECKLIST.md#phase-3-单一播放事务) 的暂停倍率操作。先把操作占位移到活体读取和 Probe 之前，把排队、执行和未确认结果区分开，再收拢重复的 provider 准入与实际写入验证。保留写前检查、回读、恢复归属和原生退出责任。

## 最新：Gameplay 控制状态共用宿主快照（11-18）

R04 已把原生入口分配的宿主 Tick 序号交给 Adapter。Gameplay 在生命周期和播放维护结束后采集，稳定状态间隔 250 ms。控制观察借用生命周期模块已持有的控制器根，查询和订阅共用同一不可变快照。首次发布前返回 `NotReady`，已知场景、局或音源切换立即使旧快照返回 `TargetExpired`。一个变化只广播一次，订阅内继续查询仍使用同一份状态。

控制命令维持独立的实时采集和操作准入。正式租约同时核对局所属会话和场景代次，即使控制器相同，旧场景请求仍会被拒绝。离线原生元数据夹具验证了观察复用控制器而取得写权限仍需要当前发现。真实游戏的帧耗时、Speed/Seek、完整 B01–B19 和新版发行包继续待验。

Release 增量解决方案构建 0 错误、0 警告。九套托管回归合计 954 项通过，四个自有原生 Tick 入口场景通过。五个原生播放夹具共 5,235 项通过，覆盖同局音源两次重建、正常跨局和旧调用仍在途的交接。跨局夹具按独立代次检查 SDK 租约和原生绑定，并在两局和退出阶段比对实际登记的 12 个物理入口。见[本轮验证](../work/runtime-redesign-observation-20261118/validation-r2.json)。

**下一离线工作**：从 [R05](runtime-redesign/CHECKLIST.md#phase-2-需求与共享观察) 修相机空目标发现的间隔，并隔离独立的维护和退休阶段。复用本轮 Gameplay 快照和入口序号做后续验证。

## 最新：组合输入、共享音频和播放服务接入需求计划（11-18）

R03 续批把四项共享音频能力、组合 UI/Input、Gameplay SDK 服务和 LOCAL 目录/选择接入按需启停。UI 查询保持只读，首个自有 Overlay 窗口才创建 Canvas，首个自有按钮才登记激活观察。LOCAL 目录发布与选择分开，已启动的本地游玩和结算展示可持有结果 UI 到实际结束。

音频由 Adapter 在模块更新后统一维护。替换和混音每期共用一次声源发现，撤回其中一项只恢复其修改。最后需求归零后旧视图失效，仍在播放的音源和片段保留到确认停止。长时间等待资源释放的模块可在确认停止后重新探测。实验播放按原有时点提前安装，Gameplay 服务休眠时只推进安装和已有原生责任。

Release 解决方案构建通过，55 条警告、0 错误。音频/内容 1004、UI/Input 79、模块 38、Gameplay 201、Loader 9，共 **1331/1331**。本轮验证覆盖当前源码和离线夹具，既有三类预览包对应上一切片。真实启动、帧耗时、Speed/Seek 和完整 B01–B19 保持待验。见[本轮验证](../work/runtime-redesign-demand-20261118/validation-r3.json)。

**下一离线工作**：沿 [R04](runtime-redesign/CHECKLIST.md#phase-2-需求与共享观察) 把 Gameplay 控制状态收拢到同一按 Host Tick 发布的不可变观察，处理场景与局切换失效。R05 继续做相机空目标发现间隔与独立阶段故障隔离。

## 最新：Standard、Tick 准入和首批按需服务已落地（11-18）

本轮三个分工代理均显式使用 GPT‑6.2 Sol / max。R01 接入 `runtime.json`、普通 Info 日志、验证插件门禁、仅诊断的错误回退及三类包。R02 在启动保留 `RequireReady` 与完整验证，在普通 Tick 改查实际登记通道、线程、epoch、in-flight 和 Unknown。宿主在命令前后各处理一次边界，再采集和发布。Developer 可显式恢复每次完整审计。入口数据从候选匹配后的准入阶段采样，含宿主派发，原游戏函数与采样投递不在统计里。

R03 接入不执行插件构造的清单发现、激活意愿和硬依赖能力需求。工具 UI、菜单资源、外观、相机、三类视觉资源和 DSP 时钟共八个模块可按需启停。最后需求撤回时先使服务视图失效。视觉退休队列仍独立推进。组合 UI/Input、共享音频播放/混音/替换和实验播放继续使用原有维护路径。

当前 Release 构建日志为 [solution-release-r3.log](../work/runtime-redesign-implementation-20261117/solution-release-r3.log)，0 警告、0 错误。当前组件重跑 12 套共 738/738 检查，其中 Runtime 使用当前原生 helper 后 389/389，另外四个实际原生通道场景通过。独立原生传输夹具 274/274。详见 [运行模式](../docs/Runtime-modes.md) 和[本轮证据](../docs/2026-11-18_runtime-modes-report.md)。本轮无真实游戏安装、启动、UI 或账号操作。新包的完整游戏启动、帧耗时、真实 Speed/Seek 和完整 B01–B19 仍待验收。

当前三类预览包的清单与六个 Release 组件一致，原生材料按当前源重建或逐字节核对。合成首次安装、升级、故障拒绝和回滚均通过。见[发行清单](../work/runtime-redesign-implementation-20261117/distribution-readback-r3.json)与[安装结果](../work/runtime-redesign-implementation-20261117/r01-package-validation-20261118/artifacts/release-r3-current-doorstop/results.json)。

**下一离线工作**：先从 [R03](runtime-redesign/CHECKLIST.md#phase-2-需求与共享观察) 拆分共享音频、组合 UI/Input 和实验播放的业务按需职责与原生早装/退休职责。随后迁移 R04 的共享观察。使用当前候选证据，不重复相机接入或复用旧游戏 PID。

## 最新：运行架构收敛方案已落地，下一实现从R01→R02开始（11-17 23:28）

用户先暂停旧goal/实测，要求独立设计整个项目，随后“下一步”授权把设计落到源码审查与实施方案。本批**仅设计、源码静态审查和文档核验**；没有改产品源码、构建、安装、操作游戏或发送请求。旧逐包去重/继续18380实测的步骤不再是默认下一工作；不要自动恢复游戏操作。

新主入口 [.docs/runtime-redesign/README.md](runtime-redesign/README.md)，五份T2文档（PLAN/SPEC/ARCHITECTURE/CHECKLIST/README）及 [审查报告](../docs/2026-11-17_runtime-redesign-report.md)。root+3个Sol/max只读审查：accepted host tick在Host计时之前完整VerifyReady；模块按依赖自动启用、无consumer需求策略；controller/观察与audio共享后端重复采集；两participant成功协调层静态verifyOrigin为4+9+4=17次且进入完整Probe/source链；部分诊断快照在主线程。不是实机测得总调用数或新耗时。r3已有data-only descriptor/availability观察继续保留，不重修旧问题。

新设计保留六项目及现有SDK/依赖/ALC/scope/mutation；Standard与Developer明确分开：启动/安装和显式诊断完整审计，普通入口只做实际登记通道/epoch/thread/in-flight/known/Unknown检查；**明确降低任意第三方机器码/内存改写的即时检测承诺**，不使用success缓存假装原全量合同未变。实际控制仍保留当前局/源/根/Clock、真实writer reservation/fences和readback，Unknown不重试、pending roots不提前释放；早装入口与按需服务不同，不能改成任意晚装。

R01模式/包→R02入口证明与计时→R03清单意愿需求/场景计划→R04共享观察与前后drain→R05唯一维护/退休→R06单一播放operation→R07诊断与成本→R08三个基础实机场景。实现任务全部未开始；先R01→R02做完整切片，不继续逐getter微改，不扩新VM/语言/播放器。基础预览可先交付，倍率/Seek单独实验；完整B01–B19/production仍未完成。

审查输入 `work/runtime-redesign-20261117/source-audit-r2.json` SHA`9ed806543481e0ae7e177314d7cb6709604a94a36e0d6541632b5315c9006224`：46文件hash/锚点、**1513产品输入保持readback-provider-r3**，README保护hash`cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`。`python work/runtime-redesign-20261117/verify_docs.py --check`只读核验通过（53本地链接、8行为边界/8任务），不启动游戏。不借旧1318或其它测试数验新架构；无commit/push。

原当前安装仍为readback-provider-r3/ZIP2f9f263…，最后记录18380/Start22:48:07.8563508；本批未核对其当前进程/画面状态，因此后续不得猜它仍存活或复用旧窗口输入。旧descriptor game-result的Unknown与退出证据保持原样。需要进入实现时以新设计合同分类迁移旧测试；真实实测须作为单独新候选阶段恢复。

## 最新：读取边界/provider修复r3已1318+4安装，新实测18380初始化（11-17 22:49）

当前主树最终`work/game-seek-20261117/readback-provider-r3/`：相对descriptor-r3五Adapter文件/1513输入freeze。ReadbackReceipt改用明确typed读取路径，两端完整receipt验证，Actual.Read与独立after-DSP、fresh accessor入口全部保留；VerifyCurrent末端补同一当前receipt本地复核。Router只选本地source/receipt身份后唯一完整Read，最后复核准确run/binding/source/receipt，防getter同步换源。ProviderForHost只选择准确登记的provider，随后实际provider API完整验证；Capture/RateSource去掉相邻无consumer的重复formal验证，不跨原生读写缓存成功，不改Runtime/native/SDK，不做旧兼容。

新六组件Release0警告/0错误、formalhost3（59344/041d62自然0）；新实际播放四mode338（94659/170902自然0）；新四directed场景93及同module双run/双SDK884=977（44088/8892d6自然0），实际native12次Prepare/Commit、joint transfer/同final Clock、正常独立样本每次DSP+2/pitch+1/media+1，copy/old/thread拒绝不污染合法current；末端graph getter内真实writer换receipt、after-DSP撤销/Dispose、pitch getter内真实handoff均拒绝旧成功并维护根责任，健康drain根0。合**1318断言+4新链接Host**（52831/f9254f自然0）。断言数随实际getter次数下降，不代表独立case数量；controlled metadata/owned不是正式Stock。r1runner缺Core using在编译前失败，原件保留，仅隔离r2夹具补using；中间r1/r2两file构建/338不计最终r3。

Runtime SHA`b59bdea0762978896ae09ff455bc341c5685faf293c889eb0e4e38d32b55a38a`不变；Adapter4530176B SHA`b5f609ec3e297d2e3729754e7e5901910e5a50ce21e021faa3b090cf0ea425b6`；Loader SHA`f5550785eb7b301bc25af73078eb7c1233b199107045218834c86fe283b56ec2`。新ZIP`artifacts/delivery-20261117/RizToolbox-0.3.0-readback-provider-preview-20261117.zip`37375585B SHA`2f9f26382a335bf200fe1ac90efada18add910c5fa5c645c22b982697b930da7`，40/25完整读回（1eb43f）。**22:47:10.3162171安装25匹配**（27323/66d545），安装器无启动；官方sky/Steam默认“玩Rizline”新游戏**PID18380/Start2026-11-17T22:48:07.8563508+08:00**，start-r1再次25匹配，仍初始化。实际窗口23137438，尚未get_window/截图；Steam窗口133694/readbackSteamWindow、启动modal788184/readbackLaunchWindow（旧）。不得复用已退出38592/19599094。

旧descriptor-r3实机已闭合，**旧38592在22:29:35.3119664退出、无游戏窗口**：正常AltF4后187.1390062秒仍不响应，ID/路径/Start三核Stop。该旧包source15.019877s、SDKPause nonce06bef…success和独立Paused、284.264149s后ReadyExperimental，之后status真正fresh且RespondingTrue。唯一0.75x nonce9da673…已发布但180秒无匹配回执，直至退出仍未返；actual stacks在ProviderForHost/完整Probe与GetTime publication/源读取，结果**Unknown、未重发**，无Resume/Seek。完整原件`descriptor-observation-r3/game-r1/game-result.json`；旧结果不验新r3源码。

下一只续本新包：`observe_readback_provider.py <unique-tag>`核25与新session；`arm_readback_provider_pause.py pause-r1`在sourceWaitingForPause/Playing稳定后单Pause（300s，不自动重试）；`send_readback_provider_probe.py action tag [value]`单显式SDK请求。准确新run暂停→Ready/status fresh→0.75x→native readback/Resume/Seek；unknown不重发。不要重复camera/N1/退休矩阵，不用旧包验新源。两个Sol/max本轮已真实参与产品与directed，未超配机器；报告正在补。完整SpeedSeek/SDK clock读出/B01–B19与production未完成，goal active，未commit/push，主README保护不变。

## 最新：查询副作用修复r3已4210+4打包安装，新实测初始化（11-17 22:02）

五Adapter文件/1513freeze：descriptor身份观察与rate/Seek capability观察均data-only；Backend状态/能力查询不进入完整Probe或准备Seek参与者。真正RequireSupported/Probe/Capture/Prepare/Execute和readout完整源/Clock/连续writer/目标检查仍在。Ready维护只对准确同run/session、无handoff/pending作调度维护，fault/drain保留；新run/故障恢复full path。Formal host同无consumer注册段合并重复完整Clock验证。

新六组件Release0/0、formalhost3（26893之后r3唯一23171/31314f）；新owned播放338（43836/13aa66）；3个实际受控SDK双局场景3869**断言**（74156/12e1fc），12次Prepare/Commit、同final Clock、同module/service/两个plugin scope、实际rate/Replay/Resume/跨局和physical-code-fault。不是3869个独立用例；合**4210断言+4Host**（99376/ac2ca7）。metadata synthetic/owned，不签Stock/game。原r2 SDK GetState在36处读DSP实际失败促成r3修复；r3首次669处混同native epoch和SDK generation的夹具只修为SDK state=其SDK lease且新run，旧件保留，产品不变。旧650/832和Runtime同字节210/99/native不计新数。

Runtime SHA`b59bdea0762978896ae09ff455bc341c5685faf293c889eb0e4e38d32b55a38a`不变；Adapter4529664B SHA`5a265619f481ec12de8816d3d52e89874a84a9dca1285607f4269a77da099ac4`；Loader SHA`5708fd0b375a7d554d79175779f068635df32cc8d37acc11fee7627467be829c`。ZIP`artifacts/delivery-20261117/RizToolbox-0.3.0-descriptor-observation-preview-20261117.zip`37375540B SHA`72d726332a9d6b84e708c46c882467f3f724d54c3698a5d6d68c584d3b0bbd7a`，40/25读回（c23f50），**21:59:26.2847005安装25匹配**（15435/6a33ac）。report`docs/2026-11-17_descriptor-observation-report.md`/scope/timeline。主README保护保持。

当前已从官方sky/Steam正常启动，新PID38592/Start2026-11-17T22:00:13.1302671+08:00，初始化中，桌面任务进行。Sky变量descriptorSteamWindow133694、descriptorLaunchWindow21892500；实际新游戏窗口仍须list_windows，不猜旧24839250。observer`observe_descriptor.py <unique-tag>`拒旧session并核25；sender`send_descriptor_probe.py action tag [value]`只显式单请求；`arm_descriptor_pause.py tag`只源WaitingForPause后Playing稳定10秒/6更新，SDK真实readiness仍决定。下一同新PID菜单/同曲→source响应→后台Pause→独立Paused→Ready后的**fresh status heartbeat**→0.75x一次真实SDK请求→Resume/音频/Clock一致性与Seek。unknown不重发。

旧source-verification游戏2748已经21:13:42结束，无旧后台sender。旧包source14.8488秒、Pause success与0.227853秒后独立Paused、498.455726秒后Ready仅定位，不验当前r3源码；旧状态查询实际栈是本修复依据。当前真实Speed/Seek、SDK clock readout、B01–B19/production仍未完成，goal active，未commit/push。

## 最新：新包实机首次Ready，查询事务阻塞定位；四Adapter修复正新r2构建（11-17 21:19）

**当前无游戏、无桌面操作任务。** source-verification-r1游戏2748已21:13:42.870结束：正常sky AltF4后至少83.7003163秒仍不响应，ID/路径/Start三核Stop，sky确认窗口消失。SDK Pause watcher21979自然0；采样34184自然0，无后台请求等待。完整原件`source-verification-r1/game-r1/game-result.json`。

本包source14.8488秒（旧61.075015），单SDKPause nonce6bef360b…success/immediate true，sender至reply18.884785秒，0.227853秒后原生独立Paused同run；相机positionX/scaleX再次绑定，未override。Pause→PreparingClock63.855774秒，**13:08:06.9729770Z首次ReadyExperimental**（距Pause reply498.455726秒）。Ready是host实际阶段，不签SDK时钟readout、Speed/Seek或生产。公共status仍stale，因为Ready后GameplayService.ObserveControlsAfterMaintenance→NativeBackend.CaptureSourceDescriptor→provider完整音频/Clock事务；准确actual栈`game-r1/ready-query-r1-stack.log`。

主树已新四Adapter file：NativePlaybackRateProvider新增明确的data-only descriptor observation接口；Backend只为状态捕获优先观察projection，真正CaptureSourceDescriptor/clock/value/Seek API原完整检查不变；StockRateHost Ready维护只比较当前精确run/session与无pending，同时继续fault/drain，真正新run/transition恢复full path；Router.ProviderHost以真实注册host/Session、run/binding、armed/withdrawn/groupactive只投影不可执行的身份DTO。Formal host创建同一无consumer段复用一次完整Clock证明；新provider仍在实际Capture/Execute按原full校验创建。

新case`work/game-seek-20261117/descriptor-observation-r2/`四file/1513freeze正在构建，唯一controller PTY26893。r1已build过，但final observer在unarmed/withdrawn Session前返回unavailable的边界补正后一律用r2新字节，不能借r1结果。Runtime/native/SDK未改，不重复旧99/210作为新功能通过。后续新实际2run controlled fixture验证data query零DSP/pitch/GetTime、copy/thread/run/pending拒绝、SDK状态Query不消费时钟，真实value/旧handoff仍原验证；新播放338/新链接Host再打新包，不能拿旧650或本游戏验候选。

下一只续26893构建和新projection checks，不重新启动已结束2748或重做camera/N1/退休批。B01–B19/完整SpeedSeek/production未完成，goal active，未commit/push，README保护保持。

## 最新：source verification新650+4通过并安装，正恢复新会话（11-17 20:55）

新5file/1513freeze，Runtime新增显式SourceVerificationScope持双锁，入口/出口完整Group及foundation/target/Dobby/helper/ordinary出口；固定只读receiver/protocol/claimed票验证，不提供Action/音频采样/writer/original/Clock操作。Adapter Capture两图+票同scope；Accessor同binding对象/pause/7句柄身份替代递归，Rooted保留run与registration。实际DSP/media/AudioSource/original消费者继续独立fresh入口。主README保护保持。

本新六组件Release0/0、Hooks210/host3自然0（57197/509e20）；新owned播放338（69433/610cef）；新实际23member scope正常/copy/thread/expired/异常/entry+exit helper及PE头破坏99（41765/78a7a5）；新链接LoaderHost4（90612/27f02d）。合**650+4**，不计旧832。原scope-r1夹具漏mode在native前失败，只修work测试，旧件保留，产品未改。

Runtime1735168B SHA`b59bdea0762978896ae09ff455bc341c5685faf293c889eb0e4e38d32b55a38a`，Adapter4525056B SHA`ed8ffaa898671eb784f7b346af2273a00cdf7ce80110c36a6bad686eb9ad1f14`，Loader SHA`1953a741c1041fd1c7ff1f07dadc63bf537d1753e14b2950391332d6c463b6ff`。ZIP`artifacts/delivery-20261117/RizToolbox-0.3.0-source-verification-preview-20261117.zip`37374223B SHA`ca19dc112db701d6fb739b54cce74c5ba8adab682ebd1751a049bb005e2f5c32`40/25全读回（86d64d），**20:51:43.9303613已安装25匹配**（45054/7d2215），未由安装器启动。新case`work/game-seek-20261117/source-verification-r1/`，报告`docs/2026-11-17_source-verification-report.md`。

当前官方sky已从Steam正式启动新PID2748/Start2026-11-17T20:54:03.3819851+08:00，实际窗口24839250/sourceGameWindow；12:56:41Z WaitingForRun，已观察标题页并点击进入。start-r1与initialized-r1再次25匹配。下一续同PID fresh截图开局→新source耗时→独立SDK Pause与Paused→Resume/Ready/SpeedSeek。Sky变量sourceSteamWindow133694、sourceLaunchWindow23661906，实际游戏窗口须list_windows取，不猜旧37489684。原CUA已恢复工具但当前段仍用官方sky，禁止混入旧state。observer`observe_source_verification.py <unique-tag>`，sender`send_source_probe.py action tag [value]`，自动单Pause watcher`arm_source_pause.py tag`要求新Playing心跳至少10秒/6更新，仍由SDK真实readiness决定。原unknown请求不重发。

旧owner game-result已闭合：相机双绑定，source61.075015秒，Paused→PreparingClock307.269065秒，单SDK Error19不签成功；12秒采样主耗physical/foundation。旧3844已20:17:11结束，不借旧log验本字节。当前新650+4也不能签正式Stock/Clock正向、完整SpeedSeek/B01–B19/production。goal active，未commit/push。

## 最新：真实卡顿采样已闭合，source verification 五文件新构建通过（11-17 20:47）

**当前无游戏、无桌面操作任务。** owner-transition-r2 的 PID3844 已20:17:11精确结束：官方sky AltF4超时，至少141.9065907秒后仍无响应，按ID/准确路径/Start三核Stop，sky观察窗口消失。原件`owner-transition-r2/game-r1/game-result.json`。25安装文件匹配；本包相机position-X/scale-X在12:05:40Z双绑定实际成功，尚未override显示验证。source61.075015秒；唯一已发SDK Pause noncefc64…回执Error19，原生Paused12:08:10Z来源未证明，**不能算SDK Pause成功**；后续pause-r3因stale status未发，r1错误CanDo watcher已取消未发。Paused→PreparingClock307.269065秒，无Ready/Resume/Speed/Seek。

实机微软dotnet-trace9.0.652701采12秒（`owner-transition-r2/game-r1/paused-r1.nettrace`），活动主线程按最近托管caller归属：MemoryFact.Verify20.97%、VerifyReadable19.14%、ReadRanges17.21%、VerifyTweenSourceMemory11.26%、ReadHeader8.84%、VerifyReady8.10%、Memmove3.30%。这是managed thread sampling（含P/Invoke，另一线程等待），不是精确CPU或调用计数。raw speedscope/r1初版把UNMANAGED归属100%，应以`paused-r1-profile-r2.json`及原始trace/stack为准。主因是基础身份/页检查在Adapter递归调用树中放大，不再做byte scratch小优化。

主树已新5file：Runtime新增`ExactHookBroker.Playback.SourceVerification.cs`，显式双锁/注册/同线程/同generation scope；只开放固定receiver/protocol/已claimed-ticket查询，无Action/sample/writer/original/Clock API；入口及出口完整Group body，同一traversal保留foundation/targets/Dobby/helper/ordinary完整出口。Adapter Capture两graph+ticket同scope，Actual借用同7句柄/同binding证明替代重复graph，Rooted同同步graph末仅本地归属复核；所有真实DSP/media/audio/original/action消费者继续独立fresh入口，不ambient缓存。新source1513 freeze，**Loader六组件Release0/0，Hooks210和formalhost3通过**（57197/509e20）；当前新播放338/实际23member source-scope entry/exit/copy/thread/expired/sticky检查待闭合，再新链接Host/打包。不能用旧832或旧游戏验新源码。新case`work/game-seek-20261117/source-verification-r1/`，不要重复camera/N1/退休矩阵。

Sol/max三代理在本轮候选阶段报usage limit而停止，未产出候选；root接手实际五file实现。不要为此转账号或反复重启agent。真实完整Clock/SpeedSeek、B01–B19/production仍未完成，goal active，未commit/push，README保护保持。

## 最新：Owner/transition/expected池已832+4安装，新游戏3844初始化（11-17 20:01）

六文件相对helper-r2新freeze1512，主README保护保持。camera双支同Info owner替代未证层级；同准确无predecessor且无pending的负向transition hint保留原生故障/drain，不重复每frame全组；新run/pending/finalfalse信号恢复原完整验证。每seal exactlength scratch池每次canonical全重置、独占，成功clear归还、异常不触旧array并discard。三项已合主树，SDK/native/Examples未改，Sol独立复审无必须阻断。

新r2七build/check自然0（2639/4192d5）：Hook210/Camera63/原formalhost3；新播放338（63575/df5db5）；新边界9+buffer5+actual helper67+负向transition137=218（19718/6e5592）；新链接Loader受控实际Host4（33848/645ba7）全0，合**832+4**。137含test-only失败dispatch/pending标记与计数注入，不签真实finalfalse回调/source/Clock。r1的cachedIsActive断言62/63失败原件保留，仅测试修为实际SetValue拒绝0writes→Refresh撤销，新产品重新构建检查；不能拿旧687验本源。

Runtime1733120B SHA`b07a0d066ef850c33882e12d9cd0c459b3576dc7916e47a13b017081b5257017`；Adapter4524544B SHA`e7852f07f061369c316d01dc5cd99f38f2824e57428e5d5af07dc6ffe97d627d`；Loader SHA`520f19878f6308373eabfaa4930b57f41674dab3b771cce468c663ded26fa0f8`。ZIP`artifacts/delivery-20261117/RizToolbox-0.3.0-owner-transition-preview-20261117.zip`37373255B SHA`5c62946509b5f47fe2bcfd0c1fa8625f7e720c16f00683742a525db5c256ab14`，40/25读回（6e38fe）；**19:59:32.4576019安装25匹配**（76505/39f608），installer无启动。新case`work/game-seek-20261117/owner-transition-r2/`，freeze SHA`b6eb5f03802e86218e1a043542c2b0ca38067c695ca2e03a3f2030d74c519652`；report`docs/2026-11-17_owner-transition-report.md`/scope/timeline已新建，旧相机静态证据只作历史原件不覆写。

随后官方computer-use sky从Steam库正常启动玩Rizline，**新PID3844/Start20:01:03.2832855+08**，初始`game-r1/initial-observation.json`；当前仍初始化，游戏活着，Sky桌面任务进行中。原42392已经正常退出不再续。Node内sky已重新import（工具runtime曾重置），ownerSteamWindow133694、ownerLaunchWindow17501274；还没新游戏window选择，不猜旧23137484。用sky.list_windows选择返回的实际Rizline.exe窗口。原CUA namespace撤回，禁止调用，无需等人工。

**下一只续本新会话**：`observe_owner_transition.py <unique-tag>`拒早于新安装的旧session，核25；初始化完成→fresh screenshot菜单/开局，相机实际绑定→source响应→在准确新run且canDoOperation true时后台SDK `send_owner_probe.py pause <tag>`一次（sender单在途/TTL/nonce，不自动重试unknown）；独立Paused→Resume→ClockReady/SpeedSeek。尚未新run/新请求/新实际成功。旧helper65.2372419秒/错误19只故障基线，不签本新源。B01–B19/production未完成，goal active，未commit/push。

## 进行中：相机owner/负向transition idle/expected池已新build，正新播放检查（11-17 19:57）

当前无游戏。三候选已合主树六文件：camera backend+NativeCameraChecks、StockRateHost/Router.Successor/ObservedInitialization、ExecutableSeal。source freeze1512，case`work/game-seek-20261117/owner-transition-r2/`；本批不改SDK/native/Examples。主README保护保持。此新字节不能用旧helper-r2 687/65秒实测验收。

r1七步controller在camera新增用例62/63停止；新测试错误地期待未Refresh的缓存lease.IsActive立即轮询graph。产品源码无故障，root只改该测试：SetValue必须拒绝且0 native setter，再Refresh撤销，原件/说明在owner-transition-r1/camera-test-correction.json。r2新freeze/七build-check步骤已全部0（2639/4192d5），Hooks210、Camera63、Gameplay --formal-playback-host-checks原3。Product六库新Release已完成。

当前唯一串行 `run_owner_r2_playback_checks.py` PTY63575（新实际22source/23PrepareCommit/3pair播放四mode338）；随后`run_owner_directed_checks.py`为新有界9、expected池5、helper实际组67、三种负向transition Host Pump定向，均尚未完成。新transition夹具在owner-transition-r2/helper-transition-checks/：真实group，真实observer association/真实router ctor，test-only设置已经拒绝的调度状态和惰性pending marker；不造native成功receipt，不签actual false-return callback或游戏。通过后实际Loader Host4，新pack/install，再实机。

两Sol独立复审无必要阻断：same明确Unsupported且无pending owner才idle；旧被撤回run的tracker不再推进，真正新的pending/rawfinalfalse/新run/fault重新走全验证。pool仅exactlength独占scratch，使用前全量canonical复制，成功归还、异常不触旧array并丢弃；五组fixture待执行。相机同Info双支owner替代未证明的hierarchy，所有运行/引用/状态/Root/线程/setter保护保持。下一不要重跑退休矩阵或借旧687，本批功能及整体ClockSpeedSeek/B01–B19仍未完成。

## 最新：helper-r2实际仍65秒，Retry无效切换热循环已定位；游戏正常退出，三候选待合（11-17 19:41）

**当前无游戏。** 已19:38:49通过Sky AltF4正常退出42392，无强制结束游戏。官方computer-use skill `@oai/sky`运行库可用，当前Node已import sky；原CUA namespace仍撤回，不要误等人工或调用撤回工具。新Sky任务已观察到游戏窗口消失，接口无completion_summary方法。Steam窗口133694/helperSteamWindow仍存在；原helperGameWindow 23137484已过期，不再输入。

本新helper-r2 687+4/ZIP2977df…与25安装文件始终匹配，实际PID42392/Start19:17:16.5611536，gameSession74d07b01c23c40cf82c39ba6466a9244，probeSession3664cf43527c4b51ab11a3c451f90d85。启动约2分49秒才WaitingForRun；首局run a0dac73781414a72aae3ec63c608c210/gen3 source65.2372419秒后WaitingForPause，随后实际FAILED RESULT0hit/479miss。pause-r1 sender等到没有run后拒绝，未发；保存的request/result是旧activation，不能当本次控制。重试后run f5478123c2394dc1b1a9506d52052f56/gen7/scene6，自动arm只发一次Pause nonce9546e8f2421649cf84d3717da8e33660，回执11:33:31.3781132Z SDK Success=false/Error19 countdown未结束，native original Pause计数0，transport exit0不当成功。新的pause-r3要求canDoOperation=true，但始终false，19:38按唯一Python路径+命令行核Stop仅等待controller43048，未发新请求。没有Resume/Speed/Seek/Ready。

实际阻断：Host报TransitionUnavailable("This transition has no unconsumed armed predecessor for an observed initializer.")仍逐frame循环full Group/profile→Router.GetOrPrepareObservedSession→HasOrdinarySuccessorBoundary。新actual栈`work/game-seek-20261115/helper-r2-retry-r1-stack.log`；不是旧log验新源。相机新首败稳定camera-outside-rig-transform。原件`helper-image-r2/game-r1/game-result.json`、current-session.log、单请求/拒绝与cancel、game-exit.json；本批report已补E09/F06/F07。

**下一合三候选（尚未主树/build，主树仍helper-r2 1512 freeze）**：
1. `helper-image-r2/camera-ownership-next/`，Sol camera_lookup_review停写：两个file backend+NativeCameraChecks，manifest用changes/basePath格式。完整方法证据只证明CameraControl改ownTransform、NoteJudge.Start单独缓存Camera.main，未证明parent；以同Info的精确双支owner graph替代hierarchy。仍保留所有roots/classes/scene/active/run/ref/flags/transform/线程和setter保护。root逐diff已审，无Fake变化；测试尚未跑。
2. `helper-image-r2/expected-buffer-next/`，Sol current_read_evidence_review停写：单ExecutableSeal，manifest files；每seal exactlength独占scratch，canonical全量重置，完整compose/compare成功才归还并clear；异常丢弃不清写旧array，避开CompositionSection异常别名。root逐diff/五组fixture已审，待实际验证，不宣称实机改善。源码SHA6015fbf…；checks五组含嵌套/并发/异常/完整首差。
3. `helper-image-r2/transition-idle-next/`，Sol persistent_scene_checks正在实现Host+Router Successor/ObservedInitialization调度hint：只针对确切无predecessor的Unsupported保存actual association+pending owner状态，不用每native call序号（例行MoveNext会打穿）；每次先维护fault/drain，same run且候选状态无变化才idle；新run/真正pending owner/native completion变化恢复全验证。必须复审pending可变对象内部状态，不得跳过实际可完成transition，也不发新权/重装第一票。等agent交manifest+短测试后再合。

下一唯一串行新freeze/build（六库Loader+必要Hooks/Camera/Host/新调度与buffer夹具+新字节播放338），别用本687/旧包签新源，不恢复旧矩阵。新包只真实功能和阻断修复；相机X/N1/退休批不重做。B01–B19/完整ClockSpeedSeek/production仍未完成，goal active；README保护保持。

## 进行中：官方sky运行库恢复实测，新游戏42392安装中（11-17 19:18）

原CUA namespace仍撤回，但已按computer-use skill的官方`@oai/sky`运行库恢复实际桌面操作；并非手写UIA或外部注入。无需再等用户手动开局（此前async问题已被当前自动工作取代）。Steam正式库→开始游戏→玩Rizline正常入口已实际启动，PID42392/Start2026-11-17T19:17:16.5611536+08，准确exe D:\SteamLibrary\steamapps\common\Rizline\Rizline.exe，窗口23137484（已由sky.list_windows返回）。Node REPL变量helperGameWindow/helperSteamWindow，官方sky已import。**此Sky桌面任务仍进行中，当前游戏活着。** 原CUA段早已completion，不要向已撤回namespace发调用。

helper-image-r2新包SHA2977df…已经19:05安装25匹配，19:18新observer再次25匹配，记录helper-image-r2/start-r1-observation.json及start-r1-toolbox.log。新游戏仍source prepare PlayerConstructor，日志每2秒有新安装stage，并非卡住已终止；不要重启。第一次激活游戏因初始化不响应10秒timeout，已停止用旧坐标，下一按新窗口选择/状态恢复，仅重试一次。初始精确进程记录game-r1/initial-observation.json。新run/相机/Pause/ClockReady/SpeedSeek尚无结论。

下一续同42392初始化；待实际安装完成再sky fresh snapshot→标题/菜单/选谱开局→本新session source接管、camera首败码→后台SDKPause实际回执+独立Paused→Resume→ClockReady/SpeedSeek。旧persistent Pause经reconciled已证独立Paused，但不验新Runtime。源冻结1512/687+4保持，不能拿旧包或旧栈验当前。整体B01–B19未完成，goal active。

## 最新：helper-r2已687+4通过/打包安装，等新开局（11-17 19:05）

**19:13补证**：当前仍没有新游戏进程/新SDK请求。旧persistent-reference日志实际上在Pause回执后约0.227秒已原生lifecycle采样同run Paused=true；旧“无后续样本”是SDK mailbox仍stale而遗漏日志，已在game-result-reconciled.json及报告E12纠正，原件保留。不把旧Paused签作helper-r2通过，Ready/SpeedSeek仍缺。

**当前无游戏、无CUA任务。** 电脑操作CUA namespace已撤回，未操作游戏UI。新包已经19:05:13.7549142安装25文件匹配，安装未请求启动；已通过async请求用户从Steam手动开局保持前台，待回复。用户允许必要实测，勿为旧Escape再要授权，不操作账号。

新主树六文件相对persistent-reference、1512输入冻结，相对helper-r1仅Group+Hub两file再改。核心：尾部22成员helper整图至少66遍→两个纯查询batch四遍，PumpCompleted在batch外，traversal另有实际helper完整入口/最终出口；不跨真实getter/Read/original/callback缓存。Section只组合本节owner，尾部仍全验；RangeSeal及完整执行节比较改4KiB+15B有界stack snapshot，保留所有字节/页身份及首差。相机只首败诊断，原准入条件不变。

**r1实际helper-image-exit用例发现最终出口遗漏，未打包r1。** root补actual helper seal保留/完整入口/Dispose最终出口（异常也尝试）后，新r2五步骤build0/Hooks210/Camera63（41064/935983），新播放338（21507/4d282e），新边界9+helper实际组67=76（96900/d89564），新链接Loader Host4（57846/fc921d）全部0，合**687+4**。正常/入口篡改/受控ObjectClass元数据投影后篡改三个真实22source/23Prepare+Commit/3pair进程通过；恢复仍sticky，0clock/roots。owned无StockTargets，不签Stock/真实game/Clock。旧r1/r2失败夹具原件保留，不能用旧611/736/788验新源。

当前Runtime1731584B SHA`cc6dbbefa8ee5f1b23e089621cdf5c190e063ac1af468e795905fbc07740e93f`；Adapter4521472B SHA`25163eb51adc5ea1e3ad5f0a4d386edd862fd4ebbc7ea86c86483384e3250c5d`；Loader55296B SHA`10432357385ca424e4a0866592a75f045e3275bc9f1ad17c9a7700e3fd86f89c`。当前ZIP`artifacts/delivery-20261117/RizToolbox-0.3.0-helper-image-preview-20261117.zip` **37371116B/SHA`2977df032836427ea380c8eff5532d2ed973b418eb36ebdfbda9a478760bc2a5`**，40/25全读回（89ee15）。Install-HelperImage.ps1 50392/a07b35自然0，25已匹配。case`work/game-seek-20261117/helper-image-r2/`，freeze SHA`27500ccdb238e42a9df5b5884754d8c4929052e2e4f234901dbaafd6551ee882`；正式报告`docs/2026-11-17_helper-image-report.md`。主README保护保持，未commit/push。

**下一仅本新包实际观察**：用户新开局后先读PID/Start/session与25安装文件，`observe_helper_image.py <unique-tag>`会拒绝早于安装的旧session；`send_helper_probe.py pause <unique-tag>`会拒旧status，sender仍单在途/nonce/TTL，不自动重发unknown。先camera首败码+source接管→唯一SDKPause→后续Paused样本→Resume→真正ClockReady/SpeedSeek。新请求尚未发送。若仍长卡只取本新PID actual stack，不重跑已过687+4。旧persistent实际source70.22秒/Pause76.51秒只故障基线，不签本包效果。B01–B19/production/完整SpeedSeek仍未完成，goal active。

## 进行中：helper批量/有界读取已合，定向测试发现缺少helper最终出口，正构建r2（11-17 19:03）

当前无游戏、无CUA任务；上个12344实测已精确结束，工具CUA namespace现被撤回，不操作游戏UI。用户仍授权必要实测，勿因旧Escape重问；待工具恢复再实测新字节。旧736+4/70秒source不能验本批源码。

主树相对persistent-reference六文件已合：Group/SourceOwner/Hub.MemberEvidence/ExecutableSeal与相机backend+NativeCameraChecks。r1新freeze1512、5步自然0（97416/85aaf8），Hooks210/Camera63，新绑定22source/23member/3pair播放338（6143/c8583e），当前新链接实际Loader宿主4场景（51144/989b5d）；9个有界比较case也已新字节0。r1 Runtime1c99b106…/Adaptera90ee908…/Loadere1216a4e…只属于r1，未打包/安装。

helper-directed r1夹具先有根路径预检失败和bootstrap旧mode白名单失败（原件保留，无原生业务执行），root修好仅夹具后directed-checks-r2正常/改helper入口两个case通过；**helper-image-exit实际失败**：真实ObjectClass projection期间改helper后被接受，group仍active。这是实质缺口，不把前面611+4或9当修复通过。已补traversal保留原helper image的完整入口/最终出口；失败时也尝试出口，group若失效仍检查原seal，无新增票/权限。仅又改Group+Hub.MemberEvidence两file，差异/旧版在helper-image-r1/helper-exit-fix.json和pre-helper-exit-fix。

**当前唯一串行构建** `build_helper_image_r2.py`，PTY41064；新freeze/case为`work/game-seek-20261117/helper-image-r2/`，1512输入，相对r1恰2、相对persistent-reference仍6。尚未完成r2检查，不能借r1签新源。下一依次`run_helper_r2_playback_checks.py`、`run_helper_r2_directed_checks.py`、`run_helper_r2_host_checks.py`，通过后`pack_helper_image.py`（现已指r2），再只安装25文件，不盲开游戏。新相机只首败诊断，原准入谓词不变；B01–B19/ClockReady/SpeedSeek/production仍未完成，goal active，主README保持。

## 最新：新包实机外观发现恢复，Clock仍阻塞；12344已结束（11-17 17:28）

persistent-reference-r1 的新源码/包仍保持冻结：1512输入、8变化，当前736项+4Host通过；新ZIP SHA7cd9511bd01a315d916e11c98d4db3cd40330daf1eac74c83354564724ff2a80，17:07:18安装25匹配。Runtime2b9ba399…/Adapter957f3468…；主README保护保持。不要拿旧788或本次736验之后的新源码。

本包新游戏12344/Start17:08:48.8851647，diag20261117T090923343Z-1919c01ff8fc4ca18e9242fac3a24ef5，run2311ee9f7a4547c68078f36a4de5c78f/gen3：source CapturingSource→WaitingForPause **70.221616秒**；唯一SDK Pause回执09:17:47.5240148Z success/Dispatched/immediate true，sender起至回执**76.514717秒**，无后续独立Paused样本。回执后**337.651954秒**才PreparingClock，未Ready，没发Resume/Speed/Seek。实际source/pause/clock三个栈在work/game-seek-20261115/persistent-reference-*-r1-stack.log；Clock位于VerifyMembers→SourceOwner→StockSourceHub→ExecutableSeal.RangeSeal完整扫描。分钟级阻塞仍是功能未完成，不能签Clock或生产。

**外观真实发现恢复**：同run出现45条game.track.group.ring-color绑定，尚未视觉覆盖测试；相机不再漏找controller，但38次graph TargetExpired，首败谓词未知，不签相机可用。CUA正常AltF4后61.341924秒仍不响应，17:28:08按PID/路径/Start三核结束，已确认消失；下一tool已CUA completion。**当前无游戏/无CUA任务。** 原件`work/game-seek-20261117/persistent-reference-r1/game-r1/game-result.json`，正式报告`docs/2026-11-17_persistent-reference-report.md`，包/代码结果与实机边界均已区分。

**下一离线工作**：已有Sol/max两项审查锁定实质重复：同一个StockSourceHub helper image每source member至少3遍扫描，22成员至少66遍/一轮VerifyMembers，另composition还会进入；正准备同一次持锁纯验证的helper seal批量入口/出口候选。必须保留每成员登记/epoch/原跳板/own页身份与sticky，遇PumpCompleted真实外部回调/原生消费须结束旧scope，不跨实际gamegetter/Read/original缓存。不要仅为Adapter72→70的小去重再跑一局。另camera-next已有首败诊断候选，正在补定向测试；两项尚未主树合并/build，不能借736验收。下一审候选→新freeze→必要定向构建/检查→新包，才考虑实机。相机X/N1/退休批不重做，B01–B19仍未整体完成，goal active。

## 进行中：常驻控制器发现修复与reference-pass整合（11-17 16:49）

**当前无游戏、无CUA任务。** occurrence-profile进程32912已在正常退出请求后等待121.6247708秒仍无响应，16:39:37按ID/路径/启动时间三核结束，已确认消失；CUA已一次completion。下方16:36“游戏尚活”仅历史。实测原件`work/game-seek-20261117/occurrence-call-profile/game-r1/game-result.json`已闭合：音源78.402008秒，唯一SDK Pause即时后置true但无后续Paused样本，回执后388.843861秒才到PreparingClock，actual stack已到TryPrepareDormant，仍无Ready；没有Resume/Speed/Seek，不签Clock或B01–B19。

两个Adapter backend候选与4测试候选均在`work/game-seek-20261117/persistent-controller/`停写；正复审并准备合入。核心是独立校验active scene与每个对象自身valid/loaded，保留真实run、唯一controller、activeAndEnabled、引用与祖先链。新增8个命名用例，尚未运行，不使用旧788验候选。另两个Runtime reference-pass候选正在独立只读复审；6模式真实ordinary section-state测试只在work，不证明非空ordinary与typedGroup联合或双PE节Game路径。主树编译输入尚保持occurrence-profile1512冻结，下一按新冻结构建/验证后才打新包。

## 进行中：occurrence-profile实机32912，已Pause/PreparingClock；持久场景与reference-pass隔离候选（11-17 16:36）

**本节仍是进行中，游戏尚活，CUA段未completion。** 当前游戏PID32912/Start16:17:32.5025685，实际窗口7539720，CUA绑定`occurrenceGame`。仅从Steam正式游戏启动，25文件与occurrence-profile包90fe03bf…逐一匹配，源码freeze1512保持，尚未改主树。诊断session20261117T081802109Z-cb6b88c7480842de86fae632bc3dd0b1；游戏sessionbe7f69b170674f8fb3eb33961dff1cfc/run8f8e09c19d524027a8b21d4528b0d635/gen3/GreatnessEarth EASY4 FREE。不要续旧35088。初始记录`work/game-seek-20261117/occurrence-call-profile/game-r1/initial-observation.json`。

本新包source CapturingSource08:23:39.2821369Z→WaitingForPause08:24:57.6841442Z=78.4020073秒。已唯一后台SDK Pause，nonce a470457d37cd4199901f80c92d3520ad，回执08:26:53.8122686Z Success/Dispatched/ImmediatePostconditiontrue；请求前Playingtrue/Pausedfalse，返回后主线程长时间未采样。发送controller `send_occurrence_probe.py`原82463/7c06b8自然0（不是unknown重发），请求/回执原件已存game-r1。**未发Resume/Speed/Seek**。08:33:22.6561299Z日志首次进入PreparingClock，16:36栈已到Session.TryArm→ClockPublication.TryPrepareDormant→VerifySource的完整图校验，仍无Ready。

本次actual栈均existingMicrosoft dotnet-stack只读自然0：`work/game-seek-20261115/occurrence-profile-source-r1-stack.log`、pause-r1（Readback.Read→PreparePausedClock）、pause-r2（Publication.PrepareFresh ctor）、clock-r1（TryPrepareDormant）。只验本新包，证明阶段仍推进但分钟级阻塞不可交付，不能当Clock支持通过。CUA恢复可用，当前还未请求退出；若结束需正常AltF4并观察，精确结束后下一tool只completion_summary一次。

同时新实机复现appearance/camera一直TargetMissing，已找到确切功能bug：真实controller自身scene=-12有效已加载，active scene=Level；Gameplay/事件在所有加载场景找唯一activecontroller，而两个外观backend错误要求controller自身scene名==run.Scene。Sol `/root/camera_lookup_review`已在`work/game-seek-20261117/persistent-controller/candidate/src/...`两个backend修正：独立CurrentScene(run.Scene)核active scene，controller/rig/Camera各自仅valid+loaded，保留唯一exactclass、GO active+Behaviour activeAndEnabled、run matcher和整个graph/相机祖先链。base-hashes在candidate/base-hashes.json。**未主树合并/build**；`/root/persistent_scene_checks`正写隔离tests-candidate（4测试file），与sourceagent已协调。

另一Sol `/root/current_read_evidence_review`正准备`work/game-seek-20261117/reference-pass/candidate/`两Runtime副本：仅一个locked pure VerifyCurrentBody内普通ReferenceImageMembers owner/generation/member完整入口、每节独立canonical状态、末完整验证，将3次Capture/6遍ordinary扫描减到1次/2遍；没有跨nativegetter/Read/original/回调成功缓存，不复用单节Snapshot。以本新source栈为依据，不重复之前composition-read。**尚未交候选、未合并/build**。root继续观察当前游戏，等候选+测试齐后才考虑退出并合入新freeze，不用当前788/本游戏验新源码。B01–B19/production仍未完成，goal active，主README保护保持。

## 最新：caller/profile与pair重复读取修复已788+4通过，新包仅安装（11-17 16:14）

**当前无游戏/无CUA任务。CUA工具已恢复可用**，不要再按下方过时“工具不可用”等待人工。旧r3进程35088已16:00:48结束，正常AltF4等待120.268秒无响应才按ID/路径/启动时间核Stop；CUA已一次completion。旧r3后台SDK Pause因果成立，Resume回执后144.900714秒实际Playing，source90.875657秒，Pause2后栈在PreparePausedClock但无Ready/Speed/Seek。原件`module-control-mailbox/game-r1/game-result.json`及current-session.log/3请求回执/4 actual栈。**这些只验旧r3 Runtime eab5…，不能验当前四file新源码。**

新四Runtime实现已冻结1512输入，仅Group/Producer/Lease/ExecutableSeal变化。构造期profile同双锁typed traversal完整入口/出口、7/9固定CALL逐条核；独立StockLeafCaller和后续native观察保持fresh。pair executable gaps的重复RangeSeal字节拷贝改页身份，完整section pinnedfile+DIR64+成员patch比较、header/非exec/成员原验保持；不跨getter/Read/native/original缓存，不发行新票。base/candidate/applied-base均存work；主README保护哈希保持。独立Sol最终只读复审无必要修点。

`work/game-seek-20261117/occurrence-call-profile/source-freeze.json`有效。新build4步骤自然0（47412/ec43c6），Hooks210；新DLL/同nativehelpers四Replay/Resume/direct-time/direct-engine **338**（18462/4d2c75）；独立supporting5进程 **143**（70573/a31731）；新caller32+pair-gap34+pair-header31 **97**（88182/ca7061），均0，合计**788**。真实owned23/23+3pair，Stock=null，不签Stock正向或实机性能。gap0x2d17/header0x40修改拒绝、还原仍sticky；caller9地址与独立查询/固定RVA/actualCALL一致。新链接Loader受控实际host **4/4**（88626/3ee18c）已重跑，不能借旧链接PASS。native未改旧442仅同字节继承，不计788；Examples仍49fb…，原27+sender7也只继承，不计788。

新Runtime1724416B SHA`2d27b718f8c635d4ff12c09f05dcd780801d98dae91f883088855ef21c0e97ca`；Loader55296B SHA`620bde88d58812528d92af07cf6306efc518240bb4251b7c75fa75bb33b435e3`，Adapter新链接4ce5de4f…；公共SDK/Core/Hosting/Examples同前字节。当前最新包`artifacts/delivery-20261117/RizToolbox-0.3.0-occurrence-profile-preview-20261117.zip`**37368320B/SHA`90fe03bfac107e5b8af0618443535c236c0c4e1282a41fe9eb9775781af8a1fb`**，40成员/25运行文件全读回（c3e006）。`Install-OccurrenceProfile.ps1`73885/882b46自然0，**16:13:06.6558038安装25匹配，无Steam/游戏启动请求**。安装证据occurrence-call-profile/install-observation.json；报告`docs/2026-11-17_occurrence-profile-report.md`、交付说明更新。旧sdk-probe-r3等包保持作历史。

**下一只从本新包实测继续**：Steam启动→核本次PID/session→开局/音源接管耗时→后台SDK Pause（当前sender已含r3跨激活回执保留修复，不激活UI造成焦点Pause）→Resume→再Pause→真正ClockReady/Speed/Seek。旧请求有匹配结果才能覆盖，unknown绝不自动重发。若仍分钟级卡顿，按新包实际栈定位，不拿旧栈/旧788为未改源码再做无关测试。exact scheduled child实际成功、完整B01–B19与production仍未完成，goal active。不要重复相机/N1/退休批/当前788+4；功能优先，性能基准仍后置。

## 最新：r3真实SDK Pause已证，Clock仍卡顿；四Runtime修复正构建（11-17 16:04）

CUA工具本轮恢复，已从Steam官方库实际启动r3，25安装字节核对匹配。PID35088/Start15:41:27.7513686，诊断session20261117T074159815Z-ba132ab6582e49c4a194fe946861418e，游戏session773d9c6901eb47f4a14328bbaed7b19d/run9d8333368785454087b0e532f6be9eb0/gen3/GreatnessEarth EASY4 FREE。**仅属r3旧Runtime eab5…实机，不能验下述新四file。** source CapturingSource07:46:32.717928Z→WaitingForPause07:48:03.593585Z=90.875657秒，仍不可交付。

当前公开SDK后台mailbox已实际执行三个有各自nonce的命令：Pause1回执07:49:14.580success/ImmediatePostconditiontrue，前Playingtrue/Pausedfalse，后07:49:15采样Playingfalse/Pausedtrue，因果成立；Resume回执07:50:43.271success/Dispatched/Immediatefalse，07:53:08.172后采样Playingtrue/Pausedfalse（回执后144.900714秒）；随后Pause2回执07:55:09.469success/immediatetrue。Pause2后实际栈已进入Router.PreparePausedClock，但无后续新状态或Ready。没有发送Speed/Seek，无exact内部child独立成功结论，未签Clock/提交/production。

正常AltF4已发，15:58:47首次观察后120.268秒仍不响应；16:00:48按ID/路径/启动时间三核Stop，确认进程消失；不是正常退出通过。CUA已一次completion，当前无游戏/无UI任务。原件`work/game-seek-20261117/module-control-mailbox/game-r1/`：game-result.json、3请求/回执/发送端记录、current-session.log、game-exit.json；四actual栈在work/game-seek-20261115/sdk-probe-r3-*-stack.log，分别定位ctor StockLeafCaller反复fullgroup、native callback组校验、Resume返回readback、PreparePausedClock。

已从隔离副本合入**四Runtime file**：Group新增构造期一次typed traversal固定7/9caller批量查询，producer ctor由先Verify+9查询缩为一次profile，lease仅proxy；独立StockLeafCaller及后续Observe仍fresh。ExecutableSeal的pair executable gaps改同group的MemoryFact身份验证，完整节pinnedfile+DIR64+成员patch字节比较仍每次执行，header/非执行/成员原验保持；不跨getter/Read/native/original缓存。此前ctor10次至少520operation/1280range重复，是新actualstack确认。候选/基线在occurrence-call-profile及pair-executable-range目录，主README保持。**新源正在build_occurrence_profile.py串行构建（47412），尚未新包或新检查通过**。Sol分别写真实owned caller positive与gap/header破坏夹具，均只work；root唯一build/run。下一只完成本新源定向验证与新包，不能把本r3实机/旧691当新修复通过。

## 最新：请求入口r3修正旧会话回执/27+7通过并安装，等人工开局（11-17 15:33）

**当前新候选为r3，不是下方r2。** 最终复审发现旧session的拒绝Publish会覆盖旧实际result，使sender无法匹配旧request；已改为仅status.inputError ScopeExpired，不改旧result，不Acquire/Execute/消耗新预算。无旧result仍保持unknown，sender不自动覆盖未知请求。Sol新测试覆盖旧成功回执跨激活逐字节保留；原OldSession覆盖未知旧请求不伪造结果。独立Sol只读再次复审无必要修点。

`module-control-mailbox/r3/source-freeze.json`1512输入/相对target-family8变化。**r3新构建三个步骤全0、27/27**（20152/a9361b）；发送脚本仍相同字节7场景0，未重跑。Examples212992B SHA`49fb1e95b5ed6a00d8b4d2194eb8fecee164ea6d58f720a7156d8a41bb0c03bc`，其余宿主/Runtime/Adapter/SDK/native产品字节保持，原691+4只按相同组件继承，不能验新Example/游戏。r1/r2原记录保留，不当r3源码通过。

**当前最新包**`artifacts/delivery-20261117/RizToolbox-0.3.0-sdk-probe-r3-preview-20261117.zip`37366478B SHA`ac74e0e22fa1062aec7e079c95e6fe98c5bfc5739ef41eecea20ab25e01ebd14`；40/25全读回（234503）。`Install-SdkProbeR3.ps1`57626/7fb6a2自然0，**15:30:59.7881147安装25匹配，没有请求游戏启动**。安装记录`module-control-mailbox/r3/install-observation.json`。报告与交付说明已更新，主README保护哈希保持。

电脑操作接口依旧不可用。人工开局问题已发，尚未取得新实际会话或发送真实控制请求；下一等用户回复后按约定延迟20秒，先只读session/心跳，再新SDK Pause实际回执及后续采样→Resume→ClockReady/SpeedSeek。scope/TTL/nonce规则与下方r2一致，旧session只状态报错这一处已修正。不要再次自动重发unknown请求。实际卡顿、exact child、B01–B19整体、production仍未完成，goal active，不重跑退休矩阵。

## 最新：SDK请求入口已实现/26+7通过，新包已安装，待人工开局（11-17 15:26）

为避免CUA激活窗口先触发焦点暂停，新增SDK-only `ModuleControlMailbox`，只在既有 `example.panel/runtime-module-proof` Enabled scope内，由1Hz FrameUpdate通过公开Storage Data处理四种显式请求，调用现有Acquire/Execute。没有请求不控制，不加Loader/native/SDK权限；当前Runtime/Adapter/Loader/SDK/native代码与target-family一致。当前电脑操作接口仍不可用，**未请求启动游戏，尚未发送真实控制请求**；已问用户手动Steam开局，回复后保持前台，等20秒再后台SDKPause。不要拿旧焦点暂停采样当SDKPause。

请求严格session/nonce/run/generation、4KiB/精确字段/单位/有限值/期限；Acquire前消费nonce，每激活32上限，验证失败也计入。lease匹配后再次核取消及过期，所有动作显式request；结果写失败仅补写，不重放。保留实际SDK Result，Dispatched不升级，值Confirmed不签Clock/提交。发送脚本`scripts/Send-ProbeControl.ps1`单写者/单在途/原子发布，拒未完成请求覆盖，超时unknown不重发。实际Data路径为`BepInEx/RizToolbox/plugin-storage/storage/plugin-example.panel/data/runtime-module-proof/`；配置仍`config/plugin-example.panel/config-runtime-module-proof.json`（当前游戏已Enabled）。

最终`work/game-seek-20261117/module-control-mailbox/r2/source-freeze.json`1512输入/相对target-family8变化，含.sln及Build.ps1正式登记。r1曾24通过，但随后新增Acquire期间过期/取消保护与PS旧时间兼容，r1不验新源；r1原件留存。**r2三个步骤全0，26/26**（PTY19953/d5027b）；同当前脚本7个假目录场景退出0，结果在sender-checks/result.json。新Examples212992B SHA`6ce8a6d7e8144af108213702c65f71c8857abd052b6eec5fc9cd65d02c9f1243`。测试为同源SDK替身，不能签真实游戏/native/Clock。原691+4只按相同组件字节继承，不重跑/不当本轮新检查；主README保护哈希保持。

**当前最新包**`artifacts/delivery-20261117/RizToolbox-0.3.0-sdk-probe-preview-20261117.zip`37366421B SHA`a62f72147816b66ef3bd91fc0825395e86579b8981c73a438213dbea9e5ce1ac`；40成员/25运行文件全读回（dc3be1），只变五份Examples DLL，其余20项与target-family逐字节相同。`Install-SdkProbe.ps1`20299/11a6c5自然0，**15:23:25.3105854安装25匹配，无游戏启动请求**。安装记录module-control-mailbox/install-observation.json；报告`docs/2026-11-17_sdk-probe-mailbox-report.md`，使用`docs/Runtime-probe-control.md`。两个Sol/max已实现/独立只读复审并停写，无必修问题。

**下一工作**：等人工新开局并保持前台后先读新会话及心跳，20秒后使用当前sender发布一次Pause，核对SDK实际回执及后续Playing/Paused采样；再Resume、真正ClockReady、Speed/Seek。若acquire卡住或结果超时，只读原日志/后续同nonce结果，不重发，不捏造成功。旧composition接管127秒仅历史，当前目标组优化实机效果仍未知。exact scheduled child成功、B01–B19整体、production仍未完成；goal active。无需重做相机/N1/退休批或已过矩阵。

## 最新：TargetFamily已新构建/691+4通过，新包仅安装，待实际Pause/Clock（11-17 14:54）

**当前无游戏/无CUA任务。** 9个Runtime/Hooks文件已实现同一次Targets.VerifyCurrent中共同Stock/Lower祖先入口/出口校验，实际pair/image/member/Engine/Time/cache/RF/UW/3种proof保持，不跨独立getter/original/Read缓存；独立null路径全验，不造source/writer票。唯一创建点是Targets.VerifyCurrent using，Sol只读复审无阻断。`target-family/source-freeze.json`1506输入，相对loader-reference-final仅9变化；Loader/Adapter/SDK/native源码未变，主README保护哈希保持。

新build四步骤自然0（53311/26a902），Runtime1718784B SHA`eab5f3d3b5e4a6ad1a8c37a7dc4d57a177299a50239db579a05640a36291933a`。Hooks210；当前Replay/Resume/direct-time/direct-engine四进程338（19445/ce906f）；actual owned23-member/3pair定向normal及pitch-cache/time-cache/engine-getter/lower-time-registration五进程143（93760/e2626e），均自然0，恢复字节仍sticky。合计691，owned中Stock=null，**不能宣称真实Stock Inspect正向或速度改善**。重新链接Loader55296B SHA`1596fa331703bd984ee876a824cc9eecc78b4f08f74a95d5021a2af93b1f345d`、Adapter也变字节，因此4个实际受控Host场景已新跑（75621/c75b78），未借旧宿主PASS；691+4不包含旧native442，不重跑。

**新包**`artifacts/delivery-20261117/RizToolbox-0.3.0-target-family-preview-20261117.zip`37326854B SHA`2eb7f95e45e89c3ec0af5b1f6103a786dca786acebe7c7d5bc836e54265eb1ba`，38/25全读回，pack8a478d自然0。`Install-TargetFamily.ps1`原13989/12d3cc自然0，14:52:39.5974110安装25匹配，**没有请求Steam或游戏启动**。游戏目录已是本新包，不能继续旧composition会话。当前电脑操作接口不可用（工具namespace撤回，不是用户撤销授权），故本轮未实机。安装记录在target-family/install-observation.json，报告`docs/2026-11-17_target-family-report.md`。

**下一必须验证**新包实际source接管耗时→真正SDKPause（避免只得到焦点自动暂停采样）→SDKResume→ClockReady/SpeedSeek；此前composition source127.13秒/Resume32.73秒仅旧包。精确内部scheduled child仍缺独立实际成功记录。若离线继续，只处理实际剩余功能或明确新失败，不重做相机/N1/退休批/已过691+4。整体B01–B19/production未完成，goal active；不要因为预览包/离线绿灯标完成。参考RD两项宿主改进已包含，勿重复。

## 最新：参考RD仓库的宿主改进已实现/验证/打包，当前无游戏（11-17 14:30）

用户新要求参照`REDDRAGON-HL/RD-InFalsus-Mod`，已固定只读clone `work/reference-rd-infalsus` HEAD`617153d0183128c5e1d7d60acfd872bbb64cd6e8`/MIT。两项实际缺口已落实，非照搬源码/偏移：ToolboxHost普通主线程`shutdown`命令与同帧停止、逐项尽力清理/终态实际插件记录；后台5秒已加载包指纹→主线程不可变`packageUpdates`。命令回调完成才确认ID，队列满可重试；坏command不饿死扫描；扫描不持有阻塞停机的磁盘锁；终态不重读disposed Adapter。仅Loader一个产品file，SDK/Adapter/native/Runtime未改。新增`scripts/Send-HostCommand.ps1`与`docs/Host-control.md`，`Validate-Runtime.ps1`收紧allStopped，主README保持保护哈希。

`loader-reference/build-result-r1.json`五步自然0，原18314/e5f701；Loader Release与Hosting20/Lifecycle14通过。新Loader55296B SHA`fd5222aea38208b8d968bfc2a7f7f99f3258220e3e4b8630d45cc00d2ecf6b57`。4个受控实际ToolboxHost场景r2通过，49466/0cd5d1；首次2case仅finally删除仍映射的fixtureDLL失败，业务断言已走完，保留原失败与夹具源后只改夹具存证。真实WatchCommands队列满重试与坏JSON仍扫描都覆盖；不构造游戏/native票，不借此签原生资源全释放。命令脚本9检查813eb5自然0，未联系游戏。最终freeze1506输入明确脚本在DLL构建后的大小写归一，由新9项另验，**编译源未变**；原freeze不覆盖。

之前Dobby五file实际组入口/出口破坏48已新通过，82883/82bea7；同Runtime Hooks210+Replay/Resume/直接拒绝338+48=596全0，不重跑。Runtime1716224B SHA`df8c42768edaa8cbc6e776c69bd977a33cefd3f580a0618a36b7e2bf294422f2`与新包完全相同；旧测试中的Loader仅inventory项，不能验新Loader，现由独立4场景及新构建闭合。native未改，原442仅按同字节继承，不算本批新检查。

**新包** `artifacts/delivery-20261117/RizToolbox-0.3.0-loader-reference-preview-20261117.zip` 37326691B SHA`63f491e46a5a74b5fc16c2c7d69558e2198a88aa6c7f636ea5bad9fa06b30207`，38成员/25运行文件全读回，fab196自然0。**未安装/未启动游戏。** 目前游戏目录仍是旧composition-read包，不要以旧安装日志验新包。报告`docs/2026-11-17_reference-host-report.md`、操作`docs/Host-control.md`，receipt/finalfreeze在loader-reference目录及delivery。所有代理停写，无活跃shell/CUA。

**下一工作**：游戏卡顿仍未解决到可交付，不能说SDK整体完成。原新composition实际source127.13秒、Resume dispatch32.73秒，已精确结束11456；Dobby五file+本Loader尚未实机。优先继续同锁Targets.VerifyCurrent祖先重复StockTargets.Inspect，或仅在有明确新包新字节后进行必要实际Pause/ClockReady验证；不再重复相机/N1/退休批/已过596。需要精确内部child实际成功、Clock/SpeedSeek与B01–B19验收，goal保持active。参考仓库请求本批已具体实现，不止写建议。

## 最新：dobby-traversal五文件新构建/548通过，正补实际组出口破坏检查与参考改进（11-17 12:33）

**当前无游戏/CUA任务。** composition-read实测闭合：25安装匹配；session fc08ff92a5614dfd8edb514c485b4062/run6208498f7bc74d4aa484a95397a3d212/generation3。source127.134811秒，SDKPause按钮收到了实际回执，但此前采样Paused=true，故前置条件19拒绝；Resume回执32.725345秒，仅Dispatched，未见后续实际Playing/Ready/SpeedSeek。无来源拒绝日志不证明exact child。AltF4已发送，首次观察后140.68秒仍未响应，12:25:18精确Stop11456并验证窗口/进程消失，CUA已completion。原件`composition-read/composition-r1-game-result.json`、after-exit日志/game-exit、实际栈`work/game-seek-20261115/composition-read-r1-capturing-a-stack.log`。当前已知卡顿未修复到可交付。

下一五file已从隔离副本审查后应用：Group/Playback.Owner/Playback.Live/SourceOwner/EnginePitchPatchPair，共同Dobby seal沿既有双锁typed traversal入口/出口完整验，每次借用核group/engine/seal/thread/generation；成员本地证据、null/独立路径全验。两个Sol只读复审无阻断。**1504输入新freeze，变化5，native/Adapter/SDK未改**。`work/game-seek-20261117/dobby-traversal/managed-build-result.json`四步自然0，原17227/d817cb；Runtime1716224B SHA`df8c42768edaa8cbc6e776c69bd977a33cefd3f580a0618a36b7e2bf294422f2`。Hooks210+本新Runtime四case338全过，原97250/326c16自然0，共548。尚未pack/install本五file新包；不能用composition包游戏/旧596验新源码。普通ReferenceCodeIdentity未变，需按相同范围继承或在新Runtime验证时另声明。

Sol `scheduled_native_checks` 正在 `dobby-traversal/group-checks/` 独立复制现有真实23-member runner，补 group-dobby-entry/exit 两case：actual Bind/issued group、同current helper；exit通过fixture内部单次ObjectClass钩子在receiver projection中途损坏DobbyPrepare，实际traversal出口必须拒绝，恢复仍sticky。不造owner/scope，不改产品；root唯一串行build/run。现有`dobby-traversal/checks-runner/inputs-r1/binding.json`可绑定，不能用固定旧helper的receiver runner。

仍有后续乘数：`PlaybackSourceGroupTargets.VerifyCurrent`入口/出口内部经Lower/Engine/LowerTime/pair多次 StockTargets.Inspect；当前五file未处理，不能预告实机速度。新用户要求参考`REDDRAGON-HL/RD-InFalsus-Mod`；已只读clone `work/reference-rd-infalsus`，HEAD`617153d0183128c5e1d7d60acfd872bbb64cd6e8`/MIT，未执行其代码脚本。Sol `scheduled_stock_profile`正对照薄入口/Core、hook生命周期、dirty后场景刷新，找本项目真正缺口；新要求为当前任务转向，不丢原底层工作。不得抄异游戏offset/自动加旧loader兼容。B01–B19/production仍未完成，goal active，主README保持。

## 最新：composition-read新构建596项通过，开始本新包实测（11-17 12:13）

仅Runtime ReferenceCodeIdentity.cs/Group.cs两文件修改，1504输入新freeze；普通成员composition完整入口/出口，中间固定身份与原像/补丁/overlap，Dobby共同seal按对象引用在同一阶段合并。无Adapter/native/公共合同变化，不跨native getter/回调缓存。Sol只读复审无阻断，产品均停写。

`work/game-seek-20261117/composition-read/managed-build-result.json`四步骤全0，原76591/40a999自然0；Loader及六组件/Examples/Hooks Release，Hooks210。当前新Runtime/原生helper同字节四进程338通过，72963/15bf14自然0。普通真实Dobby六进程48通过，r2只重跑两个新ForComposition mode，62926/45b14d自然0；r1新mode最后夹具错预期Prepare/Commit2/2已仅修为0/0，产品未变，失败和旧源保留。四旧mode当前Runtime原本已全0，未重跑；各结果绑定`ordinary-checks-r2.json`。合计596，不含按原字节继承的native442，不声称ordinary+playback group联合夹具或stock/Unity/Clock/game验收。

Runtime1715200B SHA`89313403a5d04cd6b840ee7479db870fe6b9089dc75ace83200a8dd01bb062c5`。新ZIP`artifacts/delivery-20261117/RizToolbox-0.3.0-composition-read-preview-20261117.zip`37315227B SHA`f30bfac4c07a79b54adbc3e54497e8b9d28e320bed1d8d05eb45aa9c880442cc`，35/25全部读回，pack5340e0自然0（首次pack仅Examples五目的地假设错误，未写ZIP，已修打包脚本）。原install63619/d20643自然0，12:11:56.3210516安装25匹配，官方Steam启动。**新游戏11456 Start12:11:58、实际窗口2755822，CUA绑定compositionGame**，正在初始化；12:13:40已到AudioPlayDelayed prepare，无拒绝。CUA新段已rewriteDocumentation，尚无游戏终态，结束需completion。只续本新PID，勿续旧35548，也不重跑596/旧780。报告`docs/2026-11-17_composition-read-report.md`。

## 最新：新调度包已实际恢复播放，下一修复同锁重复校验（11-17 11:59）

**游戏已退出，无当前游戏/CUA任务。不要再续35548或重做已过780。** scheduled-engine-child 新包25安装字节匹配，实际session `710140bb954e4d77a38c0ef36dd9ab1d` /run `c481797b0e94409796afce52f3036603` /generation3 /Greatness Earth EASY4 FREE。接管142.68秒；SDK Resume回执32.15秒、按钮至Playing=true/Paused=false约250.94秒。没有来源拒绝/撤销日志，但合法零child路径存在，没有独立exact Engine child成功记录，不能称child实机证明。两次Pause输入超时且无SDK BUTTON/回执，晚些采样Paused不证明SDK因果。未PreparingClock/Ready/SpeedSeek。

原件 `work/game-seek-20261117/scheduled-engine-child/scheduled-r1-game-result.json`、after-exit日志、`game-exit-r1.json`；只读实际主线程栈 `work/game-seek-20261115/scheduled-engine-child-r1-after-resume-a-stack.log`。正常AltF4工具超时，观察后等待145.77秒仍无响应，11:51:30按PID/路径/启动时间精确Stop测试进程35548，已退出；不是正常退出通过。CUA已completion。

**下一离线工作正在施工：** Runtime普通broker成员在一次同步composition的入口/出口完整验证，中间阶段保留owner/generation/member身份、canonical preimage/overlap/patch检查；同一完整验证阶段按Dobby seal对象引用合并共享父映像扫描。独立Read/原生getter边界仍新验，不引入回调缓存/ambient/跳过native票。Sol只改ReferenceCodeIdentity.cs及Group.cs两个文件，root串行构建/相关ordinary破坏回归后再考虑实机；新源码未构建，不用780验它。Adapter/native/公共合同未改，旧scheduled包保持不覆盖。B01–B19和production仍未完成，goal active。

## 最新：scheduled-engine-child 已构建、780检查通过，新游戏35548实测中（11-17 11:34）

新行为已合并：独立 schedule actualOriginal 窗口、固定 stock 完整调用链 profile、Managed 物理子树及返回/退出责任。没有新增 writer、来源票或清失效。8 个已有产品文件变化+3 个新增功能文件；1504 输入冻结，232 个新增采集项含扩大的既有原生依赖，不能称作修改240个文件。

- `work/game-seek-20261117/scheduled-engine-child/source-freeze.json` 当前有效。native+11托管步骤自然0，原build session41054/结果e2ac72。Hooks210、Gameplay201、clock14、host3、旧fresh机制31。
- 新自有 native 8进程 **442/442**，原session10940/52fe0a自然0：pitch1.0、1.25保持；reentry/direct/caller/arg/cache/worker拒绝，透明原调用/物理journal。结果`native-checks/run-01/result.json`。仅native机制，不是CLR/stock/Clock。
- 同新DLL/helper的旧机制四进程 **338/338**，原session85917/cd6781自然0，`checks-runner/checks-r1/result.json`；Owned旧profile无本次stock schedule功能，不借它签stock正向。两组共780。
- 新 Source219648B SHA `26524c62e30ea516344e62d469c91f4d62317aa70c5448b804dd32693140b1f5`；SingleReturn80384B SHA `b6aa468e539d060628699c653c89c977efca6882fae88c43f3dbd73cf6874d5e`。
- 新包 `artifacts/delivery-20261117/RizToolbox-0.3.0-scheduled-engine-child-preview-20261117.zip`，37315127B，SHA `b99935c55dbb44642b771604e00f7f69b1be26b1f06386fba0a769376abc2d67`，35/25逐字节读回。pack d31624自然0。11:27:10.4641932安装25匹配，原install48487/455073自然0。

**当前真实游戏35548 Start11:27:12，窗口1311806，绑定CUA `scheduledGame`。** 只续本新会话，不重复启动/安装。已到主菜单→Greatness Earth/Axya/EASY4/FREE并点击PLAY，11:33:41进入加载新scene4；尚未观察本新包Clock Ready或SpeedSeek。CUA新段进行中，最终观察正常退出/任务终态后下一CUA调用须仅completion_summary一次。下方“未build”“无游戏”仅历史。

新观察脚本`observe_scheduled_child.py <unique-tag>`校验本安装25文件与新session；start-a读时新log尚未生成，按时间校验拒绝，未产伪新证据；start-b与after-play-a成功。报告`docs/2026-11-17_scheduled-engine-child-report.md`初记离线/安装，待追加本新游戏终态。下一只做实际接管返回→SDK Pause一次→SDK Resume一次→检查原Engine child拒绝是否消失，再推ClockReady/SpeedSeek。B01–B19/production仍未完成，goal active。主README保护哈希保持，产品已停写。

## 最新：实际父调用已定位，scheduled-engine-child正在实现（11-17 11:09）

V2新15248已完成实际诊断并正常AltF4退出，窗口与进程无，无Stop/强杀。`engine-child-diagnostic/engine-child-r1-game-result.json`及decoded原件：接管138.72759秒；暂停点击timeout，未观察SDKPause BUTTON，实际PauseUI随后出现；SDKResume BUTTON已观察并返回。最终status4/mask2，当前EngineSetPitch float1.0，**parentRole=PlayScheduled/Double1/Before/depth2，engineTested0x7f/matched0x3f，首败bit6 ParentLowerSlotMatches，Begin0，callerInLower0**。这确认现有规则漏掉调度内部的原生音调重新提交；不代表已经取得实际native caller地址。Ready/Clock/SpeedSeek仍未通过。

独立静态链已闭合：Game PlayScheduled15116D0→cache1FB0660/nameAudioSource::Play(System.Double)→registeredUnity1D7AE0(305B)→BB74C0完整2072B/7RF递归CHAININFO→两互斥分支之一调用BB6F20(196B)→BB6F49 CALL EngineBBAD20/returnBB6F4E。重新提交float来自同native+1F4当前pitch，不是固定1.0；原件`work/game-seek-20261117/scheduled-engine-child/`。实际接受仍须精确caller+private窗口/receiver/float/fences，不只凭parentRole。

**新行为代码正在变动，V2的11/338不能验这些新源码。** root已接EnginePreflight/Group/Hub/Lease/Producer独立scheduled分支，维护语义叶与writer计数，补VerifyRoot对子调用的复核。Sol分别实现native actualOriginal窗口、固定stockprofile完整body/RF/UW/cache/CALL封口，以及小独立native检查；不做重型新CLRfixtureprofile，旧Owned fixture无schedulefeature仍作为现有机制负担边界。新native检查将只声称native自有机制，不声称CLR/正式stock/Clock。尚未freeze/build新行为，不改已交付V2ZIP，不安装/启动游戏。

下一：合完新行为→唯一串行新构建/338同字节回归+schedule定向native检查→新包实际精确child/Pause/Ready，再推进Speed/Seek。当前无游戏/CUA已completion，必要恢复授权保持。下方“15248初始化中”仅较早观察，不据此重复启动旧游戏。

## 当前：Engine child V2诊断包已安装，新15248初始化中

当前包：[RizToolbox-0.3.0-engine-child-diagnostic-preview-20261117.zip](../artifacts/delivery-20261117/RizToolbox-0.3.0-engine-child-diagnostic-preview-20261117.zip)，37,305,899B，SHA `9a9173ded64c0b7c7769da7c463adea65222a7501e088c054da3e806704d0e04`。35成员/25运行文件逐字节读回，pack c28256自然0。`work/game-seek-20261117/engine-child-diagnostic/install-observation.json`核10:43:53.8787201本地安装25匹配，官方Steam10:43:53.8875471请求，安装原57283/4cd75b自然0。

新真实 **15248 Start10:43:55.7105907+08**，Rizline window985528，CUA已从返回inventory绑定`engineDiagnosticGame`；未出现Steam选择框。首次截图因未前台显示遮挡，未点击遮挡app；Shift_L激活请求超时，随后inventory确认游戏窗口仍同，后续须重看画面，不重发未经观察的输入。`engine-child-r1-start-a-observation.json`核新会话25安装文件，02:46:01Z source commit Replay，随后02:46:04Z Resume，还在初始化；尚无本新包实际native拒绝/Clock/SpeedSeek。CUA本新段尚未completion；最终观察结果/退出后下一CUA调用须仅completion_summary。

## 已闭合的新代码与新字节检查

6 native + 2 CLR已冻结、构建、打包，Adapter/SDK公共合同未改。FailureV2私有136B/pack8、DiagnosticVersion2，无fallback；原成功身份InfoV1仍208B。原生Begin/Child实际判定按原短路顺序记录tested/matched，不重跑getter/predicate/Source query，不更改flags、票、原调用、锁、线程10优先或terminalization。只从当前Engine/Icall scope复制真实父scope和原位记录；caller仅lower函数内[0,495]偏移，标量allowlist双端执行，Ref/未知参数0，不输出地址。当前scope不是历史blocker setter。

位合同：`work/game-seek-20261117/engine-child-diagnostic/native-mask-contract.md`。Begin21位/Child29位；最低tested&~matched位是首个实际失败比较；未求值位不能视作失败。stock固定return offset0x1be。

- `source-freeze.json`：1272编译输入、相比V1仅8变化；source-r1保留8源。CLR独立Sol只读审查无blocker，root核native同短路顺序。
- 原63959/2b7588自然0：`native-build-result.json`新2native成功，`work/game-seek-20261115/engine-child-diagnostic-r1-result.json`新11托管步骤均0；Hooks210、Gameplay201、clock14、host3、旧fresh机制31。31无正式stock Adoption。
- 原67744/4fb086自然0：新`checks-runner/checks-r1/result.json` **338=Replay113+Resume133+direct-time46+direct-engine46**，4独立进程，每child实际23/23+3pair、4真实root→0、真factory claim1；正常同票/精确Engine child/实际DSP调用保留。worker10不关闭owner票。直接时间拒绝4/mask4；真实cached-native-self直达Engine拒绝4/mask2，child tested1f/matchedf、父/Begin/caller全0。拒绝无执行票，原function/physicalexit/journal排空/退休通过。
- runner冻结`runner-source-freeze.json`SHA `6f4df172a7b9f192809a907862e09d9e9dcf8f5cf97fb936bdd2e85dbc800caf`，5源+13实体bootstrap；Stage绑定新DLL/helper。338仍无stock foundation、Unity、正式stockAdoption、Clock/game/production，不能代替实机正向。

当前helper：SingleReturn80384B SHA `0119a5c1a2570977451646c340661e111f917fb1b24a6147c6f4798bdf19eb5d`；Source211456B SHA `4f5675fd99b04fe8397af994278d4112340ce59cc7f288f14a84e339a6426b09`。JudgeContext未改。不重跑本已过11/338及旧长矩阵。

## 已取得的上一V1实际失败（不是本V2验收）

V1 ZIP SHA7c8b9659…，旧新16288、session22d698c591684101b7d0fb8fe7a80159、run a26a2474bded4ea2aadf99470f2fc9b7，GreatnessEarth/Axya/EASY4/FREE：接管141.997524秒；SDK Pause精确事件33.49061秒；SDK Resume dispatch32.819183秒（非立即恢复证明）。02:14:04 Draining→02:14:09 Withdrawn：**status4/origin source/mask0x2/当前EngineSetPitch kind8 seq11 parent10 depth3/argBits0x3f800000/childFlags0**。未PreparingClock/Ready/SpeedSeek。已正常AltF4退出，窗口/进程无，无Stop，OSexit未知。原件`work/game-seek-20261117/fresh-block-diagnostic/fresh-diagnostic-r1-game-result.json`及after-exit日志/stack；旧292仅验V1字节。

静态独立证据`engine-child-analysis/static-verification.json`：固定UnityPlayer整文件/Lower495B/Engine640B匹配；lower原样传float、同函数唯一CALL1d78f9→BBAD20/return1d78fe，native self无offset变换。不能放宽arg/caller/self判定。仍缺游戏真实parent10 role和首个失败位；V2正为补这一信息。

更旧receiver2696实际泛status4与150.7197304秒接管仅历史，在11-17早已确认无进程/窗口。其原页及V1阶段页在`.docs/history/20261117-before-*`；不借旧会话验新字节。已排除Stop(void)→Stop(bool)及无PreparingClock时猜Cancel根因，不重复相机/N1/rate/float/Play移交/退休批。

## 下一步

1. **只续新15248**初始化→新游戏同曲目→等待source接管返回→SDK Pause一次→SDK Resume一次，读本包同Read的parentRole、beginTested/Matched、engineTested/Matched和callerLowerOffset。
2. 只按实际首个失败条件修最小路径；不重stage/发票、清失效flag、用sampledPaused造baseline。真正Ready后再验Speed/Seek/恢复、七根/HoldEpoch和实际DSP精度。
3. 全SDK/B01–B19/production仍未完成，goal active；功能优先，分钟级主线程阻塞仍属可用性故障，性能基准后置，不做旧兼容。

用户必要游戏/恢复授权有效，无新停止信号。UI仅cua_repl，不操作账号/认证，不commit/push。代理Sol/max；当前代理均本批停写，root唯一串行BelowNormal构建已结束。主README SHA `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`保持。报告`docs/2026-11-17_engine-child-diagnostic-report.md`；交付说明将在本轮终态更新。
