# 下一步：接实际启动与broker采用

本批增加native observer快照与既有slot的托管采用，不把这份配置当作首次安装许可。生产mid未发布，RequestPlay保持关闭。

1. 按[已选路线](recommendation.md)实现固定Doorstop native pre-init入口，使用真实 `int(__cdecl*)(const char*)` original ABI，先核固定GameAssembly映像/函数/五字节NOP/unwind，再在受支持启动合同下发布native父与mid。原init一次执行，返回和原异常保持；写入前拒绝不影响原启动，写后未知必须停止受支持启动并保留责任。当前尚无实际生产producer。还必须同时处理[RuntimeBridge](../../../src/RizToolbox.Runtime/RuntimeBridge.cs)现有的post-init `DobbyHook(il2cpp_runtime_invoke)`：仅提前UnityEvent并未消除这条首次写入。生产路径应在pre-init配置并安装该精确四指针参数、指针返回的native直通桥，post-init RuntimeBridge改为采用已有原生观察入口，不重新挂Dobby；其原异常与返回也须真实native验证。不能把两条启动链混合后仍宣称首次安装已经完整。
2. 在已安装原生bootstrap责任上增加broker的受限采用路径，绑定同一target/original/slot/epoch。不能让现有普通Bind再执行Dobby Prepare/Configure；必须统一bootstrap与旧bridge共用的native槽分配，避免仅靠碰撞拒绝提供生产可用性。此后所有插件仍分享一个物理入口，逻辑停用不移除native现场。
3. 将既有mid实际frame传递到broker当前物理凭据与Adapter ledger，保持同代/token/self/MethodInfo/实际caller/return PC，完成新的启动链→native→CLR→broker→ledger联合回归。旧调用snapshot为空时不能在mid/exit继承后到observer；CLR失效不丢原native调用或清理。
4. 固定版本实际冷启动验证pre-init安装完成→原init→post-init Attach→首个合法UnityEvent；若发现合法pre-init游戏调用，则这条profile前提失败，必须改用明确可证明的native握手或[OS调试事件方案](options.md)。本批fixture不替代游戏合同。
5. 继续三条实际listener执行、连续mutation、provider转交与取消、future来源及全部资源退休。最后接完整Retry/Return/Loader与B01–B19验收，而不是拿bootstrap原调用结束当作全run结束。

用户要求“实测稍后”仍有效；本批没有安装、游戏前台操作或游戏进程终止。
