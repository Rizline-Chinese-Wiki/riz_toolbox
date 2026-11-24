# 引擎节点传输与实际账本联合：有限复核

2026-10-25。本 agent 为本批 native transport 和 CLR 联合 runner 作者，对两者是作者自查；对其他代理的 bridge/dispatcher 和 root 的节点账本进行了有限只读交叉复核，不能称第三方独立全审。本稿仅适用[本批范围](scope.md)，采用 docs-generator，flavor=null。root 是唯一 builder；本 agent 未构建、执行 fixture、安装、修改冻结源码、启动游戏或操作 UI。root 已实际观察完整 session11589 terminal exit0；本 agent 实读 runner/wrapper/log 和正式原件，独立重算25套1597/1597、561源码和22包逐项ZIP/current hash，旧326源码ZIP、旧安装18项和原游戏3项也已实核。本批离线构建与自有联合验证通过，完整 B01–B19 仍未完成，新包未安装。

已确认本批把四个实际 native ABI 送到真实 CLR bridge/dispatcher 与同一个实际节点账本；它还没有 UnityPlayer 的生产 V4 发布凭据，也没有真实游戏、tracker/GC、scheduler/future Start 封口或引擎取消完成证明。所有 After 都只用已复制标量，Start 正常 After 只读取实际 R9 输出槽；不会从 CLR 解引用可能已释放的 node。`FinalCleanupObserved` 和 `EngineCancelled` 的含义保持分开，后者始终 false。

## Evidence

source_type=file/log，observed_at=2026-10-25；以下 source_ref 同 artifact_path。source hash 均现场计算，561 项当前声明源码已逐项和 [构建前清单](build-inputs-before.json)相等。Evidence 只证明所列有限范围，不按检查数推算覆盖率。

| E-id | source_ref / artifact_path | SHA-256 | 已实际读取 |
| --- | --- | --- | --- |
| E-native-api | [EngineNodeContext.h](../../../native/RizToolbox.JudgeContext/EngineNodeContext.h) | `da4ec5037a62eca3c0dce6a6f50bcb0935a981e71f2fb51cd52377ef00f5237a` | 4 kinds、Frame112/Completion32/Info112、七个exports |
| E-native | [EngineNodeContext.cpp](../../../native/RizToolbox.JudgeContext/EngineNodeContext.cpp) | `27dea09bb77e837ed7cdc7c3b15162b617bc4be2a8ee7c23df158fb66040b028` | 32永久槽/immutable original、一次observer快照、只读Before/Start输出槽、native异常 |
| E-types | [NativeEngineNodeContextBridge.Types.cs](../../../src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.Types.cs) | `a722e5a46c9c9dca5b3564558c2acdc2bb2fd8f90da13c0c8060cdbb559911dd` | 实际4/2/1/1参数和byte/void返回；故障frame不被形状检查吞掉 |
| E-bridge | [NativeEngineNodeContextBridge.cs](../../../src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.cs) | `4e9b00e184316fdd291874350fefbe8f435ebe17fcbde11799d08a00550b5d46` | 完整actual identity核验、adopt/Attach、永久roots；不发布source |
| E-dispatch | [NativeEngineNodeObserverDispatcher.cs](../../../src/RizToolbox.Runtime/Hooks/NativeEngineNodeObserverDispatcher.cs) | `2c1af299f9d89c5184370f1476014ccdeb734689fb6d9a388cc9ac8ec11a1924` | 四类共享物理父子栈、固定sink快照、异常后Complete/Dispose |
| E-ledger | [NativeEngineCoroutineNodeLedger.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeEngineCoroutineNodeLedger.cs) | `b7e03eec2b77a3b972795aa58d3a2b0a5a75f21ce86e3fda0c671cbbeb620d1b` | 先登记责任、Start内同步Execute匹配、地址代次、分开的清理事实 |
| E-exe | [EngineNodeFixture.cpp](../../../native/RizToolbox.JudgeContext/fixture/EngineNodeFixture.cpp) | `b3e4b25b6be9a771907630c96f252597b37220fcc8bb41f4197d368c2cedd9d6` | actual Dobby targets、exact外层CALL PC、worker、旧call跨Attach和native故障 |
| E-dll | [EngineNodeManagedFixture.cpp](../../../native/RizToolbox.JudgeContext/fixture/EngineNodeManagedFixture.cpp) | `961948960452138364c67c4489c2d82d034f5f569c1e077e81b18e3c16e83f20` | actual四类target/Result208、Start同步嵌套、node free/output protect、native catcher |
| E-clr | [NativeEngineNodeManagedRegression.cs](../../../tests/RizToolbox.Runtime.Tests/NativeEngineNodeManagedRegression.cs) | `2657d1210a82805543350057f812dd384e489e05cbf06dcd80b5d0f81dd07e56` | 实际Dobby→helper→bridge→4dispatcher→actual同一ledger、两sink同frame |
| E-runner | [Test-EngineNodeManaged.ps1](../../../scripts/Test-EngineNodeManaged.ps1) | `5d1492faeb7e597cbccddeeef681bf2a09c979341d9430cf8660801d2a7c0c3a` | 5个独立子进程、实际原件/不夸大结果核验、输入hash |
| E-dispatch-tests | [NativeEngineNodeDispatcherTests.cs](../../../tests/RizToolbox.Runtime.Tests/NativeEngineNodeDispatcherTests.cs) | `7fa6b8f01a66715ec5e905d2bd265008c88e3388684ac222da102163ca18baab` | 10组受控形状/identity/多sink/父子/错误线程/故障测试 |
| E-ledger-tests | [NativeEngineCoroutineNodeLedgerTests.cs](../../../tests/RizToolbox.AudioContent.Tests/NativeEngineCoroutineNodeLedgerTests.cs) | `7531b701c9fd1b331483118b6a9558b7f71cb1dcbe8eff17b64f9d55454b04d4` | 8组受控同步匹配/执行责任/地址代次/故障/边界测试 |

ABI 来源为上一批[固定 UnityPlayer 研究](../foundation-operation-preinit-20261024/cancellation-research.md)及 [research JSON](../foundation-operation-preinit-20261024/research/cancellation-engine-evidence.json)。本 agent 重新核对磁盘 UnityPlayer SHA-256 `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1` 和 GameAssembly `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` 与研究输入一致。Start `[792240,7929BF)` 原 body hash `6a24dc40b10797a33529428b7d82821f1cb646a72d9108cccb884bfe2f8d7721`，Execute `[78F840,78FA2E)` `1ffd5505fa6c15a9315524ad28f9846b8ba4ef04cbf9cf5c0a807fbd21a997f6`，Release `[78F680,78F768)` `771128974dd1a81b49b7f6a9743a492a752cf30aa42bd95073df111cf159d1a3`，Detach `[793840,793A5B)` `942344fdd9c188c5e9b8371d6e03ce8fb8f944bbfcd520cadd3ef5715dc5edf7`。这仍是固定磁盘机器码静态证据，没有执行这些引擎函数。

## Findings

severity=n/a_re，category=design。source-reviewed 不等于游戏验收；本有限复核没有发现 transport/当前标量账本范围内的新确定阻塞缺陷。

| F-id / status | evidence_ids / location | 结论及边界 |
| --- | --- | --- |
| F-abi，source-reviewed+validated自有native | E-native-api/E-native/E-exe/E-dll；四typed entry | Start=`u8(owner,enum,nameArg,node** output)`、Execute=`u8(node,byte* output)`、Release=`u8(node)`、Detach=`void(node)`；不带 IL2CPP MethodInfo。Start 返回的是 AL，node 经实际 R9 槽发布/清零，不能把 AL 当 node。原始 byte 0x80/0xff 未经 bool 归一化；完整 RAX/异常 flags/context 透明性没有证明 |
| F-copy，source-reviewed+validated自有native/CLR | E-native/E-dll/E-clr；CopyBefore/CopyStartOutput | 非 Start Before 仅复制 node +58 owner、+10 script handle、+18 type、+20 cache、+60 refcount；任一读故障保实际 node，其余标量清零并保 Unknown，原函数仍一次。正常 Start After 仅复制 R9 槽值，不读其 node；其余 After 均不读 node。五次 bounded read 不是一致性原子快照或 GC root |
| F-original，source-reviewed+validated自有native | E-native/E-exe/E-dll；Dispatch finally | observer 开始一次快照；original 恰好一次。原函数 SEH/C++ 沿 native 展开到 owned native catcher，不让原异常越过 P/Invoke。异常原调用会使 native Info.Unknown 永久置位，包括未 Attach 的历史，之后不得晚 Attach 假装干净。native callback 故障被包含，仍配对 Exit；不以 Unknown 丢弃既有清理责任 |
| F-adopt，source-reviewed+validated自有CLR | E-bridge/E-types/E-clr；AdoptConfigured/Attach | 核真实 kind/slot/target/original/entry/epoch，拒重复 CLR owner；只读 adopted source，仅发布一次 observer。bridge/dispatcher/delegates 永久 roots；逻辑解绑/重绑不重新 Configure/Attach/Commit。GetInfo/counters不是全局原子快照，不能据 Entered-Exited 授引擎完成 |
| F-pair，source-reviewed+validated受控/自有CLR | E-dispatch/E-dispatch-tests/E-clr；Enter/Exit | 每物理调用固定两sink及父子栈；同一 source frame、completion，被逻辑解绑或观察者故障后仍完成并逆序 Dispose。跨 kind 共用实际物理 parent，错误 frame/thread/token 保 Unknown并不消费原scope。故障 observer 的故意异常在断言 wrapper 外；非预期断言经 callbackFailure 在 native 返回后主动抛出，避免包含断言而伪绿 |
| F-generation，source-reviewed+validated受控/自有CLR | E-ledger/E-ledger-tests/E-clr；Capture/MatchingStart/Complete | 登记责任先于匹配；Start 尚未返回时按实际 native owner 和 type2/actual enum cache 关联同步 Execute。地址旧代需 FinalCleanup、执行/物理scope drain、Start completed/disposed 后才能由另一真实 Start 建新代。外来/缺失 Start不造关联，最近 foreign Start不借外层 Start；未知/溢出/错误完成保责任 |
| F-facts，source-reviewed+validated受控/自有CLR | E-ledger/E-ledger-tests；NodeRecord/Read | refCount 仅诊断，不当执行深度；Execute 物理窗口另计数。DetachReturned、Release原byte0/FinalCleanupObserved、ActiveExecutions及InlineCleanupUnproven分别保存。账本无裸指针读取、根释放或引擎调用接口；Pending保留、EngineCancelled恒 false |
| F-production，incomplete | E-bridge/E-dispatch/E-ledger及[下一步](next-integration.md) | 新 EngineNode 只支持 internal 已配置通道采用。当前 FirstInstall V3 的固定 GameAssembly18row不包含 UnityPlayer四entry；缺少 V4/equivalent producer、首个引擎调用前发布时序、真实 tracker/metadata/GC关联及完整 scheduler责任，不能当成游戏已接通 |

## Path 与有限动态证据

P-native，path_type=callflow，E-native/E-exe/E-dll 对应 F-abi/F-copy/F-original：真实 self-owned DLL target → pinned Dobby Prepare → immutable helper Configure → actual Commit → caller 调到 typed entry → 开始快照 nullable observer → copied Before → native original → copied Start 输出槽或无 node After → Exit。原生 EXE 提供准确外层 CALL return PC；nested PC仅核 fixture module，不扩大到完整引擎调用方证明。旧 call 阻在 original 跨 Attach 仍使用开始的 null observer，没有借用后发布 callback。

P-clr，path_type=callflow，E-bridge/E-dispatch/E-ledger/E-clr 对应 F-adopt/F-pair/F-generation：同一自有 Dobby 四 target → 实际 typed helper → actual bridge AdoptConfigured/Attach → 四 dispatcher → 第一 sink 的同一个 actual node ledger Capture/Complete/Dispose + 第二 sink 的 exact same frame/result。Start original 内实际调用已 detour 的 Execute，再调用 Release(raw0/free)，Start返回前已关联 native node；Start After 复制输出的已 free 指针值而不解引用。native fixture 字段和返回 byte 是受控自有函数值，不能把该 Release raw0 的协议验证描述成 UnityPlayer 真正引用归零/调度清除已经执行。

本 agent 实际读取预检 cache `D:/RizToolbox-build-cache/engine-node-preflight-20261025/engine-node-native/fixture-result.json` 和 `engine-node-managed/fixture-result.json`，并现场重算总计。原生预检 SHA-256 `0cd8c9a362adb92fef603e3c744a1b7929534f89b73ff5e22b0aeb4cf4425f79`，CLR预检 SHA-256 `df20301d6ae0eaa84195476e6d15dc81b2ff4fe4a9fa7047fa69ed771590a62c`；[native预检日志](native-fixture-preflight.log)和[联合预检日志](clr-joint-preflight.log)保留当时原件位置。

| 自有 CLR mode | 检查 | 实际原函数 / actual ledger physical scopes | 核实的边界 |
| --- | --- | --- | --- |
| normal | 216 | 9 / 9 | 两次实际 Start→同步Execute→最后Release(raw0/free)，另三种 standalone target先以foreign身份观察；两代 owner/enum/handle不同，物理配对各9，peer各9 |
| original-seh | 155 | 8 / 4 | 四kind先Attach后各原生异常；随后正常original仍各一次，但 sticky Unknown 拒绝新用户capture；四nativeUnknown均1 |
| original-cpp | 151 | 8 / 4 | 原typed C++ payload由native catcher核实；其它同SEH，不让原异常跨P/Invoke |
| detach | 192 | 11 / 7 | 首4scope内逻辑detach双sink仍complete/dispose；空4调用无用户交付，重绑后真实Start的3scope继续；不重装native |
| observer-fault | 141 | 8 / 4 | peer Complete故障，首sink ledger及双Dispose仍配对；实际Detach Before不可读且原函数free；四dispatcherUnknown，native只有Detach因snapshotfault为Unknown，其它3native仍known |

五进程855检查，44次实际原函数，28个 actual ledger Capture/Complete/Dispose，以及28个 peer Complete/Dispose。正常和detach预检各记录2个代次；normal 的两代实际 node 指针相同，detach 的两代指针不同，各自 owner、enum、handle、generation均不同。因此 normal 提供实际地址复用样本，不能只按裸地址借前代身份。所有mode均 ledgerOutstanding0、EngineCancelledfalse、Pendingtrue。原生 EXE 另有3191检查/128实际original/116 Enter/Exit/12无observer调用，含4旧call跨Attach、16原异常、32observer异常、16worker、8显式nested、4合法zero token、3Before故障、2输出槽故障和5个原函数free情形；这些是独立样本，不能加到托管suite数量制造覆盖率。

早期 [Audio预检](audio-preflight.log)为621/627，新增8组账本通过，旧6组因缺本次预检需要的native环境失败；该次不称全通过。早期 [Runtime编译](runtime-preflight.log)真实CS0266失败（新dispatcher测试的int/uint常量形状），早期 [CLR编译](clr-build-preflight.log)真实CS0170失败（短路VirtualQuery后的memory.Protect可能未赋值）；两处均在正式源码冻结前修复，之后[最终编译](clr-build-preflight-final.log)、[Runtime170预检](runtime-preflight-final.log)与上述5进程联合预检成功。原红日志保留，不写成首次零错误。

## 剩余验收边界

自有 ABI/异常/多sink联合、固定磁盘 ABI 研究、既有 V3 的 DONT_RESOLVE GameAssembly映像发布、真实游戏冷启动是不同证据层。本批 actual Dobby→helper→CLR→actual node ledger 已经在同一自有链取得；没有把旧mapped Ready/V3或受控metadata拼成UnityPlayer生产挂接/首发安全/游戏运行链。

还需实现并证明 UnityPlayer四入口的生产发布凭据与实际首个相关调用前时序、true tracker receiver/enum/Coroutine/run/slot/generation关联、Start内联清理、全部scheduler登记/删除/cleanup和continuation/nested责任、future Start封口、真实IL2CPP根/GC及全部消费者退休。完整冷启动、Retry、Return、Loader、第二局、暂停/结果页/窗口重建实测继续按用户“实测稍后”保留。Pending不解除、RequestPlay不开放、B01–B19不新增验收勾选。


## 正式终态、归档与现场一致性

E-full-run 的 root session11589 实际 terminal0发生在 SDK distribution 和 Package 后；[独立wrapper](build.log.run.json)也记录exit0、相同log SHA。本 agent 独立从原始日志提取25个suite summary，合计1597/1597；其中Runtime170、Audio627、Hooks189、Visual71、UIInput79、Judgements35、SDK distribution10。相较前批1579新增18组受控托管case（dispatcher10、节点账本8），独立native/CLR检查数不混入suite总数。日志独立重算111条不同原始compiler diagnostic文本，与summary逐行相同；包含上游/既有warning及文本/ANSI变体，不是111个新问题，也不是零warning。

| E-id | source_ref / artifact_path | SHA-256 | 正式结论 |
| --- | --- | --- | --- |
| E-full-run | [build-run.json](build-run.json) | `e66deb63a9010cb30a4c0a04abcae0114baeb5679da7ced54aead9194bb09d86` | 11589实际terminal0，root观察；wrapper和loghash一致 |
| E-full-log | [build.log](build.log) | `d19c6e5d96053b8ff2201eb502638c04e733f76ead752152051b73e5efd631bd` | 独立解析25suite总1597，含SDK distribution和Package |
| E-full-summary | [build-summary.json](build-summary.json) | `a161cd4c7fb31dba03b6399d656ddbc3763269e2af1eb58f661f00d842070b34` | 1597/25，Runtime170/Audio627/Hooks189；111原始不同diagnostic文本 |
| E-formal-native | [engine-node-native-evidence.json](engine-node-native-evidence.json) | `54b48240dcaf5b5c2f0382ba82079a82ab5a24c50ed4ddc40cd0a7c995cd9cdc` | 正式EXE3191/128original/116pair/12unobserved，与final cache JSON对象一致 |
| E-formal-clr | [engine-node-managed-evidence.json](engine-node-managed-evidence.json) | `95cec833e55fddd6d7911ce2e1bdc4ae72f2472bc6f963dee46f5514a6bd148d` | 正式五进程855/44original/28actualledger及28peerpair，与final cache JSON对象一致 |
| E-inputs | [source-manifest.json](source-manifest.json) | `ec7223f887ae9ab8b51fdc44f509f35a82a4adb801f603e7fe2390f9d807b5db` | 561项与before/current/ZIP逐项相同 |
| E-source | [source-inputs.zip](source-inputs.zip) | `28180b96ced1cc7e22eb37317106f4d76655c31da6e1db5cf7accfc465fa9193` | 实际打开并核561条目、全CRC及各条目SHA |
| E-package | [staged-package.zip](staged-package.zip) | `f559489c9f5747f9f64ad6b973618e96a1822496f93c033020df1044a8973451` | 实际打开并核22条目、全CRC及current暂存SHA |
| E-preflight | [preflight-run.json](preflight-run.json) | `e25f58ae8f7e0dbb60651352311d96e95809ec067db0c73d73575e724fdbab13` | 各真实root终态，保CS0266/CS0170和缺环境失败历史 |

Formal native 3191 与 formal CLR 855已重跑新冻结helper，统计与预检相同；真实指针值、DLL/heap地址来自各自实际进程，不把预检地址抄作formal地址。formal normal同样有两代相同node地址、不同owner/enum/handle；formal detach两代node地址不同。formal五mode总44original、28actual ledger scopes，每个observed scope由两sink共享frame/result并完成配对；未知状态仍可排空已捕获的物理context，没有把quiet计数升级为引擎完成。

本 agent 重新打开[source ZIP](source-inputs.zip)，561个条目集合无重复且精确对应[source-manifest](source-manifest.json)，逐项SHA等于[构建前](build-inputs-before.json)和当前工作树；再实际打开[package ZIP](staged-package.zip)，22个条目集合精确对应[package-manifest](package-manifest.json)，逐项SHA也等于当前 `artifacts/package`。结果与[source proof](source-archive-check.json)、[package proof](package-archive-check.json)、[staging check](staging-check.json)一致。变化为[+15/~4/-0](changes-from-operation-preinit.json)；没有源码删除或冻结后source修改。

另外实际打开旧1021批[source ZIP](../foundation-local-protection-host-20261022/source-inputs.zip)，326个历史条目逐项重算等于其manifest，旧ZIP hash `d38b6630002cc680924b4313c758ec05189e0a4edd685b3f6018183ade5f307f`。对[旧安装18项](previous-installation-check.json)和[原游戏3项](original-game-check.json)再次现场读取相对 `D:/SteamLibrary/steamapps/common/Rizline` 的文件并重算，expected/actual/current全部相等。本批22项包仍仅暂存。直接以pefile读取暂存helper的导出表为58项，其中恰好7个新 `RizEngineNodeContext_*`；未加载该helper或执行游戏。

可在仓库根复现自有联合验证；以下会执行新的独立自有子进程并写另一个输出目录，不安装或调用游戏：

```powershell
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File scripts/Test-EngineNodeManaged.ps1 -ManagedAssemblyPath tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll -FixturePath D:/RizToolbox-build-cache/engine-node-final-20261025/engine-node-fixture/RizToolbox.EngineNodeManagedFixture.dll -HelperDirectory D:/RizToolbox-build-cache/engine-node-final-20261025 -DobbyPath .tools/bepinex-789/BepInEx/core/dobby.dll -OutputDirectory D:/RizToolbox-build-cache/engine-node-review-repro-20261025
```

上述结果只签发本批离线构建、自有native传输、实际CLR/dispatcher/节点账本有限联合。V4生产发布、全部scheduler/inline清理/消费者闭环与真实游戏验收仍按[下一步](next-integration.md)推进；没有新增B01–B19验收勾选，没有安装本批包，没有改变Pending/EngineCancelled/RequestPlay边界。
