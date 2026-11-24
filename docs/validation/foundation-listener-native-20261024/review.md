# Listener 原生通道有限复核

复核日期：2026-10-24（UTC+8）。范围是本仓库新 ListenerContext 原生 lane、Bootstrap 父调用接线，以及自有 listener fixture 的调用与展开合同。只读审查源码、ABI 和实际定向结果；构建、运行和冻结输入均由 root 唯一负责。本复核不修改生产源码，不运行游戏或安装，也不新增 C#／公开 SDK 类型。

**最终完整构建已通过。** [构建终态](build-run.json)记录 root 会话 45158 exit 0，[最终日志](build.log)与[构建汇总](build-summary.json)的 SHA256 一致，为 `d4bf92cfcea01e3fd52461281e0041203332dc3633b8def7f57b5d45abb2c714`。25 套托管回归 1515/1515；本批独立 listener 原生 fixture 8349 检查完整重跑通过，单独计数，不混入 1515。已有 Bootstrap698、实际 CLR broker callback379／3 进程、映像 producer175、CLR receipt 采用59／2 进程、Unknown328／100 次确定交错读取，以及其余既有验证也均在此次完整构建通过。这里的 CLR broker callback 是上一批自有 fixture 路径，不能当成本批新增 listener 的托管回调证据。

首次定向会话 68082 也 exit 0，[定向运行日志](targeted-listener.log)与实际缓存 `D:/RizToolbox-build-cache/listener-native-targeted-20261024/listener-context` 的 `fixture-result.json`／`fixture-stdout.log` 已核对：两份文件 SHA256 均为 `512bc022662ce344fad03b55e7cef726976fdf7641dec52c130e68a3cae38b62`，`fixture-stderr.log` 为空；runner manifest 的 exitCode 0、三项输入前后哈希一致、未安装且无游戏执行。完整构建缓存 `D:/RizToolbox-build-cache/listener-native-final-20261024/listener-context/fixture-result.json` 与本批[归档结果](listener-native-evidence.json)全部字段一致，stderr 同样为空；下列值已在最终完整构建重复取得。

| 定向及最终完整构建的实际证据 | 结果 |
|---|---|
| 独立原生检查 | 8349 检查，0 失败 |
| 真实 callee ReturnPC／original 调用 | 各 74 次，三处源 CALL 返回分别保持 source+5／+5／+6 |
| 正常 continuation replay | 62 次；原异常调用未伪发正常 replay |
| pre／post fullstate 比较 | 68／56 次，逐 GPR、XMM 低 128 位、RFLAGS／MXCSR／x87 控制字；post 允许真实 INC／MOV 的规定变化 |
| callback 内真实栈遍历 | 124 次，由 `RtlCaptureContext` 起步，post 必经动态 stub 后退到 native original caller，再到外层 caller |
| post 指令边界 virtual unwind | 224 次，即 56 次 post × 0／5／7／9 四个合法边界，包含末 E9；核实际保存 caller／RSP／非易失寄存器与 XMM |
| fixture 自有 source 写入 | `WriteCode` 14 次，即两套各 3 pre＋3 post＋1 mid；另有两个 parent Dobby Commit，不把该数称为所有机器写入或游戏 producer 写数 |
| 冻结前父快照与异常／并发场景 | 2 次旧父晚 Attach，其中 1 次旧 V1 observer 为 null；6 个原 SEH、6 个原 C++、4 个观察故障、2 个 worker、4 个同／异父嵌套场景 |

定向通过前的五份编译失败日志保留：[首轮 MASM](targeted-first-asm-failure.log)、[第二轮 MASM](targeted-second-asm-failure.log)、[第三轮 MASM](targeted-third-asm-failure.log)、[C++ 类型及对齐](targeted-cpp-compile-failure.log)、[显式对齐](targeted-cpp-alignment-failure.log)。[构建终态](build-run.json)逐次保留 session、exit 1 与日志哈希。root 在各次终态后修复 fixture 的 VS2019 MASM 名字长度／宏续行／NEAR 标签／局部断言范围、lookup 指针 const 类型，以及 `/WX` 下的显式 padding；未用放宽运行断言取得通过。生产 Listener 四文件未因此修改，失败日志不被后续成功覆盖。

## 已核对的 ABI

依据实际 [ListenerContext.h](../../../native/RizToolbox.JudgeContext/ListenerContext.h)，Windows x64 默认对齐下四个公开原生结构为 Config 80、Observer 32、Frame 176、Info 176 字节。每项都有对应 `sizeof` 和关键 `offsetof` 断言；定向和最终完整构建的 helper／fixture 编译均通过这些断言。

| 结构 | 字段与字节偏移 |
|---|---|
| Config80 | Version／Size 0／4，ParentEpoch 8，SourceReturnPc[3] 16，PostTransportReturnPc[3] 40，DirectTarget[2] 64 |
| Observer32 | Version／Size 0／4，Before 8，After 16，User 24 |
| Frame176 | Version／Size／Slot／Site 0／4／8／12，Stage／OsThreadId／Flags／Reserved0 16／20／24／28；Epoch／ParentSequence／ChildSequence／ParentManagedToken 32／40／48／56；Event／List／Invokable 64／72／80；ArgRcx／Rdx／R8／R9 88／96／104／112；ActualTarget 120，SourceReturnPc 128，TransportReturnPc 136，SourceRsp 144，Index 152，MachineFrame 160，Reserved1 168 |
| Info176 | Version／Size／Slot／Attached 0／4／8／12；Epoch／ParentEpoch 16／24；PreEntry／PostEntry 32／40；SourceReturnPc[3] 48，PostTransportReturnPc[3] 72，DirectTarget[2] 96；Entered／Exited／InFlight／Faults／Abnormal／Overflow／MissingPost 112／120／128／136／144／152／160；Unknown／Reserved 168／172 |

`Before` 返回的是独立观察 child token，`After` 显式接收同 token；零值不能用来推断有没有调用 Before。ParentManagedToken 与该 child token、内部私有 child record、单调 parent／child sequence 分属不同身份。`MachineFrame` 是本次 pre/post 的栈内只读快照，异常父收口时为 null，不能跨回调保留该指针。

virtual 目标读取前先建立私有 child；若读取本身异常，Before 尚未执行，父收口仍可能发送 `Abnormal` After，token 为 0、带 `TargetReadFaulted=8`。这是单独的失败通知合同，不能称为已经取得观察 token 的正常配对。`BeforeFaulted=1` 则表示 Before 的观察异常已被包含；这两类 flag 不能混为一谈。

本轮采用独立 listener lane，并非设计文档候选 V2 聚合 observer；[六区设计](../foundation-adopted-broker-20261024/native-listener-design.md)中的候选 128 字节 frame 也不是实际 ABI。独立 lane 的快照在 V1 observer 快照之后、任何 V1 Enter 回调之前固定；两份 observer 有各自发布点，不声称一个聚合发布点。V1 observer 为 null 时，已固定的 listener observer 仍可独立执行，ParentManagedToken 0 合法；需要遮蔽的是 listener 自己的 null 快照与当前更深调用，不能借晚 Attach 或更早祖先。旧 Bootstrap V1 的布局、旧 Ready144／三次写入 receipt 不因此获得新增六区的授权。五个新增 exports 仅提供入口、配置、一次 Attach 和信息读取，不能据此声明已有游戏 publisher 或 managed ledger 接线。

## 审查中的调用与展开合同

pre 的源 CALL 硬件返回地址保留，恢复后经尾跳进入实际 callee；74 次实际 callee 记录均核对 source 返回 PC。virtual 的 `[saved RAX+178h]` 目标在源码中只读一次，读异常不在观察异常保护内。允许借用的是 callee 的四个 home slots，不包含硬件返回槽或第 5 实参；不使用 RSP 以下的 red zone。这些是 [Microsoft x64 stack usage](https://learn.microsoft.com/en-us/cpp/build/stack-usage?view=msvc-170) 的 ABI 前提；当前 fixture 只提供其实际调用形态及外侧 canaries 的证据，不代表任意新签名都符合该前提。

fullsave 的每个非易失寄存器、XMM 低 128 位、RFLAGS、MXCSR 和 x87 控制字，须与真实 callee 入口及 post 状态逐项比较；不能推广为 AVX 上半部或完整 x87 数据栈保存。恢复路径的 LEA／POP／尾 JMP 必须符合 [Microsoft x64 prolog and epilog](https://learn.microsoft.com/en-us/cpp/build/prolog-and-epilog?view=msvc-170) 的展开规则，不能把任意间接 JMP 塞入 fullsave epilog。

post 的源 E9 不压栈；stub 的真实观察 CALL 才建立 helper 返回槽。SourceReturnPc 与 TransportReturnPc 必须分开，SourceRsp 均表示原 parent body 的 P。stub 不新增固定栈或非易失寄存器保存，并通过 CHAININFO 关联实际原 parent；链表的帧寄存器与偏移须与 primary 匹配。[Microsoft x64 exception handling](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64?view=msvc-170#chained-unwind-info-structures)给出该合同，但注册成功本身不能证明展开正确。

[固定 parent unwind header](fixed-parent-unwind-header.json)记录实际受支持游戏文件的 `01 61 0a 00`：版本 1、flags 0、prolog 97、codes 10、帧寄存器与偏移均为 0。它支持本轮零 FP 的静态 profile 前提；没有执行游戏，也没有验证新增六区发布或所有 unwind code 的运行效果。

末尾 E9 的设计推论与本机证据分开保留。官方 [.NET amd64 unwinder 源码](https://raw.githubusercontent.com/dotnet/runtime/main/src/coreclr/unwinder/amd64/unwinder.cpp)的相对 JMP 分支解析 `SameFunction` 并比较 primary unwind 身份，支持“跨 secondary 范围仍可能视为同函数”的推论；源码本身不是本机 Windows 的证明。本次本机 fixture 在 0／5／7／9 四个实际 stub 指令边界，以真实 post 快照构造对应 context 并模拟此前已执行的 INC／MOV，224 次 virtual unwind 均退到实际 parent 保存的 helper caller，核 RSP、所保存非易失寄存器与 XMM；没有发生末 E9 被误当成 parent 已退栈的结果。另有 124 次回调内真实 `RtlCaptureContext` walk，post 路径确实经过动态 stub 再到外层 caller。两种证据互补；边界 context 是 virtual unwind，不冒称在每条指令上真的中断采样，也不把指令内部字节计为边界。

已读 [ListenerContext.cpp](../../../native/RizToolbox.JudgeContext/ListenerContext.cpp)、[ListenerEntry.asm](../../../native/RizToolbox.JudgeContext/ListenerEntry.asm)及 [Bootstrap 接线](../../../native/RizToolbox.JudgeContext/BootstrapVoidContext.cpp)：父 Enter 在任何观察 Enter 前固定 listener descriptor，null 快照的父也有私有记录，pre 使用当前父 capture，不重新读取全局 observer。pre 建自己的 child 后才做一次 virtual target 读取，异常读取位于观察保护外，父收口可识别 TargetReadFaulted；ASM 恢复后只把目标存到 I+8 的 home[0]，经合法 FF25 epilog 到不改栈的 leaf 尾跳，未替换 [I] 的原 source 返回 PC。

当前 TLS top 不搜索更早父；child 的 native record、两个单调 sequence、观察 token 独立。正常 post 核 exact parent／child sequence、site／source rsp／event／list／index；原 callee SEH／C++不经过 post，由父 finally 在父观察 Exit 前按本代 baseline 清 child／overflow masks。定向及最终结果覆盖正常三路、两种旧父快照、同／异父嵌套、各 site 原 SEH／C++、Before／After 观察故障及之后的正常恢复，核配平和 sticky Unknown。overflow、foreign parent 和损坏 post 的保护路径仍只是源码复核，不能随已有测试一起标成已实测。

### 审查发现与源码修正

| 发现 | 严格因果与处理 | 复核状态 |
|---|---|---|
| Post 缺少 list／index 校验 | 初版仅比 state／site／source rsp／event；实际 RBP／RBX 已变化时仍可能发 Returned，语义 frame 却携 pre 的旧 list／index。实现代理补入实际 post RBP==pre list、RBX==pre index；不符置 TLS／lane Unknown，由父收口异常清理，不能正常 After | 已核修正与正常运行；没有受控坏帧实测 |
| Listener 管理路径的父依赖 Unknown 传播有窗口 | 初版 ParentKnown 只读 Bootstrap 本地 unknown；底层 Void／Gate／Mid 先失效、尚未触发 Bootstrap GetInfo 刷新时，Listener Attach／GetInfo 可能暂报 known。管理路径现调用同一依赖刷新；Attach 获一次 CAS 资格后发布前再刷新，失败消耗资格。当前调用／异常清理 accessor 不依赖 Known，避免失效时丢掉已有收口责任 | 已只读核实际 Bootstrap／Listener 修正；不通过自造 mock 宣称该时序已实测 |
| fixture 展开覆盖曾混入指令内部位置 | 初版 offset 13 位于末 E9 内部，并非指令边界。修为 0／5／7／9；一层 parent unwind 终点是实际 parent 保存的 helper caller，不能直接跳过 native transport 到最外层 caller。完整链另从 helper 内真实捕获的 context 行走，非易失寄存器按真实 parent 保存槽核对 | 定向及最终完整构建各取得 224 次边界展开与 124 次真实 walk 通过 |

fullstate 证据分别区分 pre 快照对真实 callee、post 快照对 helper 返回后的真实 continuation。fixture 的 loop 首部 leaf 保存真实 GPR／XMM／control，在任何 C++ 参数准备前完成，只将 RSP 归一到 P，并保存／恢复用作临时寄存器的 R11；对照 post frame，仅允许 EAX／EBX 与 INC flags 的实际重放变化。68 次 pre 和 56 次 post 比较通过，后者不再只是读取回调快照或核 EAX／RBX／CF。数据仅覆盖现有 fixture 原调用形态及上述机器状态，不扩大为 AVX／完整 x87／任意原函数。

当前 fixture 的原 SEH 检查为 code、3 个参数及 payload，C++ 检查自身异常类型载荷；没有被动 first-chance 与 outer ExceptionFlags 的记录比对，不能迁用上一批 init fixture 的 flags 结果。实际 callback 内 `RtlCaptureContext` walk 与 post stub 四个合法指令边界展开已有本批实证；pre helper 的任意 partial prolog／body／epilog 和 leaf 每条指令边界尚未列入本批实测。独立 native lane 不验证 managed listener 回调。

## 当前证据与未验收范围

| 证据 | 当前结论 | 仍需完成 |
|---|---|---|
| 实际 header 及关键偏移断言 | ABI 静态计算与最终 helper／fixture 编译一致；公开结构与设计候选有明确区分 | 非当前 driver 的状态转换 |
| Listener cpp／asm、Bootstrap capture／cleanup | 已核 snapshot、tail 与局部配对修正，完整构建中的当前场景运行通过 | 未据此宣称所有保护路径已执行 |
| fixture 六个源区、动态 table、保存与检查 | 自有函数首次调用前发布；实际宽度 5／5／6 与 6／9／9；8349 检查通过 | 列出的 unimplemented 场景及真实生产 publisher |
| 官方栈／epilog／chain 合同与 SameFunction 源码 | 设计依据另有本机 post 四边界与真实 walk 实证 | pre helper 全指令位置与完整异常记录 |

## 冻结输入与暂存产物

[构建前后输入核验](build-input-integrity-check.json)确认构建前清单与[归档源码清单](source-manifest.json)相同；491 项当前文件再次只读逐一计算 SHA256，全部匹配。与上一批相比，[差异记录](changes-from-adopted-broker.json)为新增 7、修改 3、删除 0。新增的是 Listener 三个生产文件、三个 fixture 文件和一个运行脚本；修改 Bootstrap 接线及两个构建脚本。

[源码归档核验](source-archive-check.json)确认 `source-inputs.zip` 的 491 项逐项哈希匹配，ZIP SHA256 为 `46996f6dad1e51188491af8355d205a17dfb723ade79dc73f257f90140144194`，清单 SHA256 为 `de01d24a6963a5ab0418c09c0ef283ced0fcc20b5f0cb4aef62eec2de8fa1e48`。[包归档核验](package-archive-check.json)确认[包清单](package-manifest.json)的 22 项全部匹配，ZIP SHA256 为 `ad6159b3cac76533e78aacdfafca0a13c35794def91bc9698f15b935d1e0f519`。这些文件哈希已重新读取核对；源码归档验证只覆盖声明输入，不包含外部依赖、工具链、干净机器复现或完整 B19 发布矩阵。

最终日志保留 92 条不同编译诊断，来自上游 Doorstop／native 构建；不能称为零警告构建。fixture 的 `/WX` 编译通过，不等于整批没有警告。22 文件包仅暂存并归档，没有安装或启动游戏。

最终 fixture 的名字缩短为 `RtLf`；[实际结构布局](../../../native/RizToolbox.JudgeContext/fixture/ListenerFixture.h)显式增加两处 8 字节 padding 后，callee GPR／XMM／control 偏移仍为 0x28／0xb0／0x1b0，loop GPR／XMM／control 仍为 0x1c0／0x240／0x340。编译产物反汇编核对两个自有函数的 CALL／continuation 宽度均为 5／5／6、6／9／9；原 parent unwind 为保存 5 个非易失寄存器、分配 0x40、零 FP，与边界检查使用的 P+0x68 caller 槽及 P+0x70 返回 RSP 一致。生产四文件与本次定向前审查哈希一致：header `5823be922b408a248b8b26ef03ef4fcd860f7441bf1611f78c5ae6a4107a1698`；cpp `2c5d4acac62a141740eb74c387eca77d59d7ade597a577a2e2fde76e35e9974a`；ASM `378623d1547d07cb307eb228889b063b06aee5800b2d42ce66ff2f2096337990`；Bootstrap cpp `8eaea9131b0c00d2d06f28f66e97ce069fad6cfb90ab752fc2f719f39dc68c64`。

未执行任何 GameAssembly 游戏函数。最终结果明确 `gameRuntimeVerified=false`、`gameCodeExecuted=false`、`firstInstallSafetyVerified=false`、`allFutureListenersClosed=false`，并保留八项未实现／未验分支：动态 vtable 变化、target-read AV、foreign parent mask／容量溢出、missing-post 损坏、完整异常记录、pre helper 全指令展开、全部 delegate leaf、mutation／StopCoroutine／退休。真实游戏六区首写发布、V2 receipt／CLR 采用、managed ledger 与对象持根、实际 invokable／delegate leaf、listener 增删及 future mutation／cancel／资源退休闭环仍未验证。自有 fixture 的真实 native callback 通过也不能扩大成上述结论；B01–B19 不新增验收勾选，RequestPlay 保持关闭，遵从用户“实测稍后”。
