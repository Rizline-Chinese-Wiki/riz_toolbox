# Stock source native 原型分包

2026-10-31，`flavor=null`。当前 native r4 已完成 **13 个独立进程、26920/26920 项检查，13 个 actual exit 均为 0**；每个进程实际 Prepare/Commit 八个自有 source 入口。证据只验证本分包冻结的十个 owned 文件及对应 PE/PDB/COFF，不将结果扩展到后续工作树、Runtime 共同安装组或正式 Gameplay provider。

[正式报告](2026-10-31_Playback-stock-native-report.md)记录 ABI、原异常、raw bits、ASM 状态、journal 责任及复现命令；[范围](scope.md)、[时间线](timeline.md)、[证据索引](evidence-index.json)给出授权、版本和原件 hash。当前结果是 [r4 对应的十三进程原始汇总](evidence/current/test-result.json)；独立符号证明是 [3 PE/PDB 与 5 COFF/C13](evidence/current/native-symbol-source-proof.json)。

本包执行自有 `RizToolbox.PlaybackStockFixture.dll` 的真实 native store/call。`normal` 中把 stock reset 后的 pitch 恢复为 2，发生在测试 return observer 的实际 setter 调用；没有据此发行正式 Clock/provider authority。未调用 GameAssembly、未证明生产 FirstInstall 或最下层 engine writer 完整覆盖，未操作游戏、UI 或账号。

原件保留在 `D:/riz-build-cache-next/playback-stock-native-build-r4-20261031/` 与 `D:/riz-build-cache-next/playback-stock-native-checks-r3-20261031/`。r1/r2 缺失 ASM DLL exports 的失败和 r3 的旧 10 进程/20028 结果均保留，各自独立，不并入当前计数。
