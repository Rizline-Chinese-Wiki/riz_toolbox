# Native runtime ABI and lifetime contract

此文档描述 B02 本轮已经实现的内部边界。`NativeRuntime`、`NativeAbi` 和所有指针类型只供 Runtime / Adapter / Loader 使用，普通插件继续引用 SDK。启动使用的真实调用子集已验证，B02 的完整游戏、GC 和异常矩阵仍未完成。

## 调用路径

适配器通过 IL2CPP 元数据选择方法，再调用 `il2cpp_runtime_invoke`。本轮没有新增直接调用游戏 C++ 函数、任意写内存或通用原生 Hook 接口。

`Method` 按名称、参数数量、静态性选择目标；存在同参数数量重载时，适配器必须提供精确参数类型名称。`Invoke` 进一步验证参数数量、方法静态性、接收者可赋值性、参数类型、对象参数可赋值性和返回布局。所有校验完成后才把参数缓冲区交给原生方法。未实例化的开放泛型方法被拒绝；已实例化方法只有参数和返回类型均可验证时才允许走同一条 `runtime_invoke` 路径。

字段读取先取得 `il2cpp_field_get_type`，验证请求类型、实际类型和实际大小，再分配输出缓冲区；未知大结构体不会写进一个较小的 `Int32` 缓冲区。`Read` 与 `ReadStatic` 严格区分实例字段和静态字段。本轮没有字段写入 API。

## 已登记布局

| 类型 | 实际缓冲区 | 验证规则 |
|---|---|---|
| `Boolean` | 1 字节 | 原生值类型大小必须为 1；读取不观察相邻字节；写入只写一个字节 |
| `SByte` / `Byte` | 1 字节 | 类型名称、符号和大小完全匹配 |
| `Int16` / `UInt16` / `Char` | 2 字节 | `Char` 按 UTF-16 码元处理；不与整数互换 |
| `Int32` / `UInt32` / `Single` | 4 字节 | 相同大小的不同类型也不能互换 |
| `Int64` / `UInt64` / `Double` | 8 字节 | 显式按位编解码 |
| `System.IntPtr` / `System.UIntPtr` | 进程指针大小 | 只支持精确的原生指针值类型；对象引用使用独立参数种类 |
| 枚举 | 枚举的实际整数基类型大小 | 验证原生枚举基类型、大小及托管枚举完整名称；不把任意整数当作任意枚举 |
| `UnityEngine.RenderMode` | `Int32` | 为现有适配器登记的明确整数载体例外 |
| 对象、字符串、数组引用 | 进程指针大小 | 元数据必须为引用类型；非空方法参数还要验证可赋值性 |
| `UnityEngine.Vector2` | 两个连续 `Single` | 原生 `x/y` 字段、类型、大小、偏移精确匹配；对应 Adapter 的 `Vec2` |
| `UnityEngine.Color` | 四个连续 `Single` | 原生 `r/g/b/a` 字段、类型、大小、偏移精确匹配；对应 Adapter 的 `Rgba` |
| `UnityEngine.SceneManagement.Scene` | 一个 `Int32 m_Handle` | 只开放经过字段验证的 `Unbox<int>` 投影，用于现有场景名称查询；不接受一般结构体到标量转换 |

两个托管结构体还要验证程序集名称、完整类型名称、顺序/显式布局、全部实例字段类型、总大小及字段偏移。`Marshal.SizeOf` 只参与验证，不能单独证明支持。原生值类型字段的 `il2cpp_field_get_offset` 按当前 IL2CPP 的装箱对象头约定归一为数据区偏移：减去两个指针，再与白名单比较。其他布局会在原生调用之前拒绝，后续版本需要新的探测证据。

`decimal`、`DateTime`、含托管引用的结构体、仅大小相同的自定义结构体、未知值类型、未解析泛型参数、`ref/out`、按引用返回和函数指针参数均未开放。原生 C++ 按值返回 ABI、泛型共享函数的直接调用、可变参数调用也未开放。

## 所有权与线程

- `Arg.Value<T>` 先检查白名单，再分配清零的原生缓冲区；标量使用显式大小和编解码。缓冲区上限为 16 MiB。
- `Arg.Ref` 表示对象引用，不表示 `IntPtr` 标量或 `ref/out`。空对象引用有效；已释放参数会在调用前失败。
- `Arg.Reference` 可以附带已有根；参数释放时一起释放。`Invoke` 消费参数，成功和失败都会释放；重复释放幂等。
- 调用期间为实例接收者及对象参数建立 GC 根；字段读取、拆箱、字符串读取和数组枚举也在访问期间持根。存入适配器状态、跨帧或跨其他原生调用长期保留的返回引用，仍须由调用者显式 `Root`。
- `RootHandle.Target` 通过 `il2cpp_gchandle_get_target` 重新取得对象；空目标或释放后的句柄返回内部 `TargetExpiredException`。释放根幂等。根不是 Unity 对象存活性检查：Unity 原生对象已被 `Destroy` 时，适配器仍需调用已经适配的对象存活检测。
- 普通返回指针是借用值。运行时无法安全证明任意外来非空地址有效，也没有把所有适配器保存的旧指针自动迁移成代次句柄；不得把空指针检查宣称为完整的目标失效检测。
- `RuntimeBridge` 首次识别指定 Unity 生命周期方法时绑定线程。之后 Unity / 对象操作必须位于该线程，错误线程和绑定前操作会被拒绝；纯缓冲区编解码不要求游戏线程。

`String` 输入和 `Text` 输出上限均为 4 Mi UTF-16 码元。`Text` 验证对象确为 `System.String`。`ArrayItems` 验证实际数组种类和一维 rank，以 `GetValue(Int32)` 逐项读取，数量上限为 50,000；多维数组和后台 Unity 遍历未开放。枚举器只持有数组根，调用者保留元素时仍需要根。

## 异常与回调

内部签名拒绝使用 `UnsupportedSignatureException`，消息以 `UnsupportedSignature:` 开头；受控根失效使用 `TargetExpiredException`。这些内部异常没有改变公开 SDK 的 `OperationResult`，适配器按各自能力边界报告失败。

`runtime_invoke` 的 IL2CPP exception 输出非空时抛出托管异常，参数和调用根仍按 `finally` 释放。桥回调保留委托和原始跳板。游戏原始调用在框架托管 catch 之外执行，保持其异常和返回语义，不重试或伪造零返回；只有框架 tick 及其日志被隔离。初次集成曾用 catch(Exception) 包住原始调用，捕获 SEH 后返回零导致启动失败；该改动已撤回，失败证据单独保留，修正后启动通过。

原生访问违规、无效地址和 C++ 异常不能靠 `catch (Exception)` 保证恢复。桥仍是进程生命周期的单一全局 `runtime_invoke` 挂接；原生安装到跳板发布之间的在途回调同步以及实际原生卸钩尚未增加新的保证。此机制也不代表能拦截全部游戏函数。

## 本轮验证与未验收范围

运行独立检查：

```powershell
dotnet run --project tests/RizToolbox.Runtime.Tests/RizToolbox.Runtime.Tests.csproj -c Release
```

本轮独立检查 **43/43 通过**。测试使用真实原生内存缓冲区与合成的 IL2CPP 元数据/导出委托，不加载游戏或 Dobby。覆盖一字节 Boolean 相邻非零字节、所有标量与枚举往返、实际 Adapter `Vec2/Rgba` 往返、未知结构写入拒绝、字段大小和静态性、方法参数/返回/接收者、开放泛型、ref/out、装箱实际类型、Scene 投影、引用可赋值性、值缓冲区与对象引用混用拒绝、根释放、受控句柄失效、错误线程与原生异常输出。GC 测试验证的是强句柄模型和包装器契约，不是已经实测 IL2CPP 回收器。

本轮实际游戏启动已验证新增导出可解析，真实 Color/Vector2/Scene 元数据、字符串/数组及主线程服务调用可用；两个颜色插件及面板子作用域创建/撤回通过，最后会话无警告/错误。证据见 [运行摘要](validation/v03/runtime-summary.json)。真实 IL2CPP GC 后长期引用、完整游玩和异常矩阵仍未验收，不能用合成句柄模型代替。
