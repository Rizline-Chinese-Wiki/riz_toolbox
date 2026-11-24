# 下一批：实际音符 game-seconds 缓存写入

当前 RuntimeTimeTargets 已完成只读转换目标和4组回归；真实字段writer尚未实现。注意几何的head/tail必须使用raw chart坐标，与本批cache game-seconds不同。

## 已核实来源
- LevelControl.Start 在0x3ecc37–0x3ecc53把raw note.time传给ChartTimeToRealTime再写NoteInformation.realTime(0x34)；Hold尾在0x3ecd76–0x3ecda1同样转换后写holdEndRealTime(0x38)。
- NativeSeekChartPlan.Convert调用正式ChartTimeToGameSeconds并拒绝nonfinite；已生成StartGameSeconds/HoldEndGameSeconds。
- RuntimeTimeTargets按actual.Location映射完整ChartPlan，保留所有实际实例和partial coverage，不创造缺失引用。
- ordinary note的target tail=null是“不写未用尾缓存”，不是赋0。Hold必须提供finite tail，normal不能带任意tail。

## 推荐实现
新增独立NativeSeekNoteTimeParticipant与NativeJudgementBackend.NoteTime access，复用已修正的初始化writer所有权/entry confirmation/conditional raw-bit restoration模式，但不改旧初始化行为、不抽出大规模泛型框架。

Source独立拥有完整NoteView/ready-paused/run，构造时按实际chart raw type绑定每个note.realTime，以及只为Hold绑定holdEndRealTime，精确System.Single/instance/mutable/offset。原baseline可为NaN payload/-0；target需finite。完成完整、有界、恰好一次显式target覆盖与tail/type一致性后，按绑定顺序通过正式WriteExactGuarded，纯managed final guard后不插getter；全图阶段校验和selected-slot条件读回分离。

复用pending before/after-store归属、sticky第三态、无未尝试字段覆写、inverse一次性、每次确认在Operation前撤旧成功、callback撤销/源失效/unknownfree精确owner与不重试。不同于初始化writer，本批没有ready发布步骤，也不能操作flags/count/Transform/renderer/Judge队列/设备输入/协程。

先写真实backend/RunValue+FakeNative export测试，再RED、GREEN、独立小范围复核、新冻结六组件和判定/51ABI。覆盖多实例、partial/空actual、普通尾NaN不写、Hold尾必需、finite目标/原raw bits、missing/duplicate/foreign/超界枚举、before/after/逆操作异常、第三态/未尝试外改、confirmation entry/getter失效、source/metadata/thread/guard/reentry/dispose、unknownfree责任。明确保留time-domain修正的raw2→8/cache1→4测试。

完整Seek、missingruntime创建、target canvas/line/ring、renderer delayed closure、Hold engine exit仍不由这个writer授权。不要更改代理/账号、启动安装游戏、旧ZIP或Git提交推送。
