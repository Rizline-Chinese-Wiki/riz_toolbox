# 本批授权与范围

继续用户要求的兼容性底层与完整B01–B19任务，三代理并行实现native bootstrap传输、Runtime adopt-existing桥及真实native fixture，root负责接线、实际CLR联合用例和唯一构建。用户“先继续代码，实测稍后”继续有效；不安装、不操作游戏、不终止游戏进程，不提交、推送或清理共享工作树。

本批代码落地为“native先配置永久父与mid通道，后一次接入immutable观察者”的传输组件。每个调用在自己的Enter边界捕获回调快照；旧调用不继承晚到回调，native原函数仍执行一次并保留原生异常语义。托管桥采用已经配置的真实slot/Target/Original/Entry/epoch，不重挂Dobby、不重新配置原通道。

这不是完整Doorstop fork或首次安全安装器。测试只在自有fixture首次调用之前发布父与mid；未向游戏发布指令，没有验证固定游戏的pre-init启动前提，也未进行真实IL2CPP GC/listener和全run退休验收。生产尚无创建该bootstrap配置的入口，RequestPlay保持关闭。设计文档给出当前决策和后续依赖，不把设计、fixture或测试数当作完整目标完成证据。
