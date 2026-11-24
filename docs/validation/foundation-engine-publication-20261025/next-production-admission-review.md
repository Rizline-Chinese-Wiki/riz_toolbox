# 下一步：把 V4 接到真实游戏组件链

2026-10-25，只读架构审计。结论：本批 V4 的自有宿主链已经跑通，本批故障矩阵归档后直接转向候选 Doorstop 接线、生产只读采用和实际消费者；不再新增一套平行 publisher/Kernel。游戏实测按用户要求稍后进行，生产首次写入许可继续关闭。

本审计只读取规格、代码和已完成进程的原件；没有构建、启动宿主或游戏、安装文件。唯一新增产物是本文。文中“待实现”均不表示已经通过。

## 已具备与真正缺口

六个已完成用例覆盖原生冷启动、同进程 CLR 正常采用、捕获中解绑、原始 SEH、原始 C++ 异常及纯原生异常。父离线复核见 [preflight-reviewed-facts.json](preflight-reviewed-facts.json)，共 2549 条复核断言。它们证明的是：

```text
自有 EXE → 自有目标 DLL → Bootstrap → Kernel
                           ↓
                     V4 → CLR → 四 dispatcher → 两个测试消费者
```

交接时 root 已报告本批合计 34 个 primary process（6 联合预检 + 26 故障矩阵 + 2 拒绝用例）真实通过，矩阵独立核验 2035 项，正在归档。本审计没有重跑这些进程；下表 T0 只需收齐对应原件与冻结关系，不再扩充一批平行用例。

固定游戏所需的链仍有断点：

```text
Rizline.exe → UnityPlayer.dll → winhttp.dll（候选 Doorstop）
                                      → Bootstrap → Kernel
                                            ↓
                                      V4 → CLR → 生产 coordinator
```

EXE→UnityPlayer→WINHTTP 的普通导入边已有固定磁盘证据。Doorstop→Bootstrap 尚未接入；生产 CLR 没有调用 AdoptPublished 的消费者；主 Build/Install 仍选择旧默认 Doorstop 和深目录 Kernel。V4 自有用例通过不能代替这三个缺口。

## 按依赖施工

| 顺序 | 实施任务与具体交付 | 前置与完成条件 |
|---|---|---|
| T0 | 收齐本批 V4 故障矩阵、动态加载拒绝、父观察器源页/保护读回和冻结归档，不再另造一套早期发布器 | root 已报告 34 primary process 通过；归档后进入 T1，不因增加无关用例延后游戏组件接线 |
| T1 | 在 Build-Doorstop 增加独立候选模式，普通导入 Bootstrap 四个现有 ABI；真实 DllEntry 在任何可能失败返回处合并 Bootstrap 责任 | 保持现有 82 个名称/ordinal、四 canonical ASM、旧默认模式；PE 检查真实普通 IAT 和解析目标，不用 delay-load 或 DllMain 内 LoadLibrary 补链 |
| T2 | 增加候选的生产构建/打包/安装计划：winhttp、Bootstrap、Kernel 位于当前路径合同可接受的位置，三份确切身份及源码归档可追溯 | T1；只在临时安装目录验证归属、覆盖拒绝、升级和恢复。当前 Bootstrap 要求 DLL 与 EXE 同目录，深目录 Kernel 不满足它。候选避免同时加载同 basename 的两份 Kernel |
| T3 | 实现固定游戏的 native admission 分支：严格检查 EXE→UnityPlayer→候选 Doorstop→Bootstrap→Kernel，独立固定 Kernel 与 Doorstop 身份，定义 profile2 的 admission 位语义 | 可与 T1 并行写，最终使用 T1 的冻结字节。生产启用开关保持 false；不删除 owned marker，不把 owned 的 31 位直接当作游戏许可 |
| T4 | 实现生产 RequireReady 的只读采用分支及一个进程级 coordinator：只生成四个共享 dispatcher，接现有 node/identity/cancellation 账本和三类 coroutine tracker | 可先于实测完成；未获准的 profile2 返回 CapabilityUnavailable。生产路径不 Load/Configure/补 patch，故障粘住 Unknown，退订只撤消费者，保留 native owner、委托和已捕获职责 |
| T5 | 接实际对象/根与调度责任：receiver→owner/Enumerator/Coroutine/node 代次，scheduler 消费/撤销、async 与 Start 内联清理，以及 writer/delegate/future 消费者退休 | 可与 T1–T3 并行做代码；复用现有生产类与有界回归。没有真实 GC/调度证据时继续 Pending，EngineCancelled=false、RequestPlay 关闭 |
| T6 | 用户恢复游戏实测后，仅对 T1–T4 冻结候选做真实冷启动观测，再决定是否签发这个固定版本的首次写许可 | T1–T4 的静态/离线项完成；运行证据见下节。通过首次安装不等于通过 T5 或 B01–B19 |

T4 是直接推进底层接入的下一条代码主线，不应等待所有取消与资源退休完成才开始写。T5 的取消完成门槛也不能反过来伪装成首次安装的启动证据。

## T1 的初始化和失败责任

Bootstrap 成为 Doorstop 的普通依赖后，会先于 Doorstop 的 DllEntry 完成依赖初始化。它必须在候选 WinHTTP resolver 动态加载系统 WinHTTP之前完成核准备；Bootstrap 自己不调用 WinHTTP，不启动 CLR，不提前加载 GameAssembly。

Doorstop 接续应区分三种状态：

- Bootstrap 为 Ready：GetVersion/GetInfo 核对成功后才调用 VerifyV4，核验完整四行和真实依赖；后续任何代理初始化失败先调用 AbortAfterPublication。发生源写/保护责任时终止进程，禁止直接返回 FALSE 留下跳往卸载模块的源代码。
- Bootstrap 为已知 DisabledNoSourceWrite 且没有配置、写入或保护责任：可以继续原游戏与 WinHTTP 转发，底层引擎观察能力报告不可用。这是当前生产许可关闭时的预期候选行为；不能对 Disabled 无条件调用 VerifyV4 再把它毒成 Unknown。
- 记录忙、损坏、未知，或存在未封口的配置/写入责任：不可按无写降级。调用 Abort 并按其责任规则处理。不能清记录后重试，也不能在 CLR 补配置。

对 pinned upstream 的整条 DllEntry 路径逐个列出返回失败、异常与显式卸载位置。只处理新增 WinHTTP 的首个 `return FALSE` 不够。需要保留进入前 LastError、失败阶段和 Bootstrap 状态。四 Bootstrap ABI 必须产生真实普通 IAT 引用，不能仅把一个未被使用的 .lib 放进链接参数。

现有 Abort 的责任依据是源写/保护责任；动态卸载路径另外检查 configuredMask。若 T1 新增了“已 Configure 但尚未写源”的失败状态，应同步定义该状态的永久持有责任，不允许掉进直接卸载分支。

## 固定身份的无循环构建顺序

建议：固定 Kernel → 仅含既定 ABI 的 Bootstrap importlib → Doorstop 与 MAP → 生产 Bootstrap → CLR/包清单。

Doorstop 用 importlib 建立普通依赖，不依赖 Bootstrap 最终 SHA；随后 Bootstrap 编译期固定 Doorstop、Kernel 和固定游戏身份；最后 CLR 编译期固定 Bootstrap SHA、Doorstop SHA 与必要的私有 MAP RVA。Bootstrap 不把自己的最终 SHA 嵌进自身，也不信任调用者递交的一份 JSON 来给自身授权。

V4 的四个 module 行仍分别是 bootstrap/kernel/target/executable。Doorstop 是第五个受核模块，不能挤占四行之一、复用 reserved 偷改语义。可保持 V4 布局，在生产分支用独立固定合同核对第五个模块和实际 IAT，CLR 再独立核对；若需要对外暴露完整新记录，应显式另立版本。候选代理继续保持 82 个公开导出，不能为读取私有诊断随意增加导出。

## 签发生产 first-install admission 前还缺什么

### 现在即可完成的静态与代码证据

1. 固定 EXE、UnityPlayer、候选 Doorstop、Bootstrap、Kernel 的磁盘身份、路径合同、普通/延迟导入、完整真实 IAT 目标与 TLS/入口表；包含 EXE 的两个导入和 UnityPlayer 的 19 个依赖，不仅截取四个 WinHTTP 名称。
2. 沿实际启动图列出依赖循环、会先执行的 TLS/CRT/DllMain、可能回调四目标的路径；结合现有固定函数完整 body/RF/unwind 证据，明确第一次写源前所依赖的“不存在既有执行者”条件。对间接调用、外部初始化回调保留具体未闭合点，不能用“普通导入存在”盖章。
3. 固定四目标的 exact source/body、Release 的三个真实 RF、原函数桩与 continuation、页保护、Kernel ABI2/scalar1/slot28/真实 epoch、配置与提交顺序。已有代码复用；生产 profile 不可接受 owned 路径或 arbitrary RVA。
4. 候选安装后的实际普通依赖能够从固定位置解析；父持有文件/hash、包清单、生成常量和相应源码互相绑定。GameAssembly 不在当前早期普通依赖图里，此阶段只可查磁盘身份，不能要求它已加载。
5. 候选 Doorstop 后继失败责任，以及 production RequireReady/consumer 的可用、Disabled、Unknown 三条实际可执行路径。生产开关关闭时也应能完整构建并明确降级，不以空接口算完成。

### 只能在恢复游戏实测后补的运行证据

1. 从真实进程创建开始记录 PID/TID、模块实际路径/身份和普通 IAT，确认 UnityPlayer 实际选中了冻结候选代理；同名文件在磁盘存在不算选择证据。
2. 观察 Bootstrap 的实际 PROCESS_ATTACH、非空 opaque lpReserved、四 Configure/Commit/Ready，明确其早于 UnityPlayer 两个 TLS（RVA 17e7708/17e7780）、实际入口及四目标的第一次真实执行；不能只观测到 CLR 后的 Ready 快照。
3. 对首次调用之前的执行窗口、已有线程/重入及初始化间接路径形成有边界的证据。自有宿主“早于自有 TLS”只能支持实现，不会自动关闭游戏未知路径；不在 loader lock 内等待线程或依靠会执行代码的软件断点证明没有竞态。
4. 核对实际系统 WinHTTP 模块/持有文件/四 EAT 地址与真实转发槽，之后同一 PID 的 CLR 独立验证 V4 和四 Kernel tuple，再看到真实四通道调用。resolver 尚未完成时，不应提前运行 CLR 或宣称所有依赖 Ready。
5. 如启用中发生异常，原函数调用、异常传播和永久持有责任有实际证据；游戏行为、对象 GC、scheduler/取消与资源退休另列验收，不由冷启动结果代填。

这些运行项未完成前，native 的 ProductionFirstInstallAuthorized 和 CLR 的 ProductionStartupAdmissionKnown 均保持关闭。可以提交完整候选代码和静态报告，但不能把它写成“游戏底层已全覆盖”。

## Evidence → Finding → Path

本表是本次读取时点的观察；root 后续修改代码时应保留时点，不覆盖旧失败原件。复核方式为仓库根目录 `Get-Content -LiteralPath <相应路径>`、`Get-FileHash -Algorithm SHA256`，以及 `rg -n 'AdoptPublished|NativeEngineFirstInstall.RequireReady' src`；全部离线，不执行目标。

| Evidence | 读取位置 / SHA256 | Finding 与后续任务 |
|---|---|---|
| E1：已完成六进程原件的父复核 | `preflight-reviewed-facts.json`；7d7651a4c472a7e0891025a929f8ee8e0667768fd58fedbd21331cd7c2451a13 | 自有 native→CLR 成立，生产路径尚未因此成立；T0 后转 T1/T4 |
| E2：候选构建与实际 DllEntry 补丁 | `scripts/Build-Doorstop.ps1:56`；bc0dfb0f7ef0a2610c0c7bfa7b195c92d77c008ac1cd62437a135e4b420fc8c8 | 没有 Bootstrap 普通导入，WinHTTP 失败直接返回 FALSE；T1 |
| E3：native 的 owned 检查与生产拒绝 | `EngineFirstInstall.cpp:342,374,463,543`；cb15dec90d93ca59b023c6dac9d8dae746e91604182a759a74d8e1cfdb656516 | 原因是启动图未获准，不是四函数常量缺失；生产 Verify 仍强制 owned，Abort 已有供调用；T3 |
| E4：profile 和 CLR 权限来源 | `EngineBootstrapProfile.h:48`；369d6fef281fd78fec843efc37e13a6c785be9873ad087c012145239f35ec342；`NativeEngineFirstInstall.cs:41`；d907a4cfde1e14e6d31ed0384f6c60cae7f2ec429f18b08509ce9e09a1498037 | 生产 KernelIdentity=null，RequireReady 明确拒绝；生产无 AdoptPublished 调用者；T3/T4 |
| E5：主包选择与安装白名单 | `scripts/Build.ps1:17,104`；2401a17ec45e356c39a69f54582e4b6594ca379451e243f5fa753872ab0ac22b；`scripts/Install.ps1:43`；ab401aeb41aaade1f500a6ea02f414d255d14c13dce9da3443b51ac28c607ac9 | 默认构建和深目录 Kernel 尚不等于新普通依赖部署；T2 |
| E6：固定游戏的现有磁盘事实与完整目标 | [WinHTTP 契约](../foundation-winhttp-contract-20261025/research/game-winhttp-contract.md)、[固定引擎 profile](../foundation-doorstop-cold-20261025/research/engine-game-profile.md) | EXE→UnityPlayer→WINHTTP 已知，实际初始化/首次调用仍未知；T3→T6 |

调用路径 P1：E6 的已知游戏磁盘边 → E2 所缺的 Doorstop→Bootstrap 接线 → E3 的生产 native admission → E4 的 CLR 只读采用与生产消费者 → T5 的真实对象/调度/退休。前两段的代码现在可以推进；运行状态仍以 T6 的原件决定。[SPEC](../../../.docs/foundation-expansion/SPEC.md) 和 [CHECKLIST](../../../.docs/foundation-expansion/CHECKLIST.md) 的 B01–B19 验收状态不因本文改变。
