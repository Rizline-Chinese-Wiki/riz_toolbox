# 暂停 Clock 激活验证

原实现会在暂停位置或倍率变化后继续激活准备好的 `GetTime` 策略，返回旧的暂停位置。两个离线用例分别在准备至激活之间、原生激活后的读回期间改变实际播放器位置。改前两个用例均失败。 [激活前](../../../work/r06-paused-clock-activation-20261121/red-operation-clock-activation-drift.stderr.log) · [激活后](../../../work/r06-paused-clock-activation-20261121/red-operation-clock-activation-commit-drift.stderr.log)

激活前后使用同一条已完成的暂停回执检查播放来源、位置、倍率、音频长度与暂停状态。正式路径还核对当前 GetTime 消费者与原挂接凭证。激活前检查失败会让策略保持待激活，暂停值恢复后可重试。激活后读回不符会撤回这次策略，随后 `GetTime` 只保留原生返回。

| 检查 | 当前结果 |
| --- | --- |
| 激活前位置和倍率改变 | 17 项。两个值分别变化时均拒绝激活，原值回到基线后同一会话成功激活，真实夹具 `GetTime` 走过 Clock 读回。 [结果](../../../work/r06-paused-clock-activation-20261121/operation-clock-activation-drift.json) |
| 激活后位置改变 | 14 项。实际读取期间位置改变后撤回原生策略，下一次 GetTime 的原函数执行一次，托管读回未执行。两种故障路径均无播放写入，最终释放全部根。 [结果](../../../work/r06-paused-clock-activation-20261121/operation-clock-activation-commit-drift.json) |
| 相邻运行路径 | 同一源码完成七个自有原生场景和一个固定映像场景，共 408 项。覆盖 GetTime 读回期间切源、运行中读回、原函数尚未退出及 Unknown 根保留。 [汇总](../../../work/r06-paused-clock-activation-20261121/validation-r1.json) |
| 重新开局与跨局 | 292 项。三个进程覆盖重新开局交接、待开局重试和开局回调未退出时停止，独立构造器夹具验证跨局后的暂停与 Clock 激活。 [重新开局](../../../work/r06-paused-clock-activation-20261121/transfer-validation-r2.json) · [跨局](../../../work/r06-paused-clock-activation-20261121/ctor-sdk-crossrun.validation.json) |
| Release | 解决方案 0 错误、66 条警告，独立 provider 0 错误、0 条警告。验证前后核对 1809 项源码输入和用户 README。 [解决方案](../../../work/r06-paused-clock-activation-20261121/solution-build.log) · [provider](../../../work/r06-paused-clock-activation-20261121/provider-build.log) |

固定映像确认了 `GetTime` 消费者的代码配置与挂接凭证。正式 22-role Fresh 采用、游戏内 `ProgressControl.Update` 调用和实际 Speed/Seek 仍待验证。本批交付源码，现有 V5 候选对应上一批首帧修复。
