# 同谱面重开与结算归属

玩法观察以控制器和 `LevelInformation` 共同确定局身份。换控制器时，即使沿用同一个 `LevelInformation`，也要再次观察到 `isPlaying` 和 `timeReady` 才会建立新 `RunId`。采样短暂缺失期间保留上一控制器的根，当前玩法入口仍会撤回权限。 [生命周期实现](../../../src/RizToolbox.Adapter/GameEventState.cs) · [采样实现](../../../src/RizToolbox.Adapter/GameEventObserver.cs)

| 场景 | 验证结果 |
| --- | --- |
| 同谱面换控制器，新控制器还在加载 | 进入 `Loading`，旧 RunId 已退下，新局等待播放。 |
| 旧结算在新局的 `AwaitingResult` 阶段到达 | 旧成绩仍可观察，`RunId` 留空。新局继续等自己的结算。 |
| 新控制器带有自己的开始和结算来源 | 与新 RunId 关联，仅产生一次结束。 |
| 尚未采样的另一个控制器发来判定 | 该判定无法借用旧局；相同控制器的最后一笔判定仍可在 `levelOver` 后关联。 |

改前用例复现了替代控制器继承旧 `RunId` 和判定借用旧局两项问题。受控用例包含连续采样和中间短暂缺失两种路径。实际游戏同谱快重开、正常结算和迟到结果仍待实机确认。 [用例](../../../tests/RizToolbox.Game.Tests/Program.cs) · [改前日志](../../../work/b09-controller-pair-result-20261121/before-tests.log)

四个定向 Release 项目构建通过。Game 81/81、Data 24/24、Gameplay 241/241、Judgements 三个定向场景 3/3，共 349 项。源码前后核对 1814 项输入。 [最终结果](../../../work/b09-controller-pair-result-20261121/current-r1/validation.json) · [复现脚本](../../../work/b09-controller-pair-result-20261121/Test-Current.ps1)

此项修复已进入新 V5 候选，使用包内 DLL 复跑 Game 81/81。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-result-data-candidate-20261121-r1.zip) · [包内验证](../../../work/v5-result-data-delivery-20261121/packaged-contract-validation-r1.json)
