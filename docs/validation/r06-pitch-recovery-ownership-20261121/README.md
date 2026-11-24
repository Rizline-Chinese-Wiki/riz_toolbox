# 暂停倍率恢复的实际读回

真实自有夹具执行 `SetSpeed(2)`。首次 API 读回确认了 `2×`，随后独立确认失败，夹具同时在音频存储中写入 `1.25×`。旧路径在回滚时写回了 `1×`，并报告恢复成功。 [改前失败](../../../work/r06-pitch-recovery-ownership-20261121/red-confirm-drift.stderr.log)

确认过的倍率写入在执行逆向写入前重新读取当前倍率和暂停基线。若当前倍率已变化或无法再次读取，关闭这次控制并记录副作用未知。首次写入仍等待读回时，保留同一私有续票，核对其他暂停字段后仍可执行一次逆向写入，随后用实际 API 确认恢复。

| 场景 | 结果 |
| --- | --- |
| 确认后外部倍率变化 | `1.25×` 保留，原生倍率写入和写入序号均只增加一次。命令与审计均记录副作用未知，旧 Clock 不再对 SDK 提供样本，最终释放全部根。 [执行结果](../../../work/r06-pitch-recovery-ownership-20261121/operation-readback-confirm-drift.json) |
| 确认后读取暂时失败 | 再次读回能确认 `2×` 时仍恢复到 `1×`。持续无法读回时保留实际 `2×` 并结束控制。 [短暂失败](../../../work/r06-pitch-recovery-ownership-20261121/operation-readback-confirm-once.json) · [持续失败](../../../work/r06-pitch-recovery-ownership-20261121/operation-readback-confirm-always.json) |
| 首次读回失败 | 同一已完成写入的私有续票仍可确认恢复到 `1×`。 [执行结果](../../../work/r06-pitch-recovery-ownership-20261121/operation-readback-once.json) |
| 当前验证 | 12 个自有原生场景 748 项，Gameplay 238/238，共 986 项。独立 provider 和解决方案的增量 Release 均 0 错误。验证前后核对 1809 项源码和受测材料。 [结果汇总](../../../work/r06-pitch-recovery-ownership-20261121/validation-r1.json) · [解决方案构建](../../../work/r06-pitch-recovery-ownership-20261121/solution-build.log) |

此修复已编入新的 V5 候选。用包内托管 DLL 和原生助手复跑三个倍率恢复场景，共 175 项。正式 Fresh 和游戏内 Speed/Seek 待验收。 [V5 候选](D:/riz-m2/RizToolbox-0.3.0-V5-postprocessing-candidate-20261121-r1.zip) · [包内验证](../../../work/v5-postprocessing-delivery-20261121/packaged-contract-validation-r1.json)
