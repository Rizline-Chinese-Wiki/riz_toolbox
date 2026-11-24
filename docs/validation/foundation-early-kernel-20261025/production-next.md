# 下一实现包：固定 WinHTTP 与生产引擎采用

2026-10-25。目标是把已有小内核、固定发布过程和只读采用串成明确接口。以下是可编码接续，不是已完成的游戏凭据；本页只读源码，没有构建、子进程、安装或游戏调用。

## 当前真实入口在哪里

[WinHTTP静态合同](../foundation-winhttp-contract-20261025/research/game-winhttp-contract.md)已确认固定UnityPlayer按名普通导入4项：WinHttpOpen、WinHttpGetProxyForUrl、WinHttpGetIEProxyConfigForCurrentUser、WinHttpCloseHandle。不能用代理ordinal绑定系统ordinal；也不能据这4项删除其他调用者仍可能需要的现有82项公开接口。

[固定上游归档](../../../.tools/doorstop-preinit-inputs/doorstop.zip)中实际系统库加载/按名解析位于：

- `src/windows/entrypoint.c:256 DllEntry`：process attach内先`init_crt`、logger、paths，279行`load_proxy(paths->doorstop_filename)`。
- `src/windows/proxy/proxy.h:14 load_proxy`：先拼本目录`module_alt.dll`并尝试加载；失败后30–41行GetSystemDirectory、拼系统目录绝对路径并加载，最后47行load_functions(handle)。这里有堆分配/日志及两种来源选择。
- `src/windows/wincrt.c:28 dlopen`实际是LoadLibrary；`build_tools/proxygen.lua:22`生成每项GetProcAddress(handle,"精确名称")，`proxy.c.in`装入槽。它已经按名转发，不是尚待实现的空代理。

[Build-Doorstop.ps1](../../../scripts/Build-Doorstop.ps1)目前只修改上游bootstrap的原il2cpp.init调用缝，并复制四个Riz C/H文件；没有改变上述系统转发阶段。[riz_preinit.c](../../../native/RizToolbox.Doorstop/riz_preinit.c)在该较晚wrapper里LoadLibraryExW加载JudgeContext，再调用旧FirstInstall；[riz_init_call.c](../../../native/RizToolbox.Doorstop/riz_init_call.c)只保证原init一次、保留int/原生异常、成功后才CoreCLR。

## 下一包最少文件及职责

| 文件（标注“新”者尚未实现） | 本包具体工作 |
| --- | --- |
| `native/RizToolbox.Doorstop/riz_winhttp.c/.h`、`riz_winhttp_entry.asm`（新） | 定长4槽、精确按名解析、过程状态与错误责任、无参改写tail jump；系统模块必须是核验的绝对系统路径/实际文件身份，拒绝代理自身和任意_alt来源。其余既有导出保留兼容边界。 |
| `native/RizToolbox.EngineBootstrap/EngineFirstInstall.h/.cpp`（新） | 定长独立引擎发布凭据、一次producer门、读取接口；只查询/配置已由普通静态依赖加载的Kernel，禁止在早期producer里启动CLR或迟到LoadLibrary加载Kernel。 |
| `native/RizToolbox.EngineBootstrap/EngineGameProfile.h`及必要静态original ASM（新） | 固定UnityPlayer/GameAssembly/helper/publisher身份、四个RVA和完整prefix/unwind/字段合同；仅在自有等形映像验证后加入生产profile，不接受runtime任意module/target。 |
| `scripts/Build-Doorstop.ps1`及`native/RizToolbox.Doorstop/RIZLINE-MODIFICATIONS.md` | 对固定上游entrypoint/proxy生成缝做可核验patch，链接精确bootstrap普通依赖；继续对应源码ZIP/LGPL分发。文件部署/搜索策略同时固定，不靠loader内修改搜索目录。 |
| `src/RizToolbox.Runtime/Hooks/NativeEngineFirstInstall.cs`（新） | 读取已加载producer的Ready凭据、核实际模块/字节/保护/行数/代次，永久记Unknown；禁止晚安装或把配置态当发布态。 |
| `NativeEngineNodeContextBridge.cs`、`NativeEngineNodeObserverDispatcher.cs` | 新增生产AdoptPublished入口接已验证凭据和已加载Kernel句柄；保留当前AdoptConfigured作为受控底层接口，不凭其NativeLibrary.Load创建生产加载事实。 |
| `scripts/Build-EngineBootstrapFixture.ps1`、`Test-EngineBootstrapFixture.ps1`（新） | 构建/父进程锁hash、真实system-forward阶段与producer/CLR联合验收；之后再同步Build/Install/安装恢复矩阵的精确新增文件。 |

不要先扩散修改整个Adapter。真实owner/tracker接线在生产采用门稳定后进入[NativeLocalRunLaunchCoordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)和已有IdentityLedger；借用已持有receiver root，投影native owner，再接实际Enumerator代次。

## producer → Kernel → CLR 接口

建议新native接口`RizEngineFirstInstall_GetInfoV4(out,size)`返回完整快照，`GetRowV4(kind,out,size)`可选；这里只是拟定API，V4不是当前Publisher.version2或共享wire3的改名。状态保持Never/Preparing/Ready/DisabledNoSourceWrite/Unknown，配置/发布各一次，无reset、重试或撤销根。

凭据包含固定版本/大小、实际producer/Kernel/UnityPlayer/GameAssembly模块与文件身份、4行kind/slot/target/original/entry/helperEpoch、独立publisherEpoch、前后源字节/原页保护/写序号、实际完成数与所有未恢复责任，以及建立首次准入的启动阶段身份。不能把旧GameAssembly V3的27写拼成UnityPlayer V4。

producer先全部验证/准备静态original→Kernel Configure4→精确核Kernel GetInfo→不可变绑定→逐项发布并核字节/保护/缓存→封存Ready。早期Kernel无observer透明调用；随后CLR读取并复核V4，再核Kernel ABI2/record1/Frame112/Completion32和4行身份，最后Attach一次/dispatcher多sink。Unknown、重复采用、缺行或任何身份变化都不放行；任何源写尝试或保护责任失败不恢复为“无写”。

新Kernel只提供8个engine-node API，旧JudgeContext的59个导出仍服务其他typed hooks与atomicKnown；两模块不会自动共享slot、epoch、Ready或Unknown。生产V4/coordinator须真实聚合两边准入与故障责任，新owned链不表示生产host已经改用Kernel。本批23项包保持23528原字节，不是又一次25套构建。

可直接复用canonical [EngineNodeContext](../../../native/RizToolbox.JudgeContext/EngineNodeContext.cpp)、fixed producer的有界复制/hash/页验证/逐fault状态及静态original方案、[NativeFirstInstall](../../../src/RizToolbox.Runtime/Hooks/NativeFirstInstall.cs)的“仅查已加载模块+重验凭据”模式、dispatcher/账本的捕获责任。复用内部机制不等于复用其旧profile权限。

## 系统解析阶段先做可验收选择

下一包先实现forward-only自有宿主：在loader外显式完成系统WinHTTP绝对路径加载和4名填槽，再加载Importer，验证固定系统module与代理tail槽一致；不发HTTP请求、不接真实游戏。这是具体可执行阶段，不声称已解决真实EXE普通加载时序。

生产方案随后必须明确选哪一条：保留当前DllEntry内系统加载并对这条固定调用及失败/重入做实际验收；或将解析移至已证明不处于初始化回调的阶段。不能只把解析延迟到首个WinHTTP调用就假定loader外，也不能把自有宿主的预解析状态伪装成游戏已具备。无需审完所有Windows内部实现才开始编码，但要把所采用的实际阶段和拒绝条件纳入本包证据。

## 分阶段实际验收

1. **名称与转发**：冻结4个SDK声明/typed ABI、系统module路径/hash和代理槽；真实自有caller传全参数，和直接系统调用对照返回/LastError/输出；不以存错ordinal或仅符号存在当通过。缺名、错模块、自指、重复/重入和解析失败各独立child拒绝。
2. **发布时机与责任**：复用现有TLS/C/C++/DllMain首调用宿主、worker、嵌套、异常、free-node、mode15 Busy和14×4故障；把新forward阶段纳入同条记录，核所有写完成先于Importer首调用。调试器的LOAD/UNLOAD证据与实际入口执行分开，不能只因重复mapping就伪造初始化。
3. **生产形状只读采用**：自有等形映像用新V4→实际Kernel→CLR验证器→同4dispatcher/同账本，逐字段篡改、错Kernel/旧receipt/重复Attach/晚到Unknown都真拒绝；不得在CLR补Configure或补patch。旧legacy57/73继续回归，记录两条身份链各自结果。
4. **固定游戏候选**：前三步有原件后，先生成未安装的固定游戏profile与包，核真实EXE→UnityPlayer→WINHTTP普通依赖及初始化前发布合同；冷启动实测依用户后续授权恢复。没有首次历史/已存在执行者准入证明时继续Unknown，pre-il2cpp_init和UnityMain2都不能自动授权首写。

当前owned Targets必须保留RIZFIXEDOWNEDV1 marker限制；不能移除marker或只换UnityPlayer.dll名宣称游戏profile完成。实际scheduler/async/Start内联清理及取消闭包另有责任；本任务不授EngineCancelled，不开放RequestPlay，也不缩减B01–B19。
