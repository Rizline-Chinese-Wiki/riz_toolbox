# 生产共用采用与分发逻辑的原生回调联合验证

本批继续完整的 [B01–B19](../../../.docs/foundation-expansion/SPEC.md) 目标。上一批完成了实际固定映像 producer 到 CLR 的空闲采用；本批补同一生产 broker 采用与分发核心的真实 native→CLR Enter/Mid/Exit，不能以此替代真实游戏 receipt 的回调、冷启动、IL2CPP metadata/GC、监听器和全部消费者退休。

用户“先继续代码，实测稍后”继续有效。本批不安装、不操作游戏/UI、不终止游戏进程。原生目标为自有 fixture；生产入口仍要求实际 FirstInstall.Ready、固定游戏 target 和 Gate 内复核。本批不允许伪造 Ready、反射构造 owner 或篡改生产已配置 original。

root 为唯一 builder。托管 core 与联合回归、fixture 的 pre-Mid 门、原生方案有限复核由三个子代理分别负责，源码冻结后才顺序运行定向和完整构建。新回归以独立 CLR 子进程运行，永久 native 通道不在已有 suite 内重用；计数另列，不能将更多离线断言称为整项验收或覆盖率。

三处 listener pre-tail/post stub 的实现与发布不属于本批已完成结论；相关精确返回地址、完整 continuation 和 unwind 门槛保留为下一阶段实际实施任务。RequestPlay 与未完成验收项继续保持原状态。
