# 当前游戏的 WinHTTP 静态转发契约

当前 `UnityPlayer.dll` 的普通静态导入只要求四个 WinHTTP 名称：`WinHttpOpen`、`WinHttpGetProxyForUrl`、`WinHttpGetIEProxyConfigForCurrentUser`、`WinHttpCloseHandle`。现有游戏目录代理和本机系统 DLL 都导出这四个名称。固定转发应按名称绑定；代理与系统的相同序号全部指向不同名称，不能把代理序号直接交给系统 DLL。

本批完成四份磁盘 PE 的契约提取，未执行游戏、子宿主或目标代码，未加载系统 WinHTTP，未安装新文件。它提供后续实现的固定名称清单；实际 DLL 搜索结果、转发执行及游戏首次发布时机仍未证明。

## 范围与方法

- `auth`：承接用户的 PC 模组底层开发授权，以及本批只读提取四份 PE 的任务。
- `in_scope`：下面四个绝对路径的文件头、普通/延迟导入、全部导出及 PE forwarder、入口字节、TLS 目录/回调表、版本与 SHA-256。
- `network_profile`：离线，只读本机文件；没有网络请求。
- 输出仅为本目录的[提取脚本](extract-game-winhttp-contract.py)、[完整 JSON](game-winhttp-contract.json)和本文。范围与时间线内嵌本文，未创建额外 scope/timeline 文件。
- 工具路由：已读取 `ida-reverse` 技能；当前没有可调用的 IDA 后端，实际采用 64 位 Python 3.14.0 与 pefile 2024.8.26 的磁盘解析。`idaBackendUsed=false`，不将结果称为 IDA 反编译。报告采用 docs-generator 的普通逆向结构（`flavor=null`）。

### 输入身份

四份输入均为 AMD64（Machine `0x8664`）、PE32+（Magic `0x20b`）；本次 pefile 警告均为 0。入口 RVA 是 PE 头字段，不等同于已确认的业务入口或首个初始化回调。

| JSON 键 | 实际路径 | 文件字节数 | 文件版本 | 入口 RVA | TLS 回调数 | 命名导出数 / PE forwarder 数 |
|---|---|---:|---|---|---:|---:|
| `gameExe` | `D:/SteamLibrary/steamapps/common/Rizline/Rizline.exe` | 666624 | 2022.3.62.28864 | `0x1264` | 0 | 2 / 0 |
| `unityPlayer` | `D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll` | 31006120 | 2022.3.62.28864 | `0x17e7c08` | 2 | 2 / 0 |
| `installedProxy` | `D:/SteamLibrary/steamapps/common/Rizline/winhttp.dll` | 26112 | 4.5.0.0 | `0x2170` | 0 | 82 / 0 |
| `systemWinhttp` | `C:/Windows/System32/winhttp.dll` | 1253704 | 10.0.26100.9549 | `0xa2d60` | 0 | 92 / 0 |

输入 SHA-256：

```text
gameExe        f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e
unityPlayer    43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1
installedProxy 8c6cdbc38836dee87e3368f5de1994d7c0ccebf29e4ce7aba3c0981f9375412c
systemWinhttp  2509edaea64d4ed9e9993b80fa370f431f37117401ef77cf367533a31b82c180
```

脚本结束前重新读取四份文件并比较 SHA-256，`inputHashesUnchanged=true`。这些身份限定本批结论；游戏、代理或 Windows 更新后应重新提取。

## 可直接实现的四个名称槽

`Rizline.exe` 的普通导入包含 `UnityPlayer.dll!UnityMain2`（按名，IAT RVA `0xe240`）。`UnityPlayer.dll` 的普通导入描述符名为 `WINHTTP.dll`，下面四项全部按名导入；没有 WinHTTP 按序号导入，也没有延迟导入。`hint` 是名称查找提示，不是导入序号。

| 名称与系统绑定键 | UnityPlayer IAT RVA | import hint（十进制） | 现有代理导出序号 / RVA | 本机系统导出序号 / RVA |
|---|---|---:|---|---|
| `WinHttpOpen` | `0x1864d60` | 38 | 53 / `0x11a0` | 51 / `0x2d060` |
| `WinHttpGetProxyForUrl` | `0x1864d68` | 30 | 45 / `0x1160` | 41 / `0x33fd0` |
| `WinHttpGetIEProxyConfigForCurrentUser` | `0x1864d70` | 29 | 44 / `0x1158` | 40 / `0x50890` |
| `WinHttpCloseHandle` | `0x1864d78` | 8 | 23 / `0x10b0` | 9 / `0x4d7d0` |

下列是固定名称清单，可作为新代理的四槽输入数据；不是已实现或已执行的转发代码：

```text
WinHttpOpen
WinHttpGetProxyForUrl
WinHttpGetIEProxyConfigForCurrentUser
WinHttpCloseHandle
```

后续实现须导出上述精确名称，系统端也用相同名称解析。四项静态符号在两份 DLL 中均存在；这证明名称覆盖，不证明 ABI、运行时解析地址、返回值、LastError、异常或线程重入保持正确。PE 表也不提供函数原型；本批没有据名称猜测或生成函数签名。

四槽清单的覆盖范围仅为这份 UnityPlayer 的普通 WinHTTP 导入。其他 DLL、插件、运行时 `GetProcAddress` 名称/序号调用者均未枚举，不能据此删除现有代理的其他公开接口或宣称完整 WinHTTP 兼容。

## 实际转发形态与覆盖差异

### 没有 PE forwarder 字符串

四份 PE 的导出表均没有 forwarder 字符串。尤其系统 WinHTTP 的 92 项导出均为本映像 RVA；这不意味着实现没有其他 DLL 依赖。

现有代理的 82 项导出均以磁盘字节 `48 ff 25 f9 6f 00 00` 开始，即带 REX 前缀的 RIP 间接跳转，随后是 `cc` 填充。它是代码跳板，不能在报告里记作 EAT forwarder。四个必要名称对应的间接指针槽如下：

| 名称 | 代码入口 RVA | 指针槽 RVA | 本次可见内容 |
|---|---|---|---|
| `WinHttpOpen` | `0x11a0` | `0x81a0` | `.data` 虚拟范围，未有文件 raw 字节 |
| `WinHttpGetProxyForUrl` | `0x1160` | `0x8160` | 同上 |
| `WinHttpGetIEProxyConfigForCurrentUser` | `0x1158` | `0x8158` | 同上 |
| `WinHttpCloseHandle` | `0x10b0` | `0x80b0` | 同上 |

JSON 中这些槽的 `onDiskSlotValue=null`、`runtimeResolved=false`。本次没有读到进程中的槽值，不能把它们描述成已解析的系统函数地址。代理静态导入了 `GetSystemDirectoryW`、`LoadLibraryW`、`GetProcAddress` 等名称，但仅凭导入表不能证明其调用时机、选择路径或填槽结果。

### 名称集合与序号不能互换

现有代理 82 个命名导出，系统 92 个，交集 65 个；65 个共享名称的导出序号全部不同。两个表共有的 82 个序号位置也全部对应不同名称。例如，代理 `@53` 是 `WinHttpOpen`，系统 `@53` 是 `WinHttpPacJsWorkerMain`。JSON 的 `comparison.sharedNameDifferentOrdinal` 与 `sameOrdinalDifferentName` 保存逐项对照。

系统有而现有代理没有的 27 个名称：

```text
DllCanUnloadNow
DllGetClassObject
WinHttpAddRequestHeadersEx
WinHttpConnectionDeletePolicyEntriesByAppSid
WinHttpConnectionOnlyConvert
WinHttpConnectionOnlyReceive
WinHttpConnectionOnlySend
WinHttpCreateProxyList
WinHttpCreateProxyManager
WinHttpCreateProxyResult
WinHttpCreateUiCompatibleProxyString
WinHttpFreeProxySettingsEx
WinHttpFreeQueryConnectionGroupResult
WinHttpGetProxySettingsEx
WinHttpGetProxySettingsResultEx
WinHttpProtocolCompleteUpgrade
WinHttpProtocolReceive
WinHttpProtocolSend
WinHttpQueryConnectionGroup
WinHttpQueryHeadersEx
WinHttpReadDataEx
WinHttpRefreshProxySettings
WinHttpRegisterProxyChangeNotification
WinHttpResolverGetProxyForUrl
WinHttpSetProxySettingsPerUser
WinHttpSetSecureLegacyServersAppCompat
WinHttpUnregisterProxyChangeNotification
```

现有代理有而本机系统 WinHTTP 没有的 17 个名称：

```text
GetFileVersionInfoA
GetFileVersionInfoByHandle
GetFileVersionInfoExA
GetFileVersionInfoExW
GetFileVersionInfoSizeA
GetFileVersionInfoSizeExA
GetFileVersionInfoSizeExW
GetFileVersionInfoSizeW
GetFileVersionInfoW
VerFindFileA
VerFindFileW
VerInstallFileA
VerInstallFileW
VerLanguageNameA
VerLanguageNameW
VerQueryValueA
VerQueryValueW
```

这 17 项是版本信息相关名称，不能直接交给本次系统 WinHTTP 按名解析。完整 82/92 个导出的名称、序号、RVA、入口磁盘字节均保存在 JSON `images.*.exports`，未用差集清单代替原表。

## 初始化时机还缺的证据

普通静态依赖图只能确定存在 `Rizline.exe → UnityPlayer.dll → WINHTTP.dll` 的 PE 依赖边。本次没有启动进程，不能确认 WINHTTP 模块名在真实加载策略下最终选中了哪份文件，也没有测量系统 WinHTTP、代理、helper 与 UnityPlayer 的实际初始化顺序。

UnityPlayer 的 TLS 回调表有两项，RVA 分别为 `0x17e7708`、`0x17e7780`；另三份输入没有 TLS 目录。UnityPlayer 的 `UnityMain`（序号 1）和 `UnityMain2`（序号 2）同指 RVA `0x808690`，不是 PE forwarder。TLS 表和导出表都没有给出首次调用四个协程目标的时间；把 `UnityMain2` 的公共入口或代理加载位置直接当成安全首发点，仍缺真实调用证据。

| 映像 | 普通导入模块数 | 延迟导入模块数 | 本批可确认的边界 |
|---|---:|---:|---|
| Rizline.exe | 2 | 0 | UnityPlayer + KERNEL32 的磁盘依赖 |
| UnityPlayer.dll | 19 | 0 | WINHTTP 四名称及其余 18 个依赖模块 |
| 现有 winhttp.dll | 2 | 0 | SHELL32 + KERNEL32；没有系统 WinHTTP 的普通静态导入边 |
| 系统 winhttp.dll | 52 | 24 | 多个 API-set、系统及延迟模块；未递归解析依赖闭包 |

JSON 保存每个导入描述符和名称/序号项。没有解析 API-set 映射、SxS/manifest、DLL 搜索或已有模块复用；没有调用 LoadLibrary、系统 WinHTTP API 或初始化回调。因此，本批不能授权在 loader lock 内加载真正系统 WinHTTP、触发其延迟依赖、等待线程或启动托管运行时。后续固定代理需要单独确定系统模块身份与获取阶段，并记录解析完成状态、重复进入/失败的责任，随后才能检验真实游戏的首次四入口发布顺序。

本批未实现生产代理，也未证明游戏首装、GC/句柄根、coordinator、scheduler、取消或退休闭合；此前自有宿主结果不能替代这里缺少的真实游戏证据。

## 证据 → 发现 → 后续路径

本节各条均为静态观察，严重性 `n/a_re`，不作运行时验证结论。`E-001` 的复现命令见下一节，所有 JSON 定位都在同一个固定哈希文件中。

| E-id | source_ref / 内容 | content_hash |
|---|---|---|
| E-001 | [game-winhttp-contract.json](game-winhttp-contract.json)，四输入完整磁盘提取；`observedAtUtc=2026-10-25T06:28:59.597986+00:00` | `afb6397f3e095f8a14e10321bf7b863c703eaa9aefe94f96d75a65d076d1c714` |
| E-002 | [extract-game-winhttp-contract.py](extract-game-winhttp-contract.py)，解析方法、边界检查与输入尾部复核 | `473d3464b8dc2a30f9dd2f1936ea90e7697ebaed89d8ba7a141fb2c69cda77e9` |

| F-id | 发现 | evidence_ids | confidence / status | location |
|---|---|---|---|---|
| F-001 | UnityPlayer 静态 WinHTTP 合同是四个按名普通导入 | E-001, E-002 | high / observed-static | `requiredForwarding`、`images.unityPlayer.imports` |
| F-002 | 相同序号不能用来连接代理与当前系统 DLL | E-001, E-002 | high / observed-static | `comparison` |
| F-003 | 代理是代码间接跳转；进程中的填槽结果未观测 | E-001, E-002 | high / observed-static | `images.installedProxy.exports[].ripIndirectJump` |
| F-004 | 静态依赖边和两个 TLS 项不足以证明安全首发 | E-001, E-002 | high / static-boundary | `images.*.imports`、`images.unityPlayer.tls`、`gameFirstCallTimingVerified=false` |

`P-001`，`path_type=callflow`：磁盘 `Rizline.exe` 普通导入 `UnityMain2`（E-001）→ UnityPlayer 普通导入四个 WinHTTP 名称（F-001）→ 当前代理提供同名代码入口及间接槽（F-003）→ 下一批实现按名称连接身份已确认的系统模块（F-002）。最后一步尚未实现和运行，不能画成已经发生的系统调用；真实初始化、首次入口发布与失败行为是剩余验证路径（F-004）。

时间线：2026-10-25 确认只读四 PE 范围 → 按 ida-reverse 路由后采用 pefile → 在上述 UTC 时间产出 JSON 并确认四输入未漂移 → 复核名称/序号、代码跳板和 TLS 边界 → 写入本文。没有启动验证记录。

## 复现与核对

在仓库根目录执行以下 PowerShell 命令，仅运行提取脚本；它读取四个默认路径并写 JSON，不加载目标 DLL：

```powershell
& C:/Python314/python.exe -u docs/validation/foundation-winhttp-contract-20261025/research/extract-game-winhttp-contract.py
```

输入和输出路径可通过脚本 `--help` 指定。重新提取会更新观察时间，JSON 总哈希随之改变；应比较四个输入哈希及 `requiredForwarding`、`comparison`、TLS/导入/导出字段。脚本会拒绝非 64 位 Python、非 AMD64 PE32+ 或读取期间输入漂移。

本批自检：四份输入身份固定；全量普通/延迟导入与导出入 JSON；四槽清单逐项覆盖；代码跳板与 PE forwarder 区分；未知运行时字段保留 false/null；复现脚本和 JSON 哈希匹配；没有目标执行、安装或游戏修改。
