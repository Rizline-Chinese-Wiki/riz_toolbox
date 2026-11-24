# 查询本局修改记录

`riz.gameplay.modifications` 是只读、主线程、作用域绑定的审计服务。它记录实际派发的控制、当前获胜的音频修改、自有播放以及经游戏当前图对象确认的本地谱面。未知 RunId 保持 null；不根据曲名或历史最佳成绩猜局身份。接口见 [RunModificationContracts](../src/RizToolbox.Sdk/RunModificationContracts.cs)。

在已启用插件的主线程方法内读取：

```csharp
var service = context.GetService<IRunModificationService>(RunModificationCapabilityIds.Read);
if (!service.Available || service.Service is null)
    throw new InvalidOperationException(service.Error?.Message);
var result = service.Service.Read(new RunModificationQuery(MaximumEntries: 256));
if (!result.Success) throw new InvalidOperationException(result.Error?.Message);
foreach (var entry in result.Value!.Records)
    context.Log.Log(LogLevel.Info, $"{entry.RunId ?? "unassociated"}: {entry.Owner}/{entry.Operation}: {entry.Outcome}");
```

返回的是普通不可变 DTO，可复制到后台处理；后台不能调用服务。普通插件没有写入记录或原生指针接口。记录附有 session、RunId、生命周期 revision、场景代次、来源及结果/提交路径的验证状态。当前新玩法路径的 `LocalResultBehaviorVerified` 和 `SubmissionBehaviorVerified` 仍为 false。

## 关联规则

真实 `RunStarted` 之前存在的活动效果会在该开局时带入一次；若插件在已有局中启用，只报告当前真实局基线，不伪造开始事件。带显式 run/session/revision 的报告必须与当前观察匹配，否则保留为未关联。过期结果不能把旧局操作记到新局。

结束的局保留历史并关闭新记录。释放审计效果产生 `EffectReleased`，这仅表示追踪租约结束，不代表恢复已被游戏或用户更改的状态，也不意味着过去的结果重新变成未修改。控制调用抛错时记录 `InvocationFailedSideEffectsUnknown`，不假装没有副作用或自动重试原调用。

音频覆盖只追踪协调器确认的胜出者。被遮挡的候选不会被登记为已影响本局；胜出者更替、SetValue、目标失效和租约释放均更新活动追踪。正在播放的源若 Stop 失败，则播放资源与追踪保持到停止成功。记录回调或日志故障不能替换原操作的返回值/异常。

本地谱面只在所加载图对象与当前 `LevelInformation` 匹配、并有真实活动局时关联。解析候选或登记内容本身不意味着被游戏采用。提供者退出后，被当前局持有的图和对应追踪可按内容租约继续到观察到的结束边界。

## 有界保留

默认最多 64 局、每局 256 条、未关联记录 256 条、128 个活动效果，一次最多查询 512 条。读回包含 `Truncated` 和丢弃/驱逐计数。错误线程、重入、过期、非法生产者数据和容量上限分别计数。底层日志使用 debug 级聚合设施，不逐音符写修改记录。

15 项独立关联检查使用实际生命周期状态机和正式 PluginHost 子作用域；另有音频胜出/释放、控制成功/异常的生产服务回归。它们没有证明真实游戏的正常通关、取消、暂停和提交路径；B01/B11 的实机要求保持未验收。
