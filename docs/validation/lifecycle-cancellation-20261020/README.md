# Retry 原生取消与单局边界

源码已接入独立模块 `rizline.retry-cancellation`，能力为 `riz.lifecycle.retry-cancellation.observe`。当前只有静态/离线证据，新候选尚未安装，不宣称实机取消已通过。

精确入口是 NormalModeInLevelController.Retry、RVA `0x3C1310`、InstanceVoid。before根住receiver/info/pause/info GameObject，检查真实paused且未over、匹配当前已观察run且activeSelf=true；原调用成功返回后同一GameObject变为false才接受。错误线程/重入直通，不捕获上下文；原异常不产生成功after；所有context逐项finally清理。详见 `cancellation-static-proof.json` 和 `inspect-cancellation.py`。

接受边沿只排普通token，在下一宿主Update、调度Pump和采样之前发布Aborted。这样GameplayService能先将Retry记入原局，随后单局scope的待办先被取消。队列最多8项、同token去重；旧修订/局/场景或模块释放后不重放。Run scope还核对生命周期身份，匹配RunEnded可在普通监听前清理；旧结果与同局重复legacy开局不误杀新scope。

实际离线日志：

| 文件 | 结果 |
|---|---|
| `retained-hook-tests-fixed.log` | Hooks24/24，原异常、停用和失败清理 |
| `run-scope-tests.log` | Scope30/30，身份与边界矩阵 |
| `game-token-tests-final.log` | Game54/54，stale token/队列/原状态回归 |
| `audit-order-tests.log` | 审计16/16，成功Retry先关联原局再关闭 |

全量582项及候选状态见 [主报告](../../2026-10-20_实现-Rizline原生交互与单局边界-report.md)。首次retained测试失败为夹具第二次调用解绑后的原函数时仍要求活跃context，已改为验证解绑后context保持释放；旧失败日志保留。

Back不是直接Retry同义词：它先设置退出请求，再由Update推动动画和LevelOver；是否出现结果页取决于后续导航。当前不把Back、采样`_terminated`或控制器消失当成取消。该路径和同谱迟到结果origin仍需继续实现/验证。
