# R06 暂停倍率恢复证据

当前源码新增私有 continuous pitch writer 和 provider 恢复传递，保留正常 native completion 后 API 失败的恢复责任。说明见 [本轮报告](../../2026-11-19_pitch-readback-recovery-report.md)。

- [当前验收汇总](../../../work/r06-pitch-readback-recovery-20261119/validation-r7.json)：1788 输入一致，两个 Release 构建退出 0，Gameplay 238/238，19 owned 原生场景 / 1086 断言通过。
- [最终实际执行](../../../work/r06-pitch-readback-recovery-20261119/build-and-checks-r7/results.json)：每个 child 的真实退出码、日志身份及 terminal 结果。
- [修改前输入](../../../work/r06-pitch-readback-recovery-20261119/implementation-inputs-before-r1.json)与[最终输入](../../../work/r06-pitch-readback-recovery-20261119/build-and-checks-r7/inputs-after.json)：独立原始记录，不能借旧结果验新源码。
- [受测 DLL](../../../work/r06-pitch-readback-recovery-20261119/build-and-checks-r7/assemblies-after.json)、[stock 夹具和 helper](../../../work/r06-pitch-readback-recovery-20261119/owned-native-inputs-r1.json)、[owned-call 夹具](../../../work/r06-pitch-readback-recovery-20261119/managed-owned-native-input-r1.json)：使用前后实际身份相同。
- 保留失败：[r1](../../../work/r06-pitch-readback-recovery-20261119/build-and-checks-r1/results.json)、[r3](../../../work/r06-pitch-readback-recovery-20261119/checks-r3/results.json)、[r4](../../../work/r06-pitch-readback-recovery-20261119/build-and-checks-r4/results.json)、[r6](../../../work/r06-pitch-readback-recovery-20261119/direct-and-seek-r6/results.json)。

用例覆盖正常原生写入后的临时/永久读失败、独立 participant 读失败、读回不符、外部同值/ABA、源和进度变化、重入/copy/worker 拒绝，以及写后异常、Clock 撤回、GetTime 在途、普通提交/调度和旧 SetTime 恢复拒绝。

`gameExecuted=false`，`installed=false`，`packageChanged=false`，`formalFreshSourceScopeCovered=false`，`fixedGameConsumerCovered=false`，`productionAdmission=false`。R08 既有包仍属于其封存源码；本批没有创建或安装新包。
