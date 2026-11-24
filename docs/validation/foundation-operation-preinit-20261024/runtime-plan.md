# 18 个 operation 入口的 pre-init 发布与 CLR 采用方案

2026-10-24。普通工程计划，flavor=null。范围是仓库的生产安装与采用路径；不运行构建、游戏、安装或 UI。下列 CLR 改造已落盘，尚未取得本批构建/fixture 结果，不能写为通过。

## 结论

把 12 个 UnityEvent mutation 与 6 个 coroutine Stop 入口纳入原有原生 FirstInstall 的同一次 pre-init 发布。CLR 解析真实 metadata 后只采用已发布的 target/original/entry/epoch，并一次 Attach 观察者。缺少新的完整 V3 凭据时默认生产绑定拒绝；不能根据晚解析出来的 MethodInfo 再调用 Dobby。

原生发布授权、metadata 验证、logical sink 归属是三个不同边界。V3 Ready 证明固定映像和通道发布身份；晚解析只能证明该方法声明与已发布通道相符。它们不证明这次 receiver 属于某个本地会话、过去没有未观察的调用、全部 writer/future producer 关闭或引擎已经取消协程。

## 当前普通安装的风险

修改前默认 `BindNativeOperationObservation` 构造 `NativeOperationContextBridge`，在 CLR 解析 metadata 之后执行 Prepare→Configure(callbacks)→Commit。Prepare/Configure 的顺序可以避免跳入无 original 的 entry，但没有证明运行中游戏线程不会执行 Commit 正在修改的指令，也没有原生 admission gate。上一批真实 Dobby 自有 fixture 与真实 native→CLR ledger fixture 是独立证据，不能合并成生产首写安全证明。

旧 Operation V1 的 Configure 将 original 与必需 callbacks 放在同一个永久 descriptor 中，无法直接在尚未启动 CLR 时配置。因此必须原生分离 immutable original 配置和 nullable observer publication，而不是预先放置假的 CLR callbacks 或在 CLR 启动后替换 original。

## 原生合同

与原生实现代理确认的最终 ABI 如下，以本批实际 header 为准：

- `RizOperationBootstrap_Configure(kind,slot,Config24)`：只配置真实 target/original，不修改源指令。
- `RizOperationBootstrap_Attach(kind,slot,epoch,Observer32)`：只发布一份 enter/exit/user，不重配 original、不二次 Attach。
- `RizOperationBootstrap_GetInfo(kind,slot,Info96)`：返回实际 kind、slot、attached、unknown、target、original、entry、epoch 和 counters。0 是 bootstrap；2 只表示真正空槽；3 是 legacy 或正在 claim 的永久责任。部分 claim 返回 unknown，allocator 不把它当空槽。
- `Frame64` 和 V1 exit flags 保持；entry 开始只读取一次 observer。null snapshot 的原调用跨越 Attach 后仍没有新 Enter/Exit，原函数继续一次，计数与 native finally 仍负责完成。

FirstInstall V3 为1600字节：V2 prefix416，Common header 是3/1600；32字节 operation header 含 capability1、count18、expectedWrites27、reserved0、configuredMask、publishedMask；18份 row64 从448开始。完整 Ready 两 mask 均为0x3ffff；每 row 含 id/kind/slot、实际 target/original/entry/epoch、固定 RVA/bodySize/unwindRva/protection。V1/144 和 V2/416 的旧 export/布局保留，但实际 writesStarted 为27。它们不授权新18入口。

| id | 声明 | kind | slot |
|---:|---|---:|---:|
| 0 | UnityEvent AddListener | 1 | 31 |
| 1 | UnityEventBase AddCall | 1 | 30 |
| 2 | UnityEventBase RemoveListener | 2 | 31 |
| 3 | RemoveAllListeners | 0 | 31 |
| 4 | DirtyPersistentCalls | 0 | 30 |
| 5 | RebuildPersistentCallsIfNeeded | 0 | 29 |
| 6 | InvokableCallList AddListener | 1 | 29 |
| 7 | AddPersistentInvokableCall | 1 | 28 |
| 8 | InvokableCallList RemoveListener | 2 | 30 |
| 9 | Clear | 0 | 28 |
| 10 | ClearPersistent | 0 | 27 |
| 11 | PersistentCallGroup Initialize | 2 | 29 |
| 12 | StopCoroutine(IEnumerator) | 1 | 27 |
| 13 | StopCoroutine(Coroutine) | 1 | 26 |
| 14 | StopAllCoroutines | 0 | 26 |
| 15 | StopCoroutineManaged | 1 | 25 |
| 16 | StopCoroutineFromEnumeratorManaged | 1 | 24 |
| 17 | StopCoroutine(string) | 1 | 23 |

## CLR 最小实现和文件分工

1. `NativeOperationPublication.cs` 保存 blittable V3/row64 值副本、固定声明映射与完整 shape。shape 严格核3/1600/27、18 rows/masks、每个 profile RVA/kind/slot、地址范围、可执行保护和 tuple 唯一性。它不是 Ready owner。
2. `NativeFirstInstall.cs` / `.Operation.cs` 保留私有 singleton owner、helper 已加载且实际绝对路径、固定 GameAssembly SHA/PE 与既有 listener memory 核验。生产唯一读取 GetInfoV3；每次 VerifyReady 重新读完整凭据、实际18个 Info96、函数范围与保护。任何不一致 sticky Unknown。
3. `NativeListenerPublication.cs` 保留历史 V2 shape9write 受控测试入口；新生产 V3 prefix 只接受3/1600/27。生产 `ListenerPublication` 是原始 V3 prefix，不将其伪装为 V2 Ready。
4. `NativeBootstrappedOperationBridge.cs` 只采用实际 Info96，永久 root DLL/委托先于 Attach；Configure 原签名只核相同 original 并附加一次 immutable callbacks。ReadActivity 使用 native Attached，不能等托管 Configure 返回之后的布尔标记；先保存 seenAttached 再读 Info，避免合法旧 Info0 被较新读误标 Unknown。
5. `ExactHookBroker.NativeOperation.Bootstrap.cs` 将 production front 与 configured-channel 共用 core 分开。front 必须 RequireReadyV3，在静态 Gate 内复核，绑定后再次 VerifyReady。core 只首次懒采用 bridge；同物理入口的多 sink 完整比对 metadata 与 native tuple，不二次 Attach。
6. `ExactHookBroker.NativeOperation.cs` 默认重定向上项 front；显式注入的既有受控测试 overload 保留 Prepare/Configure/Commit，不能成为 SDK 或默认生产 fallback。
7. `NativeOperationContextBridge.cs` 的旧 allocator 通过实际 bootstrap GetInfo 跳过已 claim/unknown 槽，不粗略跳过所有高槽，也不覆盖 pre-init 责任。

真实 metadata resolver 的签名/RVA/declaring class/参数/return/MethodInfo 布局核验保留。它在 binding 之前发生，失败时拒绝 logical sink，不拆除原生 patch、不重试 Prepare 或更换 original。所有18个声明必须与固定 V3 row 对应；内部 configured tuple 只能采用自有 fixture 通道，不能建立私有 Ready owner。

## 失败、时序与永久责任

原生首次发布在开始写入后失败，应由 producer 保留实际行、mask、trampoline、DLL 与 Unknown，并阻止继续原 init；不能假回滚。CLR 首次 Attach 前就永久保留 bridge；失败或不确定结果不卸载、不重附加。broker 记录永久 Entry 责任和失败原因，移除本次 logical sink；已有调用仍须按原 snapshot 完成 context 清理。

绑定后的第二次 VerifyReady 失败，只撤本次返回的 Binding，并将共享 entry 标 Unknown；不能撤掉 peer 的 context completion 或物理通道。第一次 Attach 的可见窗口中可能已经执行 callbacks，因此必须在 Attach 前发布全部托管回调闭包，在它返回后读取实际活动并拒绝已 Unknown 的结果。

pre-Attach 原生调用计数不清零。它们在 Attach 后仍计入实际 InFlight，不能被称作安装空闲；null snapshot 不能继承新 CLR 清理。没有历史 observer 的调用不自动产生 CLR ledger 记录，所以此改造不宣称历史 writer 闭合或引擎取消。各 local owner 仍须在已有 activation/Ready/资源保留规则下完成自己的归属与快照验证。

## 必需的独立验收

- 布局/shape：V3=1600、row64、Info96/Observer32，完整18元组和错误 masks/slot/RVA/epoch/protection 拒绝；V1/V2 旧布局实际27但不授予 V3 Ready。
- 固定映像映射：实际 native producer 27写→私有 RequireReadyV3→18个真实通道采用，前后磁盘 hash 不变；不调用 GameAssembly 函数、不构造假的 Ready。真实 metadata 仍可明确受控。
- 自有原生回调：actual bootstrap→CLR shared broker→实际 context capture/complete/dispose；正常、原 SEH/C++、observer fault、worker、nested、逻辑解绑、多 sink、GC 后委托仍有效。旧调用停在原函数内跨 Attach，不得收到新 callbacks。
- 失败责任：重复 Attach、错误 original/epoch/target、post-VerifyReady 失败只撤当前 sink，native/managed Unknown 保留，失败不能重装；allocator 不与 native claims 冲突。
- 全回归与归档：root 唯一构建者完成真实终态后再写通过数和源码/包哈希；当前无本批动态结果。

这些验收仍不能替代真实游戏 cold-start、活体 IL2CPP metadata/GC、actual Stop completion、所有 writer/future producer 或完整资源退休。RequestPlay 不因本计划开放。
