# 正式 provider 在同一模块内持续跨局

2026-11-01，`flavor=null`。当前主目录已接入稳定私有 host provider，同一个 GameplayModule、Service 和原 SDK 服务视图可在正常换局后继续使用。换局间隙返回未就绪；旧局的来源、写入票、预备事务和控制租约继续归属旧局，不会改绑新局。精确 Session 登记校验也已修补，同时保留“原局已经逻辑退出后再交接”的正常路径。本报告只验下面确定的源码与离线范围，完整 B01–B19 仍未完成。

范围与用户授权见 [scope](../../../work/riz-toolbox-stock-formal-integration/scope.md)。未安装、启动游戏或操作 UI/账号；生产准入、真实 Speed/Seek 保持关闭。

联合结果：`D:/riz-build-cache-next/playback-stock-formal-main-integration-r1-20261101/validation-r1/test-result.json`，SHA-256 `14ffc05e0a8d9850e8bd1242024c8971736b5961f9673745902a9eedc9f20bf9`。索引 SHA-256 `e0dab7f91572910abd25a4a18407d8e69fc89d94050320d3817de4ff89db6c84`，入口为 [formal-stock-validation-index.json](formal-stock-validation-index.json)。索引纳入 7,919 个既有唯一输入，另绑定结果原件；收集器实际退出 0。

## Evidence

| ID | 原件、哈希与实际范围 |
|---|---|
| E-SOURCE | main integration `test-result.json` SHA `4998c076733d060559692ad1ca8b66ae9a95ce5fc9dbbd35417ba56ec6990bcf`。778→785 源、12 文件增量，实际 git apply check/apply 均 0；785 文件与冻结 ZIP 逐字节一致，其中产品 src 471 文件。ZIP SHA `299d9cb8286b5f651dc4a79e536473a3b101a16b0c20d8bda2c84bf0f2183c3e` |
| E-MATRIX | `foundation-stock-formal-core-r1-20261101` 760 输入通过；`foundation-stock-formal-full-joined-r1-20261101` 27 套 2465/2465。先前进程中断前 22 套已有真实 build/test exit0 及 progress；其余 5 套在 `full-continuation-r4` 真正执行。两批 780 个编译输入逐字节一致，没有把中断 parent 签成 exit0 |
| E-BUILD | `foundation-stock-formal-release-r2-20261101` 六 DLL Release、0 error/1 个既有 generated nullable warning；`foundation-stock-formal-solution-r2-20261101` 0 error/0 warning；`foundation-stock-formal-sdk-r1-20261101` 两个独立插件经正式宿主 14/14。各 driver actual exit0 |
| E-HOST | stage `delivery-root-r1/test-result.json` SHA `20e26d420c985d2c39dc7940cba415e314a87f7c31bd42e45536786f68c5d968`，索引 SHA `430082687ea0e4e3d7a3e13118453e1d7d0392952ea6e63faa8cae22b493ff69`。下表各组都使用修补后的源码及实际编译产物；4 组 managed PE/PDB 证明各 5 pair，共 1914 文档 |
| E-EXACT | 独立 `playback-stock-formal-host-review-r1-20261031/root-completion-r1/test-result.json` SHA `5b54b884a030b5d580f25e2931be9fe299a3b3919267e91f73346c6f73beb022`。3 进程/80 检查、10 PE/PDB/958 文档；内部复制 Session 拒绝后合法 host/native 仍有效，精确已退出前任仍正常移交。原反例与失败日志保留，不作外部 SDK 漏洞或广义 case-review PASS 声明 |

| E-HOST 分组 | 实际进程 / 检查 | 已证行为 |
|---|---:|---|
| formal | 8 / 10159 | 同一正式 Module/Service/SDK 双局；两插件作用域；handoff 间隙、旧资源清理、worker Release、物理故障 |
| prepared stock race | 1 / 621 | stock transition 与预备速率事务竞争时按现合同撤回；不放松已有 guard |
| provider | 7 / 1021 | 正式 provider、独占控制、来源与线程边界；4 个已完成实际子进程复用、3 个续跑 |
| clock | 10 / 179 | 当前唯一 Clock、实际 stock/native 返回、撤回与故障 |
| handoff | 6 / 1390 | 实际 joint drain、旧票与迟到回调、新局来源；含 66 局/65 次交接；5 个已完成子进程复用、1 个续跑 |

32 进程/13370 检查是上述不同分组的合计；独立 3/80 另列。封存脚本没有再次执行这些测试。native 3 PE/PDB、6 COFF、7 个编译输入链接只是既有 helper 的来源重验，不重复计入历史 388 项原生检查。

## Findings 与调用路径

F-HOST：`severity=n/a_re`、`category=design`、`status=validated`、`confidence=high`，证据 E-SOURCE/E-HOST。位置为 `NativePlaybackStockClockRouter.ProviderHost.cs`、`INativePlaybackRateProviderLifetime.cs` 和 GameplayModule。稳定 host 每次解析当前已 arm 的精确 Session；Module 按私有 lifetime 接口泵送与释放。来源、原局 Clock 与旧预备事务仍不可跨局复用。实际两个插件作用域及原服务视图完成双局 SDK 值请求与 native GetTime，而非只验证裸 Router。

F-EXACT：同级、`status=validated`、`confidence=high`，证据 E-EXACT/E-HOST。位置为 `NativePlaybackStockClockRouter.Handoff.cs`，SHA `9c515c3bfce25f1f191227f1d4dd4f8a58927c5678f7c208eb41bbd4cd90f51b`。BeginOwnHandoff 核 owner/thread、实际登记 Session、曾 arm、唯一 Clock/publication 及无 pending handoff，再核实际 group。它刻意允许精确前任已经逻辑 withdraw；内部浅复制不能借用原 publication。Bridge 的 SEH 修复 SHA `79e4947c066ad91ae56cba29db50cf61f8d091777d4456569a2f2afad3450f07` 保留。

P-HOST（`path_type=callflow`，E-HOST/F-HOST）：首局 actual Pause → 独立七根/API/host run → 唯一 Clock/native arm → 稳定 host → 正式 Backend/Service/Module → SDK SetSpeed → actual stock Resume/Replay/GetTime → exact joint withdraw/drain → 新 run/七根/route → 新 Session arm → 原 SDK 视图第二局继续值请求。旧局资源只清理自己的责任。

P-EXACT（同类型，E-EXACT/F-EXACT）：精确 Session 登记 → owner/thread 验证 → retained publication handoff → 已 withdraw 前任正常交接；内部 copy 在登记检查处拒绝，合法新旧状态保持。

## 构建限制与失败保留

单独的全新 solution 构建尚未由本项证明。source-only 续接目录首次 solution-r1 实际失败 2 errors：不在 `.sln` 的 Hosting 插件夹具子项目丢失 Release 配置，输出到了 Debug，而 Copy target 寻找 Release。完整回归正常流程会先单独构建 Hosting 项目并留下 Release 夹具；本批以 [Prepare-FormalSolutionFixtures.py](Prepare-FormalSolutionFixtures.py) 将同源、已实际测试的 25 个夹具输出逐字节带回续接目录，之后 solution-r2 通过。原失败保留，`cleanSolutionBuildProven=false`；真正干净构建修补在独立 D 任务中继续，不用本项绿灯宣称 B19 完成。

其它原件包括旧缺失 handle 的 full-r1/r2、前置传错 `-Modes` 的 runner exit1、预备事务与 stock Replay 竞争的首轮失败，以及旧 snapshot-r1 的内部 Session 反例。它们均不被改写成成功，也不用于验修补后的代码。

时间线：先完成 12 文件主目录整合，再核新源码专项及独立修补验证；会话恢复后仅续跑缺失 5 套；随后六组件、canonical solution、SDK 与联合索引通过。复核入口为 [Finalize-FormalStockIntegration.py](Finalize-FormalStockIntegration.py)、[Resume-FormalFoundation.py](Resume-FormalFoundation.py) 及原件中记录的 command/exits。收集器拒绝覆盖既有结果目录。

完整 source generation、未知 engine/field writer、生产首次准入、任意 Seek 与 B01–B19 总验收仍未完成。默认 RizlineAdapter 不签生产 provider，fixture ClockMapping 仍 unavailable；下一项继续已定位的 MusicPlayer 引用 writer 覆盖。
