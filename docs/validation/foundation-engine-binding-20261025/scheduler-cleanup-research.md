# 引擎调度、消费移交与 Start 内联清理的实际合同

2026-10-25。**固定 UnityPlayer 的调度责任至少有登记、删除并清理、消费后回调、整体销毁、异步对象和 Start 内联清理六条路径。现有四个 node 外层入口不能单独闭合这些责任。** 本次还发现一个直接影响原函数行为的 ABI 要求：scheduler 按完整 `EAX == 1` 决定是否调用 cleanup，而其 coroutine callback 尾跳 Execute；因此 Execute 必须保留完整 uint32 返回，不能只保证 AL。该新证据用于驱动本批传输修正；上批冻结的 3191/855 项结果仅证明其已声明的 AL 字节范围，不修改旧原件或把它扩写成 EAX 证明。

以下是可直接用于接线的固定机器码事实、有限观察合同及仍未证明的边界。没有执行游戏、映像函数、fixture、构建或安装。本研究完成不代表 B01–B19 完成，不签发 `EngineCancelled`、根释放许可或游戏首次安装凭据。[本批范围](scope.md)与[首次安装研究](engine-installation-research.md)分别限定运行与发布条件。

## E1：固定输入、完整函数范围和复核入口

仅从磁盘读取两个固定 DLL。没有当前可调用的 IDA API；按 ida-reverse 工作流使用已存在的 Python 3.14.0、pefile 2024.8.26、Capstone 5.0.6 提取 PE、`.pdata`、UNWIND_INFO 与实际指令。每个选中入口先归并完整 CHAININFO 组，再分别解码其全部覆盖段，不在第一个 ret 或第一个 RUNTIME_FUNCTION 截断，不把段间空隙当作函数指令。

| 输入 | SHA256 | 范围 |
| --- | --- | --- |
| `D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll` | `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1` | AMD64，ImageBase `180000000`，ImageSize `1E65000`，31,006,120 bytes |
| 同目录 `GameAssembly.dll` | `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` | 仅核对固定游戏侧身份；本研究不调用 IL2CPP |

从仓库根依次运行以下只读提取命令；它们只会重写本目录的研究原件，不启动或映射游戏：

```powershell
C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/research/scheduler-inspect.py
C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/research/scheduler-consumption.py
C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/research/scheduler-xrefs.py
C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/research/scheduler-contract.py
```

| 原件 | 实际内容 | SHA256 |
| --- | --- | --- |
| [scheduler-evidence.json](research/scheduler-evidence.json) | 10 个完整函数组、1236 条指令，Start/Execute/Continue/Detach/Release/登记/删除等 | `64ed08c89d488ab8dba3572c406a848e558e52e946cbe04567353b1db3e9f65f` |
| [scheduler-consumption.json](research/scheduler-consumption.json) | 另 10 个完整函数组、1305 条指令，pump、只删除、整体销毁、yield 变体与辅助函数 | `e4d2f484362fee99ebd37efa0c30e7a94f1d2c6d0c55c5d77d8a8e7581e8ac76` |
| [scheduler-xrefs.json](research/scheduler-xrefs.json) | 26 个调用者函数组；38 个实际指令边界上的直接转移、36 个限定 RIP 引用、2 个非执行节绝对指针 | `d2af8b2f7c47d341d25505e8be51f251b3f3f66fc4d76b758954758ea32c9e95` |
| [scheduler-contract.json](research/scheduler-contract.json) | 对前三份原件的 48 个精确指令点重新断言，并列出接口、字段和未证边界 | `f9ff4d13c1387a8ab6984659357acdb65ecfa2a463e4e04d8393b13e12d8e078` |

便于逐行复核的文本分别为 [主链反汇编](research/scheduler-disassembly.txt)、[消费链反汇编](research/scheduler-consumption-disassembly.txt)与[调用者反汇编](research/scheduler-xref-disassembly.txt)。直接转移扫描只覆盖 E8/E9，RIP 扫描只覆盖指定目标的 REX MOV/LEA；没有把这些结果称作全部间接调用者、别名或写者枚举。原始脚本 SHA256 在末尾列出。

## E2 → F1：scheduler 的实际登记节点和字段

`[44D630,44D756)` 是实际登记函数。其可观察机器 ABI 为 `pointer(tree, outputSlot, sourceRecord)`：RCX 是 manager 内的 tree 地址，RDX 是实际输出槽，R8 是源记录。它从池中取得 `0x60` 字节 tree node，将源记录复制到 node `+20`，按 due time 插树；`44D743` 将实际 tree node 写到输出槽，`44D746` 把该输出槽地址作为 RAX 返回。这里描述实际寄存器合同，不猜测源 C++ 类型名称或隐藏返回参数的源码声明。

| source record 偏移 | 实际类型/用途 | tree node 对应偏移 |
| --- | --- | --- |
| `+00` | double due time 原始位 | `+20` |
| `+08` | int64 frame eligibility | `+28` |
| `+10` | float interval 原始位 | `+30` |
| `+14` | uint8 repeat flag | `+34` |
| `+18` | payload；固定 coroutine 行为 native node | `+38` |
| `+20` | callback；固定 coroutine 为 `78F5E0` | `+40` |
| `+28` | cleanup；固定 coroutine 为 `78F680` | `+48` |
| `+30` | uint32 native owner ID | `+50` |
| `+34` | uint32 category mask | `+54` |
| `+38` | uint32 creation tag | `+58` |

源记录最少需要 `0x3C` 字节；不可把 `+14` 附近的 padding 当成已定义字段。manager 的 `+30` 是 tree 头位置，`+38` 是元素数，`+40` 是 pump 的当前/下一 iterator，`+48` 是递增 tag。`78FC23` 传入 RCX=`manager+30`。固定 manager 全局指针槽为 UnityPlayer `1CB3C00`，其实际运行值未读取。

`[78FA30,78FC47)` 的正常空-yield 路在 `78FB92` 增加 node 引用，填入 `payload=node`、callback=`78F5E0`、cleanup=`78F680`、owner ID、category `0xA` 和当前 tag，再调用上述登记函数。它另有错误记录返回和非空 yield 转入 `78FC50` 的分支；不能把每次进入 `78FA30` 都计为登记成功。

`[78FC50,79014B)` 的实际分支进一步证明登记有多个来源：

| 分支证据 | 对 node 的动作与责任 |
| --- | --- |
| `78FCA7..78FDB9`，类型比较来源是 `8392B0` 返回结构 `+E00` | 读取 yielded object `+10` float，增加 node 引用，按该值加时间登记；`78FDAC` 调 `44D630` |
| `78FDD9..78FE08`，比较来源 `+DF8` | 增加引用，frame eligibility 为 `-1`，category 为 `1`，最后 `790129` 登记 |
| `78FE28..78FE6A`，比较来源 `+DF0` | 增加引用，category 为 `0x20`，使用对应时间表达式，最后 `790129` 登记 |
| `78FE85..78FECE`，比较来源另一结构 `+B8` | 增加父 node 引用，调用真实 Start(owner,yielded,0,out)，非空结果建立双向 `+68/+70` nested 关系并置 `+80`；不经过 tree 登记 |
| `78FEEE..78FF98`，比较来源 `+2E8` | 从 yielded wrapper `+10` 取既有 node。目标 `+64` 已置位时同步调用 `78F5E0(owner,parent)`；否则检查占用并建立 `+68/+70` 关系、增加父引用 |
| `78FFB8..7900A0`，比较来源 `+1E8` | 可以转入异步对象的 callback/cleanup 字段登记，见 F5；不是 tree 节点 |
| 其余/fallback `7900A5..790129` | 增加引用并登记 category `0xA` |

类型语义名称没有通过活体 metadata 解析，表中保留实际分支/偏移；延时、固定帧等名称只能作为这些机器码行为的解释。完整控制流和调用点均在原件，没有凭常见 Unity 类名补齐未证映射。

## E3 → F2：删除、正常消费和整体销毁是三种不同责任

`[447AD0,447C26)` 的实际输入是 RCX=`manager`、RDX=`record`、R8=`tree node`，固定调用中 `record=tree node+20`。它在 `447AE1/447AE5` 先缓存 cleanup 和 payload，按是否命中当前 iterator 走两个分支，分别在 `447B33/447BF5` 调 tree erase `3F01B0`，并把回收内存推回池链；最后 `447C19` 才调用缓存的 `cleanup(payload)`。因此入口必须先复制全部标量；原函数正常返回后不得再读取原 tree node、record 或可能已由 cleanup 释放的 coroutine node。已知调用者忽略返回值；本研究不发明其更宽的源码返回类型。

`[447C30,447D60)` 则只 erase/recycle，不调 cleanup。实际使用 RCX=`manager`、R8=`tree node`；RDX 进入后被覆盖为 manager，不具有已证有效参数语义，必须保留其实际不透明输入。单凭它返回不能注销该记录全部责任，必须将责任从“可被调度的行”转到“已消费、尚待回调/清理”的物理调用。

正常 pump `[447D60,447FAF)` 的完整分支如下：

1. 增加 manager tag，取时间/帧快照，从 tree 遍历 due time 到期的行。category mask 不匹配、creation tag 等于当前 tag、frame eligibility 尚未满足时跳过；不能只按时间判断可执行。
2. 根据记录 owner ID 取得 native owner。没有 owner 时调 `447AD0` 删除并清理。
3. 非 repeat 行先缓存 payload/callback/cleanup，`447EDF` 调 `447C30` 删除行，`447EEA` 再调 `callback(owner,payload)`。**tree 已空和 callback 尚未执行可以同时成立。**
4. callback 正常返回且完整 `EAX==1` 时，`447EFA` 才调缓存的 cleanup；其他返回不在此处调用。callback 自身可以递归注册、执行或最后清理 node，必须保持物理父子关系和已消费责任。
5. repeat 行先更新时间/帧字段，`447F3B` 用旧记录复制注册一行，再 `447F46` 删除旧行，最后 `447F51` 调 callback。它不走上述 `EAX==1` 条件 cleanup。固定 coroutine 生产点的 repeat 是 0，但通用登记入口不能据此忽略该分支；复制行也不能自动推断额外增加了一次引擎 node 引用。

整体销毁 `[447750,4478AC)` 又绕过上述两个删除函数。它循环最前行；category 含 `0x40` 且能找到 owner 时先调用该行 callback，然后直接 `447840→3F01B0` erase/recycle，`447864` 无条件调非空 cleanup，直到元素数为零。固定 coroutine 类别 `1/0xA/0x20` 不含 `0x40`，但其 cleanup 仍走这条整体排空路径。只挂登记和 `447AD0` 会漏掉正常消费和整体销毁。

`scheduler-xrefs.json` 在非执行节还保留 `1982B60→447750`、`1982C10→447D60` 的两个实际指针。它们支持存在间接入口，不能由直接调用点少推断没有调用；本稿没有通过运行时对象证明全部虚表实例。

## E4 → F3：完整 EAX 是本批必须修正的实际 ABI

`447EF2` 的字节为 `83F801`，指令是 **`cmp eax,1`**，不是 `cmp al,1`。固定 coroutine callback `[78F5E0,78F673)` 接收 RCX=`native owner`、RDX=`native node`；先比较 node `+58` 是否同一 owner，不同则记录错误并 `mov eax,1` 返回。相同则将 RDX 置零、RCX 设 node，在 `78F66E` 尾跳 Execute `78F840`。

Execute 所有选中正常返回最终汇合到两种明确赋值：`78FA01: xor eax,eax` 或 `78FA27: mov eax,1`，随后共同 epilogue。其 machine ABI 必须视为 **uint32 Execute(node, optionalErrorByteOutput)**，固定 callback 也是 uint32 callback(owner,node)。只声明 byte，编译器只需保证 AL，EAX 高 24 位不属于该声明的返回义务；AL=1 而 EAX≠1 会改变 pump 的 cleanup 分支。不能用“实际原函数一般返回 0/1”替换传输层对完整值的保证。

本发现驱动本批 Execute 的 native/CLR ABI 及真实目标高位测试修正。是否已通过修正版构建，必须引用本批独立构建证据；这份只读静态研究不代替测试终态。上批 [3191 次 native 检查](../foundation-engine-node-20261025/engine-node-native-evidence.json)明确 `fullRaxPreservationVerified=false`，且原声明为 byte；[855 次 CLR 联合检查](../foundation-engine-node-20261025/engine-node-managed-evidence.json)也没有验证这一 EAX 消费语义。保留旧证据的有限范围，不改旧文件。

## E5 → F4：Start 的同步最终清理不经过 Release 外层

Start `[792240,7929BF)` 写入 owner、enum script reference，初始引用计数为 1，并在 `7924BA` 同步执行 Execute。返回后 `7924C3` 将当前计数与 1 比较：大于 1 时调用 `7925A5→78F680`，其余进入 `7924CD` 的内联最终清理。健康的已知候选应要求这里的 Before 计数为 1；实际指令也允许异常的 0/负值进入，不得因此当作合法完成。

内联分支直接执行 `dec [node+60]`、写 `+64=1`、递归 Release `+68`、拆 `+68/+70`、摘 owner list、清 node `+78` 的异步 callback/cleanup/payload/owner ID 并归还共享引用、释放 enum script handle并清 `+10/+18`。若无 wrapper handle，`79256D` 释放 `0x88` native node；否则 `792582..79258E` 释放并清 wrapper handle。两路分别在 `792576/792596` 写实际 Start 输出槽为 null，AL 由同步执行错误 byte 是否为零产生。

这说明 Start 的 null 输出、AL=1、未见外层 Release 可以是正常同步结束；它既不是未知取消请求的原因证明，也不能证明此前所有生产来源已关闭。相反，已核对的内联分支可以直接产出局部“本 node 最终清理分支已正常走完”事实。

可实现的专用内部观察点为 `7924CD` 分支入口，以及 `79257D/79259D` 两个已执行完清理与输出 null 写入、即将跳至公共 epilogue 的出口。入口时 RBX 是实际 node、R12 是实际输出槽；出口只使用入口保存的身份，**不能读 RBX 指向的内存**。它们是函数内部机器码协议，不是新 C 函数签名；需要独立固定字节、寄存器/标志保存、重放和 unwind 证明，不能用普通外层 detour 或凭 Start 返回猜出中间分支。任一内部原异常、读故障、未配对或 Start 外层尚未退出时继续保留物理责任和 Unknown/Pending。

## E6 → F5：异步对象是独立生产来源，普通派发尚未闭合

`78FFDD` 增加 node 引用后调用 yielded native object 的 vtable `+8` 检查完成情况。非完成对象且 callback 槽未占用时，`790069..79009C` 直接写入以下字段，**没有调用 tree 登记函数**：

| 对象字段 | 实际写入/动作 |
| --- | --- |
| `+10` | callback=`78F5E0` |
| `+18` | cleanup=`78F680` |
| `+20` | payload=当前 node |
| `+28` | native owner ID |
| `+0C` | 安装后 `lock inc` 的共享引用计数 |
| node `+78` | 此异步对象指针；替换旧对象前调用 `342400(old+8)` 归还共享引用 |

vtable `+8` 的 isDone 用途得到 `UnityEngine.AsyncOperation::get_isDone` 实际 icall `[F7790,F7813)` 的相同调用支持；`InternalDestroy [F7750,F7785)` 归还 object `+8` 的共享引用。这两项名称和目标由固定注册表 index 2218/2219 核对并保存在消费原件，而非仅凭字符串命名。`342400` 自身只保证共享计数和最后一次调用对象虚析构，不等于 node 的取消完成。

Detach 的两个 nested/non-nested 分支在 `7938B3/793983` 只把这类对象 `+28` owner ID 清零；callback、cleanup 和 payload 并未在此清零。已进入某个派发器并缓存这些字段的调用，也不能因为 owner ID 之后为零就被认为不存在。node 的外层 Release 和 Start 内联清理才会在其最后分支清全部字段并归还对象引用；仍需分开观察撤销与在途派发排空。

补充原件保留了 `[5DA510,5DA6DB)` 中 `5DA630..5DA65B` 的同字段形状批量清理：若 object `+10` 非空，则以 `+20` 为参数调用 `+18`，将 `+10` 置零，再归还 object `+8` 共享引用。**目前没有证明这个容器中的每个对象与上述 yielded object 的实际生产来源及普通派发是一一对应关系。** 它仅是可继续定位的真实字段/调用证据，不作为已闭合的 AsyncOperation 析构入口。普通异步完成如何取得 owner、缓存并调用 callback、何时调用 cleanup、其他完成/取消来源以及并发排空，本稿均未证明，合同保持 `asyncOrdinaryDispatchProven=false`。

## P1：能直接实现的最小观察合同与不得删除的责任

下列是覆盖**已发现路径**所必需的接线集合，不是宣称这些入口已足够覆盖所有引擎取消。每个 native 事件先登记物理责任，再复制有限标量；复制失败也必须调用原函数、完成清理配对并保 Unknown。CLR 只接收复制结果，不从 tree/record/node/异步裸地址读对象。

| lane | 实际 ABI/阶段 | 记录和移交要求 |
| --- | --- | --- |
| Register `44D630` | RCX tree、RDX out slot、R8 source record；RAX=同 out slot | Before 复制源 `0x3C` 已定义字段；正常 After 只读一次实际输出槽取 tree node，赋单独 record 代次。callback/cleanup/owner/node 均须匹配，不以地址独立认领 |
| Erase+cleanup `447AD0` | RCX manager、RDX record、R8 tree node | Before 复制 record 并核 record=`node+20`；After 只用快照。删树与 cleanup 是顺序不同的两个事实，cleanup 原异常不能算已完成 |
| Consume `447C30` | RCX manager、R8 tree node，RDX 不透明 | 在原 erase 前把行转为待 callback 的物理责任；不能在返回时清空所有责任 |
| Pump `447D60` | RCX manager、EDX category mask | 把实际被选中的 record、当前 pump/frame 和后续 `78F5E0`/cleanup 连接；跳过/回调/复制重登分别计。删除前缓存与 callback 之间的窗口也计在途 |
| Drain/destroy `447750` | RCX manager | 逐行观察实际直接 erase 与 cleanup；不能只在函数返回用元素数为零作证明；已有在途 callback 仍保留 |
| Node callback `78F5E0` | **uint32**(native owner,node) | 验实际 Before owner/node；无论 owner mismatch、尾跳 Execute、原异常都配对；After 不读可能已 free 的 node，保完整 EAX |
| Execute `78F840` | **uint32**(node,error-byte output) | 保完整 EAX；执行窗口独立计数。脚本/嵌套产生的新 Register 与 AsyncInstall 归属于真实物理父链，不能以当前引用计数代替窗口 |
| Start inline | `7924CD` → `79257D/79259D` 内部协议 | 入口复制 node 及分支身份；正常出口产生本地最终清理事实；仍等外层 Start/Execute 排空，不回读已 free node |
| Async install/revoke/dispatch | 安装 `790069..79009C`；撤销是实际清字段/owner ID 的阶段 | 单独记录对象代次、callback/payload/node 与字段改变。普通 dispatch 精确入口未证，不能通过设计一个假 callback 代替；该 lane 未闭合时所有相关候选继续 Pending |

已知直接调用者不消费 Erase/Pump/Drain 的 RAX，源层确切返回类型仍未证；后续 wrapper 应以完整 native passthrough 保存实际被消费的寄存器，不把这里的“忽略”扩展为未经验证的通用 ABI。内部 lane 必须另外满足现有安全发布合同，不在游戏运行后临时 Dobby 首次写入这些地址。

对每个 exact node 代次，至少保留 `tree registrations`、`consumed pending callbacks`、`active callbacks/executes`、`async producers/dispatches`、`nested references`、`final cleanup branch` 和 `outer physical calls` 七类责任。最后清理、Stop 原因、来源关闭、消费者归还仍是分开的事实。只有它们全部有实际配对、无新登记、无异常/overflow/foreign 身份，并结合 run/tracker/future 的实际封口后，才有资格设计取消完成凭据；当前仍然 `EngineCancelled=false`。

## 复核结论和明确未证项

F1–F5 是固定映像上的静态 validated 结论，置信度高，证据分别来自 E2–E6 的完整机器码和 48 个精确断言；P1 是基于它们的可实施协议，尚未运行实现。没有证明全部间接调用者、全部 tree/scheduler 实例及销毁路径、所有 yield 自定义类型、所有 node/async 字段写者、异步普通派发、真实 IL2CPP 对象/GC、多个标量读取的并发一致性、任意合法 PC 的异常透明性或首次发布安全。不能因几条已核分支覆盖完整函数，就宣布所有可能的未来执行来源已经收齐。

| 研究脚本 | SHA256 |
| --- | --- |
| [scheduler-inspect.py](research/scheduler-inspect.py) | `6eaf01729b13e0b74b4ae93f91cbc220a3f2f8d50e4063fc20fbfd60cdde6faf` |
| [scheduler-consumption.py](research/scheduler-consumption.py) | `f08bec8ad085920f7c2188e639cab29109719ebfd6df465f02754cf335b4613e` |
| [scheduler-xrefs.py](research/scheduler-xrefs.py) | `29f923fe050b0d5df77699b6325fb701f679c5c126d8a9a904f92efce4e587ef` |
| [scheduler-contract.py](research/scheduler-contract.py) | `625433ae653ed27bafa3e2b2b18a9e9efd031ecb0113baa418d47228f5b127bc` |
