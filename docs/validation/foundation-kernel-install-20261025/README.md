# Kernel 包的安装补充修复

本批修复实际分发漏项：核心构建23528产出23文件包，但 Install.ps1 的精确白名单仍是旧22项，实际自有临时目录安装在“Invalid package manifest”处被拒绝。新增 Kernel 的精确路径后，安装器继续严格核名单、哈希、归属和事务；没有放宽路径或接纳未知文件。

本批只修改 Install.ps1、Test-InstallSafety.ps1、Test-InstallRecovery.ps1 三个脚本，核心DLL和23包字节保持23528输出。实际结果以[摘要](summary.json)、[终态](terminal-observations.json)、[安全日志](safety.log)、[恢复日志](recovery.log)为准。修前真实失败保留在[日志](before-fix-safety.log)及[退出码](before-fix-run.json)，没有用静态猜测替代复现。

安全检查使用实际23包，在仓库 artifacts 下自建目录安装、重复安装、模拟旧22布局升级、拒绝被修改的Kernel、保留用户配置，并卸载未修改Kernel；不加载或执行这些DLL。恢复检查在独立合成仓库使用同样23项名单，主动终止自己创建的安装子进程，验证失败升级、硬中断、恢复被打断、损坏备份、锁、运行中拒绝和卸载。合成载荷不是实际游戏；其包顺序明确把winhttp放在SDK中断点之前，以保证已发生真实替换，恢复备份位置按journal中的实际替换行推导，不假定固定索引。

[7项声明输入](inputs-before.json)在本批执行前后核对。[3项覆盖](source-overlay-manifest.json)列出相对23528的精确前后hash；[现行599源码清单](source-final-manifest.json)和[源码ZIP](source-archive-check.json)由原599项减去这三旧版本、换入这三已测试版本组成，其余596项逐字节相同。原23528的sourceZIP、manifest、1617回归和终态原件保持历史不改。这是“核心完整构建＋安装补充验证”的组合证据，不是又一次完整构建或重跑1617的声明。

全部安装、升级、终止和删除只针对测试自己创建的 artifacts 目录与进程。没有安装到 Steam 游戏目录、启动游戏或改变游戏文件；旧已装18文件/326源归档及原游戏hash另有保全检查。运行核的早期绑定、生产V4、coordinator/GC、调度清理和完整B01–B19验收仍按[后续任务](../foundation-engine-kernel-20261025/next-integration.md)继续。
