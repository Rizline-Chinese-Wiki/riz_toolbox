# native Judge 活动与 LOCAL 退休前 drain

2026-10-23。承接 [1287 冻结批](../foundation-return-primitives-20261023/README.md)，本批范围见 [scope](scope.md)。**1298/1298、25 套完整离线检查通过，已冻结暂存，未安装。** Runtime **137**、Hooks **113**、AudioContent **437**，比上一批新增 11 组检查。

## 修复的问题

原生 Judge helper 原有完整 Dispatch 在途计数，但 [C# bridge](../../../src/RizToolbox.Runtime/Hooks/NativeJudgeContextBridge.cs)没有读取接口。此前 LOCAL 主要读取五门、Progress、初始化和 Timeline 活动，不能据此确认 Judge 已退出。本批读取真正的 GetStats，并补充内层 Judge、Hold 入口的原函数和 context finally 区间计数。

[broker 查询](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeJudge.cs)要求精确 address/MethodInfo/class/ABI，既不安装缺失入口，也不把逻辑解绑当成物理调用已结束。统计失败、capture/after/release 故障保持 unknown。[生产 LOCAL bridge](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunTerminationBoundaryState.cs)解析已有三个确切入口，外层必须 native transport；准入、终止快照、撤映射前后 drain 和停止都纳入这些活动。

## 验证范围

- [新增 Runtime 用例](../../../tests/RizToolbox.Runtime.Tests/NativeJudgeActivityRegression.cs)：真实 Windows x64 native caller → Dobby → 生产 Judge bridge/helper → C++ original → native catch。阻塞原函数时 activity=1，逻辑 detach 后仍为1；normal/SEH/C++ 清理后归零，异常 payload/kind 保持。worker 是 fixture 内部 CreateThread。
- [新增 Hooks 六组](../../../tests/RizToolbox.Hooks.Tests/JudgeActivityTests.cs)：受控 transport 测 exact lookup、worker、nested/cleanup、Hold AL、故障 sticky 与无效统计。native method alias 共享也拒绝。
- [新增 Audio 四组](../../../tests/RizToolbox.AudioContent.Tests/NativeJudgeDrainRegression.cs)：准入、计数汇总、撤映射后重新 drain、退休和停止；受控 registry 关闭使用已有 fixture verifier，不声称真实消费者结束。

[Hooks 定向日志](hooks-targeted-first.log) 113/113；[Audio 首轮](audio-targeted-first.log) 436/437，因为旧受控 LegacyBackend 尚未转发新活动契约，补显式转发后[第二轮](audio-targeted-second.log) 437/437。[Runtime 首轮](runtime-targeted-first.log) 136/137，因为新测试回执字段顺序写错；按实际 C++ 结构修正并追加字段偏移核验后，[第二轮](runtime-targeted-second.log) 137/137。[native 定向构建](native-targeted.log)通过既有 18 调用 probe。过程失败日志保留。

本批没有新的游戏验收。外层 C++ original 异常证明不覆盖既有 managed inner Judge/Hold 和 Play/Progress/Timeline 的 original unwind。Count=0 只是当次观察，所有来源不可再进入、controller/音频等失效、菜单账本和资源归属仍需各自证据。RequestPlay 继续关闭，只有 B03/B18a 保持勾选。

## 完整构建与冻结

[完整日志](build.log) / [摘要](build-summary.json)为本批最终通过结果，session 32705 已 exit 0。既有 native helper fixture 31261/61358、初始化 helper 1747、native-only probe 18 单独计数，不加进 1298。本批新增生产 Judge native ABI/worker/异常用例在完整 Runtime suite 中再次通过；没有另跑独立 ABI runner。最终完整增量无编译诊断；前置 Audio 仍保留 14 条共享 FakeNative CS0649 与1条既有结果 sink 测试 CS8602，不构成干净构建零 warning 证明。

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码 ZIP](source-inputs.zip) / [核对](source-archive-check.json) | 390 项，3,932,225 原始字节 | `510ba28fbd9de16f1162c604d1c5acbefc607eea48586a7ca4305c7934232bd6` |
| [源码 manifest](source-manifest.json) | 390 项 | `f474716d8057a6cc421f442d49954bcb9a00259baea335441895ed7d880233aa` |
| [暂存 ZIP](staged-package.zip) / [核对](package-archive-check.json) | 18 项，2,995,176 原始字节 | `48964fbd28b26776b146b8bcc7f0afa65f10f180326a5e6a7e722688037d3c77` |
| [完整日志](build.log) | exit 0 | `c31e1d59c95310695972eb8f27636ffe66b3d958b0f6c1577c5a37c9f4244e64` |

[暂存核对](staging-check.json)与[源码一致性](source-integrity-check.json)均通过；[旧安装核对](previous-installation-check.json)匹配现有1021包18项及其旧326源归档，[原游戏核对](original-game-check.json)匹配三原文件。相比1287为3新增、11修改、0删除，见[清单](changes-from-1287.json)/[差分](changes-from-1287.diff)。[主代理自查](review.md)明确本轮核对边界；旧1287及更早归档独立保留。

复现完整离线构建可在仓库根运行：

```powershell
pwsh -NoProfile -File scripts/build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/judge-drain-reproduce-20261023'
```

此命令编译原生 fixture、真实Dobby检查、25套检查及暂存包；不安装或启动游戏。完整冷缓存/其他机器复现矩阵仍属B19未完成范围。
