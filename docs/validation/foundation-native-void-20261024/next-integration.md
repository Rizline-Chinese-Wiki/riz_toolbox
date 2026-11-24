# 后续执行集合与调用点

独立native void父入口已实现。下一步延续[1449的三步计划](../foundation-unity-events-20261023/next-inline-plan.md)：真实Invoke中段执行集合和三条实际invokable调用点仍缺，不能用父入口Pre/Post或独立Prepare补造。

先核固定中段`1564417`附近的可重放指令、原函数unwind与寄存器存活；建立独立native中段transport，实际采集RSI的event、RBP的executing和父物理token，包含空集合路径。中段不是函数入口，不直接套Dobby普通entry；回调返回恢复寄存器/标志/栈且原位路径只继续一次。安装未知不重试、patch与callbacks保process责任。

再对两个直接call与一个virtual call采用保真call-site relay，实际记录receiver/list/index/target/参数寄存器/MethodInfo，不统一cast共享RVA的不同ABI。六字节virtual call需要保留真实返回地址，不能E8+NOP后声称原caller。真实native fixture证明每原调用一次、SEH/C++展开与上下文drain，之后再接具体listener转交owner和mutation来源。

取消与future producer封口仍依[取消证据计划](../foundation-unity-events-20261023/next-cancel-plan.md)：没有exact host/enum/Coroutine/代次对应的engine调度终止确认点前，不能把GO dead、Stop返回、计时或state=-1改成Cancelled/Ended。完整资源/结果UI/globals/menu、消费者verifier、Close和bridge/bundle/handler/coordinator退休、Retry/Return/Loader仍继续。

父native窗口通过不等于inline覆盖或whole-run结束；本批保持Pending/InlineExecutionUnobserved=true/DispatchPathVerified=false，不新增B项勾选。游戏实测仍按用户“稍后”安排。
