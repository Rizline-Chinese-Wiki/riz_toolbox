# N4：持续视觉覆盖意图验证

2026-10-29 从隔离复制的托管项目依赖实际构建并运行 Visual 可执行测试：**97/97**，其中原 Visual 76 项、N4 新功能 21 项；构建 **0 警告、0 错误**。本包实现宿主续接视觉目标代次，没有运行游戏、安装、GUI、提交或推送。真实 Unity 可见效果、同候选真实重建、逐类双包与 GPU/完整原生释放仍待游戏验收。

## 作者获得的行为

`IVisualResourceService.BindOverride(TargetQuery, IVisualAssetLease, int)` 返回由当前插件 scope 持有的 `IVisualBindingIntent`。首次只解析已有注册表；后续在原有有界发现边界重新解析，唯一匹配才走原 `MutationCoordinator` 取得新代次层。旧代次失效先结束旧层；零候选 Waiting、多候选 Ambiguous，资源可以继续由作者 scope 持有。

意图的首次唯一候选锁定 Scene、ContentId、Role、ResourceId 的实际值，包含 null。绑定以后的同名异场景/内容/角色/资源不会成为替代目标。模板和不活跃对象被排除，空泛 query 被拒绝。旧规范键/代次绝不复活。

输入必须是同一 provider、同一实际 scope 的原始 `AssetLease`。父/子/兄弟 scope 不能因为 PluginId 相同而互用，包装租约和外部 provider 的租约不执行 getter 就被拒绝。一个资产租约可以支撑多个意图；结束该租约会停止全部关联意图。`Stop` 仅结束意图和当前层，资产仍归作者。

`LastSnapshot` 为可跨线程、可在 scope/provider 结束后读取的纯托管不可变快照，包含状态、修订、当前目标代次和结构化错误。结果入口先拒绝 worker/失效 scope/provider，不探测后端。当前 Bound 只表示取得 priority 层；实际胜出仍由公共仲裁器决定。

## 实际执行证据

证据目录：`D:/riz-build-cache-next/visual-binding-n4-r1-20261029`。

| 原件 | 实际结论 |
|---|---|
| `test-result.json` | `passed=true`；build/test exit 均 0；`97/97 visual checks passed.` |
| `Visual-build.log` | 从隔离 `source/` 构建 Visual 与其 SDK/Core/Runtime/Adapter 依赖，0 警告、0 错误 |
| `Visual-tests.log` | 97 条 PASS、0 条 FAIL，其中 21 条 N4；13 个 ACCESS 批次的 9 类副作用增量全部 0 |
| `source-before.json` | 376 个冻结输入，4,175,534 bytes；执行前后冻结源与工作树均无 hash 漂移 |
| `owned-source.json` | 17 个本包负责的测试/实现/合同/作者文档输入，含未改文件；本次改变 8 个路径 |

测试调用链是实际 `VisualResourceService` → `SemanticTargetRegistry` / `MutationCoordinator` → 可控 `IVisualBackend`。child scope 与 capability/provider 撤回用正式 `PluginHost`；后端 seam 不执行 Unity。

| 功能路径 | 实际断言 |
|---|---|
| 缺失→唯一→歧义→重建及地址身份复用 | 重建使用新 generation；旧 handle 拒绝；无重复资源创建 |
| 查询/普通 Tick 与发现节奏 | GetSnapshot 不发现、不重绑；非发现 Tick 不增加 backend discovery；后续既有发现才接新对象 |
| scene/content/role/resource 和 null 锁定 | 同名其他语义与模板/不活跃实例均不写；正确语义新对象能绑定 |
| 初次歧义 | 无优先级层，唯一以后才取得层 |
| 双插件、一次性 alias 层和外部 baseline | 两插件重建后共用新一代通道；优先级照常；最后恢复最新游戏基线 |
| Sprite / Font / Material | 三类资源实际创建/加载、绑定、重建、释放当前层均通过 |
| scope 与 lease 身份 | 正式 child scope 独立终止；父/兄弟/外 provider/包装 lease 明确拒绝 |
| worker/无效请求 | backend、创建数、文件 root、目标读写、AliveReads、预算及预约增量全 0 |
| Own / Resolve / Override 内撤回 | 初次意图永久停止；未发布层和资源不泄漏 |
| 新代次 setter 失败 | 新目标原基线保留；状态携带 MutationWriteFailed；后续发现可成功 |
| 新 setter 内撤回 scope / asset / provider | 意图永久停止；在途 provider 使用原 NativeDepth retirement，Tick 后确认恢复与 once-only 释放 |
| 旧层恢复不确定 | 不申请新对象层；终态 CapabilityUnavailable；保留原生赋值、根和预算 |
| waiting/bound 停止、资产失效与一租约多意图 | stopped 不复活；Stop 保留作者资产；输入 Dispose 停止所有关联意图；释放一次 |
| cleanup pending / quarantine | 终止意图；不把逻辑停止冒充已物理释放 |
| 成功探测后的空 provider | 允许 Waiting；发现异常仍抛出给模块失效路径，不报告 Ready |
| 无 instance Key 的语义查询 | ContentId/Role/ResourceId 查询能绑定并续接新实例 |
| 有界意图数 | 每插件 32、每 provider 128；跨插件总限额、停止返还槽位，不增加额外资产预算 |

## 变更路径及复现

合同与作者说明见 [VisualBindingContracts](../../../src/RizToolbox.Sdk/VisualBindingContracts.cs)、[VisualContracts](../../../src/RizToolbox.Sdk/VisualContracts.cs) 和 [Visual 资源](../../Visual-resources.md)。状态机在 [VisualResourceService.Intents](../../../src/RizToolbox.Adapter/Visual/VisualResourceService.Intents.cs)，主服务/模块接线仍复用原发现、公共仲裁和预算。主要功能用例在 [VisualBindingChecks](../../../tests/RizToolbox.Visual.Tests/VisualBindingChecks.cs)，原 fixture 在 [Program](../../../tests/RizToolbox.Visual.Tests/Program.cs)。

本包没有改 Core、Runtime、Gameplay、native、发行或打包。root 同步补充的 `AdapterCapabilityMetadata.cs` 作为只读输入进入本隔离包，声明操作不会自动使 provider 可用。

在已有证据目录重读实际运行日志：

```powershell
$proof = 'D:/riz-build-cache-next/visual-binding-n4-r1-20261029'
Get-Content -LiteralPath (Join-Path $proof 'test-result.json')
Get-Content -LiteralPath (Join-Path $proof 'Visual-tests.log') -Tail 25
```

如需重跑冻结代码，选择一个不存在的新输出目录；它不会构建或启动游戏：

```powershell
$source = 'D:/riz-build-cache-next/visual-binding-n4-r1-20261029/source'
$output = 'D:/riz-build-cache-next/visual-binding-n4-rerun-20261029'
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh output directory.' }
& 'C:/Program Files/dotnet/dotnet.exe' build (Join-Path $source 'tests/RizToolbox.Visual.Tests/RizToolbox.Visual.Tests.csproj') -c Release --artifacts-path $output -m:1 -p:UseSharedCompilation=false --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Visual build failed.' }
& 'C:/Program Files/dotnet/dotnet.exe' (Join-Path $output 'bin/RizToolbox.Visual.Tests/release/RizToolbox.Visual.Tests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Visual checks failed.' }
```

本结果不代表全部 B01–B19 通过，不提供扫描性能或帧预算结论。正常发现仍每五次模块 Tick 一次；真实游戏、实际像素/原生消费者与性能校准均保持后置。
