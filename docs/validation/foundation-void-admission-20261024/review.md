# Void admission 有限只读复核

本页记录 native gate、typed lease、真实 bridge 接线与 root 新增实际 lease 回归的有限审查，范围以 [scope.md](scope.md) 为准。已读取 root 最终 [build.log](build.log)、[build-summary.json](build-summary.json) 和 [native gate结果](native-build/void-admission/fixture-result.json)：25组共1507/1507，Runtime159/159、Hooks164/164、Audio573/573；本审查者未运行编译、测试或游戏，也未修改源码、native、测试或脚本。通过结论限于这些离线断言，不扩大为游戏验收。

## ABI 与原生窗口

[VoidContext.h](../../../native/RizToolbox.JudgeContext/VoidContext.h) 在原有 Frame/Config/Stats 后附加 Receipt32 和 GateStats40，没有改动旧布局：receipt 绑定 slot、OS thread、全局非零 epoch，stats 区分 Active、Parked、Epoch、Phase和Unknown。四个新增 export 的参数与 [Bridge.Admission](../../../src/RizToolbox.Runtime/Hooks/NativeVoidContextBridge.Admission.cs) 的 Cdecl delegate、顺序布局和大小检查一致。

[VoidContext.cpp](../../../native/RizToolbox.JudgeContext/VoidContext.cpp) 的每槽 SRW/CV 锁只用于准入、停车和计数，锁不跨 Enter/original/Exit 或 CLR 回调。原物理 inFlight 在停车前递增，直到退出路径末尾才递减；Parked 不进入 managed Enter，也不计入 Active。Active 与 TLS 深度贯穿 Enter、唯一 original 调用及 Exit 返回。Close 拒绝当前 OS线程在任意 void 槽已有准入活动；关闭中已准入线程的同槽同步重入继续计 Active，避免等自己原调用结束时被递归停车阻断。

Close 仅在 Closing 已排空 Active 后发 Closed凭据，允许 Parked 非零；未签发的超时会重开和唤醒。已签发后的错误 receipt／wrong thread／stale epoch／Unknown 不开门。全进程 epoch 避免同一线程把一个合法槽 receipt 改成另一个已关闭槽却误中相同局部 epoch。普通 observer故障继续按既有 transport fault计数，admission wait／counter故障另记 sticky Unknown。

## 租约根、写入与错线程

[NativeVoidAdmissionLease](../../../src/RizToolbox.Runtime/Hooks/NativeVoidAdmissionLease.cs) 的构造器私有，Close 前先把 lease 加入静态 Owners，间接持有真实 protocol、bridge、native callbacks和DLL。Close 返回未知、验证丢失、Release 未确认或未来写入结果未知时，责任保持进程持有，不由 Dispose或finalizer重试／开门。已验证且尚未写入的 owner线程才可 `ReleaseUnmodified`，成功后只释放一次。

`MarkWriteAttempted` 先验证精确 receipt，再设置本地一次性标志；它不实际写代码、不授予通用patch能力。标志设置后即使 Dispose仍不Release；`RetainUnknown` 进一步禁止验证与释放。`VerifyClosed`、`ReleaseUnmodified`、`MarkWriteAttempted` 的错线程调用在到达 native protocol前即拒绝，不消耗有效owner凭据，也不因别的线程尝试而把原owner污染为Unknown。

真实 bridge 的 `ReadActivity` 使用 `max(旧完整物理inFlight, Active+Parked)`，并合并 native GateStats.Unknown。它不会把一个已关闭门前停车的真实native调用误报为物理归零；lease则根据独立 Closed/Active0/epoch凭据判断局部准入静止，不能借普通 ReadActivity=0代替这张凭据。

## 已修正的未知状态传播缺口

初稿在取得 lease 后，后续 Verify／Release故障和 `RetainUnknown` 只更新 lease本地状态，实际 bridge仍可能报告Known。当前修复在内部 protocol添加 `ReportUnknown`：`MakeUnknown` 先固定本地Unknown和首次failure，首次转换时才通知；真实adapter仅原子设置bridge `_unknown`，不访问native对象、不调用native Release或broker Gate。通知者自身异常被包含，不能清除本地状态、释放根、开门或触发第二次通知。

[受控 lease 回归](../../../tests/RizToolbox.Hooks.Tests/NativeVoidAdmissionLeaseTests.cs) 覆盖以下检查：Close未知／Release或验证丢失均通知一次；重复RetainUnknown不二次通知；reporter自身抛出时保留Unknown并限制failure长度；仅MarkWriteAttempted不虚报未知；错线程拒绝不发送未知通知。最终日志明确记录全部11组lease协议案例通过，属于Hooks164项的一部分。

## 真实 bridge 集成检查的范围

root 在 [NativeVoidAbiRegression.VerifyRealAdmissionLease](../../../tests/RizToolbox.Runtime.Tests/NativeVoidAbiRegression.cs) 复用已经真实Dobby安装的parent及私有native协议：

| 代码中的检查 | 可以验证的实际边界 |
| --- | --- |
| managed capture 内尝试Close被拒；另注册一个真实boundary probe，关闭闲置parent后启动worker，original计数不变、probe Enter/Exit均0且物理inFlight为1 | 同一真实bridge遵守原生self-close拒绝；停车在broker真实CLR probe之前发生，并继续算物理在途，避免靠原逻辑observer已detach制造空计数 |
| worker线程尝试Release失败，owner仍非Unknown且凭据有效；第二Close拒绝；正确owner只Release一次，worker随后original一次、probe Enter/Exit各1并归零 | OS线程绑定、busy凭据保留、唤醒后原函数一次，以及实际CLR probe成对重新进入 |
| original内阻塞时20ms Close被拒，resume后probe Enter/Exit累计各2并归零；再次Close取得更大epoch | native已准入窗口未被提前签发，已知超时可自动重开并继续进展，probe不丢失或重复 |
| 最终真实lease只记录WriteAttempted，不写任何instruction；Dispose不释放，随后RetainUnknown使Bridge.ReadActivity变Known=false | 未知责任从真实lease贯通真实bridge；不证明任何代码写入或取指发布安全 |

该回归在前面的观察binding已逻辑解绑后继续使用进程持有的nativeparent，并在实际lease检查开始前额外注册有计数的boundary probe。新probe仍处于active状态；Closed停车期它的Enter/Exit为0，放行后才各1，超时case结束后累计各2，因此不再只靠“旧observer已解绑”推断停车不交付CLR。最终日志的 `native void0 preserves real Dobby arguments, exact tail-relay caller, foreign recursion and probe snapshots` 运行到包含该方法的整个case通过，结合已复核的直接调用链确认这些加强断言实际执行；native gate位于Enter前的代码和独立native Enter计数测试提供互补证据。最后那张已标记writeattempt／Unknown的真实receipt不被释放，保留状态仅限其离线测试进程。

[首轮wrapper日志](build-first-wrapper.log) 已保留，其中测试与Package输出到末尾；root记录随后wrapper命令尾部的PowerShell转义错误导致外层退出1。该日志不能被标为完整命令成功，也没有包含最新probe加强的执行验收。第二次完整构建已终态退出0；最终日志SHA256 `b14808538861beecf1e0685e43b17ddd8ecdc904543564c5cb754780b5d1bf13` 与汇总一致。

## 独立 native gate 执行结果

[fixture-result.json](native-build/void-admission/fixture-result.json) 由真实Dobby Prepare／Configure／Commit运行，记录656次检查、0失败、4次成功Close、3次超时、3次self-close拒绝、1次关闭期间递归、7次parked见证；最终Active0、Parked0、epoch6，entered/exited各15、inFlight0。它核验错误release不放行、已知超时重开、original一次和SEH/C++保留；结果自身明确 `initialInstallSafetyVerified:false`、`gameRuntimeVerified:false`。JSON SHA256为 `dc8bf4aa698b73135f09a444eacd61dbbd46463d63da90b34b8804819662b294`。

这些656个native断言与1507项managed／package汇总按不同计数体系分别记录，不相加制造另一个总通过数。最终汇总的compilerDiagnosticLines为空仅描述本次完整构建日志，不能推出所有项目一次干净重编译永远无警告。

## 尚未证明的范围

没有强制触发操作系统 CV wait失效、真实计数溢出、epoch耗尽或线程／进程被外力终止；这些分支目前只有失败保留代码与协议检查，不能声称都经OS故障注入验收。跨槽关闭之间的任意依赖环也不由“同槽同步递归可通过”自动解决，调用方必须避免从已准入活动发起close，并对后续多门编排另行证明。

本协议仅约束已安全安装parent之后的original准入。当前Loader初始屏障／首次parent patch、生产mid指令并发发布、真实IL2CPP图与GC、全部监听派生工作取消和run退休仍未证明，游戏mid保持未发布。本文已把最终native／managed证据与源码边界对应，不能据离线通过关闭这些后续责任。
