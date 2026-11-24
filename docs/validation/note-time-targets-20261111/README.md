# 时间域修正与只读缓存目标：2026-11-11

**本批仅修正Hold几何的谱面边界并生成不可变缓存目标。实际音符时间cache writer尚未实现，完整Seek保持关闭。**

## 两种时间域必须分开

- HoldControl原生头尾比较使用raw `Note.time`／`otherInformations[0]`，不是NoteInformation转换缓存。几何读取现与该来源一致，非等价转换回归为raw头2/尾8、cache头1/尾4，保留head等号进入／tail严格大于结束。
- `NativeSeekTargetStatePlan.RuntimeTimeTargets` 使用完整ChartPlan正式转换的 `StartGameSeconds`／`HoldEndGameSeconds`，按每个actual.Location映射，保留所有实际实例与partial coverage。
- ordinary note tail=null表示“不写未用尾cache”，不是写0；Hold必须由正式转换提供尾game-seconds。目标生成不写原生cache、flags、count或Transform，不删除未完成RequiredWork。

固定构建来源证据见[geometry-time-domain-evidence](geometry-time-domain-evidence.json)；单行源修正和边界回归的[微型独立复核](geometry-time-domain-review.json)完成，没有已确认问题。该范围不等于完整几何／实机复核。

## 真实执行结果

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 新六组件构建 | 退出0、W0/E0 | [1-six.log](1-six.log) |
| 判定console回归 | 220/220，新增4个时间目标组 | [4-judgements.log](4-judgements.log) |
| Runtime ABI回归 | 51/51 | [5-renderer-abi.log](5-renderer-abi.log) |
| 输入及依赖 | 700项前后不变，16份DLL/PDB副本匹配六图 | [build-test-result.json](build-test-result.json) |
| 目标映射源码复核 | 小范围完成，无已确认问题 | [target-source-review.json](target-source-review.json) |

判定构建W0/E0；Runtime测试构建保持一条既有夹具CS0649/E0。该冻结图同时包含此前初始化确认入口修正，但修复复核中断仍独立记录，不能用这次测试结果补签整个生命周期审查。

历史[几何RED](geometry-red-judgements.log)为215/216，修正raw来源后[GREEN](geometry-green-judgements.log)为216/216；新增目标[RED](target-red.log)明确缺RuntimeTimeTargets/DTO，之后才执行220/220。失败历史保留。

## 后续

实际NoteInformation.realTime／Hold类型holdEndRealTime的显式写入、raw bit读回和条件恢复合同见[next-native-time-writer](next-native-time-writer.md)。当前新NoteTime测试任务已启动，尚未形成验证结果；未来测试／源码不属于本批220结果。

还需missingruntime创建、target canvas／line／ring、renderer延后consumer/source、Hold engine退出及完整时钟事务。未安装或启动游戏，未改代理／模型／账号／旧ZIP，未提交推送。B01–B19全项目目标持续，所有微型复核范围与未完成项照实保留。
