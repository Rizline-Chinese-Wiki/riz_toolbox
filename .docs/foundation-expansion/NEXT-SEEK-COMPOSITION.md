# 从状态部件接到完整 Seek

本页记录 2026-11-07 的实现边界。交付的开发预览是冻结产物；本页后续源码不能借该旧 ZIP 的验证结果验收。当前进度和本轮实际测试结果以 [CURRENT-CHECKPOINT](../CURRENT-CHECKPOINT.md) 顶部为准。

## 已有部件各自负责什么

| 部件 | 实际负责的状态 | 不能据此推断的状态 |
|---|---|---|
| `NativeSeekChartPlan` | 完整谱面与 BPM 映射、明确游戏秒数目标的分区 | 跨目标 Hold 如何恢复、任何原生写入 |
| `NativeSeekRuntimeNoteView` | 实际 runtime note 与谱面位置/线段的引用对应、两个 NoteControl 数组 | 没有实例化的谱面音符已存在；标记或渲染状态已重建 |
| `NativeSeekJudgeStateView` | 三条 Judge List 的原槽、完整 backing capacity、原顺序与 signed hold 计数 | 初始队列顺序、Hold 协程已经退出 |
| `NativeNoteCursorRewindParticipant` | 四个 NoteControl 扫描字段的重置、读回及条件恢复 | 音符、Judge、输入或 renderer 状态一起恢复 |
| `NativeJudgementCounterResetParticipant` | 七个明确计数器的重置、读回及条件恢复 | 全部计分、生命、历史或结果状态恢复 |
| `NativeSeekNoteFlagsParticipant`（已构建并通过新增 18 组） | 每个实际音符显式指定的 `isActive`、`destroyed`、`missed` 三个 Boolean 值 | 三个值等于原生初始化值、renderer/pool 同步完成、完整 Seek 可开放 |
| `HoldCoroutineLifecycle` 与正式 Start/Move 接线 | 实际调度身份、同步/跨帧 Move、自然完成、同代暂停及换局引用退休 | 所有 Hold 均被观察、引擎清理完成、全局 HoldIdle |
| Hold 六类 Stop 请求接线（新增 9 组，判定套件 70/70、通用取消回归 26/26） | 实际 Stop 原调用前后的精确 receiver/iterator/handle、嵌套请求及跨局责任；异常旧回调不污染新局 | Stop 返回等于引擎退出；named Stop 的具体匹配；已验证真实游戏 bootstrap；完整 Seek |
| `NativeSeekJudgeQueuesParticipant`（新增 23 组，当前判定 93/93） | 三条实际 Judge List 的显式顺序/重复成员、官方 Clear/Add、扩容归属和完整原存储条件恢复 | 自动推导目标顺序、输入集合、Hold 退出；不同 List 共享非空 backing 尚不支持 |
| `NativeSeekFingerIdsParticipant`（新增 25 组） | null/空/非空 Int32 List 的显式序列、官方构造/写入、原字段及完整存储条件恢复 | 实体输入、HoldIdle、目标策略及 renderer 同步；异 List 共享非空 backing 暂不支持 |
| `NativeSeekRendererCommands`（新增 18 组） | 显式真实 Appear/Disappear、FIFO/增长/立即与延后回池及完整可见状态记录 | 精确 inverse、隐藏分配清理、延后 consumer 退出、目标几何或完整 Seek |
| `NativeSeekTargetStatePlan`（新增 20 组） | 实际来源的精确排序/窗口/队列推导、练习重置输出、跨目标 Hold 与缺失实例要求 | 已完成初始化写入、目标几何、输入与 Hold 引擎退出、完整 Seek |
| `NativeSeekHoldStateParticipant`（新增 22 组） | 两条实际 Hold List、逐 Hold 静态/长度和三控制器缓存的显式写入、完整原存储条件恢复 | 目标几何、Transform、输入/协程退出、HoldIdle或完整 Seek |

三标记写入目标必须完整覆盖 owned NoteView 的实际对象，每个引用恰好一次。它接受不同音符的不同目标值，不从目标时间或 prefab 的默认状态猜值。`realTime`、`holdEndRealTime`、`isStaticNote`、`inChallengeTime`、`lastFrameHoldLineLength`、`_index`、`fingerIds` 和 renderer 引用不属于这次写入范围。

## 下一步按真实依赖补齐

1. **Hold 引擎退出证明仍待补齐，已闭合的请求接线不重做。** Start/Move 与六类 Stop 均已正式接入 backend/module，受控 broker 组合验证见 `docs/validation/hold-stop-requests-20261107/`。生产使用原有 pre-init Stop channel，缺少 bootstrap 时只报告 StopUnavailable，不能改用晚期 patch。引擎 transport 的 `ProductionStartupAdmissionKnown` 仍为 false，不得直接放开；后续需要 Hold 专属 managed/native owner、同代身份 provider、可保留至 engine terminal 的责任和真实 source closure，再接实际 Detach/最终清理及调度退出。现有 LOCAL 销毁 seal 不能认证仍存活的 Hold receiver。未知 origin、factory 和全局覆盖尚未证明，`FullCoverageKnown` 仍 false。不能用请求返回、`holdOnJudgeCount == 0`、空记录或局部自然完成签 HoldIdle。
2. **Judge 队列和 fingerIds 显式写入已闭合，不重复。** fingerIds 新增 25 组，判定 118/118、底层写入 36/36，见 `docs/validation/finger-ids-20261108/`。逐实际 note 显式覆盖、区分 null/空 List、恢复原字段及完整存储；共享非空 backing 和超预算明确 Unsupported。不提供设备输入、HoldIdle 或完整 Seek 准入。下一独立功能为第 3 项目标策略和渲染同步。
3. **目标策略、renderer 显式命令及 Hold 两 List/缓存显式恢复已验证，不重复。** 新部件见 `docs/validation/hold-state-storage-20261110/`，仅写调用方完整目标并条件恢复原存储。下一独立工作是 Hold 目标几何：读取并计算 holdLine/holdEndFlag 与 canvas/line/ring/parent 的目标坐标及必要 Transform 操作，同时保持实际 source/consumer 责任。随后补 Judge 初始化字段/挑战计数写入与缺失实例。renderer event、隐藏对象分配和 dirty 延后回池仍需 consumer/source closure；Hold engine 退出仍单独待补。
4. **接入完整事务和时钟。** 复用 `GameplayPreparedValueControl`，由 backend 组装完整参与者集合，核对同一 run/source 身份和精确写入许可，再提交、读取实际时间与各状态、失败时逆序恢复。缺少任一必要参与者时，不能以已有几个参与者确认成功来开放公开 Seek。
5. **验收当前完整组合。** 对新源码做正常构建、正式入口故障测试和独立 SDK 消费验证；真实游戏首次准入仍等待用户另行允许实测。B01–B19 的其他未完成项继续保留，不以这条 Seek 路径替代全范围。

## 本轮检查原则

新 N 音符部件使用完整阶段边界检查，加逐音符精确接收者/字段检查与读回。不要把固定七计数器的“每写一字段重扫全部”的实现直接扩大为 N² 图遍历，也不要为了避开这一问题缩小已经声明的完整图范围。阶段检查与条件写入不是原子 CAS，不能排除未观察到的外部 ABA；未知释放和已进入 setter 的责任必须保留。

现有静态依据为 `docs/validation/gameplay-semantics-20261020/note-identity-proof.json`。其中 LevelControl.Start 实例化 prefab 后取得 NoteInformation，不能据此证明其全部 Boolean 初始值为 false。历史 source-only 合同描述的是当时尚未实施的候选，不限制用户此后已授权的实现、构建和测试。
