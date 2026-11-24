# 材质槽位与可选目标的静态可达性

本次找到两项会阻碍材质成功验收的问题：原实现对 TMP 读取 `Graphic.material`，没有读取字体实际使用的 `TMP_Text.fontSharedMaterial`；128 个目标的先到先得上限又允许普通 Image/Text 先占满列表。已据此修改下一包源码，未运行 dotnet 或游戏 UI。当前已安装的 603 包实测和新源码必须分别归档，不能将此次静态研究计为新包实机通过。

## 结论与可优先验证的路径

Translucent profile 有明确的实际组件候选，最适合先验证成功应用。当前 outline profile 的可信 Shader11 与多数实际 TMP 文本不相同；修复槽位仍不等于中文文本会通过 outline profile。现有两种 profile 在 DiscTabItem 上精确拒绝，只能算拒绝分支通过。

| 场景文件 | 场景 | TextMeshProUGUI 数量 | TranslucentImage 数量 | 其中静态层级启用的两类组件总数 |
| --- | --- | ---: | ---: | ---: |
| level2 | MainMenu | 20 | 2 | 0 |
| level4 | LevelList | 31 | 14 | 15 |
| level5 | WorkshopLevelList | 31 | 15 | 17 |
| level7 | LevelOver | 2 | 2 | 2 |
| level8 | Challenge | 3 | 1 | 0 |

这里的静态启用状态来自 GameObject 与所有 Transform 祖先的 `m_IsActive`，不是当前运行状态。其他四个直接序列化场景没有这两类组件；运行时实例化的 prefab 不包含在这个零计数结论内。

TranslucentImage 的 34 个组件都包含指向 `sharedassets2.assets / Material pathID 4` 的原始序列化 PPtr 候选；该 Material 的完整 Shader 引用已由 UnityPy 的 Material 类型树解析为 `resources.assets / Shader pathID 19`，正是当前可信 profile。建议先在下列候选中选取实际激活对象：

| 场景 | 层级候选 | 静态启用 |
| --- | --- | --- |
| LevelList | `Canvas/Content/TopBar/BG` | 是 |
| LevelList | `Canvas/Content/Card/BG` | 是 |
| LevelList | `Canvas/Content/LevelList/Scrollbar Vertical/BG` | 是 |
| WorkshopLevelList | `Canvas/Content/Content/TopBar/BG` | 是 |
| WorkshopLevelList | `Canvas/Content/Content/Card/BG` | 是 |
| LevelOver | `Canvas/Content/LevelInfo/BG`、`Canvas/Content/Card/BG` | 是 |
| MainMenu | `SettingCanvas/Settings/Window/BG` | 否，设置窗口激活后再运行查询 |

上述组件的自定义 MonoBehaviour 类型树被裁剪；PPtr 字节匹配没有被假称为已经解码的 `m_Material` 字段。证据同时记录精确 MonoScript 类型、程序集、对象层级、资产文件和 pathID；真正运行时仍须通过类型验证及 exact Shader equality。

TMP 的 87 个直接场景组件中，原始引用候选统计为：82 次 Shader10、33 次 `globalgamemanagers.assets / Shader5`、5 次当前可信 outline Shader11。这是引用候选数量，不能相加当组件数量。只有以下五个组件出现 outline Shader11 材质候选，且静态祖先层级全部未启用：

- MainMenu、LevelList、WorkshopLevelList 各自设置窗口下的 `Settings/Window/SettingPages/GameplaySetting/Item (4)/Pointer/KeyHint/GamePadHint/Label`。
- WorkshopLevelList 的 `Canvas/Content/Content/TopBar/Content/Sort/KeyHint/GamePadHint/Label`。
- WorkshopLevelList 的 `Canvas/Content/Content/Card/Content/Remove/KeyHint/GamePadHint/Label`。

因此不能要求操作者不断翻到普通中文 TMP 就宣称 outline 支持；多数中文材质引用的是 `sharedassets0.assets / Shader10`，现有 profile 应继续拒绝。Shader10 是否有可信 typed Resources 路由另行只读研究，不按 Shader 名或文件 hash 单独开放。

## 为什么原目标列表不够

E-slot：dump 中 `TextMeshProUGUI : TMP_Text : MaskableGraphic`，且二者都没有覆盖 `get_material/set_material`；当前 `_api.Call(..., "get_material")` 会沿真实类祖先找到 Graphic 访问器。静态反汇编确认 `Graphic.get_material@15E86A0` 从原生字段位置 `+0x20` 读取；`TMP_Text.get_fontSharedMaterial@4AA4B0` 从 `+0x118` 读取。这里偏移只作为已安装文件的反汇编观察，不用于生产读写。生产修复只调用元数据验证后的访问器。

`TextMeshProUGUI.SetSharedMaterial@1499460` 将值传入独立 TMP 字段并调用后续更新；这进一步证明字体槽位与 Graphic 基础槽位不同。TranslucentImage 的 dump 则声明继承 Image，仍沿 Image/Graphic 的 material 访问路径，不应切换到 TMP 槽位。

E-budget：当前服务上限为 128 绑定，后续按实例 ID 排序的探针只重排已截断的列表。MainMenu 的无歧义 Graphic 继承计数下界已有 382，LevelList 下界 739；因此翻完 128 不等于遍历整个场景。研究脚本遇到未限定命名空间的歧义 `Image` 父类时保留 unresolved，不擅自补类型，所以计数下界没有把另外列出的 TranslucentImage 加入。

## 下一包已写源码

| 接收者 | 绑定槽位 | Role | ResourceId | 优先级 |
| --- | --- | --- | --- | ---: |
| 真实 assignable 到 TMP_Text | `get/set_fontSharedMaterial` | `material` | `unity.tmp.font-shared-material` | 100 |
| 真实 assignable 到 TranslucentImage | 既有 `get/set_material` | `material` | `unity.ui.translucent-image.material` | 100 |
| 其他 Graphic | 既有 `get/set_material` | `material` | `unity.ui.material` | 0 |

TMP 类来自 `Unity.TextMeshPro.dll / TMPro.TMP_Text`，TranslucentImage 来自 `LeTai.TranslucentImage.dll / LeTai.Asset.TranslucentImage.TranslucentImage`，程序集身份与序列化 MonoScript 一致。`NativeRuntime.ExactHookReceiverMatches` 在运行时验证 assignability，继承关系不按 GameObject 名或 Shader 名判定。TMP getter 必须是非静态零参数返回 `UnityEngine.Material`，setter 必须是非静态单 `UnityEngine.Material` 参数且返回 `System.Void`；缺少或不匹配只排除 TMP 槽位，记录一次诊断，不回落为 Graphic.material。空或失效 fontSharedMaterial 同样不发布。

材质 Discover 单次最多检查 4096 个原生数组元素，在目标上限生效前按真实组件槽位优先排序；原生 Resources 数组从扫描开始到 yield 完成始终 Root。普通字体/精灵路径保留原有行为。槽位 canonical key 包含 `/slot/tmp-font-shared` 或 `/slot/graphic`；保留旧的实例 alias 和名字 alias。`Role=material` 保持查询兼容；ResourceId 增加更精确的语义，不能被调用者当成 Shader profile 授权。

MaterialRuntimeProbe 的 NextTarget 已将 `unity.ui.translucent-image.material` 排在首位，`unity.tmp.font-shared-material` 次之，普通 Graphic 最后；同组随后按已绑定对象 key/generation 排序。进入 LevelList 后，应可优先看到真 Translucent 槽位，直接点击 Translucent Float 按钮即可尝试对应 profile。

限制：此修复保证新服务首次扫描的排序。运行中旧的 128 个存活普通对象已占满时，服务不会主动驱逐它们，之后才出现的新专用槽位仍可能等待原对象失效或场景重建；本轮没有任意失效用户现有租约。4096 原始对象上限之外的对象也仍不可见。完整动态饥饿解决需要独立设计分层容量和租约保护，不能称本轮已实现。

最终写入检查保持不变：固定输入文件 hash、typed Resources.Load 的精确资源引用、Shader 对象 equality、克隆参数 Set/Get 读回以及 mutation assignment readback。槽位优先级和静态引用候选不绕过其中任何一步。

## 证据、复现与待验收

本次报告采用 docs-generator 普通技术说明，`flavor=null`。范围仅项目源码与既有安装文件的读取，以及明确授权的下一包 Visual 槽位修复；未访问游戏内存、启动/操作游戏或运行 dotnet。逆向工具索引缺 IDA，沿已获授权的既有 dump、UnityPy 与 capstone 静态证据路径工作。

| Evidence | 文件 | 用途 |
| --- | --- | --- |
| E-select-001 | [material-live-selection-evidence.json](material-live-selection-evidence.json) | 输入 hash、9 场景 BuildSettings 白名单字段、121 组件身份/层级与候选引用、5 个可信材质、访问器反汇编 |
| E-select-002 | [inspect_material_selection.py](inspect_material_selection.py) | 可复现的只读提取方法；不导出 BuildSettings 其他字段 |
| E-select-003 | [static-profile-evidence.json](static-profile-evidence.json) | 既有 profile 类型、资产引用与 Translucent 更新行为证据 |

F-select-001：错误 TMP 槽位已由 E-select-001 与生产源码审查证实。P-select-001 为实际 Graphic 扫描 → runtime assignability → 精确 TMP 访问器验证 → 独立 fontSharedMaterial 绑定 → 原有 profile 校验 → 克隆/覆盖/恢复。

F-select-002：Translucent 有实际场景候选且部分静态激活，由 E-select-001 支持；运行可达、Shader identity、可见效果和后续组件 Update 是否重写参数仍待下一包实测。P-select-002 为 LevelList → 真实 TranslucentImage 槽位 → `unity.ui.translucent-image.material` 优先筛选 → 请求当前 Float profile。

复现静态研究：

```powershell
& C:\Python314\python.exe 'C:\Users\71957\Desktop\riz_toolbox\docs\validation\visual-20261020\inspect_material_selection.py'
```

新增四项离线回归源码验证 TMP 缺访问器时禁止回落、Translucent/普通槽位区分、128 首扫配额优先和槽位识别不授权错误参数 profile。此次未运行这些测试，不报告通过数量。下一包须统一串行构建，再分别验证 Translucent 成功应用/恢复、正确 TMP 字体槽位、非匹配 Shader 拒绝、空/缺槽位、切场景和重复创建释放。

## Shader10 的可信 Resources 路由补查

下一包槽位源码冻结后，继续只读检查了当前 `globalgamemanagers.ResourceManager` 的全部 30 条 key。结果保存在 [shader10-resource-route-evidence.json](shader10-resource-route-evidence.json)，方法为 [inspect_shader10_routes.py](inspect_shader10_routes.py)。这部分没有新增生产 profile。

Shader10 存于 `sharedassets0.assets / pathID 10`；其 `_FaceColor`、`_OutlineColor`、`_OutlineWidth`、`_FaceDilate` 有实际序列化类型证据，完整材质为同文件 `Material pathID 4`，字体为 `TMP_FontAsset pathID 217` 的引用候选。仅有属性类型和来源文件 hash 还不足以建立运行时 Shader 对象身份。

全部 Resources 字体/材质 key 均为 LiberationSans 系列，实际指向 resources.assets，相关材质仍使用 Shader11。没有 key 直接指向 Shader10、其 Material4 或 FontAsset217。`tmp settings` 可按类型加载 `resources.assets / TMP_Settings pathID 34`，但该对象以及 Resources 内 LiberationSans 默认/后备字体的原始数据中，都没有指向 `sharedassets0.assets / FontAsset217` 的 PPtr 候选。因此当前没有证据支持“typed Resources.Load TMP_Settings → 默认/后备字体 → Shader10”的正向闭合链。

结论：**本轮不能为 Shader10 添加新的已验证 profile**。不能把 `Resources.Load("Source Han Sans …")` 作为可用路径，也不能用 Shader.Find 名字或“目标自己返回的字体材质恰好是目标 Shader”作来源证明。后续可以另查受验证的 Addressables/AssetStore 内容 key 或确定的宿主资源对象来源；只有获得确切类型、key、输入 hash 与对象引用链并完成运行时 equality/readback 证据后，才新增独立 closed profile。

复现 Shader10 补查：

```powershell
& C:\Python314\python.exe 'C:\Users\71957\Desktop\riz_toolbox\docs\validation\visual-20261020\inspect_shader10_routes.py'
```
