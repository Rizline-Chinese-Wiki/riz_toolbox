# Operation 预初始化发布与 CLR 采用：有限复核

2026-10-24。本agent为native producer/Operation实现作者，对自身代码是作者自查；对另一代理的V3 CLR采用链进行了有限只读交叉复核，不能称独立全审。root是唯一builder并已观察94416完整terminal exit0；本agent实际读取runner、summary和原始JSON，核25套1579/1579。独立重算546当前输入/构建前/归档清单、两个ZIP每项及22暂存包全部匹配，旧安装18、历史326源码ZIP和原游戏3项也已实核。本agent未构建、运行fixture、安装、启动游戏或操作UI；冻结后仅写本review。

关键源码结论：生产默认Operation绑定已从LOCAL阶段的Prepare/Configure/Commit切换为RequireReady V3→exact row采用→一次Attach；缺少真实producer即拒绝。原explicit bridge factory overload仍为internal受控测试/自有fixture路径，生产Adapter两处调用默认入口，未找到晚期安装fallback。此结论需与自有transport、固定映像发布和实际游戏冷启动三个证据层分开，不将前两者拼成第三者。

## Evidence

source_type=file/log；observed_at=2026-10-24；source_ref同时为artifact_path。flavor=null，普通工程审查。当前scope见[scope.md](scope.md)，精确native ABI/slots与发布顺序见[native-plan.md](native-plan.md)。

| E-id | source_ref / artifact_path | SHA-256 | 已实际读取 |
|---|---|---|---|
| E-native-api | [FirstInstall.h](../../../native/RizToolbox.JudgeContext/FirstInstall.h) | 3a47c05940ab05b6e3bee5709f7343088a0be22efa6e9dd678568b2a61010ce4 | V1 144/V2 416兼容及V3 1600、18×64行、真实计数/masks |
| E-producer | [FirstInstall.cpp](../../../native/RizToolbox.JudgeContext/FirstInstall.cpp) | df30c90e2e2f4c2993a9599d1c9ba25a11c3a631ec4e2ad17356dd70aff62a50 | 18全预检、Prepare/Configure在首写前、27写/全body读回、三版atomicUnknown |
| E-profile | [FirstInstallProfile.h](../../../native/RizToolbox.JudgeContext/FirstInstallProfile.h) | a4b4fefaf3876eda385039ec6250fa21093d94f2d9e0d18831a9b0849a31f4df | 实际固定DLL完整18RF/原bytes/unwind/保留槽；本agent重新逐项比磁盘bytes |
| E-operation-api | [OperationContext.h](../../../native/RizToolbox.JudgeContext/OperationContext.h) | 3956b8979a859d99a0ee694ddc9ae50a1d0969f422eb94484a7aae7b4647549c | 旧Frame64/Config40/Stats48；新Config24/Observer32/Info96及3exports |
| E-operation | [OperationContext.cpp](../../../native/RizToolbox.JudgeContext/OperationContext.cpp) | 48fc3c0bd473941f6ac56a48e83c1e44037e2f1d600a0f2957bfb763fd65ae9f | 开始一次null/immutable observer快照、三kind原once/SEH finally、sticky faults |
| E-ready | [NativeFirstInstall.cs](../../../src/RizToolbox.Runtime/Hooks/NativeFirstInstall.cs) | b1d981e741d1428d0175cde465bdd9fc1082d3306065bb4b630e93c44a42dd76 | 私有V3 Ready owner、loaded helper+固定image、每Verify重核全receipt/lane |
| E-ready-operation | [NativeFirstInstall.Operation.cs](../../../src/RizToolbox.Runtime/Hooks/NativeFirstInstall.Operation.cs) | 2bcffc40c83549faf4e1ba15c018dde6f7780db0a3b64f1e57cce70defcc25c6 | exact target/kind row，actual Info96/protection/RF，Metadata值不是receipt来源 |
| E-shape | [NativeOperationPublication.cs](../../../src/RizToolbox.Runtime/Hooks/NativeOperationPublication.cs) | 0bea3c22e4705321802f26077f996f4a0dbf0a71511aaf0303952207697bda3f | blittable显式18行，V3 header27/完整masks/slot与RVA/kind关联 |
| E-bridge | [NativeBootstrappedOperationBridge.cs](../../../src/RizToolbox.Runtime/Hooks/NativeBootstrappedOperationBridge.cs) | 8865139f244f64862c2bc93c39f248c20465ce0b9d5cc19fdfc6d35a07ead536 | 永久roots、一次Attach、不改original、Info96+Attach发布窗口读取 |
| E-core | [ExactHookBroker.NativeOperation.Bootstrap.cs](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeOperation.Bootstrap.cs) | af106834e69bf8930d1f06c91b9586846b70ceedde9e8fb7d38976d357d9512b | Gate内前后Verify/完整tuple共享/factory一次/postVerify只撤本binding |
| E-native-preflight | [native-preflight.log](native-preflight.log) | 8aeaa42fb3ee89a81061e6c56ed6412c0ef0999454c2883eda074ac4a4f6cd1e | 实际helper编译+旧native31261/61358，root报告68117终态0 |
| E-mapped-preflight | [mapped-preflight.log](mapped-preflight.log) | fb0bfbd53bff1acd7eb24793121626371f905015bb090da56de938869449334b | 六独立模式normal839及五tamper各847，均passed |
| E-hooks-preflight | [hooks-preflight.log](hooks-preflight.log) | 5bbd5d956d2e069f1d27ea63acc23cc0c2d7637c563e8ea561de390c2a57337d | 189/189，含V3/actualclaims拒绝与生产无producer拒晚首发受控测试 |
| E-full-run | [build-run.json](build-run.json) | a156fd0915d53561440a87a5a3800338082087d3ec93015a40cfc94a630e9947 | 94416实际terminal0，Package后终态，与wrapper一致；未安装/游戏 |
| E-summary | [build-summary.json](build-summary.json) | 7bb023ad6b110365e67d91b61a61b70cbe287633109320300cb0e14ac64272c4 | 1579/25，Runtime160/Hooks189/Audio619；111原始不同diagnostic文本 |
| E-bootstrap-native | [operation-bootstrap-native-evidence.json](operation-bootstrap-native-evidence.json) | 906d92d3005809e5b58e318a00ffe4b3468ac07d82013b7616bb54ccec5e4d90 | 1477检查/84原调用/72observer pair；3旧call跨Attach、不借新observer |
| E-bootstrap-clr | [operation-bootstrap-managed-evidence.json](operation-bootstrap-managed-evidence.json) | 20c27544d077076c0d1265a4cbaa13a4cf62c7a5f9f08e79464cf32f0b08b0df | 自有真实Dobby→bootstrap→生产shared core回调，5进程837/75原调用 |
| E-mapped-final | [mapped-publication-evidence.json](mapped-publication-evidence.json) | baaf6b332fbafd2fd325255dc8f3296fa7d498fcfbe7ec639a76688aebe27f41 | 正式6进程5074检查，18row/27写，五种tamper及reentry永久Unknown |
| E-mapped-clr | [managed-adoption-evidence.json](managed-adoption-evidence.json) | b9e9104275f2895052d0b8a3070cd73bb0d5b243742a1585f2aa96f1255c24f1 | 2进程384，happy18Attach+peer/reentry拒绝；实际game回调0 |
| E-atomic | [atomic-unknown-evidence.json](atomic-unknown-evidence.json) | 63ad5e530efd86248c25fff096c7c58918f654d924eba54ed4ce6e2f4c0f9587 | 888检查，Unknown atomic已置/detail writer暂停时，V1/V2/V3各100读全Unknown |

映像原始JSON位于D:/RizToolbox-build-cache/operation-preinit-preflight-20261024/first-install-mapped/fixture-result.json，实际SHA-256 baaf6b332fbafd2fd325255dc8f3296fa7d498fcfbe7ec639a76688aebe27f41。本agent读了normal与tamper-operation原件：Ready27是重入前快照；随后实际currentPhase4/Unknown1/writes27，重复Before返回2且bytes未多写。operationRowsVerified18/invalidV3HeaderCases12，operationCallbacksExecuted0/transportInvocations0/gameInitExecuted=false。不能描述成重入后仍Ready。

## Findings

severity=n/a_re、category=design；以下source-reviewed不等于游戏运行证明。有限复核未发现新的确定阻塞缺陷。

| F-id / status | evidence_ids / location | 结论及边界 |
|---|---|---|
| F-latepatch，source-reviewed+受控preflight | E-ready/E-core/E-hooks-preflight；默认Bind | 两Adapter默认调用已转V3采用；没有producer/缺18完整receipt直接拒绝，不会普通Dobby重装。explicit createBridge overload是internal测试面，不作SDK绕行入口 |
| F-preflight，source-reviewed | E-producer/E-profile；PreflightOperations/PrepareOperations | 固定SHA、18完整source/unwind/pdata/OS lookup/页保护和empty slot在第一次DobbyPrepare前校验；18原trampoline和native immutable claim全部先于第1游戏写。真实生产时序仍依赖支持Doorstop首次init前合同 |
| F-partial，source-reviewed | E-producer；BeginGameWrite/CommitOperations | 旧9写后逐18Commit，尝试计数先于写；失败如实保writes/row original/epoch/verified masks，写后永久Unknown，无回滚卸载重试；零源写拒绝仍有原trampoline/slot/table准备责任。尚无任意第N写故障注入 |
| F-oldcall，validated自有native | E-operation/E-bootstrap-native；Entry→Dispatch finally | Entry开始只读一次observer，oldnull call后半段不借新CLR；三kind实际旧call阻在original跨Attach有实证。token0照Exit；nativecallback SEH/C++包含+Unknown，原函数SEH/C++继续，finally排空；Unknown不阻既有cleanup |
| F-identity，validated映像/受控采用 | E-producer/E-ready-operation/E-mapped-final/E-mapped-clr/E-atomic | 18tuple不可重配，V1/V2读真实27，V3完整masks/identity反复核；正式映像五tamper与reentry拒绝，CLR18Attach不改源；三版各100atomic-before-detail读均Unknown。没有执行game函数 |
| F-attach，source-reviewed+validated自有CLR | E-bridge/E-core/E-bootstrap-clr | delegate先freeze并永久root、一次Attach；双sink共享tuple/factory1/NeverInstallEngine0，detach/rebind不二Attach且实际context清理。postV3Verify失败撤本binding/标Unknown仍只有源码复核，没有该窗口故障注入 |
| F-slot，source-reviewed | E-operation/E-bridge | 3kind×32独立表，nativeGetInfo返回2仅真empty，legacy/partialclaim为3且partialUnknown1；旧allocator查actualclaims跳过，epoch跨本channel单调不复用。stats并非原子一致全局快照，不从Entered-Exited推取消完成 |

## Path 与失败边界

P-publish，path_type=callflow：fixed file/module与18RF/bytes→全部Prepare/Configure→listener动态表/边界预检→mid+六区+Unity/runtime原9写→18真实Commit→每项恢复protection/Flush/bounded branch/fullbody封存→27实际计数+18mask齐→唯一Ready。E-producer/E-profile，对应F-preflight/F-partial。任何意外Prepare改源、写后异常或最后校验不确定→atomic Unknown先置，再detail同步；资源全部留至进程退出，supported wrapper不得继续原init。

P-adopt，path_type=callflow：实际loaded V3 privateowner→真实metadata目标/kind匹配sealed row→Gate内VerifyReady→configured native channel一次Adopt/Attach→生产共用Operation dispatcher多sink→Gate内postVerify→logical binding。晚Attach只发布observer，不发布源指令。E-ready/E-bridge/E-core，对应F-latepatch/F-attach。原函数及既有scope完成不依赖未来binding.Active；logical detach不撤nativeentry、不封future生产者。

V3 native原callbacks抛出会使Info96 Unknown并最终使producer Unknown；CLR内部已包含的异常由bridge/broker自身Unknown承担，native不凭空知道CLR已catch的故障。两层Unknown不能混为相同计数。Frame sequence耗尽0仍originalonce，bootstrap epoch耗尽永久claimedUnknown；本轮未注入signed64耗尽。

另有限只读复核新[NativeOperationBootstrapManagedRegression.cs](../../../tests/RizToolbox.Runtime.Tests/NativeOperationBootstrapManagedRegression.cs)与[Test-OperationBootstrapManaged.ps1](../../../scripts/Test-OperationBootstrapManaged.ps1)：自有真实typed target由实际Dobby Prepare/Configure/Commit后调用，CLR采用共用configured core且NeverInstallEngine始终0；三kind、双sink同frame、GC、detach/rebind、原SEH/C++恢复与observer故障责任由真实回调断言。断言异常保存在callbackFailure并在P/Invoke后主动抛，故意observer fault单独位于断言wrapper外，避免测试断言被包含后伪绿。其先完成pre9调用再Attach，oldCallAcrossAttachVerified=false；跨Attach旧call由独立native fixture证明。最终重新hash该C#为38bce5053bac06d8daea3f8be2dbef4000523234675e6890e230b633e08d1db2、runner97db27b049a58bed9edca3e2bda86782c1e3fcbafc45164a6c91e475a5ced957。GC循环是在仍有测试channel引用的情况下进行，不据此宣称只靠native指针即可保活；永久managed roots另有源码依据。

## 正式动态结果与冻结

本agent实际读取[完整runner](build-run.json)与[wrapper](build.log.run.json)，两者exit0且logSHA一致；root已观察session94416的write_stdin实际terminal0。25套1579/1579包含Hooks189、Runtime160、Audio619、Visual71、UIInput79；独立fixture计数不加到托管suite数制造覆盖率。日志有111条不同原始diagnostic文本（上游、既有warning和ANSI变体等），不等于111新缺陷或零warning。早期CLR预编译曾有一个新CS8602 nullable warning，实际成功exit0；修后再次编译/完整冻结，原[预检记录](preflight-run.json)和[有warning日志](clr-build-preflight.log)均保留，不能描述成首次零warning。

| 验证 | 正式结果 | 证据边界 |
|---|---|---|
| Native bootstrap | 1477检查；84原调用、Entered/Exited84、observer72/72；12null snapshot、3lateAttach、18原异常、24observer异常、15worker、6nested、3zero/3detach | 真Dobby自有target；包含实际阻塞旧call跨Attach；不执行gamecode |
| Bootstrap CLR normal | 147检查/12原调用/6capture complete dispose | 每进程3自有target；pre9后Attach；共用production core |
| Bootstrap CLR original-SEH / C++ | 各183检查/15原调用/12capture complete dispose | 原native异常在nativecatch结束，再clean原函数恢复；不跨P/Invoke |
| Bootstrap CLR detach | 174检查/18原调用/9capture complete dispose | 已有context完成，后续无sink原继续、rebind不二Attach |
| Bootstrap CLR observer-fault | 150检查/15原调用/6capture尝试、3complete/Dispose | brokerUnknown保责任，nativeKnown仍原透传；不混为native fault计数 |
| 固定映像发布 | 6进程5074检查（839+5×847） | V3 18row/27实际写；source/post/relay/chain/operation五tamper、reentry无第28写；回调和gameinit均0 |
| 映像CLR采用 | happy294/reentry90，共384/2进程 | 实际RequireReady V3/18 readonly channels；happy18Attach/peer/错误identity拒绝/源不变/engine安装0；受控metadata，实际gamecallbacks0 |
| atomic Unknown | 888检查；三版各100read Unknown | writer在atomic置位后/detail锁前暂停；production FirstInstall源码在独立映像fixture编译seam执行，无生产testexport/mode |

五个bootstrap CLR进程合计837检查、75实际原函数，其中45为Attach前已完成原调用；不是45个跨Attach旧call。mapped fixture是DONT_RESOLVE映射，不执行GameAssembly任何函数；自有实际Dobby→native→CLR回调已在同一自有target链取得，仍与fixed-game映像采用、真实游戏冷启动分开。新联合没有实际IL2CPP metadata、生产object backend/ledger或引擎取消调用。

本agent重算所有546当前声明源码hash，与[构建前清单](build-inputs-before.json)/[归档清单](source-manifest.json)逐项相等；实际打开两个ZIP，对546源码条目和22包条目逐项重算hash，再核当前暂存22文件，全部匹配。[差分](changes-from-cancellation-request.json)为+12/~21/-0。[source proof](source-archive-check.json)与[package proof](package-archive-check.json)和实读一致。上表所有source hash均重新计算，未沿用消息中的旧值。

| artifact | SHA-256 |
|---|---|
| [完整日志](build.log) | ded7070722ae920484bb5f79cac4edde6f3b8dc4b28380535299a1519c9b59a0 |
| [source-manifest.json](source-manifest.json) | 4b9e8d3e2efd0062c746030dce391792d7e3344ae7ce3a822d0a4bdd8bc3cc95 |
| [source-inputs.zip](source-inputs.zip) | 6a3844055b5a2ce2d35f74cbc8491540480d946fdd9da38209c1ded1000cc9cb |
| [staged-package.zip](staged-package.zip) | 624cd1825b5a1bfb282d7887a1c261ff10a00476f53f9748c9fbd8d5d42dbddb |

另现场重算[旧安装18项](previous-installation-check.json)和[原游戏3项](original-game-check.json)均未变；实际打开旧1021批[source-inputs.zip](../foundation-local-protection-host-20261022/source-inputs.zip)与清单，326历史条目hash全部匹配（旧ZIP d38b6630002cc680924b4313c758ec05189e0a4edd685b3f6018183ade5f307f）。新22项包已冻结暂存，未安装；原游戏文件未改，无桌面/UI/新包游戏验收。

仍缺实际游戏cold-start/首次init合同验收、真实IL2CPP18方法metadata和对象根/GC整链、引擎取消scheduler确认/未来生产者封口、完整资源退休、任意并发首发或部分写失败与完整异常Record/Context透明性。listener invokable不等于全部delegate leaf；mutation所有inlined/direct/generic写者未关闭。新增27写凭据不授EngineCancelled，不清Pending，不开放RequestPlay，不扩大B03/B18a以外勾选；新包未安装，用户实测稍后。

Stop ABI静态直接证明self/ref寄存器，hidden MethodInfo位置符合已用IL2CPP声明约定，但成功body不读取它。生产broker对非零foreign MethodInfo按已有fault路径保责任；没有活体game回调，尚不能证明全部优化调用方传来的hidden寄存器值均匹配真实元数据。此为已有待验边界，不是本轮新增确定阻塞，不能据映像Ready或自有opaque methodcarrier省略实际冷启动/metadata/回调验收。
