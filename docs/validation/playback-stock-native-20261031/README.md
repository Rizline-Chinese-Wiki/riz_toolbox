# Stock source native 原型分包

2026-10-31，`flavor=null`。当前独立 r6 在13个实际进程完成 **27037/27037 checks，13个 actual exit均为0**；每进程实际 Dobby Prepare/Commit八个自有source入口。新增fixture `.text` DIR64由COFF ADDR64、PE relocation和非首选映射后的实际值闭合验证。r4的26920检查和r5新检查假设错误的失败单独保留，不合并数字。

[正式报告](2026-10-31_Playback-stock-native-report.md)、[scope](scope.md)、[timeline](timeline.md)限定来源与边界。[evidence-index](evidence-index.json)列E/F/P，[artifact-index](artifact-index.json)列全部实际输入/输出hash，[native-report-integrity](native-report-integrity.json)封存报告且不自引用。[只读复核工具](Verify-Report.py)检查所有原件、十个owned当前源码与快照、13个exit和真实重定位地址。[当前PE/PDB/COFF证明](evidence/r6/build/native-symbol-source-proof.json)与[当前逐进程原始结果](evidence/r6/checks/test-result.json)可直接查看。

原件在 `D:/riz-build-cache-next/playback-stock-native-build-r6-20261031/`、`D:/riz-build-cache-next/playback-stock-native-checks-r6-20261031/`；源码快照为 `sources-r6/`。未调用真实GameAssembly方法或其DllMain，未启动/安装游戏、操作UI/账号、安装工具或改正式V7/helper。此为controlled lane，没有正式currentprovider、完整enginewriter覆盖或production声明。共同组当前两个成员与其独立计数由根任务另包验收，八个ownedwrapper不扩写成九成员。
