# scheduler 逐行责任与真实回调阶段报告

2026-10-26，`flavor = null`。本阶段实现并验证六个 scheduler 通道、Drain 逐行观察和实际 C# 账本接线。完整 PC Mod 底层仍未完成；生产发布、wait、取消、全部消费者及游戏验收继续推进，B01–B19 验收状态不变。本阶段按用户要求仅处理代码和自有进程，没有安装或运行游戏。

## Evidence

下表目录均位于 `C:/Users/71957/riz-build-cache/`。回执及其原件路径、SHA 在冻结索引中逐条列出。

| ID | 证据 | 结果与边界 |
|---|---|---|
| E1 | [冻结索引](C:/Users/71957/riz-build-cache/engine-scheduler-evidence-r1-20261026/evidence-index.json)，SHA `507c2c5758953dcff1f7bd0a8323daf168ff2e686722b6ba45637a8c8e3d30e4` | 实际收集命令退出 0；复核 8 份完成回执和相应文件哈希；22 项原生/检查来源、210 项执行原件、465 项管理输入分别归档 |
| E2 | `engine-scheduler-contract-r1-20261026/fixed-sites.json`，SHA `5baa9115952c89142152d90806d70627ef7b2cd5cff470f1db4b07f171e73472` | 六个完整固定函数、两处 Drain 重放窗口；只读 PE 分析，不是可安装凭据 |
| E3 | `engine-scheduler-kernel-r2-20261026/scheduler-pe-verification-r1.json`，SHA `73a1ded02a525c0384f7a9e3b064d92ab50188363e0b08c97f1b21e47ae7a957` | 严格原生构建退出 0，33110 项磁盘检查通过；25 个导出、7 个系统导入，无 CRT、PE 入口或 TLS |
| E4 | `engine-scheduler-cases-r1-20261026/fixture-result.json`，SHA `44566304777721e35eff029dbb6de8947ae5d208cc6d189ed7536356f81d83e8` | root 实际执行 16 个自有进程，全部退出 0；3718 断言，包括实际 E9/relay/CALL 重放和运行时展开 |
| E5 | `engine-scheduler-managed-checks-r4-20261026/test-result.json`，SHA `2fe0042cff348bac67b8505d06c1eb9449dc262b4498b944e25a123337347ec6` | Audio/content 742/742、Runtime 23/23、对象身份 10/10；465 输入编译前归档、执行后哈希不变。Runtime 零警告；Audio 保留 17 条已有测试夹具警告 |
| E6 | `engine-scheduler-managed-cases-r3-20261026/fixture-result.json`，SHA `d4b63981e4b87fc739fdf3b03050781616e09a6870fd11a6635ae1da6e02c10d` | root 实际执行七个 Native→CLR 进程，全部退出 0；654 托管和 548 原生断言。实际 helper→bridge→dispatcher→同一套 identity/node/row ledger；tracker identity provider 受控，未执行真实 tracker backend |

固定 UnityPlayer SHA 为 `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`，r2 Kernel SHA 为 `5c59a29430a4ca1a183b155ed1b6e17358d0831794ac3c7578d15c2f95fe126f`。实际原生与托管构建、执行均由主代理完成；代理并行负责实现、独立审查和夹具，未用代理的“完成”消息替代执行证据。

## Finding → Path

| Finding | 实际改变 | 对应路径 |
|---|---|---|
| F1：移出队列后仍有回调责任 | Register、Remove、Consume、Pump、Drain、Callback 使用独立原生 scope；完整 RAX 保真，callback 条件比较完整 EAX；保留消费后的真实空窗 | E2/E3/E4 → `EngineSchedulerContext.h/.cpp`、`EngineSchedulerEntry.asm` → `NativeEngineSchedulerContextBridge` 与 `NativeEngineSchedulerObserverDispatcher` |
| F2：行不是裸 node 地址 | 精确 tree/entry/登记代次与实际 node generation、Start sequence、identity 关联；repeat 先登记新行再消费旧行，不伪造节点引用增加 | E4/E5/E6 → `NativeEngineSchedulerRowLedger` 与 `NativeEngineCoroutineIdentityLedger.TryGetSchedulerNode` |
| F3：清理必须经过实际 Complete/Dispose | 先实际 Identity/Node Complete，再 scheduler cleanup Complete；先 Identity/Node Dispose，再 cleanup Dispose；Drain 还须同一行 End 和外层 Dispose | E5/E6 → `NativeEngineCoroutineRuntimeCoordinator.NodeContext`、`NativeEngineSchedulerRowLedger.CleanupContext` |
| F4：Pending 不等于 Unknown | 单独按已选中的实际节点代次检查行是否退休；待处理行可重试，其他节点不阻塞该记录；签发与每次根释放前重查，已开始 free 后失去边界则保 Unknown | E5/E6 → `MayRetireNodeGeneration`、`NativeEngineCoroutineObserverRetirementAuthority` |
| F5：外部调用和重入不能借祖先身份 | foreign callback 不留下等待已知 callback 的假 pending；观察者重入遮蔽外层 Drain row；逻辑退订保留当前真实原函数及 closing context 的清理职责 | E4/E5 → 原生 source 匹配、`NativeEngineNodeObserverDispatcher`、`NativeEngineRuntimeCoordinator.Subscription` |

四个原节点 ABI2、Start inline ABI1、scheduler ABI1 和私有联合夹具 ABI2 分开。现有旧生产发布合同不包含新 scheduler/inline 的安装与采用证明；`TryAcquire` 的生产安全门继续关闭。不能用自有 `AdoptConfigured` 或受控 `ShareControlled` 绕过生产发布。

## 失败与证据修正

原生独立夹具 r1 的 ASM 类型错误和管理 r1 的五处 Call 类型错误已修，失败原件保留。两项原生逻辑缺陷由独立审查发现后修复，并增加实际 foreign callback、observer reentry 场景验证。

联合首轮 address-reuse `0xC0000602` 按控制流定位到强制重新分配旧系统地址的失败路径，当次未捕获 LastError。最终在自有 reservation 内真实 decommit/recommit，分别核两次 decommit、一次 recommit 和最后 reservation 释放，明确不证明系统地址 MEM_RELEASE 后重新获得。

联合第二轮 normal 又证明跨 CLR 后裸地址可能已被重新分配。最终六个非复用模式严格核真实 `VirtualFree(MEM_RELEASE)` 成功和每行操作回执，gap/Drain End 的当前页状态只作诊断；不把当前地址状态当成旧代分配的身份。独立 native 16 场景的实际 MEM_FREE 与无回读检查没有放宽。最终 private Descriptor216/Receipt600 与 public scheduler 帧布局分开验证。

## 复核与下一步

[本批说明](validation/foundation-engine-scheduler-20261026/README.md)列出原件目录；[collect.py](validation/foundation-engine-scheduler-20261026/collect.py)检查实际回执并生成冻结索引。原生 Build/Test 入口为 `Build-EngineNodeKernel.ps1`、`Build/Test-EngineSchedulerFixture.ps1`、`Build/Test-EngineSchedulerManagedFixture.ps1`；管理完整回归入口为 `scripts/Test-EngineSchedulerManagedChecks.ps1`，均要求新的绝对输出目录。源码继续变化后，复核历史回执应使用归档中的对应来源，不能把新工作树与旧 SHA 混用；本批未宣称换机完整复现。

下一步按 [wait 固定合同与分工](validation/foundation-engine-wait-20261026/contract-notes.md)接三条 typed 通道及发布/清空阶段。新的静态检查已实际核 9 个完整函数、1580 条指令、60 个位置，尚无 wait 原生或 CLR 动态产物。wait 与 scheduler 共用 `78F5E0`，必须依据实际 WaitScope 分流，不能假造 Consume 或把所有无来源 callback 一概放行。

仍未证明：全部 wait 写入者、全部未来任务来源关闭、取消完成、全部消费者最终结束、完整根释放、真实生产首次安装与游戏冷启动/整局/暂停/重试/返回菜单/多插件共存验收。`EngineCancelled=false`，完整目标保持 active。
