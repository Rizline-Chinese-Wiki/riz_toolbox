# Rizline 底层补全第一批实现

2026-10-19；桌面 riz_toolbox；框架 0.3.0 / SDK 1.2.0。项目职责为公共底层与架构。本批落地运行时、兼容契约、作用域和诊断，后续通用 Hook、对象定位及游戏服务仍按 [任务清单](../.docs/foundation-expansion/CHECKLIST.md) 推进。

## 交付结果

| 领域 | 已交付 | 实际验证范围 |
|---|---|---|
| 原生类型与调用 | 字段/参数/返回元数据检查、标量/枚举、已知结构布局、根/线程/参数释放 | 43 项回归；实际启动的 UI、资源、场景、数据路径通过 |
| SDK 兼容 | 结构化结果、版本范围检查、能力说明契约；程序集身份仍为 1.0.0.0 | 真实保留的 SDK 1.1/旧插件二进制，正式加载器及宿主执行，13 项通过 |
| 子作用域 | 手动/场景/局边界、取消树、事件与资源归属、迟到任务拒绝、队列状态 | 19 项回归；真实游戏面板子作用域创建与停用取消通过 |
| 能力说明 | 可用性、版本、代次、操作、线程/清理/采样语义 | 宿主实时提供，状态包含 10 项；不表示这 10 项全都可用 |
| 诊断 | 会话 JSONL、上下文、限流/轮转、计数、故障隔离、单会话导出 | 16 项诊断、9 项导出；真实运行导出 57 条记录，磁盘错误为零 |

SDK 旧 OperationResult、旧接口及记录构造签名保持不变。SDK 1.2 的新接口是增量添加；新示例要求最低 SDK 1.2，旧示例夹具仍保持 1.1，并实际执行配置、事件和自动资源清理，不是重新编译的替代样本。

## Evidence → Finding → Path

| Evidence | Finding | Path |
|---|---|---|
| [构建检查](validation/v03/build.log) | Core 24、PNG 10、Hosting 16、Game 29、Runtime 43、SDK 13、Scope 19、Diagnostics 16，共 170 项通过 | Build.ps1 已纳入四个新测试工程和实际六层包 |
| [原调用异常失败会话](validation/v03/failed-original-exception-boundary/events-000001.jsonl) | 原调用外层 catch 捕获 SEH 并返回零改变了游戏语义，首次集成在宿主初始化前失败 | 恢复原始调用在框架 catch 外执行；保留框架 tick 的异常隔离 |
| [修正后构建](validation/v03/build-runtime-fix.log)、[运行时回归](validation/v03/runtime-after-bridge-fix-tests.log) | 修正包构建零警告/错误，43 项 Runtime 检查通过 | 用该修正包完成最终实际启动 |
| [实际运行摘要](validation/v03/runtime-summary.json)、[日志](validation/v03/runtime.log) | PID 22004，退出码 0；五包加载；资源 2→1→0→1→2→1→0 恢复正确；子作用域创建/取消；零 Warning/Error | 最终受控会话通过，保留会话来源，不混入旧错误 |
| [安装检查](validation/v03/install-safety.json)、[导出检查](validation/v03/diagnostic-export-tests.log) | 12 项安装、9 项导出检查通过 | 不覆盖用户配置；按 sessionId 导出，不靠旧日志字节偏移 |
| [最终安装校验](validation/v03/installation.json)、[包清单](validation/v03/package-manifest.json) | 已安装 17 文件与最终包哈希一致，日常 runSelfValidation=false | 后续启动加载已验证的 0.3 包 |

## 清理与取消的关键修正

整个作用域树在任意取消回调前失效，避免回调继续用兄弟作用域改游戏。取消回调中触发 Dispose 会延迟到本轮取消完成。事件边界有独立的内部阶段，公开 int.MaxValue 优先级也不能抢在旧作用域失效之前运行。

排队任务执行前再次检查取消状态，覆盖“令牌已取消，但其取消回调被另一个回调阻塞”的情况。正在执行的同步动作不能强行打断，不能以取消状态宣称已回滚。异常日志接收器不能打断取消与逆序释放。旧状态快照异步落盘加入修订控制，不能覆盖较新的最终状态。

## 真实运行边界

当前仍为 Steam build 25977441 / Unity 2022.3.62f2 / Windows x64。原始 Rizline.exe SHA-256 为 f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e；GameAssembly.dll 为 853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79；本次运行前后不变。

最后一次受控运行只覆盖游戏启动，没有开局/结算/场景切换事件。这三个 false 是未游玩的预期记录，不能作为开局漏报已解决的证明。B01 保留未完成。场景/局作用域的离线边界测试也不能替代真实流程。

B02 尚未验证真实 IL2CPP GC 后的长期引用与全流程异常，B05 未提供多更新阶段或通用超时操作，B06 尚未拆成独立能力模块和恢复探测系统。B18a 已交付，B18 的性能与长时间压力验收仍未做；诊断后端当前同步写盘，未宣称任何耗时指标达标。

## 复现与下一步

在仓库根目录执行 `pwsh -File scripts/Build.ps1` 构建全部项目并运行 170 项检查。`pwsh -File scripts/Test-InstallSafety.ps1` 验证安装；`pwsh -File tests/RizToolbox.Diagnostics.Tests/Test-Export.ps1` 验证导出。`pwsh -File scripts/Validate-Runtime.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline'` 安装并启动受控自测，恢复日常配置，结果保存在 artifacts/validation/v03 的独立 runtime-* 目录。

接下来继续 B01 的真实流程和 B02 的剩余矩阵；独立代码工作可推进 B04 包/持久启用意愿和 B06 模块化适配。所有新游戏能力依照 B07/B08/B12 的统一接入、目标与仲裁机制开放，避免功能插件直接各自改原生对象。
