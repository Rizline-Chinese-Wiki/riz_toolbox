# 固定四入口最小 publisher 审查

2026-10-25。**最终独立构建和验证 session 5523 已实际 exit 0，57/57 子进程通过；有限审查未发现阻断该自有组件范围交付的缺陷。** 已只读核实预冻结、当前和 ZIP 中的 12 项声明源码以及 4 个二进制归档逐项一致。本组件尚未接入 Build.ps1、早期 loader 宿主或游戏；前批完整构建 99557 的 25/1617、576 源／22 包是独立历史证据，本批没有重跑该完整矩阵。

## 角色、范围与方法

审查者编写了 `FixedOriginals.asm`、`FixedTargets.asm`，对它们的检查属于作者自检，不称独立第三方审计。对其他代理的 `FixedPublisher.cpp`、宿主、父 runner 和 [Python 验证器](verify-fixed-publisher.py)做有限交叉审查，并只读实际 PE/函数表、receipt、事件、原始 CONTEXT/栈与进程日志。审查过程中没有构建、运行 fixture、安装、游戏或 UI 操作。[实施范围](scope.md)保持。

本组件只支持单独命名的自有 `RizFixedOwnedTargets.dll`、固定只读 marker 和四个精确前缀。它没有通用解码、复制 text、Dobby、动态路由或通用函数接入能力；固定 original 是编译期 MASM 与静态展开记录。发布器 PE 的无 TLS、无 delay import、无 CRT/Dobby/堆/迟到加载 import 已由独立验证器检查；这只是组件导入表证据，不等于完整静态/间接调用和 OS 内部依赖闭包已证。

## 最终凭据复核

| 原件 | 本审查实际核对 |
| --- | --- |
| [终态](component-run.json)、[wrapper](component.log.run.json)、[摘要](build-summary.json)、[构建日志](build.log)、[测试日志](test.log) | session 5523、exit 0；两日志 hash 与 root 记录及 wrapper 完全一致；不是此前预检终态 |
| [源码 manifest](source-manifest.json)、[预冻结](component-inputs-before.json)、[ZIP 核对](source-archive-check.json)、[输入一致性](component-input-integrity-check.json) | 12 条文件名集合及逐项 hash 在预冻结、当前文件和 ZIP 一致，testzip 无损坏；ZIP SHA256 `abdbc27706ccf070363ae0d51b2b40c38f254e644d6d86b18e1a48d045fcbd8c` |
| [二进制 manifest](binary-manifest.json)、[归档核对](binary-archive-check.json) | 4 条逐项 hash、精确文件名集合及 ZIP 完整性通过；ZIP SHA256 `8fc49eb6f7a1bad4ea63fdfa2f5ed15bacb08dfae25c0dbcfb82f2022c40dd8a`；父进程持续持有的四文件前后 hash 未变 |
| [正式矩阵](fixed-publisher-evidence.json)、[完整原件](raw/cases/fixture-result.json) | 两份 JSON 完全一致；57/57，宿主检查 2560、receipt 父断言 11522、unwind 父断言 25878，另 normal calls 父断言 271，计数分列 |
| [normal 实调](raw/cases/normal/case.calls.jsonl)、[父核对](raw/cases/normal/calls-proof.json) | 34 原调用、20 对观察、16 捕获，包含八个发布前异常基线；50 个受控展开 PC 与真实调用证据分列 |
| [normal 页快照](raw/cases/normal/case.memory.bin)及各 fault 同名原件 | 每进程 240 字节，57 份共 228 个目标快照；本审查逐项重算实际 32 字节、AllocationBase、地址和页面保护，含共享页 outstanding 影响，全部一致 |

57 进程共运行 2850 次受控 `RtlVirtualUnwind`。构建日志 SHA256 为 `07df9bad2b6759eb3312cced49672a82fdd4c5074ce9d56d97e1916585273385`，测试日志 SHA256 为 `8adc98142f15c75286bfad614540c342a00ca62aa62fdd55d899dae5b9c04e2c`。

## 已核实的实现

- 四项目标身份、实际非 NOP 前缀和静态 original 尾序列全部验证后，才绑定四个永久 continuation；四个 helper 通道的真实配置身份全部核对后，才允许依序 Commit。无重试、reset、卸载或源字节假回滚接口。
- 每次 Commit 分别记录保护修改、源写尝试、已写字节、回读、保护恢复和缓存刷新。发生过源写尝试或仍有未恢复保护责任时，终态保持 Unknown；零源写且已成功恢复保护可为 DisabledNoSourceWrite，仍保留永久 Prepare/Bind 责任。
- original 精确重放 Start 的三次 home-slot 保存、Execute 的四 push/`sub rsp,178h`、Release 的一次 DEC/CMP 与 Detach 的保存/清零。Release continuation 第一条 SETZ 消费实际比较标志，实调核对计数只减一次。Execute 保留完整 uint32 EAX；Start/Release 为 uint8，Detach 为 void。
- 尾序列改为 `MOV R11,[RIP-relative continuation] → JMP R11`。R11 是本合同明确允许改变的易失寄存器；没有承诺所有 GPR、任意 flags 或任意机器码透明。四类 typed ABI 与非易失寄存器、栈和原异常是分别验证的事项。

## 必须保留的真实失败史

1. 初始 FF25 original 尾跳候选即使把跳转纳入 SizeOfProlog，也在实际 `RtlVirtualUnwind` 中失败：[46 个受控 CONTEXT](history/fixed-publisher-normal-first-20261025/normal.unwind.jsonl)中，Execute +18、Release +16、Detach +15 三项返回 RIP=0、RSP 仅加 8，非易失寄存器未恢复。主代理禁止该候选进入源发布，不能把这轮失败删除或改称通过。
2. 改为 R11 尾序列后，实际 50 个指定 PC 展开通过。随后一次宿主执行因把 SEH `ExceptionFlags` 假定为 0 而报四项失败；该断言错误不是由屏蔽 flag 修复。
3. 最终宿主增加八次真实发布前异常基线，逐 kind 比较发布后 SEH/C++ 观测。已读首次成功矩阵中，四个 SEH 基线和四个 hooked SEH 的原始 flags 均为 `0x80`，没有 mask 或改写为零。C++ 以实际 typed catch 的 payload/kind 验证，没有冒充已捕获原始 SEH record。
4. [前几次编译与预检记录](history/observed-runs.json)保留三次实际编译失败、上述两次 normal 红和首次 57 矩阵 session 7435。7435 未同时预冻结全部源码与验证器，也没有后故障物理快照，不能作最终凭据。最终使用另一次独立冻结、重建和带物理快照的 5523；不能把前批 25/1617 当成这些新源已纳入统一构建。

## 证据应怎样解读

最终矩阵为 normal 加 14 个 fault stage 各四 kind，共 57 个进程。每个进程有 50 个受控展开 CONTEXT；normal 另有 28 次外层实调，包含八次发布前异常基线及 20 次发布后 normal/worker/异常调用，展开 Start 的同步子调用后共 34 次原调用、20 对观察、16 次异常捕获。这些数字已经用预冻结后原件复核。

受控展开检查使用实际 PE/RUNTIME_FUNCTION/UNWIND_INFO 和真正 `RtlVirtualUnwind`，父验证器独立重算全部受控栈字节、PC、八个非易失寄存器与返回栈。50 个 PC 覆盖四个固定前缀的指令边界、original 尾 MOV/JMP 和 target continuation 首 PC；**没有覆盖目标后半及全部 epilog PC，也不是每个 PC 实际触发异常**。普通 C++ caller 的实调未另加八个非易失寄存器调用前后哨兵，不能把构造 CONTEXT 的八寄存器证明改名为全部正常实调的独立寄存器快照。

| 故障 | 已执行的事实 | 没有得到的结论 |
| --- | --- | --- |
| 步骤前后故障 | 在保护、源写、回读、恢复、刷新边界受控中止，保留先前全部责任 | 不等于随机任意中断位置已验证 |
| partial write | 实际循环只写三字节并记录三字节；终态 Unknown，不执行该目标 | 不等于任意写到一半时的硬件故障或异步异常 |
| protect/restore/flush API injection | 跳过所选 API，以受控结果进入失败通路；`ApiCalled` 和 attempts=0 明确标出 | 不等于 OS 的 VirtualProtect/FlushInstructionCache 真正返回失败 |
| restore injection | 源字节已写，保护责任仍为 outstanding，Unknown 保留 | 不宣称保护已恢复或源已回滚 |
| 失败 receipt 与实际页快照 | 父独立检查事件/计数责任；每个进程终态后另保存实际 32 字节及 VirtualQuery，按真实 system page size 处理同页目标受到的保护影响 | snapshot 是所记录时刻的实际状态，不是随机任意中断过程录像；receipt 的 readback 前 `observedBytes` 仍可为零，不能拿它替代物理快照 |

实际正常、worker、同步嵌套和 SEH/C++ 执行，与构造 CONTEXT 的展开证据分开报告。首次矩阵不含物理快照的 receipt 计数是历史值；最终57进程的 receipt/unwind 父断言为 11522/25878，normal calls 为 271，不是新的 1617 回归计数或能力覆盖率。

## 验收边界与下一步

当前未在 loader lock 下运行此 publisher，也未证明它早于 TLS/CRT/首调用或已有线程。helper 和宿主仍有各自 CRT/初始化依赖，不能把最小 publisher 的 PE 条件自动扩展到它们。本批没有签发真实 UnityPlayer 首次安装、V4、系统 WinHTTP 转发、coordinator 对象/GC、scheduler/async/Start 内联清理、全部来源/消费者封口、取消完成或整局退休凭据。

下一步仍需补生产所需的依赖闭包和自有早期宿主接线；扩展到固定游戏入口前，还要验证真实前缀、上下文和最早调用历史。完整 B01–B19 不缩小，`EngineCancelled=false`、Pending 保留、RequestPlay 关闭；本组件交付不能称底层全部完成。
