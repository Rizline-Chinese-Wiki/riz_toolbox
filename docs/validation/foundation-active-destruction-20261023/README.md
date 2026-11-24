# 活动销毁与Cutout事件来源

2026-10-23。**1393/1393、25套完整离线检查通过，冻结暂存未安装**。Runtime139/Hooks127/Audio516，新增26组；承接[1367](../foundation-terminal-recycling-20261023/README.md)，当前安装仍1021。

## 实现

[active tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalActiveDestructionTracker.cs)/[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalActiveDestructionBackend.cs)在activation前借[原factory的实际五根](../../../src/RizToolbox.Adapter/AudioContent/OwnedNativeInactiveController.ActiveDestruction.cs)，立即存独立root并核identity/class和真实父子图。required Cutout完整退出之后记真实Unity frame，更晚帧才核三个clone对象Unity-dead；旧origin scene使staging先dead也正常接受。对仍活且本owner持有、未激活、无父无子staging才可请求一次Destroy，再等更晚帧。没有再次Destroy活动clone或套用inactive cleanup。

[EventSource](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutEventSource.cs)/[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutEventSourceBackend.cs)从真实controller字段取得EventTrigger、GO/Transform、triggers列表和最多32个Trigger/UnityEvent/offset，持独立root及只读快照。StartEvents@38A460使用真实broker/probe覆盖worker/reentry/异常与在途，fault传播同launch标量信号；共享source屏障先于cleanup/销毁取证，读取活动期间的fault也读后拒绝。get_Item的owned参数先存process-held记录，Runtime未采用即拒绝或cleanup异常时保责任。

[coordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)接激活前Capture、Ready/AwaitingControllerDestruction/ControllerDestroyed帧泵和两个诊断快照。ControllerDestroyed只表示三clone对象，五对象与全部消费者分别记录。EventSource无论是否分派都仍Pending，分派后UnsealedDispatchSource=true；既有外置SimpleDelay/StartEventsEnumerator和动态listener还没有完成追踪。本批不Close registry或释放factory/bundle/bridge/handler/coordinator。

## 验证

[12组销毁](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalActiveDestructionRegression.cs)与[14组事件](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalCutoutEventSourceRegression.cs)在[定向516/516](audio-targeted-first.log)和[完整构建](build-summary.json)通过。覆盖后帧、partial death/复活拒绝、已dead staging、空父Destroy、未知free/Destroy、worker/reentry、实际broker/probe异常/在途、列表变化/快照不可变等。使用既有新进程隔离故障责任，未改生产quota或清静态owner。三位子代理分工与确定缺口修复见[review](review.md)；root唯一构建，无第二runner。

[16范围/1399指令](terminal-evidence.json)/[磁盘再核对](terminal-evidence-check.json)与[EventTrigger路径](event-trigger-research.md)固定三输入hash及6直接E8候选；非完整动态listener图。完整增量0编译诊断，前置17条warning保留（15既有、2新增测试计数器CS0649）。native helper31261/61358、initial helper1747和native-only probe18独立记账，不加入1393。生产metadata/Unity销毁/free故障和get_Item quota并无直接验收，新InstanceVoid original的SEH/C++跨CLR展开未新证明；详见[scope](scope.md)。

## 产物

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | 416项，4,290,615字节 | `aa750bb9cec77862762a3166b787244f3a0ac5a544c836fc0dd2fd3c8d970d92` |
| [源码manifest](source-manifest.json) | 416项 | `16b4d6bc959dfcdcf76b9120c09b6f19ee502897e8185c2dc9158154425edd42` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | 18项，3,184,104字节 | `2deac1ff398f50fb54b9ea4dbb6783ad33794453b5522e5772cb7585539df789` |
| [完整日志](build.log) | session90989 exit0 | `9e451d404eed01cf729e7126e33e46240f7e47100c87881f201b49cda735d259` |

[较1367差分](changes-from-1367.diff)/[清单](changes-from-1367.json)：7新增、3修改、0删除；两源ZIP各自核对。[暂存](staging-check.json)/[源码](source-integrity-check.json)、[旧1021安装及326归档](previous-installation-check.json)和[原游戏三文件](original-game-check.json)均匹配。

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/active-destruction-reproduce-20261023'
```

[下一步](next-integration.md)继续外置异步/listener、音频/全部future来源、资源退休、完整命令/Loader及SPEC其余底层任务。RequestPlay关闭，仅B03/B18a勾选，完整B01–B19目标active。用户“实测稍后”保持，无本批安装、游戏/UI操作、进程终止、原文件修改、commit/push。
