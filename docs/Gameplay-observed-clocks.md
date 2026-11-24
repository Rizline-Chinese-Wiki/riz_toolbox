# 从实际样本换算玩法、DSP 与宿主时间

`IGameplayClockService.ConvertObserved` 可在同一局、同一控制器 generation 的最近三个有效样本端点内，双向换算玩法进度秒、DSP 秒和宿主单调秒。换算范围限于实际观察到的区间。`RuntimeVerified` 仍为 false，音频延迟仍未知。要判断当前能否变速或跳转，请单独读取控制服务的动作状态和支持单位。[播放控制及实测范围](Gameplay-primitives.md)

## 取得可换算的区间

在主线程按实际需要调用 `Read`。第一次、第二次有效采样返回 `WarmingUp`；第三次连续有效采样可返回 `ObservedInterval`。服务最多保留三个样本，后续读取推进这个窗口；没有后台采样线程，也没有为转换请求补采样。

`GameplayClockDomain` 明确区分三个原点不同的秒域。相同的 `GameplayClockUnit.Seconds` 不能证明两个域可直接交换。

| Domain | 样本来源 | 边界 |
|---|---|---|
| `GameProgressSeconds` | 同 capture 的 `Mapping.ProgressTimeInSeconds` | 需要可用、有限且单位为 Seconds |
| `AudioDspSeconds` | 同 capture 的 DSP 读数 | 需要可用、有限且单位为 Seconds |
| `HostMonotonicSeconds` | host 捕获起止的中点 | 不是三个读数绝对同时发生的声明 |
| `ChartCoordinates` | 谱面坐标 | 使用原有 `Convert`，不进入 `ConvertObserved` |

原有独立时钟、`Mapping` 的 capture 起止及 host 末端锚点继续可读。观察模型自己的 `Samples[*].HostMonotonicSeconds` 才是中点；每个样本同时保留 `Revision`、三个域的值及 host 起止。首次读数、只有两个样本、缺少某个域时不会填入假定 1x 的速率。

以下函数用于已经从 `Read` 获得的快照；调用环境是持有该插件作用域时钟服务的主线程。成功仍取决于调用时的局身份、播放状态、区间年龄及当前窗口端点。

```csharp
using RizToolbox.Sdk;

static Result<GameplayObservedClockConversion> GameProgressToDsp(
    IGameplayClockService clocks, GameplayClockSnapshot sample, double gameSeconds)
{
    if (sample.ObservedRunId is null || sample.ObservedInterval is not
        { State: GameplayClockObservationState.ObservedInterval } interval)
        return Result<GameplayObservedClockConversion>.Fail(new OperationError(
            ErrorCode.ObservationUnavailable, "需要三个连续有效时钟样本。",
            GameplayCapabilityIds.Clock));

    return clocks.ConvertObserved(new GameplayObservedClockConversionRequest(
        sample.ControllerGeneration, sample.ObservedRunId, interval.SegmentId,
        gameSeconds, GameplayClockDomain.GameProgressSeconds,
        GameplayClockDomain.AudioDspSeconds, MaxAgeSeconds: .1));
}
```

## 区间、失效与误差含义

| 状态 | 含义 | 转换 |
|---|---|---|
| `Unavailable` | 局/状态或必要时钟源不可用 | 拒绝 |
| `WarmingUp` | 新段还没有三个连续有效样本 | 拒绝 |
| `ObservedInterval` | 最新三个样本形成正向、可逆的有限区间 | 仅端点内插 |
| `Expired` | 策略寿命已过 | 拒绝；下一次有效 Read 开始新段 |

默认策略的最大采样间隔、最大年龄均为 0.5 秒，绝对残差容差为 0.002 游戏秒，最小可分辨正区间为 0.000001 秒。`Read(observationPolicy: ...)` 接受显式策略；改变策略会分新段。`ExpiresAtHostMonotonicSeconds` 等于最新 capture 结束 host 秒加策略 MaxAge。请求 MaxAge 只能进一步缩短这个寿命，不能扩展或复活旧段；较严格请求被拒绝不会让其他仍满足策略的请求失效。

RunId、任一控制器身份/generation、已观察到的 playing/paused、来源或 offset/delay 发生变化，会丢弃旧样本。缺失、非有限、错误单位、倒退/停滞、捕获间隔过大或超容差的实际残差也会让区间失效或分新段。暂停期间进度时间没有逆映射；恢复后重新收满三个样本。模块停止、取消、错误线程及同步 getter 重入仍经过原有守卫。

速率和 offset 由首末实际端点计算，中间样本用于实测残差检查。`CaptureSkewSeconds` 为这三个 capture 时长之和；`ObservedResidualGameSeconds` 是中间样本在 game/DSP 和 game/host 两条拟合中的最大实际残差。容差检查包含按实测速率折算的 capture 包络，转换返回目标秒域的 `EstimatedUncertaintySeconds`。这些数值描述采样不同时性与已观察的拟合偏差，不能证明未采样期间线性，也不是输出设备音频延迟或其测量值。

转换核验当前 run、generation、playing/paused、host 单调时间与年龄，并只读复核当前 progress/chart/pause 子图身份、来源与单位证据、offset/delay 及实际进度。进度倒退或超出已观察速率和采样不确定度所容许的正向连续范围，会立即丢弃旧段；改变参数后即使尚未再 Read，也不能用旧模型继续转换。这些当前状态值不进入样本历史，不推进 Read revision，不用于返回未来预测值，也不读取 DSP 来补采样。转换只使用调用时最新三个样本的端点；即使 SegmentId 相同，窗口推进后已离开的老端点也会被拒绝。源值超出实际端点时返回 `InvalidArgument`，绝不外推。

生产接线通过 `matchesCurrentGameplayRun(info, controller, runId, sceneGeneration)` 把同一次 rooted capture 与 sampler 的具体对象身份关联。新控制器已出现而 sampler 仍是旧局时，独立原始读数仍可保留，但快照不带旧 RunId，观测换算与控制拒绝；sampler 追上后生成新的 generation 并正常重新开放。服务在 Read/Convert 发布以及控制准备、提交、返回处重新核验。返回时已失去可靠关联的已执行命令仅留下无 RunId 的派发证据，不发出错误的 run-bound receipt。受控后端可省略此生产关联 delegate；隔离测试另注入它覆盖失配与恢复，生产模块接线由整树验证覆盖。

常见拒绝码为 `InvalidArgument`（非法域/单位、非有限值、年龄或越界）、`TargetExpired`（局、控制器、段或已知播放状态改变）、`ObservationUnavailable`（尚未形成区间、源失效或过期），以及原有的 `Cancelled`、`WrongThread`、`Conflict` 和作用域失效码。

实现与受控验证见 [2026-10-30 冻结验证](validation/gameplay-observed-clock-20261030/README.md)。该证据未启动真实游戏、未执行 native fixture、未安装或进行 UI 操作，不替代运行时校准。
