# UnityEvent 内段 CALL 的最小真实 fixture 方案

2026-10-24，设计与实现范围说明。本稿遵循 root 最新方案：保留 `1564417 xor ebx,ebx` 与 `1564419 xor eax,eax`，只把 `156441B` 的五字节 NOP `0F 1F 44 00 00` 替换成 `E8 rel32`。实际 CALL 返回地址与后续循环入口都为 **1564420**；不再使用九字节替换或重放 XOR。

root 随后授权的首版已独立落在 `native/RizToolbox.JudgeContext/fixture/MidFixture.cpp`、`MidCaller.asm`、`scripts/Test-EventMidContextNative.ps1`，未修改原 Initialization fixture，也不新增 managed Runtime case。代码仍待 root 统一构建。下文 parent TLS 是后续架构要求；本次 EXE 没有实现或假称原生 parent token，输出明确 `nativeParentVerified = false`。native mid stats 只覆盖 dispatch/callback；ASM 后续恢复与 RET 尚在运行时它就可能为零，因此它不能独立授权释放整个 mid/native parent 窗口。

现有 [静态指令证据](../foundation-native-void-20261024/terminal-disassembly.txt) 显示此时 `RSI` 为事件对象、`RBP` 为刚取得的 `m_ExecutingCalls`，之后首先比较该列表的 count。现有 NativeVoid 回归已证明入口的原生调用窗口，但当前 `VoidContext.cpp` 未导出 native TLS parent token。本稿的 parent 契约属于待实现要求，不能把 managed 当前 context 当成已有原生证据。

## 最小产物与原生路径

沿用已有 `native/RizToolbox.InitializationAbiFixture/{Fixture.cpp,Entries.asm}`，增加独立 `RtEventMid*` exports 和一个有合法 unwind metadata 的 MASM 函数。它的前 32 NOP 可由现有 Dobby/NativeVoid 入口 transport 持有；中段按真实顺序执行两条 XOR 与唯一五字节 NOP。导出 entry、site、siteReturn、continuation 的实际地址；siteReturn 与 continuation 必须相同且为 site + 5。中段安装器使用与生产相同的 CALL/near relay/helper 路径，不通过托管函数假装经过该 site。

最小执行链：真实 native caller → Dobby NativeVoid parent → 原 MASM 函数 → 中段 CALL → 独立 near relay 尾跳 → 保存现场的 helper → 原生 containment wrapper → observer → 恢复现场并 RET → siteReturn → 原 MASM 循环 / 返回。相对 CALL 目标须可在 signed rel32 范围内；helper 超距时 relay 只尾跳，不能多加 CALL 或改变栈。完整地址差与实际 patch 字节必须断言，不截断 64 位地址后伪造通过。

fixture 只用受控 native POD 列表和固定容量 8 的 row 数组，模拟 `_items +0x10`、`count +0x18`、`version +0x1C`，不声称它是实际 IL2CPP 对象或 GC root。事件和列表是两个不同的 opaque identities。进入 site 前将 RSI、RBP 分别设为这两个真实 fixture 地址，其余 GPR 和 XMM 使用各不相同的 64/128 位哨兵；RAX/RBX 已由原 XOR 置零。函数必须先保存调用者所有 ABI 非易失寄存器，最终正常恢复。

## 现场、flags 与 unwind 的候选实现

建议由 helper 代理确认以下 R13 frame register 布局，再冻结 ABI。设 `S` 为中段 CALL 后 helper 的 entry RSP，故 `S % 16 == 8`、`[S] = siteReturn`、原 site RSP = `S + 8`。

```asm
push r13                 ; .pushreg r13；不改 flags
pushfq                   ; .allocstack 8；保存原 flags，unwind 只需跳过此槽
sub rsp, N               ; .allocstack N；N % 16 == 8，调用前 RSP 对齐 16
lea r13, [rsp]           ; .setframe r13, 0
; 保存其余完整 GPR、XMM0..15、MXCSR；原 R13 在 [r13+N+8]
; 原 flags 在 [r13+N]；原 return slot 在 [r13+N+16]
; 包含 0x20 shadow space；POD 原生 wrapper 隔离 observer SEH/C++
; 恢复其它 GPR、XMM、MXCSR，保留 R13 作 frame register
push qword ptr [r13+N]
popfq                    ; 仍在 body，之后不能再执行改 flags 的指令
lea rsp, [r13+N+8]       ; 规范 epilog，一次跳过 allocation 与 flags 槽
pop r13
ret                      ; 使用真实 siteReturn；无需改栈返回地址
```

该布局避免在 epilog 使用 `popfq`，也避免最后 `add rsp` 损坏 flags。`.allocstack 8` 与实际 PUSHFQ 的 unwind 偏移、R13 FP 的建立位置及 MASM 生成的 `.pdata/.xdata` 仍须由实际 fixture 验证，不能仅凭伪代码认定合法。wrapper 只包含观察者故障；原函数在中段返回后的 SEH/C++ 仍应进入 NativeVoid 原有 finally 并由外层 native caller 捕获。原始 GP/XMM 恢复缓冲和提供给 observer 的只读快照应分开，防止错误修改观察快照成为改寄存器的能力。

浮点状态最低契约为全部 XMM0..15 与完整 MXCSR；如果 helper 使用浮点/x87 或要宣称完整 CPU 现场，需采用对齐的 FXSAVE/FXRSTOR 并同时核对 x87 control/status/tag。最小版不得把“GPR/XMM/MXCSR 保真”写成已覆盖 AVX upper YMM、AVX-512 或完整 XSAVE 状态。

## 保存与比对不能自己破坏现场

在 fixture 自己的大固定栈帧中，为 before/after snapshots、shadow space 与 canaries 预留不重叠区域。site 前、siteReturn 的快照全部使用 `[rsp+constant]` 的 MOV/MOVDQA/STMXCSR；FLAGS 用 PUSHFQ/POP 内存取得，净 RSP 不变；取得完整快照前不调用 C++，也不借用某个未保存的 GPR 充当输出指针。比较工作在完整 after snapshot 之后进行。

严格断言全部 15 个非 RSP GPR 相等，site RSP 前后相等，RSI/RBP 指向当前实际事件/列表，XMM 16 × 128 位逐字节相等，MXCSR 相等，before/after RFLAGS 实际读取值相等。原 XOR 的 AF 虽无架构规定，但本方案比较的是 XOR 已执行后的真实输入值与 CALL 返回后的输出值，不能以 AF 未定义为由忽略 bridge 对已保存 flags 的破坏。observer 故意改变易失 GPR/XMM、状态 flags 和安全的 MXCSR 舍入位，以证明恢复不是空操作。受保护的上层 caller stack、shadow canaries、原返回地址必须未被改写；entry RSP 与 site RSP 的固定 8 字节差须实测。

## Parent 必须是原生调用事实

NativeVoid helper 需要一个有界、每线程、覆盖每次物理调用的 TLS 栈。每层记录至少包含 slot、native generation、Self、MethodInfo、原入口 caller、Enter 返回的 broker token、phase；只有 `phase = Original` 且 token 有效的最近栈顶可以为 mid 提供 parent。Enter/Exit 观察期、worker、foreign alias、失败/零 token 都必须保留自己的物理层或显式 invalid 状态，不能跳过它们向外寻找一个看似可用的 parent。

mid frame 至少报告真实 `siteReturn`、entry/site RSP、完整保存现场、native thread identity，以及最近 native parent 的 slot/generation/token/phase/原帧标量身份。Managed adapter 再检查 exact event、expected site、parent invocation generation、实际 RBP 列表。它不能从 RSI 等于某个已知事件这一条推导 ownership。深度达到限额后报告 unknown、不中断原函数，不能回退复用前一层。

## 必须执行的断言组

| 组 | 最小真实实验与通过条件 |
|---|---|
| 基线与安装 | 未 patch 的原列表循环先跑一次；安装前比对五个原字节。Prepare/配置不发布，publish 后只有五字节 NOP 改成 CALL。前后 XOR、前置/后置 canary 字节均不变；Site/Return/relay/helper 地址互不混淆。|
| 完整现场 | 原 NOP 与 CALL 两次各取得 before/after 全现场；严格核对 GPR、RSP、RFLAGS、16 个 XMM、MXCSR 与栈 canary。原函数只进入一次、site 只经过一次、continuation 只经过一次，无额外 RET/JMP 到猜测地址。|
| 空与非空列表 | count = 0 时 mid 仍观察同一实际列表，原循环零次；count = 1/8 时 mid 先于第一个 listener，循环每项实际执行一次。修改准备阶段得到另一个 executing list 后，mid 必须报告新 RBP，不能报告之前缓存列表。|
| mutable list | 中段记录 list identity/count/version；第一个 fixture listener 随后合法改变 count/version/items，并按原循环动态 count 比较继续。原函数行为须与未 patch 基线一致。初始 snapshot 只能证明 site 时的集合，后续变化必须留痕并使“全执行列表已完成”的上层结论无效。|
| 原生 containment | observer 正常、SEH、C++ 三种模式；后两种都返回 continuation、原列表循环行为不变，callback fault 计数可见，helper 中段 inFlight 最终归零。observer 故障不能伪造“已证明执行”，也不能转成 parent original 的成功证明。|
| 原函数 unwind | mid 正常返回后，fixture original 分别抛原 SEH/C++。外层 native catch 核对原 code/kind/payload，NativeVoid after 不运行、Dispose 与 physical exit 仍配对；mid 不捕获或改写该原异常。|
| worker 与阻塞 | CreateThread 调用原函数，在 native mid observer 内阻塞，主线程看到 parent/mid physical inFlight 同时非零；逻辑 detach 后仍非零，放行后才归零。worker lane 只读取标量身份/置 fault，不接触 Unity/IL2CPP；此调用没有主线程 root capture。|
| reentry 与 foreign mask | 从中段 observer 或 fixture listener 递归进入相同 native 入口，分别使用同/不同 Self、MethodInfo。每个 mid 对应自己最近的物理 generation/token；foreign/invalid child 不继承 parent 的已知事件。child 返回后恢复外层 parent，原生与托管层都按自己的深度配对。|
| 无 parent / phase 错误 | 直接 native caller 到中段（没有 NativeVoid owner），以及 Enter/Exit 观察期间伪造到 mid；观察报告无有效 parent/unknown，原函数仍按原样返回，不从某个 managed 当前 context 借 token。|
| unwind metadata | 原生 observer 的异常 filter 先有界调用 RtlLookupFunctionEntry/RtlVirtualUnwind，再 CONTINUE_SEARCH 交 helper containment，栈应依次穿过 wrapper/helper、fixture original、NativeVoid、实际 caller。另用实际 captured frame 的已知栈槽构造各 prolog/epilog 指令边界 CONTEXT，核对虚拟展开的 RIP/RSP/R13；这部分是 metadata 模拟检验，不能声称在每条指令上真实注入异常。|

首版建议拆为独立原生 fixture 导出与 Runtime 四个 case：安装/全现场；列表/parent/reentry；containment/原 unwind；worker/detach。只有 root 确认 ABI 后才实现；避免使用 hook 新调用去重建列表、调用 PrepareInvoke 或主动执行任何 listener 来“取证”。本 fixture 即使通过，也没有证明游戏该点的实际对象布局、IL2CPP GC/root 故障、leaf/mid 全覆盖或整个 run 的终止与回收。
