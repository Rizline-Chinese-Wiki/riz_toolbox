# StockStopV1 独立代码与原生协议审查

审查发现一项已证合同缺陷：ignored foreign original 继承外层 stock TLS，能把共享当前 AudioSource 的 Stop 错签为合法 child。Root 的 native-r3 用统一 `ForwardIgnored` 屏障修复；独立新建 fixture 的六进程 328 检查均实际 exit 0。原 native-r2 失败、固定同 EXE 的 native-r3 前后对照均保留。未发现第二项已确认产品缺陷；本报告不签发完整功能或生产准入。

## 范围

授权和排除项见 [scope.md](scope.md)。审查覆盖 native `PlaybackSourceContext.cpp/.h`、`PlaybackPlayerWatch.inc`、相关 ProgressWatch；managed Group 配置、StockSourceHub.StockStop、SourceTypes、OccurrenceProducer、Router；以及当前 PS runner 的固定 fixture 身份。采用普通技术审查模板，flavor=null。

本代理未修改 T/source 或主目录，构建和执行仅针对本目录的自有测试及经过哈希固定的 native helper。主代理随后合主、SDK、mapped、七 ABI、handoff 及其它矩阵的状态各由其自身证据负责，本报告不替它们签通过。

## F-001：foreign original 借用外层 TLS parent

- severity: n/a_re（私有原生协议合同错误）
- category: design
- status: validated；native-r3 已修复并独立复验
- confidence: high
- evidence_ids: E-001、E-002、E-003、E-004
- location: native-r2 `PlaybackSourceContext.cpp:373–376` 及同类 ignored 分支；`PlaybackPlayerWatch.inc:24–43`；`Enter` 原第 276–279 行；Router 第 97–109 行。

外层是当前 watched player 的顶层 Replay，位于原函数区间而非 observer。其原函数再次调用相同 Replay entry，但 receiver 是另一个同 class player，复用当前 AudioSource。native 过滤 foreign player 后直接执行其 original，没有压 scope 或遮蔽外层 `currentScope`。因此 foreign original 的固定 Replay→Stop CALL 看到外层 root，首 child、MI、caller、同源与连续 fence 条件全成立。它被签为 flags=129，跳过 DirectAudioAttempt 推进；Router 会因此绕过普通 direct 撤权分支。

独立反例令外层 original 在 foreign Replay 返回后直接返回。旧 n2 实际 PID 30528 exit 1，49 检查中两个拒绝断言失败：两个不同 player、同一 source，foreign root 无观察计数，Stop flags=129、parent=outer root1、caller 等于固定 caller。Stop 原函数仅一次，Entered=Exited=2。该反例直接证明 native 误签；它没有声称完整 managed Schedule/Clock 复权已在此最小原生场景中发生，也未证明 853d 游戏引擎当前必然产生该重入。

修复点为 n3 `PlaybackSourceContext.cpp:362–368`：七种 ignored-original 路径都调用精确 `decltype(original(args...))` 返回类型的 `ForwardIgnored`，先保存当前 scope 并置空，`__finally` 恢复。foreign 原函数自身仍无新计数、token 或观察；其共享当前 source 的 child 成为独立 parent0 attempt，不能借外层 stock parent。该修复同时覆盖 Pause/SetPitch 等旧 leaf 的父归属，未仅屏蔽 Stop flag。

## 证据

| ID | source_ref / artifact_path | content_hash（SHA-256） | 复现与实际观察 |
|---|---|---|---|
| E-001 | [旧n2源码与当前n3审查快照清单](reviewed-source-manifest.json) | `099da806766b5a1f9e091cbb2cdbe52cb52b52b475a42d3a797c03a8ee9631c86e2` | 静态核对 ignored 分支与StockStop条件；清单固定15份源码快照。 |
| E-002 | [旧n2反例结果](foreign-player-shared-r1/run/test-result.json) | `d6e6cee91aeaa9c9194f5c22c76bf8568df488f128e35adb12ae8920abe41400` | `python foreign-player-shared-r1/Run-Counterexample.py` 的原始执行：normal Replay exit0，foreign共享源 exit1、flags129/parent1。脚本要求新目录，已存在证据不覆盖。 |
| E-003 | [固定同EXE/n3对照](foreign-player-shared-r1/run-n3/test-result.json) | `04e2e1ea87fda38bdb469ed4f958102fc6ed433874493c3f4406f891c4baa4cb` | normal Replay/Resume均exit0；foreign旧EXE仍raw exit1，仅旧parent证明断言失败，实际flags1/parent0。明确保留原退出码，未当成测试exit0。 |
| E-004 | [新r2测试/n3六进程结果](foreign-player-shared-r2/positive-r3/test-result.json) | `e08b4b88bfa7254718f3a2416b028ebae18571c4d649e85a59445675ce005cc9` | 六进程全部actual exit0，328 checks；每个launch/PID、stdout/stderr、exit、输入hash留存。 |
| E-imports | [固定PE只读证据](static-r1/pe-static-evidence.json) | `58a240d9cfd423f2f5f2c5b5c92d17455600a67fcfa30b6cf96cfccc1b51b1b9` | `python Inspect-FixedImages.py` 的PE解析；targetLoadedOrExecuted=false，853d映像hash吻合。 |

上述 `repro_command` 相对于本报告目录；原始脚本拒绝复用已存在的运行目录。第三方可直接使用固定 EXE/helper 在新证据目录执行下列命令，逐场景预期 exit0：

```powershell
$reviewRoot = 'D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102-independent-review'
& "$reviewRoot/foreign-player-shared-r2/build/StockStopNativeFixture.exe" foreign-player-shared "$reviewRoot/foreign-player-shared-r2/positive-r3/RizToolbox.PlaybackSourcePrototype.dll"
& "$reviewRoot/foreign-player-shared-r2/build/StockStopNativeFixture.exe" foreign-player-seh-restore "$reviewRoot/foreign-player-shared-r2/positive-r3/RizToolbox.PlaybackSourcePrototype.dll"
& "$reviewRoot/foreign-player-shared-r2/build/StockStopNativeFixture.exe" foreign-start-al-restore "$reviewRoot/foreign-player-shared-r2/positive-r3/RizToolbox.PlaybackSourcePrototype.dll"
& "$reviewRoot/foreign-player-shared-r2/build/StockStopNativeFixture.exe" foreign-start-seh-restore "$reviewRoot/foreign-player-shared-r2/positive-r3/RizToolbox.PlaybackSourcePrototype.dll"
```

## n3 独立复验

| 模式 | PID | actual exit | 检查 | 核心断言 |
|---|---:|---:|---:|---|
| replay | 49288 | 0 | 49 | 合法Stop唯一签章，normal physical exit。 |
| resume | 18196 | 0 | 49 | 合法Stop唯一签章，normal physical exit。 |
| foreign-player-shared | 39708 | 0 | 52 | 不同player共享source；Stop flags1、parent0、flagged0。 |
| foreign-player-seh-restore | 9140 | 0 | 54 | foreign Replay原SEH `e2440012` 传播；outer恢复后合法Stop flags129、parent1。 |
| foreign-start-al-restore | 46464 | 0 | 62 | ignored Start原函数一次；原始AL `a5`=165不bool化；outer随后正常签Stop。 |
| foreign-start-seh-restore | 20584 | 0 | 62 | 原SEH `e2440013` 传播，finally恢复outer；无伪造native abnormal child。 |

n3 helper SHA-256=`75a41e3e715e7025386a75a993e369e8e22aef3e66eb5de9becd5899a0424490`。新测试EXE=`0dd5d99a67b4db5a0cf79d0fc76c4328ad7deed1576f383b87cb14d2001513d6`；PDB=`2a22e34f7efa40f0d0882ebb59f6e4b8e1eab898eafcf5d2132a7850b763ce65`。构建脚本冻结实际源、EXE/PDB和recipe，采用 `/W4 /WX /EHa`；本次增加的七透传模板经实际编译。

## P-001：callflow

- path_type: callflow
- start: 当前watched Replay的原函数区间。
- goal: 证明foreign original的child不能借用外层stock权限。
- steps: current Replay → foreign Replay(shared current source) → ignored-original → 固定Stop CALL → n2继承outer scope并误签（E-001/E-002/F-001）；n3清TLS → Stop parent0、无StockStop标志 → finally恢复outer（E-003/E-004）。
- residual_risks: 自有原生协议复验已证；完整managed误复权和真实引擎重入可达性未在本报告中证明。

## 其余审查结论与边界

native签章在推进child fence前检查实际TLS、parent role/kind/stage、首child、owner线程、armed/fault/hold/gate、watch phase、player/source/class、MI/CALL及fence。观察回调重入、重复Stop、错误caller/MI、worker与stale source不能借用该标志。合法Stop仍推进Scheduling，保留Before、Returned与finally journal；异常Stop不能通过producer的正常返回及physical检查。OccurrenceProducer要求Replay为Stop→Pitch→Schedule，Resume为Stop→Time→Pitch→Schedule，并检查child真实物理退出与连续fence；Stop自身不产生schedule或Clock。Root无有效readback时，RootedSource会撤回逻辑权限。

watch记录不复用，source fence/DirectAudioAttempt在active watch上核验，开源要求零in-flight；旧owner退休按精确receipt/owner约束。静态检查未见旧清理直接撤后继的新增路径。同/跨局开源后正常Stop的补测已交独立handoff代理，本报告不签它的最终结果。

纯文件反汇编确认853d的Replay Stop返回PC `3fbf21`、Resume `3fc185` 与broker常量一致，incoming MI为null。`AudioSource.Stop()` 在 `1511842` 直接tail-jump engine icall，没有再次进入hook的Stop(bool)；因此fixture未遗漏这个假设的额外hook child。GameAssembly导入类别包括Win32基础、用户/注册表、COM、Shell、Winsock、IP helper、调试及baselib；386导出、x64。没有加载或执行游戏。

当前 `scripts/Test-DirectAudio.ps1:36` 固定fixture `604ebb…186a3`，ValidateSet包含stock-stop-replay/resume，且每行结果要求actual exit0及19 Prepare/Commit；与当前fixture和协议相符。

仍未证的边界：七种完整ABI/异常全集、SDK/mapped/handoff最终组合由各自矩阵负责；完整sourcegeneration、unknown lower、production、Seek/effect和B01–B19不在本增量完成范围。此处没有用本六进程证明这些功能已完成。

## 时间线

- 2026-11-02 13:34:48 UTC：旧n2正常对照与foreign共享源实际失败（E-002）。
- 2026-11-02 13:39:24 UTC：固定同EXE/n3对照，错误签章消失；保留旧证明断言的raw exit1（E-003）。
- 2026-11-02 13:53:16 UTC：新r2 fixture六场景全actual0/328，补SEH和AL（E-004）。

本次源码及证据检查完成；唯一已证合同缺陷已修复并独立验证。所有原始失败保留，未改写为通过。
