# 跨插件共享服务

`InterPluginServiceRegistry` 提供版本化服务发布、按范围查询、可选就绪通知、实际 `DispatchProxy` 调用和只读消息通道。提供者登记、消费者调用及消息回调从宿主主线程进入。插件只引用 SDK 的 `IPluginServices`，不引用 Core。

本模块的自动检查使用生产 registry 和独立作用域夹具，包括真实可收集 ALC 的同名契约拒绝。尚不能据此宣称两个实际游戏插件包的全部 B17 集成已完成；能力登记和游戏内双包验证由 Loader 接入完成。

## 宿主接入

在宿主主线程构造并持有 registry，通过作用域服务工厂提供插件视图：

```csharp
using RizToolbox.Core.Services;
using RizToolbox.Sdk;

// main、log 和 capabilities 是宿主现有对象。
var services = new InterPluginServiceRegistry(main, log,
    new ServiceCallLimits(MaximumPayloadBytes: 65536, TimeoutMilliseconds: 1000, MaximumCallDepth: 16));
capabilities.RegisterScoped<IPluginServices>(InterPluginCapabilityIds.Services,
    context => services.ForPlugin(context));
// 宿主结束时，在主线程调用 services.Dispose()。
```

提供者、消息接收者或就绪观察者故障会通过 `ProviderFaulted(pluginId, exception)` 上报，也可在 `ForPlugin(context, onFault)` 传入具体作用域的故障处理。registry 立即撤回出错提供者的登记，或移除出错消息/就绪订阅；宿主可连接 `services.ProviderFaulted += (id, error) => host.ReportPluginFault(id, error)`，复用 PluginHost 已有的依赖拓扑停用。`ReportPluginFault` 只接受主线程和当前活跃插件，未知 ID、空异常或已释放宿主返回失败。硬依赖仍由 PluginHost 管理，软依赖通过 Watch 降级，registry 不修改清单依赖或冒充插件生命周期管理器。

## 提供一个 SDK 契约

首版附带普通共享契约 `IPluginDataService.Invoke(ServiceRequest) -> OperationResult<ServiceData>`。请求包含 Operation 和可选只读字符串字典，响应包含 Value 和可选只读字符串字典。

```csharp
// 以下位于 IRizPlugin.Enable(IPluginContext context)。
var lookup = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services);
if (!lookup.Available) throw new InvalidOperationException(lookup.Error?.Message);
var result = lookup.Service!.Publish<IPluginDataService>("example.notes", new Version(1, 0), new NotesService());
if (!result.Success) throw new InvalidOperationException(result.Error?.Message);
// 登记自动归属 context；插件停止时撤回服务并使旧代理失效。

// 以下类型位于插件程序集。
sealed class NotesService : IPluginDataService
{
    public OperationResult<ServiceData> Invoke(ServiceRequest request)
        => request.Operation == "read"
            ? OperationResult<ServiceData>.Ok(new ServiceData("shared result"))
            : OperationResult<ServiceData>.Fail(ErrorCode.OperationUnsupported, "Unknown operation.",
                InterPluginCapabilityIds.Services, "example.notes");
}
```

同一个服务 ID 同时只允许一个提供者。重复发布返回 `Conflict`，在错误中保留能力、服务 ID，并在消息中列出当前提供者。登记对象本身由作用域自动 Own；提供者对象如果还持有其他 IDisposable 资源，应另行交给其自身作用域管理。

## 查询和可选就绪通知

```csharp
// services 是当前 context.GetService<IPluginServices>(...).Service。
var query = new ServiceQuery("example.notes", new Version(1, 0), new Version(2, 0));
var subscription = services.Watch(query, state => context.Log.Log(LogLevel.Info,
    state.Available ? "Notes ready." : state.Error?.Message ?? "Notes unavailable."));

var result = services.Lookup<IPluginDataService>(query);
if (result.Available)
{
    var response = result.Service!.Invoke(new ServiceRequest("read"));
    context.Log.Log(LogLevel.Info, response.Value?.Value ?? response.Error?.Message ?? "Empty result.");
}
```

Watch 同步提供一次初始快照，之后仅在可用状态、提供者代次或版本描述变化时通知。订阅自动归属当前作用域。Lookup 使用最小版本包含、最大版本排除；未就绪、版本不兼容或撤回返回 `ServiceUnavailable`。接口类型必须与登记时的 `Type` 完全相同。

`ServiceResolution<T>.Error` 与 `ServiceAvailability.Error` 均为 `OperationError`；`Available == false` 时按代码判断失败，`Message` 用于显示。尚未出现或刚撤回的服务可以带 `Retryable == true`，版本或类型不匹配需要调整请求。提供者显式返回的失败不触发提供者故障；五个错误字段经过响应快照后保持一致。`OperationResult<T>.Ok(null)` 是合法的空成功值，消费者仍应检查具体契约是否允许空内容。

每次发布分配新 Generation。提供者撤回后，缓存代理立即失效；同 ID 再发布不会让旧代理复活，消费者应重新 Lookup。代理也检查消费者作用域，消费者停用后不能继续调用。

提供者开始停用时，Lookup 即返回 `ServiceUnavailable`。已经进入的同步调用完成后，会再次核对提供者登记和消费者作用域才交付结果。异步调用的等待会随服务撤回或消费者停用而结束。

故障按调用所用的登记和作用域归属。若撤回通知或主线程派发期间已停用旧实例，重新启用的同 ID 插件会保有自己的服务登记。

## 共享类型身份

SDK 中的公开接口与普通 DTO 自动允许。额外契约必须先由宿主明确加载到默认 ALC，再校验其程序集文件版本及 SHA-256：

```csharp
// ITeamContract 来自宿主已经明确共享的契约程序集；actualPinnedSha256 来自版本化依赖锁。
var approved = servicesRegistry.AllowSharedContract<ITeamContract>(actualPinnedSha256);
if (!approved.Success) throw new InvalidOperationException(approved.Error?.Message);
```

这不是私有 ALC 间自动统一。两个插件各带同名 DLL，即使名称、版本及字节一致，也会得到不同 Type 身份而被拒绝。契约包加载、版本与哈希分发策略必须由宿主建立；服务 registry 只核验明确批准的身份。

接口参数与返回值限于基本类型、SDK/已批准程序集中的普通 DTO、一维数组、只读列表和字符串键字典。禁止指针、IntPtr、Type、object、委托、事件、ref/out、泛型方法及资源句柄。继承接口也递归校验。请求和响应经 JSON 快照复制，避免一个插件修改另一插件的字典或数组背后的存储。

## 调用预算与故障归属

| 限制 | 默认值 | 行为 |
|---|---|---|
| 请求、响应或消息大小 | 65,536 字节 | 超大请求在进入提供者前拒绝；超大响应撤回提供者 |
| 调用耗时 | 1,000 ms | 同步调用返回后检查预算；Task/Task&lt;T&gt; 受等待截止时间约束 |
| 服务/消息调用深度 | 16 层 | 达限拒绝，并输出服务链路 |
| 参数数量 | 8 | 超过数量的契约不允许登记 |
| 同服务递归 | 不允许 | 返回含 `ServiceCallCycle` 和所有者路径的错误 |

同步线程内的插件代码不能被强行中断；超预算同步调用会在执行返回后报告 `ServiceTimeout`、撤回其提供者。异步超时只结束本次等待并撤回代理，无法取消提供者已经启动的任意副作用。此边界不提供进程隔离。

提供者异常、无效响应和超时归属提供者；错误请求和错误线程不会误停提供者。嵌套服务异常只归属最初故障提供者，转发者不会因相同异常被重复报告。异步异常通过主线程调度上报，消费者作用域取消单独处理，不误判为提供者故障。

## 发布只读消息

```csharp
// services 为 IPluginServices 插件视图。
var channel = new MessageChannel<ServiceData>("example.notice", new Version(1, 0));
var subscription = services.SubscribeMessage(channel,
    value => context.Log.Log(LogLevel.Info, value.Value), priority: 10);
if (!subscription.Success) throw new InvalidOperationException(subscription.Error?.Message);

var sent = services.PublishMessage(channel, new ServiceData("ready"));
if (!sent.Success) throw new InvalidOperationException(sent.Error?.Message);
```

频道首次使用固定其类型与版本。消息按优先级降序、同优先级登记顺序同步分发；每个接收者得到独立快照，不能修改发布者或后续接收者的数据。订阅自动清理，接收者异常不阻断其他插件。递归发送相同频道会被拒绝；频道没有跨进程传输、历史重放或持久化承诺。

## 复现检查

```powershell
dotnet run --project tests/RizToolbox.Services.Tests/RizToolbox.Services.Tests.csproj -c Release
```

对应检查覆盖实际 DispatchProxy、版本范围、重复登记、初始及变化通知、提供者重建、作用域与线程检查、请求响应限额、快照隔离、异常归属、循环链路、同步预算、Task 超时、消费者取消、宿主哈希批准、私有 ALC 同名拒绝、危险/继承接口拒绝、消息和清理。正式 PluginHost 集成检查通过 `context.GetService<IPluginServices>` 获取作用域代理；提供者抛异常时正确停用提供者，捕获异常的消费者仍保持启用。每轮通过数以该轮冻结源码及运行日志为准，历史结果不继承为新合同验收。

证据链：检查执行 Core 实现并创建独立 ALC，证明版本代理和身份隔离机制可复现；真实游戏中的两个独立插件包、硬依赖停用以及 Loader 能力生命周期仍需后续集成证据。
