# 无 Dobby 早期发布器：有限原生依赖复核

2026-10-25。**最终自有宿主已证明：普通静态依赖下，新 Proxy 能在 Importer 首个 TLS/CRT/DllMain 目标调用之前完成固定四入口发布；捕获 Busy 会原生拒绝继续。** 新 Proxy、Recorder 和既有 Publisher 的直接依赖没有 CRT、Dobby、堆分配或迟到加载 API。这不等于整个 loader 调用闭包已安全，更不构成实际游戏首次发布凭据。

本页由 Proxy/Recorder 作者执行有限自查，另行读取最终 PE、归档原始映射和进程结果，不属于独立全审。复核过程只使用磁盘 PE 解析、有限入口反汇编、原始 POD 解码及哈希比较，没有构建、加载 PE、启动子进程、操作游戏或安装。可复核字段和完整直接导入名在 [native-dependency-evidence.json](native-dependency-evidence.json)。

## 1. 最终原件及结论范围

[component-run.json](component-run.json)记录 root 实际观察 session 34351 终态退出 0；本复核没有自行持有或轮询该执行会话。逐项读取归档的 73 份 result，全部 passed，checks 总计 13031；包括 16 种模式、56 个内部故障和一个附加调试器正常进程。[build-summary.json](build-summary.json)保留同一批统计；没有重跑旧的完整 25 套 1617 项。

独立比较 [source-manifest.json](source-manifest.json)的 14 项当前输入及 [source-inputs.zip](source-inputs.zip)每个条目，均与冻结哈希一致。下面检查的是 [raw/build](raw/build)最终 DLL，与 [binary-manifest.json](binary-manifest.json)一致，不用首编或早期候选替代。

| 文件 | 实际 SHA256 | PE 入口 / TLS |
| --- | --- | --- |
| [RizOwnedProxy.dll](raw/build/RizOwnedProxy.dll) | `ce42478040217dcbc9484c825c280dbdb62d500b402f39bd80a8c61d0d635e3b` | `3020` / 无 |
| [RizOwnedRecorder.dll](raw/build/RizOwnedRecorder.dll) | `25af43f4df7244da53070b7659aa238823fa43783e3e2d75856ebeec279b0a05` | `1030` / 无 |
| [RizFixedPublisher.dll](raw/build/RizFixedPublisher.dll) | `3436f4b28483a6200c0daea0532de7a3de92d82efe557126007849f6a47e683b` | 无入口 / 无 |
| [RizToolbox.JudgeContext.dll](raw/build/RizToolbox.JudgeContext.dll) | `01e5171d2e7a824308e7e9053f6959fa7cbea98bb2c2742e1571d8bf75a66f48` | `1EAB0` / callback `1E550` |

Publisher 和 helper 分别与[旧固定发布器归档](../foundation-fixed-publisher-20261025/raw/build)逐字节相同，没有借用另一个同名 DLL 的证据。helper 59 exports、返回 ABI2；当前仍是 scalar record V1，不能把两种版本混为一谈。

## 2. 新组件实际依赖与代码路径

Proxy 的普通导入是 Recorder、helper、Publisher、Kernel32 及 `ntdll!__C_specific_handler`；Recorder 普通导入 Publisher、Kernel32 和同一 SEH handler。Recorder→Publisher 是真实静态边，没有 Publisher→Recorder 的循环。Host→Targets→Proxy 链也从实际 PE 普通导入表重新确认。[父验证器的原始 PE 结果](raw/cases/pe-imports.json)另行保留，未代替本次独立解析。

新 Proxy/Recorder 的 TLS、delay import 和 load-config 目录均为 0。最终 [实际编译命令](raw/build/build-early-fixed.cmd)为 `/GS- /Zl /ENTRY:DllMain /NODEFAULTLIB /GUARD:NO`；入口字节分别直接执行 process-attach 判断及已审源码逻辑，没有 CRT 入口包装。无安全 cookie/CFG 是此小组件的构建限制，不是安全增强或完整利用缓解证明。

直接导入表没有 `LoadLibrary*`、HeapAlloc/HeapFree/GetProcessHeap、malloc/free、memcpy/memset、CRT 模块或 Dobby。结合冻结的 [Proxy.cpp](../../../native/RizToolbox.EarlyFixedFixture/Proxy.cpp)和 [Recorder.cpp](../../../native/RizToolbox.EarlyFixedFixture/Recorder.cpp)检查，定长静态存储、纯 C SHA、volatile 有界复制/清零没有引入 CRT 默认库；没有注册任意回调、创建线程或等待另一线程。这里证明的是这些组件的直接依赖和实际源码形状，未把所有间接目标及 OS 内部实现认作已关闭的全图。

Proxy `Install()` 的顺序确为：实际映像/hash/页面核验→一次四项 PrepareGroup→四次 helper Configure→一次 BindGroup→四次实际 Commit；每步读取实际 Publisher GetInfo 和四源字节/保护。slot 固定 28，Publisher epoch 与四项 helper epoch 分开保存；发布器只绑定精确 ABI2 helper entry。失败终止当前自有进程，不让 Importer 继续；写尝试及保护未恢复责任独立保留。Start/Release 返回 uint8，Execute 完整 uint32，Detach void。

Publisher 没有 PE 入口或 TLS callback，因此不需要伪造其初始化进入/返回。其实际 no-CRT/无 Dobby 依赖及静态 original 的 R11 尾跳边界，沿用并保持[上批最终依赖复核](../foundation-fixed-publisher-20261025/publisher-dependency-review.md)；这批未重新执行该组件的 57 项独立 unwind 矩阵。源入口仍是 14 字节 FF25 patch，original 的 `mov r11,[continuation]; jmp r11` 仅保证四种 typed ABI，不能扩展成所有 GPR 不变。

## 3. loader 顺序观察与未证明的依赖

[补充调试器原始日志](raw/cases/normal-debugger/debug-events.jsonl)实际记录 Recorder 入口/返回为序号 6/8，helper TLS 9/10、helper PE 入口 11/12，Proxy 入口/返回 13/51，随后 Importer TLS 进入 52。Publisher 只记录 LOAD；实际 PE 无入口/TLS，没有编造初始化事件。该进程受断点影响；它补充正常分支的依赖完成顺序，不能替代另外 72 个无调试器进程，也不能外推全部错误分支或线程竞争。

旧 helper 仍导入 VCRUNTIME/UCRT、bcrypt、HeapAlloc、VirtualAlloc、LoadLibraryExW、SRW/condition-variable 等，并有真实 TLS callback。这些导入不等于 Configure 路径必然调用它们；同样，正常 Configure 成功也没有关闭它们的所有可达路径。新组件的 GetModuleHandleW/GetProcAddress/VirtualQuery、文件打开/ReadFile/hash，以及 Kernel32 转发、NTDLL SEH 和 OS loader 内部行为，均未取得完整的传递调用与错误路径证明。因此 `allTransitiveLoaderDependenciesSafeVerified=false`，不能从“去掉 Dobby”推出 loader 全依赖已安全。

## 4. 模式15：永久 Busy 的真实拒绝

独立按固定偏移解码归档 [mapping-final.bin](raw/cases/mode-15/mapping-final.bin)，并与 [process.json](raw/cases/mode-15/process.json)及 [result.json](raw/cases/mode-15/result.json)互核。PID 55316，记录线程 30844；当前自有进程退出 `0xE04C0016`，未超时。映射 SHA256 为 `1f3abbe904446b19435b78285d418a60bdfab24891dfe25cac02463bed0b0c55`。

仅有 `RecorderAttach → CaptureBusyObserved → Reject → Terminating` 四行。事件65为 `result=1,value0=2,value1=1,flags=7,epoch=3`：首个外层 Capture 已真实取得 gate，递归 Capture 真实 CAS 遇 Busy 返回2；外层与之后独立 Capture 返回1。Fail 再执行一次真实 Capture，最终 `captureCalls=4`，infoResult/memoryResult 均0、Publisher Fresh，flags 仍7。Busy 永久责任不会因后续实际快照成功而消失；原生 Fail 保持 Unknown5/failure22，父解析器不是唯一拒绝门。

producerClaim、ProxyEnter、ImporterInitializers、四种原调用、Ready、Prepare/Configure/Bind/Commit 与源写均0；Publisher 四项保护/写/读回/恢复/缓存计数及 outstanding 均0。独立从最终 Targets PE 导出 RVA `1010/1050/1080/10B0`读取32字节，与捕获的四组内存逐字节相等，各页保护为 `0x20`（RX），tuple 区域864字节全零。header 的 proxyModule/importerModule 为0只表示尚未进入/尚未记录初始化；Targets 映像此时已经映射，实际内存快照正是对它的读取。

这是通过固定 mode15 选择器制造的**真实递归 gate 冲突**，没有声称自然双线程竞争已穷尽，也没有把该成功拒绝当成游戏准入证明。

## 5. 下一步实际游戏合同应进入哪些代码

1. **先新增生产 bootstrap，保持 fixture 冻结。** 当前 [Doorstop 构建缝](../../../scripts/Build-Doorstop.ps1)只在原 `il2cpp.init` 前调用 [RizBeforeIl2CppInit](../../../native/RizToolbox.Doorstop/riz_preinit.c)，该函数还会 LoadLibraryExW 加载 helper，最终调用 [RizFirstInstall_BeforeIl2CppInit](../../../native/RizToolbox.JudgeContext/FirstInstall.cpp)。这仍是旧 GameAssembly V3 链，不能顺手把 UnityPlayer 四入口追加到这里。具体下一实现单元应是独立 `native/RizToolbox.EngineBootstrap`（拟新增）的极小 loader producer及 receipt API，再通过 `native/RizToolbox.Doorstop` 和 `scripts/Build-Doorstop.ps1`建立真实普通静态依赖与系统 WinHTTP 转发。先核固定游戏 EXE→UnityPlayer→proxy 的实际命名、放置、全路径身份、转发和 TLS/CRT 完成顺序；不能把当前 Owned 文件改名视为完成。既有[启动研究](../foundation-engine-binding-20261025/engine-installation-research.md)已说明 UnityMain2/pre-il2cpp_init 均不足以抹除此前调用。
2. **新增游戏映像 profile 和首次发布凭据。** [FixedPublisher.cpp](../../../native/RizToolbox.FixedPublisher/FixedPublisher.cpp)的 Prepare 当前强制 `RizFixedOwnedTargets.dll`、只读 `RIZFIXEDOWNEDV1` marker 和四个 owned exports；它是自有宿主组件，故意不接受 UnityPlayer。下一生产 API 应独立固定 UnityPlayer/GameAssembly/helper 的 hash/RVA/完整指令边界与 original unwind，记录全部 row、ABI、slot、publisher/helper epoch、首次准入历史、实际字节和保护责任，并对每次失败永久拒绝。不得只替换模块名、取消 marker、复用 fixtureReady 或把旧 V3 receipt 拼成 V4。OS/查询/文件 I/O 和 helper Configure 的剩余初始化依赖也须纳入这一门，证据不够不做游戏首写。
3. **生产凭据完成后再只读采用。** 具体 CLR 入口是 [NativeEngineNodeContextBridge.AdoptConfigured](../../../src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.cs)，其当前 internal controlled 采用不授 V4。后续先验证新生产 receipt 再 Attach，拒绝旧 ABI/helper、重复或缺行凭据；实际 receiver root→native owner→Enumerator/handle 的关联进入 [NativeLocalRunLaunchCoordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)及 [NativeEngineCoroutineIdentityLedger](../../../src/RizToolbox.Adapter/AudioContent/NativeEngineCoroutineIdentityLedger.cs)，不能把受控 provider 配对当作真实 coordinator 已接通。[下一接入计划](../foundation-engine-binding-20261025/next-integration.md)保留对应身份链与取消责任。

以上三个单元目前均未取得游戏首次接入验收。本批不签发 production V4，不验证真实系统 WinHTTP，不完成 scheduler/async/Start 内联清理闭包，`EngineCancelled=false`、Pending 保留、RequestPlay 关闭。完整 B01–B19 范围不缩减；当前无安装、无游戏执行。
