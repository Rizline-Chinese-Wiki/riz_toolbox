# Replay 和 Resume 的起播读回

播放时钟现在用实际 `PlayScheduled` 实参、独立 DSP 采样、游戏的起播基准和媒体位置共同核对一次调度。Replay 的媒体位置应回到零。Resume 的起播基准应为实际调度时刻减去恢复后的媒体位置。调度在读回期间已过起播点，或起播基准、媒体位置不一致时，当前来源和时钟会撤回。 [来源实现](../../../src/RizToolbox.Adapter/Gameplay/NativePlaybackStockRootedSource.cs) · [时钟入口](../../../src/RizToolbox.Adapter/Gameplay/NativePlaybackStockClockRouter.cs)

## 内部处理

第一次读回在恢复选中倍率之前完成。恢复倍率后，再对同一个原生调度和媒体锚做一次读回。发布 Clock 复用原有的现读入口，检查媒体锚及完整 DSP 采样窗口。失败发生在恢复倍率之前时，原生调用保留游戏自身的重置动作。失败发生在恢复倍率之后时，当前 SDK 播放读数撤回，`GetTime` 回到原生返回值。

## 定向用例

| 注入位置 | 修改前 | 修复后的断言 |
| --- | --- | --- |
| Replay 原生返回后，起播基准变更 | 仍恢复选中倍率 | 仅执行游戏自己的倍率重置。 |
| Replay 原生返回后，DSP 越过起播点 | 仍恢复选中倍率 | 在额外倍率写入之前撤回。 |
| Resume 恢复选中倍率后，起播基准变更 | 冲突数据仍可发布 Clock | 撤回 Clock 和 SDK 来源。 |
| Resume 恢复读回与 Clock 发布之间，起播基准变更 | 冲突数据仍可发布 Clock | 最后一次现读拒绝旧起播基准。 |

定向用例在独立的 native fixture 中执行实际回调和存储动作，经正式 broker、Rooted Source、Clock 路由及 SDK 读取。IL2CPP 元数据由夹具提供。真实 Unity 调度、可闻同步和 Speed/Seek 整体验收仍待完成。 [用例](../../../tests/RizToolbox.PlaybackStockProvider.Tests/Program.cs) · [改前记录](../../../work/r06-stock-schedule-readback-20261121/schedule-origin-before-publication-before.json)

三个定向 Release 构建成功。七个原生夹具进程共 393 项，另有 Gameplay 238/238，合计 631 项。构建与检查前后核对 1814 项输入和夹具哈希。首次通用回归缺少 owned fixture 环境变量，重新构建并指定该夹具后，使用同一源码重跑通过。 [最终结果](../../../work/r06-stock-schedule-readback-20261121/current-r3/validation.json) · [复现脚本](../../../work/r06-stock-schedule-readback-20261121/Test-Current.ps1)

新 V5 候选已包含此项修复，用包内 DLL 复跑四个起播故障场景共 145 项。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-playback-lifecycle-candidate-20261121-r1.zip) · [包内验证](../../../work/v5-playback-lifecycle-delivery-20261121/packaged-contract-validation-r1.json)
