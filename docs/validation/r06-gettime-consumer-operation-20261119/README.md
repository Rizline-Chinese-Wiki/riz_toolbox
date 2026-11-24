# R06 GetTime consumer 操作接线：限定离线证据

本切片已通过当前源码的 **639 项**检查及两个 Release 增量构建。正式游戏 consumer、22-role Fresh/watch/SuppressObserver 和实机验收保持未完成。

- [实现与范围说明](../../2026-11-19_gettime-consumer-operation-report.md)
- [验收汇总及源码/材料身份](../../../work/r06-gettime-consumer-operation-20261119/validation-r4.json)
- [最终 11 个自然退出进程](../../../work/r06-gettime-consumer-operation-20261119/build-and-checks-r4/results.json)
- [真实 GetTime 最终 DSP 切源、Known 撤权与零根清理](../../../work/r06-gettime-consumer-operation-20261119/build-and-checks-r4/operation-gettime-readback-source.process.json)
- [实际 standalone consumer profile、同 lease 与 copy/worker 拒绝](../../../work/r06-gettime-consumer-operation-20261119/build-and-checks-r4/mapped-consumer-operation.process.json)
- [publication in-flight 的 Unknown 与根责任](../../../work/r06-gettime-consumer-operation-20261119/build-and-checks-r4/operation-publication-inflight.process.json)
- [r1 正式材料不足的原失败](../../../work/r06-gettime-consumer-operation-20261119/build-and-checks-r1/results.json)
- [r2 正常切源误判 Unknown 的原失败](../../../work/r06-gettime-consumer-operation-20261119/build-and-checks-r2/results.json)
- [r3 snapshot 字段编译错误的原失败](../../../work/r06-gettime-consumer-operation-20261119/build-and-checks-r3/results.json)
- [首个封存进程的原失败观察](../../../work/r06-gettime-consumer-operation-20261119/seal-r1-process-observation.json)

固定映像只做未执行游戏代码的 profile 检查；actual reader 场景运行自有原生 originals 和 synthetic metadata。不引用历史通过数，不签游戏或 production，也不以旧 R08 包验本批源码。`Program.cs` 副本的 after-dispatch 属性见验收汇总 `sourceSnapshots`。
