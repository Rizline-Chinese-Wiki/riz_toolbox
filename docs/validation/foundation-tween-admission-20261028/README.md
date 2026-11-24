# Tween 原生发行关闭与跳过语义

2026-10-28。本批已实现专用原生 Tween admission gate、Runtime 强根租约和 Adapter 接线，并产出 `artifacts/foundation-tween-admission-candidate-20261028/`。没有安装、启动或操作真实游戏。完整底层、Tween 工作退出与最终根释放尚未完成。

## 现在实际能做什么

正式 backend 从已保留的 Tween 对象建立独立强根，再申请原生 owner。原生 Claim 与 Play/Add 进入、Seal 使用同一把锁；调用尚在进行时 Seal 保持 Closing，退出后才成为 Sealed。Sealed 阻止该来源的新 Play/Add，已接纳的活任务继续 Execute/Update。Claim 忙状态 `6` 使用同一根重试；重复状态 `3` 不冒充可重试。

gate 精确对应 Play Void0/25、Add Reference1/20、Execute Reference1/19。确切 TweenInfo 的官方 `Object.m_CachedPtr` 为零时，这三类调用跳过原函数。永久类策略也覆盖未归属当前 owner 的 dead wrapper，是本批主动改变的行为。活的外部对象继续原调用；null、非确切类或不可确认输入保留原路径。所属对象的未知读取、异常或计数溢出保留责任并阻止退休。

原生 `Skipped=8` 与 Returned/Abnormal 分开。被跳过的调用仍配对 Before/After、完成 context 和 Dispose，但不增加 OriginalCalls，不生成成功的 Add 入队记录，也不把跳过后的图当作原函数结果。嵌套 Add 被跳过时，其父 Play 按自身实际路径记录。

Runtime 在配置前验证当前安装回执、完整 25 操作合同、实际 TweenInfo 及 Unity 官方父链、官方缓存字段和 IntPtr 布局。Read/Seal 前后检查配置 epoch、全局 Unknown 与实际对象回执。Adapter 的复制快照仅用于诊断；生产 authority 由实际绑定和同 owner 私有对象持有。

## 实际验证范围

| 实际执行 | 结果 | 原件目录（`D:/riz-build-cache-next/` 下） |
|---|---|---|
| 严格 helper 构建、实际 PE 导出核验 | exit 0，保留 `/W4 /WX` | `tween-admission-native-r2-20261028` |
| 原生 gate + operation dispatch 四进程 | 359/359；normal 131、unknown-read 38、abnormal 32、capacity 158 | `tween-admission-fixture-r3-20261028` |
| 当前 helper 完整映射发布 | 12 进程，合计 13185；含五个 Tween 函数体尾部篡改 | `tween-admission-mapped-r2-20261028` |
| 固定托管源码快照 | Audio/content 885/885、协调器 28/28、对象身份 10/10 | `tween-admission-core-r1-20261028` |
| 三套专项 | Runtime 237、Module 26、Hooks 199，共 462 | `tween-admission-managed-r1-20261028` |
| 同快照实际 CLR 采用 | 两进程，508 检查 | `tween-admission-adoption-r2-20261028` |
| 同快照六组件 Release | 成功；一个既有生成源码 nullable 警告 | `tween-admission-release-r1-20261028` |
| 完整候选打包和迁移后独立核验 | 24 载荷、23 证据，exit 0 | `artifacts/foundation-tween-admission-candidate-20261028` |

原生行为夹具编译实际 `TweenAdmission.cpp` 和 `OperationContext.cpp`，执行真实原函数、SEH、工作线程及五个物理通道。只有该 EXE 的 Ready 与官方 metadata 前提受控；没有加载生产 helper、执行游戏或证明实际游戏生产 metadata 准入。normal 检查 Closing 不提前 Sealed、五通道各自阻止 Unregister、dead 跳过、alive 转发、null 原异常、准确原调用数及 slot 重用时新 epoch。

候选 operation 合同仍为唯一 GetInfoV5、capability 3、25 操作、34 写入、2048 字节；新增六个专用 admission 导出。行为夹具的精确七个源码另存 ZIP，六个 native 输入必须与实际 helper 归档逐字节相同。迁移核验读取包内证据与固定游戏输入，不依赖生产者原输出路径。新增三份证据为夹具 manifest、实际结果和源码 ZIP。

## 保留的失败原件

`tween-admission-fixture-r1-20261028` 因 `/Fo` 尾部路径转义失败，编译器未收到源码；r2 严格编译发现夹具 unsigned int→pointer 的 C4312，并按 `/WX` 拒绝。修复路径引号及经 `uintptr_t` 的转换后，重新构建 helper-r2、运行 fixture-r3。前两次均未执行行为用例，未计入通过数。

## 还缺的实际功能

本批只关闭发行，`CanRetireWork` 仍为 false。C# 没有可调用的 Unregister 或最终释放入口，独立 admission 根和历史图根继续保留。原生 Unregister 的受控成功不等于正式 owner 满足完整退出条件。

下一批需要记录 Update 内部的 ready pop、playing 写入/搬移/清槽和 scheduled 交换删除，使同一 Tween 指针的多个排队实例有各自身份；同时关联 Update 内联 onStart/onComplete 的后续 effects。静态核验已发现 dead-tail 分支绕过清槽，第二 ready 路径可直接内联 onStart，不能靠等待队列自然为空补齐。实现路线见[工作退出计划](../../../.docs/foundation-expansion/TWEEN-WORK-COMPLETION.md)。

最终释放还要求同根死亡区间、队列和子 effects 全部退出、物理调用排空、私有一次性 Unregister→根释放账本。固定游戏不支持 `trackResurrection=true`；普通 weak 空值不能作为回收凭据。未来全部写者、UI 后续工作、生产首次执行准入和真实整局流程仍需完成。性能后置，不保留旧 SDK/API/包兼容要求。

## 冻结摘要

- 候选 manifest：`b83854aab028ee1c67d5b278d4b085a58ed75b427cf75623a2a0af049f228b30`。
- helper：`d8155b09a5737153f4ecefc13c87bf7370fa768c128186d6045f0b48e1eedfa0`。
- 原生构建 manifest：`e4b4b80d61fe272c82ea59fcb2898fd2e40eab66162d2476737fc311d9b22711`。
- gate fixture manifest：`842aad1bfc9d87cf6489f8d8b952b2f4efd1fc406d6997300a4538aa8e36ef9a`。
- gate fixture result：`fc6b54410199f0649934055165b1ebcab758511ccd5175e60ddf5374d25465c0`。
- 托管源码快照：`d4eaad021f2f90bbc7718f1a0776b923b3d3ad14776ce277f7f4b5f4156f2a19`。
- 三套专项结果：`df61a92d5329296ce7c544380f9e5f3482efc391f975adc5104ab0f93cccb049`。
- 当前 helper CLR 采用结果：`6ec4957a27df8ffc7e63475fccc63d2ce773170f4bd129b41804a07cb877460b`。
