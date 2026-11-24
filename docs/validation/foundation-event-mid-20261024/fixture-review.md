# Mid 原生 fixture 的只读覆盖复核

2026-10-24。源码处于 root 全量构建冻结期间，本次只读复核并新增本页，未修改源码、测试、原生文件或脚本，也未执行构建或游戏。本页说明实际断言范围，运行结果须以本批次最终日志为准。

后续真实运行证据：root 定向 native fixture 已 exit 0，`fixture-result.json` 记录 **3559 checks / 0 failures**，entered/exited **16/16**，inFlight **0**，faults **6**，unwindWalks **22**，virtualUnwinds **128**。这些数值来自 `D:/RizToolbox-build-cache/event-mid-final-20261024/event-mid-context/fixture-result.json`。首次全量在 MASM XMM 输入加载报 A2022，原因是外部符号声明为 BYTE 而 MOVDQA 需要 128 位；16 处加载明确写成 `xmmword ptr` 后 root 重建通过。首错日志保留在 [build-first.log](build-first.log)，后续完整构建 **1490/1490** 由 root 冻结。

22 次 walk 包括正常 callback 的实际栈快照与六次 callback 异常 filter 的真实 exception CONTEXT；128 次虚拟展开是 16 次 callback × 8 个依托真实栈槽构造的指令边界 CONTEXT。二者都没有执行游戏或建立 native parent token 证明。EXE 自己检查 helper 实际 prolog/epilog 字节、R13 FP metadata，以及五字节 fixture CALL/relay 的目标和周边字节，再执行这些 unwind 检验；未另行宣称生产发布安全。

`Test-EventMidContextNative.ps1` 强制要求已有 helper DLL 和 VS x64 工具路径，编译独立 EXE，并在编译或 EXE 非零退出时失败。EXE 只在单线程准备阶段改写自身五字节 NOP，先配置 callback，再发布 E8 CALL；near relay 使用 FF25 尾跳，不增加返回地址。真实 return PC 是导出的 `SiteReturn = Site + 5`，不依赖猜测游戏 caller。脚本输出 helper/EXE 哈希和 `nativeParentVerified=false`、`gameRuntimeVerified=false`，不修改或启动游戏。

| 实验 | 源码实际支持的结论 | 不能扩大为 |
|---|---|---|
| 未 patch / 已 Configure 未发布 | 原函数进入和 continuation 各一次；callback 为零，已配置槽 entered 仍为零。发布时检查五个原 NOP 字节、相对位移、前后各八字节未变、relay 机器码和完整 helper 地址。 | 已实现生产并发安装、撤销或热卸载。|
| 正常 callback 与全现场 | 前后快照逐一比较 16 个 GPR 槽（含 RSP）、16 个完整 XMM、RFLAGS 实测值、MXCSR、x87 control word 和栈 canaries。callback 再比较输入快照，断言真实 returnPc、entryRsp + 8 = source RSP、RBP/RSI 为本次 native list/event。正常 callback 故意改 ABI 易失寄存器和浮点控制舍入值。 | 全 XSAVE、AVX upper YMM、AVX-512、x87 数据寄存器或整个 x87 status/tag 已覆盖。非易失 GPR/XMM 通过完整哨兵比对验证，没有故意违反普通 C++ ABI 去破坏 wrapper 的非易失寄存器。|
| 空/非空/mutable list | count 0/1/8 的 native POD 列表在 continuation 中循环零/一/八项。第一项修改 count/version 时循环按动态 count 只执行一项。递归 child 使用自己的 list/event 地址，child 返回后恢复 fixture 自己的 TLS Work 栈。 | 实际 IL2CPP List、GC root、Unity listener 全体执行或游戏 parent token 已验证。此列表循环是 native fixture 的模拟，不能等同于运行游戏原函数。|
| callback × original 九组合 | callback 正常/SEH/C++ 与后续 original 正常/SEH/C++ 做 3×3 组合。每次都进入 continuation 且完成两项列表循环；callback 故障累计为六。原 SEH filter 核 code、两个 payload 参数；原 C++ catch 核 typed object payload，fixture original 的异常没有被 mid containment 冒充成功。 | 所有 Windows 异常、任意 C++ exception object 的全部字段或任意游戏 unwind 路径已证明。|
| 实际栈 walk | 每次正常 callback 调用 RtlCaptureContext 后有界 RtlVirtualUnwind；callback 故障 filter 使用真实 exception CONTEXT 再 walk，然后 CONTINUE_SEARCH 交 helper 自身 containment。要求依次能看到 helper ASM、fixture original、独立 native caller 的真实返回标签。 | 每个 prolog/epilog 指令地址都真实抛过异常。|
| 八个 unwind 边界 | 先核 helper 具体 prolog/epilog 字节、frame register R13 和 frame offset。用当前真实 helper stack slots 加 before 寄存器构造 entry、push R13 后、pushfq 后、sub 后、set FP 后、epilog LEA/pop/RET 八种 CONTEXT。断言展开 RIP/RSP、全部 ABI 非易失 GPR 和 XMM6..15。 | 八个点的 context 是独立采样或硬件异常现场；它们是依托真实栈槽的虚拟展开检验。|
| worker 阻塞与逻辑开关 | CreateThread 进入 callback 后阻塞；主线程观察 dispatch inFlight=1，再关闭 fixture observer 的逻辑开关，计数仍为1；放行和 join 后才为零，worker thread ID 不等于主线程。 | mid stats 覆盖 ASM 恢复/RET 的全部时间。helper 在 C++ dispatch 退出时就递减，外层 NativeVoid 物理窗口仍是后续归属与回收所需条件。|

Evidence → Finding → Path：本 EXE 的实际机器指令调用、完整现场比对、native 异常 containment 与 unwind 检查，在最终日志确认通过后可支持这份 helper ABI 的已测边界。游戏现场安装、native parent generation/token、执行集合变化后的 listener 责任账本和实际 IL2CPP GC/释放故障仍需各自证据。mid 零 inFlight、fixture continuation 到达或一份初始列表 snapshot，均不能独立推导整个 run 已完成或所有引用可回收。
