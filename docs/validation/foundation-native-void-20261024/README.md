# 原生void父分派

2026-10-24。**1475/1475、25套完整离线检查通过，冻结暂存未安装**。Runtime159/Hooks144/Audio561，较[1449](../foundation-unity-events-20261023/README.md)新增26组；当前安装仍1021。

## 实现

[helper](../../../native/RizToolbox.JudgeContext/VoidContext.cpp)提供独立Windows x64 `void(self, MethodInfo*)` transport，40字节Frame/Config、48字节Stats、32不可复用槽。真实caller来自原生入口，original在native层执行一次，`__finally`覆盖正常与异常退出。观察callback故障被包含并计数，原函数的SEH/C++ code/kind/payload继续向原生调用方传播。

[bridge](../../../src/RizToolbox.Runtime/Hooks/NativeVoidContextBridge.cs)与[broker](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeVoid.cs)用Prepare→Configure→Commit发布，未知结果不重试。每次物理调用持token/frame/thread/probe快照；worker不等待安装Gate，错误MethodInfo和foreign child不借父上下文。最多4096同时记录，溢出帧在probe/capture之前保守拒观察、原调用仍一次，既有记录继续配对退出。跨线程退出保留整条记录并Unknown。Binding/probe/exact activity合并真实native stats，After/Dispose结束不等于native Exit已结束；逻辑detach只撤后续投递。

[resolver](../../../src/RizToolbox.Runtime/NativeRuntime.VoidHooks.cs)只授予固定UnityEvent.Invoke官方class、无参instance void、非static/abstract/generic/inflated、RVA `15642E0` 的内部能力。新ABI追加、不改旧值，普通hook API拒绝，不开放任意地址调用。[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.cs)/[ledger](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.cs)已切换，保存实际父caller/MethodInfo并核exit；独立Prepare和图内每次native操作前后也核共享通道Known，防止foreign故障只标记通道后仍继续读图。

## 验证及边界

[4组真实native](../../../tests/RizToolbox.Runtime.Tests/NativeVoidAbiRegression.cs)通过真实Dobby与MASM tail relay核精确返回标签、完整self/MethodInfo、foreign递归、worker堵塞和逻辑detach、原函数一次、SEH/C++异常保真与probe快照。独立native callbacks的7故障模式×3原函数模式共21组合，核flags/token/原异常及计数；不是托管callback吞异常的替代测试。[机器索引](native-void-evidence.json)与[有限复核](real-native-review.md)记录范围。

[17组受控Hooks](../../../tests/RizToolbox.Hooks.Tests/NativeVoidObservationTests.cs)另核publication、4096上限、故障隔离、并发token、callback根和GC后可用。另有2组受限resolver检查，以及事件层3组新检查；原有13组图回归已切换为实际broker+受控native桥。共享Known读门、独立Prepare入口/原调用/接收输出时失效都保持Unknown，不出版新图。受控Frame.Caller不能当游戏caller；metadata、图与实际UnityEvent运行仍需另验。

首[完整构建](build-first.log)Runtime159/Hooks144通过，Audio fixture漏声明INativeDetourEngine导致CS1503；修正后[定向561/561](audio-targeted-first.log)和[最终25套](build-summary.json)通过，未放宽生产接口。完整增量诊断0条，前置既有测试warning保留，不宣称clean build零warning。三代理分工与有限审查见[transport复核](transport-review.md)；root统一构建，无第二runner。

[静态证据](terminal-evidence.json)/[字节核对](terminal-evidence-check.json)沿用同一23范围/1706指令/6E8候选，再核三输入hash；没有新增game中段/leaf执行证据。既有native helper31261/61358与initial1747按原范围另记，不重复加入1475。

内联执行集合/三条实际listener调用点、连续mutation、listener/provider转交、销毁取消和future来源封口仍缺；Pending保留、InlineExecutionUnobserved=true、DispatchPathVerified=false。全部消费者和资源退休、完整Retry/Return/Loader继续缺，RequestPlay关闭。 这轮native parent证明不关闭上述缺口。见[scope](scope.md)与[后续](next-integration.md)。完整B01–B19 active，仅B03整项/B18a子项勾选。

## 冻结

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | 431项，4,618,680字节 | `5cf3a1367e75af063ac372e0463025142c35f18b2a4c268e3b82d3b38be40490` |
| [manifest](source-manifest.json) | 431项 | `c33aa28c658179efc89711aa7d9a9c69ae293c368035b27d8640d468eca8a79a` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | 18项，3,339,240字节 | `dda28b995df7c67b8cf06656b712ee551942ec0ed9ef4fab568c5cc542678a65` |
| [完整日志](build.log) | exit0 | `4326638772c5dcdbfd1ee17a23b2238b3e2aaf1f6609033292eb93284d561c8d` |

[较1449差分](changes-from-1449.diff)/[清单](changes-from-1449.json)：7新增、14修改、0删除。两源归档各自核对；[暂存](staging-check.json)/[源码](source-integrity-check.json)、[旧1021安装与旧326归档](previous-installation-check.json)及[三原游戏文件](original-game-check.json)匹配。[文档检查](documentation-check.json)只核本地链接。

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/native-void-reproduce-20261024'
```

用户“先继续代码，实测稍后”保持，无本批安装、游戏/UI操作、进程终止、原游戏文件修改、commit/push。
