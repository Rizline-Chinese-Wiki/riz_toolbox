# 下一批：实际 Judge 初始化字段写入

本合同接在已实现的 InitializationTargets DTO 之后。目标生成已经4新增/200判定通过，但没有任何原生字段写入，不能据此移除现有 JudgeInitialization / ChallengeFlagsAndCount RequiredWork。

## 已知来源与目标
- 复用实际 RunValue、独立 NativeSeekNoteFlagsAccess 的完整 NoteView/ready-paused/source 归属及 ISeekTargetNativeProfile。
- Judge 四个 Single 缓存：_judgeTime(0x54)、_maxPerfectJudgeTimeInChallengeTime(0x58)、_normalPerfectJudgeTimeInChallengeTime(0x5c)、badTime(0x60)。
- 逐实际 NoteInformation 的 inChallengeTime(Boolean，0x56)，完整目标引用恰好一次；多实际实例与同谱面数据别名不能合并。
- LevelInformation.challengeTimeComboNum(Int32，0x94)。保留有符号原始基线。
- NoteJudgeControl._instantiateReady(Boolean，0x6c) 是最后发布的 ready 边界，不提前写 true；旧已初始化目标不得被当成“再次原生初始化”强行覆写。
- NativeSeekTargetStatePlan.InitializationTargets.Windows/ChallengeComboCount/Notes 已来自现有精确 OR + 当前初始化 List occurrence 投影；这里只写显式完整目标，不重新猜默认值或以allNotes替代初始化List。

## 实施与验收要求
新增内部 PrepareSeekJudgeInitialization(profile, targets) 与 actual access/participant；使用正式 WriteExactGuarded，纯 managed guard 后不加getter。Prepare只读、完整source独立保根；Commit分期核全图，每字段精确 receiver/type/offset/expectedCurrent 与位级读回；四缓存、flags、count完成后才发布ready。不调用Judge.Update、renderer、设备输入或协程。

先写TDD测试，再记录真实RED；实现GREEN。基线float原始位包括-0/NaN payload，目标windows须finite。条件恢复原存储、不覆写第三状态，观察过的unknown保持sticky；ready回滚顺序不能让true提前暴露未恢复cache/flags/count。callback撤销和sameRunValue/participant重入都须零后续分派；native entered/未知free保留精确公共owner，不自动重试。全图检查不能每标量重扫N音符导致N²扩张。

真实测试覆盖pending/initialized区别、完整覆盖缺/重复/foreign引用、重复occurrence和区间OR计数、floatbits、partialnative前/后失败、失败后ready不误签、第三状态及unattempted改变、条件恢复、worker/finalguard、撤销/重入、unknownfree与source变化。使用正式backend/FakeNative export fixture，不加fake-only产品路径。独立目录正常六组件图+判定/Runtime ABI测试，保存真实退出码与输入/PDB依赖。始终保持完整Seek关闭。

realTime/holdEndRealTime缓存重建、缺失runtime实例创建、目标canvas/line/ring几何、renderer consumer/source closure与Hold engine exit仍是后续独立功能，不靠这6类字段写入代替。
