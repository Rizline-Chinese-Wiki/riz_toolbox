# Wait 下一批固定合同与实施边界

2026-10-26，普通固定 PE 逆向，`flavor = null`。**已可开始三个窄原生通道：yield 发布函数、普通 wait 派发、批量 wait 清理，并为已知写入增加独立阶段回执。** `790000` 附近不是另一个函数入口：实际 `790069` 属于完整 CHAININFO 组 `78FC50..79014B`。wait 有自己的发布代次和在途责任；scheduler 行退休、Release 返回、Start inline End 或空输出均不能替代它。

范围继承 [生产静态研究 scope](../foundation-engine-production-20261025/scope.md)。按已使用的 ida-reverse / docs-generator 技能，只读固定游戏 DLL 的磁盘 PE；未加载 DLL、调用原函数、执行 native 产物、构建、启动服务或游戏。未运行会重写历史原件的旧研究脚本。新文件只有本说明和 [独立检查器](inspect-wait-sites.py)。

## Evidence → Finding → Path

| Evidence | 实际内容 / SHA256 | 可以支持的有限结论 |
|---|---|---|
| E1 | `D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll`；`43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`；31,006,120 字节 | 固定 AMD64 映像，ImageBase `180000000`，ImageSize `1E65000`；未执行 |
| E2 | [上一批路线](../foundation-engine-inline-20261026/next-scheduler-wait.md)与[既有 scheduler 静态合同](../foundation-engine-binding-20261025/scheduler-cleanup-research.md) | 已核字段/责任；本次不重写或扩大旧结果 |
| E3 | [inspect-wait-sites.py](inspect-wait-sites.py)；`c4fad49a159fff0a4ef17f98a4cb5d570ccecb07ba19289d35720c6c7644a8e9` | 独立 PE/Capstone 检查，固定 9 个函数组、60 个精确指令点；不泛扫其他代码 |
| E4 | [fixed-wait-sites.json](C:/Users/71957/riz-build-cache/engine-wait-contract-review-r1-20261026/fixed-wait-sites.json)；`c9f38983683ad656ef9383fba90bf8fd283cd2350418c3e8adf2abd32e7ed187` | 实际 `passed=true`；9 个完整函数，1580 条全部解码指令，60 个固定点及直接 call return PC |
| E5 | [wait-disassembly.txt](C:/Users/71957/riz-build-cache/engine-wait-contract-review-r1-20261026/wait-disassembly.txt)；`7956dc2b811af7cdbcd46946e34460540cf4061a12acfc534b7968ef487c7c6b` | 逐行全文，所有选中段 bytes/hash 和 `.pdata` 链在 E4；仅静态事实 |

E3 不依赖旧 JSON 提供函数边界。它读取 `.pdata`，按实际 anchor 找 RF，递归 CHAININFO，并合并所有同一 primary 的覆盖段；再核预期范围与 body hash，完整解码每一字节，不在第一个 `ret` 或 RF 截断。只为 9 个选定函数组解码指令；全表遍历仅用于解析异常展开元数据，未做广泛签名/字符串/xref 搜索。异常 handler-specific data 的源语言含义未解析，不把 unwind 存在等同任意 patch 的异常透明性证明。

| 完整 RVA 范围，左闭右开 | RF 数 | 完整 body SHA256 |
|---|---:|---|
| `78FC50..79014B` | 3 | `938b7a48664df2e0b0195fa2075f130f285a9498e0ce87693269dc2c6db3696f` |
| `5A5260..5A5343` | 1 | `7143eab8a6af6b584a341b1b41857b7ce9f74a4108987508ca803bdab1d11ccf` |
| `152540..1525D1` | 3 | `301335687736833f3e3d8c4044d6ce95bc108353cf973fae9ba65e42e2c64237` |
| `793840..793A5B` | 1 | `942344fdd9c188c5e9b8371d6e03ce8fb8f944bbfcd520cadd3ef5715dc5edf7` |
| `78F680..78F768` | 3 | `771128974dd1a81b49b7f6a9743a492a752cf30aa42bd95073df111cf159d1a3` |
| `792240..7929BF` | 1 | `6a24dc40b10797a33529428b7d82821f1cb646a72d9108cccb884bfe2f8d7721` |
| `5DA510..5DA6DB` | 7 | `01f8cbd43685808116a8c8e34fe4cf7053de54595988ae8ade76676b351d6657` |
| `5DACD0..5DAFD2` | 3 | `2e17bb4737d2a10ae76126c8c8507748a757f52a4bf8ff0eb0795492ce317ffa` |
| `342400..3426F6` | 7 | `fcac2902e4f66db63d05767341038d12af5429463bf9254a9e2d0396efb71e60` |

## F1：发布必须记录实际写入分支

`status=validated(static)`，`evidence_ids=[E1,E4,E5]`。

`78FC50` 实际输入 RCX=node、RDX=managed yielded reference。`78FFC9` 从相应 wrapper `+10` 得实际 wait；`78FFD6` 已增加 node 引用，`78FFE0` 调 wait 的 vtable `+8`，`78FFE5` 按实际 AL 非零转 tree 分支。`78FFEB/78FFF0` 检查 callback 槽是否已有值，只有空槽才进入 `79005D..7900A0` 的 wait 发布。进入 `78FC50` 或它正常返回，均不能计作 wait 发布成功。

| 真实阶段 | 固定点及实际值 | 允许签出的事实 |
|---|---|---|
| `PublishBegin` | `790069` 第一条写 wait `+28=EBX` 前；RDI=wait，R14=node，EBX=owner ID | 新发布尝试；复制实际旧 tuple、node 身份、当前实际 yield scope。部分写入或异常必须保留未确认责任 |
| 实际 tuple 写入 | `790073` callback=`78F5E0`；`79007E` cleanup=`78F680`；`790082` payload=R14 | callback/payload 不是 scheduler row；字段写入是多条非原子指令，不伪称一致事务 |
| 旧引用转交 | `790086` 读旧 node `+78`；非空时 `790093→342400(old+8)`，return PC=`790098` | 旧 wait 的 shared reference 已归还；此路径没有直接清旧 callback/cleanup/payload，旧发布代次不能被新字典覆盖掉 |
| `PublishCommitted` | `790098` 写 node `+78=RDI`，`79009C` lock inc wait `+0C`；`7900A0` 已到成功分支终点 | 同一次实际发布走完这些已知写入；仍等外层函数真实 Exit，不等于整个 wait future 来源封口 |

最短 P1：用真实 `Yield` 物理 scope 围住上述内部点；Begin 先登记责任再读标量，Committed 使用 Begin 身份与保存寄存器，检查同 scope/同 wait/node，不能靠 caller 提供 sequence。每个 wait 地址可有多个历史 publication generation。旧 wait 的引用归还和新 wait 的提交分别记录；不可只保一个可覆盖的 node→wait 槽。

## F2：普通 dispatch 不按 callback EAX 决定 cleanup

`status=validated(static)`，`evidence_ids=[E1,E4,E5]`。

`5A5260` 的 RCX 是 wait。先在 `5A5269` 检查 callback 非空，再经 `5A5277→152540(&wait.ownerId)` 取实际 native owner；`152540` 的 ID=0 分支在 `152553` 返回 null。`5A527F` owner-null 跳至 `5A528B`，只跳过 callback，**仍可能执行 cleanup**。

| 原始指令 | 原始 return PC / 后续 | 记录要求 |
|---|---|---|
| `5A5288: call [RDI+10]`，RCX=实际 owner，RDX=`[RDI+20]` | `5A528B` | 关联真实 `78F5E0` callback Enter→Complete→Dispose；owner mismatch 返回 EAX=1、不进入 Execute 也保持独立 callback scope |
| `5A528B: mov RAX,[RDI+18]` | `5A528F` test / `5A5292` 条件跳转 | callback 返回后重新读取 cleanup；不能沿用 dispatch Entry 的假定不变 tuple |
| `5A5294: mov RCX,[RDI+20]`；`5A5298: call RAX` | `5A529A` | payload 也重新读取；关联真实 Release，而非把 callback EAX=1 当 cleanup 已发生 |
| `5A529A: mov [RDI+10],0` | `5A52A2` | 仅清 callback；没有在这里清 cleanup/payload/owner ID |
| `5A530E→3B4210` | `5A5313` | 另有 managed 通知；wait callback 已清也不能提前当整个物理 dispatch 已退出 |

最短 P2：新增实际 `WaitDispatch` 外层通道；owner 查找结果、实际 callback、实际 cleanup、清 callback、函数正常/异常退出各留责任。目标不是重新 hook owner lookup 或全局 `342400`。普通 callback 完成后 cleanup 可以用新值，必须核 actual callback/Release token、caller PC、当前 wait scope 和 publication generation；tuple 改写不能借旧入口快照签完成。

**与 M1 的必要交接**：同一个 `78F5E0` 也由 wait 调用。现 scheduler RowLedger 对已知 node 且没有 Consume/Drain source 的 callback 会拒绝。M2 必须让 callback 观察协调器依据实际 `WaitDispatch` 私有 proof 分流：wait 账本承接，scheduler 行账本保留物理 mask；不能捏造 `SourceConsume`，也不能简单放行所有无来源的已知 node callback。真实 Release 的 Complete/Dispose 仍只由原 node owner 执行一次，wait 账本附加自己的 cleanup duty，不能重复 Complete node call。

## F3：owner 停用、完整字段撤销、引用归还分开

`status=validated(static)`，`evidence_ids=[E1,E4,E5]`。

| 路线 | 机器码事实 | 结论边界 |
|---|---|---|
| Detach direct/nested | `7938B3` / `793983`：RAX=实际 wait，EBP=0，只写 wait `+28=0` | 仅阻止以后正常 owner lookup；已进入 dispatch 的 callback/cleanup 和非零字段仍保留 |
| Release 最后清理 | `78F6F5/78F6F8/78F6FC/78F700` 清 wait `+28/+10/+18/+20`；`78F70C→342400`，return=`78F711`；随后清 node `+78` | 清字段、归还引用、清 node 的 wait 关联分开；引用归还后不再解引用 wait |
| Start inline 最后清理 | `792527/79252A/79252E/792532` 清同四字段；`79253E→342400`，return=`792543`；随后清 node `+78` | 新 wait 身份必须在清字段前缓存；旧 inline Begin/End ABI 没有 wait 字段，不可据其 End 猜出实际 wait 地址 |

最短 P3：新 wait side family 增 before-clear 身份、fields-cleared、shared-ref-returned 与 actual node outer completion/abnormal。需由原四通道真实 Dispatch 内部转交其不可伪造的 native scope（最少 Start/Release/Detach），或提供等强度的 native private proof；不导出允许调用方设置 current NodeSequence/StartSeq 的 setter。64-entry CAS 线程表、同槽/epoch/target、original phase、观察者重入 mask 与 LastError/异常 finally 沿既有合同。外层没有实际 scope、foreign 嵌套或 worker 不得借祖先；末尾只用缓存，不读已归还 wait 或已释放 node。

## F4：批量清理须逐对象记录

`status=validated(static)`，`evidence_ids=[E1,E4,E5]`。

`5DA510..5DA6DB` 接收 RCX=manager；其 `+190` 容器对象在 `5DA637` 被取到 RBP，RSI 保存 manager、RDI 是索引。`5DA63B/5DA63F` 看 object callback `+10`，非零时 `5DA645` 直接调 `[object+18]([object+20])`，return=`5DA648`，然后清 `+10`；此处没有普通 dispatch 的 cleanup-null 检查。无 callback 的行跳至 `5DA64C`，仍在 `5DA65B` 归还对象共享引用，return=`5DA660`。

最短 P4：真实 `Batch` scope 内 `5DA63B` 建逐对象 receipt，`5DA660` 正常结束，缓存 wait/index/manager/tuple；End 不回读可能已被 shared-ref 释放的对象。callback 为空时不能虚构 cleanup。异常缺 End 保 Unknown/未闭合责任，外层函数返回不代替逐对象配对。此容器与每一种 yielded object 的实际生产归属尚未全部证明。

## 内部阶段点的最小机器窗口

以下从 E4 的完整指令边界合并到至少 5 字节，仅作为下一步 relay 设计输入，**不是已验证可装 patch 的许可**。相对跳转、RIP 地址和原调用的 return PC 必须正确重放；每个 relay 另验 unwind/CET/flags/LastError。

| 建议点 | 最小完整指令跨度 / 下一原 PC | 注意 |
|---|---|---|
| PublishBegin `790069` | 10 / `790073` | 重放 store owner + RIP-relative LEA；RDI wait / R14 node |
| OldRef decision `79008D` | 6 / `790093` | 重放 JE 到 `790098` + ADD RCX,8；捕获实际 RCX 旧 wait，零分支不期待引用归还。保留原 `790093` CALL 在原位置 |
| OldRefReturned `790098` | 8 / `7900A0` | 在 replay 写新 node.wait 与 lock inc 之前发旧引用回执；不读旧对象 |
| PublishCommitted `7900A0` | 5 / 原 JMP 目标 `79012E` | 这是 JMP，不可落到 `7900A5` 的 tree 路径 |
| DetachOwnerRevokeBegin `7938B3` / `793983` | 各 10 / `7938BD` / `79398D` | store owner + RIP MOV 之前缓存实际 RAX wait、RDI 当前直接/nested node；此时只是尝试，须 replay 后回执或同一 Detach 正常 Complete/Dispose 才确认 owner 已清，不等同最终字段撤销 |
| ReleaseClearBegin `78F6F5` | 7 / `78F6FC` | RAX wait / RBX node；重放前缓存 |
| ReleaseFieldsCleared `78F704` | 8 / `78F70C` | RCX load wait + ADD,8；原 CALL 仍在 `78F70C`，保原 return PC `78F711` |
| ReleaseReferenceReturned `78F711` | 8 / `78F719` | 清 node.wait + CMP；用缓存，保持后续 flags |
| StartClearBegin `792527` | 7 / `79252E` | RAX wait / RBX node；独立于原 inline ABI |
| StartFieldsCleared `792536` | 8 / `79253E` | 保留原 shared-ref CALL 位置和 return PC |
| StartReferenceReturned `792543` | 9 / `79254C` | 清 node.wait + CMP；不解引用 wait |
| DispatchOwnerResult `5A527C` | 5 / `5A5281` | TEST RAX + JE `5A528B`；可记录实际 lookup owner/null |
| DispatchCallbackArmed `5A5281` | 7 / `5A5288` | 重放 RDX payload / RCX owner；原 callback CALL 位置与 return PC `5A528B` 保持 |
| DispatchCleanupDecision `5A528F` | 5 / `5A5294` | TEST RAX + JE `5A529A`；RAX 是 callback 后实际重新读出的 cleanup target |
| DispatchClearReached `5A529A` | 8 / `5A52A2` | actual cleanup 原 CALL 已返回；replay 清 callback 后配合实际外层正常 Exit 可确认本路径清空。异常后不签正常完成 |
| BatchObjectBegin `5DA63B` | 6 / `5DA641` | CMP + JE `5DA64C`；RBP wait / RSI manager / RDI index |
| BatchObjectEnd `5DA660` | 10 / `5DA66A` | INC index + CMP；保原后续 JB flags；不读已归还 wait |

在写入前命中的探针只发 `Begin/Reached`，不能把尚待 replay 的 store 当已发生；相应写入完成须由另一个已执行后点，或固定直线路径配合同一次正常外层 Complete/Dispose 证实。**不能直接 patch `5A5288` 或 `5A5298` 再在 relay 中普通 CALL 重放**：这会改变已有 callback/cleanup 看到的真实 return PC，破坏 `5A528B/5A529A` 的来源合同。上述前置窗口刻意保留原 call 指令；也不要同时安装互相覆盖的 `5A528B` 七字节与 `5A528F` 五字节探针。源 PC 和 relay CALL 的 return PC 是两种身份，side config 必须分别保存。

## 下一步精确分工与验收

以下是接下来可并行实施的文件所有权建议，需根先冻结新 ABI，再由唯一 builder 统一构建；本次没有创建这些源码或修改旧合同。

| owner | 下一批专属文件 | 交付 |
|---|---|---|
| native transport writer | 新 `native/RizToolbox.JudgeContext/EngineWaitContext.h/.cpp`、`EngineWaitEntry.asm` | 独立 `RizEngineWaitContext_` namespace，3 个 typed channel（Yield/Dispatch/Batch）+阶段 side ABI；raw 4GPR→full RAX carrier，禁止把未证返回值归一化成 bool。永久配置/冻结观察者/实际 call-parent-root-seq、部分发布、异常结束、逐对象缓存。不要改旧保留字段 |
| root/native 集成人 | `EngineNodeContext.cpp` 最少内部调用点、Kernel build 脚本 | 将实际 Start/Release/Detach scope 交给新 side family，仍由原四通道完成原调用；loader 发布合同继续独立且默认关闭。协调 source/relay PC、拒绝 overlapping windows |
| managed transport writer | 新 `src/RizToolbox.Runtime/Hooks/NativeEngineWaitContextBridge.Types.cs`、`.cs`、`NativeEngineWaitObserverDispatcher.cs` | 身份/布局和 actual private scope proof；foreign 也占物理栈，worker 不读 provider；side capture/typed capture/frozen logical membership不相互借代次 |
| adapter writer | 新 `src/RizToolbox.Adapter/AudioContent/NativeEngineWaitLedger.cs`、callback 分流协调器；仅根改现 `NativeEngineCoroutineRuntimeCoordinator.cs`、`NativeEngineSchedulerRowLedger.cs` 接口 | publication generation、旧引用、owner revoke、字段 revoke、dispatch/batch callback/cleanup、外层 duty。接 actual node identity/call，不重复执行它的 Complete/Dispose；M1 scheduler 与 wait 各自保责任 |
| owned fixture writer | 新 `native/RizToolbox.EngineWaitFixture/`、对应独立 Build/Test 脚本 | 自有函数和相同窗口的真实 E9→relay CALL→replay；真实清字段/归还引用/释放后 End 不读，typed+CLR 联合验证，保留失败目录与 SHA |
| managed tests writer | 新 `tests/RizToolbox.AudioContent.Tests/NativeEngineWaitTests.cs`；root 注册组 | 已知 node 实际 ledger+dispatcher组合，测试源/代次/真实 token/Complete→Dispose 顺序，不用任意 scalar 帧伪造生产 authority |

首批必要场景：成功发布与占用/isDone fallback 区分；旧 wait 替换仍保留旧代次；owner null 跳 callback 但仍 cleanup；callback 原异常与 cleanup 原异常；callback 重入改变 cleanup/payload；Detach 仅清 owner 后旧 dispatch 已在途；完整清字段与 shared-ref 归还分开；Start/Release 最后清理后 End 缓存；批量 callback-null 跳过 cleanup 但仍归还引用；nested foreign/observer 重入 mask；地址复用、late Attach 和主线程/worker。每项观察故障保原行为与 Unknown，不靠静默若干帧认定结束。

明确未闭合：全部 wait 字段写者；全部虚派发/自定义 yield；批量容器与所有 yielded 对象归属；多字段快照在实际线程/锁下的一致性；剩余消费者/managed 通知；生产首次安装安全。E4 的 `allWritersProven`、`allDispatchersProven`、`futureSourcesClosed`、`engineCancellationCompleted` 均为 false。已知普通派发实现和 9 个完整静态函数不代表这些全局边界已证明，不能授 EngineCancelled、全 launch Pending 解除或资源根释放。

## 重跑

检查器只接受不存在的绝对输出目录，避免覆盖 r1 原件。第一次运行使用本次 r1 路径；重跑请换新的目录名，例如：

```powershell
C:/Python314/python.exe C:/Users/71957/Desktop/riz_toolbox/docs/validation/foundation-engine-wait-20261026/inspect-wait-sites.py --output C:/Users/71957/riz-build-cache/engine-wait-contract-review-r2-20261026
```

固定映像 hash、范围、任一指令或 CHAININFO 不合即退出失败，不创建成功回执。输出包含完整 bytes/hash、所有选中指令、各 RF 展开字节、call 原始 return PC 和工具/脚本 SHA；它们仍是磁盘合同，不是 native/CLR 或游戏动态结果。
