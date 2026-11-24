# V4 原生发布与同进程 CLR 联合验证

2026-10-25。自有宿主的联合链路、26 个声明的启动故障和两个动态加载拒绝场景均符合预期。生产游戏启动许可仍关闭，新候选未安装；本批没有运行游戏，也没有完成 B01–B19 的整体验收。

已跑通：自有 EXE → 自有目标 DLL → Bootstrap → Kernel，在依赖初始化阶段发布四个固定入口；原生初始化及工作线程调用完成后，同一进程启动 CLR，C# 只读核验 V4，再接四个共享观察器和每通道两个测试消费者。真实 Doorstop → Bootstrap 的普通依赖、生产采用和协调器仍待实现。

| 正常/托管场景 | 实际子进程 PID | 原生记录行数 | 结果 |
|---|---:|---:|---|
| 原生冷启动、四个初始化阶段、三个工作线程 | 16640 | 32 | 退出 0 |
| 同进程 CLR、四通道、每通道两个消费者 | 42880 | 36 | 原生与托管通过，PID 相同 |
| 捕获期间解绑，已捕获职责继续完成 | 34752 | 41 | 原生与托管通过 |
| CLR 接入后原始 SEH 异常与清理 | 41608 | 37 | 原生与托管通过，保留 Unknown |
| CLR 接入后原始 C++ 异常与清理 | 61328 | 37 | 原生与托管通过，保留 Unknown |
| 纯原生四通道两种原始异常 | 52496 | 40 | 退出 0 |

六例共 223 行真实调用记录，Start 内的嵌套调用另计；这不是 223 个独立测试。原件位于 `C:/Users/71957/riz-build-cache/engine-publication-r4-20261025/preflight-*`。只读复核 [preflight-reviewed-facts.json](preflight-reviewed-facts.json) 通过 2549 条离线断言，不是另一轮进程重跑。托管消费者完成/释放计数与实际原生调用对齐；Kernel 结果为直接 GetInfo 的 JSON 投影，这六例未单独保存其原始 112 字节快照。

26 个故障由 `run-fault-matrix.py` 实际运行，其中 8 个在源代码写入前拒绝，18 个在已有写入责任后 fail-fast。预期失败的退出码不等于测试失败。父调试观察器从创建进程起记录真实异常和退出状态，并对 18 个终止场景独立读回四个完整函数体及页保护；包含部分写入、保护恢复失败、缓存刷新失败和前序已提交入口。8 个未写入场景有真实 typed 原调用及 V4，但没有父观察器终端源页快照。独立离线核验见 [fault-matrix-verified-r2.json](fault-matrix-verified-r2.json)：26 例、2035 条断言、0 失败。

动态拒绝由 `check-dynamic-refusal.py` 实际验证 owned 和 production-candidate 两个固定 profile，PID 为 58424、44204，均退出 0。动态 LoadLibrary 不取得静态启动许可；首次 DisabledNoWrite、Verify 后 Unknown，重复加载不复活，四个 Kernel 通道始终未配置。每例保存三份原始 V4 和三轮四通道原始 Kernel 快照。没有加载目标 DLL 或调用游戏函数。

因此，本批是 **34 个主要实际进程场景 = 6 + 26 + 2**。18 个调试父进程、2549/2035 条离线断言、7 组模型检查和构建变体均单列，不能相加成更多独立进程测试。原生 r4 的 28 个 Bootstrap 变体构建成功，共产出 31 个原生二进制；托管 build 6 成功且 0 警告/错误。上批完整 25 套 1617 项回归本批未重跑。

本批修复过 ImageBase 的 ASLR 误拒绝和静态启动非空 `lpReserved` 的契约误判。ImageBase 仅允许磁盘首选值或实际模块地址，其余头字段和整个磁盘文件 SHA 继续核验；`lpReserved` 仅作为不可解引用的静态启动事实保留。早期编译/链接、原观察器和运行失败原件保存在 `history/` 与历史缓存，没有覆盖成成功结果。

最终归档由 [archive-final.py](archive-final.py) 生成：649 项声明源码及 7 处旧源变化/23 项新增，31 个原生二进制与实际托管产物，r4 完整原件，以及旧缓存失败历史分别归档。它只读验证输入并创建新 ZIP，不构建、启动或安装；运行完成后以 `summary.json` 为准。当前环境检查 [environment-current.json](environment-current.json) 确认 18 个已安装文件、4 个原游戏文件和 23 个旧包文件 SHA 一致。

复现、证据哈希和范围见 [技术报告](../../2026-10-25_底层-V4发布与CLR联接-report.md) 与 [scope.md](scope.md)。下一步直接执行 [游戏组件接线施工表](next-production-admission-review.md)：候选 Doorstop 普通导入与失败责任 → 固定部署 → 生产许可分支和只读协调器 → 对象/GC/调度/消费者收尾 → 用户恢复后游戏实测。完整验收以 [SPEC](../../../.docs/foundation-expansion/SPEC.md) 和 [CHECKLIST](../../../.docs/foundation-expansion/CHECKLIST.md) 为准，仅 B03 整项、B18a 子项已正式验收。
