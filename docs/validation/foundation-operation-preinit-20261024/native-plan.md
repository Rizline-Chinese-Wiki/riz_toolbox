# Operation 生产预初始化发布合同

2026-10-24。决定是把18个mutation/Stop入口纳入同一次first-il2cpp_init前发布；原九写加18个Dobby Commit共27写。CLR只采用V3真实凭据并晚Attach，不在LOCAL Begin时首次改写这些源函数。该合同已按root授权落盘native实现，尚未由本agent构建或执行；任何fixture结果须引用root真实原件，不由设计推导通过。

初次审查实际生产风险路径是`NativeLocalUnityEventBackend.Mutation.cs:20`和`NativeCoroutineCancellationObserver.cs:78`在LOCAL Capture时调用默认`BindNativeOperationObservation`，原`ExactHookBroker.NativeOperation.cs:57–61`直接Prepare→Configure→Commit。此时IL2CPP/Unity可能已有调用线程；解决前保持RequestPlay关闭。代码引用是本批改动前实际读到的行，不能当作改动后仍未修复的断言。

## native 文件与 ABI

本agent独占修改[FirstInstall.h](../../../native/RizToolbox.JudgeContext/FirstInstall.h)、[FirstInstall.cpp](../../../native/RizToolbox.JudgeContext/FirstInstall.cpp)、[FirstInstallProfile.h](../../../native/RizToolbox.JudgeContext/FirstInstallProfile.h)、[OperationContext.h](../../../native/RizToolbox.JudgeContext/OperationContext.h)、[OperationContext.cpp](../../../native/RizToolbox.JudgeContext/OperationContext.cpp)。root负责exports/Build/Doorstop/fixtures，runtime代理负责只读V3采用/bridge/broker前门，不存在普通安装fallback。旧Operation Frame64/Config40/Stats48及五exports兼容原fixture。

新增三个exports：

- `RizOperationBootstrap_Configure(kind,slot,Config24*)`：header8+Target/Original；只永久claim，不patch。0成功、1非法、2已claim、3epoch耗尽stickyUnknown。
- `RizOperationBootstrap_Attach(kind,slot,epoch,Observer32*)`：header8+Enter/Exit/User；0成功、1非法、2未就绪、3epoch错、4已attempt、5Unknown。一次immutable descriptor发布，不重配Original。
- `RizOperationBootstrap_GetInfo(kind,slot,Info96*)`：u32 Version/Size/Kind/Slot/Attached/Unknown（0–23），Target24/Original32/Entry40，u64Epoch48/Entered56/Exited64/InFlight72/EnterFaults80/ExitFaults88。0bootstrapready、1非法、2真正empty、3旧V1或incomplete claim。incomplete返回Unknown1使allocator跳过，不借用半配置slot。

Entry第一步原子读取一次nullable observer到栈上，之后才构造Frame/sequence；整个原函数及finally Exit使用同一snapshot。旧null调用跨Attach仍无新Enter/Exit，合法token0仍配Exit。callback SEH/C++包含并sticky nativeUnknown，原函数SEH/C++照原生继续传播，finally计数排空；Unknown不阻止既有cleanup。无锁跨CLR，无callback时native执行原typed函数一次，不引入额外TLS或回调分配。3kind×32槽共享单调正signed64 epoch，耗尽永久Unknown不复用；Frame sequence原有耗尽0/继续original合同不改。

新增`RizFirstInstall_GetInfoV3`：`RizFirstInstallInfoV3`1600bytes。V2 prefix416（其Common header3/1600）；offset416四u32 OperationCapabilityVersion1/Count18/ExpectedWrites27/Reserved0，ConfiguredMask432/PublishedMask440，18×64 row从448开始。row偏移0/4/8/12为Id/Kind/Slot/Reserved，Target16/Original24/Entry32/Epoch40，Rva48/BodySize52/UnwindRva56/Protection60。Ready两mask均0x3ffff。V1 144、V2 416保持原export/layout/header可读，真实writesStarted为27；旧layout不能授权18项采用。

## 固定表

id0–11与UnityEventOperation当前声明顺序相同，12–17与CoroutineStopOperation顺序相同。固定SHA853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79；全部完整RF原body、最少32byte源窗口及完整unwind在profile中由真实文件生成，最大592bytes。id2完整RF仅31bytes，32源窗口包含一个相邻CC，receipt BodySize仍31。

| id | 方法 | RVA | kind/slot |
|---|---|---|---|
| 0 | UnityEvent.AddListener(UnityAction) | 15640A0 | 1/31 |
| 1 | UnityEventBase.AddCall(BaseInvokableCall) | 15630E0 | 1/30 |
| 2 | UnityEventBase.RemoveListener(object,Reflection.MethodInfo) | 1563C50 | 2/31 |
| 3 | UnityEventBase.RemoveAllListeners() | 1563B80 | 0/31 |
| 4 | UnityEventBase.DirtyPersistentCalls() | 15631A0 | 0/30 |
| 5 | UnityEventBase.RebuildPersistentCallsIfNeeded() | 1563B40 | 0/29 |
| 6 | InvokableCallList.AddListener(BaseInvokableCall) | 154DED0 | 1/29 |
| 7 | InvokableCallList.AddPersistentInvokableCall(BaseInvokableCall) | 154DF90 | 1/28 |
| 8 | InvokableCallList.RemoveListener(object,Reflection.MethodInfo) | 154E280 | 2/30 |
| 9 | InvokableCallList.Clear() | 154E110 | 0/28 |
| 10 | InvokableCallList.ClearPersistent() | 154E050 | 0/27 |
| 11 | PersistentCallGroup.Initialize(Calls,EventBase) | 1550EE0 | 2/29 |
| 12 | MonoBehaviour.StopCoroutine(IEnumerator) | 153D900 | 1/27 |
| 13 | MonoBehaviour.StopCoroutine(Coroutine) | 153D7F0 | 1/26 |
| 14 | MonoBehaviour.StopAllCoroutines() | 153D6C0 | 0/26 |
| 15 | MonoBehaviour.StopCoroutineManaged(Coroutine) | 153D750 | 1/25 |
| 16 | MonoBehaviour.StopCoroutineFromEnumeratorManaged(IEnumerator) | 153D700 | 1/24 |
| 17 | MonoBehaviour.StopCoroutine(string) | 153D7A0 | 1/23 |

## 发布、读取和失败责任

仅支持Doorstop在首次原il2cpp_init前调用一次producer的启动合同，不使用进程名、测试mode或线程枚举伪造安全。先固定模块/磁盘SHA/PE，18全部fullbody/pdata/unwind/OS lookup/页保护和empty native slot预检，再Prepare旧两项/生成listener chain、Prepare/Configure全部18操作；每次Prepare后重核全部未修改源，任何意外改源立即Unknown并如实开始责任。全部原trampoline/native epoch/entry配置好，才允许第1写。

写顺序保持mid、六listener、Unity parent、runtime原9项，随后按id0–17 Commit操作。BeginGameWrite先增加真实写尝试计数，再调用实际patch/Commit；异常/失败不能硬填27。每项核页保护恢复/Flush/真实bounded branch链/完整未覆盖body及邻字节，封存实际前32字节。ConfiguredMask记录真实成功claim，PublishedMask只记成功读回；失败后未验证项不伪造published。准备阶段已分配原trampoline、helper/module、slot、listener table和回调永久持根，零源写拒绝也不能称“没有准备责任”。源写后任何不确定永久Unknown，不回滚、不释放、不重装、不继续受支持original init。

V1/V2/V3每次Ready读取都重核完整九写listener身份与18操作：固定row、actual native target/original/entry/epoch/Unknown、完整封存源body、保护、原unwind/pdata和实际lookup、真实detour链。先核raw bytes再OS lookup；GetInfo最后shared snapshot锁内再读atomicUnknown，保留原atomic-before-detail竞态修复。Attached可以由0变1，配置/源身份不可变；查询不建立取消完成或全future producer封口权。

验证由root负责：DONT_RESOLVE真实固定映像仅隔离映射、绝不执行其函数；18源码/tuple/branch/protection/RF、V1/V2/V3形状/poison输出、操作篡改与旧chain/source篡改、重复无第28写、atomicUnknown各版100读，以及自有target的三kind晚Attach/旧call/worker/nested/SEH/C++/callbackfault。mappedbytes证明不等于真实冷启动/并发首发完成；仍缺生产实际metadata、GC/root和engine cancellation/完整根退休/全部inlined direct写者的证据。
