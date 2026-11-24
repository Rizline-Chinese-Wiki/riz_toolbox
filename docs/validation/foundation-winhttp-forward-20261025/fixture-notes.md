# 四名称 WinHTTP 转发 fixture 合同

本批新增自有宿主和普通静态导入调用者，验证 `RizToolbox.WinHttpForward.dll` 的四个真实 SDK ABI。root 唯一构建与运行；本页是实现合同，尚未声明编译或实际矩阵通过。不执行游戏，不修改网络/代理设置，不发 HTTP 请求。resolver 在 Host 显式调用的 loader 外阶段初始化，不能据此宣称在 DllMain 中动态加载系统 WinHTTP 安全。

## 源码与 CLI

- [WinHttpForwardFixture.cpp](../../../native/RizToolbox.WinHttpFixture/WinHttpForwardFixture.cpp)：CRT 自有 Host；动态加载传入的绝对自有代理路径，系统 DLL 只由 canonical resolver 初始化。
- [WinHttpFixtureApi.h](../../../native/RizToolbox.WinHttpFixture/WinHttpFixtureApi.h)：真实 SDK typed aliases、固定调用记录、定长 SHA-256、受控参数和捕获助手。
- [Importer.cpp](../../../native/RizToolbox.WinHttpFixture/Importer.cpp)：`RizWinHttpOwnedImporter.dll`，四 WinHTTP 名称普通导入；导出 `RizWinHttpImporter_GetSnapshot` 和 `RizWinHttpImporter_RunOutsideLoader`。
- canonical API 为 [riz_winhttp.h](../../../native/RizToolbox.Doorstop/riz_winhttp.h)，本宿主没有复制 resolver 或修改生产 C/ASM。

正常生产 Host 参数：`--forward <absolute-production-DLL> --output <absolute-result.json> --importer <absolute-Importer-DLL>`。fixture DLL 另加 `--fixture`；故障 child 另给 `--fault <1..10> --fault-index <0..3>`，不加载 Importer。只有 stage 5/6/10 对 index 0..3 有逐名称含义，其余规范 index 为 0。生产 DLL 不得导出 Setter；fixture 选择一次后重复 `SetFault(None,0)` 必须返回 2、不能清空原选择。

Host 退出 0 表示内部检查完成通过；1 表示断言/异常失败，2 表示参数或原件 I/O 失败。所有结果含真实 `actualProcessId` 和保存的 `mainThreadId`，父应与实际 child PID、profile 和 selector 独立绑定。Host 不链接 winhttp.lib，也不自行 LoadLibrary 系统 DLL；Importer 链接相同 basename 的自有 importlib。root 决定 `/MD` CRT 链接，纯定长 SHA 无 bcrypt 链接要求。

## 实际阶段与比较

1. 加载自有代理后保存 Fresh Info；四个未初始化 typed 入口分别以受控参数调用，要求 NULL/FALSE 与 `ERROR_DLL_INIT_FAILED=1114`，输出保持有界。
2. `Initialize()` 前设置 LastError 哨兵，成功应恢复同值。读取 Ready Info 的实际 systemModule，才 GetProcAddress 得到四个按名直调函数；模块实际路径、file volume/index 和四个 RX 地址与 resolver Info 对照。加载前的 `GetModuleHandle(winhttp.dll)` 只保事实，不假定原进程一定未加载。
3. 同一进程依次 direct → forward，分别 `WinHttpOpen(Agent, NO_PROXY, NULL, NULL, 0)` 得到两个真实 session；句柄只核各自成功/后续关联，不按地址相等判断。对各自 session 执行零 autoproxy 选项的无效本地 URL 负例、只读 GetIE，再各自 Close；另比较 Close(NULL)。
4. Open 的第五个栈参数另用 `0x40000000` 作非零探针。记录实际 direct baseline；若平台接受则正常关闭相应真实句柄，不预设该 flags 一定失败。
5. resolver Ready 后 LoadLibrary 自有 Importer；真实 TLS、C initializer、C++ initializer、DllMain 每阶段只执行 Close(NULL) 和 GetProxyForUrl(NULL, invalid text, zero options) 两个同步拒绝负例。它们不是“每阶段四 API”或任意 WinHTTP API 的 loader 安全证明。
6. LoadLibrary 返回后，Host 调用 Importer `RunOutsideLoader()`，在显式 phase 11 真实执行 Open/GetIE/invalid Proxy/Close 四 API；此时才包含配置读取与有效 session 创建。
7. 三个自有 worker 同时释放起跑，各自执行四轮 direct/forward 比较，用独立 LastError 哨兵和独立真实 session，记录 OS TID。并发期间 API/盐为只读，线程各持结果容器，全局调用序号由 Interlocked 生成，汇总在 join 后进行。
8. 正常末尾重复 Initialize，要求永久 Unknown、activeTable=poisonTable，四个未来新调用再次 NULL/FALSE+1114。实际系统模块、held file、已解析表及地址保持；不复用槽、不称取消在途调用。故障 child 也不得生成 Ready/publication，始终保实际部分 resolver 结果和拒绝调用原件。

成功 API 的 LastError 没有附加业务含义；比较仅要求同哨兵、同受控输入的 direct/forward 观察原样一致。BOOL 按 32 位记录，不转为 AL；Open 记录完整指针；GetProxyForUrl 的四参和 Open 五参均保原始标量。

## 网络和隐私边界

`WINHTTP_AUTOPROXY_OPTIONS` 全零：flags/autodetect/reserved/auto-logon 为 0、AutoConfigUrl 与 reserved pointer 为 NULL；URL 是固定 `::riz-owned-invalid-url::`。没有提供 PAC URL、WPAD、HTTP request/credentials，测试不调用 Connect/OpenRequest/SendRequest。参数的意义与可能下载机制参见 [GetProxyForUrl](https://learn.microsoft.com/en-us/windows/win32/api/winhttp/nf-winhttp-winhttpgetproxyforurl) 和 [AUTOPROXY_OPTIONS](https://learn.microsoft.com/en-us/windows/win32/api/winhttp/ns-winhttp-winhttp_autoproxy_options)；这里的故障/行为仍须实际原件验收，不能用文档预填成功结果。

GetIE 只读当前配置，不序列化 PAC/代理/绕过地址。每个字符串只记录 pointer、NULL 与否、UTF-16 长度、有界完整性及进程内未写盘盐的 SHA-256；盐只用于本进程 direct/forward 比较，不作为安全密钥或跨运行稳定标识。输出来自系统时正常 GlobalFree，并记录次数及失败数。原始用户字符串、环境快照和注册表内容均不写日志。

## 固定原件布局

Host 主 JSON `schemaVersion=1/kind=winhttp-forward-fixture` 包含 arguments、initialize、实际模块身份、四地址页属性、infoSnapshots、calls/pairs、ImporterSnapshot、callsSnapshot 与失败明细。所有文件引用是同 case 输出目录的 basename，父应拒绝 path escape 并重新算 SHA，而不只接受 Host 的 `passed/equal`。

| 原件 | 固定布局与内容 |
|---|---|
| `*.info-<label>.bin` | 每次 GetInfo 返回的精确 1248 字节；before/initialized/after-workers/repeated 为正常路径，fault 只有 before/initialized。JSON 另含每次调用的返回与 LastError 前后、四种表地址的实际四槽读取；父从 canonical header 自行解码并对照 |
| `*.calls.bin` | JSON calls 同顺序的纯 Row 串，每行 320 字节；route/pair/label 是 JSON 附加语义，不写进 Row |
| `*.importer.bin` | Snapshot 10272 字节：前 32 字节 version/size/phaseMask/rowCount/checks/failures，随后 32×320 Row 容量；实际初始化 8 行、loader 外 4 行，共 12 行，mask `0x8f0` |

Row：6×u32 为 version/byteSize/phase/kind/threadId/seed（0..23）；sequence Q24、apiAddress Q32、五个原始参数 Q40..72、rawReturn Q80；LastError/open 四 u32 为 88..103；autoFlags/autoDetectFlags/autoReserved/autoLogon 四 u32 为 104..119；autoConfigUrl Q120、autoReservedPointer Q128；输出 accessType/autoDetect/freeCount/freeFailures 四 u32 为 136..151；三个 PrivateString 从 152 开始，每个 56 字节（pointer Q、present/length/complete/reserved 四 u32、SHA256 32 字节）。header 有大小/偏移 static_assert，另已用独立 ctypes 声明计算过，不构成 native 编译成功证据。

phase：1 未初始化、2 Host direct、3 Host forward、4/5/6/7 真实 TLS/C/C++/DllMain、8/9 worker direct/forward、10 poison、11 Importer 显式 loader 外。kind：0 Open、1 GetProxyForUrl、2 GetIEProxyConfigForCurrentUser、3 Close。Importer 与 Host 分别有自己的实际序号源；父按 route/原 snapshot 区分，不能把两者序号混作一个全局时间线。

父应独立核生产恰好 7 个导出、fixture 多一个受控 Setter、无 PE entry/TLS/CRT/Dobby，以及真实 Importer 普通四名称导入。再以 held 二进制、实际进程、1248 字节 snapshot、320 字节行和 10272 字节 snapshot 为依据核原始地址/参数/输出/LastError/状态；系统 DLL 路径与身份由父本机 GetSystemDirectory 和文件句柄独立得出。未覆盖真实系统网络请求、自然并发 Initialize 或捕获旧表在途调用的所有时序，也不签发生产 bootstrap、V4、GC、scheduler、取消或退休凭据。

## 首轮实际失败与修正候选

首编因本机 SDK 的 C5105 失败，尚未启动 child；builder 加入兼容选项 `/Zc:preprocessor-` 后，r2 已编译两种代理、Importer 与 Host，但 production-normal 在加载 Importer 后以 `0xc0000409` 退出。root 调试原件记录 CFG 子码 `0x0a`。实际 r2 COFF 含 `.CRT$XCU` 的 `api$initializer$` 与 `dynamic initializer for api`：它从四个 `__imp_WinHttp*` 读取地址并写入零初始化的全局 Api，而 TLS 已先进入 Run。因此原实现的全局表依赖晚于 TLS 的 CRT 初始化。原始源码保存在 [importer-before.cpp](history/importer-before.cpp)，SHA256 `c08b11acb6e4319d90d4ca2d60658e04dbe6c92b2447cae01aaee4d7dffa66c5`。

r3 改为每个 Run/RunOutside 在栈上直接从 IAT 构造 Api；root 实际 child 不再出现 SEH，产出完整 JSON，但仍 exit1，仅 Importer 阶段合同失败。实际 COFF 只有 `.CRT$XLB→Tls`，C/C++ initializer 的 namespace 内 const 指针及函数已被优化器丢弃，不存在 `.CRT$XIC/.CRT$XCU`。这说明源码中的 allocate 声明本身不足以证明实际执行阶段。r3 原始源码保存在 [importer-before-r4.cpp](history/importer-before-r4.cpp)，SHA256 `627a2925b53b52ff7f933ee86837b5a62e9fa9b6bf870f296453a1dfcbb35382`。

当前候选仅把两个槽改为具名 `extern C` 符号 `RizWinHttpImporterCInitializer/RizWinHttpImporterCppInitializer`，并用 `/INCLUDE` 强制保留；TLS、CFG、12 行阶段合同均不变。下一次构建必须用实际 COFF/PE 确认 XIC/XCU 槽及函数均保留，并由原始 snapshot 证明 4/5/6/7 和显式 11 阶段真实发生。这里尚未宣称该候选编译或运行通过，r2/r3 失败原件保留。
