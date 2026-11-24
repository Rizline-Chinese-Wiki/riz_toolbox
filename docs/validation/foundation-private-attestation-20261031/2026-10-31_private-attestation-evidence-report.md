# 私有 FirstInstall attestation 源与实际产物审查

本次只读审查支持当前私有 pre-Prepare 凭据实现的四组实际结果：native-r3 编译exit0、mapped-r4 七进程2365 checks、machine-r4 2226 checks、relocated-seh-r1 17 checks。独立离线审查实际exit0，核对了5组PE/PDB、31个COFF对象、73条PDB模块到COFF源校验值的绑定和101项native归档源；9项相关托管源与core-r3冻结副本逐一一致。没有发现需要修改冻结代码的问题。本项没有执行游戏、安装候选、重包或证明production准入；共同组的完整运行验证由父任务单独报告。

2026-10-31，`flavor=null`。授权、排除项和离线运行边界见 [scope.md](scope.md)，修订及失败保留见 [timeline.md](timeline.md)。最终正式helper SHA-256为 `c5ad691361e7f8ecc71bdf2a0efe98a446c1943234e1a5fea3be93787e712c1c`，配套PDB为 `15ac59a462550309f0a97ba43c4f0a6ed7af932200b985529bcf87ad137f5500`。可机读的全部记录见 [evidence-index.json](evidence-index.json)，字段完整的E/F/P见 [evidence-records.json](evidence-records.json)。

## 实际证据

以下路径均在 `D:/riz-build-cache-next/` 下。哈希为实际文件字节的SHA-256；本次只读取已保存的退出码、stdout、编译符号及产物，不重新执行这些历史进程。

| Evidence | source_ref | content_hash | 观察及适用边界 |
|---|---|---|---|
| E-NATIVE | `foundation-private-native-r3-20261031/native-manifest.json` | `a809f75b0819d64149cc0abdc9b4e3d7f54b70b0347fe3f7cfc9191f1811fe26` | buildExitCode=0；101源当前SHA及原归档均一致；V7仍2528B/26ops/64writes，新只读ABI为120B/184B/28originals。 |
| E-MAPPED | `foundation-private-mapped-r4-20261031/test-result.json` | `8b6a103a5deb6e31a67b76553951990c5b704a029d98e736b9fb534b417c545d` | actualExitCode均0；normal383、prewrite-foreign12，其余5模式各394，共2365。各模式stdout的唯一JSON与结果文件完全一致。 |
| E-MACHINE | `foundation-private-machine-r4-20261031/test-result.json` | `2751b771a8b2b667081e40bdefc6d4fc3b121f133af63c91ad035109b6c63052` | build/actualExit均0；2226checks、28 Prepare/Commit、127真实单步CONTEXT、28body SEH、112body调用、84entry调用。 |
| E-RELOCATED | `foundation-private-relocated-seh-r1-20261031/test-result.json` | `04768af3412704216405f3306c8a63179d40c585060bd96528de9da5ce6dabb0` | build/actualExit均0；17checks；一次真实AV发生在normalized original的+4，读地址0x10，实际异常CONTEXT经动态RF/UW恢复。 |
| E-REVIEW | `foundation-private-proof-r1-20261031/evidence-review.json` | `57e70a4f685ecd6a08807c4d2668d686e47cc0872f84d1e1feba0d05d24501ff` | 本次离线审查exit0；5组PE/PDB、31 COFF、73条PDB模块绑定、115源review归档、9项core-r3相关源同hash。 |

`E-REVIEW.imageSurveys` 保存五个AMD64 PE的完整导入/导出清单。正式helper依赖bcrypt、MSVCP140、KERNEL32、VCRUNTIME140/140_1及三项CRT API-set；85项EAT均为本地、可执行、不可写section内入口，无forwarder、ordinal空洞或fixture导出。自有machine/relocated fixture链接native-r3的21个真实对象，另以compile-time宏编译同一FirstInstall translation unit；它们不发行production owner/token。原native ABI fixture只是被构建及审查符号，未在native-r3执行，不计为测试成功。

PE的RSDS GUID/age与实际MSF PDB一致；DBI的精确object路径及其完整C13 checksum多重集与实际COFF一致；COFF的源校验值再回核当前文件。原native zip的101项与manifest一致。新review zip是审查时封存的115项源副本，SHA为 `d1587eb0b4d3efaac4ce8a97a0b2f0a8c837debb51b649f9c2f83ab0a5853309`，它不被冒充为历史编译前归档。

## 只读实质审查与发现

| Finding | severity / status / confidence | evidence_ids | location及结论 |
|---|---|---|---|
| F-OWNER | n/a_re / validated / high | E-NATIVE、E-MAPPED、E-REVIEW | `FirstInstall.cpp::Attempt`在首个Prepare前调用私有Begin；精确固定文件/映像/DIR64基线进入终身owner，28实际Prepare原件和64实际write进入私有journal；公开reader没有capture/register/mask入口。 |
| F-ORIGINAL | n/a_re / validated / high | E-MACHINE、E-RELOCATED、E-REVIEW | `FirstInstallPlaybackAttestation.inl::Prove/VerifyOriginal`保留实际raw Dobby原件，另建独立RX的R11 normalized original与完整RF/UW；不改raw arena字节/权限。有限28项前缀闭集；synthetic边界、真实单步、正常返回、body SEH及跳板内部AV各按实际范围证明。 |
| F-IDENTITY | n/a_re / validated / high | E-NATIVE、E-REVIEW | 当前helper/PDB/COFF/原归档源闭合，9项相关managed源与core-r3冻结源同hash；完整计数不跨旧修订累计。 |
| F-COMPOSITION | n/a_re / candidate / high | E-REVIEW、E-MAPPED | `NativeFirstInstall.PlaybackMembership.cs`、`ExactHookBroker.Playback.Group.cs`、`NativePlaybackExecutableSeal*.cs`静态路径核对完成：实际CLR `_owner`及唯一membership引用，native header/64rows/28原件身份交叉核，before与pinned file+DIR64比较再compose after。共同组完整运行结论不由本项四组测试替代。 |

native在Publish前比较完整headers、executable和只读sections，并逐次核映像/Prepare/write及原件；非executable的PE声明IAT仅在初始loader绑定后立刻封存，没有调用者排除参数。Publish后native reader核自身完整body、continuation、元数据、64实际write与28对raw/normalized original及UW。它允许后续合法stock/GetTime成员改动组外目标；`outside`模式故意保持native reader可读，不能据此声称全映像无未知修改。共同组负责完整executable file+DIR64与全部实际成员精确组合。

CLR构造器私有，Create核实际`_owner`，持续验证还要求该对象等于owner已登记的唯一membership。Ready DTO、copied row、copied packet/token或caller mask不能沿正常发行路径形成current membership。native/managed Unknown和成员fault为sticky，恢复字节不复活。以上是内部受信CLR与native协议的工程身份约束，不宣称对拥有任意反射/内存写权限的进程内代码建立安全隔离。

## Evidence → Finding → Path

P-ISSUE：`path_type=callflow`；start为实际FirstInstall Attempt，goal为可由真实CLR owner持有的current membership。

1. 固定文件哈希与deny-write/delete handle → Begin按原文件、精确DIR64、零填充及唯一ImageBase loader变化核映像。证据E-NATIVE/E-MAPPED/E-REVIEW，发现F-OWNER。
2. 28实际Prepare → raw Dobby逐指令/continuation核对 → 独立RX normalized original → 每前缀状态RF/UW → 实际lane配置。证据E-MAPPED/E-MACHINE/E-RELOCATED，发现F-ORIGINAL。
3. 实际WriteSource/Commit → 64条before/after journal → Publish → readonly Get/Read持续核验。证据E-NATIVE/E-MAPPED，发现F-OWNER。
4. 实际CLR `_owner` → 唯一opaque membership → before==pinned file+DIR64 → 组内after组合。证据E-REVIEW/E-MAPPED，发现F-COMPOSITION；共同组实际运行覆盖仍由父任务整合结果独立给出。

`residual_risks`：有限固定profile与自有机器fixture不能证明全部真实游戏控制流；没有真实游戏方法/init/DllMain执行，没有候选包验包或安装结果，没有production首次写入准入。当前package脚本精确85EAT/120B/184B/28字段的更新不等于当前候选包已经重包并通过安装。managed-r1目录只观察到产物，没有落盘build退出证据，不在本报告补推该退出码。

## 独立复核与失败保留

从项目根目录执行以下离线命令；输出目录必须尚不存在。它核对保留结果和当前源，生成新的review JSON、源zip与完整历史哈希，不加载被审查的PE。

```powershell
& 'C:/Python314/python.exe' -B './docs/validation/foundation-private-attestation-20261031/Verify-Evidence.py' --output 'D:/riz-build-cache-next/foundation-private-proof-reproduce-20261031'
```

如需重新执行专项测试，使用[README](README.md)中的四条现行构建/测试命令并指定全新D目录，随后重新核源和产物；不能用本报告的历史exit证明改变后的源码。14个原目录和297个文件全保留，索引区分current和historical。machine-r1的actualExitCode=1/29failures可独立回核；mapped-r1的编译错误日志也保留，但没有独立落盘build exit字段，本报告不把文字转述的shell exit1当作可再次审计字段。

本次完成清单：已读docs-generator及实际可用tool-index/E→F→P规则，选择flavor=null；scope、timeline、E/F/P、复核命令、当前与历史hash完整；五PE导入清单及符号链保留；没有代码/根规范更改、没有游戏/UI/账号/安装、没有commit/reset；production与候选验包边界明确。
