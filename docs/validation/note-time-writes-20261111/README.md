# 实际音符时间缓存写入：2026-11-11

**本批离线显式写入／读回／条件恢复已验收；不开放完整 Seek，不证明 Unity 实机或 B01–B19 整体完成。**

`RunValue.PrepareSeekNoteTime` 接受完整实际音符的 `SeekRuntimeTimeTarget`。每个实际实例恰好一次、按原actual顺序写game-seconds的realTime，只有raw type2 Hold写holdEndRealTime；普通音符尾cache不写。缺失chart实例保持报告，不创造假引用。参数为基础ISeekChartNativeProfile，TargetProfile也可用。

## 实际结果

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 六组件正常ProjectReference构建 | 退出0、0警告/0错误 | [1-six.log](1-six.log) |
| 新判定及NoteTime回归 | 236/236，新增16组 | [4-judgements.log](4-judgements.log) |
| Runtime ABI回归 | 51/51 | [5-renderer-abi.log](5-renderer-abi.log) |
| 输入与依赖 | 706项前后不变，16份DLL/PDB副本匹配六图；当前输入也匹配 | [build-test-result.json](build-test-result.json)、[acceptance.json](acceptance.json) |
| 独立源码复核 | 所列范围完成，无必要修正 | [source-review.json](source-review.json) |

判定构建W0/E0；Runtime测试构建一条既有夹具CS0649/E0。源审没有执行测试，真实命令/退出码/哈希独立保留；之后的代理API错误没有新增审查结果，也不补签额外范围。

准备只读、独立持有完整NoteView/ready-paused/run。目标单次、有界、完整枚举，拒绝foreign/duplicate/null/short/extra，即使Count或indexer掩盖额外项也拒绝。ordinary尾必须null，Hold尾finite。原NaN payload、-0与finite目标按Single原始位处理，正逆setter都使用精确mutable字段/类型/偏移及expectedCurrent。

## 生命周期与故障边界

- 完整图阶段检查与selected-slot读写分离，不每标量重扫全图。最终owner地址解析后到managed guard及setter之间不加getter，不创建多余scalar root。
- 正逆pending槽记录已进入和实际before/after状态；只恢复本次合法前/后值，不覆写观察过的第三态或未尝试外改，unknown保持sticky。每次Confirm/ConfirmRestored在Operation入口前先撤旧成功。
- 逻辑撤销立即阻止后续操作；完整NoteView使用独立physical-owner lease保根，直到责任可释放。owner-thread/cap256/第257拒绝、logical关闭不free、最后lease与doubleDispose单次free均实际回归。
- callback撤销、partial native、unknown-free均保留精确公共owner。失败free/once-only inverse不自动重试；known根继续清理，unknown句柄/责任保留。

[真实RED](historical-red-build.log)为缺失participant；[初轮231/236](historical-231-of-236.log)暴露逻辑撤销提前free完整view根的问题，随后新增物理租约而未弱化断言。新增lease测试[局部命名冲突](historical-lease-test-shadowing.log)也保留，修后才接受本批绿色。

## 后续仍缺

目标canvas/line/ring basis、missingruntime创建、renderer延后consumer/source、Hold engine退出、完整时钟/事务与实际游戏准入仍缺。本批不操作flags/count/Transform/renderer/输入/协程，也不把game-seconds cache当成raw chart几何坐标。

未安装/启动游戏，未改代理/模型/账号/旧ZIP，未提交推送。source/layout sampling不是原子CAS，不排除外部ABA。源码变化须新冻结图验收；不能用本批或历史ZIP代验后续功能。
