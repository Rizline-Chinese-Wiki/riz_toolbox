# 主代理复核

2026-10-23。root完成实现、复核和构建；旧三名agent因额度错误未参与本批。以下不是独立审计，没有新第二runner。

1. 复核生产权限：Capture需actual setter已绑定、尚未activation；Start认领同时要求同controller、getter物理caller、实际native getter original窗口、rooted exact enum、state0和真实delegate。controlled不能通过production IsFor，也不能接生产backend。
2. 复核同步与状态：先root/publish enum再放行Start original。发现默认Delay有state2 WaitForSeconds路径，已与state1 provider循环一并支持；不再把所有Delay都限定0/1。只在已观察expected state路径正常false且state=-1时记terminal；Start返回与全部native drain仍必须确认。
3. 复核异常/线程：broker保original，tracker缺after时Dispose原子撤权，Start与Move异常均有受控用例。worker fault仅看托管controller/enum scalar；capture后重入、unknown/负在途、delegate/state变化不被计作正常完成。native实际original异常保持仍由既有真实Dobby transport fixture覆盖，未声称受控backend模拟了SEH。
4. 复核delegate：具名metadata字段约束与两条原生ctor写入一致，direct pointer/MethodInfo/target/method_code和非multicast都验证；共享Release代码用exact MethodInfo与NormalMode缓存closure区分。全部原生读取只发生在主线程且enum独立root已持有。实际游戏backend元数据/根/回调尚未运行。
5. 复核清理：四root固定槽，native尝试前process owner强持backend；失败保持unknown，未使用与自然结束各有独立清理路径；detach后再drain才free，独立root未知不重试。只释放观察根，不借此Close registry或卸掉全run保护。
6. 复核调用点：生产coordinator固定传自有tracker等待commit，核验后重读terminal/fault；菜单返回在取新normal信用之前等待相同tracker。组合3组证明不提前提交/取锁，原graph与menu回归继续通过。Loader完整根仍未构造、RequestPlay关闭。
7. 最终完整1343/25，Runtime138/Hooks126/Audio468，session72753 exit0；首次1342/session54321日志保留为过程。15条既有测试warning保留。真实Dobby初始化case增强了native original/Before/After/Start/退出窗口断言，新增Hook case还检查ordinary子getter、worker与managed transport不授权。
8. 405源与18暂存归档逐项核对，较1324增3/改9/删0；两源ZIP均核对，旧1021安装18项、旧326归档及三原游戏hash不变。静态13范围/1731指令、三输入hash和常量再读取磁盘核对。未游戏操作/安装/进程终止/commit/push，B03/B18a之外无勾选。

未将两条初始化自然结束解释成完整controller/音频/异步消费者退休，也没有把getter隐式信用改成菜单自有信用。完整目标继续，直接下一步追踪ControllerOver/Cutout/fade等实际来源及active失效/退出协议，再组合Return/Retry/Loader。
