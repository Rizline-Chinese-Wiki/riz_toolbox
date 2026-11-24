# Tween 子事件完整登记与实际空回调结束

2026-10-28。已将 owned Tween 的零参数 onStart／onComplete 在激活前登记到已有 UnityEvent 账本，复用实际 Probe、Invoke、Prepare、mutation、listener dispatch 和 effects generation。本批已经过源码快照回归与六组件构建，并打包核验；没有安装、启动或操作真实游戏。

## 功能

`NativeLocalTweenBackend.ChildEvents.cs` 只从同 launch 的实际 preactivation backend 借出已经强根保留的 Tween、官方实例字段及事件根。[私有 membership](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.ChildEvents.cs)绑定 work、listener owner、launch、源 Tween、精确 onStart/onComplete 字段和实际事件身份。它通过 backend pull 回调发行，不能从任意 DTO 构造。

[Listener 后端](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.TweenChildren.cs)先取得并记录独立事件根，再保留 slot 责任，然后完整读取 graph。共享事件复用一个 slot，两个来源 membership 仍独立。任何 graph／slot 失败保留已取得的根和 reservation，不能复用失败 slot。此登记发生在首次真实 Invoke ProbeEnter 之前，避免把已经进入的 mask 临时升级后漏记物理计数。

[登记账本](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.TweenChildren.cs)将实际 inline source token 与正式 EventRecord、具体 ListenerEffectInvocation 关联；context 复用后仍保留每个原始 generation。原 Trigger 集合与子事件 membership 集合分别核对，二者的实际并集才是当前已登记事件集合；追加 child 不扩大原 persistent Tween 来源枚举。

[实际空 effects 回执](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.ChildEffects.cs)只在真实执行列表为空、post 图稳定、mutation generation 不变、原生 After、context Dispose、物理 exit 全齐，且对应 Tween source CALL 已完成 After 后成立。它持有真实私有 source、membership、具体 invocation 与既有 EmptyListenerEffectsReceipt。初始资产为空、复制的诊断 bool、仅 source 返回或仅 Invoke 返回均不能发行该回执。运行时加 listener 后的非空调用保留 Pending。

## 已观察到的验证

原件在 `C:/Users/71957/riz-current-checks/`：

| 项目 | 实际结果 | 目录 |
|---|---|---|
| 冻结托管核心 | Audio/content 904/904、协调器 28/28、对象身份 10/10 | `tween-child-core-r1-20261028` |
| 同快照实际 CLR 采用 | 两模式、508 检查，exit 0 | `tween-child-adoption-r1-20261028` |
| 六组件 Release | 成功；一个既有 generated nullable 警告 | `tween-child-release-r1-20261028` |
| 打包和独立迁移核验 | 24 载荷／26 证据，exit 0 | `artifacts/foundation-tween-children-candidate-20261028` |

新增 10 条 focused cases 在 `NativeTweenChildEventTests.Cases`，已注册普通与 isolated 两表。它们使用受控根、图和 source journal，并经过实际 broker Invoke／mid、owner 和 effects 对象，覆盖首次物理计数、共享 membership、空调用全部边界、运行时 AddListener 后非空、context 多代、graph 失败保根、float 类型拒绝、active adoption 拒绝、私有 issuer 和 Prepare／mutation 复用。它们不证明真实 IL2CPP GC、游戏 child callback 或游戏清理完成。

原生 helper 直接复用上一 [source 批](../foundation-tween-sources-20261028/README.md)已验证的字节；本批没有重跑原生 37 映射和 source 行为夹具。19 项合成安装矩阵也属于上一 source 候选，不能写成本 child 候选再次运行了同一矩阵。

## 尚未完成

当前仅完整登记零参数 child。`onTween<float>`、非空 listener 的具体派生效果移交、事件字段运行时替换、正常容器扩容时同步保根、全部未来 writer、具体死亡区间及最终 Unregister／root-free 继续实现。空 child effects 结束也不单独放行完整 Tween owner 退休，`CanRetireWork=false`。

同步 borrowed-reference 原语是下一批未验证工作，不在本冻结候选中。性能优化后置；不维护旧 SDK／API／包兼容。真实游戏实测仍按用户安排稍后进行。

## 冻结摘要

- 候选 manifest：`eb32c9833cb0a69299ca972c0ccbfb89bdae5331d0b494e32153c4d41491662f`。
- core 快照：`541add981930b087444e6f7d9cd177c6266ff5fdc64ee97ba9b23d796b60fa73`。
- CLR 采用：`d7546a319115c7fa42d0ab25ed122211a25251cd007a4eaa835d80bf0e609596`。
- Release manifest：`86f597889674d994c3f9b71d90446facf8275642d01fb2960e41ebe12d01c600`。
