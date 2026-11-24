# 三处 invokable 调用的原生输送层

本批继续完整 [B01–B19](../../../.docs/foundation-expansion/SPEC.md) 目标，实现真实 pre-tail 与 post-continuation 观察，不使用托管包装器再次调用原函数。上一批 1515/1515 与另列 379 项回调联合已冻结；本批不是对上一批结果重命名。

新增独立 Listener 原生通道及 Bootstrap 父 TLS 接线，先在自有原生目标中验证真实硬件返回地址、机器状态、三处调用、完整 6/9/9 字节 continuation 和异常展开。Configure 只配置永久通道，不发布任何游戏指令；原有 FirstInstall V1 三写/144 字节 Ready 不授权本批六区。自有动态 stub 的注册、发布与测试也不能替代生产版本化 receipt、固定游戏映像验证或真实 IL2CPP/CLR/ledger 接入。

用户“先继续代码，实测稍后”继续有效。不安装、不启动或操作游戏/UI、不终止游戏进程，不修改原游戏文件，不 commit/push。root 为唯一 builder；三个子代理分别实现通道、自有 fixture 及只读 ABI 复核。源码冻结后顺序运行，构建过程中禁止编辑 build inputs。

本批调用点仅观察 invokable dispatch，不证明它内部每个 delegate leaf 都执行。连续 mutation、取消、未来来源封口、全部消费者退休及完整 Retry/Return/Loader 保持原验收要求；RequestPlay 仍关闭，不能以离线断言数扩大任务勾选。
