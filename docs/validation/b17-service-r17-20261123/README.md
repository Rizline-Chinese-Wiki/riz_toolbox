# B17 跨插件服务 r17 离线验收

r17 的 Services 回归通过 **26/26**。另用 r17 Author ZIP 分别编译提供者、硬依赖消费者和软依赖消费者，用 r17 Basic ZIP 的正式 Core、Hosting 与 SDK 在六个独立 .NET 8.0.24 进程中通过 **17/17** 项包发现、启用和服务检查。三个插件各有独立的可收集 ALC，程序集引用限于 SDK 和 .NET 框架。[总回执](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/validation.json) · [B17 规格](../../../.docs/foundation-expansion/SPEC.md#b17-inter-plugin-contracts)

Basic 的六份托管 DLL 与其归档成员清单及封存候选逐项匹配。Author ZIP 的离线 NuGet 源从空缓存还原，引用程序集与 r17 冻结编译引用的 SHA-256 一致。六个进程分别记录了实际载入的三个框架组件和测试插件的绝对路径、哈希与加载上下文。Services 使用冻结解决方案已有的测试程序集，运行目录内的 Core 和 SDK 均从 Basic ZIP 解出。[Basic ZIP](D:/riz-m2/r17-standard-players-20261123/publication/Basic/RizToolbox-r17-Steam-Basic.zip) · [Author ZIP](D:/riz-m2/v5-live-fixes-r17-20261123/publication/author/RizToolbox-r17-Plugin-SDK.zip) · [Services 原始日志](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/logs/packaged-services.log)

## 验收结果

| 要求 | r17 实际结果 | 证据 |
| --- | --- | --- |
| 独立包调用与版本范围 | 两个消费者通过发布者的同一服务代次调用。最低版本相等时接受，超过当前版本的下限及当前版本等于排他上限时返回带服务 ID 的 `ServiceUnavailable`。 | [合同进程](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/logs/service-contracts.json) |
| 类型身份、ALC、重复登记 | 错误的共享 SDK 接口与私有 ALC 接口分别被拒绝。独立回归核对默认 ALC 的额外合同需正确哈希，并拒绝同名同字节的私有 ALC 合同。重名登记返回 `Conflict`，原登记仍可调用。 | [合同进程](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/logs/service-contracts.json) · [Services 日志](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/logs/packaged-services.log) |
| 撤回、重新发布和依赖拓扑 | 停用时硬依赖先于提供者停止。软依赖继续启用并收到一次不可用通知。恢复后两个消费者调用新代次，旧代理仍失效。其余进程覆盖显式停用、提供者目录缺失与复位、消费者跨重启停用。 | [停用与恢复](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/logs/disable-provider.json) · [五个生命周期进程](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/validation.json) |
| 故障归属与饱和队列 | 包内真实服务抛出异常，软消费者捕获代理异常并保持启用。宿主将故障报告给提供者一次，硬依赖先停止再等待，软依赖收到撤回通知。额定容量为一的队列填满后，独立 Services 回归仍在主线程交付异步故障、撤回代理并结束另一在途等待。 | [包内故障与恢复](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/logs/service-contracts.json) · [饱和队列日志](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/logs/packaged-services.log:11) |
| 消息、回调递归与调用限制 | 消息按优先级分发独立副本并拒绝递归发送。接收者出错时，其他订阅继续收到消息。服务递归、就绪观察者异常、线程、请求大小和调用期限均有定向断言。来自提供者的业务失败保留错误代码、消息、能力、目标和可重试性。 | [Services 日志](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/logs/packaged-services.log) · [错误字段](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/logs/service-contracts.json) |

普通 .NET 宿主通过 `PluginPackageLoader.DiscoverDirectory`、`LoadDiscovered`、`PluginActivationStore.Bind` 与 `PluginHost.SetDesiredEnabled` 运行上述插件。**B17 实机验收仍开放**。需要在同一 r17 的 Steam 会话验证 Unity `ToolboxHost` 的发现与启用路径，然后触发提供者故障，记录主线程交付、硬依赖停用顺序、软依赖撤回通知、旧代理失效及明确恢复。离线饱和用例的主线程调用来自测试宿主。[游戏宿主接线](../../../src/RizToolbox.Loader/ToolboxHost.cs#L353) · [历史实机验收项](../../IMPLEMENTATION-STATUS.md#19-项任务逐项状态)

## 复跑

使用 PowerShell 7.2 以上和 .NET 8 SDK。脚本读取 r17 的标准、候选及作者交付回执，并复用冻结解决方案已有的 Services 测试程序集。指定一个新目录保存结果。

```powershell
pwsh -NoProfile -File D:/riz-m2/b17-r17-plugin-host-20261123/runner/Run-Lifecycle.ps1 `
  -OutputDirectory "D:/riz-m2/b17-r17-plugin-host-20261123/recheck-$(Get-Date -Format yyyyMMdd-HHmmss)"
```

前两次失败来自测试探针。第一次选择的接口在比较已登记 Type 前已被合同签名规则拒绝。第二次断言对代理包装的异常使用了原异常的消息。更换接口并检查包装内的原异常后，完整重跑了六个进程。[首次记录](D:/riz-m2/b17-r17-plugin-host-20261123/run-01/logs/service-contracts.json) · [第二次记录](D:/riz-m2/b17-r17-plugin-host-20261123/run-02/logs/service-contracts.json) · [复跑脚本](D:/riz-m2/b17-r17-plugin-host-20261123/runner/Run-Lifecycle.ps1)
