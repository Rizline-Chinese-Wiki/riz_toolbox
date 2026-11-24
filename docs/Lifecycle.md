# 订阅当前游戏生命周期

本轮新增 `IGameLifecycleService`：插件一次订阅取得原子初始快照，再接收严格更晚的修订事件。场景和游玩观察器共享同一个 `GameEventState`，因此 SessionId、Revision、SceneGeneration 一致。最新 Game 70/70、SDK 兼容 13/13 为离线结果；[Retry 实机证据](validation/runtime-cancellation-20261020/README.md)与 [Back/Skip 实机证据](validation/runtime-back-skip-20261020/README.md)分别绑定各自实际安装包。**正常完成及完整 B01/B09 矩阵仍未验收。**

## 插件取得快照并继续订阅

以下代码放在 `IRizPlugin.Enable(IPluginContext context)` 内，仅依赖 SDK：

```csharp
var lookup = context.GetService<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle);
if (!lookup.Available || lookup.Service is null)
    throw new InvalidOperationException(lookup.Error?.Message);

var subscribed = lookup.Service.Subscribe(change =>
{
    context.Log.Log(LogLevel.Info,
        $"{change.Snapshot.SessionId}/{change.Snapshot.Revision} " +
        $"scene={change.Snapshot.SceneGeneration}:{change.Snapshot.Scene} " +
        $"kind={change.Kind}, run={change.RelatedRunId ?? "<unassociated>"}");
});
if (!subscribed.Success || subscribed.Value is null)
    throw new InvalidOperationException(subscribed.Error?.Message);

var initial = subscribed.Value.InitialSnapshot;
context.Log.Log(LogLevel.Info,
    $"Initial revision={initial.Revision}, phase={initial.Phase}, run={initial.RunId}");
```

服务调用、订阅与主动释放都在宿主验证的主线程进行。宿主必须登记 `GameEventObserver.ForPlugin(context)` 返回的作用域视图；视图和订阅自动归属本次启用，即使插件省略 `Own`，停用后也会失效和取消。回调通过拥有者的 EventHub 执行，异常停用故障插件，其他插件继续接收事件。后台线程只能使用已复制的 DTO。

`InitialSnapshot` 与订阅登记之间没有主线程交错；它不是再次发送的历史事件。假设初始修订为 N，回调仅接收 N 之后的修订。订阅中途启用、当前事件回调内新增订阅，以及嵌套事件发布均有回归；发布队列保留修订递增顺序。旧订阅的 `Dispose` 幂等。

## 理解开始、暂停和结束的证据

| 状态或事件 | 当前证据 | 不代表的行为 |
|---|---|---|
| RunStarted | 同一活跃 `LevelInformation` 的 `ProgressControl.isPlaying && timeReady` 实际成立后锁存 | 不以进入 Level 场景或 `levelBegan` 单独为开局依据 |
| 新的一局 | 新 `LevelInformation` 身份再次取得播放证据，生成新 RunId；同谱面也分开 | 相同信息对象暂时停止/恢复不能合成重试 |
| Paused / Resumed | 当前控制器自己的 `pauseUIControl._pausing` 经布尔元数据检查后读取；首次值只作基线 | 不能以音频停止、进度停止或 UI 名字猜测暂停；两次采样间的短变化可能丢失 |
| AwaitingResult | 当前局 `levelOver` 已观察为 true | 单独 over 还不能断言正常完成或失败 |
| RunEnded + Completed / Failed / Skipped，Result 非空 | 新的实际 `InsideLevelResult`；依据 `crashed`、`skip` 分类，保留原始计数 | 不以历史最佳成绩变化推断单局 |
| RunEnded + Skipped，Result 为空 | 固定构建原始 Skip 正常返回，当前控制器 `_skip=false→true`，同一 rooted PauseUI `enabled=true→false` | 是直接终止证据，不补造结算计数或实际结果 |
| ResultObserved | 新实际结果缺少可可靠关联的已观察开始 | Result.RunId 与 RelatedRunId 必须为 null；不能补造开始或随机 ID |
| ObservationLost | 控制器或状态暂时无法观测 | 场景切换、控制器消失不等于已主动退出 |
| Aborted | Retry 原信息 GameObject `activeSelf=true→false`；或已接受 Back 请求后，其同一 PauseUI 的原始 Update 返回时 `_terminated` 与控制器 `_skip` 均 false→true | 两条路径均有独立实机证据；其他退出不因对象消失或孤立标志自动变成取消 |

采样来源明确为 `SampledGameState`，名义间隔为 250 ms，`ObservedAtUtc` 是框架观察时间，不是原始游戏调用的精确时间。短于采样间隔的变化没有完整性保证。新 Retry 取消能力 `GameLifecycleCapabilityIds.RetryCancellation` 单列可用性；实际原生挂接标为 `VerifiedCallback`。它在 native 原调用成功返回时确认边沿，释放原生 roots 后将普通 token 排队，在下一次宿主 Update 的调度队列和采样前发布 RunEnded/Aborted。这样源 SDK 命令能先记入原局审计，旧 scope 中的待办仍会在 Pump 前取消。重复、旧局、旧修订、场景改变或模块撤回的 pending token 不重放。

该观察通过精确 Retry 机器入口，原调用始终执行一次；不是依赖全局 `runtime_invoke` 桥覆盖所有调用。retained context 无论原调用抛错、插件停用、观察失败或清理失败都独立释放；失败不补造成功取消。静态源证明及离线边界测试见 [取消研究与实现](validation/lifecycle-cancellation-20261020/)。

`GameLifecycleCapabilityIds.BackCancellation` 与 `SkipTermination` 是另外两个可选能力。Back 请求只保留当前 session/run/scene 与 rooted 对象关系；正常修订推进不会误撤请求，但真正 scene/run 变化会释放 pending。只有同一原始 Update 跨越双终止边沿才排队 `Aborted`。Skip 使用独立精确入口及真实 LevelOver 的 PauseUI 禁用副作用，排队 `Skipped`。二者直接拒绝原生已结束、已失败、已跳过状态，避免 250 ms 采样落后造成误归类；队列可接受的原因仅为 Aborted/Skipped，不能制造 Completed/Failed。下一宿主 Update 在 Pump/采样前提交，确保源 SDK 操作先记审计，同时旧局任务先于执行失效。

## 关联结果而不串局

新结果只有同时满足这些条件才使用当前 RunId：当前存在已观察开始且尚未终止；谱面不矛盾；并且当前已观察 over 或控制器不再存在。否则使用 `ResultObserved`，保留独立结果且不结束当前仍在播放的局。初始化时已有的结果对象只建立去重基线，不发布历史完成。

同谱面迟到结果若出现在新局仍在播放时，也明确不关联。若新局与旧局谱面相同，而且结果到达时当前已 over，仅凭本轮采样无法在所有竞态下证明结果对象属于哪一代局；当前公开契约不承诺解决缺少原生结果身份的任意竞态。这是完整 B09 后续精确接入仍须验收的边界。

结果对象消失再返回不会重复完成；重复 over 不重复开始。已结束后取消回调不再重复终止；已 over 时的取消线索留给真实结果分类，避免在同一末期重复发终止。

## 宿主登记与符号需求

宿主必须持有同一个 `GameEventState` 并传入场景和游玩两个 `GameEventObserver`。不能为两者各建一份状态，否则场景代次与局事件会分离。内层 state 的全局生命周期发布绑定到同一 EventHub，两个观察器不会重复发布。

模块登记方式如下；`registration` 是现有 `ModuleRegistration`，`observer` 是本模块实际创建并持有的游玩观察器：

```csharp
registration.RegisterScoped<IGameLifecycleService>(
    GameLifecycleCapabilityIds.Lifecycle, context => observer.ForPlugin(context));
```

模块 manifest 必须先声明 `GameLifecycleCapabilityIds.Lifecycle`，否则 `ModuleRegistration` 会拒绝未声明能力。不能把原始观察器直接当作插件视图返回。

| 必需程度 | 原生类型与字段 | 验证 |
|---|---|---|
| 开始观测必需 | `InLevel.LevelInformation.progressControl` | 类型必须为 `InLevel.ProgressControl` |
| 开始观测必需 | `InLevel.ProgressControl.isPlaying`、`timeReady` | 字段类型必须为 `System.Boolean`，实例读的静态性/大小由 Runtime 再校验 |
| 既有局观测必需 | 当前控制器 `LevelInformation`、`LevelParams`，结果控制器 `levelOverInfo`，信息 `levelOver` | 保留原有已适配读取与 Runtime 类型校验 |
| 暂停可选 | `NormalModeInLevelController.pauseUIControl` → `PauseUIControl._pausing` | 引用类型必须为 `PauseUIControl`，标志必须为 `System.Boolean` |

暂停符号缺少或实际对象未出现时，生命周期快照中的 `PauseAvailable=false`、`Paused=null`，`Limitations` 带 `ObservationUnavailable`；开始/结果可继续工作。控制器与对象失去有效观测时保持相同原则。旧 `IGameStateService.GetSnapshot` 与旧 `RunStarted`、`RunFinished`、`GameSceneChanged` 构造签名仍保留，只增添 init 元数据。旧 `RunFinished.RunId` 的可空注解不改变 CLR 构造签名；作者应处理未关联结果的 null。

## 原生生存期和诊断上限

候选 controller/result component、gameObject、信息、进度、元数据、谱面引用均在跨多个原生调用时持根。最近的 `LevelInformation` 和结果对象根跨采样持有，替换或观察器销毁时释放，避免根已释放后的地址复用直接冒充旧身份。GC 根不等于 Unity 原生对象永久存活；实际候选仍通过已适配的活跃/场景有效性读取检查。

候选日志缓存只保存诊断文字，不解引用缓存中的地址；固定最多 128 项。最近播放/控制器样本只保存各一项，重复值不重复记录。离线 10,000 个候选输入的回归检查证明缓存有界；这不是实际 Unity GC 或完整场景压力证据。

## Evidence → Finding → Path

| Evidence | Finding | Path |
|---|---|---|
| [先前 B01 实机会话](validation/B01-20261019-playback/) | 真实播放开始与失败结算可以取得同 RunId 关联；该证据来自当时已安装包 | 继续对新包复测正常完成、失败、重试、暂停恢复和提前退出，不把本轮离线变更称已安装 |
| [Game 检查](../tests/RizToolbox.Game.Tests/Program.cs) 70/70 | 覆盖播放锁存、信息身份、共享修订、暂停、未关联/迟到结果、原子订阅、嵌套顺序、作用域清理与故障隔离、缓存上限及 Back/Skip 原因/token/队列边界 | 结合对应包的冻结实机序列；仍不能替代正常完成和迟到结果来源验收 |
| [SDK 检查](../tests/RizToolbox.Sdk.Tests/Program.cs) 13/13 | 保留 SDK 1.1 公开签名和实际已编译插件运行 | 增量接口继续按可选能力查询；不能强制老插件实现新成员 |

可复现命令，在仓库根执行：

```powershell
dotnet run --project tests/RizToolbox.Game.Tests/RizToolbox.Game.Tests.csproj -c Release
dotnet run --project tests/RizToolbox.Sdk.Tests/RizToolbox.Sdk.Tests.csproj -c Release
```

B01/B09 仍保持未完成：新代码需要宿主注册和安装；采样暂停、重试、提前退出与完整实机序列仍须独立验收，确切取消挂接没有本轮实证。
