# 固定 UnityEvent mid 发布事务

2026-10-24，只读设计；1507 源码保持冻结。目标是增加可验证的私有发布事务，生产入口在缺少首次 parent 安装证明时继续拒绝。本文没有安装或执行游戏代码。

## 现有能力与缺口

[NativeVoidAdmissionLease](../../../src/RizToolbox.Runtime/Hooks/NativeVoidAdmissionLease.cs) 只能证明一个**已配置的** void slot 已 Closed、Active 为零，允许 Parked 非零。`MarkWriteAttempted()` 后 Dispose 永不重开；当前没有“写入并实证成功后释放”的 API。它不证明第一个 parent detour 安装安全，也不证明该 slot 对应哪一个已验证的游戏模块/入口。

[NativeEventMidContextBridge](../../../src/RizToolbox.Runtime/Hooks/NativeEventMidContextBridge.cs) 已提供永久 callback/DLL 根、`Configure(actualReturnPc, observe)` 和原生活动统计。[ledger 的 ObserveInlineList](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.cs) 已接同代私有 physical identity；中段仍须由安全发布器实际安装。

[固定静态证据](../foundation-event-mid-20261024/mid-site-evidence.json) 给出唯一 `RUNTIME_FUNCTION [0x15642E0,0x156454E)`、Unwind RVA `0x1DB3FB4`、完整五字节 NOP `0x156441B: 0F 1F 44 00 00`，硬件 CALL 返回 PC 为 `0x1564420`。只替换这个 NOP，保留 `0x1564417/19` 的 XOR。独立 fixture 和联合回归不产生生产安装许可。

## 私有类型及事务入口

建议新增 `Hooks/NativeUnityEventMidInstaller.cs`，只支持一个固定 profile；不提供 `(IntPtr address, bool safe)`、任意 RVA 或公开写内存接口。对外仅返回状态快照，所有权限对象均 sealed、私有构造：

| 私有对象 | 创建者与持有内容 |
|---|---|
| `VerifiedUnityEventImage` | 固定 verifier；实际 module base/SizeOfImage、持有的模块引用、磁盘文件身份与 SHA-256、PE executable section、`.pdata/.xdata` 原始值和临界代码字节 |
| `SafelyInstalledParent` | 尚待实现的首次安装协议；实际游戏 image、精确 Invoke target/MethodInfo、broker physical entry、native bridge slot、真实已发布 patch/trampoline 和首次安全点证据 |
| `PreparedMidOwner` | 本事务；上述两个对象、固定 ReturnPc、永久 bridge/callback/relay 根、原保护/字节与目标 CALL/relay 字节、阶段和失败原因 |
| `VerifiedMidPublication` | 本事务在实际写入后复核成功才创建；绑定同一个 owner、image、parent bridge/slot、lease 实例/epoch/OS thread、实际 code/relay/helper/保护/flush 结果；只能消费一次 |

入口可为 `Prepare(VerifiedUnityEventImage image, SafelyInstalledParent parent, Action<NativeEventMidFrame> observe)` 返回 owner，随后由同一 owner 的 `Publish(NativeVoidAdmissionLease lease)` 完成事务。调用者不能用 success 布尔值或自行构造 receipt 提供权限。`Prepare` 先验证全部依赖；没有 `SafelyInstalledParent` 就返回明确不可用，不能先分配/关闭门再等待人工确认。

为当前代码保留的最小接线是：broker 增加仅能从它已拥有的精确 entry 读取的内部 parent 关联证据；它不能凭普通 `VerifiedHookTarget` 或进程地址表自动制造 `SafelyInstalledParent`。该证明必须追溯到首次安全安装协议。正常 SDK 与 fixture 的可构造目标仍无此权限。

C# 无类间 friend 权限，落实时建议把 `NativeVoidAdmissionLease` 改为 partial，将 installer 与 `VerifiedMidPublication` 嵌套在该类的新增 partial 文件中，完成消费方法保持 private。这样只有受限嵌套事务能创建 publication receipt 并调用完成方法，程序集其他代码无法通过同样的 internal 签名自行制造“已验证成功”。映像/parent proof 的取得仍由固定 verifier 与后续真实首次安装协议提供，不能为方便测试增加生产 proof 工厂。

## 固定映像核对

verifier 必须在当前装载实例上完成以下绑定，不能只读一份同名磁盘文件：

1. 实际模块引用固定于进程责任，base、SizeOfImage、Machine、section 和实际 executable memory allocation 均一致；磁盘文件身份与已允许 SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` 绑定。运行期已有 parent patch 必须来自该 owner 的记录，不能要求已改写 image 的完整映射内容等于原 PE。
2. 官方 `ResolveObservedInstanceVoid0` 结果仍是实际 Invoke class/MethodInfo、固定 entry 与 ABI；读取当前 `.pdata`，并与 `RtlLookupFunctionEntry` 的实际映像 base/Begin/End/UnwindData 交叉核对，必须等于固定记录，拒绝邻接函数、chain 或改变的 unwind。
3. mid site、真实 ReturnPc、前后固定范围均位于该唯一函数与 executable section，五字节必须完整等于 NOP；同时复核周围 XOR/下一条 compare 及固定异常信息。base+RVA 使用 checked 算术，拒绝任何跨映像或跨区域读取。

映像对象保留验证输入及有界原始字节；事务在发布前后重复核临界身份。磁盘 hash、fixture 的 `target-0x15642E0` 锚点或单次 NOP 匹配均不能单独成为生产证明。

## 发布和成功释放

1. 在进入写阶段前，将 owner 永久登记到进程表，取得近端 relay、bridge 和 callback 根。relay 为 `FF 25 00 00 00 00 <helperVA64>`，先写入、变 RX、flush 并实际读回；CALL 位移严格为 `relayVA-(module+0x1564420)` 且落在 Int32 范围。mid bridge 先用这个真实 ReturnPc Configure 并确认 Known。任何有可能已发布的 relay 都不释放或复用。
2. 取得仅属于上述 parent bridge/slot 的 admission lease，核同一 owner 映像/入口绑定、OS thread、epoch、Closed/Active0 与 native Known。Parked 可以非零，旧 full inFlight 不必为零；它们尚未进入被保护 original。确认首次 parent 安装证明以及此后所有入口都必须经过同一 admission gate、无绕过 trampoline/未覆盖 alias 的可用路径。
3. 在 `MarkWriteAttempted()` **之前**完成最后原字节/保护/模块/function/relay/bridge/receipt 检查；未尝试写可通过现有 `ReleaseUnmodified` 安全结束。之后只走成功验证或 unknown 保留两个出口。
4. 已封闭准入且真实 Active0 的前提下，对固定五字节目标页修改保护、写 `E8 rel32`、恢复原保护、flush instruction cache；每步记录实际调用结果。仍需既定发布屏障覆盖所有可能执行者：5 字节写不是原子发布协议，不能用 memcpy/VirtualProtect/flush/readback 自行补造首次安全点。
5. 写后实际重新读取五字节、relay 完整 14 字节和 helper entry，核 CALL 解码目标、ReturnPc、周围字节未变、页保护恢复、模块/function 仍同一实例、bridge Known、同 lease VerifyClosed 与 native Active0。全部成立后，事务私有构造 `VerifiedMidPublication`；只读日志不是此权限对象。
6. `NativeVoidAdmissionLease` 新增**私有到受限事务**的完成方法，仅接受该次 `VerifiedMidPublication` 并核它绑定同一 lease/epoch/slot/OS thread/owner、未消费且实际成功复核仍成立，随后调用 native exact Release。成功才标记 Published/receipt consumed。不能增加 `ReleaseAfterWrite(bool verified)` 或把原始 receipt 交给调用者直接 Release。

在任一步写尝试、保护恢复、flush、读回、proof 或 Release 结果不确定时，调用 `RetainUnknown`，保留 owner、relay、callback、lease 与实际修改记录并将 bridge uncertainty 向上报告。Dispose 不回滚、不重开、不重试该地址；fixture 能证明回滚成功也不能作为生产默认未知处理。发布成功后的逻辑 detach 只撤观察，不卸载现场 CALL 或释放桥；完整退休是另一个未完成的协议。

## 最小文件改动与默认拒绝

| 文件 | 后续改动 |
|---|---|
| 新 `NativeUnityEventMidInstaller.cs` | 固定映像 verifier、owner 状态机、近端 relay、实际写后验证与私有 publication receipt |
| `NativeVoidAdmissionLease.cs` / admission partial | 只增加同 owner 的成功 publication receipt 消费通道；保留 write/unknown 的 Dispose 禁开门与 ReportUnknown 规则 |
| `ExactHookBroker.NativeVoid.*` | 受限 parent/bridge/slot 关联与首次安装证明的来源，不能以地址存在代替 proof |
| Adapter backend | 仅在完整 proof 可得时组合既有真实 current identity 与 ObserveInlineList；缺 proof 返回明确 unavailable |
| 新独立 installer 测试 | 在自身 fixture code 首次调用前创建 fixture 专属证明；真实窗口关闭、写/flush/protection/读回故障、wrong image/lease/epoch/owner/replay 全部拒绝，不赋予 fixture proof 游戏权限 |

当前 `SafelyInstalledParent` 的生产创建者尚不存在：现有 Dobby Prepare→Configure→Commit 证明配置先于 patch 生效，却没有首次并发 instruction publication、安全点或所有线程未来入口均受 gate 控制的证据。因此可以先独立实现私有事务和有界验证，但生产 `Prepare` 默认拒绝，不向游戏发布 mid，不启用 RequestPlay，也不把本设计或 fixture receipt 记为该缺口已经完成。
