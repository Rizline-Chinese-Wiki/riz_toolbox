# 未知视觉free结果隔离

2026-10-21。此前FinishReleaseEntry吞root/budget释放异常后移除账本并回收额度；本批保留失败责任与额度，未知free不自动重试。范围是有意义的内部故障注入，未声称真实il2cpp_gchandle_free已在游戏中抛错。

每个服务预先占一个有界退休责任票；owned资产和目标已有单独票。root/budget释放未知时，保留entry、依赖、预算与责任闭包，标记Quarantined；Tick不重试该free，相关模块Unsupported并说明需要重启，恢复不能另建空预算。独立资源继续清理。借用字体、目标根和临时扫描候选也受保护；正常服务退出后归还责任票。全进程最多512退休预约，原服务账本及目标/资产上限继续生效。

代理完成首版后，主代理补了退休owner移除条件：仍有Quarantined责任时不能从ProcessRoots撤回，即使另一个正常目标刚完成。回归包含借用根隔离后正常目标退休、独立兄弟资源清理、错误free只调用一次、预算不清零、模块拒绝恢复，以及责任票入场失败前不碰native根。

`red.log`为原逻辑69/70，未知root测试失败；`green-initial.log`和`green-final.log`均71/71。新包整合/安装另见foundation-local-catalog-20261021；不能把fake后端释放计数当作真实GPU/GC释放证明。

复验：仓库根执行`dotnet run --project tests/RizToolbox.Visual.Tests/RizToolbox.Visual.Tests.csproj -c Release`。最后一例故意保留未知free至测试进程退出，没有清空生产静态隔离项来伪装恢复。
