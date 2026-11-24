# riz_toolbox 运行架构收敛审查

当前主要问题是执行边界和成本归属：主线程入口每 accepted tick 验证完整发布图，模块缺少消费者需求调度，播放操作又将嵌套本地复核放大成完整 provider/source 验证。现有 SDK、包隔离、作用域和仲裁有复用价值，不需要重造。建议先拆运行模式与入口证明，再补需求/场景计划、共享观察和唯一播放事务。本批完成设计与源码审查，**没有实现新运行行为、构建、安装或操作游戏**；真实 Speed/Seek、完整 B01–B19 与生产准入仍未完成。普通工程报告，`flavor=null`。

范围与时间见 [scope](../work/runtime-redesign-20261117/scope.md)、[timeline](../work/runtime-redesign-20261117/timeline.md)。新合同入口是 [.docs/runtime-redesign/README.md](../.docs/runtime-redesign/README.md)。用户委托自主设计后选 T2，不增加前端、数据模型或规划扩展。

## Evidence

| ID | 原件、位置与复核 | 观察边界 |
| --- | --- | --- |
| E01 | [source-audit-r2.json](../work/runtime-redesign-20261117/source-audit-r2.json)，SHA256 `9ed806543481e0ae7e177314d7cb6709604a94a36e0d6541632b5315c9006224` | 46个阅读源码的哈希与定位；1513个产品输入逐一同readback-provider-r3，主README保护保持。源码结构证据，不是新运行测试。初次记录四个搜索锚点未匹配，r2仅修正定位文字，产品未变。 |
| E02 | [game-result.json](../work/game-seek-20261117/descriptor-observation-r3/game-r1/game-result.json)，SHA256 `2b41c2da87863249f2084a6a1fa11a0f9025fdd950b80b7842825df9bd84c048` | 旧包source15.019877s、Pause→Ready284.264149s、单0.75x180s无匹配回执，Unknown未重发，已关闭。不能验当前r3或新设计。 |
| E03 | [speed-r2-stack.log](../work/game-seek-20261117/descriptor-observation-r3/game-r1/speed-r2-stack.log)，SHA256 `c1dcba104423547a142ae941d763c31432a6935d7610452e7957a72b632c1a06` | 旧包实际主线程停在BoundSource→Probe→formal rate/Clock/source→full physical proof；采样栈不等于调用计数或精确CPU占比。 |
| E04 | [NativeRuntimeInvokeBridge.cs](../src/RizToolbox.Runtime/Hooks/NativeRuntimeInvokeBridge.cs) `AfterAdmission.Observe`，以及 [NativeFirstInstall.cs](../src/RizToolbox.Runtime/Hooks/NativeFirstInstall.cs) `VerifyReady` | accepted tick 在Host之前调用完整listener/operation/single-event/tween检查；具体哈希/行定位在E01。 |
| E05 | [ToolboxHost.cs](../src/RizToolbox.Loader/ToolboxHost.cs) `TickCore/WriteStatus`、[RizlineAdapter.cs](../src/RizToolbox.Adapter/RizlineAdapter.cs) `Tick`、[ModuleManager.cs](../src/RizToolbox.Adapter/Modules/ModuleManager.cs) `Tick` | 两次drain之间有命令；Pump32为项数；四probe预算不限制全部active维护；status排序/对象构造/序列化在host线程。 |
| E06 | [NativePlaybackRateProvider.cs](../src/RizToolbox.Adapter/Gameplay/NativePlaybackRateProvider.cs) `BoundSource.VerifyAuthority`、[GameplayPreparedValueControl.cs](../src/RizToolbox.Adapter/Gameplay/GameplayPreparedValueControl.cs) `Prepare/Commit/Confirm` | 两participant成功路径，仅协调层verifyOrigin静态调用4+9+4=17次；每次当前会再Probe和source检查。不是实机测得17次总调用，尚未含participant内部。 |
| E07 | [SourceVerification](../src/RizToolbox.Runtime/Hooks/ExactHookBroker.Playback.SourceVerification.cs)、[Group](../src/RizToolbox.Runtime/Hooks/ExactHookBroker.Playback.Group.cs)、[ExecutableSeal](../src/RizToolbox.Runtime/Hooks/NativePlaybackExecutableSeal.cs) | scoped入口/出口仍完整检查整映像与全部member；当前getter进入fresh accessor时可触发该链。不能简单缓存已通过标记继续声称旧合同。 |
| E08 | [GameplayModule](../src/RizToolbox.Adapter/Modules/GameplayModule.cs)、[ControlObservation](../src/RizToolbox.Adapter/Gameplay/GameplayService.ControlObservation.cs)、[NativeBackend](../src/RizToolbox.Adapter/Gameplay/GameplayNativeBackend.cs) | 模块每1s维护之外，Adapter每tick继续pump并对订阅者采集；controller全局发现可重复。r3 descriptor/availability已是data observation，应保留，不再当作旧版本重事务问题。 |
| E09 | [AudioContentModules](../src/RizToolbox.Adapter/AudioContent/AudioContentModules.cs)、[AudioService](../src/RizToolbox.Adapter/AudioContent/AudioService.cs)、[GameCameraModule](../src/RizToolbox.Adapter/Modules/GameCameraModule.cs)、[GameCameraService](../src/RizToolbox.Adapter/GameCamera/GameCameraService.cs) | 三audio能力可分别刷新同后端；camera以极小interval逐tick，空binding可绕发现节流。UI/input Probe也会建立原生对象，不只是查符号。 |
| E10 | [Bootstrap](../src/RizToolbox.Loader/Bootstrap.cs)、[ModuleRuntimeProbe](../examples/RizToolbox.Examples/ModuleRuntimeProbe.cs)、[Build](../scripts/Build.ps1)、[Install](../scripts/Install.ps1) | 默认examples/proof为false，不能说所有诊断默认启用；但日志为回归硬编码Debug、普通包包含examples，status及包周期哈希仍常驻。 |
| E11 | [PluginHost](../src/RizToolbox.Core/PluginHost.cs)、[PluginScope](../src/RizToolbox.Core/PluginScope.cs)、[MutationChannel](../src/RizToolbox.Core/Mutation/MutationChannel.cs)、[PackageLoadContext](../src/RizToolbox.Hosting/PackageLoadContext.cs) | 已有依赖/Waiting/故障撤回、scope取消/逆序释放、代次和覆盖仲裁、私有依赖隔离；应复用。ALC与catch不能保证进程级隔离。 |

所有源码行/哈希可用本批 `verify_docs.py --check` 对E01复核；具体文件可用 `rg -n '方法名' src/...` 重现定位。E02/E03只读原件，不重新运行旧请求。

## Findings

| ID | 状态、置信度与 evidence_ids | 结论及迁移 |
| --- | --- | --- |
| F01 | 静态已确认，高；E01/E04/E05 | 基础Tick自带完整发布成本，host内部计时未包含这段。R02拆成完整安装验证与窄的实际通道检查，并补入口计时。 |
| F02 | 静态已确认，高；E01/E06/E07；历史动态支持E02/E03 | 完整证明在控制包装/样本/参与者之间递归放大。R06建立唯一操作验证入口；内部只查本地归属，保留原生真实writer、fences、读回与恢复责任。 |
| F03 | 静态已确认，高；E01/E05/E09 | 模块按依赖就绪自动激活，SupportedScenes未参与调度；无消费者也可能捕获实际资源。R03先从desired清单求需求，R05再共享维护和发现。 |
| F04 | 静态已确认，高；E01/E05/E08 | 同tick多条路径可重采controller/状态；有些维护在ModuleManager之外，未由其故障边界统一保护。R04定义host tick与观察revision，具名阶段复用数据；不把观察当写权限。 |
| F05 | 静态已确认，高；E01/E05/E10 | 部分研发/磁盘工作仍在普通路径；只将最终写盘放后台不足以去掉主线程序列化成本。R01/R07划分模式和包，对DTO副本排序/合并/持久化。 |
| F06 | 已有源码能力确认，高；E01/E11 | 基础框架已成形，应复用scope、依赖、模块和仲裁；同进程DLL信任模型不能被扩大为恶意代码或native崩溃隔离承诺。 |

以上置信度针对源码结构。新设计是否更快、能否实机稳定变速，仍须新候选验证。正常模式放弃任意外部机器码改写的即时全量检测；旧“完整physical代码篡改必须立刻撤回”的测试需在Developer/对应边界保留，不可静默删除后宣称全部旧保障仍在。

## Paths

**P01：当前 accepted host tick（E04/E05/E08→F01/F04）**

```text
native original正常完成
  → AfterAdmission: channel/thread/epoch/in-flight
  → acceptsTick → 完整NativeFirstInstall.VerifyReady
  → 再读channel → RuntimeBridge(_entered防重入)
  → ToolboxHost: 前Flush → Pump(32)
  → Adapter: 后Flush → retirement → modules → playback维护/观察
  → 同步FrameUpdate → status构造/排序/序列化 → worker写文件
```

第一轮Flush与第二轮之间命令可产生新boundary；应按sequence组织，不能盲删第二次。没有Unity frame ID证据前只称host tick。

**P02：当前倍率请求（E03/E06/E07→F02）**

```text
SDK Execute → Backend Prepare → ProviderAdoption
  → BoundSource.VerifyAuthority → RequireSupported/Probe
  → Host/Provider/Session → formal Clock/source/readback
  → FreshAccessor → SourceVerificationScope
  → 完整Group/映像/member/foundation入口和出口
```

实际样本、各参与者与前后包装还会再次经过此链。目标是一次当前操作拥有真实资格和生命周期责任，不是删除实际检查后仍返回“verified”。

**P03：需求驱动的目标路径（E05/E09/E11→F03/F06）**

```text
纯清单 + desired插件/硬依赖
  → 先登记需求（包括Waiting）
  → 最小scene observer唤醒相符模块
  → 唯一共享后端采集/维护 → SDK观察或显式操作
  → 最后需求撤回 → 旧视图立即失效 → 真实retirement继续 → 最终释放
```

若等Available之后才登记需求，会形成“模块未启用→插件Waiting→永远没有需求”的环。native首次执行前安装与业务服务需求另行管理，不能随意改成懒加载。

## 参考项目与设计判断

- [MuseDashMirror](https://github.com/MDMods/MuseDashMirror)：事件、数据、UI和编译期生成器集中为游戏专用库。它的[GameStart采集](https://github.com/MDMods/MuseDashMirror/blob/main/src/MuseDashMirror/Patches/StageBattleComponentPatch.cs)也有异步采集成本，不能推断整库零额外开销。
- [QuickRestart](https://github.com/MDMods/QuickRestart/blob/master/Main.cs)：场景事件更新一个battle标记，普通Update先早退，按键后再验证并调用游戏操作。借鉴工作时机，不据此宣称其零耗时。
- [SiraUtil Plugin](https://github.com/Auros/SiraUtil/blob/main/SiraUtil/Plugin.cs)：按App/Menu/Player等上下文安装服务，启停与撤Hook有顺序；不等价于本游戏的IL2CPP早装方案。
- [Geode Hook priority](https://github.com/geode-sdk/docs/blob/main/tutorials/hookpriority.md)与[v5事件迁移](https://github.com/geode-sdk/docs/blob/main/tutorials/migrate-v5.md)：共存靠明确前后关系、订阅句柄和生命周期；不同引擎/C++不直接移植。
- [osu! Clock](https://github.com/ppy/osu/blob/master/osu.Game/Screens/Play/GameplayClockContainer.cs)：Seek委托时间源并发OnSeek，不能将它当成判定回滚实现。[Phira练习拖动](https://github.com/TeamFlos/phira/blob/main/prpr/src/scene/game.rs)会同步跳音乐/时间并reset judge/chart，但另一键盘seek分支并无同样reset，不能概括所有seek都完备恢复。
- [.NET官方插件文档](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support)：同进程加载不可信代码不构成安全隔离。当前平台明确只运行用户信任的本地插件。

我的判断是复用现有模块、改运行合同和工作时机；无需为模仿参考项目换语言、另建播放器或重新实现加载器。按R01→R02先做一个完整切片，再推进需求与唯一播放操作。

## 本批交付与限制

五份 [设计文件](../.docs/runtime-redesign/README.md)给出目标与八项源码级任务，全部实现任务保持未完成。本批只核对文档链接、契约覆盖、源码哈希及主README保护，不重复执行旧矩阵。普通包的原版对照、入口总成本、三个用户场景和真实播放操作均是后续验收要求；本批没有新的性能数字。
