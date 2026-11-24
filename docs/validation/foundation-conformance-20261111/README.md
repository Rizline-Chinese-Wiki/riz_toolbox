# 当前公共模块广域回归：2026-11-11

**原广域执行未全通过。19个套件完整通过；音频6项缺native夹具，分发首次调用缺参数。不得将此报告当成B01–B19整体验收或实机证明。**

所有21个项目与六组件均实际构建成功，731项输入执行前后相同。原始[结果](original-result.json)和[各套件汇总](suite-summary.json)保持失败退出码。

| 套件 | 退出码 | 实际汇总 |
| --- | --- | --- |
| 01-Core-checks | 0 | 24/24 |
| 02-Adapter-checks | 0 | 10/10 |
| 03-Hosting-checks | 0 | 20/20 |
| 04-Game-checks | 0 | 79/79 |
| 05-Sdk-checks | 0 | 10/10 |
| 06-Scope-checks | 0 | 30/30 |
| 07-Module-checks | 0 | 29/29 |
| 08-Mutation-checks | 0 | 21/21 |
| 09-Data-checks | 0 | 24/24 |
| 10-Visual-checks | 0 | 97/97 |
| 11-GameResources-checks | 0 | 32/32 |
| 12-GameAppearance-checks | 0 | 40/40 |
| 13-GameCamera-checks | 0 | 55/55 |
| 14-UiInput-checks | 0 | 79/79 |
| 15-Settings-checks | 0 | 43/43 |
| 16-Storage-checks | 0 | 14/14 |
| 17-Services-checks | 0 | 19/19 |
| 18-PackageLifecycle-checks | 0 | 14/14 |
| 19-Diagnostics-checks | 0 | 16/16 |
| 20-AudioContent-checks | 1 | 0/1, 0/1, 0/1, 0/1, 995/1001 |
| 21-Distribution-checks | 2 | 未执行断言/缺少汇总 |

## 分发补验

首次Distribution.Tests退出2，仅输出usage，没有执行14个断言。后续在全新的源码快照、隔离template hive和NuGet cache中执行仓库原Test-SdkDistribution.ps1，生成reference-only SDK、两个独立模板/服务探针包，实际正式宿主加载、停用/能力恢复/错误隔离验证 **14/14通过**。

[补验结果](distribution-followup.json)及[原日志](distribution-followup.log)单列，不改写原退出2。快照输入前后不变，旧SDK/ZIP/生产目录未覆盖，也没有装入或启动游戏。

## 音频待补项

默认AudioContent原执行 **995/1001**，6个Native UnityEvent joint案例因缺RIZ_INITIALIZATION_ABI_FIXTURE及对应helper/Dobby环境未完成。新独立native-joint工作区首次Build-JudgeContext脚本在rg未找到时失败，尚未C++编译；这不是当前音符writer的行为失败，也不应默认为通过。仅子进程工具环境正在诊断，不改全局PATH或代理/账号。

本轮不重启旧typed五例、不执行新性能基准，不借历史包验新合同。音频要求native夹具的行为补验与当前游戏实际ABI仍分开记录，B01–B19其他真实流程/资源消费者闭包未完成。[当前范围状态](scope-status.json)为准。
