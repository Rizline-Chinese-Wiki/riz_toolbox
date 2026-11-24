# 原生故障在当次宿主维护中撤权（2026-11-19）

原生 watched worker 已记录 sticky Unknown 时，宿主现在直接使用当次 poll 返回的真实故障，立即撤回旧 Clock、音源描述和变速权限。外部故障通知仍在工作线程投递，负责保留原始故障对象；通知尚未到达不会延迟撤权。正常暂停、切源或 policy inactive 仍按逻辑生命周期处理。

本次先用未修改的产品代码稳定复现：在实际十三成员 owned constructor group 中，真实 worker constructor 的 before-store 已为 Unknown，原调用和 physical exit 各一次，managed source observer 为零。测试暂存真实外部通知后，第一次 Router pump 仍为 `Paused`、`_physicalFailure=null`；带未来进度期限的宿主仍为 `ReadyExperimental`。两个进程均自然退出 1，原始结果保留在 [reproduce-r1](../work/r06-owner-fault-poll-20261119/reproduce-r1/reproduction.json)。

产品改动限于三处：

- `ExactHookBroker.Playback.SourceExecution.PollOperationNativeFaults` 从 retained source/GetTime 通道读取当前健康状态，返回真实 sticky fault reason；调用仍绑定原 owner thread 和 registered lease，结果不提供任何读写或新票权限。`Armed=false`、`AuthorityActive=false` 本身不判为故障。
- `NativePlaybackStockClockRouter.PumpPhysicalFaults` 同步记录返回的故障并撤回当前 session；随后收到的 exact fault 对象仍入队、原对象仍为 `FirstPhysicalFault`。
- `NativePlaybackStockRateHost.Pump` 在进度节流之前进行物理故障维护。已停止宿主仍消费迟到通知和推进原退出责任，不再次准备来源。

测试专用 `DeferredPhysicalFaults` 暂存 broker 已调度的真实通知，不阻断线程池、不使用 sleep 或竞态重试，也不修改原生故障机制。Router 与宿主两个场景都确认：通知队列仍空时首次 owner pump 已 `Withdrawn`；宿主为 `Draining`，不再 Ready；旧 SDK descriptor/lease 不可用，无新增 pitch 写；GetTime 恰好执行一个真实原调用并返回原始 Single `17.25`。恢复通知后 Clock 不变，原故障对象被消费，七个实际原生根继续保留。

宿主场景通过测试反射绑定同一个 owned group/route/session，专门验证维护和进度节流行为；没有绕过产品正式安装或 Fresh 门禁，也不代表正式 22-role Fresh 正向。

最终完整执行为 [build-and-checks-r3/results.json](../work/r06-owner-fault-poll-20261119/build-and-checks-r3/results.json)，汇总为 [validation-r3.json](../work/r06-owner-fault-poll-20261119/validation-r3.json)。**12 个子进程和外层运行器全部自然退出 0**；1791 项当前输入、13 个受测 DLL 和七份 native 材料在执行前后身份相同，用户 README SHA256 保持。

| 当前执行 | 检查数 | 结果 |
| --- | ---: | --- |
| Gameplay 托管回归 | 238 | 通过 |
| 暂存通知的实际 worker Router / host | 25 + 26 | 首次 owner pump 立即撤权，迟到通知不复活，七根保留 |
| 既有 constructor worker / normal / foreign | 28 + 30 + 353 | 原始返回和物理退出保持；正常逻辑撤权、外来对象隔离通过 |
| Unity backing source recovery | 59 | 正常 backing 变化保持 Known；实际 Start/Pause 后新 SDK 变速确认，最终零根 |
| 真实 GetTime completion fault | 32 | 原 exact fault 对象消费和七根保留 |
| 正常暂停倍率 / publication in-flight | 131 + 81 | 正向写入通过；在途 publication Unknown 仍保留七根 |
| **合计** | **1003** | 仅统计最终 r3 当前源码 |

两个最终 Release 构建为**增量 0 errors、0 warnings**；本轮首次变更后的 solution 构建有 **55 条既有测试警告**，本次未消除。native helper 未改动、未重建；固定 SHA 的既有材料只用于本次实际执行，历史 PASS 数不参与验收。

全部失败记录保留。focused-r1 已通过首次同步撤权，但测试误要求普通游戏时间也消失，修为实际 descriptor/lease 撤权。完整 r1 的 Unity 测试同时创建两个独占写票，违反当前唯一 writer 合同；改为从真实 prepared transaction 读取同一 retained writer，保留原 capture/writer/commit 的过期拒绝。r2 还要求 optional native availability 消失必定增加 controller generation；当前合同保留独立 graph/run 读数并 Expire 旧 control lease，因此改为核对两视图同代次、旧租约 TargetExpired、无 native descriptor 和写权限。两处仅修测试，没有放宽产品 writer 或来源准入。seal-r1 的 PowerShell 换行语法错误也保留；只修工作运行器后 seal-r2 自然退出 0，没有重跑已过检查。

本批七项源码差异及六份真实改前副本均在汇总登记；原文件字节与最初 investigation baseline 一致。没有 commit/push、安装、启动游戏、操作 UI/账号或新发行包。两位现有 `gpt-6.1-sol / max` 代理再次只返回询问任务，没有有效交付，本批由 root 实施和串行复核。

下一离线工作沿正式 Fresh 的真实执行材料继续：现有 `NativePlaybackFreshSourceCapture.VerifyCurrent → BeginSourceOperation`、同票 adoption/Clock 和 watcher 已接线，勿重新实现；正式 scope 继续要求 foundation、Unity backing、LowerTime、EnginePitch、ProgressResource 和完整 22 source roles。本次十三成员 worker 切片不能提升为该正式准入。固定游戏 consumer 正向、R06/R07/R08 实机场景、真实 Speed/Seek、B01–B19 与 production 保持未完成，旧 R08 包不用于验本批源码。
