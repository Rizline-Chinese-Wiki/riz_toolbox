# GetTime 与 stock 来源共同安装组：当前源码离线整合

2026-10-31，`flavor=null`。本包实现两个物理成员的共同安装、native typed source 与私有 current/完成桥，修复故障撤回和在途责任的实际反例，并以新的源码快照完成全部托管、Release、解决方案及 SDK 验证。完整 stock provider 尚未实现，真实 Speed/Seek 与生产准入保持关闭。范围见 [scope](scope.md)，执行记录见 [timeline](timeline.md)；没有安装或启动游戏、执行游戏原方法/DllMain、操作游戏 UI 或账号。完整 N2、Seek 和 B01–B19 目标均未完成。

## 改动与实际行为

`ExactHookBroker` 新增私有 GetTime＋SetPitch 安装组与具体 lease。共同组保留唯一 module、file hash、epoch、两份 metadata alias 身份及实际安装 owner；Prepare、Commit/readback、完整映像验证通过后才 arm。原文件、精确 DIR64 relocation、组外 executable 字节继续比较；组内两段也逐字节核已签 patch、未改正文尾、原跳板与独立展开。没有 caller mask、地址表或公开 Ready 凭据的准入口。既有同 broker 的 GetTime owner 可被其实际私有身份采纳，不重复安装 GetTime。

SetPitch 的真实前六字节为 `40 53 48 83 ec 30`，它使用独立原搬移/R11 回原跳板与 unwind，未把 GetTime 的保存 RBX/PUSH RDI 模板通配给所有函数。GetTime 与 SetPitch 两个成员的 patch、尾、原跳板、Dobby relocation、relay、unwind，以及组外代码或 metadata 变化，都会共同撤回且恢复字节不复活。

新独立 source helper 使用 160B frame、136B info，提供 Void0、Single1、Double1、Reference1、Boolean1 五种参数 ABI和八个来源角色。native 同一 owner/TLS 保留实际 self、原 MI/caller、原参数 bits、parent occurrence、writer/source/scheduling fence、Before/Returned/Abnormal 和真实 in-flight。原函数保持参数并调用一次；managed observer 异常不跨 native 边界，也不替换原异常。native `__finally` 真正退出后，才生成 physical-exit journal；After 回调中不能提前拿到退出票。

Runtime 的 SourceHub/Bridge 通过实际 native current scope、private issuer 和登记的确切 observation 对象签票。复制 frame、改 token、重新构造相等对象都不产生 authority。callback 内 Dispose 只申请清理，实际退出前保留 journal/managed owner；之后 Pump 按原 observation 恰好释放一次。clean logical withdrawal 拒绝新 authority，但保留已捕获 original return、实际在途和完成回执。Unknown 保留责任而不冒报零计数。

fixed metadata resolver 保留十个声明、SetPitch 双 MI 单物理入口；stock profile 固定九个潜在 hook 正文和五个 reader/consumer 正文。本轮真实共同安装只包含 GetTime/SetPitch，剩余七个物理入口仍未建立独立 stock prologue、actual Dobby 搬移及展开安装证明。owned fixture 的其它 wrapper 只能证明参数 wire 与调用事实，不能代替这七个实际形状。

## 反例与修复

| 实际反例 | 修复与当前证明 |
|---|---|
| source 原函数 SEH 后只留下 Abnormal，整个共同组未同步 sticky 撤回 | concrete SetPitch owner 在实际 Abnormal 及 subscriber exception 路径同步 Fault；当前 group source-seh 与 fault 专项验证原异常/原调用责任和全组撤回 |
| 撤回后调用 grouped GetTime ReadActivity，被 VerifyCurrent 的 Active 条件升级成永久故障 | grouped 分支读取共同组保留活动；当前 withdraw-inflight 验两成员与 group 的 Known/实际在途，真正退出前不丢责任 |
| 映像 joint-r1 pitch-tail 改动恢复后，managed 成员已失效，但 native AuthorityActive 仍 true | SingleReturnBridge 的 ReportFault 在原 publication 锁内 once-only 标 Unknown，再按私有 `_published.Revision` 条件 withdraw；不清 publication、不撤 foreign revision。joint-r2 三类改动恢复后四 authority 均 false |
| source callback 中提前 Dispose，原生尚未退出 | 保留私有 observation/journal 至 native finally；bridge deferred-release 与 group 300-call deferred stream实际验证清理顺序 |
| 新 native r5 重定位检查误将加载后 OptionalHeader.ImageBase 当 disk preferred，且把 MASM `.text$mn` 贡献当 `.text` | 读取实际 fixture 文件 header，独立核 PE `.text` DIR64 与 COFF `.text$mn` ADDR64；fresh r6 实际 13 进程通过，r5 的 13 个非零退出原样保留 |

source-callback-fault 新进程中，用两个不同 fixture module/group 分别在实际 Before 与 Returned 抛异常；原 setter 各正常一次，完成回执可释放，随后 dormant 调用不再签票。clock-reader-fault 新进程实际 GetTime 原函数正常返回后，读取 sample 抛异常；原 Single `3` 的 raw bits `1077936128` 保留，native policy 与双方成员撤回。这两进程是额外 **62** 项，不复跑或改写原 19/941。

## 当前源码与验证

当前 core 原件为 `D:/riz-build-cache-next/foundation-stock-source-core-r1-20261031/test-result.json`，SHA **`8e3778772f18adcae23c2df42714f603e0c72d4e093920fbf298772083b59017`**。720 份 snapshot inputs 固定后，各专项与 full/Release 均使用该凭据。group19 的 D-only Program.cs 只增加本进程只读 ASLR/DIR64 诊断；另两个 focused 进程使用独立明确的行为测试 Program.cs。两个 overlay 的前后源码、实际 PDB document checksum 与原 core input 分别保存，未改生产源码或工作树测试。

| Evidence / scope | 实际结果 | 原件 |
|---|---|---|
| E-CORE | actual exit0；Audio1001、coordinator28、receiver10 | `foundation-stock-source-core-r1-20261031/test-result.json` |
| E-FULL | 27 套 **2465/2465**，每套 build/test exit0 | `foundation-stock-source-full-r1-20261031/test-result.json` |
| E-RELEASE | 六 DLL、0 error；一个既有 generated nullable warning | `foundation-stock-source-release-r1-20261031/managed-build-manifest.json` |
| E-SOLUTION | actual build exit0，**0 warning / 0 error** | `foundation-stock-source-solution-r1-20261031/test-result.json` |
| E-SDK | 两个独立生成插件经正式 host，**14/14**，actual exit0 | `foundation-stock-source-sdk-r1-20261031/test-result.json` |
| E-BRIDGE / E-BRIDGESYMBOLS | 四进程 **187**；每进程真实 Prepare8/Commit8；五 PE/PDB、**502** 文档 checksum | `playback-stock-source-bridge-r1-20261031`、`playback-stock-source-bridge-symbols-r2-20261031` |
| E-GROUP | 19 进程 **941**；Prepare38/Commit38；1 个既有 GetTime owner 采纳；每进程实际 ASLR 与 `.text` DIR64 正向；五 PE/PDB、451 文档 | `playback-group-managed-build-r6-20261031/test-result.json` |
| E-GROUPFOCUSED | 两个独立进程 **62**；三个实际 group，Prepare6/Commit6；测试行为 overlay | `playback-group-consumer-faults-build-r1-20261031/test-result.json` |
| E-MAPPED | 四进程 **84**；每个实际双 Commit，revision1→2→3；五 PE/PDB、467 文档；游戏/原调用/DllMain/source callbacks均0 | `playback-stock-joint-profile-r2-20261031/test-result.json` |
| E-NATIVEBUILD / E-NATIVECHECKS / E-NATIVESYMBOLS | native r6 **13 进程 / 27037**，各自真实 Prepare8/Commit8；三 PE/PDB、五 COFF/C13；实际 `.text` DIR64 | `playback-stock-native-build-r6-20261031`、`playback-stock-native-checks-r6-20261031` |

表内路径均在 `D:/riz-build-cache-next/`。上述计数有不同 scope，不相加为功能总覆盖率。native helper 本轮最终 SHA 为 `a96160f46ca952ed0b6097478dc44efff840127d52d65d3ea43e5ba01c314083`，fixture 为 `cc45d2a47f99180443fa1d2a839c0567c66cd7e7bacdb7a59c9af346d53c29fc`；均为独立原型，未加入正式 V7 helper 或候选。

[integration-inputs.json](integration-inputs.json) 固定十三份主 manifest，[完整Evidence字段](evidence-records.json)保留source_ref/content_hash/observed_at/repro_command等；[seal-integration.py](seal-integration.py) 实际核对共同 source/native proof、退出码/结果、显式 overlay、当前文件与符号输入。最终 seal-r2 为 **5787 个唯一文件 / 763 条当前工作树输入**，结果与完整 hash 见 [test-result](test-result.json) 和 [evidence-index](evidence-index.json)。seal-r1 把 pre-overlay copy record 错当当前 post-overlay 文件 hash，验证器及失败观察原样保留；修正只涉及证据阶段解释，没有重跑已过测试。bridge-symbols-r1 文档汇总字段为空，最终只采用明确计数并核五对文档的 r2。

## Evidence → Finding → Path

| Finding | severity / status / confidence | evidence_ids / 结论 |
|---|---|---|
| F-GROUP | n/a_re / validated / high | E-GROUP,E-GROUPFOCUSED,E-MAPPED：两个实际成员可共同安装、原有 GetTime owner 可采纳；故障/consumer变化同步撤双方与 native policy，未证明 foundation 组合 |
| F-SOURCE | n/a_re / validated / high | E-BRIDGE,E-BRIDGESYMBOLS,E-NATIVECHECKS,E-NATIVESYMBOLS：真实 native scope 可签私有 wire/完成事实，withdraw/fault 仍保原调用责任；不等于完整 stock occurrence/provider |
| F-CURRENT | n/a_re / validated / high | E-CORE,E-FULL,E-RELEASE,E-SOLUTION,E-SDK：当前源码整体验证完成；新增 CLI 专项数与27套分开，未借旧2465、return19/159、Play七模式或退休结果 |
| F-REMAINING | n/a_re / validated / high | E-GROUP,E-MAPPED,E-NATIVEBUILD：foundation 私有原搬移/展开凭据、剩余七物理入口、完整 source/writer/provider、跨局 handoff与真实准入仍缺；默认不开放真实 Speed/Seek |

P-GROUP，`path_type=callflow`：固定 file/DIR64 → private broker group → 两 actual Prepare/Commit → 各自原跳板/展开/精确 patch → 最后 arm → 持续两 profile/consumer 验证 → 变化触发共同 sticky 撤回（F-GROUP；E-GROUP,E-MAPPED）。

P-SOURCE，`path_type=callflow`：自有 actual native entry → native TLS/current Before → private observation → 原函数一次与实际 Returned/Abnormal → native finally physical-exit journal → exact completion/once-only release（F-SOURCE；E-NATIVECHECKS,E-BRIDGE）。残余范围是 raw wire 到七根、stock occurrence/actual schedule/writer reservation/final Clock/provider 的完整语义组合；本包不以该 Path 宣称生产来源成立。

## 下一功能与复现

下一步无需重做两个成员、单目标返回桥、metadata/profile或相机/N1/退休。先按 [NEXT-SOURCE-HOOKS](../playback-stock-native-20261031/NEXT-SOURCE-HOOKS.md) 为剩余七物理目标逐一建立实际前缀/搬移/continuation/unwind与准确 fixture；优先独立 Double1 PlayScheduled、ALLOC20 的 Pause/AudioPause，再推进 set_clip、Replay/Resume 与 Progress.Play 的7B flags/inline Pause。同时补 foundation 私有 pre-Prepare 原正文及完整展开证据的发行/采纳 seam，不能用 Ready、晚 snapshot 或 caller mask 替代。

随后完成 stock/leaf私有 occurrence、continuous writer/source reservation、七根/run association、唯一 final Clock构造/arm与正式 provider/module采用；跨局需同一物理安装上的旧票撤回、actual drain、新 session/revision conditional transfer。任意 Seek 与全部 B 项继续独立核对。性能和真实实测按用户安排后置。

复核本包 evidence 使用全新 seal 目录，不重新执行游戏或旧专项：

```powershell
$freshSeal = 'D:/riz-build-cache-next/stock-source-seal-repro-' + [guid]::NewGuid().ToString('N')
& 'C:/Python314/python.exe' 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-integration-20261031/seal-integration.py' --plan 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-integration-20261031/integration-inputs.json' --output $freshSeal
```

专项重新执行时分别使用仓库 `Test-PlaybackSourceBridge.ps1`、`Test-PlaybackStockJointProfile.ps1`、`Test-PlaybackSourcePrototype.ps1`，或两个 group wrapper 中已冻结的 runner/overlay；参数和实际原件均在各主 manifest，所有新执行要求不存在的 D 目录。历史失败不能覆盖，新源码必须重新 freeze/build，不能只重写 hash。
