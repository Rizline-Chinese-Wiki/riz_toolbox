# 构造调用 profile 与映像重复读取修复

本批根据新游戏的真实调用栈，修复构造期重复整组验证和同次 pair 映像校验的重复拷贝。四个 Runtime 文件已构建，788项定向检查及重新链接后的4个宿主场景通过，生成新预览包。该包后续已完成一次真实游戏观察，仍未ClockReady；结果见文末。旧r3会话只作为修复依据。

范围为已授权的本地 Rizline 兼容底层工程，普通报告，flavor=null。SDK、Adapter/Loader源码和原生组件未改，主README保护哈希保持；不增加控制权限、不制造来源票、不跨原生调用缓存成功，不做旧版本兼容。

| ID | 原件 | 证明范围 |
|---|---|---|
| E01 | [r3实际游戏结果](../work/game-seek-20261117/module-control-mailbox/game-r1/game-result.json)及[当前会话日志](../work/game-seek-20261117/module-control-mailbox/game-r1/current-session.log) | 旧r3包真实SDK Pause因果、Resume后播放采样及第二次Pause回执；无ClockReady/Speed/Seek。 |
| E02 | [构造期栈](../work/game-seek-20261115/sdk-probe-r3-source-r1-stack.log)、[Resume栈](../work/game-seek-20261115/sdk-probe-r3-resume-r1-stack.log)、[时钟准备栈](../work/game-seek-20261115/sdk-probe-r3-pause-r2-stack.log) | 分别落在StockLeafCaller→整组/首次安装验证、完整映像读取及PreparePausedClock的图验证；没有记录某个具体range的运行次数。 |
| E03 | [新源码冻结](../work/game-seek-20261117/occurrence-call-profile/source-freeze.json) | 1512输入，相对r3仅4个Runtime文件变化；原件与隔离候选哈希保留。 |
| E04 | [新构建](../work/game-seek-20261117/occurrence-call-profile/managed-build-result.json) | Loader依赖六组件、Examples、Hooks三组Release和Hooks210执行四步骤均退出0。 |
| E05 | [新播放链检查](../work/game-seek-20261117/occurrence-call-profile/checks-runner/checks-r1/result.json) | 当前DLL及相同原生helper上，实际owned 23-member/3pair Replay、Resume和直接time/engine拒绝四进程338项。 |
| E06 | [新支持证据检查](../work/game-seek-20261117/occurrence-call-profile/supporting-checks/run-r1/result.json) | 5进程143项，独立正常验证及pitch/time cache、engine getter、lower-time registration破坏；恢复字节后同组仍失效。 |
| E07 | [新增定向结果](../work/game-seek-20261117/occurrence-call-profile/focused-run-r1/result.json) | caller32、pair gap34、header31，共97项。真实owned bind，未构造虚假group/traversal/foundation。 |
| E08 | [新链接宿主结果](../work/game-seek-20261117/occurrence-call-profile/host-checks-r1.json) | 当前Loader/Runtime字节上的4个受控实际宿主场景，不代表游戏或native资源全部释放。 |
| E09 | [新包回执](../artifacts/delivery-20261117/occurrence-profile-preview-receipt.json) | 当前构建/执行文件逐一绑定、40成员/25运行文件全读回；Examples及sender按相同字节继承27+7，不计入788。 |
| E10 | [安装记录](../work/game-seek-20261117/occurrence-call-profile/install-observation.json) | 本新包安装后25文件全部匹配，没有请求Steam或游戏启动。 |

F01（E01–E02）：旧r3已由Steam实际启动，PID35088、游戏session `773d9c6901eb47f4a14328bbaed7b19d`、run `9d8333368785454087b0e532f6be9eb0`、generation3，Greatness Earth EASY4 FREE。source接管90.875657秒；后台Pause1的请求前状态Playing/非Paused，回执Success且即时条件true，随后采样Paused/非Playing，因果成立。Resume回执仅Dispatched，144.900714秒后才实际采样Playing。Pause2也有成功即时回执，随后实际栈进入PreparePausedClock，但没有Ready。正常AltF4后120.268秒仍不响应，16:00:48按PID、路径、启动时间核对后结束，不能签正常退出。该会话只验r3。

F02（E02–E07）：原producer构造先完整VerifyCurrent，再分别查询9个固定caller，每个查询又完整VerifyCurrent；stock路径合计10次完整group traversal。按源码每次foundation进出各核26个operation和64个range，构造仅这一段就至少有520/1280条重复核验，属于静态调用次数推导，不是实机计数测量。新 `CaptureStockOccurrenceCallProfile` 在原双锁和真实typed traversal内完整验证group，再逐个读取实际5字节CALL并核目标，最后完整targets/Dobby/foundation出口。producer只局部消费caller地址；lease新增proxy，不发票。独立StockLeafCaller、后续Before/Returned、getter和写入保持原fresh验证。异常在完整退出后抛出，不把caller地址误作执行证明。

F03（E02–E07）：pair seal原来先逐个非成员executable gap做RangeSeal字节读取，随后又对整个executable section做pinned file、DIR64及当前成员patch重组比对，同一次VerifyCore有重叠读取。现将pair gaps同group gaps一样保留MemoryFact页身份，仍每次执行完整section读取和字节比对；header、非执行不可变区间、成员source seal不变。构造时原gap与canonical完整节已一致，因此完整节继续覆盖这些字节。普通独立seal保持原行为；不把traversal入口/出口之间的两次验证合并。gap拒绝的消息会从局部first-difference变为完整pair映像消息，拒绝判定保持。

P01（E03–E07支持F02）：`producer ctor → lease proxy → 双锁/active → 真实traversal完整入口 → 完整group images/members → 7/9固定caller实际CALL检查 → 完整出口 → 局部profile完成`。后续每个独立native观察仍走独立验证，不复用该局部结果。

P02（E03、E06–E07支持F03）：`pair VerifyCore → header/非exec原RangeSeal + exec页身份 → pinnedfile/DIR64/当前成员重组 → 完整exec section逐字节比较`。实际owned lower `.text` padding RVA `0x2d17` 的 `CC→CD`触发完整pair映像拒绝；DOS stub RVA `0x40` 的 `0E→0F`触发header拒绝。两例恢复原字节后同组仍sticky，未因删去重复拷贝而放行修改。

E05–E07都使用owned fixture且Stock=null，没有真实StockTargets、Unity、正式adoption、Clock或生产准入，不能由此宣称游戏性能改善。新测试、旧失败、候选副本与实际游戏记录分目录保存。独立Sol复核最终四文件未发现必要修点。原生helper未改，不重跑既有442项；新Examples同r3字节，原27项及发送器7场景仅继承，不拿旧Runtime结果验当前修复。

新Runtime：1724416字节，SHA-256 `2d27b718f8c635d4ff12c09f05dcd780801d98dae91f883088855ef21c0e97ca`。新Loader：55296字节，SHA-256 `620bde88d58812528d92af07cf6306efc518240bb4251b7c75fa75bb33b435e3`。

新包：[RizToolbox-0.3.0-occurrence-profile-preview-20261117.zip](../artifacts/delivery-20261117/RizToolbox-0.3.0-occurrence-profile-preview-20261117.zip)，37368320字节，SHA-256 `90fe03bfac107e5b8af0618443535c236c0c4e1282a41fe9eb9775781af8a1fb`。仍含[SDK后台请求入口](Runtime-probe-control.md)与[宿主控制](Host-control.md)。

下一在本新包验证source接管耗时、真正SDK Pause/Resume、ClockReady与Speed/Seek；不能用E01的旧会话补新包验收。exact内部scheduled child、完整B01–B19及production仍未完成，goal保持active。

后续实际游戏（仅本occurrence-profile包）：PID32912，Start16:17:32.5025685+08，诊断session20261117T081802109Z-cb6b88c7480842de86fae632bc3dd0b1；游戏sessionbe7f69b170674f8fb3eb33961dff1cfc，run8f8e09c19d524027a8b21d4528b0d635/gen3。原件[game-result](../work/game-seek-20261117/occurrence-call-profile/game-r1/game-result.json)与[current-session](../work/game-seek-20261117/occurrence-call-profile/game-r1/current-session.log)。source接管78.402008秒；唯一SDK Pause请求前Playing/非Paused，成功回执即时后置true，主线程随后阻塞，无后续Paused样本。回执后388.843861秒才进入PreparingClock，实际只读栈已到TryPrepareDormant→VerifySource，仍未Ready。未发Resume/Speed/Seek；正常AltF4请求后121.6247708秒无响应，16:39:37按精确ID/路径/启动时间结束并确认消失。该结果不证明正常退出或完整Clock。

F04（以上实际日志与[时钟栈](../work/game-seek-20261115/occurrence-profile-clock-r1-stack.log)）：当前包仍有分钟级不可交付阻塞，且外观和相机漏认自身处于有效已加载scene=-12的常驻controller。下一独立persistent-reference候选据此修复；本788项与本游戏不验其新源码。
