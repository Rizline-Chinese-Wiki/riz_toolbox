# LOCAL 目录独立源码复核

2026-10-21；审查者：独立子代理。结论：本次限定范围内未发现可由正常公开 API 序列触发、应阻止当前 CPU 目录合入的确定缺陷。此结论只覆盖源码复核，不等同于执行了专项、整包或游戏测试，也不等同于 B15 原生播放验收。

## 范围与证据

本次任务授权只读检查五个指定文件及必要的引用实现，只新增本报告。没有构建、运行游戏、修改生产代码或操作其他代理的 UiInput 文件。`green-final.log` 的 73/73 和上级提供的整包 778/778 为原实施者的测试证据，本审查者未重新执行。文档采用通用技术审查结构，无安全厂商风格。

| 证据 | 文件与行号 | 直接检查内容 |
|---|---|---|
| E1 | `src/RizToolbox.Adapter/AudioContent/LocalCatalogService.cs:55–110` | 登记、Own 后复核、取消和失败回滚 |
| E2 | 同文件 `112–165`、`181–218` | 撤回、引用、选择代次、准备与释放 |
| E3 | `src/RizToolbox.Adapter/AudioContent/LocalCatalogContentBridge.cs:5–10` | 资源精确 entry/payload 身份 |
| E4 | `src/RizToolbox.Adapter/AudioContent/LocalCatalogModule.cs:17–34` | 退休、保留消费者和重探测 |
| E5 | `src/RizToolbox.Sdk/LocalCatalogContracts.cs:3–43` | CPU 目录、不可变快照与选择契约 |
| E6 | `tests/RizToolbox.AudioContent.Tests/LocalCatalogRegression.cs:10–123` | 九个新增用例的断言范围 |
| E7 | `src/RizToolbox.Core/PluginScope.cs:73–107`、`ScopedServiceViews.cs:162–186` | 正式 scoped factory 缓存、代次验证与结构化错误 |
| E8 | `src/RizToolbox.Adapter/AudioContent/LocalContentService.cs:46–81`、`109–115` | 共享资源图、错误线程释放、未知清理责任 |
| E9 | `src/RizToolbox.Adapter/Modules/ModuleManager.cs:221–234` | 先撤回能力/依赖者，再释放模块 |

## 核心判断及依据

| 检查点 | 判断 | Evidence → Finding → Path |
|---|---|---|
| Own 回调撤回旧行 | 修订复核已阻止用旧 next 修订提交 | E1 的 96–104 行先记录修订、Own 后检查；E6 的 70–79 行覆盖 Conflict 和下一次修订 3。无需追加生产修复。 |
| Own 回调撤回资源、取消或结束作用域 | 发布前再验证；未提交的资源逆序释放 | E1 的 99–110 行。回调主动 Dispose 的 registration 尚未发布，不会扣减 song 引用；finally 单独释放 staging leases。 |
| 同 ID 资源重建 | 旧歌曲不会借新 payload 恢复有效 | E3 比较不可变 snapshot 对象和 payload 对象；E1/E2 的 Current、Prune、Select/Prepare 都使用该判定。E6 的 41–48 行覆盖重建。 |
| 选择失败与旧代次 | 只在 Own、取消及 Current 复核成功后更新 Generation/Current | E2 的 140–149 行；旧选择 Dispose 仅在它仍是 Current 时清空指针（218 行）。原调用者显式在回调里释放旧选择属于独立操作，不应误判为事务回滚问题。 |
| 引用图与额度 | 歌曲配额计算已发布及仍持有的退休条目；资源租约复用内容 store 的预算/依赖图 | E1 的 78–95 行去重获取，E2 的 118–125 行最终释放，E8 保留未知清理根和依赖责任。没有发现同一资源 ID 因 chart 重复引用而重复获取租约。 |
| 错误线程 | 返回结构化 WrongThread；Dispose 先验证线程再消费引用/释放委托 | E1 的 Guard、E2 的 Dispose、E8 的 115 行。E6 的 90–91、104 行已有对应断言。 |
| 模块恢复 | 旧 service 已失效，持有者未释放时等待；清理未知时拒绝重建 | E4 的 19–25 行。E9 的顺序避免新模块直接复用尚未退休的旧实例。superseded selected/prepared 消费者按 SDK 21 行保活，不能将其尚未 Dispose 就仍占额度直接认定为泄漏。 |
| 列表有界与不可变 | Charts 先读一次 Count，再按最多 16 项索引复制；登记和读取都返回只读数组包装 | E1 的 65–74、95–98 行和 Read 的 52–53 行；歌曲总量上限 128。没有无界枚举调用者 chart 列表。 |
| 正式 PluginScope 代理 | 每个 capability/contract/generation 缓存视图；失效服务调用转换为 ScopeExpired | E7；E6 的 106–123 行通过真实 PluginHost 查询和停用，未只使用测试 Context。返回的 registration/selection 自身已由实现交给 context.Own，不能误称为 Result 未展开导致完全未托管。 |

## 尚未独立执行的边界

这些是测试覆盖建议，不计为已发现缺陷，也不要求扩大本轮生产改动：

- E6 没有直接通过 LocalCatalogModule 的 Release → Probe 检查“旧 prepared 仍持有时 Waiting、最后释放后重新 Available、未知清理时 Unsupported”。现有源码支持该路径，仍应补模块级断言后再把恢复矩阵称为已执行。
- E6 没有用本目录 API 到达 32/128 项歌曲限额，再验证退休条目仍计数和最终释放后额度恢复。不能用内容 store 的限额用例替代目录自身配额断言。
- 正式 scope 用例撤回的是 catalog capability，随后停用 consumer；它没有单独撤回/替换 selection capability 并保持可选能力 consumer 活跃。服务代理失效可从 E7 判断，返回句柄是否也需独立关联 capability generation 应按宿主契约明确，并以测试固定；当前 CPU RequestPlay 始终拒绝原生播放，本审查没有将此未执行组合提升为真实写入缺陷。

## 播放边界

E1 的 98 行固定 `AvailableForGamePlayback=false`；E2 的 211–216 行对有效选择返回 `OperationUnsupported`。`Prepare` 只保活已验证的 CPU 资源图。上述实现和证据不能描述为本地歌曲已原生可播放，也不能替代音乐、封面、InsideLevelParams、重试、结果和最终释放链的实机证据。

## 审查版本

SHA-256（本次读取时）：

| 文件 | SHA-256 |
|---|---|
| LocalCatalogService.cs | `7178460C56559E5B4C652CFAE49F3D13E415973F74B3AD1506038527C6B9E00E` |
| LocalCatalogContentBridge.cs | `64EC3DD1329E261B9F8195D9B0D4099AF02382BAFF8E96FD1DA0EA60FBBC4DD3` |
| LocalCatalogModule.cs | `830BFE7969B908EBA562918BAF967F957B86FFD2C3667AC6615718338D2A983B` |
| LocalCatalogContracts.cs | `08F6F2F5A0C1249A0EC8EE4D6FD370ED6ED5C8382C5DFF775E1314942C94D19A` |
| LocalCatalogRegression.cs | `4E69FDD6C980E720F91D78D61DA23C2326F79B22F67F872642E005E9392FE3C6` |
