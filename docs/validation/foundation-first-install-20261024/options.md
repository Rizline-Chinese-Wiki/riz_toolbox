# Windows 首次写入屏障：路线选择

2026-10-24，只读研究。未改源码、依赖、安装包或游戏，未构建、未启动进程、未暂停线程。结论：**选择外置 Windows debug-event 发布器；不把 loader lock + 线程枚举/Suspend 作为本框架的首次安装凭据。** 前者有官方全进程停顿合同；后者仍依赖未证明的线程启动路径与暂停点假设。本批 admission gate 继续只服务已安全安装的父入口。

## Loader lock 路线能证明什么

Microsoft [CreateThread 文档 Remarks](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createthread#remarks)说明普通进程启动、DLL 初始化与线程创建/终止的序列化约束；[DllMain 文档](https://learn.microsoft.com/en-us/windows/win32/dlls/dllmain)说明 DLL 入口调用的序列化及 loader lock。它们没有规定“任意时刻持有 `LdrLockLoaderLock` 就阻止一切新线程执行任意目标指令”。本次未找到 Microsoft 对这一扩展结论的官方合同。GetThreadContext 只有停止的线程才可取得有效上下文；对当前线程调用即使成功，返回的 context 也无效。[官方 GetThreadContext](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getthreadcontext#remarks)

辅助实现可解释正常路径，但不能补成 Windows 保证：ReactOS commit `b44f5ceb7e4c4e50f2557a1afe1a2e9d1ccf3a2d` 的 [ldrinit.c:509–524](https://github.com/reactos/reactos/blob/b44f5ceb7e4c4e50f2557a1afe1a2e9d1ccf3a2d/dll/ntdll/ldr/ldrinit.c#L509) 在线程初始化时取得 loader critical section，:645 释放；[ldrapi.c:174](https://github.com/reactos/reactos/blob/b44f5ceb7e4c4e50f2557a1afe1a2e9d1ccf3a2d/dll/ntdll/ldr/ldrapi.c#L174) 实现 LdrLockLoaderLock。这仅是该开源实现的代码，不是支持的 Windows 构建证据。

phnt commit `53fbbdc5b5d2b08761db1c7b26bfa8c820924356` 的 [ntpsapi.h:3617–3623](https://github.com/winsiderss/phnt/blob/53fbbdc5b5d2b08761db1c7b26bfa8c820924356/ntpsapi.h#L3617) 分别列出 `SKIP_THREAD_ATTACH=0x2`、`LOADER_WORKER=0x10`、`SKIP_LOADER_INIT=0x20` 和 `BYPASS_PROCESS_FREEZE=0x40`。这些是辅助 Native API 定义，不是 Microsoft 稳定保证。**跳过 DLL_THREAD_ATTACH 通知不等于已证明跳过整个 loader 初始化/锁**；也不能反向把所有 NtCreateThreadEx 路径视为普通 CreateThread 通知路径。本次未逆向当前 ntdll/kernel 去证明这些 Native flags 在本机的完整行为，更没有证据证明游戏、CoreCLR、Steam 与所有原生插件永远不使用它们。仅凭 flag 名也不能推断其对 Windows debug-event 停顿的效果。

因此候选序列“预载全部依赖→取 loader lock→snapshot/Suspend→检查 RIP→写入”至少还有下列具体缺口：

| 缺口 | 影响 |
|---|---|
| 新线程不受已证明的统一入口合同约束 | snapshot 之后，尚未被暂停的线程或进程外调用方可创建新线程；只有证明该新线程在任何 target 指令前必等同一锁，才可补齐枚举窗口。普通 DLL 初始化文档和 ReactOS 代码均不足以证明全部 Native 路径。|
| RIP 在范围外，旧 frame 仍在栈上 | 线程可能正执行 target 调出的另一个函数，返回后继续旧 target。首次父入口安装后该旧调用不在 admission Active 账内；随后 Active=0 会遗漏它。还可能有返回地址位于将被覆盖的 stolen instructions，或无法解释的栈切换/异常恢复上下文。|
| 暂停线程持有别的锁 | 已预载 DLL 不代表 Dobby、CRT、堆、诊断或异常路径不会获取被暂停线程持有的锁。loader lock 内再调用它们可死锁；必须证明停顿期间的全部调用无此依赖，不能只列预载清单。[Microsoft 明确不建议用 SuspendThread 做同步](https://learn.microsoft.com/en-us/windows/win32/procthread/suspending-thread-execution)。|
| 失败恢复与当前线程 | 每个成功 Suspend 必须只撤销自己增加的那一次计数；任何 OpenThread/context/恢复失败都不能产生“全停顿”凭据。安装线程自己的栈也须在目标以外；排除当前 TID 不等于完成该检查。|

在一个封闭、固定 OS、严格禁止绕过线程启动且完整审计原生调用路径的进程里，这条路线可能工程化；当前兼容底层不能保证所有原生参与者满足这些前提，因此不选它。

## 选择 debug event 的依据与边界

Microsoft [Debugging Events](https://learn.microsoft.com/en-us/windows/win32/debug/debugging-events) 明确规定：向 debugger 报告事件时，受影响进程的所有线程停止，直到 ContinueDebugEvent；普通 CREATE_THREAD_DEBUG_EVENT 在新线程开始 user-mode 执行前报告。因此保持一个真实 pending event，可以消除用户线程继续创建/进入目标的枚举竞态。这不是反复枚举 SuspendThread 得出的结论。

由独立原生 launcher 使用 [DEBUG_ONLY_THIS_PROCESS](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags) 从创建时跟踪实际游戏 PID，维护完整 create/exit thread 事件表；不需要 DEBUG_PROCESS 去控制 Steam 或无关子进程。发生 Steam 重启导致 PID/映像不再属于调试会话、访问失败或已被其他 debugger 占用时，拒绝发布。范围是 Windows 正常调试合同，不抵抗另一个持有写进程权限的外部修改者、内核组件或未核实的 Native 特殊调试绕过。

**全停顿不是“没有旧调用”的证明。** 首次父入口安装仍须检查所有当前 RIP 与可验证的完整活动栈，排除目标方法、将覆盖的 stolen range、已有 trampoline/relay 和旧 target 调用的返回路径；无法展开、动态栈元数据不可用或任一线程不可读时拒绝。成功后才可依赖新父入口的 admission 账。若改在真正首次执行前发布，也须用具体 OS/loader 阶段证明，而不是把任意 LOAD_DLL 通知直接等同于已审计的 pre-TLS/pre-DllMain 保证。

## 已配置父入口后发布五字节 mid：可直接实现的协议

这是当前最小可落地路径。前提是父入口已经通过独立首次安装协议安全发布；本节不倒推该前提已满足。

1. **运行阶段准备。** 目标进程先加载并保持 helper/Dobby；为固定模块实例准备 near relay，配置真实 ReturnPc 的 mid callback，完成原字节与地址校验。native owner Close admission，得到绑定 slot、OS 线程、全局 epoch 的 Closed receipt；owner Verify 后，在专用 native bootstrap 地址触发一个双方约定的 debug break。该点必须位于目标/relay/helper 之外。
2. **持有实际事件。** 外置 debugger 收到匹配 PID、TID、break PC 和 descriptor generation 的事件，保持 pending，不调用 ContinueDebugEvent。确认映像基址/大小/文件身份、原五字节 NOP、relay 的 RX 状态、完整 helper 指针、rel32 距离、receipt 与只读发布 descriptor；校验当前上下文和所有不明执行状态。receipt 的 owner 也已停止，其他普通 OS 线程不能合法释放同一 receipt；若内部状态不能由协议确认则拒绝，不能在停顿期间远程调用 Verify。
3. **只做外部发布。** debugger 自己调用 ReadProcessMemory / VirtualProtectEx / WriteProcessMemory，严格核五字节写入数量，读回字节与周边区域，恢复原页面保护，FlushInstructionCache。所有操作都针对实际进程句柄；不让目标运行 LoadLibrary、DobbyCommit、CLR 回调或日志代码。[官方 debugger 主循环](https://learn.microsoft.com/en-us/windows/win32/debug/writing-the-debugger-s-main-loop)支持停顿期间读写目标；[WriteProcessMemory](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-writeprocessmemory)、[VirtualProtectEx](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualprotectex) 与 [FlushInstructionCache](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-flushinstructioncache) 分别定义了写权限、页范围和指令缓存要求。页跨界必须逐页保存与恢复保护，不能只保存第一页的一个 DWORD。
4. **先确认再恢复。** 外部 publisher 写入不可回退的结果记录，随后才 ContinueDebugEvent。native owner 恢复后再次核绑定结果、完整目标/relay，再决定启用 mid 观察并正常释放 gate。发布完全未开始的拒绝可以恢复原样；写入、保护或 flush 结果不明时不继续新调用，保持责任并报告，需要明确修复或结束该次子进程。不能凭超时自动恢复正在执行可能半写指令的游戏。

事件身份不是 `DOORSTOP_INITIALIZED` 环境变量或一个 JSON 文件。descriptor 必须是内部 native 协议，绑定进程创建身份、映像实例、实际地址、原/新字节、长度、prepared generation、receipt epoch、状态与一次消费；普通 SDK 不得构造。Debugger 的事件线程应负责最终 [ContinueDebugEvent](https://learn.microsoft.com/en-us/windows/win32/api/debugapi/nf-debugapi-continuedebugevent)，不能把任意进程消息当 pending event 凭据。

## 真正首次父入口写入还差的具体组件

目前 [ExactHookBroker.cs:39–55](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.cs#L39) 只暴露 DobbyPrepare→original trampoline 与 DobbyCommit；[NativeVoid 安装 :67–71](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeVoid.cs#L67) 立即 Prepare/Configure/Commit。**当前接口不能把 Commit 所需完整已准备 patch 字节导出给外置 publisher。** 因而不能把上节的 mid 五字节方案直接叫作首次 parent 已解决。

选择同一个 debug-event 发布器作为首次 parent 的唯一写入后端，增加一个可核查的 native prepared-patch descriptor：准备阶段必须明确 stolen length、逐条重定位后的原 trampoline、原目标字节、完整 replacement 字节和目的地址，配置好 helper，且尚未写 target；在已持有事件的停顿期间，外部 publisher 校验全部上下文/活动栈后只提交这些字节。不能猜 Dobby 的内部结构、借另一个临时目标生成“看起来一样”的 patch，或擅自换成 14 字节跳转却不证明 prepared trampoline 覆盖同一范围。可维护固定 Dobby 分支提供这份内部导出，或实现并审计固定目标所需的有限 relocation；二者都需额外工程。

若要等同“框架全部首 patch 安全”，同一后端还必须覆盖当前 RuntimeBridge 对 `il2cpp_runtime_invoke` 的首次写入；不能只修 UnityEvent 父入口而继续从后台线程无凭据安装更早的 hook。debug event 与 admission 共同构成发布前提，不能互相替代。

## 实施顺序与生产成本

先实现供真实 loader 使用的外置原生 launcher/publisher、native descriptor/握手和明确失败状态；首个交付是**已配置父入口后的 mid 五字节发布**，验证 existing worker、新线程创建、原生异常、gate timeout 与发布不明的保留路径。随后增加 prepared parent 字节导出与所有线程活动栈的拒绝策略，再把 RuntimeBridge/首父入口接到同一后端。验证应直接围绕这些组件，复用已有 native fixture，不再增加只证明零计数的假屏障。

成本包括 Windows/x64 专用启动入口、Steam 正常启动与重新拉起的 PID 归属、debugger 独占、完整事件转发（除自己约定的 break 外不得吞原异常）、符号/栈展开支持、进程句柄生命周期、helper/relay 在调试停止期间已准备、发布后确认及失败时的进程处理。持有事件时目标进程无法替 publisher 做任何工作，因此预加载/准备必须先完成。整个方案未在游戏执行；当前没有可用的生产首次安装凭据，不能将本文件或本批 gate 测试计为该目标完成。
