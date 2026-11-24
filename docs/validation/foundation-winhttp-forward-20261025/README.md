# 固定系统 WinHTTP 转发：已通过自有宿主验证

2026-10-25。21 个独立进程全部通过，root 实际观察 session 40901 终态 0；5653 条父核验、2426 条宿主检查、456 次 typed API 调用及 132 组直接/转发对照分列。生产形态和测试形态各完成真实 TLS/C/C++/DllMain 四阶段以及 loader 外调用。

这完成了独立转发组件，尚未接入生产 Doorstop 或真实游戏。系统加载/解析只在 loader 外执行；初始化阶段仅测 Close(NULL) 与无 PAC/WPAD 的本地无效参数调用，不能推论任意 WinHTTP 调用在 DllMain 内安全。

- [正式报告](../../2026-10-25_底层-系统WinHTTP转发-report.md)、[汇总](summary.json)、[实际终态](terminal.json)、[成功命令](attempts/r4/command-result.json)。
- [613 项源码清单](source-final-manifest.json) / [ZIP](source-final-inputs.zip)：`ab5337683eb06df481a5101afadf3f6d4df04b10406d547fe46c431e28817ecc`；此前 604 项均保持原字节。
- [4 项二进制](binary-manifest.json) / [ZIP](binaries.zip)：`17c6a4ee2db5b87e252bc60f22a8a0404de018ebcdd95ecc13a21c1a7e11e45c`。
- [260 份原件](raw-manifest.json) / [ZIP](raw-inputs.zip)：`e0c59b3d6efe6fea2461e351f386668ce38a5226f36acca5fe9bb67224b9549b`；[21 进程原始汇总](raw/cases/fixture-result.json)、[PE 证明](raw/cases/pe-proof.json)。
- [初始化失败及修复审查](importer-initializer-review.md)、[验证器审查](review-final.md)、[后续接入](production-next.md)、[范围](scope.md)。

初编 SDK 警告、r2 的 TLS/CRT 初始化顺序错误、r3 的 C/C++ 入口被优化丢弃均保留于 history，分别记录实际失败，未计入通过。23 项已有包及游戏 18 项安装文件、4 项原始文件 hash 均一致；本批未重跑完整 1617 项回归，未安装或运行游戏。
