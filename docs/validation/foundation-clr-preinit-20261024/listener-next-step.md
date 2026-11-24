# 实际监听器调用的下一步

2026-10-24。这里只读核对现有实现、固定 PE 的小范围指令与已有证据；以下调用运输、mutation 和取消接线均**未实现**。本轮未修改源码、构建、安装或操作游戏。

结论：先给固定 UnityEvent 的三个实际 invokable 调用点建立原生调用归属与正常/异常配对，再把记录接到当前父调用和 executing-list ledger。保持原 callee 真正看见的源返回地址，不能把 helper 内重新 CALL 的返回地址冒充原调用点。随后另做 mutation、生产者撤销与取消/自然结束的封口；三处 call 记录本身不能签发全 run 退休凭据。

## 当前已有证据到哪里

- [NativeLocalUnityEventBackend.cs:65](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.cs#L65) 实际接入父 Invoke 与一次 inline-list 观察；[第 73 行](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.cs#L73) 另接独立 `PrepareInvoke`。它没有 listener-call observer，也没有 mutation/cancel owner。
- [NativeLocalUnityEventLedger.cs:277](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.cs#L277) 校验私有 current physical identity、真实父 generation、RSI event、RBP executing list 和固定 mid 返回地址，再独立保根并冻结列表。它只证明这一刻的列表归属。[第 387 行](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.cs#L387) 的 Read 仍返回 `Pending=true`、`MutationClosed=false`，默认 `InlineExecutionUnobserved=true`、`DispatchPathVerified=false`。
- [BootstrapVoidContext.cpp:56](../../../native/RizToolbox.JudgeContext/BootstrapVoidContext.cpp#L56) 在父 Enter 建立包含 null observer 的 TLS record，并只快照一次 observer；[MidShim:126](../../../native/RizToolbox.JudgeContext/BootstrapVoidContext.cpp#L126) 只在这个 record 的 original window 内分发。旧调用不能在中途 Attach 后借用新 observer。新 listener lane 必须沿用这个合同。
- [MidEntry.asm:1](../../../native/RizToolbox.JudgeContext/MidEntry.asm#L1) 现在处理的是原五字节 NOP 位置插入的观察 CALL，不重放原调用指令。它不能直接当作 listener invocation wrapper；需要新增确实调用原 callee 并处理配对的运输。
- [NativeRuntime.ListenerMetadata.cs:16](../../../src/RizToolbox.Runtime/NativeRuntime.ListenerMetadata.cs#L16) 只作 rooted delegate 的官方 metadata 投影；[Backend ReadLeaf:241](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.cs#L241) 对虚调用、generic/inflated、static/open、嵌套 multicast 等仍保留 opaque。投影结果不是可执行 listener capability。

以上源码链接相对仓库根目录；本文件位于 `docs/validation/<batch>/`，因此链接中的 `../../../` 指向仓库根。行号描述当前读取版本，后续实现可能移动。

## 固定的三个源调用点

固定输入 SHA256 及范围来自 [terminal-evidence.json](terminal-evidence.json)，可读上下文见 [terminal-disassembly.txt:1028](terminal-disassembly.txt#L1028)。UnityEvent 的实际 runtime function 是 `[0x15642E0,0x156454E)`，unwind `0x1DB3FB4`；证据文件线性反汇编范围末尾多出的 padding 不扩大该 runtime-function 边界。

本次 continuation bounded decode 的实际输入为 `D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll`；本轮重新只读 SHA256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。使用已有 Python `pefile.get_data` 读取下面三个 RVA 的 6/9/9 字节，并以 x64 Capstone 解码完整块。所有地址均为 RVA，非首选基址或实际 ASLR 地址；源调用的真实返回地址就是各 continuation 的开始 RVA。

| Site | 实际指令和源返回地址 | 实际入参/归属线索 |
| --- | --- | --- |
| `0x1564470` | `E8 3B A1 FE FF` → `0x154E5B0`；返回 `0x1564475` | `RCX=invokable`，`RDX=0`；`RSI=event`、`RBP=executing-list`、`EBX=index` |
| `0x15644C2` | `E8 E9 A0 FE FF` → 同一 callee；返回 `0x15644C7` | 同上；路径不同，不能仅靠相同 callee RVA 合并成同一次调用 |
| `0x156451F` | `FF 90 78 01 00 00`，`call [RAX+0x178]`；返回 **`0x1564525`** | `RCX=RDI=invokable`，`RDX=[RSI+0x28]` 实际 object[]，`R8=[RAX+0x180]` 实际 method token；RAX 是实际对象 class |

最后一条是六字节 CALL。不能写 `E8 + NOP` 然后把 `+5` 当成真实返回地址。也不能把所有 `0x154E5B0` 调用统一当作 `void0(self,MethodInfo*)`：本机 dump 的 `InvokableCall.Invoke()` 和 `Invoke(object[])` 都共享这个 RVA（dump.cs 114072–114073）；虚分支还会去其他实际 vtable target。

为规划正常返回配对，本轮仅对三个 continuation 做了 bounded PE decode，实际字节为：

| 原 continuation | 必须完整解码/重放的块 |
| --- | --- |
| `0x1564475`，6 bytes | `FF C3 8B C3 EB A5`：`inc ebx; mov eax,ebx; jmp 0x1564420` |
| `0x15644C7`，9 bytes | `FF C3 8B C3 E9 50 FF FF FF`：同上，末尾是五字节跳转 |
| `0x1564525`，9 bytes | `FF C3 8B C3 E9 F2 FE FF FF`：同上 |

第二个 direct continuation 也是 **9 bytes**，不可按第一个的六字节长度截断其 JMP。以上没有补丁写入。

## 最小保真实现顺序

1. **先写 native 六区运输 fixture。** 三个 source CALL 进入各自原生 pre-call thunk；thunk 保存实际 machine frame、校验固定 site/返回地址、从当前父 record 获取不可变 observer，并记录 child invocation。观察后恢复原始参数寄存器与必要机器状态，**尾跳**原 direct target 或原 `[RAX+0x178]` target。原 callee 收到的栈顶返回地址仍为原 `+5/+6`。不能用 C++ 再 CALL callee 后仅把日志字段填成源地址。
2. **对应三个 continuation 的正常 post 通道。** 源 continuation 转到原生 post thunk，标记同一 child 正常返回，然后按原顺序重放完整 `inc/mov/jmp`。要保存原 callee 返回后的状态，observer 的行为不能改变重放结果。每个 thunk、tail path、post path 都须有正确 Windows x64 unwind 方案；尤其 JUMP 进入 post 时仍在原 game frame，不能沿用“CALL 时栈顶是返回地址”的 MidEntry 布局。完整块解码、可达边界、pdata/unwind 与实际异常走栈证明均是**进入 producer 前的未实现门槛**。
3. **异常归父 native finally 清理。** callee 发生 SEH/C++ 时不会走正常 continuation；父 Exit 的 native finally 必须将当前尚未配对的 child 标记 abnormal 并清理 TLS/物理责任，原异常原样继续。不要补发 normal After，不要由 CLR 包住 callee，也不要以 child 计数归零推断正常完成。捕获前/回调故障同样保留原调用一次和异常原样传播。
4. **纳入真实 producer，再允许托管采用。** 扩展 [FirstInstall.cpp:442](../../../native/RizToolbox.JudgeContext/FirstInstall.cpp#L442) 的固定 profile、首写前验证、每写责任、原子 unknown/readback 及 sealed receipt。当前 Info144/三次写身份只覆盖现有 mid、parent、runtime-invoke，不包含这六区，必须版本化扩展；不能让旧 Ready authorize 新 listener lane。所有指令只在真实 pre-init 支持合同内发布，晚 Attach 只发布 observer。
5. **以实际 native child identity 接 ledger。** 增加不可由调用者构造的 parent/child token、父 event generation、site、index、invokable identity、真实目标、真实参数和 source return。只接受当前 TLS physical top 对应的父、同一 generation、实际 `RBP` list、当前 `EBX` row。child token 含独立单调序列，不能只用复用的 context 指针；foreign/worker/递归必须遮蔽父归属，worker 只可用标量报告故障。

托管 backend 在主线程对实际 call-frame 中的 invokable/args 执行先保根后读 metadata，并比对当前列表位置与版本。索引/对象/版本不一致必须记录 mutation/unknown，不能从入口时的静态列表猜当前 row。暂时不支持的对象类别保持 opaque、继续原调用，不获得执行或释放权限；不将 opaque 当作“不存在”。

## 三处调用仍不等于每个 delegate leaf

它们证明的是一次 `BaseInvokableCall/InvokableCall` 层的实际 dispatch。一次 invokable 内部可能检查 Unity target 生存状态、跳过执行，或展开多个 multicast delegate leaves；同一个 invokable 也能被别处调用。当前 graph 中列出 N 个 leaves，不意味着 N 个都实际执行。

第一版只增加 `InvokableDispatchObserved` 一类有明确范围的记录，原 `DispatchPathVerified` 不应直接改为全局 true。若需要每 leaf 的真实执行与生命周期，后续必须观察实际 delegate invoke loop/目标适配路径，并绑定实际 rooted delegate identity、method/token、target、调用线程和返回/异常。generic sharing、virtual、static/open、嵌套 multicast 仍需要各自 ABI/适配证明，不能按一个 dump 名称生成通用 hook。

## mutation、取消与全量退休的未实现门槛

当前 pre/inline/post 图相同只排除了采样瞬间可见差异；期间一次增删再恢复会逃过比较。连续 mutation owner 至少要覆盖 `InvokableCallList.AddPersistentInvokableCall 0x154DF90`、`AddListener 0x154DED0`、`RemoveListener 0x154E280`、`Clear 0x154E110`、`ClearPersistent 0x154E050`，以及 `UnityEventBase.DirtyPersistentCalls 0x15631A0`、`RebuildPersistentCallsIfNeeded 0x1563B40`、`AddCall 0x15630E0`、`RemoveListener 0x1563C50`、`RemoveAllListeners 0x1563B80`。持久化候选、delegate add/remove、反射和直接字段/列表写入的路径也要有覆盖或明确拒绝合同。上述 RVA 来自 bounded dump 行 114168–114202，只是候选，不是已经完成的 metadata/ABI/全部写者证明。

当前 terminal backend 安装了 Start/factory/MoveNext（[NativeLocalTerminalCoroutineBackend.cs:43](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineBackend.cs#L43)），没有 Stop 路径。bounded dump 中相关候选为 `StopCoroutine(IEnumerator) 0x153D900`、`StopCoroutine(Coroutine) 0x153D7F0`、`StopCoroutine(string) 0x153D7A0`、`StopAllCoroutines 0x153D6C0`，底层 `StopCoroutineManaged 0x153D750`、`StopCoroutineFromEnumeratorManaged 0x153D700`。必须将真实 stop 输入与已保根 controller/enumerator/Coroutine 的同一 slot generation 配对，并取得引擎实际取消完成/不可再 MoveNext 的证据；“调用 Stop 返回”本身不够。共享 `IDisposable.Dispose` RVA 也不能当某个 coroutine 的取消完成凭据。

[NativeLocalTerminalCoroutineTracker.cs:324](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineTracker.cs#L324) 当前仅确认已观察记录自然结束，[第 339 行](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineTracker.cs#L339) 明确保留未来生产者。[LocalRunSessionRegistry.cs:20](../../../src/RizToolbox.Adapter/AudioContent/LocalRunSessionRegistry.cs#L20) 的全 run verifier 还要求 controller/result 对象、scene/resource/audio/global消费者、旧回调及所有未来进入权都结束；当前仅有 never-activated 特殊退休路径。即使六区运输与 mutation 已实现，只要下一次 EventTrigger/delay/cutout/provider/其他 caller 仍能生产旧 run 调用，就不能签发 `ConsumersEndedToken`，不能释放其根或开放完整 RequestPlay。

## 进入生产接线前应看到的验证

- 自有真实 native 六区 fixture 覆盖三条实际形态与返回地址、三种 continuation 长度、空表/多 row、native worker、同/异父递归、旧 Enter 后 Attach、父/child context 地址复用但 token 不复用。
- callee 内直接记录真正 `_ReturnAddress()`，分别严格等于源 `+5/+6`；不能只检查运输构造的 frame 字段。原参数高位、机器状态、原调用一次、正常 replay 次数与父/child清理逐一核验。
- SEH/C++ 原异常在 native 侧同 code/payload/实际 flags 到达外层；故障回调不改变原调用与原异常。新 ASM 每个合法 unwind 区域用实际 `RtlVirtualUnwind`/walk 与异常执行验证，尤其 post JUMP frame。
- 独立映像发布先验收六区全部精确字节、保护恢复、flush、旧 receipt 拒绝和任意阶段写后 unknown；随后真实 CLR 从真实 producer receipt 采用且 engine Prepare/Commit 为零。
- 最后才在允许游戏实测后核固定冷启动、真实 IL2CPP 根/metadata、真实 event/listener/mutation/cancel 日志。前面离线结果不能替代这些证据，更不能将几次 dispatch 记作 all future closed。
