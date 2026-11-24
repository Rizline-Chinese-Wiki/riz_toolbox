# 原生void父分派

2026-10-24。1475/1475离线检查通过，431项源码和18项发布文件已冻结，新包未安装。完整底层仍未完成，详细证据见[本批索引](validation/foundation-native-void-20261024/README.md)。

UnityEvent.Invoke已从旧managed-original void观察切换到独立NativeVoid0通道：原函数在native层一次执行，实际caller/MethodInfo进入同代次并核exit，全线程probe保留原snapshot配对。原生32永久槽、broker4096同时记录有界；worker不持安装Gate，unknown保责任，逻辑解绑不伪造native drain。Invoke/独立Prepare及每次图读前后均检查共享native Known。

4组真实Dobby/MASM/native helper测试确认精确caller、原函数一次、SEH/C++保真、worker/detach与nativecallback21组合；受控17组Hook与事件/metadata检查另列，不能替代游戏实测。首轮测试适配器漏接口编译失败已修，失败日志保留；最终完整25套通过。旧1021安装、旧326归档及原游戏三hash匹配，无本批安装/UI/游戏进程终止或commit/push。

内联执行集合/三条实际listener调用点、连续mutation、listener/provider转交、销毁取消和future来源封口仍缺；Pending保留、InlineExecutionUnobserved=true、DispatchPathVerified=false。全部消费者和资源退休、完整Retry/Return/Loader继续缺，RequestPlay关闭。 下一步按[执行集合与调用点计划](validation/foundation-native-void-20261024/next-integration.md)继续，完整B01–B19 active，仅B03和B18a勾选，用户“实测稍后”保持。
