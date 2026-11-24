# 下一批：把 L / M / N 的真实传输接入生产启动链

2026-10-26，普通固定 PE 逆向与源码审计，`flavor = null`。**下一主线是实现一个完整的扩展启动发布合同：13 个 typed 入口、23 个内部观察点、12 个永久 Configure 身份，以及对应的只读 CLR 采用。不能直接打开现有四入口生产开关。** Kernel 与自有调用夹具已有实际实现，当前 Bootstrap、V4 发布记录和生产 TryAcquire 仍只认识四个 Node 入口；这是真正的生产接线缺口。

范围继承[生产研究 scope](../foundation-engine-production-20261025/scope.md)。本次仅读仓库、既有证据及固定游戏 DLL 的磁盘 PE，并新增本文；未构建、执行 native 产物、启动服务、操作游戏或写入游戏目录。使用本会话已读的 ida-reverse / docs-generator 技能；IDA 不可用，限定使用本机 Python 3.14、pefile / Capstone 离线复核已知函数，不做广泛字符串、调用者或地址扫描。本文不代替 N 的受控结果；N 最终证据与边界见本目录 README。

## 当前确切缺口

| 层 | 已有实际代码 | 下一批必须改变的实际行为 |
|---|---|---|
| Kernel | Node ABI2；Inline ABI1；Scheduler ABI1；Wait ABI1；Node4 / Scheduler6 的私有 Wait 物理 push、phase、completion、pop | 继续复用该 DLL 和真实私有来源；不另造一套传输或 caller-supplied sequence setter |
| Bootstrap | `EngineFirstInstall.cpp` 四个 `originals`、八个 Node import、四次 Configure / Commit；`Bodies` 只允许每个函数首部补丁，尾部必须与原 body 完全相同 | 全部描述符与 relay 在任何写源之前准备；13 个入口和 23 个内部点共同配置、提交和读回；验证一个函数里的全部补丁覆盖图 |
| 原函数桩 | `EngineOriginals.asm` 仅固定 Start / Execute / Release / Detach；MOV R11 / JMP R11 转 continuation | 增加九个准确原函数桩，并逐函数证明活跃寄存器 / flags / unwind；不能机械复制旧 R11 尾 |
| 发布凭据 | V4 固定 4096 字节、`rows[4]`、32 位 mask、`events[64]`、四函数 body / RF | 独立新版本证明全体目标、relay、内部覆盖块、配置 family、epoch 和部分写责任；不挪用 V4 reserved |
| Doorstop | 已有普通依赖、真实 DllEntry 前后核验及失败责任合并；但记录、Ready 与 HasResponsibility 全部按四行 V4 | 接新发布版本；扩展失败责任至新 family claim、near relay / runtime table 和每次内部写源；保持原 82 个 WinHTTP exports |
| CLR 生产采用 | `NativeEngineFirstInstall` 只读 V4，仅四 Node 身份；`TryAcquire` 只传四 dispatcher 给 Share | 从同一新生产 owner 冻结 Node4、Inline1、Scheduler6、Wait1，首次 Share 时一次接全，禁止先建四通道 owner 再 late attach |
| Adapter | 已要求 Inline、Scheduler、Wait；`RequirePublishedSchedulerImage` / `RequirePublishedWaitImage` 会核固定 RVA | 生产采用必须真正提供这些 family。当前只开启旧 gate 会让 TryAcquire 宣 Ready 后在 Adapter 入口失败 |
| 构建 / 包 | `Build-EngineProductionCandidate.ps1` 和 profile generator 固定旧 Kernel SHA `923486…bc5662`、恰好八个 Kernel exports、17 条依赖边 | 独立构建新候选，使用此次冻结的新 Kernel、完整 export 集和新 graph；不能拿旧 package Kernel 或只改 SHA 绕过出口集合核验 |

`EngineBootstrapProfile::ProductionFirstInstallAuthorized` 和 `NativeEngineFirstInstall.ProductionStartupAdmissionKnown` 目前均为 false；这是明确状态，本文不授权把任何一个改成 true。

## 固定的目标与入口桩

固定输入为 `D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll`，SHA256 `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`，31,006,120 字节，AMD64，ImageBase `180000000`，ImageSize `1E65000`。本次按实际 `.pdata` / CHAININFO 根独立合并下列完整函数，完整解码选中范围；最后一列是从入口开始达到 14 字节的最短完整指令前缀，**只是新固定桩输入，不是安装许可**。

| typed 通道 | 完整 RVA 范围 | RF 数 | 前缀字节数 / continuation RVA |
|---|---|---:|---|
| Node Start | `792240..7929BF` | 1 | 15 / `79224F` |
| Node Execute | `78F840..78FA2E` | 1 | 18 / `78F852` |
| Node Release | `78F680..78F768` | 3 | 16 / `78F690` |
| Node Detach | `793840..793A5B` | 1 | 15 / `79384F` |
| M Register | `44D630..44D756` | 1 | 15 / `44D63F` |
| M Remove | `447AD0..447C26` | 1 | 14 / `447ADE` |
| M Consume | `447C30..447D60` | 1 | 14 / `447C3E` |
| M Pump | `447D60..447FAF` | 3 | 15 / `447D6F` |
| M Drain | `447750..4478AC` | 3 | 14 / `44775E` |
| M shared callback | `78F5E0..78F673` | 1 | 17 / `78F5F1` |
| N Yield | `78FC50..79014B` | 3 | 15 / `78FC5F` |
| N Dispatch | `5A5260..5A5343` | 1 | 14 / `5A526E` |
| N Batch | `5DA510..5DA6DB` | 7 | 14 / `5DA51E` |

本次独立得到的九个新增完整 body SHA256：

```text
Register ce57348a38b6eb9041cece746e038dddd5c676721f39c4c2f857669c03b74f20
Remove   3586c1418372fd581f21df83addb7ec59256794fc52374d0fc55593a732758ff
Consume  400ac94b4e996135f4a5602be15a0bcebf3f123e8f650b4dbb003544b6225abb
Pump     cafab343388f190607027203284728c3ce672cfe89115a50257e0faae66e3d7c
Drain    6cdea79f34ee8ddcec81015abe94c299df9732c54327e10cc0ba94040b9f435d
Callback 0cb5566bcbbe2510032fe522b586bd1940f621ba1e41ba2c93dd2a67098beafc
Yield    938b7a48664df2e0b0195fa2075f130f285a9498e0ce87693269dc2c6db3696f
Dispatch 7143eab8a6af6b584a341b1b41857b7ce9f74a4108987508ca803bdab1d11ccf
Batch    01f8cbd43685808116a8c8e34fe4cf7053de54595988ae8ade76676b351d6657
```

Callback 是新增桩的必要例外：入口 `78F5E0` 为 `mov r11,rsp; sub rsp,98h; mov rax,rdx; cmp [rdx+58h],rcx`，之后 `78F5FC..78F64C` 仍用 R11 定位多个栈槽。旧桩的 `mov r11,[continuation]; jmp r11` 会破坏真实原函数，特别是 owner-mismatch 分支。候选可使用经过完整函数活跃寄存器检查的 R10 转移，保留 R11、RAX 和 CMP flags；该完整函数不使用 R10。仍须对新桩各 PC 做真实 `RtlVirtualUnwind` / 异常传播验证。旧版尝试过的内存间接 `FF25` 尾曾被 Windows 当作 epilog，不能把它当成无条件安全替代。Consume / Drain / Dispatch 的前缀也产生后续分支要消费的 flags。

Node Execute 保完整 EAX；Start / Release 仅实际 AL；Detach 是 void。M / N 继续 raw4GPR→fullRAX machine carrier，不能把未知返回类型或 shared callback 的 EAX 归一化成 bool。中间位置仍只接既有完整 GPR / XMM / flags / MXCSR / FP control 的 MidEntry 机制，不能把观察点当成函数 ABI。

## 23 个观察点与三个 covered-tail 问题

逻辑 site PC 与 replay 合同沿用 [Inline 固定检查器](../foundation-engine-inline-20261026/inspect-inline-sites.py)、[M / N 路线](../foundation-engine-inline-20261026/next-scheduler-wait.md)及 [Wait 固定合同](contract-notes.md)。新发布记录必须分别写 `observationRva` 和 `patchRva/span`；relay CALL 的实际 return PC 是第三种身份。

| family | 逻辑 RVA，按既有配置顺序 | 原最小跨度 |
|---|---|---|
| L Inline | `7924CD,79257D,79259D` | `7,5,5`；两 End 都 JMP `79299E`，不能 replay 后落到下一个字节 |
| M Drain row | `447787,447866` | `6,5`；Begin TEST / JE 到 `4477F1`，End CMP 后回 `44786B` |
| N 发布 | `790069,79008D,790098,7900A0` | `10,6,8,5`；最后一处 JMP `79012E` |
| N Detach | `7938B3,793983` | `10,10`；第一处需扩大物理覆盖，见下 |
| N Release | `78F6F5,78F704,78F711` | `7,8,8`；首处需扩大物理覆盖 |
| N Start | `792527,792536,792543` | `7,8,9`；首处需扩大物理覆盖 |
| N Dispatch | `5A527C,5A5281,5A528F,5A529A` | `5,7,5,8` |
| N Batch | `5DA63B,5DA660` | `6,10` |

只读扫描上述 13 个完整函数里的直接 CALL / JMP / Jcc 对这 23 个窗口及 13 个入口前缀的目标，发现 **3 个直接分支跳入被覆盖的窗口内部**。这只关闭该限定集合的直接边；不证明全部间接入口或外部写入者。

| 实际原分支 | 危险尾部 | 下一批必须实现的固定块方案 |
|---|---|---|
| `7938B1: 74 03` → `7938B6` | 进入逻辑 `7938B3+10` 的 offset 3，即 RIP MOV 开头 | 物理覆盖扩大为 `7938AA..7938BD`，19 字节：load wait / TEST / JE / owner store / RIP MOV。relay 重放 load、test、分支；非空路径在逻辑 `7938B3` 发原 Probe，再重放 store；空路径跳过 Probe/store；共同重放 RIP MOV 后 JMP `7938BD` |
| `78F6F3: 74 20` → `78F715` | 进入 `78F711+8` 的 offset 4，即 CMP 开头 | ClearBegin 物理覆盖改为 `78F6F0..78F6FC`，12 字节，包含 TEST / JE / 前两个 store。非空路径在逻辑 `78F6F5` 发 Probe，store 后回 `78F6FC`。空路径去专用 **CMP-only replay**，执行原 `78F715` 的 CMP 后到 `78F719`，跳过 Returned Probe 和 node.wait store |
| `792525: 74 20` → `792547` | 进入 `792543+9` 的 offset 4，即 CMP 开头 | ClearBegin 物理覆盖改为 `792522..79252E`，12 字节，同上；非空路径逻辑 `792527` Probe，空路径重放原 `792547` 的 CMP 后到 `79254C`，不发虚构的 shared-ref-returned |

三个扩大块必须生成准确 bytes、完整指令映射、分支两路径和每 PC unwind 证据。不得仅写 E9 / NOP 后让旧 JE 跳到补丁尾部；也不能把 sourcePc 偷改为扩大块起点。`793983` 的限定直接边检查未发现同类跳入，不能据此扩大为间接调用封闭。

以下原 CALL 必须留在原位置：`790093`（旧 wait 引用归还，return `790098`）、`78F70C`（return `78F711`）、`79253E`（return `792543`）、`5A5288`（callback return `5A528B`）、`5A5298`（cleanup return `5A529A`）、`5DA645`（batch cleanup return `5DA648`）。新块方案没有搬走这些 CALL。不能从 relay 普通 CALL 目标后，把 changed return PC 声称为旧 PC；CET 下也不用改返回槽来补救。

## 独立发布 ABI 策略

建议新建扩展发布 **V5**，保留旧 V4 布局、四入口 owned 用例及旧 exports 的语义；同一固定源只允许一个真实发布 owner。新候选不用 V4 的 Ready 作为全通道 Ready。

1. 新 `EngineTransportPublication.h` 定义 V5；建议独立四 export `RizEngineTransport_GetVersion / GetInfoV5 / VerifyV5 / AbortAfterPublication`。这是待实现 API，尚不存在。Doorstop 候选普通导入这四个新名称；其对外 WinHTTP exports 仍为 82 个。不要让旧 `GetVersion=4` 搭一个更大结构，或往 V4 reserved 放 side 记录。
2. V5 要有固定 header / 精确 byteSize、5 个模块身份（Bootstrap / Kernel / Unity / EXE / Doorstop）、13 个 typed 入口记录、12 个 Configure family 记录、36 个物理写源记录（13 entry + 23 side）。Inline / Wait 各一次聚合 Configure，M 六次、Node 四次，所以不是 13 次 Configure。给每个记录独立序号 / family / kind / slot / helperEpoch；用至少 64 位的 commit/bind mask，不能继续以四位 mask=15 表示全体 Ready。
3. 每个 site 记录 logical PC、physical range、原字节 / patch / readback、所属完整函数 / RF、relay code / literal target / continuation / conditional bypass、运行时函数表及 unwind block 身份。36 个写源动作的保护 / 部分写 / 读回 / restore / flush 都有独立计数；事件容量按完整成功与失败路径计算，现 `events[64]` 不够。布局与上限须在头、Doorstop镜像、CLR decoder / tests 一次冻结并 `static_assert`，超限必须明确失败。
4. V5 `Verify` 重新核 13 个函数的完整 body，按 **非重叠覆盖区间图**替换期望字节。不能继续比较 `body[prefix..end]` 等于纯原件：Node Start / Release / Detach 和 M Drain、N Yield / Dispatch / Batch 的合法 side patch 都在原尾部内。未列入覆盖图的每一字节仍须与固定原件一致；先核全部新旧窗口不重叠，再按当前 committed 图校验每次写前 / 写后状态。
5. 新 Kernel 当前有 35 个导出：Node8 + Inline7 + Scheduler10 + Wait10。候选可继续采用严格的完整普通 import/export 白名单，Bootstrap→Kernel 35 条，加 EXE→Unity 1、Unity→Doorstop 4、Doorstop→Bootstrap 4，总图 44 条。若实施选用更小的实际 imported 子集，必须另固定该精确子集并验证所需 API 的实际 IAT / EAT；不能放宽成任意 exports /任意 DLL / GetProcAddress 动态补依赖。
6. owned / fixed-game profile 严格分离；生成 header / JSON 仅是事实。native 与 managed 两个生产执行窗口 gate 继续 false，直到该完整新写源集合的独立准入证据成立。

既有 Kernel wire 不需要升版：Node ABI2 frame112 / completion32；Inline ABI1 frame144 / config88 / observer24 / info160；M ABI1 frame256 / completion48 / config64 / observer40 / info144 / row224；N ABI1 frame240 / completion48 / phase288 / config432 / observer40 / info176。不能把它们与 Bootstrap 的发布版本混为一个 ABI。

## 从代码到可实际验证的最短工作包

| 顺序 / owner | 专属文件与必要修改 | 可执行交付与硬完成条件 |
|---|---|---|
| P0 固定合同 | 新 `EngineTransportGameProfile.h`、新 `docs/validation/foundation-engine-wait-20261026/inspect-production-transport.py`；保留旧 `EngineGameProfile.h` | 从固定 DLL 独立提取 13 完整 body / CHAININFO、13 前缀、23 logical site、3 扩大 physical range / bypass、所有保留 CALL return PC。输出固定 hashes 与完整字节，拒绝任何版本差异；不把本文的最短前缀抄成未经核验的 installer |
| P1 原生 writer | 新 `EngineTransportPublication.h`、`EngineTransportFirstInstall.cpp`、`EngineTransportOriginals.asm`、`EngineTransportRelays.asm`；沿用旧验证实现的有界 helper | 全部 13 continuation、35 API 身份、12 Configure、23 relay 与运行时 unwind 注册先准备；无观察者 Attach。随后在同一已获准启动窗口内提交 36 个实际 source writes；任一中途失败保留准确责任，不能回到无写 Disabled。编译无 CRT / PE TLS / DllMain加载/CLR/线程等待 |
| P2 Doorstop / 构建 | 修改 `EngineBootstrapProductionContract.h` 的新版本合同；新 V5 Doorstop helper `.h/.c`；`Build-Doorstop.ps1`、`Build-EngineProductionCandidate.ps1`、生成 profile、`EngineProductionPackage.Common.ps1`、candidate verify / package脚本 | 新候选显式选择 V5，固定新 Kernel35 exports、44边策略和最终 hashes；旧候选保留。所有后继 DllEntry failure / IAT failure / 异常 / unload 入口合并 **所有 family + relay table + source/protection** 责任。禁止只看旧四行。生产 gate关闭时完整构建并返回有证据的 DisabledNoSourceWrite |
| P3 托管 owner | 新 `NativeEngineTransportPublication.cs`、`NativeEngineTransportFirstInstall.cs`；为 Node / Inline / Scheduler / Wait bridge + dispatcher 增 `AdoptPublished` 生产专用路径；修改 `NativeEngineRuntimeCoordinator.cs` | 新 owner 只接受已经加载且独立固定的五模块、V5实际记录和 GetInfo；禁止 `NativeLibrary.Load / Configure / 补patch`。真实原生 Verify 前后不变性、family tuple、relays、main OS TID、slot28 / exactepochs 都对齐后，第一次 Share 一次创建全通道。普通 `AdoptConfigured(path,identity)` 仍只供自有fixture，不能给生产权限 |
| P4 实际绑定 | `NativeEngineCoroutineRuntimeCoordinator.cs` 与新 owner 接口；必要的 Loader / Hosting 状态展示 | `TryAcquire` 在缺任何 Inline / M / N family 时不得宣完整 Ready；`RequirePublished*Image` 查同一 V5 owner，连 relay return PC / family epoch / actual original 一并证实。复用现有 node / identity / inline / scheduler / wait / cancellation 账本，保证 Node Complete/Dispose 只一次；不把缺观察静默回退为取消完成 |
| P5 自有完整启动验证 | 扩展独立 Publication owned target / Host，或新单一 `EngineTransportPublicationFixture`；对应 Build / Test；Runtime生产adoption用例 | 测真实普通依赖冷启动→P1 producer→Kernel全部13kind /23sites→CLR全采用→实际账本；必须执行3个covered-tail的zero/nonnull两个分支、Callback owner-match/mismatch和完整高EAX、Start inline3site与Wait同Start清理组合。不能仅把N调用DLL再跑一次当生产Bootstrap验证 |
| P6 固定游戏候选准备 | P2 的独立构建 / 包 / 合成安装恢复验证 + 新只读 cold证据方案 | 使用新的冻结 DLL和真实导入身份检查，不重写旧I/L/M/N原件；合成路径验证同basename重复Kernel、覆盖拒绝、部分安装 / 后继失败 / 升级回滚。用户恢复实测后才执行固定游戏窗口观测；本任务不触发 |

P0/P1 原生、P2 build/Doorstop、P3/P4 CLR 可以在同一次 ABI 冻结后分工并行。只有 root / 唯一 builder 统一构建，先冻结源，再 fresh 输出到 D 盘缓存，保留失败日志和输入 manifest。不能让不同构建共享一个会被覆盖的生成 profile 或 Kernel 路径。

### near relay 与永久责任

不能假设 Bootstrap / Kernel 经 ASLR 后距离 UnityPlayer 的 23 个 site 都在 E9 的 ±2GiB 内。最低方案是在实际 Unity 映像上方的已验证空闲范围准备永久 near block：先 RW 生成固定 code、literal与 CHAININFO，再核每个 rel32及DWORD RVA可表示性，注册永久 function table、切 RX / flush，最后才写任何 game source。固定 source与 unwind块须共享一致的 RtlAddFunctionTable image-base语义；如果 block落在 Unity 下方，不能把负 RVA 强转 DWORD。

不允许失败后卸载留下 RUNTIME_FUNCTION 指向已卸载的 unwind / relay。V5 在 function table 注册或任一永久 Kernel claim 出现时就记录 root 责任；旧 V4 的 `HasResponsibility` / `RetainedClaims` 仅覆盖四 Node 配置，不够用。生产无写降级必须发生在这些永久动作之前；后续失败用明确保留或终止规则，不能清记录重试。near block分配、运行时 unwind注册在真实loader阶段的依赖 / 回调风险也属于准入核验，不因使用Kernel32 API自动获准。

### 同一次实际 scope 的时序不变

Wait：BeginNode / BeginScheduler 在旧 snapshot 前 push；Entered 在 snapshot 完成后、旧 typed Enter 前发物理登记；Original 在真正 original前；Closing 在任何旧完成 / inline退出前遮蔽；旧 typed Complete + Dispose结束后才 Wait Complete，最后 Pop。其 13 类物理遮蔽以及 `CanUseSchedulerSource` 不能被生产接线删除。Shared callback真实来源仍只能是直接父 Wait Dispatch original + armed +原CALL返回地址；不能伪造 scheduler Consume来源。

Inline 仍由 actual Start 创建物理scope，StartEntered / Begin / End / StartExited保持原合同，End AL与真实Start completion核对。主 / side每次入口冻结一次observer；CLR只在全family采用与逻辑绑定完成后开放新增owned工作，部分Attach失败保留root、Unknown和实际在途职责。首次采用也不能假定启动 OS thread 就是已证Unity main OS thread；要在冷启动和真实第一调用中核这个immutable TID来源。

## 必须实际 verify 的清单

1. **磁盘 / 发布图**：新 Kernel 无Entry/TLS/CRT，35精确exports；五模块hash、文件归属、普通 ILT/IAT/EAT、无delay补链，Doorstop82exports；新 Bootstrap 实际进口与新发布API全部匹配。完整13函数 / RF与23site /3覆盖块均来自固定SHA；所有相对地址、字面量、RX保护和function table范围可在ASLR下验证。
2. **机器保真**：每个入口桩的逐PC unwind、flags与寄存器存活；Callback R11例外必须实际覆盖owner-mismatch；全部23relay CALL fullMid保真；3covered-tail空路径既不得踩E9尾，也不得多发Begin/Returned；所有保留CALL返回PC实际一致。原SEH / C++异常传播时Finally仍排空或保Unknown，不改原异常 / LastError / FP机器状态。
3. **首次写与失败矩阵**：先全部配置 / prepared后first source write；36处写源均验证protect、partial write、readback、restore、flush和后继失败，区别disabled/no-write与有claim/no-write / partial-write。不能把前4已成功、后32未写包装成Ready。事件容量与immutable record不溢出，native owner/proxy/kernel/relays/files永不悬空卸载。
4. **真实全链**：自有冷启动时发布确实早于所模拟的目标TLS/entry/第一typed/site执行；同PID CLR采用全部12family、只Attach一次；两逻辑订阅和退订/重入不借代次，lateAttach不能补借旧scope。Start inline清理与Wait发布/撤销、M consume→callback、Wait owner-null / callback-clear / 双node同waitrepublish，都使用真实原函数窗口和actualToken Complete/Dispose；不造观察回执。
5. **固定游戏窗口仍未证明**：必须观察从真实进程创建、Bootstrap PROCESS_ATTACH 到 UnityPlayer 两个TLS `17E7708/17E7780`、实际入口和全部36patch区第一执行之前的窗口，核实际IAT已选冻结候选。普通依赖存在、opaque非空lpReserved、自有宿主早于自有TLS，都不足以单独证明游戏没有先前执行者 / 依赖循环 / 间接初始化回调。不能用loader-lock内线程等待或随意暂停线程代替这个证明。

生产首次安装只解决“这些固定补丁能否在该执行窗口安全发布”。它不证明 all writers、所有 wait派发/自定义yield、future producer关闭、async封口、EngineCancelled、全部root释放、B01–B19验收或整局/重试/返回菜单。完成P0–P5可交付完整可review且自有链已验证的候选；固定游戏权限仍须P6的独立证据。

## Evidence → Finding → Path：本次读取时点

以下为当前源码读取时点的SHA，旧报告的历史哈希不覆盖；本文没有修改这些文件。路径均相对仓库根。

| Evidence | SHA256 / 文件 | Finding → Path |
|---|---|---|
| E1 V4实际布局 | `b035a90cb26dfc035caaa8ef1fe575de734b2828d3f7c9c4700400c3ab9582cd`；`native/RizToolbox.EngineBootstrap/EnginePublication.h` | 4行/4096/64event不足以代表新增全体 → 独立V5 |
| E2 native四入口实现 | `1dcaca2186924b64f57487238e93f794947cce74addc3769c263fdebb070feb7`；同目录`EngineFirstInstall.cpp` | `Configure/Commit/Bodies/Verify`均4通道 → P1覆盖图与12family/36write |
| E3桩与profile | `ae7b436fcb69e2df84843f248b6c8d0ab66e3f4446a9a41feb526f3cfc06f40b`；`EngineOriginals.asm`；`cfbf2a612d0453dff7a51663061a8768aeb2d6d93cb12a7118858e266fddea27`；`EngineBootstrapProfile.h` | 旧MOVR11桩/固定4前缀、生产false → 新9桩逐条证明，不改旧许可 |
| E4生产依赖合同 | `492044f72f4abb9837a776c03e4157b44c4f5671aa1dd8ea6d4a495b5b630dfe`；`EngineBootstrapProductionContract.h`；`11cd94e99fdf8f300d5de565e00f303473571166218db5065c9fd77292033b42`；`native/RizToolbox.Doorstop/riz_engine_bootstrap.c` | 17边、V4Ready/四行失败责任 → P2新graph/全体责任 |
| E5 CLR采用 | `6f7f3330d02938070e448d623ea478ec8dcb9c96873a5c8c88ac5acebe574205`；`src/RizToolbox.Runtime/Hooks/NativeEngineFirstInstall.cs`；`20aae1eaec744fae7240ffd1991514747b9ed12e969785b13f9b50ba9b1f8d10`；`NativeEnginePublication.cs` | 仅V4生产owner → P3新只读owner |
| E6实际coordinator | `76ac2ca7b449966d2c2958b525b29afadd3ad053af0e90ca0ae91640cf1a68d3`；同目录`NativeEngineRuntimeCoordinator.cs` | controlled能接L/M/N，production TryAcquire仅4 → P3/P4一次接全 |
| E7新wire冻结头 | Inline `c66da8ef6166c9c4289a9c39c2558f1bed57c8430f42e470b2a383e99da94ec9`；M `380fc40bb712a591eab8055c350a661bc8efab1148685d2099afa7574d8d29b2`；N `cb6f542cb9ef5c9961a795b4bda6a3015878f66b1e52b3c28cdf169e8dd4fa00`；`native/RizToolbox.JudgeContext/EngineInlineProbe.h,EngineSchedulerContext.h,EngineWaitContext.h` | 已有真实观察接口，发布而非重写传输 → P0/P1/P3 |
| E8构建 / 包 | `906270f2befaee1683eba4237ce83f3cca4647763b55ba79a25554e23f8b1ea5`；`scripts/Build-EngineProductionCandidate.ps1`；`c344189b74497d4bcab2e14baf8bc540ddcb21ccb3289b59b6cfef3c6f363fd3`；`EngineProductionPackage.Common.ps1` | 固定旧Kernel8exports/17边 → P2新候选构建与包 |
| E9生成器 | `3127725c8e47325843abc077610bd46b3791e9f7554d11821fa25f6e33abe768`；`docs/validation/foundation-engine-production-20261025/generate-production-profile.py` | 拒绝新Kernel出口集；生成事实不发准入 → P0/P2 |
| E10既有fixed-sites | `deb6deb404321dc7dd863b7424e2ed44c0ee26c936c437812cdc9fd15534ae8e`；[contract-notes.md](contract-notes.md)；Inline检查器 `2134a7ae7b2d3cf505e641e2febaadb35a6054f10737e5339ce7f6900150abff` | 23逻辑点和固定CALL来源 → P0扩大physicalrange，而非篡改旧ABI |
| E11本次实际磁盘复核 | E1固定UnitySHA；按13已知anchor查`.pdata`根、全组选段解码，遍历该集合direct CALL/JMP/Jcc目标；输出保存在本会话只读工具记录中 | 上表完整范围/九hash/前缀；3条covered-tail；Callback R11活跃 → P0新inspector固化为可重跑文件，当前不称生产动态证明 |

建议下一批第一个交付就是 P0 固定表与V5头一次冻结，随后三位writer按P1、P2、P3/P4并行，唯一builder跑P5。不要先把旧四入口gate打开，再靠运行失败逐步补通道。

## 可以立即独立实施的第一块：九个固定 typed originals

如果 V5 尚待冻结，先落实 P1 的九个真实 original 桩与机器验收，完全不改旧 V4、不触碰 game source，收益最确定。文件所有权如下：

| owner | 新增文件 | 具体任务 |
|---|---|---|
| 原生 writer | `native/RizToolbox.EngineBootstrap/EngineTransportOriginals.h/.asm` | 固定顺序 `Register,Remove,Consume,Pump,Drain,Callback,Yield,Dispatch,Batch`；私有 `RizEngineTransportContinuation[9]`，只由未来 native publisher 一次绑定，无 exported setter。分别重放上表 `15,14,14,15,14,17,15,14,14` 字节前缀、产生准确 `.pdata/.xdata`，MOV/JMP去 continuation；不加通用decoder、heap、CRT或动态目标配置API |
| 自有 fixture writer | 新 `native/RizToolbox.EngineTransportOriginalFixture/`、`scripts/Build-EngineTransportOriginalFixture.ps1`、`Test-EngineTransportOriginalFixture.ps1` | 自有ASM函数包含真实九种prefix及独立tail标签；把 continuation绑定这些真实tail，实际调用每个original并与未分拆的同字节对照函数比行为。只编译/调用自有函数，不加载Unity，不返回调用者递交的观察帧 |
| 唯一 builder / reviewer | 新固定profile核验器与fresh D盘输出 | DLL磁盘解析核bytes/RF/unwind，实际运行callee、`RtlVirtualUnwind`逐PC与异常；保存fail/pass原件、source SHA、binary SHA、返回/flags/LastError与每个unwind PC。通过后再纳入V5 writer |

Callback 的最小明确指令选择为以下 **R10 register-indirect tail**，不需要near stub，也不改真实source CALL返回地址：

```asm
; Actual callback prefix, exactly 17 bytes.
mov r11, rsp
sub rsp, 98h
.allocstack 98h
mov rax, rdx
cmp qword ptr [rdx+58h], rcx
CallbackPrefixEnd:
; 7-byte RIP-relative MOV + 3-byte ModRM11 register JMP.
mov r10, qword ptr [RizEngineTransportContinuation+5*8]
jmp r10
.endprolog
CallbackEnd:
```

`PROC FRAME` / `.allocstack`必须跟实际指令位置对应；该首版与既有安全桩一致把10字节tail放在记录的prolog范围内，RF范围为 prefix17 + tail10 = 27 字节。R11是volatile栈地址临时值，不声明成非易失frame register，不产生 `UWOP_SET_FPREG`。RAX与CMP flags不被尾部改变；R10在本次固定完整Callback body `78F5E0..78F673` 中没有任何读/写操作，不承担源函数输入或活跃临时值。机器验证仍要复核 `r10/r10d/r10w/r10b` 全部alias，不能只按一个字符串判断其它版本。

该选择避免了两个已知错误：R11续跳会破坏owner-mismatch分支的栈槽；FF25内存间接续跳可能触发Windows的epilog识别。尾部的 `jmp r10` 为 ModRM11，沿既有 `jmp r11` 的固定桩方案，不使用ret模拟跳转或改CET返回槽。其真实Windows展开必须由以下有界验收证明，而非只看MASM成功：

1. 对prefix每个指令边界、MOV的首PC、JMP的首PC建立真实CONTEXT与原调用者stack；用实际 `RtlLookupFunctionEntry` / `RtlVirtualUnwind` 恢复到同一 callerRIP/RSP以及所有非易失寄存器，包含尚未执行SUB、已经执行SUB两类位置。Callback body mismatch分支还要真实走完R11相对栈写，并核独立栈canary。
2. 分别调用owner相等与不等路径：相等路径保原Execute结果完整EAX，不等路径返回EAX=1；对照真实prefix版核RAX、R11在tail入口的值、RFLAGS以及输入四GPR。让callee自行计算结果并写独立sentinel，不能由测试拼一个“应当观察到”的frame。
3. 自有tail内部实际RaiseException / C++异常向外传播，外层SEH / C++catch收到原code/object；实际stack walk穿过original桩。观察函数引入的FP control变化必须按边界恢复，不要求普通original函数异常自动保ABI允许改变的易失状态。LastError在相同输入和原函数路径下与未分拆对照一致。
4. 其余八个桩先逐完整固定body做continuation处的volatile读前写分析，再选择R11尾或同样的已证scratch寄存器；不能把Callback例外反向推广成“所有函数都可随意用R10”。Consume/Drain/Dispatch的真实首分支必须同时测taken与not-taken，以发现尾部意外改flags；Register真实返回输出槽指针，Callback/Execute测高EAX而不只AL。

这一小包通过只签“九个fixed original/replay与异常展开已在自有环境验证”，不签23relay安装、启动窗口或生产Ready。后续V5可直接链接这九桩，避免改完发布大结构后才发现最基础的Callback尾破坏R11。
