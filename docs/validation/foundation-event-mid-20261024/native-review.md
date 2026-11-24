# Mid 原生桥只读复核

本复核读取当前 `MidEntry.asm`、`MidContext.h/.cpp` 和独立 `MidCaller.asm/MidFixture.cpp`，未修改源码、脚本或测试，未编译、安装或执行游戏。此时 root 正在统一构建；以下区分代码推导、fixture 的检查范围与尚未获得的运行证明，不预报最终构建结果。

## 栈与 flags 推导

真实 CALL 入口记为 I，I 模 16 为 8。先保存 R13，再 `pushfq`，两条指令均不改变原 flags；分配 `0x1E8` 后 B=I−0x1F8 且 B 模 16 为 0。此时：

| 内容 | 位置 |
| --- | --- |
| C/C++ shadow space | B+0x00…0x1F |
| `0x1B0` 字节 Frame | B+0x20…0x1CF，16 字节对齐 |
| 原 flags | B+0x1E8 |
| 原 R13 | B+0x1F0 |
| 硬件 CALL 写入的真实返回 PC／入口 I | B+0x1F8 |
| 游戏原中段 RSP | B+0x200，即 I+8 |

Frame 的 GPR 槽保存原值：R13 从原 push 槽读取，RSP 记录 CALL 之前的游戏栈。保留的 R13=B+0x80 只作为桥自己的 frame pointer，不混入原寄存器快照。所有 XMM 保存地址对齐，Frame 尾部 MXCSR/x87 控制字与 reserved 字段也没有覆盖原 flags、原 R13 或返回槽。

回调前的 `cld` 服务于 Windows C/C++调用约定；原 DF 已随 flags 单独保存。返回阶段恢复全部 GPR、XMM0–15 低 128 位、MXCSR 和 x87 控制字，再在 body 使用 `push [rsp+0x1E8]; popfq`。最后只有 `lea rsp,[r13+0x170]; pop r13; ret`：LEA 跳过 flags 槽，恢复原 R13，RET 使用未经替换的真实 PC；从 `popfq` 到 RET 没有改 flags 的指令，源 RSP 回到 I+8。没有发现这组偏移或恢复顺序的确定错误。

这份状态契约不包含 AVX 上半部、x87 数据／状态栈、调试寄存器或任意特权 flags 的通用虚拟化。当前源点的 RAX/RBX 已被游戏 XOR 清零，独立 fixture 使用同一初始条件；不能据此泛化为任意中段任意指令状态的完整 CPU 快照。内部 const Frame 也必须保持可信只读契约，强转写入恢复槽会违反该契约。

## Unwind 与异常范围

`.pushreg r13`、对 `pushfq` 的 `.allocstack 8`、固定分配、`.setframe r13,0x80` 和非易失 GPR/XMM 保存按实际顺序记录。对 flags push 记录的是八字节栈效应，没有声称 Windows unwinder 会恢复 RFLAGS。恢复 flags 保持在 body，严格 epilog 仅包含基于 FP 的 LEA、寄存器 POP、RET；没有把 `popfq` 或基于 RSP 的 LEA 冒充合法 epilog。参照 [Microsoft 的 epilog 规则](https://learn.microsoft.com/en-us/cpp/build/prolog-and-epilog?view=msvc-170) 和 [unwind 数据规则](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64?view=msvc-170)。

独立 fixture 会先核对实际入口 18 字节和尾部 10 字节，再构造八个 `RtlVirtualUnwind` 上下文：入口、保存 R13 后、保存 flags 后、分配后、建立 FP 后以及三处 epilog 边界。它还会从真实 callback 与 SEH/C++ exception context 展开到 helper ASM、fixture body 和原生 caller。因此它能检查真实链接产物，而不是只验证一份手写 unwind 表；八个上下文不是对每条机器指令逐点覆盖，最终是否通过必须读取 root 的实际 fixture 输出。

自有 Dispatch 只调用观察者，不调用或重放游戏代码；普通 observer SEH/C++异常在自有 native 层包含并计入 Faults。未知 ReturnPc 不调用任何 slot observer，ASM仍恢复源状态。不可把这个容错承诺扩大成能拦截 fail-fast、进程终止等不可恢复事件。

## 统计、父层与发布仍有的限制

1. Mid stats 的 inFlight 在 C++Dispatch 的 `__finally` 递减，之后仍有 ASM恢复和 RET。该数字说明观察回调窗口，不能独立证明整个桥已离开。生产 owner 的回收判断必须同时等待外层 native void0 parent 的真实在途归零，保留未知状态的责任。
2. 独立 fixture 的 parent 由其自己的 `thread_local Work` 栈组织；它没有把真实 Dobby native void0、broker `PhysicalCurrent` 和这个 mid CALL 串成同一条原生调用链。它的 JSON 明确写 `nativeParentVerified:false`。递归、worker 和解绑检查不能替代“外来／同 receiver 重入／overflow 帧不能继承父 token”的联合验证。
3. fixture 仅在自己的单线程安装阶段改一个 NOP，再启动 worker。它不证明运行中游戏五字节发布安全。固定游戏 `.pdata`、九字节候选、完整五字节 NOP、真实返回 PC 和局部直接分支边的证据见 [mid-site-evidence.json](mid-site-evidence.json)；生产并发限制见 [mid-site-review.md](mid-site-review.md)。
4. pinned Dobby 的 Prepare/Commit 顺序不提供本 CALL opcode 的并发发布证明；对齐八字节数据写也不证明指令获取／预解码同步。游戏中段 patch 应继续保持 unpublished，直到可验证的 native 执行安全点、线程创建竞态处理、缓存发布和未知结果保留均成立。

下一步先读取统一构建与独立 fixture 的真实结果；通过后补 native void0→broker 物理父 token→真实 mid CALL 的联合 native fixture，并证明安全发布，再接实际游戏中段与三个 listener 调用点。执行集合身份、动态变化、取消和所有消费者退休应各自验收，不能用本桥或列表非空作为整体结束证据。
