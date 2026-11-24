# 取消请求观察与退休屏障

2026-10-24。**本批已把六类 Stop 请求接入生产观察代码，并用共享 Pending 屏障阻止请求期间的 Ready 推进和协程根回收。完整离线构建通过 25 套、1575 项检查；新包冻结暂存，尚未安装或进行游戏实测。** 这里确认的是“请求已被记录并保留责任”，引擎是否真正取消仍为 `EngineCancelled=false`。具体边界见 [scope.md](scope.md)，后续路线见 [next-integration.md](next-integration.md)。

## 这次推进了什么

- **从身份到请求账本。** 在上批 Coroutine/Enumerator 身份基础上，加入 IEnumerator、Coroutine、StopAll、两个 internal wrapper 和 string 共六类声明。账本分别保存 Requested、原函数返回、Abnormal、Unknown 和清理状态；嵌套 wrapper 保留各自来源、native sequence 与父关联，不把嵌套次数当成不同逻辑取消次数。
- **请求返回仍保留资源。** 同 receiver 的请求在读取身份 provider 前就设置共享原子 Pending。StopAll 保存当前记录的不可变快照；请求期间出现新 Start、代次复用或当前集合变化会转 Unknown。string 不读取名称内容，也不猜测名称对应哪个 Enumerator。空参数、无匹配和空 StopAll 仍记录为已观察请求。
- **堵住同一轮推进中的遗漏。** coordinator 在同步 activation/getter/graph 调用后重新检查 Pending；Ready 状态自身也检查。三类 tracker 和后端在结束确认、撤销、读活动以及每个独立根释放前后重核。普通 Start/MoveNext 观察继续，Pending 不冒充 native InFlight；若在部分清理后才到达，则停止后续释放并保留未知责任。

## 已通过的分层证据

| 验证层 | 实际结果 | 能说明什么 |
| --- | --- | --- |
| [构建终态](build-run.json)、[测试汇总](build-summary.json) | 定向 `69797`、完整 `45961` 均 exit 0；25 套 `1575/1575`，AudioContent `619/619`、Hooks `185/185`、Runtime `160/160` | 本批新增取消核心 8、launch 屏障 6、退休屏障 8 组回归均通过；检查数不是全覆盖率 |
| [自有目标的真实 Dobby 安装](operation-dobby-evidence.json) | 1165 次检查；69 次经 detour 的 original，另 1 次未发布原目标调用，总计 70 | 三类目标真实 Prepare/Configure/Commit、精确来源返回地址及有限异常/worker/嵌套路径通过；不是游戏入口并发首发证明 |
| [真实 native → CLR → 实际账本](operation-ledger-evidence.json) | 5 个独立进程、544 次检查、15 次实际 original；15 次 mutation 记录，另 10 个取消请求均配对完成和 Dispose | 三类真实原生回调进入生产 broker 和实际 mutation ledger；kind 0/1 同时进入实际取消请求 ledger。图、根、身份 provider 和初始化 idle 仍受控 |
| [源码检查](source-archive-check.json)、[暂存包检查](package-archive-check.json) | 534 项声明源码输入、22 项暂存包文件逐项哈希匹配 | 构建、归档和当前源码一致；不包含游戏运行或干净环境重建证明 |

五个账本模式为 `normal`、`original-seh`、`original-cpp`、`before-fault`、`after-fault`。每模式运行三类原生目标并进入实际 mutation ledger；其中两类同时进入实际取消请求账本。正常模式确认三次前后图捕获；异常与捕获故障模式不确认图完成，仍完成标量责任清理并保留 Pending/Unknown。原始异常在 native fixture 内捕获，不穿过 P/Invoke。

这些联测执行的是自有目标，不是引擎 Stop 函数；没有真实 tracker provider、生产对象后端、IL2CPP metadata/GC、生产 Stop binding 或游戏 receipt。Dobby 安装与 CLR 账本联测是两条分开的实证，不能拼成已经跑通的游戏端到端链。固定 Stop 入口依据见 [research.md](research.md)、[stop-abi.json](stop-abi.json)。

## 交付状态

[变化清单](changes-from-listener-mutation.json)对比上一批：声明输入从 522 增至 534，新增 12、修改 11、删除 0。[构建前后检查](build-input-integrity-check.json)与[当前源码检查](source-integrity-check.json)均无变动。源码归档 SHA256 为 `f7cf1791598aee855953ad5e33c662312fe96117d935c108430a9a2a9201a3d3`；暂存包归档为 `fce8cbae72b98d9a744e284ec8863a43f275c7465a7a004864d596d5abc6ce12`。

[旧安装检查](previous-installation-check.json)确认原有 18 项安装文件及其旧归档 326 项输入仍匹配；[原游戏检查](original-game-check.json)确认 3 个游戏文件未变；[暂存核对](staging-check.json)明确未安装。本批定向和完整构建没有失败终态。[完整日志](build.log)保留 94 条不同原始 compiler diagnostic 文本，包含 upstream、既有警告及 ANSI 差异，不等于 94 个独立问题，也不能称零警告。

仍未确认引擎取消完成、全部 mutation 写者和 delegate leaf、未来生产者封口及完整 Retry/Return/Loader 资源退休；没有新增整项验收勾选，整体 B01–B19 继续进行。复现入口为 [run-build.ps1](run-build.ps1)，需要相同本机依赖且会重新生成产物；阅读冻结证据不需要重跑。本批三个说明文档不属于 534 项冻结构建输入。
