# 固定四入口系统 WinHTTP 转发源码合同

2026-10-25。本页是作者对新增三源的静态自查，尚未编译或运行，不能作为实测通过凭据。唯一 builder/宿主测试者为 root；本组件未接入现有 Doorstop 或实际游戏。

## 可复用入口与 ABI

Canonical 源为 [riz_winhttp.h](../../../native/RizToolbox.Doorstop/riz_winhttp.h)、[riz_winhttp.c](../../../native/RizToolbox.Doorstop/riz_winhttp.c)、[riz_winhttp_entry.asm](../../../native/RizToolbox.Doorstop/riz_winhttp_entry.asm)。独立构建目标为 `RizToolbox.WinHttpForward.dll`，要求 x64、`/NOENTRY /NODEFAULTLIB /OPT:NOICF`，只链接 Kernel32 和 SEH 所需的 NTDLL import。最终 PE 的导入、入口、TLS、实际导出和代码字节仍须由 builder/独立 verifier 实取验证。

生产形状只导出七名：`RizWinHttpForward_GetVersion/GetInfo/Initialize` 和四个 `WinHttp*`。`RIZ_WINHTTP_FIXTURE=1` 才额外导出 `RizWinHttpForward_SetFault`；非 1 的显式宏值在头中报错。`RizWinHttpActiveSlots` 只有内部对象间的链接符号，不导出。四个公开转发入口直接使用 SDK `winhttp.h` 声明对应的 typed ABI：

| 名称 | 参数与返回 |
|---|---|
| `WinHttpOpen` | 五参 `LPCWSTR,DWORD,LPCWSTR,LPCWSTR,DWORD`，返回 `HINTERNET`。见 [Microsoft 原型](https://learn.microsoft.com/en-us/windows/win32/api/winhttp/nf-winhttp-winhttpopen)。 |
| `WinHttpGetProxyForUrl` | `HINTERNET,LPCWSTR,WINHTTP_AUTOPROXY_OPTIONS*,WINHTTP_PROXY_INFO*`，返回 `BOOL`。见 [Microsoft 原型](https://learn.microsoft.com/en-us/windows/win32/api/winhttp/nf-winhttp-winhttpgetproxyforurl)。 |
| `WinHttpGetIEProxyConfigForCurrentUser` | `WINHTTP_CURRENT_USER_IE_PROXY_CONFIG*`，返回 `BOOL`。见 [Microsoft 原型](https://learn.microsoft.com/en-us/windows/win32/api/winhttp/nf-winhttp-winhttpgetieproxyconfigforcurrentuser)。 |
| `WinHttpCloseHandle` | `HINTERNET`，返回 `BOOL`。见 [Microsoft 原型](https://learn.microsoft.com/en-us/windows/win32/api/winhttp/nf-winhttp-winhttpclosehandle)。 |

Windows x64 的 ASM 每入口只执行 `mov r11,[rip+activePointer]` 和 `jmp [r11+kind*8]`，不建立栈帧，不修改实参、第五参数或调用者返回地址。只占用 volatile R11。拒绝函数具有各自精确类型，返回 NULL/FALSE、设置错误 1114，不读取或改写输出指针。成功转发没有额外 API，直接保留系统目标返回值与 LastError；LastError 是线程属性，API 失败后必须立即捕获，见 [Microsoft GetLastError](https://learn.microsoft.com/en-us/windows/win32/api/errhandlingapi/nf-errhandlingapi-getlasterror)。

## 初始化与当前身份凭据

`Initialize()` 只准在明确位于 loader 外的宿主阶段调用；本批不声明 DllMain 安全。调用 `GetSystemDirectoryW` 后只追加固定 `\winhttp.dll`，限定绝对盘符路径与 260 WCHAR 上限。先以 `GENERIC_READ/FILE_SHARE_READ` 持有系统文件，再以同一绝对路径和 `LOAD_LIBRARY_SEARCH_SYSTEM32`（0x800）调用 `LoadLibraryExW`。这限制系统搜索目录，仍会执行系统模块/依赖初始化；Microsoft 也明确不应从 DllMain 调用，见 [LoadLibraryExW 合同](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibraryexw)。

模块返回后记录并核对 `GetModuleFileNameW` 路径、实际打开文件的 volume serial、64 位 file index 和文件大小，必须与保留句柄一致。还要求实际模块不是自身、AMD64 DLL/PE32+、映像与 export 范围有效。四名分别从实际 EAT 查到唯一名字与有效 ordinal index；拒绝位于 export directory 的 forwarder RVA。使用同一固定名字的 `GetProcAddress` 必须恰等于该 EAT RVA，实际地址通过 `VirtualQuery` 证明为对应 AllocationBase 的可执行 MEM_IMAGE。这里只按名字查询，未使用 ordinal 作为外部合同，见 [GetProcAddress 名称合同](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getprocaddress)。

系统文件句柄和模块引用保留到进程退出，不提供重置/卸载。临时 loaded-path 身份核对句柄用完关闭。C 源不复制 SHA 实现：父进程应独立持有同一系统文件、记录 SHA256 和实际 file identity，再把 host 的 EAT/内存原件与磁盘 PE 对照。

**已有模块复用边界：** 本实现允许合法系统 WinHTTP 已经映射。绝对路径、文件句柄身份和父级 held hash 描述当前文件身份；`GetModuleFileNameW` 后重开路径不能证明此前映像映射的完整历史，亦不排除在持有句柄之前发生的替换历史。现有验证不能把“当前同路径/同文件”扩写成无限 loader 历史、系统依赖闭包或未修改代码全文的证明。host 应保留 `systemBeforeInit` 事实，不能伪称每次 Initialize 都创建新映像。

## 整表发布、永久拒绝与诊断

三张表均为固定四个 typed function pointers（32 字节）：初始拒绝表、独立地址的 poison 拒绝表、零初始化待解析表。四个成功目标先完整填写最后一张私有表，只有全部身份与内存核验成功，才用一次 `InterlockedCompareExchangePointer(initial→resolved)` 发布。禁止把部分槽写入正在使用的表。独立 verifier 必须核对初始/poison 地址不同、初始 DIR64 重定位与 ASM 同一个 active-pointer RVA；仅凭前后两次快照不能证明所有并发中间状态。

Initialize 只有一次 attempt。重复/实际递归调用先原子换成 poison 表，再保留 Unknown 责任；该表永远不会换回初始表，因此外层 CAS 不能重新发布。若 poison 发生在已成功发布后，历史 `publishCount=1/Published` 保留，未来新调用仍拒绝。已经读到 resolved 表的在途调用可完成；本批没有暂停它或回收模块/文件/表。Ready 状态只允许从 Resolving 的 CAS 得到，不覆盖 Unknown。

普通发布前错误进入 `DisabledNoPublish`，不清除已打开句柄、已加载模块或已解析槽。原生 SEH、重复/递归以及准入 gate 冲突进入永久 Unknown。没有等待循环。GetInfo 争用只返回 2，既不等待也不自行 poison；成功 snapshot 后恢复调用者 LastError。它不是跨线程事务快照，失败和 poison 的状态以原子责任字段和实际 active 表覆盖，不能将正在变化的 scalar 视为全部同一时刻。

Info record version 1、1248 字节；`addresses` 在 152，`systemPath` 在 208，`loadedPath` 在 728。它记录当前 state/failure/phase、retained flags、attempt/publish counters、模块/保留句柄、初始/poison/resolved/active 表地址、文件身份、PE 头、四实际地址/页保护、fault selector 和两条路径；GetInfo 输入须 version=1/byteSize=1248。GetVersion=1 与本记录版本一致，不混用已有 EngineNode 的 ABI2/scalar1。

## 固定故障选择与待实测项

Fixture setter 只允许在第一次 Initialize 前选择一次，不接受路径、模块、地址或 callback；第二次 setter 拒绝，不能清空既有选择。故障为：1 BeforePath、2 BeforeOpen、3 BeforeLoad、4 AfterLoad、5 WrongName、6 SelfAddress、7 BeforePublish、8 实际递归 Initialize、9 FileIdentity mismatch、10 BeforeName。仅 5/6/10 具有索引 0..3，其余索引固定 0。WrongName 仍实际调用固定不存在名字的 GetProcAddress；SelfAddress 只把受控候选换成本组件拒绝函数，然后通过真实地址核验拒绝；没有将该候选记作成功系统目标。

构建/宿主应分别验证 production 七导出与 fixture 八导出、无 CRT/heap/logger 依赖、全部未初始化/poison typed 拒绝、成功 Init LastError 哨兵、直连系统与尾转发四类型/第五参数、输出及资源释放、Importer 普通导入四名、线程独立 LastError、固定 fault 矩阵和永久责任。未运行时本页只定义检查，不能写成通过。

本批是将来 Doorstop 可复用的独立系统转发组件；既有 Doorstop 的 alt 路径、完整导出集合、启动时序、实际游戏四入口首次发布、V4、Kernel/旧 JudgeContext 协调、取消和退休责任均没有由本页授予。实际游戏目录/安装文件未修改。
