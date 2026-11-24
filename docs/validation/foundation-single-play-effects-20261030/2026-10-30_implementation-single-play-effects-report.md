# 将精确 Single Play listener 的全部效果转到同 run occurrence

本批实现一类非空 typed listener 的正向效果转移：实际 Single site1 的标准零参 InvokableCall，持有一个稳定的直接闭 UnityAction，精确指向同 run 已接纳的 `TweenInfo.Play()`。实际 Play 整棵调用子树的全部新 InstantWork 和 queue occurrence 进入原 owner 的工作账本后，才签发私有转移凭据。凭据不把任何 occurrence 变成已结束，也不授权释放根。未知 listener 和其它委托形态继续 Pending。

最终 fresh 固定基底 **39/39 隔离检查通过**；其中关键正向实际转移一个 InstantWork 加两个同指针 queue occurrence。原完成事务在队列未逐项退出时拒绝借票，全部实际清除并满足原独立死亡/封口条件后才成功。授权与精确范围见 [scope](scope.md)，没有真实游戏/安装或新 native 发布；完整 B01–B19 集成由 root 的统一源码冻结执行。

## 可复现冻结

| 对象 | 最终实际结果 |
|---|---|
| 正式输出 | `D:/riz-build-cache-next/single-play-effects-frozen-r1-20261030`，`passed=true` |
| 基底 | 固定 V7 core，622文件；manifest SHA `fb90ef1765ca76ea71192899a19a7c23f45bdf40859c43608050551617e95890` |
| 覆盖 | 24全文件、2个前序 Retirement 方法、1个本次 Completion 语句；overlay SHA `9110981a24606be11b083e0d6dc622a8a365aa962e739139eb445910a8347d5b` |
| 最终源码 | 631文件，before/after SHA `94a7d0cc3cbda3e319f98fbc64849af62897d478a46f57181b221490760b52ce` |
| 源码 ZIP | SHA `a21ec969a7e5e555527e033079ee9d03aa273ab507402f5269f3c3ac05efc5de` |
| 构建 | exit0；Runtime/Adapter 0警告，受控测试 fixture 26条警告 |
| 隔离执行 | 新 owner/occurrence 8、新 production reader 3、旧 Completion 12、旧 Typed 16，合计39/39 |
| 最终结果 | [test-result.json](test-result.json)，SHA `a69f8f8fa3c22913ddf488fe6336b0873d2d280171512c22d59008d5bd55a580` |

从仓库根复现，输出目录必须不存在：

```powershell
C:/Python314/python.exe docs/validation/foundation-single-play-effects-20261030/run-checks.py --output D:/riz-build-cache-next/single-play-effects-replay-20261030
C:/Python314/python.exe docs/validation/foundation-single-play-effects-20261030/verify-delegate-contract.py --output D:/riz-build-cache-next/single-play-effects-contract-replay-20261030
C:/Python314/python.exe docs/validation/foundation-single-play-effects-20261030/seal-evidence.py --output D:/riz-build-cache-next/single-play-effects-seal-replay-20261030
```

需要本机 .NET 8 SDK、Python、固定源码基底；静态字节复核另需固定游戏 PE/dump 及 pefile。构建只消费 D 盘 fresh source-snapshot，不消费工作区 bin/obj 或其它 owner 的 live 中间状态。完整命令、每个进程退出码与 stdout/stderr 哈希在最终结果内。封印脚本只读复核已有结果，不构建或执行测试。

## 生产接线与私有转移

`NativeLocalTweenBackend.TypedListenerEffects` 确认实际 backend/owner 和当前同一个 Single child 的读取窗口；`NativeTweenTypedPlayConsumerReader` 从已独立保留的 invokable 发布并 root UnityAction、其 delegate array 引用与 target，再做 metadata 读取。只接纳精确标准类、`delegates=null`、非 virtual/non-generic/non-inflated 的实例 `void Play()`；DeclaringClass、MethodInfo、固定 entry@3956f0、method_ptr、invoke_impl、method_code 与同局 target 都必须吻合。Before 和 Returned 均重新读取并比较完整选定消费者身份。其它形态没有转移票。

`WorkOwner.TypedDispatch` 仍先通过精确 native parent/frame/list/vector/version/index 校验，再记录此 child 的 operation/occurrence 起点。Returned 正常且 owner 已知时，原 source reader 必须成功连续 drain；source reader/root-set/epoch 不得更换。一个且仅一个直接 Play 必须同属 invocation 的实际父；所有新增 operation 都必须在这个 Play 子树中完成、正常返回和 disposal；各 source operation 都必须 committed，且无 pair/transient/move/failure。没有实际 Play、额外直接 sibling、skipped、未 disposal 或 journal 跳号均无票。

每一个新增 occurrence 都要保持原私有对象：Instant 必须来自这棵实际 source operation 的 Instant；queue 必须关联其真实 confirmed admission 和该子树中的 Play。两个相同 Tween 指针的 queue 项保留两个 ID；任何一项不在同 owner/source membership 就不能整体转移。私有 `TypedPlayEffectTransfer` 绑定 owner、launch、确切 child、journal/root-set/epoch/cutoff、完整 operation/source 列表及全部 occurrence 对象；外部 DTO 或任意回调不能签票。

诊断 `Transfer` 只是该私有凭据验证后的投影。`TypedEffectPending` 只有在非空实际 list 的每个 child 都有有效转移、同一个父正常完成并 disposal 后才为空。Completion 只在这一处跳过空原因；原 occurrence 循环和所有死亡/源撤回/队列退出门槛均未改。后来的 listener、source gap、Unknown 或新 occurrence 不能借用另一个 child 的凭据；旧 completion loan 的 generation/cutoff 检查仍拒绝恢复后新增工作。

## 固定 PE 中合法直接闭委托的依据

这条窄合同有真实固定构建依据。已有 `ClosedAction.Constructor@377890..377920` 的144B SHA 是 `b098d64e184cd11738043d57d7602fe598b8bc6ca06364e88d58171f2c368bb9`；本次再次核对了实际 PE 字节和先前全部指令。固定 dump 的 UnityAction 声明明确其 `.ctor` 共享 `377890`。

该构造器在 `3778a0` 写 method_ptr、`3778a7` 写 MethodInfo、`3778ae` 写 m_target；非 static 且 target 非空的闭实例分支 `3778e2..3778ee` 写 method_code=target、invoke_impl=method_ptr。现有 `NativeLocalInitializationCoroutineBackend.RequireDelegate` 与 `NativeLocalCutoutCoroutineBackend.RequireResetAction` 已采用同构门槛。详细输入哈希、关键指令、dump 行号与两个现有生产消费者摘录保存在 [delegate-contract.json](delegate-contract.json)。本次只复核既存范围，没有运行游戏或宣称所有 CreateDelegate/适配委托都采用该形态。

## 实际验证与边界

新增 owner 检查真实执行生产 WorkOwner 的 context、source reader、admission、occurrence 与 completion 算法，覆盖所有 occurrence 转移和逐项等待、Instant 子树、未知/缺 Play、delegate/target/method/multicast 改变、额外 sibling、skipped/实际 disposal、异常、transport fault、真正 EventSequence 跳号、后续 child 隔离以及旧 loan 对新工作的拒绝。关键正向在清除两个 queue 项后取得实际同步 completion interval，未调用 unregister/free。

新增 reader 检查直接执行生产 `NativeTweenTypedPlayConsumerReader` 和生产 NativeRuntime，使用受控 IL2CPP exports、真实 unmanaged 对象/MethodInfo 字节及不执行代码的 SEC_IMAGE fixture。正向实际独立持有 invokable/Action/target 三根，核完整 metadata/entry；14种不匹配形态与读取中途 chain 改变不被分类。它不是只检查 DTO，仍不等于生产游戏对象或完整 NativeLocalTweenBackend 初始化的运行验证。

原 Single 生命周期的65 managed/8 native operation/17旧定向结果由前序单独冻结，本轮没有重跑或把它们并入39。生产 native Single producer → Delivery/ReadCurrent 的游戏对象正向、任意 listener 的全部效果、setter/coroutine 的结束和 future membership writer closure 没有在本包冒称完成。

## Evidence → Finding → Path

| Evidence | source_ref / artifact_path | 观察与重现 |
|---|---|---|
| E-static | [delegate-contract.json](delegate-contract.json) | 固定 PE 144B及每条既存指令字节、输入 hash、UnityAction 共享 ctor、两个现有生产消费者；运行上面的 contract 命令 |
| E-reader | [logs/AudioContent.36.stdout.log](logs/AudioContent.36.stdout.log) 至 `.38` | 三个独立进程直接执行生产 reader/NativeRuntime，实际 `1/1`；运行上面的 run-checks 命令 |
| E-transfer | [logs/AudioContent.00.stdout.log](logs/AudioContent.00.stdout.log) 至 `.07` | 八个独立进程执行实际 owner/source/occurrence/completion；`.00` 确认全部三项转移并逐项等待；同一 repro |
| E-regression | [test-result.json](test-result.json)、`.08` 至 `.35` 日志 | 28个旧 completion/typed 检查，各自 `1/1`；同一 repro |
| E-freeze | [overlay-inputs.json](overlay-inputs.json)、[source-before.json](source-before.json)、[freeze-seal.json](freeze-seal.json) | fresh631源码与全 owned 覆盖哈希无漂移；运行 seal 命令 |

逐文件 content_hash、observed_at 和 source_ref 见 [evidence-index](evidence-index.json)；所有 Evidence source_type=file/log，linked_workitem=exact Single Play listener transfer，supersedes=none，未将静态证据写作运行验收。

F-consumer：severity=n/a_re，category=design，status=validated，confidence=high，evidence_ids=[E-static,E-reader,E-transfer]；location=Backend.TypedListenerEffects/NativeTweenTypedPlayConsumerReader；impact=固定合法闭实例 Play listener 可正向进入专用效果转移，未知形态不继承其合同；repro_steps=运行 reader 三项和 transfer正向；remediation=本包已接线；residual_risk=真实游戏委托尚未运行。

F-occurrence：severity=n/a_re，category=design，status=validated，confidence=high，evidence_ids=[E-transfer,E-regression,E-freeze]；location=WorkOwner.TypedListenerEffects 与 Completion 的条件追加点；impact=全部实际 occurrence 保留在同 owner 的原 completion账本，重复指针不合并，native return 不能结束工作；repro_steps=核 `.00` 的清除前拒绝/逐项清除后借票与 `.06/.07` 的代次隔离；remediation=已实现私有转移凭据；residual_risk=其它 listener/效果类型仍 Pending。

P-transfer（path_type=callflow）：start=实际Single site1 Before，goal=全部新增工作获得同owner authority并继续等待；steps=(1)独立root和精确direct闭委托读取，E-static/E-reader，F-consumer；(2)同child实际Play及其全部source子树完成，E-transfer，F-occurrence；(3)每个新增Instant/queue occurrence进入私有转移列表，E-transfer，F-occurrence；(4)非空全部child转移后停止追加该typed原因，E-transfer/E-regression，F-occurrence；(5)原completion逐一等待全部occurrence退出及其它独立门槛，E-transfer，F-occurrence。residual_risks=未来writer/任意listener/实机整局仍由独立任务处理。

## 冻结交接

本次生产修改仅六个完整文件和 Completion 的约定语句；完整 manifest 同时包含前序必要依赖，不代表全部27个 overlay条目均为本次新增。root 已收到最终路径与 SHA，可取同一 live字节做统一全树冻结。本包仅补文档，不再修改生产与测试源码。诊断 r1/r2 的失败和 r3 的成功都保留，未用旧2233矩阵代替本次验证。
