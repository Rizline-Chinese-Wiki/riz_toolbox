# 精确方法观察 Hook

B07 的新实现挂接经过验证的实际方法入口，SDK 提供 before/after 只读普通数据。`runtime_invoke` 启动桥负责宿主 tick，它不等同于这里的精确方法 Hook。

当前只适配已知 GameAssembly SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`、Unity `2022.3.62f2`、Windows x64。未知构建、签名、布局或代码地址均拒绝，不扫描相似地址兜底。

## 宿主接入

以下位于 `RizlineAdapter` 的模块组合处，使用其现有 api/main/log/profile 对象：

```csharp
using RizToolbox.Adapter.Modules;
using RizToolbox.Core.Hooks;

var hooks = new HookDispatcher(main, log);
// 宿主将 hooks.ProviderFaulted 连接到 PluginHost.ReportPluginFault(id, error)。
modules.Add(new ExactGameplayHookModule(api, main, log, profile, hooks,
    Path.Combine(gameRoot, "BepInEx", "core", "dobby.dll")));
```

`ExactGameplayHookModule` 实现现有 `IAdapterModule`，负责符号校验、安装及 `RegisterScoped<IHookService>`；能力 ID 为 `HookCapabilityIds.Observe` (`riz.hooks.observe`)。ModuleManager 先撤回能力并结束插件作用域，再移除逻辑绑定。HookDispatcher 由宿主结束时释放。

原生补丁、replacement delegate、trampoline 和 Dobby 库都按进程保留。停用插件和释放模块只移除逻辑订阅；不能在仍有其他 native 线程可能进入时卸载代码或释放跳板。重新激活同地址复用同一补丁。

## 支持的实际目标

| SDK 点 | 游戏方法 | 当前构建 RVA | ABI |
|---|---|---|---|
| `GameHookPoints.Judge` | `NormalModeInLevelController.GetJudge(InLevelController.JudgeType, System.Single)` | `0x3C0310` | `void(instance, int enum32, float32, MethodInfo*)` |
| `GameHookPoints.Pause` | `NormalModeInLevelController.Pause()` | `0x3A2410` | `void(instance, MethodInfo*)` |
| `GameHookPoints.Resume` | `NormalModeInLevelController.Resume()` | `0x3A2440` | `void(instance, MethodInfo*)` |

Pause/Resume 与其他控制器可能共享机器码入口；receiver 在主线程经 `il2cpp_class_is_assignable_from` 确认属于 NormalMode 后才分发逻辑观察。其他 receiver 仍调用其原函数，不向 NormalMode 插件报告。

只接受非泛型、非 static、void 返回的指定签名。枚举必须是有符号 Int32 的四字节存储，第二个参数必须为四字节 Single，ref/out 或不同返回值拒绝。

## 地址与 MethodInfo 校验

适配器先从 IL2CPP 导出元数据 API 解析唯一方法、参数类型、返回类型和 static 标记，再读取明确声明的 Unity2022 MethodInfo 布局。该布局来源是 [BepInEx/Il2CppInterop 官方 MethodInfo_29_1 实现](https://github.com/BepInEx/Il2CppInterop/blob/master/Il2CppInterop.Runtime/Runtime/VersionSpecific/MethodInfo/MethodInfo_29_1.cs)，其适用起点为 Unity2022.1；2023.2 的另一个处理器不纳入本配置。

读取前调用 VirtualQuery，要求已提交、可访问且未跨区域。读取后将 name、class、return_type、flags、parameter_count 与导出 API 逐项比较；methodPointer 必须严格等于当前 GameAssembly 模块基址加上述已验证 RVA。入口还须为该模块的可执行 MEM_IMAGE 页面。任一不符都返回 `UnsupportedSignature`，不会尝试相邻偏移。

本机 `dump.cs` 由 `inproc_dump.py` 使用真实 `il2cpp_type_get_name` 和 MethodInfo 首字段生成，记录了以上目标、原始类型拼写和 RVA。`script.json` 的 ABI Signature 缺少可靠的 this，未用于构造委托。IDA 技能启动脚本本次仍报告缺少 idalib-mcp，因此没有声称重新完成 IDA 反编译。

## 插件订阅

```csharp
// 以下代码位于 IRizPlugin.Enable(IPluginContext context)。
var lookup = context.GetService<IHookService>(HookCapabilityIds.Observe);
if (!lookup.Available) throw new InvalidOperationException(lookup.Error?.Message);

var result = lookup.Service!.Subscribe(GameHookPoints.Judge, HookPhase.After, judge =>
    context.Log.Log(LogLevel.Debug, $"judge={judge.JudgeType}; delta={judge.DeltaTime}; invocation={judge.Invocation}"),
    priority: 10);
if (!result.Success) throw new InvalidOperationException(result.Error?.Message);
// result.Value 自动 Own，插件停用即取消。
```

JudgeType 是游戏枚举的原始整数，DeltaTime 保留游戏原始 float，未推定其单位或正负语义。Pause/Resume 的 `ControllerHookObservation` 只有 Invocation 序号。Invocation 是机器入口本次调用序号，不是 RunId，也不暴露实例、MethodInfo 或 native 地址。

同一阶段按优先级降序、同优先级登记顺序分发。插件异常归属该插件并移除其订阅，其他插件仍可执行。宿主通过 `ProviderFaulted` 接入原有拓扑停用。不同版本、payload 或阶段被拒绝，`RequestMutation` 始终返回 `MutationNotSupported`；首版不允许更改参数、替换返回值或跳过原调用。

## 原调用、线程与重入

每个 native entry 都直接调用保存的 trampoline 恰好一次；原调用位于所有托管 catch 之外，观察者异常不能跳过、吞掉或重复它。finally 只负责清理重入状态；若原函数异常离开，不发布正常 After，也不重试原函数。

同线程同地址重入的原函数仍执行一次，嵌套逻辑 before/after 被抑制，避免插件递归观察；不同地址不继承这一抑制。off-main 原生调用同样执行原函数一次，但不会进行 receiver 类型读取、Unity 字段访问、root 创建或插件回调，只增加安全丢弃计数。

`GetDiagnostics()` 返回 NativeCalls、OriginalCalls、BeforeDeliveries、AfterDeliveries、SuppressedReentries、SuppressedWrongThread、Subscribers。NativeCalls 与 OriginalCalls 是入口/原调用尝试计数；BeforeDeliveries/AfterDeliveries 是该逻辑点经过 dispatcher 的次数，不是所有订阅者回调次数之和。NativeInstalled 表示当前模块逻辑绑定可用，不代表已经观察过正常游玩调用。

## 可复现检查与未验收范围

```powershell
dotnet run --project tests/RizToolbox.Hooks.Tests/RizToolbox.Hooks.Tests.csproj -c Release
```

当前 13 项检查执行生产 Broker 的 typed replacement/trampoline 委托、Core Dispatcher 和正式 PluginHost 路径，覆盖单地址补丁去重、精确参数传递、原函数计数、观察异常、原函数异常、嵌套调用、off-main 零元数据访问、共享 receiver 过滤、失败重试、ABI 冲突、GC 生命周期、MethodInfo 不符及不可执行页面拒绝、作用域清理和插件故障归属。补丁引擎使用受控 seam，没有把这些委托测试称为 Dobby 在游戏内的安装证据。

证据链：本机运行时 dump 给出签名/RVA，官方布局给出字段顺序，生产验证器要求两者与当前导出元数据一致；自动检查证明分发与拒绝语义。下一步由宿主安装本次构建，并通过实际游玩产生 GetJudge/Pause/Resume 入口计数、双插件回调及停用恢复日志，才可将对应方法标记为真实运行已验收。旧安装包的其他功能日志不属于本次 Hook 证据。
