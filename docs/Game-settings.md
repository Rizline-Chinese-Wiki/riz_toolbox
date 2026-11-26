# 读取、观察和临时覆盖游戏音量

设置服务把已验证的游戏浮点属性交给统一 MutationCoordinator，采用优先级租约和 `AdoptAndReapply` 基线策略，并提供独立可选的变化订阅。**2026-10-27 同一 core-r1 快照的 Settings 43/43、SDK 13/13 已实际通过受控检查；没有安装或运行真实游戏，不构成 B16 实机验收。** 具体原件及七套专项范围见[功能接线结果](validation/foundation-function-wiring-20261027/README.md)，性能验收按用户安排后置。

后续 core-r2 的 Audio/content 844、协调器 28、receiver 10 项也已通过，覆盖本地 Retry 来源等功能；它不是再次运行 Settings 43 项或真实游戏设置验收。项目未发布，本页按当前 API 使用，不以向后兼容为前提。

目前只适配 `SettingsControl` 的四个音量属性；不提供任意字段、存档文件、判定偏移、播放速度或游戏语言写入。插件语言表服务与游戏语言设置是不同接口。

## 插件读取设置

插件使用 [SDK 契约](../src/RizToolbox.Sdk/GameSettingsContracts.cs)，在 `IRizPlugin.Enable(IPluginContext context)` 内取得服务：

```csharp
var lookup = context.GetService<IGameSettingsReadService>(GameSettingsCapabilityIds.Read);
if (!lookup.Available || lookup.Service is null)
    throw new InvalidOperationException(lookup.Error?.Message);

var values = lookup.Service.GetSettings();
if (!values.Success)
    throw new InvalidOperationException(values.Error?.Message);

foreach (var value in values.Value!)
    context.Log.Log(LogLevel.Info,
        $"{value.Descriptor.Id}={value.Value}; " +
        $"source={value.Descriptor.Source}; override={value.Descriptor.OverrideAvailable}");
```

读服务能力为 `riz.settings.read`。`Describe()` 包含已知但不支持的属性及原因；`GetSettings()` 返回受支持 getter 的只读快照；`Read(id)` 读取单个属性。界面没有打开、slider 元数据不支持或写入范围无效，都不阻止已验证的静态 getter。游戏单例尚未初始化时返回 `NotReady`，不返回假默认值。

| 属性 ID | 游戏 getter / setter | 范围来源 |
|---|---|---|
| `rizline.audio.master-volume` | `SettingsControl.MasterVolume` | `LevelListSettingsControl.masterVolumeSlider` |
| `rizline.audio.hitfx-volume` | `SettingsControl.HitFXVolume` | `LevelListSettingsControl.hitFXVolumeSlider` |
| `rizline.audio.uise-volume` | `SettingsControl.UISEVolume` | `LevelListSettingsControl.uiSEVolumeSlider` |
| `rizline.audio.music-volume` | `SettingsControl.MusicVolume` | `LevelListSettingsControl.musicVolumeSlider` |

值保留对应 getter/setter 的原始单位。国服 Steam build `25977441` 的这四种音量均为连续 `Single` 标量：`1` 表示 UI 的 `100%`；四个 slider 的回调分别除以 `100` 后调用对应 setter，显示时分别乘以 `100`。描述符将各自 slider 范围换算到 setter 单位，例如 slider 的 `0..100` 对应 `Minimum=0`、`Maximum=1`、`WholeNumbers=false`。此换算逐字段验证，不作为其他设置的通用规则。

整数 slider 的显示步距 `1%` 对应标量 `0.01`，但原生 setter 本身不取整；请求 `0.995` 合法，`99` 超出标量范围。插件可根据 `Unit` 显示百分比，传给 `Override`、`SetValue` 的仍是标量值。基线和恢复直接保留原始浮点值，不经 UI 的百分比取整。每次读取快照、描述符或请求写入均刷新可写范围；界面消失后不会继续报告旧的写入可用性。四个属性的一次 `GetSettings()` 共用一次范围探测。

## 订阅完整设置修订

能力为 `GameSettingsCapabilityIds.Changes`（`riz.settings.changes`），接口是独立的 `IGameSettingsChangeService`：读服务负责按需读取，变化服务负责带修订的采样和订阅，各自查询能力。项目尚未发布，合同允许随实现调整，本页示例以当前源码为准。可选使用时在 `Enable` 内查询，不可用则保留读能力或显示原因，不强行启用它：

```csharp
using RizToolbox.Sdk;

static Result<IDisposable> ObserveSettings(IPluginContext context)
{
    var lookup = context.GetService<IGameSettingsChangeService>(GameSettingsCapabilityIds.Changes);
    if (!lookup.Available || lookup.Service is null)
        return Result<IDisposable>.Fail(lookup.Error ?? new OperationError(
            ErrorCode.CapabilityUnavailable, "Settings observation unavailable.", GameSettingsCapabilityIds.Changes));

    return lookup.Service.Subscribe(change =>
    {
        var snapshot = change.Snapshot;
        context.Log.Log(LogLevel.Info,
            $"settings {snapshot.SessionId}/{snapshot.Revision}; initial={change.IsInitial}");
        foreach (var value in snapshot.Values)
            context.Log.Log(LogLevel.Info, $"{value.Descriptor.Id}={value.Value}");
    });
}
```

调用和回调都在经验证的主线程。订阅成功前同步交付一次 `IsInitial=true` 的完整当前快照，`ChangedIds` 为空；订阅建立期间不会先收到增量再收到旧 initial。回调中注册第二订阅时，第二订阅收到该当前修订一次，随后接后续变化。返回句柄自动归属启用作用域，主线程 Dispose 可提前停收。

后续通知的 Snapshot 仍包含完整只读 Values；`ChangedIds` 表示相较前一修订发生值/描述符变化的 ID，也可能含被移除 ID。`SessionId` 区分 provider 实例，Revision 在值或描述符改变时递增；相同值的再次采样不增加修订、不重发变化。描述符变化包含实际 slider 的写入可用性/范围变化，不只浮点值变化。`GetSnapshot()` 读取模块最近的完整观察，不额外访问 getter，也不是强制刷新命令。

模块按 **1 秒间隔采样**，时间是模块主线程观察时间；中间变化再恢复可能不被报告，不能当每次 setter、用户操作或持久化成功回调。捕获中任一 getter 失败不发布半份修订，当前读取返回 `NotReady`，旧数据不能冒充最新；恢复后根据最后完整修订比较。快照和变更列表均为复制的只读 DTO，可保留给后台处理，不含 native 句柄。

启用作用域结束、模块撤回或能力 provider generation 改变后，旧服务视图和订阅失效，即使随后重新注册同一个服务对象也不会复活。能力恢复后须在仍有效的作用域重新 GetService 并 Subscribe；新订阅从 initial 开始，不能把 provider generation 与 snapshot Revision 混用。回调异常通过真实 ScopedEvents 归属订阅插件，终止该故障订阅/作用域，不阻止其他插件继续收到通知。回调中递归 Refresh 不属于公开 API，也不能用于强迫发布下一修订。

## 用租约覆盖设置

写服务能力为 `riz.settings.override`，其准备状态独立于读服务。只有游戏实际设置页处于活动、已加载场景中，且 slider 给出有效范围时，才接受新租约或 `SetValue`。

```csharp
var lookup = context.GetService<IGameSettingsOverrideService>(GameSettingsCapabilityIds.Override);
if (!lookup.Available || lookup.Service is null)
    throw new InvalidOperationException(lookup.Error?.Message);

var requested = lookup.Service.Override(
    new GameSettingOverrideRequest("rizline.audio.music-volume", targetValue, Priority: 10));
if (!requested.Success || requested.Value is null)
    throw new InvalidOperationException(requested.Error?.Message);

IGameSettingLease lease = requested.Value;
// targetValue 必须来自先前读取的实际范围，或插件用户在该范围内的选择。
// 当前启用作用域自动拥有 lease；主动提前结束时也可调用 lease.Dispose()。
```

上述代码放在已有 `targetValue` 浮点变量的主线程方法中。接口拒绝 NaN、无穷值、范围外值和描述符声明 `WholeNumbers=true` 时的小数值；这四种连续音量不继承 UI 整数 slider 的小数限制。较高优先级胜出，同优先级按较晚登记胜出；释放高层后露出下一层，全部释放后恢复当前基线。

游戏用户在租约期间主动修改音量时，下一次刷新采纳该值为新基线，再应用获胜插件层。释放前若刚发生尚未刷新的用户改动，也会采纳该值；不会恢复到插件启用前的陈旧音量。模块默认每秒刷新，因此两次采样间出现又消失的用户变化可能无法观察。

界面关闭后，新写入和 `SetValue` 返回 `NotReady`；已经接受的候选值仍由租约管理，可继续刷新已知候选并正常释放。插件停用、启用失败回滚和模块释放会自动结束作用域租约。模块先在 backend 仍有效时恢复所拥有的值，再清理目标和 backend。进程异常退出不提供跨进程恢复事务。

游戏官方 setter 自身可能保存值；本服务不直接修改任何存档、配置或任意文件。

## 理解写入失败的边界

写入后必须读回确认。若 setter 已写入所请求的值后抛错，读回仍等于本次请求值时，可证明仍有写入归属，事务尝试恢复前一个值；成功后保留原有租约，新失败租约不会泄漏。

若读回变成既不等于请求值、也不等于写前值的未知值，例如 setter 产生未预期的截断或其他游戏逻辑同时改值，则保留未知值、使该属性通道失效并撤销所有持有者，返回 `TargetExpired`。此后描述符不再声称该属性可写；当前模块实例不会默默重建写入通道。其他属性和读取操作仍可继续。不能把这种失败说成“总能恢复原值”。

| 返回码 | 典型原因 |
|---|---|
| `OperationUnsupported` | getter/setter 或 slider 元数据不支持，或属性未知 |
| `UnsupportedSignature` | 实际 slider 范围为非有限值或最小值大于最大值 |
| `NotReady` | 游戏尚未初始化，或实际设置页暂未就绪 |
| `AmbiguousTarget` | 同时存在多个活动且已加载的设置页 |
| `QuotaExceeded` | 候选设置页超过 32 个，拒绝用截断结果选第一个 |
| `InvalidArgument` | 请求值与实际范围不符 |
| `InternalError` | 已确认归属并回滚成功的写入事务失败 |
| `TargetExpired` | 无法确认写入归属，通道已失效 |
| `ScopeExpired` | 缓存服务视图所属启用已结束，或模块已释放 |
| `WrongThread` | 调用不在宿主主线程，尚未触及 native/backend |
| `CapabilityUnavailable` | getter 或范围探测执行失败 |

## 宿主登记和数据快照接入

[SettingsModule](../src/RizToolbox.Adapter/Settings/SettingsModule.cs) 提供两个独立 `IAdapterModule`：

```csharp
var settingsRead = new SettingsModule(api, main, log);
var settingsOverride = new SettingsOverrideModule(settingsRead);
```

组合器需把两者加入模块列表。读模块 ID 为 `rizline.settings.read`，注册 `IGameSettingsReadService` / `riz.settings.read`；写模块 ID 为 `rizline.settings.override`，依赖读能力，注册 `IGameSettingsOverrideService` / `riz.settings.override`。两者通过 `RegisterScoped` 创建 `ReadForPlugin(context)` / `OverrideForPlugin(context)`，服务视图和租约都由 context 自动拥有。不要把共享 service 实例直接发布给插件。

读模块声明 `settings.control` 类型符号，位置为 `Assembly-CSharp.dll`、空命名空间、`SettingsControl`。backend 另外实际探测 `GameManager.Instance : GameManager`，仅在其非 null 后读取游戏设置。每个 getter 必须是静态 `Single()`、setter 必须是静态 `Void(Single)`，参数、返回值和静态性均由运行时元数据校验；缺少某个 getter/setter 会单独写入描述符原因。

写入范围还需实际探测 `LevelListSettingsControl` 的四个 `UnityEngine.UI.Slider` 字段，以及 Slider 的 `get_minValue : Single()`、`get_maxValue : Single()`、`get_wholeNumbers : Boolean()`。通过 `Resources.FindObjectsOfTypeAll(Type)` 枚举后，检查 Unity 对象存活、`isActiveAndEnabled`、所属 Scene 有效且已加载；不选模板、未加载场景或多个匹配中的第一个。这些 UI 条件不加入读操作的必需条件。

原生对象、GameObject、slider 和枚举数组在跨 native 调用期间持有根。唯一候选选择和 32 项上限由 [SettingsUiProbe](../src/RizToolbox.Adapter/Settings/SettingsUiProbe.cs) 统一执行；成功、缺失、歧义、上限和异常路径都释放候选根，数据 DTO 不含原生引用。

数据模块通过 `CaptureDataSettings()` 生成只读的 `GameSettingSnapshot` 列表。它按设置 ID 排序，直接从游戏 getter 读取值，按 invariant culture 格式化，并保留来源和单位。采集这些字段无需查找设置页面上的 Slider。任一 getter 失败时整次采集返回错误。

公开设置接口的描述符还包含 Slider 的范围。定时刷新在采集描述符时读取一次实际范围。有插件持有覆盖租约时，刷新还会先读取一次范围，再决定是否重新应用覆盖值。这使游戏收窄 Slider 范围后，旧租约无法写回已越界的值。

## 证据与复查

候选名字来自本机已有逆向输出 `绿豆诡计v2/output/il2cpp_output/dump.cs`：`SettingsControl` 约 92505 行、四个 getter/setter 约 92528–92537 行，`LevelListSettingsControl` 约 95152 行，四个 slider 字段约 95174–95180 行。dump 中的名字只用于候选定位，不能代替实际静态性、签名、存活和 UI 范围检查。

离线检查位于 [Settings.Tests](../tests/RizToolbox.Settings.Tests/Program.cs)，运行：

```powershell
dotnet run --project tests/RizToolbox.Settings.Tests/RizToolbox.Settings.Tests.csproj -c Release
dotnet run --project tests/RizToolbox.Sdk.Tests/RizToolbox.Sdk.Tests.csproj -c Release
```

2026-10-19 本轮结果：Settings **25/25**，SDK **13/13**，编译零警告/错误。覆盖签名策略、UI 缺失仍可读、非归一化范围、模块能力拆分、优先级和同优先级、用户新基线、临释放外部写、非法输入、坏范围和不支持邻居、未知读回、可确认归属回滚、启停作用域、模块释放顺序、未初始化、UI 消失、数据复制、主线程，以及成功/歧义/上限/异常/缺失的根与枚举器清理。原生调用尚未以本轮新包实机执行。

待主任务完成组合与安装后，还需记录真实菜单读取、真实 slider 范围、两独立插件层、用户改音量后的基线恢复、关闭设置页和模块撤销。只有这些实际证据完成后，才可判断可逆游戏设置的实机验收；本文不把未执行项记作通过。
