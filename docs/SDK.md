# 编写插件包

当前开发版本采用框架 0.3.0 和 SDK 1.4.0。使用作者包创建面向 `net6.0` 的插件，编译时引用同包的 `RizToolbox.Sdk`。入口类实现 `IRizPlugin`，并提供公共无参构造函数。插件类与 `plugin.json` 使用相同的 ID、版本和 SDK 范围。

新作者优先从 [引用包与已验证模板](Authoring.md) 开始。构建产出 ref-only NuGet 包，模板只复制插件 DLL 和清单；不携带宿主 DLL。各能力的实现/离线/实机状态见 [验收状态](IMPLEMENTATION-STATUS.md)，不能仅因为接口出现在 SDK 中就假定游戏支持。

SDK 1.4.0 可按需读取 `LevelList` 中卡片当前选中的官方谱面。可核实的原版 Play 按钮状态单独返回。 [读取条件和返回字段](Ui-input-navigation.md#读取选曲页的当前官方谱面)

SDK 1.3.0 新增了三处可直接用于插件界面的信息：

- 显示曲目目录时，读取 `LocalSongChartSnapshot.HasPlaybackContent` 判断该版本是否有可播放的谱面数据。
- 玩家点击某个目录项时，把当时显示的曲目快照和谱面 ID 传给 `ILocalCatalogSelectionService.SelectRevision`。曲目在此期间更新时，按返回的错误刷新目录。实际开局看选中项的 `RequestPlay` 回执。
- 显示游玩控制按钮时，读取该动作的 `GameplayControlAvailability.StateReadiness`。有成功的就绪采样时可以启用按钮，采样失败时可以显示具体原因；空值表示尚无该项采样。执行时仍需处理场景或控制器变化。

r16 作者包已使用包内离线源编译模板。模块和接口的实机验收进度见[当前交付记录](../.docs/CURRENT-CHECKPOINT.md)。

子作用域使用见 [作用域与取消](Scopes.md)，会话诊断见 [诊断文档](Diagnostics.md)。接口存在不等于当前游戏提供服务；应查询具体 capability 和失败原因。[早期兼容研究](SDK-compatibility.md)保留其历史证据，不作为未发布阶段的合同冻结承诺。

游戏角色的颜色接入见[轨道与音符外观](Game-appearance.md)，近期采样区间内的时间转换见[玩法时间](Gameplay-primitives.md)。外观修改使用共享目标和覆盖租约，时间转换受当前局与采样区间约束；具体支持范围与实机验收分别列出。

当前程序集绑定版本为 `1.0.0.0`；这是当前构建值，不是历史 API 或二进制兼容承诺。清单按实际 `SdkInfo.Version` 和范围执行加载检查。开发时使用与当前候选一致的 SDK 和目录包格式；接口调整后重新编译并核对清单。验收应证明当前插件正常加载，并对错误 SDK 范围、共享类型身份、服务合同或包格式给出清楚诊断与拒绝；版本号相同不能替代合同验证。

`OperationResult` / `OperationResult<T>` 的 `Error` 是 `OperationError(Code, Message, CapabilityId, TargetId, Retryable)`。失败分支按 `Error.Code` 或 `KnownCode` 处理；消息用于显示，不解析消息前缀。`context.GetService<T>`、跨插件 Lookup/Watch 和目标查询也返回同一错误对象。工厂、状态约束和完整代码目录见 [操作错误](Operation-errors.md)。当前合同没有文本 `Fail` 或旧结果构造重载；使用这一候选的 SDK 重新编译插件。

## 从面板插件开始

```csharp
using System;
using RizToolbox.Sdk;

namespace Wiki.Hello;

public sealed class HelloPanel : IRizPlugin
{
    public PluginManifest Manifest => new(
        "wiki.hello", new Version(0, 2, 0), new Version(1, 4, 0),
        MaxSdkExclusive: new Version(2, 0, 0),
        RequiredCapabilities: new[] { CapabilityIds.Ui });

    public void Enable(IPluginContext context)
    {
        var ui = context.GetService<IUiService>(CapabilityIds.Ui);
        if (!ui.Available) throw new InvalidOperationException(ui.Error?.Message);
        var created = ui.Service!.CreatePanel(new PanelDefinition(
            "main", "Hello", "A plugin using the shared SDK"));
        if (!created.Success) throw new InvalidOperationException(created.Error?.Message);
        // 面板已由宿主自动登记，Disable 后自动释放。
    }

    public void Disable() { }
}
```

宿主将本地面板 ID `main` 变为 `wiki.hello/main`。其他插件可使用相同本地 ID。返回的 `IPanelHandle.Id` 为完整 ID，图标资源键为 `ui.panel/<完整ID>/icon`，本例为 `ui.panel/wiki.hello/main/icon`。

把入口 DLL 和清单放入独立目录：

```text
BepInEx/RizToolbox/mods/wiki.hello/
  plugin.json
  Wiki.Hello.dll
  私有依赖 DLL（如有）
```

`plugin.json` 使用以下平铺字段，属性名区分大小写，入口类型含完整 namespace：

```json
{
  "id": "wiki.hello",
  "version": "0.2.0",
  "entryAssembly": "Wiki.Hello.dll",
  "entryType": "Wiki.Hello.HelloPanel",
  "minSdk": "1.4.0",
  "maxSdkExclusive": "2.0.0",
  "dependencies": [],
  "requiredCapabilities": ["riz.ui"]
}
```

清单必须与类的 `Manifest` 一致，包括版本、SDK 范围、依赖和能力。`entryAssembly` 是包内相对路径，子目录用 `/`，禁止绝对路径和 `..`。未知/重复 JSON 属性、缺失字段、重复插件 ID、非法入口会产生包诊断。ID 使用小写 ASCII 字母、数字、点、下划线和连字符，最多 128 字符。

依赖另一个插件时，JSON 与代码声明相同范围，例如：

```json
"dependencies": [
  { "id": "wiki.base", "minVersion": "0.2.0", "maxVersionExclusive": "1.0.0" }
]
```

代码对应 `Dependencies: new[] { new PluginDependency("wiki.base", new Version(0, 2, 0), new Version(1, 0, 0)) }`。插件依赖控制版本与生命周期，不共享该插件的私有程序集。

每包有独立加载上下文，私有依赖只从本包解析，允许不同包携带同名不同版本库。宿主共享 SDK，其程序集身份必须与编译引用精确匹配；包内不要复制 SDK、Core、Loader、Runtime、Adapter、Hosting 或 .NET 框架 DLL。私有 DLL 的名称、版本与引用必须相符。旧平铺 DLL 不加载，修改 DLL 后重启游戏。

## 生命周期、线程与清理

`Enable`、`Disable`、事件和扩展点回调在 Unity 主线程执行。宿主检查 SDK、依赖版本、依赖环和必需能力，按依赖顺序启用、逆序停用。状态包含 Disabled、Waiting、Enabled、Faulted，`status.json` 还提供 `DesiredEnabled` 和原因。

能力未就绪时保留启用意愿并等待，能力出现后任意满足条件的插件都能启动。能力撤回会停用依赖者并清理作用域；用户明确停用后不会被能力恢复自动启动。同步故障进入 Faulted，需显式 enable 重试。启用失败只回滚该次新启动的依赖。

`GetService<T>` 返回绑定启用作用域和能力代次的服务视图。面板、纹理/颜色覆盖及扩展点订阅自动归属，省略 `Own` 仍会清理。`context.Events.Subscribe<T>` 也自动取消订阅。插件自建 `IDisposable` 可交给 `context.Own(...)`，重复登记同一对象不会重复归属。

停用先令作用域失效和取消排队工作，再逆序释放资源；旧服务、句柄和回调不能继续修改游戏。重新启用创建新作用域并重新获取服务。主动提前 Dispose 在主线程执行。

后台处理先复制普通 DTO，通过 `context.MainThread.InvokeAsync(...)` 返回主线程访问游戏。队列有上限，停用后排队请求取消或失败；主线程不可同步等待队列中的任务。避免 `async void`；后台任务异常和取消由插件自行处理。

## 插件配置

配置只访问当前插件目录，仍需有效作用域和主线程：

```csharp
public sealed record Options(string Greeting = "Hello");

// 在 Enable 或其他主线程回调内：
var loaded = context.Configuration.Load("settings", () => new Options());
if (!loaded.Success) throw new InvalidOperationException(loaded.Error?.Message);
var options = loaded.Value!;
var saved = context.Configuration.Save("settings", options);
if (!saved.Success) throw new InvalidOperationException(saved.Error?.Message);
```

本例写入 `BepInEx/RizToolbox/config/plugin-wiki.hello/config-settings.json`。配置名为 1–64 位 ASCII 字母、数字、连字符或下划线，首位必须为字母或数字；不要传路径或扩展名。文件不存在时返回默认值，非法 JSON、null 内容和超过 1 MiB 的文档返回错误。保存采用临时文件与原子替换。

## 服务和能力

`GetCapability(id)` 提供状态和原因，`GetService<T>(id)` 使用对应 SDK 接口，不请求实现类。能力撤回或替换使旧服务视图失效，应重新获取。

| 接口 | 能力 | 范围 |
|---|---|---|
| `ICatalogService` | `riz.catalog` | `GetCharts()`：谱面/歌曲 ID、名称、难度等级和数值评级 |
| `IScoreService` | `riz.scores` | `GetBestScoresSnapshot()`：历史最佳成绩，未知 PlayedAt 为 null |
| `IUiService` | `riz.ui` | 创建面板，返回句柄支持 SetText/Dispose |
| `IResourceService` | `riz.resources` | 已注册工具面板图标 PNG 覆盖 |
| `IGameResourceService` | `riz.game-resources` | 真实游戏 Image 颜色目标与覆盖 |
| `IGameStateService` | `riz.scenes` 或 `riz.runs` | `GetSnapshot()`：Scene、RunId、ChartId、IsPlaying |
| `IExtensionService` | `riz.extensions` | 查询和订阅准确版本、负载类型的观察点 |
| `IDataSnapshotService` | `riz.data.read` | 修订、分页和取消；[数据实际证据](validation/foundation-runtime-20261019/README.md) |
| `IGameLifecycleService` | `riz.lifecycle.observe` | [统一会话、局和场景修订](Lifecycle.md)，不补造历史开始或取消 |
| `IHookService` | `riz.hooks.observe` | [精确只读方法观察](Exact-hooks.md)，和采样事件区分 |
| `IGameplayClockService` / `IGameplayControlService` | `riz.gameplay.clock` / `riz.gameplay.controls` | [独立时间源和实验控制](Gameplay-primitives.md)，未验证时间映射不冒充秒 |
| `IRunModificationService` | `riz.gameplay.modifications` | [有界只读修改记录](Run-modifications.md)，未知关联保留 null |
| `IVisualResourceService` | `riz.visual.sprites` / `.fonts` / `.materials` | [独立视觉模块](Visual-resources.md)，材质参数仍未开放 |
| `IOwnedUiService` / `IInputActionService` / `IGameNavigationService` | `riz.ui.windows` / `riz.input.actions` / `riz.navigation.menu` | [自有 UI、框架输入和受限导航](Ui-input-navigation.md) |
| `IOfficialChartSelectionService` | `riz.navigation.official-chart-selection` | [按需读取选曲页中的当前官方谱面和 Play 按钮状态](Ui-input-navigation.md#读取选曲页的当前官方谱面) |
| 音频与本地内容接口 | `riz.audio.*` / `riz.content.*` | [具体格式、替换点和限制](Audio-content.md)，非任意新曲包加载器 |
| `IGameSettingsReadService` / `IGameSettingsOverrideService` | `riz.settings.read` / `.override` | [实际 getter 与 slider 范围租约](Game-settings.md) |
| `IGameSettingsChangeService` | `riz.settings.changes` | [可选初始快照与设置修订订阅](Game-settings.md#订阅完整设置修订)，一秒采样，作用域/provider 代次失效 |
| `ILocalCatalogProviderService` | `riz.content.local.catalog` | [精确资源修订的 LOCAL 歌曲登记](Audio-content.md#登记-local-曲目并描述播放-metadata)；可选 Playback 为作者声明，不授播放权限 |
| `ILocalCatalogSelectionService` | `riz.content.local.selection` | [选择与 RequestPlay](Audio-content.md#从当前选择请求播放)；正式组合已接线，生产首次执行准入缺失仍拒绝 |
| `IPluginStorageService` / `IPluginLocalizationService` | `riz.plugin-storage` / `riz.localization` | schema 配置、Data/Cache、独立语言表 |
| `IPluginServices` | `riz.services` | [跨 ALC 共享契约](Inter-plugin-services.md) |
| `IPerformanceService` | `riz.performance.read` | [有界回调窗口](Performance.md)，不是整帧或 GPU 时间 |

数据按谱面 ID 关联，不按显示歌名合并。RawCompletion 保留游戏原值，不预设百分数单位。`riz.practice` 当前不可用。

## 强类型观察点

```csharp
// 在两份 Manifest 中声明 riz.extensions、riz.runs。
var lookup = context.GetService<IExtensionService>(CapabilityIds.Extensions);
if (!lookup.Available) throw new InvalidOperationException(lookup.Error?.Message);
var subscription = lookup.Service!.Subscribe(GameExtensionPoints.RunFinished, result =>
{
    context.Log.Log(LogLevel.Info,
        $"Run {result.RunId}: chart={result.ChartId}, score={result.Score}, miss={result.Miss}");
}, priority: 10);
if (!subscription.Success) throw new InvalidOperationException(subscription.Error?.Message);
// 订阅已自动归属启用作用域。
```

`GetAll()`、`Describe(id)` 查看适配器实际注册的观察点。ID、Version 和负载类型必须匹配；不存在或不匹配返回失败。高优先级先调用，同优先级按注册顺序。同步异常归属到插件，由宿主停用，其他订阅继续执行。

| 声明 | ID / 版本 | 负载 |
|---|---|---|
| `GameExtensionPoints.SceneChanged` | `game.scene.changed` / 1.0.0 | `GameSceneChanged(PreviousScene, Scene)` |
| `GameExtensionPoints.RunStarted` | `game.run.started` / 1.0.0 | `RunStarted(RunId, ChartId)` |
| `GameExtensionPoints.RunFinished` | `game.run.finished` / 1.0.0 | `RunFinished` 单局分数、完成率及各判定计数 |

本批每 250 ms 读取实际活跃场景、当前 LevelInformation 对应的 ProgressControl `isPlaying && timeReady` 播放标记和结果屏幕的新 `InsideLevelResult`。仅真正观察到该播放标记后生成 RunId；`levelBegan` 单独不足以证明开局。控制器销毁后保留关联直到可靠结束或后续新游玩。初始现成结果只建立基线，不重放完成事件；无法取得的 ChartId 保持 null。

Perfect、MaxPerfect 分别读取游戏各字段，不合并为历史成绩中的 Hit；Bad、Miss、Crashed、Skipped 同样复制单局值。采样不能保证捕获两次观察之间的短暂状态，不支持修改参数、跳过原方法或替换返回值。`FrameUpdate` 由 Unity tick 发布，ElapsedSeconds 是单调时间。旧 `RunCompleted` 保留但不发布。

开局、结算与重试需真实游玩验收，离线状态机或菜单启动不能替代该项验证。

## 真实游戏颜色资源

在两份清单中声明 `riz.game-resources`。就绪后可枚举游戏自身的 Image 目标，工具创建的 UI 会被排除：

```csharp
var lookup = context.GetService<IGameResourceService>(GameCapabilityIds.Resources);
if (!lookup.Available) throw new InvalidOperationException(lookup.Error?.Message);
var targets = lookup.Service!.GetTargets();
if (!targets.Success || targets.Value!.Count == 0)
    throw new InvalidOperationException(targets.Error?.Message ?? "No game menu target is available.");
var applied = lookup.Service.OverrideColor(new GameColorOverrideRequest(
    targets.Value[0].Key, new ResourceColor(0.2f, 0.7f, 0.9f, 1), Priority: 10));
if (!applied.Success) throw new InvalidOperationException(applied.Error?.Message);
```

目标包括会话内的 `game.ui/image/<instanceId>/color`，游戏主菜单控制器就绪时还提供 `game.menu/button/<pageIndex>/color` 别名。同一对象所有别名共享一个覆盖栈；Kind 是 `unity.image.color`。请枚举目标而非跨启动保存实例 ID。高优先级生效，同优先级后注册者生效。释放恢复下一层，全部释放恢复捕获的颜色基线。覆盖句柄自动清理，也可提前 Dispose。未被覆盖的目标不会在停用时回写旧颜色。RGBA 必须为有限的 0–1 数值。

Adapter 检查对象存活并写后读回；场景目标销毁后旧覆盖失效，重新发现的对象建立新目标和基线。插件应处理目标不可用，不假定任意场景都有资源。`example.color-low`、`example.color-high` 两个独立包用于检验同目标覆盖与逐层恢复。

工具面板图标仍调用 `IResourceService.OverrideTexture`，使用完整面板 ID 对应的键。只支持 RGB/RGBA8、非交错 PNG，每边不超过 64 像素，并校验长度、CRC 和像素量；不承诺任意游戏纹理、音频或 AssetBundle 替换。

## 适配和验收

宿主以 `profiles/*.json` 的 GameAssembly 指纹选择已验证构建。Runtime 集中桥接，Adapter 维护游戏类型、字段、签名和能力。可选符号缺失关闭对应能力，关键符号失败停止适配。当前只支持已核验的 Windows x64 构建，更新后需重新核验。

开发时先检查包诊断、能力和 DesiredEnabled，再检查生命周期日志。当前合同验收至少包括同一候选 SDK 编译插件正常运行、错误合同拒绝、能力晚就绪、停用后旧服务/回调失效、不同私有依赖共存，以及两个独立包覆盖同一真实目标并依次释放。游戏版本指纹、真实 ABI、线程和资源生命周期检查仍须执行。真实开局、结算和重试另行运行游戏验证，结果记录到任务清单和证据，不能以接口存在判定通过；历史兼容证据不替代当前合同的验证。
