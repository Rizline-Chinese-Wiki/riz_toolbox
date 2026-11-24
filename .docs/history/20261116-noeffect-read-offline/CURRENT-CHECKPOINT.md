# 当前续接点（2026-11-16，优先读本页）

## 当前：idle-read已完成限定实测并退出；noeffect修复施工中

最新包 `artifacts/delivery-20261116/RizToolbox-0.3.0-idle-read-preview-20261116.zip`，37,308,123B，SHA `a7a8af240bb151caea59f14afc50ed7cdf6dff9eac37573b6417ebf4c4dd9571`，35成员读回、25运行文件安装匹配。原Install-Preview session31974自然0/bd6e54；`install-idle-read-r1-command.json` / `install-idle-read-observation.json`，22:11:19.439核25/25；官方Steam22:11:19.449请求，新实际49744于22:11:21.4857172启动。首次observe在新CLR session写出前正确拒绝旧日志（没有生成成功回执），不要把该拒绝当安装或游戏失败，更不要读旧supporting会话接受新代码。

新49744已初始化并由CUA进入GreatnessEarth/Axya/EASY4/FREE，session `1ba1da0a328a4dc090ff9ee918ac303e`，run `67708c2becd54eca87499f52ba52bb85`。首Clock14:17:20 UTC为**19.3672430秒**。Host14:17:33.1221404 CapturingSource→14:21:04.7783702 WaitingForPause，**211.6562298秒后真实接管返回**。`idle-read-r1-after-capture-a-observation.json`核25安装文件。

接管返回后实际Responding=true，CUA fresh screenshot上点击一次SDK Pause[1430,273]，click10秒timeout，没有重复。新栈 `idle-read-r1-after-capture-a-stack.log`在StockRootedSource.ObserveBefore→ActualSource.ReadDsp→Adoption.EnterAccessor，已进入真实stock source Before，但role未知。随后14:22:21 Draining、14:22:26 Withdrawn，错误为“original returned without exactly one expected leaf sequence”。CONTROL14:22:26实际Paused=true/Playing=false/CanDoOperation=true，但无有效source执行票、未ReadyExperimental，不签SDK Pause接受或SpeedSeek。源日志/25匹配/标量见`idle-read-r1-game-result.json`和`idle-read-r1-after-exit-observation.json`。

CUA已观察暂停页；正常Alt+F4后窗口消失，22:26:42查询实际49744不存在，本批没有Stop-Process，OS退出码未知，没有RunEnded。CUA该段已completion_summary。当前游戏已退出，可后续安装新候选，旧ZIP不改。

新noeffect实现仅施工中，**尚未freeze/build/test/打包**：Runtime保留真实root无leaf无执行票拒绝，仅给同一actualCall/Returnedcompletion的正常零child/无priorReject/精确三fences无多写/known无fault返回保留私有negative身份，下次Before/Fail/Dispose/drain清理；只能当前真实Returned回调识别，绝不授baseline。Adapter只在fresh且从未有accepted readback前，完整重核同源后保持等待下一真实操作；已有baseline/legacy/其他拒绝仍Fail，旧Current不复活。另加有界rootRole/args/expected/actual诊断。Sol在新work目录准备真实23-member/watch/producer的no-op→真实leaf检查，不改旧fixture/pins或借fake票。root独占Adapter，Sol独占Producer和新runner，第二Sol只读审查。

当前无构建在途；新Producer/Adapter源码已改变，**旧1240freeze和idle包结果不接受新源码**。之前13文件supporting修复/helper不改；旧supporting/package完整保留。

## 新功能修复：首次无候选时钟准备不再空转

root修改Host、StockRootedSource及新增Router.Idle（此前Freeze中的Adapter3文件不再改）。仅在Host WaitingForPause、无Host session/logicalHost时先route.PumpCompleted+PumpFaults，stopped仍Drain；再取实际CaptureCurrentPlaybackRun，必须与原run同ref。Router排除所有session/initializer/handoff/admission/retry/retained/fault/host-ended，核真实fresh source注册；HasReadbackCandidate只Open/thread/registration读_current!=null，不使用Current getter或授authority。

无候选只推迟没有输入的Clock准备。任何候选（不限Pause）、run变化/NotReady/transition均走原full group/sourceProfile/clockProfile，再原receipt/native/PhysicalExited门；所有SDK/native/use fresh不变。真实Before/Returned→producer→OnReturned票发行线未改，不会依赖Host轮询产生首Pause。无职责route.PumpCompleted只维护transport health/空journals，不常态full；真实职责/关闭仍走原drain。独立Sol读代码无阻塞，未当正向实测。

## 本候选验证与失败边界

`idle-read-r1-source-freeze.json`1240编译输入已冻。原49296自然1/b41ce5：Loader六组件、Gameplay、Examples三Release构建0，clock14、host3通过；最后defaultGameplay因为root漏传子进程RIZ_PLAYBACK_OWNED_FIXTURE，172/201失败，原日志与result保留。未改产品，`retry_idle_gameplay.py`仅给新子进程补未改自有fixture6ca4cc…，新`idle-read-r2-gameplay-result.json`/log实际201/201自然0/8b3aa9；没有重跑已过构建，也不把r1说全0。

`pack_idle_read.py`实际0/8b3aa9，要求前5成功+上述受影响组的实际新retry0、1240输入未变及新执行DLL；Runtime/Core/SDK逐字节等supporting包。旧focused五mode138断言/23真实成员与ordinary204断言/12成员仅继承**未变三库**机制范围，不接受新Adapter/game；两owned无foundation/Unity/Clock/正式Adoption，不提升权限。native helper仍88导出/SHA d099ae13726fd0ac94abf906506a99190de2260da704e6f91bea27b2e5164823，未重复原生/旧SDK矩阵。

## 最新旧包真实依据：supporting-read已退出

supporting-read ZIP SHA643b2f966f7e3b8fbf5d09d997933a2ecbe8cbf17b8fd1e141a12c4e3104fe81；实际3056/session8352799a7bc747718c1897da41299bb0、run5d3f0110a80e48b0b3047c05d3ba1ca6，GreatnessEarth/Axya/EASY4/FREE。首Clock19.0731920秒；13:53:17.3419916 CapturingSource→13:56:47.9181003 WaitingForPause，真实接管210.5761087秒后返回。实际栈在ActualSource.Read→ReadDsp→EnterAccessor→capture/group，返回后仍每tick21–28秒空轮询；这是本idle修复依据。

CUA Pause按钮与合成Escape各请求一次，没见回执/HOOK计数，不能签暂停。正常Alt+F4请求逾60秒仍无响应，22:04:22.426核精确PID3056、exe绝对路径、fractionalStartTime、Responding=false后仅Stop该测试进程；CUA确认窗口消失并completion_summary。原件`supporting-read-r1-game-result.json`、`supporting-read-r1-before-exit-*`、`supporting-read-r1-source-a-stack.log`、`supporting-read-r1-exit.json`，25安装匹配。不是正常退出或RunEnded，不验新idle代码。

更早group-read真实首Clock29.4589574、接管365.6946435秒后WaitingForPause，AltF4请求后自然消失，无Stop。其余294.328→50.222→40.807秒各单样本与失败矩阵仅历史。新旧证据不混用。

## 下一步

1. 完成noeffect typed negative身份和首readback前fresh等待；审查所有未知/异常/wrongroot/extra fences仍拒绝且不授票。新owned真实watch/23group机制验证no-op无Current、同producer下一真实leaf可issue、copied/foreign/stale拒绝；新源码冻结后root统一串行Build，不让代理自行编译。不要放松expectedLeaf或用paused bool造baseline。
2. 新包后独立安装25match/新session实测，拿明确rootRole/args/child诊断，并检真正ReadyExperimental。旧idle的叶序列错误并未指明actualRole；首次构造211秒也可能长过音频播放，需要真实caller/当前API再判断，不先猜改CALL profile或grant。
3. 若真正就绪，继续变速/Seek/恢复、七根/HoldEpoch、实际DSP精度；若新错误按实际日志/有界新栈修具体owner/回读，不猜RVA豁免或live rebaseline，不重复相机/N1/rate/float/Play移交与退休批。
4. 全SDK、B01–B19/production仍未整体完成。功能优先，性能基准后置；分钟/数十秒主线程阻塞是可用性故障。

## 约束/证据

用户已允许必要游戏及恢复实测，无新停止信号，不为旧Escape重复问。只用cua_repl操作UI、不操作账号/认证、不commit/push；子代理Sol/max，构建串行BelowNormal。主README SHA保持CFBAC4691D36EDC5DABA513AA62D5856AE61E29BF553D0DC2979832750847364。CUA49744段已completion_summary，当前game无进程。

原件`work/game-seek-20261115/`；报告`docs/2026-11-16_late-hook-composition-report.md`；交付说明`artifacts/delivery-20261116/交付说明.md`。本次替换前两页在`.docs/history/20261116-before-idle-read-checkpoint/`，上一13Runtime+3Adapter详细SHA/11步/138/204路径见其中原页。旧ZIP和原失败不改。
