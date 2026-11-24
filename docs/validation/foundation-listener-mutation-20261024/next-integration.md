# 下一步集成与验收计划

2026-10-24。本批已经接入已确认的监听器变化入口，并保存协程句柄身份，[完整离线回归](build-summary.json)通过 25 套、1553 项。下一步应把这些有限链路接到真实运行合同，再处理未来生产者和资源退休；继续增加同类测试数量不能替代这些缺口。当前证据范围见 [scope.md](scope.md)。以下均为待实施或待验收，整体底层 19 项任务仍在进行。

## 1. 先闭合真实发布、对象和账本这一条链

为 12 个 mutation 入口确定生产首次发布窗口、固定代码/metadata/ABI 身份及永久 slot 责任。现有新 Operation fixture 直接调用 helper entry，没有 Dobby Prepare/Commit；旧九写 listener receipt 也不包含这 12 个新入口。先在自有可执行目标上验证真实安装、原函数只执行一次、异常和并发故障后保责任，再单独验收实际固定游戏的发布窗口，不能将已运行的 direct-entry fixture 当作首次安装证明。

随后在用户恢复游戏实测时，把同一次真实冷启动中的 receipt、metadata 解析、listener 回调、mutation bridge/broker 和实际 ledger 串联。记录真实 Event/Calls/Group、实际前后图与 GC 根，不用受控后端代替 IL2CPP。故障时停在 Unknown 并保留已采用引用，拒绝重新安装或把未配对记录当作空闲。

生产 dispatch 对象采用目前已有 10 个有限故障注入测试：先保存 Invokable、List、第三处 RDX 的根，后做 Target/Class/List 读取。下一步应补真实 native 回调期间的 Root/Target/Class 故障联动、来源失效和预算耗尽，核对失败后不再读取对象、已保存根不丢失、申请失败不重试。这一对象采用顺序与 V2 receipt/Attach 失败矩阵应分别验收。

## 2. 补齐变化来源和实际 leaf，建立可审计的责任模型

继续以固定 metadata、signature、机器 ABI 和实际调用关系登记操作。当前允许表同时观察上层与下层，因为 AddListener/AddCall/RemoveAll/Dirty 可内联改列表；每条记录需要保留嵌套来源，避免把一次上层操作的下层调用重复计算成不同逻辑变化。

需另核序列化回调的共享地址、generic/inflated/shared 实现、直接字段写、动态增删以及其他持有者。遇到共享代码不能仅靠地址归属业务；身份或类型不明确时拒绝授权，保留未知写者。对每个 actual invokable 还需观察其 delegate leaf 的真实执行和跳过路径；重复读取 leaf 快照不能证明它们均执行。

只有可列明的生产者已封口、在途观察完整配对、相关 graph/owner 代次仍一致，才讨论 `MutationClosed`。最终列表与初始列表相等、观察入口返回或几次 InFlight 归零，都不够证明以后不会再写入或调用。

## 3. 接取消请求，再闭合资源退休

使用现有只读精确身份 API 接 typed StopCoroutine(IEnumerator)、StopCoroutine(Coroutine) 和 StopAllCoroutines；string 形式须先明确同 receiver 上名称与实际记录的关系。保存真实请求入口、原始参数、owner/代次及返回或异常状态，不借旧代次或其他 receiver。身份 Unknown 时仅保留诊断责任，不拿匹配结果释放资源。

状态必须区分“已观察取消请求”“Stop 原函数返回”“引擎已停止该实际协程”。现有 RuntimeInvoke 只有经该 export 的正常 After，不能覆盖编译后的直接调用或调度器移除；共享 Dispose 地址也不能独自确认某个类型或某个代次已终止。还需关联实际 MoveNext/Dispose、引擎调度和非自然退出，并保持 cancellation-request 与 completed 分开。详细静态依据见 [cancellation-research.md](cancellation-research.md)。

最后将取消、listener/leaf、provider、Loader 及全部在途调用纳入同一个资源责任模型，验收 Retry、Return 和 Loader 的完整往返：停止新生产、当前调用排空、借用者退出、旧资源不再可达，然后才提交退休和释放。分别测试正常、异常、退出菜单重建、重新进入和停止失败；单个 tracker 自然结束或一次 Stop 返回都不能替整个 run 的结束凭据。
