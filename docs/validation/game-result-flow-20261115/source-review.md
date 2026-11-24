# 本轮实际结果流程的源码核对

2026-11-15，root只读核对当前已安装候选的完整1172源码：`D:/riz-build-cache-next/runtime-tick-admission-20261115/run-r2/source`。这是为定向游戏观察设定判据，没有反编译或分析新的游戏二进制，没有修改生产源码。

`src/RizToolbox.Adapter/GameEventState.cs` 的 `Controller` 只在当前LevelInformation出现真实`ProgressControl.isPlaying && timeReady`后创建RunId。`levelOver`只进入AwaitingResult，不自行完成。`ControllerUnavailable`与场景切换都不推定取消；它们出现ObservationLost是明确的缺少终止证据。

`GameEventObserver.Poll`查找实际活跃result controller的`levelOverInfo`并复制InsideLevelResult字段：Score、RawCompleteRate、perfect、maxPerfect、bad、miss、crashed、skip。新结果还要消费`ResultOrigins`提供的原返回来源；旧指针、初始化已有结果和重复来源不构造新的完成事件。

`GameEventState.Finish`只有同时满足当前open run、相同session/run/LevelInformation/controller、当前原结果source generation、匹配chart，且`levelOver`或controller已消失，才发`RunEnded`并把结果关联到已观察的RunId。否则只发`ResultObserved`且RunId为空。`crashed`决定Failed，`skip`决定Skipped，其余为Completed；完成率低或界面出现成绩数字本身不能替代这些证据。

`Modules/ExactResultOriginModule.cs`观察已固定的NormalModeInLevelController.ControllerOver原始返回（既有profile3BEA60），保留entry graph与原始InsideLevelResult。应核日志`Result-origin return: accepted=True`、当前run身份及后续`Observed lifecycle RunEnded`、实际`RunFinished`字段。这里只读既有源码合同，FREE是否在实际结束时走同一返回来源仍需观察；不能由没有显式FREE分支推断实际原生调用已覆盖。

`Modules/ExactBackCancellationModule.cs`把Back与terminal Update作为两个独立来源：先捕获当前paused graph及观察run，原Back返回后`_over`成立且尚未ending时保留请求；对应原Update还需`_terminated`和controller._skip的false→true边界，才能排队Aborted并在宿主安全点发布。应核`Accepted Back request retained`与`Accepted Back terminal cancellation queued`两条日志及同RunId的RunEnded。菜单返回或对象消失均不能冒充这一正向流程。

本次新会话25008/20261115T051638374Z-892aaffc8645488f9462136b3308845c已实际进入设置页。只读phase capture已核安装23文件与GameAssembly身份，设置read/override两模块均active；尚未测试SDK主动修改设置、仲裁和恢复。游戏多次因失焦自动暂停，正常结算仍未观察，现等待用户将当前局停在结算页；不把等待计为通过。

后续实际观察已结束上述用户接手：用户回复“跑完了”，root实际看到RESULT页并冻结result-page。源码要求的原ControllerOver来源确实accepted=True，RunStarted/RunEnded/RunFinished关联同一RunId9674fa3a160c412b969673391aa14dcb。Score0、RawCompletion0、Bad0、Miss479与界面一致；实际结果Crashed=true、Skipped=false、EndReason=Failed，因此只证明本候选失败结算闭环，不证明Completed正常通关或完整B01矩阵。先前等待属于当时历史，不用它覆盖后续正向观察。
