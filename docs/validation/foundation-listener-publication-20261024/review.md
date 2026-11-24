# Listener 九写发布与 V2 凭据：有限复核

2026-10-24，最终证据复核。root 已确认定向 session23912 与完整 session53963 均为真实终态 exit0；本 agent 只读本地源码、日志、原始 JSON，并独立核对冻结文件及 ZIP entry hash，未自行构建、运行 fixture、安装或操作游戏。完整25套1515/1515通过；493项输入与22项暂存包归档一致。隔离映像发布和只读CLR采用已验证，实际游戏验收仍未完成。

范围按 [scope](scope.md)：本人实现 FirstInstall.cpp/.h/Profile.h 的版本化发布与凭据；随后只读有限复核生产 C# NativeFirstInstall/NativeListenerPublication 和 mapped regression。原生实现的作者自查不是独立第三方审计。root 唯一 builder，其他代理负责 fixture、managed 实现与有限交叉审查；493 个声明构建输入已冻结，本文件只写文档。完整 B01–B19 不因本批增加勾选。

## Evidence

source_type=file/log，定向证据初次读取于2026-10-24 19:11 +08:00，最终复核在完整runner于11:16:11 UTC结束且root发布归档后完成。源码 hash 是当前冻结文件本身；定向证据来自独立 targeted 缓存，不混用 full 输出。最终复核实际读取了文件SHA-256、manifest和ZIP内容，未重新执行构建或fixture。

| E-id | source_ref / artifact_path | SHA-256 | 观察 |
|---|---|---|---|
| E-native | [FirstInstall.cpp](../../../native/RizToolbox.JudgeContext/FirstInstall.cpp) | 1a5833a4c4ac697214d1277e8700cb01c32c103255d3a3067920227cae9e6219 | 首写前 stub/table 注册与验证、九次发布、全读回、V1/V2共用Ready核验及atomic Unknown |
| E-abi | [FirstInstall.h](../../../native/RizToolbox.JudgeContext/FirstInstall.h) | 2af877a6a81e4333a8ddcb7578a0dafb982e93d838277e0add4eb2836fbf2bac | V1 144保留，新增GetInfoV2与416字节布局 |
| E-profile | [FirstInstallProfile.h](../../../native/RizToolbox.JudgeContext/FirstInstallProfile.h) | d7c26dfdaffc90340880a5646a174f3d7da55ba52aa187f429453f3a2844a5ab | 固定SHA、三pre/三post原bytes、宽度与真实primary unwind |
| E-managed | [NativeFirstInstall.cs](../../../src/RizToolbox.Runtime/Hooks/NativeFirstInstall.cs) | 0b6e8802e2c7d37fcd042a3e17e6accb103372e18f02c9d6500ad6a6fc63828a | 生产仅GetInfoV2，不回退V1；全identity、实际bytes/protection/table再次核验 |
| E-shape | [NativeListenerPublication.cs](../../../src/RizToolbox.Runtime/Hooks/NativeListenerPublication.cs) | 54849ea95a2204ef7c758b0cd84ebe1f5925c0e4aae70c1f6738f062e48f2a45 | value-only416/176复制、固定shape与实际lane身份核对 |
| E-clr-test | [NativeFirstInstallMappedRegression.cs](../../../tests/RizToolbox.Runtime.Tests/NativeFirstInstallMappedRegression.cs) | e41d899f96afc221bc9382d60402489dfc6d37cffe2dfcd98f2f16a704ff6f7b | 两个CLR child实际采用V2，listener保持未Attach、零native调用和callbacks |
| E-targeted | [targeted.log](targeted.log) | 81d7bb459e805b068012debb6685cc3dc0c115824307857657b6eb94cc13983a | 与[targeted runner](targeted.log.run.json)的logSha256一致；runner exit0、未安装/未执行游戏 |

以下 source_ref 的共同根目录为 D:/RizToolbox-build-cache/listener-publication-targeted-20261024；artifact_path 是该目录下的实际文件。内容 hash 记录原 JSON 文件，不是测试 DLL 或源码 ZIP 的 hash。

| E-id | source_ref / artifact_path | SHA-256 | 观察 |
|---|---|---|---|
| E-mapped-run | first-install-mapped/fixture-result.json | 529c9cefce79023727541c7879d777533d9a9cee2c3356bd770419b8b4aa7d0d | normal383、四种tamper各390，共五进程1943；真实九写与V2 |
| E-unknown-run | first-install-unknown/fixture-result.json | 1b88d477a8552ea1f13f72fb3f145aefbfc8ecef2ea849809df638c42da58591 | 713项，atomic已置1且详情writer暂停期间，V1和V2各100读均Unknown |
| E-clr-run | first-install-managed/fixture-result.json | 348f8a58d3051f4d7777cd3fbc01a570222db50a0904b9b6d26d593a37a861de | happy39/reentry33，两个CLR进程72项；实际V2只读采用 |
| E-reject-run | first-install-rejected/fixture-result.json | 5a41f64da619676eee4608bb1a067a3219a22f7a5754dadca0d7cc87bb869f27 | 五独立进程205项；writesStarted0、gameLoaded/gameCodeCalled=false |

[固定文件profile证据](fixed-profile-evidence.json)另核固定GameAssembly SHA、primary RF/unwind、三个源CALL与完整6/9/9 continuation。它只读磁盘文件，明确 imageLoaded=false、dynamicUnwindVerified=false；不能拿静态文件解码代替本机动态注册/展开。定向动态证据是上表映像/Unknown fixture，完整复跑及冻结证据另见末节。

## V2 ABI 与实际语义

旧导出 GetInfo 仍要求 Version1/Size144，旧字段偏移不变，writesStarted 如实报告9。新增 RizFirstInstall_GetInfoV2 要求 common.Version2/Size416、三个reserved为0，返回0表示有效输出、1表示header无效。输出是否Ready必须看真实Phase/Unknown/全shape；调用方不能输入任意字段构造Ready，非header身份字段会被真实snapshot覆盖。

| 字段 | 字节偏移 | 固定语义 |
|---|---:|---|
| common | 0..143 | 完整V1前缀；V2输出Version2/Size416，V1输出仍1/144 |
| capabilityVersion / listenerSlot / listenerEpoch | 144 / 148 / 152 | capability1、固定slot31、实际独立listener epoch |
| preEntry / postEntry / preRelay / postRelay | 160 / 168 / 176 / 184 | 真实helper entry与近端relay |
| sourcePc[3] / sourceReturnPc[3] | 192 / 216 | 固定pre source、真实source+5/+5/+6 |
| postStub[3] / postTransportReturnPc[3] | 240 / 264 | 三stub与各真实observer CALL返回stub+5 |
| directTarget[2] | 288 | 固定两direct目标154E5B0；virtual target由lane执行时读 |
| functionTable / functionTableBase / unwindInfo[3] | 304 / 312 / 320 | 三条动态RF，基址GameBase、各secondary chain xdata |
| sourceWidth / postWidth | 344 / 356 | 5/5/6与6/9/9 |
| sourceProtection / postProtection | 368 / 380 | 六处原保护属性，发布后恢复并读回 |
| registeredFunctionCount / prewriteLookupChecks / prewriteBoundaryChecks / reserved2 | 392 / 396 / 400 / 404 | 3 / 12 / 12 / 0 |
| allocationSize | 408 | 4096 |

近端块位于GameBase上方、rel32可达：stub在+0/+100h/+200h，代码为E8 observer relay、inc ebx、mov eax,ebx、E9回1564420，共14字节；各xdata为stub+40h，零新增prolog/codes、CHAININFO链接实际Unity primary。table在+380h，pre/mid/post relay在+500h/+520h/+540h，均14字节FF25 absolute tail。代码、xdata、table与claim都永久保留，准备失败也不释放/复用。

顺序是 mid第1写→三组pre/post第2至7写→parent第8写→runtime第9写。写尝试计数在可能改页/写机器码前开始；每次写核原bytes、页保护、恢复、flush和读回。全部配置、relay、CHAININFO注册、12次真实lookup和12次OS展开在首写前完成。准备阶段无游戏写时允许DisabledNoWrite；一旦写后不确定即永久Unknown，无回滚/释放/重试。

prewrite的12次边界展开使用自有受控栈，驱动真实生产注册表与真实Unity unwind元数据，**不执行任何stub或游戏函数，不是现场采样原游戏栈**。三stub各0/5/7/9四个指令边界，不能把指令内部字节算边界。sourceReturnPcObservedByExecution=false；真实callee看到source返回地址的执行证据仍属于上一批自有native fixture，不能迁称此映像执行过game callee。

## Findings

各项 category=design/other、severity=n/a_re、confidence=high。validated只指下面明确的隔离fixture范围，源码属性与待验分支另列，不等于生产冷启动/game acceptance。

| F-id | evidence_ids | status | 结论 |
|---|---|---|---|
| F-publication | E-native,E-profile,E-mapped-run | validated，隔离映像 | 真实固定映像九写、完整六区字节/原页属性/relayRX、3RF与12lookup/12受控栈展开通过；没有执行游戏或加载器初始化 |
| F-readback | E-native,E-mapped-run | validated，四种顺序tamper | source/post/relay/chain各改一字节后，V1/V2转stickyUnknown；保真实9写，重入2且无第10写、不变更已有bytes |
| F-bad-chain | E-native,E-mapped-run | validated，所测单字节破坏 | Ready核验先安全比较封存整4KB（含table/chain），再OS lookup；Ready读不调用RtlVirtualUnwind损坏chain。该tamper仅证明所测顺序，不是任意并发写内存的安全边界 |
| F-atomic | E-native,E-unknown-run | validated，确定交错 | MarkUnknown先atomic再详情lock；V1/V2最终同snapshot锁再读atomic。writer暂停期间两版各100次返回4/1，identity/9写保留 |
| F-managed | E-managed,E-shape,E-clr-test,E-clr-run | validated，实际CLR只读采用 | 生产RequireReady强制V2+actual lane176，9写/全tuple/bytes/protection/注册核对；重复VerifyReady不变更发布。listener未Attach、Entered0/callbacks0，受控metadata无解引用 |
| F-source-guard | E-native,E-profile | source-reviewed | 每次DobbyPrepare后及首写前，FixedGame重核固定prefix、六区原bytes、完整已捕获Unity body、pdata/unwind。runtime前缀完整核；DobbyPrepare若意外改源则Unknown，而非静默采用 |
| F-scope | E-clr-run,E-mapped-run,E-unknown-run | accepted boundary | 旧V1三写不授权六区，当前V1虽报告9也不替代V2完整身份。没有listener CLR callback/ledger、真实IL2CPP/GC/全部delegate leaf或完整run退休证据 |

生产C#有限只读复核未发现新的确定阻塞bug：416/176为value-only复制，没有可变数组身份；RequireReady没有V1 fallback，VerifyReady重新比全receipt与actual lane，已有parent/runtime合法Attach不会替换listener identity。所有native源字节/链先验证后查询OS表，不由调用方提供table去执行。该结论只覆盖实际读取的三份managed文件和对应native ABI，不是所有Runtime或SDK安全审计。

## 定向实际结果

| 运行 | 实际checks/结果 | 边界 |
|---|---|---|
| 固定映像normal | 383；初始snapshot Ready3/0、writes9，稳定10读；最后重入2、current4/1/9 | phase/unknown前值是before-ready-reentry观察，不能把末状态仍称Ready |
| tamper-source/post/relay/chain | 各390，五进程合计1943；每种tamperDetected=true、fixtureTamperWrites1、noTenthProducerWrite=true | fixture故障写与producer9次计数分开，均只改自有隔离进程内存 |
| atomic Unknown | 713，V1 100/100、V2 100/100 Unknown；invalidV2HeaderCases9、callerIdentityCanonicalized=true | compile-time seam仅在fixture把真实FirstInstall.cpp编入EXE；不向生产helper添加运行时test mode |
| CLR happy/reentry | 39+33=72；V2/cap1、slot31、六区、3RF、12/12身份只读采用；happy保双idle parent sinks与runtime采用，reentry拒绝 | listenerObserverAttached=false、listenerNativeInvocations0、listenerCallbacksVerified=false；不是新lane native→CLR执行 |
| 写前拒绝 | 205/5进程、writes0、不加载/调用game | 固定输入拒绝范围，不推导任意非法指针/恶意并发模型 |
| Hooks | 171/171 | 布局、V1三写/不完整V2/冲突lane拒绝等受控检查，不代替实际game运行 |

三个映像类JSON均明确mappedImageOnly=true，gameCodeExecuted/gameInitExecuted=false、transportInvocations0、firstInstallSafetyVerified/gameRuntimeVerified=false。CLR两child additionally actualIl2CppMetadataResolved=false、managedObserverCallbacks0、actualLedgerCaptureVerified=false。输入文件hash保持，磁盘GameAssembly未修改；DONT_RESOLVE只用于隔离验证，不是生产启动模式。

## Path 与未验收

P-publish，path_type=callflow：固定文件pin/SHA与PE/pdata/source核验→DobbyPrepare两原trampoline并核未改源→runtime/Bootstrap配置→近端stub/relay/chain注册→独立listener配置/实际epoch→12lookup/12受控栈展开→mid+六区+parent/runtime九次发布→原bytes/页恢复/flush/全封存读回→V2Ready。E-native/E-profile/E-mapped-run，对应F-publication/F-source-guard。没有执行映像地址。

P-invalidate，path_type=callflow：隔离映像已Ready→四种单字节tamper或真实producer重入→V1/V2检查封存字节/identity或atomic Unknown→Unknown4/1并保9写→不回滚/不释放/不第10写。E-native/E-mapped-run/E-unknown-run，对应F-readback/F-bad-chain/F-atomic。不能把后来的新状态推成先前snapshot的未来保证。

P-adopt，path_type=callflow：CLR child只映射固定GameAssembly→实际producer九写Ready→生产RequireReady强制GetInfoV2+lane完整身份→只读保存publication→旧runtime/parent通道idle采用/双sink共享→重核全V2与零listener调用。E-managed/E-shape/E-clr-run，对应F-managed/F-scope。listener lane没有Attach managed observer，更没有捕获ledger。

仍缺真实Doorstop首次冷启动安全时序、实际游戏九区执行/新lane CLR回调与ledger、真实metadata/对象根/GC、invokable内部所有delegate leaf、连续mutation/provider转交/取消与future封口、所有消费者和资源退休及完整Retry/Return/Loader。上一批原生lane的八项未验分支仍保留：动态vtable变化、target-read AV、foreign/overflow mask、missing-post损坏、完整异常记录、pre helper全指令展开、全部delegate leaves、mutation/StopCoroutine退休。

TargetReadFaulted可在Before之前产生Abnormal After(token0)，当前V2 receipt不改变独立lane合同；未来managed只能按flags做native清理，不能由token0推已capture。原异常RIP可能是helper真实virtual read，不能声称伪装成旧CALL地址。旧V1与独立listener descriptor不是聚合V2 observer；本次V2是发布凭据版本。

## 完整构建终态与冻结复核

[完整runner](build-run.json)记录session53963、terminalObservedByRoot=true、exitCode0；[底层runner记录](build.log.run.json)同样exitCode0，结束时间2026-10-24 11:16:11.9728844 UTC。两者logSha256与本次实际读取的[完整日志](build.log)一致。[完整汇总](build-summary.json)为25套1515/1515，Runtime160、Hooks171、Visual71、UI/input79、Audio/Content573。上述定向检查在完整构建中再次执行，不能把两个runner的checks相加计成额外覆盖。

| 最终证据 | 实际完整结果 | SHA-256 |
|---|---|---|
| [build-run](build-run.json) | 53963真实终态exit0；installed/gameExecuted=false | 1716fbcdf8e07456af65c09767acff6876d50bcd5d5f64f8672a96fc3ac6f272 |
| [build-summary](build-summary.json) | 25套1515/1515；111条去重diagnostic raw lines | 8d0f581129ad2f8655d29d71d84fa54836808027853ecb5495be13f8b4345099 |
| [映像发布](mapped-publication-evidence.json) | normal383与四tamper各390，共1943/5进程，均通过 | 529c9cefce79023727541c7879d777533d9a9cee2c3356bd770419b8b4aa7d0d |
| [原子Unknown](atomic-unknown-evidence.json) | 713检查，writer暂停期间V1/V2各100/100 Unknown | 009ef894233c6fe7a10ce1349c5fe8c01a1ae3799fd147aaf8b48639e7227431 |
| [CLR只读采用](managed-adoption-evidence.json) | 72/2进程；listener仍未Attach、调用与callbacks为0 | 87236ead9a5016fe21d2c1c456cf796b72b0698ef3b3fb5f2433b0abd33c1fdc |
| [写前拒绝](native-rejection-evidence.json) | 205/5独立进程，全部通过 | 3e811849f5d27a8ad610fd2b0f0cc3c1580ce8d6a52687e1330fd1311781d660 |

本次完整运行另重跑[自有原生listener](listener-native-evidence.json)8349项、[自有broker真实CLR回调](adopted-broker-evidence.json)379/3进程及[受控生产init调用](init-call-evidence.json)259/16进程。它们各自的自有fixture/受控调用范围不变，不能替代固定游戏listener回调、冷启动或真实GC验收。111为汇总中的去重原始诊断行数，包含ANSI显示变体与既有Audio CS0649/CS8602；不代表111个新增问题，也不宣称零warning。

冻结核对同时读取[构建前manifest](build-inputs-before.json)、[归档manifest](source-manifest.json)、[输入一致性proof](build-input-integrity-check.json)和当前493个输入字节：before/post逐项一致、当前hash零变化。实际打开源码ZIP检查493个entry及包ZIP检查22个entry，均与各自manifest逐项匹配；当前暂存目录22文件也零变化。[与上一批比较](changes-from-listener-native.json)为新增2、修改14、删除0。

| 冻结产物 | SHA-256 |
|---|---|
| [源码ZIP](source-inputs.zip)，[493项proof](source-archive-check.json) | 3a4f77966ce04fa76b8bb84b7404fe250d6db58eb21f3ac78b2c753896ffc3de |
| [源码manifest](source-manifest.json) | 7693e1bcc7d671f0a97180985ce18e8a03b9939a1fa364cd0d13d83bbf1bc6f1 |
| [22项包ZIP](staged-package.zip)，[包proof](package-archive-check.json) | e8223edb3d7482e54d2b2e662acbe280661984b23c828dd50aadd53d2df47730 |
| [完整日志](build.log) | acbdddd843fcf19c113206b0d97ed110e44aadfa3cd69c447cd3f7d295808e70 |

独立复核[旧安装proof](previous-installation-check.json)：旧1021批18个安装文件当前hash全部匹配，实际打开其[历史源码ZIP](../foundation-local-protection-host-20261022/source-inputs.zip)核对326项与历史manifest全部匹配。[原游戏proof](original-game-check.json)所列Rizline.exe、GameAssembly.dll、global-metadata.dat三文件的当前hash仍各自匹配；[当前暂存proof](staging-check.json)明确installed=false。493项声明源码与22项包归档的一致性不覆盖外部工具链、缓存依赖和B19完整干净环境复现矩阵。

最终已验证范围限于本节记录的离线全量、隔离映像九写/持续读回/所测篡改拒绝、确定atomic交错与实际CLR只读V2采用；上述未验收项目保留。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false；仅B03/B18a已勾，用户“先代码、实测稍后”保持，新包未安装。本 agent 本轮只更新review.md，没有修改任何源码或构建输入。
