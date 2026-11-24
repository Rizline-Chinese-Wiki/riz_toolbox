# Listener mutation 与 typed operation：有限复核

2026-10-24。root 已确认定向32299和完整60161真实终态exit0；本agent已读取完整runner、summary与归档proof，复核25套1553/1553，其中Hooks185/185、Audio/Content597/597。完整运行再次通过原生Operation1156检查/72次原函数调用，以及真实CLR联合5进程259检查/18次原函数调用。522项声明构建输入和22项暂存包已逐项核对，源码/包ZIP内容及hash匹配，旧安装18项和原游戏3项未变；本agent本阶段仅写本review，未构建、执行fixture、安装或启动游戏。

范围是新Operation native transport、生产C# bridge/broker、固定UnityEvent mutation allowlist/resolver及backend/ledger接线的有限只读复核，另核本agent实现的自有原生fixture和离线[ABI研究](research.md)。原生实现部分为作者自查，不能称独立第三方审计；其他代理对原生计数/异常配对和受控测试提供了有限交叉复核。范围延续[上一CLR批](../foundation-listener-clr-20261024/review.md)，新生产入口仍无游戏首写或实机验收证明。本文普通工程审查，flavor=null；不包含额外SDK能力、取消完成或B01–B19完成声明。

## Evidence

source_type=file/log；observed_at=2026-10-24。本agent实际读取冻结源码、定向与完整原始JSON、首轮失败日志及最终runner，并独立读取ZIP条目/当前文件核hash；root是唯一builder。表中source_ref同时是artifact_path，hash均为实际文件SHA-256。

| E-id | source_ref / artifact_path | SHA-256 | 实际证据 |
|---|---|---|---|
| E-research | [mutation-abi.json](mutation-abi.json) | 7e7f638cbb67f8fd0235e50f51f1a3554afd8827382ae87c7043d61e52a6e363 | 固定GameAssembly机器码、17入口与RVA/签名/RF、内联及共享风险；未执行gamecode |
| E-targeted | [targeted.log](targeted.log) | be9bdd090452a52e414bb2015d5ccd0a5436360e420350687629aa1383030f45 | 修复后32299完整定向；185Hooks、597Audio、native1156及CLR259 |
| E-targeted-run | [targeted.log.run.json](targeted.log.run.json) | b0b79ee570cfdec865d98630e9c3140ade5bd764587f523e252d99500e135396 | 2026-10-24 13:52:47 UTC，exit0、未安装/执行游戏 |
| E-first-red | [targeted-first.log](targeted-first.log) | 68867264a541d21217e4f34c1b29714aa5d0d20a4c3194a479d1b760d2b08ca1 | 首11492真实exit1：581/597，6项缺已有nativefixture环境、10项controlled ctor依赖未同步 |
| E-first-run | [targeted-first.run.json](targeted-first.run.json) | d60f9058817866930aaa92bea968a1c5e52c1f700cec86bb12639815e0076257 | 首红runner保留，未用后续pass覆盖首失败 |
| E-input | [build-inputs-before.json](build-inputs-before.json) | 5f88b9d97aee76e14f90cc87b52599f89582ff930931c3b7b15e1a1071ebca09 | 声明输入522；构建前清单与归档清单逐项一致，当前源码零变化 |
| E-full-run | [build-run.json](build-run.json) | dabcf3dbbd4c3d130a3462b6f398b1203ee6050243f40b298c6fec339831a3df | 60161真实terminal exit0，2026-10-24 13:59:46 UTC；未安装/执行游戏 |
| E-full-summary | [build-summary.json](build-summary.json) | 50f778e3e9b13a99b9964dd62d8e5a3c3a489d9430fc5324d52997e362fe85ad | 25套1553/1553；新Operation与既有隔离fixture均重跑 |
| E-full-native | [operation-native-evidence.json](operation-native-evidence.json) | 9ed7abe9eadb1087afdb24af82ff1f940a26dbac1e177d83c26d84b75343364a | 完整构建实际1156检查/72调用、failures0 |
| E-full-clr | [operation-managed-evidence.json](operation-managed-evidence.json) | 61a3030de2c5377a9908a4f2c06da30f652d8119228f0de0038a4e45a4f59ef0 | 完整构建5个独立CLR进程259检查/18调用、全部exit0 |
| E-native-abi | [OperationContext.h](../../../native/RizToolbox.JudgeContext/OperationContext.h) | 851e64fd4ae73d1f88410ae87f9f34a1bfdfe52615d0a2838527990774291a47 | Frame64/Config40/Stats48、3种kind、每kind32永久slot |
| E-native | [OperationContext.cpp](../../../native/RizToolbox.JudgeContext/OperationContext.cpp) | e0e966d2188b7018c04bd8acfd2a3c034627aaaaeb2af490dd1b426782a0f20e | 实际typed original调用、SEH finally、callback包含、永久sequence合同 |
| E-bridge | [NativeOperationContextBridge.cs](../../../src/RizToolbox.Runtime/Hooks/NativeOperationContextBridge.cs) | b0f6096e232dc948d8786fb6eccfbb318ba061bbff554b0ed60c748d68bc0d95 | C#真实native Configure/统计与委托异常包含 |
| E-broker | [ExactHookBroker.NativeOperation.cs](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeOperation.cs) | 746524e70a8860ecdd4218e968a63c6151489736436fd810bc179b0679aec502 | Prepare→Configure→Commit、永久共享、精确physical top/record配对及logical detach |
| E-allowlist | [UnityEventOperationProfile.cs](../../../src/RizToolbox.Runtime/Hooks/UnityEventOperationProfile.cs) | 38c6862bbaffd6ba5013503cf2c6a473e3f6d89a4e355d5d061fd71a61d6b0c4 | 12个已确认非泛型mutation声明，上下层并列，不声称全写者覆盖 |
| E-resolver | [NativeRuntime.OperationHooks.cs](../../../src/RizToolbox.Runtime/NativeRuntime.OperationHooks.cs) | e53638733ef997802f5e75eff42663284f9bae447902b2d4c5cc793e5e9b98df | exact class/name/params/void/RVA/MethodInfo/executable验证；未取得本轮实际IL2CPP动态证据 |
| E-backend | [NativeLocalUnityEventBackend.Mutation.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.Mutation.cs) | 0c08d76e3930d33904986d8e53e0902ae179a87a490aabb9a60d7267aae8bd7e | 先解析有限全声明，再绑定；actual self/arg根采用后才metadata/graph读取 |
| E-ledger | [NativeLocalUnityEventLedger.Mutation.cs](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.Mutation.cs) | d20446e82f16b0859d8394b491bea21746b4b9038d6603f61c5338f29ff1a608 | 512记录预算、精确frame/generation、capture失败留scalar完成责任、返回事实/确认图区分 |
| E-joint | [NativeOperationManagedRegression.cs](../../../tests/RizToolbox.Runtime.Tests/NativeOperationManagedRegression.cs) | 25ad5b64ad8644b24b34272bd774b3ec82a3a12b8c8d1e9d383b13f3f509d195 | 真实native调用生产bridge/broker，prepared engine受控，5个CLR child |
| E-broker-tests | [NativeOperationObservationTests.cs](../../../tests/RizToolbox.Hooks.Tests/NativeOperationObservationTests.cs) | f6c3d12113948254bc3d70e19461a3669db351004adeb5085e1e7c46bdb31166 | 受控bridge验证顺序、共享、异常、worker/foreign/nested及错误退出保责任 |
| E-adopt-tests | [NativeUnityEventDispatchAdoptionTests.cs](../../../tests/RizToolbox.AudioContent.Tests/NativeUnityEventDispatchAdoptionTests.cs) | f9fe48ebc2164ba1d1d3ecfa365e92579e0cac1d51f2727157b9d48589fca7e4 | 10组实际生产CaptureDispatch/AdoptDispatchReference+受控native导出seam |
| E-mutation-tests | [NativeUnityEventMutationTests.cs](../../../tests/RizToolbox.AudioContent.Tests/NativeUnityEventMutationTests.cs) | 2c72a47feb65e6193aba63615a5190d06d45b24396c65e644a009fe1d4b030f4 | 8组实际ledger方法+受控frame/graph/roots，不执行真实engine mutation |

定向原始artifact共同根为D:/RizToolbox-build-cache/listener-mutation-targeted-20261024。E-native-run：operation-native/fixture-result.json，hash c93dcd3679bf922bd8aacd5d348d5c1bf00aa789cd673a912824994c7e47b07f；E-clr-run：operation-managed/fixture-result.json，hash43fb99a7f51fe99242c2c84f83797f19de3a0790071e00dce0371da99f339062。两者实际passed=true/failures0，前者不是CLR，后者不是Dobby发布或ledger联合。

复现上下文Windows x64、固定Dobby和当前声明源码、现有VS2019 x64/.NET8；仓库根的实际定向runner为下列命令，本agent只读未执行：

```powershell
pwsh -File docs/validation/foundation-listener-mutation-20261024/run-build.ps1 -Targeted
```

实际输入hash可以用Get-FileHash -Algorithm SHA256 -LiteralPath后接上表文件复核。首轮和新一轮的输入分别保存于[targeted-first-inputs.json](targeted-first-inputs.json)、[targeted-inputs-before.json](targeted-inputs-before.json)；不把测试runner修正后复跑说成首轮本来成功。

## 原生与 managed 合同

Operation独立3×32永久slot：kind0 `void(self,MethodInfo*)`；kind1 `void(self,arg0,MethodInfo*)`；kind2 `void(self,arg0,arg1,MethodInfo*)`。Windows x64分别使用RCX/RDX、RCX/RDX/R8、RCX/RDX/R8/R9；未使用arg固定null。Frame64偏移Version0/Size4/Kind8/Reserved12/Self16/Arg0 24/Arg1 32/Method40/Caller48/Sequence56；Config40=header8+Original/Enter/Exit/User四指针；Stats48=header8+Entered/Exited/InFlight/EnterFaults/ExitFaults五计数。五export GetLimit/GetFrameSize/GetEntry/Configure/GetStats使helper总export47。

Configure只拷贝一个immutable descriptor，不patch；0成功、1非法、2永久claim。native按真实kind恰好调用原函数一次，finally总有Exit(token,flags)，合法token0也不能省略Exit。Returned1/Abnormal2/EnterFaulted4；Enter/Exit observer的SEH/C++包含，原函数SEH/C++不在transport吞掉。Caller为entry实际_ReturnAddress，不是原函数以为自己被原game caller直接调用的证明；普通C++typed transport不宣称full-register/machine-frame透明。

Sequence全helper进程CAS单调，最多signed64最大正值；到顶后永久0、既不复用也不终止原函数，managed把0视为无效且Unknown。耗尽分支只有静态实现/受控invalid-frame检查，未做native耗尽注入。未Configure就发布entry没有可用original，属于调用方错误；不被这轮fixture当成正常late-attach合同。

生产C#bridge保留permanent slot与delegate；broker Entry永久持bridge、Prepared原trampoline与回调。Prepare→Configure→Commit顺序明确，失败Entry保留且不重试/卸载。shared target比同ABI/MethodInfo/declaring/return/kind，不给不同元数据串用同一entry。Enter记录所有physical parent，worker/foreign只报告scalar fault不采集对象；完成要求同token、同thread、当前_operationTop/overflow、完整64字节frame相等。返回事实和observer责任分开；logical detach只停止未来delivery，已有context完成/Dispose继续。绑定活跃数取managed/native较大值；没有新增native admission gate、未来入口撤销或物理detach能力。

## Findings

以下severity=n/a_re、category=design、confidence=high。validated仅限引用的实际fixture/受控测试范围；source-reviewed不自动成为游戏运行或生产Root/metadata实证。

| F-id / status | evidence_ids / location | 结论及边界 |
|---|---|---|
| F-native，validated | E-native,E-native-run；Dispatch/Entry三kind | 原生三kind正常、合法零token/空参数、原SEH/C++、observer fault、worker/nested均有实际执行；统计配平。没有Dobby补丁、gamecode或首次发布安全证据 |
| F-clr，validated | E-bridge,E-broker,E-joint,E-clr-run | 实际native helper调用生产C#bridge/broker，双sink/原异常/observer fault/detach配对；prepared engine只返回自有native original地址，Commit不patch，不是生产游戏安装实证 |
| F-typed，source-reviewed+validated受控 | E-research,E-allowlist,E-resolver,E-broker-tests | 12个非泛型方法按0/1/2显式引用参数分类，hidden MethodInfo位置正确；resolver拒static/generic/inflated/byref/错RVA/错MethodInfo。实际固定游戏metadata动态解析未执行 |
| F-observe，source-reviewed | E-backend,E-ledger | self/arg0/arg1已发表引用先保存根，之后Known/Target/Class/graph；Event/Calls/Group和Group的argCalls/argEvent都与已注册graph核对。只观察、不调用Add/Remove/Stop作为功能 |
| F-record，validated受控 | E-ledger,E-mutation-tests | 每物理frame不可复用sequence与每event generation，先留record/context再capture；capture失败仍回交scalarcontext等真实native completion。最多512记录、已有图128rows/128leaves/2048roots约束，超界Unknown并保责任 |
| F-finish，validated受控+source-reviewed | E-ledger,E-broker,E-mutation-tests | Complete先写Completed/NativeReturned/flags；abnormal或Unknown只scalar收尾，不读postgraph。正常且Known后才尝试After图，ConfirmedReturned要求成功capture。嵌套上下层分别记账，不能相加视作不同逻辑mutation |
| F-adopt-prefix，validated受控导出seam | E-adopt-tests,E-backend,E-targeted | 本轮10组直接运行实际生产采用方法：第1/2/3根失败、首Target/第2Class失败、alias去重、无RDX完整列表验证、错index、2048预算、Root内源fault。其根导出由FakeNative替换，证明控制流/责任顺序，不等于真实IL2CPP GC/并发根失败 |
| F-open，accepted boundary | E-research,E-allowlist,E-ledger | 上层内联、direct字段写、generic共享仍未封口，MutationClosed=false。新12入口首次源码指令发布不在既有9写V2 receipt内，无preinit扩展/安全热安装证明；不改变Pending和RequestPlay |

有限源码复核未发现新的确定阻塞缺陷。Observer capture fault会使broker永久Unknown，但已经取得的peer context仍完成；原函数abnormal本身可由native合同正常报告，broker并非一律Unknown，owned ledger收到abnormal会标自身source Unknown并不读postgraph。不能把native原异常与observer失效混为相同通道状态。

## 已实际执行的定向结果

| 验证 | checks / 原函数次数 | 实际覆盖 |
|---|---:|---|
| Operation纯native | 1156 / 72 | entered72/exited72、uniqueSequence72；原异常12、observer异常24、native worker12、nested6、lateConfiguration3、fixture logical-delivery detach3、合法token0三例 |
| CLR normal | 50 / 3 | 三kind、双sink共6capture/6complete/6dispose，native在途0 |
| CLR original-seh | 53 / 3 | 三kind原SEH在native catch内结束，双sink6/6/6；未跨P/Invoke |
| CLR original-cpp | 53 / 3 | 三kindtyped C++ catch核payload/work/kind、code及四参数，双sink6/6/6 |
| CLR detach | 56 / 6 | 已capturecontext在detach后完成，GC后原函数继续，未来不交付；6/6/6 |
| CLR observer-fault | 47 / 3 | 六次capture尝试，first抛出无context，peer3complete/3dispose，原函数仍一次，Unknown |
| 定向受控套件 | Hooks185/185、Audio597/597 | 包含Operation受控bridge、mutation ledger、dispatch采用及既有用例；不与259相加制造单一覆盖数 |

CLR总259/5进程、原函数18次；JSON nativeInFlight0、diskHashesUnchanged=true。actualLedgerCaptureVerified=false、actualIl2CppMetadataResolved=false、dobbyPublicationVerified=false、firstInstallSafetyVerified=false、gameRuntimeVerified/gameCodeExecuted=false、installed=false。纯native lateConfigurations只是在首次entry调用前配置，并无“旧call跨Attach继承”语义；fixture logical detach只是observer active gate，native descriptor从未解绑。

原异常code/参数/typed payload及外层catch恢复有验证，完整exception Flags/Record/Context逐级透明性没有证明，纯native明确exceptionFlagsTransparencyVerified=false。真实Caller只核在自有caller/entry语境有效，非ASM exact label或游戏callsite证据。72次pure native和18次CLR使用的是不同隔离进程，不能说同一original既被调用72又被调用18次。

首11492红日志的16项失败均保留：6个既有joint test缺RIZ_INITIALIZATION_ABI_FIXTURE等runner环境；10个新采用case在Backend构造前传null launch，触发已有ArgumentNullException前置条件。修复在runner提供既有fixture/environment、controlled fixture创建真实依赖对象，生产ctor要求没有放宽。之后32299正式定向全部通过。中间Runtime CLI的CS1503依赖类型错误由root修正为实现所需INativeDetourEngine与IPreparedNativeDetourEngine两个接口；那次ad-hoc验证不替代正式runner或完整full。

## Path 与未完成部分

P-operation，path_type=callflow：固定allowlist解析exact方法（E-research/E-resolver，F-typed）→broker创建永久Entry→Prepared original→native Configure→Commit（E-broker，F-clr）→native Enter64→每sink retained scope→typed原函数一次→finally Exit→精确token/top/frame完成→Dispose→计数排空（E-native-run/E-clr-run，F-native/F-finish）。后半段已有自有fixture证据，固定游戏前半段的安全首次发布与实际执行仍缺。

P-mutation，path_type=callflow：owned Event/Calls/Group receiver匹配immutable scalar历史→先追加generation/record/context→actual self/args根采用→Known后captured Before图→原生original真实返回/异常→先标scalar完成/NativeReturned→仅正常Known时After图并ConfirmedReturned→context Dispose退回父层（E-backend/E-ledger/E-mutation-tests，F-record/F-finish）。ledger图和frame动态覆盖当前来自受控测试，尚无同一真实native Operation→生产backend metadata/GC→mutation ledger整链联合。

P-root，path_type=callflow：三source引用→按managed slot去重→Root申请成功立即存slot→全批采用后才Target/Class→列表/index检查→fault时保留消耗slot/已得根并禁止后续metadata/native读（E-adopt-tests/E-targeted，F-adopt-prefix）。Root2触发source fault时继续完成剩余已发表引用的必要采用，不继续Target/Class；Root自身申请失败只保先前成功项，不能宣称三根均已取得。

取消身份相关研究见[cancellation-research.md](cancellation-research.md)，本批不宣称Stop请求实际挂钩或CancellationCompleted。Coroutine handle/enum/generation的受控身份测试仅帮助避免跨owner/代次误判；Stop返回、Dispose、MoveNext暂时不再运行都不是引擎scheduler撤销证明。仍需真实Stop void1/void0首发、引擎登记移除/在途排空、future生产者封口，再谈取消完成与根退休。

其它剩余：固定游戏12mutation入口安全首次发布、实际IL2CPP metadata/根/GC、完整native→backend/ledger联合、所有inlined/direct/generic mutation写者、vtable mutation/完整foreign-overflow/missing-post/pre每指令展开/完整异常记录、全部delegate leaf、provider转交/取消、所有消费者和完整Retry/Return/Loader。既有9写V2 Ready仅覆盖其原有publisher身份，不能给新增12 detour补发首写许可。RequestPlay关闭；Pending=true、MutationClosed=false、InlineExecutionUnobserved=true、DispatchPathVerified=false。只保留B03/B18a既有勾选，新包未安装，用户实测稍后不变。

## 完整构建状态

root 已观察完整60161的write_stdin真实终态exit0；本agent读取[build-run.json](build-run.json)与[build.log.run.json](build.log.run.json)，两者均记录exit0，日志hash一致。完整[build-summary.json](build-summary.json)为25套1553/1553，其中Runtime160、Hooks185、Audio/Content597、Visual71、UI/Input79。新Operation纯native1156/72和CLR5进程259/18在完整构建中重跑通过；既有listener native8349、listener CLR1630及adopted-broker379也有本轮结果。各层独立计数，不相加当作覆盖率。

本agent独立逐项读取当前522输入，与构建前及[归档清单](source-manifest.json)完全一致；实际打开两个ZIP并对每个文件重算SHA-256，522源码条目和22包条目全部匹配，再核当前暂存22文件。与上一CLR批相比为[+20/~14/-0](changes-from-listener-clr.json)。冻结hash如下：

| artifact | SHA-256 |
|---|---|
| [完整日志](build.log) | 8b1b3638601ca8b1a0180de30c96c62a06562f80c8ae831d6f82fa8634cd4c5d |
| [源码清单](source-manifest.json) | 8e5bce04092e67f5ee24293d3670ae2665677f3caee74dd3eb17357c010055c8 |
| [源码ZIP](source-inputs.zip) | afa730574abc39d1e55faf10f2b78cf400e05751043b7601eefa70d23a4a7fe3 |
| [暂存包ZIP](staged-package.zip) | 2ebc43af8a7295574f3fad6a732874489f5bb1362226af97e470ef32a1067043 |

[源码proof](source-archive-check.json)、[输入一致性proof](build-input-integrity-check.json)及[包proof](package-archive-check.json)与实际读回一致。summary含94条去重原始diagnostic行，包含上游和ANSI文本变体；这不是94个新问题，也不表示零warning。

本agent另对[旧安装18项](previous-installation-check.json)与[原游戏3项](original-game-check.json)现场文件重新hash，均与原记录匹配。旧安装仍是1021批，历史归档输入326项；326来自本轮旧安装proof中的记录，本agent未在本次额外重开历史源码ZIP。新22项包仅冻结暂存，installed=false；本轮没有游戏启动或新包实机验收，也没有新增12mutation入口生产首次发布安全证明。完整构建通过不关闭上文未完成边界，不升级B03/B18a以外勾选；RequestPlay关闭，用户实测稍后。
