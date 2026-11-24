# 内容事务独立只读审查

2026-10-21，代理 `battery_code_review`。审查范围由主代理指定：统一单项/批量登记、消费者引用、未知清理失败以及模块恢复。未修改生产代码、未运行构建或游戏。没有发现能从本轮范围内的正常 API 调用序列确认的新正确性缺陷；这是一轮有界静态审查，不构成完整底层验收。

## Evidence

源码根目录：`C:\Users\71957\Desktop\riz_toolbox`。以下为本次实际读取文件的 SHA-256；可在仓库根目录执行 `Get-FileHash -LiteralPath <表中路径> -Algorithm SHA256` 核对。

| ID | 文件 | SHA-256 |
| --- | --- | --- |
| E-01 | src/RizToolbox.Adapter/AudioContent/LocalContentService.cs | 1307681C35173280ADF14CF56FF395CC0CB71183D3E222DC83456E8000C069FA |
| E-02 | src/RizToolbox.Adapter/AudioContent/LocalContentService.Batch.cs | 1E89FA7363347D26E9450276E0D017D4492CFE4AD11B39F7A115619BBF2A7364 |
| E-03 | src/RizToolbox.Adapter/AudioContent/AudioContentModules.cs | 355D952A032BBDB76640DE929A65EA6D0E8898FDDC849265166C17FDD1EF0C20 |
| E-04 | tests/RizToolbox.AudioContent.Tests/ContentBatchRegression.cs | 2AEDEDA450E7971B50B93D2F134CA2137FEB6C529B6CD1C8E804E63F0469D675 |
| E-05 | tests/RizToolbox.AudioContent.Tests/Program.cs | 4B98938F47A9CB326522B9EC0932E7424D46334265474B6C139591026112B166 |

另外读取 SDK 内容契约、ContentFileAccess、ActionLease、生产 PluginScope.Own，以及 WorkshopChartModule 的关联调用，核对调用方和所有权假设。未重新执行既有 64 项用例，未把主代理所述 766 项整合结果当作本次新增执行证据。

## Findings

| ID | 结论 | 证据与位置 | 边界 |
| --- | --- | --- | --- |
| F-01 | Own 前不发布；Own 后重新检查取消、scope、parser epoch 和外部依赖对象身份，覆盖同项/批量回调失效 | E-02，LocalContentService.Batch.cs:130–150；E-04 Ownership/SingleOwnership/OwnershipDependencyWithdrawal | 静态路径与已有用例对应，没有本轮新增运行 |
| F-02 | 原发布引用和依赖引用分开计数；根 payload 清理成功后才递减依赖，失败根继续保留图及配额 | E-01 Release/CleanupPayload；E-02 WithdrawBatch；E-04 FailedRootRetainsDependencies | 任意 IDisposable 部分成功后抛错只作未知状态保留，不证明物理释放 |
| F-03 | 错误线程释放消费者先被拒绝，release 委托仍保留；模块恢复不会直接抛弃旧消费者或未知清理资源的预算 | E-01 ValidatedContentLease.Dispose；E-03 LocalContentModule.Probe/Release；E-04 WrongThreadConsumerDisposal/ModuleCleanupRecovery/ModuleRetainedRecovery | 主线程内部消费者的约束；不等于真实 GC/free 故障测试 |

上述结论属于已审查的实现行为，未发现新的可报告缺陷。特别检查了回调中撤回依赖、改变 parser、退出 scope、处置 store、处置尚未发布 registration，以及同批前向依赖和跨批旧对象撤回，未识别出能够越过最终复核的发布序列。

## Path

P-01（callflow，E-01/E-02/E-03）：有界索引复制请求和依赖 → 校验 DAG、格式及存量预算 → 解析并记录阶段资源 → Own → 复核所有权/取消/parser/依赖 → 预分配字典并原子切换发布状态（F-01）→ 整组先撤回发布 → 递减发布引用 → 最后消费者释放根 → 成功后递减依赖，未知清理保留（F-02）→ 模块停用后等待旧消费者或拒绝未知清理下的恢复（F-03）。

## 后续验收边界

本报告不覆盖 WorkshopChartService 的所有异常释放路径，不证明新增歌曲目录、音乐/封面实际消费、结果/重试引用链、真实 native free receipt、长期运行或压力测试。未知清理资源当前按设计保留到进程退出；需要物理释放证明时仍须单独实测。CPU 登记成功不应升级为完整 B15 游戏接入完成。
