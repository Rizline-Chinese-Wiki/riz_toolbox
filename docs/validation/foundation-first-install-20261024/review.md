# Bootstrap 晚接入源码复核

2026-10-24 15:36 +08:00 首审，15:41 对修复后的托管源码再次只读复核，15:46 读取最终构建记录、日志、汇总与 native JSON。范围见 [scope.md](scope.md)。审查者没有构建、安装、操作游戏或修改源码。“有断言”表示测试代码的覆盖条件；最终实际运行结果另列日志证据。

结论：原生与托管边界已经实现一次配置、一次 immutable observer Attach 和逐调用快照。审查发现托管 `ReadActivity` 的一个旧快照竞态，Runtime 代理已修改为在 `_info` 前捕获 seen 状态，本审查已核对代码与新哈希。修复后完整构建 session 59982 exit 0，25 套 1511/1511，native bootstrap fixture 698 checks、0 failures。竞态修复的因果判断仍来自源码检查，没有确定性注入该交错的动态用例。生产首次安全安装器、真实游戏接线和全底层验收仍未完成。

## 证据

各项 source_type 均为 file，E-02 的 observed_at 为 15:41，E-06 至 E-08 为 15:46，其余为 15:36；复现方式是在仓库根目录读对应文件，并用 `Get-FileHash -Algorithm SHA256 -LiteralPath <文件路径>` 核对内容。最终共享源码 E-02 哈希仍为上次修复后的值，未变化。

| ID | 文件 | 审查时 SHA-256 | 原始观察 |
|---|---|---|---|
| E-01 | [BootstrapVoidContext.cpp](../../../native/RizToolbox.JudgeContext/BootstrapVoidContext.cpp) | `fbcb45a4a12f316e5c8a55bd0b3875b0b39e5cfb9ef3138c35ca992dd714e94d` | `recordLimit = 256`；Enter 固定 TLS record 后只读一次 `published`；Exit 原生 finally 清理；Mid 核 top/state/self/PC。 |
| E-02 | [NativeBootstrappedVoidBridge.cs](../../../src/RizToolbox.Runtime/Hooks/NativeBootstrappedVoidBridge.cs) | `11b2f225c5e7c1a78e137473676dfe40c53c7a697c8c4bad7ef2a44dc322823d` | 不导出 native Configure/Dobby；先冻结 Callbacks 再 Attach；ReadActivity 先读 `seenAttachedBefore`，再调用 `_info`，只用该先前状态解释合法 Info0。 |
| E-03 | [NativeBootstrappedVoidRegression.cs](../../../tests/RizToolbox.Runtime.Tests/NativeBootstrappedVoidRegression.cs) | `58073dea880e33fac6186ded12becf92864f9a5e9baedf9b4d04e060a074cbd7` | 自有 fixture slot 30，Dobby Prepare/Configure/Mid patch/Commit 都在首次调用之前；后续 CLR callbacks 使用真实 native frames。 |
| E-04 | [NativeBootstrappedVoidBridgeTests.cs](../../../tests/RizToolbox.Hooks.Tests/NativeBootstrappedVoidBridgeTests.cs) | `e520d8e0717bbadd44376249017bf973a5a217893f968cd470f62147dff10484` | 布局/身份逐字段拒绝，包含 attached/unknown/reserved/epoch/entry 和无效输入先于加载。 |
| E-05 | [BootstrapVoidFixture.cpp](../../../native/RizToolbox.JudgeContext/fixture/BootstrapVoidFixture.cpp) | `0c23dcf6bfbf6b1fd0ee62e476de496be3d3c8713e3faccdf1cb6d804904ec50` | 老调用在 mid 前暂停，Attach 后不补发 mid/exit；同槽 foreign MethodInfo 递归；各阶段 SEH/C++ callback fault 后接 clean invocation。 |
| E-06 | [build.log](build.log) | `6e01a7e68ea8eac671909b73105c4166dabe2f1dc60d118db89cc415f2ee5ac8` | 第 29 行 native bootstrap JSON passed=true/checks698；第 355 行真实 native/CLR bootstrap 联合用例 PASS；Runtime160、Hooks167、Audio573 均通过。 |
| E-07 | [native-bootstrap-evidence.json](native-bootstrap-evidence.json) | `5465beb02b49f15b7222517bba7294da2838fbedd52443050f0a0bb005fa3e00` | `passed=true`、`checks=698`、`failures=0`，oldUnobservedCalls2/observedCalls18/foreignMethodRecursions3/originalExceptions2/observerExceptions6；firstInstallSafetyVerified 和 gameRuntimeVerified 均 false。 |
| E-08 | [build-summary.json](build-summary.json) | `e1145467c9eb6d3b4c023bf6563dbe8d99c59aa1ad470c31250af6055cdba7bf` | 25 projects、1511/1511；与 E-06 同 logSha256，nativeBootstrapAttach 结果与 E-07 一致，compilerDiagnosticLines 为空。 |

## Findings

| ID | 证据 | 状态与结论 | 影响和后续动作 |
|---|---|---|---|
| F-01 | E-01、E-02、E-06、E-08 | **源码修复已复核且最终 full 通过，high confidence，design**：旧实现中，A 的 `_info` 先返回 Attached=0；B 在 Attach 发布后读到 Attached=1 并写 `_seenAttached=1`；A 再读全局 seen=1，会将自身旧 Info0 判 invalid。现已把 `seenAttachedBefore` 读取移到 A 的 `_info` 之前，并用该局部值核 Info0。 | B 的较晚观测不能再回溯改变 A 已捕获的先前状态；真正发生在本次读取之前的已附着状态仍拒绝 Info0。该因果修复不扩大权限，Info0 仍返回 Known=false。尚无强制插入此精确交错的动态注入；full 通过也不能作为精确竞争动态复现证据。 |
| F-02 | E-01、E-02、E-04 | **validated by source review，high confidence，other**：托管只采用现存 native slot，没有重新 claim 原生表。 | 构造核同 slot/Target/Original/Entry/epoch、unattached/Known 与 ABI；进程 Owners 在任何 Attach 前保留 DLL/delegates。拒绝的首次 Configure 或不确定发布不能丢根、重试或替换。这里的 validated 仅指可直接检视的代码属性，不代表运行验证。 |
| F-03 | E-01、E-02 | **validated by source review，high confidence，design**：handler 集合一次冻结，ConfigureMid 必须早于 Configure，native 每次 Enter 只捕获一次 immutable observer。 | 未附着调用同样压入 record，晚到 Attach 无法使旧调用后半段继承新的 callbacks。未溢出的普通嵌套 Exit 在 finally 清 record、递减 depth；callback fault 不阻止原函数、原异常或外层 admission finally。 |
| F-04 | E-01、E-05、E-06、E-07 | **validated by native fixture，high confidence，other**：原生 fixture 对 Enter/Mid/Exit 的 SEH/C++ 共六条 fault 路径和随后同线程正常调用实际通过，698 checks、0 failures。 | Enter fault 后 Mid 跳过、Exit token0/EnterFaulted 配对；native sticky unknown 通过 GetInfo 传播，原 Void/Mid fault counters 仍为零。后继正常调用核 token/flags/current 与 drain；私有 native TLS depth 没有导出，准确 depth 清零仍结合 E-01 finally 源码判断，不能仅由 InFlight0 或结果计数推断。 |
| F-05 | E-03、E-05 | **validated by source review，high confidence，design**：现有联合用例只创建 fixture 首次调用之前的发布前提，没有生产 first-install proof。 | root CLR 用例覆盖 post-mid 老调用物理尾部、正常/SEH/C++ continuation、真实 caller/MethodInfo/mid event/list、未改写 admission release 和 CLR Exit fault 的 managed unknown；native fixture额外覆盖 pre-mid 跨 Attach。不能据此启用游戏 RequestPlay 或补勾整个 B01–B19。 |

### Attach 窗口的两项独立判断

当前实现已使用 native `Info.Attached`，因此 Attach 在 native 发布 descriptor 后、尚未返回 Configure 的窗口里，callback 的 ReadActivity 不必等待托管“成功”标志。它也不持 Configure 的 `_gate` 调 CLR。这避免把真正已附着的正在执行回调误报为尚未配置。

F-01 是另一个方向：多个 ReadActivity 对 0→1 的观察顺序交错，不能反过来用较晚的全局 seen 值否定较早合法的 Info0。E-02 新代码的局部先前状态消除了这条静态因果路径。E-03 在 Configure 返回后检查状态，未确定性地强制此交错，因此这份审查只宣称源码修复，不宣称已动态注入并重现该竞争。

15:36 首审时的 E-02 内容哈希为 `63a40b59e29447fb90faddaebc1f612a38f456e68ccb64fb8aa648f45632f1df`，现由上表新哈希取代。[build-run.json](build-run.json) 记录首 full session 20552 exit 0，对应修复前 [build-before-read-race-fix.log](build-before-read-race-fix.log)；最终修复后 session 59982 exit 0，1511/1511。E-06 中日志 SHA 已由实际 Get-FileHash 核对，而非仅复述 runner 字段。

[源码归档检查](source-archive-check.json) 声明 453 entries、4,875,903 uncompressed bytes、allEntryHashesMatched=true，ZIP SHA `fcdba98f9c3b160ac0ac64e38575a5bcab6406d1935964a2fa82b2efacb5381c`。[暂存归档检查](package-archive-check.json) 声明 18 entries、3,398,632 bytes、allEntryHashesMatched=true，ZIP SHA `a3e5ec54a8f4c6be5a51766162ec309cebb08ca47c4bd90d36ea08de340ec597`、installed=false。上述归档内容检查由 root 的冻结工具执行，本审查只读取其最终记录；它们不代替干净机器复现、游戏安装或 B19 全矩阵验收。

## 调用路径 P-01

path_type 为 callflow，start 为已配置且已安全发布的自有 fixture 父入口，goal 为保持一次原生执行同时晚接入 CLR 观察者。

1. Dobby Prepare 得到 actual trampoline，native Configure 永久 claim 同 index Void/Mid，fixture 在首次执行前发布父与 mid。证据 E-01、E-03、E-05，对应 F-02、F-05。
2. 父入口经过既有 Void admission，Bootstrap Enter 压入自身 record，取得一次 observer snapshot；原生 original 执行一次。证据 E-01，对应 F-03。
3. 实际 mid CALL 采集寄存器；仅 TLS 当前记录、同 state/self/expected return PC、original window 和有效 snapshot 才调用观察者。证据 E-01、E-03，对应 F-03。
4. 原生 normal return 或 SEH/C++ unwind 都进入相同 record 的 Exit；callback faults 被包含，TLS finally 清理后由既有 Void transport 结束 admission。证据 E-01、E-03、E-05，对应 F-04。
5. 托管 ReadActivity 对 slot/epoch/identity、native fault、admission 统计和自身 sticky unknown 作联合判断；用调用 native Info 之前捕获的 seen 状态解释合法旧 snapshot，保持未附着时 Known=false。证据 E-02、E-06、E-08，对应 F-01。最终完整构建已通过，精确交错仍未动态注入。

## 尚未由本审查证明的事项

没有独立强制超过 256 层的动态 overflow 断言，也没有在两个不同已附着槽之间嵌套或在 observer 回调内部再入的专项用例。对应的 mask/top/state 检查已按源码复核，但不能把它写成动态覆盖结果。生产首次 patch、真实 IL2CPP GC/listener 路径、全资源退休与 Retry/ReturnLoader 仍须沿 [本批设计](installer-design.md) 和完整 SPEC 继续实现。
