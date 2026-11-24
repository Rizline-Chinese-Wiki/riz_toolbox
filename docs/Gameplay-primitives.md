# 游戏时钟与实验控制

**实机已验证同一首官方谱的暂停和恢复。变速和跳转仍待实机验收。** r12 在《Pastel Lines》EASY 开始后，将 `SetSpeed` 和 `Seek` 的状态从等待开局更新为等待一次新的暂停。开场倒计时未结束时，暂停请求会返回 `InvalidGameState`。插件应读取当前动作的 `Supported`、支持单位及错误，等状态改变后再决定是否发出请求。[暂停与恢复实测](../work/b11-r11-stock-check-20261123/live/20261123T025755853Z-f350e869e0ce413880c0e006e8f3446d/result.json) · [r12 同局状态与回执](../work/b11-r12-stock-check-20261123/live/20261123T041248273Z-59ab7e59fbcc47ea9d6bf5ba17487d93/song-first-read.json)

SDK 能表达有限速度值、定位值、单位及目标 `RunId` 和 `ControllerGeneration`。离线验证覆盖了音频、进度、调度及判定游标的准备、提交、读回和失败恢复。

同一局内播放器、音源、clip或已验证的source epoch变化也会结束旧控制票。SDK的`ControllerGeneration`是当前服务的控制代次，插件必须使用当前租约给出的值；不要自行填写native controller generation。受控Start已有前任的单次、连续重建和忙碌交接已通过[限定验证](validation/playback-start-initialization-20261101/README.md)。无前任时另有独立[first-capture准入](validation/playback-first-capture-20261101/README.md)：先保留宿主和初始化对象，真实完成后条件捕获七根，仍须后续实际Pause/readback才建立第一个Clock与SDK。可选时间换算不可用不会单独撤销仍有效的独立读数和控制权。

本批独立构建与固定构建静态证据见 [N2 报告](validation/foundation-gameplay-values-20261029/2026-10-29_逆向-Rizline玩法时间与有值事务-report.md)。真实游戏单位校准、速度/定位及结果/提交流程继续待实测。[582 包冻结实测](validation/runtime-cancellation-20261020/README.md)证明其候选 Pause 后三个游戏时钟冻结、Resume 原生倒计时后同局继续，以及 Retry 新局身份；[603 包实测](validation/runtime-back-skip-20261020/README.md)另证明其 Back/Skip 边界，不能作为本次源码的实机验收。跨玩法/音频/内容审计见 [本局修改记录](Run-modifications.md)。

2026-10-31 新增了内部离线实现：GetTime profile 可消费 broker 私有 lease，验证实际 detour、原正文/跳板及固定 Progress 消费者；代码失效后恢复字节不能复活旧 lease。独立 owned native producer 可生成 Pause/Replay/Resume、实际 PlayScheduled double 实参/Returned、pitch/source 写入的私有凭据，并在 stock pitch=1 后恢复和读回选中速率。具体 provider 已由正式 Backend/Service 的受控路径消费；worker 错线程 Dispose 会保留七个根，主线程可重试清理。见 [当前整合](validation/gameplay-broker-source-integration-20261030/README.md)。

2026-11-01，GetTime 与 stock 来源已共用私有安装组，正式 Module/Service/同一 SDK 视图的受控跨局接续已实现。MusicPlayer writer 的失效处理已经完成[本批整合验证](validation/playback-progress-player-writer-20261101/README.md)；后续 Source V3 又加入 `AudioSource.set_time`，正常 Resume 必须实际经过 `SetTime→SetPitch→PlayScheduled`。外部位置写入会结束旧时钟和事务凭据，不能用这个失效通知代替 Seek 状态重建。V3 的正常Resume、外写失效、异常责任与旧协议拒绝已完成[当前限定整合](validation/playback-set-time-writer-20261101/README.md)；初始化跨协程的来源重捕获仍继续实现。

已有Clock的同局Start路径已覆盖实际内联字段写入、独立保留与资源重捕获。完整生产provider仍缺没有前任Clock的首次捕获、生产首次执行准入及未知底层writer的来源覆盖。stock 调度采样使用游戏 `DspTimeService.get_DspTime`，一般时钟快照的原始 `AudioSettings.dspTime` 单列；二者不能互换，也不能从 nullable origin 反推已执行 schedule。默认真实 SetSpeed/Seek 继续关闭，[下一实现合同](../.docs/foundation-expansion/N2-STOCK-SOURCE-NEXT.md)列明具体接线与验收。

## 时间源与单位边界

| 来源 | 存储 | 固定 profile 匹配时单位 | 当前证据 |
|---|---|---|---|
| `InLevel.LevelInformation.nowTime` | Single | `ChartCoordinates` | `ProgressControl.RealTimeToChartTime(_nowTime)` 的 BPM 分段坐标 |
| `InLevel.LevelInformation.nowRealTime` | Single | `Seconds` | `float(ProgressControl._nowTime)`，游戏进度秒 |
| `InLevel.LevelInformation.nowSyncedTime` | Single | `Seconds` | 本构建 Update 写入与 nowRealTime 相同值；不据名称声称额外延迟校正 |
| `AudioSettings.dspTime` | Double | 秒 | Unity API 文档；582 包实测暂停期间继续前进，不与冻结的谱面时间混为一谈 |
| `Stopwatch.GetTimestamp()` | Int64 | 按 Stopwatch.Frequency 换算秒 | 宿主单调时间，不替代谱面/DSP 时间 |

DSP 秒单位来自 [Unity AudioSettings.dspTime 官方文档](https://docs.unity3d.com/cn/2021.2/ScriptReference/AudioSettings-dspTime.html)。游戏字段单位依据固定机器生产/消费证据：实际加载文件 SHA 必须为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，字段类型/布局、精确声明方法入口及五份相关正文也须匹配；每次使用前复核正文。profile 缺失或改写时保留 `RawGameUnits`，不凭字段名升级单位。各读数独立保留来源与不可用原因。

`GameplayClockSnapshot.Mapping` 提供同 capture 的谱面坐标转换秒、内部 double 进度秒、实际采用的 `_offset` 和 `chartDelayMs/1000`；未初始化 offset 哨兵另标不可用。读取不调用会修正自身调度原点的 `MUGMusicPlayer.GetTime()`。空 BPM shifts 的公式为 `chart = seconds*bPM/60`；分段公式为 `key.time+(seconds-key.floorPosition)*key.value*bPM/60`，逆变换按对应分段计算。游戏 `LevelControl.Start` 也使用逆变换生成 Note 的真实秒位置。ChartDelay 单独报告，不能重复加进转换；`LevelInformation.speed` 已见 Note/Line 几何消费者，不等于同步播放速率。

`IGameplayClockService.Convert` 只接受游戏进度秒与谱面坐标，有限输入、同一 RunId/ControllerGeneration 及有限的实际输出都必须满足；DSP/host 绝对时间不属于该转换。示例函数的 SDK 调用方式与受控正式服务测试一致：

```csharp
using RizToolbox.Sdk;

static Result<GameplayTimeConversion> ChartPositionInSeconds(
    IGameplayClockService clocks, GameplayClockSnapshot sample, double position)
{
    if (sample.ObservedRunId is null)
        return Result<GameplayTimeConversion>.Fail(new OperationError(
            ErrorCode.InvalidGameState, "当前没有已观察到的局。", GameplayCapabilityIds.Clock));
    return clocks.Convert(new GameplayTimeConversionRequest(
        sample.ControllerGeneration, sample.ObservedRunId, position,
        GameplayClockUnit.ChartCoordinates, GameplayClockUnit.Seconds));
}
```

捕获起止的 host 单调秒包含字段、转换及 DSP 采样的耗时。Mapping 的 DSP/host 样本只关联这个区间。最近三个连续有效样本可以形成 [ObservedInterval](Gameplay-observed-clocks.md)，填入实测 `GameSecondsPerDspSecond`、`GameSecondsPerHostSecond`；这些速率只用于实际观察端点内的显式域换算，不能外推未来值。`LatencySeconds` 仍为 null，`SecondsMappingVerified` 和 `Mapping.RuntimeVerified` 保持 false，静态单位证明、受控区间算法与实机映射验收分别声明。读取中切局撤掉游戏关联/映射，仍保留 DSP/host；转换中切局返回 `TargetExpired`。

## 控制前置

实机已知 `levelBegan` 不适合判断可操作状态，所有控制不得依赖它。控制应重新采样当前活跃唯一 NormalMode 控制器、loadOver、levelOver/crashed、进度 timeReady/isPlaying 和该控制器自己的 pauseUIControl。

| 操作 | 入口 | 允许的候选状态 |
|---|---|---|
| Pause | `NormalModeInLevelController.Pause()` → 确认 `isPlaying=false` → `PauseUIControl.Pause()` | loadOver、timeReady、实际 playing、未暂停/结束；独立检查原始暂停冷却和 UI gate |
| Resume | `PauseUIControl.Resume()` | loadOver、timeReady、已暂停、未结束、CanDoOperation |
| Retry | `NormalModeInLevelController.Retry()` | 已暂停、未结束、CanDoOperation |
| Skip | `NormalModeInLevelController.Skip()` | 实际 playing、未暂停、未结束；SDK 的保守状态策略，不要求暂停页 CanDoOperation |
| SetSpeed | 按同局状态提供，实际改变倍率待实测 | 正有限值，单位 `PlaybackRateMultiplier`，同局已暂停且 CanDoOperation，动作报告支持该单位 |
| Seek | 按同局状态提供，实际跳转待实测 | 有限值，按动作返回的支持列表选择 `GameSeconds` 或 `ChartCoordinates`，同局已暂停且 CanDoOperation |

这些控制必须通过显式 `allowExperimental: true` 获取本局控制租约。租约绑定当前活跃对象代次与真实可空 RunId，单个提供者独占操作；对象消失、重建、停用或租约取消后旧控制句柄不得操作新局。

固定构建的 [18 个函数静态证据](validation/gameplay-controls-20261020/static-pause-guard/2026-10-20_逆向-Rizline暂停控制守卫-report.md)证明 `CanDoOperation = _pausing && !_over`，只适用于已暂停页面操作。Pause 单独要求实际 PauseUI 活跃、`!_over`、`!_terminated`、有限 `_pauseTime<=0`、场景时间 `>=1.8f`、`prepareToPlay=false`、没有 TouchBlocker 或原生活动窗口。签名不足只将 Pause 标为不支持。仅调用 PauseUI.Pause 会打开 UI 而不停止谱面，生产后端已补齐原始停止调用及停止读回。

Resume 使用原 PauseUI.Resume；它启动 3 秒准备过程。当帧 `Paused=false` 不能证明谱面已恢复，因此即时后置值还要求实际 `Playing=true`。倒计时期间成功调度凭据与 `ImmediatePostconditionObserved=false` 可以同时成立；应继续读取状态，不能反复调用恢复。Skip 代表提前跳过结算链，不能等同暂停页 Back 的退出动画。

## 在插件中显示每个动作的可用状态

主树 SDK 在 `GameplayControlAvailability.StateReadiness` 中提供本次采样的游戏条件。r13 开发包仍使用原接口。以暂停按钮为例，开场时 `Supported` 可以为真，`StateReadiness.Error.Message` 会说明倒计时尚未结束。倒计时结束会产生一次新的观察，按钮可以随之启用。`CanDoOperation` 描述暂停菜单的状态。

```csharp
var observation = controls.GetState();
var pause = observation.Value?.Operations.FirstOrDefault(op => op.Action == GameplayControlAction.Pause);
var enablePause = pause?.StateReadiness?.Success == true;
var reason = pause?.StateReadiness?.Error ?? pause?.Error ?? observation.Error;
```

同一份状态也会交给 `Subscribe` 回调。动作缺少底层支持或宿主还没有采到控制器时，就绪结果为空。执行命令仍需当前局的独占租约，宿主会在准备前和提交前重新检查游戏状态。

## 发出有值请求

先逐项读取 `GameplayControlSnapshot.Operations` 的 `Supported` 和 `ValueUnits`。两个插件共用同一控制独占；准备、提交、确认、恢复读回都处于同一占有区间。请求必须携带租约所观察的局与控制器代次，旧请求不能跨对象重建或 RunId 变化。下面示例在当前真实后端返回 `OperationUnsupported`；受控正式服务会实际更新音频、游戏和调度速率后确认。

```csharp
using RizToolbox.Sdk;

static Result<GameplayControlReceipt> SetPausedRunRate(
    IGameplayControlService controls, double multiplier)
{
    var acquired = controls.Acquire(allowExperimental: true);
    if (!acquired.Success)
        return Result<GameplayControlReceipt>.Fail(acquired.Error!);
    using var lease = acquired.Value!;
    return lease.Execute(new GameplayControlRequest(
        GameplayControlAction.SetSpeed, lease.ControllerGeneration,
        lease.ObservedRunId, multiplier, GameplayControlValueUnit.PlaybackRateMultiplier));
}
```

单独 `Execute(SetSpeed/Seek)` 缺少必要值，返回 `InvalidArgument`。NaN、Infinity、非正速度、缺失/错误单位在准备前拒绝；后端位置/速率范围也必须在第一次修改前检查。目标不符返回 `TargetExpired`，阶段不符返回 `InvalidGameState`，未适配动作或单位返回 `OperationUnsupported`。无值 Pause/Resume/Retry/Skip 便捷入口仍走同一服务。

## 成功、失败和清理

成功分为“元数据验证后的方法调用已返回”和“立即可读取的后置状态”，不代表完整暂停动画、重试重建或跳过结算已验收。调用前、读取前检查取消；同步 native 调用一旦进入无法通过取消强行中断。Retry/Skip 是不可逆命令，Dispose 只释放控制权，不能伪装成恢复旧局。

有值准备只捕获并保留所有受影响音频/游戏状态与恢复基线；提交前再次核取消、scope、局/对象及实际状态。提交只能执行一次，确认要实际读到相同单位的有限值和声明容差内结果。部分提交异常、确认失败、错误单位、非有限返回或提交中结束占有都进入恢复；恢复命令返回后另做独立基线读回，只有原身份/全部原状态确认恢复才记录 `Restored`。外部写入、恢复异常或无法确认的恢复记录 `SideEffectsUnknown` 并撤回该 action 到新局/控制器代次，不能覆盖他人的更新或留半重置局却宣称成功。

`GameplayModification.Request` 保留值、单位、RunId 和代次，`ConfirmedValue/ConfirmedUnit` 保留成功的实际值，`TransactionState` 区分派发、确认、恢复和未知副作用。共享 journal 复用 `Applied`、`Restored`、`InvocationFailedSideEffectsUnknown`，Detail 同步保留请求值/单位；审计故障不替代控制结果。Dispose 只释放控制/准备资源，不自动撤销已确认速度/定位、Retry/Skip 或暂停状态。游戏结果/提交影响仍未核实，所有控制继续为实验，相关 verified 标志保持 false。

## 后续验收

已验证的暂停区间中，三组游戏时钟相等而 DSP/宿主时间继续前进；恢复调度凭据的即时后置为 false，后续独立采样才观察到 Playing 和时间推进。Retry、Back 和 Skip 各自的直接结束路径已绑定不同包的冻结会话；这不代表正常完成、各非法状态或故障矩阵已经通过。

仍须以同候选核对不同刷新率、BPM 分段、实际 offset/ChartDelay、正常结束及 DSP/host 关系；完整原生事务可证明并实现后，再分别验实际速度/定位、输入/判定/结果/提交影响。判定 measured delta 与固定哨兵继续由精确来源分别解释，不能从本时钟映射把全部 RawDelta 改写为秒偏差。离线事务正向或历史包不能替代这些实机验收。
