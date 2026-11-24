# Gamepad 数字输入接入

2026-10-21。子代理完成源码与专项用例后额度中断；主代理接续运行，`regression.log` 记录 UI/input **70/70** 通过。包含原59项和新增11项。没有真实手柄或新包游戏运行证据。

`metadata-excerpts.txt` 来自用户的 IL2CPP dump，列出 Gamepad.current、16个按钮、DpadControl方向、ButtonControl.wasPressedThisFrame 和 InputDevice added/enabled。构造 backend 时还会核对运行时精确签名，符号不符只撤回该设备映射。

生产 after-input 链已经接入有根的 current 设备快照：一次采集检查 added/enabled，读取16个数字按钮，最后重新检查设备状态；中途失败丢弃整批手柄边沿。缺失/禁用/故障撤销该设备动作的队列和已排空 epoch，保留键鼠。下一次采集重新探测，成功后才返回 RuntimeAvailable。采集和派发继续使用已有焦点/文本/模态/上下文以及每回调绑定代次规则。

新增用例覆盖映射拒绝、可用性、设备持有期、缺失/禁用/恢复、获取/读状态/读控件/清理失败、读取中移除、独立失效、重绑冲突、焦点、回调重绑和重连。设备 seam 是生产采集逻辑加托管假设备；不是原生硬件驱动测试。

只支持当前 Gamepad 的数字按钮，不支持模拟轴、震动、多玩家设备选择或游戏输入消费。Trigger 表示 Input System 的按压阈值边沿。仍需真实手柄采集→队列→SDK回调、热拔/恢复和双插件焦点矩阵。完整构建与安装以 `../foundation-batch-gamepad-20261021/` 为准。
