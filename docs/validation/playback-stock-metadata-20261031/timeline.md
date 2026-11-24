# 本包时间线

2026-10-31：读取 CURRENT-CHECKPOINT 和 N2-STOCK-SOURCE-NEXT，确认旧 2465/175/34/427 不验证本包新源；遵守用户离线与新 D 盘目录边界。

2026-10-31：与 root/group/native 确定 sealed metadata receipt、十个声明与 SetPitch 单物理 alias API。共享 enum 与 group lease 由 group 维护，metadata/profile 不改旧 clock profile。

2026-10-31：实际执行 `extract-fixed-playback.py`，14 个正文 hash 复核成功，正文内 relocation 数均为 0；只新增 set_clip 80 字节反汇编，游戏方法执行与 DllMain 均为 0。

2026-10-31：新增真实 metadata resolver、14 正文 profile 与独立 mapped metadata/code runner；root 注册显式 mode。

2026-10-31：共同组通知 build-ready 后，新建 `playback-stock-metadata-build-r1-20261031`，冻结 460 输入。实际 build/test 均 exit0，0 warning/error，44/44；冻结输入、live 输入、自有三个文件在执行末尾全部 hash 一致。最终 `test-result.json` SHA `620f55cde33d65ad97f359e8b6e409c34de550f58d17867b7002be009f217ac2`，未重跑旧专项，不调用游戏代码。

2026-10-31：最终核验 486 条冻结 source/artifact hash 成功。首次辅助 index 组装因 PowerShell 数组表达式把路径串接，未生成有效 artifact 清单；该 index 与明确 invalid 状态原样保存在静态 D 目录 `evidence-index-failed-r1{,-status}.json`，不作为通过证据。修正表达式后另建最终 index，构建/测试原件与三个源码均未变化。

2026-10-31：历史 `playback-stock-joint-profile-r1-20261031` 实际 normal18 通过，pitch-tail 在第19项失败。定位到真实 R4 native `MarkFault` 只置 unknown、没有撤回 authorityActive；未删除失败或降低 managed/native 同时撤回门槛。此前 joint runner parser 错误也保留在静态 D 目录的 `joint-runner-parser-failure-r1.json`，未把它算作一次实际 child 验证。

2026-10-31：group owner 在 SingleReturn bridge 同一发布锁内增加 once-only fault cleanup，只用私有 retained publication 的 exact revision 条件撤回 native policy；本代理保留原 joint 断言，并补实际 revision 与终止 authority 状态 JSON。四个自有 C# 源稳定后交 root 统一 core-r1 冻结，不改 root checkpoint、Source bridge、旧 clock profile 或 Gameplay CLI。

2026-10-31：root 的 `foundation-stock-source-core-r1-20261031` 实际成功后，运行新增 `scripts/Test-PlaybackStockJointProfile.ps1`，session54730 terminal exit0。自有新目录 `playback-stock-joint-profile-r2-20261031` 从该 proof 复制720个冻结输入，完整复制 native R6 / GetTime R4 包，独立 Gameplay.Tests build exit0、0 warning/error；五PE/portablePDB与467文档checksum全部匹配。

2026-10-31：joint-r2 实际 normal18、pitch-tail22、body22、consumer22 全部 exit0，共4进程84项。每进程双 Prepare/Commit、revision1→2→3；恢复变更后两profile、group、两member继续拒绝且 native authority inactive。游戏方法、游戏DllMain、source Before/After、GetTime original均0，metadata/graph synthetic、productionAdmission=false。manifest SHA `4c16c8500df365cfc3e495e97451005097c5961b5906a6984dfbb32bc10087b4`。本轮不重跑历史44或旧return/camera/N1/retirement/instant专项。
