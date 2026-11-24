# 插件停用时的播放命令与清理

插件可能在游戏读回或原生调用返回期间停用。宿主会先结束该作用域的命令资格，排队中的命令进入 `Cancelled`，已进入原生写入的命令按照原有事务读回和恢复规则收尾。状态记录可以在作用域结束后读取。 [命令实现](../../../src/RizToolbox.Adapter/Gameplay/GameplayService.Commands.cs) · [SDK 合同](../../../src/RizToolbox.Sdk/GameplayContracts.cs)

| 改前复现 | 当前行为 |
| --- | --- |
| 提交后的游戏对象检查过程中停用插件，检查返回后仍进入值确认。 | 游戏对象读回前后都检查同一控制租约。若停用发生在读回期间，直接执行已进入写入的恢复与原状态读回。 |
| 命令已经确定恢复结果，随后延迟释放后端时抛出异常，调用方收到清理异常。 | 即时和排队命令保留各自终态。延迟清理尝试一次，失败单独记入日志。 |
| 第一个观察订阅释放抛出异常，排队命令及其他资源还在等清理。 | 先结束控制租约，再逐个释放观察订阅。订阅或日志接收方的异常会与其他清理隔离。 |

实际恢复仍需要对应事务的独立读回，无法确认时保持 `Unknown`。受控用例通过正式 Gameplay 服务的即时和排队入口，分别注入对象检查重入、后端清理失败和订阅清理失败。 [用例](../../../tests/RizToolbox.Gameplay.Tests/ControlCommandChecks.cs) · [改前日志](../../../work/b05-gameplay-command-shutdown-20261121/before-tests.log)

当前四个定向 Release 构建成功。Gameplay 241/241、Module 48/48、Hosting 24/24，共 313 项。三个自有原生场景另有 193 项，覆盖正常播放、在途回调期间清理拒绝和已准备事务的退出。前后核对 1814 项冻结输入及夹具文件。 [最终结果](../../../work/b05-gameplay-command-shutdown-20261121/current-r1/validation.json) · [复现脚本](../../../work/b05-gameplay-command-shutdown-20261121/Test-Current.ps1)

新 V5 候选已包含此项修复，包内 DLL 复跑 Gameplay 241/241 和三个原生场景 193 项。实机行为仍待验证。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-playback-lifecycle-candidate-20261121-r1.zip) · [包内验证](../../../work/v5-playback-lifecycle-delivery-20261121/packaged-contract-validation-r1.json)
