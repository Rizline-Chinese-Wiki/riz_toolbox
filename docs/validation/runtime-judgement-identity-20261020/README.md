# 原生判定上下文与逐音符服务集成

本包已正式安装并开始实机验证。新增 `IGameplayJudgementService`，连接原生调用者捕获、真实音符/谱面对象图、局内身份注册表及插件作用域订阅。完整 B07/B11 仍未验收。

## 构建与安装

- [构建摘要](build-summary.json)：25 个检查项目，**665/665**；Hooks38、Judgements27。
- 原生 fixture 为 31,261 项；真实固定 Dobby 的 Prepare/Configure/Commit fixture 为 61,358 项。二者均无失败、无在途遗留；结果保存在 `native-build/`。
- [安装核对](installation-check.json)：正式安装的 **18 项**全部匹配包清单 SHA。此次新增 `RizToolbox.JudgeContext.dll`。
- [源码清单](source-manifest.json)与[安装时核对](source-integrity-check.json)：242 个输入文件，改变 0 项。
- `build-before-counter-guard.log` 是先前 664 检查构建，当前包依据 `build.log`。`predecessor-session/` 是旧 644 包会话，不作为新包证据。
- 游戏进程 55876；诊断会话 `20261020T142651470Z-8206acb77fef40969c71904d8977fa3c`。旧进程已正常退出，新包由现有本地 GameLauncher 启动。

可重跑的命令：

```powershell
./scripts/Build.ps1 -NativeOutputDirectory 'D:\RizToolbox-build-cache\judge-context-integrated-20261020'
python docs/validation/runtime-judgement-identity-20261020/summarize-build.py
./scripts/Install.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline'
python docs/validation/runtime-judgement-identity-20261020/summarize-build.py --installed
python docs/validation/runtime-judgement-identity-20261020/inspect-runtime.py
```

`summarize-build.py` 读取同目录保存的完整 `build.log`；复现实验应另存新目录，保留本目录已有日志和导出。

## 第一局：667 条冻结事件

[机器核对](first-result-proof.json)对应 `session-first-result/`，未覆盖后续导出。游玩 `Greatness Earth` EASY4；界面原有 FREE 图标保持，使用正常鼠标输入；未用程序伪造判定或修改原函数参数。

- 会话中的模块成功安装、插件订阅成功。
- 观测局 `b8cb27f562b7434384840e9bfdadd28b`：SDK 完成观察 **479**，未关联 **0**；共享内层 hook 原调用/前后观察都是 **479**。
- 十二种固定调用点实际覆盖六种。下表是完整局累计，SDK 保存的详细 DTO 是各源前两个以内，共 9 条，不冒充完整逐条日志。
- DTO 含唯一谱面引用位置、稳定 RuntimeNoteId、前后计数。普通超时与挑战段超时样本共享同一个真实音符 ID；不同对象保留不同序号。
- 实际结果为 **476 Miss、2 Perfect、1 MaxPerfect、0 Bad**，合计479；`Crashed=true`、`EndReason=Failed`。这证明实际失败结算关联，不能称为正常完成。
- 结算后身份注册表不再可用，服务仍 Installed；结算页停留没有重复 RunEnded。未观察到身份撤回或 Error 级日志。

| 调用点标识 | 第一局捕获数 |
|---|---:|
| auto.normal | 0 |
| auto.challenge-max | 0 |
| tap-hold.timeout-normal | 109 |
| tap-hold.timeout-challenge | 303 |
| drag.timeout | 64 |
| tap-hold.head-max | 1 |
| tap-hold.bad | 0 |
| tap-hold.head | 1 |
| drag.success-challenge | 0 |
| drag.success-normal | 1 |
| hold.continuous-miss | 0 |
| hold.end | 0 |

调用点名称标识固定代码路径；其中的 normal/challenge 字样不应作为独立游戏模式或实际触发原因的证明。

## 后续：1,985 条冻结事件

[后续机器核对](auto-retry-skip-proof.json)对应 `session-after-auto-retry-skip/`；前667条与第一份导出完全相同。主代理通过游戏第二页 Mods 的“观赏模式”按钮开启自动演奏并开始第二局。随后实际发生 Retry 和 Skip；这两步不在主代理的电脑操作记录中，按游戏日志事实保留，不宣称由代理发起。

- 累计判定 **675**，原调用也是675，未关联0；实际来源 **8/12**。
- 新增 `auto.normal` 102次、`hold.end` 41次；全部来源计数见机器核对。
- 第二局两个样本的自动头判定与长按尾判定分别共享同一个 ID，序号1、2。原生调用序号480/481对应482/483。
- 三个开局 ID 不同，终止分别为实际结果 Failed、原 Retry 边沿 Aborted、原 Skip 边沿 Skipped。后两局没有被伪造成正常完成结果。
- 未覆盖的四源为 `auto.challenge-max`、`tap-hold.bad`、`drag.success-challenge`、`hold.continuous-miss`。
- 为查看游戏设置，主代理正式停用再启用 `example.panel`；重启订阅成功，但停用时产生 **1 条真实清理错误**：`VisualResourceService.ScopeGate.Dispose` 使用已失效的插件 dispatcher。第一份导出没有此错误，后续导出原样保留。修复与重新打包的证据另存 [清理修复目录](../runtime-visual-scope-cleanup-20261020/)，不能用修复后的源码改写本665包的运行结论。

## 接入保证及边界

固定本体 SHA/Unity 版本、元数据方法签名、字段类型/偏移和十二个 CALL 目标均通过才安装。外层实际 ABI 是 int32/single，内层是 enum32/single。broker 先 Prepare 获取原函数跳板，再配置并保留原生 helper/托管回调，最后 Commit；存在不确定安装结果时保持进程归属并禁止二次补丁。

身份基于保留的 NoteInformation，不按时间、note._index 或地址裸值猜测。registry 保留初始 chart noteData 引用图，明确区分唯一、歧义与图外对象。原方法返回后的计数读取失败会撤回当前局覆盖。最后一次调用保留进入时的局信息，待外层退出才允许释放在途根。重入、停用、异常清理有离线检查；原生 fixture 的异常展开检查独立于这些托管测试。

仍缺十二源全部实机覆盖、手动 Hold 完整矩阵、实机异常/撤回/长期 GC、真实时间单位标定及最终释放 receipt。测得的差值保留 `RawGameUnits`；哨兵值 -2/-1/1 单独标识。请求参数、原函数正常返回、计数改变和最终评分是不同事实。正常完成、同谱迟到结果以及 Speed/Seek 不由上述证据验收。

模块用于固定受支持版本。回退时退出游戏，使用已保存旧包清单和正式安装流程；不要在游戏运行时替换 DLL 或卸载原生回调。
