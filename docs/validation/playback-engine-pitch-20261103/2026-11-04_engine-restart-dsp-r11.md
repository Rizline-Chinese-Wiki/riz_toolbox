# Engine pitch 恢复测试的 DSP 续接修补

## 当前边界

R10 route 已自然退出 1，失败前完成 48 项检查。后继在前置门停止，五个后续场景尚未执行，未应用候选 Y。主目录 952 个受管文件和 364 个历史文件已重新逐项核对，全部保持原 SHA256。

本记录采用 docs-generator 的证据与结论分层。修补后的运行验收另由 R11 的实际构建、进程退出及场景摘要决定。[原始失败及留存清单](D:/riz-build-cache-next/playback-engine-pitch-draft-r1-20261103/engine-route-failure-analysis-r11/test-result.json)

## R11 实际进度

09时58分，route 已自然退出 0，55 项检查、实际 Prepare/Commit 各22、最终 roots=0。新 Start 后的 DSP 续接、实际 Pause、唯一后继 Clock 和完整物理组均已验收。原 driver 自动进入 engine-pitch-sdk-recover，其余五个场景继续按实际终态验收。[route 实际进程终态](D:/riz-build-cache-next/playback-engine-pitch-draft-r1-20261103/engine-formal-current-r11/engine-pitch-route/process-result.json)

五项目 fresh build 已自然退出 0。966 份源码仅两份测试文件变化，564 份产品输入保持 SHA256，四个产品组件共八份 DLL/PDB 逐字节相同，测试 DLL/PDB 均为本次新产物。构建保留两条既有 CS8602，位于未修改的 EnginePitchInitializationChecks.cs 第660行。[构建及源码归属](D:/riz-build-cache-next/playback-engine-pitch-draft-r1-20261103/engine-formal-current-r11/preparation-result.json)

新产物的五组 PE/PDB 与 554 份源码文档已实际校验通过，pwsh7 自然退出 0，两份修改后的测试文档均匹配 R11 源码。[符号验证](D:/riz-build-cache-next/playback-engine-pitch-draft-r1-20261103/engine-functional-symbols-r11/test-result.json)

六场景已于09时03分通过 WMI 启动。09时07分观察到 supervisor39760、driver41032、route12328 原句柄仍存活，成功场景数为零。每场景通过后会自动进入下一场景，任一实际失败停止。当前只签构建与符号，功能恢复仍待六场景终态。[进程与 checkpoint 留存](D:/riz-build-cache-next/playback-engine-pitch-draft-r1-20261103/checkpoint-r11-update-r1/test-result.json)

## 失败经过

R10 的 provider 写入 1.5、三层真实调用、Replay/Resume 原计时、direct engine 撤权以及真正新 Start/Pause 均走到各自检查。第一次后继 GetTime 返回的数值等于暂停位置，随后访问 Session.Clock 时发现 session 已撤回。Arm 的旧断言只有返回值相等，无法排除原函数回退碰巧返回同值。[实际 stdout](D:/riz-build-cache-next/playback-engine-pitch-draft-r1-20261103/engine-formal-current-r10/engine-pitch-route/stdout.log)

## 源码追踪

1. EnginePitchChecks 的计时夹具推进当前 player 的 DSP。按源码中的 schedule 和 elapsed 计算，Replay 后为 100.625，Resume 后为 101.25。这两个值属于源码计算，原失败日志未单独采集 DSP 值。
2. StartResourceInitializationFixture 新分配的 player 固定写入 Dsp=100。合成 DspTimeService 的 get_DspTime 从当前 GameplayPlayer 的 +48 字段读取。
3. LowerActualStart 采用新 player 后未迁移合成全局 DSP，因此后续读取回到 100。SingleReturnBridge 保留跨 session 的最后 DSP 值，会拒绝这次回退，并排入物理故障。
4. Router 下一次 Open 消费故障并撤回 session，最终在 Session.Clock 的注册检查被观察到。

现有 FakeNative.Handoff 已通过 SetRootedDsp 在换图时保持全局 DSP 单调。此次修补沿用同一夹具语义。[18 份原始日志与源码的逐 SHA256 留存](D:/riz-build-cache-next/playback-engine-pitch-draft-r1-20261103/engine-route-failure-analysis-r11/retention-manifest.json)

## R11 修补和验证要求

- LowerActualStart 保存原 player 的 DSP，在实际 Start、player 采用及 clip 采用后，将相同值同步到新 player 的合成 DSP 字段。保留实际分配、字段写入、初始化 receipt 和后续 Pause 流程。
- Arm 增加 GetTime 调用前后的健康度及计数检查。要求原函数调用和 Clock 应用各增加一次，读取回调增加一次，Declined 不增加，Clock 和 native policy 仍有效，同时不存在排队故障。返回值仍须通过原来的相等检查。
- 新源码在独立 R11 目录正常构建。四个产品组件的 DLL/PDB 与同源 R10 逐字节归属，修改后的测试组件使用本次新构建。
- 完整执行 route、同 Module 的双插件 SDK 恢复，以及 first/transfer 各 pending/zero-watch 四种初始化场景。每项均以自然退出、严格摘要和最终 roots=0 验收。

R10 的失败原件与冻结后继保留。R11 全部通过后，需要根据新源码重新冻结证据、应用清单和主目录验证入口。真实 Speed/Seek、完整 B01–B19 和整体 goal 仍待后续验收。
