# 游戏轨道与音符颜色

`IGameAppearanceService` 通过 `riz.game.appearance` 提供当前局的具体颜色目标，复用宿主的 `ITargetService` 和 `IMutationService`。这条能力独立于菜单 UI。场景里没有符合条件的对象时，服务仍可查询，目标列表为空。

当前定义了四个实验属性。各属性独立检查其元数据和原生访问器；`GetSupportedProperties()` 只返回检查通过的项。

| Role 常量 | 对象及属性 | 实际读写路径 |
|---|---|---|
| `GameAppearanceRoles.TrackStartColor` | 可见轨道段的起点色 | 游戏 `LineInformation` setter 写缓存及当前池对象，读回实际 `LineRenderer.startColor` |
| `GameAppearanceRoles.TrackEndColor` | 同一轨道段的终点色 | 对应游戏 setter 与实际 `LineRenderer.endColor` |
| `GameAppearanceRoles.TrackRingColor` | 活跃轨道组的判定环色 | 游戏 `LinesGroupInformation.set_color` 同时写 `+0x88` 缓存及池内 `SpriteRenderer.color`；两处同时读回 |
| `GameAppearanceRoles.NoteRendererColor` | 活跃音符的具体渲染器颜色 | 当前音符的 renderer/pool-child 图中，实际类为 `SpriteRenderer` 的 `color` |

固定构建的 dump 在 `LineRenderer` 中只给出了起点色、终点色 setter。两项轨道段属性仍要求解析到各自准确的 getter；缺失时查询该 Role 会返回 `OperationUnsupported`，音符和环色继续独立提供。缓存值不代替实际渲染器的读回。

一个音符可能有多个渲染器，一局也有多个轨道段及判定环；只按 Role 发请求通常存在多个匹配。先查询并选择具体目标，再用其 canonical key 覆盖。

```csharp
using RizToolbox.Sdk;

static Result<IMutationLease<ResourceColor>> Tint(
    IGameAppearanceService appearance, GameAppearanceTarget selected,
    ResourceColor color, int priority = 0)
{
    return appearance.OverrideColor(new(
        new TargetQuery(Key: selected.Target.Key), selected.RunId, color,
        Priority: priority, SceneGeneration: selected.SceneGeneration));
}
```

在插件清单声明 `GameAppearanceCapabilityIds.Appearance`，再通过 `context.GetService<IGameAppearanceService>` 获取当前服务。返回的覆盖租约已属于该插件作用域；提前 `Dispose()` 可撤去这一层，插件停用也会自动释放。更高优先级的层生效，移除后恢复下一层或已确认的外部基线。RGBA 必须是 `[0,1]` 内的有限值。

`examples/RizToolbox.Examples/RingColorRuntimeProbe.cs` 提供可选的单环操作窗口。`example.panel` 的 `runtime-module-proof` 与 `ring-color-proof` 两份配置均设为 `{"Enabled":true}` 后才启用。窗口先显示真实的当前目标数量；点击 Next 查看候选或输入完整 canonical key，再点击 Select，输入明确的 `r,g,b,a` 后 Apply。Release 释放本示例的覆盖层；Read 使用所选对象的准确代次读取实际颜色和共享持有者。换场景、隐藏窗口或停用示例会释放租约。缺失、过期及不支持等结果显示结构化错误码与能力、目标标识。默认关闭时不会创建这个窗口。

目标同时绑定 RunId、场景代次及独立对象代次。重试、换场景、池对象替换、原生引用图变化会使旧目标失效；重新查询才能取得新目标。一次覆盖不会自动跟随下一局。暂停时可操作仍然属于当前局的目标。缺失、歧义、目标过期和作用域结束分别返回结构化错误，不需要解析消息文本。

环目标还要求游戏缓存与实际 `SpriteRenderer.color` 当前一致。覆盖、读回和释放租约都同时检查这两处；setter 仅完成一侧时，在当前局及池归属仍有效的条件下撤回已确认的改动。游戏同时改变两处时，新颜色可成为外部基线；任一处的独立改动导致颜色分离时，已有租约失效并保留两处观测值。颜色再次一致后可重新查询目标。

宿主在每次原生读写前，核对实际 controller/information 与观察到的局，再核对存活、场景、数组成员和池归属。这样可以拒绝在两次生命周期采样之间出现的替换对象。共享注册表存在不表示任意对象都能写入：每条通道都由具体适配器登记，UI 资源接口也只能访问自己的目标。

环和音符若意外指向同一个 `SpriteRenderer.color`，会按同一物理属性冲突处理，避免两个语义通道同时仲裁它。轨道组缓存也始终随该环目标一起验证和恢复。

当前实现固定于 Steam 构建 25977441 的 Windows x64 文件及字段/方法合同。环 setter 使用已有完整 176 字节静态记录 `3FB990` / SHA-256 `4b07ecf2e6d210f7702cd2952f0f842d0ff33aaf50bc35c0a1bc4178e10c3c6e`，加载后继续核对文件哈希、MethodInfo RVA 和方法体。此前外观服务的[详细来源与验证](validation/game-appearance-run-guard-20261030/2026-10-30_game-appearance-report.md)不代替新增环通道的验证。新增受控检查位于 `tests/RizToolbox.GameAppearance.Tests/NativeRingColorChecks.cs`。真实游戏内的可见效果和恢复仍待实测。相机标量、后处理及新贴图/皮肤资源不属于这些颜色属性。
