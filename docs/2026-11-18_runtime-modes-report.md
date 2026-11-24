# Standard 运行和首批按需服务的离线验证

本批实现启动模式、Tick 入口准入和八个按需模块。以同一次最终 Release 构建核对宿主、测试和发行文件。验证范围为本机受控宿主及自有原生夹具，发行标记为 `preview/unverified`。

| 结论 | 实现 | 证据 |
| --- | --- | --- |
| Standard 默认 Info，Developer 的完整审计和验证插件分别选择。非法模式保留诊断并阻止可选模块和插件执行。 | [配置](../src/RizToolbox.Loader/RuntimeConfiguration.cs)、[启动](../src/RizToolbox.Loader/Bootstrap.cs) | [配置检查](../work/runtime-redesign-implementation-20261117/RuntimeConfiguration-release-r3.log)、[宿主检查](../work/runtime-redesign-implementation-20261117/Loader-release-r3.log) |
| 启动保留完整验证。普通 Tick 读取真实通道信息并核对线程、身份、epoch 和活动状态。Developer 显式完整审计命中自有 relay 的实际改动。 | [入口](../src/RizToolbox.Runtime/Hooks/NativeRuntimeInvokeBridge.cs)、[安装验证](../src/RizToolbox.Runtime/Hooks/NativeFirstInstall.cs) | [原生场景](../work/runtime-redesign-implementation-20261117/Runtime-native-only-r3-current-helper.log)、[原生传输证据](../work/runtime-redesign-implementation-20261117/r02-runtime-validation/runtime-evidence.json) |
| 宿主先处理已有边界，执行命令，再处理命令生成的边界。采集看到同一 Tick 的新边界。 | [宿主](../src/RizToolbox.Loader/ToolboxHost.cs)、[宿主测试](../tests/RizToolbox.Loader.Tests/Program.cs) | [宿主检查](../work/runtime-redesign-implementation-20261117/Loader-release-r3.log) |
| 清单发现时无插件构造。有效的 Waiting 意愿能唤醒能力和硬依赖，撤回时旧视图失效。八个适配模块选择按需，原生早装和视觉退休继续推进。 | [包发现](../src/RizToolbox.Hosting/PluginPackageLoader.cs)、[需求](../src/RizToolbox.Core/PluginHost.cs)、[模块](../src/RizToolbox.Adapter/Modules/ModuleManager.cs) | [Hosting](../work/runtime-redesign-implementation-20261117/Hosting-release-r3.log)、[生命周期](../work/runtime-redesign-implementation-20261117/PackageLifecycle-release-r3.log)、[模块检查](../work/runtime-redesign-implementation-20261117/Module-release-r3.log) |
| 三种发行包使用相同的 15 个运行文件。Author 附 28 份资源，Validation 附 15 份。合成升级保留已有验证示例、用户插件、配置、启用意愿和日志。部分写入失败能恢复原文件与安装权属。 | [打包](../scripts/PackageDistribution.Common.ps1)、[安装](../scripts/Install.ps1) | [发行核对](../work/runtime-redesign-implementation-20261117/distribution-readback-r3.json)、[合成安装](../work/runtime-redesign-implementation-20261117/r01-package-validation-20261118/artifacts/release-r3-current-doorstop/results.json)、[升级回滚](../work/runtime-redesign-implementation-20261117/r01-package-validation-20261118/artifacts/release-r3-current-doorstop/partial-upgrade/results.json) |

最终 [Release 构建](../work/runtime-redesign-implementation-20261117/solution-release-r3.log) 为 0 警告、0 错误。12 套定向回归共 738/738。其中 Runtime 全组使用当前 helper 和匹配的初始化夹具后为 389/389。四个原生到托管场景通过，[当前原生传输夹具](../work/runtime-redesign-implementation-20261117/native-transport-release-r3-current-helper.json)通过 274/274。更早的 Runtime 日志记录了环境变量缺失和旧夹具导出不匹配，计数以[当前配对结果](../work/runtime-redesign-implementation-20261117/Runtime-current-helper-r3.json)为准。三包的六个托管组件与[源码和产物清单](../work/runtime-redesign-implementation-20261117/source-freeze-r3.json)逐一匹配。

三种发行包的首次安装与升级均在合成目录验证。升级保留了 18 个原文件，部分写入回滚校验了 212 个安装和用户文件。作者模板和示例源码在独立工程编译通过，验证包内两套原生夹具运行通过。安装回归中的 13 个既有事务用例也通过。所有发行目录留在工作区，状态为 `preview/unverified`。

原生检查以自有 channel 执行真实进入、原函数返回和退出。Standard 在绑定后处理 4096 次不匹配回调时观察到 0 B 托管分配、0 次计时和额外通道读取。该结果只覆盖这条短路径。Accepted Tick 的统计从帧匹配筛选后的准入开始，含当前通道核验、Developer 显式审计和宿主调用。游戏原函数和采样投递另计。

完整 GameAssembly 启动链和真实帧耗时尚未验证。组合 UI/Input、共享音频和实验播放仍有常驻维护。当前仍须完成这些服务的职责拆分，再评估实机卡顿。真实 Speed/Seek、完整 B01–B19 及生产准入保持待验。
