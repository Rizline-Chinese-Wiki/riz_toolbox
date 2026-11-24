# 私有 group 验证时间线

时间为 UTC，上海本地时间加 8 小时；run 起止来自 wrapper 内的实际 `startedAt`/`endedAt`，其他观察明确按文件最后写入时间记录。

| 时间 / 来源 | 事件 | Evidence / work item |
|---|---|---|
| 2026-10-31 04:06:42.942，旧 wrapper 最后写入 | joint r1 normal 为 18/exit0，pitch-tail actual exit1；native active 标志未撤，失败目录保留 | E-FAIL / WI-GROUP-4 |
| 本轮 core freeze 前 | grouped ReadActivity 改为保留活动视图；SingleReturn ReportFault 在原锁中 once-only 按私有 revision 条件撤 native policy。固定进入 core-r1，不改正式 helper V7 | E-SOURCE / WI-GROUP-3/4 |
| 2026-10-31 08:04:40.201 → 08:05:29.872，主 wrapper | core-r1 新拷贝 + 单 Program 只读 ASLR/DIR64 overlay + native r6；fresh build、五 PE/PDB/source identity 和 19 进程全部通过，941 项 | E-GROUP/E-SYMBOLS / WI-GROUP-1/2/3/5 |
| 2026-10-31 08:04:51.214，joint r2 wrapper 最后写入 | 同 core 的 normal/pitch-tail/body/consumer 四进程 18+22+22+22 通过；不并入 group 计数 | E-JOINT / WI-GROUP-4 |
| 2026-10-31 08:12:38.191 → 08:13:20.738，新增 wrapper | 同 core/r6 的另一新副本 + 明确单 Program 行为 overlay；两个新进程 43+19、actual exit0，覆盖 Before/Returned/reader 真实抛错，无生产源码变更 | E-FAULT/E-FAULT-SYMBOLS / WI-GROUP-4/5 |
| 2026-10-31 08:19:55.028，独立 post-copy audit | 114 个 case-local 原件副本与 D 原件逐 hash 匹配；2600 个唯一实际文件身份通过。两份 scope 的四生产 PE/PDB 同 hash，所有 child stderr 为空 | E-FIXITY / WI-GROUP-5 |

所有执行均为自有 fixture 或只读 fixed-image 分包；未安装/启动游戏、执行 GameAssembly 方法/DllMain、操作 UI 或账号。未覆写旧失败，不将 native 13 模式、joint 84、旧 return 或其他模块的计数合并为本包检查。
