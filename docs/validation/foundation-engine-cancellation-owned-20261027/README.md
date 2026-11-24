# 自有进程的取消终态联合验证

2026-10-27。在三个独立自有进程中，实际 Node、L 内联、M 回调、N wait 和 Operation Stop 通道已贯通同一节点代次。正常场景通过只读终态判定后，先撤回五个自有来源对象的生产引用，再逐个释放保留引用；缺少来源封口、请求错误来源代次的场景均没有释放来源根。**本批没有签发生产 `NativeCoroutineCancellationCompletionReceipt`，生产请求账本仍为 `Pending=true / EngineCancelled=false`。** 游戏未运行、未安装，生产准入仍关闭。

本报告按 docs-generator 的证据链方式整理，只读核对已有父子原件；报告编写阶段没有重新构建或执行夹具。范围沿用[逐记录退休范围](../foundation-engine-retirement-20261026/scope.md)，本批增加自有 L/M/N/Stop 联合执行与来源引用协议；真实 Unity 对象、完整生产取消、整局资源释放不在本次通过结论内。

## 实际结果

父检查原件：[joint r2/test-result.json](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/test-result.json)，三个进程退出码均为 0，无超时、无强制终止、`issues=[]`。

| 模式 | 实际 PID | CLR / 原生检查 | 精确账本终态 | 自有来源回执 | 来源根状态 |
|---|---:|---:|---|---|---|
| `normal` | 28484 | 36 / 238 | 通过 | 签发 | 五个对象各释放一次 |
| `missing-source` | 56116 | 33 / 238 | 通过 | 拒绝：未撤回来源 | 五个对象全部保留 |
| `wrong-generation` | 56568 | 34 / 238 | 通过 | 拒绝：请求代次不符 | 五个对象全部保留 |

这些检查数属于三个有限场景，不是底层完成比例。两个负例已经完成同样的原生调用与节点清理，只是来源权限不同；因此也验证了“本地调用空闲”不能替代来源封口。

## 实际调用与身份

[独立原生夹具](../../../native/RizToolbox.EngineCancellationManagedFixture/CancellationManagedFixture.cpp)派生自已有 N 夹具，但拥有单独源码和二进制；[独立 ASM](../../../native/RizToolbox.EngineCancellationManagedFixture/CancellationCaller.asm)增加实际 L 指令探针 CALL，旧 N 源码没有被改写。

调用路径 P-001 为：实际 Start → 同步 Execute → Yield 发布 wait → N Dispatch → M Callback → 再次 Execute 与一次 Release → 引用回到 1 → L Begin 实际 CALL → Operation Reference1 helper → 自有 Stop 原函数 → 同代 Detach → Stop Complete/Dispose → N Start-clear 三处实际指令 → L EndFree 实际 CALL → 原 Start 返回 → StartExited → 原 Start Complete/Dispose。之后 Batch 返回最后 wait 引用。

[托管入口](../../../tests/RizToolbox.AudioContent.Tests/NativeEngineCancellationManagedIntegration.cs)通过真实桥、dispatcher 与已有账本接收回调，没有直接发送合成帧。原件中三个进程均有 L 事件 `[Begin=1, EndFree=2, StartExited=4]`、Node 种类序列 `[Start=0, Execute=1, Execute=1, Release=2, Detach=3]`，Stop 捕获、完成、释放各为 1。一个原生节点和一个 wait 对象实际完成释放。

Stop 发生在 Start 尚未返回时，候选保存的是当时身份：`RequestId=1 / RootRequestId=1 / RequestSequence=1 / DetachSequence=5 / StartSequence=1 / NodeGeneration=1`，tracker 当时 `StartReturned=false`。后续真实 Start 结束及身份单调升级后，共享[只读终态判定器](../../../src/RizToolbox.Adapter/AudioContent/NativeEngineCancellationTerminalVerifier.cs)才通过。候选绑定实际请求、Detach Call、IdentityLedger 与节点引用；同值异账本不能代签。候选 DTO 不承担授权。

必须区分 M 的覆盖：本场景实际执行的是 N Dispatch 内的一次 **M Callback**，并保持六条 M 通道的实际附着与空闲校验。没有执行 Register、Remove、Consume、Pump、Drain 调度行生命周期；本场景的 M 行账本可以为空。因此本报告只称 L/M/N/Stop 同代贯通，不声称全部调度行分支通过。EndRetained、嵌套 Stop、多个目标、地址复用也未在这三个新进程中执行。

## 五个来源对象与两种回执

五个来源对象都是该 DLL 用 `VirtualAlloc` 创建的独立 `SourceObject`，角色号 0–4，初始代次依次为 1–5；各有一个生产引用和一个保留引用。这是自有引用协议，不能替代生产中的 controller、staging、clone、EventTrigger 等 Unity 对象死亡证明。角色 0 实际作为 Stop receiver；角色 2 有第二个独立缓存 native-owner，未在该 receiver 上启动第二条协程。其余三个对象的缓存 owner 本来为零。

正常原件逐项显示：

| 阶段 | State | References / ProducerReferences | 五对象撤回 / 死亡序号 | RootReleaseAttempts |
|---|---:|---|---|---:|
| 初始 | 0 | 2 / 1 | 0 / 0 | 0 |
| 撤回并冻结死亡 | 2 | 1 / 0 | 6/7、8/9、10/11、12/13、14/15 | 0 |
| 逐根释放后 | 3 | 0 / 0 | 保留原序号 | 各 1 |

`OpenScopes=0`；角色 0、2 的缓存 owner 都在撤回阶段变零。各对象的地址和代次在三个快照阶段保持一致。正常场景随后调用实际 `VirtualFree`，再次释放请求被拒绝。缺少来源场景三阶段均保持 State 0、引用 2/1；错误代次场景保持已撤回的 State 2、引用 1/0，两种负例 `RootReleaseAttempts` 都为零。

测试程序集私有的 `OwnedSourceIssuer.OwnedReceipt` 只在实际 native 来源快照、同一对象/代次、生产引用撤回、实际全通道排空与共享终态判定都满足后签发。它不能传入生产 launch、不能清除生产取消 hold、也不能成为 `NativeLocalActiveSourceClosureSeal`。

生产路径仍要求实际 Adapter coordinator、自有私有签发 token、生产来源 seal、所有精确请求覆盖和 request-version 一致，再消费 `NativeCoroutineCancellationCompletionReceipt`。本夹具只复用无副作用的终态条件，**没有**调用该生产签发路径。三个进程都明确断言生产账本 `EngineCancelled=false / Pending=true`；`ownedCancellationTerminalVerified=true` 仅表示正常自有来源协议完成，不是生产取消已通过。

## 父检查、保留责任与覆盖不足

[父检查脚本](../../../scripts/Test-EngineCancellationManagedFixture.ps1)在每种模式创建一个独立子进程，记录实际 PID、终态退出码、stdout/stderr，60 秒上限只针对自己创建的进程。它独立核对模式、结果、所选调用计数、五个对象的地址/代次、撤回引用与序号、正常逐根释放次数和负例没有释放等条件。

本次 joint 的 17 项输入包括实际三个 native DLL、托管程序集及依赖、运行配置、父脚本、native build manifest 和 dotnet host。父进程为这些文件持有 `FileShare.Read` 读锁，运行后逐项复核 SHA256；本报告复读时 17 项仍全部匹配。子进程另持三个 DLL 的读锁并核运行前后 hash。native 构建脚本只对 Kernel 持读锁；源码采用构建前后 hash 检查，没有宣称源码构建全程都持独占冻结锁。

joint r1 的父检查最初缺少 schema、完整事件序列、候选字段、故障数组和子报告 native 文件哈希核验，原件保留。joint r2 已增加 `kind/schemaVersion`、非零检查数、`faults=[]`、L `[1,2,4]` 与 Node `[0,1,1,2,3]` 顺序、单候选请求/根/Detach/Start/节点代次基本关系、三个真实 native 文件与父固定 SHA 的匹配，以及 missing-source 五对象确实未撤回的复核。三个模式在加强后的父检查下重新执行通过。

仍需区分“基本关系核验”与“重建完整物理证明”：父层没有从独立原始帧重新证明候选的每个指针都来自对应账本引用，也没有检查初始五地址互异、完整 helper GetInfo 明细或所有来源最终字段。上述部分由子源码断言或 native 检查承担。输出没有独立原生二进制调用回执或完整异常上下文。报告不把子层断言等同于父层全部独立再验。

本批没有注入 managed callback 异常、SEH/C++ 原函数异常、部分 `VirtualFree` 失败、取消重入、worker 与新 Stop 并发版本竞争。源码对原生来源逐根释放先记录尝试、保留剩余对象；后继状态阻止整批重试，但没有本批运行证据证明每种故障分支。来源对象只有角色 0 的 Stop 实际占用过 open scope，不能泛化为五种活跃生产者都并发排空。三个 DLL 与回调根保留至进程退出；两个拒绝场景的来源对象由 OS 在进程退出时回收，不能写成主动成功释放。

## 构建历史、证据链与固定哈希

E-001：native r1 严格构建退出 1。`StartPrepare` 的 if 块结束括号误落入行注释，后继 extern 被识别为本地函数；失败日志和 manifest 保留。E-002：将括号恢复为独立行后，native r2 严格构建退出 0，未沿用失败产物。E-003：joint r1 首次三个真实 PID 通过，其父层覆盖不足已明确记录。E-004：加强父检查后，使用 core r4 程序集执行 joint r2，三个新 PID 再次通过。F-001（validated，high confidence）由 E-002/E-004 支持 P-001 的有限同代闭环与来源权限拒绝。F-002（validated，high confidence）由 E-004 及生产/自有类型隔离源码支持“自有回执不授生产 EngineCancelled”。

| 证据 / 文件 | SHA256 |
|---|---|
| [r1 失败日志](D:/riz-build-cache-next/engine-cancellation-managed-fixture-r1-20261027/build.log) | `75ae034dde72fdb6c0181e9a0f7a217bc267b3e830d6723be528e5279d417309` |
| [r1 失败 manifest](D:/riz-build-cache-next/engine-cancellation-managed-fixture-r1-20261027/cancellation-managed-fixture-build.json) | `d4af2f9b98aa7ef4fd986843388b469cb4b55138ae6d27eda9087055eee98410` |
| [r2 构建日志](D:/riz-build-cache-next/engine-cancellation-managed-fixture-r2-20261027/build.log) | `648d3ee2cc92eaa9b4d82dde625b9652c132014c555be25008652f7fd7ad08a3` |
| [r2 build manifest](D:/riz-build-cache-next/engine-cancellation-managed-fixture-r2-20261027/cancellation-managed-fixture-build.json) | `3af495adb8a680c971ede7c812a61d3dcd4e040b796194cf4dbb2a8779dd1e29` |
| [joint 父原件](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/test-result.json) | `d99cbe3fb579ae19f38604fda7e5f2858820486b61e2d2bbe766f6c79f64cf9b` |
| [normal 父子结果](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/normal.json) | `99b92edfba25aabc044ea46077af5bbd8c9cdd1a79eef2dc1e04aef00794c18b` |
| [missing-source 父子结果](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/missing-source.json) | `18dc707a2408bdb0be5866502d28c0ebc9020c97fe92f6b43464cc6032e19af4` |
| [wrong-generation 父子结果](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/wrong-generation.json) | `8fdfd24ae7ccb3728083c70de25f5a0661330919525c4fd145ad55473547aff4` |
| [normal 原 stdout](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/normal.stdout.log) | `bbff2fd8d08f03b00a642fa0d7a4ee97a66b184846a8d3f50d8e591d398860ed` |
| [missing-source 原 stdout](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/missing-source.stdout.log) | `f2619e29642517a6cfbe76278f5cabc49ea2b30a2274bc25be9a75725ed9c656` |
| [wrong-generation 原 stdout](D:/riz-build-cache-next/engine-cancellation-managed-joint-r2-20261027/wrong-generation.stdout.log) | `31ab8986cea418e13663b6e0efd02b73dcdad9065173a9b6da207279e173a975` |
| 实际夹具 DLL | `b3621c3296046b8308dacdb76ea67781689ce33dfa235e0487f327fb7987241e` |
| 实际 EngineNodeKernel DLL | `1541feaa8fe7b37d0d3a544fbb315387ea93e3f6b96a4dc955dc76dc9c30e38a` |
| 实际 Operation JudgeContext DLL | `398d1c1ba90cbf55442c42a0b2b0197804b82ef1f575f0a26ee7942063809ec9` |
| 实际 AudioContent.Tests DLL | `6f99f046b0dde677b8f4dc4956fb2c8545d7ee8aaf3615d426773f07ea2959aa` |
| 实际 Adapter DLL | `853b021ec34e35a753e0071b6fb9bfdfd9d03c2631493ede4ac98be04dd81e24` |
| 原生 CPP r2 | `02fcc0d639280a73768a77b9726155d3a31baebd79fdd84fd581ef58018dc8af` |
| 独立 ASM | `b5344a1e22a484102d2751e24f4e8c9481670c32b02bca31a243793b35f0aa7f` |
| 托管联合入口源码 | `233103dc33feacfa898e76cf45a0b28dd3a252e45ad91c1c9a3c7cde50e1e4f8` |
| 共享只读判定器源码 | `1f8c485d1c963d033669001e6ad7f8fef909549e9f2b525c04c32a26d5d75f5b` |
| Build 脚本 | `e30cc88f74f5bfcfeefd0bef5281fa3b850acd5f06ee1dca6eeb86a29bb0607f` |
| Test 脚本 | `e9c744426cdf5fe3a1c96f27cf2767835f51f5a0bcd3f33ee2424e2bc285a80a` |

同批已有 `coordinator-tests.log` 28/28、`receiver-tests.log` 10/10、`audio-tests.log` 794/794 的实际终态日志，但旧 `engine-active-retirement-core-r3-20261027/test-result.json` 顶层仍为 `passed=false`，子检查错误为 `Source inventory changed.`。这是需要独立纠正和重验的归档范围错误，不能把这份旧父原件改称整批成功；该原件不会被覆盖改写。该旧父 JSON SHA256 为 `04a89da1807c4c9fec189fcfd5e9c16ed0f67004265f5e03eee3d9e629b00448`，其源码快照 ZIP 为 `f4f0a79033736d21302138ee0dff7dce92839a00a9f71ed60da7fe7a20b464be`。

修正归档 inventory 排除规则后，独立 [core r4 父原件](D:/riz-build-cache-next/engine-active-retirement-core-r4-20261027/test-result.json) 已实际 `passed=true / runnerExitCode=0`，子结果 `sourceHashesUnchanged=true`，分别为 coordinator 28、receiver 10、AudioContent 795 项通过；该父 JSON SHA256 为 `19c9ba34429ecad0a9f1a826c285f0bb39c5090af191c3bc721cab4b32dbf9dc`。本报告最终 joint r2 使用的正是该 r4 托管程序集，不能把这个新终态回填为旧 r3 顶层成功。

历史 joint r1 父 JSON SHA256：`f0f006ea0a970bd0059a40b6f4319ae5efbd4a849bd3a6d9eae9ea4892a69889`，路径 `D:/riz-build-cache-next/engine-cancellation-managed-joint-r1-20261027/test-result.json`。它与最终 r2 分别保留。

本 fixture 通过独立 owned DLL 加载和 Configure/AdoptConfigured 运行，不经过 V5 冷启动 publisher。前批 V5 五模块冷链和全 12-family 首次采用属于另一组证据，不能借本场景的 L/M/N/Stop 成功宣称它们被同时联合验收。

## 复核与复现

本机原件在 `D:/riz-build-cache-next`，本目录没有复制大型构建产物，因此换机复现需要先取得 manifest 列出的原始输入与对应 .NET/MSVC 环境。以下为实际脚本参数形式；输出目录必须是新的绝对路径，报告未再次执行这些命令。

```powershell
Set-Location C:/Users/71957/Desktop/riz_toolbox
pwsh -File scripts/Build-EngineCancellationManagedFixture.ps1 `
  -KernelDirectory D:/riz-build-cache-next/engine-wait-kernel-r2-20261026 `
  -OutputDirectory D:/riz-build-cache-next/engine-cancellation-managed-fixture-repro
pwsh -File scripts/Test-EngineCancellationManagedFixture.ps1 `
  -BinaryDirectory D:/riz-build-cache-next/engine-cancellation-managed-fixture-repro `
  -ManagedDirectory D:/riz-build-cache-next/engine-active-retirement-core-r4-20261027/checks/AudioContent/bin `
  -OperationHelper C:/Users/71957/riz-build-cache/engine-production-audio-checks-r2-20261026/RizToolbox.JudgeContext.dll `
  -OutputDirectory D:/riz-build-cache-next/engine-cancellation-managed-joint-repro
```

后续最短验收是补父层剩余物理来源交叉核验、增加非空 M 调度行和异常保留场景，再在获准游戏实测时验证真正生产来源签发、同版请求完成消费以及完整消费者 Close。现有证据不开放 RequestPlay 或生产取消完成。
