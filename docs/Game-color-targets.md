# 游戏颜色目标接入

本轮将 B08/B12 的通用目标与仲裁机制接入游戏真实 `UnityEngine.UI.Image.color`。实现位于 `Adapter/GameResourceService.cs`；`rizline.menu-resources` 模块统一创建 `SemanticTargetRegistry` 和 `MutationCoordinator`，并把同一实例提供给新旧插件 API。

## 一个对象，一条颜色通道

每个合格 Image 仅登记一次 `ResourceColor` 目标，修改属性名为 `color`。目标规范键形如 `game.ui/image/<instanceId>/binding/<opaqueId>/color`，`TargetHandle.Generation` 表示本次绑定代次。规范键是会话标识，不应保存在配置中跨启动使用；不透明绑定 ID 还避免模块恢复后旧句柄重新命中新注册表。

同一对象可同时具有 `game.ui/image/<instanceId>/color` 及 `game.menu/button/<pageIndex>/color` 别名。菜单晚就绪时通过 SetAliases 更新别名，不创建第二个覆盖栈，也不结束现有租约。不同对象拥有同一别名时，查询返回 `AmbiguousTarget`，不会任取一个；对象尚未出现返回 `TargetMissing`。重新创建的对象需要重新解析目标，旧句柄与租约不会自动迁移。

按作用域注册的接口：

| 能力 | 接口 | 实际用途 |
|---|---|---|
| `riz.targets` | `ITargetService` | 查询规范目标、元数据及别名，取得只读值租约 |
| `riz.mutations` | `IMutationService` | 为已登记目标的 `color` 属性请求优先级覆盖并查询持有者 |
| `riz.game-resources` 及两个操作别名 | `IGameResourceService` | 兼容旧目标列表和 OverrideColor，内部进入同一颜色通道 |

新旧 API 均使用实际插件 ID 作为持有者。服务及返回的租约归属插件启用作用域；停用后旧服务视图失效，已结束租约的 Dispose 幂等。一个旧 API 插件和两个新 API 插件可以在同一目标上共同仲裁。

当前真实颜色通道声明 `PriorityOverride`，最高 priority 生效，同优先级后注册者生效。通用 API 的 Transform/AcquireExclusive 对本颜色通道明确返回不支持，不会另建栈绕过策略。RGBA 只能为 0–1 的有限数值。

## 基线、对象存活和清理

适配器使用 `AdoptAndReapply`：持有期间发现游戏写入新颜色，将它作为当前基线并重新应用胜出层；释放最后一个租约恢复最新游戏基线。没有租约时只更新读到的基线，不写入。setter 后由协调器读回确认；setter 改值后抛异常时，仅回滚仍能证明属于该次候选的值。读回已经变为其他值时保留该值并拒绝成功声明。

生产后端只从加载的 Unity Image 中发现目标，要求对象存活、`isActiveAndEnabled` 为真、所属 `GameObject.scene` 有效且已加载，并排除根名称以 `RizToolbox.` 开头的工具 Canvas。资源模板通常没有有效的已加载场景，因此不会被当作游戏对象。该过滤与颜色原生读写集中在 `NativeGameColorBackend`，普通插件拿不到 IL2CPP 指针。

每次发现最多扫描 4096 个 Image，每个模块最多保留 512 个目标；发现间隔 5 秒，模块每秒检查已有目标及颜色协调。状态是采样观察，不保证同帧响应对象活跃切换。精确场景名称覆盖尚未声明，不能据此认为全部游戏场景均验收。

每个登记成功的目标只持有一个 IL2CPP root，读写时经 root 获取实际引用。对象销毁或场景卸载后先作废租约，后释放 root，不写入失效对象；仍存活但停用的对象会先释放通道及其仍拥有的颜色，再离开目标表。关闭模块时先释放 coordinator，再释放 registry。`MenuResourceModule` 会先撤回能力，令插件停止和清理，然后才释放这些对象。

## 检查及真实验证边界

顺序执行，避免多个项目并发写共享工程的 obj：

```powershell
dotnet run --project tests/RizToolbox.GameResources.Tests/RizToolbox.GameResources.Tests.csproj -c Release
dotnet run --project tests/RizToolbox.Module.Tests/RizToolbox.Module.Tests.csproj -c Release
```

GameResources 的 18 项检查通过生产 `GameResourceService` 的窄后端接口提供可控 Image 值，覆盖三个真实 PluginHost 作用域的旧/新 API 同通道、别名晚出现和歧义、游戏写入基线、销毁/重建、root 次数、读回不符、setter 中途失败、目标配额和停用后缓存失效。Module 的 26 项检查另包含 scoped factory 接收真实插件 ID、能力撤回和恢复代次。

这些证据证明适配组合路径和清理逻辑可执行，不等于原生 Unity 行为已全部验收。本轮未安装或启动游戏，三插件实机共存、实际场景销毁/重建、原生 root 释放与游戏动画持续写入仍需实机证据。本接口只覆盖颜色；其他纹理、Sprite、音频、AssetBundle 和玩法控制不在本次范围。
