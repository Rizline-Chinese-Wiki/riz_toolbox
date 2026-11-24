# B13 材质 schema 的来源与实现边界

两个材质参数 profile 已接入生产适配器源码：先验证当前游戏文件，再用带类型的 Resources 资源引用确定实际 Shader 对象，最后只修改新克隆并读回。此目录的证据仍属于静态研究；这里没有游戏内材质替换、渲染效果或场景重建验收结果。此次审查未运行 dotnet，不能把新增用例数量写成通过数量。

主任务后续已实际运行 **43/43**，日志为 [tests.log](tests.log)，并纳入最新582项全量回归。首次统一编译发现 .NET6 不支持 `SHA256.HashData(Stream)`，已改为 `SHA256.Create().ComputeHash(Stream)`；先前失败日志保留。以下审查记录的“未运行”指审查子任务当时状态，新材质仍没有原生执行验收。

## 范围与当前状态

范围为现有 B13 中的材质参数支持，审查文件限于 `NativeVisualBackend.cs`、`VisualMaterialProfiles.cs`、Visual 测试程序及本目录。不扩展到自定义 Shader、任意材质参数、TMP 字体资源替换或新建游戏内视觉目标。文档采用普通技术说明，`flavor = null`。

| 项目 | 已有证据 | 尚需证明 |
| --- | --- | --- |
| Shader 属性类型、Range 边界和资源引用 | 已安装文件的 Unity 序列化对象与 ResourceManager 路由 | 游戏实际载入对应资源 |
| 生产调用路径 | 源码已使用 hash、typed Load、Shader 对象相等、克隆写入及读回 | 本版代码成功构建并在游戏执行 |
| 验证与失败路径 | Visual 测试源码共 43 个用例，含已有 6 个 profile 用例及本次新增 2 个用例 | 主代理统一串行运行后的真实结果 |
| 双插件替换与恢复 | 服务栈的 fake backend 用例 | 原生克隆实际渲染、撤销、切场景、对象重建 |

`proposed-material-profiles.json` 保留初次研究时的 `static-research-only-not-loaded-by-production` 状态，作为冻结的研究产物；当前源码的生产接入状态以本说明和实现文件为准。该 JSON 不作为外部可加载的 profile 配置。

## 已有静态证据

采集时间为 `2026-10-20T04:43:02.587373+00:00`。工具版本为 UnityPy 1.24.2、pefile 2024.8.26、capstone 5.0.6。脚本只读取已安装文件与既有 dump，没有启动、附加或修改游戏进程。

| Evidence | 文件 | SHA-256 | 结论用途 |
| --- | --- | --- | --- |
| E-001 | [static-profile-evidence.json](C:/Users/71957/Desktop/riz_toolbox/docs/validation/visual-20261020/static-profile-evidence.json) | `a40d9fa8e5938ef2d0c588c0153e45a125186b61b10c043bc842b46e2b4b15f5` | 来源文件身份、资源路由、Shader 属性类型和选定反汇编 |
| E-002 | [proposed-material-profiles.json](C:/Users/71957/Desktop/riz_toolbox/docs/validation/visual-20261020/proposed-material-profiles.json) | `4c98539e594776aec13cbec26221a0af2300e01ddb2c035b8a3586fc28acc066` | 两个闭合 profile 的派生设计及未证明项 |
| E-003 | [inspect_visual_static.py](C:/Users/71957/Desktop/riz_toolbox/docs/validation/visual-20261020/inspect_visual_static.py) | `353d214ac4bace79f5e4a53f1227838d266da1280257eb934d27f260a7cd5d9e` | 静态复核方法 |

E-001 的 `inputs` 保存全部输入路径、大小与 hash。两个生产 profile 都要求 `GameAssembly.dll`、`Rizline_Data/globalgamemanagers`、`Rizline_Data/resources.assets` 精确匹配；文字 profile 另外要求 `Rizline_Data/sharedassets0.assets`。参考原生程序集的 SHA-256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。

| Profile | 精确 Resources 引用 | 允许的参数与来源 |
| --- | --- | --- |
| `rizline.pc.tmp-mobile-outline.v1` | Material：`fonts & materials/liberationsans sdf - outline`；ResourceManager fileID 5/pathID 2 → resources.assets → sharedassets0.assets Shader pathID 11 | `_FaceColor`、`_OutlineColor` 为序列化 Color；`_OutlineWidth` 为 Range `[0,1]`；`_FaceDilate` 为 Range `[-1,1]` |
| `rizline.pc.translucent-ui-float.v1` | Shader：`translucentimage`；ResourceManager fileID 5/pathID 19 → resources.assets Shader pathID 19 | `_Vibrancy`、`_Brightness`、`_Flatten` 为序列化 Float；没有序列化数值范围，只要求有限值 |

Color 的每通道 `[0,1]` 是适配器的保守输入策略，不能称为 Shader 声明的 Range。Shader 名称只用于阅读证据，不参与生产放行；`HasProperty` 也只检查属性存在，不能证明类型。

## Finding 与生产调用路径

F-001（静态来源已核实，证据 E-001、E-002）：可从指定安装文件导出上述 Color、Float、Range 类型及精确 Resources 路由。此结论不证明游戏已经运行新代码，也不证明屏幕输出变化。

F-002（源码审查，运行行为待验收）：[VisualMaterialProfiles.cs](C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Visual/VisualMaterialProfiles.cs) 只声明这两个 profile。[NativeVisualBackend.cs](C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Visual/NativeVisualBackend.cs) 使用这些 profile 完成 P-001：

1. 在主线程保留实际源 Material，复制最多 17 项参数形成有界快照；验证总数最多 16、名称唯一、值有限、Color 策略及序列化 Range。
2. 无参数时允许完整克隆；有参数时，每个参数的名称和类型必须属于同一个既定 profile。未知属性、错误类型、混合 profile 都拒绝。
3. 以实际进程可执行文件目录为根，逐一打开并 hash 校验 profile 的固定文件；读取句柄保持到该参考资源解析结束。任一缺失、空文件、超过 256 MiB 或 hash 不同即拒绝，并释放已打开句柄。
4. 通过非泛型 `Resources.Load(System.String,System.Type)` 加载精确资源，保留资源和 Shader；源 Shader 必须通过 `UnityEngine.Object.op_Equality` 等于该参考 Shader。
5. 仅对 `Object.Instantiate` 的新 Material 写入参数。克隆 Shader 必须仍等于已验证的源 Shader；SetColor/GetColor、SetFloat/GetFloat 的读回必须有限且误差不超过 `0.0001`。
6. 失败时销毁已经创建的克隆，包括初次 Root 或包装失败的窗口；临时 roots 由作用域释放。原 Material 始终不写入，也不销毁。

旧的 `VisualMaterialSchema.Validate("UI/Default", ..., schemaVerified:true)` 没有生产调用，现已删除。其测试已迁移到真实 profile 选择器，不能再用人工布尔值模拟来源证明。

P-001 的残余风险：hash 计算在调用线程执行；此实现尚无频繁创建材质时的实机耗时证据。现有 Visual 服务只枚举 UI Graphic.material；TMP 的字体材质缓存以及 TranslucentImage 对 Float 的后续重写可能影响可见效果。临时文件读锁只覆盖验证和参考资源解析，不是整个游戏运行期的完整性锁。

## 本次审查补充的覆盖

测试源码保留原有未知 schema 拒绝、两个 profile 选择、类型混用、Color/Range 边界、hash 不匹配、双插件克隆优先级恢复等覆盖。本次另外检查空列表免 schema、null 请求/条目/Color、空名称、同类重复、17 项参数、非有限 Float，以及后一份证据缺失或损坏时先前句柄释放。有效证据使用大写 hash 仍应匹配；第二个文件为空也应拒绝。

这些用例通过 fake backend 检查服务栈和验证器，不能代替 Unity 原生资源加载或屏幕渲染验收。此次审查没有运行它们。

## 复核命令与下一步

下列静态复核需要本机现有 UnityPy、pefile、capstone、游戏文件与既有逆向输出。输出放到新目录，避免覆盖本次冻结证据：

```powershell
& C:\Python314\python.exe 'C:\Users\71957\Desktop\riz_toolbox\docs\validation\visual-20261020\inspect_visual_static.py' --output 'C:\Users\71957\Desktop\riz_toolbox\docs\validation\visual-recheck-20261020'
```

主代理完成串行构建后，应在正式安装包中用 SDK 请求匹配的实际材质目标，保存精确 Load、Shader 相等、参数读回与来源 hash 的日志；再验证两插件优先级、撤销恢复、切场景和重建后旧目标失效。只有相应运行证据齐全，才能把 B13 的材质参数分项标成实机通过。
