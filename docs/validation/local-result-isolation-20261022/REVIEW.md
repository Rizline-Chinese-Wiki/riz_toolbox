# LOCAL 结果隔离独立审查与执行清单

2026-10-22，UTC+8。flavor=null。范围：已有 LOCAL 启动静态报告、ExactHookBroker、WorkshopChartModule、结果来源/采样及 SDK DTO 的源码审查。审查后按主代理追加分派实现了独立 `LocalRunSessionRegistry` 与专属回归文件；未改broker/Workshop/LocalCatalog/Program，未构建、安装或操作游戏。**状态层尚未接线，回归尚待主代理运行，runtimeAcceptance=false；不能据此开放 RequestPlay 或勾选 B15。**

结论：先落地宿主内部的 owned session 与一次性结果捕获状态机，再把它连接到正在实施的 InstanceVoid 前置门。LOCAL 结果路径必须跳过整个 `LevelOverControl.Start`，保持保护责任直到旧消费者、全局回调及场景清理均已确认。普通游戏实例继续原样调用 original 一次。单靠结果来源 observer 或 `ProcessLevelMetadata` hook 不能完成隔离。

## Evidence

授权范围继承 [LOCAL launch scope](../local-launch-20261022/scope.md)。源文件在其他代理继续修改，下面哈希仅固定本次读取基线；后续实现必须重新审查差异，不能把源码基线当已安装包证据。

| E-id | 原始来源与可复查位置 | 本次观察及 SHA-256 |
|---|---|---|
| E-launch | [启动边界 README](../local-launch-20261022/README.md)、[boundaries.json](../local-launch-20261022/boundaries.json) | Awake 发布六回调；ControllerOver 启动协程；stock Retry/Back 不适合 LOCAL。boundaries `91f9a80804aafa25c963195e92b5e69edb2f629f4c695f137b53a89dec9ae812` |
| E-save | [前序报告结算章节](../local-metadata-bridge-20261022/2026-10-22_逆向-Rizline本地metadata桥-report.md)、[原始边界](../local-metadata-bridge-20261022/boundaries.json) | Start 内联保存/成就；boundaries `467f28ffb4ebcab9a5946f6aa33229138dd40a97278d18c923c69741b8f8b5c5` |
| E-broker | [ExactHookBroker.cs](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.cs)，InvokeVoid/Before/Finish | 阅读时仍为 observer 模式，reentry/错线程/过滤失败不阻止 original；`b32c27267fa5cd8eda684c3803f12ecb9e4e7972cea0dd01ece00a431d236568`。并行代理正在增加前置门，不能拿该哈希否定后续实现 |
| E-workshop | [WorkshopChartModule.cs](../../../src/RizToolbox.Adapter/AudioContent/WorkshopChartModule.cs)，Probe/Release | 现有绑定是原方法返回后的 Workshop Chart 替换；Release 解绑且释放服务，不能直接充当长期 LOCAL 保护 owner；`8424943cbfb3d80cc3d62e764312b08b5664bdc558940cca76d3cc5d918cea79` |
| E-origin | [ExactResultOriginModule.cs](../../../src/RizToolbox.Adapter/Modules/ExactResultOriginModule.cs)，Capture/After；[ResultOriginRegistry.cs](../../../src/RizToolbox.Adapter/ResultOriginRegistry.cs) | 只保留 actual original-return 来源，不是结果调用锁或结果发布器；module `9e7bfd85d843a6470ec2f1af0ad7f3ee203a7f9ad372642aaef4b8ba161e5e22` |
| E-sample | [GameEventObserver.cs](../../../src/RizToolbox.Adapter/GameEventObserver.cs)，结果采样块/ChartId | 从当前 LevelOverControl.levelOverInfo 读取结果并发布；`70ca3dad5cddf6c30524f4f894fe3d0f696ee0052d26dbd6dd609e5038d1a341` |
| E-state | [GameEventState.cs](../../../src/RizToolbox.Adapter/GameEventState.cs)，CaptureResultOrigin/Finish | 只在实际 observed run、原始来源和controller/information一致时关联；`874828bc6f7d0d07ea2e99b3e63e74ac2c35c57dbbba01b0552f2aee4f72549c` |
| E-dto | [GameContracts.cs](../../../src/RizToolbox.Sdk/GameContracts.cs)，RunFinished；[LocalCatalogContracts.cs](../../../src/RizToolbox.Sdk/LocalCatalogContracts.cs) | RunFinished 为不可变实际结果拷贝；LOCAL选择快照已带song revision与generation，RequestPlay当前未开放；GameContracts `a5e24fbd03d076cb0b7e956e37f85c877c4dbc24125073c778828ccfe77b8e5c` |

可复查静态证据，无游戏执行：

```powershell
& C:\Python314\python.exe docs/validation/local-launch-20261022/verify-static-claims.py
& C:\Python314\python.exe docs/validation/local-metadata-bridge-20261022/verify-static-claims.py
```

本次只读取上述校验脚本及既存报告，没有重跑二进制提取或声称新校验通过。

## Finding

| F-id / 状态 | evidence_ids | 确定结论与实施含义 |
|---|---|---|
| F-full-start / confirmed-static，高置信 | E-save,E-launch | `LevelOver.Start@3D0B20` 在 `3D0ECE/3D0EE9/3D0FAB/3D0FBC/3D0FC6` 执行 ToBestScore、BestScore.Update、DomainWrapper.set_Item、CheckAchievement、CheckOffset。只拦 ProcessLevelMetadata 或在 After 处理已经太晚 |
| F-side-effect-once / confirmed-static，高置信 | E-launch | `NormalMode.ControllerOver@3BEA60` 会在 `3BEAA5` 启动协程，不能当纯 getter 重试。必须在进入原方法前写入 InFlight；异常、null、root失败及解码失败均不得自动再调用 |
| F-sampling-gap / confirmed-source，高置信 | E-origin,E-sample,E-state | 跳过 Start 会使 levelOverInfo 保持未初始化；现有 sampler不会自然发布 LOCAL 结果，origin observer也不会补发。必须增加受控实际结果来源与一次发布状态，不能假称既有订阅已覆盖 LOCAL |
| F-owner-lifetime / confirmed-source + design，高置信 | E-workshop,E-launch | provider/业务模块解绑不能移除已提交 LOCAL 会话的保护门及身份。当前 Workshop 模块的 Release 是撤销服务的模式，不能直接继承为 committed LOCAL 的责任模型 |
| F-global-commit / confirmed-static，高置信 | E-launch | clone/Awake 发布持久对象和六个全局委托。pending/session保护必须在clone前建立；失败回滚需要比较当前全局字段与本会话安装值，不能无条件把整个旧快照写回 |
| F-dto-boundary / confirmed-source，高置信 | E-dto,E-state | LOCAL ID/revision/chart/run generation不能靠原版 From或同名title推断。现有RunFinished可承载已复制计数，但保留LOCAL来源需要宿主内部附加身份记录；无可靠observed start时RunId应保持null |

未发现源码能证明“已接入的LOCAL实际结果会污染存档”的现存运行故障：目前RequestPlay没有开放。上表是开放前已确定的缺口与约束；没有使用假设替代动态验收。

## Path：先落地最小会话与结果锁

P-owned-result，path_type=implementation-proposal，依据 F-full-start/F-side-effect-once/F-global-commit：准备完整资源 → 安装全部前置门 → 注册 PendingCommit owner → clone/绑定exact controller与params/metadata → Playing → LOCAL结果Start被前置门接管 → ClaimResult一次 → 原ControllerOver实际返回/root/校验/复制 → ResultReady或ResultFaulted → 自有结果展示 → 宿主重试/返回 → 旧消费者退出与条件恢复全局状态 → Retired后才释放最后roots。

最小下一项建议新增宿主内部 `LocalRunBoundaryState` 加 `OwnedLocalRunSession`，放在 Adapter/AudioContent，先用 fake backend 跑纯状态回归。**此阶段不改公开SDK，不把RequestPlay改成成功。**

| 接口/职责 | 最小输入与输出 | 不变量 |
|---|---|---|
| `LocalRunBoundaryState` | immutable SessionId、LocalContentId、song revision、chartId、run generation；阶段及capture状态 | 单线程纯状态；无原生调用或GC roots；错误generation不可推进当前会话 |
| `OwnedLocalRunSession` | 完整已准备bundle、已root的manager/controller/params/metadata/result、进入时返回快照、六委托安装前/后快照 | 宿主进程责任槽持有；provider撤回后仍存活；尚无consumer-end证明的未知状态保留全部根与保护；consumer-end已证明后未知free只留责任台账 |
| `ClassifyBoundary` | target kind + retained owner identity或pending transition reservation → `ContinueOriginal` / `SuppressOwned` | 在普通observer之前且可不访问native metadata；已归属入口的异常、嵌套、业务撤回不变成Continue |
| `TryClaimResult` | exact controller identity + session/run generation → `InvokeOnce` / `ReuseReady` / `CaptureInFlight` / `TerminalFailure` / `NotOwned` | 由NotRequested先改InFlight再给InvokeOnce；其余四结果都不重复调用ControllerOver |
| `CompleteResult` | 该次claim receipt + rooted actual InsideLevelResult + immutable copied counters | 校验result.metadata等于本run metadata；只接受当前InFlight receipt；发布最多一次 |
| `FailResult` | receipt + side-effects-unknown或复制失败原因 | 终态保留suppress和错误面板/返回能力；失败后再次Start只复用终态，不重取原结果 |
| `BeginRetire` / `ConfirmRetired` | 返回或重试计划；controller/场景消费者退出、全局ownership处理完毕的逐项确认 | 先Retiring，最后Retired；确认缺一项或任一cleanup unknown都不能提前释放旧bundle/门 |

建议阶段是 `Prepared → PendingCommit → Playing → ResultCapturing → ResultReady/ResultFaulted → Retiring → Retired`，外加 `CleanupUnknown`。重试创建下一代独立会话/Chart；旧代留在Retiring直到最后消费者退出。不要把“准备好重试”当“旧代已安全释放”。

内部结果记录至少保存：SessionId、LocalId、songRevision、chartId、runGeneration、实际result身份、一次性capture receipt、RunFinished不可变副本、关联observed run是否成功及原因。`RunFinished`数值必须来自该rooted result的get_Score/get_RawCompleteRate和独立perfect/maxPerfect/bad/miss/crashed/skip字段。不要把Failed/Skipped/Aborted合并；没有actual result的取消不制造假成绩。

## 条件 skip 的边界

1. 安装前验证固定build、精确实例Void ABI、类型/方法/RVA/MethodInfo；同地址只允许一个宿主协调门，普通插件只有observer权限。
2. 分类先于普通Before、保留上下文及业务handler。主线程业务执行与纯identity分类分开；worker不能解码原生对象。确定普通入口保持原始receiver/MethodInfo并调用original一次。
3. 对已经归属LOCAL的入口先锁定skip，再执行业务；handler异常、logger异常、plugin/provider停用或当次解绑都不能改回original。After不应暗示original已执行；应有明确Skipped/OriginalCompleted/OriginalFailed诊断。
4. native嵌套/业务分类重入必须分别测试。旧observer策略“reentry返回null后继续original”不能让owned入口逃逸。分类异常和非法枚举必须fail-closed；同时测试明确普通实例不被无关LOCAL owner误吞。
5. 新结果页Start时levelOverInfo还为空，不能拿它或From字符串作为owned判断。需要进入结果场景前建立明确的transition reservation，并在接管第一页时绑定exact result-screen root；冲突/多个候选不能随意选一个。
6. 尚无consumer-end证明的tombstone/retiring owner持有roots以避免旧地址复用。只有强token证明旧入口不可再达，才应在释放任何root前撤销该run的地址分类，防止已free地址复用误拦普通对象。保护门的进程寿命与业务handler注册寿命要分开。

## 全局委托与清理责任

固定样本的 `InLevelControl` 六委托是 LevelStart、GetJudge、LevelOver、SetUiColor、ControllerOver、GetLevelResult，另有controllerSet。用exported字段metadata逐项核实，不裸偏移恢复。保留manager本身、六个安装前delegate（非null时）及本run安装后的delegate roots。clone/Awake部分失败时也要记录已经改变的字段。

恢复按ownership比较进行：只有当前manager与被接管manager相同、当前字段仍等于本会话安装的值，才有写回旧值的权限。更晚controller/provider已换走的值绝不可覆盖。若部分字段被后继占有，记录ownership已转移/冲突，并通过明确策略处理仍归本会话的字段；不能凭controllerSet一个bool断言整个图已恢复。`controllerSet`仅在完整ownership条件允许时恢复，并读回核验。

旧快照也可能指向已销毁的Unity controller：GC root只保持托管包装器，不保证Unity对象活性。因此不能无条件把旧delegate恢复成可调用基线。活性/接班关系无法证明时进入有责任的恢复错误状态，保留必要roots和隔离，不用“写null全部字段”掩盖结果。

跳过Start后，不默认把cover/music交给LevelOver.OnDestroy。自有session持有结果/参数/资源的最后消费者责任；原生Release尝试一次、Unity延迟Destroy确认、root free的未知结果分别记录。不要只复制ResultOriginRegistry的计数式ReleaseFailures作为会话清理台账。

## 可执行回归清单

以下用fake native detour +纯state/session backend验证，计数器包括original、ControllerOver、result decode、publish、scene transition、delegate write、Release/Destroy/root free。表中“0/1次”都是可断言的spy计数，不能靠日志中没有异常判成功。建议门用例纳入Hooks.Tests，会话用例纳入AudioContent.Tests。

| ID | 输入/故障注入 | 必须断言 |
|---|---|---|
| G01 | 无LOCAL policy、普通Start | original原参数1次，旧Before/After顺序不变 |
| G02 | 已注册LOCAL owner，但receiver为另一普通实例 | classifier明确Continue，普通original1次、LOCAL handler0次 |
| G03 | owned Start且handler成功/失败 | 两种情况original均0；handler至多1；后者状态为ResultFaulted |
| G04 | owned classifier抛错、非法枚举、logger抛错 | original0；有界错误记录，不以catch恢复stock |
| G05 | owned handler/分类重入相同Start | 内外original均0；ControllerOver至多1；门能退出重入状态 |
| G06 | 普通original自身引发普通嵌套Start | 每个native entry的original各1次，旧观察者重入规则不扩大 |
| G07 | worker收到明确普通/owned入口 | 普通original1次；owned original0、handler0，均无native metadata分类读取 |
| G08 | owned分类后handler解绑/provider停用 | 当次及尚无consumer-end证明的后续保护保持skip；业务撤回不清owned身份 |
| G09 | 第二个协调门/冲突ABI/固定build失败 | 安装失败且RequestPlay不可提交；不污染既有observer |
| G10 | 新结果receiver，levelOverInfo为空 | 有准确reservation时绑定并skip；无关普通receiver仍Continue；多个候选拒绝选择 |
| R01 | 首次owned Start，ControllerOver成功 | 先InFlight后native调用；返回立刻root；确切1次并生成一个immutable副本 |
| R02 | 同页Start重复、不同LOCAL页重复、结果按钮重复 | 同session/run复用同actual result；ControllerOver总数仍1 |
| R03 | ControllerOver期间嵌套请求结果 | 只得到CaptureInFlight，无第二次native调用 |
| R04 | ControllerOver原生/托管异常或null | TerminalFailure；重复请求不重试，stock Start0次，未知副作用保留 |
| R05 | 返回后的root创建、类型校验、metadata匹配、字段复制任一步失败 | ControllerOver仍最多1；owned责任保留；不发布假DTO |
| R06 | 复制后改动native计数或释放result | 已发布DTO内容不变；访问结果前有root；各计数独立不互相替代 |
| R07 | observer未装、withdrawn或错过observed start | LOCAL实际副本仍可存在；SDK run关联为空/有原因，不伪造RunStarted |
| R08 | 本地result已发布后sampler又看到同root | 同结果只发布一次；旧普通结果不被本地session错误吞并 |
| R09 | song同名跨provider、同ID不同revision/新run generation | 必须区分来源；旧claim不能提交新run结果 |
| D01 | clone前snapshot→Awake完整安装→正常退出 | 精确六字段及controllerSet按比较恢复；逐项读回；root free准确1次 |
| D02 | Awake只发布前N个delegate就失败（N=0..6） | 已改变字段有责任台账；未改变字段0写入；不会释放仍被global引用的controller图 |
| D03 | 新controller替换全部或部分delegate、manager本身替换 | 不覆盖后继字段/manager；不据bool认领后继；保留/转移状态可查 |
| D04 | 旧delegate目标Unity destroyed但wrapper有root | 不恢复成可调用基线；有界恢复错误且owner保持 |
| D05 | 返回/重试命令重复与交叉 | transition只提交一次；只有准备成功才改代；取消准备保留原暂停/结果状态 |
| D06 | provider撤回或plugin关闭发生在Pending/Playing/Result/Retiring | Prepared可取消；committed走宿主正常回退；保护与资源不随provider提前消失 |
| D07 | result界面毁、controller毁、scene卸载以所有次序发生 | 最后消费者退出前Release0次；完成后每resource/root最多1次 |
| D08 | delegate恢复、Release、Destroy或free任一步抛错 | 无consumer-end证明时保留全部根与skip。强token成立后先unpublish再逐项free，失败仅保留unknown责任/quota，绝不用已free根对应裸地址继续skip；unknown free不重试 |
| S01 | LOCAL正常完成/Failed/Skipped/取消 | stock Start/BestScore.Update/DomainWrapper.set_Item/CheckAchievement路径计数0；取消无result不造DTO |
| S02 | 普通曲目正常/失败/重试/返回，LOCAL准备过或刚退出 | 原版Start/Retry/Back各entry原样一次；observer和ordinary result数据不变 |
| S03 | LOCAL→普通→LOCAL交错与GC | 代际隔离、普通行为不被残留skip，已有本地墓碑防地址复用 |

实现后定向运行（本次未运行；其他代理构建期间不要并发执行）：

```powershell
dotnet run --project tests/RizToolbox.Hooks.Tests/RizToolbox.Hooks.Tests.csproj -c Release
dotnet run --project tests/RizToolbox.AudioContent.Tests/RizToolbox.AudioContent.Tests.csproj -c Release
```

## 实机放行门

离线用例通过后仍要检查新安装包hash、实际调用计数与完整生命周期，至少涵盖正常完成、Failed、Skip、暂停重试、结果重试、返回、provider撤回、进入普通关卡。每次收集session/run generation、前置门判定、original是否执行、ControllerOver真实调用次数、实际结果及metadata identity、全局字段前/后快照和资源最终状态。

LOCAL分支需证明原版保存/Steam调用为零：已知固定入口包括 `BestScore.Update@36D170`、`DomainWrapper.set_Item@3899C0`、`SaveData.Set@3ADA50`、`SaveSystem.SaveService@38E0B0`、`LevelOver.CheckAchievement@3CE440` 及其Achievement.Trigger/AddStat/SetStat调用链。计数必须能限定来源run；进程中的无关后台保存不能误计成本地结果。存档前后hash相同只能辅助，不能单独证明未调用保存或Steam。

普通关卡验收应证明original原样执行及结果可读、原有保存/Steam路径仍可达，而不是要求测试真的触发一个新Steam成就。不调用真实Steam API来伪造验证事件；用真实正常游戏行为及已验证观察入口取证。保护门不得全局停用保存/Steam功能。

本次decision_delta：最小后续实现收敛为宿主owned-session与一次性capture状态；新增明确采样缺口及全局ownership恢复矩阵。没有替代完整LOCAL播放、长期资源/性能矩阵或B01–B19范围。

## 追加实现：未接线事务层

- [LocalRunSessionRegistry.cs](../../../src/RizToolbox.Adapter/AudioContent/LocalRunSessionRegistry.cs) 已实现：主线程注册及变迁；PendingCommit先行claim；精确controller/result-scene角色绑定；任意线程纯不可变identity快照；ControllerOver一次capture claim、cache及失败锁；provider撤回与宿主owner独立；最多32个process-owned会话；跨registry alias与ownership检查和adopt处于同一ProcessGate临界区。
- [LocalRunSessionRegistryRegression.cs](../../../tests/RizToolbox.AudioContent.Tests/LocalRunSessionRegistryRegression.cs) 提供16组`Cases`供主代理注册，包括并发两个主线程registry竞争同一metadata identity、worker分类不读RootHandle.Target、验证/清理重入、跨run token和unknown free责任。该代理没有运行测试。
- 尚未实现native clone、ControllerOver调用、结果发布/面板、场景验证器、全局委托恢复或RequestPlay接线。传入的native identity与root必须由后续native adapter先核exact类型、Unity活性、metadata及归属。

审查修正了一个确定的原始设计缺陷：若逆序释放controller/result-scene roots后，因bundle free失败仍按旧数值地址分类，会在地址复用后误吞普通实例。最终状态层采用强consumer-end token策略：外部verifier必须确认controller/result-screen的Unity对象已destroy、旧callback与全部消费者结束、globals解除且旧入口不可再达；`Close`在任何Dispose前设置`ProtectionRetired=true`并unpublish，再尝试独立free。失败保留failed ownership及process quota，但不保留已失效地址分类。

这与ControllerOver失败的`CleanupUnknown`不同：后者`ProtectionRetired=false`，尚未释放roots且仍skip。快照中的`ConsumerEndEvidence`记录前者撤分类的外部证明。对native verifier尚无实机实现或验收，不能用测试fake verifier作为实际consumer-end证据。
