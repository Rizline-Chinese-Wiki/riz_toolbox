# 下一步：给固定 mid CALL 增加可证明的发布窗口

结论：**当前安装时序不能证明首次 parent void hook 安装前 `UnityEvent.Invoke` 从未执行，也不能阻止它正在执行。生产 mid CALL 必须先取得真实 native 执行安全点，或取得另行证明的受控启动屏障。现有代码还没有这两种凭据，保持 unpublished。** 这不是五字节 opcode 或配置先后顺序能单独解决的问题。

## 本地证据为何不足以使用“首次安装”假设

| 当前路径 | 能证明什么 | 不能证明什么 |
| --- | --- | --- |
| [Bootstrap.Start](../../../src/RizToolbox.Loader/Bootstrap.cs)→[RuntimeBridge.Start/Install](../../../src/RizToolbox.Runtime/RuntimeBridge.cs) | Doorstop 托管入口创建后台安装线程，等待 GameAssembly 后安装 runtime-invoke hook | GameAssembly 映射后没有其他线程执行游戏，或 UnityEvent 尚未被调用 |
| `RuntimeBridge.OnInvoke` | 先调用游戏 original；首次匹配 Unity tick 后才初始化 ToolboxHost | host 创建前没有菜单／其他 Unity lifecycle 事件；“主线程初始化”也不是全局独占窗口 |
| [NativeLocalRunLaunchCoordinator.Begin](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)→[NativeLocalUnityEventBackend.Initialize](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.cs) | 这条待接通的 LOCAL 组合路径在自己的 inactive clone 激活前装 parent；已有 source/root 责任可保留 | 其他 UnityEvent 实例或 worker 不会调用同一全局 Invoke；当前构造路径也不是已完成的 Loader 游戏验收 |
| [DobbyDetourEngine.Prepare/Commit](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.cs) | pinned 版本准备 original、配置自有 transport，再激活 detour；失败地址保持进程责任且不重试 | 入口写入原子、其他线程已离开原函数、目标函数未来暂不执行，或可选择本例 CALL opcode |

[pinned Dobby 静态证据](../judge-context-publication-20261020/dobby-static.json)明确不作并发机器码发布承诺。首次 binding、`ReadActivity=0`、callback 未触发、inactive clone、主线程身份、延后一个 Tick 或 `GetModuleHandle` 成功，都不能代替执行屏障；在安装计数器之前已经进入原函数的调用也不会自动出现在该计数器中。

## 最小编码责任边界

只为固定的 `UnityEvent.Invoke@0x15642E0`／mid `0x156441B` 增加下面的内部能力，不暴露通用 plugin patch API，不把 `SuspendThread` 遍历当作已解决的安全点。

1. **首次安装凭据。** 先独立证明一个 native 启动／执行屏障：取得时没有线程在该函数内，期间不能有新调用进入，也涵盖线程创建；释放前有明确的指令发布协议。凭据必须绑定模块实例、固定范围、线程／epoch 和生命周期，不能由托管布尔值制造。当前 Doorstop→后台线程→首次 tick 路径不满足，不能通过简单前移一行 binding 改成满足。若选择改造受控启动入口，需要先用真实 native fixture 证明其阻挡范围，再接 Loader；没有凭据时应在任何目标写入前拒绝安装。
2. **固定 parent 的 native admission gate。** 在上述首次排他窗口内安全发布 parent void transport 后，给其 original 调用入口增加可关闭的 native gate，所有入口在进入 original 前登记并二次确认开放状态。关闭必须挡住后续调用，并等待此前已登记的原生窗口结束；未登记的旧调用由首次屏障排除。等待不调用 managed observer、不持 broker Gate、不分配内存或获取 loader 锁。若安装发起者自己在该 parent／observer／Dispose 中，直接拒绝关闭，避免等待自己或重入形成死锁。该 gate 解决后续 mid 安装的局部静止，不追溯修复首次 parent 安装证明。
3. **固定 mid installer。** 预先完成 hash／官方签名／原字节／near relay 距离／RX relay／唯一 ReturnPc 配置，再取得关闭 gate 后的真实静止凭据。在该凭据仍有效期间只写完整五字节 CALL，恢复保护、同步指令缓存、核对完整结果；禁止在临界区调用 C#、游戏 metadata 或可能重入 UnityEvent 的 API。重开 gate 前确认字节、relay、回调和保护状态均为已知一致。具体写入机制由这张执行凭据保障，不能凭对齐八字节数据写推导取指安全。
4. **进程责任与失败策略。** 保留 fixed target、original bytes、relay、callback、slot、parent gate 和安装 epoch。状态至少分为 Prepared／Quiescing／Published／Unknown。超时且可证未写时维持 unpublished；任何写入、保护或缓存结果不确定时保留 Unknown，不自动重试、解绑或释放 relay。若仍持屏障但读到残缺指令，不能悄悄恢复执行；生产安装前必须先定义并测试这一失败策略，不能把“finally 重开 gate”当通用清理。

门关闭与 drain 需要专门的 admission 计数，不能把“已经在 gate 前停车的线程”误算为必须先返回才能发布的 original，造成互相等待；同时也不能把尚在 native Exit／ASM恢复的线程判空。协议与统计字段应先在 native fixture 定义清楚，托管层只接收可审查的结果凭据。

## 必须先完成的验证

- **真实 parent→mid 联合链：** 在独立进程中把真实 Dobby parent void、broker physical top/token、五字节 CALL、near tail relay 和 mid bridge 连成同一条执行链。覆盖同 receiver 递归、foreign receiver／method、worker、4096 overflow mask、callback 中重入、逻辑 detach；确认没有向旧父层借 token。现有 standalone mid fixture 的 `nativeParentVerified:false` 必须保持，直到这个联合链真的运行通过。
- **关闭与发布竞态：** 一个线程在 original／mid／Exit 中停住，关闭者不能提前拿到凭据；关闭期间新 entrant 必须在 native gate 外停车；发布完成后各自继续一次。验证关闭发起者在自己 parent 中被拒绝、超时不写、worker 在 broker 安装 Gate 被持有时不死锁，并覆盖屏障期间新增线程。
- **精确字节与故障注入：** 错 hash、错模块实例、错原 NOP、relay 越界、重复 ReturnPc／地址、已安装第三方 patch 全部在写前拒绝。分别注入保护更改、部分写入、cache flush、保护恢复和读回失败；验证责任保留、不二次发布，以及屏障退出策略不会执行残缺代码。
- **完整物理窗口：** GPR/XMM/RFLAGS/MXCSR/x87 控制字和 unwind 验证继续保留；在 mid C++统计已经归零但 ASM尚未返回，以及 parent Exit 尚在途时，回收判断仍必须拒绝。mid stats 不能独立提供这一许可。

只有真实 native 屏障和失败策略、联合链、固定 installer 三者均验收后，才把生产 Backend 从“可接收实际 mid frame 但尚未发布”升级为“已安装且有凭据”。之后再单独验收游戏实际 executing 列表、三个 listener 调用点、动态变化、取消与退休。本计划仅写文档，没有操作游戏、线程或目标代码。
