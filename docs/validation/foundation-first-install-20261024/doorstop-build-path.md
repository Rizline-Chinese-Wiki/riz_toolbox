# 固定 Doorstop 生产构建路径

2026-10-24，只读官方源码、本机构建工具和当前 RuntimeBridge 后形成的下一轮实施任务。本轮没有构建 Doorstop、安装、启动游戏或修改生产源码。

结论：直接在固定 Doorstop 的 `init_il2cpp` 前增加一个 Windows 原生接缝，调用现有独立 C++ helper 中的新 producer。原 `il2cpp.init` 仍执行一次，返回后继续原 CoreCLR 启动。使用本机 MSVC 2019 和固定 xmake 2.6.1 即可开展构建；不需要 CMake、Meson、额外注入器或重写 CLR 启动。构建可行是源码和工具链核对结论，实际编译成功仍须下一轮 root 的日志证明。

## 固定输入

| 输入 | 官方固定地址 | 本轮实读证据 |
| --- | --- | --- |
| Doorstop 完整源码 ZIP | [commit archive](https://codeload.github.com/NeighTools/UnityDoorstop/zip/33dab9a6733862eb81869ff08431d9478b28784b) | 114,921 bytes；SHA256 `a71789ffcb359f6a37c6d2c7bbdd0527231e9db00bf46637693cff88ba7ffb80` |
| 固定构建文件 | [xmake.lua](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/xmake.lua)、[build.ps1](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/build.ps1) | 实际构建系统是 xmake；脚本固定版本 2.6.1 |
| xmake Windows x64 工具 ZIP | [xmake-v2.6.1.win64.zip](https://github.com/xmake-io/xmake/releases/download/v2.6.1/xmake-v2.6.1.win64.zip) | 5,189,505 bytes；SHA256 `08a2a411f0ac738f3e77f475bd936e16845891618ba927ee6c115ac05494fec9`；首次传输中断，完整重取后才计算此值 |
| 许可证 | [固定 LICENSE](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/LICENSE)、[README license](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/README.md#license) | Doorstop 4 为 LGPL v2.1；不能用 legacy 分支的 CC0 描述本版本 |

下载仅在 Python 内存中检查 ZIP，未展开到源码输入树。完整 ZIP 包含生成代理和资源所需 Lua、模板、proxylist；没有 `.gitmodules`、`add_requires`、NuGet、Dobby 或第三方 C++ 构建依赖。Windows 目标编译本仓 C 文件和生成代理/资源，链接 `shell32`、`kernel32`、`user32`；其自带 Windows CRT 实现，使用自定义 `DllEntry` 和 `/NODEFAULTLIB`。Dobby 和 JudgeContext 是我们的 producer 运行时依赖，不是上游 Doorstop 的依赖。[构建定义](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/xmake.lua)

分发修改版时随包保留许可证、原版权和修改日期，提供对应的 Doorstop 修改源码及构建脚本；具体要求以固定 LICENSE 为准。把 producer 保持为独立 DLL，也能避免将现有 C++ CRT/异常支持直接塞进 Doorstop 的 freestanding 链接目标。

## 本机命令

只读检查得到：`Microsoft.VCToolsVersion.default.txt = 14.29.30133`，该目录 `cl.exe` 文件版本 `19.29.30159.0`，Windows SDK 为 `10.0.19041.0`。PATH 中未找到 xmake。Microsoft 文档要求 VS2019 16.8 起支持 C11/C17，这套 14.29 工具链满足；固定上游没有显式选择 C11，而 `src/windows/util.c` 使用 compound literal 和 designated initializer，因此本次 fork 应显式使用 C11，避免默认 C 模式差异。[Microsoft C11/C17 支持](https://learn.microsoft.com/en-us/cpp/overview/install-c17-support)

以下是下一轮在独立 fork 缓存目录执行的 PowerShell 命令，本轮未执行。目录名刻意区别于本轮 source snapshot：

```powershell
$forkRoot = 'D:\RizToolbox-build-cache\doorstop-riz-33dab9a'
$upstreamZip = 'D:\RizToolbox-build-cache\doorstop-33dab9a.zip'
$xmakeZip = 'D:\RizToolbox-build-cache\xmake-v2.6.1.win64.zip'
Invoke-WebRequest 'https://codeload.github.com/NeighTools/UnityDoorstop/zip/33dab9a6733862eb81869ff08431d9478b28784b' -OutFile $upstreamZip
if ((Get-FileHash -LiteralPath $upstreamZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'a71789ffcb359f6a37c6d2c7bbdd0527231e9db00bf46637693cff88ba7ffb80') { throw 'Doorstop source mismatch' }
Expand-Archive -LiteralPath $upstreamZip -DestinationPath $forkRoot
$forkSource = Join-Path $forkRoot 'UnityDoorstop-33dab9a6733862eb81869ff08431d9478b28784b'
Invoke-WebRequest 'https://github.com/xmake-io/xmake/releases/download/v2.6.1/xmake-v2.6.1.win64.zip' -OutFile $xmakeZip
if ((Get-FileHash -LiteralPath $xmakeZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne '08a2a411f0ac738f3e77f475bd936e16845891618ba927ee6c115ac05494fec9') { throw 'xmake mismatch' }
Expand-Archive -LiteralPath $xmakeZip -DestinationPath (Join-Path $forkSource 'tools')
Push-Location $forkSource
try {
    & '.\tools\xmake\xmake.exe' f -p windows -a x64 -m release --vs=2019 --vs_toolset=14.29.30133 --vs_sdkver=10.0.19041.0 --include_logging=y --cflags=/std:c11
    if ($LASTEXITCODE -ne 0) { throw 'Doorstop configure failed' }
    & '.\tools\xmake\xmake.exe' -vD doorstop
    if ($LASTEXITCODE -ne 0) { throw 'Doorstop build failed' }
} finally { Pop-Location }
```

在加入以下接缝后再构建，产物是 `build/windows/x64/release/doorstop.dll`，后续暂存包按上游 CI 命名为 `winhttp.dll`。保留完整编译日志、生成的 `build/proxy.c`/`dll.def`/`info.rc`、产物哈希与 fork diff。上游 `build.ps1 -Arch x64 -with_logging` 也能启动构建，但其自动下载不验摘要且不会显式固定 VS/SDK；正式重复构建使用上述命令。[上游 CI 产物路径](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/.github/workflows/build-be.yml)、[固定 xmake 的 VS 参数](https://github.com/xmake-io/xmake/blob/v2.6.1/xmake/platforms/windows/xmake.lua)

## 最小修改点与接线

| 文件 | 要改的具体位置 |
| --- | --- |
| Doorstop `src/bootstrap.c` | Windows 条件包含新 C bridge header；在 `init_il2cpp(const char*)` 中现有 `const int orig_result = il2cpp.init(domain_name);` **之前**调用 `RizBeforeIl2CppInit`。保留该原调用和后续 `il2cpp_doorstop_bootstrap()`，不捕获/改写原 init 的异常与返回值。 |
| Doorstop 新 `src/windows/riz_preinit.c` 和 `.h` | 只做 WinAPI 路径解析、固定 helper 装载/函数解析及一次性状态接缝。`xmake.lua` 已 glob `src/windows/*.c`，不必另加源列表。代码保持 C11 与现有 custom CRT 兼容。 |
| 现有 JudgeContext helper 的新 C++ producer 源/头 | 新增窄 C 导出，例如 `RizFirstInstall_BeforeIl2CppInit(module, originalInit)`；负责固定映像检查、真实准备/Configure/提交、mid relay、永久根及可读 receipt。导出名是下一轮拟定接口，当前没有此函数。 |
| 现有 managed RuntimeBridge/统一 native 槽责任 | 原 init 后读取真实 producer receipt，受限采用既有 parent/mid/runtime_invoke；晚挂只发布 observer，不再发起首次指令修改。 |

当前上游 wrapper 的准确接缝是 [bootstrap.c `init_il2cpp`](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/src/bootstrap.c)，真实 ABI 是 `int (__cdecl*)(const char*)`，不能写成 `void`。C bridge 可通过原 `il2cpp.init` 地址使用 `GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, ...)` 获取真实模块，再校验其为固定 GameAssembly，避免仅凭模块名采用其他映像。[IL2CPP 声明](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/src/runtimes/il2cpp.h)

推荐职责顺序为：确定模块与 helper 完整路径并加载 → producer 校验固定 GameAssembly/UnityPlayer、入口字节、NOP、unwind 和启动合同 → DobbyPrepare 取得 original → 配置永久 native bootstrap parent/mid → 发布 mid 与 parent → 返回 sealed receipt → 原 init 一次 → 原 CoreCLR → 托管统一 allocator/broker 采用 → 发布 observer。实际顺序中必须记录每一次指令写入及责任；写入前拒绝可继续原启动，写后未知停止受支持的启动并保留 native 根。仅在 pre-init 调用函数本身不等于已经证明所有目标没有旧调用，下一轮须把固定启动合同和 producer 的写入前验证实现成实际条件。

**生产 producer 还必须一起覆盖 `il2cpp_runtime_invoke`。** 本机当前 [RuntimeBridge.cs](../../../src/RizToolbox.Runtime/RuntimeBridge.cs) 的 `Start → Install` 在 post-init 后台线程，对该函数执行 `DobbyHook`；只提前 UnityEvent 会留下同一类首次挂接竞态。下一轮 native producer 应增加 `4 ptr → ptr` 的 permanent pass-through/late-attach transport，托管 RuntimeBridge 采用其真实 original/entry 与 receipt。生产分支禁止再走当前 `Install` 的首次 Dobby 路径。此依赖尚未实现，不能以本批 bootstrap void fixture 代替验证。

## 原有配置和独立 loader 的取舍

固定 [配置头](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/src/config/config.h) 和 [Windows INI](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/assets/windows/doorstop_config.ini) 没有 native-before-init callback 选项；`target_assembly` 是 managed entry，IL2CPP 路径在原 init **之后**执行它，不能靠改配置把它变成原生 pre-init producer。保留现有 BepInEx `target_assembly`、`coreclr_path`、`corlib_dir` 即可维持后半段启动。

从机制上，独立纯 C++ loader 可以在同进程保存 UnityPlayer 的当前 `GetProcAddress` IAT 值并包一层：调用前一层获得 `il2cpp_init` wrapper，再返回自己的 `BeforeInit → 前一层 init`，其余符号原样转交。这样理论上也可保留 Doorstop 原 init 后的 CoreCLR。但是它要求一个**确定发生在 Doorstop 完成 inject 之后、Unity 第一次查询 init 之前**的同步入口；当前安装结构没有提供该入口。后台线程或等窗口出现都不满足这个时序。

不要把 `winhttp_alt.dll` 当作已存在的这个入口：上游 `DllEntry` 先 `load_proxy`，后 `inject`；alt 在前者被加载。若 alt 的 DllMain 抢先替换同一 UnityPlayer IAT，上游 `iat_hook` 按原 `GetProcAddress` 地址寻找槽会失败，Doorstop wrapper/既有 CoreCLR 链可能丢失。若另外重建完整代理 loader、同步加载 Doorstop再链 IAT，还要新增 proxy 路径与加载顺序合同，工作量大于这里两处 C 接缝。本轮选择小 fork；不创建第二套 loader。[真实加载顺序与符号拦截](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/src/windows/entrypoint.c)、[IAT 匹配实现](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/src/windows/hook.h)、[alt 查找](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/src/windows/proxy/proxy.h)

本文件把下一轮落点固定到可构建的源码，不将源码审阅、pre-init 名称、离线 fixture 或测试数记作生产首次安全发布已经完成。
