# Judge 初始化显式写入：2026-11-11

**本批构建／行为回归已通过；独立源码复核尚待真实报告，不签整批闭合或完整 Seek。**

`RunValue.PrepareSeekJudgeInitialization` 独立拥有实际 ready-paused Run／完整 NoteView 与 Judge 来源，接受已有不可变 `InitializationTargets`。pending 时显式写四个 Judge Single 缓存、每个实际音符的挑战 Boolean、LevelInformation 的 signed 挑战计数，最后发布 ready。已经 initialized 只允许与原状态完全相同的目标，零 setter，不重新初始化。

## 实际验收

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 六组件正常构建 | 退出0，0警告、0错误 | [1-six.log](1-six.log) |
| 判定／初始化 writer console 回归 | 216/216，新增16组 | [4-judgements.log](4-judgements.log) |
| Runtime ABI 回归 | 51/51 | [5-renderer-abi.log](5-renderer-abi.log) |
| 输入与依赖 | 699项前后相同，16份DLL/PDB副本匹配本次六图 | [build-test-result.json](build-test-result.json) |
| 测试构建 | 判定W0/E0；Runtime一条既有夹具CS0649/E0 | [2-judgements-build.log](2-judgements-build.log)、[3-runtime-build.log](3-runtime-build.log) |
| 独立源码复核 | 待实际报告 | [source-review-status.json](source-review-status.json) |

准备只读；目标必须有界、完整覆盖且每个实际引用恰好一次，不能靠Count正确但枚举额外外来项的集合绕过。目标窗口finite；原窗口NaN payload／负零和signed count按原始位恢复。最终字段guard在metadata／receiver解析之后，guard与setter之间不插getter。

记录所有实际forward/inverse分派及partial-native before／after状态。观察第三状态保持sticky，不覆写unattempted外改或伪造恢复。inverse先撤ready；如果撤ready失败且实际仍true，则不再恢复其他缓存／flags／count。每次恢复复验先撤销旧成功；getter失败不能保留陈旧确认。callback撤销关闭逻辑访问，已进入或未知free保留精确公共owner，native free与inverse均不自动无限重试。

## 失败历史与边界

- [RED](historical-red.log) 明确缺少实际participant；实现后才运行行为测试。
- [初轮214/216](historical-214-of-216.log) 保留原失败：标量字段没有创建新root，guard夹具用错root回调；另一项故意改source后未还原夹具的外部数据。修为真实WriteExactCore receiver解析时的hook，并只还原测试故意改的外部引用，原零setter／不改无关状态断言保持。
- [修guard时类型错误](historical-guard-type-error.log) 保留：DuringHandleTarget参数为对象地址而非handle ID；后续修正后才接受绿色。
- 新增ready before／after native失败、ready逆操作before／after失败、恢复成功后的getter复验失败及恶意枚举／错offset测试，均归于最终216例范围。

这只确认显式字段写入／读回及条件恢复，不调用Judge.Update、renderer、输入或协程；阶段观察不是原子CAS，也不排除外部ABA。realTime／holdEndRealTime缓存重建、缺失runtime实例、目标canvas／line／ring、renderer延后consumer、Hold engine退出、完整时钟事务仍未完成。未安装/启动游戏，未改代理／账号／旧ZIP，未提交推送。

本批绝对命令、退出码和哈希见[build-test-result](build-test-result.json)；源码变动见[changes.patch](changes.patch)。后续源码变化必须新图验收，不能借这批结果或旧ZIP签完成。
