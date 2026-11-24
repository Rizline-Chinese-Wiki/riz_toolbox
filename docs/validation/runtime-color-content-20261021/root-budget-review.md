# Visual 根与额度释放只读审查

审查日期：2026-10-21。范围：`VisualResourceService.FinishReleaseEntry`、实际`NativeVisualBackend.NativeObject`/`NativeRuntime.RootHandle`、`VisualResourceBudget`及已有相关测试。未修改生产代码，未启动构建，未新增或执行测试。另一个代理正在添加retirement完成日志；本审查不把新增日志当作766实机证据。

## 结论

**异常分支确实会丢失清理责任，但没有找到当前真实后端正常使用条件下可由游戏/插件输入触发的Root.Dispose或Budget.Dispose异常路径。** 应记录为缺少故障注入覆盖及保守隔离的边界缺口，不能仅看catch就报告成已复现的生产GC根泄漏。

如果Root.Dispose真的抛出，`FinishReleaseEntry`仍会把entry标为Released、从两个字典移除、释放额度并递减依赖；`TryReleaseEntry`随后返回true，retirement可以解除保留。它没有保存待重试或隔离失败的root责任。另一方面，owned资源到达这里之前必须已观察到`Native.IsAlive=false`，因此**根释放失败本身并不证明仍存活的Sprite提前失去了Texture依赖**。

## 代码证据

| 位置（仓库相对路径、当前行） | 实际行为 | 含义 |
|---|---|---|
| `src/RizToolbox.Adapter/Visual/VisualResourceService.cs:344` | `Released=true`及两个字典Remove先于Root/Budget.Dispose；两个catch只记日志，随后递减Dependency | throwing root会使该entry无法重新进入Sweep清理；owned的ticket谓词仍返回true |
| `VisualResourceService.cs:327` | Owned执行Destroy后，必须读到`IsAlive=false`才调用FinishReleaseEntry；Destroy异常/活性不可读均保留 | 根释放异常与“原生对象仍活着”是不同阶段；现有实现已保护Destroy失败和Unity延迟销毁 |
| `src/RizToolbox.Adapter/Visual/NativeVisualBackend.cs:204–221` | NativeObject.Retain返回`NativeRuntime.RootHandle`，空指针返回无操作Empty；没有插件提供的Dispose回调 | 生产Root不是任意用户IDisposable，不能用接口的可注入性代替真实触发链 |
| `src/RizToolbox.Runtime/NativeRuntime.cs:264,298–302` | RootHandle.Dispose先Verify，再Exchange id为0，再调用free；free是`il2cpp_gchandle_free`的Cdecl void委托（85行） | 错线程Verify失败时id仍保留；若free本身抛出，id已清零，不能盲目重试而假设未free |
| `src/RizToolbox.Runtime/NativeAbi.cs:185` | NativeThreadAccess只绑定一次，拒绝换线程；Verify只核线程 | 不存在运行中任意重绑使合法调用突然变错线程的正常路径 |
| `src/RizToolbox.Core/MainThreadDispatcher.cs:57`及服务/retirement入口 | 清理从VerifyAccess主线程进入；RuntimeBridge在绑定Unity生命周期线程后才运行tick | 当前生产路径没有发现“服务入口允许、Root线程门拒绝”的有效运行条件 |
| `src/RizToolbox.Adapter/Visual/VisualFileAccess.cs:49–66` | sealed内部Budget的Release仅扣已存在的owner字典项和计数；先清空Action；未调用用户代码，也不删除owner键 | 除进程/状态损坏或额外故障注入外，没有找到普通Dispose异常来源；不能把任意throwing预算lease注入当成现有公开API能力 |

上述行号为本次读码时位置；后续编辑可能变化。

## 可构造的最小失败链（尚未执行）

只在内部测试后端上构造：让`FakeObject.Retain()`返回一个在释放前抛IOException的IDisposable，创建Sprite及其Texture，释放插件资产/作用域。保持Destroy正常，使Sprite.IsAlive变false。

预期按当前代码静态推演：Sprite根的Dispose被调用一次并抛出→日志`Visual root release failed`→entry已从字典删除、Released=true→预算扣回→Texture依赖引用递减，其他引用也为0时Texture进入正常销毁→Sprite的retirement谓词返回true，后续Tick不会再次清理该root。如果这个测试root代表一个实际未free的GCHandle，则该handle仍存在，但服务已经失去它的追踪/隔离责任。

这能验证失败处理逻辑缺口；**它不等于真实`il2cpp_gchandle_free`会在该游戏中抛IOException的证据**。如果要覆盖真实RootHandle的free委托抛出情形，Runtime.Tests能直接传入throwing FreeHandle；它还会证明RootHandle在异常前已经清零id。对于这类“free可能已经发生”的未知结果，不能简单循环Dispose，否则可能掩盖责任或引入重复free。

预算异常的对应测试需要反射替换私有AssetEntry.Budget或更改测试接缝；实际sealed Budget不能通过现有插件API提供throwing Release。此审查没有为制造失败修改生产设计。

## 已有覆盖与未覆盖

- `tests/RizToolbox.Visual.Tests/Program.cs:308`：借用/owned资源root各释放一次；fake Release仅增加计数，不抛异常。
- `Program.cs:313`：Destroy重入及异常；异常发生在native死亡判定之前，不能替代Root.Dispose异常测试。
- `Program.cs:367,374`：延迟销毁、Destroy前抛异常保留根/依赖/额度，之后恢复。覆盖了本次实机pending所属阶段，不覆盖FinishReleaseEntry中的free失败。
- `tests/RizToolbox.Runtime.Tests/Program.cs:262,269`：root幂等、错线程拒绝后原线程可释放、强根跨GC模型。未测试FreeHandle委托在调用时抛异常。
- `VisualResourceBudget`已有额度和归还测试（Visual.Tests:300），没有释放中异常测试；在当前实现下也没有正常可抛的外部回调。

## 建议与验收边界

优先为throwing根增加有意义的内部故障注入回归。若底层要求覆盖未知free结果，应把“native已死”与“GC根/额度责任清理成功”分开记录：失败者保留有界process级隔离项和额度责任，不能在仅记警告后认定全部物理清理完成。不要对可能部分成功的free自动重复调用；先定义RootHandle在free失败时的明确结果/隔离策略。

新增retirement完成receipt至多证明cleanup谓词返回true、retirement reservation释放；即使正常根释放没有异常，它仍不证明GPU内存已回收。对于766实机那一次`Visual cleanup pending`，本审查不补写不存在的成功receipt，也不把“未发现生产触发Root异常”解释成物理释放已验收。
