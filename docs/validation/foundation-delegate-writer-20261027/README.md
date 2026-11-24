# 委托写入口：最新 20 操作接线与验证

2026-10-27。`InvokableCall.add_Delegate` 和 `remove_Delegate` 已接入真实预初始化发布、CLR 只读采用及 Adapter 的事件归属账本。本轮实际完成原生构建、七个映像场景、两个 CLR 采用进程、857 项音频/内容回归及最新候选打包。**没有安装或运行真实游戏，也没有执行复制的原游戏函数；完整未来写者封口和生产首次启动准入仍未完成。**

项目尚未发布，按用户最新要求统一当前协议，不维持旧 18 操作协议的采用分支；性能优化后置。本报告只说明本轮功能和证据，不将历史回归总数算进本轮。

## 实际调用链

原生发布器先对固定 GameAssembly 的完整函数、展开信息及全部目标预飞，然后统一 Prepare → Configure → Commit。最新唯一发布出口为 `RizFirstInstall_GetInfoV4`：1728 字节，前 416 字节为内嵌 listener 数据，操作头 32 字节，随后 20 行、每行 64 字节；操作 capability 为 2，完整 mask 为 `0xfffff`，预期写入 29 次。14 个 UnityEvent 操作加 6 个 Stop 操作共同属于该次发布。

新增的两个 Reference1 操作是：

| 操作 | ID / slot | 固定函数范围 | 独立核验 |
|---|---|---|---|
| `InvokableCall.add_Delegate(UnityAction)` | 12 / 27 | `0x154e890..0x154e923`，147 字节 | 完整函数与固定展开字节 |
| `InvokableCall.remove_Delegate(UnityAction)` | 13 / 26 | `0x154e930..0x154e9c3`，147 字节 | 完整函数与固定展开字节 |

两者的实际实现循环读取并通过 CompareExchange 更新 `Delegate` 字段。传入的 action 不是最终多播链；添加、移除、并发重试及移除最后一个 action 都必须在正常 After 重新读取实际结果。

CLR 从已加载 producer 的实际 V4 记录取得全部 20 个通道，核对目标、原函数、entry、epoch、slot、实际内存保护和 Windows runtime-function 身份后 Attach。`ResolveUnityEventOperation` 另核精确非泛型 `InvokableCall`、实例 `void(UnityAction)`、MethodInfo 布局和固定 RVA；不允许以另一 MethodInfo 或共享地址冒充。此路径不在运行时另装补丁。

Adapter 从已保留根的 Initial、Pre、Post、Inline、Dispatch 完整快照登记历史 invokable → event 归属。一份 invokable 可关联多个 event；从当前列表移除后，其历史根和责任仍保留。Before 先采用实际 self/action 引用，再捕获该 invokable 的委托链；正常 After 从同一保留根重读最终链并核对两次读取和同时捕获的事件图。异常、错完成、worker 或部分根采用失败不会补造成功的 After。

主要实现位置：[原生发布](../../../native/RizToolbox.JudgeContext/FirstInstall.cpp)、[固定操作声明](../../../src/RizToolbox.Runtime/Hooks/UnityEventOperationProfile.cs)、[CLR V4 采用](../../../src/RizToolbox.Runtime/Hooks/NativeFirstInstall.cs)、[事件归属与 mutation](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.Mutation.cs)、[实际委托链读取](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.Mutation.cs)。

## 已实际结束的验证

| 证据 | 实际结果 | 能证明的范围 |
|---|---|---|
| [固定 profile 核验](D:/riz-build-cache-next/delegate-writer-profile-r1-20261027/profile-verification.json) | 20 操作 / 29 写入，passed | 独立读取固定文件，核完整函数、RF、展开字节及 profile 描述；不执行函数 |
| [原生构建 r2](D:/riz-build-cache-next/delegate-writer-native-r2-20261027/native-manifest.json) | strict `/W4 /WX` 构建成功，66 项输入不变，实际导出核验通过 | 最终 helper 与源码归档、导出集一致；此构建 manifest 明确 `fixtureExecuted=false`，夹具运行另列 |
| [映像发布 r2](D:/riz-build-cache-next/delegate-writer-mapped-r2-20261027/fixture-result.json) | 7 个独立进程 / 6383 项检查通过 | 正常发布及 source、post、relay、chain、两个新操作实际篡改拒绝；20 行、29 写入、保护和实际 OS 展开身份。使用未初始化的映像，不执行游戏指令 |
| [Unknown 竞争](D:/riz-build-cache-next/delegate-writer-unknown-r1-20261027/fixture-result.json) | 896 项通过 | 实际 native writer 细节更新暂停时，Unknown 先原子可见；不因旧 Ready 快照恢复准入 |
| [拒绝矩阵](D:/riz-build-cache-next/delegate-writer-rejection-r1-20261027/fixture-result.json) | 200 项 / 5 进程通过 | 错路径、空或不符模块等拒绝，未开始源写入 |
| [实际 CLR 采用](D:/riz-build-cache-next/delegate-writer-managed-adoption-r1-20261027/fixture-result.json) | 2 进程 / 423 项通过；正常 324，重入 99 | 同进程读取真实 producer、20 个真实 Attach、共享、逻辑退订及错目标/ABI/MethodInfo 拒绝；重入后永久拒绝。metadata 是受控载体，实际 IL2CPP metadata 和回调未执行 |
| [组合源码 r2](D:/riz-build-cache-next/delegate-writer-core-r2-20261027/test-result.json) | Audio/content 857/857、协调器 28/28、receiver 10/10 | 新增 13 个委托 writer 账本场景及原功能回归；Runtime 构建零警告，Audio 夹具保留 18 警告，均零错误 |
| [Runtime / Hooks 专项](D:/riz-build-cache-next/delegate-writer-runtime-hooks-r1-20261027/test-result.json) | Runtime 207/207、Hooks 189/189，两个构建均零警告/错误 | 最新 schema、resolver、拒绝和相关运行时回归；不是完整全部托管套件的重跑 |
| [本候选安装矩阵](D:/riz-build-cache-next/delegate-writer-install-r1-20261027/test-result.json) | 17/17，通过；root 实际 session16030、exit0 | 实际安装器在合成目录中的安装/重装、哈希与证据拒绝、完整回滚、中断恢复、卸载保留和只读校验；没有装入真实游戏或执行 native payload |

13 个新账本用例实际调用同一 ledger/context/history 算法，覆盖最终多播链、最终空字段、共享 invokable、移出列表后写入、快照不变、嵌套、Before/After 失败、异常、错完成、worker、外来 receiver 和类漂移。**该组 backend 是受控观察，不是原生 CAS 执行证据。** 映像发布组和 CLR 采用组也不执行新增游戏函数，不能把三组拼成一次游戏内委托写入成功。

初次 [core-r1](D:/riz-build-cache-next/delegate-writer-core-r1-20261027/test-result.json) 因 Backend.Mutation 缺 Runtime 命名空间引用而编译失败；失败原件保留。补齐引用后的 core-r2 才计为通过。原生 r1 的构建/运行记录未具备完整独立来源归档，正式候选使用 r2 strict build 与独立导出核验，不追写旧 manifest。

## 已产候选与剩余工作

最新候选为 [foundation-latest-20op-candidate-20261027](../../../artifacts/foundation-latest-20op-candidate-20261027/transport-candidate-package-manifest.json)，24 个 payload、20 份 evidence。包内 JudgeContext SHA256 为 `81380afdb828121aec5ab5ee744a218546fbe517d0b86137839b4cdb07fb6db7`，与实际 20 操作原生构建和 CLR 采用一致；六托管组件来自 core-r2 的冻结源。包的 V5 engine transport 版本与本次 JudgeContext V4 operation publication 是不同协议，不应只凭一个 version 数字混称。

打包器核验实际 native build、profile、映像结果、CLR 采用、源码归档和最终 payload 身份后才完成。包保持 `productionAdmissionGranted=false`、`executionWindowProved=false`、`gameExecuted=false`、`installed=false`，没有安装或启动游戏。

安装结果 SHA256 `91415396e246d6ab9be0209ad36438accd9e670b35153096db0ea52c792ca3ae`，绑定上述候选 manifest。矩阵额外替换实际 V3 helper 并重新绑定外层哈希，确认真实 PE 导出核验仍拒绝；缺 operation build 证据、错误源码快照的 adoption 证据同样只读拒绝。旧协议包升级/降级兼容矩阵不再执行。

下一项功能缺口是序列化、直接 field/list 写入、泛型或共享 invokable 和外来 event 引用的实际来源封口，以及任意非空 listener 异步效果的真实 owner/终结回执。本轮只补两个确定遗漏的 writer，不将 Add/Remove 返回、调用数归零、最后一次空集合或 listener 移除当作这些责任结束。现有退出 verifier 必须同时消费过去效果与私有 writer-source 证明；缺来源仍具名 Pending，不能因本轮通过而开放整局 Close。

真实固定游戏首次执行窗口、生产 startup authority、同一进程完整取消/退场/所有资源退休及用户恢复后的游戏验证另行推进。旧框架兼容和性能优化均不作为当前功能施工的阻塞。

## 关键原件身份

下列 SHA256 均在写报告时从实际原件重新计算。完整逐文件清单见对应 JSON 和包内 evidence，不在正文复制。

| 原件 | SHA256 |
|---|---|
| core-r2 `test-result.json` | `a7ffce7920504c974ab71305489e11ea700bd9c25575ed83616486e6e57b0e1d` |
| Runtime/Hooks `test-result.json` | `8379a136fa8639ba05ceb5cdd966856738562904e0a18d4f7693998a1e1fa8e9` |
| native-r2 `native-manifest.json` | `05d11d24829f749de0cb53eaa52840bb833d87fb69a55e95f7fe8e4d373b4f6a` |
| mapped-r2 `fixture-result.json` | `c75b51a99b00b0851fe3a11ee9e2612e878f70295c5a2c20d5e8c63b3abc29cc` |
| CLR adoption `fixture-result.json` | `dfe3468e870e816444cdbdf37dbd30b31fe6972f6115a0804be9162444a5e426` |
| 最终候选 manifest | `69839aeed47ac64f9f44060cf7fa1bfc3e4301cce17c51f2e26d1423a9605117` |

本报告只读取源码与已有结果、写入此说明页；没有另行构建、运行夹具或操作游戏。
