# 生产监听发布与只读采用

2026-10-24。25套 **1515/1515** 完整通过，493源/22包冻结未安装；root唯一构建，session 53963 exit0。Runtime160/Hooks171/Audio573/Visual71/UIinput79。本批是在固定映像中的真实发布及C#采用，不执行游戏代码；真实游戏验收仍缺。

预初始化 producer 现在先准备永久4KB RX块与3个动态函数记录，验证12处lookup和12个指令边界展开，再按 mid→三组pre/post→Unity父入口→runtime-invoke 发布九次。任一写尝试后不确定永久Unknown，不回滚或释放。V1 144字节布局保留并如实报告9次；新GetInfoV2 416字节记录六区、lane epoch、targets、stubs、表、页权限与首写前证明。C# RequireReady只接受实际V2及原生lane176，并再次核实际源bytes、relay/stub、表和页权限。

映像正常383检查；source/post/relay/chain各独立390检查，合计1943/5进程。每种篡改均导致V1/V2 Unknown，重入不产生第10次producer写；fixture自身篡改写单列为1。两版记录在Unknown atomic已置位且writer暂停期间各100次读取都未误报Ready，合计713检查。非法V2 header/reserved九种拒绝，调用方伪造identity被真实快照覆盖。CLR72检查/2进程实际RequireReady、runtime/双idle broker采用通过，listener仍未附加、调用0，不把idle当listener回调证据。

上批原生listener8349项、其他native/CLR fixture继续通过。完整构建保留111条去重编译诊断；不宣称零warning或B19干净环境全矩阵复现。真实游戏冷启动、listener→CLR 回调与 ledger、IL2CPP/GC、动态 delegate leaves、连续 mutation/provider 转交与取消、全消费者退休和完整 Retry/Return/Loader 仍未验收。RequestPlay 关闭，Pending 保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。完整 B01–B19 继续，仅 B03 整项与 B18a 子项已勾；游戏实测按用户安排暂缓。

源码相较491批新增2 / 修改14 / 删除0。源码ZIP SHA-256 `3a4f77966ce04fa76b8bb84b7404fe250d6db58eb21f3ac78b2c753896ffc3de`；源码manifest `7693e1bcc7d671f0a97180985ce18e8a03b9939a1fa364cd0d13d83bbf1bc6f1`；包ZIP `e8223edb3d7482e54d2b2e662acbe280661984b23c828dd50aadd53d2df47730`；完整日志 `acbdddd843fcf19c113206b0d97ed110e44aadfa3cd69c447cd3f7d295808e70`。before/post/current输入一致；旧1021安装18项、其326源归档与原游戏三文件独立核对不变。

仓库根PowerShell7复现：

```powershell
pwsh -File scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/listener-publication-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll
```

只映射固定GameAssembly的测试使用DONT_RESOLVE；无DllMain、游戏导入、游戏函数或IL2CPP init执行。真实callee硬件返回地址证据来自[上批自有fixture](../foundation-listener-native-20261024/README.md)，本批仅核实际source+5/+5/+6字节与真实OS展开，不合并为游戏调用证明。

[完整日志](build.log)、[终态](build-run.json)、[汇总](build-summary.json)、[映像证据](mapped-publication-evidence.json)、[原子Unknown](atomic-unknown-evidence.json)、[CLR采用](managed-adoption-evidence.json)、[源码冻结](source-archive-check.json)、[包冻结](package-archive-check.json)、[差异](changes-from-listener-native.json)、[有限复核](review.md)、[授权范围](scope.md)、[下一步](next-integration.md)。
