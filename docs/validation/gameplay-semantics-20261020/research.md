# B11 判定与时间语义的只读研究

完整 B11 应提供真实 run 归属、真实音符对象的局内稳定 ID、请求判定类型和按来源区分的时间语义。只增加 `NoteId=null` 的 DTO 是中间步骤，不能作为本项完成。固定构建的 12 个判定调用来源已定位：4 个可用含 Note 的上游方法同步配对；其余 8 个只有 Update 局部，需要原生调用上下文桥才能完整覆盖。原始 float 中存在固定 -1、-2、1，不能统一当打击偏差。时钟 `nowTime` 是 BPM 映射坐标，不能与 `nowRealTime/nowSyncedTime` 一并标为秒。

本任务授权仅为本地静态研究，不改生产代码、测试，不构建或操作游戏，不新增工具。样本为本机 GameAssembly，SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；读取既有 dump。IDA 不可用，使用已有 Python/pefile/Capstone。报告类型为普通逆向，`flavor = null`。动态单位、音符关联和新 DTO 仍未验证。

## Evidence 与复现

```powershell
C:\Python314\python.exe docs\validation\gameplay-semantics-20261020\inspect-semantics.py
```

已实际执行通过：固定样本 SHA、PE 导入/导出锚点、13 个方法、12 个直接判定调用点、分支表及关键指令/浮点常量断言。完整指令、源码哈希、常量与公式见 [static-proof.json](static-proof.json)。本次 JSON SHA 为 `3c444ed13f998e28426c8fe8faddb468f5b683ec69d42be15c818088b5b4ca9b`；重跑会更新观察时间和源码快照。

补全音符身份复现：`C:\Python314\python.exe docs\validation\gameplay-semantics-20261020\inspect-note-identity.py`。已执行通过，独立解码 9 个方法（含 3 个引用类型 List 操作的机器语义）、校验 4 个 metadata 方法、12 个实际直接调用点及谱面对象引用赋值。另扫描全部可执行 PE section 的 E8 作为发现检查，命中与已从方法起点正确解码的 12 点完全相同；这不等于完整证明不存在间接调用或未来插件自建调用。证据见 [note-identity-proof.json](note-identity-proof.json)，本次 SHA `c8f6f4d6ea40ad32408a8875d70c99cc635a31e0743bcb857bd66bc9d7c45c11`。`runtimeIdentityVerified=false`，未操作游戏或构建。

| Evidence | 原始观察 | 结论边界 |
|---|---|---|
| E-ABI | NormalMode.GetJudge `0x3C0321/32B/32E` 保存 EDX/RCX/XMM2；metadata 为 instance void(enum32, Single) | 精确 ABI 为 `void(receiver, enum32, float32, MethodInfo*)`，没有 Note 参数 |
| E-Forward | GetJudgeControl.GetJudge `0x3E8030` 检查自己的 LevelInformation.levelOver 后，经委托转发相同 int/float；InLevelController `<Awake>b__12_0@0x371D60` 再虚派发 | 不可从这个中转入口凭空还原 Note；“入口发生”不等于计分采纳 |
| E-Kinds | GetJudge 跳表 `0x3C0B38`，case 地址与实际 `_bad/_miss/_perfect/_early/_late/_maxPerfect` 写入 | enum 数值可由 switch 与字段语义交叉恢复；dump 中枚举常量全显示 0x0，不能直接用这些伪常量 |
| E-Delta | NoteJudge.Update `0x3FDE9B..DEA4` 从 LevelInformation.nowSyncedTime 复制 `_nowTime`；`0x3FF295..2A3` 传 `_nowTime - NoteInformation.realTime` | 这些明确的打击路径为实际时间差，正号为晚、负号为早；尚不代表全部调用 |
| E-Sentinel | CheckHold.MoveNext `0x40337D` 传 -2f；`0x402CCC` 传 1f；NoteJudge.Update `0x3FE03C` 也为 Miss 路径准备 1f；GetJudge 将 -2 特判，其他偏差显示乘 1000 | -2 是非测量哨兵；Miss 的固定 1 不是“晚了 1000 ms”；不能对每条 RawDelta 统一给毫秒 |
| E-NotIdentity | NoteInformation.SendRendererEvent `0x3FCF7D` 将 `_index` 写 0，`0x3FCFD8..FDD` 在 renderer 数组循环递增 | `_index` 是 renderer 遍历游标，不能作 NoteId |
| E-Clock | Progress.Update `0x401272/1296` 将相同内部 double 转 float 写 nowRealTime/nowSyncedTime，`0x4012A7/12B5` 经映射写 nowTime | 三个字段有不同坐标语义，不是三份独立时钟源 |
| E-Map | `RealTimeToChartTime@0x400C80`、逆函数 `ChartTimeToRealTime@0x400400`，常量 60；LevelControl.Start 用逆函数计算 NoteInformation.realTime/holdEndRealTime | chart 坐标与 real 时间之间为 BPM 及 bpmShift 的分段映射 |
| E-Source | MUGMusicPlayer.GetTime 结合自有 DSP service 与 AudioSource.time；Progress.Update 播放时减 `_offset`，结束后可继续加 DeltaTime | 不能把当前 Unity DSP getter 值直接当局时间；也不能承诺时间已经过完整延迟校准 |

## 判定类型和偏差的准确范围

当前 SDK `JudgeHookObservation(Invocation, JudgeType, DeltaTime)` 是低层精确入口观察。`GameplayJudgementObservation` 仅是保留 DTO，没有服务实际发布它。现有底层 hook 不携带 run、音符、单位或“原函数是否实际采纳”的承诺。

由跳表与计数字段恢复的 raw 值如下。语义名称限定本固定构建，未知 raw 值仍保留原值并标 Unknown；不能直接把这个表当作跨版本稳定游戏枚举。

| Raw | 请求语义 | 跳表目标 / 主要计数 |
|---|---|---|
| -1 | Bad | `0x3C0514`，`_bad++` |
| 0 | Miss | `0x3C062E`，`_miss++` |
| 1 | Perfect | `0x3C0526`，`_perfect++` |
| 2 | RizTimeEarlyPerfect | `0x3C05DD`，perfect、early、earlyIngame 增加 |
| 3 | RizTimeLatePerfect | `0x3C060E`，perfect、late、lateIngame 增加 |
| 4 | RizTimeMaxPerfect | `0x3C0626`，`_maxPerfect++` |
| 5 | RizTimeMiss | `0x3C062E`，miss 与 challengeTimeMiss 增加 |

原函数有 `_noBad && raw==-1` 直接返回的分支，也有 `_crashed && lifeBarMode==2` 的特殊 Miss 记录逻辑。因此 DTO 应称 `RawJudgeType` / `RequestedKind`；单靠回调参数不能宣称它是最终计分结果。若需最终实际计数，复制严格同一 invocation 的 before/after 计数字段差异，单独报告原函数正常返回与哪些字段改变，仍不从未改变的字段推断“没有副作用”。

明确的 tap/hold 头与自动 predicate 共 4 个调用点把 `nowSyncedTime - note.realTime` 传入，偏差显示乘 1000，支持其 real 时间尺度按秒解释；另 8 点传固定值，具体见下表。推荐：

- 必留 `RawDelta` 与 `RawDeltaBits`，保留原始 float，不擅自校准、截断或把异常值静默改零。
- 区分 `DeltaSemantics = Unknown | TimingDifference | Sentinel`；GetJudge 会特殊处理 raw delta=-2，但仅凭数值不能证明每次偏差的源点。必须按已验证的调用来源给 TimingDifference/Sentinel，Miss/RizTimeMiss 不提供有效 TimingOffset。
- 未证明来源分支时 `TimingOffsetSeconds=null`，`DeltaUnit=Unknown`，附具体原因；不能仅因 raw 在某个范围内就声称 measured timing。
- 只在后续精确验证了来源分支、非哨兵及有限值后，才能给 `TimingOffsetSeconds`。毫秒是展示层乘 1000 的派生值，不改变保存的原始值。
- 不把 AutoPlay 标志推成“这条判定由真人/自动触发”的确切因果。可复制当时的只读模式快照，称 observed flag。

## 完整音符身份：引用来源与 12 点覆盖

原数据 `InLevel.Note` 仅有 type、time、floorPosition、otherInformations；`NoteInformation` 也没有内建稳定音符 ID。它的 `_index@0x68` 是 renderer 循环游标。按“最近的 note.realTime”“最后渲染的 Note”或“唯一剩余音符”猜测会在同时键、重复时刻、Hold 追加判定和 Miss 中错误配对，不能采用。

`LevelControl.Start@0x3EA580` 已证明实际图谱引用链：`0x3EB077..092` 从 `LevelInformation.chart.lines` 取当前 Line 存在 stack+0x20；`0x3EBF93..FC7` 从同一 Line.notes 取 Note 到 R15；新建的 NoteInformation 加入 NoteJudgeControl.notes 后，`0x3EC1A0` 直接写 `NoteInformation.noteData=R15`。因此可以用对象身份匹配实际 chart graph，而非按 time/type 相等匹配。LineInformation 是渲染线段，一个 chart Line 可以分成多个段；不能把 LineInformation 的某个数组下标直接称为 chart Line 下标。

必须区分两个名字：NoteJudgeControl 持有自己的 `levelInformation@0x20`；该 information 的 `getJudge@0x60` 才是 `InLevel.GetJudgeControl`。后者仅有 `levelInformation@0x20`，没有 `_noteInformation/currentNote`。它的 `GetJudge@0x3E8030` 是 `void(this RCX, int32 EDX, float32 XMM2, MethodInfo* R9)`，检查 information.levelOver 后转发原 int/float；inner NormalMode.GetJudge 也不带 Note。`script.json` 的签名省略隐式 this，不能据此把实例方法当静态方法。

下面“Note 来源”表示 **GetJudgeControl 原生入口尚未运行、调用者寄存器仍完好时** 的来源，不能在 CLR delegate 已进入后直接读寄存器。owner 为相应 NoteJudgeControl。call 是 E8 地址，唯一已验证返回 RVA 为 call+5。

| Call RVA | 请求来源 | 精确 Note 来源 / owner | Delta |
|---|---|---|---|
| 3FD72B | Auto predicate 普通 | RBX / RDI | `_nowTime-realTime` |
| 3FD75F | Auto predicate challenge max | RBX / RDI | `_nowTime-realTime` |
| 3FE186 | tap/hold 超时 | owner RBX 的 tap list@40，当前 EDI | 固定 1 |
| 3FE265 | tap/hold 超时 | owner RBX 的 tap list@40，当前 EDI | 固定 1 |
| 3FE373 | drag 超时 | owner RBX 的 drag list@48，当前 EDI | 固定 -2 |
| 3FEA08 | tap/hold 头 max | R14 / RBX | `_nowTime-realTime` |
| 3FEF8A | Bad 请求分支 | R14 / RBX | 固定 -1 |
| 3FF2A3 | tap/hold 头 | R14 / RBX | `_nowTime-realTime` |
| 3FF569 | drag challenge 成功 | owner RBX 的 drag list@48，当前 EDI | 固定 -2 |
| 3FF73C | drag 普通成功 | owner RBX 的 drag list@48，当前 EDI | 固定 -2 |
| 402CD7 | hold 持续期间 Miss | coroutine RSI 的 field@28；owner 为 field@20=RDI | 固定 1 |
| 403388 | hold 完成 | coroutine RSI 的 field@28；owner 为 field@20=RDI | 固定 -2 |

drag 的 XMM6 在循环初始、输入检查后、成功循环尾均重置为 -2（`3FF327/3FF4F4/3FF9FD`）；中间 FX 计算会暂时复用它，不能只看附近一条 mov 就误认该参数为位置或颜色。Bad 的 XMM13 在 `3FE490` 装入 -1，直到方法返回没有再次写入。全部浮点常量由脚本读取实际 RIP-relative 数据并断言；drag Miss 是 -2，区别于 tap/hold Miss 的 1。

### 可用配对的 typed retained 路径

- `NoteJudgeControl.<Update>b__29_0@0x3FD410`：instance `bool8 AL(this RCX, NoteInformation* RDX, MethodInfo* R8)`。prologue 把 RDX 保存到 RBX，RCX 到 RDI；方法内有两个直接 GetJudge。Update 只在 AutoPlay 分支构造此 predicate，`3FF320` 交给 List.RemoveAll；普通手动输入没有走这个 predicate。
- `NoteJudgeControl.CheckHold@0x3FD010`：instance `reference RAX(this RCX, NoteInformation* RDX, bool8 R8B, MethodInfo* R9)`。只分配迭代器并写 owner@20、holdNoteInformation@28、isPressLeft@30；此工厂方法没有判定。不能把工厂调用期间的 thread-local context 留到未来帧。
- `<CheckHold>d__32.MoveNext@0x402670`：instance `bool8 AL(this RCX, MethodInfo* RDX)`。每次进入从 receiver@20 取 owner，从 receiver@28 取 hold Note；原调用期间包含另外两个直接 GetJudge。应每次 MoveNext 独立 retain/压栈/原调用/finally 退出，不能要求 MoveNext 返回 true 才承认内部判定，最后一次完成可能返回 false。

因此 typed retained 同步上下文能准确覆盖 4/12 点。需要给 bool8 返回与带 Note 参数的入口单独验证 ABI，并保留原返回值；现有 InstanceVoid retained API 不接受它们。Before 创建包含 roots 的 IDisposable context，inner NormalMode.GetJudge 在原生同步调用栈内读取匹配的 context；finally 退出，即使 native/observer 抛错、停用或无判定都释放。仅在 after 回调清理会泄漏，按“最近一次 predicate”关联则会串音符。

### 最小完整实现架构

若要固定构建全 12 点覆盖，建议增加一个 **内部 GetJudgeControl 原生入口上下文桥**，加上现有 inner NormalMode.GetJudge 语义观察。桥必须在任何 C++/CLR prologue 改写寄存器之前保存入口 return address、调用者通用寄存器、XMM 和参数；将 return RVA 精确匹配上表，按已知规则取 Note 和 owner，再创建受控调用范围。不要求插件接触这些原始值。也可逐点实现已验证的 callsite bridge；当前只提供 DobbyHook/DobbyDestroy 的 managed broker 本身不具备这项能力。

未知返回点、RVA/字节不符、非主线程、来源 owner/info 不符、当前 list 索引越界、类型/字段解析失败或 graph 超限时，该条语义观察拒绝关联；原调用仍原样执行一次。保留既有 raw hook，单独撤下完整 Note identity capability，不能从错误上下文补默认值。具体桥还必须处理 Windows x64 stack alignment、shadow space、非易失寄存器、bool/float ABI、可展开的异常路径及停用中的 finally；这些是下一步工程与实测任务，本报告没有证明已有桥可用。

关联检查是 `source owner.levelInformation == GetJudgeControl.levelInformation == inner NormalController.LevelInformation == 当前真实 open run.info`，并匹配同一 controller/session/run/scene。inner Normal.GetJudge 自身发生才发布对应判定观察；outer 被 levelOver guard 拒绝时不虚构 inner 判定。每个 inner native Invocation 独立记录，不能假定一个 delegate 永远只调用一次。scope 栈顶不匹配时不得回退搜索最近旧 Note；嵌套、reentry、模块停用必须有明确的隔离或原样直通策略。

### 局内稳定 NoteId 与根生命周期

没有原生永久 Note ID，需要明确发布的是 **RuntimeNoteId**：`SessionId + ObservedRunId + NoteSequence`。NoteSequence 为本 run 内部单调分配，键是确实捕获并 GC-root 住的 NoteInformation 对象身份；同一个物理 Note 的头、持续 Miss、尾判定复用该 ID，两个 time/type 完全相同但对象不同的 Note 得到不同 ID。不得使用 `_index`、GetInstanceID 或裸地址作为 public ID；也不承诺不同 run 或不同加载顺序下序号相同。

registry 可在已验证 LevelControl.Start 正常返回后读取实际 NoteJudge.notes 建表，或对精确来源首次遇到的 NoteInformation 懒分配。无论哪种，都必须限制单局条数、使用可验证的 RootHandle，而非只把 IntPtr 放 Dictionary 以免地址回收复用。graph path 为可选说明：把 rooted noteData 与当时实际 `chart.lines[i].notes[j]` 按引用匹配，唯一时给 `ChartLineOrdinal/ChartNoteOrdinal`；重复对象别名命中多个位置时该 path 必须缺失/标 Ambiguous，但 RuntimeNoteId 仍可准确区分实际 NoteInformation。新插入的真实运行时 Note 可有 RuntimeNoteId，不能凭 type/time 假造 chart path。

registry 持有本 run 的 chart/info 与已登记 NoteInformation roots；单次 native invocation 另持短租约，确保 run 在原调用内刚好结束时，末次判定仍归属 before 捕获的旧 RunId。run 结束、scene/info/controller 替换、模块停用先禁止新捕获并让 registry 退役，再在最后一个在途租约 finally 释放所有 roots。旧 scope 不能取得下一次重试 registry；普通 lifecycle revision 推进不改变 NoteId。若某条读取失败，不保留半个 entry。达到资源上限应撤销完整覆盖能力并报告原因，不无限根住对象。

该方案解决 NoteId 的完整设计，不等于已经实现或运行验证。现有生产代码未接入上面的上下文桥、registry 或新版 DTO，因此 B11 当前仍不能标完成。

## 时钟计算

ProgressControl 的内部 `_nowTime` 为 double。播放且 timeReady 时，Update 采用 `MusicPlayer.GetTime() - _offset`；若 information.levelOver 已 true，则它可按 DeltaTime 继续增加。随后：

```text
nowRealTime   = float(_nowTime)
nowSyncedTime = float(_nowTime)
nowTime       = RealTimeToChartTime(_nowTime)
```

这说明该函数在此构建写入的 nowRealTime/nowSyncedTime 相同，不能推广成跨版本永远相同，也未对整个二进制所有写入者作完整证明。读取当帧字段应称同步复制快照，不宣称是三个独立计时源。

空 bpmShifts 时，映射为 `chartCoordinate = realTime * chart.bPM / 60`，逆函数为 `realTime = chartCoordinate * 60 / chart.bPM`。有 shifts 时，按 key.floorPosition 选择当前 real 区间，公式为：

```text
chartCoordinate = key.time + (realTime - key.floorPosition) * key.value * chart.bPM / 60
realTime = key.floorPosition + (chartCoordinate - key.time) * 60 / key.value / chart.bPM
```

图谱中的 time 坐标表现为 base BPM 的拍坐标，仍建议 SDK 保留“chart coordinate / BPM 映射坐标”的说明，直到对真实常速/BPM 变化谱验证准确范围；不要草率复用只含 Seconds 的枚举。`Chart.get_ChartDelay()` 另由 chartDelayMs / 1000 得到，但所研究的 RealTimeToChartTime 公式不直接加这个字段；不要再人为加一次“谱面延迟”。

当前 `GameplayClockSnapshot.SecondsMappingVerified=false` 是合理限制。可先在新时钟 contract 说明原始坐标来源与映射依赖，而不直接升级为全字段 Seconds。Unity DSP 秒与游戏自有 DspTimeService 的平滑/修正来源也不相同，绝不能靠一次 DSP 差值冒称采样精确对齐、输入延迟或音频延迟。

## 建议的新版本只读 DTO

保持旧 `game.normal.judge@1.0` 原构造和程序集身份，另声明新 semantic point，例如 `game.normal.judgement@1.0` 及独立可选 capability。第一版能诚实提供：

| 字段 | 可提供范围 |
|---|---|
| schema/point version、build fingerprint | 绑定声明与固定构建 |
| native Invocation、phase、source | 直接 machine-entry，Before/正常 After 明确区分 |
| raw enum、raw float、float bits | 原样复制，不改变原函数参数 |
| requested kind / known mapping | 固定构建表，未知保留 raw |
| SessionId、ObservedRunId、SceneGeneration、captured lifecycle Revision | 仅在 receiver.LevelInformation 与当前已观察开始的同一 info 严格匹配时提供；缺失则明确未关联，不事后按 chart 补造 |
| host monotonic capture time | 原生回调发生时普通数值快照；不是音符预期时刻 |
| game raw nowTime/real/synced + availability/source | 主线程读取同一 rooted info，逐字段失败隔离；不调三次全局扫描 |
| RuntimeNoteId / identity source | 完整实现后按上面的精确 NoteInformation 来源与局内 registry 提供；现有代码尚未实现，null DTO 只算中间产物 |
| ChartLineOrdinal / ChartNoteOrdinal | 实际 graph 按引用唯一命中才提供，重名值/共享对象别名不猜配对 |
| Delta semantics / optional timing seconds | 当前入口不能证明 measured timing 时 null/Unknown；-2 哨兵明确标识 |
| NativeReturned / observed counter changes（可选 After） | 原函数正常返回的事实与复制的字段变化，区别于“最终正确判定” |

关联应在原调用前根住 receiver/info，捕获同局身份，然后在原调用正常返回后复制 DTO，所有根由内部 finally 逐项释放。匹配允许同一局正常 revision 推进，但不允许 scene/run/info 被替换；若原生判定导致最后一次失血/终止，仍可归属先前捕获的真实旧 RunId，而不要求 after 仍然存在“可取消”的 token。不要把 cancellation token API 当通用 judgement token，因为其 `_over` 条件会排除刚好结束的最后判定。

参数承载 ABI（enum32+float）目前普通 Bind 没有 per-invocation retained observation 支持，新语义实现前需要增加同等内部 finally 保留机制，不能在普通 Before 根住后仅靠 After 清理：原函数异常时 After 不会运行。SDK 回调只拿普通不可变 DTO，没有原始对象、修改参数或控制 original 的权限。

## 最少验证路径

1. 先以中间产物检查 run 关联与 raw DTO：正常游玩一次含普通成功/Bad/Miss，检查同一次 native invocation 的 Before/After、原调用一次、已关联 RunId；初始化在局中、未观察开始时保持未关联。这一步不能宣称完整 B11。
2. 静态 mapping 对照实际结果计数与 hook 原参数；专门覆盖 noBad、lifeBarMode 特殊失败分支、自动判定、Hold 结束 -2 与 Miss 1，不能用只测 tap 的范围升级所有 delta 单位。
3. 同 run 的时钟 raw 快照与 host monotonic/dsp 连续采样，覆盖播放、暂停、恢复倒计时、BPM 变化、offset 与正常结束；用真实 chart 数据验证上述分段双向映射，不写入速度/定位字段。
4. 新同谱面 run 与旧迟到回调、scene 变化、模块停用、原函数抛错/读取失败时，不串 RunId、不泄漏根、不伪造 After/NoteId。
5. 实现并验证上述上下文桥与 registry 后，才开放完整 Note identity capability。真实测试需覆盖上表 12 点、同时键/同 time 重复对象、长按头尾同 ID、自动/手动拖键、Bad、Miss、同谱面 Retry 新 RunId，以及原函数异常/guard no-op/逻辑停用中的租约释放。stub 测试可证明托管清理和匹配规则，不能代替桥的实际 ABI、调用点和每种分支实测。无法触发的分支保留明确未验证状态。
