# UnityEvent来源与监听图

2026-10-23开始，2026-10-24完成冻结。**1449/1449、25套完整离线检查通过，冻结暂存未安装**。Runtime153/Hooks127/Audio558，较[1412](../foundation-cutout-async-20261023/README.md)新增37组；当前安装仍1021。

## 实现与接线

[ledger](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.cs)与[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.cs)在activation前，从同launch的生产Source同步借用实际UnityEvent根并立即持自己的独立根；原Source责任不转移。记录serialized persistent候选、persistent/runtime/executing缓存、delegate Target/Method及只读签名。32事件、128聚合rows/leaves、2048独立根与32物理深度均有界，未知类型/静态/open/virtual/generic或不完整多播保持opaque。

Invoke采用既有early retained观察与全线程physical probe，复制Pre/Post，worker、错误MethodInfo、同event重入和原函数异常保留Unknown。独立List.Prepare采用真实ref0 native观察，原返回list先保根，再核实际executing对象/顺序与独立代次。**固定版本Invoke内联Prepare**，独立入口不能覆盖它；InlineExecutionUnobserved=true/DispatchPathVerified=false始终保留，不虚构caller、不要求每次Invoke有Prepare，也不安装ABI不同但共享RVA的叶Invoke hook。

[Runtime metadata](../../../src/RizToolbox.Runtime/NativeRuntime.ListenerMetadata.cs)只从实际rooted delegate核官方布局、有限MethodInfo与declaring-class成员关系，给只读metadata；受限ref0 resolver只接受两个确切Prepare入口及真实闭返回class。不主动调用listener、Prepare/Rebuild/GetRuntimeCall，不开放任意地址调用。

[coordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)在激活前Capture、Ready原生操作前后核idle、传播Unknown并显示纯快照。Source、initial、terminal、active四条路径补每次Runtime操作/Target/native读及清理的共享launch故障检查；返回引用先保存已取得的独立根，正常参数消费确认后再核late fault。局部free失败继续其他已知独立根的一次尝试；外部authority故障停住后续操作，最后一根free内故障也不能返回确认成功。

## 验证和限界

[14组Runtime](../../../tests/RizToolbox.Runtime.Tests/ListenerMetadataRegression.cs)与[13组事件](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalUnityEventLedgerRegression.cs)首轮在[153/153](runtime-targeted-first.log)、[548/548](audio-targeted-first.log)通过。跨owner屏障新增10组：Source1、initial4、terminal1（7阶段）、active4。active用真实生产backend/registry与FakeNative publication/unbox，metadata/roots由fixture注入，未走游戏metadata初始化。新增fixture第二实例复用身份造成[556/558](audio-after-authority-first.log)，改为逐实例唯一metadata和wrapper地址后，[最终558/558](audio-targeted-final.log)与[完整25套](build-summary.json)通过；生产distinct约束未放宽、未清owner配额。

[交叉检查](review.md)由三代理分工与root统一runner完成。受控broker/probe/metadata和部分生产backend故障回归不等于实际游戏listener、生产GC free故障注入或native original异常展开验收。native helper和ABI fixtures保持既有范围，未重复计入1449。完整增量0编译诊断；定向构建17条既有测试warning保留，不能宣称全仓clean build零warning。

初次完整构建的24套代码检查通过，但末尾SDK分发隔离restore遇NuGet EOF/连接重置，[原失败日志](build-first-sdk-network-failure.log)保留。相同依赖[单独重试](sdk-restore-retry.log)成功后，完整流程再次从头运行通过；没有跳过SDK分发或改动生产distinct/依赖准入。

[23范围/1706指令](terminal-evidence.json)、[逐字节核对](terminal-evidence-check.json)、[独立三函数264指令](listener-prepare-evidence.json)与[metadata路线说明](listener-metadata-and-prepare.md)固定三输入hash、6直接E8候选；不是完整执行调用图。

监听列表连续mutation、inline实际执行路径、listener/provider转交、销毁取消、全部active消费者与资源退休、完整Retry/Return/Loader仍缺；Pending保留，不签whole-run结束或Close，RequestPlay关闭。 详见[scope](scope.md)与[下一步](next-integration.md)。B01–B19仍active，仅B03整项/B18a子项勾选。

## 冻结与复现

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | 424项，4,517,447字节 | `4c3dd3eff3e9701c9ac161b5986012de332f009c538c1c6a2eafc4dba7e17332` |
| [源码manifest](source-manifest.json) | 424项 | `f3db09a22fd66eb068fd596a791d08f51a1d3a208b4d10858e166076224c408e` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | 18项，3,316,712字节 | `8d416e727f052ddbdf845996f56d51fda7ba2aecd77362f4f9e4018d534f4239` |
| [完整日志](build.log) | exit0 | `b1680db7312d37389c366ff5eb74b23ce19a0b40845e2561c8348656dff6db95` |

[较1412差分](changes-from-1412.diff)/[清单](changes-from-1412.json)：新增5、修改15、删除0，两源ZIP各自核对。[暂存](staging-check.json)/[源码](source-integrity-check.json)、[旧1021安装与旧326归档](previous-installation-check.json)及[原游戏三文件](original-game-check.json)匹配。

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/unity-events-reproduce-20261023'
```

用户“先继续代码，实测稍后”保持。无本批安装、游戏/UI操作、游戏进程终止、原文件修改、commit/push。

后续具体单元见[inline执行计划](next-inline-plan.md)与[取消证据计划](next-cancel-plan.md)，仅为下一步设计。[文档链接核对](documentation-check.json)不增加运行时验收。
