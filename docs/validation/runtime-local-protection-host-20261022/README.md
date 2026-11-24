# 四个原生前置门：实机尚未收尾

2026-10-22。用户要求“先继续代码，实测稍后”，已停止前台游戏操作。现场保留暂停状态；后续必须重新读取当前窗口，不复用旧坐标。没有安装下一批代码、退出或终止当前游戏。

真实会话 `20261022T141913071Z-42d75d5467ea4cf6a55f99761b6e12a8` 使用已安装的 [1021 包](../foundation-local-protection-host-20261022/README.md)。[当前观察记录](current-session-observation.json)及 `live-snapshots/20261022T143057369Z/` 保存 14:30:57 UTC 的前 1280 条连续事件，0 Error。**这是仍在运行的会话前缀，不是完整停止会话。**

## 已观察与未观察

| Evidence | Finding | 下一步 |
|---|---|---|
| seq84 的 LocalRunProtection 状态 | AllGatesInstalled=true、RetainedBindings=4；实际 metadata/RVA 解析与四门绑定成功 | 后续完整会话继续核对计数 |
| seq1083，普通暂停菜单 Retry | ControllerRetry NativeCalls=OriginalCalls=Classified=Continued=1，Claimed/Skipped/Faults=0 | 普通结果页 Start、Retry、Back 各需真实调用 |
| 所有已记录宿主状态 | AcceptingNewRuns=false、UnretiredSessions=0；没有 LOCAL 事务 handler 或会话 | 首次结果 receiver 归属及实际 LOCAL 激活另行实现/验收 |

本次通过切换现有 Steam 主窗口触发游戏自动暂停，再返回游戏点击普通重试。没有按 Escape，也没有操作认证界面。另一任务的窗口反复抢焦点会使游戏暂停，结果页测试因而延后。

## 收尾条件

恢复实测后，先刷新现场，再完成普通 ResultStart、ResultRetry、ResultBack。正常退出后独立记录进程不存在与是否实际观察到正常退出，再运行 `freeze-proof.py`。该脚本严格要求从 sequence1 开始的完整连续会话、四门 ordinary 增量全正、原调用计数一致、零 LOCAL claim/skip/fault，以及实际 18 个安装文件匹配。当前快照缺三类正向调用，不能作为脚本成功输入。

本目录没有最终 runtime-proof.json。四门的普通转发不证明首次 LOCAL 结果归属、保存隔离、ControllerOver 一次执行、完整游玩或消费者结束；不增加任务勾选。没有创建 opt-in markers，见 [进入前记录](markers-before.json)。
