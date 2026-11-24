# Start 初始化与同局资源交接

2026-11-01 补充终态：忙碌交接75、连续两次同局重建130均实际exit0，合此前13模式547为15唯一模式752；单次70为补充范围不重复计入。证据`D:/riz-build-cache-next/playback-start-initialization-main-integration-r1-20261101/continuation-final-r1/handoff-result.json` SHA `20d86390015da750db1239b6b748d4ae7c773d65eeb66509e88786551e027f2c`，索引SHA `ca407cf0f0ec8286622a04de4ec7838075b09f13f11962d62082b21c1522c633`。原两项中断现场保留；没有借其checkpoint代替终态。另七个独立测试文件已更新为当前协议，实际7进程1863、30组PE/PDB对应，精确合主原件`D:/riz-build-cache-next/playback-source-v4-entry-update-r1-20261101/main-apply-r2/test-result.json` SHA `65ec9c6b3e053f513bdd44dc3a29497811621a0e1d9efdf217081512aa47c9d5`。本次未改产品；下方“仍执行”是此前记录。

2026-11-01，flavor=null。当前主树已精确合入 Source V4、private Init V2、条件七根捕获、正式 Router 接线和 SDK 服务代次，共64文件；846个声明输入与冻结候选逐项一致。完整 B01–B19 尚未完成。

## 当前已证行为

Start 的实际 Before 结束旧 source、Clock 和准备中的控制权；枚举器与 Progress 用两个独立 GC 根跨 yield 保留。只有同一枚举器的真实 set_clip 子调用、两个原生字段 store、父子 physical exit 与 raw AL0 对应后，私有 native 凭据才能进入同局交接。

typed capture permit 在 hold 下登记一个后继 source 的完整七根，前后验证实际图、宿主局和 native 记录。source open 后才释放初始化的两个根与其独占 observations。后继仍须后续真实 Pause/readback，依次建立 dormant publication、唯一 final Clock，再实际 arm。普通跨局交接继续独立要求不同 run。

正式单次恢复专项 actual exit0／70项：同一 Module、Service 与原双插件视图延续；旧控制票失效，新票能确认实际 pitch 与 Clock 变速；旧清理不撤新 Clock；实际 Replay、Resume 的 SetTime/pitch/schedule 保持选中速率；最终根和 observations 释放到0。该专项不重复前置速率事务，另有两次连续重建和忙碌交接专项仍在执行。

Service 的控制票代次与 native ControllerGeneration 分开。音源图或 native source epoch 改变结束旧票；私有已绑定 capture 验证原 SDK 请求后，才把内部 native 请求映射到真实 controller generation。可选的时间换算不可用仅使该映射不可用，独立时钟读数和仍有效的控制票保持可用；真实图或局变化仍结束旧票。

## 对应证据

| 范围 | 当前结果 | 原件 |
|---|---|---|
| 精确主树合入 |64文件，846输入，477产品输入（470cs＋6csproj＋1json）；git check/apply均0|`D:/riz-build-cache-next/playback-start-initialization-main-integration-r1-20261101/main-apply-r1/test-result.json`，SHA `80f0d8c5deba7b9db5b14bf6d3c809954c0abb0600f3936f019c570caea2aa04`|
| 当前矩阵 |28套2598/2598、solution0error/0warning、SDK双插件14、六Release0（1既有generated warning）|同目录`core-r4`、`full-r4`、`solution-r4`、`sdk-r4`、`release-r4`|
| 正式单次恢复 |1进程70、实际12Prepare/Commit、最终0根|`initialization-checks-r3/router-one-cycle.result.json`和对应stdout/exit|
| 初始化其余范围 |13进程547：native/bridge/hold/tracker11/472，Router在途停用37、宿主失效38|`initialization-checks-r2/progress.json`及各模式actual exit0|
| 当前主树独立初始化重验 |13进程547，当前主树fresh DLL，均actual exit0；五PE/PDB、488文档另对应|`main-current-checks-r1/test-result.json`，SHA `8d9fcf91dfa880e1a1ec341b54da04a5c9a96f3ae704238b582b9112bc02b496`|
| 当前源和矩阵事实回核 |846源与候选逐项一致，477产品输入，1621声明事实通过|`current-base-review-r1/test-result.json`；只证明已完成范围，busy与连续两次仍另待终态|
| 当前固定映像 |1进程31、实际12Prepare/Commit，游戏方法与DllMain为0|`D:/riz-build-cache-next/playback-start-initialization-mapped-r2-20261101/actual-r1/test-result.json`|
| 原8roles |14进程36675；当前native源|组合目录`native-eight-checks-r1/test-result.json`|
| 可选换算修复 |旧Service真实失败；单产品修复后12场景133全0|`clock-read-fix-r1/test-result.json`，SHA `0b88d4fff34d56cae93efff9b77a52a266a012f09930e840524263a2b5db4848`|

E=上表实际PID/命令、输入hash、exit与PE/PDB源码对应；F=所列私有交接及SDK恢复行为；P=唯一consumer→独立保留→真实退出→native移交→七根→后续Pause/Clock/SDK。各范围分别计数，不相加为完整底层验收。

已完成范围的文件索引见 [evidence-index.json](evidence-index.json)。独立有限审查核33份源码、56件证据和全部488份PDB文档，在所列同局交接、permit、所有权和Service代次合同内未发现产品缺陷；该审查不覆盖新first-capture或共享构造器增量，也不替代待终态的执行。

28套、六Release和SDK所用477产品输入与合入主树相同。之后仅独立 Start 测试与runner补了诊断、两个临时receiver的计数及单次模式；它不属于28套执行项目，当前专项fresh build与5PE/PDB另行核对。矩阵和solution验证范围不冒充这些之后的测试源码。原失败和无终态中断现场均保留，未改 native fault/hold 条件换取通过。

原busy PID21636和normal PID42756以及各父进程已证实缺席，日志只有检查点前缀；它们没有最终JSON或exit，不能列为成功。`continuation-final-r1`先验证当前846源、已构建DLL与native复用材料，只补这两个缺失场景。较长CPU执行按实际进程等待，观察超时不会触发终止或重复运行。

## 运行与剩余范围

使用 [Test-StartInitialization.ps1](../../../scripts/Test-StartInitialization.ps1)，明确提供当前 managed assembly、两个 helper、固定 owned fixture 和 Dobby 的绝对路径及新输出目录。该固定 fixture 有其确切私有 CALL profile；重新编译的 recipe 产物不自动取得该身份。新 runner 已实际执行 hold 模式1进程61项通过。

当前 SourceV4／168B frame／136B info、WriterV1；private InitV2／160B，joint helper snapshot96B／handoff info104B。尚缺没有前任 Session/Clock 的首次捕获、未知 lower writer、完整 source generation、真实生产首次准入、任意 Seek 的玩法重建、effect 完成与真实游戏 B 矩阵。生产 Speed/Seek 保持关闭；未安装或启动游戏，未操作游戏UI或账号。单次受控恢复通过不代表真实Unity生命周期、资源退休或完整 B01–B19 已验收。

下一功能已在独立D目录实现首次初始化捕获：只从宿主关联开始，使用单独私有native完成凭据进入条件七根捕获，后续实际Pause/readback才建立唯一Clock。它不创建虚构的前任publication；该增量当前尚未合入或验收。共享MUG构造器的receiver筛选与低层writer覆盖另行推进。
