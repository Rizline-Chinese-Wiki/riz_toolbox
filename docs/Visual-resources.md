# 游戏 UI 视觉资源

2026-10-29 新增宿主持有的语义覆盖意图 `BindOverride`。插件可以保留同一 scope 的资源，在旧对象失效后等待唯一匹配的新对象；每次仍取得独立的新代次覆盖租约。状态为 `Waiting / Bound / Ambiguous / Stopped`，不会按同名选择第一个对象。此功能的生产服务 fixture 验证与真实游戏可见效果分开，当前仍未运行本候选的真实游戏重建、双包或 GPU 释放验收。

2026-10-21 源码已增加独立的 `TMP_Text.color` 目标，使用现有 `riz.targets` / `riz.mutations` 和游戏颜色服务；GameResources25/25通过，见[TMP颜色验证](validation/tmp-color-20261021/README.md)。颜色字段与字体共享材质分别保留基线；这次接入不代表下面的 TMP 材质 Color 或真实渲染恢复已验收。查询 `Role=tmp-text-color`、`ResourceId=unity.tmp-text.color` 可取得候选；出现多个时须明确选定规范键/代次，不能取同名第一个。

B13目前提供Sprite、Font和Material三个实验性模块。最新644检查包（Visual55）加入独立assignment pins及进程级retirement：恢复失败不先销毁仍附着的克隆，保留原baseline、目标根、依赖和预算；外部游戏写不被覆盖，无法确认的原生存活状态不等于死亡。预留槽位全进程最多512，Host每tick最多4次延后重试，失败退避到30秒，未决资源不驱逐；首次同步清理另计，尚无整帧时延预算验收。模块撤回使公共租约立即失效，旧原生赋值未解除时同类新provider等待。

新包实机重测两次Translucent Float创建/赋值和manual/scene-change租约释放，并观察到Unity延迟销毁保留根/预算的路径，见[独立会话](validation/runtime-input-capture-fix-20261020/README.md)。该包没有最终物理释放receipt，探针计数不代表GPU或全部原生资源。前一607包的[488条证据](validation/runtime-material-slots-20261020/README.md)保留；TMP Color、完整基线恢复、像素/GPU和逐类双插件矩阵仍缺，不验收整个B13。

| 能力 ID | 服务 | 已实现的入口 | 明确边界 |
|---|---|---|---|
| `riz.visual.sprites` | `IVisualResourceService` | 插件 PNG → Texture2D → Sprite，覆盖 Image.sprite | 全图矩形、居中 pivot；不提供图集裁切、RawImage.texture、音符/轨道语义目标 |
| `riz.visual.fonts` | 同上 | 借用 `Arial.ttf` 或 `LegacyRuntime.ttf`，覆盖 UI.Text.font | 名称受限，游戏中不存在时加载失败；不加载 TTF/OTF 文件或 TMP 字体 |
| `riz.visual.materials` | 同上 | 克隆目标Material；已知profile允许Color/Float；按接收者选择Graphic或TMP fontShared槽 | 精确文件hash/Shader身份及类型校验；Translucent Float已有部分实机，TMP Color/完整渲染恢复待验 |

普通插件仅需引用 SDK。所有目标和资产句柄均为进程内不透明 ID，不含原生地址；旧目标句柄不会跟随场景重建。模块声明不代表当前能力可用，插件须检查服务查询结果。

## 从插件包应用一张 PNG

以下函数在插件启用作用域的游戏主线程调用。`query` 来自 `GetTargets()` 展示出的规范目标或别名；`relativePng` 是包内文件的相对路径。所有返回租约已自动归属插件作用域，插件也可以提前 Dispose。

```csharp
using RizToolbox.Sdk;

static OperationResult<IMutationLease<VisualAsset>> ApplySprite(
    IPluginContext context, TargetQuery query, string relativePng)
{
    var lookup = context.GetService<IVisualResourceService>(VisualCapabilityIds.Sprites);
    if (!lookup.Available || lookup.Service is null)
        return OperationResult<IMutationLease<VisualAsset>>.Fail(lookup.Error ?? new OperationError(
            ErrorCode.CapabilityUnavailable, "Sprite service unavailable.", VisualCapabilityIds.Sprites));

    var service = lookup.Service;
    var match = service.Resolve(query);
    if (match.Kind != TargetMatchKind.Unique || match.Target is null)
        return OperationResult<IMutationLease<VisualAsset>>.Fail(match.Error ?? new OperationError(
            ErrorCode.TargetMissing, "No unique sprite target is available.", VisualCapabilityIds.Sprites, query.Key));

    var loaded = service.LoadPng(new(VisualFileArea.Package, relativePng));
    if (!loaded.Success)
        return OperationResult<IMutationLease<VisualAsset>>.Fail(loaded.Error!);
    using var texture = loaded.Value!;

    var created = service.CreateSprite(texture.Asset);
    if (!created.Success)
        return OperationResult<IMutationLease<VisualAsset>>.Fail(created.Error!);
    using var sprite = created.Value!;

    // 覆盖租约继续持有 Sprite 及其 Texture2D，函数结束不会销毁正在使用的资源。
    return service.Override(match.Target, sprite.Asset, priority: 10);
}
```

`VisualFileArea.PluginData` 使用宿主为实际插件 ID 登记的数据目录。适配器的 `VisualModule` 构造器接收包目录与数据目录两个 `Func<string,string?>` resolver；返回 null 的目录不可读取。各视觉模块应共享一个 `VisualResourceBudget`，以落实跨模块总配额。

来源只接受已登记根目录内的普通 `.png` 文件。绝对路径、盘符/ADS、空路径分段、`.`、`..`、符号链接和 junction 均被拒绝；Windows 还在打开文件后检查实际句柄路径，防止目录链接切换到授权根以外。不存在或不能读取的文件不会触碰游戏目标。

## 格式、预算和线程

PNG 仅接受 8-bit RGB/RGBA、非交错、标准压缩/滤波方式。验证 chunk CRC、IHDR/IDAT/IEND 顺序、像素长度和文件结束；拒绝索引色、灰度、16-bit、tRNS、未知关键 chunk、分离的 IDAT 和尾随数据。辅助色彩配置不作颜色空间变换。AssetBundle、JPEG、DDS 和其他格式未接入。

| 限制 | 当前值 |
|---|---|
| 编码文件 | 33 bytes 至 16 MiB |
| 宽、高 | 各不超过 2048 |
| 像素数 | 不超过 4,194,304 |
| 解码后的 Rgba 数组 | 不超过 64 MiB；另有受限编码、行缓冲和上传临时数组 |
| 每插件 | 32 个资产预约，估算资源内存 32 MiB |
| 共享视觉预算 | 128 个资产预约，估算资源内存 96 MiB |
| 资源估算 | Texture2D 为宽×高×4；Sprite 为 1 KiB；Material clone 为 64 KiB；借用字体为 0 bytes 但占资产数 |
| 每类目标 | 最多保留 128 个；每次完整发现最多扫描 4096 个对象 |

资源预算是受控资产估算，不能解释为 Unity 进程实际内存或字体字形缓存上限。解码临时内存另受固定上限约束。旧 `PngDecoder.Decode(byte[])` 图标入口仍维持 64×64 和原调用签名。

当前公共加载接口是同步主线程接口；文件读取和 CPU 解码也在该调用中完成，没有异步后台加载声明。后台线程调用在进入生产服务或活跃资产探测前被拒绝。插件可用 `context.MainThread.InvokeAsync` 切回主线程；Unity 对象创建、赋值、销毁和 root 释放始终留在主线程。

## 目标发现及租约

生产后端从已加载的Unity Image、Text或Graphic发现对象，要求对象存活、激活且属于有效并加载场景，排除根名称以 `RizToolbox.` 开头的工具Canvas。资源模板不作为游戏实例。材质按接收者类型确定语义槽，不能把TMP的Graphic.material当字体材质。

| 接收者与槽ID | ResourceId | 读写入口及边界 |
|---|---|---|
| 普通Graphic，`graphic` | `unity.ui.material` | `get_material` / `set_material` |
| 精确TranslucentImage接收者，`graphic` | `unity.ui.translucent-image.material` | 同一Graphic入口，来源语义独立；607实机成功目标为Canvas/BG |
| TMP_Text及派生接收者，`tmp-font-shared` | `unity.tmp.font-shared-material` | 元数据验证的`get_fontSharedMaterial` / `set_fontSharedMaterial`；访问器缺失或当前共享材质不存在则不发布，不回退到Graphic槽 |

目标携带实际场景、角色与资源类型。材质规范键包含 `/slot/<slotId>/binding/<id>`，使同一组件不同属性不会共用覆盖通道；仍须按对象代次重新解析。此接入没有宣称覆盖所有TMP缓存、实例化fontMaterial或字体资产本身。

别名形如 `game.visual/sprite/<instanceId>` 和 `game.visual/sprite/name/<gameObjectName>`；font、material 使用相应角色。规范目标在实例别名后增加随机 binding ID，同一绑定仅有一条对应属性的 B08 覆盖通道。同名对象查询返回 `AmbiguousTarget`，缺失返回 `TargetMissing`；别名不创建独立覆盖栈。

模块默认每秒检查已有对象和覆盖值，每五次Tick做一轮有界发现；状态是采样观察。每轮在新持根的原生数组中直接读取最多4096项，整数游标跨轮推进，未在当前窗口出现不表示原对象销毁。原生数组上限50000；稳定数组顺序下最终覆盖尾部，Unity任意重排仍不提供逐对象严格公平保证。

最多保持128个绑定。容量满时仍更新本轮所有已发现旧绑定的别名和元数据；后出现的Translucent/TMP可替换无覆盖的低优先级绑定，同优先级按最后准入顺序轮换。每轮最多替换一个，保留正在覆盖、原生写入中、框架资源基线或赋值尚未确认的目标，cleanup pending时停止新增。候选优先级和50000条有界历史不承诺大列表的即时可见：每五次Tick最多一换，大列表可能等待较久；所有目标被持有时明确记录延迟。淘汰结束旧句柄代次，调用方须重新Resolve。实际服务红绿、5000身份边界和独立8192对象算法模型见[本轮证据](validation/visual-admission-20261021/service-admission-checkpoint.md)；新包仍需真实动态场景验证。

对象销毁或场景卸载使旧代次失效，不再调用setter。活着但停用的目标先恢复仍属于通道的资源，再释放目标root。一次性 `Override` 仍需作者重新 Resolve；`BindOverride` 可在既有发现边界续接符合已锁定语义的新代次。607实测第一次沿用旧选择被TARGET MISSING拒绝，刷新后取得当前Canvas/BG槽才创建成功；该历史会话没有运行新的持续意图。

每个绑定保留一个组件 root，每个已观察到的资源身份最多保留一个长期资产 root。原始游戏资源和内置字体是借用资源，永不由本服务 Destroy；框架创建的 Texture2D、Sprite 和 Material clone 在最后一个资产租约、覆盖引用、基线引用及依赖引用结束后销毁。Sprite 还会持有其 Texture2D。模块撤回会先结束通道和目标，再释放所有资产；之后重复 Dispose 安全。

覆盖采用 `PriorityOverride`：高 priority 生效，同优先级后注册者生效。只接受同一插件尚未结束的资产租约；其他插件不能使用猜到或转交的资产 token。覆盖租约不接受 SetValue，替换时获取新租约，再结束旧租约。没有租约的资源不可重新使用。

通道沿用 `AdoptAndReapply`：游戏写入新的 Sprite/Font/Material 时，将它作为基线并重申当前胜出层；最后一个覆盖结束时恢复最新基线。包括框架资源在内的基线在恢复完成前一直持有。赋值后读回确认；setter 写后抛错，仅在读回仍属于本次候选时回滚，不覆盖第三方已写入的资源。

加载或上传失败返回 `ResourceLoadFailed`，文字为“资源加载失败，原资源保留”；候选尚未覆盖时不会改变正在显示的资源。创建或 setter 内发生作用域撤销会丢弃返回值并释放其租约；模块撤回后才返回的未挂接创建结果会被销毁。Mutation 重入由现有仲裁器拒绝，Destroy 重入不会重复销毁资产。

## 在对象重建后继续覆盖

在当前主线程和当前插件 scope 中调用 `BindOverride(query, assetLease, priority)`。`assetLease` 必须是同一 Visual provider、同一实际 scope 返回的原始租约；仅相同 PluginId 的父/子/兄弟 scope 不能互相借用。包装对象或其他 provider 的租约会在读取其 getter 前返回 `InvalidArgument`。不同服务视图属于同一个实际 scope 时可以复用该租约。

下面的 helper 在包内 PNG 上传后保留 Sprite 租约给当前 scope。Texture2D 的作者租约可以结束，Sprite 的依赖引用仍保留它。`query` 应使用稳定别名及已知场景、内容、角色或资源字段；不要把一次性规范 binding 键、原生地址或实例 ID 当作可重建语义。

```csharp
using RizToolbox.Sdk;

static OperationResult<IVisualBindingIntent> KeepSprite(
    IPluginContext context, TargetQuery query, string relativePng)
{
    var lookup = context.GetService<IVisualResourceService>(VisualCapabilityIds.Sprites);
    if (!lookup.Available || lookup.Service is null)
        return OperationResult<IVisualBindingIntent>.Fail(lookup.Error ?? new OperationError(
            ErrorCode.CapabilityUnavailable, "Sprite service unavailable.", VisualCapabilityIds.Sprites));

    var service = lookup.Service;
    var loaded = service.LoadPng(new(VisualFileArea.Package, relativePng));
    if (!loaded.Success)
        return OperationResult<IVisualBindingIntent>.Fail(loaded.Error!);
    using var texture = loaded.Value!;

    var created = service.CreateSprite(texture.Asset);
    if (!created.Success)
        return OperationResult<IVisualBindingIntent>.Fail(created.Error!);
    var sprite = created.Value!; // 由此 context 的 scope 持有，不能在函数末尾 Dispose。
    var binding = service.BindOverride(query, sprite, priority: 10);
    if (!binding.Success)
        sprite.Dispose();
    return binding;
}
```

`BindOverride` 成功表示意图已被 scope 接管，不保证此刻有对象。`intent.GetSnapshot()` 返回当前状态；它只读已发布状态，不扫描游戏、不申请新租约。`intent.LastSnapshot` 是不可变托管记录，可在 worker、scope 结束或 provider 撤回后读取，用于保留最后状态与停止原因。

| 状态 | 含义及结构化错误 | 目标/资源行为 |
|---|---|---|
| `Waiting` | 零匹配为 `TargetMissing`；新层获取失败时保留具体仲裁错误 | 当前 Target 为 null，保留作者的资源租约，后续既有发现边界可重试 |
| `Bound` | 已取得普通 priority 层；Error 为 null | Target 含当前规范键及非复用 Generation；高优先级插件仍可能胜出 |
| `Ambiguous` | 已锁定语义内有多个候选，`AmbiguousTarget` | 先结束旧层，所有候选保持各自基线；不选择第一个 |
| `Stopped` | 显式停止为 `Cancelled`；scope/provider 结束为 `ScopeExpired`；输入租约/资产失效为 `TargetExpired`；清理不确定为 `CapabilityUnavailable` | 永久停止，不因后续 provider 恢复或同名对象出现而重新启动 |

`Revision` 仅在发布状态内容发生变化时递增，不随空 tick 增加。`Target` 只在 Bound 时有值；`Identity` 记录首次唯一匹配的 Scene、ContentId、Role、ResourceId，包含 null。以后即使 query 省略某字段，也只接续与该锁定值完全相同的候选，不能自动跨其他 scene/content/role/resource。需要切换场景或内容时，作者应结束旧意图并在明确的新 scope/查询中创建新意图。

输入查询必须包含 Key、Role、ContentId、ResourceId 中至少一个非空字段，并保持 `ActiveOnly=true`、`IncludeTemplates=false`。首次匹配前仍按整个查询计算唯一性；模板和不活跃实例从不参与。首次唯一之前没有锁定身份，零候选和多候选仍可持有同一个等待意图。

初次创建只使用当前 SemanticTargetRegistry。之后每五次模块 Tick 的既有有界发现完成后才重新选择匹配并申请新层，不扩大扫描窗口、不每帧全量扫描；普通 Tick 只做已有目标/资源与当前层的生命周期核对。符号、后端构造及发现已成功的 Visual provider 在当前零目标时继续可用，让插件能够提前创建 Waiting 意图；发现异常、能力撤回、cleanup pending/quarantine 仍按原模块路径撤回。

对象代次失效先结束旧层，新层仍走同一个 `MutationCoordinator`，与一次性 `Override` 和其他插件共享优先级及最新外部基线。旧层恢复失败、目标赋值不确定或清理 quarantine 会停止意图，不能在旧原生赋值尚未隔离时偷偷取得另一个对象。provider 在原生 setter 栈内撤回时，意图先停止，挂接引用/依赖/预算由原 retirement 保留；物理恢复和释放等写栈退出后确认。

`Stop()` 或 `Dispose()` 只结束意图及当前覆盖层，输入资产租约仍归作者/scope。作者显式结束输入租约时，该租约上的全部意图立即停止；Sprite 依赖和目标挂接引用按原资源预算及 retirement 规则释放。provider 最多保持 128 个意图，每个 PluginId 在该 provider 最多 32 个，子 scope 计入同一插件额度。停止会返还意图槽位，不另占一份资产预算。

所有结果入口先判断主线程、scope 和 provider，失效调用不会探测文件、资源或目标。`GetSnapshot` / `Stop` 在 worker 返回 `WrongThread`，scope/provider 结束后返回 `ScopeExpired`；最后的 terminal 状态仍从 `LastSnapshot` 读取。意图没有订阅回调，避免在获取或释放一半的原生层中向插件发布事件。

## 使用有来源证明的 Material 参数

`VisualMaterialRequest(Source)` 的空参数列表仍可完整克隆当前材质。有参数时，先检查固定构建和关联 Unity assets 的 SHA-256，再通过确切的 `Resources.Load(String,Type)` 引用取得 Shader，要求源材质的实际 Shader 对象相等。随后只在克隆上设置并读回参数；共享源材质不被修改。文件不符、资源缺失、Shader 身份不同、未知属性/类型或超出范围均拒绝。

| 确切参考资源 | 已声明参数 | 边界 |
|---|---|---|
| Material `fonts & materials/liberationsans sdf - outline` | Color `_FaceColor`、`_OutlineColor`；Float `_OutlineWidth`、`_FaceDilate` | Color 各通道 `[0,1]` 为适配器策略；两个 Float 的序列化 Range 分别 `[0,1]`、`[-1,1]` |
| Shader `translucentimage` | Float `_Vibrancy`、`_Brightness`、`_Flatten` | 序列化类型为 Float，无声明 Range；只接受有限值 |

属性来源、文件hash、资源引用链与源码边界见[材质来源证据](validation/visual-20261020/README.md)。最多16个独特参数，不能混用profile；Shader名称和HasProperty不充当类型证明。Texture参数、关键字、相机和后处理尚未开放。TranslucentImage可能在Update重写值；创建/赋值成功不能代替可见效果与持久性。文件hash在主线程同步计算，频繁创建成本仍待完整测量。

603旧包把普通Canvas/DiscTabItem材质用于TMP/Translucent请求时，精确shader不匹配被拒绝，见[拒绝证据](validation/runtime-back-skip-20261020/material-refusal.md)。607独立选择真实Translucent槽后，以`_Brightness=0.7`、`_Vibrancy=1.1`、`_Flatten=0.1`完成两次成功请求。成功意味着生产后端每项SetFloat/GetFloat容差检查和覆盖通道赋值读回被接受；冻结日志没有公开原始getter值。请求的colors为空，探针通用文字“GetColor/GetFloat”不能证明Color已测。

第一次manual和第二次scene-change（LevelList→FakeMainMenu）均记录覆盖Dispose后inactive、同一资产租约Dispose后`managedLeaseValidAfterDispose=false`，随后保留clone/override/估算bytes为0。引用移除后resource-count的assetValid为null。SDK没有公开基线恢复receipt；这些计数不是GPU或全进程原生资源统计，零Error不代表恢复及渲染完整验收。

原生调用只选择 metadata 可证明的签名。特别是本构建 Sprite.Create 使用 `(Rect, Vector2, Single, Texture2D)`，不按四个参数猜选另一个重载；Texture2D.Reinitialize 明确选择 TextureFormat 重载，上传明确选择 `(IntPtr, Int32)`。Rect 由 Runtime 的字段/偏移/大小白名单验证，不能据此扩展任意结构体封送。SDK 不暴露这些原生缓冲。

## 离线证据和剩余验收

执行时避免其他测试项目同时构建共享工程：

```powershell
dotnet run --project tests/RizToolbox.Visual.Tests/RizToolbox.Visual.Tests.csproj -c Release
```

2026-10-20最新607完整构建中Visual **47/47**通过。它们使用生产 `VisualResourceService` 的 `IVisualBackend` 窄seam覆盖三插件同栈、歧义、失败保留、预算、Sprite依赖、游戏基线、销毁/重建、停用恢复、模块撤回、后台拒绝、scope撤销、重入、读回不符及setter写后失败；profile类型/范围、证据缺失拒绝、参数异常、多文件校验失败句柄释放，以及材质槽选择/优先排序也有离线检查。排序检查不证明满128绑定集的动态准入公平性。

| 证据 | 结论 | 执行路径 |
|---|---|---|
| `tests/RizToolbox.Visual.Tests/Program.cs` 的生产服务检查 | 仲裁、作用域、预算及释放在可控后端上可执行 | `VisualResourceService` → `SemanticTargetRegistry` / `MutationCoordinator` → `IVisualBackend` |
| 本机 `dump.cs`、序列化shader与资源路由 | 精确候选签名、两个固定schema及语义槽；不等于所有目标可用 | `VisualModule` → `NativeModuleSymbolResolver` → `NativeRuntime` |
| 603冻结拒绝与607真实Canvas/BG Float成功 | 旧目标/shader拒绝；新槽克隆/参数/赋值成功及两种托管租约清理 | 各包独立会话 → `NativeVisualBackend` → `VisualResourceService` / 通道；见各目录inspector |

仍需TMP Color成功、动态128目标准入、独立基线恢复/可见像素/GPU与原生root统计、真实逐类双插件覆盖、失败保留、反复加载释放及停用/场景完整矩阵。工具面板图标不计游戏目标；内置字体实际取得、独立音符/轨道/相机/后处理语义目标仍缺。B13不应勾选完成。
