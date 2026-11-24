# 范围与授权

本批继续用户授权的 PC MOD 兼容底层，遵守“先继续代码，实测稍后”。范围是既有 pending launch 的精确初始化钩子、共享 Timeline authority、实际输入/返回图核验、真实在途与共享准备状态，以及离线回归、独立原生 ABI fixture 和文档。

原生画像固定为 GameAssembly SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`、Unity 2022.3.62f2、Windows x64。入口为 LevelControl.Start、NormalMode.GetInsideLevelParams、Progress.Play、SteamTimeline.Tooltip；GameMode 复用已有 Progress binding 的同一 entry 和 trampoline，不另装第二个地址所有者。其它 Progress/Timeline 能力按各自精确上下文保留。

不在本批验收范围内：实际激活入口、生产 RequestPlay admission、完整结果 sink/Retry/Back、激活后 native 消费者结束证明、游戏内 LOCAL 初始化/正常完成，以及 Save/Steam 间接和异步全路径。普通原调用 native SEH/C++ 异常跨 managed replacement 的完整输送也未证明。新的离线 ABI fixture 不代表游戏 prologue、IL2CPP 对象图或游戏原调用异常已验收。

源码搜索确认，`NativeLocalRunTerminationBoundaryState.Create`、实际 termination handler 构造与 `NativeLocalRunLaunchReservation.Reserve` 目前没有 Loader/模块生产调用点；它们是可调用的生产 Runtime/Adapter 实现，由离线测试接线驱动。实现存在不等于用户的游戏初始化路径已启用。需要真实 launch/activation/commands/result view/active退休协调器后，才能另行评审开放入口。

本批完整构建1210/1210、25套通过，声明源码输入与暂存发布文件分别冻结并逐项核对；另有引用同包Runtime/SDK的独立native ABI runner复核。通过范围仅为离线实现与输送，不表示安装或游戏成功。当前实际安装仍按 [1021批](../foundation-local-protection-host-20261022/README.md) 理解；本轮没有安装、游戏输入或进程终止操作。完整 B01–B19 目标不缩小，不新增整项或子项勾选。

设计依据是独立冻结的 [初始化静态证据](../local-initialization-order-20261023/README.md) 及 [I01–I08计划](../local-initialization-order-20261023/next-initialization-isolation.md)。该证据仅支持明确直接调用锚点和有限控制流，不能扩成整个 Unity 调度顺序或 Steam 零调用证明。
