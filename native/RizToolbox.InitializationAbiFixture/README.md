# Windows x64 初始化 ABI 离线测试夹具

用于在脱离游戏的情况下，通过真实的原生调用方和 Dobby DLL 来验证 `ExactHookBroker` 的生产级 Hook 替换逻辑。

夹具里提供 8 个独立的 MASM 目标桩函数，每个入口都有 32 字节可打补丁的 NOP 区域，后面尾跳转到带类型的 C++ 原函数。这个原生 DLL 在整个进程生命周期内常驻，打完补丁后**不可卸载**。

## 构建与运行

1. 编译原生 DLL 并运行纯原生自检（默认输出到 `artifacts/native-initialization-abi`）：

```powershell
./scripts/Build-InitializationAbiFixture.ps1
```

此时生成的 `native-probe-result.json` 中 `managedTransportVerified` 为 `false`，因为它只测了 C++ 夹具本身，还没接托管运行时。

2. 编译完正式的 Runtime 程序集后，单独运行完整的生产级 ABI 回归测试：

```powershell
./scripts/Test-InitializationAbiFixture.ps1 `
  -RuntimeAssembly ./src/RizToolbox.Runtime/bin/Release/net6.0/RizToolbox.Runtime.dll `
  -SkipNativeBuild
```

测试运行器直接引用编好的 Runtime 和 SDK DLL，在 `artifacts` 下建独立临时工程，不干扰主解决方案的编译缓存；安装前会先校验 Dobby 的 SHA-256，缺库或 API 版本不匹配会直接报错。

## C# 集成说明

- 可复用的测试入口：`NativeInitializationAbiFixtureRegression.Run(nativeDllPath, dobbyDllPath)`。
- Runtime 测试通过环境变量 `RIZ_INITIALIZATION_ABI_FIXTURE` 和 `RIZ_INITIALIZATION_ABI_DOBBY` 定位原生库；运行 `scripts/build.ps1` 时会自动编译夹具、设置并恢复这两个变量。如果直接跑 Runtime 测试，需要手动提供这两个路径。
- 测试仅通过反射构造目标 token 来填入夹具的原生地址；其余所有 Hook 替换、分发、回调、计数、布尔值封送、拦截和跳板全部走生产 Runtime 代码。

## C 导出接口（`Fixture.h`）

| 导出函数 | 作用 |
| --- | --- |
| `RtInitTarget(kind)` | 获取 8 个原生目标之一的地址（Start / getter / Play / Tooltip / Progress / Mode / Instantaneous / EndRange） |
| `RtInitInvoke(request, result)` | 在当前线程按精确签名调用目标函数，并记录可能抛出的 SEH 异常 |
| `RtInitInvokeWorker(request, result)` | 通过 Win32 `CreateThread` 在工作线程调用目标，等待线程结束后返回 |
| `RtInitRead(kind, observation)` | 读取原函数调用次数、不透明指针参数、原始 float/Boolean 位以及原生线程 ID |
| `RtInitSetReference(reference)` | 设置 getter 原函数在 `RAX` 中返回的引用值 |
| `RtInitPlayInputBits()` | 读取 MASM Play 调用前 `EDX` 的实际值（测试会给高 24 位塞非零值、`DL` 设为 0/1） |
| `RtInitSetObserver(callback, context)` | 注册在 C++ 原函数内部回调的观察者，用于构造同步递归和嵌套调用场景（观察者必须自行捕获所有异常） |
| `RtInitReset()` | 仅在所有原生调用都退出后清空观测记录；不重置生产策略状态 |

在 Windows x64 下，请求结构体为 48 字节、结果 24 字节、观测结果 64 字节。核心签名：
- `Start`：`void(self, method)`
- `getter`：`ref(self, information, method)`
- `Play`：`void(self, uint8, method)`
- `Tooltip`：`void(string, float, method)`（`float` 在 `XMM1`）

所有指针参数都是不透明句柄，夹具绝不会解引用。

## 覆盖范围与边界

测试覆盖：原函数只调用一次、指针与 `MethodInfo` 完整保留、引用/空返回、真假布尔值、可空字符串、`+0/-0`/负数/静默 NaN/信号 NaN 位模式、`EDX` 高位脏数据下的单字节布尔解码（`0xA5B6C700 | value`）、同步递归、Win32 工作线程调用、LOCAL 父级接管与子级屏蔽、回调异常兜底，以及原生调用栈内发生逻辑解绑 + GC。全程要求 C++ SEH 状态为 0。

> 边界说明：这里只证明离线 ABI 和分发链路正确，**不验证** IL2CPP 元数据解析、真实游戏对象字段、游戏二进制的真实函数序言或实机 LOCAL 运行。所有测试清单中的 `gameRuntimeVerified` 均为 `false`。
