# 原始共享线条缓冲区接入离线验收

2026-11-14。本批主树 909 输入已接受；完整 SDK/B01–B19 和真实 Speed/Seek 仍未完成。没有安装或启动游戏，没有调用真实 Unity renderer。

## 现在实现的能力

同一原 `LineRingReader` 惰性保留 `LineControl.linePositions@0x78`，由原 source/root 表维持实际数组身份，并通过已接受的 Runtime 完整观测/前缀写入 primitive 处理坐标。原 endpoint command owner 增加一次准备、完整 baseline/expected、正序写入、反序选定恢复和 journal；不另抓来源，也不另建每条 Line 的新数组。

只有前缀被修改，未写尾部和共享引用保留。正向 owned bits 必须有限；恢复保留原 NaN payload 和负零。source/字段关联在回调后重新核对，写入后的 reader 检查失败也保留实际 entered 记录。临时 pin 清理未知时同时保留原 source/命令和 publication 清理责任，不重试。选定值恢复后，外部别名/consumer 仍未闭合，owner 和原来源根仍保留。

独立审查发现的 r1 问题已修复：进入 `beforeStore` admission 不能作为本工具已写入的证明；外部回调若把数组改成恰好目标值，而本工具实际未进入 store，状态会被隔离，恢复拒绝覆盖外部值。只有明确 StoreEntered 才加入 inverse 资格。原 r1 五项通过和审查记录保留，不作为修正版验收。审查时文件正在改变，旧报告 hash 表不能冒充 r2 独立批准；[root 复核](../work/line-vector3-buffer-reader-integration-20261114/root-review-r2.md)明确记录了该区别。

## Evidence → Finding → Path

| Evidence | Finding | Path |
| --- | --- | --- |
| [正式新主图结果](validation/line-vector3-buffer-reader-20261114/run/result.json)，SHA `a3698022cd90d96a6bf4610bcb25dafdbd46ca938dc8b2aabeb5cb50441e6b42` | 六组件 Release 0 警告/0 错误；solution 0 错误，55 项既有测试警告保留；59 个自然步骤全部通过 | 主树 909 新产物 |
| [新 reader 七项日志](validation/line-vector3-buffer-reader-20261114/run/reader7.log) | 同物理数组、完整尾部/位恢复、非有限正向拒绝、字段替换、写后检查失败、写前/写后未知清理，以及外部目标不能被误 inverse | 原 reader/source/endpoint owner 的实际导出后端 |
| writer01–36 和 source 选择 19 项原日志 | 36 项原端点写入、19 项相关来源漂移/重入/生命周期/动态消费回归通过 | 不是完整 source30 或完整 Judgements 默认矩阵 |
| [默认 Runtime 日志](validation/line-vector3-buffer-reader-20261114/run/default366.log) | 366/366 自然通过 | 本批 Runtime 新产物；不借旧包验收 |
| [原 root 终态](validation/line-vector3-buffer-reader-20261114/run/root-exit.json) | session96387 实际 exit0；不另称 kernel handle witness | 根工具实际终态 |
| [归档摘要](validation/line-vector3-buffer-reader-20261114/validation-summary.json)，SHA `8df53d0819339010c4d49b2dacb54b38a386ad1a5f4ed86e35e2599be45eb469` | 完整 909 输入、控制/日志、六生产 pairs、两测试DLL/PDB及直接输出文件、fixture、静态字段片段及 r1/r2 历史均冻结；原子发布和直接归档脚本 archive-only 均实际 exit0 | 接受的本批离线切片 |

Build.ps1 已显式加入 `--line-ring-buffer-checks`，避免其既有 legacy/source 分组遗漏新 registry；候选脚本 AST 实际解析通过。接入准备最初因两个 legacy for300 循环导致文本锚点歧义，写入前拒绝；修正为准确执行循环后才产出 payload，失败控制保留，没有污染主树或改写历史。

可在仓库根独立复核归档：

```powershell
python docs/validation/line-vector3-buffer-reader-20261114/control/publish.py --check-only docs/validation/line-vector3-buffer-reader-20261114
```

## 剩余边界

本批仍没有 SetPositions 调用、saved array/count/nested plan 的实际 consumer 验收，也没有把同一 Line 的重复 occurrence 当成两条不同 Line。`work/line-setpositions-profile-next-20261114/` 新 profile/runtime_invoke 候选已另在 synthetic SEC_IMAGE + 实际 IL2CPP 导出 seam 下严格构建并通过八项：不可读MethodInfo在元数据出口前拒绝、原数组和显式数量、错误签名/RVA、容量、admission 回调漂移、字段保存后 exception-out、worker、精确临时 pin 清理失败。它尚未合主或接受，真实 loaded-body admission 未测；不能用该候选八项验收主树，反之亦然。

下一步先独立核对/接入该精确入口，再把原 reader 保存的 `_positionsPlan@0x88`、count@0x90、nested renderer plan 及两条不同 Line 的共享 scratch 接入同一有序 journal。Ring 初始化、Hold engine、Pause/Replay/Resume、真实 DSP schedule 与完整时钟事务仍未闭合。完整 Speed/Seek 保持关闭；原用户 README 和 881 预览 ZIP 未改，909 未重新打包。
