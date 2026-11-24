# 过程

2026-10-30，仅使用本机已有 Python、.NET 8 与 VS2019 BuildTools，无安装或 GUI。

| 阶段 | D 盘目录后缀 | 实际结果 |
|---|---|---|
| 前置审计 | `single-play-automatic-frozen-r4-20261030` | 只读 r4 与 NEXT；旧 automatic-instant 的 source site rows 为 0，outer parent 受控，不重跑旧模式 |
| 新 fixture | `single-play-automatic-duplicates-work-20261030` | actual 24/19/31/25/20；完整 native 存储、43 source CALL labels、store+barrier；托管 graph 从当时 native 存储读回 |
| frozen r1 | `single-play-automatic-duplicates-frozen-r1-20261030` | native/managed actual exit0，进程 exit1；新 fixture 的 Add 与 Execute 共用 target，broker 正确拒绝不同 slot/epoch 共享该身份；未修改失败原件 |
| frozen r2 | 相同前缀 `r2` | native/managed exit0，进程 exit1；真实 native 链已 Add 两次，source 4 行/2 事务与 15 次 TLS reference reads 均 known；generic MethodInfo 与真实 native 参数不符，以及 controlled reference binding 太晚导致旧 root set 未 enroll，托管 strict 拒绝；仅修新 D fixture |
| frozen r3 | 相同前缀 `r3` | native/managed/进程均 exit0，strict 通过；两次真实队列退出分别 Pending/可借完成区间；20 source rows/10 事务、83 reference deliveries；结果尚未逐行输出 raw journal 字段，不作为最终证据版本 |
| frozen r4 | 相同前缀 `r4` | 最终版本；增加 20 个真实 ReadRecord 的显式字段和 source14 E8/actual Caller+5 核验；三个 actual exit0、strict 通过、664 输入前后 hash 一致 |
| 独立只读复核 | `live-relevant-readonly-audit.json` | 15 个正式 reader/native 文件与旧 r4 固定基底 hash 相同；未发现须改 live reader 的故障 |

最终执行观察时间为 `2026-10-30T14:16:46.615785+00:00`，来自 r4 `test-result.json`。四个 frozen 目录与 D 工作输入全部保留，不覆盖、删除或改写失败历史。
