# 执行时间线

下列 UTC 时间来自各 build/test manifest 的原始 `startedAt/completedAt`；北京时间为 UTC+8。`rN` 标识具体输入，不能只按日期合并。本报告阶段只读取、复制和校验既有证据，没有重新构建或执行测试。

| UTC / 北京时间 | 阶段及证据 |
|---|---|
| 2026-10-31 03:48:35–03:48:45 / 11:48:35–11:48:45 | native build-r1 编译/link actual exit 0，但仅 ASM `PUBLIC` 未成为 DLL export；`StockMethod*` getters 存在，`StockGetTime/StockSetPitch` 缺失。E-HISTORY 保留 exports 与构建 manifest，不能将 link 成功视为入口可执行证明。 |
| 03:50:50–03:51:00 / 11:50:50–11:51:00 | native build-r2 增加 executable runner、dormant journal 修复，编译 actual exit 0，仍有相同 ASM exports 缺口。E-HISTORY。 |
| 从 03:51:33 / 从 11:51:33 | checks-r1 对 r2 实物执行，八条已记录 child 均 actual exit `-1073741819`（`0xc0000005`），stderr 记录空 target/Prepare 拒绝。随后中断；`journal-overflow` 只留下空 stdout/stderr，没有 exit 文件。该目录为不完整失败，未给未记录进程补终态。E-HISTORY。 |
| 03:52:13–03:52:24 / 11:52:13–11:52:24 | fresh build-r3 显式导出全部 ASM targets/End，保留旧目录。E-HISTORY。 |
| 03:52:50–03:52:51 / 11:52:50–11:52:51 | checks-r2 对 r3 完成旧 10 进程/20028 checks，全部 actual exit 0；该范围不含 r4 的 worker withdraw/After 窗口/ASM caller 增量，单独保留。E-HISTORY。 |
| 03:55:43–03:55:53 / 11:55:43–11:55:53 | build-r4：native 永久 withdraw 允许 private owner+epoch 的 worker 操作，Arm 仍限 owner-thread/无在途/一次；增加 ASM caller 与检查。五个 TU 编译/link actual exit 0，冻结源和输入前后 hash 未变。E-BUILD。 |
| 03:56:40–03:56:42 / 11:56:40–11:56:42 | checks-r3：13 个独立进程/26920 checks、全部 actual exit 0，每进程八个真实 Dobby Prepare/Commit；全部 stderr 0 字节。E-RUN、E-EXITS 及逐场景 raw stdout。 |
| r4 检查完成后 | `Verify-PlaybackSourcePrototype.py` 只读解析实际 PE/PDB/C13：3 PE/PDB、5 COFF 源校验、两个 fixture target body/unwind 通过；结果原件见 E-SYMBOL。没有增加运行场景计数。 |
| 报告阶段 | 新增本分包报告、scope、timeline、十个 owned 源快照、原始证据副本、E/F/P 索引及只读 seal。源码、旧输出目录和 checkpoint 不改；仅十个 owned 固定版本属于本报告的源码范围。 |
