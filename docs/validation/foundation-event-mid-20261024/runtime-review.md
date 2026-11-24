# Runtime 物理上下文与 inline 协议复核

2026-10-24。只读复核当前冻结源码；本文件不表示构建、原生 fixture 或游戏实测已经通过。Root 是唯一构建者，本次复核未执行构建或游戏操作。

## 核对范围与证据

检查 [当前物理上下文](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeVoid.Current.cs)、[原生 void broker](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeVoid.cs)、[mid bridge](../../../src/RizToolbox.Runtime/Hooks/NativeEventMidContextBridge.cs)、[ledger](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.cs)、[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.cs) 以及两组 [Hooks](../../../tests/RizToolbox.Hooks.Tests/NativeVoidObservationTests.cs) / [Audio](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalUnityEventLedgerRegression.cs) 回归源码。此处依据是代码路径和测试内容；实际通过数应以 Root 的最终日志为准。

| 要求 | 当前实现证据 | 结论 |
|---|---|---|
| 不借用父 receiver 的 scope | 每次 entry 在 probe/capture 前压入线程 physical top；查询仅接受同 entry、同 token 的当前 top，不向父链搜索 | 接线满足 |
| 只允许原调用窗口 | Capturing 阶段查询拒绝；Enter 最终返回前设 OriginalWindow；Exit 首先设 Exiting，所有 after/Dispose/probe Exit 后弹栈 | 接线满足 |
| worker 和 foreign child 遮父 | worker/foreign MethodInfo 也有自己的记录；线程与 MethodInfo 门在 context 查询前；无该 sink context 的 child 查询失败 | 接线满足 |
| overflow 不暴露父 scope | token 0 对应线程 overflowDepth；直到配对 Exit(0) 前所有 current 查询拒绝；错序会使线程状态未知 | 接线满足；溢出帧仍明确未被观察 |
| identity 不可跨代重放 | identity 私有保存实际 invocation、sink、原 context；IsCurrentFor 重核 top/stage/thread/overflow/dictionary 同一记录、scope 引用及真实活动 | 接线满足 |
| detach 不提前结束 native 责任 | sink active 只控制新投递与 current 许可；已取得的 context 仍释放，probe 按 entry 快照退出；native helper 持有实际退出窗口 | 接线满足 |
| adopted 列表先持根后检查 | CaptureInline 使用 CapturePrepared 纪律，先 Hold(published:true) 并存根表，再 Target/class/list 操作 | 接线满足 |
| 原生异常和 observer 故障不制造成功 | after 仅正常同帧返回；异常仍走 reverse Dispose/probe Exit，unknown sticky；跨线程和错序保留完整责任 | 接线满足 |
| inline 不冒充完整执行完成 | Inline 与 Pre/Post/IndependentPrepared 分开保存，Pending、InlineExecutionUnobserved、MutationClosed、DispatchPathVerified 保持既有未完成语义 | 接线满足 |

## 已发现并修复的重放问题

最初的 inline 协议只比较 identity 的 entry、非零 token、线程和完整入口 frame。`EventRecord.InvokeContext` 在下一 generation 会复用对象，同 Self/caller/MethodInfo 的后续调用可满足这些标量条件，所以“私有构造 identity”本身并不证明证书仍在其原生窗口。

该发现对应的修复是 `NativeVoidPhysicalIdentity.IsCurrentFor(context)`：证书保留实际 invocation/sink/context 引用，重新验证当前物理 top 和字典中的同一记录。Root 已在 `ObserveInlineList` 的 root 接收前和接收后调用此门。Hooks 的 `CurrentIdentityReplay` 使用完全相同 frame 与复用 context，Audio 的 `InlineStaleCapability` 使用同一 event 的下一 generation，均专门覆盖旧证书失效；尚须实际运行才能形成通过证据。

## 列表接收和故障后收尾

ledger 在采用 raw executing 前核 fixed mid ReturnPc、RSI、RBP 非零、context active/current/generation、entry ABI/地址、真实 caller/MethodInfo 和 `IsCurrentFor`。随后检查 backend 的共享 Invoke/Prepare 活动为 Known 且存在活动窗口。`CaptureInline` 中的前置 Check 只检查 managed authority 和 transport statistics；对 RBP 指向对象的第一项操作是建立并登记独立根，之后才读取 Target/class。已有同一 backend 独立根的地址会复用该责任，不转移 Source 的根。

root、列表或元数据检查期间出现 fault 时，新增根不会被丢弃；ledger 不出版 Inline。接收后再次核通道和证书，再冻结完整列表并检查与该次图中的 executing 身份、版本、行顺序相同。Inline 的 raw list 与 Pre/Post 的列表不相同是允许的，三个快照分别保存；独立 Prepare 不被拿来补 inline 证据。

事件 context 在正常 original 返回后的 after/dispose/probe Exit 才结束；observer fault 会使 authority 或 entry unknown，后续 native 图读取门拒绝继续。错线程 Exit 和错序 Exit 不尝试寻找父记录，也不宣布资源可释放。已保存的过期 identity 可能仍保留托管 context 对象，但 `IsCurrentFor` 会拒绝，不能因此重新读取其已结束的 native 责任。

## 必须保留的限制

mid Frame 是 native snapshot 的值复制，managed observe 没有原寄存器写回通道。Bridge 32 个槽、DLL、reverse P/Invoke callback 和未知配置都由进程静态 owner 保留，不提供卸载或复用。

native mid statistics 只覆盖 C++ Dispatch/callback：其 inFlight 归零时 ASM 恢复和 epilog 仍可能未离开，不能据此单独授权资源退休。本批 current identity 还要求父 NativeVoid 的真实 native inFlight，ledger 也保留 Pending；完整 mid 物理退出和回收协议仍需后续证明。

生产 mid patch 未发布。当前固定游戏中段不满足已经证明的安全发布/栈与 GC safepoint 条件；这里实现的是观察 transport 和 adopted-list 协议组件，不能把 controlled frame、native fixture 或离线通过数当成真实游戏 inline 验收。三个 listener 调用分支、mutation、delegate 派生任务、取消和整体资源退休继续未关闭，`DispatchPathVerified` 保持 false。

本次复核在重放修复后未发现新的确定接线缺陷。该结论的范围限于上述冻结代码路径，后续构建、真实 native ABI 及实际游戏行为分别需要独立证据。
