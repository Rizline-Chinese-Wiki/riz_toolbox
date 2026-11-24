# SDK 1.2 兼容与结果契约（历史记录）

本页记录 SDK 1.2.0 当时的结果契约、程序集身份和兼容测试。r15 使用 SDK 1.3.0，面向该版本编写插件请使用[当前 SDK 指南](SDK.md)。

当时的程序集身份为 `RizToolbox.Sdk, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null`。1.2 沿用了既有接口和构造函数，原测试中的 1.1 编译插件可以继续加载。

这次交付包括结构化结果及转换实现、SDK 范围检查、不可变能力元数据目录和真实旧插件的兼容验证。能力元数据是可选查询契约；适配器必须登记真实描述，才可用于报告某项游戏能力。新增 DTO 本身不表示相应游戏能力已经实现。

## 检查插件可用的 SDK 范围

插件清单采用最低版本包含、最高版本不包含的范围：

```csharp
using RizToolbox.Sdk;

var manifest = new PluginManifest(
    "wiki.example", new Version(0, 2, 0), new Version(1, 1, 0),
    MaxSdkExclusive: new Version(2, 0, 0));
Result compatible = SdkCompatibility.Check(manifest);
if (!compatible.Success)
    Console.WriteLine($"{compatible.Error!.KnownCode}: {compatible.Error.Message}");
```

`SdkCompatibility.Check` 可以从任意线程调用。范围无效返回 `InvalidArgument`；宿主版本不在范围内返回 `SdkIncompatible`，目标 ID 为插件 ID，不能靠立即重试解决。这个检查只负责版本范围；正式包加载器还检查程序集身份、共享 SDK、入口类型和清单一致性。宿主已有的包诊断代码继续使用 `sdk-incompatible` 字符串，以保留原有日志契约。

新插件仅在使用新 API 时将 `minSdk` 提高到 `1.2.0`。旧插件不需要为了版本号变化重新编译。插件包不携带私有 `RizToolbox.Sdk.dll`，否则正式包加载器会拒绝它。

## 给操作返回可判断的错误

`OperationResult` 与 `OperationResult<T>` 完整保留。新增的 `Result` 与 `Result<T>` 通过静态工厂保证成功没有错误、失败具有 `OperationError`。成功的泛型结果可以包含 null，调用者必须按具体服务契约解释空值，不能用空值推断失败。

```csharp
using RizToolbox.Sdk;

Result<int> pending = Result<int>.Fail(new OperationError(
    ErrorCode.NotReady, "曲目数据尚未加载", CapabilityIds.Catalog,
    TargetId: "chart/123", Retryable: true));

OperationResult<int> oldView = pending.ToLegacy();
Result<int> explicitView = oldView.ToResult(
    ErrorCode.NotReady, CapabilityIds.Catalog, "chart/123", retryable: true);
```

| 字段 | 含义 |
|---|---|
| `Code` | 稳定数值错误代码；未知未来数值仍保留原值 |
| `KnownCode` | 当前 SDK 认识的代码，未知数值降为 `Unknown` |
| `Message` | 可显示的原因，不用它解析控制分支 |
| `CapabilityId` / `TargetId` | 已知时携带能力及目标身份，不包含原生指针 |
| `Retryable` | 允许稍后重新检查的提示，调用方仍须遵守调度与状态规则 |

`ToLegacy` 保留成功值或错误消息，会丢失结构化代码、ID 和可重试性。`ToResult` 默认使用 `Unknown`，不猜测旧错误字符串；适配器在确知原因时可以显式提供代码。消息缺失的旧失败会获得明确的后备消息。转换和版本检查不访问 Unity、不需要清理、不修改游玩状态。

`ErrorCode` 为计划中的各类失败提供稳定数值身份，但只有具体服务真正返回某代码时，才能宣称该失败分支已经接入。调用方必须为所有枚举提供未知值分支；对无法识别的操作线程、观察来源或清理类型，不应执行写操作或假定其受支持。

## 描述能力的准确范围

`CapabilityInfo` 是某次能力状态的不可变快照，带能力版本、稳定性、可用性、可用性代次和操作列表。`Generation` 只标识能力提供者的可用性变化，不可充当游戏对象的寿命代次。

每个 `CapabilityOperationInfo` 声明独立操作 ID、线程、观察来源、采样间隔、清理责任、是否修改游玩及适用场景。`Sampled` 可以声明正的有限秒数；`Snapshot` 或 `DirectCallback` 不允许填写采样间隔。采样间隔只说明调度间隔，不保证投递延迟或没有漏报。空场景列表表示尚未声明场景；`"*"` 才明确表示与场景无关。

下面是在适配器已经核验相关行为后建立描述的方法，`test.runs` 是测试 ID，不代表游戏适配器已提供它：

```csharp
using RizToolbox.Sdk;

ICapabilityMetadataProvider metadata = new CapabilityMetadataCatalog(new[]
{
    new CapabilityInfo("test.runs", new Version(1, 0, 0),
        CapabilityStability.Experimental, available: true, generation: 1,
        operations: new[]
        {
            new CapabilityOperationInfo("observe", OperationThread.MainThread,
                ObservationMode.Sampled, CleanupResponsibility.EnableScope,
                modifiesGameplay: false, supportedScenes: new[] { "Level" },
                description: "正常模式局状态采样", samplingIntervalSeconds: 0.25)
        })
});
Result<CapabilityInfo> description = metadata.Describe("test.runs");
```

`CapabilityMetadataCatalog` 构造时复制输入，拒绝重复 ID 和冲突描述，查询以 ordinal ID 区分大小写，结果按 ID 排序。目录只读，可以跨线程共享；读描述不激活能力，也不授予服务。目录中已声明但暂不可用的项仍可成功查询，调用者查看 `Available` 和 `UnavailableReason`。不存在的 ID 返回 `CapabilityUnavailable`。

宿主可以把提供者以 `ICapabilityMetadataProvider` 登记到 `CapabilityIds.Metadata`（`riz.capability-metadata`）。插件使用原有 `GetService<ICapabilityMetadataProvider>` 查询，必须处理能力尚未登记的情况。当前 0.3 宿主已登记实时提供者，能力注册、撤回和替换后查询会反映新代次；Adapter 为现有操作声明线程、清理及采样语义。独立的 `CapabilityMetadataCatalog` 仍是不可变目录，不能把它本身理解为自动刷新服务。B06 的完整模块化和重探测恢复仍待实现。

## 重现兼容验证

仓库根目录执行：

```powershell
dotnet run --project tests/RizToolbox.Sdk.Tests/RizToolbox.Sdk.Tests.csproj -c Release
```

13 项检查涵盖：旧样本哈希、程序集身份、旧公开 API 保留、禁止给已有接口增加强制实现成员、保存的 API 基线、真实旧插件加载/执行/配置/事件/自动清理、不兼容范围独立拒绝、结构化结果转换、未知枚举、版本边界、元数据快照与拒绝分支，以及不实现新可选接口的旧样式上下文。

旧样本来自本机已安装的 0.2 包，捕获后没有重新编译。正式 `PluginPackageLoader` 将旧 `StatusPanelPlugin` 放入独立 ALC，绑定新的共享 SDK；当前 `PluginHost` 启动它，替代 UI 接口记录创建和更新，真实 Core 配置保存、事件订阅及退出清理都执行。测试并未启动游戏，不能替代游戏内验收。

| 证据 | 结论 | 位置 |
|---|---|---|
| 旧文件来源与 SHA256 | 使用真实已编译 SDK 1.1 / 插件样本 | [provenance.json](../tests/RizToolbox.Sdk.Tests/Fixtures/sdk-1.1.0/provenance.json) |
| 旧 DLL 与文本基线互相核验，当前 API 是超集 | 现有公开类型及签名保留；接口没有新增强制成员 | [public-api.txt](../tests/RizToolbox.Sdk.Tests/Fixtures/sdk-1.1.0/public-api.txt)、[Program.cs](../tests/RizToolbox.Sdk.Tests/Program.cs) |
| 旧插件在正式新 Host 执行 | 一个真实旧插件跨 SDK 1.1 → 1.2 的行为兼容 | 同一测试的 `OldPluginBehavior` |
| Future / expired 包同时发现 | 不兼容包被独立诊断，兼容包继续加载 | 同一测试的 `IncompatiblePackage` |

这份验证覆盖 SDK 1.1 到本次 1.2 的一个已编译插件样本及全部旧公共签名。它不证明任意第三方插件、原生接入包、任意未来版本或任意私有依赖都兼容，也不承诺 ALC 立即卸载或热更新。
