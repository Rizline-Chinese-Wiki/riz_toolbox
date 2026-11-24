# 最小真实 native parent 与 mid 联合回归计划

2026-10-24。只读设计，当前没有改动 native、managed source、测试或构建脚本，也没有运行构建。此计划弥补联合调用链的证据缺口，不涉及向游戏发布 mid patch。

## 需要证明的同一次调用

必须实际执行以下整条链：现有 MASM caller → 已由 Dobby 发布的 `NativeVoid0` parent → native void helper → 原 trampoline → fixture 原函数 → 实际 native 中段 CALL → native mid helper → CLR `NativeEventMidContextBridge` → 同 Binding 的 `TryGetCurrentNativeVoidContext` / `IsCurrentFor` → `NativeLocalUnityEventLedger.ObserveInlineList` → fixture backend 的同代列表采用 → native mid 返回 → original 返回 → parent after / Dispose / paired probe Exit。

不能由测试直接调用 private broker Enter/Exit，不能在 mid callback 内手工构造 NativeVoidFrame 或 identity，不能用一份 controlled broker 测试和另一份 standalone mid 测试相加代替这条联合路径。列表/metadata backend 仍允许 controlled seam，但其范围必须单列，不得称为真实 IL2CPP GC root 或游戏图验收。

## 可复用的现有文件与入口

| 文件 | 已存在可复用内容 | 联合回归用途 |
|---|---|---|
| [InitializationAbiFixture/Fixture.cpp](../../../native/RizToolbox.InitializationAbiFixture/Fixture.cpp) | `VoidBody`、`RtVoidTarget`、`RtVoidExpectedCaller`、`RtVoidControl`、`RtVoidRead`、`RtVoidInvoke`、`RtVoidInvokeWorker`、`RtVoidResume` | native original 次数、递归、worker、堵塞、SEH/C++ 原异常及调用结果记录 |
| [InitializationAbiFixture/Entries.asm](../../../native/RizToolbox.InitializationAbiFixture/Entries.asm) | `PATCHABLE_ENTRY` 的独立 32 NOP entry，`VOID_CALLER` 的真实 caller label 与 tail relay | 新独立 parent kind 的实际 Dobby entry、native caller 及 fixture-only mid label |
| [NativeVoidAbiRegression.cs](../../../tests/RizToolbox.Runtime.Tests/NativeVoidAbiRegression.cs) | NativeLibrary/GetExport、PE image bounds、DobbyDetourEngine、实际 NativeVoidContextBridge、catch result 和 observation 布局 | 复用写法，不反射其 private Fixture 或直接调用 broker private methods |
| [MidCaller.asm](../../../native/RizToolbox.JudgeContext/fixture/MidCaller.asm) | 正确 FRAME/unwind、RSI/RBP、完整状态前后 snapshot 与 helper call 的证明方式 | 取其保存/恢复和实际 return label 模式，避免复制整个 standalone executable |
| [MidFixture.cpp](../../../native/RizToolbox.JudgeContext/fixture/MidFixture.cpp) | standalone helper 配置、真实 return PC、callback 异常与 native 状态复核 | 继续独立运行；不能直接将它的 `main` 链入 DLL 冒充联合 managed 路径 |
| [NativeLocalUnityEventLedgerRegression.cs](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalUnityEventLedgerRegression.cs) | controlled graph、authority、CaptureBackend、source generation 与 immutable snapshot 断言 | 增加单独真实 native transport fixture，替换该 fixture 的 controlled bridge/engine 部分 |
| [IsolatedAudioRegression.cs](../../../tests/RizToolbox.AudioContent.Tests/IsolatedAudioRegression.cs) | 每条 retained-owner 故障 case 独立子进程 | 联合成功/故障 case 隔离永久 patch、callback root、unknown owner 与 32 slot 配额 |
| [Build.ps1](../../../scripts/Build.ps1) / [Build-InitializationAbiFixture.ps1](../../../scripts/Build-InitializationAbiFixture.ps1) | helper、fixture DLL、Dobby 路径与环境变量，Runtime/Audio 顺序运行 | 正常完整构建自动向 Audio 子进程继承三个既有 native 环境变量 |

把联合用例放进 `RizToolbox.AudioContent.Tests` 最小：该程序集已获 Runtime 与 Adapter 两方 InternalsVisibleTo，现有 project reference 已经包含 Adapter，不需要扩大生产可见性。Runtime.Tests 虽引用 Adapter 项目，但没有访问 Adapter internals 的 friendship；把 ledger 联合 case 放在那里反而要加新权限。

## 必须保持的地址关系

ledger 当前同时要求 `identity.Entry.Address == module + 0x15642E0` 与 `midFrame.ReturnPc == module + 0x1564420`。因此联合 fixture 的实际 mid return label 必须位于其真实 parent entry 后 `0x140` 字节。任意 standalone MidCaller 返回地址不能满足这个关系，不能通过改 ledger 常量、把 Frame.ReturnPc 改成常量或随意换一个 module 数值绕过。

建议新增 fixture-only **void kind 4**，保留 0–3 原地址和原有测试行为。由 MASM 将 parent patchable entry 与指定 mid return label 放在同一有界布局单元内，汇编时/编译后双重断言 `MidReturn - ParentEntry == 0x140`。mid 使用真正 CALL 的返回标签，RSI 保存这次 parent self，RBP 保存配置的实际 fixture executing 指针。

最少实现可使现有 `VoidBody(4, self, method)` 在 native 原函数活动窗口内调用一个专门的 FRAME 声明 MASM mid wrapper；这个 wrapper 的 call return label 仍按上面布局约束定位。该路径是 native original 内部调用的真实 native 观察点，报告中明确包含 wrapper 这一级栈帧，不声称它重现游戏函数自身的编译布局或 GC safepoint。若实现者选择把 mid 直接放在新的 parent ASM 函数体中，也必须保留正确 unwind 和原异常的 native catch 证据，不能省略。

mid wrapper 中用同模块 rel32 CALL 到一个 tail relay，再跳转配置的 helper entry。这样不要求 fixture DLL 和 helper DLL 位于 ±2 GB 范围，helper 收到的仍是 wrapper 真正 CALL 返回标签。wrapper 必须保存 Win64 非易失寄存器并用正规 prolog/epilog，不把 native 状态存进 managed 临时变量。

测试中的 `controlledModule = ParentEntry - 0x15642E0` 只是 CaptureBackend 的固定偏移锚点；先断言它为正且 `controlledModule + 0x1564420 == exportedMidReturn`。它不等于经过哈希选择的真实 GameAssembly 模块，本 fixture 不覆盖生产 resolver/profile。actual parent entry、caller、mid return 均额外核属于真实 fixture PE 的 executable 范围。

## 最少两个新增 exports

扩展既有 `RtVoidTarget/ExpectedCaller/Control/Read/Invoke/InvokeWorker/Resume` 的 kind 范围至 4，以新 kind 复用原有 native original 和 catch 设施；不改旧 kind 的行为。新增只需要：

| 建议 export | 签名/返回 | 约束 |
|---|---|---|
| `RtVoidMidConfigure` | `uint32_t(kind, void* helperEntry, void* actualExecuting)` | 仅 kind 4；仅 original 空闲时配置；要求 helper/list 非零；配置原生真实 mid CALL 的目标和 actual RBP 输入，不能执行 managed observer |
| `RtVoidMidExpectedReturn` | `void*(kind)` | kind 4 返回实际 ASM mid CALL 后 label，非法 kind 返回 null；不返回推算地址 |

新增 C++/MASM 内部协作函数与 data 符号不需要 export。统计尽量使用既有 `RtVoidRead` 的 original entered/exited 与真实 helper stats；callback/采用次数在 managed callback 内计数。如果要证明 wrapper continuation 恰一次，可让 `RtVoidRead(kind4).returned` 记录 native mid continuation 次数，明确只在 void kind4 赋予该诊断字段此含义，避免再增加第三个 export。原有 native kind0–3 的 assertions 保留。

## 配置和一次实际调用顺序

1. 从既有 `RIZ_INITIALIZATION_ABI_FIXTURE`、`RIZ_INITIALIZATION_ABI_DOBBY`、`RIZ_INITIALIZATION_CONTEXT_HELPER` 读路径。使用真实 fixture DLL 的 `RtVoidTarget(4)`、`RtVoidExpectedCaller(4)` 和新 `RtVoidMidExpectedReturn(4)`，核实际映像范围、唯一入口与 `+0x140` 差值。
2. 创建真实 `NativeEventMidContextBridge`；Configure 使用刚取得的真实 mid return PC 与 managed observe。observer 只复制实际 frame 并调用当前 Binding 能力，不制造/改写字段；DLL/桥/callback 永久保根。
3. 配置 native `RtVoidMidConfigure(4, midBridge.EntryAddress, actualExecuting)`；fixture executing 使用与 controlled backend 初始图一致的有效 HGlobal 对象地址，不能用任意不可访问整数冒充列表存储。controlled backend 可读 fixture list 的 size/version/items 来生成快照，其 root 协议仍按 controlled adoption 证据记录。
4. 创建 ledger 的 controlled authority/backend。backend 使用真实 `DobbyDetourEngine`、`NativeVoidContextBridge`、`BindNativeVoidObservation` 和 `BindBoundaryProbe`；由正常 Prepare→Configure→Commit 发布 parent。保留原来的 `CaptureNativeInvoke/CompleteNativeInvoke/ProbeEnter/ProbeExit` 接线，不注入 simulated NativeVoidFrame。
5. mid observe 先核 `frame.IsValid`、actual return PC 与 helper stats；通过原 Binding 的 `TryGetCurrentNativeVoidContext<InvokeContext>(frame.Event, …)` 获得真实原生 parent 的私有证书，再交给 `ObserveInlineList`。后者继续核 fixed-address 关系、证书的 IsCurrentFor、generation 和 actual executing 身份。
6. 调用真实 `RtVoidInvoke(4, parentTarget, self, MethodInfo, result)`；native helper 调 original 一次，原函数内部 mid CALL 进入 CLR 完成 adopted-list 协议，正常返回后继续原有原函数退出、parent after、Dispose、physical probe Exit。

callback 安装时 ledger/backend 可能尚未赋值，observer 可持受控容器，但没有开始真实 Invoke 前必须全部绑定。配置期不得为了“热身”先调 helper，亦不得用 Gate 或主线程等待 native worker 造成安装死锁。

## 最小联合用例集合

建议三个隔离 case，证明范围足够同时保持实现很小：

1. **真实父与 inline 正常路径**：非空及空 fixture list 各一次；parent capture 早于 original，mid callback 时 parent/native mid inFlight 均大于零，entry caller 等于 exported parent caller，mid return 等于 exported mid label，Event/Executing 等于 native 输入。`IsCurrentFor` 为真，采用与 ledger generation/token 对齐，Pre/inline/Post 分离、inline 后 original continuation 和 parent normal after 各一次。完整返回后所有 native inFlight 为零，scope 只 Dispose 一次；Pending/DispatchPathVerified 保持未完成。
2. **真实 nested/worker 边界**：用已有 `RtVoidControl` 的 native nested call 配置同已 patch parent 与 foreign self/MethodInfo，mid wrapper 必须读取嵌套调用自己的 self；foreign child/worker 不获得父 context、不可采用父列表，probe 全部配对，actual original 都执行一次。正常父在 child 返回后重新得到自己仍 active 的 token。worker 使用真实 `RtVoidInvokeWorker`，不能只用 Task 调 controlled bridge。
3. **真实故障与下一代重放**：mid observer/controlled adoption 在 root 接收后 fault，原生继续与 original 次数不变、unknown 保留、无 Inline 出版；另取前一次真实 callback 的 identity，在下次相同 self/caller/MethodInfo 且复用同一 InvokeContext 的真实 callback 中尝试采用，必须在采用前拒绝旧证书。已有 native original SEH/C++ mode 至少各穿过一次 mid，不能把异常路径记为 Returned；原异常 code/kind/payload 由既有 native catch 取得。

新 case 必须纳入 Audio Program 的 isolated case 选择，不能清空静态 owner 或扩大 slot 配额来让测试通过。Root 唯一构建，完成后分别保存 native fixture 地址关系/模块哈希、真实 callback 计数、父和 mid 活动计数、generation/token 与 adopted snapshot 证据。

## 通过后仍不能宣称的范围

这会证明真实 native parent、真实 mid helper、CLR bridge、broker 私有 current identity 和 Adapter core 同 generation 之间的联合接线。fixture graph/root/backend 仍属于受控来源；没有证明真实 UnityEvent 的 IL2CPP object、生产中段 patch 的安全发布/GC safepoint、三条 listener 调用、mutation/future/取消和资源退休。不能据此启用生产 mid patch、RequestPlay 或 `DispatchPathVerified=true`。
