# 协程启动与推进的原生观察边界

2026-10-23。承接[1315菜单交接批](../foundation-menu-handoff-20261023/README.md)，**1324/1324、25套完整离线检查通过，冻结暂存未安装**。Runtime138、Hooks125、AudioContent450；新增8组Hook检查和1组真实原生ABI检查。当前安装仍为1021保护宿主。

这批新增内部生产 transport 和精确 Start resolver。**Adapter 尚未调用这套协程入口；实际初始化协程追踪器、锁回调归属与活动局退休尚待实现。** 下一步见[具体接线要求](next-integration.md)，未扩大B01–B19勾选。

## 实现

[native helper](../../../native/RizToolbox.JudgeContext/CoroutineContext.cpp)分别提供32个不可重配的 Start/MoveNext 入口。Start 保留完整引用返回值，MoveNext 保留原始AL字节；original只在native执行一次，finally覆盖完整观察回调与清理。48字节frame含实际self、IEnumerator、MethodInfo及原始返回地址，不能由观察者跳过original或替换结果。

[bridge](../../../src/RizToolbox.Runtime/Hooks/NativeCoroutineContextBridge.cs)核验frame/config/stats布局，保持DLL/回调与已用slot的进程责任。[broker](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeCoroutine.cs)沿Prepare→Configure→Commit发布，要求首个patch就使用native transport，同地址共享已验证的StartCoroutine_Auto别名；MoveNext可共享既有普通Boolean观察。原生在途计数包括worker、逻辑解绑后的original与context清理；捕获/after/清理故障和无效统计保持unknown，不允许归零后冒充安全。

worker和外来MethodInfo只通知标量fault回调并转发original，不在worker读取原生对象。fault处理方仍须按实际receiver/enum的纯托管身份决定是否影响自己；普通无关调用不自动污染所有run。物理重入仍捕获独立协程身份，普通逻辑观察保留原重入抑制规则。

[精确resolver](../../../src/RizToolbox.Runtime/NativeRuntime.CoroutineHooks.cs)只接受该构建的公开MonoBehaviour.StartCoroutine(IEnumerator)及独立验证的StartCoroutine_Auto，RVA均为153D380；检查声明类、参数/返回引用、非静态/非泛型、MethodInfo和可执行地址。私有StartCoroutineManaged2@153D2D0不是这条调用链的Hook点。

## 验证与静态发现

[8组Hook检查](../../../tests/RizToolbox.Hooks.Tests/CoroutineObservationTests.cs)覆盖别名/首patch准入、发布顺序、异常、同入口共享、物理重入、worker/foreign metadata、调用中解绑和unknown不重试。[真实ABI检查](../../../tests/RizToolbox.Runtime.Tests/NativeCoroutineAbiRegression.cs)使用真实Dobby、生产broker/bridge/helper、C++original/native catch/CreateThread和带unwind信息的汇编caller：

- Start经过tail-jump relay后，frame.Caller仍精确等于预先导出的汇编返回label；MoveNext同样精确比较，非仅检查地址位于DLL。
- 原样保留self、enum、primary/Auto/null MethodInfo、64位引用与AL=00/01/80/FF。
- Start尚未返回时同步首次MoveNext仍可观察；两种ABI的原生SEH/C++异常kind/payload保留，abnormal不执行成功after，context在真实在途内释放。
- 阻塞原生worker与逻辑detach期间inFlight仍为1，真正退出后归零。

[9个范围/1574指令](coroutine-disassembly.txt)、[metadata](coroutine-metadata.txt)、[证据](coroutine-evidence.json)由[有界离线脚本](inspect-coroutines.py)生成，[再次核对](coroutine-evidence-check.json)覆盖三输入hash、全部范围/逐指令字节及两项常量：

- getter的SimpleDelay调用返回点为3BEFDA，延迟常量原始位40466666，即约3.1秒；场景转场的3.0秒是另一项常量。
- getter的SimpleWaitUntil调用返回点为3BF0B2；两条helper均尾跳公开153D380入口，所以可在启动前取得实际controller/enum和物理来源。
- WaitUntil的todo使用共享Release(false)动作。不能凭3.1秒延迟结束推断该动作已经执行。
- MoveNext在执行body期间也会将state设为-1；只读-1不证明完成。后续追踪必须同时拥有正确enum、正常返回false与完整native窗口drain。

这些是静态调用链与离线transport证据，尚未确认游戏内实际controller/enum/回调，也不签发全部run消费者结束。

## 构建与归档

[完整日志](build.log)/[摘要](build-summary.json)为session67464 exit0，含15条既有测试warning（14条FakeNative CS0649、1条结果sink CS8602），无编译错误。native helper旧fixture31261/61358、初始化helper1747与native-only probe18单独记录，不加入1324。

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | 402项，4,052,877原始字节 | `592d05b939c4a24c1c732f45b981ce29264970dec7d12894e37789150db72eb9` |
| [源码manifest](source-manifest.json) | 402项 | `d375324f59c42013a5cbec1af746753e8880d7370f25731aafdb4590da344b0f` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | 18项，3,058,664原始字节 | `79ad52ae632e668cc9a90989aad87bd4df1b6e809fc99a8caf812ea6f3f55a4b` |
| [完整日志](build.log) | exit0 | `d4f5d4985b51d6d00876a37f18c2455c540d77fee6df16502de97146e3efec85` |

[差分](changes-from-1315.diff)/[清单](changes-from-1315.json)为7新增、9修改、0删除；历史与本批ZIP各自验证。[暂存](staging-check.json)、[工作树输入](source-integrity-check.json)、[旧1021安装](previous-installation-check.json)18项及旧326源归档、[原游戏三文件](original-game-check.json)均匹配。原安装未更新，无游戏进程/UI操作；遵循用户“先继续代码，实测稍后”。[自查](review.md)与[范围](scope.md)记录限制。

复现本批完整离线构建：

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/coroutine-boundaries-reproduce-20261023'
```

本批没有独立子代理或第二runner审计，不能将一次完整构建称为B19全部发布矩阵完成。
