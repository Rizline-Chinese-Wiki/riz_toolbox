# GetTime 与 stock 来源的离线整合

本包已用新的 core-r1 源码凭据闭合离线整合：**27 套 2465/2465、六组件 Release、解决方案 0 error/0 warning、独立双插件 14/14**。core SHA 为 `8e3778772f18adcae23c2df42714f603e0c72d4e093920fbf298772083b59017`，不是上一批 core-r3。新增私有 GetTime＋SetPitch 共同安装组、typed native source/current scope、托管私有 observation/completion，以及固定 GameAssembly 副本的双 hook profile 联合。

[授权和范围](scope.md)列出实际交付边界。完整 stock provider、跨局 handoff、真实 Speed/Seek、生产首次准入和 B01–B19 仍未完成；未安装或启动游戏。

专项各自独立：native **13 进程 / 27037**；托管桥 **4 / 187**；安装组 **19 / 941**，另补 consumer/source 回调故障 **2 / 62**；映像联合 **4 / 84**。这些数不相加为统一覆盖率。映像中的游戏原方法、DllMain 与 source callbacks 均为 0；自有 fixture 中执行真实 Dobby/原调用。

修复 source 原异常未同步撤组、clean withdrawal 活动读取误判、fault 后 native policy active 未撤回、callback 内 Dispose 过早丢失完成责任等问题。恢复字节不复活故障 owner；正常撤回仍保留实际在途和完成责任。失败原件全部保留。

[总报告](2026-10-31_implementation-stock-source-integration-report.md)、[实际结果](test-result.json)、[证据索引](evidence-index.json)对应 `D:/riz-build-cache-next/foundation-stock-source-integration-seal-r2-20261031/`：**5787 个唯一文件 hash、763 条当前工作树输入**已实际回核。两个 group 专项只对各自 D 盘 Program.cs 加明确测试 overlay；所有生产源码来自同一 core，overlay 自身也有实际 PDB/source 证明。

实际专项分包：

- [原生 typed source 与 stock-shaped fixture](../playback-stock-native-20261031/README.md)。
- [共同组与实际回调故障](../playback-source-group-20261031/README.md)。
- [metadata / 固定正文与映像联合](../playback-stock-metadata-20261031/README.md)。
- [剩余七个物理目标的精确实现入口](../playback-stock-native-20261031/NEXT-SOURCE-HOOKS.md)。

当前进度与下一功能见 [checkpoint](../../../.docs/CURRENT-CHECKPOINT.md) 和 [N2 合同](../../../.docs/foundation-expansion/N2-STOCK-SOURCE-NEXT.md)。没有创建新的可安装游戏候选。
