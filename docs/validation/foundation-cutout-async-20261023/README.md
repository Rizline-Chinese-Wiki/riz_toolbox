# Cutout直接异步协程

2026-10-23。**1412/1412、25套完整离线检查通过，冻结暂存未安装**。Runtime139/Hooks127/Audio535，新增19组（17协程、2source窗口）；承接[1393](../foundation-active-destruction-20261023/README.md)，当前安装仍1021。

## 实现与接线

[tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineTracker.cs)/[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineBackend.cs)观察reset SimpleDelay与每Trigger的延迟事件。准入来自same launch实际EventTrigger source、StartEvents物理代次、caller38A60C/38A6B2和独立root的真正IEnumerator。public Start/Auto和共用Delay Move只共享现有typed native patch；事件Move@39B4D0新增逻辑观察。静态Trigger工厂不是ref0实例factory，本批没有新增该ABI。

Start original之前发布enum，承接同步首Move。reset可在<=0首帧自然false/Coroutine为空，正秒数接受provider state1逐帧更改剩余seconds或WaitForSeconds state2；event只走state0→1→false/-1。reset Action必须匹配exact MethodInfo、EventTrigger Target、singlecast/nonvirtual/direct entry；相同RVA的EncoderNLS方法不能借用权限。每record自己的Trigger/events/offset不能换成source中另一合法row。

64同时记录、128根，正常终结后撤身份/redrain、enum/Coroutine根独立各free一次，确认后才清旧身份/复用代次。130次原始地址复用/角色切换已覆盖。新的factory/Start仍继续观测；ObservedEnded只指当前直接协程，不撤future producers或签发全run证明。其它observer触发launch/source fault时，纯ObservationAuthorityUnknown让读取/发布/free立即保责任，局部rootfree失败仍可继续安全独立根的一次清理。

[Source](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutEventSource.cs)给出纯物理分派代次窗口，要求main/非busy/known/own inFlight=1/entered=exited+1；typedcaller与actualhost另核。[coordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)激活前Capture并接Ready帧泵/快照，[active destruction](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalActiveDestructionTracker.cs)前后核真实协程在途，不能仅凭StartEvents已经返回就继续原生liveness/Destroy/free。

## 验证与限界

[17组受控协程](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalCutoutCoroutineRegression.cs)与[2组真实broker/probe窗口](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalCutoutEventSourceRegression.cs)在[定向535/535](audio-targeted-final.log)及[完整构建](build-summary.json)通过。首轮[534/535](audio-targeted-first.log)为fixture可选callback内计数未执行，修为每次读取无条件增加计数再注入；失败日志保留。

用户明确“重启他们”后三位代理恢复：activation_finish写17回归，settings_finish写backend，never_active_retirement_code独立只读审查，root负责tracker/接线/统一构建；见[review](review.md)。无第二runner。新增受控故障不代替生产真实metadata/delegate/GC free或游戏验收；native helper31261/61358、initial helper1747及native-only probe18按原范围单独记账，不加入1412。

[21范围/1592指令](terminal-evidence.json)/[逐字节复核](terminal-evidence-check.json)与[四函数证据](next-async-evidence.md)、[EventTrigger路径](event-trigger-research.md)固定三输入hash/6直接E8候选，不是完整listener图。两个throwing Reset及reset Action live代码范围在backend核验，不能把Reset当停止/结束操作。完整增量0编译诊断，前置17条既有测试warning保留。

Source.Pending仍true，分派后UnsealedDispatchSource保留；动态listener/provider转交工作及GO销毁取消未自然false的路径继续未封口，不发全run token、不Close、不释放factory/bundle/bridge/handler/coordinator。完整剩余见[scope](scope.md)、[下一步](next-integration.md)。RequestPlay关闭，仅B03/B18a勾选，完整B01–B19目标active。

## 冻结与复现

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | 419项，4,366,199字节 | `8631af3deacab76ede9b9bb85ed0c28f8478a4b66644cc5f5e5240671348a4c3` |
| [源码manifest](source-manifest.json) | 419项 | `5a4cbc74b3b1c9bbfb0bac82d0a0fc8277d588f31fbdbab9ee3a2138ab671a07` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | 18项，3,215,336字节 | `0e86c65799838c9912f1421b896b2d5633890198f15db1deb33c07203e460158` |
| [完整日志](build.log) | session5141 exit0 | `8d02c38c96d3af1fb4536843c05f562f87f10e2d146f0b1a342cceaec4a99955` |

[较1393差分](changes-from-1393.diff)/[清单](changes-from-1393.json)：3新增、5修改、0删除，两源ZIP各自核对。[暂存](staging-check.json)/[源码](source-integrity-check.json)、[旧1021安装和旧326归档](previous-installation-check.json)及[原游戏三文件](original-game-check.json)匹配。

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/cutout-async-reproduce-20261023'
```

用户“先继续代码，实测稍后”保持。无本批安装、游戏/UI操作、游戏进程终止、原文件修改、commit/push。

有限追加材料：[后端metadata/Reset/清理复核](backend-review.md)、[下一轮listener接线计划](next-listener-plan.md)。两者仅文档，没有改变本批冻结输入或增加游戏验收。
