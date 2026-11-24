# UnityEvent 中段观察点与原生桥设计

结论：选择 `UnityEvent.Invoke+0x13B`，即 RVA `0x156441B` 的完整五字节 NOP。保留前面的两条 XOR，把这一个 NOP 替换为 `CALL rel32`，由近端 relay 以无寄存器副作用的 tail JMP 转交自有 ASM 桥，桥最后 RET 到真实返回 PC `0x1564420`。该形状不需要重放 XOR，也不需要合成或修改返回地址。此处只完成静态证据与独立 transport；**生产游戏 patch 尚未发布，其并发发布安全尚未证明**。

## 固定输入与单函数证据

[inspect-mid-site.py](inspect-mid-site.py) 流式核对 GameAssembly SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，用异常目录的 17 次二分查找定位唯一 `RUNTIME_FUNCTION`，只解析该函数的一个 unwind 项，再反汇编其 148 条指令。没有读取 `script.json`，没有全映像交叉引用扫描，也没有执行游戏。

对应 [mid-site-evidence.json](mid-site-evidence.json)：

| 项目 | 实际值 |
| --- | --- |
| 原生范围 | `[0x15642E0, 0x156454E)`，末尾为 `int3`；旧证据取到 `0x1564550` 的额外两个 padding 字节不属于该 unwind 项 |
| RUNTIME_FUNCTION | 异常目录索引 `103257`，Unwind RVA `0x1DB3FB4` |
| UNWIND_INFO | 版本 1，flags 0，prolog 长 `0x61`，10 个槽，六条 unwind 操作，无 frame register、handler 或 chained entry |
| 栈与保存寄存器 | `push rsi`、分配 `0x20`，故 body RSP 比函数入口低 `0x28` 且模 16 为 0；另保存 RBX/RBP/RDI/R14 到 body RSP 的 `+0x30/+0x38/+0x40/+0x48` |
| 九字节候选 | `0x1564417: 33 DB 33 C0 0F 1F 44 00 00` |
| 选定五字节 | `0x156441B: 0F 1F 44 00 00`；下一条是 `0x1564420: cmp eax,[rbp+0x18]` |

函数内所有直接 CALL/JMP 条件边均已检查，没有目标落入九字节候选内部；三个循环回边都进入 `0x1564420`，不会在每个 listener 后重新进入观察 NOP。此结论限于这个固定函数的直接边，不能扩大为外部／间接控制流全覆盖的证明。

## 为什么只改五字节 NOP

`xor ebx,ebx` 与 `xor eax,eax` 分别使整个 RBX、RAX 归零；后者令 CF/OF/SF 为 0、ZF/PF 为 1，AF 未定义。桥必须保存当时实际 flags 图像，不能用“结果为零”合成一个 flags 常量。NOP 虽显示内存形式，不会读取 `[rax+rax]`，没有源寄存器、flags 或内存状态需要重放。

五字节替换为 `E8 <disp32>`，位移为 `relayVA - (loadedImageBase + 0x1564420)`，必须在 signed int32 范围内。relay 可使用 `FF 25 00 00 00 00 <bridgeVA64>` 的 RIP 相对 tail JMP，保持 CALL 已压入的真实 PC、RSP、GPR 和 flags。模块首选 ImageBase 只用于静态记录，不能当实际装载地址。

从 `0x1564417` 开始只改五字节会切开 NOP，因此不可用。完整覆盖九字节再用五字节 CALL 加四个 NOP时，硬件返回 PC 为 `0x156441C`，并非 `0x1564420`；若还要恢复原 XOR 语义和 flags，反而增加控制流及重放责任。本批选择保留原 XOR 的方案。

中段 RBP 为游戏实际取出的 executing list，RSI 为当前 UnityEvent；RBX/RAX 为初始索引零。这是观察实际列表的入口条件，不是每条 listener 已执行的证明。后续三个实际调用点仍需独立归属与验证。

## 保持栈、flags 和异常展开

新增 [MidEntry.asm](../../../native/RizToolbox.JudgeContext/MidEntry.asm)、[MidContext.h](../../../native/RizToolbox.JudgeContext/MidContext.h)、[MidContext.cpp](../../../native/RizToolbox.JudgeContext/MidContext.cpp)。内部 Frame 为 `0x1B0`、Config 和 Stats 均为 `0x28`；32 个永久槽按真实 ReturnPc 唯一选择。没有给游戏函数或未经检查的 C++函数编造 RVA。

令真实 CALL 进入桥时 RSP 为 I，I 模 16 为 8；原游戏中段 RSP 为 I+8。顺序为：先 `push r13` 并记录 `.pushreg r13`；再 `pushfq` 并用 `.allocstack 8` 记录其栈效应；再分配 `0x1E8`，得到 16 字节对齐的 B=I−0x1F8；设 R13=B+0x80，并记录 `.setframe r13,0x80`。原 R13 位于 B+0x1F0，原 flags 位于 B+0x1E8，真实返回槽位于 B+0x1F8。

桥保留所有 GPR、XMM0–15 的低 128 位、MXCSR 和 x87 控制字；Frame 中 RSP 项记录 I+8，R13 项取原保存槽。C/C++回调前清 DF，结束时再恢复原 flags。为非易失 GPR/XMM 保存生成对应 unwind 操作，volatile 保存不冒充 unwind 元数据。Frame 是栈内的内部 const 观察契约，回调不得通过强制转换修改它；它也不是 AVX 上半部或 x87 数据栈快照。

回调后先恢复寄存器和浮点控制，body 最后用 `push [rsp+0x1E8]; popfq` 恢复 flags。真正 epilog 只做 `lea rsp,[r13+0x170]; pop r13; ret`，LEA 一次跳过 flags 槽到原 R13 槽，后续无改 flags 指令，RET 消耗真实 CALL 返回槽，最终 RSP 恢复为 I+8。这里不能以 `add rsp,...` 代替，也不能把 `popfq` 塞进 epilog。该形状依据 [Microsoft 的 x64 prolog/epilog 规则](https://learn.microsoft.com/en-us/cpp/build/prolog-and-epilog?view=msvc-170)；`.pdata/.xdata` 和动态 relay 的展开边界依据 [x64 异常处理规则](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64?view=msvc-170)。实际编码、prolog/body/epilog 各 PC 的 `RtlVirtualUnwind` 结果仍须由独立 native fixture 验证，静态推导不是动态验收。

回调的普通 SEH/C++异常在自有 C++Dispatch 内包含并记 Faults，不跨出到游戏。未知 ReturnPc 不选槽、不调用观察者，桥原样恢复返回。Dispatch 的 inFlight 覆盖回调与异常处理；C++返回后还有 ASM 恢复和 RET，故该统计不能独立授予资源回收权限，必须与外层实际 native void0 parent 的完整物理窗口共同检查。

## 父 token 归属与安全发布限制

中段观察必须绑定同一线程、当前最内层的实际 native void0 invocation token、原生 self/method/caller 和中段固定 ReturnPc。不同 receiver 或同 receiver 的递归调用也各有 token；外来帧、worker、无 parent、溢出、逻辑解绑和未知状态不得继承旧父 token。不能只靠 receiver 相同、最后一次 TLS 值或“某个父调用还在途”认领。Frame 中的 RSI/RBP 只是与该物理父层验证后的对象来源；对象读取、GC root 和实际 listener 图证据由生产 owner 承担。

现有 [pinned Dobby 静态证据](../judge-context-publication-20261020/dobby-static.json) 和真实 fixture证明 Prepare→Configure→Commit 的准备／激活顺序，不证明五字节代码写入的原子性，也没有选择本例 CALL opcode 的独立导出协议。因此不能把普通入口 detour 的 Prepare/Commit 直接当成本中段 CALL 的安全安装器。

`0x1564418` 确为八字节对齐，原八字节为 `DB 33 C0 0F 1F 44 00 00`；保持前三字节后，可构造 `DB 33 C0 E8 <disp32>`。但对齐八字节数据原子写不自动证明跨核取指、已解码指令和执行点的发布协议。`VirtualProtect`、`memcpy`、cache flush、读回一致或配置先于发布，都不能单独补上这个证明。

生产发布必须另有可验证的 native 执行安全点，排除任何线程正在或将并发执行该函数，并处理线程创建竞态、指令缓存发布与未知结果保留；否则维持 unpublished。本批可以先验证 helper、真实 fixture、父层归属和实际列表 owner，不能据此声称游戏已接通中段，也不能关闭来源、取消或完整退休责任。
