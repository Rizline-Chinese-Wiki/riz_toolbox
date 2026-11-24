# Tween 同步引用保护与销毁确认

2026-10-28。本批补运行时新增对象的即时保根、容器扩容跟踪和实际销毁区间。托管回归与六组件构建已经通过；原生回调重入的最后边界仍在验证，候选尚未打包。没有安装、启动或操作真实游戏。

## 已实现的行为

实际 source 在读取新引用后，同步发布带原生 TLS、线程、source epoch、物理 source／relay 和事务身份的通知。CLR 先为本次通知的所有新对象预留责任、取得并记录独立强根，然后才读 Target、官方对象类型和实际发布身份。相同地址仅在已有独立强根仍被持有时共享；失败的 root 请求不通过稍后的裸地址重试。

原生 source 在锁外发送通知，原游戏调用与原异常保持自己的执行边界。观察失败后仍发送实际后续 After／Abnormal，并保留未完成责任。新增数组用实际不可读页检验：引用通知取得根并恢复可读之后，source 才能继续读取其内容。该夹具使用自有对象和受控元数据，不代表实际 IL2CPP GC 验收。

同 helper／module／epoch 只建立一个进程级引用观察器。后续 reader 加入该观察器，不重新 Attach。hub 固定首次实际 managed／OS 线程，失败的 Attach 不被替换。每个 reader 只以激活前已保留的 Manager／Tween 作为归属锚；动态保活的外部 Tween 不自动获得独立 Play 的归属。

新根进入 reader 时保留当前通知的原始 RootedReference、私有 receipt、实际 strong handle 和当前日志位置。历史 ScopeReferences 只能帮助识别原 owner，不能被重放成当前 After 的新捕获。日志序号必须晚于实际取得该引用时的位置，之后的根不能补认已经发布的旧行。

销毁观察从激活前开始，记录同一个独立 graph root 的实际存活基线。随后只有同 coordinator 的已分类 Return、实际 Sealed admission lease、相同 slot／epoch，以及两个不同后续 frame 的稳定零 cached pointer 与官方 alive=false，才发行私有 DeathReceipt。最初就为零、没有 Return、同帧重复、帧回退、复活、class／field／root／epoch 改变均不能取得确认。

销毁确认只证明该 Unity 原生对象的死亡区间。队列别名、listener 派生效果、wrapper GC 和强根释放仍由各自 owner 完成；本批没有调用 Destroy、Unregister 或最终 root-free，`CanRetireWork` 仍为 false。

## 已观察到的验证

本批新原件位于 `D:/riz-build-cache-next/`，失败和中间批次保留原件。

| 项目 | 实际结果 | 目录 |
|---|---|---|
| 最新托管核心 | Audio/content 916/916、协调器 28/28、对象身份 10/10 | `tween-reference-death-core-r2-20261028` |
| 同快照专项 | Runtime 272/272、Module 26/26、Hooks 199/199 | `tween-reference-death-managed-r1-20261028` |
| 六组件 Release | 成功；一个既有 generated nullable 警告 | `tween-reference-death-release-r1-20261028` |
| 独立引用原语 | 八进程、11311 次检查，exit 0 | `tween-reference-owned-r1-20261028` |
| 中间 source 联合批 | 十二进程均通过；另发现 AfterCommitted 后状态复核缺口，尚非最终候选 | `tween-reference-source-owned-r2-20261028` |

Runtime 新增 10 个引用桥场景、8 个 reader 联合场景和 5 个死亡读取场景；Adapter 新增 12 个死亡区间场景并登记普通与 isolated 两表。专项使用受控官方元数据、受控根和实际 owner／lease／receipt 代码，不是游戏或生产准入证明。完整 24 套托管矩阵本批没有重跑。

第一次 source runner 实际通过旧八个模式与新增 grow 子进程，但因新增 joint JSON 与旧 ABI JSON 不同而拒绝结果；已按两种实际范围分别核对。不能将这次脚本拒绝写成扩容场景执行失败，也不能将未继续运行的模式计为通过。

## 接下来

先完成最后原生边界、同 helper 的映像／CLR 采用、打包和合成安装矩阵。之后实现 `onTween<float>` 的真实 typed Invoke 通道、非空 listener 的具体派生效果移交、未来 writer 封口与全部消费者退出，再接最终退休。性能优化后置，不维护未发布框架自身的旧 SDK／API／包兼容；游戏实测继续按用户安排稍后进行。

## 已冻结的托管摘要

- core 快照：`8881eeda2a65010846e4cd98361147eab43f36ae8a074a891087b23aa9572115`。
- 三套专项：`4315aa77e3cd169a2405925c2452a5c63dc224d0895bc1b2f323548f3bdf1d9f`。
- Release manifest：`726ae3958bb6c0854d579b5c9a5d61746536236fe5e13730339ca46edde88c0f`。
