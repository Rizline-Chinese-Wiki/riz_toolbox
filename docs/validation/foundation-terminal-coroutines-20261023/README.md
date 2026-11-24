# 结算协程与按名称启动

2026-10-23。**1360/1360、25套完整离线检查通过，冻结暂存未安装**。Runtime139、Hooks127、AudioContent483；较[1343初始化协程批](../foundation-initialization-coroutines-20261023/README.md)新增17组：13组终止tracker、2组实际result capture组合、1组Factory Hook、1组真实native Factory ABI。

本批在生产启动类激活前安装Cutout/Fade观察，结算捕获实际包围ControllerOver的命名Cutout发布。原安装仍1021，没有本批游戏实测；完整资源退休和Retry/Return/Loader组合尚未完成，RequestPlay关闭。

## 关键发现与实现

[静态证据](terminal-evidence.json)/[957条指令](terminal-disassembly.txt)确认：NormalMode.ControllerOver@3BEA60在3BEAA5调用的是`StartCoroutine(string)`@153D490，返回caller为3BEAAA，绕过已有IEnumerator重载@153D380。Cutout factory@3BED10返回真实`<Cutout>d__112`，MoveNext@3C5900经state0→1→-1，在最后一段请求Destroy控制器GameObject。Fade factory@3BED80、MoveNext@3C5A40使用state0→1→1…→-1，首帧固定0.2秒时长，因此不能凭从未观察过yield的false判自然结束。

[helper](../../../native/RizToolbox.JudgeContext/CoroutineContext.cpp)追加Factory kind2：exact instance ref0的native original直接执行一次，保留完整引用/原始caller，finally与activity覆盖观察和context清理。旧Start kind0、Move kind1、frame/config/stats布局及slot上限保持不变；Factory也是32个不可重配slot。[bridge/broker](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeCoroutine.cs)追加sealed ABI并拒绝普通Bind/错误ABI升级，仍使用Prepare→Configure→Commit。

[resolver](../../../src/RizToolbox.Runtime/NativeRuntime.CoroutineHooks.cs)独立验证公开String Start、IEnumerator Start/Auto和无参IEnumerator factory。String Start的reference参数是实际methodName，不能把它当enum；factory输出才是后续Move的真实IEnumerator。

[terminal tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineTracker.cs)/[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineBackend.cs)预留32个owner、每局16条factory记录、8层named context及32个enum/Coroutine root槽，激活前绑定6个物理入口。实际named Cutout同时要求同controller、3BEAAA caller和真实result capture claim窗口；factory返回后独立root/发布exact nested enum及`<>4__this`，然后才能观察同步首次Move。

Fade可通过命名或IEnumerator Start关联；直接观察到的factory也持有实际enum。当前记录只有沿已观察state路径正常false、稳定enum/controller和完整native退出，才计自然结束。worker只看纯托管identity/原子fault；original异常、缺after、错误state/ref/角色、未知root保留责任。

首帧事件中另一个factory会独立记账；外来named子调用用预留mask暂时遮蔽外层factory关联，不读取外来methodName，也不让子factory满足父Start。`TryConfirmObservedEnded`只查询现有记录：后续新factory出现会重新等待，不撤销未来producer或签发全run结束。当前16条记录是**每局累计factory尝试上限**，自然结束后尚不回收；第17次在新root前拒绝并保留unknown。活动观察根尚无可调用的全退休free路径，只有未激活且从未创建记录时可清理unused观察者。

## 生产组合

[coordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)在setter后activation前Capture terminal owner，经[reservation](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchReservation.cs)把同launch tracker附到预留result capture，再安装initial tracker和menu。host unknown查询包含terminal故障。

[result capture](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalResultCapture.cs)在实际registry result claim后、native ControllerOver前进入once-only窗口；原函数必须成功发布实际named Cutout/factory/Start后窗口才正常完成，随后才核验/缓存actual result。生产native backend没有同registry/session tracker时拒绝调用；缺Cutout或原函数失败保留claim和根，不重试、不回退stock。controlled backend的接缝不能给production授予IsFor权限。

## 验证与冻结

[真实Factory ABI](../../../tests/RizToolbox.Runtime.Tests/NativeCoroutineAbiRegression.cs)使用真实Dobby、生产helper/bridge/broker、C++original/native catch/CreateThread与汇编返回label，保留full ref0/null/caller，证实named Start→Factory after发布实际enum→first Move的顺序，并验证SEH/C++异常不执行成功after、native worker/逻辑detach时inFlight仍1。fixture模拟native调用链，不执行Unity按名称反射，也未验证游戏String或新Adapter backend。

[13组tracker](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalTerminalCoroutineRegression.cs)、[2组result组合](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalResultCaptureRegression.cs)和[Factory Hook](../../../tests/RizToolbox.Hooks.Tests/CoroutineObservationTests.cs)覆盖上述配对/隔离、原函数异常、worker、状态、容量和未知责任。[10范围/957指令](terminal-evidence.json)、[metadata](terminal-metadata.txt)、[6个直接call候选](direct-call-candidates.json)经[磁盘再核对](terminal-evidence-check.json)匹配三输入hash与全部字节；候选只覆盖经符号边界解码确认的E8，不是完整xref或Fade未使用证明。

[完整日志](build.log)/[摘要](build-summary.json)为session73370 exit0；完整增量编译诊断0条，不等于干净重建零warning，前置[Audio](audio-targeted-first.log)仍保留15条既有测试warning。native fixture31261/61358、初始化helper1747及native-only probe18单独记账，不加入1360。本批root自查，无独立子代理/第二runner。

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | 408项，4,177,572原始字节 | `4577628c65d73018e727d29388016cccc9041d27bdbce9e2f38f47492c1ad0b2` |
| [源码manifest](source-manifest.json) | 408项 | `75e0c4bd8581fc1160d6296b116af97badb47cb9b11bdd75811dc8071372d5ed` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | 18项，3,118,056原始字节 | `74715647e4c9cacb3a555720fe5b2631f13e20cd46722d75e8b4f1e4ed81a1a2` |
| [完整日志](build.log) | exit0 | `9ae386600fef16bf687b74435be0f97655def968024270e144ae97a9137dc2a2` |

[差分](changes-from-1343.diff)/[清单](changes-from-1343.json)为3新增、17修改、0删除；两源ZIP各自核对。[暂存](staging-check.json)/[源码](source-integrity-check.json)、[旧1021安装](previous-installation-check.json)18项及旧326归档、[原游戏三文件](original-game-check.json)均匹配。

复现完整离线构建：

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/terminal-coroutines-reproduce-20261023'
```

[scope](scope.md)明确限制，[review](review.md)记录自查，[下一步](next-integration.md)继续active退休和完整命令。仅B03/B18a勾选；用户“先继续代码，实测稍后”保持，无安装/游戏输入/进程终止/原文件修改/commit/push。
