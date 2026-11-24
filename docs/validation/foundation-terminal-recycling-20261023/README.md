# 协程观察根回收

2026-10-23。**1367/1367、25套完整离线检查通过，冻结暂存未安装**。Runtime139/Hooks127/Audio490，新增7组；承接[1360](../foundation-terminal-coroutines-20261023/README.md)。当前安装仍1021。本批只回收已结束协程的观察责任，尚未完成整局active退休。

## 代码行为

[tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineTracker.cs)将16条记录改成同时尚未退休记录上限。自然结束后先撤Published身份，保留enum/代次识别迟到worker/main调用，再读实际native activity；非零待后帧且不重发布。确认后[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineBackend.cs)分别释放Coroutine/enum根；每根只尝试一次，独立失败不妨碍仍安全的其他根。只有free及末次drain均已确认，才清旧enum并允许下代复用。

worker fault在activity读取期间发生时，读取后再次拒绝；不把已花掉的root操作重复执行，也不把该record报告为成功退休。Created/NaturallyEnded/Retired是累计事实，LiveRecords是当前占用，required Cutout结束事实不随slot/角色复用丢失。后续factory仍可重新进入Pending；ObservedEnded并非producer永久封口。

[coordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)在Ready帧泵调用回收，以及initial tracker已有的TryReleaseAfterEnded。未关闭terminal producers，未释放controller/bundle或签整局消费者结束。

## 验证与边界

[七组新增回归](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalTerminalRecycleRegression.cs)覆盖64次创建/同地址复用/角色切换、撤身份后二次drain等待、main/worker重入、独立free失败、free后活动、末次drain worker和结束查询worker。使用既有子进程隔离保留未知责任的测试，不修改生产32-owner配额。[定向490/490](audio-targeted-confirmed.log)和[完整构建](build-summary.json)均通过；最初别名编译错误和489/490容量失败日志保留。

activation_finish、settings_finish、never_active_retirement_code三位子代理做有限只读审查，见[review](review.md)。生产backend真实gchandle free异常尚无直接fixture，新backend尚无游戏验收。完整增量0编译诊断，前置定向仍有15条既有测试warning；不声称干净重建零warning。既有native helper31261/61358、initial helper1747、native-only probe18独立记账，不加入1367。

[12范围/991指令](terminal-evidence.json)/[逐字节复核](terminal-evidence-check.json)冻结三输入hash与6直接E8候选。新增Reset@3C5A00/3C5C40各64字节，只构造并raise NotSupportedException，不写枚举状态；禁止将Reset作为停止/结束凭据。另查到[Cutout的外置EventTrigger来源](event-trigger-research.md)，动态UnityEvent listener无法据这些静态函数证明结束。

## 冻结与复现

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | 409项，4,195,697原始字节 | `aecd80193c7f35162444f7e34951a72f3cf294f198e7c5d412a7f831f414859c` |
| [源码manifest](source-manifest.json) | 409项 | `c0fd65527084886263068bbb0c8819dc5a0603f23690925b508012a3f5953ee0` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | 18项，3,124,712原始字节 | `f98e5f148dc65dd8091577b09a498a2c7ac75e1518d0b65bdf0b533777525fbe` |
| [完整日志](build.log) | session1689 exit0 | `c7834d5a1968ac976518193780f78b1f196ac470c51eea9b74618d4af222dbb8` |

[较1360差分](changes-from-1360.diff)/[清单](changes-from-1360.json)：新增1、修改5、删除0，两源ZIP独立核对。[暂存](staging-check.json)/[源码](source-integrity-check.json)、[旧1021安装与旧326归档](previous-installation-check.json)、[原游戏三文件](original-game-check.json)匹配。

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/terminal-recycling-reproduce-20261023'
```

[scope](scope.md)与[下一步](next-integration.md)明确未完成项。RequestPlay关闭，仅B03/B18a勾选，B01–B19目标active。用户“先继续代码，实测稍后”保持，无本批安装、游戏/UI操作、进程终止、commit/push。
