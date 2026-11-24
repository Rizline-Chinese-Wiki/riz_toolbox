# 下一阶段：真实回调与全run闭合

前提：本批1515/1515、25套、482源/22包已冻结未安装。actualCLR已完成RequireReady/RuntimeAdopt与双idle sinks采用；零游戏和native callback的边界保持。用户“实测稍后”继续有效，本计划不能作为通过证据。

## 1. 将采用接到真实回调证据

复用真实Dobby/MASM/helper联合fixture，把生产adopter与broker实际native→CLR Enter/Mid/Exit、两个sink、逻辑解绑/异常和ledger同代identity串起来；metadata/图若仍受控须继续标明，不能替代真实IL2CPP与GC。核旧调用跨Attach不借新callback、原函数一次、Unknown保留责任、probe snapshot配对以及全部解绑不伪造physical drain。

## 2. 固定游戏冷启动合同

获准实测时才安装同一冻结包，记录Doorstop→真实producer三写Ready→original init一次/int结果或原异常→CoreCLR→RequireReady/Attach→首合法tick/UnityEvent的同会话顺序。当前受控ready的16进程C函数验证不证明这个实际顺序；DONT_RESOLVE映射也不是生产启动模式。init0、不支持映像、重入/Unknown与正常启动的透明行为须分别验收。

## 3. listener/future与全部消费者退休

继续实际executing-list的三条listener callsite，闭合动态leaf/provider、异步转交、连续mutation与取消。未证明的来源保持opaque/Pending；只在全部未来来源结束后组合audio/图roots/globals/menu/result/settings等逐owner证据，执行VerifyConsumersEnded→Close→bridge/bundle/reservation/handler/coordinator退休，再验证同revision的Retry/Return/Loader。

正常/失败/取消/provider撤回与多轮重开都需检查原调用一次、Unknown不释放、条件式基线恢复以及资源计数不单调增长。完整标准未变，RequestPlay仍关闭，仅B03/B18a可勾。热路径已确定不会每次重做SHA或遍历pdata；固定VirtualQuery/锁开销需独立测量，不能凭估计优化，也不能称隔离映像计时为游戏性能基线。
