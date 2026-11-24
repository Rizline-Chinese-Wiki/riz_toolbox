# 精确初始化与共享 Timeline 隔离

2026-10-23。**1210/1210、25套完整构建通过，新包仅暂存；当前安装仍1021四门包。** Runtime131、Hooks96、AudioContent372，较1187批新增23组离线检查，不能换算为底层覆盖率。完整日志保留13条共享FakeNative的CS0649警告，没有编译错误。两个旧helper fixture为31261/61358；新native-only probe为18次，包含raw Play EDX高位污染；生产真实Dobby初始化ABI检查已在完整Runtime suite通过，独立runner引用同包DLL再次通过。

本批将已有launch预留基础设施接到Start/getter/Play sealed replacement，并把初始化与终止的Timeline权限放在同一GameMode entry上。用户要求实测稍后，本轮没有安装或操作游戏，生产 `RequestPlay` 继续返回明确的OperationUnsupported。通过不等于游戏验收。

这里的“接到replacement”指可调用的生产Runtime/Adapter实现和离线测试接线。源码搜索尚无Loader/模块调用实际termination bridge Create、handler构造或LaunchReservation.Reserve；没有用户可进入的真实LOCAL启动coordinator，也未启用游戏初始化路径。下一步仍要串起真实launch/activate、commands/result view和active退休，才能评审入口开放。

范围见 [scope](scope.md)，有限独立审查过程见 [independent-review](independent-review.md)。下表只描述当前源码与已读取的回归路径，不以“有接口/有测试”代替完整要求。

## 对照 I01–I08

| 任务 | 当前实现 | 尚未证明或完成 |
|---|---|---|
| I01 sealed authority | 固定画像、metadata/RVA/参数/返回形状；新增Start/getter/Play/Tooltip，Mode复用原Progress entry。普通透传与初始化、Play、终止各自权限分开 | 完整游戏metadata解析/真实prologue安装、所有间接Steam入口仍未验 |
| I02 Start/getter与I02b Play作用域 | TLS未定Start父帧；pure exact controller先claim再main验证；独立Play仅授Mode。ordinary子帧遮蔽较旧权限，失配/worker/失败保留unknown；已认领帧在logical detach后保留权限 | 原生游戏异常展开与实际Unity回调顺序不由这些管理帧证明；scope allocation失败/溢出只做有界拒绝，不表示业务成功 |
| I03实际图与原调用 | Before核对父Start.info与真实入参、反向progress、exact owned params及五引用/资源闭包；返回独立root后再核验。getter许可前拒绝不调用original，许可后只调用一次；完成receipt追加实际loadOver读回并等完整Start退出 | actual activation、真实settings/Awake/globals、真实RunStarted与游戏结果尚未验；getter失败null与原调用自身native异常是不同范围 |
| I04根与原配额 | 复用现有graph/work/factory/capture配额；原graph record固定controller/info/progress/Start/returned params根槽。unknown责任保留，纯空预留取消与native尝试后清理分开 | immutable progress发布仍在采用图时分配数组，初始化identity亦可能在native后分配；捕获异常并保责任不等于全部发布存储已activation前预留。历史helper临时root覆盖未因本批自动补齐 |
| I05部分挂接/解绑 | 新入口先解析；同Mode不重装；部分挂接保留entry/delegate/trampoline和unknown owner；logical detach不抹去当前claim，重复或仍在途owner拒绝重绑 | 实际游戏并发改写/全原生异常矩阵未证；不声称Dobby patch对所有执行线程原子 |
| I06真实drain与ready | 新Start/getter/Play/Tooltip及共享static实际入口计数接入bridge；初始图采用和getter返回不能提前commit；worker fault原子撤ready，最终receipt等待Start退出与loadOver；结果/退休仍依赖确切旧消费者证明 | 计数只证明已观察入口的活动状态，不能证明不存在未来callback；active消费者结束机制仍缺。共享Mode在聚合时可能保守重复计数，零值判断仍需全部入口排空，不能将总和当唯一调用数 |
| I07离线覆盖 | Hooks行为回归、Adapter经生产Runtime binding进入Before/After，另有生产字段/publication受控路径；Windows x64 C++ caller→真实Dobby→生产replacement→native original已在完整Runtime及独立runner执行通过 | C++原生original SEH/异常穿越CLR trampoline未覆盖；不是游戏内测试 |
| I08交付与live验收 | 本目录记录范围、实现对照和独立审查限界；完整日志、364源/18暂存ZIP及独立runner均核对，历史1187归档保持独立 | actual activation/admission和游戏连续会话未完成，不新增B勾选 |

I04的映射分配失败会转unknown并保留已有根，本页没有把它描述为无责任丢失；它仍不满足原计划“全部发布存储提前预留”的更强条件。下一实现应在native/activation记账前，为同一record预留immutable初始化身份、progress token及映射发布容量，采用图时只填/发布已预留记录，并验证失败不会多占第二套配额；本批不把I04标完成。I06的drain与closed consumer-end证据也不互相替代。

## 源码边界

Runtime为 [ExactHookBroker.LocalInitializationIsolation.cs](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.LocalInitializationIsolation.cs)；现有 [Progress隔离](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.LocalProgressIsolation.cs)共享Mode、Instantaneous与EndRange能力。Adapter为 [NativeLocalRunInitializationBoundary.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunInitializationBoundary.cs)、[graph bridge](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunTerminationBoundaryState.cs)与 [共享launch状态](../../../src/RizToolbox.Adapter/AudioContent/LocalRunLaunchCommitState.cs)。

LOCAL getter已被纯身份识别时，先保护其实际父Start，再检查主线程和native图。Before/After失败返回native null并置sticky fault，不调用原getter作兜底，也不撤销父尾保护；ordinary原调用保留原异常语义。本批没有证明IL2CPP getter原调用抛出的native SEH可安全跨越managed反向P/Invoke、执行finally并回到游戏异常处理，不能把managed IOException注入或未发生SEH的fixture当作证明。

[新增native fixture](../../../native/RizToolbox.InitializationAbiFixture/README.md)把原始full-width pointer、引用/null返回、bool和float位型、worker/递归及null失败输送分开验证。[native-only probe](native-build/initialization-abi/native-probe-result.json)的18次只校验C++ harness，明确 `managedTransportVerified=false`；生产Runtime suite已执行真实Dobby case，[独立runner](independent-native-abi/README.md)引用相同发布Runtime/SDK再执行通过，exit0、编译0 warning/error。独立runner的 [result](independent-native-abi/fixture-result.json) 与 [六项SHA](independent-native-abi/fixture-manifest.json)均已实际复核，Runtime SHA为 `16cdba8495830a888966c574d6bf22c303125d27de66999deaab19840077bf69`。Play用真实MASM caller设置非零EDX高24位与DL=0/1，实际生产I1输送仍保false/true。所有结果仍为 `gameRuntimeVerified=false`。

## 冻结与构建证据

- [build-summary.json](build-summary.json)：1210/1210、25套，Runtime131/Hooks96/Audio372；[build.log](build.log) SHA256 `f7f9d4ea7ee17e6dc02ef4d3d0073e3212607eb12dbd639c4d8f9a06d895cedf`。编译警告是13条共享FakeNative CS0649，不能写成零诊断。
- [source-archive-check.json](source-archive-check.json)：[source-inputs.zip](source-inputs.zip)共364项，ZIP SHA256 `0cd7d8ac15b436ad2d7a0a1063134338ddcabf83912b6da3f8f2411668c27014`；[manifest](source-manifest.json) SHA256 `23d9c3672841f895f490097861a9c29cf1f7d626b15779055a66e4b166860b71`。独立交付审查已逐项核对ZIP和冻结时当前源码。
- [package-archive-check.json](package-archive-check.json)：[staged-package.zip](staged-package.zip)共18项，ZIP SHA256 `cbdc9040f5a94290d7a6420d6774bce54ce75b25d801f049fd341ab0dc420da4`；逐项与[发布manifest](package-manifest.json)和暂存目录匹配，`installed=false`。
- [previous-installation-check.json](previous-installation-check.json)：实际1021安装18项及其旧326源码归档分别核对；[original-game-check.json](original-game-check.json)：原exe/GameAssembly/global-metadata三SHA不变。1187和更早历史归档不重写。

过程失败保留：[Hooks first](hooks-targeted-first.log)有两处CS1501测试调用编译错误；[Hooks second](hooks-targeted-second.log)为95/96，旧“失败安装可重试”断言与新的unknown原生安装保留策略冲突。其后第三、第四次均为96/96，[第四次](hooks-targeted-fourth.log)作最终Hooks定向；[Audio second](audio-targeted-second.log)为372/372，[Runtime targeted](runtime-targeted-first.log)为131/131。最终完整构建与独立runner分别留存，不把早期失败改写成从未失败。

重新构建命令会改写新暂存输出，不安装或操作游戏：

```powershell
Set-Location C:\Users\71957\Desktop\riz_toolbox
& .\scripts\Build.ps1 -NativeOutputDirectory D:\RizToolbox-build-cache\initialization-isolation-20261023
```

归档只固定声明输入和发布文件，不包括完整工具链/依赖缓存，不证明新系统复现。继续开发后应核对历史ZIP，不要求旧manifest证明新工作树未变。详见 [正式报告](2026-10-23_底层-精确初始化隔离-report.md)。

## 下一步仍需保留的门槛

本批日志、归档与独立复查已收口，仍需实际activation协调器、完整handler/sink/Retry/Back、六globals归属/条件恢复和active消费者结束证明。恢复授权实测之后，以同一镜像和同一新包验证普通原版与LOCAL实际初始化、开始、暂停、重试、结果和返回；不能从Start/Play的明确Timeline锚点推导所有Save/Steam间接或异步路径已覆盖。

完整目标继续按 [B01–B19 SPEC](../../../.docs/foundation-expansion/SPEC.md) 和 [全范围剩余审计](../foundation-local-termination-20261023/foundation-remaining-audit.md) 推进；本批仍是其中一段底层接线。
