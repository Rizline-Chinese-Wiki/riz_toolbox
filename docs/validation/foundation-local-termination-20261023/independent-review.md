# 多代理审查与处理

本批三个子代理分别负责 Progress/Timeline、结束队列与实际桥接、封闭的未激活取消事务。主代理整合 host、测试注册、构建和证据。source/test 冻结后统一构建，未并行运行共用 obj 的构建。

| 发现 | 修复与验证位置 |
|---|---|
| 只在 handler 内标记终止，handler 故障、忙或 worker 拒绝后仍可能执行 Timeline 尾部 | sealed LevelOver policy 在已 claim 后先运行纯 runtime marker；真实 claim 对 unknown 当前帧保留帧内来源，不污染 ordinary 子帧。LocalProgressIsolationTests 覆盖故障、worker、detach、递归及 unknown A→ordinary B→A |
| registry.Close 后宿主可能提前停止，未等待 bundle 后帧清理 | host 纳入 never-active process pending；模块退出置取消意愿，独立 pump 持续推进。NativeNeverActivatedRetirementRegression.HostWithdrawal |
| bridge 映射尚持根时宿主可能先撤五门 | host 纳入 bridge pending/unknown，先推进 Closed 映射退休，再停止空桥接，最后停止五门。桥接回归覆盖退出次序、worker 持有旧 token 及未知 peer 隔离 |
| 空 handler 只凭 HasPendingWork=false 即开放新局 | ValidateHost 默认拒绝；实际 handler 验证相同 registry、结果 sink、commands 与原生桥接；每次 RegisterPrepared 重新检查 |
| factory 首次 Create 前只凭 controller 数值接受历史关闭证明 | 构造时固定 exact registry/SessionHandle/identity，释放始终验证相同对象；历史地址重用回归 |
| factory 尚未入队就已接管 root，弃置时没有有界保留责任 | 构造先预留进程 32 槽再接管；容量拒绝仍由调用方持有，未知 free 不重试、不退配额 |
| 参数 retention 抛错时零 root 记录不能证明未尝试分配 | RetentionAttempted 在分配前置位；仅明确尚未 retention/export/mutation 的拒绝可进入有限取消分支 |
| 首次确认五对象 dead 的同一个 Tick 不应立即发证 | 实现已有阶段 return，下一 Tick 才发行；允许下一 Tick 与 dead 确认处于同一 Unity 帧。没有宣称必须再晚一帧 |

剩余限制：部分历史 helper 的临时 root 尚未全部纳入新固定记录；异常传播会把封闭事务留为 unknown，不会签发 consumer-end 证明，但不能据此宣称临时 root 记录已全覆盖。实际激活前的 termination reservation、设置初始化、重试/返回与已激活局退休仍待实现及实测。
