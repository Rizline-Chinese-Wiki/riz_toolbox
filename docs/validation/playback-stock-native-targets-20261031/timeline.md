# 阶段记录

2026-10-31，全部离线。各实际 UTC 起止时间、exit code、命令与源 hash 保存在对应 build/test JSON，未从描述推断终态。

| 阶段 | 实际结果 | 保留位置与含义 |
|---|---|---|
| r7、r8 | 编译失败 | MASM 的 DOTNAME/代码地址类型错误，未执行 native 检查 |
| r9 | 编译 exit 0，A/B native 失败 | 手工 CHAININFO 与自动 `.pdata` 混排导致 RF 未排序；OS lookup 缺项，进程非零退出 |
| r10、r11 | 编译 exit 0，A/B native exit 1 | 仅独立 set_clip 原跳板 PC5 的 SAVE_RBX/frame+16 不成立；r11 明确记录 role6/independent=1/offset5/RBX=0；目标完整 UW 和四目标原 SEH 已完成 |
| r12 | A/B 1 进程/1078 检查全部 exit 0 | 独立 SAVE_RBX defer 到 PC6；目标完整 UW 未改，四新目标各自原 SEH 一次。此后继续 C/D |
| r13 | 编译失败 | Progress 等值分支布局超过预留晚保存边界；原件保留，未执行 |
| r14 target | 1 进程/2579 检查全部 exit 0 | 七个新前缀、nested reset/schedule、原异常、GPR/XMM、真实 Progress 等值/非等值分支首次整体闭合；不另加最终计数 |
| r14 final | 14 进程/36675 检查全部 exit 0 | 112 actual Prepare/Commit，所有进程真实 ASLR/RX `.text` DIR64、原调用与物理退出责任闭合 |
| r14 PE/PDB/COFF | exit 0 | 三组 PE/PDB 身份、五个实际编译对象 C13、九正文/十 metadata 导出与排序 RF/CHAININFO、PE DIR64/COFF ADDR64 全过 |

失败与成功目录全部以 `D:/riz-build-cache-next/playback-stock-native-` 开头；闭合文件集合及 hash 见 `artifact-index.json`，本批没有覆盖任一旧目录。
