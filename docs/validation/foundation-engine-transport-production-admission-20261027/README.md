# 生产首次准入：已证事实与剩余条件

2026-10-27。固定游戏 V5 的五模块身份、最终 44 条磁盘依赖和 P0 源字节合同已有证据；**本次真实进程第一次写入前的执行历史、持续排他和主线程来源尚无可签发凭据，生产签发分支也未实现。** 当前严格生产候选属于 facts-only：核验事实后拒绝配置和源写入。Owned 冷启动、最早 TLS 采样及机器传输结果不能转为生产许可，修改一个 bool 也不能完成生产接入。

## 范围与原件

本页采用 docs-generator，flavor=null，只读复核当前 Bootstrap/Profile/Doorstop/CLR 源码及既有结果，重新计算下表四份原件 SHA256。范围承接[既有本地只读研究授权](../foundation-engine-binding-20261025/scope.md)，本次仅新增本页。用户“先继续代码，实测稍后”仍有效；本文没有构建、加载或执行 PE，没有启动、操作或安装真实游戏，没有修改 gate、代码或脚本。下文真实冷启动是待安排步骤，本文未执行。

| 证据 | 原件与范围 | 本次复核 SHA256 |
|---|---|---|
| E-001 | [P0 固定磁盘合同](D:/riz-build-cache-next/engine-transport-production-contract-r1-20261026/production-transport-facts.json)，1,537,011 字节；876,709 项检查 | `260166cd28f603239f68c63595efc6fb1eacf7d382d0befa64fac54d40c657ee` |
| E-002 | [生产原生构建 r2](D:/riz-build-cache-next/engine-transport-production-native-r2-20261027/production-native-build.json)，14,891 字节；实际严格构建及输入归档 | `0622d8538d238c75180901006787282a01a3dfac2ce41494a20b0753f2c42143` |
| E-003 | [最终 PE 独立验证 r1](D:/riz-build-cache-next/engine-transport-production-final-validation-r1-20261027/final-native-validation.json)，11,680 字节；5 模块、44 条最终磁盘边 | `5ef30b7936f2c71c37aed42de1324f529a1f4a906e42af23bfdc69b5052e5e21` |
| E-004 | [Owned 最早观察 r3](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/test-result.json)，12,361 字节；PID 20296、287 检查、退出 0 | `707b026aeecbe226cb981dbfd1851376c083b6e937ce3c08dec33a60932b623c` |

这些路径是本机既有离线原件。第三方持有相同文件后，例如可用 `Get-FileHash -LiteralPath 'D:/riz-build-cache-next/engine-transport-production-final-validation-r1-20261027/final-native-validation.json' -Algorithm SHA256` 复核 E-003，其余文件按表中实际路径核对；本页没有重跑其生成器或验收程序。2026-10-26 的 P0、2026-10-27 的生产构建/验证和 Owned 观察分别保留原时间与范围，不能合成一次生产运行。

## 当前启动链实际做了什么

磁盘普通依赖为 `Rizline.exe → UnityPlayer.dll → winhttp.dll → RizToolbox.EngineBootstrap.dll → RizToolbox.EngineNodeKernel.dll`。四组指定边分别是 UnityMain2 的 1 条、WinHTTP 的 4 条、V5 API 的 4 条和 Kernel 的 35 条。E-003 的最终图摘要为 `9e8fd775c669be6528df7fa28c30d8cbcbe62284b6667b2e748414743d9ef05c`；它明确记录 `targetImagesLoaded=false`、`nativePublicationReadyProved=false` 和 `productionAdmissionGranted=false`。

当前生产入口在[Initialize 第 663 行](../../../native/RizToolbox.EngineBootstrap/EngineTransportFirstInstall.cpp#L663)验证真实已加载模块、原始完整函数体及源展开数据，再准备自身的原函数桩描述；[第 672 行](../../../native/RizToolbox.EngineBootstrap/EngineTransportFirstInstall.cpp#L672)在 Configure、near 分配和源写入之前拒绝。以下拒绝互相独立：

| 位置 | 实际条件 | 对下一实现的含义 |
|---|---|---|
| [Profile.h:42](../../../native/RizToolbox.EngineBootstrap/EngineTransportProfile.h#L42)、[52](../../../native/RizToolbox.EngineBootstrap/EngineTransportProfile.h#L52) | `ProductionFirstInstallAuthorized=false`，静态断言要求关闭 | 生成 hash/图不授准入 |
| [FirstInstall.cpp:674](../../../native/RizToolbox.EngineBootstrap/EngineTransportFirstInstall.cpp#L674) | 只有 Owned 设置 `mainThread` 及 Owned proof；676 行生产分支直接 Reject | 改掉上一 bool 后仍不会生产发布；尚缺真实主线程及首次窗口的签发路径 |
| [Families.cpp:483](../../../native/RizToolbox.EngineBootstrap/EngineTransportFamilies.cpp#L483) | `FixedGame` 独立返回 `AdmissionUnproven` | 配置描述符不是准入凭据 |
| [SourceWrites.cpp:281](../../../native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.cpp#L281)、[GameProfile.h:10](../../../native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h#L10) | profile 2 又受 facts 头的 false 门约束 | P0 磁盘事实不能授源写许可 |
| [CLR FirstInstall.cs:14](../../../src/RizToolbox.Runtime/Hooks/NativeEngineTransportFirstInstall.cs#L14)、[54](../../../src/RizToolbox.Runtime/Hooks/NativeEngineTransportFirstInstall.cs#L54) | `ProductionStartupAdmissionKnown=false`，`RequireReady()` 独立拒绝 | 托管指纹齐全也不能采用生产 V5 |

E-002 的实际 [bootstrap.pe.txt](D:/riz-build-cache-next/engine-transport-production-native-r2-20261027/bootstrap.pe.txt) 第 181–197 行保留模块、文件、VirtualQuery、RtlLookupFunctionEntry 等读取导入，没有 VirtualAlloc、VirtualProtect、FlushInstructionCache 或 RtlAddFunctionTable。结合上述 constexpr 拒绝，可判定该最终 PE 的写入路径不可达并被优化移除。**严格构建通过证明源码能编译及最终 PE 的事实合同成立，不证明生产配置/写入分支已经运行；这个旧二进制不能靠配置开关变成可写版本。** 后续若实现真实生产准入，必须重新构建并生成五份最终指纹及 CLR profile。

[Doorstop BeforeAttach:145](../../../native/RizToolbox.Doorstop/riz_engine_transport.cpp#L145)消费的是 Bootstrap 已有结果；[163 行](../../../native/RizToolbox.Doorstop/riz_engine_transport.cpp#L163)在无责任的 DisabledNoSourceWrite 下允许继续原 Doorstop。它不能为更早的 Bootstrap 首写补签权限。旧 pre-il2cpp_init 更晚，也不能抹除 Unity 已发生的初始化历史。

## 已证明与尚缺的首次窗口条件

| 条件 | 现有资料实际支持 | 剩余证明方式与边界 |
|---|---|---|
| 固定映像、完整指令及展开身份 | E-001 固定 Unity SHA `43fb0aea…653ad1`、13 完整函数、27 RF、1,838 指令、23 逻辑点、36 非重叠物理窗口及 3 个扩大块 | 这些是磁盘事实；本次进程仍须重验加载身份、实际源字节、保护与展开归属 |
| 五模块及 44 条绑定 | E-002/E-003 解析最终真实 PE、完整 SHA 和生成 CLR 指纹；未知 self RVA 已在最终 PE 重解 | **必须真实启动观察**实际 MEM_IMAGE/持有文件、加载路径、运行时 ILT/IAT/EAT 是否仍对应；磁盘图不等于加载时序 |
| Bootstrap 先于原 Unity 初始化及相关目标执行 | E-004 的四个自有采样点均见同 PID 的 36 处提交字节；[独立观察范围](../foundation-engine-transport-owned-observed-20261027/README.md)明确仅执行自有短桩/采样器 | **必须真实启动观察**原 Unity TLS、CRT、Entry 的顺序和此前执行者。旧[启动研究第 40–44 行](../foundation-engine-binding-20261025/engine-installation-research.md#L40)仅关闭两个 TLS 的直接 ProcessAttach 分支，8 与 3,006 个非空 CRT initializer 的间接行为未关闭 |
| producer 确为后续 Unity 主线程 | [FirstInstall.cpp:736](../../../native/RizToolbox.EngineBootstrap/EngineTransportFirstInstall.cpp#L736)记录实际 producer/owner TID；Owned 另按自有合同赋 mainThread | **必须真实启动关联**进程初始线程、producer、原 Unity 初始化和后续主线程。`GetCurrentThreadId()` 本身不能签生产主线程身份 |
| 写前无早期执行、无在途责任，写中持续排他 | 原字节、原保护、所有窗口预检/读回及失败保留已实现；没有完整早期执行历史或游戏执行屏障 | 需本次进程的可信执行历史/排他机制，覆盖抢先 worker、新线程及指向待改区的 PC/未完成责任。事后一次线程快照或原字节相同均不证明“从未执行” |
| 36 次发布期间其他线程不能取到半写代码 | [SourceWrites.cpp:243](../../../native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.cpp#L243)变为 RWX，[253 行](../../../native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.cpp#L253)逐字节复制；[producer gate:685](../../../native/RizToolbox.EngineBootstrap/EngineTransportFirstInstall.cpp#L685)只串行化自身 API | 不能把读回和 flush 当作原子发布或执行线程停放；持续排他必须由外层可信机制承担 |
| 覆盖块没有未处理的外部/间接落点 | E-001 的 `directEdgeScanScope` 仅扫描所选 13 完整函数内的直接 CALL/JMP/Jcc；显式不主张 external/indirect closure | 可继续补静态合同或强制机制；一次正常真实启动也不能证明未来永无外部 interior entry |

这里区分两类缺口：真实加载顺序、实际线程与本次首次执行历史必须进入真实进程取证；持续排他的机制及外部/间接落点合同还需要实现或静态关闭。前者的正常观测不能自动完成后者，也不能从当前 Owned=true 推出生产允许。

## 最小下一步：先取得生产无写观测

1. **准备独立无写观察方案，等待用户恢复真实启动安排。** 保持全部生产门关闭，从进程创建开始保存初始线程、全部模块/线程事件，在 Bootstrap、Doorstop、原 Unity TLS/Entry 边界读取实际身份、绑定和发布记录。目标是证实本次真实五模块链与预期 `Disabled/AdmissionUnproven/0 Configure/0 sourceWrite` 一致，并得到实际顺序与线程关联；不运行整局来代替早期证据。
2. **明确观测能力的缺项。** 仅有几个边界断点仍不等于完整首次执行历史；不能先向 13/36 个待改窗口写断点，再用它证明第一次写安全。漏历史、早期目标执行、不能管理的线程或失去排他均应明确拒绝。本文没有实现或虚构 startup authority 类型。
3. **由实际结果决定生产分支。** 在可信逐进程 authority 的来源、身份、主线程和持续排他条件都明确后，再实现私有签发/消费及反例验收，接入真实 Configure/Commit，最后重建五映像指纹和托管采用。此前不修改 bool，不把外部 JSON、Ready DTO 或 Owned receipt 当生产凭据。

## 证据到结论

- **F-001**：`severity=n/a_re`、`category=design`、`status=validated`、`confidence=high`。E-001/E-002/E-003 与上述源码位置支持“静态生产事实已成立，但生产签发分支、主线程 proof 和执行排他尚未成立”；影响是当前候选不能授生产首写。
- **F-002**：同为 `n/a_re / validated / high`。E-004 支持自有 TLS/Entry 的补丁观察，结合其明确不执行原游戏代码的范围，只能作为自有链证据，不能补齐生产首次准入。
- **P-001**：`path_type=callflow`。磁盘 EXE→Unity→Doorstop→Bootstrap→Kernel（E-003）→Bootstrap 原始事实核验后生产拒绝（F-001）→Doorstop 消费无写结果继续原入口；不存在由 Owned 观察（E-004/F-002）跳到生产 Ready 的合法步骤。

本页完成的是只读准入审计及文档交接。真实生产启动观察仍依用户暂缓安排，未在本文执行。
