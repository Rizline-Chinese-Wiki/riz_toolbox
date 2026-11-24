# 首次安装屏障：现有启动路径与候选改造

2026-10-24，只读研究。没有修改 Loader、Runtime、native、依赖缓存、安装包或游戏，没有构建或启动游戏。结论：**现有路径没有可验证的首次指令写入凭据；本批 admission gate 只能保护已经安全安装的父入口。**

## 已核实的启动顺序

| 证据 | 实际行为 | 证明范围 |
|---|---|---|
| [Entrypoint.cs:8](../../../src/RizToolbox.Loader/Entrypoint.cs#L8)、同文件 :17 | Doorstop 托管入口设置程序集解析，然后调用 Bootstrap。 | 没有 native 首次写入授权。|
| [Bootstrap.cs:29](../../../src/RizToolbox.Loader/Bootstrap.cs#L29)、:33、:43 | 选择 GameAssembly 指纹 profile，创建并启动 RuntimeBridge。 | 指纹身份成立不意味着没有执行线程。|
| [RuntimeBridge.cs:21](../../../src/RizToolbox.Runtime/RuntimeBridge.cs#L21)、:29、:31、:35 | 新建后台线程；只等待 GameAssembly 映射，随后创建 API 对象并 DobbyHook `il2cpp_runtime_invoke`。 | 没有与原初始化线程握手，也没有阻止其他线程首次进入被改写范围。|
| [Install.ps1:54](../../../scripts/Install.ps1#L54)、:67 | 使用固定包的 winhttp/Dobby，把 Doorstop target 改为本项目 Loader。 | 不能据此推导 Doorstop 是 pre-init。|
| Doorstop v4.5.0 [src/windows/entrypoint.c:142–154](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/src/windows/entrypoint.c#L142)、:215 | 通过 UnityPlayer 的 GetProcAddress IAT 入口，把 `il2cpp_init` 的解析结果重定向给 wrapper。 | 这是一个具体可改造的 native 初始化入口；没有封锁任意直接调用。|
| 同版本 [src/bootstrap.c:314–320](https://github.com/NeighTools/UnityDoorstop/blob/33dab9a6733862eb81869ff08431d9478b28784b/src/bootstrap.c#L314)、:311 | wrapper **先调用原 il2cpp.init，返回后才启动 CoreCLR/托管入口**；managed startup 调用本身同步。 | 当前 Entrypoint 位于原 init 之后；仅把后台安装改为同步，仍不能成为 pre-init。|

本地 `.tools/bepinex-789/.doorstop_version:1` 为 4.5.0。实际 `winhttp.dll` 为 26,112 字节，SHA-256 `8c6cdbc38836dee87e3368f5de1994d7c0ccebf29e4ce7aba3c0981f9375412c`。已在内存中读取[官方 Windows release ZIP](https://github.com/NeighTools/UnityDoorstop/releases/download/v4.5.0/doorstop_win_release_4.5.0.zip)，其 SHA-256 为 `7bb953e8d883c8bde76ced96f6d0e45660ad6e0151880d8ab5856bf4f532b147`，其中 `x64/winhttp.dll` 与本地文件逐字节相同。发布页面为 [v4.5.0](https://github.com/NeighTools/UnityDoorstop/releases/tag/v4.5.0)，tag 解析至 `33dab9a6733862eb81869ff08431d9478b28784b`。这建立了发布身份及对应源码顺序；没有声称本机已重新构建并复现该二进制。

官方源码、ZIP 和 API tree 仅通过网络读取到工具内存，未落入 `src/tests/native/scripts/.tools/packaging`，也未改动 445 项 source manifest 输入。本文只保存出处、哈希与分析。

## 选择与推荐

| 路线 | 可以解决 | 尚欠证明与代价 |
|---|---|---|
| 把现有 RuntimeBridge 后台线程改为同步 | 缩短 Doorstop managed 入口到安装之间的窗口。 | 仍然 post-init，且没有全线程阻挡；不推荐作为首次安全安装方案。|
| **独立 native pre-init wrapper，保留 post-init 托管启动** | 在上述 GetProcAddress 重定向处，给固定模块实例提供原 init 之前的同步 native 回调；避免依赖第一次 tick 或 CLR 回调来取得启动顺序。 | 需要维护并固定 Doorstop 分支、native 安装器与启动证据协议；pre-init 本身仍不是全局屏障。必须另外证明其他线程/其他调用路径不能执行目标范围，否则在任何目标写入前拒绝。|
| 受控启动调试器的 OS 调试事件停顿 | Windows 文档定义的 pending debug event 可提供整个被调试进程的执行停顿，强于枚举线程计数。 | 当前仓库没有该启动器；须处理受控启动、首次 DLL load/TLS/DllMain 时序、远端原生代码准备、发布和恢复协议。不能把 LOAD_DLL 通知与“任意时点安全运行 LoadLibrary/Dobby”混为一谈。|

推荐下一步实现第二条的**独立 native 启动契约 fixture**，再决定是否承担生产 Doorstop 分支。具体改造点是原 wrapper 调用 `il2cpp.init` 之前插入 native `BeforeInit`，原调用后执行原有 CoreCLR bootstrap；不要直接把现有全部托管 Bootstrap 前移。`BeforeInit` 只做固定映像身份、明确安装范围、native 状态机及可审计授权，不读尚未初始化的 IL2CPP 对象，也不假定类型枚举可用。首次安装凭据须绑定映像基址/大小/文件身份、范围、OS 线程、epoch、实际启动阶段和结束状态；普通插件不能创建它。

这是一条可具体实施的候选入口改造，**不是已证明的生产写入授权**。如果无法建立覆盖其他线程和早期入口的阻挡合同，BeforeInit 必须输出“无凭据/未写入”，原游戏正常继续；不能将“目标暂时没调用”“计数为零”“这是主线程”或“原 init 还没返回”升级为安全证明。第三条提供真实全进程停顿的独立研究方向；[Windows 调试器主循环文档](https://learn.microsoft.com/en-us/windows/win32/debug/writing-the-debugger-s-main-loop)明确描述事件期间所有目标线程停顿，但当前尚未实现此路线。

## 最小实现与验收步骤

1. 新建受控 native host + fake UnityPlayer/GameAssembly DLL，复现 GetProcAddress→wrapper→原 init→post-init startup。真实 original init 必须一次；其返回值、SEH code/payload、C++ typed payload 不变；BeforeInit/PostInit 顺序由 native 事件和固定地址验证，不能只读托管布尔值。
2. 在 fake DLL 的 TLS/DllMain、原 init 内部、原 init 前后分别安排 target 调用；另创建已经运行的 worker，以及在 barrier 获取期间产生的新 worker。故意安排会绕过解析入口的直接函数指针调用。所有可能运行目标的路径都必须被可靠阻止，或者使凭据获取失败并证明没有发生目标写入。
3. 在成功场景中，把调用线程持续挡在确定阶段；构造真实已映射且尚未执行的目标范围，先核对原字节，再安装 native parent。验证安装前/中/后分别发生的调用次数、完整参数、异常与递归；发布过程不可出现半条指令被执行。释放前读回完整目标与 trampoline/relay；任何未知结果保留责任、停止第二次写入。
4. 原 init 内部若可自行触发目标，不能把安装延至原 init 返回后。worker 若可越过 BeforeInit，必须新增真实阻挡机制或放弃该路线；不允许用 thread enumeration + SuspendThread 的瞬时结果补作全局证明。fixture 的所有拒绝/超时场景都必须在写入前结束授权，并恢复未改写的原启动行为。
5. fixture 通过后才接 Loader：现有 post-init host/主线程能力组合保持独立；生产入口安装器先凭 native 启动证据建立父入口，再使用本批 admission gate 管理后续原方法内段发布。首个 parent 安装、后续 gate 和资源回收是三份不同证据，不能互相替代。

本次未发现可以直接复用的现成 pre-init 全局屏障。未运行新 fixture、未修改 Doorstop、未获得生产首次安装凭据，也未证明所有游戏线程都经由该 wrapper。Evidence → Finding → Path：固定 release 身份与实际源码顺序排除了“当前 Doorstop managed 入口已经 pre-init”的假设；下一步应先验证 native 启动合同，而非继续在首次 tick 中写入目标。
