# V5 全 native 自有映像冷启动证据（2026-10-27）

本轮已在一个真实新进程中完成自有五模块加载、44 条普通依赖验证、12 个 Kernel family 配置、36 个源窗口提交和 67 条 RF 身份发布。冷启动进程 PID **44176** 正常退出，`GetInfoV5 → VerifyV5 → GetInfoV5` 成功，之后调用自有 `UnityMain2` 叶桩再读，三份 **91280 字节**快照逐字节相同。结果限于下述冻结候选包；游戏、生产准入和 CLR 全 family 采用均未验收。

`wmain` 首读已为 Ready，只能证明该时刻加载和发布完成。当前自有 TLS 回调与 DLL 入口没有观察记录，不能据此声称“在目标首次 TLS/入口执行前完成了安装”。`startupStatic=1`、静态导入图和相同线程号也不能补足这个时序证据。

## 范围与证据入口

授权及游戏只读约束沿用 [scope](../foundation-engine-production-20261025/scope.md)。本报告仅新增文档，通过读取 JSON、ZIP、源码、raw 和最终 PE 复核已有结果；没有重编译、重启测试进程或操作游戏。原游戏 DLL 仅作为生成器的只读输入，未安装任何产物。

| 证据 | 原件 | 本报告用途 |
| --- | --- | --- |
| E1 | [候选 r3 构建记录](D:/riz-build-cache-next/engine-transport-owned-candidate-r3-20261027/owned-candidate-build.json)、[27 输入冻结 ZIP](D:/riz-build-cache-next/engine-transport-owned-candidate-r3-20261027/owned-candidate-inputs.zip) | 严格构建结果、精确源码与所有构建产物身份 |
| E2 | [native compile profile](D:/riz-build-cache-next/engine-transport-owned-candidate-r3-20261027/native-profile.json)、[生成头](D:/riz-build-cache-next/engine-transport-owned-candidate-r3-20261027/EngineTransportProfile.Generated.h) | owned profile、四个外部整文件 pin、自身实际身份策略、44 条依赖的编译事实 |
| E3 | [Doorstop 构建记录](D:/riz-build-cache-next/engine-transport-doorstop-owned-r1-20261027/transport-doorstop-build.json)、[Doorstop manifest](D:/riz-build-cache-next/engine-transport-doorstop-owned-r1-20261027/doorstop-manifest.json)、[对应源码 ZIP](D:/riz-build-cache-next/engine-transport-doorstop-owned-r1-20261027/doorstop-corresponding-source.zip) | V5 普通导入、最终 winhttp 及对应源码 |
| E4 | [自有目标 manifest](D:/riz-build-cache-next/engine-transport-owned-target-r1-20261027/owned-target-image.json) | 独立名称、原函数与 unwind 复制白名单、自有 TLS/入口/导出、原游戏输入身份 |
| E5 | [冷启动父记录](D:/riz-build-cache-next/engine-transport-owned-cold-r1-20261027/test-result.json)、[实际子记录](D:/riz-build-cache-next/engine-transport-owned-cold-r1-20261027/child-result.json)、[publication.raw](D:/riz-build-cache-next/engine-transport-owned-cold-r1-20261027/publication.raw) | 同一 PID 的真实执行结果和完整发布快照 |
| E6 | [stdout](D:/riz-build-cache-next/engine-transport-owned-cold-r1-20261027/stdout.log)、[stderr](D:/riz-build-cache-next/engine-transport-owned-cold-r1-20261027/stderr.log)、[运行前输入](D:/riz-build-cache-next/engine-transport-owned-cold-r1-20261027/input-before.json) | 子输出与持久化 JSON 一致、无 stderr、运行输入冻结 |

本报告以 E1 ZIP 中的 r3 源码为实现依据。复核时，r3 的 27 个输入中 26 个仍与磁盘文件一致；唯一后续变动为 `Build-EngineTransportOwnedCandidate.ps1` 增加 `ManagedHost` 选项。r3 使用 ZIP 中没有该选项的脚本和 `Host.cpp`，没有 CLR hosting。不能把当前工作树的新脚本哈希写成历史 r3 的输入。

## 实际结果与失败历史

| 轮次 | 实际结果 | 原因或范围 |
| --- | --- | --- |
| [candidate r1](D:/riz-build-cache-next/engine-transport-owned-candidate-r1-20261027/owned-candidate-build.json) | `passed=false`；`buildExitCode=null`；未执行 fixture | [host-build.log](D:/riz-build-cache-next/engine-transport-owned-candidate-r1-20261027/host-build.log) 为 `LNK1118`，生成的 `owned-target.def` 在 `LIBRARY` 语句处失败；尚未运行 Bootstrap 严格构建，因此其退出码为空。随后改为 DEF 逐行写入。 |
| [candidate r2](D:/riz-build-cache-next/engine-transport-owned-candidate-r2-20261027/owned-candidate-build.json) | `passed=false`；Bootstrap build exit **1**；未执行 fixture | [bootstrap-build.log](D:/riz-build-cache-next/engine-transport-owned-candidate-r2-20261027/bootstrap-build.log) 在 `EngineTransportFirstInstall.cpp:590` 的 `const bool near=...` 出现 `C2513`，随后 `C2059`，另有 `C4389` 符号比较警告。r3 将变量改为 `inNearArena`，并显式转换 `U32(raw[0]>>3)`。 |
| [candidate r3](D:/riz-build-cache-next/engine-transport-owned-candidate-r3-20261027/owned-candidate-build.json) | `passed=true`；build exit **0**；`sourceHashesUnchanged=true` | Host 及四个 C++ Bootstrap 单元使用 `/std:c++17 /O2 /W4 /WX`；两份 originals 由 `ml64` 汇编；实际链接、dumpbin 成功。构建本身 `fixtureExecuted=false`，不能视为执行通过。 |
| [owned cold r1](D:/riz-build-cache-next/engine-transport-owned-cold-r1-20261027/test-result.json) | 实际 PID **44176**；exit **0**；`passed=true` | 使用冻结 candidate r3；`started=true`，`timedOut=false`，`killed=false`，输入未变化；子记录 PID 与父记录一致。 |

r1/r2 的 `sourceHashesUnchanged=false` 是失败路径未完成成功后的最终哈希检查，不能单凭此字段断言构建过程中发生了源码篡改。三个历史 ZIP 分别都含 27 个输入；本报告复算每个成员 SHA-256、长度和 ZIP CRC，均与各轮原件一致。构建记录列出的 r1/r2/r3 **10 / 24 / 39** 个产物也全部逐项复核通过。

冷启动子记录的结果如下，未把其他 fixture 的进程计数加到本轮：

| 项目 | PID 44176 的实际值 |
| --- | --- |
| version / publication bytes / magic | `5` / `91280` / `0x3556425550545a52` |
| read / Verify / after / final | `0 / 0 / 0 / 0` |
| state / failure / epoch | `Ready(4)` / `0` / `1` |
| owner / producer / main OSTID | 均为 `42672`，`mainThreadProof=OwnedContract(1)` |
| startup | `DLL_PROCESS_ATTACH(1)`，`startupStatic=1`，`claimed=1` |
| module / typed / site / window / family / RF / resource | `5 / 13 / 23 / 36 / 12 / 67 / 1` |
| configured / bound / retained families | 均为 `0xfff` |
| prepared / attempted / committed windows | 均为 `0xfffffffff`（36 位） |
| source write attempts / completions | `36 / 36`，全部物理窗口共 `380` 字节 |
| outstanding protection / outstanding window mask | 均为 `0` |
| retained resource mask | `1` |
| event count / snapshot sequence / overflow | `327 / 327 / 0` |
| verifyFailures / lastWin32Error | `0 / 0` |
| immutable / LastError preserved / raw persisted | 均为 `true` |
| owned UnityMain2 返回值 | `0` |

`mainThreadProof=OwnedContract` 表示自有测试协议选定的线程，不是游戏主线程证明。Node 四个 family 的 ABI 不带 main-OSTID，发布行中该字段为零；Inline、六个 Scheduler 和 Wait 的相应线程号为 42672，不能把前者的零值误读成八个带线程 ABI 的验证失败。

## 五模块与 44 条依赖

最终 PE 均为 AMD64 `0x8664`。本报告直接解析候选目录中的五个文件，将 raw 的模块哈希、字节数、PE 元数据逐项匹配；五个模块地址不同，持有文件句柄与卷/文件索引均非零。raw 中这些地址和句柄是 PID 44176 当时的数值，进程退出后不能用它们声称句柄仍存活。

| 模块索引 / 文件 | 文件字节 | SizeOfImage | 时间戳 | Entry RVA | Exception RVA | SHA-256 |
| --- | ---: | --- | --- | --- | --- | --- |
| 0 Bootstrap | 205824 | `0x73000` | `0x6ae036bf` | `0x9650` | `0x71000` | `efe4cafbddc16c1e7688f42f53ae5cdfe4df8f4774799c8a95c36facc1b40984` |
| 1 Kernel | 133120 | `0x3a000` | `0x6adf0b3f` | `0` | `0x37000` | `1541feaa8fe7b37d0d3a544fbb315387ea93e3f6b96a4dc955dc76dc9c30e38a` |
| 2 TransportOwnedTarget | 31006120 | `0x1e65000` | `0x68d678af` | `0x17e7c08` | `0x1d21000` | `e1ea816da5cf76d4b19a5b11dd4fb451bebc95f0f0d04aed895d9c619a965633` |
| 3 RizTransportOwnedHost.exe | 16896 | `0x4b000` | `0x6ae036b3` | `0x1d50` | `0x49000` | `9d906de91005805c297373f5fe2d538c0a9e8569e9843ac752fe481127ed1083` |
| 4 winhttp.dll | 70144 | `0x8b000` | `0x6ae03587` | `0x3ad8` | `0x88000` | `3a6443af5e62a489704cebccb522997c3f3f805dca34a07e71d554eb515acb70` |

Bootstrap 与 Kernel 的 TLS、delay-import 目录均为零，Kernel 没有 DLL 入口。五模块都没有 delay-import；自有 target 有重建的 TLS 目录，不属于“没有 TLS”的目标。

Bootstrap 的编译 profile 自身条目采用 `ObserveHeldSelf`：编译时自身 file/hash/header 占位均为零，运行时从持有的自身文件记录实际 SHA、PE 和文件身份；另外四个模块采用固定整文件 pin。raw 的自身 SHA 与最终 Bootstrap 文件完全一致。这个策略解决的是自身最终哈希不能预先嵌入自身的问题，不能单凭“已记录自身哈希”推出独立生产信任锚已成立。Bootstrap 模块 flags 为 `7`，四个固定外部模块为 `15`。

| 有向关系 | 普通导入数 | 本轮验证内容 |
| --- | ---: | --- |
| Host → 自有 target | 1 | 唯一 `UnityMain2` 的 ILT / IAT / EAT |
| 自有 target → winhttp | 4 | `WinHttpOpen`、`WinHttpGetProxyForUrl`、`WinHttpGetIEProxyConfigForCurrentUser`、`WinHttpCloseHandle` |
| winhttp → Bootstrap | 4 | 四个 V5 API 的普通导入及实际导出地址 |
| Bootstrap → Kernel | 35 | 完整固定 Kernel 普通导入集合、唯一符号及实际导出 |
| 合计 | **44** | 图只覆盖五模块之间的指定依赖，不是进程全部系统/CRT 导入 |

本报告从最终 PE 的实际 descriptor、ILT/IAT RVA 和唯一非 forwarder EAT 重新生成 44 行，按五个 little-endian uint32 加 NUL 结尾 ASCII 符号编码，得到 **2336 字节**，SHA-256 为：

`dd83c70a14c09ee5caca3343730ddc0d0ce488d171c51d770b2852cee0a5ddbb`

它与 raw 的 `dependencyGraphSha256` 相同。编译 profile 的 `662953c9f7a380833ea75e1b064a0e506e978daef12aacaf55a8c854d10e44d8` 是保留 Bootstrap 未知自身 RVA 为零的 compile-facts 摘要，明确 `runtimeGraphDigestComplete=false`；不能用它冒充运行时完整图摘要。冻结源码的 `Graph(true/false)` 在运行中还要求实际 IAT 指针恰等于目标模块基址加唯一导出 RVA，禁止 ordinal、重复符号、forwarder 和 delay-import；离线 PE 重算验证图结构和摘要，实际已解析指针检查来自本次成功 native Verify。

最终 Bootstrap 只有四个非 forwarder 导出：

| 导出 | RVA |
| --- | --- |
| `RizEngineTransport_AbortAfterPublication` | `0x9a60` |
| `RizEngineTransport_GetInfoV5` | `0x9b80` |
| `RizEngineTransport_GetVersion` | `0x9c40` |
| `RizEngineTransport_VerifyV5` | `0x9c50` |

Kernel 最终 EAT 为 35 个非 forwarder 导出；Doorstop 保留 82 个公开名称，四个指定 WinHTTP 导出为自有代理入口。其余 78 个公开名称全部可调用不在本轮验证范围。Doorstop manifest 的 `productionLoaderStageVerified=false` 和 `legacyVersionNamesCallableVerified=false` 仍保留；构建成功和本次四符号依赖通过不改变这两个范围限制。

## 自有目标的可执行边界

E4 从固定原游戏 DLL **只读**生成不同名称的 `RizToolbox.TransportOwnedTarget.dll`，不是把原游戏 DLL 直接加载到 fixture。原输入为 31006120 字节，SHA-256 `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`，本报告复核仍一致。facts JSON SHA 为 `260166cd28f603239f68c63595efc6fb1eacf7d382d0befa64fac54d40c657ee`，内部固定事实摘要为 `b85a2e3e5b7a1db5c807ddabfa7409f0f9bd8f549e7603a06dd70c980b6c4bdb`；两者分别是文件与事实 payload 的身份。

自有映像保留 13 个固定完整函数体、27 段选定 unwind，以及完整 93760 行 `.pdata` 的几何布局。其余未列代码以 `CC` 填充、未列数据置零；原 imports、TLS、入口、reloc 被自有内容替换。复核按 manifest 的 41 个复制范围及 25 个自有范围重新建立允许表，所有选定 body/unwind 哈希、未列 section 字节和零 overlay 均通过。

完整 `.pdata` 的原 SHA 为 `31f0d0a8f6058609ec7e0243798eac72d71c0f8fc2ea96dde2431df535c9ec6d`，输出为 `c8631e733da7031828e4034cba4bdba3479ab385237465d624ad5b942ee17590`。它不是逐字节原样复制后不再改动：四个 owned stub 义务共用三个唯一 RF 表项（entry 与 UnityMain2 共用一项），只把这些项的 unwind 指针改到 owned leaf unwind。复核重建后的整张 `.pdata` 与输出完全一致，差异共 9 个字节，没有扩大到 27 个选定 source RF。

| 自有 loader / export 代码 | RVA | 实际字节 | 本轮观察 |
| --- | --- | --- | --- |
| DLL entry | `0x17e7c08` | `b8 01 00 00 00 c3` | 无入口观察记录 |
| TLS callback 0 | `0x17e7708` | `c3` | 无 TLS 观察记录 |
| TLS callback 1 | `0x17e7780` | `c3` | 无 TLS 观察记录 |
| UnityMain2 | `0x17e7c20` | `31 c0 c3` | `wmain` 实际调用，返回 0 |

自有 TLS 目录 RVA 为 `0x1e34188`，回调数组为 `0x1e341b0`，owned leaf unwind 为 `0x1e341c8`。`RizEngineTransportOwnedMarker` 是独立数据导出，固定 16 字节 marker 配合整文件身份校验。保留完整 13 函数体是为了验证源身份和限定窗口写入；本轮 Host 不调用这些函数、Kernel entry 或源 E9，不能声称本次冷启动执行了它们。

## 12 个配置、36 个窗口、67 条 RF

冻结实现先验证五模块与完整函数体，再准备 13 个 originals、一个 near arena 和 23 个 relay，登记 RF，配置 Kernel，最后执行 36 个源窗口事务并发布。raw 的事件序列逐项递增，从 `Entered(1)` 到 `Published(327)`；其中 12 条 family 配置记录先于第一次源写入，36 个窗口各有保护尝试、保护成功、写入尝试、字节进度、回读、恢复、刷新、提交八类事件，各类均 36 条。发布后事件没有继续追加。

12 个 family 是 Node Start/Execute/Release/Detach 四个、聚合 Inline 一个、Scheduler 六个、聚合 Wait 一个，并非 13 次 Configure。每行 `configureAttempts=1`、`configureSucceeded=1`、`flags=7`、`helperUnknown=0`、`retainedClaim=1`，slot 均为 28；覆盖 13 typed、23 site 和 36 window 的精确 mask。聚合 family 的 scalar target/original/entry 为零是 ABI 设计，身份由各 typed 行、mask 与实际 Configure 字节摘要共同约束。

36 个 raw 窗口均满足：全部物理字节写入；`readbackBytes=physicalBytes`；`patchBytes` 与 `observedBytes` 在有效长度内一致；一次保护、一次写入、一次回读、一次恢复、一次刷新均成功；`protectionOutstanding=0`、`lastWin32Error=0`、`failureStage=0`；保护从 `PAGE_EXECUTE_READ(0x20)` 恢复为 `0x20`；`writeSequence < readbackSequence < commitSequence`。其中 13 个是 typed entry 窗口、23 个是 side 窗口；索引 22、24、27 三个扩大零分支窗口 flags 为 255，其余为 63。不能把逻辑观察 PC 与扩大后的物理起点混用。

| RF 类型 | 全局记录索引 | 数量 | 身份与存储 |
| --- | --- | ---: | --- |
| Source | 0–26 | 27 | 自有 target `.pdata` 中精确 source RF，保留完整 CHAININFO 关系 |
| Original | 27–39 | 13 | 最终 Bootstrap 的实际静态 originals RF |
| Relay | 40–62 | 23 | 单个 near table 的 23 个真正登记表项；外层均为 16B CHAININFO |
| Partial primary | 63–66 | 4 | 早期 Dispatch 的 metadata-only 父描述符；位于各 relay unwind+4，内部 partial unwind 位于 relay+192 |
| 合计 | 0–66 | **67** | 发布身份清单，不是 67 次 `RtlAddFunctionTable` |

单个 near 资源实际分配 `8192` 字节，flags 为 15，`tableCount=23`，`rfFirst=40`、`rfCount=27`（含四个 metadata 父描述符），登记/保护均一次成功，根保留为 1。23 条 outer relay chain 中 19 条引用真实 source 父 RF；sites 17–20 的四条 chain 指向索引 63–66。四个父描述符保留真实 Dispatch `BeginRva=0x5a5260` / `EndRva=0x5a5343`，其 `tableEntryIndex=NoIndex`，并不是另一张覆盖 source 的已登记 RF 表。

四个 synthetic partial unwind 的 12 字节均为 `01 00 03 00 00 01 2a 00 00 70 00 00`，SHA-256 为 `72ec9fd9e9a445de5614695f29e88ead679250466574f7b126745e32e450a1ea`。本报告从 source/original 最终 PE 核对前 40 条 RF tuple、unwind 原字节摘要和表项地址关系，再从 raw 的精确 parent tuple 重建 23 个 outer unwind，并重建四个 partial 摘要，27 个 near 摘要全部一致。真实 near 内存身份、lookup、literal、保护与代码摘要的复核由本次 native Verify 执行；raw 不包含整个 near arena 内存转储，离线重建不能冒称再次读取已退出进程的内存。

RF 容量剩余 61 行、资源容量剩余 22 行和事件数组未使用尾部均全部为零。63 个 source/original/relay 表项走真实 `RtlLookupFunctionEntry` 身份核对；四个 partial primary 通过嵌套 CHAININFO 身份核对，不伪称它们能由重叠 source PC 单独 lookup 到。

## 读验读不变性的证据强度

冻结 `Host.cpp` 在同一进程中实际执行 `GetVersion`、第一次 `GetInfoV5`、`VerifyV5`、第二次 `GetInfoV5`，调用 owned `UnityMain2` 后第三次读取。它以两次 `memcmp(..., sizeof(publication))` 比较完整快照，并把第一次原始快照写到 E5 raw。五次 API 调用前均设置 `LastError=0x6244a973` 并核查保留。子 JSON 中 `immutable=true` 和 `lastErrorPreserved=true` 来自这些实际比较，父记录同时检查子 PID、成功形状、完整 raw 长度和输入身份。

冻结 Bootstrap 的 `VerifyV5` 还要求当前快照等于其内部 `readySnapshot`，重新核查 held file、模块 PE、已解析依赖图、所有固定函数体（仅覆盖合法 patch 范围）、原始函数/relay/unwind、资源、36 个写入回执和实际 Kernel Info。成功路径没有更新成功事件或快照字段。不能把它缩减为“只检查 state=Ready”；也不能把本次只验证成功路径扩大为所有失败、abort、并发与故障路径已被该进程覆盖。

E6 stdout 解析后与 E5 子 JSON 完全相同；stderr 为零字节。父记录列出的 41 个运行输入 SHA 和 5 个运行产物 SHA/长度本报告均重新核对通过。三份独立快照没有分别导出成三个 raw 文件；不变性依据是冻结 Host 的实际内存比较加本次通过记录，持久化的 raw 只有第一份。

## 输入与产物冻结

以下 21 个仓库输入来自 candidate r3 的 ZIP，每项均按 manifest SHA、长度、成员路径核对；另外六个外部输入为 Doorstop manifest/DLL、自有 target manifest/DLL 和 Kernel DLL/import library，身份见上文及 E1 原件。

| r3 仓库输入 | SHA-256 |
| --- | --- |
| `EngineOriginals.asm` | `ae7b436fcb69e2df84843f248b6c8d0ab66e3f4446a9a41feb526f3cfc06f40b` |
| `EngineTransportCompileContract.h` | `83c56afbd1881b3fff3fa3d60c25a35fe90c19dcb1b3109f4f7d358c98fddbb6` |
| `EngineTransportFamilies.cpp` | `ef9e52043d957b335c0b4950904c07b03b7886e6acd2b9d3a75919028b276ddd` |
| `EngineTransportFamilies.h` | `2d08cb1b26ec6a4de2b0e8426932a5656ccd61a164eac6ee7ce6e9d495f4bd5b` |
| `EngineTransportFirstInstall.cpp` | `a52d64e0d2dd68731961a1a90cb41d1c2e41ac7374c5018b8974d02279ec4026` |
| `EngineTransportGameProfile.h` | `de2c9593aaf0d4ef11765ebf0521b2ba4a3a855af3f4420365cb497f534c213c` |
| `EngineTransportOriginals.asm` | `3e1a51e7e8a830a792be559f76aebd9cc0d49ef3cc1c89024bf323704b85e361` |
| `EngineTransportOriginals.h` | `647d98cfea0820a2c170c2b3bf6eb46e4cbda3eaccfeb40ccffd48e57e04d406` |
| `EngineTransportProfile.h` | `3f5968bba828a438dc338da8ae6b1f6fd8eecb7dd69395dc39d59b49a7cb09dc` |
| `EngineTransportPublication.h` | `e99c92ab0bcc7dec65dac54f08c42849b10ca61bf415c44b0c6516b08f5e2eb0` |
| `EngineTransportRelays.cpp` | `4e7f10d10f9d328a59622f43adfd5e333a294a3f4a2ed6fb89ca6f080316f781` |
| `EngineTransportRelays.h` | `587066825d688ac344d3a104977b744a7c368b5a8c2b43a7a0c2dfa60d7c87b1` |
| `EngineTransportSourceWrites.cpp` | `42c5fad797f3b8c14df7cf48bd20cabe640e94565a296908cea289c48bf569f5` |
| `EngineTransportSourceWrites.h` | `020356315bec79d7fd07f3dde0e0b89bf2552f0e3021293d0bb875af7f2901ba` |
| `EngineTransportColdFixture/Host.cpp` | `649151b04edc5d32a48e14bfd9aab17e2da0831c163308a7e800af065923697b` |
| `EngineNodeContext.h` | `b863639bb9d8f4b6c2b0bd7b6e2f34cbfa10072633b334b97ca3fa217e6d89a2` |
| `EngineInlineProbe.h` | `c66da8ef6166c9c4289a9c39c2558f1bed57c8430f42e470b2a383e99da94ec9` |
| `EngineSchedulerContext.h` | `380fc40bb712a591eab8055c350a661bc8efab1148685d2099afa7574d8d29b2` |
| `EngineWaitContext.h` | `cb6f542cb9ef5c9961a795b4bda6a3015878f66b1e52b3c28cdf169e8dd4fa00` |
| `Build-EngineTransportOwnedCandidate.ps1`（历史 ZIP 版本） | `3bfd7a644a9b6b20dba8a2049559c366137084fb3e570b15425253491aec6347` |
| `generate-engine-transport-profile.py` | `4b4870579bd917de3b59303e7183a6401ff5357436a787d16855d6d8ed0ae2f6` |

Doorstop 的六个 wrapper 输入、生成 builder 的 SHA 均与其构建记录一致；对应源码 ZIP 共 63 个成员、CRC 通过，其中 manifest 指定的五个 canonical 源文件和四个 V5 文件逐项 SHA 相同；manifest 指定的 10 个最终文件全部核对通过。owned-image generator SHA `6ec099d70cb5d65cdb7f730193e51c3a112caf3349ae9e3a660eb503f3e3ed9f` 与 E4 相同。

| 冻结证据 | 字节或条数 | SHA-256 |
| --- | --- | --- |
| candidate r1 build JSON | 历史失败 | `07a223c5069fe9884b49e779bf9673f1b3a53e5271537d1cd5a085bde7d7713e` |
| candidate r1 inputs ZIP | 27 输入 | `58163cad3cd7ac26e32eead8659038016ac60e29c8486e65fedc51d5ba0a5d83` |
| candidate r2 build JSON | 历史失败 | `a1f91b62ca138e9c77ee2e71575874cc4516cd7ae161a087af3ba31fdad41638` |
| candidate r2 inputs ZIP | 27 输入 | `3bb1a4f9ad7b28147ba1ef250e5b6a49060a097ba365a201769175a921ba0009` |
| candidate r3 build JSON | 通过 | `0d0a55756b35bda4576630d75f5c0648b09f5db25967c5d836f72ccbcd665fee` |
| candidate r3 inputs ZIP | 27 输入 | `d8750a0f79bc3e1dfcfaea7427ff7996b384ab45b1db582b6fd99c248e935754` |
| native-profile.json | 21341 | `e54b16d54e10dd216cf5127a8c0065228da8d429253255bd15f70016bd62c7e3` |
| EngineTransportProfile.Generated.h | 4562 | `0d50ae94a225568f2e0cd5885a17459fdb0011b85bcd3f2be7b63040f94952cd` |
| owned-target-image.json | 22631 | `ceddfccf8ea8535813220902da51dab662bdbc00e0639b08de429bfc4b2d0d0d` |
| transport-doorstop-build.json | 1756 | `bfdfe39aba8e757a1ec5288b6da54518c242142bd32aec1c0189182a352f5461` |
| doorstop-manifest.json | 4669 | `705aa99f38fa36f881833e8dee104285589a3eb6a8f1055be2a6f84abe67e0c7` |
| doorstop-corresponding-source.zip | 63 成员 | `2d9bd832dabb76552f18801efdc022229b0bc165aa9518e468d486472e6bdc57` |
| cold test-result.json | 10851 | `d3e97c5fff762db8e5f71b3de62ea8f0465f3aa170badbd423735b80e7a7a63e` |
| cold child-result.json | 520 | `e53250b078be1dac2c3001fc6543fa75d273d2cf022f3bac05965bc8510a5d48` |
| cold publication.raw | 91280 | `9fea1ebdd5099684953cd9b5db9a3902c3e92e5eeff7517c5d46fa56dac00168` |
| cold input-before.json | 8311 | `488047423b3734e27080ee32f843deabdf2b82826436aec1887f66df527fc9e1` |
| cold stdout.log | 521 | `06dd7e78d7305b47fdaefc628bd1516465d25ed4c4f8ec73ab74e88664b74f4c` |
| cold stderr.log | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| Test-EngineTransportOwnedCold.ps1 | runner | `1b4cb896dea71ee2fc4c7e2929da5ab01191bfabb09fadb1dd7a2f7e4ccf1c7c` |

复跑既有冻结候选包可使用以下命令；输出目录必须事先不存在。本报告没有执行它，也不把未来工作树构建当作 r3 字节复现：

```powershell
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' `
  -NoProfile -File 'C:/Users/71957/Desktop/riz_toolbox/scripts/Test-EngineTransportOwnedCold.ps1' `
  -CandidateDirectory 'D:/riz-build-cache-next/engine-transport-owned-candidate-r3-20261027' `
  -OutputDirectory 'D:/riz-build-cache-next/engine-transport-owned-cold-recheck-20261027' `
  -TimeoutSeconds 60
```

## 结论与后续证据路径

| 证据 → 发现 | 已成立的结论 | 下一步所缺 |
| --- | --- | --- |
| E1–E4 → 最终五模块、固定图与 owned 身份一致 | 可构建、可静态装载的完整 native V5 自有候选链已成立 | 独立生产信任锚与 fixed-game 首次安装准入仍关闭 |
| E5 raw + native Verify → 12 配置、36 完整提交、67 RF 身份在同一进程中一致 | 安装与发布的自有成功路径真实闭环 | TLS/entry 处可持久化观察及跨模块先后顺序，不能由 `wmain Ready` 代替 |
| E5–E6 + frozen Host → 实际读验读、owned leaf 调用后再读均相同 | 成功 Verify 不改变本次整份 publication，LastError 保留 | 当前全 native Host 无 CLR 全采用、全部消费者、故障/abort/并发与生命周期关闭证据 |
| 本轮只调用 owned UnityMain2；[relay machine 独立报告](../foundation-engine-transport-relay-machine-20261027/README.md)另有机器上下文证据 | 两类证据分别覆盖安装发布与 relay 机器语义 | 不可把两个进程的通过项相加，冒称当前 cold 已执行 31 条 relay 路由或完成 Kernel ledger/异常传播验收 |

本轮 `gameExecuted=false`、`originalGameCodeExecuted=false`、`installed=false`、`productionAdmissionVerified=false`、`fullFamilyAdoptionVerified=false`。B01–B19 整体底层仍未完成全验收；本报告只冻结完整 native owned 冷启动的这一次成功与此前两次真实构建失败。
