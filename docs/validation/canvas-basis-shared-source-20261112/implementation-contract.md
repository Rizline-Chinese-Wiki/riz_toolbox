# 同源 Canvas basis 的最小实现合同

本文件只交接下一批实现，不表示已经实现、构建或验收。2026-11-12 读取时，CURRENT-CHECKPOINT 的最新正文仍是位置批次 284；主控正在冻结后的新缓存 16/300 回归。本方案不使用在途结果认证新代码，不改该 checkpoint，不修改 src/tests，不构建、不运行测试、不打开二进制。

下一 API 固定为 JudgementRunCapture.PrepareSeekCanvasBasis(sourceProfile, positionProfile, chartTime, speed)，返回 NativeSeekCanvasBasisCommands。它协调同一份实际来源、同一个 Plan 的缓存和值位置命令。它不是 IGameplayValueParticipant；CompleteSeekPermitted 在 aggregate、journal、原两个 child、source capture 和 Plan 上均保持 false。Unity internal-call、consumer/callback closure、line/ring、缺失 runtime、Hold 引擎退出和完整时钟事务仍未完成，生产真实 Speed/Seek 不开。

## 依据及本批真正要补的缝

| 证据 | 当前事实 | 对实现的约束 |
|---|---|---|
| E1：[当前 checkpoint](/C:/Users/71957/Desktop/riz_toolbox/.docs/CURRENT-CHECKPOINT.md:1)、[下一合同](/C:/Users/71957/Desktop/riz_toolbox/work/canvas-cache-validation-20261112/next-canvas-basis-contract.md:1) | 位置、缓存分别有独立所有者；尚未证明同一次来源 | 新增同源组合，不重做数值、位置或缓存 writer |
| E2：[RunValue 准入](/C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.cs:405) | 三个现有 Canvas 工厂共享一个 gate，每次公共调用递增 construction serial | 新入口只准入一次，不能内部再调用两个公共 Prepare 或公共 Capture |
| E3：[CacheAccess](/C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.CanvasCaches.cs:51)、[PositionAccess](/C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.CanvasPositions.cs:53) | 目前各自 new 一次 source capture 和 Plan | 两个新 child 必须从同一个 holder 派生；相同 profile/目标时间不算同源 |
| E4：[缓存撤销](/C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.CanvasCaches.cs:151)、[位置撤销](/C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.CanvasPositions.cs:172) | 两者目前都把 RequestDispose 下传 CanvasSource | 直接共享现对象会让一个 child 撤掉 sibling，必须改成 child-local 撤销 |
| E5：[source 根及 lifetime](/C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.CanvasSource.cs:54)、[capture lifecycle](/C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasTargetCapture.Lifecycle.cs:16) | source 已拥有完整根和一个长期 NoteView physical lease；capture 已先 reserve 公共 source owner、再创建来源，且绑定一次独立 lifetime | 保留这些机制；共享 child 不再建立第二套物理根，不重构 Capture 根管理 |
| E6：[缓存执行](/C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasCacheParticipant.Execute.cs:5)、[位置执行](/C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasPositionCommands.Execute.cs:6) | 两类 writer 已有别名预检、完整 expected、pending、条件 inverse 和一次执行限制 | 复用原公共 child 对象和 journal，aggregate 只排序与联检 |
| E7：[已冻结静态合同](/C:/Users/71957/Desktop/riz_toolbox/work/seek-canvas-cache-native-20261112/cache-contract.md:15)、[完整 Update 证据](/C:/Users/71957/Desktop/riz_toolbox/work/seek-canvas-cache-native-20261112/cache-static-evidence.json:258) | 完整 Update 先复制 nowTime，再 GetInformations，随后进入位置 setter；SetPosition 消费 floorPositions × speed 及 xPositions | 显式缓存全部到位并读回后才进入位置；不声称复制 native 帧级发布顺序，不调用 producer |
| E8：[缓存独立复核](/C:/Users/71957/Desktop/riz_toolbox/work/seek-canvas-cache-native-20261112/independent-source-review.md:7) | cache final VerifySource 若套进同一 NoteView.WithRetainedSource 会必然重入拒绝 | 新 shared scope 只能是 managed scope，不能包一层 NoteView in-flight/native scope |

Evidence → Finding → Path：E2–E5 证明缺口是“来源与释放隔离”，不是数值或 writer 缺失；因此只增加 Canvas 专属 SharedBasisSource、两个原 owner 的预留/附接入口和一个 coordinator。E6–E8 决定 coordinator 的缓存先于位置、逆序恢复、原 owner 保责及 managed scope 边界。

## 接口和最小实现位置

以下是拟新增/调整的 C# 声明合同，方法体按后文规则实现；不是已经提交的可运行源码。

~~~csharp
// JudgementRunCapture 新虚方法及 RunValue override；不增加 SDK/服务准入。
internal virtual NativeSeekCanvasBasisCommands PrepareSeekCanvasBasis(
    ISeekCanvasNativeProfile sourceProfile,
    ISeekCanvasPositionNativeProfile positionProfile,
    float chartTime, float speed);

// Gameplay/NativeSeekCanvasBasisCommands.cs：
// 仅跨 Judgements/Gameplay 传递这一个 Canvas holder，不建立通用 source/lease 框架。
internal interface INativeSeekCanvasBasisAccess : IDisposable
{
    void VerifyOwnerThread();
    void VerifyLocal(); // 只做 managed 检查，不 dispatch native
    NativeSeekCanvasTargetCapture SourceCapture { get; } // immutable provenance
    INativeSeekCanvasCacheAccess CreateCacheAccess(); // 各一次
    INativeSeekCanvasPositionAccess CreatePositionAccess(
        ISeekCanvasPositionNativeProfile profile); // 各一次
    void VerifySource(Action verifyCallerLocal); // common sweep，自己进入 managed scope
}

internal sealed partial class NativeSeekCanvasBasisCommands : IDisposable
{
    internal static NativeSeekCanvasBasisCommands Prepare(
        Func<INativeSeekCanvasBasisAccess> createBasis,
        ISeekCanvasPositionNativeProfile positionProfile);
    internal void VerifyPrepared();
    internal void Execute();
    internal bool VerifyAppliedValues();
    internal void RestoreSelectedValues();
    internal bool VerifyRestoredValues();
    internal SeekCanvasBasisEffectsJournal GetJournal();
    internal bool CompleteSeekPermitted => false;
    public void Dispose();
}

// 分别加到两个现有 child manager；现有 Prepare(Func<...>) 签名不变，
// 由它调用 Reserve + PrepareReserved，保留独立入口行为。
internal static NativeSeekCanvasCacheParticipant Reserve();
internal void PrepareReserved(Func<INativeSeekCanvasCacheAccess> createAccess);
internal void VerifyExpectedValues();
internal long ObservationRevision { get; }

internal static NativeSeekCanvasPositionCommands Reserve();
internal void PrepareReserved(Func<INativeSeekCanvasPositionAccess> createAccess);
internal void VerifyExpectedValues();
internal long ObservationRevision { get; }

// 两个现有 access interface 各补两项，不改 Read/WriteSlot 的 raw-bit 合同。
NativeSeekCanvasTargetCapture SourceCapture { get; }
IDisposable EnterOperation(); // Canvas holder 的 managed scope
~~~

NativeSeekCanvasBasisCommands 的 OriginalCacheOwner / OriginalPositionOwner 是实际 Reserve 得到的对象引用，诊断入口可在 aggregate Dispose 后读取。不得 clone/recreate child，不得使用一个“合成位置 owner”替换它们。SourceCapture 只用于内部来源身份检查和诊断，不能由外部绕过 holder 提前 Dispose。

SharedBasisSource 放在 NativeJudgementBackend.NativeSeekNoteFlagsAccess.CanvasSource 内，新增一个 CanvasSource.Basis.cs 即可。它实现上面的 INativeSeekCanvasBasisAccess，持有唯一实际 CanvasSource 与唯一 NativeSeekCanvasTargetCapture。新增 backend 私有 CreateSeekCanvasBasisAccess 和内层 CreateCanvasBasis 桥接，保持现有命名层次。

~~~csharp
// CanvasSource 内的私有实现；均为 Canvas 特定方法。
private sealed class SharedBasisSource : INativeSeekCanvasBasisAccess
{
    internal static SharedBasisSource Capture(
        NativeJudgementBackend owner, RunValue run,
        ISeekCanvasNativeProfile profile, float chartTime, float speed);
    internal ChildLease AcquireChild(bool positions); // 每种最多一个，纯 managed
    internal IDisposable EnterOperation(Action verifyCallerLocal);
    internal void VerifySourceInOperation(); // 使用已有 scope，不再 Enter
    internal void VerifyPhysicalLocal(); // 不调用当前 caller，防递归
    internal void VerifySourceUse(); // PhysicalLocal + 当前 scope caller
    internal void RecordUnknownCleanup(Exception failure, object? originalOwner);
    // IDisposable.Dispose 只 drop bootstrap hold，不强制 drop child。
}
~~~

ChildLease 也只嵌套在 SharedBasisSource：一个 holder 最多 bootstrap + cache + position 三个 managed 持有计数；它不 Root、不 RetainPhysicalSource、不再次获取 Transform、Chart、Ease 或 NoteView 根。两个 child constructor 改为接收 SharedBasisSource；先获得自己的 ChildLease，再进行任何 native 元数据/字段读取。source 的 Root/Reference 仍复用原 _core._retained 去重，缓存数组需要的根进入同一套原物理源。

原独立 CreateCaches / CreatePositions 路径各建立一个 own holder，构造自己的一个 child，然后释放 bootstrap hold。保留原入口和原 writer，不保留两套构造算法，也不新增旧兼容分支。直接公共 CaptureSeekCanvasTargetSource 的路径保持原样。

## 工厂顺序与构造准入

1. RunValue 首先 owner-thread，检查同一个 _capturingSeekCanvasTargetSource，再检查 run/backend disposed，做 null/finite 的纯 managed 检查。它只递增一次 _canvasSourceConstructionSerial，只设置/清除一次既有 gate；finally 一定清 gate。
2. 调用 NativeSeekCanvasBasisCommands.Prepare。它先 reserve aggregate 的 256 owner 槽，再 Reserve 原 cache owner，再 Reserve 原 position owner。这些预留均在调用 createBasis、profile getter、source/native publication 之前完成。任一预算满，先释放已预留但未附接的纯 managed owner，不采集来源。
3. createBasis 只调用一次 SharedBasisSource.Capture。holder 先有 bootstrap hold，再通过现有 NativeSeekCanvasTargetCapture.Capture 创建一次 CanvasSource；现有 Capture 自己先 reserve source owner 后才进入 native。它产生一次 Plan，保留完整 TransformReferences。
4. cacheOwner.PrepareReserved(basis.CreateCacheAccess)，再 positionOwner.PrepareReserved(() => basis.CreatePositionAccess(positionProfile))。两个 child 使用同一个 holder、同一个 capture 对象和 capture.Plan 对象；ordinal 直接配对同一次 TransformReferences，不能按 CanvasMove.index remap。
5. 预检两类完整目标、aliases、exact metadata/ABI，并读取两类完整 baseline。保留现有各自双样本规则；随后按 cache.VerifyPrepared → position.VerifyPrepared → common source sweep → cache.VerifyPrepared → position.VerifyPrepared 做跨 child 复验。任何失败仍为零写。
6. 断言 ReferenceEquals(cache.SourceCapture, position.SourceCapture)，且与 basis.SourceCapture 是同一个对象；两类目标必须由该对象的 Plan.Rows / Request 派生。只比较 Request 数值或 source profile 不足以通过。
7. 成功后 aggregate 持有 bootstrap，两个原 child 各持有一个 lease；Prepared 才能为 true。已有来源/数值捕获不重算、不调用 GetInformations/SetPosition/Update、不调用 Ease 初始化器。

不能在新入口内部调用 run.PrepareSeekCanvasCaches、run.PrepareSeekCanvasPositions 或 run.CaptureSeekCanvasTargetSource；这些会再次触发同一 gate/serial。保持 VerifyCanvasSourceConstruction 现语义：构造中的同 serial run 撤销要拒绝；先前已经返回的独立 owner 不因后续一次被取消的新构造而失效。RunValue 退出后原独立来源仍可用，backend 退出仍撤销准入。

两个 PrepareReserved 负责原来的 createAccess 异常接管和准备逻辑，不再次 reserve，不默默更换原 owner。Reserve 后尚未附接的 Dispose 只退还 managed 槽。不能用“先构造 source，再看 child 容量”替代此顺序。

## shared scope、logical Dispose 与 physical release

SharedBasisSource 不改变 NativeSeekCanvasTargetCapture 的 root/lifetime/disposal 协议。source 仍把唯一 BindLifetimeGuard 绑定到 capture.Lifetime；绝不能改绑某一个 child 或 aggregate 的永久 lifetime。

两个 child manager 的现有 Operation 在 _busy 检查后、第一次 Local/native 之前，用 _access.EnterOperation() 获取 shared managed scope，结束时先离开 scope，再执行原 finally 的 deferred Release。确认入口仍须在任何可失败准入之前撤销旧成功。child constructor 自己也用同一 scope 覆盖其 native 初始化；纯 managed SourceCapture 身份属性不需要 native/scope。

共享 scope 只允许一个活动调用方。它保存本次 caller 的纯 managed OwnLifetime/Local（不含 source.VerifyLocal），用以在深层 source 循环中检查撤销。CanvasSource 仅增加可选的 holder 引用，在 OwnLifetime 调用 holder.VerifySourceUse；它不再 Bind 任一 child。VerifySourceUse 执行 PhysicalLocal + 当前 caller guard；PhysicalLocal 不回调 caller，因此没有 OwnLifetime/source 递归。原直接 source capture 没有 holder，行为不变。

child VerifySource 在已经进入的 scope 内调用 holder.VerifySourceInOperation，然后复用该 child 原有 target metadata/引用检查。aggregate 的 common source sweep 使用 basis.VerifySource(aggregate.Local)，临时获取同一个 shared scope。这样 cache/position 之间及 common sweep 回调里的 sibling 功能调用会在任何 native export 前拒绝，而正常前后两个阶段顺序进入。

这个 scope 不是 NoteView.WithRetainedSource：不持 NoteView 的 _inFlight，不改变 Runtime writer。cache writer 继续复用原长期 physical lease，最后 source recheck 可独立进入 NoteView；position getter/setter 继续保留原局部 WithRetainedSource。beforeDispatch 的最终 admission 只做 managed guard/登记，不能在里面补全图/source native 扫描。

| 动作 | logical 变化 | physical 变化 |
|---|---|---|
| child RequestDispose | 只标记本 child withdrawn，并阻断其后续回调；不调用 _source.RequestDispose | 不扣 lease，不影响 sibling |
| child 公共 Dispose | 保留原生命周期：撤旧证明、通知自己的 Access、按原 Release 条件判断是否可释放 | 只有原 child 确认可 Release 才 Dispose 自己的 ChildLease |
| cache 已完整确认自己的 applied/restored 值 | 只关闭 cache storage 责任；Dispose 仍须按现规则 fresh 复验 | 可 drop cache lease，position lease 和 bootstrap 不动 |
| position setter 任一方向已进入 | 原 position _externalEffects 永远保留；值恢复或 cache 确认均不能清除此事实 | 原 position Release 继续拒绝，因此不能 drop position lease |
| aggregate Dispose | 撤 aggregate 的证明；尝试 Dispose 原两个 child，保留所有异常及对象 | 只释放自己的 bootstrap，绝不能替原 child 扣 lease |
| 最后一个已获准持有者释放 | source 无进一步可用 caller | 当计数为 0 且无 active scope，才一次调用原 capture.Dispose |

如果 Dispose 发生在回调/active scope 内，bootstrap drop 可以登记，但 terminal capture.Dispose 必须推迟到 scope 完全退出，避免在 capture._busy 时重入 Dispose。aggregate busy Dispose 收集 child 的逻辑撤销/保责错误，不在内部提前清空引用；外层 finally 完成 deferred bootstrap release，并把 cleanup 错误附到外层原失败。原 child 在 busy 时的未确认责任照旧保留，不能靠 sibling 的旧成功证明释放。

holder 在 native free 前就设置 terminalAttempted；未知结果保留 holder、capture 和原失败，永不重试。重复 lease Dispose 只能返回既有结果/抛既有责任，不能再次递减或 free。计数归零不等于已 released：只有 capture.Dispose 确认成功才置 released。非最后 child 的 drop 是它自己的逻辑物理持有权已交还，不表示整个实际 source 已 free。

未知 temporary publication/free 发生时，Access 除原 Attach 逻辑外还必须 RecordUnknownCleanup 到 holder。holder 此后在 managed admission 拒绝所有 source/native 使用及 terminal cleanup，保留原 publication/source owner；不能让健康 sibling 再释放同一完整源。已观察的普通 source 过期也 sticky 拒绝访问，但已知 root 的一次正常清理仍可进行；不要把“来源不再当前”误作“free 结果未知”。

## 一次执行、完整复验与条件恢复

VerifyExpectedValues 是两个原 child 的小型内部入口：Operation 中完整 Observe、拒绝 sticky Unrecognized、VerifySource；它不确认 applied/restored，也不写、不改变 expected。它允许在 child 部分执行后核完整 expected，包括未进入槽和 aliases。

Execute 的固定顺序：

1. 一次性状态检查，记录 ExecutionEntered，撤 aggregate 和两类当前证明；cache.VerifyPrepared、position.VerifyPrepared、common source sweep。此时任何拒绝均不进入写入。
2. 原 cache.Commit 一次。保留其既有显式顺序：三数组的完整 unique slots，然后 nowTime，再 speed；不改成 native producer 的逐行 store 顺序。cache.Confirm 必须完整成功，随后 position.VerifyPrepared 和 common source sweep。
3. 原 position.Execute 一次。第一个 setter 进入前，三个数组及 nowTime/speed 已完成并被读回；Plan.FloorSpeed 不额外乘 Request.Speed，位置使用同一 Plan 的 LocalPosition。
4. 做两轮联合读回：common source sweep → cache.Confirm → position.VerifyAppliedValues，重复一轮，最后 managed Local。两轮必须全部成功才记 ExecutionFinished / aggregate AppliedValuesVerified。任何一轮失败先撤 aggregate 旧成功，记录原两个 journal。

显式合同是“缓存/time/speed 完整可观察后，再位置”，不是 native Update 同帧协议。已有静态完整 Update 的 time-before-GetInformations-before-position 不能证明这里的逐 store/回调发布等价。联合读回也是有序完整样本，不是原子 CAS；不宣称排除未观察外部 writer/ABA。

VerifyAppliedValues 再次执行上述两轮完整联合观察，不消费旧 Confirm。入口先撤旧 aggregate 成功；任一生命周期、源、缓存或位置失败都不能留下旧 aggregate true。原 child 的 Confirm/VerifyAppliedValues 入口继续撤自己的旧证明。

RestoreSelectedValues 显式调用才进入，不能在 Execute catch/Dispose 里自动补写：

1. 记录 RestorationEntered，撤所有 aggregate 证明；common sweep、cache.VerifyExpectedValues、position.VerifyExpectedValues。任何 observed 第三态阻断本次所有 inverse，不能只看已进入槽。
2. 先原 position.RestoreSelectedValues，再 position.VerifyRestoredValues；未进入 setter 的完整 baseline 只验证，零 setter。部分 setter 已进入且可观察为其 before/target 时，复用原 pending/逆序规则。
3. position 阶段成功后，common sweep、cache.VerifyExpectedValues，然后原 cache.Restore、cache.ConfirmRestored。cache 只逆序恢复已进入物理槽，保留 NaN payload/-0 的 baseline bits。
4. 两轮 common sweep → cache.ConfirmRestored → position.VerifyRestoredValues，最后 managed Local；全部成功仅可记 RestoredValuesVerified。任一 source/cleanup/第三态失败停止后续 inverse，保留两个原 owner 和剩余 expected。

可见值全回原位也不会把 position.ExternalEffectsUnclosed / SideEffectsUnknown / HasRetainedResponsibility 改为 false。aggregate 的恢复成功名称只指 selected values；不能返回 GameplayControlRestoration 给完整事务当引擎恢复证明。

如果允许通过 OriginalCacheOwner / OriginalPositionOwner 独立操作，则每个 child 的功能 Operation/Dispose 入口递增纯 managed ObservationRevision。aggregate 成功记录当时两 revision，GetJournal 发现不一致就撤对应 aggregate 旧成功，不能因 child 后来再次 Confirm 又复活早先 aggregate proof。GetJournal/线程诊断不递增。只增加这两个有界计数，不新增事件系统。

## journal 和全部失败归属

SeekCanvasBasisEffectsJournal 至少包含：Prepared/ExecutionEntered/Finished、AppliedValuesVerified、RestorationEntered/Finished、RestoredValuesVerified、Disposed、CompleteSeekPermitted=false、HasRetainedResponsibility、ExternalEffectsUnclosed，以及同一个 SourceCapture/Plan 的身份、OriginalCacheOwner、OriginalPositionOwner、两个原 journal 的不可变副本、阶段失败列表和 source cleanup owner。Child journal 的数组/值/attempt 列表继续按现实现复制，aggregate 不重建 setter/store 记录。

cache journal 补 SourceCapture；position journal 补 SourceCapture 和 SourceCleanupOwner。构造失败尚无 _access 时，从该 Access 的异常 provenance 记录 source capture；尚未派生任何 child 的 source 构造失败则只由 aggregate + 原 source capture 持有，child 不冒领 native 责任。

保留现有 SeekCanvasCacheCleanupOwner、SeekCanvasPositionCleanupOwner、SeekCanvasPositionEffectsOwner：它们必须是原两个公共 child 对象。aggregate 只增加 SeekCanvasBasisOwner / SeekCanvasBasisCleanupFailure / SeekCanvasBasisFailures 这样的独立键，不覆盖 child 或 NativePublication 的键。Access 构造/临时 cleanup owner 要在 holder 的原始来源记录中保留；显式 SourceCleanupOwner 优先保存真正的 publication/source origin，不能被外层 Attach 覆盖成 aggregate。position 的 AdoptCleanupResponsibility 应与 cache 一样优先保存已有 SourceCleanupOwner，再 fallback Access owner，同时保留 Access → holder 的责任链。

| 失败点 | 必须保留的责任 | 后续准入/释放 |
|---|---|---|
| 任何 owner 预算或纯 managed 参数拒绝 | 已预留但未附接的 owner 清楚退还 | 零 source、零 native；不得调用工厂后才拒预算 |
| source factory 在返回前失败且无 cleanup unknown | 原失败；已有 capture 按现规则清理 | 两 child 不得伪装 Prepared；预留清理一次 |
| source factory 返回前出现未知 free | 原 capture 公共 owner、native/source owner、typed cleanup failure、aggregate | 不丢掉未赋值对象；不重试，不继续 child 初始化 |
| cache access/准备失败；或 position access/准备失败 | 已创建 child、失败 child 的原公共 owner、holder/capture、原 cleanup origin | 整体零写；尝试所有独立清理，不因第一个 Dispose throw 跳过其余 |
| cache 未完成或 partial native | 原 cache attempts/pending、complete expected；原 position 仍显示未进入 | 不执行位置；仅明确 Restore 可恢复已进入 cache |
| cache 完成后进入位置前失败 | 原 cache journal，原 position 的零进入事实 | 不自动 cache inverse；原状态/来源过期事实不能抹除 |
| position partial native 或 setter 后撤销 | 原 position EffectsOwner、entered/pending/native-return 记录；原 cache owner | 值读回可以失败，已进入事实不能丢；source 根由原 position lease 保留 |
| setter 回调改 cache，或 cache 检查回调改 position | 两类各自 observed/sticky 事实和 aggregate 失败 | 联合成功撤销；第三态不覆盖，不把回到 expected 当成未发生 |
| 后续 Confirm/ConfirmRestored 失败 | 原 journal 不可变历史，新成功标志为 false | 保留未闭合 storage/effects；不能继续消费旧 true |
| busy / worker / sibling reentry | 原在途 owner；拒绝操作的明确信息 | worker/reentry 在任何 native 前拒绝；拒绝不能扣 scope/lease |
| aggregate Dispose 中一个或两个 child 抛错 | 两个精确 child、每一份错误、holder bootstrap/source 原因 | 仍尝试其余 child；不清 child 引用，不强制 drop effects lease |
| temporary 或最终 free 未知 | 原 publication/root/source owner + 原 child/aggregate 接管 owner | 共享源冻结，native cleanup 不重试；不能仅记字符串 |

GetJournal 是 owner-thread 的纯 managed 诊断；在 busy/cleanup/reentry 中仍按原模式拒绝。即使 aggregate 已 logical Dispose，原 child 的 effects/cleanup journal 仍可读取。aggregate.HasRetainedResponsibility 为两个 child 保责或 holder 未知 cleanup 的并集；cache 的成功不能清除 position 的保责。

## 最小修改分工

| 修改单元 | 具体工作 | 边界 |
|---|---|---|
| NativeJudgementBackend.cs | 新 virtual/override 和一个 private factory 桥；复用同 gate/serial 一次 | 不改旧 serial 失效语义，不调用旧公共工厂嵌套 |
| 新 CanvasSource.Basis.cs | 实现 SharedBasisSource、ChildLease、managed shared scope、bootstrap、once cleanup 和 construction provenance | 不新增物理根，不新增通用源/租约框架 |
| CanvasSource.cs | 可选 holder 参数/字段；OwnLifetime 增 holder.VerifySourceUse | 保留唯一 capture lifetime、原 NoteView lease、原根及 metadata |
| CanvasCaches.cs / CanvasPositions.cs | ctor 改收 holder，SourceCapture 同源；各自 OwnLifetime；RequestDispose 只撤自身；Dispose 仅 drop 自己 lease；unknown 向 holder 登记 | ReadWrite/Read 的精确 writer、native admission、aliases 及 ABI 算法复用 |
| 两 child Lifecycle/Execute/cs | Reserve + PrepareReserved、EnterOperation scope、VerifyExpectedValues、诊断 provenance 与 revision | 不把 PositionCommands 改成 value participant；不削弱原 partial/sticky/effects 规则 |
| 新 NativeSeekCanvasBasisCommands.cs 及按需 Lifecycle/Execute 分片 | 一次构造、双 baseline 联检、上述执行/恢复/读回顺序、Dispose 全错误收集和 immutable journal | 不接生产 Seek，不给缓存确认添加引擎 closure |
| 新 tests/CanvasBasis 目录和 Program selector | 以下 16 组正式 backend TDD，复用两个既有 official-export fixture | 本交接没有写/运行这些测试；后续须用新源码新产物验收 |

无需改 NativeSeekCanvasTargetCapture 的根、owner registry 或 public Dispose；无需新增 profile、Runtime 泛化读写 API、数值求值器、producer hook、性能功能或旧兼容。

## 16 组正式 backend TDD

每组必须从真实 NativeJudgementBackend.CaptureRun → RunValue.PrepareSeekCanvasBasis 进入；可复用 FakeNative.ConfigureCanvasCaches(positions: true) 的官方导出 fixture，不 mock INativeSeekCanvasCacheAccess / INativeSeekCanvasPositionAccess / basis access。新增 16 Cases 和独立 selector；不把旧 cache/position 的 16 组改名当新增。记录自然退出、原输入冻结图和实际依赖产物后才可签本批通过。

| # | 场景 | 关键断言 |
|---|---|---|
| 1 | 单次来源及 Plan | 两 child SourceCapture 和 Plan 均 ReferenceEquals；只有一次数值 Plan 计算及对应 Range 调用；完整实际 ordinal 映射一致，非仅相同 profile |
| 2 | 准备双 baseline、完整源及 exact metadata | 零 cache store/Unity setter；从 cache 采集切到位置采集时改源/另一类 baseline 必拒绝；不重新捕获接受新来源 |
| 3 | 空完整图及非法请求 | 三空数组仍有两个 scalar target，零 position setter；null/nonfinite 在 source/native 前拒绝；CompleteSeekPermitted 始终 false |
| 4 | 两类多别名与冲突 | 同物理 array/index 跨 kind 相等只写一次；同 Transform 相等只 setter 一次；任一种冲突都使整个 basis 在所有写之前拒绝 |
| 5 | 依赖顺序及全量成功 | 第一个 setter 的 export-boundary 看到三数组、nowTime/speed 全为同 Plan 目标；FloorSpeed 未多乘 speed；不调用 producer/consumer，不按 move.index remap |
| 6 | fresh metadata/ABI/引用漂移 | 热缓存后改 scalar/array 元数据、Single/Vector3 width、方法/当前数组引用，写前拒绝；保持旧根，不 retarget detached 引用 |
| 7 | cache 前/后 partial native | 官方 scalar/store 进入前后分别注入失败，位置尝试为 0；原 cache pending/owner 精确；条件 inverse 仅已进入槽且恢复 NaN payload/-0 |
| 8 | cache 成功到位置前撤销 | source/profile/revision/backend 撤销挡住首 setter；aggregate 和 cache 旧成功不可继续作组合证明；不自动 inverse 或丢 cache owner |
| 9 | position 前/后 partial native | 已进入 setter 不论 before/after/throw 都记原 PositionOwner；读回失败也保 pending；恢复位置先于缓存，effects 和完整 source 根始终保留 |
| 10 | 跨 child 回调第三态 | setter 改 nowTime/cache，或 cache 观察回调改未进入位置；联合读回失败、sticky；外部改回 expected 后不覆写或伪造整体恢复 |
| 11 | 完整源跨阶段过期 | 数组槽、List backing/size/version/tail、KeyPoint、Ease、static vector、range/profile 在阶段间漂移都拒绝；不重算 Plan，不丢已进入 owner |
| 12 | RunValue 独立 lifetime 与构造 serial | 返回后 Run.Dispose 不损坏原两 child；下一取消构造不伤旧 holder；source/cache/position/basis 四入口同步重入均在 native 前拒绝且没有内部二次 serial |
| 13 | child logical Dispose 隔离及 refcount | 未执行时先 Dispose cache，position 原 owner 仍能 VerifyPrepared/执行；反向同理；aggregate 两类要求不满足要拒绝；最后无 effects hold 才一次 free 原源；double Dispose 不重复 free |
| 14 | shared scope、worker 和 callback Dispose | 对 sibling/aggregate/common sweep 同步重入、worker 都零 native；回调撤销后深层 source 不再继续 exports；不出现 NoteView in-flight 自重入；bootstrap cleanup 延迟至 scope 退出 |
| 15 | 旧证明、不可变 journal、aggregate Dispose | 原两个 owner identity 不变；失败再确认和 child revision 撤旧 aggregate proof；历史 journal 不变；cache 已恢复/释放而 position 已入 setter 时，aggregate Dispose 仍保原 EffectsOwner 和原根 |
| 16 | 工厂预算与全部未知 cleanup | aggregate/cache/position/source 各容量边界均在 acquisition 前拒绝；source 构造、position getter 临时 result/receiver、cache pin、最后 free 各注入未知；精确公共 owner + 原 publication/source owner 可从异常/journal 找回；所有 repeated Dispose/访问不重试 native free |

第 16 组必须包含“位置 setter 从未进入但 getter temporary free 已未知”和“已进入位置后 aggregate Dispose 同时碰到 cache 失败”两个分支，不能只测试成功后的末端 free。原根释放断言应排除允许正常释放的本次临时验证根，逐 handle 检查 original independent root 不被提前释放和每个未知 handle 只 free 一次。

本文件到此可交给实现者动工；本 agent 没有实现或验收上述新增行为。本次只新增这份合同，下一批先完成这些同源边界，再继续既定 line/ring 等目标。
