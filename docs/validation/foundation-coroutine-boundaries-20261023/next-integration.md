# 下一步：追踪真实初始化协程，再接活动退休

本批只完成[transport及静态锚点](README.md)。下列是待实现要求，不能据此声称已有tracker、完成解锁或全部消费者已结束。

## 第一件交付：同局两条初始化协程

在生产NativeLocalRunLaunchCoordinator实际setter后、activation前建立NativeLocalInitializationCoroutineTracker，使用同一LocalRunLaunchCommitState和broker。native尝试前先登记有界process owner，并预留两条record、独立enum/Coroutine root槽和Start/Move context；安装失败或root未知均留责任，不重试。受控backend不能取得生产使用权限。

| 角色 | Start的实际Self | 原始Caller RVA | 实际enum | MoveNext RVA |
|---|---|---|---|---|
| getter延迟动作 | 当前launch的实际controller | 3BEFDA | SimpleCoroutine的嵌套`<Delay>d__4` | 3AF0A0 |
| getter等待后释放 | 同一实际controller | 3BF0B2 | SimpleCoroutine的嵌套`<WaitUntil>d__8` | 3B2D70 |

绑定公开Start/Auto@153D380及两条MoveNext前核实际profile/SHA、declared class/method/ABI与字段布局。Start捕获还必须确认同一launch处于真实getter初始化窗口；只按Self或TLS不够。其他controller/caller不认领，可能属于自己的异常线程/metadata调用只原子撤ready。

Start原调用前独立root真实enum，核精确nested class、state=0、todo/predicate及Delay秒数原始40466666位，并记录实际delegate身份。只读原始header/字段或已验证Delegate API；不得猜Delegate raw offsets。将enum身份发布后才能放行original，因为它可以同步调用首次MoveNext。Start正常返回后核/独立root真正的Coroutine；若返回null，只能在已经同步观察到该enum正常false终结时接受，否则unknown。

Move捕获只能匹配已发布且仍root的确切enum，不能从父调用继承权限。正常true对应继续等待；正常false还要复核terminal state及稳定delegate字段，并等Start/两条Move的native activity全部known且0。原函数异常、缺after、自己相关worker/metadata故障、重复Start/非法state/字段变化、未知root操作均撤ready并保留责任。state=-1也在body运行中出现，不能单独作完成证据。

把两条实际自然结束接入生产Tick的commit前核对，并暴露清晰的Pending/Ended/Unknown内部快照。只说明这两条初始化回调已经结束，不签发全部run结束。返回前也要要求其已结束，避免旧getter回调消耗新取的normal信用。

## 仍需补的锁与退休证据

WaitUntil的todo实际Release(false)要核对真实delegate的声明方法/target及原生执行配对；共享3B1B60地址有多个别名，不能直接按地址认领。把观察到的getter Lock/Release与既有菜单连续账本对齐，不用UnlockAll、等待3.1秒或读count=0代替所有权。

之后纳入ControllerOver的Cutout/fade等其他实际异步来源；汇总初始化/结果UI/native Judge及inner/Hold/Progress/Timeline的完整在途和真实来源结束证据。销毁自有controller/音频并确认Unity失效；条件恢复六globals与菜单，先撤分类再redrain，最后释放bundle、各root/handler/bridge/coordinator槽。未知状态仍保留有界责任，不把registry.Close用作替代证明。

这些成立后，才组合完整Return、全新bundle/chart/run代次的Retry和Loader构造根，再开放RequestPlay并按用户安排实测。现行“先继续代码，实测稍后”继续有效。

## 必要验证

受控检查覆盖无关caller/controller、错误class/初始state/常量/delegate、同步首Move、nested/worker/metadata fault、异常和缺after、nullCoroutine两分支、逻辑detach与未知root释放；组合检查保证commit/return不能早于两条真实完成。真实native transport已具备本批fixture，后续新增Adapter需要自己的组合证据；不能仅累计测试数量宣称全覆盖。
