# 过程

全部发生于 2026-10-30，本轮只使用本地 CLI。

| 阶段 | 原始目录 | 观察 |
|---|---|---|
| 确认缺口 | 上一包固定来源与 live owned 文件 | reader 与 transfer 原先分开验证；SingleOwnerIntegration 直接构造 DTO，native subscription callback 明示未验证真实 Delivery/ReadCurrent |
| diagnostic r1 | `D:/riz-build-cache-next/single-play-joined-diagnostic-r1-20261030` | native 链接缺两个 fixture admission 依赖；补上 Ready/Validate 占位，Validate 固定 false，没有建立 native admission issuer |
| diagnostic r2/r3 | 相同前缀 r2/r3 | native 与 managed 构建通过；测试在正向完成观察之前故意调用非法复制 frame 的 ReadCurrent，native 正确触发粘性 Unknown，导致正向断言失败；保留失败目录 |
| diagnostic r4 | 相同前缀 r4 | 六进程全部通过；复制 frame 拒绝已移到正常观察之后 |
| frozen r1 | `D:/riz-build-cache-next/single-play-joined-frozen-r1-20261030` | 七进程、严格结果合同全部通过；copied-read 独立进程，正常模式保持 clean native 状态 |
| frozen r2 | 相同前缀 r2 | source fault 后的 transfer 失效与非空 Pending 通过 |
| frozen r3 | 相同前缀 r3 | 最终版本：controlled 创建串行持有同一 bridge gate；abnormal/missing-post 的具体 Pending 原因入 snapshot；七进程与全部输入 hash 复核通过 |

各目录均未覆盖、删除或复用。最终采用 r3；r1/r2 的绿灯只说明对应来源，不替代最终 r3。
