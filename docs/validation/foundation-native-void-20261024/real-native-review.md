# 真实原生 void0 回归边界复核

2026-10-24，只读复核。范围见 [scope.md](scope.md)。本页核对四条测试的实际断言，不把离线原生结果视为游戏全流程证明。日志 [build-first.log](build-first.log) 明确记录四条 PASS 及 Runtime **159/159**；该次完整构建随后遇到 Audio fixture 编译问题，因此这些局部结果不能证明该次全量构建成功。最终全量结果以本批次后续构建日志及汇总为准。

实际调用链为 MASM caller → tail relay → Dobby replacement → native helper → Dobby trampoline → C++ original。原函数没有经托管 delegate 调用；SEH 与 C++ 原异常由外层 native caller 捕获。测试与 fixture 路径分别是 `tests/RizToolbox.Runtime.Tests/NativeVoidAbiRegression.cs`、`native/RizToolbox.InitializationAbiFixture/Fixture.cpp` 和 `Entries.asm`。

| 测试 | 实际断言 |
|---|---|
| `Observe` | 完整 64 位 Self/MethodInfo 和 null MethodInfo；caller 必须等于独立汇编返回标签且处于 fixture 映像内；capture 在 original 进入前，after 在 original 退出后，after/Dispose 仍处于 native inFlight ≥ 1。两次递归分别使用 foreign Self 与 foreign MethodInfo；前者由观察者自己拒绝持有，后者跳过 capture 并触发 scalar fault，原调用均各自发生一次。主 probe 按 Self/MethodInfo 栈配对共 6 次进出；另一个 probe 在 Enter 中 detach，仍收到本次快照对应的 Exit，后续不再进入。|
| `UnwindAndWorker` | 正常、原生 SEH、原生 C++ 三路径均执行 original 一次；只有正常路径调用 after，三个上下文都 Dispose。异常路径的物理 helper 排空，broker 观察权保持 sticky Unknown。真正 CreateThread original 阻塞时 inFlight = 1，逻辑 observer/probe detach 后仍为 1；恢复后 worker 收到原返回、已进入的 probe 仍配对退出，后续 detached 调用不再投递。worker 不 capture 主线程上下文，并产生 fault。|
| `ManagedFaults` | 分别令 capture、after、Dispose 抛出托管异常，再验证正常调用及会抛异常的 fault handler。每次 native original 仍执行一次、物理 inFlight 最终为零；有上下文的调用共 after 3 次、Dispose 3 次。foreign MethodInfo 不 capture，即使 fault handler 抛出也不改变 original。这里只断言至少一次 fault 通知，并未逐条核对所有 fault 文本或计数。|
| `NativeCallbackFaults` | 独立 target、helper 固定 slot 31，直接配置原生 Enter/Exit callbacks。检查 Dobby Prepare 未改入口、Configure 未提前发布、Commit 改写入口；错误版本/长度/空 original 被拒绝，已配置槽不可再配置，清空调用端 config 不影响 helper 的副本。每次检查 callback 帧、Self/MethodInfo、实际汇编 caller、user、token、flags、原函数一次与 helper 完整计数。|

原生 callback 测试是 **3 × 7 = 21 个组合**：original 为正常 / SEH / C++，callback mode 为 `0, 1, 2, 4, 8, 5, 10`，分别表示无故障、Enter SEH、Enter C++、Exit SEH、Exit C++、Enter/Exit 同为 SEH、Enter/Exit 同为 C++。它覆盖 selected combinations，并未穷举全部 16 个 callback bitmask。正常 original 的 flags 为 Returned `1`，异常 original 为 Abnormal `2`，Enter fault 叠加 `4`，因此实际断言值为 `1/2/5/6`。Enter fault 时 token 为零，其余为 `0xFEDCBA9876543210`。每轮检查 entered/exited 递增且相等、inFlight = 0，以及累计 enterFaults/exitFaults；Exit callback 自身故障不能改变正在传播的原异常。

原异常 receipt 明确断言 SEH code `0xE04F494E` / C++ code `0xE06D7363`、exception kind、original kind `0x5600 + fixtureKind`、payload `0xBADC0FFEE0DDF00D XOR fixtureKind`。因此这里验证的是 fixture 原异常的标量身份保真；没有宣称验证游戏中的任意 C++ 对象内容或全部 Windows unwind 情形。

Evidence → Finding → Path：四条 PASS 与上述独立 native 断言支持“新增 void0 transport 的已测 ABI、原异常和物理调用窗口有效”；可据此继续接游戏真实 UnityEvent.Invoke 与执行归属观察。它们没有执行游戏、没有 leaf/mid 指令点观测、没有实际 IL2CPP GC/root 释放故障注入，也未证明 UnityEvent 内联准备后的每个 listener 已完成。helper 零 inFlight 只表示这些物理入口当前排空，不能推出事件消费者、协程/取消、销毁、资源生命周期或整个 run 完成。
