# 范围与限界

本批继续完整B01–B19目标，新增实际EventTrigger的两条协程观察：reset SimpleDelay与每个Trigger的延时事件。它们都走已存在的typed public IEnumerator Start/Auto与Move transport；不把静态Trigger参数工厂误当作ref0实例factory，也未新造native ABI。

准入同时要求activation前捕获的same launch/source、实际EventTrigger host、物理StartEvents窗口代次、精确native caller、actual rooted enum及已捕获字段。Start original前发布enum，承接同步首次Move和零/负秒reset同步false。Delay支持provider state1逐帧更新和WaitForSeconds state2，事件只接受0→1→自然false/-1。每条记录的Action或Trigger/events/offset都保持自己身份；同source其他合法row不能替换它。

64同时记录、128独立enum/Coroutine根有界；仅自然完成且完整native/source退出后撤身份、redrain、独立free并确认代次复用。worker/reentry/缺after/字段漂移/unknown保责任，未知free不重试，安全独立根继续一次清理。生产配额仍32 owner，不以清静态owner或增配额让故障用例通过。

新回归属于受控算法/故障、source实际broker/probe范围；existing真实native fixture仍只证明其原transport契约。没有本批生产metadata/Action/actual enum游戏运行证据，也没有直接向新backend的真实gchandle free注入失败。live callback/throwing Reset字节核验只支持固定静态代码范围，不替代游戏验收。

观察到这些协程自然结束仍不证明UnityEvent persistent/runtime/executing listeners或provider回调转交的外置工作结束；future StartEvents/Unity事件仍存在。EventSource.Pending及UnsealedDispatchSource保留，未发whole-run token、Close registry或释放factory/bundle/bridge/handler/coordinator。对象销毁取消协程但未观测自然false的路径保持pending，尚缺确切取消/消费者结束证据。

RequestPlay关闭，仅B03整项/B18a子项勾选，目标active。用户“先继续代码，实测稍后”保持，无本批安装、游戏/UI输入、游戏进程终止、原游戏修改、commit/push。
