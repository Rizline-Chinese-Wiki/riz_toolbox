# LOCAL目录与选择事务

2026-10-21。独立CPU歌曲目录、选择代次及实际游戏内消费者已实现，尚未接通本地歌曲原生播放/预览。完整B15未验收。

## 实现范围

新增可选SDK契约`ILocalCatalogProviderService`（`riz.content.local.catalog`）和`ILocalCatalogSelectionService`（`riz.content.local.selection`），不向旧接口增加必需成员。歌曲具有插件命名空间的稳定ID、标题/作者、独立修订、已验证audio/cover及1–16个chart引用。128项总量、每提供者32项上限同时计算撤回但仍被消费者持有的条目；资源仍使用原内容store与额度。

登记复制有界元数据、获取精确内容entry的CPU消费租约，Own之后复核scope、取消、资源对象/修订与目录修订，再一次发布。资源同ID重建不能偷换旧行的payload；旧行被移除，新请求拒绝。选择成功才递增代次，失败/取消保留旧选择；旧选择释放不撤销新选择。内部`LocalPlayRequest`保存完整原修订资源图，提供者撤回后仍保活至最后消费者释放；错误线程释放先拒绝再消费委托。

独立module依赖`riz.content.local`，通过正式scoped capability注册，旧模块退休后等待已有host消费者或对未知清理要求重启。SDK快照`AvailableForGamePlayback=false`；RequestPlay明确OperationUnsupported，不伪造Workshop UInt64、不替换原版歌曲。

## 已执行回归

- `red.log`：64/69，原64项全部通过，新增5项真实行为在scaffold上失败。
- `green-initial.log`：69/69。
- `red-revision.log`：一次测试调用写错API的编译失败，原样保留，不作为行为red。
- `red-revision-behavior.log`：72/73，发现Own回调撤回旧行时目录修订可重复；新测试的Error解引用失败对应错误成功返回，其他新增行为通过。
- `green-final.log`：73/73，修复为登记期间目录修订改变则Conflict，重试按新修订发布。覆盖精确旧payload保活、迟到选择/准备拒绝、错误线程、真实PluginHost/PluginScope服务查询与旧视图失效。
- `examples-build.log`：消费者与TMP探针编译成功、0警告/错误。后续完整构建与新实机必须看独立整合证据，不能把此日志扩展成原生播放成功。

## 游戏内消费者

`LocalCatalogRuntimeProbe`在真实MainMenu挂载目录窗口，6行分页、歌曲/难度选择、收藏过滤、稳定LocalId收藏/上次选择存储，以及Play结果显示。提供者撤回或修订变化会释放旧选择。默认不开启，配置`local-catalog-proof`显式Enabled。

可额外显式设置`PublishValidationFixture=true`，生成并通过真实服务验证原创短WAV、RGBA PNG及一个fileVersion0单音符谱面，发布两个同标题不同ID的测试条目，供界面/选择/收藏/拒绝流程实测。它们标为validation fixture，始终不宣称可原生游玩；原版曲库不变，已有不同字节资产会被保留并拒绝覆盖。

复验：在仓库根执行`dotnet run --project tests/RizToolbox.AudioContent.Tests/RizToolbox.AudioContent.Tests.csproj -c Release`。真实播放仍需独立音乐/封面消费、ResourceReference关闭泛型所有权、InsideLevelParams、重试/结果及释放链，见原LOCAL边界研究和B15 SPEC。
# 补充边界回归（2026-10-21）

独立源码审查见 [independent-review.md](independent-review.md)。随后补5项实际行为回归：32/128歌曲配额包含撤回但仍持有条目；最后持有者释放后恢复额度；真实 LocalCatalogModule Release→Waiting→最后消费者释放→新服务恢复；未知 chart 清理永久 Unsupported 且不重试；正式 PluginHost 中仅撤回可选 selection capability 时旧代理失效而 consumer scope 保持活跃，新代次可重取。

首次 `boundaries-compile-failed.log` 是测试错误调用 Core internal GetService 重载，不能作为行为失败。改为真正创建 PluginHost/PluginScope 访问已注册工厂后，`green-boundaries.log` **78/78**。测试没有增加返回句柄必须随可选能力代次撤销的新契约，也没有放开原生播放。

