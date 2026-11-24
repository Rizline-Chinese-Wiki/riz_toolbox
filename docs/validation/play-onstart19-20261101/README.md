# PlayOnStart19 自有原生调用验证

2026-11-01，`flavor=null`。第19站点的真实CALL、Void0/mid、非空listener与既有私有source/reference/child membership已在自有fixture联合通过。产品源码来自已冻结778基线；没有修改产品src，也不验后续785增量。scope见[当前续接点](../../../.docs/CURRENT-CHECKPOINT.md)。没有安装或启动游戏。

原件在 `D:/riz-build-cache-next/foundation-next-functional-gap-review-r1-20261031/s19-stage-r1/`；[索引](evidence-index.json)逐项绑定结果、源码、实际退出和符号。

| Evidence | 实际结果 |
|---|---|
| E-S19 | test-result-r4.json / execution-r4.json：actual native Play与Invoke original各一次；source site19 Before/After两行、一个完成事务；实际E8 CALL的caller等于relayReturnPc+5；mid、非空执行列表、listener各一次 |
| E-REF | 同一onStart字段0x88的child membership、actual Invoke generation和source transaction关联；六次真实TLS引用投递全部完成，scope/unknown/fault最终零 |
| E-SYMBOL | 5组managed PE/PDB、563文档；native一组PE/PDB和14COFF，编译与执行均actual0；managed保留26项既有fixture warning |
| E-SOURCE | source-snapshot-r4.zip共903文件，运行前后hash一致。469个产品源文件与778归档相同；新增内容限自有nativefixture和D-only测试接线 |

F-CALL（`n/a_re/high/validated`）：E-S19/E-REF/E-SYMBOL证明本条实际调用与私有归属，而非只验拒绝分支。P-CALL（`path_type=callflow`）：actual source S19Before→真实五字节CALL→Void0 Invoke→mid→非空listener→同事务child association→Invoke/Source实际返回及reference scope关闭。

非空listener仍为Unaccounted、EffectsEnded=false，零rootfree/retirement/productionauthority。本项不证明完整效果退休、Unity GC、source generation、未知writer或生产首次准入；20/21仍待独立实现。历史r1重复borrow定义、r2未映射receiver、r3缺UnityEvent class接线均保留，修复只在fixture，不放松产品guard。

可按原件run-s19.py和全新attempt编号重现；每次独立输出保留源码ZIP、命令、stdout/stderr/exit及PE/PDB。后续进入site20，不能重复计算本项S19或拿此旧源码验新产品。
