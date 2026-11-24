# native Judge 在途与退休前 drain

2026-10-23，普通工程报告，flavor=null。**读取真正的 native Judge 活动，并将它纳入 LOCAL 准入、终止快照及图退休；1298/1298、25 套完整离线检查通过，新包已冻结暂存未安装。** 完整活动局退休仍未完成。

| Evidence | Finding | Path |
|---|---|---|
| [native helper](../native/RizToolbox.JudgeContext/JudgeContext.cpp)、[生产 C# bridge](../src/RizToolbox.Runtime/Hooks/NativeJudgeContextBridge.cs) | 原有native finally统计此前未被C#消费，本批接通实际GetStats及sticky unknown | 准入/退休不再遗漏此在途窗口 |
| [broker](../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeJudge.cs)、[LOCAL桥](../src/RizToolbox.Adapter/AudioContent/NativeLocalRunTerminationBoundaryState.cs) | exact lookup读取物理入口，Judge/Hold完整managed区间计数纳入撤分类前后drain | 下一步仍需完整旧来源结束和实际资源退休验证器 |
| [真实Dobby Runtime用例](../tests/RizToolbox.Runtime.Tests/NativeJudgeActivityRegression.cs)、[完整日志](validation/foundation-judge-drain-20261023/build.log) | 原生阻塞worker及逻辑解绑后保持inFlight=1，normal/SEH/C++结束后归零，原异常信息保真 | 限于受控原生fixture，不能扩成游戏全覆盖或managed inner/Hold unwind证明 |
| [源核对](validation/foundation-judge-drain-20261023/source-archive-check.json)、[包核对](validation/foundation-judge-drain-20261023/package-archive-check.json) | 390源和18发布文件对应本批通过结果；原游戏文件未改 | 暂存保留，按用户安排延后游戏实测 |

详见[本批README](validation/foundation-judge-drain-20261023/README.md)、[scope](validation/foundation-judge-drain-20261023/scope.md)与[主代理自查](validation/foundation-judge-drain-20261023/review.md)。完整B01–B19继续；RequestPlay仍关闭，菜单锁/相机、活动controller和音频/旧回调退休、Retry/Return命令及Loader组合未完成。当前安装仍1021，不新增验收勾选。
