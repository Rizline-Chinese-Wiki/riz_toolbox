# B12 视觉目标动态准入与身份审计

日期：2026-10-20。方法：审计阶段只读检查当前源码、既有测试和契约；未执行测试、未启动游戏。以下“确定缺陷”指可由代码控制流确定的行为，不等于已在真实游戏中触发。审计后获准实施的最小 Core 修复见末节；Visual 容量策略未改。

## 结论

**动态准入确有未完成的底层能力；首次排序测试不能证明它已解决。** 当前最多 128 个已绑定目标可以长期排斥后来出现的目标，包括优先级更高的 TMP/Translucent。容量分支还会阻断已有目标的语义刷新。原生发现固定扫描数组前 4096 项，另外形成独立的扫描饥饿。

**身份保护并非完全缺失。** 同一 registry 内的销毁、重建、旧代次拒绝，以及 Visual 规范键中的随机 binding ID 已有实现。另发现一个确定的通用 registry 缺陷：代次计数器按实例重新开始，使旧句柄可能被新的 registry 接受；Visual 的随机规范键挡住了这条具体路径。目前没有证据证明真实 Unity 指针复用已经突破 Visual 的持根与身份保护。

| 项目 | 判断 | 当前影响 |
| --- | --- | --- |
| 128 已满后新 TMP/Translucent 准入 | 确定未实现；现有文档已承认 | 已有目标持续存活时，新目标可永久不可见 |
| 容量满时继续刷新已有别名/元数据 | 确定实现错误 | 遇到第一个未绑定候选即停止，后面的旧目标也不再更新 |
| 原生扫描超过 4096 原始对象 | 确定固定前缀饥饿 | 即使绑定容量有余，后部对象也可能从不被检查 |
| 同 registry、已观察到销毁后重建 | 已有实现及测试覆盖 | 旧代次拒绝，新绑定需要重新解析 |
| 不同 registry 的句柄隔离 | 确定违反 SDK 契约 | 同键、同类型、相同局部代次可接受旧句柄 |
| 真实 native pointer/instance ID 同时复用 | 未证实 | 不应写成已复现缺陷，也不应宣称完全穷尽 |

## 1. 容量分支同时阻断动态准入和语义刷新

证据：

- `src/RizToolbox.Adapter/Visual/VisualResourceService.cs:33`：`MaximumTargets=128`。
- 同文件 `Refresh`，约 85–92 行：先处理已有 `candidate.Identity` 的别名/元数据，然后执行 `if(_bindings.Count>=MaximumTargets)break;`。
- `src/RizToolbox.Adapter/Visual/VisualMaterialSlots.cs:13`：排序只作用于本轮候选枚举。`VisualCandidate` 没有优先级字段，服务层也没有替换/公平准入策略。
- `docs/Visual-resources.md:87` 已明确说明首次排序不等于动态淘汰或公平准入。

确定序列：

1. 首轮发现 128 个普通、活跃且持续存活的目标，全部绑定。
2. 后续出现 TMP 目标。后端将其排到本轮候选前面。
3. 它不是已有 binding，容量检查直接 `break`。
4. 每轮重演同样顺序，TMP 永远不进入；排在它后面的已有目标也无法执行 `SetAliases` / `UpdateMetadata`。

因此对象改名、场景或语义信息变化即使已被后端发现，也可能不能反映到查询结果。这不是仅仅“第 129 个目标不支持”的预期限制。

最小修复应分两步：

1. 容量满时跳过新增而继续处理枚举，确保所有本轮已发现的已有绑定都能更新语义；对延迟准入给出有界、去重的诊断。只把 `break` 改成 `continue` 能修复元数据问题，**不能解决动态饥饿**。
2. 将发现优先级作为内部候选/绑定数据传到服务层。迟到的高优先级目标可替换无活动覆盖持有者的较低优先级目标；同优先级的公平性另用受限轮换/等待年龄策略，不能每轮全量换绑。持续有活动覆盖的目标保留。

淘汰必须调用既有恢复、失效、retirement 路径，不得直接删除字典并释放 native root。若恢复产生 `VisualCleanupPending`，本轮立即停止新增准入，保持资源和预算；后续 cleanup 已确认完成后再准入。候选淘汰条件还需检查 `NativeDepth`、通道持有者以及未确认的 owned assignment，不能把“当前没插件租约”误当成“确定没有 native 引用”。

最小验收场景：

- 先占满 128 普通目标，再出现 TMP/Translucent；可安全淘汰时新目标进入，数量不超过 128。
- 容量满且首个候选是新目标时，后面的已有目标改名/改 metadata 仍生效，原目标代次保持不变。
- 所有可选受害者都持有有效覆盖时，保留覆盖，明确报告延迟；释放后能继续准入。
- 淘汰恢复失败时保持根、资源预算和 unavailable 状态，既不强行回收，也不继续新写入。
- 有界同优先级轮换不会复活旧句柄，也不会每轮打断持有中的覆盖。

## 2. 4096 上限当前限制的是固定数组前缀

证据：

- `src/RizToolbox.Adapter/Visual/NativeVisualBackend.cs:52` 从 `ArrayItems` 首项开始枚举。
- 约 54 行先计数/判断 4096，再判断 `Eligible`；无效、停用、非目标等对象也消耗原始扫描额度。
- 材质优先级排序发生在本轮已经扫描出的候选之内，无法提高数组前缀以外的目标。
- `src/RizToolbox.Runtime/NativeRuntime.cs:194` 的 `ArrayItems` 每次都从索引 0 调用原生 `GetValue`。

若数组顺序稳定、前 4096 项长期占据前缀，后面的活跃目标永远不能进入发现集，甚至在 128 绑定容量没有用满时也如此。

最小修复方向是**每次仍有界、跨次推进的扫描游标**：

- 每轮对新取得且持根的数组读取有界窗口，保存整数游标而非未持根的 native 指针；数组长度变化时归一化游标。
- 给 native 数组访问增加受验证的起点/窗口支持，或使用现有有类型的 `GetValue(index)` 调用。不要用 `ArrayItems(...).Skip(cursor)`，它仍会对被跳过的前缀执行全部原生读取，破坏每轮工作预算。
- 已绑定目标仍用当前独立存活/覆盖检查维护；未出现在本轮窗口不能判定销毁。
- 在稳定数组顺序下证明若干轮后覆盖尾部；若 Unity 每轮任意重排数组，简单索引游标不提供严格公平保证，需要声明该边界或增加对象去重/扫描周期设计。

验收应证明“活跃目标位于第 4097 项以后且容量有余时最终被发现”，同时证明每轮原始读取仍受预算限制。仅增加 `MaximumScanned` 不能消除此类饥饿。

## 3. 通用 registry 的跨实例代次碰撞

证据：

- `src/RizToolbox.Sdk/TargetContracts.cs:9` 承诺 `process-local, non-reused binding generation`。
- `src/RizToolbox.Core/Targets/SemanticTargetRegistry.cs:11` 的 `_generation` 是实例字段。
- 同文件约 50 行：每次注册使用 `checked(++_generation)`。
- `Find` 约 157 行：仅按 key、generation、value type 和当前对象存活检查，没有 registry/epoch 身份。

由代码可直接推导的最小复现（本次未运行）：

```csharp
// 使用同一个验证线程 dispatcher/logger。
var first = new SemanticTargetRegistry(main, log);
var old = first.Register("scene/object", () => 1).Value!;
first.Dispose();
using var second = new SemanticTargetRegistry(main, log);
var current = second.Register("scene/object", () => 2).Value!;
// old 和 current 的 Key、Generation 相同；当前都是 generation 1。
// 当前实现 second.Acquire(old) 成功，读到第二个绑定。
```

即使第一个 registry 未销毁，两个不同 registry 的第一次同键注册也会碰撞。原来的 lease 仍绑定原 owner，不会自动复活；缺陷在把旧 `TargetHandle` 交给新 registry 的 `Acquire` / `Invalidate` 等入口。

最小修复是由共享宿主为各 registry 提供非复用的代次分配器，或在同一 Core 加载域内使用非泛型共享计数器。不能继续使用每个 registry 的局部计数。分配须跨线程安全，达到上限后永久拒绝分配而不能回绕复用；保持现有 `TargetHandle<T>(Key, Generation)` ABI 即可。若未来允许多个 Core 加载域，应使用宿主统一分配器，不能把每个加载域自己的 static 误称进程全局。

验收：跨 registry 重建后旧 handle 的 Acquire/Invalidate/覆盖入口不能作用于新对象；同一 registry 重建仍保持现有拒绝行为。不要只断言“两次 generation 不等”，还应验证旧入口操作被拒绝且新对象值未被改变。

## 4. 已有身份保护与尚未证实的范围

- `VisualResourceService.Bind` 给规范键追加 `/binding/` 和随机 GUID；因此上述通用 registry 碰撞不会直接复现为当前 Visual 的同键碰撞。
- `NativeVisualBackend` 的候选身份组合 instance ID、pointer 和 slot；每个已绑定接收者通过 `ComponentRoot` 持根。服务每轮先检查旧绑定存活/资格，再发现新对象。
- 逻辑目标失效与 physical retirement 使用独立根所有权。读失败或场景卸载不再被当作 native 接收者已死亡的证明。
- `tests/RizToolbox.Visual.Tests/Program.cs` 约 89 行已有“旧对象死亡、同 identity 重建后旧目标不可写”的用例；约 392 行只验证初始容量。
- 该测试文件约 20–25 行只验证 `Prioritize(...).Take(128)` 的首次排序，没有覆盖服务层占满后的第二次发现。
- `tests/RizToolbox.Mutation.Tests/Program.cs:66` 的代次测试只在同一 Fixture/registry 内重新注册，没有跨 registry 验证。

后续若要测试 native 身份复用，应明确区分：可控假后端的同身份重建、真实 Unity instance ID 复用、真实托管接收者地址复用、资源缓存身份复用。不能把第一类通过写成后三类均已实测。当前没有足够证据要求新增猜测性的 native setter 或取消已有保守清理保护。

## 建议执行顺序

1. 先修容量分支的语义刷新及通用 registry 跨实例身份隔离；二者都是范围清楚的确定错误。
2. 实现有界窗口发现与明确的候选优先级/动态准入协议；覆盖所有 Visual kind 的统一策略，保留 native retirement 安全边界。
3. 再用上述行为级用例验收；运行时验证记录实际发现数、绑定数、延迟数、淘汰理由和扫描窗口，避免仅用测试数量宣布 B12 完成。

## 审计后的授权实施检查点

父任务随后授权立即修复第 3 项，要求不占用当前构建槽。本检查点仅修改：

- `src/RizToolbox.Core/Targets/SemanticTargetRegistry.cs`：共享非泛型 static 代次计数器，通过 `Volatile.Read` 与 CAS 分配；达到 `long.MaxValue` 永久返回 `TargetGenerationExhausted`，不先递增再处理回绕。注册失败不转移 lifetime。既有 metadata/alias 更新路径不分配新代次。
- `tests/RizToolbox.Mutation.Tests/Program.cs`：增加跨存活/已退休 registry 的旧句柄读取、语义修改、失效、覆盖隔离回归；增加多 registry 并行分配验证；最后执行上限故障注入，验证拒绝后没有回绕、没有根所有权转移、现存绑定仍可读取和更新 metadata。

新增后共 21 项目标/仲裁用例。本子任务未编译或运行，**不能宣称通过**。父任务负责串行红绿验证。修改前的 Core 单文件快照保存在：

`C:\Users\71957\AppData\Local\Temp\riz-toolbox-target-registry-before-cross-registry-fix-20261020.cs`

红阶段应使用新增测试配合该旧 Core 文件；绿阶段恢复当前 Core 文件。切换前须另存当前实现并确保没有其他构建并行。快照为本次实际修改前文件，不以 Git HEAD 替代当前工作区历史。

第 1、2 项动态准入与扫描窗口仍未实现，也未获得运行时验收。跨注册表修复不等于 B12 全部完成。
