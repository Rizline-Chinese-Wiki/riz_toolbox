# 关闭泛型资源引用：可选只读运行时诊断

793后续：existing-owner入口已在原版ControllerOver正确持有的InsideLevelParams上运行，取得AssetRefReference<Sprite/AudioClip>的官方Release派发（RVA B115D0、interface MethodInfo slot1到实现slot5）。见[1335条完整会话](../runtime-resource-owner-20261021/README.md)。这些是AssetRefReference对象；专用ResourceReference<AudioClip>及其本地文件所有权仍未证明。下文最初“virtual dispatch unknown”属于787 metadata-only阶段，不覆盖793的新证据，也不能反过来把AssetRef映射当ResourceRef映射。

父任务后续动态结果：787 final包在真实游戏session `20261021T094900103Z-5d3bca7a402a4aec8cefcda43ebf42a7` 成功记录全部13行。冻结的[运行时证明](../runtime-canvas-catalog-20261021/runtime-proof.json)包含header/count、连续index和每行原始Values；Sprite闭类ctor/Release分别为RVA AEF2C0/AEF1F0，均已核declaring type及entry。AudioClip concrete与实际对象virtual dispatch仍unknown。本会话因用户停止GUI而仅有部分日志快照，不代表正常退出或资源最终释放。以下“子任务未执行”是原代理交付时的历史边界，不覆盖父任务这次实际采集。

2026-10-21，flavor=null。本阶段新增可执行的只读元数据诊断，专门补充前序[25个有界静态范围](../local-provider-catalog-20261021/README.md)留下的关闭泛型证据缺口。代码存在不等于动态验证完成；本子任务没有启动游戏、运行诊断、构建或安装包。B15 原生本地音乐、封面消费和完整 LOCAL 生命周期仍未据此验收。

范围与授权见 [scope.md](scope.md)。不构造 `ResourceReference`，不分配 Unity 资源，不调用构造器、`Release` 或其他游戏方法，不装 hook，不扫描进程对象，不读 Il2CppClass 的虚表或接口偏移表。已有 scratch buffer 与临时 GC handle 仅用于现有字段读取和保活机制，离开读取范围后释放。

## 如何采集

父任务在 `WorkshopChartModule.Probe` 的已知构建、Unity版本与磁盘 SHA256 核验之后接入一次显式 opt-in 调用。标记文件为：

```text
D:\SteamLibrary\steamapps\common\Rizline\BepInEx\RizToolbox\probes\local-resource-reference.enabled
```

只有存在此 marker 才执行 `CaptureMetadata`；默认不执行。日志先输出 `LocalResourceReferenceProbe metadata` 和 Stage/Build/CapturedUtc/count，再逐条输出 `LocalResourceReferenceProbe row`（带 index 和 evidence 的 JSON）。每条限制1800字符，以免宿主2048字符消息上限截断整份报告；超限明确报告采集失败，不能按缺行当完整成功。该入口只查元数据，不自动执行 `CaptureExistingOwner`，不搜索或创建 live AudioClip 引用。父任务已接线并在构建；本文不将接线代码当作已经采集的日志。

Adapter 内部手动调用上下文如下；`api/profile/gameAssemblyModule` 必须来自已完成构建核验、绑定当前游戏主线程的现有宿主，不能使用插件提供的原生指针：

```csharp
var diagnostics = new LocalResourceReferenceDiagnostics(api, profile);
var report = diagnostics.CaptureMetadata(gameAssemblyModule);
var json = System.Text.Json.JsonSerializer.Serialize(report);
```

后续仅在 Adapter 已正确持有原版创建的 `NativeRuntime.RootHandle` 时，可调用：

```csharp
var report = diagnostics.CaptureExistingOwner(
    gameAssemblyModule, existingOwnerRoot, "InsideLevelParams");
```

允许的 exact owner 只有 `InsideLevelParams` 和 `LevelOverControl`。所有对象必须早已存在；不能为获取证据构造 owner 或 wrapper。`RootHandle` 的所有权仍属于调用者，诊断只临时保活字段返回的引用。

## 输出能证明什么

| evidence id | 路径 | 采集成功后的含义 | 尚不能推出 |
|---|---|---|---|
| `inside.cover` / `inside.music` | exact field → `il2cpp_field_get_type` → `il2cpp_class_from_type` → class type round trip | 分别得到精确的闭 `IResourceReference<UnityEngine.Sprite/AudioClip>` 类型、Il2CppType 和 Il2CppClass | concrete `ResourceReference<AudioClip>` 已存在 |
| `inside.ctor` | 精确5参数签名、declaring class、返回类型核验 | `InsideLevelParams` 的原生 MethodInfo 和签名 | 已调用、适合自定义 metadata 的重试/结果路径 |
| `sprite.concrete.field` | `WorkshopLevelCardControl` → `NestedClass(<StartPlayLevel>d__41)` → `<coverReference>5__2` | 字段实际引用的闭 `ResourceReference<UnityEngine.Sprite>` 类 | 已创建 Sprite / 引用或音乐闭 concrete 类 |
| `*.ctor/get/release` | 上述 exact closed class 枚举方法与参数返回类型 | 真实声明类、MethodInfo、签名、generic/inflated 标记 | 因方法名相同就可使用共享占位地址 |
| `*.dispatch` | 既有 owner 的 exact reference field → rooted existing object → closed interface Release → 官方 `il2cpp_object_get_virtual_method` | 该已有对象的接口 MethodInfo 到实现 MethodInfo 的实际解析结果 | 所有退出路径、所有对象均使用相同实现 |

每个方法独立报告 `EntryStatus`。只有先 `VerifyReadable` 验证完整既有 `MethodInfo2022` 结构可读、再比对导出元数据的 name/class/return/flags/implementation flags/parameter count 六项，并通过 `VerifyExecutable` 证明 entry 位于所给 GameAssembly 的可执行 image 区域，才输出 entry、RVA、`MethodInfoSlot`。这里复用项目已存在的固定 Unity 2022 布局，没有新猜测偏移。`MethodInfoSlot` 是该 MethodInfo 的布局字段，不表示遍历或证明了 Il2CppClass 虚表/接口偏移表。

入口地址检查没有把静态候选 `0xAEF1F0` / `0xAEF2C0` 作为验证输入；候选地址不能反过来证明方法身份。若 `existing.*.resolved.release` 的实际 RVA 恰好等于候选、其 exact concrete declaring type 已证明且 `EntryStatus` 为 Verified，才可将该对象的解析结果与静态候选联系起来。只取得 metadata-only 的 ctor/Release 地址，还不能证明 `LevelOver.OnDestroy` 的 slot1 在该对象上完成了同一派发。

## 保守失败与缺口

- exact类型名称、参数、返回类型、declaring class、字段静态性或引用布局变化时，相关 evidence 为 `failed`；没有兼容别名或泛型猜测回退。
- 空字段、缺少 optional virtual resolver 导出或缺少 live exact concrete class 时，结果明确为 `unknown`。解析结果仍是 interface 本身、是 abstract/static 或类型不匹配时拒绝作为实现证据。
- metadata-only 默认报告 `audio.concrete=unknown` 和 `interface.dispatch=unknown`。没有 live AudioClip wrapper 就不宣称 closed `ResourceReference<AudioClip>`、ctor、Release 和接口映射已确认。
- 未检查缓存移出并发顺序、资源释放全路径、provider撤回、取消、重试和结果 UI；未分配本地音乐或封面，未开始 LOCAL 关卡。B15 原生资源消费仍待后续实现和实测。

## Evidence → Finding → Path

| E-id | source_ref | repro_command | content_hash |
|---|---|---|---|
| E1 | [Runtime只读接口](../../../src/RizToolbox.Runtime/NativeRuntime.MetadataDiagnostics.cs) | `Get-FileHash -Algorithm SHA256 src/RizToolbox.Runtime/NativeRuntime.MetadataDiagnostics.cs`，仓库根目录执行 | `1964AD882255A780739D618D226B5229E4C65BBEB296F699119E4684076D4B8C` |
| E2 | [Adapter opt-in报告器](../../../src/RizToolbox.Adapter/AudioContent/LocalResourceReferenceDiagnostics.cs) | `Get-FileHash -Algorithm SHA256 src/RizToolbox.Adapter/AudioContent/LocalResourceReferenceDiagnostics.cs`，仓库根目录执行 | `5D3D5AE7B6366E603E81F9D0AFEF73344D0EF57BC9A75111B80BC1EF8A01DD2B` |
| E3 | [前序静态证据](../local-provider-catalog-20261021/README.md)及其metadata-excerpts | 前序 README 的 inspect-catalog.py 命令；本阶段未重复运行25范围 | 已记录在前序 boundaries.json |
| E4 | 运行时 JSON | 创建上述 marker，使用父任务验证构建进入原版游戏后读取 `LocalResourceReferenceProbe metadata` 日志；本子任务未执行 | n/a：尚无运行时产物 |

F1：severity=n/a_re，evidence_ids=E1/E2，confidence=high，location=`InspectMetadataFieldClass`/`CaptureMetadata`，status=implemented-unexecuted。已实现不依赖字符串拼类或私有反射的 exact field → closedclass 诊断。

F2：severity=n/a_re，evidence_ids=E1/E2/E3，confidence=high，location=`InspectExistingReference`，status=implemented-unexecuted。已实现官方只读 virtual resolver 与保守签名检查；尚无 E4，不能宣称实际派发映射成立。

P1，path_type=callflow，E1/E2→F1/F2：已验证宿主主线程及构建 → opt-in metadata采集 → exact字段闭类型和MethodInfo → 若未来已持有原版owner则读取其既有引用 → 官方resolver → 核验MethodInfo与entry → 报告事实或缺口。最后两步没有现成owner时停在unknown，不创建替代对象。

时间线：先阅读前序25范围和dump字段；获父任务批准新增Runtime partial与optional resolver；等待父任务UI red构建结束后落盘两文件；仅进行源码范围和格式检查；构建、安装和动态采集交由父任务串行调度。图表、IOC和ATT&CK为n/a：本任务是有界元数据证据补充，不涉及攻击路径。
