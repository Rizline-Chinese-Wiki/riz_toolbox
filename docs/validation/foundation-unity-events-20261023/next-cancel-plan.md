# 下一步：取得 Cutout 协程取消证据并封闭未来来源

2026-10-24。`flavor=null`、`gameRuntimeVerified=false`。承接[本批范围](scope.md)与[后续接线](next-integration.md)，仅复核已有静态证据和当前 tracker 接口；没有新逆向、构建、安装或游戏操作，也不改变 B01–B19 的验收状态。

**结论：先找到并验证 engine 对某一条实际协程终止调度责任的确认点，再接取消退休。** 当前接口只能退休配对观察到自然 `MoveNext=false` 的记录。GO dead 可以保留为对象生命周期事实，不能替代取消事实。尚未取得该确认点，不能添加一个由调用方填写的 `cancelled=true` 来放行。

## 已有证据与确定边界

以下证据均于 2026-10-24 从工作树读取，`source_type=file`、`linked_workitem=Cutout 取消与来源封口`、`supersedes=none`。源文件 hash 表示这次复核版本；后续修改需要重新核对。

| E-id | 来源与 SHA-256 | 可以证明的范围 |
|---|---|---|
| E-001 | [EventTrigger 有限静态证据](../foundation-cutout-async-20261023/event-trigger-research.md)，`b0fd5836013ae4504731b924c9cdab858924c4ec43fd54b5bec85ef04cb62d4f` | `StartEvents@38A460` 在实际 EventTrigger 上启动 reset Delay 和延时事件；Cutout 的 `Destroy@3C59E3` 请求销毁 controller GO。既有 5 函数/475 指令没有 engine 取消确认点。 |
| E-002 | [Cutout tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineTracker.cs)，`5b81a864c9146faf4b4fd8fef71e9526e6a3a4b5eb266dafc1513361fee20787` | 64 条同时记录，持实际 host/enum/Coroutine 责任，使用 Start/Move 配对及代次；`RecycleNaturallyEnded` 只接受自然 Terminal，没有取消终结接口。 |
| E-003 | [EventSource](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutEventSource.cs)，`374608270a93a37ccd92b828a9d59391d32c7f752d983e678734b91f67a9a9bd` | `Tick` 保持同一捕获对象图，可记录 `Alive=false`；`Read().Pending` 恒为 true。`TryConfirmNativeIdle` 只证明当前观察边界空闲。 |
| E-004 | [Active destruction](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalActiveDestructionTracker.cs)，`d0fd9396e4a71d9831248728b7d3c7bf373939097205a539493e156afb565c6c` | 自然 Cutout 后的更晚真实帧核验五对象 liveness，活动边界先后排空；`CloneDestroyed`/`FiveObjectsDestroyed` 没有签发协程取消或全部消费者结束证明。 |

复核命令：在仓库根目录对表中相对文件执行 `Get-FileHash -LiteralPath '<相对文件>' -Algorithm SHA256`，读取所链接方法；E-001 内含固定 PE 的有限复核脚本。本文没有新增原生执行证据。

F-001（`status=validated-source/static`、`confidence=high`，E-001–E-004）：controller 与 EventTrigger 是不同组件，实际 GO 归属仍以捕获实例为准。对 controller 调用 `StopAllCoroutines` 不能覆盖另一 host；自然 Cutout、Source dead 与协程自然 false 是三个独立事实。

F-002（`status=missing-evidence`，E-001–E-004）：缺少的唯一新增原生事实是：**engine 在什么可观测边界确认已撤销这条启动实例的调度责任，而且该边界能关联同一 host、enum、实际 Coroutine、启动代次，并覆盖其取消清理/在途退出。** 尚未证明此边界存在于当前观察 ABI 中；不预先命名回调、不指定未核实 RVA、不假定 Unity 会调用 `Dispose`。

## 下一最小代码单元

P-001（`path_type=callflow`，F-001/F-002）：先做有界的实际取消来源调查与观察适配，再将已证实回执接入现有记录；不扩展为全游戏调度器扫描。

1. 调查输入只取现有 exact host、enum、Coroutine 根及同一 run/slot/Start 代次。沿实际停止或 host 销毁进入的 engine 路径，找上面的确认点；必须核固定版本代码、真实入口 ABI、实际身份映射，以及取消清理是否还会调用用户代码。若该事实仍无法观察，记录保持 pending，这一步停止，不靠额外帧数推断。
2. 确认点具备真实生产证据后，增加一个专用取消观察 backend，并由它在受保护的实际调用窗口内产生不可由普通调用方构造的内部回执。记录至少关联 run/host/enum/Coroutine/slot-generation、物理取消窗口、退出结果和故障状态；同步首 Move、尚未返回 Start 输出、嵌套取消都要保留同一责任。不要让受控测试 authority 采用生产 backend。
3. tracker 增加独立取消终结原因，不递增 `NaturallyEnded`。只有回执对应实际记录、Start 已完整配对、取消清理与既有 native/source/listener 边界已退出，才可撤该记录纯身份、再次 drain、各根唯一一次释放并允许代次复用。沿用 unknown-free 不重试和独立根清理契约。回执不能自动清 `Source.Pending`。

在第 1 步取得事实之前，可实施的最小诊断改动仅是按实际 slot/generation 区分“等待自然结束”与“host 已失效但取消未证实”，并保留原根和原终结条件；它必须明确不改变 `TryConfirmObservedEnded`、退休或资源释放结果。无需为一个未找到的引擎通知先搭建成功实现。

## 取消之后还要封闭未来来源

取消只覆盖具体启动实例。`TryConfirmNativeIdle()==true` 表示此刻没有在途调用；另一个 future `StartEvents`、手工 `MoveNext` 或已经转交的 listener/provider 工作仍可能出现。封口必须由实际 owner 证明该 run 的生产入口已撤除或已受精确阻止，并在保护仍安装时观察撤除前后的物理 drain；只注销分类或 detach 自己的观察 hook 无法阻止原游戏工作。

实际 EventTrigger 若属于自有 clone 子树，可将同一对象的销毁事实与经核实的 engine 调度撤销契约组合使用；若是外置或持久 host，只能取消该 run 的确切操作，不能 `StopAll` 共享 host。两者都还要处理已捕获 UnityEvent、provider、manager/queue 的实际外置工作及未来入口。未知 Target/Method、opaque listener、内联执行尚未观察、动态转交或入口未封闭时，Source 与整局消费者继续 pending；不清空共享事件制造完成。

## 验证边界

| 验证层 | 必须覆盖 | 不能据此宣称 |
|---|---|---|
| 受控 tracker/backend | dead 无回执不退休；同 host 不同 enum/代次拒绝；Start 未返回/取消中再入/缺 after/worker/fault 保责任；自然与取消计数分离；撤身份后二次 drain；独立 free 失败不重试；slot 复用拒绝旧回执 | fake 回执证明了 engine 取消行为 |
| 实际 broker/native fixture | 确认点的已核 ABI、前后配对、普通观察被抑制时物理计数、异常或退出未知、跨线程与嵌套归属 | 它与真实 Unity 调度责任映射已经成立 |
| 后续游戏实测 | 固定版本 exact 记录在延时等待阶段被真实取消，原生确认点关联该实例；取消后旧实例不再进入；同步已分派的外置工作另有 owner；future 入口封闭与完整 drain | 有限时间内没再触发即可证明全局无未来调用 |

仅 GO/组件 dead、`Object.Destroy`/`StopCoroutine`/`StopAllCoroutines` 正常返回、`IEnumerator.Dispose` 返回、等待若干帧、当前在途为零、根数为零或 Cutout 自然 false，均不能单独替代上述取消与来源封口证据。当前优先补 F-002；代码和离线证据继续推进，游戏实测遵循用户“实测稍后”。
