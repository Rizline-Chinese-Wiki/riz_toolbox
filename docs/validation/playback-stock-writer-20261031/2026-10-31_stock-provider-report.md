# S5：单 Session 正式 stock provider 离线消费

本包将实际登记的 stock Session 交给 concrete provider，并经现有 GameplayNativeBackend、GameplayService、GameplayModule 和 PluginHost 消费。最终源码通过 provider **7 进程 / 1021 检查**及独立 clock 回归 **10 进程 / 179 检查**，两个构建均为 0 警告、0 错误。provider 与 clock 各有独立 **5 PE/PDB / 471 文档**源码绑定，不能相加为 10 个独立组件。本包是受控 native fixture 的单 Session 实现；[范围与授权](scope.md)保留所有真实首次准入及跨局正式模块边界。

## 可审交付

`s5-stock-provider.patch` 以当时只读主树为基线，只包含 8 个 S5 文件；共享 `NativePlaybackSingleReturnBridge.cs` 与主树均为 SHA256 `79e4947c066ad91ae56cba29db50cf61f8d091777d4456569a2f2afad3450f07`，不进入补丁。`before-source-snapshot.zip` 保存 756 个基线文件，`implemented-source-snapshot.zip` 保存 762 个最终源码文件。`changed-files.json` 给出逐文件 before/after hash，`source-manifest.json`、`baseline-manifest.json` 固定完整快照；独立复制基线上实际 `git apply --check`、实际 apply 及 762 文件逐字节核对见 `apply-check.json`。

| 文件 | 行为与责任 |
|---|---|
| `NativePlaybackStockRateProvider.cs` | 只由 actual armed OwnedFixture Session 登记；Capture 独立七根，GetTime 返回同一个私有 lease；复制对象和错线程 cleanup 无权撤回 |
| `NativePlaybackStockClockRouter.Provider.cs` | 精确 provider/Session 注册；物理 fault 回调只保存确切对象并入队；owner Pump 撤当前 logical Session |
| `NativePlaybackStockClockRouter.cs` | partial 接缝、owner Open/Pump 处理 fault；logical Dispose 保留 physical group/lease |
| `GameplayModule.cs` | Tick/Pump、Release logical cleanup；Release 先验 owner 线程，避免先丢 Service 引用 |
| provider test `Program.cs` | 正式模块/Service/双插件 SDK、独立准备根、原生写入/Clock/journal/stock 调度/GetTime 与拒绝矩阵 |
| provider test project | 单独 runner，复用既有 NativeRuntime synthetic metadata fixture |
| `Test-PlaybackStockProvider.ps1` | fresh 输出、独立进程、actual exit/stdout/stderr 和源码/二进制 hash |
| `Verify-PlaybackStockProviderSymbols.ps1` | PE/PDB 必须是该 execution 的确切 managed artifact；逐文档 SHA256 与 actual CodeView/PDB id |

`native-input-snapshot.zip` 保存本次实际复用的 native 输入及其先前构建/符号/COFF/CPP/H/ASM 凭据，映射见 `native-input-manifest.json`。native 输入未因 S5 改动或重建；其中 prior supporting artifacts 不算本轮执行。实际使用的四个 native DLL 仅以新 execution 的 `inputs[1..4]` 为准。`retained-evidence-manifest.json` 固定本 case 全部保留证据，旧失败和旧通过不作为当前源码验收。

## 实际执行

| 最终 evidence | 实际通过范围 | 单列计数 |
|---|---|---|
| `checks-r4/test-result.json` | normal-module、wrong-origin、worker、run-loss、external-equal、release-prepared、physical-fault | 7 / 1021 |
| `clock-regression-r3/test-result.json` | 原 S4 dormant/arm、原调用/撤回/在途/rollback 回归 | 10 / 179 |
| `provider-symbols-r1/test-result.json` | actual provider runner DLL 与四个依赖的 CodeView/portable PDB/source | 5 / 471 |
| `clock-symbols-r2/test-result.json` | actual clock runner DLL 与四个依赖的 CodeView/portable PDB/source | 5 / 471 |

正常 provider 路径先由插件一实际 SetSpeed=2，确认 native pitch、唯一 final Clock 和 shared run journal；停用该 scope 后插件二取得 exclusive lease，实际 SetSpeed=1.5。旧插件一的 lease.Dispose 不影响插件二。然后实际 stock Replay/Resume 的叶子 double、reset-to-one/私有 writer restore 与实际 native GetTime 均保留第二插件选择的 1.5；pre-roll 仍为原始未缩放语义。准备、确认和 native reader 均核同一个 final Clock 对象。

worker 模式实际拒绝 provider/Capture/source/module Release 错线程调用，之后 owner 仍可继续使用及恰好释放资源。physical-fault 模式先实际触发 completion observer fault，保留 original bits；worker 通知只入队，Clock 尚未逻辑撤回，随后正式 Module.Tick 消费并撤回。Unknown 保留 7 根责任；其余模式最终 ActiveRoots=0。每个进程物理 Prepare/Commit 仍为 9/9，正常 Module.Release 不 Dispose 物理 lease。

## 失败和反例保留

`release-worker-counterexample-r1` 只去掉新增 Release 首行线程 guard，并保持相同增强 worker 检查，actual exit=1，精确复现“Rejected worker Module.Release lost the live Service or its roots before the owner could retry.”。最终 worker 模式 actual exit=0，修复发生在失去 Service 引用之前。

`checks-r3` 的新增第二插件事务曾放在 Replay/Resume 之后：native fixture 已将 media 置 0，而 synthetic Progress.Update 并未执行，严格媒体/进度+offset 一致性 guard 正确拒绝。本包将两插件事务放在同一个已验证暂停基线，然后继续实际 Replay/Resume/GetTime；没有放松 participants guard、伪造 fixed game profile 或补造 source authority。早期 `early-checks-r1`、`checks-r1`、旧 `checks-r2` 与构建失败均保留。

`symbol-driver-r1/wrong-build.*` 实际将 provider execution 配到 clock build，actual exit=1，明确拒绝 “The supplied build is not the actual executed test assembly.”。旧 `clock-symbols-r1` 只证明旧 clock runner；新 proof 明确标记 clock 的 `controlledProviderIssued=false`，provider 的为 true。

## Evidence → Finding → Path

以下均为 `source_type=file`；时间为 UTC，source_ref 与 artifact_path 相同并相对 case 根。完整实际 argv/cwd、stdout/stderr/exit 由表中 command 文件保留；复跑须将输出改为尚不存在的新 rN 目录。

| Evidence | observed_at / source_ref / artifact_path | content_hash / raw_excerpt / repro_command |
|---|---|---|
| E-PROVIDER | 2026-10-31T15:14:22Z；`checks-r4/test-result.json` | `08437a63c6cbcf1ba5dadf3f304cbe1ef05066f2908a73c53ec1353913c02312`；passed=true, 7/1021；`managed-build-r6/checks.command.json` |
| E-CLOCK | 2026-10-31T15:11:16Z；`clock-regression-r3/test-result.json` | `218d883fcc00883b8695f80c104c9a5a3247a44c678bf095d8995f121a72a0a5`；passed=true, 10/179；`managed-clock-regression-r3/checks.command.json` |
| E-SYMBOL | 2026-10-31T15:15:05Z；`provider-symbols-r1/test-result.json` | `10e93f0fcc9699be3c1b7453ece050aba06563bbc94b1dd93dda90454c04e9f8`；5/471, exact execution；`symbol-driver-r1/provider.command.json` |
| E-WORKER | 2026-10-31T15:04:48Z；`release-worker-counterexample-r1/test-result.json` | `02831bf92634e716d356c480e4a10d0f3fb3a5bd5cb0f63981ae3fe9eb00171a`；reproduced=true, actualExit=1；`release-worker-counterexample-r1/worker.command.json` |

上述 Evidence 的 linked_workitem 均为 S5，E-PROVIDER/E-CLOCK/E-SYMBOL 取代各自旧版当前验收，E-WORKER 的 supersedes=none；旧文件继续封存。

| Finding | severity / category / confidence / status | evidence_ids；location；impact / remediation |
|---|---|---|
| F-PROVIDER | n/a_re / other / high / validated | E-PROVIDER, E-SYMBOL；provider→Module→PluginHost；单 Session 受控正式消费闭合，按此限定合入 |
| F-RELEASE | info / design / high / validated | E-WORKER, E-PROVIDER；GameplayModule.Release；先验线程保留 owner retry，修复已纳入 |
| F-FAULT | n/a_re / design / high / validated | E-PROVIDER, E-SYMBOL；Router.Provider/PumpPhysicalFaults；callback 队列和 owner 撤回分离，Unknown 不当 drain |
| F-BOUNDARY | n/a_re / other / high / validated | E-PROVIDER, E-CLOCK；controlled factory/默认 RizlineAdapter；fixture ClockMapping unavailable、production=false，跨局正式模块仍待实现 |

P-S5，`path_type=callflow`：start=actual private common group 与 host-issued run/source；goal=双 scope 正式值事务与真实 native GetTime。步骤为实际 stock completion/rooted readback → dormant/arm/唯一 final Clock（E-CLOCK，F-PROVIDER）→ 精确 Session/provider/GetTime lease → Backend/Service/Module/PluginHost exclusive lease → actual native pitch + final Clock/journal → stock Replay/Resume/GetTime（E-PROVIDER/E-SYMBOL，F-PROVIDER/F-FAULT）。残余风险为下节边界；不外推真实首次安装或第二局。

## 尚未完成

默认 RizlineAdapter 继续不给真实 provider；没有 production flag 翻转。未知 engine/lower writer、source generation 完整来源、真实 first-admission、真实 metadata/root/run/DSP/消费者/可闻同步/结果流程、任意 Seek 未签通过。单 Session provider 与 readonly module 注入参数不能自动采用新 Session；持续存活的 Module/Service/PluginHost 跨局 adoption 需要在 S6 与正式模块组合包继续验证。

当前交付只在 S5 隔离目录写入。源码 hash、before/after、实际 apply、当前执行/符号与全部证据完整性以 `test-result.json` 和 `verification-index.json` 为入口；不以旧 1007 或旧 symbols 验最终源码。
