# 九个播放入口与 FirstInstall 私有安装证据

最终补记：含foundation映像的7模式226均actual0。完整seal已在独立`playback-nine-source-integration-seal-r1-20261031`通过，7342唯一文件／16882引用，5PE/PDB469映像文档；[索引](evidence-index.json)。seal只针对core-r4、通过合入前确切归档复验原727输入，`currentIntegratedSourceAccepted=false`，不验收后续主树。首四普通mode与新三foundation mode使用逐字节相同的五DLL；每个模式各有真实exit/log，未补造旧中断driver的退出。所有相关job已terminal，下面未闭合措辞为早先施工状态。

2026-10-31，`flavor=null`。范围和用户授权见[scope](scope.md)。当前源码已实现完整九成员物理安装模型，最终82模式已实际通过；含foundation映像7模式仍在执行，本文保留联合验收施工状态，不能作为整批验收通过或可安装版本声明。

## 实际改变

原来GetTime和SetPitch仅有两个成员的共同安装。现在同一broker能管理GetTime、SetPitch、MUG Pause/Replay/Resume、PlayScheduled、set_clip、Progress.Play和AudioSource.Pause九个物理入口；八个source通道共用一个native owner、TLS、fence和completion journal。任一成员的代码、原跳板、展开或元数据改变会撤回共同组，恢复字节不能复活旧owner。

每个入口采用其固定真实前缀，Progress保留完整7B的比较及flags，set_clip保留MOV-save/PUSH-RDI，Pause/Replay/Resume各有自己的栈与寄存器形状。actual Dobby original与独立R11回原跳板分别核验，后者有实际Windows展开注册。新source及GetTime的code/metadata在同一次8192B分配中保留两页，code为RX、metadata为RO，避免在模块旁另搜2GB空闲区时耗尽。未知形状明确拒绝。

另一个实际问题是原展开元数据的晚捕获：安装前修改自有fixture的只读xdata，旧源码会把被改过的字节当基线并成功arm。当前从锁定PE文件核对完整原正文内所有RF/UW/CHAININFO，所有source和GetTime在任何Prepare前完成预验并持续重验。[精确反例复验](PRECAPTURE-XDATA.md)实际得到Prepare=Commit=0，不发行任何group/lease；原错误通过、汇总器的一次错误与修正后的派生结果均保留。

FirstInstall自己保留首次Prepare前的file/DIR64、28个实际original、64次自身写入与逐PC展开事实。新增的两个只读导出使helper EAT为85；既有V7 publication仍为2528B/26 operations/64 writes，独立attestation为version1/header120B/range184B/28 originals。Runtime只从实际注册的FirstInstall owner发行opaque CLR membership，并由共同组把其真实pre/post写入与原文件组合；公开Ready、复制row/token、caller mask不发行权威。native Publish后负责自身member，组继续负责完整executable文件比较，避免合法新组修改被误当foreign write。

## 当前验证证据

最终基础core-r4包含727输入，SHA `d96e595c2b8244d176b3b748f72d265212489f2a845791f4ba9f6b2a647707d0`。下列独立范围分别使用实际退出码及自己的来源证据，不把检查数相加。

| Evidence | 实际输出根（均在 `D:/riz-build-cache-next/`） | 已观察结果 / 范围 |
|---|---|---|
| E-CORE | `foundation-nine-source-core-r4-20261031` | 实际exit0；Audio1001、coordinator28、receiver10，冻结727输入 |
| E-FULL | `foundation-nine-source-full-r2-20261031` | 27套2465/2465，每个build/test实际0 |
| E-RELEASE | `foundation-nine-source-release-r2-20261031` | 六DLL实际build0，0 error/1既有生成profile nullable warning |
| E-SOLUTION | `foundation-nine-source-solution-r1-20261031` | 同一full源码solution实际0 error/0 warning |
| E-SDK | `foundation-nine-source-sdk-r1-20261031` | 两个独立生成插件经正式loader/host消费，14/14，实际exit0 |
| E-BRIDGE | `playback-nine-source-bridge-r2-20261031`、`playback-nine-source-bridge-symbols-r1-20261031` | 4进程187；5组PE/PDB、504文档，其中489冻结文档，生成文档另核 |
| E-THUNK | `playback-nine-source-trampolines-r2-20261031`、`playback-nine-source-trampoline-symbols-r1-20261031` | 5进程400；3组PE/PDB、183文档，其中174冻结文档 |
| E-GROUP | `playback-source-nine-group-r3-20261031` | 82进程3404，Prepare=Commit=711；三precapture均0/0，其余79均9/9；5PE/PDB456文档，同core-r4无overlay |
| E-NATIVE | `playback-stock-native-build-r14-20261031`、`playback-stock-native-checks-r14-full-20261031` | 14进程36675、3PE/PDB/5COFF/9完整正文；[独立报告](../playback-stock-native-targets-20261031/README.md) |
| E-FOUNDATION | `foundation-private-native-r3-20261031`、`foundation-private-mapped-r4-20261031`、`foundation-private-machine-r4-20261031`、`foundation-private-relocated-seh-r1-20261031` | member映像7进程2365、机械2226、实际跳板内AV/SEH17；[433文件证据封存](../foundation-private-attestation-20261031/README.md) |
| E-PRE | `playback-source-precap-xdata-final-r1-20261031`及其引用的r1/r2原件 | 同一旧mutation，当前native/CLR15项、5PE/PDB，零Prepare/Commit；汇总修正退出0，旧失败原样保留 |
| E-PACKAGE-PROTOCOL | `playback-nine-package-protocol-r1-20261031` | 当前package reader实际解析helper85个可执行导出/101归档源，四个PS文件语法0错；只验协议，不验完整包 |

当前source bridge、trampoline和九成员组均直接来自同一core，无生产/测试overlay。精确旧反例只有单独测试Program overlay，不能替代完整组矩阵。foundation联合映像结果将在实际完成后单列；core-r1/r3与旧两成员结果只保留历史范围。旧core-r3的82矩阵已过，但剩余mapped验证因源码改为r4明确取消；精确三个D-only进程的CommandLine/父链与终态保存在 `playback-source-nine-cancellation-r2-20261031/cancellation.json`，并非超时后重复启动。

## Evidence → Finding → Path

| Finding | severity / confidence / status | evidence_ids / 实际结论 |
|---|---|---|
| F-PRE：晚capture不能签原始展开事实 | n/a_re / high / validated | E-PRE、E-GROUP、E-CORE；同一旧mutation在任何Prepare前被固定文件比较拒绝，三precapture负例恢复字节后旧失败组仍拒绝 |
| F-THUNK：各source需独立精确搬移与展开 | n/a_re / high / validated | E-THUNK、E-NATIVE；八role正向及SEH/原跳板篡改专项，不能把GetTime六字节形状套给其余目标 |
| F-MEMBER：FirstInstall自己的实际member可发行不可复制的组合凭据 | n/a_re / high / validated | E-FOUNDATION、E-PACKAGE-PROTOCOL；成员发行与本身展开事实已证，完整共同组联合仍需最后映像矩阵 |
| F-GROUP：九个实际物理成员可以共享一个保留完整映像比较的安装组 | n/a_re / high / validated | E-GROUP；82个自有执行进程、三前置拒绝、711次真实Prepare/Commit及单hub共同撤回；这不是游戏运行证明 |
| F-JOINT：foundation与九成员的当前联合范围待实际闭合 | n/a_re / high / candidate | E-CORE、E-FULL、E-GROUP与当前源；最终mapped7未完成前不升级为整批联合验证或生产准入 |

P-PRE（`path_type=callflow`）：私有进程内旧xdata mutation → 固定文件RF/UW比较 → source owner构造拒绝 → 无Prepare/Commit、无group/lease。每步见E-PRE/F-PRE。

P-GROUP（`path_type=callflow`，实现路径，联合验收施工中）：pinned image与实际FirstInstall owner → opaque membership → broker内部九个member preflight → actual Dobby/独立原跳板/展开 → 完整file+DIR64/member组合 → arm与持续验证。已证步骤分别见E-FOUNDATION/E-THUNK/E-CORE；未用代码存在代替最终联合执行。

## 未完成与下一步

真实Pause/Replay/Resume/inlinePause的语义occurrence、当次actual schedule配对、selected-rate独立writer恢复/readback、七根/run/source/DSP、唯一final Clock与正式provider/module、同一物理安装跨局的旧票撤回/actual drain/条件移交仍未闭合。S2只在独立D staging实现，未合入本批生产树。任意Seek还须完整note/judge/hold/协程/input/result重建，不能只改AudioSource.time。

GameAssembly只使用D副本映像的DONT_RESOLVE，不调用原游戏方法、DllMain或真实source callback。自有Replay/Resume/Progress受控RF为3/3/3，固定游戏原件为5/5/4。未安装/启动游戏、未操作游戏UI/账号、未重包当前候选；真实Speed/Seek、production与完整B01–B19保持未完成。性能按用户安排后置。
