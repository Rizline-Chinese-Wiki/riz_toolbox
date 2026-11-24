# 私有 GetTime + SetPitch group 分包

2026-10-31，`flavor=null`。本包在同一冻结 core 源码和 native r6 上完成两份独立验证：**19 进程 / 941 检查**，以及新增 consumer/reader 故障 **2 进程 / 62 检查**；两份 fresh managed build、两个 runner 和全部子进程 actual exit 均为 0。19 模式包含 922 项冻结 core 测试原有检查及 19 项 D-only ASLR/DIR64 只读检查；新增 2 模式使用另一份明确的测试行为 overlay，未重跑或改写前一结果。

[正式报告](2026-10-31_Playback-source-group-report.md)、[范围](scope.md)、[时间线](timeline.md)及[证据索引](evidence-index.json)记录实际 PE/PDB/source、输出、exit、hash 和边界。主结果为 [19 模式 wrapper](evidence/group19/test-result.json)与 [2 模式 wrapper](evidence/faults/test-result.json)；全部原件保留在各自 D 盘新目录。

两个实际物理成员可在私有组中共同持续验证，也能采纳 registry 中真正既有的 GetTime owner；SetPitch 的两种 metadata alias 共用一个物理 detour。clean withdraw 仍保留 Known 与实际在途直至退出。物理/回调/reader 故障会 sticky 撤回双方，并在 bridge 原锁内按其确切私有 publication revision 条件撤 native policy。

这仍是 S1 两成员子项。foundation V7 的完整共同首次安装凭据未闭合，组合继续 unsupported；未签生产 FirstInstall、完整 stock source/provider/writer/run/handoff，默认真实 Speed/Seek 仍关闭。未安装/启动游戏、操作 UI/账号或执行 GameAssembly 方法/DllMain。
