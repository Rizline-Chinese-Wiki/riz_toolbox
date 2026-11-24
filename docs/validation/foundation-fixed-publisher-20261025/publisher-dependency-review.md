# 自有固定 publisher：产物依赖与本地调用有限复核

2026-10-25。**本次产物的本地调用层已经移除 CRT、Dobby、堆分配、stdio 和迟到 LoadLibrary 路径；它仍不证明 helper/OS 的全部传递依赖适合生产 loader，也尚未接入早期加载宿主或游戏。** 本 agent 是 FixedPublisher.h/.cpp 作者，执行作者自查和实际 PE 只读研究，不称第三方独立全审；root 是唯一 builder/runner。本 agent 未构建、执行 DLL/fixture、安装或操作游戏/UI。采用 ida-reverse 工具核验与本地 pefile/Capstone；没有伪称使用不可调用的 IDA。

范围见 [scope.md](scope.md)。root 已实际观察最终独立组件 session5523 terminal exit0，[component-run](component-run.json)和[汇总](build-summary.json)已记录12项声明输入、4个二进制、57个独立child/57份actual memory snapshot。本 agent 保留第六编研究原件后，实际对最终DLL重跑磁盘研究，并核当前12源码与冻结manifest一致；没有重建。前批 early-loader 的576源/完整25套1617与99557终态保持独立，本批没有重跑完整25/1617，也不拼成新publisher已通过loader/游戏部署。

## Evidence

source_type=file/log，observed_at=2026-10-25。以下 hash 由本 agent 现场计算；source_ref 同 artifact_path。

| E-id | 已实际读取 | SHA-256 |
| --- | --- | --- |
| E-binary | [最终归档publisher DLL](raw/build/RizFixedPublisher.dll)，19968字节，与final cache一致 | `3436f4b28483a6200c0daea0532de7a3de92d82efe557126007849f6a47e683b` |
| E-source | [FixedPublisher.cpp](../../../native/RizToolbox.FixedPublisher/FixedPublisher.cpp) / [header](../../../native/RizToolbox.FixedPublisher/FixedPublisher.h) | cpp `144a551073703943c7aa99f40134aea8f6348d8bd357d595549ff917b5eae6b1`；h `2f45c9ca31e36b2daab3a664dea0e32c9fd928eb6fafe3391a223790e246b5d2` |
| E-static | [磁盘研究脚本](research/inspect-publisher-dependency.py) / [最终完整指令、RF和调用JSON](research/publisher-dependency-evidence.json) | 脚本 `3d2de387568a0c3848a90cc34f0a683f5863705fe47065fcb99f7fe6faf86258`；JSON `34a2b81fac073ec256e0be8cadb526f1fd254ca2981ffaddb7c8b6179a7cfad7` |
| E-unwind | [最终normal展开原件](raw/cases/normal/case.unwind.jsonl)，50条真实RtlVirtualUnwind受控输入/输出CONTEXT | `9f6d48ffd13e27ea99b7d98e870ce2f72a5baf263a0427abe9dc1c1ccd3edc80` |
| E-calls | [最终normal调用原件](raw/cases/normal/case.calls.jsonl)，实际typed原函数/嵌套/worker及原异常 | `b13a9496f411e0c801916035d86589f5c183f187bc0308d5b596516168382679` |
| E-receipt | [最终normal receipt](raw/cases/normal/case.receipt.bin)，21640字节固定POD | `ab9bc8c9d6379d5dccb07f3ce50a3c984d3220454da00aed4c3b556f223c5fe0` |
| E-memory | [最终normal独立物理内存快照](raw/cases/normal/case.memory.bin) | `6b4c1d957dc475f0f0f6f108b611084a0925b215a206f9b3bba73a57b98b7202` |

最终研究输入为 `D:/RizToolbox-build-cache/fixed-publisher-final-20261025/RizFixedPublisher.dll`，现场重算与上表归档DLL相同。本 agent 实读 [process.json](raw/cases/normal/process.json)的exitCode0、[stdout](raw/cases/normal/child.stdout.log)，以及最终独立unwind-proof/receipt-proof/calls-proof；其结论分别保持依赖层、受控CONTEXT和真实原调用的边界。脚本复核命令：`C:/Python314/python.exe docs/validation/foundation-fixed-publisher-20261025/research/inspect-publisher-dependency.py --publisher D:/RizToolbox-build-cache/fixed-publisher-final-20261025/RizFixedPublisher.dll`，仅读磁盘并重写本研究JSON，不加载执行模块。

第六编研究在覆盖前实际保存为[旧研究JSON](research/before-final-publisher-dependency-evidence.json)，SHA256 `9213349be6934190fdb206abc254bb7ec2fa4c4f83821c0e956ab6e9dcaedca2`。旧DLL SHA为 `1390c4cd241a233d1055d94ea00c357850649b56bbffe98eae3a631fb28661cd`；本 agent 对新旧JSON逐对象比较，最终路径/hash改变，26个region、全部graphs、原桩指令/偏移/RF、固定字符串引用均完全相同。因此下面机器码结论确已核到最终产物，没有直接外推旧二进制。

## Findings：实际 imports 与本地 call graph

E-binary 为 AMD64，ImageBase `180000000`、SizeOfImage `e000`；AddressOfEntryPoint=0，TLS、delay import、load-config directory 均为0。它没有动态 CRT 启动入口、TLS initializer 或 CRT DLL导入；零初始化POD与函数地址重定位仍依赖正常映像加载，不等于可以未映射就调用。构建明确 `/NODEFAULTLIB /NOENTRY /GS- /GUARD:NO`，不是具备CFG/安全cookie的产物，也不宣称绕过这些限制取得生产安全。

实际普通导入是 Kernel32 的15项和 ntdll 的1项：

| 本地用途 | 实际普通imports |
| --- | --- |
| 固定模块/已知export与文件身份 | GetModuleHandleW、GetModuleFileNameW、GetProcAddress、CreateFileW、GetFileSizeEx、ReadFile、CloseHandle |
| 当前调用者、页检查与发布 | GetCurrentThreadId、GetSystemInfo、VirtualQuery、VirtualProtect、FlushInstructionCache、GetCurrentProcess、GetLastError、SetLastError |
| native SEH语言处理器 | ntdll!__C_specific_handler |

没有 CRT/Dobby DLL、malloc/free/new、memset、HeapAlloc、VirtualAlloc、LoadLibrary/LoadLibraryEx、stdio、CNG、线程创建/等待或任意传入callback。该结论不仅依赖 import 名单：E-static 合并实际 RUNTIME_FUNCTION/CHAININFO，各精确分段按入口和直接分支构建 CFG，保存原始bytes/hash，并从六个public API及静态原桩追踪本地直接call/jmp和异常目录的语言处理器。26个请求region没有未解码CFG指令。未把代码间隙、跳转内嵌地址或原桩 exclusive-End label当函数指令。

| public API | 已访问本地函数/leaf数 | 非IAT间接call | 已解析实际import范围 |
| --- | --- | --- | --- |
| Prepare | 11 | 0 | 有界磁盘SHA与模块/页/调用者检查、native SEH |
| Bind | 8 | 3 | 固定module/export查询、页核验、native SEH；3个外部helper查询另列 |
| Commit | 9 | 0 | 当前进程页检查/保护、回读、缓存刷新、native SEH |
| GetInfo | 4 | 0 | 调用者输出页检查、native SEH |
| GetInfoSize / GetVersion | 各1 | 0 | 无普通import调用 |

本地堆、格式化日志、运行时指令解码和Dobby懒单例不再存在。显式 SHA256 使用有界栈缓冲和本地算术，Copy/填充为volatile逐byte写；产物已链接通过且实际call graph没有被隐式 `memset` 或分配函数补上。gate 为编译器生成的原子指令，不等待其他线程完成任务；它只提供本组件 API互斥，不能授目标调用历史、并发首写或loader准入。

### 三个 helper 查询仍是外部边界

Bind internal primary `10b0` 中有且仅有三处未由普通IAT解析的外部call：`1428 call rsi`、`1550 call [rbp+b0]`、`15cf call [rbp+a8]`。实际代码分别由固定字符串 `RizEngineNodeContext_GetAbiVersion`、`RizEngineNodeContext_GetEntry`、`RizEngineNodeContext_GetInfo` 的GetProcAddress结果提供；字符串RVA为5230/5258/5278，实际LEA引用116c/117c/118f保留在JSON。源码同时要求同一精确已加载helper映像、可执行页、ABI2、kind/slot28/target/original/entry/helperEpoch完整tuple和未Attach/零活动/无Unknown。

这不是任意用户callback，但也不是把helper实现展开审计完毕。它不调用helper Configure/Attach：root宿主在Prepare取得original之后另行配置，Bind仅核验，publisher epoch与helper epoch分别存储。Kernel32/ntdll实际实现、转发、系统内部路径和helper调用闭包均未在本稿审计；不能将“publisher本地无LoadLibrary”提升成所有传递路径都无动态加载或适合loader lock。

## 原桩FF25失败历史与最终R11合同

原source发布计划仍是14字节 `FF25 [rip+0] + entry64`，随后NOP到N=15/18/16/15。它不含对地址内嵌数据的执行路径；N字节是完整指令边界，父静态验证另核原字节、发布字节、RIP落点和保护。本组件不暴露任意profile，要求自有固定模块、DATA marker `RIZFIXEDOWNEDV1\0`、四个固定export、实际磁盘SHA与精确prefix。

**失败的是先前 original stub 的末尾FF25候选，不能把它与source入口patch混为一谈。** 本 agent 实读 `fixed-publisher-normal-first-20261025/normal.unwind.jsonl`，46个受控CONTEXT中3条失败：Execute offset18、Release16、Detach15；afterRip=0、RSP未恢复到预期。即使 SizeOfProlog 包含FF25，实际RtlVirtualUnwind仍按不期望的epilog路径处理。原件SHA `568294892314a4bccf639e9e9e2a79c2c4e13b2253f9a71ea0c66d6009dbe67a`，process exit1、stderr“Unwind/Prepare failed before source publication”保留，不能称最初即安全。

最终静态original桩准确重放prefix，再以 `4c8b1d disp32` 加载R11，`41ffe3` 跳R11；每项tail均10字节，没有内嵌指针。Prepare实核该序列与RIP slot正好为对应 `RizFixedContinuation[k]`，四prefix全通过后才设置四continuation。该MOV/JMP保持原flags并明确clobber volatile R11；只承诺四种typed ABI，不承诺全GPR、完整RFLAGS或任意PC异常透明。

实际最终产物四桩为：Start `[4690,46a9)`，Execute `[46b0,46cc)`，Release `[46d0,46ea)`，Detach `[46f0,4709)`；prefix-end/MOV PC分别469f/46c2/46e0/46ff，JMP PC均为MOV+7。静态 `.pdata/.xdata` 和机器码均在 E-static；没有依赖动态分配、RtlAddFunctionTable、运行时解码或写入桩代码。Execute LEA RBP是已保存的临时值，不产生UWOP_SET_FPREG；Release的DEC只重放一次，续接先消费CMP ZF；Detach保原home-slot save与清EBP语义。

最终normal原件有50个实际RtlVirtualUnwind调用，覆盖声明的原桩指令PC、MOV/JMP、目标continuation等受控上下文，独立验证454项通过。另真实调用验证271项/receipt246项通过：34次original、20组observer Enter/Exit、16次被原native catcher捕获的SEH/C++异常。没有把50个构造CONTEXT称50次真实异常，也没有把真实续接中抛异常推广为每个PC异步异常已经执行。第五次编译后的第二轮正常原件已50展开通过，但四个 `flags==0` 测试假设失败；后续测试改为先取得实际未发布异常baseline再比exactflags，保留旧红，未改publisher掩盖它。

## 发布责任与部署边界

只读源码核验本地四项Prepare→全部Bind→按0..3 Commit；定长POD保留原/补丁/回读32字节、每内部阶段、实际byte数、页保护义务及缓存刷新结果。写前改变保护后如确定恢复成功，可终止为DisabledNoSourceWrite，仍永久claimed/prepared/bound且不能重试；恢复失败或任一source写尝试后保Unknown。PartialWrite确实只写选定数量的byte；forced API failure明确Injected而非伪称Win32实际返回失败。[最终57模式矩阵](fixed-publisher-evidence.json)与raw/cases aggregate现场读取相等，汇总2560项host检查、11522项独立receipt断言、25878项独立展开断言及271项调用断言，57份实际内存快照分别保留；它们是不同计数，不混成原25套托管suite。本稿不扩大为任意OS失败/并发/指令写入中断全覆盖。

当前是普通自有宿主在loader窗口外使用的新组件。它尚未替换前批 early-loader Proxy，也未证明静态依赖顺序、TLS/CRT/首个相关调用前准入；没有真实系统WinHTTP转发/搜索路径、生产UnityPlayer manifest/首发owner/V4、真实对象/GC、scheduler/async/futureStart封口或整局退休。不能只更换模块名把本prototype当游戏安装器。JSON的fullIndirectCallClosureProved、allTransitiveDependenciesLoaderSafeProved、earlyLoaderAdmissionVerified、productionV4Issued、gameReady、engineCancellationCompleted均为false，Pending与完整B01–B19验收继续保留。
