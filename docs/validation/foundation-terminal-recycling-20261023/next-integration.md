# 下一步：确切活动对象失效与完整退休

本批已完成独立终止协程观察根的回收和记录复用；保留生产 probes，未来新 factory 仍重新进入等待。不能把零 live record 或曾经 ObservedEnded 当成永久来源结束。

## 先捕获真实对象并跨帧确认失效

在实际 ControllerOver 前，由同 launch/registry/session 的生产 owner 预留有界 active 退休责任，独立持有原 clone controller/GO/transform、初始化图和实际音频图对象。对象身份来自既有 factory、参数绑定、activation 和 NativeLocalRunTerminationBoundaryState 的实际发布记录，不接受调用方地址或 bool 冒充。

记录 actual required Cutout 沿 state1 到 false 的完整 native 退出帧。更晚的真实 Unity frame 才核同一 controller/GO/transform 的 Unity dead；需要逐个核音频/组件的实际归属和失效，不能只凭 Destroy 请求、场景名、计时或 clone GO dead 推断所有借用对象已死。staging parent 若仍活，仅对本 owner 持有的对象请求一次销毁再等待后帧。禁止复用 inactive-only 销毁流程或删掉 active 检查。

## 再封 future producers

保留五门、Progress/Timeline/初始化/Judge 与 terminal probes。先停止同 session 的结果按钮/命令接收，完成六 globals 的条件恢复并确认不再引用旧 controller；逐个处理 UnityEvent、Cutout EventTrigger.StartEvents 和外置 delay/队列持有的旧闭包。动态目标未知时继续保责任，不伪造全结束证据。

terminal 新 SealAfterDestroyed 必须接受真实生产 active owner，核同局实际 dead、initial 完成/closed、所有已观察 terminal 结束和完整 native drain；再撤身份/redrain、detach/redrain，最后释放剩余观察责任。菜单使用真实 Return owner、等待旧初始化/终止回调后才取得新 normal 信用，条件恢复自己的对象/信用，保留后继。

结果 presentation callbacks 完成、Judge outer/inner/Hold、Progress/Timeline/Play、globals/menu 和所有外置消费者都取得确切结束证据后，host 帧泵才通过 actual issuance barrier 调用 VerifyConsumersEnded→Close→bridge.TryRetire。逐个独立根只 free 一次，失败继续可安全的其他清理；最后 bundle、reservation、handler/bridge/coordinator 槽退休。

## 完整命令与交付仍须完成

Retry 创建新 revision lease、bundle、Chart/JudgeData 和 run 代次，不能复用旧 native wrapper。Return 使用实际 completed scene owner；Loader 组合完整 host、bridge、handler、sink、coordinator 后才按故障边界开放 RequestPlay。还须完成 SPEC 的其余 B01–B19 能力，不能将目标缩为 LOCAL。

验证覆盖多帧销毁、返回/重试发生在 Cutout 中、后继 globals/menu、UnityEvent/外置异步、worker/reentry、未知 Destroy/free 和长循环。用户“先继续代码，实测稍后”继续有效，离线证据和游戏实测分别记账。
