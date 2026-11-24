# Progress／Start 的独立身份凭据

2026-11-02 20:46 更新：本批连同六direct／旧leaf／MUG隔离和新owner-pump修复已实际合主57文件，当前889输入。新foundation-r2的28套2598、solution0/0、SDK14、六Release及mapped-r2的108已通过；formal-r2完整SDK worker37＋normal28实际通过，formal-r3已first102／same54／cross57／foreign61及native0/1各28通过，剩余十项仍在实际运行或排队。主目录fresh定向和符号验证另行进行，未签全部完成。见[组合主目录报告](2026-11-02_combined-main-integration-report.md)。

本批原施工位于 `D:/riz-build-cache-next/playback-progress-control-isolation-r1-20261102/`，以MUG三wrapper隔离后的P/managed-checks-r1的883输入为基线。以下17:58–18:22阶段记录保留历史，后续终态以上段和CURRENT-CHECKPOINT为准。没有安装／启动游戏或操作UI／账号；生产Speed/Seek关闭。

## 实际问题

P/n2只补到MUG player，Progress.Play、Progress.set_MusicPlayer和Start.MoveNext仍先进入共同native owner。`counterexample-r1` 两个实际exit1分别覆盖已有player watch和尚只有host关联的范围：972个foreign original均执行，却产生972个Before/journal，Source从1变649，Scheduling从1变325，worker令flags=6。这是缺少Progress身份来源的真实反例；固定P/n2原件没有被改写。

## 实现

保留SourceV4／168B frame／136B info和PlayerWatchV2／120B，新增正交私有ProgressWatchV1／96B。它借用四根初始化宿主或七根actual source中已有的独立Progress RootHandle，不额外伪造对象保留，不发行Clock、rate、generation或初始化完成权。

四根host在Start前绑定精确Progress。七根source将后继ProgressWatch和已staged的确切PlayerWatch receipt配对，native验证progress+0x38的player、progress+0x28的AudioSource和class；source-open在同一native锁内同时提交两个watch。旧host／旧source的native记录退休后保持不可复用，迟到旧retire不清除后继。部分staging失败沿用已注册owner的owner-thread根保留／drain；未证明watch退休或未知GC free时不把后续空操作当回收成功。

Progress.Play／setter先比较精确active/pending receiver；Start先安全读出enum+0x20指向的Progress并匹配保留身份，再进入InitializationEnter。foreign调用不推进journal、fence、thread fault或FreshBlocked，仍执行原bool／ref／BooleanReturn0原函数。匹配当前Progress而enum class错误时关闭当前权限。相关worker在原函数前Unknown并抑制managed图捕获；AL raw byte、确切SetClip CALL／MI／parent／单child与physical exit合同保持。

## 已闭合范围

| 范围 | 实际结果 | 本批目录内原件 |
|---|---|---|
| 固定P/n2反例 | 两实际exit1，972个original保留 | `counterexample-r1/test-result.json` |
| 新native build | `/W4 /WX`，actual0 | `n1/native-build-result.json` |
| foreign、host-only、current、pending、worker | 11进程3119，全actual0 | `native-checks-r1/test-result.json` |
| 加class故障、host→source及迟到旧退休 | 15进程4622，全actual0 | `native-checks-r2/test-result.json` |
| 新托管build／19组foreign、Runtime、first hold | build0 warning／0 error，三进程78 actual0 | `managed-checks-r1/test-result.json` |
| 真实四根／七根RootHandle借用、foreign AL1／AL0／SetClip／inline Pause／writer／worker、不污染current hold、真实current AL1后安全停用 | 两进程60 actual0，两次fresh build均0/0 | `managed-checks-r2/test-result.json`、`managed-checks-r3/test-result.json` |
| PE／PDB／实际源码 | 5 pairs、511个PDB文档、888输入 | `symbols-r1/test-result.json` |
| 新组合整树 | 28套2598，全部build/test exit0 | `foundation-r1/full-r1/test-result.json` |
| solution、独立双插件SDK、六Release | solution0 warning／0 error，SDK14，六Release0 | `foundation-r1/solution-r1`、`sdk-r1`、`release-r1` |

r2专项中host CLI名为progress-first-hold，内部旧result mode仍为direct-source-first-hold；r3仅修正该报告名并补正式SDK foreign的Progress调用，重新fresh build和两项60全部通过。r2符号证明签其888源码；r3变化在测试项，managed产品与foundation逐项对应须在最终整合时再次回核。

新helper下既有六direct/watch94／15556、旧五leaf27／7767、MUG三wrapper20／6735和原八role14／36675也分别actual0，原件位于 `direct-watch-regression-r1`、`legacy-regression-r1`、`player-regression-r1`、`original-eight-r1`。旧fixture未配置ProgressWatch的回归只签原有范围，不能代替本批四／七根联合。

Source helper SHA `bec5adc4e56f387f3bd6f5c47737095e9341f7d890512dfa30b04be451ea2a5c`，Single SHA `b18c320bd26a2296dd517d098de3f93d3f797023d2a060801110207ebc3886e4`；fixed owned fixture仍为625837。完整实现合同另在本D目录 `IMPLEMENTATION-CONTRACT.md`。

## 仍需验证和整合

18:22 新结果：`managed-checks-r5` 实际三进程81全部0，其中严格owner-pump诊断21。仅执行owner pump后，任何额外health/provider probe之前，Clock已Withdrawn；两个SDK clock代次1→2、game readings仍可用；SourceIdentity非空但NativeDescriptor=null；另一通用Acquire成功而SetSpeed拒绝，pitch未被多写，最后unknown七根仍保留。与L/r7的Paused／无SourceIdentity／raw game不可用／Acquire报UnsupportedSignature形成实际对照。该诊断不执行初始Speed，不代替仍运行的完整formal-r2 worker。

18:20 续接：当前Q源码已再补owner故障传播，不能用上方888／foundation-r1验收这次产品变化。实际L `worker-clock-contract-r7` exit0／18记录：native descriptor确已消失，整个SourceIdentity=null，两个时钟generation仍1、game readings不可用；三次owner pump后Clock仍Paused，后续旧lease调用才令其撤回，generic Acquire报UnsupportedSignature，最后保留七根。该诊断没有执行初始SDK变速，不替代完整worker场景；原full worker31失败与r6诊断13失败均保留。

Q新接线先poll保留的native source/Single健康，再消费owner故障队列；无managed callback的worker也能被owner pump发现。稳定host在没有当前armed session时先返回NotReady；物理组verify失败转为TargetExpired并关闭native rate，独立backend可表示“无native descriptor”，继续按其自身合同读取game字段。新产品不增加任何native支持或重新打开失效policy。

r4 actual build1因缺少Runtime命名空间保留；r5补using后fresh build0 warning／0 error，progress两专项已actual60，新增严格“owner pump后、任何额外probe前Clock必须Withdrawn”的诊断在跑。当前889输入／481产品输入；`symbols-r2` 已核新r5实际5PE/PDB／512文档。`foundation-r2` driver49764重跑这次产品变化；`formal-r2` driver20856的worker48312、normal27876使用新r5，均以实际句柄重核。r3旧formal-r1仍只签改前产品和新ProgressWatch范围，不借旧DLL验本次产品修复。mapped-r1仍是改前产品，其metadata69已0、joint待终态。

`formal-r1` 使用当前r3实际DLL／新n1，运行正式SDK normal、foreign（含Progress）、first、same-run和cross-run；driver49288于17:50后独立隐藏启动，实际句柄须实时重核，不因观察超时重跑。新fixed-image 19组、当前源码candidate及r3 PE/PDB仍须闭合，再按文件hash合主。L/n1与P/n2正在执行的正式用例各按自身scope，不能给本新helper验收。

L的r11已实际通过首次source-open102和native-U64 SDK28；worker实际exit1的合并断言尚在核对，它同时要求代次增加和非空identity内descriptor为空，不能仅凭该错误句声称恢复了原生控制。现有actual证据已证明worker原函数前Unknown、实际GetTime走original不进Clock reader、旧Clock/provider／lease撤回和七根保留。新的worker-clock-contract-r6记录实际nullable identity及代次，尚须终态。失败原件保留，不为通过测试放宽产品。

本批仍不证明完整source generation、未知lower／icall／Playable、生产首次准入、任意Seek、effect或全B01–B19完成。
