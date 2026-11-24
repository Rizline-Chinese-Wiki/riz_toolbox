# 标准构建与 Producer 回调离线验证

2026-11-04T22:48:09.056319+08:00

四个正式产品 Sdk、Runtime、Core、Adapter 的 net6 标准构建已通过，零警告、零错误；493 个产品源码与 12 个生成文件共 505 个 PDB 文档匹配。独立复核 1010 个实际／冻结源码路径及八个新 PE/PDB。候选尚未合入 C 盘主源码。

Producer 五项实际场景通过，共 338 检查、56 条物理调用记录全部释放。三项正常场景使用原验证程序集，两项异常场景使用修正断言后的新验证程序集；它们绑定完全相同的新 Runtime／Sdk 产品。每场景实际 23 Prepare／23 Commit，每个进程均有自然退出 0 的原始记录。

| 场景 | 检查 | 释放的物理记录 | 保留根 |
|---|---:|---:|---:|
| resume | 53 | 8 | 0 |
| withdraw-child | 76 | 16 | 0 |
| dispose | 54 | 8 | 0 |
| throw-before | 76 | 12 | 4 |
| throw-after | 79 | 12 | 4 |

正常场景证明 Resume 私有凭证／原生 schedule、主动撤权后先释放 child 再收到父回调，以及正常 Dispose 不误报故障。异常场景证明写入前／后原生 SEH、旧凭证失效、无新语义凭证、缺少父回调时仍依赖真实 PhysicalExit 排空。两个 Unknown 场景各保留四个正式根，不能表述为全部根释放。

原异常测试错误地要求 lower child 完全没有 managed completion。原生 Exit 实际先报告 Abnormal，再写 physical journal；Abnormal 的 Returned 为 false。新版只纠正这条断言，产品源码和原生 helper 未改变。旧失败与原日志保留。

验证脚本的两个问题也保留了原件：MSBuild 多行属性解析，以及新测试目录使生成文件／CodeView 也经过 PathMap。后续仅修正验证，未重新编译来替换原产物。两个测试程序集各有 18 个完整 PDB 文档，PE GUID／stamp／age 与 PDB 一致。

Adapter 测试项目保留四条 CS8602 可空性警告，均来自测试中的 first／transfer 条件 lease 读取，已按具体位置复核；没有隐藏警告。四个产品本身仍为零警告。测试项目实际 167 个引用、60 个 PDB 文档匹配。其行为矩阵正在按真实退出逐项验收，不能用安装通过代替恢复和重试通过。

当前 Router 已通过 1 项：lower-time-install。原 R11 成功 3/6；原进程保留，不重启。

机器可核证据：[独立复核](D:/riz-build-cache-next/playback-lower-time-r1-20261104/root-producer-standard-closure-r1/test-result.json)、[标准构建](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-time-standard-integration-r2/build/build-complete.json)、[正常三项](D:/riz-build-cache-next/playback-lower-time-r1-20261104/b2-producer-standard-validation-r2/verification-reconciliation-r1/execution/test-result.json)、[异常两项](D:/riz-build-cache-next/playback-lower-time-r1-20261104/b2-producer-standard-validation-r3/postcheck-reconciliation-r1/execution/test-result.json)。

完整 S3、source in-flight 重试、typed 诊断拒绝、同 ordinal／source-open 撤销、provider SDK 正向、连续 Clock 和全 B01–B19 尚未完成。真实 Speed／Seek 准入保持关闭。本批未安装／启动真实游戏，也未操作游戏 UI、账号或网络。


## 后续实际状态

2026-11-04T22:54:00.422853+08:00 **下一离线工作：收 Router R5 的实际 core9／typed6 终态。R4 已因 Resume 第33条组合断言实际 exit1 停链，不能继续等旧 supervisor33080；新 supervisor38988 已一次启动。四产品与 Producer338 的既有验收保持，主X952未变。**

R4 `Z/b3-router-standard-validation-r4/build/cases/lower-time-resume/process-result.json` 实际 PID45796／FILETIME134382770140940947，自然exit1；安装27通过保持。失败在调用语义顺序与 root 返回后的 `child.TryReadCompletion` 组合断言。wrapper Returned 内早先的真实 child PhysicalExit 已核，但旧日志未拆分两项，故不能据此断言产品错误或确定唯一根因。

R5 `Z/b3-router-standard-validation-r5` 只修一个测试文件：在 wrapper Returned 内持有实际私有 completion，root 返回后检查这份完成事实与其确切 observation；拆分 Stop/time/pitch/schedule 顺序和 physical completion 两项，并打印实际 roles／childReleased／capturedPhysical。不会让已释放 native row 再读成功成为要求，也没有伪造 completion。四个产品和原生 helper 不变；R4 全文件及真实失败另固定，未热改／重启。安装27已验不重跑，余 core9＋typed6 须各自新实际进程通过，不能签完整S3。

R5 新 harness build 已自然0，60 PDB／167引用匹配，四条已逐位置审查的 CS8602 测试警告保留，产品仍零警告零错误。preparation SHA 34c21e2718e964532490ad3a0887cd910f5be6a875a2b7981ffff52ee0d7afe4，runner SHA f0c97880f4745b2a7f76e33352fee4f3cac27cdb739d613791f03c412331317d。当前 state=running-core，当前原进程PID45140已按实际句柄核对（exit=259、exitFileTime=0）。入口 `Z/time-harness-supervision-router-r5/progress.json` 及 `Z/b3-router-standard-validation-r5/build/cases/<mode>/process-result.json`；失败保留停，无自动重试或回滚，不修改运行输入。

四产品标准net6与Producer五场景338／56物理记录验收及报告保持。原R11六场景／E012→App47→fresh链继续独立，S3不代签。尚需source in-flight、typed错误诊断、同ordinal、source-open撤销、最近原重试回归、provider SDK、连续Clock、任意Seek／generation双ABA／其他writer与effects／生产DSP、全B01–B19。真实Speed／Seek准入关闭。已清旧构建19.3GB，新输出仍只D，fresh预留792915741B与后继元数据余量保持；不安装／启动游戏，不执行真实游戏DLL／方法，不操作UI／账号／网络。


## R6后续实际状态

2026-11-04T23:03:39.895215+08:00 **下一离线工作：收 Router R6 core9／typed6 的真实终态，随后接 B3 typed edge 新源码。R5 已自然exit1停止；其新诊断确认是额外合法policy writer导致测试顺序断言错误，childReleased=false，不能沿用“已提前释放child”的假设。四产品与Producer338验收保持，主X952不变。**

R5实际Resume日志roles=[16,10,1,5,1]／capturedPhysical=true／childReleased=false。RootedSource在Replay/Resume root Returned内无条件调用真实 `_producer.WritePitch(receipt,_selectedRate)`，即使rate仍为1；它与原stock四叶具有同一ParentSequence。Observation没有OwnedWriter flag，不能按role或父序号随意排除。当前receipt／continuation仍保留实际物理树，因此root返回后要求全部row已释放也不成立。

R6 `Z/b3-router-standard-validation-r6` 仅改同一测试文件，产品及native不动：经当前source→producer→实际ContinuationReceipt→ReservationFor，真正VerifyCurrent，再用 `reservation.Contains`＋ActualAttempt引用相等精确区分恰一个私有writer。额外核真实224B reservation的nonce／root及attempt token／sequence／MI／ArgBits／物理退出及一次postfence。其余四个stock叶严格维持Stop→time→pitch→schedule，两套真实pitch triple；未释放row逐条须已正常PhysicalExit，外层Dispose后仍全released／roots0。独立Sol/max只读审查同意此修订；没有造票或放宽stock序列。R4/R5失败原件及新源均固定，不重启或热改。

R6已新build自然0，60PDB／167引用，四条明确测试CS8602保留，产品仍零警告零错误。preparation SHA217164af725142ed7ddb4164f3e08485d52ffd127924e38b4c4c6b611ef1d5d6，runner SHAa2da209a60236f82a2c18b487244644cfa57ce86dba4dc57bc0e05a51f80b38f。新supervisor44812一次启动，当前state=running-core；已实际通过0项（）。原句柄观察见 `Z/checkpoint-router-r6/test-result.json`。主入口 `Z/time-harness-supervision-router-r6/progress.json`，逐项OS结果在 `build/cases/<mode>/process-result.json`。安装27既有通过不重复；任一后续失败保留停，不能拿新build签功能通过。

并行 `Z/b3-typed-edge-source-r1` 仅准备下一批真实wrong root/yield／同initializer／source-open前撤销的源码和缺口说明，未经新build/test不得验收；native返回诊断篡改、fresh source-in-flight result8若缺实际fixture出口就明确留空，不造字段／counter／receipt。原R11目前成功4/6，state=running-engine-pitch-transfer-pending，其E012→App47→fresh门保持独立，不能被S3代签。

仍未完成：S3完整边界／providerSDK及连续Clock、任意Seek、generation双ABA、其他writer/effects、生产DSP及B01–B19。真实Speed/Seek准入关闭；不安装／启动／操作真实游戏或UI／账号／网络。此前旧构建已清19.3GB，新输出/obj/cache/TEMP/备份仍D，fresh预留792915741B与后继metadata余量保持。主952／历史364／用户README逐SHA不变。


## Typed edge 独立编译与接续

2026-11-04T23:52:40.680673+08:00 **下一离线工作为收 Router R6 core9／typed6 原链结果。八个 typed edge 测试已独立编译通过并接好耐久后继。后继持原句柄，待十五场景全部自然0后自动启动 edge-only。主X952。**

R6 目前实际通过 2 项，合 138 检查，场景为 lower-time-direct, lower-time-resume。Resume42 和 direct96 已有独立自然0证据。主入口是 `Z/time-harness-supervision-router-r6/progress.json`，各场景按 `Z/b3-router-standard-validation-r6/build/cases/<mode>/process-result.json` 与 case-result 验收。固定十五项包括 core9 和 typed6，既有安装27另已通过。

`Z/b3-typed-edge-standard-validation-r1` 已完成一次标准 net8 harness 编译、PDB verifier 与独立复核。实际58 authored＋3 generated，共61文档，167引用，八个case仅有待执行源码。restore／compiler／verifier及外层均自然OS0，四条具体CS8602测试警告保留。四个正式net6产品自身W0/E0。独立复核122个实际／冻结源路径及引用、analyzers、imports、四库copy-local。preparation SHA 43cb4a35cec7eb525d3b7c589bec396c1a355ee9cc889bfb4ac15b5e81ef4ae2，build证据在该目录 `build/build-complete.json`。

八项覆盖实际wrong root／yield／kind-role请求、disposed真实GC handle、同initializer在original前后和退出后的拒绝、交叉旧新enumerator/yield，以及fresh Start后source-open前撤销与自然排空。新增一个helper、三个最小接入patch并限制入口只接受八项edge模式。已审查实际Completion→NativeIdentityFor→registered coordinator身份链，产品和原生helper均由现有冻结绑定提供。新增用例的行为须由各自实际OS终态证明。

新后继为supervisor40492→waiter25264，waiter持原R6 supervisor44812／core driver35296的精确句柄。配置 `Z/router-r6-to-typed-edge-preparation-r1/configuration.json` SHA 1a1ba9dba7386ea81a7eb6115897b8bb13f8e30fd609febc78382345b0f59475。当前state=waiting-r6-supervisor。原R6两进程自然0、原supervisor严格build/core/typed全0、九core与六typed的mode全集／唯一PID+FILETIME／DLL与stdout逐SHA通过后才一次启动edge-only。入口 `Z/router-r6-to-typed-edge-continuation-r1/progress.json`，外层OS结果最终在 `Z/router-r6-to-typed-edge-launch-r1/test-result.json`。任一失败落盘停链。后继实际运行与句柄观察见 `Z/root-typed-edge-build-review-r1/test-result.json`。

源码审查还列出四类后续缺口。native返回Reason/Role/FenceKind/Stamp损坏需要正式owned fault-output边界。distinct initializer同ordinal需要合法fixture控制。fresh initializer独立Source in-flight result8尚需在真实权限链内获得可消费identity，目前readInitialization在InFlight时receipt为0。跨owner／opaque复制拒绝也待独立用例。当前八项错误请求关联不能验收这些缺口。

验证runner的已知小缺口已记录。execution元数据写入会提前缩短预留槽，在之后OS采证异常且磁盘写失败时，最终process-result可能缺失。Popen后的finally仍自然wait，缺证据时停链。独立构建本次各实际元数据完整。原R11当前成功4/6，state=running-engine-pitch-transfer-pending，原PID468已重核精确FILETIME和OS状态，其E012→App47→fresh门独立保留。

Producer五项338／56记录释放、四产品标准构建及既有报告保持有效。完整S3、正式SDK／连续Clock、任意Seek、generation双ABA、其他writer/effects、生产DSP、全B01–B19继续在功能清单内。真实Speed/Seek准入关闭。已清旧构建19.3GB，当前新输出／cache／TEMP／备份均D，fresh硬预留792915741B及后继8MiB保持。主952、历史364和用户README逐SHA复核完成。游戏实测继续后置。


## Owned诊断故障变体与接续

2026-11-05T00:15:42.646722+08:00 **下一离线工作继续收 Router R6 的 core9／typed6。八个 typed edge 和两个 owned native诊断故障场景均已独立编译，两个耐久后继持R6原句柄等待十五场景全自然0后执行。主X952，Y应用门按原链。**

R6当前实际通过2项／138检查，场景为lower-time-direct, lower-time-resume。主入口 `Z/time-harness-supervision-router-r6/progress.json`，逐场景实际记录在 `Z/b3-router-standard-validation-r6/build/cases`。R6所需十五项与既有安装27按各自原件验收。八edge后继supervisor40492→waiter25264，配置SHA1a1ba9dba7386ea81a7eb6115897b8bb13f8e30fd609febc78382345b0f59475，当前state=waiting-r6-supervisor。

新owned native诊断故障变体已实现。基于R4逐SHA核对17个native原件，仅在ReadAudioInterruption已经组装完真实144B输出后，按thread_local控制修改返回副本的一个字段。新增CPP/header和一个.inc输出hook，新控制导出0为正常，1..24覆盖Reason、AttemptRole、FenceKind、ActualStamp、版本／尺寸／保留位、owner／epoch／enumerator／yield、initializer／ordinal／progress、attempt sequence/token、三fence、三childflag以及Invalidated/payload一致性。真实owner、journal、initializer、票及三fence均由原协议管理。

native变体 `Z/audio-diagnostic-fault-fixture-build-r2/test-result.json` 已自然OS0，VS2019 /W4 /WX构建W0/E0，19源输入与导出静态核对完成。新Source helper 202240B，SHA 33fec6f475a97f76f0927f65a487464bf2eb91cf3b9347f8dbe4e7171ecef7ad。Single仍为既有R4固定helper。首次R1因启动cmd.exe argv0写成正斜杠路径，在编译器执行前自然exit1，bin为空，原件保留。独立bootstrap探针已实证原生路径正常，R2用原生Windows命令路径及CRLF recipe构建通过。代理的未完成草稿和错误路径脚本也有单独原件清单。

两个managed场景 `Z/audio-diagnostic-fault-standard-validation-r1` 已一次新build、PDB verifier及独立复核通过。59 authored＋3 generated共62PDB文档，167引用，四条原测试CS8602保留，正式四库自身仍W0/E0。通过正常Group.ReadAudioInterruption入口，first/direct与transfer/wrapper两场景分别在真实original stage1里逐模式Reject，每次finally恢复0，再核完整正常POD相等、health／initializer／roots恒等，随后走新Start、source-open、真实Pause、SDKModuleService/journal及最终释放。两项运行结果尚待原R6全通过后收取。

新后继为supervisor3208→waiter41348，已持原44812／35296精确FILETIME句柄。配置 `Z/router-r6-to-audio-diagnostic-preparation-r1/configuration.json` SHA d16a1d13fc123625b8e61f74859e84bf0d0485b0a695c348aaebd4d37e34d10c，state=waiting-r6-supervisor。入口 `Z/router-r6-to-audio-diagnostic-continuation-r1/progress.json`，最终外层OS结果在 `Z/router-r6-to-audio-diagnostic-launch-r1/test-result.json`。门要求原两OS0、原build/core/typed严格全0、九core六typed各唯一实际进程及精确stdout/DLL，之后仅一次edge-only启动两个新mode。运行代码已经固定，失败保留停链。独立编译与原句柄观察见 `Z/root-audio-diagnostic-build-review-r1/test-result.json`。

输出故障尚处于待实际验证阶段。distinct initializer同ordinal、独立fresh Source in-flight result8和跨owner／opaque复制仍有独立缺口。source-in-flight重入候选被正式权限链更早拦住，静态顺序证据见 `Z/b3-source-inflight-feasibility-r1/test-result.json`。原R11当前4/6，state=running-engine-pitch-transfer-pending，实际原进程PID468已核FILETIME及OS状态。其E012→App47→fresh门独立推进。

Producer五项338、四产品标准net6以及既有报告继续按各自范围验收。完整S3／SDK连续Clock、任意Seek、generation双ABA、其他writer/effects、生产DSP及B01–B19仍在功能清单。新源码／build／obj／cache／TEMP／备份均D，fresh预留792915741B和后继metadata余量保持。主952、历史364、用户README逐SHA复核完成。游戏实测后置。


2026-11-05T09:06:05.421294+08:00 **下一离线工作改为收重启后的恢复批次。旧链与旧等待任务已经不在当前OS中，不能再按旧progress等待。R6新批已一次启动，R11两项恢复正在独立准备。**

本机本次启动时间为2026-11-05 08:47:06＋08:00。独立现场审计见 `Z/root-r6-terminal-review-r1/terminal-review.json`。旧R6 wrapper与R11 transfer-pending均在上一启动周期2026-11-04 16:18:47Z返回1073807364（0x40010004，本地Windows SDK符号DBG_TERMINATE_PROCESS）。两项stderr均为空，原因为何尚无直接证据。R6最后checkpoint33是此前通过的trace，不能当作失败断言。所有旧失败／日志／原后继均保留，旧链不重开。

R6恢复目录 `Z/reboot-recovery-r6-r1`，配置SHA c49d948d45d8f7197abf6b1f7eaa7ebce5654fc9df52dea7d64c748c696e1d01。supervisor3892已通过WMI outside-job一次启动，driver42764精确FILETIME与OS live状态已核，当前state=running-lower-time-wrapper。仅复用逐SHA／实际源／冻结源／PDB重核的同一R6程序集，13个未验收场景随后接八edge与两fault场景，任一失败保留停链。既有resume42和direct96共138只引用不重跑。十五项的替代门明确由旧2＋新13组成，不向旧目录补写成功终态。旧2按原schema验收，原件没有exit FILETIME及独立OS exit字段，新13／edge8／fault2使用新完整字段。

R11旧四成功共273检查保持，原transfer-pending同码非零，transfer-zero-watch未执行。新 `Z/reboot-recovery-r11-r1` 仅准备余下两项，复用原冻结程序集与966源、二进制和inputs。原E012→App47→fresh不能从旧等待任务自动继续，恢复六项验证后必须另建明确的封存／应用门。当前main仍X952，Y与S3未应用。

新生命周期模块 `Z/metadata-terminal-slot-r2/terminal_slot_r2.py` SHA bc8e3dc18f8195f2ffaa5e8ac69bf98f3844fb677119a2e23ca11760c3f31586 已固定。两个65KB预留槽全程不truncate，Popen原句柄在finally自然wait，execution采证／写入异常不会跳过终态落盘尝试。旧缺陷已在两原runner副本实际复现，七类新验证和两旧复现共九个自有短进程均有实际句柄见证。终态介质本身写失败仍可能没有完整JSON，此时抛错保留预留槽，不能签成功。正式四库仍W0/E0，既有测试四条CS8602保留。

独立功能工作已选定SDK当前原生媒体时间只读快照。现IGameplayClockService尚未提供当前唯一NativePlaybackClock的media-seconds，候选须走已注册session／publication／receipt及真实DSP采样，不借callback-only GetTime权限。实现／验证放独立D目录，完成状态以其新证据为准。真实Speed/Seek准入、任意Seek、generation双ABA、其他writer/effects、生产DSP与B01–B19仍未整体完成。已清旧构建19.3GB，新输出／TEMP／cache／备份继续D，fresh保留792915741B＋metadata余量。真实游戏／UI／账号／网络不操作。
