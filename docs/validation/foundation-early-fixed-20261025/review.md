# 固定发布器与早期宿主集成审查

2026-10-25。**session 34351 已实际 exit 0，73/73 自有子进程通过；本次有限审查未发现阻断该自有集成交付的缺陷。** 固定发布器已经在这批宿主中真实运行，四入口发布早于目标 DLL 的初始化和首次目标调用。此结论只覆盖本批自有进程，不授予游戏首次安装、生产 V4 或完整底层验收。

## 证据与复核方法

审查者编写了本批 Importer、Host、ImporterApi.h，对它们的检查属于作者自检；对其他代理的 Proxy、Recorder、构建器、Debugger、父 runner 和验证器作有限交叉审查。没有构建、执行子进程、安装、启动游戏或操作 UI。完整范围见 [scope](scope.md)。

| 正式原件 | 本审查实际只读核对 |
| --- | --- |
| [终态](component-run.json)、[wrapper](component.log.run.json)、[摘要](build-summary.json)、[构建日志](build.log)、[测试日志](test.log) | session 34351、exit 0；两日志 SHA256 与 root 终态记录、wrapper 一致。构建 `436b23250ba01280bf9c965542c611cd65e79a3bf3cfd216a9710840090371a5`，测试 `e526f4bdedf1511358adeea7beb711350362816d77f6b7a7917ac1b624059042` |
| [源码 manifest](source-manifest.json)、[预冻结](component-inputs-before.json)、[归档核对](source-archive-check.json)、[输入核对](component-input-integrity-check.json) | 14 项在预冻结、当前文件、ZIP 中逐项 hash 和精确文件名集合一致，testzip 无损坏；ZIP SHA256 `a455676e512ac075ed7cd85c13d3c766d26d6ef47e103912cccfee1d9d7a0245` |
| [二进制 manifest](binary-manifest.json)、[归档核对](binary-archive-check.json) | 7 文件 ZIP 的精确名称、逐项 hash、完整性均通过；父持有的七文件前后 hash 未变。ZIP SHA256 `477bf08fc920ddb89735f22aa3e0ebb4bacf7af517b0a55f32f3d3418cc4c50e` |
| [正式汇总](early-fixed-evidence.json)、[原件树汇总](raw/cases/fixture-result.json) | 两份 JSON 完全一致：73 子进程、13031 条独立父断言；16 个模式、56 个内部故障、1 个额外 debugger 样例 |
| [PE 依赖原件](raw/cases/pe-imports.json)与每进程原始 mapping | 普通静态链、目标非 NOP 前缀、无 Dobby 导入及具体 ABI 单列；73 份 mapping 逐 104 字节独立解码，共 8924 行，与正式 JSON 每字段一致 |
| 追加的实际页快照 | 按原始 mapping 的 240 字节区域，逐项重算 73×4=292 个目标的实际 32 字节、地址、AllocationBase 和共享页保护；全部一致。另按真实事件顺序重算 196 个 Release/Detach 诊断与对应 Before 引用计数 |

本批独立声明的 14 源／7 二进制没有代替之前的统一构建。前批 session 99557 的 25 套 1617 回归、576 源／22 包仍是其自己的冻结里程碑；本批没有重跑那些完整托管套件。

## 已证实的集成行为

**真实依赖和初始化顺序。** 普通链是 `Host → RizFixedOwnedTargets.dll → Proxy → 固定 publisher/helper/Recorder`，Recorder 也普通静态依赖 publisher。publisher 的 PE entry 和 TLS 都为空，不制造“其初始化已返回”的事件。Proxy 只定位并发布已映射的目标代码，没有在目标 DLL 初始化前调用它的 C++ body。实际 PrepareGroup、四次 helper Configure、BindGroup、四次 Commit 和回读完成后，才发布本 fixture 的 Ready。

[normal](raw/cases/mode-0/result.json) 的 Ready 序号 38，早于目标 TLS 初始化进入 39，随后真实执行 TLS、C 初始化器、C++ 初始化器和 DllMain。四阶段均执行四个直接目标；Start 中再同步执行实际 Execute/Release，而非只记录预期事件。

[debugger 补充原件](raw/cases/normal-debugger/result.json)有 17 个仪器化观察：helper TLS 在 9–10、实际 DLL entry 在 11–12 返回，Proxy entry 在 13 进入；Ready 50 后，Proxy 在 51 返回，目标实际 TLS callback 在 52–79 执行。目标 DLL entry 的 80–159 覆盖 C 初始化器 81–106、C++ 初始化器 107–132 和 DllMain 133–158。publisher 仅有真实 DLL load，没有伪造 entry/TLS。该样例用临时内存断点，不能当无扰动、所有线程/系统版本或游戏启动证明；主模式没有这些断点。

**实调、嵌套和返回宽度。** normal 的 2162 条父断言核实 86 次原调用，分 kind 为 `[15,28,28,15]`；[worker 模式](raw/cases/mode-3/result.json)的 2262 条父断言核实 92 次，分 kind 为 `[16,30,30,16]`，包含两个真实 worker。两者各有 56 对观察，分 kind 为 `[10,18,18,10]`；每对绑定同一实际调用、实参、序号、token、线程和物理 LIFO。Execute 的完整返回为 `0xa5c3fe81`，没有截成 AL。

Release 的真实前缀执行一次 DEC，continuation 第一条 SETZ 消费其比较标志。phase 9 从引用计数 2 开始：Start 内的 Release 减至 1，直接 Release 从 1 减至 0，原件记录 ZF=1；其他阶段也逐事件核对正常减计数。Detach 记录清零后的 EBP 诊断，原 ASM 恢复调用者 RBP。这里只使用自有、持续存活的 node 存储；计数为零和 ZF=1 **不代表游戏节点已释放或取消完成**。

73 进程的原件合计 392 次原调用、168 对观察、32 次异常捕获；包含 baseline 的 68 次和 debugger 的 86 次，不能再次重复相加。worker 共开始 5 次、完成 4 次；差额来自刻意保留的迟到安装 worker，不是漏记的成功退休。

## 异常基线与失败责任

**基线是真正未发布的独立 child。** [mode 14](raw/cases/mode-14/result.json) 的 1145 条父断言核实 68 次原调用、8 次捕获、0 观察、0 写入和无 Ready。它使用相同 Targets 二进制和 RVA，经历真实四阶段初始化，再对实际四入口运行 SEH/C++；没有 twin、direct-body 替代，也没有 Prepare/Configure/Bind/Commit。

原始 SEH flags 为 `0x80`，C++ 原始记录 flags 为 `0x81`。normal、worker 和 debugger 各自四类 SEH、四类 C++ 的 code、原始 flags、参数数量及稳定 payload/kind，与该 baseline 逐项相等，未 mask 或归零。SEH 的实际 callId 按本 child 核验；C++ 的对象/类型/模块地址按本 child 保留，并另有实际 typed catch 的 payload/kind/callId 记录。比较是同二进制、同系统的跨 child 证据，不称同对象、同地址或同一运行。

**故障后没有放行初始化。** 56 个内部故障在每 kind 的保护、写、回读、恢复和刷新边界产生实际责任；这 56 个失败 child 都未进入目标初始化/原调用。partial-write 在本批实际写 **1 字节**，与上一 standalone 组件的 3 字节样例分开；实际页快照核对写入前缀和未动后缀。恢复失败的 outstanding 会影响同一系统页上的其他目标，父按实测 pageSize 验证所有相关保护，未用单 tuple 的值掩盖同页影响。API failure 的 stage 12/13/14 是跳过相应 API 的受控结果注入，明确保留 `ApiCalled`/attempts 的区别，不能称 OS API 真正内部失败。

[迟到 mode 2](raw/cases/mode-2/result.json)在真实额外 worker 完成四目标调用后仍保持该线程阻塞，再尝试安装，因已有历史拒绝，零写入；没有伪称这个 worker 已完成。部分发布失败保留永久准备/配置/绑定、实际已写字节和 Unknown，不回滚、重试、卸载或继续授 Ready。

[Capture Busy 原件](raw/cases/mode-15/result.json)为明确受控递归：外层已取得原子 gate，内层真实返回 2；外层和后续 Capture 返回 1。最终保留 flags=7、4 次实际 Capture、Unknown、failure 22、零写入且 Proxy/Importer 未进入。该证据是实际递归竞争，不等于自然多线程调度矩阵。

**审查误报与更正。** 我先前把读取的中间版本 Event 65 字段顺序当成最终冻结合同，错误建议交换父断言；root 依据冻结源码与原件纠正，runtime 恢复对应断言。最终 Recorder SHA256 为 `da317e8195b5a08cd36ca8751356b5d7e1ab156a8e52a44d4c9b6f36347a0cd8`：实际 `result=outer 1、value0=inside 2、value1=later 1、epoch=3`，最终 meta.captureCalls=4。本审查已经独立从 raw mapping 核实这些字段，撤回反序结论；不能把这次审查往返描述成原生功能通过了一次不存在的失败/修复。

## 剩余路径

wrong-hash、architecture、prefix、ABI、missing-dependency 和 dependency-not-ready 中若干条件是 expected/selector 改变，保留各自真实拒绝范围；不是实际替换坏模块、缺失静态 DLL或调用未初始化 helper 的全矩阵。无 CRT/Dobby/堆/迟到加载的组件导入表，只是必要条件；全部 helper/系统传递依赖、静态或间接调用和所有错误路径安全仍未闭合。

固定 original 继续限定四类 typed ABI，允许 volatile R11 改变，不声称 full-GPR 或任意 flags 透明。本批采用已冻结组件的静态展开合同，未重跑上一批 50 个构造 CONTEXT 的逐 PC 矩阵，更没有扩展到所有 target epilog 或每 PC 的实际异常；真实正常/worker/嵌套/异常调用证据单列。

后续仍需真实系统 WinHTTP 名称/转发/依赖放置、固定 UnityPlayer 最早调用历史和生产首次发布凭据，再接 coordinator 的真实对象/GC、scheduler/async/Start 内联清理、全部 writer/future/消费者及整局资源退休。本批未启动游戏、未安装包，未签发 V4，`EngineCancelled=false`、Pending 保留、RequestPlay 关闭；完整 B01–B19 不缩小，正式验收仍仅 B03 整项与 B18a 子项。
