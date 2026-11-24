# 必须拒绝的退休负例

所有拒绝均应保留exact分类、native roots与bounded process责任；unknown free不重试。本矩阵是实施验收要求，不是测试执行记录。

| # | 输入/真实状态 | 应得到的行为 | 需要防住的错误 |
|---|---|---|---|
| N01 | `SetActive(false)`调用正常返回，但读回activeInHierarchy仍true | false/unknown；不Destroy source | 将调用返回当生命周期证据 |
| N02 | Destroy请求后同一frame | false，等晚一帧 | 将延迟Destroy请求当销毁完成 |
| N03 | controller dead，controller GO/parent/任一transform还活 | false，根不释放 | 只核一个Unity对象 |
| N04 | controller/GO都dead，但info或其progress/note/judge组件仍活 | never-active分支永远拒绝；active分支继续持有 | 忽略chart拆分消费者 |
| N05 | inactive最后读回`LevelInformation!=0` | never-active分支拒绝 | 靠最终active=false掩盖已发生LevelStart |
| N06 | Awake native/original/before/after任一非零；或reentry/wrong-thread/failure/InFlight非零 | never-active分支拒绝 | 将`CloneAfter=0`误当完全没进Awake |
| N07 | capture window从Instantiate返回后才开始 | 拒绝 | 漏掉同步Awake |
| N08 | 六globals identity全同，但manager/control owner已换 | 拒绝，分别观察old/current owner | 把baseline数值当当前owner证据 |
| N09 | 某delegate identity变了，Target仍为旧controller | active分支拒绝；never-active baseline不变条件失败 | 用delegate指针变化当解绑 |
| N10 | multicast包含一个旧controller target，或无法读取invocation list | 拒绝 | 只核`Delegate.Target`最后项，或吞异常当无目标 |
| N11 | `controllerSet=false`，某六字段仍指旧controller | 拒绝 | 单bool代替六globals |
| N12 | 原scene名称变了，但旧handle仍loaded，或卸载operation未done | active分支拒绝 | 按名称判断卸载 |
| N13 | 旧scene卸载，但旧controller已在DontDestroy scene里 | 拒绝 | 场景卸载代替持久对象退出 |
| N14 | 旧SceneManager仍有活旧GO；当前manager已更换 | 拒绝；不覆盖/清空successor | 只读current manager |
| N15 | ClearDontDestroy返回，list内保留dead wrapper | 允许把dead wrapper记为不活，但仍合取其他证据；不得声称list空 | 误读函数名，要求永远不可能的空list或误放行活项 |
| N16 | SceneManager `_lock=false,_async=null`，event/action list还可能恢复旧run | active分支拒绝 | 只用两个状态字段证明所有协程结束 |
| N17 | 旧controller dead，LevelOver静态SimpleDelay尚未完成 | active全run token拒绝 | 忽略外置协程；staticcallback无run图读取也不是完成证据 |
| N18 | 结果UI dead，但自建面板按钮/排队命令仍持有旧session/result | 拒绝；owner `HasPendingWork=true` | 只核原生LevelOver对象 |
| N19 | exact result capture正在调用或publication输出未确认 | 拒绝；result claim不回退/不重调 | 一次性capture和结束证明混同 |
| N20 | 仅`NativeCalls==OriginalCalls`或两次快照计数相同 | 不授予在途排空 | 这些均是累计入口；original可能未返回，callback可能暂停 |
| N21 | handler内请求关闭自身；nested/worker hook已claim LOCAL | 延后到Tick并经entry/exit屏障；当前claim始终skip | handler返回前释放旧receiver/classification |
| N22 | binding.Dispose正常返回，但retained callback finally尚未执行 | 拒绝 | logical detach当drain |
| N23 | 完整扫描超限、分批扫描尚未结束、数组类型不符、list在遍历时变更 | 拒绝/unknown | 部分无结果当“无消费者” |
| N24 | retirement request指向其他session/generation/controller/resultscene | 拒绝 | 使用旧run proof关闭后继 |
| N25 | caller把`neverActivated=true`传入，但事务曾调用激活/scene/ControllerOver | 拒绝 | 用声明bool替真实backend证据 |
| N26 | 五Unity对象dead后，某free抛异常 | CleanupUnknown，失败root/配额继续保留，不重试 | 重复Dispose制造假成功 |
| N27 | 源prefab发生变化或被当作clone销毁 | 中止并保留unknown；source永不由此路径销毁 | 混淆借用source与独占clone |
| N28 | Prepared取消声称“没有controller”，但native allocation/publication结果未知 | 拒绝空verifier；保持责任 | 0指针代替原生无输出证明 |

正向最小验收：独占inactive parent/clone从未发布给原生可调用源；精确参数注入后取消；从Instantiate前到Destroy后续帧的Awake窗口完整为零；同owner的six-globals/flag完全不变；info/result/scene/coroutine调用均未发生；五Unity对象真实dead；hook在途为0且issuance屏障成立；exact request/token关闭只释放本run。此正例必须有runtime日志才可标记验收完成。
