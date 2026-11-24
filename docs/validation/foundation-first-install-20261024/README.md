# 原生启动观察通道与晚接入

2026-10-24。**1511/1511、25套；Runtime160/Hooks167/Audio573通过并冻结暂存，未安装。** 新增native bootstrap观察通道与adopt-existing托管桥：每次Enter固定一次immutable observer快照，旧调用即使跨过Attach，也不继承新Mid/Exit；新调用三回调按实际native token/原caller/returnPC配对。CLR只采用已经配置的slot/Target/Original/Entry/epoch，不再次Dobby或Configure原通道。

## 实际通过的范围

独立native fixture有698个断言，2个旧调用在Enter尚无observer、原函数Mid之前等待，Attach后Mid/Exit仍零回调；18个新观察调用包含3次foreign-MethodInfo递归、2次原函数SEH/C++以及6次Enter/Mid/Exit observer SEH/C++。每次observer故障后同线程正常调用继续核token/flags；准确私有TLS归零同时依赖源码finally审查，不能仅用InFlight归零推断。实际mid为MASM CALL，不合成Frame。

新增Runtime联合用例在真实Dobby/MASM/helper上采用slot30：初始化前直通一次、旧worker跨Attach不借新Exit、三次正常/原SEH/C++配对、新lease Verify/Release、CLR observer故障后原函数仍一次且bridge Known=false。3个Hooks回归单列ABI56/40、身份/附着状态拒绝、参数拒绝。原helper ABI不变，总31 exports，新增Configure/Attach/GetInfo；Configure自身不发布代码。

实际Doorstop pre-init安装器/生产producer与首次安全发布尚未实现或验收，游戏mid未发布。bootstrap与旧allocator共用native表的统一槽责任、broker受限采用、真实IL2CPP/listener/GC、连续mutation/provider转交/取消、全消费者资源退休及完整Retry/Return/Loader仍缺。Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false，RequestPlay关闭。

## 构建与证据

453源/18暂存，较1507新增8/修改4/删除0。源码ZIP `fcdba98f9c3b160ac0ac64e38575a5bcab6406d1935964a2fa82b2efacb5381c`；暂存ZIP `a3e5ec54a8f4c6be5a51766162ec309cebb08ca47c4bd90d36ea08de340ec597`；full exit0，日志SHA `6e01a7e68ea8eac671909b73105c4166dabe2f1dc60d118db89cc415f2ee5ac8`。旧1021安装18项、旧326归档及原游戏三hash匹配。最终完整日志0条不同编译诊断；首次full exit0单独保留，静态审查修正旧Info快照竞态后再次完整构建通过。

[完整日志](build.log)、[汇总](build-summary.json)、[runner记录](build-run.json)、[首轮](build-before-read-race-fix.log)、[原生结果](native-bootstrap-evidence.json)、[源码归档](source-archive-check.json)、[暂存归档](package-archive-check.json)、[与1507差异](changes-from-1507.json)、[有限审查](review.md)。命令（仓库根、PowerShell7）：`scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/bootstrap-attach-final-20261024`。固定缓存/工具链与干净环境B19复现另行验收。

修正了一处实际并发因果错误：ReadActivity先保存此前是否见过Attached1，再读取native Info，以此判断合法旧Attached0快照，不能用别的线程随后看到的Attached1倒推本次旧读取失效。此改动有静态因果复核和完整回归，尚无确定调度的并发故障注入；Attach已发布但Configure未返回时按实际native Info报告Known，无等待安装锁。

23范围/1706指令/6E8和三输入hash继续[核对](terminal-evidence-check.json)，[来源](static-evidence-origin.json)单列。本批无安装、游戏/UI操作、游戏进程终止、原文件修改或commit/push，游戏仍1021。未验证>256 TLS深度溢出、任意跨槽/observer内再入或并发Attach争抢。

## 后续

[已选启动路线](recommendation.md)、[方案边界](options.md)、[固定发布事务设计](installer-design.md)及[具体接线](next-integration.md)。必须先实现实际producer与broker采用，再接真实游戏listener和全run闭合。[完整标准](../../../.docs/foundation-expansion/SPEC.md)仅B03整项/B18a子项勾选，用户“实测稍后”保持。[授权](scope.md)、[正式报告](../../2026-10-24_底层-启动观察接入-report.md)。
