# 执行时间线

本任务跨越 UTC+8 的 2026-10-30 / 2026-10-31；目录中的 rN 是实际迭代，不能以日期相同推定输入相同。

| 阶段 | 实际观察与保留原件 |
|---|---|
| 10-30 读入/分工 | 读取 CURRENT-CHECKPOINT/NEXT-FUNCTIONAL-GAPS；Runtime 与专用 fixture 独占，根任务负责 Adapter profile；不重跑已冻 27/2436、19/159。 |
| 10-30 安装原型 | 先落 concrete lease 与同全局地址冲突域 broker；Runtime diagnostic r4 Release actual exit 0、0 warning/error。 |
| 10-30 实际形状校正 | 根任务在非执行游戏映像副本观察 Dobby actual consumed=6，搬移 MOV/PUSH；此前五字节假设弃用。 |
| 10-30 展开反例 | owned managed r3 的 RtlVirtualUnwind 在 Dobby FF25 continuation PC6 失败，安装在 Commit 前拒绝；改用私有 RX R11 continuation thunk，完整保留实际 Dobby 原跳板。 |
| 10-30 / 31 r5 | native r2 W4/WX、managed r5 0 warning/error；checks-r1 实际 24 进程/386，14 Commit 全 exit 0。 |
| 10-31 首次封存反例 | `gettime-broker-precapture-counterexample-r1-20261031` 对 r5 Dobby `.text` padding RVA 0x3870 做 CC→CB，盘不变且未执行该字节；实际 leaseIssued=true、VerifyCurrent 成功。根任务授权修复。 |
| 10-31 r6 修复/重验 | 可执行 virtual bytes 依 pinned 文件及唯一 bounded DIR64 relocation 逐字节核；helper 在任何其 export 调用前预验。managed r6 0 warning/error，checks-r2 实际 28/414、14 Commit、28 个 child exit 0。 |
| 10-31 静态证明/冻结 | 实际 5 managed PE/PDB、21 portable PDB 编译源码 SHA256、2 native PE/PDB、owned CPP+ASM 与 helper COFF 源校验一致；持源码只读锁冻结 223 条。独立完整性检查 223/223、28 exits、414 checks 全过。 |
| 10-31 r6 正向反例 | 根任务映像 r5 在重复 profile 构造时因整段 VirtualQuery RegionSize 改变误撤回。独立旧 r6 实证：覆盖 88B 属性/内容全不变，只是邻接页 split 使 RegionSize12288→8192，旧 seal 实际拒绝。 |
| 10-31 r7 修复/重验 | MemoryFact 只封存要求的覆盖区间；其每个当前查询子区的 allocation/protection/state/type 仍须完全相同。邻接 split/merge、真实 heap grow/free 正向及覆盖第二页保护变化拒绝加入；fresh 29/427、14 Commit、全 child exit0，编译0 warning/error。 |
| 10-31 最终冻结/报告 | 新 freeze-r2：304 条、22 个当前编译源 SHA256、5 managed/2 native PE-PDB 全核；旧两个冻结/矩阵与两类反例保留。更新本 scope/timeline、正式报告及 index；生产 false、游戏/账号操作 false。 |
