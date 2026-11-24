# LOCAL 结束入口与首次结果页归属

2026-10-22，固定游戏SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。仅复用既有Python/pefile/capstone做有限静态提取；没有运行目标函数或操作游戏。本目录由first_result_receiver_route子代理完成提取和关键发现，最终说明由root收尾；`runtimeAcceptance=false`。

**结论：当前四门不能自动识别首次结果Start。可编码的另一条路线是在已绑定exact LOCAL controller的LevelOver之前接管终止，并显示宿主自有结果UI；同时隔离Progress.Update尾部的Steam Timeline调用。没有实现这些新增门之前继续禁止LOCAL激活。**

## Evidence → Finding → Path

| Evidence | Finding | 实现路径 |
|---|---|---|
| [LevelOver/Skip/PauseUI指令](disassembly.txt)、[符号/范围/完整字节](boundaries.json) | NormalMode.LevelOver@3C0C00是终止汇合点；Skip与PauseUI经virtual slot调用该入口 | 对已发布exact controller做original前接管；普通实例仍原样执行 |
| 同文件Progress.Update@400FE0，4014B4之后 | 自动结束/失败经GameManager.inLevelControl.LevelOver；返回后4014DB/40157A/4015AA继续调用Steam Timeline | 仅skip LevelOver仍不足，需保留Progress.Update普通职责并在exact LOCAL终止上下文抑制相应尾调 |
| 同文件GetJudge@3C0310，3C087B/3C088F；LevelOverCoroutine.MoveNext@3C5C80，3C5F02 | 失败先写LevelInformation状态，LevelOver启动的协程最终转场 | 接管整个LevelOver可阻止该stock结果转场；不能通过Start.After补救 |
| 同文件Progress.Play(Boolean)@400700，400795/4007B5 | 暂停/继续也会设置Steam Timeline模式 | Play(false)不是纯音频停止；若宿主调用它，需精确操作作用域的Timeline保护 |
| [元数据](metadata-excerpts.txt)、[提取来源](extractor-provenance.json) | Timeline三入口的static ABI含enum、引用、Single、UInt32、UInt64结构返回/参数 | 需要专属verified ABI，不可套现有InstanceVoid或任意地址skip |

## 可直接编码的链

1. 启动前从exact controller.LevelInformation取得progressControl，并核progress.levelInformation反向相等；独立root并按session/run generation发布纯身份映射。不能靠“有LOCAL局”识别任意Progress实例。
2. 为精确Progress.Update@400FE0增加宿主专用around上下文：original执行一次、finally退出。上下文必须覆盖终止尾部并保留嵌套/线程归属；普通before/after observer捕获失败会转发，不能承担LOCAL隔离权限。
3. 给NormalMode.LevelOver@3C0C00增加原调用前的sealed policy。已绑定LOCAL直接接管，先标记唯一终止请求，在匹配的Update上下文标记terminal-claimed，然后排队给宿主pump。handler错误/撤回/重入均不得fallback。普通controller仍原样一次。
4. 在离开最外native调用后执行一次真实ControllerOver及结果复制，使用已实现的[NativeLocalResultCapture](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalResultCapture.cs)，再显示自有UI。不调用stock结果Start，也不为了找receiver依赖尚为空的levelOverInfo。
5. 对SetTimelineGameMode@48A950、AddInstantaneousTimelineEvent@48A3E0、EndRangeTimelineEvent@48A730实现sealed、精确static ABI保护；只在exact LOCAL终止上下文跳过，其他调用原样转发。结构返回需按已验证ABI返回正确的空handle，不伪造SDK成功事件。
6. 不能全skip Progress.Update。它还处理音乐timeReady、时间同步、resume、等待judges清空、fade和终止标志；统一禁用会破坏游戏流程。Progress终止尾部还会清ChallengeTimeControl.RiztimeEvent，须入局前验证/接管该全局责任，保留其他所有者。
7. 终止后的音频/输入停用、Retry新图、返回快照、globals恢复、hook/协程排空与消费者结束仍是实际事务的一部分。这条路线缩小首次结果receiver问题，不豁免整条链验收。

## 当前实现与缺口

1021四门为ResultStart/ControllerRetry/ResultRetry/ResultBack，未包括LevelOver、Progress.Update around或Timeline static gates。新增一次结果capture和pending参数setter是本轮独立步骤，不等于这条终止链已接好。仍未开放RequestPlay、未激活LOCAL、未证明LOCAL保存/Steam零调用。

本目录不选择“仅靠SceneLoaded通知猜结果页归属”的方案：SceneLoaded的发生顺序与可枚举到的exact本次receiver仍需直接证明。当前已有controller身份的LevelOver路径更具体，但上方新增前提必须先实现并实测。

## 复核

```powershell
python docs/validation/local-result-ownership-20261022/inspect-result-ownership.py
python docs/validation/local-result-ownership-20261022/verify-evidence.py
```

第一条只读固定磁盘输入并重提取声明范围；第二条校验固定范围字节和关键call锚点。辅助脚本不能证明所有动态路径可达、正确static ABI、原生patch或完整无保存行为。
