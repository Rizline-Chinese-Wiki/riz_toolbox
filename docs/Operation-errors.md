# 处理操作失败

当前未发布候选统一使用 `OperationError(Code, Message, CapabilityId?, TargetId?, Retryable)`。`OperationResult` / `OperationResult<T>` 的错误字段、服务查询与可用性、目标查询均携带这个对象。已有领域服务的 `Result` / `Result<T>` 保持各自返回类型，也使用相同错误。插件应随当前 SDK 重新编译。

## 读取和传播错误

以下代码位于插件 `Enable(IPluginContext context)` 或其他主线程回调中：

```csharp
var lookup = context.GetService<ICatalogService>(CapabilityIds.Catalog);
if (!lookup.Available)
{
    var error = lookup.Error!;
    context.Log.Log(LogLevel.Warning, $"{error.Code}: {error.Message}");
    return;
}
var charts = lookup.Service!.GetCharts();
if (!charts.Success)
{
    var error = charts.Error!;
    switch (error.KnownCode)
    {
        case ErrorCode.NotReady:
        case ErrorCode.CapabilityUnavailable:
            context.Log.Log(LogLevel.Info, error.Message);
            break;
        default:
            context.Log.Log(LogLevel.Warning, $"{error.Code} ({error.TargetId}): {error.Message}");
            break;
    }
    return;
}
context.Log.Log(LogLevel.Info, $"Charts: {charts.Value!.Count}");
```

`Retryable` 表示状态以后可能变化，不会自动安排重试。只在服务的线程、作用域和代次仍有效时重试；目标已失效时重新发现目标。缺少适用能力或目标时，其 ID 可以为空。已知目标的底层错误应保留原 ID，转发者不覆盖其能力或可重试性。

创建新失败时声明代码和消息；传播已有失败时直接传递错误对象：

```csharp
OperationResult<ServiceData> LoadSharedData(OperationResult<string> source)
{
    if (!source.Success) return OperationResult<ServiceData>.Fail(source.Error!);
    if (string.IsNullOrWhiteSpace(source.Value))
        return OperationResult<ServiceData>.Fail(ErrorCode.InvalidArgument,
            "A nonempty value is required.", InterPluginCapabilityIds.Services, "example.data");
    return OperationResult<ServiceData>.Ok(new(source.Value));
}
```

没有文本 `Fail(string)`、错误消息前缀分类或旧记录构造重载。`Code` 是机器字段，`Message` 是给人阅读的说明。`KnownCode` 将未知数值归为 `Unknown`，原始 `Code` 和其他四个字段仍保留；未知错误必须走失败分支。

## 状态与传输约束

| 返回状态 | Value | Error |
|---|---|---|
| `OperationResult.Ok()` | 不适用 | null |
| `OperationResult<T>.Ok(value)` | 原值；契约允许时可以为 null | null |
| `OperationResult.Fail(error)` | 不适用 | 同一个非空错误对象 |
| `OperationResult<T>.Fail(error)` | `default(T)` | 同一个非空错误对象 |

结果只能由工厂创建。失败要求非空错误和非空白消息；成功值是否为 null 不用于推断成功。默认 JSON 转换器通过同样的工厂重建结果，支持结果嵌套在共享 DTO 中，并拒绝成功带错误、失败带有效值、缺少成功值字段或缺少失败错误的矛盾输入。服务响应快照保留错误的五个字段，成功值中的字典和数组仍独立复制。

服务视图在失效作用域或错误线程访问时返回 `ScopeExpired` / `WrongThread`，附带能力及已知目标，且不调用后端。返回普通值或资源清理的接口仍遵循各自异常合同。跨插件提供者抛出的异常继续使用故障归属和撤回流程；提供者正常返回的结构化失败不会被误判为插件故障。

`CapabilityDescriptor.UnavailableReason` 等能力说明是诊断元数据。可执行服务查询使用 `ServiceLookup<T>.Error`；跨插件查询与 Watch 使用 `ServiceResolution<T>.Error`、`ServiceAvailability.Error`。

## 错误代码目录

数值定义来自当前 `ErrorCode`。增加代码不能改变已有数值；消费者保留未知数值并通过 `KnownCode` 降级。

| 数值 | Code | 含义 |
|---|---|---|
| 0 | Unknown | 未识别的错误或未来数值的分类 |
| 1 | InvalidArgument | 参数无效或必需参数缺失 |
| 2 | SdkIncompatible | SDK 范围不满足 |
| 3 | CapabilityUnavailable | 所请求能力未就绪或已撤回 |
| 4 | UnsupportedBuild | 游戏构建不受支持 |
| 5 | UnsupportedSignature | 声明、签名或精确版本不匹配 |
| 6 | TargetExpired | 原目标代次已消失、失效或被替换 |
| 7 | ScopeExpired | 启用、场景、会话或租约作用域已结束 |
| 8 | WrongThread | 操作未在要求的线程上执行 |
| 9 | QueueFull | 有界工作队列已满 |
| 10 | Cancelled | 操作取消 |
| 11 | TimedOut | 等待超过截止时间 |
| 12 | DependencyUnsatisfied | 必需插件缺失、不兼容或不可启用 |
| 13 | MigrationFailed | 配置迁移失败 |
| 14 | MutationNotSupported | 该目标或策略不支持所请求修改 |
| 15 | Conflict | 目标、服务或生命周期资源存在冲突 |
| 16 | ObservationUnavailable | 所需读取或观察不可用 |
| 17 | NotReady | 数据或对象仍在准备 |
| 18 | OperationUnsupported | 当前实现没有该操作 |
| 19 | InvalidGameState | 当前游戏状态不允许该操作 |
| 20 | TargetMissing | 尚未找到匹配目标 |
| 21 | AmbiguousTarget | 查询匹配多个目标 |
| 22 | ResourceLoadFailed | 资源或持久数据加载失败 |
| 23 | NavigationUnavailable | 所需导航目标或入口不可用 |
| 24 | ContentInvalid | 内容格式或内容数据无效 |
| 25 | ConfigInvalid | 配置内容无效 |
| 26 | QuotaExceeded | 固定配额或大小限制已达到 |
| 27 | ServiceUnavailable | 跨插件提供者不存在或已撤回 |
| 28 | InternalError | 未预期的内部或回调错误 |
| 29 | ContractIdentityMismatch | 共享程序集或接口类型身份不匹配 |
| 30 | UnsupportedContract | 契约包含禁止或不支持的数据/成员 |
| 31 | ServiceCallCycle | 服务或消息调用形成循环 |
| 32 | ServiceCallDepthExceeded | 嵌套调用深度超过限制 |
| 33 | ServicePayloadTooLarge | 服务或消息载荷超过限制 |
| 34 | ServiceRequestRejected | 服务请求未通过边界检查 |
| 35 | ServiceProviderFailed | 提供者执行失败 |
| 36 | TargetTypeMismatch | 目标或属性的值类型不匹配 |
| 37 | TargetGenerationExhausted | 不可复用的目标代次已耗尽 |
| 38 | MutationReentrancy | 修改求值期间发生禁止的重入 |
| 39 | MutationWriteFailed | 修改写入或验证失败 |
| 40 | DependencyCycle | 依赖或顺序约束形成循环 |

## 复现合同检查

在仓库根目录运行；将每批构建放入独立目录：

```powershell
dotnet build tests/RizToolbox.Sdk.Tests/RizToolbox.Sdk.Tests.csproj --artifacts-path D:/riz-build-cache-next/sdk-structured-errors-check
dotnet D:/riz-build-cache-next/sdk-structured-errors-check/bin/RizToolbox.Sdk.Tests/debug/RizToolbox.Sdk.Tests.dll
```

检查覆盖不可构造矛盾结果、空错误/空消息拒绝、未知数值保留、嵌套及 camelCase JSON、提供者错误传播、作用域/线程拒绝与当前宿主的 SDK 范围诊断。插件程序集身份与独立加载由 Hosting/Services 的对应回归验证；离线检查不代表游戏内全部能力已验收。
