# FirstInstall 私有 pre-Prepare 凭据与共同组采纳

2026-10-31，`flavor=null`。新增真实 FirstInstall 的原文件封存、28 个实际 Dobby 原件、独立 RX original/完整展开，以及只读凭据接口；CLR 仅由实际已登记的 `NativeFirstInstall` owner 发行不可复制构造的 `PlaybackMembership`。当前 V7 仍为 **2528B / 26 operations / 64 writes**，新版正式 Judge helper 增至 **85 exports**。这项发行能力独立于 production 首次写入准入；没有开放真实 Speed/Seek/provider。

## 范围与结果

范围沿用 [N2 合同](../../../.docs/foundation-expansion/N2-STOCK-SOURCE-NEXT.md)。只在 D 盘副本映像与自有机器代码执行测试。GameAssembly 采用 `DONT_RESOLVE_DLL_REFERENCES`，GameAssembly 原方法调用、原 `il2cpp_init`、GameAssembly DllMain 均为 0；没有新候选安装、游戏启动、UI 或账号操作。没有安全攻击 case、IOC 或 ATT&CK 归属。

| 证据 | 实际结果 | 原件及 SHA-256 |
|---|---|---|
| E-NATIVE | 新 helper 编译 exit0；独立 PE EAT 核 85 个本地 RX export；101 条 native source inputs 回核无变化 | `D:/riz-build-cache-next/foundation-private-native-r3-20261031/native-manifest.json`；`a809f75b0819d64149cc0abdc9b4e3d7f54b70b0347fe3f7cfc9191f1811fe26` |
| E-MAPPED | **7 个进程 / 2365 checks**，全部 actual exit0；normal 383、prewrite-foreign 12、patch/original/unwind/outside/continuation 各 394 | `D:/riz-build-cache-next/foundation-private-mapped-r4-20261031/test-result.json`；`8b6a103a5deb6e31a67b76553951990c5b704a029d98e736b9fb534b417c545d` |
| E-MACHINE | **2226 checks / actual exit0**；28 Prepare、28 Commit、127 个真实单步机器 CONTEXT 展开、28 次原 body SEH、112 次原 body 调用 / 84 次 entry；正常返回逐一核八个非易失 GPR 和 XMM6–15 | `D:/riz-build-cache-next/foundation-private-machine-r4-20261031/test-result.json`；`2751b771a8b2b667081e40bdefc6d4fc3b121f133af63c91ad035109b6c63052` |
| E-RELOCATED-SEH | **17 checks / actual exit0**；一次真实访问异常直接发生在 normalized original 的 `+4`，读取 `0x10`；异常上下文经其实际动态 RF/UW 恢复 RIP/RSP/GPR/XMM | `D:/riz-build-cache-next/foundation-private-relocated-seh-r1-20261031/test-result.json`；`04768af3412704216405f3306c8a63179d40c585060bd96528de9da5ce6dabb0` |

最终 helper 为 `D:/riz-build-cache-next/foundation-private-native-r3-20261031/RizToolbox.JudgeContext.dll`，SHA **`c5ad691361e7f8ecc71bdf2a0efe98a446c1943234e1a5fea3be93787e712c1c`**。自有机器 fixture 编译同一 `Prove` builder，并链接该 helper 构建的 21 个实际对象；它的 compile-time seam 无法发行生产 owner/token，也不在正式 helper EAT 中。

`D:/riz-build-cache-next/foundation-private-managed-r1-20261031/` 保留早期 Runtime 产物；本项没有找到该单独构建的落盘 exit/log，不从产物推断退出码。共同 9 成员 + foundation 的最终 CLR/mapped 整合由父任务独立执行，不能用本页四组计数替代其结果。

独立离线审查已 actual exit0：5 组 PE/PDB、31 个 COFF、73 条 PDB 模块到 COFF 的编译源 checksum 绑定、101 项 native 原归档源，以及 9 项相关 managed 源与 core-r3 冻结副本均匹配。完整边界与 E→F→P 见 [证据审查报告](2026-10-31_private-attestation-evidence-report.md)、[scope](scope.md)、[timeline](timeline.md)。本页没有候选包重包/安装或生产准入通过结论。

## 私有发行路径

1. 实际 `Attempt` 在首个 DobbyPrepare 前完成 Game/Dobby 固定 SHA 校验，并把 deny-write/delete 的 file handle 复制进进程终身 owner。按 pinned file、精确 AMD64 DIR64、零填充和唯一合法 ImageBase 变化比较完整 executable 及只读 section。非 executable 的 PE 声明 IAT 由 loader 绑定后立即封存；调用者没有排除区间参数。
2. 28 个实际 Prepare 返回的 raw Dobby 地址/字节各自保留。闭集逐指令核真实 fixed prefix、搬移长度及 `FF25` 的精确 continuation。另分配独立 RX code，把该尾部规范成 `MOV R11,continuation; JMP R11; NOP`；**不改 raw Dobby arena 的任何字节或页权限**。
3. Native lane 及 V7 receipt 配置的是该独立 normalized original。每个完成前缀状态有独立 RF/UW，全部为有限生成的完整无 handler unwind；逐边界核 OS lookup 和 `RtlVirtualUnwind`。synthetic context 将已经保存的寄存器毒化，防止等值寄存器掩盖错误 SAVE offset。
4. 只有实际 `WriteSource` 和实际 Dobby Commit 路径能推进 64 条 private before/after journal。每次 Prepare/write 前和最终 Publish 前都核完整映像；source write 的前后字节、页身份与原跳板均核对成功后才登记。任何不明前置修改不能由晚 snapshot 合法化。
5. CLR 调用 `NativeFirstInstall.RequireReady().RequirePlaybackGroupAdoptionEvidence()`。私有 factory 必须是进程实际 `_owner`，且登记唯一 membership 对象。构造和持续验证消费 native header 及全部 64 rows，交叉核 28 个实际 configured original/entry/epoch。公开 V7、copy row、copy token packet、任意 mask 均不能构造 current membership。

raw Dobby 原件与 normalized RX 原件都在 Get/Read 时逐字节及逐 covered memory fact 核验。RF/UW block 必须先等于私有原件，才会交给 OS unwind。所有文件句柄、原件、代码、表和失败责任保留到进程结束；没有撤销后重新签发或地址复用。

## 发布后验证职责

native Get/Read 在 Publish 后持续验证 **自己的完整 source bodies、其 continuation、只读元数据、64 条 actual writes、28 对 raw/normalized originals 及完整 UW**。它不宣称组外 executable bytes 仍等于原文件；后续合法 GetTime/stock hook 可以改变自己的目标。

共同组在首次采纳及每次验证都必须使用真实 private membership：将 `before` 与 pinned file + 精确 DIR64 比较，再组成 `after`；组外及其余成员仍由共同组全映像验证。`outside` 模式明确验证 native member reader 保持可读，不把这个结果当全映像无未知修改证明。未知组外改动必须由共同组拒绝，不能放入 native caller mask。

`PlaybackMembership` 仅提供内部 `VerifyCurrent()`、新的地址/宽度副本 `ImageRanges()` 与 `ComposeIntoPinnedFile(rva, Span<byte>)`；内部原字节不可变，构造器私有。Prepare/Commit 未完成、owner 不同、所有权对象不对、header/row 改变或 native Unknown 都拒绝。member fault 与 managed producer Unknown 均 sticky；恢复字节不会复活。

## 独立 ABI

仅新增两个只读/验证 export：

```text
RizFirstInstall_GetPlaybackAttestationV1(info*)
RizFirstInstall_ReadPlaybackAttestedRangeV1(token, index, range*)
```

返回 0 表示完整且当前；1 是无效 header/token/index；2 表示不可用/Unknown。Prepare 中不会读取正在增长的 vector；只在实际 Ready 的 acquire 边界之后读取完成的 registry。Unknown 路径仅复制有界的 Unknown 头，不能采纳部分表。公开 reader 不含 capture/register/write 操作。

| Header 字段 | offset / 大小 | 内容 |
|---|---|---|
| version/size/state/unknown | 0 / 16 | V1、120B、published=3、known=0 |
| token / epoch | 16 / 16 | 实际 private owner 地址、唯一进程代次 |
| module/helper/dobby | 32 / 24 | 实际保留的模块身份 |
| imageSize/originalCount/rangeCount/completedWrites | 56 / 16 | 固定 image、28、64、64 |
| supportedOriginals/lookupChecks/boundaryChecks/reserved | 72 / 16 | 28、155、155、0 |
| fileSha256 | 88 / 32 | pinned GameAssembly SHA；总 120B |

| Range 字段 | offset / 大小 | 内容 |
|---|---|---|
| version/size/kind/id | 0 / 16 | V1、184B、Dobby 或实际 inline write、实际 journal index |
| address/original/entry/epoch | 16 / 32 | 真实目标、实际 configured RX original、entry、native epoch |
| functionTable/functionTableBase | 48 / 16 | 自有动态 RF 及 normalized code base |
| length/protection/movedByteCount/originalByteCount | 64 / 16 | 源宽度及有限搬移凭据 |
| functionCount/unwindByteCount/lookupChecks/boundaryChecks | 80 / 16 | 完整表/展开覆盖 |
| reserved0/reserved1 | 96 / 16 | 全 0 |
| beforeBytes/afterBytes | 112 / 64 | 各 32B，只有 length 内生效 |
| continuation | 176 / 8 | 原 source + exact moved count；总 184B |

raw Dobby 地址只在 native 私有 owner 中保存。Range.original 始终是实际配置进 runtime/Unity/operation lane 的独立 RX original。

## Evidence → Finding → Path

| Finding | Evidence | 已验证路径与边界 |
|---|---|---|
| F-OWNER：可实际发行 pre-Prepare member | E-NATIVE、E-MAPPED | 实际 Attempt → pinned file/reloc → 28 Prepare → 64 writes → Publish → native Get/Read；不是 Ready DTO 重建 |
| F-UW：有限闭集完整搬移与展开 | E-MACHINE、E-RELOCATED-SEH | actual raw Dobby → 独立 RX normalized → 真实指令边界/正常返回/原 body SEH/跳板内部 AV；自有目标，不是 GameAssembly 原方法执行 |
| F-COMPOSE：可被共同组精确组合 | E-MAPPED 与当前 private CLR 源码 | private membership before==pinned file → after；自身篡改 sticky，组外当前性由共同组独立闭合 |

## 复现与失败保留

以下命令只用既有本机工具和 D 盘副本。请换全新输出目录，不覆盖任何历史原件。

```powershell
./scripts/Build-JudgeContext.ps1 -OutputDirectory 'D:/riz-build-cache-next/review-private-native' -SkipFixture
./scripts/Test-FirstInstallPlaybackAttestation.ps1 -OutputDirectory 'D:/riz-build-cache-next/review-private-mapped' -HelperDirectory 'D:/riz-build-cache-next/review-private-native' -GameAssemblyPath 'D:/riz-build-cache-next/playback-stock-joint-profile-r2-20261031/images/normal/GameAssembly.dll' -DobbyPath 'D:/riz-build-cache-next/playback-stock-joint-profile-r2-20261031/native-inputs/dobby.dll'
./scripts/Test-FirstInstallPlaybackMachine.ps1 -OutputDirectory 'D:/riz-build-cache-next/review-private-machine' -HelperDirectory 'D:/riz-build-cache-next/review-private-native' -DobbyPath 'D:/riz-build-cache-next/playback-stock-joint-profile-r2-20261031/native-inputs/dobby.dll'
./scripts/Test-FirstInstallPlaybackRelocatedFault.ps1 -OutputDirectory 'D:/riz-build-cache-next/review-private-fault' -HelperDirectory 'D:/riz-build-cache-next/review-private-native' -DobbyPath 'D:/riz-build-cache-next/playback-stock-joint-profile-r2-20261031/native-inputs/dobby.dll'
```

`foundation-private-mapped-r1-20261031/build.log` 保留第一轮 fixture 编译失败：auto 初始化引用自身 `sizeof(bad)`；该目录没有独立落盘 build exit 字段，因此审查仅核编译错误日志。改成明确类型后才执行后续正例。`foundation-private-machine-r1-20261031/test-result.json` 保留实际 exit1 / 29 failures：Windows 异常投递清 TF，原验收只得到 28 个入场 CONTEXT；修正为每一步显式 rearm TF 后得到 127 个真实边界。两项失败没有被改写成成功，也不与最终结果混算。

native-r1 与早期 mapped-r2 是历史隔离正例，曾直接规范共享 Dobby pool；已由 raw/独立 RX 分离版本替代，不用于当前组合。完整源文件、当前与历史产物哈希、实际退出见 [evidence-index.json](evidence-index.json)。本次审查封存实际 115 项相关源（包含审查脚本），位于 `D:/riz-build-cache-next/foundation-private-proof-r1-20261031/reviewed-source-snapshot.zip`；这是审查时副本，另有 native-r3 的101项原构建源归档。9项托管相关源已只读核对为父任务 core-r3 同hash。
