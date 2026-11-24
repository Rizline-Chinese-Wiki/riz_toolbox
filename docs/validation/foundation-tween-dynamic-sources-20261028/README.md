# Tween 动态对象来源接管

2026-10-28。本批补运行时新增 Tween 的正式来源提升。它只在一次实际 Play/Add/Execute 调用产生候选、并且同一对象随后出现在已保留 Manager 的稳定队列图中时才生效；单纯出现在共享 Manager 队列里的外来对象不会被升级。

## 所有权顺序

候选由当前真实 operation frame 记录。空闲 host tick 重新取得稳定 graph 后，`NativeLocalTweenWorkOwner` 要求后端返回它已经独立保留的 `RootHandle`，不从日志或裸地址补 Root。

同一个 Root 随后依次用于：

1. 已配置的 native Tween admission claim；
2. 当前对象的 live death baseline；
3. `onStart`/`onComplete` 的 runtime-idle child-event 追加；
4. 加入 owner 的 `_sources`，使后续 Play/Add/Execute 匹配。

只有完成上述身份核验后才更新 `_observeUpdates`。任何一步失败都让 owner 进入 sticky Unknown，已取得的 lease、death tracker、child slot 和 roots 保留，不回调释放或重试。admission seal 请求后不再提升新来源。

child-event 追加只允许在 activated listener transport 完全 idle 时执行；活动中的 Invoke、Prepare、mutation 或 owner 切换会拒绝并保留责任。`onTween<float>` 需要与 typed-event 通道合流，本批没有把它误记成零参数 child。

## 验证

为避免与并行 typed-event 草稿互相影响，本批代码叠加到上一份已通过的全量源码快照，在 `D:/riz-build-cache-next/dynamic-source-isolated-r1-20261028/source` 隔离构建。AudioContent Release 构建 0 warning / 0 error。

以下 9 个独立进程全部通过：

- 稳定 graph 只提升一次，并共享同一 Root；
- 后续 Play/Add/Execute 均进入 owned operation；
- 实际 Add 的队列 append 后才提升；
- admission 失败保留 Unknown 并停止后续 graph read；
- 无队列 occurrence 的对象不提升；
- 仅出现在共享 Manager 队列、没有实际调用的外来对象不提升；
- runtime idle child membership 成功追加；
- 活动 Invoke 期间拒绝 child 追加并保留 source 责任。

这些是 controlled roots/admission/death/listener fixtures，不是 IL2CPP GC、真实游戏或生产 helper 验收。候选包必须等本批与 typed-event、references、death 修复合并后重新快照、构建和打包。

## 尚未完成

- 运行中 `onTween<float>` membership 与 typed Invoke 绑定；
- 非空 listener 派生效果的完整归属和结束；
- 动态 source 的最终 Unregister/root-free；
- 真实游戏中的动态创建、场景切换和共享 Manager 矩阵。
