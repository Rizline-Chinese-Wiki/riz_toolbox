# 原生 AudioClip 包装器实测

2026-10-22（UTC+8）的真实游戏会话验证了 `ResourceReference<AudioClip>` 的精确构造、getter、Release 和跨帧销毁。完整 LOCAL 曲包播放尚未完成，B15 保持未勾选。

## 已验证行为

探针自建两个静音 AudioClip，每个 80 sample frames、单声道、8000 Hz，不使用原版歌曲资源。会话 `20261021T160706416Z-d5e9394a85cb4eb997019c4e046ff76b` 正常进入主菜单，随后通过退出按钮和确认按钮正常退出；完整冻结日志 218 条连续事件、0 Error。

| 阶段 | 日志 sequence / Unity frame | 观察 |
|---|---|---|
| 精确构造与读取 | 107 / 6 | 两种 canDestroy 包装器的 getter 返回各自原始 AudioClip；两次 Release 返回 |
| 释放结果 | 123 / 8 | false 的 AudioClip 仍存活且 samples/channels/frequency 一致；true 的 AudioClip 经 Object.op_Implicit 确认已销毁 |
| 探针收尾 | 124 / 69 | 显式清理 false 的资源后，也经后续帧确认销毁；两个独立观察 root 已释放 |

`canDestroy=false` 的 Release 原本就是不销毁路径，单独调用它不能证明销毁能力。本次 true 路径和后续帧检查分别提供了该证明。

## 证据与复现

| Evidence | 原始记录 | 支持的结论 |
|---|---|---|
| E-build | [构建摘要](../foundation-resource-factory-20261021/build-summary.json)、[完整构建](../foundation-resource-factory-20261021/build.log) | 25 套、814/814，0 编译诊断；两个原生 fixture 31,261 / 61,358 通过 |
| E-install | [安装核对](../foundation-resource-factory-20261021/installation-check.json)、[源码核对](../foundation-resource-factory-20261021/source-integrity-check.json)、[游戏原文件](../foundation-resource-factory-20261021/original-game-after.json) | 18 个安装 hash 匹配、276 个源码输入未变、三个原游戏文件未变 |
| E-runtime | [机器核验结果](runtime-proof.json)、[完整事件](20261021T160706416Z-d5e9394a85cb4eb997019c4e046ff76b/events-000001.jsonl) | 精确包装器和跨帧 Unity 对象生命周期已实际运行；各文件 SHA-256 在 proof 中 |
| E-restore | [marker 恢复](probe-marker-restoration.json) | metadata 和 factory 两个 marker 均恢复原 Absent |

从仓库根核验冻结会话：

```powershell
python docs/validation/runtime-resource-factory-20261022/freeze-proof.py docs/validation/runtime-resource-factory-20261022/20261021T160706416Z-d5e9394a85cb4eb997019c4e046ff76b
```

脚本检查完整 sequence、唯一三阶段记录、帧号递增、所有生命周期断言和当前安装 hash。若安装已更新，需使用对应版本环境；脚本会拒绝把其他包当作本次证据。

重新实测时，先正常退出游戏并安装对应包，显式创建 `BepInEx/RizToolbox/probes/local-resource-reference-factory.enabled`，再启动。这个 marker 才允许自建资源与调用资源方法；原 `local-resource-reference.enabled` 保持元数据采集用途。实测后恢复 marker 原值。

## 实现与边界

F-lifetime（validated，E-runtime）：原生 AudioClip 闭包装器能完成已观察的生命周期。调用路径为自建 Clip → 精确闭类型/签名核验 → NewExact 交接现有 GC root → getter 独立 root → 一次 Release → 后续 Unity 帧确认 → 观察 root 释放。

F-failure（离线验证，E-build）：Release 先设置原生 released 再 Destroy；异常后第二次调用可能直接返回，不能当作恢复成功。实现禁止该重试，独立尝试两个 root 的释放；未知清理保留进程责任。包装器 live/failed 共限 64，探针失败责任最多 4。对应 9 项清理回归和 12 项精确构造回归已通过，未在游戏进程故意注入原生 free 异常。

本次不证明 Sprite 包装器生命周期、实际可闻播放/延迟、native heap/GPU/长期 GC 数量，也不证明本地歌曲启动、metadata、重试和结算接入。LOCAL RequestPlay 继续明确返回 OperationUnsupported。下一条实现依据见 [metadata 静态报告](../local-metadata-bridge-20261022/README.md)。
