# 三处 invokable 调用：六区原生运输设计

2026-10-24，只读设计。依据[上一批 listener 下一步](../foundation-clr-preinit-20261024/listener-next-step.md)与当前 BootstrapVoid/Mid 源码；没有修改原生运输或游戏，没有构建和发布六区补丁。本批先做共用 broker adoption core 与自有 fixture 的真实 CLR 联合验证；本文给下一批实现边界，不能记作已验证能力。

可行路线：保留原调用的硬件返回地址，pre 使用真实 CALL→完整保存→观察→尾跳原 callee；post 使用原 continuation 的 E9→不额外分配栈的近端片段→真实 CALL 观察 helper→完整重放 continuation。post 片段以动态 chained unwind 描述它仍拥有的原 UnityEvent frame。普通 C++ 再 CALL 原 callee 会改变它实际看到的返回地址，不能代用。

## 固定六区与源返回地址

输入仍为固定 GameAssembly SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；函数 `[0x15642E0,0x156454E)`、unwind `0x1DB3FB4`。下面的地址为 RVA，所有发布前还须以实际 ASLR 映像实例核对。

| 区域 | 原指令/长度 | 候选替换与必须保持的事实 |
|---|---|---|
| pre 0 | `1564470: E8 3B A1 FE FF`，5字节 | 5字节 CALL 近端 pre relay；callee 真 `_ReturnAddress()` 仍 `1564475` |
| pre 1 | `15644C2: E8 E9 A0 FE FF`，5字节 | 5字节 CALL；callee 真返回仍 `15644C7` |
| pre 2 | `156451F: FF 90 78 01 00 00`，6字节 | **`90 E8 rel32`**，CALL 从 site+1 开始；callee 真返回仍 **`1564525`**；不是 `E8 rel32 90` |
| post 0 | `1564475: FF C3 8B C3 EB A5`，6字节 | 覆盖完整块为 E9+剩余填充；stub 重放 `inc ebx; mov eax,ebx; jmp 1564420` |
| post 1 | `15644C7: FF C3 8B C3 E9 50 FF FF FF`，9字节 | 覆盖完整9字节，重放同样三条语义；不能截成6字节 |
| post 2 | `1564525: FF C3 8B C3 E9 F2 FE FF FF`，9字节 | 覆盖完整9字节，重放同样三条语义 |

两个 direct 原目标都是 `154E5B0`，但该 RVA 有共享 ABI；pre 仅保存原 RCX/RDX/R8/R9，不按 dump 名字重签一个 C++ 原函数类型。virtual 原目标来自实际 `[RAX+178h]`，原 RCX 是 invokable、RDX 是 object[]、R8 是 method token。RSI/RBP/EBX 分别是当前 event/list/index，仍须和当前父 record、ledger generation 对应。

## pre：普通 CALL frame，恢复后尾跳

source CALL 进入 pre helper 时 `I=RSP`、`[I]=原sourceReturnPc`、`I%16=8`；原 game body 的 RSP 是 `P=I+8`。这里可以复用现有 MidEntry 的保存原则：在合法 FRAME prolog 中保 GPR、XMM低128位、RFLAGS、MXCSR、x87控制字，CLR只见copy/只读观察，返回前恢复。不能扩大为已保 AVX upper lanes 或完整 x87 数据栈。

恢复末尾必须遵守 Windows x64 epilog 格式。建议完整 helper 以合法 LEA/POP→`jmp [rip+fixedTailPtr]` 结束，跳到不改 RSP/非易失寄存器的极小 leaf tail。`jmp [rax+178h]` 的 ModRM 不是00，不能直接塞在仍按完整 prolog解释的 epilog里；作为独立无栈 leaf 则没有 helper 栈帧可遗漏。此限制已核 [Microsoft prolog/epilog](https://learn.microsoft.com/en-us/cpp/build/prolog-and-epilog?view=msvc-170)。

目标运输有两级选择，必须在实现前明确：

- 最小透明执行：direct tail 跳固定原目标；virtual leaf tail恢复后执行原 `jmp [rax+178h]`。callee 的真实返回槽不变。若 observer 之前另读了一次 vtable target，只能在明确 vtable 稳定合同下关联该观测，不能无条件称它就是随后实际跳入的目标。
- 严格一次目标读取：virtual pre 在完整保存后由 native 固定逻辑读取目标一次，把它存入私有 native record/不可写机器槽；恢复阶段将目标复制到原 callee 的 shadow slot，例如 `[I+8]`，leaf tail `jmp [rsp+8]`。GPR/flags不因此改变；此槽必须属于保证存在的四个 home slots，不碰第5实参或parent保存寄存器。若目标读取异常，应保持原生异常展开与父 finally，而非被观察故障处理吞掉后跳零。该路径尚需具体指令与异常fixture验证，不可只由文字宣称透明。

Microsoft 规定四个 register home slots 由 callee 使用，caller不能跨调用保值；同时不能借用当前 RSP 以下的所谓 red zone。[官方 stack usage](https://learn.microsoft.com/en-us/cpp/build/stack-usage?view=msvc-170)支持上述 shadow scratch 前提，但不证明特定实现已正确。fixture 应把允许改变的 home slots 与必须保持的外侧 canaries 分开核对。不要用 push target/ret、篡改真实 source return 或普通C++再CALL来省略尾跳设计。

## post：JUMP入口仍是原 game frame

callee RET 已消费 pre 的硬件返回槽，此时 `RSP=P`、`P%16=0`；源 continuation 的 E9 不增加任何栈项。直接跳进现有 MidEntry 会把 `[P]` 错当返回地址，必须拒绝。

近端 post stub保持 `RSP=P`，只有三段：

```asm
; 设计示意，不是可直接部署的编码；pointer table放到不可执行数据区。
call qword ptr [rip + postObserverPointer]
; 此真实CALL只调用观察helper，不调用/重放原listener。
inc ebx
mov eax, ebx
jmp originalLoop1564420
```

该 stub 本身不 PUSH、不 SUB、不占用非易失寄存器。原 UnityEvent frame已有20h outgoing shadowspace供这次观察CALL使用；helper入口才是新的普通 CALL frame，负责完整保存/恢复 callee 返回后的机器状态。`inc/mov` 必须在观察后恰好重放一次，最终flags与原 continuation一致；不能观察完再意外重放两次inc。

stub 内观察CALL的真实返回PC是 **stub自己的+6等实际长度位置**，不是原 source return。pre记录的 SourceReturnPc 与 post-helper看到的 TransportReturnPc须分字段/私有映射，避免把post新CALL PC伪装原listener返回。三stub各自唯一 transport PC用于选 site。

### post unwind 的可实现方案

为三个不连续 post片段登记动态 `RUNTIME_FUNCTION`。各自 `UNWIND_INFO` 是零新增prolog/零新增codes的 secondary record，带 `UNW_FLAG_CHAININFO`并链接固定原 UnityEvent 的完整 `RUNTIME_FUNCTION`；原frame分配/保存语义保持。helper通过自己的普通静态 `.pdata/.xdata`先退回post stub；stub再用chain退原 UnityEvent frame，最终到父原生 transport caller。

这是基于官方noncontiguous code chain用途的设计推论，尚未执行。Microsoft明确 chained secondary不能另做额外固定栈分配或PUSH分组，因此不能把post fullsave栈直接硬接原frame；fullsave必须在上述真实CALL helper里。[官方 exception handling](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64?view=msvc-170#chained-unwind-info-structures)。

动态代码、xdata和function table必须永久持根。建议分配 GameBase 以上且在 source rel32可达范围的近端块，以实际GameBase作为动态table基址，让post/code/xdata及被链接的原`.xdata`均可用非负32位RVA表达。准确核DWORD对齐、范围不重叠/不溢出、实际profile和原function表未变，再 `RtlAddFunctionTable`；成功及真实 `RtlLookupFunctionEntry`读回确认都发生在任何source patch之前。注册失败是写前拒绝；有source写尝试后不得移除table或释放任何stub。[RtlAddFunctionTable官方合同](https://learn.microsoft.com/en-us/windows/win32/api/winnt/nf-winnt-rtladdfunctiontable)。

动态table基址选择、chain跨原映像的实际lookup和任意指令位置的unwind都必须在fixture实证，不能因API返回TRUE即视为证明。不能给post伪造一个正常栈return地址，也不能依赖改ret绕过 shadow-stack配对。

## 父/child记录与最小新ABI

旧V1 Observer40已经一次Attach，不能在父调用中途挂一份新的listener全局callback再让旧父借用。建议新V2 observer把Enter/Exit/Mid/ListenerBefore/ListenerExit/User整份一次冻结；父Enter在null时也压record并固定整份snapshot。保持旧V1 ABI用于旧fixture，V2生产receipt必须指明本次六区能力，旧Ready144/三写不能授权新增六区。

callback语义frame可定128字节候选：header `u32 Version,Size,Site,Stage`；`u64 ParentSequence,ChildSequence,ParentManagedToken`；9个pointer为`Event,List,ActualTarget,ArgRcx,ArgRdx,ArgR8,ArgR9,SourceReturnPc,SourceRsp`；末尾`u32 Index,OsThreadId,Flags,Reserved`。ASM完整机器snapshot保持私有，callback得到copy；Before可返回独立managed child token，Exit显式传同token。此布局只是实施建议，落盘时需逐offset staticassert与CLR断言。

原父record地址会复用，应另加单调ParentSequence；ChildSequence也独立不可复用。固定TLS child栈同时匹配当前physical parent、parent sequence、source site/return/RSP、event/list/index与当前线程，不搜索更老父；递归/foreign/worker必须遮蔽。stack overflow以固定mask配对，禁止揭开旧parent。

callee正常返回才走post，核当前child后发Returned并在native finally清child。callee SEH/C++不经过post，由现有父 `VoidShimExit` 在同descriptor父Exit之前先对自己的未完成children发Abnormal/清TLS；原异常不由CLR包围，也不伪发normal。父Exit仍负责准确pop、原Void admission负责更宽物理窗口。嵌套父只清自己的children；回调自身SEH/C++被包含并置stickyUnknown，但不得屏蔽原callee或父异常。

## 文件边界与先验项目

| 负责文件 | 实现边界 |
|---|---|
| 新 `ListenerContext.h/.cpp` | site固定配置、V2只读callback语义frame、私有child token/TLS/overflow、状态与未知传播；不自己写游戏指令 |
| 新 `ListenerEntry.asm` | pre CALL fullsave+合法tail epilog+leaf tails；post observer普通CALL helper；真实机器状态保存与静态unwind |
| 新 `ListenerPostStubs.cpp` | near stub编码、完整replay、动态function/xdata表构造与注册/readback；所有对象永久责任 |
| `BootstrapVoidContext.*` V2扩展 | 父snapshot包含listener表，唯一父sequence、异常child清理；保持V1布局合同 |
| `FirstInstall*` V2 producer/receipt | 六个source区与所有stub/unwind的固定profile/首写前验证；新写数与身份，任何写后unknown保留，旧receipt拒绝 |
| 新六区MASM/native fixture | 仅自有函数首次执行前发布；真实source形态、machine state/returnPC/unwind/SEH与Cpp，不调用游戏 |

进入生产publisher前应至少取得：

1. callee内部真正记录 `_ReturnAddress()`，三处分别为source+5/+5/+6；高64位所有原参数、正常原调用一次、正常replay一次、inc结果及flags保持。覆盖virtual实际target与home-space canaries。
2. 在pre partial prolog/body/epilog、leaf tail、post helper及post动态stub每个指令边界做真实 `RtlLookupFunctionEntry`/`RtlVirtualUnwind`；从观察helper捕获的实际context一路到post stub再退到外层真实caller。不能只构造一个跳过post的context。
3. 原callee SEH/C++在外层收到相同实际code/flags/payload，post未被调用，父finally精确清本代child；Before/Post/Abnormal观察故障不改原异常。异常记录比对真实first-chance与outer，不硬编码flags0。
4. 空表/多row、worker、同/异父递归、旧父Enter后Attach、父/child地址复用但token不同、容量溢出与unknown保责任；100次等静态计数不能代替真实配对。
5. 单独真实固定映像映射只验证六区bytes/页保护/flush/链表table与V2 receipt；不执行游戏函数。然后再做生产CLR采用和真实coldboot。实际invokable dispatch仍不等于每个delegate leaf，更不等于所有future/mutation/cancel闭合。

以上Microsoft来源已于2026-10-24通过web读取；它们提供ABI/表格合同，不是本方案的实现通过证据。本批不会因此新增任务勾选或开放RequestPlay。
