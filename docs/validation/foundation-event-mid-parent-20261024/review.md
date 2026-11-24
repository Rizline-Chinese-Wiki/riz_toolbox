# 联合 parent／mid 契约只读复核

本页审查真实 Dobby void0 parent、fixture 自有中段 CALL、mid bridge、broker 当前物理 token 和 listener ledger 的联合契约；范围以 [scope.md](scope.md) 为准。已读取两实施代理落盘后的 ASM/C++和 Audio 联合测试，以及 root 运行的首四项和新增两项冷 owner 隔离定向日志；六项均已通过。审查者未修改 native／Runtime／Adapter／测试或构建脚本，未编译或操作游戏。全量构建结论与生产游戏验收仍另行判断。

## 固定地址和 Dobby 边界

新增 fixture kind 4 应有独立 `RtVoidEntry4`；其前 32 字节为入口 detour 的 NOP 区，中段完整五字节 NOP 固定在 `entry+0x13B`，CALL 的真实返回 label 为 `entry+0x140`。ledger 采用的测试 module base 可由 `entry−0x15642E0` 推导，使 ledger 固定检查的 `module+0x1564420` 对应这个真实原生返回 PC；这只是测试地址映射，不代表 fixture 具有真实游戏 metadata。

真实 Dobby 准备的 original 应仅迁移入口小段，再回到原 target 后续 native body。联合验证必须分别确认：原 32 NOP／五 NOP；首次自身页 CALL 发布的 rel32、near tail relay 和邻接字节；parent Prepare→Configure→Commit 后中段五字节仍在原地址、回调 ReturnPc 仍为 `entry+0x140`。不能只看 callback 有发生，因为被入口 relocation 搬走的中段可能产生另一个返回 PC。预留 32 NOP 是布局前提，不能替代对实际 patch 结果和真实回调 PC 的检查。

fixture 中段先发布、parent 随后安装，均必须发生在这个独立 target 首次执行前且没有并发 worker。此安装顺序可验证联合执行链，不验证生产游戏 live patch。真实游戏并发屏障仍未解决，延续 [生产发布计划](../foundation-event-mid-20261024/next-publication-plan.md) 的 unpublished 限制。

当前 [Entries.asm](../../../native/RizToolbox.InitializationAbiFixture/Entries.asm) 已将全部 kind-4 body 放在同一个 `PROC FRAME` 内；[Fixture.cpp](../../../native/RizToolbox.InitializationAbiFixture/Fixture.cpp) 的首次配置验证准确偏移、32 NOP、五 NOP、helper 可执行地址、near relay 位移／目标、写后邻接八字节与首 32 字节。认领后的失败保留 publication=-1 与 relay/list责任，后续 kind-4调用拒绝。这个 exported fixture入口只供首次自身页发布，不是能防恶意并发调用的生产安装器。

[NativeUnityEventMidIntegrationTests.cs](../../../tests/RizToolbox.AudioContent.Tests/NativeUnityEventMidIntegrationTests.cs) 在 parent binding 前验证 E8 的位移指向 `FF25` relay 和完整 helper 指针，binding 后再次确认原前缀已变而五字节中段 CALL 不变；回调还精确检查真实 `ExpectedMid=entry+0x140`。四项定向执行均经过该 fixture构造和 callback检查，证明本次实际 Dobby入口 relocation 没吞掉中段；这不推及其他映像布局或游戏安装器。

## 原生调用、异常和物理窗口

kind 4 的 ASM wrapper 应有真实 FRAME/unwind，保存并建立 RSI=self、RBP=fixture actual list，使用真实 CALL→relay tail JMP→mid ASM→C++Dispatch，再恢复返回原 wrapper 后调用 C++ `VoidBody(4)`。原生 original 仍由 Dobby parent helper 调用一次；native body 的 entered/exited 计数只能证明后续 body 到达一次，不能冒充中段回调时它已经进入。中段当下的物理窗口必须直接读 parent native inFlight。

原函数 SEH/C++异常应从 native body 经 fixture ASM、Dobby original、native void helper 展开，最终由 native catch 导出状态和原 payload。managed capture／mid／after 观察者不得接管 original，不得让异常经过 CLR delegate 的 original 调用，也不得将 catch 后的状态伪装为正常 After。worker 由 native 线程进入同一条 patched chain；只允许 scalar identity／fault lane，不能在 wrong-thread 分支读取对象图或建立 IL2CPP根。

mid stats 在 C++Dispatch 结束便归零，ASM仍可能恢复；而 parent native inFlight 必须覆盖整个 original 和 native Exit。最有力的联合断言是：worker 已通过中段，随后在 `VoidBody(4)` 内阻塞，此时 mid inFlight=0、parent inFlight=1，binding／ledger都不得判定 native idle。该断言比只在 callback 中同时看到两个计数大于零更接近真实回收边界。

当前联合测试已实现这项 worker post-mid block 断言，同时验证 graph读取数不增加且 worker 无法取得主线程 current context。ASM实际保存 RBX/RBP/RSI并分配 `0x30`，CALL前 RSP模16为0，self/method位于 shadow以外的 `+0x20/+0x28`。中段之后才进入 `VoidBody(4)`，其 `returned` 字段在 body入口累计，测试不把这个字段当函数正常返回证明；SEH/C++的最终状态另核 native catch 的异常类别、code、原 kind 和 payload。

## token 与证据强度

mid 必须经真实 `NativeEventMidContextBridge` 把完整只读 frame 交给 Binding，从当前线程最内层原生 parent 取得该 sink 自己的 `InvokeContext` 和 physical identity，再送 `ObserveInlineList`；不能从 receiver 搜索旧 context、拿 synthetic frame 代替此帧，或把 fixture 的 TLS 工作栈当作 broker physical top。

外来 receiver／method、worker、同 receiver 重入、入口／出口观察者再入、解绑和 overflow 遮罩的语义已有受控回归，但联合新案例仍应明确实际覆盖了哪些。仅旧 capability 被拒绝的检查必须实际跨越一次 context generation 复用；不能只改整数制造“过期 token”。同一个 HGlobal list 可以跨调用复用，但每次 ledger snapshot 的 token、generation 和真实 ReturnPc 归属必须独立。

实际机器 CALL、Dobby parent 和 frame/token 归属可以由本联合 fixture证明；列表对象图、采用／root 操作仍是受控 backend 协议，不是实际 IL2CPP类型或 GC handle。`InlineExecutionUnobserved`、`DispatchPathVerified`、`MutationClosed` 的原限制保持，不从内联列表观察推出 listener 已执行、来源已封口或资源可退役。

只读审查时指出的一项测试计数问题已经关闭：六次 physical original／probe 中，foreign MethodInfo一帧在 broker层拒绝 context捕获；其余五次包括 foreign正常method的mask context，应该有五次 capture／After／scope退出。当前测试以每次 ProbeEnter保存独立 context槽，在 ProbeExit检查该次 capture实际返回的对象已经 Dispose，避免从最后一个 owned context 的状态推测所有 frame清理。这是测试计数和证据强度修正，未发现要求改生产 native helper 的确定错误。

## 六项定向日志与实际覆盖

[audio-targeted-first.log](audio-targeted-first.log) 记录首四个独立 `1/1 audio/content checks passed`，名称逐项对应注册的四个 case；它们没有走被跳过的测试分支。日志 SHA256 为 `484c1aa58a2e241f6adc9947d198e980111b2b88c350b3165ad368c56116e84d`，当时四 case 的测试文件 SHA256 为 `0de522204201228a783ae85dc66c3a5e7cdc13a582c4ce720d439a659bc08abc`。

[audio-cold-targeted.log](audio-cold-targeted.log) 记录新增 SEH／C++ 两个干净 owner 各自的 `1/1` 通过，SHA256 为 `3782c9b85fd8691aa85c305b98e6a21e68cd43446b8d74c7b6a7beb68b4daafb`。当前包含六 case 的测试文件 SHA256 为 `1f394528cfc6523cc516f2a19b031004a33169aed89c6172c44efa6d9e2bdef9`；它们通过真实 native chain 执行 `ColdOriginalFault(1/2)`，补齐下面单独 adoption-fault case 的前置 Unknown 限制。

| 定向 case | 对应已运行的关键断言 | 结论边界 |
| --- | --- | --- |
| `NormalAndRecursion` | 真 parent caller／MethodInfo／mid PC；两项和空列表采用；代次 token变化且旧 snapshot不变；foreign receiver和foreign method各跳过自身中段采用；原生 original／continuation／probe各6次，实际context Capture／After／Dispose各5次，owned inline采用4次；最终父／mid均0且owner已知但Pending | 可证明此 fixture真实嵌套原生链的token归属和controlled list记录；不证明真实Unity listener执行 |
| `WorkerAndDetach` | nativeworker经过mid后阻塞：mid0、parent/binding/probe各1、owner不能idle；worker拒绝current且graph读取数不增加；主线程callback内解绑后物理在途仍1、已有scope／probe继续退出；随后original继续而不再采用／交付旧观察 | 覆盖实际父层尾部窗口；没有在每条ASM恢复指令上暂停，不虚称逐PC全窗口验证 |
| `AdoptionAndNativeFaults` | controlled采用责任先记再故障：保留1条list责任、不发布Inline、ownerUnknown/Pending；随后真实SEH和C++由nativecatch核kind/code/payload；共3次original/continuation、2次abnormal probe exit、parent物理0而binding sticky Unknown | root责任是controlled计数，不是IL2CPP GC handle；本case异常路径发生在owner已经Unknown后，健康owner路径由新增两冷case独立验证 |
| `StaleIdentity` | 两次真实同frame原生调用重用同一InvokeContext，current token不同且新identity有效，旧identity在新代次被拒于采用前；两次original／continuation／probe／scope均成对，Inline采用0、ownerUnknown | 证明这一实际context复用路径拒绝旧capability；不替代全部overflow／错线程／入口出口再入的联合native矩阵 |
| `ColdOriginalFault(1)`／SEH | 首次调用前owner非Unknown、Inline采用0；实际mid成功采用两行list，保留真实token／mid PC／parent caller与1条受控root责任，backend无CleanupUnknown；随后真实original SEH由nativecatch验证code/kind/payload；original／continuation／capture／scope／probe均1，abnormal probe1、成功After0；Inline保留，ReturnedInvocations0、Post空、旧identity无效，物理父／mid均Known/0但binding／probe sticky Unknown/0 | 证明此前健康owner在真实原始SEH前已成功采用Inline，异常后不发布成功或丢失受控责任；仍非生产GC或游戏验收 |
| `ColdOriginalFault(2)`／C++ | 在另一个独立干净owner／隔离进程执行同一组成功采用与精确清理断言，原始C++异常由nativecatch核 `0xE06D7363`、原kind及payload；不借SEH case或adoption-fault case的Unknown状态通过 | 覆盖真实C++展开后的成功采用记录保留和失败关闭；不扩展成所有监听派生任务和取消路径已完成 |

[首轮全量日志](build-first.log) 保留新测试缺少 Runtime using 导致的 CS0246，并记录两处 nullable警告；对应 using与引用标注已经修正。两份定向日志各有17条既有测试警告，未声称全仓零警告。首轮编译失败不隐藏，也不能把六项定向通过代替最终全量结果。

## 当前状态与下一步

当前完成源码契约复核及六项真实定向结果核对：固定 `+0x140`、Dobby后中段位置、真实 native窗口和异常catch、worker／detach／旧generation身份拒绝，以及健康owner成功采用后遇真实SEH／C++的责任保留和异常清理，均已在本fixture范围得到执行证据。root的最终全量构建仍按实际终态和汇总另行记录；生产安全发布、实际 IL2CPP graph/root、三个listener调用点与完整退休仍为后续工作。
