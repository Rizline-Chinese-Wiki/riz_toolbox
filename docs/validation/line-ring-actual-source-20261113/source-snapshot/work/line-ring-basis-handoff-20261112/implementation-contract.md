# 下一 line/ring basis：先补完整实际来源

这是下一批短实施合同，未实现、未构建、未运行测试，不是新增用户授权。当前 Canvas basis 源码已冻结；主控交接时 D:/riz-build-cache-next/canvas-basis-20261112/root-r2/final-r2（controller 6336）在途且尚未通过，本文不核活性或执行终态，不能把实现或独立源审算成执行验收。本文件不改 src/tests、checkpoint，不打开或执行游戏二进制。

**下一具体功能固定为：在现 Canvas basis 上新增 CaptureLineRingSource，读取同一 NoteView 下完整实际 dynamicLines / lineGroups / groups.lines、端点输入及目标对象 baseline，返回独立保根的不可变 source capture。** 首批零写，不依赖任一 Hold 实例；它为后续纯投影和实际 writer 提供真实完整来源，不宣称完成 line/ring 重建。

## 1. 已有依据和不可跨越的证据边界

| 现有文件 | 可以复用的事实 | 尚不能据此声称 |
|---|---|---|
| [CanvasSource.Basis.cs](C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.CanvasSource.Basis.cs)、[CanvasBasisCommands.cs](C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasBasisCommands.cs) | 唯一 CanvasSource/capture/Plan、managed holds/scope、原 cache/position owner、terminal observer | 当前新图已经通过；后续 line/ring 可另捕一份同 profile 来源冒充同源 |
| [NativeSeekRuntimeNoteView.cs](C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Gameplay/NativeSeekRuntimeNoteView.cs)、[SeekNotes.cs](C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.SeekNotes.cs) | 完整 chart lines/points 引用与 time、实际 Note 数组；每实际 Note 的 runtime line/start/end/group identity 与 chart location | 没有 Note 的全部 line/group 已枚举；所有 group 到 chart line 的对应关系已证明 |
| [NativeSeekHoldGeometryProfile.cs](C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Gameplay/NativeSeekHoldGeometryProfile.cs) 的 CaptureFields / NewBodies | LineControl、LineInformation、LinePoint、LinesGroupInformation 的已列 exact 字段；四个 LineControl 完整 body pins；若干 Transform getter wrapper | Hold profile 可直接充当无 Hold 的生产入口；body pin 已证明每条数值和副作用语义 |
| [geometry-static-evidence.json](C:/Users/71957/Desktop/riz_toolbox/work/seek-hold-geometry-native-20261110/geometry-static-evidence.json)、[geometry-profile-verification.json](C:/Users/71957/Desktop/riz_toolbox/work/seek-hold-geometry-native-20261110/geometry-profile-verification.json) | exactFields、geometryBodies、externalBodyEdges、原始完整指令文本、原 hash 和显式 false 声明 | live ABI/目标环境、resolved Unity effects 或 inverse 已观察 |
| [LineControl.Update.asm.txt](C:/Users/71957/Desktop/riz_toolbox/work/seek-hold-geometry-native-20261110/LineControl.Update.asm.txt)、[Start](C:/Users/71957/Desktop/riz_toolbox/work/seek-hold-geometry-native-20261110/LineControl.Start.asm.txt) | 冻结文本显示 Start 取 speed/canvas；Update 更新 time/sceneWidth/UI scale，再 SetDynamicLines、SetRing、SetLineColor | 显式目标 API 应调用这些 producer，或独立 store 与原帧发布等价 |
| [HoldGeometry.Read](C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.HoldGeometry.Read.cs)、[HoldGeometry.Plan](C:/Users/71957/Desktop/riz_toolbox/src/RizToolbox.Adapter/Gameplay/NativeSeekHoldGeometryCommands.Plan.cs) | 已有 Hold 所引用 line/end/ring/Transform closure 的 actual 读取与 Hold 操作模型 | 全部实际线段、ring segment selection、line renderer mesh 或颜色已完成 |

已知字段直接按上述 exactFields/CaptureFields 引用，不在新合同猜 offset/RVA：LevelInformation.lineControl；LineControl 的 levelInformation、dynamicLines、lineGroups、_canvasControl、_nowTime、_sceneWidth、_speed、_gameSceneScale；LineInformation 的 lineIsStatic、canvasIndex、startLinePoint、endLinePoint、endTransform、endLineInformation、linesGroup；LinePoint 的 time、xPosition、easeType、canvasIndex、floorPosition；group 的 judgeRingTransform、ringActive、lines、nowLineIndex。

必须另补的静态合同分四件，未证明前不写对应对象：

- SetDynamicLines 的全部裁剪边界、采样数量、舍入、常量及顺序；externalBodyEdges 明确包含尚未 pin 的 GetEaseWithIndex、LineInformation.SetPositions 和无 metadata 的 helper。GetEaseWithProgress 的已有合同不能替代它们。
- SetRing 的完整段选择/边界/nowLineIndex/ringActive 写入与位置路径；其 Appear、Disappear、set_color 是未 pin 的外部边，不能当作纯字段 setter 或无副作用。
- LineInformation.SetPositions 对实际 renderer/mesh/数组容量的生产合同，以及 static line 的初始化/重建来源。当前 dynamicLines 不等于全部 static + dynamic 线段。
- 无 Note 的 group/segment 如何对应 chart line、所有父链/坐标转换及颜色依赖。找不到明确关联就报告 RequiredWork，不按数组 ordinal、最近时间、Hold 关联或引用排序猜测。

这些是后续待证明项，不要求本次打开二进制或探索新目标。旧 verify-geometry-profile.py 默认会读取原 DLL，当前不要执行；现有 JSON/asm 只能支持其已记录范围。Evidence → Finding → Path：完整枚举缺口由 NoteView 与 Hold reader 的覆盖范围证明，因此先做 actual source，再补纯投影语义，最后按明确目标写入。

## 2. 下一 API 与有界数据合同

以下是拟实现签名，方法体属于下一批；首批没有 Execute/Restore API。

~~~csharp
// 现 NativeSeekCanvasBasisCommands 的新内部只读入口。
internal NativeSeekLineRingSourceCapture CaptureLineRingSource(
    ISeekLineRingSourceProfile profile);

// 现 INativeSeekCanvasBasisAccess 仅增加一个具体 reader 工厂。
IRetainedSeekLineRingReader CreateLineRingReader(ISeekLineRingSourceProfile profile);

// 新 profile 独立于 ISeekHoldNativeProfile；仅核已冻结 metadata/body/getter。
internal interface ISeekLineRingSourceProfile : ISeekChartNativeProfile
{
    IntPtr InformationClass { get; }
    IntPtr LineControlClass { get; }
    IntPtr LineClass { get; }
    IntPtr LinePointClass { get; }
    IntPtr GroupClass { get; }
    IntPtr TransformClass { get; }
}
internal interface IRetainedSeekLineRingReader : IDisposable
{
    void VerifyOwnerThread();
    void VerifyLocal(); // managed only
    void BindLifetimeGuard(Action guard);
    void VerifySource();
    SeekLineRingSourceSnapshot ReadSnapshot();
    void RequestDispose(); // 仅撤自己的 logical admission
}
internal sealed class NativeSeekLineRingSourceCapture : IDisposable
{
    internal static NativeSeekLineRingSourceCapture Capture(
        Func<IRetainedSeekLineRingReader> create);
    internal SeekLineRingSourceSnapshot Snapshot { get; }
    internal void VerifySource();
    internal void VerifyBaseline();
    internal bool CompleteSeekPermitted => false;
    public void Dispose();
}
~~~

Snapshot 至少包含以下不可变分组，不用一个模糊的“line pose”覆盖全部事实：

| 分组 | 内容 |
|---|---|
| Provenance | 原 Canvas capture 和 Plan 的 exact 对象身份、同一 NoteView.Identity、LineControl 引用、原 CanvasControl 引用 |
| SourceGraph | 两个 LineControl 原数组身份及完整 occurrence；各 group.lines 原数组身份/完整 occurrence；每 line 的端点、group、endLine、Transform identity；每端点已知数值；完整 chart point 引用映射 |
| BaselineValues | 四个 LineControl scalar 原始 bits；group.nowLineIndex/ringActive；line/end/ring 及必要实际父链的 local/world position、local rotation/scale 原始 bits |
| Coverage/RequiredWork | declared dynamic occurrences、group segment occurrences、无 Note 段、static 段、已证明 chart joins、未解决/矛盾关联；不把 partial/missing runtime 当完成 |

SourceGraph 与 BaselineValues 分开：VerifySource 复验前者和原 Canvas/NoteView 来源；未来合法改 cache/selection/pose 不能令来源自行失效。VerifyBaseline 还核后者，供零写准备确认；两个 API 都撤销自己的旧成功后重新完整观察。NaN/-0 可原样保留为 baseline，不将其当可计算的有限目标。

## 3. 最小采集实现顺序

1. CaptureLineRingSource 先 owner-thread/未 disposed/非 busy 的 managed 准入，source capture 自己 reserve 256 公共 owner 槽，然后才调用 profile/reader 工厂；预算拒绝不得产生 native publication。
2. SharedBasisSource 增加一次性的 AcquireLineRingSource hold（一个明确 bool 标记即可），不把现 bool positions 改造成通用注册框架。reader 借原 PhysicalSource._core._view、原 roots、原 capture.Plan；不重新 new NoteView、不再次调用 RunValue 公共 Canvas factory，不递增/套触发旧构造 serial。
3. reader 的每次读取进入原 managed shared scope，caller guard 绑定本 reader/capture 的 logical lifetime；沿用 caller-withdrawal 标记，普通撤销不误 poison sibling。禁止为整次采集包 NoteView.WithRetainedSource，避免现源复验重入。
4. 确认原 NoteView identity、information.lineControl 和 LineControl.levelInformation/_canvasControl 回指同一 information/CanvasControl；source profile 的 Chart/Progress/Information/Transform 类必须与原 holder exact 一致。
5. 先完整读取 dynamicLines、lineGroups，再读取每个 group.lines，保留所有 slot/重复引用；继续读取其端点、endLineInformation 和必要 Transform closure。包括没有 Hold、没有任何 Note 的实际段；不从 HoldNotes 或 NoteView.Notes 推导全集。
6. publication 先登记根再验类/ABI/撤销；复用当前 reference-array/scalar/Transform 读取能力，fresh 核 exact metadata。新增根仍进入原去重表；逻辑 occurrence 不去重，物理根按引用去重。
7. 使用现 MaximumLines/MaximumLinePoints 等上限作明确的支持预算，另核所有 groups.lines 的总 occurrence/根/父链总量与 checked 算术。它们是本 API 的拒绝边界，不是声称 native 容量；禁止截断、只读前缀或按 Note 数限制实际段。
8. 通过原 NoteView.Notes 的 exact runtime line/start/end/group 引用建立已证明 joins，保留同 chart location 的多个实例。无 Note 的稳定段可被完整捕获，但 chart join 标为 unresolved；同引用两次观察不一致或已证明关联矛盾则拒绝，不能取第一条。
9. 两轮完整 snapshot 相等，并在前后复验原 Canvas capture/NoteView 及所有保存 descriptor/数组身份才发布。目标 time/speed 来自同一 Plan.Request；当前 LineControl._nowTime 等只记 baseline，绝不冒充目标输入。
10. readonly snapshot 的 managed 列表与 records 自有复制；不暴露 native 指针写能力。返回后允许原 RunValue 退出；backend 撤销仍拒访问，后续新构造取消不应伤已有 holder。

原 aggregate Dispose 只 drop 自己 bootstrap/原 children；新 reader hold 尚在时物理源不 free。reader logical Dispose 不撤掉 cache/position，reader 释放后也不能清除原 position EffectsOwner。保留现 terminal observer，只有全部已获准 holds 且活动 scope 退出才一次 terminal cleanup。

## 4. 纯目标与实际写入的后续最小分步

阶段 A 就是上面的 actual capture，可单独交付并离线验收；RequiredWork 清楚列未解决项，无 target-time 重建宣称。

阶段 B 新 NativeSeekLineRingTargetPlan.Capture(snapshot, originalCanvasPlan)，只复制完整输入、使用同一 CanvasPlan.Rows 的 floor/x/speed 与已捕获初始化 Ease 样本。先补静态合同，再分别实现“dynamic line 的完整采样目标”和“ring segment/selection/pose 目标”；输入域不支持时返回明确 RequiredWork 或拒绝，不能借旧 frame pose、Hold 专用公式或调用 producer 补结果。没有完整坐标/取样/helper 合同的分支不输出伪目标。

阶段 C 才新增 NativeSeekLineRingCommands：先绑定原 source capture 与完整 baseline、检查所有 aliases，先同步必要 LineControl scalar，再按已证明依赖派发具体 line buffer/selection/ring pose；完整 source/value 读回。它不是 value participant。SetPositions/Appear/Disappear/color 不得整方法调用替代显式 writer；未闭合语义对应 writer 保持缺席。

每个实际 writer 保存原公共 commands owner、逻辑 occurrence → 物理对象映射、expected、pending attempt、final native admission 和读回；同一物理属性同目标合并、冲突零写拒绝，但有序依赖命令不能无条件去重。partial native 仍归最初 owner；只有精确 before/target pending 可恢复，第三态永久 sticky，不覆盖未进入槽外改。Transform/renderer/activation/颜色方法进入即外部 effects 未闭合，值原位不清 effects/root；unknown free 保原 publication/source owner，不重试，aggregate 不替换它。

## 5. 首批改动分工与失败归属

| 位置 | 首批必要修改 |
|---|---|
| 新 Gameplay/NativeSeekLineRingSourceCapture.cs | immutable snapshot/coverage、先 reserve、双样本、VerifySource/VerifyBaseline、原公共 cleanup owner |
| 新 Gameplay/NativeSeekLineRingSourceProfile.cs | 从既有 exactFields/body/getter pins 组合独立只读 profile；不构造 Hold profile、不要求 HoldControl 实例 |
| 新 Judgements/NativeJudgementBackend.LineRingSource.cs（按需 Read 分片） | 真实 control arrays/segment/point/Transform reader，使用原 CanvasSource roots/NoteView |
| 现 CanvasBasisCommands 和 CanvasSource.Basis | 一个 reader 工厂/hold及受控捕获入口；保留原 gate、两个 child journal、terminal observer、typed caller withdrawal |
| 新 tests/LineRingSource 与 Program selector | 下表 16 组真实 backend 测试；不改旧测试含义、不用 mock reader 当实际 backend |

创建 reader 前失败且已释放：退还新 managed owner；source/reader 半构造及临时 getter/free 未知：异常保存 SeekLineRingSourceCleanupOwner + 原 NativePublication/Canvas owner + typed failure，未返回对象也可找回。未知 cleanup poison 同一个 holder，禁止 sibling 继续 native 或末端重试；普通 caller cancellation 只关本 caller。source 过期允许已知根一次清理，不谎报为未知 free。所有 source-only journal 的 AttemptedWrites 为 0，原 Canvas/Position effects 保持原 owner，CompleteSeekPermitted=false。

## 6. 首批 16 组离线 TDD

均从真实 backend 的 Canvas basis 再进 CaptureLineRingSource；用官方导出 fixture，不替换 source/access。此处只是待实现场景，不是现已通过结果。

| # | 组 | 核心断言 |
|---|---|---|
| 1 | 同源 provenance | 同一 Canvas capture/Plan/NoteView，未另建来源或重复数值 Range 求值 |
| 2 | 无 Hold 完整覆盖 | 只有普通 Note 或零 Note，仍捕获 dynamic/group 全段，零 producer/setter |
| 3 | 空完整图 | 空 control arrays/group.lines 正确保留；无 null 假目标或“完整环境”宣称 |
| 4 | occurrence/aliases | 重复 group/line/point/Transform 保逻辑槽，物理根去重，矛盾重复样本拒绝 |
| 5 | chart/runtime joins | 多实例及已证明 segment 映射保留；无 Note join 明确 unresolved、不 ordinal 猜测 |
| 6 | static/非 dynamic 段 | group.lines 中额外 static 段保留并标覆盖，不因不在 dynamicLines 而漏掉 |
| 7 | exact class/descriptor | 错类、字段类型/static/offset、热缓存后 ABI 漂移拒绝，零写 |
| 8 | 数组/容器完整性 | rank/element/长度/总预算、替换原引用、采集中 Count/slot 变化拒绝，不读前缀 |
| 9 | point/source 漂移 | time/x/floor/ease/canvasIndex 或端点/group 回指变更使 Source sticky 失效 |
| 10 | baseline 分离 | scalar/selection/pose 漂移使 VerifyBaseline 失败；不误作 SourceGraph identity 改变 |
| 11 | 原 Canvas 来源漂移 | move/List/Ease/静态向量或 NoteView 变化拒绝；不得 recapture 接受新来源 |
| 12 | publication/callback 撤销 | root先登记；字段/array/getter回调撤销后不再 native；sibling 不被普通撤销误 poison |
| 13 | worker/shared reentry | reader、Canvas cache/position、common sweep 互重入与 worker 在 native 前拒绝 |
| 14 | independent Dispose | Run退出仍可用；先释放 reader/aggregate 任一顺序不提前 free sibling/原 effects 根；双 Dispose 单次 |
| 15 | 旧证明/不可变历史 | 新复验失败先撤旧成功，所有 occurrence/bitwise baseline 历史不可改，原 position owner identity不变 |
| 16 | 预算及 unknown cleanup | reserve满时零采集；构造/临时box或receiver/末端free未知均保精确新公共owner及原source/publication，禁止重试 |

阶段 B/C 另建数值边界、取样顺序、条件 writer、pending/第三态及 engine effects 的新测试，不把这 16 个 source 测试计作它们已覆盖。当前仍保持真实 Speed/Seek 关闭；本次只新增本文档。
