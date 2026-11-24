# 语义目标与修改协调

本模块为 B12 提供通用目标身份与租约，为 B08 提供可逆属性仲裁。它不会扫描 Unity 场景、直接定位组件或为任意玩法命令生成撤销操作。实际游戏接入由 Adapter 登记经过验证的目标及读写回调；通用检查通过不等于所有游戏对象已经适配。

生产宿主由独立 `SemanticTargetsModule` 持有共享注册表和协调器，空注册表也可提供服务。菜单颜色与[游戏轨道/音符颜色](Game-appearance.md)分别发现、刷新和释放自己的目标；任一发现模块撤回，不会清掉另一模块的通道或基线。具体外观能力仍有自己的构建、角色和局身份约束。

## 适配器登记一个目标

`SemanticTargetRegistry` 和 `MutationCoordinator` 在主线程构造、使用和释放。下面是完整的纯托管接入示例；将读写回调替换为适配器已验证的字段访问即可接入游戏。

```csharp
using RizToolbox.Core;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Sdk;

using var main = new MainThreadDispatcher();
ILogger log = new ConsoleLogger();
using var targets = new SemanticTargetRegistry(main, log);
using var mutations = new MutationCoordinator(targets, main, log);

var actualColor = new ResourceColor(.2f, .3f, .4f);
var bound = targets.Register<ResourceColor>(
    "game.menu/primary-accent",
    read: () => actualColor,
    isAlive: () => true,
    metadata: new TargetMetadata(Scene: "MainMenu", Role: "primary-accent"),
    aliases: new[] { "theme.primary-accent" });
if (!bound.Success) throw new InvalidOperationException(bound.Error?.Message);

var registered = mutations.Register(bound.Value!, "color", value => actualColor = value,
    strategy: MutationStrategy.PriorityOverride,
    externalWrites: ExternalWritePolicy.AdoptAndReapply,
    validate: value => new[] { value.R, value.G, value.B, value.A }
        .All(v => float.IsFinite(v) && v >= 0 && v <= 1) ? null : "RGBA channels must be in [0, 1].");
if (!registered.Success) throw new InvalidOperationException(registered.Error?.Message);

var channel = registered.Value!;
var first = channel.Override("plugin.a", new ResourceColor(1, 0, 0), priority: 10);
var second = channel.Override("plugin.b", new ResourceColor(0, 1, 0), priority: 20);
if (!first.Success || !second.Success) throw new InvalidOperationException(first.Error?.Message ?? second.Error?.Message);
// plugin.b 的绿色生效；移除后恢复 plugin.a 的红色，再移除恢复基线。
second.Value!.Dispose();
first.Value!.Dispose();

sealed class ConsoleLogger : ILogger
{
    public void Log(LogLevel level, string message, Exception? exception = null)
        => Console.WriteLine($"[{level}] {message} {exception}");
}
```

真实对象的 GC/root 句柄通过 `lifetime:` 传入登记。登记成功后，registry 接管它；登记失败时调用方仍须释放。`isAlive` 先判断对象是否有效；失效时 registry 先通知协调器撤销租约，再释放该 root。作废过程不执行失效对象的属性写入。先释放 coordinator、再释放 registry，可对仍然存活且仍属于本模块的值进行恢复。

`TargetHandle<T>` 只有规范键和单调递增的代次。相同语义键重新登记会产生新代次，旧租约不能操作重建对象；原生地址或 Unity 实例 ID 即使复用也不会使旧租约恢复。`T` 应是普通不可变快照，例如 `ResourceColor`；不应登记原生指针、可变游戏对象或会被插件就地修改的数组。

## 查询、别名与重建

`TargetQuery` 可组合精确的 Key、Scene、Role、ContentId、ResourceId。默认排除模板和非活跃对象。registry 只查已登记元数据，适配器负责统一、限频发现和更新，避免每个插件各扫场景。

| 匹配 | 结果 | 调用方处理 |
|---|---|---|
| 唯一 | `TargetMatchKind.Unique` 与规范 `TargetHandle<T>` | 获取租约或请求修改 |
| 没有 | `Missing`，错误含 `TargetMissing` | 等待下一次适配器发现后重新查询 |
| 多个 | `Ambiguous`，错误含 `AmbiguousTarget` 与候选描述 | 增加场景、角色等约束，不能取第一个 |

别名总是解析为同一规范键和代次，因此同一对象经别名请求修改会进入同一仲裁通道。不同对象可以声明相同别名，此时查询明确返回歧义。`SetAliases(handle, aliases)` 可在菜单延迟出现时追加语义别名，不会结束现有租约；它替换完整别名集合。`UpdateMetadata(handle, metadata)` 可更新同一对象的活跃状态等属性。对象身份变化必须 `Invalidate(oldHandle)`，再 `Register`，不能只改元数据掩盖重建。

`InvalidateScene(scene)` 作废当前登记到该场景的所有对象。持续绑定的宿主逻辑应在下一次唯一匹配后重新获取句柄和修改租约。本模块不自动把旧租约迁移到新对象。

## 提供插件服务

适配器可在能力表中注册按作用域创建的服务：

```csharp
// capabilities、targets、mutations 是宿主持有的实例。
capabilities.RegisterScoped<ITargetService>(TargetCapabilityIds.Targets,
    context => targets.ForPlugin(context));
capabilities.RegisterScoped<IMutationService>(TargetCapabilityIds.Mutations,
    context => mutations.ForPlugin(context));
```

`ForPlugin` 从 context 获取实际插件 ID，插件不能冒用其他持有者；每次调用和缓存句柄操作都检查 context 主线程与活跃作用域。返回的目标/修改租约直接交给 `IPluginContext.Own`，插件省略 `Own` 也会随停用释放。Dispose 幂等，清理允许在作用域已失效后执行。

```csharp
// 以下代码位于 IRizPlugin.Enable(IPluginContext context) 内。
var targetService = context.GetService<ITargetService>(TargetCapabilityIds.Targets);
var mutationService = context.GetService<IMutationService>(TargetCapabilityIds.Mutations);
if (!targetService.Available || !mutationService.Available)
    throw new InvalidOperationException(targetService.Error?.Message ?? mutationService.Error?.Message);

var match = targetService.Service!.Resolve<ResourceColor>(new TargetQuery(Role: "primary-accent"));
if (match.Kind != TargetMatchKind.Unique)
    throw new InvalidOperationException(match.Error?.Message);

var result = mutationService.Service!.Override(
    new MutationKey<ResourceColor>(match.Target!, "color"), new ResourceColor(1, .2f, .4f), priority: 10);
if (!result.Success) throw new InvalidOperationException(result.Error?.Message);
// result.Value 已自动归属当前启用作用域。
```

## 为属性选择修改策略

每个“规范语义目标、对象代次、属性名”只能登记一个通道。策略由适配器选定，插件请求不同策略会得到 `MutationNotSupported`，不能用覆盖栈绕过独占属性。

| 策略 | 仲裁 | 典型场景 |
|---|---|---|
| `PriorityOverride` | 最高优先级生效；同优先级后登记者生效 | 颜色、已验证的显示属性 |
| `OrderedTransform` | 按 order 升序、相同 order 按登记顺序，依次变换当前基线 | 可证明可组合的纯数据变换 |
| `Exclusive` | 一次只允许一个租约；冲突返回 `Conflict` 与当前所有者 | 不宜同时控制的可逆状态 |

变换必须纯粹，不修改输入、调用游戏代码或在计算过程中注册新修改。变换抛异常或输出未通过适配器校验时，该提供者租约被结束，其他变换重新计算；错误通过 `ProviderFaulted(owner, exception)` 和可选 `onFault` 上报。宿主可用该回调连接插件故障处理。普通无效候选请求被拒绝；`SetValue` 失败保留原候选。模板代码没有宣称变换原生参数或返回值。

`MutationSnapshot<T>` 提供 Baseline、Effective、每个所有者、候选值、优先级、登记序号、胜出理由和最后错误。变换链中每个仍有效的提供者均参与输出，Candidate 为该层变换后的快照。独占冲突也列出所有者。

## 游戏自身写入时如何处理

适配器在统一主线程 tick 中调用 `channel.Refresh()` 或 `coordinator.RefreshAll()`。读取实际值与本通道上次确认写入值比较，用以下明确策略处理游戏写入：

| 策略 | 持有期间的游戏写入 | 租约结束 |
|---|---|---|
| `AdoptAndReapply` | 新值成为基线，再计算当前修改 | 恢复最新游戏基线 |
| `PreserveExternalAndInvalidate` | 保留游戏新值并结束当前全部租约 | 不再写入；通道可接收新请求 |
| `RejectExternalWhileHeld` | 明确独占属性值，重申当前修改 | 只在实际值仍为本通道上次写入时恢复原基线 |

没有持有者时，Refresh 只更新基线，不写入。最后一个租约恢复基线后即放弃写入归属，之后游戏自身的修改不会被旧通道覆盖。

适配器写入后必须读取验证。若写回调先修改再抛异常，仅当读取证明实际值仍等于本次请求值时才回滚到调用前值；若读取已经变成其他值，则结束通道租约，保留该外部值。对象失效也直接结束租约，不尝试盲写恢复。无法撤销的命令不应登记为本通道属性。

## 检查与验收边界

运行：

```powershell
dotnet run --project tests/RizToolbox.Mutation.Tests/RizToolbox.Mutation.Tests.csproj -c Release
```

该程序当前包含 18 项目标/仲裁检查，覆盖同名歧义、多场景、模板/活跃筛选、别名、代次重建、三个插件交错启停、相同优先级、纯变换与故障移除、独占冲突、三种游戏基线策略、写入中途失败、失去写入归属、作用域清理、root 释放顺序、线程和重入拒绝。

证据链：上述检查使用可控托管属性与计数 root，直接执行 registry 和 coordinator 的生产实现。Adapter 现已把真实 Image.color、菜单别名和旧 IGameResourceService 接入同一注册表与仲裁通道，见 [游戏颜色接入](Game-color-targets.md)。该接入的 18 项离线检查验证生产适配组合与三个插件作用域共存；实际场景销毁、原生 root 和三插件真实游戏写入仍需独立实机证据，不能用托管检查代替。
