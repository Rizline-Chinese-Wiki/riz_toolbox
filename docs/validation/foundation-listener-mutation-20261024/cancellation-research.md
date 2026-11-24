# 协程取消接线：三个优先项

2026-10-24。只读现有 backend/tracker、broker、runtime-invoke 和本地 IL2CPP dump；未改生产/测试、构建、映射或执行游戏。结论是：先建立实际 Start 输出与 IEnumerator 的同 owner 身份表，再增加 typed Stop 请求观察；取消完成必须另有引擎侧证据，不能把请求返回、Dispose 或短时间没有 MoveNext 当作完成。

## 1. 保存真实 Coroutine 句柄，增加精确请求观察

现有 [Terminal tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineTracker.cs)、[Cutout tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineTracker.cs) 已保存 Enumerator、slot/generation，分别关联实际 controller 或 EventTrigger/source dispatch；[Initialization tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalInitializationCoroutineTracker.cs) 在每 owner 的两个一次性角色记录中保存 Enumerator 与 getter 调用来源。三个 `CompleteStart` 都将真实返回值交给 backend `RetainCoroutine`，但 tracker record 没有保存可用于 Stop(Coroutine) 匹配的 handle scalar。先在成功保根后记录 `receiver + enumerator + coroutineHandle + owner + slot/generation`；Initialization 可使用其现有 owner 身份和一次性 role，不凭空引入可复用 generation。

固定 dump 给出的候选如下，均属于 `UnityEngine.CoreModule.dll / UnityEngine.MonoBehaviour`。这些是接下来运行时解析和签名验证的输入，不是已安装的观察点。

| 声明 | RVA | Windows x64 原生观察 ABI |
| --- | --- | --- |
| `void StopCoroutine(System.Collections.IEnumerator)` | `0x153D900` | 实例 `void(self, routine, MethodInfo*)`，RCX/RDX/R8 |
| `void StopCoroutine(UnityEngine.Coroutine)` | `0x153D7F0` | 同上，RDX 是实际已保存的 Coroutine 对象 |
| `void StopCoroutine(System.String)` | `0x153D7A0` | 同上，RDX 是字符串引用，不能与 Enumerator 混用 |
| `void StopAllCoroutines()` | `0x153D6C0` | 实例 `void(self, MethodInfo*)`，RCX/RDX |
| `void StopCoroutineManaged(UnityEngine.Coroutine)` | `0x153D750` | 一引用的实例 void 候选；需实际 metadata flags 与 wrapper 核对 |
| `void StopCoroutineFromEnumeratorManaged(System.Collections.IEnumerator)` | `0x153D700` | 同上，输入类别是 IEnumerator |

resolver 应按声明 class、完整参数类型和 `System.Void` 返回查 `InspectDeclaredMethod`，拒绝 static/generic/inflated/byref、非 reference8 参数、错误 RVA/MethodInfo/declaring class，再验证执行区和固定前缀。`script.json` 的生成签名遗漏了 MonoBehaviour 实例 self，不能照该 C prototype 少传一个参数。现 [NativeRuntime.CoroutineHooks.cs](../../../src/RizToolbox.Runtime/NativeRuntime.CoroutineHooks.cs) 的 Start resolver 是可复用的核验模式；[NativeCoroutineContextBridge](../../../src/RizToolbox.Runtime/Hooks/NativeCoroutineContextBridge.cs) 仅有 Start/MoveNext/Factory，没有 void(ref1) kind。新增 typed void0/ref1 原生运输，原函数恰好一次、原 SEH/C++ 留在 native，observer fault 只能使责任 Unknown。不得把 Start 的指针返回 ABI 强行用作 Stop 的 void ABI；新入口的首 patch 也必须有独立安全发布合同，旧九写 V2 不授权额外 Stop 入口热补丁。

匹配只读 owner 当前已公布的实际记录，不借祖先、不按裸地址跨 owner/代次猜测。Stop(IEnumerator)/Stop(Coroutine) 以 receiver 与输入类别精确匹配；Stop(string) 还需先保字符串根，再解码并关联实际 named Start→factory 记录，不能只按 `Cutout` 文本取最后一个 enum。StopAll 在 Enter 捕获同 receiver 的有限 record 集合和当时序列；调用期间新 Start 必须另记、使“本次请求覆盖全部”的判断失败。public wrapper 与 internal wrapper 可能嵌套，只有证实同一实际调用后才合并为一次请求；直接进入 internal wrapper 也不得漏记。worker 路径只更新已知 scalar/Unknown，不能在 worker 上读 metadata 或分配游戏根。

## 2. 请求和完成分别建账，先允许 Pending

建议每个请求记录不可复用序列、准确 owner/record 身份、输入类别、实际 caller/thread、Enter/Returned/Abnormal；初版状态只到 `Requested` 或 `RequestReturned`，另外保留 `CompletionUnproved=true`。Stop 正常返回不设置现有 `Terminal`/NaturallyEnded，也不增加 required Cutout 的自然完成信用。Stop 原异常只记录请求不确定并保留已有根。

现有自然完成依据可以保留：[Terminal CompleteMove](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineTracker.cs) 核实际 enum/controller、配对 MoveNext 的 raw false、已知状态到 `-1`；Cutout 和 Initialization 对各自状态/闭包也有专门检查。`TryConfirmObservedEnded`/`TryConfirmEnded` 与自然结束回收只适用于这些已有证据。取消不一定再产生 false，不能伪造一次 false 或靠轮询 `<>1__state==-1` 填上。

五类 iterator——`<Cutout>d__112`、`<FadeOutScoreDisplay>d__113`、`<Delay>d__4`、`<WaitUntil>d__8`、`<StartEventsEnumerator>d__16`——在 dump 中都共用 `System.IDisposable.Dispose` RVA `0x36D800`；本轮未核其完整函数体，更没有证明它撤销 engine scheduler。不得按类分别挂同址，也不得把 Dispose 返回或 `Coroutine.ReleaseCoroutine` 包装器释放当取消完成。若要观察共享 Dispose，先证明实际 ABI/alias，再用真实 self 对现有保根 enum/generation 分流，只记 disposal 事实。

完成的下一道门是固定 UnityPlayer 的调度登记撤销/取消清理：从上述 Stop wrapper 的实际 icall 目标追到 scheduler 的移除边界，证明同一 MonoBehaviour/Coroutine/enum 对应登记已撤销、其 native/MoveNext/回调均 drain，并明确同步或异步完成语义。当前仓库没有这张 engine 记录映射或完成 receipt；在得到它以前，实施请求观察并保 Pending 是可行的第一版，不能签 `CancellationCompleted`。

## 3. 封闭未来生产者，再接退休；不依赖 runtime-invoke 兜底

现 [RuntimeBridge.cs:35](../../../src/RizToolbox.Runtime/RuntimeBridge.cs#L35) 只处理 native runtime-invoke 的 After，绑定 tick MethodInfo 后在第 41 行过滤其他方法，并用重入锁抑制嵌套 tick；[NativeRuntimeInvokeBridge](../../../src/RizToolbox.Runtime/Hooks/NativeRuntimeInvokeBridge.cs) 也只提供单个永久 After owner。它只观察真正经过 `il2cpp_runtime_invoke` 的调用，无法覆盖生成代码直接 CALL、引擎 icall 内部调度或所有 coroutine MoveNext。即使在现 owner 下增加只读分发，也只会增加某些 runtime-invoke 路径的旁证，没有 Stop Enter 快照、全路径取消或不再执行证明；不要再 Attach 第二个 owner，更不能借 after 的方法名判已取消。

未来取消模型应由同一个 source/run owner 同时掌握新 Start/factory、named source、Delay/provider 转交与 listener 派生工作的进入权。先撤销该 generation 的未来进入资格，观察真实请求与 engine 完成，再前后核 native 在途、active contexts、排队/迟到 callback，最后才允许复用 slot 或释放 enum/handle 根。Terminal/Cutout 现有回收的“撤 Published→redrain→逐根 once-free→确认后复用”可复用其顺序；不能把自然完成 predicate 改成 StopReturned。

最小验证顺序：自有 native void0/ref1 fixture 验真实参数/原调用一次/SEH/C++/worker；受控 owner 测两个同址不同代次、同 handle 不同 receiver、Start 内同步首 MoveNext、StopAll 内新 Start、嵌套 wrapper 去重、停止后迟到 MoveNext；之后单独验固定游戏的真实 metadata/安全首发及 scheduler 取消完成。在途非零、已取消旧代次再进入、取消对象/引擎登记不能唯一关联或根释放故障时保 Unknown/责任，不以测试数或静默若干帧改成全部 future ended。

## 本次证据范围

本地 `C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/dump.cs` 的 bounded 读取范围：93262–93272、93742–93754、93817–93828、95594–95622、112427–112438、112644–112664。dump SHA256 `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb`；`script.json` 仅按上述 Stop/SetupCoroutine 名称输出少量项，SHA256 `a51cc6ee5881110fbeb884679b13d20bce4ac4df9055bf535f3cdc476bf35d10`。本轮没有执行游戏、引擎 icall 或新增二进制动态验证；以上 RVA 只适用于已固定的本机版本。

证据链：实际 tracker/backend 源码及 bounded metadata → 确认已有 Enumerator/自然结束归属、缺 handle scalar/取消运输与完成 receipt → 先实现请求身份与观察，再单独补 engine 完成和 future 封口；每一步保留未证明边界。
