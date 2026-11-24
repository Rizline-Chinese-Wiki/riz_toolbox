# 首次发布 producer 与托管采用：有限复核

2026-10-24 16:34 +08:00 首审，16:38 复核两项修复源码，16:43 读取第二轮native fixture JSON，16:46核对最终汇总、日志SHA与归档记录。范围见 [scope.md](scope.md)。审查没有运行构建、安装或启动游戏；root已通过真实runner终态确认完整构建session7957 exit0，25套1515/1515。审查者在首轮构建明确结束后，按root授权修复自有mapped fixture与脚本；以下区分这些实际修改和只读审查。采用普通技术复核结构，不套用malware/APT类型。

生产代码已经包含固定映像producer、runtime-invoke原生After通道和托管采用接线。源码检查找到两个窄失败路径问题，root已修改，本审查核对了实际新代码与哈希。第二轮native runtime274、五个写前拒绝case共100、固定映像mapped175及完整1515回归均通过。mapped此前Ready/三写与之后重入Unknown都已实际观察，原文件hash未变；474源与22项暂存包已冻结，未安装。两项源码因果修复仍无专项强制故障注入证据。真实Doorstop冷启动及正常首次IL2CPP生命周期合同仍待游戏验收。

## Evidence

以下source_type为file，E-01/E-08/E-09/E-10的observed_at为2026-10-24 16:38 +08:00，E-11至E-14为16:43，E-15/E-16为16:46，其余为16:34。复现是在仓库根目录读取相应文件，并以 `Get-FileHash -Algorithm SHA256 -LiteralPath <对应路径>` 核对。16:43还重核E-01/E-08/E-09哈希与表中相同。

| ID | 文件 | SHA-256 | 原始观察 |
|---|---|---|---|
| E-01 | [FirstInstall.cpp](../../../native/RizToolbox.JudgeContext/FirstInstall.cpp) | `9a49c8a99ea31f25acd72c59915e65da93563275d3b004d0c7fde92e3e911112` | 实际映像/哈希/exports/函数表/字节核对；Prepare 后重核；三次写入为 mid、UnityEvent 父、runtime-invoke；最终 Ready 要求 writesStarted==3；rootSelf 查询失败也根据最终 phase 返回0/2。 |
| E-02 | [FirstInstallProfile.h](../../../native/RizToolbox.JudgeContext/FirstInstallProfile.h) | `cdc184ca4c1000927c8568b4f58ab4fe47f6f591996d287a697f7a58cc8b3dfb` | 固定 runtime export2845E0 的 E9 指向实际 internal2AF0E0；Unity15642E0、mid156441B/return1564420；固定原字节、unwind、双文件指纹。 |
| E-03 | [RuntimeInvokeContext.cpp](../../../native/RizToolbox.JudgeContext/RuntimeInvokeContext.cpp) | `4d769fecd1213c7d2e230fe1b8162bffe2960e6a28e5af87ff6edeb4d5199e5f` | Entry 在调用 original 之前只取一次 immutable observer；四指针原调用原生执行一次；正常返回后 After；SEH/C++ 仅走 finally 计数。 |
| E-04 | [NativeFirstInstall.cs](../../../src/RizToolbox.Runtime/Hooks/NativeFirstInstall.cs) | `6469105294bd65cab52f8cb846c1ca7560e5fa01b4cee713b95cc15fc8cbd782` | 只读取已加载 helper，Ready144/固定模块和文件 hash/entry 保护/实际 mid CALL 对 relay；没有 native Configure、Dobby 或 caller-provided Ready 工厂。 |
| E-05 | [NativeRuntimeInvokeBridge.cs](../../../src/RizToolbox.Runtime/Hooks/NativeRuntimeInvokeBridge.cs) | `1647c19e7f69501aa5266c272a0eec2e87d88fd50b64d166e648639135857519` | 先永久 Owners root 再一次 Attach；After copyFrame，核实际 OSThread/producer/真实 activeInfo，异常包含并置 managed unknown。 |
| E-06 | [ExactHookBroker.NativeVoid.Bootstrap.cs](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeVoid.Bootstrap.cs) | `9ea08119e3329fc752c5a4380f20b90c55d21f056481f287ec405bf344ce2d85` | 只采用 producer 对应 exact Unity target/entry/epoch/original，先备 sink/probe 后 Attach，shared entry 核 ABI/MethodInfo/class/transport。 |
| E-07 | [RuntimeBridge.cs](../../../src/RizToolbox.Runtime/RuntimeBridge.cs) | `6455b4bc8d3ff76adfed79e965dc3f1e7a8b6574c55fb00e55dceae9f67b580d` | Start 仅 RequireReady→NativeRuntime→Adopt；OnAfterInvoke 不再调用 runtime original 或安装 runtime detour。 |
| E-08 | [FirstInstallMappedFixture.cpp](../../../native/RizToolbox.JudgeContext/fixture/FirstInstallMappedFixture.cpp) | `6bab6b60c9373e31112cdf2e3dee5503b6ef5453536942b6a741a959a91800bb` | 隔离 DONT_RESOLVE 映射，无游戏执行；Ready 三写及十次稳定读取后再入 Before，要求2/Unknown4且三写/指令/保护/两通道不变。 |
| E-09 | [Build-Doorstop.ps1](../../../scripts/Build-Doorstop.ps1) | `ed3d2f4e61ee43cde21c449df81880eab42a7c483c44dd858635f1e2e49a26d3` | replacement 保存 `orig_result`，root 已改 `if (riz_ready && orig_result != 0)` 决定 CLR bootstrap。 |
| E-10 | [Test-FirstInstallMappedNative.ps1](../../../scripts/Test-FirstInstallMappedNative.ps1) | `67091f269e67e700b711438bd3c5053d5144331e582646bf5c038d63e8691809` | 修复多行 if 中行首-or；Parser.ParseFile实际Errors0/Tokens789；严格验先前Ready snapshot与最后Unknown分别报告。 |
| E-11 | `D:/RizToolbox-build-cache/preinit-producer-final-20261024/first-install-mapped/fixture-result.json` | `66530d269b512c6553a7459a3fed6c6f46efafbedfc63ee0827f381619647deb` | passed=true/checks175/failures0；原Ready snapshot phase3/unknown0/writes3；readyReentryStatus2/rejectedtrue，最后currentPhase4/currentUnknown1/currentWrites3、reentryBytesUnchangedtrue。 |
| E-12 | `D:/RizToolbox-build-cache/preinit-producer-final-20261024/first-install-mapped/source-image-check.json` | `aea686a9889c5d22417c06cc7647846d9f8fb5cc1da571a93d1c3e931b76b651` | fixtureExitCode0、unchangedtrue；GameAssembly前后均853d6530…5b1b79，Dobby前后均8015de7d…55ded。 |
| E-13 | `D:/RizToolbox-build-cache/preinit-producer-final-20261024/runtime-invoke/fixture-result.json` | `1058dff072f64b1190cc763d7d6aff537b358a73b5d756ce2e49482df0a7e1dc` | passed=true/checks274/failures0；12original、8After、2old、10observed、3recursive、2originalExceptions、2observerExceptions；entered/exited12、inFlight0、observerFaults2。 |
| E-14 | `D:/RizToolbox-build-cache/preinit-producer-final-20261024/first-install-rejected/fixture-result.json` | `63263738e61ee18d32990923ea1b95dffabba1661d8426dc09ba439beace39e6` | 五个独立进程：null/private/module/unmapped/badpath，合计100checks、0failures；全writesStarted0/gameLoadedfalse/gameCodeCalledfalse。 |
| E-15 | [build.log](build.log) | `4ad71734738a8e6b5640d1f8cc10609c66f6c0298379c2c9a647f39c9f996418` | root确认session7957已exit0后，本审查实际Get-FileHash核此SHA。 |
| E-16 | [build-summary.json](build-summary.json) | `52b40abeecbc2ebce2c200539af0ea92537aced215295bc06ba67f5a51dd3d71` | projects25/passed1515/total1515，logSha256与E-15一致，nativeRuntimeInvoke与mappedPublication记录独立native结果。 |

16:43[第二轮构建日志](build.log)第223行runtime JSON、第226–230行五个negative JSON、第233行mapped JSON与上述独立文件一致。16:46在root明确确认真实runner终态后才计算最终日志SHA；exit0依据runner终态，不能只由“Package:”尾行推断。

[源码归档检查](source-archive-check.json)记录474 entries、5,020,113 uncompressed bytes、allEntryHashesMatched=true，source ZIP SHA `89cdc781791f9c8ccfa42a5a69c201ef57661814d79d2b63b141cc6367cfd039`，manifest SHA `4ca0b9901dad722d8dec0a9e3735d08a52998fdc9060dd878cdf83929694f9c5`。[暂存归档检查](package-archive-check.json)记录22 entries、3,601,739 bytes、allEntryHashesMatched=true、installed=false，package ZIP SHA `4c7939c1beab080b40194b346bbcec62f7a545eb2f8ef2761a07a403099e3740`。[与1511差异](changes-from-1511.json)为新增21/修改11/删除0。归档逐项哈希检查由root冻结工具执行，本审查读取其结果；它们不等价于游戏已安装或B19干净环境全矩阵复现。

## Findings

### F-01：rootSelf 查询失败的竞态返回分类

severity 为 low，category 为 design，status 为 source fix reviewed，confidence 为 high；证据 E-01。

旧实现的 `RizFirstInstall_BeforeIl2CppInit` 在自身模块 `GetModuleHandleExW(FROM_ADDRESS)` 失败时调用 `DisableIfUnwritten(); return 0;`。若另一个 Before 重入已把全局 phase 置 Unknown，`DisableIfUnwritten` 会保留 Unknown，但该分支仍返回 0；Doorstop 因而把它当“写前已知拒绝”并允许 original init。正常函数尾部会重新核 Unknown 并返回 2，旧提前返回遗漏了同样的分类。

函数地址有效且helper未卸载时该查询通常应成功，故这是极窄错误路径，未动态注入。root已修复为仅最终phase==DisabledNoWrite返回0，否则返回2；审查读取实际代码，确认该提前失败分支不再无条件返回0。没有新增绕过身份验证的testmode。修复后完整full已通过，self查询失败与并发重入的组合仍未专项注入。

### F-02：原 init 返回失败时仍启动 CLR

severity 为 medium，category 为 design，status 为 source fix reviewed，confidence 为 high；证据 E-09，以及上一批 [启动合同](../foundation-first-install-20261024/recommendation.md)。

旧 Doorstop replacement 是 `orig_result = il2cpp.init(domain_name)` 后 `if (riz_ready) il2cpp_doorstop_bootstrap()`。原 init 正常返回 0 时仍会启动 CoreCLR，与既定“init 失败或异常时不启动 CLR”合同不符。原生异常会自然越过 bootstrap，但旧实现对失败 int 返回值缺少分支。

root 已将条件改为 `riz_ready && orig_result != 0`，保持原 domainName、original 一次及 int 原值返回，仅 native Ready 且原 init 非零才启动 CLR。本审查已核新代码；尚无 original init 返回0的专项执行注入，不能把一般成功构建当该分支的动态证明。

### F-03：写尝试之前和之后的责任分类有实作

severity 为 info，category 为 design，status 为 validated by source review，confidence 为 high；证据 E-01、E-02。

固定映像和 Dobby 的文件以拒绝 write/delete sharing 的句柄 pin 住并计算哈希，核实际模块 AllocationBase、固定 exports/内层 E9、PE、`.pdata`、`RtlLookupFunctionEntry`、原 prefix 和 unwind。没有 UnityPlayer 或进程名称依赖阻止隔离映射正向 case。DobbyPrepare 后重新核 FixedGame；若意外改变固定字节，则转 Unknown，不能作为未改写拒绝继续。

mid 与两个 Commit 在尝试改变游戏保护/指令之前增加 writesStarted。护页恢复、flush、字节/跳转链读回、两通道身份任一失败都保留原生资源并转 Unknown，不回滚、重试或释放。这里 validated 仅为代码属性复核；真实成功/故障行为必须另看 root 最终 fixture 日志。

### F-04：runtime 原函数保持 native，旧调用不会借新 After

severity 为 info，category 为 other，status 为 validated by source review，confidence 为 high；证据 E-03、E-05、E-07。

Entry 在原函数之前 snapshot observer，Dispatch 不重读全局 descriptor；normal return 才调用同一份 After，原 SEH/C++ 不进入 CLR After，native finally 仍清 Entered/Exited/InFlight。返回值来自 private original result 局部量，After 中的 frame 观察不会替换返回结果。observer SEH/C++ 被包含并形成 native sticky unknown/counter；CLR 自己包含的异常形成 managed bridge unknown。

RuntimeBridge已去掉原来的managed original wrapper/首次runtime Dobby安装。Adopt先永久root callbacks/library，再一次Attach，并且允许native在Attach返回之前调用已经构造完整的After。E-13的实际274检查现已通过：真实ABI、worker、递归、旧blocked original跨Attach与两种原异常/observer故障均按fixture断言执行。它验证自有native fixture，仍非真实游戏调用或生产冷启动。

### F-05：broker 采用同一物理入口，没有再挂一次 Dobby

severity 为 info，category 为 design，status 为 validated by source review，confidence 为 high；证据 E-04、E-06。

新生产绑定路径仅允许 producer 实际 UnityTarget，随后 AdoptBootstrap 同 slot/Target/Original/epoch 并核 Entry，准备 observer/probe 后 Attach；该路径不调用 Prepare/Commit 或重配原通道。已存在 entry 还核 ABI、MethodInfo、DeclaringClass、ReturnClass、transport/Entry 与 Known，才共享新逻辑订阅。原来的显式 bridge 工厂重挂路径仍供隔离 fixture 等已有内部分支使用；它本身不能生成生产 Ready 凭据。

### F-06：映射正向验证的证明范围

severity为info，category为other，status为validated by mapped native fixture，confidence为high；证据E-08、E-10、E-11、E-12。

该fixture实际175检查、0失败，检查真实固定映像的producer publication bytes：Info144、两通道真实identity、parent/runtime prefix已改变、mid E8解码到FF25 relay的完整helper指针、邻字节和页保护、RtlLookup对原`.pdata`、十次稳定Known信息。Runtime/UnityEvent/mid通道执行计数均为零，与fixture源码中没有游戏函数调用一致。E-12实际记录脚本hash前后核对不变及fixtureExitCode0；未安装、未改磁盘GameAssembly。

这不证明正常Doorstop首次init的启动时序，也不证明IL2CPP已初始化、实际listener/GC/结果菜单/资源退休正确。所有Ready检查及十次稳定读取之后，第二次Before实际返回2并将producer置Unknown；writesStarted仍3、runtime/unity/mid/relay指令和页面保护不变，两个transport的身份/零执行信息也不变。JSON的phase3/unknown0明确标`phaseObservation=before-ready-reentry`，最后状态实际为currentPhase4/currentUnknown1；这不是当前仍Ready的声明。两个epoch均为1属于独立runtime与bootstrap计数域，不表示可跨通道互换receipt。

这里“真实三处发布”仅指隔离fixture进程中生产publisher确实改变了actual固定映像的mid、UnityEvent父、runtime-invoke内部entry三个内存现场，并通过字节/保护/跳转/函数表断言。映射来自DONT_RESOLVE，未执行GameAssembly DllMain、original il2cpp_init或任意游戏函数；E-11同时明确firstInstallSafetyVerified=false/gameRuntimeVerified=false，不能扩大为支持启动合同已验收。

## Path P-01

path_type 为 callflow，start 为受支持正常启动里的固定 Doorstop init wrapper，goal 为首次发布后 post-init 只接观察回调。

1. native wrapper 在原 init 之前进入 producer，核固定 actual module/exports/hash/代码/函数表。E-01、E-02，对应 F-03。
2. DobbyPrepare 返回实际 trampoline，先配置永久 runtime 与 UnityEvent parent/mid，再按 mid→Unity parent→runtime 写入及复核，Ready3/Unknown0/Writes3 才允许原 init。E-01，对应 F-03。F-01 提前失败分支已按最终 phase 修复分类。
3. 原 init 以真实 int ABI 调用一次；CLR bootstrap 现受 native Ready 与原 init 非零两项约束。E-09，对应 F-02，尚缺失败返回的专项动态注入。
4. CLR Runtime 读取已加载 producer 并采用 runtime native After，broker 采用现成 UnityEvent 物理入口；都不执行这三处首次 Dobby。E-04 至 E-07，对应 F-04、F-05。
5. native 每次 invocation snapshot callbacks，original 执行一次；观察故障保留原函数结果/异常责任。E-03，对应 F-04。真实 listener/inline graph 和全 run 退休仍为后续完整目标。

## 后续验收

首full session93340由root确认为exit1，日志另存 [build-first-parse-failure.log](build-first-parse-failure.log)：native runtime fixture274和五个写前拒绝case已运行，但mapped脚本在多行if的-or解析处失败，不能记正向通过。该脚本后经实际Parser.ParseFile核Errors0，并追加E-08重入断言；第二full session7957最终exit0、1515/1515，实际正向结果如E-11，而非追认首轮失败为成功。

native runtime、映射正向/写前拒绝已有上述独立实际结果，完整离线1515回归与474源/22包冻结均已确认。仍缺：F-01组合并发失败、F-02 init返回0专项注入；实际Doorstop冷启动中producer完成→original init一次→CLR Attach→首个合法UnityEvent的顺序证据；初始化失败/异常的真实透明行为；真实IL2CPP/listener/GC与完整run资源退休/Retry/ReturnLoader。真实冷启动仍按用户“实测稍后”保留。不得因本批producer或测试数量增加把完整B01–B19标记完成。
