# 新Kernel早期链验证器的适配边界

2026-10-25。本文是wrapper作者的接口说明及离线自查，**不是整批正式通过报告**。首次604输入构建session31803真实退出1，72个非debug用例通过，debugger在producer进入前拒绝重复publisher映射；原始失败必须保留。root随后实际诊断，最终metadata版session69351的3个自有child均退出0。本文作者只读取这些原件进行离线验证，没有构建、加载DLL、运行child、安装或启动游戏，也未修改两份历史验证器。正式整批重跑仍由root完成。

## 固定两个历史实现，保留原断言

新入口为[verify-early-kernel.py](verify-early-kernel.py)。它读取两个精确SHA256固定的历史模块，经AST生成**仅在内存中**的Kernel profile：

| 实际历史文件 | 固定SHA256 | 适配内容 |
|---|---|---|
| [早期链验证器](../foundation-early-fixed-20261025/verify-early-fixed.py) | `056b494994c86558444d873da2a841c99fd31cd82297884fa41b288f20730d64` | exact模块/结果kind名、magic、五处wire-version表达式；debugger函数由新wrapper独立实现 |
| [固定publisher验证器](../foundation-fixed-publisher-20261025/verify-fixed-publisher.py) | `2144013f0dfbd114dec164b43c2fac28a083f2ca3bd11e120a5deab2775b27d7` | exact publisher/helper文件名、结果kind、Info/Receipt两个version比较 |

遇到未知文件hash，尚未执行其模块就拒绝；不是将任意文件的文本全局替换后运行。每个版本转换按函数名和完整AST表达式匹配、要求恰好命中一次；magic恰好转换一次。字符串只能匹配已列清单的**完整字面量**，不匹配子串，不改广义数字。早期模块原有四语句import bootstrap被精确替换为已验hash、同样受profile适配的固定模块；没有删原断言函数。parent须将新wrapper、两份实际历史验证器以及profile头文件全部列入冻结输入并持有只读句柄，未知传递输入不能沿用旧证明。

最终`load_all()`静态载入产生fixed6、early40项记录，全部变换带原文件行、前后表达式/字符串与adapted AST SHA。原先仅为旧debugger改写的processAttach规则已移除，因为新debugger需要独立核验实际生命周期和硬件返回记录；没有保留无效的第41项。静态载入本身**不说明PE、mapping、73child或原生链已执行通过**。`verify_workload`及`exception_records`在适配前后整个AST强制相等；所有Start嵌套、四kind返回/实参、逐线程LIFO、原异常与typed catch、真实DEC/ZF、双向配对、baseline和独立实际内存规则继续调用旧实现。其余`verify_mapping`及fixed receipt路径仅受已列身份/版本表达式修改，未重写故障逻辑。

## 四个版本各自有含义

| 协议或对象 | Kernel profile实际要求 | 保留的不变条件 |
|---|---:|---|
| MMF magic | `0x4b465245` | 总449120、Header360/tuple216/event104等offset不改 |
| Header/config/tuple/SnapshotMeta wire | 3 | mode0..15、56内部fault、16模式+debug矩阵与事件IDs不改 |
| publisher Config/Binding/Info/Receipt | 2 | 结构不改，21640 Info/272 Receipt，固定四入口 |
| helper transport ABI | 2 | Start/Release uint8、Execute uint32、Detach void |
| helper scalar Frame/Completion/Info | 1 | 112/32/112字节，slot28，独立epoch |
| ActualMemory | 1 | 240字节、系统pageSize、每目标实际32源字节/VirtualQuery保护 |

wrapper没有把所有2改3或所有1改2。旧wire2不能过新`decode_mapping`，旧Info/Receipt1不能过新fixed decoder；初始化器写wire3但expectedHelperAbi仍2。实际copy-before/after、永久Unknown、API实调计数、partial1byte与共享页保护责任全部沿旧约束。新proof/result种类分别为`owned-early-kernel-pe-import-check`、`owned-kernel-fixed-publisher-pe-proof`、`owned-early-kernel-shared-log-verification`，并保存profileMetadata及两份传递SHA；该元数据只防混用受控fixture证据，不是生产授权凭据。

**零tuple不等于丢版本检查。** 部分早期拒绝或mode14/mode15发生在InitTargets之前，此时整个216字节tuple应为零，不能要求尚未初始化的版本字段为3。新wrapper对所有数字字段全0且三块hex全0的row允许未初始化；只要任一标量/字节不为0，就必须version3/size216/实际kind/slot28全部正确。旧version2但非空的tuple必定拒绝。实际publisher Info/Memory/Snapshot、零源写/Unknown及禁止Importer等独立检查仍必须成功，不能仅因tuple空跳过整个责任审查。

## 新Kernel PE与debugger语义

PE依旧走历史完整普通依赖/固定前缀与unwind形状检查，再追加新Kernel严格合同：准确`RizToolbox.EngineNodeKernel.dll`名和hash、AMD64 DLL、入口0、TLS/load-config/delay/CLR目录0、恰8个nonforwarded且无隐藏ordinal-only导出、普通导入仅`Kernel32!RaiseFailFastException`与`ntdll!__C_specific_handler`。准确新publisher/proxy/recorder/host文件名与普通依赖边同样固定；不接受把旧JudgeContext改名视为Kernel。

新wrapper只替换内存中模块的`verify_debugger`函数，并在该模块新增1006/1007/1008事件名；不删除或过滤原始JSON/shared行，也不改历史默认profile。每条JSON的sequence/type/PID/TID/role/base与共享row相同，按事件类型核全部共享payload。未知类型、重复JSON键、缺字段、旧metadata和行数不一致均拒绝。仅真实1002/1003/1004/1005标`processAttach=true`；生命周期及诊断为false。每行`noInitializationRequired`严格等于role是否为1/2。

type1001 Load与1006 Unload的共享identity分别记录generation、instanceId、真实映像SizeOfImage、volumeSerial/fileIndex及sourceLoadSequence。**新profile的value0是volumeSerial，不再是初始化理由。** parent在同一只读文件句柄调用64位HANDLE签名的`GetFileInformationByHandle`，同时重读并核PE哈希/长度，取前后相同identity写入PE proof。debugger的actual及self-expected identity都必须等于这份独立parent证据。每role最多8代、全表最多32个实例；同一个live base不能再次Load。只有无entry/TLS的publisher id1和Kernel id2可有多实例，所有非选实例须实际Unload且在ProxyAttachBegin之前结束；最终五个role各恰一live实例，其base必须等于MMF相应header。role3/4/5仍仅一个实例，实际entry/TLS RVA多重集严格等于held PE，不能借多映射跳过初始化。

1007是**已退休实例收到的重复卸载通知**，不创建或结束第二个生命周期。它必须指向此前已1006关闭且同base历史唯一的实例、当时无live映像；JSON与shared须逐项保真实`VirtualQueryEx`结果，查询覆盖该base且不再为原AllocationBase的MEM_COMMIT/MEM_IMAGE。root三份最终诊断均未命中此分支，结果`retiredUnloadNotifications=0`、`retiredUnloadNotificationsObserved=false`；这是校验合同，不能声称原生分支已覆盖。未观察的系统模块卸载只在stderr，不能由五role证明推导完整OS生命周期闭包。

初始化入口1002/1004与真返回1003/1005的shared index是硬件slot，entry字段是module instanceId，epoch是expectedReturnRsp，value0是实际事件RSP。JSON的before/after均为实际`GetThreadContext`标量，after来自Set之后重新读取。parent根据逐线程pending表独立重算所有DR0..3、DR7 local execute enables、最先空slot及expectedRsp=entryRsp+8；真返回必须同thread/slot/PC/instance/expectedRsp，DLL返回完整RAX低32位为1，实际after槽已清，pending最终为空。保留原PE入口/TLS计数、Recorder返回早于producer，以及ProxyAttachBegin < Ready < Proxy真实返回 < TargetsTLS进入。

1008是同pending thread/PC命中但RSP不同的实际硬件事件，不能当作真返回、不能清槽或丢pending。JSON/shared须同一slot、instance、预期与实际RSP和真实DR6/DR7；before只有所属PC的owned B0..3命中，BD/BS/BT无外来原因；after保PC/RSP/槽并真实RF置位，随后必须发生同pending的精确RSP真返回。parent重算`slotArmed/slotCleared/resumeFlagObserved`，不只信布尔字段。Windows会规范化DR6保留/reset位及DR7固定bit10，因此不把它们强制成源码请求值；仍严格核受控B0..3/BD/BS/BT、全部槽/控制、RF及有意义EFLAGS。EFLAGS仅允许请求RF和实际已见OS fixed bit1规范化，不能改变DF等标志。

“内核无需初始化”来自实际PE空entry/TLS目录，非只信JSON。输出明确`noInitializationRequired=true`、`actualKernelLoadBeforeProxyVerified=true`、`zeroSyntheticKernelInitializationRowsVerified=true`，没有虚构helper初始化返回字段。Load/硬件断点属于额外自有debugger child仪器化证据；主模式无debugger。`completeOsLoaderLifetimeClosureVerified=false`，不能据此签全部OS依赖、所有错误分支或游戏loader安全。

## 本次已实际执行的离线核验

原件是root session69351产生的[D:/RizToolbox-build-cache/early-kernel-final-preflight-20261025](D:/RizToolbox-build-cache/early-kernel-final-preflight-20261025)，三个child已经由root实际退出0；本文作者没有再次运行它们。新wrapper读取每份真实mapping/JSONL，沿固定实现复核整个mapping、workload、receipt、实际页和跨child异常baseline。各份实际2457项父断言通过；各有18个debugger行、6次Load、1次Unload、4对真实entry/return和3条1008；1007均0。完整离线输出在[offline-verifier/offline-summary.json](D:/RizToolbox-build-cache/early-kernel-final-preflight-20261025/offline-verifier/offline-summary.json)，不替代即将冻结后的整批运行。

另对真实第二份raw的副本做14个**仅离线校验器负例**，包括同时篡改JSON/shared后的wrong file ID、header选择base、真实返回RSP、丢Unload/return、未清slot、错栈未RF、把精确栈伪标错栈、外来DR7/BS、缺实际readback字段、未知事件、改变DF、JSON/shared行丢失。14项全部被拒绝，输出[negative-mutations/results.json](D:/RizToolbox-build-cache/early-kernel-final-preflight-20261025/offline-verifier/negative-mutations/results.json)。这些是受控输入变异，不是native故障实测，也没有增加child数量或补足1007原生覆盖。

## baseline和证据层次

mode14仍须先作为未发布独立child：相同Targets binary/RVA、真实TLS/C/C++/DllMain之后四类SEH/四类C++，零Prepare/Configure/Bind/Commit/Ready/Attach。新baseline proof须同profileMetadata双hash，成功mode0/3/debug后续按kind/异常类别与同binary baseline精确比较raw code/flags/参数数量/payload/kind；局部callId/对象与模块指针各自核，不要求跨child相同。不mask flags，不改成twin/direct body基线。

mode15保留真实受控Capture递归冲突：event65 result=outer1/value0=inside2/value1=later1/epoch3，最终CaptureCalls4/flags7/Unknown/failure22，并禁止Proxy/Importer/写操作。其他mode必须flags3；stage12/13/14仍是**强制API结果**，不能冒充自然OS内部失败。partial仍实际1byte、独立实际源页及同页Outstanding保护规则继续强制。

正式运行证明必须来自root实际child退出、完整原始mapping/JSONL/进程记录/held hash及归档。当前文档只说明可核的适配规则和有限代码自查，没有生成新profile成功结果。基于受控JSON生成的proof也不是密码签名；parent对实际binary/source/hash的只读持有及本地原件来源不可省略。游戏双映像、WinHTTP/V4、实际coordinator/GC、scheduler/future与取消/退休闭包均继续缺失，EngineCancelled=false，Pending保留，完整B01–B19未因验证器准备而勾选。
