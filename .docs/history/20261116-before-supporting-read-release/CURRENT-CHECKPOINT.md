# 当前续接点（2026-11-16，优先读本页）

## 当前：group-read-r1 真实接管已返回；supporting-read 下一修复正在实施

`artifacts/delivery-20261116/RizToolbox-0.3.0-group-read-preview-20261116.zip`，37,305,351 B，SHA `e705d78faa6bc0cb6d106da7a6d3f0a9efeb3fd3ef314b9742f3c59afc747a4c`；35成员读回、25运行文件安装匹配。安装原exec46346自然0/2c182d，`install-group-read-r1-command.json` / `install-group-read-observation.json`。初次解压后重复创建已有.tools目录即停止，未安装；第二次才执行实际安装。旧ZIP未覆盖。helper未改，仍为88导出/SHA `d099ae13726fd0ac94abf906506a99190de2260da704e6f91bea27b2e5164823`。

15:36:57官方Steam2272590请求启动，实际新PID13420，15:36:58启动；本包已实际进入菜单与 Greatness Earth/Axya/EASY4/FREE。session `1916f94d63764d0981bc0f358240e0b8`，run `ac38e144d6b54abdabede366cc73f80b`。07:46:18 UTC首次Clock从315.6156334到345.0745908，**29.4589574秒**；07:46:35.7002150 CapturingSource，**07:52:41.3948585 WaitingForPause，真实接管已返回，约365.6946435秒**。两栈从Capture推进到真实Adoption/StockRootedSource构造，不是持续停在同一点。仍有每轮约30秒阻塞，未实测SDK Pause或Speed/Seek。

CUA Alt+F4请求后实际进程与窗口均消失（15:58:52前已无13420），本批没有Stop-Process；没有OS退出码、RunEnded或正常通关证据。`group-read-r1-after-exit-observation.json`25文件匹配；原日志、两实际只读栈与完整标量抽取见 `group-read-r1-game-result.json`。不把新源码借本旧候选验收。

## 正在实施的新源码（尚未构建、打包）

Sol已冻结Adapter三文件：ReserveAdoption一次返回同次adoption/binding/origin纯投影，private注册仍先于完整VerifyCurrent；ActualSource.Read外层只持有显式读责任，内层首尾graph及各真实getter独立fresh保留，防止同步关闭后七根提前释放。不是验证成功缓存。SHA见代理清单，根agent待统一构建。

另一Sol正在沿现有VerificationTraversal显式传票，去掉同次members/pair/proof递归中的stock/lower/Engine/LowerTime父级supporting重复检查；目标验证入口/出口仍完整fresh，所有member本地bytes/cache/metadata/relocation/unwind与私有注册保持。依据新第二栈 `group-read-r1-source-b-stack.log`。native helper不改，旧API/无票独立操作仍完整fresh。此实现**尚未完成/冻结/验证**，不能用group-read的11/9/4步或游戏结果签新源码。

## 本批新实现与冻结

只改Runtime两文件：`ExactHookBroker.Playback.Group.cs`（SHA `fdcb9c69ef352a630f7fa14c90c002a73d04991af8d9d23b2265cf682aa0029d`）与 `NativePlaybackExecutableSeal.cs`（`7d770838dcf25ccd5f73adb257198afe9b0aad59937e9b7f477cc3c2707e2b98`）。

一次Group.VerifyCurrent在原两把锁内持有显式私有VerificationTraversal，targets在入口/出口完整检查；images/composition/members中间只消费本次票，避免每个section重复完整targets。票核self/实际group注册/thread/两锁/active/sticky/generation，并封装原foundation scope。foundation仍两端完整验证，owned无foundation也必经scoped路径。canonical/整节bytes/member/getter/root检查不跳过；独立无票方法、安装和native操作仍fresh，无ambient或跨调用缓存。错误上下文Dispose直接拒绝，但不宣称所有错误Dispose都把票置unknown。独立Sol静态审查无阻塞，产品源码已冻结。

上批source-read仍包含：一次foundation整组遍历的入口/退出完整验证与中间私有ranges共享；两组构造时ReadConstructionIdentity合并纯Binding/Origin投影，BorrowRoots及跨getter验证独立。clock-read顶层Read/转换显式consumer scope仍保留，Seek/SetSpeed准备独立。不重复这些已完成接线。

## 本候选新验证（不借旧包结果）

- `group-read-r1-result.json`11步全0，exec56405自然0/219e4b：fresh owned fixture，Loader六组件/Gameplay/Examples/Hooks Release，Hooks210/210、clock14、host3、Gameplay201/201、旧fresh职责机制31。31没有正式Adoption正向。
- `group-read-owned-r1/result.json`9步全0，exec81245自然0/ddc75c：新MASM与managed、四普通机制模式、真实自有Judge/Coroutine/Coroutine-tail。当前Runtime/Core/SDK/helper与包一致，不实例化stock foundation或Unity。
- **额外实际无foundation组正向已执行**：`group-read-traversal-r2/result.json`4步全0，exec80444自然0/437c3d。当前StartInitialization runner与当前Runtime/Core/SDK/Adapter，固定9fe8cde…自有Start fixture作为未改输入；raw99、SEH44、interference61，共204条运行断言，每进程12次实际Prepare/Commit。不是204个独立测试，也不是stock foundation或游戏接管通过。所有输入/执行DLL字节在打包时再次核对。
- 额外r1尝试保留失败：旧11-member fixture缺StockMethodPause，2checks即退出，未达到新Traversal；当前SourceRoles已有StartMoveNext，不修退休旧runner、不放松profile。改用匹配现行合同的固定Start fixture，新r2单独执行三模式；不拼接旧结果。
- `pack_proof.py group-read-r1 source-read group-read`自然0/11d730。额外打包门禁要求上述新11/9/4步骤、同字节inputs，35成员读回。native未改，不重复原生长矩阵。

## 最新真实游戏依据（已退出的旧候选）

source-read-r1实际42604/session `e647e5c8e7d24de98d6a2cf0031cecbf`，GreatnessEarth Axya EASY4 FREE，run `42a325b16b93412188794826aea41931`。07:13:47 UTC首Clock从monotonic89.689419到130.496296，40.806877秒，单位语义可读；07:13:54 RunStarted，07:14:10 Host CapturingSource。结束前归档仍未见接管返回，逾5分钟无响应。新只读栈 `source-read-r1-source-a-stack.log` 在TryIssue→VerifyStockFreshSource→group Compose/成员RangeSeal，无新镜像firstDiff。CUA Alt+F4正常退出请求仍无响应，15:19:31核精确PID/路径/StartTime后仅结束42604，CUA确认窗口消失；`source-read-r1-exit.json`。不是正常退出或RunEnded，更不是新group-read通过。

三实际候选首Clock单样本294.3280863→50.2224856→40.806877秒，原件哈希与25文件匹配见 `source-read-clock-comparison.json`；不是延迟分布或Speed/Seek验收。logger会截断大JSON，只消费完整标量，不解析截断尾部。更早clock-read 41872 CapturingSource约9分钟未返回，14:50:08精确结束；原件见上批历史。

## 下一工作

1. 先完成新supporting-read Runtime显式票与Adapter两处最小去重，审查同次固定引用/双锁/thread/generation、入口/退出full fresh、跨getter/root责任；构建串行BelowNormal，不让代理另开构建。
2. 新源码统一构建与必要定向检查，使用新目录/新结果；Sol正在准备匹配现行23成员lower-time profile的focused自有组验证，不修退休的20/21旧runner或放松pin。新候选需要真正覆盖双映像supporting路径，普通无foundation12成员204断言不代替它。
3. 新包生成后再安装并实测本新候选真实接管、SDK Pause→变速/Seek与恢复；当前game已退出。完整保留本新候选run/session，不能用本group-read或旧包签新源码。继续七根/完整Seek与HoldEpoch、实际DSP/音频精度及其余B项；未知改写仍按真实永久owner/原跳板/配置回读修，不猜RVA白名单或live rebaseline。
4. 完整SDK/B01–B19/production engine未完成。相机X/N1/退休批/rate/float/Play移交已完成，不重做；性能基准后置，当前数十秒/分钟主线程阻塞属于可用性故障。

## 约束与证据位置

用户已允许必要游戏操作和恢复实测，无新停止信号，不为旧Escape重复询问。代理Sol/max，构建串行BelowNormal；不操作账号/认证，不commit/push，不做旧兼容。主README保持SHA `CFBAC4691D36EDC5DABA513AA62D5856AE61E29BF553D0DC2979832750847364`。

原件 `work/game-seek-20261115/`；报告 `docs/2026-11-16_late-hook-composition-report.md`；交付说明 `artifacts/delivery-20261116/交付说明.md`。本次更新前两原页逐字存于 `.docs/history/20261116-before-supporting-read-checkpoint/`，group-read及更早见其中历史链接。旧ZIP不改。
