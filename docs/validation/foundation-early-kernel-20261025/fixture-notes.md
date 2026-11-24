# 独立 Kernel 的自有早期链 profile

本批在现有 EarlyFixed fixture 增加编译期 `RIZ_EARLY_ENGINE_KERNEL=1` profile，复用四个真实固定前缀、原函数工作负载、模式和事件。省略该宏保留默认旧 profile；显式值为 0 或其他值会编译拒绝。本页记录当前实现及诊断合同，尚未声明最终 73 个宿主正式通过。root 是唯一 builder 和 child 执行者，未启动或修改实际游戏。

## 独立身份与版本

| 身份 | 默认旧 profile | 新 Kernel profile |
|---|---|---|
| Host | `RizOwnedHost.exe` | `RizKernelOwnedHost.exe` |
| Publisher | `RizFixedPublisher.dll` | `RizKernelFixedPublisher.dll` |
| Helper | `RizToolbox.JudgeContext.dll` | `RizToolbox.EngineNodeKernel.dll` |
| Proxy | `RizOwnedProxy.dll` | `RizKernelOwnedProxy.dll` |
| Recorder | `RizOwnedRecorder.dll` | `RizKernelOwnedRecorder.dll` |
| Debugger | `RizOwnedDebugger.exe` | `RizKernelOwnedDebugger.exe` |
| 自有 Targets | `RizFixedOwnedTargets.dll` | 同一受限名字，沿用 `RIZFIXEDOWNEDV1` marker |
| MMF magic / wire version | `0x58465245` / 2 | `0x4b465245` / 3 |
| MMF 环境变量 | `RIZ_EARLY_FIXED_MAPPING_NAME` | `RIZ_EARLY_KERNEL_MAPPING_NAME` |
| Publisher Config/Binding/Info/Receipt.version | 1 | 2（`RizFixedVersion`） |
| OwnedCall/PhaseReport/Snapshot.version | 2 | 3（`RizEarlyWorkloadVersion`） |
| Tuple / publisherSnapshot meta.version | wire 2 | wire 3 |
| actualMemory.version | 独立 scalar record 1 | 保持 1 |
| helper scalar record / transport ABI | 1 / 2 | 保持 1 / 2 |

[EarlyFixedProfile.h](../../../native/RizToolbox.EarlyFixedFixture/EarlyFixedProfile.h) 集中声明早期链名字与版本；canonical publisher 使用 [FixedPublisherProfile.h](../../../native/RizToolbox.FixedPublisher/FixedPublisherProfile.h) 选择同一个宏。Shared 的 static_assert 防止早期 profile 与 publisher 版本选择不一致。结构布局继续为 Shared 449120、Header 360、Event 104×4096、Tuple 216×4、publisherInfo 21640、actualMemory 240、publisherSnapshot 32 字节；没有复制核心 transport 或工作负载。

普通静态链由 root builder 链接成 `Host → Targets → Proxy → Publisher/Kernel/Recorder`，各组件的真实 import/export、全路径和哈希由父验证器检查。名称、版本和 marker 都只是身份检查的一部分；不能替代真实 tuple、helper epoch、固定前缀、页属性和发布顺序。

## 保留的实际路径

模式 0–15、14 个故障 stage×4 kind、独立未发布 baseline 与一个 debugger normal 均保留，共原合同 73 个独立宿主。没有新增伪装通过的测试模式。

- 初始化工作负载仍在 Targets 的真实 TLS callback、C initializer、C++ initializer 和 DllMain 中调用四个 typed 入口，Start 内同步 Execute/Release。
- Start/Release 原 AL、Execute 完整 uint32 EAX、Detach void 保持原合同；实际 Release DEC/CMP 的计数与 ZF、Detach 清零 EBP 的诊断没有改动。
- Proxy 沿 PrepareGroup → Configure 四永久槽 → BindGroup → 四次 Commit → 实际 Capture/核对 → Ready；slot 28、真实写入/保护/快照和失败责任保持。
- baseline mode 14 仍对同名实际 Targets 的未发布入口运行八种原生 SEH/C++，无 Prepare/Configure/Bind/Commit/Attach/Ready；父按同二进制、同系统的跨 child 稳定异常字段比对，不掩码原 flags，也不称同对象同 run。
- prior-call/late-worker 先保留真实初始化/原函数或 worker 已执行证据后拒绝。模式 15 继续制造真正 Capture 递归 gate Busy，保留永久 Unknown，不将受控递归称作自然并发竞态。
- Worker 的等待、observer Attach 和异常负载都在 loader 窗口外由原 Host 路径执行。没有增加 loader 窗口内线程等待、动态加载或托管启动。

## 调试观察的边界

新 Kernel 没有 PE entry/TLS，publisher 同样没有。新 profile 的 debugger 对这两个已加载模块只记录真实 `DebugLoad=1001`，检查映像头的 entry/TLS 为零；不为其设置入口 breakpoint，不生成 `DebugEntry`、`DebugReturn`、TLS 或 TLS return。磁盘无 entry/TLS 的独立证明仍由父 PE 检查完成，不能把调试器声明当作唯一证据。

新 debugger JSON 的 `noInitializationRequired=true` 仅适用于 publisher id1 和 Kernel id2；新 profile 的 Load/Unload/retired 通知 `processAttach=false`，真实入口/TLS 事件为 true。默认旧 profile 的 JSON 输出形式保持原合同。Shared 布局不变，但新 profile 的 debug 事件采用下面的明确身份/栈载荷，不能继续把旧 `value0=DLL_PROCESS_ATTACH` 套在新 Load 上。

父必须验证真实 DLL load 在 Proxy/Targets 加载与初始化之前，以及 publisher 准备、Kernel 配置、四次写入完成、Ready 和首次 Targets TLS 工作负载的实际顺序。没有 Kernel init-return 这一事件，因此不能继续套用旧 helper 的“init return → Proxy init”断言。调试模式会短暂改动自有 child 其他模块的初始化指令，必须与未插桩主矩阵结果分开解释。

### 映射代次与真实通知

root 的失败诊断实际观察到同一个 publisher 文件在不同基址的 Load，以及相同已卸载基址的后续 Unload 通知；仅凭这些现象不命名其 Windows 内部成因。Debugger 不再以 basename 在全进程生命周期唯一作为新 profile 的断言。每个实际 Load 保留全局 `instanceId` 和角色内 `generation`；历史总数最多 32、每角色最多 8，只有先确认无 entry/TLS 的 id1/2 允许同时存在不同基址。hFile 的 volume/fileIndex 必须与 host 目录内对应的预期文件身份相同；父另从 held 文件句柄独立取得身份。

首次关闭某个精确 live base 的通知是 `1006`；仅 id1/2 无入口、无 pending return 的映射可关闭。后续同基址通知只有在历史实例唯一、已关闭，且实际 `VirtualQueryEx` 证明那里不再是原 `MEM_COMMIT/MEM_IMAGE/allocationBase` 时才记 `1007 retiredUnloadNotification`，不制造第二次卸载或新映射。没有已知 Load 身份的系统基址 Unload 只保留独立 stderr 辅助记录，不能据此声称全 OS 生命周期闭合。终态仍要求五角色各恰好一个 live 观察实例，且与实际 header 的五个模块基址完全相同；多映射未消解就拒绝。

| 新 profile 事件 | Shared 固定载荷（偏移为 Event 起点） |
|---|---|
| `1001 Load` / `1006 Unload` | kind=角色，index=generation，result=live 1/0，flags=`0x80000000`；Q40 module=原基址，Q48 target=原基址，Q56 original=映像字节数，Q64 entry=fileIndex64，Q72 epoch=instanceId，Q80 value0=volumeSerial，Q88 value1=首次 Load sequence，Q96 caller=0 |
| `1007 retired notification` | 同一 instance/file/Load sequence；phase=query.Type，result=query.State，flags=`0x80000000 | query.Protect`；Q40=原基址，Q48=query.BaseAddress，Q56=query.RegionSize，Q64=fileIndex，Q72=instanceId，Q80=volumeSerial，Q88=Load sequence，Q96=query.AllocationBase |

JSON 的 Load/Unload/retired 行另保留 `moduleBytes/instanceId/generation/loadSequence/live/fileVolumeSerial/fileIndex/expectedFileVolumeSerial/expectedFileIndex/fileIdentityMatched`；1007 加 `query{base,allocationBase,regionSize,state,type,protect}` 和明确 retired 标记。所有通知按真实 sequence 与 Shared 一一核对，不能先丢掉重复行再运行旧断言。

### 入口与返回的线程硬件观察

新 profile 的最初 entry/TLS 仍用一次性 INT3；返回 PC 改用该线程 DR0–DR3 的四个 local execute 槽，整个 pending 表也有 64 项上限。首次控制位/地址须无外部占用，之后每次必须与本观察器拥有的槽完全一致；只对 SDM 固定的 DR7[10] 容许 OS 规范化。R/W、LEN、global enable、GD、TF 等不受控状态不被默许。旧 default 仍走原 INT3 return 分支。

硬件命中必须匹配实际 tid、PC、启用槽的 DR6 状态和预期返回 RSP，才生成真实 `1003/1005` 并清除该槽。匹配 PC 但 RSP 不符只发 `1008`，设置 RF 让该指令执行一次，保留 pending/DR 槽；不伪造返回。DR6 只清已处理的 B0–3/BD/BS/BT，保留其余位并置 BLD[11]/RTM[16]。每次修改后通过新的 GetThreadContext 读取实际寄存器，再写 raw，不把拟写入的 CONTEXT 当作读回值。

| 新 profile 事件 | Shared 主要栈/槽合同 |
|---|---|
| `1002/1004 entry` | index=debugSlot，result=1，Q40 module=基址，Q48 target=entry PC，Q56 original=真实返回 PC，Q64 entry=instanceId，Q72 epoch=entry RSP+8，Q80 value0=entry RSP，Q88 value1=0，Q96 caller=返回 PC |
| `1003/1005 true return` | index=同槽，result=1；target/original/instance 同对应 entry，Q72=预期返回 RSP，Q80=实际 RSP且必须相等，Q88=真实 RAX，Q96=返回 PC |
| `1008 stack mismatch` | index=槽，result=0，target=经过的返回 PC，Q56 original=预期 RSP，Q64 entry=实际 RSP，Q72 epoch=instanceId，Q80=Dr6，Q88=Dr7，Q96=经过 PC；不消费 pending entry |

入口和真实返回 JSON 直接给出 `actualRsp/expectedRsp/debugSlot/dr6/dr7/instanceId`。所有硬件观察还给出 `hardwareContextBefore/After{rip,rsp,eflags,dr:[Dr0,Dr1,Dr2,Dr3],dr6,dr7}`，均来源于实际 GetThreadContext；`resumeFlagRequested/resumeFlagObserved/slotArmed/slotCleared` 可由这些原数重算。1008 也保留同样 before/after 原数及 `returnStackMismatch`；它的诊断 `processAttach=false`。RF 的下一指令清除属于架构规则，本观察不是逐指令 trace，不能声称观测了每一条后续指令。

规则核对采用微软 [AMD64 CONTEXT](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-context)、[GetThreadContext](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getthreadcontext)、[SetThreadContext](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setthreadcontext)和 [Debugging Events](https://learn.microsoft.com/en-us/windows/win32/debug/debugging-events)，以及 [Intel SDM Vol.3B](https://cdrdv2-public.intel.com/858456/253669-088-sdm-vol-3b.pdf) §19.2.3/19.2.4/19.3.1.1（PDF 149–155 页）。Windows 的 context setter 可规范化 OS 控制位，因此代码检查实际 readback；文档不保证当前候选已经通过实测。

### 已保留的诊断阶段

首次完整尝试 31803 退出 1：前 72 项通过，debugger 在重复名称断言退出，不能记为 73 通过。root 后续 49229 的三个 child 退出 1，采到 publisher 不同基址 Load；96108 的三个 lifetime child 退出 1，暴露重复 Unload 和共享返回 PC 的错误栈命中。其对应源码分别保存在 [diagnostic-debugger.cpp](history/diagnostic-debugger.cpp) 与 [lifetime-debugger-first.cpp](history/lifetime-debugger-first.cpp)。

root 52109 的三个 hardware 诊断 child 实际退出 0，使用源码 `6d5ddbae…3613c8`，已保存 [hardware-debugger-first.cpp](history/hardware-debugger-first.cpp)。每个 child 实有 3 个 1008 续行；attempt2 有 publisher 两次 Load/一次 Unload，最终选中唯一映射。三个 child 都未命中 1007，不能宣称该分支已跑过。当前随后增加入口/返回实际 RSP、槽及前后 DR readback 元数据，仍须 root 新预飞和正式冻结后矩阵验证；这三个成功诊断不替代最终 73/57/73。

## 本次源改动与验收责任

仅适配 EarlyFixedShared、Recorder、Proxy、ImporterApi、Importer、Host、EarlyFixedDebugger 和新增 profile 头；builder/runner 与 producer profile 分别由 root/其他代理维护。Recorder/Proxy 的 publisher record version 改为 `RizFixedVersion`；Importer/Host 的自有记录版本改为 `RizEarlyWorkloadVersion`。原函数、helper ABI、故障分支和运行负载没有另一份实现。

后续只可由 root 在源输入冻结后构建，再保留 73 个真实 child 的退出状态、原始事件、页字节/保护、publisher snapshot、独立 baseline 和 debugger load-only 观察。编译或验证失败应保留原件，不能把配置选择器注入解释为实际 OS API 内部失败。

本页不授予实际游戏 bootstrap/WinHTTP 系统转发、生产 Kernel 接线或 V4 凭据；GC/对象根、coordinator、scheduler/async/内联清理、取消与退休仍需要后续实际实现和各自证据。
